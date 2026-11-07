using ImGuiNET;
using SkywardRenderingSharp;
using SkywardRenderingSharp.Animation;
using SkywardRenderingSharp.Lighting;
using SkywardRenderingSharp.Resources;

namespace SkywardViewer;

/// <summary>Animation and rendering controls: clip, expression and gaze pickers with a timeline, and the post chain, lighting and culling switches.</summary>
sealed class SettingsPanel
{
    readonly SkywardRenderer _renderer;
    readonly SkywardRom _rom;
    string[] _paletteNames = [];
    string _palette = "";
    StagePalette? _loaded;
    int _slot = 6, _fogLayer, _blendSlot = -1;
    float _blend;
    bool _fog = true, _dirty;
    float _intensity = 0.4f;
    bool _raw = true;
    bool _dof;

    /// <summary>The palette slot in effect, for tinting a stage's sky; null with no palette selected.</summary>
    public PaletteSlot? Slot { get; private set; }

    public SettingsPanel(SkywardRenderer renderer, SkywardRom rom) { _renderer = renderer; _rom = rom; _paletteNames = rom.PaletteNames.ToArray(); }

    /// <summary>Selects the palette named like a stage, when the game ships one.</summary>
    public void SelectPaletteFor(string name)
    {
        if (!_paletteNames.Contains(name)) return;
        _palette = name; _loaded = _rom.OpenPalette(name); _dirty = true;
    }

    public void DrawPalette()
    {
        ImGui.SeparatorText("Stage palette (lights, sky, fog, paint)");
        string shown = _palette.Length == 0 ? "(none: stand-in lights)" : _palette;
        if (ImGui.BeginCombo("Palette", shown))
        {
            if (ImGui.Selectable("(none: stand-in lights)", _palette.Length == 0)) { _palette = ""; _loaded = null; Slot = null; _renderer.Settings.Fog = false; _renderer.Lights = LightRig.Characters(); }
            foreach (string name in _paletteNames) if (ImGui.Selectable(name, name == _palette)) { _palette = name; _loaded = _rom.OpenPalette(name); _dirty = true; }
            ImGui.EndCombo();
        }
        if (_loaded is null) return;
        DrawSlotPicker();
        _dirty |= ImGui.Checkbox("Fog", ref _fog);
        _dirty |= ImGui.SliderInt("Fog layer", ref _fogLayer, 0, 2);
        _dirty |= ImGui.Checkbox("Raw palette lights", ref _raw);
        if (!_raw && ImGui.SliderFloat("Light intensity##palette", ref _intensity, 0.05f, 1.5f)) _dirty = true;
        DrawBlendPicker();
        if (_dirty) { Slot = _renderer.ApplyPalette(_loaded, _slot, _fogLayer, _fog, _raw ? null : _intensity, _blendSlot >= 0 ? _blendSlot : null, _blend); _dirty = false; }
    }

    // Only the slots the stage uses are offered; the rest hold a default template. The file does not name what a slot stands for, so each is described by its data.
    void DrawSlotPicker()
    {
        var used = Enumerable.Range(0, StagePalette.Slots).Select(i => _loaded!.Slot(i)).Where(s => !s.IsTemplate).ToList();
        if (used.Count > 0 && used.All(s => s.Index != _slot)) { _slot = used[0].Index; _dirty = true; }
        var current = _loaded!.Slot(_slot);
        ImGui.ColorButton("##sky", new System.Numerics.Vector4(current.SkyTop, 1f), ImGuiColorEditFlags.NoTooltip, new System.Numerics.Vector2(18, 18));
        ImGui.SameLine();
        if (!ImGui.BeginCombo("Slot", $"{current.Index}: {current.Describe()}")) return;
        foreach (var s in used) if (ImGui.Selectable($"{s.Index}: {s.Describe()}", s.Index == _slot)) { _slot = s.Index; _dirty = true; }
        ImGui.EndCombo();
    }

    // The engine blends between two slots by a time value when the time of day or weather changes; this mixes the current slot toward another.
    void DrawBlendPicker()
    {
        var used = Enumerable.Range(0, StagePalette.Slots).Select(i => _loaded!.Slot(i)).Where(s => !s.IsTemplate).ToList();
        string shown = _blendSlot < 0 ? "(none)" : $"{_blendSlot}: {_loaded!.Slot(_blendSlot).Describe()}";
        if (ImGui.BeginCombo("Blend toward", shown))
        {
            if (ImGui.Selectable("(none)", _blendSlot < 0)) { _blendSlot = -1; _dirty = true; }
            foreach (var s in used) if (ImGui.Selectable($"{s.Index}: {s.Describe()}", s.Index == _blendSlot)) { _blendSlot = s.Index; _dirty = true; }
            ImGui.EndCombo();
        }
        if (_blendSlot >= 0) _dirty |= ImGui.SliderFloat("Blend", ref _blend, 0f, 1f);
    }

    public void DrawAnimation(Playback playback, AnimationSet? animations)
    {
        if (animations is null) { ImGui.TextDisabled("Animation needs an object (not a stage)."); return; }
        ImGui.SeparatorText("Animation");
        playback.Clip = Pick("Clip", playback.Clip, animations.ClipNames);
        playback.Face = Pick("Expression", playback.Face, animations.PatternNames);
        playback.Gaze = Pick("Gaze", playback.Gaze, animations.SrtNames);
        bool playing = playback.Playing, speedChanged;
        if (ImGui.Checkbox("Playing", ref playing)) playback.Playing = playing;
        float speed = playback.Speed;
        speedChanged = ImGui.SliderFloat("Speed", ref speed, 0f, 3f);
        if (speedChanged) playback.Speed = speed;
        float frame = playback.ClipFrame;
        if (ImGui.SliderFloat("Frame", ref frame, 0, MathF.Max(1, animations.Clip(playback.Clip)?.FrameCount ?? 1))) { playback.ClipFrame = frame; playback.Playing = false; }
    }

    public void DrawRendering()
    {
        var s = _renderer.Settings;
        ImGui.SeparatorText("Rendering");
        bool paint = s.Paint, bloom = s.Bloom, cull = s.DisableCulling;
        if (ImGui.Checkbox("Painterly pass", ref paint)) s.Paint = paint;
        if (ImGui.Checkbox("Bloom", ref bloom)) s.Bloom = bloom;
        if (ImGui.Checkbox("Depth of field", ref _dof)) { if (_dof) _renderer.UseCutsceneDepthOfField(); else s.DepthOfField = false; }
        if (_dof)
        {
            // The engine's pass ramps blur between focus - width / 2 and focus + width / 2 (Dof_FogToAlpha), and fully blurs what is nearer than the near distance (Dof_MaskNear).
            float focus = (s.DofFar.X + s.DofFar.Y) / 2, width = s.DofFar.Y - s.DofFar.X, near = s.DofNear;
            bool changed = ImGui.DragFloat("DoF focus distance", ref focus, 50f, 0f, 200000f);
            changed |= ImGui.DragFloat("DoF ramp width", ref width, 10f, 1f, 100000f);
            if (changed) s.DofFar = new System.Numerics.Vector2(focus - width / 2, focus + width / 2);
            if (ImGui.DragFloat("DoF near distance (0: off)", ref near, 10f, 0f, 100000f)) s.DofNear = near;
        }
        if (ImGui.Checkbox("Disable backface culling", ref cull)) s.DisableCulling = cull;
        float strength = s.BloomStrength;
        if (ImGui.SliderFloat("Bloom strength", ref strength, 0f, 1f)) s.BloomStrength = strength;
        float paintConstant = s.PaintConstant;
        if (ImGui.SliderFloat("Paint amount (everywhere)", ref paintConstant, 0f, 1f)) s.PaintConstant = paintConstant;
        int passes = s.PaintPasses;
        if (ImGui.SliderInt("Paint passes", ref passes, 1, 6)) s.PaintPasses = passes;
        var far = s.PaintFar;
        if (ImGui.DragFloat("Paint far distance", ref far.X, 50f, 100f, 100000f)) s.PaintFar = far;
    }

    static string Pick(string label, string current, IEnumerable<string> names)
    {
        string result = current;
        if (!ImGui.BeginCombo(label, current.Length == 0 ? "(none)" : current)) return result;
        if (ImGui.Selectable("(none)", current.Length == 0)) result = "";
        foreach (string name in names.Order()) if (ImGui.Selectable(name, name == current)) result = name;
        ImGui.EndCombo();
        return result;
    }
}

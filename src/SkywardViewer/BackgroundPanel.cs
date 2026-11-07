using System.Numerics;
using ImGuiNET;
using SkywardRenderingSharp;
using SkywardRenderingSharp.Lighting;
using SkywardRenderingSharp.Rendering;

namespace SkywardViewer;

/// <summary>Chooses what is behind the model: the stage's own sky (or nothing for an object), any sky box in the game's data, or a solid colour.</summary>
sealed class BackgroundPanel
{
    const string Default = "(stage sky, or palette colour)";
    const string Solid = "(solid colour)";

    readonly SkywardRenderer _renderer;
    readonly string[] _choices;
    readonly Dictionary<string, SkyBox> _loaded = [];
    string _choice = Default;
    Vector3 _color = new(0.62f, 0.78f, 0.92f);

    readonly PaletteSlot _fallback;

    public BackgroundPanel(SkywardRenderer renderer, SkywardRenderingSharp.Resources.SkywardRom rom)
    {
        // A sky box needs palette colours (it ships red placeholders); with no stage palette selected the Skyloft day slot stands in.
        _fallback = rom.OpenPalette("F000").Slot(6);
        _renderer = renderer; _choices = [Default, Solid, .. renderer.SkyBoxNames];
    }

    public void Draw()
    {
        ImGui.SeparatorText("Background");
        if (ImGui.BeginCombo("Sky", _choice))
        {
            foreach (string name in _choices) if (ImGui.Selectable(name, name == _choice)) _choice = name;
            ImGui.EndCombo();
        }
        if (_choice == Solid && ImGui.ColorEdit3("Colour", ref _color)) _renderer.Settings.SkyColor = _color;
    }

    /// <summary>The sky instances for this frame, or null for none.</summary>
    public IReadOnlyList<ModelInstance>? Sky(StageScene? stage, Vector3 eye, PaletteSlot? slot)
    {
        if (_choice == Solid) { _renderer.Settings.SkyColor = _color; return null; }
        if (_choice == Default) return stage?.Sky(eye, slot ?? _fallback);
        if (!_loaded.TryGetValue(_choice, out var box)) _loaded[_choice] = box = _renderer.LoadSkyBox(_choice);
        return box.Instances(eye, slot ?? _fallback);
    }
}

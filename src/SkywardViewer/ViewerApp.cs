using System.Numerics;
using ImGuiNET;
using Silk.NET.Input;
using Silk.NET.OpenGL;
using Silk.NET.OpenGL.Extensions.ImGui;
using Silk.NET.Windowing;
using SkywardRenderingSharp;
using SkywardRenderingSharp.Lighting;
using SkywardRenderingSharp.Rendering;
using SkywardRenderingSharp.Resources;

namespace SkywardViewer;

/// <summary>The viewer window: first asks for the romfs folder (remembered afterwards), then draws the renderer's output with the control panels on top.</summary>
/// <remarks>Left drag orbits, right or middle drag pans, the wheel zooms. Loading a model or stage happens on the render thread, between frames.</remarks>
sealed class ViewerApp : IDisposable
{
    readonly IWindow _window;
    readonly OrbitCamera _camera = new();
    readonly Playback _playback = new();
    readonly ViewerSettings _saved = ViewerSettings.Load();
    FolderPicker? _picker;
    SkywardRom? _rom;
    GL _gl = null!;
    IInputContext _input = null!;
    ImGuiController _imgui = null!;
    SkywardRenderer? _renderer;
    ContentPanel? _content;
    SettingsPanel? _settings;
    BackgroundPanel? _background;
    SceneObject? _object;
    StageScene? _stage;
    (string? Model, string? Stage) _pending;
    string? _openRomfs;
    string _error = "";
    Vector2 _lastMouse;

    public ViewerApp(string? romfs, string? model, string? stage)
    {
        _openRomfs = romfs is not null ? romfs : _saved.Romfs.Length > 0 ? _saved.Romfs : null;
        _pending = (model, stage);
        _window = Window.Create(WindowOptions.Default with
        {
            Size = new(1600, 900), Title = "Skyward Sword HD viewer", VSync = false,
            API = new GraphicsAPI(ContextAPI.OpenGL, ContextProfile.Core, ContextFlags.ForwardCompatible, new APIVersion(4, 3)),
        });
        _window.Load += OnLoad; _window.Render += OnRender; _window.Closing += Dispose;
    }

    public void Run() => _window.Run();

    void OnLoad()
    {
        _gl = GL.GetApi(_window); _input = _window.CreateInput();
        _imgui = new ImGuiController(_gl, _window, _input);
        foreach (var mouse in _input.Mice) { mouse.MouseMove += OnMouseMove; mouse.Scroll += OnScroll; }
    }

    void OnMouseMove(IMouse mouse, Vector2 position)
    {
        var delta = position - _lastMouse;
        _lastMouse = position;
        if (ImGui.GetIO().WantCaptureMouse) return;
        if (mouse.IsButtonPressed(MouseButton.Left)) _camera.Orbit(delta.X, delta.Y);
        else if (mouse.IsButtonPressed(MouseButton.Right) || mouse.IsButtonPressed(MouseButton.Middle)) _camera.Pan(delta.X, delta.Y);
    }

    void OnScroll(IMouse mouse, ScrollWheel wheel)
    {
        if (!ImGui.GetIO().WantCaptureMouse) _camera.Zoom(wheel.Y);
    }

    void OnRender(double seconds)
    {
        _imgui.Update((float)seconds);
        if (_openRomfs is not null) TryOpen(_openRomfs);
        if (_renderer is null) DrawPicker();
        else Frame((float)seconds);
        _imgui.Render();
    }

    void TryOpen(string romfs)
    {
        _openRomfs = null;
        try
        {
            _rom = SkywardRom.Open(romfs);
            _renderer = new SkywardRenderer(_gl, _rom);
            _content = new ContentPanel(_rom.Index, romfs);
            _settings = new SettingsPanel(_renderer, _rom);
            _background = new BackgroundPanel(_renderer, _rom);
            _saved.Romfs = romfs; _saved.Save();
            _pending = _pending == default ? ("Alink", null) : _pending;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or KeyNotFoundException)
        {
            _error = $"Could not open {romfs}: {e.Message}";
            _rom = null; _renderer = null;
        }
    }

    void DrawPicker()
    {
        _gl.ClearColor(0.1f, 0.11f, 0.13f, 1f); _gl.Clear(ClearBufferMask.ColorBufferBit);
        _picker ??= new FolderPicker(_saved.Romfs);
        ImGui.SetNextWindowPos(new Vector2(40, 40), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(640, 520), ImGuiCond.FirstUseEver);
        ImGui.Begin("Open a romfs");
        if (_error.Length > 0) ImGui.TextWrapped(_error);
        if (_picker.Draw() is { } chosen) _openRomfs = chosen;
        ImGui.End();
    }

    void Frame(float seconds)
    {
        ApplyPending();
        if (_object is not null) _playback.Advance(seconds, _object);
        var size = _window.FramebufferSize;
        if (size.X > 0 && size.Y > 0) DrawScene(size.X, size.Y);
        DrawPanels();
    }

    void DrawScene(int width, int height)
    {
        var camera = _camera.ToCamera();
        var renderer = _renderer!;
        var sky = _background!.Sky(_stage, camera.Position, _settings!.Slot);
        var world = _stage?.World(_settings!.Slot) ?? (_object is not null ? _playback.Instances(_object) : []);
        renderer.Render(camera, world, width, height, sky);
        _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, renderer.OutputFramebuffer);
        _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, 0);
        _gl.Viewport(0, 0, (uint)width, (uint)height);
        _gl.BlitFramebuffer(0, 0, width, height, 0, 0, width, height, ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    void DrawPanels()
    {
        ImGui.SetNextWindowPos(new Vector2(12, 12), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(400, 820), ImGuiCond.FirstUseEver);
        ImGui.Begin("Skyward viewer");
        var request = _content!.Draw();
        if (request != default) _pending = request;
        _settings!.DrawAnimation(_playback, _stage is null ? _object?.Animations : null);
        _settings.DrawPalette();
        _background!.Draw();
        _settings.DrawRendering();
        if (ImGui.Button("Change romfs folder")) { _renderer?.Dispose(); _renderer = null; _object = null; _stage = null; _picker = new FolderPicker(_saved.Romfs); }
        ImGui.End();
    }

    void ApplyPending()
    {
        var (model, stage) = _pending;
        _pending = default;
        try
        {
            if (model is not null) LoadObject(model);
            else if (stage is not null) LoadStage(stage);
        }
        catch (Exception e) when (e is KeyNotFoundException or FileNotFoundException or InvalidDataException)
        {
            _content!.Status = e.Message;
        }
    }

    void LoadObject(string id)
    {
        var loaded = _renderer!.LoadObject(id);
        _stage = null; _object = loaded; _playback.Reset();
        _camera.Frame(loaded.Main.BoundsMin, loaded.Main.BoundsMax);
        _renderer.Lights = LightRig.Characters();
        _content!.Status = $"Loaded {id}: {loaded.Models.Count} model(s).";
    }

    void LoadStage(string id)
    {
        var loaded = _renderer!.LoadStage(id);
        var (min, max) = loaded.Bounds();
        _object = null; _stage = loaded; _playback.Reset();
        _camera.Frame(min, max);
        _renderer.Lights = loaded.Lights;
        _settings!.SelectPaletteFor(id);
        _content!.Status = $"Loaded stage {id}, room {loaded.Room}.";
    }

    public void Dispose()
    {
        _imgui?.Dispose(); _input?.Dispose(); _renderer?.Dispose(); _gl?.Dispose();
    }
}

using ImGuiNET;
using SkywardRenderingSharp.Content;

namespace SkywardViewer;

/// <summary>The "what to look at" panel: search every model in the romfs by name, or pick a stage.</summary>
sealed class ContentPanel
{
    const int MaxResults = 60;

    readonly ContentIndex _index;
    readonly string[] _stages;
    string _search = "Alink";
    string[] _results = [];
    int _stage;

    public ContentPanel(ContentIndex index, string romfs)
    {
        _index = index;
        _stages = Directory.GetDirectories(Path.Combine(romfs, "Stage")).Select(Path.GetFileName).Where(n => n is not null).Select(n => n!).Order().ToArray();
        Refresh();
    }

    public string Status { get; set; } = "";

    /// <summary>Draws the panel. Returns what the user asked to load, if anything.</summary>
    public (string? Model, string? Stage) Draw()
    {
        string? model = null, stage = null;
        ImGui.SeparatorText("Model or object");
        if (ImGui.InputText("Search", ref _search, 64)) Refresh();
        if (ImGui.BeginListBox("##models", new System.Numerics.Vector2(-1, 150)))
        {
            foreach (string name in _results) if (ImGui.Selectable(name)) model = name;
            ImGui.EndListBox();
        }
        if (ImGui.Button("Load typed ID")) model = _search;
        ImGui.SeparatorText("Stage");
        ImGui.Combo("##stage", ref _stage, _stages, _stages.Length);
        ImGui.SameLine();
        if (ImGui.Button("Load stage")) stage = _stages[_stage];
        if (Status.Length > 0) ImGui.TextWrapped(Status);
        return (model, stage);
    }

    void Refresh() => _results = _search.Length < 2 ? [] : _index.Search(_search).Select(l => l.Model).Distinct().Take(MaxResults).ToArray();
}

using ImGuiNET;

namespace SkywardViewer;

/// <summary>An ImGui folder browser for choosing the extracted romfs: click folders to descend, type a path, and accept a folder that holds Stage and Object.</summary>
sealed class FolderPicker
{
    string _path;
    string _typed;
    string _error = "";

    public FolderPicker(string start)
    {
        _path = Directory.Exists(start) ? start : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        _typed = _path;
    }

    static bool IsRomfs(string path) => Directory.Exists(Path.Combine(path, "Stage")) && Directory.Exists(Path.Combine(path, "Object"));

    /// <summary>Draws the browser. Returns the chosen romfs folder once the user accepts one.</summary>
    public string? Draw()
    {
        string? chosen = null;
        ImGui.TextWrapped("Choose the extracted Skyward Sword HD romfs folder (it contains Stage and Object).");
        if (ImGui.InputText("Path", ref _typed, 512, ImGuiInputTextFlags.EnterReturnsTrue)) Go(_typed);
        ImGui.Separator();
        if (ImGui.BeginListBox("##folders", new System.Numerics.Vector2(-1, 320)))
        {
            if (ImGui.Selectable("..")) Go(Path.GetDirectoryName(_path) ?? "");
            foreach (string folder in Children()) if (ImGui.Selectable(Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar)) is { Length: > 0 } n ? n : folder)) Go(folder);
            ImGui.EndListBox();
        }
        bool valid = IsRomfs(_path);
        ImGui.BeginDisabled(!valid);
        if (ImGui.Button("Use this folder")) chosen = _path;
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.TextDisabled(valid ? "Looks like a romfs." : "Not a romfs (needs Stage and Object folders).");
        if (_error.Length > 0) ImGui.TextWrapped(_error);
        return chosen;
    }

    void Go(string path)
    {
        _error = "";
        if (path.Length == 0) { _path = ""; _typed = ""; return; }
        if (!Directory.Exists(path)) { _error = $"No folder {path}"; return; }
        _path = Path.GetFullPath(path); _typed = _path;
    }

    // At the top level the drives are listed; elsewhere the sub folders that can be read.
    IEnumerable<string> Children()
    {
        if (_path.Length == 0) return DriveInfo.GetDrives().Select(d => d.Name);
        try { return Directory.GetDirectories(_path).Order().ToList(); }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException) { return []; }
    }
}

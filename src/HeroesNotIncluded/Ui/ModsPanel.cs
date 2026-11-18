using Brutal.ImGuiApi;
using HeroesNotIncluded.Mods;
using Narsil;
using Narsil.Ui;

namespace HeroesNotIncluded.Ui;

/// <summary>
/// The title screen's Mods (docs/MODDING.md): every mod installed, turned on or off, then applied.
/// Applying reloads the content, art and sounds, so it's tried first: a mod set whose defs won't load
/// is refused with the reason, and the game stays as it was. Code isn't sandboxed, so a mod that
/// ships assemblies says so before it's turned on.
/// </summary>
public sealed class ModsPanel
{
    public enum Result { None, Back, Applied }

    private readonly HashSet<string> _on;
    private string? _error;

    public ModsPanel()
    {
        GameContent.Rediscover();
        _on = new HashSet<string>(GameContent.Current.Mods.Ids, StringComparer.Ordinal);
    }

    public Result Draw(GameHost host, float4 dim)
    {
        var catalog = GameContent.Catalog;
        ImGui.TextColored(dim, "Mods"u8);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + Gui.S(460));
        if (catalog.All.Count == 0)
            ImGuiText.Wrapped("No mods installed. A mod is a folder with a mod.json; put one in the mods folder below.");
        foreach (var mod in catalog.All)
        {
            ImGui.PushID(mod.Id);
            var on = _on.Contains(mod.Id);
            if (ImGui.Checkbox($"{mod.Name}  {mod.Version}", ref on))
            {
                if (on)
                    _on.Add(mod.Id);
                else
                    _on.Remove(mod.Id);
                _error = null;
            }
            if (mod.Description.Length > 0)
                ImGui.TextColored(dim, ImGuiText.Escape(mod.Description));
            if (mod.Authors.Count > 0)
                ImGui.TextColored(dim, ImGuiText.Escape($"by {string.Join(", ", mod.Authors)}"));
            if (mod.DependsOn.Count > 0)
                ImGui.TextColored(dim, ImGuiText.Escape($"Needs {string.Join(", ", mod.DependsOn)}"));
            if (mod.RunsCode)
                ImGui.TextColored(Theme.Warn, "Runs code: only turn it on if you trust who made it."u8);
            ImGui.PopID();
        }
        foreach (var p in catalog.Problems)
            ImGui.TextColored(Theme.Warn, ImGuiText.Escape(p));
        foreach (var p in catalog.Resolve(_on).Problems)
            ImGui.TextColored(Theme.Warn, ImGuiText.Escape(p));
        if (_error != null)
            ImGui.TextColored(Theme.Bad, ImGuiText.Escape(_error));
        ImGui.TextColored(dim, ImGuiText.Escape(GameContent.UserModsDirectory));
        ImGui.PopTextWrapPos();

        float2? size = new float2(Gui.S(200), 0);
        var changed = !_on.SetEquals(GameContent.Current.Mods.Ids);
        ImGui.BeginDisabled(!changed);
        var apply = ImGui.Button("Apply"u8, in size);
        ImGui.EndDisabled();
        if (ImGui.Button("Open mods folder"u8, in size))
        {
            Directory.CreateDirectory(GameContent.UserModsDirectory);
            _error = SettingsWindow.OpenFolder(GameContent.UserModsDirectory);
        }
        if (ImGui.Button("Back"u8, in size))
            return Result.Back;
        return apply && Apply(host) ? Result.Applied : Result.None;
    }

    private bool Apply(GameHost host)
    {
        var previous = GameContent.Current;
        var content = GameContent.Use(_on);
        try
        {
            content.LoadDefs(host.DataDirectory);
        }
        catch (Exception ex)
        {
            Log.Engine.Warning($"Mods not applied, their defs didn't load: {ex}");
            _error = $"Not applied: {ex.Message}";
            GameContent.Use(previous.Mods.Ids);
            return false;
        }
        content.ApplyTo(host);
        Settings.Current.Mods = [.. content.Mods.Ids];
        Settings.Save();
        return true;
    }
}

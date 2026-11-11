using Narsil;
using Narsil.Platform;

namespace SupportingCast.Rendering;

/// <summary>
/// Notices def edits: F8, or (Debug builds, <c>SC_WATCH=1</c>) any <c>*.json</c> change under the
/// defs directory, after a short quiet spell so an editor's save-and-rename counts once.
/// <see cref="VillageGame"/> does the actual reload (a save round trip onto the new defs).
/// </summary>
public sealed class DefReloader : IDisposable
{
    /// <summary>Seconds without further changes before a reload.</summary>
    public const float QuietSeconds = 0.3f;

    private readonly FileSystemWatcher? _watcher;
    private volatile bool _dirty;
    private float _quiet;

    public string Directory { get; }

    public DefReloader(string directory, bool watch)
    {
        Directory = directory;
        if (!watch || !System.IO.Directory.Exists(directory))
            return;
        _watcher = new FileSystemWatcher(directory, "*.json") { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.Renamed += OnChanged;
        _watcher.Deleted += OnChanged;
        _watcher.EnableRaisingEvents = true;
        Log.Engine.Info($"Watching {directory} for def changes (F8 reloads by hand)");
    }

    private void OnChanged(object sender, FileSystemEventArgs e) => MarkChanged();

    /// <summary>A def file changed (the watcher calls this; tests may too).</summary>
    public void MarkChanged()
    {
        _dirty = true;
        _quiet = 0;
    }

    /// <summary>Call once per frame. True when the defs should be reloaded now.</summary>
    public bool Update(Input input, float dt) => Update(input.WasKeyPressed(GlfwKey.F8), dt);

    public bool Update(bool forced, float dt)
    {
        if (forced)
        {
            _dirty = false;
            return true;
        }
        if (!_dirty)
            return false;
        _quiet += dt;
        if (_quiet < QuietSeconds)
            return false;
        _dirty = false;
        return true;
    }

    public void Dispose() => _watcher?.Dispose();
}

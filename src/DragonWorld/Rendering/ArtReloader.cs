using Narsil;
using Narsil.Platform;
using Narsil.Rendering;

namespace DragonWorld.Rendering;

/// <summary>
/// Hot-reloads the texture atlas on F5, and (in Debug builds, or with DW_WATCH=1) whenever a PNG
/// or <c>.anim.json</c> under the textures directory changes. Point <c>tools/export_art.py --watch
/// --also-copy-to</c> at the build output's <c>data/textures</c> and saving in Aseprite updates the game.
/// </summary>
public sealed class ArtReloader : IDisposable
{
    private readonly TextureAtlas _atlas;
    private readonly FileSystemWatcher? _watcher;
    private volatile bool _dirty;
    private float _quiet;

    public ArtReloader(TextureAtlas atlas)
    {
        _atlas = atlas;
        if (!DevPaths.Watch("DW_WATCH_ART") || !Directory.Exists(atlas.Directory))
            return;
        _watcher = new FileSystemWatcher(atlas.Directory) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size };
        _watcher.Changed += OnChanged;
        _watcher.Created += OnChanged;
        _watcher.Renamed += OnChanged;
        _watcher.EnableRaisingEvents = true;
        Log.Render.Info($"Watching {atlas.Directory} for art changes (F5 reloads by hand)");
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        if (e.FullPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || e.FullPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            _dirty = true;
            _quiet = 0; // wait for a quiet spell after the last file, not the first
        }
    }

    /// <summary>Call once per frame. Returns true if the atlas was reloaded.</summary>
    public bool Update(Input input, float dt)
    {
        if (input.WasKeyPressed(GlfwKey.F5))
            return _atlas.Reload();
        if (!_dirty)
            return false;
        // Exports write several files; wait for a quiet moment.
        _quiet += dt;
        if (_quiet < 0.3f)
            return false;
        _dirty = false;
        _quiet = 0;
        return _atlas.Reload();
    }

    public void Dispose() => _watcher?.Dispose();
}

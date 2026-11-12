namespace HeroesNotIncluded;

/// <summary>
/// The source checkout's data folders when the game runs from one (dotnet run, tests). The game
/// normally reads the copies in the build output, so live reloads (defs, shaders) read these
/// instead; edits then show up without a rebuild. All null in a shipped build.
/// </summary>
public static class DevPaths
{
    private static readonly Lazy<string?> Root = new(() => FindRoot(AppContext.BaseDirectory));

    /// <summary>The folder holding HeroesNotIncluded.sln above <paramref name="start"/>, or null.</summary>
    public static string? FindRoot(string start)
    {
        for (var dir = new DirectoryInfo(start); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "HeroesNotIncluded.sln")))
                return dir.FullName;
        return null;
    }

    public static string? Defs => Existing(Root.Value, "data", "defs");

    /// <summary>The quad renderer's shaders, in Narsil's voxel module (copied into the output's data/shaders).</summary>
    public static string? VoxelShaders => Existing(Root.Value, "external", "narsil", "src", "Narsil.Voxel", "data", "shaders");

    /// <summary>Narsil's own shaders (sprites, meshes, post), which the build copies next to the voxel ones.</summary>
    public static string? EngineShaders => Existing(Root.Value, "external", "narsil", "src", "Narsil", "data", "shaders");

    private static string? Existing(string? root, params string[] parts)
    {
        if (root == null)
            return null;
        var path = Path.Combine([root, .. parts]);
        return Directory.Exists(path) ? path : null;
    }

    /// <summary>File watching for live reloads: Debug builds, or <c>SC_WATCH=1</c> (<c>SC_WATCH_ART=1</c> still works for art).</summary>
    public static bool Watch(string? legacyVariable = null)
    {
#if DEBUG
        return true;
#else
        return Environment.GetEnvironmentVariable("SC_WATCH") == "1"
            || (legacyVariable != null && Environment.GetEnvironmentVariable(legacyVariable) == "1");
#endif
    }
}

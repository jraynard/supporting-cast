using System.Text.Json;
using System.Text.Json.Serialization;

namespace HeroesNotIncluded.Platforms;

/// <summary>
/// Whether this copy of the game is Steam's, and which app it is. It's read from a
/// <c>steam.json</c> beside the game that only Steam packages carry (<c>tools/steam_package.sh</c>),
/// not compiled in: co-op's build tag hashes the game's assemblies, so an itch build and a Steam
/// build of the same version stay the same bytes and can play together. <c>--steam N</c> tries a
/// checkout against an app (480, Valve's Spacewar, before ours exists) without restarting through
/// Steam.
/// </summary>
public sealed record SteamConfig(uint AppId, uint StoreAppId, bool Dev = false)
{
    public const string FileName = "steam.json";

    /// <summary>The store page to wishlist: the full game's, also from the demo.</summary>
    public string StoreUrl => $"https://store.steampowered.com/app/{StoreAppId}/";

    private sealed class File
    {
        [JsonPropertyName("appId")] public uint AppId { get; set; }
        [JsonPropertyName("storeAppId")] public uint StoreAppId { get; set; }
    }

    /// <summary>A <c>steam.json</c>'s text, or null if it isn't one (no app ID, or not JSON).</summary>
    public static SteamConfig? Parse(string json)
    {
        try
        {
            var file = JsonSerializer.Deserialize<File>(json);
            return file is { AppId: > 0 } ? new SteamConfig(file.AppId, file.StoreAppId > 0 ? file.StoreAppId : file.AppId) : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary><c>--steam N</c> (a dev run), else the <c>steam.json</c> beside the game (<see cref="Places"/>), else null: no Steam.</summary>
    public static SteamConfig? Find(string[] args, string directory)
    {
        var at = Array.IndexOf(args, "--steam");
        if (at >= 0)
            return at + 1 < args.Length && uint.TryParse(args[at + 1], out var id) && id > 0 ? new SteamConfig(id, id, Dev: true) : null;
        foreach (var dir in Places(directory))
        {
            var path = Path.Combine(dir, FileName);
            try
            {
                if (System.IO.File.Exists(path))
                    return Parse(System.IO.File.ReadAllText(path));
            }
            catch (IOException)
            {
                return null;
            }
        }
        return null;
    }

    /// <summary>
    /// Where a <c>steam.json</c> may be: beside the game's files, and for a Mac app the folder
    /// holding the <c>.app</c>. Anything added inside a signed bundle breaks its signature, so a
    /// Mac depot keeps the file beside the bundle (<c>tools/steam_package.sh</c>).
    /// </summary>
    public static IEnumerable<string> Places(string directory)
    {
        yield return directory;
        var dir = new DirectoryInfo(directory);
        if (dir is { Name: "MacOS", Parent: { Name: "Contents", Parent: { } app } } && app.Name.EndsWith(".app", StringComparison.Ordinal) && app.Parent is { } holder)
            yield return holder.FullName;
    }
}

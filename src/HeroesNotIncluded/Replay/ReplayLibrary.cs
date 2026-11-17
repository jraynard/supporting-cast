using HeroesNotIncluded.Sim.Replay;

namespace HeroesNotIncluded.Replay;

/// <summary>
/// Where replays are kept (<c>replays/</c> in the config folder), the newest few: they're a few
/// hundred kilobytes each, written at a round's reveal.
/// </summary>
public static class ReplayLibrary
{
    /// <summary>How many replays to keep before the oldest go.</summary>
    public const int Keep = 20;

    public static string Directory => Path.Combine(Narsil.GameHost.DefaultConfigDirectory(TitleGame.DataFolder), "replays");

    public sealed record Entry(string Path, ReplayFile.Header Header, DateTime Written);

    /// <summary>Write a replay and prune the oldest; the path it went to, or null (with the log told) if it couldn't.</summary>
    public static string? Save(ReplayFile replay, string name)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var path = Path.Combine(Directory, name + ReplayFile.Extension);
            using (var stream = File.Create(path))
                replay.Write(stream);
            foreach (var old in List().Skip(Keep))
                File.Delete(old.Path);
            return path;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Narsil.Log.Engine.Warning($"replay: couldn't keep it: {e.Message}");
            return null;
        }
    }

    /// <summary>The replays kept, newest first; unreadable ones left out.</summary>
    public static List<Entry> List()
    {
        var list = new List<Entry>();
        if (!System.IO.Directory.Exists(Directory))
            return list;
        foreach (var path in System.IO.Directory.GetFiles(Directory, "*" + ReplayFile.Extension))
        {
            try
            {
                using var stream = File.OpenRead(path);
                list.Add(new Entry(path, ReplayFile.ReadHeader(stream), File.GetLastWriteTime(path)));
            }
            catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                // not one of ours, or damaged: not listed
            }
        }
        return [.. list.OrderByDescending(e => e.Written)];
    }

    /// <summary>Read a whole replay; <see cref="InvalidDataException"/> or an IO error if it can't be.</summary>
    public static ReplayFile Load(string path)
    {
        using var stream = File.OpenRead(path);
        return ReplayFile.Read(stream);
    }
}

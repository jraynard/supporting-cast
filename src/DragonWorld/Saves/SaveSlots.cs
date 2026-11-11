using System.Globalization;
using Narsil;
using DragonWorld.Sim;
using DragonWorld.Sim.Defs;
using DragonWorld.Sim.Saving;

namespace DragonWorld;

/// <summary>A save file on disk and what its header says (or why it can't be read).</summary>
public sealed record SaveSlot(string Name, string Path, DateTime Modified, SaveHeader? Header, string? Error)
{
    public bool IsAutosave => Name.StartsWith(SaveSlots.AutosavePrefix, StringComparison.Ordinal);

    /// <summary>"Day 4, 06:00 · 3 villagers, Ember" for menus.</summary>
    public string Summary
    {
        get
        {
            if (Header is not { } h)
                return Error ?? "unreadable";
            var people = h.Villagers == 1 ? "1 villager" : $"{h.Villagers} villagers";
            return h.Dragons.Count > 0 ? $"{h.Time} · {people}, {string.Join(", ", h.Dragons)}" : $"{h.Time} · {people}";
        }
    }
}

/// <summary>A loaded save, ready for <see cref="VillageGame.FromSave"/>.</summary>
public sealed record LoadedGame(Simulation Sim, SaveHeader Header, string SlotName);

/// <summary>
/// Save files in the user config directory (<c>saves/*.dwsave</c>): named saves, the quicksave and
/// three rotating autosaves. Writes go to a temp file first and are moved into place, so a crash
/// mid-save never leaves a broken file where a good one was.
/// </summary>
public static class SaveSlots
{
    public const string Extension = ".dwsave";
    public const string Quicksave = "quicksave";
    public const string AutosavePrefix = "autosave-";
    public const int AutosaveCount = 3;

    /// <summary>Where saves live; null (tests, tools) disables saving and lists nothing.</summary>
    public static string? Directory { get; set; }

    public static string DefaultDirectory => System.IO.Path.Combine(GameHost.DefaultConfigDirectory(TitleGame.DataFolder), "saves");

    public static string PathOf(string name) =>
        System.IO.Path.Combine(Directory ?? throw new InvalidOperationException("Saving is disabled"), name + Extension);

    /// <summary>Every save, newest first. Unreadable files are listed with their error rather than hidden.</summary>
    public static IReadOnlyList<SaveSlot> List()
    {
        if (Directory == null || !System.IO.Directory.Exists(Directory))
            return [];
        var slots = new List<SaveSlot>();
        foreach (var path in System.IO.Directory.EnumerateFiles(Directory, "*" + Extension))
        {
            var name = System.IO.Path.GetFileNameWithoutExtension(path);
            var modified = File.GetLastWriteTimeUtc(path);
            try
            {
                using var stream = File.OpenRead(path);
                slots.Add(new SaveSlot(name, path, modified, SaveGame.ReadHeader(stream), null));
            }
            catch (Exception ex) when (ex is SaveException or IOException or UnauthorizedAccessException)
            {
                slots.Add(new SaveSlot(name, path, modified, null, ex.Message));
            }
        }
        return slots.OrderByDescending(s => s.Modified).ThenBy(s => s.Name, StringComparer.Ordinal).ToList();
    }

    /// <summary>The most recent readable save (the title's Continue), or null.</summary>
    public static SaveSlot? Newest() => List().FirstOrDefault(s => s.Header != null);

    /// <summary>The autosave slot to write next: an unused one, else the oldest.</summary>
    public static string NextAutosave()
    {
        var names = Enumerable.Range(1, AutosaveCount).Select(i => AutosavePrefix + i).ToList();
        if (Directory == null)
            return names[0];
        return names.FirstOrDefault(n => !File.Exists(PathOf(n)))
            ?? names.MinBy(n => File.GetLastWriteTimeUtc(PathOf(n)))!;
    }

    /// <summary>A file-system-safe slot name from what the player typed; empty if nothing usable is left.</summary>
    public static string Clean(string name)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars();
        var clean = new string(name.Trim().Select(c => invalid.Contains(c) || char.IsControl(c) ? '_' : c).ToArray());
        return clean.Trim('.', ' ');
    }

    /// <summary>Save the game into a slot. Returns null on success, else why it failed (shown to the player).</summary>
    public static string? Write(string name, Simulation sim, IReadOnlyDictionary<string, string> meta)
    {
        if (Directory == null)
            return "Saving is disabled.";
        if (SaveGame.CannotSave(sim) is { } reason)
            return reason;
        var path = PathOf(name);
        var temp = path + ".tmp";
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var bytes = SaveGame.Write(sim, meta);
            File.WriteAllBytes(temp, bytes);
            File.Move(temp, path, overwrite: true);
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SaveException)
        {
            Log.Engine.Warning($"Couldn't save to {path}: {ex.Message}");
            try { File.Delete(temp); } catch (IOException) { }
            return $"Couldn't save: {ex.Message}";
        }
    }

    /// <summary>
    /// Save into a slot with only the serialization on this thread: compressing and writing the
    /// file happen on a worker, so an autosave doesn't stall the frame. Returns why it couldn't
    /// start, or the task that finishes it (its result: why the write failed, or null).
    /// </summary>
    public static (string? Error, Task<string?>? Done) WriteInBackground(string name, Simulation sim, IReadOnlyDictionary<string, string> meta)
    {
        if (Directory == null)
            return ("Saving is disabled.", null);
        if (SaveGame.CannotSave(sim) is { } reason)
            return (reason, null);
        SaveGame.Image image;
        try
        {
            image = SaveGame.Capture(sim, meta);
        }
        catch (SaveException ex)
        {
            return ($"Couldn't save: {ex.Message}", null);
        }
        var path = PathOf(name);
        var dir = Directory;
        return (null, Task.Run(() =>
        {
            var temp = path + ".tmp";
            try
            {
                System.IO.Directory.CreateDirectory(dir);
                File.WriteAllBytes(temp, image.Encode());
                File.Move(temp, path, overwrite: true);
                return (string?)null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Engine.Warning($"Couldn't save to {path}: {ex.Message}");
                try { File.Delete(temp); } catch (IOException) { }
                return $"Couldn't save: {ex.Message}";
            }
        }));
    }

    /// <summary>Load a save. Throws <see cref="SaveException"/> with a player-readable message on failure.</summary>
    public static LoadedGame Load(string path, DefDatabase defs)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var (sim, header) = SaveGame.Read(stream, defs);
            return new LoadedGame(sim, header, System.IO.Path.GetFileNameWithoutExtension(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new SaveException($"Couldn't read {System.IO.Path.GetFileName(path)}: {ex.Message}", ex);
        }
    }

    public static void Delete(SaveSlot slot)
    {
        try
        {
            File.Delete(slot.Path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.Engine.Warning($"Couldn't delete {slot.Path}: {ex.Message}");
        }
    }

    /// <summary>"3 min ago", "yesterday" for menus.</summary>
    public static string Ago(DateTime utc, DateTime nowUtc)
    {
        var age = nowUtc - utc;
        if (age.TotalMinutes < 1) return "just now";
        if (age.TotalHours < 1) return $"{(int)age.TotalMinutes} min ago";
        if (age.TotalDays < 1) return $"{(int)age.TotalHours} h ago";
        return utc.ToLocalTime().ToString("d MMM, HH:mm", CultureInfo.InvariantCulture);
    }
}

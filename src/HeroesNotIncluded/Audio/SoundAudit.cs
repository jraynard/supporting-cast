using Narsil.Audio;

namespace HeroesNotIncluded.Audio;

/// <summary>
/// <c>--check-art</c>'s sound half: every id and mood the game names is in the manifest, every file
/// it lists is there and decodes. A sound that's missing in game is only a warning in the log, so
/// this is where it gets caught.
/// </summary>
public static class SoundAudit
{
    public sealed record Report(List<string> Errors, List<string> Warnings, int Files);

    public static Report Run(string manifestPath)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        SoundManifest manifest;
        try
        {
            manifest = SoundManifest.Load(manifestPath);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            errors.Add($"sounds: {ex.Message}");
            return new Report(errors, warnings, 0);
        }
        // A playlist with nothing there yet is a warning: our songs come later, and the free ones
        // are fetched (tools/fetch_music.py), not kept in git.
        foreach (var m in manifest.MissingFiles)
            if (manifest.Music.ContainsKey(m[..Math.Max(0, m.IndexOf(':'))]))
                warnings.Add($"music {m}");
            else
                errors.Add($"sound {m}");
        foreach (var id in Sounds.All)
            if (!manifest.Sounds.TryGetValue(id, out var def))
                errors.Add($"sound '{id}' is named by the game but not in sounds.json");
            else if (def.Files.Count == 0)
                errors.Add($"sound '{id}' has no files");
        foreach (var id in manifest.Sounds.Keys.Except(Sounds.All))
            warnings.Add($"sound '{id}' is in sounds.json but the game never plays it");
        foreach (var mood in Moods.All)
            if (!manifest.Music.TryGetValue(mood, out var list))
                errors.Add($"music '{mood}' is missing from sounds.json");
            else if (list.Files.Count == 0)
                warnings.Add($"music '{mood}' has no tracks (run tools/fetch_music.py for the free ones)");
        var files = 0;
        foreach (var file in manifest.AllFiles.Where(File.Exists))
        {
            files++;
            try
            {
                if (manifest.Music.Values.Any(m => m.Files.Contains(file)) || manifest.Sounds.Values.Any(s => s.Stream && s.Files.Contains(file)))
                {
                    using var stream = IPcmStream.Open(file); // music is checked by opening, not decoding all of it
                    if (stream.Read(new short[4096]) == 0)
                        errors.Add($"sound {file} is empty");
                }
                else if (SoundData.Load(file).Frames == 0)
                    errors.Add($"sound {file} is empty");
            }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException or IOException)
            {
                errors.Add($"sound {ex.Message}");
            }
        }
        return new Report(errors, warnings, files);
    }
}

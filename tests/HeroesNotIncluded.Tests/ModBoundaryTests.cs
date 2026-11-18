using System.Text.RegularExpressions;

namespace HeroesNotIncluded.Tests;

/// <summary>
/// The game is the first mod's host, not its owner (docs/MODDING.md): it never names the carpenter,
/// whose views and sounds come in through <see cref="Mods.GameRegistry"/>. This reads the source.
/// </summary>
public class ModBoundaryTests
{
    [Fact]
    public void TheGameNeverNamesTheCarpenter()
    {
        var src = Path.GetFullPath(Path.Combine(DevPaths.Defs!, "..", "..", "src", "HeroesNotIncluded"));
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(src, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(src, file).Replace('\\', '/');
            if (rel.StartsWith("bin/", StringComparison.Ordinal) || rel.StartsWith("obj/", StringComparison.Ordinal))
                continue;
            var n = 0;
            foreach (var line in File.ReadLines(file))
            {
                n++;
                if (Regex.IsMatch(line.Split("//")[0], @"\bCarpentry\b"))
                    offenders.Add($"{rel}:{n}: {line.Trim()}");
            }
        }
        Assert.True(offenders.Count == 0, "The game names the carpenter (use a hook in Mods/GameRegistry.cs):\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void TheModKitBuildsAgainstTheSimsOwnBannedList()
    {
        // A mod's sim half is held to the game's determinism rules: the template's copy of the list must be the sim's.
        var root = Path.GetFullPath(Path.Combine(DevPaths.Defs!, "..", ".."));
        Assert.Equal(File.ReadAllText(Path.Combine(root, "src", "HeroesNotIncluded.Sim", "BannedSymbols.txt")),
            File.ReadAllText(Path.Combine(root, "modkit", "templates", "hni-trade", "src", "BannedSymbols.txt")));
    }
}

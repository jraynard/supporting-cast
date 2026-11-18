using System.Text.RegularExpressions;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// The core is the first mod (docs/MODDING.md): it reaches a trade only through the hooks a mod would
/// use, so a trade can be moved out, or left out, without the core noticing. These read the source.
/// </summary>
public class ModBoundaryTests
{
    private static string SimSource => Path.GetFullPath(Path.Combine(TestWorld.FindDefsDirectory(), "..", "..", "src", "HeroesNotIncluded.Sim"));

    /// <summary>Files still waiting for their registry (ROADMAP, Now 8): the bot's per-trade play.</summary>
    private static readonly string[] NotYet = ["Bots/BotPlayer.cs", "Bots/BotPlayer.Play.cs", "Bots/BotPlayer.Woodshop.cs"];

    [Fact]
    public void TheCoreNeverNamesTheCarpenter()
    {
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(SimSource, "*.cs", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(SimSource, file).Replace('\\', '/');
            if (rel.StartsWith("Carpentry/", StringComparison.Ordinal) || rel.StartsWith("bin/", StringComparison.Ordinal) || rel.StartsWith("obj/", StringComparison.Ordinal) || NotYet.Contains(rel))
                continue;
            var n = 0;
            foreach (var line in File.ReadLines(file))
            {
                n++;
                var code = line.Split("//")[0];
                if (Regex.IsMatch(code, @"\bCarpentry\b"))
                    offenders.Add($"{rel}:{n}: {line.Trim()}");
            }
        }
        Assert.True(offenders.Count == 0, "The core names the carpenter (use a hook in Modding/TradeModule.cs):\n" + string.Join("\n", offenders));
    }
}

using System.Text.RegularExpressions;

namespace HeroesNotIncluded.Tests;

/// <summary>
/// Keeps the secrets rule true: the game reads other characters through <c>Perception</c>. A new
/// direct read of the creature list fails here until someone decides it's safe and says why.
/// </summary>
public partial class PerceptionBoundaryTests
{
    /// <summary>Direct reads that have been checked, per file, and why each is safe.</summary>
    private static readonly Dictionary<string, int> Allowed = new()
    {
        ["Rendering/DoorAnimator.cs"] = 1, // who's in a doorway; DoorAnimator.CanSee gates the result
        ["VillageGame.Adventure.cs"] = 1, // PickCreature, which filters by View.CanSee
        ["VillageGame.Arena.cs"] = 1, // the practice ring's foe: a dev tool with no round, two people and no secrets
        ["VillageGame.Hud.cs"] = 1, // the overseer's bar, only with no round
        ["VillageGame.Ui.cs"] = 2, // your own opinions of the cast; the Work tab, closed to players
        ["WorldRenderer.cs"] = 3, // the draw and label loops, which filter by Seen; debug paths, only with no viewer
    };

    [GeneratedRegex(@"Each<Creature>|Query<Creature>|\b_?sim\.Creatures\(\)")]
    private static partial Regex DirectRead();

    [Fact]
    public void TheGameReadsOtherCharactersOnlyThroughPerception()
    {
        var root = Path.GetFullPath(Path.Combine(DevPaths.Defs!, "..", "..", "src", "HeroesNotIncluded"));
        var found = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") && !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Select(f => (File: Path.GetRelativePath(root, f).Replace('\\', '/'), Count: DirectRead().Count(File.ReadAllText(f))))
            .Where(x => x.Count > 0)
            .ToDictionary(x => x.File, x => x.Count);

        foreach (var (file, count) in found)
            Assert.True(count <= Allowed.GetValueOrDefault(file),
                $"{file} reads the creature list directly {count} time(s), {Allowed.GetValueOrDefault(file)} allowed. Go through View (PerceptionView), or add it to Allowed and say why it's safe.");
    }
}

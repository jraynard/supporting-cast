using HeroesNotIncluded.Mods;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Director;
using Narsil.Mods;

namespace HeroesNotIncluded.Tests;

/// <summary>The Next Fest demo (docs/NEXT-FEST.md): its content as its mods make it, and what it may be started with.</summary>
[Collection(nameof(ModCodeTests))]
public class DemoTests
{
    private static string ModsDir => DevPaths.Mods ?? throw new InvalidOperationException("no mods/ in the checkout");

    private static readonly string DataDir = Path.GetFullPath(Path.Combine(DevPaths.Defs!, ".."));

    private static Sim.Defs.DefDatabase DemoDefs() =>
        new GameContent(ModCatalog.Discover([ModsDir]).Resolve(GameContent.DemoMods)).LoadDefs(DataDir);

    [Fact]
    public void TheDemoIsTwoDaysAndItsDragonIsGoneBeforeTheFestival()
    {
        var season = DemoDefs().Season;
        Assert.Equal(2, season.Days);
        Assert.Contains(season.Beats, b => b.Day == 2 && b.Weights().Any(k => k.Kind == CrisisKind.Dragon));
        Assert.All(season.Beats, b => Assert.InRange(b.Day, 1, season.Days));
        foreach (var beat in season.Beats.Where(b => b.Day == season.Days))
        {
            var lasts = beat.Weights().Max(k => k.Kind == CrisisKind.Dragon ? DragonCrisis.Hours : season.CrisisHours);
            Assert.True(beat.ToHour + lasts <= season.FestivalSetupHour, $"a day-{beat.Day} beat could run into the festival");
        }
    }

    [Fact]
    public void TheDemoOffersMillbrookAlone()
    {
        var defs = DemoDefs();
        Assert.True(defs.Map("Millbrook").Offered);
        Assert.False(defs.Map("Reedmere").Offered); // still loaded, for --map and tests
    }

    [Fact]
    public void TheDemoDealsItsSixTrades()
    {
        var dealt = DemoDefs().AllTrades.Where(t => !t.Unlimited).Select(t => t.Name).Order().ToList();
        Assert.Equal(["Blacksmith", "Carpenter", "Cook", "Empath", "Innkeeper", "Tinker"], dealt);
    }

    [Fact]
    public void TheDemoIsStartedOnlyAsADemoMayBe()
    {
        var kept = DemoGate.Arguments(
            ["--demo", "--raid-test", "--new-game", "--map", "Reedmere", "--name", "Arin", "--mods", "miner", "--dev", "--join", "ABCD-1234"],
            out var dropped);
        Assert.Equal(["--demo", "--new-game", "--name", "Arin", "--join", "ABCD-1234"], kept);
        Assert.Equal(["--raid-test", "--map", "Reedmere", "--mods", "miner", "--dev"], dropped);
    }

    [Fact]
    public void ADemoSteamJsonSaysSo()
    {
        Assert.True(Platforms.SteamConfig.Parse("""{ "appId": 2, "storeAppId": 1, "demo": true }""")!.Demo);
        Assert.False(Platforms.SteamConfig.Parse("""{ "appId": 1 }""")!.Demo);
    }
}

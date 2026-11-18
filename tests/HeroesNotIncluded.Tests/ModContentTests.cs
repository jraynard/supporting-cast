using HeroesNotIncluded.Mods;
using Narsil.Mods;

namespace HeroesNotIncluded.Tests;

/// <summary>Mods as the game sees them: the shipped example, content layering, saves and the check.</summary>
public class ModContentTests
{
    private static string ModsDir => DevPaths.Mods ?? throw new InvalidOperationException("no mods/ in the checkout");

    private static readonly string DataDir = Path.GetFullPath(Path.Combine(DevPaths.Defs!, ".."));

    private static GameContent With(params string[] ids) => new(ModCatalog.Discover([ModsDir]).Resolve(ids));

    [Fact]
    public void EveryShippedModPassesTheCheck()
    {
        var catalog = ModCatalog.Discover([ModsDir]);
        Assert.Empty(catalog.Problems);
        Assert.NotEmpty(catalog.All);
        foreach (var mod in catalog.All)
        {
            var report = ModCheck.Check(mod.Directory, catalog, DataDir);
            Assert.True(report.Errors.Count == 0, $"{mod.Id}: {string.Join("; ", report.Errors)}");
        }
    }

    [Fact]
    public void TheLongSeasonPatchesOnlyTheDays()
    {
        var core = new GameContent(ModOrder.None).LoadDefs(DataDir);
        var modded = With("long-season").LoadDefs(DataDir);
        Assert.Equal(5, modded.Season.Days);
        Assert.Equal(core.Season.FestivalHour, modded.Season.FestivalHour);
        Assert.Equal(core.Season.StartingPurse, modded.Season.StartingPurse);
        var report = ModCheck.Check(Path.Combine(ModsDir, "long-season"), ModCatalog.Discover([ModsDir]), DataDir);
        Assert.Contains("Seasons: changes Season", report.Notes);
    }

    [Fact]
    public void ContentLaysModFoldersAfterTheCore()
    {
        var content = With("long-season");
        Assert.Equal(2, content.DefDirectories(DataDir).Count);
        Assert.Equal(Path.Combine(DataDir, "defs"), content.DefDirectories(DataDir)[0]);
        Assert.Single(content.TextureRoots(DataDir)); // the example has no art
        Assert.Equal("+long-season", content.TagSuffix);
        Assert.Equal("", new GameContent(ModOrder.None).TagSuffix);
    }

    [Fact]
    public void ASaveMustMatchTheModsTurnedOn()
    {
        var core = new GameContent(ModOrder.None);
        var modded = With("long-season");
        var plain = new Dictionary<string, string>(); // a save from before mods
        var withMod = new Dictionary<string, string> { [GameContent.SaveMetaKey] = modded.SaveMeta };

        Assert.Null(core.Refuses(plain));
        Assert.Null(modded.Refuses(withMod));
        Assert.Contains("made with long-season", core.Refuses(withMod));
        Assert.Contains("made with no mods", modded.Refuses(plain));
        // Another version of the same mod still loads.
        Assert.Null(modded.Refuses(new Dictionary<string, string> { [GameContent.SaveMetaKey] = "long-season 0.9.0" }));
    }

    [Fact]
    public void ACheckCatchesABrokenReference()
    {
        var dir = Directory.CreateTempSubdirectory("hni-badmod").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, ModManifest.FileName), """{ "id": "badmod" }""");
            Directory.CreateDirectory(Path.Combine(dir, "defs"));
            File.WriteAllText(Path.Combine(dir, "defs", "a.json"), """{ "things": [ { "name": "Lute", "building": { "cost": [ { "def": "Unobtainium" } ] } } ] }""");
            var report = ModCheck.Check(dir, ModCatalog.Empty, DataDir);
            Assert.Contains(report.Errors, e => e.Contains("Unobtainium"));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}

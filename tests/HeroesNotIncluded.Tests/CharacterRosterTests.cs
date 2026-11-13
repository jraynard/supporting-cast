using HeroesNotIncluded.Characters;
using HeroesNotIncluded.Rendering;
using HeroesNotIncluded.Net;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;

namespace HeroesNotIncluded.Tests;

/// <summary>The saved characters players play in every village, solo or co-op (<see cref="CharacterRoster"/>).</summary>
public class CharacterRosterTests : IDisposable
{
    private static readonly Lazy<DefDatabase> Defs = new(() => DefDatabase.LoadDirectory(DevPaths.Defs!));

    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"dw-roster-{Guid.NewGuid():N}");
    private string File => Path.Combine(_dir, "characters.json");

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void EveryHairStyleAndFantasyColourRoundTrips()
    {
        // More styles than a roster holds: a rosterful at a time.
        foreach (var batch in Enum.GetValues<HairStyle>().Chunk(CharacterRoster.Max))
        {
            var roster = CharacterRoster.Load(File, Defs.Value);
            foreach (var old in roster.Characters.ToList())
                roster.Remove(old.Id);
            foreach (var style in batch)
                roster.Put(new SavedCharacter
                {
                    Name = style.ToString(),
                    Look = new Appearance
                    {
                        Hair = style, HairColor = 0x7A44B8, EyeColor = 0xC02A2A, SkinColor = 0xE8BE96,
                        Beard = (BeardStyle)((int)style % 5), Eyes = (EyeStyle)((int)style % 4),
                    },
                    Clothes = ["Tunic:Orchil", "Trousers", "FurBoots"],
                });
            var loaded = CharacterRoster.Load(File, Defs.Value).Characters;
            Assert.Equal(batch.Length, loaded.Count);
            foreach (var c in loaded)
            {
                var style = Enum.Parse<HairStyle>(c.Name);
                Assert.Equal(style, c.Look.Hair);
                Assert.Equal((BeardStyle)((int)style % 5), c.Look.Beard);
                Assert.Equal((EyeStyle)((int)style % 4), c.Look.Eyes);
                Assert.Equal(0x7A44B8u, c.Look.HairColor);
                Assert.Equal(0xC02A2Au, c.Look.EyeColor);
                Assert.Equal(["Tunic:Orchil", "Trousers", "FurBoots"], c.Clothes);
            }
        }
    }

    [Fact]
    public void AnElfStaysAnElfAndAFolkNobodyKnowsIsHuman()
    {
        var roster = CharacterRoster.Load(File, Defs.Value);
        roster.Put(new SavedCharacter { Name = "Ilsa", Look = new Appearance { Folk = Folk.Elf, SkinColor = HumanLook.ElfSkinTones[2] } });
        roster.Put(new SavedCharacter { Name = "Odd", Look = new Appearance { Folk = (Folk)99 } });
        var loaded = CharacterRoster.Load(File, Defs.Value).Characters;
        Assert.Equal(Folk.Elf, loaded.Single(c => c.Name == "Ilsa").Look.Folk);
        Assert.Equal(Folk.Human, loaded.Single(c => c.Name == "Odd").Look.Folk);
    }

    /// <summary>
    /// Some villagers are elves, by the same hash that picks their looks (the same on every peer);
    /// the draw comes last, so everyone who was human before still looks as they did.
    /// </summary>
    [Fact]
    public void AboutOneVillagerInSixIsAnElf()
    {
        var looks = Enumerable.Range(1, 1200).Select(id => HumanLook.Derived(id)).ToList();
        var elves = looks.Where(a => a.Folk == Folk.Elf).ToList();
        Assert.InRange(elves.Count, 1200 / HumanLook.ElfOneIn - 80, 1200 / HumanLook.ElfOneIn + 80);
        Assert.All(elves, a =>
        {
            Assert.Contains(a.SkinColor, HumanLook.ElfSkinTones);
            Assert.Equal(BeardStyle.None, a.Beard);
        });
        Assert.All(looks.Where(a => a.Folk == Folk.Human), a => Assert.Contains(a.SkinColor, HumanLook.SkinTones));
        Assert.Equal(HumanLook.Derived(77), HumanLook.Derived(77));
    }

    [Fact]
    public void EveryHairStyleIsOfferedOnceWithItsOwnSheets()
    {
        Assert.Equal(Enum.GetValues<HairStyle>().Order(), HumanLook.HairStyles.Select(s => s.Style).Order());
        var hair = Enum.GetValues<HairStyle>().Select(h => HumanLook.HairSheet(new Appearance { Hair = h })).ToList();
        Assert.Equal(hair.Count, hair.Distinct().Count());
        foreach (var colours in new[] { HumanLook.HairColors, HumanLook.EyeColors, HumanLook.SkinTones })
            Assert.Equal(colours.Length, colours.Distinct().Count());
    }

    [Fact]
    public void TheLastUsedComesFirstAndAFullRosterDropsNobody()
    {
        var roster = CharacterRoster.Load(File, Defs.Value);
        var first = roster.Put(new SavedCharacter { Name = "First" })!;
        for (var i = 1; i < CharacterRoster.Max; i++)
            Assert.NotNull(roster.Put(new SavedCharacter { Name = $"Next{i}" }));
        Assert.True(roster.Full);

        // One more is refused, and nobody makes way for them.
        Assert.Null(roster.Put(new SavedCharacter { Name = "Late" }));
        Assert.Equal(CharacterRoster.Max, roster.Characters.Count);
        Assert.Contains(roster.Characters, c => c.Id == first.Id);

        // Those already there can still be used and changed.
        Assert.NotNull(roster.Put(first with { Name = "Firstborn" }));
        Assert.Equal(first.Id, roster.Last!.Id);
        Assert.Equal("Firstborn", CharacterRoster.Load(File, Defs.Value).Get(first.Id)!.Name);

        roster.Remove(first.Id);
        Assert.DoesNotContain(CharacterRoster.Load(File, Defs.Value).Characters, c => c.Id == first.Id);
        Assert.NotNull(roster.Put(new SavedCharacter { Name = "Late" }));
    }

    [Fact]
    public void AnUpdateKeepsTheOrder()
    {
        var roster = CharacterRoster.Load(File, Defs.Value);
        var older = roster.Put(new SavedCharacter { Name = "Older" })!;
        var newer = roster.Put(new SavedCharacter { Name = "Newer" })!;
        roster.Update(older with { Name = "Older still" });
        Assert.Equal(newer.Id, roster.Last!.Id);
        Assert.Equal("Older still", roster.Get(older.Id)!.Name);
    }

    [Fact]
    public void RoundsAreCountedByTradeAndKept()
    {
        var progress = new CharacterProgress()
            .With("Blacksmith", earned: 40, survived: true)
            .With("Blacksmith", earned: -5, survived: false)
            .With("Healer", earned: 12, survived: true);
        Assert.Equal(new TradeRecord(2, 1, 35), progress.Trades["Blacksmith"]);
        Assert.Equal(new TradeRecord(1, 1, 12), progress.Trades["Healer"]);
        Assert.Equal(3, progress.Rounds);

        var roster = CharacterRoster.Load(File, Defs.Value);
        var c = roster.Put(new SavedCharacter
        {
            Name = "Bram",
            Progress = progress with { Titles = ["Smith", "Smith", " "], Title = "Smith", Unlocked = ["SmithApron"] },
        })!;
        var loaded = CharacterRoster.Load(File, Defs.Value).Get(c.Id)!;
        Assert.Equal(progress.Trades, loaded.Progress.Trades);
        Assert.Equal(["Smith"], loaded.Progress.Titles);
        Assert.Equal("Smith", loaded.Progress.Title);
        Assert.Equal(["SmithApron"], loaded.Progress.Unlocked);
    }

    [Fact]
    public void ATradeSinceRemovedAndATitleNotEarnedAreDropped()
    {
        var roster = CharacterRoster.Load(File, Defs.Value);
        var c = roster.Put(new SavedCharacter
        {
            Progress = new CharacterProgress { Title = "Archmage" }.With("Alchemist", 3, true).With("Cook", 7, true),
        })!;
        var loaded = CharacterRoster.Load(File, Defs.Value).Get(c.Id)!;
        Assert.Equal(["Cook"], loaded.Progress.Trades.Keys);
        Assert.Null(loaded.Progress.Title);
    }

    /// <summary>Rosters from before the version (a bare list, no progress) load as they were, and are saved anew.</summary>
    [Fact]
    public void AnOldListRosterIsReadAndWrittenAnew()
    {
        Directory.CreateDirectory(_dir);
        var id = Guid.NewGuid();
        System.IO.File.WriteAllText(File, $$"""
            [
              {
                "Id": "{{id}}",
                "Name": "Wren",
                "Look": { "Body": 1, "Hair": 2, "HairColor": 4210752, "EyeColor": 0, "SkinColor": 15253142, "Beard": 0, "Eyes": 1, "Stance": 0, "Folk": 1, "Model": null },
                "Traits": [],
                "Clothes": ["Tunic:Orchil"],
                "LastUsed": "2026-10-01T12:00:00Z"
              }
            ]
            """);
        var roster = CharacterRoster.Load(File, Defs.Value);
        var wren = Assert.Single(roster.Characters);
        Assert.Equal(id, wren.Id);
        Assert.Equal(Folk.Elf, wren.Look.Folk);
        Assert.Equal(HairStyle.Ponytail, wren.Look.Hair);
        Assert.Empty(wren.Progress.Trades);
        Assert.Equal(wren.LastUsed, wren.Created);

        roster.Save();
        Assert.Contains($"\"Version\": {CharacterRoster.Version}", System.IO.File.ReadAllText(File));
        Assert.Equal(id, Assert.Single(CharacterRoster.Load(File, Defs.Value).Characters).Id);
    }

    /// <summary>A roster saved by a newer build is shown, but never overwritten with what this one understands of it.</summary>
    [Fact]
    public void ANewerRosterIsNeverOverwritten()
    {
        Directory.CreateDirectory(_dir);
        var text = """{ "Version": 99, "Characters": [ { "Name": "Future", "Glamour": { "Wings": true } } ] }""";
        System.IO.File.WriteAllText(File, text);
        var roster = CharacterRoster.Load(File, Defs.Value);
        Assert.True(roster.ReadOnly);
        Assert.Equal("Future", Assert.Single(roster.Characters).Name);
        roster.Put(new SavedCharacter { Name = "Now" });
        Assert.Equal(text, System.IO.File.ReadAllText(File));
    }
}

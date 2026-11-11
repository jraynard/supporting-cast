using DragonWorld.Rendering;
using DragonWorld.Net;
using DragonWorld.Sim.Components;
using DragonWorld.Sim.Defs;

namespace DragonWorld.Tests;

/// <summary>The saved characters players bring into co-op games (<see cref="CharacterRoster"/>).</summary>
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
    public void ACharacterAndTheirDragonRoundTrip()
    {
        var roster = CharacterRoster.Load(File, Defs.Value);
        Assert.Empty(roster.Characters);
        var look = new Appearance { Body = BodyType.Female, Hair = HairStyle.Long, HairColor = 0xA8402A, EyeColor = 0x3A6EA5, SkinColor = 0xC68863 };
        var teal = new DragonLook { Element = DragonElement.Poison, Scales = 0x2E6A4A, Belly = 0xE8C070, Wings = 0x24503A, Horns = 0x3A3436, Eyes = 0x7AE0FF };
        var bea = roster.Put(new SavedCharacter { Name = "Bea", Look = look, Traits = ["Hardy"], DragonName = "Kestrel", DragonLook = teal });

        var again = CharacterRoster.Load(File, Defs.Value);
        var loaded = Assert.Single(again.Characters);
        Assert.Equal(bea.Id, loaded.Id);
        Assert.Equal("Bea", loaded.Name);
        Assert.Equal(look, loaded.Look);
        Assert.Equal(["Hardy"], loaded.Traits);
        Assert.Equal(teal, loaded.DragonLook);
        Assert.True(loaded.HasDragon);
        Assert.Equal(bea.Id, again.Find("bea")!.Id);
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
    public void EveryHairStyleIsOfferedOnceWithItsOwnSheets()
    {
        Assert.Equal(Enum.GetValues<HairStyle>().Order(), HumanLook.HairStyles.Select(s => s.Style).Order());
        var hair = Enum.GetValues<HairStyle>().Select(h => HumanLook.HairSheet(new Appearance { Hair = h })).ToList();
        Assert.Equal(hair.Count, hair.Distinct().Count());
        foreach (var colours in new[] { HumanLook.HairColors, HumanLook.EyeColors, HumanLook.SkinTones })
            Assert.Equal(colours.Length, colours.Distinct().Count());
    }

    [Fact]
    public void WhatThisBuildDoesntKnowIsDropped()
    {
        Directory.CreateDirectory(_dir);
        System.IO.File.WriteAllText(File, """
            [ { "Id": "8b0c2f0e-3b8f-4c56-9d3e-1f2a3b4c5d6e", "Name": "  Wren ", "Look": { "Body": 7, "Hair": 1, "HairColor": 4294967295 },
                "Traits": [ "Hardy", "NotATrait", "Hardy" ], "DragonName": "Ash", "DragonLook": { "Scales": 4294967295 } } ]
            """);
        var c = Assert.Single(CharacterRoster.Load(File, Defs.Value).Characters);
        Assert.Equal("Wren", c.Name);
        Assert.Equal(BodyType.Male, c.Look.Body); // 7 isn't a body
        Assert.Equal(0xFFFFFFu, c.Look.HairColor);
        Assert.Equal(["Hardy"], c.Traits);
        Assert.Equal(0xFFFFFFu, c.DragonLook!.Value.Scales);

        System.IO.File.WriteAllText(File, "{ not json");
        Assert.Empty(CharacterRoster.Load(File, Defs.Value).Characters); // a broken file is an empty roster, not a crash
    }

    [Fact]
    public void TheLastUsedComesFirstAndTheOldestGoesPastTheLimit()
    {
        var roster = CharacterRoster.Load(File, Defs.Value);
        var first = roster.Put(new SavedCharacter { Name = "First" });
        for (var i = 0; i < CharacterRoster.Max; i++)
            roster.Put(new SavedCharacter { Name = $"Next{i}" });
        Assert.Equal(CharacterRoster.Max, roster.Characters.Count);
        Assert.DoesNotContain(roster.Characters, c => c.Id == first.Id);

        var picked = roster.Characters[^1];
        roster.Put(picked); // used again
        Assert.Equal(picked.Id, roster.Last!.Id);
        Assert.Equal(CharacterRoster.Max, CharacterRoster.Load(File, Defs.Value).Characters.Count);
        roster.Remove(picked.Id);
        Assert.DoesNotContain(CharacterRoster.Load(File, Defs.Value).Characters, c => c.Id == picked.Id);
    }
}

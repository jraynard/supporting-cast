using DragonWorld.Sim.Combat;
using DragonWorld.Sim.Components;
using DragonWorld.Sim.Defs;
using DragonWorld.Sim.Maps;
using DragonWorld.Sim.Saving;
using DragonWorld.Sim.Systems;
using static DragonWorld.Sim.Tests.TestWorld;

namespace DragonWorld.Sim.Tests;

/// <summary>Clothes (<see cref="Clothing"/>): who gets dressed in what, dyes, and that it never shifts other rolls.</summary>
public class ClothingTests
{
    private static Entity In(Simulation sim, Entity e, ApparelSlot slot) =>
        sim.World.TryGet<Apparel>(e, out var a) && sim.World.IsAlive(a[slot]) ? a[slot] : Entity.Null;

    [Fact]
    public void EveryOutfitResolvesAndPresetsHaveTheirLook()
    {
        var defs = LoadDefs();
        Assert.Contains(defs.AllOutfits, o => o.Villager);
        foreach (var o in defs.AllOutfits)
        {
            Assert.NotEmpty(o.Pieces);
            Assert.All(o.Pieces, p => Assert.True(Clothing.IsClothes(p.ThingDef), $"{o.Name}: {p.Def} isn't clothes"));
            Assert.Equal(o.Pieces.Count, o.Pieces.Select(p => p.ThingDef.Apparel!.Slot).Distinct().Count());
            if (o.Preset)
                Assert.True(o.Hair == null || Enum.TryParse<HairStyle>(o.Hair, out _), $"{o.Name}: unknown hair {o.Hair}");
        }
        Assert.Contains(defs.AllOutfits, o => o.Name == "Wanderer" && o.Preset);
    }

    [Fact]
    public void GeneratedVillagersAreDressedInDyedClothesWithoutMovingTheMainStream()
    {
        var sim = Simulation.CreateStandard(LoadDefs(), 96, 96, 1234, MapGenerator.RecommendedDepth);
        MapGenerator.Generate(sim, new MapGenerator.Options());
        var people = sim.Creatures().Where(e => sim.World.Get<Creature>(e).Def.Race == Race.Human).ToList();
        Assert.NotEmpty(people);
        foreach (var e in people)
        {
            Assert.True(Clothing.Dressed(sim, e));
            var tunic = In(sim, e, ApparelSlot.Tunic);
            Assert.False(tunic.IsNull);
            var t = sim.World.Get<Thing>(tunic);
            if (t.Def.MaterialsOfDef != null)
                Assert.Equal("Cloth", t.Material!.Of); // dyed wool
            Assert.False(In(sim, e, ApparelSlot.Feet).IsNull);
        }

        // Dressing someone draws on their own stream only.
        var extra = sim.SpawnCreature(sim.Defs.Creature("Human"), "Ylva", sim.World.Get<Position>(people[0]).Cell);
        var afterSpawn = sim.Rand.Fingerprint;
        Clothing.DressVillager(sim, extra);
        Assert.Equal(afterSpawn, sim.Rand.Fingerprint);
        Assert.True(Clothing.Dressed(sim, extra));
    }

    [Fact]
    public void TheSameVillagerAlwaysGetsTheSameClothes()
    {
        string Outfit()
        {
            var sim = Simulation.CreateStandard(LoadDefs(), 96, 96, 99, MapGenerator.RecommendedDepth);
            MapGenerator.Generate(sim, new MapGenerator.Options());
            return string.Join("|", sim.Creatures().Where(e => sim.World.Get<Creature>(e).Def.Race == Race.Human)
                .Select(e => string.Join(",", Clothing.Describe(sim, e))));
        }
        Assert.Equal(Outfit(), Outfit());
    }

    [Fact]
    public void WomenOnlyOutfitsGoToWomen()
    {
        var sim = Create();
        var weaver = sim.Defs.Outfit("Weaver");
        Assert.True(Clothing.Suits(weaver, BodyType.Female));
        Assert.False(Clothing.Suits(weaver, BodyType.Male));
        for (var i = 0; i < 24; i++)
        {
            var e = sim.SpawnCreature(sim.Defs.Creature("Human"), $"P{i}", C(5 + i % 10, 5 + i / 10));
            sim.World.Add(e, new Appearance { Body = BodyType.Male });
            Clothing.DressVillager(sim, e);
            Assert.NotEqual("ApronDress", sim.World.Get<Thing>(In(sim, e, ApparelSlot.Tunic)).Def.Name);
        }
    }

    [Fact]
    public void TheCreatorsChoicesAreWornAndUnknownOnesSkipped()
    {
        var sim = Create();
        sim.SpawnStructure(sim.Thing("Wagon"), C(20, 20));
        var player = Guid.NewGuid();
        sim.ApplyNow(new CreateCharacterCommand(player, "Ylva", new Appearance { Hair = HairStyle.Shaggy, Beard = BeardStyle.Stubble, Eyes = EyeStyle.Narrow },
            [], Clothes: ["LeatherVest", "Trousers:IronBlack", "HoodedCloak:Orchil", "IronHelm", "Nonsense:Woad", "Tunic:Granite"]));
        var me = Players.CharacterOf(sim, player);
        var w = sim.World;
        Assert.Equal("LeatherVest", w.Get<Thing>(In(sim, me, ApparelSlot.Body)).Def.Name); // a garment in the Body slot
        Assert.Null(w.Get<Thing>(In(sim, me, ApparelSlot.Tunic)).Material); // granite isn't a dye: an undyed tunic
        Assert.Equal("IronBlack", w.Get<Thing>(In(sim, me, ApparelSlot.Legs)).Material!.Name);
        Assert.Equal("Orchil", w.Get<Thing>(In(sim, me, ApparelSlot.Cloak)).Material!.Name);
        Assert.True(In(sim, me, ApparelSlot.Head).IsNull); // armor isn't clothes: not from the creator
        Assert.Equal(BeardStyle.Stubble, w.Get<Appearance>(me).Beard);
        Assert.Equal(EyeStyle.Narrow, w.Get<Appearance>(me).Eyes);
    }

    [Fact]
    public void RaidersWearTheirKindsClothesUnderTheirArmor()
    {
        var sim = Create();
        var kind = sim.Defs.AllRaiderKinds.First(k => k.Name == "Veteran");
        var r = Raids.SpawnRaider(sim, kind, "Vask", C(10, 10), new SimRandom(5));
        Assert.False(In(sim, r, ApparelSlot.Tunic).IsNull);
        Assert.False(In(sim, r, ApparelSlot.Body).IsNull); // the chain shirt over it
        Assert.False(In(sim, r, ApparelSlot.Feet).IsNull);
    }

    [Fact]
    public void ClothesAndLooksSurviveASave()
    {
        var sim = Create();
        var e = sim.SpawnCreature(sim.Defs.Creature("Human"), "Ylva", C(10, 10));
        sim.World.Add(e, new Appearance { Hair = HairStyle.CrownBraid, Beard = BeardStyle.Braided, Eyes = EyeStyle.Bright });
        Clothing.Dress(sim, e, sim.Defs.Outfit("Seer"));
        var orders = new Outfit();
        orders.SetBare(GearSlot.Cloak, true);
        sim.World.Add(e, orders);
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        Assert.Equal(Clothing.Describe(sim, e), Clothing.Describe(loaded, e));
        Assert.Equal(sim.World.Get<Appearance>(e), loaded.World.Get<Appearance>(e));
        Assert.True(loaded.World.Get<Outfit>(e).BareCloak);
        Assert.Equal(sim.FullHash(), loaded.FullHash());
    }

    [Fact]
    public void ABareClothesSlotIsFilledFromWhatLiesAround()
    {
        var sim = Create();
        var e = sim.SpawnCreature(sim.Defs.Creature("Human"), "Ylva", C(10, 10));
        Array.Fill(sim.World.Get<Needs>(e).Values, 1f);
        Array.Fill(sim.World.Get<Mind>(e).WorkPriority, 0);
        sim.SpawnItem(sim.Thing("FurBoots"), 1, C(13, 10));
        sim.TickMany(SimClock.TicksPerHour);
        Assert.Equal("FurBoots", sim.World.Get<Thing>(In(sim, e, ApparelSlot.Feet)).Def.Name);
    }
}

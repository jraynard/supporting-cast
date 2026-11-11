using DragonWorld.Sim.Components;
using DragonWorld.Sim.Defs;
using DragonWorld.Sim.Maps;
using DragonWorld.Sim.Systems;

namespace DragonWorld.Sim.Tests;

/// <summary>Silver and gold (docs/plans/mining-progression.md): minted into coin, worn as finery, heaped into a dragon's hoard, and counted by raiders.</summary>
public class TreasureTests
{
    private static Simulation Warm()
    {
        var sim = TestWorld.Create(30, 30, depth: 5);
        sim.Climate.BaseTemperature = 20;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 20f);
        return sim;
    }

    private static int Count(Simulation sim, string def) =>
        sim.World.Query<Thing>().Where(e => sim.World.Get<Thing>(e).Def.Name == def)
            .Sum(e => sim.World.TryGet<Stack>(e, out var s) ? s.Count : 1);

    [Fact]
    public void RiverSandHidesAFleckOfGoldNowAndThen()
    {
        int Dig()
        {
            var sim = Warm();
            sim.FillBlocks(new int3(0, 0, 1), new int3(29, 29, 1), "Sand");
            var rand = sim.Rand.Fingerprint;
            for (var y = 0; y < 30; y++)
            for (var x = 0; x < 30; x++)
                sim.DigOut(new int3(x, y, 1));
            Assert.Equal(rand, sim.Rand.Fingerprint);
            return Count(sim, "GoldOre");
        }
        var found = Dig();
        Assert.InRange(found, 10, 50); // 3% of 900
        Assert.Equal(found, Dig());
    }

    [Fact]
    public void TheGoldsmithStrikesCoinAndMakesRings()
    {
        var sim = Warm();
        var smith = sim.SpawnCreature(sim.Defs.Creature("Human"), "Smith", C(5, 5), ageDays: 25 * 365);
        sim.SetSkills(smith, 8);
        var work = sim.World.Get<Mind>(smith).WorkPriority;
        Array.Fill(work, 0);
        work[(int)WorkType.Craft] = 1;
        sim.World.Add(smith, new Outfit { BareNeck = true }); // keep the ring for the count
        var mint = sim.SpawnStructure(sim.Thing("Mint"), C(15, 15));
        sim.RebuildDerivedIfNeeded();
        sim.SpawnItem(sim.Thing("SilverBar"), 2, C(6, 9));
        sim.SpawnItem(sim.Thing("BronzeHammer"), 1, C(7, 9));
        RecipeBooks.Discover(sim, smith, "handle:SilverBar");
        sim.ApplyNow(new Core.AddBillCommand(mint, sim.Defs.Recipe("MintSilver"), BillMode.Count, 1));
        sim.ApplyNow(new Core.AddBillCommand(mint, sim.Defs.Recipe("MakeSilverRing"), BillMode.Count, 1));
        for (var t = 0; t < SimClock.HoursToTicks(6); t += 60)
        {
            Array.Fill(sim.World.Get<Needs>(smith).Values, 1f);
            sim.TickMany(60);
        }
        Assert.Equal(50, Count(sim, "SilverCoin"));
        Assert.Equal(1, Count(sim, "SilverRing"));
        Assert.Equal(110f, Hoard.VillageWealth(sim)); // 50 coins and a 60-worth ring
    }

    [Fact]
    public void JewelryIsWornAtTheNeckAndLiftsTheMood()
    {
        var sim = Warm();
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), "Arin", C(5, 5), ageDays: 25 * 365);
        var torc = sim.Wear(me, sim.Thing("GoldTorc"));
        Assert.Equal(torc, sim.World.Get<Apparel>(me).Neck);
        Assert.Contains(torc, sim.Worn(me));
        Assert.True(MoodSystem.Holds(sim, me, sim.Defs.Thought("WearingFinery")));
        var (loaded, _) = Saving.SaveGame.Read(Saving.SaveGame.Write(sim), sim.Defs);
        Assert.Equal(torc, loaded.World.Get<Apparel>(me).Neck);
        Assert.Equal(sim.StateHash(), loaded.StateHash());
    }

    [Fact]
    public void ADragonGivenGoldKeepsItAndSleepsHappyOnIt()
    {
        var sim = Warm();
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), "Arin", C(10, 10), ageDays: 25 * 365);
        sim.World.Add(me, new Avatar());
        var dragon = Scenarios.Companions.SpawnBondedDragon(sim, me, "Ashwing", null, C(11, 10));
        var before = sim.World.Get<Dragon>(dragon).Affinity;
        var bar = sim.CreateItem(sim.Thing("GoldBar"), 1);
        sim.World.Get<Carrying>(me).Item = bar;
        var action = AvatarSystem.ActionAt(sim, me, sim.World.Get<Position>(dragon).Cell);
        Assert.Equal(AvatarActionKind.Gift, action.Kind);
        Assert.Equal("Give gold bar to Ashwing", action.Label);
        AvatarSystem.Perform(sim, me, action);
        Assert.True(sim.World.Get<Carrying>(me).Item.IsNull);
        // Worth its value (up to the Gift interaction's affinity), slowed by how far the trust has
        // already come (a trusting partner gains at its level's factor).
        var worth = Math.Min(sim.Defs.DragonInteraction("Gift").Affinity, sim.Thing("GoldBar").HoardValue * Hoard.AffinityPerValue);
        Assert.Equal(before + worth * Bonding.Level(sim, before)!.GainFactor, sim.World.Get<Dragon>(dragon).Affinity, 3);
        Assert.Contains(sim.World.Get<Mood>(dragon).Memories, m => m.Def.Name == "GivenTreasure");
        Assert.True(Hoard.ValueAround(sim, sim.World.Get<Position>(dragon).Cell) >= Hoard.Enough);

        sim.World.Get<Mind>(dragon).Asleep = true;
        Assert.True(MoodSystem.Holds(sim, dragon, sim.Defs.Thought("NearHoard")));
    }

    [Fact]
    public void TreasureDrawsBiggerRaids()
    {
        var sim = Warm();
        sim.SpawnCreature(sim.Defs.Creature("Human"), "Arin", C(10, 10), ageDays: 25 * 365);
        var poor = sim.Storyteller.RaidPoints(sim);
        sim.SpawnItem(sim.Thing("GoldBar"), 4, C(12, 12));
        Assert.Equal(poor + 4 * 250 / Hoard.WealthPerPoint, sim.Storyteller.RaidPoints(sim), 3);
    }

    [Theory]
    [InlineData(1234UL)]
    [InlineData(99UL)]
    public void GeneratedMapsHoldSilverAndGoldDeepInTheGranite(ulong seed)
    {
        var sim = Simulation.CreateStandard(TestWorld.LoadDefs(), 160, 160, seed, MapGenerator.RecommendedDepth);
        MapGenerator.Generate(sim, new MapGenerator.Options { SpawnStartingKit = false, Wildlife = false });
        int silver = 0, gold = 0, copper = 0, tin = 0, coal = 0;
        foreach (var b in sim.Map.Block)
            switch (b.Name)
            {
                case "SilverOre": silver++; break;
                case "GoldOre": gold++; break;
                case "CopperOre": copper++; break;
                case "TinOre": tin++; break;
                case "CoalSeam": coal++; break;
            }
        Assert.True(copper > 50 && tin > 10 && coal > 50 && silver > 10 && gold > 3,
            $"seed {seed}: copper {copper}, tin {tin}, coal {coal}, silver {silver}, gold {gold}");
    }
}

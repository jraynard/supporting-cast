using DragonWorld.Sim.Ai;
using DragonWorld.Sim.Combat;
using DragonWorld.Sim.Components;
using DragonWorld.Sim.Defs;
using DragonWorld.Sim.Maps;
using DragonWorld.Sim.Saving;
using DragonWorld.Sim.Scenarios;
using DragonWorld.Sim.Systems;

namespace DragonWorld.Sim.Tests;

/// <summary>
/// Save games. The contract: a loaded game is the saved one with every job interrupted, and from
/// there both run tick for tick alike. Comparing hashes for hours afterwards catches anything the
/// save forgot (random state, entity ids, store order, a system's private bookkeeping).
/// </summary>
public class SaveLoadTests
{
    /// <summary>Save <paramref name="live"/>, load it back, and interrupt the original to match.</summary>
    private static Simulation RoundTrip(Simulation live)
    {
        var bytes = SaveGame.Write(live);
        var (loaded, _) = SaveGame.Read(bytes, live.Defs);
        AiSystem.InterruptAll(live);
        return loaded;
    }

    /// <summary>Equal now and after every game hour for <paramref name="hours"/> hours.</summary>
    private static void AssertSameFuture(Simulation live, Simulation loaded, int hours)
    {
        Assert.Equal(live.StateHash(), loaded.StateHash());
        Assert.True(live.FullHash() == loaded.FullHash(), "the load restored something differently (FullHash)");
        for (var h = 1; h <= hours; h++)
        {
            live.TickMany(SimClock.TicksPerHour);
            loaded.TickMany(SimClock.TicksPerHour);
            Assert.True(live.StateHash() == loaded.StateHash(), $"the loaded game diverged within hour {h}");
            Assert.True(live.FullHash() == loaded.FullHash(), $"the loaded game diverged within hour {h} (FullHash)");
        }
        Assert.Equal(live.Messages.Messages.Count, loaded.Messages.Messages.Count);
    }

    private static (Simulation Sim, int3 Center) Generated(ulong seed, int size = 120)
    {
        var sim = Simulation.CreateStandard(TestWorld.LoadDefs(), size, size, seed, MapGenerator.RecommendedDepth);
        var center = MapGenerator.Generate(sim);
        SliceDemo.ApplyVillageDefaults(sim, center);
        return (sim, center);
    }

    [Fact]
    public void LoadedGameContinuesLikeTheInterruptedOriginal()
    {
        var (live, center) = Generated(99);
        SliceDemo.ApplyPlayerOrders(live, center);
        live.TickMany(SimClock.HoursToTicks(7)); // mid-morning: hauling, building, digging
        Assert.Contains(live.World.Query<Mind>(), e => live.World.Get<Mind>(e).Job != null);

        var loaded = RoundTrip(live);
        AssertSameFuture(live, loaded, hours: 5);
    }

    [Fact]
    public void SavingALoadedGameGivesTheSameBytes()
    {
        var (live, center) = Generated(7, size: 64);
        SliceDemo.ApplyPlayerOrders(live, center);
        live.TickMany(SimClock.HoursToTicks(3));
        AiSystem.InterruptAll(live);

        var bytes = SaveGame.Write(live);
        var again = SaveGame.Write(SaveGame.Read(bytes, live.Defs).Sim);
        Assert.Equal(bytes, again);
    }

    [Fact]
    public void SliceCarriesOnAfterALoad()
    {
        var (sim, center) = Generated(1234, size: 160);
        var hall = SliceDemo.ApplyPlayerOrders(sim, center);
        sim.TickMany(SimClock.DaysToTicks(1f));
        SliceDemo.ApplyUpperStoreyOrders(sim, hall);
        sim.TickMany(SimClock.HoursToTicks(2));

        // Load in the middle of the slice and play on: the egg hatches and the hall gets finished.
        var (loaded, header) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        Assert.Equal(sim.Clock.Tick, header.Tick);
        Assert.Equal(sim.Clock.Tick, loaded.Clock.Tick);
        // Whether the floor got finished is the question; a hatchling's sneeze may burn the hall
        // down afterwards, which is the game working, not the load failing.
        var floorBuilt = false;
        for (var hour = 0; hour < 48 - 2; hour++)
        {
            loaded.TickMany(SimClock.HoursToTicks(1));
            floorBuilt |= loaded.Map.HasFloor[loaded.Map.Idx(hall.UpperRoom)];
        }

        var w = loaded.World;
        var dragon = Assert.Single(w.Query<Dragon>());
        Assert.False(w.Get<Creature>(dragon).Dead);
        Assert.True(w.IsAlive(w.Get<Dragon>(dragon).Keeper));
        Assert.True(floorBuilt, "the upper floor should be built after loading");
        Assert.All(w.Query<Creature>(), e => Assert.False(w.Get<Creature>(e).Dead, $"{w.Get<Creature>(e).Name} died"));
    }

    [Fact]
    public void StorytellerStateSurvives()
    {
        var (live, _) = Generated(5, size: 64);
        live.Storyteller.SetDifficulty(live, live.Defs.AllDifficulties.First(d => !d.Peaceful));
        live.TickMany(SimClock.HoursToTicks(2));

        var loaded = RoundTrip(live);
        Assert.Equal(live.Storyteller.Difficulty, loaded.Storyteller.Difficulty);
        Assert.Equal(live.Storyteller.NextRaidTick, loaded.Storyteller.NextRaidTick);

        // Its private random stream came back too: the same raid arrives in both.
        var a = live.Storyteller.SendRaid(live);
        var b = loaded.Storyteller.SendRaid(loaded);
        Assert.NotEmpty(a);
        Assert.Equal(a, b);
        AssertSameFuture(live, loaded, hours: 2);
    }

    [Fact]
    public void WildlifeStateSurvives()
    {
        var (live, _) = Generated(5, size: 64);
        live.TickMany(SimClock.HoursToTicks(1));
        Assert.NotEmpty(live.World.Query<Herd>());

        var loaded = RoundTrip(live);
        Assert.Equal(live.Wildlife.NextArrivalTick, loaded.Wildlife.NextArrivalTick);
        var a = live.Wildlife.SendHerd(live);
        var b = loaded.Wildlife.SendHerd(loaded);
        Assert.NotEmpty(a);
        Assert.Equal(a, b);
        AssertSameFuture(live, loaded, hours: 3);
    }

    [Fact]
    public void AHuntInProgressPlaysOnAlike()
    {
        var (live, center) = Generated(5, size: 96);
        var w = live.World;
        var hunter = live.Creatures().First(e => w.Get<Creature>(e).Def.Race == Race.Human);
        live.Equip(hunter, live.Defs.Thing("Bow"));
        foreach (var e in live.World.Query<Herd>())
            w.Add(e, new HuntOrder());
        live.TickMany(SimClock.HoursToTicks(1));
        Assert.Contains(w.Query<Mind>(), e => w.Get<Mind>(e).Job is HuntJob);

        var loaded = RoundTrip(live);
        AssertSameFuture(live, loaded, hours: 3);
    }

    [Fact]
    public void AFightInProgressSnapshotsWholeAndPlaysOnAlike()
    {
        var (live, center) = Generated(1234, size: 160);
        RaidDemo.Setup(live, center);
        for (var t = 0; t < SimClock.TicksPerHour && !live.Combat.Active; t++)
            live.Tick();
        Assert.True(live.Combat.Active);
        live.Combat.VillageAuto = true;
        for (var i = 0; i < 1500 && live.Combat.Round < 2; i++)
            live.Combat.Advance(live); // into the fight: TU spent, reactions used, a round gone by
        Assert.True(live.Combat.Active, "the fight should still be on to snapshot it");

        // As co-op's barrier does: play out any move under way, then interrupt.
        live.Combat.FinishAnimation(live);
        AiSystem.InterruptAll(live);
        var (loaded, _) = SaveGame.Read(SaveGame.Snapshot(live), live.Defs);
        Assert.True(loaded.Combat.Active);
        Assert.Equal(live.StateHash(), loaded.StateHash());
        Assert.True(live.FullHash() == loaded.FullHash(), "the snapshot restored the fight differently (FullHash)");

        for (var step = 0; step < 20_000 && (live.Combat.Active || loaded.Combat.Active); step++)
        {
            live.Combat.Advance(live);
            loaded.Combat.Advance(loaded);
            if (step % 50 == 0)
                Assert.True(live.StateHash() == loaded.StateHash(), $"the loaded fight diverged by step {step}");
        }
        Assert.Equal(live.Combat.Log, loaded.Combat.Log);
        Assert.Equal(live.Combat.Outcome, loaded.Combat.Outcome);
        AssertSameFuture(live, loaded, hours: 1);
    }

    [Fact]
    public void AFightChangesTheStateHash()
    {
        var (sim, center) = Generated(1234, size: 160);
        RaidDemo.Setup(sim, center);
        for (var t = 0; t < SimClock.TicksPerHour && !sim.Combat.Active; t++)
            sim.Tick();
        var before = sim.StateHash(withMap: false);
        sim.Combat.Units[0].TimeUnits -= 1; // nothing else moved: only the fight differs
        Assert.NotEqual(before, sim.StateHash(withMap: false));
    }

    [Fact]
    public void WoundsGearAndTheLastFightSurvive()
    {
        var (live, center) = Generated(1234, size: 160);
        var cast = RaidDemo.Setup(live, center);
        for (var t = 0; t < SimClock.TicksPerHour && !live.Combat.Active; t++)
            live.Tick();
        Assert.True(live.Combat.Active);
        Assert.Throws<SaveException>(() => SaveGame.Write(live));
        live.Combat.Resolve(live, maxRounds: 40);
        live.TickMany(SimClock.HoursToTicks(2));

        var loaded = RoundTrip(live);
        Assert.Equal(live.Combat.Outcome, loaded.Combat.Outcome);
        Assert.Equal(live.Combat.Log, loaded.Combat.Log);
        foreach (var e in live.World.Query<Health>())
        {
            var a = live.World.Get<Health>(e);
            var b = loaded.World.Get<Health>(e);
            Assert.Equal(a.Injuries.Count, b.Injuries.Count);
            Assert.Equal(a.Scars, b.Scars);
            Assert.Equal(a.Pain, b.Pain);
        }
        Assert.Contains(live.World.Query<Health>(), e => live.World.Get<Health>(e).Injuries.Count > 0);
        foreach (var e in live.World.Query<Apparel>())
            Assert.Equal(live.World.Get<Apparel>(e), loaded.World.Get<Apparel>(e));
        Assert.True(loaded.World.IsAlive(cast.Archer));
        AssertSameFuture(live, loaded, hours: 4);
    }

    [Fact]
    public void PrisonersSurvive()
    {
        var (live, center) = Generated(1234, size: 160);
        RaidDemo.Setup(live, center);
        for (var t = 0; t < SimClock.TicksPerHour && !live.Combat.Active; t++)
            live.Tick();
        live.Combat.Resolve(live, maxRounds: 40);
        var bandit = live.Creatures().First(e => live.World.Get<Creature>(e).Faction == Faction.Raider);
        live.World.Get<Creature>(bandit).Faction = Faction.Prisoner;
        live.World.Add(bandit, new Prisoner { Was = Faction.Raider, Mode = PrisonerMode.Recruit, Resistance = 3, LastVisit = 17 });
        live.World.Add(bandit, new CaptureOrder());

        var loaded = RoundTrip(live);
        Assert.Equal(live.World.Get<Prisoner>(bandit), loaded.World.Get<Prisoner>(bandit));
        Assert.True(loaded.World.Has<CaptureOrder>(bandit));
        Assert.Equal(Faction.Prisoner, loaded.World.Get<Creature>(bandit).Faction);
    }

    [Fact]
    public void StartsNextTickPredictsTheFight()
    {
        var (sim, center) = Generated(1234, size: 160);
        RaidDemo.Setup(sim, center);
        var predicted = false;
        for (var t = 0; t < SimClock.TicksPerHour && !sim.Combat.Active; t++)
        {
            predicted = sim.Combat.StartsNextTick(sim);
            sim.Tick();
            Assert.Equal(predicted, sim.Combat.Active);
        }
        Assert.True(predicted, "the bandits should have come within sight");
    }

    [Fact]
    public void BadFilesFailWithAClearError()
    {
        var (sim, _) = Generated(3, size: 48);
        sim.TickMany(600);
        var bytes = SaveGame.Write(sim);

        var newer = (byte[])bytes.Clone();
        BitConverter.GetBytes(SaveGame.FormatVersion + 1).CopyTo(newer, 4);
        Assert.Contains("newer version", Assert.Throws<SaveException>(() => SaveGame.Read(newer, sim.Defs)).Message);

        Assert.Throws<SaveException>(() => SaveGame.Read(bytes[..(bytes.Length / 2)], sim.Defs));
        Assert.Throws<SaveException>(() => SaveGame.Read(bytes[..10], sim.Defs));
        Assert.Throws<SaveException>(() => SaveGame.Read("not a save at all"u8.ToArray(), sim.Defs));
    }

    [Fact]
    public void UnknownDefsAreNamed()
    {
        // Saved with defs where the campfire is called something this game doesn't have.
        var defs = DefDatabase.LoadDirectory(TestWorld.FindDefsDirectory()); // a private copy we can rename in
        var sim = Simulation.CreateStandard(defs, 48, 48, 3, MapGenerator.RecommendedDepth);
        var center = MapGenerator.Generate(sim);
        SliceDemo.ApplyVillageDefaults(sim, center);
        sim.SpawnStructure(defs.Thing("Campfire"), sim.SpiralFrom(center + new int3(3, 3, 0), 6).First(c => sim.CanPlaceStructure(c, defs.Thing("Campfire"))));
        defs.Thing("Campfire").Name = "Brazier";

        var bytes = SaveGame.Write(sim);
        var error = Assert.Throws<SaveException>(() => SaveGame.Read(bytes, TestWorld.LoadDefs()));
        Assert.Contains("'Brazier'", error.Message);
    }

    /// <summary>A private copy of the game's defs with <paramref name="edit"/> applied to core.json (or every file).</summary>
    private static DefDatabase EditedDefs(Func<string, string> edit, bool everyFile = false)
    {
        var dir = Path.Combine(Path.GetTempPath(), "dw-defs-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            foreach (var file in Directory.GetFiles(TestWorld.FindDefsDirectory(), "*.json"))
            {
                var text = File.ReadAllText(file);
                File.WriteAllText(Path.Combine(dir, Path.GetFileName(file)), everyFile || Path.GetFileName(file) == "core.json" ? edit(text) : text);
            }
            return DefDatabase.LoadDirectory(dir);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private const string BedCost = """
        "isBed": true, "work": 150, "cost": [ { "def": "Wood", "count": 10 } ]
        """;

    [Fact]
    public void RebindOntoFreshDefsContinuesLikeTheOriginal()
    {
        var (live, center) = Generated(99, size: 64);
        SliceDemo.ApplyPlayerOrders(live, center);
        live.TickMany(SimClock.HoursToTicks(3));

        var rebound = SaveGame.Rebind(live, EditedDefs(text => text));
        Assert.NotSame(live.Defs, rebound.Defs);
        AiSystem.InterruptAll(live);
        AssertSameFuture(live, rebound, hours: 2);
    }

    [Fact]
    public void RebindKeepsDeliveriesWhenTheCostListChanges()
    {
        var sim = TestWorld.Create(20, 20);
        var site = sim.PlaceBlueprint(sim.Defs.Thing("Bed"), TestWorld.C(5, 5));
        sim.World.Get<Construction>(site).Delivered[0] = 5; // 5 of the 10 wood

        // Stone goes in front of the wood: the delivered wood must follow the wood entry.
        var defs = EditedDefs(text =>
        {
            Assert.Contains(BedCost, text);
            return text.Replace(BedCost, """
                "isBed": true, "work": 150, "cost": [ { "def": "Stone", "count": 3 }, { "def": "Wood", "count": 10 } ]
                """);
        });
        var rebound = SaveGame.Rebind(sim, defs);
        var c = rebound.World.Get<Construction>(site);
        Assert.Equal([0, 5], c.Delivered);
        Assert.Equal(0, c.NextNeededMaterial(out var stone));
        Assert.Equal(3, stone);
        Assert.False(c.MaterialsComplete);
    }

    [Fact]
    public void RebindNamesADefThatIsGone()
    {
        var sim = TestWorld.Create(20, 20);
        sim.PlaceBlueprint(sim.Defs.Thing("Bed"), TestWorld.C(5, 5));
        // Renamed everywhere it's named (the sorcerer's tower wants one too), so only the save is stale.
        var defs = EditedDefs(text => text.Replace("\"name\": \"Bed\"", "\"name\": \"Cot\"").Replace("\"Bed\",", "\"Cot\","), everyFile: true);
        Assert.Contains("'Bed'", Assert.Throws<SaveException>(() => SaveGame.Rebind(sim, defs)).Message);
    }

    [Fact]
    public void FormatOneConstructionSitesStillLoad()
    {
        // Format 1 wrote Delivered by position: [target, ints, work, materials].
        var defs = TestWorld.LoadDefs();
        using var buffer = new MemoryStream();
        using (var w = new SaveWriter(buffer, defs))
        {
            w.Int(1); // one entity
            w.Entity(new Entity(0, 0));
            w.Def(defs.Thing("Bed"));
            w.Ints([6, 99]); // a stale extra slot is dropped
            w.Float(12f);
            w.Int(0);
        }
        var world = new World();
        world.Create();
        buffer.Position = 0;
        using var r = new SaveReader(buffer, defs, version: 1);
        ComponentCodecs.All.Single(k => k.Name == "Construction").Read(r, world);
        var c = world.Get<Construction>(new Entity(0, 0));
        Assert.Equal([6], c.Delivered);
        Assert.Equal(12f, c.WorkDone);
    }

    [Fact]
    public void EveryComponentTypeHasACodec()
    {
        var types = typeof(Creature).Assembly.GetTypes()
            .Where(t => t.Namespace == typeof(Creature).Namespace && !t.IsEnum && !t.IsAbstract && !t.IsNested)
            .Where(t => t != typeof(Injury) && t != typeof(Infection) && t != typeof(Scar) && t != typeof(Memory) && t != typeof(Bill)) // parts of Health, Mood and Workbench
            .Append(typeof(Leaving));
        foreach (var t in types)
            Assert.True(ComponentCodecs.Has(t), $"{t.Name} has no save codec");
    }
}

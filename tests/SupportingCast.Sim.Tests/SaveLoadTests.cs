using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Combat;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Maps;
using SupportingCast.Sim.Saving;
using SupportingCast.Sim.Scenarios;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Tests;

/// <summary>
/// Save games. The contract: a loaded game is the saved one with every job interrupted, and from
/// there both run tick for tick alike. Comparing hashes for hours afterwards catches anything the
/// save forgot (random state, entity ids, store order, a system's private bookkeeping).
/// </summary>
public class SaveLoadTests
{
    /// <summary>Save <paramref name="live"/>, load it back, and interrupt the original to match.</summary>
    internal static Simulation RoundTrip(Simulation live)
    {
        var bytes = SaveGame.Write(live);
        var (loaded, _) = SaveGame.Read(bytes, live.Defs);
        AiSystem.InterruptAll(live);
        return loaded;
    }

    /// <summary>Equal now and after every game hour for <paramref name="hours"/> hours.</summary>
    internal static void AssertSameFuture(Simulation live, Simulation loaded, int hours)
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
        VillageSetup.ApplyVillageDefaults(sim, center);
        return (sim, center);
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
    public void APartyInTownSurvives()
    {
        var live = VillageRound.NewSimulation(TestWorld.LoadDefs(), 1234);
        live.Presets.PresetMode = true;
        VillageRound.Setup(live);
        live.Round.OpenLobby();
        live.ApplyNow(new Round.JoinLobbyCommand(Guid.Empty, "Arin", default, []));
        live.ApplyNow(new Round.StartRoundCommand());
        var party = live.Parties.Send(live, live.Defs.Party("IronLantern"));
        Assert.NotEmpty(party);
        live.World.Get<Needs>(party[0]).Values[live.Defs.Need("Food").Index] = 0.5f;
        live.TickMany(SimClock.HoursToTicks(1));
        Assert.Contains(live.Shops.All, e => e.Kind.Kind == ServiceKind.Meal);

        var loaded = RoundTrip(live);
        Assert.Equal(live.Parties.NextArrivalTick, loaded.Parties.NextArrivalTick);
        Assert.Equal(live.Parties.PartiesSent, loaded.Parties.PartiesSent);
        Assert.Equal(party.Count, loaded.World.Query<Adventurer>().Count);
        Assert.Equal(live.Shops.All.Select(e => (e.Id, e.Kind, e.Status, e.KnownBy.Count)),
            loaded.Shops.All.Select(e => (e.Id, e.Kind, e.Status, e.KnownBy.Count)));
        // Sent on their way in both: they walk off the map alike.
        foreach (var e in party)
        {
            live.World.Get<Adventurer>(e).LeaveTick = live.Clock.Tick;
            loaded.World.Get<Adventurer>(e).LeaveTick = loaded.Clock.Tick;
        }
        AssertSameFuture(live, loaded, hours: 4);
    }

    [Fact]
    public void APartyAwayOnAQuestSurvives()
    {
        var (live, me, party) = ExpeditionTests.Round("Innkeeper");
        var q = live.Expeditions.Quests[0];
        var strength = Expeditions.ExpeditionSystem.PartyStrength(live, live.World.Get<Adventurer>(party[0]).Party);
        live.Expeditions.Hear(me, q.Id, strength * 0.9f, null, 1f);
        var at = live.World.Get<Position>(party[0]).Cell;
        var cell = live.SpiralFrom(at, 2).First(c => c != at && live.Map.Passable(c) && c.Z == at.Z);
        live.World.Get<Position>(me).Cell = cell;
        live.World.Get<Position>(me).Pos = Position.CenterOf(cell);
        Assert.True(live.Expeditions.Offer(live, me, party[0], q.Id));
        for (var h = 0; h < 6 && party.Any(e => live.World.IsAlive(e)); h++)
            live.TickMany(SimClock.TicksPerHour);
        Assert.NotEmpty(live.Expeditions.Out[0].Members);

        var loaded = RoundTrip(live);
        Assert.Equal(live.Expeditions.Out[0].Members.Select(m => (m.Name, m.Coin, m.Wounds.Count, m.Weapon?.Hp)),
            loaded.Expeditions.Out[0].Members.Select(m => (m.Name, m.Coin, m.Wounds.Count, m.Weapon?.Hp)));
        AssertSameFuture(live, loaded, hours: 12);
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
        VillageSetup.ApplyVillageDefaults(sim, center);
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

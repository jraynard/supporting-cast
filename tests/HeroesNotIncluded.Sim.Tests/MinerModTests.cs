using HeroesNotIncluded.Miner;
using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Modding;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>The miner (mods/miner): a player's trade at the mine in Millbrook's hill, turned on here as a player would.</summary>
[Collection(nameof(SimContentTests))]
public class MinerModTests
{
    private static readonly Guid Arin = new("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Bea = new("00000000-0000-0000-0000-00000000000b");

    private static DefDatabase MinerDefs() =>
        DefDatabase.LoadDirectories([.. TestWorld.DefDirectories(), Path.Combine(TestWorld.FindDefsDirectory(), "..", "..", "mods", "miner", "defs")]);

    private static T WithMiner<T>(Func<T> test)
    {
        try
        {
            SimContent.Use([.. TestContent.Standard, ("miner", typeof(MinerMod).Assembly)]);
            return test();
        }
        finally
        {
            SimContent.Use(TestContent.Standard);
        }
    }

    private static Simulation Round(DefDatabase defs, params (Guid Id, string? Wants)[] seats)
    {
        var sim = VillageRound.NewSimulation(defs, 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        foreach (var (id, wants) in seats)
        {
            sim.ApplyNow(new JoinLobbyCommand(id, id.ToString()[^1..], default));
            if (wants != null)
                sim.ApplyNow(new SetTradePreferencesCommand(id, [sim.Defs.Trade(wants)], []));
        }
        sim.ApplyNow(new StartRoundCommand());
        return sim;
    }

    /// <summary>Tick the sim with the keys a player would press, through the predictor as the game does.</summary>
    private static void Play(Simulation sim, Entity me, Func<WorkSession, (bool Held, int Axis)> keys, int maxTicks = 60 * 120)
    {
        var predictor = new WorkPredictor();
        for (var t = 0; t < maxTicks && sim.World.Has<WorkSession>(me); t++)
        {
            sim.Tick();
            var (held, axis) = predictor.Session is { } s ? keys(s) : (false, 0);
            predictor.Update(sim, me, 1, false, held, axis, c => sim.ApplyNow(c));
        }
        sim.Tick();
    }

    /// <summary>The bot's own hewing (it reads only the session): over the crack, swung back past the mark, let go.</summary>
    private static (bool, int) Hew(WorkSession s) =>
        new MinerBot(null!).Play(s) is { } k ? (k.Held, k.Axis) : (false, 0);

    private static (Simulation Sim, Entity Me) Miner()
    {
        var sim = Round(MinerDefs(), (Arin, "Miner"));
        var me = Players.CharacterOf(sim, Arin);
        return (sim, me);
    }

    [Fact]
    public void NobodyIsTheMinerUnlessTheyAskedAndTheShedStandsByTheMine() => WithMiner(() =>
    {
        var defs = MinerDefs();
        Assert.True(defs.Trade("Miner").PlayerOnly);
        var sim = Round(defs, (Arin, null), (Bea, "Blacksmith"));
        Assert.DoesNotContain(sim.Creatures(), e => sim.World.TryGet<Trade>(e, out var t) && t.Def.Name == "Miner");
        var shed = ShopSystem.WorkplaceOf(sim, defs.Trade("Miner"));
        Assert.False(shed.IsNull, "the shed's plot is filled with the mod on");
        // Without the mod the plot's left empty, and the round goes on as it would.
        var plain = Round(TestWorld.LoadDefs(), (Bea, "Blacksmith"));
        Assert.DoesNotContain(plain.World.Query<PresetBuilding>(), e => plain.World.Get<PresetBuilding>(e).Def.Name == "MineShed");
        return 0;
    });

    [Fact]
    public void TheMinerMayDigAndMineButGathersNothingElse() => WithMiner(() =>
    {
        var (sim, me) = Miner();
        Assert.True(RoundSystem.MayMine(sim, me));
        Assert.False(RoundSystem.MayGather(sim, me));
        Assert.False(RoundSystem.MayFell(sim, me));
        var mind = sim.World.Get<Mind>(me);
        Assert.True(mind.WorkPriority[(int)WorkType.Mine] > 0);
        Assert.Equal(0, mind.WorkPriority[(int)WorkType.Chop]);
        Assert.Equal(0, mind.WorkPriority[(int)WorkType.Harvest]);
        Assert.True(Crafting.ToolSpeed(sim, me, "Pick") > 0f, "the kit's pick");
        // Anyone else in the round still may not.
        var smith = sim.Creatures().First(e => sim.World.TryGet<Trade>(e, out var t) && t.Def.Name == "Blacksmith");
        Assert.False(RoundSystem.MayMine(sim, smith));
        return 0;
    });

    [Fact]
    public void FAtARockFaceHewsItOut() => WithMiner(() =>
    {
        var (sim, me) = Miner();
        HewDrill.Stage(sim, me);
        var hew = Assert.IsType<HewSession>(sim.World.Get<WorkSession>(me));
        var face = hew.Cell;
        Play(sim, me, Hew);
        Assert.False(sim.World.Has<WorkSession>(me));
        Assert.False(sim.Map.IsSolid(sim.Map.Idx(face)), "the face came away");
        Assert.Contains(sim.World.Query<Thing>(), e => sim.World.Get<Thing>(e) is { Spawned: true } t && t.Cell == face && t.Def.Name == "Stone");
        return 0;
    });

    [Fact]
    public void ACleanHewingGetsMoreOutOfASeamThanTheDigKey() => WithMiner(() =>
    {
        var (sim, me) = Miner();
        HewDrill.Stage(sim, me);
        var face = ((HewSession)sim.World.Get<WorkSession>(me)).Cell;
        WorkSystem.Stop(sim, me, null);
        var iron = sim.Defs.Block("IronOre");
        sim.Map.Block[sim.Map.Idx(face)] = iron;
        sim.Map.MarkStructureChanged(sim.Map.Idx(face));
        sim.RebuildDerivedIfNeeded();
        // What the dig key would give the same hand for it.
        var dug = iron.DigYield.Where(y => y.ThingDef.Name == "IronOre")
            .Sum(y => Mining.OreYield(sim, me, iron, y.ThingDef, y.Count));
        // F at it, as a player would: hewn as many slabs as the rock it looks like.
        var action = AvatarSystem.ActionAt(sim, me, face);
        Assert.Equal(AvatarActionKind.Custom, action.Kind);
        Assert.StartsWith("Hew the", action.Label);
        AvatarSystem.Perform(sim, me, action);
        Assert.Equal(HewSession.ChunksFor(HewSession.FaceOf(sim, face)), Assert.IsType<HewSession>(sim.World.Get<WorkSession>(me)).Chunks);
        Play(sim, me, Hew);
        var hewn = sim.World.Query<Thing>().Where(e => sim.World.Get<Thing>(e) is { Spawned: true } t && t.Cell == face && t.Def.Name == "IronOre")
            .Sum(e => sim.World.Get<Stack>(e).Count);
        Assert.Equal(dug + 1, hewn);
        return 0;
    });

    [Fact]
    public void OffTheCrackHardTheArmsAreJarred() => WithMiner(() =>
    {
        var (sim, me) = Miner();
        HewDrill.Stage(sim, me);
        var s = (HewSession)sim.World.Get<WorkSession>(me).Clone();
        // Walk the point as far from the crack as it goes, then swing hard and let go.
        var away = s.Crack > 0 ? -1 : 1;
        var inputs = new List<WorkInput> { new(0, WorkInputKind.Axis, away), new(60, WorkInputKind.Axis, 0), new(60, WorkInputKind.Hold, 1), new(100, WorkInputKind.Hold, 0) };
        s.Advance(inputs, 101);
        Assert.Equal(HewBlow.Rang, s.LastBlow);
        Assert.True(s.Jarred > 0);
        Assert.Equal(0, s.ChunksDone);
        // Jarred, the next swing comes up slower.
        var before = s.Raised;
        s.Advance([new WorkInput(101, WorkInputKind.Hold, 1)], 111);
        Assert.True(s.Raised - before < HewSession.RaisePerStep * 10 * 0.5f);
        return 0;
    });

    [Fact]
    public void AHewingUnderWaySurvivesASave() => WithMiner(() =>
    {
        var (sim, me) = Miner();
        HewDrill.Stage(sim, me);
        Play(sim, me, Hew, maxTicks: 90);
        var live = (HewSession)sim.World.Get<WorkSession>(me);
        var loaded = SaveLoadTests.RoundTrip(sim);
        var again = Assert.IsType<HewSession>(loaded.World.Get<WorkSession>(me));
        Assert.Equal(live.Cell, again.Cell);
        Assert.Equal(live.Chunks, again.Chunks);
        Assert.Equal(live.ChunksDone, again.ChunksDone);
        Assert.Equal(live.Crack, again.Crack);
        Assert.Equal(live.Face, again.Face);
        return 0;
    });

    /// <summary>Iron ore in the smithy's chest.</summary>
    private static int OreInTheSmithy(Simulation sim)
    {
        var smithy = ShopSystem.BuildingOf(sim, sim.Defs.Building("Smithy"));
        var chest = Buildings.Parts(sim, smithy).First(e => sim.World.Get<Thing>(e).Def.Name == "Chest");
        return sim.StoredIn(chest).Where(e => sim.World.Get<Thing>(e).Def.Name == "IronOre").Sum(e => sim.World.Get<Stack>(e).Count);
    }

    [Fact]
    public void WithAPlayerTheMinerTheCarterBringsLessOre() => WithMiner(() =>
    {
        var defs = MinerDefs();
        var load = defs.Season.OreDelivery.Single(t => t.Def == "IronOre").Count;
        var without = Round(defs, (Bea, "Blacksmith"));
        var before = OreInTheSmithy(without);
        OreCarterSystem.Deliver(without);
        Assert.Equal(before + load, OreInTheSmithy(without));

        var with = Round(defs, (Arin, "Miner"), (Bea, "Blacksmith"));
        before = OreInTheSmithy(with);
        OreCarterSystem.Deliver(with);
        Assert.Equal(before + (int)MathF.Round(load * defs.Season.SuppliedShare), OreInTheSmithy(with));
        return 0;
    });
}

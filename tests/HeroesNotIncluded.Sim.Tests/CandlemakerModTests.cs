using Candlemaker;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Modding;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>The candlemaker (mods/candlemaker), a player's trade built from the ModKit: turned on here, as a player would.</summary>
[Collection(nameof(SimContentTests))]
public class CandlemakerModTests
{
    private static readonly Guid Arin = new("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Bea = new("00000000-0000-0000-0000-00000000000b");

    private static DefDatabase CandleDefs() =>
        DefDatabase.LoadDirectories([.. TestWorld.DefDirectories(), Path.Combine(TestWorld.FindDefsDirectory(), "..", "..", "mods", "candlemaker", "defs")]);

    private static T WithCandlemaker<T>(Func<T> test)
    {
        try
        {
            SimContent.Use([.. TestContent.Standard, ("candlemaker", typeof(CandlemakerMod).Assembly)]);
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

    /// <summary>A candle dipped by F at these steps, the last one finishing it.</summary>
    private static DipSession Dipped(params int[] steps)
    {
        var session = new DipSession();
        session.Advance([.. steps.Select(s => new WorkInput(s, WorkInputKind.Press))], steps.Max() + 1);
        return session;
    }

    private static int[] Every(int gap) => [.. Enumerable.Range(0, DipSession.Layers).Select(i => i * gap)];

    [Fact]
    public void OnlyAPlayerWhoAskedIsTheCandlemaker() => WithCandlemaker(() =>
    {
        var defs = CandleDefs();
        Assert.True(defs.Trade("Candlemaker").PlayerOnly);
        var sim = Round(defs, (Arin, null), (Bea, "Candlemaker"));
        var candlemakers = sim.Creatures().Where(e => sim.World.TryGet<Trade>(e, out var t) && t.Def.Name == "Candlemaker").ToList();
        Assert.Equal([Players.CharacterOf(sim, Bea)], candlemakers);
        Assert.False(CandlemakerModule.RodOf(sim, candlemakers[0]).IsNull, "the kit's dipping rod");
        return 0;
    });

    [Fact]
    public void LettingEachLayerSetMakesAGoodCandle()
    {
        var s = Dipped(Every(DipSession.SetSteps));
        Assert.True(s.Done);
        Assert.Equal(DipSession.Layers, s.Dipped);
        Assert.False(s.Lumpy);
        Assert.Equal(1f, s.Quality);
    }

    [Fact]
    public void DippingBeforeTheWaxHasSetMakesItLumpy()
    {
        var s = Dipped(Every(DipSession.SetSteps / 3));
        Assert.True(s.Done);
        Assert.True(s.Lumpy);
        Assert.Equal(DipSession.Layers - 1, s.Lumps); // the first dip, on a bare wick, is always clean
        Assert.True(s.Quality < 0.5f);

        // One dip just short of set is enough.
        var steps = Every(DipSession.SetSteps);
        steps[4] -= 1;
        Assert.Equal(1, Dipped(steps).Lumps);
    }

    [Fact]
    public void TheCandleGoesIntoThePackAndTheSessionSurvivesASave() => WithCandlemaker(() =>
    {
        var defs = CandleDefs();
        foreach (var (gap, def) in new[] { (DipSession.SetSteps, CandlemakerModule.CandleDef), (10, CandlemakerModule.LumpyDef) })
        {
            var sim = Round(defs, (Arin, "Candlemaker"));
            var me = Players.CharacterOf(sim, Arin);
            WorkSystem.Start(sim, me, new DipSession { Rod = CandlemakerModule.RodOf(sim, me) });
            var session = (DipSession)sim.World.Get<WorkSession>(me);

            // Half dipped, saved and loaded: the layers, the lumps and the wax's warmth come back.
            session.Advance([new WorkInput(0, WorkInputKind.Press), new WorkInput(gap, WorkInputKind.Press)], gap + 5);
            var (loaded, _) = Saving.SaveGame.Read(Saving.SaveGame.Write(sim), defs);
            var back = Assert.IsType<DipSession>(loaded.World.Get<WorkSession>(me));
            Assert.Equal((session.Dipped, session.Lumps, session.SinceDip), (back.Dipped, back.Lumps, back.SinceDip));

            var rest = Every(gap).Skip(2).Select(s => new WorkInput(s, WorkInputKind.Press)).ToList();
            session.Advance([.. rest], rest[^1].Step + 1);
            Assert.True(session.Done);
            session.Finish(sim);
            Assert.Single(Inventory.CarriedOf(sim, me, defs.Thing(def)));
        }
        return 0;
    });

    [Fact]
    public void ThePredictorsCopyLeavesTheOriginalAlone()
    {
        var s = Dipped(0);
        var copy = (DipSession)s.Clone();
        copy.Advance([new WorkInput(copy.StepsDone, WorkInputKind.Press)], copy.StepsDone + 1);
        Assert.Equal(2, copy.Dipped);
        Assert.Equal((1, 0), (s.Dipped, s.Lumps));
    }
}

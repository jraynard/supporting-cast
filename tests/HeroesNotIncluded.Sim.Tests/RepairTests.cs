using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Bots;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Tinkering;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>What crises break: the bridge comes down and cuts the road; the tinker braces it whole again.</summary>
public class RepairTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    private static (Simulation Sim, Entity Me) Round(string trade)
    {
        var sim = VillageRound.NewSimulation(TestWorld.LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default, []));
        sim.ApplyNow(new SetTradePreferencesCommand(Ana, [sim.Defs.Trade(trade)], []));
        sim.ApplyNow(new StartRoundCommand());
        return (sim, Players.CharacterOf(sim, Ana));
    }

    /// <summary>The bridge's two ends: its cells furthest apart along its longer side (Millbrook's runs north to south).</summary>
    private static (int3 A, int3 B) Ends(Simulation sim)
    {
        var w = sim.World;
        var span = w.Query<Thing>().Select(e => w.Get<Thing>(e)).Where(t => t.Def.Name == "StoneBridge").Select(t => t.Cell).ToList();
        var alongX = span.Max(c => c.X) - span.Min(c => c.X) >= span.Max(c => c.Y) - span.Min(c => c.Y);
        return alongX ? (span.MinBy(c => c.X)!, span.MaxBy(c => c.X)!) : (span.MinBy(c => c.Y)!, span.MaxBy(c => c.Y)!);
    }

    [Fact]
    public void NobodyStepsOffTheBridgesSides()
    {
        var (sim, me) = Round("Tinker");
        var w = sim.World;
        var map = sim.Map;
        var span = w.Query<Thing>().Select(e => w.Get<Thing>(e)).Where(t => t.Def.Name == "StoneBridge").Select(t => t.Cell).ToHashSet();
        var (a, b) = Ends(sim);
        var alongX = a.Y == b.Y || Math.Abs(b.X - a.X) > Math.Abs(b.Y - a.Y);
        int Along(int3 c) => alongX ? c.X : c.Y;
        var (lo, hi) = (Math.Min(Along(a), Along(b)), Math.Max(Along(a), Along(b)));

        // Walking links leave the deck only off its ends.
        Span<Narsil.Sim.Grid.NavGraph.Step> steps = stackalloc Narsil.Sim.Grid.NavGraph.Step[Narsil.Sim.Grid.NavGraph.MaxSteps];
        foreach (var c in span)
        {
            var n = Narsil.Sim.Grid.NavGraph.Neighbors(new Maps.MapNav(map), map.Idx(c), steps);
            for (var k = 0; k < n; k++)
            {
                var to = map.CellOf(steps[k].Index);
                Assert.True(span.Contains(to) || Along(to) < lo || Along(to) > hi, $"{c} links off the side to {to}");
            }
        }

        // Nor can a co-op client claim to be standing off a side, on any level, anywhere along it.
        HeroesNotIncluded.Sim.Combat.FreeMoveSystem.Ensure(sim, me);
        var across = alongX ? new int3(0, 1, 0) : new int3(1, 0, 0);
        foreach (var c in span)
            foreach (var side in new[] { across, new int3(-across.X, -across.Y, 0) })
            {
                var beside = c + side;
                if (span.Contains(beside) || span.Contains(beside with { Z = beside.Z - 1 }) || span.Contains(beside with { Z = beside.Z + 1 }))
                    continue;
                foreach (var dz in new[] { -1, 0, 1 })
                {
                    w.Get<Position>(me) = Position.At(c);
                    HeroesNotIncluded.Sim.Combat.FreeMoveSystem.Claim(sim, me, beside.X + 0.5f, beside.Y + 0.5f, c.Z + dz, stopped: true);
                    Assert.True(span.Contains(w.Get<Position>(me).Cell), $"from {c}, claimed off the side to {w.Get<Position>(me).Cell}");
                }
            }
    }

    private static bool Across(Simulation sim, Entity who, int3 a, int3 b) => Navigation.CanReach(sim, who, a, b, PathEnd.OnTile);

    private static void Play(Simulation sim, Entity me, int maxTicks = 60 * 180)
    {
        var predictor = new WorkPredictor();
        var stone = -1;
        for (var t = 0; t < maxTicks && sim.World.Has<WorkSession>(me); t++)
        {
            sim.Tick();
            var (press, axis) = (false, 0);
            if (predictor.Session is SpanSession s && s.AtStone != stone && MathF.Abs(s.At * (SpanSession.Stones - 1) - s.AtStone) < 0.1f)
            {
                stone = s.AtStone;
                var o = s.Offsets[stone];
                (press, axis) = o < -s.BandWidth * 0.5f ? (true, 0) : o > s.BandWidth * 0.5f ? (false, -1) : (false, 0);
            }
            predictor.Update(sim, me, 1, press, false, axis, c => sim.ApplyNow(c));
        }
        sim.Tick();
        Assert.False(sim.World.Has<WorkSession>(me), "the session should have ended");
    }

    [Fact]
    public void ABrokenBridgeCutsTheRoadAndKeepsTheCarterAway()
    {
        var (sim, me) = Round("Tinker");
        var (a, b) = Ends(sim);
        Assert.True(Across(sim, me, a, b));

        var breach = sim.Repairs.BreakBridge(sim, new SimRandom(3));
        Assert.NotNull(breach);
        Assert.NotEmpty(breach.Cells);
        Assert.True(sim.Repairs.RoadCut);
        sim.RebuildDerivedIfNeeded();
        Assert.False(Across(sim, me, a, b), "nobody walks over a gap");
        Assert.Null(sim.Repairs.BreakBridge(sim, new SimRandom(4))); // it's down already

        // The next morning the carter can't get across.
        var dread = sim.Reckoning.Dread;
        sim.Clock.Tick = sim.Round.TickAt(sim, 2, Shops.InnSupplySystem.DeliveryHour) - 1;
        sim.TickMany(SimClock.TicksPerHour);
        Assert.Contains(sim.Messages.Messages, m => m.Text.StartsWith("The carter couldn't get over the river"));
        Assert.True(sim.Reckoning.Dread > dread, "a cut road weighs on the valley");
    }

    [Fact]
    public void TheTinkerBracesTheSpanAndTheRoadIsOpenAgain()
    {
        var (sim, me) = Round("Tinker");
        var (a, b) = Ends(sim);
        var breach = sim.Repairs.BreakBridge(sim, new SimRandom(3))!;
        var gap = breach.Cells[0].Cell;
        var stand = sim.SpiralFrom(gap, 2).First(c => c != gap && sim.Map.Passable(c) && sim.Map.Standable(sim.Map.Idx(c)) && AvatarSystem.InReach(c, gap));
        sim.World.Get<Position>(me).Cell = stand;
        sim.World.Get<Position>(me).Pos = Position.CenterOf(stand);

        // Without wood, it says so.
        while (Wagons.WoodOn(sim, me) > 0)
            Wagons.UseWood(sim, me, Wagons.WoodOn(sim, me));
        Assert.NotNull(Repairs.MendProblem(sim, me, breach));
        Inventory.Stow(sim, me, sim.CreateItem(sim.Thing("Wood"), 3));
        Assert.Null(Repairs.MendProblem(sim, me, breach));
        Assert.Equal(AvatarActionKind.Mend, AvatarSystem.ActionAt(sim, me, gap).Kind);

        sim.ApplyNow(new MendBreachCommand(me, breach.Id));
        Assert.IsType<SpanSession>(sim.World.Get<WorkSession>(me));
        Play(sim, me);
        Assert.False(sim.Repairs.RoadCut);
        Assert.Equal(1, Wagons.WoodOn(sim, me));
        sim.RebuildDerivedIfNeeded();
        Assert.True(Across(sim, me, a, b), "the way across is open again");
    }

    [Fact]
    public void OnlyTheTinkerMendsTheTown()
    {
        var (sim, me) = Round("Healer");
        var breach = sim.Repairs.BreakBridge(sim, new SimRandom(3))!;
        Assert.Equal("mending the town is the tinker's work", Repairs.MendProblem(sim, me, breach));
    }

    [Fact]
    public void AnAITinkerMendsTheBridge()
    {
        var (sim, _) = Round("Healer");
        sim.Repairs.BreakBridge(sim, new SimRandom(3));
        for (var i = 0; i < 24 && sim.Repairs.RoadCut; i++)
            sim.TickMany(SimClock.TicksPerHour / 4);
        Assert.False(sim.Repairs.RoadCut, "the town's tinker went out and mended it");
    }

    [Fact]
    public void ABotTinkerFetchesWoodAndBracesTheSpan()
    {
        var (sim, me) = Round("Tinker");
        var bot = new BotPlayer(() => sim, Ana, c => sim.ApplyNow(c), 9);
        sim.Repairs.BreakBridge(sim, new SimRandom(3));
        for (var t = 0; t < SimClock.TicksPerHour * 4 && sim.Repairs.RoadCut; t++)
        {
            sim.Tick();
            bot.Tick();
        }
        Assert.False(sim.Repairs.RoadCut, $"bot {bot.Doing}");
    }

    [Fact]
    public void ABreachSurvivesASave()
    {
        var (sim, _) = Round("Tinker");
        var breach = sim.Repairs.BreakBridge(sim, new SimRandom(3))!;
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        var again = Assert.Single(loaded.Repairs.Breaches);
        Assert.Equal(breach.Cells, again.Cells);
        Assert.Equal(breach.Seed, again.Seed);
    }
}

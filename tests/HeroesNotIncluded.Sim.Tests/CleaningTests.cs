using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Hospitality;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// The mouse in the cleaning games (<see cref="WorkInputKind.Point"/>): the wash's brush goes where
/// it points, and a stain is rubbed out by moving over it (<see cref="ScrubSession"/>), never by
/// standing on it.
/// </summary>
public class CleaningTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    /// <summary>
    /// The button held and the pointer dragged back and forth along each row of the floor in turn,
    /// <paramref name="passes"/> times over, an input a step from <paramref name="from"/>.
    /// </summary>
    public static List<WorkInput> Rubbing(int passes, int from = 0)
    {
        var inputs = new List<WorkInput> { new(from, WorkInputKind.Hold, 1) };
        var step = from + 1;
        for (var p = 0; p < passes; p++)
            for (var row = 0; row < ScrubSession.GridH; row++)
            {
                var v = (row + 0.5f) / ScrubSession.GridH;
                for (var i = 0; i <= 20; i++)
                {
                    var u = i <= 10 ? i / 10f : (20 - i) / 10f; // across and back
                    inputs.Add(new WorkInput(step++, WorkInputKind.Point, WorkSession.PackPoint(u, v)));
                }
            }
        return inputs;
    }

    private static (Simulation Sim, Entity Me) Tavern()
    {
        var sim = VillageRound.NewSimulation(TestWorld.LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default, []));
        sim.ApplyNow(new SetTradePreferencesCommand(Ana, [sim.Defs.Trade("Innkeeper")], []));
        sim.ApplyNow(new StartRoundCommand());
        return (sim, Players.CharacterOf(sim, Ana));
    }

    /// <summary>A spill in the common room, the player beside it with a mop, scrubbing it.</summary>
    private static (Simulation Sim, Entity Me, int3 Spot, ScrubSession Scrub) Spill()
    {
        var (sim, me) = Tavern();
        var inn = ShopSystem.BuildingOf(sim, sim.Defs.Building("Inn"));
        var b = sim.World.Get<PresetBuilding>(inn);
        var spot = Buildings.CellOf(sim.Map, b.Def, b.Anchor, b.Facing, 6, 10);
        var by = Buildings.CellOf(sim.Map, b.Def, b.Anchor, b.Facing, 6, 9);
        sim.World.Get<Position>(me).Cell = by;
        sim.World.Get<Position>(me).Pos = Position.CenterOf(by);
        Toolbelt.PutOn(sim, me, sim.CreateItem(sim.Thing("Mop"), 1));
        sim.Mess.Spill(sim, MessKind.SpilledAle, spot);
        sim.ApplyNow(new ScrubCommand(me, spot));
        return (sim, me, spot, Assert.IsType<ScrubSession>(sim.World.Get<WorkSession>(me)));
    }

    private static (int X, int Y) Darkest(ScrubSession s)
    {
        var (bx, by, most) = (0, 0, -1f);
        for (var y = 0; y < ScrubSession.GridH; y++)
            for (var x = 0; x < ScrubSession.GridW; x++)
                if (s.BlotAt(x, y) > most)
                    (bx, by, most) = (x, y, s.BlotAt(x, y));
        return (bx, by);
    }

    [Fact]
    public void HoldingStillOverAStainDoesntCleanItButRubbingDoes()
    {
        var (_, _, _, s) = Spill();
        var start = s.Amount;
        Assert.True(start > 0f);
        var (bx, by) = Darkest(s);
        var at = WorkSession.PackPoint((bx + 0.5f) / ScrubSession.GridW, (by + 0.5f) / ScrubSession.GridH);
        // Pointed at its darkest, the button down, and held there.
        s.Advance([new WorkInput(0, WorkInputKind.Point, at), new WorkInput(1, WorkInputKind.Hold, 1), new WorkInput(2, WorkInputKind.Point, at)], 300);
        Assert.Equal(start, s.Amount, 4);
        // Dragged about with the button up: the mop's off the floor.
        var up = s.Clone();
        up.Advance([new WorkInput(300, WorkInputKind.Hold, 0), .. Rubbing(1, 301).Skip(1)], 600);
        Assert.Equal(start, ((ScrubSession)up).Amount, 4);
        // Rubbed back and forth over it: it lifts.
        s.Advance(Rubbing(1, 300), 600);
        Assert.True(s.Amount < start * 0.8f, $"{start} -> {s.Amount}");
    }

    [Fact]
    public void DraggingWithTheMouseRubsAStainOut()
    {
        var (sim, me, spot, s) = Spill();
        s.Advance(Rubbing(8), 2000);
        Assert.True(s.Done, $"left {s.Amount}");
        sim.Tick();
        Assert.Null(sim.Mess.At(sim, spot));
    }

    [Fact]
    public void TheKeysSweepTheMopAlongARowAndBetweenRows()
    {
        var (_, _, _, s) = Spill();
        var start = s.Amount;
        // D along the row from the middle to the right edge: across a third of the floor and more.
        s.Advance([new WorkInput(0, WorkInputKind.Axis, 1)], 20);
        Assert.Equal(1f, s.MopX, 3);
        Assert.Equal(0.5f, s.MopY, 3);
        // Then up a row or two with W.
        s.Advance([new WorkInput(20, WorkInputKind.Axis, 0), new WorkInput(20, WorkInputKind.Axis2, -1), new WorkInput(25, WorkInputKind.Axis2, 0)], 30);
        Assert.True(s.MopY < 0.4f);
        Assert.True(s.Amount <= start);
    }

    [Fact]
    public void ThePointerMovesTheWashingBrushAndHeldItScrubsAPieceClean()
    {
        var (sim, me) = Tavern();
        var inn = ShopSystem.BuildingOf(sim, sim.Defs.Building("Inn"));
        var tub = Buildings.Parts(sim, inn).First(e => BarWork.IsTub(sim, e));
        var cell = sim.SpiralFrom(sim.World.Get<Thing>(tub).Cell, 2).First(c => sim.Map.Passable(c) && AvatarSystem.InReach(sim, c, tub));
        sim.World.Get<Position>(me).Cell = cell;
        sim.World.Get<Position>(me).Pos = Position.CenterOf(cell);
        sim.World.Get<Carrying>(me).Item = sim.CreateItem(sim.Thing("DirtyTankard"), 1);
        sim.ApplyNow(new BarWorkCommand(me, tub));
        var s = Assert.IsType<WashSession>(sim.World.Get<WorkSession>(me));
        s.Advance([new WorkInput(0, WorkInputKind.Point, WorkSession.PackPoint(0.25f, 0.75f))], 1);
        Assert.Equal(0.25f, s.BrushX, 2);
        Assert.Equal(0.75f, s.BrushY, 2);

        // Played as a player would, through the predictor: point at the grime, button held.
        var predictor = new WorkPredictor();
        for (var t = 0; t < 60 * 120 && sim.World.Has<WorkSession>(me); t++)
        {
            sim.Tick();
            var point = -1;
            if (predictor.Session is WashSession w)
            {
                var (gx, gy, most) = (0, 0, 0f);
                for (var y = 0; y < WashSession.GridH; y++)
                    for (var x = 0; x < WashSession.GridW; x++)
                        if (w.GrimeAt(x, y) > most)
                            (gx, gy, most) = (x, y, w.GrimeAt(x, y));
                point = WorkSession.PackPoint((gx + 0.5f) / WashSession.GridW, (gy + 0.5f) / WashSession.GridH);
            }
            predictor.Update(sim, me, 1, false, true, 0, c => sim.ApplyNow(c), point: point);
        }
        sim.Tick();
        Assert.True(BarWork.DirtyOn(sim, me).IsNull, "washed");
        Assert.False(Vessels.CleanOn(sim, me, "Tankard").IsNull);
    }

    [Fact]
    public void APredictedScrubAndTheSimsReplayOfItAgree()
    {
        var (sim, me, _, _) = Spill();
        var predictor = new WorkPredictor();
        var script = Rubbing(3);
        var step = 0;
        var compared = 0;
        var start = 0f;
        ScrubSession? last = null;
        for (var t = 0; t < 600 && sim.World.Has<WorkSession>(me); t++)
        {
            sim.Tick();
            // The script's input for this step, as the game would send it: the pointer's place, the button.
            var point = script.LastOrDefault(i => i.Kind == WorkInputKind.Point && i.Step <= step, new WorkInput(0, WorkInputKind.Point, -1)).Value;
            predictor.Update(sim, me, 1, false, true, 0, c => sim.ApplyNow(c), point: point);
            step++;
            if (predictor.Session is ScrubSession seen)
                last = seen;
            if (t == 0 && last != null)
                start = last.Amount;
            var real = sim.World.TryGet<WorkSession>(me, out var r) ? r as ScrubSession : null;
            if (real != null && last != null && real.StepsDone == last.StepsDone)
            {
                for (var y = 0; y < ScrubSession.GridH; y++)
                    for (var x = 0; x < ScrubSession.GridW; x++)
                        Assert.Equal(last.BlotAt(x, y), real.BlotAt(x, y));
                Assert.Equal(last.MopX, real.MopX);
                Assert.Equal(last.Strokes, real.Strokes);
                compared++;
            }
        }
        Assert.True(compared > 10, $"compared {compared} times");
        Assert.True(last!.Amount < start, $"{start} -> {last.Amount}: it was rubbed");
    }
}

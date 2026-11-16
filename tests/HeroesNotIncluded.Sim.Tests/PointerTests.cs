using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Tinkering;
using HeroesNotIncluded.Sim.Work;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// The mouse in the mini-games beyond the cleaning ones: a click is F unless the game minds where
/// (<see cref="WorkInputKind.Click"/>), a tool is pulled toward where it points at its own pace and
/// never jumped there (so what's felt in passing is still felt, and still sets things off), a game
/// steered against a wander turns as far as the mouse moves, and the predicted play agrees with the sim's.
/// </summary>
public class PointerTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    private static T Ready<T>(T s, int tremorLevel = 5) where T : WorkSession
    {
        s.Rand = new SimRandom(3);
        s.Hands = new Hands(1f, 1f, tremorLevel);
        s.Begin();
        return s;
    }

    private static WorkInput Point(int step, float u, float v = 0.5f) => new(step, WorkInputKind.Point, WorkSession.PackPoint(u, v));

    private static WorkInput Click(int step, float u, float v = 0.5f) => new(step, WorkInputKind.Click, WorkSession.PackPoint(u, v));

    private static WorkInput ClickOff(int step) => new(step, WorkInputKind.Click, WorkSession.OffPicture);

    [Fact]
    public void AClickInAGameThatDoesntMindWhereIsF()
    {
        var pressed = Ready(new StrikerSession { Game = "Striker" });
        var clicked = Ready(new StrikerSession { Game = "Striker" });
        pressed.Advance([new WorkInput(30, WorkInputKind.Press)], 31);
        clicked.Advance([Click(30, 0.2f, 0.8f)], 31);
        Assert.True(pressed.Flight > 0, "F swings the mallet");
        Assert.Equal(pressed.Flight, clicked.Flight);
        var off = Ready(new StrikerSession { Game = "Striker" });
        off.Advance([ClickOff(30)], 31);
        Assert.Equal(pressed.Flight, off.Flight);
    }

    /// <summary>A strongbox in the pack and lockpicks, a probe begun on it.</summary>
    private static (Simulation Sim, Entity Me, Entity Box) Probing(bool trapped)
    {
        var sim = Create(40, 40);
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), "Wright", C(10, 10), ageDays: 25 * 365);
        sim.Round.OpenLobby();
        sim.Round.Sit(Ana, "Wright", null, null, null);
        sim.Round.Start(sim);
        sim.GiveStartingPack(me);
        Inventory.Stow(sim, me, sim.CreateItem(sim.Thing("Lockpicks"), 1));
        var box = Lockwork.Make(sim, new SimRandom(7), 3f, 30, "the Gloam Wood");
        ref var b = ref sim.World.Get<Strongbox>(box);
        b.Armed = trapped;
        b.Trap = trapped ? TrapKind.Blade : TrapKind.None;
        b.TrapAt = 0.5f;
        Inventory.Stow(sim, me, box);
        sim.ApplyNow(new ProbeBoxCommand(me, box));
        return (sim, me, box);
    }

    [Fact]
    public void ThePointerPullsTheProbeAtItsOwnPaceAndATriggerSweptStillSprings()
    {
        var (sim, me, _) = Probing(trapped: true);
        var s = (ProbeSession)sim.World.Get<WorkSession>(me).Clone();
        var from = s.Probe;
        // Pointed at the hinge end with the button held: pressing in as it goes, never a jump.
        s.Advance([new WorkInput(0, WorkInputKind.Hold, 1), Point(0, 1f)], 1);
        Assert.InRange(s.Probe - from, 0f, ProbeSession.ProbePerStep + 1e-5f);
        s.Advance([], 60 * 60);
        Assert.True(s.Sprung, "swept over the trigger with weight on it");
    }

    [Fact]
    public void PointedAtTheFarEndTheProbeStopsShortTillToldItsDone()
    {
        var (sim, me, _) = Probing(trapped: false);
        var s = (ProbeSession)sim.World.Get<WorkSession>(me).Clone();
        s.Advance([Point(0, 1f)], 60 * 30);
        Assert.False(s.Done, "the mouse alone mustn't finish the search");
        Assert.InRange(s.Probe, ProbeSession.MouseReach - ProbeSession.ProbePerStep, ProbeSession.MouseReach + ProbeSession.ProbePerStep);
        s.Advance([new WorkInput(s.StepsDone, WorkInputKind.Act)], s.StepsDone + 60 * 5);
        Assert.True(s.Done);
    }

    [Fact]
    public void AKeyTakesThePlaceOfThePointerTillItMovesAgain()
    {
        var (sim, me, _) = Probing(trapped: false);
        var s = (ProbeSession)sim.World.Get<WorkSession>(me).Clone();
        s.Advance([Point(0, 0.8f), new WorkInput(10, WorkInputKind.Axis, -1), new WorkInput(20, WorkInputKind.Axis, 0)], 30);
        Assert.Null(s.PointU);
        var at = s.Probe;
        s.Advance([], 90);
        Assert.Equal(at, s.Probe); // let go of the key, nothing pulls it on
    }

    [Fact]
    public void TheBladeTurnsAsFarAsTheMouseMovesNotToWhereItPoints()
    {
        var s = Ready(new SharpenSession { StartDullness = 0.5f }, tremorLevel: 20);
        var start = s.Angle;
        s.Advance([Point(0, 0.9f)], 1); // the first place says nothing: no move yet
        var drifted = s.Angle;
        Assert.True(MathF.Abs(drifted - start) < 0.01f);
        s.Advance([Point(1, 0.92f)], 2);
        Assert.True(s.Angle - drifted is > 0.01f and < 0.03f, $"moved {s.Angle - drifted}");
        s.Advance([Point(2, 0.1f)], 3);
        Assert.True(drifted + 0.02f - s.Angle <= SharpenSession.MouseTurn + 0.01f, "a fling turns it no further than a wrist");
    }

    [Fact]
    public void AClickOnASpokePicksItAndOffTheWheelSpinsIt()
    {
        var s = Ready(new TrueSession { Seed = 11 });
        Assert.Equal(0, s.Selected);
        // Straight down from the hub, with the wheel unturned, is a quarter turn round: spoke 2 of 8.
        s.Advance([Click(0, 0.5f, 0.82f)], 1);
        Assert.Equal(2, s.Selected);
        Assert.False(s.Spinning);
        s.Advance([], 120);
        s.Advance([ClickOff(s.StepsDone)], s.StepsDone + 1);
        Assert.True(s.Spinning);
    }

    [Fact]
    public void AClickedStoneHasTheLevelCarriedThereAndAgainWedgesIt()
    {
        var s = Ready(new SpanSession { Seed = 5 });
        var before = s.Offsets[4];
        s.Advance([Click(0, 4.5f / SpanSession.Stones)], 600);
        Assert.Equal(4, s.AtStone);
        Assert.Equal(before, s.Offsets[4]);
        s.Advance([Click(s.StepsDone, 4.5f / SpanSession.Stones)], s.StepsDone + 2);
        Assert.True(s.Offsets[4] > before || s.Done, "a click on the stone it's on drives a wedge in");
    }

    [Fact]
    public void AClickOnAHalfOfTheDoughPushesFromThatSide()
    {
        var s = Ready(new KneadSession());
        Assert.Equal(-1, s.NextSide);
        s.Advance([Click(0, 0.2f)], 1);
        Assert.Equal(1, s.Pushed);
        Assert.Equal(1, s.NextSide);
        s.Advance([Click(1, 0.8f)], 2);
        Assert.Equal(2, s.Pushed);
    }

    [Fact]
    public void WhereTheMousePointedIsSavedWithTheSession()
    {
        var (sim, me, _) = Probing(trapped: false);
        var s = sim.World.Get<WorkSession>(me);
        s.Advance([Point(0, 0.6f, 0.3f)], 1);
        using var stream = new MemoryStream();
        using (var w = new SaveWriter(stream, sim.Defs))
            WorkSession.Write(w, s);
        stream.Position = 0;
        using var r = new SaveReader(stream, sim.Defs, version: SaveGame.FormatVersion);
        var read = WorkSession.Read(r);
        Assert.Equal(s.PointU, read.PointU);
        Assert.Equal(s.PointV, read.PointV);
    }

    [Fact]
    public void APredictedProbeByMouseAndTheSimsReplayOfItAgree()
    {
        var (sim, me, _) = Probing(trapped: true);
        var predictor = new WorkPredictor();
        for (var t = 0; t < 60 * 20 && sim.World.Has<WorkSession>(me); t++)
        {
            sim.Tick();
            var u = Math.Min(1f, t / 600f); // the mouse drawn slowly along the seam, the button down partway
            predictor.Update(sim, me, 1, false, t is > 200 and < 260, 0, c => sim.ApplyNow(c), point: WorkSession.PackPoint(u, 0.5f),
                click: t == 100 ? WorkSession.PackPoint(u, 0.5f) : -1);
        }
        var local = (ProbeSession)predictor.Session!;
        for (var t = 0; t < 120 && sim.World.Has<WorkSession>(me); t++)
        {
            sim.Tick();
            predictor.Update(sim, me, 0, false, false, 0, c => sim.ApplyNow(c));
        }
        if (sim.World.TryGet<WorkSession>(me, out var real))
        {
            var p = (ProbeSession)real;
            Assert.Equal(local.StepsDone, p.StepsDone);
            Assert.Equal((local.Probe, local.Pressure, local.Sprung), (p.Probe, p.Pressure, p.Sprung));
        }
        else
            Assert.True(local.Done || local.Failed != null, "the sim ended a session the copy hadn't");
    }
}

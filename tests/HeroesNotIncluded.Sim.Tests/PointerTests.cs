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

    /// <summary>How far, on average, a blow strays from a joint the cleaver's carried to: struck at once, or once the hand's settled.</summary>
    private static float Stray(bool settle)
    {
        var total = 0f;
        for (var seed = 1u; seed <= 40; seed++)
        {
            var s = new ButcherSession { Rand = new SimRandom(seed), Hands = new Hands(0.6f, 1f, 5) };
            s.Begin();
            var target = ButcherSession.JointsAt[2];
            s.Advance([Point(0, target)], 1);
            while (MathF.Abs(s.Cleaver - target) > Aim.PerStep * 0.5f)
                s.Advance([], s.StepsDone + 1);
            if (settle)
                s.Advance([], s.StepsDone + Aim.SettleSteps);
            s.Advance([new WorkInput(s.StepsDone, WorkInputKind.Press)], s.StepsDone + 1);
            total += MathF.Abs(s.LastAt - target);
        }
        return total / 40f;
    }

    [Fact]
    public void AHurriedCleaverGoesWiderThanASettledOne()
    {
        var hurried = Stray(settle: false);
        var settled = Stray(settle: true);
        Assert.True(hurried > settled * 1.8f, $"hurried {hurried}, settled {settled}");
    }

    [Fact]
    public void AClickOnAJointCarriesTheCleaverThereAndBringsItDown()
    {
        var s = Ready(new ButcherSession());
        s.Advance([Click(0, ButcherSession.JointsAt[1])], 1);
        Assert.Equal(0, s.Blows);
        s.Advance([], 200);
        Assert.Equal(1, s.Blows);
        Assert.True(MathF.Abs(s.LastAt - ButcherSession.JointsAt[1]) < 0.1f);
    }

    /// <summary>A bird gripped at the middle of the picture by the mouse, the button held.</summary>
    private static PluckSession Gripped(uint seed = 3)
    {
        var s = new PluckSession { Rand = new SimRandom(seed), Hands = new Hands(1f, 1f, 5) };
        s.Begin();
        s.Advance([new WorkInput(0, WorkInputKind.Hold, 1), Click(0, 0.5f, 0.5f)], 1);
        return s;
    }

    [Fact]
    public void ThePullIsAsHardAsTheBirdsDraggedAndNoQuicker()
    {
        var s = Gripped();
        s.Advance([], 60);
        Assert.True(s.Pull < 0.02f, $"held still on the bird, it barely pulls ({s.Pull})");
        // Flung right out: it tightens no quicker than F held would.
        s.Advance([Point(s.StepsDone, 1f, 0.5f)], s.StepsDone + 10);
        Assert.True(s.Pull <= 10 * PluckSession.PullPerStep * 1.05f + 0.02f, $"pull {s.Pull}");
        // Eased back to a firm drag, it settles there.
        var firm = 0.55f / PluckSession.DragPull;
        s.Advance([Point(s.StepsDone, 0.5f + firm, 0.5f)], s.StepsDone + 60);
        Assert.InRange(s.Pull, 0.5f, 0.6f);
    }

    [Fact]
    public void AGripHeldOnGivesOutAfterAHandful()
    {
        var s = Gripped();
        s.Advance([Point(1, 0.5f + 0.6f / PluckSession.DragPull, 0.5f)], 60 * 20);
        Assert.False(s.Done);
        Assert.True(s.Plucked <= PluckSession.Handful + 0.01f, $"plucked {s.Plucked} on one grip");
        Assert.Equal(0, s.Tears);
        // Let go and take hold again: more comes.
        var before = s.Plucked;
        s.Advance([new WorkInput(s.StepsDone, WorkInputKind.Hold, 0)], s.StepsDone + 2);
        s.Advance([new WorkInput(s.StepsDone, WorkInputKind.Hold, 1), Click(s.StepsDone, 0.5f, 0.5f), Point(s.StepsDone + 1, 0.5f + 0.6f / PluckSession.DragPull, 0.5f)], s.StepsDone + 200);
        Assert.True(s.Plucked > before + 0.05f);
    }

    [Fact]
    public void DraggedPastWhereTheSkinGivesItTears()
    {
        var s = Gripped();
        s.Advance([Point(1, 1f, 1f)], 60 * 3);
        Assert.Equal(1, s.Tears);
        Assert.Equal(0f, s.Pull); // torn loose: the hand has to take hold again
    }

    /// <summary>Pressed in the pot and held, the mouse going round its middle a round every <paramref name="secondsARound"/> (0: held still).</summary>
    private static StewSession Stirred(float secondsARound, int steps = 240)
    {
        var s = Ready(new StewSession());
        var inputs = new List<WorkInput> { new(0, WorkInputKind.Hold, 1), Click(0, 0.8f, 0.5f) };
        for (var t = 1; secondsARound > 0f && t < steps; t++)
        {
            var a = t / (secondsARound * SimClock.TicksPerSecond) * MathF.Tau;
            inputs.Add(Point(t, 0.5f + 0.3f * MathF.Cos(a), 0.5f + 0.3f * MathF.Sin(a)));
        }
        s.Advance(inputs, steps);
        return s;
    }

    [Fact]
    public void CirclingThePotStirsItAndAStillPressDoesnt()
    {
        var round = Stirred(1.5f);
        Assert.True(round.Stirring);
        Assert.Equal(0, round.SinceStir);
        var still = Stirred(0f);
        Assert.False(still.Stirring, "pressed still in the pot isn't stirring");
        Assert.True(still.SinceStir > 200);
        var slow = Stirred(8f);
        Assert.False(slow.Stirring, "a round in eight seconds is hardly stirring");
        // F held is a stir as ever.
        var keys = Ready(new StewSession());
        keys.Advance([new WorkInput(0, WorkInputKind.Hold, 1)], 60);
        Assert.True(keys.Stirring);
    }

    [Fact]
    public void TheWheelFeedsAndBanksTheFire()
    {
        var s = Ready(new StewSession());
        var flame = s.Flame;
        s.Advance([new WorkInput(0, WorkInputKind.Axis2, -1), new WorkInput(8, WorkInputKind.Axis2, 0)], 20);
        Assert.True(s.Flame > flame);
    }

    /// <summary>
    /// A heat's first blows: the button held till the hammer's raised <paramref name="wind"/> past the
    /// mark (0 on it), then let go; pushed <paramref name="push"/> steps off centre with D first. The
    /// piece's shape gained, and the blows' average grade.
    /// </summary>
    private static (float Shape, float Grade) Swung(float precision, float wind, int push = 0, int blows = 4, float shaped = 0f)
    {
        var (sim, me, forge, recipe) = WorkTests.AtTheForge();
        Smithing.PieceOf(sim, me)!.Shape = shaped;
        var real = WorkTests.Start(sim, me, forge, recipe);
        var shape = 0f;
        var grade = 0f;
        const int Heats = 12;
        for (var k = 0; k < Heats; k++)
        {
            var s = (HammerSession)real.Clone();
            s.Rand = new SimRandom((uint)k + 1);
            s.Hands = new Hands(precision, 1f, 5);
            var from = s.Shape;
            for (var blow = 0; blow < blows && !s.Done; blow++)
            {
                while (!s.Done && !s.Ready)
                    s.Advance([], s.StepsDone + 1);
                var top = Math.Clamp(s.Mark + wind, 0f, 1f);
                s.Advance([new WorkInput(s.StepsDone, WorkInputKind.Hold, 1)], s.StepsDone + 1);
                if (push > 0) // steered away from the spot
                {
                    var away = s.Spot > s.Offset ? -1 : 1;
                    s.Advance([new WorkInput(s.StepsDone, WorkInputKind.Axis, away), new WorkInput(s.StepsDone + push, WorkInputKind.Axis, 0)], s.StepsDone + push + 1);
                }
                while (!s.Done && s.Raise < top - HammerSession.WindPerStep * 0.5f && s.Raise < 1f)
                    s.Advance(push > 0 ? [] : [new WorkInput(s.StepsDone, WorkInputKind.Axis, Bots.BotPlayer.HammerCentre(s))], s.StepsDone + 1);
                var before = s.Blows;
                s.Advance([new WorkInput(s.StepsDone, WorkInputKind.Hold, 0), new WorkInput(s.StepsDone, WorkInputKind.Axis, 0)], s.StepsDone + 1);
                while (!s.Done && s.Blows == before && s.Swinging)
                    s.Advance([], s.StepsDone + 1);
                // Over to the next spot.
                while (!s.Done && MathF.Abs(s.Offset - s.Spot) > s.CentreWidth * 0.3f)
                    s.Advance([new WorkInput(s.StepsDone, WorkInputKind.Axis, Math.Sign(s.Spot - s.Offset)), new WorkInput(s.StepsDone + 1, WorkInputKind.Axis, 0)], s.StepsDone + 1);
            }
            shape += s.Shape - from;
            grade += s.Quality;
        }
        return (shape / Heats, grade / Heats);
    }

    [Fact]
    public void RaisedToTheMarkAndLetGoIsAFullBlow()
    {
        var (shape, grade) = Swung(1f, 0f);
        Assert.True(grade > 0.85f, $"grade {grade}");
        Assert.True(shape > 0f);
    }

    [Fact]
    public void AShortWindMovesItLessAndAnOverwoundOneLessStill()
    {
        var full = Swung(1f, 0f);
        var shortWind = Swung(1f, -0.22f);
        var over = Swung(1f, 0.25f);
        Assert.True(shortWind.Shape < full.Shape && shortWind.Shape > full.Shape * 0.5f, $"short {shortWind.Shape}, full {full.Shape}");
        Assert.True(over.Shape > 0f && over.Shape < shortWind.Shape * 0.5f, $"overwound {over.Shape}, short {shortWind.Shape}");
    }

    [Fact]
    public void ABlowOffCentreGlances()
    {
        var centred = Swung(1f, 0f);
        var off = Swung(1f, 0f, push: 40);
        Assert.True(off.Shape < centred.Shape * 0.8f && off.Grade < centred.Grade, $"off {off.Shape}/{off.Grade}, centred {centred.Shape}/{centred.Grade}");
    }

    [Fact]
    public void ARaisedHammerWandersOffThePiece()
    {
        var s = Ready(new HammerSession());
        s.Advance([new WorkInput(0, WorkInputKind.Hold, 1)], 0);
        var wandered = 0f;
        for (var seed = 1u; seed <= 20; seed++)
        {
            var c = (HammerSession)s.Clone();
            c.Rand = new SimRandom(seed);
            c.Hands = new Hands(0.3f, 1f, 5);
            c.Advance([new WorkInput(0, WorkInputKind.Hold, 1)], 36);
            wandered += MathF.Abs(c.Offset);
        }
        Assert.True(wandered / 20f > 0.02f, $"wandered {wandered / 20f}");
    }

    [Fact]
    public void TheMouseMovedSideToSideShiftsTheHammerAcross()
    {
        var s = Ready(new HammerSession());
        s.Advance([Point(0, 0.5f), Point(1, 0.55f)], 2);
        Assert.InRange(s.Offset, 0.09f, 0.11f);
    }

    [Fact]
    public void AnUnsteadyHandsWindWavers()
    {
        float Uneven(float precision)
        {
            var total = 0f;
            for (var seed = 1u; seed <= 20; seed++)
            {
                var s = new HammerSession { Rand = new SimRandom(seed), Hands = new Hands(precision, 1f, 5) };
                s.Begin();
                s.Advance([new WorkInput(0, WorkInputKind.Hold, 1)], 20);
                total += MathF.Abs(s.Raise - 20 * HammerSession.WindPerStep);
            }
            return total / 20f;
        }
        Assert.Equal(0f, Uneven(1f), 4);
        Assert.True(Uneven(0f) > 0.005f, $"a shaky wind's off by {Uneven(0f)}");
    }

    [Fact]
    public void AnOverwoundBlowNeverUndoesWorkButMovesItOnALittle()
    {
        var full = Swung(1f, 0f, blows: 1, shaped: 0.5f);
        var over = Swung(1f, 0.3f, blows: 1, shaped: 0.5f);
        Assert.True(over.Shape > 0f && over.Shape < full.Shape * 0.3f, $"over {over.Shape}, a full blow {full.Shape}");
    }

    [Fact]
    public void ThePressedButtonOnTheBirdPullsNothingTillItsDragged()
    {
        var s = new PluckSession { Rand = new SimRandom(3), Hands = new Hands(1f, 1f, 5) };
        s.Begin();
        // Pressed on the window's words, off the picture: still a grip, and held still it doesn't pull.
        s.Advance([new WorkInput(0, WorkInputKind.Hold, 1), ClickOff(0)], 240);
        Assert.True(s.Gripping);
        Assert.True(s.Pull < 0.02f && s.Plucked < 0.01f && s.Tears == 0, $"pull {s.Pull}, plucked {s.Plucked}");
    }

    [Fact]
    public void TheMouseMovedLeftDrawsTheFingersBackForTheNextSlice()
    {
        var s = Ready(new ChopSession { Needed = 6 });
        var slice = 1f / ChopSession.MouseDraw;
        s.Advance([Point(0, 0.8f), Point(1, 0.8f - slice)], 2);
        Assert.InRange(s.Bared, 0.95f, 1.05f);
        s.Advance([Click(2, 0.8f - slice)], 3);
        Assert.Equal((1, 0, 1), (s.Cuts, s.Slips, s.LastMark));
        Assert.Equal(0f, s.Bared); // up against the blade again
        s.Advance([Click(3, 0.8f - slice)], 4);
        Assert.Equal(1, s.Slips); // chopped again without drawing back: the fingers
    }
}

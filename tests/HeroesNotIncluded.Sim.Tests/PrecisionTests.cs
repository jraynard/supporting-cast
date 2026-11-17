using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Tinkering;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// The precision games played as a person plays them: a few steps late on everything they see
/// (the screen, the input poll and the hand together). A window the game tells you to hit must be
/// hittable that way at level 0, and its cue must never promise more than the window gives.
/// </summary>
public class PrecisionTests
{
    /// <summary>A new hand: steady, but no skill to widen anything.</summary>
    private static readonly Hands Novice = new(1f, 0.75f, 0);

    private static HammerSession AtTheAnvil(uint seed)
    {
        var (sim, me, forge, recipe) = WorkTests.AtTheForge();
        var s = (HammerSession)WorkTests.Start(sim, me, forge, recipe).Clone();
        s.Rand = new SimRandom(seed);
        s.Hands = Novice;
        return s;
    }

    private static void Step(WorkSession s, params WorkInput[] inputs) => s.Advance(inputs, s.StepsDone + 1);

    /// <summary>Kept over the spot (A/D) a step.</summary>
    private static void Centred(HammerSession s, params WorkInput[] more) =>
        Step(s, [.. more, new WorkInput(s.StepsDone, WorkInputKind.Axis, Bots.BotPlayer.HammerCentre(s))]);

    /// <summary>One blow over the spot: held till the hammer reaches <paramref name="top"/> as seen, let go <paramref name="lag"/> steps later.</summary>
    private static void Blow(HammerSession s, float top, int lag)
    {
        while (!s.Done && (!s.Ready || MathF.Abs(s.Offset - s.Spot) > s.CentreWidth * 0.3f))
            Centred(s);
        Centred(s, new WorkInput(s.StepsDone, WorkInputKind.Hold, 1));
        while (!s.Done && s.Raise < top && s.Raise < 1f)
            Centred(s);
        for (var k = 0; k < lag && !s.Done; k++)
            Centred(s);
        var before = s.Blows;
        Step(s, new WorkInput(s.StepsDone, WorkInputKind.Hold, 0));
        while (!s.Done && s.Blows == before && s.Swinging)
            Step(s);
    }

    [Fact]
    public void LettingGoAMomentAfterTheGhostIsStillOnTheMark()
    {
        var on = 0;
        var blows = 0;
        for (var seed = 1u; seed <= 12; seed++)
        {
            var s = AtTheAnvil(seed);
            for (var b = 0; b < 4 && !s.Done; b++)
            {
                var before = s.Blows;
                Blow(s, s.Mark, lag: 4); // seen level with the ghost; the hand follows ~70 ms later
                if (s.Blows == before)
                    continue;
                blows++;
                if (s.LastWindWas == 0)
                    on++;
            }
        }
        Assert.True(blows >= 30, $"only {blows} blows landed");
        Assert.True(on >= blows * 0.8f, $"{on} of {blows} on the mark");
    }

    [Fact]
    public void TheArmSlowsOnlyNearTheMark()
    {
        var s = AtTheAnvil(3);
        Assert.Equal(HammerSession.WindPerStep, s.WindRate);
        Step(s, new WorkInput(0, WorkInputKind.Hold, 1));
        while (s.Raise < s.Mark - HammerSession.Approach)
            Step(s);
        Assert.Equal(HammerSession.WindPerStep * HammerSession.NearSlow, s.WindRate);
    }

    [Fact]
    public void AHairPastTheMarkIsAHeavyBlowNotAWastedOne()
    {
        float Grade(Func<HammerSession, float> top)
        {
            var total = 0f;
            for (var seed = 1u; seed <= 8; seed++)
            {
                var s = AtTheAnvil(seed);
                Blow(s, top(s), lag: 0);
                total += s.Quality;
            }
            return total / 8f;
        }
        var on = Grade(s => s.Mark);
        var hair = Grade(s => s.BandHigh + s.HalfWidth * 0.3f);
        var far = Grade(s => s.BandHigh + s.HalfWidth * 2.5f);
        Assert.True(hair > far + 0.3f, $"a hair over {hair}, far over {far}");
        Assert.True(hair < on, $"a hair over {hair}, on the mark {on}");
        Assert.True(far < 0.15f, $"far over still poor: {far}");
    }

    private static PickSession Lock(uint seed, int pins = 5)
    {
        var s = new PickSession { Pins = pins, LockSeed = 1000 + seed, Rand = new SimRandom(seed), Hands = Novice };
        s.Begin();
        return s;
    }

    /// <summary>
    /// A picker who reads the lock as drawn and acts <paramref name="lag"/> steps late: keeps the
    /// tension mid-band, lifts till the cue says set it, and backs off if the stiffness fades.
    /// </summary>
    private static void Pick(PickSession s, int lag, int maxSteps = 60 * 120, Func<PickSession, bool>? until = null)
    {
        var queue = new Queue<(bool Press, int Axis, int Axis2)>();
        for (var k = 0; k < lag; k++)
            queue.Enqueue((false, 0, 0));
        int axis = 0, axis2 = 0, cooldown = 0, pin = -1;
        var feelWas = 0f;
        for (var t = 0; t < maxSteps && !s.Done && until?.Invoke(s) != true; t++)
        {
            var mid = (s.Low + s.High) * 0.5f;
            var want2 = s.Tension < mid - 0.03f ? -1 : s.Tension > mid + 0.03f ? 1 : 0;
            var press = false;
            var want = 0;
            if (s.Pin != pin)
                (pin, cooldown, feelWas) = (s.Pin, 0, 0f);
            if (cooldown > 0)
                cooldown--;
            else if (s.InBand && !s.Turning)
            {
                if (s.Feel >= s.SetFeel)
                    (press, cooldown) = (true, lag * 2 + 4);
                else
                    want = s.Feel < feelWas - 0.005f && feelWas > 0.5f ? -1 : 1;
            }
            feelWas = s.Feel;
            queue.Enqueue((press, want, want2));
            var (p, a, a2) = queue.Dequeue();
            var inputs = new List<WorkInput>();
            if (p)
                inputs.Add(new(s.StepsDone, WorkInputKind.Press));
            if (a != axis)
                inputs.Add(new(s.StepsDone, WorkInputKind.Axis, axis = a));
            if (a2 != axis2)
                inputs.Add(new(s.StepsDone, WorkInputKind.Axis2, axis2 = a2));
            s.Advance(inputs, s.StepsDone + 1);
        }
    }

    [Fact]
    public void PressingOnTheCueAlwaysSetsThePin()
    {
        for (var seed = 1u; seed <= 10; seed++)
        {
            var s = Lock(seed, pins: 1);
            Step(s, new WorkInput(0, WorkInputKind.Axis2, -1));
            while (s.Tension < (s.Low + s.High) * 0.5f)
                Step(s);
            Step(s, new WorkInput(s.StepsDone, WorkInputKind.Axis2, 0), new WorkInput(s.StepsDone, WorkInputKind.Axis, 1));
            while (s.Feel < s.SetFeel && s.Slips == 0)
                Step(s);
            Assert.Equal(0, s.Slips);
            Assert.True(s.InBand);
            Step(s, new WorkInput(s.StepsDone, WorkInputKind.Axis, 0), new WorkInput(s.StepsDone, WorkInputKind.Press));
            Assert.True(s.Pin == 1, $"seed {seed}: pressed on the cue (Feel {s.Feel}, set at {s.SetFeel}) and it missed");
        }
    }

    [Fact]
    public void ANoviceALittleLatePicksALockWithoutLosingThePins()
    {
        for (var seed = 1u; seed <= 6; seed++)
        {
            var s = Lock(seed);
            Pick(s, lag: 6);
            Assert.True(s.Opened, $"seed {seed}: not open (pin {s.Pin}, slips {s.Slips})");
            Assert.True(s.DroppedAt < 0, $"seed {seed}: the pins dropped at step {s.DroppedAt}");
        }
    }

    [Fact]
    public void ABriefSlackChattersThePinsALongOneDropsThem()
    {
        foreach (var brief in new[] { true, false })
        {
            // Pick till two are set, then let the tension off.
            var s = Lock(4);
            Pick(s, lag: 0, maxSteps: 60 * 60, until: x => x.Pin >= 2);
            Assert.Equal(2, s.Pin);
            Step(s, new WorkInput(s.StepsDone, WorkInputKind.Axis, 0), new WorkInput(s.StepsDone, WorkInputKind.Axis2, 1));
            while (s.Tension >= s.Low)
                Step(s);
            if (brief)
                Step(s, new WorkInput(s.StepsDone, WorkInputKind.Axis2, 0)); // just under, and held there
            var below = 0;
            var chattered = false;
            while (s.Tension < s.Low && s.Pin == 2 && below < (brief ? PickSession.SlackGrace / 2 : 600))
            {
                chattered |= s.Chattering;
                Step(s);
                below++;
            }
            if (brief)
            {
                Step(s, new WorkInput(s.StepsDone, WorkInputKind.Axis2, -1));
                while (s.Tension < s.Low && s.Pin == 2)
                    Step(s);
                Assert.True(chattered, "slack, the pins chattered");
                Assert.Equal(2, s.Pin);
                Assert.True(s.DroppedAt < 0);
            }
            else
            {
                Assert.Equal(0, s.Pin);
                Assert.True(below >= PickSession.SlackGrace, $"dropped after {below} steps slack");
                Assert.True(s.DroppedAt >= 0);
            }
        }
    }

    [Fact]
    public void ALockChatteringIsSavedChattering()
    {
        var (sim, _, _, _) = WorkTests.AtTheForge();
        var s = Lock(4);
        Pick(s, lag: 0, maxSteps: 60 * 60, until: x => x.Pin >= 2);
        Step(s, new WorkInput(s.StepsDone, WorkInputKind.Axis, 0), new WorkInput(s.StepsDone, WorkInputKind.Axis2, 1));
        while (!s.Chattering)
            Step(s);
        Step(s, new WorkInput(s.StepsDone, WorkInputKind.Axis2, 0));
        using var stream = new MemoryStream();
        using (var w = new SaveWriter(stream, sim.Defs))
            WorkSession.Write(w, s);
        stream.Position = 0;
        using var r = new SaveReader(stream, sim.Defs, version: SaveGame.FormatVersion);
        var read = (PickSession)WorkSession.Read(r);
        Assert.True(read.Chattering);
        // Carried on alike, the two drop their pins on the same step.
        for (var k = 0; k < PickSession.SlackGrace + 5; k++)
        {
            Step(s);
            Step(read);
        }
        Assert.Equal(s.DroppedAt, read.DroppedAt);
        Assert.True(s.DroppedAt >= 0);
    }

    [Fact]
    public void ACrimpAMomentLateIsStillTightButWellLateIsLoose()
    {
        int Tight(int lateSteps)
        {
            var s = new CrimpSession { Rand = new SimRandom(1), Hands = Novice };
            s.Begin();
            var perMark = CrimpSession.Round * SimClock.TicksPerSecond / CrimpSession.Marks;
            var inputs = Enumerable.Range(1, CrimpSession.Marks)
                .Select(k => new WorkInput((int)MathF.Round(k * perMark) + lateSteps, WorkInputKind.Press)).ToList();
            s.Advance(inputs, inputs[^1].Step + 2);
            return s.Crimps.Sum();
        }
        Assert.Equal(CrimpSession.Marks, Tight(0));
        Assert.Equal(CrimpSession.Marks, Tight(5)); // ~85 ms behind every mark
        Assert.Equal(0, Tight(10));
    }
}

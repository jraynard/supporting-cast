using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Work;

/// <summary>How the striker's last blow went, for the panel.</summary>
public enum SledgeResult : byte
{
    None,

    /// <summary>On the smith's call.</summary>
    OnCall,

    /// <summary>Brought down with no call to answer: it knocks the work about.</summary>
    Offbeat,

    /// <summary>The call came and went with the sledge still up (or down).</summary>
    Missed,
}

/// <summary>
/// The striker at the anvil (<see cref="Pairs"/>): heave the sledge up (hold W or F) and keep it
/// there, and when the smith's hammer taps the spot, let go and bring it down on the call. A full
/// heave moves the metal; a short one, less; held up too long, the arms tire and it sags. Too late
/// and the call's gone; with no call at all, the sledge comes down off the beat and knocks the work
/// out of true, and may crack it. How true the smith tapped matters as much: the sledge falls where
/// the hammer showed it. Skill (and steady hands) widen the moment to answer in.
/// </summary>
public sealed class SledgeSession : HelperSession
{
    /// <summary>How far the sledge is heaved up a step while held: up in about three-quarters of a second.</summary>
    public const float HeavePerStep = 0.022f;

    /// <summary>How far it comes down a step once let go.</summary>
    public const float SwingPerStep = 0.09f;

    /// <summary>A full heave: up this far, the sledge comes down with all its weight.</summary>
    public const float FullHeave = 0.8f;

    /// <summary>Under this it's no blow at all.</summary>
    public const float MinHeave = 0.15f;

    /// <summary>Steps it can be held up at the full before the arms tire, and how far it sags a step then.</summary>
    public const int TireAfter = 150;
    public const float SagPerStep = 0.004f;

    /// <summary>Steps a call stands to be answered, at the least practised hands (more with skill).</summary>
    public const int CallSteps = 40;

    /// <summary>The last third of the window still counts, at down to half: late, the metal's begun to set.</summary>
    public const float LateWorth = 0.5f;

    /// <summary>Steps the sledge rests after a blow before it can be heaved again.</summary>
    public const int RecoverySteps = 14;

    private int _wind, _recovery, _atTop;
    private bool _falling;

    /// <summary>The anvil.</summary>
    public Entity Station { get; set; }

    /// <summary>How far it's heaved up, 0 (on the ground) to 1.</summary>
    public float Raise { get; private set; }

    /// <summary>The top of this heave: what the blow is judged by.</summary>
    public float Peak { get; private set; }

    /// <summary>The smith has tapped and the call stands: bring it down now.</summary>
    public bool CallOpen { get; private set; }

    /// <summary>Steps since the call.</summary>
    public int CallAge { get; private set; }

    /// <summary>Blows on the call, off the beat, and calls let go by.</summary>
    public int Strikes { get; private set; }
    public int Offbeats { get; private set; }
    public int Missed { get; private set; }

    public SledgeResult LastWas { get; private set; }
    public float LastGrade { get; private set; }

    /// <summary>The step the last blow landed (or call was missed), -1 before any.</summary>
    public int LastStep { get; private set; } = -1;

    /// <summary>Steps a call stands, for these hands.</summary>
    public int Window => (int)(CallSteps * Hands.Tolerance);

    public bool Swinging => _falling;
    public bool Ready => _recovery <= 0;

    /// <summary>Held up so long the arms have begun to tire.</summary>
    public bool Tiring => _atTop > TireAfter;

    public override Skill Skill => Skill.Crafting;
    public override float Xp => Math.Min(40f, 4f * Strikes);
    public override string Title => $"At the sledge for {LeadName}";

    public override int3? Focus(Simulation sim) => sim.World.IsAlive(Station) && sim.World.TryGet<Thing>(Station, out var t) ? t.Cell : null;

    internal static SledgeSession For(Simulation sim, HammerSession lead) => new() { Station = lead.Station };

    public override bool Follows(WorkSession lead) => lead is HammerSession { Heavy: true } h && h.Station == Station;

    protected override bool LeadAtIt(Simulation sim) => Pairs.AtWork(sim, Partner);

    protected override bool InPlace(Simulation sim) =>
        sim.World.IsAlive(Station) && AvatarSystem.InReach(sim, sim.World.Get<Position>(Worker).Cell, Station);

    /// <summary>W held heaves it up; let go, it comes down.</summary>
    protected override void Axis2(int value) => _wind = value;

    protected override void Received(Beat beat)
    {
        if (beat.Kind != BeatKind.Call)
            return;
        if (CallOpen)
            MissCall(); // a new tap before the last was answered: that one's gone
        (CallOpen, CallAge) = (true, 0);
    }

    private void MissCall()
    {
        CallOpen = false;
        Missed++;
        (LastWas, LastGrade, LastStep) = (SledgeResult.Missed, 0f, StepsDone);
        Send(BeatKind.Missed);
    }

    protected override void Step()
    {
        if (CallOpen && ++CallAge > Window)
            MissCall();
        if (_recovery > 0)
        {
            _recovery--;
            return;
        }
        var raising = (_wind < 0 || Held) && !_falling;
        if (raising)
        {
            // Slower near the top, an unsteady hand wavers; held up too long, it sags.
            var rate = Raise >= FullHeave ? HeavePerStep * 0.3f : HeavePerStep;
            Raise = Math.Clamp(Raise + rate + (Rand.NextFloat() * 2f - 1f) * Hands.Tremor * 0.01f, 0f, 1f);
            if (Raise >= FullHeave)
                _atTop++;
            if (Tiring)
                Raise = Math.Max(0f, Raise - SagPerStep - rate);
            Peak = Tiring ? Raise : Math.Max(Peak, Raise);
        }
        else if (Raise > 0f)
        {
            _falling = true;
            Raise = Math.Max(0f, Raise - SwingPerStep);
            if (Raise <= 0f)
            {
                if (Peak >= MinHeave)
                    Land();
                (Peak, _falling, _atTop) = (0f, false, 0);
            }
        }
    }

    /// <summary>The sledge comes down: on the call, as good as the heave and how soon; with none, off the beat.</summary>
    private void Land()
    {
        var heave = Math.Clamp(Peak / FullHeave, 0f, 1f);
        _recovery = RecoverySteps;
        if (CallOpen)
        {
            var onTime = Window * 2 / 3;
            var timing = CallAge <= onTime ? 1f : 1f - (1f - LateWorth) * (CallAge - onTime) / (float)Math.Max(1, Window - onTime);
            var grade = Math.Clamp(heave * timing * (0.85f + 0.15f * Hands.Precision), 0f, 1f);
            CallOpen = false;
            Strikes++;
            Sample(grade);
            (LastWas, LastGrade, LastStep) = (SledgeResult.OnCall, grade, StepsDone);
            Send(BeatKind.Strike, grade);
        }
        else
        {
            Offbeats++;
            Sample(0f);
            (LastWas, LastGrade, LastStep) = (SledgeResult.Offbeat, 0f, StepsDone);
            Send(BeatKind.Offbeat, heave);
        }
    }

    protected override void WriteState(SaveWriter w)
    {
        base.WriteState(w);
        w.Entity(Station);
        w.Int(_wind);
        w.Int(_recovery);
        w.Int(_atTop);
        w.Bool(_falling);
        w.Float(Raise);
        w.Float(Peak);
        w.Bool(CallOpen);
        w.Int(CallAge);
        w.Int(Strikes);
        w.Int(Offbeats);
        w.Int(Missed);
        w.Byte((byte)LastWas);
        w.Float(LastGrade);
        w.Int(LastStep);
    }

    protected override void ReadState(SaveReader r)
    {
        base.ReadState(r);
        Station = r.Entity();
        _wind = Math.Sign(r.Int());
        _recovery = Math.Max(0, r.Int());
        _atTop = Math.Max(0, r.Int());
        _falling = r.Bool();
        Raise = Math.Clamp(r.Float(), 0f, 1f);
        Peak = Math.Clamp(r.Float(), 0f, 1f);
        CallOpen = r.Bool();
        CallAge = Math.Max(0, r.Int());
        Strikes = r.Int();
        Offbeats = r.Int();
        Missed = r.Int();
        var was = r.Byte();
        LastWas = Enum.IsDefined((SledgeResult)was) ? (SledgeResult)was : SledgeResult.None;
        LastGrade = r.Float();
        LastStep = r.Int();
    }
}

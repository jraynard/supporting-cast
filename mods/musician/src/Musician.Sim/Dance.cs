using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Musician;

/// <summary>
/// A player dancing to a musician (anyone in earshot of music, F where they stand): a step to the side on the beat
/// (A/D), a turn (Space), a clap (F). Steps are kept as they come; whether each was on the beat is the musician's to
/// say, so it's judged in the sim (<see cref="Settle"/>, which may read the world) a tick after, never in the pure
/// step. In step for long enough, it's remembered as a dance. Off when the music stops.
/// </summary>
public sealed class DanceSession : WorkSession
{
    /// <summary>The musician danced to.</summary>
    public Entity Musician;

    /// <summary>A step: when (session steps), and which: -1 to the left, +1 to the right, 0 a turn, 2 a clap.</summary>
    public readonly record struct Move(int Step, int Kind);

    public const int Left = -1, Right = 1, Turn = 0, Clap = 2;

    /// <summary>The steps lately (the oldest beyond <see cref="Kept"/> forgotten), and how many in all.</summary>
    public List<Move> Moves = [];
    public int Moved;

    public const int Kept = 32;

    /// <summary>Steps judged so far (by the sim, against the musician's beat), on it and off it, and the run on it now and best.</summary>
    public int Judged, OnBeat, OffBeat, Streak, BestStreak;

    /// <summary>The music's tempo (for the dancer's hop), and when it was last heard playing (sim tick).</summary>
    public float Tempo;
    public long HeardAt;

    /// <summary>Ticks either side of a beat a step still lands on it.</summary>
    public float Window => 8f * Math.Max(0.75f, Hands.Tolerance);

    /// <summary>Steps on the beat that make it a dance worth remembering.</summary>
    public const int Remembered = 8;

    /// <summary>Ticks the music may stop for before the dance does.</summary>
    public const int Pause = 2 * SimClock.TicksPerSecond;

    public override Skill Skill => Skill.Social;
    public override float Xp => 0f;
    public override string Title => "Dancing";
    public override bool Dancing => true;
    public override float DanceTempo => Tempo;

    /// <summary>The steps danced on the beat lately (of those judged): the dancer's own sense of it.</summary>
    public float InStep => Judged == 0 ? 0f : (float)Streak / Math.Max(1, Math.Min(Judged, 8));

    public override int3? Focus(Simulation sim) =>
        sim.World.IsAlive(Musician) && sim.World.TryGet<Position>(Musician, out var p) ? p.Cell : null;

    private void Moving(int kind)
    {
        Moves.Add(new Move(StepsDone, kind));
        Moved++;
        if (Moves.Count > Kept)
            Moves.RemoveAt(0);
    }

    protected override void Axis(int value)
    {
        if (value != 0)
            Moving(value < 0 ? Left : Right);
    }

    protected override void Act() => Moving(Turn);

    protected override void Press() => Moving(Clap);

    protected override void Step() { }

    protected override void Cloned() => Moves = [.. Moves];

    /// <summary>The music still there to dance to (or only just stopped), and the dancer in earshot of it.</summary>
    public override bool StillValid(Simulation sim)
    {
        var w = sim.World;
        if (!w.IsAlive(Musician) || !w.TryGet<Position>(Musician, out var at) || !w.TryGet<Position>(Worker, out var me))
            return false;
        return sim.Clock.Tick - HeardAt <= Pause && SpeechSystem.Hear(sim, at.Cell, me.Cell, Volume.Say) != null;
    }

    /// <summary>Each step not yet judged, against the musician's beat as it fell then; the music's tempo, while it plays.</summary>
    public override void Settle(Simulation sim)
    {
        if (Performance.Of(sim, Musician) is { Playing: true } p)
            (HeardAt, Tempo) = (sim.Clock.Tick, p.Tempo);
        var first = Moved - Moves.Count;
        for (; Judged < Moved; Judged++)
        {
            if (Judged < first)
                continue; // forgotten before it was judged (a flurry): let it go
            var off = Performance.OffBeat(sim, Musician, StartTick + Moves[Judged - first].Step);
            if (off is { } o && MathF.Abs(o) <= Window)
            {
                OnBeat++;
                Streak++;
                BestStreak = Math.Max(BestStreak, Streak);
            }
            else
            {
                OffBeat++;
                Streak = 0;
            }
        }
    }

    public override void Finish(Simulation sim) { }

    /// <summary>Stopped (the music over, Esc, walked off): enough of it in step is a dance remembered.</summary>
    public override void Interrupted(Simulation sim)
    {
        if (OnBeat >= Remembered)
            MoodSystem.Remember(sim, Worker, sim.Defs.ThoughtOrNull("Danced"));
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Musician);
        w.Int(Moves.Count);
        foreach (var m in Moves)
        {
            w.Int(m.Step);
            w.Int(m.Kind);
        }
        w.Int(Moved);
        w.Int(Judged);
        w.Int(OnBeat);
        w.Int(OffBeat);
        w.Int(Streak);
        w.Int(BestStreak);
        w.Float(Tempo);
        w.Long(HeardAt);
    }

    protected override void ReadState(SaveReader r)
    {
        Musician = r.Entity();
        Moves.Clear();
        for (var n = r.Count(Kept); n > 0; n--)
            Moves.Add(new Move(r.Int(), Math.Clamp(r.Int(), Left, Clap)));
        Moved = Math.Max(r.Int(), Moves.Count);
        Judged = Math.Clamp(r.Int(), 0, Moved);
        OnBeat = r.Int();
        OffBeat = r.Int();
        Streak = r.Int();
        BestStreak = r.Int();
        Tempo = r.Float();
        HeardAt = r.Long();
    }

    /// <summary>The music <paramref name="who"/> can hear clearly enough to dance to (the nearest), or Entity.Null.</summary>
    public static Entity MusicHeard(Simulation sim, Entity who)
    {
        var w = sim.World;
        if (!w.TryGet<Position>(who, out var me))
            return Entity.Null;
        var (best, distance) = (Entity.Null, int.MaxValue);
        foreach (var e in w.Each<WorkSession>())
            if (e != who && w.Get<WorkSession>(e) is PlaySession s && Performance.Of(s).Playing && w.TryGet<Position>(e, out var at)
                && SpeechSystem.Hear(sim, at.Cell, me.Cell, Volume.Say) == Clarity.Clear && HeroesNotIncluded.Sim.Maps.Map.Distance(at.Cell, me.Cell) is var d && d < distance)
                (best, distance) = (e, d);
        return best;
    }
}

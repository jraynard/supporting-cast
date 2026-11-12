using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Tinkering;

/// <summary>
/// Truing a buckled wagon wheel. The wheel turns slowly past a gauge, and the gauge's needle swings
/// out where the rim bulges and in where it dips. F tightens the spoke passing the gauge, pulling a
/// bulge in; A slackens it, letting a dip out. Work the wrong way and the wobble gets worse; get
/// every spoke within the band and the wheel runs true. Neighbouring spokes pull a little too, so
/// it's never quite one at a time.
/// </summary>
public sealed class TrueSession : WorkSession
{
    public const int Spokes = 8;

    /// <summary>Seconds for one turn of the wheel.</summary>
    public const float TurnSeconds = 4f;

    /// <summary>How far one turn of the spoke key moves the rim at that spoke, and at the ones beside it.</summary>
    public const float Pull = 0.06f, PullBeside = 0.015f;

    /// <summary>Within this everywhere (before skill widens it), the wheel's true.</summary>
    public const float Band = 0.035f;

    public Entity Wagon { get; set; } = Entity.Null;
    public uint Seed { get; set; }

    /// <summary>How far the rim stands out (+) or in (−) at each spoke.</summary>
    public float[] Offsets { get; private set; } = [];

    /// <summary>The wheel's turn, 0..1, from where spoke 0 is at the gauge.</summary>
    public float Turn { get; private set; }

    /// <summary>What the gauge reads now: the rim at the gauge, with the hand's shake in it.</summary>
    public float Gauge { get; private set; }

    public int Turns { get; private set; }
    public bool Trued { get; private set; }

    private int _key;

    public float BandWidth => Band * Hands.Tolerance;

    /// <summary>The spoke passing the gauge now.</summary>
    public int AtGauge => (int)MathF.Round(Turn * Spokes) % Spokes;

    /// <summary>The worst the rim stands out or in anywhere.</summary>
    public float Wobble => Offsets.Length == 0 ? 0f : Offsets.Max(MathF.Abs);

    public override Skill Skill => Skill.Crafting;
    public override float Xp => Trued ? 50f : 15f;
    public override string Title => "Truing a wagon wheel";

    public override int3? Focus(Simulation sim) => sim.World.IsAlive(Wagon) && sim.World.TryGet<Thing>(Wagon, out var t) ? t.Cell : null;

    public override bool StillValid(Simulation sim) => Wagons.WheelProblem(sim, Worker, Wagon) == null;

    /// <summary>The buckle in a wheel of this seed: the same every time it's looked at, until it's trued.</summary>
    public static float[] Buckle(uint seed)
    {
        var rand = new SimRandom(seed);
        var o = new float[Spokes];
        for (var i = 0; i < Spokes; i++)
            o[i] = rand.Range(-0.3f, 0.3f);
        // Always properly out somewhere.
        var worst = 0;
        for (var i = 1; i < Spokes; i++)
            if (MathF.Abs(o[i]) > MathF.Abs(o[worst]))
                worst = i;
        o[worst] = MathF.Sign(o[worst] == 0 ? 1 : o[worst]) * 0.32f;
        return o;
    }

    protected internal override void Begin()
    {
        Offsets = Buckle(Seed);
    }

    protected override void Cloned()
    {
        Offsets = (float[])Offsets.Clone();
    }

    protected override void Axis(int value)
    {
        if (value < 0)
            _key = -1;
    }

    protected override void Press() => _key = 1;

    protected override void Step()
    {
        if (Done)
            return;
        if (_key != 0)
        {
            Work(_key);
            _key = 0;
            if (Wobble <= BandWidth)
            {
                Trued = true;
                Done = true;
                return;
            }
        }
        Turn += 1f / (TurnSeconds * SimClock.TicksPerSecond);
        if (Turn >= 1f)
        {
            Turn -= 1f;
            Turns++;
        }
        // The rim between two spokes runs between their offsets.
        var at = Turn * Spokes;
        var i = (int)MathF.Floor(at) % Spokes;
        var f = at - MathF.Floor(at);
        var rim = Offsets[i] * (1f - f) + Offsets[(i + 1) % Spokes] * f;
        Gauge = rim + (Rand.NextFloat() * 2f - 1f) * 0.012f * (1f + Hands.Tremor * 3f);
    }

    /// <summary>A turn of the spoke key on the spoke at the gauge: +1 tightens (pulls a bulge in), −1 slackens.</summary>
    private void Work(int way)
    {
        var s = AtGauge;
        var was = MathF.Abs(Offsets[s]);
        var pull = Pull * (1f + (Rand.NextFloat() * 2f - 1f) * (0.2f + Hands.Tremor));
        Offsets[s] -= way * pull;
        Offsets[(s + 1) % Spokes] -= way * PullBeside;
        Offsets[(s + Spokes - 1) % Spokes] -= way * PullBeside;
        Sample(MathF.Abs(Offsets[s]) < was ? 1f : 0f);
    }

    public override void Finish(Simulation sim)
    {
        if (Trued)
            Wagons.WheelTrued(sim, Worker, Wagon);
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Wagon);
        w.UInt(Seed);
        foreach (var o in Offsets)
            w.Float(o);
        w.Float(Turn);
        w.Float(Gauge);
        w.Int(Turns);
        w.Bool(Trued);
        w.Int(_key);
    }

    protected override void ReadState(SaveReader r)
    {
        Wagon = r.Entity();
        Seed = r.UInt();
        Offsets = new float[Spokes];
        for (var i = 0; i < Spokes; i++)
            Offsets[i] = r.Float();
        Turn = r.Float();
        Gauge = r.Float();
        Turns = r.Int();
        Trued = r.Bool();
        _key = Math.Clamp(r.Int(), -1, 1);
    }
}

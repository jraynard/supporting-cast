using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Tinkering;

/// <summary>
/// Truing a buckled wagon wheel. The wheel stands still on its stand; A/D turn it a spoke at a time
/// to bring one to the spoke key. W tightens that spoke, pulling a bulge in; S slackens it, letting
/// a dip out. F spins the wheel once past the gauge, which chalks how far the rim stands out or in
/// at every spoke; the chalk is what you saw, not what's there after you've worked a spoke since.
/// Neighbouring spokes pull a little too. Spin it with every spoke within the band and it runs
/// true. (The wheel used to turn on its own, so each turn of the key was a timing test against a
/// moving needle; a playtester wanted to work one spoke at a time and spin to see.)
/// </summary>
public sealed class TrueSession : WorkSession
{
    public const int Spokes = 8;

    /// <summary>Seconds for a spin, and for the wheel to be turned on by one spoke.</summary>
    public const float SpinSeconds = 2.5f, StepSeconds = 0.18f;

    /// <summary>How far one turn of the spoke key moves the rim at that spoke, and at the ones beside it.</summary>
    public const float Pull = 0.04f, PullBeside = 0.01f;

    /// <summary>Within this everywhere (before skill widens it), the wheel's true.</summary>
    public const float Band = 0.04f;

    public Entity Wagon { get; set; } = Entity.Null;
    public uint Seed { get; set; }

    /// <summary>How far the rim stands out (+) or in (−) at each spoke.</summary>
    public float[] Offsets { get; private set; } = [];

    /// <summary>The spoke at the key (and at the gauge, the wheel at rest).</summary>
    public int Selected { get; private set; }

    /// <summary>The wheel's turn, 0..1, from where spoke 0 is at the key: for drawing it.</summary>
    public float Turn { get; private set; }

    /// <summary>Spinning: how far round the spin has come, 0..1.</summary>
    public bool Spinning { get; private set; }
    public float Spun { get; private set; }

    /// <summary>What the gauge reads now, mid-spin: the rim passing it, with the hand's shake in it.</summary>
    public float Gauge { get; private set; }

    /// <summary>What the last spin chalked at each spoke (only where <see cref="Chalked"/>).</summary>
    public float[] Chalk { get; private set; } = new float[Spokes];
    public bool[] Chalked { get; private set; } = new bool[Spokes];

    /// <summary>Turns of the key at each spoke since it was chalked: + tightened, − slackened. What your hands remember.</summary>
    public int[] Worked { get; private set; } = new int[Spokes];

    /// <summary>Spins made, and turns of the key (for the sounds).</summary>
    public int Turns { get; private set; }
    public int Keyed { get; private set; }

    public bool Trued { get; private set; }

    private int _key, _way;

    public float BandWidth => Band * Hands.Tolerance;

    /// <summary>The spoke passing the gauge now.</summary>
    public int AtGauge => (int)MathF.Round(Turn * Spokes) % Spokes;

    /// <summary>The worst the rim stands out or in anywhere.</summary>
    public float Wobble => Offsets.Length == 0 ? 0f : Offsets.Max(MathF.Abs);

    /// <summary>Where the chalk at a spoke says the rim runs now, reckoning a key's turn as a full <see cref="Pull"/>: what a player works out from the chalk.</summary>
    public static float Reckoned(TrueSession s, int spoke) => s.Chalk[spoke] - s.Worked[spoke] * Pull;

    /// <summary>The chalked spoke reckoned furthest outside <paramref name="mark"/>, or −1 if none (the bot's and the tests' way of reading the chalk).</summary>
    public static int Worst(TrueSession s, float mark)
    {
        var worst = -1;
        var most = mark;
        for (var i = 0; i < Spokes; i++)
            if (s.Chalked[i] && MathF.Abs(Reckoned(s, i)) > most)
                (worst, most) = (i, MathF.Abs(Reckoned(s, i)));
        return worst;
    }

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
        Chalk = (float[])Chalk.Clone();
        Chalked = (bool[])Chalked.Clone();
        Worked = (int[])Worked.Clone();
    }

    /// <summary>A/D: on to the next spoke at once, and on again each time the wheel settles while it's held.</summary>
    protected override void Axis(int value)
    {
        _way = value;
        if (value != 0 && !Spinning)
            Selected = (Selected + value + Spokes) % Spokes;
    }

    /// <summary>W tightens, S slackens: one turn of the key a press.</summary>
    protected override void Axis2(int value)
    {
        if (value != 0)
            _key = -value;
    }

    /// <summary>
    /// The picture's square round the wheel, as a share of the wheel's radius each way from its hub:
    /// a click inside the rim picks the spoke it's on (turned to the key), anywhere else spins it.
    /// </summary>
    public const float PickedOut = 1.25f;

    protected override void Clicked(float? u, float? v)
    {
        if (Spinning)
            return;
        if (u is { } x && v is { } y)
        {
            float dx = x - 0.5f, dy = y - 0.5f;
            if (dx * dx + dy * dy <= 0.5f / PickedOut * (0.5f / PickedOut) * 1.1f)
            {
                // On screen a spoke stands at (i / Spokes - Turn) of a turn (y down, as it's drawn).
                var a = DetMath.Atan2(dy, dx) / MathF.Tau;
                Selected = ((int)MathF.Round((a + Turn) * Spokes) % Spokes + Spokes) % Spokes;
                return;
            }
        }
        Press();
    }

    /// <summary>F: spin it past the gauge.</summary>
    protected override void Press()
    {
        if (Spinning)
            return;
        Spinning = true;
        Spun = 0f;
        Turn = Selected / (float)Spokes;
    }

    protected override void Step()
    {
        if (Done)
            return;
        if (Spinning)
        {
            _key = 0;
            SpinOn();
            return;
        }
        if (_key != 0)
        {
            Work(_key);
            _key = 0;
        }
        // Turned on toward the spoke chosen, the short way round; settled, a held key turns it on again.
        var target = Selected / (float)Spokes;
        var d = target - Turn;
        d -= MathF.Round(d);
        var speed = 1f / (Spokes * StepSeconds * SimClock.TicksPerSecond);
        if (MathF.Abs(d) <= speed)
        {
            Turn = target;
            if (_way != 0)
                Selected = (Selected + _way + Spokes) % Spokes;
        }
        else
            Turn = Wrap(Turn + MathF.Sign(d) * speed);
    }

    private void SpinOn()
    {
        var before = Spun * Spokes;
        Spun = Math.Min(1f, Spun + 1f / (SpinSeconds * SimClock.TicksPerSecond));
        var now = Spun * Spokes;
        Turn = Wrap((Selected + now) / Spokes);
        // The rim between two spokes runs between their offsets.
        var at = Selected + now;
        var i = (int)MathF.Floor(at) % Spokes;
        var f = at - MathF.Floor(at);
        var rim = Offsets[i] * (1f - f) + Offsets[(i + 1) % Spokes] * f;
        var shake = 0.008f * (1f + Hands.Tremor * 3f);
        Gauge = rim + (Rand.NextFloat() * 2f - 1f) * shake;
        // Chalk each spoke as it passes (the one it started at, as it comes back round).
        for (var k = (int)MathF.Floor(before) + 1; k <= (int)MathF.Floor(now); k++)
        {
            var s = (Selected + k) % Spokes;
            Chalk[s] = Offsets[s] + (Rand.NextFloat() * 2f - 1f) * shake;
            Chalked[s] = true;
            Worked[s] = 0;
        }
        if (Spun < 1f)
            return;
        Spinning = false;
        Turns++;
        Turn = Selected / (float)Spokes;
        if (Wobble <= BandWidth)
        {
            Trued = true;
            Done = true;
        }
    }

    private static float Wrap(float t) => t - MathF.Floor(t);

    /// <summary>A turn of the spoke key on the spoke at the key: +1 tightens (pulls a bulge in), −1 slackens.</summary>
    private void Work(int way)
    {
        var s = Selected;
        var was = MathF.Abs(Offsets[s]);
        var pull = Pull * (1f + (Rand.NextFloat() * 2f - 1f) * (0.1f + Hands.Tremor));
        Offsets[s] -= way * pull;
        Offsets[(s + 1) % Spokes] -= way * PullBeside;
        Offsets[(s + Spokes - 1) % Spokes] -= way * PullBeside;
        Worked[s] = Math.Clamp(Worked[s] + way, -99, 99);
        Keyed++;
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
        w.Int(Selected);
        w.Bool(Spinning);
        w.Float(Spun);
        for (var i = 0; i < Spokes; i++)
        {
            w.Float(Chalk[i]);
            w.Bool(Chalked[i]);
            w.Int(Worked[i]);
        }
        w.Int(Keyed);
        w.Int(_way);
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
        if (r.Version < 39)
        {
            // A wheel from when it turned on its own: brought to rest at the spoke at the gauge, nothing chalked.
            Selected = (int)MathF.Round(Wrap(Turn) * Spokes) % Spokes;
            Turn = Selected / (float)Spokes;
            return;
        }
        Selected = Math.Clamp(r.Int(), 0, Spokes - 1);
        Spinning = r.Bool();
        Spun = Math.Clamp(r.Float(), 0f, 1f);
        for (var i = 0; i < Spokes; i++)
        {
            Chalk[i] = r.Float();
            Chalked[i] = r.Bool();
            Worked[i] = r.Int();
        }
        Keyed = r.Int();
        _way = Math.Clamp(r.Int(), -1, 1);
    }
}

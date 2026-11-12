using SupportingCast.Sim.Components;
using SupportingCast.Sim.Saving;
using SupportingCast.Sim.Work;

namespace SupportingCast.Sim.Healing;

/// <summary>
/// Meditating mana back: finding the way inward. Each sitting is a labyrinth grown fresh from the
/// session's seed, entered at a corner, with the still centre of the self at its heart. WASD walk
/// its passages; only what's near is clear, and what's been seen stays dimly remembered. Hold F to
/// stop and widen what you see. Every new place reached gathers a little mana, and the centre a
/// great deal (the sooner, the more), which ends the sitting. Pain narrows the sight, makes the
/// steps heavy, and now and then scatters the thoughts, throwing you back along your path. A
/// meditation stone in reach eases all of it and gathers more.
/// </summary>
public sealed class MeditateSession : WorkSession
{
    /// <summary>The labyrinth's size, in places each way (odd, so it has a centre).</summary>
    public const int Size = 9;

    /// <summary>How long a sitting lasts, in steps.</summary>
    public const int MaxSteps = 30 * 60;

    /// <summary>Steps to walk from one place to the next, untroubled.</summary>
    public const int StepSteps = 9;

    /// <summary>How far one sees untroubled (in places), and how much further holding still.</summary>
    public const float Sight = 2.6f, FocusSight = 1.6f;

    /// <summary>Mana for each new place reached, and for the centre, as shares of the most they hold.</summary>
    public const float GainPerPlace = 0.008f, GainAtCentre = 0.6f;

    /// <summary>Openings out of a place, as bits.</summary>
    public const byte North = 1, East = 2, South = 4, West = 8;

    /// <summary>The pain carried when the sitting began, 0..1.</summary>
    public float Pain { get; set; }

    /// <summary>
    /// How much a meditation stone in reach helps (<see cref="Defs.BuildingProps.Meditation"/>),
    /// 0 for none: pain troubles the mind that much less, and each place gathers that much more.
    /// </summary>
    public float Stillness { get; set; }

    /// <summary>The pain that troubles the mind, after the stone's help.</summary>
    public float FeltPain => Pain * (1f - Stillness);

    public float MaxMana { get; set; }

    /// <summary>Room for mana when the sitting began.</summary>
    public float Room { get; set; }

    public float Gained { get; private set; }

    /// <summary>The labyrinth: the openings out of each place (<see cref="North"/> and so on), row by row.</summary>
    public byte[] Open { get; private set; } = new byte[Size * Size];

    /// <summary>Places seen (dimly remembered once out of sight) and places walked.</summary>
    public bool[] Seen { get; private set; } = new bool[Size * Size];
    public bool[] Walked { get; private set; } = new bool[Size * Size];

    /// <summary>Where they are, and the way they came (for a scattered thought to throw them back along).</summary>
    public int X { get; private set; }
    public int Y { get; private set; }
    public List<int> Trail { get; private set; } = [];

    /// <summary>Reached the still centre.</summary>
    public bool Centred { get; private set; }

    /// <summary>The step a scattered thought last threw them back, for the scene.</summary>
    public int ScatteredAt { get; private set; } = int.MinValue / 2;

    private int _dx, _dy, _ready;

    public static int Centre => Size / 2;
    public static int Index(int x, int y) => y * Size + x;

    /// <summary>How far they see now: less in pain, more holding still, more by the stone.</summary>
    public float SightNow => (Sight * (1f - 0.55f * FeltPain) + (Held ? FocusSight : 0f)) * (1f + 0.3f * Stillness);

    /// <summary>Steps a move takes now: heavier in pain.</summary>
    public int StepLength => (int)(StepSteps * (1f + 2.5f * FeltPain));

    public bool Scattered => StepsDone - ScatteredAt < 30;

    public override Skill Skill => Skill.Medicine;
    public override float Xp => 15f;
    public override string Title => "Meditating";

    public static string? Problem(Simulation sim, Entity who)
    {
        if (!sim.World.TryGet<Mana>(who, out var mana))
            return "you haven't the gift";
        if (mana.Current >= mana.Max - 0.5f)
            return "your mana is full";
        return null;
    }

    public static void TryStart(Simulation sim, Entity who)
    {
        if (Problem(sim, who) is { } why)
        {
            sim.Messages.Add(MessageKind.Info, $"Can't meditate: {why}.", sim.LocationOf(who));
            return;
        }
        var mana = sim.World.Get<Mana>(who);
        var pain = sim.World.TryGet<Health>(who, out var h) ? Math.Clamp(h.Pain, 0f, 1f) : 0f;
        WorkSystem.Start(sim, who, new MeditateSession
        {
            Pain = pain, MaxMana = mana.Max, Room = mana.Max - mana.Current, Stillness = StillnessAt(sim, who),
        });
    }

    /// <summary>The help of the best meditation stone within reach (0 if none).</summary>
    public static float StillnessAt(Simulation sim, Entity who)
    {
        var at = sim.World.Get<Position>(who).Cell;
        var best = 0f;
        foreach (var e in sim.World.Each<Thing>())
        {
            var t = sim.World.Get<Thing>(e);
            if (t.Spawned && t.Def.Building is { Meditation: > 0 } b && !sim.World.Has<Construction>(e) && Systems.AvatarSystem.InReach(sim, at, e))
                best = Math.Max(best, Math.Clamp(b.Meditation, 0f, 0.9f));
        }
        return best;
    }

    public override bool StillValid(Simulation sim) => sim.World.Has<Mana>(Worker);

    protected internal override void Begin()
    {
        Grow();
        var corner = Rand.NextInt(4);
        (X, Y) = (corner % 2 == 0 ? 0 : Size - 1, corner < 2 ? 0 : Size - 1);
        Trail.Add(Index(X, Y));
        Walked[Index(X, Y)] = true;
        Look();
    }

    /// <summary>A labyrinth grown by wandering from the centre and backing up at dead ends: every place reachable, one way between any two.</summary>
    private void Grow()
    {
        var stack = new List<int> { Index(Centre, Centre) };
        var grown = new bool[Size * Size];
        grown[stack[0]] = true;
        Span<int> ways = stackalloc int[4];
        while (stack.Count > 0)
        {
            var here = stack[^1];
            int x = here % Size, y = here / Size, n = 0;
            for (var d = 0; d < 4; d++)
            {
                var (nx, ny) = Step(x, y, d);
                if (nx is >= 0 and < Size && ny is >= 0 and < Size && !grown[Index(nx, ny)])
                    ways[n++] = d;
            }
            if (n == 0)
            {
                stack.RemoveAt(stack.Count - 1);
                continue;
            }
            var way = ways[Rand.NextInt(n)];
            var (tx, ty) = Step(x, y, way);
            var there = Index(tx, ty);
            Open[here] |= Bit(way);
            Open[there] |= Bit((way + 2) % 4);
            grown[there] = true;
            stack.Add(there);
        }
    }

    /// <summary>Directions 0..3: north, east, south, west.</summary>
    public static (int X, int Y) Step(int x, int y, int dir) => dir switch
    {
        0 => (x, y - 1),
        1 => (x + 1, y),
        2 => (x, y + 1),
        _ => (x - 1, y),
    };

    public static byte Bit(int dir) => (byte)(1 << dir);

    public bool CanGo(int x, int y, int dir) => (Open[Index(x, y)] & Bit(dir)) != 0;

    protected override void Cloned()
    {
        Open = (byte[])Open.Clone();
        Seen = (bool[])Seen.Clone();
        Walked = (bool[])Walked.Clone();
        Trail = [.. Trail];
    }

    protected override void Axis(int value) => _dx = value;
    protected override void Axis2(int value) => _dy = value;

    /// <summary>Everything within sight is seen (and stays remembered).</summary>
    private void Look()
    {
        var r = SightNow;
        for (var y = 0; y < Size; y++)
            for (var x = 0; x < Size; x++)
                if ((x - X) * (x - X) + (y - Y) * (y - Y) <= r * r)
                    Seen[Index(x, y)] = true;
    }

    protected override void Step()
    {
        // A scattered thought throws them a few places back the way they came.
        if (FeltPain > 0 && Trail.Count > 1 && Rand.Chance(0.0035f * FeltPain))
        {
            for (var i = 0; i < 3 && Trail.Count > 1; i++)
                Trail.RemoveAt(Trail.Count - 1);
            (X, Y) = (Trail[^1] % Size, Trail[^1] / Size);
            ScatteredAt = StepsDone;
            _ready = StepsDone + StepLength * 2;
        }
        if (!Held && StepsDone >= _ready && (_dx != 0 || _dy != 0))
        {
            // Across first if that way's open, else up or down.
            int? dir = _dx > 0 && CanGo(X, Y, 1) ? 1 : _dx < 0 && CanGo(X, Y, 3) ? 3
                : _dy < 0 && CanGo(X, Y, 0) ? 0 : _dy > 0 && CanGo(X, Y, 2) ? 2 : null;
            if (dir is { } d)
            {
                (X, Y) = Step(X, Y, d);
                var here = Index(X, Y);
                // Walking back the way you came takes the trail back up.
                if (Trail.Count > 1 && Trail[^2] == here)
                    Trail.RemoveAt(Trail.Count - 1);
                else
                    Trail.Add(here);
                if (!Walked[here])
                {
                    Walked[here] = true;
                    Gain(MaxMana * GainPerPlace * (1f + Stillness));
                    Sample(1f);
                }
                _ready = StepsDone + StepLength;
                if (X == Centre && Y == Centre)
                {
                    // The sooner, the more.
                    Gain(MaxMana * GainAtCentre * (1f + Stillness) * (1f - StepsDone / (float)MaxSteps));
                    Centred = true;
                    Done = true;
                }
            }
        }
        Look();
        if (StepsDone + 1 >= MaxSteps || Gained >= Room - 0.01f)
            Done = true;
    }

    private void Gain(float mana) => Gained = Math.Min(Room, Gained + mana);

    public override void Finish(Simulation sim)
    {
        ref var mana = ref sim.World.Get<Mana>(Worker);
        mana.Current = Math.Min(mana.Max, mana.Current + Gained);
        var at = sim.LocationOf(Worker);
        if (Centred)
            sim.Messages.Add(MessageKind.Good, $"You find the still centre of yourself, and come back with {Gained:F0} mana.", at);
        else
            sim.Messages.Add(Gained >= Room * 0.5f ? MessageKind.Good : MessageKind.Info, Pain > 0.4f && Gained < Room * 0.5f
                ? $"The pain keeps scattering your thoughts. You come back with {Gained:F0} mana."
                : $"You come back to yourself with {Gained:F0} mana.", at);
    }

    // ---------------- For a careful meditator (bots) ----------------

    /// <summary>
    /// The way someone who remembers what they've seen would go next: toward the centre by the
    /// shortest way through what they know, guessing unseen places open. -1 if there's nowhere to go.
    /// </summary>
    public int Heading()
    {
        var goal = Index(Centre, Centre);
        var from = new int[Size * Size];
        Array.Fill(from, -1);
        var start = Index(X, Y);
        from[start] = start;
        var queue = new Queue<int>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            var here = queue.Dequeue();
            if (here == goal)
                break;
            int x = here % Size, y = here / Size;
            for (var d = 0; d < 4; d++)
            {
                var (nx, ny) = Step(x, y, d);
                if (nx is < 0 or >= Size || ny is < 0 or >= Size)
                    continue;
                var next = Index(nx, ny);
                // A seen place's walls are known; between two unseen ones, guess it's open.
                var known = Seen[here] || Seen[next];
                if (from[next] >= 0 || (known && !CanGo(x, y, d)))
                    continue;
                from[next] = here;
                queue.Enqueue(next);
            }
        }
        if (from[goal] < 0)
            return -1;
        var step = goal;
        while (from[step] != start)
            step = from[step];
        for (var d = 0; d < 4; d++)
            if (Step(X, Y, d) is var (sx, sy) && Index(sx, sy) == step)
                return d;
        return -1;
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Float(Pain);
        w.Float(Stillness);
        w.Float(MaxMana);
        w.Float(Room);
        w.Float(Gained);
        for (var i = 0; i < Size * Size; i++)
        {
            w.Byte(Open[i]);
            w.Bool(Seen[i]);
            w.Bool(Walked[i]);
        }
        w.Int(X);
        w.Int(Y);
        w.Int(Trail.Count);
        foreach (var t in Trail)
            w.Int(t);
        w.Bool(Centred);
        w.Int(ScatteredAt);
        w.Int(_dx);
        w.Int(_dy);
        w.Int(_ready);
    }

    protected override void ReadState(SaveReader r)
    {
        Pain = r.Float();
        Stillness = r.Float();
        MaxMana = r.Float();
        Room = r.Float();
        if (r.Version < 6)
        {
            // A sitting from before the labyrinth (a breathing rhythm): it ends with what it had.
            r.Int(); // calm
            Gained = r.Float();
            r.Int(); // turns
            r.Bool(); // inhaling
            r.Int(); // next turn
            r.Int(); // half start
            r.Int(); // flinch until
            r.Bool(); // answered
            Done = true;
            return;
        }
        Gained = r.Float();
        for (var i = 0; i < Size * Size; i++)
        {
            Open[i] = r.Byte();
            Seen[i] = r.Bool();
            Walked[i] = r.Bool();
        }
        X = Math.Clamp(r.Int(), 0, Size - 1);
        Y = Math.Clamp(r.Int(), 0, Size - 1);
        for (var n = r.Count(Size * Size * 4); n > 0; n--)
            Trail.Add(Math.Clamp(r.Int(), 0, Size * Size - 1));
        if (Trail.Count == 0)
            Trail.Add(Index(X, Y));
        Centred = r.Bool();
        ScatteredAt = r.Int();
        _dx = Math.Sign(r.Int());
        _dy = Math.Sign(r.Int());
        _ready = r.Int();
    }
}

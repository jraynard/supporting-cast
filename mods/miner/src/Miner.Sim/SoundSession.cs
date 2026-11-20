using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Miner;

/// <summary>
/// Sounding the rock (the miner's second hand game; a first pass, to be found by playing it): tapping a stretch of
/// wall with a light hammer and listening for where a seam lies behind it, before cutting in. Calm: nothing to fail,
/// only time spent.
/// <list type="bullet">
/// <item>The wall in front, five spots across it: the face and two to either side (<see cref="Spots"/>), each a
/// cell of the wall, tapped or not as it looks (open ground isn't rock).</item>
/// <item>A/D (or the mouse) picks a spot; F (or a click) taps it. Sound rock rings back clear and long; ore behind it
/// deadens the note, more the nearer it lies (<see cref="DepthWeight"/>: the wall's own cell and three behind).</item>
/// <item>Every tap is heard through the hand's shake (<see cref="Noise"/>), so one tap can mislead: tap again and
/// the run of them tells true. Each spot keeps its last few taps (<see cref="RecentAt"/>), shown as they were heard,
/// not summed up: reading the spread is the skill, with patience and comparing spots.</item>
/// <item>Space chalks the spot (a seam, nothing, or wiped): the miner's own claim, seen by everyone
/// (<see cref="ChalkMarks"/>), left on the wall when the sounding's put down.</item>
/// </list>
/// What lies behind each spot (<c>_dead</c>) is read once at the start and kept private: nothing shown reads it,
/// only each tap's note (<see cref="LastHeard"/>), heard as anyone would, noise and all.
/// </summary>
public sealed class SoundSession : WorkSession
{
    /// <summary>Spots across the wall: the face and two either side.</summary>
    public const int Spots = 5;

    /// <summary>Steps between taps: a tap's note dies away before the next.</summary>
    public const int TapSteps = 18;

    /// <summary>How much ore in each cell back from the wall deadens the note: the wall's own cell, then one, two and three behind.</summary>
    public static readonly float[] DepthWeight = [1f, 0.75f, 0.5f, 0.3f];

    /// <summary>
    /// How far one tap's note can stray from the truth (0 ring to 1 dead), at most: a new miner's ear (<see cref="BaseNoise"/>,
    /// steadied by Mining skill through <see cref="Hands.Tolerance"/>), more for a shaky hand. Most taps stray far less (two
    /// draws summed, so a wild one is rare): with ore a cell behind, about one tap in ten misleads a new miner, three rarely.
    /// </summary>
    public const float BaseNoise = 1.0f, TremorNoise = 0.8f;

    /// <summary>The taps each spot keeps, as they were heard.</summary>
    public const int Kept = 5;

    /// <summary>The face (the middle spot).</summary>
    public int3 Face { get; set; }

    /// <summary>A step to the right along the wall, as the miner faces it; and a step into the rock.</summary>
    public int3 Along { get; set; }
    public int3 Into { get; set; }

    /// <summary>What the face looks like, for the title.</summary>
    public string Label { get; set; } = "rock";

    /// <summary>Which spots are rock to tap, as anyone sees the wall.</summary>
    public bool[] Rock { get; private set; } = new bool[Spots];

    /// <summary>The chalk on each spot, as the miner leaves it.</summary>
    public ChalkMark[] Marks { get; private set; } = new ChalkMark[Spots];

    /// <summary>Taps at each spot, and the sum of what they sounded like (0 ring, 1 dead).</summary>
    public int[] Taps { get; private set; } = new int[Spots];
    public float[] Heard { get; private set; } = new float[Spots];

    /// <summary>Each spot's last <see cref="Kept"/> taps as heard, newest at <c>(Taps - 1) % Kept</c>.</summary>
    private float[] _recent = new float[Spots * Kept];

    /// <summary>A spot's kept taps as heard (0 ring, 1 dead), oldest first.</summary>
    public IEnumerable<float> RecentAt(int spot)
    {
        var n = Math.Min(Taps[spot], Kept);
        for (var k = n; k >= 1; k--)
            yield return _recent[spot * Kept + (Taps[spot] - k) % Kept];
    }

    /// <summary>The spot the hammer's over.</summary>
    public int Spot { get; private set; } = Spots / 2;

    /// <summary>Taps so far; the last tap's spot and how it sounded (0 a clear ring, 1 dead).</summary>
    public int TapCount { get; private set; }
    public int LastSpot { get; private set; } = -1;
    public float LastHeard { get; private set; }

    /// <summary>Steps till the hammer can tap again.</summary>
    public int Cooldown { get; private set; }

    /// <summary>What lies behind each spot: how dead its note truly is. Never shown.</summary>
    private float[] _dead = new float[Spots];

    private int _axis;

    public float Noise => BaseNoise / Hands.Tolerance + TremorNoise * Hands.Tremor;

    /// <summary>How a spot has sounded over its taps (0 ring, 1 dead), or null if it's not been tapped.</summary>
    public float? HeardAt(int spot) => Taps[spot] > 0 ? Heard[spot] / Taps[spot] : null;

    public override Skill Skill => Skill.Mining;
    public override float Xp => 3f + 0.3f * Math.Min(TapCount, 20);
    public override string Title => $"Sounding the {Label}";

    public override int3? Focus(Simulation sim) => Face;

    /// <summary>The wall's cell for a spot (0 at the left, as the miner faces it).</summary>
    public int3 CellOf(int spot) => Face + Along * (spot - Spots / 2);

    /// <summary>The thing def that sounds rock: held in the hand, it turns F at a face from hewing to sounding.</summary>
    public const string HammerDef = "SoundingHammer";

    public static bool HammerInHand(Simulation sim, Entity who) =>
        sim.World.TryGet<Carrying>(who, out var c) && !c.Item.IsNull && sim.World.IsAlive(c.Item)
        && sim.World.TryGet<Thing>(c.Item, out var t) && t.Def.Name == HammerDef;

    /// <summary>
    /// Why <paramref name="who"/>, at <paramref name="here"/>, can't sound <paramref name="cell"/>, or null if they can:
    /// rock, in reach and square in front of them on their level, the hammer in hand, and a trade that may mine.
    /// </summary>
    public static string? Problem(Simulation sim, Entity who, int3 cell, int3 here)
    {
        var map = sim.Map;
        if (!map.InBounds(cell) || !map.IsSolid(map.Idx(cell)) || map.Block[map.Idx(cell)].DigTool != "Pick")
            return "not rock";
        var d = cell - here;
        if (d.Z != 0 || Math.Abs(d.X) + Math.Abs(d.Y) != 1)
            return "stand square to the face";
        if (!HammerInHand(sim, who))
            return "no sounding hammer in hand";
        if (!RoundSystem.MayMine(sim, who))
            return "not yours to mine";
        return null;
    }

    /// <summary>A sounding of the wall at <paramref name="cell"/> from <paramref name="here"/>: what each spot looks like, its chalk, and (privately) what's behind it.</summary>
    public static SoundSession At(Simulation sim, int3 cell, int3 here)
    {
        var into = cell - here;
        var s = new SoundSession
        {
            Face = cell,
            Into = into,
            // To the miner's right as they face the wall (y runs south): facing north (0,-1), right is east (1,0).
            Along = new int3(-into.Y, into.X, 0),
            Label = HewSession.FaceOf(sim, cell).Label,
        };
        var map = sim.Map;
        for (var k = 0; k < Spots; k++)
        {
            var c = s.CellOf(k);
            s.Rock[k] = map.InBounds(c) && map.IsSolid(map.Idx(c)) && Mining.ShownBlock(sim, map.Idx(c)).DigTool == "Pick";
            s.Marks[k] = ChalkMarks.At(sim, c);
            var dead = 0f;
            for (var depth = 0; depth < DepthWeight.Length; depth++)
            {
                var b = c + into * depth;
                if (map.InBounds(b) && map.Block[map.Idx(b)].Ore)
                    dead += DepthWeight[depth];
            }
            s._dead[k] = Math.Clamp(dead, 0f, 1f);
        }
        return s;
    }

    public override bool StillValid(Simulation sim) =>
        Problem(sim, Worker, Face, sim.World.Get<Position>(Worker).Cell) == null;

    protected override void Step()
    {
        if (Cooldown > 0)
            Cooldown--;
    }

    // A/D (and W/S) step the hammer a spot at a time: on the press, not while held.
    protected override void Axis(int value)
    {
        if (value != 0 && value != _axis)
            Spot = Math.Clamp(Spot + value, 0, Spots - 1);
        _axis = value;
    }

    protected override void Pointed(float u, float v) => Spot = SpotAt(u);

    protected override void Clicked(float? u, float? v)
    {
        if (u is { } x)
            Spot = SpotAt(x);
        Press();
    }

    private static int SpotAt(float u) => Math.Clamp((int)(u * Spots), 0, Spots - 1);

    protected override void Press()
    {
        if (Cooldown > 0 || !Rock[Spot])
            return;
        var heard = Math.Clamp(_dead[Spot] + (Rand.NextFloat() + Rand.NextFloat() - 1f) * Noise, 0f, 1f);
        _recent[Spot * Kept + Taps[Spot] % Kept] = heard;
        Taps[Spot]++;
        Heard[Spot] += heard;
        TapCount++;
        LastSpot = Spot;
        LastHeard = heard;
        Cooldown = TapSteps;
        Sample(1f);
    }

    /// <summary>Space: chalk the spot, round from nothing to a seam, to bare, to wiped.</summary>
    protected override void Act()
    {
        if (Rock[Spot])
            Marks[Spot] = (ChalkMark)(((int)Marks[Spot] + 1) % 3);
    }

    /// <summary>The chalk stays on the wall however the sounding ends.</summary>
    public override void Finish(Simulation sim) => LeaveChalk(sim);

    public override void Interrupted(Simulation sim) => LeaveChalk(sim);

    private void LeaveChalk(Simulation sim)
    {
        for (var k = 0; k < Spots; k++)
            if (Rock[k])
                ChalkMarks.Set(sim, CellOf(k), Marks[k]);
    }

    protected override void Cloned()
    {
        Rock = [.. Rock];
        Marks = [.. Marks];
        Taps = [.. Taps];
        Heard = [.. Heard];
        _dead = [.. _dead];
        _recent = [.. _recent];
    }

    protected override void WriteState(SaveWriter w)
    {
        foreach (var c in new[] { Face, Along, Into })
        {
            w.Int(c.X);
            w.Int(c.Y);
            w.Int(c.Z);
        }
        w.String(Label);
        for (var k = 0; k < Spots; k++)
        {
            w.Bool(Rock[k]);
            w.Byte((byte)Marks[k]);
            w.Int(Taps[k]);
            w.Float(Heard[k]);
            w.Float(_dead[k]);
        }
        foreach (var heard in _recent)
            w.Float(heard);
        w.Int(Spot);
        w.Int(TapCount);
        w.Int(LastSpot);
        w.Float(LastHeard);
        w.Int(Cooldown);
        w.Int(_axis);
    }

    protected override void ReadState(SaveReader r)
    {
        int3 Cell() => new(r.Int(), r.Int(), r.Int());
        Face = Cell();
        Along = Cell();
        Into = Cell();
        Label = r.String();
        for (var k = 0; k < Spots; k++)
        {
            Rock[k] = r.Bool();
            var mark = r.Byte();
            Marks[k] = Enum.IsDefined((ChalkMark)mark) ? (ChalkMark)mark : ChalkMark.None;
            Taps[k] = Math.Max(0, r.Int());
            Heard[k] = Math.Clamp(r.Float(), 0f, Taps[k]);
            _dead[k] = Math.Clamp(r.Float(), 0f, 1f);
        }
        for (var k = 0; k < _recent.Length; k++)
            _recent[k] = Math.Clamp(r.Float(), 0f, 1f);
        Spot = Math.Clamp(r.Int(), 0, Spots - 1);
        TapCount = Math.Max(0, r.Int());
        LastSpot = Math.Clamp(r.Int(), -1, Spots - 1);
        LastHeard = Math.Clamp(r.Float(), 0f, 1f);
        Cooldown = Math.Clamp(r.Int(), 0, TapSteps);
        _axis = Math.Sign(r.Int());
    }
}

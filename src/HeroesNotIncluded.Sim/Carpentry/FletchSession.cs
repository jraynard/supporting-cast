using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Carpentry;

/// <summary>The stages of making an arrow (<see cref="FletchSession"/>), in order.</summary>
public enum ArrowStage : byte
{
    /// <summary>Sighting the shaft down its length and bending it straight.</summary>
    Sight,

    /// <summary>Shaving the extra wood off, with the knife where the hand puts it.</summary>
    Shave,

    /// <summary>Pushing the iron head onto the shaft's end, in line.</summary>
    Head,

    /// <summary>Setting three feathers round the shaft.</summary>
    Fletch,

    Done,
}

/// <summary>
/// Making an arrow at the carpenter's bench, the pattern for the score in the quiver: one arrow,
/// taken through every stage once (each scored, the quiver's quality their average).
/// <list type="bullet">
/// <item><b>Sight</b>: the shaft comes with a warp; holding F bends it back against it, faster the
/// longer it's held, till Space says it's true. The needle shows the warp as the carpenter's eye
/// reads it, a little off the truth (<see cref="Misread"/>), so a shaft can look truer than it is;
/// left past <see cref="WarpLimit"/>, the quiver is warped (<see cref="Fletched.Warped"/>), which
/// nobody can see.</item>
/// <item><b>Shave</b>: the shaft side on, lumps of extra wood standing proud of it. The knife goes
/// where the mouse (or WASD) puts it, and shaves while the button (or F) is down, taking the wood
/// down to its edge wherever it passes, a bite at a time. Below the shaft's line it gouges the
/// shaft. Space when it's smooth enough: the more wood off and the fewer gouges, the better.</item>
/// <item><b>Head</b>: the iron head on the shaft's end, crooked. Holding F pushes it on, and it tips
/// further off line as it goes; A and D (or the mouse) keep it straight. Seated, it's bound with sinew.</item>
/// <item><b>Fletch</b>: the shaft end on. A and D (or the mouse) point round it, F sets a feather
/// there: three, best a third of a turn apart.</item>
/// </list>
/// </summary>
public sealed class FletchSession : WorkSession
{
    // ---------------- Sight ----------------

    /// <summary>How far a hold bends the shaft per step at first, and the most a long hold gets to (times that).</summary>
    public const float BendPerStep = 0.003f, MostBend = 3f;

    /// <summary>Steps of a hold for it to bend one more time as fast.</summary>
    public const int BendSteps = 60;

    /// <summary>True enough (before skill widens it); past this either way the arrow flies wide.</summary>
    public const float True = 0.04f, WarpLimit = 0.15f;

    /// <summary>How far the eye can misread a warp, at the shakiest.</summary>
    public const float MostMisread = 0.12f;

    // ---------------- Shave ----------------

    /// <summary>Columns along the shaft the wood's kept in.</summary>
    public const int Columns = 48;

    /// <summary>Lumps of extra wood along a shaft.</summary>
    public const int Lumps = 3;

    /// <summary>How far the knife moves a step on the keys (the panel is 0..1 each way).</summary>
    public const float KnifeSpeed = 0.012f;

    /// <summary>Half the knife's edge, along the shaft.</summary>
    public const float Edge = 0.03f;

    /// <summary>The most one pass takes off a column, per length of the knife's travel.</summary>
    public const float BitePerTravel = 6f;

    /// <summary>
    /// Down the panel (v), where the shaft's line is, and how tall a lump of 1 stands: a knife at
    /// v above the line is that high in the wood, below it, in the shaft.
    /// </summary>
    public const float LineV = 0.75f, HeightV = 0.6f;

    /// <summary>What a gouge costs, against the wood it would take to cost as much left on.</summary>
    public const float GougeCost = 3f;

    // ---------------- Head ----------------

    /// <summary>How far a hold pushes the head on per step (seated at 1).</summary>
    public const float SeatPerStep = 0.006f;

    /// <summary>How fast A/D (or the mouse) turn the head back in line, per step.</summary>
    public const float TiltPerStep = 0.012f;

    /// <summary>How fast a crooked head tips further as it's pushed (a share of its tilt, per step), and its jitter.</summary>
    public const float TipAway = 0.012f, TipJitter = 0.01f;

    /// <summary>In line (before skill widens it); this far off, it's no good.</summary>
    public const float InLine = 0.06f, OffLine = 0.5f;

    // ---------------- Fletch ----------------

    /// <summary>Feathers to the arrow.</summary>
    public const int Feathers = 3;

    /// <summary>How far round A/D turn the pointer a step (a turn is 1).</summary>
    public const float AimPerStep = 0.008f;

    /// <summary>How far off even (a share of a turn) a feather can be set and still count for anything.</summary>
    public const float SpacingSlack = 0.12f;

    /// <summary>The bench.</summary>
    public Entity Station { get; set; } = Entity.Null;

    public ArrowStage Stage { get; private set; }

    /// <summary>Straightening the shaft (the first stage).</summary>
    public bool Sighting => Stage == ArrowStage.Sight;

    /// <summary>The shaft's true warp now (−1..1, 0 straight). Hidden: the game shows <see cref="SeenWarp"/>.</summary>
    internal float Warp { get; private set; }

    /// <summary>How far off the carpenter's eye reads this shaft's warp.</summary>
    internal float Misread { get; private set; }

    /// <summary>The warp as the carpenter sees it down the shaft: what the needle reads.</summary>
    public float SeenWarp => Warp + Misread;

    /// <summary>Which way a hold bends the shaft: back against the warp it came with.</summary>
    public int BendDirection { get; private set; } = 1;

    /// <summary>The shaft was left past the limit: the quiver's warped.</summary>
    internal bool AnyWarped { get; private set; }

    /// <summary>The wood still standing proud of the shaft in each column (0..1 tall).</summary>
    public float[] Wood { get; private set; } = new float[Columns];

    /// <summary>How deep each column's been gouged into the shaft (0 none).</summary>
    public float[] Gouges { get; private set; } = new float[Columns];

    /// <summary>Where the knife is on the panel: along the shaft (0..1) and down (0..1; <see cref="LineV"/> is the shaft's line).</summary>
    public float KnifeX { get; private set; } = 0.1f;
    public float KnifeY { get; private set; } = 0.2f;

    /// <summary>How high the knife's edge stands over the shaft's line (negative: in the shaft).</summary>
    public float KnifeHeight => HeightAt(KnifeY);

    public static float HeightAt(float v) => (LineV - v) / HeightV;

    private float _woodAtStart;

    /// <summary>How crooked the head sits on the shaft (−1..1, 0 in line).</summary>
    public float Tilt { get; private set; }

    /// <summary>How far it's pushed on, 0..1.</summary>
    public float Seated { get; private set; }

    private float _seatSum;
    private int _seatSteps;

    /// <summary>Where round the shaft the pointer is, to set the next feather (0..1, 0 at the top).</summary>
    public float Aim { get; private set; }

    /// <summary>Feathers set.</summary>
    public int Set { get; private set; }

    /// <summary>Where round the shaft each feather was set.</summary>
    public float[] FeatherAt { get; private set; } = new float[Feathers];

    private int _hold;
    private int _accept;
    private int _dx, _dy;

    public float TrueWidth => True * Hands.Tolerance;
    public float InLineWidth => InLine * Hands.Tolerance;

    public override Skill Skill => Skill.Crafting;
    public override float Xp => 40f;
    public override string Title => Stage switch
    {
        ArrowStage.Sight => "Sighting the shaft",
        ArrowStage.Shave => "Shaving the shaft",
        ArrowStage.Head => "Fitting the head",
        _ => $"Fletching the arrow (feather {Math.Min(Set + 1, Feathers)} of {Feathers})",
    };

    public override int3? Focus(Simulation sim) => sim.World.IsAlive(Station) ? sim.World.Get<Thing>(Station).Cell : null;

    public override bool StillValid(Simulation sim)
    {
        var w = sim.World;
        return Carpenters.IsBench(sim, Station) && AvatarSystem.InReach(sim, w.Get<Position>(Worker).Cell, Station)
            && w.Get<Workbench>(Station) is { Active: { } r, Stocked: true } bench && bench.By == Worker && Carpenters.Fletches(r);
    }

    protected internal override void Begin()
    {
        // A warp one way or the other, never nearly straight to begin with.
        var size = 0.35f + Rand.NextFloat() * 0.45f;
        Warp = Rand.Chance(0.5f) ? size : -size;
        BendDirection = Math.Sign(Warp);
        Misread = (Rand.NextFloat() * 2f - 1f) * MostMisread * (1f - Hands.Precision * 0.5f) / Hands.Tolerance;
        // Lumps of wood along the shaft, each a smooth hump.
        for (var k = 0; k < Lumps; k++)
        {
            var at = (k + 0.25f + Rand.NextFloat() * 0.5f) / Lumps;
            var height = 0.35f + Rand.NextFloat() * 0.6f;
            for (var i = 0; i < Columns; i++)
            {
                var d = MathF.Abs((i + 0.5f) / Columns - at) / 0.13f;
                if (d < 1f)
                    Wood[i] = Math.Max(Wood[i], height * (1f - d * d));
            }
        }
        for (var i = 0; i < Columns; i++)
            Wood[i] = Math.Max(Wood[i], 0.08f); // a skin of wood the whole length
        _woodAtStart = Wood.Sum();
        var tilt = 0.3f + Rand.NextFloat() * 0.3f;
        Tilt = Rand.Chance(0.5f) ? tilt : -tilt;
    }

    protected override void Cloned()
    {
        FeatherAt = (float[])FeatherAt.Clone();
        Wood = (float[])Wood.Clone();
        Gouges = (float[])Gouges.Clone();
    }

    /// <summary>Space: the shaft's true (sighting), or it's shaved enough.</summary>
    protected override void Act() => _accept = 1;

    /// <summary>F (or a click): set a feather where the pointer is.</summary>
    protected override void Press()
    {
        if (Stage == ArrowStage.Fletch && Set < Feathers)
            FeatherAt[Set++] = Aim;
    }

    protected override void Axis(int value) => _dx = value;

    protected override void Axis2(int value) => _dy = value;

    protected override void Pointed(float u, float v)
    {
        switch (Stage)
        {
            case ArrowStage.Shave:
                MoveKnife(u, v, Held);
                break;
            case ArrowStage.Fletch:
                // Round the shaft from the middle of the panel: the top is 0, clockwise.
                if (MathF.Abs(u - 0.5f) + MathF.Abs(v - 0.5f) > 0.05f)
                {
                    var a = DetMath.Atan2(u - 0.5f, -(v - 0.5f)) / (2f * MathF.PI);
                    Aim = a - MathF.Floor(a);
                }
                break;
        }
    }

    protected override void Step()
    {
        switch (Stage)
        {
            case ArrowStage.Sight:
                StepSight();
                break;
            case ArrowStage.Shave:
                StepShave();
                break;
            case ArrowStage.Head:
                StepHead();
                break;
            case ArrowStage.Fletch:
                StepFletch();
                break;
        }
    }

    private void Next(ArrowStage stage)
    {
        Stage = stage;
        _hold = 0;
        _accept = 0;
        if (stage == ArrowStage.Done)
            Done = true;
    }

    // ---------------- Sight ----------------

    private void StepSight()
    {
        if (_accept > 0)
        {
            AnyWarped = MathF.Abs(Warp) > WarpLimit;
            Sample(Straightness(Warp));
            Next(ArrowStage.Shave);
            return;
        }
        if (!Held)
        {
            _hold = 0;
            return;
        }
        _hold++;
        // Bent against the warp it started with: the same way all along, so it can go past straight.
        var speed = Math.Min(MostBend, 1f + _hold / (float)BendSteps);
        var wobble = 1f + (Rand.NextFloat() * 2f - 1f) * (0.2f + Hands.Tremor);
        Warp = Math.Clamp(Warp - BendDirection * BendPerStep * speed * wobble, -1f, 1f);
    }

    /// <summary>How true a shaft was left, 0..1.</summary>
    public float Straightness(float warp)
    {
        var off = MathF.Abs(warp);
        return off <= TrueWidth ? 1f : Math.Max(0f, 1f - (off - TrueWidth) / (WarpLimit * 2f - TrueWidth));
    }

    // ---------------- Shave ----------------

    private void StepShave()
    {
        if (_accept > 0)
        {
            Sample(ShaveScore());
            Next(ArrowStage.Head);
            return;
        }
        if (_dx != 0 || _dy != 0)
            MoveKnife(KnifeX + _dx * KnifeSpeed, KnifeY + _dy * KnifeSpeed, Held);
    }

    /// <summary>Move the knife, shaving what it passes over if it's down (in short pieces, so a quick drag doesn't skip).</summary>
    private void MoveKnife(float x, float y, bool shaving)
    {
        x = Math.Clamp(x, 0f, 1f);
        y = Math.Clamp(y, 0f, 1f);
        float dx = x - KnifeX, dy = y - KnifeY;
        var dist = MathF.Sqrt(dx * dx + dy * dy);
        if (!shaving || dist <= 0f)
        {
            (KnifeX, KnifeY) = (x, y);
            return;
        }
        var pieces = Math.Max(1, (int)MathF.Ceiling(dist / (Edge * 0.5f)));
        var bite = BitePerTravel * dist / pieces;
        for (var p = 1; p <= pieces; p++)
        {
            var px = KnifeX + dx * p / pieces;
            var height = HeightAt(KnifeY + dy * p / pieces) + (Rand.NextFloat() * 2f - 1f) * Hands.Tremor * 0.05f;
            Shave(px, height, bite);
        }
        (KnifeX, KnifeY) = (x, y);
    }

    private void Shave(float at, float height, float bite)
    {
        var lo = Math.Max(0, (int)((at - Edge) * Columns));
        var hi = Math.Min(Columns - 1, (int)((at + Edge) * Columns));
        for (var i = lo; i <= hi; i++)
        {
            if (Wood[i] > height)
                Wood[i] = Math.Max(Math.Max(height, 0f), Wood[i] - bite);
            // Below the line, and nothing left there over it: the edge is in the shaft.
            if (height < 0f && Wood[i] <= 0.001f)
                Gouges[i] = Math.Max(Gouges[i], -height);
        }
    }

    /// <summary>How the shave's gone so far, 0..1: the share of the wood taken off, less what the gouges cost.</summary>
    public float ShaveScore()
    {
        if (_woodAtStart <= 0f)
            return 1f;
        var off = 1f - Wood.Sum() / _woodAtStart;
        var gouged = Gouges.Sum() * GougeCost / _woodAtStart;
        return Math.Clamp(off - gouged, 0f, 1f);
    }

    // ---------------- Head ----------------

    private void StepHead()
    {
        // Turned back in line: A/D, or the mouse across the panel (u 0..1 → −1..1).
        var turn = PointU is { } u ? Toward(Tilt, u * 2f - 1f, TiltPerStep) : _dx;
        Tilt = Math.Clamp(Tilt + turn * TiltPerStep, -1f, 1f);
        if (!Held)
            return;
        // Pushed on, a crooked head goes more crooked.
        Tilt = Math.Clamp(Tilt + Tilt * TipAway + (Rand.NextFloat() * 2f - 1f) * TipJitter * (1f + Hands.Tremor * 3f), -1f, 1f);
        Seated = Math.Min(1f, Seated + SeatPerStep);
        _seatSum += LineScore(Tilt);
        _seatSteps++;
        if (Seated >= 1f)
        {
            Sample(_seatSum / Math.Max(1, _seatSteps));
            Next(ArrowStage.Fletch);
        }
    }

    /// <summary>How well a head sits this far off line, 0..1.</summary>
    public float LineScore(float tilt)
    {
        var off = MathF.Abs(tilt);
        return off <= InLineWidth ? 1f : Math.Max(0f, 1f - (off - InLineWidth) / (OffLine - InLineWidth));
    }

    // ---------------- Fletch ----------------

    private void StepFletch()
    {
        if (_dx != 0)
        {
            var a = Aim + _dx * AimPerStep;
            Aim = a - MathF.Floor(a);
        }
        if (Set < Feathers)
            return;
        Sample(Spacing(FeatherAt));
        Next(ArrowStage.Done);
    }

    /// <summary>How evenly three feathers sit round a shaft, 0..1: each a third of a turn from the last.</summary>
    public static float Spacing(float[] at)
    {
        // In order round the shaft, the gaps between them: each should be a third.
        var sorted = at.Select(a => a - MathF.Floor(a)).OrderBy(a => a).ToArray();
        var worst = 0f;
        for (var i = 0; i < sorted.Length; i++)
        {
            var gap = (i + 1 < sorted.Length ? sorted[i + 1] : sorted[0] + 1f) - sorted[i];
            worst = Math.Max(worst, MathF.Abs(gap - 1f / sorted.Length));
        }
        return Math.Max(0f, 1f - worst / SpacingSlack);
    }

    public override void Finish(Simulation sim)
    {
        var w = sim.World;
        var at = sim.LocationOf(Worker);
        var quality = Quality;
        foreach (var item in Crafting.Finish(sim, Station, Worker, Crafting.QualityFrom(quality)))
        {
            if (w.Get<Thing>(item).Def.Quiver)
                Fletching.Mark(sim, item, Worker, quality, AnyWarped);
            Inventory.Stow(sim, Worker, item);
        }
        // Said as the carpenter sees it: their own eye, not the truth of the warp.
        var note = quality >= 0.85f ? " A fine arrow: the rest are cut to match it." : quality < 0.4f ? " Rough work: it'll do, just." : "";
        sim.Messages.Tell(Worker, MessageKind.Info, $"You make a quiver of arrows.{note}", at);
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Station);
        w.Byte((byte)Stage);
        w.Float(Warp);
        w.Float(Misread);
        w.Int(BendDirection);
        w.Bool(AnyWarped);
        foreach (var x in Wood)
            w.Float(x);
        foreach (var x in Gouges)
            w.Float(x);
        w.Float(KnifeX);
        w.Float(KnifeY);
        w.Float(_woodAtStart);
        w.Float(Tilt);
        w.Float(Seated);
        w.Float(_seatSum);
        w.Int(_seatSteps);
        w.Float(Aim);
        w.Int(Set);
        foreach (var f in FeatherAt)
            w.Float(f);
        w.Int(_hold);
        w.Int(_accept);
        w.Int(_dx);
        w.Int(_dy);
    }

    protected override void ReadState(SaveReader r)
    {
        Station = r.Entity();
        var stage = r.Byte();
        Stage = Enum.IsDefined((ArrowStage)stage) ? (ArrowStage)stage : throw new SaveException($"Corrupt save: arrow stage {stage}");
        Warp = Math.Clamp(r.Float(), -1f, 1f);
        Misread = Math.Clamp(r.Float(), -1f, 1f);
        BendDirection = r.Int() < 0 ? -1 : 1;
        AnyWarped = r.Bool();
        Wood = new float[Columns];
        for (var i = 0; i < Columns; i++)
            Wood[i] = Math.Clamp(r.Float(), 0f, 1f);
        Gouges = new float[Columns];
        for (var i = 0; i < Columns; i++)
            Gouges[i] = Math.Clamp(r.Float(), 0f, 2f);
        KnifeX = Math.Clamp(r.Float(), 0f, 1f);
        KnifeY = Math.Clamp(r.Float(), 0f, 1f);
        _woodAtStart = Math.Max(0f, r.Float());
        Tilt = Math.Clamp(r.Float(), -1f, 1f);
        Seated = Math.Clamp(r.Float(), 0f, 1f);
        _seatSum = Math.Max(0f, r.Float());
        _seatSteps = Math.Max(0, r.Int());
        Aim = Math.Clamp(r.Float(), 0f, 1f);
        Set = Math.Clamp(r.Int(), 0, Feathers);
        FeatherAt = new float[Feathers];
        for (var i = 0; i < Feathers; i++)
            FeatherAt[i] = Math.Clamp(r.Float(), 0f, 1f);
        _hold = Math.Max(0, r.Int());
        _accept = Math.Sign(r.Int());
        _dx = Math.Sign(r.Int());
        _dy = Math.Sign(r.Int());
    }
}

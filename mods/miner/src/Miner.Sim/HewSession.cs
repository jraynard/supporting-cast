using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Miner;

/// <summary>
/// Hewing a rock face (the miner's hand game; a first pass, to be found by playing it). The wall is seen side on,
/// the crack it'll break along running into it at some height: a seam of weakness.
/// <list type="bullet">
/// <item>W/S (or A/D, or the mouse up and down the wall) puts the pick's point at the crack's height (<see cref="Aim"/>).</item>
/// <item>F held swings the pick back, let go it strikes. On the crack and hard enough (<see cref="NeedRaise"/>)
/// a slab comes away, a layer off the wall, and the crack runs on a little higher or lower.</item>
/// <item>On the crack but soft, it only chips. Off it and hard, the point rings off the rock and jars the arms:
/// the next swing comes up slowly (<see cref="Jarred"/>).</item>
/// </list>
/// The face takes as many slabs as the rock it looks like (<see cref="ChunksFor"/>, read from what's shown:
/// hidden ore hews like its host rock, so the game never gives a seam away). A clean hewing loosens a little
/// more of a seam than the dig key would (<see cref="CleanHew"/>).
/// </summary>
public sealed class HewSession : WorkSession
{
    /// <summary>How fast holding F swings the pick back, per step; jarred arms bring it up at <see cref="JarredRaise"/> of that.</summary>
    public const float RaisePerStep = 0.022f, JarredRaise = 0.4f;

    /// <summary>How far the aim moves a step on the keys, and how much of the way to the mouse.</summary>
    public const float AimPerStep = 0.025f, MouseEase = 0.3f;

    /// <summary>How near the crack (up the wall, −1..1) the point must land, before skill widens it.</summary>
    public const float CrackWidth = 0.15f;

    /// <summary>How far back the pick must swing to break a slab away.</summary>
    public const float NeedRaise = 0.55f;

    /// <summary>Steps the arms stay jarred after the point rings off the rock.</summary>
    public const int JarSteps = 25;

    /// <summary>Dig work (the block's <c>digWork</c>) per slab, and the fewest and most slabs a face takes.</summary>
    public const float DigWorkPerChunk = 250f;
    public const int FewestChunks = 2, MostChunks = 6;

    /// <summary>The hewing's quality at which a seam gives one more piece of ore.</summary>
    public const float CleanHew = 0.75f;

    /// <summary>The rock face being hewn.</summary>
    public int3 Cell { get; set; }

    /// <summary>What the face looks like (<see cref="Mining.ShownBlock"/>), for the title: never what it is.</summary>
    public string Face { get; set; } = "rock";

    /// <summary>Slabs it takes, and broken away so far.</summary>
    public int Chunks { get; set; } = 3;
    public int ChunksDone { get; private set; }

    /// <summary>Where the point is up the wall, −1 (high) .. 1 (low).</summary>
    public float Aim { get; private set; }

    /// <summary>The height the crack runs into the wall at now, −1 (high) .. 1 (low).</summary>
    public float Crack { get; private set; }

    /// <summary>How far back the pick is swung (0 down, 1 as far as it goes).</summary>
    public float Raised { get; private set; }

    /// <summary>Steps the arms are still jarred for (0: not).</summary>
    public int Jarred { get; private set; }

    /// <summary>Blows struck, and how the last went (for the panel and the sound).</summary>
    public int Blows { get; private set; }
    public HewBlow LastBlow { get; private set; }

    public float CrackWidthNow => CrackWidth * Hands.Tolerance;

    private bool _raising;
    private int _aim;

    public override Skill Skill => Skill.Mining;
    public override float Xp => 6f + 2f * Chunks;
    public override string Title => $"Hewing the {Face} (slab {Math.Min(ChunksDone + 1, Chunks)} of {Chunks})";

    public override int3? Focus(Simulation sim) => Cell;

    /// <summary>The face as anyone sees it: hidden ore passes for its rock.</summary>
    public static BlockDef FaceOf(Simulation sim, int3 cell) => Mining.ShownBlock(sim, sim.Map.Idx(cell));

    /// <summary>Slabs a face takes, by the rock it looks like.</summary>
    public static int ChunksFor(BlockDef shown) =>
        Math.Clamp((int)MathF.Round(shown.DigWork / DigWorkPerChunk), FewestChunks, MostChunks);

    /// <summary>
    /// Why <paramref name="who"/>, standing at <paramref name="here"/>, can't hew <paramref name="cell"/>, or null
    /// if they can: rock (a pick's work), dug out of nothing built on, in arm's reach, a pick to hand that breaks it,
    /// and a trade that may mine in a round.
    /// </summary>
    public static string? Problem(Simulation sim, Entity who, int3 cell, int3 here)
    {
        var map = sim.Map;
        if (!map.InBounds(cell) || !map.IsSolid(map.Idx(cell)) || !sim.CanDig(cell))
            return "no rock there";
        var block = map.Block[map.Idx(cell)];
        if (block.DigTool != "Pick")
            return "not rock";
        if (!AvatarSystem.InReach(here, cell))
            return "out of reach";
        if (!RoundSystem.MayMine(sim, who))
            return "not yours to mine";
        return Mining.BreakProblem(sim, who, block);
    }

    /// <summary>A hewing of the face at <paramref name="cell"/>, as many slabs as the rock it looks like.</summary>
    public static HewSession At(Simulation sim, int3 cell)
    {
        var face = FaceOf(sim, cell);
        return new HewSession { Cell = cell, Face = face.Label, Chunks = ChunksFor(face) };
    }

    public override bool StillValid(Simulation sim) =>
        Problem(sim, Worker, Cell, sim.World.Get<Position>(Worker).Cell) == null;

    protected override void Begin() => Crack = Rand.Range(-0.6f, 0.6f);

    /// <summary>The crack after a slab comes away: a little higher or lower than it was, never off the wall.</summary>
    private float NextCrack() => Math.Clamp(Crack + Rand.Range(-0.5f, 0.5f), -0.75f, 0.75f);

    // A/D and W/S both move the point along the wall (A and W up it), so the keys of any mini-game work here.
    protected override void Axis(int value) => _aim = value;

    protected override void Axis2(int value) => _aim = value;

    protected override void Step()
    {
        if (Done)
            return;
        if (Jarred > 0)
            Jarred--;
        if (!_raising)
        {
            Aim = PointV is { } pv
                ? Math.Clamp(Aim + (Math.Clamp(pv * 2f - 1f, -1f, 1f) - Aim) * MouseEase, -1f, 1f)
                : Math.Clamp(Aim + _aim * AimPerStep, -1f, 1f);
        }
        if (Held)
        {
            Raised = Math.Min(1f, Raised + RaisePerStep * (Jarred > 0 ? JarredRaise : 1f));
            _raising = true;
            return;
        }
        if (!_raising)
            return;
        _raising = false;
        Blows++;
        // Where the point lands: where it was aimed, give or take the hand's shake.
        var landed = Aim + (Rand.NextFloat() * 2f - 1f) * Hands.Tremor * 0.15f;
        var onCrack = MathF.Abs(landed - Crack) <= CrackWidthNow;
        var hard = Raised >= NeedRaise;
        Raised = 0f;
        if (onCrack && hard)
        {
            ChunksDone++;
            LastBlow = HewBlow.Broke;
            Sample(1f);
            Crack = NextCrack();
            if (ChunksDone >= Chunks)
                Done = true;
        }
        else if (onCrack)
        {
            LastBlow = HewBlow.Chipped;
            Sample(0.6f);
        }
        else if (hard)
        {
            LastBlow = HewBlow.Rang;
            Jarred = JarSteps;
            Sample(0.2f);
        }
        else
        {
            LastBlow = HewBlow.Glanced;
            Sample(0.5f);
        }
    }

    /// <summary>The face comes away: dug out as the dig key digs it (ore by the miner's skill), and one more piece of a seam for a clean hewing.</summary>
    public override void Finish(Simulation sim)
    {
        var block = sim.Map.Block[sim.Map.Idx(Cell)];
        sim.DigOut(Cell, Worker);
        var clean = Quality >= CleanHew;
        if (clean && block.Ore && block.DigYield.FirstOrDefault(y => y.ThingDef.Name != "Stone") is { } ore)
            sim.SpawnItem(ore.ThingDef, 1, Cell, ore.MaterialDef);
        sim.Messages.Tell(Worker, MessageKind.Info, clean ? "The face comes away clean." : "The face comes away.", Cell);
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Int(Cell.X);
        w.Int(Cell.Y);
        w.Int(Cell.Z);
        w.String(Face);
        w.Int(Chunks);
        w.Int(ChunksDone);
        w.Float(Aim);
        w.Float(Crack);
        w.Float(Raised);
        w.Int(Jarred);
        w.Int(Blows);
        w.Byte((byte)LastBlow);
        w.Bool(_raising);
        w.Int(_aim);
    }

    protected override void ReadState(SaveReader r)
    {
        Cell = new int3(r.Int(), r.Int(), r.Int());
        Face = r.String();
        Chunks = Math.Clamp(r.Int(), FewestChunks, MostChunks);
        ChunksDone = Math.Clamp(r.Int(), 0, Chunks);
        Aim = Math.Clamp(r.Float(), -1f, 1f);
        Crack = Math.Clamp(r.Float(), -1f, 1f);
        Raised = Math.Clamp(r.Float(), 0f, 1f);
        Jarred = Math.Clamp(r.Int(), 0, JarSteps);
        Blows = Math.Max(0, r.Int());
        var blow = r.Byte();
        LastBlow = Enum.IsDefined((HewBlow)blow) ? (HewBlow)blow : HewBlow.None;
        _raising = r.Bool();
        _aim = Math.Sign(r.Int());
    }
}

/// <summary>How a blow at the rock face went.</summary>
public enum HewBlow : byte
{
    None,

    /// <summary>On the crack, hard: a slab came away.</summary>
    Broke,

    /// <summary>On the crack, too soft: a chip, the slab holds.</summary>
    Chipped,

    /// <summary>Off the crack, hard: the point rang off the rock and jarred the arms.</summary>
    Rang,

    /// <summary>Off the crack, soft: it glanced off.</summary>
    Glanced,
}

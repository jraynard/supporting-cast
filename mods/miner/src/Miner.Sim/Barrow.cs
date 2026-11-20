using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Saving;

namespace HeroesNotIncluded.Miner;

/// <summary>
/// What the town pays the miner for a piece of something hewn, tipped into the smithy's chest (the mod's own kind of
/// def, <c>"ores"</c> in miner.json, named for the thing). Only what's on this list goes in a barrow: ore and coal, not
/// the stone that comes away with them.
/// </summary>
public sealed class OreDef : Def, IModDef
{
    public const string Kind = "ores";

    /// <summary>Silver a piece, paid by whoever works the smithy. Fractions add up over a load.</summary>
    public float Silver { get; set; } = 1f;

    [System.Text.Json.Serialization.JsonIgnore] public ThingDef ThingDef { get; private set; } = null!;

    public void Resolve(DefDatabase db) => ThingDef = db.Thing(Name);

    public static OreDef? Of(Simulation sim, ThingDef def) => sim.Defs.ModDef<OreDef>(Kind, def.Name);
}

/// <summary>One piece in a barrow: what it is, and where on the bed it sits.</summary>
public readonly record struct BarrowPiece(ThingDef Def, byte Slot);

/// <summary>
/// A wheelbarrow's load (on the barrow itself, so it goes where the barrow goes and stays when it's set down): the
/// pieces in it, each in a slot of the bed, and so how it balances. Not a container: what's in it is out of the
/// world till it's tipped out or spills. Everyone can see a barrow's load; nothing about it is hidden.
/// </summary>
public sealed class BarrowLoad
{
    public List<BarrowPiece> Pieces { get; } = [];

    /// <summary>Where the barrow was last seen being pushed (to tell when it's wheeled onto a new cell), and the step before (to tell a turn).</summary>
    public int3 LastCell { get; set; } = new(-1, -1, -1);
    public int3 LastStep { get; set; }

    /// <summary>How far it leans as it's pushed: −1 over to the left, 1 over to the right (<see cref="BarrowSystem"/>).</summary>
    public float Lean { get; set; }

    /// <summary>Which way its pusher's leaning it back (Q/E): −1 left, 1 right, 0 not.</summary>
    public int Nudge { get; set; }

    /// <summary>Ticks since it was last wheeled onto a new cell: stood still, it settles.</summary>
    public int Still { get; set; }

    public float Mass => Pieces.Sum(p => p.Def.Mass);

    public BarrowLoad Copy()
    {
        var c = new BarrowLoad { LastCell = LastCell, LastStep = LastStep, Lean = Lean, Nudge = Nudge, Still = Still };
        c.Pieces.AddRange(Pieces);
        return c;
    }
}

/// <summary>
/// The barrow's bed and its balance. Seen from above, the wheel at the front: <see cref="Cols"/> slots across and
/// <see cref="Rows"/> from the wheel back to the handles, each holding <see cref="Layers"/> pieces. Its load balances
/// where its weight centres: square across it, and a little behind the wheel (<see cref="IdealRow"/>), so the wheel
/// takes most and the handles little. Off that, it's <see cref="Risk"/>y: pushed, it leans toward its heavy side and
/// won't right itself (<see cref="BarrowSystem"/>), so its pusher must keep leaning it back.
/// </summary>
public static class Barrows
{
    public const string BarrowDef = "Wheelbarrow";

    public const int Cols = 3, Rows = 4, Layers = 2;
    public const int Slots = Cols * Rows;

    /// <summary>The most a barrow takes, kg: ten of iron ore.</summary>
    public const float MaxMass = 30f;

    /// <summary>The best place for the load's weight, rows back from the wheel.</summary>
    public const float IdealRow = 0.8f;

    /// <summary>How much the weight's being off-centre across, and off the ideal fore and aft, makes for risk.</summary>
    public const float SideRisk = 0.7f, ForeRisk = 0.35f;

    /// <summary>Below this a load rights itself as it's pushed: a well-loaded barrow wants only the odd nudge (its weight within half a slot of square, a row of ideal).</summary>
    public const float Steady = 0.35f;

    public static int ColOf(int slot) => slot % Cols;
    public static int RowOf(int slot) => slot / Cols;
    public static int SlotAt(int col, int row) => Math.Clamp(row, 0, Rows - 1) * Cols + Math.Clamp(col, 0, Cols - 1);

    public static bool IsBarrow(Simulation sim, Entity e) =>
        !e.IsNull && sim.World.IsAlive(e) && sim.World.TryGet<Thing>(e, out var t) && t.Def.Name == BarrowDef;

    /// <summary>The barrow in someone's hands, or Entity.Null.</summary>
    public static Entity Held(Simulation sim, Entity who) =>
        sim.World.TryGet<Carrying>(who, out var c) && IsBarrow(sim, c.Item) ? c.Item : Entity.Null;

    /// <summary>A barrow's load (empty if none's been put in it).</summary>
    public static BarrowLoad LoadOf(Simulation sim, Entity barrow) =>
        sim.World.TryGet<BarrowLoad>(barrow, out var load) ? load : new BarrowLoad();

    /// <summary>Whether a thing goes in a barrow: it's on the price list.</summary>
    public static bool Loadable(Simulation sim, ThingDef def) => OreDef.Of(sim, def) != null;

    /// <summary>Where a load's weight centres: across (−1 left .. 1 right) and back from the wheel (0 .. <see cref="Rows"/> − 1).</summary>
    public static (float Across, float Back) Centre(IReadOnlyList<BarrowPiece> pieces)
    {
        float mass = 0f, across = 0f, back = 0f;
        foreach (var p in pieces)
        {
            var m = p.Def.Mass;
            mass += m;
            across += m * (ColOf(p.Slot) - (Cols - 1) / 2f);
            back += m * RowOf(p.Slot);
        }
        return mass <= 0f ? (0f, IdealRow) : (across / mass, back / mass);
    }

    /// <summary>How likely a load is to tip, 0 (square and a little behind the wheel) upward; below <see cref="Steady"/> it never does.</summary>
    public static float Risk(IReadOnlyList<BarrowPiece> pieces)
    {
        if (pieces.Count == 0)
            return 0f;
        var (across, back) = Centre(pieces);
        return SideRisk * MathF.Abs(across) + ForeRisk * MathF.Abs(back - IdealRow);
    }

    /// <summary>
    /// Set something down where <paramref name="who"/> can reach it again: a free cell of open ground on their own level in
    /// reach (where they stand first), else wherever there's room near. A tunnel's narrow; "near" alone may find the hilltop.
    /// </summary>
    public static Entity PutDown(Simulation sim, Entity who, Entity item)
    {
        var map = sim.Map;
        var here = sim.World.Get<Position>(who).Cell;
        foreach (var c in sim.SpiralFrom(here, 1))
            if (c.Z == here.Z && map.InBounds(c) && map.Passable(c) && map.Item[map.Idx(c)].IsNull && HeroesNotIncluded.Sim.Systems.AvatarSystem.InReach(here, c))
                return sim.PlaceItemNear(item, c);
        return sim.PlaceItemNear(item, here);
    }

    /// <summary>Pieces in a slot.</summary>
    public static int InSlot(IReadOnlyList<BarrowPiece> pieces, int slot) => pieces.Count(p => p.Slot == slot);

    /// <summary>What a load's worth at the smithy, silver (rounded to the nearest whole).</summary>
    public static int Worth(Simulation sim, IEnumerable<BarrowPiece> pieces) =>
        (int)MathF.Round(pieces.Sum(p => OreDef.Of(sim, p.Def)?.Silver ?? 0f));

    /// <summary>A load in words: "6 iron ore and 4 coal".</summary>
    public static string Describe(IEnumerable<BarrowPiece> pieces)
    {
        var parts = pieces.GroupBy(p => p.Def).OrderBy(g => g.Key.Index).Select(g => $"{g.Count()} {g.Key.Label}").ToList();
        return parts.Count == 0 ? "nothing" : parts.Count == 1 ? parts[0] : string.Join(", ", parts[..^1]) + " and " + parts[^1];
    }

    public static void Write(SaveWriter w, BarrowLoad load)
    {
        w.Int3(load.LastCell);
        w.Int3(load.LastStep);
        w.Float(load.Lean);
        w.Int(load.Nudge);
        w.Int(load.Still);
        w.Int(load.Pieces.Count);
        foreach (var p in load.Pieces)
        {
            w.Def(p.Def);
            w.Byte(p.Slot);
        }
    }

    public static BarrowLoad Read(SaveReader r)
    {
        var load = new BarrowLoad
        {
            LastCell = r.Int3(),
            LastStep = r.Int3(),
            Lean = Math.Clamp(r.Float(), -1f, 1f),
            Nudge = Math.Sign(r.Int()),
            Still = Math.Max(0, r.Int()),
        };
        var n = r.Count(Slots * Layers);
        for (var k = 0; k < n; k++)
        {
            var def = r.Def<ThingDef>();
            var slot = r.Byte();
            if (slot < Slots)
                load.Pieces.Add(new BarrowPiece(def, slot));
        }
        return load;
    }
}

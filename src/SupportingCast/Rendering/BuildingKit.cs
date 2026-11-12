using Narsil.Rendering;
using SupportingCast.Sim;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Ecs;
using SupportingCast.Sim.Maps;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Rendering;

/// <summary>
/// Draws a preset building's parts with its look's art kit (<see cref="BuildingKitDef"/>, docs/ART.md
/// "Building kits") instead of generic blocks. Walls are pieced together per cell from the plan:
/// a straight run gets the kit's span, a corner, an end or a T gets its post with an arm towards
/// each neighbour, a door its frame. The roof is pitched over the whole plot, but each roof cell
/// draws only its own share (slopes, eaves, gables, barge boards, the ridge), so chunks mesh
/// independently. Pure (no GPU), like <see cref="PropModel"/>: shared by the chunk mesher, the
/// ghosts of a phase going up, and tests.
/// </summary>
public static class BuildingKit
{
    /// <summary>The building and kit a part is drawn with, if it belongs to a preset building whose look has one.</summary>
    public static (PresetBuilding Building, BuildingKitDef Kit)? Of(Simulation sim, Entity part)
    {
        var e = Buildings.Of(sim, part);
        if (e.IsNull)
            return null;
        var b = sim.World.Get<PresetBuilding>(e);
        return b.VariantDef.KitDef is { } kit ? (b, kit) : null;
    }

    private static bool Joins(char symbol) => symbol is 'W' or 'D' or 'P';

    /// <summary>A box wholly this low is the footing a wall stands on: drawn on the ground storey only.</summary>
    public const float FootingHeight = 0.12f;

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<IReadOnlyList<ModelBox>, IReadOnlyList<ModelBox>> Raised = new();

    /// <summary>A piece without its footing, for an upper storey standing on the walls below.</summary>
    private static IReadOnlyList<ModelBox> WithoutFooting(IReadOnlyList<ModelBox> model) =>
        Raised.GetValue(model, m => m.Where(box => box.Max[2] > FootingHeight).ToList());

    /// <summary>The kit's models for a wall, post or door cell, each with its turn (made along X).</summary>
    public static IEnumerable<(IReadOnlyList<ModelBox> Model, int Facing)> WallPieces(BuildingKitDef kit, PresetBuilding b, int3 cell)
    {
        if (cell.Z == b.Anchor.Z)
            return GroundPieces(kit, b, cell);
        return GroundPieces(kit, b, cell).Select(p => (WithoutFooting(p.Model), p.Facing));
    }

    private static IEnumerable<(IReadOnlyList<ModelBox> Model, int Facing)> GroundPieces(BuildingKitDef kit, PresetBuilding b, int3 cell)
    {
        char At(int dx, int dy) => Buildings.SymbolAt(b.Def, b.Anchor, b.Facing, new int3(cell.X + dx, cell.Y + dy, cell.Z));
        var symbol = At(0, 0);
        bool e = Joins(At(1, 0)), w = Joins(At(-1, 0)), s = Joins(At(0, 1)), n = Joins(At(0, -1));
        if (symbol == 'D')
        {
            yield return (kit.Door, e || w ? 0 : 1);
            yield break;
        }
        if (symbol == 'W' && e && w && !n && !s)
        {
            yield return (kit.Span, 0);
            yield break;
        }
        if (symbol == 'W' && n && s && !e && !w)
        {
            yield return (kit.Span, 1);
            yield break;
        }
        // Each post's end grain turned its own way, so a row of them doesn't look stamped.
        yield return (symbol == 'P' ? kit.Post : kit.Corner, (int)(Variation.Hash(cell.X, cell.Y, 0xB057) & 3));
        // Facings turn the +X arm: 1 to +Y, 2 to -X, 3 to -Y (PropModel.Rotate).
        if (e) yield return (kit.Arm, 0);
        if (s) yield return (kit.Arm, 1);
        if (w) yield return (kit.Arm, 2);
        if (n) yield return (kit.Arm, 3);
    }

    /// <summary>Fixed light per direction, as <see cref="PropModel"/> and the terrain use: east, west, south, north.</summary>
    private static float SideShade(float2 d) =>
        MathF.Abs(d.X) > MathF.Abs(d.Y) ? (d.X > 0 ? 0.78f : 0.66f) : (d.Y > 0 ? 0.88f : 0.6f);

    private static uint Shade(float s)
    {
        var b = (uint)(Math.Clamp(s, 0, 1) * 255f);
        return b | b << 8 | b << 16 | b << 24;
    }

    /// <summary>
    /// A roof cell's share of the building's pitched roof. The ridge runs along the plot's longer
    /// side; the slopes rest on the side walls' middles and fall past them as eaves, the roof
    /// reaches past the gable walls, and the gables are boarded up to it under barge boards that
    /// cross above the ridge.
    /// </summary>
    /// <param name="sprite">
    /// A sprite name (with the part's material) to its art, picking among its variants
    /// (<c>_v2</c>...) by the given number, so long slopes don't repeat cell by cell.
    /// </param>
    public static void Roof(List<Quad> output, RoofKit roof, PresetBuilding b, int3 cell, Func<string, uint, (Sprite Sprite, uint Tint)> sprite, uint color, uint flags)
    {
        var def = b.Def;
        var back = Footprint.Rotate(cell.X - b.Anchor.X, cell.Y - b.Anchor.Y, (4 - b.Facing) & 3);
        int col = back.X - Footprint.Lo(def.Width), row = back.Y - Footprint.Lo(def.Depth);
        if (def.At(col, row) == ' ')
            return;
        var alongX = def.Width >= def.Depth;
        int length = alongX ? def.Width : def.Depth, span = alongX ? def.Depth : def.Width;
        int a0 = alongX ? col : row, c0 = alongX ? row : col;
        var floor = (float)cell.Z; // the tops of the walls
        var mid = span / 2f;
        var t = roof.Thickness;

        // Plan space (a along the ridge, c across it, z up) to the world, turned with the building.
        float3 P(float a, float c, float z)
        {
            var px = alongX ? a : c;
            var py = alongX ? c : a;
            var d = Footprint.Rotate(px + Footprint.Lo(def.Width) - 0.5f, py + Footprint.Lo(def.Depth) - 0.5f, b.Facing);
            return new float3(b.Anchor.X + 0.5f + d.X, b.Anchor.Y + 0.5f + d.Y, z);
        }
        float2 Dir(float da, float dc)
        {
            var d = Footprint.Rotate(alongX ? da : dc, alongX ? dc : da, b.Facing);
            return new float2(d.X, d.Y);
        }
        // The underside: on the side walls' middles (c = 0.5 and span - 0.5), rising to the ridge.
        float H(float c) => floor + roof.Pitch * (Math.Min(c, span - c) - 0.5f);
        var pick = Variation.Hash(cell.X, cell.Y, 0x500F);
        void Add(float3 origin, float3 u, float3 v, string name, float shade, bool flip = false)
        {
            if (string.IsNullOrEmpty(name))
                return;
            var (art, tint) = sprite(name, name == roof.Sprite ? pick : 0);
            var uv = flip ? new float4(art.Uv.Z, art.Uv.Y, art.Uv.X, art.Uv.W) : art.Uv;
            output.Add(new Quad { Origin = origin, AxisU = u, AxisV = v, Flags = flags, Color = PropModel.Multiply(color, tint), Ao = Shade(shade), Uv = uv });
        }

        float aLo = a0 == 0 ? 0.5f - roof.Overhang : a0, aHi = a0 == length - 1 ? length - 0.5f + roof.Overhang : a0 + 1;
        float cLo = c0 == 0 ? 0.5f - roof.Eave : c0, cHi = c0 == span - 1 ? span - 0.5f + roof.Eave : c0 + 1;
        List<(float A, float B)> pieces = cLo < mid && cHi > mid ? [(cLo, mid), (mid, cHi)] : [(cLo, cHi)];
        var gableEnds = new List<float>();
        if (a0 == 0) gableEnds.Add(aLo);
        if (a0 == length - 1) gableEnds.Add(aHi);

        foreach (var (ca, cb) in pieces)
        {
            var rising = H(cb) > H(ca);
            var outward = Dir(0, rising ? -1 : 1);
            // The slope's top, lit as mostly facing the sky.
            var o = P(aLo, ca, H(ca) + t);
            Add(o, P(aHi, ca, H(ca) + t) - o, P(aLo, cb, H(cb) + t) - o, roof.Sprite, 0.62f + 0.38f * SideShade(outward), flip: (pick & 0x100) != 0);
            // The eave's edge: a lip of turf hanging over it, or the trim board.
            if (c0 == 0 && ca == cLo || c0 == span - 1 && cb == cHi)
            {
                var c = ca == cLo && c0 == 0 ? ca : cb;
                var e = P(aLo, c, H(c) + t);
                var lip = string.IsNullOrEmpty(roof.EaveTrim) ? (roof.Trim, t) : (roof.EaveTrim, t + roof.Lip);
                Add(e, P(aHi, c, H(c) + t) - e, new float3(0, 0, -lip.Item2), lip.Item1, SideShade(Dir(0, c < mid ? -1 : 1)));
            }

            foreach (var aEdge in gableEnds)
            {
                var outwardA = aEdge < 1 ? -1 : 1;
                var shade = SideShade(Dir(outwardA, 0));
                // Barge board over the roof's edge, carried past the ridge so the pair cross there.
                var slope = (H(cb) - H(ca)) / (cb - ca);
                float c1 = ca, c2 = cb;
                if (rising && Math.Abs(cb - mid) < 0.001f) c2 = mid + roof.Horns;
                if (!rising && Math.Abs(ca - mid) < 0.001f) c1 = mid - roof.Horns;
                float Z(float c) => H(ca) + slope * (c - ca) + t + 0.05f;
                var edge = aEdge + outwardA * 0.03f;
                var bo = P(edge, c1, Z(c1));
                Add(bo, P(edge, c2, Z(c2)) - bo, new float3(0, 0, -(t + 0.14f)), roof.Trim, shade);

                // The gable: boards from the top of the end wall up to the roof, flush with the wall's outer face.
                var aG = aEdge < 1 ? 0.26f : length - 0.26f;
                float ga = Math.Max(ca, 0.5f), gb = Math.Min(cb, span - 0.5f);
                if (gb <= ga)
                    continue;
                float lower = Math.Min(H(ga), H(gb)), upper = Math.Max(H(ga), H(gb));
                if (lower > floor + 0.001f)
                {
                    var r = P(aG, ga, lower);
                    Add(r, P(aG, gb, lower) - r, new float3(0, 0, floor - lower), roof.GableFill, shade);
                }
                var g = P(aG, ga, upper);
                Add(g, P(aG, gb, upper) - g, new float3(0, 0, lower - upper), roof.Gable, shade, flip: !rising);
            }
        }

        // The ridge beam, from the cell the ridge runs through (or along whose edge it runs).
        if (!string.IsNullOrEmpty(roof.Ridge) && mid >= c0 && mid < c0 + 1)
        {
            var apex = H(mid) + t;
            float r0 = mid - 0.09f, r1 = mid + 0.09f, z0 = apex - 0.03f, z1 = apex + 0.09f;
            var top = P(aLo, r0, z1);
            Add(top, P(aHi, r0, z1) - top, P(aLo, r1, z1) - top, roof.Ridge, 1f);
            foreach (var (c, sign) in new[] { (r0, -1), (r1, 1) })
            {
                var side = P(aLo, c, z1);
                Add(side, P(aHi, c, z1) - side, new float3(0, 0, z0 - z1), roof.Ridge, SideShade(Dir(0, sign)));
            }
            foreach (var aEdge in gableEnds)
            {
                var end = P(aEdge, r0, z1);
                Add(end, P(aEdge, r1, z1) - end, new float3(0, 0, z0 - z1), roof.Ridge, SideShade(Dir(aEdge < 1 ? -1 : 1, 0)));
            }
        }
    }
}

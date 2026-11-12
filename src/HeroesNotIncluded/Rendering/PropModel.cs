using Narsil.Rendering;
using HeroesNotIncluded.Sim.Defs;
using Maps = HeroesNotIncluded.Sim.Maps;

namespace HeroesNotIncluded.Rendering;

/// <summary>
/// Turns a <see cref="ThingDef.Model"/> (a few textured boxes) into <see cref="Quad"/>s for one
/// cell: turned by the thing's facing, shaded per face like walls (<c>WorldMesher.AddCube</c>), with
/// faces pressed flat against a neighbouring wall left out. Each face shows the part of its sprite
/// matching its size, so a 0.4-tall bed side keeps the 32 px per cell pixel scale instead of
/// squashing the whole sprite. Pure (no GPU), so it's shared by the chunk mesher (furniture), the
/// world renderer (item piles, blueprint ghosts) and tests.
/// </summary>
public static class PropModel
{
    /// <summary>Fixed light per face direction, matching the terrain cubes: top, east, west, south, north.</summary>
    private const float Top = 1f, East = 0.78f, West = 0.66f, South = 0.88f, North = 0.6f;

    /// <summary>
    /// Append a model's quads. <paramref name="sprite"/> resolves a sprite name (with the thing's
    /// material) to its art and tint; <paramref name="occluded"/> says whether the cell at a map
    /// offset from <paramref name="cell"/> hides faces flush against it (a wide model's far end asks
    /// about its own neighbours); <paramref name="count"/> filters boxes by
    /// <see cref="ModelBox.MinCount"/>; <paramref name="rise"/> (0..1) shows only the bottom part,
    /// for buildings going up; <paramref name="heightScale"/> squashes it up the way (furniture).
    /// </summary>
    public static void Build(
        List<Quad> output,
        IReadOnlyList<ModelBox> model,
        int3 cell,
        int facing,
        Func<string, (Sprite Sprite, uint Tint)> sprite,
        uint color,
        uint flags,
        Func<int2, bool>? occluded = null,
        int count = int.MaxValue,
        float rise = 1f,
        float lift = 0f,
        float heightScale = 1f
    )
    {
        var origin = new float3(cell.X, cell.Y, cell.Z + lift);
        foreach (var box in model)
        {
            if (count < box.MinCount)
                continue;
            // Up the way, scaled (furniture stands lower than it's modelled: WorldScale.FurnitureHeight).
            var m = new float3(box.Min[0], box.Min[1], box.Min[2] * heightScale);
            var M = new float3(box.Max[0], box.Max[1], Math.Min(box.Max[2], box.Min[2] + (box.Max[2] - box.Min[2]) * rise) * heightScale);
            if (M.Z <= m.Z)
                continue;
            float dx = M.X - m.X, dy = M.Y - m.Y, dz = M.Z - m.Z;
            var down = new float3(0, 0, -dz);
            var side = string.IsNullOrEmpty(box.Side) ? box.Top : box.Side;
            var front = string.IsNullOrEmpty(box.Front) ? side : box.Front;

            Face(box.Top, new float3(m.X, m.Y, M.Z), new float3(dx, 0, 0), new float3(0, dy, 0), dx, dy, top: true);
            Face(side, new float3(M.X, M.Y, M.Z), new float3(0, -dy, 0), down, dy, dz, edge: OnEdge(M.X));   // +X
            Face(side, new float3(m.X, m.Y, M.Z), new float3(0, dy, 0), down, dy, dz, edge: OnEdge(m.X));    // -X
            Face(front, new float3(m.X, M.Y, M.Z), new float3(dx, 0, 0), down, dx, dz, edge: OnEdge(M.Y));   // +Y, the front
            Face(side, new float3(M.X, m.Y, M.Z), new float3(-dx, 0, 0), down, dx, dz, edge: OnEdge(m.Y));   // -Y
        }

        static bool OnEdge(float v) => MathF.Abs(v - MathF.Round(v)) < 0.001f;

        void Face(string name, float3 p, float3 u, float3 v, float w, float h, bool top = false, bool edge = false)
        {
            if (string.IsNullOrEmpty(name))
                return;
            var local = Normal(Cross(u, v));
            var n = Rotate(local, facing);
            // A side flush with a cell edge, against a wall or rock: nobody sees it. Which cell is
            // beyond it: the one the face's middle is in, one step along its normal.
            if (edge && occluded != null)
            {
                var mid = p + u * 0.5f + v * 0.5f - local * 0.01f;
                var beyond = Maps.Footprint.Rotate((int)MathF.Floor(mid.X) + (int)MathF.Round(local.X), (int)MathF.Floor(mid.Y) + (int)MathF.Round(local.Y), facing);
                if (occluded(beyond))
                    return;
            }
            var (art, tint) = sprite(name);
            var uv = art.Uv;
            var shade = top ? Top : n.X > 0.5f ? East : n.X < -0.5f ? West : n.Y > 0.5f ? South : North;
            var b = (uint)(shade * 255f);
            if (top)
            {
                output.Add(new Quad
                {
                    Origin = origin + RotatePoint(p, facing),
                    AxisU = Rotate(u, facing),
                    AxisV = Rotate(v, facing),
                    Flags = flags,
                    Color = Multiply(color, tint),
                    Ao = b | b << 8 | b << 16 | b << 24,
                    Uv = new float4(uv.X, uv.Y, uv.X + (uv.Z - uv.X) * Math.Min(w, 1f), uv.Y + (uv.W - uv.Y) * Math.Min(h, 1f)),
                });
                return;
            }
            // Sides look WorldScale.LevelHeight times taller than they are: stack the art up them (its
            // top row at the box's top, as before) so the pixels stay square.
            var bottom = (uint)(shade * 0.8f * 255f); // sides darken towards the floor
            var across = new float4(uv.X, uv.Y, uv.X + (uv.Z - uv.X) * Math.Min(w, 1f), uv.W);
            var template = new Quad { Flags = flags, Color = Multiply(color, tint) };
            var tiles = WorldScale.TilesPerLevel(art);
            var count = WorldScale.TileCount(h, tiles);
            for (var k = 0; k < count; k++)
            {
                var q = WorldScale.Tile(template, origin + RotatePoint(p, facing), Rotate(u, facing), h, across, tiles, k);
                q.Ao = k == count - 1 ? b | b << 8 | bottom << 16 | bottom << 24 : b | b << 8 | b << 16 | b << 24;
                output.Add(q);
            }
        }
    }

    /// <summary>
    /// Quarter turns about the vertical axis, clockwise seen from above: each turn swings the
    /// model's front from south to west, then north, then east.
    /// </summary>
    public static float3 Rotate(float3 v, int facing)
    {
        // The sim's footprints turn the same way (Maps.Footprint), so a model stays on its cells.
        var r = Maps.Footprint.Rotate(v.X, v.Y, facing);
        return new float3(r.X, r.Y, v.Z);
    }

    /// <summary>A point in the cell, turned about the cell's centre.</summary>
    public static float3 RotatePoint(float3 p, int facing)
    {
        var r = Rotate(new float3(p.X - 0.5f, p.Y - 0.5f, p.Z), facing);
        return new float3(r.X + 0.5f, r.Y + 0.5f, r.Z);
    }

    /// <summary>Which map direction a model's front (+Y) faces once turned.</summary>
    public static int2 FrontDirection(int facing)
    {
        var f = Rotate(new float3(0, 1, 0), facing);
        return new int2((int)MathF.Round(f.X), (int)MathF.Round(f.Y));
    }

    private static float3 Cross(float3 a, float3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);

    private static float3 Normal(float3 v)
    {
        var len = MathF.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        return len > 0 ? new float3(v.X / len, v.Y / len, v.Z / len) : v;
    }

    /// <summary>
    /// Height of the model's surface at a point of the cell (unturned model space, 0..1), for
    /// things drawn lying on it (a sleeper on a bed's mattress and pillow); 0 if nothing is there.
    /// </summary>
    public static float SurfaceAt(IReadOnlyList<ModelBox> model, float x, float y)
    {
        var top = 0f;
        foreach (var b in model)
            if (b.MinCount == 0 && b.Min[0] <= x && b.Max[0] >= x && b.Min[1] <= y && b.Max[1] >= y)
                top = Math.Max(top, b.Max[2]);
        return top;
    }

    /// <summary>Two packed <see cref="Rgba"/> colours multiplied channel by channel.</summary>
    public static uint Multiply(uint a, uint b)
    {
        if (b == Rgba.White)
            return a;
        uint C(uint x, uint y, int shift) => ((x >> shift & 0xFF) * (y >> shift & 0xFF) + 127) / 255 << shift;
        return C(a, b, 0) | C(a, b, 8) | C(a, b, 16) | C(a, b, 24);
    }
}

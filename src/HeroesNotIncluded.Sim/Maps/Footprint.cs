using SupportingCast.Sim.Defs;

namespace SupportingCast.Sim.Maps;

/// <summary>
/// The cells a structure wider than one cell covers (<see cref="ThingDef.SizeX"/> ×
/// <see cref="ThingDef.SizeY"/>), turned by its facing. Each axis runs from <see cref="Lo"/> to
/// <c>Lo + n - 1</c> around the anchor (<c>Thing.Cell</c>): an odd size is centred on it (the 3x3
/// nest), an even size grows towards +X and the front (+Y), so a 1x2 bed's headboard sits in the
/// anchor and its foot one cell in front. Quarter turns use the same convention as the game's
/// <c>PropModel.Rotate</c>, which calls <see cref="Rotate(float, float, int)"/>, so a model and the
/// cells it stands on can't drift apart.
/// </summary>
public static class Footprint
{
    /// <summary>The lowest offset along an axis <paramref name="n"/> cells long.</summary>
    public static int Lo(int n) => -((n - 1) / 2);

    public static bool IsWide(ThingDef def) => def.SizeX > 1 || def.SizeY > 1;

    /// <summary>A model-space offset turned clockwise (seen from above) by quarter turns.</summary>
    public static int2 Rotate(int dx, int dy, int facing) => (facing & 3) switch
    {
        1 => new int2(-dy, dx),
        2 => new int2(-dx, -dy),
        3 => new int2(dy, -dx),
        _ => new int2(dx, dy),
    };

    /// <summary>The same turn for a float offset (relative to the anchor cell's centre).</summary>
    public static float2 Rotate(float dx, float dy, int facing) => (facing & 3) switch
    {
        1 => new float2(-dy, dx),
        2 => new float2(-dx, -dy),
        3 => new float2(dy, -dx),
        _ => new float2(dx, dy),
    };

    /// <summary>
    /// The covered cells, anchor among them, in a fixed order (model rows, then columns). Cells
    /// off the map come back as (-1, -1, -1) so placement fails rather than silently shrinking.
    /// </summary>
    public static IEnumerable<int3> Cells(Map map, ThingDef def, int3 anchor, int facing)
    {
        int lx = Lo(def.SizeX), ly = Lo(def.SizeY);
        for (var y = ly; y < ly + def.SizeY; y++)
        for (var x = lx; x < lx + def.SizeX; x++)
        {
            var d = Rotate(x, y, facing);
            var c = new int3(anchor.X + d.X, anchor.Y + d.Y, anchor.Z);
            yield return map.InBounds(c) ? c : new int3(-1, -1, -1);
        }
    }

    /// <summary>Whether <paramref name="cell"/> is one of the footprint's cells.</summary>
    public static bool Contains(ThingDef def, int3 anchor, int facing, int3 cell)
    {
        if (cell.Z != anchor.Z)
            return false;
        // Undo the turn: the quarter turns that make it up to a whole one.
        var d = Rotate(cell.X - anchor.X, cell.Y - anchor.Y, (4 - facing) & 3);
        int lx = Lo(def.SizeX), ly = Lo(def.SizeY);
        return d.X >= lx && d.X < lx + def.SizeX && d.Y >= ly && d.Y < ly + def.SizeY;
    }

    /// <summary>
    /// The footprint's centre in world x/y (cell corners at integers, so a 1x1 at (3, 4) is
    /// centred on (3.5, 4.5)): where a sleeper lies on a long bed.
    /// </summary>
    public static float2 Centre(ThingDef def, int3 anchor, int facing)
    {
        var lx = Lo(def.SizeX) + (def.SizeX - 1) / 2f;
        var ly = Lo(def.SizeY) + (def.SizeY - 1) / 2f;
        var d = Rotate(lx, ly, facing);
        return new float2(anchor.X + 0.5f + d.X, anchor.Y + 0.5f + d.Y);
    }

    /// <summary>
    /// The cells touching the footprint's sides (not its corners) from outside it, in a fixed
    /// order: the front row first, then the sides, then the back. Off-map cells are left out.
    /// Where to stand to work at a wide bench without standing on it.
    /// </summary>
    public static IEnumerable<int3> Beside(Map map, ThingDef def, int3 anchor, int facing)
    {
        int lx = Lo(def.SizeX), ly = Lo(def.SizeY), hx = lx + def.SizeX - 1, hy = ly + def.SizeY - 1;
        IEnumerable<int2> Local()
        {
            for (var x = lx; x <= hx; x++) yield return new int2(x, hy + 1);   // the front
            for (var y = hy; y >= ly; y--) yield return new int2(lx - 1, y);   // the sides, front end first
            for (var y = hy; y >= ly; y--) yield return new int2(hx + 1, y);
            for (var x = lx; x <= hx; x++) yield return new int2(x, ly - 1);   // the back
        }
        foreach (var l in Local())
        {
            var d = Rotate(l.X, l.Y, facing);
            var c = new int3(anchor.X + d.X, anchor.Y + d.Y, anchor.Z);
            if (map.InBounds(c))
                yield return c;
        }
    }
}

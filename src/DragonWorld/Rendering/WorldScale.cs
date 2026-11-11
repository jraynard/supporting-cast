using Narsil.Rendering;

namespace DragonWorld.Rendering;

/// <summary>
/// How tall a map level looks. A cell is 32 px wide, but people are 56 px tall and the adult dragon
/// 96, so a one-cell-tall wall barely reached their waists. The camera draws world height stretched
/// by <see cref="LevelHeight"/> (<see cref="Camera3D.VerticalScale"/>), so walls, doors, cliffs,
/// stairs and furniture stand 64 px tall while camera-facing sprites keep their size. The sim's
/// cells are untouched: this is presentation only.
/// <para>
/// Vertical faces stack their texture so its pixels stay square (<see cref="TilesPerLevel"/>): a
/// 32×32 texture repeats twice up a level, art drawn 32×64 (doors, ramp sides) shows once.
/// Offsets measured against a sprite (a bubble over a head, a held item) go through
/// <see cref="Rise"/>; offsets measured against blocks (a bar over a wall) stay in world z.
/// </para>
/// </summary>
public static class WorldScale
{
    public const float LevelHeight = 2f;

    /// <summary>A world offset that rises <paramref name="cells"/> sprite cells: world z divided by the stretch.</summary>
    public static float3 Rise(float cells) => new(0, 0, cells / LevelHeight);

    /// <summary>How many copies of a sprite stack up a face one level tall, keeping its pixels square.</summary>
    public static int TilesPerLevel(in Sprite s) =>
        Math.Max(1, (int)MathF.Round(LevelHeight * s.PixelSize.X / Math.Max(1f, s.PixelSize.Y)));

    /// <summary>Tiles a vertical face <paramref name="height"/> (world) tall needs.</summary>
    public static int TileCount(float height, int tilesPerLevel) =>
        Math.Max(1, (int)MathF.Ceiling(height * tilesPerLevel - 1e-3f));

    /// <summary>
    /// Tile <paramref name="k"/> of a vertical face hanging from <paramref name="top"/> (V runs
    /// down), <paramref name="height"/> tall, across <paramref name="u"/>. Top-anchored faces keep the
    /// texture's top row at the face's top and crop the last tile at the bottom (furniture, as box
    /// faces always showed the top of their art); bottom-anchored faces crop at the top (a wall rising
    /// as it's built). The template supplies flags, colour and shading.
    /// </summary>
    public static Quad Tile(in Quad template, float3 top, float3 u, float height, float4 uv, int tilesPerLevel, int k, bool fromBottom = false)
    {
        var t = 1f / tilesPerLevel;
        float hi, lo;
        if (fromBottom)
        {
            lo = top.Z - height + k * t;
            hi = MathF.Min(lo + t, top.Z);
        }
        else
        {
            hi = top.Z - k * t;
            lo = MathF.Max(hi - t, top.Z - height);
        }
        var frac = Math.Clamp((hi - lo) / t, 0f, 1f);
        var span = uv.W - uv.Y;
        var tileUv = fromBottom
            ? new float4(uv.X, uv.W - span * frac, uv.Z, uv.W)
            : new float4(uv.X, uv.Y, uv.Z, uv.Y + span * frac);
        var q = template;
        q.Origin = new float3(top.X, top.Y, hi);
        q.AxisU = u;
        q.AxisV = new float3(0, 0, lo - hi);
        q.Uv = tileUv;
        return q;
    }
}

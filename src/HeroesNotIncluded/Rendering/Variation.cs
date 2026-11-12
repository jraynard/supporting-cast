using Narsil.Rendering;
using HeroesNotIncluded.Sim.Core;

namespace HeroesNotIncluded.Rendering;

/// <summary>
/// Cosmetic per-cell variety: which sprite variant a cell shows, whether it's mirrored, and smooth
/// value noise for patches (wildflowers, tree species art). Everything is a pure function of the
/// cell coordinates, so the same map always looks the same and remeshing never flickers. Never
/// feeds back into the sim.
/// </summary>
public static class Variation
{
    public static uint Hash(int x, int y, uint salt) => SimRandom.Hash(x, y, salt);

    /// <summary>A uniform 0..1 value per cell.</summary>
    public static float Unit(int x, int y, uint salt) => (Hash(x, y, salt) & 0xFFFFFF) / (float)0x1000000;

    /// <summary>One of <paramref name="variants"/>, chosen per cell.</summary>
    public static Sprite Pick(IReadOnlyList<Sprite> variants, int x, int y, uint salt) =>
        variants.Count == 1 ? variants[0] : variants[(int)(Hash(x, y, salt) % (uint)variants.Count)];

    /// <summary>Mirror a sprite's UVs horizontally and/or vertically (cheap variety for tiles that don't have a "right way up").</summary>
    public static Sprite Mirror(in Sprite s, bool u, bool v)
    {
        var uv = s.Uv;
        if (u)
            uv = new float4(uv.Z, uv.Y, uv.X, uv.W);
        if (v)
            uv = new float4(uv.X, uv.W, uv.Z, uv.Y);
        return s with { Uv = uv };
    }

    /// <summary>Smooth value noise in 0..1 with features about <paramref name="scale"/> cells across.</summary>
    public static float Noise(float x, float y, uint salt, float scale)
    {
        x /= scale;
        y /= scale;
        var x0 = (int)MathF.Floor(x);
        var y0 = (int)MathF.Floor(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        var a = Unit(x0, y0, salt);
        var b = Unit(x0 + 1, y0, salt);
        var c = Unit(x0, y0 + 1, salt);
        var d = Unit(x0 + 1, y0 + 1, salt);
        return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
    }

    /// <summary>Two octaves of <see cref="Noise"/>: blobby patches with ragged edges.</summary>
    public static float Patches(int x, int y, uint salt, float scale) =>
        Noise(x, y, salt, scale) * 0.7f + Noise(x, y, salt + 1, scale * 0.4f) * 0.3f;
}

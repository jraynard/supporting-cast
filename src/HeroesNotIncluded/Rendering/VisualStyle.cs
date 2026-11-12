using Narsil;
using Narsil.Rendering;

namespace HeroesNotIncluded.Rendering;

/// <summary>
/// The HD-2D look (<see cref="Settings.Hd2d"/>) with a cool Nordic grade: the player's strengths turned into Narsil's
/// post-processing settings, plus the glow boost that lets fire bloom. Off is a plain copy of the
/// scene with fire drawn as authored, so F10 compares the two directly.
/// </summary>
public static class VisualStyle
{
    /// <summary>How bright <see cref="Quad.Glow"/> quads are drawn with HD-2D on (bloom catches what's over 1).</summary>
    public const float GlowBoost = 3f;

    /// <summary>How far the grade leans cool at the slider's default (the shader scales warmth by 8% per unit).</summary>
    public const float NordicChill = 0.6f;

    public static PostSettings For(Settings s) => !s.Hd2d
        ? PostSettings.Off
        : PostSettings.Off with
        {
            TiltShift = s.TiltShift,
            FocusY = 0.5f,
            FocusBand = 0.16f,
            Bloom = s.Bloom * 4f, // only what's over 1 (glowing quads) blooms, so it can be strong
            BloomThreshold = 1f,
            Vignette = s.Vignette,
            // The Nordic grade: the slider's middle is a cool, slightly muted picture (the world is cold
            // and spruce-green); sliding it up brings gold back, down pushes it toward frost.
            Warmth = (s.Warmth - 0.5f) * 2f - NordicChill,
            Saturation = 0.94f + 0.12f * s.Warmth,
            Contrast = 1.08f,
        };

    public static void Apply(GameHost host, QuadRenderer quads)
    {
        var s = Settings.Current;
        host.Post.Settings = For(s);
        quads.GlowBoost = s.Hd2d ? GlowBoost : 1f;
    }
}

namespace HeroesNotIncluded.Rendering;

/// <summary>
/// The colour hot iron shows at a heat, shared by the forge's mini-game scenes and the world (the
/// piece in a smith's tongs, in the coals, on the anvil), so a piece reads the same wherever it's
/// seen. Heat is only ever shown this way, never as a number: a smith reads iron by eye.
/// </summary>
public static class ForgeGlow
{
    // Black through dull red, cherry, orange and yellow to white: what hot iron looks like.
    private static readonly (float Temp, float4 Colour)[] Ramp =
    [
        (430f, new float4(0.27f, 0.26f, 0.28f, 1f)),
        (520f, new float4(0.36f, 0.09f, 0.07f, 1f)),
        (650f, new float4(0.58f, 0.11f, 0.06f, 1f)),
        (760f, new float4(0.82f, 0.17f, 0.08f, 1f)),
        (870f, new float4(0.96f, 0.38f, 0.10f, 1f)),
        (1000f, new float4(1f, 0.62f, 0.17f, 1f)),
        (1150f, new float4(1f, 0.86f, 0.42f, 1f)),
        (1300f, new float4(1f, 0.97f, 0.88f, 1f)),
    ];

    /// <summary>The colour iron glows at a heat.</summary>
    public static float4 Colour(float temp)
    {
        if (temp <= Ramp[0].Temp)
            return Ramp[0].Colour;
        for (var i = 1; i < Ramp.Length; i++)
            if (temp <= Ramp[i].Temp)
            {
                var (t0, c0) = Ramp[i - 1];
                var (t1, c1) = Ramp[i];
                return c0 + (c1 - c0) * ((temp - t0) / (t1 - t0));
            }
        return Ramp[^1].Colour;
    }

    /// <summary>How much light hot iron throws, 0..1 (none below a dull red).</summary>
    public static float Strength(float temp) => Math.Clamp((temp - 480f) / 700f, 0f, 1f);
}

namespace SupportingCast.Sim.Environment;

/// <summary>
/// Outdoor temperature over the day. Cold nights are the main pressure in the slice:
/// the egg needs a fire, humans need shelter.
/// </summary>
public sealed class Climate(Simulation sim)
{
    /// <summary>Daily mean outdoor temperature.</summary>
    public float BaseTemperature { get; set; } = 8f;

    /// <summary>Half of the day/night swing.</summary>
    public float DailySwing { get; set; } = 8f;

    /// <summary>Dev/debug offset added on top (cold snap, heat wave).</summary>
    public float Offset { get; set; }

    public float OutdoorTemperature
    {
        get
        {
            // Coldest around 04:00, warmest around 16:00.
            var h = sim.Clock.HourOfDay;
            var phase = (h - 10f) / 24f * MathF.Tau;
            return BaseTemperature + DailySwing * DetMath.Sin(phase) + Offset;
        }
    }
}

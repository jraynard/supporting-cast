namespace HeroesNotIncluded.Sim.Core;

/// <summary>
/// Game time. The sim advances in fixed ticks; 60 ticks are one game second at 1x speed.
/// A day is 24 game-hours of <see cref="TicksPerHour"/> ticks each.
/// </summary>
public sealed class SimClock
{
    public const int TicksPerSecond = 60;
    public const int TicksPerHour = 2400;   // 40 real seconds per in-game hour at 1x (divides by 12, 4, 6, 10, 15)
    public const int TicksPerDay = TicksPerHour * 24; // 16 real minutes per day at 1x

    public long Tick { get; internal set; }

    /// <summary>Hour the game started at, so day one doesn't begin at midnight.</summary>
    public int StartHour { get; internal set; } = 8;

    public long TotalTicks => Tick + StartHour * (long)TicksPerHour;
    public int Day => (int)(TotalTicks / TicksPerDay) + 1;
    public float HourOfDay => TotalTicks % TicksPerDay / (float)TicksPerHour;

    /// <summary>0 at midnight, 1 at noon.</summary>
    public float Daylight
    {
        get
        {
            // Smooth curve: dark 20:00-05:00, ramps at dawn/dusk.
            var h = HourOfDay;
            return h switch
            {
                < 5f => 0f,
                < 8f => (h - 5f) / 3f,
                < 17f => 1f,
                < 20f => 1f - (h - 17f) / 3f,
                _ => 0f,
            };
        }
    }

    public static float TicksToDays(long ticks) => ticks / (float)TicksPerDay;
    public static int DaysToTicks(float days) => (int)(days * TicksPerDay);
    public static int HoursToTicks(float hours) => (int)(hours * TicksPerHour);

    public string TimeString()
    {
        var h = HourOfDay;
        var hour = (int)h;
        var minute = (int)((h - hour) * 60);
        return $"Day {Day}, {hour:00}:{minute:00}";
    }
}

using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Systems;

/// <summary>Slept two hours or more: a little quicker afoot and at work until <see cref="Until"/>.</summary>
public struct WellRested
{
    public long Until;
}

/// <summary>A meal still sitting well: quicker afoot and at work by <see cref="Strength"/> until <see cref="Until"/>.</summary>
public struct WellFed
{
    public long Until;

    /// <summary>0..1: how good the meal was (<see cref="WellBeing.FedStrength"/>).</summary>
    public float Strength;
}

/// <summary>What about a dish decides how well it feeds: the cook's grade, and whether it's the house dish.</summary>
public readonly record struct Meal(Quality Quality, bool House)
{
    public static readonly Meal Plain = new(Quality.Plain, false);

    /// <summary>What <paramref name="item"/> (a food stack) is, as a meal.</summary>
    public static Meal Of(Simulation sim, Entity item)
    {
        var w = sim.World;
        if (!w.IsAlive(item))
            return Plain;
        var q = w.TryGet<Crafted>(item, out var c) ? c.Quality : Quality.Plain;
        return new Meal(q, w.TryGet<Dish>(item, out var d) && d.House);
    }
}

/// <summary>
/// Eating and sleeping are bonuses, never chores (docs/DESIGN.md, "Well rested, well fed"). Two hours'
/// sleep leaves you <see cref="WellRested"/> for <see cref="RestedHours"/> after you wake; a meal leaves you
/// <see cref="WellFed"/> for <see cref="FedHours"/>, the better the dish the more, so a good cook's pot is
/// worth crossing the square for. Skipping either costs nothing. <see cref="Needs"/> still runs underneath,
/// but only as the AI's appetite and drowsiness: what sends villagers to supper and to bed.
/// </summary>
public static class WellBeing
{
    /// <summary>Sleep it takes to wake rested, and how long that lasts once up.</summary>
    public const float SleepHours = 2f, RestedHours = 8f;

    /// <summary>How long a meal sits well.</summary>
    public const float FedHours = 4f;

    /// <summary>Rested: this much quicker at work and afoot.</summary>
    public const float RestedWork = 0.10f, RestedMove = 0.05f;

    /// <summary>Fed at full strength: this much quicker at work and afoot.</summary>
    public const float FedWork = 0.15f, FedMove = 0.05f;

    /// <summary>A house dish feeds this much better: the one the inn's known for.</summary>
    public const float HouseBonus = 0.1f;

    public static float GradeFactor(Quality q) => q switch
    {
        Quality.Crude => 0.6f,
        Quality.Fine => 1.3f,
        Quality.Masterwork => 1.6f,
        _ => 1f,
    };

    /// <summary>
    /// How well a meal of <paramref name="food"/> feeds, 0..1. Rot counts only when <paramref name="knowRot"/>:
    /// the sim knows it; a tooltip mustn't (a dish's rot is hidden), so it passes false.
    /// </summary>
    public static float FedStrength(ThingDef food, Meal meal, float rot = 0f, bool knowRot = true)
    {
        if (food.WellFed <= 0)
            return 0f;
        var s = food.WellFed * GradeFactor(meal.Quality) + (meal.House ? HouseBonus : 0f);
        if (knowRot)
            s *= 1f - Math.Clamp(rot, 0f, 1f);
        return Math.Clamp(s, 0f, 1f);
    }

    public static bool Rested(Simulation sim, Entity e) =>
        sim.World.TryGet<WellRested>(e, out var r) && r.Until > sim.Clock.Tick;

    /// <summary>How well fed <paramref name="e"/> is now, 0 when not.</summary>
    public static float Fed(Simulation sim, Entity e) =>
        sim.World.TryGet<WellFed>(e, out var f) && f.Until > sim.Clock.Tick ? f.Strength : 0f;

    /// <summary>Hours of a buff left (0 when it's gone).</summary>
    public static float HoursLeft(Simulation sim, long until) =>
        Math.Max(0f, (until - sim.Clock.Tick) / (float)SimClock.TicksPerHour);

    public static float WorkFactor(Simulation sim, Entity e) =>
        (Rested(sim, e) ? 1f + RestedWork : 1f) * (1f + FedWork * Fed(sim, e));

    public static float MoveFactor(Simulation sim, Entity e) =>
        (Rested(sim, e) ? 1f + RestedMove : 1f) * (1f + FedMove * Fed(sim, e));

    /// <summary>Whether a meal this good would do <paramref name="e"/> any more good than the one they've had.</summary>
    public static bool WouldFeed(Simulation sim, Entity e, float strength) =>
        strength > 0f && strength > Fed(sim, e) && FoodPoisoning.Severity(sim, e) <= 0f;

    /// <summary>
    /// <paramref name="e"/> has eaten a meal this good: a better one than the last sits well for
    /// <see cref="FedHours"/> from now; a lesser one changes nothing. Nothing stays down while ill.
    /// </summary>
    public static void Ate(Simulation sim, Entity e, float strength)
    {
        if (!WouldFeed(sim, e, strength) || !sim.World.Has<Creature>(e))
            return;
        sim.World.Add(e, new WellFed { Until = sim.Clock.Tick + SimClock.HoursToTicks(FedHours), Strength = strength });
    }

    /// <summary>Still asleep past <see cref="SleepHours"/>: rested until <see cref="RestedHours"/> after now.</summary>
    public static void Slept(Simulation sim, Entity e) =>
        sim.World.Add(e, new WellRested { Until = sim.Clock.Tick + SimClock.HoursToTicks(RestedHours) });

    /// <summary>Food poisoning takes the good of a meal with it.</summary>
    public static void Sickened(Simulation sim, Entity e) => sim.World.Remove<WellFed>(e);

    /// <summary>The words for a meal this good.</summary>
    public static string Tier(float strength) => strength switch
    {
        >= 0.85f => "feasted",
        >= 0.6f => "hearty",
        >= 0.35f => "well fed",
        _ => "fed",
    };
}

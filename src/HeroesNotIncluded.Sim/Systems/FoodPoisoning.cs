using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;

namespace SupportingCast.Sim.Systems;

/// <summary>
/// Spoiled food can make you ill. Past <see cref="SpoiledAt"/> rot, every meal of it is a chance
/// of food poisoning, rising to near certain as it nears rotting away and scaled by the eater's
/// <see cref="CreatureDef.FoodPoisonFactor"/> (dragons eat carrion and rarely mind). Cooking halves
/// a meal's rot (<see cref="Crafting"/>), so the cure for a larder that's turning is the campfire.
/// The roll is a hash of the eater and the tick, never <see cref="Simulation.Rand"/>, so eating
/// doesn't shift anyone else's rolls. Being ill (<see cref="Components.FoodPoisoned"/>) slows
/// walking and work and keeps the stomach from filling; it passes, sooner in bed, and never kills.
/// </summary>
public static class FoodPoisoning
{
    /// <summary>Rot progress past which food counts as spoiled.</summary>
    public const float SpoiledAt = 0.5f;

    /// <summary>How long the worst case lasts; a mild one lasts half as long.</summary>
    public const float MaxHours = 30f;

    /// <summary>At full severity: this much slower walking, and half again as much off work.</summary>
    public const float MoveLoss = 0.3f, WorkLoss = 0.5f;

    /// <summary>Food bar lost per hour at full severity (it doesn't stay down).</summary>
    public const float FoodLossPerHour = 0.03f;

    public static bool Spoiled(float rot) => rot >= SpoiledAt;

    /// <summary>The rot of a food stack or carcass (0 for things that don't rot).</summary>
    public static float RotOf(Simulation sim, Entity food) =>
        sim.World.TryGet<Rot>(food, out var r) ? r.Progress : 0f;

    /// <summary>Whether a stack has gone off.</summary>
    public static bool IsSpoiled(Simulation sim, Entity food) => Spoiled(RotOf(sim, food));

    /// <summary>The chance that eating food at <paramref name="rot"/> makes <paramref name="eater"/> ill.</summary>
    public static float Chance(CreatureDef eater, float rot) =>
        Math.Clamp((rot - SpoiledAt) / (1f - SpoiledAt), 0f, 1f) * eater.FoodPoisonFactor;

    /// <summary>
    /// After a creature has eaten food at <paramref name="rot"/>: a memory of eating spoiled food,
    /// and perhaps food poisoning (it takes the worse of what it has and what this one brings).
    /// </summary>
    public static void AfterEating(Simulation sim, Entity eater, float rot)
    {
        var w = sim.World;
        if (!Spoiled(rot) || !w.TryGet<Creature>(eater, out var c) || c.Dead)
            return;
        var chance = Chance(c.Def, rot);
        var roll = SimRandom.Hash(eater.Index, (int)sim.Clock.Tick, (uint)sim.Seed + 0xF00Du) / (float)uint.MaxValue;
        if (roll >= chance)
        {
            MoodSystem.Remember(sim, eater, sim.Defs.Thoughts.AteSpoiledFood);
            return;
        }
        var severity = Math.Clamp(0.4f + 0.6f * (rot - SpoiledAt) / (1f - SpoiledAt), 0f, 1f);
        var until = sim.Clock.Tick + (long)(MaxHours * (0.5f + 0.5f * severity) * SimClock.TicksPerHour);
        if (w.TryGet<Components.FoodPoisoned>(eater, out var had))
        {
            severity = Math.Max(severity, had.Severity);
            until = Math.Max(until, had.Until);
        }
        else if (c.Def.Race == Race.Human || w.Has<Avatar>(eater))
            sim.Messages.Add(MessageKind.Warning, $"{c.Name} has food poisoning from spoiled food.", w.Get<Position>(eater).Cell);
        w.Add(eater, new Components.FoodPoisoned { Until = until, Severity = severity });
    }

    /// <summary>How ill a creature is now, 0..1 (0 when well).</summary>
    public static float Severity(Simulation sim, Entity e) =>
        sim.World.TryGet<Components.FoodPoisoned>(e, out var p) && p.Until > sim.Clock.Tick ? p.Severity : 0f;

    public static float MoveFactor(Simulation sim, Entity e) => 1f - MoveLoss * Severity(sim, e);

    public static float WorkFactor(Simulation sim, Entity e) => 1f - WorkLoss * Severity(sim, e);

    /// <summary>For the inspector, or null when well.</summary>
    public static string? Describe(Simulation sim, Entity e)
    {
        if (!sim.World.TryGet<Components.FoodPoisoned>(e, out var p) || p.Until <= sim.Clock.Tick)
            return null;
        var hours = (p.Until - sim.Clock.Tick) / (float)SimClock.TicksPerHour;
        var how = p.Severity >= 0.75f ? "badly" : p.Severity >= 0.5f ? "" : "mildly";
        return $"Food poisoning{(how.Length > 0 ? $" ({how})" : "")}: slower, works less, keeps little down. About {Math.Max(1, (int)MathF.Ceiling(hours))} h more{(HealthSystem.InBed(sim, e) ? "" : ", sooner in bed")}.";
    }
}

/// <summary>Food poisoning runs its course: the stomach empties a little, and it passes faster in bed.</summary>
public sealed class FoodPoisoningSystem : ISimSystem
{
    public string Name => "FoodPoisoning";
    public int Interval => 250;

    private readonly List<Entity> _done = [];

    public void Update(Simulation sim)
    {
        var w = sim.World;
        var now = sim.Clock.Tick;
        var hours = Interval / (float)SimClock.TicksPerHour;
        var food = sim.Defs.AllNeeds.Find(n => n.Name == "Food");
        foreach (var e in w.Query<Components.FoodPoisoned>())
        {
            ref var p = ref w.Get<Components.FoodPoisoned>(e);
            if (!w.TryGet<Creature>(e, out var c) || c.Dead || p.Until <= now)
            {
                _done.Add(e);
                continue;
            }
            if (food != null && w.Has<Needs>(e))
                w.Get<Needs>(e).Add(food, -FoodPoisoning.FoodLossPerHour * p.Severity * hours);
            if (HealthSystem.InBed(sim, e))
                p.Until -= Interval / 2; // rest: half again as fast
        }
        foreach (var e in _done)
            if (w.IsAlive(e))
                w.Remove<Components.FoodPoisoned>(e);
        _done.Clear();
    }
}

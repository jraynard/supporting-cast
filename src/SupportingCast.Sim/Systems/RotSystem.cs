using SupportingCast.Sim.Components;

namespace SupportingCast.Sim.Systems;

/// <summary>
/// Food and animal carcasses go off and finally rot away: at full speed between 10 and 30°C,
/// slower in the cold, not at all frozen, up to twice as fast in real heat. A glut of meat
/// doesn't keep, so the village hunts and fishes steadily or builds somewhere cold to store it.
/// Only things with a <see cref="Rot"/> are visited, so the cost follows the food stacks, not the map.
/// Carried and stored-away (unspawned) items keep.
/// </summary>
public sealed class RotSystem : ISimSystem
{
    public string Name => "Rot";
    public int Interval => 250;

    /// <summary>Speed of rotting at a temperature, relative to the def's RotDays.</summary>
    public static float RateAt(float celsius) =>
        celsius <= 0 ? 0f
        : celsius < 10 ? celsius / 10f
        : celsius <= 30 ? 1f
        : Math.Min(2f, 1f + (celsius - 30f) / 30f);

    /// <summary>
    /// The temperature food keeps at: the air where it is, or for something in a store an ice
    /// dragon has chilled (<see cref="Chilled"/>, <see cref="Defs.ChillProps"/>), no warmer than the store's cold.
    /// </summary>
    public static float KeptAt(Simulation sim, Entity item, int3 cell)
    {
        var air = sim.TemperatureAt(cell);
        var w = sim.World;
        if (!w.TryGet<Stored>(item, out var stored) || !w.TryGet<Chilled>(stored.In, out var chilled) || chilled.Until < sim.Clock.Tick
            || !w.TryGet<Thing>(stored.In, out var store) || store.Def.Building?.Chill is not { } chill)
            return air;
        return Math.Min(air, chill.MinTemp);
    }

    public void Update(Simulation sim)
    {
        var w = sim.World;
        var step = Interval / (float)SimClock.TicksPerDay;
        foreach (var e in w.Query<Rot>())
        {
            float days;
            int3 cell;
            if (w.TryGet<Thing>(e, out var thing))
            {
                if (!thing.Spawned && !w.Has<Stored>(e))
                    continue; // carried: only briefly; stored in a container: rots there
                days = thing.Def.RotDays;
                cell = thing.Cell;
            }
            else if (w.TryGet<Creature>(e, out var c) && c.Dead && c.Def.Animal is { } animal && !w.Has<Carried>(e))
            {
                days = animal.CorpseRotDays;
                cell = w.Get<Position>(e).Cell;
            }
            else
                continue;
            if (days <= 0)
                continue;

            ref var rot = ref w.Get<Rot>(e);
            rot.Progress += step / days * RateAt(KeptAt(sim, e, cell));
            if (rot.Progress < 1f)
                continue;

            if (w.Has<Thing>(e))
            {
                sim.Messages.AddThrottled($"rot:{thing.Def.Name}", SimClock.TicksPerHour * 4, MessageKind.Warning,
                    $"Some {thing.Def.Label} has rotted away.", cell);
                sim.DestroyThing(e);
            }
            else
            {
                sim.Messages.AddThrottled("rot:carcass", SimClock.TicksPerHour * 4, MessageKind.Info,
                    $"A {w.Get<Creature>(e).Def.Label} carcass has rotted away.", cell);
                w.Destroy(e);
            }
        }
    }
}

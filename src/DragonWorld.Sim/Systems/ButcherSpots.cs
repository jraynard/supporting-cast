using DragonWorld.Sim.Ai;
using DragonWorld.Sim.Components;
using DragonWorld.Sim.Defs;
using DragonWorld.Sim.Maps;

namespace DragonWorld.Sim.Systems;

/// <summary>
/// A butcher spot (<see cref="BuildingProps.ButcherSpot"/>) is where the village cuts up its kills:
/// hunters carry carcasses there (<see cref="ButcherJob"/>'s carry), butcher faster and waste less,
/// and the meat lands by the larder rather than out in the field. Without one, carcasses are cut up
/// where they lie, as before.
/// </summary>
public static class ButcherSpots
{
    /// <summary>How far (cells) a carcass may lie from a spot's footprint and still count as at it.</summary>
    public const int Reach = 1;

    /// <summary>Carrying a carcass this heavy (kg) or more slows a person to <see cref="MinCarrySpeed"/>.</summary>
    public const float HeavyCarry = 120f;

    public const float MinCarrySpeed = 0.5f;

    /// <summary>The spot a carcass lies at (beside or on its footprint), or Entity.Null.</summary>
    public static Entity SpotAt(Simulation sim, int3 cell)
    {
        var w = sim.World;
        foreach (var e in w.Each<Thing>())
        {
            var t = w.Get<Thing>(e);
            if (!IsSpot(sim, e, t) || t.Cell.Z != cell.Z)
                continue;
            foreach (var c in Footprint.Cells(sim.Map, t.Def, t.Cell, t.Facing))
                if (Math.Abs(c.X - cell.X) <= Reach && Math.Abs(c.Y - cell.Y) <= Reach)
                    return e;
        }
        return Entity.Null;
    }

    /// <summary>The nearest finished spot <paramref name="pawn"/> can reach from <paramref name="from"/>, or Entity.Null.</summary>
    public static Entity Nearest(Simulation sim, Entity pawn, int3 from)
    {
        var w = sim.World;
        var best = Entity.Null;
        var bestDist = int.MaxValue;
        foreach (var e in w.Each<Thing>())
        {
            var t = w.Get<Thing>(e);
            if (!IsSpot(sim, e, t))
                continue;
            var d = Math.Abs(t.Cell.X - from.X) + Math.Abs(t.Cell.Y - from.Y) + Math.Abs(t.Cell.Z - from.Z);
            if (d < bestDist && Navigation.CanReach(sim, pawn, from, t.Cell, PathEnd.Touch))
            {
                best = e;
                bestDist = d;
            }
        }
        return best;
    }

    private static bool IsSpot(Simulation sim, Entity e, in Thing t) =>
        t.Def.Building?.ButcherSpot != null && t.Spawned && !sim.World.Has<Construction>(e);

    /// <summary>Whether <paramref name="carrier"/> can carry <paramref name="carcass"/> (people and big dragons can; a hatchling can't).</summary>
    public static bool CanCarry(Simulation sim, Entity carrier, Entity carcass)
    {
        var w = sim.World;
        var by = w.Get<Creature>(carrier);
        if (by.Def.Race is not (Race.Human or Race.Dragon))
            return false;
        return by.Stage(sim.Clock.Tick).Size >= w.Get<Creature>(carcass).Stage(sim.Clock.Tick).Size * 0.5f;
    }

    /// <summary>
    /// Walking speed factor for a creature carrying a carcass of <paramref name="mass"/> kg: a hare
    /// hardly counts, a deer slows a person to a trudge; something twice the carcass's size isn't slowed.
    /// </summary>
    public static float CarrySpeed(Simulation sim, Entity carrier, Entity carcass)
    {
        var w = sim.World;
        if (!w.TryGet<Creature>(carcass, out var c) || c.Def.Animal is not { } animal)
            return 1f;
        var by = w.Get<Creature>(carrier);
        if (by.Stage(sim.Clock.Tick).Size >= c.Stage(sim.Clock.Tick).Size * 2f)
            return 1f;
        return Math.Clamp(1f - (1f - MinCarrySpeed) * animal.Mass / HeavyCarry, MinCarrySpeed, 1f);
    }
}

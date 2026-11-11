using SupportingCast.Sim.Components;

namespace SupportingCast.Sim.Environment;

/// <summary>
/// The temperature a creature feels: the air, plus warmth radiating from a lit fire close by
/// (<see cref="Defs.BuildingProps.RadiantWarmth"/>). The heat model alone leaves an outdoor
/// campfire barely warmer a few tiles off, since the open air carries the heat away. Sitting by
/// one is still how you get through a cold night, and how a dragon sleeps out in a nest beside it.
/// Only creatures feel this; eggs, food and crafting go by the air (<see cref="Simulation.TemperatureAt"/>).
/// </summary>
public static class Warmth
{
    /// <summary>How far a fire's warmth reaches, in tiles (and one level up or down).</summary>
    public const int Range = 3;

    /// <summary>A lit fire giving off radiant warmth, and the room it's in (null: open sky).</summary>
    public readonly record struct Source(int3 Cell, float Warmth, Room? Room);

    /// <summary>Every lit fire that radiates warmth, for checking many cells at once.</summary>
    public static List<Source> Sources(Simulation sim)
    {
        var w = sim.World;
        var list = new List<Source>();
        foreach (var e in w.Each<Fueled>())
        {
            var thing = w.Get<Thing>(e);
            if (thing.Spawned && thing.Def.Building is { RadiantWarmth: > 0 } b && sim.Burning(e))
                list.Add(new Source(thing.Cell, b.RadiantWarmth, null));
        }
        // Rooms only after the loop: looking one up may rebuild them.
        for (var k = 0; k < list.Count; k++)
            list[k] = list[k] with { Room = sim.Rooms.RoomAt(list[k].Cell) };
        return list;
    }

    /// <summary>
    /// Radiant warmth at a cell: the strongest fire in reach, full next to it and falling off by a
    /// quarter per tile beyond. Only fires in the same room reach, or any fire when both are outdoors.
    /// </summary>
    public static float Radiant(Simulation sim, int3 cell, List<Source> sources)
    {
        if (sources.Count == 0)
            return 0f;
        var best = 0f;
        Room? room = null;
        var roomKnown = false;
        foreach (var s in sources)
        {
            var dz = Math.Abs(s.Cell.Z - cell.Z);
            var d = Math.Max(Math.Abs(s.Cell.X - cell.X), Math.Abs(s.Cell.Y - cell.Y));
            if (dz > 1 || d > Range)
                continue;
            var warmth = s.Warmth * (1f - Math.Max(0, d - 1) / (Range + 1f));
            if (warmth <= best)
                continue;
            if (!roomKnown)
            {
                room = sim.Rooms.RoomAt(cell);
                roomKnown = true;
            }
            if (room == s.Room || (room is not { Indoors: true } && s.Room is not { Indoors: true }))
                best = warmth;
        }
        return best;
    }

    /// <summary>What a creature standing in <paramref name="cell"/> feels.</summary>
    public static float FeltAt(Simulation sim, int3 cell, List<Source> sources) => sim.TemperatureAt(cell) + Radiant(sim, cell, sources);

    /// <summary>What a creature standing in <paramref name="cell"/> feels.</summary>
    public static float FeltAt(Simulation sim, int3 cell) => FeltAt(sim, cell, Sources(sim));

    /// <summary>What a creature feels where it is.</summary>
    public static float Felt(Simulation sim, Entity creature) => FeltAt(sim, sim.World.Get<Position>(creature).Cell);

    /// <summary>A fire's own tile: creatures warm themselves beside one, not standing in it.</summary>
    public static bool IsHearth(List<Source> sources, int3 cell)
    {
        foreach (var s in sources)
            if (s.Cell == cell)
                return true;
        return false;
    }
}

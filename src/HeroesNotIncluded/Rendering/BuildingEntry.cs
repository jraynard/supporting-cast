using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Ecs;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Rendering;

/// <summary>
/// Finding your way into a building with its roof on. A click on its roof means "go in": a
/// playtester clicked the middle of the inn and nothing happened, as the roof isn't somewhere to
/// walk. And standing at its door keeps it open to view, so a door on its far side (the shrine's,
/// under its eave) stays in sight as you come and go. Presentation only: the sim never reads it.
/// </summary>
public static class BuildingEntry
{
    /// <summary>How far from under the click to look for floor inside (cells): the widest building's half and some.</summary>
    private const int Reach = 10;

    /// <summary>
    /// Where a click on <paramref name="cell"/> means to walk, when it's on the roof of a building
    /// (other than <paramref name="open"/>, the one cut away to view): the open floor inside on its
    /// ground level nearest under the click. Null when the click means the cell itself.
    /// </summary>
    public static int3? Inside(Simulation sim, int3 cell, Entity open)
    {
        var map = sim.Map;
        if (!map.InBounds(cell))
            return null;
        var index = BuildingPlanIndex.For(sim);
        var building = index.At(cell);
        if (building.IsNull || building == open || !sim.World.TryGet<PresetBuilding>(building, out var plan))
            return null;
        var floor = map.Floor[map.Idx(cell)];
        if (floor.IsNull || !sim.World.Get<Thing>(floor).Def.IsRoof)
            return null; // not its roof: a floor you can see, or ground beside it
        var z = plan.Anchor.Z;
        int3? best = null;
        var bestDistance = int.MaxValue;
        for (var r = 0; r <= Reach && best == null; r++)
        for (var dy = -r; dy <= r; dy++)
        for (var dx = -r; dx <= r; dx++)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r)
                continue; // this ring only
            var c = new int3(cell.X + dx, cell.Y + dy, z);
            if (!map.InBounds(c) || index.At(c) != building || !index.IsPlanFloor(c))
                continue;
            var i = map.Idx(c);
            if (!map.Standable(i) || !map.Structure[i].IsNull)
                continue;
            var distance = dx * dx + dy * dy;
            if (distance < bestDistance)
                (best, bestDistance) = (c, distance);
        }
        return best;
    }

    /// <summary>
    /// The building hiding <paramref name="cell"/> from the camera: one whose wall (or door) is
    /// beside it on the camera's side, <paramref name="heading"/> being the view's direction into
    /// the screen across the ground. Standing behind the shrine, its roof hid you and its door.
    /// Walls beside you on the far side don't count, so walking down the street in front of the
    /// houses leaves their roofs on. Entity.Null if none.
    /// </summary>
    public static Entity WallBetween(Simulation sim, int3 cell, float2 heading)
    {
        var map = sim.Map;
        for (var dy = -1; dy <= 1; dy++)
        for (var dx = -1; dx <= 1; dx++)
        {
            if (dx * heading.X + dy * heading.Y >= -0.1f)
                continue; // not toward the camera
            var c = new int3(cell.X + dx, cell.Y + dy, cell.Z);
            if (!map.InBounds(c))
                continue;
            var wall = map.Structure[map.Idx(c)];
            if (wall.IsNull || !sim.World.TryGet<Thing>(wall, out var t) || t.Def.Building is not ({ IsWall: true } or { IsDoor: true }))
                continue;
            var building = Buildings.Of(sim, wall);
            if (!building.IsNull)
                return building;
        }
        return Entity.Null;
    }

    /// <summary>
    /// The building whose door is in or beside <paramref name="cell"/> (on its level; straight in
    /// front first, then a corner off), or Entity.Null.
    /// </summary>
    public static Entity DoorBeside(Simulation sim, int3 cell)
    {
        var map = sim.Map;
        ReadOnlySpan<int2> around = [new(0, 0), new(1, 0), new(-1, 0), new(0, 1), new(0, -1), new(1, 1), new(-1, 1), new(1, -1), new(-1, -1)];
        foreach (var d in around)
        {
            var c = new int3(cell.X + d.X, cell.Y + d.Y, cell.Z);
            if (!map.InBounds(c))
                continue;
            var door = map.Structure[map.Idx(c)];
            if (door.IsNull || !sim.World.TryGet<Thing>(door, out var t) || t.Def.Building is not { IsDoor: true })
                continue;
            var building = Buildings.Of(sim, door);
            if (!building.IsNull)
                return building;
        }
        return Entity.Null;
    }
}

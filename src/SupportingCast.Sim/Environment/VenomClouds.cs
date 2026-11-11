using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Environment;

/// <summary>
/// Poison breath's lingering clouds (<see cref="VenomCloud"/>, an entity each): whoever stands in
/// one is poisoned (<see cref="Systems.Afflictions"/>), friend and foe alike (as fire burns anyone
/// in it), except poison dragons, and plants in it wither. A cloud keeps to the air it was breathed
/// into: walls and floors hold it, as they stop the breath itself. Withering is also how poison
/// clears land without fire (Blight work: <see cref="Wilt"/>).
/// </summary>
public static class VenomClouds
{
    /// <summary>Leave a cloud at <paramref name="cell"/>, or thicken one already within a cell of it.</summary>
    public static void Spawn(Simulation sim, int3 cell, CloudProps cloud, ElementDef element, Entity source)
    {
        var w = sim.World;
        var until = sim.Clock.Tick + SimClock.HoursToTicks(cloud.Hours);
        foreach (var e in w.Query<VenomCloud>())
        {
            ref var v = ref w.Get<VenomCloud>(e);
            if (Math.Abs(v.Cell.Z - cell.Z) <= 0 && Math.Max(Math.Abs(v.Cell.X - cell.X), Math.Abs(v.Cell.Y - cell.Y)) <= 1)
            {
                v.Until = Math.Max(v.Until, until);
                v.Radius = Math.Max(v.Radius, cloud.Radius);
                return;
            }
        }
        var c = w.Create();
        w.Add(c, new VenomCloud { Cell = cell, Radius = cloud.Radius, Until = until, Source = source });
    }

    /// <summary>
    /// Poison eating at a plant (a breath, a cloud, Blight work): <paramref name="amount"/> off its
    /// health; at none it withers (<see cref="Wither"/>). True if it was a plant.
    /// </summary>
    public static bool Wilt(Simulation sim, Entity plant, float amount)
    {
        var w = sim.World;
        if (plant.IsNull || !w.IsAlive(plant) || !w.TryGet<Thing>(plant, out var t) || t.Def.Category != ThingCategory.Plant || !t.Spawned || amount <= 0)
            return false;
        ref var thing = ref w.Get<Thing>(plant);
        thing.Hp -= amount;
        if (thing.Hp <= 0)
            Wither(sim, plant);
        return true;
    }

    /// <summary>
    /// A plant dies of poison: a tree leaves its dead snag (as burning does: <see cref="ThingDef.BurnsInto"/>),
    /// anything smaller is gone. Land being cleared (<see cref="Maps.Designation.Burn"/>) is done
    /// with: the mark comes off, and a snag is marked for felling.
    /// </summary>
    public static void Wither(Simulation sim, Entity plant)
    {
        var map = sim.Map;
        var (def, cell) = (sim.World.Get<Thing>(plant).Def, sim.World.Get<Thing>(plant).Cell);
        var i = map.Idx(cell);
        sim.DestroyThing(plant);
        var clearing = (map.Designations[i] & Maps.Designation.Burn) != 0;
        if (clearing)
            map.ClearDesignation(i, Maps.Designation.Burn);
        if (def.BurnsIntoDef is { } remains && map.Structure[i].IsNull)
        {
            sim.SpawnStructure(remains, cell);
            if (clearing && remains.Harvest?.WorkType == WorkType.Chop)
                map.Designate(i, Maps.Designation.Chop);
        }
    }

    /// <summary>Whether a creature stands in a cloud's air: within its radius, on its level, in the same room (or both outdoors).</summary>
    public static bool Inside(Simulation sim, in VenomCloud v, int3 at)
    {
        if (at.Z != v.Cell.Z)
            return false;
        float dx = at.X - v.Cell.X, dy = at.Y - v.Cell.Y;
        return dx * dx + dy * dy <= v.Radius * v.Radius && sim.Rooms.RoomAt(at) == sim.Rooms.RoomAt(v.Cell);
    }

    /// <summary>The cloud <paramref name="at"/> is inside, if any (people walk out of them: <c>HumanBrain</c>).</summary>
    public static VenomCloud? CloudAt(Simulation sim, int3 at)
    {
        foreach (var e in sim.World.Each<VenomCloud>())
            if (Inside(sim, sim.World.Get<VenomCloud>(e), at))
                return sim.World.Get<VenomCloud>(e);
        return null;
    }
}

/// <summary>Clouds poison whoever's in them, wither what grows there, and thin away.</summary>
public sealed class VenomCloudSystem : ISimSystem
{
    public string Name => "VenomClouds";
    public int Interval => 30;

    private readonly List<Entity> _gone = [];

    public void Update(Simulation sim)
    {
        var w = sim.World;
        if (w.Count<VenomCloud>() == 0 || sim.Defs.AllElements.Count == 0)
            return;
        var poison = sim.Defs.Element(DragonElement.Poison);
        var now = sim.Clock.Tick;
        foreach (var e in w.Query<VenomCloud>())
        {
            var v = w.Get<VenomCloud>(e);
            if (v.Until <= now)
            {
                _gone.Add(e);
                continue;
            }
            foreach (var who in sim.Creatures())
            {
                if (w.Get<Creature>(who).Dead || !Inside(sim, v, w.Get<Position>(who).Cell) || Dragons.ImmuneTo(sim, who, poison))
                    continue;
                AfflictionRules.Apply(sim, who, poison.Status, v.Source);
            }
            // Plants in it wither by the hour.
            var r = (int)MathF.Ceiling(v.Radius);
            foreach (var c in sim.SpiralFrom(v.Cell, r))
            {
                if (!sim.Map.InBounds(c) || !Inside(sim, v, c))
                    continue;
                VenomClouds.Wilt(sim, sim.Map.Structure[sim.Map.Idx(c)], poison.Wilt * Interval / SimClock.TicksPerHour);
            }
        }
        foreach (var e in _gone)
            w.Destroy(e);
        _gone.Clear();
    }

    private static bool Inside(Simulation sim, in VenomCloud v, int3 at) => VenomClouds.Inside(sim, v, at);
}

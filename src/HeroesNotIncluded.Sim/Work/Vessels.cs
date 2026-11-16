using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Work;

/// <summary>
/// The inn's crockery. A drink is poured into a clean tankard and a bowl of stew ladled into a
/// clean bowl; the customer leaves it dirty where they finished (<see cref="ThingDef.Leaves"/>),
/// and it's washed at the tub (<see cref="WashSession"/>) before it's used again. Run out of clean
/// ones and the bar stops, however full the cask.
/// </summary>
public static class Vessels
{
    public static bool IsDirty(ThingDef def) => def.WashesInto != null;

    /// <summary>A clean vessel of a kind to hand: on someone (hand, then pack), else on a shelf they're standing at. Entity.Null if none.</summary>
    public static Entity CleanOn(Simulation sim, Entity who, string def) => ToHand(sim, who, d => d.Name == def);

    /// <summary>
    /// Something to hand: on someone (in hand first, then the pack), else in a container they're
    /// standing at, or anywhere behind a bar they're standing behind (<see cref="BehindTheBar"/>):
    /// what's on the shelf back there needn't be carried to be used.
    /// </summary>
    public static Entity ToHand(Simulation sim, Entity who, Func<ThingDef, bool> fits)
    {
        var near = NearToHand(sim, who, fits);
        if (!near.IsNull)
            return near;
        foreach (var box in BehindTheBar(sim, who))
            foreach (var item in sim.StoredIn(box))
                if (fits(sim.World.Get<Thing>(item).Def) && !sim.World.Has<Shops.ForSale>(item))
                    return item;
        return Entity.Null;
    }

    /// <summary>How far from the counter a container still counts as behind the bar.</summary>
    public const int BarDepth = 3;

    /// <summary>
    /// The containers behind a bar counter this person is standing beside, on the keeper's side of
    /// it: the building's own, on the same side of the counter's line as they are, within
    /// <see cref="BarDepth"/> of it, and in the same room. Anywhere along a long bar reaches its shelf; across the counter,
    /// on the customers' side, nothing does. Empty when they're at no bar.
    /// </summary>
    public static IEnumerable<Entity> BehindTheBar(Simulation sim, Entity who)
    {
        var w = sim.World;
        if (!w.TryGet<Position>(who, out var p))
            yield break;
        var at = p.Cell;
        var seen = new HashSet<Entity>();
        foreach (var d in Maps.Map.Neighbors8)
        {
            var c = new int3(at.X + d.X, at.Y + d.Y, at.Z);
            if (!sim.Map.InBounds(c) || sim.Map.Structure[sim.Map.Idx(c)] is not { IsNull: false } counter || !seen.Add(counter)
                || w.Get<Thing>(counter) is not { Def.Building.Bar: true } t || w.Has<Construction>(counter))
                continue;
            var building = Systems.Buildings.Of(sim, counter);
            if (building.IsNull)
                continue;
            var cells = Maps.Footprint.Cells(sim.Map, t.Def, t.Cell, t.Facing).ToList();
            // A counter's a line of cells: which way across it is this person?
            var alongX = cells.All(q => q.Y == t.Cell.Y);
            int Side(int3 q) => alongX ? Math.Sign(q.Y - t.Cell.Y) : Math.Sign(q.X - t.Cell.X);
            var mine = Side(at);
            if (mine == 0)
                continue; // at its end, not behind it
            var room = sim.Rooms.RoomAt(at);
            foreach (var part in Systems.Buildings.Parts(sim, building))
            {
                if (!sim.IsContainer(part) || w.Has<Construction>(part) || !w.TryGet<Thing>(part, out var shelf) || shelf.Cell.Z != at.Z)
                    continue;
                // Behind this bar, not through a wall in the room next door (the storeroom's boxes).
                if (Side(shelf.Cell) == mine && cells.Any(q => Maps.Map.Distance(q, shelf.Cell) <= BarDepth)
                    && sim.Rooms.RoomAt(shelf.Cell) == room)
                    yield return part;
            }
        }
    }

    private static Entity NearToHand(Simulation sim, Entity who, Func<ThingDef, bool> fits)
    {
        if (AvatarSystem.Supply(sim, who, fits) is { IsNull: false } carried)
            return carried;
        var w = sim.World;
        if (!w.TryGet<Position>(who, out var p))
            return Entity.Null;
        var at = p.Cell;
        foreach (var d in Maps.Map.Neighbors8)
        {
            var c = new int3(at.X + d.X, at.Y + d.Y, at.Z);
            if (!sim.Map.InBounds(c) || sim.Map.Structure[sim.Map.Idx(c)] is not { IsNull: false } box || !sim.IsContainer(box)
                || !Inventory.CanReachContainer(sim, who, box))
                continue;
            foreach (var item in sim.StoredIn(box))
                if (fits(w.Get<Thing>(item).Def) && !w.Has<Shops.ForSale>(item))
                    return item;
        }
        return Entity.Null;
    }

    /// <summary>One of something to hand (<see cref="ToHand"/>) used up: off the person, or off the shelf.</summary>
    public static bool UseOne(Simulation sim, Entity who, Func<ThingDef, bool> fits)
    {
        var item = ToHand(sim, who, fits);
        if (item.IsNull)
            return false;
        sim.World.Destroy(Inventory.OnPerson(sim, who, item) ? Inventory.TakeFromPerson(sim, who, item, 1) : sim.TakeFromStack(item, 1));
        return true;
    }

    /// <summary>
    /// Ready to pour or ladle: a clean vessel of the kind to hand (<see cref="ToHand"/>), and their
    /// hand either empty or holding that kind (so the drink or bowl can go in it).
    /// </summary>
    public static bool Ready(Simulation sim, Entity who, string def)
    {
        var held = sim.World.Get<Carrying>(who).Item;
        return !CleanOn(sim, who, def).IsNull && (held.IsNull || sim.World.Get<Thing>(held).Def.Name == def);
    }

    /// <summary>Use up one clean vessel of a kind from someone (hand first, then pack), and free their hand for what goes in it. False if they had none.</summary>
    public static bool Use(Simulation sim, Entity who, string def)
    {
        var w = sim.World;
        if (!UseOne(sim, who, d => d.Name == def))
            return false;
        ref var carrying = ref w.Get<Carrying>(who);
        // The rest of the stack goes to the pack, so the drink lands in hand.
        if (!carrying.Item.IsNull && w.Get<Thing>(carrying.Item).Def.Name == def && !sim.PackOf(who).IsNull)
        {
            var rest = carrying.Item;
            carrying.Item = Entity.Null;
            Inventory.Stow(sim, who, rest);
        }
        return true;
    }

    /// <summary>What's left once someone has drunk or eaten this: set down where they are.</summary>
    public static void LeaveAfter(Simulation sim, Entity who, ThingDef consumed)
    {
        if (consumed.Leaves is not { } leaves || !sim.World.TryGet<Position>(who, out var at))
            return;
        sim.SpawnItem(sim.Defs.Thing(leaves), 1, at.Cell);
    }
}

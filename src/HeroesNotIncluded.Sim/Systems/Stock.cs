using HeroesNotIncluded.Sim.Components;

namespace HeroesNotIncluded.Sim.Systems;

/// <summary>
/// A workplace's stock: its chest, its stations, what's in them. Every trade keeps its stock this way
/// (the woodshop's logs, the tinker's springs), so it lives here rather than with any one of them.
/// </summary>
public static class Stock
{
    /// <summary>What wood-burning fires burn (never timber): split by the carpenter, brought by the carter.</summary>
    public const string FirewoodDef = "Firewood";

    /// <summary>What crafted things are made of (tools' hafts, contraptions, furniture): sawn by the carpenter. Timber (<c>Wood</c>) is for building and mending.</summary>
    public const string PlanksDef = "Planks";

    /// <summary>Raw timber: walls, roofs, the bridge and a wagon's axle, and what planks are sawn from at a pinch.</summary>
    public const string TimberDef = "Wood";

    private static bool Is(Simulation sim, Entity e, string def) =>
        sim.World.IsAlive(e) && sim.World.TryGet<Thing>(e, out var t) && t.Def.Name == def;

    /// <summary>The chest at a building, Entity.Null if none.</summary>
    public static Entity ChestOf(Simulation sim, Entity building) => StationOf(sim, building, "Chest");

    /// <summary>The first part of <paramref name="def"/> in a building (a station, a chest), Entity.Null if none.</summary>
    public static Entity StationOf(Simulation sim, Entity building, string def)
    {
        if (building.IsNull || !sim.World.IsAlive(building))
            return Entity.Null;
        return Buildings.Parts(sim, building).Where(e => Is(sim, e, def)).OrderBy(e => e.Index).FirstOrDefault(Entity.Null);
    }

    /// <summary>How many of <paramref name="def"/> are in a chest.</summary>
    public static int CountIn(Simulation sim, Entity chest, string def) =>
        chest.IsNull ? 0 : sim.StoredIn(chest).Where(e => sim.World.Get<Thing>(e).Def.Name == def).Sum(e => sim.World.Get<Stack>(e).Count);

    /// <summary>Use up <paramref name="count"/> of <paramref name="def"/> from a chest. False (nothing used) if it's short.</summary>
    public static bool TakeFrom(Simulation sim, Entity chest, string def, int count)
    {
        if (CountIn(sim, chest, def) < count)
            return false;
        foreach (var e in sim.StoredIn(chest).Where(e => sim.World.Get<Thing>(e).Def.Name == def).OrderBy(e => e.Index).ToList())
        {
            if (count <= 0)
                break;
            var n = Math.Min(count, sim.World.Get<Stack>(e).Count);
            sim.DestroyThing(sim.TakeFromStack(e, n));
            count -= n;
        }
        return true;
    }
}

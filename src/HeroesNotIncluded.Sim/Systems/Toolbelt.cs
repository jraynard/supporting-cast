using HeroesNotIncluded.Sim.Components;

namespace HeroesNotIncluded.Sim.Systems;

/// <summary>
/// The tools someone carries on their belt: up to <see cref="Slots"/> items with a
/// <see cref="Defs.ThingDef.Tool"/>, held off the map with a <see cref="Stored"/> marker whose
/// container is the creature itself (so <see cref="Simulation.LocationOf"/> follows them, and
/// saving needs nothing new). Hands stay free for hauling, and jobs never take them
/// (<see cref="Simulation.IsAvailable"/>). <see cref="Crafting.ToolSpeed"/> counts them.
/// </summary>
public static class Toolbelt
{
    public const int Slots = 4;

    /// <summary>What's on the creature's belt, in entity order.</summary>
    public static List<Entity> Of(Simulation sim, Entity creature) => sim.StoredIn(creature);

    public static bool HasRoom(Simulation sim, Entity creature) => Of(sim, creature).Count < Slots;

    /// <summary>The best tool of this kind on the belt (fastest), or Entity.Null.</summary>
    public static Entity Best(Simulation sim, Entity creature, string kind)
    {
        var best = Entity.Null;
        var speed = 0f;
        foreach (var e in Of(sim, creature))
            if (sim.World.Get<Thing>(e).Def.Tool is { } t && t.Kind == kind && t.Speed > speed)
            {
                best = e;
                speed = t.Speed;
            }
        return best;
    }

    /// <summary>
    /// Hang one unit of <paramref name="item"/> (a tool, wherever it is) on the belt. Returns what
    /// went on, or Entity.Null if it isn't a tool or the belt is full.
    /// </summary>
    public static Entity PutOn(Simulation sim, Entity creature, Entity item)
    {
        var w = sim.World;
        if (!w.IsAlive(item) || w.Get<Thing>(item).Def.Tool == null || !HasRoom(sim, creature))
            return Entity.Null;
        if (w.TryGet<Carrying>(creature, out var hands) && hands.Item == item && w.Get<Stack>(item).Count <= 1)
            w.Get<Carrying>(creature).Item = Entity.Null;
        var one = sim.TakeFromStack(item, 1); // off the map or out of its container
        w.Get<Thing>(one).Cell = w.Get<Position>(creature).Cell;
        w.Add(one, new Stored { In = creature });
        return one;
    }

    /// <summary>Take a tool off the belt, into empty hands or else at the creature's feet.</summary>
    public static void TakeOff(Simulation sim, Entity creature, Entity item)
    {
        var w = sim.World;
        if (!w.IsAlive(item) || !w.TryGet<Stored>(item, out var s) || s.In != creature)
            return;
        w.Remove<Stored>(item);
        ref var hands = ref w.Get<Carrying>(creature);
        if (hands.Item.IsNull)
            hands.Item = item;
        else
            sim.PlaceItemNear(item, w.Get<Position>(creature).Cell);
    }

    /// <summary>Everything on the belt drops at the creature's feet (on death).</summary>
    public static void DropAll(Simulation sim, Entity creature)
    {
        var w = sim.World;
        foreach (var e in Of(sim, creature))
        {
            w.Remove<Stored>(e);
            sim.PlaceItemNear(e, w.Get<Position>(creature).Cell);
        }
    }

    /// <summary>The kit everyone starts with: a stone axe and a stone pick (see <c>Simulation.GiveStartingPack</c>).</summary>
    public static readonly string[] StartingTools = ["StoneAxe", "StonePick"];

    /// <summary>Put the <see cref="StartingTools"/> on an empty belt.</summary>
    public static void GiveStartingTools(Simulation sim, Entity creature)
    {
        if (Of(sim, creature).Count > 0)
            return;
        foreach (var name in StartingTools)
            if (sim.Defs.TryThing(name, out var def))
                PutOn(sim, creature, sim.CreateItem(def, 1));
    }
}

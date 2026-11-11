using DragonWorld.Sim.Components;

namespace DragonWorld.Sim.Systems;

/// <summary>
/// Treasure (<see cref="Defs.ThingDef.HoardValue"/>: coins, silver and gold, jewelry): until there's
/// trade, what it's good for (docs/plans/mining-progression.md). Dragons love to sleep on it, a
/// keeper's gift of it wins one over, and it draws raiders (<see cref="Storyteller.RaidPoints"/>).
/// </summary>
public static class Hoard
{
    /// <summary>Treasure within this many cells of a sleeping dragon counts as its hoard.</summary>
    public const int Reach = 2;

    /// <summary>Hoard worth at least this much makes a dragon content (a silver ring and a few coins).</summary>
    public const float Enough = 50f;

    /// <summary>Wealth per raid point the storyteller adds.</summary>
    public const float WealthPerPoint = 25f;

    /// <summary>Affinity a gift gains per unit of value (at most the Gift interaction's affinity, data/defs/dragons.json).</summary>
    public const float AffinityPerValue = 0.0002f;

    public static float ValueOf(Simulation sim, Entity item) =>
        sim.World.Get<Thing>(item).Def.HoardValue * (sim.World.TryGet<Stack>(item, out var s) ? s.Count : 1);

    /// <summary>Treasure lying within <see cref="Reach"/> of a cell, on its level.</summary>
    public static float ValueAround(Simulation sim, int3 cell)
    {
        var map = sim.Map;
        var total = 0f;
        for (var dy = -Reach; dy <= Reach; dy++)
        for (var dx = -Reach; dx <= Reach; dx++)
        {
            var c = new int3(cell.X + dx, cell.Y + dy, cell.Z);
            if (map.InBounds(c) && map.Item[map.Idx(c)] is { IsNull: false } item && sim.World.Get<Thing>(item).Def.HoardValue > 0)
                total += ValueOf(sim, item);
        }
        return total;
    }

    /// <summary>
    /// A keeper hands their dragon treasure: it's set down beside the dragon (its hoard grows where
    /// it rests), and the dragon thinks better of them for it.
    /// </summary>
    public static void Give(Simulation sim, Entity giver, Entity dragon, Entity item)
    {
        var w = sim.World;
        var value = ValueOf(sim, item);
        sim.PlaceItemNear(item, w.Get<Position>(dragon).Cell);
        // Care like any other (Bonding): the gift def's affinity is the most a gift is worth.
        if (w.Has<Dragon>(dragon) && sim.Defs.AllDragonInteractions.Find(x => x.Kind == Defs.CareKind.Gift) is { Affinity: > 0 } gift)
            Bonding.Interact(sim, giver, dragon, gift, amount: Math.Min(1f, value * AffinityPerValue / gift.Affinity));
        else
            MoodSystem.Remember(sim, dragon, sim.Defs.Thoughts.GivenTreasure, giver);
        sim.Messages.Add(MessageKind.Good, $"{w.Get<Creature>(dragon).Name} takes the {w.Get<Thing>(item).Label} to its hoard.", w.Get<Position>(dragon).Cell);
    }

    /// <summary>All the treasure the village has: lying about, in storage, carried, worn.</summary>
    public static float VillageWealth(Simulation sim)
    {
        var w = sim.World;
        var total = 0f;
        foreach (var e in w.Each<Stack>())
            if (w.Get<Thing>(e).Def.HoardValue > 0)
                total += ValueOf(sim, e);
        return total;
    }
}

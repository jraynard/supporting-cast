using HeroesNotIncluded.Sim.Carpentry;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Scenarios;

/// <summary>
/// The woods on demand (<c>--fell-test</c>): the player (the carpenter, unless told otherwise) stood
/// by the pine nearest the woodshop with an axe and a saw to hand, so felling it (<see cref="FellSession"/>),
/// bucking it (<see cref="BuckSession"/>) and carrying a log home are a few keys away. An oak's there
/// too if there's one near, for a great log.
/// </summary>
public static class FellDrill
{
    /// <summary>Stand the player by a tree and give them the tools. Call once the round's dealt. The tree, or Entity.Null.</summary>
    public static Entity Stage(Simulation sim, Entity player)
    {
        var w = sim.World;
        var from = Shops.ShopSystem.BuildingFor(sim, Defs.TownRole.Woodshop) is { IsNull: false } shop && w.TryGet<PresetBuilding>(shop, out var b)
            ? b.Anchor : w.Get<Position>(player).Cell;
        // A pine in the open: nothing built within a few cells, so it has room to come down.
        bool Open(int3 at) => sim.SpiralFrom(at, 4).All(c => !sim.Map.InBounds(c) || sim.Map.Structure[sim.Map.Idx(c)] is var s
            && (s.IsNull || w.Get<Thing>(s).Def.Category == Defs.ThingCategory.Plant));
        var tree = sim.SpiralFrom(from, 80)
            .Where(c => sim.Map.InBounds(c))
            .Select(c => sim.Map.Structure[sim.Map.Idx(c)])
            .FirstOrDefault(e => !e.IsNull && w.Get<Thing>(e).Def.Name == "Pine" && Open(w.Get<Thing>(e).Cell), Entity.Null);
        if (tree.IsNull)
            return Entity.Null;
        var at = w.Get<Thing>(tree).Cell;
        var stand = sim.SpiralFrom(at, 2).FirstOrDefault(c => c != at && sim.Map.Passable(c) && AvatarSystem.InReach(c, at), new int3(-1, -1, -1));
        if (stand.X >= 0)
            FireDrill.MoveTo(sim, player, stand);
        foreach (var tool in new[] { "Axe", "Saw" })
            if (Crafting.ToolSpeed(sim, player, tool) <= 0f)
                Inventory.Stow(sim, player, sim.CreateItem(sim.Defs.Thing(tool), 1));
        return tree;
    }
}

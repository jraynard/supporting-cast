using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Tinkering;

namespace HeroesNotIncluded.Sim.Scenarios;

/// <summary>
/// Every job for two pairs of hands, ready at once (<c>--pairs-test</c>; docs/DESIGN.md, "Two pairs
/// of hands"), for trying them in co-op: a greatsword's iron by the forge (the striker), a great log's
/// wood by the sawhorse (the pitman), a party's wagon with a cracked axle by the woodshop and a log to
/// hew it from (the lifter), and a villager lying dead on a shrine pallet, with the empath's mana full
/// (the keeper). The work's begun the usual way, by whoever holds the trade; anyone else lends a hand
/// with F where it's going on, and alone Help! from the quick-say wheel brings an AI villager.
/// Staged through <see cref="DevPairsDrillCommand"/> once the round's dealt, so every peer stages the same.
/// </summary>
public static class PairsDrill
{
    /// <summary>Stage it all. Call once the round's dealt; what couldn't be staged (no such building on this map) is left out.</summary>
    public static List<string> Stage(Simulation sim)
    {
        var staged = new List<string>();
        var w = sim.World;

        // The striker: a greatsword's iron and leather by the forge, the recipe known to all.
        RecipeBooks.Discover(sim, Entity.Null, "handle:IronBar");
        if (Station(sim, TownRole.Forge, "Forge") is { IsNull: false } forge)
        {
            Lay(sim, forge, "IronBar", 5);
            Lay(sim, forge, "Leather", 1);
            staged.Add("a greatsword's iron by the forge (the smith makes it; someone takes the sledge)");
        }

        // Each trade's own work for two (the carpenter's great log and wagon axle).
        foreach (var module in Modding.Trades.All(sim))
            staged.AddRange(module.PairsDrill(sim));

        // The keeper: a villager dead on a shrine pallet, and every empath's mana full.
        if (Station(sim, TownRole.Shrine, "StrawPallet") is { IsNull: false } pallet && Victim(sim) is { IsNull: false } victim)
        {
            FireDrill.MoveTo(sim, victim, w.Get<Thing>(pallet).Cell);
            NeedsSystem.Kill(sim, victim, ref w.Get<Creature>(victim), "was found dead");
            foreach (var e in w.Query<Mana>())
                w.Get<Mana>(e).Current = w.Get<Mana>(e).Max;
            staged.Add($"{w.Get<Creature>(victim).Name} dead on a shrine pallet (the empath calls them back; someone holds them)");
        }
        sim.Messages.Add(MessageKind.Info, staged.Count == 0
            ? "The pairs drill found nothing to stage on this map."
            : "Work for two pairs of hands, ready: " + string.Join("; ", staged) + ". Lend a hand with F, or call Help!.");
        return staged;
    }

    /// <summary>The first piece of furniture of this def in the building of this role, Entity.Null if none.</summary>
    private static Entity Station(Simulation sim, TownRole role, string def)
    {
        var building = Shops.ShopSystem.BuildingFor(sim, role);
        return building.IsNull ? Entity.Null : Systems.Stock.StationOf(sim, building, def);
    }

    /// <summary>Some of a thing set down beside a station.</summary>
    public static void Lay(Simulation sim, Entity station, string def, int count) =>
        sim.PlaceItemNear(sim.CreateItem(sim.Defs.Thing(def), count), sim.World.Get<Thing>(station).Cell);

    /// <summary>A party's wagon, its axle cracked, on open ground near the building; Entity.Null if there's nowhere.</summary>
    public static Entity Wagon(Simulation sim, Entity near)
    {
        var def = sim.Defs.Thing(Wagons.Def);
        var at = sim.World.TryGet<PresetBuilding>(near, out var building) ? building.Anchor : sim.LocationOf(near);
        foreach (var c in sim.SpiralFrom(at, 14))
        {
            if (Maps.Map.Distance(c, at) < 4 || !sim.CanPlaceStructure(c, def, 0)
                || !sim.FootprintCells(def, c, 0).All(f => sim.Map.Passable(f) && sim.Map.Item[sim.Map.Idx(f)].IsNull && Buildings.At(sim, f).IsNull))
                continue;
            var wagon = sim.SpawnStructure(def, c);
            sim.World.Add(wagon, new Wagon { Party = -1, Owner = Entity.Null, Damage = WagonDamage.Axle, WheelSeed = 77, MendedBy = Entity.Null });
            return wagon;
        }
        return Entity.Null;
    }

    /// <summary>Who's found dead: the first AI villager of the town who isn't an empath, Entity.Null if none.</summary>
    private static Entity Victim(Simulation sim)
    {
        var w = sim.World;
        return sim.Creatures().Where(e => w.Get<Creature>(e) is { Faction: Faction.Village, Def.Race: Race.Human, Dead: false }
                && !w.Has<PlayerCharacter>(e) && !w.Has<Avatar>(e) && !w.Has<Mana>(e) && !w.Has<Adventurer>(e))
            .OrderBy(e => e.Index).FirstOrDefault(Entity.Null);
    }
}

/// <summary>Debug (<c>--pairs-test</c>): stage every job for two pairs of hands (<see cref="PairsDrill"/>).</summary>
public sealed record DevPairsDrillCommand : IDevCommand
{
    public void Apply(Simulation sim)
    {
        if (sim.Round.Live)
            PairsDrill.Stage(sim);
    }
}

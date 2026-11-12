using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Maps;

namespace HeroesNotIncluded.Sim.Scenarios;

/// <summary>
/// Standard village setup plus the orders a player would give in the vertical slice: a wooden hall
/// west of the landing site (campfire, stairs and nest below; a second storey with beds once the
/// fire is lit), a tunnel into the nearest hill for stone, and trees marked for chopping. Shared
/// by the scenario test and the game's --demo flag.
/// </summary>
public static class VillageSetup
{
    /// <summary>Starter stockpile and designated keeper, as every new village gets.</summary>
    public static void ApplyVillageDefaults(Simulation sim, int3 center)
    {
        var map = sim.Map;
        for (var y = 3; y < 8; y++)
        for (var x = 3; x < 9; x++)
            map.SetZone(map.Idx(center.X + x, center.Y + y, center.Z), 1);
    }

    /// <summary>The dwelling's spare beds, once it's up: what a player adds for more settlers.</summary>
    public static void FillSpareBeds(Simulation sim, Entity dwelling)
    {
        if (!sim.World.IsAlive(dwelling) || !sim.World.TryGet<PresetBuilding>(dwelling, out var b))
            return;
        foreach (var spot in Systems.Buildings.Slots(sim, b))
            if (spot.Slot.Accepts(sim.Defs.Thing("Bed")))
                Systems.Buildings.FillSlot(sim, dwelling, spot.Index, sim.Defs.Thing("Bed"));
    }

    /// <summary>A preset building at the nearest spot to <paramref name="preferred"/> its plot fits, on the ground there; Entity.Null if none within reach.</summary>
    public static Entity PlaceBuildingNear(Simulation sim, BuildingDef def, int3 preferred, int facing)
    {
        var map = sim.Map;
        for (var r = 0; r <= 14; r++)
        for (var dy = -r; dy <= r; dy++)
        for (var dx = -r; dx <= r; dx++)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r || !map.InBounds(preferred.X + dx, preferred.Y + dy, 0))
                continue;
            var ground = map.VisibleCell(preferred.X + dx, preferred.Y + dy, map.Depth - 1);
            if (map.IsSolid(map.Idx(ground)))
                ground = new int3(ground.X, ground.Y, ground.Z + 1);
            if (ground.Z != preferred.Z || Systems.Buildings.PlacementProblem(sim, def, ground, facing) != null)
                continue; // the landing's level: a house up a hill is a long way round
            return Systems.Buildings.Place(sim, def, ground, facing);
        }
        return Entity.Null;
    }

    /// <summary>The few shore cells nearest the landing site that villagers can walk to, marked for fishing.</summary>
    public static int MarkFishingSpots(Simulation sim, int3 center, int count = 6, int radius = 50)
    {
        var spots = new List<int3>();
        foreach (var c in sim.SpiralFrom(center, radius))
        {
            if (spots.Count == count)
                break;
            if (Systems.Fishing.CanFishFrom(sim, c) && sim.Regions.CanReach(center, c, PathEnd.OnTile))
                spots.Add(c);
        }
        return Systems.Fishing.Designate(sim, spots);
    }

    /// <summary>What a player keeping the larder full clicks: hunt every animal near the village that won't fight back.</summary>
    public static int HuntNearbyGame(Simulation sim, int3 center, int radius = 45)
    {
        var w = sim.World;
        var marked = 0;
        foreach (var e in w.Query<Herd>())
        {
            var c = w.Get<Creature>(e);
            if (!c.Able || c.Def.Animal is not { Aggressive: false } || w.Has<HuntOrder>(e) || w.Has<Combat.Leaving>(e))
                continue;
            if (Map.Distance(w.Get<Position>(e).Cell, center) > radius)
                continue;
            w.Add(e, new HuntOrder());
            marked++;
        }
        return marked;
    }

}

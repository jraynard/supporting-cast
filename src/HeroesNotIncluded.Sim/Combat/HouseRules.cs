using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Combat;

/// <summary>
/// A house's rules on fighting (docs/DESIGN.md, "Bar fights"): in a building whose def says
/// <see cref="BuildingDef.NoSteel"/> (the inn), nobody of the town or its guests swings a weapon,
/// only their fists, so a fight there is a brawl and not a killing. Raiders and beasts keep no house
/// rules, and while a raid is on the rule's lifted for everyone: the town may defend the inn with steel.
/// Decided in the sim from where someone stands, so every peer agrees.
/// </summary>
public static class HouseRules
{
    /// <summary>Whether <paramref name="e"/> must leave their weapon be where they stand.</summary>
    public static bool NoSteel(Simulation sim, Entity e)
    {
        var w = sim.World;
        if (!w.TryGet<Creature>(e, out var c) || Factions.Attacker(c.Faction) || RaidOn(sim)
            || !w.TryGet<Position>(e, out var p))
            return false;
        return NoSteelAt(sim, p.Cell);
    }

    /// <summary>Whether this cell is inside a building with no steel allowed (raids aside).</summary>
    public static bool NoSteelAt(Simulation sim, int3 cell) => !RuledBuilding(sim, cell).IsNull;

    /// <summary>The no-steel building this cell is in, or Entity.Null.</summary>
    public static Entity RuledBuilding(Simulation sim, int3 cell)
    {
        var w = sim.World;
        foreach (var b in w.Each<PresetBuilding>())
        {
            var pb = w.Get<PresetBuilding>(b);
            if (!pb.Def.NoSteel || pb.Demolishing)
                continue;
            if (Buildings.SymbolAt(pb.Def, pb.Anchor, pb.Facing, cell) != ' '
                || (cell.Z > pb.Anchor.Z && cell.Z < pb.Anchor.Z + pb.Def.Storeys
                    && Buildings.SymbolAt(pb.Def, pb.Anchor, pb.Facing, new int3(cell.X, cell.Y, pb.Anchor.Z)) != ' '))
                return b;
        }
        return Entity.Null;
    }

    /// <summary>A raid's on: the town's mustered, or the Director has raiders in the valley.</summary>
    public static bool RaidOn(Simulation sim) =>
        sim.Militia.Mustering || sim.Director.Active.Any(k => k.Kind == CrisisKind.Raid);
}

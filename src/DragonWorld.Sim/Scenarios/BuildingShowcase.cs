using DragonWorld.Sim.Components;
using DragonWorld.Sim.Defs;
using DragonWorld.Sim.Systems;

namespace DragonWorld.Sim.Scenarios;

/// <summary>
/// Debug/screenshots (<c>--building-demo</c>): the preset buildings beside the landing. A row to
/// the north has every building in every look, finished, a couple with extras added (a second
/// bed, bellows by the forge); a row to the south has a dwelling stopped at each phase, its next
/// phase laid out as blueprints, so the villagers carry on raising them. Plots that won't fit the
/// ground nearby are skipped.
/// </summary>
public static class BuildingShowcase
{
    public static int3 Setup(Simulation sim, int3 center)
    {
        var defs = sim.Defs;
        var x = center.X - 30;
        foreach (var def in defs.AllBuildings)
            for (var v = 0; v < def.Variants.Count; v++)
            {
                var b = PlaceNear(sim, def, new int2(x + def.Width / 2, center.Y - 12 - def.Depth / 2), v, BuildPhase.Done);
                if (!b.IsNull)
                    AddExtras(sim, b);
                x += def.Width + 3;
            }

        var dwelling = defs.Building("Dwelling");
        x = center.X - 16;
        foreach (var phase in Enum.GetValues<BuildPhase>())
        {
            PlaceNear(sim, dwelling, new int2(x, center.Y + 10), 0, phase);
            x += dwelling.Width + 3;
        }
        sim.RebuildDerivedIfNeeded();
        return new int3(center.X - 6, center.Y - 12, center.Z); // the finished row
    }

    /// <summary>A building at the first spot near <paramref name="at"/> its plot fits, on the ground there.</summary>
    private static Entity PlaceNear(Simulation sim, BuildingDef def, int2 at, int variant, BuildPhase upTo)
    {
        var map = sim.Map;
        for (var r = 0; r <= 4; r++)
        for (var dy = -r; dy <= r; dy++)
        for (var dx = -r; dx <= r; dx++)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r || !map.InBounds(at.X + dx, at.Y + dy, 0))
                continue;
            var ground = map.VisibleCell(at.X + dx, at.Y + dy, map.Depth - 1);
            if (map.IsSolid(map.Idx(ground)))
                ground = new int3(ground.X, ground.Y, ground.Z + 1);
            if (!map.InBounds(ground) || Buildings.PlacementProblem(sim, def, ground, 0, variant) != null)
                continue;
            return Buildings.SpawnFinished(sim, def, ground, 0, variant, upTo);
        }
        return Entity.Null;
    }

    /// <summary>What a player might add once it's up: its first upgrade fitted and first slot filled (a pot on the hearth, another bed, bellows by the smithy's forge).</summary>
    private static void AddExtras(Simulation sim, Entity building)
    {
        var b = sim.World.Get<PresetBuilding>(building);
        if (b.Def.Upgrades.Count > 0)
            b.Upgrades.Add(b.Def.Upgrades[0].Name); // the hearth's cooking pot
        if (Buildings.Slots(sim, b).FirstOrDefault() is not { Slot: not null } spot)
            return;
        var def = spot.Slot.ThingDefs[0];
        if (Buildings.SlotProblem(sim, building, spot.Index, def) == null)
            sim.World.Add(sim.SpawnStructure(def, spot.Cell, spot.Facing), new PartOf { Building = building });
    }
}

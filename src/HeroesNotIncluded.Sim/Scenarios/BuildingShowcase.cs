using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Scenarios;

/// <summary>
/// Debug/screenshots (<c>--building-demo</c>): the preset buildings beside the landing. A row to
/// the north has every building in every look, finished, a couple with extras added (a second
/// bed, bellows by the forge); a row to the south has the inn stopped at each phase, its next
/// phase laid out as blueprints, so the villagers carry on raising them. Plots that won't fit the
/// ground nearby are skipped.
/// </summary>
public static class BuildingShowcase
{
    /// <summary>How wide a row of plots runs before the next starts beside it, kept to the flat ground round the landing.</summary>
    private const int RowWidth = 60;

    public static int3 Setup(Simulation sim, int3 center)
    {
        var defs = sim.Defs;
        var row = new Row(center.X - RowWidth / 2, center.Y - 12, -1);
        foreach (var def in defs.AllBuildings)
            for (var v = 0; v < def.Variants.Count; v++)
            {
                var b = PlaceNear(sim, def, row.Next(def), v, BuildPhase.Done);
                if (!b.IsNull)
                    AddExtras(sim, b);
            }

        // The inn, two storeys, shows every phase there is.
        var inn = defs.Building(defs.AllBuildings.Exists(d => d.Name == "Inn") ? "Inn" : "Dwelling");
        row = new Row(center.X - RowWidth / 2, center.Y + 10, 1);
        foreach (var phase in Enum.GetValues<BuildPhase>())
            PlaceNear(sim, inn, row.Next(inn), 0, phase);
        sim.RebuildDerivedIfNeeded();
        return new int3(center.X - 6, center.Y - 12, center.Z); // the finished row
    }

    /// <summary>Plots side by side from <c>x</c>, wrapping onto a new row further out (<c>dir</c>: -1 north, 1 south) past <see cref="RowWidth"/>.</summary>
    private sealed class Row(int left, int top, int dir)
    {
        private int _x, _y = top, _deepest;
        private bool _started;

        /// <summary>Where the next plot's middle goes.</summary>
        public int2 Next(BuildingDef def)
        {
            if (!_started)
            {
                _x = left;
                _started = true;
            }
            else if (_x + def.Width > left + RowWidth)
            {
                _x = left;
                _y += dir * (_deepest + 3);
                _deepest = 0;
            }
            var at = new int2(_x + def.Width / 2, _y + dir * (def.Depth / 2));
            _x += def.Width + 3;
            _deepest = Math.Max(_deepest, def.Depth);
            return at;
        }
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

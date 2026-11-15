using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Ecs;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Rendering;

/// <summary>
/// Which preset building's plot each cell is in (<see cref="Buildings.At"/>), and whether it's an
/// open floor cell ('.') of any building's plan, read from an index rather than by testing every
/// building's plan. The mesher asks for each neighbour of every wall it cuts, and a remesh asks
/// thousands of times; the sim's own lookup scans every building each time. Rebuilt when the
/// buildings, the structure or the defs change. Presentation only: the sim never reads it.
/// </summary>
public sealed class BuildingPlanIndex
{
    [ThreadStatic] private static BuildingPlanIndex? _current;

    /// <summary>The index for this game, built or brought up to date as needed.</summary>
    public static BuildingPlanIndex For(Simulation sim)
    {
        var index = _current ??= new BuildingPlanIndex();
        index.Ensure(sim);
        return index;
    }

    private Simulation? _sim;
    private object? _defs;
    private int _structure = -1, _buildings = -1;
    private Entity[] _at = [];
    private bool[] _floor = [];

    /// <summary>The building whose plot holds <paramref name="cell"/>, as <see cref="Buildings.At"/> finds it.</summary>
    public Entity At(int3 cell)
    {
        var map = _sim!.Map;
        return map.InBounds(cell) ? _at[map.Idx(cell)] : Buildings.At(_sim, cell);
    }

    /// <summary>An open floor cell of some preset building's plan ('.').</summary>
    public bool IsPlanFloor(int3 cell)
    {
        var map = _sim!.Map;
        return map.InBounds(cell) && _floor[map.Idx(cell)];
    }

    private void Ensure(Simulation sim)
    {
        var map = sim.Map;
        var buildings = sim.World.Store<PresetBuilding>().Version;
        if (ReferenceEquals(sim, _sim) && ReferenceEquals(sim.Defs, _defs) && _structure == map.StructureVersion
            && _buildings == buildings && _at.Length == map.Count)
            return;
        (_sim, _defs, _structure, _buildings) = (sim, sim.Defs, map.StructureVersion, buildings);
        if (_at.Length != map.Count)
        {
            _at = new Entity[map.Count];
            _floor = new bool[map.Count];
        }
        Array.Fill(_at, Entity.Null);
        Array.Clear(_floor);

        // In store order, the first building to claim a cell keeps it: Buildings.At's answer.
        var w = sim.World;
        foreach (var e in w.Each<PresetBuilding>())
        {
            var b = w.Get<PresetBuilding>(e);
            var def = b.Def;
            int lx = Footprint.Lo(def.Width), ly = Footprint.Lo(def.Depth);
            for (var row = 0; row < def.Depth; row++)
            for (var col = 0; col < def.Width; col++)
            {
                var d = Footprint.Rotate(col + lx, row + ly, b.Facing);
                int x = b.Anchor.X + d.X, y = b.Anchor.Y + d.Y;
                // Its storeys, and the level of its roof above them.
                for (var z = b.Anchor.Z; z <= b.Anchor.Z + def.Storeys; z++)
                {
                    var cell = new int3(x, y, z);
                    if (!map.InBounds(cell))
                        continue;
                    var i = map.Idx(cell);
                    var symbol = Buildings.SymbolAt(def, b.Anchor, b.Facing, cell);
                    if (symbol == '.')
                        _floor[i] = true;
                    if (_at[i].IsNull && Claims(b, cell, symbol))
                        _at[i] = e;
                }
            }
        }
    }

    /// <summary>The test <see cref="Buildings.At"/> makes of one building for one cell.</summary>
    private static bool Claims(PresetBuilding b, int3 cell, char symbol) =>
        symbol != ' '
        || cell.Z > b.Anchor.Z && cell.Z <= b.Anchor.Z + b.Def.Storeys
            && Buildings.SymbolAt(b.Def, b.Anchor, b.Facing, new int3(cell.X, cell.Y, b.Anchor.Z)) != ' '
            && (cell.Z < b.Anchor.Z + b.Def.Storeys || b.VariantDef.Resolved.RoofDef != null);
}

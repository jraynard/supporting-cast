using DragonWorld.Sim;
using DragonWorld.Sim.Defs;

namespace DragonWorld.Sim.Tests;

/// <summary>Helpers for building small headless simulations from the real game defs.</summary>
public static class TestWorld
{
    private static readonly Lazy<DefDatabase> Defs = new(() => DefDatabase.LoadDirectory(FindDefsDirectory()));

    public static DefDatabase LoadDefs() => Defs.Value;

    /// <summary>An empty two-level map (grass ground at z = 0, air above) with the standard systems, at a fixed seed.</summary>
    public static Simulation Create(int width = 40, int height = 40, ulong seed = 1, int depth = Simulation.DefaultDepth)
    {
        var sim = Simulation.CreateStandard(Defs.Value, width, height, seed, depth);
        sim.RebuildDerivedIfNeeded();
        Array.Fill(sim.Map.Temperature, sim.Climate.OutdoorTemperature);
        return sim;
    }

    /// <summary>Ground-level cell of a <see cref="Create"/> map (the walkable level above the z = 0 ground).</summary>
    public static int3 C(int x, int y) => new(x, y, 1);

    public static ThingDef Thing(this Simulation sim, string name) => sim.Defs.Thing(name);

    /// <summary>Set every skill of a person to one level (average by default), for tests about something else.</summary>
    public static void SetSkills(this Simulation sim, Entity person, int level = Components.Skills.Average)
    {
        var skills = sim.World.Get<Components.Skills>(person);
        Array.Fill(skills.Level, level);
        Array.Fill(skills.Xp, 0f);
    }

    /// <summary>Fill a box of cells (inclusive bounds) with a block, e.g. stone to raise a plateau.</summary>
    public static void FillBlocks(this Simulation sim, int3 min, int3 max, string block = "Stone")
    {
        var def = sim.Defs.Block(block);
        for (var z = min.Z; z <= max.Z; z++)
        for (var y = min.Y; y <= max.Y; y++)
        for (var x = min.X; x <= max.X; x++)
            sim.Map.Block[sim.Map.Idx(x, y, z)] = def;
        sim.Map.MarkStructureChanged();
        sim.RebuildDerivedIfNeeded();
    }

    /// <summary>Build a finished rectangular wall ring (inclusive bounds), optionally with a door.</summary>
    public static void BuildWallRing(this Simulation sim, int3 min, int3 max, string wall = "WoodWall", int3? door = null)
    {
        for (var x = min.X; x <= max.X; x++)
        for (var y = min.Y; y <= max.Y; y++)
        {
            if (x != min.X && x != max.X && y != min.Y && y != max.Y)
                continue;
            var t = new int3(x, y, min.Z);
            sim.SpawnStructure(sim.Thing(door == t ? "Door" : wall), t);
        }
        sim.RebuildDerivedIfNeeded();
    }

    public static string FindDefsDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "data", "defs");
            if (Directory.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new DirectoryNotFoundException("Could not find data/defs above the test directory");
    }
}

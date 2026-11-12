using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Scenarios;

namespace HeroesNotIncluded.Sim.Tests;

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

    /// <summary>
    /// Millbrook, the map rounds are played on, with <paramref name="villagers"/> AI villagers and no
    /// round open: for tests that need a real town. <see cref="VillageRound.Setup"/> adds what a round
    /// needs on top (beds filled, the shrine stocked) but fixes the villager count.
    /// </summary>
    public static (Simulation Sim, int3 Center) Millbrook(ulong seed = 1234, int villagers = VillageRound.StartingVillagers, DefDatabase? defs = null)
    {
        defs ??= Defs.Value;
        var map = defs.Map(VillageRound.DefaultMap);
        var sim = MapBuilder.CreateSimulation(defs, map, seed);
        var center = MapBuilder.Build(sim, map, new MapGenerator.Options { Villagers = villagers }).Center;
        return (sim, center);
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

    /// <summary>
    /// Takes mixed drinks off the inn's sign, so a thirsty customer orders a plain ale: for tests
    /// about drink orders and their price, not about which drink.
    /// </summary>
    /// <summary>Someone served a drink or a meal sits with it a while (Hospitality.Table): tick till they've finished.</summary>
    public static void TillFinished(Simulation sim, Entity who)
    {
        for (var t = 0; t < SimClock.HoursToTicks(1.5f) && sim.World.Has<Hospitality.Savouring>(who); t += 60)
            sim.TickMany(60);
        Assert.False(sim.World.Has<Hospitality.Savouring>(who), "they finished what they were served");
    }

    public static void AleOnly(Simulation sim)
    {
        var inn = Shops.ShopSystem.BuildingOf(sim, sim.Defs.Building("Inn"));
        if (inn.IsNull)
            return;
        if (!sim.World.Has<Shops.ShopPrices>(inn))
            sim.World.Add(inn, Shops.ShopPrices.Defaults(sim.Defs));
        sim.World.Get<Shops.ShopPrices>(inn).Offered[(int)HeroesNotIncluded.Sim.Defs.ServiceKind.MixedDrink] = false;
    }
}

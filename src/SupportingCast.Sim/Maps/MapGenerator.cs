using SupportingCast.Sim.Components;

namespace SupportingCast.Sim.Maps;

/// <summary>
/// Generates the starting map: lakes, beaches, rocky hills with boulders and natural slopes,
/// forests and berry bushes, plus a flat landing site in the middle with the villagers and the egg.
/// </summary>
public static class MapGenerator
{
    /// <summary>Z levels a generated map should have: ground to dig into, hills, and building headroom.</summary>
    public const int RecommendedDepth = 12;

    public sealed class Options
    {
        public int Villagers { get; init; } = 3;
        public bool SpawnStartingKit { get; init; } = true;

        /// <summary>Herds graze here from the start and more wander in (<see cref="Systems.WildlifeSystem"/>).</summary>
        public bool Wildlife { get; init; } = true;

        /// <summary>Deep woods (the Pact's enchanted forest): groves spread into drier ground and grow denser.</summary>
        public bool Forest { get; init; }
    }

    public static readonly string[] VillagerNames =
    [
        "Aldric", "Brenna", "Corin", "Dagny", "Eamon", "Freya", "Gareth", "Hilde", "Ivo", "Jorunn",
    ];

    public static int3 Generate(Simulation sim, Options? options = null)
    {
        options ??= new Options();
        var map = sim.Map;
        var defs = sim.Defs;
        var seed = (uint)sim.Seed;

        var grass = defs.Block("Grass");
        var dirt = defs.Block("Dirt");
        var sand = defs.Block("Sand");
        var rock = defs.Block("Stone");
        var shallow = defs.Block("ShallowWater");
        var water = defs.Block("Water");
        var oak = defs.Thing("Oak");
        var pine = defs.Thing("Pine");
        var birch = defs.Thing("Birch");
        var bush = defs.Thing("BerryBush");
        var emberroot = defs.Thing("EmberrootPlant");
        var flax = defs.Thing("WildFlax");
        var boulder = defs.Thing("Boulder");

        var slope = defs.Thing("Slope");

        // Lowlands stand at baseZ, with a few levels of ground below to dig into. Hills rise up
        // to maxRise levels above, leaving headroom to build on top. Tiny maps stay flat.
        var baseZ = map.Depth >= 8 ? 4 : 1;
        var maxRise = Math.Clamp(map.Depth - baseZ - 4, 0, 4);
        var center = new int3(map.Width / 2, map.Height / 2, baseZ);
        const float clearRadius = 9f;

        var standZ = new int[map.LayerSize];
        var tops = new Defs.BlockDef[map.LayerSize];
        var moistures = new float[map.LayerSize];
        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
        {
            var elevation = Fbm(x, y, seed, 48f, 4);
            var moisture = Fbm(x, y, seed + 7919, 32f, 3);

            // Keep the landing site dry and flat.
            var dc = MathF.Sqrt((x - center.X) * (x - center.X) + (y - center.Y) * (y - center.Y));
            var landing = Math.Clamp(1f - (dc - clearRadius) / 12f, 0f, 1f);
            elevation = elevation * (1 - landing) + 0.5f * landing;

            // Highlands climb one level per elevation step; the highest are bare rock.
            var rise = elevation > 0.6f ? Math.Min(maxRise, 1 + (int)((elevation - 0.6f) / 0.05f)) : 0;
            var top = elevation switch
            {
                < 0.28f => water,
                < 0.32f => shallow,
                < 0.35f => sand,
                > 0.72f => rock,
                _ => moisture < 0.35f ? dirt : grass,
            };

            // Fill the column: stone at depth, a couple of dirt layers under soft ground, then the top.
            var h = baseZ + rise;
            for (var z = 0; z < h; z++)
            {
                var block = z == h - 1 ? top : top != rock && z >= h - 3 ? dirt : rock;
                map.Block[map.Idx(x, y, z)] = block;
            }

            var col = y * map.Width + x;
            standZ[col] = h;
            tops[col] = top;
            moistures[col] = moisture;
        }

        PlaceStrata(sim, standZ, baseZ, center, seed);
        PlaceVeins(sim, standZ, moistures, center, seed);

        // Natural slopes up some of the one-level steps, so most hills can be walked onto.
        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
        {
            var col = y * map.Width + x;
            if (tops[col].IsWater)
                continue;
            foreach (var d in Map.Neighbors4)
            {
                int nx = x + d.X, ny = y + d.Y;
                if (nx < 0 || ny < 0 || nx >= map.Width || ny >= map.Height)
                    continue;
                if (standZ[ny * map.Width + nx] != standZ[col] + 1)
                    continue;
                var cell = new int3(x, y, standZ[col]);
                if (SimRandom.Hash(x, y, seed + 57) % 10 < 3 && sim.CanPlaceStructure(cell))
                    sim.SpawnStructure(slope, cell);
            }
        }

        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
        {
            var col = y * map.Width + x;
            var dc = MathF.Sqrt((x - center.X) * (x - center.X) + (y - center.Y) * (y - center.Y));
            if (dc < clearRadius)
                continue;

            var cell = new int3(x, y, standZ[col]);
            if (!sim.CanPlaceStructure(cell))
                continue;
            var terrain = tops[col];
            var moisture = moistures[col];
            var r = SimRandom.Hash(x, y, seed + 31) / (float)uint.MaxValue;
            if (terrain == rock && r < 0.55f)
                sim.SpawnStructure(boulder, cell);
            else if (terrain == grass && moisture > (options.Forest ? 0.3f : 0.55f) && r < (options.Forest ? 0.3f : 0.22f))
                sim.SpawnStructure(Species(x, y, moisture, forest: true), cell);
            else if (terrain == grass && r < (options.Forest ? 0.08f : 0.025f))
                sim.SpawnStructure(Species(x, y, moisture, forest: false), cell);
            else if ((terrain == grass || terrain == dirt) && r > 0.988f)
                sim.SpawnStructure(bush, cell);
            // Emberroot (a dragon-cake ingredient) on forest edges and on the hills, from a roll of its
            // own so it doesn't move anything else.
            else if ((terrain == grass || terrain == dirt) && (moisture is > 0.4f and < 0.6f || standZ[col] > center.Z)
                && SimRandom.Hash(x, y, seed + 71) / (float)uint.MaxValue > 0.985f)
                sim.SpawnStructure(emberroot, cell);
            // Wild flax (fiber for bandages) in damp open grass, also from a roll of its own.
            else if (terrain == grass && moisture > 0.45f && SimRandom.Hash(x, y, seed + 83) / (float)uint.MaxValue > 0.99f)
                sim.SpawnStructure(flax, cell);
        }

        // A few guaranteed resources near the start so the first hour isn't spent walking.
        PlaceCluster(sim, c => Species(c.X, c.Y, moistures[c.Y * map.Width + c.X], forest: true), center + new int3(14, -6, 0), 10, seed + 1);
        PlaceCluster(sim, boulder, center + new int3(-15, 8, 0), 6, seed + 2);
        PlaceCluster(sim, bush, center + new int3(-8, -13, 0), 4, seed + 3);
        PlaceCluster(sim, emberroot, center + new int3(10, 12, 0), 4, seed + 5);
        // Flax near the start, so the first wounds can be bandaged.
        PlaceCluster(sim, flax, center + new int3(-12, -2, 0), 4, seed + 6);

        sim.RebuildDerivedIfNeeded();

        // Start everything at the outdoor temperature, and light it, so the first frame looks right.
        Array.Fill(map.Temperature, sim.Climate.OutdoorTemperature);
        sim.Lighting.Update(sim);

        if (options.SpawnStartingKit)
            SpawnStart(sim, center, options.Villagers);
        if (options.Wildlife)
        {
            sim.Wildlife.PlaceStartingHerds(sim, center);
            sim.Wildlife.Enable(sim);
        }

        return center;

        // Which tree grows where: a slow noise grows groves of one species, oaks in the wettest
        // ground and pines in drier stands, birches where they meet and out on open grassland.
        // About one tree in ten is a stray from elsewhere. Hash-based, so it uses no sim.Rand.
        Defs.ThingDef Species(int x, int y, float moisture, bool forest)
        {
            var roll = SimRandom.Hash(x, y, seed + 4244) % 100;
            if (!forest)
                return roll < 55 ? birch : roll < 80 ? oak : pine;
            var grove = Fbm(x, y, seed + 4243, 22f, 2) + (moisture - 0.62f) * 0.8f;
            var pick = grove < 0.44f ? pine : grove > 0.54f ? oak : birch;
            if (roll < 10)
                pick = roll < 4 ? birch : pick == oak ? pine : oak;
            return pick;
        }
    }

    /// <summary>Nothing changes this close to the landing, so the start (and the slice) plays the same with or without veins.</summary>
    private const float VeinClearRadius = 24f;

    /// <summary>
    /// Kinds of rock, hash-based like everything here (no sim.Rand): basalt at the bottom of the
    /// map, granite in the cores of high hills, slate in thin bands through the hills, and the
    /// rest limestone, sandstone or plain rough stone by region. Kept clear of the
    /// landing, which stays rough stone so the start plays the same. Skipped if the defs lack them.
    /// </summary>
    private static void PlaceStrata(Simulation sim, int[] standZ, int baseZ, int3 center, uint seed)
    {
        var map = sim.Map;
        var defs = sim.Defs;
        if (!defs.TryBlock("Sandstone", out var sandstone) || !defs.TryBlock("Limestone", out var limestone)
            || !defs.TryBlock("Granite", out var granite) || !defs.TryBlock("Slate", out var slate) || !defs.TryBlock("Basalt", out var basalt))
            return;
        var rock = defs.Block("Stone");
        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
        {
            var dc = MathF.Sqrt((x - center.X) * (x - center.X) + (y - center.Y) * (y - center.Y));
            if (dc < VeinClearRadius)
                continue;
            var h = standZ[y * map.Width + x];
            var rise = h - baseZ;
            var region = Fbm(x, y, seed + 9301, 20f, 2);
            var slateBand = Fbm(x, y, seed + 9321, 9f, 2) > 0.58f;
            var deepBasalt = Fbm(x, y, seed + 9311, 10f, 2) > 0.55f;
            for (var z = 0; z < h; z++)
            {
                var i = map.Idx(x, y, z);
                if (map.Block[i] != rock)
                    continue;
                var core = Fbm(x + z * 37, y - z * 53, seed + 9331, 8f, 2);
                Defs.BlockDef kind;
                if (z == 0 || (z == 1 && deepBasalt))
                    kind = basalt;
                else if (rise >= 2 && z >= baseZ - 1 && core > 0.42f)
                    kind = granite;
                else if (rise >= 1 && slateBand && z == baseZ)
                    kind = slate;
                else
                    kind = region > 0.55f ? sandstone : region > 0.45f ? rock : limestone;
                map.Block[i] = kind;
            }
        }
    }

    /// <summary>
    /// Ore and clay from the <see cref="Defs.VeinDef"/>s, each in blobs of its own hashed noise
    /// inside its host rock, at its depth below the surface. Kept clear of the landing.
    /// </summary>
    private static void PlaceVeins(Simulation sim, int[] standZ, float[] moistures, int3 center, uint seed)
    {
        var map = sim.Map;
        if (sim.Defs.AllVeins.Count == 0)
            return;
        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
        {
            var dc = MathF.Sqrt((x - center.X) * (x - center.X) + (y - center.Y) * (y - center.Y));
            if (dc < VeinClearRadius)
                continue;
            var col = y * map.Width + x;
            var h = standZ[col];
            foreach (var vein in sim.Defs.AllVeins)
            {
                if (moistures[col] < vein.MoistureMin)
                    continue;
                for (var depth = vein.DepthMin; depth <= vein.DepthMax; depth++)
                {
                    var z = h - depth;
                    if (z < 0)
                        break;
                    var i = map.Idx(x, y, z);
                    if (!vein.HostDefs.Contains(map.Block[i]))
                        continue;
                    var n = vein.Flat ? Fbm(x, y, seed + vein.Salt, vein.Scale, 2) : Fbm(x + z * 37, y - z * 53, seed + vein.Salt, vein.Scale, 2);
                    if (n > vein.Threshold)
                        map.Block[i] = vein.BlockDef;
                }
            }
        }
    }

    private static void PlaceCluster(Simulation sim, Defs.ThingDef def, int3 at, int count, uint seed) =>
        PlaceCluster(sim, _ => def, at, count, seed);

    private static void PlaceCluster(Simulation sim, Func<int3, Defs.ThingDef> defAt, int3 at, int count, uint seed)
    {
        var placed = 0;
        foreach (var t in sim.SpiralFrom(at, 6))
        {
            if (placed >= count)
                break;
            if (SimRandom.Hash(t.X, t.Y, seed) % 3 != 0)
                continue;
            if (!sim.CanPlaceStructure(t))
                continue;
            sim.SpawnStructure(defAt(t), t);
            placed++;
        }
    }

    private static void SpawnStart(Simulation sim, int3 center, int villagers)
    {
        var human = sim.Defs.Creature("Human");
        var people = new List<Entity>();
        for (var i = 0; i < villagers; i++)
        {
            var name = VillagerNames[(i + (int)(sim.Seed % 7)) % VillagerNames.Length];
            var villager = sim.SpawnCreature(human, name, center + new int3(i - villagers / 2, 2, 0), ageDays: 25 * 365);
            sim.GiveStartingPack(villager);
            people.Add(villager);
        }

        var egg = sim.CreateItem(sim.Defs.Thing("DragonEgg"), 1);
        sim.PlaceItemNear(egg, center + new int3(0, -1, 0));

        // The settlers brought pine planks; everything later comes from what they fell.
        sim.SpawnItem(sim.Defs.Thing("Wood"), 60, center + new int3(3, -2, 0), sim.Defs.Material("Pine"));
        // Food for the first days: after that the dragon's meat comes from hunting and fishing
        // (it rots, so there's no point bringing more). One hunting bow among them.
        sim.SpawnItem(sim.Defs.Thing("Berries"), 120, center + new int3(-3, -2, 0));
        sim.SpawnItem(sim.Defs.Thing("Meat"), 80, center + new int3(-4, 0, 0));
        sim.SpawnItem(sim.Defs.Thing("Bow"), 1, center + new int3(1, -3, 0));
        // Everyone has a stone axe and pick on their belt (GiveStartingPack); one rod for the fisher.
        sim.SpawnItem(sim.Defs.Thing("FishingRod"), 1, center + new int3(2, -3, 0));
        // One good jerkin among them: whoever is free first puts it on.
        sim.SpawnItem(sim.Defs.Thing("LeatherJerkin"), 1, center + new int3(2, -3, 0));
        // Clothes last, so they don't shift the entities (and the rolls seeded by them) above.
        foreach (var villager in people)
            Systems.Clothing.DressVillager(sim, villager);

        sim.Messages.Add(
            MessageKind.Info,
            sim.Presets.PresetMode
                ? "Your villagers have arrived with a dragon egg. Raise a hatchery (the Buildings bar) to keep the egg warm through the night."
                : "Your villagers have arrived with a dragon egg. Build a fire and keep the egg warm through the night.",
            center
        );
    }

    // ---- Value noise ----

    private static float ValueNoise(float x, float y, uint seed)
    {
        var x0 = (int)MathF.Floor(x);
        var y0 = (int)MathF.Floor(y);
        var fx = x - x0;
        var fy = y - y0;
        fx = fx * fx * (3 - 2 * fx);
        fy = fy * fy * (3 - 2 * fy);
        float H(int ix, int iy) => SimRandom.Hash(ix, iy, seed) / (float)uint.MaxValue;
        var a = H(x0, y0) + (H(x0 + 1, y0) - H(x0, y0)) * fx;
        var b = H(x0, y0 + 1) + (H(x0 + 1, y0 + 1) - H(x0, y0 + 1)) * fx;
        return a + (b - a) * fy;
    }

    private static float Fbm(int x, int y, uint seed, float scale, int octaves)
    {
        float sum = 0, amp = 1, norm = 0, freq = 1f / scale;
        for (var o = 0; o < octaves; o++)
        {
            sum += ValueNoise(x * freq, y * freq, seed + (uint)o * 1013) * amp;
            norm += amp;
            amp *= 0.5f;
            freq *= 2f;
        }
        return sum / norm;
    }
}

using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Maps;

namespace SupportingCast.Sim.Scenarios;

/// <summary>
/// Standard village setup plus the orders a player would give in the vertical slice: a wooden hall
/// west of the landing site (campfire, stairs and nest below; a second storey with beds once the
/// fire is lit), a tunnel into the nearest hill for stone, and trees marked for chopping. Shared
/// by the scenario test and the game's --demo flag.
/// </summary>
public static class SliceDemo
{
    /// <summary>Starter stockpile and designated keeper, as every new village gets.</summary>
    public static void ApplyVillageDefaults(Simulation sim, int3 center)
    {
        var map = sim.Map;
        for (var y = 3; y < 8; y++)
        for (var x = 3; x < 9; x++)
            map.SetZone(map.Idx(center.X + x, center.Y + y, center.Z), 1);
        foreach (var e in sim.World.Query<Creature>())
        {
            if (sim.World.Get<Creature>(e).Def.Race != Defs.Race.Human)
                continue;
            sim.World.Add(e, new Keeper { Dragon = Entity.Null });
            break;
        }
    }

    /// <summary>Where the hall that <see cref="ApplyPlayerOrders"/> lays out is.</summary>
    public readonly record struct Hall(int3 Min, int3 Max, int3 Campfire, int3 Stairs, int3 Nest, int3? DigFace)
    {
        /// <summary>A cell in the middle of the upper room.</summary>
        public int3 UpperRoom => new(Campfire.X, Campfire.Y, Campfire.Z + 1);

        public IReadOnlyList<int3> Beds => [new(Max.X - 1, Min.Y + 1, Min.Z + 1), new(Max.X - 1, Max.Y - 1, Min.Z + 1)];
    }

    /// <summary>
    /// The first orders: the ground floor of a wooden hall west of the landing site (walls, a
    /// door, the campfire for the egg, stairs up and a dragon nest), a short tunnel into the
    /// nearest hillside for the nest's stone, and trees marked for chopping. The upper storey
    /// comes later (<see cref="ApplyUpperStoreyOrders"/>): ordered all at once, the villagers
    /// would spend the first day's wood on floorboards instead of the egg's fire.
    /// </summary>
    public static Hall ApplyPlayerOrders(Simulation sim, int3 center)
    {
        var map = sim.Map;
        var z = center.Z;
        var min = new int3(center.X - 8, center.Y - 4, z);
        var max = new int3(center.X - 4, center.Y, z);
        var door = new int3(center.X - 6, center.Y, z);
        Ring(sim, min, max, door);
        var fire = new int3(center.X - 6, center.Y - 2, z);
        sim.PlaceBlueprint(sim.Defs.Thing("Campfire"), fire);
        var stairs = new int3(center.X - 7, center.Y - 3, z);
        sim.PlaceBlueprint(sim.Defs.Thing("WoodStairs"), stairs);
        // The nest takes 3x3 cells: it goes outside, by the hall's east wall, or the nearest place it fits.
        var nest = PlaceNear(sim, sim.Defs.Thing("Nest"), new int3(center.X - 2, center.Y - 2, z));

        var dig = DigIntoNearestHill(sim, center);

        for (var y = center.Y - 25; y <= center.Y + 25; y++)
        for (var x = center.X - 25; x <= center.X + 25; x++)
        for (var cz = 0; cz < map.Depth; cz++)
        {
            if (!map.InBounds(x, y, cz))
                continue;
            var s = map.Structure[map.Idx(x, y, cz)];
            if (!s.IsNull && sim.World.Get<Thing>(s).Def.IsTree)
                map.Designate(map.Idx(x, y, cz), Designation.Chop);
        }
        MarkFishingSpots(sim, center);
        return new Hall(min, max, fire, stairs, nest, dig);
    }

    /// <summary>Where the preset-building slice's orders went (<see cref="ApplyPresetOrders"/>, <see cref="ApplyHomeOrders"/>).</summary>
    public readonly record struct Homestead(Entity Hatchery, int3 Nest, int3? DigFace)
    {
        public Entity Dwelling { get; init; }
        public Entity Longhouse { get; init; }
    }

    /// <summary>
    /// The first orders with preset buildings on (<c>Systems.BuildingSystem.PresetMode</c>): the
    /// hatchery by the landing (a hut round a fire, the one place warm enough for the egg at
    /// night), the nest beside it, a tunnel for stone, trees marked for chopping. Houses come
    /// once the egg is safe (<see cref="ApplyHomeOrders"/>), as the upper storey does in the
    /// freeform slice.
    /// </summary>
    public static Homestead ApplyPresetOrders(Simulation sim, int3 center)
    {
        var map = sim.Map;
        var z = center.Z;
        var hatchery = PlaceBuildingNear(sim, sim.Defs.Building("Hatchery"), new int3(center.X - 4, center.Y - 2, z), facing: 0);
        var nest = PlaceNear(sim, sim.Defs.Thing("Nest"), new int3(center.X - 2, center.Y - 7, z));
        var dig = DigIntoNearestHill(sim, center);
        for (var y = center.Y - 25; y <= center.Y + 25; y++)
        for (var x = center.X - 25; x <= center.X + 25; x++)
        for (var cz = 0; cz < map.Depth; cz++)
        {
            if (!map.InBounds(x, y, cz))
                continue;
            var s = map.Structure[map.Idx(x, y, cz)];
            if (!s.IsNull && sim.World.Get<Thing>(s).Def.IsTree)
                map.Designate(map.Idx(x, y, cz), Designation.Chop);
        }
        MarkFishingSpots(sim, center);
        return new Homestead(hatchery, nest, dig);
    }

    /// <summary>The second orders: a dwelling west of the landing and the longhouse south-west of it.</summary>
    public static Homestead ApplyHomeOrders(Simulation sim, int3 center, Homestead home) => home with
    {
        Dwelling = PlaceBuildingNear(sim, sim.Defs.Building("Dwelling"), new int3(center.X - 12, center.Y - 3, center.Z), facing: 0),
        Longhouse = PlaceBuildingNear(sim, sim.Defs.Building("Longhouse"), new int3(center.X - 9, center.Y + 8, center.Z), facing: 0),
    };

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

    /// <summary>
    /// The second storey: planks over the hall (open above the stairs, so heat and smoke rise up
    /// the stairwell), walls and two beds.
    /// </summary>
    public static void ApplyUpperStoreyOrders(Simulation sim, Hall hall)
    {
        var up = hall.Min.Z + 1;
        var hole = new int3(hall.Stairs.X, hall.Stairs.Y, up);
        for (var y = hall.Min.Y; y <= hall.Max.Y; y++)
        for (var x = hall.Min.X; x <= hall.Max.X; x++)
            if (new int3(x, y, up) != hole)
                sim.PlaceBlueprint(sim.Defs.Thing("WoodFloor"), new int3(x, y, up));
        Ring(sim, new int3(hall.Min.X, hall.Min.Y, up), new int3(hall.Max.X, hall.Max.Y, up), null);
        // Beds are two cells long: headboard to the nearer wall (north or south), foot into the room.
        foreach (var b in hall.Beds)
            sim.PlaceBlueprint(sim.Defs.Thing("Bed"), b, facing: b.Y > hall.Campfire.Y ? 2 : 0);
    }

    private static void MineNearestBoulder(Simulation sim, int3 center)
    {
        var best = Entity.Null;
        var bestDist = int.MaxValue;
        foreach (var e in sim.World.Query<Thing>())
        {
            var t = sim.World.Get<Thing>(e);
            if (!t.Spawned || t.Def.Name != "Boulder" || !sim.Regions.CanReach(center, t.Cell, PathEnd.Touch))
                continue;
            var d = Map.Distance(center, t.Cell);
            if (d < bestDist)
            {
                best = e;
                bestDist = d;
            }
        }
        if (!best.IsNull)
            sim.Map.Designate(sim.Map.Idx(sim.World.Get<Thing>(best).Cell), Designation.Mine);
    }

    /// <summary>
    /// A blueprint at <paramref name="preferred"/>, or failing that the nearest cell on the same level (in
    /// square rings, row by row) where it fits: wide structures like the nest need a clear patch.
    /// </summary>
    private static int3 PlaceNear(Simulation sim, ThingDef def, int3 preferred)
    {
        for (var r = 0; r <= 12; r++)
        for (var dy = -r; dy <= r; dy++)
        for (var dx = -r; dx <= r; dx++)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r)
                continue;
            var c = new int3(preferred.X + dx, preferred.Y + dy, preferred.Z);
            if (sim.Map.InBounds(c) && !sim.PlaceBlueprint(def, c).IsNull)
                return c;
        }
        return preferred;
    }

    private static void Ring(Simulation sim, int3 min, int3 max, int3? door)
    {
        for (var y = min.Y; y <= max.Y; y++)
        for (var x = min.X; x <= max.X; x++)
        {
            if (x != min.X && x != max.X && y != min.Y && y != max.Y)
                continue;
            var t = new int3(x, y, min.Z);
            sim.PlaceBlueprint(sim.Defs.Thing(t == door ? "Door" : "WoodWall"), t);
        }
    }

    /// <summary>
    /// Find the closest hillside at the landing level (a diggable block next to open ground) and
    /// mark a three-block tunnel into it, and mark the nearest boulder for mining: the nest needs
    /// its stone. Returns the tunnel's first block, or null if there's no hill nearby.
    /// </summary>
    private static int3? DigIntoNearestHill(Simulation sim, int3 center)
    {
        var map = sim.Map;
        MineNearestBoulder(sim, center);
        for (var r = 1; r <= 30; r++)
        for (var dy = -r; dy <= r; dy++)
        for (var dx = -r; dx <= r; dx++)
        {
            if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != r)
                continue;
            var face = new int3(center.X + dx, center.Y + dy, center.Z);
            if (!map.InBounds(face) || !sim.CanDig(face))
                continue;
            foreach (var d in Map.Neighbors4)
            {
                var front = Map.Offset(face, new int2(-d.X, -d.Y));
                if (!map.Passable(front) || !sim.Regions.CanReach(center, front, PathEnd.OnTile))
                    continue;
                // Tunnel straight in, away from the open side.
                for (var k = 0; k < 3; k++)
                {
                    var c = Map.Offset(face, new int2(d.X * k, d.Y * k));
                    if (sim.CanDig(c))
                        map.Designate(map.Idx(c), Designation.Dig);
                }
                return face;
            }
        }
        return null;
    }
}

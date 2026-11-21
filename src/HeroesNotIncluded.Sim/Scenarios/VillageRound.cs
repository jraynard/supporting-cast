using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Scenarios;

/// <summary>Who a player made in the creator.</summary>
public sealed record Hero(string? Name, Appearance? Look, IReadOnlyList<string>? Clothes = null);

/// <summary>
/// The village a round is played in: a hand-made map (<see cref="MapDef"/>, Millbrook unless told
/// otherwise) with its working buildings already up and AI villagers living in it, so a trade
/// nobody took still gets done and players walk into a place that's running.
/// </summary>
public static class VillageRound
{
    /// <summary>
    /// AI villagers at the start, before any player arrives: one to each of the six trades the AI works, and a
    /// townsperson with no trade: a second pair of hands when someone at work for two calls for one, the bucket
    /// line, the public fires. Never a trade's work, nor errands or hauling: those were the labourer's, and are
    /// the players' now.
    /// </summary>
    public const int StartingVillagers = 7;

    /// <summary>The map a round is played on unless another is chosen (data/defs/maps/).</summary>
    public const string DefaultMap = "Millbrook";

    /// <summary>
    /// The landing point and the buildings trades will work from. <see cref="Tavern"/> and
    /// <see cref="Kitchen"/> are the same building where they share a roof (Millbrook's inn).
    /// </summary>
    public readonly record struct Town(int3 Center, Entity Tavern, Entity Forge, Entity Shrine, Entity TinkerShop)
    {
        public Entity Kitchen { get; init; }
        public Entity Woodshop { get; init; }
    }

    /// <summary>A simulation the size of the round's map, with the standard systems: what <see cref="Setup"/> builds into.</summary>
    public static Simulation NewSimulation(DefDatabase defs, ulong seed, string map = DefaultMap) =>
        MapBuilder.CreateSimulation(defs, defs.Map(map), seed);

    /// <summary>Lay out the map and raise the town (see <see cref="NewSimulation"/>). Returns where everything is.</summary>
    public static Town Setup(Simulation sim, string map = DefaultMap)
    {
        var built = MapBuilder.Build(sim, sim.Defs.Map(map), new MapGenerator.Options { Villagers = StartingVillagers });
        // Who works where is the buildings' roles (buildings.json "roles"), not the map's say.
        Entity Role(TownRole role) => Shops.ShopSystem.BuildingFor(sim, role);
        var shrine = Role(TownRole.Shrine);
        StockShrine(sim, shrine);
        StockSmithy(sim, Role(TownRole.Forge));
        StockInn(sim, Role(TownRole.Tavern), Role(TownRole.Kitchen));
        LockStoreroom(sim, Role(TownRole.Tavern));
        Hospitality.InnRooms.FitLocks(sim, Role(TownRole.Tavern));
        StockTinkerShop(sim, Role(TownRole.TinkerShop));
        Modding.Trades.StockTown(sim); // each trade's own workplace (the woodshop's logs)
        if (sim.Defs.Season.BarnWood > 0)
            sim.PlaceItemNear(sim.CreateItem(sim.Defs.Thing(Systems.Stock.TimberDef), sim.Defs.Season.BarnWood, sim.Defs.Material("Pine")), StockpileMiddle(sim));
        if (sim.Defs.Season.BarnPlanks > 0)
            sim.PlaceItemNear(sim.CreateItem(sim.Defs.Thing(Systems.Stock.PlanksDef), sim.Defs.Season.BarnPlanks, sim.Defs.Material("Pine")), StockpileMiddle(sim));
        if (sim.Defs.Season.BarnFirewood > 0)
            sim.PlaceItemNear(sim.CreateItem(sim.Defs.Thing(Systems.Stock.FirewoodDef), sim.Defs.Season.BarnFirewood, sim.Defs.Material("Pine")), StockpileMiddle(sim));
        Environment.Buckets.HangAtWells(sim); // for fighting fire
        // Beds for everyone, or the first night sends them all out to sleep in the cold.
        foreach (var (spec, home) in built.Buildings)
            if (spec.Role == "Home")
                VillageSetup.FillSpareBeds(sim, home);
        sim.RebuildDerivedIfNeeded();
        return new Town(built.Center, Role(TownRole.Tavern), Role(TownRole.Forge), shrine, Role(TownRole.TinkerShop)) { Kitchen = Role(TownRole.Kitchen), Woodshop = Role(TownRole.Woodshop) };
    }

    /// <summary>The stockpile cell nearest the middle of the town's stockpile (the barn's): where its stock is laid and delivered.</summary>
    public static int3 StockpileMiddle(Simulation sim)
    {
        var map = sim.Map;
        if (map.ZonedCells.Count == 0)
            return new int3(map.Width / 2, map.Height / 2, 0);
        long x = 0, y = 0, z = 0;
        foreach (var i in map.ZonedCells)
        {
            var c = map.CellOf(i);
            (x, y, z) = (x + c.X, y + c.Y, z + c.Z);
        }
        var n = map.ZonedCells.Count;
        var mid = new int3((int)(x / n), (int)(y / n), (int)(z / n));
        return map.ZonedCells.Select(map.CellOf).OrderBy(c => Maps.Map.Distance(c, mid)).First();
    }

    /// <summary>A trade's workplace stocked again, as it was at the start (the practice yard's Restock).</summary>
    public static void Restock(Simulation sim, Entity building)
    {
        if (!sim.World.IsAlive(building) || !sim.World.TryGet<PresetBuilding>(building, out var b))
            return;
        if (b.Def.Has(TownRole.Tavern) || b.Def.Has(TownRole.Kitchen))
            StockInn(sim, b.Def.Has(TownRole.Tavern) ? building : Entity.Null, b.Def.Has(TownRole.Kitchen) ? building : Entity.Null);
        if (b.Def.Has(TownRole.Forge))
            StockSmithy(sim, building);
        if (b.Def.Has(TownRole.Shrine))
            StockShrine(sim, building);
        if (b.Def.Has(TownRole.TinkerShop))
            StockTinkerShop(sim, building);
        Modding.Trades.Stock(sim, building); // each trade's own (the woodshop)
    }

    /// <summary>Clean linen in the inn's storeroom: a change for every room, and a few over.</summary>
    public const int RoomLinen = 8;


    /// <summary>
    /// The inn's storeroom door has a lock fitted, and starts locked: the innkeeper holds the key
    /// (<see cref="Defs.TradeDef.HoldsKeys"/>), and anyone else picks it.
    /// </summary>
    private static void LockStoreroom(Simulation sim, Entity inn)
    {
        if (inn.IsNull)
            return;
        var w = sim.World;
        var parts = Buildings.Parts(sim, inn);
        var chest = parts.FirstOrDefault(e => w.Get<Thing>(e).Def.Name == "Chest", Entity.Null);
        if (chest.IsNull)
            return;
        var at = w.Get<Thing>(chest).Cell;
        var door = parts.Where(e => w.Get<Thing>(e).Def.Building is { IsDoor: true } && w.Get<Thing>(e).Cell.Z == at.Z)
            .OrderBy(e => Maps.Map.Distance(w.Get<Thing>(e).Cell, at)).FirstOrDefault(Entity.Null);
        if (door.IsNull)
            return;
        w.Add(door, new Tinkering.LockCut { Seed = sim.Rand.NextUInt() | 1u, Pins = StoreroomPins, Tightness = 0.5f, Maker = Entity.Null });
        sim.SetLocked(door, true);
    }

    /// <summary>Pins in the storeroom's lock.</summary>
    public const int StoreroomPins = 4;

    /// <summary>Stock in the tinker's chest: iron for locks and clockwork, planks for contraptions, timber for wagon axles and the bridge, charcoal for rockets.</summary>
    private static void StockTinkerShop(Simulation sim, Entity shop)
    {
        if (shop.IsNull)
            return;
        foreach (var e in sim.World.Query<PartOf>())
            if (sim.World.Get<PartOf>(e).Building == shop && sim.World.Get<Thing>(e).Def.Name == "Chest")
            {
                sim.Store(e, sim.CreateItem(sim.Defs.Thing("IronBar"), 6));
                sim.Store(e, sim.CreateItem(sim.Defs.Thing(Systems.Stock.PlanksDef), TinkerPlanks)); // contraptions
                sim.Store(e, sim.CreateItem(sim.Defs.Thing(Systems.Stock.TimberDef), TinkerTimber)); // new axles for parties' wagons, the bridge
                sim.Store(e, sim.CreateItem(sim.Defs.Thing("Charcoal"), 6));
                return;
            }
    }

    /// <summary>Planks and timber in the tinker's chest at the start.</summary>
    public const int TinkerPlanks = 10, TinkerTimber = 6;

    /// <summary>Iron ore and planks in the smithy's chest at the start: a few blooms' worth, and hafts for spears. The carter brings more (<see cref="Shops.OreCarterSystem"/>).</summary>
    public const int SmithyOre = 12, SmithyPlanks = 6;

    private static void StockSmithy(Simulation sim, Entity smithy)
    {
        if (smithy.IsNull)
            return;
        foreach (var e in sim.World.Query<PartOf>())
        {
            if (sim.World.Get<PartOf>(e).Building != smithy)
                continue;
            switch (sim.World.Get<Thing>(e).Def.Name)
            {
                case "FuelBin":
                    sim.Store(e, sim.CreateItem(sim.Defs.Thing("Charcoal"), sim.Defs.Season.SmithyCharcoal));
                    break;
                case "Chest":
                    sim.Store(e, sim.CreateItem(sim.Defs.Thing("IronOre"), SmithyOre));
                    sim.Store(e, sim.CreateItem(sim.Defs.Thing(Systems.Stock.PlanksDef), SmithyPlanks));
                    foreach (var t in sim.Defs.Season.SmithyStock)
                        sim.Store(e, sim.CreateItem(t.ThingDef, t.Count, t.MaterialDef));
                    break;
            }
        }
    }

    /// <summary>What the inn's larder starts a round with: a day or two of stew for a busy common room.</summary>
    public static readonly (string Def, int Count)[] LarderStock = [("Cabbage", 12), ("Meat", 10), ("Berries", 20), ("Emberroot", 6), ("Herbs", 6), ("Flour", 8), ("Oats", 8), ("PorkSide", 1), ("Fowl", 2), ("CoarseSalt", 6)];

    /// <summary>Kegs in the inn's storeroom at the start, and the clean crockery on its shelves.</summary>
    public const int StoreroomKegs = 3, Tankards = 10, Bowls = 10;

    /// <summary>What's on the shelf behind the bar for mixing drinks.</summary>
    public static readonly (string Def, int Count)[] BarStock = [("Spirits", 8), ("Honey", 8), ("Herbs", 8), ("Berries", 10)];

    /// <summary>The inn opens stocked: food in the larder, kegs in the storeroom, tankards on the shelf behind the bar and bowls on the kitchen's.</summary>
    /// <summary>
    /// The tavern's storeroom and bar, and the kitchen's larder, logs and bowls: one building or two
    /// (either may be Entity.Null). Stocked in the same order whichever, so Millbrook's inn comes out as it always has.
    /// </summary>
    private static void StockInn(Simulation sim, Entity tavern, Entity kitchen)
    {
        var w = sim.World;
        if (!kitchen.IsNull)
        {
            Shops.InnSupplySystem.Deliver(sim, kitchen, "Larder", LarderStock);
            Shops.InnSupplySystem.Deliver(sim, kitchen, "LogBasket", Shops.InnSupplySystem.Logs);
        }
        if (!tavern.IsNull)
        {
            Shops.InnSupplySystem.Deliver(sim, tavern, "Chest", [("Keg", StoreroomKegs)]);
            // The rooms' clean linen, and a bucket for the slops and the jugs (Hospitality.InnRooms).
            Shops.InnSupplySystem.Deliver(sim, tavern, "Chest", [("Linen", RoomLinen), (Environment.Buckets.Empty, 1)]);
        }
        Entity Near(Entity building, string def, Func<Defs.ThingDef, bool> by)
        {
            if (building.IsNull)
                return Entity.Null;
            var parts = Buildings.Parts(sim, building);
            var mark = parts.FirstOrDefault(e => by(w.Get<Thing>(e).Def), Entity.Null);
            if (mark.IsNull)
                return Entity.Null;
            var at = w.Get<Thing>(mark).Cell;
            return parts.Where(e => w.Get<Thing>(e).Def.Name == def).OrderBy(e => Maps.Map.Distance(w.Get<Thing>(e).Cell, at)).FirstOrDefault(Entity.Null);
        }
        if (Near(tavern, "BarShelf", d => d.Building?.Cask != null) is { IsNull: false } bar)
        {
            sim.Store(bar, sim.CreateItem(sim.Defs.Thing("Tankard"), Tankards));
            foreach (var (def, count) in BarStock)
                sim.Store(bar, sim.CreateItem(sim.Defs.Thing(def), count));
        }
        if (Near(kitchen, "Shelf", d => d.Building?.StewPot != null) is { IsNull: false } shelf)
            sim.Store(shelf, sim.CreateItem(sim.Defs.Thing("Bowl"), Bowls));
    }

    /// <summary>Bandages in the shrine's chest: the healer's backup for when the mana runs dry.</summary>
    public const int ShrineBandages = 12;


    /// <summary>Where the shrine keeps its dressings: the linen basket, else its first chest (Entity.Null if neither).</summary>
    public static Entity ShrineStore(Simulation sim, Entity shrine)
    {
        if (shrine.IsNull)
            return Entity.Null;
        return sim.World.Query<PartOf>()
            .Where(e => sim.World.Get<PartOf>(e).Building == shrine && sim.World.Get<Thing>(e).Def.Building is { StorageStacks: > 0 })
            .OrderBy(e => sim.World.Get<Thing>(e).Def.Name == "LinenBasket" ? 0 : 1).ThenBy(e => e.Index).FirstOrDefault(Entity.Null);
    }

    private static void StockShrine(Simulation sim, Entity shrine)
    {
        var store = ShrineStore(sim, shrine);
        if (store.IsNull)
            return;
        sim.Store(store, sim.CreateItem(sim.Defs.Thing(Systems.Bandaging.BandageDef), ShrineBandages));
        sim.Store(store, sim.CreateItem(sim.Defs.Thing(Systems.Bandaging.ClothDef), sim.Defs.Season.ShrineCloth));
    }

    /// <summary>A player's character walking into town near <paramref name="near"/>. Co-op's host claims it on joining (<see cref="Players.Join"/>).</summary>
    public static Entity SpawnHero(Simulation sim, Hero hero, int3 near)
    {
        var w = sim.World;
        var cell = sim.SpiralFrom(near + new int3(0, 2, 0), 10).FirstOrDefault(c => sim.Map.Passable(c), near);
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), string.IsNullOrWhiteSpace(hero.Name) ? "Arin" : hero.Name.Trim(), cell, ageDays: 24 * 365f);
        Array.Fill(w.Get<Needs>(me).Values, 0.75f);
        w.Add(me, new Avatar());
        sim.GiveStartingPack(me);
        if (hero.Look is { } look)
            w.Add(me, look);
        Clothing.DressHero(sim, me, hero.Clothes); // last, so it shifts no other entity
        return me;
    }
}

using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Shops;

/// <summary>
/// An AI innkeeper washes up: gathers the dirty crockery lying about the inn, scrubs it at the tub
/// and puts it back on the shelf clean. Without it the bar runs out of tankards.
/// </summary>
public sealed class WashJob(Entity tub) : Job
{
    /// <summary>Ticks to wash one vessel.</summary>
    public const int TicksEach = 30;


    public static Job? For(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        if (!w.TryGet<Trade>(pawn, out var trade) || !trade.Def.HasWorkplace)
            return null;
        var inn = ShopSystem.WorkplaceOf(sim, trade.Def);
        if (inn.IsNull)
            return null;
        var tub = Buildings.Parts(sim, inn).FirstOrDefault(e => BarWork.IsTub(sim, e), Entity.Null);
        if (tub.IsNull || sim.Reservations.IsReservedByOther(tub, pawn) || Dirty(sim, pawn, tub).IsNull)
            return null;
        return new WashJob(tub);
    }

    /// <summary>
    /// The clean vessels a villager's trade serves into at their workplace: the bar's tankards
    /// for whoever pours, the pot's bowls for whoever cooks. Each washes up their own.
    /// </summary>
    public static HashSet<string> VesselsOf(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        var kinds = new HashSet<string>();
        if (!w.TryGet<Trade>(pawn, out var trade) || !trade.Def.HasWorkplace)
            return kinds;
        foreach (var part in Buildings.Parts(sim, ShopSystem.WorkplaceOf(sim, trade.Def)))
        {
            var b = w.Get<Thing>(part).Def.Building;
            if (b?.Cask is { } cask && trade.Def.Serving(ServiceKind.Drink))
                kinds.Add(cask.Into);
            if (b?.StewPot is { } pot && trade.Def.Serving(ServiceKind.Meal))
                kinds.Add(pot.Into);
        }
        // Whoever lets the rooms washes the sheets that come off the beds.
        if (trade.Def.Serving(ServiceKind.Lodging))
            kinds.Add(Hospitality.RoomChores.LinenDef);
        return kinds;
    }

    /// <summary>The nearest dirty tankard or bowl of theirs, wherever the customer left it (by the shrine, as often as not).</summary>
    private static Entity Dirty(Simulation sim, Entity pawn, Entity tub)
    {
        var mine = VesselsOf(sim, pawn);
        return mine.Count == 0 ? Entity.Null : AiQueries.FindNearestItem(sim, pawn, (_, def) => def.WashesInto is { } clean && mine.Contains(clean), 80);
    }

    protected override IEnumerable<JobStatus> Run()
    {
        if (!Reserve(tub))
            yield break;
        for (var trip = 0; trip < 4; trip++)
        {
            var dirty = Dirty(Sim, Pawn, tub);
            if (dirty.IsNull || !Reserve(dirty))
                break;
            Report = "Clearing the tables";
            foreach (var s in GoTo(Get<Thing>(dirty).Cell, PathEnd.OnTile)) yield return s;
            if (HasFailed) yield break;
            if (!Sim.IsAvailable(dirty))
                continue;
            Inventory.Stow(Sim, Pawn, Sim.TakeFromStack(dirty, Get<Stack>(dirty).Count));
        }
        if (BarWork.DirtyOn(Sim, Pawn).IsNull)
        {
            Fail("nothing to wash");
            yield break;
        }
        Report = "Washing up";
        foreach (var s in GoTo(Get<Thing>(tub).Cell, PathEnd.Touch)) yield return s;
        if (HasFailed) yield break;
        var clean = new List<Entity>();
        while (BarWork.DirtyOn(Sim, Pawn) is { IsNull: false } dirty)
        {
            var count = Get<Stack>(dirty).Count;
            foreach (var s in Wait(TicksEach * count)) yield return s;
            if (!Alive(dirty) || !Inventory.OnPerson(Sim, Pawn, dirty))
                break;
            var into = Sim.Defs.Thing(Get<Thing>(dirty).Def.WashesInto!);
            Destroy(Inventory.TakeFromPerson(Sim, Pawn, dirty, count));
            clean.Add(Sim.CreateItem(into, count));
        }
        // Back on the shelf, clean.
        foreach (var item in clean)
        {
            var shelf = ShelfFor(Sim, Pawn, tub, Get<Thing>(item).Def);
            if (shelf.IsNull)
            {
                Sim.PlaceItemNear(item, Get<Position>(Pawn).Cell);
                continue;
            }
            Get<Carrying>(Pawn).Item = item;
            Report = "Putting the crockery away";
            foreach (var s in GoTo(Get<Thing>(shelf).Cell, PathEnd.Touch)) yield return s;
            Get<Carrying>(Pawn).Item = Entity.Null;
            if (HasFailed || !Alive(shelf))
            {
                Sim.PlaceItemNear(item, Get<Position>(Pawn).Cell);
                yield break;
            }
            if (Sim.Store(shelf, item) is { IsNull: false } over)
                Sim.PlaceItemNear(over, Get<Position>(Pawn).Cell);
        }
        SkillRules.Learn(Sim, Pawn, Skill.Cooking, 5f);
    }

    private void Destroy(Entity e) => Sim.World.Destroy(e);

    /// <summary>The shelf in the inn that already keeps this kind (else the nearest with room).</summary>
    public static Entity ShelfFor(Simulation sim, Entity pawn, Entity near, ThingDef def)
    {
        var w = sim.World;
        if (Buildings.Of(sim, near) is not { IsNull: false } inn)
            return Entity.Null;
        var shelves = Buildings.Parts(sim, inn).Where(e => w.Get<Thing>(e).Def.Name is "Shelf" or "BarShelf" && !w.Has<Construction>(e)).ToList();
        return shelves.FirstOrDefault(s => sim.StoredIn(s).Any(i => w.Get<Thing>(i).Def == def) && sim.HasRoomFor(s, def, null), Entity.Null)
            is { IsNull: false } keeps ? keeps : shelves.FirstOrDefault(s => sim.HasRoomFor(s, def, null), Entity.Null);
    }
}

/// <summary>An AI innkeeper changes the keg when the cask runs dry: fetches a full one from the storeroom and knocks it in.</summary>
public sealed class TapJob(Entity cask, Entity keg) : Job
{
    /// <summary>Ticks it takes to knock a keg in.</summary>
    public const int TapTicks = 180;

    public static Job? For(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        if (!w.TryGet<Trade>(pawn, out var trade) || !trade.Def.HasWorkplace || !trade.Def.Serving(ServiceKind.Drink))
            return null;
        var inn = ShopSystem.WorkplaceOf(sim, trade.Def);
        if (inn.IsNull)
            return null;
        foreach (var cask in Buildings.Parts(sim, inn))
        {
            if (w.Get<Thing>(cask).Def.Building?.Cask is not { } props || w.Has<Construction>(cask) || PourSession.ServingsLeft(sim, cask) > 0
                || sim.Reservations.IsReservedByOther(cask, pawn))
                continue;
            var keg = AiQueries.FindNearestItem(sim, pawn, (_, d) => d.Name == props.Keg, 80);
            if (!keg.IsNull)
                return new TapJob(cask, keg);
        }
        return null;
    }

    protected override IEnumerable<JobStatus> Run()
    {
        if (!Reserve(cask) || !Reserve(keg))
            yield break;
        Report = "Fetching a keg";
        foreach (var s in GoTo(Get<Thing>(keg).Cell, PathEnd.OnTile)) yield return s;
        if (HasFailed) yield break;
        if (!Sim.IsAvailable(keg))
        {
            Fail("the keg's gone");
            yield break;
        }
        if (!Get<Carrying>(Pawn).Item.IsNull)
        {
            var held = Get<Carrying>(Pawn).Item;
            Get<Carrying>(Pawn).Item = Entity.Null;
            Inventory.Stow(Sim, Pawn, held);
        }
        var carried = Sim.TakeFromStack(keg, 1);
        Get<Carrying>(Pawn).Item = carried;
        Report = "Tapping a keg";
        foreach (var s in GoTo(Get<Thing>(cask).Cell, PathEnd.Touch)) yield return s;
        if (HasFailed) yield break;
        foreach (var s in Wait(TapTicks)) yield return s;
        if (!Alive(carried) || Get<Carrying>(Pawn).Item != carried)
        {
            Fail("dropped the keg");
            yield break;
        }
        // A practised hand seats the tap in a blow or two; a clumsy one sprays a few servings about.
        var level = SkillRules.LevelOf(Sim, Pawn, Skill.Cooking);
        var misses = Sim.Rand.NextFloat() < Math.Max(0.1f, 0.6f - 0.05f * level) ? 1 : 0;
        BarWork.Refill(Sim, cask, carried, misses * TapSession.SprayPerMiss);
        SkillRules.Learn(Sim, Pawn, Skill.Cooking, 15f);
    }
}

/// <summary>
/// The carter's morning round: each day of a round, kegs for the inn's storeroom and a sack of food
/// for its larder, so the bar and the pot can run all season if they're kept at. What isn't drunk
/// or cooked piles up.
/// </summary>
public sealed class InnSupplySystem : ISimSystem, Saving.ISaveState
{
    /// <summary>The hour the carter calls.</summary>
    public const float DeliveryHour = 6;

    public static readonly (string Def, int Count)[] Kegs = [("Keg", 2), ("Spirits", 4), ("Honey", 4), ("Herbs", 4), ("Berries", 6)];
    /// <summary>Wood for the kitchen's fires a day, into its log basket: a day's burning and a little over.</summary>
    public static readonly (string Def, int Count)[] Logs = [("Wood", 16)];

    public static readonly (string Def, int Count)[] Food = [("Cabbage", 6), ("Meat", 5), ("Berries", 8), ("Emberroot", 3), ("Herbs", 3), ("Flour", 6), ("Oats", 4), ("PorkSide", 1), ("Fowl", 1), ("CoarseSalt", 2)];

    private int _lastDay;

    public string Name => "InnSupply";
    public int Interval => SimClock.TicksPerHour / 4;

    public void Update(Simulation sim)
    {
        if (!sim.Round.Live || sim.Clock.HourOfDay < DeliveryHour || sim.Clock.Day <= _lastDay)
            return;
        _lastDay = sim.Clock.Day;
        // The cottages' bread crocks: plain food at home, for anyone with no cook to feed them.
        if (!sim.Director.Active.Any(c => c.Kind == Defs.CrisisKind.Shortage) && !sim.Repairs.RoadCut)
            Systems.Homes.StockCrocks(sim);
        // The round's first morning is what it was stocked with.
        if (sim.Clock.Tick < SimClock.TicksPerHour * 6)
            return;
        // Kegs for the tavern, food and wood for the kitchen: one stop where they share a roof (Millbrook's inn).
        var inn = ShopSystem.BuildingFor(sim, Defs.TownRole.Tavern);
        var kitchen = ShopSystem.BuildingFor(sim, Defs.TownRole.Kitchen);
        var at = !inn.IsNull ? inn : kitchen;
        if (at.IsNull)
            return;
        var anchor = sim.World.Get<PresetBuilding>(at).Anchor;
        var where = inn.IsNull || kitchen.IsNull || inn == kitchen ? $"the {sim.World.Get<PresetBuilding>(at).Def.Label}"
            : $"the {sim.World.Get<PresetBuilding>(inn).Def.Label} and the {sim.World.Get<PresetBuilding>(kitchen).Def.Label}";
        if (sim.Director.Active.Any(c => c.Kind == Defs.CrisisKind.Shortage))
        {
            sim.Messages.Add(MessageKind.Warning, $"The carter didn't come: no kegs or food for {where} today.", anchor);
            return;
        }
        // With a span down the road's cut. Where the inn and the kitchen share a roof (Millbrook's,
        // over the river) that's the end of it. Where they stand apart the carter calls at the kitchen
        // first, if a cart can still get there, and at the inn only if it's still joined to the kitchen:
        // a lake town's cookhouse on the shore gets its food while its inn out at the landing waits.
        bool toInn, toKitchen;
        if (!sim.Repairs.RoadCut)
            (toInn, toKitchen) = (!inn.IsNull, !kitchen.IsNull);
        else if (inn.IsNull || kitchen.IsNull || inn == kitchen)
            (toInn, toKitchen) = (false, false);
        else
        {
            var kitchenAt = Inside(sim, kitchen);
            toKitchen = kitchenAt is { } k && Enumerable.Range(0, 4).Any(side => Combat.Raids.EdgeEntry(sim, side, k) != null);
            toInn = toKitchen && Inside(sim, inn) is { } i && sim.Regions.CanReach(kitchenAt!.Value, i, PathEnd.OnTile);
        }
        if (!toInn && !toKitchen)
        {
            sim.Messages.Add(MessageKind.Warning, $"The carter couldn't get through, the {Cut(sim)} down: no kegs or food for {where} today.", anchor);
            return;
        }
        if (toInn)
            Deliver(sim, inn, "Chest", Kegs);
        if (toKitchen)
        {
            Deliver(sim, kitchen, "Larder", Food);
            Deliver(sim, kitchen, "LogBasket", Logs);
        }
        if (toInn && (toKitchen || kitchen.IsNull))
            sim.Messages.Add(MessageKind.Info, $"The carter's been by {where}: kegs and bar stock for the storeroom, food for the larder and wood for the kitchen.", anchor);
        else
        {
            var reached = toInn ? inn : kitchen;
            var missed = toInn ? kitchen : inn;
            sim.Messages.Add(MessageKind.Warning,
                $"The carter's been by the {sim.World.Get<PresetBuilding>(reached).Def.Label}, but couldn't get to the {sim.World.Get<PresetBuilding>(missed).Def.Label}, the {Cut(sim)} down.",
                sim.World.Get<PresetBuilding>(missed).Anchor);
        }
    }

    /// <summary>Somewhere to stand inside a building, by its storeroom chest or larder (where the carter unloads); null if none.</summary>
    private static int3? Inside(Simulation sim, Entity building)
    {
        var w = sim.World;
        var box = Buildings.Parts(sim, building).FirstOrDefault(e => w.Get<Thing>(e).Def.Name is "Chest" or "Larder" && !w.Has<Construction>(e), Entity.Null);
        var at = box.IsNull ? w.Get<PresetBuilding>(building).Anchor : w.Get<Thing>(box).Cell;
        foreach (var c in sim.SpiralFrom(at, 3))
            if (sim.Map.InBounds(c) && sim.Map.Passable(c) && sim.Map.Standable(sim.Map.Idx(c)) && Buildings.At(sim, c) == building)
                return c;
        return null;
    }

    /// <summary>What's down, for the carter's message: "bridge", "causeway".</summary>
    private static string Cut(Simulation sim) => sim.Repairs.Breaches.Count > 0 ? sim.Repairs.Breaches[0].Label : "road";

    /// <summary>Into the first of the inn's containers of a kind, else on the floor beside it.</summary>
    public static void Deliver(Simulation sim, Entity inn, string into, (string Def, int Count)[] goods)
    {
        var w = sim.World;
        var box = Buildings.Parts(sim, inn).FirstOrDefault(e => w.Get<Thing>(e).Def.Name == into && !w.Has<Construction>(e), Entity.Null);
        var at = box.IsNull ? w.Get<PresetBuilding>(inn).Anchor : w.Get<Thing>(box).Cell;
        foreach (var (def, count) in goods)
        {
            var thing = sim.Defs.Thing(def);
            for (var left = count; left > 0;)
            {
                var n = Math.Min(left, thing.StackLimit);
                left -= n;
                var item = sim.CreateItem(thing, n);
                var over = box.IsNull ? item : sim.Store(box, item);
                if (!over.IsNull)
                    sim.PlaceItemNear(over, at);
            }
        }
    }

    public void Save(Saving.SaveWriter w, Simulation sim) => w.Int(_lastDay);

    public void Load(Saving.SaveReader r, Simulation sim) => _lastDay = r.Int();
}

/// <summary>An AI innkeeper cleans the inn: mops up whatever's on its floors, a spot at a time.</summary>
public sealed class CleanJob(int3 cell) : Job
{
    public override string? ToolKind => "Mop";
    public override int3? FocusCell => cell;

    /// <summary>Ticks to clear a whole spot, by hand; a mop halves it.</summary>
    public const int TicksPerSpot = 240;

    public static Job? For(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        if (!w.TryGet<Trade>(pawn, out var trade) || !trade.Def.HasWorkplace)
            return null;
        var inn = ShopSystem.WorkplaceOf(sim, trade.Def);
        if (inn.IsNull || sim.Mess.All.Count == 0)
            return null;
        var anchor = w.Get<PresetBuilding>(inn).Anchor;
        var from = w.Get<Position>(pawn).Cell;
        int3? best = null;
        var bestDist = int.MaxValue;
        foreach (var i in sim.Mess.All.Keys)
        {
            var c = sim.Map.CellOf(i);
            // In the inn, by the plan's reach from its middle: the keeper's floors, not the whole town's.
            if (Map.Distance(c, anchor) > InnReach || sim.Reservations.IsTileReservedByOther(c, pawn) || !Mine(sim, trade.Def, inn, c))
                continue;
            var d = Map.Distance(from, c);
            if (d < bestDist && Navigation.CanReach(sim, pawn, from, c, PathEnd.Touch))
                (best, bestDist) = (c, d);
        }
        return best is { } at ? new CleanJob(at) : null;
    }

    /// <summary>How far from the inn's middle its floors reach.</summary>
    public const int InnReach = 9;

    /// <summary>
    /// Whose floor a cell is: the kitchen's (the room with the pot) the cook's, the rest the
    /// keeper's. A trade that sees to everything there cleans it all.
    /// </summary>
    public static bool Mine(Simulation sim, TradeDef trade, Entity inn, int3 cell)
    {
        bool meals = trade.Serving(ServiceKind.Meal), rest = trade.Serving(ServiceKind.Drink) || trade.Serving(ServiceKind.Lodging);
        if (meals && rest)
            return true;
        var pot = Cooking.PotIn(sim, inn);
        var kitchen = pot.IsNull ? null : sim.Rooms.RoomAt(sim.World.Get<Thing>(pot).Cell);
        var inKitchen = kitchen != null && sim.Rooms.RoomAt(cell) == kitchen;
        return inKitchen ? meals : rest;
    }

    protected override IEnumerable<JobStatus> Run()
    {
        if (!ReserveTile(cell))
            yield break;
        Report = "Cleaning the floor";
        foreach (var s in GoTo(cell, PathEnd.Touch)) yield return s;
        if (HasFailed) yield break;
        if (Sim.Mess.At(Sim, cell) is not { } mess)
            yield break;
        var mop = !Toolbelt.Best(Sim, Pawn, "Mop").IsNull;
        foreach (var s in Wait((int)(TicksPerSpot * mess.Amount * (mop ? 0.5f : 1f)))) yield return s;
        Sim.Mess.Clean(Sim, cell, 1f);
    }
}

/// <summary>
/// An inn job that goes into a guest room: fetches the room's key first if its door's locked (off
/// the board, else the spare from the storeroom), and puts it back after: on the board if the
/// board's short of it, else back with the spares.
/// </summary>
public abstract class RoomKeyJob : Job
{
    private Entity _key = Entity.Null;

    /// <summary>The innkeeper's inn, by their trade's workplace.</summary>
    protected Entity Inn => Pawn.IsNull || !Sim.World.TryGet<Trade>(Pawn, out var t) || !t.Def.HasWorkplace ? Entity.Null : ShopSystem.WorkplaceOf(Sim, t.Def);

    /// <summary>Have a key to this bed's room on them, if its door's locked. Fails the job if there's none to be had.</summary>
    protected IEnumerable<JobStatus> KeyFor(Entity bed)
    {
        if (!Hospitality.InnRooms.LockedIn(Sim, bed) || !Hospitality.InnRooms.KeyOn(Sim, Pawn, bed).IsNull)
            yield break;
        var seed = Hospitality.InnRooms.SeedOf(Sim, bed);
        var from = Hospitality.InnRooms.OnBoard(Sim, Inn, seed);
        if (from.IsNull)
            from = Hospitality.InnRooms.KeyIn(Sim, Hospitality.InnRooms.SpareBox(Sim, Inn), seed);
        if (from.IsNull || !Sim.World.TryGet<Stored>(from, out var st))
        {
            Fail("no key to the room");
            yield break;
        }
        var box = st.In;
        foreach (var s in GoTo(Get<Thing>(box).Cell, PathEnd.Touch)) yield return s;
        if (HasFailed)
            yield break;
        if (!Alive(from) || !Sim.World.TryGet<Stored>(from, out var still) || still.In != box)
        {
            Fail("the key's gone");
            yield break;
        }
        _key = Sim.TakeFromStack(from, 1);
        Inventory.Stow(Sim, Pawn, _key);
    }

    /// <summary>The key fetched for the room, back where it's wanted.</summary>
    protected IEnumerable<JobStatus> PutKeyBack()
    {
        if (_key.IsNull || !Alive(_key) || !Inventory.OnPerson(Sim, Pawn, _key))
            yield break;
        var seed = Sim.World.Get<Tinkering.Key>(_key).Seed;
        var board = Hospitality.InnRooms.Board(Sim, Inn);
        var to = !board.IsNull && Hospitality.InnRooms.KeyIn(Sim, board, seed).IsNull ? board : Hospitality.InnRooms.SpareBox(Sim, Inn);
        if (to.IsNull)
            to = board;
        if (to.IsNull)
            yield break;
        foreach (var s in GoTo(Get<Thing>(to).Cell, PathEnd.Touch)) yield return s;
        if (HasFailed || !Alive(_key) || !Inventory.OnPerson(Sim, Pawn, _key))
            yield break;
        var rest = Sim.Store(to, Inventory.TakeFromPerson(Sim, Pawn, _key, 1));
        if (!rest.IsNull)
            Inventory.Stow(Sim, Pawn, rest);
        _key = Entity.Null;
    }
}

/// <summary>
/// An AI innkeeper turns a room round once its guest has gone (with the room's key, if it's
/// locked): clean linen from the inn's stores, the bed stripped and made, the pot emptied and the
/// jug filled (without the games or a trip to the well: as the empath's AI works the gift), and
/// anything left behind picked up for the lost property box. The soiled sheets are left for the wash.
/// </summary>
public sealed class MakeBedJob(Entity bed) : RoomKeyJob
{
    public override Entity Focus => bed;

    public const int Ticks = 150, ChoreTicks = 90;

    public static Job? For(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        if (!w.TryGet<Trade>(pawn, out var trade) || !trade.Def.HasWorkplace || !trade.Def.Serving(ServiceKind.Lodging))
            return null;
        var inn = ShopSystem.WorkplaceOf(sim, trade.Def);
        if (inn.IsNull || (w.Count<Hospitality.Unmade>() == 0 && w.Count<Hospitality.Slops>() == 0 && w.Count<Hospitality.JugEmpty>() == 0))
            return null;
        var from = w.Get<Position>(pawn).Cell;
        foreach (var part in Buildings.Parts(sim, inn))
            if (w.Get<Thing>(part).Def.Building is { IsBed: true } && !Hospitality.RoomChores.Ready(sim, part) && !Hospitality.Lodging.Lodged(sim, Occupant(sim, part))
                && !sim.Reservations.IsReservedByOther(part, pawn) && Gettable(sim, pawn, inn, part)
                && (!w.Has<Hospitality.Unmade>(part) || !Linen(sim, pawn, inn).IsNull)
                && (Hospitality.InnRooms.LockedIn(sim, part) || Navigation.CanReach(sim, pawn, from, w.Get<Thing>(part).Cell, PathEnd.Touch)))
                return new MakeBedJob(part);
        return null;
    }

    /// <summary>Whoever's been given this bed, if anyone.</summary>
    private static Entity Occupant(Simulation sim, Entity bed)
    {
        foreach (var o in sim.World.Each<BedOwner>())
            if (sim.World.Get<BedOwner>(o).Bed == bed)
                return o;
        return Entity.Null;
    }

    /// <summary>Clean linen on them, or kept in the inn, or Entity.Null.</summary>
    private static Entity Linen(Simulation sim, Entity pawn, Entity inn)
    {
        if (Hospitality.RoomChores.LinenOn(sim, pawn) is { IsNull: false } on)
            return on;
        foreach (var part in Buildings.Parts(sim, inn))
            if (sim.IsContainer(part))
                foreach (var item in sim.StoredIn(part))
                    if (sim.World.Get<Thing>(item).Def.Name == Hospitality.RoomChores.LinenDef)
                        return item;
        return Entity.Null;
    }

    /// <summary>Not let to anyone tonight, and if its room's locked, there's a key to be had for it.</summary>
    private static bool Gettable(Simulation sim, Entity pawn, Entity inn, Entity bed)
    {
        if (!Hospitality.InnRooms.LockedIn(sim, bed) || !Hospitality.InnRooms.KeyOn(sim, pawn, bed).IsNull)
            return true;
        var seed = Hospitality.InnRooms.SeedOf(sim, bed);
        return !Hospitality.InnRooms.OnBoard(sim, inn, seed).IsNull || !Hospitality.InnRooms.KeyIn(sim, Hospitality.InnRooms.SpareBox(sim, inn), seed).IsNull;
    }

    protected override IEnumerable<JobStatus> Run()
    {
        if (!Reserve(bed))
            yield break;
        var w = Sim.World;
        Report = "Turning a room round";
        // Clean linen first, from wherever the inn keeps it.
        if (w.Has<Hospitality.Unmade>(bed) && Hospitality.RoomChores.LinenOn(Sim, Pawn).IsNull)
        {
            var linen = Linen(Sim, Pawn, Inn);
            if (linen.IsNull || !w.TryGet<Stored>(linen, out var st))
            {
                Fail("no clean linen");
                yield break;
            }
            foreach (var s in GoTo(Get<Thing>(st.In).Cell, PathEnd.Touch)) yield return s;
            if (HasFailed) yield break;
            if (!Alive(linen) || !w.Has<Stored>(linen))
            {
                Fail("the linen's gone");
                yield break;
            }
            Inventory.Stow(Sim, Pawn, Sim.TakeFromStack(linen, 1));
        }
        foreach (var s in KeyFor(bed)) yield return s;
        if (HasFailed) yield break;
        foreach (var s in GoTo(Get<Thing>(bed).Cell, PathEnd.Touch)) yield return s;
        if (HasFailed) yield break;
        if (w.Has<Hospitality.Unmade>(bed) && !Hospitality.RoomChores.LinenOn(Sim, Pawn).IsNull)
        {
            foreach (var s in Wait(Ticks)) yield return s;
            if (!Alive(bed))
                yield break;
            Hospitality.MakeBedSession.MakeUp(Sim, bed, Pawn);
        }
        foreach (var fixture in new[] { Hospitality.RoomChores.FixtureOf(Sim, bed, Hospitality.RoomChores.PotDef), Hospitality.RoomChores.FixtureOf(Sim, bed, Hospitality.RoomChores.StandDef) })
        {
            if (fixture.IsNull || !(w.Has<Hospitality.Slops>(fixture) || w.Has<Hospitality.JugEmpty>(fixture)))
                continue;
            foreach (var s in GoTo(Get<Thing>(fixture).Cell, PathEnd.Touch)) yield return s;
            if (HasFailed) yield break;
            foreach (var s in Wait(ChoreTicks)) yield return s;
            if (!Alive(fixture))
                continue;
            w.Remove<Hospitality.Slops>(fixture);
            w.Remove<Hospitality.JugEmpty>(fixture);
        }
        // Anything left on the floor, to the lost property box.
        var room = Sim.Rooms.RoomAt(Get<Thing>(bed).Cell);
        var found = new List<Entity>();
        foreach (var item in w.Query<Hospitality.LeftBy>().OrderBy(e => e.Index))
            if (Get<Thing>(item).Spawned && Sim.Rooms.RoomAt(Get<Thing>(item).Cell) == room)
                found.Add(item);
        foreach (var item in found)
        {
            foreach (var s in GoTo(Get<Thing>(item).Cell, PathEnd.Touch)) yield return s;
            if (HasFailed) yield break;
            if (Alive(item) && Get<Thing>(item).Spawned)
                Inventory.Stow(Sim, Pawn, Sim.TakeFromStack(item, Get<Stack>(item).Count));
        }
        foreach (var s in PutKeyBack()) yield return s;
        foreach (var s in PutAwayLeftThings()) yield return s;
        // The sheets down to the wash tub, to be washed with the crockery (WashJob).
        if (Hospitality.RoomChores.SoiledOn(Sim, Pawn) is { IsNull: false } soiled
            && Buildings.Parts(Sim, Inn).FirstOrDefault(e => BarWork.IsTub(Sim, e), Entity.Null) is { IsNull: false } tub)
        {
            foreach (var s in GoTo(Get<Thing>(tub).Cell, PathEnd.Touch)) yield return s;
            if (!HasFailed && Alive(soiled) && Inventory.OnPerson(Sim, Pawn, soiled))
                Sim.PlaceItemNear(Inventory.TakeFromPerson(Sim, Pawn, soiled, Get<Stack>(soiled).Count), PawnCell, byHand: true);
        }
    }

    /// <summary>What guests left, off to the lost property box.</summary>
    private IEnumerable<JobStatus> PutAwayLeftThings()
    {
        var w = Sim.World;
        var box = Buildings.Parts(Sim, Inn).FirstOrDefault(e => w.Get<Thing>(e).Def.Name == Hospitality.RoomChores.LostPropertyDef, Entity.Null);
        var carried = new List<Entity>();
        if (w.Get<Carrying>(Pawn).Item is { IsNull: false } held && w.Has<Hospitality.LeftBy>(held))
            carried.Add(held);
        if (Sim.PackOf(Pawn) is { IsNull: false } pack)
            carried.AddRange(Sim.StoredIn(pack).Where(e => w.Has<Hospitality.LeftBy>(e)));
        if (box.IsNull || carried.Count == 0)
            yield break;
        foreach (var s in GoTo(Get<Thing>(box).Cell, PathEnd.Touch)) yield return s;
        if (HasFailed) yield break;
        foreach (var item in carried)
            if (Alive(item) && Inventory.OnPerson(Sim, Pawn, item))
            {
                var rest = Sim.Store(box, Inventory.TakeFromPerson(Sim, Pawn, item, Get<Stack>(item).Count));
                if (!rest.IsNull)
                    Sim.PlaceItemNear(rest, Get<Thing>(box).Cell, byHand: true);
            }
    }
}

/// <summary>
/// An AI innkeeper hangs room keys back on the board: one handed in on the counter, one left in a
/// room, or a guest's key they're still carrying from a letting that fell through.
/// </summary>
public sealed class HangKeyJob(Entity key, Entity board) : Job
{
    public override Entity Focus => key;

    public static Job? For(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        if (!w.TryGet<Trade>(pawn, out var trade) || !trade.Def.HasWorkplace || !trade.Def.Serving(ServiceKind.Lodging))
            return null;
        var inn = ShopSystem.WorkplaceOf(sim, trade.Def);
        var board = Hospitality.InnRooms.Board(sim, inn);
        if (board.IsNull)
            return null;
        var from = w.Get<Position>(pawn).Cell;
        var seen = new HashSet<uint>();
        foreach (var k in w.Query<Tinkering.Key>().OrderBy(e => e.Index))
        {
            if (!Hospitality.InnRooms.IsRoomKey(sim, k))
                continue;
            var seed = w.Get<Tinkering.Key>(k).Seed;
            if (Inventory.OnPerson(sim, pawn, k))
            {
                // Left on them from a job that fell through: where it belongs.
                if (seen.Add(seed))
                    return new HangKeyJob(k, Hospitality.InnRooms.OnBoard(sim, inn, seed).IsNull ? board
                        : Hospitality.InnRooms.SpareBox(sim, inn) is { IsNull: false } box ? box : board);
                continue;
            }
            // Loose in the inn: on the counter, or on a room's floor upstairs.
            if (!w.Get<Thing>(k).Spawned || sim.Reservations.IsReservedByOther(k, pawn)
                || Map.Distance(w.Get<Thing>(k).Cell, w.Get<PresetBuilding>(inn).Anchor) > CleanJob.InnReach)
                continue;
            if (Navigation.CanReach(sim, pawn, from, w.Get<Thing>(k).Cell, PathEnd.Touch))
                return new HangKeyJob(k, Hospitality.InnRooms.OnBoard(sim, inn, seed).IsNull ? board
                    : Hospitality.InnRooms.SpareBox(sim, inn) is { IsNull: false } box ? box : board);
        }
        return null;
    }

    protected override IEnumerable<JobStatus> Run()
    {
        var w = Sim.World;
        Report = Sim.World.Get<Thing>(board).Def.Name == Hospitality.InnRooms.BoardDef ? "Hanging a key on the board" : "Putting a spare key away";
        if (!Inventory.OnPerson(Sim, Pawn, key))
        {
            if (!Reserve(key))
                yield break;
            foreach (var s in GoTo(Get<Thing>(key).Cell, PathEnd.Touch)) yield return s;
            if (HasFailed || !Alive(key) || !w.Get<Thing>(key).Spawned)
                yield break;
            Inventory.Stow(Sim, Pawn, Sim.TakeFromStack(key, 1));
        }
        foreach (var s in GoTo(Get<Thing>(board).Cell, PathEnd.Touch)) yield return s;
        if (HasFailed || !Alive(key) || !Inventory.OnPerson(Sim, Pawn, key))
            yield break;
        var rest = Sim.Store(board, Inventory.TakeFromPerson(Sim, Pawn, key, 1));
        if (!rest.IsNull)
            Inventory.Stow(Sim, Pawn, rest);
    }
}

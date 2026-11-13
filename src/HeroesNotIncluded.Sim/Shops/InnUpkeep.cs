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
        if (!w.TryGet<Trade>(pawn, out var trade) || trade.Def.WorkplaceDef is not { } workplace)
            return null;
        var inn = ShopSystem.BuildingOf(sim, workplace);
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
        if (!w.TryGet<Trade>(pawn, out var trade) || trade.Def.WorkplaceDef is not { } workplace)
            return kinds;
        foreach (var part in Buildings.Parts(sim, ShopSystem.BuildingOf(sim, workplace)))
        {
            var b = w.Get<Thing>(part).Def.Building;
            if (b?.Cask is { } cask && trade.Def.Serving(ServiceKind.Drink))
                kinds.Add(cask.Into);
            if (b?.StewPot is { } pot && trade.Def.Serving(ServiceKind.Meal))
                kinds.Add(pot.Into);
        }
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
        var shelves = Buildings.Parts(sim, inn).Where(e => w.Get<Thing>(e).Def.Name == "Shelf" && !w.Has<Construction>(e)).ToList();
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
        if (!w.TryGet<Trade>(pawn, out var trade) || trade.Def.WorkplaceDef is not { } workplace || !trade.Def.Serving(ServiceKind.Drink))
            return null;
        var inn = ShopSystem.BuildingOf(sim, workplace);
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
    public static readonly (string Def, int Count)[] Food = [("Cabbage", 6), ("Meat", 5), ("Berries", 8)];

    private int _lastDay;

    public string Name => "InnSupply";
    public int Interval => SimClock.TicksPerHour / 4;

    public void Update(Simulation sim)
    {
        if (!sim.Round.Live || sim.Clock.HourOfDay < DeliveryHour || sim.Clock.Day <= _lastDay)
            return;
        _lastDay = sim.Clock.Day;
        // The round's first morning is what it was stocked with.
        if (sim.Clock.Tick < SimClock.TicksPerHour * 6)
            return;
        var inn = ShopSystem.BuildingOf(sim, sim.Defs.Building("Inn"));
        if (inn.IsNull)
            return;
        if (sim.Director.Active.Any(c => c.Kind == Defs.CrisisKind.Shortage))
        {
            sim.Messages.Add(MessageKind.Warning, "The carter didn't come: no kegs or food for the inn today.", sim.World.Get<PresetBuilding>(inn).Anchor);
            return;
        }
        if (sim.Repairs.RoadCut)
        {
            sim.Messages.Add(MessageKind.Warning, "The carter couldn't get over the river: no kegs or food for the inn today.", sim.World.Get<PresetBuilding>(inn).Anchor);
            return;
        }
        Deliver(sim, inn, "Chest", Kegs);
        Deliver(sim, inn, "Larder", Food);
        sim.Messages.Add(MessageKind.Info, "The carter's been by the inn: kegs and bar stock for the storeroom, and a sack of food for the larder.",
            sim.World.Get<PresetBuilding>(inn).Anchor);
    }

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
        if (!w.TryGet<Trade>(pawn, out var trade) || trade.Def.WorkplaceDef is not { } workplace)
            return null;
        var inn = ShopSystem.BuildingOf(sim, workplace);
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

/// <summary>An AI innkeeper makes up the beds guests have left.</summary>
public sealed class MakeBedJob(Entity bed) : Job
{
    public override Entity Focus => bed;

    public const int Ticks = 150;

    public static Job? For(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        if (!w.TryGet<Trade>(pawn, out var trade) || trade.Def.WorkplaceDef is not { } workplace || !trade.Def.Serving(ServiceKind.Lodging))
            return null;
        var inn = ShopSystem.BuildingOf(sim, workplace);
        if (inn.IsNull || w.Count<Hospitality.Unmade>() == 0)
            return null;
        var from = w.Get<Position>(pawn).Cell;
        foreach (var part in Buildings.Parts(sim, inn))
            if (w.Has<Hospitality.Unmade>(part) && !sim.Reservations.IsReservedByOther(part, pawn)
                && Navigation.CanReach(sim, pawn, from, w.Get<Thing>(part).Cell, PathEnd.Touch))
                return new MakeBedJob(part);
        return null;
    }

    protected override IEnumerable<JobStatus> Run()
    {
        if (!Reserve(bed))
            yield break;
        Report = "Making up a bed";
        foreach (var s in GoTo(Get<Thing>(bed).Cell, PathEnd.Touch)) yield return s;
        if (HasFailed) yield break;
        foreach (var s in Wait(Ticks)) yield return s;
        if (Alive(bed))
            Sim.World.Remove<Hospitality.Unmade>(bed);
    }
}

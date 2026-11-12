using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Maps;
using SupportingCast.Sim.Round;
using SupportingCast.Sim.Systems;
using SupportingCast.Sim.Work;

namespace SupportingCast.Sim.Shops;

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
        var tub = Buildings.Parts(sim, inn).FirstOrDefault(e => BarWork.IsTub(sim, e));
        if (tub.IsNull || sim.Reservations.IsReservedByOther(tub, pawn) || Dirty(sim, pawn, tub).IsNull)
            return null;
        return new WashJob(tub);
    }

    /// <summary>The nearest dirty tankard or bowl, wherever the customer left it (by the shrine, as often as not).</summary>
    private static Entity Dirty(Simulation sim, Entity pawn, Entity tub) =>
        AiQueries.FindNearestItem(sim, pawn, (_, def) => Vessels.IsDirty(def), 80);

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
        return shelves.FirstOrDefault(s => sim.StoredIn(s).Any(i => w.Get<Thing>(i).Def == def) && sim.HasRoomFor(s, def, null))
            is { IsNull: false } keeps ? keeps : shelves.FirstOrDefault(s => sim.HasRoomFor(s, def, null));
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
        if (!w.TryGet<Trade>(pawn, out var trade) || trade.Def.WorkplaceDef is not { } workplace)
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

    public static readonly (string Def, int Count)[] Kegs = [("Keg", 2)];
    public static readonly (string Def, int Count)[] Food = [("Cabbage", 6), ("Meat", 5), ("Berries", 8)];

    private int _lastDay;

    public string Name => "InnSupply";
    public int Interval => SimClock.TicksPerHour / 4;

    public void Update(Simulation sim)
    {
        if (sim.Round.Phase != RoundPhase.Playing || sim.Clock.HourOfDay < DeliveryHour || sim.Clock.Day <= _lastDay)
            return;
        _lastDay = sim.Clock.Day;
        // The round's first morning is what it was stocked with.
        if (sim.Clock.Tick < SimClock.TicksPerHour * 6)
            return;
        var inn = ShopSystem.BuildingOf(sim, sim.Defs.Building("Inn"));
        if (inn.IsNull)
            return;
        Deliver(sim, inn, "Chest", Kegs);
        Deliver(sim, inn, "Larder", Food);
        sim.Messages.Add(MessageKind.Info, "The carter's been by the inn: kegs for the storeroom and a sack of food for the larder.",
            sim.World.Get<PresetBuilding>(inn).Anchor);
    }

    /// <summary>Into the first of the inn's containers of a kind, else on the floor beside it.</summary>
    public static void Deliver(Simulation sim, Entity inn, string into, (string Def, int Count)[] goods)
    {
        var w = sim.World;
        var box = Buildings.Parts(sim, inn).FirstOrDefault(e => w.Get<Thing>(e).Def.Name == into && !w.Has<Construction>(e));
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

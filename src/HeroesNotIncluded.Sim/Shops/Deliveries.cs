using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Shops;

/// <summary>
/// The carter from the hill mines: every morning of a round after the first, at
/// <see cref="DeliveryHour"/>, leaves what <see cref="Defs.SeasonDef.OreDelivery"/> lists (ore to
/// smelt, and charcoal) in the smithy's chest, and beside it what won't fit. The smith starts a
/// round with only a couple of bars, so the morning's ore is what keeps the forge busy before the
/// first adventurers come in. The woodcutter comes with it, with logs for the barn
/// (<see cref="DeliverWood"/>). Nothing happens outside a round.
/// </summary>
public sealed class OreCarterSystem : ISimSystem, ISaveState
{
    public const int DeliveryHour = 6;
    public const string SmithyDef = "Smithy";

    public string Name => "OreCarter";
    public int Interval => SimClock.TicksPerHour / 4;

    private int _lastDay = -1;

    public void Update(Simulation sim)
    {
        if (!sim.Round.Live || sim.Clock.Day == _lastDay)
            return;
        // The round's first day is stocked by the deal (VillageRound.StockSmithy): the carter comes from the next.
        if (_lastDay < 0)
        {
            _lastDay = sim.Clock.Day;
            return;
        }
        if (sim.Clock.HourOfDay < DeliveryHour)
            return;
        _lastDay = sim.Clock.Day;
        Deliver(sim);
    }

    /// <summary>
    /// What the players' trades bring the town themselves (<see cref="Defs.TradeDef.Supplies"/>): a player who's
    /// the miner digs the ore the carter would bring. Their characters count connected or not (one who's left
    /// is an AI villager in the same trade, still at it).
    /// </summary>
    public static HashSet<string> SuppliedByPlayers(Simulation sim)
    {
        var w = sim.World;
        var supplied = new HashSet<string>();
        foreach (var e in w.Query<PlayerCharacter>())
            if (w.TryGet<Trade>(e, out var t))
                supplied.UnionWith(t.Def.Supplies);
        return supplied;
    }

    /// <summary>How much of a delivery's thing comes: all of it, or the season's share of what a player supplies.</summary>
    public static int Delivered(Simulation sim, Defs.ThingCount load, HashSet<string> supplied) =>
        supplied.Contains(load.ThingDef.Name) ? (int)MathF.Round(load.Count * sim.Defs.Season.SuppliedShare) : load.Count;

    /// <summary>Leave the morning's load at each smithy (there's one in Millbrook).</summary>
    public static void Deliver(Simulation sim)
    {
        var load = sim.Defs.Season.OreDelivery;
        if (load.Count == 0)
            return;
        var w = sim.World;
        var supplied = SuppliedByPlayers(sim);
        foreach (var smithy in w.Query<PresetBuilding>().Where(e => w.Get<PresetBuilding>(e).Def.Name == SmithyDef).OrderBy(e => e.Index))
        {
            var chest = Buildings.Parts(sim, smithy).Where(e => w.Get<Thing>(e).Def.Name == "Chest")
                .OrderBy(e => e.Index).DefaultIfEmpty(Entity.Null).First();
            if (chest.IsNull)
                continue;
            var cell = w.Get<Thing>(chest).Cell;
            // With the road cut, a smithy the carter can't reach from the road waits (a lake town's, over the causeway).
            if (sim.Repairs.RoadCut && sim.SpiralFrom(cell, 2).FirstOrDefault(c => sim.Map.InBounds(c) && sim.Map.Passable(c), new int3(-1, -1, -1)) is var by
                && by.X >= 0 && !Enumerable.Range(0, 4).Any(side => Combat.Raids.EdgeEntry(sim, side, by) != null))
            {
                sim.Messages.Add(MessageKind.Warning, $"The carter couldn't get the ore through to the smithy, the {sim.Repairs.Breaches[0].Label} down.", cell);
                continue;
            }
            foreach (var t in load)
            {
                var count = Delivered(sim, t, supplied);
                if (count <= 0)
                    continue;
                var item = sim.CreateItem(t.ThingDef, count, t.MaterialDef);
                var rest = sim.Store(chest, item);
                if (!rest.IsNull)
                    sim.PlaceItemNear(rest, cell);
            }
            sim.Messages.Add(MessageKind.Info, "The carter's left a load of ore at the smithy.", cell);
        }
        DeliverWood(sim);
        DeliverToWoodshop(sim);
    }

    /// <summary>The carter's load for the woodshop (<see cref="Defs.SeasonDef.WoodshopDelivery"/>): feathers, which nobody in town grows.</summary>
    public static void DeliverToWoodshop(Simulation sim)
    {
        var load = sim.Defs.Season.WoodshopDelivery;
        var chest = Systems.Stock.ChestOf(sim, ShopSystem.BuildingFor(sim, Defs.TownRole.Woodshop));
        if (load.Count == 0 || chest.IsNull)
            return;
        var cell = sim.World.Get<Thing>(chest).Cell;
        foreach (var t in load)
        {
            var rest = sim.Store(chest, sim.CreateItem(t.ThingDef, t.Count, t.MaterialDef));
            if (!rest.IsNull)
                sim.PlaceItemNear(rest, cell);
        }
        sim.Messages.Add(MessageKind.Info, "The carter's left a bundle of feathers at the woodshop.", cell);
    }

    /// <summary>
    /// The woodcutter's morning load (<see cref="Defs.SeasonDef.WoodDelivery"/>, timber) and the
    /// carter's firewood (<see cref="Defs.SeasonDef.FirewoodDelivery"/>), stacked in the barn's
    /// stockpile. In a shortage there's no firewood to be had: the carpenter's axe is the town's.
    /// </summary>
    public static void DeliverWood(Simulation sim)
    {
        var season = sim.Defs.Season;
        if (sim.Map.ZonedCells.Count == 0)
            return;
        var at = Scenarios.VillageRound.StockpileMiddle(sim);
        if (season.WoodDelivery.Count > 0)
        {
            foreach (var t in season.WoodDelivery)
                sim.PlaceItemNear(sim.CreateItem(t.ThingDef, t.Count, t.MaterialDef), at);
            sim.Messages.Add(MessageKind.Info, "The woodcutter's stacked a load of timber in the barn.", at);
        }
        if (season.FirewoodDelivery.Count == 0)
            return;
        if (sim.Director.Active.Any(c => c.Kind == Defs.CrisisKind.Shortage))
        {
            sim.Messages.Add(MessageKind.Warning, "No firewood came this morning: there's none to be had in the shortage.", at);
            return;
        }
        foreach (var t in season.FirewoodDelivery)
            sim.PlaceItemNear(sim.CreateItem(t.ThingDef, t.Count, t.MaterialDef), at);
        sim.Messages.Add(MessageKind.Info, "The carter's stacked a load of firewood in the barn.", at);
    }

    public void Save(SaveWriter w, Simulation sim) => w.Int(_lastDay);

    public void Load(SaveReader r, Simulation sim) => _lastDay = r.Int();
}

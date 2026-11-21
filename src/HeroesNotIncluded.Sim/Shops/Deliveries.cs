using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Shops;

/// <summary>
/// The carter's and the woodcutter's morning for the town itself: every morning of a round after the
/// first, at <see cref="DeliveryHour"/>, timber and firewood stacked in the barn's stockpile
/// (<see cref="DeliverWood"/>), the town's common store. What the shops need comes to order
/// (<see cref="CarterOrders"/>). Nothing happens outside a round.
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

    /// <summary>The morning's load for the barn.</summary>
    public static void Deliver(Simulation sim) => DeliverWood(sim);

    /// <summary>With the road cut, a place the carter can't reach from the road waits (a lake town's, over the causeway).</summary>
    public static bool CutOff(Simulation sim, int3 cell) =>
        sim.Repairs.RoadCut && sim.SpiralFrom(cell, 2).FirstOrDefault(c => sim.Map.InBounds(c) && sim.Map.Passable(c), new int3(-1, -1, -1)) is var by
        && by.X >= 0 && !Enumerable.Range(0, 4).Any(side => Combat.Raids.EdgeEntry(sim, side, by) != null);

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

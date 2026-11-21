using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Shops;

namespace HeroesNotIncluded.Sim.Bots;

/// <summary>
/// Ordering from the carter, as a player keeping their shop would: once an evening, what the trade's
/// standing orders say it keeps in (<see cref="Defs.TradeDef.StandingOrders"/>), sent through the shop
/// window's command and paid from its own purse. A careless bot misjudges it (now short, now over), as a
/// player can; a skilled steady one orders to need. Nothing's sent for it by the town while it plays.
/// </summary>
public sealed partial class BotPlayer
{
    private int _orderedDay = -1;

    /// <summary>The evening's order, once a day from the standing-order hour on. Never takes the bot's turn.</summary>
    private void OrderSupplies(Simulation sim, Entity me)
    {
        var w = sim.World;
        var day = sim.Clock.Day;
        if (day == _orderedDay || sim.Clock.HourOfDay < CarterOrders.StandingHour - 1 || !sim.Round.Live
            || !w.TryGet<Trade>(me, out var trade) || trade.Def.StandingOrders.Count == 0)
            return;
        _orderedDay = day;
        var building = ShopSystem.WorkplaceOf(sim, trade.Def);
        if (building.IsNull)
            return;
        var orders = CarterOrders.Of(sim);
        var silver = Coin.Count(sim, me);
        foreach (var standing in trade.Def.StandingOrders)
        {
            var supply = standing.SupplyDef;
            // Its judgement of what's wanted: off by up to half either way as care and skill fall short.
            var off = (1f - Temper.Care) / Math.Max(Skill, 0.5f) * 0.5f;
            var keep = (int)MathF.Round(standing.Keep * (1f + (_rand.NextFloat() * 2f - 1f) * off));
            var lacking = keep - CarterOrders.InStock(sim, building, supply) - orders.Ordered(building, supply) * supply.Per;
            if (lacking <= 0)
                continue;
            var lots = Math.Min((lacking + supply.Per - 1) / supply.Per, CarterOrders.MostLots(sim, supply) - orders.Ordered(building, supply));
            var price = CarterOrders.Price(sim, supply);
            lots = Math.Min(lots, price == 0 ? lots : silver / price);
            if (lots <= 0 || orders.Refusal(sim, me, supply, lots) != null)
                continue;
            silver -= lots * price;
            Send(new OrderSuppliesCommand(me, supply.Name, lots));
        }
    }
}

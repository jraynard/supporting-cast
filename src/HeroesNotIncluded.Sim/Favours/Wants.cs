using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Favours;

/// <summary>
/// The town's own errands (<see cref="Defs.TradeDef.Wants"/>): each hour of a round, a trade held
/// by the AI that's running short of something a labourer could fetch (logs for the hearth,
/// berries for the pot) pins an errand for it, paid from its own purse. Without these the board
/// stays empty unless a player posts, and the labourer has nothing to do but haul. Players keep
/// their own shelves: a player's trade posts nothing on their behalf.
/// </summary>
public static class Wants
{
    /// <summary>Every AI tradesperson short of something, an errand for it (one open at a time for each thing).</summary>
    public static void Post(Simulation sim)
    {
        var w = sim.World;
        if (FavourBoard.AnyBoard(sim).IsNull)
            return;
        foreach (var e in w.Query<Trade>())
        {
            var trade = w.Get<Trade>(e).Def;
            if (trade.Wants.Count == 0 || !trade.HasWorkplace || Players.Steered(sim, e) || w.Get<Creature>(e) is not { Dead: false, Downed: false }
                || w.Has<Adventurer>(e))
                continue;
            var shop = Shops.ShopSystem.WorkplaceOf(sim, trade);
            if (shop.IsNull)
                continue;
            foreach (var want in trade.Wants)
            {
                if (sim.Favours.All.Any(f => f.IsOpen && f.Poster == e && f.Wants == want.ThingDef) || Stock(sim, shop, want.ThingDef) >= want.Below
                    || Shops.Coin.Count(sim, e) < want.Price)
                    continue;
                sim.Favours.SendErrand(sim, e, want.ThingDef, want.Count, want.Price, (int)MathF.Ceiling(want.Hours));
            }
        }
    }

    /// <summary>How many of a thing are kept at a building: in its chests and baskets, or lying inside it.</summary>
    public static int Stock(Simulation sim, Entity building, Defs.ThingDef def)
    {
        var w = sim.World;
        var n = 0;
        foreach (var part in Buildings.Parts(sim, building))
            if (w.Get<Thing>(part).Def.Building?.StorageStacks > 0)
                foreach (var item in sim.StoredIn(part))
                    if (w.Get<Thing>(item).Def == def)
                        n += w.Get<Stack>(item).Count;
        foreach (var item in sim.AvailableItems())
            if (w.Get<Thing>(item).Def == def && w.Get<Thing>(item).Spawned && Buildings.At(sim, w.Get<Thing>(item).Cell) == building)
                n += w.Get<Stack>(item).Count;
        return n;
    }
}

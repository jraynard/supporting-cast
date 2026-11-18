using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Shops;

/// <summary>
/// An adventurer buys a piece off a wares shelf themselves: walks over to it, looks it over (still
/// on show, still at the price they'd pay, still the silver for it), takes it down and pays its
/// seller (<see cref="ShopSystem.BoughtOffShelf"/>). Nobody has to be there to serve them: a
/// keeper sells by stocking the shelf. They set out from <see cref="ShopSystem.BrowsingFor"/>.
/// </summary>
public sealed class ShelfBuyJob(Entity item, int price, int worth) : Job
{
    public override Entity Focus => item;

    protected override IEnumerable<JobStatus> Run()
    {
        var shops = Sim.Shops;
        var label = Alive(item) ? Sim.LabelOf(item) : "piece";
        if (Goods.ShownIn(Sim, item).IsNull || !Reserve(item))
        {
            shops.DoneBrowsing(Pawn);
            Fail("it's gone from the shelf");
            yield break;
        }
        Report = $"Going to look at the {label} on the shelf";
        foreach (var s in GoTo(Get<Thing>(item).Cell, PathEnd.Touch)) yield return s;
        shops.DoneBrowsing(Pawn);
        if (HasFailed) yield break;
        if (!Alive(item) || Goods.ShownIn(Sim, item).IsNull || !Sim.World.TryGet<Stored>(item, out var shelf)
            || !Inventory.CanReachContainer(Sim, Pawn, shelf.In))
        {
            Fail("it's gone from the shelf");
            yield break;
        }
        // The price may have changed while they walked over: dearer than they'd agreed, or than they have, they leave it.
        var now = Get<ForSale>(item).Price;
        if (now > price || now > Coin.Count(Sim, Pawn))
        {
            Barks.Say(Sim, Pawn, "adv_shelf_dearer", Volume.Say, "{n} silver now? Never mind.", n: now);
            Fail("dearer than they'd pay");
            yield break;
        }
        shops.BoughtOffShelf(Sim, Pawn, item, now, worth);
    }
}

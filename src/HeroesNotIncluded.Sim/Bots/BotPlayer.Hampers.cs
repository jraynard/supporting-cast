using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Hospitality;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Bots;

/// <summary>
/// The inn, supplied from the kitchen (<see cref="Provisions"/>): as innkeeper the bot sends for
/// hampers from the bar, takes them off the hatch and unpacks
/// them, and serves lodgers their board off a lake town's warming shelf; as cook it ties rations and
/// (where the inn's apart) ladles bowls, packs them and puts them up on the hatch.
/// </summary>
public sealed partial class BotPlayer
{
    /// <summary>The inn's keeper with the kitchen's hamper in their arms: to the inn's shelf with it, and unpack it.</summary>
    private bool CarryHamper(Simulation sim, Entity me, long now)
    {
        if (!Provisions.KeepsInn(sim, me) || !Provisions.IsHamper(sim, sim.World.Get<Carrying>(me).Item)
            || Provisions.UnpackAt(sim) is not { IsNull: false } shelf)
            return false;
        Doing = "carrying the hamper back to the inn";
        return AtStation(sim, me, shelf, now, s => Send(new BarWorkCommand(me, s)));
    }

    /// <summary>
    /// The inn's keeper, its shelves running low: sends to the kitchen for a hamper from the bar,
    /// then, once it's up, over to the hatch to take it.
    /// </summary>
    private bool SendForHamper(Simulation sim, Entity me, long now)
    {
        var w = sim.World;
        if (!Provisions.KeepsInn(sim, me))
            return false;
        if (Provisions.Ordered(sim, me) is { } order)
        {
            if (!order.Assignee.IsNull || !Provisions.HamperUp(sim))
                return false; // being packed: back to the bar meanwhile
            var hatch = Hatch.Of(sim, Provisions.Cookhouse(sim));
            if (hatch.IsNull)
                return false;
            Doing = "fetching the hamper from the kitchen";
            if (!w.Get<Carrying>(me).Item.IsNull)
            {
                Send(new StowCommand(me));
                return true;
            }
            return AtStation(sim, me, hatch, now, at =>
            {
                Send(new AvatarWorkCommand(me, true, w.Get<Thing>(at).Cell));
                Send(new AvatarWorkCommand(me, false));
            });
        }
        if (!Provisions.WantsSupplies(sim) || ShopSystem.SmallHours(sim) || Provisions.Refusal(sim, me) != null)
            return false;
        var bar = Buildings.Parts(sim, Provisions.Inn(sim)).OrderBy(e => e.Index)
            .FirstOrDefault(p => w.Get<Thing>(p).Def.Building is { Bar: true }, Entity.Null);
        if (bar.IsNull)
            return false;
        Doing = "sending to the kitchen for a hamper";
        return AtStation(sim, me, bar, now, _ => Send(new OrderProvisionsCommand(me)));
    }

    /// <summary>A lodger's board: a dish off the warming shelf, handed across to them.</summary>
    private bool Board(Simulation sim, Entity me, PerceptionView view, Entity lodger, long now)
    {
        var w = sim.World;
        var dish = BarService.DishOn(sim, me);
        if (!dish.IsNull)
        {
            Doing = "serving a lodger's board";
            if (Reach(sim, me, lodger, now))
                Send(new GiveCommand(me, lodger, dish, 1));
            return true;
        }
        if (Provisions.Shelf(sim) is not { IsNull: false } shelf || Provisions.DishFor(sim, shelf, lodger) is not { IsNull: false } on)
            return false; // the shelf's bare: nothing to give them
        Doing = "taking a lodger's board off the shelf";
        if (!w.Get<Carrying>(me).Item.IsNull)
        {
            Send(new StowCommand(me));
            return true;
        }
        return AtStation(sim, me, shelf, now, _ => Send(new TakeFromStorageCommand(me, on, 1)));
    }

    /// <summary>The pot as the cook sees it (or last saw it): cooked, with bowls in it to ladle.</summary>
    private bool Ladleable(Simulation sim, Entity me, PerceptionView view, Entity pot) =>
        !pot.IsNull && (view.Pot(pot) ?? (_pot is { } remembered && remembered.Pot == pot ? remembered : null)) is { Cooked: true, Empty: false, Bowls: > 0 };

    /// <summary>
    /// Something to do for the inn's hamper now: it's in hand to put up, there are dishes on the cook,
    /// or the pot's cooked to ladle from. Otherwise the order waits, and the kitchen's own work comes first.
    /// </summary>
    private bool HamperInReach(Simulation sim, Entity me, PerceptionView view)
    {
        var w = sim.World;
        return Provisions.IsHamper(sim, w.Get<Carrying>(me).Item) || Provisions.DishesOn(sim, me).Count > 0
            || (Provisions.Split(sim) && Ladleable(sim, me, view, Nearest(sim, me, "StewPot")));
    }

    /// <summary>
    /// The cook, a hamper on order from the inn: bowls ladled from the pot (a clean bowl for each),
    /// packed at the prep table, and put up on the hatch. False once it's up (the keeper collects it),
    /// or when the pot has nothing to give (its meals come first).
    /// </summary>
    private bool PackHamper(Simulation sim, Entity me, PerceptionView view, long now)
    {
        var w = sim.World;
        var shop = MyShop(sim, me) ?? Entity.Null;
        var hatch = Hatch.Of(sim, shop);
        if (hatch.IsNull || Provisions.HamperUp(sim))
            return false;
        var held = w.Get<Carrying>(me).Item;
        if (Provisions.IsHamper(sim, held))
        {
            Doing = "putting the inn's hamper up on the hatch";
            return AtStation(sim, me, hatch, now, at =>
            {
                Send(new AvatarWorkCommand(me, true, w.Get<Thing>(at).Cell));
                Send(new AvatarWorkCommand(me, false));
            });
        }
        var dishes = Provisions.DishesOn(sim, me).Sum(d => w.Get<Stack>(d).Count);
        var pot = Nearest(sim, me, "StewPot");
        var ladling = Provisions.Split(sim) && Ladleable(sim, me, view, pot) && dishes < Provisions.Holds; // dishes for the board, where the inn's apart
        if (!ladling)
        {
            if (dishes == 0)
                return false;
            Doing = "packing a hamper for the inn";
            var table = Buildings.Parts(sim, shop).FirstOrDefault(p => Rations.IsPrepTable(sim, p), Entity.Null);
            return AtStation(sim, me, table, now, t => Send(new PrepCommand(me, t, PrepStep.Hamper)));
        }
        var bowl = Cooking.BowlOf(sim, pot);
        if (Carried(sim, me, bowl).IsNull && !Vessels.Shelved(sim, pot, bowl)) // the kitchen shelf's are to hand at the pot
            return Crockery(sim, me, view, now, bowl, pot);
        if (!held.IsNull && w.Get<Thing>(held).Def.Name != bowl)
        {
            Send(new StowCommand(me)); // the last bowl into the pack: a free hand for the ladle
            return true;
        }
        Doing = "ladling bowls for the inn's hamper";
        return AtStation(sim, me, pot, now, at => Send(new PotCommand(me, at, PotStep.Ladle)));
    }
}

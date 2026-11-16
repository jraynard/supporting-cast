using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Expeditions;
using HeroesNotIncluded.Sim.Healing;
using HeroesNotIncluded.Sim.Tinkering;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Bots;

public sealed partial class BotPlayer
{
    /// <summary>
    /// A meal from the pot: ladle a bowl and hand it over; with the pot not ready, cook it (fill it
    /// from the larder, tend it till it's done, season it by taste) first.
    /// </summary>
    private bool Serve(Simulation sim, Entity me, PerceptionView view, Entity diner, long now)
    {
        var w = sim.World;
        var held = w.Get<Carrying>(me).Item;
        // Ordered at the hatch: put it up there (a dish waiting already, and it's the diner's to take).
        var shop = MyShop(sim, me) ?? Entity.Null;
        var atHatch = view.Orders().Any(o => o.Status == OrderStatus.Open && o.Poster == diner && o.Kind == ServiceKind.Meal)
            && sim.World.TryGet<PresetBuilding>(shop, out var building) && building.Def.HatchServes.Contains(ServiceKind.Meal);
        var hatch = atHatch ? Hatch.Of(sim, shop) : Entity.Null;
        // As many up as it knows are waiting for one: no more till one's taken.
        if (!hatch.IsNull && view.InContainer(hatch).Where(d => w.Get<Thing>(d).Def.Cooked).Sum(d => w.Get<Stack>(d).Count)
            >= view.Orders().Count(o => o.Status == OrderStatus.Open && o.Kind == ServiceKind.Meal))
        {
            Doing = "waiting on the hatch";
            return true;
        }
        if (w.IsAlive(held) && w.Get<Thing>(held).Def.Cooked)
        {
            if (!hatch.IsNull)
            {
                Doing = "putting a dish up on the hatch";
                return AtStation(sim, me, hatch, now, at =>
                {
                    Send(new AvatarWorkCommand(me, true, w.Get<Thing>(at).Cell));
                    Send(new AvatarWorkCommand(me, false));
                });
            }
            if (Reach(sim, me, diner, now))
                Send(new GiveCommand(me, diner, held, 1));
            return true;
        }
        var pot = Nearest(sim, me, "StewPot");
        if (pot.IsNull)
            return false;
        if (view.Pot(pot) is { } seen)
            _pot = seen;
        if (_pot is not { } p || p.Pot != pot)
            return AtStation(sim, me, pot, now, _ => { });
        if (AvatarSystem.Supply(sim, me, d => Cooking.Takes(sim, pot, d)) is { IsNull: false } food && (p.Empty || !p.Cooked))
        {
            Doing = "filling the pot";
            _potSeasoned = false;
            return AtStation(sim, me, pot, now, at => Send(new PotCommand(me, at, PotStep.Add)));
        }
        if (p.Empty || (p.Bowls == 0 && p.Cooked))
        {
            Doing = "fetching food for the pot";
            _potSeasoned = false;
            return AtStation(sim, me, Nearest(sim, me, "Larder"), now, larder =>
            {
                var stores = view.InContainer(larder).ToList();
                if (stores.FirstOrDefault(e => Cooking.IsIngredient(w.Get<Thing>(e).Def) && !w.Get<Thing>(e).Def.IsSeasoner, Entity.Null) is { IsNull: false } stack)
                    Send(new TakeFromStorageCommand(me, stack, 8)); // an armful for the pot
                // And something to give it a taste, once it's got food in.
                if (stores.FirstOrDefault(e => w.Get<Thing>(e).Def.IsSeasoner, Entity.Null) is { IsNull: false } seasoner
                    && AvatarSystem.Supply(sim, me, d => d.IsSeasoner).IsNull)
                    Send(new TakeFromStorageCommand(me, seasoner, 1));
            });
        }
        if (!p.Cooked)
        {
            Doing = "cooking a stew";
            return AtStation(sim, me, pot, now, at => Send(new PotCommand(me, at, PotStep.Tend)));
        }
        if (!_potSeasoned)
        {
            Doing = "seasoning the stew";
            return AtStation(sim, me, pot, now, at => Send(new PotCommand(me, at, PotStep.Season)));
        }
        var bowl = Cooking.BowlOf(sim, pot);
        if (Carried(sim, me, bowl).IsNull)
            return Crockery(sim, me, view, now, bowl, pot);
        if (!held.IsNull && w.Get<Thing>(held).Def.Name != bowl)
        {
            Send(new StowCommand(me)); // a free hand for the ladle
            return true;
        }
        Doing = "ladling a bowl";
        return AtStation(sim, me, pot, now, at => Send(new PotCommand(me, at, PotStep.Ladle)));
    }
}

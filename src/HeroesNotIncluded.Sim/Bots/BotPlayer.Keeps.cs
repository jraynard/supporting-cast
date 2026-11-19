using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Bots;

public sealed partial class BotPlayer
{
    /// <summary>
    /// What its trade keeps in (<see cref="Defs.TradeDef.Keeps"/>: the smith's charcoal), made as a player would while its
    /// workplace's stores run short: the station's fire fed if it's out, the makings fetched, the craft begun and seen
    /// through, and what's made put away in the store it's kept in. By what's in its own shop's stores. Only once
    /// it's down to half what it keeps when <paramref name="urgent"/> (before its other work), else whenever it's short. True while at it.
    /// </summary>
    private bool KeepIn(Simulation sim, Entity me, PerceptionView view, long now, bool urgent)
    {
        var w = sim.World;
        if (!w.TryGet<Trade>(me, out var trade) || trade.Def.Keeps.Count == 0 || MyShop(sim, me) is not { IsNull: false } shop)
            return false;
        if (w.Get<Mind>(me).Job is Ai.CraftJob)
        {
            // A player's craft goes on only while the work key's held, as at a tree.
            Doing = "making something it keeps in";
            if (!w.Get<Avatar>(me).WorkHeld)
                Send(new AvatarWorkCommand(me, true));
            _holdingCraft = true;
            return true;
        }
        if (_holdingCraft)
        {
            _holdingCraft = false;
            Send(new AvatarWorkCommand(me, false));
        }
        var stores = Buildings.Parts(sim, shop).Where(e => w.Get<Thing>(e).Def.Building?.StorageStacks > 0).OrderBy(e => e.Index).ToList();
        foreach (var keep in trade.Def.Keeps)
        {
            var recipe = keep.RecipeDef;
            var product = recipe.Products[0].ThingDef;
            var station = Nearest(sim, me, recipe.Station);
            if (station.IsNull || !Crafting.CanMake(sim, me, recipe))
                continue;
            // What's been made goes where it's kept: the store already holding some, else one with room for it.
            // Its own workplace's stores are common knowledge to it, as a keeper knows their shelves (as Fetch).
            var kept = stores.Sum(s => sim.StoredIn(s).Where(i => w.Get<Thing>(i).Def == product).Sum(i => w.Get<Stack>(i).Count));
            var mine = Carried(sim, me, product);
            var store = mine.IsNull ? Entity.Null
                : stores.Where(s => sim.ContainerRoom(s, product, w.Get<Thing>(mine).Material) > 0)
                    .OrderBy(s => sim.StoredIn(s).Any(i => w.Get<Thing>(i).Def == product) ? 0 : 1).FirstOrDefault(Entity.Null);
            if (!store.IsNull && kept < keep.Below)
            {
                Doing = $"putting {product.Label} away";
                return AtStation(sim, me, store, now, s =>
                {
                    var pack = sim.PackOf(me);
                    if (w.Get<Carrying>(me).Item is { IsNull: false } held && w.Get<Thing>(held).Def == product)
                    {
                        Send(new AvatarWorkCommand(me, true, w.Get<Thing>(s).Cell));
                        Send(new AvatarWorkCommand(me, false));
                    }
                    else if (!pack.IsNull && sim.StoredIn(pack).FirstOrDefault(i => w.Get<Thing>(i).Def == product, Entity.Null) is { IsNull: false } made)
                        Send(new StoreFromPackCommand(me, s, made));
                });
            }
            if (kept + OnMe(sim, me, product.Name) >= (urgent ? keep.Below / 2 : keep.Below))
                continue;
            // An on-demand fire (the kiln) fed before the makings go in, or the craft waits on a cold one.
            if (w.Get<Thing>(station).Def.Building?.Fuel is { } fuel && view.FuelIn(station) is <= 0f)
            {
                bool IsFuel(Entity e) => w.IsAlive(e) && fuel.Takes(w.Get<Thing>(e).Def);
                if (IsFuel(w.Get<Carrying>(me).Item))
                {
                    Doing = $"feeding the {w.Get<Thing>(station).Label}";
                    return AtStation(sim, me, station, now, f =>
                    {
                        Send(new AvatarWorkCommand(me, true, w.Get<Thing>(f).Cell));
                        Send(new AvatarWorkCommand(me, false));
                    });
                }
                if (Known(sim, me, view, sim.Defs.Thing(fuel.Def)) is { IsNull: false } logs)
                {
                    Doing = $"fetching {sim.Defs.Thing(fuel.Def).Label} for the {w.Get<Thing>(station).Label}";
                    if (!w.Get<Carrying>(me).Item.IsNull && !IsFuel(w.Get<Carrying>(me).Item))
                    {
                        Send(new StowCommand(me));
                        return true;
                    }
                    return Fetch(sim, me, view, logs, FuelAtOnce, now);
                }
                Need(sim, me, sim.Defs.Thing(fuel.Def).Label, now);
                continue;
            }
            var bench = w.Get<Workbench>(station);
            var staged = bench.Active == recipe ? bench.Staged : [];
            var short_ = false;
            for (var i = 0; i < recipe.Ingredients.Count && !short_; i++)
            {
                var x = recipe.Ingredients[i];
                var need = x.Count - OnMe(sim, me, x.Def) - (i < staged.Length ? staged[i] : 0);
                if (need <= 0)
                    continue;
                if (Known(sim, me, view, x.ThingDef) is not { IsNull: false } stack)
                {
                    Need(sim, me, x.ThingDef.Label, now);
                    short_ = true;
                    continue;
                }
                Doing = $"fetching {x.ThingDef.Label} for {recipe.Label}";
                return Fetch(sim, me, view, stack, need, now);
            }
            if (short_)
                continue;
            Doing = $"making {recipe.Label}";
            return AtStation(sim, me, station, now, s => Send(new CraftCommand(me, s, recipe)));
        }
        return false;
    }

    private bool _holdingCraft;

    /// <summary>A stack of a thing in its hands or pack, Entity.Null if none.</summary>
    private static Entity Carried(Simulation sim, Entity me, Defs.ThingDef def)
    {
        var w = sim.World;
        if (w.Get<Carrying>(me).Item is { IsNull: false } held && w.Get<Thing>(held).Def == def)
            return held;
        return sim.PackOf(me) is { IsNull: false } pack ? sim.StoredIn(pack).FirstOrDefault(i => w.Get<Thing>(i).Def == def, Entity.Null) : Entity.Null;
    }

    /// <summary>Fuel it fetches at once for a station's fire.</summary>
    public const int FuelAtOnce = 10;
}

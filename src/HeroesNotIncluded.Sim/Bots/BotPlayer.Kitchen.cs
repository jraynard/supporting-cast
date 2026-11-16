using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Bots;

public sealed partial class BotPlayer
{
    /// <summary>
    /// How careful a cook must be to sniff a pot before the first bowl goes out of it. Below this
    /// it doesn't bother, and a pot that's turned goes out to the diners (a slip, counted).
    /// </summary>
    public const float SniffFrom = 0.5f;

    // This pot's been sniffed (or the sniff skipped), and pots that smelt not quite right: those aren't served from.
    private bool _potSniffed;
    private readonly HashSet<Entity> _sourPots = [];

    /// <summary>What's up on the hatch, by dish, as it last saw it.</summary>
    private readonly Dictionary<ThingDef, int> _hatchUp = [];

    /// <summary>What it fetched from the larder for the pot, by def (and what it chops into).</summary>
    private readonly HashSet<string> _forPot = [];

    /// <summary>
    /// A meal: a dish asked for by name (<see cref="ServeDish"/>), else a bowl from the pot, ladled
    /// and put up on the hatch; with the pot not ready, cook it (fill it from the larder, chopped,
    /// tend it till it's done, season it by taste, and sniff it) first. With nothing in the larder
    /// for a pot, a dish made ahead goes up instead.
    /// </summary>
    private bool Serve(Simulation sim, Entity me, PerceptionView view, Entity diner, long now)
    {
        var w = sim.World;
        if (LidAFlare(sim, me, view, now) || DoughReady(sim, me, view, now))
            return true;
        var shop = MyShop(sim, me) ?? Entity.Null;
        // What's still wanted at the hatch, by what will do for it: this order's own first; if what
        // it wants is up already, another that isn't; all of them up, it waits on the diners.
        var unmet = Unmet(sim, me, view, shop);
        var own = view.WantedMeal(_task) is { Dish: { } named } && named.Menu?.MadeAt != "StewPot" ? named : null;
        if (own != null && unmet.GetValueOrDefault(own) > 0)
            return ServeDish(sim, me, view, diner, own, now);
        if ((own != null || unmet.GetValueOrDefault(PlainMeal) <= 0) && unmet.FirstOrDefault(u => u.Key != PlainMeal && u.Value > 0).Key is { } other)
            return ServeDish(sim, me, view, diner, other, now);
        if (unmet.GetValueOrDefault(PlainMeal) <= 0)
        {
            Doing = "waiting on the hatch";
            return AtStation(sim, me, Hatch.Of(sim, shop), now, _ => { }); // by it, to see when one's taken
        }
        var held = w.Get<Carrying>(me).Item;
        // Ordered at the hatch: put it up there (a dish waiting already, and it's the diner's to take).
        var atHatch = view.Orders().Any(o => o.Status == OrderStatus.Open && o.Poster == diner && o.Kind == ServiceKind.Meal)
            && sim.World.TryGet<PresetBuilding>(shop, out var building) && building.Def.HatchServes.Contains(ServiceKind.Meal);
        var hatch = atHatch ? Hatch.Of(sim, shop) : Entity.Null;
        var pot = KitchenPot(sim, me);
        if (w.IsAlive(held) && w.Get<Thing>(held).Def.Cooked)
        {
            if (!pot.IsNull)
                NameTheHouseDish(sim, me, view, shop, pot, held, now);
            return PutUp(sim, me, diner, hatch, now);
        }
        if (pot.IsNull)
            return PutUpStock(sim, me, view, diner, hatch, now);
        if (view.Pot(pot) is { } seen)
            _pot = seen;
        if (_pot is not { } p || p.Pot != pot)
            return AtStation(sim, me, pot, now, _ => { });
        if (!p.Lit && FireBy(sim, pot) is { IsNull: false } fire)
            return FeedFire(sim, me, view, fire, now);
        // Only what it fetched for the pot goes in it: not the meat it's carrying for the spit, or a pie's filling.
        bool ForThePot(ThingDef d) => Cooking.Takes(sim, pot, d) && (d.IsSeasoner || _forPot.Contains(d.Name));
        if (AvatarSystem.Supply(sim, me, ForThePot) is { IsNull: false } food && (p.Empty || !p.Cooked))
        {
            _potSeasoned = _potSniffed = false;
            // In hand: the work key (and the knife) take what's held before anything in the pack.
            if (held != food)
            {
                Send(new HoldCommand(me, food));
                return true;
            }
            // Chopped first, at the prep table: it cooks the quicker for it.
            if (w.Get<Thing>(food).Def.ChopsInto != null && Part(sim, me, "PrepTable") is { IsNull: false } table)
            {
                Doing = $"chopping the {w.Get<Thing>(food).Def.Label}";
                return AtStation(sim, me, table, now, t => Send(new PrepCommand(me, t, PrepStep.Chop)));
            }
            Doing = "filling the pot";
            return AtStation(sim, me, pot, now, at => Send(new PotCommand(me, at, PotStep.Add)));
        }
        if (p.Empty || (p.Bowls == 0 && p.Cooked))
        {
            // Nothing in the larder for a pot: a roast or a loaf made ahead goes up instead.
            if (!sim.StoredIn(Larder(sim, me)).Any(e => Cooking.IsIngredient(w.Get<Thing>(e).Def) && !w.Get<Thing>(e).Def.IsSeasoner))
                return PutUpStock(sim, me, view, diner, hatch, now);
            Doing = "fetching food for the pot";
            _potSeasoned = _potSniffed = false;
            return AtStation(sim, me, Larder(sim, me), now, larder =>
            {
                _forPot.Clear();
                var stores = view.InContainer(larder).ToList();
                void Take(Entity stack, int count)
                {
                    var def = w.Get<Thing>(stack).Def;
                    _forPot.Add(def.Name);
                    if (def.ChopsInto != null)
                        _forPot.Add(def.ChopsInto);
                    Send(new TakeFromStorageCommand(me, stack, count));
                }
                // Something green and some meat (a stew of one thing is flat), and a seasoner to give it a taste.
                bool Stewing(ThingDef d) => Cooking.IsIngredient(d) && !d.IsSeasoner && !d.Grain && d.Name != Baking.Flour && d.Joints == null && d.Plucks == null;
                var greens = stores.FirstOrDefault(e => Stewing(w.Get<Thing>(e).Def) && !w.Get<Thing>(e).Def.Roasts, Entity.Null);
                var meat = stores.FirstOrDefault(e => Stewing(w.Get<Thing>(e).Def) && w.Get<Thing>(e).Def.Roasts, Entity.Null);
                var any = stores.FirstOrDefault(e => Cooking.IsIngredient(w.Get<Thing>(e).Def) && !w.Get<Thing>(e).Def.IsSeasoner, Entity.Null);
                var seasoner = AvatarSystem.Supply(sim, me, d => d.IsSeasoner).IsNull ? stores.FirstOrDefault(e => w.Get<Thing>(e).Def.IsSeasoner, Entity.Null) : Entity.Null;
                // Chosen before any's taken: a stack taken whole is gone from the larder.
                if (!greens.IsNull)
                    Take(greens, meat.IsNull ? 8 : 5);
                if (!meat.IsNull)
                    Take(meat, greens.IsNull ? 8 : 3);
                if (greens.IsNull && meat.IsNull && !any.IsNull)
                    Take(any, 8); // whatever there is
                if (!seasoner.IsNull)
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
        if (!_potSniffed)
        {
            // A careful cook smells it before the first bowl goes out; a careless one doesn't think to.
            if (Temper.Care < SniffFrom)
            {
                _potSniffed = true;
                Stats.Mistakes++;
            }
            else
            {
                Doing = "smelling the stew";
                return AtStation(sim, me, pot, now, at =>
                {
                    _potSniffed = true;
                    Send(new SniffCommand(me, at));
                });
            }
        }
        if (_sniffed is { } sniff && sniff.Target == pot)
        {
            _sniffed = null;
            if (sniff.Smell == SniffSession.PotWord(1f))
            {
                // Something in it's turned: not a bowl of it goes out. The other pot, or what else there is.
                _sourPots.Add(pot);
                if (now >= _nextTalk)
                    Say(sim, me, "kitchen_sour", Volume.Say, now);
                return true;
            }
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

    /// <summary>Stands for the plain meal in <see cref="Unmet"/>: whatever's cooked will do.</summary>
    private static readonly ThingDef PlainMeal = new() { Name = "" };

    /// <summary>
    /// The open meal orders it knows of, less what's up on the hatch for them (as it last saw it):
    /// by the dish asked for by name, and <see cref="PlainMeal"/> for the rest (anything cooked does
    /// for those, once the named ones have theirs).
    /// </summary>
    private Dictionary<ThingDef, int> Unmet(Simulation sim, Entity me, PerceptionView view, Entity shop)
    {
        var w = sim.World;
        var wanted = new Dictionary<ThingDef, int>();
        var hatch = Hatch.Of(sim, shop);
        // What's up on it, as it last saw it: looked over whenever it's beside it.
        if (!hatch.IsNull && AvatarSystem.InReach(sim, w.Get<Position>(me).Cell, hatch))
        {
            _hatchUp.Clear();
            foreach (var item in view.InContainer(hatch))
                if (w.Get<Thing>(item).Def.Cooked)
                    _hatchUp[w.Get<Thing>(item).Def] = _hatchUp.GetValueOrDefault(w.Get<Thing>(item).Def) + w.Get<Stack>(item).Count;
        }
        foreach (var o in view.Orders().Where(o => o.Status == OrderStatus.Open && o.Kind == ServiceKind.Meal).OrderBy(o => o.Id))
        {
            var dish = view.WantedMeal(o.Id)?.Dish is { } d && d.Menu?.MadeAt != "StewPot" ? d : PlainMeal;
            wanted[dish] = wanted.GetValueOrDefault(dish) + 1;
        }
        var spare = 0;
        foreach (var (def, n) in _hatchUp.OrderBy(kv => kv.Key.Name, StringComparer.Ordinal))
        {
            var taken = Math.Min(n, wanted.GetValueOrDefault(def));
            if (taken > 0)
                wanted[def] -= taken;
            spare += n - taken;
        }
        if (wanted.ContainsKey(PlainMeal))
            wanted[PlainMeal] -= spare;
        return wanted;
    }

    /// <summary>
    /// A dish asked for by name (a roast, bread, a pie): one made already goes up on the hatch;
    /// else it's made (<see cref="MakeDish"/>). Nothing else will do for them.
    /// </summary>
    private bool ServeDish(Simulation sim, Entity me, PerceptionView view, Entity diner, ThingDef dish, long now)
    {
        var w = sim.World;
        var shop = MyShop(sim, me) ?? Entity.Null;
        var hatch = Hatch.Of(sim, shop);
        var held = w.Get<Carrying>(me).Item;
        if (w.IsAlive(held) && w.Get<Thing>(held).Def == dish)
            return PutUp(sim, me, diner, hatch, now);
        if (sim.PackOf(me) is { IsNull: false } pack && sim.StoredIn(pack).FirstOrDefault(e => w.Get<Thing>(e).Def == dish, Entity.Null) is { IsNull: false } made)
        {
            Send(held.IsNull ? new HoldCommand(me, made) : new StowCommand(me));
            return true;
        }
        return MakeDish(sim, me, view, dish, now);
    }

    /// <summary>A roast, a loaf or a pie it's carrying, to go up for a plain meal while the stew's not ready: Entity.Null if none.</summary>
    private static Entity Stock(Simulation sim, Entity me) =>
        AvatarSystem.Supply(sim, me, d => d.Cooked && d.Menu is { } m && m.MadeAt != "StewPot");

    /// <summary>A dish it has on it, into its hands and up on the hatch. False with none.</summary>
    private bool PutUpStock(Simulation sim, Entity me, PerceptionView view, Entity diner, Entity hatch, long now)
    {
        var w = sim.World;
        var dish = Stock(sim, me);
        if (dish.IsNull)
            return false;
        var held = w.Get<Carrying>(me).Item;
        if (held != dish)
        {
            Send(held.IsNull ? new HoldCommand(me, dish) : new StowCommand(me));
            return true;
        }
        return PutUp(sim, me, diner, hatch, now);
    }

    /// <summary>What's in hand up on the hatch (the work key there), or, with no hatch, handed to the diner.</summary>
    private bool PutUp(Simulation sim, Entity me, Entity diner, Entity hatch, long now)
    {
        var w = sim.World;
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
            Send(new GiveCommand(me, diner, w.Get<Carrying>(me).Item, 1));
        return true;
    }

    /// <summary>The kitchen's pot to cook in: the first that hasn't smelt off. Entity.Null if there's none.</summary>
    private Entity KitchenPot(Simulation sim, Entity me)
    {
        if (MyShop(sim, me) is not { IsNull: false } shop)
            return Nearest(sim, me, "StewPot");
        foreach (var part in Buildings.Parts(sim, shop))
            if (Cooking.IsPot(sim, part) && !_sourPots.Contains(part))
                return part;
        return Entity.Null;
    }

    /// <summary>A part of the cook's own workplace by def (its kitchen is common knowledge to it), else the nearest anywhere.</summary>
    private static Entity Part(Simulation sim, Entity me, string def)
    {
        var w = sim.World;
        if (MyShop(sim, me) is { IsNull: false } shop)
            foreach (var part in Buildings.Parts(sim, shop))
                if (w.Get<Thing>(part).Def.Name == def && !w.Has<Construction>(part))
                    return part;
        return Nearest(sim, me, def);
    }

    private static Entity Larder(Simulation sim, Entity me) => Part(sim, me, "Larder");

    /// <summary>
    /// A pot flaring in sight (flames over it show across the room): the lid clapped on, before
    /// it's a fire in the kitchen. True while it's at it.
    /// </summary>
    private bool LidAFlare(Simulation sim, Entity me, PerceptionView view, long now)
    {
        if (Flaring(sim, me, view) is not { IsNull: false } pot)
            return false;
        Doing = "clapping the lid on the pot";
        if (Alarm($"flare:{pot.Index}:{now / SimClock.TicksPerHour}"))
            Say(sim, me, "kitchen_flare", Volume.Shout, now);
        return AtStation(sim, me, pot, now, at => Send(new PotCommand(me, at, PotStep.Lid)));
    }

    /// <summary>A pot in its kitchen it can see flaring, or Entity.Null.</summary>
    private static Entity Flaring(Simulation sim, Entity me, PerceptionView view)
    {
        if (MyShop(sim, me) is not { IsNull: false } shop)
            return Entity.Null;
        foreach (var part in Buildings.Parts(sim, shop))
            if (Cooking.IsPot(sim, part) && view.KitchenLook(part) is { Flaring: true })
                return part;
        return Entity.Null;
    }

    /// <summary>Names for a house dish, one picked by its words.</summary>
    private static readonly string[] DishNames = ["Hearthside Stew", "Carter's Pot", "Long Road Stew", "Millstone Supper", "Ember Pot", "Wanderer's Bowl"];

    /// <summary>
    /// A bowl good enough to put a name to (fine or better, as it looks ladled), and no house dish
    /// on the sign yet: the pot it came from is named the house dish.
    /// </summary>
    private void NameTheHouseDish(Simulation sim, Entity me, PerceptionView view, Entity shop, Entity pot, Entity bowl, long now)
    {
        var w = sim.World;
        if (shop.IsNull || view.HouseDish(shop) != null || !w.TryGet<Crafted>(bowl, out var made) || made.Quality < Quality.Fine
            || w.Get<Thing>(bowl).Def != Cooking.Serves(sim, pot) || view.Pot(pot) is not { Cooked: true, Empty: false }
            || !AvatarSystem.InReach(sim, w.Get<Position>(me).Cell, pot) || !Alarm($"housedish:{now / SimClock.TicksPerDay}"))
            return;
        var name = DishNames[_talkRand.NextInt(DishNames.Length)];
        Send(new NameDishCommand(me, pot, name));
        if (now >= _nextTalk)
            Say(sim, me, "kitchen_house_dish", Volume.Say, now, thing: name);
    }

    /// <summary>The cook's games (a <see cref="Play"/> case for each, in BotPlayer.KitchenPlay); null for a session that isn't one.</summary>
    private (bool Press, bool Held, int Axis)? PlayKitchen(WorkSession s)
    {
        // Flames over a pot across the kitchen: whatever's in hand is put down for the lid (the stew's own flare is its own game's).
        if (s is ChopSession or KneadSession or CrimpSession or RoastSession or BakeSession or ButcherSession or PluckSession or PackSession
            && _view != null && world() is var sim && Players.CharacterOf(sim, player) is { IsNull: false } me && !Flaring(sim, me, _view).IsNull)
        {
            StopKitchenWork();
            return (false, false, 0);
        }
        return PlayKitchenGame(s);
    }

    private (bool Press, bool Held, int Axis)? PlayKitchenGame(WorkSession s) => s switch
    {
        ChopSession ch => Chop(ch),
        KneadSession kn => Knead(kn),
        CrimpSession cr => Crimp(cr),
        RoastSession ro => Turn(ro),
        BakeSession ba => Bake(ba),
        ButcherSession bu => Cleave(bu),
        PluckSession pl => Pluck(pl),
        PackSession pa => Pack(pa),
        SniffSession sn => Sniff(sn),
        _ => null,
    };

    /// <summary>
    /// The cook between orders (BotPlayer.KitchenWork): a flaring pot lidded; a rat chased off; a roast left on the
    /// spit or bread in the oven seen to; the kitchen's fires kept; its dishes on the sign; what the
    /// carter brought whole dressed; a roast, a loaf or a pie or two made ahead for the rush; rations
    /// packed for a party's road. True while at it.
    /// </summary>
    private bool Kitchen(Simulation sim, Entity me, PerceptionView view, long now)
    {
        if (!IsCook(sim, me))
            return false;
        return LidAFlare(sim, me, view, now) || ChaseRats(sim, me, view, now) || SeeToTheSpitAndOven(sim, me, view, now) || KeepTheFires(sim, me, view, now)
            || OfferTheMenu(sim, me, view) || DressWhole(sim, me, view, now) || MakeAhead(sim, me, view, now) || PackRations(sim, me, view, now);
    }

    /// <summary>It cooks at a kitchen of its own: its trade sees to meals (a trade with no list sees to all), and its workplace has a pot.</summary>
    private static bool IsCook(Simulation sim, Entity me) =>
        sim.World.TryGet<Trade>(me, out var trade) && trade.Def.HasWorkplace && trade.Def.Serving(ServiceKind.Meal)
        && MyShop(sim, me) is { IsNull: false } shop && !Cooking.PotIn(sim, shop).IsNull;
}

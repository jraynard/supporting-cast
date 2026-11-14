using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Shops;

/// <summary>
/// Every <see cref="Order"/> in the round. Adventurers want things (a meal when hungry, a mend when
/// their gear is worn, a healer when hurt, and once per party a quest), each worth so much to them.
/// They read the shops' signs (<see cref="ShopPrices"/>) and order from the cheapest that's worth
/// it and that their purse covers; if none is, they say so and go without for a while. Ordered,
/// they ask aloud and keep asking while nobody takes it on. Done, they pay in silver and think better of the town;
/// left waiting, they give up and think worse (<see cref="Adventurer.Satisfaction"/>). Only those
/// who heard an order asked know of it. Players asking each other go through <c>Favours.FavourBoard</c>.
/// </summary>
public sealed class ShopSystem(ulong seed) : ISimSystem, Saving.ISaveState
{
    public string Name => "Shops";
    public int Interval => SimClock.TicksPerHour / 12;

    /// <summary>Hunger (the Food need) below which an adventurer asks for a meal.</summary>
    public const float MealBelow = 0.6f;

    /// <summary>Satisfaction off when a meal they only half wanted (a mealtime's) never comes.</summary>
    public const float PeckishGiveUpMood = 0.25f;

    /// <summary>At a mealtime they'll eat if they're under this: not starving, just ready for it.</summary>
    public const float MealtimeBelow = 0.85f;

    /// <summary>Breakfast and supper, game hours: when adventurers come to the hatch all at once.</summary>
    public const float BreakfastFromHour = 7f, BreakfastUntilHour = 9.5f;

    /// <summary>The small hours, when adventurers order nothing but a room or a bad hurt seen to.</summary>
    public const float SmallHoursFrom = 23f, SmallHoursUntil = 6f;

    public static bool Mealtime(Simulation sim)
    {
        var h = sim.Clock.HourOfDay;
        return h is >= BreakfastFromHour and < BreakfastUntilHour or >= SupperFromHour and < SupperUntilHour;
    }

    public static bool SmallHours(Simulation sim)
    {
        var h = sim.Clock.HourOfDay;
        return h >= SmallHoursFrom || h < SmallHoursUntil;
    }

    /// <summary>Wear (fraction of max HP) below which an adventurer wants a piece of gear mended.</summary>
    public const float RepairBelow = 0.75f;

    /// <summary>Game hours after one order of a kind closes before the same asker posts another.</summary>
    public const float CooldownHours = 2;

    /// <summary>Game hours between an asker repeating an order nobody has taken.</summary>
    public const float RepeatHours = 1.5f;

    /// <summary>Game hours a party spends in town (eating, drinking, getting mended) before it asks for a quest.</summary>
    public const float QuestAfterHours = 4;

    /// <summary>Game hours between one round and the next an adventurer orders (±25%).</summary>
    public const float HoursBetweenDrinks = 3;

    /// <summary>The evening hour from which an adventurer staying the night asks for a room, and how late they still will.</summary>
    public const float LodgingFromHour = 18, LodgingUntilHour = 2;

    /// <summary>Satisfaction lost each time an adventurer finds the price too dear for what it's worth.</summary>
    public const float TooDearMood = 0.25f;

    /// <summary>Satisfaction gained, at most, for a bargain: scaled by how much under its worth they paid.</summary>
    public const float BargainMood = 0.5f;

    /// <summary>How long closed orders stay on the books (for the list and the cooldown).</summary>
    public const int KeepClosedTicks = SimClock.TicksPerDay;

    private readonly SimRandom _rand = new(seed ^ 0xE77A_4D5u);
    private readonly List<Order> _orders = [];
    private readonly HashSet<int> _questAsked = [];

    /// <summary>When someone last went without something (too dear, or nobody offering it): they don't look again until the cooldown's passed.</summary>
    private readonly Dictionary<(Entity Who, ServiceKind Kind), long> _wentWithout = [];

    /// <summary>Who last saw to each patient's wounds (drawn or bandaged), to be paid for a Healing order.</summary>
    private readonly Dictionary<Entity, Entity> _treatedBy = [];
    private int _nextId = 1;

    /// <summary>Every order still on the books, oldest first. Game code reads them through <c>Perception</c>.</summary>
    public IReadOnlyList<Order> All => _orders;

    public Order? Find(int id) => _orders.Find(e => e.Id == id);

    public void Update(Simulation sim)
    {
        if (!sim.Round.Live)
            return;
        var now = sim.Clock.Tick;
        // The small hours don't count against anyone's patience: an order asked late waits for the morning.
        var night = SmallHours(sim);
        foreach (var e in _orders)
        {
            if (!e.IsOpen)
                continue;
            if (night)
                e.Deadline += Interval;
            if (!sim.World.IsAlive(e.Poster) || sim.World.Get<Creature>(e.Poster).Dead || sim.World.Has<Leaving>(e.Poster))
            {
                Close(sim, e, OrderStatus.Cancelled);
                // Off on a quest, they leave it in the smith's keeping; leaving town for good, they call it theft.
                var questing = sim.World.IsAlive(e.Poster) && sim.World.TryGet<Adventurer>(e.Poster, out var a) && a.Quest != 0;
                LostTheirGear(sim, e, questing);
            }
            else if (e.Kind.Kind == ServiceKind.Goods && (!sim.World.IsAlive(e.Subject) || !sim.World.Has<ForSale>(e.Subject)))
                Close(sim, e, OrderStatus.Cancelled); // sold to someone else, or taken off sale
            else if (e.Kind.Kind == ServiceKind.Healing && !Untended(sim, e.Poster))
                Healed(sim, e);
            else if (e.Kind.Kind == ServiceKind.WagonRepair && !Tinkering.Wagons.Damaged(sim, e.Subject))
                WagonMended(sim, e);
            else if (now >= e.Deadline && StillAtIt(sim, e))
                BearWithIt(sim, e);
            else if (now >= e.Deadline)
                GiveUp(sim, e);
            else if (Hatch.TryCollect(sim, e))
                continue; // took a dish off the hatch
            else if (e.Assignee.IsNull && TimeToAsk(sim, e))
                Announce(sim, e, Asking(e.Kind, e.Price, SubjectLabel(sim, e.Subject)));
            else
            {
                TryHandOver(sim, e);
                TryLetARoom(sim, e);
            }
        }
        _orders.RemoveAll(e => !e.IsOpen && now - e.ClosedTick > KeepClosedTicks);
        foreach (var gone in _treatedBy.Keys.Where(k => !sim.World.IsAlive(k)).ToList())
            _treatedBy.Remove(gone);
        var stale = SimClock.HoursToTicks(CooldownHours);
        foreach (var key in _wentWithout.Where(kv => !sim.World.IsAlive(kv.Key.Who) || now - kv.Value > stale).Select(kv => kv.Key).ToList())
            _wentWithout.Remove(key);
        ShopForWhatTheyWant(sim);
        ShopForSupper(sim);
    }

    /// <summary>The evening hours villagers buy their supper at the inn.</summary>
    public const float SupperFromHour = 18f, SupperUntilHour = 21f;

    /// <summary>Food below which a villager wants supper.</summary>
    public const float SupperBelow = 0.6f;

    /// <summary>
    /// Of an evening, the villagers the AI runs buy their supper at the inn's hatch, if they're
    /// hungry and have the silver: they line up with the adventurers, pay the cook and sit down
    /// with it. So the common room fills at supper, and the kitchen has its rush. Whoever cooks
    /// doesn't buy from themselves; players eat as they like.
    /// </summary>
    private void ShopForSupper(Simulation sim)
    {
        var hour = sim.Clock.HourOfDay;
        if (hour < SupperFromHour || hour >= SupperUntilHour)
            return;
        var w = sim.World;
        var food = sim.Defs.Need("Food");
        foreach (var who in sim.Creatures())
        {
            if (w.Get<Creature>(who) is not { Faction: Faction.Village, Def.Race: Race.Human, Dead: false }
                || w.Has<Avatar>(who) || w.Get<Mind>(who).Asleep || !SpeechSystem.CanAct(sim, who))
                continue;
            if (w.TryGet<Trade>(who, out var trade) && trade.Def.Serving(ServiceKind.Meal))
                continue;
            if (w.Get<Needs>(who).Get(food) >= SupperBelow || Hospitality.Table.IsSavouring(sim, who))
                continue;
            Want(sim, who, ServiceKind.Meal, Entity.Null);
        }
    }

    /// <summary>Each adventurer in town shops for whatever they lack and haven't already ordered.</summary>
    private void ShopForWhatTheyWant(Simulation sim)
    {
        var w = sim.World;
        var food = sim.Defs.Need("Food");
        foreach (var who in sim.Creatures())
        {
            if (!w.TryGet<Adventurer>(who, out var a) || w.Has<Leaving>(who) || !SpeechSystem.CanAct(sim, who))
                continue;
            // Work comes after the road's been washed down: fed, served, mended, then a quest.
            // Once one of the party has a quest on order, the rest leave it to them.
            // Nobody sets out on the festival's night.
            if (!_questAsked.Contains(a.Party) && sim.Round.Phase != Round.RoundPhase.Festival
                && sim.Clock.Tick - a.Arrived >= SimClock.HoursToTicks(QuestAfterHours)
                && Want(sim, who, ServiceKind.Quest, Entity.Null))
                _questAsked.Add(a.Party);
            // Sat with a drink or a meal, they don't order another till it's finished.
            var savouring = Hospitality.Table.IsSavouring(sim, who);
            var hunger = w.Get<Needs>(who).Get(food);
            // In the small hours the town's abed: only a room (till it's late) or a bad hurt seen to.
            if (SmallHours(sim))
            {
                if (WantsARoom(sim, who, a))
                    Want(sim, who, ServiceKind.Lodging, Entity.Null);
                if (w.TryGet<Health>(who, out var hurt) && hurt.Injuries.Where(i => !i.Tended).Sum(i => i.Damage) >= 8f)
                    Want(sim, who, ServiceKind.Healing, Entity.Null, 1.75f);
                continue;
            }
            // Hungry, or peckish at a mealtime (breakfast, supper): the rushes at the hatch.
            if (!savouring && (hunger < MealBelow || (Mealtime(sim) && hunger < MealtimeBelow)))
                Want(sim, who, ServiceKind.Meal, Entity.Null, hunger < food.UrgentBelow ? 1.5f : 1f);
            if (sim.Clock.Tick >= a.NextDrink && !savouring && MayAsk(sim, who, ServiceKind.Drink)
                && !_orders.Exists(e => e.IsOpen && e.Poster == who && e.Kind.Kind == ServiceKind.MixedDrink))
            {
                // Now and then something mixed, at its dearer price; too dear, or not to be had, an ale.
                var fancy = _rand.NextFloat() < Hospitality.DrinkingSystem.NamedDrinkChance && MayAsk(sim, who, ServiceKind.MixedDrink)
                    && Want(sim, who, ServiceKind.MixedDrink, Entity.Null);
                if (!fancy)
                    Want(sim, who, ServiceKind.Drink, Entity.Null);
                w.Get<Adventurer>(who).NextDrink = sim.Clock.Tick + SimClock.HoursToTicks(HoursBetweenDrinks * _rand.Range(0.75f, 1.25f));
            }
            if (MostWorn(sim, who) is { IsNull: false } gear)
                Want(sim, who, ServiceKind.Repair, gear, WeaponWear.HpFraction(sim, gear) < 0.35f ? 1.5f : 1f);
            if (w.TryGet<Health>(who, out var h) && h.Injuries.Exists(i => !i.Tended))
                Want(sim, who, ServiceKind.Healing, Entity.Null, h.Injuries.Where(i => !i.Tended).Sum(i => i.Damage) >= 8f ? 1.75f : 1f);
            if (Tinkering.Lockwork.LockedBoxOf(sim, who) is { IsNull: false } box)
                Want(sim, who, ServiceKind.Unlock, box);
            if (Tinkering.Wagons.Of(sim, a.Party) is { IsNull: false } wagon && Tinkering.Wagons.Damaged(sim, wagon)
                && w.Get<Tinkering.Wagon>(wagon).Owner == who && !_orders.Exists(e => e.IsOpen && e.Subject == wagon))
                Want(sim, who, ServiceKind.WagonRepair, wagon);
            if (Tinkering.Curios.UnvaluedOf(sim, who) is { IsNull: false } curio)
                Want(sim, who, ServiceKind.Appraise, curio);
            if (WantsARoom(sim, who, a))
                Want(sim, who, ServiceKind.Lodging, Entity.Null);
            ShopForGear(sim, who);
        }
    }

    /// <summary>Evening, staying the night, and no room paid for tonight: they'll want one (another night, if they had one last night).</summary>
    private static bool WantsARoom(Simulation sim, Entity who, Adventurer a)
    {
        var hour = sim.Clock.HourOfDay;
        if (hour is < LodgingFromHour and >= LodgingUntilHour || a.LeaveTick - sim.Clock.Tick < SimClock.HoursToTicks(6))
            return false;
        if (Hospitality.Lodging.Lodged(sim, who))
            return false;
        // A bed of their own that isn't a let one (a villager's): no need of a room.
        return !(sim.World.TryGet<BedOwner>(who, out var own) && sim.World.IsAlive(own.Bed) && !IsLet(sim, own.Bed));
    }

    /// <summary>
    /// Whoever took on a room for someone shows them up to it, once they're in reach: tonight, a
    /// let bed is theirs (<see cref="Hospitality.Lodging"/>; the one they had last night if they
    /// did), and they pay. A made bed if there is one; shown to an unmade one, they take it and
    /// think the less of the place. With every room taken, they wait or give up.
    /// </summary>
    private void TryLetARoom(Simulation sim, Order e)
    {
        var w = sim.World;
        if (e.Kind.Kind != ServiceKind.Lodging || e.Assignee.IsNull || !Handing.InReach(sim, e.Assignee, e.Poster))
            return;
        var kept = Hospitality.Lodging.Kept(sim, e.Poster);
        var bed = kept.IsNull ? FreeLetBed(sim, e.Where) : kept;
        if (bed.IsNull)
            return;
        Hospitality.Lodging.Let(sim, e.Poster, bed);
        var welcome = ShopName.Of(sim, e.Where) is { } called ? $" Welcome to {called}." : "";
        sim.Speech.Say(sim, e.Assignee, kept.IsNull ? $"Up the stairs. It's yours for the night.{welcome}" : "Same room again tonight.", Volume.Say);
        if (kept.IsNull && Hospitality.Lodging.IsUnmade(sim, bed))
        {
            if (w.Has<Adventurer>(e.Poster))
                w.Get<Adventurer>(e.Poster).Satisfaction -= UnmadeMood;
            sim.Speech.Say(sim, e.Poster, "Somebody else's sheets? Lovely.", Volume.Say);
        }
        Complete(sim, e, e.Assignee);
    }

    /// <summary>Satisfaction lost by a guest shown to a bed nobody's made up.</summary>
    public const float UnmadeMood = 0.5f;

    /// <summary>
    /// A bed in a building that lets them (<see cref="BuildingDef.LetsBeds"/>) nobody living has been
    /// given: a made one if there is one, else an unmade one. Entity.Null if they're all taken.
    /// </summary>
    public static Entity FreeLetBed(Simulation sim, Entity building)
    {
        var w = sim.World;
        if (!w.IsAlive(building) || !w.TryGet<PresetBuilding>(building, out var b) || !b.Def.LetsBeds)
            return Entity.Null;
        var taken = new HashSet<Entity>();
        foreach (var o in w.Each<BedOwner>())
            taken.Add(w.Get<BedOwner>(o).Bed);
        var unmade = Entity.Null;
        foreach (var part in Buildings.Parts(sim, building))
            if (w.Get<Thing>(part).Def.Building is { IsBed: true } && !w.Has<Construction>(part) && !taken.Contains(part))
            {
                if (!w.Has<Hospitality.Unmade>(part))
                    return part;
                if (unmade.IsNull)
                    unmade = part;
            }
        return unmade;
    }

    /// <summary>A bed let out (in a building that lets them): only whoever's been given it sleeps there.</summary>
    public static bool IsLet(Simulation sim, Entity bed) =>
        Buildings.Of(sim, bed) is { IsNull: false } b && sim.World.Get<PresetBuilding>(b).Def.LetsBeds;

    /// <summary>A bed the sick are seen to on (<see cref="BuildingDef.LineOnBeds"/>: the shrine's pallets), not one to spend the night in.</summary>
    public static bool IsForPatients(Simulation sim, Entity bed) =>
        Buildings.Of(sim, bed) is { IsNull: false } b && sim.World.Get<PresetBuilding>(b).Def.LineOnBeds;

    /// <summary>
    /// An adventurer wants something: what's it worth to them (<see cref="Worth"/>), and what can
    /// they spare? They order from the cheapest shop offering it within that, the nearest of equals,
    /// at the price on its sign. Too dear everywhere, they say so, think the less of the town for it
    /// and go without for <see cref="CooldownHours"/>; with nobody offering it, they just go without.
    /// True if they ordered.
    /// </summary>
    private bool Want(Simulation sim, Entity who, ServiceKind kind, Entity subject, float urgency = 1f)
    {
        if (!MayAsk(sim, who, kind))
            return false;
        var def = sim.Defs.Service(kind);
        var worth = Worth(sim, who, def, urgency);
        var spare = Math.Max(0, Coin.Count(sim, who) - Promised(who));
        // The box pays for its own opening: they know what's in it, and they pay once it's open.
        if (kind == ServiceKind.Unlock && sim.World.TryGet<Strongbox>(subject, out var box))
            spare += box.Silver;
        var (shop, price, cheapest) = ChooseShop(sim, who, def, Math.Min(worth, spare));
        // Somewhere offers it, but nowhere they know the way to: they ask.
        if (cheapest == Lost)
        {
            sim.Directions.Ask(sim, who, def.AtDef!, def.Label);
            return false;
        }
        if (price < 0)
        {
            _wentWithout[(who, kind)] = sim.Clock.Tick;
            if (cheapest >= 0)
                TooDear(sim, who, def, cheapest, worth, spare, SubjectLabel(sim, subject) ?? WoundLabel(sim, who));
            return false;
        }
        var text = Asking(def, price, SubjectLabel(sim, subject) ?? WoundLabel(sim, who));
        // A meal: they may ask for a dish on the menu by name (at its price), or the house dish, or
        // say what they fancy, in the same breath (Shops.Menu, Hospitality.Tastes).
        Work.FlavourKind? craving = null;
        var house = false;
        ThingDef? named = null;
        if (kind == ServiceKind.Meal && sim.World.Has<Adventurer>(who) && Menu.Offered(sim, shop).ToList() is { Count: > 0 } menu
            && _rand.NextFloat() < Menu.AskChance)
        {
            var pick = menu[_rand.NextInt(menu.Count)];
            if (pick.Price <= Math.Min((int)MathF.Round(worth * pick.Dish.Menu!.Worth), spare))
            {
                named = pick.Dish;
                price = pick.Price;
                text = string.Format(pick.Dish.Menu.Asks, price);
            }
        }
        if (kind == ServiceKind.Meal && named == null && sim.World.Has<Adventurer>(who))
        {
            if (Work.HouseDish.Of(sim, shop) is { } dish && _rand.NextFloat() < Work.HouseDish.AskChance)
            {
                house = true;
                text += $" The {dish.Name}, if there's any. I've heard about it.";
            }
            else if (Hospitality.Tastes.Crave(sim, who, _rand) is { } c)
            {
                craving = c;
                text += " " + Work.Flavour.Craving(c);
            }
        }
        var order = Post(sim, who, def, text, price, sim.Clock.Tick + SimClock.HoursToTicks(def.PatienceHours), subject, shop, worth, craving);
        order.WantsHouse = house;
        order.Dish = named;
        return true;
    }

    /// <summary>
    /// A player orders for themselves at a shop (F at the inn's serving hatch): at the price on its
    /// sign, if they have the silver to spare. Said aloud like anyone's order, and in its line like
    /// anyone's: at the front of it, with a dish up, they take it, pay, and sit with it. Players
    /// know the town, so there's no asking the way. Null if it's ordered; else why not.
    /// </summary>
    public string? PlayerOrder(Simulation sim, Entity who, ServiceKind kind, Entity shop)
    {
        var w = sim.World;
        var def = sim.Defs.Service(kind);
        if (!sim.Round.Live)
            return "nobody's serving yet";
        if (w.TryGet<Trade>(who, out var trade) && w.TryGet<PresetBuilding>(shop, out var b) && trade.Def.WorkplaceDef == b.Def && trade.Def.Serving(kind))
            return "you'd be serving yourself";
        if (_orders.Exists(e => e.IsOpen && e.Poster == who && e.Kind.Kind == kind))
            return $"you've ordered {def.Label} already";
        if (ShopPrices.Posted(sim, shop, kind) is not { Offered: true } sign)
            return $"there's no {def.Label} to be had here";
        var spare = Math.Max(0, Coin.Count(sim, who) - Promised(who));
        if (sign.Price > spare)
            return $"{def.Label} is {sign.Price} silver, and you have {spare} to spare";
        Post(sim, who, def, Asking(def, sign.Price, null), sign.Price, sim.Clock.Tick + SimClock.HoursToTicks(def.PatienceHours), Entity.Null, shop, sign.Price);
        return null;
    }

    /// <summary>A player's open order of a kind, if they have one.</summary>
    public Order? OpenOrderOf(Entity who, ServiceKind kind) => _orders.Find(e => e.IsOpen && e.Poster == who && e.Kind.Kind == kind);

    /// <summary>
    /// An adventurer looks over the gear on show in the town's shops (<see cref="Goods.OnShow"/>)
    /// for a piece they'd be better off with. Of those within what it's worth to them (per tier,
    /// times how well it's made) and what they can spare, they order the best, then the cheapest.
    /// If the only pieces they'd want are too dear, they say so and stop looking for a while.
    /// </summary>
    private void ShopForGear(Simulation sim, Entity who)
    {
        if (!MayAsk(sim, who, ServiceKind.Goods))
            return;
        var w = sim.World;
        var def = sim.Defs.Service(ServiceKind.Goods);
        var spend = w.Get<Adventurer>(who).Kind?.Spend ?? 1f;
        var spare = Math.Max(0, Coin.Count(sim, who) - Promised(who));
        int? roll = null;
        (Entity Item, Entity Shop, int Price, int Worth, int Tier)? best = null, dear = null;
        foreach (var (item, shop, price) in Goods.OnShow(sim))
        {
            if (_orders.Exists(o => o.IsOpen && o.Subject == item) || !Goods.IsUpgrade(sim, who, item))
                continue;
            roll ??= _rand.Range(def.WorthMin, def.WorthMax + 1);
            var t = w.Get<Thing>(item);
            var worth = (int)MathF.Round(roll.Value * Math.Max(1, t.Def.GearTier) * Goods.WorthFactor(sim, item) * spend);
            var tier = t.Def.GearTier;
            if (price <= Math.Min(worth, spare))
            {
                if (best is not { } b || tier > b.Tier || (tier == b.Tier && price < b.Price))
                    best = (item, shop, price, worth, tier);
            }
            else if (dear is not { } d || price < d.Price)
                dear = (item, shop, price, worth, tier);
        }
        if (best is { } buy)
        {
            var label = w.Get<Thing>(buy.Item).Label;
            Post(sim, who, def, Asking(def, buy.Price, label), buy.Price, sim.Clock.Tick + SimClock.HoursToTicks(def.PatienceHours),
                buy.Item, buy.Shop, buy.Worth);
        }
        else if (dear is { } no)
        {
            _wentWithout[(who, ServiceKind.Goods)] = sim.Clock.Tick;
            TooDear(sim, who, def, no.Price, no.Worth, spare, w.Get<Thing>(no.Item).Label);
        }
    }

    /// <summary>
    /// What a service is worth to this adventurer right now, in silver: rolled in the def's range
    /// (<see cref="ServiceDef.WorthMin"/>), times how free with coin their kind is
    /// (<see cref="AdventurerKindDef.Spend"/>), times how badly they need it.
    /// </summary>
    private int Worth(Simulation sim, Entity who, ServiceDef def, float urgency)
    {
        var spend = sim.World.TryGet<Adventurer>(who, out var a) ? a.Kind?.Spend ?? 1f : 1f;
        return Math.Max(0, (int)MathF.Round(_rand.Range(def.WorthMin, def.WorthMax + 1) * spend * urgency));
    }

    /// <summary>Silver this person has promised to orders still open.</summary>
    private int Promised(Entity who)
    {
        var sum = 0;
        foreach (var e in _orders)
            if (e.IsOpen && e.Poster == who)
                sum += e.Price;
        return sum;
    }

    /// <summary><c>Cheapest</c> from <see cref="ChooseShop"/> when a shop offers it, but the asker doesn't know the way to any that do.</summary>
    private const int Lost = -2;

    /// <summary>
    /// The shop to order from: of the standing buildings of the service's kind offering it at no
    /// more than <paramref name="budget"/> that they know the way to (<see cref="Hospitality.DirectionsSystem"/>),
    /// the cheapest, then the nearest. Price -1 if none; <c>Cheapest</c> is the lowest price offered
    /// at all (-1 if nobody offers it, <see cref="Lost"/> if only shops they can't find do). A town
    /// with no building for it at all serves it at the default price, from nowhere in particular.
    /// </summary>
    private static (Entity Shop, int Price, int Cheapest) ChooseShop(Simulation sim, Entity who, ServiceDef def, int budget)
    {
        if (def.AtDef == null || BuildingOf(sim, def.AtDef).IsNull)
            return def.DefaultPrice <= budget ? (Entity.Null, def.DefaultPrice, def.DefaultPrice) : (Entity.Null, -1, def.DefaultPrice);
        var w = sim.World;
        var from = w.Get<Position>(who).Cell;
        var best = Entity.Null;
        int bestPrice = -1, bestDist = int.MaxValue, cheapest = -1;
        var lost = false;
        foreach (var e in w.Each<PresetBuilding>())
        {
            var b = w.Get<PresetBuilding>(e);
            if (b.Def != def.AtDef || b.Demolishing || ShopPrices.Posted(sim, e, def.Kind) is not { Offered: true } sign)
                continue;
            if (!Hospitality.DirectionsSystem.Knows(sim, who, e))
            {
                lost = true;
                continue;
            }
            if (cheapest < 0 || sign.Price < cheapest)
                cheapest = sign.Price;
            if (sign.Price > budget)
                continue;
            var dist = Maps.Map.Distance(from, b.Anchor);
            if (best.IsNull || sign.Price < bestPrice || (sign.Price == bestPrice && dist < bestDist))
            {
                best = e;
                bestPrice = sign.Price;
                bestDist = dist;
            }
        }
        return (best, bestPrice, cheapest < 0 && lost ? Lost : cheapest);
    }

    /// <summary>The cheapest offer is more than they'll pay: they say so, and a price over its worth sours them on the town.</summary>
    private void TooDear(Simulation sim, Entity who, ServiceDef def, int price, int worth, int spare, string? subject)
    {
        string line;
        if (price > worth)
        {
            if (sim.World.Has<Adventurer>(who))
                sim.World.Get<Adventurer>(who).Satisfaction -= TooDearMood;
            line = def.TooDearLines.Count == 0
                ? $"{price} silver for {def.Label}? I'll do without."
                : string.Format(def.TooDearLines[_rand.NextInt(def.TooDearLines.Count)], price, subject ?? "gear");
        }
        else
            line = spare <= 0 ? $"I'd want {def.Label}, if I had the silver." : $"{price} silver? I haven't got it to spare.";
        sim.Speech.Say(sim, who, line, Volume.Say);
    }

    /// <summary>Has nothing of this kind open, didn't just have one close, and didn't just go without one?</summary>
    private bool MayAsk(Simulation sim, Entity who, ServiceKind kind)
    {
        var since = sim.Clock.Tick - SimClock.HoursToTicks(CooldownHours);
        if (_wentWithout.TryGetValue((who, kind), out var at) && at > since)
            return false;
        foreach (var e in _orders)
            if (e.Poster == who && e.Kind.Kind == kind && (e.IsOpen || e.ClosedTick > since))
                return false;
        return true;
    }

    /// <summary>Put an order on the books and say it aloud. Those who hear it clearly know of it.</summary>
    public Order Post(Simulation sim, Entity poster, ServiceDef kind, string text, int price, long deadline, Entity? subject = null,
        Entity? where = null, int worth = 0, Work.FlavourKind? craving = null)
    {
        var order = new Order
        {
            Craving = craving,
            Id = _nextId++, Kind = kind, Poster = poster, PosterName = sim.World.Get<Creature>(poster).Name, Text = text,
            Where = where ?? (kind.AtDef is { } at ? BuildingOf(sim, at) : Entity.Null), Subject = subject ?? Entity.Null, Price = price,
            Worth = worth, PostedTick = sim.Clock.Tick, Deadline = deadline,
        };
        _orders.Add(order);
        Learn(order, poster);
        // Word of work at a trade's building reaches whoever holds that trade.
        var workplace = sim.World.IsAlive(order.Where) && sim.World.TryGet<PresetBuilding>(order.Where, out var shop) ? shop.Def : kind.AtDef;
        if (workplace != null)
            foreach (var e in sim.Creatures())
                if (sim.World.TryGet<Trade>(e, out var trade) && trade.Def.WorkplaceDef == workplace)
                    Learn(order, e);
        // At a shop with a line they say it at the counter (or the hatch), when their turn comes (TimeToAsk).
        if (LineCells(sim, order.Where, Hatch.Takes(sim, order)).Count == 0)
            Announce(sim, order, text);
        if (kind.Kind == ServiceKind.MixedDrink && sim.World.Has<Adventurer>(poster))
            sim.Drinking.Ordered(sim, order);
        return order;
    }

    /// <summary>The asker says it (again): everyone who catches it clearly now knows of it.</summary>
    private static void Announce(Simulation sim, Order e, string? text)
    {
        if (text == null || sim.Speech.Say(sim, e.Poster, text, Volume.Say) is not { } said)
            return;
        e.LastAsked = sim.Clock.Tick;
        foreach (var h in said.Listeners)
            if (h.Clarity == Clarity.Clear)
                Learn(e, h.Listener);
    }

    public static void Learn(Order e, Entity who)
    {
        if (!who.IsNull && !e.KnownBy.Contains(who))
            e.KnownBy.Add(who);
    }

    /// <summary>Someone says they'll see to an order they know of. False if they can't take it.</summary>
    public bool Accept(Simulation sim, Entity who, int id)
    {
        if (Find(id) is not { IsOpen: true } e || e.Poster == who || !e.Assignee.IsNull || !e.KnownBy.Contains(who))
            return false;
        e.Assignee = who;
        e.AssigneeName = sim.World.Get<Creature>(who).Name;
        if (sim.Speech.Say(sim, who, $"I'll see to {e.Kind.Label}, {e.PosterName}.", Volume.Say) is { } said)
            foreach (var h in said.Listeners)
                if (h.Clarity == Clarity.Clear)
                    Learn(e, h.Listener);
        return true;
    }

    /// <summary>
    /// The order is done, by <paramref name="by"/>: the asker pays what they promised (or what
    /// they have), thanks them aloud, and an adventurer thinks the better of the town for it.
    /// </summary>
    public void Complete(Simulation sim, Order e, Entity by)
    {
        if (!e.IsOpen)
            return;
        _served[(int)e.Kind.Kind]++;
        var paid = Coin.Pay(sim, e.Poster, by, e.Price);
        Close(sim, e, OrderStatus.Done);
        Learn(e, by);
        if (e.Assignee.IsNull)
        {
            e.Assignee = by;
            e.AssigneeName = sim.World.Get<Creature>(by).Name;
        }
        if (sim.World.TryGet<Adventurer>(e.Poster, out _))
        {
            // Quick service counts for more, and so does a bargain.
            var quick = sim.Clock.Tick - e.PostedTick < (e.Deadline - e.PostedTick) / 2;
            var bargain = e.Worth > 0 ? Math.Clamp((e.Worth - e.Price) / (float)e.Worth, 0f, 1f) * BargainMood : 0f;
            sim.World.Get<Adventurer>(e.Poster).Satisfaction += (quick ? 1.5f : 1f) + bargain;
        }
        var thanks = paid >= e.Price ? $"There. {paid} silver, as promised." : $"Here's {paid} silver. It's all I've got.";
        sim.Speech.Say(sim, e.Poster, thanks, Volume.Say);
    }

    // ---------------- The line ----------------

    /// <summary>
    /// Time to say what they want: nobody's taken it on, and they're at the front of the shop's
    /// line (or it has none) and haven't said it since ordering, or not for <see cref="RepeatHours"/>.
    /// </summary>
    private bool TimeToAsk(Simulation sim, Order e)
    {
        var now = sim.Clock.Tick;
        var hatch = Hatch.Takes(sim, e);
        var lined = LineCells(sim, e.Where, hatch);
        if (lined.Count == 0)
            return now - e.LastAsked >= SimClock.HoursToTicks(RepeatHours);
        if (LineAt(sim, e.Where, hatch) is not { Count: > 0 } line || line[0] != e.Poster || !sim.World.TryGet<Position>(e.Poster, out var at)
            || at.Cell.Z != lined[0].Z || Maps.Map.Distance(at.Cell, lined[0]) > 1)
            return false;
        return e.LastAsked < e.PostedTick || now - e.LastAsked >= SimClock.HoursToTicks(RepeatHours);
    }

    /// <summary>
    /// Is the order holding its asker's place in line: open, and not a piece they've already
    /// handed over to be seen to (they step aside and wait for it to come back)?
    /// </summary>
    public static bool WaitsInLine(Simulation sim, Order e) =>
        e.IsOpen && !(HandsOver(e.Kind.Kind) && !e.Assignee.IsNull && sim.World.IsAlive(e.Subject) && Inventory.OnPerson(sim, e.Assignee, e.Subject));

    /// <summary>
    /// The customers waiting at a shop, front first: in the order they first ordered there. Worked
    /// out from the orders each time, so there's nothing to save and nothing to fall out of step.
    /// <paramref name="hatch"/>: the line at its serving hatch instead (<see cref="Shops.Hatch"/>).
    /// </summary>
    public List<Entity> LineAt(Simulation sim, Entity shop, bool hatch = false)
    {
        var line = new List<Entity>();
        if (shop.IsNull)
            return line;
        foreach (var e in _orders) // oldest first
            if (e.Where == shop && WaitsInLine(sim, e) && !line.Contains(e.Poster) && Hatch.Takes(sim, e) == hatch)
                line.Add(e.Poster);
        return line;
    }

    /// <summary>
    /// The cells of a shop's line on the map, front first (<see cref="BuildingDef.Line"/>, turned
    /// with the building), leaving out any that are off the map, walled, or have something built
    /// in them. Empty if it isn't a building or has no line.
    /// </summary>
    public static List<int3> LineCells(Simulation sim, Entity shop, bool hatch = false)
    {
        var cells = new List<int3>();
        if (!sim.World.IsAlive(shop) || !sim.World.TryGet<PresetBuilding>(shop, out var b))
            return cells;
        if (!hatch && b.Def.LineOnBeds)
            foreach (var bed in BedsOf(sim, shop))
                cells.Add(sim.World.Get<Thing>(bed).Cell);
        foreach (var at in hatch ? b.Def.HatchLine : b.Def.Line)
        {
            var c = Buildings.CellOf(sim.Map, b.Def, b.Anchor, b.Facing, at[0], at[1]);
            if (sim.Map.InBounds(c) && sim.Map.Passable(c) && sim.Map.Structure[sim.Map.Idx(c)].IsNull)
                cells.Add(c);
        }
        return cells;
    }

    /// <summary>A shop's finished beds that a customer can lie on (<see cref="BuildingDef.LineOnBeds"/>), in store order.</summary>
    public static List<Entity> BedsOf(Simulation sim, Entity shop)
    {
        var beds = new List<Entity>();
        var w = sim.World;
        foreach (var e in w.Each<PartOf>())
            if (w.Get<PartOf>(e).Building == shop && w.TryGet<Thing>(e, out var t) && t.Spawned && t.Def.Building?.IsBed == true
                && !w.Has<Construction>(e) && sim.Map.Passable(t.Cell))
                beds.Add(e);
        return beds;
    }

    /// <summary>
    /// Where a customer belongs: the shop of their oldest order still holding a place in line, their
    /// place in it (0 at the front), and the cell to stand in (null past the line's end: they wait
    /// about the shop). Null if they're waiting on nothing.
    /// </summary>
    public (Entity Shop, int Place, int3? Cell)? PlaceOf(Simulation sim, Entity who)
    {
        foreach (var e in _orders)
        {
            if (e.Poster != who || !WaitsInLine(sim, e) || !sim.World.IsAlive(e.Where))
                continue;
            var hatch = Hatch.Takes(sim, e);
            var place = LineAt(sim, e.Where, hatch).IndexOf(who);
            var cells = LineCells(sim, e.Where, hatch);
            return (e.Where, place, place >= 0 && place < cells.Count ? cells[place] : null);
        }
        return null;
    }

    /// <summary>
    /// Open orders in the order a keeper serves them: whoever's nearest the front of the line at
    /// the shop it's for first, then the rest oldest first.
    /// </summary>
    public List<Order> FrontFirst(Simulation sim)
    {
        var lines = new Dictionary<(Entity, bool), List<Entity>>();
        int Rank(Order e)
        {
            var key = (e.Where, Hatch.Takes(sim, e));
            if (!lines.TryGetValue(key, out var line))
                lines[key] = line = LineAt(sim, e.Where, key.Item2);
            var place = line.IndexOf(e.Poster);
            return place < 0 ? int.MaxValue : place;
        }
        return _orders.Where(e => e.IsOpen).OrderBy(Rank).ToList(); // stable: ties stay oldest first
    }

    /// <summary>Orders that hand something over to be seen to and given back.</summary>
    public static bool HandsOver(ServiceKind kind) => kind is ServiceKind.Repair or ServiceKind.Unlock or ServiceKind.Appraise;

    /// <summary>
    /// An adventurer gives the piece to be mended (or the box to be opened) to whoever took the
    /// job on, once they're in reach. True if it changed hands (or already had).
    /// </summary>
    public bool TryHandOver(Simulation sim, Order e)
    {
        var w = sim.World;
        if (!e.IsOpen || !HandsOver(e.Kind.Kind) || e.Assignee.IsNull || !w.IsAlive(e.Subject))
            return false;
        if (Inventory.OnPerson(sim, e.Assignee, e.Subject))
            return true;
        if (!Inventory.OnPerson(sim, e.Poster, e.Subject) || !Handing.InReach(sim, e.Poster, e.Assignee) || !SpeechSystem.CanAct(sim, e.Poster))
            return false;
        if (e.Kind.Kind == ServiceKind.Unlock)
        {
            var box = Inventory.TakeFromPerson(sim, e.Poster, e.Subject, 1);
            w.Add(box, new Entrusted { Owner = e.Poster });
            Inventory.Stow(sim, e.Assignee, box);
            sim.Speech.Say(sim, e.Poster, "Here. Mind your fingers: none of us would touch the lid.", Volume.Say);
            GiveTime(sim, e);
            return true;
        }
        if (e.Kind.Kind == ServiceKind.Appraise)
        {
            var curio = Inventory.TakeFromPerson(sim, e.Poster, e.Subject, 1);
            w.Add(curio, new Entrusted { Owner = e.Poster });
            Inventory.Stow(sim, e.Assignee, curio);
            sim.Speech.Say(sim, e.Poster, "Careful with it. Tell us straight what it's worth.", Volume.Say);
            GiveTime(sim, e);
            return true;
        }
        if (w.TryGet<Equipment>(e.Poster, out var eq) && eq.Weapon == e.Subject)
            w.Remove<Equipment>(e.Poster);
        else if (w.Get<Thing>(e.Subject).Def.Apparel is { } apparel && w.TryGet<Apparel>(e.Poster, out var worn) && worn[apparel.Slot] == e.Subject)
            w.Get<Apparel>(e.Poster)[apparel.Slot] = Entity.Null;
        else
            return false;
        w.Add(e.Subject, new Entrusted { Owner = e.Poster });
        Inventory.Stow(sim, e.Assignee, e.Subject);
        sim.Speech.Say(sim, e.Poster, $"Here. Mind you bring it back {(WeaponWear.Edged(w.Get<Thing>(e.Subject).Def) ? "sharp" : "whole")}.", Volume.Say);
        GiveTime(sim, e);
        return true;
    }

    /// <summary>Handed over: the work's clock starts now, whatever was left of their wait for someone to take it on.</summary>
    private static void GiveTime(Simulation sim, Order e) =>
        e.Deadline = Math.Max(e.Deadline, sim.Clock.Tick + SimClock.HoursToTicks(HandedOverHours));

    /// <summary>Game hours an asker gives once they've handed their piece over: the work's clock starts then.</summary>
    public const float HandedOverHours = 4f;

    /// <summary>How long, in all, they'll wait on a piece that's still being worked: this many times their patience.</summary>
    public const float MostPatience = 3f;

    /// <summary>
    /// The deadline's come but their piece is still with whoever took the job: they'd sooner wait
    /// than walk off without it, up to <see cref="MostPatience"/> times their usual patience.
    /// </summary>
    private static bool StillAtIt(Simulation sim, Order e) =>
        HandsOver(e.Kind.Kind) && !e.Assignee.IsNull && sim.World.IsAlive(e.Subject) && sim.World.IsAlive(e.Assignee)
        && Inventory.OnPerson(sim, e.Assignee, e.Subject)
        && sim.Clock.Tick < e.PostedTick + SimClock.HoursToTicks(e.Kind.PatienceHours * MostPatience);

    private static void BearWithIt(Simulation sim, Order e)
    {
        e.Deadline = sim.Clock.Tick + SimClock.TicksPerHour;
        sim.Speech.Say(sim, e.Poster, $"Is my {SubjectLabel(sim, e.Subject)} nearly done? I'll give you a while longer.", Volume.Say);
    }

    /// <summary>Has this person handed something over to be seen to that they haven't had back?</summary>
    public bool GearOut(Simulation sim, Entity poster) =>
        _orders.Exists(e => e.IsOpen && e.Poster == poster && HandsOver(e.Kind.Kind) && sim.World.IsAlive(e.Subject)
            && !Inventory.OnPerson(sim, poster, e.Subject));

    /// <summary>Did the asker go without the thing they handed over (the order closing with it still out)?</summary>
    private static void LostTheirGear(Simulation sim, Order e, bool questing = false)
    {
        var w = sim.World;
        if (!HandsOver(e.Kind.Kind) || !w.IsAlive(e.Subject) || !w.IsAlive(e.Poster) || Inventory.OnPerson(sim, e.Poster, e.Subject))
            return;
        if (w.Has<Entrusted>(e.Subject))
            w.Remove<Entrusted>(e.Subject); // theirs now, for better or worse
        if (questing)
        {
            sim.Speech.Say(sim, e.Poster, $"We're off. Keep my {SubjectLabel(sim, e.Subject)} safe till we're back.", Volume.Say);
            return;
        }
        if (w.Has<Adventurer>(e.Poster))
            w.Get<Adventurer>(e.Poster).Satisfaction -= 2f;
        sim.Speech.Say(sim, e.Poster, $"And {(e.AssigneeName.Length > 0 ? e.AssigneeName : "this town")} keeps my {SubjectLabel(sim, e.Subject)}. Thieves, the lot of you.", Volume.Shout);
    }

    /// <summary>Someone saw to a patient's wounds (an empath drew one, a healer bandaged them): theirs to be paid for it.</summary>
    public void Treated(Simulation sim, Entity patient, Entity by) => _treatedBy[patient] = by;

    private static bool Untended(Simulation sim, Entity who) =>
        sim.World.TryGet<Health>(who, out var h) && h.Injuries.Exists(i => i.NeedsTending(sim.Clock.Tick));

    /// <summary>Nothing left untended: whoever took it on (or did the work) is paid; if nobody did, they mended on their own.</summary>
    private void Healed(Simulation sim, Order e)
    {
        var w = sim.World;
        var by = !e.Assignee.IsNull && w.IsAlive(e.Assignee) ? e.Assignee
            : _treatedBy.TryGetValue(e.Poster, out var t) && w.IsAlive(t) ? t : Entity.Null;
        _treatedBy.Remove(e.Poster);
        if (by.IsNull)
            Close(sim, e, OrderStatus.Cancelled);
        else
            Complete(sim, e, by);
    }

    /// <summary>They can taste how it was poured (or cooked), and say so.</summary>
    private static void Tasted(Simulation sim, Entity who, Quality quality, bool meal = false, Work.DishFault fault = Work.DishFault.None)
    {
        var (mood, line) = (quality, meal) switch
        {
            (Quality.Crude, false) => (-0.75f, "Flat and warm. Still, a drink's a drink."),
            (Quality.Fine, false) => (0.25f, "That's a decent pour."),
            (Quality.Masterwork, false) => (0.5f, "Now that's how you pour an ale!"),
            (Quality.Crude, true) => (-0.75f, "Burnt, or bland, or both. I've eaten worse. Not often."),
            (Quality.Fine, true) => (0.25f, "Good stew, this."),
            (Quality.Masterwork, true) => (0.5f, "Gods, that's a stew. My compliments to whoever's at the pot!"),
            _ => (0f, (string?)null),
        };
        // A poor bowl: they say what's wrong with it, which is the cook's best lesson.
        if (meal && quality <= Quality.Plain && fault != Work.DishFault.None)
            line = Work.Cooking.FaultLine(fault);
        if (sim.World.Has<Adventurer>(who))
            sim.World.Get<Adventurer>(who).Satisfaction += mood;
        if (line != null)
            sim.Speech.Say(sim, who, line, Volume.Say);
    }

    /// <summary>Satisfaction off for being handed something other than the dish they asked for.</summary>
    public const float WrongDishMood = 0.3f;

    /// <summary>Silver over the price for quick, fine service: a coin for fine, two for masterwork, if it came within a third of their patience.</summary>
    public const int FineTip = 1, MasterworkTip = 2;

    /// <summary>What an adventurer would tip for this order served now at this quality (0 for none).</summary>
    private static int TipFor(Simulation sim, Order e, Quality quality)
    {
        if (!e.IsOpen || !sim.World.Has<Adventurer>(e.Poster) || quality < Quality.Fine)
            return 0;
        var quick = sim.Clock.Tick - e.PostedTick < (e.Deadline - e.PostedTick) / 3;
        return !quick ? 0 : quality >= Quality.Masterwork ? MasterworkTip : FineTip;
    }

    /// <summary>A tip, if they've the silver once they've paid: to whoever served them.</summary>
    private static void Tip(Simulation sim, Entity who, Entity to, int tip)
    {
        if (tip <= 0 || who == to || Coin.Count(sim, who) <= 0)
            return;
        var paid = Coin.Pay(sim, who, to, tip);
        if (paid > 0)
            sim.Speech.Say(sim, who, paid == 1 ? "And one for yourself. That was quick." : $"Have {paid} for yourself. Quick, and good.", Volume.Say);
    }

    private static void GiveUp(Simulation sim, Order e)
    {
        Close(sim, e, OrderStatus.Expired);
        LostTheirGear(sim, e);
        // Only peckish (it was a mealtime): they shrug it off, more or less.
        if (e.Kind.Kind == ServiceKind.Meal && sim.World.TryGet<Adventurer>(e.Poster, out _) && sim.World.TryGet<Needs>(e.Poster, out var needs)
            && needs.Get(sim.Defs.Need("Food")) >= MealBelow)
        {
            sim.World.Get<Adventurer>(e.Poster).Satisfaction -= PeckishGiveUpMood;
            sim.Speech.Say(sim, e.Poster, "Never mind the food. I'll eat later.", Volume.Say);
            return;
        }
        if (sim.World.Has<Adventurer>(e.Poster))
        {
            sim.World.Get<Adventurer>(e.Poster).Satisfaction -= 1f;
            sim.Speech.Say(sim, e.Poster, $"Forget it. So much for {e.Kind.Label} in this town.", Volume.Say);
        }
    }

    private static void Close(Simulation sim, Order e, OrderStatus status)
    {
        e.Status = status;
        e.ClosedTick = sim.Clock.Tick;
    }

    /// <summary>
    /// Something handed to someone (<see cref="GiveCommand"/>): if it's what they're waiting for,
    /// the order is done and paid; otherwise they keep it. Takes ownership of the unspawned item.
    /// </summary>
    public void Received(Simulation sim, Entity giver, Entity to, Entity item)
    {
        var w = sim.World;
        var def = w.Get<Thing>(item).Def;
        var repair = _orders.Find(e => e.IsOpen && e.Poster == to && e.Kind.Kind == ServiceKind.Repair && e.Subject == item);
        if (repair != null)
        {
            TakeBack(sim, repair, giver);
            return;
        }
        var unlock = _orders.Find(e => e.IsOpen && e.Poster == to && e.Kind.Kind == ServiceKind.Unlock && e.Subject == item);
        if (unlock != null)
        {
            TakeBackBox(sim, unlock, giver);
            return;
        }
        var appraise = _orders.Find(e => e.IsOpen && e.Poster == to && e.Kind.Kind == ServiceKind.Appraise && e.Subject == item);
        if (appraise != null)
        {
            TakeBackCurio(sim, appraise, giver);
            return;
        }
        var bought = _orders.Find(e => e.IsOpen && e.Poster == to && e.Kind.Kind == ServiceKind.Goods && e.Subject == item);
        if (bought != null)
        {
            Goods.TakeUp(sim, to, item);
            Complete(sim, bought, giver);
            return;
        }
        // A mixed drink's order first: it's what they'd rather have, if both are open.
        var drink = _orders.Find(e => e.IsOpen && e.Poster == to && e.Kind.Kind == ServiceKind.MixedDrink)
            ?? _orders.Find(e => e.IsOpen && e.Poster == to && e.Kind.Kind == ServiceKind.Drink);
        if (drink != null && def.Drink)
        {
            // A first sip, and they pay; then they take it off to sit with (Hospitality.Table).
            var quality = w.TryGet<Crafted>(item, out var made) ? made.Quality : Quality.Plain;
            sim.Drinking.Served(sim, to, item, drink);
            Tasted(sim, to, quality);
            var tip = TipFor(sim, drink, quality);
            Complete(sim, drink, giver);
            Tip(sim, to, giver, tip);
            Hospitality.Table.Begin(sim, to, item, meal: false, 1, 0f, _rand);
            return;
        }
        var meal = _orders.Find(e => e.IsOpen && e.Poster == to && e.Kind.Kind == ServiceKind.Meal);
        if (meal != null && def.EdibleBy(w.Get<Creature>(to).Def))
        {
            // Asked for a dish by name, and handed something else: they eat it, and say so.
            if (meal.Dish is { } wanted && wanted != def && w.Has<Adventurer>(to))
            {
                w.Get<Adventurer>(to).Satisfaction -= WrongDishMood;
                sim.Speech.Say(sim, to, $"I asked for {wanted.Label}.", Volume.Say);
            }
            var count = Math.Min(EatJob.Wanted(sim, to, def), w.Get<Stack>(item).Count);
            var eaten = sim.TakeFromStack(item, count);
            if (eaten != item)
                Inventory.Stow(sim, giver, item); // the rest back to the server
            var rot = FoodPoisoning.RotOf(sim, eaten);
            Work.Dish? dish = w.TryGet<Work.Dish>(item, out var d) ? d : null;
            var tip = 0;
            if (w.TryGet<Crafted>(eaten, out var made) && made.Quality is var q)
            {
                Tasted(sim, to, q, meal: true, dish?.Fault ?? Work.DishFault.None);
                tip = TipFor(sim, meal, q);
            }
            Hospitality.Tastes.Served(sim, to, dish?.Flavour, meal, dish?.House ?? false);
            Complete(sim, meal, giver);
            Tip(sim, to, giver, tip);
            Hospitality.Table.Begin(sim, to, eaten, meal: true, count, rot, _rand);
            return;
        }
        Inventory.Stow(sim, to, item);
    }

    /// <summary>
    /// The mended piece comes back: they take it up again and look it over. Done right, they pay;
    /// otherwise they say what's still wrong and the job is open again for anyone.
    /// </summary>
    private void TakeBack(Simulation sim, Order e, Entity giver)
    {
        var w = sim.World;
        var item = e.Subject;
        w.Remove<Entrusted>(item);
        if (w.Get<Thing>(item).Def.Apparel != null)
            sim.WearItem(e.Poster, item);
        else
            sim.EquipItem(e.Poster, item);
        if (WeaponWear.Mended(sim, item))
        {
            Complete(sim, e, giver);
            return;
        }
        e.Assignee = Entity.Null;
        e.AssigneeName = "";
        sim.Speech.Say(sim, e.Poster, $"It's still {WeaponWear.Faults(sim, item)}. I'm not paying for that.", Volume.Say);
    }

    /// <summary>
    /// The strongbox comes back. Open, they take the silver out and pay; still locked, they say so
    /// and the job is open again for anyone.
    /// </summary>
    private void TakeBackBox(Simulation sim, Order e, Entity giver)
    {
        var w = sim.World;
        var box = e.Subject;
        w.Remove<Entrusted>(box);
        Inventory.Stow(sim, e.Poster, box);
        ref var b = ref w.Get<Strongbox>(box);
        if (b.Locked)
        {
            e.Assignee = Entity.Null;
            e.AssigneeName = "";
            sim.Speech.Say(sim, e.Poster, "It's still locked. I'm not paying for that.", Volume.Say);
            return;
        }
        var silver = b.Silver;
        b.Silver = 0;
        if (silver > 0)
            Inventory.Stow(sim, e.Poster, sim.CreateItem(sim.Defs.Thing(Coin.Silver), silver));
        Complete(sim, e, giver);
    }

    /// <summary>
    /// The curio comes back. Looked over by whoever hands it back, they take that one's word for
    /// what it's worth (their quote, or the middle of their reckoning) and pay; not looked at, they
    /// say so and the job is open again for anyone.
    /// </summary>
    private void TakeBackCurio(Simulation sim, Order e, Entity giver)
    {
        var w = sim.World;
        var curio = e.Subject;
        w.Remove<Entrusted>(curio);
        Inventory.Stow(sim, e.Poster, curio);
        if (!w.TryGet<Tinkering.Appraisal>(curio, out var a) || a.By != giver)
        {
            e.Assignee = Entity.Null;
            e.AssigneeName = "";
            sim.Speech.Say(sim, e.Poster, "You haven't even looked at it. I'm not paying for that.", Volume.Say);
            return;
        }
        var told = Tinkering.Curios.Told(a);
        w.Get<Tinkering.Valued>(curio) = new Tinkering.Valued { Worth = told, ToldBy = giver };
        sim.Speech.Say(sim, e.Poster, a.Doubts ? $"A fake? Worth {told}? Gods. Well, I'm glad I asked." : $"{told} silver, you reckon. I'll remember that.", Volume.Say);
        Complete(sim, e, giver);
    }

    /// <summary>Nothing wrong with their wagon now: whoever mended it is paid (whoever took it on, if nobody's known to have).</summary>
    private void WagonMended(Simulation sim, Order e)
    {
        var w = sim.World;
        var by = Tinkering.Wagons.IsWagon(sim, e.Subject) && w.Get<Tinkering.Wagon>(e.Subject).MendedBy is var m && w.IsAlive(m) ? m
            : !e.Assignee.IsNull && w.IsAlive(e.Assignee) ? e.Assignee : Entity.Null;
        if (by.IsNull)
        {
            Close(sim, e, OrderStatus.Cancelled);
            return;
        }
        sim.Speech.Say(sim, e.Poster, "The wagon runs like new. Good work.", Volume.Say);
        Complete(sim, e, by);
    }

    /// <summary>The weapon or armor most in need of mending (<see cref="WeaponWear.NeedsMending"/>); Entity.Null if none.</summary>
    public static Entity MostWorn(Simulation sim, Entity who)
    {
        var w = sim.World;
        var worst = Entity.Null;
        var worstHp = 2f;
        void Consider(Entity item)
        {
            if (!w.IsAlive(item) || !WeaponWear.NeedsMending(sim, item) || !WeaponWear.SmithCanMend(sim, item))
                return;
            var hp = WeaponWear.HpFraction(sim, item);
            if (hp < worstHp)
            {
                worst = item;
                worstHp = hp;
            }
        }
        if (w.TryGet<Equipment>(who, out var eq))
            Consider(eq.Weapon);
        foreach (var item in sim.Worn(who))
            Consider(item);
        return worst;
    }

    private string Asking(ServiceDef def, int price, string? subject) =>
        def.Lines.Count == 0 ? $"Wanted: {def.Label}. {price} silver." : string.Format(def.Lines[_rand.NextInt(def.Lines.Count)], price, subject ?? "gear");

    private static string? SubjectLabel(Simulation sim, Entity subject) =>
        sim.World.TryGet<Tinkering.Wagon>(subject, out var wagon) ? Tinkering.Wagons.DamageLabel(wagon.Damage)
        : sim.World.IsAlive(subject) && sim.World.TryGet<Thing>(subject, out var t) ? t.Label : null;

    private static string? WoundLabel(Simulation sim, Entity who)
    {
        if (!sim.World.TryGet<Health>(who, out var h))
            return null;
        var wound = h.Injuries.Find(i => !i.Tended);
        return wound == null ? null : h.Body.Parts[wound.Part].Label;
    }

    /// <summary>The first standing building of a kind, Entity.Null if the town has none.</summary>
    public static Entity BuildingOf(Simulation sim, BuildingDef def)
    {
        foreach (var e in sim.World.Each<PresetBuilding>())
        {
            var b = sim.World.Get<PresetBuilding>(e);
            if (b.Def == def && !b.Demolishing)
                return e;
        }
        return Entity.Null;
    }

    public void Save(Saving.SaveWriter w, Simulation sim)
    {
        var (a, b, c, d) = _rand.State;
        w.UInt(a); w.UInt(b); w.UInt(c); w.UInt(d);
        w.Int(_served.Length);
        foreach (var n in _served)
            w.Int(n);
        w.Int(_nextId);
        var asked = _questAsked.Order().ToList();
        w.Int(asked.Count);
        foreach (var p in asked)
            w.Int(p);
        var treated = _treatedBy.OrderBy(kv => kv.Key.Index).ToList();
        w.Int(treated.Count);
        foreach (var (patient, by) in treated)
        {
            w.Entity(patient);
            w.Entity(by);
        }
        var without = _wentWithout.OrderBy(kv => kv.Key.Who.Index).ThenBy(kv => kv.Key.Kind).ToList();
        w.Int(without.Count);
        foreach (var ((who, kind), tick) in without)
        {
            w.Entity(who);
            w.Def(sim.Defs.Service(kind));
            w.Long(tick);
        }
        w.Int(_orders.Count);
        foreach (var e in _orders)
        {
            w.Int(e.Id);
            w.Def(e.Kind);
            w.Entity(e.Poster);
            w.String(e.PosterName);
            w.String(e.Text);
            w.Entity(e.Where);
            w.Entity(e.Subject);
            w.Int(e.Price);
            w.Int(e.Worth);
            w.Long(e.PostedTick);
            w.Long(e.Deadline);
            w.Entity(e.Assignee);
            w.String(e.AssigneeName);
            w.Byte((byte)e.Status);
            w.Long(e.ClosedTick);
            w.Long(e.LastAsked);
            w.Int(e.Craving is { } craving ? (int)craving : -1);
            w.Bool(e.WantsHouse);
            w.String(e.Dish?.Name ?? "");
            w.Int(e.KnownBy.Count);
            foreach (var k in e.KnownBy)
                w.Entity(k);
        }
    }

    private readonly int[] _served = new int[Enum.GetValues<ServiceKind>().Length];

    /// <summary>Orders of a kind done this round: the inn keeping its customers fed through a shortage.</summary>
    public int Served(ServiceKind kind) => _served[(int)kind];

    public void Load(Saving.SaveReader r, Simulation sim)
    {
        _rand.State = (r.UInt(), r.UInt(), r.UInt(), r.UInt());
        Array.Clear(_served);
        if (r.Version >= 10)
            for (int i = 0, n = r.Count(256); i < n; i++)
            {
                var v = r.Int();
                if (i < _served.Length)
                    _served[i] = v;
            }
        _nextId = r.Int();
        _questAsked.Clear();
        for (var n = r.Count(1 << 16); n > 0; n--)
            _questAsked.Add(r.Int());
        _treatedBy.Clear();
        for (var n = r.Count(1 << 16); n > 0; n--)
            _treatedBy[r.Entity()] = r.Entity();
        _wentWithout.Clear();
        for (var n = r.Count(1 << 16); n > 0; n--)
        {
            var who = r.Entity();
            var kind = r.Def<ServiceDef>().Kind;
            _wentWithout[(who, kind)] = r.Long();
        }
        _orders.Clear();
        for (var n = r.Count(1 << 16); n > 0; n--)
        {
            var e = new Order
            {
                Id = r.Int(), Kind = r.Def<ServiceDef>(), Poster = r.Entity(), PosterName = r.String(), Text = r.String(),
                Where = r.Entity(), Subject = r.Entity(), Price = r.Int(), Worth = r.Int(), PostedTick = r.Long(), Deadline = r.Long(),
                Assignee = r.Entity(), AssigneeName = r.String(),
            };
            var status = r.Byte();
            if (!Enum.IsDefined((OrderStatus)status))
                throw new Saving.SaveException($"Corrupt save: order status {status}");
            e.Status = (OrderStatus)status;
            e.ClosedTick = r.Long();
            e.LastAsked = r.Long();
            if (r.Version >= 21) // cravings came with format 21
                e.Craving = r.Int() is var c && Enum.IsDefined((Work.FlavourKind)c) && c >= 0 ? (Work.FlavourKind)c : null;
            if (r.Version >= 22)
                e.WantsHouse = r.Bool();
            if (r.Version >= 24 && r.String() is { Length: > 0 } dishName && sim.Defs.TryThing(dishName, out var dishDef) && dishDef.Menu != null)
                e.Dish = dishDef;
            for (var k = r.Count(1 << 16); k > 0; k--)
                e.KnownBy.Add(r.Entity());
            _orders.Add(e);
        }
    }
}

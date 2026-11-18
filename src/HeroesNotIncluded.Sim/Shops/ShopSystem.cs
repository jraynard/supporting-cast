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

    /// <summary>
    /// Adventurers on their way to buy a piece off a wares shelf (<see cref="ShelfBuyJob"/>): the
    /// piece, the price they agreed to and what it was worth to them. They take it down and pay for
    /// it themselves; nobody needs to serve them.
    /// </summary>
    private readonly Dictionary<Entity, (Entity Item, int Price, int Worth)> _browsing = [];

    /// <summary>The piece this adventurer's on their way to buy off a shelf, if they are (<see cref="ShelfBuyJob"/>).</summary>
    public (Entity Item, int Price, int Worth)? BrowsingFor(Entity who) => _browsing.TryGetValue(who, out var b) ? b : null;

    /// <summary>They've gone for it (or given it up): no longer on their way to it.</summary>
    public void DoneBrowsing(Entity who) => _browsing.Remove(who);

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
            else if (e.Kind.Kind == ServiceKind.Goods && e.Wants == null && (!sim.World.IsAlive(e.Subject) || !sim.World.Has<ForSale>(e.Subject)))
                Close(sim, e, OrderStatus.Cancelled); // sold to someone else, or taken off sale
            else if (e.Wants != null && !Goods.WouldUpgrade(sim, e.Poster, e.Wants, e.WantsMending))
                Close(sim, e, OrderStatus.Cancelled); // bought something as good or better meanwhile
            else if (e.Kind.Kind == ServiceKind.Healing && (e.Mend ? !Healing.LayOnHands.Hurt(sim, e.Poster) : !Untended(sim, e.Poster)))
                Healed(sim, e);
            else if (e.Kind.Kind is ServiceKind.WagonRepair or ServiceKind.WagonJoinery && !Tinkering.Wagons.DamagedFor(sim, e.Subject, e.Kind.Kind))
                WagonMended(sim, e);
            else if (e.Kind.Kind == ServiceKind.Arrows && Carpentry.Fletching.Carried(sim, e.Poster).Count >= Carpentry.Fletching.MostCarried)
                Close(sim, e, OrderStatus.Cancelled); // bought one off the shelf after all
            else if (now >= e.Deadline && StillAtIt(sim, e))
                BearWithIt(sim, e);
            else if (now >= e.Deadline)
                GiveUp(sim, e);
            else if (Hatch.TryCollect(sim, e))
                continue; // took a dish off the hatch
            else if (e.Assignee.IsNull && TimeToAsk(sim, e))
                Announce(sim, e, e.Wants != null ? e.Text : Asking(e.Kind, e.Price, SubjectLabel(sim, e.Subject, e.Kind.Kind)));
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
        foreach (var who in _browsing.Where(kv => !sim.World.IsAlive(kv.Key) || Goods.ShownIn(sim, kv.Value.Item).IsNull).Select(kv => kv.Key).ToList())
            _browsing.Remove(who);
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
            // A traveller's passing through: no quests for them.
            if (!a.PartyDef.Traveller && !SeekingOrSentOnQuest(sim, a.Party) && sim.Round.Phase != Round.RoundPhase.Festival
                && sim.Clock.Tick - a.Arrived >= SimClock.HoursToTicks(QuestAfterHours)
                && Want(sim, who, ServiceKind.Quest, Entity.Null))
                _questAsked.Add(a.Party);
            // Sat with a drink or a meal, they don't order another till it's finished.
            var savouring = Hospitality.Table.IsSavouring(sim, who);
            var hunger = w.Get<Needs>(who).Get(food);
            // In the small hours the town's abed: only a room (till it's late) or a bad hurt seen to.
            if (SmallHours(sim))
            {
                if (w.TryGet<Hospitality.OwedRoom>(who, out var owedNow))
                    AskForTheRightKey(sim, who, owedNow);
                else if (WantsARoom(sim, who, a))
                    Want(sim, who, ServiceKind.Lodging, Entity.Null);
                if (w.TryGet<Health>(who, out var hurt) && hurt.Injuries.Where(i => !i.Tended).Sum(i => i.Damage) >= 8f)
                    Want(sim, who, ServiceKind.Healing, Entity.Null, 1.75f);
                continue;
            }
            // Hungry, or peckish at a mealtime (breakfast, supper): the rushes at the hatch.
            // A lodger at a lake town's inn has their board off its warming shelf, at a mealtime.
            if (!savouring && (hunger < MealBelow || (Mealtime(sim) && hunger < MealtimeBelow)) && !AskForBoard(sim, who))
                Want(sim, who, ServiceKind.Meal, Entity.Null, hunger < food.UrgentBelow ? 1.5f : 1f);
            if (sim.Clock.Tick >= a.NextDrink && !savouring && MayAsk(sim, who, ServiceKind.Drink)
                && !_orders.Exists(e => e.IsOpen && e.Poster == who && e.Kind.Kind == ServiceKind.MixedDrink))
            {
                // Now and then something mixed, at its dearer price; too dear, or not to be had, an ale.
                var fancy = _rand.NextFloat() < Hospitality.DrinkingSystem.NamedDrinkChance && MayAsk(sim, who, ServiceKind.MixedDrink)
                    && Want(sim, who, ServiceKind.MixedDrink, Entity.Null);
                if (!fancy)
                    Want(sim, who, ServiceKind.Drink, Entity.Null);
                w.Get<Adventurer>(who).NextDrink = sim.Clock.Tick
                    + SimClock.HoursToTicks(HoursBetweenDrinks * Adventurers.Tempers.Thirst(Adventurers.Tempers.Of(sim, who)) * _rand.Range(0.75f, 1.25f));
            }
            // The worst-worn piece for each who mends: the smith's, the carpenter's, the tinker's.
            foreach (var mend in Mends)
                if (MostWorn(sim, who, mend) is { IsNull: false } gear)
                    Want(sim, who, mend, gear, WeaponWear.HpFraction(sim, gear) < 0.35f ? 1.5f : 1f);
            if (w.TryGet<Health>(who, out var h) && h.Injuries.Exists(i => !i.Tended))
                Want(sim, who, ServiceKind.Healing, Entity.Null, h.Injuries.Where(i => !i.Tended).Sum(i => i.Damage) >= 8f ? 1.75f : 1f);
            if (Tinkering.Lockwork.LockedBoxOf(sim, who) is { IsNull: false } box)
                Want(sim, who, ServiceKind.Unlock, box);
            // The wheel at the tinker's, the woodwork at the carpenter's: an order for each part that's broken.
            if (Tinkering.Wagons.Of(sim, a.Party) is { IsNull: false } wagon && w.Get<Tinkering.Wagon>(wagon).Owner == who)
                foreach (var part in (ReadOnlySpan<ServiceKind>)[ServiceKind.WagonRepair, ServiceKind.WagonJoinery])
                    if (Tinkering.Wagons.DamagedFor(sim, wagon, part) && !_orders.Exists(e => e.IsOpen && e.Subject == wagon && e.Kind.Kind == part))
                        Want(sim, who, part, wagon);
            if (Tinkering.Curios.UnvaluedOf(sim, who) is { IsNull: false } curio)
                Want(sim, who, ServiceKind.Appraise, curio);
            if (w.TryGet<Hospitality.OwedRoom>(who, out var owed))
                AskForTheRightKey(sim, who, owed);
            else if (WantsARoom(sim, who, a))
                Want(sim, who, ServiceKind.Lodging, Entity.Null);
            ShopForGear(sim, who);
            if (WantsArrows(sim, who, a))
                Want(sim, who, ServiceKind.Arrows, Entity.Null, SeekingOrSentOnQuest(sim, a.Party) ? 1.25f : 1f);
        }
    }

    /// <summary>
    /// An archer with no quiver asks for one aloud when there's none on show to buy (one on a shelf
    /// they'd simply buy, <see cref="ShopForGear"/>): the carpenter's order to fletch. Travellers don't shoot for a living.
    /// </summary>
    private bool WantsArrows(Simulation sim, Entity who, Adventurer a)
    {
        if (a.PartyDef.Traveller || !Carpentry.Fletching.Shoots(sim, who) || Carpentry.Fletching.Carried(sim, who).Count >= Carpentry.Fletching.MostCarried)
            return false;
        var w = sim.World;
        foreach (var (item, _, _) in Goods.OnShow(sim))
            if (Carpentry.Fletching.IsQuiver(sim, item))
                return false;
        return !_orders.Exists(e => e.IsOpen && e.Poster == who && e.Kind.Kind == ServiceKind.Goods && w.IsAlive(e.Subject) && Carpentry.Fletching.IsQuiver(sim, e.Subject));
    }

    /// <summary>
    /// A lodger's board (<see cref="Hospitality.Provisions.BoardAt"/>): at breakfast or supper, with a
    /// room at an inn whose warming shelf has a dish for them, they ask at its bar rather than trek
    /// to the cookhouse. It's in with the room: no charge, served by the innkeeper. True if they asked.
    /// </summary>
    private bool AskForBoard(Simulation sim, Entity who)
    {
        if (!Mealtime(sim) || Hospitality.Provisions.BoardAt(sim, who) is not { IsNull: false } inn || !MayAsk(sim, who, ServiceKind.Meal))
            return false;
        var def = sim.Defs.Service(ServiceKind.Meal);
        Post(sim, who, def, Hospitality.Provisions.BoardAsk, 0, sim.Clock.Tick + SimClock.HoursToTicks(def.PatienceHours), Entity.Null, inn);
        return true;
    }

    /// <summary>Back down from walking in on someone: another key, and they've paid already.</summary>
    private void AskForTheRightKey(Simulation sim, Entity who, Hospitality.OwedRoom owed)
    {
        if (_orders.Exists(e => e.IsOpen && e.Poster == who && e.Kind.Kind == ServiceKind.Lodging) || !sim.World.IsAlive(owed.Inn))
            return;
        var def = sim.Defs.Service(ServiceKind.Lodging);
        Post(sim, who, def, "You gave me someone else's room! Another key, and I'm not paying twice.", 0,
            sim.Clock.Tick + PatienceTicks(sim, who, def), Entity.Null, owed.Inn, def.WorthMax);
    }

    /// <summary>
    /// Gear on show is a luxury: an adventurer keeps back what the work they came for costs, a
    /// quest while the party's not yet sought one and a locked box's opening while they carry one,
    /// so a smith's shelf doesn't leave a party unable to pay for the job or its reward.
    /// </summary>
    private int KeptFromGear(Simulation sim, Entity who)
    {
        if (!sim.World.TryGet<Adventurer>(who, out var a))
            return 0;
        // Staying the night with no room yet: a bed's price isn't spent on gear, in the evening.
        var kept = sim.Clock.HourOfDay >= LodgingFromHour - 2f && a.LeaveTick - sim.Clock.Tick >= SimClock.HoursToTicks(6) && !Hospitality.Lodging.Lodged(sim, who)
            ? SignPrice(sim, ServiceKind.Lodging) : 0;
        if (a.PartyDef.Traveller)
            return kept;
        if (!SeekingOrSentOnQuest(sim, a.Party))
            kept += SignPrice(sim, ServiceKind.Quest);
        if (!Tinkering.Lockwork.LockedBoxOf(sim, who).IsNull)
            kept += SignPrice(sim, ServiceKind.Unlock);
        return kept;
    }

    /// <summary>What the town's shop for a service asks for it, or 0 if it isn't offered.</summary>
    private static int SignPrice(Simulation sim, ServiceKind kind)
    {
        var shop = BuildingFor(sim, sim.Defs.Service(kind).AtRole);
        return !shop.IsNull && ShopPrices.Posted(sim, shop, kind) is { Offered: true } sign ? sign.Price : 0;
    }

    /// <summary>
    /// A traveller's come for a bed: until they have tonight's, they keep back what a room costs
    /// from anything else (a hurt that won't wait aside), so supper and a scrape seen to don't leave
    /// them sleeping under a hedge. Adventurers spend as they please.
    /// </summary>
    private static int KeptForARoom(Simulation sim, Entity who, ServiceKind kind, float urgency)
    {
        if (kind == ServiceKind.Lodging || urgency > 1f || !sim.World.TryGet<Adventurer>(who, out var a) || !a.PartyDef.Traveller
            || Hospitality.Lodging.Lodged(sim, who))
            return 0;
        var inn = BuildingFor(sim, sim.Defs.Service(ServiceKind.Lodging).AtRole);
        return !inn.IsNull && ShopPrices.Posted(sim, inn, ServiceKind.Lodging) is { Offered: true } room ? room.Price : 0;
    }

    /// <summary>Bedtimes fall over this many hours from <see cref="LodgingFromHour"/>, before the town's night (22:00).</summary>
    public const float BedtimeSpread = 3f;

    /// <summary>
    /// The hour this guest wants a room tonight: their own, derived from who they are and the night,
    /// never stored, so the whole party doesn't rush the board at six. The same on every peer.
    /// </summary>
    public static float Bedtime(Simulation sim, Entity who) =>
        LodgingFromHour + BedtimeSpread * (SimRandom.Hash(who.Index, Hospitality.Lodging.Tonight(sim), (uint)sim.Seed ^ 0xBED7_1AE5u) % 1024) / 1024f;

    /// <summary>Evening, staying the night, and no room paid for tonight: they'll want one (another night, if they had one last night).</summary>
    private static bool WantsARoom(Simulation sim, Entity who, Adventurer a)
    {
        var hour = sim.Clock.HourOfDay;
        if (hour is < LodgingFromHour and >= LodgingUntilHour || a.LeaveTick - sim.Clock.Tick < SimClock.HoursToTicks(6))
            return false;
        if (hour >= LodgingFromHour && hour < Bedtime(sim, who))
            return false; // not tired yet: each turns in at an hour of their own

        if (Hospitality.Lodging.Lodged(sim, who) || sim.World.Has<Hospitality.WalkIn>(who))
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
        // Rooms with keys are let by handing over the key (Received): only another night in the
        // room they hold the key to is settled here.
        if (kept.IsNull && Hospitality.InnRooms.Board(sim, e.Where) is { IsNull: false })
            return;
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

    /// <summary>
    /// A guest who asked for a room is handed a key: that's their room tonight, whichever it is, and
    /// they pay. Someone else's (it's let tonight), and they go up and walk in on them
    /// (<see cref="Hospitality.WalkInJob"/>). Owed a room for a wrong key before, they hand that one
    /// back and don't pay again. False if the key opens nothing here.
    /// </summary>
    private bool TakeKey(Simulation sim, Order e, Entity giver, Entity key)
    {
        var w = sim.World;
        var seed = w.Get<Tinkering.Key>(key).Seed;
        var bed = Hospitality.InnRooms.BedFor(sim, seed, e.Poster);
        if (bed.IsNull || Buildings.Of(sim, bed) != e.Where)
            return false;
        // The inn keeps a book: they want their name in it before they'll take a key.
        if (!Hospitality.Ledger.SignedIn(sim, Hospitality.InnRooms.Board(sim, e.Where), e.Poster))
        {
            Inventory.Stow(sim, giver, key);
            sim.Speech.Say(sim, e.Poster, "Aren't you going to write me in?", Volume.Say);
            return true;
        }
        Inventory.Stow(sim, e.Poster, key);
        if (w.TryGet<Hospitality.OwedRoom>(e.Poster, out var owed))
        {
            w.Remove<Hospitality.OwedRoom>(e.Poster);
            var wrong = Hospitality.InnRooms.KeyOn(sim, e.Poster, owed.WrongBed);
            if (!wrong.IsNull && wrong != key)
                Inventory.Stow(sim, giver, Inventory.TakeFromPerson(sim, e.Poster, wrong, 1));
        }
        var number = Hospitality.InnRooms.Number(sim, bed);
        if (!Hospitality.InnRooms.LodgedIn(sim, seed, e.Poster).IsNull)
        {
            // They don't know it yet: up they go.
            if (w.Has<Hospitality.WalkIn>(e.Poster))
                w.Remove<Hospitality.WalkIn>(e.Poster);
            w.Add(e.Poster, new Hospitality.WalkIn { Bed = bed });
            if (w.Has<BedOwner>(e.Poster))
                w.Remove<BedOwner>(e.Poster);
            if (w.Has<Hospitality.LodgeStay>(e.Poster))
                w.Remove<Hospitality.LodgeStay>(e.Poster);
        }
        else
        {
            Hospitality.Lodging.Let(sim, e.Poster, bed);
            if (Hospitality.Lodging.IsUnmade(sim, bed))
            {
                if (w.Has<Adventurer>(e.Poster))
                    w.Get<Adventurer>(e.Poster).Satisfaction -= UnmadeMood;
                sim.Speech.Say(sim, e.Poster, "Will the bed be made up by the time I turn in?", Volume.Say);
            }
            Hospitality.RoomChores.Grumble(sim, e.Poster, bed);
        }
        sim.Speech.Say(sim, giver, $"Room {number}, up the stairs.{(ShopName.Of(sim, e.Where) is { } called ? $" Welcome to {called}." : "")}", Volume.Say);
        Complete(sim, e, giver);
        return true;
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
        var unready = Entity.Null;
        var keyed = !Hospitality.InnRooms.Board(sim, building).IsNull;
        foreach (var part in Buildings.Parts(sim, building))
            if (w.Get<Thing>(part).Def.Building is { IsBed: true } && !w.Has<Construction>(part) && !taken.Contains(part)
                && (!keyed || !Hospitality.InnRooms.OnBoard(sim, building, Hospitality.InnRooms.SeedOf(sim, part)).IsNull))
            {
                if (Hospitality.RoomChores.Ready(sim, part))
                    return part;
                if (!w.Has<Hospitality.Unmade>(part))
                {
                    if (unready.IsNull)
                        unready = part;
                    continue;
                }
                if (unmade.IsNull)
                    unmade = part;
            }
        return unready.IsNull ? unmade : unready;
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
        // A lake town's inn with nothing on its warming shelf: a room with no board to it.
        if (kind == ServiceKind.Lodging && Hospitality.Provisions.ShelfBare(sim))
            worth = (int)MathF.Round(worth * Hospitality.Provisions.BareShelfWorth);
        var spare = Math.Max(0, Coin.Count(sim, who) - Promised(who) - KeptForARoom(sim, who, kind, urgency));
        // The box pays for its own opening: they know what's in it, and they pay once it's open.
        if (kind == ServiceKind.Unlock && sim.World.TryGet<Strongbox>(subject, out var box))
            spare += box.Silver;
        var (shop, price, cheapest) = ChooseShop(sim, who, def, Math.Min(worth, spare));
        // Somewhere offers it, but nowhere they know the way to: they ask.
        if (cheapest == Lost)
        {
            sim.Directions.Ask(sim, who, sim.World.Get<PresetBuilding>(BuildingFor(sim, def.AtRole)).Def, def.Label);
            return false;
        }
        if (price < 0)
        {
            _wentWithout[(who, kind)] = sim.Clock.Tick;
            if (cheapest >= 0)
                TooDear(sim, who, def, cheapest, worth, spare, SubjectLabel(sim, subject, kind) ?? WoundLabel(sim, who));
            return false;
        }
        var text = Asking(def, price, SubjectLabel(sim, subject, kind) ?? WoundLabel(sim, who));
        // A meal: they may ask for a dish on the menu by name (at its price), or the house dish, or
        // say what they fancy, in the same breath (Shops.Menu, Hospitality.Tastes).
        Work.FlavourKind? craving = null;
        var house = false;
        ThingDef? named = null;
        if (kind == ServiceKind.Meal && sim.World.Has<Adventurer>(who) && Menu.Asked(sim, shop) is { Count: > 0 } menu
            && _rand.NextFloat() < (menu[0].Dish.Menu!.Morning ? Menu.BreakfastAskChance : Menu.AskChance))
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
        Hello(sim, who);
        var order = Post(sim, who, def, text, price, sim.Clock.Tick + PatienceTicks(sim, who, def), subject, shop, worth, craving);
        order.WantsHouse = house;
        order.Dish = named;
        return true;
    }

    /// <summary>
    /// A party's first order since they came (or came back) to town: one of them says hello for
    /// all of them first, in the party's manner (the leader, by temper). Nothing kept: the orders
    /// they've placed since they came are the memory.
    /// </summary>
    private void Hello(Simulation sim, Entity who)
    {
        var w = sim.World;
        if (!w.TryGet<Adventurer>(who, out var a) || a.PartyDef.Traveller)
            return;
        foreach (var o in _orders)
            if (o.PostedTick >= a.Arrived && w.TryGet<Adventurer>(o.Poster, out var them) && them.Party == a.Party)
                return;
        Speech.Barks.Say(sim, who, "adv_hello", Volume.Say, null, who: a.PartyDef.Label);
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
        if (w.TryGet<Trade>(who, out var trade) && w.TryGet<PresetBuilding>(shop, out var b) && trade.Def.WorksAt(b.Def) && trade.Def.Serving(kind))
            return "you'd be serving yourself";
        if (_orders.Exists(e => e.IsOpen && e.Poster == who && e.Kind.Kind == kind))
            return $"you've ordered {def.Label} already";
        if (ShopPrices.Posted(sim, shop, kind) is not { Offered: true } sign)
            return $"there's no {def.Label} to be had here";
        var spare = Math.Max(0, Coin.Count(sim, who) - Promised(who));
        if (sign.Price > spare)
            return $"{def.Label} is {sign.Price} silver, and you have {spare} to spare";
        if (kind == ServiceKind.Healing && !Healing.LayOnHands.Hurt(sim, who))
            return "you've nothing that wants healing";
        var order = Post(sim, who, def, Asking(def, sign.Price, null), sign.Price, sim.Clock.Tick + PatienceTicks(sim, who, def), Entity.Null, shop, sign.Price);
        order.Mend = kind == ServiceKind.Healing;
        // A bowl already up and nobody ahead: paid for and in hand at once.
        Hatch.TryCollect(sim, order);
        return null;
    }

    /// <summary>A player's open order of a kind, if they have one.</summary>
    public Order? OpenOrderOf(Entity who, ServiceKind kind) => _orders.Find(e => e.IsOpen && e.Poster == who && e.Kind.Kind == kind);

    /// <summary>
    /// An adventurer looks over the gear on show in the town's shops (<see cref="Goods.OnShow"/>)
    /// for a piece they'd be better off with. Of those within what it's worth to them (per tier,
    /// times how well it's made) and what they can spare, they go for the best, then the cheapest,
    /// and buy it off the shelf themselves (<see cref="ShelfBuyJob"/>, <see cref="BoughtOffShelf"/>).
    /// If the only pieces they'd want are too dear, they say so and stop looking for a while. If
    /// nothing on show would do, they may ask a shop to make one, by name (<see cref="AskToMake"/>);
    /// asked already, they buy the first one put on show.
    /// </summary>
    private void ShopForGear(Simulation sim, Entity who)
    {
        if (_browsing.ContainsKey(who))
            return;
        var w = sim.World;
        var def = sim.Defs.Service(ServiceKind.Goods);
        var spend = w.Get<Adventurer>(who).Kind?.Spend ?? 1f;
        // What they've offered for a piece they asked to be made isn't spent yet: something else on show may do instead.
        var asked = _orders.Find(e => e.IsOpen && e.Poster == who && e.Kind.Kind == ServiceKind.Goods && e.Wants != null);
        var spare = Math.Max(0, Coin.Count(sim, who) - Promised(who) - KeptFromGear(sim, who));
        // Asked a shop to make one: the first that's put up on show they come and buy, at its price if they can.
        // Waiting on it, they'll still buy something else that's on show.
        if (asked != null)
        {
            foreach (var (item, _, price) in Goods.OnShow(sim))
                if (Goods.Fills(sim, asked, item) && !Spoken(item) && price <= Math.Min(Math.Max(asked.Worth, asked.Price), spare))
                {
                    _browsing[who] = (item, price, asked.Worth);
                    return;
                }
            if (_wentWithout.TryGetValue((who, ServiceKind.Goods), out var at) && at > sim.Clock.Tick - SimClock.HoursToTicks(CooldownHours))
                return;
        }
        else if (!MayAsk(sim, who, ServiceKind.Goods))
            return;
        int? roll = null;
        (Entity Item, Entity Shop, int Price, int Worth, int Tier)? best = null, dear = null;
        foreach (var (item, shop, price) in Goods.OnShow(sim))
        {
            if (Spoken(item) || !Goods.IsUpgrade(sim, who, item))
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
            _browsing[who] = (buy.Item, buy.Price, buy.Worth);
        else if (dear is { } no)
        {
            _wentWithout[(who, ServiceKind.Goods)] = sim.Clock.Tick;
            TooDear(sim, who, def, no.Price, no.Worth, spare, w.Get<Thing>(no.Item).Label);
        }
        else if (asked == null)
            AskToMake(sim, who, def, spend, spare);

        // Someone else is on their way to it, or waiting to be handed it.
        bool Spoken(Entity item) =>
            _browsing.Values.Any(b => b.Item == item) || _orders.Exists(o => o.IsOpen && o.Subject == item);
    }

    /// <summary>The chance, each hour an adventurer finds nothing on show for them, that they ask for a piece to be made.</summary>
    public const float AskToMakeChance = 0.6f;

    /// <summary>They've been in town this long before they'd ask a shop to make them something: time to look round first.</summary>
    public const float AskToMakeAfterHours = 0.5f;

    /// <summary>Game hours they'll wait for a piece asked for by name: it has to be made.</summary>
    public const float AskToMakeHours = 10f;

    /// <summary>
    /// Nothing on show would do: they may ask a shop a player keeps to make them a piece, naming it
    /// ("Could you make me an iron sword?"), at what it's worth to them. One ask at a time in a party,
    /// and not every time they look. A shop that has it hands it over (or puts it on show, and they
    /// buy it, <see cref="ShopForGear"/>). Lapsed, they cool off for a while before asking again.
    /// </summary>
    private void AskToMake(Simulation sim, Entity who, ServiceDef def, float spend, int spare)
    {
        var w = sim.World;
        var now = sim.Clock.Tick;
        var a = w.Get<Adventurer>(who);
        // Looked round first, then now and then (AskToMakeChance an hour, checked each pass): one ask at a time in a party.
        if (now - a.Arrived < SimClock.HoursToTicks(AskToMakeAfterHours)
            || _orders.Exists(e => e.IsOpen && e.Kind.Kind == ServiceKind.Goods && e.Wants != null
                && w.TryGet<Adventurer>(e.Poster, out var other) && other.Party == a.Party)
            || _rand.NextFloat() >= AskToMakeChance * Interval / SimClock.TicksPerHour)
            return;
        var wishes = Goods.Wishes(sim, who);
        if (wishes.Count == 0)
            return;
        var wish = wishes[_rand.NextInt(wishes.Count)];
        var worth = (int)MathF.Round(_rand.Range(def.WorthMin, def.WorthMax + 1) * Math.Max(1, wish.Def.GearTier) * Goods.WorthFactor(wish.Def) * spend);
        var price = Math.Min(worth, spare);
        if (price < 1 || price * 2 < worth)
            return; // they couldn't pay what it's worth
        var label = Goods.WishLabel(wish.Def, wish.Mending);
        var text = def.AskLines.Count == 0 ? $"Could you make me a {label}? {price} silver."
            : string.Format(def.AskLines[_rand.NextInt(def.AskLines.Count)], price, label);
        Post(sim, who, def, text, price, now + SimClock.HoursToTicks(AskToMakeHours), where: wish.Shop, worth: worth,
            wants: wish.Def, wantsMending: wish.Mending);
    }

    /// <summary>
    /// An adventurer took a piece down off a wares shelf and pays its seller for it, wherever they
    /// are (the shelf is honest dealing): the piece is theirs, and it goes on the books as an order
    /// done by the seller, a bargain counted as one. If it fills a piece they'd asked for by name,
    /// that's the order done.
    /// </summary>
    public void BoughtOffShelf(Simulation sim, Entity buyer, Entity item, int price, int worth)
    {
        var w = sim.World;
        if (!w.TryGet<ForSale>(item, out var sale) || !w.IsAlive(sale.Seller))
            return;
        var seller = sale.Seller;
        var shop = w.TryGet<Stored>(item, out var stored) ? Goods.SellsAt(sim, seller, stored.In) : Entity.Null;
        var label = sim.LabelOf(item);
        var down = sim.TakeFromStack(item, 1);
        Goods.TakeUp(sim, buyer, down);
        var asked = _orders.Find(e => e.IsOpen && e.Poster == buyer && e.Kind.Kind == ServiceKind.Goods && Goods.Fills(sim, e, down));
        if (asked == null)
        {
            asked = new Order
            {
                Id = _nextId++, Kind = sim.Defs.Service(ServiceKind.Goods), Poster = buyer, PosterName = w.Get<Creature>(buyer).Name,
                Text = $"Bought the {label} off the shelf.", Where = shop, Subject = down, Price = price, Worth = worth,
                PostedTick = sim.Clock.Tick, Deadline = sim.Clock.Tick, LastAsked = sim.Clock.Tick,
            };
            _orders.Add(asked);
            Learn(asked, buyer);
        }
        asked.Price = price;
        Learn(asked, seller);
        Complete(sim, asked, seller);
        sim.Messages.Tell(seller, MessageKind.Info, $"{w.Get<Creature>(buyer).Name} bought the {label} off your shelf: {price} silver.", sim.LocationOf(buyer));
    }

    /// <summary>
    /// What a service is worth to this adventurer right now, in silver: rolled in the def's range
    /// (<see cref="ServiceDef.WorthMin"/>), times how free with coin their kind is
    /// (<see cref="AdventurerKindDef.Spend"/>), times how badly they need it.
    /// </summary>
    private int Worth(Simulation sim, Entity who, ServiceDef def, float urgency)
    {
        var spend = sim.World.TryGet<Adventurer>(who, out var a) ? (a.Kind?.Spend ?? 1f) * Adventurers.Tempers.Worth(Adventurers.Tempers.Of(sim, who)) : 1f;
        return Math.Max(0, (int)MathF.Round(_rand.Range(def.WorthMin, def.WorthMax + 1) * spend * urgency));
    }

    /// <summary>How long someone waits for an order of this kind: the service's patience, and an adventurer's temper.</summary>
    private static long PatienceTicks(Simulation sim, Entity who, ServiceDef def) =>
        SimClock.HoursToTicks(def.PatienceHours * (sim.World.Has<Adventurer>(who) ? Adventurers.Tempers.Patience(Adventurers.Tempers.Of(sim, who)) : 1f));

    /// <summary>Silver this person has promised to orders still open (not a piece asked to be made: that's only an offer till it's there).</summary>
    private int Promised(Entity who)
    {
        var sum = 0;
        foreach (var e in _orders)
            if (e.IsOpen && e.Poster == who && e.Wants == null)
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
        if (BuildingFor(sim, def.AtRole).IsNull)
            return def.DefaultPrice <= budget ? (Entity.Null, def.DefaultPrice, def.DefaultPrice) : (Entity.Null, -1, def.DefaultPrice);
        var w = sim.World;
        var from = w.Get<Position>(who).Cell;
        var best = Entity.Null;
        int bestPrice = -1, bestDist = int.MaxValue, cheapest = -1;
        var lost = false;
        foreach (var e in w.Each<PresetBuilding>())
        {
            var b = w.Get<PresetBuilding>(e);
            if (!b.Def.Has(def.AtRole) || b.Demolishing || ShopPrices.Posted(sim, e, def.Kind) is not { Offered: true } sign)
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

    /// <summary>
    /// Whether a party has work in hand: asked and still waiting, or sent off once already. One whose
    /// ask lapsed with nobody answering asks again after <see cref="CooldownHours"/>: a party that asked
    /// at night, while the innkeeper was at the stew, used never to ask again, and there was no
    /// sending them anywhere in the morning.
    /// </summary>
    private bool SeekingOrSentOnQuest(Simulation sim, int party)
    {
        if (!_questAsked.Contains(party))
            return false;
        var lapsed = false;
        foreach (var e in _orders)
        {
            if (e.Kind.Kind != ServiceKind.Quest || !sim.World.TryGet<Adventurer>(e.Poster, out var a) || a.Party != party)
                continue;
            if (e.IsOpen || e.Status == OrderStatus.Done || sim.Clock.Tick - e.ClosedTick < SimClock.HoursToTicks(CooldownHours))
                return true;
            lapsed = true;
        }
        // Closed orders are let go after a while (a sent party's too), so only a lapse seen frees them.
        if (lapsed)
            _questAsked.Remove(party);
        return !lapsed;
    }

    /// <summary>Has nothing of this kind open, didn't just have one close, and didn't just go without one?</summary>
    private bool MayAsk(Simulation sim, Entity who, ServiceKind kind)
    {
        var since = sim.Clock.Tick - SimClock.HoursToTicks(CooldownHours);
        if (_wentWithout.TryGetValue((who, kind), out var at) && at > since)
            return false;
        foreach (var e in _orders)
            if (e.Poster == who && e.Kind.Kind == kind && (e.IsOpen || e.ClosedTick > since)
                && !(kind == ServiceKind.Goods && e.Status == OrderStatus.Done)) // bought one: they may look for the next at once
                return false;
        return true;
    }

    /// <summary>Put an order on the books and say it aloud. Those who hear it clearly know of it.</summary>
    public Order Post(Simulation sim, Entity poster, ServiceDef kind, string text, int price, long deadline, Entity? subject = null,
        Entity? where = null, int worth = 0, Work.FlavourKind? craving = null, ThingDef? wants = null, MendingDef? wantsMending = null)
    {
        var order = new Order
        {
            Craving = craving, Wants = wants, WantsMending = wantsMending,
            Id = _nextId++, Kind = kind, Poster = poster, PosterName = sim.World.Get<Creature>(poster).Name, Text = text,
            Where = where ?? BuildingFor(sim, kind.AtRole), Subject = subject ?? Entity.Null, Price = price,
            Worth = worth, PostedTick = sim.Clock.Tick, Deadline = deadline,
        };
        _orders.Add(order);
        Learn(order, poster);
        // Word of work at a trade's building reaches whoever holds that trade.
        var workplace = sim.World.IsAlive(order.Where) && sim.World.TryGet<PresetBuilding>(order.Where, out var shop) ? shop.Def : null;
        foreach (var e in sim.Creatures())
            if (sim.World.TryGet<Trade>(e, out var trade) && (workplace != null ? trade.Def.WorksAt(workplace) : trade.Def.HasWorkplace && trade.Def.Role == kind.AtRole))
                Learn(order, e);
        // At a shop with a line they say it at the counter (or the hatch), when their turn comes (TimeToAsk).
        // A piece asked for by name isn't waited for in line (it has to be made): said at once.
        if (!WaitsInLine(sim, order) || LineCells(sim, order.Where, Hatch.Takes(sim, order)).Count == 0)
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

    /// <summary>
    /// What it costs a keeper to say "Sorry, we're out": less than leaving them to give up waiting
    /// (1, <see cref="GiveUp"/>), since they're told straight and can go elsewhere.
    /// </summary>
    public const float RefusedMood = 0.5f;

    /// <summary>
    /// Whether <paramref name="who"/> may turn this order away: open, known to them, for something
    /// their trade serves, at their workplace, and with nothing of the asker's in the shop's keeping
    /// (that's handed back first). A shortage, a dry cask, a cold pot: better told than kept waiting.
    /// </summary>
    public static bool MayTurnAway(Simulation sim, Entity who, Order e)
    {
        var w = sim.World;
        if (!e.IsOpen || e.Poster == who || !e.KnownBy.Contains(who) || !w.TryGet<Trade>(who, out var trade) || !trade.Def.HasWorkplace
            || !trade.Def.Serving(e.Kind.Kind) || (!e.Where.IsNull && e.Where != WorkplaceOf(sim, trade.Def)))
            return false;
        return !HandsOver(e.Kind.Kind) || !w.IsAlive(e.Subject) || !w.IsAlive(e.Poster) || Inventory.OnPerson(sim, e.Poster, e.Subject);
    }

    /// <summary>
    /// Whether this order is <paramref name="who"/>'s to take on: something their trade sees to where
    /// they work (an empath overhearing a drink ordered at the inn can't say they'll pour it), a quest
    /// if they broker them, a piece asked for at their own shop, or a lodger's board at an inn with no
    /// kitchen. The same as the bots' and the AI's (<c>BotPlayer.KindsFor</c>, <see cref="ServeJob"/>).
    /// </summary>
    public static bool MayTake(Simulation sim, Entity who, Order e)
    {
        if (!sim.World.TryGet<Trade>(who, out var t))
            return false;
        var trade = t.Def;
        var kind = e.Kind.Kind;
        if (kind == ServiceKind.Quest && trade.Brokers)
            return true;
        if (!trade.HasWorkplace)
            return false;
        if (kind == ServiceKind.Goods)
            return e.Where.IsNull || ShopPrices.Keeps(sim, who, e.Where);
        if (kind == ServiceKind.Meal && trade.Role == TownRole.Tavern && Hospitality.Provisions.Split(sim))
            return true;
        return e.Kind.AtRole == trade.Role && trade.Serving(kind);
    }

    /// <summary>
    /// "Sorry, we're out": the keeper says so aloud and the order is over. The asker takes it better
    /// than being left to wait it out, and won't ask for that again for a while (<see cref="MayAsk"/>).
    /// </summary>
    public bool TurnAway(Simulation sim, Entity who, int id)
    {
        if (Find(id) is not { } e || !MayTurnAway(sim, who, e))
            return false;
        Close(sim, e, OrderStatus.Refused);
        if (e.Assignee.IsNull)
        {
            e.Assignee = who;
            e.AssigneeName = sim.World.Get<Creature>(who).Name;
        }
        Speech.Barks.Say(sim, who, "keeper_out", Volume.Say, "Sorry, {who}, we're out of {thing}.", who: e.PosterName, thing: e.Kind.Label);
        if (sim.World.Has<Adventurer>(e.Poster))
        {
            sim.World.Get<Adventurer>(e.Poster).Satisfaction -= RefusedMood;
            Speech.Barks.Say(sim, e.Poster, "adv_turned_away", Volume.Say, "Out? Fine. I'll go without.", thing: e.Kind.Label);
        }
        return true;
    }

    /// <summary>Someone says they'll see to an order they know of. False if they can't take it.</summary>
    public bool Accept(Simulation sim, Entity who, int id)
    {
        if (Find(id) is not { IsOpen: true } e || e.Poster == who || !e.Assignee.IsNull || !e.KnownBy.Contains(who) || !MayTake(sim, who, e))
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
        if (Hospitality.Provisions.IsBoard(sim, e))
            Speech.Barks.Say(sim, e.Poster, "adv_board", Volume.Say, "That's my board. Much obliged.");
        else if (paid >= e.Price)
            Speech.Barks.Say(sim, e.Poster, "adv_paid", Volume.Say, "There. {n} silver, as promised.", n: paid);
        else
            Speech.Barks.Say(sim, e.Poster, "adv_paid_short", Volume.Say, "Here's {n} silver. It's all I've got.", n: paid);
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
    /// handed over to be seen to (they step aside and wait for it to come back)? The inn's hamper,
    /// sent for from the bar, holds a place only once it's up and nobody's running it over
    /// (<see cref="Hospitality.Provisions.Waits"/>).
    /// </summary>
    public static bool WaitsInLine(Simulation sim, Order e) =>
        e.IsOpen && e.Wants == null && (e.Kind.Kind == ServiceKind.Provisions
            ? Hospitality.Provisions.Waits(sim, e)
            : !(HandsOver(e.Kind.Kind) && !e.Assignee.IsNull && sim.World.IsAlive(e.Subject) && Inventory.OnPerson(sim, e.Assignee, e.Subject)));

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

    /// <summary>The services that mend a worn piece of gear, each at the shop of whoever makes it (<see cref="WeaponWear.MendKind"/>).</summary>
    public static readonly ServiceKind[] Mends = [ServiceKind.Repair, ServiceKind.WoodRepair, ServiceKind.CrossbowRepair];

    /// <summary>A mending order: the piece is handed over, mended, and given back (<see cref="Mends"/>).</summary>
    public static bool IsMend(ServiceKind kind) => kind is ServiceKind.Repair or ServiceKind.WoodRepair or ServiceKind.CrossbowRepair;

    /// <summary>Orders that hand something over to be seen to and given back.</summary>
    public static bool HandsOver(ServiceKind kind) => IsMend(kind) || kind is ServiceKind.Unlock or ServiceKind.Appraise;

    /// <summary>
    /// The order of the customer standing at <paramref name="cell"/> that <paramref name="who"/> can
    /// take with the work key: one whose thing changes hands, asked at a shop they keep, that they
    /// heard asked and nobody's seeing to yet, with the customer in handing reach. Drinks, meals and
    /// healing are served, not taken, so they aren't offered here. Oldest first; null if none.
    /// </summary>
    public static Order? TakeableAt(Simulation sim, Entity who, int3 cell)
    {
        var w = sim.World;
        foreach (var e in sim.Shops.All)
            if (e.IsOpen && HandsOver(e.Kind.Kind) && e.Assignee.IsNull && e.Poster != who && e.KnownBy.Contains(who)
                && w.IsAlive(e.Poster) && w.TryGet<Position>(e.Poster, out var at) && at.Cell == cell
                && ShopPrices.Keeps(sim, who, e.Where) && MayTake(sim, who, e) && Handing.InReach(sim, who, e.Poster))
                return e;
        return null;
    }

    /// <summary>
    /// The open order of <paramref name="poster"/>'s that this item fills: a piece they asked a shop
    /// to make, by name (<see cref="Order.Wants"/>). Null if it isn't what they asked for.
    /// </summary>
    public static Order? AskedFor(Simulation sim, Entity poster, Entity item)
    {
        foreach (var e in sim.Shops.All)
            if (e.IsOpen && e.Poster == poster && e.Kind.Kind == ServiceKind.Goods && Goods.Fills(sim, e, item))
                return e;
        return null;
    }

    /// <summary>
    /// An order a player can be walked over to see to (<see cref="SeeToCustomerCommand"/>): a thing
    /// that changes hands, or a drink poured at the bar and carried to wherever they've sat down.
    /// </summary>
    public static bool Delivers(ServiceKind kind) => HandsOver(kind) || kind is ServiceKind.Drink or ServiceKind.MixedDrink;

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

    /// <summary>
    /// Someone saw to a patient's wounds (an empath drew one, a healer bandaged them): theirs to be paid for it.
    /// An adventurer remembers who it was, for their party's tale if they go on a quest (<see cref="TendedBy"/>).
    /// </summary>
    public void Treated(Simulation sim, Entity patient, Entity by)
    {
        _treatedBy[patient] = by;
        var w = sim.World;
        if (by == patient || !w.Has<Adventurer>(patient) || !w.IsAlive(by))
            return;
        if (!w.TryGet<TendedBy>(patient, out var tended))
            w.Add(patient, tended = new TendedBy());
        tended.Add(by);
    }

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

    /// <summary>
    /// Word of the food (docs/DESIGN.md, "Cook"): fine meals served, and the house dish, get the
    /// inn's table talked of on the road; a poor one, the other way. The more it's talked of, the
    /// sooner the next party comes (<see cref="Adventurers.PartySystem"/>), and the talk fades as
    /// each new party rides in, so it has to be kept up.
    /// </summary>
    public float Fare { get; private set; }

    public const float FareMost = 20f;

    /// <summary>How much sooner parties come at the most, for a table everyone's talking of.</summary>
    public const float FareSooner = 0.3f;

    /// <summary>How much of the talk is still going when the next party rides in.</summary>
    public const float FareKept = 0.85f;

    internal void HeardOfTheFood(float by) => Fare = Math.Clamp(Fare + by, 0f, FareMost);

    internal void TalkFades() => Fare *= FareKept;

    /// <summary>The share by which the next party's coming is brought forward, 0..<see cref="FareSooner"/>.</summary>
    public float FareFactor => FareSooner * Fare / FareMost;

    /// <summary>What's said of the inn's table on the road, for the sign.</summary>
    public static string FareWord(float fare) => fare switch
    {
        < 2f => "Nobody's said much about the food.",
        < 6f => "The food's spoken of, here and there.",
        < 12f => "Travellers talk of the inn's table.",
        _ => "The inn's table is famous up and down the road.",
    };

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

    /// <summary>What a piece asked for by name and never made costs in their opinion of the town.</summary>
    public const float AskLapsedMood = 0.25f;

    private static void GiveUp(Simulation sim, Order e)
    {
        Close(sim, e, OrderStatus.Expired);
        LostTheirGear(sim, e);
        // Only peckish (it was a mealtime): they shrug it off, more or less.
        if (e.Kind.Kind == ServiceKind.Meal && sim.World.TryGet<Adventurer>(e.Poster, out _) && sim.World.TryGet<Needs>(e.Poster, out var needs)
            && needs.Get(sim.Defs.Need("Food")) >= MealBelow)
        {
            sim.World.Get<Adventurer>(e.Poster).Satisfaction -= PeckishGiveUpMood;
            Speech.Barks.Say(sim, e.Poster, "adv_give_up_peckish", Volume.Say, "Never mind the food. I'll eat later.");
            return;
        }
        // A piece asked for by name: they knew it had to be made. A shrug, not a grudge.
        if (e.Wants != null && sim.World.Has<Adventurer>(e.Poster))
        {
            sim.World.Get<Adventurer>(e.Poster).Satisfaction -= AskLapsedMood;
            Speech.Barks.Say(sim, e.Poster, "adv_ask_lapsed", Volume.Say, "No {thing} yet, then. I'll make do.", thing: Goods.WishLabel(e.Wants, e.WantsMending));
            return;
        }
        if (sim.World.Has<Adventurer>(e.Poster))
        {
            sim.World.Get<Adventurer>(e.Poster).Satisfaction -= 1f;
            Speech.Barks.Say(sim, e.Poster, "adv_give_up", Volume.Say, "Forget it. So much for {thing} in this town.", thing: e.Kind.Label);
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
        if (Hospitality.RoomChores.HandedBack(sim, giver, to, item))
            return;
        var repair = _orders.Find(e => e.IsOpen && e.Poster == to && IsMend(e.Kind.Kind) && e.Subject == item);
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
        // A quiver for an archer who asked for arrows: theirs, and paid for.
        var arrows = Carpentry.Fletching.IsQuiver(sim, item) ? _orders.Find(e => e.IsOpen && e.Poster == to && e.Kind.Kind == ServiceKind.Arrows) : null;
        if (arrows != null)
        {
            Goods.TakeUp(sim, to, item);
            Complete(sim, arrows, giver);
            return;
        }
        var bought = _orders.Find(e => e.IsOpen && e.Poster == to && e.Kind.Kind == ServiceKind.Goods && (e.Subject == item || Goods.Fills(sim, e, item)));
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
        var room = _orders.Find(e => e.IsOpen && e.Poster == to && e.Kind.Kind == ServiceKind.Lodging);
        if (room != null && Hospitality.InnRooms.IsRoomKey(sim, item) && TakeKey(sim, room, giver, item))
            return;
        // The inn's hamper, handed to the keeper who ordered it.
        if (Hospitality.Provisions.Received(sim, giver, to, item, this))
            return;
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
                // Word of the food: they'll talk of it on the road.
                HeardOfTheFood(q switch { Quality.Masterwork => 2f, Quality.Fine => 1f, Quality.Crude => -1f, _ => 0f } + (dish?.House == true ? 0.5f : 0f));
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
        var by = Tinkering.Wagons.IsWagon(sim, e.Subject)
            && (e.Kind.Kind == ServiceKind.WagonJoinery ? w.Get<Tinkering.Wagon>(e.Subject).JoinedBy : w.Get<Tinkering.Wagon>(e.Subject).MendedBy) is var m && w.IsAlive(m) ? m
            : !e.Assignee.IsNull && w.IsAlive(e.Assignee) ? e.Assignee : Entity.Null;
        if (by.IsNull)
        {
            Close(sim, e, OrderStatus.Cancelled);
            return;
        }
        sim.Speech.Say(sim, e.Poster, Tinkering.Wagons.Damaged(sim, e.Subject) ? "That's that part seen to. Good work." : "The wagon runs like new. Good work.", Volume.Say);
        Complete(sim, e, by);
    }

    /// <summary>The weapon or armor most in need of mending (<see cref="WeaponWear.NeedsMending"/>); Entity.Null if none.</summary>
    public static Entity MostWorn(Simulation sim, Entity who, ServiceKind mend = ServiceKind.Repair)
    {
        var w = sim.World;
        var worst = Entity.Null;
        var worstHp = 2f;
        void Consider(Entity item)
        {
            if (!w.IsAlive(item) || WeaponWear.MendKind(sim, item) != mend)
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

    private static string? SubjectLabel(Simulation sim, Entity subject, ServiceKind? kind = null) =>
        sim.World.TryGet<Tinkering.Wagon>(subject, out var wagon)
            ? Tinkering.Wagons.DamageLabel(kind is { } k && Tinkering.Wagons.PartsFor(k) is var parts and not Tinkering.WagonDamage.None ? wagon.Damage & parts : wagon.Damage)
        : sim.World.IsAlive(subject) && sim.World.TryGet<Thing>(subject, out var t) ? t.Label : null;

    private static string? WoundLabel(Simulation sim, Entity who)
    {
        if (!sim.World.TryGet<Health>(who, out var h))
            return null;
        var wound = h.Injuries.Find(i => !i.Tended);
        return wound == null ? null : h.Body.Parts[wound.Part].Label;
    }

    /// <summary>
    /// The first standing building that is this to the town (<see cref="TownRole"/>: the kitchen,
    /// the tavern), Entity.Null if it has none. Where a trade works and a service is served.
    /// </summary>
    public static Entity BuildingFor(Simulation sim, TownRole role)
    {
        if (role == TownRole.None)
            return Entity.Null;
        foreach (var e in sim.World.Each<PresetBuilding>())
        {
            var b = sim.World.Get<PresetBuilding>(e);
            if (b.Def.Has(role) && !b.Demolishing)
                return e;
        }
        return Entity.Null;
    }

    /// <summary>The building a trade works from in this town, Entity.Null if none.</summary>
    public static Entity WorkplaceOf(Simulation sim, TradeDef trade) => BuildingFor(sim, trade.Role);

    /// <summary>The building someone's trade works from, Entity.Null if they have none or the town hasn't one.</summary>
    public static Entity WorkplaceOf(Simulation sim, Entity who) =>
        sim.World.TryGet<Trade>(who, out var t) ? BuildingFor(sim, t.Def.Role) : Entity.Null;

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
            w.Bool(e.Mend);
            w.String(e.Wants?.Name ?? "");
            w.String(e.WantsMending?.Name ?? "");
            w.Int(e.KnownBy.Count);
            foreach (var k in e.KnownBy)
                w.Entity(k);
        }
        w.Float(Fare);
        var browsing = _browsing.OrderBy(kv => kv.Key.Index).ToList();
        w.Int(browsing.Count);
        foreach (var (who, (item, price, worth)) in browsing)
        {
            w.Entity(who);
            w.Entity(item);
            w.Int(price);
            w.Int(worth);
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
            if (r.Version >= 26)
                e.Mend = r.Bool();
            if (r.Version >= 61) // goods asked for by name (format 61)
            {
                if (r.String() is { Length: > 0 } wants && sim.Defs.TryThing(wants, out var wantsDef) && Goods.IsGear(wantsDef))
                    e.Wants = wantsDef;
                if (r.String() is { Length: > 0 } mending && sim.Defs.AllMendings.Find(m => m.Name == mending) is { } m)
                    e.WantsMending = m;
            }
            for (var k = r.Count(1 << 16); k > 0; k--)
                e.KnownBy.Add(r.Entity());
            _orders.Add(e);
        }
        Fare = r.Version >= 30 ? Math.Clamp(r.Float(), 0f, FareMost) : 0f; // word of the food came with format 30
        _browsing.Clear();
        if (r.Version >= 61) // adventurers buying off the shelf themselves (format 61)
            for (var n = r.Count(1 << 16); n > 0; n--)
                _browsing[r.Entity()] = (r.Entity(), Math.Max(0, r.Int()), Math.Max(0, r.Int()));
    }
}

using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Combat;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Round;
using SupportingCast.Sim.Speech;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Shops;

/// <summary>
/// Every <see cref="Order"/> in the round. Adventurers ask aloud for what they want (a meal when
/// hungry, a mend when their gear is worn, a healer when hurt, and once per party a quest), and
/// keep asking while nobody takes it on. Done, they pay in silver and think better of the town;
/// left waiting, they give up and think worse (<see cref="Adventurer.Satisfaction"/>). Only those
/// who heard an order asked know of it. Players asking each other go through <c>Favours.FavourBoard</c>.
/// </summary>
public sealed class ShopSystem(ulong seed) : ISimSystem, Saving.ISaveState
{
    public string Name => "Shops";
    public int Interval => SimClock.TicksPerHour / 12;

    /// <summary>Hunger (the Food need) below which an adventurer asks for a meal.</summary>
    public const float MealBelow = 0.6f;

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

    /// <summary>How long closed orders stay on the books (for the list and the cooldown).</summary>
    public const int KeepClosedTicks = SimClock.TicksPerDay;

    private readonly SimRandom _rand = new(seed ^ 0xE77A_4D5u);
    private readonly List<Order> _orders = [];
    private readonly HashSet<int> _questAsked = [];

    /// <summary>Who last saw to each patient's wounds (drawn or bandaged), to be paid for a Healing order.</summary>
    private readonly Dictionary<Entity, Entity> _treatedBy = [];
    private int _nextId = 1;

    /// <summary>Every order still on the books, oldest first. Game code reads them through <c>Perception</c>.</summary>
    public IReadOnlyList<Order> All => _orders;

    public Order? Find(int id) => _orders.Find(e => e.Id == id);

    public void Update(Simulation sim)
    {
        if (sim.Round.Phase != RoundPhase.Playing)
            return;
        var now = sim.Clock.Tick;
        foreach (var e in _orders)
        {
            if (!e.IsOpen)
                continue;
            if (!sim.World.IsAlive(e.Poster) || sim.World.Get<Creature>(e.Poster).Dead || sim.World.Has<Leaving>(e.Poster))
            {
                Close(sim, e, OrderStatus.Cancelled);
                LostTheirGear(sim, e);
            }
            else if (e.Kind.Kind == ServiceKind.Healing && !Untended(sim, e.Poster))
                Healed(sim, e);
            else if (now >= e.Deadline)
                GiveUp(sim, e);
            else if (e.Assignee.IsNull && now - e.LastAsked >= SimClock.HoursToTicks(RepeatHours))
                Announce(sim, e, Line(e.Kind, e.Price, SubjectLabel(sim, e.Subject)));
            else
            {
                TryHandOver(sim, e);
                TryLetARoom(sim, e);
            }
        }
        _orders.RemoveAll(e => !e.IsOpen && now - e.ClosedTick > KeepClosedTicks);
        foreach (var gone in _treatedBy.Keys.Where(k => !sim.World.IsAlive(k)).ToList())
            _treatedBy.Remove(gone);
        AskForWhatTheyWant(sim);
    }

    /// <summary>Each adventurer in town asks for whatever they lack and haven't already asked for.</summary>
    private void AskForWhatTheyWant(Simulation sim)
    {
        var w = sim.World;
        var food = sim.Defs.Need("Food");
        foreach (var who in sim.Creatures())
        {
            if (!w.TryGet<Adventurer>(who, out var a) || w.Has<Leaving>(who) || !SpeechSystem.CanAct(sim, who))
                continue;
            // Work comes after the road's been washed down: fed, served, mended, then a quest.
            if (!_questAsked.Contains(a.Party) && sim.Clock.Tick - a.Arrived >= SimClock.HoursToTicks(QuestAfterHours))
            {
                _questAsked.Add(a.Party);
                PostFor(sim, who, ServiceKind.Quest, Entity.Null);
            }
            if (w.Get<Needs>(who).Get(food) < MealBelow)
                PostFor(sim, who, ServiceKind.Meal, Entity.Null);
            if (sim.Clock.Tick >= a.NextDrink && MayAsk(sim, who, ServiceKind.Drink))
            {
                PostFor(sim, who, ServiceKind.Drink, Entity.Null);
                w.Get<Adventurer>(who).NextDrink = sim.Clock.Tick + SimClock.HoursToTicks(HoursBetweenDrinks * _rand.Range(0.75f, 1.25f));
            }
            if (MostWorn(sim, who) is { IsNull: false } gear)
                PostFor(sim, who, ServiceKind.Repair, gear);
            if (w.TryGet<Health>(who, out var h) && h.Injuries.Exists(i => !i.Tended))
                PostFor(sim, who, ServiceKind.Healing, Entity.Null);
            if (Locks.Lockwork.LockedBoxOf(sim, who) is { IsNull: false } box)
                PostFor(sim, who, ServiceKind.Unlock, box);
            if (WantsARoom(sim, who, a))
                PostFor(sim, who, ServiceKind.Lodging, Entity.Null);
        }
    }

    /// <summary>Evening, staying the night, and nowhere to sleep yet: they'll want a room.</summary>
    private static bool WantsARoom(Simulation sim, Entity who, Adventurer a)
    {
        var hour = sim.Clock.HourOfDay;
        if (hour is < LodgingFromHour and >= LodgingUntilHour || a.LeaveTick - sim.Clock.Tick < SimClock.HoursToTicks(6))
            return false;
        return !(sim.World.TryGet<BedOwner>(who, out var own) && sim.World.IsAlive(own.Bed));
    }

    /// <summary>
    /// Whoever took on a room for someone shows them up to it, once they're in reach: a free let
    /// bed is theirs for their stay (<see cref="BedOwner"/>), and they pay. With every room taken,
    /// they're told so, and wait or give up.
    /// </summary>
    private void TryLetARoom(Simulation sim, Order e)
    {
        var w = sim.World;
        if (e.Kind.Kind != ServiceKind.Lodging || e.Assignee.IsNull || !Handing.InReach(sim, e.Assignee, e.Poster))
            return;
        var bed = FreeLetBed(sim, e.Where);
        if (bed.IsNull)
            return;
        w.Remove<BedOwner>(e.Poster);
        w.Add(e.Poster, new BedOwner { Bed = bed });
        sim.Speech.Say(sim, e.Assignee, "Up the stairs. Yours till you move on.", Volume.Say);
        Complete(sim, e, e.Assignee);
    }

    /// <summary>A bed in a building that lets them (<see cref="BuildingDef.LetsBeds"/>) nobody living has been given; Entity.Null if they're all taken.</summary>
    public static Entity FreeLetBed(Simulation sim, Entity building)
    {
        var w = sim.World;
        if (!w.IsAlive(building) || !w.TryGet<PresetBuilding>(building, out var b) || !b.Def.LetsBeds)
            return Entity.Null;
        var taken = new HashSet<Entity>();
        foreach (var o in w.Each<BedOwner>())
            taken.Add(w.Get<BedOwner>(o).Bed);
        foreach (var part in Buildings.Parts(sim, building))
            if (w.Get<Thing>(part).Def.Building is { IsBed: true } && !w.Has<Construction>(part) && !taken.Contains(part))
                return part;
        return Entity.Null;
    }

    /// <summary>A bed let out (in a building that lets them): only whoever's been given it sleeps there.</summary>
    public static bool IsLet(Simulation sim, Entity bed) =>
        Buildings.Of(sim, bed) is { IsNull: false } b && sim.World.Get<PresetBuilding>(b).Def.LetsBeds;

    private void PostFor(Simulation sim, Entity who, ServiceKind kind, Entity subject)
    {
        if (!MayAsk(sim, who, kind))
            return;
        var def = sim.Defs.Service(kind);
        // The shop's sign sets the price; a shop not offering it gets no custom.
        var shop = def.AtDef is { } at ? BuildingOf(sim, at) : Entity.Null;
        var price = def.DefaultPrice;
        if (!shop.IsNull)
        {
            if (ShopPrices.Posted(sim, shop, kind) is not { Offered: true } posted)
                return;
            price = posted.Price;
        }
        var text = Line(def, price, SubjectLabel(sim, subject) ?? WoundLabel(sim, who));
        Post(sim, who, def, text, price, sim.Clock.Tick + SimClock.HoursToTicks(def.PatienceHours), subject);
    }

    /// <summary>Has nothing of this kind open, and didn't just have one close?</summary>
    private bool MayAsk(Simulation sim, Entity who, ServiceKind kind)
    {
        var since = sim.Clock.Tick - SimClock.HoursToTicks(CooldownHours);
        foreach (var e in _orders)
            if (e.Poster == who && e.Kind.Kind == kind && (e.IsOpen || e.ClosedTick > since))
                return false;
        return true;
    }

    /// <summary>Put an order on the books and say it aloud. Those who hear it clearly know of it.</summary>
    public Order Post(Simulation sim, Entity poster, ServiceDef kind, string text, int price, long deadline, Entity? subject = null)
    {
        var order = new Order
        {
            Id = _nextId++, Kind = kind, Poster = poster, PosterName = sim.World.Get<Creature>(poster).Name, Text = text,
            Where = kind.AtDef is { } at ? BuildingOf(sim, at) : Entity.Null, Subject = subject ?? Entity.Null, Price = price,
            PostedTick = sim.Clock.Tick, Deadline = deadline,
        };
        _orders.Add(order);
        Learn(order, poster);
        // Word of work at a trade's building reaches whoever holds that trade.
        if (kind.AtDef != null)
            foreach (var e in sim.Creatures())
                if (sim.World.TryGet<Trade>(e, out var trade) && trade.Def.WorkplaceDef == kind.AtDef)
                    Learn(order, e);
        Announce(sim, order, text);
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
            // Quick service counts for more.
            var quick = sim.Clock.Tick - e.PostedTick < (e.Deadline - e.PostedTick) / 2;
            sim.World.Get<Adventurer>(e.Poster).Satisfaction += quick ? 1.5f : 1f;
        }
        var thanks = paid >= e.Price ? $"There. {paid} silver, as promised." : $"Here's {paid} silver. It's all I've got.";
        sim.Speech.Say(sim, e.Poster, thanks, Volume.Say);
    }

    /// <summary>Shops that hand something over to be seen to and given back.</summary>
    public static bool HandsOver(ServiceKind kind) => kind is ServiceKind.Repair or ServiceKind.Unlock;

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
        return true;
    }

    /// <summary>Did the asker go without the thing they handed over (the order closing with it still out)?</summary>
    private static void LostTheirGear(Simulation sim, Order e)
    {
        var w = sim.World;
        if (!HandsOver(e.Kind.Kind) || !w.IsAlive(e.Subject) || !w.IsAlive(e.Poster) || Inventory.OnPerson(sim, e.Poster, e.Subject))
            return;
        if (w.Has<Entrusted>(e.Subject))
            w.Remove<Entrusted>(e.Subject); // theirs now, for better or worse
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

    /// <summary>They can taste how it was poured, and say so.</summary>
    private static void Tasted(Simulation sim, Entity who, Quality quality)
    {
        var (mood, line) = quality switch
        {
            Quality.Crude => (-0.75f, "Flat and warm. Still, a drink's a drink."),
            Quality.Fine => (0.25f, "That's a decent pour."),
            Quality.Masterwork => (0.5f, "Now that's how you pour an ale!"),
            _ => (0f, (string?)null),
        };
        if (sim.World.Has<Adventurer>(who))
            sim.World.Get<Adventurer>(who).Satisfaction += mood;
        if (line != null)
            sim.Speech.Say(sim, who, line, Volume.Say);
    }

    private static void GiveUp(Simulation sim, Order e)
    {
        Close(sim, e, OrderStatus.Expired);
        LostTheirGear(sim, e);
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
        var drink = _orders.Find(e => e.IsOpen && e.Poster == to && e.Kind.Kind == ServiceKind.Drink);
        if (drink != null && def.Drink)
        {
            var quality = w.TryGet<Crafted>(item, out var made) ? made.Quality : Quality.Plain;
            w.Destroy(item);
            Tasted(sim, to, quality);
            Complete(sim, drink, giver);
            return;
        }
        var meal = _orders.Find(e => e.IsOpen && e.Poster == to && e.Kind.Kind == ServiceKind.Meal);
        if (meal != null && def.EdibleBy(w.Get<Creature>(to).Def))
        {
            var count = Math.Min(EatJob.Wanted(sim, to, def), w.Get<Stack>(item).Count);
            var eaten = sim.TakeFromStack(item, count);
            if (eaten != item)
                Inventory.Stow(sim, giver, item); // the rest back to the server
            var rot = FoodPoisoning.RotOf(sim, eaten);
            w.Destroy(eaten);
            EatJob.Nourish(sim, to, def, count, rot);
            Complete(sim, meal, giver);
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

    private string Line(ServiceDef def, int price, string? subject) =>
        def.Lines.Count == 0 ? $"Wanted: {def.Label}. {price} silver." : string.Format(def.Lines[_rand.NextInt(def.Lines.Count)], price, subject ?? "gear");

    private static string? SubjectLabel(Simulation sim, Entity subject) =>
        sim.World.IsAlive(subject) && sim.World.TryGet<Thing>(subject, out var t) ? t.Label : null;

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
            w.Long(e.PostedTick);
            w.Long(e.Deadline);
            w.Entity(e.Assignee);
            w.String(e.AssigneeName);
            w.Byte((byte)e.Status);
            w.Long(e.ClosedTick);
            w.Long(e.LastAsked);
            w.Int(e.KnownBy.Count);
            foreach (var k in e.KnownBy)
                w.Entity(k);
        }
    }

    public void Load(Saving.SaveReader r, Simulation sim)
    {
        _rand.State = (r.UInt(), r.UInt(), r.UInt(), r.UInt());
        _nextId = r.Int();
        _questAsked.Clear();
        for (var n = r.Count(1 << 16); n > 0; n--)
            _questAsked.Add(r.Int());
        _treatedBy.Clear();
        for (var n = r.Count(1 << 16); n > 0; n--)
            _treatedBy[r.Entity()] = r.Entity();
        _orders.Clear();
        for (var n = r.Count(1 << 16); n > 0; n--)
        {
            var e = new Order
            {
                Id = r.Int(), Kind = r.Def<ServiceDef>(), Poster = r.Entity(), PosterName = r.String(), Text = r.String(),
                Where = r.Entity(), Subject = r.Entity(), Price = r.Int(), PostedTick = r.Long(), Deadline = r.Long(),
                Assignee = r.Entity(), AssigneeName = r.String(),
            };
            var status = r.Byte();
            if (!Enum.IsDefined((OrderStatus)status))
                throw new Saving.SaveException($"Corrupt save: order status {status}");
            e.Status = (OrderStatus)status;
            e.ClosedTick = r.Long();
            e.LastAsked = r.Long();
            for (var k = r.Count(1 << 16); k > 0; k--)
                e.KnownBy.Add(r.Entity());
            _orders.Add(e);
        }
    }
}

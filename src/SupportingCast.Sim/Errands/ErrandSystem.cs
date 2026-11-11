using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Combat;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Round;
using SupportingCast.Sim.Speech;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Errands;

/// <summary>
/// Every <see cref="Errand"/> in the round. Adventurers ask aloud for what they want (a meal when
/// hungry, a mend when their gear is worn, a healer when hurt, and once per party a quest), and
/// keep asking while nobody takes it on. Done, they pay in silver and think better of the town;
/// left waiting, they give up and think worse (<see cref="Adventurer.Satisfaction"/>). Players
/// post requests of their own. Only those who heard an errand asked know of it.
/// </summary>
public sealed class ErrandSystem(ulong seed) : ISimSystem, Saving.ISaveState
{
    public string Name => "Errands";
    public int Interval => SimClock.TicksPerHour / 12;

    /// <summary>Hunger (the Food need) below which an adventurer asks for a meal.</summary>
    public const float MealBelow = 0.6f;

    /// <summary>Wear (fraction of max HP) below which an adventurer wants a piece of gear mended.</summary>
    public const float RepairBelow = 0.75f;

    /// <summary>Game hours after one errand of a kind closes before the same asker posts another.</summary>
    public const float CooldownHours = 2;

    /// <summary>Game hours between an asker repeating an errand nobody has taken.</summary>
    public const float RepeatHours = 1.5f;

    /// <summary>Open requests one player may have at once.</summary>
    public const int MaxOpenRequests = 5;

    /// <summary>How long closed errands stay on the books (for the list and the cooldown).</summary>
    public const int KeepClosedTicks = SimClock.TicksPerDay;

    /// <summary>Longest request text.</summary>
    public const int MaxTextLength = 120;

    private readonly SimRandom _rand = new(seed ^ 0xE77A_4D5u);
    private readonly List<Errand> _errands = [];
    private readonly HashSet<int> _questAsked = [];
    private int _nextId = 1;

    /// <summary>Every errand still on the books, oldest first. Game code reads them through <c>Perception</c>.</summary>
    public IReadOnlyList<Errand> All => _errands;

    public Errand? Find(int id) => _errands.Find(e => e.Id == id);

    public void Update(Simulation sim)
    {
        if (sim.Round.Phase != RoundPhase.Playing)
            return;
        var now = sim.Clock.Tick;
        foreach (var e in _errands)
        {
            if (!e.IsOpen)
                continue;
            if (!sim.World.IsAlive(e.Poster) || sim.World.Get<Creature>(e.Poster).Dead || sim.World.Has<Leaving>(e.Poster))
                Close(sim, e, ErrandStatus.Cancelled);
            else if (now >= e.Deadline)
                GiveUp(sim, e);
            else if (e.Assignee.IsNull && e.Kind.Kind != ErrandKind.Request && now - e.LastAsked >= SimClock.HoursToTicks(RepeatHours))
                Announce(sim, e, Line(e.Kind, e.Price, SubjectLabel(sim, e.Subject)));
        }
        _errands.RemoveAll(e => !e.IsOpen && now - e.ClosedTick > KeepClosedTicks);
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
            if (!_questAsked.Contains(a.Party))
            {
                _questAsked.Add(a.Party);
                PostFor(sim, who, ErrandKind.Quest, Entity.Null);
            }
            if (w.Get<Needs>(who).Get(food) < MealBelow)
                PostFor(sim, who, ErrandKind.Meal, Entity.Null);
            if (MostWorn(sim, who) is { IsNull: false } gear)
                PostFor(sim, who, ErrandKind.Repair, gear);
            if (w.TryGet<Health>(who, out var h) && h.Injuries.Exists(i => !i.Tended))
                PostFor(sim, who, ErrandKind.Healing, Entity.Null);
        }
    }

    private void PostFor(Simulation sim, Entity who, ErrandKind kind, Entity subject)
    {
        if (!MayAsk(sim, who, kind))
            return;
        var def = sim.Defs.Errand(kind);
        var price = _rand.Range(def.PriceMin, def.PriceMax + 1);
        var text = Line(def, price, SubjectLabel(sim, subject) ?? WoundLabel(sim, who));
        Post(sim, who, def, text, price, sim.Clock.Tick + SimClock.HoursToTicks(def.PatienceHours), subject);
    }

    /// <summary>Has nothing of this kind open, and didn't just have one close?</summary>
    private bool MayAsk(Simulation sim, Entity who, ErrandKind kind)
    {
        var since = sim.Clock.Tick - SimClock.HoursToTicks(CooldownHours);
        foreach (var e in _errands)
            if (e.Poster == who && e.Kind.Kind == kind && (e.IsOpen || e.ClosedTick > since))
                return false;
        return true;
    }

    /// <summary>Put an errand on the books and say it aloud. Those who hear it clearly know of it.</summary>
    public Errand Post(Simulation sim, Entity poster, ErrandKindDef kind, string text, int price, long deadline, Entity? subject = null)
    {
        var errand = new Errand
        {
            Id = _nextId++, Kind = kind, Poster = poster, PosterName = sim.World.Get<Creature>(poster).Name, Text = text,
            Where = kind.AtDef is { } at ? BuildingOf(sim, at) : Entity.Null, Subject = subject ?? Entity.Null, Price = price,
            PostedTick = sim.Clock.Tick, Deadline = deadline,
        };
        _errands.Add(errand);
        Learn(errand, poster);
        Announce(sim, errand, text);
        return errand;
    }

    /// <summary>The asker says it (again): everyone who catches it clearly now knows of it.</summary>
    private static void Announce(Simulation sim, Errand e, string? text)
    {
        if (text == null || sim.Speech.Say(sim, e.Poster, text, Volume.Say) is not { } said)
            return;
        e.LastAsked = sim.Clock.Tick;
        foreach (var h in said.Listeners)
            if (h.Clarity == Clarity.Clear)
                Learn(e, h.Listener);
    }

    public static void Learn(Errand e, Entity who)
    {
        if (!who.IsNull && !e.KnownBy.Contains(who))
            e.KnownBy.Add(who);
    }

    /// <summary>Someone says they'll see to an errand they know of. False if they can't take it.</summary>
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
    /// The errand is done, by <paramref name="by"/>: the asker pays what they promised (or what
    /// they have), thanks them aloud, and an adventurer thinks the better of the town for it.
    /// </summary>
    public void Complete(Simulation sim, Errand e, Entity by)
    {
        if (!e.IsOpen)
            return;
        var paid = Coin.Pay(sim, e.Poster, by, e.Price);
        Close(sim, e, ErrandStatus.Done);
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

    /// <summary>The asker calls it off (a player's own request).</summary>
    public bool Cancel(Simulation sim, Entity who, int id)
    {
        if (Find(id) is not { IsOpen: true } e || e.Poster != who)
            return false;
        Close(sim, e, ErrandStatus.Cancelled);
        return true;
    }

    private static void GiveUp(Simulation sim, Errand e)
    {
        Close(sim, e, ErrandStatus.Expired);
        if (sim.World.Has<Adventurer>(e.Poster))
        {
            sim.World.Get<Adventurer>(e.Poster).Satisfaction -= 1f;
            sim.Speech.Say(sim, e.Poster, $"Forget it. So much for {e.Kind.Label} in this town.", Volume.Say);
        }
    }

    private static void Close(Simulation sim, Errand e, ErrandStatus status)
    {
        e.Status = status;
        e.ClosedTick = sim.Clock.Tick;
    }

    /// <summary>
    /// Something handed to someone (<see cref="GiveCommand"/>): if it's what they're waiting for,
    /// the errand is done and paid; otherwise they keep it. Takes ownership of the unspawned item.
    /// </summary>
    public void Received(Simulation sim, Entity giver, Entity to, Entity item)
    {
        var w = sim.World;
        var def = w.Get<Thing>(item).Def;
        var meal = _errands.Find(e => e.IsOpen && e.Poster == to && e.Kind.Kind == ErrandKind.Meal);
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

    /// <summary>The worn weapon or apparel most in need of mending, below <see cref="RepairBelow"/>; Entity.Null if none.</summary>
    public static Entity MostWorn(Simulation sim, Entity who)
    {
        var w = sim.World;
        var worst = Entity.Null;
        var worstFrac = RepairBelow;
        void Consider(Entity item)
        {
            if (!w.IsAlive(item) || sim.IsPack(item))
                return;
            var t = w.Get<Thing>(item);
            if (t.Def.Weapon == null && t.Def.Apparel is not { Sharp: > 0 } and not { Blunt: > 0 })
                return;
            var frac = t.MaxHp > 0 ? t.Hp / t.MaxHp : 1f;
            if (frac < worstFrac)
            {
                worst = item;
                worstFrac = frac;
            }
        }
        if (w.TryGet<Equipment>(who, out var eq))
            Consider(eq.Weapon);
        foreach (var item in sim.Worn(who))
            Consider(item);
        return worst;
    }

    private string Line(ErrandKindDef def, int price, string? subject) =>
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

    /// <summary>Post a player's request in their own words. Null if it's empty or they have too many open.</summary>
    public Errand? PostRequest(Simulation sim, Entity who, string? text, int price, int hours)
    {
        var line = SpeechSystem.Clean(text);
        if (line.Length > MaxTextLength)
            line = line[..MaxTextLength].Trim();
        if (line.Length == 0 || _errands.Count(e => e.IsOpen && e.Poster == who) >= MaxOpenRequests)
            return null;
        var def = sim.Defs.Errand(ErrandKind.Request);
        price = Math.Clamp(price, def.PriceMin, def.PriceMax);
        hours = Math.Clamp(hours, 1, 24);
        return Post(sim, who, def, $"{line} ({price} silver)", price, sim.Clock.Tick + SimClock.HoursToTicks(hours));
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
        w.Int(_errands.Count);
        foreach (var e in _errands)
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
        _errands.Clear();
        for (var n = r.Count(1 << 16); n > 0; n--)
        {
            var e = new Errand
            {
                Id = r.Int(), Kind = r.Def<ErrandKindDef>(), Poster = r.Entity(), PosterName = r.String(), Text = r.String(),
                Where = r.Entity(), Subject = r.Entity(), Price = r.Int(), PostedTick = r.Long(), Deadline = r.Long(),
                Assignee = r.Entity(), AssigneeName = r.String(),
            };
            var status = r.Byte();
            if (!Enum.IsDefined((ErrandStatus)status))
                throw new Saving.SaveException($"Corrupt save: errand status {status}");
            e.Status = (ErrandStatus)status;
            e.ClosedTick = r.Long();
            e.LastAsked = r.Long();
            for (var k = r.Count(1 << 16); k > 0; k--)
                e.KnownBy.Add(r.Entity());
            _errands.Add(e);
        }
    }
}

using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Favours;

/// <summary>
/// Every <see cref="Favour"/> in the round, pinned to the town's notice board in the square: only
/// those who've read the board (F at it) know of one. Someone says they'll do it, and the asker
/// pays them in silver when they say it's done. An errand (bring so many of something) is done by
/// bringing it, and a labourer the AI runs will take one on (<see cref="ErrandJob"/>). Shops are for
/// what adventurers buy (<see cref="ShopSystem"/>); this is the town asking itself.
/// </summary>
public sealed class FavourBoard : ISimSystem, Saving.ISaveState
{
    public string Name => "Favours";
    public int Interval => SimClock.TicksPerHour / 12;

    /// <summary>Open favours one player may have at once.</summary>
    public const int MaxOpen = 5;

    /// <summary>Longest favour text.</summary>
    public const int MaxTextLength = 120;

    /// <summary>Most silver a favour may offer.</summary>
    public const int MaxPrice = 999;

    /// <summary>Longest a favour may stay open, in game hours.</summary>
    public const int MaxHours = 24;

    /// <summary>How long closed favours stay on the books, for the list.</summary>
    public const int KeepClosedTicks = SimClock.TicksPerDay;

    private readonly List<Favour> _favours = [];
    private int _nextId = 1;

    /// <summary>Every favour still on the books, oldest first. Game code reads them through <c>Perception</c>.</summary>
    public IReadOnlyList<Favour> All => _favours;

    public Favour? Find(int id) => _favours.Find(r => r.Id == id);

    public void Update(Simulation sim)
    {
        if (!sim.Round.Live)
            return;
        var now = sim.Clock.Tick;
        foreach (var r in _favours)
        {
            if (!r.IsOpen)
                continue;
            if (!sim.World.IsAlive(r.Poster) || sim.World.Get<Creature>(r.Poster).Dead)
                Close(sim, r, FavourStatus.Cancelled);
            else if (now >= r.Deadline)
                Close(sim, r, FavourStatus.Expired);
        }
        _favours.RemoveAll(r => !r.IsOpen && now - r.ClosedTick > KeepClosedTicks);
    }

    /// <summary>Most of one thing an errand may ask for.</summary>
    public const int MaxCount = 99;

    /// <summary>The notice board within reach of someone, or Entity.Null.</summary>
    public static Entity BoardBy(Simulation sim, Entity who)
    {
        var w = sim.World;
        var here = w.Get<Position>(who).Cell;
        foreach (var c in sim.SpiralFrom(here, 1))
        {
            if (!sim.Map.InBounds(c))
                continue;
            var e = sim.Map.Structure[sim.Map.Idx(c)];
            if (IsBoard(sim, e) && AvatarSystem.InReach(sim, here, e))
                return e;
        }
        return Entity.Null;
    }

    public static bool IsBoard(Simulation sim, Entity e) =>
        !e.IsNull && sim.World.IsAlive(e) && sim.World.TryGet<Thing>(e, out var t) && t.Def.Name == "NoticeBoard";

    /// <summary>
    /// Pin a favour to the board, in your own words: you have to be at it. Null if it's empty,
    /// you're not at the board, or you have too many open.
    /// </summary>
    public Favour? Post(Simulation sim, Entity who, string? text, int price, int hours) => Pin(sim, who, text, price, hours, null, 0);

    /// <summary>Pin an errand: so many of a thing wanted, brought to you, for a price.</summary>
    public Favour? PostErrand(Simulation sim, Entity who, Defs.ThingDef? wants, int count, int price, int hours) =>
        wants == null || wants.Category != Defs.ThingCategory.Item ? null : Pin(sim, who, null, price, hours, wants, Math.Clamp(count, 1, MaxCount));

    private Favour? Pin(Simulation sim, Entity who, string? text, int price, int hours, Defs.ThingDef? wants, int count)
    {
        var line = wants != null ? $"Wanted: {count} {wants.Label}, brought to me" : SpeechSystem.Clean(text);
        if (line.Length > MaxTextLength)
            line = line[..MaxTextLength].Trim();
        if (line.Length == 0 || _favours.Count(r => r.IsOpen && r.Poster == who) >= MaxOpen)
            return null;
        var board = BoardBy(sim, who);
        if (board.IsNull)
        {
            sim.Messages.Add(MessageKind.Info, "Favours are pinned to the notice board in the square: go to it first.", sim.LocationOf(who));
            return null;
        }
        price = Math.Clamp(price, 0, MaxPrice);
        hours = Math.Clamp(hours, 1, MaxHours);
        var favour = new Favour
        {
            Id = _nextId++, Poster = who, PosterName = sim.World.Get<Creature>(who).Name, Text = $"{line} ({price} silver)",
            Price = price, PostedTick = sim.Clock.Tick, Deadline = sim.Clock.Tick + SimClock.HoursToTicks(hours),
            Wants = wants, Count = count,
        };
        _favours.Add(favour);
        Learn(favour, who);
        sim.Messages.Add(MessageKind.Info, $"{favour.PosterName} pins a note to the notice board.", sim.World.Get<Thing>(board).Cell);
        return favour;
    }

    /// <summary>Read the board: everything open on it is known to you now. How many you hadn't known; -1 if you're not at it.</summary>
    public int Read(Simulation sim, Entity who)
    {
        if (BoardBy(sim, who).IsNull)
            return -1;
        var learned = 0;
        foreach (var r in _favours)
            if (r.IsOpen && !r.KnownBy.Contains(who))
            {
                Learn(r, who);
                learned++;
            }
        return learned;
    }

    /// <summary>
    /// An errand's goods handed over: whoever took it on, in reach of the asker with enough of the
    /// thing on them, gives it over and is paid at once. False if it isn't so.
    /// </summary>
    public bool Deliver(Simulation sim, Entity who, int id)
    {
        if (Find(id) is not { IsOpen: true, Wants: { } wants } r || r.Assignee != who || !Handing.InReach(sim, who, r.Poster))
            return false;
        var stacks = Inventory.CarriedOf(sim, who, wants);
        if (stacks.Sum(s => sim.World.Get<Stack>(s).Count) < r.Count)
            return false;
        var left = r.Count;
        foreach (var stack in stacks)
        {
            if (left <= 0)
                break;
            var take = Math.Min(left, sim.World.Get<Stack>(stack).Count);
            Inventory.Stow(sim, r.Poster, Inventory.TakeFromPerson(sim, who, stack, take));
            left -= take;
        }
        var paid = Coin.Pay(sim, r.Poster, who, r.Price);
        Close(sim, r, FavourStatus.Done);
        sim.Speech.Say(sim, r.Poster, paid >= r.Price ? $"Just what I wanted. {paid} silver, as promised." : $"Thanks. Here's {paid} silver; it's all I've got.", Volume.Say);
        return true;
    }

    /// <summary>Someone says they'll see to a favour they know of. False if they can't take it.</summary>
    public bool Accept(Simulation sim, Entity who, int id)
    {
        if (Find(id) is not { IsOpen: true } r || r.Poster == who || !r.Assignee.IsNull || !r.KnownBy.Contains(who))
            return false;
        r.Assignee = who;
        r.AssigneeName = sim.World.Get<Creature>(who).Name;
        SayAndSpread(sim, r, who, $"I'll see to it, {r.PosterName}.");
        return true;
    }

    /// <summary>The asker says it's done: they pay whoever took it on (in reach), what they promised or what they have.</summary>
    public bool Complete(Simulation sim, Entity who, int id)
    {
        if (Find(id) is not { IsOpen: true } r || r.Poster != who || !Handing.InReach(sim, who, r.Assignee))
            return false;
        var paid = Coin.Pay(sim, r.Poster, r.Assignee, r.Price);
        Close(sim, r, FavourStatus.Done);
        var thanks = paid >= r.Price ? $"There. {paid} silver, as promised." : $"Here's {paid} silver. It's all I've got.";
        sim.Speech.Say(sim, r.Poster, thanks, Volume.Say);
        return true;
    }

    /// <summary>The asker calls it off.</summary>
    public bool Cancel(Simulation sim, Entity who, int id)
    {
        if (Find(id) is not { IsOpen: true } r || r.Poster != who)
            return false;
        Close(sim, r, FavourStatus.Cancelled);
        return true;
    }

    private static void SayAndSpread(Simulation sim, Favour r, Entity speaker, string text)
    {
        if (sim.Speech.Say(sim, speaker, text, Volume.Say) is not { } said)
            return;
        foreach (var h in said.Listeners)
            if (h.Clarity == Clarity.Clear)
                Learn(r, h.Listener);
    }

    public static void Learn(Favour r, Entity who)
    {
        if (!who.IsNull && !r.KnownBy.Contains(who))
            r.KnownBy.Add(who);
    }

    private static void Close(Simulation sim, Favour r, FavourStatus status)
    {
        r.Status = status;
        r.ClosedTick = sim.Clock.Tick;
    }

    public void Save(Saving.SaveWriter w, Simulation sim)
    {
        w.Int(_nextId);
        w.Int(_favours.Count);
        foreach (var r in _favours)
        {
            w.Int(r.Id);
            w.Entity(r.Poster);
            w.String(r.PosterName);
            w.String(r.Text);
            w.Int(r.Price);
            w.Long(r.PostedTick);
            w.Long(r.Deadline);
            w.Entity(r.Assignee);
            w.String(r.AssigneeName);
            w.Byte((byte)r.Status);
            w.Long(r.ClosedTick);
            w.Bool(r.Wants != null);
            if (r.Wants != null)
            {
                w.Def(r.Wants);
                w.Int(r.Count);
            }
            w.Int(r.KnownBy.Count);
            foreach (var k in r.KnownBy)
                w.Entity(k);
        }
    }

    public void Load(Saving.SaveReader r, Simulation sim)
    {
        _nextId = r.Int();
        _favours.Clear();
        for (var n = r.Count(1 << 16); n > 0; n--)
        {
            var (id, poster, posterName, text, price) = (r.Int(), r.Entity(), r.String(), r.String(), r.Int());
            var (posted, deadline, assignee, assigneeName) = (r.Long(), r.Long(), r.Entity(), r.String());
            var status = r.Byte();
            if (!Enum.IsDefined((FavourStatus)status))
                throw new Saving.SaveException($"Corrupt save: favour status {status}");
            var closed = r.Long();
            var (wants, count) = r.Version >= 11 && r.Bool() ? (r.Def<Defs.ThingDef>(), r.Int()) : (null, 0);
            var q = new Favour
            {
                Id = id, Poster = poster, PosterName = posterName, Text = text, Price = price, PostedTick = posted,
                Deadline = deadline, Assignee = assignee, AssigneeName = assigneeName, Wants = wants, Count = count,
                Status = (FavourStatus)status, ClosedTick = closed,
            };
            for (var k = r.Count(1 << 16); k > 0; k--)
                q.KnownBy.Add(r.Entity());
            _favours.Add(q);
        }
    }
}

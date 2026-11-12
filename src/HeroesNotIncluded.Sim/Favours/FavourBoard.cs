using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Speech;

namespace HeroesNotIncluded.Sim.Favours;

/// <summary>
/// Every player <see cref="Favour"/> in the round. Asked aloud, so only those who hear know of it;
/// someone says they'll do it, and the asker pays them in silver when they say it's done. Shops are
/// for what adventurers buy (<see cref="ShopSystem"/>); this is players asking each other.
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
        if (sim.Round.Phase != RoundPhase.Playing)
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

    /// <summary>Ask in your own words, aloud. Null if it's empty or they have too many open.</summary>
    public Favour? Post(Simulation sim, Entity who, string? text, int price, int hours)
    {
        var line = SpeechSystem.Clean(text);
        if (line.Length > MaxTextLength)
            line = line[..MaxTextLength].Trim();
        if (line.Length == 0 || _favours.Count(r => r.IsOpen && r.Poster == who) >= MaxOpen)
            return null;
        price = Math.Clamp(price, 0, MaxPrice);
        hours = Math.Clamp(hours, 1, MaxHours);
        var favour = new Favour
        {
            Id = _nextId++, Poster = who, PosterName = sim.World.Get<Creature>(who).Name, Text = $"{line} ({price} silver)",
            Price = price, PostedTick = sim.Clock.Tick, Deadline = sim.Clock.Tick + SimClock.HoursToTicks(hours),
        };
        _favours.Add(favour);
        Learn(favour, who);
        SayAndSpread(sim, favour, who, favour.Text);
        return favour;
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
            var q = new Favour
            {
                Id = r.Int(), Poster = r.Entity(), PosterName = r.String(), Text = r.String(), Price = r.Int(),
                PostedTick = r.Long(), Deadline = r.Long(), Assignee = r.Entity(), AssigneeName = r.String(),
            };
            var status = r.Byte();
            if (!Enum.IsDefined((FavourStatus)status))
                throw new Saving.SaveException($"Corrupt save: favour status {status}");
            q.Status = (FavourStatus)status;
            q.ClosedTick = r.Long();
            for (var k = r.Count(1 << 16); k > 0; k--)
                q.KnownBy.Add(r.Entity());
            _favours.Add(q);
        }
    }
}

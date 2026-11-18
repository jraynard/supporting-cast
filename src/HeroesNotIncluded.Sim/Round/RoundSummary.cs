using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Saving;

namespace HeroesNotIncluded.Sim.Round;

/// <summary>How a round came out. <see cref="None"/> while it's still going.</summary>
public enum RoundOutcome : byte
{
    None,

    /// <summary>The festival came, and the town's Renown met the season's target.</summary>
    Won,

    /// <summary>The festival came, but the town wasn't known well enough.</summary>
    FellShort,

    /// <summary>Dread overflowed before the festival.</summary>
    Dread,

    /// <summary>Nobody of the village was left alive.</summary>
    TownFell,
}

/// <summary>Someone called up at the festival's ceremony (<see cref="FestivalSystem"/>), and what for.</summary>
public sealed record HonourRow(string Name, string Title);

/// <summary>One of the town in the reveal: who they were, what they did, what they made of it.</summary>
/// <param name="DiedOnDay">The day of the round they died (1 is the first), if they lie dead.</param>
/// <param name="CameBack">Called back from the grave this round.</param>
/// <param name="Errands">Unfinished business they saw to as a ghost.</param>
/// <param name="CalledBy">Who called them back, and <paramref name="HeldBy"/> who held them while they did ("" if nobody).</param>
public sealed record RevealRow(string Name, string Trade, bool Player, int Earned, bool Alive, int? DiedOnDay = null, bool CameBack = false, int Errands = 0,
    string CalledBy = "", string HeldBy = "");

/// <summary>
/// The end-of-round reveal, built once when the round ends and saved with it, so every peer and a
/// reloaded save show the same thing. It's the one place exact Renown and Dread are shown. M4 adds
/// allegiances to <see cref="People"/>.
/// </summary>
public sealed class RoundSummary
{
    public RoundOutcome Outcome;
    public long EndedTick;
    public float Renown;
    public float Dread;
    public float RenownTarget;
    public float DreadLimit;

    /// <summary>Renown and Dread each game hour from the deal, for the reveal's graph.</summary>
    public List<float> RenownByHour = [];
    public List<float> DreadByHour = [];

    public int Parties;
    public int AdventurersServed;
    public int QuestsCleared;
    public int QuestsFailed;
    public int AdventurersLost;
    public List<RevealRow> People = [];

    /// <summary>Who the festival's ceremony called up, in order.</summary>
    public List<HonourRow> Honours = [];

    /// <summary>
    /// Every quest a party went on, and whose work went with them ("Behind every hero"). The hidden
    /// truths in it (a warped quiver, a binding contraption) are the reveal's to tell.
    /// </summary>
    public List<Expeditions.QuestTale> Quests = [];

    /// <summary>The headline a mode gave its end (<see cref="Modding.RoundEnd"/>), or null for the outcome's own.</summary>
    public string? Told { get; set; }

    public string Headline => Told ?? Outcome switch
    {
        RoundOutcome.Won => "The town came through the season.",
        RoundOutcome.FellShort => "The season's over, and the town is soon forgotten.",
        RoundOutcome.Dread => "Dread took the valley before the festival.",
        RoundOutcome.TownFell => "The town fell.",
        _ => "",
    };

    public void Save(SaveWriter w)
    {
        w.Int((int)Outcome);
        w.Long(EndedTick);
        w.Float(Renown);
        w.Float(Dread);
        w.Float(RenownTarget);
        w.Float(DreadLimit);
        Floats(w, RenownByHour);
        Floats(w, DreadByHour);
        w.Int(Parties);
        w.Int(AdventurersServed);
        w.Int(QuestsCleared);
        w.Int(QuestsFailed);
        w.Int(AdventurersLost);
        w.Int(People.Count);
        foreach (var p in People)
        {
            w.String(p.Name);
            w.String(p.Trade);
            w.Bool(p.Player);
            w.Int(p.Earned);
            w.Bool(p.Alive);
            w.Int(p.DiedOnDay ?? 0);
            w.Bool(p.CameBack);
            w.Int(p.Errands);
            w.String(p.CalledBy);
            w.String(p.HeldBy);
        }
        w.Int(Honours.Count);
        foreach (var h in Honours)
        {
            w.String(h.Name);
            w.String(h.Title);
        }
        w.Int(Quests.Count);
        foreach (var q in Quests)
            q.Save(w);
        w.NullableString(Told);
    }

    public static RoundSummary Read(SaveReader r)
    {
        var s = new RoundSummary
        {
            Outcome = (RoundOutcome)r.Int(), EndedTick = r.Long(), Renown = r.Float(), Dread = r.Float(),
            RenownTarget = r.Float(), DreadLimit = r.Float(), RenownByHour = Floats(r), DreadByHour = Floats(r),
            Parties = r.Int(), AdventurersServed = r.Int(), QuestsCleared = r.Int(), QuestsFailed = r.Int(), AdventurersLost = r.Int(),
        };
        for (var n = r.Count(RoundSystem.MaxSeats * 4); n > 0; n--)
        {
            var row = new RevealRow(r.String(), r.String(), r.Bool(), r.Int(), r.Bool());
            if (r.Version >= 18) // the dead's day, the returned and their errands came with format 18
                row = row with { DiedOnDay = r.Int() is > 0 and var day ? day : null, CameBack = r.Bool(), Errands = r.Int() };
            if (r.Version >= 58) // who called them back, and who held them (format 58)
                row = row with { CalledBy = r.String(), HeldBy = r.String() };
            s.People.Add(row);
        }
        if (r.Version >= 20) // the festival's honours came with format 20
            for (var n = r.Count(100); n > 0; n--)
                s.Honours.Add(new HonourRow(r.String(), r.String()));
        if (r.Version >= 54) // the quests' tales came with format 54
            for (var n = r.Count(1 << 10); n > 0; n--)
                s.Quests.Add(Expeditions.QuestTale.Read(r));
        if (r.Version >= 63) // a mode's own headline came with format 63
            s.Told = r.NullableString();
        return s;
    }

    internal static void Floats(SaveWriter w, List<float> list)
    {
        w.Int(list.Count);
        foreach (var f in list)
            w.Float(f);
    }

    internal static List<float> Floats(SaveReader r)
    {
        var list = new List<float>();
        for (var n = r.Count(100_000); n > 0; n--)
            list.Add(r.Float());
        return list;
    }

    /// <summary>The day of the round a tick fell on: 1 is the day of the deal.</summary>
    private static int DayOfRound(Simulation sim, long tick)
    {
        var startHour = sim.Clock.StartHour * (long)SimClock.TicksPerHour;
        return (int)((tick + startHour) / SimClock.TicksPerDay - (Math.Max(0, sim.Round.StartTick) + startHour) / SimClock.TicksPerDay) + 1;
    }

    /// <summary>The town as it stands: everyone of the village, players first, and what they earned since the deal.</summary>
    internal static List<RevealRow> PeopleOf(Simulation sim, IReadOnlyList<(Entity Who, int Coin)> startCoin)
    {
        var w = sim.World;
        var rows = new List<(RevealRow Row, int Order)>();
        foreach (var (e, start) in startCoin)
        {
            if (!w.IsAlive(e) || !w.TryGet<Creature>(e, out var c))
                continue;
            var trade = w.TryGet<Trade>(e, out var t) ? t.Def.Label : "villager";
            // A co-op player keeps PlayerCharacter after leaving; a solo hero has only the Avatar.
            var player = w.Has<PlayerCharacter>(e) || w.Has<Avatar>(e);
            int? died = c.Dead && w.TryGet<Afterlife.Death>(e, out var d) ? DayOfRound(sim, d.Tick) : null;
            var errands = w.TryGet<Afterlife.Unfinished>(e, out var u) ? u.Done : 0;
            var (calledBy, heldBy) = w.TryGet<Afterlife.Revived>(e, out var back) ? (NameOf(back.CalledBy), NameOf(back.HeldBy)) : ("", "");
            rows.Add((new RevealRow(c.Name, trade, player, Shops.Coin.Count(sim, e) - start, !c.Dead, died, w.Has<Afterlife.Revived>(e), errands, calledBy, heldBy), player ? 0 : 1));
        }
        return rows.OrderBy(r => r.Order).Select(r => r.Row).ToList();

        string NameOf(Entity who) => w.IsAlive(who) && w.TryGet<Creature>(who, out var named) ? named.Name : "";
    }
}

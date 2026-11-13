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

/// <summary>One of the town in the reveal: who they were, what they did, what they made of it.</summary>
public sealed record RevealRow(string Name, string Trade, bool Player, int Earned, bool Alive);

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

    public string Headline => Outcome switch
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
        }
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
            s.People.Add(new RevealRow(r.String(), r.String(), r.Bool(), r.Int(), r.Bool()));
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
            rows.Add((new RevealRow(c.Name, trade, player, Shops.Coin.Count(sim, e) - start, !c.Dead), player ? 0 : 1));
        }
        return rows.OrderBy(r => r.Order).Select(r => r.Row).ToList();
    }
}

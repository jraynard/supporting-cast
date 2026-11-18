using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Director;

/// <summary>Who in town could meet a kind of trouble: the trade that answers it, held by a player, by the AI, or by nobody able.</summary>
public enum Cover : byte
{
    Nobody,
    Ai,
    Player,
}

/// <summary>
/// The Director's judgement, as rules (docs/DIRECTOR-PLAN.md): when a beat comes in its window,
/// how hard it hits, which kind of trouble it is, and which small incident fills a lull. Each
/// answers the <see cref="Tension"/>: ease off a town that's struggling, push one that's coasting,
/// send trouble somebody can meet and that the season hasn't had yet, and give a player's trade its
/// moment. Pure apart from the rolls it's handed, so it runs the same on every peer. Anything
/// deciding from outside the sim (a host-side mind) goes through <see cref="DirectorChoiceCommand"/>.
/// </summary>
public static class RuleMind
{
    /// <summary>
    /// A beat's moment: its planned one (<paramref name="planned"/>, rolled in its window), pulled
    /// toward the window's start by a push and toward its end by easing off.
    /// </summary>
    public static long Due(long planned, long from, long to, float push) =>
        push >= 0f ? planned - (long)((planned - from) * Math.Min(push, 1f))
        : planned + (long)((Math.Max(to, planned) - planned) * Math.Min(-push, 1f));

    /// <summary>A beat's strength, eased or pushed within the season's <see cref="PacingDef.StrengthRange"/>.</summary>
    public static float Strength(PacingDef pacing, float strength, float push)
    {
        var (lo, hi) = (pacing.StrengthRange[0], pacing.StrengthRange[1]);
        push = Math.Clamp(push, -1f, 1f);
        return strength * (push >= 0f ? 1f + (hi - 1f) * push : 1f + (1f - lo) * push);
    }

    /// <summary>Who holds the trade working from a building of <paramref name="role"/>: a player beats the AI, the AI beats nobody able.</summary>
    public static Cover CoverOf(Simulation sim, TownRole role)
    {
        var w = sim.World;
        var cover = Cover.Nobody;
        foreach (var e in w.Query<Trade>())
        {
            if (w.Get<Trade>(e).Def.Role != role || w.Get<Creature>(e) is not { Dead: false, Downed: false } || w.Has<Adventurer>(e))
                continue;
            if (Players.Steered(sim, e))
                return Cover.Player;
            cover = Cover.Ai;
        }
        return cover;
    }

    /// <summary>
    /// A beat's kinds, weighed against the town: a kind the season's had already counts for less,
    /// one a player's trade answers for more, and one nobody able could answer for much less.
    /// </summary>
    public static List<(CrisisKind Kind, float Weight)> Weigh(Simulation sim, IEnumerable<(CrisisKind Kind, float Weight)> kinds, IReadOnlyCollection<CrisisKind> seen)
    {
        var pacing = sim.Defs.Season.Pacing;
        var list = new List<(CrisisKind, float)>();
        foreach (var (kind, weight) in kinds)
        {
            var w = weight;
            if (seen.Contains(kind))
                w *= pacing.SeenWeight;
            if (pacing.AnsweredBy.TryGetValue(kind, out var role))
                w *= CoverOf(sim, role) switch { Cover.Player => pacing.PlayerWeight, Cover.Nobody => pacing.NobodyWeight, _ => 1f };
            if (w > 0f)
                list.Add((kind, w));
        }
        return list;
    }

    /// <summary>An incident's weight now: more when a player holds the trade it's for.</summary>
    public static float Weigh(Simulation sim, IncidentDef incident) =>
        incident.Weight * (incident.ForRole != TownRole.None && CoverOf(sim, incident.ForRole) == Cover.Player ? sim.Defs.Season.Pacing.PlayerWeight : 1f);

    /// <summary>One of <paramref name="options"/> by weight; default if there are none.</summary>
    public static T? Pick<T>(SimRandom rand, IReadOnlyList<(T Item, float Weight)> options)
    {
        if (options.Count == 0)
            return default;
        var roll = rand.NextFloat() * options.Sum(o => o.Weight);
        foreach (var (item, weight) in options)
        {
            if (roll < weight)
                return item;
            roll -= weight;
        }
        return options[^1].Item;
    }
}

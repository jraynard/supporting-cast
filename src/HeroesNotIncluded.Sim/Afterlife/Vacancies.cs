using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Afterlife;

/// <summary>
/// A dead townsperson's trade stands empty, a real loss, until the next dawn: then one of the
/// town's labourers takes it up as the AI does, at a basic level, so the town still runs. If the
/// dead come back, they find someone helping in their place.
/// </summary>
public static class Vacancies
{
    /// <summary>The hour the work is taken up.</summary>
    public const int DawnHour = 6;

    internal static void Update(Simulation sim)
    {
        var w = sim.World;
        if (sim.Clock.HourOfDay < DawnHour)
            return;
        // Today's dawn, as a tick: only those dead before it leave work for this morning.
        var dawn = sim.Clock.Tick - (long)((sim.Clock.HourOfDay - DawnHour) * SimClock.TicksPerHour);
        foreach (var body in w.Query<Death>())
        {
            if (w.Get<Death>(body).Tick >= dawn || !w.TryGet<Trade>(body, out var trade) || trade.Def.Unlimited
                || !w.TryGet<Creature>(body, out var dead) || !dead.Dead || dead.Faction != Faction.Village || Held(sim, trade.Def))
                continue;
            var hand = sim.Creatures().FirstOrDefault(e => w.Get<Creature>(e) is { Faction: Faction.Village, Def.Race: Race.Human, Downed: false }
                && !w.Has<PlayerCharacter>(e) && !w.Has<Avatar>(e) && !w.Has<Carried>(e)
                && (!w.TryGet<Trade>(e, out var t) || t.Def.Unlimited), Entity.Null);
            if (hand.IsNull)
                continue;
            Round.RoundSystem.Assign(sim, hand, trade.Def);
            sim.Messages.Add(MessageKind.Info, $"{w.Get<Creature>(hand).Name} takes up the {trade.Def.Label}'s work that {dead.Name} left.", w.Get<Position>(hand).Cell);
        }
    }

    /// <summary>Someone living holds this trade.</summary>
    private static bool Held(Simulation sim, TradeDef trade)
    {
        var w = sim.World;
        foreach (var e in sim.Creatures())
            if (w.TryGet<Trade>(e, out var t) && t.Def == trade)
                return true;
        return false;
    }
}

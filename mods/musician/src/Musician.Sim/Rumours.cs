using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Expeditions;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Musician;

/// <summary>When the musician last passed what they'd heard on to the inn (<see cref="Rumours"/>): once in a while, not every visit.</summary>
public struct ToldRumours
{
    public long Tick;
}

/// <summary>
/// The musician hears things: adventurers at the music talk (<see cref="AudienceJob"/>), and what the musician believes of
/// each quest (their own <see cref="Intel"/>, read through Perception like anyone's) they can carry to whoever brokers
/// quests: F at the innkeeper says it aloud, and whoever hears it clearly takes it in, as a rumour. Only the musician:
/// it's their trade to be where the talk is.
/// </summary>
public static class Rumours
{
    /// <summary>The chance an adventurer at the music, settled in, gossips about a quest (once a time they come).</summary>
    public const float GossipChance = 0.5f;

    /// <summary>How far off an adventurer's tale at the music is, either way: nearer the truth than the tavern's idle talk.</summary>
    public const float GossipSpread = 0.2f;

    /// <summary>How much a listener believes what the musician passes on.</summary>
    public const float Weight = 0.35f;

    /// <summary>Game hours before the musician has more worth passing on.</summary>
    public const float EveryHours = 2f;

    /// <summary>At most this many quests told at once.</summary>
    public const int AtOnce = 3;

    /// <summary>The open quests the musician has heard of, as they believe them, surest first.</summary>
    public static List<(Quest Quest, Intel Intel)> Heard(Simulation sim, Entity musician) =>
        [.. sim.Expeditions.Quests
            .Where(q => q.Status != QuestStatus.Cleared)
            .Select(q => (q, sim.Expeditions.IntelOf(musician, q.Id)))
            .Where(p => p.Item2 != null)
            .Select(p => (p.q, p.Item2!))
            .OrderByDescending(p => p.Item2.Confidence)
            .ThenBy(p => p.q.Id)];

    /// <summary>The broker (the innkeeper) standing at a cell, or Entity.Null.</summary>
    public static Entity BrokerAt(Simulation sim, int3 cell)
    {
        var w = sim.World;
        foreach (var e in w.Each<Trade>())
            if (w.Get<Trade>(e).Def.Brokers && w.TryGet<Position>(e, out var p) && p.Cell == cell && w.Get<Creature>(e) is { Dead: false, Downed: false })
                return e;
        return Entity.Null;
    }

    /// <summary>Whether <paramref name="musician"/> has something to pass on to <paramref name="broker"/> now.</summary>
    public static bool CanTell(Simulation sim, Entity musician, Entity broker)
    {
        var w = sim.World;
        if (musician == broker || !w.TryGet<Trade>(musician, out var t) || t.Def.Name != MusicianModule.TradeName)
            return false;
        if (w.TryGet<ToldRumours>(musician, out var told) && sim.Clock.Tick - told.Tick < (long)(EveryHours * SimClock.TicksPerHour))
            return false;
        return Heard(sim, musician).Count > 0 && Sim.Shops.Handing.InReach(sim, musician, broker);
    }

    /// <summary>
    /// Pass it on: each quest the musician's heard of, said aloud as they believe it. Whoever hears it clearly (the
    /// innkeeper, and anyone else near) takes it in. Returns how many were told.
    /// </summary>
    public static int Tell(Simulation sim, Entity musician, Entity broker)
    {
        if (!CanTell(sim, musician, broker))
            return 0;
        var w = sim.World;
        var told = 0;
        foreach (var (quest, intel) in Heard(sim, musician).Take(AtOnce))
        {
            var line = $"Word at the music, {w.Get<Creature>(broker).Name}: {quest.Def.Label} is {ExpeditionSystem.DangerWord(intel.Danger)}.";
            if (sim.Speech.Say(sim, musician, line, Volume.Say) is not { } said)
                continue;
            foreach (var h in said.Listeners)
                if (h.Clarity == Clarity.Clear && h.Listener != musician)
                    sim.Expeditions.Hear(h.Listener, quest.Id, intel.Danger, null, Weight);
            told++;
        }
        if (told > 0)
        {
            if (w.Has<ToldRumours>(musician))
                w.Remove<ToldRumours>(musician);
            w.Add(musician, new ToldRumours { Tick = sim.Clock.Tick });
        }
        return told;
    }
}

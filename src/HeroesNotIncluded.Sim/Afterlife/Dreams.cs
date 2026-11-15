using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Afterlife;

/// <summary>
/// Words a ghost whispered to a sleeper (<see cref="Dreams"/>): theirs to remember once they wake,
/// as a dream. Never says who whispered them.
/// </summary>
public struct Dream
{
    public string Text;

    /// <summary>When it was whispered, and when they woke after it (0 while they sleep on).</summary>
    public long Tick;
    public long WokeTick;
}

/// <summary>
/// Dream whispers (docs/DESIGN.md, "Death"): a ghost by someone asleep can leave them a few words.
/// They wake having dreamt them: a player is told (<see cref="Perception.PerceptionView.Dream"/>),
/// a villager the AI runs is a little comforted. Short, like frost, so the dead can't tell all.
/// </summary>
public static class Dreams
{
    public const float Cost = 0.5f;

    /// <summary>How near a sleeper a ghost must be to whisper, cells.</summary>
    public const float Reach = 3f;

    /// <summary>At most so many words and letters.</summary>
    public const int MaxWords = 8, MaxLetters = 60;

    /// <summary>How long a dream's remembered after waking (and how long one whispered to a sleeper who never wakes lasts).</summary>
    public const int KeptTicks = SimClock.TicksPerDay;

    /// <summary>Why this ghost can't whisper that to them now, or null if it can.</summary>
    public static string? Problem(Simulation sim, Entity ghost, Entity sleeper, string? text)
    {
        var w = sim.World;
        if (!w.IsAlive(ghost) || !w.TryGet<Ghost>(ghost, out var g))
            return "you can't";
        if (!g.Riding.IsNull)
            return "not while you're in a beast";
        if (!Asleep(sim, sleeper) || sleeper == g.Body)
            return "there's nobody asleep there";
        if (!Errands.Near(sim, ghost, sleeper, Reach))
            return "you need to be nearer them";
        if (Hauntings.Words(text, MaxWords, MaxLetters) is null)
            return "whisper something first";
        if (g.Presence < Cost - 0.0001f)
            return "you haven't the presence yet; it gathers in time";
        return null;
    }

    private static bool Asleep(Simulation sim, Entity e) =>
        sim.World.IsAlive(e) && sim.World.TryGet<Creature>(e, out var c) && c.Able && c.Def.Race == Race.Human
        && sim.World.TryGet<Mind>(e, out var mind) && mind.Asleep;

    /// <summary>The nearest sleeper a ghost could whisper to; null if none.</summary>
    public static Entity NearestSleeper(Simulation sim, Entity ghost)
    {
        var w = sim.World;
        if (!w.IsAlive(ghost) || !w.TryGet<Ghost>(ghost, out var g))
            return Entity.Null;
        var at = w.Get<Position>(ghost).Pos;
        var best = Entity.Null;
        var bestD = float.MaxValue;
        foreach (var e in sim.Creatures())
        {
            if (e == g.Body || !Asleep(sim, e) || !Errands.Near(sim, ghost, e, Reach))
                continue;
            var p = w.Get<Position>(e).Pos;
            var d = (p.X - at.X) * (p.X - at.X) + (p.Y - at.Y) * (p.Y - at.Y);
            if (d < bestD)
                (best, bestD) = (e, d);
        }
        return best;
    }

    /// <summary>Whisper into a sleeper's dream (a newer one replaces an older). True if it was.</summary>
    public static bool Whisper(Simulation sim, Entity ghost, Entity sleeper, string? text)
    {
        if (Problem(sim, ghost, sleeper, text) != null)
            return false;
        var w = sim.World;
        w.Get<Ghost>(ghost).Presence -= Cost;
        var dream = new Dream { Text = Hauntings.Words(text, MaxWords, MaxLetters)!, Tick = sim.Clock.Tick };
        if (w.Has<Dream>(sleeper))
            w.Get<Dream>(sleeper) = dream;
        else
            w.Add(sleeper, dream);
        return true;
    }

    /// <summary>Each second: those who've woken remember their dream (a villager's comforted by it); old dreams fade.</summary>
    internal static void Update(Simulation sim)
    {
        var w = sim.World;
        var now = sim.Clock.Tick;
        foreach (var e in w.Query<Dream>())
        {
            ref var d = ref w.Get<Dream>(e);
            if (d.WokeTick == 0)
            {
                if (w.TryGet<Mind>(e, out var mind) && !mind.Asleep && w.TryGet<Creature>(e, out var c) && !c.Dead)
                {
                    d.WokeTick = now;
                    if (!w.Has<PlayerCharacter>(e) && !w.Has<Avatar>(e))
                        MoodSystem.Remember(sim, e, sim.Defs.Thoughts.DreamtOfTheDead);
                }
                else if (now - d.Tick > KeptTicks)
                    w.Remove<Dream>(e);
            }
            else if (now - d.WokeTick > KeptTicks)
                w.Remove<Dream>(e);
        }
    }
}

/// <summary>A ghost whispers into a sleeper's dream (<see cref="Dreams.Whisper"/>).</summary>
public sealed record WhisperCommand(Entity Actor, Entity Sleeper, string Text) : ISimCommand, IActorCommand
{
    public void Apply(Simulation sim) => Dreams.Whisper(sim, Actor, Sleeper, Text);
}

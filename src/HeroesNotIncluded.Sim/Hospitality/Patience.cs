using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Hospitality;

/// <summary>
/// A guest's patience with a player's fists (<see cref="Patience"/>): who's been hitting them, how many
/// punches' worth they're holding against them, and when the last one landed (it fades from there).
/// </summary>
public struct Grudge
{
    public Entity By;
    public float Count;
    public long Last;
}

/// <summary>
/// A player's fists in the inn (docs/DESIGN.md, "Bar fights"). A crowded inn is elbows and swings gone
/// wide, so a guest puts up with a punch or two (<see cref="BrawlDef.Tolerance"/>, fewer if they're rowdy
/// or loud with drink) with a word and a glare, and then hits back: that's a brawl
/// (<see cref="BrawlCrisis.HitBack"/>), the one the player started. While it's on, anyone else they
/// hit joins at once. Well water calms it all down (<see cref="Doused"/>). No raid on: then fists are
/// for the raiders. Decided in the sim from the blows that land, so every peer agrees.
/// </summary>
public static class Patience
{
    /// <summary>A guest the inn's patience applies to: a person the AI steers, on their feet, not in a brawl or on their way.</summary>
    public static bool Guest(Simulation sim, Entity e)
    {
        var w = sim.World;
        return w.IsAlive(e) && w.TryGet<Creature>(e, out var c) && c is { Able: true, Def.Race: Race.Human, Faction: Faction.Adventurer or Faction.Village }
            && !Players.Steered(sim, e) && !w.Has<Brawler>(e) && !w.Has<Leaving>(e);
    }

    /// <summary>How many punches' worth <paramref name="e"/> takes before hitting back.</summary>
    public static float Limit(Simulation sim, Entity e)
    {
        var tolerance = sim.Defs.Season.Brawl.Tolerance;
        var short_ = Adventurers.Tempers.Of(sim, e) == Adventurers.Temper.Rowdy || DrinkingSystem.LevelOf(sim, e) >= DrinkingSystem.LoudAt;
        return Math.Max(1f, short_ ? tolerance - 1 : tolerance);
    }

    /// <summary>A blow of <paramref name="attacker"/>'s landed on <paramref name="target"/> (<see cref="Blows"/>): their patience, or a brawl.</summary>
    public static void Struck(Simulation sim, Entity attacker, Entity target, bool heavy)
    {
        var w = sim.World;
        if (!Players.Steered(sim, attacker) || w.Has<Brawler>(attacker) || !Guest(sim, target) || HouseRules.RaidOn(sim)
            || !w.TryGet<Position>(target, out var at))
            return;
        var inn = HouseRules.RuledBuilding(sim, at.Cell);
        if (inn.IsNull)
            return;
        // A brawl they started is on: nobody gives them the benefit of the doubt now.
        if (BrawlCrisis.In(sim, inn) is { } brawl && brawl.Instigator == attacker)
        {
            w.Remove<Grudge>(target);
            BrawlCrisis.HitBack(sim, target, attacker, inn);
            return;
        }
        var now = sim.Clock.Tick;
        var fade = Math.Max(1f, sim.Defs.Season.Brawl.GrudgeFadeSeconds * SimClock.TicksPerSecond);
        var g = w.TryGet<Grudge>(target, out var was) && was.By == attacker
            ? was with { Count = Math.Max(0f, was.Count - (now - was.Last) / fade) }
            : new Grudge { By = attacker };
        g.Count += heavy ? 2 : 1;
        g.Last = now;
        var limit = Limit(sim, target);
        if (g.Count >= limit)
        {
            w.Remove<Grudge>(target);
            BrawlCrisis.HitBack(sim, target, attacker, inn);
            return;
        }
        if (w.Has<Grudge>(target))
            w.Get<Grudge>(target) = g;
        else
            w.Add(target, g);
        if (!SpeechSystem.CanAct(sim, target))
            return;
        var name = w.Get<Creature>(attacker).Name;
        if (g.Count >= limit - 1)
            Barks.Say(sim, target, "brawl_warned", Volume.Say, "Do that again. Go on.", who: name);
        else
            Barks.Say(sim, target, "brawl_bumped", Volume.Say, "Oi! Watch it!", who: name);
        Emotes.Show(sim, target, Emote.Angry, null);
    }

    /// <summary>
    /// A bucket's water has landed on <paramref name="e"/> (<see cref="Environment.DrenchSystem"/>): well water
    /// sobers a drunk (<see cref="BrawlDef.DouseSobers"/>), cools a grudge, and puts a fighter out of it, sober or
    /// not, unless they're so far gone they're still past clumsy after it. True if they've said something of
    /// their own about it (so the splash's own spluttering is left out).
    /// </summary>
    public static bool Doused(Simulation sim, Entity e)
    {
        var w = sim.World;
        w.Remove<Grudge>(e);
        if (w.Has<Tipsy>(e))
            w.Get<Tipsy>(e).Level = Math.Max(0f, w.Get<Tipsy>(e).Level - sim.Defs.Season.Brawl.DouseSobers);
        if (DrinkingSystem.LevelOf(sim, e) >= DrinkingSystem.ClumsyAt)
            return false; // too far gone to notice
        var fighting = false;
        if (BrawlCrisis.Holding(sim, e) is { } brawl && BrawlCrisis.Fighting(sim, e))
        {
            brawl.Settled(sim, e);
            fighting = true;
        }
        else if (w.TryGet<Mind>(e, out var m) && m.Job is BrawlJob)
        {
            AiSystem.EndJob(sim, e, m, false); // and their foe's loop stops: nobody fights a man dripping (Brawls.Ready)
            fighting = true;
        }
        if (!fighting || !SpeechSystem.CanAct(sim, e))
            return false;
        Barks.Say(sim, e, "brawl_doused", Volume.Shout, "Pfah! All right! All right!");
        Emotes.Show(sim, e, Emote.Angry, null);
        return true;
    }
}

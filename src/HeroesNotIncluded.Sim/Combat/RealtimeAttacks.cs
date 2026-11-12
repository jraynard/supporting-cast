using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Combat;

/// <summary>An attack of the last moments, for the renderer. Not part of the sim state.</summary>
public sealed record ShotEvent(long Tick, float3 From, float3 To, bool Ranged, bool Hit);

/// <summary>
/// Every attack in the game: hunters shooting game, a cornered boar goring back. Uses the
/// odds and wounds of <see cref="CombatRules"/>, on a cooldown. The target is
/// <see cref="Provoked"/>, and an animal (with its herd-mates nearby) stops what it's doing to react.
/// </summary>
public static class RealtimeAttacks
{
    /// <summary>Herd-mates this close notice one of them getting hurt.</summary>
    public const int AlarmRadius = 14;

    /// <summary>Roll an attack if it's possible from where the attacker stands. Null if it isn't.</summary>
    public static AttackResult? TryAttack(Simulation sim, Entity attacker, Entity target, AttackDef attack)
    {
        var w = sim.World;
        if (!CombatRules.Chance(sim, attacker, target, attack).Possible)
            return null;
        var wasAlive = !w.Get<Creature>(target).Dead;
        var result = CombatRules.Resolve(sim, attacker, target, attack);

        var now = sim.Clock.Tick;
        sim.RecentShots.RemoveAll(s => now - s.Tick > SimClock.TicksPerSecond * 5);
        sim.RecentShots.Add(new ShotEvent(now, w.Get<Position>(attacker).Pos, w.Get<Position>(target).Pos, attack.Ranged, result.Outcome == AttackOutcome.Hit));

        var c = w.Get<Creature>(target);
        if (wasAlive && c.Dead && c.Def.Race == Race.Animal)
        {
            sim.Wildlife.Hunted++;
            w.Remove<HuntOrder>(target);
        }
        Provoke(sim, target, attacker);
        if (w.TryGet<Herd>(target, out var herd))
        {
            var at = w.Get<Position>(target).Cell;
            foreach (var mate in w.Query<Herd>())
                if (mate != target && w.Get<Herd>(mate).Id == herd.Id && Maps.Map.Distance(w.Get<Position>(mate).Cell, at) <= AlarmRadius)
                    Provoke(sim, mate, attacker);
        }
        return result;
    }

    /// <summary>Remember who did it, and make an animal think again right away.</summary>
    public static void Provoke(Simulation sim, Entity e, Entity by)
    {
        var w = sim.World;
        var c = w.Get<Creature>(e);
        if (!c.Able)
            return;
        var provoked = new Provoked { By = by, Tick = sim.Clock.Tick };
        if (w.Has<Provoked>(e))
            w.Get<Provoked>(e) = provoked;
        else
            w.Add(e, provoked);
        if (c.Def.Race != Race.Animal)
            return;
        var mind = w.Get<Mind>(e);
        if (mind.Job is AnimalAttackJob)
            return;
        AiSystem.EndJob(sim, e, mind, false);
        mind.NextThinkTick = sim.Clock.Tick;
    }

    /// <summary>The creature that hurt <paramref name="e"/> within the last <paramref name="withinTicks"/>, if it's still about.</summary>
    public static Entity ProvokedBy(Simulation sim, Entity e, int withinTicks)
    {
        var w = sim.World;
        if (!w.TryGet<Provoked>(e, out var p) || sim.Clock.Tick - p.Tick > withinTicks || !w.IsAlive(p.By) || !w.Get<Creature>(p.By).Able)
            return Entity.Null;
        return p.By;
    }
}

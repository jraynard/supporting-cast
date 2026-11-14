using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Combat;

/// <summary>An attack of the last moments, for the renderer. Not part of the sim state.</summary>
public sealed record ShotEvent(long Tick, float3 From, float3 To, bool Ranged, bool Hit);

/// <summary>
/// Every blow that lands (<see cref="Blows"/>): the wound (<see cref="CombatRules.Strike"/>), the shot for
/// the renderer, a hunt counted. The target is
/// <see cref="Provoked"/>, and an animal (with its herd-mates nearby) stops what it's doing to react.
/// </summary>
public static class RealtimeAttacks
{
    /// <summary>Herd-mates this close notice one of them getting hurt.</summary>
    public const int AlarmRadius = 14;

    /// <summary>
    /// A blow that's reached its target lands, no roll (<see cref="CombatRules.Strike"/>, with
    /// free movement), with all a blow's after-effects.
    /// </summary>
    public static AttackResult Strike(Simulation sim, Entity attacker, Entity target, AttackDef attack, float damageScale = 1f)
    {
        var wasAlive = !sim.World.Get<Creature>(target).Dead;
        var result = CombatRules.Strike(sim, attacker, target, attack, damageScale);
        After(sim, attacker, target, attack, result, wasAlive);
        return result;
    }

    /// <summary>An arrow that struck nobody, flying from <paramref name="attacker"/> to <paramref name="to"/>: for the renderer and the sound.</summary>
    public static void Flew(Simulation sim, Entity attacker, float3 to)
    {
        var now = sim.Clock.Tick;
        sim.RecentShots.RemoveAll(s => now - s.Tick > SimClock.TicksPerSecond * 5);
        sim.RecentShots.Add(new ShotEvent(now, sim.World.Get<Position>(attacker).Pos, to, Ranged: true, Hit: false));
    }

    /// <summary>What every attack leaves: the shot for the renderer, a hunt counted, and the target (and its herd) provoked.</summary>
    private static void After(Simulation sim, Entity attacker, Entity target, AttackDef attack, AttackResult result, bool wasAlive)
    {
        var w = sim.World;
        var now = sim.Clock.Tick;
        sim.RecentShots.RemoveAll(s => now - s.Tick > SimClock.TicksPerSecond * 5);
        sim.RecentShots.Add(new ShotEvent(now, w.Get<Position>(attacker).Pos, w.Get<Position>(target).Pos, attack.Ranged, result.Outcome == AttackOutcome.Hit));

        var c = w.Get<Creature>(target);
        if (wasAlive && c.Dead && c.Def.Race == Race.Animal)
        {
            sim.Wildlife.Hunted++;
            w.Remove<HuntOrder>(target);
            // A player's kill: the hunt's practice, and word of what to do with it.
            if (w.Has<Avatar>(attacker))
            {
                Systems.SkillRules.Learn(sim, attacker, Skill.Hunting, Ai.AvatarHunting.XpPerKill);
                sim.Messages.Add(MessageKind.Good, $"{w.Get<Creature>(attacker).Name} brought down the {c.Def.Label}. Butcher it for meat and hide.", sim.LocationOf(target));
            }
        }
        else if (w.Has<Avatar>(attacker) && c.Def.Race == Race.Animal)
            Systems.SkillRules.Learn(sim, attacker, Skill.Hunting, Ai.AvatarHunting.XpPerAttack);
        Provoke(sim, target, attacker);
        if (w.TryGet<Herd>(target, out var herd))
        {
            var at = w.Get<Position>(target).Cell;
            foreach (var mate in w.Query<Herd>())
                if (mate != target && w.Get<Herd>(mate).Id == herd.Id && Maps.Map.Distance(w.Get<Position>(mate).Cell, at) <= AlarmRadius)
                    Provoke(sim, mate, attacker);
        }
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

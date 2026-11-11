using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Combat;
using SupportingCast.Sim.Components;

namespace SupportingCast.Sim.Systems;

/// <summary>
/// Taking, keeping and letting go of prisoners. A downed raider marked for capture is carried to
/// a prisoner bed (Doctor work), becomes <see cref="Faction.Prisoner"/> and stays there, fed and
/// tended by wardens, until the player has them released or recruited. Prisoners have moods:
/// a content one listens to wardens more readily, a miserable one may break out
/// (<see cref="PrisonBreakSystem"/>), becoming a raider again until recaptured.
/// </summary>
public static class Prisoners
{
    /// <summary>Recruiting talks wear down resistance by this (plus a tenth per Social level) a visit.</summary>
    public const float ResistancePerVisit = 0.5f;

    /// <summary>Below this mood a prisoner may try to break out.</summary>
    public const float BreakBelow = 0.35f;

    /// <summary>Hourly break chance of a prisoner at mood 0 (about half a day's worth of tries).</summary>
    public const float MaxBreakPerHour = 0.03f;

    /// <summary>An able villager within this many cells, with a clear view, makes a break much less likely.</summary>
    public const int WatchRadius = 8;

    public const float WatchedFactor = 0.3f;

    /// <summary>Recaptured escapees start over with this much more resistance.</summary>
    public const float RecapturedResistance = 2f;

    public static bool IsPrisoner(Simulation sim, Entity e) => sim.World.Get<Creature>(e).Faction == Faction.Prisoner;

    /// <summary>Can this creature be marked for capture right now?</summary>
    public static bool Capturable(Simulation sim, Entity e)
    {
        var c = sim.World.Get<Creature>(e);
        return c is { Faction: Faction.Raider, Downed: true, Dead: false } && c.Def.Race == Defs.Race.Human;
    }

    /// <summary>A captured raider arrives in their bed: disarmed, no longer leaving, now the village's to keep.</summary>
    public static void Imprison(Simulation sim, Entity e, Entity bed)
    {
        var w = sim.World;
        sim.DropWeapon(e);
        w.Remove<Leaving>(e);
        w.Remove<CaptureOrder>(e);
        ref var c = ref w.Get<Creature>(e);
        var was = c.Faction;
        c.Faction = Faction.Prisoner;
        w.Remove<Prisoner>(e);
        var resistance = sim.Rand.Range(2f, 5f);
        if (w.Has<Escaping>(e))
        {
            w.Remove<Escaping>(e);
            resistance += RecapturedResistance;
        }
        w.Add(e, new Prisoner { Was = was, Mode = PrisonerMode.Hold, Resistance = resistance, LastVisit = sim.Clock.Tick });
        w.Remove<BedOwner>(e);
        w.Add(e, new BedOwner { Bed = bed });
        sim.Messages.Add(MessageKind.Good, $"{c.Name} has been taken prisoner.", w.Get<Position>(e).Cell);
    }

    /// <summary>Let a prisoner go: they head for the map edge and don't come back.</summary>
    public static void Release(Simulation sim, Entity e)
    {
        var w = sim.World;
        Free(sim, e);
        ref var c = ref w.Get<Creature>(e);
        c.Faction = Faction.Wild;
        w.Add(e, new Leaving());
        sim.Messages.Add(MessageKind.Info, $"{c.Name} has been released and is leaving.", w.Get<Position>(e).Cell);
    }

    /// <summary>A warden's daily talk. Returns true if the prisoner joined the village.</summary>
    public static bool Persuade(Simulation sim, Entity prisoner, Entity warden)
    {
        var w = sim.World;
        ref var p = ref w.Get<Prisoner>(prisoner);
        var social = SkillRules.LevelOf(sim, warden, Skill.Social);
        p.LastVisit = sim.Clock.Tick;
        SkillRules.Learn(sim, warden, Skill.Social, 300f);
        var name = w.Get<Creature>(prisoner).Name;
        var willing = PersuadeFactor(sim, prisoner, warden);
        var opinion = MoodRules.Opinion(sim, prisoner, warden);
        MoodSystem.Remember(sim, prisoner, sim.Defs.Thoughts.WardenTalkedWithMe, warden);
        if (p.Resistance > 0)
        {
            p.Resistance = Math.Max(0, p.Resistance - (ResistancePerVisit + 0.1f * social) * willing);
            sim.Messages.Add(MessageKind.Info, $"{w.Get<Creature>(warden).Name} talked with {name}"
                + (p.Resistance > 0 ? $" (resistance {p.Resistance:F1})." : ", who is starting to listen."), w.Get<Position>(prisoner).Cell);
            return false;
        }
        if (!sim.Rand.Chance(0.2f + 0.03f * social + Math.Clamp(opinion / 200f, -0.1f, 0.15f)))
        {
            sim.Messages.Add(MessageKind.Info, $"{name} isn't ready to join yet.", w.Get<Position>(prisoner).Cell);
            return false;
        }
        Free(sim, prisoner);
        w.Get<Creature>(prisoner).Faction = Faction.Village;
        w.Get<Mind>(prisoner).WorkPriority = Mind.DefaultPriorities();
        MoodSystem.Remember(sim, prisoner, sim.Defs.Thoughts.RecruitedMe, warden);
        sim.Messages.Add(MessageKind.Good, $"{name} has joined the village!", w.Get<Position>(prisoner).Cell);
        return true;
    }

    /// <summary>
    /// How readily a prisoner listens to a warden: ×1 at neutral mood and opinion, from ×0.4
    /// (miserable, hates them) to ×1.6 (content, likes them). Scales how far a talk wears down resistance.
    /// </summary>
    public static float PersuadeFactor(Simulation sim, Entity prisoner, Entity warden) =>
        Math.Clamp(0.6f + 0.8f * MoodRules.MoodOf(sim, prisoner) + MoodRules.Opinion(sim, prisoner, warden) / 100f, 0.4f, 1.6f);

    /// <summary>
    /// Chance per game hour that a prisoner tries to break out: none while content (mood at or
    /// above <see cref="BreakBelow"/>), downed, carried, asleep, or ready to join; rising as mood
    /// falls, scaled by how well they can walk, and much lower with a villager watching.
    /// </summary>
    public static float BreakChancePerHour(Simulation sim, Entity e)
    {
        var w = sim.World;
        if (!w.TryGet<Prisoner>(e, out var p) || p.Resistance <= 0 || p.Mode == PrisonerMode.Release)
            return 0f;
        var c = w.Get<Creature>(e);
        if (!c.Able || w.Has<Carried>(e) || w.Get<Mind>(e).Asleep)
            return 0f;
        var mood = MoodRules.MoodOf(sim, e);
        if (mood >= BreakBelow)
            return 0f;
        var chance = MaxBreakPerHour * (BreakBelow - mood) / BreakBelow;
        if (w.TryGet<Health>(e, out var h))
            chance *= h.Capacity(Defs.Capacity.Moving);
        return Watched(sim, e) ? chance * WatchedFactor : chance;
    }

    /// <summary>An able villager is close by with a clear view of the prisoner.</summary>
    public static bool Watched(Simulation sim, Entity e)
    {
        var w = sim.World;
        var at = w.Get<Position>(e).Pos + new float3(0, 0, 0.7f);
        foreach (var v in sim.Creatures())
        {
            var c = w.Get<Creature>(v);
            if (c.Faction != Faction.Village || !c.Able || c.Def.Race != Defs.Race.Human)
                continue;
            var vp = w.Get<Position>(v).Pos + new float3(0, 0, 0.7f);
            var d = vp - at;
            if (d.X * d.X + d.Y * d.Y + d.Z * d.Z <= WatchRadius * WatchRadius && CombatRules.LineOfFire(sim, vp, at))
                return true;
        }
        return false;
    }

    /// <summary>
    /// A prisoner breaks out: a raider again (<see cref="Escaping"/>), off to grab a weapon and
    /// run. The village is shaken; a fight starts if anyone sees them.
    /// </summary>
    public static void Escape(Simulation sim, Entity e)
    {
        var w = sim.World;
        Free(sim, e);
        ref var c = ref w.Get<Creature>(e);
        c.Faction = Faction.Raider; // whoever they were with before, they're the village's enemy now
        w.Add(e, new Escaping());
        w.Get<Mind>(e).Asleep = false;
        sim.Messages.Add(MessageKind.Danger, $"{c.Name} is breaking out!", w.Get<Position>(e).Cell);
        MoodSystem.RememberVillage(sim, sim.Defs.Thoughts.PrisonerEscaped);
    }

    private static void Free(Simulation sim, Entity e)
    {
        var w = sim.World;
        w.Remove<Prisoner>(e);
        w.Remove<BedOwner>(e);
        AiSystem.EndJob(sim, e, w.Get<Mind>(e), false);
    }
}

/// <summary>
/// Hourly: each unhappy prisoner may try to break out (<see cref="Prisoners.BreakChancePerHour"/>),
/// and unhappy prisoners nearby may join them. Rolls on <see cref="Simulation.Rand"/> only for
/// prisoners with a chance at all, so a village without unhappy prisoners never rolls.
/// </summary>
public sealed class PrisonBreakSystem : ISimSystem
{
    /// <summary>Unhappy prisoners within this many cells of a break may join it (a coin flip each).</summary>
    public const int JoinRadius = 10;

    public string Name => "PrisonBreak";
    public int Interval => SimClock.TicksPerHour;

    public void Update(Simulation sim)
    {
        var w = sim.World;
        var prisoners = w.Query<Prisoner>();
        prisoners.Sort((a, b) => a.Index.CompareTo(b.Index));
        foreach (var e in prisoners)
        {
            if (!w.IsAlive(e) || !w.Has<Prisoner>(e))
                continue; // joined an earlier break
            var chance = Prisoners.BreakChancePerHour(sim, e);
            if (chance <= 0 || !sim.Rand.Chance(chance))
                continue;
            var at = w.Get<Position>(e).Cell;
            Prisoners.Escape(sim, e);
            foreach (var o in prisoners)
                if (o != e && w.IsAlive(o) && w.Has<Prisoner>(o) && Combat.CombatRules.Distance(at, w.Get<Position>(o).Cell) <= JoinRadius
                    && Prisoners.BreakChancePerHour(sim, o) > 0 && sim.Rand.Chance(0.5f))
                    Prisoners.Escape(sim, o);
        }
    }
}

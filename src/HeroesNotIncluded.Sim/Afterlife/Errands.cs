using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Afterlife;

/// <summary>On a body whose ghost saw to some unfinished business: how much, for the reveal.</summary>
public struct Unfinished
{
    public int Done;
}

/// <summary>
/// The dead's unfinished business (docs/DESIGN.md, "Death"; data/defs/afterlife.json): a ghost
/// that lingers near someone an errand is for, long enough, does it: a mourner's grief lifts, a
/// sleeper rests better, a lost traveller knows the way. A little Renown each, then a rest. The
/// dead sense who needs them (<see cref="PerceptionView.Business"/>); the living see nothing.
/// </summary>
public static class Errands
{
    /// <summary>Is <paramref name="who"/> someone this errand is for right now?</summary>
    public static bool Qualifies(Simulation sim, GhostErrandDef def, Entity who)
    {
        var w = sim.World;
        if (!w.IsAlive(who) || !w.TryGet<Creature>(who, out var c) || c.Dead || c.Def.Race != Race.Human)
            return false;
        switch (def.Kind)
        {
            case GhostErrandKind.Grieving:
                var thoughts = sim.Defs.Thoughts;
                return c.Faction == Faction.Village && w.TryGet<Mood>(who, out var mood)
                    && mood.Memories.Exists(m => m.Def == thoughts.FriendDied || m.Def == thoughts.VillagerDied);
            case GhostErrandKind.Sleeper:
                return w.TryGet<Mind>(who, out var mind) && mind.Asleep && w.TryGet<Needs>(who, out var needs) && needs.Get(sim.Defs.Need("Rest")) < 0.95f;
            case GhostErrandKind.Lost:
                foreach (var ask in sim.Directions.Open)
                    if (ask.Asker == who)
                        return true;
                return false;
        }
        return false;
    }

    /// <summary>Within an errand's reach of a ghost: on its level, near enough.</summary>
    public static bool Near(Simulation sim, Entity ghost, Entity who, float reach)
    {
        var w = sim.World;
        var a = w.Get<Position>(ghost).Pos;
        var b = w.Get<Position>(who).Pos;
        return (int)a.Z == (int)b.Z && (a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) <= reach * reach;
    }

    /// <summary>Each ghost's lingering goes on, or finds someone new to linger by; and finishes what's lingered long enough.</summary>
    internal static void Update(Simulation sim, int ticks)
    {
        var w = sim.World;
        var defs = sim.Defs.AllGhostErrands;
        if (defs.Count == 0)
            return;
        foreach (var ghost in w.Query<Ghost>())
        {
            ref var g = ref w.Get<Ghost>(ghost);
            if (sim.Clock.Tick < g.RestUntil)
            {
                g.ErrandTarget = Entity.Null;
                continue;
            }
            if (!g.ErrandTarget.IsNull && g.Errand >= 0 && g.Errand < defs.Count
                && Qualifies(sim, defs[g.Errand], g.ErrandTarget) && Near(sim, ghost, g.ErrandTarget, defs[g.Errand].Reach))
            {
                g.ErrandTicks += ticks;
                if (g.ErrandTicks >= defs[g.Errand].Ticks)
                    Complete(sim, ghost, ref g, defs[g.Errand]);
                continue;
            }
            g.ErrandTarget = Entity.Null;
            g.ErrandTicks = 0;
            for (var i = 0; i < defs.Count && g.ErrandTarget.IsNull; i++)
                foreach (var e in sim.Creatures())
                    if (e != g.Body && Near(sim, ghost, e, defs[i].Reach) && Qualifies(sim, defs[i], e))
                    {
                        (g.Errand, g.ErrandTarget) = (i, e);
                        break;
                    }
        }
    }

    private static void Complete(Simulation sim, Entity ghost, ref Ghost g, GhostErrandDef def)
    {
        var w = sim.World;
        var who = g.ErrandTarget;
        var name = w.Get<Creature>(who).Name;
        switch (def.Kind)
        {
            case GhostErrandKind.Grieving:
                var thoughts = sim.Defs.Thoughts;
                w.Get<Mood>(who).Memories.RemoveAll(m => m.Def == thoughts.FriendDied || m.Def == thoughts.VillagerDied);
                break;
            case GhostErrandKind.Sleeper:
                w.Get<Needs>(who).Add(sim.Defs.Need("Rest"), 0.25f);
                break;
            case GhostErrandKind.Lost:
                foreach (var ask in sim.Directions.Open.ToList())
                    if (ask.Asker == who)
                        sim.Directions.Feel(sim, ask.Id);
                break;
        }
        sim.Reckoning.Add(sim, def.Renown, 0f);
        g.Done++;
        g.LastDone = def.Kind switch
        {
            GhostErrandKind.Grieving => $"{name}'s grief lifts a little.",
            GhostErrandKind.Sleeper => $"{name} sleeps easier.",
            _ => $"{name} knows the way now, somehow.",
        };
        g.ErrandTarget = Entity.Null;
        g.ErrandTicks = 0;
        g.RestUntil = sim.Clock.Tick + SimClock.HoursToTicks(def.RestHours);
        if (w.IsAlive(g.Body))
        {
            if (!w.Has<Unfinished>(g.Body))
                w.Add(g.Body, new Unfinished());
            w.Get<Unfinished>(g.Body).Done++;
        }
    }
}

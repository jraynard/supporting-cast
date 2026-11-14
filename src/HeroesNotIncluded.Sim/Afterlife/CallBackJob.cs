using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Afterlife;

/// <summary>
/// An AI empath's Calling back (<see cref="Revival"/>): for a fresh body laid out on a pallet in
/// their shrine, they kneel a while and weave it, if their mana runs to it. Without a player at
/// the shrine, the dead still have a way back, but only if someone carries them there.
/// </summary>
public sealed class CallBackJob(Entity body) : Job
{
    /// <summary>How long the rite takes on its knees, ticks.</summary>
    public const int RiteTicks = 300;

    public override Entity Focus => body;

    /// <summary>A job for this pawn, if they're an empath of the shrine with mana enough and a fresh body lies on its pallets.</summary>
    public static Job? For(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        if (!w.TryGet<Mana>(pawn, out var mana) || !w.TryGet<Trade>(pawn, out var trade) || trade.Def.WorkplaceDef?.Name != "Shrine"
            || mana.Current < sim.Defs.Mending("CallBack").ManaCost)
            return null;
        var from = w.Get<Position>(pawn).Cell;
        foreach (var e in w.Query<Death>())
        {
            if (w.Has<Calling>(e) || !Revival.Fresh(sim, e) || !Revival.OnShrinePallet(sim, e)
                || sim.Reservations.IsReservedByOther(e, pawn)
                || !Navigation.CanReach(sim, pawn, from, w.Get<Position>(e).Cell, PathEnd.Touch))
                continue;
            return new CallBackJob(e);
        }
        return null;
    }

    protected override IEnumerable<JobStatus> Run()
    {
        var name = Get<Creature>(body).Name;
        Report = $"Calling {name} back";
        if (!Reserve(body)) yield break;
        foreach (var s in GoToEntity(body, PathEnd.Touch)) yield return s;
        if (HasFailed) yield break;
        foreach (var s in Wait(RiteTicks)) yield return s;
        if (!Alive(body) || !Get<Creature>(body).Dead || !Revival.Fresh(Sim, body) || !Pathfinder.Touches(PawnCell, Sim.LocationOf(body)))
        {
            Fail("the body's gone or gone cold");
            yield break;
        }
        ref var mana = ref Sim.World.Get<Mana>(Pawn);
        var cost = Sim.Defs.Mending("CallBack").ManaCost;
        if (mana.Current < cost)
        {
            Fail("not enough mana");
            yield break;
        }
        mana.Current -= cost;
        var medicine = SkillRules.LevelOf(Sim, Pawn, Skill.Medicine);
        Revival.Call(Sim, Pawn, Math.Clamp(0.4f + 0.03f * medicine, 0.3f, 0.8f));
    }
}

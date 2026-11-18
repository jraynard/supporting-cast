using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Afterlife;

/// <summary>
/// An AI empath's Calling back (<see cref="Revival"/>): for a fresh body laid out on a pallet in
/// their shrine, they kneel a while and weave it, if their mana runs to it. Without a player at
/// the shrine, the dead still have a way back, but only if someone carries them there. The rite's
/// for two: they ask the nearest villager willing to hold the body (<see cref="KeepVigilJob"/>), and
/// wait a while for them; a keeper who slips costs the weave, as a player's does.
/// </summary>
public sealed class CallBackJob(Entity body) : Job
{
    /// <summary>How long the rite takes on its knees, ticks.</summary>
    public const int RiteTicks = 300;

    /// <summary>How long they'll wait for someone to come and hold the body.</summary>
    public const int KeeperWaitTicks = SimClock.TicksPerHour;

    /// <summary>How far they'll look for a keeper, cells; and the runes a calling's keeper braces against.</summary>
    public const int KeeperReach = 30, Pulls = 4;

    public override Entity Focus => body;

    /// <summary>A job for this pawn, if they're an empath of the shrine with mana enough and a fresh body lies on its pallets.</summary>
    public static Job? For(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        if (!w.TryGet<Mana>(pawn, out var mana) || !w.TryGet<Trade>(pawn, out var trade) || trade.Def.Role != TownRole.Shrine
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

        // Someone to hold them: whoever's kneeling there for us already, or the nearest willing.
        var keeper = Keeper();
        if (keeper.IsNull)
        {
            var asked = Ask();
            if (asked.IsNull)
            {
                Fail("nobody to hold the body");
                yield break;
            }
            Sim.Speech.Say(Sim, Pawn, $"Hold {name} for me, {Get<Creature>(asked).Name}. I can't call them back alone.", Speech.Volume.Say);
            for (var t = 0; t < KeeperWaitTicks && (keeper = Keeper()).IsNull; t += 30)
            {
                Report = $"Waiting for someone to hold {name}";
                foreach (var s in Wait(30)) yield return s;
                if (!Alive(body) || !Alive(asked))
                    break;
            }
            if (keeper.IsNull)
            {
                Fail("nobody came to hold the body");
                yield break;
            }
        }
        Report = $"Calling {name} back, {Get<Creature>(keeper).Name} holding them";
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
        var quality = Math.Clamp(0.4f + 0.03f * medicine, 0.3f, 0.8f);
        // The keeper braces against each pull; a slip costs that rune's share of the weave.
        var still = Keeper();
        for (var i = 0; i < Pulls; i++)
            if (still.IsNull || Sim.Rand.NextFloat() < Math.Max(0.04f, 0.14f - 0.01f * SkillRules.LevelOf(Sim, still, Skill.Medicine)))
                quality -= quality / Pulls;
        Revival.Call(Sim, Pawn, quality, still);
    }

    /// <summary>Who's kneeling at the body for this calling (a <see cref="KeepVigilJob"/> for us), or Entity.Null.</summary>
    private Entity Keeper()
    {
        var w = Sim.World;
        foreach (var e in Sim.Creatures())
            if (w.TryGet<Mind>(e, out var mind) && mind.Job is KeepVigilJob vigil && vigil.Body == body && vigil.Empath == Pawn
                && vigil.Kneeling && Revival.BodyBeside(Sim, e) == body)
                return e;
        return Entity.Null;
    }

    /// <summary>The nearest villager willing to hold the body, sent to it. Entity.Null if there's none.</summary>
    private Entity Ask()
    {
        var w = Sim.World;
        var at = Sim.LocationOf(body);
        var best = Entity.Null;
        var bestDist = int.MaxValue;
        foreach (var e in Sim.Creatures())
        {
            if (e == Pawn || w.Get<Creature>(e).Def.Race != Race.Human || !HeroesNotIncluded.Sim.Work.Pairs.Willing(Sim, e, Pawn))
                continue;
            var d = Map.Distance(w.Get<Position>(e).Cell, at);
            if (d > KeeperReach || d >= bestDist || !Navigation.CanReach(Sim, e, w.Get<Position>(e).Cell, at, PathEnd.Touch))
                continue;
            (best, bestDist) = (e, d);
        }
        if (!best.IsNull)
            AiSystem.Order(Sim, best, new KeepVigilJob(body, Pawn));
        return best;
    }
}

/// <summary>
/// Kneeling at a body to hold it while an AI empath calls it back (<see cref="CallBackJob"/>): there
/// till the empath's done (or gives up), or an hour at most.
/// </summary>
public sealed class KeepVigilJob(Entity body, Entity empath) : Job
{
    public Entity Body => body;
    public Entity Empath => empath;

    /// <summary>At the body and holding it.</summary>
    public bool Kneeling { get; private set; }

    public override Entity Focus => body;

    protected override IEnumerable<JobStatus> Run()
    {
        Report = $"Going to hold {Get<Creature>(body).Name}";
        foreach (var s in GoToEntity(body, PathEnd.Touch)) yield return s;
        if (HasFailed) yield break;
        Kneeling = true;
        Report = $"Holding {Get<Creature>(body).Name}";
        for (var t = 0; t < SimClock.TicksPerHour && Alive(body) && Alive(empath)
            && Sim.World.TryGet<Mind>(empath, out var mind) && mind.Job is CallBackJob; t++)
            yield return JobStatus.Running;
    }
}

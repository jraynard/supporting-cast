using DragonWorld.Sim.Components;
using DragonWorld.Sim.Core;
using DragonWorld.Sim.Defs;
using DragonWorld.Sim.Maps;
using DragonWorld.Sim.Systems;

namespace DragonWorld.Sim.Ai;

/// <summary>
/// Adventure mode's way of directing companions: the avatar asks, and the companion's brain fits the
/// request around its own needs (see <see cref="Request"/>). Asking replaces direct control.
/// </summary>
public static class Requests
{
    /// <summary>Why a companion can't be asked (or null): only able village people other than the asker.</summary>
    public static string? CantAsk(Simulation sim, Entity from, Entity companion)
    {
        var w = sim.World;
        // Commands come off the network: either side may be any entity, or none.
        if (!w.Has<Creature>(companion) || !w.Has<Creature>(from) || companion == from)
            return "nobody to ask";
        var c = w.Get<Creature>(companion);
        if (c.Dead ||c.Faction != Faction.Village || c.Def.Race != Race.Human || w.Has<Avatar>(companion))
            return "not one of your companions";
        return null;
    }

    /// <summary>Below this chance a companion drops a request they'd taken on.</summary>
    public const float DropBelow = 0.3f;

    /// <summary>How much mood (around 0.5) and opinion of the asker (per 100 points) move the chance of agreeing.</summary>
    public const float MoodWeight = 0.8f, OpinionWeight = 0.8f;

    /// <summary>Asked while this unhappy, a companion goes along but holds it against you.</summary>
    public const float BossyMoodBelow = 0.35f;

    /// <summary>
    /// How likely <paramref name="companion"/> is to agree to <paramref name="kind"/> from
    /// <paramref name="from"/>: likely by default, less when they're down or think little of the
    /// asker, more for the egg (everyone cares about the egg). The reason names what weighs most.
    /// </summary>
    public static (float Chance, string Reason) Willingness(Simulation sim, Entity from, Entity companion, RequestKind kind)
    {
        var mood = MoodRules.MoodOf(sim, companion);
        var opinion = MoodRules.Opinion(sim, companion, from);
        var chance = Math.Clamp(0.85f + (mood - 0.5f) * MoodWeight + opinion / 100f * OpinionWeight + (kind == RequestKind.GuardEgg ? 0.1f : 0f), 0.05f, 1f);
        var moodDrag = Math.Max(0f, 0.5f - mood) * MoodWeight;
        var opinionDrag = Math.Max(0f, -opinion / 100f * OpinionWeight);
        var reason = moodDrag > 0.05f && moodDrag >= opinionDrag ? $"they're {MoodRules.Describe(mood)}"
            : opinion <= MoodRules.RivalAt ? "they can't stand you"
            : opinionDrag > 0.02f ? "they don't think much of you"
            : chance >= 0.95f ? "they're glad to help"
            : "they have their own work to do";
        return (chance, reason);
    }

    /// <summary>They said no to <paramref name="from"/> within the hour, and won't be asked again yet.</summary>
    public static bool RecentlyRefused(Simulation sim, Entity from, Entity companion) =>
        sim.World.TryGet<Mood>(companion, out var mood)
        && mood.Memories.Exists(m => m.Def == sim.Defs.Thoughts.TurnedYouDown && m.Other == from);

    /// <summary>What a guard should watch over for <paramref name="from"/>: the egg, or once hatched, their dragon.</summary>
    public static Entity GuardTarget(Simulation sim, Entity from)
    {
        var w = sim.World;
        var held = w.IsAlive(from) ? w.Get<Carrying>(from).Item : Entity.Null;
        if (!held.IsNull && w.Has<Egg>(held))
            return held;
        // Their own egg first, then one nobody owns; never another player's.
        var unowned = Entity.Null;
        foreach (var egg in w.Query<Egg>())
        {
            var keeper = w.Get<Egg>(egg).Keeper;
            if (keeper == from)
                return egg;
            if (unowned.IsNull && !w.IsAlive(keeper))
                unowned = egg;
        }
        if (!unowned.IsNull)
            return unowned;
        foreach (var d in w.Query<Dragon>())
            if (w.Get<Dragon>(d).Keeper == from && !w.Get<Creature>(d).Dead)
                return d;
        return Entity.Null;
    }

    /// <summary>The job a standing request calls for now, or null (none, or nothing left to do it for).</summary>
    public static Job? JobFor(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        if (!w.TryGet<Request>(pawn, out var req))
            return null;
        var name = w.Get<Creature>(pawn).Name;
        var target = req.Target;
        if (!Alive(sim, target))
        {
            // The egg hatched (or was lost): guard the dragon instead, if there is one.
            target = req.Kind == RequestKind.GuardEgg ? GuardTarget(sim, req.From) : Entity.Null;
            if (target.IsNull)
            {
                w.Remove<Request>(pawn);
                sim.Messages.Add(MessageKind.Info, $"{name} goes back to their own work.", w.Get<Position>(pawn).Cell);
                return null;
            }
            w.Get<Request>(pawn).Target = target;
        }
        if (Willingness(sim, req.From, pawn, req.Kind) is { Chance: < DropBelow } unwilling)
        {
            w.Remove<Request>(pawn);
            sim.Messages.Add(MessageKind.Warning, $"{name} stops {(req.Kind == RequestKind.Follow ? "following you" : "standing guard")}: {unwilling.Reason}.",
                w.Get<Position>(pawn).Cell);
            return null;
        }
        return req.Kind == RequestKind.Follow ? new EscortJob(target, 600) : new GuardJob(target, 600);
    }

    private static bool Alive(Simulation sim, Entity e) =>
        sim.World.IsAlive(e) && !(sim.World.TryGet<Creature>(e, out var c) && c.Dead);
}

/// <summary>Ask a companion to follow, guard the egg, or (with a null kind) go back to their own work.</summary>
public sealed record RequestCommand(Entity From, Entity Companion, RequestKind? Kind) : ISimCommand, IActorCommand
{
    public Entity Actor => From;

    public void Apply(Simulation sim)
    {
        if (Requests.CantAsk(sim, From, Companion) != null)
            return;
        var w = sim.World;
        var name = w.Get<Creature>(Companion).Name;
        var at = w.Get<Position>(Companion).Cell;
        if (Kind is not { } kind)
        {
            if (w.Has<Request>(Companion))
                w.Remove<Request>(Companion);
            sim.Messages.Add(MessageKind.Info, $"{name} goes back to their own work.", at);
        }
        else
        {
            var target = kind == RequestKind.Follow ? From : Requests.GuardTarget(sim, From);
            if (target.IsNull)
                return;
            if (Requests.RecentlyRefused(sim, From, Companion))
            {
                sim.Messages.Add(MessageKind.Info, $"{name} has already said no. Give it an hour.", at);
                return;
            }
            var (chance, reason) = Requests.Willingness(sim, From, Companion, kind);
            if (!sim.Rand.Chance(chance))
            {
                MoodSystem.Remember(sim, Companion, sim.Defs.Thoughts.TurnedYouDown, From);
                sim.Messages.Add(MessageKind.Warning, $"{name} won't: {reason}.", at);
                return;
            }
            if (MoodRules.MoodOf(sim, Companion) < Requests.BossyMoodBelow)
                MoodSystem.Remember(sim, Companion, sim.Defs.Thoughts.BossedAround, From);
            w.Add(Companion, new Request { Kind = kind, Target = target, From = From });
            sim.Messages.Add(MessageKind.Info, kind == RequestKind.Follow
                ? $"{name} will follow {w.Get<Creature>(From).Name}."
                : $"{name} will stand guard over the {(w.Has<Egg>(target) ? "egg" : w.Get<Creature>(target).Name)}.", at);
        }
        // Drop whatever they were doing, so the request takes effect now.
        AiSystem.EndJob(sim, Companion, w.Get<Mind>(Companion), false);
    }
}

/// <summary>Stay close behind someone (within a couple of cells), for a while.</summary>
public sealed class EscortJob(Entity leader, int ticks) : Job
{
    public override bool Interruptible => true;
    public override Entity Focus => leader;

    protected override IEnumerable<JobStatus> Run()
    {
        Report = $"Following {Get<Creature>(leader).Name}";
        for (var t = 0; t < ticks; t += 20)
        {
            if (!Alive(leader))
                yield break;
            if (Map.Distance(PawnCell, Sim.LocationOf(leader)) > 2)
            {
                foreach (var s in GoToEntity(leader, PathEnd.Touch, maxTicks: 600)) yield return s;
                if (HasFailed) yield break;
            }
            foreach (var s in Wait(20)) yield return s;
        }
    }
}

/// <summary>Keep within a few cells of something precious (the egg wherever it is, or a young dragon), for a while.</summary>
public sealed class GuardJob(Entity ward, int ticks) : Job
{
    public const int Radius = 3;

    public override bool Interruptible => true;
    public override Entity Focus => ward;

    protected override IEnumerable<JobStatus> Run()
    {
        Report = Sim.World.Has<Egg>(ward) ? "Guarding the egg" : $"Guarding {Get<Creature>(ward).Name}";
        for (var t = 0; t < ticks; t += 30)
        {
            if (!Alive(ward))
                yield break;
            if (Map.Distance(PawnCell, Sim.LocationOf(ward)) > Radius)
            {
                foreach (var s in GoToEntity(ward, PathEnd.Touch, maxTicks: 900)) yield return s;
                if (HasFailed) yield break;
            }
            foreach (var s in Wait(30)) yield return s;
        }
    }
}

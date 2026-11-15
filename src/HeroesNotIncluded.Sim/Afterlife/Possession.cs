using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Afterlife;

/// <summary>
/// On an animal a ghost has got into (<see cref="Possession"/>): its brain is quiet and it moves and
/// bites as the ghost steers it, through free movement and blows like a player's character. Never
/// says whose ghost: the living see only a beast behaving oddly.
/// </summary>
public struct Possessed
{
    public Entity Ghost;
    public long Since;
}

/// <summary>
/// The dead riding the living (docs/DESIGN.md, "Death"): a ghost with presence enough can slip into
/// a wild beast near it (a hare, a deer, a pig, a boar; never a raid's wolves or bears, which are
/// someone else's) and steer it, bite with it, until its presence is spent, the beast falls or it lets
/// go. Being in a beast spends presence by the beast's size; it gathers none meanwhile. Let go, the
/// beast bolts from where it stood.
/// </summary>
public static class Possession
{
    /// <summary>What getting in costs.</summary>
    public const float Cost = 0.4f;

    /// <summary>Presence spent each game hour in a beast, times (1 + its mass / 100): a hare's cheap, a bear's dear.</summary>
    public const float DrainPerHour = 0.25f;

    /// <summary>How near a beast must be to get into, cells.</summary>
    public const float Reach = 2f;

    /// <summary>The beast this ghost's in, or null.</summary>
    public static Entity Ridden(Simulation sim, Entity ghost)
    {
        var w = sim.World;
        if (!w.IsAlive(ghost) || !w.TryGet<Ghost>(ghost, out var g) || g.Riding.IsNull || !w.IsAlive(g.Riding)
            || !w.TryGet<Possessed>(g.Riding, out var p) || p.Ghost != ghost)
            return Entity.Null;
        return g.Riding;
    }

    /// <summary>Could this beast be got into at all (whoever's near)?</summary>
    public static bool Possessable(Simulation sim, Entity animal) =>
        sim.World.IsAlive(animal) && sim.World.TryGet<Creature>(animal, out var c) && c.Able && c.Def.Race == Race.Animal
        && c.Faction == Faction.Wild && !sim.World.Has<Possessed>(animal) && !sim.World.Has<Carried>(animal);

    /// <summary>Why this ghost can't get into that beast now, or null if it can.</summary>
    public static string? Problem(Simulation sim, Entity ghost, Entity animal)
    {
        var w = sim.World;
        if (!w.IsAlive(ghost) || !w.TryGet<Ghost>(ghost, out var g))
            return "you can't";
        if (!g.Riding.IsNull)
            return "you're in one already";
        if (!w.IsAlive(animal) || !w.TryGet<Creature>(animal, out var c) || c.Def.Race != Race.Animal)
            return "there's no beast there";
        if (c.Faction != Faction.Wild)
            return "it's someone else's beast: it throws you off";
        if (!Possessable(sim, animal))
            return "it won't have you now";
        if (!Errands.Near(sim, ghost, animal, Reach))
            return "you need to be nearer it";
        if (g.Presence < Cost - 0.0001f)
            return "you haven't the presence yet; it gathers in time";
        return null;
    }

    /// <summary>The nearest beast a ghost could get into, within reach; null if none.</summary>
    public static Entity Nearest(Simulation sim, Entity ghost)
    {
        var w = sim.World;
        if (!w.IsAlive(ghost) || !w.Has<Position>(ghost))
            return Entity.Null;
        var at = w.Get<Position>(ghost).Pos;
        var best = Entity.Null;
        var bestD = float.MaxValue;
        foreach (var e in sim.Creatures())
        {
            if (!Possessable(sim, e) || !Errands.Near(sim, ghost, e, Reach))
                continue;
            var p = w.Get<Position>(e).Pos;
            var d = (p.X - at.X) * (p.X - at.X) + (p.Y - at.Y) * (p.Y - at.Y);
            if (d < bestD)
                (best, bestD) = (e, d);
        }
        return best;
    }

    /// <summary>Get into a beast: its job's dropped, it stands still, and it's the ghost's to steer. True if it took.</summary>
    public static bool Possess(Simulation sim, Entity ghost, Entity animal)
    {
        if (Problem(sim, ghost, animal) != null)
            return false;
        var w = sim.World;
        ref var g = ref w.Get<Ghost>(ghost);
        g.Presence -= Cost;
        g.Riding = animal;
        g.ErrandTarget = Entity.Null;
        g.ErrandTicks = 0;
        var mind = w.Get<Mind>(animal);
        if (mind.Job != null)
            AiSystem.EndJob(sim, animal, mind, false);
        w.Get<Mover>(animal).Stop();
        w.Add(animal, new Possessed { Ghost = ghost, Since = sim.Clock.Tick });
        if (w.Has<FreeMover>(animal))
        {
            ref var m = ref w.Get<FreeMover>(animal);
            (m.Fighting, m.Wish, m.Drawn) = (false, float2.Zero, false);
        }
        else
            w.Add(animal, new FreeMover { ClaimBudget = FreeMoveSystem.MaxClaim });
        w.Get<Position>(ghost) = w.Get<Position>(animal);
        return true;
    }

    /// <summary>Let go of the beast (if in one): it bolts from where it stands, startled by what was in it.</summary>
    public static void Release(Simulation sim, Entity ghost)
    {
        var w = sim.World;
        if (!w.IsAlive(ghost) || !w.TryGet<Ghost>(ghost, out var g) || g.Riding.IsNull)
            return;
        var animal = g.Riding;
        w.Get<Ghost>(ghost).Riding = Entity.Null;
        if (!w.IsAlive(animal) || !w.TryGet<Possessed>(animal, out var p) || p.Ghost != ghost)
            return;
        Free(sim, animal);
    }

    /// <summary>The beast is its own again; if it's still on its feet, it shies from the spot.</summary>
    private static void Free(Simulation sim, Entity animal)
    {
        var w = sim.World;
        w.Remove<Possessed>(animal);
        if (w.Has<FreeMover>(animal))
            w.Get<FreeMover>(animal).Wish = float2.Zero; // FreeMoveSystem hands it back to the grid
        if (w.TryGet<Creature>(animal, out var c) && c.Able && w.Has<Position>(animal))
        {
            Hauntings.Leave(sim, w.Get<Position>(animal).Cell, HauntKind.Spook);
            w.Get<Mind>(animal).NextThinkTick = sim.Clock.Tick;
        }
    }

    /// <summary>Each riding ghost is where its beast is (every tick, so it sees from there).</summary>
    internal static void Follow(Simulation sim)
    {
        var w = sim.World;
        foreach (var e in w.Each<Possessed>())
        {
            var ghost = w.Get<Possessed>(e).Ghost;
            if (w.IsAlive(ghost) && w.Has<Ghost>(ghost) && w.Has<Position>(ghost))
                w.Get<Position>(ghost) = w.Get<Position>(e);
        }
    }

    /// <summary>Each second: presence spent, and a ghost thrown out when it's gone, the beast falls, or the round's over.</summary>
    internal static void Update(Simulation sim, int ticks)
    {
        var w = sim.World;
        foreach (var animal in w.Query<Possessed>())
        {
            var ghost = w.Get<Possessed>(animal).Ghost;
            var c = w.TryGet<Creature>(animal, out var cc) ? cc : default;
            if (!w.IsAlive(ghost) || !w.TryGet<Ghost>(ghost, out var g) || g.Riding != animal)
            {
                Free(sim, animal); // its ghost was called back, or has gone
                continue;
            }
            ref var gr = ref w.Get<Ghost>(ghost);
            var mass = c.Def?.Animal?.Mass ?? 40f;
            gr.Presence = Math.Max(0f, gr.Presence - DrainPerHour * (1f + mass / 100f) * ticks / SimClock.TicksPerHour);
            if (gr.Presence <= 0f || !c.Able || !sim.Round.Live)
                Release(sim, ghost);
        }
        // A ghost whose beast has died (and gone) is only a ghost again.
        foreach (var ghost in w.Query<Ghost>())
        {
            ref var g = ref w.Get<Ghost>(ghost);
            if (!g.Riding.IsNull && (!w.IsAlive(g.Riding) || !w.Has<Possessed>(g.Riding)))
                g.Riding = Entity.Null;
        }
    }
}

/// <summary>A ghost gets into a beast near it (<see cref="Possession.Possess"/>).</summary>
public sealed record PossessCommand(Entity Actor, Entity Animal) : ISimCommand, IActorCommand
{
    public void Apply(Simulation sim) => Possession.Possess(sim, Actor, Animal);
}

/// <summary>A ghost lets go of its beast (<see cref="Possession.Release"/>).</summary>
public sealed record ReleaseCommand(Entity Actor) : ISimCommand, IActorCommand
{
    public void Apply(Simulation sim) => Possession.Release(sim, Actor);
}

/// <summary>A riding ghost steers its beast, as <see cref="FreeMoveCommand"/> steers a character (At: where it is on the player's screen).</summary>
public sealed record RideMoveCommand(Entity Actor, float WishX, float WishY, float AtX = float.NaN, float AtY = float.NaN, int AtZ = 0)
    : ISimCommand, IActorCommand
{
    public void Apply(Simulation sim)
    {
        var animal = Possession.Ridden(sim, Actor);
        if (animal.IsNull || !sim.World.Has<FreeMover>(animal))
            return;
        var wish = Moves.Wish(WishX, WishY);
        FreeMoveSystem.Claim(sim, animal, AtX, AtY, AtZ, stopped: wish == float2.Zero);
        sim.World.Get<FreeMover>(animal).Wish = wish;
    }
}

/// <summary>A riding ghost bites (or gores) with its beast, the way it aims: it lands on whoever's in the sweep (<see cref="Reach"/>).</summary>
public sealed record RideAttackCommand(Entity Actor, bool Heavy, float Aim, float AtX = float.NaN, float AtY = float.NaN, int AtZ = 0, long Pressed = -1)
    : ISimCommand, IActorCommand
{
    public void Apply(Simulation sim)
    {
        var animal = Possession.Ridden(sim, Actor);
        if (animal.IsNull || !float.IsFinite(Aim) || !sim.World.Get<Creature>(animal).Able
            || CombatRules.Attacks(sim, animal).FirstOrDefault(a => !a.Ranged) is not { } attack)
            return;
        FreeMoveSystem.Claim(sim, animal, AtX, AtY, AtZ, stopped: false);
        Blows.Begin(sim, animal, Entity.Null, attack, Heavy, MathF.IEEERemainder(Aim, MathF.Tau), Pressed < 0 ? null : Pressed);
    }
}

/// <summary>A riding ghost's beast springs aside (<see cref="DodgeCommand"/>, for a beast).</summary>
public sealed record RideDodgeCommand(Entity Actor, float DirX, float DirY, long At, float AtX = float.NaN, float AtY = float.NaN, int AtZ = 0)
    : ISimCommand, IActorCommand
{
    public void Apply(Simulation sim)
    {
        var animal = Possession.Ridden(sim, Actor);
        if (animal.IsNull || !sim.World.Has<FreeMover>(animal) || !sim.World.Get<Creature>(animal).Able)
            return;
        FreeMoveSystem.Claim(sim, animal, AtX, AtY, AtZ, stopped: false);
        var at = Blows.Stamp(sim, At);
        if (Blows.Dodge(sim, animal, at))
            FreeMoveSystem.Dash(sim, animal, Moves.Wish(DirX, DirY), at);
    }
}

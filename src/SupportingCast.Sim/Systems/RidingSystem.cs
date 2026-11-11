using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Combat;
using SupportingCast.Sim.Components;

namespace SupportingCast.Sim.Systems;

/// <summary>
/// A keeper riding their grown dragon. The pair moves as the dragon: it's drafted while mounted,
/// and in a fight they are two units that move as one (see <see cref="Combat.Encounter.Move"/>);
/// so it goes where it's sent (if it obeys, which it does more readily with its keeper aboard),
/// walking or flying, and the rider rides along with their own AI at rest. Mounting and dismounting
/// happen on the ground; a mount that falls throws its rider, wherever it is.
/// </summary>
public sealed class RidingSystem : ISimSystem
{
    public string Name => "Riding";
    public int Interval => 60;

    /// <summary>Added to a dragon's obedience while its keeper rides it.</summary>
    public const float MountedObedience = 0.25f;

    /// <summary>Bond a mounted dragon gains per update (time together).</summary>
    public const float BondPerUpdate = 0.01f;

    /// <summary>Why <paramref name="rider"/> can't mount <paramref name="dragon"/> now, or null if they can.</summary>
    public static string? CantMount(Simulation sim, Entity rider, Entity dragon)
    {
        var w = sim.World;
        if (!w.IsAlive(dragon) || !w.TryGet<Dragon>(dragon, out var d))
            return "not a dragon";
        if (!Rescues.CarriedBy(sim, rider).IsNull)
            return "put down what you're carrying first";
        var stage = w.Get<Creature>(dragon).Stage(sim.Clock.Tick);
        if (!stage.CanBeRidden)
            return $"too young to ride ({stage.Label})";
        if (d.Keeper != rider)
            return "only its keeper can ride it";
        if (!w.Get<Creature>(rider).Able || !w.Get<Creature>(dragon).Able)
            return "not fit to ride";
        if (w.Has<Chained>(dragon))
            return "it's in chains";
        if (w.Has<Captive>(rider))
            return "you're locked up";
        if (w.Has<Mount>(dragon) || w.Has<Riding>(rider))
            return "already riding";
        if (sim.Combat.Involves(rider) || sim.Combat.Involves(dragon))
            return "not in the middle of a fight";
        if (Bonding.Peek(sim, dragon).Sulking)
            return "it's sulking, and won't have you on its back";
        if (d.Affinity < stage.MinRideAffinity)
            return $"it won't let anyone on its back yet (affinity {d.Affinity:F2} of {stage.MinRideAffinity:F2})";
        return null;
    }

    /// <summary>
    /// Ready to climb on right now: allowed (<see cref="CantMount"/>), side by side, and the dragon on
    /// the ground (not flying past).
    /// </summary>
    public static bool CanClimbOn(Simulation sim, Entity rider, Entity dragon)
    {
        var w = sim.World;
        if (CantMount(sim, rider, dragon) != null)
            return false;
        var at = sim.LocationOf(dragon);
        return !Combat.TacticalMoves.Airborne(sim, at) && !w.Get<Mover>(dragon).Moving && Ai.Pathfinder.Touches(sim.LocationOf(rider), at);
    }

    /// <summary>
    /// The player calls their dragon to ride it: it comes and waits beside them (<see cref="Ai.AwaitRiderJob"/>),
    /// and they climb on when it lands (<see cref="Ai.WaitForMountJob"/>). At once, if it's already there.
    /// </summary>
    public static void CallToRide(Simulation sim, Entity rider, Entity dragon)
    {
        if (CanClimbOn(sim, rider, dragon))
        {
            MountUp(sim, rider, dragon);
            return;
        }
        AiSystem.Order(sim, dragon, new Ai.AwaitRiderJob(rider));
        AiSystem.Order(sim, rider, new Ai.WaitForMountJob(dragon));
        sim.Messages.Add(MessageKind.Info, $"{sim.World.Get<Creature>(rider).Name} calls {sim.World.Get<Creature>(dragon).Name} down.", sim.LocationOf(rider));
    }

    /// <summary>Climb on: both stop what they're doing, and the dragon is drafted.</summary>
    public static void MountUp(Simulation sim, Entity rider, Entity dragon)
    {
        var w = sim.World;
        if (w.Get<Mind>(rider).Job is not MountJob) // that one ends by itself, having done this
            AiSystem.EndJob(sim, rider, w.Get<Mind>(rider), false);
        AiSystem.EndJob(sim, dragon, w.Get<Mind>(dragon), false);
        w.Add(dragon, new Mount { Rider = rider });
        w.Add(rider, new Riding { Mount = dragon });
        w.Get<Mind>(dragon).Drafted = true;
        w.Get<Position>(rider) = w.Get<Position>(dragon);
        sim.Messages.Add(MessageKind.Good, $"{w.Get<Creature>(rider).Name} climbs onto {w.Get<Creature>(dragon).Name}'s back.", sim.LocationOf(dragon));
    }

    /// <summary>
    /// Get off, onto the ground next to the dragon. Only once landed, unless <paramref name="thrown"/>
    /// (the mount went down): then the rider drops from wherever they are.
    /// </summary>
    public static bool Dismount(Simulation sim, Entity rider, bool thrown = false)
    {
        var w = sim.World;
        if (!w.TryGet<Riding>(rider, out var riding))
            return false;
        var dragon = riding.Mount;
        var at = w.Get<Position>(rider).Cell;
        if (!thrown && TacticalMoves.Airborne(sim, at))
            return false;
        w.Remove<Riding>(rider);
        if (w.IsAlive(dragon))
        {
            w.Remove<Mount>(dragon);
            w.Get<Mind>(dragon).Drafted = false;
            w.Get<Mover>(dragon).Stop();
        }
        var landing = at;
        if (!thrown)
            foreach (var c in sim.SpiralFrom(at, 2))
                if (c != at && sim.Map.Passable(c))
                {
                    landing = c;
                    break;
                }
        // Thrown riders fall from the mount's cell (MovementSystem drops and hurts them).
        w.Get<Position>(rider) = thrown ? w.Get<Position>(rider) : Position.At(landing);
        w.Get<Mind>(rider).NextThinkTick = sim.Clock.Tick;
        return true;
    }

    /// <summary>Whoever is riding <paramref name="dragon"/>, if anyone.</summary>
    public static Entity RiderOf(Simulation sim, Entity dragon) =>
        sim.World.TryGet<Mount>(dragon, out var m) && sim.World.IsAlive(m.Rider) ? m.Rider : Entity.Null;

    /// <summary>The mount when <paramref name="e"/> is a rider; otherwise <paramref name="e"/> itself.</summary>
    public static Entity Steed(Simulation sim, Entity e) =>
        sim.World.TryGet<Riding>(e, out var r) && sim.World.IsAlive(r.Mount) ? r.Mount : e;

    /// <summary>
    /// Send a mounted dragon (given it, or its rider) after game: it runs the animal down (or stoops on it
    /// from the air) and kills it, rider aboard, if it obeys (the Hunt with me trick helps). The kill is
    /// left where it falls, for butchering. Returns false if it refused or can't hunt that.
    /// </summary>
    public static bool OrderHunt(Simulation sim, Entity e, Entity prey)
    {
        var w = sim.World;
        var dragon = Steed(sim, e);
        if (!w.Has<Mount>(dragon) || !w.IsAlive(prey) || !w.TryGet<Creature>(prey, out var p) || p.Dead || p.Def.Animal == null
            || Combat.CombatRules.Attacks(sim, dragon).FirstOrDefault(a => !a.Ranged) == null)
            return false;
        if (!sim.Rand.Chance(DragonSystem.Obedience(sim, dragon, Defs.WorkType.Hunt)))
        {
            sim.Messages.Add(MessageKind.Info, $"{w.Get<Creature>(dragon).Name} won't give chase.", sim.LocationOf(dragon));
            return false;
        }
        AiSystem.Order(sim, dragon, new DragonHuntJob(prey, mounted: true));
        return true;
    }

    /// <summary>
    /// Send a mounted dragon (given it, or its rider) to a cell: walking or flying (always flying,
    /// with <paramref name="fly"/>), if it obeys. Returns false if it refused or there's no way there.
    /// </summary>
    public static bool OrderMove(Simulation sim, Entity e, int3 cell, bool fly = false)
    {
        var w = sim.World;
        var dragon = Steed(sim, e);
        if (!w.Has<Mount>(dragon))
            return false;
        if (!sim.Rand.Chance(DragonSystem.Obedience(sim, dragon)))
        {
            sim.Messages.Add(MessageKind.Info, $"{w.Get<Creature>(dragon).Name} ignores the reins.", sim.LocationOf(dragon));
            return false;
        }
        return Orders.MoveTo(sim, dragon, cell, fly);
    }

    public void Update(Simulation sim)
    {
        var w = sim.World;
        var food = sim.Defs.Need("Food");
        var rest = sim.Defs.Need("Rest");
        var bond = sim.Defs.Need("Bond");
        foreach (var dragon in w.Query<Mount>())
        {
            var rider = w.Get<Mount>(dragon).Rider;
            if (!w.IsAlive(rider) || !w.Get<Creature>(rider).Able || !w.Get<Creature>(dragon).Able)
            {
                Throw(sim, dragon, rider);
                continue;
            }
            w.Get<Needs>(dragon).Add(bond, BondPerUpdate);
            if (w.Get<Mover>(dragon).Moving || w.Get<Mind>(dragon).Job is DragonHuntJob)
                continue; // on the move, or on the hunt
            // Hungry or worn out (either of them): they get down. In a fight they stay up and fight
            // as a pair (the mount moves and breathes, the rider strikes from its back).
            if (sim.Combat.Involves(dragon) || sim.Combat.Involves(rider))
                continue;
            if (Urgent(sim, rider, food) || Urgent(sim, rider, rest) || Urgent(sim, dragon, food) || Urgent(sim, dragon, rest))
                Dismount(sim, rider);
        }
    }

    /// <summary>The mount is down or dead, or the rider is: they part, the hard way.</summary>
    public static void Throw(Simulation sim, Entity dragon, Entity rider)
    {
        var w = sim.World;
        if (w.IsAlive(rider) && w.Has<Riding>(rider))
        {
            Dismount(sim, rider, thrown: true);
            return;
        }
        if (w.IsAlive(dragon) && w.Has<Mount>(dragon))
        {
            w.Remove<Mount>(dragon);
            w.Get<Mind>(dragon).Drafted = false;
        }
    }

    /// <summary>Unseat anyone riding, or ridden by, a creature that just went down.</summary>
    public static void OnIncapacitated(Simulation sim, Entity e)
    {
        var w = sim.World;
        if (w.TryGet<Mount>(e, out var m))
            Throw(sim, e, m.Rider);
        else if (w.TryGet<Riding>(e, out var r))
            Throw(sim, r.Mount, e);
    }

    private static bool Urgent(Simulation sim, Entity e, Defs.NeedDef need) =>
        sim.World.Get<Needs>(e).Has[need.Index] && sim.World.Get<Needs>(e).Get(need) < need.UrgentBelow;
}

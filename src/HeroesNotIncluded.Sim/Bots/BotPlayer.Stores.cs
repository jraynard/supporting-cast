using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Bots;

/// <summary>Where a bot finds what it needs: what it can see lying about, or its own workplace's stock, as a careful player would.</summary>
public sealed partial class BotPlayer
{
    /// <summary>
    /// The nearest stack of a thing it knows of and may take: lying in sight or in the town's stockpile
    /// (everyone knows what the barn holds), or kept in its own workplace's stores
    /// (<see cref="Simulation.Ours"/>), never another's chest. Entity.Null if none.
    /// </summary>
    public static Entity Known(Simulation sim, Entity me, PerceptionView view, ThingDef def)
    {
        var w = sim.World;
        var here = w.Get<Position>(me).Cell;
        var best = Entity.Null;
        var bestD = int.MaxValue;
        foreach (var e in sim.AvailableItems())
        {
            var t = w.Get<Thing>(e);
            if (t.Def != def || !sim.IsAvailable(e) || sim.Reservations.IsReservedByOther(e, me) || !Ai.Claims.Free(sim, e, me)
                || (t.Spawned && !view.CanSee(e) && sim.Map.Zone[sim.Map.Idx(t.Cell)] == 0) || (sim.Round.Live && w.Has<Stored>(e) && !sim.Ours(e, me)))
                continue;
            var at = sim.LocationOf(e);
            var d = Maps.Map.Distance(here, at);
            if (d < bestD && sim.Regions.CanReach(here, at, t.Spawned ? PathEnd.OnTile : PathEnd.Touch))
                (best, bestD) = (e, d);
        }
        return best;
    }

    /// <summary>Some of a stack picked up from the ground, or taken out of the store it's kept in.</summary>
    public bool Fetch(Simulation sim, Entity me, PerceptionView view, Entity stack, int need, long now)
    {
        var w = sim.World;
        if (w.TryGet<Stored>(stack, out var stored))
            return AtStation(sim, me, stored.In, now, c =>
            {
                if (view.InContainer(c).Contains(stack))
                    Send(new Core.TakeFromStorageCommand(me, stack, need));
            });
        // A full pair of hands takes nothing more that won't go in the pack: put it down first.
        var t = w.Get<Thing>(stack);
        if (!w.Get<Carrying>(me).Item.IsNull && sim.PackOf(me) is var pack && (pack.IsNull || sim.ContainerRoom(pack, t.Def, t.Material) <= 0))
        {
            FreeHand(sim, me);
            return true;
        }
        WorkAt(sim, me, t.Cell, now);
        return true;
    }

    /// <summary>Walk up to a cell and work what's there with the key held (a stack to pick up).</summary>
    private void WorkAt(Simulation sim, Entity me, int3 cell, long now)
    {
        if (sim.World.Get<Mind>(me).Job is AvatarWalkJob { Interact: true } walk && walk.Target == cell)
            return; // on its way
        if (now - _lastWalk < ThinkTicks * 2)
            return;
        _lastWalk = now;
        Send(new Core.AvatarGoToCommand(me, cell, Interact: true));
    }
}

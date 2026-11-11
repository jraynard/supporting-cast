using DragonWorld.Sim.Components;
using DragonWorld.Sim.Systems;

namespace DragonWorld.Sim.Ai;

/// <summary>Direct player orders for drafted creatures (and mounted dragons): go there now.</summary>
public static class Orders
{
    /// <summary>
    /// Drop the current job and walk, or fly if it can (and it's worth it), to a cell. With
    /// <paramref name="fly"/> a flyer flies however short the way (else it walks). False if there's no way.
    /// </summary>
    public static bool MoveTo(Simulation sim, Entity pawn, int3 cell, bool fly = false)
    {
        var w = sim.World;
        AiSystem.EndJob(sim, pawn, w.Get<Mind>(pawn), false);
        var from = w.Get<Position>(pawn).Cell;
        var path = (fly ? Navigation.PlanFlight(sim, pawn, from, cell, PathEnd.OnTile) : null)
            ?? Navigation.PlanRoute(sim, pawn, from, cell, PathEnd.OnTile);
        if (path is null)
            return false;
        var mover = w.Get<Mover>(pawn);
        mover.Path = path;
        mover.PathIndex = 0;
        mover.StepProgress = 0;
        mover.StepFrom = from;
        return true;
    }
}

using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Maps;

namespace SupportingCast.Sim.Shops;

/// <summary>
/// An AI healer answers a Healing order at their shrine the plain way: bandages
/// (<see cref="TendJob"/>). The gift is for players; a villager in the trade binds wounds well
/// enough. The order pays once nothing's left untended.
/// </summary>
public static class HealOrders
{
    public static Job? For(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        if (!w.TryGet<Trade>(pawn, out var trade) || trade.Def.WorkplaceDef is not { } workplace)
            return null;
        var now = sim.Clock.Tick;
        var from = w.Get<Position>(pawn).Cell;
        foreach (var e in sim.Shops.All)
        {
            if (!e.IsOpen || e.Kind.Kind != ServiceKind.Healing || e.Kind.AtDef != workplace || !e.KnownBy.Contains(pawn)
                || (!e.Assignee.IsNull && e.Assignee != pawn) || !w.IsAlive(e.Poster)
                || !w.TryGet<Health>(e.Poster, out var h) || !h.Injuries.Exists(i => i.NeedsTending(now))
                || sim.Reservations.IsReservedByOther(e.Poster, pawn)
                || !Navigation.CanReach(sim, pawn, from, w.Get<Position>(e.Poster).Cell, PathEnd.Touch))
                continue;
            if (e.Assignee.IsNull && !sim.Shops.Accept(sim, pawn, e.Id))
                continue;
            return new TendJob(e.Poster);
        }
        return null;
    }
}

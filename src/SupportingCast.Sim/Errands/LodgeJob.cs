using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Maps;

namespace SupportingCast.Sim.Errands;

/// <summary>
/// An AI innkeeper lets a room: takes on someone's <see cref="ErrandKind.Lodging"/> while a bed
/// upstairs is free and goes to them. In reach, <see cref="ErrandSystem"/> gives them the bed and
/// they pay, as it does for a player innkeeper who walks up to them.
/// </summary>
public sealed class LodgeJob(int errand) : Job
{
    /// <summary>A room this villager could let now: their trade's, known to them, theirs or nobody's, with a bed free.</summary>
    public static Job? For(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        if (!w.TryGet<Trade>(pawn, out var trade) || trade.Def.WorkplaceDef is not { } workplace)
            return null;
        var from = w.Get<Position>(pawn).Cell;
        foreach (var e in sim.Errands.All)
        {
            if (!e.IsOpen || e.Kind.Kind != ErrandKind.Lodging || e.Kind.AtDef != workplace || !e.KnownBy.Contains(pawn)
                || (!e.Assignee.IsNull && e.Assignee != pawn) || !w.IsAlive(e.Poster))
                continue;
            if (!ErrandSystem.FreeLetBed(sim, e.Where).IsNull && Navigation.CanReach(sim, pawn, from, w.Get<Position>(e.Poster).Cell, PathEnd.Touch))
                return new LodgeJob(e.Id);
        }
        return null;
    }

    protected override IEnumerable<JobStatus> Run()
    {
        var errands = Sim.Errands;
        if (errands.Find(errand) is not { IsOpen: true } e || (e.Assignee != Pawn && !errands.Accept(Sim, Pawn, errand)))
        {
            Fail("someone else has it");
            yield break;
        }
        Report = $"Letting a room to {e.PosterName}";
        foreach (var s in GoToEntity(e.Poster, PathEnd.Touch, 1500)) yield return s;
        if (HasFailed) yield break;
        // In reach: the errand system shows them up on its next pass. Wait for it a moment.
        for (var t = 0; t < 4 && e.IsOpen; t++)
            foreach (var s in Wait(SimClock.TicksPerHour / 12)) yield return s;
        if (e.IsOpen)
            Fail("couldn't let the room");
    }
}

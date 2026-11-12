using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Maps;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Shops;

/// <summary>
/// An AI keeper sells a piece on show in their shop to whoever ordered it: takes it down, carries
/// it over and hands it to them for the price. (A villager who took over a player's trade sells
/// what the player left on show.)
/// </summary>
public sealed class SellJob(int order) : Job
{
    /// <summary>An order for goods this villager could see to now: on show in their shop, known to them, theirs or nobody's.</summary>
    public static Job? For(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        if (!w.TryGet<Trade>(pawn, out var trade) || trade.Def.WorkplaceDef is not { } workplace)
            return null;
        var from = w.Get<Position>(pawn).Cell;
        foreach (var e in sim.Shops.FrontFirst(sim))
        {
            if (e.Kind.Kind != ServiceKind.Goods || !e.KnownBy.Contains(pawn) || (!e.Assignee.IsNull && e.Assignee != pawn)
                || !w.IsAlive(e.Poster) || Goods.ShownIn(sim, e.Subject) is not { IsNull: false } shop
                || w.Get<PresetBuilding>(shop).Def != workplace || sim.Reservations.IsReservedByOther(e.Subject, pawn))
                continue;
            if (Navigation.CanReach(sim, pawn, from, w.Get<Thing>(e.Subject).Cell, PathEnd.OnTile)
                && Navigation.CanReach(sim, pawn, from, w.Get<Position>(e.Poster).Cell, PathEnd.Touch))
                return new SellJob(e.Id);
        }
        return null;
    }

    protected override IEnumerable<JobStatus> Run()
    {
        var shops = Sim.Shops;
        if (shops.Find(order) is not { IsOpen: true } e || (e.Assignee != Pawn && !shops.Accept(Sim, Pawn, order)))
        {
            Fail("someone else has it");
            yield break;
        }
        var item = e.Subject;
        if (!Reserve(item))
        {
            Fail("someone else has it");
            yield break;
        }
        var label = Sim.LabelOf(item);
        Report = $"Taking down the {label} for {e.PosterName}";
        foreach (var s in GoTo(Get<Thing>(item).Cell, PathEnd.OnTile)) yield return s;
        if (HasFailed) yield break;
        if (Goods.ShownIn(Sim, item).IsNull) // (it's for sale, so not IsAvailable: only its seller's shop takes it down)
        {
            Fail("it's gone from the shelf");
            yield break;
        }
        Get<Carrying>(Pawn).Item = Inventory.PickUp(Sim, Pawn, item, Get<Carrying>(Pawn).Item);

        Report = $"Selling {e.PosterName} the {label}";
        foreach (var s in GoToEntity(e.Poster, PathEnd.Touch, 1500)) yield return s;
        if (HasFailed) yield break;
        if (!Alive(item) || !Inventory.OnPerson(Sim, Pawn, item) || !Handing.InReach(Sim, Pawn, e.Poster) || !e.IsOpen)
        {
            Fail("couldn't hand it over");
            yield break;
        }
        shops.Received(Sim, Pawn, e.Poster, Inventory.TakeFromPerson(Sim, Pawn, item, 1));
    }
}

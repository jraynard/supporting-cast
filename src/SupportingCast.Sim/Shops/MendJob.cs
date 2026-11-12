using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Combat;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Maps;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Shops;

/// <summary>
/// An AI smith sees to a repair order from start to finish: takes it on, collects the piece,
/// hammers out its chips at the forge, puts the edge back, and hands it over for the pay. A
/// basic hand at it, with no forge heat to manage: players do it better by hand
/// (<see cref="Work.HammerSession"/>, <see cref="Work.SharpenSession"/>). Picks the job up again
/// where it left off if interrupted, since the piece stays with the smith.
/// </summary>
public sealed class MendJob(int order) : Job
{
    /// <summary>Ticks of hammering and of grinding.</summary>
    public const int HammerTicks = 480, GrindTicks = 300;

    /// <summary>A repair order this villager could see to now: their trade's, known to them, theirs or nobody's.</summary>
    public static Job? For(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        if (!w.TryGet<Trade>(pawn, out var trade) || trade.Def.WorkplaceDef is not { } workplace)
            return null;
        var from = w.Get<Position>(pawn).Cell;
        foreach (var e in sim.Shops.FrontFirst(sim))
        {
            if (!e.IsOpen || e.Kind.Kind != ServiceKind.Repair || e.Kind.AtDef != workplace || !e.KnownBy.Contains(pawn)
                || (!e.Assignee.IsNull && e.Assignee != pawn) || !w.IsAlive(e.Subject) || !w.IsAlive(e.Poster))
                continue;
            var chipped = WeaponWear.HpFraction(sim, e.Subject) < WeaponWear.MendedHp;
            if (chipped && (Station(sim, pawn, e.Subject).IsNull || !CanPay(sim, pawn, e.Subject)))
                continue;
            if (Navigation.CanReach(sim, pawn, from, w.Get<Position>(e.Poster).Cell, PathEnd.Touch))
                return new MendJob(e.Id);
        }
        return null;
    }

    /// <summary>The nearest station that mends this kind of piece.</summary>
    private static Entity Station(Simulation sim, Entity pawn, Entity item)
    {
        var w = sim.World;
        var from = w.Get<Position>(pawn).Cell;
        var def = w.Get<Thing>(item).Def;
        var best = Entity.Null;
        var bestDist = int.MaxValue;
        foreach (var s in w.Each<Workbench>())
        {
            if (!Crafting.IsStation(sim, s) || Crafting.RepairRecipe(w.Get<Thing>(s).Def, def) == null)
                continue;
            var d = Map.Distance(from, w.Get<Thing>(s).Cell);
            if (d < bestDist)
            {
                best = s;
                bestDist = d;
            }
        }
        return best;
    }

    /// <summary>Has the material to hand, or knows where some is lying.</summary>
    private static bool CanPay(Simulation sim, Entity pawn, Entity item)
    {
        var station = Station(sim, pawn, item);
        if (station.IsNull || Crafting.RepairCost(sim, sim.World.Get<Thing>(station).Def, item) is not { } cost)
            return true;
        return Have(sim, pawn, cost.Material) >= cost.Count || !Lying(sim, pawn, cost.Material).IsNull;
    }

    private static int Have(Simulation sim, Entity pawn, ThingCount material) =>
        Core.CraftCommandRules.Sources(sim, pawn).Where(e => sim.World.IsAlive(e)
            && Crafting.Accepts(material, null, sim.World.Get<Thing>(e).Def, sim.World.Get<Thing>(e).Material))
            .Sum(e => sim.World.Get<Stack>(e).Count);

    /// <summary>The nearest free stack of the material on the ground or in a stockpile.</summary>
    private static Entity Lying(Simulation sim, Entity pawn, ThingCount material) =>
        AiQueries.FindNearestItem(sim, pawn, (e, def) => Crafting.Accepts(material, null, def, sim.World.Get<Thing>(e).Material));

    protected override IEnumerable<JobStatus> Run()
    {
        var orders = Sim.Shops;
        if (orders.Find(order) is not { IsOpen: true } e || (e.Assignee != Pawn && !orders.Accept(Sim, Pawn, order)))
        {
            Fail("someone else has it");
            yield break;
        }
        var item = e.Subject;
        var label = Sim.LabelOf(item);
        Report = $"Fetching {e.PosterName}'s {label}";
        for (var tries = 0; tries < 3 && !Inventory.OnPerson(Sim, Pawn, item); tries++)
        {
            foreach (var s in GoToEntity(e.Poster, PathEnd.Touch, 1500)) yield return s;
            if (HasFailed) yield break;
            if (!orders.TryHandOver(Sim, e))
                foreach (var s in Wait(30)) yield return s;
        }
        if (!Inventory.OnPerson(Sim, Pawn, item))
        {
            Fail("couldn't get the piece");
            yield break;
        }

        if (WeaponWear.HpFraction(Sim, item) < WeaponWear.MendedHp && Station(Sim, Pawn, item) is { IsNull: false } station)
        {
            // Fetch the iron for it first, if it isn't to hand.
            if (Crafting.RepairCost(Sim, Get<Thing>(station).Def, item) is { } need && Have(Sim, Pawn, need.Material) < need.Count
                && Lying(Sim, Pawn, need.Material) is { IsNull: false } stock && Reserve(stock))
            {
                Report = $"Fetching {need.Material.ThingDef.Label}";
                foreach (var s in GoTo(Get<Thing>(stock).Cell, PathEnd.OnTile)) yield return s;
                if (HasFailed) yield break;
                if (Sim.IsAvailable(stock))
                    Get<Carrying>(Pawn).Item = Inventory.PickUp(Sim, Pawn, stock, Get<Carrying>(Pawn).Item);
            }
            Report = $"Hammering out the {label}";
            foreach (var s in GoTo(Get<Thing>(station).Cell, PathEnd.Touch)) yield return s;
            if (HasFailed) yield break;
            for (var i = 0; i < HammerTicks; i++)
            {
                if (!Alive(item) || !Inventory.OnPerson(Sim, Pawn, item) || !Crafting.IsStation(Sim, station))
                {
                    Fail("the mending was called off");
                    yield break;
                }
                SkillRules.Learn(Sim, Pawn, Skill.Crafting, SkillRules.XpPerWorkTick);
                yield return JobStatus.Running;
            }
            if (Crafting.RepairCost(Sim, Get<Thing>(station).Def, item) is { } cost && Crafting.PayForRepair(Sim, Pawn, cost.Material, cost.Count))
            {
                ref var t = ref Get<Thing>(item);
                t.Hp = t.MaxHp;
                if (WeaponWear.Edged(t.Def))
                    WeaponWear.SetDullness(Sim, item, Math.Max(WeaponWear.Dullness(Sim, item), WeaponWear.DullAfterHammering));
            }
        }
        if (WeaponWear.Edged(Get<Thing>(item).Def) && WeaponWear.Dullness(Sim, item) > WeaponWear.MendedDullness)
        {
            Report = $"Sharpening the {label}";
            for (var i = 0; i < GrindTicks; i++)
            {
                if (!Alive(item) || !Inventory.OnPerson(Sim, Pawn, item))
                {
                    Fail("the sharpening was called off");
                    yield break;
                }
                yield return JobStatus.Running;
            }
            WeaponWear.SetDullness(Sim, item, 0f);
        }

        Report = $"Bringing back {e.PosterName}'s {label}";
        foreach (var s in GoToEntity(e.Poster, PathEnd.Touch, 1500)) yield return s;
        if (HasFailed) yield break;
        if (!Handing.InReach(Sim, Pawn, e.Poster) || !Inventory.OnPerson(Sim, Pawn, item))
        {
            Fail("couldn't hand it back");
            yield break;
        }
        orders.Received(Sim, Pawn, e.Poster, Inventory.TakeFromPerson(Sim, Pawn, item, 1));
    }
}

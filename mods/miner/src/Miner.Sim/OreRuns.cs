using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Miner;

/// <summary>
/// An AI miner's ore to the smithy (a player's character once they've left, still at the trade): it doesn't play the
/// barrow, so it carries an armful at a time, as much as a barrow would take, from the shed's chest or from where it lies
/// hewn by the mine, and is paid for it at the smithy as a barrow's load is (<see cref="BarrowSystem.Deliver"/>).
/// </summary>
public static class OreRuns
{
    /// <summary>How far from the mine shed hewn ore is looked for: the mine and its mouth.</summary>
    public const int Reach = 40;

    /// <summary>The smithy's chest, Entity.Null if the town has none.</summary>
    public static Entity SmithyChest(Simulation sim) =>
        ShopSystem.BuildingOf(sim, sim.Defs.Building(OreCarterSystem.SmithyDef)) is { IsNull: false } smithy ? Stock.ChestOf(sim, smithy) : Entity.Null;

    /// <summary>A run for an AI miner now, or null: ore in the shed's chest first, then the nearest lying about the mine.</summary>
    public static Job? Find(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        if (!sim.Round.Live || w.Has<Avatar>(pawn) || !MinerModule.IsMiner(sim, pawn))
            return null;
        var smithy = SmithyChest(sim);
        var shed = ShopSystem.WorkplaceOf(sim, pawn);
        if (smithy.IsNull || shed.IsNull)
            return null;
        var chest = Stock.ChestOf(sim, shed);
        if (!chest.IsNull && !sim.Reservations.IsReservedByOther(chest, pawn)
            && sim.StoredIn(chest).FirstOrDefault(e => Barrows.Loadable(sim, w.Get<Thing>(e).Def), Entity.Null) is { IsNull: false })
            return new OreRunJob(chest, smithy);
        var from = w.Get<PresetBuilding>(shed).Anchor;
        var lying = w.Query<Thing>()
            .Where(e => w.Get<Thing>(e) is { Spawned: true } t && Barrows.Loadable(sim, t.Def) && !w.Has<Stored>(e)
                && HeroesNotIncluded.Sim.Maps.Map.Distance(t.Cell, from) <= Reach && sim.IsAvailable(e) && !sim.Reservations.IsReservedByOther(e, pawn))
            .OrderBy(e => HeroesNotIncluded.Sim.Maps.Map.Distance(w.Get<Thing>(e).Cell, from)).ThenBy(e => e.Index)
            .DefaultIfEmpty(Entity.Null).First();
        return lying.IsNull ? null : new OreRunJob(lying, smithy);
    }

    /// <summary>How many of a thing make a barrowload.</summary>
    public static int Armful(HeroesNotIncluded.Sim.Defs.ThingDef def) => Math.Max(1, (int)(Barrows.MaxMass / Math.Max(0.1f, def.Mass)));
}

/// <summary>
/// Carry a barrowload of ore to the smithy's chest and be paid for it: from <paramref name="source"/>, the shed's chest or a
/// stack lying hewn. What it carries is in its hands, so a job cut short puts it down where it stands.
/// </summary>
public sealed class OreRunJob(Entity source, Entity smithy) : Job
{
    public override Entity Focus => smithy;

    protected override IEnumerable<JobStatus> Run()
    {
        Report = "Taking ore to the smithy";
        if (!Reserve(source)) yield break;
        foreach (var s in GoTo(Get<Thing>(source).Cell, PathEnd.Touch)) yield return s;
        if (HasFailed) yield break;
        if (!Alive(source) || !Get<Carrying>(Pawn).Item.IsNull)
        {
            Fail("can't take the ore");
            yield break;
        }
        Entity carried;
        if (Sim.World.Get<Thing>(source).Def.Building != null)
        {
            // The shed's chest: an armful of the first ore in it.
            var stack = Sim.StoredIn(source).FirstOrDefault(e => Barrows.Loadable(Sim, Get<Thing>(e).Def), Entity.Null);
            if (stack.IsNull)
            {
                Fail("the chest's empty");
                yield break;
            }
            var def = Get<Thing>(stack).Def;
            var count = Math.Min(Get<Stack>(stack).Count, OreRuns.Armful(def));
            if (!Stock.TakeFrom(Sim, source, def.Name, count))
            {
                Fail("the chest's empty");
                yield break;
            }
            carried = Sim.CreateItem(def, count);
        }
        else
        {
            if (!Sim.IsAvailable(source))
            {
                Fail("the ore's gone");
                yield break;
            }
            carried = Sim.TakeFromStack(source, Math.Min(Get<Stack>(source).Count, OreRuns.Armful(Get<Thing>(source).Def)));
        }
        Get<Carrying>(Pawn).Item = carried;
        foreach (var s in GoTo(Get<Thing>(smithy).Cell, PathEnd.Touch)) yield return s;
        if (HasFailed) yield break;
        if (!Alive(smithy) || Get<Carrying>(Pawn).Item != carried || !Alive(carried))
        {
            Fail("lost the ore");
            yield break;
        }
        var pieces = Enumerable.Repeat(Get<Thing>(carried).Def, Get<Stack>(carried).Count).ToList();
        Get<Carrying>(Pawn).Item = Entity.Null;
        Sim.DestroyThing(carried);
        BarrowSystem.Deliver(Sim, Pawn, smithy, pieces, paid: true);
    }
}

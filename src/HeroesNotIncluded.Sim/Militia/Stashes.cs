using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Militia;

/// <summary>
/// What a trade keeps hidden at its workplace (<see cref="TradeDef.Stash"/>) in a stash
/// (<see cref="BuildingProps.Stash"/>): the innkeeper's cudgel under the bar. It's put there at the
/// deal, no job takes it, and only the trade's holder reaches in: an AI holder fetches it when
/// trouble comes (the muster, or raiders near) and puts it back once the town stands down; a player
/// takes it by hand.
/// </summary>
public static class Stashes
{
    public static bool IsStash(Simulation sim, Entity e) =>
        !e.IsNull && sim.World.IsAlive(e) && sim.World.TryGet<Thing>(e, out var t) && t.Def.Building is { Stash: true } && !sim.World.Has<Construction>(e);

    /// <summary>The stash at a trade's workplace (the first, in entity order), or Entity.Null.</summary>
    public static Entity Of(Simulation sim, TradeDef trade)
    {
        var w = sim.World;
        if (trade.Stash.Count == 0 || trade.WorkplaceDef is not { } workplace)
            return Entity.Null;
        foreach (var e in w.Query<Thing>().OrderBy(e => e.Index))
            if (IsStash(sim, e) && Buildings.Of(sim, e) is { IsNull: false } b && w.Get<PresetBuilding>(b).Def == workplace)
                return e;
        return Entity.Null;
    }

    /// <summary>Is this the stash of the trade <paramref name="who"/> holds (theirs to reach into)?</summary>
    public static bool Reaches(Simulation sim, Entity who, Entity stash) =>
        sim.World.TryGet<Trade>(who, out var trade) && trade.Def.Stash.Count > 0 && Of(sim, trade.Def) == stash;

    /// <summary>Does this kind of thing belong in some trade's stash? (A stash holds nothing else.)</summary>
    public static bool Stashed(Simulation sim, ThingDef def)
    {
        foreach (var trade in sim.Defs.AllTrades)
            foreach (var item in trade.Stash)
                if (item.ThingDef == def)
                    return true;
        return false;
    }

    /// <summary>At the deal: whatever the trade keeps at its workplace is put in its stash, unless it's there already.</summary>
    public static void Fill(Simulation sim, TradeDef trade)
    {
        var stash = Of(sim, trade);
        if (stash.IsNull)
            return;
        var held = sim.StoredIn(stash);
        foreach (var item in trade.Stash)
        {
            if (held.Any(e => sim.World.Get<Thing>(e).Def == item.ThingDef))
                continue;
            var made = sim.CreateItem(item.ThingDef, item.Count, Simulation.MaterialFor(item.ThingDef, null));
            if (sim.Store(stash, made) is { IsNull: false } left)
                sim.DestroyThing(left);
        }
    }

    /// <summary>Trouble's here (the muster, or raiders near): an unarmed holder fetches the weapon from their stash. Null otherwise.</summary>
    public static Ai.Job? ArmFrom(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        if (!w.TryGet<Trade>(pawn, out var trade) || trade.Def.Stash.Count == 0 || w.Has<NonViolent>(pawn) || !w.Get<Creature>(pawn).Able
            || (w.TryGet<Equipment>(pawn, out var eq) && w.IsAlive(eq.Weapon)) || (w.TryGet<Outfit>(pawn, out var outfit) && outfit.Unarmed))
            return null;
        if (!sim.Militia.Mustering && Ai.Defence.Threat(sim, pawn, Ai.Defence.VillagerRange) == null)
            return null;
        var stash = Of(sim, trade.Def);
        if (stash.IsNull)
            return null;
        var weapon = sim.StoredIn(stash).FirstOrDefault(e => w.Get<Thing>(e).Def.Weapon != null);
        if (weapon.IsNull || sim.Reservations.IsReservedByOther(weapon, pawn)
            || !Ai.Navigation.CanReach(sim, pawn, w.Get<Position>(pawn).Cell, w.Get<Thing>(stash).Cell, PathEnd.Touch))
            return null;
        return new FetchStashedJob(stash, weapon);
    }

    /// <summary>
    /// The town's stood down and nothing's near: what came out of the stash goes back in (from hand,
    /// or the pack it was put away in for the rack's weapon). Null otherwise.
    /// </summary>
    public static Ai.Job? PutBack(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        if (sim.Militia.Open || !w.TryGet<Trade>(pawn, out var trade) || trade.Def.Stash.Count == 0
            || Ai.Defence.Threat(sim, pawn, Ai.Defence.VillagerRange) != null || StashedOnPerson(sim, pawn, trade.Def).IsNull)
            return null;
        var stash = Of(sim, trade.Def);
        if (stash.IsNull || !Ai.Navigation.CanReach(sim, pawn, w.Get<Position>(pawn).Cell, w.Get<Thing>(stash).Cell, PathEnd.Touch))
            return null;
        return new ReturnStashedJob(stash);
    }

    /// <summary>Something of the trade's stash they have on them (wielded first, then in the pack), or Entity.Null.</summary>
    internal static Entity StashedOnPerson(Simulation sim, Entity pawn, TradeDef trade)
    {
        var w = sim.World;
        bool Belongs(Entity e) => trade.Stash.Any(s => s.ThingDef == w.Get<Thing>(e).Def);
        if (w.TryGet<Equipment>(pawn, out var eq) && w.IsAlive(eq.Weapon) && Belongs(eq.Weapon))
            return eq.Weapon;
        if (sim.PackOf(pawn) is { IsNull: false } pack)
            foreach (var e in sim.StoredIn(pack))
                if (Belongs(e))
                    return e;
        return Entity.Null;
    }
}

/// <summary>Fetch the weapon kept in the stash at work (Lucky Lou, from under the bar) and take it up.</summary>
public sealed class FetchStashedJob(Entity stash, Entity weapon) : Ai.Job
{
    public override Entity Focus => stash;

    protected override IEnumerable<Ai.JobStatus> Run()
    {
        Report = $"Reaching for the {Get<Thing>(weapon).Label}";
        if (!Reserve(weapon)) yield break;
        foreach (var s in GoToBeside(stash)) yield return s;
        if (HasFailed) yield break;
        foreach (var s in Wait(30)) yield return s;
        if (!Alive(stash) || !Alive(weapon) || !Sim.World.TryGet<Stored>(weapon, out var stored) || stored.In != stash)
        {
            Fail("it's not there");
            yield break;
        }
        Inventory.PutOn(Sim, Pawn, Sim.TakeFromStack(weapon, 1));
    }
}

/// <summary>Put what came out of the stash back in it, and take up their own weapon again if there's one put away.</summary>
public sealed class ReturnStashedJob(Entity stash) : Ai.Job
{
    public override Entity Focus => stash;

    protected override IEnumerable<Ai.JobStatus> Run()
    {
        Report = "Putting it back where it lives";
        foreach (var s in GoToBeside(stash)) yield return s;
        if (HasFailed) yield break;
        foreach (var s in Wait(30)) yield return s;
        if (!Alive(stash))
        {
            Fail("it's gone");
            yield break;
        }
        var w = Sim.World;
        if (!w.TryGet<Trade>(Pawn, out var trade) || Stashes.StashedOnPerson(Sim, Pawn, trade.Def) is not { IsNull: false } item)
            yield break;
        var t = w.Get<Thing>(item);
        if (!Sim.HasRoomFor(stash, t.Def, t.Material))
        {
            Fail("no room for it");
            yield break;
        }
        if (w.TryGet<Equipment>(Pawn, out var eq) && eq.Weapon == item)
            w.Remove<Equipment>(Pawn);
        else
            item = Sim.TakeFromStack(item, w.Get<Stack>(item).Count);
        Sim.Store(stash, item);
        Muster.Rearm(Sim, Pawn);
    }
}

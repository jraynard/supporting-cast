using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Expeditions;
using HeroesNotIncluded.Sim.Healing;
using HeroesNotIncluded.Sim.Tinkering;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Bots;

public sealed partial class BotPlayer
{
    /// <summary>Feel a strongbox over for traps, then pick it, then hand it back open.</summary>
    private bool OpenBox(Simulation sim, Entity me, PerceptionView view, Entity owner, long now)
    {
        var box = Entrusted(sim, me, owner);
        if (box.IsNull)
        {
            Reach(sim, me, owner, now);
            return true;
        }
        if (view.Strongbox(box) is not { } known)
            return false;
        Doing = known.Locked ? (known.SearchedByMe ? "picking a lock" : "feeling for traps") : "handing back a strongbox";
        if (known.Locked)
        {
            Send(known.SearchedByMe ? new PickLockCommand(me, box) : new ProbeBoxCommand(me, box));
            return true;
        }
        if (Reach(sim, me, owner, now, returning: true))
            Send(new GiveCommand(me, owner, box, 1));
        return true;
    }

    /// <summary>
    /// Out to the customer's wagon (whose is whose is plain to see), and mend what's wrong with it:
    /// true the wheel, then a new axle (wood from the shop's chest if it hasn't the wood on it).
    /// Paid once nothing's wrong with it.
    /// </summary>
    private bool MendWagon(Simulation sim, Entity me, PerceptionView view, Entity owner, long now)
    {
        var w = sim.World;
        var wagon = Entity.Null;
        foreach (var e in w.Each<Wagon>())
            if (w.Get<Wagon>(e).Owner == owner)
                wagon = e;
        if (wagon.IsNull)
            return false;
        var cell = w.Get<Thing>(wagon).Cell;
        var seen = view.Wagon(wagon);
        if (!AvatarSystem.InReach(sim, w.Get<Position>(me).Cell, wagon) || seen is not { } known)
        {
            // An axle wants wood: from the shop on the way, if it's none on it.
            if (seen is { } s && (s.Damage & WagonDamage.Axle) != 0 && Wagons.WoodOn(sim, me) < Wagons.AxleWood && FetchWood(sim, me, view, now))
                return true;
            Doing = "going out to a wagon";
            Walk(sim, me, cell, now);
            return true;
        }
        if (known.Damage == WagonDamage.None)
            return false;
        Doing = "mending a wagon";
        if ((known.Damage & WagonDamage.Wheel) != 0)
            Send(new TrueWheelCommand(me, wagon));
        else if (Wagons.WoodOn(sim, me) >= Wagons.AxleWood)
            Send(new FitAxleCommand(me, wagon));
        else
            return FetchWood(sim, me, view, now);
        return true;
    }

    /// <summary>Wood from a chest at its workplace, for an axle. False if there's none to be had.</summary>
    private bool FetchWood(Simulation sim, Entity me, PerceptionView view, long now)
    {
        var w = sim.World;
        if (!w.TryGet<Trade>(me, out var trade) || !trade.Def.HasWorkplace)
            return false;
        foreach (var e in w.Each<PartOf>())
        {
            var b = w.Get<PartOf>(e).Building;
            if (!w.TryGet<PresetBuilding>(b, out var pb) || !trade.Def.WorksAt(pb.Def) || w.Get<Thing>(e).Def.Name != "Chest")
                continue;
            var chest = e;
            if (view.InContainer(chest).FirstOrDefault(i => w.Get<Thing>(i).Def.Name == "Wood", Entity.Null) is not { IsNull: false } wood)
                continue;
            Doing = "fetching wood for an axle";
            return AtStation(sim, me, chest, now, _ => Send(new TakeFromStorageCommand(me, wood, Wagons.AxleWood)));
        }
        return false;
    }

    /// <summary>Look a curio over under the loupe, then hand it back with an honest reckoning (its middle).</summary>
    private bool Appraise(Simulation sim, Entity me, PerceptionView view, Entity owner, long now)
    {
        var curio = Entrusted(sim, me, owner);
        if (curio.IsNull)
        {
            Reach(sim, me, owner, now);
            return true;
        }
        if (view.Curio(curio) is not { } known)
            return false;
        if (known.Low == null)
        {
            Doing = "looking a curio over";
            Send(new AppraiseCommand(me, curio));
            return true;
        }
        Doing = "handing back a curio";
        if (Reach(sim, me, owner, now, returning: true))
            Send(new GiveCommand(me, owner, curio, 1));
        return true;
    }

    /// <summary>The tinker's further games; null for a session that isn't one.</summary>
    private (bool Press, bool Held, int Axis)? PlayBench(WorkSession s) => null;

    /// <summary>The tinker between orders. True while at it.</summary>
    private bool Bench(Simulation sim, Entity me, PerceptionView view, long now) => false;
}

using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Combat;

/// <summary>
/// A raider making off with a player's silver (<see cref="Plunders"/>): who it was cut from and how much. The raider runs
/// for the map's edge with it, but isn't <see cref="Leaving"/>: the town may still chase it down, and if it falls the
/// silver falls with it.
/// </summary>
public struct Plunder
{
    public Entity From;
    public int Coins;
}

/// <summary>
/// Raiders rob the players they put down: a raider with nothing yet stolen goes to a player's character lying downed
/// near it, cuts half their purse and makes off with it (<see cref="RobJob"/>). Downed or killed before it's off the map,
/// it drops the lot where it falls. Only players' characters are robbed: the AI villagers carry little, and a player
/// downed should lose something to the raid, not only time.
/// </summary>
public static class Plunders
{
    /// <summary>How far a raider will go for a downed player's purse.</summary>
    public const int Reach = 10;

    /// <summary>The share of a downed player's silver a raider takes.</summary>
    public const float Share = 0.5f;

    /// <summary>What a raider would take from someone: half their silver, none if they've under two.</summary>
    public static int Take(Simulation sim, Entity from) => (int)(Coin.Count(sim, from) * Share);

    /// <summary>A downed player near this raider with silver worth cutting, and no other raider after it; or Entity.Null.</summary>
    public static Entity Mark(Simulation sim, Entity raider)
    {
        var w = sim.World;
        if (w.Has<Plunder>(raider))
            return Entity.Null;
        var here = w.Get<Position>(raider).Cell;
        var best = Entity.Null;
        var bestDistance = int.MaxValue;
        foreach (var e in w.Each<PlayerCharacter>())
        {
            if (!w.TryGet<Creature>(e, out var c) || !c.Downed || c.Dead || w.Has<Carried>(e) || Take(sim, e) < 1 || Robbing(sim, e, raider))
                continue;
            var at = w.Get<Position>(e).Cell;
            var d = Map.Distance(here, at);
            if (d <= Reach && d < bestDistance && sim.Regions.CanReach(here, at, PathEnd.Touch))
                (best, bestDistance) = (e, d);
        }
        return best;
    }

    /// <summary>Another raider already on its way to rob them. (Not a reservation: a villager kneeling to bind them doesn't stop it.)</summary>
    private static bool Robbing(Simulation sim, Entity victim, Entity except)
    {
        var w = sim.World;
        foreach (var e in w.Each<Mind>())
            if (e != except && w.Get<Mind>(e).Job is RobJob r && r.Victim == victim)
                return true;
        return false;
    }

    /// <summary>
    /// Cut the purse: half the silver, kept in the raider's own purse (the <see cref="Plunder"/>'s count, not an item in hand,
    /// so ending a job never sets it down).
    /// </summary>
    public static int Rob(Simulation sim, Entity raider, Entity from)
    {
        var w = sim.World;
        var amount = Take(sim, from);
        if (amount < 1 || w.Has<Plunder>(raider))
            return 0;
        var taken = 0;
        foreach (var stack in Coin.Stacks(sim, from))
        {
            if (taken >= amount)
                break;
            var take = Math.Min(amount - taken, w.Get<Stack>(stack).Count);
            sim.DestroyThing(Inventory.TakeFromPerson(sim, from, stack, take));
            taken += take;
        }
        if (taken < 1)
            return 0;
        w.Add(raider, new Plunder { From = from, Coins = taken });
        sim.Messages.Add(MessageKind.Danger, $"{w.Get<Creature>(raider).Name} cuts {w.Get<Creature>(from).Name}'s purse and runs for it with {taken} silver!",
            w.Get<Position>(raider).Cell);
        return taken;
    }

    /// <summary>A robber down or dead before it's away: the silver falls where it does.</summary>
    public static void Drop(Simulation sim, Entity raider)
    {
        var w = sim.World;
        if (!w.TryGet<Plunder>(raider, out var p))
            return;
        w.Remove<Plunder>(raider);
        var cell = w.Get<Position>(raider).Cell;
        var dropped = p.Coins;
        if (dropped > 0)
            sim.PlaceItemNear(sim.CreateItem(sim.Defs.Thing(Coin.Silver), dropped), cell);
        if (dropped > 0)
            sim.Messages.Add(MessageKind.Good,
                $"Caught! {w.Get<Creature>(raider).Name} drops the {dropped} silver cut from {(w.TryGet<Creature>(p.From, out var c) ? c.Name : "someone")}.", cell);
    }

    /// <summary>A robber off the map: the silver's gone with it.</summary>
    public static void GotAway(Simulation sim, Entity raider)
    {
        var w = sim.World;
        if (w.TryGet<Plunder>(raider, out var p))
            sim.Messages.Add(MessageKind.Warning,
                $"{w.Get<Creature>(raider).Name} got away with {p.Coins} of {(w.TryGet<Creature>(p.From, out var c) ? c.Name : "someone")}'s silver.",
                w.Get<Position>(raider).Cell);
    }
}

/// <summary>A raider goes to a downed player, cuts their purse (<see cref="Plunders.Rob"/>) and runs for the edge with it.</summary>
public sealed class RobJob(Entity victim) : Job
{
    public Entity Victim => victim;

    protected override IEnumerable<JobStatus> Run()
    {
        Report = "Robbing";
        foreach (var s in GoToEntity(victim, PathEnd.Touch, SimClock.TicksPerSecond * 20)) yield return s;
        if (HasFailed) yield break;
        if (!Alive(victim) || Get<Creature>(victim) is not { Downed: true, Dead: false })
        {
            Fail("they're up, or gone");
            yield break;
        }
        foreach (var s in Wait(SimClock.TicksPerSecond)) yield return s; // a knife to the purse strings
        if (!Alive(victim) || Get<Creature>(victim) is not { Downed: true, Dead: false })
        {
            Fail("they're up, or gone");
            yield break;
        }
        Plunders.Rob(Sim, Pawn, victim);
    }
}

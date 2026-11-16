using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Bots;

public sealed partial class BotPlayer
{
    // The rat it's after, since when, and when it'll try again having given one up; and whether it's on the move for one.
    private Entity _rat = Entity.Null;
    private long _ratSince, _noRatsUntil;
    private bool _ratting;

    /// <summary>How near a rat it swings, in cells.</summary>
    private const float RatReach = 1.2f;

    /// <summary>
    /// A rat it can see about its kitchen (a shortage brings them): after it with whatever's in its
    /// hand, by free movement and blows, as a player goes after one. Its moves are made as it
    /// thinks, so a quick rat may lead it a dance; given up after a while, and tried again later.
    /// True while it's chasing.
    /// </summary>
    private bool ChaseRats(Simulation sim, Entity me, PerceptionView view, long now)
    {
        var w = sim.World;
        if (now < _noRatsUntil || MyShop(sim, me) is not { IsNull: false } shop)
            return StopChasing(me);
        if (_rat.IsNull || !w.IsAlive(_rat) || w.Get<Creature>(_rat).Dead || !view.CanSee(_rat))
        {
            var here = w.Get<Position>(me).Cell;
            _rat = view.Creatures().Where(e => w.Get<Creature>(e) is { Dead: false } c && c.Def.Name == Hospitality.RatSystem.Def
                    && Buildings.At(sim, w.Get<Position>(e).Cell) == shop && w.Get<Position>(e).Cell.Z == here.Z)
                .OrderBy(e => Maps.Map.Distance(w.Get<Position>(e).Cell, here)).ThenBy(e => e.Index).FirstOrDefault(Entity.Null);
            _ratSince = now;
            if (_rat.IsNull)
                return StopChasing(me);
        }
        if (now - _ratSince > RatJob.ChaseTicks)
        {
            _noRatsUntil = now + SimClock.TicksPerHour / 2; // it's led a dance: leave it a while
            _rat = Entity.Null;
            return StopChasing(me);
        }
        Doing = "chasing a rat";
        if (!w.TryGet<Combat.FreeMover>(me, out var free) || !free.Drawn)
            Send(new DrawWeaponCommand(me, true));
        var from = w.Get<Position>(me).Pos;
        var to = w.Get<Position>(_rat).Pos;
        var d = new float2(to.X - from.X, to.Y - from.Y);
        var dist = MathF.Sqrt(d.X * d.X + d.Y * d.Y);
        var aim = DetMath.Atan2(d.Y, d.X);
        var dir = dist > 1e-3f ? d * (1f / dist) : new float2(1, 0);
        // Closing in, slowing as it comes near so it doesn't run past.
        var wish = dir * Math.Clamp((dist - RatReach * 0.7f) * 0.6f, -1f, 1f);
        Send(new FreeMoveCommand(me, wish.X, wish.Y, aim));
        _ratting = true;
        if (dist <= RatReach && !w.Has<Combat.Windup>(me))
            Send(new AttackCommand(me, false, aim));
        return true;
    }

    /// <summary>Done chasing: feet still, weapon away. False, so it gets on with its day.</summary>
    private bool StopChasing(Entity me)
    {
        if (!_ratting)
            return false;
        _ratting = false;
        Send(new FreeMoveCommand(me, 0f, 0f, 0f));
        Send(new DrawWeaponCommand(me, false));
        return false;
    }
}

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
    // ---------------- Fighting (free movement) ----------------

    private Entity _foe = Entity.Null;
    private float2 _sentWish;
    private bool _guarding;

    /// <summary>A bot's foe this close is a fight it takes on, armed and on its feet.</summary>
    private const int FightRange = 8;

    /// <summary>
    /// With free movement (<see cref="Combat.FreeMoveSystem"/>), an armed bot that isn't hurt stands
    /// and fights a raider or predator it can see near, as a player would: weapon drawn, to its
    /// reach, striking the way it aims; seeing a blow coming at it, it steps out of the sweep (or
    /// guards, if it's too winded to dash). Hurt, it gets away (<see cref="Safety"/>). Only what it
    /// can see, only through commands. True while it's fighting.
    /// </summary>
    private bool Fight(Simulation sim, Entity me, PerceptionView view, long now)
    {
        var w = sim.World;
        // The nervy don't stand and fight: they get out of the way (Safety).
        if (!w.TryGet<Combat.FreeMover>(me, out var free) || !w.Get<Creature>(me).Able || Ai.RaiderBrain.WantsToFlee(sim, me) || Temper.Nerve < 0.25f
            || !w.TryGet<Equipment>(me, out var eq) || !w.IsAlive(eq.Weapon) || w.Get<Thing>(eq.Weapon).Def.Weapon is not { } weapon)
            return StopFighting(me, free.Drawn);
        var here = w.Get<Position>(me).Pos;
        if (_foe.IsNull || !w.IsAlive(_foe) || !view.CanSee(_foe) || !w.Get<Creature>(_foe).Able)
        {
            _foe = Entity.Null;
            var best = float.MaxValue;
            foreach (var e in view.Creatures())
                if (w.Get<Creature>(e) is { Dead: false, Downed: false } c && Factions.Attacker(c.Faction) && !w.Has<Combat.Leaving>(e)
                    && c.Def.Race != Race.Dragon && w.Get<Position>(e).Cell.Z == w.Get<Position>(me).Cell.Z)
                {
                    var p = w.Get<Position>(e).Pos;
                    var d = (p.X - here.X) * (p.X - here.X) + (p.Y - here.Y) * (p.Y - here.Y);
                    if (d < FightRange * FightRange && d < best)
                        (_foe, best) = (e, d);
                }
            if (_foe.IsNull)
                return StopFighting(me, free.Drawn);
        }
        Doing = $"fighting {w.Get<Creature>(_foe).Name}";
        if (!free.Drawn)
            Send(new DrawWeaponCommand(me, true));
        var them = w.Get<Position>(_foe).Pos;
        var to = new float2(them.X - here.X, them.Y - here.Y);
        var dist = MathF.Sqrt(to.X * to.X + to.Y * to.Y);
        var aim = DetMath.Atan2(to.Y, to.X);
        var dir = dist > 1e-3f ? to * (1f / dist) : new float2(1, 0);
        var reach = weapon.Ranged ? Math.Max(2f, weapon.OptimalRange) : Combat.Reach.ShapeOf(sim, me, weapon).Length * 0.75f;

        // A blow coming at it: out of the sweep, to the side; winded, guard instead.
        if (w.TryGet<Combat.Windup>(_foe, out var blow) && blow.Impact - now <= 14 && Combat.Reach.Struck(sim, _foe,
                Combat.CombatRules.Attacks(sim, _foe)[Math.Min(blow.Attack, Combat.CombatRules.Attacks(sim, _foe).Count - 1)], blow.Aim).Contains(me))
        {
            var side = new float2(-DetMath.Sin(blow.Aim), DetMath.Cos(blow.Aim)) * (_rand.Chance(0.5f) ? 1f : -1f);
            if (w.TryGet<Combat.Guard>(me, out var g) && g.Stamina >= Combat.Blows.DodgeCost && blow.Impact - now <= 8)
                Send(new DodgeCommand(me, side.X, side.Y, now));
            else if (!_guarding)
            {
                Send(new GuardCommand(me, true, now));
                _guarding = true;
            }
            Move(me, side, aim);
            return true;
        }
        if (_guarding)
        {
            Send(new GuardCommand(me, false, now));
            _guarding = false;
        }
        var wish = dir * Math.Clamp((dist - reach) * 2f, -1f, 1f);
        Move(me, wish, aim);
        if (dist - Combat.Reach.BodyRadius <= (weapon.Ranged ? weapon.Range : Combat.Reach.ShapeOf(sim, me, weapon).Length) && !w.Has<Combat.Windup>(me))
            Send(new AttackCommand(me, _rand.Chance(0.25f) && !weapon.Ranged, aim));
        return true;
    }

    private void Move(Entity me, float2 wish, float aim)
    {
        var len = MathF.Sqrt(wish.X * wish.X + wish.Y * wish.Y);
        var w = len < 0.1f ? float2.Zero : wish;
        Send(new FreeMoveCommand(me, w.X, w.Y, aim));
        _sentWish = w;
    }

    /// <summary>The fight's done (or it won't take it on): feet still, weapon put away. False, so it gets on with its day.</summary>
    private bool StopFighting(Entity me, bool drawn)
    {
        if (_foe.IsNull && _sentWish == float2.Zero && !_guarding)
            return false;
        _foe = Entity.Null;
        if (_guarding)
            Send(new GuardCommand(me, false, 0));
        _guarding = false;
        if (_sentWish != float2.Zero)
            Send(new FreeMoveCommand(me, 0f, 0f, 0f));
        _sentWish = float2.Zero;
        if (drawn)
            Send(new DrawWeaponCommand(me, false));
        return false;
    }

    /// <summary>
    /// Bleeding, with a bandage on it: stanch it, as anyone would, rather than bleed out waiting on
    /// the healer. (The healer's own bleeding is <see cref="Mend"/>'s, among the mendings.)
    /// </summary>
    private bool BindOwn(Simulation sim, Entity me)
    {
        if (!Bandaging.FirstAidOnly(sim, me) || Bandaging.WoundsToBind(sim, me, me) == 0 || Bandaging.CantBind(sim, me, me) != null)
            return false;
        Send(new BandageCommand(me, me));
        return true;
    }

    /// <summary>Wounds adding up to this, a bot goes to the healer rather than carry them about.</summary>
    public const float SeeTheEmpathFrom = 6f;

    private long _healerRefusedAt = long.MinValue / 2;

    /// <summary>
    /// Hurt enough to be slowed by it (and not a healer itself): to the shrine, F at a pallet to
    /// ask, as a player would, then wait in its place in line till it's seen to. Only with the
    /// silver for it; turned away, it leaves it an hour.
    /// </summary>
    private bool SeeTheEmpath(Simulation sim, Entity me, long now)
    {
        var w = sim.World;
        if (_task != 0 || !sim.Round.Live || Healing.Empathy.Gifted(sim, me) || !w.TryGet<Health>(me, out var h)
            || h.Injuries.Sum(i => i.Damage) < SeeTheEmpathFrom || !Healing.LayOnHands.Hurt(sim, me))
            return false;
        if (sim.Shops.OpenOrderOf(me, ServiceKind.Healing) != null)
        {
            Doing = "waiting on the empath";
            if (sim.Shops.PlaceOf(sim, me) is { Cell: { } spot } && w.Get<Position>(me).Cell != spot)
                Walk(sim, me, spot, now);
            return true;
        }
        if (now - _healerRefusedAt < SimClock.TicksPerHour)
            return false;
        var healing = sim.Defs.Service(ServiceKind.Healing);
        var here = w.Get<Position>(me).Cell;
        var pallet = Entity.Null;
        var best = int.MaxValue;
        foreach (var b in w.Query<PresetBuilding>())
        {
            if (!w.Get<PresetBuilding>(b).Def.Has(healing.AtRole) || Shops.ShopPrices.Posted(sim, b, ServiceKind.Healing) is not { Offered: true } sign
                || sign.Price > Shops.Coin.Count(sim, me))
                continue;
            foreach (var bed in Shops.ShopSystem.BedsOf(sim, b))
                if (Maps.Map.Distance(here, w.Get<Thing>(bed).Cell) is var d && d < best)
                    (pallet, best) = (bed, d);
        }
        if (pallet.IsNull)
        {
            _healerRefusedAt = now;
            return false;
        }
        Doing = "going to the empath";
        return AtStation(sim, me, pallet, now, p =>
        {
            Send(new AvatarWorkCommand(me, true, w.Get<Thing>(p).Cell));
            Send(new AvatarWorkCommand(me, false));
            _healerRefusedAt = now; // if that didn't take, not again for a while
        });
    }

    /// <summary>
    /// Out of harm's way, as a careful player would be: away from a fire close by, and from raiders,
    /// beasts or a dragon it can see coming (indoors if there's a door near; a dragon, from anywhere
    /// it's seen). It doesn't fight. True while it's getting clear or keeping its head down.
    /// </summary>
    private bool Safety(Simulation sim, Entity me, PerceptionView view, long now)
    {
        var w = sim.World;
        var here = w.Get<Position>(me).Cell;
        // Too hot where it stands (hours at the forge's mouth): a step back to cool off.
        var tol = w.Get<Creature>(me).Tolerance(now);
        if (Environment.Warmth.FeltAt(sim, here) > tol.SafeMax - 8 && Ai.AnimalBrain.FleeSpot(sim, me, here, 5) is { } cooler)
        {
            Doing = "stepping back from the heat";
            Walk(sim, me, cooler, now);
            return true;
        }
        // Fire close by, or anywhere in the building it's in (a roof alight overhead): out, and away.
        var building = Buildings.At(sim, here);
        foreach (var i in sim.Fire.BurningTiles)
        {
            var f = sim.Map.CellOf(i);
            var near = Math.Abs(f.Z - here.Z) <= 3 && Maps.Map.Distance(f, here) <= 3;
            if ((near || (!building.IsNull && Buildings.At(sim, f) == building)) && Ai.AnimalBrain.FleeSpot(sim, me, f, 10) is { } away)
            {
                Doing = "getting away from the fire";
                if (now - _lastWalk >= ThinkTicks * 2)
                {
                    Send(new AvatarGoToCommand(me, away));
                    _lastWalk = now;
                }
                return true;
            }
        }
        Entity? foe = null;
        var nearest = int.MaxValue;
        foreach (var e in view.Creatures())
            if (w.Get<Creature>(e) is { Dead: false, Downed: false } c && Factions.Attacker(c.Faction) && !w.Has<Combat.Leaving>(e))
            {
                var d = Maps.Map.Distance(w.Get<Position>(e).Cell, here);
                if ((d <= Ai.Defence.VillagerRange || c.Def.Race == Race.Dragon) && d < nearest)
                    (foe, nearest) = (e, d);
            }
        if (foe is not { } threat)
            return false;
        var at = w.Get<Position>(threat).Cell;
        if (sim.Rooms.RoomAt(here) is { Indoors: true } && nearest > 2)
        {
            Doing = "keeping indoors";
            return true;
        }
        if ((Ai.Defence.Shelter(sim, me, at) ?? Ai.AnimalBrain.FleeSpot(sim, me, at, 12)) is { } safe)
        {
            Doing = "getting out of the way";
            if (now - _lastWalk >= ThinkTicks * 2)
            {
                Send(new AvatarGoToCommand(me, safe));
                _lastWalk = now;
            }
            return true;
        }
        return false;
    }

    /// <summary>The tinker mends what a crisis broke: wood from the shop, then to the gap, and brace it. True while at it.</summary>
    private bool MendTown(Simulation sim, Entity me, PerceptionView view, long now)
    {
        var w = sim.World;
        if (!w.TryGet<Trade>(me, out var trade) || !trade.Def.MendsTown || sim.Repairs.Breaches.Count == 0)
            return false;
        var breach = sim.Repairs.Breaches[0];
        if (Wagons.WoodOn(sim, me) < Repairs.MendWood)
            return FetchWood(sim, me, view, now);
        var here = w.Get<Position>(me).Cell;
        if (!breach.Cells.Exists(c => AvatarSystem.InReach(here, c.Cell)))
        {
            var near = breach.Cells.Select(c => c.Cell).Where(c => Ai.Navigation.CanReach(sim, me, here, c, PathEnd.Touch))
                .OrderBy(c => Maps.Map.Distance(c, here)).Select(c => (int3?)c).FirstOrDefault();
            if (near is not { } gap)
                return false;
            Doing = $"going to mend the {breach.Label}";
            Walk(sim, me, gap, now);
            return true;
        }
        Doing = $"mending the {breach.Label}";
        Send(new MendBreachCommand(me, breach.Id));
        return true;
    }
}

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
    /// <summary>
    /// At night, with nothing in hand and tired, it goes to bed like a player would, so a solo
    /// round's night can pass (time runs on only while every player sleeps). Asleep, it does
    /// nothing at all: any step would wake it. True while it's sleeping or on its way.
    /// </summary>
    private bool Bedtime(Simulation sim, Entity me, long now)
    {
        var w = sim.World;
        if (w.Get<Mind>(me).Asleep)
        {
            Doing = "asleep";
            return true;
        }
        if (_task != 0 || !Ai.AiQueries.IsNight(sim) || w.Get<Needs>(me).Get(sim.Defs.Need("Rest")) > BedtimeRest)
            return false;
        Doing = "going to bed";
        var bed = Ai.AiQueries.FindFreeBed(sim, me, forTheNight: true);
        if (bed.IsNull || w.Get<Position>(me).Cell == w.Get<Thing>(bed).Cell)
            Send(new SleepCommand(me));
        else if (now - _lastWalk >= ThinkTicks * 2)
        {
            Send(new AvatarGoToCommand(me, w.Get<Thing>(bed).Cell));
            _lastWalk = now;
        }
        return true;
    }

    /// <summary>
    /// At supper and not fed (<see cref="WellBeing"/>), with nothing in hand: something to eat from the
    /// nearest store or pile, eaten where it's kept. Eating's a bonus, so a bot only stops for it of an
    /// evening, its day's work done, never at breakfast with the round just begun.
    /// </summary>
    private bool Supper(Simulation sim, Entity me, long now)
    {
        var w = sim.World;
        var hour = sim.Clock.HourOfDay;
        var supper = hour >= Shops.ShopSystem.SupperFromHour && hour < Shops.ShopSystem.SupperUntilHour;
        if (_task != 0 || WellBeing.Fed(sim, me) > 0f || (!supper && w.Get<Mind>(me).Job is not EatHeldJob))
            return false;
        if (w.Get<Mind>(me).Job is EatHeldJob)
        {
            Doing = "eating";
            return true;
        }
        if (_food is { } chosen && (!w.IsAlive(chosen) || now - _foodSince > SimClock.TicksPerSecond * 60))
        {
            // Couldn't get at it in a minute: try something else for a while.
            _noFood.Add(chosen);
            _food = null;
        }
        if (now - _noFoodSince > SimClock.TicksPerHour)
        {
            _noFood.Clear();
            _noFoodSince = now;
        }
        if (_food == null)
        {
            var here = w.Get<Position>(me).Cell;
            var eater = w.Get<Creature>(me).Def;
            var best = int.MaxValue;
            foreach (var e in sim.AvailableItems())
            {
                var d = w.Get<Thing>(e).Def;
                if (!d.IsFood || !d.EdibleBy(eater) || (d.IsMeat && !d.Cooked) || _noFood.Contains(e) || sim.Reservations.IsReservedByOther(e, me)
                    || !WellBeing.WouldFeed(sim, me, WellBeing.FedStrength(d, Meal.Of(sim, e), knowRot: false)))
                    continue;
                var at = sim.LocationOf(e);
                var dist = Maps.Map.Distance(at, here);
                if (dist < best && dist <= 60 && Ai.Navigation.CanReach(sim, me, here, at, w.Get<Thing>(e).Spawned ? PathEnd.OnTile : PathEnd.Touch))
                    (_food, best) = (e, dist);
            }
            _foodSince = now;
        }
        if (_food is not { } food)
            return false;
        Doing = "getting something to eat";
        if (AvatarSystem.CanEat(sim, me, food))
            Send(new EatCommand(me, food));
        else
            Walk(sim, me, sim.LocationOf(food), now);
        return true;
    }

    private Entity? _food;
    private long _foodSince, _noFoodSince;
    private readonly HashSet<Entity> _noFood = [];

    /// <summary>Rest below which a bot turns in at night.</summary>
    private const float BedtimeRest = 0.85f;
}

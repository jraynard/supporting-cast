using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Hospitality;

/// <summary>
/// A customer taking their time over what they were served: the drink or the bowl in hand, they
/// find a seat and sit with it a while (<see cref="SavourJob"/>). Paid for and tasted when it's
/// handed over; its effects (the drink going to their head, the meal filling them) come when it's
/// finished, and the empty is left where they sat. Sat together over a drink, they talk, so a full
/// common room is one where rumours are overheard.
/// </summary>
public struct Savouring
{
    /// <summary>What they're drinking or eating, in their hand.</summary>
    public Entity Item;

    public long Since, Until;
    public bool Meal;

    /// <summary>A meal: how many servings they're eating, and the rot in them (hidden).</summary>
    public int Count;
    public float Rot;

    /// <summary>How many of their chances to talk over it (<see cref="Table.TalkChances"/>) have come round.</summary>
    public int Talks;
}

/// <summary>Being served and finishing up (<see cref="Savouring"/>). Ticked by <see cref="DrinkingSystem"/>.</summary>
public static class Table
{
    /// <summary>Game hours over a drink and over a meal, give or take a fifth.</summary>
    public const float DrinkHours = 0.6f, MealHours = 0.8f;

    /// <summary>How far they look for a free seat.</summary>
    public const int SeatSearch = 10;

    /// <summary>
    /// Chances over a drink or a meal to talk (a third and two thirds of the way through), and the
    /// odds of each: they tell a rumour to whoever's sat near. Drink makes it likelier.
    /// </summary>
    public const int TalkChances = 2;
    public const float TalkChance = 0.5f;

    public static bool IsSavouring(Simulation sim, Entity who) => sim.World.Has<Savouring>(who);

    /// <summary>
    /// They take what they were served in hand to sit with (anything already in hand goes to the
    /// pack, else at their feet). Anything they were still on is finished first, in a gulp.
    /// </summary>
    public static void Begin(Simulation sim, Entity who, Entity item, bool meal, int count, float rot, SimRandom rand)
    {
        var w = sim.World;
        if (w.Has<Savouring>(who))
            Finish(sim, who, rand);
        var held = w.Get<Carrying>(who).Item;
        if (!held.IsNull)
        {
            w.Get<Carrying>(who).Item = Entity.Null;
            var pack = sim.PackOf(who);
            var left = pack.IsNull ? held : sim.Store(pack, held);
            if (!left.IsNull)
                sim.PlaceItemNear(left, w.Get<Position>(who).Cell);
        }
        w.Get<Carrying>(who).Item = item;
        var now = sim.Clock.Tick;
        var hours = (meal ? MealHours : DrinkHours) * rand.Range(0.8f, 1.2f);
        w.Add(who, new Savouring { Item = item, Since = now, Until = now + SimClock.HoursToTicks(hours), Meal = meal, Count = count, Rot = rot });
    }

    /// <summary>
    /// Done with it: the drink goes to their head (<see cref="DrinkingSystem.Drank"/>) or the meal
    /// fills them, and the empty tankard or bowl is set down where they are. If it's left their hand
    /// (dropped, taken), there's nothing to finish.
    /// </summary>
    public static void Finish(Simulation sim, Entity who, SimRandom rand)
    {
        var w = sim.World;
        if (!w.TryGet<Savouring>(who, out var s))
            return;
        w.Remove<Savouring>(who);
        if (!w.IsAlive(s.Item))
            return;
        ref var carrying = ref w.Get<Carrying>(who);
        if (carrying.Item == s.Item)
            carrying.Item = Entity.Null;
        else
            return;
        var def = w.Get<Thing>(s.Item).Def;
        if (s.Meal)
        {
            EatJob.Nourish(sim, who, def, s.Count, s.Rot);
            if (rand.NextFloat() < 0.5f)
                sim.Mess.Spill(sim, MessKind.Crumbs, w.Get<Position>(who).Cell, 0.4f);
        }
        else
            sim.Drinking.Drank(sim, who, s.Item);
        w.Destroy(s.Item);
        Vessels.LeaveAfter(sim, who, def);
    }

    /// <summary>Those done finish; those gone or dead drop it; a third and two thirds of the way through, some talk.</summary>
    internal static void Update(Simulation sim, SimRandom rand)
    {
        var w = sim.World;
        if (w.Count<Savouring>() == 0)
            return;
        var now = sim.Clock.Tick;
        foreach (var e in w.Query<Savouring>())
        {
            var c = w.Get<Creature>(e);
            if (c.Dead)
            {
                w.Remove<Savouring>(e);
                continue;
            }
            ref var s = ref w.Get<Savouring>(e);
            if (now >= s.Until || w.Has<Combat.Leaving>(e))
            {
                Finish(sim, e, rand);
                continue;
            }
            if (s.Talks >= TalkChances || now < s.Since + (s.Until - s.Since) * (s.Talks + 1) / (TalkChances + 1))
                continue;
            s.Talks++;
            var tipsy = DrinkingSystem.LevelOf(sim, e);
            if (sim.Round.Live && w.Has<Adventurer>(e) && SpeechSystem.CanAct(sim, e)
                && rand.NextFloat() < TalkChance + 0.4f * tipsy)
                sim.Expeditions.Gossip(sim, e, spread: Math.Max(0.05f, 0.25f - 0.2f * tipsy));
        }
    }

    /// <summary>The nearest free seat in reach, in the room they're in if there's one there. Entity.Null if none.</summary>
    public static Entity FreeSeat(Simulation sim, Entity who)
    {
        var w = sim.World;
        var map = sim.Map;
        var at = w.Get<Position>(who).Cell;
        var room = sim.Rooms.RoomAt(at);
        var best = Entity.Null;
        var bestScore = int.MaxValue;
        for (var dy = -SeatSearch; dy <= SeatSearch; dy++)
            for (var dx = -SeatSearch; dx <= SeatSearch; dx++)
            {
                var c = new int3(at.X + dx, at.Y + dy, at.Z);
                if (!map.InBounds(c) || map.Structure[map.Idx(c)] is not { IsNull: false } seat || w.Has<Construction>(seat)
                    || w.Get<Thing>(seat).Def.Building is not { Seat: true } || sim.Reservations.IsReservedByOther(seat, who))
                    continue;
                // Somewhere else in the room, rather than across a wall.
                var score = Maps.Map.Distance(at, c) + (room != null && sim.Rooms.RoomAt(c) != room ? 100 : 0);
                if (score < bestScore && Navigation.CanReach(sim, who, at, c, PathEnd.OnTile))
                    (best, bestScore) = (seat, score);
            }
        return best;
    }
}

/// <summary>
/// Sitting with a drink or a meal (<see cref="Savouring"/>): to a free seat if there is one, and
/// there until it's finished. Without a seat they stand where they are with it.
/// </summary>
public sealed class SavourJob(Entity seat) : Job
{
    // Not interruptible, so it takes over from a stroll the moment they're served; it ends every
    // ten seconds (sat or not) for the brain to look again.

    /// <summary>Sat down on their seat (for the pose).</summary>
    public bool Seated { get; private set; }

    public Entity Seat => seat;

    protected override IEnumerable<JobStatus> Run()
    {
        var meal = Get<Savouring>(Pawn).Meal;
        if (!seat.IsNull)
        {
            Report = "Looking for a seat";
            if (!Reserve(seat)) yield break;
            foreach (var s in GoTo(Get<Thing>(seat).Cell, PathEnd.OnTile)) yield return s;
            if (HasFailed) yield break;
            Seated = true;
        }
        Report = meal ? "Eating" : "Nursing a drink";
        // Looked at again now and then, so anything more pressing gets a look in.
        for (var t = 0; t < 600 && Sim.World.Has<Savouring>(Pawn) && (seat.IsNull || Alive(seat)); t++)
            yield return JobStatus.Running;
    }
}

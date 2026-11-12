using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Maps;
using SupportingCast.Sim.Systems;
using SupportingCast.Sim.Work;

namespace SupportingCast.Sim.Shops;

/// <summary>
/// An AI innkeeper serves an order: pours a drink at the cask (as well as their Cooking skill
/// allows, a basic hand next to a player's <see cref="PourSession"/>) or ladles a bowl from the
/// inn's stew pot (else fetches a cooked meal from the stores), and takes it to whoever asked.
/// </summary>
public sealed class ServeJob(int order) : Job
{
    /// <summary>Ticks it takes an AI innkeeper to pour.</summary>
    public const int PourTicks = 120;

    /// <summary>Ticks it takes to ladle a bowl.</summary>
    public const int LadleTicks = 60;

    /// <summary>An order this villager could serve now: their trade's, known to them, theirs or nobody's, with something to serve.</summary>
    public static Job? For(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        if (!w.TryGet<Trade>(pawn, out var trade) || trade.Def.WorkplaceDef is not { } workplace)
            return null;
        var from = w.Get<Position>(pawn).Cell;
        foreach (var e in sim.Shops.FrontFirst(sim))
        {
            if (!e.IsOpen || e.Kind.Kind is not (ServiceKind.Drink or ServiceKind.Meal) || e.Kind.AtDef != workplace
                || !e.KnownBy.Contains(pawn) || (!e.Assignee.IsNull && e.Assignee != pawn) || !w.IsAlive(e.Poster))
                continue;
            var supply = e.Kind.Kind == ServiceKind.Drink ? Cask(sim, pawn) : Pot(sim, e.Where) is { IsNull: false } pot ? pot : Meal(sim, pawn, e.Poster);
            if (!supply.IsNull && Navigation.CanReach(sim, pawn, from, w.Get<Position>(e.Poster).Cell, PathEnd.Touch))
                return new ServeJob(e.Id);
        }
        return null;
    }

    /// <summary>The nearest cask with a drink left in it.</summary>
    public static Entity Cask(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        var from = w.Get<Position>(pawn).Cell;
        var best = Entity.Null;
        var bestDist = int.MaxValue;
        foreach (var e in w.Each<Thing>())
        {
            var t = w.Get<Thing>(e);
            if (!t.Spawned || t.Def.Building?.Cask == null || w.Has<Construction>(e) || PourSession.ServingsLeft(sim, e) <= 0)
                continue;
            var d = Map.Distance(from, t.Cell);
            if (d < bestDist)
            {
                best = e;
                bestDist = d;
            }
        }
        return best;
    }

    /// <summary>The nearest cooked meal the diner can eat, and that hasn't turned: a villager can smell it.</summary>
    private static Entity Meal(Simulation sim, Entity pawn, Entity diner)
    {
        var eater = sim.World.Get<Creature>(diner).Def;
        return AiQueries.FindNearestItem(sim, pawn, (e, def) => def.Cooked && def.EdibleBy(eater) && !FoodPoisoning.IsSpoiled(sim, e));
    }

    /// <summary>The shop's stew pot, if it has a cooked bowl to ladle; Entity.Null otherwise.</summary>
    public static Entity Pot(Simulation sim, Entity shop) =>
        Cooking.PotIn(sim, shop) is { IsNull: false } pot && Cooking.PotOf(sim, pot).Cooked && Cooking.ServingsLeft(sim, pot) > 0
        && !sim.Reservations.IsReservedByOther(pot, Entity.Null) ? pot : Entity.Null;

    protected override IEnumerable<JobStatus> Run()
    {
        var orders = Sim.Shops;
        if (orders.Find(order) is not { IsOpen: true } e || (e.Assignee != Pawn && !orders.Accept(Sim, Pawn, order)))
        {
            Fail("someone else has it");
            yield break;
        }
        Entity served;
        if (e.Kind.Kind == ServiceKind.Drink)
        {
            var cask = Cask(Sim, Pawn);
            if (cask.IsNull || !Reserve(cask))
            {
                Fail("nothing to pour");
                yield break;
            }
            Report = $"Pouring for {e.PosterName}";
            foreach (var s in GoTo(Get<Thing>(cask).Cell, PathEnd.Touch)) yield return s;
            if (HasFailed) yield break;
            foreach (var s in Wait(PourTicks)) yield return s;
            if (PourSession.ServingsLeft(Sim, cask) <= 0)
            {
                Fail("the cask ran dry");
                yield break;
            }
            PourSession.Draw(Sim, cask);
            var level = SkillRules.LevelOf(Sim, Pawn, Skill.Cooking);
            SkillRules.Learn(Sim, Pawn, Skill.Cooking, 20f);
            served = PourSession.PourInto(Sim, Pawn, cask, Crafting.QualityFrom(0.3f + 0.035f * level + Sim.Rand.Range(-0.15f, 0.15f)));
        }
        else if (Pot(Sim, e.Where) is { IsNull: false } pot && Reserve(pot))
        {
            Report = $"Ladling a bowl for {e.PosterName}";
            foreach (var s in GoTo(Get<Thing>(pot).Cell, PathEnd.Touch)) yield return s;
            if (HasFailed) yield break;
            foreach (var s in Wait(LadleTicks)) yield return s;
            served = Cooking.Ladle(Sim, Pawn, pot);
            if (served.IsNull)
            {
                Fail("the pot's empty");
                yield break;
            }
        }
        else
        {
            var food = Meal(Sim, Pawn, e.Poster);
            if (food.IsNull || !Reserve(food))
            {
                Fail("nothing cooked to serve");
                yield break;
            }
            Report = $"Fetching a meal for {e.PosterName}";
            foreach (var s in GoTo(Get<Thing>(food).Cell, PathEnd.OnTile)) yield return s;
            if (HasFailed) yield break;
            if (!Sim.IsAvailable(food))
            {
                Fail("the food's gone");
                yield break;
            }
            var def = Get<Thing>(food).Def;
            var portion = Sim.TakeFromStack(food, Math.Min(EatJob.Wanted(Sim, e.Poster, def), Get<Stack>(food).Count));
            // In hand, so it doesn't merge into a stack in the pack on the way.
            if (Get<Carrying>(Pawn).Item.IsNull)
                Get<Carrying>(Pawn).Item = portion;
            else
                Inventory.Stow(Sim, Pawn, portion);
            served = portion;
        }

        Report = $"Serving {e.PosterName}";
        foreach (var s in GoToEntity(e.Poster, PathEnd.Touch, 1500)) yield return s;
        if (HasFailed) yield break;
        if (!Alive(served) || !Inventory.OnPerson(Sim, Pawn, served) || !Handing.InReach(Sim, Pawn, e.Poster))
        {
            Fail("couldn't serve it");
            yield break;
        }
        orders.Received(Sim, Pawn, e.Poster, Inventory.TakeFromPerson(Sim, Pawn, served, Get<Stack>(served).Count));
    }
}

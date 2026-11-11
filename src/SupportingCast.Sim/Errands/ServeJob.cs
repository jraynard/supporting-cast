using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Maps;
using SupportingCast.Sim.Systems;
using SupportingCast.Sim.Work;

namespace SupportingCast.Sim.Errands;

/// <summary>
/// An AI innkeeper serves an order: pours a drink at the cask (as well as their Cooking skill
/// allows, a basic hand next to a player's <see cref="PourSession"/>) or fetches a cooked meal
/// from the stores, and takes it to whoever asked.
/// </summary>
public sealed class ServeJob(int errand) : Job
{
    /// <summary>Ticks it takes an AI innkeeper to pour.</summary>
    public const int PourTicks = 120;

    /// <summary>An order this villager could serve now: their trade's, known to them, theirs or nobody's, with something to serve.</summary>
    public static Job? For(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        if (!w.TryGet<Trade>(pawn, out var trade) || trade.Def.WorkplaceDef is not { } workplace)
            return null;
        var from = w.Get<Position>(pawn).Cell;
        foreach (var e in sim.Errands.All)
        {
            if (!e.IsOpen || e.Kind.Kind is not (ErrandKind.Drink or ErrandKind.Meal) || e.Kind.AtDef != workplace
                || !e.KnownBy.Contains(pawn) || (!e.Assignee.IsNull && e.Assignee != pawn) || !w.IsAlive(e.Poster))
                continue;
            var supply = e.Kind.Kind == ErrandKind.Drink ? Cask(sim, pawn) : Meal(sim, pawn, e.Poster);
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

    /// <summary>The nearest cooked meal the diner can eat.</summary>
    private static Entity Meal(Simulation sim, Entity pawn, Entity diner)
    {
        var eater = sim.World.Get<Creature>(diner).Def;
        return AiQueries.FindNearestItem(sim, pawn, (_, def) => def.Cooked && def.EdibleBy(eater));
    }

    protected override IEnumerable<JobStatus> Run()
    {
        var errands = Sim.Errands;
        if (errands.Find(errand) is not { IsOpen: true } e || (e.Assignee != Pawn && !errands.Accept(Sim, Pawn, errand)))
        {
            Fail("someone else has it");
            yield break;
        }
        Entity served;
        if (e.Kind.Kind == ErrandKind.Drink)
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
        errands.Received(Sim, Pawn, e.Poster, Inventory.TakeFromPerson(Sim, Pawn, served, Get<Stack>(served).Count));
    }
}

using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Maps;
using SupportingCast.Sim.Systems;
using SupportingCast.Sim.Work;

namespace SupportingCast.Sim.Shops;

/// <summary>
/// An AI innkeeper keeps the pot going: fills it from the larder (or whatever raw food is to hand)
/// when it's running low, then stands over it stirring now and then until it's cooked through, and
/// seasons it as well as their Cooking allows. The pot cooks by the same rules as a player's
/// (<see cref="Cooking.Step"/>): a careless fire still scorches it.
/// </summary>
public sealed class CookPotJob(Entity pot) : Job
{
    /// <summary>Bowls left at which the keeper fills the pot up again.</summary>
    public const int RefillBelow = 3;

    /// <summary>The nutrition the keeper aims to have in the pot: some ten bowls.</summary>
    public const float FillTo = 2.5f;

    /// <summary>The longest they'll stand over one pot.</summary>
    public const int MaxTendTicks = SimClock.TicksPerHour * 3;

    /// <summary>The pot at this villager's workplace, if it wants filling or tending and they can.</summary>
    public static Job? For(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        if (!w.TryGet<Trade>(pawn, out var trade) || trade.Def.WorkplaceDef is not { } workplace)
            return null;
        var pot = Cooking.PotIn(sim, ShopSystem.BuildingOf(sim, workplace));
        if (pot.IsNull || sim.Reservations.IsReservedByOther(pot, pawn) || Cooking.BeingWorked(sim, pot) || !Cooking.Lit(sim, pot))
            return null;
        var p = Cooking.PotOf(sim, pot);
        var wantsFood = Cooking.ServingsLeft(sim, pot) < RefillBelow && p.Nutrition < FillTo && !Ingredient(sim, pawn).IsNull;
        if (!wantsFood && (p.Empty || p.Cooked))
            return null;
        return Navigation.CanReach(sim, pawn, w.Get<Position>(pawn).Cell, w.Get<Thing>(pot).Cell, PathEnd.Touch) ? new CookPotJob(pot) : null;
    }

    /// <summary>The nearest raw food fit for the pot: a villager can smell when it's turned.</summary>
    private static Entity Ingredient(Simulation sim, Entity pawn) =>
        AiQueries.FindNearestItem(sim, pawn, (e, def) => Cooking.IsIngredient(def) && !FoodPoisoning.IsSpoiled(sim, e), 80);

    protected override IEnumerable<JobStatus> Run()
    {
        if (!Cooking.IsPot(Sim, pot) || !Reserve(pot))
        {
            Fail("no pot");
            yield break;
        }
        var p = Cooking.PotOf(Sim, pot);
        // Fill it: a few trips to the larder at most.
        for (var trip = 0; trip < 4 && Cooking.ServingsLeft(Sim, pot) < RefillBelow && p.Nutrition < FillTo; trip++)
        {
            var food = Ingredient(Sim, Pawn);
            if (food.IsNull || !Reserve(food))
                break;
            Report = "Fetching food for the pot";
            foreach (var s in GoTo(Get<Thing>(food).Cell, PathEnd.OnTile)) yield return s;
            if (HasFailed) yield break;
            if (!Sim.IsAvailable(food))
                break;
            var def = Get<Thing>(food).Def;
            var want = Math.Max(1, (int)MathF.Ceiling((FillTo - p.Nutrition) / def.Nutrition));
            var carried = Sim.TakeFromStack(food, Math.Min(want, Get<Stack>(food).Count));
            if (Get<Carrying>(Pawn).Item.IsNull)
                Get<Carrying>(Pawn).Item = carried;
            else
                Inventory.Stow(Sim, Pawn, carried);
            Report = "Filling the pot";
            foreach (var s in GoTo(Get<Thing>(pot).Cell, PathEnd.Touch)) yield return s;
            if (HasFailed) yield break;
            if (!Alive(carried))
                break;
            Cooking.Add(Sim, pot, carried);
            if (Alive(carried) && Inventory.OnPerson(Sim, Pawn, carried))
                Sim.PlaceItemNear(Inventory.TakeFromPerson(Sim, Pawn, carried, Get<Stack>(carried).Count), Get<Position>(Pawn).Cell);
        }
        if (p.Empty)
        {
            Fail("nothing to cook");
            yield break;
        }
        if (p.Cooked)
            yield break;

        // Tend it: a steady hand keeps the fire at a simmer and the spoon going now and then.
        Report = "Cooking a stew";
        foreach (var s in GoTo(Get<Thing>(pot).Cell, PathEnd.Touch)) yield return s;
        if (HasFailed) yield break;
        var level = SkillRules.LevelOf(Sim, Pawn, Skill.Cooking);
        p.Flame = Math.Clamp(0.64f - 0.006f * level, Cooking.SimmerAt + 0.04f, 0.64f);
        for (var t = 0; t < MaxTendTicks && !p.Cooked; t += 60)
        {
            if (!Alive(pot) || p.Empty || !Cooking.Lit(Sim, pot))
            {
                Fail("the fire's gone out");
                yield break;
            }
            p.TendedUntil = Sim.Clock.Tick + 90;
            foreach (var s in Wait(60)) yield return s;
        }
        if (!p.Cooked)
            yield break;
        // Season it, by taste: a practised cook gets close.
        var off = Math.Max(0.02f, 0.2f - 0.015f * level);
        p.Seasoning = Math.Max(p.Seasoning, p.SeasonTarget + Sim.Rand.Range(-off, off));
        p.Flame = (Cooking.SteamingAt + Cooking.SimmerAt) * 0.5f;
        SkillRules.Learn(Sim, Pawn, Skill.Cooking, 35f);
    }
}

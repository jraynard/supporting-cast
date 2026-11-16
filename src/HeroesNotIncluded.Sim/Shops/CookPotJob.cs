using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Shops;

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
        if (!w.TryGet<Trade>(pawn, out var trade) || !trade.Def.HasWorkplace || !trade.Def.Serving(ServiceKind.Meal))
            return null;
        var pot = Cooking.PotIn(sim, ShopSystem.WorkplaceOf(sim, trade.Def));
        if (pot.IsNull || sim.Reservations.IsReservedByOther(pot, pawn) || Cooking.BeingWorked(sim, pot) || !Cooking.Lit(sim, pot))
            return null;
        var p = Cooking.PotOf(sim, pot);
        var wantsFood = Cooking.ServingsLeft(sim, pot) < RefillBelow && p.Nutrition < FillTo && !Ingredient(sim, pawn, grains: Porridge(sim, p)).IsNull;
        if (!wantsFood && (p.Empty || p.Cooked))
            return null;
        return Navigation.CanReach(sim, pawn, w.Get<Position>(pawn).Cell, w.Get<Thing>(pot).Cell, PathEnd.Touch) ? new CookPotJob(pot) : null;
    }

    /// <summary>
    /// The nearest raw food fit for the pot (or a seasoner for it): a villager can smell when it's
    /// turned. Seasoners only from a larder: the honey and herbs behind the bar are the keeper's.
    /// </summary>
    private static Entity Ingredient(Simulation sim, Entity pawn, bool seasoner = false, bool grains = false) =>
        AiQueries.FindNearestItem(sim, pawn, (e, def) => Cooking.IsIngredient(def) && def.IsSeasoner == seasoner && def.Grain == grains && !FoodPoisoning.IsSpoiled(sim, e)
            && (!seasoner || (sim.World.TryGet<Stored>(e, out var st) && sim.World.Get<Thing>(st.In).Def.Name == "Larder")), 80);

    /// <summary>Early hours the AI cook puts oats on, for porridge at breakfast.</summary>
    public const float PorridgeFromHour = 4f, PorridgeUntilHour = 10f;

    /// <summary>Whether this pot's a porridge pot: one started of an early morning, or one that's porridge already.</summary>
    private static bool Porridge(Simulation sim, StewPot p) =>
        p.Empty ? sim.Clock.HourOfDay is >= PorridgeFromHour and < PorridgeUntilHour : Cooking.GrainShare(p) >= Cooking.PorridgeFrom;

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
            var food = Ingredient(Sim, Pawn, grains: Porridge(Sim, p));
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
        // Something to give it a taste, if the larder has it: one seasoner a fill.
        if (Cooking.SeasonersIn(p) == 0 && !p.Cooked && Ingredient(Sim, Pawn, seasoner: true) is { IsNull: false } seasoner && Reserve(seasoner))
        {
            Report = "Fetching something for the pot";
            foreach (var s in GoTo(Get<Thing>(seasoner).Cell, PathEnd.OnTile)) yield return s;
            if (HasFailed) yield break;
            if (Sim.IsAvailable(seasoner))
            {
                var pinch = Sim.TakeFromStack(seasoner, 1);
                if (Get<Carrying>(Pawn).Item.IsNull)
                    Get<Carrying>(Pawn).Item = pinch;
                else
                    Inventory.Stow(Sim, Pawn, pinch);
                foreach (var s in GoTo(Get<Thing>(pot).Cell, PathEnd.Touch)) yield return s;
                if (HasFailed) yield break;
                if (Alive(pinch))
                {
                    Cooking.Add(Sim, pot, pinch);
                    if (Alive(pinch) && Inventory.OnPerson(Sim, Pawn, pinch))
                        Sim.PlaceItemNear(Inventory.TakeFromPerson(Sim, Pawn, pinch, Get<Stack>(pinch).Count), Get<Position>(Pawn).Cell);
                }
            }
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

/// <summary>
/// An AI cook dresses what the carter brought whole (a side, a bird) at the kitchen's butcher block,
/// without the games and at an average hand (<see cref="Butchery.DressAll"/>): a while at the block,
/// and what comes off goes back in the larder.
/// </summary>
public sealed class DressJob(Entity block, Entity larder) : Job
{
    /// <summary>How long dressing the lot takes at the block.</summary>
    public const int Ticks = SimClock.TicksPerSecond * 40;

    public override Entity Focus => block;

    public static Job? For(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        if (!w.TryGet<Trade>(pawn, out var trade) || !trade.Def.HasWorkplace || !trade.Def.Serving(ServiceKind.Meal))
            return null;
        var inn = ShopSystem.WorkplaceOf(sim, trade.Def);
        if (inn.IsNull)
            return null;
        var parts = Buildings.Parts(sim, inn).ToList();
        var block = parts.FirstOrDefault(e => Butchery.IsBlock(sim, e), Entity.Null);
        var larder = parts.FirstOrDefault(e => w.Get<Thing>(e).Def.Name == "Larder", Entity.Null);
        if (block.IsNull || larder.IsNull || sim.Reservations.IsReservedByOther(block, pawn)
            || !sim.StoredIn(larder).Any(e => w.Get<Thing>(e).Def is { Joints: not null } or { Plucks: not null }))
            return null;
        return Navigation.CanReach(sim, pawn, w.Get<Position>(pawn).Cell, w.Get<Thing>(block).Cell, PathEnd.Touch) ? new DressJob(block, larder) : null;
    }

    protected override IEnumerable<JobStatus> Run()
    {
        if (!Butchery.IsBlock(Sim, block) || !Reserve(block))
        {
            Fail("no block");
            yield break;
        }
        Report = "Dressing meat at the block";
        foreach (var s in GoTo(Get<Thing>(block).Cell, PathEnd.Touch)) yield return s;
        if (HasFailed) yield break;
        for (var t = 0; t < Ticks; t += 60)
        {
            if (!Alive(block) || !Alive(larder))
            {
                Fail("the block's gone");
                yield break;
            }
            foreach (var s in Wait(60)) yield return s;
        }
        Butchery.DressAll(Sim, larder);
    }
}

/// <summary>
/// An AI cook goes after a rat in its larder with whatever's in its hand (the kitchen knife): free
/// movement and blows, as a player would, and the rat darting off.
/// </summary>
public sealed class RatJob(Entity rat) : Job
{
    /// <summary>How long it chases one before giving it up for now.</summary>
    public const int ChaseTicks = SimClock.TicksPerSecond * 30;

    public override Entity Focus => rat;

    public static Job? For(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        if (!w.TryGet<Trade>(pawn, out var trade) || !trade.Def.HasWorkplace || !trade.Def.Serving(ServiceKind.Meal))
            return null;
        var inn = ShopSystem.WorkplaceOf(sim, trade.Def);
        if (inn.IsNull || !Combat.CombatRules.Attacks(sim, pawn).Any(a => !a.Ranged))
            return null;
        var at = w.Get<Position>(pawn).Cell;
        foreach (var rat in Hospitality.RatSystem.In(sim, inn, at))
            if (!sim.Reservations.IsReservedByOther(rat, pawn) && Navigation.CanReach(sim, pawn, at, w.Get<Position>(rat).Cell, PathEnd.Touch))
                return new RatJob(rat);
        return null;
    }

    protected override IEnumerable<JobStatus> Run()
    {
        Report = "Chasing a rat";
        if (Combat.CombatRules.Attacks(Sim, Pawn).FirstOrDefault(a => !a.Ranged) is not { } attack || !Reserve(rat))
        {
            Fail("can't go after it");
            yield break;
        }
        var until = Sim.Clock.Tick + ChaseTicks;
        foreach (var s in FightFree(rat, attack, () => Sim.Clock.Tick < until && Alive(rat) && !Get<Creature>(rat).Dead, 0f, 16f))
            yield return s;
    }
}

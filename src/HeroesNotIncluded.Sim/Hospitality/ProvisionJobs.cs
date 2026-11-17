using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Hospitality;

/// <summary>
/// An AI innkeeper sends for a hamper (<see cref="Provisions"/>): the merchandise shelf's short of
/// rations, or the warming shelf of dishes, and none's on order. Posting it is all the job does,
/// from wherever they are; once it's up the order holds their place in the hatch's line (unless a
/// labourer's running it over), and the brain walks them over to wait (<c>HumanBrain</c>, "in line").
/// </summary>
public sealed class OrderProvisionsJob : Job
{
    public static Job? For(Simulation sim, Entity pawn)
    {
        if (!Provisions.KeepsInn(sim, pawn) || !sim.Round.Live || ShopSystem.SmallHours(sim) || !Provisions.WantsSupplies(sim)
            || Provisions.IsHamper(sim, sim.World.Get<Carrying>(pawn).Item) || Provisions.Refusal(sim, pawn) != null)
            return null;
        return new OrderProvisionsJob();
    }

    protected override IEnumerable<JobStatus> Run()
    {
        Report = "Sending to the kitchen for a hamper";
        if (Provisions.Order(Sim, Pawn) is { } why)
            Fail(why);
        yield break;
    }
}

/// <summary>An AI innkeeper (or the labourer running it) carries the hamper in their arms to the inn's shelf and unpacks it.</summary>
public sealed class UnpackHamperJob(Entity shelf) : Job
{
    public static Job? For(Simulation sim, Entity pawn)
    {
        if (!(Provisions.KeepsInn(sim, pawn) || Provisions.Running(sim, pawn) != null) || !Provisions.IsHamper(sim, sim.World.Get<Carrying>(pawn).Item)
            || Provisions.UnpackAt(sim) is not { IsNull: false } shelf)
            return null;
        return new UnpackHamperJob(shelf);
    }

    protected override IEnumerable<JobStatus> Run()
    {
        Report = "Carrying the hamper back to the inn";
        foreach (var s in GoToBeside(shelf)) yield return s;
        if (HasFailed) yield break;
        if (!Alive(shelf) || !Provisions.Unpack(Sim, Pawn, shelf))
            Fail("couldn't unpack it");
    }
}

/// <summary>
/// A labourer runs the inn's hamper over from the cookhouse (<see cref="Provisions.TakeToRun"/>):
/// off the hatch, across to the inn, unpacked there, for a small fee from the innkeeper. Only where
/// the kitchen stands apart from the inn.
/// </summary>
public sealed class HamperRunJob(Entity hatch) : Job
{
    public static Job? For(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        if (!sim.Round.Live || !Provisions.Runs(sim, pawn) || Players.Steered(sim, pawn))
            return null;
        // Already carrying one over: on to the inn with it.
        if (Provisions.Running(sim, pawn) != null && Provisions.IsHamper(sim, w.Get<Carrying>(pawn).Item))
            return UnpackHamperJob.For(sim, pawn);
        if (Provisions.ToRun(sim) is not { } order || order.Poster == pawn || !w.Get<Carrying>(pawn).Item.IsNull
            || Hatch.Of(sim, Provisions.Cookhouse(sim)) is not { IsNull: false } hatch
            || !Navigation.CanReach(sim, pawn, w.Get<Position>(pawn).Cell, w.Get<Thing>(hatch).Cell, PathEnd.Touch))
            return null;
        return new HamperRunJob(hatch);
    }

    protected override IEnumerable<JobStatus> Run()
    {
        Report = "Fetching the inn's hamper from the cookhouse";
        foreach (var s in GoToBeside(hatch)) yield return s;
        if (HasFailed) yield break;
        if (!Provisions.TakeToRun(Sim, Pawn))
        {
            Fail("someone's taken it");
            yield break;
        }
        var shelf = Provisions.UnpackAt(Sim);
        if (shelf.IsNull)
        {
            Fail("nowhere at the inn to unpack it");
            yield break;
        }
        Report = "Carrying the hamper over to the inn";
        foreach (var s in GoToBeside(shelf)) yield return s;
        if (HasFailed) yield break;
        if (!Alive(shelf) || !Provisions.Unpack(Sim, Pawn, shelf))
            Fail("couldn't unpack it");
    }
}

/// <summary>
/// An AI cook packs the inn's hamper the plain way, without the game (as <c>Healing.LayOnHands</c>
/// works the gift): rations tied from road food out of the larder while the inn's merchandise
/// shelf is short of them, and (where the inn's apart, for the lodgers' board) bowls ladled from
/// the pot or whatever's cooked about the kitchen; packed at the prep table as well as their skill
/// allows, and put up on the hatch for the innkeeper.
/// </summary>
public sealed class PackHamperJob(int order, Entity shop) : Job
{
    /// <summary>Ticks it takes an AI cook to pack and tie a hamper.</summary>
    public const int PackTicks = 300;

    /// <summary>At no skill, how often an AI cook ties the lid slack; a level takes a share off it.</summary>
    public const float SlackChance = 0.25f, SlackPerLevel = 0.02f;

    /// <summary>Bundles of rations an AI cook ties for one hamper.</summary>
    public const int RationsPerHamper = 2;

    /// <summary>Food that keeps on the road and goes in a bundle: fruit, greens, bread, cured meat (not a raw joint, flour or grain).</summary>
    public static bool RoadFood(ThingDef d) =>
        Rations.IsPortion(d) && !d.Roasts && !d.Grain && d.Name != Baking.Flour && d.Joints == null && d.Plucks == null && !d.IsSeasoner
        && (!d.Cooked || d.Name == Baking.Bread);

    /// <summary>The inn wants rations off this hamper.</summary>
    private static bool RationsWanted(Simulation sim) =>
        !Provisions.Merchandise(sim).IsNull && Provisions.RationsShelved(sim) < Provisions.RationsLow;

    /// <summary>The nearest road food sound to pack, or Entity.Null.</summary>
    private static Entity RoadSource(Simulation sim, Entity pawn) =>
        AiQueries.FindNearestItem(sim, pawn, (e, d) => RoadFood(d) && !FoodPoisoning.IsSpoiled(sim, e) && !sim.InKept(e), 80);

    public static Job? For(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        if (!Provisions.Cooks(sim, pawn) || Provisions.Wanted(sim, pawn) is not { } order || Provisions.HamperUp(sim)
            || Provisions.Cookhouse(sim) is not { IsNull: false } shop || Hatch.Of(sim, shop).IsNull || PrepTable(sim, shop).IsNull
            || Provisions.IsHamper(sim, w.Get<Carrying>(pawn).Item))
            return null;
        // Something to pack: dishes or rations on them already, food for the road the inn's short of, or
        // (apart) the pot cooked with a clean bowl to be had, or something cooked lying about.
        if (Provisions.DishesOn(sim, pawn).Count == 0 && (!RationsWanted(sim) || RoadSource(sim, pawn).IsNull)
            && (!Provisions.Split(sim) || Source(sim, pawn, shop).IsNull))
            return null;
        return new PackHamperJob(order.Id, shop);
    }

    /// <summary>Where the dishes come from: the shop's cooked pot (with a clean bowl to ladle into), else the nearest cooked food sound to serve.</summary>
    private static Entity Source(Simulation sim, Entity pawn, Entity shop)
    {
        if (ServeJob.Pot(sim, shop) is { IsNull: false } pot
            && (!Vessels.CleanOn(sim, pawn, Cooking.BowlOf(sim, pot)).IsNull || !ServeJob.CleanVessel(sim, pawn, Cooking.BowlOf(sim, pot)).IsNull))
            return pot;
        return AiQueries.FindNearestItem(sim, pawn, (e, d) => d.Cooked && d.Nutrition > 0 && !d.Drink && !FoodPoisoning.IsSpoiled(sim, e) && !sim.InKept(e), 80);
    }

    private static Entity PrepTable(Simulation sim, Entity shop) =>
        Buildings.Parts(sim, shop).FirstOrDefault(p => Rations.IsPrepTable(sim, p), Entity.Null);

    protected override IEnumerable<JobStatus> Run()
    {
        if (Sim.Shops.Find(order) is not { IsOpen: true })
        {
            Fail("the inn's done without");
            yield break;
        }
        // Rations first, while the inn's shelf is short: road food fetched, tied at the table.
        if (RationsWanted(Sim))
        {
            for (var trip = 0; trip < RationsPerHamper * Rations.Portions && Rations.PortionsOn(Sim, Pawn) < RationsPerHamper * Rations.Portions; trip++)
            {
                var food = RoadSource(Sim, Pawn);
                if (food.IsNull || !Reserve(food))
                    break;
                Report = "Fetching food for the road";
                foreach (var s in GoTo(Get<Thing>(food).Cell, PathEnd.Touch)) yield return s;
                if (HasFailed) yield break;
                if (!Sim.IsAvailable(food, Pawn))
                    break;
                var want = RationsPerHamper * Rations.Portions - Rations.PortionsOn(Sim, Pawn);
                var taken = Provisions.Portion(Sim, food, Math.Min(want, Get<Stack>(food).Count));
                if (Get<Carrying>(Pawn).Item.IsNull)
                    Get<Carrying>(Pawn).Item = taken;
                else
                    Inventory.Stow(Sim, Pawn, taken);
            }
            if (Rations.PortionsOn(Sim, Pawn) >= Rations.Portions && PrepTable(Sim, shop) is { IsNull: false } bench && Reserve(bench))
            {
                Report = "Tying rations for the road";
                foreach (var s in GoToBeside(bench)) yield return s;
                if (HasFailed) yield break;
                var tying = SkillRules.LevelOf(Sim, Pawn, Skill.Cooking);
                for (var n = 0; n < RationsPerHamper && Rations.PortionsOn(Sim, Pawn) >= Rations.Portions; n++)
                {
                    foreach (var s in Wait(PackTicks)) yield return s;
                    var handiwork = Math.Clamp(0.4f + 0.05f * tying + Sim.Rand.Range(-0.1f, 0.1f), 0.05f, 0.95f);
                    Rations.Make(Sim, Pawn, handiwork, Sim.Rand.NextFloat() < Math.Max(0f, SlackChance - SlackPerLevel * tying));
                    SkillRules.Learn(Sim, Pawn, Skill.Cooking, 10f);
                }
            }
        }
        // Gather what goes in it, up to a hamper's worth: dishes for the board, where the inn's apart.
        if (Provisions.Split(Sim) && Provisions.DishesOn(Sim, Pawn).Sum(d => Get<Stack>(d).Count) < Provisions.Holds)
        {
            var source = Source(Sim, Pawn, shop);
            if (Cooking.IsPot(Sim, source))
            {
                foreach (var s in LadleFrom(source)) yield return s;
                if (HasFailed) yield break;
            }
            else if (!source.IsNull && Reserve(source))
            {
                Report = "Fetching dishes for the inn's hamper";
                foreach (var s in GoTo(Get<Thing>(source).Cell, PathEnd.Touch)) yield return s;
                if (HasFailed) yield break;
                if (!Sim.IsAvailable(source, Pawn))
                {
                    Fail("the food's gone");
                    yield break;
                }
                var taken = Provisions.Portion(Sim, source, Math.Min(Provisions.Holds, Get<Stack>(source).Count));
                if (Get<Carrying>(Pawn).Item.IsNull)
                    Get<Carrying>(Pawn).Item = taken;
                else
                    Inventory.Stow(Sim, Pawn, taken);
            }
        }
        if (Provisions.DishesOn(Sim, Pawn).Count == 0)
        {
            Fail("nothing to pack");
            yield break;
        }
        var table = PrepTable(Sim, shop);
        if (table.IsNull || !Reserve(table))
        {
            Fail("no prep table");
            yield break;
        }
        Report = "Packing a hamper for the inn";
        foreach (var s in GoToBeside(table)) yield return s;
        if (HasFailed) yield break;
        foreach (var s in Wait(PackTicks)) yield return s;
        var level = SkillRules.LevelOf(Sim, Pawn, Skill.Cooking);
        SkillRules.Learn(Sim, Pawn, Skill.Cooking, 15f);
        var hamper = Provisions.MakeHamper(Sim, Pawn, Sim.Rand.NextFloat() < Math.Max(0f, SlackChance - SlackPerLevel * level));
        if (hamper.IsNull)
        {
            Fail("nothing to pack");
            yield break;
        }
        var hatch = Hatch.Of(Sim, shop);
        Report = "Putting the inn's hamper up on the hatch";
        if (Get<Carrying>(Pawn).Item != hamper)
        {
            // Set down beside the table (their hands were full): pick it back up.
            foreach (var s in GoTo(Get<Thing>(hamper).Cell, PathEnd.Touch)) yield return s;
            if (HasFailed || !Alive(hamper) || !Get<Thing>(hamper).Spawned) yield break;
            Sim.DespawnThing(hamper);
            Provisions.TakeInArms(Sim, Pawn, hamper);
        }
        foreach (var s in GoToBeside(hatch)) yield return s;
        if (HasFailed) yield break;
        if (!Alive(hamper) || Get<Carrying>(Pawn).Item != hamper || !Inventory.CanReachContainer(Sim, Pawn, hatch))
        {
            Fail("couldn't put it up");
            yield break;
        }
        Get<Carrying>(Pawn).Item = Entity.Null;
        var left = Sim.Store(hatch, hamper);
        if (!left.IsNull)
        {
            Get<Carrying>(Pawn).Item = left;
            Fail("the hatch is full");
            yield break;
        }
        Provisions.CallUp(Sim, Pawn);
    }

    /// <summary>Ladle bowls from the pot for the hamper: as many clean bowls as are to hand, up to a hamper's worth.</summary>
    private IEnumerable<JobStatus> LadleFrom(Entity pot)
    {
        if (!Reserve(pot))
        {
            Fail("someone's at the pot");
            yield break;
        }
        var bowlDef = Cooking.BowlOf(Sim, pot);
        if (Vessels.CleanOn(Sim, Pawn, bowlDef).IsNull)
        {
            var vessel = ServeJob.CleanVessel(Sim, Pawn, bowlDef);
            if (vessel.IsNull || !Reserve(vessel))
            {
                Fail($"no clean {bowlDef}");
                yield break;
            }
            Report = $"Fetching clean {Sim.Defs.Thing(bowlDef).Label}s";
            foreach (var s in GoTo(Get<Thing>(vessel).Cell, PathEnd.OnTile)) yield return s;
            if (HasFailed) yield break;
            if (!Sim.IsAvailable(vessel, Pawn))
            {
                Fail("they're gone");
                yield break;
            }
            Inventory.Stow(Sim, Pawn, Sim.TakeFromStack(vessel, Math.Min(Provisions.Holds, Get<Stack>(vessel).Count)));
        }
        Report = "Ladling bowls for the inn's hamper";
        foreach (var s in GoTo(Get<Thing>(pot).Cell, PathEnd.Touch)) yield return s;
        if (HasFailed) yield break;
        for (var n = 0; n < Provisions.Holds && !Vessels.CleanOn(Sim, Pawn, bowlDef).IsNull; n++)
        {
            foreach (var s in Wait(ServeJob.LadleTicks)) yield return s;
            if (Cooking.Ladle(Sim, Pawn, pot).IsNull)
                break;
        }
    }
}

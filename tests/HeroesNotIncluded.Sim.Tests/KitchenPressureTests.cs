using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Director;
using HeroesNotIncluded.Sim.Hospitality;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>Pressure in the kitchen: two pots, its fire fed by hand, the grease fire, and rats in the larder.</summary>
public class KitchenPressureTests
{
    // ---------------- The grease fire ----------------

    /// <summary>The kitchen test's pot, full of meat (fatty) or cabbage, left at a rolling boil.</summary>
    private static (Simulation Sim, Entity Me, Entity Pot) Boiling(string def = "Meat")
    {
        var (sim, me, pot) = InnKitchenTests.Kitchen();
        Cooking.Add(sim, pot, sim.CreateItem(sim.Thing(def), 8));
        var p = Cooking.PotOf(sim, pot);
        p.Heat = p.Flame = 1f;
        return (sim, me, pot);
    }

    private static void TillItFlares(Simulation sim, Entity pot)
    {
        for (var i = 0; i < 400 && !Cooking.Flaring(sim, pot); i++)
            sim.TickMany(SimClock.TicksPerSecond * 5);
        Assert.True(Cooking.Flaring(sim, pot), "a fatty pot at a rolling boil flares in the end");
    }

    [Fact]
    public void AFattyPotLeftAtABoilFlaresAndTheLidPutsItOut()
    {
        var (sim, me, pot) = Boiling();
        Assert.True(Cooking.Fatty(Cooking.PotOf(sim, pot)));
        TillItFlares(sim, pot);
        Assert.True(Perception.For(sim, me).Pot(pot)!.Value.Flaring);
        Assert.Equal("Clap the lid on!", AvatarSystem.ActionAt(sim, me, sim.World.Get<Thing>(pot).Cell).Label);
        sim.ApplyNow(new PotCommand(me, pot, PotStep.Next));
        Assert.False(Cooking.Flaring(sim, pot));
        Assert.Empty(sim.Fire.BurningTiles);
    }

    [Fact]
    public void AFlareLeftAloneSetsTheKitchenAlight()
    {
        var (sim, _, pot) = Boiling();
        TillItFlares(sim, pot);
        sim.TickMany(Cooking.FlareSteps + 20);
        Assert.False(Cooking.Flaring(sim, pot));
        Assert.NotEmpty(sim.Fire.BurningTiles);
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("kitchen with it"));
    }

    [Fact]
    public void APotOfGreensNeverFlares()
    {
        var (sim, _, pot) = Boiling("Cabbage");
        Assert.False(Cooking.Fatty(Cooking.PotOf(sim, pot)));
        for (var i = 0; i < 60; i++)
            sim.TickMany(SimClock.TicksPerSecond * 5);
        Assert.False(Cooking.Flaring(sim, pot));
    }

    [Fact]
    public void InTheStewTheLidGoesOnWithSpaceAndLeftItsAFire()
    {
        var (sim, me, pot) = Boiling();
        // The fat's caught as the cook comes to it: the session takes the flare up.
        Cooking.PotOf(sim, pot).FlareUntil = sim.Clock.Tick + Cooking.FlareSteps;
        WorkSystem.Start(sim, me, StewSession.For(sim, pot));
        var s = Assert.IsType<StewSession>(sim.World.Get<WorkSession>(me));
        Assert.True(s.Flaring > 0);
        s.Advance([new WorkInput(30, WorkInputKind.Act)], 32);
        Assert.Equal(0, s.Flaring);
        Assert.Equal(1, s.Lidded);
        WorkSystem.Stop(sim, me, null);
        Assert.False(Cooking.Flaring(sim, pot));

        // Again, and this time it's left.
        Cooking.PotOf(sim, pot).FlareUntil = sim.Clock.Tick + Cooking.FlareSteps;
        WorkSystem.Start(sim, me, StewSession.For(sim, pot));
        s = Assert.IsType<StewSession>(sim.World.Get<WorkSession>(me));
        s.Advance([], Cooking.FlareSteps + 2);
        Assert.True(s.Caught);
        Assert.True(s.Done);
        sim.Tick();
        Assert.NotEmpty(sim.Fire.BurningTiles);
    }

    [Fact]
    public void AFlareSurvivesASave()
    {
        var (sim, _, pot) = Boiling();
        TillItFlares(sim, pot);
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        Assert.Equal(Cooking.PotOf(sim, pot).FlareUntil, Cooking.PotOf(loaded, pot).FlareUntil);
    }

    // ---------------- The kitchen fire, fed by hand ----------------

    [Fact]
    public void APotsFireSetRoaringBurnsWoodFaster()
    {
        var (sim, _, pot) = InnKitchenTests.Kitchen();
        var hearth = sim.Map.Structure[sim.Map.Idx(C(11, 10))];
        Assert.Equal(1f, Cooking.FireDraw(sim, sim.World.Get<Thing>(hearth).Cell)); // an empty pot draws nothing extra
        Cooking.Add(sim, pot, sim.CreateItem(sim.Thing("Meat"), 4));
        var p = Cooking.PotOf(sim, pot);
        p.Flame = 1f;
        var roaring = Cooking.FireDraw(sim, sim.World.Get<Thing>(hearth).Cell);
        p.Flame = 0f;
        var banked = Cooking.FireDraw(sim, sim.World.Get<Thing>(hearth).Cell);
        Assert.True(roaring > 1.4f && banked < 0.6f, $"{roaring} {banked}");
    }

    [Fact]
    public void TheInnsKitchenHasTwoPotsBothOverAFireAndALogBasketStocked()
    {
        var (sim, me, _) = ExpeditionTests.Round("Cook");
        var inn = ShopSystem.BuildingOf(sim, sim.Defs.Building("Inn"));
        var parts = Buildings.Parts(sim, inn).ToList();
        var pots = parts.Where(e => Cooking.IsPot(sim, e)).ToList();
        Assert.Equal(2, pots.Count);
        Assert.All(pots, pot => Assert.True(Cooking.Lit(sim, pot)));
        var basket = parts.Single(e => sim.World.Get<Thing>(e).Def.Name == "LogBasket");
        Assert.Contains(sim.StoredIn(basket), e => sim.World.Get<Thing>(e).Def.Name == "Wood");

        // Only the kitchen's cook keeps its fires: not the keeper, not any villager.
        var kitchenFire = parts.First(e => sim.World.Get<Thing>(e).Def.Name == "Fireplace"
            && AiQueries.StaffOnly(sim, Environment.Warmth.FireCell(sim.World.Get<Thing>(e))));
        var keeper = sim.Creatures().Single(e => sim.World.TryGet<Trade>(e, out var t) && t.Def.Name == "Innkeeper");
        Assert.True(WorkGivers.FeedsHere(sim, me, kitchenFire, sim.World.Get<Thing>(kitchenFire)));
        Assert.False(WorkGivers.FeedsHere(sim, keeper, kitchenFire, sim.World.Get<Thing>(kitchenFire)));
        var hall = parts.First(e => sim.World.Get<Thing>(e).Def.Name == "Fireplace" && e != kitchenFire);
        Assert.True(WorkGivers.FeedsHere(sim, keeper, hall, sim.World.Get<Thing>(hall)));
    }

    // ---------------- Rats ----------------

    private static (Simulation Sim, Entity Me, Entity Larder, List<Entity> Rats) Ratty(string trade = "Cook")
    {
        var (sim, me, _) = ExpeditionTests.Round(trade);
        var inn = ShopSystem.BuildingOf(sim, sim.Defs.Building("Inn"));
        var larder = Buildings.Parts(sim, inn).Single(e => sim.World.Get<Thing>(e).Def.Name == "Larder");
        sim.Director.Enable(sim.Defs.Difficulty("Normal"));
        Assert.IsType<ShortageCrisis>(sim.Director.Send(sim, CrisisKind.Shortage));
        var rats = sim.World.Query<Vermin>().ToList();
        Assert.Equal(ShortageCrisis.Rats, rats.Count);
        return (sim, me, larder, rats);
    }

    private static int Food(Simulation sim, Entity larder) =>
        sim.StoredIn(larder).Where(e => sim.World.Get<Thing>(e).Def.Nutrition > 0).Sum(e => sim.World.Get<Stack>(e).Count);

    [Fact]
    public void RatsLeftAliveGnawTheLarder()
    {
        var (sim, _, larder, rats) = Ratty();
        foreach (var r in rats)
            sim.World.Get<Vermin>(r).NextGnaw = 0; // hungry now
        var before = Food(sim, larder);
        Assert.True(RatSystem.Gnaw(sim, larder));
        Assert.Equal(before - 1, Food(sim, larder));
    }

    [Fact]
    public void ADeadRatGnawsNothingAndTheRestRunWhenTheShortagePasses()
    {
        var (sim, _, larder, rats) = Ratty();
        NeedsSystem.Kill(sim, rats[0], ref sim.World.Get<Creature>(rats[0]), "was stabbed");
        Assert.True(sim.World.Get<Creature>(rats[0]).Dead);
        var inn = ShopSystem.BuildingOf(sim, sim.Defs.Building("Inn"));
        Assert.DoesNotContain(rats[0], RatSystem.In(sim, inn, sim.World.Get<Thing>(larder).Cell));
        // Met: the inn kept serving.
        var w = sim.World;
        var me = sim.World.Query<Avatar>().Single();
        for (var i = 0; i < ShortageCrisis.Served; i++)
        {
            var drinker = sim.Creatures().First(e => w.Get<Creature>(e) is { Faction: Faction.Village, Dead: false } && e != me);
            sim.Shops.Complete(sim, sim.Shops.Post(sim, drinker, sim.Defs.Service(ServiceKind.Drink), "An ale.", 0, sim.Clock.Tick + 1000, Entity.Null, Entity.Null, 0), me);
        }
        for (var i = 0; i < 20 && sim.Director.Active.Any(c => c.Kind == CrisisKind.Shortage); i++)
            sim.TickMany(SimClock.TicksPerHour / 12);
        Assert.DoesNotContain(sim.Director.Active, c => c.Kind == CrisisKind.Shortage);
        sim.TickMany(400);
        Assert.All(rats.Skip(1).Where(sim.World.IsAlive), r => Assert.True(sim.World.Has<Migrating>(r)));
    }

    [Fact]
    public void AnAiCookGoesAfterTheRats()
    {
        var (sim, _, _, _) = Ratty("Blacksmith");
        var cook = sim.Creatures().Single(e => sim.World.TryGet<Trade>(e, out var t) && t.Def.Name == "Cook");
        Assert.IsType<RatJob>(RatJob.For(sim, cook));
    }

    [Fact]
    public void ARatSurvivesASave()
    {
        var (sim, _, larder, rats) = Ratty();
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        Assert.Equal(larder, loaded.World.Get<Vermin>(rats[0]).Larder);
        Assert.Equal(sim.World.Get<Vermin>(rats[0]).NextGnaw, loaded.World.Get<Vermin>(rats[0]).NextGnaw);
    }
}

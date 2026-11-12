using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Systems;
using static SupportingCast.Sim.Tests.TestWorld;

namespace SupportingCast.Sim.Tests;

/// <summary>Spoiled food making people ill (<see cref="FoodPoisoning"/>), and what keeps it from happening.</summary>
public class FoodPoisoningTests
{
    private static Simulation Warm()
    {
        var sim = Create();
        Array.Fill(sim.Map.Temperature, 20f);
        return sim;
    }

    private static Entity Person(Simulation sim, int3 at, string name = "Ann")
    {
        var e = sim.SpawnCreature(sim.Defs.Creature("Human"), name, at);
        sim.SetSkills(e);
        return e;
    }

    /// <summary>How many of <paramref name="eaters"/> fall ill eating food at <paramref name="rot"/>, one meal each.</summary>
    private static int IllAfterOneMealEach(Simulation sim, string creature, float rot, int eaters)
    {
        var ill = 0;
        for (var i = 0; i < eaters; i++)
        {
            var e = sim.SpawnCreature(sim.Defs.Creature(creature), $"Eater{i}", C(1 + i % 30, 1 + i / 30), ageDays: 20);
            FoodPoisoning.AfterEating(sim, e, rot);
            if (FoodPoisoning.Severity(sim, e) > 0)
                ill++;
        }
        return ill;
    }

    [Fact]
    public void FreshFoodIsSafe()
    {
        var sim = Warm();
        Assert.Equal(0, IllAfterOneMealEach(sim, "Human", 0.45f, 200));
        Assert.Equal(0f, FoodPoisoning.Chance(sim.Defs.Creature("Human"), 0.3f));
    }

    [Fact]
    public void TheMoreItHasTurnedTheLikelierItSickens()
    {
        var sim = Warm();
        var mild = IllAfterOneMealEach(sim, "Human", 0.6f, 300);   // chance 0.2
        var bad = IllAfterOneMealEach(sim, "Human", 0.95f, 300);   // chance 0.9
        Assert.InRange(mild, 30, 95);
        Assert.InRange(bad, 240, 300);
    }

    [Fact]
    public void DragonsRarelyMindCarrion()
    {
        var sim = Warm();
        var ill = IllAfterOneMealEach(sim, "Dragon", 0.95f, 300); // 0.9 × 0.15
        Assert.InRange(ill, 15, 75);
    }

    [Fact]
    public void BeingIllSlowsWalkingAndWorkAndPasses()
    {
        var sim = Warm();
        var ann = Person(sim, C(5, 5));
        var food = sim.Defs.Need("Food");
        sim.World.Get<Needs>(ann).Values[food.Index] = 0.9f;
        var before = MovementSystem.WalkSpeedPerTick(sim, ann);
        sim.World.Add(ann, new FoodPoisoned { Until = sim.Clock.Tick + SimClock.HoursToTicks(10), Severity = 1f });
        Assert.Equal(before * (1 - FoodPoisoning.MoveLoss), MovementSystem.WalkSpeedPerTick(sim, ann), 4);
        Assert.Equal(1 - FoodPoisoning.WorkLoss, FoodPoisoning.WorkFactor(sim, ann), 4);
        Assert.True(MoodSystem.Holds(sim, ann, sim.Defs.Thought("FoodPoisoned")));
        Assert.NotNull(FoodPoisoning.Describe(sim, ann));

        sim.TickMany(SimClock.HoursToTicks(11));
        Assert.False(sim.World.Has<FoodPoisoned>(ann));
        Assert.True(sim.World.IsAlive(ann) && !sim.World.Get<Creature>(ann).Dead, "food poisoning never kills");
        Assert.Equal(1f, FoodPoisoning.WorkFactor(sim, ann));
    }

    [Fact]
    public void PeopleEatFreshFoodBeforeSpoiledAndSpoiledWhenThatsAllThereIs()
    {
        var sim = Warm();
        var ann = Person(sim, C(5, 5));
        var human = sim.Defs.Creature("Human");
        sim.SpawnItem(sim.Thing("Berries"), 5, C(6, 5), rot: 0.8f); // close but spoiled
        sim.SpawnItem(sim.Thing("Berries"), 5, C(15, 5), rot: 0.1f);
        var fresh = sim.Map.Item[sim.Map.Idx(C(15, 5))];
        var spoiled = sim.Map.Item[sim.Map.Idx(C(6, 5))];
        Assert.Equal(fresh, Assert.IsType<EatJob>(HumanBrain.EatSomething(sim, ann, human)).Food);

        sim.World.Destroy(fresh);
        Assert.Equal(spoiled, Assert.IsType<EatJob>(HumanBrain.EatSomething(sim, ann, human)).Food);
    }

    [Fact]
    public void EatingSpoiledFoodPassesItsRotOn()
    {
        var sim = Warm();
        var food = sim.Defs.Need("Food");
        var ill = 0;
        for (var i = 0; i < 40; i++)
        {
            var e = Person(sim, C(2 + i % 20, 2 + 2 * (i / 20)), $"P{i}");
            sim.World.Get<Needs>(e).Values[food.Index] = 0.2f;
            sim.SpawnItem(sim.Thing("Berries"), 10, sim.World.Get<Position>(e).Cell, rot: 0.98f);
            AiSystem.Order(sim, e, new EatJob(sim.Map.Item[sim.Map.Idx(sim.World.Get<Position>(e).Cell)]));
        }
        sim.TickMany(200);
        foreach (var e in sim.World.Query<Creature>())
            if (sim.World.Has<FoodPoisoned>(e))
                ill++;
        Assert.InRange(ill, 28, 40); // chance 0.96 each
    }

    [Fact]
    public void CookingHalvesTheRotSoAMealFromTurningMeatIsSafe()
    {
        var sim = Warm();
        // MakeProducts halves the ingredients' rot: meat at 0.9 makes meals at 0.45, under SpoiledAt.
        Assert.True(FoodPoisoning.Spoiled(0.9f));
        Assert.False(FoodPoisoning.Spoiled(0.9f * 0.5f));
        Assert.Equal(0f, FoodPoisoning.Chance(sim.Defs.Creature("Human"), 0.45f));
    }

    [Fact]
    public void FoodPoisoningSurvivesASave()
    {
        var sim = Warm();
        var ann = Person(sim, C(5, 5));
        sim.World.Add(ann, new FoodPoisoned { Until = sim.Clock.Tick + 5000, Severity = 0.7f });
        var (loaded, _) = Saving.SaveGame.Read(Saving.SaveGame.Write(sim), sim.Defs);
        var p = loaded.World.Get<FoodPoisoned>(ann);
        Assert.Equal(sim.Clock.Tick + 5000, p.Until);
        Assert.Equal(0.7f, p.Severity);
    }
}

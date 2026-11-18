using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Expeditions;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>Eating and sleeping as bonuses, never chores (<see cref="WellBeing"/>).</summary>
public class WellBeingTests
{
    private static Simulation Warm()
    {
        var sim = Create();
        sim.Climate.BaseTemperature = 20;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 20f);
        return sim;
    }

    private static Entity Person(Simulation sim)
    {
        var e = sim.SpawnCreature(sim.Defs.Creature("Human"), "Ana", C(5, 5), ageDays: 25 * 365);
        sim.SetSkills(e);
        return e;
    }

    [Fact]
    public void TheBetterTheDishTheBetterItFeeds()
    {
        var sim = Warm();
        float S(string def, Quality q = Quality.Plain, bool house = false, float rot = 0f) =>
            WellBeing.FedStrength(sim.Thing(def), new Meal(q, house), rot);

        Assert.Equal(0f, S("Meat")); // raw meat does you no good
        Assert.True(S("Berries") < S("Bread"));
        Assert.True(S("Bread") < S("Stew"));
        Assert.True(S("Stew", Quality.Crude) < S("Stew") && S("Stew") < S("Stew", Quality.Fine));
        Assert.True(S("Stew", Quality.Fine) < S("Stew", Quality.Fine, house: true));
        Assert.Equal("feasted", WellBeing.Tier(S("Stew", Quality.Masterwork, house: true)));
        Assert.Equal("fed", WellBeing.Tier(S("Bread")));
        Assert.True(S("Stew", rot: 0.4f) < S("Stew"));
        // Rot is hidden: a tooltip's guess (knowRot false) doesn't move for it.
        Assert.Equal(S("Stew"), WellBeing.FedStrength(sim.Thing("Stew"), Meal.Plain, 0.4f, knowRot: false));
    }

    [Fact]
    public void AMealSitsWellForFourHoursAndOnlyABetterOneTopsItUp()
    {
        var sim = Warm();
        var e = Person(sim);
        EatJob.Nourish(sim, e, sim.Thing("Stew"), 1, meal: new Meal(Quality.Fine, false));
        var fine = WellBeing.FedStrength(sim.Thing("Stew"), new Meal(Quality.Fine, false));
        Assert.Equal(fine, WellBeing.Fed(sim, e));
        var until = sim.World.Get<WellFed>(e).Until;

        sim.TickMany(60);
        WellBeing.Ate(sim, e, WellBeing.FedStrength(sim.Thing("Bread"), Meal.Plain));
        Assert.Equal(fine, WellBeing.Fed(sim, e));
        Assert.Equal(until, sim.World.Get<WellFed>(e).Until);

        WellBeing.Ate(sim, e, 0.95f);
        Assert.Equal(0.95f, WellBeing.Fed(sim, e));
        Assert.True(sim.World.Get<WellFed>(e).Until > until, "a better meal starts the hours afresh");

        sim.TickMany(SimClock.HoursToTicks(WellBeing.FedHours) + 1);
        Assert.Equal(0f, WellBeing.Fed(sim, e));
    }

    [Fact]
    public void GoingWithoutFoodOrSleepDoesNoHarm()
    {
        var sim = Warm();
        var e = Person(sim);
        var hp = sim.World.Get<Creature>(e).Hp;
        sim.World.Get<Mind>(e).NextThinkTick = long.MaxValue; // nobody goes to eat or to bed
        for (var i = 0; i < 48; i++)
        {
            Array.Fill(sim.World.Get<Needs>(e).Values, 0f);
            sim.TickMany(SimClock.TicksPerHour);
            sim.World.Get<Mind>(e).NextThinkTick = long.MaxValue;
        }
        Assert.False(sim.World.Get<Creature>(e).Dead);
        Assert.Equal(hp, sim.World.Get<Creature>(e).Hp);
        Assert.True(sim.World.TryGet<Mood>(e, out var mood));
        Assert.DoesNotContain(mood.Situational, t => t.Mood < 0);
    }

    [Fact]
    public void RestedAndFedAreQuickerAfootAndAtWork()
    {
        var sim = Warm();
        var e = Person(sim);
        var walk = MovementSystem.WalkSpeedPerTick(sim, e);
        Assert.Equal(1f, WellBeing.WorkFactor(sim, e));

        WellBeing.Slept(sim, e);
        Assert.True(MovementSystem.WalkSpeedPerTick(sim, e) > walk);
        var rested = WellBeing.WorkFactor(sim, e);
        Assert.True(rested > 1f);

        WellBeing.Ate(sim, e, 0.8f);
        Assert.True(WellBeing.WorkFactor(sim, e) > rested);
        Assert.True(WellBeing.WorkFactor(sim, e) < 1.3f, "a bonus, not a different game");
    }

    [Fact]
    public void FoodPoisoningTakesTheGoodOfAMealAndNothingStaysDown()
    {
        var sim = Warm();
        var e = Person(sim);
        WellBeing.Ate(sim, e, 0.7f);
        FoodPoisoning.Sicken(sim, e, 0.6f);
        Assert.Equal(0f, WellBeing.Fed(sim, e));
        WellBeing.Ate(sim, e, 0.7f);
        Assert.Equal(0f, WellBeing.Fed(sim, e));
    }

    [Fact]
    public void BothKeepThroughASave()
    {
        var sim = Warm();
        var e = Person(sim);
        WellBeing.Slept(sim, e);
        WellBeing.Ate(sim, e, 0.55f);
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        Assert.Equal(sim.World.Get<WellRested>(e).Until, loaded.World.Get<WellRested>(e).Until);
        Assert.Equal(0.55f, WellBeing.Fed(loaded, e));
    }

    [Fact]
    public void APartyThatLeavesWellFedAndRestedFightsBetter()
    {
        var defs = LoadDefs();
        MemberRecord Member(float fed, bool rested) => new()
        {
            Kind = defs.AllAdventurerKinds[0], PartyDef = defs.AllParties[0], Melee = 6, FedAtLeave = fed, RestedAtLeave = rested,
        };
        var plain = ExpeditionSystem.Strength(Member(0f, false));
        Assert.True(ExpeditionSystem.Strength(Member(0.9f, false)) > plain);
        Assert.True(ExpeditionSystem.Strength(Member(0.9f, true)) > ExpeditionSystem.Strength(Member(0.9f, false)));
        Assert.True(ExpeditionSystem.Strength(Member(0f, false)) >= plain, "going hungry is no penalty");
    }
}

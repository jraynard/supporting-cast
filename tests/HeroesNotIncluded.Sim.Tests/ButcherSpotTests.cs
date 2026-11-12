using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Systems;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>Hunters carrying their kills to a butcher spot (<see cref="ButcherSpots"/>) and cutting them up there.</summary>
public class ButcherSpotTests
{
    private static (Simulation Sim, Entity Hunter) Setup()
    {
        var sim = Create(60, 60);
        Array.Fill(sim.Map.Temperature, 15f);
        var hunter = sim.SpawnCreature(sim.Defs.Creature("Human"), "Aldric", C(10, 30));
        sim.SetSkills(hunter);
        return (sim, hunter);
    }

    private static Entity Carcass(Simulation sim, string kind, int3 at)
    {
        var e = sim.SpawnCreature(sim.Defs.Creature(kind), kind, at, Faction.Wild);
        NeedsSystem.Kill(sim, e, ref sim.World.Get<Creature>(e));
        return e;
    }

    private static IEnumerable<(int3 Cell, int Count)> Stacks(Simulation sim, string def) =>
        sim.World.Query<Thing>().Where(e => sim.World.Get<Thing>(e) is { Spawned: true } t && t.Def.Name == def)
            .Select(e => (sim.World.Get<Thing>(e).Cell, sim.World.Get<Stack>(e).Count));

    private static bool TickUntil(Simulation sim, Func<bool> done, int maxTicks)
    {
        for (var t = 0; t < maxTicks && !done(); t++)
            sim.Tick();
        return done();
    }

    [Fact]
    public void AHunterCarriesAKillToTheSpotAndButchersItThereWithLessWaste()
    {
        var (sim, hunter) = Setup();
        var spot = sim.SpawnStructure(sim.Thing("ButcherSpot"), C(12, 30));
        var deer = Carcass(sim, "Deer", C(40, 30));

        Assert.True(TickUntil(sim, () => sim.World.Has<Carried>(deer), SimClock.HoursToTicks(1)), "the hunter should pick the deer up");
        Assert.IsType<ButcherJob>(sim.World.Get<Mind>(hunter).Job);
        Assert.True(TickUntil(sim, () => !sim.World.IsAlive(deer), SimClock.HoursToTicks(2)), "the deer should be butchered");

        // Average skill takes the full yield; the spot adds a tenth. It all lands by the spot.
        var meat = Stacks(sim, "Meat").ToList();
        var yield = sim.Defs.Creature("Deer").Animal!.MeatYield;
        Assert.Equal((int)MathF.Round(yield * (ButcherJob.ButcherYield(sim, hunter) + 0.1f)), meat.Sum(m => m.Count));
        Assert.All(meat, m => Assert.InRange(Math.Abs(m.Cell.X - 12), 0, 3));
        Assert.Equal(spot, ButcherSpots.SpotAt(sim, meat[0].Cell));
    }

    [Fact]
    public void WithoutASpotTheCarcassIsCutUpWhereItLies()
    {
        var (sim, _) = Setup();
        var deer = Carcass(sim, "Deer", C(40, 30));
        Assert.True(TickUntil(sim, () => !sim.World.IsAlive(deer), SimClock.HoursToTicks(2)));
        Assert.All(Stacks(sim, "Meat"), m => Assert.InRange(m.Cell.X, 37, 43));
    }

    [Fact]
    public void ADeerIsHeavyAndAHareIsNot()
    {
        var (sim, hunter) = Setup();
        var deer = Carcass(sim, "Deer", C(20, 20));
        var hare = Carcass(sim, "Hare", C(22, 20));
        Assert.InRange(ButcherSpots.CarrySpeed(sim, hunter, deer), 0.6f, 0.8f);
        Assert.InRange(ButcherSpots.CarrySpeed(sim, hunter, hare), 0.98f, 1f);

        var free = MovementSystem.WalkSpeedPerTick(sim, hunter);
        sim.World.Add(deer, new Carried { By = hunter });
        Assert.Equal(free * ButcherSpots.CarrySpeed(sim, hunter, deer), MovementSystem.WalkSpeedPerTick(sim, hunter), 5);
    }

    [Fact]
    public void ALoadMidCarrySetsTheCarcassDownAndPlaysOnTheSame()
    {
        var (sim, hunter) = Setup();
        sim.SpawnStructure(sim.Thing("ButcherSpot"), C(12, 30));
        var deer = Carcass(sim, "Deer", C(40, 30));
        Assert.True(TickUntil(sim, () => sim.World.Has<Carried>(deer), SimClock.HoursToTicks(1)));
        sim.TickMany(120);

        var (loaded, _) = Saving.SaveGame.Read(Saving.SaveGame.Write(sim), sim.Defs);
        Assert.False(loaded.World.Has<Carried>(deer), "jobs aren't saved: the carcass is set down");
        AiSystem.InterruptAll(sim);
        Assert.Equal(sim.World.Get<Position>(deer).Cell, loaded.World.Get<Position>(deer).Cell);
        for (var i = 0; i < SimClock.HoursToTicks(1); i++)
        {
            sim.Tick();
            loaded.Tick();
        }
        Assert.Equal(sim.FullHash(), loaded.FullHash());
    }
}

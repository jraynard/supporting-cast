using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Core;
using SupportingCast.Sim.Scenarios;
using SupportingCast.Sim.Systems;
using static SupportingCast.Sim.Tests.TestWorld;

namespace SupportingCast.Sim.Tests;

/// <summary>Pairs of grown dragons courting and laying clutches (<see cref="BreedingSystem"/>).</summary>
public class BreedingTests
{
    private static (Simulation Sim, Entity She, Entity He) Pair(bool nest = true)
    {
        var sim = Create(40, 40);
        sim.Climate.BaseTemperature = 20;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 20f);
        var keeper = sim.SpawnCreature(sim.Defs.Creature("Human"), "Wren", C(10, 10), ageDays: 25 * 365);
        var she = Companions.SpawnBondedDragon(sim, keeper, "Ashwing", null, C(14, 10));
        var he = Dragons.Spawn(sim, sim.Defs.Creature("NorseDragon"), DragonElement.Ice, "Rime", C(16, 12), ageDays: 20);
        sim.World.Get<Dragon>(she).Sex = DragonSex.Female;
        sim.World.Get<Dragon>(he).Sex = DragonSex.Male;
        foreach (var d in new[] { she, he })
            sim.World.Get<Dragon>(d).Affinity = 0.6f;
        if (nest)
            sim.SpawnStructure(sim.Thing("Nest"), C(20, 20));
        return (sim, she, he);
    }

    /// <summary>Hours of the breeding system with both kept fed and content.</summary>
    private static void ContentHours(Simulation sim, int hours, params Entity[] dragons)
    {
        var breeding = sim.Systems.OfType<BreedingSystem>().Single();
        var food = sim.Defs.Need("Food");
        for (var i = 0; i < hours; i++)
        {
            foreach (var d in dragons)
            {
                sim.World.Get<Needs>(d).Values[food.Index] = 1f;
                sim.World.Get<Mood>(d).Value = 0.7f;
            }
            sim.Clock.Tick += SimClock.TicksPerHour;
            breeding.Update(sim);
        }
    }

    [Fact]
    public void EveryDragonHasASexAndKeepsItThroughASave()
    {
        var sim = Create();
        var sexes = Enumerable.Range(0, 40).Select(i => sim.World.Get<Dragon>(
            Dragons.Spawn(sim, null, DragonElement.Fire, $"D{i}", C(1 + i % 20, 1 + i / 20))).Sex).ToList();
        Assert.Contains(DragonSex.Male, sexes);
        Assert.Contains(DragonSex.Female, sexes);
        var (loaded, _) = Saving.SaveGame.Read(Saving.SaveGame.Write(sim), sim.Defs);
        var again = loaded.World.Query<Dragon>().Select(e => loaded.World.Get<Dragon>(e).Sex).ToList();
        Assert.Equal(sexes, again);
    }

    [Fact]
    public void AContentedPairWithANestCourtsAndSheLaysAClutchThere()
    {
        var (sim, she, he) = Pair();
        ContentHours(sim, 24 * 20, she, he);
        Assert.True(sim.World.Has<Gravid>(she), "a contented pair should court within a few weeks");
        Assert.False(sim.World.Has<Gravid>(he));
        Assert.Contains(sim.World.Get<Mood>(she).Memories, m => m.Def.Name == "CourtedMe" && m.Other == he);

        // Her time comes: she goes to the nest and lays.
        sim.World.Get<Gravid>(she).LayTick = sim.Clock.Tick;
        var food = sim.Defs.Need("Food");
        sim.World.Get<Needs>(she).Values[food.Index] = 1f;
        bool Laid() => !sim.World.Has<Gravid>(she);
        for (var t = 0; t < SimClock.HoursToTicks(2) && !Laid(); t++)
            sim.Tick();
        Assert.True(Laid(), "she should lay her clutch");
        var eggs = sim.World.Query<Egg>().ToList();
        Assert.InRange(eggs.Count, 1, 2);
        foreach (var egg in eggs)
        {
            var e = sim.World.Get<Egg>(egg);
            Assert.True(e.Keeper.IsNull, "a village egg, nobody's yet");
            Assert.Contains(e.Breed!.Name, new[] { "Dragon", "NorseDragon" });
            Assert.Contains(e.Element, new[] { DragonElement.Fire, DragonElement.Ice });
            Assert.InRange(Maps.Map.Distance(sim.LocationOf(egg), C(20, 20)), 0, 3);
        }
        Assert.True(sim.World.Has<LaidClutch>(she));

        // And not again for a while.
        ContentHours(sim, 24 * 10, she, he);
        Assert.False(sim.World.Has<Gravid>(she), "she rests between clutches");
    }

    [Fact]
    public void NoNestOrAnUnhappyPairMeansNoCourting()
    {
        var (sim, she, he) = Pair(nest: false);
        ContentHours(sim, 24 * 20, she, he);
        Assert.False(sim.World.Has<Gravid>(she));

        var (sad, she2, he2) = Pair();
        var breeding = sad.Systems.OfType<BreedingSystem>().Single();
        for (var i = 0; i < 24 * 20; i++)
        {
            sad.World.Get<Mood>(she2).Value = 0.4f;
            sad.Clock.Tick += SimClock.TicksPerHour;
            breeding.Update(sad);
        }
        Assert.False(sad.World.Has<Gravid>(she2));
        Assert.False(BreedingSystem.Contented(sad, she2));
        _ = he2;
    }

    [Fact]
    public void ClutchesMixTheirParentsBreedsAndElements()
    {
        var (sim, she, he) = Pair();
        var seen = new HashSet<(string, DragonElement)>();
        for (var k = 0; k < 12; k++)
        {
            BreedingSystem.Mate(sim, she, he);
            sim.Clock.Tick += SimClock.TicksPerHour * 7; // a different lay tick each time
            sim.World.Get<Gravid>(she).LayTick = sim.Clock.Tick;
            foreach (var egg in BreedingSystem.Lay(sim, she))
                seen.Add((sim.World.Get<Egg>(egg).Breed!.Name, sim.World.Get<Egg>(egg).Element));
        }
        Assert.True(seen.Count >= 3, string.Join(", ", seen));
    }

    [Fact]
    public void AVillagerCanKeepOneOfSeveralDragons()
    {
        var (sim, she, he) = Pair();
        var keeper = sim.World.Get<Dragon>(she).Keeper;
        var other = sim.SpawnCreature(sim.Defs.Creature("Human"), "Bea", C(12, 12), ageDays: 25 * 365);
        sim.ApplyNow(new MakeKeeperCommand(other, he));
        Assert.Equal(other, sim.World.Get<Dragon>(he).Keeper);
        Assert.Equal(keeper, sim.World.Get<Dragon>(she).Keeper); // left alone
        Assert.Equal(she, sim.World.Get<Keeper>(keeper).Dragon);
    }

    [Fact]
    public void ADragonKeepsItsSexOnAFlight()
    {
        var sim = Create(40, 40);
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), "Wren", C(10, 10), ageDays: 25 * 365);
        sim.World.Add(me, new Avatar());
        var dragon = Companions.SpawnBondedDragon(sim, me, "Ashwing", null, C(12, 10));
        var flipped = sim.World.Get<Dragon>(dragon).Sex == DragonSex.Male ? DragonSex.Female : DragonSex.Male;
        sim.World.Get<Dragon>(dragon).Sex = flipped; // not what its index would roll on the far side
        var party = Travel.Pack(sim, me, Entity.Null, Travel.FlightHours);
        var home = Simulation.CreateStandard(sim.Defs, 60, 60, 5);
        var arrived = Travel.Arrive(home, party);
        Assert.Equal(flipped, home.World.Get<Dragon>(Companions.DragonOf(home, arrived)).Sex);
    }
}

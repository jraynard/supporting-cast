using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Systems;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

public class PerceptionTests
{
    /// <summary>A flat map in a round, in full daylight, with one character at (10, 10).</summary>
    private static (Simulation Sim, Entity Me) Round(int depth = Simulation.DefaultDepth)
    {
        var sim = Create(60, 60, depth: depth);
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), "Me", C(10, 10), ageDays: 25 * 365);
        sim.Round.OpenLobby();
        sim.Round.Sit(Guid.Empty, "Me", null, null, null);
        sim.Round.Start(sim);
        sim.RebuildDerivedIfNeeded();
        Array.Fill(sim.Map.Light, 1f);
        return (sim, me);
    }

    private static Entity Person(Simulation sim, int x, int y) =>
        sim.SpawnCreature(sim.Defs.Creature("Human"), "Other", C(x, y), ageDays: 25 * 365);

    private static ThingDef Door(Simulation sim) => sim.Defs.AllThings.First(t => t.Building is { IsDoor: true, Fence: false, PartOnly: false });

    private static void WallAcross(Simulation sim, int x, string wall = "WoodWall")
    {
        for (var y = 0; y < 60; y++)
            sim.SpawnStructure(sim.Thing(wall), C(x, y));
        sim.RebuildDerivedIfNeeded();
    }

    [Fact]
    public void WithNoRoundEveryoneSeesEverything()
    {
        var sim = Create(60, 60);
        var a = sim.SpawnCreature(sim.Defs.Creature("Human"), "A", C(1, 1), ageDays: 9000);
        var b = sim.SpawnCreature(sim.Defs.Creature("Human"), "B", C(58, 58), ageDays: 9000);

        Assert.True(Perception.For(sim, a).Omniscient);
        Assert.True(Perception.For(sim, a).CanSee(b));
    }

    [Fact]
    public void NoViewerSeesEverythingEvenInARound()
    {
        var (sim, _) = Round();
        var far = Person(sim, 58, 58);
        Assert.True(Perception.For(sim, Entity.Null).CanSee(far));
    }

    [Fact]
    public void YouSeeWhoIsNearButNotWhoIsFar()
    {
        var (sim, me) = Round();
        var near = Person(sim, 15, 10);
        var far = Person(sim, 50, 10);

        var view = Perception.For(sim, me);
        Assert.True(view.CanSee(me));
        Assert.True(view.CanSee(near));
        Assert.False(view.CanSee(far));
    }

    [Fact]
    public void AWallHidesWhoIsBehindIt()
    {
        var (sim, me) = Round();
        var other = Person(sim, 15, 10);
        WallAcross(sim, 12);
        Assert.False(Perception.For(sim, me).CanSee(other));
    }

    [Fact]
    public void AFenceCanBeSeenOverButAPalisadeCannot()
    {
        var (sim, me) = Round();
        var other = Person(sim, 15, 10);
        WallAcross(sim, 12, "Fence");
        Assert.True(Perception.For(sim, me).CanSee(other));

        var (sim2, me2) = Round();
        var other2 = Person(sim2, 15, 10);
        WallAcross(sim2, 12, "Palisade");
        Assert.False(Perception.For(sim2, me2).CanSee(other2));
    }

    [Fact]
    public void AShutDoorHidesTheRoomUntilSomeoneStandsInIt()
    {
        var (sim, me) = Round();
        var other = Person(sim, 15, 10);
        for (var y = 0; y < 60; y++)
            sim.SpawnStructure(y == 10 ? Door(sim) : sim.Thing("WoodWall"), C(12, y));
        sim.RebuildDerivedIfNeeded();
        Assert.False(Perception.For(sim, me).CanSee(other));

        var inDoorway = Person(sim, 12, 10);
        var view = Perception.For(sim, me);
        Assert.True(view.CanSee(inDoorway));
        Assert.True(view.CanSee(other));
    }

    [Fact]
    public void TheDarkShortensSight()
    {
        var (sim, me) = Round();
        var other = Person(sim, 22, 10);
        Assert.True(Perception.For(sim, me).CanSee(other));

        Array.Fill(sim.Map.Light, 0.12f);
        Assert.False(Perception.For(sim, me).CanSee(other));
    }

    [Fact]
    public void SomeoneBesideYouIsSeenInTheDark()
    {
        var (sim, me) = Round();
        var other = Person(sim, 11, 10);
        Array.Fill(sim.Map.Light, 0f);
        Assert.True(Perception.For(sim, me).CanSee(other));
    }

    [Fact]
    public void AFloorHidesTheLevelBelow()
    {
        var (sim, me) = Round(depth: 4);
        var below = new int3(10, 10, 2);
        sim.World.Get<Position>(me).Cell = below + new int3(0, 0, 0);
        sim.World.Get<Position>(me).Pos = Position.CenterOf(below);
        var up = new int3(10, 10, 3);
        Assert.True(Perception.For(sim, me).CanSeeCell(up));

        sim.SpawnStructure(sim.Defs.AllThings.First(t => t.Building is { IsFloor: true, PartOnly: false }), up);
        sim.RebuildDerivedIfNeeded();
        Assert.False(Perception.For(sim, me).CanSeeCell(up));
    }

    [Fact]
    public void LooseItemsHideButBuildingsDoNot()
    {
        var (sim, me) = Round();
        sim.SpawnItem(sim.Thing("Wood"), 5, C(50, 10));
        var item = sim.Map.Item[sim.Map.Idx(C(50, 10))];
        var wall = sim.SpawnStructure(sim.Thing("WoodWall"), C(50, 12));
        var view = Perception.For(sim, me);
        Assert.False(view.CanSee(item));
        Assert.True(view.CanSee(wall));
    }

    [Fact]
    public void CreaturesListsOnlyThoseInSight()
    {
        var (sim, me) = Round();
        var near = Person(sim, 13, 10);
        Person(sim, 55, 55);
        var seen = Perception.For(sim, me).Creatures().ToList();
        Assert.Contains(me, seen);
        Assert.Contains(near, seen);
        Assert.Equal(2, seen.Count);
    }

    [Fact]
    public void DescribeTellsOnlyWhatShows()
    {
        var (sim, me) = Round();
        var near = Person(sim, 13, 10);
        var far = Person(sim, 55, 55);
        var view = Perception.For(sim, me);

        Assert.Null(view.Describe(far));
        var seen = view.Describe(near);
        Assert.NotNull(seen);
        Assert.Equal("Other", seen.Value.Name);

        sim.World.Get<Creature>(near).Downed = true;
        Assert.Equal("Collapsed.", view.Describe(near)!.Value.Condition);
    }

    [Fact]
    public void AViewNoticesWhenTheViewerMoves()
    {
        var (sim, me) = Round();
        var other = Person(sim, 40, 10);
        var view = Perception.For(sim, me);
        Assert.False(view.CanSee(other));

        var cell = C(35, 10);
        sim.World.Get<Position>(me).Cell = cell;
        sim.World.Get<Position>(me).Pos = Position.CenterOf(cell);
        Assert.True(view.CanSee(other));
    }

    [Fact]
    public void LookingChangesNothing()
    {
        var (sim, me) = Round();
        Person(sim, 13, 10);
        var before = sim.FullHash();
        var view = Perception.For(sim, me);
        _ = view.Creatures().ToList();
        Assert.Equal(before, sim.FullHash());
    }

    [Fact]
    public void BuildingTheFieldIsCheap()
    {
        var (sim, me) = Round(depth: 8);
        var view = Perception.For(sim, me);
        view.CanSeeCell(C(11, 10)); // warm up
        var clock = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < 20; i++)
        {
            var cell = C(10 + i, 10);
            sim.World.Get<Position>(me).Cell = cell;
            view.CanSeeCell(cell);
        }
        Assert.True(clock.ElapsedMilliseconds < 400, $"20 rebuilds took {clock.ElapsedMilliseconds} ms");
    }
}

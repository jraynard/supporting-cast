using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>Windows (an <c>O</c> in a plan): walls to everything but the daylight they let into their rooms.</summary>
public class WindowTests
{
    private static readonly int3[] Sides = [new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0)];

    private static (Simulation Sim, List<int3> Windows) Inn()
    {
        var (sim, _) = TestWorld.Millbrook();
        sim.RebuildDerivedIfNeeded();
        var inn = sim.World.Query<PresetBuilding>().Select(e => sim.World.Get<PresetBuilding>(e)).Single(b => b.Def.Name == "Inn");
        var windows = Enumerable.Range(0, inn.Def.Storeys)
            .SelectMany(level => Buildings.Plot(sim.Map, inn.Def, inn.Anchor, inn.Facing, level))
            .Where(p => p.Symbol == 'O').Select(p => p.Cell).ToList();
        return (sim, windows);
    }

    /// <summary>The indoor cells beside a window (its room's side).</summary>
    private static IEnumerable<int3> Inside(Simulation sim, int3 window) =>
        Sides.Select(d => window + d).Where(c => sim.Rooms.RoomAt(c) is { Indoors: true });

    [Fact]
    public void TheInnsWindowsAreWallsItsRoomsStayIndoors()
    {
        var (sim, windows) = Inn();
        Assert.True(windows.Count >= 10, $"the inn has {windows.Count} windows");
        foreach (var w in windows)
        {
            var s = sim.Map.Structure[sim.Map.Idx(w)];
            Assert.False(s.IsNull, $"a window at {w}");
            Assert.True(sim.World.Get<Thing>(s).Def.Building is { IsWall: true, Window: true }, $"{w} is a window");
            Assert.False(sim.Map.Passable(w), $"nobody walks through the window at {w}");
            Assert.NotEmpty(Inside(sim, w)); // it opens onto a room that's still indoors
        }
    }

    [Fact]
    public void ByDayAWindowLetsTheSkyIntoItsRoom()
    {
        var (sim, windows) = Inn();
        sim.Clock.Tick = (12 - sim.Clock.StartHour) * (long)SimClock.TicksPerHour; // noon
        sim.Lighting.Update(sim);
        var dim = sim.Lighting.Indoors(sim); // a roofed room's, with no window
        Assert.NotEmpty(sim.Lighting.Windows(sim));
        foreach (var w in windows)
        foreach (var c in Inside(sim, w))
            Assert.True(sim.Map.Light[sim.Map.Idx(c)] > dim + 0.1f, $"by the window at {w}, {c} is lit {sim.Map.Light[sim.Map.Idx(c)]:0.00} (dim {dim:0.00})");
    }

    [Fact]
    public void AWindowsDaylightStaysInItsRoom()
    {
        // Two rooms side by side, a window in the west one's north wall at (6, 4); the east room's
        // corner (9, 5) is within the window's reach, but through the wall between them at x = 8.
        var sim = TestWorld.Create(20, 20);
        var map = sim.Map;
        sim.BuildWallRing(new int3(4, 4, 1), new int3(12, 10, 1), "StoneWall");
        for (var y = 5; y <= 9; y++)
            sim.SpawnStructure(sim.Thing("StoneWall"), new int3(8, y, 1));
        var at = new int3(6, 4, 1);
        sim.DestroyThing(map.Structure[map.Idx(at)]);
        sim.SpawnStructure(sim.Thing("LeadedWindow"), at);
        sim.RebuildDerivedIfNeeded();
        sim.Clock.Tick = (12 - sim.Clock.StartHour) * (long)SimClock.TicksPerHour; // noon
        sim.Lighting.Update(sim);
        var dim = sim.Lighting.Indoors(sim);
        float At(int x, int y) => map.Light[map.Idx(x, y, 1)];
        Assert.True(sim.Rooms.RoomAt(new int3(6, 5, 1)) is { Indoors: true }, "the room with the window is indoors");
        Assert.True(At(6, 5) > dim + 0.1f, $"beside the window it's lit {At(6, 5):0.00} (dim {dim:0.00})");
        Assert.True(At(6, 7) > dim && At(6, 7) < At(6, 5), $"fading across the room: {At(6, 5):0.00}, then {At(6, 7):0.00}");
        Assert.Equal(dim, At(9, 5), 3); // next door, within reach but through a wall: none of it
    }
}

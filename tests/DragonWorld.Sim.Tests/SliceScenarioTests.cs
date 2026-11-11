using DragonWorld.Sim.Systems;
using DragonWorld.Sim.Ai;
using DragonWorld.Sim.Components;
using DragonWorld.Sim.Maps;
using DragonWorld.Sim.Scenarios;
using Xunit.Abstractions;

namespace DragonWorld.Sim.Tests;

/// <summary>
/// The vertical slice as an automated playthrough: the player only issues the orders a human
/// would (build a two-storey hall with a campfire, dig for stone, chop trees) and the village must
/// do the rest, through to a young dragon taking flight.
/// </summary>
public class SliceScenarioTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData(1234UL)]
    [InlineData(99UL)]
    public void VillageBuildsTheHallHatchesTheEggAndRaisesAFlyingDragon(ulong seed)
    {
        var sim = Simulation.CreateStandard(TestWorld.LoadDefs(), 160, 160, seed, MapGenerator.RecommendedDepth);
        var c = MapGenerator.Generate(sim);

        SliceDemo.ApplyVillageDefaults(sim, c);
        var hall = SliceDemo.ApplyPlayerOrders(sim, c);
        try
        {
            Play(sim, c, hall);
        }
        finally
        {
            foreach (var m in sim.Messages.Messages)
                output.WriteLine($"[{SimClock.TicksToDays(m.Tick):F2}d] {m.Kind}: {m.Text}");
            output.WriteLine($"Animals killed: {sim.Wildlife.Hunted}, fish caught: {sim.Wildlife.FishCaught}");
            foreach (var e in sim.Creatures().Where(e => sim.World.Has<Mood>(e)))
            {
                var mood = sim.World.Get<Mood>(e);
                var thoughts = mood.Situational.Select(t => t.Label).Concat(mood.Memories.Select(m => m.Def.Label));
                output.WriteLine($"Mood of {sim.World.Get<Creature>(e).Name}: {mood.Value:F2} ({string.Join(", ", thoughts)})");
                var opinions = sim.Creatures().Where(o => o != e && sim.World.Has<Mood>(o))
                    .Select(o => $"{sim.World.Get<Creature>(o).Name} {MoodRules.Opinion(sim, e, o):+0;-0;0}");
                output.WriteLine($"  thinks of: {string.Join(", ", opinions)}");
            }
            foreach (var ev in sim.Social.Recent)
                output.WriteLine($"[{SimClock.TicksToDays(ev.Tick):F2}d] {sim.World.Get<Creature>(ev.Initiator).Name} {ev.Def.Verb} {sim.World.Get<Creature>(ev.Recipient).Name}");
        }
    }

    private static void Play(Simulation sim, int3 center, SliceDemo.Hall hall)
    {
        var w = sim.World;

        // Day 1: the ground floor and fire are up and the egg is incubating somewhere warm.
        sim.TickMany(SimClock.DaysToTicks(1f));
        var room = sim.Rooms.RoomAt(hall.Campfire);
        Assert.NotNull(room);
        Assert.True(room.Indoors);
        Assert.True(sim.World.Has<Fueled>(sim.Map.Structure[sim.Map.Idx(hall.Campfire)]), "campfire should be built");
        var egg = Assert.Single(w.Query<Egg>());
        Assert.True(w.Get<Egg>(egg).Progress > 0.2f, "egg should have been incubating");
        Assert.True(w.Query<Apparel>().Any(e => sim.Worn(e).Any()), "someone should have put on the starting jerkin");

        // With the egg safe, the player orders the upper storey.
        SliceDemo.ApplyUpperStoreyOrders(sim, hall);

        // Day 3: the egg has hatched into a living dragon with a keeper, and the whole hall is up,
        // upstairs and all. (Parts may have burned since — hatchlings sneeze fire.)
        sim.TickMany(SimClock.DaysToTicks(2f));
        var dragon = Assert.Single(w.Query<Dragon>());
        Assert.False(w.Get<Creature>(dragon).Dead);
        Assert.True(w.IsAlive(w.Get<Dragon>(dragon).Keeper));
        var built = w.Query<Thing>().Where(e => w.Get<Thing>(e).Spawned && !w.Has<Construction>(e)).Select(e => w.Get<Thing>(e)).ToList();
        Assert.True(sim.Map.HasFloor[sim.Map.Idx(hall.UpperRoom)], "the upper floor should be built");
        Assert.Contains(built, t => t.Def.Name == "WoodStairs");
        Assert.Contains(built, t => t.Def.Name == "Bed" && t.Cell.Z == hall.UpperRoom.Z);
        Assert.True(sim.Regions.CanReach(hall.Campfire + new int3(0, 1, 0), hall.UpperRoom, PathEnd.OnTile), "stairs should lead upstairs");

        // Growing up takes days of the same routine; rather than play them all (minutes in a Debug
        // build), age the dragon to the eve of its juvenile stage and play on from there.
        var juvenileAge = w.Get<Creature>(dragon).Def.LifeStages.First(st => st.CanFly).MinAgeDays;
        w.Get<Creature>(dragon).BornTick = sim.Clock.Tick - SimClock.DaysToTicks(juvenileAge - 0.05f);

        // A little later: the dragon is a juvenile and has flown; it's fed and nobody died. Meanwhile
        // the player keeps marking the game that wanders near for the hunter.
        for (var hour = 0; hour < 18; hour++)
        {
            SliceDemo.HuntNearbyGame(sim, center);
            sim.TickMany(SimClock.HoursToTicks(1));
        }
        Assert.Equal("Juvenile", w.Get<Creature>(dragon).Stage(sim.Clock.Tick).Name);
        Assert.True(w.Get<Dragon>(dragon).Flights > 0, "the juvenile should have taken to the air");
        Assert.True(w.Get<Needs>(dragon).Get(sim.Defs.Need("Food")) > 0.15f, "keeper should keep the dragon fed");
        Assert.True(sim.Wildlife.Hunted + sim.Wildlife.FishCaught > 0, "the village should have hunted or fished");
        // A warm hall with beds, a hatched dragon and food: nobody should be miserable.
        foreach (var e in sim.Creatures().Where(e => w.Has<Mood>(e)))
            Assert.True(w.Get<Mood>(e).Value >= 0.25f, $"{w.Get<Creature>(e).Name} is miserable ({w.Get<Mood>(e).Value:F2})");
        foreach (var e in w.Query<Creature>())
        {
            if (w.Get<Creature>(e).Def.Race == Defs.Race.Animal)
                continue; // game, hunted or not
            Assert.False(w.Get<Creature>(e).Dead, $"{w.Get<Creature>(e).Name} died");
            var at = w.Get<Position>(e).Cell;
            Assert.True(Navigation.CanReach(sim, e, at, center, PathEnd.Touch), $"{w.Get<Creature>(e).Name} is stuck at {at}");
        }
    }
}

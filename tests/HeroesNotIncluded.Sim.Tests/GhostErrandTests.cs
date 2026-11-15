using HeroesNotIncluded.Sim.Afterlife;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>The dead's unfinished business (docs/DESIGN.md, "Death"; data/defs/afterlife.json).</summary>
public class GhostErrandTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    /// <summary>A round where Ana's died: her ghost and a villager who mourns her.</summary>
    private static (Simulation Sim, Entity Ghost, Entity Mourner) Mourned()
    {
        var (sim, _) = TestWorld.Millbrook(7, 4);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default, []));
        sim.ApplyNow(new StartRoundCommand());
        var w = sim.World;
        var ana = Players.CharacterOf(sim, Ana);
        ref var c = ref w.Get<Creature>(ana);
        NeedsSystem.Kill(sim, ana, ref c, "died");
        var ghost = Ghosts.Of(sim, Ana);
        var defs = sim.Defs.AllGhostErrands;
        var grief = defs.First(d => d.Kind == GhostErrandKind.Grieving);
        var mourner = sim.Creatures().First(e => Errands.Qualifies(sim, grief, e));
        return (sim, ghost, mourner);
    }

    /// <summary>Keep the ghost by someone as they go about, for a while.</summary>
    private static void Linger(Simulation sim, Entity ghost, Entity who, int ticks)
    {
        for (var t = 0; t < ticks; t += 10)
        {
            sim.World.Get<Position>(ghost) = sim.World.Get<Position>(who);
            sim.TickMany(10);
        }
    }

    [Fact]
    public void ADeathLeavesMournersTheDeadCanSense()
    {
        var (sim, ghost, mourner) = Mourned();
        sim.World.Get<Position>(ghost) = sim.World.Get<Position>(mourner);

        var business = Perception.For(sim, ghost).Business().ToList();

        Assert.Contains(business, b => b.Who == mourner && b.Errand.Kind == GhostErrandKind.Grieving);
        Assert.Empty(Perception.For(sim, mourner).Business()); // the living sense nothing of it
    }

    [Fact]
    public void LingeringByAMournerLiftsTheirGriefForALittleRenown()
    {
        var (sim, ghost, mourner) = Mourned();
        var w = sim.World;
        var grief = sim.Defs.AllGhostErrands.First(d => d.Kind == GhostErrandKind.Grieving);
        var renown = sim.Reckoning.Renown;

        for (var t = 0; t < grief.Ticks + SimClock.TicksPerSecond * 4; t += 10)
        {
            w.Get<Position>(ghost) = w.Get<Position>(mourner); // keep by them as they go about
            sim.TickMany(10);
        }

        Assert.False(Errands.Qualifies(sim, grief, mourner));
        Assert.True(sim.Reckoning.Renown >= renown + grief.Renown - 0.01f);
        Assert.Equal(1, w.Get<Ghost>(ghost).Done);
        Assert.Equal(1, w.Get<Unfinished>(w.Get<Ghost>(ghost).Body).Done);
    }

    [Fact]
    public void WanderingOffLosesTheLingering()
    {
        var (sim, ghost, mourner) = Mourned();
        var w = sim.World;
        Linger(sim, ghost, mourner, SimClock.TicksPerSecond * 3);
        Assert.Equal(mourner, w.Get<Ghost>(ghost).ErrandTarget);

        var grief = sim.Defs.AllGhostErrands.First(d => d.Kind == GhostErrandKind.Grieving);
        w.Get<Position>(ghost) = Position.At(new int3(1, 1, w.Get<Position>(mourner).Cell.Z));
        sim.TickMany(grief.GraceTicks + SimClock.TicksPerSecond * 2); // longer than they may be out of reach

        Assert.Equal(0, w.Get<Ghost>(ghost).ErrandTicks);
        Assert.True(w.Get<Ghost>(ghost).ErrandTarget.IsNull);
    }

    [Fact]
    public void SteppingOutBrieflyOnlyPausesTheLingering()
    {
        var (sim, ghost, mourner) = Mourned();
        var w = sim.World;
        Linger(sim, ghost, mourner, SimClock.TicksPerSecond * 3);
        var before = w.Get<Ghost>(ghost).ErrandTicks;
        Assert.True(before > 0);

        // Out of reach but still in sight of them, a little while.
        var away = w.Get<Position>(mourner).Pos;
        var ax = away.X + 8f < sim.Map.Width - 1 ? away.X + 8f : away.X - 8f;
        w.Get<Position>(ghost) = new Position { Pos = new float3(ax, away.Y, away.Z), Cell = new int3((int)ax, (int)away.Y, (int)away.Z) };
        sim.TickMany(SimClock.TicksPerSecond * 2); // within the grace

        var g = w.Get<Ghost>(ghost);
        Assert.Equal(mourner, g.ErrandTarget);
        Assert.Equal(before, g.ErrandTicks); // paused, not lost
        Assert.True(g.ErrandAwayTicks > 0);
        Assert.Contains(Perception.For(sim, ghost).Business(), b => b.Who == mourner && b.Paused);

        Linger(sim, ghost, mourner, SimClock.TicksPerSecond * 2);
        Assert.True(w.Get<Ghost>(ghost).ErrandTicks > before); // and back by them, it goes on
        Assert.Equal(0, w.Get<Ghost>(ghost).ErrandAwayTicks);
    }

    [Fact]
    public void LingeringWorksFromAcrossTheRing()
    {
        var (sim, ghost, mourner) = Mourned();
        var w = sim.World;
        var grief = sim.Defs.AllGhostErrands.First(d => d.Kind == GhostErrandKind.Grieving);
        Assert.True(grief.Reach >= 4.5f);

        for (var t = 0; t < grief.Ticks + SimClock.TicksPerSecond * 4 && w.Get<Ghost>(ghost).Done == 0; t += 10)
        {
            // Four cells off them, not at their side: no chasing.
            var at = w.Get<Position>(mourner).Pos;
            var x = at.X + 4f < sim.Map.Width - 1 ? at.X + 4f : at.X - 4f;
            w.Get<Position>(ghost) = new Position { Pos = new float3(x, at.Y, at.Z), Cell = new int3((int)x, (int)at.Y, (int)at.Z) };
            sim.TickMany(10);
        }

        Assert.Equal(1, w.Get<Ghost>(ghost).Done);
    }

    [Fact]
    public void ALostTravellerByAGhostSuddenlyKnowsTheWay()
    {
        var (sim, ghost, _) = Mourned();
        var w = sim.World;
        var stranger = sim.Creatures().First(e => w.Get<Creature>(e) is { Faction: Faction.Village, Def.Race: Race.Human } && !w.Has<PlayerCharacter>(e));
        w.Get<Mood>(stranger).Memories.Clear(); // not grieving: only lost
        var smithy = sim.Defs.AllBuildings.First(b => b.Name == "Smithy");
        sim.Directions.Ask(sim, stranger, smithy, "a mend");
        Assert.Contains(sim.Directions.Open, a => a.Asker == stranger);
        var lost = sim.Defs.AllGhostErrands.First(d => d.Kind == GhostErrandKind.Lost);
        w.Get<Ghost>(ghost).RestUntil = 0;

        for (var t = 0; t < lost.Ticks + SimClock.TicksPerSecond * 3 && sim.Directions.Open.Any(a => a.Asker == stranger); t += 10)
        {
            w.Get<Position>(ghost) = w.Get<Position>(stranger);
            sim.TickMany(10);
        }

        Assert.DoesNotContain(sim.Directions.Open, a => a.Asker == stranger);
    }

    [Fact]
    public void ErrandProgressSurvivesASave()
    {
        var (sim, ghost, mourner) = Mourned();
        Linger(sim, ghost, mourner, SimClock.TicksPerSecond * 3);

        var (loaded, _) = SaveGame.Read(SaveGame.Snapshot(sim), sim.Defs);

        var g = loaded.World.Get<Ghost>(Ghosts.Of(loaded, Ana));
        Assert.Equal(mourner, g.ErrandTarget);
        Assert.True(g.ErrandTicks > 0);
    }
}

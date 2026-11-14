using HeroesNotIncluded.Sim.Afterlife;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>The signs a ghost leaves, and who perceives them (docs/DESIGN.md, "Death").</summary>
public class HauntingTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Bea = new("00000000-0000-0000-0000-00000000000b");

    /// <summary>A round where Ana's dead; her ghost floats right by Bea.</summary>
    private static (Simulation Sim, Entity Ghost, Entity Bea) Haunted()
    {
        var (sim, _) = TestWorld.Millbrook(7, 4);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default, []));
        sim.ApplyNow(new JoinLobbyCommand(Bea, "Bea", default, []));
        sim.ApplyNow(new StartRoundCommand());
        var ana = Players.CharacterOf(sim, Ana);
        ref var c = ref sim.World.Get<Creature>(ana);
        NeedsSystem.Kill(sim, ana, ref c, "died");
        var ghost = Ghosts.Of(sim, Ana);
        var bea = Players.CharacterOf(sim, Bea);
        sim.World.Get<Position>(ghost) = sim.World.Get<Position>(bea);
        return (sim, ghost, bea);
    }

    [Fact]
    public void AChillSpendsPresenceAndIsSeenButNeverSaysWhose()
    {
        var (sim, ghost, bea) = Haunted();
        var before = sim.World.Get<Ghost>(ghost).Presence;

        sim.ApplyNow(new HauntCommand(ghost, HauntKind.Chill));

        Assert.Equal(before - Hauntings.Cost(HauntKind.Chill), sim.World.Get<Ghost>(ghost).Presence, 3);
        var sign = Assert.Single(Perception.For(sim, bea).Signs());
        Assert.Equal(HauntKind.Chill, sign.Kind);
        Assert.DoesNotContain("Ana", sign.Words);
    }

    [Fact]
    public void WithoutPresenceAGhostCantHauntUntilItGathers()
    {
        var (sim, ghost, _) = Haunted();
        sim.World.Get<Ghost>(ghost).Presence = 0f;

        sim.ApplyNow(new HauntCommand(ghost, HauntKind.Knock));
        Assert.Equal(0, sim.World.Count<Haunting>());

        sim.TickMany(SimClock.TicksPerHour);
        Assert.Equal(Hauntings.PresencePerHour, sim.World.Get<Ghost>(ghost).Presence, 2);
        sim.ApplyNow(new HauntCommand(ghost, HauntKind.Knock));
        Assert.Equal(1, sim.World.Count<Haunting>());
    }

    [Fact]
    public void AKnockIsHeardNearbyAndNotFarOff()
    {
        var (sim, ghost, bea) = Haunted();
        var w = sim.World;
        var far = sim.Creatures().First(e => w.Get<Creature>(e).Def.Race == Defs.Race.Human && e != bea
            && Distance(w.Get<Position>(e).Cell, w.Get<Position>(bea).Cell) > Speech.SpeechSystem.SayRange * 3);

        sim.ApplyNow(new HauntCommand(ghost, HauntKind.Knock));

        Assert.Contains(Perception.For(sim, bea).Signs(), s => s.Kind == HauntKind.Knock);
        Assert.DoesNotContain(Perception.For(sim, far).Signs(), s => s.Kind == HauntKind.Knock);
    }

    [Fact]
    public void AGutteredLampIsReallyDarkAWhile()
    {
        var (sim, ghost, bea) = Haunted();
        var w = sim.World;
        var lamp = Buildings.LitLamps(sim).First().Cell;
        w.Get<Position>(ghost) = Position.At(lamp);
        w.Get<Position>(bea) = Position.At(lamp);

        sim.ApplyNow(new HauntCommand(ghost, HauntKind.Gutter));

        Assert.True(Hauntings.Guttered(sim, lamp));
        Assert.True(Perception.For(sim, bea).Dark(lamp));
        sim.TickMany(Hauntings.Lasts(HauntKind.Gutter) + SimClock.TicksPerSecond * 2);
        Assert.False(Hauntings.Guttered(sim, lamp)); // and the sign's faded
        Assert.Equal(0, w.Count<Haunting>());
    }

    [Fact]
    public void AWeakFireGuttersOut()
    {
        var (sim, ghost, _) = Haunted();
        var w = sim.World;
        var fire = w.Query<Fueled>().First(e => w.Get<Thing>(e) is { Spawned: true, Def.Building.Fuel: not null });
        var props = w.Get<Thing>(fire).Def.Building!.Fuel!;
        w.Get<Fueled>(fire).Fuel = props.Capacity * 0.1f;
        Assert.True(sim.Burning(fire));
        w.Get<Position>(ghost) = Position.At(Environment.Warmth.FireCell(w.Get<Thing>(fire)));
        Assert.True(Hauntings.GutterTarget(sim, w.Get<Position>(ghost).Cell) is { Fire.IsNull: false }); // the fire, not a lamp

        sim.ApplyNow(new HauntCommand(ghost, HauntKind.Gutter));

        Assert.False(sim.Burning(fire));
    }

    [Fact]
    public void TheSignsAndPresenceSurviveASave()
    {
        var (sim, ghost, _) = Haunted();
        sim.ApplyNow(new HauntCommand(ghost, HauntKind.Chill));
        var presence = sim.World.Get<Ghost>(ghost).Presence;

        var (loaded, _) = SaveGame.Read(SaveGame.Snapshot(sim), sim.Defs);

        Assert.Equal(1, loaded.World.Count<Haunting>());
        Assert.Equal(presence, loaded.World.Get<Ghost>(Ghosts.Of(loaded, Ana)).Presence, 4);
    }

    private static float Distance(int3 a, int3 b) =>
        MathF.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y) + (a.Z - b.Z) * (a.Z - b.Z));
}

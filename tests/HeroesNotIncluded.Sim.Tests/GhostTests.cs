using HeroesNotIncluded.Sim.Afterlife;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>Death sticks in a round: a player who dies is a ghost (docs/DESIGN.md, "Death").</summary>
public class GhostTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Bea = new("00000000-0000-0000-0000-00000000000b");

    private static Simulation Round(params Guid[] players)
    {
        var (sim, _) = TestWorld.Millbrook(7, 4);
        sim.Round.OpenLobby();
        var i = 0;
        foreach (var p in players)
            sim.ApplyNow(new JoinLobbyCommand(p, $"P{i++}", default, []));
        sim.ApplyNow(new StartRoundCommand());
        return sim;
    }

    private static void Kill(Simulation sim, Entity e, string cause = "bled to death")
    {
        ref var c = ref sim.World.Get<Creature>(e);
        NeedsSystem.Kill(sim, e, ref c, cause);
    }

    [Fact]
    public void APlayerWhoDiesIsAGhostAndCantTakeAnotherTrade()
    {
        var sim = Round(Ana);
        var w = sim.World;
        var ana = Players.CharacterOf(sim, Ana);

        Kill(sim, ana);

        var ghost = Ghosts.Of(sim, Ana);
        Assert.False(ghost.IsNull);
        Assert.Equal(ana, w.Get<Ghost>(ghost).Body);
        Assert.False(w.Has<Avatar>(ana));
        Assert.True(w.Has<PlayerCharacter>(ana)); // still theirs, for the reveal
        Assert.Equal("bled to death", w.Get<Death>(ana).Cause);
        Assert.Equal(w.Get<Position>(ana).Cell, w.Get<Position>(ghost).Cell);

        sim.ApplyNow(new TakeTradeCommand(Ana, [], []));
        Assert.True(Players.CharacterOf(sim, Ana).IsNull);
    }

    [Fact]
    public void AVillagerTheAiRanLeavesNoGhost()
    {
        var sim = Round(Ana);
        var w = sim.World;
        var villager = sim.Creatures().First(e => w.Get<Creature>(e) is { Faction: Faction.Village, Def.Race: Race.Human } && !w.Has<PlayerCharacter>(e));

        Kill(sim, villager);

        Assert.True(w.Has<Death>(villager));
        Assert.False(Ghosts.Any(sim));
    }

    [Fact]
    public void OutsideARoundTheDeadKeepTheirAvatar()
    {
        // The practice ring and the demos freshen their dead: no ghost there.
        var (sim, _) = TestWorld.Millbrook(7, 1);
        var me = sim.Creatures().First(e => sim.World.Get<Creature>(e).Def.Race == Race.Human);
        sim.World.Add(me, new Avatar());

        Kill(sim, me);

        Assert.True(sim.World.Has<Avatar>(me));
        Assert.False(sim.World.Has<Death>(me));
        Assert.False(Ghosts.Any(sim));
    }

    [Fact]
    public void AGhostDriftsOnlySoFarAtATimeAndCanGoBackToItsBody()
    {
        var sim = Round(Ana);
        var ana = Players.CharacterOf(sim, Ana);
        Kill(sim, ana);
        var ghost = Ghosts.Of(sim, Ana);
        var start = sim.World.Get<Position>(ghost).Pos;

        sim.ApplyNow(new GhostMoveCommand(ghost, start.X + 50, start.Y, (int)start.Z));
        var moved = sim.World.Get<Position>(ghost).Pos;
        Assert.Equal(start.X + Ghosts.MaxStep, moved.X, 3);

        sim.ApplyNow(new GhostMoveCommand(ghost, float.NaN, float.NaN, 0, ToBody: true));
        Assert.Equal(sim.World.Get<Position>(ana).Cell, sim.World.Get<Position>(ghost).Cell);
    }

    [Fact]
    public void OnlyItsPlayerMovesAGhost()
    {
        var sim = Round(Ana, Bea);
        Kill(sim, Players.CharacterOf(sim, Ana));
        var ghost = Ghosts.Of(sim, Ana);
        var move = new GhostMoveCommand(ghost, 1, 1, 1);

        Assert.Null(CommandRules.Refusal(sim, move, Ana, isHost: false));
        Assert.NotNull(CommandRules.Refusal(sim, move, Bea, isHost: true));
    }

    [Fact]
    public void AGhostSeesAllAroundItInTheDarkButNoFurther()
    {
        var sim = Round(Ana);
        Kill(sim, Players.CharacterOf(sim, Ana));
        var ghost = Ghosts.Of(sim, Ana);
        var here = sim.World.Get<Position>(ghost).Cell;
        Array.Clear(sim.Map.Light); // pitch dark: it doesn't matter to the dead
        var view = Perception.For(sim, ghost);
        Assert.True(view.Ghostly);

        var near = Open(sim, here, 4, 8);
        var far = Open(sim, here, Ghosts.SightRadius + 3, Ghosts.SightRadius + 10);
        Assert.True(view.CanSeeCell(near));
        Assert.False(view.CanSeeCell(far));
    }

    [Fact]
    public void TheDeadHearTheLivingAndEachOtherButTheLivingNeverHearTheDead()
    {
        var sim = Round(Ana, Bea);
        var w = sim.World;
        var ana = Players.CharacterOf(sim, Ana);
        var bea = Players.CharacterOf(sim, Bea);
        Kill(sim, ana);
        var ghost = Ghosts.Of(sim, Ana);
        // Float right beside Bea.
        w.Get<Position>(ghost) = w.Get<Position>(bea);

        sim.ApplyNow(new SayCommand(bea, "Is anyone there?", Volume.Say));
        sim.ApplyNow(new SayCommand(ghost, "Right behind you.", Volume.Shout));

        var living = sim.Speech.Kept[^2];
        Assert.Contains(living.Ghosts, h => h.Listener == ghost);
        Assert.DoesNotContain(living.Listeners, h => h.Listener == ghost);

        var dead = sim.Speech.Kept[^1];
        Assert.Equal(Volume.Ghost, dead.Volume);
        Assert.Empty(dead.Listeners);
        Assert.Contains(dead.Ghosts, h => h.Listener == ghost);

        Assert.DoesNotContain(Perception.For(sim, bea).Heard(), l => l.Id == dead.Id);
        var heard = Perception.For(sim, ghost).Heard().Select(l => l.Id).ToList();
        Assert.Contains(living.Id, heard);
        Assert.Contains(dead.Id, heard);
    }

    [Fact]
    public void TheLivingCantSpeakAsTheDead()
    {
        var sim = Round(Ana);
        var ana = Players.CharacterOf(sim, Ana);

        Assert.Null(sim.Speech.Say(sim, ana, "boo", Volume.Ghost));
    }

    [Fact]
    public void SomeoneCollapsedCanStillCallForHelpButNotFar()
    {
        var sim = Round(Ana);
        var w = sim.World;
        var ana = Players.CharacterOf(sim, Ana);
        ref var c = ref w.Get<Creature>(ana);
        HealthSystem.SetDowned(sim, ana, ref c, true);

        var said = sim.Speech.Say(sim, ana, "Help!", Volume.Shout);

        Assert.NotNull(said);
        Assert.False(SpeechSystem.CanAct(sim, ana));
        Assert.True(SpeechSystem.CanSpeak(sim, ana));
    }

    [Fact]
    public void WhenNoPlayerLivesTheSeasonRunsOnFast()
    {
        var sim = Round(Ana);
        Assert.False(Players.AllAsleep(sim));

        Kill(sim, Players.CharacterOf(sim, Ana));

        Assert.True(Players.AllAsleep(sim));
    }

    [Fact]
    public void AGhostAndWhatItHeardSurviveASave()
    {
        var sim = Round(Ana, Bea);
        var ana = Players.CharacterOf(sim, Ana);
        Kill(sim, ana);
        sim.ApplyNow(new SayCommand(Ghosts.Of(sim, Ana), "Still here.", Volume.Say));

        var (loaded, _) = SaveGame.Read(SaveGame.Snapshot(sim), sim.Defs);

        var ghost = Ghosts.Of(loaded, Ana);
        Assert.False(ghost.IsNull);
        Assert.Equal(ana, loaded.World.Get<Ghost>(ghost).Body);
        Assert.True(loaded.World.Has<Death>(ana));
        Assert.Contains(Perception.For(loaded, ghost).Heard(), l => l.Text == "Still here.");
    }

    /// <summary>An open (not solid) cell on the same level between two distances from <paramref name="here"/>.</summary>
    private static int3 Open(Simulation sim, int3 here, int min, int max)
    {
        var map = sim.Map;
        for (var d = min; d <= max; d++)
            foreach (var (dx, dy) in new[] { (d, 0), (-d, 0), (0, d), (0, -d) })
            {
                var c = new int3(here.X + dx, here.Y + dy, here.Z);
                if (map.InBounds(c) && !map.IsSolid(map.Idx(c)))
                    return c;
            }
        throw new InvalidOperationException("no open cell");
    }
}

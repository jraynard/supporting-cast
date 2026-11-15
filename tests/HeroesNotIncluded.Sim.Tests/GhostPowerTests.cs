using HeroesNotIncluded.Sim.Afterlife;
using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Hospitality;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// What the dead can do beyond errands (docs/DESIGN.md, "Death"): startle and beckon beasts, get
/// into one, write in frost, whisper into dreams, and shake raiders' nerve.
/// </summary>
public class GhostPowerTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    /// <summary>A round where Ana's died, her ghost at full presence. Friendly fire is off, as by default.</summary>
    private static (Simulation Sim, Entity Ghost) Dead()
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
        w.Get<Ghost>(ghost).Presence = 1f;
        Assert.False(sim.Round.FriendlyFire);
        return (sim, ghost);
    }

    /// <summary>Open ground on the ghost's level, as far from anyone as the map's corners allow.</summary>
    private static int3 Quiet(Simulation sim)
    {
        var w = sim.World;
        var z = w.Get<Position>(sim.Creatures().First()).Cell.Z;
        int3? best = null;
        var bestD = -1;
        foreach (var (x, y) in new[] { (6, 6), (sim.Map.Width - 7, 6), (6, sim.Map.Height - 7), (sim.Map.Width - 7, sim.Map.Height - 7) })
        {
            if (sim.Map.NearestStandable(x, y, z) is not { } c)
                continue;
            var d = sim.Creatures().Min(e => Maps.Map.Distance(w.Get<Position>(e).Cell, c));
            if (d > bestD)
                (best, bestD) = (c, d);
        }
        return best!.Value;
    }

    private static Entity Beast(Simulation sim, string def, int3 at, Faction faction = Faction.Wild) =>
        sim.SpawnCreature(sim.Defs.Creature(def), def, at, faction, ageDays: 400);

    private static void Put(Simulation sim, Entity e, int3 at) => sim.World.Get<Position>(e) = Position.At(at);

    private static int3 Step(Simulation sim, int3 from) =>
        new[] { new int3(1, 0, 0), new int3(-1, 0, 0), new int3(0, 1, 0), new int3(0, -1, 0) }
            .Select(d => new int3(from.X + d.X, from.Y + d.Y, from.Z)).First(c => sim.Map.Passable(c) && sim.Map.NearestStandable(c.X, c.Y, c.Z) == c);

    [Fact]
    public void ASpookedRatGivesUpItsLarderForGood()
    {
        var (sim, ghost) = Dead();
        var w = sim.World;
        var at = Quiet(sim);
        var rat = Beast(sim, "Rat", at);
        w.Add(rat, new Vermin { Larder = Entity.Null });
        Put(sim, ghost, at);

        Assert.True(Hauntings.Haunt(sim, ghost, HauntKind.Spook));
        var job = new AnimalBrain().Think(sim, rat);

        Assert.IsType<FleeJob>(job);
        Assert.True(w.Has<Migrating>(rat));
    }

    [Fact]
    public void BeastsBoltFromASpookButNotFromOneFarOff()
    {
        var (sim, ghost) = Dead();
        var at = Quiet(sim);
        var deer = Beast(sim, "Deer", at);
        Put(sim, ghost, new int3(at.X + Hauntings.SpookReach + 6, at.Y, at.Z));
        Assert.True(Hauntings.Haunt(sim, ghost, HauntKind.Spook));
        Assert.IsNotType<FleeJob>(new AnimalBrain().Think(sim, deer));

        Put(sim, ghost, at);
        sim.World.Get<Ghost>(ghost).Presence = 1f;
        Assert.True(Hauntings.Haunt(sim, ghost, HauntKind.Spook));
        Assert.IsType<FleeJob>(new AnimalBrain().Think(sim, deer));
    }

    [Fact]
    public void ABeckoningDrawsTimidBeastsToGrazeAboutIt()
    {
        var (sim, ghost) = Dead();
        var at = Quiet(sim);
        var hare = Beast(sim, "Hare", at);
        var there = sim.SpiralFrom(at, 8).First(c => c.Z == at.Z && Maps.Map.Distance(c, at) >= 6
            && sim.Map.NearestStandable(c.X, c.Y, c.Z) == c); // some way off, on its own level
        Put(sim, ghost, there);
        Assert.True(Hauntings.Haunt(sim, ghost, HauntKind.Beckon));

        var job = Assert.IsType<GrazeJob>(new AnimalBrain().Think(sim, hare));

        Assert.True(Maps.Map.Distance(job.Target, there) <= 3, $"grazes at {job.Target}, beckoned to {there}");
    }

    [Fact]
    public void FrostSpellsAFewWordsForAnyoneToRead()
    {
        var (sim, ghost) = Dead();
        var w = sim.World;
        var reader = sim.Creatures().First(e => w.Get<Creature>(e) is { Faction: Faction.Village, Def.Race: Race.Human } && w.Get<Creature>(e).Able);
        var at = w.Get<Position>(reader).Cell;
        Put(sim, ghost, at);

        Assert.NotNull(Hauntings.Problem(sim, ghost, HauntKind.Frost, "!!! 123"));
        Assert.True(Hauntings.Haunt(sim, ghost, HauntKind.Frost, "the miller did it, I saw him"));

        var frost = Assert.Single(w.Query<Haunting>().Select(e => w.Get<Haunting>(e)), h => h.Kind == HauntKind.Frost);
        Assert.Equal("the miller did", frost.Text);
        Assert.True(w.Get<Ghost>(ghost).Presence < 0.01f); // it took all of it
        Assert.Contains(Perception.For(sim, reader).Signs(), s => s.Kind == HauntKind.Frost && s.Text == "the miller did");
    }

    [Fact]
    public void AWhisperIsDreamtOnWakingByTheSleeperAlone()
    {
        var (sim, ghost) = Dead();
        var w = sim.World;
        var people = sim.Creatures().Where(e => w.Get<Creature>(e) is { Faction: Faction.Village, Def.Race: Race.Human } && w.Get<Creature>(e).Able
            && !w.Has<PlayerCharacter>(e)).Take(2).ToList();
        var (sleeper, other) = (people[0], people[1]);
        w.Get<Mind>(sleeper).Asleep = true;
        Put(sim, ghost, w.Get<Position>(sleeper).Cell);

        Assert.True(Dreams.Whisper(sim, ghost, sleeper, "check the well"));
        Assert.Null(Perception.For(sim, sleeper).Dream()); // asleep yet

        w.Get<Mind>(sleeper).Asleep = false;
        sim.TickMany(SimClock.TicksPerSecond + 1);

        var dream = Perception.For(sim, sleeper).Dream();
        Assert.NotNull(dream);
        Assert.Contains("check the well", dream!.Value.Words);
        Assert.Null(Perception.For(sim, other).Dream());
        Assert.Contains(w.Get<Mood>(sleeper).Memories, m => m.Def == sim.Defs.Thoughts.DreamtOfTheDead);
    }

    [Fact]
    public void ASignNearRaidersShakesTheirNerve()
    {
        var (sim, ghost) = Dead();
        var w = sim.World;
        var at = Quiet(sim);
        var near = Raids.SpawnRaider(sim, sim.Defs.AllRaiderKinds[0], "Vask", at, new SimRandom(5));
        var far = Raids.SpawnRaider(sim, sim.Defs.AllRaiderKinds[0], "Orr", sim.Map.NearestStandable(at.X + (at.X < sim.Map.Width / 2 ? 12 : -12), at.Y, at.Z)!.Value, new SimRandom(6));
        Put(sim, ghost, at);

        Assert.True(Hauntings.Haunt(sim, ghost, HauntKind.Knock));

        Assert.True(Hauntings.IsUnnerved(sim, near));
        Assert.False(Hauntings.IsUnnerved(sim, far));

        // Its blows come slower than a steady one's.
        var attack = CombatRules.Attacks(sim, near).First(a => !a.Ranged);
        Assert.True(Blows.Begin(sim, near, Entity.Null, attack, aim: 0f));
        Assert.True(Blows.Begin(sim, far, Entity.Null, CombatRules.Attacks(sim, far).First(a => !a.Ranged), aim: 0f));
        var shaken = w.Get<Windup>(near);
        var steady = w.Get<Windup>(far);
        if (CombatRules.Attacks(sim, far).First(a => !a.Ranged) == attack)
            Assert.True(shaken.Impact - shaken.Start > steady.Impact - steady.Start);

        sim.TickMany(Hauntings.UnnerveTicks + SimClock.TicksPerSecond * 2);
        Assert.False(w.Has<Unnerved>(near)); // it passes
    }

    [Fact]
    public void AGhostSteersABeastItsGotInto()
    {
        var (sim, ghost) = Dead();
        var w = sim.World;
        var at = Quiet(sim);
        var deer = Beast(sim, "Deer", at);
        Put(sim, ghost, at);

        sim.ApplyNow(new PossessCommand(ghost, deer));
        Assert.Equal(deer, Possession.Ridden(sim, ghost));
        Assert.True(w.Has<FreeMover>(deer));
        Assert.NotNull(Perception.For(sim, Entity.Null).Strange(deer));

        var to = Step(sim, at);
        sim.ApplyNow(new RideMoveCommand(ghost, to.X - at.X, to.Y - at.Y));
        var from = w.Get<Position>(deer).Pos;
        sim.TickMany(SimClock.TicksPerSecond);

        var now = w.Get<Position>(deer).Pos;
        Assert.True((now.X - from.X) * (to.X - at.X) + (now.Y - from.Y) * (to.Y - at.Y) > 0.3f, $"moved {from} -> {now}");
        Assert.Null(w.Get<Mind>(deer).Job); // its brain is quiet
        Assert.Equal(w.Get<Position>(deer).Cell, w.Get<Position>(ghost).Cell); // the ghost goes with it
    }

    [Fact]
    public void SpentPresenceThrowsTheGhostOutAndTheBeastBolts()
    {
        var (sim, ghost) = Dead();
        var w = sim.World;
        var at = Quiet(sim);
        var deer = Beast(sim, "Deer", at);
        Put(sim, ghost, at);
        w.Get<Ghost>(ghost).Presence = Possession.Cost + 0.02f;

        Assert.True(Possession.Possess(sim, ghost, deer));
        for (var t = 0; t < SimClock.TicksPerHour / 4 && w.Has<Possessed>(deer); t += SimClock.TicksPerSecond)
            sim.TickMany(SimClock.TicksPerSecond);

        Assert.False(w.Has<Possessed>(deer));
        Assert.True(w.Get<Ghost>(ghost).Riding.IsNull);
        Assert.IsType<FleeJob>(new AnimalBrain().Think(sim, deer)); // startled by what was in it
    }

    [Fact]
    public void ARaidsBeastsWontHaveAGhost()
    {
        var (sim, ghost) = Dead();
        var at = Quiet(sim);
        var wolf = Beast(sim, "Wolf", at, Faction.Predator);
        Put(sim, ghost, at);

        Assert.Contains("someone else's", Possession.Problem(sim, ghost, wolf));
        Assert.False(Possession.Possess(sim, ghost, wolf));
    }

    [Fact]
    public void ARiddenBoarGoresARaiderButPassesThroughTheTown()
    {
        var (sim, ghost) = Dead();
        var w = sim.World;
        var at = Quiet(sim);
        var boar = Beast(sim, "Boar", at);
        Put(sim, ghost, at);
        Assert.True(Possession.Possess(sim, ghost, boar));
        var next = Step(sim, at);
        var aim = MathF.Atan2(next.Y - at.Y, next.X - at.X);

        var raider = Raids.SpawnRaider(sim, sim.Defs.AllRaiderKinds[0], "Vask", next, new SimRandom(5));
        Hold(sim, raider);
        sim.ApplyNow(new RideAttackCommand(ghost, false, aim));
        Assert.True(w.Has<Windup>(boar));
        sim.TickMany(SimClock.TicksPerSecond * 2);
        Assert.NotEmpty(w.Get<Health>(raider).Injuries);

        sim.Depart(raider);
        var villager = sim.Creatures().First(e => w.Get<Creature>(e) is { Faction: Faction.Village, Def.Race: Race.Human } && w.Get<Creature>(e).Able && !w.Has<PlayerCharacter>(e));
        Hold(sim, villager);
        Put(sim, villager, next);
        var before = w.Get<Health>(villager).Injuries.Count;
        sim.ApplyNow(new RideAttackCommand(ghost, false, aim));
        sim.TickMany(SimClock.TicksPerSecond * 2);
        Assert.Equal(before, w.Get<Health>(villager).Injuries.Count);
    }

    /// <summary>Stands where it's put, whatever it sees: drafted, its job dropped.</summary>
    private static void Hold(Simulation sim, Entity e)
    {
        var mind = sim.World.Get<Mind>(e);
        if (mind.Job != null)
            AiSystem.EndJob(sim, e, mind, false);
        mind.Drafted = true;
        sim.World.Get<Mover>(e).Stop();
    }

    [Fact]
    public void ABeastFallingThrowsItsGhostOut()
    {
        var (sim, ghost) = Dead();
        var w = sim.World;
        var at = Quiet(sim);
        var hare = Beast(sim, "Hare", at);
        Put(sim, ghost, at);
        Assert.True(Possession.Possess(sim, ghost, hare));

        ref var c = ref w.Get<Creature>(hare);
        NeedsSystem.Kill(sim, hare, ref c, "died");
        sim.TickMany(SimClock.TicksPerSecond + 1);

        Assert.True(w.Get<Ghost>(ghost).Riding.IsNull);
        Assert.False(w.IsAlive(hare) && w.Has<Possessed>(hare));
    }

    [Fact]
    public void BeingInABeastSurvivesASave()
    {
        var (sim, ghost) = Dead();
        var at = Quiet(sim);
        var deer = Beast(sim, "Deer", at);
        Put(sim, ghost, at);
        Assert.True(Possession.Possess(sim, ghost, deer));
        Hauntings.Leave(sim, at, HauntKind.Frost, "go north");
        Hauntings.Unnerve(sim, at);

        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);

        var lw = loaded.World;
        var lghost = Ghosts.Of(loaded, Ana);
        Assert.Equal(deer, Possession.Ridden(loaded, lghost));
        Assert.Contains(lw.Query<Haunting>(), e => lw.Get<Haunting>(e).Text == "go north");
    }
}

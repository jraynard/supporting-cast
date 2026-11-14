using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Systems;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>The town in a raid: the armed fight, the rest get away, adventurers join in, and players strike only at the raiders.</summary>
public class DefenceTests
{
    private static Entity Person(Simulation sim, string name, int3 cell, Faction faction = Faction.Village, string? weapon = "ShortSword")
    {
        var e = sim.SpawnCreature(sim.Defs.Creature("Human"), name, cell, faction, ageDays: 25 * 365);
        Array.Fill(sim.World.Get<Needs>(e).Values, 1f);
        if (weapon != null)
            sim.Equip(e, sim.Defs.Thing(weapon));
        sim.SetSkills(e, 8);
        return e;
    }

    /// <summary>A raider who stays where it's put (drafted: it waits for orders), to be met.</summary>
    private static Entity StandingRaider(Simulation sim, int3 cell)
    {
        var r = Raids.SpawnRaider(sim, sim.Defs.AllRaiderKinds[0], "Skarn", cell, sim.Rand);
        sim.World.Get<Mind>(r).Drafted = true;
        return r;
    }

    [Fact]
    public void AnArmedVillagerGoesOutToMeetARaider()
    {
        var sim = Create();
        var corin = Person(sim, "Corin", C(5, 5));
        var raider = StandingRaider(sim, C(13, 5));
        var hit = false;
        for (var t = 0; t < SimClock.TicksPerSecond * 40 && !hit; t++)
        {
            sim.Tick();
            hit = sim.World.Get<Health>(raider).Injuries.Count > 0 || sim.World.Get<Creature>(raider).Dead;
        }
        Assert.True(hit, $"Corin: {sim.World.Get<Mind>(corin).Job?.Report ?? "idle"}");
    }

    [Fact]
    public void ARaiderTurnsOnWhoeverHurtsThem()
    {
        var sim = Create();
        var raider = Raids.SpawnRaider(sim, sim.Defs.AllRaiderKinds[0], "Skarn", C(10, 5), sim.Rand);
        var near = Person(sim, "Near", C(12, 5), weapon: null);
        var far = Person(sim, "Far", C(18, 5));
        Assert.Equal(near, Assert.IsType<AssaultJob>(new RaiderBrain().Think(sim, raider)).Focus);

        RealtimeAttacks.Provoke(sim, raider, far);
        Assert.Equal(far, Assert.IsType<AssaultJob>(new RaiderBrain().Think(sim, raider)).Focus);
    }

    [Fact]
    public void ARaiderBreaksOffToFaceSomeoneCuttingIntoThem()
    {
        var sim = Create();
        var raider = Raids.SpawnRaider(sim, sim.Defs.AllRaiderKinds[0], "Skarn", C(10, 5), sim.Rand);
        var near = Person(sim, "Near", C(12, 5), weapon: null);
        sim.World.Get<Mind>(near).Drafted = true;
        var far = Person(sim, "Far", C(18, 5));
        sim.World.Get<Mind>(far).Drafted = true;
        sim.TickMany(10);
        Assert.Equal(near, sim.World.Get<Mind>(raider).Job?.Focus);

        sim.TickMany(AssaultJob.CommitTicks);
        RealtimeAttacks.Provoke(sim, raider, far);
        sim.TickMany(30);
        Assert.Equal(far, sim.World.Get<Mind>(raider).Job?.Focus);
    }

    [Fact]
    public void AnUnarmedVillagerGetsAway()
    {
        var sim = Create();
        var bea = Person(sim, "Bea", C(10, 10), weapon: null);
        var raider = StandingRaider(sim, C(14, 10));
        var before = Map.Distance(sim.World.Get<Position>(bea).Cell, sim.World.Get<Position>(raider).Cell);
        sim.TickMany(SimClock.TicksPerSecond * 10);
        var after = Map.Distance(sim.World.Get<Position>(bea).Cell, sim.World.Get<Position>(raider).Cell);
        Assert.True(after > before, $"from {before} to {after} cells; {sim.World.Get<Mind>(bea).Job?.Report ?? "idle"}");
        Assert.Empty(sim.World.Get<Health>(bea).Injuries);
    }

    [Fact]
    public void AdventurersFightRaidersAndRaidersComeForThem()
    {
        Assert.True(Factions.Hostile(Faction.Raider, Faction.Adventurer));
        Assert.False(Factions.Hostile(Faction.Village, Faction.Adventurer));

        var sim = Create();
        var hero = Person(sim, "Aldric", C(5, 5), Faction.Adventurer);
        var raider = StandingRaider(sim, C(18, 5));
        for (var t = 0; t < SimClock.TicksPerSecond * 5 && sim.World.Get<Mind>(hero).Job is not DefendJob; t++)
            sim.Tick();
        Assert.IsType<DefendJob>(sim.World.Get<Mind>(hero).Job);

        // A raider left to itself goes for the adventurer: there's nobody else.
        sim.World.Get<Mind>(raider).Drafted = false;
        Assert.IsType<AssaultJob>(new RaiderBrain().Think(sim, raider));
    }

    [Fact]
    public void FDoesntStartAFightAPlayersDrawnSwingDoes()
    {
        var sim = Create();
        var me = Person(sim, "Arin", C(5, 5));
        sim.World.Add(me, new Avatar());
        var raider = StandingRaider(sim, C(6, 5));
        Assert.Null(AvatarHunting.ActionAt(sim, me, C(6, 5), C(5, 5))); // F at a raider: nothing (the mouse fights)

        sim.Tick(); // moving freely from here
        sim.ApplyNow(new DrawWeaponCommand(me, true));
        for (var t = 0; t < SimClock.TicksPerSecond * 10 && sim.World.Get<Health>(raider).Injuries.Count == 0; t++)
        {
            if (!sim.World.Has<Windup>(me))
                sim.ApplyNow(new AttackCommand(me, false, 0f)); // east, at it
            sim.Tick();
        }
        Assert.True(sim.World.Get<Health>(raider).Injuries.Count > 0 || sim.World.Get<Creature>(raider).Dead);
    }
}

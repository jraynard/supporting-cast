using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Combat;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Systems;
using SupportingCast.Sim.Maps;

namespace SupportingCast.Sim.Tests;

public class PrisonerTests
{
    private static Simulation Warm(int size = 30)
    {
        var sim = TestWorld.Create(size, size);
        sim.Climate.BaseTemperature = 20;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 20f);
        return sim;
    }

    /// <summary>A villager doing only the given work.</summary>
    private static Entity Villager(Simulation sim, int3 at, string name, params WorkType[] work)
    {
        var e = sim.SpawnCreature(sim.Defs.Creature("Human"), name, at, ageDays: 25 * 365);
        Array.Fill(sim.World.Get<Needs>(e).Values, 1f);
        var mind = sim.World.Get<Mind>(e);
        Array.Fill(mind.WorkPriority, 0);
        foreach (var w in work)
            mind.WorkPriority[(int)w] = 1;
        return e;
    }

    /// <summary>A bandit lying downed and bleeding, given up on the raid.</summary>
    private static Entity DownedBandit(Simulation sim, int3 at)
    {
        var e = sim.SpawnCreature(sim.Defs.Creature("Human"), "Bandit", at, Faction.Raider, ageDays: 30 * 365);
        Array.Fill(sim.World.Get<Needs>(e).Values, 1f);
        sim.Equip(e, sim.Thing("Club"));
        sim.World.Add(e, new Leaving());
        var h = sim.World.Get<Health>(e);
        var inj = HealthSystem.Damage(sim, e, 14, DamageType.Cut, h.Body.PartIndex("LeftLeg"))!;
        inj.InfectionRolled = true; // keep infection out of these tests
        h.Blood = 0.4f;
        sim.TickMany(60);
        Assert.True(sim.World.Get<Creature>(e).Downed);
        return e;
    }

    private static Entity PrisonerBed(Simulation sim, int3 at)
    {
        var bed = sim.SpawnStructure(sim.Thing("Bed"), at);
        sim.World.Add(bed, new ForPrisoners());
        sim.RebuildDerivedIfNeeded();
        return bed;
    }

    /// <summary>A prisoner already in their bed.</summary>
    private static Entity Prisoner(Simulation sim, int3 bedAt)
    {
        var bed = PrisonerBed(sim, bedAt);
        var e = sim.SpawnCreature(sim.Defs.Creature("Human"), "Captive", bedAt, Faction.Raider, ageDays: 30 * 365);
        Array.Fill(sim.World.Get<Needs>(e).Values, 1f);
        Prisoners.Imprison(sim, e, bed);
        return e;
    }

    [Fact]
    public void DoctorsStabilizeDownedRaidersWhoThenLeave()
    {
        var sim = Warm();
        var bandit = DownedBandit(sim, C(15, 15));
        Villager(sim, C(5, 5), "Doctor", WorkType.Doctor);
        sim.TickMany(SimClock.HoursToTicks(1));
        Assert.All(sim.World.Get<Health>(bandit).Injuries, i => Assert.True(i.Tended));

        for (var t = 0; t < 8 && sim.World.IsAlive(bandit); t++)
            sim.TickMany(SimClock.DaysToTicks(0.25f));
        Assert.False(sim.World.IsAlive(bandit), "the bandit should have recovered and left");
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("Bandit has fled the area"));
        Assert.DoesNotContain(sim.Messages.Messages, m => m.Text.Contains("Bandit has bled to death"));
    }

    [Fact]
    public void VillagersDoNotSleepInPrisonerBeds()
    {
        var sim = Warm();
        PrisonerBed(sim, C(10, 10));
        var villager = Villager(sim, C(5, 5), "Sleepy");
        Assert.True(AiQueries.FindFreeBed(sim, villager).IsNull);
        var bandit = DownedBandit(sim, C(15, 15));
        Assert.False(AiQueries.FindFreeBed(sim, bandit, prisoner: true).IsNull);
    }

    [Fact]
    public void ACaptureOrderTakesTheRaiderToAPrisonerBed()
    {
        var sim = Warm();
        var bed = PrisonerBed(sim, C(20, 20));
        var bandit = DownedBandit(sim, C(15, 15));
        sim.World.Add(bandit, new CaptureOrder());
        Villager(sim, C(5, 5), "Doctor", WorkType.Doctor);
        sim.TickMany(SimClock.HoursToTicks(2));

        var c = sim.World.Get<Creature>(bandit);
        Assert.False(c.Dead);
        Assert.Equal(Faction.Prisoner, c.Faction);
        Assert.Equal(Faction.Raider, sim.World.Get<Prisoner>(bandit).Was);
        Assert.Equal(bed, sim.World.Get<BedOwner>(bandit).Bed);
        Assert.False(sim.World.Has<Leaving>(bandit));
        Assert.True(HealthSystem.InBed(sim, bandit));
        Assert.False(sim.World.Has<Equipment>(bandit), "prisoners are disarmed");
        Assert.All(sim.World.Get<Health>(bandit).Injuries, i => Assert.True(i.Tended));
    }

    [Fact]
    public void PrisonersKeepToTheirBedAndWardensFeedThem()
    {
        var sim = Warm();
        var prisoner = Prisoner(sim, C(20, 20));
        sim.SpawnItem(sim.Thing("Berries"), 20, C(5, 20));
        Villager(sim, C(5, 5), "Warden", WorkType.Warden);
        var food = sim.Defs.Need("Food");
        sim.World.Get<Needs>(prisoner).Values[food.Index] = 0.1f;
        sim.TickMany(SimClock.HoursToTicks(2));

        Assert.True(sim.World.Get<Needs>(prisoner).Get(food) > 0.5f, "the warden should have brought a meal");
        Assert.True(Map.Distance(sim.World.Get<Position>(prisoner).Cell, C(20, 20)) <= 1, "prisoners stay by their bed");
    }

    [Fact]
    public void AGoodWardenRecruitsAPrisoner()
    {
        var sim = Warm();
        var prisoner = Prisoner(sim, C(20, 20));
        ref var p = ref sim.World.Get<Prisoner>(prisoner);
        p.Mode = PrisonerMode.Recruit;
        p.Resistance = 1f;
        p.LastVisit = -SimClock.TicksPerDay;
        var warden = Villager(sim, C(5, 5), "Warden", WorkType.Warden);
        sim.SetSkills(warden, Skills.Max);

        for (var day = 0; day < 6 && sim.World.Get<Creature>(prisoner).Faction == Faction.Prisoner; day++)
        {
            foreach (var e in new[] { prisoner, warden })
                Array.Fill(sim.World.Get<Needs>(e).Values, 1f);
            sim.TickMany(SimClock.DaysToTicks(1));
        }
        Assert.Equal(Faction.Village, sim.World.Get<Creature>(prisoner).Faction);
        Assert.False(sim.World.Has<Prisoner>(prisoner));
        Assert.False(sim.World.Has<BedOwner>(prisoner));
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("Captive has joined the village"));
    }

    [Fact]
    public void ReleasedPrisonersWalkOff()
    {
        var sim = Warm();
        var prisoner = Prisoner(sim, C(20, 20));
        sim.World.Get<Prisoner>(prisoner).Mode = PrisonerMode.Release;
        Villager(sim, C(5, 5), "Warden", WorkType.Warden);
        sim.TickMany(SimClock.HoursToTicks(2));
        Assert.False(sim.World.IsAlive(prisoner));
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("released"));
    }

    // ---------------- Prisoner mood and prison breaks ----------------

    /// <summary>Bad news piled on (refreshed daily) so the prisoner's mood sinks well below the break line.</summary>
    private static void Torment(Simulation sim, Entity e)
    {
        for (var i = 0; i < 5; i++)
            MoodSystem.Remember(sim, e, sim.Defs.Thoughts.VillagerDied);
    }

    [Fact]
    public void AMiserableUnwatchedPrisonerBreaksOut()
    {
        var sim = Warm(40);
        var prisoner = Prisoner(sim, C(20, 20));
        var broke = false;
        for (var day = 0; day < 10 && !broke; day++)
        {
            Torment(sim, prisoner);
            for (var h = 0; h < 24 && !broke; h++)
            {
                sim.TickMany(SimClock.TicksPerHour);
                broke = !sim.World.IsAlive(prisoner) || sim.World.Has<Escaping>(prisoner);
            }
        }
        Assert.True(broke, "a miserable prisoner nobody watches should try to escape");
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("Captive is breaking out"));
        if (sim.World.IsAlive(prisoner))
        {
            Assert.Equal(Faction.Raider, sim.World.Get<Creature>(prisoner).Faction);
            Assert.False(sim.World.Has<Prisoner>(prisoner));
        }
    }

    [Fact]
    public void AContentPrisonerStaysPut()
    {
        var sim = Warm(40);
        var prisoner = Prisoner(sim, C(20, 20));
        sim.World.Get<Prisoner>(prisoner).Mode = PrisonerMode.Recruit;
        sim.World.Get<Prisoner>(prisoner).Resistance = 50f; // stays a prisoner for the whole test
        sim.SpawnItem(sim.Thing("Berries"), 75, C(5, 20));
        Villager(sim, C(5, 5), "Warden", WorkType.Warden);
        for (var day = 0; day < 3; day++)
        {
            sim.TickMany(SimClock.TicksPerDay);
            Assert.True(sim.World.Has<Prisoner>(prisoner), $"broke out on day {day + 1}");
            Assert.Equal(0f, Prisoners.BreakChancePerHour(sim, prisoner));
        }
        Assert.True(MoodRules.MoodOf(sim, prisoner) >= Prisoners.BreakBelow);
    }

    [Fact]
    public void BreaksNeedLowMoodAndAreRarerWhenWatched()
    {
        var sim = Warm(40);
        var prisoner = Prisoner(sim, C(20, 20));
        sim.World.Get<Mood>(prisoner).Value = 0.5f;
        Assert.Equal(0f, Prisoners.BreakChancePerHour(sim, prisoner));
        sim.World.Get<Mood>(prisoner).Value = 0.1f;
        var alone = Prisoners.BreakChancePerHour(sim, prisoner);
        Assert.True(alone > 0);
        Villager(sim, C(23, 20), "Guard");
        Assert.True(Prisoners.Watched(sim, prisoner));
        Assert.Equal(alone * Prisoners.WatchedFactor, Prisoners.BreakChancePerHour(sim, prisoner), 4);
        sim.World.Get<Prisoner>(prisoner).Resistance = 0; // ready to join: no reason to run
        Assert.Equal(0f, Prisoners.BreakChancePerHour(sim, prisoner));
    }

    [Fact]
    public void WardenTalksLiftAPrisonersMoodAndOpinion()
    {
        var sim = Warm();
        var prisoner = Prisoner(sim, C(20, 20));
        ref var p = ref sim.World.Get<Prisoner>(prisoner);
        p.Mode = PrisonerMode.Recruit;
        p.Resistance = 5f;
        p.LastVisit = -SimClock.TicksPerDay;
        var warden = Villager(sim, C(5, 5), "Warden", WorkType.Warden);
        var before = MoodRules.Opinion(sim, prisoner, warden);
        sim.TickMany(SimClock.HoursToTicks(2));
        Assert.Contains(sim.World.Get<Mood>(prisoner).Memories, m => m.Def == sim.Defs.Thoughts.WardenTalkedWithMe && m.Other == warden);
        Assert.True(MoodRules.Opinion(sim, prisoner, warden) > before);
        Assert.True(sim.World.Get<Prisoner>(prisoner).Resistance < 5f);
    }

    [Fact]
    public void ContentPrisonersListenBetter()
    {
        var sim = Warm();
        var prisoner = Prisoner(sim, C(20, 20));
        var warden = Villager(sim, C(5, 5), "Warden");
        sim.World.Get<Mood>(prisoner).Value = 0.1f;
        var sullen = Prisoners.PersuadeFactor(sim, prisoner, warden);
        sim.World.Get<Mood>(prisoner).Value = 0.8f;
        var content = Prisoners.PersuadeFactor(sim, prisoner, warden);
        Assert.True(content > sullen + 0.4f, $"{content} vs {sullen}");
    }

    [Fact]
    public void AnEscapeeGrabsAWeaponAndRuns()
    {
        var sim = Warm(40);
        var prisoner = Prisoner(sim, C(20, 20));
        sim.SpawnItem(sim.Thing("Axe"), 1, C(23, 20));
        Prisoners.Escape(sim, prisoner);
        Assert.True(sim.World.Has<Escaping>(prisoner));
        var armed = false;
        for (var t = 0; t < SimClock.TicksPerHour && sim.World.IsAlive(prisoner) && !armed; t += 30)
        {
            sim.TickMany(30);
            armed = sim.World.IsAlive(prisoner) && RaiderBrain.Armed(sim, prisoner);
        }
        Assert.True(armed, "the escapee should pick up the axe");
        sim.TickMany(SimClock.HoursToTicks(2));
        Assert.False(sim.World.IsAlive(prisoner), "then run off the map");
    }

    [Fact]
    public void AGameSavedMidEscapePlaysOnTheSame()
    {
        var sim = Warm(40);
        var prisoner = Prisoner(sim, C(20, 20));
        Prisoner(sim, C(22, 22));
        sim.SpawnItem(sim.Thing("Axe"), 1, C(25, 20));
        Prisoners.Escape(sim, prisoner);
        sim.TickMany(120);
        var (loaded, _) = Saving.SaveGame.Read(Saving.SaveGame.Write(sim), sim.Defs);
        AiSystem.InterruptAll(sim);
        Assert.True(loaded.World.Has<Escaping>(prisoner));
        for (var h = 0; h < 3; h++)
        {
            Assert.Equal(sim.StateHash(), loaded.StateHash());
            sim.TickMany(SimClock.TicksPerHour);
            loaded.TickMany(SimClock.TicksPerHour);
        }
        Assert.Equal(sim.StateHash(), loaded.StateHash());
    }
}

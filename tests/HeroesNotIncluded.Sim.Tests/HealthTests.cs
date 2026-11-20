using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Tests;

public class HealthTests
{
    private static Simulation Warm(int size = 30)
    {
        var sim = TestWorld.Create(size, size);
        sim.Climate.BaseTemperature = 20;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 20f);
        return sim;
    }

    /// <summary>A villager who stays put and isn't hungry or tired, doing only the given work (none by default).</summary>
    private static Entity Person(Simulation sim, int3 at, string name = "Patient", params WorkType[] work)
    {
        var e = sim.SpawnCreature(sim.Defs.Creature("Human"), name, at, ageDays: 25 * 365);
        Array.Fill(sim.World.Get<Needs>(e).Values, 1f);
        var mind = sim.World.Get<Mind>(e);
        Array.Fill(mind.WorkPriority, 0);
        foreach (var w in work)
            mind.WorkPriority[(int)w] = 1;
        return e;
    }

    private static int Part(Simulation sim, Entity e, string name) => sim.World.Get<Health>(e).Body.PartIndex(name);

    [Fact]
    public void AnUntendedWoundStopsBleedingByItselfAfterAFewHours()
    {
        // A solo round: a raid put the whole town down, nobody was left to bind anyone, and all bled out by morning.
        var sim = Warm();
        var me = Person(sim, C(10, 10));
        var h = sim.World.Get<Health>(me);
        HealthSystem.Damage(sim, me, 10, DamageType.Cut, Part(sim, me, "LeftLeg"));
        var fresh = HealthSystem.Bleeding(sim, h);
        Assert.True(fresh > 0f);
        var half = (int)(SimClock.TicksPerHour * HealthSystem.ClotHours / 2f);
        sim.TickMany(half);
        Assert.InRange(HealthSystem.Bleeding(sim, h), fresh * 0.4f, fresh * 0.6f); // easing off
        sim.TickMany(half + 60);
        Assert.Equal(0f, HealthSystem.Bleeding(sim, h));
        var blood = h.Blood;
        Assert.True(blood > 1f - fresh * HealthSystem.ClotHours * 0.6f, $"lost about half of {HealthSystem.ClotHours} hours' fresh bleeding: {blood}");
        sim.TickMany(SimClock.TicksPerHour);
        Assert.True(h.Blood >= blood, "stopped: blood comes back");
        Assert.True(h.Injuries[0].NeedsTending(sim.Clock.Tick), "still wants a dressing");
    }

    [Fact]
    public void BloodComesBackFasterInBed()
    {
        var sim = Warm();
        var bed = sim.SpawnStructure(sim.Thing("Bed"), C(10, 10));
        var abed = Person(sim, sim.World.Get<Thing>(bed).Cell, "Abed");
        var floor = Person(sim, C(20, 20), "Floor");
        foreach (var e in new[] { abed, floor })
        {
            sim.World.Get<Health>(e).Blood = 0.1f; // bled white, but not bleeding now
            HealthSystem.SetDowned(sim, e, ref sim.World.Get<Creature>(e), true);
        }
        Assert.True(HealthSystem.InBed(sim, abed));
        sim.TickMany(SimClock.HoursToTicks(6));
        float Gained(Entity e) => sim.World.Get<Health>(e).Blood - 0.1f;
        Assert.Equal(HealthSystem.InBedBloodFactor, Gained(abed) / Gained(floor), 0.05f);
        // From nearly white to on their feet in well under a day.
        sim.TickMany(SimClock.HoursToTicks(12));
        Assert.False(sim.World.Get<Creature>(abed).Downed, $"blood {sim.World.Get<Health>(abed).Blood:0.00}");
        // (Back up, they'd see to the one on the floor: WorkGivers.Emergency.)
    }

    [Fact]
    public void HitLocationsFollowCoverage()
    {
        var sim = Warm();
        var h = sim.World.Get<Health>(Person(sim, C(5, 5)));
        var hits = new int[h.Body.Parts.Count];
        const int rolls = 4000;
        for (var i = 0; i < rolls; i++)
            hits[HealthSystem.PickPart(sim, h)]++;
        var total = h.Body.Parts.Sum(p => p.Coverage);
        for (var i = 0; i < hits.Length; i++)
            Assert.InRange(hits[i] / (float)rolls, h.Body.Parts[i].Coverage / total - 0.03f, h.Body.Parts[i].Coverage / total + 0.03f);

        // Attacks from above favor the head.
        var head = h.Body.PartIndex("Head");
        var fromAbove = Enumerable.Range(0, rolls).Count(_ => HealthSystem.PickPart(sim, h, PartHeight.High) == head);
        Assert.True(fromAbove > hits[head] * 1.5f, $"head hit {fromAbove} times from above vs {hits[head]} level");
    }

    [Fact]
    public void UntendedBleedingDownsThenKills()
    {
        var sim = Warm();
        var pawn = Person(sim, C(5, 5));
        // Deep cuts all over: more than the blood they have, even with each easing off by itself (HealthSystem.ClotHours).
        foreach (var part in new[] { "Torso", "LeftLeg", "RightLeg", "LeftArm", "RightArm" })
            HealthSystem.Damage(sim, pawn, 30, DamageType.Cut, Part(sim, pawn, part));
        var h = sim.World.Get<Health>(pawn);
        Assert.True(h.BleedPerHour * HealthSystem.ClotHours / 2f > 1.2f, $"cut deep enough to bleed out ({h.BleedPerHour:F2}/h)");

        var downedAt = -1L;
        for (var t = 0; t < SimClock.HoursToTicks(24) && !sim.World.Get<Creature>(pawn).Dead; t += 60)
        {
            sim.TickMany(60);
            if (downedAt < 0 && sim.World.Get<Creature>(pawn).Downed)
                downedAt = sim.Clock.Tick;
        }
        Assert.True(downedAt > 0, "should collapse from blood loss first");
        Assert.True(sim.World.Get<Creature>(pawn).Dead, "should bleed out without care");
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("bled to death"));
    }

    [Fact]
    public void DoctorRescuesTheDownedToBedAndTheyRecover()
    {
        var sim = Warm();
        var bed = sim.SpawnStructure(sim.Thing("Bed"), C(20, 20));
        sim.RebuildDerivedIfNeeded();
        var patient = Person(sim, C(5, 5));
        Person(sim, C(10, 5), "Doctor", WorkType.Doctor);
        HealthSystem.Damage(sim, patient, 18, DamageType.Cut, Part(sim, patient, "LeftLeg"));
        var h = sim.World.Get<Health>(patient);
        h.Blood = 0.4f; // already lost a lot
        sim.TickMany(60);
        Assert.True(sim.World.Get<Creature>(patient).Downed);

        var wasInBed = false;
        for (var t = 0; t < SimClock.HoursToTicks(2); t += 30)
        {
            sim.TickMany(30);
            wasInBed |= HealthSystem.InBed(sim, patient);
        }
        Assert.True(wasInBed, "the doctor should have carried the patient to bed");
        Assert.All(h.Injuries, i => Assert.True(i.Tended));
        Assert.Equal(0, h.BleedPerHour);

        sim.TickMany(SimClock.DaysToTicks(1));
        var c = sim.World.Get<Creature>(patient);
        Assert.False(c.Dead);
        Assert.False(c.Downed, $"should be back up after a day (blood {h.Blood:F2}, pain {h.Pain:F2})");
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("bandaged"));
        Assert.True(sim.World.IsAlive(bed));
    }

    /// <summary>
    /// Someone collapses in the night: the nearest villager wakes (whatever their work, none here),
    /// binds the bleeding and carries them to a bed. A player who collapsed after a raid used to lie
    /// in the road till morning.
    /// </summary>
    [Fact]
    public void ASleeperWakesToBindAndCarryTheCollapsed()
    {
        var sim = Warm();
        var bed = sim.SpawnStructure(sim.Thing("Bed"), C(20, 20));
        var own = sim.SpawnStructure(sim.Thing("Bed"), C(8, 8));
        sim.RebuildDerivedIfNeeded();
        var sleeper = Person(sim, C(8, 8), "Sleeper");
        sim.World.Get<Needs>(sleeper).Values[sim.Defs.Need("Rest").Index] = 0.2f;
        AiSystem.Order(sim, sleeper, new SleepJob(own));
        sim.TickMany(30);
        Assert.True(sim.World.Get<Mind>(sleeper).Asleep);

        var patient = Person(sim, C(5, 5));
        HealthSystem.Damage(sim, patient, 18, DamageType.Cut, Part(sim, patient, "LeftLeg"));
        var h = sim.World.Get<Health>(patient);
        h.Blood = 0.4f;
        sim.TickMany(60);
        Assert.True(sim.World.Get<Creature>(patient).Downed);

        var wasInBed = false;
        for (var t = 0; t < SimClock.HoursToTicks(2) && !wasInBed; t += 30)
        {
            sim.TickMany(30);
            wasInBed |= HealthSystem.InBed(sim, patient);
        }
        Assert.True(wasInBed, $"carried to a bed (sleeper: {sim.World.Get<Mind>(sleeper).Job?.Report ?? "idle"}, {sim.Ai.LastFailure.GetValueOrDefault(sleeper)})");
        Assert.Equal(0, h.BleedPerHour);
    }

    /// <summary>On their feet but bleeding out, with no foe about: someone nearby binds it.</summary>
    [Fact]
    public void SomeoneBindsAVillagerBleedingOutOnTheirFeet()
    {
        var sim = Warm();
        var patient = Person(sim, C(5, 5));
        var helper = Person(sim, C(12, 5), "Helper");
        HealthSystem.Damage(sim, patient, 14, DamageType.Cut, Part(sim, patient, "LeftLeg"));
        HealthSystem.Damage(sim, patient, 14, DamageType.Cut, Part(sim, patient, "RightLeg"));
        var h = sim.World.Get<Health>(patient);
        h.Blood = 0.55f;
        sim.TickMany(1);
        Assert.False(sim.World.Get<Creature>(patient).Downed);
        Assert.True(h.BleedPerHour > 0 && h.Blood / h.BleedPerHour <= WorkGivers.CriticalHours, $"bleeding {h.BleedPerHour:0.00}/h of {h.Blood:0.00}");

        for (var t = 0; t < SimClock.HoursToTicks(1) && h.BleedPerHour > 0; t += 30)
            sim.TickMany(30);
        Assert.Equal(0, h.BleedPerHour);
        Assert.False(sim.World.Get<Creature>(helper).Downed);
    }

    /// <summary>The bed burns down while the doctor walks to the patient: the rescue fails cleanly instead of throwing.</summary>
    [Fact]
    public void RescueGivesUpWhenTheBedIsGone()
    {
        var sim = Warm();
        var bed = sim.SpawnStructure(sim.Thing("Bed"), C(20, 20));
        sim.RebuildDerivedIfNeeded();
        var patient = Person(sim, C(5, 5));
        HealthSystem.Damage(sim, patient, 18, DamageType.Cut, Part(sim, patient, "LeftLeg"));
        sim.World.Get<Health>(patient).Blood = 0.4f;

        var doctor = Person(sim, C(28, 28), "Doctor"); // no work types, so it only does what it's ordered to
        sim.TickMany(60);
        Assert.True(sim.World.Get<Creature>(patient).Downed);
        AiSystem.Order(sim, doctor, new RescueJob(patient, bed));
        sim.TickMany(10);
        Assert.IsType<RescueJob>(sim.World.Get<Mind>(doctor).Job);
        Assert.False(sim.World.Has<Carried>(patient)); // still walking over: that's the window being tested
        sim.DestroyThing(bed);
        sim.RebuildDerivedIfNeeded();

        sim.TickMany(SimClock.HoursToTicks(1)); // StrictJobs: any exception would fail the test
        Assert.False(sim.World.Has<Carried>(patient));
        Assert.Contains("bed is gone", sim.Ai.LastFailure.GetValueOrDefault(doctor) ?? "(none)");
    }

    [Fact]
    public void DestroyingAVitalPartKills()
    {
        var sim = Warm();
        var pawn = Person(sim, C(5, 5));
        HealthSystem.Damage(sim, pawn, 1000, DamageType.Blunt, Part(sim, pawn, "Head"));
        Assert.True(sim.World.Get<Creature>(pawn).Dead);
    }

    [Fact]
    public void LegWoundsSlowWalking()
    {
        var sim = Warm(40);
        var healthy = Person(sim, C(2, 5), "Healthy");
        var limping = Person(sim, C(2, 15), "Limping");
        HealthSystem.Damage(sim, limping, 20, DamageType.Cut, Part(sim, limping, "LeftLeg"));
        HealthSystem.Damage(sim, limping, 20, DamageType.Cut, Part(sim, limping, "RightLeg"));
        Assert.True(sim.World.Get<Health>(limping).Capacity(Capacity.Moving) < 0.5f);
        foreach (var (e, y) in new[] { (healthy, 5), (limping, 15) })
        {
            sim.World.Get<Mind>(e).Drafted = true;
            var mover = sim.World.Get<Mover>(e);
            mover.Path = sim.Pathfinder.FindPath(C(2, y), C(35, y), PathEnd.OnTile);
            mover.StepFrom = C(2, y);
        }
        sim.TickMany(SimClock.TicksPerSecond * 3);
        var fast = sim.World.Get<Position>(healthy).Pos.X;
        var slow = sim.World.Get<Position>(limping).Pos.X;
        Assert.True(slow - 2 < (fast - 2) * 0.6f, $"limping {slow:F1} vs healthy {fast:F1}");
    }

    [Fact]
    public void TornWingsGroundADragon()
    {
        var sim = Warm();
        var dragon = sim.SpawnCreature(sim.Defs.Creature("Dragon"), "Ember", C(5, 5), ageDays: 5);
        Assert.NotNull(Navigation.FlyingStage(sim, dragon));
        HealthSystem.Damage(sim, dragon, 1000, DamageType.Cut, Part(sim, dragon, "LeftWing"));
        HealthSystem.Damage(sim, dragon, 1000, DamageType.Cut, Part(sim, dragon, "RightWing"));
        Assert.False(sim.World.Get<Creature>(dragon).Dead);
        Assert.Null(Navigation.FlyingStage(sim, dragon));
    }

    [Fact]
    public void StandingInFireCausesBurns()
    {
        var sim = Warm();
        var pawn = Person(sim, C(5, 5));
        sim.World.Get<Mind>(pawn).Drafted = true; // doesn't run away
        sim.SpawnItem(sim.Thing("Wood"), 30, C(5, 5));
        sim.Fire.Ignite(sim, C(5, 5), 1f);
        sim.TickMany(60);
        var h = sim.World.Get<Health>(pawn);
        Assert.Contains(h.Injuries, i => i.Type == DamageType.Burn);
        Assert.Equal(0, h.BleedPerHour); // burns don't bleed
    }

    // ---------------- Infection and scars ----------------

    /// <summary>A wound with an infection already set in (its one roll spent), on the left arm.</summary>
    private static Injury Infected(Simulation sim, Entity e, bool tended, float quality = 0f)
    {
        var inj = HealthSystem.Damage(sim, e, 6, DamageType.Blunt, Part(sim, e, "LeftArm"))!;
        inj.InfectionRolled = true;
        inj.Infection = new Infection { Severity = 0.1f };
        inj.Tended = tended;
        inj.Quality = quality;
        inj.TendedTick = sim.Clock.Tick;
        return inj;
    }

    /// <summary>Tick in quarter days, keeping the person fed and rested so only the wound matters.</summary>
    private static void Live(Simulation sim, Entity e, float days)
    {
        for (var t = 0f; t < days && !sim.World.Get<Creature>(e).Dead; t += 0.25f)
        {
            Array.Fill(sim.World.Get<Needs>(e).Values, 1f);
            sim.TickMany(SimClock.DaysToTicks(0.25f));
        }
    }

    [Fact]
    public void AnUntendedInfectionKills()
    {
        var sim = Warm();
        var patient = Person(sim, C(5, 5));
        Infected(sim, patient, tended: false);
        Live(sim, patient, 3);
        Assert.True(sim.World.Get<Creature>(patient).Dead);
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("died of an infection"));
    }

    [Fact]
    public void AWellTendedInfectionIsFoughtOffAndTheWoundHeals()
    {
        var sim = Warm();
        var patient = Person(sim, C(5, 5));
        var inj = Infected(sim, patient, tended: true, quality: 0.9f);
        Live(sim, patient, 3);
        Assert.False(sim.World.Get<Creature>(patient).Dead);
        Assert.Null(inj.Infection);
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("fought off the infection"));
        Live(sim, patient, 1);
        Assert.DoesNotContain(inj, sim.World.Get<Health>(patient).Injuries);
    }

    [Fact]
    public void DoctorsRedressInfectedWoundsDaily()
    {
        var sim = Warm();
        var patient = Person(sim, C(5, 5));
        var inj = Infected(sim, patient, tended: true, quality: 0.2f);
        inj.TendedTick = sim.Clock.Tick - SimClock.TicksPerDay;
        Assert.True(inj.NeedsTending(sim.Clock.Tick));
        var doctor = Person(sim, C(8, 5), "Doctor", WorkType.Doctor);
        sim.SetSkills(doctor, Skills.Max);
        sim.TickMany(SimClock.HoursToTicks(1));
        Assert.False(inj.NeedsTending(sim.Clock.Tick));
        Assert.True(inj.Quality > 0.8f, $"a master doctor should dress it well, got {inj.Quality:F2}");
    }

    [Fact]
    public void BadWoundsCanInfectButScratchesRarelyDo()
    {
        // The roll is once per wound, half a day in: count how many of many bites on many people fester.
        var sim = Warm(60);
        var bites = new List<Injury>();
        var scratches = new List<Injury>();
        for (var i = 0; i < 40; i++)
        {
            var p = Person(sim, C(2 + i % 20, 2 + i / 20 * 3), $"P{i}");
            bites.Add(HealthSystem.Damage(sim, p, 12, DamageType.Bite, Part(sim, p, "LeftLeg"))!);
            scratches.Add(HealthSystem.Damage(sim, p, 1, DamageType.Blunt, Part(sim, p, "RightArm"))!);
            foreach (var inj in sim.World.Get<Health>(p).Injuries)
                inj.Tended = true; // no bleeding out; quality 0 is as bad as untended for infection
        }
        sim.TickMany(SimClock.HoursToTicks(13));
        Assert.All(bites, b => Assert.True(b.InfectionRolled));
        var festering = bites.Count(b => b.Infection != null);
        Assert.InRange(festering, 4, 25); // ~30%
        Assert.True(scratches.Count(b => b.Infection != null) <= 2);
    }

    [Fact]
    public void DeepWoundsCanLeaveScarsThatLinger()
    {
        var sim = Warm(40);
        var people = new List<Entity>();
        for (var i = 0; i < 8; i++)
        {
            var p = Person(sim, C(3 + i * 3, 5), $"P{i}");
            var inj = HealthSystem.Damage(sim, p, 12, DamageType.Cut, Part(sim, p, "LeftArm"))!;
            inj.Tended = true;
            inj.InfectionRolled = true;
            people.Add(p);
        }
        sim.TickMany(SimClock.DaysToTicks(3));
        var scarred = people.Where(p => sim.World.Get<Health>(p).Scars.Count > 0).ToList();
        Assert.NotEmpty(scarred);
        var h = sim.World.Get<Health>(scarred[0]);
        Assert.Empty(h.Injuries);
        Assert.True(h.PartHealth(Part(sim, scarred[0], "LeftArm")) < 1f);
        Assert.True(h.Capacity(Capacity.Manipulation) < 1f);
    }
}

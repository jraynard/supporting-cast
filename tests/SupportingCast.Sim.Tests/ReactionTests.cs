using SupportingCast.Sim.Combat;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Tests;

/// <summary>Reactions paid from saved time units: snap shots at movers, swings as they pass (<see cref="Reactions"/>).</summary>
public class ReactionTests
{
    private static Simulation Field(ulong seed = 1)
    {
        var sim = TestWorld.Create(40, 20, seed, 6);
        sim.Climate.BaseTemperature = 20;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 20f);
        return sim;
    }

    private static Entity Human(Simulation sim, int3 at, Faction faction = Faction.Village, string? weapon = null)
    {
        var e = sim.SpawnCreature(sim.Defs.Creature("Human"), faction == Faction.Village ? "Villager" : "Bandit", at, faction, ageDays: 25 * 365);
        Array.Fill(sim.World.Get<Needs>(e).Values, 1f);
        sim.SetSkills(e);
        if (weapon != null)
            sim.Equip(e, sim.Thing(weapon));
        return e;
    }

    private static AttackDef Bow(Simulation sim, Entity e) => CombatRules.Attacks(sim, e).First(a => a.Ranged);

    private static float Snap(Simulation sim, Entity e) => TimeUnits.AttackCost(sim, e, Bow(sim, e), ShotMode.Snap);

    /// <summary>The village side ends its phase (keeping its TU); the raiders' phase begins.</summary>
    private static void ToRaiders(Simulation sim)
    {
        foreach (var u in sim.Combat.Units.Where(u => u.Side == Side.Village && !u.Acted))
            sim.Combat.Wait(sim, u.Entity);
        sim.Combat.EndPhase(sim);
        Assert.Equal(Side.Raiders, sim.Combat.Phase);
    }

    private static void Walk(Simulation sim, Entity e, int3 to)
    {
        Assert.Null(sim.Combat.Move(sim, e, to, fromAi: true));
        sim.Combat.FinishAnimation(sim);
    }

    /// <summary>Wounds (to the legs) until the next real hit will push pain over the collapse line.</summary>
    private static void NearCollapse(Simulation sim, Entity e)
    {
        var h = sim.World.Get<Health>(e);
        var limbs = new[] { "LeftLeg", "RightLeg", "LeftArm", "RightArm" }.Select(h.Body.PartIndex).ToArray();
        for (var i = 0; i < 1000 && h.Pain < 0.74f; i++)
            HealthSystem.Damage(sim, e, 0.5f, DamageType.Blunt, limbs[i % limbs.Length]);
        Assert.InRange(h.Pain, 0.74f, 0.8f);
        Assert.False(sim.World.Get<Creature>(e).Downed);
    }

    [Fact]
    public void TuKeptFromYourTurnSnapShootsARaiderWalkingIntoRange()
    {
        var sim = Field();
        var archer = Human(sim, C(5, 10), weapon: "Bow");
        var bandit = Human(sim, C(24, 10), Faction.Raider, "Club");
        sim.Combat.Start(sim);
        var unit = sim.Combat.Unit(archer)!;
        var full = unit.TimeUnits;
        Assert.Equal(TimeUnits.PerTurn(sim, archer), full);
        ToRaiders(sim);
        Assert.Equal(full, unit.TimeUnits); // kept through the enemy's phase

        Walk(sim, bandit, C(18, 10));
        Assert.Equal(1, sim.Combat.ReactionCount);
        Assert.Equal(archer, sim.Combat.LastReaction.Reactor);
        Assert.Contains(sim.Combat.Log, l => l.Text.Contains("fires on overwatch") && l.Text.Contains("shoots at"));
        Assert.Equal(full - Snap(sim, archer), unit.TimeUnits); // paid for the snap shot
    }

    [Fact]
    public void AFighterOnOverwatchSwingsAtAnEnemyCloseEnoughToReach()
    {
        var sim = Field();
        var axeman = Human(sim, C(10, 10), weapon: "ShortSword");
        var first = Human(sim, C(16, 10), Faction.Raider, "Club");
        var second = Human(sim, C(16, 12), Faction.Raider, "Club");
        sim.Combat.Start(sim);
        var unit = sim.Combat.Unit(axeman)!;
        var blade = CombatRules.Attacks(sim, axeman).First(a => !a.Ranged);
        var swing = TimeUnits.AttackCost(sim, axeman, blade, ShotMode.Normal);
        unit.TimeUnits = 3 * swing;
        ToRaiders(sim);

        // Predicted, then done: a swing as the raider steps in beside it.
        var path = sim.Combat.RangeOf(sim, first).PathTo(sim.Map, C(11, 10))!;
        Assert.Contains(Reactions.Threats(sim, sim.Combat, first, C(16, 10), path), t => t.Reactor == axeman && t.Kind == ReactionKind.Overwatch && !t.Attack.Ranged);
        Walk(sim, first, C(11, 10));
        Assert.Equal(1, sim.Combat.ReactionCount);
        Assert.Equal(ReactionKind.Overwatch, sim.Combat.LastReaction.Kind);
        Assert.Contains(sim.Combat.Log, l => l.Text.Contains("strikes on overwatch"));
        Assert.True(unit.OverwatchUsed);
        Assert.Equal(2 * swing, unit.TimeUnits, 3);

        // Once a phase: the next one to step in isn't swung at on overwatch.
        Walk(sim, second, C(11, 11));
        Assert.Single(sim.Combat.Log, l => l.Text.Contains("on overwatch"));
    }

    [Fact]
    public void TuSpentInYourTurnLeaveNothingToReactWith()
    {
        var sim = Field();
        var archer = Human(sim, C(5, 10), weapon: "Bow");
        var bandit = Human(sim, C(24, 10), Faction.Raider, "Club");
        sim.Combat.Start(sim);
        var unit = sim.Combat.Unit(archer)!;
        unit.TimeUnits = Snap(sim, archer) - 1; // walked about all turn
        Assert.False(Reactions.CanReact(sim, unit));
        ToRaiders(sim);
        Walk(sim, bandit, C(18, 10));
        Assert.Equal(0, sim.Combat.ReactionCount);
    }

    [Fact]
    public void ReactionsNeedALineOfFire()
    {
        var sim = Field();
        Human(sim, C(5, 10), weapon: "Bow");
        var bandit = Human(sim, C(24, 10), Faction.Raider, "Club");
        sim.FillBlocks(new int3(12, 0, 1), new int3(12, 19, 3)); // a rock wall across the field
        sim.Combat.Start(sim);
        ToRaiders(sim);
        Walk(sim, bandit, C(18, 10));
        Assert.Equal(0, sim.Combat.ReactionCount);
    }

    [Fact]
    public void OverwatchFiresOncePerPhaseWhateverTheTuKept()
    {
        var sim = Field();
        var archer = Human(sim, C(5, 10), weapon: "Bow");
        var first = Human(sim, C(24, 8), Faction.Raider, "Club");
        var second = Human(sim, C(24, 12), Faction.Raider, "Club");
        sim.Combat.Start(sim);
        var unit = sim.Combat.Unit(archer)!;
        unit.TimeUnits = 3 * Snap(sim, archer); // enough for three
        ToRaiders(sim);
        Walk(sim, first, C(17, 8));
        Assert.Equal(1, sim.Combat.ReactionCount); // one shot at a move, however many cells of it are in range
        Assert.True(unit.OverwatchUsed);
        Assert.Empty(Reactions.Threats(sim, sim.Combat, second, C(24, 12), sim.Combat.RangeOf(sim, second).PathTo(sim.Map, C(17, 12))!));
        Walk(sim, second, C(17, 12));
        Assert.Equal(1, sim.Combat.ReactionCount); // the phase's overwatch shot is spent
        Assert.Equal(3 * Snap(sim, archer) - Snap(sim, archer), unit.TimeUnits);

        // The next enemy phase gives it another.
        foreach (var u in sim.Combat.Units.Where(u => u.Side == Side.Raiders))
            sim.Combat.Wait(sim, u.Entity);
        sim.Combat.EndPhase(sim);
        Assert.False(unit.OverwatchUsed);
    }

    [Fact]
    public void OverwatchOnlyFiresAtAnEnemyComingIntoRangeAndSight()
    {
        var sim = Field();
        Human(sim, C(5, 10), weapon: "Bow");
        var bandit = Human(sim, C(14, 10), Faction.Raider, "Club"); // already in the archer's sights
        sim.Combat.Start(sim);
        ToRaiders(sim);
        var path = sim.Combat.RangeOf(sim, bandit).PathTo(sim.Map, C(10, 10))!;
        Assert.DoesNotContain(Reactions.Threats(sim, sim.Combat, bandit, C(14, 10), path), t => t.Kind == ReactionKind.Overwatch);
        Walk(sim, bandit, C(10, 10));
        Assert.Equal(0, sim.Combat.ReactionCount);
    }

    [Fact]
    public void ANewTurnRefillsTheTu()
    {
        var sim = Field();
        var archer = Human(sim, C(5, 10), weapon: "Bow");
        Human(sim, C(30, 10), Faction.Raider, "Club");
        sim.Combat.Start(sim);
        sim.Combat.Unit(archer)!.TimeUnits = 3;
        ToRaiders(sim);
        foreach (var u in sim.Combat.Units.Where(u => u.Side == Side.Raiders))
            sim.Combat.Wait(sim, u.Entity);
        sim.Combat.EndPhase(sim);
        Assert.Equal(Side.Village, sim.Combat.Phase);
        Assert.Equal(TimeUnits.PerTurn(sim, archer), sim.Combat.Unit(archer)!.TimeUnits);
    }

    [Fact]
    public void AMoverBroughtDownByAReactionStopsWhereItFalls()
    {
        // The reaction has to hit; some seed lands it.
        for (ulong seed = 1; seed < 60; seed++)
        {
            var sim = Field(seed);
            Human(sim, C(5, 10), weapon: "Bow");
            var bandit = Human(sim, C(20, 10), Faction.Raider, "Club"); // just out of the bow's 14 cells: overwatch fires as it steps in
            sim.Combat.Start(sim);
            ToRaiders(sim);
            NearCollapse(sim, bandit);
            // Hobbled, the bandit still has a few cells of movement: walk toward the archer.
            var range = sim.Combat.RangeOf(sim, bandit);
            var dest = range.Cells.Select(sim.Map.CellOf).Where(c => c.Y == 10 && c.X < 20).MinBy(c => c.X);
            Walk(sim, bandit, dest);
            if (!sim.World.Get<Creature>(bandit).Downed)
                continue;
            Assert.NotEqual(dest, sim.World.Get<Position>(bandit).Cell);
            Assert.False(sim.World.Get<Mover>(bandit).Moving);
            Assert.False(sim.Combat.Busy(sim));
            return;
        }
        Assert.Fail("no seed's reaction brought the bandit down");
    }

    [Fact]
    public void SteppingOutOfReachDrawsASwingPaidInTu()
    {
        var sim = Field();
        var axeman = Human(sim, C(10, 10), weapon: "Axe");
        var bandit = Human(sim, C(11, 10), Faction.Raider, "Club");
        sim.Combat.Start(sim);
        var unit = sim.Combat.Unit(axeman)!;
        ToRaiders(sim);
        var before = unit.TimeUnits;
        Walk(sim, bandit, C(15, 10));
        Assert.Equal(1, sim.Combat.ReactionCount);
        Assert.Contains(sim.Combat.Log, l => l.Text.Contains("strikes as they pass"));
        Assert.Equal(axeman, sim.Combat.LastReaction.Reactor);
        Assert.Equal(ReactionKind.Opportunity, sim.Combat.LastReaction.Kind);
        Assert.Equal(before - TimeUnits.AttackCost(sim, axeman, CombatRules.Attacks(sim, axeman)[0], ShotMode.Normal), unit.TimeUnits);
    }

    [Fact]
    public void CirclingWithinReachIsFree()
    {
        var sim = Field();
        Human(sim, C(10, 10), weapon: "Axe");
        var bandit = Human(sim, C(11, 10), Faction.Raider, "Club");
        sim.Combat.Start(sim);
        ToRaiders(sim);
        Walk(sim, bandit, C(11, 11)); // still touching the axeman's cell
        Assert.Equal(0, sim.Combat.ReactionCount);
    }

    [Fact]
    public void ThreatsPredictTheReactionsAMoveDraws()
    {
        var sim = Field();
        var archer = Human(sim, C(5, 10), weapon: "Bow");
        var axeman = Human(sim, C(20, 11), weapon: "Axe");
        var bandit = Human(sim, C(21, 10), Faction.Raider, "Club");
        sim.Combat.Start(sim);
        ToRaiders(sim);

        var from = sim.World.Get<Position>(bandit).Cell;
        var range = sim.Combat.RangeOf(sim, bandit);
        var path = range.PathTo(sim.Map, C(16, 10))!;
        var threats = Reactions.Threats(sim, sim.Combat, bandit, from, path);
        Assert.Contains(threats, t => t.Reactor == axeman && t.Kind == ReactionKind.Opportunity);
        Assert.Contains(threats, t => t.Reactor == archer && t.Kind == ReactionKind.Overwatch);
        Assert.Equal(2, threats.Count);
        Assert.All(threats, t => Assert.InRange(t.Chance, CombatRules.MinChance, CombatRules.MaxChance * CombatRules.ReactionFactor + 0.001f));
        Assert.Equal(0, sim.Combat.ReactionCount);

        var tu = sim.Combat.Units.ToDictionary(u => u.Entity, u => u.TimeUnits);
        Walk(sim, bandit, C(16, 10));
        if (sim.World.Get<Creature>(bandit).Able) // a downed bandit stops early and may dodge the later one
        {
            var reacted = sim.Combat.Units.Where(u => u.Side == Side.Village && u.TimeUnits < tu[u.Entity]).Select(u => u.Entity).OrderBy(e => e.Index);
            Assert.Equal(threats.Select(t => t.Reactor).OrderBy(e => e.Index), reacted);
        }
    }

    [Fact]
    public void AMountedArcherReactsFromTheSaddle()
    {
        var sim = Field();
        var rider = Human(sim, C(8, 10), weapon: "Bow");
        var dragon = sim.SpawnCreature(sim.Defs.Creature("Dragon"), "Vesper", C(8, 10), ageDays: 14);
        sim.World.Get<Dragon>(dragon).Affinity = 2f;
        EggSystem.AssignKeeper(sim, dragon, rider);
        Array.Fill(sim.World.Get<Needs>(dragon).Values, 1f);
        RidingSystem.MountUp(sim, rider, dragon);
        var bandit = Human(sim, C(28, 10), Faction.Raider, "Club");
        sim.Combat.Start(sim);
        ToRaiders(sim);
        Walk(sim, bandit, C(22, 10));
        Assert.Equal(rider, sim.Combat.LastReaction.Reactor);
        Assert.Equal(C(8, 10), sim.World.Get<Position>(dragon).Cell);
    }

    [Fact]
    public void ArchersWithNothingToShootKeepTheirTuToReact()
    {
        var sim = Field();
        var archer = Human(sim, C(5, 10), weapon: "Bow");
        Human(sim, C(28, 10), Faction.Raider, "Club");
        sim.FillBlocks(new int3(7, 0, 1), new int3(7, 19, 3)); // walled in: no shot, nowhere to go
        sim.FillBlocks(new int3(3, 0, 1), new int3(3, 19, 3));
        sim.Combat.Start(sim);
        sim.Combat.VillageAuto = true;
        var unit = sim.Combat.Unit(archer)!;
        for (var i = 0; i < 20 && !unit.Acted; i++)
        {
            sim.Combat.FinishAnimation(sim);
            sim.Combat.StepAi(sim);
        }
        Assert.True(unit.Acted);
        Assert.True(Reactions.CanReact(sim, unit), $"{unit.TimeUnits} TU left");
    }

    [Fact]
    public void ReservingTuKeepsAReactionBack()
    {
        var sim = Field();
        var archer = Human(sim, C(5, 10), weapon: "Bow");
        Human(sim, C(5, 3), Faction.Raider, "Club"); // off to the side, within joining distance
        sim.Combat.Start(sim);
        var unit = sim.Combat.Unit(archer)!;
        var far = C(5 + (int)(unit.TimeUnits / TimeUnits.PerCell), 10); // the full pool's walk
        Assert.True(sim.Combat.RangeOf(sim, archer).CanEndAt(sim.Map, far));

        unit.ReserveForReactions = true;
        Assert.False(sim.Combat.RangeOf(sim, archer).CanEndAt(sim.Map, far));
        Assert.NotNull(sim.Combat.Move(sim, archer, far));
        var furthest = sim.Combat.RangeOf(sim, archer).Cells.Select(sim.Map.CellOf).Where(c => c.Y == 10).MaxBy(c => c.X);
        Walk(sim, archer, furthest);
        Assert.True(Reactions.CanReact(sim, unit));
    }

    // ---------------- Answering attacks ----------------

    private static void Strike(Simulation sim, Entity attacker, Entity target)
    {
        var attack = CombatRules.Attacks(sim, attacker)[0];
        Assert.NotNull(sim.Combat.Attack(sim, attacker, target, attack, out var error, fromAi: true));
        Assert.Null(error);
    }

    [Fact]
    public void AnAttackInReachIsAnsweredWithASwing()
    {
        var sim = Field();
        var axeman = Human(sim, C(10, 10), weapon: "Axe");
        var bandit = Human(sim, C(11, 10), Faction.Raider, "Club");
        sim.Combat.Start(sim);
        var unit = sim.Combat.Unit(axeman)!;
        ToRaiders(sim);
        var before = unit.TimeUnits;
        var predicted = Reactions.Answers(sim, sim.Combat, bandit, C(11, 10), axeman);
        Assert.Single(predicted, t => t.Reactor == axeman && t.Kind == ReactionKind.Response);

        Strike(sim, bandit, axeman);
        if (!sim.World.Get<Creature>(axeman).Able)
            return; // knocked flat by the blow itself: nothing to answer with
        Assert.Equal(1, sim.Combat.ReactionCount);
        Assert.Equal((axeman, bandit, ReactionKind.Response), sim.Combat.LastReaction);
        Assert.Contains(sim.Combat.Log, l => l.Text.Contains("answers the attack"));
        Assert.Equal(before - TimeUnits.AttackCost(sim, axeman, CombatRules.Attacks(sim, axeman)[0], ShotMode.Normal), unit.TimeUnits);
    }

    [Fact]
    public void OnlyTheOneAttackedAnswers()
    {
        var sim = Field();
        var archer = Human(sim, C(5, 10), weapon: "Bow");
        var friend = Human(sim, C(14, 10));
        var bandit = Human(sim, C(15, 10), Faction.Raider, "Club");
        sim.Combat.Start(sim);
        ToRaiders(sim);
        Assert.DoesNotContain(Reactions.Answers(sim, sim.Combat, bandit, C(15, 10), friend), t => t.Reactor == archer);
        Strike(sim, bandit, friend);
        Assert.DoesNotContain(sim.Combat.Log, l => l.Text.StartsWith("Villager answers the attack") && l.Text.Contains("shoots"));
        Assert.Equal(TimeUnits.PerTurn(sim, archer), sim.Combat.Unit(archer)!.TimeUnits); // kept its TU
    }

    [Fact]
    public void EachUnitAnswersAttacksOnItselfOncePerPhase()
    {
        var sim = Field();
        var axeman = Human(sim, C(14, 10), weapon: "Axe");
        var bandit = Human(sim, C(15, 10), Faction.Raider, "Club");
        sim.World.Get<Health>(axeman).Blood = 1f;
        sim.Combat.Start(sim);
        var unit = sim.Combat.Unit(axeman)!;
        ToRaiders(sim);
        Strike(sim, bandit, axeman);
        if (!sim.World.Get<Creature>(bandit).Able || !sim.World.Get<Creature>(axeman).Able)
            return;
        Assert.Equal(1, sim.Combat.ReactionCount);
        Assert.True(unit.ResponseUsed);
        Assert.Empty(Reactions.Answers(sim, sim.Combat, bandit, C(15, 10), axeman));
        Strike(sim, bandit, axeman);
        if (!sim.World.Get<Creature>(bandit).Able || !sim.World.Get<Creature>(axeman).Able)
            return;
        Assert.Equal(1, sim.Combat.ReactionCount); // TU left (60 - 21 = 39), but the phase's answer is spent
    }

    [Fact]
    public void NoTuNoAnswer()
    {
        var sim = Field();
        var axeman = Human(sim, C(10, 10), weapon: "Axe");
        var bandit = Human(sim, C(11, 10), Faction.Raider, "Club");
        sim.Combat.Start(sim);
        sim.Combat.Unit(axeman)!.TimeUnits = 5;
        ToRaiders(sim);
        Assert.Empty(Reactions.Answers(sim, sim.Combat, bandit, C(11, 10), axeman));
        Strike(sim, bandit, axeman);
        Assert.Equal(0, sim.Combat.ReactionCount);
    }

    [Fact]
    public void ReactionsAreDeterministic()
    {
        string Run()
        {
            var sim = Field(5);
            Human(sim, C(5, 10), weapon: "Bow");
            Human(sim, C(9, 12), weapon: "Axe");
            Human(sim, C(24, 10), Faction.Raider, "Club");
            Human(sim, C(24, 12), Faction.Raider, "ShortSword");
            sim.Combat.Start(sim);
            sim.Combat.Resolve(sim, maxRounds: 30);
            return string.Join("\n", sim.Combat.Log.Select(l => l.Text)) + sim.StateHash();
        }
        Assert.Equal(Run(), Run());
    }

    // ---------------- Bonded dragons ----------------

    /// <summary>A keeper at (10, 10) with their grown dragon beside them at the given trust, and a bandit with a club down the field.</summary>
    private static (Simulation Sim, Entity Keeper, Entity Dragon, Entity Bandit) Bonded(float affinity)
    {
        var sim = Field();
        var keeper = Human(sim, C(10, 10));
        var dragon = sim.SpawnCreature(sim.Defs.Creature("Dragon"), "Vesper", C(10, 11), ageDays: 16);
        Array.Fill(sim.World.Get<Needs>(dragon).Values, 1f);
        EggSystem.AssignKeeper(sim, dragon, keeper);
        sim.World.Get<Dragon>(dragon).Affinity = affinity;
        var bandit = Human(sim, C(24, 10), Faction.Raider, "Club");
        sim.Combat.Start(sim);
        return (sim, keeper, dragon, bandit);
    }

    [Fact]
    public void ADevotedDragonStrikesAtWhoeverStepsUpToItsKeeper()
    {
        var (sim, keeper, dragon, bandit) = Bonded(1.0f);
        Assert.True(Bonding.Reached(sim, dragon, Bonding.Devoted));
        ToRaiders(sim);
        var path = sim.Combat.RangeOf(sim, bandit).PathTo(sim.Map, C(11, 10));
        Assert.NotNull(path);
        Assert.Contains(Reactions.Threats(sim, sim.Combat, bandit, C(24, 10), path!), t => t.Kind == ReactionKind.Guard && t.Reactor == dragon);
        Walk(sim, bandit, C(11, 10));
        Assert.Equal(dragon, sim.Combat.LastReaction.Reactor);
        Assert.Equal(ReactionKind.Guard, sim.Combat.LastReaction.Kind);
        Assert.Contains(sim.Combat.Log, l => l.Text.Contains("guards Villager"));
    }

    [Fact]
    public void AMerelyTrustingDragonLetsThemCome()
    {
        var (sim, _, dragon, bandit) = Bonded(0.6f);
        ToRaiders(sim);
        Walk(sim, bandit, C(11, 10));
        // Stepping into its own reach still draws its overwatch swing, as anyone's; but it doesn't guard.
        Assert.False(sim.Combat.LastReaction.Reactor == dragon && sim.Combat.LastReaction.Kind == ReactionKind.Guard);
        Assert.DoesNotContain(sim.Combat.Log, l => l.Text.Contains("guards"));
    }

    [Fact]
    public void ASoulboundDragonAnswersAnAttackOnItsKeeperWithNoTuLeft()
    {
        var (sim, keeper, dragon, bandit) = Bonded(1.6f);
        sim.World.Get<Position>(bandit) = Position.At(C(11, 10));
        ToRaiders(sim);
        sim.Combat.Unit(dragon)!.TimeUnits = 0; // spent everything in its own turn
        var club = CombatRules.Attacks(sim, bandit).First(a => !a.Ranged);
        Assert.Contains(Reactions.Answers(sim, sim.Combat, bandit, C(11, 10), keeper), t => t.Kind == ReactionKind.Defend && t.Reactor == dragon);
        Assert.NotNull(sim.Combat.Attack(sim, bandit, keeper, club, out _, fromAi: true));
        Assert.Contains(sim.Combat.Log, l => l.Text.Contains("flies at whoever hurt Villager"));
        Assert.Equal(0, sim.Combat.Unit(dragon)!.TimeUnits); // fury costs nothing
    }

    [Fact]
    public void ADevotedDragonWithNoTuLeftOnlyWatches()
    {
        var (sim, keeper, dragon, bandit) = Bonded(1.0f);
        sim.World.Get<Position>(bandit) = Position.At(C(11, 10));
        ToRaiders(sim);
        sim.Combat.Unit(dragon)!.TimeUnits = 0;
        var club = CombatRules.Attacks(sim, bandit).First(a => !a.Ranged);
        sim.Combat.Attack(sim, bandit, keeper, club, out _, fromAi: true);
        Assert.DoesNotContain(sim.Combat.Log, l => l.Text.Contains("flies at"));
        // And attacking someone else doesn't rouse a soulbound one either.
        Assert.DoesNotContain(Reactions.Answers(sim, sim.Combat, bandit, C(11, 10), dragon), t => t.Kind == ReactionKind.Defend);
    }
}

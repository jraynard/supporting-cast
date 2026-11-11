using SupportingCast.Sim.Combat;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Tests;

public class CombatTests
{
    private static Simulation Field(int w = 40, int h = 20, int depth = 6, ulong seed = 1)
    {
        var sim = TestWorld.Create(w, h, seed, depth);
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

    private static Entity Dragon(Simulation sim, int3 at, float affinity = 2f)
    {
        var e = sim.SpawnCreature(sim.Defs.Creature("Dragon"), "Ember", at, ageDays: 5);
        Array.Fill(sim.World.Get<Needs>(e).Values, 1f);
        sim.World.Get<Dragon>(e).Affinity = affinity;
        return e;
    }

    private static AttackDef Weapon(Simulation sim, Entity e) => CombatRules.Attacks(sim, e)[0];

    // ---------------- Movement ----------------

    [Fact]
    public void MoveCostsTuByTheCellAndMoreWithLegWounds()
    {
        var sim = Field();
        var pawn = Human(sim, C(10, 10));
        var budget = TimeUnits.PerTurn(sim, pawn);
        Assert.Equal(60f, budget);
        Assert.Equal(TimeUnits.PerCell, TimeUnits.WalkCost(sim, pawn), 3); // a healthy human: the reference walker
        var range = TacticalMoves.Reachable(sim, pawn, budget);
        Assert.True(range.CanEndAt(sim.Map, C(25, 10)), "15 cells on open grass");
        Assert.False(range.CanEndAt(sim.Map, C(27, 10)));
        Assert.Equal(5 * TimeUnits.PerCell, range.CostTo(sim.Map, C(15, 10)), 2);
        Assert.All(range.Cells, i => Assert.True(range.CostTo(sim.Map, sim.Map.CellOf(i)) <= budget + 0.01f));

        var legs = sim.World.Get<Health>(pawn).Body;
        HealthSystem.Damage(sim, pawn, 20, DamageType.Cut, legs.PartIndex("LeftLeg"));
        Assert.True(TimeUnits.WalkCost(sim, pawn) > TimeUnits.PerCell);
        var hurt = TacticalMoves.Reachable(sim, pawn, TimeUnits.PerTurn(sim, pawn));
        Assert.True(hurt.Cells.Count() < range.Cells.Count() * 0.7f, $"{hurt.Cells.Count()} vs {range.Cells.Count()} cells");
    }

    [Fact]
    public void EnemiesBlockTheWayButFriendsDont()
    {
        var sim = Field();
        // A corridor along y = 10.
        sim.FillBlocks(new int3(0, 9, 1), new int3(39, 9, 2));
        sim.FillBlocks(new int3(0, 11, 1), new int3(39, 11, 2));
        var pawn = Human(sim, C(5, 10));
        var blocker = Human(sim, C(8, 10), Faction.Raider);
        var range = TacticalMoves.Reachable(sim, pawn, 60);
        Assert.False(range.CanEndAt(sim.Map, C(10, 10)));
        Assert.False(range.CanEndAt(sim.Map, C(8, 10)), "can't end in an occupied cell");

        sim.World.Get<Creature>(blocker).Faction = Faction.Village;
        range = TacticalMoves.Reachable(sim, pawn, 60);
        Assert.True(range.CanEndAt(sim.Map, C(10, 10)));
        Assert.False(range.CanEndAt(sim.Map, C(8, 10)));
    }

    [Fact]
    public void FlyersCrossWallsAndCanHover()
    {
        var sim = Field();
        sim.FillBlocks(new int3(12, 0, 1), new int3(12, 19, 2)); // a two-high rock wall across the map
        var dragon = Dragon(sim, C(10, 10));
        var walker = Human(sim, C(10, 12));
        Assert.False(TacticalMoves.Reachable(sim, walker, 60).CanEndAt(sim.Map, C(14, 10)));

        var range = TacticalMoves.Reachable(sim, dragon, TimeUnits.PerTurn(sim, dragon));
        Assert.True(range.CanEndAt(sim.Map, C(14, 10)), "should fly over the wall");
        Assert.Contains(range.Cells, i => TacticalMoves.Airborne(sim, sim.Map.CellOf(i)));
        var path = range.PathTo(sim.Map, C(14, 10))!;
        Assert.Contains(path, c => c.Z >= 3);
    }

    // ---------------- Time units ----------------

    [Fact]
    public void ActionsCostTuAndAUnitActsAsOftenAsTheyLast()
    {
        var sim = Field();
        var villager = Human(sim, C(10, 10), weapon: "Axe");
        var bandit = Human(sim, C(11, 10), Faction.Raider, "Club");
        sim.World.Get<Health>(bandit).Blood = 1f;
        sim.Combat.Start(sim);
        var unit = sim.Combat.Unit(villager)!;
        var axe = Weapon(sim, villager);
        var cost = TimeUnits.AttackCost(sim, villager, axe, ShotMode.Normal);
        Assert.Equal(MathF.Ceiling(60 * TimeUnits.Melee), cost);

        // Standing still, two swings fit in the turn; the third doesn't.
        Assert.NotNull(sim.Combat.Attack(sim, villager, bandit, axe, out _));
        Assert.Equal(60 - cost, unit.TimeUnits);
        if (sim.Combat.Active && sim.World.Get<Creature>(bandit).Able)
        {
            Assert.NotNull(sim.Combat.Attack(sim, villager, bandit, axe, out _));
            if (sim.Combat.Active && sim.World.Get<Creature>(bandit).Able)
            {
                Assert.Null(sim.Combat.Attack(sim, villager, bandit, axe, out var error));
                Assert.Contains("not enough time units", error);
            }
        }
        Assert.False(unit.Acted, "spending TU doesn't end the turn: ending it does");
    }

    [Fact]
    public void MovingThenActingSharesOnePool()
    {
        var sim = Field();
        var villager = Human(sim, C(5, 10), weapon: "Axe");
        var bandit = Human(sim, C(20, 10), Faction.Raider, "Club");
        sim.Combat.Start(sim);
        var unit = sim.Combat.Unit(villager)!;
        Assert.Null(sim.Combat.Move(sim, villager, C(10, 10)));
        sim.Combat.FinishAnimation(sim);
        Assert.Equal(60 - 5 * TimeUnits.PerCell, unit.TimeUnits, 2);
        Assert.Null(sim.Combat.Move(sim, villager, C(15, 10))); // and again
        sim.Combat.FinishAnimation(sim);
        Assert.Equal(60 - 10 * TimeUnits.PerCell, unit.TimeUnits, 2);
        // 20 TU left: not enough for a 21-TU swing, even once there.
        Assert.Null(sim.Combat.Move(sim, villager, C(19, 10)));
        sim.Combat.FinishAnimation(sim);
        Assert.Null(sim.Combat.Attack(sim, villager, bandit, Weapon(sim, villager), out var error));
        Assert.Contains("not enough time units", error);
    }

    [Fact]
    public void AimedShotsHitMoreOftenAndCostMoreThanSnapShots()
    {
        var sim = Field();
        var archer = Human(sim, C(5, 10), weapon: "Bow");
        var bandit = Human(sim, C(13, 10), Faction.Raider);
        var bow = Weapon(sim, archer);
        var snap = CombatRules.Chance(sim, archer, bandit, bow, ShotMode.Snap);
        var aimed = CombatRules.Chance(sim, archer, bandit, bow, ShotMode.Aimed);
        Assert.True(aimed.Chance > snap.Chance, $"{aimed.Chance:P0} vs {snap.Chance:P0}");
        Assert.True(TimeUnits.AttackCost(sim, archer, bow, ShotMode.Aimed) > TimeUnits.AttackCost(sim, archer, bow, ShotMode.Snap) * 1.5f);
        // Real-time hunting shoots at the bow's own accuracy, between the two.
        var plain = CombatRules.Chance(sim, archer, bandit, bow);
        Assert.InRange(plain.Chance, snap.Chance, aimed.Chance);

        sim.Combat.Start(sim);
        var unit = sim.Combat.Unit(archer)!;
        sim.Combat.Attack(sim, archer, bandit, bow, out _, mode: ShotMode.Aimed);
        Assert.Equal(60 - TimeUnits.AttackCost(sim, archer, bow, ShotMode.Aimed), unit.TimeUnits);
        Assert.Contains(sim.Combat.Log, l => l.Text.StartsWith("Aimed:"));
    }

    [Fact]
    public void ThingsInReachCanBePickedUpForTu()
    {
        var sim = Field();
        var villager = Human(sim, C(5, 10));
        Human(sim, C(20, 10), Faction.Raider, "Club");
        Entity Drop(string def, int count, int3 at)
        {
            var item = sim.CreateItem(sim.Thing(def), count);
            sim.PlaceItemNear(item, at);
            return item;
        }
        var club = Drop("Club", 1, C(6, 10));
        var stone = Drop("Stone", 3, C(5, 11));
        sim.SpawnItem(sim.Thing("Wood"), 3, C(9, 10)); // too far
        sim.Combat.Start(sim);
        var unit = sim.Combat.Unit(villager)!;
        var inReach = sim.Combat.ItemsInReach(sim, villager);
        Assert.Equal(2, inReach.Count);

        Assert.Null(sim.Combat.PickUp(sim, villager, club));
        Assert.Equal("Club", sim.World.Get<Thing>(sim.World.Get<Equipment>(villager).Weapon).Def.Name); // a weapon is wielded
        Assert.Equal(60 - TimeUnits.Share(sim, villager, TimeUnits.PickUp), unit.TimeUnits);
        Assert.Null(sim.Combat.PickUp(sim, villager, stone));
        Assert.Equal("Stone", sim.World.Get<Thing>(sim.World.Get<Carrying>(villager).Item).Def.Name);

        unit.TimeUnits = 1;
        Assert.NotNull(sim.Combat.PickUp(sim, villager, Drop("Berries", 1, C(4, 10))));
    }

    [Fact]
    public void GearComesOutOfAChestForTuAndGoesWhereItBelongs()
    {
        var sim = Field();
        var villager = Human(sim, C(5, 10));
        Human(sim, C(20, 10), Faction.Raider, "Club");
        var chest = sim.SpawnStructure(sim.Thing("Chest"), C(6, 10));
        Entity Put(string def, int count)
        {
            var item = sim.CreateItem(sim.Thing(def), count);
            Assert.True(sim.Store(chest, item).IsNull);
            return item;
        }
        var sword = Put("ShortSword", 1);
        var satchel = Put("Satchel", 1);
        var berries = Put("Berries", 10);
        var bandages = Put("Bandage", 4);
        sim.Combat.Start(sim);
        var unit = sim.Combat.Unit(villager)!;
        var w = sim.World;
        Assert.Contains(chest, sim.Combat.ContainersInReach(sim, villager));
        var cost = TimeUnits.Share(sim, villager, TimeUnits.PickUp);

        Assert.Null(sim.Combat.TakeOut(sim, villager, sword));
        Assert.Equal("ShortSword", w.Get<Thing>(w.Get<Equipment>(villager).Weapon).Def.Name); // wielded
        Assert.Null(sim.Combat.TakeOut(sim, villager, berries));
        Assert.Equal("Berries", w.Get<Thing>(w.Get<Carrying>(villager).Item).Def.Name); // into empty hands
        // Hands full, no pack: refused, and it costs nothing (nothing in hand is dropped).
        var before = unit.TimeUnits;
        Assert.Equal("hands full (and no room in a pack)", sim.Combat.TakeOut(sim, villager, bandages));
        Assert.Equal(before, unit.TimeUnits);
        Assert.Null(sim.Combat.TakeOut(sim, villager, satchel));
        Assert.False(sim.PackOf(villager).IsNull); // worn on the back
        Assert.Null(sim.Combat.TakeOut(sim, villager, bandages)); // now into the pack
        Assert.Contains(sim.StoredIn(sim.PackOf(villager)), e => w.Get<Thing>(e).Def.Name == "Bandage");
        Assert.Equal(60 - 4 * cost, unit.TimeUnits);
        Assert.Empty(sim.StoredIn(chest));

        // A chest out of reach can't be emptied.
        var far = sim.SpawnStructure(sim.Thing("Chest"), C(9, 10));
        var stone = sim.CreateItem(sim.Thing("Stone"), 1);
        sim.Store(far, stone);
        Assert.DoesNotContain(far, sim.Combat.ContainersInReach(sim, villager));
        Assert.Equal("out of reach", sim.Combat.TakeOut(sim, villager, stone));
    }

    [Fact]
    public void ABodyBlocksItsCellUntilItsDraggedAside()
    {
        var sim = Field();
        var villager = Human(sim, C(5, 10));
        var bandit = Human(sim, C(6, 10), Faction.Raider, "Club");
        var other = Human(sim, C(20, 10), Faction.Raider, "Club");
        sim.Combat.Start(sim);
        HealthSystem.SetDowned(sim, bandit, ref sim.World.Get<Creature>(bandit), true);
        var unit = sim.Combat.Unit(villager)!;
        Assert.False(sim.Combat.RangeOf(sim, villager).CanEndAt(sim.Map, C(6, 10)), "can't stand on him");

        Assert.Contains(bandit, sim.Combat.BodiesInReach(sim, villager));
        var targets = sim.Combat.DragTargets(sim, villager, bandit);
        Assert.DoesNotContain(C(5, 10), targets); // not where the dragger stands
        Assert.Contains(C(6, 11), targets);
        Assert.NotNull(sim.Combat.Drag(sim, villager, bandit, C(9, 10))); // not beside him
        Assert.Null(sim.Combat.Drag(sim, villager, bandit, C(6, 11)));
        Assert.Equal(C(6, 11), sim.World.Get<Position>(bandit).Cell);
        Assert.Equal(60 - TimeUnits.Share(sim, villager, TimeUnits.Drag), unit.TimeUnits);
        Assert.True(sim.Combat.RangeOf(sim, villager).CanEndAt(sim.Map, C(6, 10)), "the way's clear");

        Assert.DoesNotContain(other, sim.Combat.BodiesInReach(sim, villager)); // the living (and the far) aren't bodies to drag
    }

    [Fact]
    public void AGrownDragonIsTooHeavyToDrag()
    {
        var sim = Field();
        var villager = Human(sim, C(5, 10));
        Human(sim, C(20, 10), Faction.Raider, "Club");
        var dragon = sim.SpawnCreature(sim.Defs.Creature("Dragon"), "Vesper", C(6, 10), ageDays: 16);
        sim.Combat.Start(sim);
        HealthSystem.SetDowned(sim, dragon, ref sim.World.Get<Creature>(dragon), true);
        Assert.DoesNotContain(dragon, sim.Combat.BodiesInReach(sim, villager));
        Assert.Equal("too heavy to drag", sim.Combat.Drag(sim, villager, dragon, C(6, 11)));
    }

    // ---------------- Attacks ----------------

    [Fact]
    public void HitChanceFallsWithDistanceAndCover()
    {
        var sim = Field();
        var archer = Human(sim, C(5, 10), weapon: "Bow");
        var near = Human(sim, C(9, 10), Faction.Raider);
        var far = Human(sim, C(17, 10), Faction.Raider);
        var bow = Weapon(sim, archer);
        var close = CombatRules.Chance(sim, archer, near, bow);
        var distant = CombatRules.Chance(sim, archer, far, bow);
        Assert.True(close.Chance > distant.Chance + 0.15f, $"{close.Chance:P0} vs {distant.Chance:P0}");
        Assert.False(CombatRules.Chance(sim, archer, Human(sim, C(25, 10), Faction.Raider), bow).Possible, "beyond the bow's range");

        sim.SpawnStructure(sim.Thing("Boulder"), C(8, 10));
        sim.RebuildDerivedIfNeeded();
        var covered = CombatRules.Chance(sim, archer, near, bow);
        Assert.True(covered.Cover > 0.3f);
        Assert.True(covered.Chance < close.Chance - 0.25f);
    }

    [Fact]
    public void HighGroundHelpsAndWallsBlock()
    {
        var sim = Field();
        sim.FillBlocks(new int3(4, 9, 1), new int3(6, 11, 2)); // a mound; stand on top at z = 3
        var raider = Human(sim, C(12, 10), Faction.Raider);
        var up = Human(sim, new int3(5, 10, 3), weapon: "Bow");
        var level = Human(sim, C(5, 14), weapon: "Bow");
        var bow = Weapon(sim, up);
        Assert.True(CombatRules.Chance(sim, up, raider, bow).Height > 0);
        Assert.True(CombatRules.Chance(sim, up, raider, bow).Chance > CombatRules.Chance(sim, level, raider, bow).Chance);

        sim.FillBlocks(new int3(9, 12, 1), new int3(9, 16, 3)); // a rock wall between the level archer and the raider
        var blocked = CombatRules.Chance(sim, level, raider, bow);
        Assert.False(blocked.Possible);
        Assert.Equal("no line of fire", blocked.Blocked);
    }

    [Fact]
    public void MeleeCantReachAHoveringDragon()
    {
        var sim = Field();
        var raider = Human(sim, C(10, 10), Faction.Raider, "Club");
        var dragon = Dragon(sim, new int3(11, 10, 2)); // one level up, in the air
        Assert.True(TacticalMoves.Airborne(sim, new int3(11, 10, 2)));
        var odds = CombatRules.Chance(sim, raider, dragon, Weapon(sim, raider));
        Assert.False(odds.Possible);

        var archer = Human(sim, C(4, 10), Faction.Raider, "Bow");
        Assert.True(CombatRules.Chance(sim, archer, dragon, Weapon(sim, archer)).Possible, "arrows can");
    }

    [Fact]
    public void ScalesTurnWeakBlows()
    {
        var sim = Field();
        var raider = Human(sim, C(10, 10), Faction.Raider, "Club");
        var club = Weapon(sim, raider);
        var human = CombatRules.DamageChance(sim, Human(sim, C(11, 10)), club);
        var dragon = CombatRules.DamageChance(sim, Dragon(sim, C(11, 11)), club);
        Assert.True(dragon < human - 0.1f, $"club vs dragon {dragon:P0}, vs human {human:P0}");
    }

    [Fact]
    public void ResolvedHitsLeaveInjuries()
    {
        var sim = Field();
        var axeman = Human(sim, C(10, 10), weapon: "Axe");
        var raider = Human(sim, C(11, 10), Faction.Raider);
        var axe = Weapon(sim, axeman);
        var outcomes = new List<AttackOutcome>();
        for (var i = 0; i < 40 && !sim.World.Get<Creature>(raider).Dead; i++)
            outcomes.Add(CombatRules.Resolve(sim, axeman, raider, axe).Outcome);
        Assert.Contains(AttackOutcome.Hit, outcomes);
        Assert.Contains(AttackOutcome.Miss, outcomes);
        Assert.Contains(sim.World.Get<Health>(raider).Injuries, i => i.Type == DamageType.Cut);
    }

    // ---------------- Encounters ----------------

    [Fact]
    public void CombatStartsWhenBanditsComeInSight()
    {
        var sim = Field(60);
        var villager = Human(sim, C(5, 10), weapon: "Axe");
        var bandit = Human(sim, C(50, 10), Faction.Raider, "Club");
        for (var t = 0; t < SimClock.TicksPerHour && !sim.Combat.Active; t++)
            sim.Tick();
        Assert.True(sim.Combat.Active);
        Assert.Equal(Side.Village, sim.Combat.Phase);
        Assert.NotNull(sim.Combat.Unit(villager));
        Assert.NotNull(sim.Combat.Unit(bandit));
        var d = CombatRules.Distance(sim.World.Get<Position>(villager).Cell, sim.World.Get<Position>(bandit).Cell);
        Assert.InRange(d, 10, Encounter.DetectRadius + 1);
    }

    [Fact]
    public void PhasesAlternateAndOnlyRoundsMoveTheClock()
    {
        var sim = Field();
        var villager = Human(sim, C(5, 10), weapon: "Bow");
        Human(sim, C(15, 10), Faction.Raider, "Club");
        sim.Combat.Start(sim);
        var tick = sim.Clock.Tick;

        Assert.Null(sim.Combat.Move(sim, villager, C(3, 10)));
        sim.Combat.FinishAnimation(sim);
        Assert.Equal(C(3, 10), sim.World.Get<Position>(villager).Cell);
        Assert.Equal(tick, sim.Clock.Tick);
        Assert.NotNull(sim.Combat.Move(sim, villager, C(15, 15))); // too far now

        sim.Combat.Wait(sim, villager);
        sim.Combat.EndPhase(sim);
        Assert.Equal(Side.Raiders, sim.Combat.Phase);
        while (sim.Combat.Active && sim.Combat.Phase == Side.Raiders)
        {
            sim.Combat.FinishAnimation(sim);
            sim.Combat.StepAi(sim);
        }
        Assert.Equal(2, sim.Combat.Round);
        Assert.Equal(tick + Encounter.RoundTicks, sim.Clock.Tick);
    }

    [Fact]
    public void HoveringDragonStaysUpBetweenRounds()
    {
        var sim = Field();
        var dragon = Dragon(sim, C(5, 10));
        Human(sim, C(15, 10), Faction.Raider, "Club");
        sim.Combat.Start(sim);
        var hover = new int3(5, 10, 3);
        Assert.Null(sim.Combat.Move(sim, dragon, hover));
        sim.Combat.FinishAnimation(sim);
        sim.Combat.Wait(sim, dragon);
        sim.Combat.EndPhase(sim);
        sim.Combat.EndPhase(sim);
        Assert.Equal(hover, sim.World.Get<Position>(dragon).Cell);
    }

    [Fact]
    public void ADragonThatRefusesActsOnInstinct()
    {
        // Obedience is never below a coin flip, so some seed refuses the first order.
        for (ulong seed = 1; seed < 40; seed++)
        {
            var sim = Field(seed: seed);
            var dragon = Dragon(sim, C(5, 10), affinity: 0f);
            var bandit = Human(sim, C(8, 10), Faction.Raider, "Club");
            sim.Combat.Start(sim);
            var refusal = sim.Combat.Move(sim, dragon, C(5, 14));
            if (refusal == null)
                continue;
            Assert.Contains("ignores the order", refusal);
            var unit = sim.Combat.Unit(dragon)!;
            Assert.True(unit.Instinct);
            for (var i = 0; i < 2 * Tactics.MaxStepsPerTurn && !unit.Acted; i++)
            {
                sim.Combat.FinishAnimation(sim);
                sim.Combat.StepAi(sim);
            }
            Assert.True(unit.Acted, "the dragon should take its own turn");
            Assert.Equal(Side.Village, sim.Combat.Phase); // still the player's phase for everyone else
            return;
        }
        Assert.Fail("no seed refused an order at obedience 0.5");
    }

    [Fact]
    public void CombatEndsWhenTheRaidersAreDown()
    {
        var sim = Field();
        var villager = Human(sim, C(5, 10), weapon: "Axe");
        var bandit = Human(sim, C(15, 10), Faction.Raider, "Club");
        sim.Combat.Start(sim);
        sim.World.Get<Health>(bandit).Blood = 0.3f; // lost too much to stand
        HealthSystem.SetDowned(sim, bandit, ref sim.World.Get<Creature>(bandit), true);
        sim.Combat.Wait(sim, villager);
        sim.Combat.EndPhase(sim);
        sim.Combat.EndPhase(sim);
        Assert.False(sim.Combat.Active);
        Assert.Contains("beaten", sim.Combat.Outcome);
        Assert.True(sim.World.Has<Leaving>(bandit));
        Assert.False(sim.World.Get<Creature>(bandit).Dead);
    }
}

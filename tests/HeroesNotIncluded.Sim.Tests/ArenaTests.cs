using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Scenarios;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>The practice ring (<see cref="Arena"/>): bouts start both fighters fresh, one foe at a time.</summary>
public class ArenaTests
{
    private static (Simulation Sim, Entity Me) Ring()
    {
        var sim = Arena.NewSimulation(TestWorld.LoadDefs(), 7);
        var center = Arena.Setup(sim);
        var me = VillageRound.SpawnHero(sim, new Hero("Arin", null, null), center);
        return (sim, me);
    }

    private static List<Entity> Foes(Simulation sim) =>
        sim.Creatures().Where(e => sim.World.Get<Creature>(e).Faction == Faction.Raider).ToList();

    [Fact]
    public void TheRingIsEmptyButForTheTwoOfYou()
    {
        var (sim, me) = Ring();
        Arena.Bout(sim, me, "Swordsman");
        Assert.Equal(2, sim.Creatures().Count());
        Assert.DoesNotContain(sim.World.Query<Thing>(), e => sim.World.Get<Thing>(e).Def.Category == Defs.ThingCategory.Item && sim.World.Get<Thing>(e).Spawned);
    }

    [Fact]
    public void ABoutArmsYouAndSendsOneFoeAcrossTheRing()
    {
        var (sim, me) = Ring();
        var foe = Arena.Bout(sim, me, "Swordsman", "IronSpear");
        Assert.False(foe.IsNull);
        Assert.Single(Foes(sim));
        Assert.Equal("IronSpear", sim.World.Get<Thing>(sim.World.Get<Equipment>(me).Weapon).Def.Name);
        var d = Maps.Map.Distance(sim.World.Get<Position>(me).Cell, sim.World.Get<Position>(foe).Cell);
        Assert.True(d >= 3, $"the foe starts {d} cells off");
        Assert.True(Arena.Bout(sim, me, "NoSuchKind").IsNull);
    }

    [Fact]
    public void TheFoeComesForYouAndYouDoNothingYouDidntAskFor()
    {
        var (sim, me) = Ring();
        Arena.Bout(sim, me, "Swordsman");
        var hurt = false;
        for (var t = 0; t < SimClock.TicksPerSecond * 30 && !hurt; t++)
        {
            sim.Tick();
            Assert.Null(sim.World.Get<Mind>(me).Job);
            hurt = sim.World.Get<Health>(me).Injuries.Count > 0 || sim.RecentBlows.Any(b => b.Target == me);
        }
        Assert.True(hurt, "the foe came at you");
    }

    [Fact]
    public void ANewBoutStartsYouFreshAndTheLastFoeGone()
    {
        var (sim, me) = Ring();
        var first = Arena.Bout(sim, me, "Swordsman");
        for (var t = 0; t < SimClock.TicksPerSecond * 60 && sim.World.Get<Creature>(me).Able; t++)
            sim.Tick();
        Blows.GuardOf(sim, me).Stamina = 0.2f;
        var second = Arena.Bout(sim, me, "Brawler");
        Assert.False(sim.World.IsAlive(first));
        Assert.Equal(second, Assert.Single(Foes(sim)));
        Assert.Empty(sim.World.Get<Health>(me).Injuries);
        Assert.True(sim.World.Get<Creature>(me).Able);
        Assert.Equal(1f, sim.World.Get<Guard>(me).Stamina);
    }

    [Fact]
    public void AnArcherKeepsItsDistanceAndShoots()
    {
        var (sim, me) = Ring();
        var archer = Arena.Bout(sim, me, "Archer");
        BlowEvent? shot = null;
        for (var t = 0; t < SimClock.TicksPerSecond * 30 && shot == null; t++)
        {
            sim.Tick();
            shot = sim.RecentBlows.FirstOrDefault(b => b.Attacker == archer && b.Target == me);
        }
        Assert.NotNull(shot);
        Assert.True(Maps.Map.Distance(sim.World.Get<Position>(archer).Cell, sim.World.Get<Position>(me).Cell) > 1, "it shot from off");
    }

    [Fact]
    public void ADragonCanBeBroughtToTheRing()
    {
        var (sim, me) = Ring();
        Assert.Contains(Arena.Dragon, Arena.Foes(sim.Defs));
        var dragon = Arena.Bout(sim, me, Arena.Dragon);
        Assert.True(Blows.IsDragon(sim, dragon));
        Arena.Bout(sim, me, "Swordsman");
        Assert.False(sim.World.IsAlive(dragon), "the next bout sends it off");
    }
}

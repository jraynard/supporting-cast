using DragonWorld.Sim.Ai;
using DragonWorld.Sim.Combat;
using DragonWorld.Sim.Components;
using DragonWorld.Sim.Core;
using DragonWorld.Sim.Scenarios;
using DragonWorld.Sim.Systems;
using static DragonWorld.Sim.Tests.TestWorld;

namespace DragonWorld.Sim.Tests;

/// <summary>Saddles on dragons (<see cref="Saddles"/>): putting one on, what it does, and where it goes.</summary>
public class SaddleTests
{
    private static (Simulation Sim, Entity Keeper, Entity Dragon) Setup(float ageDays = 20)
    {
        var sim = Create(40, 40);
        sim.Climate.BaseTemperature = 20;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 20f);
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), "Wren", C(10, 10), ageDays: 25 * 365);
        sim.SetSkills(me);
        var dragon = Companions.SpawnBondedDragon(sim, me, "Ashwing", null, C(14, 10), ageDays);
        sim.World.Get<Dragon>(dragon).Affinity = 0.9f;
        return (sim, me, dragon);
    }

    private static Entity SaddleAt(Simulation sim, int3 at)
    {
        sim.SpawnItem(sim.Thing("Saddle"), 1, at);
        return sim.Map.Item[sim.Map.Idx(at)];
    }

    private static bool TickUntil(Simulation sim, Func<bool> done, int maxTicks)
    {
        for (var t = 0; t < maxTicks && !done(); t++)
            sim.Tick();
        return done();
    }

    [Fact]
    public void TheKeeperFetchesASaddleAndBucklesItOn()
    {
        var (sim, me, dragon) = Setup();
        var saddle = SaddleAt(sim, C(6, 12));
        sim.ApplyNow(new SaddleCommand(dragon, saddle));
        Assert.IsType<SaddleJob>(sim.World.Get<Mind>(me).Job);
        Assert.True(TickUntil(sim, () => Saddles.Of(sim, dragon) == saddle, SimClock.HoursToTicks(1)), "the dragon should be saddled");
        Assert.False(sim.World.Get<Thing>(saddle).Spawned);
        Assert.False(sim.World.Get<Carrying>(me).HasItem);
        Assert.Equal(sim.World.Get<Position>(dragon).Cell, sim.LocationOf(saddle));

        // And off again: a villager sets it down when the job's done (a player's character would keep it in hand).
        sim.ApplyNow(new SaddleCommand(dragon, null));
        Assert.True(TickUntil(sim, () => Saddles.Of(sim, dragon).IsNull, SimClock.HoursToTicks(1)));
        sim.Tick();
        Assert.True(sim.World.Get<Thing>(saddle).Spawned);
    }

    [Fact]
    public void OnlyAGrownDragonWearsOne()
    {
        var (sim, me, young) = Setup(ageDays: 6);
        var saddle = SaddleAt(sim, C(6, 12));
        Assert.NotNull(Saddles.CantWear(sim, young, saddle));
        Assert.NotNull(Saddles.CantWear(sim, me, saddle));
        sim.ApplyNow(new SaddleCommand(young, saddle));
        Assert.False(sim.World.Get<Mind>(me).Job is SaddleJob);
    }

    [Fact]
    public void ASaddledRiderAimsTruerAndTheScalesUnderItAreTougher()
    {
        var (sim, me, dragon) = Setup();
        sim.Equip(me, sim.Thing("Bow"));
        var deer = sim.SpawnCreature(sim.Defs.Creature("Deer"), "Deer", C(24, 10), Faction.Wild);
        RidingSystem.MountUp(sim, me, dragon);
        var bow = CombatRules.Attacks(sim, me).First(a => a.Ranged);
        var bareback = CombatRules.Chance(sim, me, deer, bow).Chance;
        var torso = sim.World.Get<Health>(dragon).Body.Parts.FindIndex(p => p.Name == "Torso");
        var scales = CombatRules.ArmorAt(sim, dragon, torso, Defs.DamageType.Cut).Armor;

        var saddle = sim.CreateItem(sim.Thing("Saddle"), 1);
        sim.World.Remove<Mount>(dragon); // CantWear refuses a ridden dragon: buckle it on, then climb back on
        sim.World.Remove<Riding>(me);
        Saddles.PutOn(sim, dragon, saddle);
        RidingSystem.MountUp(sim, me, dragon);
        var saddled = CombatRules.Chance(sim, me, deer, bow).Chance;
        Assert.True(saddled > bareback + 0.05f, $"{bareback} → {saddled}");
        Assert.True(CombatRules.ArmorAt(sim, dragon, torso, Defs.DamageType.Cut).Armor > scales);
    }

    [Fact]
    public void SaddlebagsCarryALoadNobodyElseTakesAndItAllDropsIfTheDragonDies()
    {
        var (sim, _, dragon) = Setup();
        var saddle = sim.CreateItem(sim.Thing("Saddle"), 1);
        Saddles.PutOn(sim, dragon, saddle);
        var meat = sim.CreateItem(sim.Thing("Meat"), 20);
        Assert.True(sim.Store(saddle, meat).IsNull);
        Assert.False(sim.IsAvailable(meat), "a saddlebag is like a pack: not up for grabs");
        Assert.Equal(sim.World.Get<Position>(dragon).Cell, sim.LocationOf(meat));

        NeedsSystem.Kill(sim, dragon, ref sim.World.Get<Creature>(dragon));
        Assert.True(sim.World.Get<Thing>(saddle).Spawned);
        Assert.Contains(meat, sim.StoredIn(saddle));
    }

    [Fact]
    public void TheSaddleKeepsThroughASaveAndAFlight()
    {
        var (sim, me, dragon) = Setup();
        var saddle = sim.CreateItem(sim.Thing("Saddle"), 1);
        Saddles.PutOn(sim, dragon, saddle);
        sim.Store(saddle, sim.CreateItem(sim.Thing("Berries"), 7));
        var (loaded, _) = Saving.SaveGame.Read(Saving.SaveGame.Write(sim), sim.Defs);
        Assert.Equal(saddle, Saddles.Of(loaded, dragon));

        sim.World.Add(me, new Avatar());
        var party = Travel.Pack(sim, me, Entity.Null, Travel.FlightHours);
        Assert.Equal("Saddle", party.Dragon!.Saddle!.Value.Def);
        var home = Simulation.CreateStandard(sim.Defs, 60, 60, 5);
        var arrived = Travel.Arrive(home, party);
        var newDragon = Companions.DragonOf(home, arrived);
        var newSaddle = Saddles.Of(home, newDragon);
        Assert.False(newSaddle.IsNull);
        Assert.Equal(7, home.StoredIn(newSaddle).Sum(e => home.World.Get<Stack>(e).Count));
    }
}

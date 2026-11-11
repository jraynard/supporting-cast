using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Core;
using SupportingCast.Sim.Scenarios;
using SupportingCast.Sim.Systems;
using static SupportingCast.Sim.Tests.TestWorld;

namespace SupportingCast.Sim.Tests;

/// <summary>Rescuing downed dragons (<see cref="Rescues"/>): carried to a nest, or fed and tended where they lie.</summary>
public class RescueTests
{
    private static Simulation Mild()
    {
        var sim = Create(40, 40);
        sim.Climate.BaseTemperature = 20;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 20f);
        return sim;
    }

    private static Entity Person(Simulation sim, int3 at, string name, bool avatar = false)
    {
        var e = sim.SpawnCreature(sim.Defs.Creature("Human"), name, at, ageDays: 25 * 365);
        sim.SetSkills(e);
        if (avatar)
            sim.World.Add(e, new Avatar());
        return e;
    }

    /// <summary>Down a creature and keep it down a while: low on blood.</summary>
    private static void Down(Simulation sim, Entity e)
    {
        sim.World.Get<Health>(e).Blood = 0.4f;
        HealthSystem.SetDowned(sim, e, ref sim.World.Get<Creature>(e), true);
    }

    private static bool TickUntil(Simulation sim, Func<bool> done, int maxTicks)
    {
        for (var t = 0; t < maxTicks && !done(); t++)
            sim.Tick();
        return done();
    }

    [Fact]
    public void ADoctorCarriesADownedJuvenileToItsNest()
    {
        var sim = Mild();
        var doc = Person(sim, C(5, 5), "Hild");
        var keeper = Person(sim, C(6, 5), "Wren");
        var young = Companions.SpawnBondedDragon(sim, keeper, "Kit", null, C(25, 20), ageDays: 6);
        var nest = sim.SpawnStructure(sim.Thing("Nest"), C(10, 30));
        Down(sim, young);
        Assert.True(Rescues.CanCarry(sim, doc, young));

        Assert.True(TickUntil(sim, () => HealthSystem.InBed(sim, young), SimClock.HoursToTicks(2)), "the juvenile should be laid in the nest");
        Assert.Equal(nest, sim.Map.Structure[sim.Map.Idx(sim.World.Get<Position>(young).Cell)]);
    }

    [Fact]
    public void ADownedAdultIsTooBigToCarryButIsFedWhereItLiesAndMendsFasterByAFire()
    {
        var sim = Mild();
        var doc = Person(sim, C(5, 5), "Hild");
        var keeper = Person(sim, C(6, 5), "Wren");
        var adult = Companions.SpawnBondedDragon(sim, keeper, "Ashwing", null, C(25, 20));
        sim.SpawnStructure(sim.Thing("Nest"), C(10, 30));
        sim.SpawnItem(sim.Thing("Meat"), 30, C(5, 10));
        var food = sim.Defs.Need("Food");
        sim.World.Get<Needs>(adult).Values[food.Index] = 0.2f;
        Down(sim, adult);
        Assert.False(Rescues.CanCarry(sim, doc, adult));

        Assert.True(TickUntil(sim, () => sim.World.Get<Needs>(adult).Get(food) > 0.3f, SimClock.HoursToTicks(2)), "the doctor should bring it meat");
        Assert.False(HealthSystem.InBed(sim, adult));
    }

    [Fact]
    public void ALitFireBesideADownedDragonSpeedsItsWounds()
    {
        float Healed(bool fire)
        {
            var sim = Mild();
            var keeper = Person(sim, C(6, 5), "Wren");
            var adult = Companions.SpawnBondedDragon(sim, keeper, "Ashwing", null, C(25, 20));
            HealthSystem.Damage(sim, adult, 20, Defs.DamageType.Cut, sim.World.Get<Health>(adult).Body.Parts.FindIndex(p => p.Name == "Torso"));
            Down(sim, adult);
            if (fire)
            {
                var campfire = sim.SpawnStructure(sim.Thing("Campfire"), C(26, 21));
                sim.World.Get<Fueled>(campfire).Fuel = 1000;
            }
            var before = sim.World.Get<Health>(adult).Injuries.Sum(i => i.Damage);
            sim.TickMany(SimClock.HoursToTicks(6));
            return before - sim.World.Get<Health>(adult).Injuries.Sum(i => i.Damage);
        }
        var cold = Healed(false);
        var warm = Healed(true);
        Assert.True(warm > cold * 1.15f, $"{cold} by itself, {warm} by the fire");
    }

    [Fact]
    public void ADevotedDragonCarriesItsDownedKeeperToBed()
    {
        var sim = Mild();
        var keeper = Person(sim, C(30, 30), "Wren");
        var dragon = Companions.SpawnBondedDragon(sim, keeper, "Ashwing", null, C(28, 30));
        sim.World.Get<Dragon>(dragon).Affinity = 0.95f;
        Assert.True(Bonding.Reached(sim, dragon, Bonding.Devoted));
        var bed = sim.SpawnStructure(sim.Thing("Bed"), C(6, 6));
        Down(sim, keeper);

        Assert.True(TickUntil(sim, () => HealthSystem.InBed(sim, keeper), SimClock.HoursToTicks(2)), "the dragon should bring its keeper home");
        Assert.Equal(bed, sim.Map.Structure[sim.Map.Idx(sim.World.Get<Position>(keeper).Cell)]);
    }

    [Fact]
    public void ThePlayerPicksUpAHatchlingAndLaysItInTheNest()
    {
        var sim = Mild();
        var me = Person(sim, C(10, 10), "Wren", avatar: true);
        var kit = Companions.SpawnBondedDragon(sim, me, "Kit", null, C(11, 10), ageDays: 1);
        var nest = sim.SpawnStructure(sim.Thing("Nest"), C(10, 14));
        Down(sim, kit);

        var pick = AvatarSystem.ActionAt(sim, me, C(11, 10));
        Assert.Equal(AvatarActionKind.Carry, pick.Kind);
        sim.ApplyNow(new AvatarWorkCommand(me, true, C(11, 10)));
        Assert.Equal(kit, Rescues.CarriedBy(sim, me));
        sim.ApplyNow(new AvatarWorkCommand(me, false));

        // It stays in your arms while you walk (jobs ending don't drop it).
        sim.World.Get<Position>(me) = Position.At(C(10, 12));
        AiSystem.InterruptAll(sim);
        sim.Tick();
        Assert.Equal(kit, Rescues.CarriedBy(sim, me));
        var (loaded, _) = Saving.SaveGame.Read(Saving.SaveGame.Write(sim), sim.Defs);
        Assert.Equal(kit, Rescues.CarriedBy(loaded, me));

        var lay = AvatarSystem.ActionAt(sim, me, C(10, 13));
        Assert.Equal(AvatarActionKind.Carry, lay.Kind);
        Assert.Contains("nest", lay.Label);
        sim.ApplyNow(new AvatarWorkCommand(me, true, C(10, 13)));
        Assert.True(Rescues.CarriedBy(sim, me).IsNull);
        Assert.True(HealthSystem.InBed(sim, kit));
        Assert.Equal(nest, sim.Map.Structure[sim.Map.Idx(sim.World.Get<Position>(kit).Cell)]);
    }

    [Fact]
    public void ThePlayerCarriesACarcassToTheButcherSpotAndDropsItIfTheyGoDown()
    {
        var sim = Mild();
        var me = Person(sim, C(10, 10), "Wren", avatar: true);
        sim.SpawnStructure(sim.Thing("ButcherSpot"), C(10, 16));
        var deer = sim.SpawnCreature(sim.Defs.Creature("Deer"), "Deer", C(11, 10), Faction.Wild);
        NeedsSystem.Kill(sim, deer, ref sim.World.Get<Creature>(deer));

        Assert.Equal(AvatarActionKind.Carry, AvatarSystem.ActionAt(sim, me, C(11, 10)).Kind);
        sim.ApplyNow(new AvatarWorkCommand(me, true, C(11, 10)));
        Assert.Equal(deer, Rescues.CarriedBy(sim, me));

        sim.World.Get<Position>(me) = Position.At(C(10, 14));
        var setDown = AvatarSystem.ActionAt(sim, me, C(10, 15));
        Assert.Contains("butcher spot", setDown.Label);

        Down(sim, me);
        sim.Tick();
        Assert.True(Rescues.CarriedBy(sim, me).IsNull, "going down drops what's in your arms");
        Assert.Equal(C(10, 14), sim.World.Get<Position>(deer).Cell);
    }
}

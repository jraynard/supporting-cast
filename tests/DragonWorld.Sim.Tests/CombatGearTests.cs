using DragonWorld.Sim.Combat;
using DragonWorld.Sim.Components;
using DragonWorld.Sim.Core;

namespace DragonWorld.Sim.Tests;

/// <summary>Handling gear mid-fight: letting go of what's in hand, and drawing another weapon from the pack.</summary>
public class CombatGearTests
{
    private static (Simulation Sim, Entity Me) Fight(string? weapon = "Club")
    {
        var sim = TestWorld.Create(40, 20, 1, 6);
        sim.Climate.BaseTemperature = 20;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 20f);
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), "Arin", C(10, 10), ageDays: 25 * 365);
        Array.Fill(sim.World.Get<Needs>(me).Values, 1f);
        sim.SetSkills(me);
        if (weapon != null)
            sim.Equip(me, sim.Thing(weapon));
        sim.WearItem(me, sim.CreateItem(sim.Thing("LeatherPack"), 1));
        var bandit = sim.SpawnCreature(sim.Defs.Creature("Human"), "Bandit", C(30, 10), Faction.Raider, ageDays: 25 * 365);
        Array.Fill(sim.World.Get<Needs>(bandit).Values, 1f);
        sim.Combat.Start(sim);
        Assert.Equal(Side.Village, sim.Combat.Phase);
        return (sim, me);
    }

    [Fact]
    public void WhatsInHandCanBeDroppedToFreeTheHands()
    {
        var (sim, me) = Fight();
        var key = sim.CreateItem(sim.Thing("Stone"), 1);
        sim.World.Get<Carrying>(me).Item = key;
        var before = sim.Combat.Unit(me)!.TimeUnits;

        sim.ApplyNow(new CombatDropCommand(me));

        Assert.True(sim.World.Get<Carrying>(me).Item.IsNull);
        Assert.True(sim.World.Get<Thing>(key).Spawned, "it's on the ground");
        Assert.True(Math.Max(Math.Abs(sim.World.Get<Thing>(key).Cell.X - 10), Math.Abs(sim.World.Get<Thing>(key).Cell.Y - 10)) <= 1, "at your feet");
        Assert.Equal(before - TimeUnits.Share(sim, me, TimeUnits.Drop), sim.Combat.Unit(me)!.TimeUnits);
        // Nothing left to drop.
        Assert.Equal("nothing in hand", sim.Combat.DropHeld(sim, me));
    }

    [Fact]
    public void AWeaponInThePackCanBeDrawnInsteadOfTheOneWielded()
    {
        var (sim, me) = Fight("Club");
        var w = sim.World;
        var club = w.Get<Equipment>(me).Weapon;
        var sword = sim.CreateItem(sim.Thing("ShortSword"), 1);
        Assert.True(sim.Store(sim.PackOf(me), sword).IsNull);
        Assert.Contains(sword, sim.Combat.WeaponsToHand(sim, me));
        var before = sim.Combat.Unit(me)!.TimeUnits;

        sim.ApplyNow(new CombatWieldCommand(me, sword));

        Assert.Equal(sword, w.Get<Equipment>(me).Weapon);
        Assert.Contains(club, sim.StoredIn(sim.PackOf(me))); // the club is put away
        Assert.Contains(CombatRules.Attacks(sim, me), a => a.Label == sim.Thing("ShortSword").Weapon!.Label);
        Assert.Equal(before - TimeUnits.Share(sim, me, TimeUnits.Wield), sim.Combat.Unit(me)!.TimeUnits);
        Assert.Contains(sim.Combat.Log, l => l.Text.Contains("puts away the") && l.Text.Contains("draws the"));
    }

    [Fact]
    public void ASwordTakenIntoTheHandsCanBeWielded()
    {
        // Wielding a club already, a sword taken from a chest lands in the hands: draw it from there.
        var (sim, me) = Fight("Club");
        var w = sim.World;
        var sword = sim.CreateItem(sim.Thing("ShortSword"), 1);
        w.Get<Carrying>(me).Item = sword;
        sim.ApplyNow(new CombatWieldCommand(me, sword));
        Assert.Equal(sword, w.Get<Equipment>(me).Weapon);
        Assert.NotEqual(sword, w.Get<Carrying>(me).Item);
    }

    [Fact]
    public void NotEnoughTimeUnitsAndYouKeepWhatYouHave()
    {
        var (sim, me) = Fight("Club");
        var w = sim.World;
        var club = w.Get<Equipment>(me).Weapon;
        var sword = sim.CreateItem(sim.Thing("ShortSword"), 1);
        sim.Store(sim.PackOf(me), sword);
        sim.Combat.Unit(me)!.TimeUnits = 1;
        Assert.NotNull(sim.Combat.Wield(sim, me, sword));
        Assert.Equal(club, w.Get<Equipment>(me).Weapon);
        // And a weapon somewhere else (lying about, someone else's) isn't to hand.
        var other = sim.CreateItem(sim.Thing("ShortSword"), 1);
        sim.PlaceItemNear(other, C(20, 10));
        sim.Combat.Unit(me)!.TimeUnits = 60;
        Assert.Equal("not to hand", sim.Combat.Wield(sim, me, other));
    }
}

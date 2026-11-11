using DragonWorld.Sim.Components;
using DragonWorld.Sim.Saving;
using DragonWorld.Sim.Scenarios;
using DragonWorld.Sim.Systems;

namespace DragonWorld.Sim.Tests;

/// <summary>Dragon breeds (docs/plans/dragon-types.md): the Hearth dragon and the Norse one, from eggs, starts and saves.</summary>
public class BreedTests
{
    [Fact]
    public void TheBreedsAreTheDragonDefsInCreatorOrder()
    {
        var defs = TestWorld.LoadDefs();
        var breeds = Dragons.Breeds(defs).Select(b => b.Name).ToList();
        Assert.Equal(["Dragon", "NorseDragon"], breeds);
        Assert.Equal("Dragon", Dragons.DefaultBreed(defs).Name);
        Assert.Equal("Hearth dragon", defs.Creature("Dragon").Breed!.Label);
        Assert.Null(defs.Creature("Human").Breed);
        // Anything that isn't a breed (a hostile peer's human, an unknown name) is the default.
        Assert.Equal("Dragon", Dragons.BreedOrDefault(defs, defs.Creature("Human")).Name);
        Assert.Equal("Dragon", Dragons.BreedOrDefault(defs, "NoSuchDragon").Name);
        Assert.Equal("NorseDragon", Dragons.BreedOrDefault(defs, "NorseDragon").Name);
    }

    [Fact]
    public void TheNorseBreedIsTougherButSlowerThanTheHearthDragon()
    {
        var defs = TestWorld.LoadDefs();
        var (hearth, norse) = (defs.Creature("Dragon"), defs.Creature("NorseDragon"));
        Assert.Equal(3, norse.LifeStages.Count);
        Assert.True(norse.MaxHp > hearth.MaxHp);
        Assert.True(norse.MoveSpeed < hearth.MoveSpeed);
        Assert.True(norse.LifeStages[^1].Armor > hearth.LifeStages[^1].Armor);
        Assert.True(norse.LifeStages[^1].Size > hearth.LifeStages[^1].Size);
        Assert.All(norse.LifeStages, s => Assert.StartsWith("creatures/dragon_norse_", s.Sprite));
        // A Norse adult still outflies a deer, so it can feed itself on the wing (DragonHunting).
        var adult = norse.LifeStages[^1];
        Assert.True(norse.MoveSpeed * adult.SpeedFactor * adult.FlySpeedFactor > defs.Creature("Deer").MoveSpeed);
    }

    [Fact]
    public void AChosenEggHatchesItsBreedAndElement()
    {
        var sim = TestWorld.Create(20, 20);
        sim.Climate.BaseTemperature = 40f;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 40f);
        var keeper = sim.SpawnCreature(sim.Defs.Creature("Human"), "Tester", C(12, 12));
        sim.World.Get<Mind>(keeper).Drafted = true;
        var egg = sim.CreateItem(sim.Thing("DragonEgg"), 1);
        Dragons.ChooseEgg(sim, egg, sim.Defs.Creature("NorseDragon"), DragonElement.Ice);
        sim.PlaceItemNear(egg, C(10, 10));

        sim.TickMany(SimClock.DaysToTicks(1.6f));

        var dragon = Assert.Single(sim.World.Query<Dragon>());
        Assert.Equal("NorseDragon", sim.World.Get<Creature>(dragon).Def.Name);
        Assert.Equal(DragonElement.Ice, sim.World.Get<Dragon>(dragon).Element);
        Assert.Equal(keeper, sim.World.Get<Dragon>(dragon).Keeper);
    }

    [Fact]
    public void AnEggNobodyChoseHatchesAFireHearthDragon()
    {
        var sim = TestWorld.Create(20, 20);
        var egg = sim.CreateItem(sim.Thing("DragonEgg"), 1);
        Assert.Null(sim.World.Get<Egg>(egg).Breed);
        Assert.Equal(DragonElement.Fire, sim.World.Get<Egg>(egg).Element);
    }

    [Fact]
    public void ANorsePartnerAndAChosenEggSurviveASave()
    {
        var sim = TestWorld.Create(24, 24);
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), "Arin", C(10, 10));
        var dragon = Companions.SpawnBondedDragon(sim, me, "Ashwing", DragonLooks(DragonElement.Lightning), C(12, 10), breed: sim.Defs.Creature("NorseDragon"));
        var egg = sim.CreateItem(sim.Thing("DragonEgg"), 1);
        Dragons.ChooseEgg(sim, egg, sim.Defs.Creature("NorseDragon"), DragonElement.Poison);
        sim.PlaceItemNear(egg, C(5, 5));
        // A few days into a Norse adulthood (its adults start later than the Hearth dragon's).
        Assert.Equal(Companions.StartingAgeDays(sim.Defs.Creature("NorseDragon")), sim.World.Get<Creature>(dragon).AgeDays(sim.Clock.Tick), 2);
        Assert.Equal(DragonElement.Lightning, sim.World.Get<Dragon>(dragon).Element);

        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);

        Assert.Equal("NorseDragon", loaded.World.Get<Creature>(dragon).Def.Name);
        Assert.Equal(DragonElement.Lightning, loaded.World.Get<Dragon>(dragon).Element);
        Assert.Equal("NorseDragon", loaded.World.Get<Egg>(egg).Breed!.Name);
        Assert.Equal(DragonElement.Poison, loaded.World.Get<Egg>(egg).Element);
        Assert.Equal(sim.FullHash(), loaded.FullHash());
    }

    [Fact]
    public void ANewcomersEggIsTheOneTheyPicked()
    {
        var sim = TestWorld.Create(30, 30);
        var player = Guid.NewGuid();
        sim.ApplyNow(new CreateCharacterCommand(player, "Bea", default, [], "", DragonLooks(DragonElement.Nature), DragonBreed: sim.Defs.Creature("NorseDragon")));
        var me = Players.CharacterOf(sim, player);
        var egg = sim.World.Get<Carrying>(me).Item;
        Assert.Equal("NorseDragon", sim.World.Get<Egg>(egg).Breed!.Name);
        Assert.Equal(DragonElement.Nature, sim.World.Get<Egg>(egg).Element);
    }

    [Fact]
    public void AHostileBreedIsTheDefault()
    {
        var sim = TestWorld.Create(30, 30);
        var player = Guid.NewGuid();
        sim.ApplyNow(new CreateCharacterCommand(player, "Bea", default, [], DragonBreed: sim.Defs.Creature("Human")));
        var egg = sim.World.Get<Carrying>(Players.CharacterOf(sim, player)).Item;
        Assert.Equal("Dragon", sim.World.Get<Egg>(egg).Breed!.Name);
    }

    [Fact]
    public void ANorseAdultTakesMoreToBringDown()
    {
        // The same beating (deterministic blows to the torso) leaves the Norse adult healthier.
        float After(string breed)
        {
            var sim = TestWorld.Create(20, 20);
            var def = sim.Defs.Creature(breed);
            var d = sim.SpawnCreature(def, "Test", C(10, 10), ageDays: Dragons.AdultAgeDays(def) + 1);
            var torso = sim.World.Get<Health>(d).Body.Parts.FindIndex(p => p.Name == "Torso");
            for (var i = 0; i < 4; i++) // 80 in all: most of a Hearth torso (90), not a Norse one (120)
                HealthSystem.Damage(sim, d, 20f, Defs.DamageType.Cut, torso);
            return sim.World.Get<Health>(d).PartHealth(torso);
        }
        Assert.True(After("NorseDragon") > After("Dragon"));
    }

    private static DragonLook DragonLooks(DragonElement element) => new() { Element = element, Scales = 0x4A86C8, Belly = 0xD2E8F6, Wings = 0x2A4F8A, Horns = 0x2E2A2E, Eyes = 0x9AE4FF };
}

using HeroesNotIncluded.Sim.Bots;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// What keeps the town in its makings without the carter for everything: planks for what's made, charcoal baked
/// at the smithy's kiln from firewood (<see cref="Defs.TradeDef.Keeps"/>), cloth at the shrine for bandages.
/// </summary>
public class MaterialSupplyTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    private static (Simulation Sim, Entity Me) Round(string trade, string map = VillageRound.DefaultMap)
    {
        var sim = VillageRound.NewSimulation(TestWorld.LoadDefs(), 1234, map);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim, map);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default));
        sim.ApplyNow(new SetTradePreferencesCommand(Ana, [sim.Defs.Trade(trade)], []));
        sim.ApplyNow(new StartRoundCommand());
        return (sim, Players.CharacterOf(sim, Ana));
    }

    private static Entity Part(Simulation sim, Entity building, string def) =>
        Buildings.Parts(sim, building).First(e => sim.World.Get<Thing>(e).Def.Name == def);

    private static int Charcoal(Simulation sim, Entity bin) => Stock.CountIn(sim, bin, "Charcoal");

    [Fact]
    public void TheSmithyHasBronzeAndTheCopperAndTinToAlloyMore()
    {
        // A playtest: adventurers asked for bronze pieces and the smith had none to make them from.
        var (sim, me) = Round("Blacksmith");
        var smithy = Shops.ShopSystem.WorkplaceOf(sim, me);
        var chest = Part(sim, smithy, "Chest");
        Assert.Equal(4, Stock.CountIn(sim, chest, "BronzeBar"));
        Assert.Equal(8, Stock.CountIn(sim, chest, "CopperOre"));
        Assert.Equal(4, Stock.CountIn(sim, chest, "TinOre"));
        // The carter's morning load brings more copper and tin with the iron.
        OreCarterSystem.Deliver(sim);
        Assert.Equal(8 + 6, Stock.CountIn(sim, chest, "CopperOre"));
        Assert.Equal(4 + 2, Stock.CountIn(sim, chest, "TinOre"));
        // The ores handled, copper's smelted and bronze alloyed at the smithy's kiln, which charcoal brings hot enough.
        RecipeBooks.Discover(sim, me, "handle:CopperOre");
        RecipeBooks.Discover(sim, me, "handle:TinOre");
        var kiln = sim.World.Query<Thing>().First(e => sim.World.Get<Thing>(e).Def.Name == "Kiln" && Crafting.KeptAt(sim, e) == smithy);
        foreach (var name in new[] { "SmeltCopper", "AlloyBronze" })
        {
            var recipe = sim.Defs.Recipe(name);
            Assert.True(Crafting.CanMake(sim, me, recipe), name);
            Assert.Contains(recipe, sim.World.Get<Thing>(kiln).Def.Recipes);
            Assert.True(sim.World.Get<Thing>(kiln).Def.Building!.Fuel!.OptionFor(sim.Defs.Thing("Charcoal"))!.Reach >= recipe.MinTemp, $"{name}: hot enough on charcoal");
        }
    }

    [Fact]
    public void CraftedThingsTakePlanksAndTimberIsForBuilding()
    {
        var defs = TestWorld.LoadDefs();
        var timber = defs.Thing(Stock.TimberDef);
        // Weapons, tools and contraptions made at a station: never raw timber (stone tools knapped by hand may).
        foreach (var recipe in defs.AllRecipes.Where(r => r.Station.Length > 0 && r.Products.Exists(p => p.ThingDef.Weapon != null || p.ThingDef.Tool != null || p.ThingDef.Contraption != null)))
            Assert.DoesNotContain(recipe.Ingredients, x => x.ThingDef == timber);
        Assert.Contains(defs.Recipe("ForgeIronSpear").Ingredients, x => x.Def == Stock.PlanksDef);
        Assert.Contains(defs.Thing("Table").Building!.Cost, c => c.Def == Stock.PlanksDef);
        Assert.Contains(defs.Thing("WoodWall").Building!.Cost, c => c.Def == Stock.TimberDef); // building stays timber
        Assert.Contains(defs.Recipe("BakeCharcoal").Ingredients, x => x.Def == Stock.FirewoodDef);
        Assert.DoesNotContain(defs.Season.OreDelivery, t => t.Def == "Charcoal"); // the kiln makes it now
    }

    [Theory]
    [InlineData(VillageRound.DefaultMap)]
    [InlineData(ReedmereMapTests.Map)]
    public void EachSmithyHasAKilnInItsYardAndTheTownStartsWithPlanksAndCloth(string map)
    {
        var (sim, _) = Round("Empath", map);
        var w = sim.World;
        var smithy = ShopSystem.BuildingFor(sim, Defs.TownRole.Forge);
        var kiln = w.Query<Thing>().Single(e => w.Get<Thing>(e).Def.Name == "Kiln");
        Assert.Equal(smithy, Crafting.KeptAt(sim, kiln));
        Assert.True(Crafting.IsStation(sim, kiln));
        Assert.Equal(VillageRound.SmithyPlanks, Stock.CountIn(sim, Stock.ChestOf(sim, smithy), Stock.PlanksDef));
        var store = VillageRound.ShrineStore(sim, ShopSystem.BuildingFor(sim, Defs.TownRole.Shrine));
        Assert.Equal(sim.Defs.Season.ShrineCloth, Stock.CountIn(sim, store, Bandaging.ClothDef));
        Assert.Contains(sim.AvailableItems(), e => w.Get<Thing>(e).Def.Name == Stock.PlanksDef); // the barn's
    }

    [Fact]
    public void AnAiSmithBakesCharcoalAtTheKilnWhileTheBinIsLowThenStops()
    {
        var (sim, _) = Round("Empath");
        var w = sim.World;
        var smithy = ShopSystem.BuildingFor(sim, Defs.TownRole.Forge);
        var bin = Part(sim, smithy, "FuelBin");
        Assert.True(Stock.TakeFrom(sim, bin, "Charcoal", sim.Defs.Season.SmithyCharcoal));
        var keep = sim.Defs.Trade("Blacksmith").Keeps.Single();
        var kiln = w.Query<Thing>().Single(e => w.Get<Thing>(e).Def.Name == "Kiln");
        Assert.True(Crafting.Short(sim, kiln, keep));
        Assert.True(Crafting.WantsFiring(sim, kiln), "short, it's what lights the kiln");

        for (var h = 0; h < 16 && Crafting.Short(sim, kiln, keep); h++)
            sim.TickMany(SimClock.TicksPerHour);
        var log = string.Join("\n", sim.Messages.Messages.TakeLast(20).Select(m => m.Text));
        Assert.False(Crafting.Short(sim, kiln, keep), $"the smith kept the charcoal in:\n{log}");

        // Kept in: nothing more's begun, and the kiln goes cold.
        sim.TickMany(SimClock.TicksPerHour * 2);
        var made = sim.SpawnedItems().Concat(sim.StoredIn(bin)).Where(e => w.Get<Thing>(e).Def.Name == "Charcoal").Sum(e => w.Get<Stack>(e).Count);
        sim.TickMany(SimClock.TicksPerHour * 2);
        Assert.Null(w.Get<Workbench>(kiln).Active);
        Assert.False(Crafting.WantsFiring(sim, kiln));
        Assert.InRange(made, keep.Below, keep.Below + keep.RecipeDef.Products[0].Count * 2);
    }

    [Fact]
    public void APlayerSmithBakesTheirOwn()
    {
        var (sim, me) = Round("Blacksmith");
        var smithy = ShopSystem.BuildingFor(sim, Defs.TownRole.Forge);
        var bin = Part(sim, smithy, "FuelBin");
        Stock.TakeFrom(sim, bin, "Charcoal", sim.Defs.Season.SmithyCharcoal);
        var kiln = sim.World.Query<Thing>().Single(e => sim.World.Get<Thing>(e).Def.Name == "Kiln");
        Assert.False(Crafting.WantsFiring(sim, kiln), "no AI smith to keep it in");
        Assert.Null(Crafting.KeepOf(sim, me, kiln, sim.Defs.Recipe("BakeCharcoal")));
    }

    [Fact]
    public void TheBotSmithBakesCharcoalAndPutsItInTheBin()
    {
        var (sim, me) = Round("Blacksmith");
        var bot = new BotPlayer(() => sim, Ana, c => sim.ApplyNow(c), 7) { Temper = BotTemperament.Steady };
        var smithy = ShopSystem.BuildingFor(sim, Defs.TownRole.Forge);
        var bin = Part(sim, smithy, "FuelBin");
        Stock.TakeFrom(sim, bin, "Charcoal", sim.Defs.Season.SmithyCharcoal - 2);
        for (var t = 0; t < SimClock.TicksPerHour * 4 && Charcoal(sim, bin) <= 2; t++)
        {
            sim.Tick();
            bot.Tick();
        }
        Assert.True(Charcoal(sim, bin) > 2, $"bot: {bot.Doing}");
    }

    [Fact]
    public void TheCarterBringsClothToTheShrineButNotOverABrokenBridge()
    {
        var (sim, _) = Round("Tinker");
        var store = VillageRound.ShrineStore(sim, ShopSystem.BuildingFor(sim, Defs.TownRole.Shrine));
        var cloth = sim.Defs.Season.ShrineDelivery.Single(t => t.Def == Bandaging.ClothDef).Count;
        OreCarterSystem.Deliver(sim);
        Assert.Equal(sim.Defs.Season.ShrineCloth + cloth, Stock.CountIn(sim, store, Bandaging.ClothDef));

        // With the bridge down, it's left only where the carter can still get to from the road (as the ore is).
        Assert.NotNull(sim.Repairs.BreakBridge(sim, new SimRandom(3)));
        sim.RebuildDerivedIfNeeded();
        var cut = OreCarterSystem.CutOff(sim, sim.World.Get<Thing>(store).Cell);
        OreCarterSystem.DeliverToShrine(sim);
        Assert.Equal(sim.Defs.Season.ShrineCloth + cloth * (cut ? 1 : 2), Stock.CountIn(sim, store, Bandaging.ClothDef));
        Assert.Equal(cut, sim.Messages.Messages.Any(m => m.Text.StartsWith("The carter couldn't get the cloth through to the shrine")));
    }

    [Fact]
    public void TheBotEmpathTearsBandagesFromTheShrinesCloth()
    {
        var (sim, me) = Round("Empath");
        var w = sim.World;
        var bot = new BotPlayer(() => sim, Ana, c => sim.ApplyNow(c), 7) { Temper = BotTemperament.Steady };
        var store = VillageRound.ShrineStore(sim, ShopSystem.BuildingFor(sim, Defs.TownRole.Shrine));
        // None left anywhere it'd look: the basket's and its own gone, the cloth still there.
        Stock.TakeFrom(sim, store, Bandaging.BandageDef, Stock.CountIn(sim, store, Bandaging.BandageDef));
        foreach (var e in sim.StoredIn(sim.PackOf(me)).Where(e => w.Get<Thing>(e).Def.Name == Bandaging.BandageDef).ToList())
            sim.DestroyThing(e);
        w.Get<Mana>(me).Current = w.Get<Mana>(me).Max; // with mana to spare, it wants linen for a charm
        for (var t = 0; t < SimClock.TicksPerHour * 3 && BotPlayer.OnMe(sim, me, Bandaging.BandageDef) == 0; t++)
        {
            sim.Tick();
            bot.Tick();
        }
        Assert.True(BotPlayer.OnMe(sim, me, Bandaging.BandageDef) > 0, $"bot: {bot.Doing} at {sim.Clock.HourOfDay} small={ShopSystem.SmallHours(sim)} job={w.Get<Mind>(me).Job?.GetType().Name} cloth={Stock.CountIn(sim, store, Bandaging.ClothDef)} mana={w.Get<Mana>(me).Current} live={sim.Round.Live}");
        Assert.True(Stock.CountIn(sim, store, Bandaging.ClothDef) < sim.Defs.Season.ShrineCloth, "torn from the shrine's cloth");
    }
}

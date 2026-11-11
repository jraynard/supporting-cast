using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Maps;
using SupportingCast.Sim.Saving;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Tests;

/// <summary>
/// The crafting chain past the first benches: recipe books and discovery, leather and cooking,
/// kilns and the dragon-stoked forge, quality, tools, mending, hand recipes and shed scales.
/// </summary>
public class CraftingProgressionTests
{
    private static Simulation Warm()
    {
        var sim = TestWorld.Create();
        sim.Climate.BaseTemperature = 20;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 20f);
        return sim;
    }

    private static Entity Villager(Simulation sim, int3 at, int skill = Skills.Average, params WorkType[] only)
    {
        var e = sim.SpawnCreature(sim.Defs.Creature("Human"), "Smith", at, ageDays: 25 * 365);
        Array.Fill(sim.World.Get<Needs>(e).Values, 1f);
        sim.SetSkills(e, skill);
        var mind = sim.World.Get<Mind>(e);
        Array.Fill(mind.WorkPriority, 0);
        foreach (var w in only)
            mind.WorkPriority[(int)w] = 1;
        return e;
    }

    private static Entity Avatar(Simulation sim, int3 at)
    {
        var e = sim.SpawnCreature(sim.Defs.Creature("Human"), "Bearer", at, ageDays: 25 * 365);
        Array.Fill(sim.World.Get<Needs>(e).Values, 1f);
        sim.SetSkills(e);
        sim.World.Add(e, new Avatar());
        sim.GiveStartingPack(e);
        return e;
    }

    private static Entity Station(Simulation sim, string def, int3 at)
    {
        var e = sim.SpawnStructure(sim.Thing(def), at);
        sim.RebuildDerivedIfNeeded();
        return e;
    }

    private static void Bill(Simulation sim, Entity station, string recipe, BillMode mode = BillMode.Count, int target = 1) =>
        sim.ApplyNow(new AddBillCommand(station, sim.Defs.Recipe(recipe), mode, target));

    private static int Count(Simulation sim, string def) =>
        sim.World.Query<Thing>().Where(e => sim.World.Get<Thing>(e).Def.Name == def)
            .Sum(e => sim.World.TryGet<Stack>(e, out var s) ? s.Count : 1);

    /// <summary>Keep everyone fed and rested while a long test runs.</summary>
    private static void TickFed(Simulation sim, int ticks)
    {
        for (var t = 0; t < ticks; t += 60)
        {
            foreach (var e in sim.World.Query<Needs>())
                Array.Fill(sim.World.Get<Needs>(e).Values, 1f);
            sim.TickMany(60);
        }
    }

    // ---------------- Recipe books ----------------

    [Fact]
    public void SkillUnlocksFollowTheVillageAndThePlayersOwnLevel()
    {
        var sim = Warm();
        var bow = sim.Defs.Recipe("MakeBow");
        var novice = Villager(sim, C(5, 5), 2);
        Assert.False(RecipeBooks.KnownToVillage(sim, bow));
        Assert.False(RecipeBooks.Knows(sim, novice, bow));

        var expert = Villager(sim, C(6, 5), 6);
        Assert.True(RecipeBooks.KnownToVillage(sim, bow));
        Assert.True(RecipeBooks.Knows(sim, novice, bow)); // companions craft from the village's book

        var me = Avatar(sim, C(7, 5));
        sim.SetSkills(me, 2);
        Assert.False(RecipeBooks.Knows(sim, me, bow)); // the player goes by their own
        sim.World.Get<Skills>(me).Xp[(int)Skill.Crafting] = 0;
        sim.World.Get<Skills>(me).Level[(int)Skill.Crafting] = 3;
        SkillRules.Learn(sim, me, Skill.Crafting, Skills.XpToNext(3));
        Assert.True(RecipeBooks.Knows(sim, me, bow));
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("worked out how to make hunting bow"));
        _ = expert;
    }

    [Fact]
    public void HandlingAHideTeachesTanningAndCompanionsPassItOn()
    {
        var sim = Warm();
        var tan = sim.Defs.Recipe("TanHides");
        var me = Avatar(sim, C(10, 10));
        var hauler = Villager(sim, C(4, 4), only: WorkType.Haul);
        sim.SpawnItem(sim.Thing("Hide"), 2, C(5, 6));
        sim.Map.SetZone(sim.Map.Idx(C(20, 20)), 1);
        sim.TickMany(SimClock.TicksPerHour);

        Assert.True(RecipeBooks.KnownToVillage(sim, tan)); // the hauler carried it
        Assert.False(RecipeBooks.Knows(sim, me, tan));   // but the player hasn't seen one
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("New recipe: leather"));

        // A companion who knows it can show the player; the interaction is only possible then.
        var teach = sim.Defs.Interaction("TeachCraft");
        Assert.Same(sim.Defs.Recipe("TanHides"), RecipeBooks.Teachable(sim, hauler, me));
        for (var i = 0; i < 3; i++)
            MoodSystem.Remember(sim, hauler, sim.Defs.Thought("DeepTalk"), me); // on good terms, whatever their temperaments
        Assert.True(SocialSystem.Weight(sim, hauler, me, teach) > 0);
        sim.Social.Interact(sim, hauler, me, teach);
        Assert.True(RecipeBooks.Knows(sim, me, tan));
        // One recipe a talk: the cap, jerkin and satchel (also found by handling hides) come next.
        var taught = 1;
        while (RecipeBooks.Teachable(sim, hauler, me) != null && taught < 10)
        {
            sim.Social.Interact(sim, hauler, me, teach);
            taught++;
        }
        Assert.Equal(4, taught);
        Assert.Null(RecipeBooks.Teachable(sim, hauler, me));
        Assert.Equal(0f, SocialSystem.Weight(sim, hauler, me, teach)); // nothing left to teach

        // Books save and load.
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        Assert.True(RecipeBooks.KnownToVillage(loaded, tan));
        Assert.True(RecipeBooks.Knows(loaded, me, tan));
    }

    [Fact]
    public void BuildingAStationCanBeADiscovery()
    {
        const string bench = """
            { "things": [ { "name": "Bench", "category": "Building", "building": { "workbench": {}, "cost": [] } } ],
              "recipes": [ { "name": "Thing", "station": "Bench", "unlock": { "discover": "built:Bench" },
                             "ingredients": [ { "def": "Wood" } ], "products": [ { "def": "Stone" } ] } ] }
            """;
        var files = Directory.EnumerateFiles(TestWorld.FindDefsDirectory(), "*.json", SearchOption.AllDirectories).Order().Select(File.ReadAllText);
        var defs = DefDatabase.FromJson([.. files, bench]);
        var sim = Simulation.CreateStandard(defs, 20, 20, 1);
        var r = defs.Recipe("Thing");
        Assert.False(RecipeBooks.KnownToVillage(sim, r));
        sim.CompleteConstruction(sim.PlaceBlueprint(defs.Thing("Bench"), C(5, 5)));
        Assert.True(RecipeBooks.KnownToVillage(sim, r));
        Assert.Throws<Exception>(() => DefDatabase.FromJson(
            """{ "things": [ { "name": "Rock", "category": "Item", "mass": 1 } ], "recipes": [ { "name": "R", "unlock": { "discover": "seen:Rock" }, "ingredients": [ { "def": "Rock" } ], "products": [ { "def": "Rock" } ] } ] }"""));
    }

    // ---------------- Leather and cooking ----------------

    [Fact]
    public void ButcheringGivesHidesAndASkilledButcherGetsMore()
    {
        int Hides(int skill)
        {
            var sim = Warm();
            Villager(sim, C(5, 5), skill, WorkType.Hunt);
            var deer = sim.SpawnCreature(sim.Defs.Creature("Deer"), "deer", C(10, 10), ageDays: 400);
            ref var c = ref sim.World.Get<Creature>(deer);
            NeedsSystem.Kill(sim, deer, ref c, "shot");
            sim.RebuildDerivedIfNeeded();
            sim.TickMany(SimClock.TicksPerHour);
            Assert.False(sim.World.IsAlive(deer));
            return Count(sim, "Hide") * 100 + Count(sim, "Meat");
        }
        var novice = Hides(0);
        var master = Hides(14);
        Assert.Equal(2, novice / 100);
        Assert.True(master / 100 >= 3);
        Assert.True(master % 100 > novice % 100);
    }

    [Fact]
    public void ALitFireCooksMealsThatPeopleReachForFirst()
    {
        var sim = Warm();
        var cook = Villager(sim, C(5, 5), only: WorkType.Cook);
        var fire = Station(sim, "Campfire", C(12, 12));
        sim.World.Get<Fueled>(fire).Fuel = 0;
        sim.SpawnItem(sim.Thing("Meat"), 6, C(6, 8));
        sim.SpawnItem(sim.Thing("Berries"), 5, C(5, 7));
        Bill(sim, fire, "CookMeat", target: 2);

        TickFed(sim, SimClock.TicksPerHour);
        Assert.Equal(0, Count(sim, "SimpleMeal")); // not lit

        sim.World.Get<Fueled>(fire).Fuel = 10;
        TickFed(sim, SimClock.TicksPerHour);
        Assert.Equal(4, Count(sim, "SimpleMeal"));

        sim.World.Get<Mind>(cook).WorkPriority[(int)WorkType.Cook] = 0;
        var food = sim.Defs.Need("Food");
        sim.World.Get<Needs>(cook).Values[food.Index] = 0.2f;
        sim.TickMany(SimClock.TicksPerHour / 2);
        Assert.Equal(5, Count(sim, "Berries"));
        Assert.True(Count(sim, "SimpleMeal") < 4);
        Assert.Contains(sim.World.Get<Mood>(cook).Memories, m => m.Def.Name == "AteCookedMeal");
    }

    // ---------------- Kilns and forges ----------------

    [Fact]
    public void AKilnOnlyBurnsWhenThereIsWorkAndBakesCharcoal()
    {
        var sim = Warm();
        var worker = Villager(sim, C(5, 5), only: [WorkType.Craft, WorkType.Refuel]);
        var kiln = Station(sim, "Kiln", C(12, 12));
        sim.World.Get<Fueled>(kiln).Fuel = 20;
        sim.SpawnItem(sim.Thing("Wood"), 30, C(6, 8));

        TickFed(sim, SimClock.TicksPerHour * 2);
        Assert.Equal(20f, sim.World.Get<Fueled>(kiln).Fuel); // nothing to make: cold and unburned
        Assert.False(sim.Burning(kiln));
        Assert.True(sim.TemperatureAt(C(12, 12)) < 30);

        Bill(sim, kiln, "BakeCharcoal", target: 2);
        TickFed(sim, SimClock.TicksPerHour * 4);
        Assert.Equal(10, Count(sim, "Charcoal"));
        Assert.True(sim.World.Get<Fueled>(kiln).Fuel < 20);
        // The stonework keeps the heat in: standing beside it is warm, not burning.
        Assert.True(sim.World.Get<Creature>(worker).Hp >= sim.World.Get<Creature>(worker).Def.MaxHp - 0.01f);
    }

    [Fact]
    public void ADragonStokesTheForgeToIronHeat()
    {
        float Run(bool withDragon)
        {
            var sim = Warm();
            var forge = Station(sim, "Forge", C(20, 20));
            sim.World.Get<Fueled>(forge).Fuel = 20;
            sim.ApplyNow(new AddBillCommand(forge, sim.Defs.Recipe("SmeltIron"), BillMode.Count, 1));
            if (withDragon)
            {
                var d = sim.SpawnCreature(sim.Defs.Creature("Dragon"), "Ember", C(14, 20), ageDays: 6);
                sim.World.Get<Dragon>(d).Affinity = 2f;
                var work = sim.World.Get<Mind>(d).WorkPriority;
                Array.Fill(work, 0);
                work[(int)WorkType.Stoke] = 1;
            }
            for (var t = 0; t < SimClock.HoursToTicks(12); t += 60)
            {
                foreach (var e in sim.World.Query<Needs>())
                    Array.Fill(sim.World.Get<Needs>(e).Values, 1f);
                sim.TickMany(60);
                if (sim.TemperatureAt(C(20, 20)) >= 1100)
                {
                    if (withDragon)
                        Assert.True(sim.World.Has<Stoked>(forge));
                    return (float)sim.Clock.Tick / SimClock.TicksPerHour;
                }
            }
            return float.PositiveInfinity;
        }
        var alone = Run(false);
        var stoked = Run(true);
        Assert.True(stoked < 2f, $"a dragon got it to 1100°C in {stoked:F1} h");
        Assert.True(stoked * 2 < alone, $"stoked {stoked:F1} h, charcoal alone {alone:F1} h");
    }

    // ---------------- Quality, tools, mending ----------------

    [Fact]
    public void CraftedGearHasAQualityThatScalesArmor()
    {
        var sim = Warm();
        var maker = Villager(sim, C(5, 5), 6, WorkType.Craft);
        var rack = Station(sim, "TanningRack", C(12, 12));
        RecipeBooks.Discover(sim, maker, "handle:Hide");
        sim.SpawnItem(sim.Thing("Leather"), 30, C(6, 8));
        Bill(sim, rack, "MakeLeatherCap", BillMode.Count, 5);
        sim.World.Get<Mind>(maker).WorkPriority[(int)WorkType.Craft] = 1;
        sim.World.Add(maker, new Outfit { BareHead = true }); // keep them from wearing the caps
        TickFed(sim, SimClock.TicksPerHour * 6);

        var caps = sim.World.Query<Crafted>().Where(e => sim.World.Get<Thing>(e).Def.Name == "LeatherCap").ToList();
        Assert.Equal(5, caps.Count);
        Assert.All(caps, e => Assert.Equal(maker, sim.World.Get<Crafted>(e).Maker));

        // Quality scales armor: put a fine cap and a crude one on two people and compare.
        var a = Villager(sim, C(20, 20));
        var b = Villager(sim, C(22, 20));
        var fine = sim.CreateItem(sim.Thing("LeatherCap"), 1);
        sim.World.Add(fine, new Crafted { Quality = Quality.Fine, Maker = maker });
        var crude = sim.CreateItem(sim.Thing("LeatherCap"), 1);
        sim.World.Add(crude, new Crafted { Quality = Quality.Crude, Maker = maker });
        sim.WearItem(a, fine);
        sim.WearItem(b, crude);
        var head = sim.World.Get<Health>(a).Body.PartIndex("Head");
        var fineArmor = SupportingCast.Sim.Combat.CombatRules.ArmorAt(sim, a, head, DamageType.Cut).Armor;
        var crudeArmor = SupportingCast.Sim.Combat.CombatRules.ArmorAt(sim, b, head, DamageType.Cut).Armor;
        Assert.Equal(0.25f * 1.15f, fineArmor, 3);
        Assert.Equal(0.25f * 0.8f, crudeArmor, 3);
        Assert.Equal("fine leather cap", sim.LabelOf(fine));
        // Wearing someone's work: a good opinion of them.
        Assert.Contains(sim.World.Get<Mood>(a).Memories, m => m.Def.Name == "UsesTheirWork" && m.Other == maker);
    }

    [Fact]
    public void ForgeWorkNeedsAHammerToHand()
    {
        var sim = Warm();
        var smith = Villager(sim, C(5, 5), 8, WorkType.Craft);
        var forge = Station(sim, "Forge", C(20, 20));
        sim.World.Get<Fueled>(forge).Fuel = 20;
        RecipeBooks.Discover(sim, smith, "handle:IronBar");
        sim.SpawnItem(sim.Thing("IronBar"), 4, C(6, 8));
        sim.SpawnItem(sim.Thing("Planks"), 4, C(7, 8));
        Bill(sim, forge, "ForgeAxe");
        Array.Fill(sim.Map.Temperature, 1000f, sim.Map.Idx(C(20, 20)), 1);
        var axe = sim.Defs.Recipe("ForgeAxe");
        Assert.Equal("Hammer", Crafting.MissingTool(sim, smith, forge, axe));

        sim.SpawnItem(sim.Thing("Hammer"), 1, C(21, 21)); // a hammer left by the forge
        Assert.Null(Crafting.MissingTool(sim, smith, forge, axe));
        Assert.Equal(1.25f, Crafting.ToolFactor(sim, smith, forge, axe), 3);
        Assert.Equal(0f, Crafting.ToolSpeed(sim, smith, "Hammer")); // not on them, though
    }

    [Fact]
    public void ThePlayerMendsWornGearAtTheStationThatMakesIt()
    {
        var sim = Warm();
        var me = Avatar(sim, C(10, 12));
        var rack = Station(sim, "TanningRack", C(11, 12));
        var cap = sim.Wear(me, sim.Thing("LeatherCap"));
        ref var t = ref sim.World.Get<Thing>(cap);
        t.Hp = t.Def.MaxHp * 0.3f;
        var cost = Crafting.RepairCost(sim, sim.Thing("TanningRack"), cap)!.Value;
        Assert.Equal(2, cost.Count); // 3 leather × 70% worn × half, rounded up
        sim.Store(sim.PackOf(me), sim.CreateItem(sim.Thing("Leather"), 5));

        sim.ApplyNow(new RepairCommand(me, rack, cap));
        sim.ApplyNow(new AvatarWorkCommand(me, true));
        sim.TickMany(SimClock.TicksPerSecond * 20);

        Assert.Equal(sim.Thing("LeatherCap").MaxHp, sim.World.Get<Thing>(cap).Hp);
        Assert.Equal(3, sim.StoredIn(sim.PackOf(me)).Sum(e => sim.World.Get<Stack>(e).Count));
    }

    [Fact]
    public void ThePlayerMakesAClubByHandStandingStill()
    {
        var sim = Warm();
        var me = Avatar(sim, C(10, 10));
        sim.Store(sim.PackOf(me), sim.CreateItem(sim.Thing("Wood"), 5));
        sim.Store(sim.PackOf(me), sim.CreateItem(sim.Thing("Stone"), 2));
        sim.ApplyNow(new HandCraftCommand(me, sim.Defs.Recipe("MakeClub")));
        Assert.IsType<HandCraftJob>(sim.World.Get<Mind>(me).Job);
        sim.TickMany(SimClock.TicksPerSecond * 20);

        var made = sim.World.Query<Thing>().Single(e => sim.World.Get<Thing>(e).Def.Name == "Club");
        Assert.True(Systems.Inventory.OnPerson(sim, me, made));
        Assert.True(sim.World.Has<Crafted>(made));
        Assert.Equal(2, Count(sim, "Wood"));
        Assert.Equal(1, Count(sim, "Stone"));

        // Walking off stops it before anything's used.
        sim.ApplyNow(new HandCraftCommand(me, sim.Defs.Recipe("MakeClub")));
        sim.TickMany(30);
        sim.ApplyNow(new AvatarMoveCommand(me, new int2(1, 0)));
        sim.TickMany(10);
        Assert.Null(sim.World.Get<Mind>(me).Job);
        Assert.Equal(2, Count(sim, "Wood"));
    }

    // ---------------- Scales and the map ----------------

    [Fact]
    public void GrowingDragonsShedScales()
    {
        var sim = Warm();
        var d = sim.SpawnCreature(sim.Defs.Creature("Dragon"), "Ember", C(10, 10), ageDays: 3.9f);
        TickFed(sim, SimClock.HoursToTicks(4));
        Assert.Equal("Juvenile", sim.World.Get<Creature>(d).Stage(sim.Clock.Tick).Name);
        Assert.True(Count(sim, "DragonScale") >= 3);
    }

    [Fact]
    public void GeneratedMapsHaveOreAndClayAwayFromTheLanding()
    {
        var sim = Simulation.CreateStandard(TestWorld.LoadDefs(), 160, 160, 1234, MapGenerator.RecommendedDepth);
        var center = MapGenerator.Generate(sim, new MapGenerator.Options { SpawnStartingKit = false, Wildlife = false });
        var ore = sim.Defs.Block("IronOre");
        var clay = sim.Defs.Block("Clay");
        int ores = 0, clays = 0, near = 0;
        for (var i = 0; i < sim.Map.Count; i++)
        {
            var b = sim.Map.Block[i];
            if (b != ore && b != clay)
                continue;
            if (b == ore) ores++; else clays++;
            var c = sim.Map.CellOf(i);
            if (MathF.Sqrt((c.X - center.X) * (c.X - center.X) + (c.Y - center.Y) * (c.Y - center.Y)) < 24)
                near++;
        }
        Assert.True(ores > 20, $"{ores} ore blocks");
        Assert.True(clays > 5, $"{clays} clay blocks");
        Assert.Equal(0, near);
    }
}

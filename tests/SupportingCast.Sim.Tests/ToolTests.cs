using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Maps;
using SupportingCast.Sim.Saving;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Tests;

/// <summary>Tool tiers: gathering slow by hand and faster with better tools, the belt they hang on, recipes that want a tier.</summary>
public class ToolTests
{
    private static Simulation Warm()
    {
        var sim = TestWorld.Create();
        sim.Climate.BaseTemperature = 20;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 20f);
        return sim;
    }

    private static Entity Worker(Simulation sim, int3 at, params WorkType[] only)
    {
        var e = sim.SpawnCreature(sim.Defs.Creature("Human"), "Worker", at, ageDays: 25 * 365);
        Array.Fill(sim.World.Get<Needs>(e).Values, 1f);
        sim.SetSkills(e);
        var mind = sim.World.Get<Mind>(e);
        Array.Fill(mind.WorkPriority, 0);
        foreach (var w in only)
            mind.WorkPriority[(int)w] = 1;
        return e;
    }

    /// <summary>Ticks for one worker to fell an oak, with <paramref name="tool"/> (or nothing) on the belt.</summary>
    private static int TicksToFell(string? tool)
    {
        var sim = Warm();
        var worker = Worker(sim, C(14, 15), WorkType.Chop);
        if (tool != null)
            Assert.False(Toolbelt.PutOn(sim, worker, sim.CreateItem(sim.Thing(tool), 1)).IsNull);
        var tree = sim.SpawnStructure(sim.Thing("Oak"), C(15, 15));
        sim.RebuildDerivedIfNeeded();
        sim.Map.SetDesignations(sim.Map.Idx(C(15, 15)), Designation.Chop);
        var ticks = 0;
        while (sim.World.IsAlive(tree) && ticks < SimClock.HoursToTicks(4))
        {
            sim.Tick();
            ticks++;
        }
        Assert.False(sim.World.IsAlive(tree));
        return ticks;
    }

    [Fact]
    public void BetterToolsFellTreesFaster()
    {
        var bare = TicksToFell(null);
        var stone = TicksToFell("StoneAxe");
        var iron = TicksToFell("Axe");
        Assert.True(bare > stone * 1.8f, $"bare {bare} vs stone {stone}");
        Assert.True(stone > iron * 1.4f, $"stone {stone} vs iron {iron}");
    }

    [Fact]
    public void OnlyPeopleNeedTools()
    {
        var sim = Warm();
        var dragon = sim.SpawnCreature(sim.Defs.Creature("Dragon"), "Ember", C(5, 5), ageDays: 14);
        var person = Worker(sim, C(6, 6));
        Assert.Equal(1f, Crafting.GatherFactor(sim, dragon, "Axe"));
        Assert.Equal(Crafting.BareHands, Crafting.GatherFactor(sim, person, "Axe"));
        Toolbelt.GiveStartingTools(sim, person);
        Assert.Equal(0.75f, Crafting.GatherFactor(sim, person, "Axe"));
        Assert.Equal(0.75f, Crafting.GatherFactor(sim, person, "Pick"));
        // Rock wants a pick; soil any hands.
        Assert.Equal("Pick", sim.Defs.Block("Stone").DigTool);
        Assert.Null(sim.Defs.Block("Dirt").DigTool);
    }

    [Fact]
    public void FineForgeWorkWantsAnIronHammer()
    {
        var sim = Warm();
        var smith = Worker(sim, C(20, 20), WorkType.Craft);
        var forge = sim.SpawnStructure(sim.Thing("Forge"), C(21, 21));
        var sword = sim.Defs.Recipe("ForgeSword");
        var axe = sim.Defs.Recipe("ForgeAxe");
        Toolbelt.PutOn(sim, smith, sim.CreateItem(sim.Thing("StoneHammer"), 1));
        Assert.Null(Crafting.MissingTool(sim, smith, forge, axe)); // a stone hammer starts the forge
        Assert.Equal("Hammer", Crafting.MissingTool(sim, smith, forge, sword)); // but a blade wants iron
        Toolbelt.PutOn(sim, smith, sim.CreateItem(sim.Thing("Hammer"), 1));
        Assert.Null(Crafting.MissingTool(sim, smith, forge, sword));
        // Upgrades take the old tool: the iron axe is a stone axe reforged.
        Assert.Contains(axe.Ingredients, i => i.ThingDef.Name == "StoneAxe");
    }

    [Fact]
    public void BeltToolsGoWithTheirOwnerAndSurviveASave()
    {
        var sim = Warm();
        var me = Worker(sim, C(5, 5));
        sim.GiveStartingPack(me);
        var belt = Toolbelt.Of(sim, me);
        Assert.Equal(["StoneAxe", "StonePick"], belt.Select(e => sim.World.Get<Thing>(e).Def.Name).Order());
        Assert.All(belt, e => Assert.False(sim.IsAvailable(e))); // no hauler or crafter takes them
        Assert.DoesNotContain(belt[0], sim.AvailableItems());

        sim.World.Get<Position>(me) = Position.At(C(12, 9));
        Assert.Equal(C(12, 9), sim.LocationOf(belt[0]));

        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        var again = loaded.World.Query<Creature>().First(e => loaded.World.Get<Creature>(e).Name == "Worker");
        Assert.Equal(2, Toolbelt.Of(loaded, again).Count);
        Assert.Equal(0.75f, Crafting.GatherFactor(loaded, again, "Axe"));

        // Dead, it all drops where they fell.
        ref var c = ref sim.World.Get<Creature>(me);
        NeedsSystem.Kill(sim, me, ref c);
        Assert.Empty(Toolbelt.Of(sim, me));
        Assert.Equal(2, sim.SpawnedItems().Count(e => sim.World.Get<Thing>(e).Def.Tool != null));
    }

    [Fact]
    public void WorkersFetchBetterToolsForTheirWork()
    {
        var sim = Warm();
        // A miner swaps the stone pick on their belt for an iron one lying about.
        var miner = Worker(sim, C(5, 5), WorkType.Mine);
        Toolbelt.GiveStartingTools(sim, miner);
        sim.SpawnItem(sim.Thing("Pick"), 1, C(9, 9));
        sim.TickMany(SimClock.HoursToTicks(1));
        Assert.Equal("Pick", sim.World.Get<Thing>(Toolbelt.Best(sim, miner, "Pick")).Def.Name);
        Assert.Equal(2, Toolbelt.Of(sim, miner).Count); // swapped, not added
        Assert.Contains(sim.SpawnedItems(), e => sim.World.Get<Thing>(e).Def.Name == "StonePick"); // the old one, left behind

        // An iron axe is a weapon too: an unarmed woodcutter wields it, and it counts all the same.
        var woodcutter = Worker(sim, C(25, 25), WorkType.Chop);
        sim.SpawnItem(sim.Thing("Axe"), 1, C(28, 28));
        sim.TickMany(SimClock.HoursToTicks(1));
        Assert.Equal(1.25f, Crafting.GatherFactor(sim, woodcutter, "Axe"));
    }

    [Fact]
    public void ThePlayerPutsAToolOnTheBeltAndTakesItOff()
    {
        var sim = Warm();
        var me = Worker(sim, C(5, 5));
        sim.World.Add(me, new Avatar());
        sim.SpawnItem(sim.Thing("FishingRod"), 1, C(6, 5));
        var pick = AvatarSystem.ActionAt(sim, me, C(6, 5));
        Assert.Equal(AvatarActionKind.PickUp, pick.Kind);
        Assert.Contains("belt", pick.Label);
        sim.ApplyNow(new Core.AvatarWorkCommand(me, true, C(6, 5)));
        sim.Tick();
        Assert.Single(Toolbelt.Of(sim, me));
        Assert.True(sim.World.Get<Carrying>(me).Item.IsNull, "hands stay free");

        var onBelt = Toolbelt.Of(sim, me)[0];
        sim.ApplyNow(new Core.BeltCommand(me, onBelt, On: false));
        Assert.Empty(Toolbelt.Of(sim, me));
        Assert.Equal(onBelt, sim.World.Get<Carrying>(me).Item); // no pack: into the hands
        sim.ApplyNow(new Core.BeltCommand(me, onBelt, On: true));
        Assert.Single(Toolbelt.Of(sim, me));
        Assert.True(sim.World.Get<Carrying>(me).Item.IsNull);
        Assert.DoesNotContain(sim.SpawnedItems(), e => sim.World.Get<Thing>(e).Def.Name == "FishingRod");
    }

    [Fact]
    public void EveryWeaponAndToolHasAShapeInHand()
    {
        var defs = TestWorld.LoadDefs();
        foreach (var t in defs.AllThings.Where(t => t.Weapon != null || t.Tool != null))
            Assert.False(string.IsNullOrEmpty(t.Held), $"{t.Name} has no held shape (it wouldn't be drawn in hand)");
    }

    [Fact]
    public void TheToolInHandIsTheBestOfItsKindAndJobsSayWhichKind()
    {
        var sim = Warm();
        var worker = Worker(sim, C(14, 15), WorkType.Chop);
        Assert.True(Crafting.ToolInHand(sim, worker, "Axe").IsNull || sim.World.Get<Thing>(Crafting.ToolInHand(sim, worker, "Axe")).Def.Tool!.Kind == "Axe");
        foreach (var e in Toolbelt.Of(sim, worker).ToList())
            Toolbelt.TakeOff(sim, worker, e);
        var stone = Toolbelt.PutOn(sim, worker, sim.CreateItem(sim.Thing("StoneAxe"), 1));
        Assert.Equal(stone, Crafting.ToolInHand(sim, worker, "Axe"));
        var steel = Toolbelt.PutOn(sim, worker, sim.CreateItem(sim.Thing("SteelAxe"), 1));
        Assert.Equal(steel, Crafting.ToolInHand(sim, worker, "Axe"));
        Assert.True(Crafting.ToolInHand(sim, worker, "Rod").IsNull);
        Assert.Equal("Pick", new DigJob(C(1, 1)).ToolKind);
        Assert.Equal("Hammer", new BuildJob(Entity.Null).ToolKind);
    }
}

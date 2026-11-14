using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Militia;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Systems;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// Each trade's own makeshift weapon (the cook's knife, the smith's hammer, the innkeeper's cudgel
/// under the bar), the muster arming them properly all the same, and the tinker's crossbow.
/// </summary>
public class TradeWeaponTests
{
    /// <summary>
    /// A round with every named trade an AI's (the player labours), just dealt. The hunting bow
    /// left lying in town is cleared away: whoever has only a makeshift weapon would take it up.
    /// </summary>
    private static Simulation Round(string playerTrade = "Labourer")
    {
        var sim = VillageRound.NewSimulation(LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Guid.Empty, "Arin", default, []));
        sim.ApplyNow(new SetTradePreferencesCommand(Guid.Empty, [sim.Defs.Trade(playerTrade)], []));
        sim.ApplyNow(new StartRoundCommand());
        foreach (var e in sim.World.Query<Thing>())
            if (sim.World.Get<Thing>(e) is { Spawned: true, Def.Weapon: not null })
                sim.DestroyThing(e);
        return sim;
    }

    private static Entity Holder(Simulation sim, string trade) =>
        sim.Creatures().First(e => !sim.World.Has<Avatar>(e) && sim.World.TryGet<Trade>(e, out var t) && t.Def.Name == trade);

    private static string? Wielded(Simulation sim, Entity e) =>
        sim.World.TryGet<Equipment>(e, out var eq) && sim.World.IsAlive(eq.Weapon) ? sim.World.Get<Thing>(eq.Weapon).Def.Name : null;

    private static Entity Lou(Simulation sim) => sim.World.Query<Thing>().Single(e => sim.World.Get<Thing>(e).Def.Name == "LuckyLou");

    [Fact]
    public void EachTradeStartsWithItsOwnWeapon()
    {
        var sim = Round();
        var w = sim.World;
        Assert.Equal("KitchenKnife", Wielded(sim, Holder(sim, "Cook")));
        Assert.Equal("Staff", Wielded(sim, Holder(sim, "Healer")));
        Assert.Equal("Dagger", Wielded(sim, Holder(sim, "Tinker")));
        var smith = Holder(sim, "Blacksmith");
        Assert.Equal("Hammer", Wielded(sim, smith));
        Assert.True(Crafting.ToolSpeed(sim, smith, "Hammer") > 0, "the hammer in hand still works the anvil");

        // The innkeeper's is under the bar, not on them, and no job of the town's takes it.
        var lou = Lou(sim);
        Assert.True(w.TryGet<Stored>(lou, out var under) && Stashes.IsStash(sim, under.In));
        Assert.False(sim.IsAvailable(lou));
        Assert.True(Stashes.Reaches(sim, Holder(sim, "Innkeeper"), under.In));
        Assert.False(Stashes.Reaches(sim, Holder(sim, "Cook"), under.In));
        Assert.Equal(0, sim.ContainerRoom(under.In, sim.Defs.Thing("IronSpear"), null));
        Assert.True(w.Get<Thing>(lou).Def.Weapon!.Makeshift);
    }

    [Fact]
    public void AnEmptyStashOffersNothingToReachFor()
    {
        // Regression: with the cudgel out, F at the bar offered "Reach under the counter for stone
        // bridge" (a default Entity is index 0, a real one) and pressing it threw.
        var sim = Round("Innkeeper");
        var w = sim.World;
        var me = sim.Creatures().Single(e => w.Has<Avatar>(e));
        var lou = Lou(sim);
        var stash = w.Get<Stored>(lou).In;
        var at = w.Get<Thing>(stash).Cell;
        var beside = sim.SpiralFrom(at, 2).First(c => c != at && sim.Map.Passable(c) && AvatarSystem.InReach(c, at));
        w.Get<Position>(me) = Position.At(beside);

        Assert.Equal(AvatarActionKind.Equip, AvatarSystem.ActionAt(sim, me, at).Kind); // it's there: reach for it

        sim.DestroyThing(lou);
        var action = AvatarSystem.ActionAt(sim, me, at);
        Assert.NotEqual(AvatarActionKind.Equip, action.Kind);
        Assert.DoesNotContain("bridge", action.Label);
    }

    [Fact]
    public void AMakeshiftWeaponCountsAsUnarmedForTheMusterAndDoesntSwellARaid()
    {
        var sim = Round();
        var w = sim.World;
        var cook = Holder(sim, "Cook");
        Assert.False(MilitiaSystem.ArmedProperly(sim, cook));
        var unarmed = MilitiaSystem.Unarmed(sim);
        var points = Raids.Points(sim);
        Inventory.PutOn(sim, cook, sim.CreateItem(sim.Defs.Thing("IronSpear"), 1));
        Assert.True(MilitiaSystem.ArmedProperly(sim, cook));
        Assert.Equal(unarmed - 1, MilitiaSystem.Unarmed(sim));
        Assert.Equal(points + 15f, Raids.Points(sim));
        // The knife's put away, kept in the work pack between jobs, not dropped.
        var knife = sim.StoredIn(sim.PackOf(cook)).Single(e => w.Get<Thing>(e).Def.Name == "KitchenKnife");
        Assert.True(Inventory.KeptBetweenJobs(sim, knife));
    }

    [Fact]
    public void InAMusterTheCookTakesASpearAndAfterwardsTheirKnifeAgain()
    {
        var sim = Round();
        var w = sim.World;
        var cook = Holder(sim, "Cook");
        var rack = w.Query<Thing>().Single(e => MilitiaSystem.IsRack(sim, e));
        var spears = new List<Entity>();
        while (sim.ContainerRoom(rack, sim.Defs.Thing("IronSpear"), null) > 0 && spears.Count < MilitiaSystem.Unarmed(sim))
        {
            var s = sim.CreateItem(sim.Defs.Thing("IronSpear"), 1);
            w.Add(s, new TownArms());
            Assert.True(sim.Store(rack, s).IsNull);
            spears.Add(s);
        }
        sim.Militia.Muster(sim);
        for (var h = 0; h < 6 && !(w.TryGet<Equipment>(cook, out var eq) && w.Has<TownArms>(eq.Weapon)); h++)
            sim.TickMany(SimClock.TicksPerHour / 2);
        Assert.True(w.Has<TownArms>(w.Get<Equipment>(cook).Weapon), $"cook holds {Wielded(sim, cook)}: {w.Get<Mind>(cook).Job?.Report}");
        Assert.Contains(sim.StoredIn(sim.PackOf(cook)), e => w.Get<Thing>(e).Def.Name == "KitchenKnife");

        sim.Militia.StandDown(sim);
        for (var h = 0; h < 8 && Wielded(sim, cook) != "KitchenKnife"; h++)
            sim.TickMany(SimClock.TicksPerHour / 2);
        Assert.True(Wielded(sim, cook) == "KitchenKnife",
            $"cook holds {Wielded(sim, cook)}, pack [{string.Join(", ", sim.StoredIn(sim.PackOf(cook)).Select(e => w.Get<Thing>(e).Def.Name))}], doing {w.Get<Mind>(cook).Job?.Report}");
        Assert.All(spears, s => Assert.True(!w.Has<Stored>(s) || w.Get<Stored>(s).In == rack || w.Has<Equipment>(cook)));
    }

    [Fact]
    public void TheInnkeeperReachesUnderTheBarWhenTheRaidComesAndPutsItBackAfter()
    {
        var sim = Round();
        var w = sim.World;
        var keeper = Holder(sim, "Innkeeper");
        var lou = Lou(sim);
        var stash = w.Get<Stored>(lou).In;
        sim.TickMany(SimClock.TicksPerHour);
        Assert.Equal(stash, w.Get<Stored>(lou).In); // a quiet day: it stays put

        sim.Militia.Muster(sim);
        for (var h = 0; h < 12 && Wielded(sim, keeper) is null; h++)
            sim.TickMany(SimClock.TicksPerHour / 4);
        Assert.NotNull(Wielded(sim, keeper));
        Assert.False(w.Has<Stored>(lou) && w.Get<Stored>(lou).In == stash, "Lou's out from under the bar");

        sim.Militia.StandDown(sim);
        for (var h = 0; h < 12 && !(w.Has<Stored>(lou) && w.Get<Stored>(lou).In == stash); h++)
            sim.TickMany(SimClock.TicksPerHour / 2);
        Assert.Equal(stash, w.Get<Stored>(lou).In);
    }

    [Fact]
    public void NobodyButTheInnkeeperTakesFromUnderTheBar()
    {
        var sim = Round();
        var w = sim.World;
        var me = w.Query<Avatar>().Single();
        var lou = Lou(sim);
        var stash = w.Get<Stored>(lou).In;
        if (w.TryGet<Trade>(me, out var mine) && mine.Def.Name == "Innkeeper")
            return; // dealt the innkeeper: nothing to refuse
        w.Get<Position>(me).Cell = sim.SpiralFrom(w.Get<Thing>(stash).Cell, 3)
            .First(c => sim.Map.Passable(c) && sim.Map.Standable(sim.Map.Idx(c)) && AvatarSystem.InReach(c, w.Get<Thing>(stash).Cell));
        sim.ApplyNow(new TakeFromStorageCommand(me, lou));
        Assert.Equal(stash, w.Get<Stored>(lou).In);
        Assert.NotEqual(AvatarActionKind.OpenStorage, AvatarSystem.ActionAt(sim, me, w.Get<Thing>(stash).Cell, AvatarTool.Use).Kind);
    }

    [Fact]
    public void WithOnlyAMakeshiftWeaponAVillagerFightsOnlyWhenCornered()
    {
        var sim = Create();
        var cook = sim.SpawnCreature(sim.Defs.Creature("Human"), "Mags", C(5, 5), Faction.Village, ageDays: 25 * 365);
        Array.Fill(sim.World.Get<Needs>(cook).Values, 1f);
        sim.Equip(cook, sim.Defs.Thing("KitchenKnife"));
        var far = Raids.SpawnRaider(sim, sim.Defs.AllRaiderKinds[0], "Skarn", C(11, 5), sim.Rand);
        Assert.IsNotType<DefendJob>(Defence.Respond(sim, cook, far));
        var near = Raids.SpawnRaider(sim, sim.Defs.AllRaiderKinds[0], "Brak", C(6, 5), sim.Rand);
        Assert.IsType<DefendJob>(Defence.Respond(sim, cook, near));

        // With a proper weapon, they go out to meet the far one.
        sim.Equip(cook, sim.Defs.Thing("ShortSword"));
        Assert.IsType<DefendJob>(Defence.Respond(sim, cook, far));
    }

    [Fact]
    public void ASmithWhoTakesUpASwordKeepsTheHammerOnTheBelt()
    {
        var sim = Round();
        var smith = Holder(sim, "Blacksmith");
        var hammer = sim.World.Get<Equipment>(smith).Weapon;
        Inventory.PutOn(sim, smith, sim.CreateItem(sim.Defs.Thing("ShortSword"), 1));
        Assert.Equal("ShortSword", Wielded(sim, smith));
        Assert.Contains(hammer, Toolbelt.Of(sim, smith));
        Assert.True(Crafting.ToolSpeed(sim, smith, "Hammer") > 0);
    }

    [Fact]
    public void TheTinkerCanMakeACrossbowAndShootIt()
    {
        var sim = Round();
        var tinker = Holder(sim, "Tinker");
        var recipe = sim.Defs.AllRecipes.Single(r => r.Name == "MakeCrossbow");
        Assert.Equal("TinkerBench", recipe.Station);
        Assert.True(RecipeBooks.Knows(sim, tinker, recipe));
        var crossbow = sim.Defs.Thing("Crossbow");
        Assert.False(crossbow.Weapon!.Makeshift);
        Inventory.PutOn(sim, tinker, sim.CreateItem(crossbow, 1));
        Assert.True(Archery.Bow(sim, tinker) is { Ranged: true });
        Assert.True(MilitiaSystem.ArmedProperly(sim, tinker));
        Assert.Contains(sim.StoredIn(sim.PackOf(tinker)), e => sim.World.Get<Thing>(e).Def.Name == "Dagger");
    }

    [Fact]
    public void OnlyTheLabourerKeepsTheStoneAxeAndPick()
    {
        var sim = Round();
        var w = sim.World;
        bool HasStoneTools(Entity e) => Toolbelt.Of(sim, e).Any(t => Toolbelt.StartingTools.Contains(w.Get<Thing>(t).Def.Name));
        foreach (var trade in new[] { "Innkeeper", "Cook", "Blacksmith", "Healer", "Tinker" })
            Assert.False(HasStoneTools(Holder(sim, trade)), $"the {trade.ToLowerInvariant()} still has the stone tools");
        var me = w.Query<Avatar>().Single();
        Assert.Equal("Labourer", w.Get<Trade>(me).Def.Name);
        Assert.True(HasStoneTools(me), "the labourer keeps them");
    }
}

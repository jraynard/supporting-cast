using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>The inn's upkeep: clean crockery, the washing up, and changing the keg.</summary>
public class InnUpkeepTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    /// <summary>A cask at (11, 10) and a wash tub at (9, 10) either side of a player at (10, 10).</summary>
    private static (Simulation Sim, Entity Me, Entity Cask, Entity Tub) Bar()
    {
        var sim = Create(40, 40);
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), "Inna", C(10, 10), ageDays: 25 * 365);
        sim.Round.OpenLobby();
        sim.Round.Sit(Ana, "Inna", null, null, null);
        sim.Round.Start(sim);
        sim.GiveStartingPack(me);
        var cask = sim.SpawnStructure(sim.Thing("AleCask"), C(11, 10));
        var tub = sim.SpawnStructure(sim.Thing("WashTub"), C(9, 10));
        sim.RebuildDerivedIfNeeded();
        return (sim, me, cask, tub);
    }

    /// <summary>Play a session that wants a press as its cue comes round, until it's done.</summary>
    private static void OnTheBeat(Simulation sim, Entity me, Func<WorkSession, bool> cue, bool miss = false)
    {
        var predictor = new WorkPredictor();
        var was = false;
        for (var t = 0; t < 60 * 60 && sim.World.Has<WorkSession>(me); t++)
        {
            sim.Tick();
            var press = false;
            if (predictor.Session is { } s)
            {
                var now = cue(s);
                // A careful hand strikes as the cue comes; a careless one just before it.
                press = miss ? !now && t % 50 == 0 : now && !was;
                was = now;
            }
            predictor.Update(sim, me, 1, press, false, 0, c => sim.ApplyNow(c));
        }
        sim.Tick();
    }

    [Fact]
    public void NoCleanTankardNoPour()
    {
        var (sim, me, cask, _) = Bar();
        PourSession.TryStart(sim, me, cask);
        Assert.False(sim.World.Has<WorkSession>(me));
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("clean tankard"));
        Assert.Contains("no clean tankard", AvatarSystem.ActionAt(sim, me, C(11, 10)).Label);
    }

    [Fact]
    public void ATankardInThePackIsPouredIntoAndTheDrinkLandsInHand()
    {
        var (sim, me, cask, _) = Bar();
        Inventory.Stow(sim, me, sim.CreateItem(sim.Thing("Tankard"), 4));
        PourSession.TryStart(sim, me, cask);
        var s = (PourSession)sim.World.Get<WorkSession>(me);
        s.Advance([new WorkInput(0, WorkInputKind.Hold, 1), new WorkInput(0, WorkInputKind.Axis, 1), new WorkInput(160, WorkInputKind.Hold, 0)], 400);
        sim.Tick();
        var drink = sim.World.Get<Carrying>(me).Item;
        Assert.True(sim.World.Get<Thing>(drink).Def.Drink);
        Assert.Equal(3, sim.StoredIn(sim.PackOf(me)).Where(e => sim.World.Get<Thing>(e).Def.Name == "Tankard").Sum(e => sim.World.Get<Stack>(e).Count));
    }

    /// <summary>Wash as a player would: steer the brush to the grimiest spot (WASD) and scrub there (F held), until done or stopped.</summary>
    private static void Scrub(Simulation sim, Entity me, Func<WashSession, bool>? until = null)
    {
        var predictor = new WorkPredictor();
        for (var t = 0; t < 60 * 120 && sim.World.Has<WorkSession>(me); t++)
        {
            sim.Tick();
            bool held = false;
            int ax = 0, ay = 0;
            if (predictor.Session is WashSession s)
            {
                if (until?.Invoke(s) == true)
                    break;
                var (gx, gy, most) = (0, 0, 0f);
                for (var y = 0; y < WashSession.GridH; y++)
                    for (var x = 0; x < WashSession.GridW; x++)
                        if (s.GrimeAt(x, y) > most)
                            (gx, gy, most) = (x, y, s.GrimeAt(x, y));
                float tx = (gx + 0.5f) / WashSession.GridW, ty = (gy + 0.5f) / WashSession.GridH;
                ax = MathF.Abs(tx - s.BrushX) > 0.03f ? Math.Sign(tx - s.BrushX) : 0;
                ay = MathF.Abs(ty - s.BrushY) > 0.03f ? Math.Sign(ty - s.BrushY) : 0;
                held = MathF.Abs(tx - s.BrushX) < 0.09f && MathF.Abs(ty - s.BrushY) < 0.09f;
            }
            predictor.Update(sim, me, 1, false, held, ax, c => sim.ApplyNow(c), ay);
        }
        sim.Tick();
    }

    [Fact]
    public void DirtyCrockeryIsScrubbedCleanWithTheBrush()
    {
        var (sim, me, _, tub) = Bar();
        sim.World.Get<Carrying>(me).Item = sim.CreateItem(sim.Thing("DirtyTankard"), 3);
        Assert.Equal(AvatarActionKind.BarWork, AvatarSystem.ActionAt(sim, me, C(9, 10)).Kind);
        sim.ApplyNow(new BarWorkCommand(me, tub));
        var start = Assert.IsType<WashSession>(sim.World.Get<WorkSession>(me));
        Assert.True(start.Clean < 0.9f, "it comes out of the water grimy");
        Scrub(sim, me);

        Assert.True(BarWork.DirtyOn(sim, me).IsNull, "nothing dirty left");
        var clean = Vessels.CleanOn(sim, me, "Tankard");
        Assert.False(clean.IsNull);
        Assert.Equal(3, sim.World.Get<Stack>(clean).Count);
    }

    [Fact]
    public void ScrubbingOnlyCleansWhereTheBrushIs()
    {
        var (sim, me, _, tub) = Bar();
        sim.World.Get<Carrying>(me).Item = sim.CreateItem(sim.Thing("DirtyTankard"), 1);
        sim.ApplyNow(new BarWorkCommand(me, tub));
        var s = (WashSession)sim.World.Get<WorkSession>(me);
        // Scrubbing the empty water off to one side does nothing to the piece.
        var before = s.Clean;
        s.Advance([new WorkInput(0, WorkInputKind.Axis, -1), new WorkInput(0, WorkInputKind.Axis2, -1), new WorkInput(60, WorkInputKind.Hold, 1)], 200);
        Assert.Equal(0f, s.BrushX);
        Assert.Equal(before, s.Clean, 3);
    }

    [Fact]
    public void StoppedHalfwayWhatsWashedStaysWashed()
    {
        var (sim, me, _, tub) = Bar();
        sim.World.Get<Carrying>(me).Item = sim.CreateItem(sim.Thing("DirtyBowl"), 4);
        sim.ApplyNow(new BarWorkCommand(me, tub));
        Scrub(sim, me, until: s => s.Washed >= 1);
        sim.ApplyNow(new StopWorkCommand(me));
        Assert.Equal(1, sim.World.Get<Stack>(Vessels.CleanOn(sim, me, "Bowl")).Count);
        Assert.Equal(3, sim.World.Get<Stack>(BarWork.DirtyOn(sim, me)).Count);
    }

    [Fact]
    public void ADryCaskTakesANewKegKnockedInCleanly()
    {
        var (sim, me, cask, _) = Bar();
        sim.World.Add(cask, new CaskDrawn { Drawn = 60 });
        sim.World.Get<Carrying>(me).Item = sim.CreateItem(sim.Thing("Keg"), 1);
        Assert.Equal("Knock in the new keg", AvatarSystem.ActionAt(sim, me, C(11, 10)).Label);
        sim.ApplyNow(new BarWorkCommand(me, cask));
        Assert.IsType<TapSession>(sim.World.Get<WorkSession>(me));
        OnTheBeat(sim, me, s => ((TapSession)s).OnTheTap);

        Assert.True(sim.World.Get<Carrying>(me).Item.IsNull, "the keg went in");
        Assert.True(PourSession.ServingsLeft(sim, cask) >= 60 - TapSession.SprayPerMiss, $"{PourSession.ServingsLeft(sim, cask)}");
    }

    [Fact]
    public void AFumbledTapSpraysServingsAway()
    {
        var (sim, me, cask, _) = Bar();
        sim.World.Add(cask, new CaskDrawn { Drawn = 60 });
        sim.World.Get<Carrying>(me).Item = sim.CreateItem(sim.Thing("Keg"), 1);
        sim.ApplyNow(new BarWorkCommand(me, cask));
        OnTheBeat(sim, me, s => ((TapSession)s).OnTheTap, miss: true);
        Assert.True(PourSession.ServingsLeft(sim, cask) <= 60 - 2 * TapSession.SprayPerMiss, $"{PourSession.ServingsLeft(sim, cask)}");
    }

    [Fact]
    public void AFullCaskWontTakeAKeg()
    {
        var (sim, me, cask, _) = Bar();
        sim.World.Get<Carrying>(me).Item = sim.CreateItem(sim.Thing("Keg"), 1);
        sim.ApplyNow(new BarWorkCommand(me, cask));
        Assert.False(sim.World.Has<WorkSession>(me));
    }

    [Fact]
    public void WashingAndTappingSurviveASave()
    {
        var (sim, me, _, tub) = Bar();
        sim.World.Get<Carrying>(me).Item = sim.CreateItem(sim.Thing("DirtyTankard"), 3);
        sim.ApplyNow(new BarWorkCommand(me, tub));
        var s = (WashSession)sim.World.Get<WorkSession>(me);
        s.Advance([new WorkInput(0, WorkInputKind.Axis, 1), new WorkInput(0, WorkInputKind.Hold, 1)], 20);
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        var copy = (WashSession)loaded.World.Get<WorkSession>(me);
        Assert.Equal((s.DirtyDef, s.Count, s.BrushX, s.Clean, s.StepsDone), (copy.DirtyDef, copy.Count, copy.BrushX, copy.Clean, copy.StepsDone));
        // A copy run ahead doesn't share the grime with the original.
        var ahead = (WashSession)s.Clone();
        ahead.Advance([new WorkInput(0, WorkInputKind.Axis, -1), new WorkInput(0, WorkInputKind.Axis2, 1), new WorkInput(0, WorkInputKind.Hold, 1)], 60);
        Assert.NotEqual(s.Clean, ahead.Clean);
    }

    // ---------------- In a round ----------------

    private static (Simulation Sim, List<Entity> Party) Tavern(string playerTrade)
    {
        var sim = VillageRound.NewSimulation(LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default, []));
        sim.ApplyNow(new SetTradePreferencesCommand(Ana, [sim.Defs.Trade(playerTrade)], []));
        sim.ApplyNow(new StartRoundCommand());
        TestWorld.AleOnly(sim); // drink orders here are for ale
        var party = sim.Parties.Send(sim, sim.Defs.Party("GildedSpurs"));
        return (sim, party);
    }

    private static Entity Inn(Simulation sim) => ShopSystem.BuildingOf(sim, sim.Defs.Building("Inn"));

    private static int Count(Simulation sim, string def) =>
        sim.World.Query<Thing>().Where(e => sim.World.Get<Thing>(e).Def.Name == def && sim.World.IsAlive(e)).Sum(e => sim.World.Get<Stack>(e).Count);

    [Fact]
    public void ADrinkServedLeavesADirtyTankardBehind()
    {
        var (sim, party) = Tavern("Innkeeper");
        var me = Players.CharacterOf(sim, Ana);
        var drinker = party[0];
        sim.World.Get<Adventurer>(drinker).NextDrink = sim.Clock.Tick;
        sim.TickMany(SimClock.TicksPerHour / 6);
        var order = sim.Shops.All.Single(e => e.IsOpen && e.Poster == drinker && e.Kind.Kind == ServiceKind.Drink);
        var ale = sim.CreateItem(sim.Thing("Ale"), 1);
        sim.World.Add(ale, new Crafted { Quality = Quality.Plain, Maker = me });
        var at = sim.World.Get<Position>(drinker).Cell;
        sim.World.Get<Position>(me).Cell = sim.SpiralFrom(at, 3).First(c => c != at && sim.Map.Passable(c) && c.Z == at.Z);
        sim.World.Get<Position>(me).Pos = Position.CenterOf(sim.World.Get<Position>(me).Cell);
        Inventory.Stow(sim, me, ale);
        var dirty = Count(sim, "DirtyTankard");

        sim.ApplyNow(new GiveCommand(me, drinker, ale, 1));
        Assert.Equal(OrderStatus.Done, order.Status);
        Assert.Equal(dirty, Count(sim, "DirtyTankard")); // still drinking it
        TillFinished(sim, drinker);
        Assert.Equal(dirty + 1, Count(sim, "DirtyTankard"));
    }

    [Fact]
    public void TheInnOpensWithCleanCrockeryAndKegsInTheStoreroom()
    {
        var (sim, _) = Tavern("Healer");
        var parts = Buildings.Parts(sim, Inn(sim));
        int Held(string def) => parts.SelectMany(p => sim.StoredIn(p)).Where(e => sim.World.Get<Thing>(e).Def.Name == def).Sum(e => sim.World.Get<Stack>(e).Count);
        Assert.Equal(VillageRound.Tankards, Held("Tankard"));
        Assert.Equal(VillageRound.Bowls, Held("Bowl"));
        Assert.Equal(VillageRound.StoreroomKegs, Held("Keg"));
        Assert.Single(parts, p => BarWork.IsTub(sim, p));
        // Everything a named drink takes is on the shelf behind the bar, beside the tankards.
        var cask = parts.Single(p => sim.World.Get<Thing>(p).Def.Building?.Cask != null);
        var barShelf = parts.Where(p => sim.World.Get<Thing>(p).Def.Name == "Shelf")
            .OrderBy(p => Maps.Map.Distance(sim.World.Get<Thing>(p).Cell, sim.World.Get<Thing>(cask).Cell)).First();
        var onShelf = sim.StoredIn(barShelf).Select(e => sim.World.Get<Thing>(e).Def.Name).ToHashSet();
        foreach (var drink in sim.Defs.AllThings.Where(d => d.Mix is { House: false }))
            Assert.All(drink.Mix!.Adds, add => Assert.Contains(add, onShelf));
        Assert.Contains("Tankard", onShelf);
    }

    [Fact]
    public void AnAIInnkeeperWashesUpAndChangesTheKeg()
    {
        var (sim, _) = Tavern("Healer");
        var inn = Inn(sim);
        var cask = Buildings.Parts(sim, inn).Single(p => sim.World.Get<Thing>(p).Def.Building?.Cask != null);
        sim.World.Add(cask, new CaskDrawn { Drawn = 60 });
        sim.SpawnItem(sim.Thing("DirtyTankard"), 5, sim.World.Get<Thing>(cask).Cell + new int3(-2, 3, 0));
        for (var i = 0; i < 16 && (PourSession.ServingsLeft(sim, cask) == 0 || Count(sim, "DirtyTankard") > 0); i++)
            sim.TickMany(SimClock.TicksPerHour / 4);
        Assert.True(PourSession.ServingsLeft(sim, cask) > 40, $"cask {PourSession.ServingsLeft(sim, cask)}");
        Assert.Equal(0, Count(sim, "DirtyTankard"));
    }

    [Fact]
    public void TheCarterCallsEachMorning()
    {
        var (sim, _) = Tavern("Healer");
        var parts = Buildings.Parts(sim, Inn(sim));
        int Kegs() => parts.SelectMany(p => sim.StoredIn(p)).Where(e => sim.World.Get<Thing>(e).Def.Name == "Keg").Sum(e => sim.World.Get<Stack>(e).Count);
        var before = Kegs();
        while (sim.Clock.Day < 2 || sim.Clock.HourOfDay < InnSupplySystem.DeliveryHour + 0.5f)
            sim.TickMany(SimClock.TicksPerHour / 4);
        Assert.True(Kegs() >= before + InnSupplySystem.Kegs[0].Count - 1, $"{before} -> {Kegs()}"); // less any the keeper's knocked in since
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("carter"));
    }
}

using HeroesNotIncluded.Sim.Bots;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

public class WorkTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    /// <summary>A round with a character in a smithy (<see cref="SmithyKit"/>), a billet for an axe in their tongs.</summary>
    private static (Simulation Sim, Entity Me, Entity Forge, RecipeDef Recipe) AtTheForge()
    {
        var (sim, me, kit, recipe) = AtTheSmithy();
        return (sim, me, kit.Forge, recipe);
    }

    private static (Simulation Sim, Entity Me, SmithyKit Kit, RecipeDef Recipe) AtTheSmithy()
    {
        var sim = Create(40, 40);
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), "Smith", C(10, 10), ageDays: 25 * 365);
        sim.Round.OpenLobby();
        sim.Round.Sit(Ana, "Smith", null, null, null);
        sim.Round.Start(sim);
        var kit = SmithyKit.Place(sim);
        var recipe = sim.Defs.Recipe("ForgeAxe");
        sim.World.Get<Workbench>(kit.Forge).Begin(recipe, me);
        Smithing.BeginMaking(sim, me, kit.Forge, recipe);
        return (sim, me, kit, recipe);
    }

    /// <summary>A heat on the anvil, the billet drawn from the fire glowing orange.</summary>
    private static HammerSession Start(Simulation sim, Entity me, Entity forge, RecipeDef recipe)
    {
        Smithing.PieceOf(sim, me)!.Temp = 950f;
        var anvil = sim.World.Query<Thing>().First(e => sim.World.Get<Thing>(e).Def.Name == "Anvil");
        sim.ApplyNow(new SmithCommand(me, anvil));
        return (HammerSession)sim.World.Get<WorkSession>(me);
    }

    /// <summary>Plays a whole session perfectly or badly through the predictor, as a client would.</summary>
    private static void Play(Simulation sim, Entity me, WorkPredictor predictor, bool onTheMark, int maxTicks = 60 * 60)
    {
        for (var t = 0; t < maxTicks && sim.World.Has<WorkSession>(me); t++)
        {
            sim.Tick();
            var strike = predictor.Session is HammerSession { Ready: true } h
                && (onTheMark ? MathF.Abs(h.Marker - h.Mark) <= h.HalfWidth * 0.5f : MathF.Abs(h.Marker - h.Mark) >= 0.5f);
            predictor.Update(sim, me, 1, strike, false, 0, c => sim.ApplyNow(c));
        }
    }

    [Fact]
    public void ACarefulHandForgesAMasterworkHeatByHeat()
    {
        var (sim, me, kit, _) = AtTheSmithy();
        kit.Drive(sim, me);

        Assert.False(sim.World.Has<WorkSession>(me));
        Assert.True(Smithing.PieceOf(sim, me) == null, string.Join("\n", sim.Messages.Messages.TakeLast(12).Select(m => m.Text)));
        Assert.Null(sim.World.Get<Workbench>(kit.Forge).Active);
        var made = sim.World.Query<Crafted>().Single();
        Assert.Equal(Quality.Masterwork, sim.World.Get<Crafted>(made).Quality);
        Assert.False(sim.World.Has<Flawed>(made));
    }

    [Fact]
    public void BadBlowsMakeACrudePiece()
    {
        var (sim, me, kit, _) = AtTheSmithy();
        kit.Drive(sim, me, SmithStyle.BadAim);

        var made = sim.World.Query<Crafted>().Single();
        Assert.Equal(Quality.Crude, sim.World.Get<Crafted>(made).Quality);
    }

    [Fact]
    public void AHeatEndsWhenThePieceGoesBlackAndItKeepsItsShape()
    {
        var (sim, me, forge, recipe) = AtTheForge();
        Smithing.PieceOf(sim, me)!.Shape = 0.3f;
        Start(sim, me, forge, recipe);
        var predictor = new WorkPredictor();
        for (var t = 0; t < 60 * 60 && sim.World.Has<WorkSession>(me); t++)
        {
            sim.Tick();
            predictor.Update(sim, me, 1, false, false, 0, c => sim.ApplyNow(c));
        }
        sim.Tick();

        var piece = Smithing.PieceOf(sim, me)!;
        Assert.False(sim.World.Has<WorkSession>(me));
        Assert.Equal(0.3f, piece.Shape);
        Assert.True(piece.Temp < Smithing.Window(recipe).Lo * HammerSession.TooCold + 1f, "it went too cold to work");
        Assert.Equal(Glow.Black, Smithing.GlowOf(piece.Temp));
    }

    [Fact]
    public void ThePredictedSessionMatchesWhatTheSimReplays()
    {
        var (sim, me, forge, recipe) = AtTheForge();
        var real = Start(sim, me, forge, recipe);
        var predictor = new WorkPredictor();
        var held = new List<ISimCommand>();

        // Run ahead for a while with the commands held back, as a slow link would.
        for (var t = 0; t < 200; t++)
        {
            sim.Tick();
            var strike = predictor.Session is HammerSession { Ready: true } h && MathF.Abs(h.Marker - h.Mark) <= h.HalfWidth;
            predictor.Update(sim, me, 1, strike, false, 0, held.Add);
        }
        var ahead = (HammerSession)predictor.Session!;
        Assert.True(ahead.Blows > 0);
        Assert.Equal(0, real.Blows);

        foreach (var c in held)
            sim.ApplyNow(c);

        Assert.Equal(ahead.StepsDone, real.StepsDone);
        Assert.Equal(ahead.Blows, real.Blows);
        Assert.Equal(ahead.Mark, real.Mark);
        Assert.Equal(ahead.Quality, real.Quality);
    }

    [Fact]
    public void ACopyDoesNotDisturbTheOriginal()
    {
        var (sim, me, forge, recipe) = AtTheForge();
        var real = Start(sim, me, forge, recipe);
        var mark = real.Mark;
        var copy = (HammerSession)real.Clone();

        copy.Advance([new WorkInput(0, WorkInputKind.Press)], 1);

        Assert.Equal(1, copy.Blows);
        Assert.Equal(0, real.Blows);
        Assert.Equal(mark, real.Mark);
    }

    [Fact]
    public void AnArmStillRecoveringCannotStrike()
    {
        var (sim, me, forge, recipe) = AtTheForge();
        var s = (HammerSession)Start(sim, me, forge, recipe).Clone();
        s.Advance([new WorkInput(0, WorkInputKind.Press), new WorkInput(1, WorkInputKind.Press), new WorkInput(2, WorkInputKind.Press)], 3);
        Assert.Equal(1, s.Blows);
    }

    [Fact]
    public void WalkingAwayStopsTheJob()
    {
        var (sim, me, forge, recipe) = AtTheForge();
        Start(sim, me, forge, recipe);
        var far = C(30, 30);
        sim.World.Get<Position>(me).Cell = far;
        sim.World.Get<Position>(me).Pos = Position.CenterOf(far);
        sim.Tick();
        Assert.False(sim.World.Has<WorkSession>(me));
    }

    [Fact]
    public void BeingDownedStopsTheJob()
    {
        var (sim, me, forge, recipe) = AtTheForge();
        Start(sim, me, forge, recipe);
        sim.World.Get<Creature>(me).Downed = true;
        sim.Tick();
        Assert.False(sim.World.Has<WorkSession>(me));
    }

    [Fact]
    public void AWorkerWhoGoesQuietLosesTheJob()
    {
        var (sim, me, forge, recipe) = AtTheForge();
        Start(sim, me, forge, recipe);
        sim.TickMany(WorkSystem.StallTicks + 2);
        Assert.False(sim.World.Has<WorkSession>(me));
    }

    [Fact]
    public void LeavingTheGameStopsTheJob()
    {
        var (sim, me, forge, recipe) = AtTheForge();
        Start(sim, me, forge, recipe);
        Players.Away(sim, Ana);
        Assert.False(sim.World.Has<WorkSession>(me));
    }

    [Fact]
    public void AJobInProgressSurvivesASave()
    {
        var (sim, me, forge, recipe) = AtTheForge();
        var real = Start(sim, me, forge, recipe);
        sim.TickMany(5);
        sim.ApplyNow(new WorkInputBatchCommand(me, real.StartTick, [new WorkInput(0, WorkInputKind.Press)], 3));

        var (loaded, _) = SaveGame.Read(SaveGame.Snapshot(sim), sim.Defs);

        var copy = (HammerSession)loaded.World.Get<WorkSession>(me);
        Assert.Equal(1, copy.Blows);
        Assert.Equal(real.Mark, copy.Mark);
        Assert.Equal(real.StepsDone, copy.StepsDone);
        Assert.Equal(recipe, copy.Recipe);
        Assert.Equal(sim.FullHash(), loaded.FullHash());
    }

    [Fact]
    public void ABatchFromTheFutureIsRefused()
    {
        var (sim, me, forge, recipe) = AtTheForge();
        var real = Start(sim, me, forge, recipe);
        sim.ApplyNow(new WorkInputBatchCommand(me, real.StartTick, [], 100_000));
        Assert.Equal(0, real.StepsDone);
    }

    [Fact]
    public void ABatchOutOfOrderIsRefused()
    {
        var (sim, me, forge, recipe) = AtTheForge();
        var real = Start(sim, me, forge, recipe);
        sim.TickMany(10);
        sim.ApplyNow(new WorkInputBatchCommand(me, real.StartTick, [new WorkInput(5, WorkInputKind.Press), new WorkInput(1, WorkInputKind.Press)], 8));
        Assert.Equal(0, real.StepsDone);
    }

    [Fact]
    public void ABatchForAnotherJobIsRefused()
    {
        var (sim, me, forge, recipe) = AtTheForge();
        var real = Start(sim, me, forge, recipe);
        sim.TickMany(10);
        sim.ApplyNow(new WorkInputBatchCommand(me, real.StartTick + 1, [], 5));
        Assert.Equal(0, real.StepsDone);
    }

    [Fact]
    public void OutsideARoundForgingStaysAnOrdinaryJob()
    {
        var sim = Create(40, 40);
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), "Smith", C(10, 10), ageDays: 25 * 365);
        sim.World.Add(me, new Avatar());
        var forge = sim.SpawnStructure(sim.Thing("Forge"), C(11, 10));
        Assert.False(Smithing.Applies(sim, me, forge, sim.Defs.Recipe("ForgeAxe")));
    }

    [Fact]
    public void OnlyGearIsHammered()
    {
        var (sim, me, forge, _) = AtTheForge();
        Assert.True(Smithing.Applies(sim, me, forge, sim.Defs.Recipe("ForgeAxe")));
        Assert.False(Smithing.Applies(sim, me, forge, sim.Defs.Recipe("SmeltIron")));
    }

    [Fact]
    public void ABotPlaysTheForgeThroughCommands()
    {
        var (sim, me, forge, recipe) = AtTheForge();
        var bot = new BotPlayer(() => sim, Ana, c => sim.ApplyNow(c), 5);
        Start(sim, me, forge, recipe);

        for (var t = 0; t < 60 * 60 && sim.World.Has<WorkSession>(me); t++)
        {
            sim.Tick();
            bot.Tick();
        }
        sim.Tick();

        // It stops as the colour fades, so the heat's blows are good ones and nothing cracks.
        var piece = Smithing.PieceOf(sim, me)!;
        Assert.False(sim.World.Has<WorkSession>(me));
        Assert.True(piece.Shape > 0.2f, $"shape {piece.Shape}");
        Assert.True(piece.BlowQuality > 0.8f, $"blows {piece.BlowQuality}");
        Assert.Equal(PieceFlaw.None, piece.Flaw);
    }

    [Fact]
    public void ABotGreetsWhoItSeesAndNotWhoItDoesNot()
    {
        var sim = Create(70, 70);
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), "Bot", C(10, 10), ageDays: 25 * 365);
        sim.Round.OpenLobby();
        sim.Round.Sit(Ana, "Bot", null, null, null);
        sim.Round.Start(sim);
        sim.RebuildDerivedIfNeeded();
        Array.Fill(sim.Map.Light, 1f);
        sim.SpawnCreature(sim.Defs.Creature("Human"), "Near", C(13, 10), ageDays: 25 * 365);
        sim.SpawnCreature(sim.Defs.Creature("Human"), "Far", C(60, 60), ageDays: 25 * 365);
        var sent = new List<ISimCommand>();
        var bot = new BotPlayer(() => sim, Ana, sent.Add, 5);

        bot.Tick();

        var say = Assert.IsType<SayCommand>(Assert.Single(sent));
        Assert.Equal(me, say.Actor);
        Assert.Equal("Morning, Near.", say.Text);
    }

    [Fact]
    public void ABotOnlyActsAsItsOwnCharacter()
    {
        var (sim, me, _, _) = AtTheForge();
        var sent = new List<ISimCommand>();
        var bot = new BotPlayer(() => sim, Ana, sent.Add, 5);
        for (var t = 0; t < 200; t++)
        {
            sim.Tick();
            bot.Tick();
        }
        Assert.All(sent, c => Assert.Null(CommandRules.Refusal(sim, c, Ana, isHost: false)));
    }
}

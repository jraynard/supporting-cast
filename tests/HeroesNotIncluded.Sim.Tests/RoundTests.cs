using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Tests;

public class RoundTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Bea = new("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid Cal = new("00000000-0000-0000-0000-00000000000c");

    private static Simulation Town(int villagers = 4, ulong seed = 7)
    {
        var (sim, _) = TestWorld.Millbrook(seed, villagers);
        sim.Round.OpenLobby();
        return sim;
    }

    private static void Sit(Simulation sim, Guid player, string name, params string[] wanted)
    {
        sim.ApplyNow(new JoinLobbyCommand(player, name, default));
        sim.ApplyNow(new SetTradePreferencesCommand(player, wanted.Select(sim.Defs.Trade).ToList(), []));
    }

    private static List<Entity> Humans(Simulation sim) =>
        sim.Creatures().Where(e => sim.World.Get<Creature>(e) is { Faction: Faction.Village, Def.Race: Race.Human }).ToList();

    private static string TradeOf(Simulation sim, Guid player) =>
        sim.World.Get<Trade>(Players.CharacterOf(sim, player)).Def.Name;

    [Fact]
    public void PlayersGetTheTradesTheyAskedFor()
    {
        var sim = Town();
        Sit(sim, Ana, "Ana", "Empath");
        Sit(sim, Bea, "Bea", "Blacksmith");
        sim.ApplyNow(new StartRoundCommand());

        Assert.Equal(RoundPhase.Playing, sim.Round.Phase);
        Assert.Equal("Empath", TradeOf(sim, Ana));
        Assert.Equal("Blacksmith", TradeOf(sim, Bea));
    }

    [Fact]
    public void AContestedTradeGoesToOneAndTheOtherGetsTheirSecondChoice()
    {
        var sim = Town();
        Sit(sim, Ana, "Ana", "Empath", "Innkeeper");
        Sit(sim, Bea, "Bea", "Empath", "Blacksmith");
        sim.ApplyNow(new StartRoundCommand());

        var trades = new[] { TradeOf(sim, Ana), TradeOf(sim, Bea) };
        Assert.Single(trades, "Empath");
        Assert.True(trades.Contains("Innkeeper") || trades.Contains("Blacksmith"));
    }

    [Fact]
    public void ARefusedTradeIsNeverDealt()
    {
        var named = TestWorld.LoadDefs().AllTrades.Where(t => !t.Unlimited).ToList();
        for (var seed = 1UL; seed <= 20; seed++)
        {
            var sim = Town(seed: seed);
            sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default));
            sim.ApplyNow(new SetTradePreferencesCommand(Ana, [], named));
            sim.ApplyNow(new StartRoundCommand());
            Assert.Equal("Musician", TradeOf(sim, Ana)); // what's left: it can't be refused
        }
    }

    [Fact]
    public void TheDealIsTheSameOnEveryPeer()
    {
        string Deal()
        {
            var sim = Town();
            foreach (var (id, name) in new[] { (Ana, "Ana"), (Bea, "Bea"), (Cal, "Cal") })
                Sit(sim, id, name, "Empath", "Innkeeper");
            sim.ApplyNow(new StartRoundCommand());
            return string.Join(",", new[] { Ana, Bea, Cal }.Select(p => TradeOf(sim, p)));
        }
        Assert.Equal(Deal(), Deal());
    }

    [Fact]
    public void PlayersBecomeVillagersSoTheCastStaysTheSameSize()
    {
        var sim = Town(villagers: 4);
        var before = Humans(sim);
        Sit(sim, Ana, "Ana");
        Sit(sim, Bea, "Bea");
        sim.ApplyNow(new StartRoundCommand());

        Assert.Equal(before, Humans(sim));
        var ana = Players.CharacterOf(sim, Ana);
        Assert.Contains(ana, before);
        Assert.True(sim.World.Has<Avatar>(ana));
        Assert.Equal("Ana", sim.World.Get<Creature>(ana).Name);
    }

    [Fact]
    public void MorePlayersThanVillagersBringsNewcomers()
    {
        var sim = Town(villagers: 1);
        var before = Humans(sim).Count;
        Sit(sim, Ana, "Ana");
        Sit(sim, Bea, "Bea");
        Sit(sim, Cal, "Cal");
        sim.ApplyNow(new StartRoundCommand());

        Assert.Equal(Math.Max(before, 3), Humans(sim).Count);
        foreach (var p in new[] { Ana, Bea, Cal })
            Assert.False(Players.CharacterOf(sim, p).IsNull);
    }

    [Fact]
    public void TradesNobodyDrewGoToTheAiWithTheirPriorities()
    {
        var sim = Town(villagers: 4);
        Sit(sim, Ana, "Ana", "Empath");
        sim.ApplyNow(new StartRoundCommand());

        var w = sim.World;
        var ai = Humans(sim).Where(e => !w.Has<PlayerCharacter>(e)).ToList();
        Assert.All(ai, e => Assert.True(w.Has<Trade>(e)));
        Assert.DoesNotContain(ai, e => w.Get<Trade>(e).Def.Name == "Empath");
        var smith = Assert.Single(ai, e => w.Get<Trade>(e).Def.Name == "Blacksmith");
        Assert.Equal(1, w.Get<Mind>(smith).WorkPriority[(int)WorkType.Craft]);
    }

    [Fact]
    public void ALateJoinerTakesTheTradeTheyWantFromTheAiAndKeepsItsFace()
    {
        var sim = Town(villagers: 4);
        Sit(sim, Ana, "Ana", "Empath");
        sim.ApplyNow(new StartRoundCommand());
        var w = sim.World;
        var before = Humans(sim);
        var smith = before.Single(e => w.TryGet<Trade>(e, out var t) && t.Def.Name == "Blacksmith");
        var name = w.Get<Creature>(smith).Name;

        sim.ApplyNow(new TakeTradeCommand(Bea, [sim.Defs.Trade("Blacksmith")], []));

        Assert.Equal(smith, Players.CharacterOf(sim, Bea));
        Assert.Equal(name, w.Get<Creature>(smith).Name);
        Assert.Equal(before, Humans(sim));
    }

    [Fact]
    public void ALeftPlayersCharacterIsHeldForThem()
    {
        var sim = Town(villagers: 1);
        Sit(sim, Ana, "Ana");
        sim.ApplyNow(new StartRoundCommand());
        var ana = Players.CharacterOf(sim, Ana);
        Players.Leave(sim, Ana);

        sim.ApplyNow(new TakeTradeCommand(Bea, [], []));

        Assert.NotEqual(ana, Players.CharacterOf(sim, Bea));
        Assert.True(Players.Join(sim, Ana));
        Assert.Equal(ana, Players.CharacterOf(sim, Ana));
        Assert.True(sim.World.Has<Avatar>(ana));
    }

    [Fact]
    public void SomeoneWithACharacterCantTakeAnother()
    {
        var sim = Town();
        Sit(sim, Ana, "Ana");
        sim.ApplyNow(new StartRoundCommand());
        var ana = Players.CharacterOf(sim, Ana);

        sim.ApplyNow(new TakeTradeCommand(Ana, [], []));

        Assert.Equal(ana, Players.CharacterOf(sim, Ana));
        Assert.Equal(1, sim.World.Count<PlayerCharacter>());
    }

    [Fact]
    public void ARoundHasAFixedCast()
    {
        var sim = Town(villagers: 1);
        Sit(sim, Ana, "Ana");
        sim.ApplyNow(new StartRoundCommand());
        var before = Humans(sim).Count;

        sim.TickMany(SimClock.HoursToTicks(SettlerSystem.MaxDelayHours + 2));

        Assert.Equal(0, sim.World.Count<AwaitingSettler>());
        Assert.Equal(before, Humans(sim).Count);
    }

    [Fact]
    public void OnlyTheHostCanStartTheRound()
    {
        var sim = Town();
        Assert.NotNull(CommandRules.Refusal(sim, new StartRoundCommand(), Ana, isHost: false));
        Assert.Null(CommandRules.Refusal(sim, new StartRoundCommand(), Ana, isHost: true));
    }

    [Fact]
    public void OnlyTheHostSetsTheRoundsOptions()
    {
        var sim = Town();
        Assert.NotNull(CommandRules.Refusal(sim, new SetRoundOptionsCommand(true, false), Ana, isHost: false));
        Assert.Null(CommandRules.Refusal(sim, new SetRoundOptionsCommand(true, false), Ana, isHost: true));
    }

    [Fact]
    public void ARoundHasNoFriendlyFireOrTraitorsUnlessTheHostSaysSo()
    {
        var sim = Town();
        Assert.Equal(RoundOptions.Default, sim.Round.Options);
        sim.ApplyNow(new SetRoundOptionsCommand(true, true));
        Assert.True(sim.Round.Options.FriendlyFire);
        Assert.False(sim.Round.Options.Traitors); // not until Whispers
        Sit(sim, Ana, "Ana", "Empath");
        sim.ApplyNow(new StartRoundCommand());
        sim.ApplyNow(new SetRoundOptionsCommand(false, false));
        Assert.True(sim.Round.Options.FriendlyFire, "the options are settled once the round's dealt");
        Assert.True(sim.Round.FriendlyFire);
    }

    [Fact]
    public void TheRoundsOptionsSurviveASave()
    {
        var sim = Town();
        sim.ApplyNow(new SetRoundOptionsCommand(true, false));
        Sit(sim, Ana, "Ana", "Empath");
        sim.ApplyNow(new StartRoundCommand());

        var (loaded, _) = SaveGame.Read(SaveGame.Snapshot(sim), sim.Defs);

        Assert.Equal(sim.Round.Options, loaded.Round.Options);
        Assert.True(loaded.Round.FriendlyFire);
        Assert.Equal(sim.FullHash(), loaded.FullHash());
    }

    [Fact]
    public void ALobbyAndItsPreferencesSurviveASave()
    {
        var sim = Town();
        Sit(sim, Ana, "Ana", "Empath");
        sim.ApplyNow(new SetReadyCommand(Ana, true));

        var (loaded, _) = SaveGame.Read(SaveGame.Snapshot(sim), sim.Defs);

        Assert.Equal(RoundPhase.Lobby, loaded.Round.Phase);
        var seat = Assert.Single(loaded.Round.Seats);
        Assert.Equal(Ana, seat.Player);
        Assert.True(seat.Ready);
        Assert.Equal("Empath", Assert.Single(seat.Wanted).Name);
        Assert.Equal(sim.FullHash(), loaded.FullHash());
    }

    [Fact]
    public void ARoundInPlaySurvivesASave()
    {
        var sim = Town();
        Sit(sim, Ana, "Ana", "Empath");
        sim.ApplyNow(new StartRoundCommand());

        var (loaded, _) = SaveGame.Read(SaveGame.Snapshot(sim), sim.Defs);

        Assert.Equal(RoundPhase.Playing, loaded.Round.Phase);
        Assert.Equal("Empath", TradeOf(loaded, Ana));
    }

    [Fact]
    public void BadPreferencesAreCleanedUp()
    {
        var sim = Town();
        var healer = sim.Defs.Trade("Empath");
        var musician = sim.Defs.Trade("Musician");
        sim.ApplyNow(new JoinLobbyCommand(Ana, "", default));

        sim.Round.SetPreferences(Ana, [healer, null!, healer], [healer, musician, null!]);

        var seat = sim.Round.SeatOf(Ana)!;
        Assert.Empty(seat.Wanted); // refusing it wins
        Assert.Equal(healer, Assert.Single(seat.Never)); // the unlimited trade can't be refused
        Assert.Equal("Wanderer", seat.Name);
    }

    [Fact]
    public void LobbyCommandsDoNothingOutsideALobby()
    {
        var sim = TestWorld.Create();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default));
        sim.ApplyNow(new StartRoundCommand());
        sim.ApplyNow(new TakeTradeCommand(Ana, [], []));

        Assert.Equal(RoundPhase.None, sim.Round.Phase);
        Assert.Empty(sim.Round.Seats);
        Assert.True(Players.CharacterOf(sim, Ana).IsNull);
    }

    [Fact]
    public void ASinglePlayerSeatLeavesTheCharacterForAHostToAdopt()
    {
        var sim = Town();
        Sit(sim, Guid.Empty, "Solo");
        sim.ApplyNow(new StartRoundCommand());

        Assert.Equal(1, sim.World.Count<Avatar>());
        Assert.Equal(0, sim.World.Count<PlayerCharacter>());
        Assert.True(Players.Join(sim, Ana, adoptLooseAvatar: true));
    }

    [Fact]
    public void EveryoneStartsTheRoundInTheirTradesBuilding()
    {
        var sim = VillageRound.NewSimulation(TestWorld.LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        Sit(sim, Ana, "Ana", "Empath");
        Sit(sim, Bea, "Bea", "Blacksmith");
        sim.ApplyNow(new StartRoundCommand());

        var w = sim.World;
        var placed = 0;
        foreach (var e in Humans(sim))
        {
            if (!w.TryGet<Trade>(e, out var trade) || !trade.Def.HasWorkplace)
                continue;
            var inside = Buildings.At(sim, w.Get<Position>(e).Cell);
            Assert.False(inside.IsNull, $"{w.Get<Creature>(e).Name} ({trade.Def.Name}) isn't in a building");
            Assert.True(trade.Def.WorksAt(w.Get<PresetBuilding>(inside).Def));
            placed++;
        }
        Assert.True(placed >= 4, $"only {placed} placed"); // the four named trades, players and AI alike
        Assert.Equal("Shrine", w.Get<PresetBuilding>(Buildings.At(sim, w.Get<Position>(Players.CharacterOf(sim, Ana)).Cell)).Def.Name);
    }

    [Fact]
    public void ABlacksmithCanForgeFromTheFirstMorning()
    {
        var sim = VillageRound.NewSimulation(TestWorld.LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        Sit(sim, Guid.Empty, "Arin", "Blacksmith");
        sim.ApplyNow(new StartRoundCommand());
        var w = sim.World;
        var me = w.Query<Avatar>().Single();
        var forge = w.Query<Thing>().Single(e => w.Get<Thing>(e).Def.Name == "Forge");
        var spear = sim.Defs.Recipe("ForgeIronSpear");

        Assert.True(RecipeBooks.Knows(sim, me, spear), "a blacksmith knows the forge's recipes");
        Assert.True(SkillRules.LevelOf(sim, me, Skill.Crafting) >= 6);
        Assert.Null(Crafting.MissingTool(sim, me, forge, spear));
        Assert.False(Crafting.HotEnough(sim, forge, spear));
        Assert.True(Crafting.CanFireFor(sim, forge, spear), "a cold forge with fuel in it is ready to start: starting is what lights it");

        // In reach of the forge and the smithy's chest (the spear's third bar is in it), not through a wall.
        var chest = w.Query<Thing>().First(e => w.Get<Thing>(e).Def.Name == "Chest" && Buildings.At(sim, w.Get<Thing>(e).Cell) == Buildings.At(sim, w.Get<Thing>(forge).Cell));
        var beside = sim.SpiralFrom(w.Get<Thing>(forge).Cell, 4).First(c => sim.Map.Passable(c) && AvatarSystem.InReach(sim, c, forge) && AvatarSystem.InReach(sim, c, chest));
        w.Get<Position>(me).Cell = beside;
        w.Get<Position>(me).Pos = Position.CenterOf(beside);
        Assert.True(Work.Kindling.IsCold(sim, forge), "the round opens with the forge to light");
        Work.Kindling.Light(sim, forge); // the morning's first job (KindlingTests)
        sim.ApplyNow(new CraftCommand(me, forge, spear)); // brings the kit's bars: the billet's in the tongs, and the forge fires
        Assert.True(Work.Smithing.PieceOf(sim, me) != null, string.Join(" | ", sim.Messages.Messages.TakeLast(4).Select(m => m.Text)));
        Assert.Contains("billet", sim.Messages.Messages[^1].Text);
        sim.ApplyNow(new Work.SmithCommand(me, forge));

        Assert.True(w.Get<Work.WorkSession>(me) is Work.HeatSession, "cold or not, it goes in the fire: the bellows bring it up");
    }

    [Fact]
    public void AForgeWithNoFuelCantBeStarted()
    {
        var sim = TestWorld.Create();
        var forge = sim.SpawnStructure(sim.Thing("Forge"), TestWorld.C(10, 10));
        var spear = sim.Defs.Recipe("ForgeIronSpear");
        sim.World.Get<Fueled>(forge).Fuel = 0;
        Assert.False(Crafting.CanFireFor(sim, forge, spear));
        sim.World.Get<Fueled>(forge).Fuel = 5;
        Assert.True(Crafting.CanFireFor(sim, forge, spear));
    }

    [Fact]
    public void InARoundOnlyAGatheringTradeDigsChopsAndForages()
    {
        var sim = Town();
        Sit(sim, Ana, "Ana", "Empath");
        sim.ApplyNow(new StartRoundCommand());
        var ana = Players.CharacterOf(sim, Ana);
        Assert.False(RoundSystem.MayGather(sim, ana));

        // Nobody's offered a dig or a harvest who isn't a gatherer.
        var w = sim.World;
        var tree = w.Query<Thing>().First(e => w.Get<Thing>(e).Def.Harvest is { WorkType: WorkType.Chop });
        var at = w.Get<Thing>(tree).Cell;
        Assert.NotEqual(AvatarActionKind.Harvest, AvatarSystem.ActionAt(sim, ana, at, AvatarTool.Use, at + new int3(1, 0, 0)).Kind);

        // Nor does the AI: its trades leave it alone, and so does a villager with no trade.
        foreach (var e in Humans(sim).Where(e => !w.Has<PlayerCharacter>(e)))
        {
            var gathers = w.TryGet<Trade>(e, out var t) && t.Def.Gathers;
            foreach (var work in RoundSystem.GatheringWork)
                if (!(work == WorkType.Mine && w.TryGet<Trade>(e, out var m) && m.Def.Mines))
                    Assert.Equal(gathers, w.Get<Mind>(e).WorkPriority[(int)work] > 0);
        }
    }
}

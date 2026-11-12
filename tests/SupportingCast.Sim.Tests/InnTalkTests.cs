using SupportingCast.Sim.Bots;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Hospitality;
using SupportingCast.Sim.Round;
using SupportingCast.Sim.Saving;
using SupportingCast.Sim.Scenarios;
using SupportingCast.Sim.Shops;
using SupportingCast.Sim.Speech;
using SupportingCast.Sim.Systems;
using SupportingCast.Sim.Work;
using static SupportingCast.Sim.Tests.TestWorld;

namespace SupportingCast.Sim.Tests;

/// <summary>Mixed drinks, drink taking hold, and finding the way round town.</summary>
public class InnTalkTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    /// <summary>A bar counter at (10, 12) with a cask at (10, 10) behind it, and a player between at (10, 11) with a tankard.</summary>
    private static (Simulation Sim, Entity Me, Entity Bar, Entity Cask) Bar()
    {
        var sim = Create(40, 40);
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), "Inna", C(10, 11), ageDays: 25 * 365);
        sim.Round.OpenLobby();
        sim.Round.Sit(Ana, "Inna", null, null, null);
        sim.Round.Start(sim);
        sim.GiveStartingPack(me);
        var cask = sim.SpawnStructure(sim.Thing("AleCask"), C(10, 10));
        var bar = sim.SpawnStructure(sim.Thing("BarCounter"), C(10, 12));
        sim.RebuildDerivedIfNeeded();
        sim.World.Get<Carrying>(me).Item = sim.CreateItem(sim.Thing("Tankard"), 2);
        return (sim, me, bar, cask);
    }

    private static void Give(Simulation sim, Entity who, string def, int count = 1) => Inventory.Stow(sim, who, sim.CreateItem(sim.Thing(def), count));

    /// <summary>Mix as a player would: each cue's key as it reaches the mark (or the wrong key, fumbling).</summary>
    private static void Mix(Simulation sim, Entity me, bool fumble = false)
    {
        var predictor = new WorkPredictor();
        var done = -1;
        for (var t = 0; t < 60 * 30 && sim.World.Has<WorkSession>(me); t++)
        {
            sim.Tick();
            bool press = false;
            var axis = 0;
            if (predictor.Session is MixSession m && m.Resolved < m.Cues.Length && m.Resolved != done && m.StepsDone >= m.DueOf(m.Resolved))
            {
                var cue = m.Cues[m.Resolved];
                if (fumble)
                    cue = cue == 'F' ? 'A' : 'F';
                press = cue == 'F';
                axis = cue == 'A' ? -1 : cue == 'D' ? 1 : 0;
                done = m.Resolved;
            }
            predictor.Update(sim, me, 1, press, false, axis, c => sim.ApplyNow(c));
        }
        sim.Tick();
    }

    [Fact]
    public void ANamedDrinkMixedOnCueIsFineAndDoesWhatsInIt()
    {
        var (sim, me, bar, cask) = Bar();
        Give(sim, me, "Spirits");
        Give(sim, me, "Honey");
        var full = PourSession.ServingsLeft(sim, cask);
        sim.ApplyNow(new MixCommand(me, bar, sim.Thing("Hearthwarmer"), null));
        Assert.IsType<MixSession>(sim.World.Get<WorkSession>(me));
        Mix(sim, me);

        var drink = sim.World.Get<Carrying>(me).Item;
        Assert.Equal("Hearthwarmer", sim.World.Get<Thing>(drink).Def.Name);
        Assert.True(sim.World.Get<Crafted>(drink).Quality >= Quality.Fine, $"{sim.World.Get<Crafted>(drink).Quality}");
        var mix = sim.World.Get<DrinkMix>(drink);
        Assert.True(mix.Warming > 0.5f && mix.Strength > 0.4f, $"warming {mix.Warming}, strength {mix.Strength}"); // spirits and honey on top of the ale
        Assert.Equal(full - 1, PourSession.ServingsLeft(sim, cask));
        Assert.True(AvatarSystem.Supply(sim, me, d => d.Name is "Spirits" or "Honey").IsNull, "the adds went in");
    }

    [Fact]
    public void AFumbledMixIsCrude()
    {
        var (sim, me, bar, _) = Bar();
        Give(sim, me, "Berries");
        Give(sim, me, "Herbs");
        sim.ApplyNow(new MixCommand(me, bar, sim.Thing("Bramblecup"), null));
        Mix(sim, me, fumble: true);
        Assert.Equal(Quality.Crude, sim.World.Get<Crafted>(sim.World.Get<Carrying>(me).Item).Quality);
    }

    [Fact]
    public void AHouseMixDoesWhateverItsIngredientsDo()
    {
        var (sim, me, bar, _) = Bar();
        Give(sim, me, "Herbs");
        Give(sim, me, "Honey");
        sim.ApplyNow(new MixCommand(me, bar, sim.Thing("HouseMix"), "Herbs,Honey"));
        var s = (MixSession)sim.World.Get<WorkSession>(me);
        Assert.Equal("DAF", s.Cues); // one cue per add, then the stir
        Mix(sim, me);
        var mix = sim.World.Get<DrinkMix>(sim.World.Get<Carrying>(me).Item);
        Assert.True(mix.Numbing > 0.2f && mix.Warming > 0.1f, $"numbing {mix.Numbing}, warming {mix.Warming}");
        Assert.Equal("herbs and honey", mix.Adds);
    }

    [Fact]
    public void NothingToAddNoMix()
    {
        var (sim, me, bar, _) = Bar();
        sim.ApplyNow(new MixCommand(me, bar, sim.Thing("Hearthwarmer"), null));
        Assert.False(sim.World.Has<WorkSession>(me));
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("spirits"));
    }

    // ---------------- Drink taking hold ----------------

    private static (Simulation Sim, List<Entity> Party) Tavern(string playerTrade)
    {
        var sim = VillageRound.NewSimulation(LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default, []));
        sim.ApplyNow(new SetTradePreferencesCommand(Ana, [sim.Defs.Trade(playerTrade)], []));
        sim.ApplyNow(new StartRoundCommand());
        var party = sim.Parties.Send(sim, sim.Defs.Party("GildedSpurs"));
        return (sim, party);
    }

    private static Entity Drink(Simulation sim, string def)
    {
        var d = sim.CreateItem(sim.Thing(def), 1);
        sim.World.Add(d, new Crafted { Quality = Quality.Plain, Maker = Entity.Null });
        return d;
    }

    [Fact]
    public void DrinkGoesToTheHeadAndWearsOff()
    {
        var (sim, party) = Tavern("Innkeeper");
        var a = party[0];
        for (var i = 0; i < 3; i++)
            sim.Drinking.Drank(sim, a, Drink(sim, "Ale"), null);
        var level = DrinkingSystem.LevelOf(sim, a);
        Assert.True(level >= DrinkingSystem.MerryAt, $"{level}");
        Assert.Equal("merry", Perception.For(sim, Players.CharacterOf(sim, Ana)).Drunkenness(a) ?? Perception.For(sim, Entity.Null).Drunkenness(a));
        sim.TickMany(SimClock.HoursToTicks(6));
        Assert.True(DrinkingSystem.LevelOf(sim, a) < level);
    }

    [Fact]
    public void ALoudDrinkerCarriesFurther()
    {
        var sim = Create(60, 20);
        var talker = sim.SpawnCreature(sim.Defs.Creature("Human"), "Loud", C(5, 10), ageDays: 25 * 365);
        var far = sim.SpawnCreature(sim.Defs.Creature("Human"), "Far", C(5 + (int)SpeechSystem.SayRange + 3, 10), ageDays: 25 * 365);
        sim.RebuildDerivedIfNeeded();
        var sober = sim.Speech.Say(sim, talker, "Hello there.", Volume.Say)!;
        Assert.DoesNotContain(sober.Listeners, h => h.Listener == far && h.Clarity == Clarity.Clear);
        sim.World.Add(talker, new Tipsy { Level = DrinkingSystem.LoudAt + 0.1f });
        var loud = sim.Speech.Say(sim, talker, "HELLO THERE.", Volume.Say)!;
        Assert.Contains(loud.Listeners, h => h.Listener == far && h.Clarity == Clarity.Clear);
    }

    [Fact]
    public void AMerryDrinkerTellsRumoursAndTruerOnes()
    {
        var (sim, party) = Tavern("Innkeeper");
        var me = Players.CharacterOf(sim, Ana);
        sim.TickMany(SimClock.TicksPerHour / 12 + 1); // the round's quests are rolled
        Assert.NotEmpty(sim.Expeditions.Quests);
        var teller = party[0];
        var at = sim.World.Get<Position>(teller).Cell;
        sim.World.Get<Position>(me).Cell = sim.SpiralFrom(at, 2).First(c => c != at && sim.Map.Passable(c) && c.Z == at.Z);
        sim.World.Get<Position>(me).Pos = Position.CenterOf(sim.World.Get<Position>(me).Cell);
        var before = sim.Speech.Kept.Count;
        sim.Expeditions.Gossip(sim, teller, spread: 0.05f);
        var said = sim.Speech.Kept.Skip(before).SingleOrDefault(u => u.Speaker == teller)
            ?? throw new Xunit.Sdk.XunitException($"nothing said: canAct {SpeechSystem.CanAct(sim, teller)}, quests {sim.Expeditions.Quests.Count}, said lately {sim.Speech.Kept.Count(u => u.Speaker == teller)}");
        Assert.Contains(said.Listeners, h => h.Listener == me);
        var quests = Perception.For(sim, me).Quests().ToList();
        Assert.Contains(quests, q => q.Confidence > 0.2f); // they heard it, and took it in
    }

    [Fact]
    public void FarGoneTheyFallAsleepWhereTheySit()
    {
        var (sim, party) = Tavern("Innkeeper");
        var a = party[0];
        sim.World.Add(a, new Tipsy { Level = DrinkingSystem.OutAt + 0.2f });
        for (var i = 0; i < 12 && !sim.World.Get<Mind>(a).Asleep; i++)
            sim.TickMany(SimClock.TicksPerHour / 8);
        Assert.True(sim.World.Get<Mind>(a).Asleep);
    }

    [Fact]
    public void ACustomerWhoAskedForANamedDrinkCaresWhatTheyGet()
    {
        var (sim, party) = Tavern("Innkeeper");
        var a = party[0];
        var order = sim.Shops.Post(sim, a, sim.Defs.Service(ServiceKind.Drink), "A drink!", 2, sim.Clock.Tick + SimClock.TicksPerHour * 2);
        // Ask until one wants something in particular.
        for (var i = 0; i < 40 && sim.Drinking.Wanted(order.Id) == null; i++)
        {
            order = sim.Shops.Post(sim, party[i % party.Count], sim.Defs.Service(ServiceKind.Drink), "A drink!", 2, sim.Clock.Tick + SimClock.TicksPerHour * 2);
            a = order.Poster;
        }
        var wanted = sim.Drinking.Wanted(order.Id);
        Assert.NotNull(wanted);
        var mood = sim.World.Get<Adventurer>(a).Satisfaction;
        sim.Drinking.Drank(sim, a, Drink(sim, wanted!), order);
        Assert.True(sim.World.Get<Adventurer>(a).Satisfaction >= mood + DrinkingSystem.RightDrinkMood - 0.01f);
    }

    [Fact]
    public void ABotInnkeeperMixesTheNamedDrinkItHearsAskedFor()
    {
        var (sim, party) = Tavern("Innkeeper");
        foreach (var e in party)
        {
            sim.World.Get<Health>(e).Injuries.Clear();
            sim.World.Get<Adventurer>(e).NextDrink = sim.Clock.Tick + SimClock.TicksPerDay;
        }
        var bot = new BotPlayer(() => sim, Ana, c => sim.ApplyNow(c), 3);
        var me = Players.CharacterOf(sim, Ana);
        // Let them in, then one orders until it's for something in particular.
        sim.TickMany(SimClock.TicksPerHour / 2);
        Order? order = null;
        for (var i = 0; i < 60 && (order == null || sim.Drinking.Wanted(order.Id) == null); i++)
        {
            if (order != null)
                order.Status = OrderStatus.Cancelled;
            order = sim.Shops.Post(sim, party[i % party.Count], sim.Defs.Service(ServiceKind.Drink), "A drink!", 2, sim.Clock.Tick + SimClock.TicksPerHour * 3);
            ShopSystem.Learn(order, me);
        }
        var wanted = sim.Drinking.Wanted(order!.Id)!;
        for (var t = 0; t < SimClock.TicksPerHour * 2 && order.IsOpen; t++)
        {
            sim.Tick();
            bot.Tick();
        }
        Assert.True(order.Status == OrderStatus.Done, $"{order.Status}: bot {bot.Doing}\n" + string.Join("\n", sim.Messages.Messages.TakeLast(8).Select(m => m.Text)));
        Assert.Contains(sim.Speech.Kept, u => u.Speaker == order.Poster && u.Text.Contains($"A proper {sim.Thing(wanted).Label}"));
    }

    // ---------------- Finding the way ----------------

    private static Entity Smithy(Simulation sim) => ShopSystem.BuildingOf(sim, sim.Defs.Building("Smithy"));

    /// <summary>A party member with a worn blade, standing in the inn, who doesn't know where the smithy is.</summary>
    private static (Simulation Sim, Entity Lost, Entity Me) LostAtTheInn(string playerTrade)
    {
        var (sim, party) = Tavern(playerTrade);
        var lost = party[0];
        var inn = ShopSystem.BuildingOf(sim, sim.Defs.Building("Inn"));
        var b = sim.World.Get<PresetBuilding>(inn);
        var cell = Buildings.CellOf(sim.Map, b.Def, b.Anchor, b.Facing, 6, 10);
        foreach (var e in party)
        {
            sim.World.Remove<KnowsTheWay>(e);
            sim.World.Get<Position>(e).Cell = cell;
            sim.World.Get<Position>(e).Pos = Position.CenterOf(cell);
        }
        var me = Players.CharacterOf(sim, Ana);
        sim.World.Get<Position>(me).Cell = Buildings.CellOf(sim.Map, b.Def, b.Anchor, b.Facing, 6, 9);
        sim.World.Get<Position>(me).Pos = Position.CenterOf(sim.World.Get<Position>(me).Cell);
        var blade = sim.World.Get<Equipment>(lost).Weapon;
        sim.World.Get<Thing>(blade).Hp = sim.World.Get<Thing>(blade).MaxHp * 0.4f;
        return (sim, lost, me);
    }

    [Fact]
    public void NotKnowingTheWayTheyAskAndAPlayerPointsIt()
    {
        var (sim, lost, me) = LostAtTheInn("Innkeeper");
        Assert.False(DirectionsSystem.Knows(sim, lost, Smithy(sim)));
        sim.TickMany(SimClock.TicksPerHour / 6);
        var ask = Perception.For(sim, me).WaysAsked().FirstOrDefault(a => a.Asker == lost);
        Assert.Equal("smithy", ask.To);
        Assert.DoesNotContain(sim.Shops.All, e => e.Poster == lost && e.Kind.Kind == ServiceKind.Repair);

        sim.ApplyNow(new DirectCommand(me, ask.Id));
        Assert.True(DirectionsSystem.Knows(sim, lost, Smithy(sim)));
        sim.TickMany(SimClock.TicksPerHour / 4);
        Assert.Contains(sim.Shops.All, e => e.Poster == lost && e.Kind.Kind == ServiceKind.Repair);
    }

    [Fact]
    public void LeftUnansweredAVillagerTellsThemInTheEnd()
    {
        var (sim, lost, _) = LostAtTheInn("Healer"); // an AI innkeeper hears them
        for (var i = 0; i < 12 && !DirectionsSystem.Knows(sim, lost, Smithy(sim)); i++)
            sim.TickMany(SimClock.TicksPerHour / 6);
        Assert.True(DirectionsSystem.Knows(sim, lost, Smithy(sim)));
    }

    [Fact]
    public void WalkingPastAShopTheySeeItForWhatItIs()
    {
        var (sim, party) = Tavern("Innkeeper");
        var a = party[0];
        sim.World.Remove<KnowsTheWay>(a);
        var smithy = Smithy(sim);
        var near = sim.SpiralFrom(sim.World.Get<PresetBuilding>(smithy).Anchor + new int3(0, 6, 0), 4).First(sim.Map.Passable);
        sim.World.Get<Position>(a).Cell = near;
        sim.World.Get<Position>(a).Pos = Position.CenterOf(near);
        sim.TickMany(SimClock.TicksPerHour / 6);
        Assert.True(DirectionsSystem.Knows(sim, a, smithy));
        Assert.True(DirectionsSystem.Knows(sim, party[1], smithy), "and the rest of the party hears of it");
    }

    [Fact]
    public void DrinkAndTheWaySurviveASave()
    {
        var (sim, lost, _) = LostAtTheInn("Innkeeper");
        sim.TickMany(SimClock.TicksPerHour / 6);
        sim.World.Add(lost, new Tipsy { Level = 0.4f });
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        Assert.Equal(0.4f, DrinkingSystem.LevelOf(loaded, lost), 3);
        Assert.Equal(sim.Directions.Open.Count, loaded.Directions.Open.Count);
        Assert.Equal(sim.World.TryGet<KnowsTheWay>(lost, out var k) ? k.Shops.Count : 0, loaded.World.TryGet<KnowsTheWay>(lost, out var k2) ? k2.Shops.Count : 0);
    }
}

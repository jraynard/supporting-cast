using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Expeditions;
using HeroesNotIncluded.Sim.Hospitality;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>The cook pass: flavours in the pot, diners' cravings, sniffing for rot, and rations for the road.</summary>
public class CookPassTests
{
    // ---------------- Flavours ----------------

    [Fact]
    public void APotTastesOfWhatWentInBlendedByHowMuch()
    {
        var (sim, me, pot) = InnKitchenTests.Kitchen();
        InnKitchenTests.Fill(sim, me, pot, ("Meat", 6));
        var p = sim.World.Get<StewPot>(pot);
        Assert.Equal(FlavourKind.Savoury, p.Flavour.Leading);
        Assert.Equal("savoury", p.Flavour.Words());

        InnKitchenTests.Fill(sim, me, pot, ("Cabbage", 5));
        Assert.True(p.Flavour.Has(FlavourKind.Fresh));
        Assert.Equal(FlavourKind.Savoury, p.Flavour.Leading); // 0.9 of meat against 0.6 of cabbage
        // A little of it: a hint, not a taste of its own.
        Assert.Equal("savoury, with something green", Perception.For(sim, me).Pot(pot)!.Value.Tastes);
    }

    [Fact]
    public void ASeasonerFlavoursThePotButAddsNoBowls()
    {
        var (sim, me, pot) = InnKitchenTests.Kitchen();
        var at = sim.World.Get<Thing>(pot).Cell;
        // Into an empty pot it has nothing to flavour: the work key doesn't offer it.
        sim.World.Get<Carrying>(me).Item = sim.CreateItem(sim.Thing("Emberroot"), 5);
        Assert.NotEqual(AvatarActionKind.Cook, AvatarSystem.ActionAt(sim, me, at).Kind);
        sim.World.Get<Carrying>(me).Item = Entity.Null;

        InnKitchenTests.Fill(sim, me, pot, ("Meat", 6));
        sim.World.Get<StewPot>(pot).Doneness = 1f;
        var bowls = Cooking.ServingsLeft(sim, pot);
        var root = sim.CreateItem(sim.Thing("Emberroot"), 5);
        sim.World.Get<Carrying>(me).Item = root;
        for (var i = 0; i < Cooking.MaxSeasoners; i++)
        {
            Assert.Equal("Put the emberroot in the pot", AvatarSystem.ActionAt(sim, me, at).Label);
            sim.ApplyNow(new PotCommand(me, pot, PotStep.Next));
        }
        var p = sim.World.Get<StewPot>(pot);
        Assert.Equal(Cooking.MaxSeasoners, Cooking.SeasonersIn(p));
        Assert.Equal(5 - Cooking.MaxSeasoners, sim.World.Get<Stack>(root).Count);
        Assert.Equal(bowls, Cooking.ServingsLeft(sim, pot));
        Assert.True(p.Flavour.Has(FlavourKind.Hot));
        // Enough: the rest of the root stays in hand and the key goes back to the pot's own work.
        Assert.NotEqual("Put the emberroot in the pot", AvatarSystem.ActionAt(sim, me, at).Label);
    }

    [Fact]
    public void ARoundedPotOutscoresAOneNoteOne()
    {
        var flat = new StewPot { Doneness = 1f, Flavour = new Flavour(1f, 0, 0, 0) };
        var rounded = new StewPot { Doneness = 1f, Flavour = new Flavour(0.6f, 0.1f, 0.15f, 0.3f) };
        Assert.True(Cooking.QualityOf(rounded) > Cooking.QualityOf(flat) + 0.05f,
            $"{Cooking.QualityOf(rounded):0.00} against {Cooking.QualityOf(flat):0.00}");
        Assert.Equal(0f, new Flavour(1f, 0, 0, 0).Balance);
        Assert.True(new Flavour(1f, 1f, 1f, 0).Balance > 0.95f);
    }

    [Fact]
    public void ABowlCarriesThePotsFlavourAndAnyoneCanTasteIt()
    {
        var (sim, me, pot) = InnKitchenTests.Kitchen();
        InnKitchenTests.Fill(sim, me, pot, ("Meat", 6), ("Berries", 6));
        sim.World.Get<StewPot>(pot).Doneness = 1f;
        var bowl = Cooking.Ladle(sim, me, pot);
        var dish = sim.World.Get<Dish>(bowl);
        Assert.True(dish.Flavour.Has(FlavourKind.Sweet) && dish.Flavour.Has(FlavourKind.Savoury));
        Assert.Equal(1f, dish.Flavour.Total, 0.001f);
        Assert.Equal(dish.Flavour.Words(), Perception.For(sim, me).Tastes(bowl));
    }

    [Fact]
    public void FlavourAndDishesSurviveASave()
    {
        var (sim, me, pot) = InnKitchenTests.Kitchen();
        InnKitchenTests.Fill(sim, me, pot, ("Meat", 6), ("Cabbage", 3));
        sim.World.Get<StewPot>(pot).Doneness = 1f;
        var bowl = Cooking.Ladle(sim, me, pot);
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        Assert.Equal(sim.World.Get<StewPot>(pot).Flavour, loaded.World.Get<StewPot>(pot).Flavour);
        Assert.Equal(sim.World.Get<Dish>(bowl).Flavour, loaded.World.Get<Dish>(bowl).Flavour);
    }

    // ---------------- Cravings ----------------

    [Fact]
    public void ADinerPleasedOrNotByWhatTheyFancied()
    {
        var (sim, _, party) = ExpeditionTests.Round("Cook");
        var diner = party[0];
        var meal = sim.Defs.Service(ServiceKind.Meal);
        ref var a = ref sim.World.Get<Adventurer>(diner);
        var before = a.Satisfaction;
        Tastes.Served(sim, diner, new Flavour(0.5f, 0, 0.5f, 0), new Order { Kind = meal, Craving = FlavourKind.Hot });
        Assert.Equal(before + Tastes.RightMood, sim.World.Get<Adventurer>(diner).Satisfaction, 0.001f);
        Tastes.Served(sim, diner, new Flavour(1f, 0, 0, 0), new Order { Kind = meal, Craving = FlavourKind.Sweet });
        Assert.Equal(before + Tastes.RightMood - Tastes.WrongMood, sim.World.Get<Adventurer>(diner).Satisfaction, 0.001f);
        // Fancying nothing in particular, it's only the pot's quality that counts.
        Tastes.Served(sim, diner, new Flavour(1f, 0, 0, 0), new Order { Kind = meal });
        Assert.Equal(before + Tastes.RightMood - Tastes.WrongMood, sim.World.Get<Adventurer>(diner).Satisfaction, 0.001f);
    }

    [Fact]
    public void WhatADinerFanciesIsOneOfTheirKindsTastes()
    {
        var (sim, _, party) = ExpeditionTests.Round("Cook");
        var rand = new SimRandom(7);
        var said = new HashSet<FlavourKind>();
        foreach (var e in party)
        {
            var tastes = sim.World.Get<Adventurer>(e).Kind!.Tastes;
            for (var i = 0; i < 40; i++)
                if (Tastes.Crave(sim, e, rand) is { } c)
                {
                    Assert.Contains(c, tastes);
                    said.Add(c);
                }
        }
        Assert.NotEmpty(said);
    }

    [Fact]
    public void ACravingIsSaidWithTheOrderAndSurvivesASave()
    {
        var (sim, _, _) = ExpeditionTests.Round("Cook");
        for (var h = 0; h < 24 && !sim.Shops.All.Any(o => o.Craving != null); h++)
            sim.TickMany(SimClock.TicksPerHour);
        var order = sim.Shops.All.First(o => o.Craving != null);
        Assert.Equal(ServiceKind.Meal, order.Kind.Kind);
        Assert.Contains(Flavour.Craving(order.Craving!.Value), order.Text);
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        Assert.Equal(order.Craving, loaded.Shops.All.Single(o => o.Id == order.Id).Craving);
    }

    // ---------------- Sniffing ----------------

    /// <summary>Sniff over and over, a breath apart; every word the nose came back with.</summary>
    private static List<string> Sniffs(SniffSession s, int times)
    {
        var words = new List<string>();
        for (var i = 0; i < times; i++)
        {
            var at = s.StepsDone;
            s.Advance([new WorkInput(at, WorkInputKind.Act)], at + SniffSession.Breath + 1);
            words.Add(s.Smell);
        }
        return words;
    }

    [Fact]
    public void ASkilledNoseTellsTurnedFoodFromFresh()
    {
        var (sim, me, _) = InnKitchenTests.Kitchen();
        sim.SetSkills(me, 8);
        var bad = sim.CreateItem(sim.Thing("Meat"), 3);
        sim.World.Get<Rot>(bad).Progress = 0.8f;
        Inventory.Stow(sim, me, bad);
        sim.ApplyNow(new SniffCommand(me, bad));
        var s = Assert.IsType<SniffSession>(sim.World.Get<WorkSession>(me));
        Assert.Equal("", s.Smell); // nothing till they sniff
        Assert.All(Sniffs(s, 20), w => Assert.Equal("off", w));
        sim.ApplyNow(new StopWorkCommand(me));
        sim.Tick();

        var good = sim.CreateItem(sim.Thing("Cabbage"), 3);
        Inventory.Stow(sim, me, good);
        sim.ApplyNow(new SniffCommand(me, good));
        Assert.All(Sniffs((SniffSession)sim.World.Get<WorkSession>(me), 20), w => Assert.Contains(w, new[] { "fresh", "fine" }));
    }

    [Fact]
    public void APotOnlyEverSmellsNotQuiteRight()
    {
        var (sim, me, pot) = InnKitchenTests.Kitchen();
        sim.SetSkills(me, 8);
        var meat = sim.CreateItem(sim.Thing("Meat"), 6);
        sim.World.Get<Rot>(meat).Progress = 0.9f;
        sim.World.Get<Carrying>(me).Item = meat;
        sim.ApplyNow(new PotCommand(me, pot, PotStep.Add));
        sim.ApplyNow(new PotCommand(me, pot, PotStep.Sniff));
        var s = Assert.IsType<SniffSession>(sim.World.Get<WorkSession>(me));
        Assert.True(s.IsPot);
        Assert.All(Sniffs(s, 10), w => Assert.Equal("not quite right", w));
        // Nothing can be sniffed out of an empty pot.
        var (other, _, empty) = InnKitchenTests.Kitchen();
        Assert.Null(SniffSession.For(other, empty));
    }

    // ---------------- Rations ----------------

    /// <summary>A prep table beside the kitchen test's cook (at 10, 12), and three portions of food in hand.</summary>
    private static (Simulation Sim, Entity Me, Entity Table) PrepKitchen(params (string Def, int Count)[] food)
    {
        var (sim, me, _) = InnKitchenTests.Kitchen();
        var table = sim.SpawnStructure(sim.Thing("PrepTable"), C(10, 12));
        sim.RebuildDerivedIfNeeded();
        foreach (var (def, count) in food)
            Inventory.Stow(sim, me, sim.CreateItem(sim.Thing(def), count));
        return (sim, me, table);
    }

    /// <summary>Fold the four corners on cue, from their sides, then draw the cord and let go after so many steps.</summary>
    private static List<WorkInput> Packing(int pullSteps, bool wrongSide = false)
    {
        var inputs = new List<WorkInput>();
        var cue = (int)(PackSession.Beat * 0.5f * SimClock.TicksPerSecond);
        var gap = (int)(PackSession.Beat * SimClock.TicksPerSecond);
        for (var i = 0; i < PackSession.Folds; i++)
        {
            var side = i % 2 == 0 ? -1 : 1;
            inputs.Add(new WorkInput(cue + i * gap, WorkInputKind.Axis, wrongSide ? -side : side));
            inputs.Add(new WorkInput(cue + i * gap + 1, WorkInputKind.Axis, 0));
        }
        var pull = cue + PackSession.Folds * gap;
        inputs.Add(new WorkInput(pull, WorkInputKind.Hold, 1));
        inputs.Add(new WorkInput(pull + pullSteps, WorkInputKind.Hold, 0));
        return inputs;
    }

    private static Entity Pack(Simulation sim, Entity me, Entity table, List<WorkInput> inputs)
    {
        var at = sim.World.Get<Thing>(table).Cell;
        Assert.Equal("Pack rations for the road", AvatarSystem.ActionAt(sim, me, at).Label);
        sim.ApplyNow(new AvatarWorkCommand(me, true, at));
        sim.ApplyNow(new AvatarWorkCommand(me, false));
        var s = Assert.IsType<PackSession>(sim.World.Get<WorkSession>(me));
        s.Advance(inputs, inputs[^1].Step + 5);
        Assert.True(s.Done);
        sim.Tick();
        Assert.False(sim.World.Has<WorkSession>(me));
        return Rations.Carried(sim, me).Single();
    }

    [Fact]
    public void ThreePortionsPackedWellMakeAGoodBundle()
    {
        var (sim, me, table) = PrepKitchen(("Meat", 2), ("Cabbage", 2));
        var ration = Pack(sim, me, table, Packing(70));
        var r = sim.World.Get<Ration>(ration);
        Assert.False(r.LooseWrap);
        Assert.True(sim.World.Get<Crafted>(ration).Quality >= Quality.Fine, $"{sim.World.Get<Crafted>(ration).Quality} ({r.Quality:0.00})");
        Assert.Equal(1, Rations.PortionsOn(sim, me)); // two meat and one cabbage went in
        Assert.True(r.Flavour.Has(FlavourKind.Savoury) && r.Flavour.Has(FlavourKind.Fresh));
        Assert.Equal(me, r.Maker);
        Assert.NotNull(Perception.For(sim, me).Tastes(ration));
    }

    [Fact]
    public void ACordTiedSlackLooksSnugButLetsTheAirIn()
    {
        var (sim, me, table) = PrepKitchen(("Meat", 3));
        // Let go as it first reads snug: it said snug, and it isn't.
        var steps = (int)MathF.Ceiling((PackSession.SnugAt + 0.05f) / PackSession.PullPerStep);
        var ration = Pack(sim, me, table, Packing(steps));
        Assert.True(sim.World.Get<Ration>(ration).LooseWrap);
        Assert.DoesNotContain("loose", Perception.For(sim, me).Tastes(ration)!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FoldsFromTheWrongSideMakeAPoorBundle()
    {
        var (sim, me, table) = PrepKitchen(("Berries", 3), ("Meat", 3));
        var good = sim.World.Get<Ration>(Pack(sim, me, table, Packing(70))).Quality;
        var (sim2, me2, table2) = PrepKitchen(("Berries", 3), ("Meat", 3));
        var sloppy = sim2.World.Get<Ration>(Pack(sim2, me2, table2, Packing(70, wrongSide: true))).Quality;
        Assert.True(sloppy < good - 0.2f, $"{sloppy:0.00} against {good:0.00}");
    }

    [Fact]
    public void WithoutThreePortionsThereIsNothingToPack()
    {
        var (sim, me, table) = PrepKitchen(("Meat", 2));
        Assert.NotEqual(AvatarActionKind.Cook, AvatarSystem.ActionAt(sim, me, sim.World.Get<Thing>(table).Cell).Kind);
        Rations.Start(sim, me, table);
        Assert.False(sim.World.Has<WorkSession>(me));
    }

    [Fact]
    public void ARationSurvivesASave()
    {
        var (sim, me, table) = PrepKitchen(("Meat", 3));
        var ration = Pack(sim, me, table, Packing(70));
        sim.World.Get<Rot>(ration).Progress = 0.2f;
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        var (a, b) = (sim.World.Get<Ration>(ration), loaded.World.Get<Ration>(ration));
        Assert.Equal((a.Quality, a.Flavour, a.Maker, a.LooseWrap), (b.Quality, b.Flavour, b.Maker, b.LooseWrap));
        Assert.Equal(0.2f, FoodPoisoning.RotOf(loaded, ration), 0.001f);
    }

    private static Entity NewRation(Simulation sim, float quality = 0.8f, float rot = 0f, bool loose = false)
    {
        var item = sim.CreateItem(sim.Thing(Rations.Def), 1);
        sim.World.Add(item, new Ration { Quality = quality, Flavour = new Flavour(1f, 0, 0, 0), LooseWrap = loose });
        sim.World.Add(item, new Crafted { Quality = Crafting.QualityFrom(quality), Maker = Entity.Null });
        sim.World.Get<Rot>(item).Progress = rot;
        return item;
    }

    [Fact]
    public void AnAdventurerBuysRationsOnShowAtTheInnOneApiece()
    {
        var (sim, me, party) = ExpeditionTests.Round("Cook");
        var inn = ShopSystem.BuildingOf(sim, sim.Defs.Building("Inn"));
        var cell = ShopSystem.LineCells(sim, inn).Last();
        var ration = sim.PlaceItemNear(NewRation(sim), cell);
        Assert.Equal(inn, Buildings.At(sim, sim.World.Get<Thing>(ration).Cell));
        sim.ApplyNow(new SetItemPriceCommand(me, ration, 3, true));
        Assert.Equal(inn, Goods.ShownIn(sim, ration));
        Assert.All(party, e => Assert.True(Goods.IsUpgrade(sim, e, ration)));

        for (var t = 0; t < SimClock.HoursToTicks(4) && !sim.Shops.All.Any(o => o.Subject == ration); t += 300)
            sim.TickMany(300);
        var order = sim.Shops.All.Single(o => o.Subject == ration);
        var buyer = order.Poster;
        sim.World.Get<Carrying>(me).Item = Inventory.PickUp(sim, me, ration, sim.World.Get<Carrying>(me).Item);
        var by = sim.SpiralFrom(sim.World.Get<Position>(buyer).Cell, 3).First(c => c != sim.World.Get<Position>(buyer).Cell && sim.Map.Passable(c) && c.Z == sim.World.Get<Position>(buyer).Cell.Z);
        sim.World.Get<Position>(me).Cell = by;
        sim.World.Get<Position>(me).Pos = Position.CenterOf(by);
        sim.ApplyNow(new GiveCommand(me, buyer, ration, 1));
        Assert.Equal(OrderStatus.Done, order.Status);
        Assert.Contains(ration, Rations.Carried(sim, buyer));
        // One's enough for the road.
        Assert.False(Goods.IsUpgrade(sim, buyer, NewRation(sim)));
    }

    [Fact]
    public void SoundRationsMakeAStrongerPartyAndTurnedOnesAWeakerOne()
    {
        var (sim, _, party) = ExpeditionTests.Round("Cook");
        var hours = 10f;
        var none = ExpeditionSystem.Record(sim, party[0]);
        var fresh = new List<RationRecord> { new(0.9f, 0f, false, Entity.Null) };
        var turned = new List<RationRecord> { new(0.9f, 0.45f, true, Entity.Null) };
        var plain = ExpeditionSystem.Strength(none, null, null, Rations.StrengthFactor(sim, [], hours));
        var fed = ExpeditionSystem.Strength(none, null, null, Rations.StrengthFactor(sim, fresh, hours));
        var sick = ExpeditionSystem.Strength(none, null, null, Rations.StrengthFactor(sim, turned, hours));
        Assert.True(fed > plain * 1.04f, $"{fed:0.00} against {plain:0.00}");
        Assert.True(sick < plain * 0.9f, $"{sick:0.00} against {plain:0.00}");
        // Tied slack, a bundle that was sound in town turns on a long road.
        var tight = new RationRecord(0.9f, 0.25f, false, Entity.Null);
        var slack = tight with { LooseWrap = true };
        Assert.False(Rations.Turned(sim, tight, 48f));
        Assert.True(Rations.Turned(sim, slack, 48f));
    }

    [Fact]
    public void RationsGoOutOnAQuestAndComeBackAsAWord()
    {
        var (sim, me, party) = ExpeditionTests.Round("Cook");
        foreach (var e in party)
            Inventory.Stow(sim, e, NewRation(sim, rot: 0.95f));
        var record = ExpeditionSystem.Record(sim, party[0]);
        Assert.Single(record.Rations);
        var id = sim.World.Get<Adventurer>(party[0]).Party;
        ExpeditionTests.SendAndWait(sim, me, party);
        var back = sim.Creatures().Where(e => sim.World.TryGet<Adventurer>(e, out var a) && a.Party == id).ToList();
        Assert.NotEmpty(back);
        // Eaten on the way: none come back, and they were sick with it.
        Assert.All(back, e => Assert.Empty(Rations.Carried(sim, e)));
        Assert.All(back, e => Assert.True(FoodPoisoning.Severity(sim, e) > 0f, "sick from the road"));
        var tale = back.Select(e => sim.World.TryGet<ReportPending>(e, out var r) ? r.Text : null).FirstOrDefault(t => t != null)
            ?? sim.Speech.Kept.Last(u => back.Contains(u.Speaker) && u.Text.Contains("rations")).Text;
        Assert.Contains("rations had turned", tale);
    }
}

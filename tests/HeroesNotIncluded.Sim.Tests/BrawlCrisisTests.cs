using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Director;
using HeroesNotIncluded.Sim.Hospitality;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;
using Xunit.Abstractions;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>The brawl at the inn (<see cref="BrawlCrisis"/>): a rowdy crowd, its heat, and the ways it's settled.</summary>
public class BrawlCrisisTests(ITestOutputHelper output)
{
    private static Simulation Round(ulong seed = 1234)
    {
        var sim = VillageRound.NewSimulation(TestWorld.LoadDefs(), seed);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Director.Enable(sim.Defs.Difficulty("Normal"));
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Guid.Empty, "Arin", default));
        sim.ApplyNow(new StartRoundCommand());
        return sim;
    }

    /// <summary>Tick till the crowd's in and swinging.</summary>
    private static void TillBegun(Simulation sim, BrawlCrisis brawl)
    {
        for (var t = 0; t < SimClock.HoursToTicks(4) && !brawl.Begun && sim.Director.Active.Contains(brawl); t += SimClock.TicksPerSecond * 10)
            sim.TickMany(SimClock.TicksPerSecond * 10);
        Assert.True(brawl.Begun, "the crowd never got into the inn");
    }

    [Fact]
    public void LeftAloneItBoilsOver()
    {
        var sim = Round();
        var brawl = Assert.IsType<BrawlCrisis>(sim.Director.Send(sim, CrisisKind.Brawl));
        Assert.True(brawl.Crowd.Count >= 3);
        TillBegun(sim, brawl);
        output.WriteLine($"begun after {(sim.Clock.Tick - brawl.Started) / (float)SimClock.TicksPerHour:0.0}h");
        var unmetBefore = sim.Director.Unmet;
        for (var h = 0; h < 12 && sim.Director.Active.Contains(brawl); h++)
        {
            sim.TickMany(SimClock.TicksPerHour / 2);
            if (brawl.Heat < sim.Defs.Season.Brawl.BreakAt && sim.Director.Active.Contains(brawl))
                Assert.Equal(0, brawl.Smashed); // not till it's hot
            output.WriteLine($"{h / 2f:0.0}h heat {brawl.Heat:0.00}, smashed {brawl.Smashed}, fighting {brawl.Brawlers.Count(e => BrawlCrisis.Fighting(sim, e))}/{brawl.Brawlers.Count}, "
                + $"down {brawl.Brawlers.Count(e => sim.World.IsAlive(e) && sim.World.Get<Creature>(e).Downed)}, gone {brawl.Brawlers.Count(e => !sim.World.IsAlive(e))}, "
                + $"in the inn {brawl.Brawlers.Count(e => sim.World.IsAlive(e) && brawl.InInn(sim, e))}");
        }
        Assert.DoesNotContain(brawl, sim.Director.Active);
        Assert.Equal(unmetBefore + 1, sim.Director.Unmet);
        Assert.Equal(1f, brawl.Heat);
        var said = sim.Messages.Messages.Select(m => m.Text).ToList();
        output.WriteLine($"hurled {said.Count(t => t.Contains(" hurls "))}, shoved into {said.Count(t => t.Contains(" goes over "))}, smashed {said.Count(t => t.Contains(" to matchwood"))}");
        // Tables and stools smashed on the way: the carpenter's to make again.
        Assert.True(brawl.Smashed > 0, "nothing was smashed");
        var losses = Carpentry.Refurnishing.Of(sim).Losses;
        Assert.Equal(brawl.Smashed, losses.Count);
        Assert.All(losses, l => Assert.True(l.Def.BreaksInBrawls && l.Building == brawl.Inn));
        Assert.All(brawl.Crowd.Where(sim.World.IsAlive), e => Assert.True(sim.World.Has<Combat.Leaving>(e), "the crowd goes off sour"));
        Assert.All(brawl.Brawlers, e => Assert.False(sim.World.IsAlive(e) && sim.World.Get<Creature>(e).Dead, "nobody dies of fists"));
    }

    /// <summary>Someone of the town lays a brawler out: the blow is theirs, and down they go.</summary>
    private static void LayOut(Simulation sim, Entity brawler, Entity by)
    {
        Combat.RealtimeAttacks.Provoke(sim, brawler, by);
        for (var i = 0; i < 40 && !sim.World.Get<Creature>(brawler).Downed; i++)
        {
            HealthSystem.Damage(sim, brawler, 3f, DamageType.Blunt);
            sim.TickMany(5);
        }
        Assert.True(sim.World.Get<Creature>(brawler).Downed);
    }

    private static Entity Player(Simulation sim) => sim.Creatures().First(e => sim.World.Get<Creature>(e) is { Faction: Faction.Village, Def.Name: "Human" });

    [Fact]
    public void KnockingThemAllDownMeetsIt()
    {
        var sim = Round();
        var brawl = (BrawlCrisis)sim.Director.Send(sim, CrisisKind.Brawl)!;
        TillBegun(sim, brawl);
        var metBefore = sim.Director.Met;
        foreach (var e in brawl.Brawlers.ToList())
            if (BrawlCrisis.Fighting(sim, e))
                LayOut(sim, e, Player(sim));
        for (var i = 0; i < 6 && sim.Director.Active.Contains(brawl); i++)
            sim.TickMany(SimClock.TicksPerHour / 6);
        Assert.DoesNotContain(brawl, sim.Director.Active);
        Assert.Equal(metBefore + 1, sim.Director.Met);
        // Out cold: they sleep it off where they fell, and nobody's dead of it.
        Assert.All(brawl.Brawlers, e => Assert.False(sim.World.Get<Creature>(e).Dead));
        Assert.All(brawl.Brawlers, e => Assert.False(sim.World.Has<Brawler>(e)));
        Assert.All(brawl.Crowd, e => Assert.False(sim.World.Has<Combat.Leaving>(e), "a brawl put down isn't held against the town"));
    }

    [Fact]
    public void DroppedByAMateTheyGetBackUpAndAtIt()
    {
        var sim = Round();
        var brawl = (BrawlCrisis)sim.Director.Send(sim, CrisisKind.Brawl)!;
        TillBegun(sim, brawl);
        var (a, b) = (brawl.Brawlers[0], brawl.Brawlers[1]);
        LayOut(sim, a, b);
        sim.TickMany(SimClock.TicksPerHour / 6 + 1);
        Assert.True(sim.World.Has<Brawler>(a), "a mate's punch doesn't settle them");
        Assert.Contains(brawl, sim.Director.Active);
    }

    [Fact]
    public void SettlingTheRingleaderTakesTheHeartOutOfTheRest()
    {
        var sim = Round();
        var brawl = (BrawlCrisis)sim.Director.Send(sim, CrisisKind.Brawl)!;
        TillBegun(sim, brawl);
        sim.TickMany(SimClock.TicksPerHour);
        var heat = brawl.Heat;
        Assert.True(heat > 0.2f);
        LayOut(sim, brawl.Ringleader, Player(sim));
        sim.TickMany(SimClock.TicksPerHour / 6 + 1);
        Assert.False(sim.World.Has<Brawler>(brawl.Ringleader));
        Assert.True(brawl.Heat < heat - 0.2f, $"heat {heat:0.00} -> {brawl.Heat:0.00}");
        foreach (var e in brawl.Brawlers.Where(e => e != brawl.Ringleader))
            if (Adventurers.Tempers.Of(sim, e) != Adventurers.Temper.Rowdy)
                Assert.False(sim.World.Has<Brawler>(e), $"{sim.World.Get<Creature>(e).Name} loses heart");
    }

    [Fact]
    public void AGuestStruckByABrawlerJoinsInIfTheyreInTheirCups()
    {
        var sim = Round();
        var brawl = (BrawlCrisis)sim.Director.Send(sim, CrisisKind.Brawl)!;
        TillBegun(sim, brawl);
        var guests = sim.Parties.Send(sim, sim.Defs.Party("GildedSpurs"));
        var (drunk, sober) = (guests[0], guests[1]);
        var inside = sim.World.Get<Position>(brawl.Brawlers.First(e => brawl.InInn(sim, e))).Cell;
        foreach (var g in new[] { drunk, sober })
        {
            var at = sim.SpiralFrom(inside, 3).First(c => sim.Map.Passable(c) && Buildings.At(sim, c) == brawl.Inn);
            sim.World.Get<Position>(g) = Position.At(at);
        }
        sim.World.Add(drunk, new Tipsy { Level = 0.6f });
        foreach (var g in new[] { drunk, sober })
            Combat.RealtimeAttacks.Provoke(sim, g, brawl.Ringleader);
        sim.TickMany(SimClock.TicksPerHour / 6 + 1);
        Assert.True(sim.World.Has<Brawler>(drunk), "in their cups: in it");
        Assert.Contains(drunk, brawl.Brawlers);
        if (Adventurers.Tempers.Of(sim, sober) != Adventurers.Temper.Rowdy)
            Assert.False(sim.World.Has<Brawler>(sober), "sober and easy: stays out of it");
    }

    [Fact]
    public void ABrawlUnderWaySurvivesASave()
    {
        var sim = Round();
        var brawl = (BrawlCrisis)sim.Director.Send(sim, CrisisKind.Brawl)!;
        TillBegun(sim, brawl);
        sim.TickMany(SimClock.TicksPerHour / 2);
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        var again = Assert.IsType<BrawlCrisis>(Assert.Single(loaded.Director.Active));
        Assert.Equal(brawl.Heat, again.Heat);
        Assert.Equal(brawl.Ringleader, again.Ringleader);
        Assert.Equal(brawl.Brawlers.Count(e => sim.World.Has<Brawler>(e)), again.Brawlers.Count(e => loaded.World.Has<Brawler>(e)));
        loaded.TickMany(SimClock.TicksPerHour);
        Assert.True(again.Heat > brawl.Heat, "and carries on");
    }

    [Fact]
    public void WithTwoLaidOutTheRestKeepAtIt()
    {
        // Playtest: with two down the others seemed to stop. Among themselves they pull their
        // punches (HouseRules.Pulled) but they swing, every turn.
        var sim = Round();
        var me = Player(sim);
        Scenarios.BrawlDrill.Stage(sim, me);
        var brawl = BrawlCrisis.Of(sim)!;
        sim.TickMany(SimClock.TicksPerHour / 6 + 1);
        foreach (var e in brawl.Brawlers.Where(e => e != brawl.Ringleader).Take(2).ToList())
            LayOut(sim, e, me);
        var swings = 0;
        for (var t = 0; t < SimClock.TicksPerHour; t++)
        {
            sim.Tick();
            foreach (var e in brawl.Brawlers)
                if (sim.World.IsAlive(e) && sim.World.TryGet<Combat.Windup>(e, out var wu) && wu.Start == sim.Clock.Tick - 1)
                    swings++;
        }
        output.WriteLine($"{swings} swings in the hour, heat {brawl.Heat:0.00}");
        Assert.Contains(brawl, sim.Director.Active);
        Assert.True(swings >= 20, $"only {swings} swings in an hour");
        Assert.All(brawl.Brawlers, e => Assert.False(sim.World.Get<Creature>(e).Dead));
    }

    [Fact]
    public void AHurledStoolFliesBreaksOverThemAndIsTheCarpentersToMakeAgain()
    {
        var sim = Round();
        Scenarios.BrawlDrill.Stage(sim, Player(sim));
        var brawl = BrawlCrisis.Of(sim)!;
        sim.TickMany(SimClock.TicksPerHour / 6 + 1);
        var stool = sim.World.Query<Thing>().First(e => brawl.Breakable(sim, e) && sim.World.Get<Thing>(e).Def.ThrownInBrawls);
        var cell = sim.World.Get<Thing>(stool).Cell;
        var (thrower, target) = (brawl.Brawlers[0], brawl.Brawlers[1]);
        var spots = sim.SpiralFrom(cell, 3).Where(c => c != cell && c.Z == cell.Z && sim.Map.Passable(c) && Buildings.At(sim, c) == brawl.Inn).ToList();
        Scenarios.FireDrill.MoveTo(sim, thrower, spots[0]);
        Scenarios.FireDrill.MoveTo(sim, target, spots.Last(c => Combat.CombatRules.LineOfFire(sim, Position.CenterOf(spots[0]) + new float3(0, 0, 0.5f), Position.CenterOf(c) + new float3(0, 0, 0.5f))));
        Assert.True(brawl.Hurl(sim, thrower, stool, target));
        Assert.False(sim.World.IsAlive(stool), "it broke over them");
        Assert.Contains(sim.RecentShots, s => s.Sprite == "buildings/stool");
        Assert.Contains(Carpentry.Refurnishing.Of(sim).Losses, l => l.Cell == cell && l.Def.Name == "Stool");
        Assert.Equal(1, brawl.Smashed);
        // It broke over the first one in its way (the target, or someone between), who knows who threw it.
        Assert.Contains(sim.Creatures(), c => sim.World.TryGet<Provoked>(c, out var p) && p.By == thrower && p.Tick == sim.Clock.Tick);
    }

    // ---------------- Other ways to settle it ----------------

    private static readonly Guid Ana = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

    /// <summary>A round with Ana as <paramref name="trade"/>, the brawl staged in the inn round her and begun.</summary>
    private static (Simulation Sim, Entity Me, BrawlCrisis Brawl) Brawling(string trade = "Innkeeper")
    {
        var sim = VillageRound.NewSimulation(TestWorld.LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Director.Enable(sim.Defs.Difficulty("Normal"));
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default));
        sim.ApplyNow(new SetTradePreferencesCommand(Ana, [sim.Defs.Trade(trade)], []));
        sim.ApplyNow(new StartRoundCommand());
        var me = Players.CharacterOf(sim, Ana);
        Assert.Equal(trade, sim.World.Get<Trade>(me).Def.Name);
        Scenarios.BrawlDrill.Stage(sim, me);
        var brawl = BrawlCrisis.Of(sim)!;
        sim.TickMany(SimClock.TicksPerHour / 6 + 1);
        Assert.True(brawl.Begun);
        return (sim, me, brawl);
    }

    /// <summary>Stand <paramref name="who"/> right beside <paramref name="by"/>, in the inn.</summary>
    private static void Beside(Simulation sim, BrawlCrisis brawl, Entity who, Entity by)
    {
        var at = sim.World.Get<Position>(by).Cell;
        Scenarios.FireDrill.MoveTo(sim, who, sim.SpiralFrom(at, 2).First(c => c != at && c.Z == at.Z && sim.Map.Passable(c) && Buildings.At(sim, c) == brawl.Inn));
    }

    private static void Quick(Simulation sim, Entity me, string id) =>
        sim.ApplyNow(new SayCommand(me, sim.Defs.Bark("quick_" + id)!.Lines[0].Text, Speech.Volume.Shout, id));

    [Fact]
    public void ABucketOfWellWaterOverABrawlerSobersThemOutOfIt()
    {
        var (sim, _, brawl) = Brawling();
        var wet = brawl.Brawlers.First(e => BrawlCrisis.Fighting(sim, e));
        var before = DrinkingSystem.LevelOf(sim, wet);
        Environment.Buckets.Splash(sim, sim.World.Get<Position>(wet).Cell);
        Assert.True(sim.World.Has<Brawler>(wet), "the water's still in the air");
        sim.TickMany(SimClock.TicksPerSecond);
        Assert.True(DrinkingSystem.LevelOf(sim, wet) < before);
        Assert.False(sim.World.Has<Brawler>(wet));
        Assert.Contains(brawl, sim.Director.Active); // the rest are still at it
    }

    [Fact]
    public void AGhostsSpookSendsABrawlerRunning()
    {
        var (sim, _, brawl) = Brawling();
        var shaken = brawl.Brawlers.First(e => BrawlCrisis.Fighting(sim, e));
        Afterlife.Hauntings.Leave(sim, sim.World.Get<Position>(shaken).Cell, Afterlife.HauntKind.Spook);
        sim.TickMany(SimClock.TicksPerHour / 6 + 1);
        Assert.False(sim.World.Has<Brawler>(shaken));
        Assert.True(sim.World.Has<Combat.Leaving>(shaken));
    }

    [Fact]
    public void TakeItOutsideWorksOnlyIfTheRingleaderHearsIt()
    {
        var (sim, me, brawl) = Brawling();
        // Out of the ringleader's hearing: they just say no.
        var other = brawl.Brawlers.First(e => e != brawl.Ringleader && BrawlCrisis.Fighting(sim, e));
        Scenarios.FireDrill.MoveTo(sim, brawl.Ringleader, sim.World.Get<Position>(me).Cell + new int3(40, 0, 0) is var far && sim.Map.InBounds(far) && sim.Map.Passable(far)
            ? far : sim.SpiralFrom(new int3(5, 5, sim.World.Get<Position>(me).Cell.Z), 10).First(c => sim.Map.Passable(c)));
        Beside(sim, brawl, me, other);
        Quick(sim, me, "outside");
        Assert.True(sim.World.Has<Brawler>(other), "not the ringleader's say");
        // Beside the ringleader: the whole crowd goes out with them.
        Beside(sim, brawl, brawl.Ringleader, me);
        Quick(sim, me, "outside");
        Assert.All(brawl.Brawlers, e => Assert.False(sim.World.Has<Brawler>(e)));
        Assert.All(brawl.Crowd.Where(e => BrawlCrisis.Of(sim) != null), e => Assert.True(sim.World.Has<Combat.Leaving>(e) || sim.World.Get<Creature>(e).Downed));
    }

    [Fact]
    public void AQuickLinesWordsMustBeItsOwnToMeanAnything()
    {
        var (sim, me, brawl) = Brawling();
        Beside(sim, brawl, brawl.Ringleader, me);
        sim.ApplyNow(new SayCommand(me, "Everyone calm down please.", Speech.Volume.Shout, "outside"));
        Assert.True(sim.World.Has<Brawler>(brawl.Ringleader));
    }

    [Fact]
    public void TheWatchScaresOffTheSoberingOnceAndOnlyOnce()
    {
        var (sim, me, brawl) = Brawling();
        var near = brawl.Brawlers.Where(e => BrawlCrisis.Fighting(sim, e)).Take(2).ToList();
        Beside(sim, brawl, me, near[0]);
        Quick(sim, me, "watch"); // loud with drink: they don't care
        Assert.All(near, e => Assert.True(sim.World.Has<Brawler>(e)));
        var (loaded, _) = Saving.SaveGame.Read(Saving.SaveGame.Write(sim), sim.Defs);
        Quick(loaded, me, "watch"); // and it's been tried
        Assert.All(near, e => Assert.True(loaded.World.Has<Brawler>(e)));

        // A fresh brawl, the crowd sobered a little first: the watch sends them off.
        (sim, me, brawl) = Brawling();
        foreach (var e in brawl.Brawlers)
            sim.World.Get<Tipsy>(e).Level = 0.4f;
        Beside(sim, brawl, me, brawl.Brawlers[0]);
        Quick(sim, me, "watch");
        Assert.Contains(brawl.Brawlers, e => !sim.World.Has<Brawler>(e) && sim.World.Has<Combat.Leaving>(e));
    }

    [Fact]
    public void DrinksOnTheHouseFromTheKeeperBuysThemOffAtSoMuchAHead()
    {
        var (sim, me, brawl) = Brawling("Innkeeper");
        Inventory.Stow(sim, me, sim.CreateItem(sim.Defs.Thing(Shops.Coin.Silver), 40));
        var silver = Shops.Coin.Count(sim, me);
        Beside(sim, brawl, me, brawl.Brawlers[0]);
        Quick(sim, me, "drinks");
        var bought = brawl.Brawlers.Count(e => !sim.World.Has<Brawler>(e));
        Assert.True(bought > 0);
        Assert.Equal(silver - bought * sim.Defs.Season.Brawl.DrinksCost, Shops.Coin.Count(sim, me));

        // From anyone else, it's jeered.
        var (sim2, me2, brawl2) = Brawling("Blacksmith");
        Inventory.Stow(sim2, me2, sim2.CreateItem(sim2.Defs.Thing(Shops.Coin.Silver), 40));
        Beside(sim2, brawl2, me2, brawl2.Brawlers[0]);
        Quick(sim2, me2, "drinks");
        Assert.All(brawl2.Brawlers, e => Assert.True(sim2.World.Has<Brawler>(e)));
    }

    [Fact]
    public void ABotKeeperStandsTheDrinksThenSettlesTheRestWithFists()
    {
        var sim = VillageRound.NewSimulation(TestWorld.LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Director.Enable(sim.Defs.Difficulty("Normal"));
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default));
        sim.ApplyNow(new SetTradePreferencesCommand(Ana, [sim.Defs.Trade("Innkeeper")], []));
        var bot = new Bots.BotPlayer(() => sim, Ana, c => sim.ApplyNow(c), 7) { Temper = Bots.BotTemperament.Steady };
        sim.ApplyNow(new StartRoundCommand());
        var me = Players.CharacterOf(sim, Ana);
        Inventory.Stow(sim, me, sim.CreateItem(sim.Defs.Thing(Shops.Coin.Silver), 30));
        Scenarios.BrawlDrill.Stage(sim, me);
        var brawl = BrawlCrisis.Of(sim)!;
        var silver = Shops.Coin.Count(sim, me);
        var (met, unmet) = (sim.Director.Met, sim.Director.Unmet);
        for (var t = 0; t < SimClock.HoursToTicks(5) && sim.Director.Active.Contains(brawl); t++)
        {
            sim.Tick();
            bot.Tick();
        }
        output.WriteLine($"over: {!sim.Director.Active.Contains(brawl)}, met {sim.Director.Met - met}, unmet {sim.Director.Unmet - unmet}, heat {brawl.Heat:0.00}, "
            + $"silver {silver} -> {Shops.Coin.Count(sim, me)}, smashed {brawl.Smashed}");
        Assert.True(Shops.Coin.Count(sim, me) < silver, "the keeper stood a round");
        Assert.DoesNotContain(brawl, sim.Director.Active);
        Assert.Equal(met + 1, sim.Director.Met);
    }

    // ---------------- A player's fists ----------------

    /// <summary>A round with Ana, and <paramref name="guests"/> adventurers stood in the inn beside her, none rowdy nor in their cups.</summary>
    private static (Simulation Sim, Entity Me, Entity Inn, List<Entity> Guests) InTheInn(int guests = 1)
    {
        var sim = VillageRound.NewSimulation(TestWorld.LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Director.Enable(sim.Defs.Difficulty("Normal"));
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default));
        sim.ApplyNow(new StartRoundCommand());
        var me = Players.CharacterOf(sim, Ana);
        var inn = Shops.ShopSystem.BuildingFor(sim, TownRole.Tavern);
        var cells = sim.SpiralFrom(sim.World.Get<PresetBuilding>(inn).Anchor, 12)
            .Where(c => sim.Map.InBounds(c) && sim.Map.Passable(c) && Buildings.At(sim, c) == inn).ToList();
        var here = cells[cells.Count / 2];
        Scenarios.FireDrill.MoveTo(sim, me, here);
        var calm = new List<Entity>();
        foreach (var party in sim.Defs.AllParties.Where(p => !p.Crowd))
        {
            calm.AddRange(sim.Parties.Send(sim, party).Where(e => Adventurers.Tempers.Of(sim, e) != Adventurers.Temper.Rowdy));
            if (calm.Count >= guests)
                break;
        }
        Assert.True(calm.Count >= guests, "not enough even-tempered guests");
        var spots = cells.Where(c => c != here && c.Z == here.Z).OrderBy(c => Maps.Map.Distance(c, here)).ToList();
        for (var i = 0; i < guests; i++)
        {
            sim.World.Remove<Tipsy>(calm[i]);
            Scenarios.FireDrill.MoveTo(sim, calm[i], spots[i]);
        }
        return (sim, me, inn, calm.Take(guests).ToList());
    }

    private static AttackDef Fists(Simulation sim, Entity e) => sim.World.Get<Creature>(e).Def.Attacks.First(a => !a.Ranged);

    [Fact]
    public void APlayersRealPunchInTheInnIsHeldAgainstThem()
    {
        var (sim, me, _, guests) = InTheInn();
        var guest = guests[0];
        // A real blow, through Blows: tried till one lands (the guest may duck the first).
        for (var i = 0; i < 10 && !sim.World.Has<Grudge>(guest); i++)
        {
            Scenarios.FireDrill.MoveTo(sim, guest, sim.World.Get<Position>(me).Cell + new int3(1, 0, 0));
            var them = sim.World.Get<Position>(guest).Pos - sim.World.Get<Position>(me).Pos;
            Combat.Blows.Begin(sim, me, guest, Fists(sim, me), aim: DetMath.Atan2(them.Y, them.X));
            sim.TickMany(SimClock.TicksPerSecond);
        }
        var g = sim.World.Get<Grudge>(guest);
        Assert.Equal(me, g.By);
        Assert.Equal(1f, g.Count);
        Assert.Empty(sim.Director.Active.OfType<BrawlCrisis>());
        Assert.Contains(sim.Speech.Kept, u => u.Speaker == guest);
    }

    [Fact]
    public void APunchOrTwoIsForgivenAndForgotten()
    {
        var (sim, me, _, guests) = InTheInn();
        var guest = guests[0];
        Patience.Struck(sim, me, guest, heavy: false);
        Patience.Struck(sim, me, guest, heavy: false);
        Assert.Empty(sim.Director.Active.OfType<BrawlCrisis>());
        Assert.False(sim.World.Has<Brawler>(guest));
        // Given time, it's forgotten: two more and they've still not had enough.
        sim.TickMany((int)(sim.Defs.Season.Brawl.GrudgeFadeSeconds * 2 * SimClock.TicksPerSecond));
        Patience.Struck(sim, me, guest, heavy: false);
        Patience.Struck(sim, me, guest, heavy: false);
        Assert.Empty(sim.Director.Active.OfType<BrawlCrisis>());
        // Outside the inn nobody's keeping count.
        Scenarios.FireDrill.MoveTo(sim, guest, new int3(5, 5, sim.World.Get<Position>(guest).Cell.Z) is var o && sim.Map.Passable(o) && Buildings.At(sim, o).IsNull
            ? o : sim.SpiralFrom(new int3(5, 5, 0), 20).First(c => sim.Map.InBounds(c) && sim.Map.Passable(c) && Buildings.At(sim, c).IsNull));
        sim.World.Remove<Grudge>(guest);
        Patience.Struck(sim, me, guest, heavy: true);
        Assert.False(sim.World.Has<Grudge>(guest));
    }

    [Fact]
    public void PunchedPastTheirPatienceTheyHitBackAndItsABrawl()
    {
        var (sim, me, inn, guests) = InTheInn();
        var guest = guests[0];
        Patience.Struck(sim, me, guest, heavy: true); // a haymaker's two
        Assert.Empty(sim.Director.Active.OfType<BrawlCrisis>());
        Patience.Struck(sim, me, guest, heavy: false);
        var brawl = Assert.Single(sim.Director.Active.OfType<BrawlCrisis>());
        Assert.Equal(me, brawl.Instigator);
        Assert.Equal(guest, brawl.Ringleader);
        Assert.Equal(inn, brawl.Inn);
        Assert.True(brawl.Begun);
        Assert.True(sim.World.Has<Brawler>(guest));
        Assert.Equal(me, brawl.FoeFor(sim, guest));
        var swings = 0;
        for (var t = 0; t < SimClock.TicksPerSecond * 10; t++)
        {
            sim.Tick();
            if (sim.World.TryGet<Combat.Windup>(guest, out var wu) && wu.Start == sim.Clock.Tick - 1)
                swings++;
        }
        Assert.True(swings > 0, "they swing back");
        Assert.Contains(sim.RecentBlows, b => b.Attacker == guest && b.Target == me);
    }

    [Fact]
    public void OnceItsStartedAnyoneElseTheyHitJoinsAndTheRowdyPileIn()
    {
        var (sim, me, _, guests) = InTheInn(5);
        for (var i = 0; i < 3; i++)
            Patience.Struck(sim, me, guests[0], heavy: false);
        var brawl = Assert.Single(sim.Director.Active.OfType<BrawlCrisis>());
        Patience.Struck(sim, me, guests[1], heavy: false); // no benefit of the doubt now
        Assert.True(sim.World.Has<Brawler>(guests[1]));
        // Two at it: the drunk and the rowdy among the onlookers join in unasked.
        foreach (var e in guests.Skip(2))
            sim.World.Add(e, new Tipsy { Level = 0.64f });
        for (var t = 0; t < SimClock.HoursToTicks(2) && brawl.Brawlers.Count < 3; t += 60)
            sim.TickMany(60);
        Assert.True(brawl.Brawlers.Count >= 3, "nobody piled in");
        Assert.Contains(brawl.Brawlers, e => e != guests[0] && e != guests[1]);
    }

    [Fact]
    public void LayingOutAPlayersBrawlEarnsTheTownNothing()
    {
        var (sim, me, _, guests) = InTheInn();
        for (var i = 0; i < 3; i++)
            Patience.Struck(sim, me, guests[0], heavy: false);
        var brawl = Assert.Single(sim.Director.Active.OfType<BrawlCrisis>());
        var (met, unmet, renown, dread) = (sim.Director.Met, sim.Director.Unmet, sim.Reckoning.Renown, sim.Reckoning.Dread);
        LayOut(sim, guests[0], me);
        for (var i = 0; i < 6 && sim.Director.Active.Contains(brawl); i++)
            sim.TickMany(SimClock.TicksPerHour / 6);
        Assert.DoesNotContain(brawl, sim.Director.Active);
        Assert.Equal((met, unmet), (sim.Director.Met, sim.Director.Unmet));
        Assert.Equal(renown, sim.Reckoning.Renown);
        Assert.Equal(dread, sim.Reckoning.Dread);
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("no thanks to whoever started it"));
    }

    [Fact]
    public void APlayersBrawlBoilingOverIsDreadForTheTown()
    {
        var (sim, me, _, guests) = InTheInn(2);
        for (var i = 0; i < 3; i++)
            Patience.Struck(sim, me, guests[0], heavy: false);
        Patience.Struck(sim, me, guests[1], heavy: false);
        var brawl = Assert.Single(sim.Director.Active.OfType<BrawlCrisis>());
        typeof(BrawlCrisis).GetProperty(nameof(BrawlCrisis.Heat))!.SetValue(brawl, 0.999f);
        var (unmet, dread) = (sim.Director.Unmet, sim.Reckoning.Dread);
        for (var i = 0; i < 6 && sim.Director.Active.Contains(brawl); i++)
            sim.TickMany(SimClock.TicksPerHour / 6);
        Assert.DoesNotContain(brawl, sim.Director.Active);
        Assert.Equal(unmet + 1, sim.Director.Unmet);
        Assert.True(sim.Reckoning.Dread > dread);
        Assert.All(guests, e => Assert.True(sim.World.Has<Combat.Leaving>(e), "they go off sour"));
    }

    [Fact]
    public void WalkAwayFromItAndTheyCoolOff()
    {
        var (sim, me, _, guests) = InTheInn();
        for (var i = 0; i < 3; i++)
            Patience.Struck(sim, me, guests[0], heavy: false);
        var brawl = Assert.Single(sim.Director.Active.OfType<BrawlCrisis>());
        var met = sim.Director.Met;
        var away = sim.SpiralFrom(sim.World.Get<Position>(me).Cell + new int3(30, 0, 0), 20)
            .First(c => sim.Map.InBounds(c) && sim.Map.Passable(c) && Buildings.At(sim, c).IsNull);
        Scenarios.FireDrill.MoveTo(sim, me, away);
        for (var i = 0; i < 12 && sim.Director.Active.Contains(brawl); i++)
            sim.TickMany(SimClock.TicksPerHour / 6);
        Assert.DoesNotContain(brawl, sim.Director.Active);
        Assert.False(sim.World.Has<Brawler>(guests[0]));
        Assert.False(sim.World.Has<Combat.Leaving>(guests[0]), "settled, not driven off");
        Assert.Equal(met, sim.Director.Met);
    }

    [Fact]
    public void NobodyKeepsCountWhileARaidsOn()
    {
        var (sim, me, _, guests) = InTheInn();
        Assert.NotNull(sim.Director.SendRaid(sim, 0));
        for (var i = 0; i < 5; i++)
            Patience.Struck(sim, me, guests[0], heavy: true);
        Assert.False(sim.World.Has<Grudge>(guests[0]));
        Assert.Empty(sim.Director.Active.OfType<BrawlCrisis>());
    }

    [Fact]
    public void ABucketOverASoberBrawlerCoolsThemAndTheySayItOnce()
    {
        var (sim, me, _, guests) = InTheInn();
        var guest = guests[0];
        for (var i = 0; i < 3; i++)
            Patience.Struck(sim, me, guest, heavy: false);
        var brawl = Assert.Single(sim.Director.Active.OfType<BrawlCrisis>());
        Assert.False(sim.World.Has<Tipsy>(guest));
        var said = sim.Speech.Kept.Count(u => u.Speaker == guest);
        Environment.Buckets.Splash(sim, sim.World.Get<Position>(guest).Cell, me);
        sim.TickMany(SimClock.TicksPerSecond * 2);
        Assert.False(sim.World.Has<Brawler>(guest), "sober or not, they're out of it");
        Assert.Equal(said + 1, sim.Speech.Kept.Count(u => u.Speaker == guest)); // their own line, not the splash's as well
        for (var i = 0; i < 3 && sim.Director.Active.Contains(brawl); i++)
            sim.TickMany(SimClock.TicksPerHour / 6);
        Assert.DoesNotContain(brawl, sim.Director.Active);
    }

    [Fact]
    public void ABucketOverABarFightStopsIt()
    {
        var (sim, _, _, guests) = InTheInn(2);
        var (a, b) = (guests[0], guests[1]);
        Brawls.Start(sim, a, b);
        sim.TickMany(SimClock.TicksPerSecond);
        Assert.True(Brawls.InOne(sim, a) && Brawls.InOne(sim, b));
        Environment.Buckets.Splash(sim, sim.World.Get<Position>(a).Cell);
        sim.TickMany(SimClock.TicksPerSecond * 4);
        Assert.False(Brawls.InOne(sim, a), "the wet one's done");
        Assert.False(Brawls.InOne(sim, b), "and nobody fights a man dripping");
    }

    [Fact]
    public void APlayersBrawlSurvivesASave()
    {
        var (sim, me, _, guests) = InTheInn();
        for (var i = 0; i < 3; i++)
            Patience.Struck(sim, me, guests[0], heavy: false);
        Patience.Struck(sim, me, guests[0], heavy: false);
        sim.TickMany(SimClock.TicksPerSecond * 3);
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        var again = Assert.IsType<BrawlCrisis>(Assert.Single(loaded.Director.Active));
        Assert.Equal(me, again.Instigator);
        Assert.True(loaded.World.Has<Brawler>(guests[0]));
        Assert.Equal(sim.World.Get<Brawler>(guests[0]).Idle, loaded.World.Get<Brawler>(guests[0]).Idle);
    }
}

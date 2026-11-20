using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Director;
using HeroesNotIncluded.Sim.Hospitality;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Scenarios;
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
}

using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Director;
using HeroesNotIncluded.Sim.Healing;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>The Director's other crises: a fire, a shortage at the inn, a fever brought to town.</summary>
public class CrisisTests
{
    private static Simulation Round(bool party = false)
    {
        var sim = VillageRound.NewSimulation(TestWorld.LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Director.Enable(sim.Defs.Difficulty("Normal"));
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Guid.Empty, "Arin", default, []));
        sim.ApplyNow(new StartRoundCommand());
        if (party)
        {
            sim.Parties.Send(sim, sim.Defs.Party("GildedSpurs"));
            sim.TickMany(SimClock.TicksPerHour / 2);
        }
        return sim;
    }

    private static void TillOver(Simulation sim, ICrisis crisis, float hours)
    {
        for (var t = 0; t < SimClock.HoursToTicks(hours) && sim.Director.Active.Contains(crisis); t += SimClock.TicksPerHour / 6)
            sim.TickMany(SimClock.TicksPerHour / 6);
    }

    [Fact]
    public void AFireIsRaisedAndFoughtTillItsOut()
    {
        var sim = Round();
        var fire = Assert.IsType<FireCrisis>(sim.Director.Send(sim, CrisisKind.Fire));
        Assert.True(sim.Fire.BurningTiles.Any(), "something's alight");
        Assert.Contains(sim.Speech.Kept, u => u.Text.StartsWith("Fire!"));
        TillOver(sim, fire, sim.Defs.Season.CrisisHours + 1);
        Assert.DoesNotContain(fire, sim.Director.Active);
        Assert.Equal(1, sim.Director.Met + sim.Director.Unmet);
    }

    [Fact]
    public void RatsInTheLarderAndNoCarterTillTheShortagePasses()
    {
        var sim = Round();
        var inn = ShopSystem.BuildingOf(sim, sim.Defs.Building("Inn"));
        var larder = Buildings.Parts(sim, inn).Single(e => sim.World.Get<Thing>(e).Def.Name == "Larder");
        int Food() => sim.StoredIn(larder).Where(e => sim.World.Get<Thing>(e).Def.Nutrition > 0).Sum(e => sim.World.Get<Stack>(e).Count);
        var before = Food();
        var shortage = Assert.IsType<ShortageCrisis>(sim.Director.Send(sim, CrisisKind.Shortage));
        Assert.True(Food() <= before / 2 + 1, $"{Food()} of {before} left");

        sim.Clock.Tick = sim.Round.TickAt(sim, 2, InnSupplySystem.DeliveryHour) - 1;
        sim.TickMany(SimClock.TicksPerHour / 2);
        Assert.Contains(sim.Messages.Messages, m => m.Text.StartsWith("The carter didn't come"));
        Assert.True(sim.Director.Active.Contains(shortage) || sim.Director.Met + sim.Director.Unmet == 1);
    }

    [Fact]
    public void TheShortageIsMetIfTheInnKeepsServing()
    {
        var sim = Round();
        var shortage = sim.Director.Send(sim, CrisisKind.Shortage)!;
        Assert.Null(shortage.Check(sim));
        // Six served: the counts the crisis watches.
        var me = sim.World.Query<Avatar>().Single();
        for (var i = 0; i < ShortageCrisis.Served; i++)
            ServeOne(sim, me);
        Assert.True(shortage.Check(sim));
    }

    /// <summary>An ale ordered and done, as far as the books go.</summary>
    private static void ServeOne(Simulation sim, Entity by)
    {
        var w = sim.World;
        var drinker = sim.Creatures().First(e => w.Get<Creature>(e) is { Faction: Faction.Village, Dead: false } && e != by);
        var order = sim.Shops.Post(sim, drinker, sim.Defs.Service(ServiceKind.Drink), "An ale.", 0, sim.Clock.Tick + 1000, Entity.Null, Entity.Null, 0);
        sim.Shops.Complete(sim, order, by);
    }

    [Fact]
    public void AFeverComesWithAPartyAndSpreadsToThoseNearby()
    {
        var sim = Round(party: true);
        var plague = Assert.IsType<PlagueCrisis>(sim.Director.Send(sim, CrisisKind.Plague));
        var w = sim.World;
        var sick = sim.Creatures().Single(e => FeverSystem.Has(sim, e));
        Assert.True(w.Has<Adventurer>(sick));
        Assert.Equal("fever", DamageTypes.Wound(DamageType.Fever));

        // Someone stays close by them for a few hours.
        var near = sim.Creatures().First(e => w.Get<Creature>(e) is { Faction: Faction.Village, Dead: false } && !w.Has<Avatar>(e));
        w.Get<Mind>(near).Drafted = true;
        w.Get<Mind>(sick).Drafted = true;
        // Stopped where they stand: a walk under way would carry them back off.
        AiSystem.EndJob(sim, near, w.Get<Mind>(near), false);
        var at = w.Get<Position>(sick).Cell;
        var beside = sim.SpiralFrom(at, 2).First(c => c != at && sim.Map.Passable(c) && c.Z == at.Z);
        w.Get<Position>(near).Cell = beside;
        w.Get<Position>(near).Pos = Position.CenterOf(beside);
        for (var i = 0; i < 96 && !FeverSystem.Has(sim, near); i++) // 5% a quarter hour: a day all but makes it sure
            sim.TickMany(SimClock.TicksPerHour / 4);
        Assert.True(FeverSystem.Has(sim, near), "a fever passes to whoever sits close");

        // Seen off: nobody fevered any more, and nobody carried it away.
        foreach (var e in sim.Creatures())
            w.Get<Health>(e).Injuries.RemoveAll(i => i.Type == DamageType.Fever);
        Assert.True(plague.Check(sim));
    }

    [Fact]
    public void AFeverNobodySeesToGrowsAndOneSeenToDoesNot()
    {
        var sim = TestWorld.Create();
        var w = sim.World;
        var left = sim.SpawnCreature(sim.Defs.Creature("Human"), "Left", TestWorld.C(5, 5), ageDays: 9000);
        var tended = sim.SpawnCreature(sim.Defs.Creature("Human"), "Tended", TestWorld.C(25, 25), ageDays: 9000);
        foreach (var e in new[] { left, tended })
        {
            Array.Fill(w.Get<Needs>(e).Values, 1f);
            w.Get<Mind>(e).Drafted = true;
            FeverSystem.Catch(sim, e);
        }
        w.Get<Health>(tended).Injuries.Single(i => i.Type == DamageType.Fever).Tended = true;
        sim.TickMany(SimClock.TicksPerHour * 4);
        float Fever(Entity e) => w.Get<Health>(e).Injuries.Single(i => i.Type == DamageType.Fever).Damage;
        Assert.True(Fever(left) > FeverSystem.Caught + 2f, $"untended: {Fever(left):0.0}");
        Assert.True(Fever(tended) <= FeverSystem.Caught, $"tended: {Fever(tended):0.0}");
        Assert.True(Fever(left) <= FeverSystem.Worst);
    }

    [Fact]
    public void TheEmpathKnowsAMendingForAFever()
    {
        var defs = TestWorld.LoadDefs();
        var quell = defs.AllMendings.Single(m => m.Heals.Contains(DamageType.Fever));
        Assert.True(quell.KnownAtStart);
        Assert.Contains(DamageType.Fever, quell.Feels.Keys);
        Assert.Contains(defs.AllDrawFeels, d => d.Name == "Fever");
    }

    [Fact]
    public void ADragonComesDownBreathesFireAndIsGoneWithinItsHours()
    {
        var sim = Round(party: true);
        var crisis = Assert.IsType<DragonCrisis>(sim.Director.Send(sim, CrisisKind.Dragon));
        var w = sim.World;
        Assert.Equal(Race.Dragon, w.Get<Creature>(crisis.Dragon).Def.Race);
        Assert.Contains(sim.Speech.Kept, u => u.Text.StartsWith("Dragon!"));
        var breathed = false;
        var hurt = 0;
        for (var t = 0; t < SimClock.HoursToTicks(DragonCrisis.Hours + 1) && sim.Director.Active.Contains(crisis); t++)
        {
            sim.Tick();
            breathed |= sim.RecentBreaths.Any(b => b.Source == crisis.Dragon);
            if (w.IsAlive(crisis.Dragon))
                hurt = w.Get<Health>(crisis.Dragon).Injuries.Count;
        }
        Assert.True(breathed, "it breathed on the town");
        Assert.DoesNotContain(crisis, sim.Director.Active);
        var dead = sim.Creatures().Count(e => w.Get<Creature>(e) is { Faction: Faction.Village, Dead: true });
        Console.WriteLine($"Dragon: met {sim.Director.Met}, unmet {sim.Director.Unmet}, village dead {dead}, dragon wounds {hurt}, renown {sim.Reckoning.Renown:0}, dread {sim.Reckoning.Dread:0}");
    }

    [Fact]
    public void ADragonHurtEnoughIsDrivenOff()
    {
        var sim = Round();
        var crisis = (DragonCrisis)sim.Director.Send(sim, CrisisKind.Dragon)!;
        Assert.Null(crisis.Check(sim));
        for (var i = 0; i < 12 && !Ai.DragonBrain.DrivenOff(sim, crisis.Dragon); i++)
            HealthSystem.Damage(sim, crisis.Dragon, 12f, DamageType.Cut);
        Assert.True(crisis.Check(sim));
        Assert.IsType<Ai.LeaveMapJob>(new Ai.DragonBrain().Think(sim, crisis.Dragon));
        var renown = sim.Reckoning.Renown;
        sim.TickMany(SimClock.TicksPerHour / 6 + 1);
        Assert.Equal(renown + sim.Defs.Season.DragonMetRenown, sim.Reckoning.Renown);
    }

    [Fact]
    public void CrisesUnderWaySurviveASave()
    {
        var sim = Round(party: true);
        sim.Director.Send(sim, CrisisKind.Fire);
        sim.Director.Send(sim, CrisisKind.Shortage);
        sim.Director.Send(sim, CrisisKind.Plague);
        sim.Director.Send(sim, CrisisKind.Dragon);
        sim.TickMany(SimClock.TicksPerHour / 6);
        var kinds = sim.Director.Active.Select(c => c.Kind).ToList();
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        Assert.Equal(kinds, loaded.Director.Active.Select(c => c.Kind).ToList());
        Assert.Equal(sim.Shops.Served(ServiceKind.Drink), loaded.Shops.Served(ServiceKind.Drink));
    }
}

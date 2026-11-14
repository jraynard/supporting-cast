using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Healing;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>The practice yard: a round with no season, and customers when sent for, ready for the trade.</summary>
public class PracticeTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    private static (Simulation Sim, Entity Me) Yard(string trade, bool practice = true)
    {
        var sim = VillageRound.NewSimulation(LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default, []));
        sim.ApplyNow(new SetTradePreferencesCommand(Ana, [sim.Defs.Trade(trade)], []));
        sim.ApplyNow(new StartRoundCommand());
        sim.Round.Practice = practice;
        return (sim, Players.CharacterOf(sim, Ana));
    }

    private static List<Entity> Adventurers(Simulation sim) => sim.Creatures().Where(e => sim.World.Has<Adventurer>(e)).ToList();

    [Fact]
    public void TheYardHasNoSeasonAndNobodyComesUnasked()
    {
        var (sim, _) = Yard("Innkeeper");
        sim.TickMany(SimClock.TicksPerDay * (sim.Defs.Season.Days + 1));
        Assert.Equal(RoundPhase.Playing, sim.Round.Phase); // no festival, no end
        Assert.Empty(Adventurers(sim));
        Assert.Equal(0f, sim.Reckoning.Dread);

        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        Assert.True(loaded.Round.Practice);
    }

    [Fact]
    public void CustomersComeForAHealerHurt()
    {
        var (sim, me) = Yard("Healer");
        sim.ApplyNow(new PracticeCommand(me, PracticeAction.SendCustomers));
        var party = Adventurers(sim);
        Assert.NotEmpty(party);
        Assert.All(party, e => Assert.True(LayOnHands.Hurt(sim, e)));
    }

    [Fact]
    public void CustomersComeForASmithWithWornBlades()
    {
        var (sim, me) = Yard("Blacksmith");
        sim.ApplyNow(new PracticeCommand(me, PracticeAction.SendCustomers));
        var armed = Adventurers(sim).Where(e => sim.World.TryGet<Equipment>(e, out var eq) && sim.World.IsAlive(eq.Weapon)).ToList();
        Assert.NotEmpty(armed);
        Assert.All(armed, e => Assert.True(Combat.WeaponWear.HpFraction(sim, sim.World.Get<Equipment>(e).Weapon) < 0.5f));
    }

    [Fact]
    public void CustomersComeForATinkerWithAStrongboxAndACurio()
    {
        var (sim, me) = Yard("Tinker");
        sim.ApplyNow(new PracticeCommand(me, PracticeAction.SendCustomers));
        var party = Adventurers(sim);
        Assert.Contains(party, e => !Tinkering.Lockwork.LockedBoxOf(sim, e).IsNull);
        Assert.Contains(party, e => !Tinkering.Curios.UnvaluedOf(sim, e).IsNull);
    }

    [Fact]
    public void APatientIsBroughtToAShrinePalletWithTheWoundAskedFor()
    {
        var (sim, me) = Yard("Healer");
        foreach (var kind in new[] { DamageType.Burn, DamageType.Fever })
        {
            var before = sim.Creatures().ToHashSet();
            sim.ApplyNow(new PracticeCommand(me, PracticeAction.Patient, (int)kind));
            var patient = sim.Creatures().Single(e => !before.Contains(e));
            Assert.Contains(sim.World.Get<Health>(patient).Injuries, i => i.Type == kind);
            var pallets = ShopSystem.BedsOf(sim, Practice.Workplace(sim, me)).Select(b => sim.World.Get<Thing>(b).Cell);
            Assert.Contains(sim.World.Get<Position>(patient).Cell, pallets);
        }
    }

    [Fact]
    public void RestockAndFreshenUp()
    {
        var (sim, me) = Yard("Healer");
        int Bandages() => sim.World.Query<Thing>().Where(e => sim.World.Get<Thing>(e).Def.Name == Bandaging.BandageDef)
            .Sum(e => sim.World.Get<Stack>(e).Count);
        var had = Bandages();
        sim.ApplyNow(new PracticeCommand(me, PracticeAction.Restock));
        Assert.Equal(had + VillageRound.ShrineBandages, Bandages());

        sim.World.Get<Mana>(me).Current = 0f;
        HealthSystem.Damage(sim, me, 10f, DamageType.Cut, sim.World.Get<Health>(me).Body.Parts.FindIndex(p => !p.Vital));
        sim.ApplyNow(new PracticeCommand(me, PracticeAction.Freshen));
        Assert.Empty(sim.World.Get<Health>(me).Injuries);
        Assert.Equal(sim.World.Get<Mana>(me).Max, sim.World.Get<Mana>(me).Current);
    }

    [Fact]
    public void NothingIsSetUpOutsideTheYard()
    {
        var (sim, me) = Yard("Healer", practice: false);
        var count = sim.Creatures().Count();
        sim.ApplyNow(new PracticeCommand(me, PracticeAction.SendCustomers));
        sim.ApplyNow(new PracticeCommand(me, PracticeAction.Patient, (int)DamageType.Cut));
        Assert.Equal(count, sim.Creatures().Count());
    }
}

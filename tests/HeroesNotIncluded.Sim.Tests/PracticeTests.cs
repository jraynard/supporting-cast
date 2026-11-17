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
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default));
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
        var armed = Weapons(sim);
        Assert.Contains(armed, w => Practice.SmithyMends(sim, me, w));
        foreach (var weapon in armed)
            if (Practice.SmithyMends(sim, me, weapon))
                Assert.True(Combat.WeaponWear.HpFraction(sim, weapon) < 0.5f);
            else
                Assert.False(Combat.WeaponWear.NeedsMending(sim, weapon), $"{sim.LabelOf(weapon)}: nothing the smithy can mend, so nothing to ask for");
    }

    [Fact]
    public void DullCustomersWantOnlyTheGrindstoneAndAskForIt()
    {
        var (sim, me) = Yard("Blacksmith");
        sim.ApplyNow(new PracticeCommand(me, PracticeAction.SendCustomers, (int)CustomerKind.Dull));
        var dull = Weapons(sim).Where(w => Practice.SmithyMends(sim, me, w)).ToList();
        Assert.NotEmpty(dull);
        Assert.All(dull, w => Assert.True(Combat.WeaponWear.HpFraction(sim, w) >= Combat.WeaponWear.MendedHp && Combat.WeaponWear.Dullness(sim, w) > Combat.WeaponWear.WantsMendDullness));
        for (var t = 0; t < 12 * 12 && !sim.Shops.All.Any(o => o.Kind.Kind == ServiceKind.Repair); t++)
            sim.TickMany(SimClock.TicksPerHour / 12);
        Assert.Contains(sim.Shops.All, o => o.Kind.Kind == ServiceKind.Repair && Practice.SmithyMends(sim, me, sim.World.Get<Equipment>(o.Poster).Weapon));
    }

    [Fact]
    public void ALessonSetsUpWhatItNeeds()
    {
        var (sim, me) = Yard("Blacksmith");
        var lessons = sim.Defs.Trade("Blacksmith").Lessons;
        int Bars() => sim.StoredIn(sim.PackOf(me)).Where(i => sim.World.Get<Thing>(i).Def.Name == "IronBar").Sum(i => sim.World.Get<Stack>(i).Count);
        var bars = Bars();
        sim.ApplyNow(new PracticeCommand(me, PracticeAction.Lesson, lessons.FindIndex(l => l.Id == "forge")));
        Assert.Equal(bars + 3, Bars());
        Assert.Empty(Adventurers(sim));

        sim.ApplyNow(new PracticeCommand(me, PracticeAction.Lesson, lessons.FindIndex(l => l.Id == "mend")));
        Assert.NotEmpty(Adventurers(sim));

        // A lesson the trade hasn't got sets up nothing.
        var count = sim.Creatures().Count();
        sim.ApplyNow(new PracticeCommand(me, PracticeAction.Lesson, lessons.Count));
        sim.ApplyNow(new PracticeCommand(me, PracticeAction.Lesson, -1));
        sim.ApplyNow(new PracticeCommand(me, PracticeAction.SendCustomers, 99));
        Assert.Equal(count, sim.Creatures().Count());
    }

    private static List<Entity> Weapons(Simulation sim) => Adventurers(sim)
        .Where(e => sim.World.TryGet<Equipment>(e, out var eq) && sim.World.IsAlive(eq.Weapon))
        .Select(e => sim.World.Get<Equipment>(e).Weapon).ToList();

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

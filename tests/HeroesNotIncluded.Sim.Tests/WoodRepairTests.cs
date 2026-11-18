using HeroesNotIncluded.Sim.Carpentry;
using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Tinkering;
using HeroesNotIncluded.Sim.Work;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// Whoever makes a piece mends it: a worn bow, club or staff is asked of the carpenter at the
/// woodshop, a crossbow of the tinker, and only forged gear of the smith (<see cref="WeaponWear.MendKind"/>).
/// </summary>
public class WoodRepairTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    /// <summary>Millbrook in a round, Ana playing <paramref name="playerTrade"/>, the Gilded Spurs in town with the bowman's bow worn.</summary>
    private static (Simulation Sim, Entity Bowman, Entity Bow) TownWithAWornBow(string playerTrade)
    {
        var sim = VillageRound.NewSimulation(LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default));
        sim.ApplyNow(new SetTradePreferencesCommand(Ana, [sim.Defs.Trade(playerTrade)], []));
        sim.ApplyNow(new StartRoundCommand());
        var party = sim.Parties.Send(sim, sim.Defs.Party("GildedSpurs"));
        foreach (var e in party)
            Hospitality.DirectionsSystem.KnowsTheTown(sim, e);
        // Only the bow is worn, so the bowman's want is the woodshop's alone.
        foreach (var e in party)
        {
            if (sim.World.TryGet<Equipment>(e, out var eq) && sim.World.IsAlive(eq.Weapon))
                sim.World.Get<Thing>(eq.Weapon).Hp = sim.World.Get<Thing>(eq.Weapon).MaxHp;
            foreach (var worn in sim.Worn(e))
                sim.World.Get<Thing>(worn).Hp = sim.World.Get<Thing>(worn).MaxHp;
        }
        var bowman = party.First(e => Fletching.Shoots(sim, e));
        var bow = sim.World.Get<Equipment>(bowman).Weapon;
        sim.World.Get<Thing>(bow).Hp = sim.World.Get<Thing>(bow).MaxHp * 0.5f;
        return (sim, bowman, bow);
    }

    private static Entity Keeper(Simulation sim, string trade) =>
        sim.Creatures().Single(e => sim.World.TryGet<Trade>(e, out var t) && t.Def.Name == trade);

    private static Order? MendOf(Simulation sim, Entity owner) =>
        sim.Shops.All.FirstOrDefault(e => e.Poster == owner && ShopSystem.IsMend(e.Kind.Kind));

    [Fact]
    public void WhoeverMakesAPieceMendsIt()
    {
        var sim = VillageRound.NewSimulation(LoadDefs(), 1234);
        VillageRound.Setup(sim);
        ServiceKind? KindOf(string def, float hp, float dull = 0f)
        {
            var item = sim.CreateItem(sim.Defs.Thing(def), 1);
            sim.World.Get<Thing>(item).Hp = sim.World.Get<Thing>(item).MaxHp * hp;
            if (dull > 0)
                WeaponWear.SetDullness(sim, item, dull);
            return WeaponWear.MendKind(sim, item);
        }
        Assert.Equal(ServiceKind.WoodRepair, KindOf("Bow", 0.5f));
        Assert.Equal(ServiceKind.WoodRepair, KindOf("Club", 0.5f));
        Assert.Equal(ServiceKind.WoodRepair, KindOf("Staff", 0.5f));
        Assert.Equal(ServiceKind.CrossbowRepair, KindOf("Crossbow", 0.5f));
        Assert.Equal(ServiceKind.Repair, KindOf("ShortSword", 0.5f));
        Assert.Equal(ServiceKind.Repair, KindOf("ShortSword", 1f, 0.7f)); // a dull edge is the smith's
        Assert.Null(KindOf("Bow", 0.9f)); // not worn enough to ask
    }

    [Fact]
    public void AWornBowIsAskedOfTheWoodshopNotTheForge()
    {
        var (sim, bowman, bow) = TownWithAWornBow("Empath");
        for (var t = 0; t < 12 && MendOf(sim, bowman) == null; t++)
            sim.TickMany(SimClock.TicksPerHour / 6);
        var order = MendOf(sim, bowman);
        Assert.NotNull(order);
        Assert.Equal(ServiceKind.WoodRepair, order.Kind.Kind);
        Assert.Equal(TownRole.Woodshop, order.Kind.AtRole);
        Assert.Equal(bow, order.Subject);
        Assert.DoesNotContain(sim.Shops.All, e => e.Poster == bowman && e.Kind.Kind == ServiceKind.Repair);
    }

    [Fact]
    public void AnAICarpenterMendsABowAndIsPaid()
    {
        var (sim, bowman, bow) = TownWithAWornBow("Empath");
        var carpenter = Keeper(sim, "Carpenter");
        Assert.False(sim.World.Has<Avatar>(carpenter));
        var purse = Coin.Count(sim, carpenter);

        for (var h = 0; h < 12 && MendOf(sim, bowman) is not { Status: OrderStatus.Done }; h++)
            sim.TickMany(SimClock.TicksPerHour);

        var order = MendOf(sim, bowman);
        Assert.NotNull(order);
        Assert.True(order.Status == OrderStatus.Done, $"{order.Status}, assignee {order.AssigneeName}, carpenter: {sim.Ai.LastFailure.GetValueOrDefault(carpenter)} / {sim.World.Get<Mind>(carpenter).Job?.Report}\n"
            + string.Join("\n", sim.Speech.Kept.TakeLast(12).Select(u => $"{u.SpeakerName}: {u.Text}")));
        Assert.Equal(carpenter, order.Assignee);
        Assert.Equal(bow, sim.World.Get<Equipment>(bowman).Weapon);
        Assert.True(WeaponWear.Mended(sim, bow));
        Assert.True(Coin.Count(sim, carpenter) > purse);
    }

    [Fact]
    public void APlayerCarpenterMendsABowOnTheTilleringStick()
    {
        var (sim, bowman, bow) = TownWithAWornBow("Carpenter");
        var me = Players.CharacterOf(sim, Ana);
        for (var t = 0; t < 12 && MendOf(sim, bowman) == null; t++)
            sim.TickMany(SimClock.TicksPerHour / 6);
        var order = MendOf(sim, bowman)!;
        sim.ApplyNow(new TakeOrderCommand(me, order.Id));
        var at = sim.World.Get<Position>(bowman).Cell;
        var cell = sim.SpiralFrom(at, 3).First(c => c != at && sim.Map.Passable(c) && c.Z == at.Z);
        sim.World.Get<Position>(me).Cell = cell;
        sim.World.Get<Position>(me).Pos = Position.CenterOf(cell);
        Assert.True(sim.Shops.TryHandOver(sim, order));
        Assert.True(Inventory.OnPerson(sim, me, bow));

        // At the bench with a plank, Mend opens the tillering stick on the worn bow.
        var shop = ShopSystem.WorkplaceOf(sim, sim.Defs.Trade("Carpenter"));
        var bench = Carpenters.StationOf(sim, shop, Carpenters.BenchDef);
        var by = sim.SpiralFrom(sim.World.Get<Thing>(bench).Cell, 2).First(c => sim.Map.Passable(c) && AvatarSystem.InReach(sim, c, bench));
        sim.World.Get<Position>(me).Cell = by;
        sim.World.Get<Position>(me).Pos = Position.CenterOf(by);
        int Planks() => Core.CraftCommandRules.Sources(sim, me).Where(i => sim.World.Get<Thing>(i).Def.Name == Carpenters.PlanksDef).Sum(i => sim.World.Get<Stack>(i).Count);
        Inventory.Stow(sim, me, sim.CreateItem(sim.Defs.Thing(Carpenters.PlanksDef), 2));
        var planks = Planks();
        sim.ApplyNow(new RepairCommand(me, bench, bow));
        var session = Assert.IsType<BowSession>(sim.World.Get<WorkSession>(me));
        Assert.Equal(bow, session.Mending);
        // Set stiff in a few places only: most of the limbs have kept their arc.
        Assert.InRange(session.Stiffness.Count(s => s > BowSession.Even + 0.05f), 1, 3);

        // Worked well, it comes back sound for a plank.
        Assert.True(WeaponWear.MendAtBench(sim, me, bench, bow, 1f));
        Assert.True(WeaponWear.Mended(sim, bow));
        Assert.Equal(planks - 1, Planks());
    }

    [Fact]
    public void APoorMendLeavesItShortOfWhatTheyllTakeBack()
    {
        var sim = Create(20, 20);
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), "Carp", C(5, 5), ageDays: 25 * 365);
        sim.GiveStartingPack(me);
        var bench = sim.SpawnStructure(sim.Thing(Carpenters.BenchDef), C(6, 5));
        var bow = sim.CreateItem(sim.Defs.Thing("Bow"), 1);
        sim.World.Get<Thing>(bow).Hp = sim.World.Get<Thing>(bow).MaxHp * 0.4f;
        Inventory.Stow(sim, me, bow);
        Assert.False(WeaponWear.MendAtBench(sim, me, bench, bow, 1f)); // no plank to hand
        Inventory.Stow(sim, me, sim.CreateItem(sim.Defs.Thing(Carpenters.PlanksDef), 2)); // worn this far, two
        Assert.True(WeaponWear.MendAtBench(sim, me, bench, bow, 0.2f));
        Assert.False(WeaponWear.Mended(sim, bow));
        Assert.True(WeaponWear.HpFraction(sim, bow) >= WeaponWear.BenchMendFloor);
    }

    [Fact]
    public void AWornCrossbowGoesToTheTinkersAndTheAITinkerMendsIt()
    {
        var (sim, bowman, bow) = TownWithAWornBow("Empath");
        // The bowman's traded up: a crossbow, worn.
        sim.World.Get<Thing>(bow).Hp = sim.World.Get<Thing>(bow).MaxHp;
        var crossbow = sim.CreateItem(sim.Defs.Thing("Crossbow"), 1);
        sim.World.Get<Thing>(crossbow).Hp = sim.World.Get<Thing>(crossbow).MaxHp * 0.5f;
        sim.World.Remove<Equipment>(bowman);
        sim.DestroyThing(bow);
        sim.EquipItem(bowman, crossbow);
        var tinker = Keeper(sim, "Tinker");

        for (var h = 0; h < 12 && MendOf(sim, bowman) is not { Status: OrderStatus.Done }; h++)
            sim.TickMany(SimClock.TicksPerHour);

        var order = MendOf(sim, bowman);
        Assert.NotNull(order);
        Assert.Equal(ServiceKind.CrossbowRepair, order.Kind.Kind);
        Assert.True(order.Status == OrderStatus.Done, $"{order.Status}, tinker: {sim.Ai.LastFailure.GetValueOrDefault(tinker)} / {sim.World.Get<Mind>(tinker).Job?.Report}");
        Assert.Equal(tinker, order.Assignee);
        Assert.True(WeaponWear.Mended(sim, crossbow));
    }
}

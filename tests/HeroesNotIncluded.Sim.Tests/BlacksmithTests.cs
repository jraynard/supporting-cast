using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>Weapon wear and the two ways of putting it right: the whetstone and the anvil (Stage 3 of M2).</summary>
public class BlacksmithTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    /// <summary>A player in a round with a whetstone, a hot forge beside them and a short sword in their pack.</summary>
    private static (Simulation Sim, Entity Me, Entity Forge, Entity Sword) AtTheBench(float hp = 1f, float dullness = 0f)
    {
        var sim = Create(40, 40);
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), "Smith", C(10, 10), ageDays: 25 * 365);
        sim.Round.OpenLobby();
        sim.Round.Sit(Ana, "Smith", null, null, null);
        sim.Round.Start(sim);
        sim.GiveStartingPack(me);
        var forge = sim.SpawnStructure(sim.Thing("Forge"), C(11, 10));
        sim.RebuildDerivedIfNeeded();
        sim.World.Get<Fueled>(forge).Fuel = 10;
        sim.Map.Temperature[sim.Map.Idx(C(11, 10))] = 1200;
        Inventory.Stow(sim, me, sim.CreateItem(sim.Thing("Whetstone"), 1));
        Inventory.Stow(sim, me, sim.CreateItem(sim.Thing("IronBar"), 4));
        var sword = sim.CreateItem(sim.Thing("ShortSword"), 1);
        sim.World.Get<Thing>(sword).Hp = sim.World.Get<Thing>(sword).MaxHp * hp;
        WeaponWear.SetDullness(sim, sword, dullness);
        Inventory.Stow(sim, me, sword);
        return (sim, me, forge, sword);
    }

    /// <summary>Grind as a careful player would: steer for the middle of the band, ease off when it's hot.</summary>
    private static void Grind(Simulation sim, Entity me, bool easeOff = true, int maxTicks = 60 * 120)
    {
        var predictor = new WorkPredictor();
        var resting = false;
        for (var t = 0; t < maxTicks && sim.World.Has<WorkSession>(me); t++)
        {
            sim.Tick();
            var axis = 0;
            var hold = false;
            if (predictor.Session is SharpenSession s)
            {
                var off = s.Angle - 0.5f;
                axis = MathF.Abs(off) > s.HalfWidth * 0.3f ? -Math.Sign(off) : 0;
                resting = easeOff && (resting ? s.Heat > 0.3f : s.Heat > 0.8f);
                hold = !resting && s.InBand;
            }
            predictor.Update(sim, me, 1, false, hold, axis, c => sim.ApplyNow(c));
        }
        sim.Tick();
    }

    private static void Hammer(Simulation sim, Entity me, int maxTicks = 60 * 60)
    {
        var predictor = new WorkPredictor();
        for (var t = 0; t < maxTicks && sim.World.Has<WorkSession>(me); t++)
        {
            sim.Tick();
            var strike = predictor.Session is HammerSession { Ready: true } h && MathF.Abs(h.Marker - h.Mark) <= h.HalfWidth * 0.5f;
            predictor.Update(sim, me, 1, strike, false, 0, c => sim.ApplyNow(c));
        }
        sim.Tick();
    }

    [Fact]
    public void BladesDullAndChipWithUseAndCutWorseForIt()
    {
        var (sim, me, _, sword) = AtTheBench();
        Assert.Equal(1f, WeaponWear.DamageFactor(sim, sword));
        for (var i = 0; i < 20; i++)
            WeaponWear.Struck(sim, me, sword, turned: i % 2 == 0, damage: 6f);

        Assert.True(WeaponWear.Dullness(sim, sword) > WeaponWear.WantsMendDullness);
        Assert.True(WeaponWear.HpFraction(sim, sword) < 1f);
        Assert.True(WeaponWear.DamageFactor(sim, sword) < 0.85f);
        Assert.True(WeaponWear.NeedsMending(sim, sword));
        Assert.DoesNotContain("keen", Perception.For(sim, me).Condition(sword));
    }

    [Fact]
    public void ACarefulHandOnTheWhetstonePutsTheEdgeBack()
    {
        var (sim, me, _, sword) = AtTheBench(dullness: 0.8f);
        sim.ApplyNow(new SharpenCommand(me, sword));
        Assert.True(sim.World.Has<WorkSession>(me));
        Grind(sim, me);

        Assert.False(sim.World.Has<WorkSession>(me));
        Assert.True(WeaponWear.Dullness(sim, sword) <= WeaponWear.MendedDullness, $"dullness {WeaponWear.Dullness(sim, sword)}");
        Assert.True(WeaponWear.Mended(sim, sword));
    }

    [Fact]
    public void GrindingWithoutEasingOffBurnsTheTemper()
    {
        var (sim, me, _, sword) = AtTheBench(dullness: 1f);
        sim.World.Add(sword, new Crafted { Quality = Quality.Fine, Maker = me });
        var hp = sim.World.Get<Thing>(sword).Hp;
        sim.ApplyNow(new SharpenCommand(me, sword));
        Grind(sim, me, easeOff: false);

        Assert.Equal(Quality.Plain, sim.World.Get<Crafted>(sword).Quality);
        Assert.True(sim.World.Get<Thing>(sword).Hp < hp);
    }

    [Fact]
    public void AStoneCantTakeOutChips()
    {
        var (sim, me, _, sword) = AtTheBench(hp: 0.5f, dullness: 0.6f);
        var hp = sim.World.Get<Thing>(sword).Hp;
        sim.ApplyNow(new SharpenCommand(me, sword));
        Grind(sim, me);

        Assert.True(sim.World.Get<Thing>(sword).Hp < hp, "grinding at chips only wears the blade");
        Assert.False(WeaponWear.Mended(sim, sword));
    }

    [Fact]
    public void HammeringOutChipsAtTheForgeLeavesTheBladeToSharpen()
    {
        var (sim, me, forge, sword) = AtTheBench(hp: 0.4f);
        var kit = new SmithyKit(forge, sim.SpawnStructure(sim.Thing("Anvil"), C(9, 10)), sim.SpawnStructure(sim.Thing("QuenchTub"), C(10, 11)),
            sim.SpawnStructure(sim.Thing("Grindstone"), C(10, 9)));
        sim.SpawnStructure(sim.Thing("Bellows"), C(12, 10));
        sim.RebuildDerivedIfNeeded();
        var bars = Coinless(sim, me, "IronBar");

        // Into the tongs, then the fire and the anvil, a heat at a time.
        sim.ApplyNow(new RepairCommand(me, forge, sword));
        Assert.True(Smithing.PieceOf(sim, me) is { IsMend: true } p && p.Mending == sword);
        kit.Drive(sim, me);

        Assert.Null(Smithing.PieceOf(sim, me));
        Assert.True(WeaponWear.HpFraction(sim, sword) >= WeaponWear.MendedHp);
        Assert.True(WeaponWear.Dullness(sim, sword) >= WeaponWear.DullAfterHammering, "the anvil flattens the edge");
        Assert.True(Coinless(sim, me, "IronBar") < bars, "mending uses iron");

        // Then the grindstone, which is quicker than a whetstone, and it's right.
        Assert.Equal("Grind an edge on the " + sim.LabelOf(sword), Smithing.StepAt(sim, me, kit.Grindstone));
        sim.ApplyNow(new SmithCommand(me, kit.Grindstone));
        Assert.True(sim.World.Get<WorkSession>(me) is SharpenSession { OnWheel: true });
        sim.ApplyNow(new StopWorkCommand(me));
        kit.Grind(sim, me);
        Assert.True(WeaponWear.Mended(sim, sword));
    }

    private static int Coinless(Simulation sim, Entity me, string def) =>
        sim.StoredIn(sim.PackOf(me)).Where(i => sim.World.Get<Thing>(i).Def.Name == def).Sum(i => sim.World.Get<Stack>(i).Count);

    // ---------------- Repair orders ----------------

    private static (Simulation Sim, Entity Owner, Entity Sword) TownWithAWornSword(string playerTrade)
    {
        var sim = VillageRound.NewSimulation(LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default, []));
        sim.ApplyNow(new SetTradePreferencesCommand(Ana, [sim.Defs.Trade(playerTrade)], []));
        sim.ApplyNow(new StartRoundCommand());
        var party = sim.Parties.Send(sim, sim.Defs.Party("GildedSpurs"));
        foreach (var e in party)
            Hospitality.DirectionsSystem.KnowsTheTown(sim, e); // finding the way is InnTalkTests'
        var owner = party.First(e => sim.World.TryGet<Equipment>(e, out var eq) && WeaponWear.Edged(sim.World.Get<Thing>(eq.Weapon).Def));
        var sword = sim.World.Get<Equipment>(owner).Weapon;
        sim.World.Get<Thing>(sword).Hp = sim.World.Get<Thing>(sword).MaxHp * 0.5f;
        WeaponWear.SetDullness(sim, sword, 0.7f);
        return (sim, owner, sword);
    }

    private static Order RepairOf(Simulation sim, Entity owner) =>
        sim.Shops.All.Single(e => e.Poster == owner && e.Kind.Kind == ServiceKind.Repair);

    [Fact]
    public void AnAISmithSeesARepairThroughAndIsPaid()
    {
        var (sim, owner, sword) = TownWithAWornSword("Healer");
        var smith = sim.Creatures().Single(e => sim.World.TryGet<Trade>(e, out var t) && t.Def.Name == "Blacksmith");
        Assert.False(sim.World.Has<Avatar>(smith));
        var purse = Coin.Count(sim, smith);

        for (var h = 0; h < 10 && sim.Shops.All.All(e => e.Kind.Kind != ServiceKind.Repair || e.IsOpen); h++)
            sim.TickMany(SimClock.TicksPerHour);

        var order = RepairOf(sim, owner);
        Assert.True(order.Status == OrderStatus.Done, $"{order.Status}, assignee {order.AssigneeName}, smith: {sim.Ai.LastFailure.GetValueOrDefault(smith)} / {sim.World.Get<Mind>(smith).Job?.Report}\n"
            + string.Join("\n", sim.Speech.Kept.TakeLast(12).Select(u => $"{u.SpeakerName}: {u.Text}")));
        Assert.Equal(smith, order.Assignee);
        Assert.Equal(sword, sim.World.Get<Equipment>(owner).Weapon);
        Assert.True(WeaponWear.Mended(sim, sword));
        Assert.True(Coin.Count(sim, smith) > purse, $"purse {purse} now {Coin.Count(sim, smith)}, held {sim.World.Get<Carrying>(smith).Item}, pack {sim.PackOf(smith)} "
            + string.Join("\n", sim.Speech.Kept.TakeLast(6).Select(u => $"{u.SpeakerName}: {u.Text}")));
    }

    [Fact]
    public void TheyWaitOnABladeThatsStillBeingWorked()
    {
        var (sim, owner, sword) = TownWithAWornSword("Blacksmith");
        var me = Players.CharacterOf(sim, Ana);
        sim.TickMany(SimClock.TicksPerHour / 6);
        var order = RepairOf(sim, owner);
        sim.ApplyNow(new TakeOrderCommand(me, order.Id));
        var at = sim.World.Get<Position>(owner).Cell;
        var cell = sim.SpiralFrom(at, 3).First(c => c != at && sim.Map.Passable(c) && c.Z == at.Z);
        sim.World.Get<Position>(me).Cell = cell;
        sim.World.Get<Position>(me).Pos = Position.CenterOf(cell);

        // Late in their wait for someone to take it on, the hand-over starts the work's own clock.
        var posted = order.Deadline;
        Assert.True(sim.Shops.TryHandOver(sim, order));
        Assert.True(order.Deadline >= sim.Clock.Tick + SimClock.HoursToTicks(ShopSystem.HandedOverHours) - 1);

        // Past that, with the blade still at the smith's, they ask after it and wait on.
        sim.TickMany((int)(order.Deadline - sim.Clock.Tick) + 120);
        Assert.True(order.IsOpen, $"{order.Status}");
        Assert.Contains(sim.Speech.Kept, u => u.Speaker == owner && u.Text.Contains("nearly done"));
        Assert.True(order.Deadline > posted);
    }

    [Fact]
    public void ABladeHandedBackUnmendedIsRefusedAndTheJobReopens()
    {
        var (sim, owner, sword) = TownWithAWornSword("Blacksmith");
        var me = Players.CharacterOf(sim, Ana);
        sim.TickMany(SimClock.TicksPerHour / 6);
        var order = RepairOf(sim, owner);
        Assert.Contains(Perception.For(sim, me).Orders(), e => e.Id == order.Id); // word reaches the smith

        sim.ApplyNow(new TakeOrderCommand(me, order.Id));
        Assert.Equal(me, order.Assignee);
        var at = sim.World.Get<Position>(owner).Cell;
        var cell = sim.SpiralFrom(at, 3).First(c => c != at && sim.Map.Passable(c) && c.Z == at.Z);
        sim.World.Get<Position>(me).Cell = cell;
        sim.World.Get<Position>(me).Pos = Position.CenterOf(cell);
        Assert.True(sim.Shops.TryHandOver(sim, order));
        Assert.True(Inventory.OnPerson(sim, me, sword));
        Assert.False(sim.World.Has<Equipment>(owner));

        sim.World.Get<Position>(owner).Cell = cell + new int3(1, 0, 0) == at ? at : sim.World.Get<Position>(owner).Cell;
        sim.ApplyNow(new GiveCommand(me, owner, sword, 1));
        Assert.True(order.IsOpen);
        Assert.True(order.Assignee.IsNull, "refused: the job is open again");
        Assert.Equal(sword, sim.World.Get<Equipment>(owner).Weapon);

        // Done properly the second time, it's paid for.
        sim.ApplyNow(new TakeOrderCommand(me, order.Id));
        Assert.True(sim.Shops.TryHandOver(sim, order));
        sim.World.Get<Thing>(sword).Hp = sim.World.Get<Thing>(sword).MaxHp;
        WeaponWear.SetDullness(sim, sword, 0f);
        var purse = Coin.Count(sim, me);
        sim.ApplyNow(new GiveCommand(me, owner, sword, 1));
        Assert.Equal(OrderStatus.Done, order.Status);
        Assert.Equal(purse + order.Price, Coin.Count(sim, me));
    }
}

using SupportingCast.Sim.Components;
using SupportingCast.Sim.Core;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Tests;

/// <summary>The player's hands, pack and gear: F on the ground, and the inventory commands.</summary>
public class InventoryTests
{
    private static (Simulation Sim, Entity Me) Setup(bool pack = true)
    {
        var sim = TestWorld.Create(30, 20, 1, 4);
        sim.Climate.BaseTemperature = 20;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 20f);
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), "Bearer", C(10, 10), ageDays: 25 * 365);
        Array.Fill(sim.World.Get<Needs>(me).Values, 1f);
        sim.SetSkills(me);
        sim.World.Add(me, new Avatar());
        if (pack)
            sim.GiveStartingPack(me);
        return (sim, me);
    }

    private static void Press(Simulation sim, Entity me, int3 cell)
    {
        sim.Enqueue(new AvatarWorkCommand(me, true, cell));
        sim.Enqueue(new AvatarWorkCommand(me, false));
        sim.Tick();
    }

    private static void Do(Simulation sim, ISimCommand command)
    {
        sim.Enqueue(command);
        sim.Tick();
    }

    private static int Packed(Simulation sim, Entity me, string def) =>
        sim.StoredIn(sim.PackOf(me)).Where(e => sim.World.Get<Thing>(e).Def.Name == def).Sum(e => sim.World.Get<Stack>(e).Count);

    private static Entity Spawn(Simulation sim, string def, int3 cell)
    {
        sim.SpawnItem(sim.Thing(def), 1, cell);
        return sim.Map.Item[sim.Map.Idx(cell)];
    }

    private static Entity Keg(Simulation sim, Entity me) => sim.World.Get<Carrying>(me).Item = sim.CreateItem(sim.Thing("Keg"), 1);

    [Fact]
    public void GearOnTheGroundGoesStraightOnIntoAnEmptySlot()
    {
        var (sim, me) = Setup();
        Keg(sim, me); // hands full: doesn't matter
        var helm = Spawn(sim, "IronHelm", C(11, 10));
        Assert.Equal(AvatarActionKind.Equip, AvatarSystem.ActionAt(sim, me, C(11, 10)).Kind);
        Press(sim, me, C(11, 10));
        Assert.Equal(helm, sim.World.Get<Apparel>(me).Head);

        // The slot's taken now: a second helm is picked up instead.
        sim.SpawnItem(sim.Thing("LeatherCap"), 1, C(11, 10));
        Assert.Equal(AvatarActionKind.PickUp, AvatarSystem.ActionAt(sim, me, C(11, 10)).Kind);
        Press(sim, me, C(11, 10));
        Assert.Equal(1, Packed(sim, me, "LeatherCap"));
        Assert.Equal(helm, sim.World.Get<Apparel>(me).Head);

        sim.SpawnItem(sim.Thing("Axe"), 1, C(11, 10));
        Assert.Equal("Wield woodcutter's axe", AvatarSystem.ActionAt(sim, me, C(11, 10)).Label);
    }

    [Fact]
    public void PickingUpFillsThePackThenTheHands()
    {
        var (sim, me) = Setup();
        sim.SpawnItem(sim.Thing("Wood"), 40, C(11, 10), sim.Defs.Material("Birch")); // birch: the 1 kg baseline
        Press(sim, me, C(11, 10));
        Assert.Equal(25, Packed(sim, me, "Wood")); // 25 kg of 1 kg wood
        var held = sim.World.Get<Carrying>(me).Item;
        Assert.Equal(15, sim.World.Get<Stack>(held).Count);
        Assert.True(sim.Map.Item[sim.Map.Idx(C(11, 10))].IsNull);

        // Full pack, full hands: nothing more to pick up.
        sim.SpawnItem(sim.Thing("Stone"), 5, C(11, 10));
        Assert.NotEqual(AvatarActionKind.PickUp, AvatarSystem.ActionAt(sim, me, C(11, 10)).Kind);
    }

    [Fact]
    public void WithAKegInHandThePackStillPicksThingsUp()
    {
        var (sim, me) = Setup();
        var keg = Keg(sim, me);
        sim.SpawnItem(sim.Thing("Berries"), 10, C(11, 10));
        Press(sim, me, C(11, 10));
        Assert.Equal(keg, sim.World.Get<Carrying>(me).Item);
        Assert.Equal(10, Packed(sim, me, "Berries"));

        // Without a pack, hands full means no picking up, as before packs.
        var (bare, other) = Setup(pack: false);
        Keg(bare, other);
        bare.SpawnItem(bare.Thing("Berries"), 10, C(11, 10));
        Assert.Equal(AvatarActionKind.None, AvatarSystem.ActionAt(bare, other, C(11, 10)).Kind);
    }

    [Fact]
    public void BuildingAndEatingTakeFromThePack()
    {
        var (sim, me) = Setup();
        Keg(sim, me);
        sim.Store(sim.PackOf(me), sim.CreateItem(sim.Thing("Wood"), 20, sim.Defs.Material("Pine")));
        var wall = sim.PlaceBlueprint(sim.Thing("WoodWall"), C(11, 10));
        var deliver = AvatarSystem.ActionAt(sim, me, C(11, 10));
        Assert.Equal(AvatarActionKind.Deliver, deliver.Kind);
        Assert.Contains("(pack)", deliver.Label);
        Press(sim, me, C(11, 10));
        var cost = sim.Thing("WoodWall").Building!.Cost[0].Count;
        Assert.Equal(cost, sim.World.Get<Construction>(wall).Delivered[0]);
        Assert.Equal(20 - cost, Packed(sim, me, "Wood"));

        sim.Store(sim.PackOf(me), sim.CreateItem(sim.Thing("Berries"), 10));
        var food = sim.Defs.Need("Food");
        sim.World.Get<Needs>(me).Values[food.Index] = 0.3f;
        Assert.NotEqual(AvatarActionKind.Eat, AvatarSystem.ActionAt(sim, me, C(10, 10)).Kind);
        Do(sim, new EatCommand(me, sim.StoredIn(sim.PackOf(me)).First(e => sim.World.Get<Thing>(e).Def.Name == "Berries")));
        sim.TickMany(120);
        Assert.True(sim.World.Get<Needs>(me).Get(food) > 0.3f);
        Assert.True(Packed(sim, me, "Berries") < 10);
    }

    [Fact]
    public void HoldingSwapsWithThePackButNeverStowsTheKeg()
    {
        var (sim, me) = Setup();
        var pack = sim.PackOf(me);
        sim.Store(pack, sim.CreateItem(sim.Thing("Stone"), 5));
        var stone = sim.StoredIn(pack).Single();
        sim.World.Get<Carrying>(me).Item = sim.CreateItem(sim.Thing("Berries"), 4);
        Do(sim, new HoldCommand(me, stone));
        Assert.Equal("Stone", sim.World.Get<Thing>(sim.World.Get<Carrying>(me).Item).Def.Name);
        Assert.Equal(4, Packed(sim, me, "Berries"));

        Do(sim, new StowCommand(me));
        Assert.False(sim.World.Get<Carrying>(me).HasItem);
        Assert.Equal(5, Packed(sim, me, "Stone"));

        var keg = Keg(sim, me);
        Do(sim, new HoldCommand(me, sim.StoredIn(pack)[0]));
        Assert.Equal(keg, sim.World.Get<Carrying>(me).Item); // refused: the keg doesn't go in a pack
        Do(sim, new StowCommand(me));
        Assert.Equal(keg, sim.World.Get<Carrying>(me).Item);
    }

    [Fact]
    public void EquippingFromThePackSwapsTheOldPieceIntoIt()
    {
        var (sim, me) = Setup();
        var cap = sim.Wear(me, sim.Thing("LeatherCap"));
        sim.Store(sim.PackOf(me), sim.CreateItem(sim.Thing("IronHelm"), 1));
        var helm = sim.StoredIn(sim.PackOf(me)).Single();
        Do(sim, new EquipCommand(me, helm));
        Assert.Equal(helm, sim.World.Get<Apparel>(me).Head);
        Assert.Equal([cap], sim.StoredIn(sim.PackOf(me)));
        var head = sim.World.Get<Creature>(me).Def.BodyDef!.PartIndex("Head");
        Assert.Equal(helm, Combat.CombatRules.ArmorAt(sim, me, head, DamageType.Cut).Item);

        // A full pack: the old piece goes into empty hands, then onto the ground.
        sim.Store(sim.PackOf(me), sim.CreateItem(sim.Thing("Stone"), 12)); // 24 + 0.5 kg
        Do(sim, new EquipCommand(me, cap));
        Assert.Equal(cap, sim.World.Get<Apparel>(me).Head);
        Assert.Equal(helm, sim.World.Get<Carrying>(me).Item);
        sim.Store(sim.PackOf(me), sim.CreateItem(sim.Thing("Berries"), 10)); // the last kilo
        Do(sim, new UnequipCommand(me, GearSlot.Head));
        Assert.True(sim.World.Get<Thing>(cap).Spawned); // pack and hands full: onto the ground
    }

    [Fact]
    public void TakingThePackOffKeepsItsContents()
    {
        var (sim, me) = Setup();
        var pack = sim.PackOf(me);
        sim.Store(pack, sim.CreateItem(sim.Thing("Wood"), 10, sim.Defs.Material("Oak")));
        Do(sim, new UnequipCommand(me, GearSlot.Back));
        Assert.Equal(pack, sim.World.Get<Carrying>(me).Item); // into empty hands
        Assert.Equal(10, sim.World.Get<Stack>(sim.StoredIn(pack).Single()).Count);
        Assert.True(sim.PackOf(me).IsNull);

        Do(sim, new EquipCommand(me, pack));
        Assert.Equal(pack, sim.PackOf(me));
        Assert.False(sim.World.Get<Carrying>(me).HasItem);
    }

    [Fact]
    public void AnotherPacksContentsAreOnlyReachableOnTheGround()
    {
        var (sim, me) = Setup();
        var other = sim.SpawnCreature(sim.Defs.Creature("Human"), "Other", C(11, 10), ageDays: 25 * 365);
        var theirs = sim.GiveStartingPack(other);
        sim.Store(theirs, sim.CreateItem(sim.Thing("Berries"), 5));
        var berries = sim.StoredIn(theirs).Single();
        Do(sim, new TakeFromStorageCommand(me, berries));
        Assert.False(sim.World.Get<Carrying>(me).HasItem); // not out of someone's back

        sim.TakeOff(other, ApparelSlot.Back);
        sim.World.Get<Position>(other) = Position.At(C(20, 10));
        var cell = sim.World.Get<Thing>(theirs).Cell;
        sim.World.Get<Position>(me) = Position.At(cell);
        Assert.Equal(AvatarActionKind.OpenStorage, AvatarSystem.ActionAt(sim, me, cell).Kind);
        Do(sim, new TakeFromStorageCommand(me, berries));
        Assert.Equal(berries, sim.World.Get<Carrying>(me).Item);

        Do(sim, new StoreFromPackCommand(me, theirs, berries)); // not from hands
        Do(sim, new StowCommand(me));
        Do(sim, new StoreFromPackCommand(me, theirs, sim.StoredIn(sim.PackOf(me)).Single()));
        Assert.Equal(5, sim.StoredIn(theirs).Sum(e => sim.World.Get<Stack>(e).Count));
    }

    [Fact]
    public void TradingUpPacksMovesTheContents()
    {
        var (sim, me) = Setup();
        var old = sim.PackOf(me);
        sim.Store(old, sim.CreateItem(sim.Thing("Stone"), 4));
        var spare = Spawn(sim, "Satchel", C(11, 10));
        Do(sim, new EquipCommand(me, spare));
        Assert.Equal(spare, sim.PackOf(me));
        Assert.Equal(4, Packed(sim, me, "Stone"));
        Assert.Empty(sim.StoredIn(old));
        Assert.Equal(old, sim.World.Get<Carrying>(me).Item);
    }
}

/// <summary>Companions with packs: one trip carries more, and the load never gets stranded in a pack.</summary>
public class CompanionPackTests
{
    private static (Simulation Sim, Entity Pawn) Setup(params WorkType[] only)
    {
        var sim = TestWorld.Create(40, 40, 1, 4);
        sim.Climate.BaseTemperature = 20;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 20f);
        var e = sim.SpawnCreature(sim.Defs.Creature("Human"), "Worker", C(5, 5), ageDays: 25 * 365);
        Array.Fill(sim.World.Get<Needs>(e).Values, 1f);
        sim.SetSkills(e);
        var mind = sim.World.Get<Mind>(e);
        Array.Fill(mind.WorkPriority, 0);
        foreach (var w in only)
            mind.WorkPriority[(int)w] = 1;
        sim.GiveStartingPack(e);
        return (sim, e);
    }

    private static void Stockpile(Simulation sim)
    {
        for (var x = 20; x < 24; x++)
        for (var y = 20; y < 24; y++)
            sim.Map.SetZone(sim.Map.Idx(C(x, y)), 1);
    }

    [Fact]
    public void AHaulerFillsTheirPackOnOneTrip()
    {
        var (sim, pawn) = Setup(WorkType.Haul);
        Stockpile(sim);
        var oak = sim.Defs.Material("Oak");
        sim.SpawnItem(sim.Thing("Wood"), 20, C(8, 8), oak);
        sim.SpawnItem(sim.Thing("Wood"), 20, C(10, 8), oak);
        sim.SpawnItem(sim.Thing("Wood"), 20, C(9, 11), oak);
        var mostPacked = 0f;
        for (var t = 0; t < SimClock.TicksPerHour; t++)
        {
            sim.Tick();
            mostPacked = Math.Max(mostPacked, sim.LoadOf(sim.PackOf(pawn)));
        }
        // 50 wood fill the hands; the 10 past a stack ride in the pack, all on the one trip.
        Assert.True(mostPacked >= 10f, $"the pack should have carried the rest (at most {mostPacked} kg)");
        Assert.Equal(60, sim.SpawnedItems().Where(e => sim.Map.Zone[sim.Map.Idx(sim.World.Get<Thing>(e).Cell)] == 1)
            .Sum(e => sim.World.Get<Stack>(e).Count));
        Assert.Empty(sim.StoredIn(sim.PackOf(pawn)));
    }

    [Fact]
    public void AnInterruptedHaulSetsThePackLoadDown()
    {
        var (sim, pawn) = Setup(WorkType.Haul);
        Stockpile(sim);
        var oak = sim.Defs.Material("Oak");
        sim.SpawnItem(sim.Thing("Wood"), 45, C(8, 8), oak);
        sim.SpawnItem(sim.Thing("Wood"), 30, C(10, 8), oak);
        for (var t = 0; t < SimClock.TicksPerHour && sim.LoadOf(sim.PackOf(pawn)) <= 0; t++)
            sim.Tick();
        Assert.True(sim.LoadOf(sim.PackOf(pawn)) > 0);
        AiSystem.InterruptAll(sim);
        Assert.Empty(sim.StoredIn(sim.PackOf(pawn)));
        Assert.False(sim.World.Get<Carrying>(pawn).HasItem);
        Assert.Equal(75, sim.AvailableItems().Sum(e => sim.World.Get<Stack>(e).Count));
    }

    [Fact]
    public void ABuilderBringsTheWholeCostInOneTrip()
    {
        var (sim, pawn) = Setup(WorkType.Construct);
        var oak = sim.Defs.Material("Oak");
        // Small stacks: without a pack it'd take a trip each.
        sim.SpawnItem(sim.Thing("Wood"), 3, C(8, 8), oak);
        sim.SpawnItem(sim.Thing("Wood"), 3, C(9, 8), oak);
        sim.SpawnItem(sim.Thing("Wood"), 3, C(10, 8), oak);
        sim.SpawnItem(sim.Thing("Wood"), 3, C(8, 9), oak);
        var site = sim.PlaceBlueprint(sim.Thing("WoodWall"), C(20, 20));
        var need = sim.Thing("WoodWall").Building!.Cost[0].Count;
        Assert.True(need > 3);
        var deliveries = 0;
        var last = 0;
        for (var t = 0; t < SimClock.TicksPerHour && sim.World.IsAlive(site) && sim.World.Has<Construction>(site); t++)
        {
            sim.Tick();
            if (!sim.World.IsAlive(site) || !sim.World.Has<Construction>(site))
                break;
            var now = sim.World.Get<Construction>(site).Delivered[0];
            if (now != last)
                deliveries++;
            last = now;
        }
        Assert.Equal(1, deliveries);
    }

    [Fact]
    public void ASpareSatchelGetsPickedUpAndAPlayersPackIsTheirOwn()
    {
        var (sim, pawn) = Setup();
        sim.TakeOff(pawn, ApparelSlot.Back);
        sim.World.Get<Position>(pawn) = Position.At(C(15, 15)); // walk away from it
        sim.TickMany(SimClock.TicksPerHour);
        Assert.False(sim.PackOf(pawn).IsNull, "they should have put a satchel back on");

        // A player's character (even one the AI is running, its player away) keeps what's in their pack.
        sim.World.Add(pawn, new PlayerCharacter { PlayerId = Guid.NewGuid() });
        sim.Store(sim.PackOf(pawn), sim.CreateItem(sim.Thing("Berries"), 5));
        AiSystem.InterruptAll(sim);
        Assert.Single(sim.StoredIn(sim.PackOf(pawn)));
    }
}

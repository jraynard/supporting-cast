using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Expeditions;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Tinkering;
using HeroesNotIncluded.Sim.Work;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// The Tinker beyond the lockwork (docs/DESIGN.md, "Tinker"): locks pinned and fitted to doors,
/// keys cut and copied; contraptions fitted together and carried on quests; party wagons mended;
/// curios appraised, bought and sent to the collector.
/// </summary>
public class TinkerTradeTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    /// <summary>A player in a round at a tinker's bench, with iron, wood and charcoal in their pack.</summary>
    private static (Simulation Sim, Entity Me, Entity Bench) AtTheBench()
    {
        var sim = Create(40, 40);
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), "Wright", C(10, 10), ageDays: 25 * 365);
        sim.Round.OpenLobby();
        sim.Round.Sit(Ana, "Wright", null, null, null);
        sim.Round.Start(sim);
        sim.GiveStartingPack(me);
        sim.World.Get<Skills>(me).Level[(int)Skill.Crafting] = 6;
        Inventory.Stow(sim, me, sim.CreateItem(sim.Thing("IronBar"), 6));
        Inventory.Stow(sim, me, sim.CreateItem(sim.Thing("Wood"), 6));
        var bench = sim.SpawnStructure(sim.Thing(Tinkers.BenchDef), C(10, 11));
        sim.RebuildDerivedIfNeeded();
        return (sim, me, bench);
    }

    /// <summary>Play a session through the predictor, as a player at the keys would, until it's over.</summary>
    private static void Play(Simulation sim, Entity me, Func<WorkSession, (bool Press, bool Held, int Axis)> keys, int maxTicks = 60 * 180) =>
        Play(sim, me, w => { var (p, h, a) = keys(w); return (p, h, a, 0); }, maxTicks);

    /// <summary>As the other, with W/S too.</summary>
    private static void Play(Simulation sim, Entity me, Func<WorkSession, (bool Press, bool Held, int Axis, int Axis2)> keys, int maxTicks = 60 * 180) =>
        PlayAll(sim, me, w => { var (p, h, a, a2) = keys(w); return (p, h, a, a2, false); }, maxTicks);

    /// <summary>As the first, with the game's other action (Space): set a pin, latch a spring.</summary>
    private static void Play(Simulation sim, Entity me, Func<WorkSession, (bool Press, bool Held, int Axis, bool Act)> keys, int maxTicks = 60 * 180) =>
        PlayAll(sim, me, w => { var (p, h, a, act) = keys(w); return (p, h, a, 0, act); }, maxTicks);

    private static void PlayAll(Simulation sim, Entity me, Func<WorkSession, (bool Press, bool Held, int Axis, int Axis2, bool Act)> keys, int maxTicks)
    {
        var predictor = new WorkPredictor();
        for (var t = 0; t < maxTicks && sim.World.Has<WorkSession>(me); t++)
        {
            sim.Tick();
            var (press, held, axis, axis2, act) = predictor.Session is { } s ? keys(s) : (false, false, 0, 0, false);
            predictor.Update(sim, me, 1, press, held, axis, c => sim.ApplyNow(c), axis2, act: act);
        }
        sim.Tick();
        Assert.False(sim.World.Has<WorkSession>(me), "the session should have ended");
    }

    private static RecipeDef Recipe(Simulation sim, string name) => sim.Defs.AllRecipes.Single(r => r.Name == name);

    private static List<Entity> Carried(Simulation sim, Entity who) =>
        sim.StoredIn(sim.PackOf(who)).Append(sim.World.Get<Carrying>(who).Item).Where(e => sim.World.IsAlive(e)).ToList();

    // ---------------- Locks and keys ----------------

    /// <summary>File each pin down to its mark (the session knows it: the maker sees it on the gauge), then set it.</summary>
    private static (bool, bool, int, bool) FileToTheMark(WorkSession w)
    {
        var s = (PinSession)w;
        return s.Length > s.Mark + 0.004f ? (false, true, 0, false) : (false, false, 0, true);
    }

    private static (Entity Lock, Entity Key) PinALock(Simulation sim, Entity me, Entity bench)
    {
        sim.ApplyNow(new CraftCommand(me, bench, Recipe(sim, "MakeLock")));
        Assert.IsType<PinSession>(sim.World.Get<WorkSession>(me));
        Play(sim, me, FileToTheMark);
        var mine = Carried(sim, me);
        var lk = mine.Single(e => Locksmithing.IsLock(sim, e));
        var key = mine.Single(e => Locksmithing.IsKey(sim, e));
        return (lk, key);
    }

    [Fact]
    public void PinningALockAtTheBenchMakesTheLockAndItsKey()
    {
        var (sim, me, bench) = AtTheBench();
        var (lk, key) = PinALock(sim, me, bench);
        var cut = sim.World.Get<LockCut>(lk);
        Assert.Equal(cut.Seed, sim.World.Get<Key>(key).Seed);
        Assert.Equal(cut.Pins, sim.World.Get<Key>(key).Pins);
        Assert.Equal(Locksmithing.PinsFor(6), cut.Pins);
        Assert.True(cut.Tightness > 0.8f, $"every pin filed to its mark: a tight lock ({cut.Tightness})");
        Assert.True(sim.World.Get<Key>(key).Fit > 0.8f);
        Assert.Equal(me, cut.Maker);
    }

    [Fact]
    public void FilingPastTheMarksMakesALooseLock()
    {
        var (sim, me, bench) = AtTheBench();
        sim.ApplyNow(new CraftCommand(me, bench, Recipe(sim, "MakeLock")));
        Play(sim, me, w =>
        {
            var s = (PinSession)w;
            return s.Length > s.Mark - 0.06f ? (false, true, 0, false) : (false, false, 0, true);
        });
        var lk = Carried(sim, me).Single(e => Locksmithing.IsLock(sim, e));
        Assert.True(sim.World.Get<LockCut>(lk).Tightness < 0.5f);
    }

    [Fact]
    public void AFittedLockLocksItsDoorForItsKeysAlone()
    {
        var (sim, me, bench) = AtTheBench();
        var (lk, key) = PinALock(sim, me, bench);
        var door = sim.SpawnStructure(sim.Thing("Door"), C(11, 10));
        sim.RebuildDerivedIfNeeded();
        Assert.Equal("Fit a lock to the wooden door", Tinkers.ActionAt(sim, me, door));

        sim.ApplyNow(new FitLockCommand(me, door, lk));
        Assert.True(Locksmithing.HasLock(sim, door));
        Assert.False(sim.World.IsAlive(lk));
        Assert.Equal("Lock the wooden door", Tinkers.ActionAt(sim, me, door));

        sim.World.Get<Key>(key).Fit = 1f; // a sweet key: no sticking in this test
        sim.ApplyNow(new TurnKeyCommand(me, door, true));
        Assert.True(sim.IsLocked(door));
        sim.RebuildDerivedIfNeeded();
        Assert.False(sim.Map.Passable(C(11, 10)), "nobody walks through a locked door");

        // Someone without the key can't turn it, and sees only a locked door.
        var stranger = sim.SpawnCreature(sim.Defs.Creature("Human"), "Pell", C(12, 10), ageDays: 25 * 365);
        Assert.False(Locksmithing.Turn(sim, stranger, door, false));
        Assert.True(sim.IsLocked(door));
        Assert.False(Perception.For(sim, stranger).Lock(door)!.Value.HaveKey);
        Assert.Null(Perception.For(sim, stranger).Lock(door)!.Value.Tightness);
        Assert.NotNull(Perception.For(sim, me).Lock(door)!.Value.Tightness);

        sim.ApplyNow(new TurnKeyCommand(me, door, false));
        Assert.False(sim.IsLocked(door));
    }

    [Fact]
    public void AFittedDoorLockCanBePickedAndATightOneLeavesLessRoom()
    {
        var (sim, me, bench) = AtTheBench();
        var (lk, _) = PinALock(sim, me, bench);
        var door = sim.SpawnStructure(sim.Thing("Door"), C(11, 10));
        sim.ApplyNow(new FitLockCommand(me, door, lk));
        sim.SetLocked(door, true);
        // Picked, as a burglar would (its maker, keyless, with picks).
        var burglar = me;
        foreach (var k in Locksmithing.Keys(sim, me))
            sim.DestroyThing(Inventory.TakeFromPerson(sim, me, k, 1));
        Inventory.Stow(sim, burglar, sim.CreateItem(sim.Thing("Lockpicks"), 1));
        PickSession.TryStartDoor(sim, burglar, door);
        var pick = (PickSession)sim.World.Get<WorkSession>(burglar);
        Assert.True(pick.Door);
        Assert.True(pick.HalfWidth < PickSession.SetHalfWidth * pick.Hands.Tolerance * 0.75f, "a tight lock gives the pick less room");
        var heights = Lockwork.PinHeights(pick.LockSeed, pick.Pins);
        Play(sim, burglar, w =>
        {
            var s = (PickSession)w;
            var off = heights[Math.Min(s.Pin, heights.Length - 1)] - s.Lift;
            var there = !s.Turning && MathF.Abs(off) <= s.HalfWidth * 0.3f;
            return (there && s.InBand, false, there || s.Turning ? 0 : Math.Sign(off), TinkerTests.Tense(s, (s.Low + s.High) * 0.5f));
        });
        Assert.False(sim.IsLocked(door));
    }

    [Fact]
    public void AKeyIsCopiedAtTheBenchFromAnIronBar()
    {
        var (sim, me, bench) = AtTheBench();
        var (_, key) = PinALock(sim, me, bench);
        var bars = Carried(sim, me).Where(e => sim.World.Get<Thing>(e).Def.Name == "IronBar").Sum(e => sim.World.Get<Stack>(e).Count);
        sim.ApplyNow(new CopyKeyCommand(me, bench, key));
        var copying = (PinSession)sim.World.Get<WorkSession>(me);
        Assert.True(copying.IsCopy);
        Play(sim, me, FileToTheMark);
        var keys = Locksmithing.Keys(sim, me);
        Assert.Equal(2, keys.Count);
        Assert.All(keys, k => Assert.Equal(sim.World.Get<Key>(key).Seed, sim.World.Get<Key>(k).Seed));
        Assert.Equal(bars - 1, Carried(sim, me).Where(e => sim.World.Get<Thing>(e).Def.Name == "IronBar").Sum(e => sim.World.Get<Stack>(e).Count));
    }

    [Fact]
    public void LocksKeysAndAPinningSurviveASave()
    {
        var (sim, me, bench) = AtTheBench();
        var (lk, key) = PinALock(sim, me, bench);
        var door = sim.SpawnStructure(sim.Thing("Door"), C(11, 10));
        sim.ApplyNow(new FitLockCommand(me, door, lk));
        sim.SetLocked(door, true);
        sim.ApplyNow(new CraftCommand(me, bench, Recipe(sim, "MakeLock")));
        sim.ApplyNow(new WorkInputBatchCommand(me, sim.World.Get<WorkSession>(me).StartTick, [new WorkInput(0, WorkInputKind.Hold, 1)], 40));
        sim.TickMany(30);
        var loaded = SaveLoadTests.RoundTrip(sim);
        Assert.Equal(sim.World.Get<LockCut>(door).Seed, loaded.World.Get<LockCut>(door).Seed);
        Assert.Equal(sim.World.Get<Key>(key).Fit, loaded.World.Get<Key>(key).Fit);
        Assert.True(loaded.IsLocked(door));
        Assert.Equal(((PinSession)sim.World.Get<WorkSession>(me)).Length, ((PinSession)loaded.World.Get<WorkSession>(me)).Length);
        SaveLoadTests.AssertSameFuture(sim, loaded, 1);
    }

    [Fact]
    public void AnAIBillPinsALockWithAKey()
    {
        var (sim, me, bench) = AtTheBench();
        var made = Crafting.MakeProducts(sim, Recipe(sim, "MakeLock"), null, null, me, C(10, 10));
        Tinkers.Made(sim, made, me);
        Assert.Contains(made, e => Locksmithing.IsLock(sim, e));
        var key = made.Single(e => Locksmithing.IsKey(sim, e));
        Assert.Equal(sim.World.Get<LockCut>(made.Single(e => Locksmithing.IsLock(sim, e))).Seed, sim.World.Get<Key>(key).Seed);
    }

    // ---------------- The shop's stock ----------------

    [Fact]
    public void TheTinkersStockIsLeftForTheTinker()
    {
        var (sim, me, _) = ExpeditionTests.Round("Healer");
        var w = sim.World;
        var shop = sim.World.Query<PresetBuilding>().Single(e => w.Get<PresetBuilding>(e).Def.Name == "TinkerShop");
        var chest = w.Query<PartOf>().Single(e => w.Get<PartOf>(e).Building == shop && w.Get<Thing>(e).Def.Name == "Chest");
        int Wood() => sim.StoredIn(chest).Where(e => w.Get<Thing>(e).Def.Name == "Wood").Sum(e => w.Get<Stack>(e).Count);
        Assert.Equal(16, Wood());
        Assert.DoesNotContain(sim.AvailableItems(), e => w.TryGet<Stored>(e, out var st) && st.In == chest);
        // Half a day of the town fetching wood and feeding fires: none of it from here.
        sim.TickMany(SimClock.TicksPerHour * 12);
        Assert.Equal(16, Wood());

        // By hand it's there for the taking.
        var wood = sim.StoredIn(chest).First(e => w.Get<Thing>(e).Def.Name == "Wood");
        var at = w.Get<Thing>(chest).Cell;
        var cell = sim.SpiralFrom(at, 2).First(c => c != at && sim.Map.Passable(c) && AvatarSystem.InReach(sim, c, chest));
        w.Get<Position>(me).Cell = cell;
        w.Get<Position>(me).Pos = Position.CenterOf(cell);
        sim.ApplyNow(new TakeFromStorageCommand(me, wood, 2));
        Assert.Equal(14, Wood());
    }

    // ---------------- The inn's storeroom ----------------

    private static Entity StoreroomDoor(Simulation sim) =>
        sim.World.Query<LockCut>().Single(e => Locksmithing.IsDoor(sim, e));

    /// <summary>
    /// The storeroom door, once the innkeeper's done with it: the round's been running a while, and
    /// they may just have let themselves through (locked again once they're clear, <see cref="RelockSystem"/>).
    /// </summary>
    private static Entity SettledStoreroomDoor(Simulation sim)
    {
        var door = StoreroomDoor(sim);
        for (var t = 0; t < SimClock.TicksPerHour && sim.World.Has<Relock>(door); t += 20)
            sim.TickMany(20);
        return door;
    }

    [Fact]
    public void TheStoreroomStartsLockedAndTheInnkeeperHoldsTheKey()
    {
        var (sim, me, _) = ExpeditionTests.Round("Healer");
        var door = SettledStoreroomDoor(sim);
        Assert.True(sim.IsLocked(door));
        Assert.Equal("Inn", sim.World.Get<PresetBuilding>(Buildings.Of(sim, door)).Def.Name);
        var keeper = sim.Creatures().Single(e => sim.World.TryGet<Trade>(e, out var t) && t.Def.Name == "Innkeeper");
        Assert.False(Locksmithing.KeyFor(sim, keeper, sim.World.Get<LockCut>(door).Seed).IsNull);
        Assert.True(Locksmithing.KeyFor(sim, me, sim.World.Get<LockCut>(door).Seed).IsNull);
        // The key's kept between jobs: still on them hours later.
        sim.TickMany(SimClock.TicksPerHour * 3);
        Assert.False(Locksmithing.KeyFor(sim, keeper, sim.World.Get<LockCut>(door).Seed).IsNull);
        // A tinker in the doorway sees a lock to pick, and no key for it.
        Assert.StartsWith("The lock on the", Tinkers.ActionAt(sim, me, door));
        Assert.False(Perception.For(sim, me).Lock(door)!.Value.HaveKey);
    }

    [Fact]
    public void AnAIInnkeeperLetsThemselvesIntoTheStoreroomForAKegAndLocksUpBehind()
    {
        var (sim, _, _) = ExpeditionTests.Round("Healer");
        NoQuests(sim);
        var w = sim.World;
        var door = StoreroomDoor(sim);
        var keeper = sim.Creatures().Single(e => w.TryGet<Trade>(e, out var t) && t.Def.Name == "Innkeeper");
        var cask = w.Query<Thing>().Single(e => w.Get<Thing>(e).Def.Building?.Cask != null);
        var props = w.Get<Thing>(cask).Def.Building!.Cask!;
        if (w.Has<CaskDrawn>(cask))
            w.Get<CaskDrawn>(cask).Drawn = props.Servings;
        else
            w.Add(cask, new CaskDrawn { Drawn = props.Servings }); // run dry
        var unlocked = false;
        for (var t = 0; t < SimClock.TicksPerHour * 6 && PourSession.ServingsLeft(sim, cask) == 0; t++)
        {
            sim.Tick();
            unlocked |= !sim.IsLocked(door);
        }
        Assert.True(PourSession.ServingsLeft(sim, cask) > 0, $"{sim.Ai.LastFailure.GetValueOrDefault(keeper)} / {w.Get<Mind>(keeper).Job?.Report}");
        Assert.True(unlocked, "through the storeroom door, with the key");
        sim.TickMany(SimClock.TicksPerHour / 4);
        Assert.True(sim.IsLocked(door), "locked up again behind them");
    }

    [Fact]
    public void NobodyWithoutTheKeyGetsAtTheKegs()
    {
        var (sim, me, _) = ExpeditionTests.Round("Healer");
        var w = sim.World;
        var door = SettledStoreroomDoor(sim);
        var keeper = sim.Creatures().Single(e => w.TryGet<Trade>(e, out var t) && t.Def.Name == "Innkeeper");
        var keg = sim.AvailableItems().First(e => w.Get<Thing>(e).Def.Name == "Keg");
        var stranger = sim.Creatures().First(e => e != keeper && e != me && w.Has<Trade>(e) && !w.Has<Adventurer>(e));
        var from = w.Get<Position>(stranger).Cell;
        Assert.False(Ai.Navigation.CanReach(sim, stranger, from, w.Get<Thing>(keg).Cell, PathEnd.OnTile));
        Assert.True(Ai.Navigation.CanReach(sim, keeper, w.Get<Position>(keeper).Cell, w.Get<Thing>(keg).Cell, PathEnd.OnTile));
        Assert.True(sim.IsLocked(door));
    }

    // ---------------- Contraptions ----------------

    /// <summary>Seat each part at its fit (the session's own copy: a test's privilege), then wind it to the catch.</summary>
    private static (bool, bool, int, bool) FitTrue(WorkSession w)
    {
        var s = (FitSession)w;
        if (s.Winding)
            return s.Catching ? (false, false, 0, true) : (s.StepsDone % 20 == 0, false, 0, false);
        // Square it up first; press only while it's square.
        var steer = MathF.Abs(s.Offset) > s.TrueHalfWidth * 0.25f ? -Math.Sign(s.Offset) : 0;
        return (false, MathF.Abs(s.Offset) <= s.TrueHalfWidth * 0.6f, steer, false);
    }

    [Fact]
    public void FittingAContraptionMakesItSoundAndForcingAPartLeavesItBinding()
    {
        var (sim, me, bench) = AtTheBench();
        sim.ApplyNow(new CraftCommand(me, bench, Recipe(sim, "MakeTappingRod")));
        Assert.IsType<FitSession>(sim.World.Get<WorkSession>(me));
        Play(sim, me, FitTrue);
        var rod = Carried(sim, me).Single(e => Contraptions.IsContraption(sim, e));
        Assert.False(sim.World.Get<Contraption>(rod).Binding);
        Assert.True(sim.World.Get<Crafted>(rod).Quality >= Quality.Plain);

        // Forced: pressed hard, never squared up, so each part jams in crooked.
        sim.ApplyNow(new CraftCommand(me, bench, Recipe(sim, "MakeSpringSnare")));
        Play(sim, me, w => ((FitSession)w).Winding ? FitTrue(w) : (false, true, 0, false));
        var snare = Carried(sim, me).Single(e => Contraptions.IsContraption(sim, e) && sim.World.Get<Thing>(e).Def.Name == "SpringSnare");
        Assert.True(sim.World.Get<Contraption>(snare).Binding, "forced home while it bound");
    }

    [Fact]
    public void ACrossbowIsFittedAndSpannedByHand()
    {
        var (sim, me, bench) = AtTheBench();
        sim.ApplyNow(new CraftCommand(me, bench, Recipe(sim, "MakeCrossbow")));
        var fit = Assert.IsType<FitSession>(sim.World.Get<WorkSession>(me));
        Assert.Equal(3, fit.Parts);
        Assert.True(fit.Winds, "the string's drawn back to the nut");
        Assert.Equal("crossbow", fit.What);
        Play(sim, me, FitTrue);
        var crossbow = Carried(sim, me).Single(e => sim.World.Get<Thing>(e).Def.Name == "Crossbow");
        Assert.False(sim.World.Has<Contraption>(crossbow), "a weapon, not road gear");
        Assert.True(sim.World.Get<Crafted>(crossbow).Quality >= Quality.Plain);
    }

    [Fact]
    public void ASeatedPartSitsHomeAMomentAndAnEarlyLatchSlips()
    {
        var (sim, me, bench) = AtTheBench();
        sim.ApplyNow(new CraftCommand(me, bench, Recipe(sim, "MakeTappingRod")));
        var sawSettle = false;
        var slipped = false;
        var early = false;
        Play(sim, me, w =>
        {
            var s = (FitSession)w;
            if (s.Settling > 0)
            {
                sawSettle = true;
                Assert.Equal(1f, s.Depth); // the part that went in stays in while it sits home
            }
            if (s.Winding && !early)
            {
                early = true;
                return (false, false, 0, true); // latch before winding at all: it won't hold
            }
            slipped |= s.Slipped > 0;
            return FitTrue(w);
        });
        Assert.True(sawSettle);
        Assert.True(slipped, "an early latch slips, and the winding goes on");
        var rod = Carried(sim, me).Single(e => Contraptions.IsContraption(sim, e));
        Assert.True(sim.World.Get<Crafted>(rod).Quality >= Quality.Plain);
    }

    [Fact]
    public void ContraptionsHelpWhereTheyFitAndJamWhenTheyBind()
    {
        var defs = LoadDefs();
        var lantern = new ContraptionRecord(defs.Thing("ClockworkLantern"), Quality.Plain, false, Entity.Null);
        var dark = defs.AllQuests.Single(q => q.Name == "KetterickDeep");
        var lit = defs.AllQuests.Single(q => q.Name == "AshfallTower");
        Assert.True(Contraptions.StrengthFactor([lantern], dark) > 1.1f);
        Assert.Equal(1f, Contraptions.StrengthFactor([lantern], lit));
        var binding = lantern with { Binding = true };
        Assert.Single(Contraptions.Jam([lantern, binding], () => 0f));
        Assert.Empty(Contraptions.Jam([lantern, binding], () => 0.99f));
    }

    [Fact]
    public void ContraptionsGoOutOnAQuestAndComeBackWithTheirBearer()
    {
        var (sim, me, party) = ExpeditionTests.Round("Innkeeper");
        var bearer = party[0];
        var name = sim.World.Get<Creature>(bearer).Name;
        var lantern = sim.CreateItem(sim.Thing("ClockworkLantern"), 1);
        sim.World.Add(lantern, new Contraption { Maker = me });
        sim.World.Add(lantern, new Crafted { Quality = Quality.Fine, Maker = me });
        Inventory.Stow(sim, bearer, lantern);
        sim.Expeditions.Quests[0].Danger = 0.5f; // easy: they'll come back
        ExpeditionTests.SendAndWait(sim, me, party);
        var back = sim.Creatures().Single(e => sim.World.Get<Creature>(e).Name == name && sim.World.Has<Adventurer>(e));
        var again = Contraptions.Carried(sim, back);
        Assert.Single(again);
        Assert.Equal(Quality.Fine, sim.World.Get<Crafted>(again[0]).Quality);
    }

    [Fact]
    public void AContraptionOnShowSellsToAnAdventurerWhoHasNone()
    {
        var (sim, me, party) = ExpeditionTests.Round("Tinker");
        var rod = sim.CreateItem(sim.Thing("TappingRod"), 1);
        sim.World.Add(rod, new Contraption { Maker = me });
        Assert.True(Goods.IsGear(sim.Thing("TappingRod")));
        Assert.True(Goods.IsUpgrade(sim, party[0], rod));
        Inventory.Stow(sim, party[0], rod);
        var another = sim.CreateItem(sim.Thing("TappingRod"), 1);
        sim.World.Add(another, new Contraption { Maker = me });
        Assert.False(Goods.IsUpgrade(sim, party[0], another), "not two that do the same");
        Assert.True(Goods.WorthFactor(sim, another) > 1f);
    }

    // ---------------- Wagons ----------------

    /// <summary>A round with a party in town and their wagon parked, with this wrong with it.</summary>
    private static (Simulation Sim, Entity Me, List<Entity> Party, Entity Wagon) WithAWagon(string trade, WagonDamage damage, bool questing = false)
    {
        var (sim, me, party) = ExpeditionTests.Round(trade);
        if (!questing)
            NoQuests(sim);
        foreach (var e in party)
            Hospitality.DirectionsSystem.KnowsTheTown(sim, e); // finding the way is InnTalkTests'
        var id = sim.World.Get<Adventurer>(party[0]).Party;
        var at = sim.World.Get<Position>(party[0]).Cell;
        var def = sim.Thing(Wagons.Def);
        var cell = sim.SpiralFrom(at, 12).First(c => Maps.Map.Distance(c, at) >= 4 && sim.CanPlaceStructure(c, def, 0)
            && sim.FootprintCells(def, c, 0).All(f => sim.Map.Passable(f) && sim.Map.Item[sim.Map.Idx(f)].IsNull && Buildings.At(sim, f).IsNull));
        var wagon = sim.SpawnStructure(def, cell);
        sim.World.Add(wagon, new Wagon { Party = id, Owner = party[0], Damage = damage, WheelSeed = 77, MendedBy = Entity.Null });
        Inventory.Stow(sim, party[0], sim.CreateItem(sim.Thing(Coin.Silver), 30)); // the means to have it mended
        sim.RebuildDerivedIfNeeded();
        return (sim, me, party, wagon);
    }

    /// <summary>Every quest closed: no broker sends the party off (and their orders with them) mid-test.</summary>
    private static void NoQuests(Simulation sim)
    {
        foreach (var q in sim.Expeditions.Quests)
            q.Status = QuestStatus.Cleared;
    }

    private static void Beside(Simulation sim, Entity who, Entity thing)
    {
        var at = sim.World.Get<Thing>(thing).Cell;
        var cell = sim.SpiralFrom(at, 2).First(c => c != at && sim.Map.Passable(c) && c.Z == at.Z && AvatarSystem.InReach(sim, c, thing));
        sim.World.Get<Position>(who).Cell = cell;
        sim.World.Get<Position>(who).Pos = Position.CenterOf(cell);
    }

    private static void BesidePerson(Simulation sim, Entity who, Entity other)
    {
        var at = sim.World.Get<Position>(other).Cell;
        var cell = sim.SpiralFrom(at, 2).First(c => c != at && sim.Map.Passable(c) && c.Z == at.Z);
        sim.World.Get<Position>(who).Cell = cell;
        sim.World.Get<Position>(who).Pos = Position.CenterOf(cell);
    }

    [Fact]
    public void TruingTheWheelMendsTheWagonAndTheOwnerPays()
    {
        var (sim, me, party, wagon) = WithAWagon("Tinker", WagonDamage.Wheel);
        for (var h = 0; h < 8 && !sim.Shops.All.Any(e => e.Kind.Kind == ServiceKind.WagonRepair); h++)
            sim.TickMany(SimClock.TicksPerHour / 4);
        var order = sim.Shops.All.Single(e => e.Kind.Kind == ServiceKind.WagonRepair);
        Assert.Equal(wagon, order.Subject);
        Assert.Contains("buckled wheel", order.Text);
        ShopSystem.Learn(order, me);
        Assert.True(sim.Shops.Accept(sim, me, order.Id));

        Beside(sim, me, wagon);
        Assert.Equal("Look the wagon over", Tinkers.ActionAt(sim, me, wagon));
        var purse = Coin.Count(sim, me);
        sim.ApplyNow(new TrueWheelCommand(me, wagon));
        var spoke = -1;
        Play(sim, me, w =>
        {
            var s = (TrueSession)w;
            if (s.AtGauge == spoke)
                return (false, false, 0);
            spoke = s.AtGauge;
            var o = s.Offsets[spoke];
            return o > s.BandWidth * 0.5f ? (true, false, 0) : o < -s.BandWidth * 0.5f ? (false, false, -1) : (false, false, 0);
        });
        Assert.False(Wagons.Damaged(sim, wagon));
        sim.TickMany(SimClock.TicksPerHour / 4);
        Assert.Equal(OrderStatus.Done, order.Status);
        Assert.True(Coin.Count(sim, me) >= purse + order.Price);
    }

    [Fact]
    public void ANewAxleIsFittedFromWood()
    {
        var (sim, me, _, wagon) = WithAWagon("Tinker", WagonDamage.Axle);
        Beside(sim, me, wagon);
        var wood = Wagons.WoodOn(sim, me);
        Inventory.Stow(sim, me, sim.CreateItem(sim.Thing("Wood"), 3));
        sim.ApplyNow(new FitAxleCommand(me, wagon));
        Assert.IsType<FitSession>(sim.World.Get<WorkSession>(me));
        Play(sim, me, FitTrue);
        Assert.False(Wagons.Damaged(sim, wagon));
        Assert.Equal(wood + 3 - Wagons.AxleWood, Wagons.WoodOn(sim, me));
    }

    [Fact]
    public void AnAITinkerMendsAWagonAndIsPaid()
    {
        var (sim, _, _, wagon) = WithAWagon("Healer", WagonDamage.Wheel | WagonDamage.Axle);
        var tinker = sim.Creatures().Single(e => sim.World.TryGet<Trade>(e, out var t) && t.Def.Name == "Tinker");
        for (var h = 0; h < 24 && Wagons.Damaged(sim, wagon); h++)
            sim.TickMany(SimClock.TicksPerHour);
        Assert.False(Wagons.Damaged(sim, wagon), $"{sim.Ai.LastFailure.GetValueOrDefault(tinker)} / {sim.World.Get<Mind>(tinker).Job?.Report}");
        sim.TickMany(SimClock.TicksPerHour / 4);
        var order = sim.Shops.All.Single(e => e.Kind.Kind == ServiceKind.WagonRepair);
        Assert.Equal(OrderStatus.Done, order.Status);
        Assert.Equal(tinker, order.Assignee);
    }

    [Fact]
    public void ABrokenWagonBringsNothingHomeFromAClearedQuest()
    {
        var (sim, me, party, _) = WithAWagon("Innkeeper", WagonDamage.Axle, questing: true);
        var id = sim.World.Get<Adventurer>(party[0]).Party;
        sim.Expeditions.Quests[0].Danger = 0.5f;
        ExpeditionTests.SendAndWait(sim, me, party);
        Assert.Equal(1, sim.Expeditions.Succeeded);
        var back = sim.Creatures().Where(e => sim.World.TryGet<Adventurer>(e, out var a) && a.Party == id).ToList();
        Assert.NotEmpty(back);
        Assert.All(back, e => Assert.True(Lockwork.LockedBoxOf(sim, e).IsNull));
        Assert.All(back, e => Assert.True(Curios.UnvaluedOf(sim, e).IsNull));
    }

    [Fact]
    public void AWagonParksOutsideAndGoesWithItsParty()
    {
        var (sim, _, party) = ExpeditionTests.Round("Tinker");
        var id = sim.World.Get<Adventurer>(party[0]).Party;
        Entity wagon = Entity.Null;
        for (ulong seed = 1; seed < 40 && wagon.IsNull; seed++)
            wagon = Wagons.Bring(sim, seed, sim.Defs.Party("IronLantern"), id, party[0], sim.World.Get<Position>(party[0]).Cell);
        Assert.False(wagon.IsNull);
        Assert.True(Buildings.At(sim, sim.World.Get<Thing>(wagon).Cell).IsNull, "parked outside");
        Assert.Equal(wagon, Wagons.Of(sim, id));
        Wagons.Leave(sim, id);
        Assert.False(sim.World.IsAlive(wagon));
    }

    // ---------------- Curios ----------------

    /// <summary>A curio of known make, on someone.</summary>
    private static Entity CurioOn(Simulation sim, Entity who, bool genuine, int worth = 40)
    {
        if (sim.World.Has<Adventurer>(who))
            Inventory.Stow(sim, who, sim.CreateItem(sim.Thing(Coin.Silver), 20)); // the means to have it looked at
        var item = sim.CreateItem(sim.Thing("ClockworkBird"), 1);
        sim.World.Add(item, new Curio { Worth = genuine ? worth : Curios.FakeWorth, Face = worth, Genuine = genuine, Seed = 0x1234_5679u });
        sim.World.Add(item, new Valued { Worth = worth, ToldBy = Entity.Null });
        Inventory.Stow(sim, who, item);
        return item;
    }

    /// <summary>Along the piece; still over every tell (the test knows where) till it's read.</summary>
    private static Func<WorkSession, (bool, bool, int)> ReadEveryTell(uint seed)
    {
        var tells = Curios.Tells(seed);
        return w =>
        {
            var s = (LoupeSession)w;
            for (var i = 0; i < tells.Length; i++)
                if (!s.Studied[i] && MathF.Abs(s.At - tells[i]) <= LoupeSession.StudyRange * 0.5f)
                    return (false, true, 0);
            return (false, false, 1);
        };
    }

    [Fact]
    public void TheLoupeNarrowsTheReckoningAndSpotsAFake()
    {
        var (sim, me, _) = AtTheBench();
        var bird = CurioOn(sim, me, genuine: true);
        var glance = Curios.Read(sim, me, bird, 0, false);
        sim.ApplyNow(new AppraiseCommand(me, bird));
        Play(sim, me, ReadEveryTell(0x1234_5679u));
        var a = sim.World.Get<Appraisal>(bird);
        Assert.Equal(me, a.By);
        Assert.True(a.High - a.Low < glance.High - glance.Low, "every tell read narrows it");
        Assert.InRange(40, a.Low - 8, a.High + 8);
        Assert.False(a.Doubts);

        var fake = CurioOn(sim, me, genuine: false);
        sim.ApplyNow(new AppraiseCommand(me, fake));
        Play(sim, me, ReadEveryTell(0x1234_5679u));
        Assert.True(sim.World.Get<Appraisal>(fake).Doubts, "the giveaway was read");
        Assert.True(sim.World.Get<Appraisal>(fake).High < 10);
    }

    [Fact]
    public void PerceptionShowsOnlyYourOwnReckoningNeverTheTruth()
    {
        var (sim, me, _) = AtTheBench();
        var bird = CurioOn(sim, me, genuine: false);
        var other = sim.SpawnCreature(sim.Defs.Creature("Human"), "Pell", C(11, 10), ageDays: 25 * 365);
        Assert.Null(Perception.For(sim, me).Curio(bird)!.Value.Low);
        Curios.Appraised(sim, me, bird, Curios.Read(sim, me, bird, 5, true));
        var mine = Perception.For(sim, me).Curio(bird)!.Value;
        Assert.NotNull(mine.Low);
        Assert.True(mine.Doubts);
        var theirs = Perception.For(sim, other).Curio(bird)!.Value;
        Assert.Null(theirs.Low);
        Assert.False(theirs.Doubts);
    }

    [Fact]
    public void AnAppraisedCurioIsHandedBackAtTheWordGivenOrBoughtAtANearPrice()
    {
        var (sim, me, party) = ExpeditionTests.Round("Tinker");
        NoQuests(sim);
        foreach (var e in party)
            Hospitality.DirectionsSystem.KnowsTheTown(sim, e);
        var owner = party[0];
        var bird = CurioOn(sim, owner, genuine: true, worth: 40);
        for (var h = 0; h < 8 && !sim.Shops.All.Any(e => e.Kind.Kind == ServiceKind.Appraise); h++)
            sim.TickMany(SimClock.TicksPerHour / 4);
        var order = sim.Shops.All.Single(e => e.Kind.Kind == ServiceKind.Appraise && e.Subject == bird);
        ShopSystem.Learn(order, me);
        Assert.True(sim.Shops.Accept(sim, me, order.Id));
        BesidePerson(sim, me, owner);
        Assert.True(sim.Shops.TryHandOver(sim, order));
        Assert.True(Inventory.OnPerson(sim, me, bird));

        // Handed back unlooked-at: refused.
        sim.ApplyNow(new GiveCommand(me, owner, bird, 1));
        Assert.True(order.IsOpen);
        Assert.True(order.Assignee.IsNull);

        Assert.True(sim.Shops.Accept(sim, me, order.Id));
        Assert.True(sim.Shops.TryHandOver(sim, order));
        Curios.Appraised(sim, me, bird, Curios.Read(sim, me, bird, 5, false));
        sim.ApplyNow(new QuoteCurioCommand(me, bird, 12)); // talking it down
        Inventory.Stow(sim, me, sim.CreateItem(sim.Thing(Coin.Silver), 30));
        var mine = Coin.Count(sim, me);
        var theirs = Coin.Count(sim, owner);
        Assert.False(Curios.Offer(sim, me, bird, 5), "well under what they now believe");
        Assert.True(Curios.Offer(sim, me, bird, 11));
        Assert.False(sim.World.Has<Entrusted>(bird));
        Assert.True(Inventory.OnPerson(sim, me, bird));
        Assert.Equal(OrderStatus.Done, order.Status);
        Assert.Equal(mine - 11 + order.Price, Coin.Count(sim, me));
        Assert.Equal(theirs + 11 - order.Price, Coin.Count(sim, owner));
    }

    [Fact]
    public void TheCollectorPaysWhatACurioWasReallyWorthTheMorningAfter()
    {
        var (sim, me, _) = ExpeditionTests.Round("Tinker");
        var crate = sim.World.Query<Thing>().Single(e => sim.World.Get<Thing>(e).Def.Name == CollectorSystem.CrateDef);
        var real = CurioOn(sim, me, genuine: true, worth: 50);
        var fake = CurioOn(sim, me, genuine: false, worth: 50);
        foreach (var c in new[] { real, fake })
            sim.Store(crate, Inventory.TakeFromPerson(sim, me, c, 1));
        // Through the next carrier's call and the one after.
        for (var h = 0; h < 50 && !(sim.StoredIn(crate).Any(e => sim.World.Get<Thing>(e).Def.Name == Coin.Silver)); h++)
            sim.TickMany(SimClock.TicksPerHour);
        Assert.False(sim.World.IsAlive(real));
        var silver = sim.StoredIn(crate).Where(e => sim.World.Get<Thing>(e).Def.Name == Coin.Silver).Sum(e => sim.World.Get<Stack>(e).Count);
        Assert.Equal((int)MathF.Round(50 * Curios.CollectorShare) + (int)MathF.Round(Curios.FakeWorth * Curios.CollectorShare), silver);
    }

    [Fact]
    public void AnAITinkerAppraisesACurioAndIsPaid()
    {
        var (sim, _, party) = ExpeditionTests.Round("Healer");
        NoQuests(sim);
        foreach (var e in party)
            Hospitality.DirectionsSystem.KnowsTheTown(sim, e);
        var tinker = sim.Creatures().Single(e => sim.World.TryGet<Trade>(e, out var t) && t.Def.Name == "Tinker");
        var bird = CurioOn(sim, party[0], genuine: true);
        for (var h = 0; h < 24 && !sim.Shops.All.Any(e => e.Kind.Kind == ServiceKind.Appraise && !e.IsOpen); h++)
            sim.TickMany(SimClock.TicksPerHour);
        var order = sim.Shops.All.Single(e => e.Kind.Kind == ServiceKind.Appraise);
        Assert.True(order.Status == OrderStatus.Done, $"{order.Status}: {sim.Ai.LastFailure.GetValueOrDefault(tinker)} / {sim.World.Get<Mind>(tinker).Job?.Report}");
        Assert.Equal(tinker, sim.World.Get<Appraisal>(bird).By);
        Assert.Equal(tinker, sim.World.Get<Valued>(bird).ToldBy);
    }

    [Fact]
    public void ContraptionsWagonsAndCuriosSurviveASave()
    {
        var (sim, me, party, wagon) = WithAWagon("Tinker", WagonDamage.Wheel);
        var bird = CurioOn(sim, party[1], genuine: false);
        Curios.Appraised(sim, me, bird, Curios.Read(sim, me, bird, 2, false));
        var rod = sim.CreateItem(sim.Thing("TappingRod"), 1);
        sim.World.Add(rod, new Contraption { Binding = true, Maker = me });
        Inventory.Stow(sim, party[0], rod);
        var loaded = SaveLoadTests.RoundTrip(sim);
        Assert.Equal(WagonDamage.Wheel, loaded.World.Get<Wagon>(wagon).Damage);
        Assert.Equal(sim.World.Get<Curio>(bird), loaded.World.Get<Curio>(bird));
        Assert.Equal(sim.World.Get<Appraisal>(bird), loaded.World.Get<Appraisal>(bird));
        Assert.True(loaded.World.Get<Contraption>(rod).Binding);
        SaveLoadTests.AssertSameFuture(sim, loaded, 1);
    }

    [Fact]
    public void APinIsSetWithSpaceNotD()
    {
        var (sim, me, bench) = AtTheBench();
        sim.ApplyNow(new CraftCommand(me, bench, Recipe(sim, "MakeLock")));
        var s = Assert.IsType<PinSession>(sim.World.Get<WorkSession>(me));
        s.Advance([new WorkInput(s.StepsDone, WorkInputKind.Axis, 1)], s.StepsDone + 5);
        Assert.Equal(0, s.Pin);
        s.Advance([new WorkInput(s.StepsDone, WorkInputKind.Act)], s.StepsDone + 2);
        Assert.Equal(1, s.Pin);
    }
}

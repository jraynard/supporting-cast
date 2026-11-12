using SupportingCast.Sim.Bots;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Expeditions;
using SupportingCast.Sim.Locks;
using SupportingCast.Sim.Maps;
using SupportingCast.Sim.Round;
using SupportingCast.Sim.Scenarios;
using SupportingCast.Sim.Shops;
using SupportingCast.Sim.Systems;
using SupportingCast.Sim.Work;
using static SupportingCast.Sim.Tests.TestWorld;

namespace SupportingCast.Sim.Tests;

/// <summary>The Carpenter's lockwork: strongboxes from quests, feeling for traps, picking locks.</summary>
public class CarpenterTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    /// <summary>A player in a round with lockpicks and a strongbox in their pack.</summary>
    private static (Simulation Sim, Entity Me, Entity Box) AtTheBench(bool trapped, ulong seed = 7)
    {
        var sim = Create(40, 40);
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), "Wright", C(10, 10), ageDays: 25 * 365);
        sim.Round.OpenLobby();
        sim.Round.Sit(Ana, "Wright", null, null, null);
        sim.Round.Start(sim);
        sim.GiveStartingPack(me);
        Inventory.Stow(sim, me, sim.CreateItem(sim.Thing("Lockpicks"), 1));
        var box = Lockwork.Make(sim, new SimRandom(seed), 3f, 30, "the Gloam Wood");
        ref var b = ref sim.World.Get<Strongbox>(box);
        b.Armed = trapped;
        b.Trap = trapped ? TrapKind.Blade : TrapKind.None;
        b.TrapAt = 0.5f;
        Inventory.Stow(sim, me, box);
        return (sim, me, box);
    }

    private static int Wounds(Simulation sim, Entity e) => sim.World.Get<Health>(e).Injuries.Count;

    /// <summary>Play a session through the predictor, as a player at the keys would, until it's over.</summary>
    private static void Play(Simulation sim, Entity me, Func<WorkSession, (bool Press, bool Held, int Axis)> keys, int maxTicks = 60 * 120)
    {
        var predictor = new WorkPredictor();
        for (var t = 0; t < maxTicks && sim.World.Has<WorkSession>(me); t++)
        {
            sim.Tick();
            var (press, held, axis) = predictor.Session is { } s ? keys(s) : (false, false, 0);
            predictor.Update(sim, me, 1, press, held, axis, c => sim.ApplyNow(c));
        }
        sim.Tick();
        Assert.False(sim.World.Has<WorkSession>(me), "the session should have ended");
    }

    /// <summary>A careful hand that knows where the trigger is: light to it, stop, ease the spring out, on to the hinge.</summary>
    private static (bool, bool, int) Careful(WorkSession w)
    {
        var s = (ProbeSession)w;
        if (!s.Disarmed && MathF.Abs(s.Probe - s.TrapAt) <= ProbeSession.ProbePerStep)
            return (false, s.Pressure < (s.Low + s.High) * 0.5f, 0);
        return s.Pressure > 0.1f ? (false, false, 0) : (false, false, 1);
    }

    [Fact]
    public void ACarefulProbeFindsTheTrapAndDrawsIt()
    {
        var (sim, me, box) = AtTheBench(trapped: true);
        sim.ApplyNow(new ProbeBoxCommand(me, box));
        Assert.IsType<ProbeSession>(sim.World.Get<WorkSession>(me));
        Play(sim, me, Careful);

        var b = sim.World.Get<Strongbox>(box);
        Assert.False(b.Armed);
        Assert.Equal(me, b.SearchedBy);
        Assert.Equal(0, Wounds(sim, me));
        Assert.Equal("a spring blade", Perception.For(sim, me).Strongbox(box)!.Value.SpentTrap);
    }

    [Fact]
    public void DraggingTheProbeAcrossATriggerWithWeightOnItSetsItOff()
    {
        var (sim, me, box) = AtTheBench(trapped: true);
        sim.ApplyNow(new ProbeBoxCommand(me, box));
        Play(sim, me, _ => (false, true, 1));

        Assert.False(sim.World.Get<Strongbox>(box).Armed);
        var wound = Assert.Single(sim.World.Get<Health>(me).Injuries);
        Assert.Equal(DamageType.Cut, wound.Type);
        Assert.Contains("Arm", sim.World.Get<Health>(me).Body.Parts[wound.Part].Name);
    }

    [Fact]
    public void AnUntrappedBoxFeelsTheSameAllTheWayToTheHinge()
    {
        var (sim, me, box) = AtTheBench(trapped: false);
        sim.ApplyNow(new ProbeBoxCommand(me, box));
        var feel = 0f;
        Play(sim, me, w =>
        {
            feel = Math.Max(feel, ((ProbeSession)w).Feel);
            return (false, false, 1);
        });
        Assert.True(feel < 0.6f, $"a clean seam peaked at {feel}");
        Assert.Equal(me, sim.World.Get<Strongbox>(box).SearchedBy);
        Assert.True(sim.World.Get<Strongbox>(box).Locked);
    }

    /// <summary>Set each pin where it sets, knowing the lock.</summary>
    private static Func<WorkSession, (bool, bool, int)> Picker(Simulation sim, Entity box)
    {
        var b = sim.World.Get<Strongbox>(box);
        var heights = Lockwork.PinHeights(b.LockSeed, b.Pins);
        return w =>
        {
            var s = (PickSession)w;
            var off = heights[Math.Min(s.Pin, heights.Length - 1)] - s.Lift;
            return MathF.Abs(off) <= s.HalfWidth * 0.3f ? (true, false, 0) : (false, false, Math.Sign(off));
        };
    }

    [Fact]
    public void PickingALockWithTheTrapStillArmedSetsItOffAsTheLidComesUp()
    {
        var (sim, me, box) = AtTheBench(trapped: true);
        sim.ApplyNow(new PickLockCommand(me, box));
        Play(sim, me, Picker(sim, box));

        var b = sim.World.Get<Strongbox>(box);
        Assert.False(b.Locked);
        Assert.False(b.Armed);
        Assert.Equal(1, Wounds(sim, me));
        Assert.Equal(30, Perception.For(sim, me).Strongbox(box)!.Value.Silver);
    }

    [Fact]
    public void ASearchedAndDrawnBoxOpensClean()
    {
        var (sim, me, box) = AtTheBench(trapped: true);
        sim.ApplyNow(new ProbeBoxCommand(me, box));
        Play(sim, me, Careful);
        sim.ApplyNow(new PickLockCommand(me, box));
        Play(sim, me, Picker(sim, box));
        Assert.False(sim.World.Get<Strongbox>(box).Locked);
        Assert.Equal(0, Wounds(sim, me));
    }

    [Fact]
    public void SlipsDropPinsAndWearThePicksUntilOneSnaps()
    {
        var (sim, me, box) = AtTheBench(trapped: false);
        var picks = Lockwork.Picks(sim, me);
        var hp = sim.World.Get<Thing>(picks).Hp;
        sim.ApplyNow(new PickLockCommand(me, box));
        Play(sim, me, w => (w.StepsDone % 20 == 0, false, 0)); // jabbing at the bottom of the lift

        Assert.True(sim.World.Get<Strongbox>(box).Locked);
        Assert.True(sim.World.Get<Thing>(picks).Hp < hp);
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("snaps"));
    }

    [Fact]
    public void LockworkSessionsSurviveASave()
    {
        var (sim, me, box) = AtTheBench(trapped: true);
        sim.ApplyNow(new ProbeBoxCommand(me, box));
        sim.ApplyNow(new WorkInputBatchCommand(me, sim.World.Get<WorkSession>(me).StartTick, [new WorkInput(0, WorkInputKind.Axis, 1)], 40));
        sim.TickMany(30);
        var loaded = SaveLoadTests.RoundTrip(sim);
        var a = (ProbeSession)sim.World.Get<WorkSession>(me);
        var b = (ProbeSession)loaded.World.Get<WorkSession>(me);
        Assert.Equal(a.Probe, b.Probe);
        Assert.Equal(a.TrapAt, b.TrapAt);
        SaveLoadTests.AssertSameFuture(sim, loaded, 1);

        var (sim2, me2, box2) = AtTheBench(trapped: false);
        sim2.ApplyNow(new PickLockCommand(me2, box2));
        sim2.TickMany(10);
        var loaded2 = SaveLoadTests.RoundTrip(sim2);
        Assert.Equal(((PickSession)sim2.World.Get<WorkSession>(me2)).Pins, ((PickSession)loaded2.World.Get<WorkSession>(me2)).Pins);
        SaveLoadTests.AssertSameFuture(sim2, loaded2, 1);
    }

    // ---------------- Strongboxes in the round ----------------

    [Fact]
    public void AClearedQuestBringsBackALockedStrongboxAndTheyAskForItOpened()
    {
        var (sim, me, party) = ExpeditionTests.Round("Innkeeper");
        var id = sim.World.Get<Adventurer>(party[0]).Party;
        var q = sim.Expeditions.Quests[0];
        q.Danger = 0.5f; // easy: they'll clear it
        ExpeditionTests.SendAndWait(sim, me, party);
        Assert.Equal(1, sim.Expeditions.Succeeded);

        var back = sim.Creatures().Where(e => sim.World.TryGet<Adventurer>(e, out var a) && a.Party == id).ToList();
        var carrier = back.Single(e => !Lockwork.LockedBoxOf(sim, e).IsNull);
        var box = Lockwork.LockedBoxOf(sim, carrier);
        Assert.Equal(ExpeditionSystem.BoxShare(q), sim.World.Get<Strongbox>(box).Silver);
        Assert.InRange(sim.World.Get<Strongbox>(box).Pins, Lockwork.MinPins, Lockwork.MaxPins);

        sim.TickMany(SimClock.TicksPerHour / 4);
        var order = sim.Shops.All.Single(e => e.Kind.Kind == ServiceKind.Unlock && e.Poster == carrier);
        Assert.Equal(box, order.Subject);
        var carpenter = sim.Creatures().Single(e => sim.World.TryGet<Trade>(e, out var t) && t.Def.Name == "Carpenter");
        Assert.Contains(carpenter, order.KnownBy); // word of work at the woodshop reaches the carpenter
    }

    /// <summary>A round with the player in <paramref name="trade"/>, and an adventurer holding a strongbox.</summary>
    private static (Simulation Sim, Entity Owner, Entity Box) TownWithABox(string trade, bool trapped)
    {
        var sim = VillageRound.NewSimulation(LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default, []));
        sim.ApplyNow(new SetTradePreferencesCommand(Ana, [sim.Defs.Trade(trade)], []));
        sim.ApplyNow(new StartRoundCommand());
        var party = sim.Parties.Send(sim, sim.Defs.Party("GildedSpurs"));
        foreach (var e in party)
            Hospitality.DirectionsSystem.KnowsTheTown(sim, e); // finding the way is InnTalkTests'
        var owner = party[0];
        var box = Lockwork.Make(sim, new SimRandom(5), 3f, 25, "the Rimewater Caves");
        ref var b = ref sim.World.Get<Strongbox>(box);
        b.Armed = trapped;
        b.Trap = trapped ? TrapKind.Needle : TrapKind.None;
        Inventory.Stow(sim, owner, box);
        // Back from a quest with their share: enough to pay for the box on top of whatever else they've ordered.
        Inventory.Stow(sim, owner, sim.CreateItem(sim.Defs.Thing(Coin.Silver), 40));
        return (sim, owner, box);
    }

    private static Order UnlockOf(Simulation sim, Entity owner) =>
        sim.Shops.All.Single(e => e.Poster == owner && e.Kind.Kind == ServiceKind.Unlock);

    [Fact]
    public void AnAICarpenterOpensAStrongboxAndIsPaid()
    {
        var (sim, owner, box) = TownWithABox("Healer", trapped: false);
        var carpenter = sim.Creatures().Single(e => sim.World.TryGet<Trade>(e, out var t) && t.Def.Name == "Carpenter");
        Assert.False(sim.World.Has<Avatar>(carpenter));
        var purse = Coin.Count(sim, carpenter);
        var theirs = Coin.Count(sim, owner);

        for (var h = 0; h < 12 && sim.Shops.All.All(e => e.Kind.Kind != ServiceKind.Unlock || e.IsOpen); h++)
            sim.TickMany(SimClock.TicksPerHour);

        var order = UnlockOf(sim, owner);
        Assert.True(order.Status == OrderStatus.Done, $"{order.Status}, carpenter: {sim.Ai.LastFailure.GetValueOrDefault(carpenter)} / {sim.World.Get<Mind>(carpenter).Job?.Report}\n"
            + string.Join("\n", sim.Speech.Kept.TakeLast(12).Select(u => $"{u.SpeakerName}: {u.Text}")));
        Assert.Equal(carpenter, order.Assignee);
        Assert.False(sim.World.Get<Strongbox>(box).Locked);
        Assert.Equal(0, sim.World.Get<Strongbox>(box).Silver);
        Assert.True(Inventory.OnPerson(sim, owner, box));
        Assert.Equal(purse + order.Price, Coin.Count(sim, carpenter));
        Assert.True(Coin.Count(sim, owner) >= theirs + 25 - order.Price - 10, "the silver inside went to its owner"); // less what they spent on drink meanwhile
    }

    [Fact]
    public void ABoxHandedBackLockedIsRefusedAndTheJobReopens()
    {
        var (sim, owner, box) = TownWithABox("Carpenter", trapped: false);
        var me = Players.CharacterOf(sim, Ana);
        sim.TickMany(SimClock.TicksPerHour / 6);
        var order = UnlockOf(sim, owner);
        sim.ApplyNow(new TakeOrderCommand(me, order.Id));
        var at = sim.World.Get<Position>(owner).Cell;
        sim.World.Get<Position>(me) = Position.At(sim.SpiralFrom(at, 2).First(c => c != at && sim.Map.Passable(c) && c.Z == at.Z));
        Assert.True(sim.Shops.TryHandOver(sim, order));
        Assert.True(Inventory.OnPerson(sim, me, box));
        Assert.True(sim.World.Has<Entrusted>(box));

        sim.ApplyNow(new GiveCommand(me, owner, box, 1));
        Assert.True(order.IsOpen);
        Assert.True(order.Assignee.IsNull);
        Assert.True(Inventory.OnPerson(sim, owner, box));
    }

    [Fact]
    public void ABotCarpenterFeelsForTheTrapPicksTheLockAndIsPaid()
    {
        var (sim, owner, box) = TownWithABox("Carpenter", trapped: true);
        var bot = new BotPlayer(() => sim, Ana, c => sim.ApplyNow(c), 11);
        var me = Players.CharacterOf(sim, Ana);
        var purse = Coin.Count(sim, me);
        for (var t = 0; t < SimClock.TicksPerHour * 8 && sim.Shops.All.All(e => e.Kind.Kind != ServiceKind.Unlock || e.IsOpen); t++)
        {
            sim.Tick();
            bot.Tick();
        }
        var order = UnlockOf(sim, owner);
        Assert.True(order.Status == OrderStatus.Done, $"{order.Status}: bot {bot.Doing}\n" + string.Join("\n", sim.Messages.Messages.TakeLast(10).Select(m => m.Text)));
        Assert.True(Coin.Count(sim, me) >= purse + order.Price);
        Assert.Equal(0, Wounds(sim, me)); // it found the needle before the lid came up
    }
}

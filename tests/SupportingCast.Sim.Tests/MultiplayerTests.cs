using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Maps;
using SupportingCast.Sim.Saving;
using SupportingCast.Sim.Scenarios;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Tests;

/// <summary>
/// Co-op: players' characters, their own eggs and dragons, who may command what, and the join
/// barrier (interrupt everyone, snapshot) that lets a newcomer step in without a desync.
/// </summary>
public class MultiplayerTests
{
    private static readonly Guid Arin = new("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
    private static readonly Guid Bea = new("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");

    private static Simulation Warm()
    {
        var sim = TestWorld.Create();
        sim.Climate.BaseTemperature = 30;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 30f);
        sim.SpawnStructure(sim.Thing("Wagon"), C(20, 20));
        return sim;
    }

    private static Entity Create(Simulation sim, Guid player, string name)
    {
        sim.ApplyNow(new CreateCharacterCommand(player, name, new Appearance { Hair = HairStyle.Long }, ["Kind"]));
        return Players.CharacterOf(sim, player);
    }

    [Fact]
    public void EachPlayerArrivesWithTheirOwnEgg()
    {
        var sim = Warm();
        var a = Create(sim, Arin, "Arin");
        var b = Create(sim, Bea, "Bea");
        var w = sim.World;

        Assert.False(a.IsNull);
        Assert.False(b.IsNull);
        Assert.NotEqual(a, b);
        foreach (var (me, name) in new[] { (a, "Arin"), (b, "Bea") })
        {
            Assert.True(w.Has<Avatar>(me));
            Assert.True(w.Has<Keeper>(me));
            Assert.Equal(name, w.Get<Creature>(me).Name);
            var egg = w.Get<Carrying>(me).Item;
            Assert.True(w.Has<Egg>(egg));
            Assert.Equal(me, w.Get<Egg>(egg).Keeper);
            Assert.True(Map.Distance(w.Get<Position>(me).Cell, C(20, 20)) <= 12, "turns up by the wagon");
        }
        // Creating again does nothing: one character per player.
        sim.ApplyNow(new CreateCharacterCommand(Arin, "Arin 2", default, []));
        Assert.Equal(2, w.Count<PlayerCharacter>());
    }

    [Fact]
    public void EggsHatchToTheirOwnKeepers()
    {
        var sim = Warm();
        var a = Create(sim, Arin, "Arin");
        var b = Create(sim, Bea, "Bea");
        var w = sim.World;
        // Put both eggs down side by side, nearer Bea, so "nearest villager" would pick wrong.
        foreach (var me in new[] { a, b })
            sim.ApplyNow(new AvatarDropCommand(me, w.Get<Position>(me).Cell));
        sim.Tick();
        foreach (var egg in w.Query<Egg>())
            w.Get<Egg>(egg).Progress = 0.999f;

        sim.TickMany(SimClock.TicksPerHour);

        Assert.Empty(w.Query<Egg>());
        Assert.True(w.IsAlive(w.Get<Keeper>(a).Dragon));
        Assert.True(w.IsAlive(w.Get<Keeper>(b).Dragon));
        Assert.NotEqual(w.Get<Keeper>(a).Dragon, w.Get<Keeper>(b).Dragon);
        Assert.Equal(a, w.Get<Dragon>(w.Get<Keeper>(a).Dragon).Keeper);
    }

    [Fact]
    public void LeavingHandsTheCharacterToTheVillageAndRejoiningTakesItBack()
    {
        var sim = Warm();
        var a = Create(sim, Arin, "Arin");
        var w = sim.World;
        sim.SetSkills(a);
        Players.Leave(sim, Arin);

        Assert.False(w.Has<Avatar>(a));
        Assert.False(w.Get<Carrying>(a).HasItem); // the egg is set down for the village to tend
        Assert.Single(w.Query<Egg>());
        // A villager now: give them something to do and they do it.
        sim.SpawnItem(sim.Thing("Wood"), 10, C(8, 8));
        sim.Map.SetZone(sim.Map.Idx(C(15, 8)), 1);
        sim.TickMany(SimClock.TicksPerHour);
        Assert.True(sim.Map.Item[sim.Map.Idx(C(15, 8))] is { IsNull: false } || w.Get<Mind>(a).Job != null || w.Get<Carrying>(a).HasItem,
            "works as a villager while away");

        Assert.True(Players.Join(sim, Arin));
        Assert.Equal(a, Players.CharacterOf(sim, Arin));
        Assert.True(w.Has<Avatar>(a));
        Assert.False(Players.Join(sim, Bea), "a new player has no character yet");
    }

    [Fact]
    public void HostAdoptsTheAvatarOfASinglePlayerSave()
    {
        var sim = Warm();
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), "Solo", C(5, 5), ageDays: 9000);
        sim.World.Add(me, new Avatar());
        Assert.True(Players.Join(sim, Arin, adoptLooseAvatar: true));
        Assert.Equal(me, Players.CharacterOf(sim, Arin));
        Assert.False(Players.Join(sim, Bea, adoptLooseAvatar: true), "only one loose avatar to adopt");
    }

    [Fact]
    public void PlayersSteerOnlyTheirOwnCharacterAndDragon()
    {
        var sim = Warm();
        var a = Create(sim, Arin, "Arin");
        var b = Create(sim, Bea, "Bea");
        var villager = sim.SpawnCreature(sim.Defs.Creature("Human"), "Cal", C(3, 3), ageDays: 9000);
        var dragon = sim.SpawnCreature(sim.Defs.Creature("Dragon"), "Ember", C(4, 4), ageDays: 20);
        EggSystem.AssignKeeper(sim, dragon, b);

        Assert.Null(CommandRules.Refusal(sim, new AvatarMoveCommand(a, new int2(1, 0)), Arin, false));
        Assert.NotNull(CommandRules.Refusal(sim, new AvatarMoveCommand(b, new int2(1, 0)), Arin, false));
        Assert.NotNull(CommandRules.Refusal(sim, new BreathOrderCommand(dragon, C(1, 1)), Arin, false));
        Assert.Null(CommandRules.Refusal(sim, new BreathOrderCommand(dragon, C(1, 1)), Bea, false));
        // Tending and calling a dragon: only its own player's (docs/plans/dragon-interactions.md).
        Assert.NotNull(CommandRules.Refusal(sim, new DragonCareCommand(a, dragon, sim.Defs.DragonInteraction("Pet")), Arin, false));
        Assert.Null(CommandRules.Refusal(sim, new DragonCareCommand(b, dragon, sim.Defs.DragonInteraction("Pet")), Bea, false));
        Assert.NotNull(CommandRules.Refusal(sim, new CallDragonCommand(a, dragon), Arin, false));
        Assert.Null(CommandRules.Refusal(sim, new DraftCommand(villager, true), Arin, false));
        Assert.NotNull(CommandRules.Refusal(sim, new WorkPriorityCommand(b, WorkType.Haul, 1), Arin, false));
        Assert.Null(CommandRules.Refusal(sim, new AreaOrderCommand(AreaTool.Chop, new int2(0, 0), new int2(5, 5), 1, 1), Bea, false));
        Assert.NotNull(CommandRules.Refusal(sim, new CreateCharacterCommand(Bea, "x", default, []), Arin, false));
        Assert.NotNull(CommandRules.Refusal(sim, new DevHatchCommand(), Bea, false));
        Assert.Null(CommandRules.Refusal(sim, new DevHatchCommand(), Arin, true));
    }

    [Fact]
    public void CharacterSurvivesASave()
    {
        var sim = Warm();
        var a = Create(sim, Arin, "Arin");
        var loaded = SaveGame.Read(SaveGame.Write(sim), sim.Defs).Sim;
        var again = Players.CharacterOf(loaded, Arin);
        Assert.Equal(a, again);
        var egg = loaded.World.Get<Carrying>(again).Item;
        Assert.Equal(again, loaded.World.Get<Egg>(egg).Keeper);
    }

    /// <summary>
    /// The lockstep join: at a barrier everyone runs InterruptAll and the host snapshots. The
    /// newcomer's copy and the running ones must then agree for hours of identical commands.
    /// </summary>
    [Fact]
    public void JoinBarrierKeepsPeersInStep()
    {
        var sim = Simulation.CreateStandard(TestWorld.LoadDefs(), 96, 96, 42, MapGenerator.RecommendedDepth);
        var center = MapGenerator.Generate(sim);
        SliceDemo.ApplyVillageDefaults(sim, center);
        SliceDemo.ApplyPlayerOrders(sim, center);
        Assert.True(Players.Join(sim, Arin, adoptLooseAvatar: true) || Create(sim, Arin, "Arin") is { IsNull: false });
        var peer = SaveGame.Read(SaveGame.Write(sim), sim.Defs).Sim;
        AiSystem.InterruptAll(sim);
        sim.TickMany(SimClock.HoursToTicks(3)); // jobs under way

        // Barrier: everyone interrupts; a newcomer loads the host's snapshot.
        peer.TickMany(SimClock.HoursToTicks(3)); // the peer already in the game ran the same hours
        AiSystem.InterruptAll(sim);
        AiSystem.InterruptAll(peer);
        var newcomer = SaveGame.Read(SaveGame.Write(sim), sim.Defs).Sim;
        Assert.Equal(sim.StateHash(), newcomer.StateHash());
        Assert.Equal(sim.StateHash(), peer.StateHash());

        // The join itself, then the same commands everywhere.
        ISimCommand[] commands =
        [
            new CreateCharacterCommand(Bea, "Bea", new Appearance(), ["Kind"]),
            new AreaOrderCommand(AreaTool.Chop, new int2(center.X - 20, center.Y - 20), new int2(center.X + 20, center.Y + 20), center.Z, MapGenerator.RecommendedDepth - 1),
        ];
        var all = new[] { sim, peer, newcomer };
        foreach (var s in all)
            foreach (var c in commands)
                s.ApplyNow(c);
        var bea = Players.CharacterOf(sim, Bea);
        for (var h = 1; h <= 4; h++)
        {
            foreach (var s in all)
            {
                s.ApplyNow(new AvatarMoveCommand(bea, new int2(h % 2 == 0 ? 1 : -1, 0)));
                s.TickMany(SimClock.TicksPerHour);
            }
            Assert.True(sim.StateHash() == newcomer.StateHash(), $"the newcomer diverged within hour {h}");
            Assert.True(sim.StateHash() == peer.StateHash(), $"the running peer diverged within hour {h}");
        }
    }
}

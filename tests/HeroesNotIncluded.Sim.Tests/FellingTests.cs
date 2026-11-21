using HeroesNotIncluded.Sim.Carpentry;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// The carpenter in the woods (docs/DESIGN.md, "Carpenter"): a tree felled where it's aimed, the
/// fall that hurts whoever's under it, the trunk bucked into logs (by two on a great one), logs that
/// are heavy to carry, and firewood split at the block for the town's fires.
/// </summary>
public class FellingTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    /// <summary>A carpenter in a round in open ground, axe and saw in the pack, a tree beside them.</summary>
    private static (Simulation Sim, Entity Me, Entity Tree) ByATree(string tree = "Pine")
    {
        var sim = Create(40, 40);
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), "Wright", C(10, 10), ageDays: 25 * 365);
        sim.Round.OpenLobby();
        sim.Round.Sit(Ana, "Wright", null, null);
        sim.Round.Start(sim);
        if (!sim.World.Has<Avatar>(me))
            sim.World.Add(me, new Avatar());
        if (sim.World.Has<Trade>(me))
            sim.World.Get<Trade>(me).Def = sim.Defs.Trade("Carpenter");
        else
            sim.World.Add(me, new Trade { Def = sim.Defs.Trade("Carpenter") });
        sim.GiveStartingPack(me);
        sim.World.Get<Skills>(me).Level[(int)Skill.Crafting] = 6;
        Inventory.Stow(sim, me, sim.CreateItem(sim.Thing("Axe"), 1));
        Inventory.Stow(sim, me, sim.CreateItem(sim.Thing("Saw"), 1));
        var t = sim.SpawnStructure(sim.Thing(tree), C(10, 11));
        sim.RebuildDerivedIfNeeded();
        return (sim, me, t);
    }

    /// <summary>Play a session through the predictor, as a player at the keys would, until it's over.</summary>
    private static void Play(Simulation sim, Entity me, Func<WorkSession, (bool Held, int Axis, int Axis2, bool Act)> keys, int maxTicks = 60 * 240)
    {
        var predictor = new WorkPredictor();
        for (var t = 0; t < maxTicks && sim.World.Has<WorkSession>(me); t++)
        {
            sim.Tick();
            var (held, axis, axis2, act) = predictor.Session is { } s ? keys(s) : (false, 0, 0, false);
            predictor.Update(sim, me, 1, false, held, axis, c => sim.ApplyNow(c), axis2, act: act);
        }
        sim.Tick();
    }

    /// <summary>A woodsman's fell: turn to the aim, notch to the mark, round the back, and thin the hinge without cutting through it.</summary>
    private static Func<WorkSession, (bool, int, int, bool)> FellWell(float aim, float notch = FellSession.NotchMark) => w =>
    {
        var s = (FellSession)w;
        if (!s.AimSet && MathF.Abs(FellSession.Wrap180(aim - s.Aim)) > FellSession.TurnPerStep)
            return (false, Math.Sign(FellSession.Wrap180(aim - s.Aim)), 0, false);
        var per = FellSession.DeepestBlow / s.Girth;
        if (!s.Backing)
        {
            if (s.Notch >= notch - 0.02f && s.Raised <= 0f)
                return (false, 0, 0, true); // deep enough: round the back
            var want = Math.Max(0.02f, notch - s.Notch);
            return (s.Raised * per < Math.Min(per, want), 0, 0, false);
        }
        // Leave a hinge about half the width it falls at: blows no deeper than the hinge less that.
        var wantBack = Math.Max(0.015f, s.Hinge - FellSession.HingeFalls * 0.5f);
        return (s.Raised * per < Math.Min(per * 0.7f, wantBack), 0, 0, false);
    };

    private static float Off(float a, float b) => MathF.Abs(FellSession.Wrap180(a - b));

    private static float Fell(Simulation sim, Entity me, Entity tree, float aim, float notch = FellSession.NotchMark)
    {
        Assert.Equal(AvatarActionKind.Fell, AvatarSystem.ActionAt(sim, me, sim.World.Get<Thing>(tree).Cell).Kind);
        Felling.StartFelling(sim, me, tree);
        Assert.IsType<FellSession>(sim.World.Get<WorkSession>(me));
        var dir = 0f;
        var predictor = new WorkPredictor();
        var keys = FellWell(aim, notch);
        for (var t = 0; t < 60 * 120 && sim.World.Has<WorkSession>(me); t++)
        {
            sim.Tick();
            if (sim.World.TryGet<WorkSession>(me, out var real) && real is FellSession { Done: true } f)
                dir = f.FellDir;
            var (held, axis, axis2, act) = predictor.Session is { } s ? keys(s) : (false, 0, 0, false);
            predictor.Update(sim, me, 1, false, held, axis, c => sim.ApplyNow(c), axis2, act: act);
        }
        sim.Tick();
        Assert.False(sim.World.Has<WorkSession>(me), "the fell should be over");
        Assert.True(sim.World.Has<Falling>(tree), "the tree should be coming down");
        return sim.World.Get<Falling>(tree).Dir;
    }

    // ---------------- Felling ----------------

    [Fact]
    public void ACarpenterFellsByHandAndACookDoesnt()
    {
        var (sim, me, tree) = ByATree();
        Assert.Equal(AvatarActionKind.Fell, AvatarSystem.ActionAt(sim, me, C(10, 11)).Kind);
        sim.World.Get<Trade>(me).Def = sim.Defs.Trade("Cook");
        Assert.NotEqual(AvatarActionKind.Fell, AvatarSystem.ActionAt(sim, me, C(10, 11)).Kind);
        Assert.NotEqual(AvatarActionKind.Harvest, AvatarSystem.ActionAt(sim, me, C(10, 11)).Kind);
        _ = tree;
    }

    [Fact]
    public void TheCarpenterMayFellButNotForage()
    {
        var (sim, me, _) = ByATree();
        Assert.True(RoundSystem.MayFell(sim, me));
        Assert.False(RoundSystem.MayGather(sim, me));
        var bush = sim.SpawnStructure(sim.Thing("BerryBush"), C(11, 10));
        sim.RebuildDerivedIfNeeded();
        Assert.NotEqual(AvatarActionKind.Harvest, AvatarSystem.ActionAt(sim, me, sim.World.Get<Thing>(bush).Cell).Kind);
    }

    [Fact]
    public void AWellCutTreeGoesNearWhereItsAimed()
    {
        foreach (var aim in new[] { 90f, 0f, 200f })
        {
            var (sim, me, tree) = ByATree();
            var dir = Fell(sim, me, tree, aim);
            Assert.True(Off(dir, aim) < 30f, $"aimed {aim}, went {dir:F0} (leans {FellSession.LeanOf(C(10, 11)).Dir})");
        }
    }

    [Fact]
    public void TheFallFollowsTheAimWhenTheNotchSteersAndTheLeanWhenItDoesnt()
    {
        const float lean = 0f, aim = 150f;
        // The notch to the mark: on the aim, within the shake.
        var steered = FellSession.FallDir(aim, lean, 0.8f, FellSession.NotchMark, false, 1f, 0f);
        Assert.True(Off(steered, aim) < 1f, $"{steered}");
        // A shallow notch: pulled well toward the lean.
        var shallow = FellSession.FallDir(aim, lean, 0.8f, 0.08f, false, 1f, 0f);
        Assert.True(Off(shallow, lean) < Off(aim, lean) - 40f, $"{shallow}");
        // Cut through the hinge: it goes with the lean, whatever the aim.
        var through = FellSession.FallDir(aim, lean, 0.8f, FellSession.NotchMark, true, 1f, 0f);
        Assert.True(Off(through, lean) < 1f, $"{through}");
        // A notch past the mark steers less than one at it.
        Assert.True(FellSession.NotchSteer(0.55f) < FellSession.NotchSteer(FellSession.NotchMark));
    }

    [Fact]
    public void ALeanIsTheSameForATreeWhereverItsAskedFrom() =>
        Assert.Equal(FellSession.LeanOf(C(3, 4)), FellSession.LeanOf(C(3, 4)));

    [Fact]
    public void ItCreaksBeforeItLands()
    {
        var (sim, _, tree) = ByATree();
        sim.World.Add(tree, new Falling { Dir = 0f, At = sim.Clock.Tick });
        sim.TickMany(Felling.CreakTicks / 2);
        Assert.True(sim.World.IsAlive(tree), "still creaking");
        sim.TickMany(Felling.CreakTicks);
        Assert.False(sim.World.IsAlive(tree));
        var trunk = sim.Map.Structure[sim.Map.Idx(C(10, 11))];
        Assert.True(Felling.IsTrunk(sim, trunk));
        Assert.Equal(sim.Thing("Pine").Felling!.Logs, sim.World.Get<Trunk>(trunk).Lengths);
    }

    [Fact]
    public void ItHurtsWhoeverItLandsOnAndNotWhoeverStandsBeside()
    {
        var (sim, _, tree) = ByATree();
        // East of the stump at (10, 11): one on the line, one a row off it.
        var under = sim.SpawnCreature(sim.Defs.Creature("Human"), "Under", C(13, 11), ageDays: 25 * 365);
        var beside = sim.SpawnCreature(sim.Defs.Creature("Human"), "Beside", C(13, 13), ageDays: 25 * 365);
        sim.World.Add(tree, new Falling { Dir = 0f, At = sim.Clock.Tick - Felling.CreakTicks });
        Felling.Land(sim, tree);
        Assert.NotEmpty(sim.World.Get<Health>(under).Injuries);
        Assert.Empty(sim.World.Get<Health>(beside).Injuries);
    }

    [Fact]
    public void AWallInTheWayBreaksTheTrunkShort()
    {
        var (sim, _, tree) = ByATree("Pine");
        sim.SpawnStructure(sim.Thing("WoodWall"), C(12, 11));
        sim.RebuildDerivedIfNeeded();
        sim.World.Add(tree, new Falling { Dir = 0f, At = sim.Clock.Tick });
        Felling.Land(sim, tree);
        var trunk = sim.Map.Structure[sim.Map.Idx(C(10, 11))];
        Assert.True(sim.World.Get<Trunk>(trunk).Lengths < sim.Thing("Pine").Felling!.Logs);
    }

    // ---------------- Bucking ----------------

    private static Entity Felled(Simulation sim, Entity tree, float dir = 0f)
    {
        var cell = sim.World.Get<Thing>(tree).Cell;
        sim.World.Add(tree, new Falling { Dir = dir, At = sim.Clock.Tick });
        Felling.Land(sim, tree);
        return sim.Map.Structure[sim.Map.Idx(cell)];
    }

    /// <summary>Steady strokes on the keys, a wedge in once the kerf starts to close, and the butt kept whole when it may be.</summary>
    private static Func<WorkSession, (bool, int, int, bool)> BuckSteady(bool keepGreat = false, bool wedge = true)
    {
        var way = 1;
        return w =>
        {
            var s = (BuckSession)w;
            if (keepGreat && s.CanKeepGreat)
                return (false, 0, 0, true);
            if (s.Wedging > 0f)
                return (false, 0, 0, false);
            if (wedge && !s.Wedged && (s.Progress >= BuckSession.PinchFrom - 0.05f || s.Bound))
                return (false, 0, 0, true);
            if (s.Stroke >= 0.97f) way = -1;
            else if (s.Stroke <= 0.03f) way = 1;
            return (false, 0, way, false);
        };
    }

    private static int Lying(Simulation sim, string def) =>
        sim.World.Query<Thing>().Where(e => sim.World.Get<Thing>(e).Def.Name == def && !sim.World.Has<Stored>(e) && sim.World.Get<Thing>(e).Spawned)
            .Sum(e => sim.World.Get<Stack>(e).Count);

    [Fact]
    public void APineIsBuckedIntoThreeLogs()
    {
        var (sim, me, tree) = ByATree("Pine");
        var trunk = Felled(sim, tree);
        Assert.Equal(AvatarActionKind.Buck, AvatarSystem.ActionAt(sim, me, C(10, 11)).Kind);
        Felling.StartBucking(sim, me, trunk);
        Play(sim, me, BuckSteady());
        Assert.False(sim.World.IsAlive(trunk), "the trunk's all logs now");
        Assert.Equal(3, Lying(sim, Felling.LogDef));
    }

    [Fact]
    public void AnOaksButtCanBeKeptWholeAsAGreatLog()
    {
        var (sim, me, tree) = ByATree("Oak");
        var trunk = Felled(sim, tree);
        Assert.True(sim.World.Get<Trunk>(trunk).GreatButt);
        Felling.StartBucking(sim, me, trunk);
        Play(sim, me, BuckSteady(keepGreat: true), maxTicks: 60 * 600);
        Assert.False(sim.World.IsAlive(trunk));
        Assert.Equal(1, Lying(sim, Felling.LogDef));
        Assert.Equal(1, Lying(sim, Felling.GreatLogDef));
    }

    [Fact]
    public void StoppedHalfwayTheCutLogsAreKeptAndTheRestWaits()
    {
        var (sim, me, tree) = ByATree("Pine");
        var trunk = Felled(sim, tree);
        Felling.StartBucking(sim, me, trunk);
        var predictor = new WorkPredictor();
        var keys = BuckSteady();
        for (var t = 0; t < 60 * 240 && sim.World.TryGet<WorkSession>(me, out var s) && ((BuckSession)s).CutsDone < 1; t++)
        {
            sim.Tick();
            var (held, axis, axis2, act) = predictor.Session is { } p ? keys(p) : (false, 0, 0, false);
            predictor.Update(sim, me, 1, false, held, axis, c => sim.ApplyNow(c), axis2, act: act);
        }
        sim.ApplyNow(new StopWorkCommand(me));
        sim.Tick();
        Assert.Equal(1, Lying(sim, Felling.LogDef));
        Assert.Equal(2, sim.World.Get<Trunk>(trunk).Lengths);
    }

    [Fact]
    public void AKerfLeftUnwedgedAndHurriedBinds()
    {
        var (sim, me, tree) = ByATree("Pine");
        var trunk = Felled(sim, tree);
        Felling.StartBucking(sim, me, trunk);
        var s = Assert.IsType<BuckSession>(sim.World.Get<WorkSession>(me));
        // At the keys' pace the kerf closes slowly; the helper's jerks against it close it fast.
        Assert.True(BuckSession.PinchPerStroke * (1f - BuckSession.PinchFrom) / BuckSession.CutPerStroke < 1f, "a steady unwedged cut should get through");
        Assert.True(BuckSession.BindJolt * 4 >= 1f, "four jerks against it should bind it");
        _ = s;
    }

    [Fact]
    public void AGreatTrunkGoesFasterWithSomeoneOnTheOtherEnd()
    {
        int CutsIn(bool helped)
        {
            var (sim, me, tree) = ByATree("Oak");
            var trunk = Felled(sim, tree);
            Entity helper = Entity.Null;
            if (helped)
            {
                helper = sim.SpawnCreature(sim.Defs.Creature("Human"), "Other End", C(11, 10), ageDays: 25 * 365);
                sim.World.Add(helper, new Avatar());
            }
            Felling.StartBucking(sim, me, trunk);
            if (helped)
                Assert.True(Pairs.Join(sim, helper, me));
            var lead = new WorkPredictor();
            var other = new WorkPredictor();
            var keys = BuckSteady();
            var pulling = 0;
            for (var t = 0; t < 60 * 60 && sim.World.Has<WorkSession>(me); t++)
            {
                sim.Tick();
                var (held, axis, axis2, act) = lead.Session is { } p ? keys(p) : (false, 0, 0, false);
                lead.Update(sim, me, 1, false, held, axis, c => sim.ApplyNow(c), axis2, act: act);
                if (helped && sim.World.Has<WorkSession>(helper))
                {
                    // The other end pulls back on each cue, all the way, then lets go.
                    var h = other.Session as CrosscutSession;
                    var pull = h is { CallOpen: true } || (pulling > 0 && pulling < 40);
                    pulling = pull ? pulling + 1 : 0;
                    other.Update(sim, helper, 1, false, pull && (h?.Drawn ?? 0f) < 1f, 0, c => sim.ApplyNow(c));
                }
            }
            return sim.World.TryGet<WorkSession>(me, out var s) ? ((BuckSession)s).CutsDone : 99;
        }
        Assert.True(CutsIn(helped: true) > CutsIn(helped: false));
    }

    // ---------------- Logs and carrying ----------------

    [Fact]
    public void AGreatLogInTheArmsSlowsYouAndAStackOfFirewoodDoesnt()
    {
        var (sim, me, _) = ByATree();
        var w = sim.World;
        var bare = MovementSystem.WalkSpeedPerTick(sim, me);
        w.Get<Carrying>(me).Item = sim.CreateItem(sim.Thing(Carpenters.FirewoodDef), 20);
        var firewood = MovementSystem.WalkSpeedPerTick(sim, me);
        sim.DestroyThing(w.Get<Carrying>(me).Item);
        w.Get<Carrying>(me).Item = sim.CreateItem(sim.Thing(Felling.GreatLogDef), 1);
        var great = MovementSystem.WalkSpeedPerTick(sim, me);
        Assert.True(firewood > bare * 0.8f, $"{firewood} vs {bare}");
        Assert.True(great < firewood * 0.7f, $"{great} vs {firewood}");
    }

    [Fact]
    public void ALogWontGoInAPack()
    {
        var (sim, me, _) = ByATree();
        var log = sim.CreateItem(sim.Thing(Felling.LogDef), 1);
        Assert.Equal(0, sim.ContainerRoom(sim.PackOf(me), sim.Thing(Felling.LogDef), null));
        _ = log;
    }

    // ---------------- Firewood ----------------

    [Fact]
    public void ThePlainChopStillGivesTimberAndOnlyFellingGivesLogs()
    {
        var pine = TestWorld.LoadDefs().Thing("Pine");
        Assert.All(pine.Harvest!.Yield, y => Assert.Equal("Wood", y.ThingDef.Name));
        Assert.Equal(3, pine.Felling!.Logs);
    }

    [Fact]
    public void WithTheBarnsFirewoodGoneTheAiCarpenterFellsAndSplits()
    {
        var sim = Scenarios.VillageRound.NewSimulation(LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        Scenarios.VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default));
        sim.ApplyNow(new SetTradePreferencesCommand(Ana, [sim.Defs.Trade("Cook")], []));
        sim.ApplyNow(new StartRoundCommand());
        foreach (var item in sim.AvailableItems().ToList())
            if (sim.World.Get<Thing>(item).Def.Name is Carpenters.FirewoodDef or Felling.LogDef or Felling.GreatLogDef)
                sim.DestroyThing(item);
        var carpenter = sim.World.Query<Trade>().First(e => sim.World.Get<Trade>(e).Def.Name == "Carpenter");
        if (Crafting.ToolSpeed(sim, carpenter, Felling.AxeKind) <= 0f)
            Inventory.Stow(sim, carpenter, sim.CreateItem(sim.Thing("Axe"), 1));
        for (var h = 0; h < 8 && Carpenters.TownFirewood(sim) == 0; h++)
            sim.TickMany(SimClock.TicksPerHour);
        Assert.True(Carpenters.TownFirewood(sim) > 0, "the AI carpenter should have felled a tree and split its logs");
    }

    [Fact]
    public void TheTownsFiresBurnFirewoodNotTimber()
    {
        var defs = TestWorld.LoadDefs();
        foreach (var t in defs.AllThings.Where(t => t.Building?.Fuel != null))
            Assert.DoesNotContain(t.Building!.Fuel!.Options, o => o.ThingDef.Name == "Wood");
        Assert.Contains(defs.AllThings, t => t.Building?.Fuel?.Options[0].ThingDef.Name == Carpenters.FirewoodDef);
    }

    [Fact]
    public void ALogIsSplitIntoFirewoodAtTheBlock()
    {
        var (sim, me, _) = ByATree();
        var block = sim.SpawnStructure(sim.Thing(Carpenters.BlockDef), C(9, 10));
        sim.RebuildDerivedIfNeeded();
        sim.World.Get<Carrying>(me).Item = sim.CreateItem(sim.Thing(Felling.LogDef), 1);
        sim.ApplyNow(new CraftCommand(me, block, sim.Defs.AllRecipes.Single(r => r.Name == "SplitLog")));
        Assert.IsType<SplitSession>(sim.World.Get<WorkSession>(me));
        // Over the check, raised past what it needs, let go; a stuck axe rocked free.
        Play(sim, me, w =>
        {
            var s = (SplitSession)w;
            if (s.Stuck > 0)
                return (false, 0, 0, true);
            if (!(s.Raised > 0f) && MathF.Abs(s.Aim - s.Check) > s.CheckWidthNow * 0.4f)
                return (false, Math.Sign(s.Check - s.Aim), 0, false);
            return (s.Raised < s.Need + 0.1f, 0, 0, false);
        });
        var fire = sim.StoredIn(sim.PackOf(me)).Append(sim.World.Get<Carrying>(me).Item)
            .Where(e => sim.World.IsAlive(e) && sim.World.Get<Thing>(e).Def.Name == Carpenters.FirewoodDef).Sum(e => sim.World.Get<Stack>(e).Count);
        Assert.Equal(4, fire);
    }

    [Fact]
    public void TheCarterStacksFirewoodInTheBarn()
    {
        var (sim, _) = Millbrook();
        Assert.True(sim.Defs.Season.FirewoodDelivery.Count > 0);
        var before = Lying(sim, Carpenters.FirewoodDef);
        Shops.OreCarterSystem.DeliverWood(sim);
        Assert.True(Lying(sim, Carpenters.FirewoodDef) > before, "a load of firewood in an ordinary week");
    }

    [Fact]
    public void WithTheBarnsFirewoodGoneTheCarpenterBotFellsAndSplits()
    {
        var sim = Scenarios.VillageRound.NewSimulation(LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        Scenarios.VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default));
        sim.ApplyNow(new SetTradePreferencesCommand(Ana, [sim.Defs.Trade("Carpenter")], []));
        var bot = new Bots.BotPlayer(() => sim, Ana, c => sim.ApplyNow(c), 7) { Temper = Bots.BotTemperament.Steady };
        sim.ApplyNow(new StartRoundCommand());
        var me = Players.CharacterOf(sim, Ana);
        // The barn's firewood gone (and no logs lying about): the town's fires want splitting.
        foreach (var item in sim.AvailableItems().ToList())
            if (sim.World.Get<Thing>(item).Def.Name is Carpenters.FirewoodDef or Felling.LogDef or Felling.GreatLogDef)
                sim.DestroyThing(item);
        bool Split() => sim.Messages.Messages.Any(m => m.Text.StartsWith("You split"));
        for (var t = 0; t < SimClock.TicksPerHour * 6 && !Split(); t++)
        {
            sim.Tick();
            bot.Tick();
        }
        Assert.True(Split(), $"bot: {bot.Doing}, session {(sim.World.TryGet<WorkSession>(me, out var ws) ? ws.GetType().Name : "none")}\n" + string.Join("\n", sim.Messages.Messages.Where(m => m.Text.Contains("buck") || m.Text.Contains("fell") || m.Text.Contains("crash") || m.Text.Contains("Can't") || m.Text.Contains("log")).TakeLast(20).Select(m => m.Text)));
        _ = me;
    }

    // ---------------- Nailing (a wagon's sprung board) ----------------

    /// <summary>A nail session on its own, for its rules: no wagon needed to step it.</summary>
    private static NailSession Nailing()
    {
        var (sim, me, _) = ByATree();
        var s = new NailSession { Nails = 2 };
        s.Worker = me;
        s.Hands = Hands.Of(sim, me, Skill.Crafting);
        s.Rand = new SimRandom(5);
        s.Begin();
        return s;
    }

    /// <summary>Raise to <paramref name="raise"/> and let go, from <paramref name="step"/>; the inputs, and the step after.</summary>
    private static (List<WorkInput>, int) Blow(int step, float raise)
    {
        var hold = (int)MathF.Ceiling(raise / NailSession.RaisePerStep);
        return ([new WorkInput(step, WorkInputKind.Hold, 1), new WorkInput(step + hold, WorkInputKind.Hold, 0)], step + hold + 1);
    }

    [Fact]
    public void ATapSetsANailAndAHardFirstBlowBendsIt()
    {
        var tap = Nailing();
        var (inputs, upTo) = Blow(0, NailSession.SetMax * 0.5f);
        tap.Advance(inputs, upTo);
        Assert.True(tap.Set);
        Assert.False(tap.Bent);

        var hard = Nailing();
        (inputs, upTo) = Blow(0, 1f);
        hard.Advance(inputs, upTo);
        Assert.True(hard.Bent);
        // The claw draws it: a few pulls, and it's a fresh nail to start.
        var claws = Enumerable.Range(0, NailSession.ClawPulls).Select(k => new WorkInput(upTo + k * 2, WorkInputKind.Act)).ToList();
        hard.Advance(claws, upTo + NailSession.ClawPulls * 2 + 1);
        Assert.False(hard.Bent);
        Assert.False(hard.Set);
    }

    // ---------------- Sessions as the rest ----------------

    [Fact]
    public void AFellingSurvivesASaveMidway()
    {
        var (sim, me, tree) = ByATree();
        Felling.StartFelling(sim, me, tree);
        var predictor = new WorkPredictor();
        var keys = FellWell(45f);
        for (var t = 0; t < 600 && !(predictor.Session is FellSession { Notch: > 0.1f }); t++)
        {
            sim.Tick();
            var (held, axis, axis2, act) = predictor.Session is { } s ? keys(s) : (false, 0, 0, false);
            predictor.Update(sim, me, 1, false, held, axis, c => sim.ApplyNow(c), axis2, act: act);
        }
        var before = Assert.IsType<FellSession>(sim.World.Get<WorkSession>(me)); // midway through the notch
        var loaded = SaveGame.Read(SaveGame.Write(sim), sim.Defs).Sim;
        var after = Assert.IsType<FellSession>(loaded.World.Get<WorkSession>(me));
        Assert.Equal(before.Notch, after.Notch);
        Assert.Equal(before.Aim, after.Aim);
        Assert.Equal(before.Backing, after.Backing);
        Assert.Equal(SaveGame.Hash(sim), SaveGame.Hash(loaded));
    }

    /// <summary>Run a session ahead with its commands held back, as a slow link would; then the sim replays them.</summary>
    private static (WorkSession Ahead, WorkSession Real) RunAhead(Simulation sim, Entity me, Func<WorkSession, (bool Held, int Axis, int Axis2, bool Act)> keys)
    {
        var real = sim.World.Get<WorkSession>(me);
        var predictor = new WorkPredictor();
        var held = new List<ISimCommand>();
        var sentNow = false;
        for (var t = 0; t < 400 && (t < 200 || !sentNow); t++)
        {
            var before = held.Count;
            sim.Tick();
            var (h, axis, axis2, act) = predictor.Session is { } s ? keys(s) : (false, 0, 0, false);
            predictor.Update(sim, me, 1, false, h, axis, held.Add, axis2, act: act);
            sentNow = held.Count > before;
        }
        var ahead = predictor.Session!;
        foreach (var c in held)
            sim.ApplyNow(c);
        Assert.Equal(ahead.StepsDone, real.StepsDone);
        Assert.Equal(ahead.Quality, real.Quality);
        return (ahead, real);
    }

    [Fact]
    public void ThePredictedFellMatchesWhatTheSimReplays()
    {
        var (sim, me, tree) = ByATree();
        Felling.StartFelling(sim, me, tree);
        var (ahead, real) = RunAhead(sim, me, FellWell(120f));
        var (a, r) = ((FellSession)ahead, (FellSession)real);
        Assert.True(a.Blows > 0);
        Assert.Equal(a.Blows, r.Blows);
        Assert.Equal(a.Notch, r.Notch);
        Assert.Equal(a.Aim, r.Aim);
    }

    [Fact]
    public void ThePredictedBuckMatchesWhatTheSimReplays()
    {
        var (sim, me, tree) = ByATree();
        Felling.StartBucking(sim, me, Felled(sim, tree));
        var (ahead, real) = RunAhead(sim, me, BuckSteady());
        var (a, r) = ((BuckSession)ahead, (BuckSession)real);
        Assert.True(a.Progress > 0f || a.CutsDone > 0);
        Assert.Equal(a.Progress, r.Progress);
        Assert.Equal(a.Pinch, r.Pinch);
    }

    [Fact]
    public void ACopyOfASessionDoesNotDisturbTheOriginal()
    {
        var (sim, me, tree) = ByATree();
        Felling.StartFelling(sim, me, tree);
        var real = (FellSession)sim.World.Get<WorkSession>(me);
        var copy = (FellSession)real.Clone();
        copy.Advance([new WorkInput(0, WorkInputKind.Hold, 1), new WorkInput(40, WorkInputKind.Hold, 0)], 42);
        Assert.Equal(1, copy.Blows);
        Assert.Equal(0, real.Blows);
        Assert.Equal(0f, real.Notch);
    }

    [Fact]
    public void AFelledTrunkSurvivesASave()
    {
        var (sim, _, tree) = ByATree("Oak");
        var trunk = Felled(sim, tree);
        var loaded = SaveGame.Read(SaveGame.Write(sim), sim.Defs).Sim;
        var t = loaded.World.Get<Trunk>(trunk);
        Assert.Equal(3, t.Lengths);
        Assert.True(t.GreatButt);
        Assert.Equal("Oak", t.Material?.Name);
    }
}

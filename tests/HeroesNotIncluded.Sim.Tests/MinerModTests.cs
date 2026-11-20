using HeroesNotIncluded.Miner;
using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Modding;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>The miner (mods/miner): a player's trade at the mine in Millbrook's hill, turned on here as a player would.</summary>
[Collection(nameof(SimContentTests))]
public class MinerModTests
{
    private static readonly Guid Arin = new("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Bea = new("00000000-0000-0000-0000-00000000000b");

    private static DefDatabase MinerDefs() =>
        DefDatabase.LoadDirectories([.. TestWorld.DefDirectories(), Path.Combine(TestWorld.FindDefsDirectory(), "..", "..", "mods", "miner", "defs")]);

    private static T WithMiner<T>(Func<T> test)
    {
        try
        {
            SimContent.Use(TestContent.Standard); // on by default now: the standard content has it
            return test();
        }
        finally
        {
            SimContent.Use(TestContent.Standard);
        }
    }

    private static Simulation Round(DefDatabase defs, params (Guid Id, string? Wants)[] seats)
    {
        var sim = VillageRound.NewSimulation(defs, 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        foreach (var (id, wants) in seats)
        {
            sim.ApplyNow(new JoinLobbyCommand(id, id.ToString()[^1..], default));
            if (wants != null)
                sim.ApplyNow(new SetTradePreferencesCommand(id, [sim.Defs.Trade(wants)], []));
        }
        sim.ApplyNow(new StartRoundCommand());
        return sim;
    }

    /// <summary>Tick the sim with the keys a player would press, through the predictor as the game does.</summary>
    private static void Play(Simulation sim, Entity me, Func<WorkSession, (bool Held, int Axis)> keys, int maxTicks = 60 * 120)
    {
        var predictor = new WorkPredictor();
        for (var t = 0; t < maxTicks && sim.World.Has<WorkSession>(me); t++)
        {
            sim.Tick();
            var (held, axis) = predictor.Session is { } s ? keys(s) : (false, 0);
            predictor.Update(sim, me, 1, false, held, axis, c => sim.ApplyNow(c));
        }
        sim.Tick();
    }

    /// <summary>The bot's own hewing (it reads only the session): over the crack, swung back past the mark, let go.</summary>
    private static (bool, int) Hew(WorkSession s) =>
        new MinerBot(null!).Play(s) is { } k ? (k.Held, k.Axis) : (false, 0);

    private static (Simulation Sim, Entity Me) Miner()
    {
        var sim = Round(MinerDefs(), (Arin, "Miner"));
        var me = Players.CharacterOf(sim, Arin);
        return (sim, me);
    }

    [Fact]
    public void NobodyIsTheMinerUnlessTheyAskedAndTheShedStandsByTheMine() => WithMiner(() =>
    {
        var defs = MinerDefs();
        Assert.True(defs.Trade("Miner").PlayerOnly);
        var sim = Round(defs, (Arin, null), (Bea, "Blacksmith"));
        Assert.DoesNotContain(sim.Creatures(), e => sim.World.TryGet<Trade>(e, out var t) && t.Def.Name == "Miner");
        var shed = ShopSystem.WorkplaceOf(sim, defs.Trade("Miner"));
        Assert.False(shed.IsNull, "the shed's plot is filled with the mod on");
        // Without the mod the plot's left empty, and the round goes on as it would.
        var plain = Round(DefDatabase.LoadDirectories([.. TestWorld.DefDirectories().Where(d => !d.Contains(Path.Combine("mods", "miner")))]), (Bea, "Blacksmith"));
        Assert.DoesNotContain(plain.World.Query<PresetBuilding>(), e => plain.World.Get<PresetBuilding>(e).Def.Name == "MineShed");
        return 0;
    });

    [Fact]
    public void ALateJoinerCanComeToTownAsTheMinerOnceOnly() => WithMiner(() =>
    {
        var sim = Round(MinerDefs(), (Arin, "Blacksmith"));
        var w = sim.World;
        var villagers = sim.Creatures().Count();
        var miner = sim.Defs.Trade("Miner");

        sim.ApplyNow(new TakeTradeCommand(Bea, [miner], [], "Bea", default, null));

        var bea = Players.CharacterOf(sim, Bea);
        Assert.Equal(miner, w.Get<Trade>(bea).Def);
        Assert.Equal("Bea", w.Get<Creature>(bea).Name);
        Assert.Equal(villagers + 1, sim.Creatures().Count()); // a newcomer: no villager was taken over
        Assert.True(Crafting.ToolSpeed(sim, bea, "Pick") > 0f, "the kit's pick");

        // A third asking for it gets the closest thing going, not a second miner.
        var cy = new Guid("00000000-0000-0000-0000-00000000000c");
        sim.ApplyNow(new TakeTradeCommand(cy, [miner], []));
        Assert.NotEqual(miner, w.Get<Trade>(Players.CharacterOf(sim, cy)).Def);
        return 0;
    });

    [Fact]
    public void TheLessonGoesIntoTheMineHewsAFaceAndFillsTheShedsChest() => WithMiner(() =>
    {
        var (sim, me) = Miner();
        sim.Round.Practice = true;
        var trade = sim.Defs.Trade("Miner");
        var lesson = trade.Lessons.Single(l => l.Id == "hew");
        var run = new HeroesNotIncluded.Sim.Scenarios.Lessons.LessonRun(sim, me, trade, lesson);
        HeroesNotIncluded.Sim.Scenarios.Lessons.LessonStage Stage() => HeroesNotIncluded.Sim.Scenarios.Lessons.Lessons.Stage(sim, me, run);

        var first = Stage();
        Assert.Equal("mine", first.Step);
        Assert.Equal(ShopSystem.WorkplaceOf(sim, trade), first.Point);

        HewDrill.Stage(sim, me); // stood in the tunnels at a face, the hewing begun
        Assert.Equal("hew", Stage().Step);
        sim.World.Remove<WorkSession>(me);
        Assert.Equal("face", Stage().Step);

        var stone = sim.CreateItem(sim.Defs.Thing("Stone"), 2);
        Inventory.Stow(sim, me, stone);
        var chest = run.Station(sim, "Chest");
        Assert.Equal(("carry", chest), (Stage().Step, Stage().Point));
        Assert.True(sim.Store(chest, sim.CreateItem(sim.Defs.Thing("Stone"), 2)).IsNull);
        Assert.True(Stage().Done);
        return 0;
    });

    [Fact]
    public void TheSoundingLessonTakesTheHammerTapsTheWallAndChalksIt() => WithMiner(() =>
    {
        var (sim, me) = Miner();
        sim.Round.Practice = true;
        var trade = sim.Defs.Trade("Miner");
        var lesson = trade.Lessons.Single(l => l.Id == "sound");
        Assert.True(lesson.Core);
        var run = new HeroesNotIncluded.Sim.Scenarios.Lessons.LessonRun(sim, me, trade, lesson);
        HeroesNotIncluded.Sim.Scenarios.Lessons.LessonStage Stage() => HeroesNotIncluded.Sim.Scenarios.Lessons.Lessons.Stage(sim, me, run);

        Assert.Equal("hammer", Stage().Step);
        HewDrill.StageSounding(sim, me); // the hammer in hand, at a face, the sounding begun
        var s = (SoundSession)sim.World.Get<WorkSession>(me);
        Assert.Equal("tap", Stage().Step);
        var predictor = new WorkPredictor();
        for (var t = 0; t < (SoundSession.TapSteps + 2) * SoundSession.Spots * 2 && s.TapCount < SoundSession.Spots; t++)
        {
            sim.Tick();
            predictor.Update(sim, me, 1, t % (SoundSession.TapSteps + 2) == 0, false, t % (SoundSession.TapSteps + 2) == 1 ? 1 : 0, c => sim.ApplyNow(c));
            s = (SoundSession)sim.World.Get<WorkSession>(me);
        }
        Assert.Equal("chalk", Stage().Step);
        Assert.False(Stage().Done);
        sim.Tick();
        predictor.Update(sim, me, 1, false, false, 0, c => sim.ApplyNow(c), act: true);
        for (var t = 0; t < 5; t++)
        {
            sim.Tick();
            predictor.Update(sim, me, 1, false, false, 0, c => sim.ApplyNow(c));
        }
        sim.ApplyNow(new StopWorkCommand(me));
        Assert.True(Stage().Done);
        return 0;
    });

    [Fact]
    public void ThereIsNoMinerInATownWithoutAMine() => WithMiner(() =>
    {
        var sim = VillageRound.NewSimulation(MinerDefs(), 1234, "Reedmere");
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim, "Reedmere");
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Arin, "Arin", default));
        sim.ApplyNow(new SetTradePreferencesCommand(Arin, [sim.Defs.Trade("Miner")], []));
        sim.ApplyNow(new StartRoundCommand());
        Assert.False(RoundSystem.InTown(sim, sim.Defs.Trade("Miner")));
        Assert.NotEqual("Miner", sim.World.Get<Trade>(Players.CharacterOf(sim, Arin)).Def.Name);
        sim.ApplyNow(new TakeTradeCommand(Bea, [sim.Defs.Trade("Miner")], []));
        Assert.NotEqual("Miner", sim.World.Get<Trade>(Players.CharacterOf(sim, Bea)).Def.Name);
        return 0;
    });

    [Fact]
    public void AMinerWhosePlayerLeftKeepsTheTradeAndThePick() => WithMiner(() =>
    {
        var (sim, me) = Miner();
        Players.Leave(sim, Arin);
        for (var t = 0; t < 60 * 30; t++)
            sim.Tick();
        Assert.True(sim.World.IsAlive(me));
        Assert.Equal("Miner", sim.World.Get<Trade>(me).Def.Name);
        Assert.True(Crafting.ToolSpeed(sim, me, "Pick") > 0f);
        Assert.False(RoundSystem.MayGather(sim, me));
        return 0;
    });

    [Fact]
    public void TheMinerMayDigAndMineButGathersNothingElse() => WithMiner(() =>
    {
        var (sim, me) = Miner();
        Assert.True(RoundSystem.MayMine(sim, me));
        Assert.False(RoundSystem.MayGather(sim, me));
        Assert.False(RoundSystem.MayFell(sim, me));
        var mind = sim.World.Get<Mind>(me);
        Assert.True(mind.WorkPriority[(int)WorkType.Mine] > 0);
        Assert.Equal(0, mind.WorkPriority[(int)WorkType.Chop]);
        Assert.Equal(0, mind.WorkPriority[(int)WorkType.Harvest]);
        Assert.True(Crafting.ToolSpeed(sim, me, "Pick") > 0f, "the kit's pick");
        // Anyone else in the round still may not.
        var smith = sim.Creatures().First(e => sim.World.TryGet<Trade>(e, out var t) && t.Def.Name == "Blacksmith");
        Assert.False(RoundSystem.MayMine(sim, smith));
        return 0;
    });

    [Fact]
    public void FAtARockFaceHewsItOut() => WithMiner(() =>
    {
        var (sim, me) = Miner();
        HewDrill.Stage(sim, me);
        var hew = Assert.IsType<HewSession>(sim.World.Get<WorkSession>(me));
        var face = hew.Cell;
        Play(sim, me, Hew);
        Assert.False(sim.World.Has<WorkSession>(me));
        Assert.False(sim.Map.IsSolid(sim.Map.Idx(face)), "the face came away");
        Assert.Contains(sim.World.Query<Thing>(), e => sim.World.Get<Thing>(e) is { Spawned: true } t && t.Cell == face && t.Def.Name == "Stone");
        return 0;
    });

    [Fact]
    public void ACleanHewingGetsMoreOutOfASeamThanTheDigKey() => WithMiner(() =>
    {
        var (sim, me) = Miner();
        HewDrill.Stage(sim, me);
        var face = ((HewSession)sim.World.Get<WorkSession>(me)).Cell;
        WorkSystem.Stop(sim, me, null);
        var iron = sim.Defs.Block("IronOre");
        sim.Map.Block[sim.Map.Idx(face)] = iron;
        sim.Map.MarkStructureChanged(sim.Map.Idx(face));
        sim.RebuildDerivedIfNeeded();
        // What the dig key would give the same hand for it.
        var dug = iron.DigYield.Where(y => y.ThingDef.Name == "IronOre")
            .Sum(y => Mining.OreYield(sim, me, iron, y.ThingDef, y.Count));
        // F at it, as a player would: hewn as many slabs as the rock it looks like.
        var action = AvatarSystem.ActionAt(sim, me, face);
        Assert.Equal(AvatarActionKind.Custom, action.Kind);
        Assert.StartsWith("Hew the", action.Label);
        AvatarSystem.Perform(sim, me, action);
        Assert.Equal(HewSession.ChunksFor(HewSession.FaceOf(sim, face)), Assert.IsType<HewSession>(sim.World.Get<WorkSession>(me)).Chunks);
        Play(sim, me, Hew);
        var hewn = sim.World.Query<Thing>().Where(e => sim.World.Get<Thing>(e) is { Spawned: true } t && t.Cell == face && t.Def.Name == "IronOre")
            .Sum(e => sim.World.Get<Stack>(e).Count);
        Assert.Equal(dug + 1, hewn);
        return 0;
    });

    [Fact]
    public void OffTheCrackHardTheArmsAreJarred() => WithMiner(() =>
    {
        var (sim, me) = Miner();
        HewDrill.Stage(sim, me);
        var s = (HewSession)sim.World.Get<WorkSession>(me).Clone();
        // Walk the point as far from the crack as it goes, then swing hard and let go.
        var away = s.Crack > 0 ? -1 : 1;
        var inputs = new List<WorkInput> { new(0, WorkInputKind.Axis, away), new(60, WorkInputKind.Axis, 0), new(60, WorkInputKind.Hold, 1), new(100, WorkInputKind.Hold, 0) };
        s.Advance(inputs, 101);
        Assert.Equal(HewBlow.Rang, s.LastBlow);
        Assert.True(s.Jarred > 0);
        Assert.Equal(0, s.ChunksDone);
        // Jarred, the next swing comes up slower.
        var before = s.Raised;
        s.Advance([new WorkInput(101, WorkInputKind.Hold, 1)], 111);
        Assert.True(s.Raised - before < HewSession.RaisePerStep * 10 * 0.5f);
        return 0;
    });

    [Fact]
    public void AHewingUnderWaySurvivesASave() => WithMiner(() =>
    {
        var (sim, me) = Miner();
        HewDrill.Stage(sim, me);
        Play(sim, me, Hew, maxTicks: 90);
        var live = (HewSession)sim.World.Get<WorkSession>(me);
        var loaded = SaveLoadTests.RoundTrip(sim);
        var again = Assert.IsType<HewSession>(loaded.World.Get<WorkSession>(me));
        Assert.Equal(live.Cell, again.Cell);
        Assert.Equal(live.Chunks, again.Chunks);
        Assert.Equal(live.ChunksDone, again.ChunksDone);
        Assert.Equal(live.Crack, again.Crack);
        Assert.Equal(live.Face, again.Face);
        return 0;
    });

    /// <summary>
    /// Stood at a face with the sounding hammer in hand, the wall's five spots and the two cells behind each made plain
    /// rock, but for a pocket of iron ore one cell behind <paramref name="seamAt"/>; the sounding begun afresh.
    /// </summary>
    private static (Simulation Sim, Entity Me, SoundSession S) Sounding(int seamAt)
    {
        var (sim, me) = Miner();
        HewDrill.StageSounding(sim, me);
        var first = Assert.IsType<SoundSession>(sim.World.Get<WorkSession>(me));
        var here = sim.World.Get<Position>(me).Cell;
        WorkSystem.Stop(sim, me, null);
        var map = sim.Map;
        var host = HewSession.FaceOf(sim, first.Face);
        for (var k = 0; k < SoundSession.Spots; k++)
            for (var depth = 0; depth < SoundSession.DepthWeight.Length; depth++)
            {
                var c = first.CellOf(k) + first.Into * depth;
                map.Block[map.Idx(c)] = k == seamAt && depth == 1 ? sim.Defs.Block("IronOre") : host;
                map.MarkStructureChanged(map.Idx(c));
            }
        sim.RebuildDerivedIfNeeded();
        WorkSystem.Start(sim, me, SoundSession.At(sim, first.Face, here));
        return (sim, me, (SoundSession)sim.World.Get<WorkSession>(me));
    }

    /// <summary>Taps at each spot in turn, pointed at with the mouse, <paramref name="each"/> apiece, a tap's note left to die between.</summary>
    private static int TapAll(SoundSession s, int each, int from = 0)
    {
        var inputs = new List<WorkInput>();
        var step = from;
        for (var k = 0; k < SoundSession.Spots; k++)
        {
            inputs.Add(new WorkInput(step, WorkInputKind.Point, WorkSession.PackPoint((k + 0.5f) / SoundSession.Spots, 0.5f)));
            for (var n = 0; n < each; n++)
            {
                step += SoundSession.TapSteps + 1;
                inputs.Add(new WorkInput(step, WorkInputKind.Press));
            }
            step++;
        }
        s.Advance(inputs, step + 1);
        return step + 1;
    }

    [Fact]
    public void TheSoundingHammerInHandTurnsFAtAFaceToSoundingIt() => WithMiner(() =>
    {
        var (sim, me) = Miner();
        // The kit's hammer, in the pack or the hand.
        var carried = sim.StoredIn(sim.PackOf(me)).Append(sim.World.Get<Carrying>(me).Item).Where(e => !e.IsNull);
        Assert.Contains(carried, e => sim.World.Get<Thing>(e).Def.Name == SoundSession.HammerDef);
        HewDrill.StageSounding(sim, me);
        var s = Assert.IsType<SoundSession>(sim.World.Get<WorkSession>(me));
        WorkSystem.Stop(sim, me, null);
        var action = AvatarSystem.ActionAt(sim, me, s.Face);
        Assert.StartsWith("Sound the", action.Label);
        AvatarSystem.Perform(sim, me, action);
        Assert.IsType<SoundSession>(sim.World.Get<WorkSession>(me));
        return 0;
    });

    [Fact]
    public void OreBehindASpotDeadensItsNoteAndTheBotHearsWhere() => WithMiner(() =>
    {
        var (_, _, live) = Sounding(seamAt: 0);
        var s = (SoundSession)live.Clone();
        Assert.All(Enumerable.Range(0, SoundSession.Spots), k => Assert.Null(s.HeardAt(k))); // nothing heard before a tap
        TapAll(s, each: 10);
        Assert.True(s.Rock.All(r => r));
        Assert.All(Enumerable.Range(0, SoundSession.Spots), k => Assert.Equal(SoundSession.Kept, s.RecentAt(k).Count())); // the last few kept, as heard
        var seam = s.HeardAt(0)!.Value;
        for (var k = 1; k < SoundSession.Spots; k++)
            Assert.True(seam > s.HeardAt(k)!.Value + 0.25f, $"spot 0 ({seam:0.00}) sounds deader than spot {k} ({s.HeardAt(k):0.00})");
        Assert.Equal(0, MinerBot.Seam(s));

        // Sound rock all along: no spot stands out, and the bot chalks nothing.
        var (_, _, plain) = Sounding(seamAt: -1);
        var p = (SoundSession)plain.Clone();
        TapAll(p, each: 10);
        Assert.Null(MinerBot.Seam(p));
        return 0;
    });

    [Fact]
    public void TapsComeNoFasterThanTheNoteDies() => WithMiner(() =>
    {
        var (_, _, live) = Sounding(seamAt: 2);
        var s = (SoundSession)live.Clone();
        s.Advance([new WorkInput(0, WorkInputKind.Press), new WorkInput(1, WorkInputKind.Press), new WorkInput(5, WorkInputKind.Press)], 10);
        Assert.Equal(1, s.TapCount);
        s.Advance([new WorkInput(SoundSession.TapSteps + 2, WorkInputKind.Press)], SoundSession.TapSteps + 3);
        Assert.Equal(2, s.TapCount);
        return 0;
    });

    [Fact]
    public void ChalkStaysOnTheWallTillTheFaceIsDugOut() => WithMiner(() =>
    {
        var (sim, me, s) = Sounding(seamAt: 1);
        var cell = s.CellOf(1);
        var predictor = new WorkPredictor();
        sim.Tick();
        // Over spot 1 (the mouse), Space once: a seam.
        predictor.Update(sim, me, 1, false, false, 0, c => sim.ApplyNow(c), point: WorkSession.PackPoint(1.5f / SoundSession.Spots, 0.5f));
        sim.Tick();
        predictor.Update(sim, me, 1, false, false, 0, c => sim.ApplyNow(c), act: true);
        for (var t = 0; t < 10; t++)
        {
            sim.Tick();
            predictor.Update(sim, me, 1, false, false, 0, c => sim.ApplyNow(c));
        }
        Assert.Equal(ChalkMark.Seam, ((SoundSession)sim.World.Get<WorkSession>(me)).Marks[1]);
        Assert.Equal(ChalkMark.None, ChalkMarks.At(sim, cell)); // chalked as the sounding's put down
        sim.ApplyNow(new StopWorkCommand(me));
        Assert.Equal(ChalkMark.Seam, ChalkMarks.At(sim, cell));
        Assert.Equal(ChalkMark.Seam, ChalkMarks.At(SaveLoadTests.RoundTrip(sim), cell));

        // Sounded again, the chalk's there to start with; and once the face is dug out, it's gone with the rock.
        Assert.Equal(ChalkMark.Seam, SoundSession.At(sim, s.Face, sim.World.Get<Position>(me).Cell).Marks[1]);
        sim.DigOut(cell, me);
        for (var t = 0; t < SimClock.TicksPerHour / 4 + 1; t++)
            sim.Tick();
        Assert.Equal(ChalkMark.None, ChalkMarks.At(sim, cell));
        return 0;
    });

    [Fact]
    public void ASoundingUnderWaySurvivesASave() => WithMiner(() =>
    {
        var (sim, me, s) = Sounding(seamAt: 3);
        var predictor = new WorkPredictor();
        for (var t = 0; t < 120; t++)
        {
            sim.Tick();
            predictor.Update(sim, me, 1, t % 30 == 0, false, t % 60 == 0 ? 1 : 0, c => sim.ApplyNow(c));
        }
        var live = (SoundSession)sim.World.Get<WorkSession>(me);
        Assert.True(live.TapCount > 0);
        var again = Assert.IsType<SoundSession>(SaveLoadTests.RoundTrip(sim).World.Get<WorkSession>(me));
        Assert.Equal(live.Face, again.Face);
        Assert.Equal(live.Spot, again.Spot);
        Assert.Equal(live.TapCount, again.TapCount);
        Assert.Equal(live.Taps, again.Taps);
        Assert.Equal(live.Heard, again.Heard);
        Assert.Equal(live.Rock, again.Rock);
        for (var k = 0; k < SoundSession.Spots; k++)
            Assert.Equal(live.RecentAt(k), again.RecentAt(k));
        return 0;
    });

    // ---------------- The barrow ----------------

    private static int Lying(Simulation sim, string def) =>
        sim.World.Query<Thing>().Where(e => sim.World.Get<Thing>(e) is { Spawned: true } t && t.Def.Name == def).Sum(e => sim.World.Get<Stack>(e).Count);

    /// <summary>The hauling drill: at a face, six iron ore and six coal lying at the miner's feet, a barrow beside, the loading begun.</summary>
    private static (Simulation Sim, Entity Me, LoadSession S) Hauling()
    {
        var (sim, me) = Miner();
        HewDrill.StageHauling(sim, me);
        return (sim, me, Assert.IsType<LoadSession>(sim.World.Get<WorkSession>(me)));
    }

    /// <summary>Set every piece, the slot for each chosen by <paramref name="slotFor"/> (the n-th piece), pointing with the mouse.</summary>
    private static void SetAll(LoadSession s, Func<int, int> slotFor)
    {
        var inputs = new List<WorkInput>();
        var n = s.ToLoad.Count;
        for (var k = 0; k < n; k++)
        {
            var slot = slotFor(k);
            var u = (Barrows.ColOf(slot) + 0.5f) / Barrows.Cols;
            var v = (Barrows.RowOf(slot) + 0.5f) / Barrows.Rows;
            inputs.Add(new WorkInput(2 * k, WorkInputKind.Point, WorkSession.PackPoint(u, v)));
            inputs.Add(new WorkInput(2 * k + 1, WorkInputKind.Press));
        }
        s.Advance(inputs, 2 * n + 2);
    }

    /// <summary>Play the live session to its end with the given slots, as the predictor would send them.</summary>
    private static void Load(Simulation sim, Entity me, Func<int, int> slotFor)
    {
        var predictor = new WorkPredictor();
        var k = 0;
        for (var t = 0; t < 600 && sim.World.Has<WorkSession>(me); t++)
        {
            sim.Tick();
            var slot = slotFor(k);
            var point = WorkSession.PackPoint((Barrows.ColOf(slot) + 0.5f) / Barrows.Cols, (Barrows.RowOf(slot) + 0.5f) / Barrows.Rows);
            var press = t % 2 == 1;
            predictor.Update(sim, me, 1, press, false, 0, c => sim.ApplyNow(c), point: point);
            if (press)
                k++;
        }
        sim.Tick();
    }

    /// <summary>Even and square: across the bed a row at a time from just behind the wheel, the weight centred near the ideal.</summary>
    private static int Square(int k) => Barrows.SlotAt(k % Barrows.Cols, k / (Barrows.Cols * Barrows.Layers));

    /// <summary>All down the left side.</summary>
    private static int Lopsided(int k) => Barrows.SlotAt(0, k / Barrows.Layers % Barrows.Rows);

    [Fact]
    public void TwoBarrowsStandByTheMineShed() => WithMiner(() =>
    {
        var (sim, _) = Miner();
        var shed = ShopSystem.WorkplaceOf(sim, sim.Defs.Trade("Miner"));
        var chest = HeroesNotIncluded.Sim.Systems.Stock.ChestOf(sim, shed);
        var near = sim.World.Query<Thing>().Count(e => sim.World.Get<Thing>(e) is { Spawned: true } t && t.Def.Name == Barrows.BarrowDef
            && HeroesNotIncluded.Sim.Maps.Map.Distance(t.Cell, sim.World.Get<Thing>(chest).Cell) <= 4);
        Assert.Equal(MinerModule.ShedBarrows, near);
        return 0;
    });

    [Fact]
    public void LoadingTakesTheOreInReachOntoTheBedAndLeavesNothingBehind() => WithMiner(() =>
    {
        var (sim, me, s) = Hauling();
        // Six iron ore (18 kg) and six coal (6 kg): all of it fits, 24 of 30 kg.
        Assert.Equal(12, s.ToLoad.Count);
        Load(sim, me, Square);
        Assert.False(sim.World.Has<WorkSession>(me));
        var load = sim.World.Get<BarrowLoad>(s.Barrow);
        Assert.Equal(6, load.Pieces.Count(p => p.Def.Name == "IronOre"));
        Assert.Equal(6, load.Pieces.Count(p => p.Def.Name == "Coal"));
        Assert.Equal(0, Lying(sim, "IronOre"));
        Assert.Equal(0, Lying(sim, "Coal"));
        Assert.True(Barrows.Risk(load.Pieces) < Barrows.Steady, $"square loading is steady (risk {Barrows.Risk(load.Pieces):0.00})");
        return 0;
    });

    [Fact]
    public void ASlotTakesTwoAndSpaceLiftsTheTopOneOff() => WithMiner(() =>
    {
        var (_, _, live) = Hauling();
        var s = (LoadSession)live.Clone();
        var slot = Barrows.SlotAt(1, 1);
        var at = WorkSession.PackPoint(0.5f, 1.5f / Barrows.Rows);
        s.Advance([new WorkInput(0, WorkInputKind.Point, at), new WorkInput(1, WorkInputKind.Press), new WorkInput(2, WorkInputKind.Press), new WorkInput(3, WorkInputKind.Press)], 4);
        Assert.Equal(2, Barrows.InSlot(s.Placed, slot)); // the third doesn't fit there
        Assert.Equal(10, s.ToLoad.Count);
        s.Advance([new WorkInput(4, WorkInputKind.Act)], 5);
        Assert.Equal(1, Barrows.InSlot(s.Placed, slot));
        Assert.Equal(11, s.ToLoad.Count);
        Assert.Equal(1, s.Lifts);
        return 0;
    });

    [Fact]
    public void FAtABarrowLoadsItWithOreInReachOrElsePicksItUp() => WithMiner(() =>
    {
        var (sim, me, s) = Hauling();
        var barrow = s.Barrow;
        var cell = sim.World.Get<Thing>(barrow).Cell;
        WorkSystem.Stop(sim, me, null); // nothing loaded yet: the ore's still lying there
        var load = AvatarSystem.ActionAt(sim, me, cell);
        Assert.StartsWith("Load the barrow", load.Label);
        Load(sim, me, Square); // not started: F first
        AvatarSystem.Perform(sim, me, load);
        Assert.IsType<LoadSession>(sim.World.Get<WorkSession>(me));
        Load(sim, me, Square);
        // Nothing left to load: F picks it up into both hands.
        var take = AvatarSystem.ActionAt(sim, me, cell);
        Assert.Equal(AvatarActionKind.PickUp, take.Kind);
        AvatarSystem.Perform(sim, me, take);
        Assert.Equal(barrow, Barrows.Held(sim, me));
        Assert.Equal(12, Barrows.LoadOf(sim, barrow).Pieces.Count); // its load came with it

        // With it in hand, F at ore lying in reach sets it down and loads it.
        var here = sim.World.Get<Position>(me).Cell;
        var ore = sim.PlaceItemNear(sim.CreateItem(sim.Defs.Thing("CopperOre"), 2), here);
        var oreCell = sim.World.Get<Thing>(ore).Cell;
        var set = AvatarSystem.ActionAt(sim, me, oreCell);
        Assert.StartsWith("Set the barrow down", set.Label);
        AvatarSystem.Perform(sim, me, set);
        Assert.True(Barrows.Held(sim, me).IsNull);
        Assert.Equal(barrow, ((LoadSession)sim.World.Get<WorkSession>(me)).Barrow);
        return 0;
    });

    /// <summary>The miner pushing a barrow with <paramref name="pieces"/> in it across <paramref name="cells"/> fresh cells, a step per tenth of a second.</summary>
    private static (Simulation Sim, Entity Me, Entity Barrow) Pushing(Func<int, int> slotFor, int count = 10)
    {
        var (sim, me) = Miner();
        var barrow = sim.CreateItem(sim.Defs.Thing(Barrows.BarrowDef), 1);
        var load = new BarrowLoad();
        for (var k = 0; k < count; k++)
            load.Pieces.Add(new BarrowPiece(sim.Defs.Thing("IronOre"), (byte)slotFor(k)));
        sim.World.Add(barrow, load);
        sim.World.Get<Carrying>(me).Item = barrow;
        return (sim, me, barrow);
    }

    /// <summary>
    /// Push the barrow back and forth along a row of ground by where the miner stands, a cell every tenth of a second;
    /// <paramref name="lean"/> given, leaning it back with Q/E as a player watching the gauge would (by the lean, which anyone sees).
    /// </summary>
    private static void Walk(Simulation sim, Entity me, int cells, bool lean = false)
    {
        var start = sim.World.Get<Position>(me).Cell;
        for (var k = 1; k <= cells && !Barrows.Held(sim, me).IsNull; k++)
        {
            var x = start.X + (k / 6 % 2 == 0 ? k % 6 : 6 - k % 6);
            HeroesNotIncluded.Sim.Scenarios.FireDrill.MoveTo(sim, me, new int3(x, start.Y, start.Z));
            for (var t = 0; t < 6; t++)
            {
                if (lean && Barrows.Held(sim, me) is { IsNull: false } b && sim.World.TryGet<BarrowLoad>(b, out var load))
                    sim.ApplyNow(new BarrowLeanCommand(me, load.Lean < -0.15f ? 1 : load.Lean > 0.15f ? -1 : 0));
                sim.Tick();
            }
        }
    }

    [Fact]
    public void ASquareLoadNeverTipsAndALopsidedOneDoesWithNothingLost() => WithMiner(() =>
    {
        var (sim, me, barrow) = Pushing(Square);
        Assert.True(Barrows.Risk(Barrows.LoadOf(sim, barrow).Pieces) < Barrows.Steady);
        Walk(sim, me, 400);
        Assert.Equal(barrow, Barrows.Held(sim, me));
        Assert.Equal(10, Barrows.LoadOf(sim, barrow).Pieces.Count);

        var (sim2, me2, barrow2) = Pushing(Lopsided);
        Assert.True(Barrows.Risk(Barrows.LoadOf(sim2, barrow2).Pieces) > Barrows.Steady + 0.4f);
        var before = Lying(sim2, "IronOre");
        Walk(sim2, me2, 400);
        Assert.True(Barrows.Held(sim2, me2).IsNull, "it went over");
        Assert.True(sim2.World.Get<Thing>(barrow2).Spawned, "set down where it fell");
        var left = Barrows.LoadOf(sim2, barrow2).Pieces;
        Assert.InRange(left.Count, 1, 9);
        Assert.Equal(10, left.Count + Lying(sim2, "IronOre") - before); // spilled, not lost
        return 0;
    });

    [Fact]
    public void ALopsidedLoadLeanedBackAsItsPushedMakesIt() => WithMiner(() =>
    {
        var (sim, me, barrow) = Pushing(Lopsided);
        Walk(sim, me, 400, lean: true);
        Assert.Equal(barrow, Barrows.Held(sim, me));
        Assert.Equal(10, Barrows.LoadOf(sim, barrow).Pieces.Count);
        return 0;
    });

    [Fact]
    public void ABarrowStoodStillSettles() => WithMiner(() =>
    {
        var (sim, me, barrow) = Pushing(Lopsided);
        Walk(sim, me, 6); // a few steps: it starts to lean
        var load = sim.World.Get<BarrowLoad>(barrow);
        load.Lean = -0.6f;
        for (var t = 0; t < 60 * 4; t++)
            sim.Tick();
        Assert.Equal(barrow, Barrows.Held(sim, me));
        Assert.InRange(load.Lean, -0.1f, 0.1f);
        return 0;
    });

    /// <summary>The miner at the smithy's chest with a loaded barrow in hand; the smith with <paramref name="silver"/> to pay.</summary>
    private static (Simulation Sim, Entity Me, Entity Smith, Entity Chest) AtTheSmithy(int silver)
    {
        var sim = Round(MinerDefs(), (Arin, "Miner"), (Bea, "Blacksmith"));
        var me = Players.CharacterOf(sim, Arin);
        var smith = Players.CharacterOf(sim, Bea);
        foreach (var coins in Coin.Stacks(sim, smith).ToList())
            sim.DestroyThing(Inventory.TakeFromPerson(sim, smith, coins, sim.World.Get<Stack>(coins).Count));
        if (silver > 0)
            Inventory.Stow(sim, smith, sim.CreateItem(sim.Defs.Thing(Coin.Silver), silver));
        var smithy = ShopSystem.BuildingOf(sim, sim.Defs.Building("Smithy"));
        var chest = HeroesNotIncluded.Sim.Systems.Stock.ChestOf(sim, smithy);
        var at = sim.World.Get<Thing>(chest).Cell;
        var stand = sim.SpiralFrom(at, 1).First(c => sim.Map.InBounds(c) && sim.Map.Passable(c) && AvatarSystem.InReach(c, at));
        HeroesNotIncluded.Sim.Scenarios.FireDrill.MoveTo(sim, me, stand);
        var barrow = sim.CreateItem(sim.Defs.Thing(Barrows.BarrowDef), 1);
        var load = new BarrowLoad();
        for (var k = 0; k < 6; k++)
            load.Pieces.Add(new BarrowPiece(sim.Defs.Thing("IronOre"), (byte)Square(k)));
        for (var k = 6; k < 10; k++)
            load.Pieces.Add(new BarrowPiece(sim.Defs.Thing("Coal"), (byte)Square(k)));
        sim.World.Add(barrow, load);
        sim.World.Get<Carrying>(me).Item = barrow;
        return (sim, me, smith, chest);
    }

    [Fact]
    public void TippingIntoTheSmithysChestIsPaidForByTheSmith() => WithMiner(() =>
    {
        var (sim, me, smith, chest) = AtTheSmithy(silver: 20);
        var had = Coin.Count(sim, me);
        var ore = OreInTheSmithy(sim);
        var tip = AvatarSystem.ActionAt(sim, me, sim.World.Get<Thing>(chest).Cell);
        Assert.Contains("smithy's chest", tip.Label);
        Assert.Contains("8 silver", tip.Label); // six iron at 1, four coal at a half
        AvatarSystem.Perform(sim, me, tip);
        Assert.Equal(ore + 6, OreInTheSmithy(sim));
        Assert.Equal(had + 8, Coin.Count(sim, me));
        Assert.Equal(12, Coin.Count(sim, smith));
        Assert.Empty(Barrows.LoadOf(sim, Barrows.Held(sim, me)).Pieces);
        return 0;
    });

    [Fact]
    public void ASmithShortOfSilverOwesTheRestAndPaysItWhenTheyHaveIt() => WithMiner(() =>
    {
        var (sim, me, smith, chest) = AtTheSmithy(silver: 3);
        var had = Coin.Count(sim, me);
        AvatarSystem.Perform(sim, me, AvatarSystem.ActionAt(sim, me, sim.World.Get<Thing>(chest).Cell));
        Assert.Equal(had + 3, Coin.Count(sim, me));
        Assert.Equal(5, sim.System<OreTally>().OwedBy(smith, me));
        Assert.Equal(5, SaveLoadTests.RoundTrip(sim).System<OreTally>().OwedBy(smith, me));
        // The smith comes by some silver: the debt's paid.
        Inventory.Stow(sim, smith, sim.CreateItem(sim.Defs.Thing(Coin.Silver), 10));
        for (var t = 0; t < SimClock.TicksPerHour / 4 + 1; t++)
            sim.Tick();
        Assert.Equal(0, sim.System<OreTally>().OwedBy(smith, me));
        Assert.Equal(had + 8, Coin.Count(sim, me));
        return 0;
    });

    [Fact]
    public void TheHaulingLessonFindsABarrowLoadsItAndTipsItAtTheSmithy() => WithMiner(() =>
    {
        var (sim, me, smith, chest) = AtTheSmithy(silver: 20);
        var barrow = Barrows.Held(sim, me);
        sim.World.Get<Carrying>(me).Item = Entity.Null; // put it down for now, by the shed's others
        sim.PlaceItemNear(barrow, sim.World.Get<Position>(me).Cell);
        sim.Round.Practice = true;
        var trade = sim.Defs.Trade("Miner");
        var lesson = trade.Lessons.Single(l => l.Id == "haul");
        Assert.True(lesson.Core);
        var run = new HeroesNotIncluded.Sim.Scenarios.Lessons.LessonRun(sim, me, trade, lesson);
        HeroesNotIncluded.Sim.Scenarios.Lessons.LessonStage Stage() => HeroesNotIncluded.Sim.Scenarios.Lessons.Lessons.Stage(sim, me, run);

        var first = Stage();
        Assert.Equal("barrow", first.Step);
        Assert.Equal(barrow, first.Point); // the nearest
        var take = AvatarSystem.ActionAt(sim, me, sim.World.Get<Thing>(barrow).Cell);
        AvatarSystem.Perform(sim, me, take);
        Assert.Equal(barrow, Barrows.Held(sim, me));
        Assert.Equal(("wheel", chest), (Stage().Step, Stage().Point)); // it's loaded already
        Assert.False(Stage().Done);
        AvatarSystem.Perform(sim, me, AvatarSystem.ActionAt(sim, me, sim.World.Get<Thing>(chest).Cell));
        Assert.True(Stage().Done);
        return 0;
    });

    [Fact]
    public void ALoadingAndABarrowsLoadSurviveASave() => WithMiner(() =>
    {
        var (sim, me, live) = Hauling();
        var predictor = new WorkPredictor();
        for (var t = 0; t < 7; t++)
        {
            sim.Tick();
            predictor.Update(sim, me, 1, t % 2 == 1, false, 0, c => sim.ApplyNow(c), point: WorkSession.PackPoint(0.5f, 0.3f));
        }
        var s = (LoadSession)sim.World.Get<WorkSession>(me);
        Assert.True(s.Placed.Count > 0);
        var loaded = SaveLoadTests.RoundTrip(sim);
        var again = Assert.IsType<LoadSession>(loaded.World.Get<WorkSession>(me));
        Assert.Equal(s.Placed, again.Placed);
        Assert.Equal(s.ToLoad, again.ToLoad);
        Assert.Equal(s.Barrow, again.Barrow);
        // Finished, the barrow keeps its load through a save.
        Load(sim, me, Square);
        var pieces = sim.World.Get<BarrowLoad>(s.Barrow).Pieces;
        Assert.Equal(pieces, SaveLoadTests.RoundTrip(sim).World.Get<BarrowLoad>(s.Barrow).Pieces);
        return 0;
    });

    [Fact]
    public void TheMinerBotSoundsHewsAndWheelsOreToTheSmithyForPay() => WithMiner(() =>
    {
        var sim = VillageRound.NewSimulation(MinerDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Arin, "Arin", default));
        sim.ApplyNow(new SetTradePreferencesCommand(Arin, [sim.Defs.Trade("Miner")], []));
        var bot = new HeroesNotIncluded.Sim.Bots.BotPlayer(() => sim, Arin, c => sim.ApplyNow(c), 7) { Temper = HeroesNotIncluded.Sim.Bots.BotTemperament.Steady };
        sim.ApplyNow(new StartRoundCommand());
        sim.Round.Endless = true;
        var me = Players.CharacterOf(sim, Arin);
        // Whatever it finds (iron or coal) goes to the smithy; the carter doesn't come till tomorrow's morning.
        int Hewn() => HeroesNotIncluded.Sim.Systems.Stock.ChestOf(sim, ShopSystem.BuildingOf(sim, sim.Defs.Building("Smithy"))) is var chest
            ? sim.StoredIn(chest).Where(e => Barrows.Loadable(sim, sim.World.Get<Thing>(e).Def)).Sum(e => sim.World.Get<Stack>(e).Count) : 0;
        var ore = Hewn();
        var silver = Coin.Count(sim, me);
        bool Paid() => Coin.Count(sim, me) > silver && Hewn() > ore;
        for (var t = 0; t < SimClock.TicksPerHour * 8 && !Paid(); t++)
        {
            sim.Tick();
            bot.Tick();
        }
        Assert.True(Paid(), $"bot: {bot.Doing}; chalk {sim.System<ChalkMarks>().All.Count}\n" + string.Join("\n", sim.Messages.Messages.Where(m => m.Text.Contains("smith") || m.Text.Contains("barrow") || m.Text.Contains("silver")).TakeLast(12).Select(m => m.Text)));
        return 0;
    });

    [Fact]
    public void AMinerWhosePlayerLeftTakesTheHewnOreToTheSmithy() => WithMiner(() =>
    {
        var (sim, me) = Miner();
        sim.Round.Endless = true;
        var shed = ShopSystem.WorkplaceOf(sim, sim.Defs.Trade("Miner"));
        var at = sim.World.Get<PresetBuilding>(shed).Anchor;
        sim.PlaceItemNear(sim.CreateItem(sim.Defs.Thing("IronOre"), 8), at);
        var ore = OreInTheSmithy(sim);
        var silver = Coin.Count(sim, me);
        Players.Leave(sim, Arin);
        for (var t = 0; t < SimClock.TicksPerHour * 4 && OreInTheSmithy(sim) < ore + 8; t++)
            sim.Tick();
        Assert.Equal(ore + 8, OreInTheSmithy(sim));
        Assert.True(Coin.Count(sim, me) > silver, "paid for it");
        return 0;
    });

    /// <summary>Iron ore in the smithy's chest.</summary>
    private static int OreInTheSmithy(Simulation sim)
    {
        var smithy = ShopSystem.BuildingOf(sim, sim.Defs.Building("Smithy"));
        var chest = Buildings.Parts(sim, smithy).First(e => sim.World.Get<Thing>(e).Def.Name == "Chest");
        return sim.StoredIn(chest).Where(e => sim.World.Get<Thing>(e).Def.Name == "IronOre").Sum(e => sim.World.Get<Stack>(e).Count);
    }

    [Fact]
    public void WithAPlayerTheMinerTheCarterBringsLessOre() => WithMiner(() =>
    {
        var defs = MinerDefs();
        var load = defs.Season.OreDelivery.Single(t => t.Def == "IronOre").Count;
        var without = Round(defs, (Bea, "Blacksmith"));
        var before = OreInTheSmithy(without);
        OreCarterSystem.Deliver(without);
        Assert.Equal(before + load, OreInTheSmithy(without));

        var with = Round(defs, (Arin, "Miner"), (Bea, "Blacksmith"));
        before = OreInTheSmithy(with);
        OreCarterSystem.Deliver(with);
        Assert.Equal(before + (int)MathF.Round(load * defs.Season.SuppliedShare), OreInTheSmithy(with));
        return 0;
    });
}

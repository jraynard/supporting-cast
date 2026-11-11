using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Errands;
using SupportingCast.Sim.Healing;
using SupportingCast.Sim.Maps;
using SupportingCast.Sim.Round;
using SupportingCast.Sim.Saving;
using SupportingCast.Sim.Scenarios;
using SupportingCast.Sim.Systems;
using SupportingCast.Sim.Work;
using static SupportingCast.Sim.Tests.TestWorld;

namespace SupportingCast.Sim.Tests;

/// <summary>The empathic healer: drawing wounds, mending them with the right spell, meditating (Stage 5 of M2).</summary>
public class HealerTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    /// <summary>An empath (a player, Medicine 10) beside a villager with a cut on the arm.</summary>
    private static (Simulation Sim, Entity Me, Entity Patient, int Arm) AtTheBedside(float cut = 20f, int medicine = 10)
    {
        var sim = Create(40, 40);
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), "Eir", C(10, 10), ageDays: 25 * 365);
        var patient = sim.SpawnCreature(sim.Defs.Creature("Human"), "Torvi", C(11, 10), ageDays: 25 * 365);
        sim.Round.OpenLobby();
        sim.Round.Sit(Ana, "Eir", null, null, null);
        sim.Round.Start(sim);
        var spawned = new[] { me, patient };
        me = Players.CharacterOf(sim, Ana);
        patient = spawned.Single(e => e != me);
        sim.World.Get<Mind>(patient).Drafted = true; // lying still for it, as in a bed
        if (sim.World.Get<Position>(patient).Cell != C(11, 10) && sim.World.Get<Position>(me).Cell != C(10, 10))
            throw new InvalidOperationException("the two should be side by side");
        sim.World.Get<Skills>(me).Level[(int)Skill.Medicine] = medicine;
        sim.World.Add(me, new Mana { Current = 100, Max = 100 });
        var h = sim.World.Get<Health>(patient);
        var arm = h.Body.Parts.FindIndex(p => p.Label.Contains("arm") && !p.Vital);
        HealthSystem.Damage(sim, patient, cut, DamageType.Cut, arm);
        return (sim, me, patient, arm);
    }

    private static int CutKey(Simulation sim, Entity who, int part) =>
        Empathy.KeyOf(sim.World.Get<Health>(who).Injuries.Single(i => i.Part == part && i.Type == DamageType.Cut));

    /// <summary>Draw as a steady hand would (steering for the middle), or as a panicking one (pushing it out).</summary>
    private static void Draw(Simulation sim, Entity me, bool steady, int maxTicks = 60 * 120)
    {
        var predictor = new WorkPredictor();
        for (var t = 0; t < maxTicks && sim.World.Has<WorkSession>(me); t++)
        {
            sim.Tick();
            var axis = 1;
            if (steady && predictor.Session is DrawSession d)
            {
                var off = d.Composure - 0.5f;
                axis = MathF.Abs(off) > d.HalfWidth * 0.25f ? -Math.Sign(off) : 0;
            }
            predictor.Update(sim, me, 1, false, true, axis, c => sim.ApplyNow(c));
        }
        sim.Tick();
    }

    /// <summary>Play the runes perfectly, each on its mark.</summary>
    private static void Cast(Simulation sim, Entity me, int maxTicks = 60 * 60)
    {
        var predictor = new WorkPredictor();
        var lastAxis = 0;
        for (var t = 0; t < maxTicks && sim.World.Has<WorkSession>(me); t++)
        {
            sim.Tick();
            var press = false;
            var axis = 0;
            if (predictor.Session is CastSession c && c.Resolved < c.Mending.Runes.Length && c.StepsDone == c.DueOf(c.Resolved))
            {
                var rune = c.Mending.Runes[c.Resolved];
                press = rune == 'F';
                axis = rune == 'L' ? -1 : rune == 'R' ? 1 : 0;
                if (axis != 0 && axis == lastAxis)
                {
                    predictor.Update(sim, me, 0, false, false, 0, x => sim.ApplyNow(x)); // let go first
                }
            }
            lastAxis = axis;
            predictor.Update(sim, me, 1, press, false, axis, x => sim.ApplyNow(x));
        }
        sim.Tick();
    }

    [Fact]
    public void DrawingAWoundMovesItToTheSamePartOfTheHealerAtTheirBurden()
    {
        var (sim, me, patient, arm) = AtTheBedside();
        var burden = Empathy.Burden(sim, me);
        Assert.InRange(burden, 0.75f, 0.85f);
        sim.ApplyNow(new LayOnHandsCommand(me, patient, CutKey(sim, patient, arm)));
        Assert.True(sim.World.Has<WorkSession>(me), string.Join("; ", sim.Messages.Messages.Select(m => m.Text)));
        Draw(sim, me, steady: true);

        Assert.True(!sim.World.Get<Health>(patient).Injuries.Exists(i => i.Type == DamageType.Cut), $"ticks {sim.Clock.Tick}: " + string.Join("; ", sim.Messages.Messages.Select(m => m.Text)) + " fail " + sim.Ai.LastFailure.GetValueOrDefault(me));
        Assert.DoesNotContain(sim.World.Get<Health>(patient).Injuries, i => i.Type == DamageType.Cut);
        var taken = sim.World.Get<Health>(me).Injuries.Single(i => i.Part == arm && i.Type == DamageType.Cut);
        Assert.True(taken.Absorbed);
        Assert.Equal(20f * burden, taken.Damage, 0.5f);
    }

    [Fact]
    public void APatientKeepsStillWhileTheirWoundIsDrawn()
    {
        var (sim, me, patient, arm) = AtTheBedside();
        sim.World.Get<Mind>(patient).Drafted = false; // free to wander off, as an adventurer would
        sim.ApplyNow(new LayOnHandsCommand(me, patient, CutKey(sim, patient, arm)));
        var at = sim.World.Get<Position>(patient).Cell;
        Draw(sim, me, steady: true);

        Assert.Equal(at, sim.World.Get<Position>(patient).Cell);
        Assert.DoesNotContain(sim.World.Get<Health>(patient).Injuries, i => i.Type == DamageType.Cut);
    }

    [Fact]
    public void LosingYourComposureSnapsTheLinkWithABacklash()
    {
        var (sim, me, patient, arm) = AtTheBedside(cut: 18f);
        var before = sim.World.Get<Health>(patient).Injuries.Single(i => i.Type == DamageType.Cut).Damage;
        sim.ApplyNow(new LayOnHandsCommand(me, patient, CutKey(sim, patient, arm)));
        Draw(sim, me, steady: false);

        var left = sim.World.Get<Health>(patient).Injuries.Single(i => i.Type == DamageType.Cut).Damage;
        var drawn = before - left;
        Assert.True(left > 0, "only part of it came across");
        var mine = sim.World.Get<Health>(me).Injuries.Where(i => i.Part == arm).Sum(i => i.Damage);
        Assert.True(mine > drawn * Empathy.Burden(sim, me), $"the backlash adds to it: mine {mine}, drawn {drawn}, burden {Empathy.Burden(sim, me)}; {string.Join("; ", sim.Messages.Messages.Select(m => m.Text))}");
    }

    [Fact]
    public void AbsorbedWoundsAreKnownOnlyByTheirFeel()
    {
        var (sim, me, patient, arm) = AtTheBedside();
        Empathy.Draw(sim, me, patient, CutKey(sim, patient, arm), 1f, snapped: false);
        HealthSystem.Damage(sim, me, 5f, DamageType.Blunt, arm); // an ordinary bruise of your own

        var felt = Perception.For(sim, me).FeltWounds(me)!;
        var absorbed = felt.Single(f => f.Absorbed);
        Assert.Equal("a clean, bright line of pain", absorbed.Feels);
        Assert.Equal(DamageTypes.Wound(DamageType.Blunt), felt.Single(f => !f.Absorbed).Feels);
    }

    [Fact]
    public void TheRightMendingHealsAndTheWrongOneOnlyCostsMana()
    {
        var (sim, me, patient, arm) = AtTheBedside();
        Empathy.Draw(sim, me, patient, CutKey(sim, patient, arm), 1f, snapped: false);
        var key = CutKey(sim, me, arm);
        var before = Empathy.Find(sim, me, key)!.Damage;

        sim.ApplyNow(new CastMendingCommand(me, key, sim.Defs.Mending("Ease")));
        Cast(sim, me);
        Assert.Equal(100f - sim.Defs.Mending("Ease").ManaCost, sim.World.Get<Mana>(me).Current, 0.5f);
        Assert.Equal(before, Empathy.Find(sim, me, key)!.Damage, 0.2f); // natural healing aside

        sim.ApplyNow(new CastMendingCommand(me, key, sim.Defs.Mending("Knit")));
        Cast(sim, me);
        var after = Empathy.Find(sim, me, key)?.Damage ?? 0f;
        Assert.True(after < before - 10f, $"{before} -> {after}");
    }

    /// <summary>Meditate pressing on the rhythm you'd expect (a steady breath), as a person would.</summary>
    private static float MeditateByRhythm(float pain, bool atStone = false)
    {
        var (sim, me, _, _) = AtTheBedside();
        if (atStone)
        {
            sim.SpawnStructure(sim.Thing("MeditationStone"), C(10, 11));
            sim.RebuildDerivedIfNeeded();
        }
        sim.World.Get<Mana>(me).Current = 0;
        sim.ApplyNow(new MeditateCommand(me));
        var real = (MeditateSession)sim.World.Get<WorkSession>(me);
        Assert.Equal(atStone, real.Stillness > 0);
        real.Pain = pain; // as if sitting down in that much pain
        var predictor = new WorkPredictor();
        for (var t = 0; t < 60 * 120 && sim.World.Has<WorkSession>(me); t++)
        {
            sim.Tick();
            var press = predictor.Session is MeditateSession m && m.StepsDone == m.HalfStart + MeditateSession.HalfBreath;
            predictor.Update(sim, me, 1, press, false, 0, c => sim.ApplyNow(c));
        }
        sim.Tick();
        return sim.World.Get<Mana>(me).Current;
    }

    [Fact]
    public void MeditatingIsHarderWhileInPain()
    {
        var calm = MeditateByRhythm(0f);
        var hurting = MeditateByRhythm(0.9f);
        Assert.True(calm > 60f, $"a calm sitting fills most of it ({calm})");
        Assert.True(hurting < calm * 0.6f, $"in pain: {hurting} vs {calm}");
    }

    [Fact]
    public void MeditatingAtTheShrineStoneIsCalmerInPain()
    {
        var bare = MeditateByRhythm(0.9f);
        var atStone = MeditateByRhythm(0.9f, atStone: true);
        Assert.True(atStone > bare * 1.3f, $"at the stone {atStone} vs {bare}");
    }

    [Fact]
    public void TheShrineHasPalletsAStoneAndSupplies()
    {
        var sim = Simulation.CreateStandard(LoadDefs(), 160, 160, 1234, MapGenerator.RecommendedDepth);
        sim.Presets.PresetMode = true;
        var town = VillageRound.Setup(sim);
        Assert.False(town.Shrine.IsNull);
        var parts = sim.World.Query<PartOf>().Where(e => sim.World.Get<PartOf>(e).Building == town.Shrine).ToList();
        int Count(string def) => parts.Count(e => sim.World.Get<Thing>(e).Def.Name == def);
        Assert.Equal(1, Count("MeditationStone"));
        Assert.Equal(2, Count("StrawPallet"));
        var chest = parts.Single(e => sim.World.Get<Thing>(e).Def.Name == "Chest");
        Assert.Equal(VillageRound.ShrineBandages, sim.StoredIn(chest).Where(i => sim.World.Get<Thing>(i).Def.Name == "Bandage").Sum(i => sim.World.Get<Stack>(i).Count));
        Assert.Equal("Shrine", sim.Defs.Trade("Healer").Workplace);
    }

    [Fact]
    public void ManaComesBackOnItsOwnButSlowerInPain()
    {
        var (sim, me, patient, arm) = AtTheBedside();
        sim.World.Get<Mana>(me).Current = 0;
        sim.TickMany(SimClock.TicksPerHour);
        var rested = sim.World.Get<Mana>(me).Current;
        Assert.True(rested > 0);

        HealthSystem.Damage(sim, me, 25f, DamageType.Blunt, arm);
        sim.World.Get<Mana>(me).Current = 0;
        sim.TickMany(SimClock.TicksPerHour);
        Assert.True(sim.World.Get<Mana>(me).Current < rested);
    }

    [Fact]
    public void ACastInProgressSurvivesASave()
    {
        var (sim, me, patient, arm) = AtTheBedside();
        Empathy.Draw(sim, me, patient, CutKey(sim, patient, arm), 1f, snapped: false);
        sim.ApplyNow(new CastMendingCommand(me, CutKey(sim, me, arm), sim.Defs.Mending("Knit")));
        var s = (CastSession)sim.World.Get<WorkSession>(me);
        s.Advance([new WorkInput(s.DueOf(0), WorkInputKind.Axis, -1)], s.DueOf(0) + 5);

        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        var copy = (CastSession)loaded.World.Get<WorkSession>(me);
        Assert.Equal((s.Mending, s.Resolved, s.StepsDone, s.Hits[0]), (copy.Mending, copy.Resolved, copy.StepsDone, copy.Hits[0]));
    }

    // ---------------- Healing errands ----------------

    private static (Simulation Sim, Entity Hurt) ShrineWithAHurtAdventurer(string playerTrade)
    {
        var sim = Simulation.CreateStandard(LoadDefs(), 160, 160, 1234, MapGenerator.RecommendedDepth);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default, []));
        sim.ApplyNow(new SetTradePreferencesCommand(Ana, [sim.Defs.Trade(playerTrade)], []));
        sim.ApplyNow(new StartRoundCommand());
        var party = sim.Parties.Send(sim, sim.Defs.Party("GildedSpurs"));
        var hurt = party[0];
        var h = sim.World.Get<Health>(hurt);
        HealthSystem.Damage(sim, hurt, 15f, DamageType.Cut, h.Body.Parts.FindIndex(p => p.Label.Contains("arm") && !p.Vital));
        sim.TickMany(SimClock.TicksPerHour / 6);
        return (sim, hurt);
    }

    [Fact]
    public void AHealerWhoDrawsAnAdventurersWoundIsPaid()
    {
        var (sim, hurt) = ShrineWithAHurtAdventurer("Healer");
        var me = Players.CharacterOf(sim, Ana);
        Assert.True(Empathy.Gifted(sim, me), "the healer's trade brings the gift");
        var errand = sim.Errands.All.Single(e => e.IsOpen && e.Poster == hurt && e.Kind.Kind == ErrandKind.Healing);
        Assert.Contains(Perception.For(sim, me).Errands(), e => e.Id == errand.Id);
        sim.ApplyNow(new AcceptErrandCommand(me, errand.Id));

        var at = sim.World.Get<Position>(hurt).Cell;
        var cell = sim.SpiralFrom(at, 2).First(c => c != at && sim.Map.Passable(c) && c.Z == at.Z && Ai.Pathfinder.Touches(c, at));
        sim.World.Get<Position>(me).Cell = cell;
        sim.World.Get<Position>(me).Pos = Position.CenterOf(cell);
        var wounds = Perception.For(sim, me).FeltWounds(hurt)!;
        Assert.NotEmpty(wounds);
        var purse = Coin.Count(sim, me);
        foreach (var wound in wounds) // the cut, and any scrape from the road
            Empathy.Draw(sim, me, hurt, wound.Key, 1f, snapped: false);
        sim.TickMany(SimClock.TicksPerHour / 6);

        Assert.Equal(ErrandStatus.Done, errand.Status);
        Assert.Equal(purse + errand.Price, Coin.Count(sim, me));
    }

    [Fact]
    public void AnAIHealerBandagesAnAdventurerAndIsPaid()
    {
        var (sim, hurt) = ShrineWithAHurtAdventurer("Blacksmith");
        var healer = sim.Creatures().Single(e => sim.World.TryGet<Trade>(e, out var t) && t.Def.Name == "Healer");
        for (var i = 0; i < 16 && sim.Errands.All.Any(e => e.IsOpen && e.Kind.Kind == ErrandKind.Healing); i++)
            sim.TickMany(SimClock.TicksPerHour / 4);

        var errand = sim.Errands.All.Single(e => e.Poster == hurt && e.Kind.Kind == ErrandKind.Healing);
        Assert.Equal(ErrandStatus.Done, errand.Status);
        Assert.Equal(healer, errand.Assignee);
    }
}

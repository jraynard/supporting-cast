using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Healing;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>The empathic healer: drawing wounds, mending them with the right spell, meditating (Stage 5 of M2).</summary>
public class HealerTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    /// <summary>An empath (a player, Medicine 10) beside a villager with a cut on the arm, which they've felt (unless not).</summary>
    private static (Simulation Sim, Entity Me, Entity Patient, int Arm) AtTheBedside(float cut = 20f, int medicine = 10, bool felt = true)
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
        if (felt)
            FeelOver(sim, me, patient);
        return (sim, me, patient, arm);
    }

    /// <summary>Run the hands from head to feet, pressing in (slow and thorough) or skimming.</summary>
    private static void FeelOver(Simulation sim, Entity me, Entity patient, bool pressed = true)
    {
        sim.ApplyNow(new FeelCommand(me, patient));
        Assert.IsType<FeelSession>(sim.World.Get<WorkSession>(me));
        var predictor = new WorkPredictor();
        for (var t = 0; t < 60 * 60 && sim.World.Has<WorkSession>(me); t++)
        {
            sim.Tick();
            predictor.Update(sim, me, 1, false, pressed, 1, c => sim.ApplyNow(c));
        }
        sim.Tick();
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

    /// <summary>
    /// Weave these runes (by name), each keyed on a beat (or well before one), then let it go with F.
    /// Null runes: start the weave and leave it in hand.
    /// </summary>
    private static void Weave(Simulation sim, Entity me, int wound, string[] runes, bool onBeat = true, int maxTicks = 60 * 60)
    {
        sim.ApplyNow(new WeaveCommand(me, wound));
        var keys = runes.Select(n => sim.Defs.Rune(n).Index).ToList();
        var predictor = new WorkPredictor();
        for (var t = 0; t < maxTicks && sim.World.Has<WorkSession>(me); t++)
        {
            sim.Tick();
            var rune = -1;
            var press = false;
            if (predictor.Session is CastSession c)
            {
                if (c.Woven.Count >= keys.Count)
                    press = true;
                else if (c.StepsDone == (onBeat ? c.NextBeat : c.NextBeat - 20))
                    rune = keys[c.Woven.Count];
            }
            predictor.Update(sim, me, 1, press, false, 0, x => sim.ApplyNow(x), 0, rune);
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

    /// <summary>A draw of this kind held for <paramref name="steps"/>, the keys as given, its jolts all but gone (to see the pattern's own push).</summary>
    private static List<float> DrawPath(DrawPattern pattern, int steps, bool held, int steer, float gravity = 1f)
    {
        var s = new DrawSession { Pattern = pattern, Rate = 1e-6f, Gravity = gravity, Hands = new Hands(1f, 1.25f, 10) };
        s.Begin();
        var inputs = new List<WorkInput> { new(0, WorkInputKind.Hold, held ? 1 : 0) };
        if (steer != 0)
            inputs.Add(new WorkInput(0, WorkInputKind.Axis, steer));
        var path = new List<float>();
        for (var i = 0; i < steps && !s.Done; i++)
        {
            s.Advance(i == 0 ? inputs : [], i + 1);
            path.Add(s.Composure);
        }
        return path;
    }

    [Fact]
    public void FrostNumbsTheHandsSoSteeringAnswersLate()
    {
        var plain = DrawPath(DrawPattern.Spike, 12, held: false, steer: 1);
        var numb = DrawPath(DrawPattern.Numb, 12, held: false, steer: 1);
        Assert.True(plain[^1] - 0.5f > 0.1f, $"plain hands move it ({plain[^1]})");
        Assert.True(numb[^1] - 0.5f < (plain[^1] - 0.5f) * 0.5f, $"numb ones barely ({numb[^1]} vs {plain[^1]})");
    }

    [Fact]
    public void ABurnPullsOneWayAndABruiseSwellsAndEbbs()
    {
        var burn = DrawPath(DrawPattern.Drift, 120, held: true, steer: 0);
        var off = burn.Select(c => c - 0.5f).ToList();
        Assert.True(MathF.Abs(off[^1]) > 0.1f, $"a burn pulls the needle away ({off[^1]})");
        Assert.All(off.Skip(1), o => Assert.Equal(Math.Sign(off[^1]), Math.Sign(o))); // and always the same way

        var period = (int)(2.6f * SimClock.TicksPerSecond);
        var bruise = DrawPath(DrawPattern.Wave, period, held: true, steer: 0);
        Assert.True(bruise.Max(c => MathF.Abs(c - 0.5f)) > 0.1f, "a bruise swells the needle off the middle");
        Assert.True(MathF.Abs(bruise[^1] - 0.5f) < 0.03f, $"and it ebbs back by itself ({bruise[^1]})");

        var still = DrawPath(DrawPattern.Spike, 120, held: true, steer: 0);
        Assert.All(still, c => Assert.Equal(0.5f, c, 0.001f)); // a cut only jolts
    }

    [Fact]
    public void ABedOrASteadyingMendingWidensTheCalm()
    {
        var hands = new Hands(1f, 1f, 0);
        var plain = new DrawSession { Hands = hands }.HalfWidth;
        Assert.True(new DrawSession { Hands = hands, Bedside = true }.HalfWidth > plain * 1.2f);
        Assert.True(new DrawSession { Hands = hands, Steady = 0.5f }.HalfWidth > plain * 1.4f);
    }

    [Fact]
    public void EachKindOfWoundDrawsItsOwnWay()
    {
        var (sim, me, patient, arm) = AtTheBedside();
        HealthSystem.Damage(sim, patient, 10f, DamageType.Burn, arm);
        var burn = Empathy.KeyOf(sim.World.Get<Health>(patient).Injuries.Single(i => i.Type == DamageType.Burn));
        FeelOver(sim, me, patient);
        sim.ApplyNow(new LayOnHandsCommand(me, patient, burn));
        var s = (DrawSession)sim.World.Get<WorkSession>(me);
        Assert.Equal(DrawPattern.Drift, s.Pattern);
        s.Advance([new WorkInput(0, WorkInputKind.Hold, 1)], 30);

        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        var copy = (DrawSession)loaded.World.Get<WorkSession>(me);
        Assert.Equal((s.Pattern, s.Composure, s.Bedside, s.Steady), (copy.Pattern, copy.Composure, copy.Bedside, copy.Steady));
    }

    [Fact]
    public void AWoundIsKnownAndDrawnOnlyOnceItsBeenFelt()
    {
        var (sim, me, patient, arm) = AtTheBedside(felt: false);
        var view = Perception.For(sim, me);
        Assert.Empty(view.FeltWounds(patient)!);
        var key = Empathy.KeyOf(sim.World.Get<Health>(patient).Injuries.Single());
        Assert.NotNull(DrawSession.Problem(sim, me, patient, key)); // no drawing what you haven't felt

        FeelOver(sim, me, patient, pressed: false);
        var felt = Perception.For(sim, me).FeltWounds(patient)!;
        Assert.Equal(key, felt.Single().Key);
        Assert.Equal("arm", felt.Single().Part.Split(' ')[^1]);
        Assert.Null(DrawSession.Problem(sim, me, patient, key));
    }

    [Fact]
    public void OnlyAPressedPassFindsFesteringUnderADressing()
    {
        bool Finds(bool pressed)
        {
            var (sim, me, patient, _) = AtTheBedside(felt: false);
            var inj = sim.World.Get<Health>(patient).Injuries.Single();
            inj.Tended = true;
            inj.Infection = new Infection { Severity = 0.3f };
            FeelOver(sim, me, patient, pressed);
            var felt = Perception.For(sim, me).FeltWounds(patient)!.Single();
            return felt.Festering;
        }
        Assert.False(Finds(pressed: false));
        Assert.True(Finds(pressed: true));
    }

    [Fact]
    public void APatientKeepsStillWhileFeltOverAndAFeelSurvivesASave()
    {
        var (sim, me, patient, _) = AtTheBedside(felt: false);
        sim.World.Get<Mind>(patient).Drafted = false;
        sim.ApplyNow(new FeelCommand(me, patient));
        sim.TickMany(30);
        Assert.Equal(me, Empathy.DrawnBy(sim, patient));
        var s = (FeelSession)sim.World.Get<WorkSession>(me);
        s.Advance([new WorkInput(s.StepsDone, WorkInputKind.Hold, 1), new WorkInput(s.StepsDone, WorkInputKind.Axis, 1)], s.StepsDone + 40);

        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        var copy = (FeelSession)loaded.World.Get<WorkSession>(me);
        Assert.Equal((s.Hands01, s.Spots.Count, s.Spots[0].Found, s.Parts.Count), (copy.Hands01, copy.Spots.Count, copy.Spots[0].Found, copy.Parts.Count));
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

        Weave(sim, me, key, ["Still", "Flesh"]); // easing: for a bruise
        Assert.Equal(100f - sim.Defs.Mending("Ease").ManaCost, sim.World.Get<Mana>(me).Current, 0.5f);
        Assert.Equal(before, Empathy.Find(sim, me, key)!.Damage, 0.2f); // natural healing aside

        Weave(sim, me, key, ["Flesh", "Thread"]); // knitting, in either order
        var after = Empathy.Find(sim, me, key)?.Damage ?? 0f;
        Assert.True(after < before - 10f, $"{before} -> {after}");
    }

    [Fact]
    public void AnExtraLeadRuneHealsMoreForMoreMana()
    {
        float Mend(string[] runes, out float spent)
        {
            var (sim, me, _, _) = AtTheBedside();
            // A deep cut on the healer's biggest limb: more than either cast can close.
            var h = sim.World.Get<Health>(me);
            var limb = Enumerable.Range(0, h.Body.Parts.Count).Where(i => !h.Body.Parts[i].Vital).MaxBy(h.PartMaxHp);
            HealthSystem.Damage(sim, me, h.PartMaxHp(limb) * 0.8f, DamageType.Cut, limb);
            var key = CutKey(sim, me, limb);
            var before = Empathy.Find(sim, me, key)!.Damage;
            Assert.True(before > 18f, $"the wound should outlast a plain cast ({before})");
            Weave(sim, me, key, runes);
            spent = 100f - sim.World.Get<Mana>(me).Current;
            return before - (Empathy.Find(sim, me, key)?.Damage ?? 0f);
        }
        var plain = Mend(["Thread", "Flesh"], out var plainCost);
        var strong = Mend(["Thread", "Flesh", "Thread"], out var strongCost);
        Assert.True(strong > plain + 3f, $"{plain} -> {strong}");
        Assert.True(strongCost > plainCost + 5f, $"{plainCost} -> {strongCost}");
        // Any other extra rune spoils the mix.
        var (sim2, _, _, _) = AtTheBedside();
        Assert.Null(Weaving.Match(sim2.Defs, new[] { "Thread", "Flesh", "Flesh" }.Select(sim2.Defs.Rune).ToList()));
    }

    [Fact]
    public void AWeaveThatMakesNothingFizzlesForALittleMana()
    {
        var (sim, me, patient, arm) = AtTheBedside();
        Empathy.Draw(sim, me, patient, CutKey(sim, patient, arm), 1f, snapped: false);
        var key = CutKey(sim, me, arm);
        var before = Empathy.Find(sim, me, key)!.Damage;
        Weave(sim, me, key, ["Salt", "Rime", "Thread"]);
        Assert.Equal(100f - 3 * Weaving.FizzlePerRune, sim.World.Get<Mana>(me).Current, 0.5f);
        Assert.Equal(before, Empathy.Find(sim, me, key)!.Damage, 0.2f);
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("fizzles"));
    }

    [Fact]
    public void AMixThatWorksIsWrittenIntoTheBook()
    {
        var (sim, me, _, _) = AtTheBedside();
        sim.World.Add(me, Weaving.NewBook(sim));
        var steady = sim.Defs.Mending("Steady");
        Assert.False(Weaving.Knows(sim, me, steady));
        Assert.Equal(0f, Empathy.SteadiedBand(sim, me));

        Weave(sim, me, 0, ["Still", "Still"]); // needs no wound
        Assert.True(Weaving.Knows(sim, me, steady));
        Assert.True(Empathy.SteadiedBand(sim, me) > 0.2f);
        sim.TickMany(SimClock.HoursToTicks(steady.SteadyHours) + 60);
        Assert.Equal(0f, Empathy.SteadiedBand(sim, me));
    }

    [Fact]
    public void TheDealGivesAHealerTheirBook()
    {
        var (sim, me, _, _) = AtTheBedside();
        RoundSystem.Assign(sim, me, sim.Defs.Trade("Healer"));
        Assert.True(Weaving.Knows(sim, me, sim.Defs.Mending("Knit")));
        Assert.False(Weaving.Knows(sim, me, sim.Defs.Mending("Sear")));
    }

    [Fact]
    public void LettingAWeaveGoCostsNothing()
    {
        var (sim, me, _, _) = AtTheBedside();
        sim.ApplyNow(new WeaveCommand(me, 0));
        var s = (CastSession)sim.World.Get<WorkSession>(me);
        s.Advance([new WorkInput(s.NextBeat, WorkInputKind.Rune, sim.Defs.Rune("Still").Index)], s.NextBeat + 5);
        sim.ApplyNow(new StopWorkCommand(me));
        Assert.False(sim.World.Has<WorkSession>(me));
        Assert.Equal(100f, sim.World.Get<Mana>(me).Current, 0.01f);
    }

    [Fact]
    public void RunesOffTheBeatMakeAWeakerMending()
    {
        float Mend(bool onBeat)
        {
            var (sim, me, patient, arm) = AtTheBedside(cut: 40f);
            Empathy.Draw(sim, me, patient, CutKey(sim, patient, arm), 1f, snapped: false);
            var key = CutKey(sim, me, arm);
            var before = Empathy.Find(sim, me, key)!.Damage;
            Weave(sim, me, key, ["Thread", "Flesh"], onBeat);
            return before - (Empathy.Find(sim, me, key)?.Damage ?? 0f);
        }
        var good = Mend(true);
        var poor = Mend(false);
        Assert.True(poor < good * 0.6f, $"on the beat {good}, off it {poor}");
    }

    [Fact]
    public void PainMakesTheHeartRaceAndStumble()
    {
        List<int> Beats(float pain)
        {
            var s = new CastSession { Pain = pain };
            s.Begin();
            var beats = new List<int>();
            var last = s.NextBeat;
            for (var step = 0; step < 60 * 20; step++)
            {
                s.Advance([], step + 1);
                if (s.NextBeat != last)
                {
                    beats.Add(s.NextBeat - last);
                    last = s.NextBeat;
                }
            }
            return beats;
        }
        var calm = Beats(0f);
        var hurting = Beats(0.9f);
        Assert.Single(calm.Distinct());
        Assert.True(hurting.Average() < calm.Average(), "faster");
        Assert.True(hurting.Distinct().Count() > 3, "and uneven");
    }

    [Fact]
    public void NoWeaveMakesTwoMendings()
    {
        // Checked at load (Weaving.Validate); this guards the defs as they're written.
        var defs = LoadDefs();
        foreach (var m in defs.AllMendings)
            Assert.Equal(m, Weaving.Match(defs, Weaving.Recipe(m))?.Mending);
    }

    /// <summary>Meditate as a careful person would: toward the centre by what they've seen. Mana after, and whether they reached it.</summary>
    private static (float Mana, bool Centred) MeditateByWalking(float pain, bool atStone = false, int seed = 0)
    {
        var (sim, me, _, _) = AtTheBedside();
        if (atStone)
        {
            sim.SpawnStructure(sim.Thing("MeditationStone"), C(10, 11));
            sim.RebuildDerivedIfNeeded();
        }
        sim.TickMany(seed); // a different labyrinth
        sim.World.Get<Mana>(me).Current = 0;
        sim.ApplyNow(new MeditateCommand(me));
        var real = (MeditateSession)sim.World.Get<WorkSession>(me);
        Assert.Equal(atStone, real.Stillness > 0);
        real.Pain = pain; // as if sitting down in that much pain
        var predictor = new WorkPredictor();
        var centred = false;
        for (var t = 0; t < MeditateSession.MaxSteps + 600 && sim.World.Has<WorkSession>(me); t++)
        {
            sim.Tick();
            var (axis, axis2) = (0, 0);
            if (predictor.Session is MeditateSession m)
            {
                var way = m.Heading();
                (axis, axis2) = (way == 1 ? 1 : way == 3 ? -1 : 0, way == 0 ? -1 : way == 2 ? 1 : 0);
                centred = m.Centred;
            }
            predictor.Update(sim, me, 1, false, false, axis, c => sim.ApplyNow(c), axis2);
        }
        sim.Tick();
        centred |= sim.Messages.Messages.Any(x => x.Text.Contains("still centre"));
        return (sim.World.Get<Mana>(me).Current, centred);
    }

    /// <summary>The average over a few labyrinths, so one easy or hard one doesn't decide it.</summary>
    private static float MeditateAverage(float pain, bool atStone = false) =>
        Enumerable.Range(0, 4).Average(i => MeditateByWalking(pain, atStone, seed: i * 7).Mana);

    [Fact]
    public void ACalmMindFindsTheCentre()
    {
        var (mana, centred) = MeditateByWalking(0f);
        Assert.True(centred, "a calm sitting reaches the still centre");
        Assert.True(mana > 60f, $"and fills most of it ({mana})");
    }

    [Fact]
    public void MeditatingIsHarderWhileInPain()
    {
        var calm = MeditateAverage(0f);
        var hurting = MeditateAverage(0.9f);
        Assert.True(hurting < calm * 0.8f, $"in pain: {hurting} vs {calm}");
    }

    [Fact]
    public void MeditatingAtTheShrineStoneIsCalmerInPain()
    {
        var bare = MeditateAverage(0.9f);
        var atStone = MeditateAverage(0.9f, atStone: true);
        Assert.True(atStone > bare * 1.2f, $"at the stone {atStone} vs {bare}");
    }

    [Fact]
    public void EachSittingIsAWholeLabyrinthWithOneWayBetweenAnyTwoPlaces()
    {
        int[] Walls(ulong seed)
        {
            var s = new MeditateSession { Hands = new Hands(1f, 1f, 0) };
            s.Rand = new SimRandom(seed);
            s.Begin();
            // A perfect maze: every place reachable, and exactly places - 1 openings between them.
            var openings = 0;
            for (var i = 0; i < s.Open.Length; i++)
                openings += System.Numerics.BitOperations.PopCount(s.Open[i]);
            Assert.Equal((MeditateSession.Size * MeditateSession.Size - 1) * 2, openings);
            var reached = new HashSet<int> { 0 };
            var queue = new Queue<int>([0]);
            while (queue.Count > 0)
            {
                var here = queue.Dequeue();
                for (var d = 0; d < 4; d++)
                    if (s.CanGo(here % MeditateSession.Size, here / MeditateSession.Size, d)
                        && MeditateSession.Step(here % MeditateSession.Size, here / MeditateSession.Size, d) is var (x, y)
                        && reached.Add(MeditateSession.Index(x, y)))
                        queue.Enqueue(MeditateSession.Index(x, y));
            }
            Assert.Equal(s.Open.Length, reached.Count);
            Assert.True(s.X is 0 or MeditateSession.Size - 1 && s.Y is 0 or MeditateSession.Size - 1, "they start at a corner");
            return s.Open.Select(b => (int)b).ToArray();
        }
        Assert.Equal(Walls(3), Walls(3));
        Assert.NotEqual(Walls(3), Walls(4));
    }

    [Fact]
    public void PainNarrowsTheSightAndHoldingStillWidensIt()
    {
        var calm = new MeditateSession { Pain = 0f };
        var hurting = new MeditateSession { Pain = 0.9f };
        Assert.True(hurting.SightNow < calm.SightNow * 0.7f);
        Assert.True(hurting.StepLength > calm.StepLength * 1.5f);
        var still = new MeditateSession { Pain = 0f, Hands = new Hands(1f, 1f, 0) };
        still.Rand = new SimRandom(1);
        still.Begin();
        still.Advance([new WorkInput(0, WorkInputKind.Hold, 1)], 1);
        Assert.True(still.SightNow > calm.SightNow + 1f);
    }

    [Fact]
    public void ASittingSurvivesASave()
    {
        var (sim, me, _, _) = AtTheBedside();
        sim.World.Get<Mana>(me).Current = 0;
        sim.ApplyNow(new MeditateCommand(me));
        var s = (MeditateSession)sim.World.Get<WorkSession>(me);
        s.Advance([new WorkInput(0, WorkInputKind.Axis, 1), new WorkInput(0, WorkInputKind.Axis2, 1)], 120);

        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        var copy = (MeditateSession)loaded.World.Get<WorkSession>(me);
        Assert.Equal((s.X, s.Y, s.Gained, s.Trail.Count), (copy.X, copy.Y, copy.Gained, copy.Trail.Count));
        Assert.Equal(s.Open, copy.Open);
        Assert.Equal(s.Seen, copy.Seen);
    }

    [Fact]
    public void TheShrineHasPalletsAStoneAndSupplies()
    {
        var sim = VillageRound.NewSimulation(LoadDefs(), 1234);
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
    public void ThePalletsAreKeptForTheHurtNotForANightsSleep()
    {
        var sim = VillageRound.NewSimulation(LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        var town = VillageRound.Setup(sim);
        var w = sim.World;
        var pallet = w.Query<PartOf>().First(e => w.Get<PartOf>(e).Building == town.Shrine && w.Get<Thing>(e).Def.Name == "StrawPallet");
        // Someone with no bed of their own, by the shrine at bedtime.
        var drifter = sim.Creatures().First(e => w.Get<Creature>(e) is { Faction: Faction.Village, Def.Race: Race.Human });
        w.Remove<BedOwner>(drifter);
        w.Get<Position>(drifter) = Position.At(w.Get<Thing>(pallet).Cell);
        Assert.False(Ai.AiQueries.FindFreeBed(sim, drifter, forTheNight: true) is var bed && w.IsAlive(bed) && ShopSystem.IsForPatients(sim, bed));
        // Carried in hurt, though, a pallet is just the place.
        Assert.True(ShopSystem.IsForPatients(sim, Ai.AiQueries.FindFreeBed(sim, drifter)));
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
        sim.World.Add(me, Weaving.NewBook(sim));
        sim.ApplyNow(new WeaveCommand(me, CutKey(sim, me, arm)));
        var s = (CastSession)sim.World.Get<WorkSession>(me);
        s.Advance([new WorkInput(s.NextBeat, WorkInputKind.Rune, sim.Defs.Rune("Thread").Index)], s.NextBeat + 5);

        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        var copy = (CastSession)loaded.World.Get<WorkSession>(me);
        Assert.Equal((s.Wound, s.StepsDone, s.NextBeat, s.Woven[0], s.Strong[0], s.Palette.Count),
            (copy.Wound, copy.StepsDone, copy.NextBeat, copy.Woven[0], copy.Strong[0], copy.Palette.Count));
        Assert.Equal(sim.World.Get<Spellbook>(me).Known.Count, loaded.World.Get<Spellbook>(me).Known.Count);
    }

    // ---------------- Healing orders ----------------

    private static (Simulation Sim, Entity Hurt) ShrineWithAHurtAdventurer(string playerTrade)
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
        var order = sim.Shops.All.Single(e => e.IsOpen && e.Poster == hurt && e.Kind.Kind == ServiceKind.Healing);
        Assert.Contains(Perception.For(sim, me).Orders(), e => e.Id == order.Id);
        sim.ApplyNow(new TakeOrderCommand(me, order.Id));

        var at = sim.World.Get<Position>(hurt).Cell;
        var cell = sim.SpiralFrom(at, 2).First(c => c != at && sim.Map.Passable(c) && c.Z == at.Z && Ai.Pathfinder.Touches(c, at));
        sim.World.Get<Position>(me).Cell = cell;
        sim.World.Get<Position>(me).Pos = Position.CenterOf(cell);
        Assert.Empty(Perception.For(sim, me).FeltWounds(hurt)!); // not till their hands have been over them
        FeelOver(sim, me, hurt);
        var wounds = Perception.For(sim, me).FeltWounds(hurt)!;
        Assert.NotEmpty(wounds);
        var purse = Coin.Count(sim, me);
        foreach (var wound in wounds) // the cut, and any scrape from the road
            Empathy.Draw(sim, me, hurt, wound.Key, 1f, snapped: false);
        sim.TickMany(SimClock.TicksPerHour / 6);

        Assert.Equal(OrderStatus.Done, order.Status);
        Assert.Equal(purse + order.Price, Coin.Count(sim, me));
    }

    [Fact]
    public void AnAIHealerBandagesAnAdventurerAndIsPaid()
    {
        var (sim, hurt) = ShrineWithAHurtAdventurer("Blacksmith");
        var healer = sim.Creatures().Single(e => sim.World.TryGet<Trade>(e, out var t) && t.Def.Name == "Healer");
        for (var i = 0; i < 16 && sim.Shops.All.Any(e => e.IsOpen && e.Kind.Kind == ServiceKind.Healing); i++)
            sim.TickMany(SimClock.TicksPerHour / 4);

        var order = sim.Shops.All.Single(e => e.Poster == hurt && e.Kind.Kind == ServiceKind.Healing);
        Assert.Equal(OrderStatus.Done, order.Status);
        Assert.Equal(healer, order.Assignee);
    }

    [Fact]
    public void ABotHealerDrawsAnAdventurersWoundAndWeavesItAway()
    {
        var (sim, hurt) = ShrineWithAHurtAdventurer("Healer");
        var me = Players.CharacterOf(sim, Ana);
        var bot = new Bots.BotPlayer(() => sim, Ana, c => sim.ApplyNow(c), 11);
        var order = sim.Shops.All.Single(e => e.IsOpen && e.Poster == hurt && e.Kind.Kind == ServiceKind.Healing);
        var wove = false;
        for (var t = 0; t < SimClock.TicksPerHour * 6; t++)
        {
            sim.Tick();
            bot.Tick();
            wove |= sim.World.TryGet<WorkSession>(me, out var s) && s is CastSession;
            if (wove && order.Status == OrderStatus.Done && !sim.World.Get<Health>(me).Injuries.Exists(i => i.Absorbed))
                break;
        }
        var log = $"bot {bot.Doing}\n" + string.Join("\n", sim.Messages.Messages.TakeLast(12).Select(m => m.Text));
        Assert.True(order.Status == OrderStatus.Done, $"{order.Status}: {log}");
        Assert.True(wove, "it wove a mending: " + log);
        Assert.DoesNotContain(sim.Messages.Messages, m => m.Text.Contains("fizzles"));
        Assert.False(sim.World.Get<Health>(me).Injuries.Exists(i => i.Absorbed), "it mended what it took: " + log);
    }

    [Fact]
    public void TheWoundedLieOnTheShrinesPalletsAndAreDrawnFromAtTheBedside()
    {
        var (sim, hurt) = ShrineWithAHurtAdventurer("Healer");
        var order = sim.Shops.All.Single(e => e.IsOpen && e.Poster == hurt && e.Kind.Kind == ServiceKind.Healing);
        Assert.Equal(2, ShopSystem.BedsOf(sim, order.Where).Count);
        var cells = ShopSystem.LineCells(sim, order.Where);
        Assert.Equal(ShopSystem.BedsOf(sim, order.Where).Select(b => sim.World.Get<Thing>(b).Cell), cells.Take(2));

        for (var t = 0; t < SimClock.TicksPerHour * 2 && !HealthSystem.InBed(sim, hurt); t++)
            sim.Tick();
        Assert.True(HealthSystem.InBed(sim, hurt), $"lying down: {sim.World.Get<Mind>(hurt).Job?.Report}");
        sim.TickMany(60);
        Assert.True(sim.World.Get<Mind>(hurt).Job is { LiesDown: true }, sim.World.Get<Mind>(hurt).Job?.Report);

        var me = Players.CharacterOf(sim, Ana);
        var at = sim.World.Get<Position>(hurt).Cell;
        var cell = sim.SpiralFrom(at, 2).First(c => c != at && sim.Map.Passable(c) && c.Z == at.Z && Ai.Pathfinder.Touches(c, at)
            && sim.Map.Structure[sim.Map.Idx(c)].IsNull);
        sim.World.Get<Position>(me).Cell = cell;
        sim.World.Get<Position>(me).Pos = Position.CenterOf(cell);
        FeelOver(sim, me, hurt);
        var wound = Perception.For(sim, me).FeltWounds(hurt)!.First();
        sim.ApplyNow(new LayOnHandsCommand(me, hurt, wound.Key));
        Assert.True(((DrawSession)sim.World.Get<WorkSession>(me)).Bedside);
    }
}

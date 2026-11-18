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
public class EmpathTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    /// <summary>An empath (a player, Medicine 10) beside a villager with a cut on the arm, which they've felt (unless not).</summary>
    private static (Simulation Sim, Entity Me, Entity Patient, int Arm) AtTheBedside(float cut = 20f, int medicine = 10, bool felt = true)
    {
        var sim = Create(40, 40);
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), "Eir", C(10, 10), ageDays: 25 * 365);
        var patient = sim.SpawnCreature(sim.Defs.Creature("Human"), "Torvi", C(11, 10), ageDays: 25 * 365);
        sim.Round.OpenLobby();
        sim.Round.Sit(Ana, "Eir", null, null);
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

    /// <summary>
    /// Lay hands on them and read them over without drawing: skim head to feet, stopping on each
    /// warmth to press it until it's read (or for at most <paramref name="press"/> steps), then on.
    /// <paramref name="read"/> false: a skim only, never pressing.
    /// </summary>
    private static void FeelOver(Simulation sim, Entity me, Entity patient, bool read = true, int press = int.MaxValue)
    {
        sim.ApplyNow(new FeelCommand(me, patient));
        Assert.IsType<HandsOnSession>(sim.World.Get<WorkSession>(me));
        var predictor = new WorkPredictor();
        var pressed = new Dictionary<int, int>();
        for (var t = 0; t < 60 * 60 && sim.World.Has<WorkSession>(me); t++)
        {
            sim.Tick();
            var hold = false;
            if (read && predictor.Session is HandsOnSession h && h.Under >= 0 && h.Spots[h.Under] is var s && !h.Read(s) && s.Sensed > 0f)
            {
                pressed[s.Key] = pressed.GetValueOrDefault(s.Key) + 1;
                hold = pressed[s.Key] <= press;
            }
            predictor.Update(sim, me, 1, false, hold, hold ? 0 : 1, c => sim.ApplyNow(c));
        }
        if (sim.World.Has<WorkSession>(me)) // warmths left unread at the feet: take the hands away
            sim.ApplyNow(new StopWorkCommand(me));
        sim.Tick();
    }

    private static int CutKey(Simulation sim, Entity who, int part) =>
        Empathy.KeyOf(sim.World.Get<Health>(who).Injuries.Single(i => i.Part == part && i.Type == DamageType.Cut));

    /// <summary>
    /// Lay on hands as a steady hand would (reading each warmth, drawing it while steering for the
    /// middle, then on to the next), or as a panicking one (pushing the needle out once linked).
    /// </summary>
    private static void Draw(Simulation sim, Entity me, bool steady, int maxTicks = 60 * 120)
    {
        var predictor = new WorkPredictor();
        for (var t = 0; t < maxTicks && sim.World.Has<WorkSession>(me); t++)
        {
            sim.Tick();
            var (held, axis) = (false, 1);
            if (predictor.Session is HandsOnSession h)
            {
                if (h.Linked)
                {
                    var off = h.Link.Composure - 0.5f;
                    (held, axis) = (true, !steady ? 1 : MathF.Abs(off) > h.Link.HalfWidth * 0.25f ? -Math.Sign(off) : 0);
                }
                else if (h.Under >= 0 && h.Spots[h.Under] is { Sensed: > 0f, Drawn: < 1f })
                    (held, axis) = (!h.Held || h.Reading, 0);
            }
            predictor.Update(sim, me, 1, false, held, axis, c => sim.ApplyNow(c));
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
    public void DrawingAWoundMovesItToTheSamePartOfTheEmpathAtTheirBurden()
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

    /// <summary>A draw of this kind held open for <paramref name="steps"/>, the keys as given, its jolts all but gone (to see the pattern's own push).</summary>
    private static List<float> DrawPath(DrawPattern pattern, int steps, bool held, int steer, float gravity = 1f)
    {
        var rand = new SimRandom(1);
        var link = new DrawLink { Tolerance = 1.25f };
        link.Take(pattern, 1e-6f, 1f, gravity, rand);
        var path = new List<float>();
        for (var i = 0; i < steps && !link.Snapped; i++)
        {
            link.Step(rand, held, steer, 0f, 0f, i);
            path.Add(link.Composure);
        }
        return path;
    }

    [Fact]
    public void FrostNumbsTheHandsSoSteeringAnswersLate()
    {
        var plain = DrawPath(DrawPattern.Spike, 16, held: true, steer: 1);
        var numb = DrawPath(DrawPattern.Numb, 16, held: true, steer: 1);
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
        var plain = new DrawLink { Tolerance = hands.Tolerance }.HalfWidth;
        Assert.True(new DrawLink { Tolerance = hands.Tolerance, Bedside = true }.HalfWidth > plain * 1.2f);
        Assert.True(new DrawLink { Tolerance = hands.Tolerance, Steady = 0.5f }.HalfWidth > plain * 1.4f);
    }

    [Fact]
    public void EachKindOfWoundDrawsItsOwnWay()
    {
        var (sim, me, patient, arm) = AtTheBedside();
        HealthSystem.Damage(sim, patient, 10f, DamageType.Burn, arm);
        var burn = Empathy.KeyOf(sim.World.Get<Health>(patient).Injuries.Single(i => i.Type == DamageType.Burn));
        FeelOver(sim, me, patient);
        sim.ApplyNow(new LayOnHandsCommand(me, patient, burn));
        var s = (HandsOnSession)sim.World.Get<WorkSession>(me);
        s.Advance([new WorkInput(0, WorkInputKind.Hold, 1)], 30);
        Assert.True(s.Linked, "the hands start on the burn, read before: pressing opens the link");
        Assert.Equal(DrawPattern.Drift, s.Link.Pattern);

        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        var copy = (HandsOnSession)loaded.World.Get<WorkSession>(me);
        Assert.Equal((s.Link.Pattern, s.Link.Composure, s.Link.Bedside, s.Link.Steady, s.Linked, s.Spots[s.Under].Drawn),
            (copy.Link.Pattern, copy.Link.Composure, copy.Link.Bedside, copy.Link.Steady, copy.Linked, copy.Spots[copy.Under].Drawn));
    }

    [Fact]
    public void ASkimSensesAWarmthButOnlyAStillPressReadsIt()
    {
        var (sim, me, patient, _) = AtTheBedside(felt: false);
        Assert.Empty(Perception.For(sim, me).FeltWounds(patient)!);
        var key = Empathy.KeyOf(sim.World.Get<Health>(patient).Injuries.Single());

        FeelOver(sim, me, patient, read: false);
        Assert.Empty(Perception.For(sim, me).FeltWounds(patient)!); // a warmth, no more

        FeelOver(sim, me, patient);
        var felt = Perception.For(sim, me).FeltWounds(patient)!;
        Assert.Equal(key, felt.Single().Key);
        Assert.Equal("arm", felt.Single().Part.Split(' ')[^1]);
    }

    [Fact]
    public void AWoundNotYetReadCantBeDrawn()
    {
        var (sim, me, patient, arm) = AtTheBedside(felt: false);
        sim.ApplyNow(new LayOnHandsCommand(me, patient, CutKey(sim, patient, arm)));
        var s = (HandsOnSession)sim.World.Get<WorkSession>(me);
        s.Advance([new WorkInput(0, WorkInputKind.Hold, 1)], HandsOnSession.ReadSteps / 2);
        Assert.True(s.Reading);
        Assert.False(s.Linked);
        s.Advance([], HandsOnSession.ReadSteps + 5);
        Assert.True(s.Linked, "read through, the press opens the link");
    }

    [Fact]
    public void PressingAWoundBeatsInItsOwnWay()
    {
        // A cut jolts and lets go; frost is a dull steady numbness; a bruise swells and ebbs.
        var cut = Enumerable.Range(0, 120).Select(i => HandsOnSession.PulseShape(DrawPattern.Spike, i, 1f)).ToList();
        var frost = Enumerable.Range(0, 120).Select(i => HandsOnSession.PulseShape(DrawPattern.Numb, i, 1f)).ToList();
        var bruise = Enumerable.Range(0, 160).Select(i => HandsOnSession.PulseShape(DrawPattern.Wave, i, 1f)).ToList();
        Assert.True(cut.Max() > 0.9f && cut.Min() < 0.05f);
        Assert.True(frost.Max() < 0.35f && frost.Min() > 0.15f);
        Assert.True(bruise.Max() > 0.9f && bruise.Min() < 0.1f);
        Assert.True(HandsOnSession.PulseShape(DrawPattern.Grow, 10, 0.9f) > HandsOnSession.PulseShape(DrawPattern.Grow, 10, 0.1f)); // venom grows as you read it
    }

    [Fact]
    public void OnlyALongerPressFindsFesteringUnderADressing()
    {
        bool Finds(int press)
        {
            var (sim, me, patient, _) = AtTheBedside(felt: false);
            var inj = sim.World.Get<Health>(patient).Injuries.Single();
            inj.Tended = true;
            inj.Infection = new Infection { Severity = 0.3f };
            FeelOver(sim, me, patient, press: press);
            return Perception.For(sim, me).FeltWounds(patient)!.Single().Festering;
        }
        Assert.False(Finds(HandsOnSession.ReadSteps + 5));
        Assert.True(Finds(int.MaxValue));
    }

    [Fact]
    public void PressingWhileTheHandsMoveOverAWoundMakesThePatientFlinch()
    {
        var (sim, me, patient, arm) = AtTheBedside(felt: false);
        sim.ApplyNow(new FeelCommand(me, patient));
        var s = (HandsOnSession)sim.World.Get<WorkSession>(me);
        // Pressed all the way down the body, never stopping.
        s.Advance([new WorkInput(0, WorkInputKind.Hold, 1), new WorkInput(0, WorkInputKind.Axis, 1)], 600);
        Assert.True(s.Flinches > 0, "a rough hand dragged over a cut");
        Assert.All(s.Spots, p => Assert.True(p.Found < 1f, "and nothing was read pressing on the move"));
        sim.Tick();
        Assert.Contains(sim.World.Get<Mood>(patient).Memories, m => m.Def == sim.Defs.Thoughts.RoughHands);
    }

    [Fact]
    public void OneSittingReadsAndDrawsEveryWoundInTurn()
    {
        var (sim, me, patient, arm) = AtTheBedside(cut: 8f, felt: false);
        var h = sim.World.Get<Health>(patient);
        var leg = h.Body.Parts.FindIndex(p => p.Label.Contains("leg") && !p.Vital);
        HealthSystem.Damage(sim, patient, 6f, DamageType.Blunt, leg);
        Assert.Equal(2, h.Injuries.Count);

        sim.ApplyNow(new FeelCommand(me, patient));
        Draw(sim, me, steady: true);

        Assert.Empty(h.Injuries);
        Assert.Equal(2, sim.World.Get<Health>(me).Injuries.Count(i => i.Absorbed));
    }

    [Fact]
    public void AWoundDrawnWholeLandsWhileTheHandsGoOn()
    {
        var (sim, me, patient, arm) = AtTheBedside(cut: 8f);
        var h = sim.World.Get<Health>(patient);
        HealthSystem.Damage(sim, patient, 6f, DamageType.Blunt, h.Body.Parts.FindIndex(p => p.Label.Contains("leg") && !p.Vital)); // another, not yet read
        sim.ApplyNow(new LayOnHandsCommand(me, patient, CutKey(sim, patient, arm)));
        var s = (HandsOnSession)sim.World.Get<WorkSession>(me);
        var predictor = new WorkPredictor();
        for (var t = 0; t < 60 * 60 && !s.Spots.All(p => p.Applied); t++)
        {
            sim.Tick();
            var axis = predictor.Session is HandsOnSession x && MathF.Abs(x.Link.Composure - 0.5f) > x.Link.HalfWidth * 0.25f ? -Math.Sign(x.Link.Composure - 0.5f) : 0;
            predictor.Update(sim, me, 1, false, true, axis, c => sim.ApplyNow(c));
        }
        Assert.True(sim.World.Has<WorkSession>(me), "the hands are still on them");
        Assert.DoesNotContain(sim.World.Get<Health>(patient).Injuries, i => i.Type == DamageType.Cut);
        Assert.Contains(sim.World.Get<Health>(me).Injuries, i => i.Absorbed);
    }

    [Fact]
    public void APatientKeepsStillWhileFeltOverAndHandsOnSurvivesASave()
    {
        var (sim, me, patient, _) = AtTheBedside(felt: false);
        sim.World.Get<Mind>(patient).Drafted = false;
        sim.ApplyNow(new FeelCommand(me, patient));
        sim.TickMany(30);
        Assert.Equal(me, Empathy.DrawnBy(sim, patient));
        var s = (HandsOnSession)sim.World.Get<WorkSession>(me);
        s.Advance([new WorkInput(s.StepsDone, WorkInputKind.Axis, 1)], s.StepsDone + 40);

        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        var copy = (HandsOnSession)loaded.World.Get<WorkSession>(me);
        Assert.Equal((s.Hands01, s.Spots.Count, s.Spots[0].Sensed, s.Parts.Count, s.Link.Composure),
            (copy.Hands01, copy.Spots.Count, copy.Spots[0].Sensed, copy.Parts.Count, copy.Link.Composure));
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
    public void ADownedEmpathCanStillMendThemselvesButNotKnotACharm()
    {
        var (sim, me, patient, arm) = AtTheBedside();
        Empathy.Draw(sim, me, patient, CutKey(sim, patient, arm), 1f, snapped: false);
        Inventory.Stow(sim, me, sim.CreateItem(sim.Thing("Bandage"), 2));
        var key = CutKey(sim, me, arm);
        var before = Empathy.Find(sim, me, key)!.Damage;
        ref var c = ref sim.World.Get<Creature>(me);
        HealthSystem.SetDowned(sim, me, ref c, true);

        sim.ApplyNow(new WeaveCharmCommand(me));
        Assert.False(sim.World.Has<WorkSession>(me), "a charm waits till you're up");

        sim.ApplyNow(new WeaveCommand(me, key));
        var cast = Assert.IsType<CastSession>(sim.World.Get<WorkSession>(me));
        Assert.True(cast.Downed);
        Assert.True(cast.WindowSteps < (int)(CastSession.Window * cast.Hands.Tolerance), "the beat is harder to keep on the ground");
        sim.World.Remove<WorkSession>(me);

        Weave(sim, me, key, ["Flesh", "Thread"]); // knitting
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
    public void TheDealGivesAEmpathTheirBook()
    {
        var (sim, me, _, _) = AtTheBedside();
        RoundSystem.Assign(sim, me, sim.Defs.Trade("Empath"));
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
    public void AWeaveForNoWoundTakesHoldOfOneItMends()
    {
        // The gift's own Weave button chooses no wound: a knitting woven from it still finds the cut you carry.
        var (sim, me, patient, arm) = AtTheBedside(cut: 30f);
        Empathy.Draw(sim, me, patient, CutKey(sim, patient, arm), 1f, snapped: false);
        var key = CutKey(sim, me, arm);
        var before = Empathy.Find(sim, me, key)!.Damage;
        Weave(sim, me, 0, ["Thread", "Flesh"]);
        Assert.True((Empathy.Find(sim, me, key)?.Damage ?? 0f) < before - 5f);
        Assert.DoesNotContain(sim.Messages.Messages, m => m.Text.Contains("finds no wound"));
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
    public void TheShrineHasPalletsAStoneAnAltarAndSupplies()
    {
        var sim = VillageRound.NewSimulation(LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        var town = VillageRound.Setup(sim);
        Assert.False(town.Shrine.IsNull);
        var parts = sim.World.Query<PartOf>().Where(e => sim.World.Get<PartOf>(e).Building == town.Shrine).ToList();
        int Count(string def) => parts.Count(e => sim.World.Get<Thing>(e).Def.Name == def);
        Assert.Equal(1, Count("MeditationStone"));
        Assert.Equal(3, Count("StrawPallet"));
        Assert.Equal(1, Count("RuneAltar"));
        Assert.Equal(2, Count("HerbRack"));
        var basket = parts.Single(e => sim.World.Get<Thing>(e).Def.Name == "LinenBasket");
        Assert.Equal(VillageRound.ShrineBandages, sim.StoredIn(basket).Where(i => sim.World.Get<Thing>(i).Def.Name == "Bandage").Sum(i => sim.World.Get<Stack>(i).Count));
        Assert.Single(parts, e => sim.World.Get<Thing>(e).Def.Name == "Chest"); // and a chest for the rest
        Assert.Equal("Shrine", sim.Defs.Trade("Empath").Workplace);
    }

    [Fact]
    public void AFeltWoundThatWorsensMustBeFeltAgain()
    {
        var (sim, me, patient, arm) = AtTheBedside(cut: 1f);
        var h = sim.World.Get<Health>(patient);
        var cut = h.Injuries.Single(i => i.Part == arm && i.Type == DamageType.Cut);
        Assert.Equal(me, cut.FeltBy);
        var was = Empathy.Severity(h, cut);
        HealthSystem.Damage(sim, patient, 0.05f, DamageType.Cut, arm);
        Assert.Equal(was, Empathy.Severity(h, cut));
        Assert.Equal(me, cut.FeltBy); // a little worse, but the same wound to the hand
        while (Empathy.Severity(h, cut) == was)
            HealthSystem.Damage(sim, patient, 0.25f, DamageType.Cut, arm);
        Assert.True(cut.FeltBy.IsNull);
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
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default));
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
    public void AEmpathWhoDrawsAnAdventurersWoundIsPaid()
    {
        var (sim, hurt) = ShrineWithAHurtAdventurer("Empath");
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
    public void AnAIEmpathDrawsAnAdventurersWoundMendsItAndIsPaid()
    {
        var (sim, hurt) = ShrineWithAHurtAdventurer("Blacksmith");
        var healer = sim.Creatures().Single(e => sim.World.TryGet<Trade>(e, out var t) && t.Def.Name == "Empath");
        for (var i = 0; i < 16 && sim.Shops.All.Any(e => e.IsOpen && e.Kind.Kind == ServiceKind.Healing); i++)
            sim.TickMany(SimClock.TicksPerHour / 4);

        var order = sim.Shops.All.Single(e => e.Poster == hurt && e.Kind.Kind == ServiceKind.Healing);
        Assert.Equal(OrderStatus.Done, order.Status);
        Assert.Equal(healer, order.Assignee);
        // It may be part way through the next of the party (they come in bruised): let it finish.
        for (var i = 0; i < 8 && sim.World.Get<Health>(healer).Injuries.Where(i => i.Absorbed).Sum(i => i.Damage) >= LayOnHands.MendFrom; i++)
            sim.TickMany(SimClock.TicksPerHour / 4);
        var log = string.Join("\n", sim.Messages.Messages.TakeLast(8).Select(m => m.Text));
        // Not bound and left to close over days: drawn out of them, and woven away.
        Assert.DoesNotContain(sim.World.Get<Health>(hurt).Injuries, i => i.Type == DamageType.Cut && i.Damage >= LayOnHands.MendFrom);
        Assert.True(sim.World.Get<Mana>(healer).Current < sim.World.Get<Mana>(healer).Max, "it cost mana: " + log);
        Assert.True(sim.World.Get<Health>(healer).Injuries.Where(i => i.Absorbed).Sum(i => i.Damage) < LayOnHands.MendFrom, "it mended what it took: " + log);
    }

    /// <summary>A healer's shrine with its AI healer, a player in another trade cut and bandaged, and the shrine's pallets.</summary>
    private static (Simulation Sim, Entity Me, Entity Healer, Entity Pallet) HurtPlayerAtTheShrine()
    {
        var (sim, adventurer) = ShrineWithAHurtAdventurer("Blacksmith");
        sim.World.Get<Health>(adventurer).Injuries.Clear(); // it's the player's turn
        sim.World.Get<Health>(adventurer).Changed();
        var me = Players.CharacterOf(sim, Ana);
        var healer = sim.Creatures().Single(e => sim.World.TryGet<Trade>(e, out var t) && t.Def.Name == "Empath");
        var h = sim.World.Get<Health>(me);
        var leg = h.Body.Parts.FindIndex(p => p.Label.Contains("leg") && !p.Vital);
        HealthSystem.Damage(sim, me, 14f, DamageType.Cut, leg);
        foreach (var inj in h.Injuries)
            Bandaging.Dress(sim, inj, 0.5f); // bound: no longer bleeding, still hurt
        h.Changed();
        var shrine = sim.Shops.All.Single(e => e.Poster == adventurer && e.Kind.Kind == ServiceKind.Healing).Where;
        var pallet = ShopSystem.BedsOf(sim, shrine)[0];
        var at = sim.World.Get<Thing>(pallet).Cell;
        var cell = sim.SpiralFrom(at, 2).First(c => c != at && sim.Map.Passable(c) && c.Z == at.Z && Ai.Pathfinder.Touches(c, at)
            && sim.Map.Structure[sim.Map.Idx(c)].IsNull);
        sim.World.Get<Position>(me).Cell = cell;
        sim.World.Get<Position>(me).Pos = Position.CenterOf(cell);
        return (sim, me, healer, pallet);
    }

    [Fact]
    public void AHurtPlayerAsksAtAShrinePalletAndIsMendedNotJustBound()
    {
        var (sim, me, healer, pallet) = HurtPlayerAtTheShrine();
        var at = sim.World.Get<Thing>(pallet).Cell;
        var ask = AvatarSystem.ActionAt(sim, me, at);
        Assert.Equal(AvatarActionKind.OrderHealing, ask.Kind);
        Assert.Contains("healed", ask.Label);
        var purse = Coin.Count(sim, me);
        Assert.Null(sim.Shops.PlayerOrder(sim, me, ServiceKind.Healing, Buildings.Of(sim, pallet)));
        var order = sim.Shops.OpenOrderOf(me, ServiceKind.Healing)!;
        Assert.True(order.Mend);
        Assert.NotEqual(AvatarActionKind.OrderHealing, AvatarSystem.ActionAt(sim, me, at).Kind); // asked: the pallet's for lying on now

        // Bound already, so nothing needs tending: an order only for bleeding would close at once, unpaid.
        sim.TickMany(SimClock.TicksPerHour / 12);
        Assert.Equal(OrderStatus.Open, order.Status);
        for (var i = 0; i < 16 && order.IsOpen; i++)
            sim.TickMany(SimClock.TicksPerHour / 4);

        var log = string.Join("\n", sim.Messages.Messages.TakeLast(8).Select(m => m.Text));
        Assert.True(order.Status == OrderStatus.Done, $"{order.Status}: {log}");
        Assert.Equal(healer, order.Assignee);
        Assert.False(LayOnHands.Hurt(sim, me), log);
        Assert.Equal(purse - order.Price, Coin.Count(sim, me));
    }

    [Fact]
    public void AHurtBotGoesToTheShrineAndAsks()
    {
        var (sim, me, healer, _) = HurtPlayerAtTheShrine();
        var bot = new Bots.BotPlayer(() => sim, Ana, c => sim.ApplyNow(c), 11);
        Order? asked = null;
        for (var t = 0; t < SimClock.TicksPerHour * 4 && (asked == null || asked.IsOpen); t++)
        {
            sim.Tick();
            bot.Tick();
            asked ??= sim.Shops.All.FirstOrDefault(e => e.Poster == me && e.Kind.Kind == ServiceKind.Healing);
        }
        var log = $"bot {bot.Doing}\n" + string.Join("\n", sim.Messages.Messages.TakeLast(8).Select(m => m.Text));
        Assert.True(asked != null, "it asked: " + log);
        Assert.True(asked!.Mend, log);
        Assert.True(asked.Status == OrderStatus.Done, $"{asked.Status}: {log}");
        Assert.Equal(healer, asked.Assignee);
        Assert.False(LayOnHands.Hurt(sim, me), log);
    }

    [Fact]
    public void AnUnhurtPlayerIsntOfferedHealing()
    {
        var (sim, me, _, pallet) = HurtPlayerAtTheShrine();
        sim.World.Get<Health>(me).Injuries.Clear();
        sim.World.Get<Health>(me).Changed();
        Assert.NotEqual(AvatarActionKind.OrderHealing, AvatarSystem.ActionAt(sim, me, sim.World.Get<Thing>(pallet).Cell).Kind);
        Assert.NotNull(sim.Shops.PlayerOrder(sim, me, ServiceKind.Healing, Buildings.Of(sim, pallet)));
    }

    [Fact]
    public void AnAIEmpathSeesToABadlyHurtVillagerUnaskedAndMendsWhatItCarries()
    {
        var (sim, adventurer) = ShrineWithAHurtAdventurer("Blacksmith");
        sim.World.Get<Health>(adventurer).Injuries.Clear();
        sim.World.Get<Health>(adventurer).Changed();
        var me = Players.CharacterOf(sim, Ana);
        var healer = sim.Creatures().Single(e => sim.World.TryGet<Trade>(e, out var t) && t.Def.Name == "Empath");
        var villager = sim.Creatures().First(e => e != me && e != healer && sim.World.Get<Creature>(e).Faction == Faction.Village
            && sim.World.Has<Trade>(e) && !sim.World.Has<Adventurer>(e));
        var h = sim.World.Get<Health>(villager);
        HealthSystem.Damage(sim, villager, 12f, DamageType.Blunt, h.Body.Parts.FindIndex(p => p.Label.Contains("arm") && !p.Vital));
        foreach (var inj in h.Injuries)
            Bandaging.Dress(sim, inj, 0.5f);
        h.Changed();

        for (var i = 0; i < 24 && LayOnHands.Hurt(sim, villager); i++)
            sim.TickMany(SimClock.TicksPerHour / 4);
        var log = string.Join("\n", sim.Messages.Messages.TakeLast(8).Select(m => m.Text));
        Assert.False(LayOnHands.Hurt(sim, villager), log);

        // Anything the casts left on the healer, it weaves away as the mana comes.
        for (var i = 0; i < 24 && sim.World.Get<Health>(healer).Injuries.Exists(x => x.Absorbed && x.Damage >= LayOnHands.MendFrom); i++)
            sim.TickMany(SimClock.TicksPerHour / 2);
        Assert.DoesNotContain(sim.World.Get<Health>(healer).Injuries, x => x.Absorbed && x.Damage >= LayOnHands.MendFrom);
    }

    [Fact]
    public void ABotEmpathDrawsAnAdventurersWoundAndWeavesItAway()
    {
        var (sim, hurt) = ShrineWithAHurtAdventurer("Empath");
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
    public void ASpentAIEmpathMeditatesItsManaBackAtTheStone()
    {
        var (sim, hurt) = ShrineWithAHurtAdventurer("Blacksmith");
        sim.World.Get<Health>(hurt).Injuries.Clear(); // nobody waiting on them
        var healer = sim.Creatures().Single(e => sim.World.TryGet<Trade>(e, out var t) && t.Def.Name == "Empath");
        ref var mana = ref sim.World.Get<Mana>(healer);
        mana.Current = 0f;
        var sat = false;
        for (var t = 0; t < SimClock.TicksPerHour && !sat; t += 60)
        {
            sim.TickMany(60);
            sat = sim.World.Get<Mind>(healer).Job is MeditateJob;
        }
        Assert.True(sat, "off to the meditation stone");
        sim.TickMany(MeditateJob.SittingTicks + SimClock.TicksPerHour / 4);
        // Far more than an hour's rest gives back (Empathy.RegenPerHour).
        Assert.True(sim.World.Get<Mana>(healer).Current > sim.World.Get<Mana>(healer).Max * 0.3f, $"mana {sim.World.Get<Mana>(healer).Current}");
    }

    [Fact]
    public void TheWoundedLieOnTheShrinesPalletsAndAreDrawnFromAtTheBedside()
    {
        var (sim, hurt) = ShrineWithAHurtAdventurer("Empath");
        var order = sim.Shops.All.Single(e => e.IsOpen && e.Poster == hurt && e.Kind.Kind == ServiceKind.Healing);
        Assert.Equal(3, ShopSystem.BedsOf(sim, order.Where).Count);
        var cells = ShopSystem.LineCells(sim, order.Where);
        Assert.Equal(ShopSystem.BedsOf(sim, order.Where).Select(b => sim.World.Get<Thing>(b).Cell), cells.Take(3));

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
        Assert.True(((HandsOnSession)sim.World.Get<WorkSession>(me)).Link.Bedside);
    }
}

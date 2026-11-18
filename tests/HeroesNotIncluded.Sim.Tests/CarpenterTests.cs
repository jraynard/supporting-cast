using HeroesNotIncluded.Sim.Bots;
using HeroesNotIncluded.Sim.Carpentry;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Expeditions;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// The Carpenter (docs/DESIGN.md, "Carpenter"): planks sawn at the sawhorse, quivers fletched at the
/// bench with a warp nobody can see, sold to archers and shot away on a quest; the AI carpenter and
/// the bot at the woodshop.
/// </summary>
public class CarpenterTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    /// <summary>A player in a round at a sawhorse and a carpenter's bench, with logs, planks and feathers in their pack.</summary>
    private static (Simulation Sim, Entity Me, Entity Horse, Entity Bench) AtTheWoodshop()
    {
        var sim = Create(40, 40);
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), "Wright", C(10, 10), ageDays: 25 * 365);
        sim.Round.OpenLobby();
        sim.Round.Sit(Ana, "Wright", null, null);
        sim.Round.Start(sim);
        sim.GiveStartingPack(me);
        sim.World.Get<Skills>(me).Level[(int)Skill.Crafting] = 6;
        Inventory.Stow(sim, me, sim.CreateItem(sim.Thing("Wood"), 6));
        sim.World.Get<Carrying>(me).Item = sim.CreateItem(sim.Thing(Felling.LogDef), 1); // a log in the arms, to saw
        Inventory.Stow(sim, me, sim.CreateItem(sim.Thing("Planks"), StartPlanks));
        Inventory.Stow(sim, me, sim.CreateItem(sim.Thing("Feathers"), 2));
        var horse = sim.SpawnStructure(sim.Thing(Carpenters.SawhorseDef), C(10, 11));
        var bench = sim.SpawnStructure(sim.Thing(Carpenters.BenchDef), C(8, 9));
        sim.RebuildDerivedIfNeeded();
        return (sim, me, horse, bench);
    }

    /// <summary>Planks in the pack to begin with: a quiver's and a bow's.</summary>
    private const int StartPlanks = 6;

    /// <summary>Play a session through the predictor, as a player at the keys would, until it's over.</summary>
    private static void Play(Simulation sim, Entity me, Func<WorkSession, (bool Press, bool Held, int Axis, bool Act)> keys, int maxTicks = 60 * 180) =>
        Play(sim, me, w => { var (p, h, a, act) = keys(w); return (p, h, a, 0, act); }, maxTicks);

    /// <summary>As the other, with W/S too.</summary>
    private static void Play(Simulation sim, Entity me, Func<WorkSession, (bool Press, bool Held, int Axis, int Axis2, bool Act)> keys, int maxTicks = 60 * 180)
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

    private static int PlanksOn(Simulation sim, Entity who) =>
        Carried(sim, who).Where(e => sim.World.Get<Thing>(e).Def.Name == "Planks").Sum(e => sim.World.Get<Stack>(e).Count);

    // ---------------- Sawing ----------------

    /// <summary>
    /// Planks made well: each side hewn with blows raised to bring it near the line, then the beam
    /// sawn in steady strokes down and up on the keys, the blade steered back whenever it's off the line.
    /// </summary>
    private static Func<WorkSession, (bool, bool, int, int, bool)> SawStraight(bool steer = true, Func<SawSession, bool>? hewHold = null)
    {
        var way = 1;
        return w =>
        {
            var s = (SawSession)w;
            if (s.Hewing)
            {
                if (s.Turning > 0f)
                    return (false, false, 0, 0, false);
                if (s.Proud[s.Side] <= s.FlatWidth && s.Raised <= 0f)
                    return (false, false, 0, 0, true); // flat enough: turn the log
                var want = Math.Min(SawSession.DeepestBlow, s.Proud[s.Side] - s.FlatWidth * 0.4f);
                return (false, hewHold?.Invoke(s) ?? SawSession.BlowDepth(s.Raised) < want, 0, 0, false);
            }
            if (s.Stroke >= 0.97f) way = -1;
            else if (s.Stroke <= 0.03f) way = 1;
            var back = steer && MathF.Abs(s.Offset) > s.OnLineWidth * 0.3f ? -Math.Sign(s.Offset) : 0;
            return (false, false, back, way, false);
        };
    }

    /// <summary>A second pair of hands at the sawhorse, an avatar standing by it to lend them.</summary>
    private static Entity Pitman(Simulation sim, Entity horse)
    {
        var at = sim.World.Get<Thing>(horse).Cell;
        var cell = sim.SpiralFrom(at, 2).First(c => c != at && sim.Map.Passable(c) && AvatarSystem.InReach(sim, c, horse)
            && c != sim.World.Get<Position>(sim.Creatures().First()).Cell);
        var pitman = sim.SpawnCreature(sim.Defs.Creature("Human"), "Pitman", cell, ageDays: 25 * 365);
        sim.World.Add(pitman, new Avatar());
        return pitman;
    }

    /// <summary>
    /// A great log sawn: the top sawyer's keys each tick through the predictor, and the pitman's
    /// alongside (if any): take up the pit with F at the sawhorse, then pull with each stroke (or,
    /// careless, pull whenever). Returns the sawyer's session as it ended, for its counts.
    /// </summary>
    private static SawSession SawGreat(Simulation sim, Entity me, Entity horse, Entity? pitman, bool withTheStroke = true, int maxTicks = 60 * 240)
    {
        var w = sim.World;
        var lead = new WorkPredictor();
        var pit = new WorkPredictor();
        var keys = SawStraight();
        var session = (SawSession)w.Get<WorkSession>(me);
        for (var t = 0; t < maxTicks && w.Has<WorkSession>(me); t++)
        {
            sim.Tick();
            var (press, held, axis, axis2, act) = lead.Session is { } s ? keys(s) : (false, false, 0, 0, false);
            lead.Update(sim, me, 1, press, held, axis, c => sim.ApplyNow(c), axis2, act: act);
            if (pitman is not { } p)
                continue;
            if (!w.Has<WorkSession>(p))
            {
                var cell = w.Get<Thing>(horse).Cell;
                if (AvatarSystem.ActionAt(sim, p, cell, AvatarTool.Use).Kind == AvatarActionKind.LendAHand)
                    sim.ApplyNow(new AvatarWorkCommand(p, true, cell));
                continue;
            }
            if (pit.Session?.StartTick != w.Get<WorkSession>(p).StartTick)
            {
                pit.Update(sim, p, 1, false, false, 0, c => sim.ApplyNow(c));
                continue;
            }
            var pull = pit.Session is PitSession ps && (withTheStroke ? ps.CallOpen || ps.Down is > 0f and < 1f : true);
            pit.Update(sim, p, 1, false, pull, 0, c => sim.ApplyNow(c));
        }
        sim.Tick();
        return session;
    }

    [Fact]
    public void ATopSawyerAndAPitmanSawAGreatLogTogether()
    {
        var (sim, me, horse, _) = AtTheWoodshop();
        sim.World.Get<Carrying>(me).Item = sim.CreateItem(sim.Thing(Felling.GreatLogDef), 1); // a great log in the arms
        var pitman = Pitman(sim, horse);
        sim.ApplyNow(new CraftCommand(me, horse, Recipe(sim, "SawGreatLog")));
        Assert.True(Assert.IsType<SawSession>(sim.World.Get<WorkSession>(me)).Great);
        var session = SawGreat(sim, me, horse, pitman);
        Assert.False(sim.World.Has<WorkSession>(me), "the great log should be sawn");
        Assert.True(session.Pulls > 0);
        Assert.True(PlanksOn(sim, me) >= StartPlanks + 7, $"{PlanksOn(sim, me)} planks"); // 10 at best
        Assert.False(sim.World.Has<WorkSession>(pitman)); // out of the pit once it's done
    }

    [Fact]
    public void AloneAGreatLogsSawBarelyMoves()
    {
        var (sim, me, horse, _) = AtTheWoodshop();
        sim.World.Get<Carrying>(me).Item = sim.CreateItem(sim.Thing(Felling.GreatLogDef), 1);
        sim.ApplyNow(new CraftCommand(me, horse, Recipe(sim, "SawGreatLog")));
        var session = SawGreat(sim, me, horse, null, maxTicks: 60 * 60);
        Assert.True(sim.World.Has<WorkSession>(me), "a minute's sawing alone shouldn't see a great log through");
        Assert.True(session.Cut == 0, $"cut {session.Cut}, {session.Progress:F2} along");
    }

    [Fact]
    public void APitmanPullingAgainstTheSawBindsIt()
    {
        var (sim, me, horse, _) = AtTheWoodshop();
        sim.World.Get<Carrying>(me).Item = sim.CreateItem(sim.Thing(Felling.GreatLogDef), 1);
        var pitman = Pitman(sim, horse);
        sim.ApplyNow(new CraftCommand(me, horse, Recipe(sim, "SawGreatLog")));
        var session = SawGreat(sim, me, horse, pitman, withTheStroke: false, maxTicks: 60 * 200);
        Assert.True(session.Binds > 0, "pulled whenever, it binds");
    }

    [Fact]
    public void HewingSquareThenSawingTrueGivesEveryPlank()
    {
        var (sim, me, horse, _) = AtTheWoodshop();
        sim.ApplyNow(new CraftCommand(me, horse, Recipe(sim, "SawLog")));
        Assert.IsType<SawSession>(sim.World.Get<WorkSession>(me));
        Play(sim, me, SawStraight());
        Assert.Equal(StartPlanks + Carpenters.PlanksPerBatch, PlanksOn(sim, me));
    }

    [Fact]
    public void ACutLeftToWanderWastesPlanks()
    {
        var (sim, me, horse, _) = AtTheWoodshop();
        sim.ApplyNow(new CraftCommand(me, horse, Recipe(sim, "SawLog")));
        var session = (SawSession)sim.World.Get<WorkSession>(me);
        Play(sim, me, SawStraight(steer: false));
        var sawn = PlanksOn(sim, me) - StartPlanks;
        Assert.InRange(sawn, 1, Carpenters.PlanksPerBatch - 1);
        Assert.True(session.Quality < 0.75f, $"{session.Quality}");
    }

    [Fact]
    public void HewingPastTheLineWastesPlanks()
    {
        var (sim, me, horse, _) = AtTheWoodshop();
        sim.ApplyNow(new CraftCommand(me, horse, Recipe(sim, "SawLog")));
        var session = (SawSession)sim.World.Get<WorkSession>(me);
        Play(sim, me, SawStraight(hewHold: s => s.Raised < 1f)); // every blow from as high as it goes
        var sawn = PlanksOn(sim, me) - StartPlanks;
        Assert.InRange(sawn, 1, Carpenters.PlanksPerBatch);
        // At four planks a batch the waste may round away: the work's worse all the same.
        Assert.True(session.Quality < 0.85f, $"{session.Quality}");
    }

    /// <summary>Hew the log square straight on the session: blows from high, waiting out each turn of the log.</summary>
    private static void HewSquare(SawSession s)
    {
        while (s.Hewing && s.StepsDone < 20000)
        {
            if (s.Turning > 0f)
            {
                s.Advance([], s.StepsDone + 1);
                continue;
            }
            var at = s.StepsDone;
            if (s.Proud[s.Side] <= s.FlatWidth)
                s.Advance([new WorkInput(at, WorkInputKind.Act)], at + 1); // flat enough (or past): turn it
            else
                s.Advance([new WorkInput(at, WorkInputKind.Hold, 1), new WorkInput(at + 50, WorkInputKind.Hold, 0)], at + 52);
        }
        Assert.False(s.Hewing);
    }

    [Fact]
    public void TheLogIsTurnedBetweenSidesAndTheAxeRestsMeanwhile()
    {
        var (sim, me, horse, _) = AtTheWoodshop();
        sim.ApplyNow(new CraftCommand(me, horse, Recipe(sim, "SawLog")));
        var s = (SawSession)sim.World.Get<WorkSession>(me);
        // Hewn past flat, blows and all: it stays on this side till it's turned.
        for (var b = 0; b < 5; b++)
        {
            var at = s.StepsDone;
            s.Advance([new WorkInput(at, WorkInputKind.Hold, 1), new WorkInput(at + 50, WorkInputKind.Hold, 0)], at + 52);
        }
        Assert.Equal(0, s.Side);
        s.Advance([new WorkInput(s.StepsDone, WorkInputKind.Act)], s.StepsDone + 1);
        Assert.Equal(1, s.Side);
        Assert.True(s.Turning > 0f);
        // Held while it turns: the axe doesn't go up.
        s.Advance([new WorkInput(s.StepsDone, WorkInputKind.Hold, 1)], s.StepsDone + 5);
        Assert.Equal(0f, s.Raised);
        s.Advance([], s.StepsDone + SawSession.TurnSteps);
        Assert.Equal(0f, s.Turning);
        Assert.True(s.Raised > 0f); // still held, round now: up it goes
    }

    [Fact]
    public void TurningASideRoughCostsAndNearTheLineScoresBest()
    {
        var s = new SawSession { Hands = new Hands(1f, 1f, 5) };
        Assert.True(s.SideScore(0.02f) > 0.9f);
        Assert.True(s.SideScore(0.3f) < s.SideScore(0.05f));
        Assert.Equal(0f, s.SideScore(1f)); // bark and all
        Assert.True(s.SideScore(-0.2f) < s.SideScore(0.02f));
    }

    [Fact]
    public void ABlowGoesAsDeepAsTheAxeWasRaised()
    {
        var (sim, me, horse, _) = AtTheWoodshop();
        sim.ApplyNow(new CraftCommand(me, horse, Recipe(sim, "SawLog")));
        var s = (SawSession)sim.World.Get<WorkSession>(me);
        s.Advance([new WorkInput(0, WorkInputKind.Hold, 1), new WorkInput(20, WorkInputKind.Hold, 0)], 22);
        Assert.Equal(1, s.Blows);
        Assert.InRange(1f - s.Proud[0], SawSession.BlowDepth(20 * SawSession.RaisePerStep) * 0.8f, SawSession.BlowDepth(20 * SawSession.RaisePerStep) * 1.2f);
    }

    [Fact]
    public void AStillSawCutsNothingAndOnlyTheDownstrokeCuts()
    {
        var (sim, me, horse, _) = AtTheWoodshop();
        sim.ApplyNow(new CraftCommand(me, horse, Recipe(sim, "SawLog")));
        var s = (SawSession)sim.World.Get<WorkSession>(me);
        // Hewn, then left alone: nothing cut.
        HewSquare(s);
        var at = s.StepsDone;
        s.Advance([], at + 120);
        Assert.Equal(0f, s.Progress);
        // Drawn up (it starts drawn up): nothing; pulled down: the cut goes along.
        s.Advance([new WorkInput(s.StepsDone, WorkInputKind.Axis2, -1)], s.StepsDone + 30);
        Assert.Equal(0f, s.Progress);
        s.Advance([new WorkInput(s.StepsDone, WorkInputKind.Axis2, 1)], s.StepsDone + 30);
        Assert.True(s.Progress > 0f);
    }

    [Fact]
    public void TheMouseEasesTheSawAndSteersOnlyOutsideItsMiddle()
    {
        var (sim, me, horse, _) = AtTheWoodshop();
        sim.ApplyNow(new CraftCommand(me, horse, Recipe(sim, "SawLog")));
        var s = (SawSession)sim.World.Get<WorkSession>(me);
        HewSquare(s);
        var at = s.StepsDone;
        // The pointer dropped to the bottom near the middle: the saw follows, a part of the way a step, and isn't steered.
        s.Advance([new WorkInput(at, WorkInputKind.Point, WorkSession.PackPoint(0.52f, 1f))], at + 1);
        Assert.InRange(s.Stroke, 0.001f, SawSession.MostPace + 0.001f);
        var offset = s.Offset;
        s.Advance([], s.StepsDone + 40);
        Assert.True(s.Stroke > 0.9f);
        Assert.True(MathF.Abs(s.Offset - offset) < 0.2f, $"{offset} to {s.Offset}");
    }

    [Fact]
    public void YieldFollowsTheCut() =>
        Assert.True(SawSession.Yield(10, 1f) == 10 && SawSession.Yield(10, 0f) == 5 && SawSession.Yield(10, 0.5f) is > 5 and < 10);

    // ---------------- Fletching ----------------

    /// <summary>
    /// An arrow made well, stage by stage: bend the shaft till the needle reads straight; shave in
    /// passes along it with the edge a hair over the line, then call it done; push the head on, keeping
    /// it in line; set the feathers a third of a turn apart.
    /// </summary>
    private static Func<WorkSession, (bool, bool, int, int, bool)> FletchWell(float edgeHeight = 0.03f)
    {
        var (passes, way, set) = (0, 1, -1);
        return w =>
        {
            var s = (FletchSession)w;
            switch (s.Stage)
            {
                case ArrowStage.Sight:
                    var seen = s.SeenWarp * s.BendDirection;
                    return seen > s.TrueWidth * 0.3f ? (false, s.StepsDone % 3 == 0 || seen > 0.15f, 0, 0, false) : (false, false, 0, 0, true);
                case ArrowStage.Shave:
                    if (passes >= 4)
                        return (false, false, 0, 0, true);
                    var wantY = FletchSession.LineV - edgeHeight * FletchSession.HeightV;
                    if (MathF.Abs(s.KnifeY - wantY) > FletchSession.KnifeSpeed)
                        return (false, true, 0, Math.Sign(wantY - s.KnifeY), false);
                    if (s.KnifeX >= 0.99f && way > 0 || s.KnifeX <= 0.01f && way < 0)
                    {
                        way = -way;
                        passes++;
                    }
                    return (false, true, way, 0, false);
                case ArrowStage.Head:
                    return (false, true, MathF.Abs(s.Tilt) > s.InLineWidth * 0.3f ? -Math.Sign(s.Tilt) : 0, 0, false);
            }
            if (set == s.Set || s.Set >= FletchSession.Feathers)
                return (false, false, 0, 0, false);
            if (s.Set == 0)
            {
                set = 0;
                return (true, false, 0, 0, false);
            }
            var want = s.FeatherAt[0] + s.Set / (float)FletchSession.Feathers;
            want -= MathF.Floor(want);
            var d = want - s.Aim;
            d -= MathF.Round(d);
            if (MathF.Abs(d) <= FletchSession.AimPerStep * 0.6f)
            {
                set = s.Set;
                return (true, false, 0, 0, false);
            }
            return (false, false, Math.Sign(d), 0, false);
        };
    }

    /// <summary>As <see cref="FletchWell"/>, but the shaft called true at once, warp and all.</summary>
    private static Func<WorkSession, (bool, bool, int, int, bool)> FletchWarped()
    {
        var well = FletchWell();
        return w => w is FletchSession { Sighting: true } ? (false, false, 0, 0, true) : well(w);
    }


    private static Entity Quiver(Simulation sim, Entity me) => Carried(sim, me).Single(e => Fletching.IsQuiver(sim, e));

    [Fact]
    public void FletchingTrueMakesAGoodQuiver()
    {
        var (sim, me, _, bench) = AtTheWoodshop();
        sim.ApplyNow(new CraftCommand(me, bench, Recipe(sim, "Fletch")));
        Assert.IsType<FletchSession>(sim.World.Get<WorkSession>(me));
        Play(sim, me, FletchWell());
        var quiver = Quiver(sim, me);
        var f = sim.World.Get<Fletched>(quiver);
        Assert.False(f.Warped, "sighted straight by a steady eye: no shaft past the limit");
        Assert.True(f.Quality > 0.6f, $"{f.Quality}");
        Assert.Equal(me, f.Maker);
        Assert.Equal(me, sim.World.Get<Crafted>(quiver).Maker);
    }

    [Fact]
    public void AShaftLeftWarpedHidesInTheQuiver()
    {
        var (sim, me, _, bench) = AtTheWoodshop();
        sim.ApplyNow(new CraftCommand(me, bench, Recipe(sim, "Fletch")));
        // Called true at once, warp and all; the feathers set well.
        Play(sim, me, FletchWarped());
        var quiver = Quiver(sim, me);
        Assert.True(sim.World.Get<Fletched>(quiver).Warped);
        // What anyone sees is how well it's made, never the warp.
        var view = Perception.For(sim, me);
        Assert.DoesNotContain("warp", view.Condition(quiver) ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShavingIntoTheShaftGougesItAndMakesAPoorerArrow()
    {
        float Made(Func<WorkSession, (bool, bool, int, int, bool)> keys)
        {
            var (sim, me, _, bench) = AtTheWoodshop();
            sim.ApplyNow(new CraftCommand(me, bench, Recipe(sim, "Fletch")));
            Play(sim, me, keys);
            return sim.World.Get<Fletched>(Quiver(sim, me)).Quality;
        }
        var clean = Made(FletchWell());
        var gouged = Made(FletchWell(edgeHeight: -0.2f)); // the edge well into the shaft every pass
        Assert.True(gouged < clean - 0.15f, $"clean {clean}, gouged {gouged}");
    }

    [Fact]
    public void ShavingTakesOnlyWhereTheKnifePassesWithTheButtonDown()
    {
        var (sim, me, _, bench) = AtTheWoodshop();
        sim.ApplyNow(new CraftCommand(me, bench, Recipe(sim, "Fletch")));
        var s = (FletchSession)sim.World.Get<WorkSession>(me);
        s.Advance([new WorkInput(0, WorkInputKind.Act)], 2); // through the sight
        Assert.Equal(ArrowStage.Shave, s.Stage);
        var before = s.Wood.Sum();
        // Across the shaft just over its line, the button up: nothing comes off.
        var v = FletchSession.LineV - 0.02f * FletchSession.HeightV;
        s.Advance([new WorkInput(s.StepsDone, WorkInputKind.Point, WorkSession.PackPoint(0.1f, v)),
            new WorkInput(s.StepsDone + 1, WorkInputKind.Point, WorkSession.PackPoint(0.9f, v))], s.StepsDone + 3);
        Assert.Equal(before, s.Wood.Sum(), 3);
        // The same with it down: the wood along there comes off, and the shaft's left whole.
        s.Advance([new WorkInput(s.StepsDone, WorkInputKind.Hold, 1),
            new WorkInput(s.StepsDone + 1, WorkInputKind.Point, WorkSession.PackPoint(0.1f, v))], s.StepsDone + 3);
        Assert.True(s.Wood.Sum() < before * 0.8f, $"{s.Wood.Sum()} of {before}");
        Assert.All(s.Gouges, g => Assert.Equal(0f, g));
    }

    [Fact]
    public void AHeadPushedOnCrookedMakesAPoorerArrow()
    {
        float Made(Func<WorkSession, (bool, bool, int, int, bool)> keys)
        {
            var (sim, me, _, bench) = AtTheWoodshop();
            sim.ApplyNow(new CraftCommand(me, bench, Recipe(sim, "Fletch")));
            Play(sim, me, keys);
            return sim.World.Get<Fletched>(Quiver(sim, me)).Quality;
        }
        var well = FletchWell();
        var straight = Made(well);
        var crookedly = FletchWell();
        var crooked = Made(w => w is FletchSession { Stage: ArrowStage.Head } ? (false, true, 0, 0, false) : crookedly(w)); // pushed, never steered
        Assert.True(crooked < straight - 0.1f, $"straight {straight}, crooked {crooked}");
    }


    [Fact]
    public void SpacingIsBestAThirdOfATurnApart()
    {
        Assert.Equal(1f, FletchSession.Spacing([0.1f, 0.1f + 1f / 3f, 0.1f + 2f / 3f]), 3);
        Assert.Equal(1f, FletchSession.Spacing([0.8f, 0.8f + 1f / 3f - 1f, 0.8f + 2f / 3f - 1f]), 3); // round the turn
        Assert.Equal(1f, FletchSession.Spacing([0.7f, 0.0333f, 0.3667f]), 2); // in any order
        Assert.Equal(0f, FletchSession.Spacing([0.1f, 0.12f, 0.14f]));
    }

    [Fact]
    public void AQuiverAndItsWarpSurviveASave()
    {
        var (sim, me, _, bench) = AtTheWoodshop();
        sim.ApplyNow(new CraftCommand(me, bench, Recipe(sim, "Fletch")));
        Play(sim, me, FletchWarped());
        var quiver = Quiver(sim, me);
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        Assert.Equal(sim.World.Get<Fletched>(quiver), loaded.World.Get<Fletched>(quiver));
    }

    [Fact]
    public void ASessionMidwaySurvivesASave()
    {
        var (sim, me, _, bench) = AtTheWoodshop();
        sim.ApplyNow(new CraftCommand(me, bench, Recipe(sim, "Fletch")));
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        var a = (FletchSession)sim.World.Get<WorkSession>(me);
        var b = (FletchSession)loaded.World.Get<WorkSession>(me);
        Assert.Equal(a.SeenWarp, b.SeenWarp);
        Assert.Equal(a.BendDirection, b.BendDirection);
    }

    // ---------------- Bows ----------------

    /// <summary>Scrape the stiffest section a short stroke at a time till every one is near even, then draw it.</summary>
    private static (bool, bool, int, bool) TillerEven(WorkSession w)
    {
        var s = (BowSession)w;
        if (s.Stringing)
            return StringIt(s);
        var stiffest = Enumerable.Range(0, BowSession.Sections).MaxBy(i => s.Stiffness[i]);
        if (s.Stiffness[stiffest] <= BowSession.Even + s.RightWidth * 0.6f)
            return (false, false, 0, true);
        if (s.Selected != stiffest)
            return (false, false, s.StepsDone % 2 == 0 ? Math.Sign(BowSession.ColumnOf(stiffest) - BowSession.ColumnOf(s.Selected)) : 0, false);
        return (false, s.StepsDone % 20 < 10, 0, false);
    }

    /// <summary>Bend it a little past the catch and slip the loop on.</summary>
    private static (bool, bool, int, bool) StringIt(BowSession s) =>
        s.Flex >= s.CatchLow + 0.05f ? (false, false, 0, true) : (false, true, 0, false);

    [Fact]
    public void StringingCatchesOnlyBentFarEnoughAndStrainingCosts()
    {
        Quality Made(Func<BowSession, (bool, bool, int, bool)> stringing, out int missed)
        {
            var (sim, me, _, bench) = AtTheWoodshop();
            sim.ApplyNow(new CraftCommand(me, bench, Recipe(sim, "MakeBow")));
            var miss = 0;
            Play(sim, me, w =>
            {
                var s = (BowSession)w;
                miss = s.Missed;
                return s.Stringing ? stringing(s) : TillerEven(w);
            }, maxTicks: 60 * 600);
            missed = miss;
            return sim.World.Get<Crafted>(NewBow(sim, me)).Quality;
        }
        var good = Made(StringIt, out var none);
        Assert.Equal(0, none);
        // Space straight away, slack: it misses; then bent all the way before it's slipped on.
        var tries = 0;
        var strained = Made(s => tries++ == 0 ? (false, false, 0, true) : s.Flex < 0.99f ? (false, true, 0, false) : (false, false, 0, true), out var missedOnce);
        Assert.True(missedOnce >= 1);
        Assert.True(strained < good, $"good {good}, strained {strained}");
    }

    private static Entity NewBow(Simulation sim, Entity me) =>
        Carried(sim, me).Single(e => sim.World.Get<Thing>(e).Def.Name == "Bow" && sim.World.TryGet<Crafted>(e, out var c) && c.Maker == me);

    [Fact]
    public void TilleringEvenMakesAFineBow()
    {
        var (sim, me, _, bench) = AtTheWoodshop();
        sim.ApplyNow(new CraftCommand(me, bench, Recipe(sim, "MakeBow")));
        Assert.IsType<BowSession>(sim.World.Get<WorkSession>(me));
        Play(sim, me, TillerEven, maxTicks: 60 * 600);
        Assert.True(sim.World.Get<Crafted>(NewBow(sim, me)).Quality >= Quality.Fine, $"{sim.World.Get<Crafted>(NewBow(sim, me)).Quality}");
    }

    [Fact]
    public void AStaveDrawnAsItIsOrHingedMakesAPoorBow()
    {
        var (sim, me, _, bench) = AtTheWoodshop();
        sim.ApplyNow(new CraftCommand(me, bench, Recipe(sim, "MakeBow")));
        // One section scraped and scraped, the rest left stiff.
        Play(sim, me, w => w is BowSession { Stringing: true } b ? StringIt(b)
            : ((BowSession)w).Stiffness[0] > BowSession.Thinnest + 0.01f ? (false, true, 0, false) : (false, false, 0, true), maxTicks: 60 * 600);
        Assert.Equal(Quality.Crude, sim.World.Get<Crafted>(NewBow(sim, me)).Quality);
    }

    [Fact]
    public void AHingeCostsMoreThanTheSameLeftStiff()
    {
        var s = new BowSession();
        Assert.Equal(1f, s.SectionScore(BowSession.Even));
        Assert.True(s.SectionScore(BowSession.Even - 0.1f) < s.SectionScore(BowSession.Even + 0.1f));
    }

    // ---------------- Archers and the road ----------------

    [Fact]
    public void OnlyArchersBuyAQuiverAndOnlyOne()
    {
        var sim = Create(30, 30);
        var archer = sim.SpawnCreature(sim.Defs.Creature("Human"), "Fen", C(5, 5), ageDays: 25 * 365);
        var fighter = sim.SpawnCreature(sim.Defs.Creature("Human"), "Brand", C(7, 5), ageDays: 25 * 365);
        sim.GiveStartingPack(archer);
        sim.GiveStartingPack(fighter);
        sim.EquipItem(archer, sim.CreateItem(sim.Thing("Bow"), 1));
        sim.EquipItem(fighter, sim.CreateItem(sim.Thing("Club"), 1));
        var quiver = Fletching.Make(sim, Entity.Null, 0.7f, false);
        Assert.True(Goods.IsGear(sim.Thing("Quiver")));
        Assert.True(Goods.IsUpgrade(sim, archer, quiver));
        Assert.False(Goods.IsUpgrade(sim, fighter, quiver));
        Goods.TakeUp(sim, archer, quiver);
        Assert.Single(Fletching.Carried(sim, archer));
        Assert.False(Goods.IsUpgrade(sim, archer, Fletching.Make(sim, Entity.Null, 0.9f, false)));
    }

    [Fact]
    public void ATrueQuiverMakesAnArcherStrongerAndAWarpedOneWeaker()
    {
        var good = new List<QuiverRecord> { new(0.9f, false, Entity.Null) };
        var warped = new List<QuiverRecord> { new(0.9f, true, Entity.Null) };
        Assert.True(Fletching.StrengthFactor(true, good) > 1.05f);
        Assert.True(Fletching.StrengthFactor(true, warped) < 1f);
        Assert.Equal(1f, Fletching.StrengthFactor(false, good));
        Assert.Equal(1f, Fletching.StrengthFactor(true, []));
    }

    [Fact]
    public void AnArchersQuiverGoesOutOnTheQuestAndIsSavedWithTheParty()
    {
        var (sim, me, party) = ExpeditionTests.Round("Innkeeper"); // the player brokers: no waiting on an AI's say-so
        var archer = party.FirstOrDefault(e => Fletching.Shoots(sim, e), party[0]);
        if (!Fletching.Shoots(sim, archer))
            sim.EquipItem(archer, sim.CreateItem(sim.Thing("Bow"), 1));
        Inventory.Stow(sim, archer, Fletching.Make(sim, Entity.Null, 0.8f, true));
        Assert.Single(ExpeditionSystem.Record(sim, archer).Quivers);
        // Sent off as SaveLoadTests' party is: the broker beside them, offering the quest.
        var q = sim.Expeditions.Quests[0];
        var strength = ExpeditionSystem.PartyStrength(sim, sim.World.Get<Adventurer>(party[0]).Party);
        sim.Expeditions.Hear(me, q.Id, strength * 0.9f, null, 1f);
        var at = sim.World.Get<Position>(party[0]).Cell;
        var cell = sim.SpiralFrom(at, 2).First(c => c != at && sim.Map.Passable(c) && c.Z == at.Z);
        sim.World.Get<Position>(me).Cell = cell;
        sim.World.Get<Position>(me).Pos = Position.CenterOf(cell);
        Assert.True(sim.Expeditions.Offer(sim, me, party[0], q.Id));
        var name = sim.World.Get<Creature>(archer).Name;
        MemberRecord? Away(Simulation s) => s.Expeditions.Out.SelectMany(x => x.Members).FirstOrDefault(m => m.Name == name);
        for (var h = 0; h < 12 && Away(sim) == null; h++)
            sim.TickMany(SimClock.TicksPerHour / 2);
        var away = Away(sim);
        Assert.NotNull(away);
        Assert.Equal(new QuiverRecord(0.8f, true, Entity.Null), away.Quivers[0]);
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        Assert.Equal(away.Quivers, Away(loaded)!.Quivers);
    }

    // ---------------- The AI carpenter and the bot ----------------

    [Fact]
    public void TheAiCarpenterPutsQuiversOnShowAtTheWoodshop()
    {
        var (sim, _, _) = ExpeditionTests.Round("Innkeeper", tillTheyAsk: false);
        var shop = ShopSystem.BuildingFor(sim, TownRole.Woodshop);
        Assert.False(shop.IsNull);
        Assert.Contains(sim.Creatures(), e => sim.World.TryGet<Trade>(e, out var t) && t.Def.Name == "Carpenter");
        for (var t = 0; t < 12 && Carpenters.OnShow(sim, shop) < 1; t++)
            sim.TickMany(SimClock.TicksPerHour / 2);
        Assert.True(Carpenters.OnShow(sim, shop) >= 1, string.Join("\n", sim.Messages.Messages.TakeLast(12).Select(m => m.Text)));
        Assert.All(Goods.OnShow(sim).Where(g => g.Shop == shop), g => Assert.True(sim.World.Has<Fletched>(g.Item)));
    }

    [Fact]
    public void TheWoodshopsLogsAreTheCarpentersNotTheTownsFirewood()
    {
        var sim = VillageRound.NewSimulation(LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default));
        sim.ApplyNow(new SetTradePreferencesCommand(Ana, [sim.Defs.Trade("Carpenter")], []));
        sim.ApplyNow(new StartRoundCommand());
        var chest = Carpenters.ChestOf(sim, ShopSystem.BuildingFor(sim, TownRole.Woodshop));
        sim.TickMany(SimClock.TicksPerHour);
        Assert.Equal(VillageRound.WoodshopLogs, Carpenters.CountIn(sim, chest, Carpenters.LogDef));
    }

    [Fact]
    public void TheCarpenterBotFletchesForTheShelf()
    {
        var sim = VillageRound.NewSimulation(LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Ana", default));
        sim.ApplyNow(new SetTradePreferencesCommand(Ana, [sim.Defs.Trade("Carpenter")], []));
        var bot = new BotPlayer(() => sim, Ana, c => sim.ApplyNow(c), 7) { Temper = BotTemperament.Steady };
        sim.ApplyNow(new StartRoundCommand());
        var me = Players.CharacterOf(sim, Ana);
        bool Made() => Carried(sim, me).Concat(Goods.OnShow(sim).Select(g => g.Item))
            .Any(e => sim.World.TryGet<Fletched>(e, out var f) && f.Maker == me);
        for (var t = 0; t < SimClock.TicksPerHour * 6 && !Made(); t++)
        {
            sim.Tick();
            bot.Tick();
        }
        Assert.True(Made(), $"bot: {bot.Doing}\n" + string.Join("\n", sim.Messages.Messages.TakeLast(12).Select(m => m.Text)));
    }
}

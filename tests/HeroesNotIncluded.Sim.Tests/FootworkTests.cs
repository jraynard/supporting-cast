using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Systems;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// AI footwork beyond closing and striking (<see cref="FreeFight"/>): the hurt drop back rather
/// than giving up all at once, the dragon circles on the ground, and the unarmed gang up on a raider.
/// </summary>
public class FootworkTests
{
    private static Entity Person(Simulation sim, string name, int3 cell, string? weapon = "ShortSword")
    {
        var e = sim.SpawnCreature(sim.Defs.Creature("Human"), name, cell, Faction.Village, ageDays: 25 * 365);
        Array.Fill(sim.World.Get<Needs>(e).Values, 1f);
        if (weapon != null)
            sim.Equip(e, sim.Defs.Thing(weapon));
        sim.SetSkills(e, 8);
        return e;
    }

    /// <summary>A raider who stays where it's put (drafted: it waits for orders), hard to put down.</summary>
    private static Entity StandingRaider(Simulation sim, int3 cell, string kind = "SteelKnight")
    {
        var r = Raids.SpawnRaider(sim, sim.Defs.AllRaiderKinds.First(k => k.Name == kind), "Skarn", cell, sim.Rand);
        sim.World.Get<Mind>(r).Drafted = true;
        return r;
    }

    /// <summary>Cuts to an arm, a little at a time, till the pain is past <paramref name="pain"/>.</summary>
    private static void HurtTo(Simulation sim, Entity e, float pain)
    {
        var h = sim.World.Get<Health>(e);
        string[] parts = ["LeftArm", "RightArm", "Torso"];
        for (var i = 0; i < 200 && h.Pain <= pain; i++)
            HealthSystem.Damage(sim, e, 1f, DamageType.Blunt, h.Body.PartIndex(parts[i % parts.Length]));
    }

    private static float Dist(Simulation sim, Entity a, Entity b)
    {
        var p = sim.World.Get<Position>(a).Pos;
        var q = sim.World.Get<Position>(b).Pos;
        return MathF.Sqrt((p.X - q.X) * (p.X - q.X) + (p.Y - q.Y) * (p.Y - q.Y));
    }

    private static bool Fighting(Simulation sim, Entity e, Entity foe) => sim.World.Get<Mind>(e).Job is DefendJob d && d.Focus == foe;

    /// <summary>Ticks until they're all pressing the raider and in close (or a while passes).</summary>
    private static void TillPressing(Simulation sim, Entity foe, params Entity[] who)
    {
        for (var t = 0; t < SimClock.TicksPerSecond * 30 && !who.All(e => Engagement.Presses(sim, e, foe) && Dist(sim, e, foe) < 2f); t++)
            sim.Tick();
        Assert.All(who, e => Assert.True(Engagement.Presses(sim, e, foe), $"{sim.World.Get<Creature>(e).Name}: {sim.World.Get<Mind>(e).Job?.Report ?? "idle"}"));
    }

    [Fact]
    public void AHurtFighterGivesUpItsTurnAndHangsBack()
    {
        var sim = Create();
        var raider = StandingRaider(sim, C(20, 20));
        var ana = Person(sim, "Ana", C(15, 19));
        var bea = Person(sim, "Bea", C(15, 21));
        TillPressing(sim, raider, ana, bea);

        HurtTo(sim, ana, FreeFight.WaryPain + 0.02f);
        Assert.True(FreeFight.Wary(sim, ana));
        Assert.False(RaiderBrain.WantsToFlee(sim, ana));
        sim.TickMany(SimClock.TicksPerSecond * 3);
        Assert.True(Fighting(sim, ana, raider), sim.World.Get<Mind>(ana).Job?.Report ?? "idle");
        Assert.False(Engagement.Presses(sim, ana, raider), "the hurt one gives up its turn");
        Assert.True(Engagement.Presses(sim, bea, raider), "the fresh one keeps at it");
        Assert.True(Dist(sim, ana, raider) > FreeFight.WaryHold - 0.6f, $"hanging back: {Dist(sim, ana, raider):0.00} cells");
    }

    [Fact]
    public void AHurtFighterTheRaiderIsAfterKeepsSwinging()
    {
        var sim = Create();
        var raider = StandingRaider(sim, C(20, 20));
        var ana = Person(sim, "Ana", C(16, 20));
        var bea = Person(sim, "Bea", C(13, 22));
        TillPressing(sim, raider, ana, bea);
        // The raider comes at Ana: backing off with it on her would only be taking its blows.
        sim.World.Get<Mind>(raider).Drafted = false;
        for (var t = 0; t < SimClock.TicksPerSecond * 5 && !Engagement.Presses(sim, raider, ana); t++)
            sim.Tick();
        Assert.True(Engagement.Presses(sim, raider, ana), sim.World.Get<Mind>(raider).Job?.Report ?? "idle");

        HurtTo(sim, ana, FreeFight.WaryPain + 0.02f);
        sim.TickMany(SimClock.TicksPerSecond);
        Assert.True(!Engagement.Presses(sim, raider, ana) || Engagement.Presses(sim, ana, raider), "she fights back while it's on her");
    }

    [Fact]
    public void TheLastFighterStandingStillPressesWhenHurt()
    {
        var sim = Create();
        var raider = StandingRaider(sim, C(20, 20));
        var ana = Person(sim, "Ana", C(15, 20));
        TillPressing(sim, raider, ana);

        HurtTo(sim, ana, FreeFight.WaryPain + 0.02f);
        var blows = sim.RecentBlows.Count(b => b.Attacker == ana);
        sim.TickMany(SimClock.TicksPerSecond * 4);
        Assert.True(Engagement.Presses(sim, ana, raider));
        Assert.True(sim.RecentBlows.Count(b => b.Attacker == ana) > blows, "still striking");
    }

    [Fact]
    public void ABrokenFighterBacksOffFacingTheFoeBeforeItRuns()
    {
        var sim = Create();
        var raider = StandingRaider(sim, C(20, 20));
        var ana = Person(sim, "Ana", C(15, 20));
        TillPressing(sim, raider, ana);
        while (sim.World.Has<Windup>(ana))
            sim.Tick();

        HurtTo(sim, ana, 0.52f);
        Assert.True(RaiderBrain.WantsToFlee(sim, ana));
        var from = Dist(sim, ana, raider);
        var faced = 0;
        var ticks = 0;
        for (; ticks < FreeFight.WithdrawTicks && Fighting(sim, ana, raider); ticks++)
        {
            sim.Tick();
            if (!sim.World.TryGet<FreeMover>(ana, out var m))
                continue;
            var p = sim.World.Get<Position>(ana).Pos;
            var q = sim.World.Get<Position>(raider).Pos;
            var toward = MathF.Atan2(q.Y - p.Y, q.X - p.X);
            var off = MathF.Abs(MathF.IEEERemainder(m.Aim - toward, 2 * MathF.PI));
            if (off < 0.3f)
                faced++;
        }
        Assert.True(ticks > SimClock.TicksPerSecond / 3, $"backed off for {ticks} ticks before turning");
        Assert.True(faced >= ticks * 3 / 4, $"facing the foe {faced} of {ticks} ticks");
        Assert.True(Dist(sim, ana, raider) > from + 0.5f, $"from {from:0.00} to {Dist(sim, ana, raider):0.00} cells");

        sim.TickMany(SimClock.TicksPerSecond * 2);
        Assert.False(Fighting(sim, ana, raider), "then it gets away");
    }

    [Fact]
    public void ThreeUnarmedVillagersMobARaider()
    {
        var sim = Create();
        var raider = StandingRaider(sim, C(20, 20), sim.Defs.AllRaiderKinds[0].Name);
        var folk = new[] { Person(sim, "Ana", C(17, 20), null), Person(sim, "Bea", C(17, 22), null), Person(sim, "Cal", C(18, 17), null) };
        Assert.True(Defence.Mob(sim, folk[0], raider));
        var fought = new HashSet<Entity>();
        for (var t = 0; t < SimClock.TicksPerSecond * 10; t++)
        {
            sim.Tick();
            foreach (var e in folk)
                if (Fighting(sim, e, raider))
                    fought.Add(e);
        }
        Assert.True(fought.Count >= 2, $"{fought.Count} went at it: {string.Join(", ", folk.Select(e => sim.World.Get<Mind>(e).Job?.Report ?? "idle"))}");
        Assert.Contains(sim.RecentBlows, b => folk.Contains(b.Attacker) && b.Target == raider);
    }

    [Fact]
    public void TwoUnarmedVillagersStillGetAway()
    {
        var sim = Create();
        var raider = StandingRaider(sim, C(20, 20), sim.Defs.AllRaiderKinds[0].Name);
        var ana = Person(sim, "Ana", C(17, 20), null);
        var bea = Person(sim, "Bea", C(17, 22), null);
        Assert.False(Defence.Mob(sim, ana, raider));
        sim.TickMany(SimClock.TicksPerSecond * 5);
        Assert.False(Fighting(sim, ana, raider) || Fighting(sim, bea, raider));
    }

    [Fact]
    public void AnUnarmedVillagerJoinsAnArmedOneAtARaider()
    {
        var sim = Create();
        var raider = StandingRaider(sim, C(20, 20));
        var ana = Person(sim, "Ana", C(15, 20));
        TillPressing(sim, raider, ana);
        var bea = Person(sim, "Bea", C(17, 23), null);
        var cal = Person(sim, "Cal", C(18, 24), null);
        Assert.True(Defence.Mob(sim, bea, raider), "one fighting it and two who'd join make three");
        sim.TickMany(SimClock.TicksPerSecond * 2);
        Assert.True(Fighting(sim, bea, raider) || Fighting(sim, cal, raider));
    }

    // The dragon: open ground, someone far off as bait for its breath, and a foe put beside it once it's down.

    private static (Simulation Sim, Entity Dragon) DragonDown()
    {
        var sim = Create(48, 48);
        var def = sim.Defs.AllCreatures.First(c => c.Race == Race.Dragon);
        var dragon = sim.SpawnCreature(def, "Ashjaw", C(36, 36), Faction.Predator, ageDays: def.LifeStages[^1].MinAgeDays + 8);
        Array.Fill(sim.World.Get<Needs>(dragon).Values, 1f);
        var bait = Person(sim, "Bait", C(6, 6), null);
        sim.World.Get<Mind>(bait).Drafted = true;
        for (var t = 0; t < SimClock.TicksPerSecond * 120 && sim.World.Get<Mind>(dragon).Job?.Report != "Snapping at the town"; t++)
            sim.Tick();
        Assert.Equal("Snapping at the town", sim.World.Get<Mind>(dragon).Job?.Report);
        // The bait's done its work: out of the way.
        sim.Depart(bait);
        sim.World.FlushDeferred();
        return (sim, dragon);
    }

    private static Entity StandBeside(Simulation sim, Entity dragon, int dx, int dy)
    {
        var at = sim.World.Get<Position>(dragon).Cell;
        var cell = sim.SpiralFrom(new int3(at.X + dx, at.Y + dy, at.Z), 3).First(c => sim.Map.Passable(c) && c != at);
        var e = Person(sim, "Corin", cell, "IronSpear");
        sim.World.Get<Mind>(e).Drafted = true;
        return e;
    }

    [Fact]
    public void TheDragonCirclesItsFoeOnTheGroundAndBites()
    {
        var (sim, dragon) = DragonDown();
        var foe = StandBeside(sim, dragon, 3, 0);
        var turned = 0f;
        float? last = null;
        for (var t = 0; t < SimClock.TicksPerSecond * 15 && sim.World.Get<Creature>(foe).Able; t++)
        {
            sim.Tick();
            var p = sim.World.Get<Position>(dragon).Pos;
            var q = sim.World.Get<Position>(foe).Pos;
            var bearing = MathF.Atan2(p.Y - q.Y, p.X - q.X);
            if (last is { } l)
                turned += MathF.IEEERemainder(bearing - l, 2 * MathF.PI);
            last = bearing;
            Assert.True(!sim.World.Has<FreeMover>(dragon) || sim.World.Get<FreeMover>(dragon).Fighting);
        }
        Assert.True(MathF.Abs(turned) > MathF.PI / 2, $"went {MathF.Abs(turned) * 180 / MathF.PI:0} degrees round");
        Assert.Contains(sim.RecentBlows, b => b.Attacker == dragon && b.Target == foe);
    }

    [Fact]
    public void TheDragonFightsWhereItLandedAndTakesOffFromTheGrid()
    {
        var (sim, dragon) = DragonDown();
        var landing = sim.World.Get<Position>(dragon).Cell;
        var foe = StandBeside(sim, dragon, 3, 0);
        sim.TickMany(SimClock.TicksPerSecond * 2);
        Assert.True(sim.World.Has<FreeMover>(dragon), "fighting freely on the ground");
        // The foe walks off down the street: it doesn't follow far.
        var away = sim.SpiralFrom(new int3(landing.X - 12, landing.Y, landing.Z), 3).First(c => sim.Map.Passable(c));
        sim.World.Get<Position>(foe) = Position.At(away);
        var furthest = 0;
        for (var t = 0; t < DragonPassJob.RestlessTicks + SimClock.TicksPerSecond * 2 && sim.World.Get<Mind>(dragon).Job is DragonPassJob; t++)
        {
            sim.Tick();
            furthest = Math.Max(furthest, Map.Distance(landing, sim.World.Get<Position>(dragon).Cell));
        }
        Assert.True(furthest <= DragonPassJob.Leash + 2, $"went {furthest} cells from where it landed");
        Assert.IsNotType<DragonPassJob>(sim.World.Get<Mind>(dragon).Job);
        Assert.False(sim.World.Has<FreeMover>(dragon), "back on the grid to take off");
    }

    [Fact]
    public void FourMayPressADragonAtOnce()
    {
        var (sim, dragon) = DragonDown();
        Assert.Equal(Engagement.OnDragon, Engagement.Limit(sim, dragon));
    }
}

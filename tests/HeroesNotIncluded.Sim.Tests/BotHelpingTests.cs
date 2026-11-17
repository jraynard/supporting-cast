using HeroesNotIncluded.Sim.Afterlife;
using HeroesNotIncluded.Sim.Bots;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Environment;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Systems;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// Bots pitching in as players do when a round goes wrong (<see cref="BotPlayer"/>, "Helping"):
/// water on a fire, the downed carried to a bed, a neighbour's bleeding bound, the dead called back.
/// </summary>
public class BotHelpingTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid Bea = new("00000000-0000-0000-0000-00000000000b");

    /// <summary>A dealt round with these players in these trades, bots for each.</summary>
    private static (Simulation Sim, List<BotPlayer> Bots) Round(params (Guid Id, string Trade)[] seats) => Round(BotTemperament.Steady, seats);

    private static (Simulation Sim, List<BotPlayer> Bots) Round(BotTemperament temper, params (Guid Id, string Trade)[] seats)
    {
        var sim = VillageRound.NewSimulation(LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        var bots = new List<BotPlayer>();
        foreach (var (id, trade) in seats)
        {
            sim.ApplyNow(new JoinLobbyCommand(id, id.ToString()[^1..], default));
            sim.ApplyNow(new SetTradePreferencesCommand(id, [sim.Defs.Trade(trade)], []));
            bots.Add(new BotPlayer(() => sim, id, c => sim.ApplyNow(c), 7 + (ulong)bots.Count) { Temper = temper });
        }
        sim.ApplyNow(new StartRoundCommand());
        return (sim, bots);
    }

    private static bool Run(Simulation sim, List<BotPlayer> bots, Func<bool> done, int maxTicks)
    {
        for (var t = 0; t < maxTicks && !done(); t++)
        {
            sim.Tick();
            foreach (var b in bots)
                b.Tick();
        }
        return done();
    }

    private static void Kill(Simulation sim, Entity e)
    {
        ref var c = ref sim.World.Get<Creature>(e);
        NeedsSystem.Kill(sim, e, ref c, "bled to death");
    }

    /// <summary>Put <paramref name="who"/> a few steps from <paramref name="target"/>, in the open.</summary>
    private static void Near(Simulation sim, Entity who, Entity target, int steps)
    {
        var at = sim.World.Get<Position>(target).Cell;
        var spot = sim.SpiralFrom(at, steps + 2).First(c => Maps.Map.Distance(c, at) >= steps && c.Z == at.Z && sim.Map.Passable(c)
            && sim.Regions.CanReach(c, at, PathEnd.Touch));
        FireDrill.MoveTo(sim, who, spot);
    }

    [Fact]
    public void ABotAtTheWellThrowsWaterOnTheFire()
    {
        var (sim, bots) = Round((Ana, "Blacksmith"));
        var me = Players.CharacterOf(sim, Ana);
        FireDrill.Stage(sim, me);
        var here = sim.World.Get<Position>(me).Cell;
        var view = Perception.For(sim, me);
        var dbg = string.Join(" ", sim.Fire.BurningTiles.Select(i => sim.Map.CellOf(i)).Select(c => $"{c.X},{c.Y},{c.Z}:{Maps.Map.Distance(c, here)}:{view.CanSeeCell(c)}"));
        var thrown = false;
        Run(sim, bots, () => thrown |= sim.RecentSplashes.Any(s => s.Thrower == me), SimClock.TicksPerHour);
        Assert.True(thrown, $"never threw: {bots[0].Doing} at {here.X},{here.Y},{here.Z}; {dbg}");
    }

    [Fact]
    public void ABotWithEmptyHandsFetchesABucketAndFillsIt()
    {
        var (sim, bots) = Round((Ana, "Blacksmith"));
        var me = Players.CharacterOf(sim, Ana);
        FireDrill.Stage(sim, me);
        // Nothing in hand: it has to take a bucket down from the well and draw it first.
        var held = sim.World.Get<Carrying>(me).Item;
        sim.World.Get<Carrying>(me).Item = Entity.Null;
        sim.DestroyThing(held);
        var thrown = false;
        Run(sim, bots, () => thrown |= sim.RecentSplashes.Any(s => s.Thrower == me), SimClock.TicksPerHour);
        Assert.True(thrown, $"never threw: {bots[0].Doing}");
    }

    [Fact]
    public void BotsThrowWaterOnTheDragonOnceItsDown()
    {
        var cara = new Guid("00000000-0000-0000-0000-00000000000c");
        var (sim, bots) = Round((Ana, "Blacksmith"), (Bea, "Tinker"), (cara, "Cook"));
        var dragon = DragonDrill.Stage(sim, Players.CharacterOf(sim, Ana))!.Dragon;
        Assert.True(Run(sim, bots, () => sim.World.TryGet<Ai.Doused>(dragon, out var d) && d.Times > 0, SimClock.TicksPerHour * 4),
            $"never soaked it: {string.Join(" / ", bots.Select(b => b.Doing))}");
    }

    [Fact]
    public void ANervyBotHidesFromTheDragonInsteadOfThrowingWater()
    {
        var nervy = BotTemperament.Steady with { Name = "Nervy", Nerve = 0.1f };
        var (sim, bots) = Round(nervy, (Ana, "Blacksmith"));
        var me = Players.CharacterOf(sim, Ana);
        var dragon = DragonDrill.Stage(sim, me)!.Dragon;
        Run(sim, bots, () => false, SimClock.TicksPerHour * 2);
        Assert.False(sim.World.TryGet<Ai.Doused>(dragon, out var d) && d.Times > 0 && sim.RecentSplashes.Any(s => s.Thrower == me));
        Assert.DoesNotContain("throwing water", bots[0].Doing);
    }

    [Fact]
    public void ABotShoutsWhenItSeesAFire()
    {
        var (sim, bots) = Round((Ana, "Blacksmith"));
        var me = Players.CharacterOf(sim, Ana);
        FireDrill.Stage(sim, me);
        Run(sim, bots, () => bots[0].Stats.Said > 0, SimClock.TicksPerHour / 4);
        Assert.Contains(sim.Speech.Kept, l => l.Speaker == me && l.Volume == Speech.Volume.Shout);
    }

    [Fact]
    public void ABotCarriesSomeoneDownedToABed()
    {
        var (sim, bots) = Round((Ana, "Tinker"), (Bea, "Blacksmith"));
        var me = Players.CharacterOf(sim, Ana);
        var bea = Players.CharacterOf(sim, Bea);
        bots.RemoveAt(1); // Bea's down: nobody at her keys
        sim.World.Get<Health>(bea).Blood = 0.4f;
        HealthSystem.SetDowned(sim, bea, ref sim.World.Get<Creature>(bea), true);
        Near(sim, me, bea, 4);
        Assert.True(Run(sim, bots, () => !sim.World.Has<Carried>(bea) && InABed(sim, bea), SimClock.TicksPerHour),
            $"Bea's still lying there: {bots[0].Doing}");
    }

    [Fact]
    public void ABotBindsANeighboursBleeding()
    {
        var (sim, bots) = Round((Ana, "Blacksmith"), (Bea, "Tinker"));
        var me = Players.CharacterOf(sim, Ana);
        var bea = Players.CharacterOf(sim, Bea);
        bots.RemoveAt(1);
        var body = sim.World.Get<Health>(bea).Body;
        HealthSystem.Damage(sim, bea, 6, DamageType.Cut, body.PartIndex("LeftArm"));
        sim.Store(sim.PackOf(me), sim.CreateItem(sim.Thing("Bandage"), 2));
        Near(sim, me, bea, 3);
        Assert.True(Bandaging.WoundsToBind(sim, bea, me) > 0);
        Assert.True(Run(sim, bots, () => Bandaging.WoundsToBind(sim, bea, me) == 0, SimClock.TicksPerHour / 2),
            $"still bleeding: {bots[0].Doing}");
    }

    [Fact]
    public void AHealerBotCallsADeadBotBackAndItAnswers()
    {
        var (sim, bots) = Round((Ana, "Healer"), (Bea, "Blacksmith"));
        var empath = Players.CharacterOf(sim, Ana);
        var bea = Players.CharacterOf(sim, Bea);
        sim.World.Get<Mana>(empath) = new Mana { Current = 100, Max = 100 };
        Kill(sim, bea);
        Assert.False(Ghosts.Of(sim, Bea).IsNull);
        Near(sim, empath, bea, 5);
        Assert.True(Run(sim, bots, () => !sim.World.Get<Creature>(bea).Dead, SimClock.TicksPerHour),
            $"Bea's still dead: {bots[0].Doing} / {bots[1].Doing}");
        Assert.Equal(bea, Players.CharacterOf(sim, Bea));
    }

    [Fact]
    public void ADeadBotsGhostShakesARaider()
    {
        var (sim, bots) = Round((Ana, "Blacksmith"));
        var me = Players.CharacterOf(sim, Ana);
        Kill(sim, me);
        var ghost = Ghosts.Of(sim, Ana);
        sim.World.Get<Ghost>(ghost).Presence = 1f;
        var at = sim.World.Get<Position>(me).Cell;
        var spot = sim.SpiralFrom(at, 12).First(c => Maps.Map.Distance(c, at) >= 8 && c.Z == at.Z && sim.Map.Passable(c));
        var raider = Combat.Raids.SpawnRaider(sim, sim.Defs.AllRaiderKinds[0], "Vask", spot, new SimRandom(5));
        Assert.True(Run(sim, bots, () => Hauntings.IsUnnerved(sim, raider) || !Possession.Ridden(sim, ghost).IsNull, SimClock.TicksPerHour / 2),
            $"never went at the raider: {bots[0].Doing}");
    }

    [Fact]
    public void ADeadBotWhispersToASleepingEmpathToCallItBack()
    {
        var (sim, bots) = Round((Ana, "Blacksmith"), (Bea, "Healer"));
        var me = Players.CharacterOf(sim, Ana);
        var bea = Players.CharacterOf(sim, Bea);
        bots.RemoveAt(1);
        Kill(sim, me);
        var ghost = Ghosts.Of(sim, Ana);
        sim.World.Get<Ghost>(ghost).Presence = 1f;
        Near(sim, bea, me, 6);
        sim.World.Get<Mind>(bea).Asleep = true;
        Assert.True(Run(sim, bots, () => sim.World.Has<Dream>(bea), SimClock.TicksPerHour / 4), $"no dream: {bots[0].Doing}");
        Assert.Contains("call me back", sim.World.Get<Dream>(bea).Text);
    }

    [Fact]
    public void ACarelessBotSlipsBindingItselfWhereASteadyOneDoesNot()
    {
        int Slips(BotTemperament temper)
        {
            var slips = 0;
            for (var go = 0; go < 4; go++)
            {
                var (sim, bots) = Round(temper, (Ana, "Blacksmith"));
                var me = Players.CharacterOf(sim, Ana);
                var body = sim.World.Get<Health>(me).Body;
                HealthSystem.Damage(sim, me, 6, DamageType.Cut, body.PartIndex("LeftArm"));
                sim.Store(sim.PackOf(me), sim.CreateItem(sim.Thing("Bandage"), 2));
                var bot = new BotPlayer(() => sim, Ana, c => sim.ApplyNow(c), 11 + (ulong)go) { Temper = temper, Skill = 0.1f };
                Run(sim, [bot], () => Bandaging.BandagesOn(sim, me) < 2 && !sim.World.Has<Work.WorkSession>(me), SimClock.TicksPerHour / 2);
                slips += bot.Stats.Mistakes;
            }
            return slips;
        }
        Assert.Equal(0, Slips(BotTemperament.Steady));
        Assert.True(Slips(BotTemperament.Steady with { Name = "Careless", Care = 0f }) > 0);
    }

    private static bool InABed(Simulation sim, Entity e)
    {
        var cell = sim.World.Get<Position>(e).Cell;
        var site = sim.Map.Structure[sim.Map.Idx(cell)];
        return !site.IsNull && sim.World.Get<Thing>(site).Def.Building is { IsBed: true };
    }
}

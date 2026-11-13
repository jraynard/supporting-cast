using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Systems;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>Hunting in real time: stalking to bow range, animals bolting or turning, butchering the carcass.</summary>
public class HuntingTests
{
    private static (Simulation Sim, Entity Hunter) Setup(bool bow = true)
    {
        var sim = Create(60, 60);
        var hunter = sim.SpawnCreature(sim.Defs.Creature("Human"), "Aldric", C(10, 30));
        sim.SetSkills(hunter);
        if (bow)
            sim.Equip(hunter, sim.Thing("Bow"));
        return (sim, hunter);
    }

    private static Entity Animal(Simulation sim, string kind, int3 at, bool hunt = true)
    {
        var e = sim.SpawnCreature(sim.Defs.Creature(kind), kind, at, Faction.Wild);
        if (hunt)
            sim.World.Add(e, new HuntOrder());
        return e;
    }

    private static int Meat(Simulation sim) =>
        sim.World.Query<Thing>().Where(e => sim.World.Get<Thing>(e) is { Spawned: true } t && t.Def.Name == "Meat")
            .Sum(e => sim.World.Get<Stack>(e).Count);

    private static bool TickUntil(Simulation sim, Func<bool> done, int maxTicks)
    {
        for (var t = 0; t < maxTicks; t++)
        {
            if (done())
                return true;
            sim.Tick();
        }
        return done();
    }

    [Fact]
    public void AnArcherShootsAHareAndButchersIt()
    {
        var (sim, hunter) = Setup();
        var hare = Animal(sim, "Hare", C(32, 30));
        Assert.True(TickUntil(sim, () => sim.World.Get<Creature>(hare).Dead, SimClock.HoursToTicks(1)), "the hare should be shot");
        Assert.False(sim.World.Has<HuntOrder>(hare));
        Assert.Equal(1, sim.Wildlife.Hunted);

        // Then the hunter cuts it up where it fell.
        Assert.True(TickUntil(sim, () => !sim.World.IsAlive(hare), SimClock.HoursToTicks(1)), "the carcass should be butchered");
        Assert.Equal(sim.Defs.Creature("Hare").Animal!.MeatYield, Meat(sim));
        Assert.True(sim.World.Get<Skills>(hunter).Xp[(int)Skill.Shooting] > 0, "hunting trains Shooting");
    }

    [Fact]
    public void AWoundedBoarTurnsOnItsHunter()
    {
        var (sim, hunter) = Setup();
        var boar = Animal(sim, "Boar", C(30, 30));
        var charged = TickUntil(sim, () => sim.World.Get<Mind>(boar).Job is AnimalAttackJob, SimClock.HoursToTicks(1));
        Assert.True(charged, "the boar should charge once it's hit");
        Assert.True(TickUntil(sim, () => sim.World.Get<Health>(hunter).Injuries.Count > 0, SimClock.TicksPerSecond * 60),
            "the boar should gore the hunter");
    }

    [Fact]
    public void NobodyHuntsWithoutABow()
    {
        var (sim, hunter) = Setup(bow: false);
        var hare = Animal(sim, "Hare", C(30, 30));
        sim.TickMany(SimClock.TicksPerSecond * 30);
        Assert.False(sim.World.Get<Mind>(hunter).Job is HuntJob);
        Assert.True(sim.World.Get<Creature>(hare).Able);
    }

    [Fact]
    public void LiftingTheOrderCallsTheHuntOff()
    {
        var (sim, hunter) = Setup();
        var deer = Animal(sim, "Deer", C(45, 30));
        Assert.True(TickUntil(sim, () => sim.World.Get<Mind>(hunter).Job is HuntJob, SimClock.TicksPerSecond * 10));
        sim.World.Remove<HuntOrder>(deer);
        sim.TickMany(5);
        Assert.False(sim.World.Get<Mind>(hunter).Job is HuntJob);
    }

    [Fact]
    public void AHurtAnimalsHerdBoltsWithIt()
    {
        var (sim, hunter) = Setup();
        sim.Wildlife.Enable(sim);
        var herd = sim.Wildlife.SendHerd(sim, sim.Defs.Creature("Deer"));
        Assert.True(herd.Count >= 2);
        sim.World.Add(herd[0], new HuntOrder());
        Assert.True(TickUntil(sim, () => sim.World.Has<Provoked>(herd[1]), SimClock.HoursToTicks(2)),
            "a herd-mate should notice the shot");
        sim.TickMany(2);
        Assert.IsType<FleeJob>(sim.World.Get<Mind>(herd[1]).Job);
    }

    // ---------------- Adventure: the player's own hunting (Ai/AvatarHunting.cs) ----------------

    private static (Simulation Sim, Entity Me) Player(bool bow = true)
    {
        var (sim, me) = Setup(bow);
        sim.World.Add(me, new Avatar());
        return (sim, me);
    }

    [Fact]
    public void FShootsAnAnimalInBowRangeOnlyWhileHeld()
    {
        var (sim, me) = Player();
        // Beyond the hare's flee radius (9 cells) but inside the bow's 14: it hasn't noticed you.
        var hare = Animal(sim, "Hare", C(20, 30), hunt: false);
        sim.World.Get<Mind>(hare).Drafted = true; // keeps still for the bow's draw (Combat.Blows), as a hare grazing might
        var action = AvatarSystem.ActionAt(sim, me, C(20, 30));
        Assert.Equal(AvatarActionKind.Hunt, action.Kind);
        Assert.Equal("Shoot the hare", action.Label);

        AvatarSystem.Perform(sim, me, action); // started, but the key isn't held
        Assert.IsType<AvatarHuntJob>(sim.World.Get<Mind>(me).Job);
        sim.TickMany(20);
        Assert.Empty(sim.RecentShots);

        sim.ApplyNow(new AvatarWorkCommand(me, true));
        Assert.True(TickUntil(sim, () => sim.RecentShots.Count > 0, SimClock.TicksPerSecond * 5), "holding F looses an arrow");
        Assert.True(sim.World.Get<Skills>(me).Xp[(int)Skill.Hunting] > 0, "it trains Hunting");
        Assert.True(sim.World.Has<Provoked>(hare), "the hare knows it's hunted");
    }

    [Fact]
    public void BareHandsOnlyReachWhatsBesideYou()
    {
        var (sim, me) = Player(bow: false);
        Animal(sim, "Hare", C(18, 30), hunt: false);
        Assert.NotEqual(AvatarActionKind.Hunt, AvatarSystem.ActionAt(sim, me, C(18, 30)).Kind);
        Animal(sim, "Hare", C(11, 30), hunt: false);
        var action = AvatarSystem.ActionAt(sim, me, C(11, 30));
        Assert.Equal(AvatarActionKind.Hunt, action.Kind);
        Assert.StartsWith("Strike at", action.Label);
    }

    [Fact]
    public void FButchersACarcassInReachWhileHeld()
    {
        var (sim, me) = Player();
        var deer = Animal(sim, "Deer", C(11, 30), hunt: false);
        sim.World.Get<Creature>(deer).Dead = true;
        Assert.Equal(AvatarActionKind.None, AvatarSystem.ActionAt(sim, me, C(14, 30)).Kind); // nothing there
        var action = AvatarSystem.ActionAt(sim, me, C(11, 30));
        Assert.Equal(AvatarActionKind.Butcher, action.Kind);

        sim.ApplyNow(new AvatarWorkCommand(me, true, C(11, 30)));
        Assert.IsType<ButcherJob>(sim.World.Get<Mind>(me).Job);
        Assert.True(TickUntil(sim, () => !sim.World.IsAlive(deer), SimClock.TicksPerSecond * 60), "the deer is cut up");
        Assert.True(Meat(sim) > 0);
    }

    [Fact]
    public void HoldingFKeepsShootingTheHare()
    {
        var (sim, me) = Player();
        var hare = Animal(sim, "Hare", C(20, 30), hunt: false);
        sim.ApplyNow(new AvatarWorkCommand(me, true, C(20, 30)));
        // Hit, it bolts; the player keeps F held and presses it again whenever it's back in range.
        var down = TickUntil(sim, () =>
        {
            if (sim.World.Get<Mind>(me).Job == null && sim.World.Get<Creature>(hare) is { Dead: false }
                && AvatarSystem.ActionAt(sim, me, sim.World.Get<Position>(hare).Cell).Kind == AvatarActionKind.Hunt)
                sim.ApplyNow(new AvatarWorkCommand(me, true, sim.World.Get<Position>(hare).Cell));
            return sim.World.Get<Creature>(hare).Dead;
        }, SimClock.TicksPerSecond * 30);
        Assert.True(down || sim.World.Get<Health>(hare).Injuries.Count > 0, "the hare is shot (down, or wounded and away)");
        if (down)
            Assert.True(sim.Messages.Messages.Any(m => m.Text.Contains("brought down the hare")), string.Join(" | ", sim.Messages.Messages.Select(m => m.Text)));
    }
}

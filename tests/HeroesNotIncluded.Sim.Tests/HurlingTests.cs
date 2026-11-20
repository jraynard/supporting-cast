using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>A player picking up a stool and hurling it (<see cref="Hurling"/>).</summary>
public class HurlingTests
{
    private static readonly Guid Ana = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

    private static (Simulation Sim, Entity Me, Entity Stool) AtAStool()
    {
        var sim = VillageRound.NewSimulation(TestWorld.LoadDefs(), 1234);
        sim.Presets.PresetMode = true;
        VillageRound.Setup(sim);
        sim.Round.OpenLobby();
        sim.ApplyNow(new JoinLobbyCommand(Ana, "Arin", default));
        sim.ApplyNow(new StartRoundCommand());
        var me = Players.CharacterOf(sim, Ana);
        var inn = ShopSystem.BuildingFor(sim, TownRole.Tavern);
        var stool = sim.World.Query<Thing>().First(e => Hurling.Throwable(sim, e) && Buildings.At(sim, sim.World.Get<Thing>(e).Cell) == inn);
        var cell = sim.World.Get<Thing>(stool).Cell;
        var beside = sim.SpiralFrom(cell, 1).First(c => c != cell && c.Z == cell.Z && sim.Map.Passable(c) && AvatarSystem.InReach(c, cell));
        FireDrill.MoveTo(sim, me, beside);
        sim.TickMany(2);
        return (sim, me, stool);
    }

    private static void FistsUp(Simulation sim, Entity me)
    {
        sim.ApplyNow(new DrawWeaponCommand(me, true));
        sim.TickMany(2);
    }

    [Fact]
    public void FistsUpFAtAStoolPicksItUpAndFSetsItDown()
    {
        var (sim, me, stool) = AtAStool();
        var cell = sim.World.Get<Thing>(stool).Cell;
        var here = sim.World.Get<Position>(me).Cell;
        Assert.NotEqual("Pick up the stool", AvatarSystem.ActionAt(sim, me, cell).Label); // sheathed: it's a seat
        FistsUp(sim, me);
        var pick = AvatarSystem.ActionAt(sim, me, cell);
        Assert.Equal("Pick up the stool", pick.Label);
        Assert.True(Hurling.Act(sim, me, pick));
        Assert.False(sim.World.IsAlive(stool));
        Assert.True(Hurling.Holding(sim, me));
        Assert.Throws<InvalidOperationException>(() => { if (!Blows.Begin(sim, me, Entity.Null, CombatRules.Attacks(sim, me)[0])) throw new InvalidOperationException(); }); // no punching with it
        var down = AvatarSystem.ActionAt(sim, me, cell);
        Assert.Equal("Set the stool down", down.Label);
        Assert.True(Hurling.Act(sim, me, down));
        Assert.False(Hurling.Holding(sim, me));
        Assert.True(Hurling.Throwable(sim, sim.Map.Structure[sim.Map.Idx(cell)]), "back where it was set");
        Assert.Equal(here, sim.World.Get<Position>(me).Cell);
    }

    [Fact]
    public void DrawnBackAndHurledItBreaksOverWhoeverItHits()
    {
        var (sim, me, stool) = AtAStool();
        FistsUp(sim, me);
        var from = sim.World.Get<Thing>(stool).Cell;
        Assert.True(Hurling.Act(sim, me, AvatarSystem.ActionAt(sim, me, from)));
        // Someone two steps off, in a straight line from where they stand.
        var here = sim.World.Get<Position>(me).Cell;
        var (dx, dy) = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) }.First(d =>
            sim.Map.Passable(new int3(here.X + d.Item1, here.Y + d.Item2, here.Z)) && sim.Map.Passable(new int3(here.X + 2 * d.Item1, here.Y + 2 * d.Item2, here.Z)));
        // A guest (friendly fire's off: a stool passes through the town as a blow does).
        var target = sim.Parties.Send(sim, sim.Defs.Party("GildedSpurs"))[0];
        sim.ApplyNow(new HurlCommand(me, true, false, 0f, sim.Clock.Tick));
        Assert.Equal(0f, Hurling.Charge(sim, me), 3);
        sim.TickMany(SimClock.TicksPerSecond / 2);
        var half = Hurling.Charge(sim, me);
        Assert.InRange(half, 0.3f, 0.7f); // the gauge fills as it's held
        sim.TickMany(SimClock.TicksPerSecond);
        Assert.Equal(1f, Hurling.Charge(sim, me));
        FireDrill.MoveTo(sim, target, new int3(here.X + 2 * dx, here.Y + 2 * dy, here.Z)); // stood there as it's thrown
        var hit = Hurling.Throw(sim, me, MathF.Atan2(dy, dx));
        Assert.Equal(target, hit);
        Assert.False(Hurling.Holding(sim, me));
        Assert.Contains(sim.RecentShots, s => s.Sprite == "buildings/stool");
        Assert.Equal(me, sim.World.Get<Provoked>(target).By);
        Assert.Contains(Carpentry.Refurnishing.Of(sim).Losses, l => l.Cell == from && l.Def.Name == "Stool"); // gone: the carpenter's to make again
    }

    [Fact]
    public void NotDrawnBackItIsntThrown()
    {
        var (sim, me, stool) = AtAStool();
        FistsUp(sim, me);
        Assert.True(Hurling.Act(sim, me, AvatarSystem.ActionAt(sim, me, sim.World.Get<Thing>(stool).Cell)));
        Assert.Null(Hurling.Throw(sim, me, 0f));
        Assert.True(Hurling.Holding(sim, me));
    }

    [Fact]
    public void DownedWithAStoolInHandItsSetDownAndASaveKeepsOneHeld()
    {
        var (sim, me, stool) = AtAStool();
        FistsUp(sim, me);
        Assert.True(Hurling.Act(sim, me, AvatarSystem.ActionAt(sim, me, sim.World.Get<Thing>(stool).Cell)));
        var (loaded, _) = Saving.SaveGame.Read(Saving.SaveGame.Write(sim), sim.Defs);
        Assert.Equal("Stool", loaded.World.Get<Hefting>(me).Def.Name);
        var stools = sim.World.Query<Thing>().Count(e => Hurling.Throwable(sim, e));
        HealthSystem.SetDowned(sim, me, ref sim.World.Get<Creature>(me), true);
        Assert.False(Hurling.Holding(sim, me));
        Assert.Equal(stools + 1, sim.World.Query<Thing>().Count(e => Hurling.Throwable(sim, e)));
    }
}

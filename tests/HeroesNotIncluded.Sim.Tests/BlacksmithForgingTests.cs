using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>Forging a piece a heat at a time: the fire, the anvil, the quench, and the flaws a careless hand leaves.</summary>
public class BlacksmithForgingTests
{
    private static readonly Guid Ana = new("00000000-0000-0000-0000-00000000000a");

    private static (Simulation Sim, Entity Me, SmithyKit Kit) Smithy(string recipe = "ForgeSword")
    {
        var sim = Create(40, 40);
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), "Smith", C(10, 10), ageDays: 25 * 365);
        sim.Round.OpenLobby();
        sim.Round.Sit(Ana, "Smith", null, null, null);
        sim.Round.Start(sim);
        var kit = SmithyKit.Place(sim);
        var r = sim.Defs.Recipe(recipe);
        sim.World.Get<Workbench>(kit.Forge).Begin(r, me);
        Smithing.BeginMaking(sim, me, kit.Forge, r);
        return (sim, me, kit);
    }

    private static Entity Made(Simulation sim) => sim.World.Query<Crafted>().Single();

    [Fact]
    public void ASwordTakesSeveralHeatsAndComesOffTheQuenchBlunt()
    {
        var (sim, me, kit) = Smithy();
        var heats = 0;
        var wasHeating = false;
        for (var t = 0; t < 60 * 60 * 10 && Smithing.PieceOf(sim, me) != null; t += 60)
        {
            kit.Drive(sim, me, maxTicks: 60);
            var heating = sim.World.TryGet<WorkSession>(me, out var s) && s is HeatSession;
            if (heating && !wasHeating)
                heats++;
            wasHeating = heating;
        }

        Assert.True(heats >= 2, $"{heats} heats");
        var sword = Made(sim);
        Assert.True(sim.World.Get<Crafted>(sword).Quality >= Quality.Fine);
        Assert.Equal(1f, WeaponWear.Dullness(sim, sword));
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("Grind it an edge"));
    }

    [Fact]
    public void AnUnblownFireIsTooCoolForIronTillTheBellowsBringItUp()
    {
        var (sim, me, kit) = Smithy(); // the forge's cell is at 1200: the building has been hot all day
        sim.ApplyNow(new SmithCommand(me, kit.Forge));
        var heat = Assert.IsType<HeatSession>(sim.World.Get<WorkSession>(me));
        Assert.True(heat.Fire < heat.Lo, $"starts at {heat.Fire}, iron works from {heat.Lo}");

        for (var t = 0; t < 60 * 20; t++)
            sim.Tick();
        Assert.True(heat.Fire < heat.Lo, $"left alone it settles at {heat.Fire}");

        // Worked, the bellows bring it to working heat and the piece is drawn out.
        kit.Drive(sim, me, maxTicks: 60 * 60);
        Assert.Contains(sim.Messages.Messages, m => m.Text.StartsWith("Drew the"));
    }

    [Fact]
    public void HammeringBlackIronCracksIt()
    {
        var (sim, me, kit) = Smithy();
        kit.Drive(sim, me, SmithStyle.HammerCold);
        Assert.Equal(PieceFlaw.Crack, sim.World.Get<Flawed>(Made(sim)).Flaw);
    }

    [Fact]
    public void LeftInTheFireItBurnsThrough()
    {
        var (sim, me, kit) = Smithy();
        kit.Drive(sim, me, SmithStyle.LeaveInFire);

        Assert.Null(Smithing.PieceOf(sim, me));
        Assert.Empty(sim.World.Query<Crafted>());
        Assert.Null(sim.World.Get<Workbench>(kit.Forge).Active);
        Assert.Contains(sim.Messages.Messages, m => m.Text.Contains("burned through"));
    }

    [Fact]
    public void QuenchedTooLongItsBrittleAndTooShortItsSoft()
    {
        var (sim, me, kit) = Smithy();
        kit.Drive(sim, me, SmithStyle.QuenchTooLong);
        Assert.Equal(PieceFlaw.Brittle, sim.World.Get<Flawed>(Made(sim)).Flaw);

        (sim, me, kit) = Smithy();
        kit.Drive(sim, me, SmithStyle.QuenchTooShort);
        Assert.Equal(PieceFlaw.Soft, sim.World.Get<Flawed>(Made(sim)).Flaw);
    }

    [Fact]
    public void AFlawDoesNotShowButTellsInUse()
    {
        var (sim, me, kit) = Smithy();
        kit.Drive(sim, me, SmithStyle.QuenchTooShort);
        var soft = Made(sim);
        var sound = sim.CreateItem(sim.Thing("ShortSword"), 1);
        Inventory.Stow(sim, me, sound);
        WeaponWear.SetDullness(sim, soft, 0f);
        WeaponWear.SetDullness(sim, sound, 0f);

        // Looked over, a soft blade and a sound one read the same.
        var view = Perception.For(sim, me);
        Assert.Equal(view.Condition(sound), view.Condition(soft));

        // In a fight the soft one loses its edge twice as fast.
        WeaponWear.Struck(sim, me, soft, false, 0f);
        WeaponWear.Struck(sim, me, sound, false, 0f);
        Assert.Equal(WeaponWear.Dullness(sim, sound) * 2f, WeaponWear.Dullness(sim, soft), 4);
    }

    [Fact]
    public void ArmorIsFinishedOffTheAnvil()
    {
        var (sim, me, kit) = Smithy("ForgeHelm");
        Assert.False(Smithing.NeedsQuench(sim.Defs.Recipe("ForgeHelm")));
        kit.Drive(sim, me);
        Assert.Null(Smithing.PieceOf(sim, me));
        Assert.Equal("ForgeHelm", sim.Defs.Recipe("ForgeHelm").Name);
        Assert.NotNull(sim.World.Get<Thing>(Made(sim)).Def.Apparel);
    }

    [Fact]
    public void OthersSeeTheGlowButNotTheFlaw()
    {
        var (sim, me, kit) = Smithy();
        var other = sim.SpawnCreature(sim.Defs.Creature("Human"), "Onlooker", C(12, 12), ageDays: 25 * 365);
        Smithing.PieceOf(sim, me)!.Temp = 900f;
        Smithing.PieceOf(sim, me)!.Flaw = PieceFlaw.Crack;

        var seen = Perception.For(sim, other).Piece(me);
        Assert.NotNull(seen);
        Assert.Equal(Glow.Orange, seen.Value.Glow);
        Assert.True(seen.Value.Hot);
        Assert.Null(seen.Value.Next); // only the smith knows what they mean to do next
        Assert.NotNull(Perception.For(sim, me).Piece(me)!.Value.Next);
    }

    [Fact]
    public void APieceCoolsInTheAirBetweenSteps()
    {
        var (sim, me, _) = Smithy();
        Smithing.PieceOf(sim, me)!.Temp = 900f;
        sim.TickMany(SimClock.TicksPerSecond * 30);
        Assert.True(Smithing.PieceOf(sim, me)!.Temp < 500f);
    }

    [Fact]
    public void APieceInTheTongsSurvivesASave()
    {
        var (sim, me, kit) = Smithy();
        kit.Drive(sim, me, maxTicks: 60 * 8);
        var before = Smithing.PieceOf(sim, me)!;
        Assert.True(before.Shape > 0f || before.Temp > 500f);

        var (loaded, _) = SaveGame.Read(SaveGame.Snapshot(sim), sim.Defs);
        var after = Smithing.PieceOf(loaded, me)!;
        Assert.Equal(before.Shape, after.Shape);
        Assert.Equal(before.Temp, after.Temp);
        Assert.Equal(before.Recipe, after.Recipe);
        Assert.Equal(sim.FullHash(), loaded.FullHash());
    }

    [Fact]
    public void CallingTheWorkOffAtTheForgeLetsThePieceGo()
    {
        var (sim, me, kit) = Smithy();
        sim.ApplyNow(new CancelCraftCommand(kit.Forge));
        sim.TickMany(20);
        Assert.Null(Smithing.PieceOf(sim, me));
    }

    [Fact]
    public void TheWorkKeyOffersTheNextStepAtEachStation()
    {
        var (sim, me, kit) = Smithy();
        Assert.StartsWith("Put the", Smithing.StepAt(sim, me, kit.Forge));
        Assert.StartsWith("Hammer the", Smithing.StepAt(sim, me, kit.Anvil));
        Assert.Null(Smithing.StepAt(sim, me, kit.Tub)); // not shaped yet
        Smithing.PieceOf(sim, me)!.Shape = 1f;
        Assert.StartsWith("Quench the", Smithing.StepAt(sim, me, kit.Tub));
    }
}

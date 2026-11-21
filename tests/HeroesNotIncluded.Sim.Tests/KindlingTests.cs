using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>Opening the shop: a forge goes cold when it's left, is lit again by hand (flint, steel, breath), and keeps the night banked.</summary>
public class KindlingTests
{
    private static ForgeYard.Yard ColdYard()
    {
        var yard = ForgeYard.Setup(TestWorld.LoadDefs());
        yard.Sim.Tick(); // the forge's hearth is seen
        Kindling.PutOutAll(yard.Sim);
        return yard;
    }

    /// <summary>Light the forge as a careful hand would: strike on the flint, then short breaths. Ticks taken, or -1.</summary>
    private static int Light(ForgeYard.Yard yard, bool holdForever = false, int maxTicks = 60 * 90)
    {
        var sim = yard.Sim;
        Kindling.Start(sim, yard.Smith, yard.Forge);
        var predictor = new WorkPredictor();
        var puffing = false;
        for (var t = 0; t < maxTicks; t++)
        {
            sim.Tick();
            if (!sim.World.Has<WorkSession>(yard.Smith))
                return Kindling.IsCold(sim, yard.Forge) ? -1 : t;
            bool press = false, held = false;
            if (predictor.Session is KindleSession k)
            {
                if (!k.Ember)
                    press = MathF.Abs(k.Steel - KindleSession.StrikeAt) < 0.04f;
                else if (holdForever)
                    held = true;
                else
                {
                    if (puffing && (k.Draft >= KindleSession.GentleHi - 0.08f || k.Breath < 0.08f))
                        puffing = false;
                    else if (!puffing && k.Draft <= 0.02f && k.Breath > 0.3f)
                        puffing = true;
                    held = puffing;
                }
            }
            predictor.Update(sim, yard.Smith, 1, press, held, 0, c => sim.ApplyNow(c));
        }
        return -1;
    }

    [Fact]
    public void ACarefulHandLightsAColdForge()
    {
        var yard = ColdYard();
        Assert.True(Kindling.IsCold(yard.Sim, yard.Forge));
        Assert.False(yard.Sim.Burning(yard.Forge));
        var took = Light(yard);
        Assert.True(took > 0, "never lit");
        Assert.False(Kindling.IsCold(yard.Sim, yard.Forge));
        Assert.True(took < 60 * 45, $"took {took / 60} seconds");
    }

    [Fact]
    public void BreathHeldTooLongBlowsTheEmberOut()
    {
        var yard = ColdYard();
        var sim = yard.Sim;
        Kindling.Start(sim, yard.Smith, yard.Forge);
        var session = (KindleSession)sim.World.Get<WorkSession>(yard.Smith);
        // A strike dead on the flint, then a breath held and held.
        var ticks = 0;
        var predictor = new WorkPredictor();
        while (ticks++ < 60 * 20 && (predictor.Session as KindleSession ?? session) is var k && k.Lost == 0)
        {
            sim.Tick();
            var press = predictor.Session is KindleSession { Ember: false } s && MathF.Abs(s.Steel - KindleSession.StrikeAt) < 0.04f;
            predictor.Update(sim, yard.Smith, 1, press, predictor.Session is KindleSession { Ember: true }, 0, c => sim.ApplyNow(c));
        }
        Assert.True(((KindleSession)predictor.Session!).Lost > 0, "a held breath should have blown it out");
        Assert.True(Kindling.IsCold(sim, yard.Forge));
    }

    [Fact]
    public void AColdForgeWontHeatAPiece()
    {
        var yard = ColdYard();
        yard.Sim.ApplyNow(new SmithCommand(yard.Smith, yard.Forge));
        Assert.False(yard.Sim.World.Has<WorkSession>(yard.Smith) && yard.Sim.World.Get<WorkSession>(yard.Smith) is HeatSession);
    }

    [Fact]
    public void LeftItGoesColdBankedItKeeps()
    {
        var left = ForgeYard.Setup(TestWorld.LoadDefs());
        var banked = ForgeYard.Setup(TestWorld.LoadDefs());
        foreach (var yard in (ForgeYard.Yard[])[left, banked])
        {
            // Nothing to make: the forge sits idle.
            Smithing.PieceOf(yard.Sim, yard.Smith)!.Forge = Entity.Null;
            yard.Sim.World.Remove<WorkPiece>(yard.Smith);
            yard.Sim.World.Get<Workbench>(yard.Forge).Active = null;
            for (var t = 0; t <= new KindlingSystem().Interval; t++)
                yard.Sim.Tick(); // the hearth is seen
        }
        Assert.True(Kindling.Bank(banked.Sim, banked.Smith, banked.Forge));
        var hours = (int)Kindling.ColdAfterHours + 4;
        foreach (var yard in (ForgeYard.Yard[])[left, banked])
            for (var t = 0; t < SimClock.HoursToTicks(hours); t++)
                yard.Sim.Tick();
        Assert.True(Kindling.IsCold(left.Sim, left.Forge), "left, it should have gone out");
        Assert.False(Kindling.IsCold(banked.Sim, banked.Forge), "banked, it should have kept");
    }

    [Fact]
    public void ARoundOpensWithTheForgeCold()
    {
        var sim = TestWorld.Create();
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), "Smith", TestWorld.C(10, 10), ageDays: 25 * 365);
        var forge = sim.SpawnStructure(sim.Defs.Thing("Forge"), TestWorld.C(11, 10));
        sim.Tick();
        Assert.False(Kindling.IsCold(sim, forge));
        sim.Round.OpenLobby();
        sim.Round.Sit(ForgeYard.Player, "Smith", null, null);
        sim.Round.Start(sim);
        Assert.True(Kindling.IsCold(sim, forge));
        Assert.Equal("Light the Forge", Kindling.StepAt(sim, me, forge));
    }
}

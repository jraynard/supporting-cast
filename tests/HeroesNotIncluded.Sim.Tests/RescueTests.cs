using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Systems;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>Rescuing downed dragons (<see cref="Rescues"/>): carried to a nest, or fed and tended where they lie.</summary>
public class RescueTests
{
    private static Simulation Mild()
    {
        var sim = Create(40, 40);
        sim.Climate.BaseTemperature = 20;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 20f);
        return sim;
    }

    private static Entity Person(Simulation sim, int3 at, string name, bool avatar = false)
    {
        var e = sim.SpawnCreature(sim.Defs.Creature("Human"), name, at, ageDays: 25 * 365);
        sim.SetSkills(e);
        if (avatar)
            sim.World.Add(e, new Avatar());
        return e;
    }

    /// <summary>Down a creature and keep it down a while: low on blood.</summary>
    private static void Down(Simulation sim, Entity e)
    {
        sim.World.Get<Health>(e).Blood = 0.4f;
        HealthSystem.SetDowned(sim, e, ref sim.World.Get<Creature>(e), true);
    }

    private static bool TickUntil(Simulation sim, Func<bool> done, int maxTicks)
    {
        for (var t = 0; t < maxTicks && !done(); t++)
            sim.Tick();
        return done();
    }

    [Fact]
    public void ThePlayerCarriesACarcassToTheButcherSpotAndDropsItIfTheyGoDown()
    {
        var sim = Mild();
        var me = Person(sim, C(10, 10), "Wren", avatar: true);
        sim.SpawnStructure(sim.Thing("ButcherSpot"), C(10, 16));
        var deer = sim.SpawnCreature(sim.Defs.Creature("Deer"), "Deer", C(11, 10), Faction.Wild);
        NeedsSystem.Kill(sim, deer, ref sim.World.Get<Creature>(deer));

        Assert.Equal(AvatarActionKind.Carry, AvatarSystem.ActionAt(sim, me, C(11, 10)).Kind);
        sim.ApplyNow(new AvatarWorkCommand(me, true, C(11, 10)));
        Assert.Equal(deer, Rescues.CarriedBy(sim, me));

        sim.World.Get<Position>(me) = Position.At(C(10, 14));
        var setDown = AvatarSystem.ActionAt(sim, me, C(10, 15));
        Assert.Contains("butcher spot", setDown.Label);

        Down(sim, me);
        sim.Tick();
        Assert.True(Rescues.CarriedBy(sim, me).IsNull, "going down drops what's in your arms");
        Assert.Equal(C(10, 14), sim.World.Get<Position>(deer).Cell);
    }
}

using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Work;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>How a test smith goes about it: a careful hand, or one particular mistake.</summary>
public enum SmithStyle
{
    Careful,
    BadAim,
    HammerCold,
    QuenchTooLong,
    QuenchTooShort,
    LeaveInFire,
}

/// <summary>
/// A forge with an anvil, quench tub and grindstone round a smith at (10, 10), and a player who
/// drives a piece through every step with commands and the predictor, as a client would.
/// </summary>
public sealed record SmithyKit(Entity Forge, Entity Anvil, Entity Tub, Entity Grindstone)
{
    public static SmithyKit Place(Simulation sim)
    {
        var forge = sim.SpawnStructure(sim.Thing("Forge"), C(11, 10));
        sim.SpawnStructure(sim.Thing("Bellows"), C(12, 10));
        var anvil = sim.SpawnStructure(sim.Thing("Anvil"), C(9, 10));
        var tub = sim.SpawnStructure(sim.Thing("QuenchTub"), C(10, 11));
        var wheel = sim.SpawnStructure(sim.Thing("Grindstone"), C(10, 9));
        sim.RebuildDerivedIfNeeded();
        sim.World.Get<Fueled>(forge).Fuel = 10;
        sim.Map.Temperature[sim.Map.Idx(C(11, 10))] = 1200;
        return new SmithyKit(forge, anvil, tub, wheel);
    }

    /// <summary>Work the piece in the smith's tongs to the end (or until <paramref name="maxTicks"/>): the fire, the anvil, the tub, as it needs.</summary>
    public void Drive(Simulation sim, Entity me, SmithStyle style = SmithStyle.Careful, int maxTicks = 60 * 60 * 10)
    {
        var predictor = new WorkPredictor();
        var stopped = -1L;
        for (var t = 0; t < maxTicks; t++)
        {
            sim.Tick();
            var w = sim.World;
            if (!w.Has<WorkSession>(me))
            {
                if (Smithing.PieceOf(sim, me) is not { } p)
                    break;
                var lo = Smithing.Window(p.Recipe).Lo;
                var hot = p.Temp >= lo;
                // Hammering cold: wait for the colour to go before taking it to the anvil.
                if (style == SmithStyle.HammerCold && !p.Shaped && p.Temp >= lo * 0.62f)
                {
                    if (p.Temp < lo * 0.78f)
                        sim.ApplyNow(new SmithCommand(me, Anvil));
                    continue;
                }
                var station = !hot ? Forge : !p.Shaped ? Anvil : Tub;
                sim.ApplyNow(new SmithCommand(me, station));
                continue;
            }
            // Nothing from the last step's play reaches the next one: catch up to the new session first.
            if (predictor.Session?.StartTick != w.Get<WorkSession>(me).StartTick)
            {
                predictor.Update(sim, me, 1, false, false, 0, c => sim.ApplyNow(c));
                continue;
            }
            bool press = false, held = false, act = false;
            switch (predictor.Session)
            {
                case HeatSession h:
                    held = true;
                    act = style != SmithStyle.LeaveInFire && h.LooksReady;
                    break;
                case HammerSession h:
                    // A careful smith goes back to the fire as the colour fades.
                    if (style != SmithStyle.HammerCold && h.Workable < 0.95f && stopped != h.StartTick)
                    {
                        stopped = h.StartTick;
                        sim.ApplyNow(new StopWorkCommand(me));
                        continue;
                    }
                    var off = MathF.Abs(h.Marker - h.Mark);
                    press = h.Ready && (style == SmithStyle.BadAim ? off >= 0.5f : off <= h.HalfWidth * 0.5f);
                    break;
                case QuenchSession q:
                    held = style switch
                    {
                        SmithStyle.QuenchTooShort => !q.Dunked || q.Temp > 700f,
                        SmithStyle.QuenchTooLong => true,
                        _ => !(q.Dunked && q.Steam < 0.2f),
                    };
                    break;
            }
            predictor.Update(sim, me, 1, press, held, 0, c => sim.ApplyNow(c), act: act);
        }
        sim.Tick();
    }

    /// <summary>Grind the edge on a blade at the wheel, steering for the band and easing off when it's hot.</summary>
    public void Grind(Simulation sim, Entity me, int maxTicks = 60 * 120)
    {
        sim.ApplyNow(new SmithCommand(me, Grindstone));
        var predictor = new WorkPredictor();
        var resting = false;
        for (var t = 0; t < maxTicks && sim.World.Has<WorkSession>(me); t++)
        {
            sim.Tick();
            var axis = 0;
            var hold = false;
            if (predictor.Session is SharpenSession s)
            {
                var off = s.Angle - 0.5f;
                axis = MathF.Abs(off) > s.HalfWidth * 0.3f ? -Math.Sign(off) : 0;
                resting = resting ? s.Heat > 0.3f : s.Heat > 0.8f;
                hold = !resting && s.InBand;
            }
            predictor.Update(sim, me, 1, false, hold, axis, c => sim.ApplyNow(c));
        }
        sim.Tick();
    }
}

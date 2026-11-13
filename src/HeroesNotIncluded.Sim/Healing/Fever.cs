using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Healing;

/// <summary>
/// A fever (<see cref="DamageType.Fever"/>) is a wound with nothing to see: an injury on the torso
/// that hurts, grows while nobody sees to it, and passes to whoever spends time close by (sat at
/// the same table, lying on the next pallet). Seen to (bound and rested), it stops growing and the
/// body throws it off in time; the empath can draw it and quell it (Rime and Still). Nobody names
/// it from outside: the healer feels it as a heat in the blood.
/// </summary>
public sealed class FeverSystem(ulong seed) : ISimSystem, ISaveState
{
    public string Name => "Fever";
    public int Interval => SimClock.TicksPerHour / 4;

    /// <summary>How bad a fever is when it's caught, and the worst it gets (well short of killing).</summary>
    public const float Caught = 5f, Worst = 12f;

    /// <summary>How much an unattended fever grows each game hour.</summary>
    public const float GrowthPerHour = 1f;

    /// <summary>Within this many cells (on the same level), a fever may pass.</summary>
    public const int Reach = 2;

    /// <summary>Chance each quarter hour that someone close to a fevered person catches it.</summary>
    public const float CatchChance = 0.05f;

    private readonly SimRandom _rand = new(seed ^ 0xFE7E_12u);

    public void Update(Simulation sim)
    {
        var w = sim.World;
        List<Entity>? sick = null;
        foreach (var e in sim.Creatures())
            if (w.TryGet<Health>(e, out var h) && !w.Get<Creature>(e).Dead && h.Injuries.Exists(i => i.Type == DamageType.Fever))
                (sick ??= []).Add(e);
        if (sick == null)
            return;
        var grow = GrowthPerHour * Interval / SimClock.TicksPerHour;
        foreach (var e in sick)
            foreach (var inj in w.Get<Health>(e).Injuries)
                if (inj.Type == DamageType.Fever && !inj.Tended)
                {
                    inj.Damage = Math.Min(Worst, inj.Damage + grow);
                    inj.PeakDamage = Math.Max(inj.PeakDamage, inj.Damage);
                }
        foreach (var e in sick)
        {
            var at = w.Get<Position>(e).Cell;
            foreach (var o in sim.Creatures())
            {
                if (o == e || sick.Contains(o) || w.Get<Creature>(o) is not { Dead: false, Def.Race: Race.Human })
                    continue;
                var there = w.Get<Position>(o).Cell;
                if (there.Z != at.Z || Map.Distance(there, at) > Reach)
                    continue;
                if (_rand.Chance(CatchChance))
                    Catch(sim, o);
            }
        }
    }

    public static bool Has(Simulation sim, Entity e) =>
        sim.World.TryGet<Health>(e, out var h) && h.Injuries.Exists(i => i.Type == DamageType.Fever);

    /// <summary>Someone comes down with it: an injury on the torso, if they haven't one already.</summary>
    public static void Catch(Simulation sim, Entity e, float amount = Caught)
    {
        if (Has(sim, e))
            return;
        var body = sim.World.Get<Creature>(e).Def.BodyDef;
        var torso = body?.PartIndex("Torso") ?? -1;
        HealthSystem.Damage(sim, e, amount, DamageType.Fever, torso);
    }

    public void Save(SaveWriter w, Simulation sim)
    {
        var (a, b, c, d) = _rand.State;
        w.UInt(a); w.UInt(b); w.UInt(c); w.UInt(d);
    }

    public void Load(SaveReader r, Simulation sim) => _rand.State = (r.UInt(), r.UInt(), r.UInt(), r.UInt());
}

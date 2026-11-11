using DragonWorld.Sim.Components;
using DragonWorld.Sim.Defs;

namespace DragonWorld.Sim.Systems;

/// <summary>
/// Wounds over time: untended injuries bleed, blood slowly comes back once they stop, injuries
/// heal (faster when tended, faster still in bed), and creatures collapse when blood, pain or a
/// head wound get the better of them, getting up again once they recover. <see cref="Damage"/> is
/// the one way to hurt a creature's body.
/// </summary>
public sealed class HealthSystem : ISimSystem
{
    public string Name => "Health";
    public int Interval => 60;

    private const float UpdatesPerHour = SimClock.TicksPerHour / 60f;
    private const float UpdatesPerDay = SimClock.TicksPerDay / 60f;

    /// <summary>Injury damage healed per game day, per 100 max HP.</summary>
    public const float HealPerDay = 4f, TendedHealPerDay = 12f;

    /// <summary>Blood regained per game day once nothing bleeds.</summary>
    public const float BloodPerDay = 0.5f;

    /// <summary>
    /// Infection race, per game day: severity gains (less the better the wound was tended) against
    /// immunity (×1.3 in bed). Untended, it's roughly a coin flip in bed and fatal out of it.
    /// </summary>
    public const float SeverityPerDay = 0.6f, ImmunityPerDay = 0.5f;

    /// <summary>A wound gets its one chance to fester this long after it's made.</summary>
    public const int InfectionDelayTicks = SimClock.TicksPerHour * 12;

    public void Update(Simulation sim)
    {
        var w = sim.World;
        foreach (var e in sim.Creatures())
        {
            if (!w.TryGet<Health>(e, out var h))
                continue;
            ref var c = ref w.Get<Creature>(e);

            var bleed = h.BleedPerHour;
            h.Blood = bleed > 0 ? h.Blood - bleed / UpdatesPerHour : Math.Min(1f, h.Blood + BloodPerDay / UpdatesPerDay);

            var inBed = InBed(sim, e);
            // In bed, or (too big to carry to one) lying downed by a fire: they mend faster.
            var bed = inBed ? 1.5f : c.Downed && h.Injuries.Count > 0 && ByAFire(sim, e) ? ByFireFactor : 1f;
            for (var i = h.Injuries.Count - 1; i >= 0; i--)
            {
                var inj = h.Injuries[i];
                Fester(sim, e, c, h, inj, inBed);
                if (inj.Infection != null)
                    continue; // a festering wound doesn't close
                inj.Damage -= HealRate(inj) * h.Scale * bed / UpdatesPerDay;
                if (inj.Damage <= 0)
                {
                    h.Injuries.RemoveAt(i);
                    Scar(sim, e, c, h, inj);
                }
            }
            h.Changed();

            if (h.Blood <= 0)
            {
                h.Blood = 0;
                NeedsSystem.Kill(sim, e, ref c, "bled to death");
                continue;
            }
            if (h.InfectionSeverity >= 1f)
            {
                NeedsSystem.Kill(sim, e, ref c, "died of an infection");
                continue;
            }
            UpdateDowned(sim, e, ref c, h);
        }
    }

    /// <summary>Damage healed per day per 100 max HP: tending helps, and good tending more (12 at quality 0.5).</summary>
    public static float HealRate(Injury inj) =>
        inj.Tended ? HealPerDay + (TendedHealPerDay - HealPerDay) * (0.4f + 0.8f * inj.Quality) : HealPerDay;

    /// <summary>Roll a wound's one chance to get infected, then run any infection's race.</summary>
    private static void Fester(Simulation sim, Entity e, Creature c, Health h, Injury inj, bool inBed)
    {
        var part = h.Body.Parts[inj.Part].Label;
        if (!inj.InfectionRolled && sim.Clock.Tick - inj.Tick >= InfectionDelayTicks)
        {
            inj.InfectionRolled = true;
            var size = Math.Clamp(inj.PeakDamage / (0.25f * h.PartMaxHp(inj.Part)), 0.2f, 1f);
            var chance = DamageTypes.InfectChance(inj.Type) * size * (inj.Tended ? 1f - inj.Quality : 1f);
            var fireproof = inj.Type == DamageType.Burn && c.Def.SafeTempMax >= 300;
            if (!fireproof && sim.Rand.Chance(chance))
            {
                inj.Infection = new Infection { Severity = 0.1f, ImmunityRate = sim.Rand.Range(0.85f, 1.15f) };
                sim.Messages.Add(c.Faction == Faction.Village ? MessageKind.Warning : MessageKind.Info,
                    $"The {DamageTypes.Wound(inj.Type)} on {c.Name}'s {part} is infected.", sim.World.Get<Position>(e).Cell);
            }
        }

        if (inj.Infection is not { } infection)
            return;
        infection.Severity += SeverityPerDay * (1f - 0.6f * (inj.Tended ? inj.Quality : 0f)) / UpdatesPerDay;
        infection.Immunity += ImmunityPerDay * infection.ImmunityRate * (inBed ? 1.3f : 1f) / UpdatesPerDay;
        if (infection.Immunity >= 1f)
        {
            inj.Infection = null;
            sim.Messages.Add(c.Faction == Faction.Village ? MessageKind.Good : MessageKind.Info,
                $"{c.Name} has fought off the infection in their {part}.", sim.World.Get<Position>(e).Cell);
        }
    }

    /// <summary>A deep wound that has healed may leave a scar (burns most often, careful tending less so).</summary>
    private static void Scar(Simulation sim, Entity e, Creature c, Health h, Injury inj)
    {
        var max = h.PartMaxHp(inj.Part);
        if (inj.PeakDamage < 0.35f * max)
            return;
        if (!sim.Rand.Chance((inj.Type == DamageType.Burn ? 0.8f : 0.5f) * (1f - 0.5f * inj.Quality)))
            return;
        var scarred = 0f;
        foreach (var s in h.Scars)
            if (s.Part == inj.Part)
                scarred += s.Damage;
        var damage = Math.Min(0.1f * max, 0.4f * max - scarred); // scars alone never cripple a part
        if (damage <= 0)
            return;
        h.Scars.Add(new Scar(inj.Part, inj.Type, damage));
        if (c.Faction == Faction.Village)
            sim.Messages.Add(MessageKind.Info, $"{c.Name}'s {DamageTypes.Wound(inj.Type)} has left a scar on the {h.Body.Parts[inj.Part].Label}.",
                sim.World.Get<Position>(e).Cell);
    }

    /// <summary>Collapse below these; get up again with some margin (no flickering at the threshold).</summary>
    private static void UpdateDowned(Simulation sim, Entity e, ref Creature c, Health h)
    {
        var conscious = h.Capacity(Capacity.Consciousness);
        if (!c.Downed && (h.Blood < 0.45f || h.Pain > 0.8f || conscious < 0.3f))
            SetDowned(sim, e, ref c, true);
        else if (c.Downed && h.Blood >= 0.5f && h.Pain <= 0.7f && conscious >= 0.35f)
            SetDowned(sim, e, ref c, false);
    }

    public static void SetDowned(Simulation sim, Entity e, ref Creature c, bool downed)
    {
        if (c.Downed == downed)
            return;
        c.Downed = downed;
        var cell = sim.World.Get<Position>(e).Cell;
        if (downed)
        {
            RidingSystem.OnIncapacitated(sim, e);
            Scenarios.EscapeCamp.OnIncapacitated(sim, e);
            var mind = sim.World.Get<Mind>(e);
            AiSystem.EndJob(sim, e, mind, false);
            mind.Asleep = false;
            sim.World.Get<Mover>(e).Stop();
            sim.Messages.Add(c.Faction == Faction.Village ? MessageKind.Danger : MessageKind.Info, $"{c.Name} has collapsed.", cell);
        }
        else
        {
            sim.Messages.Add(MessageKind.Good, $"{c.Name} is back on their feet.", cell);
        }
    }

    /// <summary>How much faster a downed creature mends lying by a lit fire (where nobody can carry it to bed).</summary>
    public const float ByFireFactor = 1.25f;

    private List<Environment.Warmth.Source>? _fires;
    private long _firesTick = -1;

    /// <summary>Within a lit fire's warmth (<see cref="Environment.Warmth"/>), looked up once an update.</summary>
    private bool ByAFire(Simulation sim, Entity e)
    {
        if (_firesTick != sim.Clock.Tick)
        {
            _fires = Environment.Warmth.Sources(sim);
            _firesTick = sim.Clock.Tick;
        }
        return _fires!.Count > 0 && Environment.Warmth.Radiant(sim, sim.World.Get<Position>(e).Cell, _fires) > 0;
    }

    public static bool InBed(Simulation sim, Entity e)
    {
        var s = sim.Map.Structure[sim.Map.Idx(sim.World.Get<Position>(e).Cell)];
        return !s.IsNull && sim.World.Get<Thing>(s).Def.Building?.IsBed == true && !sim.World.Has<Construction>(s);
    }

    /// <summary>
    /// Hurt a creature: <paramref name="amount"/> damage to <paramref name="part"/>, or to a part
    /// picked by coverage (favoring <paramref name="bias"/> heights). Repeated untended wounds of the
    /// same kind on a part merge. Destroying a vital part kills. Creatures without a body lose
    /// <see cref="Creature.Hp"/> instead. Returns the injury, or null if nothing was hurt.
    /// </summary>
    public static Injury? Damage(Simulation sim, Entity e, float amount, DamageType type, int part = -1, PartHeight? bias = null)
    {
        var w = sim.World;
        ref var c = ref w.Get<Creature>(e);
        if (c.Dead || amount <= 0)
            return null;
        if (!w.TryGet<Health>(e, out var h))
        {
            c.Hp -= amount;
            if (c.Hp <= 0)
                NeedsSystem.Kill(sim, e, ref c);
            return null;
        }

        if (part < 0)
            part = PickPart(sim, h, bias);
        amount = Math.Min(amount, h.PartMaxHp(part) - h.PartDamage(part));
        if (amount <= 0)
            return null;

        var injury = h.Injuries.Find(i => i.Part == part && i.Type == type && !i.Tended);
        if (injury == null)
        {
            injury = new Injury { Part = part, Type = type, Tick = sim.Clock.Tick };
            h.Injuries.Add(injury);
        }
        injury.Damage += amount;
        injury.PeakDamage = Math.Max(injury.PeakDamage, injury.Damage);
        h.Changed();

        var def = h.Body.Parts[part];
        var cell = w.Get<Position>(e).Cell;
        if (h.PartHealth(part) <= 0 && def.Vital)
        {
            NeedsSystem.Kill(sim, e, ref c, $"died of a destroyed {def.Label}");
            return injury;
        }
        if (amount >= 0.3f * h.PartMaxHp(part))
            sim.Messages.AddThrottled(
                $"wound:{e.Index}:{part}",
                SimClock.TicksPerHour,
                c.Faction == Faction.Village ? MessageKind.Warning : MessageKind.Info,
                h.PartHealth(part) <= 0
                    ? $"{c.Name}'s {def.Label} has been mangled."
                    : $"{c.Name} has a bad {DamageTypes.Wound(type)} on the {def.Label}.",
                cell
            );
        if (!c.Downed && (h.Pain > 0.8f || h.Capacity(Capacity.Consciousness) < 0.3f))
            SetDowned(sim, e, ref c, true);
        return injury;
    }

    /// <summary>A part to hit, weighted by coverage; heights matching the bias count double, the opposite half.</summary>
    public static int PickPart(Simulation sim, Health h, PartHeight? bias = null)
    {
        var parts = h.Body.Parts;
        Span<float> weights = stackalloc float[parts.Count];
        var total = 0f;
        for (var i = 0; i < parts.Count; i++)
        {
            var wgt = h.PartHealth(i) > 0 ? parts[i].Coverage : 0f; // a mangled limb can't be hit again
            if (bias is { } b && b != PartHeight.Mid)
                wgt *= parts[i].Height == b ? 2f : parts[i].Height == PartHeight.Mid ? 1f : 0.5f;
            weights[i] = wgt;
            total += wgt;
        }
        var roll = sim.Rand.NextFloat() * total;
        for (var i = 0; i < parts.Count; i++)
        {
            roll -= weights[i];
            if (roll < 0)
                return i;
        }
        return parts.Count - 1;
    }

    /// <summary>A random part at a height (falls on the leg, burn on the feet...), or any part if none.</summary>
    public static int PartAt(Simulation sim, Health h, PartHeight height)
    {
        var candidates = new List<int>();
        for (var i = 0; i < h.Body.Parts.Count; i++)
            if (h.Body.Parts[i].Height == height && h.PartHealth(i) > 0)
                candidates.Add(i);
        return candidates.Count > 0 ? candidates[sim.Rand.NextInt(candidates.Count)] : PickPart(sim, h);
    }
}

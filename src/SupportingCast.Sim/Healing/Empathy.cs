using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Healing;

/// <summary>A wound as an empath feels it (<c>PerceptionView.FeltWounds</c>). <see cref="Key"/> names it in commands.</summary>
public readonly record struct FeltWound(int Key, string Part, string Feels, string Severity, bool Festering, bool Absorbed);

/// <summary>
/// The empath's gift (docs/DESIGN.md, "Healer"): drawing a patient's wound into their own body,
/// and mending what they carry with the right <see cref="MendingDef"/>. Whoever has
/// <see cref="Mana"/> has the gift. A wound is named by its part and kind (<see cref="KeyOf"/>),
/// since wounds of one kind on one part merge.
/// </summary>
public static class Empathy
{
    /// <summary>A master takes on this share of what they draw; a novice all of it.</summary>
    public const float MasterBurden = 0.6f;

    /// <summary>A drawn wound never leaves a vital part of the empath with less than this share of its health.</summary>
    public const float VitalMargin = 0.15f;

    /// <summary>Extra damage a snapped link leaves the empath, as a share of the wound.</summary>
    public const float Backlash = 0.1f;

    /// <summary>Mana regained per game hour at rest, as a share of the most they hold, before pain.</summary>
    public const float RegenPerHour = 0.06f;

    public static bool Gifted(Simulation sim, Entity e) => sim.World.Has<Mana>(e);

    public static int KeyOf(Injury inj) => inj.Part * 64 + (int)inj.Type;

    public static Injury? Find(Simulation sim, Entity who, int key) =>
        sim.World.TryGet<Health>(who, out var h) ? h.Injuries.Find(i => KeyOf(i) == key) : null;

    /// <summary>How much of a drawn wound the empath takes on, by their Medicine.</summary>
    public static float Burden(Simulation sim, Entity healer) =>
        1f - (1f - MasterBurden) * Math.Clamp(SkillRules.LevelOf(sim, healer, Skill.Medicine) / (float)Skills.Max, 0f, 1f);

    /// <summary>How a wound of this kind feels to carry, from whichever mending heals it.</summary>
    public static string Feel(Simulation sim, DamageType type)
    {
        foreach (var m in sim.Defs.AllMendings)
            if (m.Feels.TryGetValue(type, out var feels))
                return feels;
        return "a pain you can't place";
    }

    public static string Severity(Health h, Injury inj)
    {
        var share = inj.Damage / Math.Max(1f, h.PartMaxHp(inj.Part));
        return share < 0.15f ? "faint" : share < 0.35f ? "sharp" : share < 0.6f ? "heavy" : "overwhelming";
    }

    /// <summary>
    /// Draw <paramref name="fraction"/> of a patient's wound into the healer, at the healer's
    /// burden, capped so it never kills them outright. A link that snapped leaves a backlash.
    /// An infection comes across with a wound drawn whole. Returns the damage taken off the patient.
    /// </summary>
    public static float Draw(Simulation sim, Entity healer, Entity patient, int key, float fraction, bool snapped)
    {
        var w = sim.World;
        if (Find(sim, patient, key) is not { } wound || !w.TryGet<Health>(healer, out var mine) || !w.TryGet<Health>(patient, out var theirs))
            return 0f;
        // The same part of the healer's body (by name, should the patient be built differently).
        var part = theirs.Body == mine.Body ? wound.Part : mine.Body.Parts.FindIndex(p => p.Label == theirs.Body.Parts[wound.Part].Label);
        if (part < 0)
            return 0f;
        var burden = Burden(sim, healer);
        var drawn = wound.Damage * Math.Clamp(fraction, 0f, 1f);
        // Room left in the healer's part (a vital one keeps a margin).
        var room = mine.PartMaxHp(part) - mine.PartDamage(part);
        if (mine.Body.Parts[part].Vital)
            room -= VitalMargin * mine.PartMaxHp(part);
        drawn = Math.Max(0f, Math.Min(drawn, room / burden));
        if (drawn <= 0f)
            return 0f;
        var whole = drawn >= wound.Damage - 0.01f;

        wound.Damage -= drawn;
        var infection = whole ? wound.Infection : null;
        if (whole)
            theirs.Injuries.Remove(wound);
        theirs.Changed();

        var taken = HealthSystem.Damage(sim, healer, drawn * burden, wound.Type, part);
        if (taken != null)
        {
            taken.Absorbed = true;
            if (infection != null && taken.Infection == null)
            {
                taken.Infection = infection;
                taken.InfectionRolled = true;
            }
        }
        if (snapped)
            HealthSystem.Damage(sim, healer, drawn * Backlash, wound.Type, part);
        mine.Changed();
        sim.Errands.Treated(sim, patient, healer);
        return drawn;
    }

    /// <summary>
    /// Cast a mending on a wound the caster carries, at a cast quality 0..1. The mana goes either
    /// way; only the right mending takes hold. Returns whether it healed anything.
    /// </summary>
    public static bool Cast(Simulation sim, Entity caster, int key, MendingDef mending, float quality)
    {
        var w = sim.World;
        if (!w.Has<Mana>(caster))
            return false;
        ref var mana = ref w.Get<Mana>(caster);
        mana.Current = Math.Max(0f, mana.Current - mending.ManaCost);
        if (Find(sim, caster, key) is not { } wound || !w.TryGet<Health>(caster, out var h))
            return false;
        var fits = mending.Heals.Contains(wound.Type);
        var cleansed = mending.Cleanses && wound.Infection != null && quality >= 0.5f;
        if (!fits && !cleansed)
            return false;
        if (cleansed)
            wound.Infection = null;
        if (fits)
        {
            wound.Damage -= mending.Heal * (0.25f + 0.75f * Math.Clamp(quality, 0f, 1f));
            if (wound.Damage <= 0.01f)
                h.Injuries.Remove(wound);
        }
        h.Changed();
        return true;
    }
}

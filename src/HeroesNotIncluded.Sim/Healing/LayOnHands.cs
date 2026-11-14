using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Healing;

/// <summary>
/// The AI empath's gift, worked the way a player works it but without the hands-on games: draw a
/// wound in, then weave the mending that fits it on yourself. A bandage only stops the bleeding,
/// and a bound wound still takes days to close: in a three-day season, a hurt player with only an
/// AI healer to go to would carry it to the end. The AI draws only what its mana will mend at
/// once (so it never walks off carrying a patient's wounds it can't shift), and binds the rest.
/// Its picks read the true wounds, as any AI's do: the secrets rule is for what players are shown.
/// </summary>
public static class LayOnHands
{
    /// <summary>A wound smaller than this isn't worth the mana: it's left to close on its own.</summary>
    public const float MendFrom = 1.5f;

    /// <summary>Wounds adding up to this, a villager the AI runs is seen to unasked.</summary>
    public const float UnaskedFrom = 8f;

    /// <summary>Still carrying a wound worth mending (what an order to be mended waits on).</summary>
    public static bool Hurt(Simulation sim, Entity who) =>
        sim.World.TryGet<Health>(who, out var h) && h.Injuries.Exists(i => i.Damage >= MendFrom || i.NeedsTending(sim.Clock.Tick));

    /// <summary>The cast quality an AI empath counts on, by their Medicine (a roll either side of it lands).</summary>
    public static float ExpectedQuality(Simulation sim, Entity healer) =>
        Math.Clamp(0.45f + 0.03f * SkillRules.LevelOf(sim, healer, Skill.Medicine), 0.3f, 0.9f);

    /// <summary>
    /// The mending this empath would weave for a wound of this kind: one in their book that heals
    /// it, the most healing for the mana. Searing burns the caster, so it's only for want of another.
    /// </summary>
    public static MendingDef? MendingFor(Simulation sim, Entity healer, DamageType type)
    {
        MendingDef? best = null;
        foreach (var m in sim.Defs.AllMendings)
        {
            if (m.CallsBack || !m.Heals.Contains(type) || !Weaving.Knows(sim, healer, m))
                continue;
            var better = best == null
                || ((best.Sears > 0) != (m.Sears > 0) ? m.Sears <= 0 : m.HealAt(0) / m.CostAt(0) > best.HealAt(0) / best.CostAt(0));
            if (better)
                best = m;
        }
        return best;
    }

    /// <summary>The cleansing this empath knows for festering, if any.</summary>
    public static MendingDef? CleansingFor(Simulation sim, Entity healer)
    {
        foreach (var m in sim.Defs.AllMendings)
            if (m.Cleanses && Weaving.Knows(sim, healer, m))
                return m;
        return null;
    }

    /// <summary>
    /// What drawing this wound and mending it whole would take: the mending, how many casts, and
    /// the mana for them (a cleansing included, if it festers). Null if it can't be mended.
    /// </summary>
    public static (MendingDef Mending, int Casts, float Mana)? Plan(Simulation sim, Entity healer, Injury wound)
    {
        if (MendingFor(sim, healer, wound.Type) is not { } m)
            return null;
        var carried = wound.Damage * Empathy.Burden(sim, healer);
        var perCast = m.HealAt(0) * (0.25f + 0.75f * ExpectedQuality(sim, healer));
        var casts = Math.Max(1, (int)MathF.Ceiling(carried / perCast));
        var mana = casts * m.CostAt(0);
        if (wound.Infection != null)
        {
            if (CleansingFor(sim, healer) is not { } cleanse)
                return null;
            mana += cleanse.CostAt(0);
        }
        return (m, casts, mana);
    }

    private static float ManaOf(Simulation sim, Entity e) => sim.World.TryGet<Mana>(e, out var m) ? m.Current : 0f;

    /// <summary>A wound of theirs this empath could draw and mend now, with the mana they have.</summary>
    public static bool CanMendAny(Simulation sim, Entity healer, Entity patient) =>
        sim.World.TryGet<Health>(patient, out var h) && h.Injuries.Exists(i => i.Damage >= MendFrom && !i.Absorbed
            && Plan(sim, healer, i) is { } p && p.Mana <= ManaOf(sim, healer));

    /// <summary>Something this empath can do for them: a wound to mend, or one to bind.</summary>
    private static bool CanHelp(Simulation sim, Entity healer, Entity patient) =>
        CanMendAny(sim, healer, patient)
        || (sim.World.TryGet<Health>(patient, out var h) && h.Injuries.Exists(i => i.NeedsTending(sim.Clock.Tick)));

    /// <summary>An empath of a shrine (an AI's trade with the gift).</summary>
    private static bool ShrineEmpath(Simulation sim, Entity pawn, out BuildingDef shrine)
    {
        shrine = null!;
        if (!Empathy.Gifted(sim, pawn) || !sim.World.TryGet<Trade>(pawn, out var trade) || trade.Def.WorkplaceDef is not { } workplace)
            return false;
        shrine = workplace;
        return true;
    }

    /// <summary>
    /// A healing order at this empath's shrine, front of the line first: they take it on and lay
    /// hands on whoever asked. An order to be mended (a player's) waits on the wounds, not the
    /// bleeding. Replaces the plain bandaging an AI healer used to answer with.
    /// </summary>
    public static Job? ForOrder(Simulation sim, Entity pawn)
    {
        if (!ShrineEmpath(sim, pawn, out var shrine))
            return Shops.HealOrders.For(sim, pawn);
        var w = sim.World;
        var now = sim.Clock.Tick;
        var from = w.Get<Position>(pawn).Cell;
        foreach (var e in sim.Shops.FrontFirst(sim))
        {
            if (!e.IsOpen || e.Kind.Kind != ServiceKind.Healing || e.Kind.AtDef != shrine || !e.KnownBy.Contains(pawn)
                || (!e.Assignee.IsNull && e.Assignee != pawn) || !w.IsAlive(e.Poster) || e.Poster == pawn
                || !w.TryGet<Health>(e.Poster, out var h)
                || !(e.Mend ? CanHelp(sim, pawn, e.Poster) : h.Injuries.Exists(i => i.NeedsTending(now)))
                || sim.Reservations.IsReservedByOther(e.Poster, pawn)
                || !Navigation.CanReach(sim, pawn, from, w.Get<Position>(e.Poster).Cell, PathEnd.Touch))
                continue;
            if (e.Assignee.IsNull && !sim.Shops.Accept(sim, pawn, e.Id))
                continue;
            return new LayOnHandsJob(e.Poster);
        }
        return null;
    }

    /// <summary>
    /// A villager the AI runs, badly hurt (<see cref="UnaskedFrom"/>) and with a wound this empath
    /// can mend now, the worst first: the healer goes to them. Players and adventurers ask.
    /// </summary>
    public static Job? ForVillager(Simulation sim, Entity pawn)
    {
        if (!ShrineEmpath(sim, pawn, out _))
            return null;
        var w = sim.World;
        var from = w.Get<Position>(pawn).Cell;
        var best = Entity.Null;
        var worst = 0f;
        foreach (var e in sim.Creatures())
        {
            if (e == pawn || w.Get<Creature>(e) is not { Faction: Faction.Village, Dead: false } || w.Has<Avatar>(e) || w.Has<PlayerCharacter>(e)
                || w.Has<Adventurer>(e) || w.Has<Carried>(e) || !w.TryGet<Health>(e, out var h))
                continue;
            var hurt = h.Injuries.Sum(i => i.Damage);
            if (hurt < UnaskedFrom || hurt <= worst || !CanMendAny(sim, pawn, e) || sim.Reservations.IsReservedByOther(e, pawn)
                || !Navigation.CanReach(sim, pawn, from, w.Get<Position>(e).Cell, PathEnd.Touch))
                continue;
            best = e;
            worst = hurt;
        }
        return best.IsNull ? null : new LayOnHandsJob(best);
    }

    /// <summary>A wound the empath carries (drawn, or their own) that a mending in their book heals, and the mana for it.</summary>
    public static Job? ForSelf(Simulation sim, Entity pawn)
    {
        if (!ShrineEmpath(sim, pawn, out _) || !sim.World.TryGet<Health>(pawn, out var h))
            return null;
        var mana = ManaOf(sim, pawn);
        foreach (var inj in h.Injuries.OrderByDescending(i => i.Absorbed).ThenByDescending(i => i.Damage))
            if (inj.Damage >= MendFrom && MendingFor(sim, pawn, inj.Type) is { } m && m.CostAt(0) <= mana)
                return new MendSelfJob(Empathy.KeyOf(inj));
        return null;
    }

    /// <summary>
    /// Weave a mending on yourself, paying its mana; a roll either side of the expected quality.
    /// False if the mana's short (nothing spent).
    /// </summary>
    public static bool CastOnSelf(Simulation sim, Entity healer, int key, MendingDef m)
    {
        ref var mana = ref sim.World.Get<Mana>(healer);
        var cost = m.CostAt(0);
        if (mana.Current < cost - 0.01f)
            return false;
        mana.Current = Math.Max(0f, mana.Current - cost);
        var quality = Math.Clamp(ExpectedQuality(sim, healer) + sim.Rand.Range(-0.1f, 0.1f), 0.05f, 1f);
        Empathy.Cast(sim, healer, key, m, quality);
        return true;
    }
}

/// <summary>
/// An AI empath lays hands on a patient (<see cref="LayOnHands"/>): feels them over, then for
/// each wound its mana runs to, the worst first, draws it in and weaves its mending; anything
/// still bleeding after that is bound, as any healer would. Follows a patient who wanders off.
/// </summary>
public sealed class LayOnHandsJob(Entity patient) : Job
{
    public override Entity Focus => patient;

    /// <summary>Feeling them over; drawing one wound; weaving one mending (ticks).</summary>
    public const int FeelTicks = 120, DrawTicks = 240, CastTicks = 180;

    protected override IEnumerable<JobStatus> Run()
    {
        var name = Get<Creature>(patient).Name;
        Report = $"Laying hands on {name}";
        if (!Reserve(patient)) yield break;

        foreach (var s in Beside(FeelTicks)) yield return s;
        if (HasFailed) yield break;

        var mended = 0;
        // One wound at a time, re-planned each time: the patient may have taken more, or the mana
        // run lower than it looked.
        for (var tries = 0; tries < 12; tries++)
        {
            var health = Get<Health>(patient);
            var mana = Sim.World.Get<Mana>(Pawn).Current;
            Injury? next = null;
            (MendingDef Mending, int Casts, float Mana)? planned = null;
            foreach (var inj in health.Injuries.OrderByDescending(i => i.Damage))
                if (inj.Damage >= LayOnHands.MendFrom && !inj.Absorbed && LayOnHands.Plan(Sim, Pawn, inj) is { } p && p.Mana <= mana)
                {
                    next = inj;
                    planned = p;
                    break;
                }
            if (next == null || planned is not { } plan)
                break;
            var key = Empathy.KeyOf(next);
            var festered = next.Infection != null;

            Report = $"Drawing {name}'s wound";
            foreach (var s in Beside(DrawTicks)) yield return s;
            if (HasFailed) yield break;
            if (Empathy.Find(Sim, patient, key) == null)
                continue; // it went (another healer, a mending of their own)
            if (Empathy.Draw(Sim, Pawn, patient, key, 1f, snapped: false, out var carried) <= 0f || carried == 0)
                break; // no room left in the empath for it

            Report = "Weaving a mending";
            if (festered && LayOnHands.CleansingFor(Sim, Pawn) is { } cleanse)
            {
                foreach (var s in Wait(CastTicks)) yield return s;
                LayOnHands.CastOnSelf(Sim, Pawn, carried, cleanse);
            }
            // The planned casts, and one more should the rolls fall short and the mana allow.
            for (var c = 0; c < plan.Casts + 1 && Empathy.Find(Sim, Pawn, carried) is { } still && still.Damage >= 0.01f; c++)
            {
                foreach (var s in Wait(CastTicks)) yield return s;
                if (!LayOnHands.CastOnSelf(Sim, Pawn, carried, plan.Mending))
                    break;
            }
            mended++;
        }

        // What's left still bleeding is bound.
        var left = Get<Health>(patient);
        var bind = left.Injuries.Count(i => i.NeedsTending(Sim.Clock.Tick));
        if (bind > 0)
        {
            Report = $"Tending {name}";
            foreach (var s in Beside(TendJob.TicksPerInjury * bind)) yield return s;
            if (HasFailed) yield break;
            var medicine = SkillRules.LevelOf(Sim, Pawn, Skill.Medicine);
            foreach (var inj in left.Injuries)
                if (inj.NeedsTending(Sim.Clock.Tick))
                    Bandaging.Dress(Sim, inj, Math.Clamp(0.35f + 0.03f * medicine + Sim.Rand.Range(-0.1f, 0.1f), 0.05f, 1f));
            left.Changed();
        }
        if (mended == 0 && bind == 0)
            yield break;
        Sim.Shops.Treated(Sim, patient, Pawn);
        MoodSystem.Remember(Sim, patient, Sim.Defs.Thoughts.TendedMe, Pawn);
        var healer = Get<Creature>(Pawn).Name;
        Sim.Messages.Add(MessageKind.Good, mended > 0
            ? $"{healer} drew {name}'s {(mended == 1 ? "wound" : "wounds")} and mended {(mended == 1 ? "it" : "them")}{(bind > 0 ? ", and bound the rest" : "")}."
            : $"{healer} bandaged {name}'s wounds.", Sim.LocationOf(patient));
    }

    /// <summary>Beside the patient for so many ticks, following them should they move off.</summary>
    private IEnumerable<JobStatus> Beside(int ticks)
    {
        for (var done = 0; done < ticks;)
        {
            foreach (var s in GoToEntity(patient, PathEnd.Touch)) yield return s;
            if (HasFailed) yield break;
            while (done < ticks && Pathfinder.Touches(PawnCell, Sim.LocationOf(patient)))
            {
                done++;
                yield return JobStatus.Running;
                if (!Alive(patient) || Get<Creature>(patient).Dead)
                {
                    Fail("patient died");
                    yield break;
                }
            }
        }
    }
}

/// <summary>An AI empath weaves a mending on a wound they carry, where they stand.</summary>
public sealed class MendSelfJob(int wound) : Job
{
    protected override IEnumerable<JobStatus> Run()
    {
        Report = "Weaving a mending";
        foreach (var s in Wait(LayOnHandsJob.CastTicks)) yield return s;
        if (Empathy.Find(Sim, Pawn, wound) is not { } inj || LayOnHands.MendingFor(Sim, Pawn, inj.Type) is not { } m)
            yield break;
        LayOnHands.CastOnSelf(Sim, Pawn, wound, m);
    }
}

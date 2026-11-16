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
    private static bool ShrineEmpath(Simulation sim, Entity pawn, out TownRole shrine)
    {
        shrine = TownRole.None;
        if (!Empathy.Gifted(sim, pawn) || !sim.World.TryGet<Trade>(pawn, out var trade) || !trade.Def.HasWorkplace)
            return false;
        shrine = trade.Def.Role;
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
            if (!e.IsOpen || e.Kind.Kind != ServiceKind.Healing || e.Kind.AtRole != shrine || !e.KnownBy.Contains(pawn)
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

    /// <summary>Mana (a share of the most) below which a shrine's AI empath goes to sit at its stone.</summary>
    public const float MeditateBelow = 0.4f;

    /// <summary>Mana (a share of the most) at which an AI empath with nothing else to do weaves a charm to sell.</summary>
    public const float CharmsFrom = 0.8f;

    /// <summary>Charms an AI empath keeps on show at once, and the price it asks for each.</summary>
    public const int CharmsOnShow = 2, CharmPrice = 4;

    /// <summary>The meditation stone at an empath's shrine, Entity.Null if none.</summary>
    private static Entity StoneOf(Simulation sim, TownRole shrine)
    {
        var b = Shops.ShopSystem.BuildingFor(sim, shrine);
        if (b.IsNull)
            return Entity.Null;
        foreach (var part in Buildings.Parts(sim, b))
            if (sim.World.Get<Thing>(part).Def.Building is { Meditation: > 0 } && !sim.World.Has<Construction>(part))
                return part;
        return Entity.Null;
    }

    /// <summary>
    /// Mana run low (a busy day empties it): the shrine's empath sits at its meditation stone a
    /// while to win it back, rather than turning the hurt away all afternoon. After the orders and
    /// their own wounds, which still come first while there's mana for them.
    /// </summary>
    public static Job? Meditate(Simulation sim, Entity pawn)
    {
        if (!ShrineEmpath(sim, pawn, out var shrine) || !sim.World.TryGet<Mana>(pawn, out var mana) || mana.Current >= mana.Max * MeditateBelow)
            return null;
        var stone = StoneOf(sim, shrine);
        if (stone.IsNull || sim.Reservations.IsReservedByOther(stone, pawn)
            || !Navigation.CanReach(sim, pawn, sim.World.Get<Position>(pawn).Cell, sim.World.Get<Thing>(stone).Cell, PathEnd.Touch))
            return null;
        return new MeditateJob(stone);
    }

    /// <summary>
    /// A quiet hour with mana to spare: weave a charm into a strip of linen and put it out at the
    /// shrine for the parties to buy, a different mending each time, while fewer than
    /// <see cref="CharmsOnShow"/> of theirs are on show. The players' empath does it by hand.
    /// </summary>
    public static Job? WeaveCharm(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        if (!sim.Round.Live || Shops.ShopSystem.SmallHours(sim) || !ShrineEmpath(sim, pawn, out var shrine)
            || !w.TryGet<Mana>(pawn, out var mana) || mana.Current < mana.Max * CharmsFrom || Bandaging.BandagesOn(sim, pawn) <= 0)
            return null;
        var shop = Shops.ShopSystem.BuildingFor(sim, shrine);
        var shown = Shops.Goods.OnShow(sim).Where(g => g.Shop == shop && w.TryGet<Charm>(g.Item, out _)).Select(g => w.Get<Charm>(g.Item).Mending).ToList();
        if (shop.IsNull || shown.Count >= CharmsOnShow)
            return null;
        // A healing mending in their book, not one already on show: wards for the road.
        var pick = sim.Defs.AllMendings.FirstOrDefault(m => !m.CallsBack && m.Heals.Count > 0 && m.Sears <= 0 && Weaving.Knows(sim, pawn, m)
            && !shown.Contains(m) && m.CostAt(0) <= mana.Current);
        var altar = StoneOf(sim, shrine);
        return pick == null || altar.IsNull ? null : new WeaveCharmJob(pick, altar);
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

/// <summary>
/// An AI empath sits at the meditation stone and gathers their mana back: about what a player's
/// sitting that finds the centre gathers, over half an hour. Up and about again once it's full or
/// someone needs them badly (a higher priority job takes over).
/// </summary>
public sealed class MeditateJob(Entity stone) : Job
{
    /// <summary>How long a sitting lasts, and the share of the most mana it gathers over it (more at a better stone).</summary>
    public const int SittingTicks = SimClock.TicksPerHour / 2;
    public const float Gathers = 0.5f;

    public override Entity Focus => stone;

    protected override IEnumerable<JobStatus> Run()
    {
        Report = "Meditating";
        if (!Reserve(stone)) yield break;
        foreach (var s in GoTo(Get<Thing>(stone).Cell, PathEnd.Touch)) yield return s;
        if (HasFailed) yield break;
        var still = MeditateSession.StillnessAt(Sim, Pawn);
        for (var t = 0; t < SittingTicks; t += 60)
        {
            foreach (var s in Wait(60)) yield return s;
            ref var mana = ref Sim.World.Get<Mana>(Pawn);
            var pain = Sim.World.TryGet<Health>(Pawn, out var h) ? Math.Clamp(h.Pain, 0f, 1f) : 0f;
            mana.Current = Math.Min(mana.Max, mana.Current + mana.Max * Gathers * (1f + still) / (SittingTicks / 60f) * (1f - 0.6f * pain));
            if (mana.Current >= mana.Max)
                yield break;
        }
    }
}

/// <summary>An AI empath weaves a mending into linen at the shrine and puts the charm out for sale there.</summary>
public sealed class WeaveCharmJob(MendingDef mending, Entity at) : Job
{
    protected override IEnumerable<JobStatus> Run()
    {
        Report = "Weaving a charm";
        foreach (var s in GoTo(Get<Thing>(at).Cell, PathEnd.Touch)) yield return s;
        if (HasFailed) yield break;
        foreach (var s in Wait(LayOnHandsJob.CastTicks * 2)) yield return s;
        ref var mana = ref Sim.World.Get<Mana>(Pawn);
        if (mana.Current < mending.CostAt(0))
            yield break;
        var quality = Math.Clamp(LayOnHands.ExpectedQuality(Sim, Pawn) + Sim.Rand.Range(-0.1f, 0.1f), 0.05f, 1f);
        var charm = Charms.Make(Sim, Pawn, mending, 0, quality);
        if (charm.IsNull)
            yield break; // no linen after all
        mana.Current = Math.Max(0f, mana.Current - mending.CostAt(0));
        // Laid out in the shrine where the parties will see it, at the price they ask for one.
        var item = Inventory.TakeFromPerson(Sim, Pawn, charm, 1);
        Sim.PlaceItemNear(item, PawnCell);
        Shops.Goods.Set(Sim, Pawn, item, LayOnHands.CharmPrice, true);
    }
}

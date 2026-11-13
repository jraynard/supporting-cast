using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;

namespace HeroesNotIncluded.Sim.Combat;

/// <summary>How the one on the receiving end of a blow means to meet it (an AI's, decided as it's wound up).</summary>
public enum BlowResponse : byte
{
    None,
    Block,
    Parry,
}

/// <summary>
/// A blow being wound up: seen coming from <see cref="Start"/>, landing at <see cref="Impact"/>
/// (<see cref="Blows"/>). It's in plain sight, so the renderer draws it for anyone who can see the
/// attacker. <see cref="Attack"/> indexes <see cref="CombatRules.Attacks"/>, so a weapon dropped
/// meanwhile ends it.
/// </summary>
public struct Windup
{
    public Entity Target;
    public int Attack;
    public bool Heavy;
    public long Start;
    public long Impact;

    /// <summary>An AI target's answer to it, rolled when it began; players answer for themselves.</summary>
    public BlowResponse Response;

    /// <summary>
    /// Where it's aimed, radians (0 along +x, toward +y), fixed as it starts: with free movement
    /// (<see cref="FreeMoveSystem"/>) it lands on whoever's in its sweep that way, so stepping
    /// aside is a dodge.
    /// </summary>
    public float Aim;
}

/// <summary>
/// A fighter's footing: stamina, a raised guard and when it went up (a guard raised just before a
/// blow lands parries it), a dodge's moment of not being there, and being staggered (by a parry, or a
/// guard broken), which leaves them open. Players' moments are stamped with the tick they pressed
/// the key on (<see cref="CombatInputCommand"/>), so a late-arriving key still counts as when it was pressed.
/// </summary>
public struct Guard
{
    /// <summary>0..1: what's left to swing, block and dodge with.</summary>
    public float Stamina;

    /// <summary>When the guard went up; -1 when it's down.</summary>
    public long RaisedAt;

    /// <summary>When it last came down (for a blow that landed between): -1 if never.</summary>
    public long LoweredAt;

    public long DodgeFrom;
    public long DodgeUntil;
    public long StaggeredUntil;

    /// <summary>A parry's reward: the next blow at <see cref="RiposteOn"/> before <see cref="RiposteUntil"/> is a riposte.</summary>
    public Entity RiposteOn;
    public long RiposteUntil;

    /// <summary>A player's next swing may start at this tick (recovery after the last).</summary>
    public long NextSwing;

    public static Guard Fresh => new() { Stamina = 1f, RaisedAt = -1, LoweredAt = -1, LastRaise = -1, DodgeFrom = -1, DodgeUntil = -1, StaggeredUntil = -1, RiposteUntil = -1 };

    /// <summary>When the guard last went up, kept once it's lowered again; -1 if never.</summary>
    public long LastRaise;

    /// <summary>
    /// Was the guard up at <paramref name="tick"/>? Up now and raised by then, or down now but
    /// raised before it and lowered after (a key let go that arrived after the blow was due).
    /// </summary>
    public readonly bool UpAt(long tick) =>
        RaisedAt >= 0 ? RaisedAt <= tick : LastRaise >= 0 && LastRaise <= tick && LoweredAt > tick;
}

/// <summary>
/// A dragon drawing breath before it breathes on <see cref="Target"/> at <see cref="At"/>: in plain
/// sight, so the renderer marks where it'll land. A guard up then takes some of the sting out; only
/// a late dodge gets most of the way clear (<see cref="CombatRules.Breathe"/>).
/// </summary>
public struct Inhaling
{
    public int3 Target;
    public long Start;
    public long At;
}

/// <summary>How a blow ended, for the renderer's words and the sounds. Not sim state.</summary>
public enum BlowOutcome : byte
{
    Hit,
    Miss,
    Blocked,
    Parried,
    Dodged,
    GuardBroken,
    Riposte,
}

/// <summary>A blow of the last moments, for the renderer and sounds. Not part of the sim state.</summary>
public sealed record BlowEvent(long Tick, Entity Attacker, Entity Target, BlowOutcome Outcome, bool Heavy);

/// <summary>
/// Blows that are seen coming. Every melee blow is wound up first (<see cref="Windup"/>), long
/// enough to read: whoever it's aimed at can raise a guard to block it (less hurt, some stamina),
/// raise it just as it lands to parry it (no hurt, and the attacker is left open for a riposte),
/// or step out of reach or dodge. A heavy blow takes longer, hurts more and breaks a tired guard.
/// An arrow (its draw is the windup) is blocked or dodged, never parried; a dragon's jaws come
/// quick, are only partly blocked, and are dodged only late.
/// The wounds themselves are still <see cref="CombatRules"/>'s, through <see cref="RealtimeAttacks.TryAttack"/>.
/// <para>
/// A blow at a player's character lands <see cref="LatencyGrace"/> after its impact: in co-op the
/// player's key reaches the sim late, stamped with when it was pressed, and this lets a parry
/// pressed in time count, as the mini-games replay stamped inputs (<c>WorkPredictor</c>).
/// </para>
/// </summary>
public static class Blows
{
    /// <summary>A guard raised this soon before a blow lands parries it.</summary>
    public const int ParryTicks = SimClock.TicksPerSecond / 5;

    /// <summary>How long a dodge has blows pass through.</summary>
    public const int DodgeTicks = SimClock.TicksPerSecond * 2 / 5;

    /// <summary>How late a blow at a player lands after its impact, for their keys to arrive.</summary>
    public const int LatencyGrace = SimClock.TicksPerSecond / 5;

    /// <summary>The oldest a player's stamped key may be: older, and it's taken as now.</summary>
    public const int MaxStampAge = SimClock.TicksPerSecond;

    /// <summary>Left open after a parry or a broken guard.</summary>
    public const int StaggerTicks = SimClock.TicksPerSecond * 6 / 5;

    /// <summary>How long a parry's riposte waits to be taken.</summary>
    public const int RiposteTicks = SimClock.TicksPerSecond * 3 / 2;

    /// <summary>A player's swings are quicker than the attack's own windup (they're the ones reading the fight).</summary>
    public const float PlayerWindup = 0.45f;

    /// <summary>A player's pause after a blow lands before the next can start.</summary>
    public const int PlayerRecoveryTicks = SimClock.TicksPerSecond * 2 / 5;

    public const float HeavyWindup = 1.8f, HeavyDamage = 1.5f;
    public const float RiposteAccuracy = 0.35f, RiposteDamage = 1.75f;

    /// <summary>Of a blocked blow's hurt, the share that still gets through.</summary>
    public const float BlockedDamage = 0.3f;

    /// <summary>
    /// A dragon's jaws (and its breath, <see cref="CombatRules.Breathe"/>) can't be parried and are
    /// only partly blocked: this share still gets through a guard, which it costs twice the wind.
    /// </summary>
    public const float DragonBlocked = 0.7f;

    /// <summary>
    /// Against a dragon a dodge must be late, started this close before it lands: it's quick and it
    /// covers a lot of ground. Its blows also come quicker than the attack's own windup.
    /// </summary>
    public const int DragonDodgeTicks = SimClock.TicksPerSecond / 7;
    public const float DragonWindup = 0.7f;

    /// <summary>A breath dodged in time still singes: this share of it lands.</summary>
    public const float BreathDodged = 0.5f;

    /// <summary>How long a dragon draws breath before it breathes: long enough to see, not to get clear.</summary>
    public const int InhaleTicks = SimClock.TicksPerSecond * 6 / 5;

    public const float SwingCost = 0.1f, HeavyCost = 0.22f, DodgeCost = 0.2f, BlockCost = 0.18f, ParryCost = 0.04f;

    /// <summary>An AI's odds of parrying a blow it sees coming, and of blocking it, with Melee skill (0..20) on top.</summary>
    public const float AiParry = 0.02f, AiParryPerSkill = 0.008f, AiBlock = 0.12f, AiBlockPerSkill = 0.015f;

    /// <summary>Stamina back per second with the guard down, and with it up.</summary>
    public const float Regen = 0.3f, RegenGuarding = 0.08f;

    /// <summary>A fighter's footing, fresh if they've not fought yet.</summary>
    public static ref Guard GuardOf(Simulation sim, Entity e)
    {
        if (!sim.World.Has<Guard>(e))
            sim.World.Add(e, Guard.Fresh);
        return ref sim.World.Get<Guard>(e);
    }

    public static bool Staggered(Simulation sim, Entity e) =>
        sim.World.TryGet<Guard>(e, out var g) && g.StaggeredUntil > sim.Clock.Tick;

    public static bool WindingUp(Simulation sim, Entity e) => sim.World.Has<Windup>(e);

    /// <summary>
    /// Start winding up a blow at <paramref name="target"/>. False if it can't now: already swinging,
    /// staggered, guarding, too tired, or the attack isn't one this attacker has.
    /// </summary>
    public static bool Begin(Simulation sim, Entity attacker, Entity target, AttackDef attack, bool heavy = false, float? aim = null)
    {
        var w = sim.World;
        var free = sim.FreeMove.Enabled;
        // With free movement a player may swing at the air (Target null): it lands on whoever's in its sweep.
        var aimed = !target.IsNull || !free;
        if (!w.IsAlive(attacker) || attacker == target || w.Has<Windup>(attacker) || !w.TryGet<Creature>(attacker, out var c) || !c.Able
            || (aimed && (!w.IsAlive(target) || !w.TryGet<Creature>(target, out var tc) || tc.Dead)))
            return false;
        var index = CombatRules.Attacks(sim, attacker).IndexOf(attack);
        if (index < 0)
            return false;
        var now = sim.Clock.Tick;
        ref var g = ref GuardOf(sim, attacker);
        var player = w.Has<Avatar>(attacker);
        var cost = heavy ? HeavyCost : SwingCost;
        if (g.StaggeredUntil > now || g.RaisedAt >= 0 || g.Stamina < cost || (player && now < g.NextSwing))
            return false;
        g.Stamina -= cost;
        var ticks = attack.WindupTicks * (heavy ? HeavyWindup : 1f) * (player ? PlayerWindup : 1f) * (IsDragon(sim, attacker) ? DragonWindup : 1f)
            * (free ? Reach.WindupFactor(sim, attacker, attack) : 1f);
        var from = w.Get<Position>(attacker).Pos;
        var windup = new Windup
        {
            Target = target,
            Attack = index,
            Heavy = heavy,
            Start = now,
            Impact = now + Math.Max(1, (int)ticks),
            Response = !aimed || w.Has<Avatar>(target) ? BlowResponse.None : AiResponse(sim, target, heavy, Parryable(sim, attacker, attack)),
            Aim = aim is { } a && float.IsFinite(a) ? a
                : aimed ? DetMath.Atan2(w.Get<Position>(target).Pos.Y - from.Y, w.Get<Position>(target).Pos.X - from.X) : 0f,
        };
        w.Add(attacker, windup);
        if (w.Has<FreeMover>(attacker))
            w.Get<FreeMover>(attacker).Aim = windup.Aim;
        // A player's next may start a moment after this one lands; after a shot, once the bow's ready again.
        if (player)
            g.NextSwing = windup.Impact + (attack.Ranged ? Math.Max(PlayerRecoveryTicks, attack.CooldownTicks - (int)ticks) : PlayerRecoveryTicks);
        return true;
    }

    /// <summary>
    /// How an AI meets a blow it sees coming, rolled once: armed folk block or parry by their
    /// skill, the unarmed and animals take it. A heavy blow is harder to parry.
    /// </summary>
    private static BlowResponse AiResponse(Simulation sim, Entity target, bool heavy, bool parryable)
    {
        var w = sim.World;
        if (!w.TryGet<Equipment>(target, out var eq) || !w.IsAlive(eq.Weapon) || w.Get<Thing>(eq.Weapon).Def.Weapon is not { Ranged: false }
            || !w.TryGet<Skills>(target, out var skills) || !w.Get<Creature>(target).Able)
            return BlowResponse.None;
        var skill = skills[Skill.Melee];
        var parry = (AiParry + AiParryPerSkill * skill) * (heavy ? 0.5f : 1f);
        var block = AiBlock + AiBlockPerSkill * skill;
        var roll = sim.Rand.NextFloat();
        // What can't be parried is blocked by whoever would have tried.
        return roll < parry ? parryable ? BlowResponse.Parry : BlowResponse.Block : roll < parry + block ? BlowResponse.Block : BlowResponse.None;
    }

    /// <summary>Stop a blow being wound up (the job behind it ended, or a guard went up mid-swing).</summary>
    public static void Cancel(Simulation sim, Entity attacker)
    {
        if (sim.World.Has<Windup>(attacker))
            sim.World.Remove<Windup>(attacker);
        if (sim.World.Has<Inhaling>(attacker))
            sim.World.Remove<Inhaling>(attacker);
    }

    public static bool IsDragon(Simulation sim, Entity e) =>
        sim.World.TryGet<Creature>(e, out var c) && c.Def.Race == Race.Dragon;

    /// <summary>A blade can be parried; an arrow or a dragon's jaws can't (they're blocked, or dodged).</summary>
    public static bool Parryable(Simulation sim, Entity attacker, AttackDef attack) => !attack.Ranged && !IsDragon(sim, attacker);

    /// <summary>
    /// Did a dodge carry <paramref name="e"/> clear of something landing at <paramref name="impact"/>?
    /// From a dragon only if it was started late, close before it (<see cref="DragonDodgeTicks"/>).
    /// </summary>
    public static bool DodgedAt(in Guard g, long impact, bool dragon) =>
        g.DodgeFrom <= impact && impact < g.DodgeUntil && (!dragon || impact - g.DodgeFrom <= DragonDodgeTicks);

    /// <summary>A player's stamped key, taken as now if it's from the future or too old.</summary>
    public static long Stamp(Simulation sim, long at)
    {
        var now = sim.Clock.Tick;
        return at > now || at < now - MaxStampAge || at < 0 ? now : at;
    }

    /// <summary>Raise or lower a guard, as of <paramref name="at"/>. Raising one ends your own swing.</summary>
    public static void SetGuard(Simulation sim, Entity e, bool up, long at)
    {
        ref var g = ref GuardOf(sim, e);
        if (up && g.RaisedAt < 0)
        {
            g.RaisedAt = g.LastRaise = at;
            Cancel(sim, e);
        }
        else if (!up && g.RaisedAt >= 0)
        {
            g.LoweredAt = Math.Max(at, g.RaisedAt);
            g.RaisedAt = -1;
        }
    }

    /// <summary>Dodge, as of <paramref name="at"/>: for a moment blows go wide. False if too tired or staggered.</summary>
    public static bool Dodge(Simulation sim, Entity e, long at)
    {
        ref var g = ref GuardOf(sim, e);
        if (g.Stamina < DodgeCost || g.StaggeredUntil > sim.Clock.Tick || g.DodgeUntil > at)
            return false;
        g.Stamina -= DodgeCost;
        g.DodgeFrom = at;
        g.DodgeUntil = at + DodgeTicks;
        Cancel(sim, e);
        return true;
    }

    /// <summary>When a blow wound up now lands: its impact, and a moment more if it's at a player.</summary>
    public static long LandsAt(Simulation sim, in Windup windup) =>
        windup.Impact + (!sim.FreeMove.Enabled && sim.World.Has<Avatar>(windup.Target) ? LatencyGrace : 0);

    /// <summary>
    /// A blow lands (or doesn't). On the town's grid it's at its target, if it's still in reach;
    /// with free movement (<see cref="FreeMoveSystem"/>), on everyone in its sweep the way it was
    /// aimed (<see cref="Reach"/>), and an arrow on the first one along its line.
    /// </summary>
    internal static void Land(Simulation sim, Entity attacker, Windup windup)
    {
        var w = sim.World;
        if (!w.Get<Creature>(attacker).Able)
            return;
        var attacks = CombatRules.Attacks(sim, attacker);
        if (windup.Attack >= attacks.Count)
            return;
        var attack = attacks[windup.Attack];
        if (sim.FreeMove.Enabled)
        {
            var struck = Reach.Struck(sim, attacker, attack, windup.Aim);
            if (struck.Count == 0)
                Report(sim, attacker, windup.Target, BlowOutcome.Miss, windup.Heavy); // into the air, or they stepped out of it
            foreach (var victim in struck)
                Meet(sim, attacker, victim, windup, attack, free: true);
            return;
        }
        var target = windup.Target;
        if (!w.IsAlive(target) || w.Get<Creature>(target).Dead)
            return;
        if (!CombatRules.Chance(sim, attacker, target, attack).Possible)
        {
            Report(sim, attacker, target, BlowOutcome.Miss, windup.Heavy); // stepped out of reach
            return;
        }
        Meet(sim, attacker, target, windup, attack, free: false);
    }

    /// <summary>A blow that reaches <paramref name="target"/>: a dodge, a guard, a parry, then the wound.</summary>
    private static void Meet(Simulation sim, Entity attacker, Entity target, in Windup windup, AttackDef attack, bool free)
    {
        var w = sim.World;
        var impact = windup.Impact;
        ref var mine = ref GuardOf(sim, attacker);
        ref var theirs = ref GuardOf(sim, target);
        var dragon = IsDragon(sim, attacker);
        if (DodgedAt(theirs, impact, dragon))
        {
            Report(sim, attacker, target, BlowOutcome.Dodged, windup.Heavy);
            return;
        }
        // A player's guard is up or not; free, it must also face the blow. An AI meets the blow
        // aimed at it as it decided when it saw it coming, and is caught by any other.
        var guarding = w.Has<Avatar>(target)
            ? theirs.UpAt(impact) && theirs.StaggeredUntil <= impact && (!free || Reach.Facing(sim, target, attacker))
            : target == windup.Target && windup.Response != BlowResponse.None;
        var parried = guarding && Parryable(sim, attacker, attack) && (w.Has<Avatar>(target)
            ? impact - theirs.LastRaise <= ParryTicks
            : windup.Response == BlowResponse.Parry);
        if (parried)
        {
            theirs.Stamina = Math.Max(0f, theirs.Stamina - ParryCost);
            mine.StaggeredUntil = sim.Clock.Tick + StaggerTicks;
            theirs.RiposteOn = attacker;
            theirs.RiposteUntil = sim.Clock.Tick + RiposteTicks;
            Blows.Cancel(sim, attacker); // knocked out of whatever it was winding up next
            Report(sim, attacker, target, BlowOutcome.Parried, windup.Heavy);
            RealtimeAttacks.Provoke(sim, target, attacker);
            return;
        }
        var riposte = mine.RiposteOn == target && mine.RiposteUntil > sim.Clock.Tick;
        if (riposte)
            mine.RiposteUntil = -1;
        var damage = (windup.Heavy ? HeavyDamage : 1f) * (riposte ? RiposteDamage : 1f);
        var accuracy = riposte ? RiposteAccuracy : 0f;
        var outcome = riposte ? BlowOutcome.Riposte : BlowOutcome.Hit;
        if (guarding)
        {
            var cost = BlockCost * (windup.Heavy || dragon ? 2f : 1f);
            if (theirs.Stamina >= cost)
            {
                theirs.Stamina -= cost;
                damage *= dragon ? DragonBlocked : BlockedDamage;
                outcome = BlowOutcome.Blocked;
            }
            else
            {
                // Too tired to hold it: the guard's knocked aside, and the blow lands whole.
                theirs.Stamina = 0f;
                theirs.StaggeredUntil = sim.Clock.Tick + StaggerTicks;
                outcome = BlowOutcome.GuardBroken;
            }
        }
        if (free)
        {
            // It reached them, so it lands: no roll. Skill and armour decide how much it hurts.
            RealtimeAttacks.Strike(sim, attacker, target, attack, damage * Reach.SkillDamage(sim, attacker, attack));
            Report(sim, attacker, target, outcome, windup.Heavy);
            return;
        }
        var result = RealtimeAttacks.TryAttack(sim, attacker, target, attack, accuracy, damage);
        if (result is { Outcome: AttackOutcome.Miss } && outcome is BlowOutcome.Hit or BlowOutcome.Riposte)
            outcome = BlowOutcome.Miss;
        Report(sim, attacker, target, outcome, windup.Heavy);
    }

    internal static void Report(Simulation sim, Entity attacker, Entity target, BlowOutcome outcome, bool heavy)
    {
        var now = sim.Clock.Tick;
        sim.RecentBlows.RemoveAll(b => now - b.Tick > SimClock.TicksPerSecond * 5);
        sim.RecentBlows.Add(new BlowEvent(now, attacker, target, outcome, heavy));
    }
}

/// <summary>
/// Lands the blows that are due (<see cref="Blows"/>), and gives fighters their breath back.
/// Every tick: a parry's timing is a fifth of a second.
/// </summary>
public sealed class BlowSystem : ISimSystem
{
    public string Name => "Blows";
    public int Interval => 1;

    private readonly List<Entity> _due = [];

    public void Update(Simulation sim)
    {
        var w = sim.World;
        var now = sim.Clock.Tick;
        _due.Clear();
        foreach (var e in w.Each<Windup>())
            if (now >= Blows.LandsAt(sim, w.Get<Windup>(e)))
                _due.Add(e);
        _due.Sort((a, b) => a.Index.CompareTo(b.Index));
        foreach (var e in _due)
        {
            if (!w.IsAlive(e) || !w.Has<Windup>(e))
                continue;
            var windup = w.Get<Windup>(e);
            w.Remove<Windup>(e);
            Blows.Land(sim, e, windup);
        }
        foreach (var e in w.Each<Guard>())
        {
            ref var g = ref w.Get<Guard>(e);
            if (g.Stamina >= 1f || w.Has<Windup>(e))
                continue;
            var rate = g.RaisedAt >= 0 ? Blows.RegenGuarding : Blows.Regen;
            g.Stamina = Math.Min(1f, g.Stamina + rate / SimClock.TicksPerSecond);
        }
    }
}

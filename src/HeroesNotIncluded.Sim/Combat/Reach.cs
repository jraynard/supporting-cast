using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;

namespace HeroesNotIncluded.Sim.Combat;

/// <summary>
/// Where a blow reaches, with free movement on (<see cref="FreeMoveSystem"/>): each weapon sweeps
/// its own shape the way it's aimed, from the swinger's middle. A sword cuts a wide arc, a spear a
/// long narrow line, a club a short wide one; an arrow flies straight until it meets someone or
/// something solid. Whoever's in it when it lands is struck (<see cref="Blows"/>): there's no roll
/// to hit, so skill shows in how quick the windup is and how hard the blow lands instead.
/// </summary>
public static class Reach
{
    /// <summary>How far a blow's edge counts into someone: they're about this wide.</summary>
    public const float BodyRadius = 0.3f;

    /// <summary>Half the width of an arrow's line, and how close to it someone must be to be struck.</summary>
    public const float ArrowWidth = 0.35f;

    /// <summary>A guard faces a blow coming from within this many radians of where its bearer faces.</summary>
    public const float GuardArc = 1.3f;

    /// <summary>A weapon's sweep: how far from the swinger's middle (cells), and half its angle (radians).</summary>
    public readonly record struct Shape(float Length, float HalfArc);

    /// <summary>The sweep of what <paramref name="attacker"/> swings, by the kind of thing it is (its def's "held").</summary>
    public static Shape ShapeOf(Simulation sim, Entity attacker, AttackDef attack)
    {
        var w = sim.World;
        var held = w.TryGet<Equipment>(attacker, out var eq) && w.IsAlive(eq.Weapon) && w.Get<Thing>(eq.Weapon).Def is { Weapon: { } wd } d && wd == attack
            ? d.Held : null;
        if (held == null)
        {
            // Fists, teeth, claws: short, unless it's something big (a dragon's jaws).
            var size = w.Get<Creature>(attacker).Stage(sim.Clock.Tick).Size;
            return new Shape(0.9f + 0.45f * Math.Max(0f, size - 1f), 0.85f);
        }
        return held switch
        {
            "sword" => new Shape(1.35f, 1.05f),
            "longsword" => new Shape(1.6f, 1.15f),
            "spear" => new Shape(2.05f, 0.22f),
            "knife" => new Shape(1.0f, 0.7f),
            "axe" => new Shape(1.3f, 0.95f),
            "club" or "mace" or "hammer" => new Shape(1.2f, 0.95f),
            _ => new Shape(1.1f, 0.8f), // tools pressed into service: a pick, tongs, a saw
        };
    }

    /// <summary>
    /// Who a blow aimed along <paramref name="aim"/> strikes now: everyone in its sweep, friend or
    /// foe (an arrow: the first one along its line, short of a wall), in entity order. A blade
    /// doesn't know whose side you're on.
    /// </summary>
    public static List<Entity> Struck(Simulation sim, Entity attacker, AttackDef attack, float aim) =>
        InSweep(sim, attacker, attack, aim, _ => true);

    /// <summary>
    /// Anyone not hostile to <paramref name="attacker"/> (other than <paramref name="target"/>) in
    /// the sweep of a blow aimed at <paramref name="target"/>: an AI holds its blow while there is
    /// (<see cref="Blows.Begin"/>), rather than cut down its own.
    /// </summary>
    public static bool FriendInTheWay(Simulation sim, Entity attacker, AttackDef attack, float aim, Entity target)
    {
        var faction = sim.World.Get<Creature>(attacker).Faction;
        return InSweep(sim, attacker, attack, aim,
            e => e != target && !Factions.Hostile(faction, sim.World.Get<Creature>(e).Faction) && sim.World.Get<Creature>(e).Able).Count > 0;
    }

    private static List<Entity> InSweep(Simulation sim, Entity attacker, AttackDef attack, float aim, Func<Entity, bool> counts)
    {
        var w = sim.World;
        var me = w.Get<Position>(attacker);
        var dir = new float2(DetMath.Cos(aim), DetMath.Sin(aim));
        var hit = new List<Entity>();
        var nearest = float.MaxValue;
        var shape = attack.Ranged ? default : ShapeOf(sim, attacker, attack);
        foreach (var e in sim.Creatures())
        {
            if (e == attacker || w.Get<Creature>(e).Dead || w.Has<Carried>(e) || !counts(e))
                continue;
            var at = w.Get<Position>(e);
            if (Math.Abs(at.Cell.Z - me.Cell.Z) > 1)
                continue;
            var d = new float2(at.Pos.X - me.Pos.X, at.Pos.Y - me.Pos.Y);
            var along = d.X * dir.X + d.Y * dir.Y;
            if (attack.Ranged)
            {
                var across = MathF.Abs(d.X * dir.Y - d.Y * dir.X);
                if (along > 0.2f && along <= attack.Range && across <= ArrowWidth && along < nearest
                    && CombatRules.LineOfFire(sim, me.Pos + new float3(0, 0, 0.7f), at.Pos + new float3(0, 0, 0.5f)))
                {
                    nearest = along;
                    hit.Clear();
                    hit.Add(e);
                }
                continue;
            }
            var dist = MathF.Sqrt(d.X * d.X + d.Y * d.Y);
            if (dist - BodyRadius > shape.Length)
                continue;
            // Right on top of them, any angle; else within the sweep, its edge reaching into them.
            var angle = MathF.Abs(MathF.IEEERemainder(DetMath.Atan2(d.Y, d.X) - aim, MathF.Tau));
            var edge = Math.Min(1f, BodyRadius / Math.Max(dist, 0.01f));
            var spread = shape.HalfArc + DetMath.Atan2(edge, MathF.Sqrt(1f - edge * edge)); // the arc, widened by their body
            if (dist < 0.45f || angle <= spread)
                hit.Add(e);
        }
        return hit;
    }

    /// <summary>Does <paramref name="e"/> face <paramref name="from"/>, near enough for its guard to meet a blow from there?</summary>
    public static bool Facing(Simulation sim, Entity e, Entity from)
    {
        var w = sim.World;
        if (!w.TryGet<FreeMover>(e, out var m))
            return true;
        var a = w.Get<Position>(e).Pos;
        var b = w.Get<Position>(from).Pos;
        var toward = DetMath.Atan2(b.Y - a.Y, b.X - a.X);
        return MathF.Abs(MathF.IEEERemainder(toward - m.Aim, MathF.Tau)) <= GuardArc;
    }

    /// <summary>With no roll to hit, Melee (or Shooting) skill quickens the windup: a tenth slower at 0, a tenth quicker at 20.</summary>
    public static float WindupFactor(Simulation sim, Entity attacker, AttackDef attack) =>
        sim.World.TryGet<Skills>(attacker, out var s) ? 1.1f - 0.01f * s[CombatRules.SkillFor(attack)] : 1f;

    /// <summary>...and lands it harder: 0.85 at 0, 1.15 at 20.</summary>
    public static float SkillDamage(Simulation sim, Entity attacker, AttackDef attack) =>
        sim.World.TryGet<Skills>(attacker, out var s) ? 0.85f + 0.015f * s[CombatRules.SkillFor(attack)] : 1f;
}

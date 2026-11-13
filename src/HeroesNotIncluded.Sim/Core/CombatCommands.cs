using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;

namespace HeroesNotIncluded.Sim.Core;

/// <summary>A player's move in a fight beyond F's light blows (<see cref="Blows"/>).</summary>
public enum CombatInput : byte
{
    GuardUp,
    GuardDown,
    Dodge,
    Heavy,
}

/// <summary>
/// Raise or lower a guard, dodge, or wind up a heavy blow at <paramref name="Target"/>.
/// <paramref name="At"/> is the tick the key was pressed on the player's screen: in co-op it arrives
/// late, and a parry is all in the timing (<see cref="Blows.LatencyGrace"/>). A stamp from the
/// future or long past is taken as now.
/// </summary>
public sealed record CombatInputCommand(Entity Avatar, CombatInput Input, long At, Entity Target = default) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        var w = sim.World;
        if (!w.IsAlive(Avatar) || !w.Has<Avatar>(Avatar) || !w.Get<Creature>(Avatar).Able)
            return;
        var at = Blows.Stamp(sim, At);
        switch (Input)
        {
            case CombatInput.GuardUp:
                Blows.SetGuard(sim, Avatar, true, at);
                break;
            case CombatInput.GuardDown:
                Blows.SetGuard(sim, Avatar, false, at);
                break;
            case CombatInput.Dodge:
                Blows.Dodge(sim, Avatar, at);
                break;
            case CombatInput.Heavy:
                // Only at a foe beside you, with a blow you have to strike it with.
                if (!w.IsAlive(Target) || !w.Has<Position>(Target) || Ai.AvatarHunting.FoeAt(sim, w.Get<Position>(Target).Cell) != Target
                    || Ai.AvatarHunting.AttackOn(sim, Avatar, Target) is not { Ranged: false } attack)
                    return;
                Blows.Begin(sim, Avatar, Target, attack, heavy: true);
                break;
        }
    }
}

/// <summary>
/// Free movement (<see cref="FreeMoveSystem"/>): which way the keys ask to go (<paramref name="WishX"/>,
/// <paramref name="WishY"/>, on the map, up to length 1) and which way the mouse points
/// (<paramref name="Aim"/>, radians). Sent when either changes enough to matter.
/// </summary>
public sealed record FreeMoveCommand(Entity Avatar, float WishX, float WishY, float Aim) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        var w = sim.World;
        if (!w.IsAlive(Avatar) || !w.Has<FreeMover>(Avatar))
            return;
        ref var m = ref w.Get<FreeMover>(Avatar);
        m.Wish = Moves.Wish(WishX, WishY);
        if (float.IsFinite(Aim))
            m.Aim = MathF.IEEERemainder(Aim, MathF.Tau);
    }
}

/// <summary>A swing with free movement, light or heavy, the way the mouse points: it lands on whoever's in its sweep (<see cref="Reach"/>).</summary>
public sealed record AttackCommand(Entity Avatar, bool Heavy, float Aim) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        var w = sim.World;
        if (!w.IsAlive(Avatar) || !w.Has<FreeMover>(Avatar) || !w.Get<Creature>(Avatar).Able || !float.IsFinite(Aim)
            || CombatRules.Attacks(sim, Avatar).FirstOrDefault() is not { } attack)
            return;
        Blows.Begin(sim, Avatar, Entity.Null, attack, Heavy && !attack.Ranged, MathF.IEEERemainder(Aim, MathF.Tau));
    }
}

/// <summary>A dodge with free movement: blows pass through you for a moment as you dash along <paramref name="DirX"/>, <paramref name="DirY"/> (or back from your aim).</summary>
public sealed record DodgeCommand(Entity Avatar, float DirX, float DirY, long At) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        var w = sim.World;
        if (!w.IsAlive(Avatar) || !w.Has<FreeMover>(Avatar) || !w.Get<Creature>(Avatar).Able)
            return;
        var at = Blows.Stamp(sim, At);
        if (Blows.Dodge(sim, Avatar, at))
            FreeMoveSystem.Dash(sim, Avatar, Moves.Wish(DirX, DirY), at);
    }
}

internal static class Moves
{
    /// <summary>A direction from the wire: finite, and no longer than 1.</summary>
    public static float2 Wish(float x, float y)
    {
        if (!float.IsFinite(x) || !float.IsFinite(y))
            return float2.Zero;
        var len = MathF.Sqrt(x * x + y * y);
        return len > 1f ? new float2(x / len, y / len) : new float2(x, y);
    }
}

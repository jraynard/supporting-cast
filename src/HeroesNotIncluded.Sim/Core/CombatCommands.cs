using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;

namespace HeroesNotIncluded.Sim.Core;

/// <summary>
/// Raise or lower a guard (<see cref="Blows.SetGuard"/>), weapon drawn. <paramref name="At"/> is the
/// tick the key was pressed on the player's screen: in co-op it arrives late, and a parry is all in
/// the timing (<see cref="Blows.Grace"/>). A stamp from the future or long past is taken as now.
/// </summary>
public sealed record GuardCommand(Entity Avatar, bool Up, long At) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        var w = sim.World;
        if (!w.IsAlive(Avatar) || !w.Has<Avatar>(Avatar) || !w.Get<Creature>(Avatar).Able)
            return;
        if (Up && !(w.TryGet<FreeMover>(Avatar, out var free) && free.Drawn))
            return; // nothing to guard with, sheathed
        Blows.SetGuard(sim, Avatar, Up, Blows.Stamp(sim, At));
    }
}

/// <summary>
/// Free movement (<see cref="FreeMoveSystem"/>): which way the keys ask to go (<paramref name="WishX"/>,
/// <paramref name="WishY"/>, on the map, up to length 1) and which way the mouse points
/// (<paramref name="Aim"/>, radians). Sent when either changes enough to matter.
/// </summary>
/// <remarks>In co-op it also says where the player is on their own screen (<paramref name="AtX"/>,
/// <paramref name="AtY"/>, <paramref name="AtZ"/>; NaN for none), where their walk runs ahead:
/// the sim takes it if it's near (<see cref="FreeMoveSystem.Claim"/>).</remarks>
public sealed record FreeMoveCommand(Entity Avatar, float WishX, float WishY, float Aim, float AtX = float.NaN, float AtY = float.NaN, int AtZ = 0)
    : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        var w = sim.World;
        if (!FreeMoveSystem.Ensure(sim, Avatar))
            return;
        var wish = Moves.Wish(WishX, WishY);
        FreeMoveSystem.Claim(sim, Avatar, AtX, AtY, AtZ, stopped: wish == float2.Zero);
        ref var m = ref w.Get<FreeMover>(Avatar);
        m.Wish = wish;
        // Sheathed, you face the way you walk (FreeMoveSystem); drawn, the way you aim.
        if (m.Drawn && float.IsFinite(Aim))
            m.Aim = MathF.IEEERemainder(Aim, MathF.Tau);
    }
}

/// <summary>A swing with free movement, light or heavy, the way the mouse points: it lands on whoever's in its sweep (<see cref="Reach"/>).</summary>
/// <remarks><paramref name="Pressed"/>: the tick it was pressed on the player's screen (-1 for now): it's judged against where everyone was then (<see cref="Windup.Lag"/>).</remarks>
public sealed record AttackCommand(Entity Avatar, bool Heavy, float Aim, float AtX = float.NaN, float AtY = float.NaN, int AtZ = 0, long Pressed = -1)
    : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        var w = sim.World;
        if (!FreeMoveSystem.Ensure(sim, Avatar))
            return;
        // Struck from where you were on your screen (co-op), if that's near.
        FreeMoveSystem.Claim(sim, Avatar, AtX, AtY, AtZ, stopped: false);
        if (!w.TryGet<FreeMover>(Avatar, out var free) || !free.Drawn || !w.Get<Creature>(Avatar).Able || !float.IsFinite(Aim)
            || CombatRules.Attacks(sim, Avatar).FirstOrDefault() is not { } attack)
            return;
        if (attack.Ranged)
            return; // a bow's drawn and loosed (BowCommand), not swung
        Blows.Begin(sim, Avatar, Entity.Null, attack, Heavy, MathF.IEEERemainder(Aim, MathF.Tau), Pressed < 0 ? null : Pressed);
    }
}

/// <summary>A dodge with free movement: blows pass through you for a moment as you dash along <paramref name="DirX"/>, <paramref name="DirY"/> (or back from your aim).</summary>
public sealed record DodgeCommand(Entity Avatar, float DirX, float DirY, long At, float AtX = float.NaN, float AtY = float.NaN, int AtZ = 0)
    : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        var w = sim.World;
        FreeMoveSystem.Claim(sim, Avatar, AtX, AtY, AtZ, stopped: false);
        if (!FreeMoveSystem.Ensure(sim, Avatar) || !w.Get<Creature>(Avatar).Able)
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

/// <summary>
/// Draw or sheathe your weapon (<see cref="FreeMover.Drawn"/>): combat mode, where the mouse fights,
/// or not, where it walks and acts. Sheathing drops a guard.
/// </summary>
public sealed record DrawWeaponCommand(Entity Avatar, bool Drawn) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        var w = sim.World;
        if (!FreeMoveSystem.Ensure(sim, Avatar))
            return;
        w.Get<FreeMover>(Avatar).Drawn = Drawn;
        if (!Drawn)
        {
            Blows.SetGuard(sim, Avatar, false, sim.Clock.Tick);
            Archery.Draw(sim, Avatar, false, sim.Clock.Tick);
        }
    }
}

/// <summary>
/// A player's bow (<see cref="Archery"/>): draw it and hold it (<paramref name="Draw"/> true, the right
/// button down), let it down (false), or <paramref name="Loose"/> along <paramref name="Aim"/> once it's
/// fully drawn (the left button). <paramref name="At"/> is the tick it was pressed on the player's
/// screen; with a loose, where they were too (co-op), as for a swing.
/// </summary>
public sealed record BowCommand(Entity Avatar, bool Draw, bool Loose, float Aim, long At, float AtX = float.NaN, float AtY = float.NaN, int AtZ = 0)
    : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (!FreeMoveSystem.Ensure(sim, Avatar) || !sim.World.Get<Creature>(Avatar).Able)
            return;
        var at = Blows.Stamp(sim, At);
        if (!Loose)
        {
            Archery.Draw(sim, Avatar, Draw, at);
            return;
        }
        if (!float.IsFinite(Aim))
            return;
        FreeMoveSystem.Claim(sim, Avatar, AtX, AtY, AtZ, stopped: false);
        Archery.Loose(sim, Avatar, MathF.IEEERemainder(Aim, MathF.Tau), At);
    }
}

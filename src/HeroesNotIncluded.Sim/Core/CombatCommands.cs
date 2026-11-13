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

using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Ai;

/// <summary>
/// Hunting for the player's character (Adventure mode): a live animal is hunted as anything is
/// fought, weapon drawn, aimed and swung or shot (<see cref="Combat.Blows"/>), so a herd scatters and
/// a boar turns on you as it would on the village's hunters; F at a carcass butchers it
/// (<see cref="ButcherJob"/>). Stalking, tracking and the rest are for later.
/// </summary>
public static class AvatarHunting
{
    /// <summary>Hunting practice for each shot or blow, and on top for a kill.</summary>
    public const float XpPerAttack = 30f, XpPerKill = 80f;

    /// <summary>The wild animal standing in a cell (alive or a carcass), or Entity.Null.</summary>
    public static Entity AnimalAt(Simulation sim, int3 cell)
    {
        var w = sim.World;
        foreach (var e in w.Each<Creature>())
            if (w.Get<Creature>(e).Def.Race == Race.Animal && w.Get<Position>(e).Cell == cell)
                return e;
        return Entity.Null;
    }

    /// <summary>What F does at an animal in this cell: butcher its carcass (or carry it to the butcher spot). Null otherwise.</summary>
    public static AvatarAction? ActionAt(Simulation sim, Entity avatar, int3 cell, int3 here)
    {
        var w = sim.World;
        var animal = AnimalAt(sim, cell);
        if (animal.IsNull || sim.Reservations.IsReservedByOther(animal, avatar))
            return null;
        var c = w.Get<Creature>(animal);
        if (c.Dead)
        {
            if (c.Def.Animal == null || !AvatarSystem.InReach(here, cell))
                return null;
            // With a butcher spot built, a carcass lying elsewhere is carried there first (hands free).
            if (!w.Get<Carrying>(avatar).HasItem && ButcherSpots.SpotAt(sim, cell).IsNull && ButcherSpots.CanCarry(sim, avatar, animal)
                && !ButcherSpots.Nearest(sim, avatar, here).IsNull)
                return new(AvatarActionKind.Carry, $"Carry the {c.Def.Label} to the butcher spot", cell, animal);
            return new(AvatarActionKind.Butcher, $"Butcher the {c.Def.Label} carcass", cell, animal);
        }
        return null; // a live one's hunted with your weapon drawn, aimed and swung (Combat.Blows)
    }
}

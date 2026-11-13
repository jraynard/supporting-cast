using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Ai;

/// <summary>
/// Hunting for the player's character (Adventure mode): F at a wild animal shoots it from as far as
/// the weapon reaches (a bow), or strikes it from beside it; F at a carcass butchers it. A first,
/// simple pass: the same real-time attacks (<see cref="Combat.RealtimeAttacks"/>) and butchering
/// (<see cref="ButcherJob"/>) the village's hunters use, so a herd scatters and a boar turns on you
/// exactly as it would on them. Stalking, tracking and the rest are for later.
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

    /// <summary>
    /// A raider or predator on its feet in a cell, or Entity.Null: the only people a player can
    /// strike at so far. Fighting the town's own (or adventurers) waits for M4.
    /// </summary>
    public static Entity FoeAt(Simulation sim, int3 cell)
    {
        var w = sim.World;
        foreach (var e in w.Each<Creature>())
            if (w.Get<Creature>(e) is { Dead: false, Downed: false } c && Factions.Attacker(c.Faction) && c.Def.Race != Race.Animal
                && w.Get<Position>(e).Cell == cell)
                return e;
        return Entity.Null;
    }

    /// <summary>What to call a quarry in a prompt: "the boar", or a raider's name.</summary>
    public static string Called(Simulation sim, Entity e) =>
        sim.World.Get<Creature>(e) is { Def.Race: Race.Animal } c ? $"the {c.Def.Label}" : sim.World.Get<Creature>(e).Name;

    /// <summary>The attack the hunter could make on the prey from where they stand now, or null (out of range, no line of fire, won't fight).</summary>
    public static AttackDef? AttackOn(Simulation sim, Entity hunter, Entity prey)
    {
        foreach (var attack in Combat.CombatRules.Attacks(sim, hunter))
            if (Combat.CombatRules.Chance(sim, hunter, prey, attack).Possible)
                return attack;
        return null;
    }

    /// <summary>What F does at an animal in this cell: hunt it, or butcher its carcass. Null if neither.</summary>
    public static AvatarAction? ActionAt(Simulation sim, Entity avatar, int3 cell, int3 here)
    {
        var w = sim.World;
        var animal = AnimalAt(sim, cell);
        if (animal.IsNull && FoeAt(sim, cell) is { IsNull: false } foe)
            return AttackOn(sim, avatar, foe) is { } swing
                ? new(AvatarActionKind.Hunt, $"{(swing.Ranged ? "Shoot at" : "Strike at")} {Called(sim, foe)}", cell, foe)
                : null;
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
        if (AttackOn(sim, avatar, animal) is not { } attack)
            return null;
        var verb = attack.Ranged ? "Shoot" : "Strike at";
        return new(AvatarActionKind.Hunt, $"{verb} the {c.Def.Label}", cell, animal);
    }
}

/// <summary>
/// The player's character attacks an animal or a foe while the work key is held: a shot each time
/// the bow is ready (<see cref="AttackDef.CooldownTicks"/>), or a blow wound up and landed
/// (<see cref="Combat.Blows"/>), and the next once it's recovered. They don't move on their own (the
/// player does the stalking), so the job ends when the prey falls or gets out of range or sight.
/// </summary>
public sealed class AvatarHuntJob(Entity prey) : Job, IFromTheSaddle
{
    public override Entity Focus => prey;

    protected override IEnumerable<JobStatus> Run()
    {
        var animal = Get<Creature>(prey).Def.Race == Race.Animal;
        var label = AvatarHunting.Called(Sim, prey);
        Report = animal ? $"Hunting {label}" : $"Fighting {label}";
        var nextAttack = 0L;
        var struck = false;
        while (true)
        {
            if (!Alive(prey))
            {
                Fail("prey is gone");
                yield break;
            }
            if (Get<Creature>(prey).Dead || (!animal && Get<Creature>(prey).Downed))
            {
                if (struck)
                {
                    // It fell to us (a blow lands after its windup, so it's told here).
                    SkillRules.Learn(Sim, Pawn, animal ? Skill.Hunting : Skill.Melee, AvatarHunting.XpPerKill);
                    Sim.Messages.Add(MessageKind.Good, animal
                        ? $"{Get<Creature>(Pawn).Name} brought down {label}. Butcher it for meat and hide."
                        : $"{Get<Creature>(Pawn).Name} brought down {label}.", Sim.LocationOf(prey));
                }
                yield break;
            }
            if (WaitingForWorkKey())
            {
                yield return JobStatus.Running;
                continue;
            }
            if (AvatarHunting.AttackOn(Sim, Pawn, prey) is not { } attack)
            {
                Fail($"{label} is out of reach");
                yield break;
            }
            var skill = animal ? Skill.Hunting : attack.Ranged ? Skill.Shooting : Skill.Melee;
            if (attack.Ranged)
            {
                // A shot flies at once, on the bow's own pace.
                if (Sim.Clock.Tick >= nextAttack)
                {
                    FacePrey();
                    Combat.RealtimeAttacks.TryAttack(Sim, Pawn, prey, attack);
                    SkillRules.Learn(Sim, Pawn, skill, AvatarHunting.XpPerAttack);
                    nextAttack = Sim.Clock.Tick + attack.CooldownTicks;
                    struck = true;
                }
            }
            else if (!Combat.Blows.WindingUp(Sim, Pawn) && Combat.Blows.Begin(Sim, Pawn, prey, attack))
            {
                // A blow, wound up quick (Combat.Blows): F held swings again once it's landed.
                FacePrey();
                SkillRules.Learn(Sim, Pawn, skill, AvatarHunting.XpPerAttack);
                struck = true;
            }
            yield return JobStatus.Running;
        }
    }

    /// <summary>Turn toward the prey, so the sprite looks where the arrow goes.</summary>
    private void FacePrey()
    {
        if (!Sim.World.Has<Avatar>(Pawn))
            return;
        var d = Sim.LocationOf(prey) - PawnCell;
        if (d.X != 0 || d.Y != 0)
            Get<Avatar>(Pawn).Facing = new int2(Math.Sign(d.X), Math.Sign(d.Y));
    }
}

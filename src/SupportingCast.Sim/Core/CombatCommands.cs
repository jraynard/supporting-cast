using SupportingCast.Sim.Combat;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Core;

/// <summary>
/// Shared handling for the player's orders in a turn-based fight. Every combat order goes
/// through a command, so a co-op fight plays out alike on every peer: an order for a unit the AI
/// plays is refused, and one that arrives while a move or a reaction is still playing out waits
/// in <see cref="Encounter.Queued"/> (another player's unit may be the one walking). What an
/// order came to goes to <see cref="Encounter.Feedback"/> for its player's notice.
/// </summary>
internal static class CombatOrder
{
    /// <summary>Can the order for <paramref name="unit"/> be carried out now? False when it was queued, refused (and reported) or is moot.</summary>
    public static bool Ready(Simulation sim, ISimCommand order, Entity unit)
    {
        var enc = sim.Combat;
        if (!enc.Active || !sim.World.IsAlive(unit) || !sim.World.Has<Creature>(unit) || enc.Unit(unit) is not { } u)
            return false;
        if (enc.Settling(sim))
        {
            if (enc.Queued.Count < Encounter.MaxQueued)
                enc.Queued.Add(order);
            return false;
        }
        if (u.Side != Side.Village || enc.AiControls(u))
        {
            enc.Report(unit, $"{sim.World.Get<Creature>(unit).Name} is acting on their own");
            return false;
        }
        return true;
    }

    /// <summary>Report the refusal, or (when given) what happened.</summary>
    public static void Done(Simulation sim, Entity unit, string? error, string? success = null)
    {
        if ((error ?? success) is { } text)
            sim.Combat.Report(unit, text);
    }

    /// <summary>A creature that's still there to aim at or handle (it may have died since the order was sent).</summary>
    public static bool There(Simulation sim, Entity e) => sim.World.IsAlive(e) && sim.World.Has<Creature>(e) && sim.World.Has<Position>(e);

    /// <summary>A thing that's still there to handle.</summary>
    public static bool ThingThere(Simulation sim, Entity e) => sim.World.IsAlive(e) && sim.World.Has<Thing>(e);
}

/// <summary>Combat: move a unit to a cell it can reach with its TU (<see cref="Encounter.Move"/>).</summary>
public sealed record CombatMoveCommand(Entity Unit, int3 Cell) : ISimCommand, ISubjectCommand
{
    public Entity Subject => Unit;

    public void Apply(Simulation sim)
    {
        if (!sim.Map.InBounds(Cell) || !CombatOrder.Ready(sim, this, Unit))
            return;
        CombatOrder.Done(sim, Unit, sim.Combat.Move(sim, Unit, Cell));
    }
}

/// <summary>
/// Combat: attack <paramref name="Target"/> with the unit's attack at index <paramref name="Attack"/>
/// of <see cref="CombatRules.Attacks"/> (attacks aren't defs: the list is the same on every peer).
/// </summary>
public sealed record CombatAttackCommand(Entity Unit, Entity Target, int Attack, ShotMode Mode) : ISimCommand, ISubjectCommand
{
    public Entity Subject => Unit;

    public void Apply(Simulation sim)
    {
        if (!CombatOrder.There(sim, Target) || !CombatOrder.Ready(sim, this, Unit))
            return;
        var attacks = CombatRules.Attacks(sim, Unit);
        if (Attack < 0 || Attack >= attacks.Count)
        {
            CombatOrder.Done(sim, Unit, "can't attack that way");
            return;
        }
        var enc = sim.Combat;
        var attack = attacks[Attack];
        var reactions = enc.ReactionCount;
        var log = enc.Log.Count;
        if (enc.Attack(sim, Unit, Target, attack, out var error, mode: attack.Ranged ? Mode : ShotMode.Normal) is { } result)
        {
            // Answers to the attack come straight after it in the log.
            var answers = enc.Log.Skip(log).Skip(1).TakeLast(enc.ReactionCount - reactions).Select(l => l.Text);
            CombatOrder.Done(sim, Unit, null, string.Join("\n", answers.Prepend(result.Describe(sim))));
        }
        else
            CombatOrder.Done(sim, Unit, error);
    }
}

/// <summary>Combat: a dragon breathes fire at a cell in its breath range.</summary>
public sealed record CombatBreatheCommand(Entity Unit, int3 Cell) : ISimCommand, ISubjectCommand
{
    public Entity Subject => Unit;

    public void Apply(Simulation sim)
    {
        if (!sim.Map.InBounds(Cell) || !CombatOrder.Ready(sim, this, Unit))
            return;
        CombatOrder.Done(sim, Unit, sim.Combat.Breathe(sim, Unit, Cell), "Fire!");
    }
}

/// <summary>Combat: pick up an item in reach, or take one out of a container in reach (if it's <see cref="Stored"/>).</summary>
public sealed record CombatPickUpCommand(Entity Unit, Entity Item) : ISimCommand, ISubjectCommand
{
    public Entity Subject => Unit;

    public void Apply(Simulation sim)
    {
        if (!CombatOrder.ThingThere(sim, Item) || !CombatOrder.Ready(sim, this, Unit))
            return;
        var w = sim.World;
        var label = w.Get<Thing>(Item).Label;
        var stored = w.Has<Stored>(Item);
        var error = stored ? sim.Combat.TakeOut(sim, Unit, Item) : sim.Combat.PickUp(sim, Unit, Item);
        CombatOrder.Done(sim, Unit, error, $"{w.Get<Creature>(Unit).Name} {(stored ? "takes" : "picks up")} the {label}.");
    }
}

/// <summary>Combat: let go of what's in hand, at your feet (<see cref="Encounter.DropHeld"/>).</summary>
public sealed record CombatDropCommand(Entity Unit) : ISimCommand, ISubjectCommand
{
    public Entity Subject => Unit;

    public void Apply(Simulation sim)
    {
        if (CombatOrder.Ready(sim, this, Unit))
            CombatOrder.Done(sim, Unit, sim.Combat.DropHeld(sim, Unit));
    }
}

/// <summary>Combat: switch to a weapon from the hands or the pack (<see cref="Encounter.Wield"/>).</summary>
public sealed record CombatWieldCommand(Entity Unit, Entity Weapon) : ISimCommand, ISubjectCommand
{
    public Entity Subject => Unit;

    public void Apply(Simulation sim)
    {
        if (!CombatOrder.ThingThere(sim, Weapon) || !CombatOrder.Ready(sim, this, Unit))
            return;
        CombatOrder.Done(sim, Unit, sim.Combat.Wield(sim, Unit, Weapon));
    }
}

/// <summary>Combat: take everything out of a container in reach, while TU last (a pack first, making room for the rest).</summary>
public sealed record CombatTakeAllCommand(Entity Unit, Entity Container) : ISimCommand, ISubjectCommand
{
    public Entity Subject => Unit;

    public void Apply(Simulation sim)
    {
        if (!CombatOrder.ThingThere(sim, Container) || !CombatOrder.Ready(sim, this, Unit))
            return;
        var enc = sim.Combat;
        if (!enc.ContainersInReach(sim, Unit).Contains(Container))
        {
            CombatOrder.Done(sim, Unit, "out of reach");
            return;
        }
        var w = sim.World;
        var taken = 0;
        string? stop = null;
        foreach (var item in sim.StoredIn(Container).ToList().OrderBy(i => w.Get<Thing>(i).Def.Apparel is { Slot: ApparelSlot.Back } ? 0 : 1))
        {
            if (enc.TakeOut(sim, Unit, item) is { } why)
            {
                stop = why;
                if (why.StartsWith("not enough", StringComparison.Ordinal))
                    break;
                continue;
            }
            taken++;
        }
        CombatOrder.Done(sim, Unit, null,
            $"{w.Get<Creature>(Unit).Name} takes {taken} thing{(taken == 1 ? "" : "s")} from the {w.Get<Thing>(Container).Label}." + (stop != null ? $" ({stop})" : ""));
    }
}

/// <summary>Combat: drag a body beside the unit into a free cell next to where it lies.</summary>
public sealed record CombatDragCommand(Entity Unit, Entity Body, int3 To) : ISimCommand, ISubjectCommand
{
    public Entity Subject => Unit;

    public void Apply(Simulation sim)
    {
        if (!CombatOrder.There(sim, Body) || !sim.Map.InBounds(To) || !CombatOrder.Ready(sim, this, Unit))
            return;
        var w = sim.World;
        CombatOrder.Done(sim, Unit, sim.Combat.Drag(sim, Unit, Body, To), $"{w.Get<Creature>(Unit).Name} drags {w.Get<Creature>(Body).Name} aside.");
    }
}

/// <summary>Combat: bind your own wounds, or those of someone beside you, for TU.</summary>
public sealed record CombatBandageCommand(Entity Unit, Entity Patient) : ISimCommand, ISubjectCommand
{
    public Entity Subject => Unit;

    public void Apply(Simulation sim)
    {
        if (!CombatOrder.There(sim, Patient) || !CombatOrder.Ready(sim, this, Unit))
            return;
        if (Bandaging.CantBind(sim, Unit, Patient) is { } why)
        {
            CombatOrder.Done(sim, Unit, why);
            return;
        }
        var w = sim.World;
        var log = $"{w.Get<Creature>(Unit).Name} bandages {(Patient == Unit ? "their own" : $"{w.Get<Creature>(Patient).Name}'s")} wounds.";
        CombatOrder.Done(sim, Unit, sim.Combat.Interact(sim, Unit, TimeUnits.Bandage, () => Bandaging.Bind(sim, Unit, Patient) > 0, log),
            "Bandaged: the bleeding stops.");
    }
}

/// <summary>Combat (the Escape): strike the chains off your dragon, beside it with the key.</summary>
public sealed record CombatUnchainCommand(Entity Unit, Entity Dragon) : ISimCommand, ISubjectCommand
{
    public Entity Subject => Unit;

    public void Apply(Simulation sim)
    {
        if (!CombatOrder.There(sim, Dragon) || !CombatOrder.Ready(sim, this, Unit))
            return;
        if (Scenarios.EscapeCamp.ChainedDragonInReach(sim, Unit) != Dragon)
        {
            CombatOrder.Done(sim, Unit, "can't do that now");
            return;
        }
        var name = sim.World.Get<Creature>(Dragon).Name;
        CombatOrder.Done(sim, Unit,
            sim.Combat.Interact(sim, Unit, TimeUnits.Interact, () => Scenarios.EscapeCamp.Unchain(sim, Unit, Dragon), $"{sim.World.Get<Creature>(Unit).Name} strikes the chains off {name}!"),
            $"{name} is free! Get beside it and fly away.");
    }
}

/// <summary>Combat: keep (or stop keeping) enough TU back for a reaction (<see cref="CombatUnit.ReserveForReactions"/>).</summary>
public sealed record CombatReserveCommand(Entity Unit, bool On) : ISimCommand, ISubjectCommand
{
    public Entity Subject => Unit;

    public void Apply(Simulation sim)
    {
        if (CombatOrder.Ready(sim, this, Unit))
            sim.Combat.Unit(Unit)!.ReserveForReactions = On;
    }
}

/// <summary>Combat: end one unit's turn, keeping its TU for reactions.</summary>
public sealed record CombatWaitCommand(Entity Unit) : ISimCommand, ISubjectCommand
{
    public Entity Subject => Unit;

    public void Apply(Simulation sim)
    {
        if (CombatOrder.Ready(sim, this, Unit))
            sim.Combat.Wait(sim, Unit);
    }
}

/// <summary>
/// Combat: a player ends their turn, for every unit they command (<paramref name="Player"/>'s
/// character and dragon in co-op; null, single player: every unit the player commands). The
/// village's phase passes to the raiders once every player's units are done
/// (<see cref="Encounter.StepAi"/>).
/// </summary>
public sealed record CombatEndTurnCommand(Guid? Player) : ISimCommand, IPlayerCommand
{
    public Guid? Owner => Player;

    public void Apply(Simulation sim)
    {
        var enc = sim.Combat;
        if (!enc.Active || enc.Phase != Side.Village)
            return;
        if (enc.Settling(sim))
        {
            if (enc.Queued.Count < Encounter.MaxQueued)
                enc.Queued.Add(this);
            return;
        }
        foreach (var u in enc.Units)
            if (u.Side == Side.Village && !enc.AiControls(u) && (Player == null || CommandRules.ControllerOf(sim, u.Entity) == Player))
                u.Acted = true;
    }
}

/// <summary>Combat: let the AI play the village's side (the HUD's Auto box). Host only in co-op.</summary>
public sealed record CombatAutoCommand(bool On) : IDevCommand
{
    public void Apply(Simulation sim) => sim.Combat.VillageAuto = On;
}

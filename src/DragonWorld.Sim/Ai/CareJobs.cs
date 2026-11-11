using DragonWorld.Sim.Components;
using DragonWorld.Sim.Core;
using DragonWorld.Sim.Defs;
using DragonWorld.Sim.Maps;
using DragonWorld.Sim.Systems;

namespace DragonWorld.Sim.Ai;

/// <summary>
/// A keeper tends their dragon (<see cref="DragonInteractionDef"/>): walks over, has it stay put
/// (<see cref="AttendJob"/>), works at it for the def's ticks (the player holds F), and then the
/// care counts through <see cref="Bonding.Interact"/>. Village keepers and the player both use it.
/// </summary>
public sealed class DragonCareJob(Entity dragon, DragonInteractionDef def, TrickDef? trick = null) : Job
{
    public override Entity Focus => dragon;

    public DragonInteractionDef Def => def;

    protected override IEnumerable<JobStatus> Run()
    {
        var name = Get<Creature>(dragon).Name;
        Report = $"Going to {name}";
        foreach (var s in GoToEntity(dragon)) yield return s;
        if (HasFailed) yield break;
        if (Bonding.Problem(Sim, Pawn, dragon, def) is { } why)
        {
            Fail(why);
            yield break;
        }
        Report = $"{def.Doing} {name}";
        if (Sim.World.Get<Mind>(dragon).Job is not AttendJob)
            AiSystem.Order(Sim, dragon, new AttendJob(Pawn, Math.Max(240, def.Ticks * 3)));
        var done = 0f;
        // At once for the player; a moment's fuss for anyone else.
        var ticks = def.Ticks > 0 ? def.Ticks : Sim.World.Has<Avatar>(Pawn) ? 0 : 60;
        while (done < ticks)
        {
            if (!Alive(dragon) || Get<Creature>(dragon).Dead)
            {
                Fail("dragon is gone");
                yield break;
            }
            if (Map.Distance(Sim.LocationOf(dragon), PawnCell) > 2)
            {
                foreach (var s in GoToEntity(dragon)) yield return s;
                if (HasFailed) yield break;
            }
            done += Work(Skill.Social);
            yield return JobStatus.Running;
        }
        if (Bonding.Problem(Sim, Pawn, dragon, def) is { } late)
        {
            Fail(late);
            yield break;
        }
        Bonding.Interact(Sim, Pawn, dragon, def, trick: trick);
        if (Sim.World.Get<Mind>(dragon).Job is AttendJob attend && attend.Keeper == Pawn)
            AiSystem.EndJob(Sim, dragon, Sim.World.Get<Mind>(dragon), true);
    }
}

/// <summary>A dragon holds still beside its keeper while they tend it (and wanders off if they stop).</summary>
public sealed class AttendJob(Entity keeper, int maxTicks) : Job
{
    public override bool Interruptible => true;
    public override Entity Focus => keeper;
    public Entity Keeper => keeper;

    protected override IEnumerable<JobStatus> Run()
    {
        Report = $"With {Get<Creature>(keeper).Name}";
        for (var t = 0; t < maxTicks; t += 30)
        {
            if (!Alive(keeper) || Sim.World.Get<Mind>(keeper).Job is not DragonCareJob { Focus: var f } || f != Pawn)
                yield break;
            if (Sim.World.Get<Mind>(keeper).Job is DragonCareJob tend)
                Report = $"With {Get<Creature>(keeper).Name} ({tend.Def.Label})";
            foreach (var s in Wait(30)) yield return s;
        }
    }
}

/// <summary>Called by its keeper: the dragon comes over and stays near them a while.</summary>
public sealed class ComeToJob(Entity keeper) : Job
{
    public const int StayTicks = SimClock.TicksPerHour / 4;

    public override bool Interruptible => true;
    public override Entity Focus => keeper;

    protected override IEnumerable<JobStatus> Run()
    {
        Report = $"Coming to {Get<Creature>(keeper).Name}";
        foreach (var s in GoToEntity(keeper, PathEnd.Touch)) yield return s;
        if (HasFailed) yield break;
        Report = $"Waiting by {Get<Creature>(keeper).Name}";
        for (var t = 0; t < StayTicks; t += 30)
        {
            if (!Alive(keeper))
                yield break;
            if (Map.Distance(Sim.LocationOf(keeper), PawnCell) > 3)
            {
                foreach (var s in GoToEntity(keeper, PathEnd.Touch, maxTicks: 600)) yield return s;
                if (HasFailed) yield break;
            }
            foreach (var s in Wait(30)) yield return s;
        }
    }
}

/// <summary>A devoted dragon carries a share of its kill to its keeper and sets it down beside them.</summary>
public sealed class BringKillJob(Entity meat, Entity keeper) : Job
{
    public override Entity Focus => keeper;

    protected override IEnumerable<JobStatus> Run()
    {
        var name = Get<Creature>(keeper).Name;
        Report = $"Fetching some of its kill for {name}";
        if (!Reserve(meat)) yield break;
        foreach (var s in GoTo(Get<Thing>(meat).Cell, PathEnd.OnTile)) yield return s;
        if (HasFailed) yield break;
        if (!Sim.IsAvailable(meat))
        {
            Fail("the meat is gone");
            yield break;
        }
        var carried = Sim.TakeFromStack(meat, Get<Stack>(meat).Count);
        Get<Carrying>(Pawn).Item = carried;
        Report = $"Bringing its kill to {name}";
        foreach (var s in GoToEntity(keeper, PathEnd.Touch)) yield return s;
        if (HasFailed) yield break;
        Get<Carrying>(Pawn).Item = Entity.Null;
        var at = Sim.LocationOf(keeper);
        Sim.PlaceItemNear(carried, at);
        Sim.Messages.Add(MessageKind.Good, $"{Get<Creature>(Pawn).Name} drops a share of its kill at {name}'s feet.", at);
    }
}

/// <summary>A keeper puts food in a feeding trough for their hand-fed hatchling.</summary>
public sealed class FillTroughJob(Entity trough, Entity food, int count) : Job
{
    protected override IEnumerable<JobStatus> Run()
    {
        Report = $"Fetching {Get<Thing>(food).Def.Label} for the trough";
        if (!Reserve(food)) yield break;
        foreach (var s in GoTo(Get<Thing>(food).Cell, PathEnd.OnTile)) yield return s;
        if (HasFailed) yield break;
        if (!Sim.IsAvailable(food))
        {
            Fail("food disappeared");
            yield break;
        }
        var carried = Sim.TakeFromStack(food, Math.Min(count, Get<Stack>(food).Count));
        Get<Carrying>(Pawn).Item = carried;
        Report = "Filling the feeding trough";
        foreach (var s in GoTo(Get<Thing>(trough).Cell, PathEnd.Touch)) yield return s;
        if (HasFailed) yield break;
        if (!Alive(trough))
        {
            Fail("the trough is gone");
            yield break;
        }
        foreach (var s in Wait(60)) yield return s;
        Get<Carrying>(Pawn).Item = Sim.Store(trough, carried);
    }
}

using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Maps;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Ai;

public enum JobStatus
{
    Running,
    Succeeded,
    Failed,
}

/// <summary>
/// A job a rider can do from the saddle without moving (shooting from dragonback): the AI runs it for
/// a rider, who otherwise does nothing of their own while the mount carries them.
/// </summary>
public interface IFromTheSaddle;

/// <summary>
/// A unit of work for a creature, written as a coroutine: <see cref="Run"/> yields once per tick.
/// Helpers like <see cref="GoTo"/> call <see cref="Fail"/> and stop; the job then ends as failed.
/// Reservations taken through <see cref="Reserve"/> are released automatically when the job ends.
/// </summary>
public abstract class Job
{
    public Entity Pawn { get; private set; }
    protected Simulation Sim { get; private set; } = null!;

    /// <summary>Short status shown in the inspector, e.g. "Hauling wood".</summary>
    public string Report { get; protected set; } = "";

    public string? FailReason { get; private set; }

    /// <summary>
    /// The thing this job works on (a construction site, a tree, a bed), if any. A read-only hint
    /// for the renderer (facing, work effects); the sim doesn't use it.
    /// </summary>
    public virtual Entity Focus => Entity.Null;

    /// <summary>The cell this job works on when it isn't a thing (digging, breathing fire at a spot).</summary>
    public virtual int3? FocusCell => null;

    /// <summary>
    /// The kind of tool (<see cref="Defs.ToolProps.Kind"/>: "Axe", "Pick", "Hammer") this job works
    /// with, if any: a read-only hint for the renderer, which draws it in the worker's hand.
    /// </summary>
    public virtual string? ToolKind => null;

    /// <summary>Idle-type jobs that may be dropped when something more important comes up.</summary>
    public virtual bool Interruptible => false;
    public bool HasFailed => FailReason != null;

    /// <summary>
    /// The last tick this job asked for skilled work (<see cref="Work"/>), or -1. A read-only hint
    /// for the game: only work stalls without the work key, so only it needs a "hold F" prompt.
    /// </summary>
    public long LastWorkTick { get; private set; } = -1;

    /// <summary>Whether the job is at its hands-on work right now (asked for it this tick or the last).</summary>
    public bool IsWorking(long now) => LastWorkTick >= 0 && now - LastWorkTick <= 1;

    private IEnumerator<JobStatus>? _routine;

    internal void Start(Simulation sim, Entity pawn)
    {
        Sim = sim;
        Pawn = pawn;
    }

    /// <summary>Advance one tick.</summary>
    internal JobStatus Tick()
    {
        if (HasFailed)
            return JobStatus.Failed;
        _routine ??= Run().GetEnumerator();
        bool more;
        try
        {
            more = _routine.MoveNext();
        }
        catch (Exception ex) when (!Sim.StrictJobs)
        {
            // Exceptions are deterministic, so failing the job keeps the sim deterministic too.
            Fail($"crashed: {ex.GetType().Name}: {ex.Message}");
            Sim.Messages.AddThrottled($"jobcrash:{GetType().Name}", SimClock.TicksPerHour, MessageKind.Warning,
                $"A {GetType().Name} failed unexpectedly ({ex.Message}). This is a bug; the job was abandoned.");
            return JobStatus.Failed;
        }
        if (HasFailed)
            return JobStatus.Failed;
        if (!more)
            return JobStatus.Succeeded;
        return _routine.Current;
    }

    internal void End(bool succeeded)
    {
        // Runs the coroutine's pending finally blocks and frees nested helper iterators.
        _routine?.Dispose();
        _routine = null;
        OnEnd(succeeded);
        Sim.Reservations.ReleaseAll(Pawn);
        if (Sim.World.TryGet<Mover>(Pawn, out var mover))
        {
            // A player's character finishes the step under way rather than snapping back to the
            // cell it left: the next walk or held direction carries on from there.
            if (Sim.World.Has<Avatar>(Pawn) && mover.Moving && mover.StepProgress > 0)
            {
                mover.Path = [mover.Path![mover.PathIndex]];
                mover.PathIndex = 0;
            }
            else
                mover.Stop();
        }
    }

    protected abstract IEnumerable<JobStatus> Run();

    /// <summary>Called when the job ends for any reason (success, failure, interruption).</summary>
    protected virtual void OnEnd(bool succeeded) { }

    protected void Fail(string reason) => FailReason ??= reason;

    // ---- Helpers ----

    protected ref T Get<T>(Entity e) => ref Sim.World.Get<T>(e);
    protected bool Alive(Entity e) => Sim.World.IsAlive(e);
    protected int3 PawnCell => Sim.World.Get<Position>(Pawn).Cell;

    /// <summary>
    /// One tick of skilled work: practice for the worker, and how much work it got done. The
    /// player's avatar only works while the work key is held, so every job is hold-to-work for it.
    /// </summary>
    protected float Work(Skill skill)
    {
        LastWorkTick = Sim.Clock.Tick;
        if (Sim.World.TryGet<Avatar>(Pawn, out var avatar) && !avatar.WorkHeld)
            return 0f;
        SkillRules.Learn(Sim, Pawn, skill, SkillRules.XpPerWorkTick);
        var factor = SkillRules.WorkFactor(Sim, Pawn, skill) * FoodPoisoning.WorkFactor(Sim, Pawn);
        // Mood paces companions; the player's own character always works at full pace.
        return Sim.World.Has<Avatar>(Pawn) ? factor : factor * MoodRules.WorkFactor(MoodRules.MoodOf(Sim, Pawn));
    }

    /// <summary>
    /// For unskilled hands-on work (beating out flames): true while the player's avatar isn't
    /// holding the work key, so the job should wait. Counts as asking for work, for the prompt.
    /// </summary>
    protected bool WaitingForWorkKey()
    {
        LastWorkTick = Sim.Clock.Tick;
        return Sim.World.TryGet<Avatar>(Pawn, out var avatar) && !avatar.WorkHeld;
    }

    protected bool Reserve(Entity target)
    {
        if (Sim.Reservations.TryReserve(target, Pawn))
            return true;
        Fail("target reserved by someone else");
        return false;
    }

    protected bool ReserveTile(int3 tile)
    {
        if (Sim.Reservations.TryReserveTile(tile, Pawn))
            return true;
        Fail("tile reserved by someone else");
        return false;
    }

    /// <summary>Walk (or fly, see <see cref="Navigation"/>) to a cell. Yields while moving; calls Fail if no route.</summary>
    protected IEnumerable<JobStatus> GoTo(int3 target, PathEnd end)
    {
        if (Arrived(target, end))
            yield break;

        var mover = Sim.World.Get<Mover>(Pawn);
        var from = PawnCell;
        var path = Navigation.PlanRoute(Sim, Pawn, from, target, end);
        if (path == null)
        {
            Fail($"no path to {target}");
            yield break;
        }

        mover.Path = path;
        mover.PathIndex = 0;
        mover.StepProgress = 0;
        mover.StepFrom = from;

        while (mover.Moving)
            yield return JobStatus.Running;

        if (!Arrived(target, end))
            Fail("could not reach destination");
    }

    /// <summary>
    /// Walk to work at a building: beside it, as <see cref="PathEnd.Touch"/> on its cell. One wider
    /// than a cell (a 2x1 bench, a bed) is worked from outside its footprint, from the front if
    /// that side is open (<see cref="Footprint.Beside"/>), so nobody stands on half the bench.
    /// </summary>
    protected IEnumerable<JobStatus> GoToBeside(Entity building)
    {
        var thing = Get<Thing>(building);
        if (!Footprint.IsWide(thing.Def))
        {
            foreach (var s in GoTo(thing.Cell, PathEnd.Touch)) yield return s;
            yield break;
        }
        if (IsBeside(thing, PawnCell))
            yield break;

        // Front cells first, then the nearest of the rest; take the first one there's a way to.
        var map = Sim.Map;
        var here = PawnCell;
        var front = Footprint.Beside(map, thing.Def, thing.Cell, thing.Facing).Take(thing.Def.SizeX).ToHashSet();
        var spots = Footprint.Beside(map, thing.Def, thing.Cell, thing.Facing)
            .Where(c => map.Standable(map.Idx(c)) && map.Passable(c))
            .OrderBy(c => front.Contains(c) ? 0 : 1)
            .ThenBy(c => Math.Abs(c.X - here.X) + Math.Abs(c.Y - here.Y))
            .ToList();
        foreach (var spot in spots)
        {
            if (Navigation.PlanRoute(Sim, Pawn, here, spot, PathEnd.OnTile) == null)
                continue;
            foreach (var s in GoTo(spot, PathEnd.OnTile)) yield return s;
            yield break;
        }
        // Boxed in on every side: lean in from wherever touches it.
        foreach (var s in GoTo(thing.Cell, PathEnd.Touch)) yield return s;
    }

    /// <summary>Standing next to a building's footprint (or, for one cell, touching it).</summary>
    protected bool IsBeside(in Thing thing, int3 at)
    {
        if (!Footprint.IsWide(thing.Def))
            return Pathfinder.Touches(at, thing.Cell);
        if (at.Z != thing.Cell.Z || Footprint.Contains(thing.Def, thing.Cell, thing.Facing, at))
            return false;
        foreach (var c in Footprint.Cells(Sim.Map, thing.Def, thing.Cell, thing.Facing))
            if (Math.Abs(c.X - at.X) <= 1 && Math.Abs(c.Y - at.Y) <= 1 && c.Z == at.Z)
                return true;
        return false;
    }

    /// <summary>
    /// Walk to an entity that may move (a creature, or a carried item). Re-paths when the target
    /// wanders off. Calls Fail if the target dies or can't be reached.
    /// </summary>
    protected IEnumerable<JobStatus> GoToEntity(Entity target, PathEnd end = PathEnd.Touch, int maxTicks = 3000)
    {
        var mover = Sim.World.Get<Mover>(Pawn);
        var ticks = 0;
        while (true)
        {
            if (!Alive(target))
            {
                Fail("target is gone");
                yield break;
            }
            var dest = Sim.LocationOf(target);
            if (Arrived(dest, end))
            {
                mover.Stop();
                yield break;
            }
            if (++ticks > maxTicks)
            {
                Fail("took too long to reach target");
                yield break;
            }

            var lastEnd = mover.Moving ? mover.Path![^1] : dest;
            var drift = Math.Abs(lastEnd.X - dest.X) + Math.Abs(lastEnd.Y - dest.Y) + Math.Abs(lastEnd.Z - dest.Z);
            if (!mover.Moving || drift > 2)
            {
                var from = PawnCell;
                var path = Navigation.PlanRoute(Sim, Pawn, from, dest, end);
                if (path == null)
                {
                    Fail($"no path to {dest}");
                    yield break;
                }
                // Keep walking the current step smoothly; only replace the remainder.
                mover.Path = path;
                mover.PathIndex = 0;
                mover.StepProgress = 0;
                mover.StepFrom = from;
            }
            yield return JobStatus.Running;
        }
    }

    protected bool Arrived(int3 target, PathEnd end)
    {
        var here = PawnCell;
        if (end == PathEnd.OnTile)
            return here == target;
        return Pathfinder.Touches(here, target);
    }

    /// <summary>Units gathered so far by <see cref="GatherNear"/> (an iterator can't return a count).</summary>
    protected sealed class Tally
    {
        public int Count;
    }

    /// <summary>
    /// Fill the work pack with more stacks of an item lying near <paramref name="near"/> (within
    /// <see cref="BulkHaulJob.GatherRadius"/>), nearest first, until <paramref name="have"/> reaches
    /// <paramref name="want"/> or the pack is full. Each stack is reserved as it's chosen. Stops early,
    /// without failing, when nothing more is near; fails only if a walk does.
    /// </summary>
    protected IEnumerable<JobStatus> GatherNear(Entity pack, ThingDef def, Func<Entity, Thing, bool>? accept, int3 near, int want, Tally have)
    {
        while (!pack.IsNull && have.Count < want && Sim.ContainerRoom(pack, def, null) > 0)
        {
            var next = Entity.Null;
            var best = int.MaxValue;
            foreach (var e in Sim.AvailableItems())
            {
                var t = Get<Thing>(e);
                if (t.Def != def || Sim.Reservations.IsReservedByOther(e, Pawn) || Sim.Map.Fire[Sim.Map.Idx(t.Cell)] > 0)
                    continue;
                if (accept != null && !accept(e, t))
                    continue;
                var d = Maps.Map.Distance(t.Cell, near);
                if (d <= BulkHaulJob.GatherRadius && d < best && Navigation.CanReach(Sim, Pawn, PawnCell, t.Cell, PathEnd.OnTile))
                    (next, best) = (e, d);
            }
            if (next.IsNull || !Sim.Reservations.TryReserve(next, Pawn))
                yield break;
            foreach (var s in GoTo(Get<Thing>(next).Cell, PathEnd.OnTile)) yield return s;
            if (HasFailed) yield break;
            if (!Sim.IsAvailable(next))
                continue;
            var t2 = Get<Thing>(next);
            var take = Math.Min(Math.Min(want - have.Count, Sim.ContainerRoom(pack, def, t2.Material)), Get<Stack>(next).Count);
            if (take <= 0)
                yield break;
            Sim.Store(pack, Sim.TakeFromStack(next, take));
            have.Count += take;
        }
    }

    /// <summary>Wait a number of ticks.</summary>
    protected static IEnumerable<JobStatus> Wait(int ticks)
    {
        for (var i = 0; i < ticks; i++)
            yield return JobStatus.Running;
    }
}

using DragonWorld.Sim.Components;
using DragonWorld.Sim.Defs;
using DragonWorld.Sim.Environment;
using DragonWorld.Sim.Maps;
using DragonWorld.Sim.Systems;

namespace DragonWorld.Sim.Ai;

/// <summary>
/// The colony work of the other elements' breath (<see cref="ElementDef.Work"/>; fire's are
/// <see cref="BreatheFireJob"/> and <see cref="StokeJob"/>, which stay as they are): a dragon flies
/// or walks within reach of its target and breathes at it, as a fire dragon does at a mark.
/// </summary>
public abstract class ElementBreathJob(int3 target) : Job
{
    public override int3? FocusCell => target;

    protected int3 Target => target;

    /// <summary>
    /// Close in until within <paramref name="share"/> of the dragon's reach (and on the ground: a
    /// flyer that stopped mid-air would just fall), as <see cref="BreatheFireJob"/> does.
    /// </summary>
    protected IEnumerable<JobStatus> Approach(float share = 0.7f)
    {
        var reach = Breath.RangeOf(Sim, Pawn);
        var mover = Get<Mover>(Pawn);
        var from = PawnCell;
        if (Distance(from) <= reach * (share + 0.1f))
            yield break;
        var route = Navigation.PlanRoute(Sim, Pawn, from, target, PathEnd.Touch);
        if (route == null)
        {
            Fail("can't get close enough");
            yield break;
        }
        mover.Path = route;
        mover.PathIndex = 0;
        mover.StepProgress = 0;
        mover.StepFrom = from;
        while (mover.Moving && (Distance(PawnCell) > reach * share || !Sim.Map.Passable(PawnCell)))
            yield return JobStatus.Running;
        mover.Stop();
    }

    /// <summary>One breath at the target, of the dragon's element.</summary>
    protected BreathEvent Puff()
    {
        var stage = Get<Creature>(Pawn).Stage(Sim.Clock.Tick);
        var mouth = Get<Position>(Pawn).Pos + new float3(0, 0, stage.Size * 0.6f);
        return Breath.Breathe(Sim, mouth, Position.CenterOf(target) + new float3(0, 0, 0.3f), Breath.ShapeOf(Sim, Pawn), Pawn);
    }

    protected float Distance(int3 c)
    {
        float dx = c.X - target.X, dy = c.Y - target.Y, dz = c.Z - target.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>The plant standing at a cell, or Entity.Null.</summary>
    protected Entity PlantAt(int3 cell)
    {
        var s = Sim.Map.Structure[Sim.Map.Idx(cell)];
        return !s.IsNull && Sim.World.TryGet<Thing>(s, out var t) && t.Def.Category == ThingCategory.Plant && t.Spawned ? s : Entity.Null;
    }
}

/// <summary>A poison dragon withers the plants on land marked for clearing (<see cref="Designation.Burn"/>): no fire, no smoke.</summary>
public sealed class BlightJob(int3 target) : ElementBreathJob(target)
{
    private const int Puffs = 6;

    protected override IEnumerable<JobStatus> Run()
    {
        Report = "Going to wither the marked plants";
        if (!ReserveTile(Target)) yield break;
        foreach (var s in Approach()) yield return s;
        if (HasFailed) yield break;
        Report = "Breathing poison on the marked plants";
        for (var p = 0; p < Puffs && !PlantAt(Target).IsNull; p++)
        {
            Puff();
            foreach (var s in Wait(15)) yield return s;
        }
    }
}

/// <summary>A nature dragon breathes green on a harvested plant, so it grows back faster (<see cref="Nurtured"/>).</summary>
public sealed class NurtureJob(Entity plant, int3 cell) : ElementBreathJob(cell)
{
    public override Entity Focus => plant;

    protected override IEnumerable<JobStatus> Run()
    {
        Report = $"Going to tend the {Get<Thing>(plant).Label}";
        if (!Reserve(plant)) yield break;
        foreach (var s in Approach(0.5f)) yield return s;
        if (HasFailed || !Alive(plant)) yield break;
        Report = $"Breathing green on the {Get<Thing>(plant).Label}";
        Puff();
        foreach (var s in Wait(30)) yield return s;
        if (Alive(plant))
            Breath.Nurture(Sim, plant); // it was aimed at this one: it takes even if every ray missed
    }
}

/// <summary>A lightning dragon shatters rock marked for digging from where it stands (<see cref="Designation.Dig"/>).</summary>
public sealed class QuarryJob(int3 rock) : ElementBreathJob(rock)
{
    /// <summary>Strikes to break a block: much faster than claws or a pick.</summary>
    public const int Strikes = 3;

    protected override IEnumerable<JobStatus> Run()
    {
        var map = Sim.Map;
        var i = map.Idx(Target);
        var block = map.Block[i];
        Report = $"Going to shatter {Mining.ShownBlock(Sim, i).Label}";
        if (!ReserveTile(Target)) yield break;
        foreach (var s in Approach(0.4f)) yield return s;
        if (HasFailed) yield break;
        Report = $"Shattering {Mining.ShownBlock(Sim, i).Label}";
        for (var strike = 0; strike < Strikes; strike++)
        {
            if (map.Block[i] != block || (map.Designations[i] & Designation.Dig) == 0)
            {
                Fail("dig cancelled");
                yield break;
            }
            Puff();
            foreach (var s in Wait(40)) yield return s;
        }
        if (map.Block[i] == block && (map.Designations[i] & Designation.Dig) != 0)
            Sim.DigOut(Target, Pawn);
    }
}

/// <summary>
/// An ice dragon breathes frost into a store (<see cref="BuildingProps.Chill"/>, the chest) so the
/// food in it keeps: it's cooled for a while (<see cref="Chilled"/>), as a forge a fire dragon
/// stokes runs hot (<see cref="StokeJob"/>).
/// </summary>
public sealed class ChillJob(Entity store) : Job
{
    /// <summary>How long one breathing keeps it cold: two game hours.</summary>
    public const int Lasts = SimClock.TicksPerHour * 2;

    private const int Puffs = 3, PuffTicks = 45;

    public override Entity Focus => store;

    protected override IEnumerable<JobStatus> Run()
    {
        Report = "Going to chill the store";
        if (!Alive(store) || Get<Thing>(store).Def.Building?.Chill == null)
        {
            Fail("the store is gone");
            yield break;
        }
        if (!Reserve(store)) yield break;
        foreach (var s in GoToBeside(store)) yield return s;
        if (HasFailed) yield break;

        Report = $"Breathing frost into the {Get<Thing>(store).Label}";
        var stage = Get<Creature>(Pawn).Stage(Sim.Clock.Tick);
        for (var p = 0; p < Puffs; p++)
        {
            if (!Alive(store))
                yield break;
            // Only the look of a breath (render-only): the cold goes in through Chilled.
            var mouth = Get<Position>(Pawn).Pos + new float3(0, 0, stage.Size * 0.6f);
            var ev = new BreathEvent { Tick = Sim.Clock.Tick, Mouth = mouth, Source = Pawn, Element = DragonElement.Ice };
            ev.RayEnds.Add(Position.CenterOf(Get<Thing>(store).Cell) + new float3(0, 0, 0.4f));
            Sim.RecentBreaths.Add(ev);
            foreach (var s in Wait(PuffTicks)) yield return s;
        }
        if (!Alive(store))
            yield break;
        if (Sim.World.Has<Chilled>(store))
            Get<Chilled>(store).Until = Sim.Clock.Tick + Lasts;
        else
            Sim.World.Add(store, new Chilled { Until = Sim.Clock.Tick + Lasts });
    }
}

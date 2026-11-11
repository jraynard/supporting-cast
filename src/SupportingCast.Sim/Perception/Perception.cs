using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;

// Not in a namespace of its own: a namespace named Perception would hide the class from the rest
// of SupportingCast.Sim.
namespace SupportingCast.Sim.Systems;

/// <summary>What can be told about someone from looking at them. Nothing here needs an examination.</summary>
public readonly record struct PerceivedPerson(string Name, string Condition, string? Holding);

/// <summary>A line as one character caught it. <see cref="Speaker"/> is null when they couldn't tell who it was.</summary>
public readonly record struct HeardLine(long Id, long Tick, string Who, string Text, Speech.Volume Volume, Speech.Clarity Clarity, Entity? Speaker);

/// <summary>
/// The one way the game learns what a character knows (docs/DESIGN.md, "One knowledge layer").
/// UI, rendering and bots ask a <see cref="PerceptionView"/> rather than reading the world, so
/// "you only know what your character knows" holds in one place, and a future server can filter
/// what it sends by the same rules.
/// </summary>
public static class Perception
{
    /// <summary>How far (cells) a character can see in good light.</summary>
    public const int SightRadius = 18;

    /// <summary>Levels above and below the viewer considered: past this a line of sight is rare, and costly to look for.</summary>
    public const int SightLevels = 2;

    /// <summary>Anyone this close is seen whatever the light: you can tell who's beside you in the dark.</summary>
    public const float TouchRange = 2.5f;

    /// <summary>The shortest sight gets, as a share of <see cref="SightRadius"/>, on the darkest night.</summary>
    public const float DarkFactor = 0.35f;

    /// <summary>Light at or above this is good enough to see as far as anyone can.</summary>
    public const float FullLight = 0.5f;

    /// <summary>
    /// What <paramref name="viewer"/> knows. No viewer, or a village with no round, sees
    /// everything: the title screen, the demos, the overseer.
    /// </summary>
    public static PerceptionView For(Simulation sim, Entity viewer) =>
        new(sim, sim.Round.Phase == Round.RoundPhase.None || viewer.IsNull || !sim.World.IsAlive(viewer) ? Entity.Null : viewer);

    /// <summary>Someone in a round with no character yet, or no longer: the town's buildings and nothing in it.</summary>
    public static PerceptionView Blind(Simulation sim) => new(sim, Entity.Null, blind: true);
}

/// <summary>
/// One character's senses. Holds a field of visible cells, rebuilt only when it could have changed,
/// so asking about every creature and item each frame stays cheap. Strictly read-only, and never
/// to be fed back into sim state: sight depends on <see cref="Maps.Map.Light"/>, which a peer that
/// just loaded a snapshot won't have recomputed yet.
/// </summary>
public sealed class PerceptionView(Simulation sim, Entity viewer, bool blind = false)
{
    /// <summary>How often (ticks) the field is rebuilt for doors and light, if nothing else prompts it.</summary>
    private const int RefreshTicks = 6;

    private readonly HashSet<int> _visible = [];
    private readonly HashSet<int> _openDoors = [];
    private int3 _builtAt;
    private int _builtVersion = -1;
    private long _builtSlice = -1;

    public Entity Viewer => viewer;

    /// <summary>Sees everything (no viewer, or no round).</summary>
    public bool Omniscient => viewer.IsNull && !blind;

    /// <summary>Has no character to see through (<see cref="Perception.Blind"/>).</summary>
    public bool Blind => blind;

    /// <summary>This view belongs to this game and character, so a cached one is still good.</summary>
    public bool IsFor(Simulation other, Entity who) => ReferenceEquals(sim, other) && viewer == who;

    public bool CanSeeCell(int3 cell)
    {
        if (Omniscient)
            return true;
        if (!sim.Map.InBounds(cell))
            return false;
        Refresh();
        return _visible.Contains(sim.Map.Idx(cell));
    }

    /// <summary>
    /// Can this creature or thing be seen right now? Buildings and furniture always can: the town's
    /// layout is common knowledge. Something worn, packed or stored shows only to someone within reach.
    /// </summary>
    public bool CanSee(Entity e)
    {
        if (Omniscient || e == viewer)
            return true;
        var w = sim.World;
        if (!w.IsAlive(e))
            return false;
        if (w.TryGet<Position>(e, out var p))
            return CanSeeCell(p.Cell);
        if (!w.TryGet<Thing>(e, out var thing))
            return false;
        if (!thing.Spawned)
            return Within(sim.LocationOf(e), 1);
        return thing.Def.Category != ThingCategory.Item || CanSeeCell(thing.Cell);
    }

    /// <summary>The creatures in sight, the viewer included.</summary>
    public IEnumerable<Entity> Creatures()
    {
        foreach (var e in sim.World.Query<Creature>())
            if (CanSee(e))
                yield return e;
    }

    /// <summary>What the viewer can tell about someone in sight; null if they can't see them.</summary>
    public PerceivedPerson? Describe(Entity e)
    {
        var w = sim.World;
        if (!CanSee(e) || !w.TryGet<Creature>(e, out var c))
            return null;
        return new PerceivedPerson(c.Name, Condition(e, c), Holding(e));
    }

    /// <summary>
    /// The errands this character knows of, oldest first: those they heard asked, took on or asked
    /// for themselves. Open ones and those closed within the last day.
    /// </summary>
    public IEnumerable<Errands.KnownErrand> Errands()
    {
        if (blind)
            yield break;
        foreach (var e in sim.Errands.All)
            if (Omniscient || e.KnownBy.Contains(viewer))
                yield return new Errands.KnownErrand(e.Id, e.Kind.Kind, e.Kind.Label, e.PosterName, e.Text, e.Price,
                    e.PostedTick, e.Deadline, e.AssigneeName, e.Status, e.Poster == viewer, e.Assignee == viewer);
    }

    /// <summary>Everything this character has heard that's still remembered, oldest first.</summary>
    public IEnumerable<HeardLine> Heard()
    {
        if (blind)
            yield break;
        foreach (var u in sim.Speech.Kept)
        {
            if (Omniscient)
            {
                yield return new HeardLine(u.Id, u.Tick, u.SpeakerName, u.Text, u.Volume, Speech.Clarity.Clear, u.Speaker);
                continue;
            }
            foreach (var h in u.Listeners)
                if (h.Listener == viewer)
                    yield return new HeardLine(u.Id, u.Tick, h.Identified ? u.SpeakerName : "Someone",
                        Speech.SpeechSystem.TextFor(u, h), u.Volume, h.Clarity, h.Identified ? u.Speaker : null);
        }
    }

    private string Condition(Entity e, Creature c)
    {
        var w = sim.World;
        if (c.Dead)
            return "Not moving.";
        if (c.Downed)
            return "Collapsed.";
        if (w.TryGet<Mind>(e, out var mind) && mind.Asleep)
            return "Asleep.";
        if (w.TryGet<Health>(e, out var h))
        {
            if (h.Capacity(Capacity.Moving) < 0.7f)
                return "Limping badly.";
            if (h.Injuries.Exists(i => !i.Tended))
                return "Hurt.";
        }
        return "Looks well enough.";
    }

    private string? Holding(Entity e)
    {
        var w = sim.World;
        if (w.TryGet<Carrying>(e, out var carrying) && w.IsAlive(carrying.Item))
            return w.Get<Thing>(carrying.Item).Def.Label;
        if (w.TryGet<Equipment>(e, out var gear) && w.IsAlive(gear.Weapon))
            return w.Get<Thing>(gear.Weapon).Def.Label;
        return null;
    }

    private bool Within(int3 cell, int cells)
    {
        if (!sim.World.IsAlive(viewer))
            return false;
        var here = sim.World.Get<Position>(viewer).Cell;
        return cell.Z == here.Z && Math.Abs(cell.X - here.X) <= cells && Math.Abs(cell.Y - here.Y) <= cells;
    }

    private void Refresh()
    {
        if (!sim.World.IsAlive(viewer))
        {
            _visible.Clear();
            return;
        }
        var map = sim.Map;
        var here = sim.World.Get<Position>(viewer).Cell;
        var slice = sim.Clock.Tick / RefreshTicks;
        if (_builtVersion == map.StructureVersion && _builtSlice == slice && here == _builtAt)
            return;
        (_builtVersion, _builtSlice, _builtAt) = (map.StructureVersion, slice, here);
        Build(here);
    }

    private void Build(int3 here)
    {
        var map = sim.Map;
        var w = sim.World;
        _visible.Clear();
        if (!map.InBounds(here))
            return;

        // A door has no open state in the sim: it stands open while someone is in the doorway,
        // which is also how it's drawn.
        _openDoors.Clear();
        foreach (var e in w.Each<Creature>())
        {
            var cell = w.Get<Position>(e).Cell;
            if (map.InBounds(cell))
                _openDoors.Add(map.Idx(cell));
        }

        const int r = Perception.SightRadius;
        var z0 = Math.Max(0, here.Z - Perception.SightLevels);
        var z1 = Math.Min(map.Depth - 1, here.Z + Perception.SightLevels);
        for (var z = z0; z <= z1; z++)
        for (var y = Math.Max(0, here.Y - r); y <= Math.Min(map.Height - 1, here.Y + r); y++)
        for (var x = Math.Max(0, here.X - r); x <= Math.Min(map.Width - 1, here.X + r); x++)
        {
            var i = map.Idx(x, y, z);
            if (map.IsSolid(i))
                continue;
            var target = new int3(x, y, z);
            if (InRange(here, target, i) && Clear(here, target, i))
                _visible.Add(i);
        }
    }

    // Dark cells are seen from less far: what counts is the light where the thing is.
    private bool InRange(int3 here, int3 target, int i)
    {
        float dx = target.X - here.X, dy = target.Y - here.Y, dz = target.Z - here.Z;
        var distance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        if (distance <= Perception.TouchRange)
            return true;
        var light = Math.Clamp(sim.Map.Light[i] / Perception.FullLight, Perception.DarkFactor, 1f);
        return distance <= Perception.SightRadius * light;
    }

    private bool Clear(int3 here, int3 target, int targetIdx)
    {
        if (here == target)
            return true;
        var map = sim.Map;
        var hereIdx = map.Idx(here);
        var lowest = Math.Min(here.Z, target.Z);
        var crossesLevels = here.Z != target.Z;
        // A roof you can't see through, seen from another level: the room under it stays private.
        if (crossesLevels && (map.Roofed[hereIdx] || map.Roofed[targetIdx]))
            return false;

        var from = new float3(here.X + 0.5f, here.Y + 0.5f, here.Z + 0.5f);
        var d = new float3(target.X - here.X, target.Y - here.Y, target.Z - here.Z);
        var length = MathF.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z);
        return !map.Raycast(from, d * (1f / length), length, i =>
        {
            // Stepping between levels goes through the floor of the upper cell, at either end of the line.
            if (crossesLevels && map.CellOf(i).Z > lowest && map.Ceiling(i))
                return true;
            return i != hereIdx && i != targetIdx && Blocks(i);
        }, out _, out _);
    }

    private bool Blocks(int i)
    {
        var map = sim.Map;
        if (map.IsSolid(i))
            return true;
        var s = map.Structure[i];
        if (s.IsNull || sim.World.Has<Construction>(s))
            return false;
        var building = sim.World.Get<Thing>(s).Def.Building;
        if (building == null || building.SeeThrough)
            return false;
        return building.IsWall || (building.IsDoor && !_openDoors.Contains(i));
    }
}

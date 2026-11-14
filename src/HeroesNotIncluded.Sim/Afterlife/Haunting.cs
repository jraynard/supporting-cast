using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Afterlife;

/// <summary>The signs a ghost can leave.</summary>
public enum HauntKind : byte
{
    /// <summary>A sudden cold where the ghost is: breath shows, for a while.</summary>
    Chill,

    /// <summary>A nearby lamp goes dark a while, or a fire low on fuel goes out.</summary>
    Gutter,

    /// <summary>A knock, or a creak, heard by whoever's near enough.</summary>
    Knock,
}

/// <summary>
/// A sign of the dead in the world (its own entity, with a <see cref="Position"/>), until
/// <see cref="Until"/>. Never says who left it: the living perceive only the sign
/// (<see cref="PerceptionView.Signs"/>), never the ghost.
/// </summary>
public struct Haunting
{
    public HauntKind Kind;
    public long Tick;
    public long Until;
}

/// <summary>
/// Haunting (docs/DESIGN.md, "Death"): a ghost's presence fills slowly, and it spends it on small
/// signs the living can perceive but can't place. A guttered lamp is really dark, and a fire
/// that goes out is out: properties, not scripts.
/// </summary>
public static class Hauntings
{
    /// <summary>Presence a ghost gathers each game hour (it holds at most 1).</summary>
    public const float PresencePerHour = 0.5f;

    /// <summary>A newly dead ghost's presence.</summary>
    public const float StartPresence = 0.5f;

    /// <summary>How far from a ghost a light can be guttered, cells.</summary>
    public const int GutterReach = 4;

    /// <summary>A fire this low (share of what it holds) goes out when guttered; a fuller one only flickers.</summary>
    public const float WeakFire = 0.25f;

    public static float Cost(HauntKind kind) => kind switch
    {
        HauntKind.Chill => 0.35f,
        HauntKind.Gutter => 0.5f,
        _ => 0.25f,
    };

    /// <summary>How long each sign lasts, ticks.</summary>
    public static int Lasts(HauntKind kind) => kind switch
    {
        HauntKind.Chill => SimClock.TicksPerHour / 2,
        HauntKind.Gutter => SimClock.TicksPerHour,
        _ => SimClock.TicksPerSecond * 2,
    };

    /// <summary>Why this ghost can't leave that sign now, or null if it can.</summary>
    public static string? Problem(Simulation sim, Entity ghost, HauntKind kind)
    {
        var w = sim.World;
        if (!Enum.IsDefined(kind) || !w.IsAlive(ghost) || !w.TryGet<Ghost>(ghost, out var g))
            return "you can't";
        if (g.Presence < Cost(kind) - 0.0001f)
            return "you haven't the presence yet; it gathers in time";
        if (kind == HauntKind.Gutter && GutterTarget(sim, w.Get<Position>(ghost).Cell) is null)
            return "there's no light near enough to gutter";
        return null;
    }

    /// <summary>Leave a sign where the ghost is. True if it was left.</summary>
    public static bool Haunt(Simulation sim, Entity ghost, HauntKind kind)
    {
        if (Problem(sim, ghost, kind) != null)
            return false;
        var w = sim.World;
        var here = w.Get<Position>(ghost).Cell;
        var at = here;
        if (kind == HauntKind.Gutter)
        {
            var (cell, fire) = GutterTarget(sim, here)!.Value;
            at = cell;
            if (!fire.IsNull && w.Get<Thing>(fire).Def.Building?.Fuel is { } props && w.Get<Fueled>(fire).Fuel <= props.Capacity * WeakFire)
                w.Get<Fueled>(fire).Fuel = 0f; // a weak fire gutters out
        }
        w.Get<Ghost>(ghost).Presence -= Cost(kind);
        var sign = w.Create();
        w.Add(sign, Position.At(at));
        w.Add(sign, new Haunting { Kind = kind, Tick = sim.Clock.Tick, Until = sim.Clock.Tick + Lasts(kind) });
        return true;
    }

    /// <summary>The light nearest a cell that could gutter: a lit lamp, or a burning fire (Entity.Null for a lamp).</summary>
    public static (int3 Cell, Entity Fire)? GutterTarget(Simulation sim, int3 from)
    {
        (int3, Entity)? best = null;
        var bestD = int.MaxValue;
        void Consider(int3 c, Entity fire)
        {
            if (c.Z != from.Z)
                return;
            var d = Math.Max(Math.Abs(c.X - from.X), Math.Abs(c.Y - from.Y));
            if (d <= GutterReach && d < bestD && !Guttered(sim, c))
                (best, bestD) = ((c, fire), d);
        }
        foreach (var (cell, _, _) in Buildings.LitLamps(sim))
            Consider(cell, Entity.Null);
        var w = sim.World;
        foreach (var e in w.Each<Fueled>())
            if (w.Get<Thing>(e).Spawned && sim.Burning(e))
                Consider(Environment.Warmth.FireCell(w.Get<Thing>(e)), e);
        return best;
    }

    /// <summary>A lamp here is guttered dark right now.</summary>
    public static bool Guttered(Simulation sim, int3 cell)
    {
        var w = sim.World;
        if (w.Count<Haunting>() == 0)
            return false;
        foreach (var e in w.Each<Haunting>())
            if (w.Get<Haunting>(e).Kind == HauntKind.Gutter && w.Get<Position>(e).Cell == cell)
                return true;
        return false;
    }

    /// <summary>Presence gathers, and old signs fade.</summary>
    internal static void Update(Simulation sim, int ticks)
    {
        var w = sim.World;
        var gain = PresencePerHour * ticks / SimClock.TicksPerHour;
        foreach (var e in w.Each<Ghost>())
        {
            ref var g = ref w.Get<Ghost>(e);
            g.Presence = Math.Min(1f, g.Presence + gain);
        }
        if (w.Count<Haunting>() == 0)
            return;
        foreach (var e in w.Query<Haunting>())
            if (w.Get<Haunting>(e).Until < sim.Clock.Tick)
                w.Destroy(e);
    }
}

/// <summary>A ghost leaves a sign where it is (<see cref="Hauntings.Haunt"/>).</summary>
public sealed record HauntCommand(Entity Actor, HauntKind Kind) : ISimCommand, IActorCommand
{
    public void Apply(Simulation sim) => Hauntings.Haunt(sim, Actor, Kind);
}

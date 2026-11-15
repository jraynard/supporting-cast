using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
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

    /// <summary>Something only animals feel: those near it bolt, and a rat at a larder leaves it for good.</summary>
    Spook,

    /// <summary>A pull only animals feel: timid ones nearby come and graze about it a while.</summary>
    Beckon,

    /// <summary>A few words written in frost (<see cref="Haunting.Text"/>), there for anyone to read until it melts.</summary>
    Frost,
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

    /// <summary>What a frost sign says (<see cref="HauntKind.Frost"/>); empty for the rest.</summary>
    public string Text;
}

/// <summary>
/// A raider or a predator shaken by a sign of the dead near them (<see cref="Hauntings.Unnerve"/>):
/// until <see cref="Until"/> its blows come slower and it breaks sooner. The dead help defend the
/// town without striking a blow.
/// </summary>
public struct Unnerved
{
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

    /// <summary>How far from a spook animals bolt, and from a beckoning they come, cells.</summary>
    public const int SpookReach = 8, BeckonReach = 10;

    /// <summary>How near a chill, knock or spook must be to raiders to shake them, cells, and for how long it does.</summary>
    public const int UnnerveReach = 5;
    public const int UnnerveTicks = SimClock.TicksPerHour / 3;

    /// <summary>An unnerved attacker's windup is this much slower, and it breaks at this much pain (not half).</summary>
    public const float UnnervedWindup = 1.3f, UnnervedBreaksAt = 0.3f;

    /// <summary>Frost: at most so many words and letters.</summary>
    public const int FrostWords = 3, FrostLetters = 24;

    public static float Cost(HauntKind kind) => kind switch
    {
        HauntKind.Chill => 0.35f,
        HauntKind.Gutter => 0.5f,
        HauntKind.Spook => 0.3f,
        HauntKind.Beckon => 0.4f,
        HauntKind.Frost => 1f,
        _ => 0.25f,
    };

    /// <summary>How long each sign lasts, ticks.</summary>
    public static int Lasts(HauntKind kind) => kind switch
    {
        HauntKind.Chill => SimClock.TicksPerHour / 2,
        HauntKind.Gutter => SimClock.TicksPerHour,
        HauntKind.Spook => SimClock.TicksPerSecond * 3,
        HauntKind.Beckon => SimClock.TicksPerHour / 2,
        HauntKind.Frost => SimClock.TicksPerHour * 2,
        _ => SimClock.TicksPerSecond * 2,
    };

    /// <summary>
    /// Words fit to write in frost or whisper into a dream: letters, spaces and apostrophes, at most
    /// <paramref name="words"/> words and <paramref name="letters"/> characters; null if nothing's left.
    /// The limit is what keeps the dead from naming names at length.
    /// </summary>
    public static string? Words(string? text, int words, int letters)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var kept = new string(text.Where(ch => char.IsAsciiLetter(ch) || ch == ' ' || ch == '\'').ToArray());
        var parts = kept.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(words);
        var joined = string.Join(' ', parts);
        if (joined.Length > letters)
            joined = joined[..letters].TrimEnd();
        return joined.Length == 0 ? null : joined;
    }

    /// <summary>Why this ghost can't leave that sign now, or null if it can.</summary>
    public static string? Problem(Simulation sim, Entity ghost, HauntKind kind, string? text = null)
    {
        var w = sim.World;
        if (!Enum.IsDefined(kind) || !w.IsAlive(ghost) || !w.TryGet<Ghost>(ghost, out var g))
            return "you can't";
        if (!g.Riding.IsNull)
            return "not while you're in a beast";
        if (kind == HauntKind.Frost && Words(text, FrostWords, FrostLetters) is null)
            return "write something first: a word or three";
        if (g.Presence < Cost(kind) - 0.0001f)
            return "you haven't the presence yet; it gathers in time";
        if (kind == HauntKind.Gutter && GutterTarget(sim, w.Get<Position>(ghost).Cell) is null)
            return "there's no light near enough to gutter";
        return null;
    }

    /// <summary>Leave a sign where the ghost is. True if it was left.</summary>
    public static bool Haunt(Simulation sim, Entity ghost, HauntKind kind, string? text = null)
    {
        if (Problem(sim, ghost, kind, text) != null)
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
        Leave(sim, at, kind, kind == HauntKind.Frost ? Words(text, FrostWords, FrostLetters) : null);
        return true;
    }

    /// <summary>A sign at a cell, whoever (or whatever) left it; a chill, knock or spook unnerves raiders near it.</summary>
    public static Entity Leave(Simulation sim, int3 at, HauntKind kind, string? text = null)
    {
        var w = sim.World;
        var sign = w.Create();
        w.Add(sign, Position.At(at));
        w.Add(sign, new Haunting { Kind = kind, Tick = sim.Clock.Tick, Until = sim.Clock.Tick + Lasts(kind), Text = text ?? "" });
        if (kind is HauntKind.Chill or HauntKind.Knock or HauntKind.Spook)
            Unnerve(sim, at);
        return sign;
    }

    /// <summary>Raiders and predators near a sign are shaken by it (<see cref="Unnerved"/>).</summary>
    public static void Unnerve(Simulation sim, int3 at)
    {
        var w = sim.World;
        var until = sim.Clock.Tick + UnnerveTicks;
        foreach (var e in sim.Creatures())
        {
            var c = w.Get<Creature>(e);
            if (!Factions.Attacker(c.Faction) || c.Def.Race == Race.Dragon || c.Dead)
                continue;
            var cell = w.Get<Position>(e).Cell;
            if (cell.Z != at.Z || Math.Max(Math.Abs(cell.X - at.X), Math.Abs(cell.Y - at.Y)) > UnnerveReach)
                continue;
            if (w.Has<Unnerved>(e))
                w.Get<Unnerved>(e).Until = Math.Max(w.Get<Unnerved>(e).Until, until);
            else
                w.Add(e, new Unnerved { Until = until });
        }
    }

    /// <summary>Is this one shaken by the dead right now?</summary>
    public static bool IsUnnerved(Simulation sim, Entity e) =>
        sim.World.TryGet<Unnerved>(e, out var u) && u.Until > sim.Clock.Tick;

    /// <summary>
    /// The freshest sign of this kind within <paramref name="reach"/> of a cell, on its level (for
    /// animals: a spook to run from, a beckoning to go to); null if there's none.
    /// </summary>
    public static int3? Nearest(Simulation sim, int3 from, HauntKind kind, int reach)
    {
        var w = sim.World;
        if (w.Count<Haunting>() == 0)
            return null;
        int3? best = null;
        var bestTick = long.MinValue;
        foreach (var e in w.Each<Haunting>())
        {
            var h = w.Get<Haunting>(e);
            if (h.Kind != kind || h.Until < sim.Clock.Tick)
                continue;
            var c = w.Get<Position>(e).Cell;
            if (c.Z != from.Z || Math.Max(Math.Abs(c.X - from.X), Math.Abs(c.Y - from.Y)) > reach)
                continue;
            if (h.Tick > bestTick)
                (best, bestTick) = (c, h.Tick);
        }
        return best;
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
            if (g.Riding.IsNull) // in a beast, it's spent, not gathered (Possession)
                g.Presence = Math.Min(1f, g.Presence + gain);
        }
        if (w.Count<Unnerved>() > 0)
            foreach (var e in w.Query<Unnerved>())
                if (w.Get<Unnerved>(e).Until <= sim.Clock.Tick)
                    w.Remove<Unnerved>(e);
        if (w.Count<Haunting>() == 0)
            return;
        foreach (var e in w.Query<Haunting>())
            if (w.Get<Haunting>(e).Until < sim.Clock.Tick)
                w.Destroy(e);
    }
}

/// <summary>A ghost leaves a sign where it is (<see cref="Hauntings.Haunt"/>); <paramref name="Text"/> is what frost spells.</summary>
public sealed record HauntCommand(Entity Actor, HauntKind Kind, string Text = "") : ISimCommand, IActorCommand
{
    public void Apply(Simulation sim) => Hauntings.Haunt(sim, Actor, Kind, Text);
}

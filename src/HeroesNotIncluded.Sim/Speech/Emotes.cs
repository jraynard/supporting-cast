using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Speech;

/// <summary>
/// What a character can show without words. Eight, so a stick's eight ways reach each one. Point
/// carries a cell: "look there", seen by whoever can see the pointer.
/// </summary>
public enum Emote : byte
{
    Wave,
    Bow,
    Cheer,
    Laugh,
    Point,
    Shrug,
    Weep,
    Angry,
}

/// <summary>
/// A character showing an <see cref="Emote"/>: since when, until when, and where they stood (they
/// stop if they step away). Sim state, so every peer sees the same wave at the same moment, and
/// a replay shows it; who sees it at all is <see cref="PerceptionView.EmoteOf"/>'s to say.
/// </summary>
public struct Emoting
{
    public Emote Kind;
    public long Since, Until;
    public int3 From;
    public int3? At;
}

/// <summary>An emote as a viewer sees it (<see cref="PerceptionView.EmoteOf"/>): what, where it points, and since when.</summary>
public readonly record struct SeenEmote(Emote Kind, int3? At, long Since, long Until);

/// <summary>
/// Showing feeling (docs/ROADMAP.md, "Emotes and quick-say"). Seen, not heard: unlike speech it
/// carries as far as sight does, and no further, so a wave across the square works and a wave
/// behind a wall doesn't. Nothing in the sim acts on it yet; it's for players to read each other.
/// </summary>
public static class Emotes
{
    /// <summary>Ticks between one emote and the next from the same character.</summary>
    public const int Gap = SimClock.TicksPerSecond;

    /// <summary>How far a pointing finger reaches (cells): beyond it, it's pointing nowhere much.</summary>
    public const int PointReach = 40;

    /// <summary>How long one shows (ticks at 1x).</summary>
    public static int Duration(Emote kind) => SimClock.TicksPerSecond * kind switch
    {
        Emote.Point => 4,
        Emote.Bow or Emote.Shrug => 2,
        _ => 3,
    };

    /// <summary>Why this character can't show that now, or null if they can.</summary>
    public static string? Problem(Simulation sim, Entity e, Emote kind)
    {
        var w = sim.World;
        if (!Enum.IsDefined(kind))
            return "no such thing";
        if (!SpeechSystem.CanAct(sim, e) || w.Has<Carried>(e))
            return "not now";
        if (w.Has<Work.WorkSession>(e))
            return "busy working";
        if (w.TryGet<FreeMover>(e, out var m) && m.Drawn)
            return "weapon drawn";
        if (w.TryGet<Emoting>(e, out var last) && sim.Clock.Tick - last.Since < Gap)
            return "too soon";
        return null;
    }

    /// <summary>Show it, if they can. <paramref name="at"/> is kept only for Point, and only within reach.</summary>
    public static bool Show(Simulation sim, Entity e, Emote kind, int3? at)
    {
        if (Problem(sim, e, kind) != null)
            return false;
        var from = sim.World.Get<Position>(e).Cell;
        if (kind != Emote.Point || at is not { } to || !sim.Map.InBounds(to)
            || Math.Abs(to.X - from.X) > PointReach || Math.Abs(to.Y - from.Y) > PointReach)
            at = null;
        var now = sim.Clock.Tick;
        sim.World.Add(e, new Emoting { Kind = kind, Since = now, Until = now + Duration(kind), From = from, At = at });
        return true;
    }

    /// <summary>
    /// What they're showing now, or null: it's run out, they've stepped off the cell they did it
    /// on, or they're no longer up to it (knocked down, at work, a blade out).
    /// </summary>
    public static Emoting? Current(Simulation sim, Entity e)
    {
        var w = sim.World;
        if (!w.IsAlive(e) || !w.TryGet<Emoting>(e, out var em) || em.Until <= sim.Clock.Tick)
            return null;
        if (!w.TryGet<Position>(e, out var p) || p.Cell != em.From || !SpeechSystem.CanAct(sim, e) || w.Has<Carried>(e)
            || w.Has<Work.WorkSession>(e) || (w.TryGet<FreeMover>(e, out var m) && m.Drawn))
            return null;
        return em;
    }

    /// <summary>Drop the long-finished ones: they're only kept to space emotes out (<see cref="Gap"/>).</summary>
    public static void Tidy(Simulation sim)
    {
        var w = sim.World;
        if (w.Count<Emoting>() == 0)
            return;
        foreach (var e in w.Query<Emoting>())
            if (w.Get<Emoting>(e).Until + Gap <= sim.Clock.Tick)
                w.Remove<Emoting>(e);
    }
}

/// <summary>A character shows an emote (<see cref="Emotes"/>).</summary>
public sealed record EmoteCommand(Entity Actor, Emote Kind, int3? At) : ISimCommand, IActorCommand
{
    public void Apply(Simulation sim) => Emotes.Show(sim, Actor, Kind, At);
}

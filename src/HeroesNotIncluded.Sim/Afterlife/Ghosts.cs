using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Afterlife;

/// <summary>
/// On a body that died in a round: when, and how. The cause is the truth (for the Undertaker
/// later), read only through Perception; the moment of death is what a revival's freshness runs from.
/// </summary>
public struct Death
{
    public long Tick;
    public string Cause;
}

/// <summary>
/// A player whose character died in a round (docs/DESIGN.md, "Death"). Its own entity with a
/// <see cref="Position"/> and nothing else a creature has, so every query over creatures and
/// avatars passes it by. It floats where its player looks, sees what's around it
/// (<see cref="Perception.PerceptionView"/>), hears the living and talks with the other dead,
/// until someone brings its body back.
/// </summary>
public struct Ghost
{
    /// <summary>The player it is; <see cref="Guid.Empty"/> for a solo round's hero.</summary>
    public Guid PlayerId;

    /// <summary>The body it left.</summary>
    public Entity Body;

    /// <summary>Who they were, as their fellow ghosts know them.</summary>
    public string Name;

    /// <summary>Their player has left the game: called back, they come by themselves, and wait for them.</summary>
    public bool Away;

    /// <summary>What it has to haunt with, 0..1: it gathers slowly and signs spend it (<see cref="Hauntings"/>).</summary>
    public float Presence;

    /// <summary>The unfinished business it's lingering over (<see cref="Errands"/>): which errand (an index into the defs), for whom, how long so far.</summary>
    public int Errand;
    public Entity ErrandTarget;
    public int ErrandTicks;

    /// <summary>After an errand the dead rest a while: none counts before this tick.</summary>
    public long RestUntil;

    /// <summary>Errands seen to, and the last, in words, for the ghost.</summary>
    public int Done;
    public string LastDone;
}

/// <summary>Dying in a round, and being a ghost after it.</summary>
public static class Ghosts
{
    /// <summary>How far a ghost sees, in cells, whatever the light and through walls.</summary>
    public const int SightRadius = 12;

    /// <summary>Levels above and below a ghost it sees.</summary>
    public const int SightLevels = 1;

    /// <summary>Furthest one move may carry a ghost (cells): it drifts, it doesn't jump.</summary>
    public const float MaxStep = 4f;

    /// <summary>
    /// A creature has died (<see cref="NeedsSystem.Kill"/>). In a round, a person's body keeps
    /// how they died, and a player's character leaves a ghost: their avatar goes, so they steer
    /// nothing, and taking up another trade is closed to them (<see cref="Round.RoundSystem.TakeTrade"/>).
    /// Outside rounds (the practice ring, the demos) nothing changes: the ring freshens its dead.
    /// </summary>
    public static void OnDeath(Simulation sim, Entity body, string cause)
    {
        var w = sim.World;
        if (!sim.Round.Live || w.Get<Creature>(body).Def.Race != Race.Human)
            return;
        if (!w.Has<Death>(body))
            w.Add(body, new Death { Tick = sim.Clock.Tick, Cause = cause });
        if (w.Get<Creature>(body).Faction == Faction.Village)
            sim.Reckoning.Add(sim, 0f, sim.Defs.Season.DreadPerTownDeath); // the valley feels it

        Guid player;
        if (w.TryGet<PlayerCharacter>(body, out var pc))
            player = pc.PlayerId;
        else if (w.Has<Avatar>(body))
            player = Guid.Empty; // a solo round's hero
        else
            return; // a villager the AI ran: no ghost
        if (w.Has<Avatar>(body))
        {
            Work.WorkSystem.Stop(sim, body, null);
            w.Remove<Avatar>(body);
        }
        if (!Of(sim, player).IsNull)
            return;
        var ghost = w.Create();
        w.Add(ghost, w.Get<Position>(body));
        w.Add(ghost, new Ghost { PlayerId = player, Body = body, Name = w.Get<Creature>(body).Name, Presence = Hauntings.StartPresence, ErrandTarget = Entity.Null, LastDone = "" });
    }

    /// <summary>The player's ghost, or null if they aren't dead.</summary>
    public static Entity Of(Simulation sim, Guid player)
    {
        var w = sim.World;
        foreach (var e in w.Each<Ghost>())
            if (w.Get<Ghost>(e).PlayerId == player)
                return e;
        return Entity.Null;
    }

    /// <summary>Is there anyone dead and watching?</summary>
    public static bool Any(Simulation sim) => sim.World.Count<Ghost>() > 0;

    /// <summary>
    /// Drift toward a point (a ghost's player looks there), at most <see cref="MaxStep"/>, and up or
    /// down a level at most; or back to the body. A ghost goes through walls and over the ground,
    /// but stays on the map.
    /// </summary>
    public static void Move(Simulation sim, Entity ghost, float x, float y, int z, bool toBody)
    {
        var w = sim.World;
        if (!w.IsAlive(ghost) || !w.TryGet<Ghost>(ghost, out var g))
            return;
        ref var pos = ref w.Get<Position>(ghost);
        float3 to;
        if (toBody)
        {
            if (!w.IsAlive(g.Body) || !w.Has<Position>(g.Body))
                return;
            to = w.Get<Position>(g.Body).Pos;
        }
        else
        {
            if (!float.IsFinite(x) || !float.IsFinite(y))
                return;
            var d = new float2(x - pos.Pos.X, y - pos.Pos.Y);
            var len = MathF.Sqrt(d.X * d.X + d.Y * d.Y);
            if (len > MaxStep)
                d *= MaxStep / len;
            to = new float3(pos.Pos.X + d.X, pos.Pos.Y + d.Y, pos.Cell.Z + Math.Clamp(z - pos.Cell.Z, -1, 1));
        }
        var map = sim.Map;
        to = new float3(Math.Clamp(to.X, 0f, map.Width - 0.01f), Math.Clamp(to.Y, 0f, map.Height - 0.01f), Math.Clamp((int)MathF.Floor(to.Z), 0, map.Depth - 1));
        pos.Pos = to;
        pos.Cell = new int3((int)to.X, (int)to.Y, (int)to.Z);
    }

    /// <summary>How they died, in their own words ("You bled to death."), for the moment it happens.</summary>
    public static string LastWords(string cause) =>
        string.IsNullOrWhiteSpace(cause) || cause == "died" ? "You have died." : $"You {cause}.";
}

/// <summary>A ghost drifts (its player's view moved) or goes back to its body.</summary>
public sealed record GhostMoveCommand(Entity Actor, float X, float Y, int Z, bool ToBody = false) : ISimCommand, IActorCommand
{
    public void Apply(Simulation sim) => Ghosts.Move(sim, Actor, X, Y, Z, ToBody);
}

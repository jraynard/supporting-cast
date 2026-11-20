using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Combat;

/// <summary>
/// A stool (or anything <see cref="ThingDef.ThrownInBrawls"/>) picked up off the floor to throw
/// (<see cref="Hurling"/>): what it is, where it stood and how it faced, so it can be set back; and
/// since when it's been drawn back to throw (-1 while it's only held).
/// </summary>
public struct Hefting
{
    public ThingDef Def;
    public int3 From;
    public byte Facing;
    public long DrawnSince;
}

/// <summary>
/// Picking up a stool and hurling it, a player's improvised weapon (and a brawler's,
/// <see cref="Hospitality.BrawlCrisis.Hurl"/>). Fists up (combat mode), F at a stool picks it up off
/// the floor; held, the right button draws it back, filling a power gauge, and the left throws it the
/// way you aim. It's no bow: nothing shows where it'll land, and it goes off a little to one side or
/// the other of where you meant, further and wilder for shaky hands. It breaks over whoever it hits,
/// or where it comes down, and is gone (the carpenter's to make again, <see cref="Modding.Trades.Lost"/>).
/// F again sets it down where you point instead.
/// </summary>
public static class Hurling
{
    /// <summary>A stool hurled: what it does to whoever it hits (it breaks over them), at full power.</summary>
    public static readonly AttackDef Stool = new()
    {
        Name = "HurledStool", Label = "a hurled stool", Verb = "hurls a stool at", DamageMin = 3f, DamageMax = 7f,
        DamageType = DamageType.Blunt, Glance = 0f,
    };

    /// <summary>Seconds of drawing back to fill the gauge.</summary>
    public const float ChargeSeconds = 1f;

    /// <summary>How far (cells) a throw goes, barely drawn back and all the way.</summary>
    public const float NearThrow = 1.5f, FarThrow = 6f;

    /// <summary>How far off line (radians, either way) a throw may go with steady hands; shaky ones add to it.</summary>
    public const float Wobble = 0.2f;

    /// <summary>How near the line (cells) someone must be for it to hit them.</summary>
    public const float HitsWithin = 0.55f;

    /// <summary>The damage a throw does barely drawn back, against all the way.</summary>
    public const float WeakestThrow = 0.5f;

    /// <summary>A piece that can be picked up and thrown.</summary>
    public static bool Throwable(Simulation sim, Entity thing) =>
        sim.World.TryGet<Thing>(thing, out var t) && t.Def.ThrownInBrawls && !sim.World.Has<Construction>(thing);

    /// <summary>Holding one.</summary>
    public static bool Holding(Simulation sim, Entity e) => sim.World.Has<Hefting>(e);

    /// <summary>How far the throw's drawn back, 0..1 (0 if it isn't).</summary>
    public static float Charge(Simulation sim, Entity e) =>
        sim.World.TryGet<Hefting>(e, out var h) && h.DrawnSince >= 0
            ? Math.Clamp((sim.Clock.Tick - h.DrawnSince) / (ChargeSeconds * SimClock.TicksPerSecond), 0f, 1f)
            : 0f;

    /// <summary>The F action at a cell for a player fists-up: pick up the stool there, or set down the one held.</summary>
    public static AvatarAction? ActionAt(Simulation sim, Entity avatar, int3 cell, int3 here, Entity held)
    {
        var w = sim.World;
        if (!AvatarSystem.InReach(here, cell))
            return null;
        if (w.TryGet<Hefting>(avatar, out var h))
            return Free(sim, cell) ? new AvatarAction(AvatarActionKind.Carry, $"Set the {h.Def.Label} down", cell, Entity.Null) : null;
        if (!held.IsNull || !w.TryGet<Combat.FreeMover>(avatar, out var fm) || !fm.Drawn)
            return null;
        var site = sim.Map.Structure[sim.Map.Idx(cell)];
        return !site.IsNull && Throwable(sim, site)
            ? new AvatarAction(AvatarActionKind.Carry, $"Pick up the {w.Get<Thing>(site).Def.Label}", cell, site)
            : null;
    }

    /// <summary>Somewhere a held piece could be set down: standable, nothing standing there.</summary>
    private static bool Free(Simulation sim, int3 cell) =>
        sim.Map.InBounds(cell) && sim.Map.Structure[sim.Map.Idx(cell)].IsNull && sim.Map.Standable(sim.Map.Idx(cell)) && sim.Map.Passable(cell);

    /// <summary>Do the F action (<see cref="ActionAt"/>): pick it up, or set it down. False if it wasn't one of these.</summary>
    public static bool Act(Simulation sim, Entity avatar, AvatarAction action)
    {
        var w = sim.World;
        if (w.Has<Hefting>(avatar) && action.Target.IsNull)
        {
            SetDown(sim, avatar, action.Cell);
            return true;
        }
        if (action.Target.IsNull || !Throwable(sim, action.Target) || w.Has<Hefting>(avatar) || !w.Get<Carrying>(avatar).Item.IsNull)
            return false;
        PickUp(sim, avatar, action.Target);
        return true;
    }

    /// <summary>Pick a piece up off the floor: it's gone from where it stood, held.</summary>
    public static void PickUp(Simulation sim, Entity e, Entity piece)
    {
        var t = sim.World.Get<Thing>(piece);
        sim.DestroyThing(piece);
        sim.Map.MarkStructureChanged();
        sim.World.Add(e, new Hefting { Def = t.Def, From = t.Cell, Facing = t.Facing, DrawnSince = -1 });
    }

    /// <summary>Set the held piece down at <paramref name="cell"/> (or where it came from, or beside them): back as it was.</summary>
    public static void SetDown(Simulation sim, Entity e, int3? cell = null)
    {
        var w = sim.World;
        if (!w.TryGet<Hefting>(e, out var h))
            return;
        w.Remove<Hefting>(e);
        var here = w.Get<Position>(e).Cell;
        var at = cell is { } c && Free(sim, c) ? c
            : sim.SpiralFrom(here, 2).Where(x => x.Z == here.Z && Free(sim, x)).Cast<int3?>().FirstOrDefault();
        if (at is { } put)
        {
            sim.SpawnStructure(h.Def, put, h.Facing);
            sim.Map.MarkStructureChanged();
        }
        else
            Modding.Trades.Lost(sim, h.Def, h.From, h.Facing); // nowhere to put it: as good as broken
    }

    /// <summary>Draw the held piece back to throw (as of <paramref name="at"/>), or let it down again. False if nothing's held.</summary>
    public static bool Draw(Simulation sim, Entity e, bool back, long at)
    {
        var w = sim.World;
        if (!w.Has<Hefting>(e))
            return false;
        ref var h = ref w.Get<Hefting>(e);
        h.DrawnSince = back ? (h.DrawnSince >= 0 ? h.DrawnSince : Math.Min(at, sim.Clock.Tick)) : -1;
        return true;
    }

    /// <summary>
    /// Throw the piece drawn back along <paramref name="aim"/>, give or take: as far and as hard as the
    /// gauge says, off line a little. It breaks over the first one in its way, or where it comes down.
    /// The one it hit, Entity.Null if nobody; null if nothing was drawn back to throw.
    /// </summary>
    public static Entity? Throw(Simulation sim, Entity e, float aim)
    {
        var w = sim.World;
        if (!w.TryGet<Hefting>(e, out var h) || h.DrawnSince < 0)
            return null;
        var power = Charge(sim, e);
        w.Remove<Hefting>(e);
        var hands = Work.Hands.Of(sim, e, Skill.Melee);
        aim += sim.Rand.Range(-1f, 1f) * (Wobble + hands.Tremor) / hands.Tolerance;
        var range = (NearThrow + (FarThrow - NearThrow) * power) * sim.Rand.Range(0.85f, 1.15f);
        var hit = Fly(sim, e, h.Def, aim, range, WeakestThrow + (1f - WeakestThrow) * power);
        Modding.Trades.Lost(sim, h.Def, h.From, h.Facing); // gone: the carpenter's to make again
        if (Hospitality.BrawlCrisis.Of(sim) is { } brawl)
            brawl.Broke(sim, h.From);
        if (w.Has<FreeMover>(e))
            w.Get<FreeMover>(e).Aim = aim;
        return hit;
    }

    /// <summary>
    /// A piece of <paramref name="def"/> flies from <paramref name="thrower"/> along <paramref name="aim"/>
    /// up to <paramref name="range"/> cells (a wall stops it): drawn tumbling, it breaks over the first
    /// creature near its line, or where it lands. The one it hit, or Entity.Null.
    /// </summary>
    public static Entity Fly(Simulation sim, Entity thrower, ThingDef def, float aim, float range, float force)
    {
        var w = sim.World;
        var from = w.Get<Position>(thrower).Pos;
        var end = Reach.ArrowEnd(sim, from, aim, range);
        var dir = new float2(DetMath.Cos(aim), DetMath.Sin(aim));
        var length = MathF.Sqrt((end.X - from.X) * (end.X - from.X) + (end.Y - from.Y) * (end.Y - from.Y));
        var hit = Entity.Null;
        var best = float.MaxValue;
        var z = w.Get<Position>(thrower).Cell.Z;
        foreach (var o in sim.Creatures())
        {
            if (o == thrower || !w.Get<Creature>(o).Able || !w.TryGet<Position>(o, out var p) || p.Cell.Z != z)
                continue;
            var rel = new float2(p.Pos.X - from.X, p.Pos.Y - from.Y);
            var along = rel.X * dir.X + rel.Y * dir.Y;
            var across = MathF.Abs(rel.X * dir.Y - rel.Y * dir.X);
            if (along > 0.3f && along <= length + 0.3f && across <= HitsWithin && along < best)
                (hit, best) = (o, along);
        }
        var to = hit.IsNull ? end : w.Get<Position>(hit).Pos;
        sim.RecentShots.RemoveAll(s => sim.Clock.Tick - s.Tick > SimClock.TicksPerSecond * 5);
        sim.RecentShots.Add(new ShotEvent(sim.Clock.Tick, from, to, Ranged: true, Hit: !hit.IsNull, "Crash!", def.Sprite));
        if (!hit.IsNull && Reach.Harms(sim, thrower, hit))
        {
            RealtimeAttacks.Strike(sim, thrower, hit, Stool, force);
            sim.Messages.Add(MessageKind.Warning, $"{w.Get<Creature>(thrower).Name} hurls a {def.Label} at {w.Get<Creature>(hit).Name}, and it breaks over them!", w.Get<Position>(hit).Cell);
        }
        else
            sim.Messages.Add(MessageKind.Info, $"{w.Get<Creature>(thrower).Name} hurls a {def.Label}, and it smashes to pieces.", new int3((int)MathF.Floor(to.X), (int)MathF.Floor(to.Y), z));
        return hit;
    }
}

/// <summary>
/// A player's stool (<see cref="Hurling"/>), worked like a bucket: draw it back (<paramref name="Back"/>
/// true, the right button held) and let it down (false); <paramref name="Throw"/> hurls it along
/// <paramref name="Aim"/>. <paramref name="At"/>: the tick the button went down.
/// </summary>
public sealed record HurlCommand(Entity Avatar, bool Back, bool Throw, float Aim, long At, float AtX = float.NaN, float AtY = float.NaN, int AtZ = 0)
    : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (!sim.World.IsAlive(Avatar) || !sim.World.Has<Creature>(Avatar) || !sim.World.Get<Creature>(Avatar).Able)
            return;
        if (!Throw)
        {
            Hurling.Draw(sim, Avatar, Back, Blows.Stamp(sim, At));
            return;
        }
        if (!float.IsFinite(Aim))
            return;
        if (sim.World.Has<FreeMover>(Avatar))
            FreeMoveSystem.Claim(sim, Avatar, AtX, AtY, AtZ, stopped: false);
        Hurling.Throw(sim, Avatar, MathF.IEEERemainder(Aim, MathF.Tau));
    }
}

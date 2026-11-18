using HeroesNotIncluded.Sim.Afterlife;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Environment;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Bots;

/// <summary>
/// Pitching in when it goes wrong, as players do in a round: a bucket line at a fire or the dragon,
/// the downed carried to a bed, a neighbour's bleeding bound, the dead called back (or carried to
/// the shrine for the empath). Only what it can see, only through the commands a player has.
/// </summary>
public sealed partial class BotPlayer
{
    /// <summary>How far off (cells) it'll see a fire or the dragon as its business.</summary>
    public const int DouseRange = 30;

    /// <summary>How far off (cells) it'll go to someone downed, bleeding or dead.</summary>
    public const int HelpRange = 25;

    /// <summary>The nearest it stands to what it's throwing at (cells): out of the heat, and out of the dragon's bite.</summary>
    public const int ThrowFrom = 3;

    // The throw in hand: where it's aimed, and since when it's been swinging.
    private int3? _throwAt;
    private long _swingSince;

    /// <summary>
    /// The swing of a bucket in hand, every tick: let go when the swing's as far back as the target
    /// is far off, as a player times it by eye. True while it's swinging (nothing else till it's thrown).
    /// </summary>
    private bool Throwing(Simulation sim, Entity me, long now)
    {
        var w = sim.World;
        if (!w.Has<BucketSwing>(me) || _throwAt is not { } target)
        {
            _throwAt = null;
            return false;
        }
        var from = w.Get<Position>(me).Cell;
        float dx = target.X - from.X, dy = target.Y - from.Y;
        var want = Math.Clamp((MathF.Sqrt(dx * dx + dy * dy) - 1f) / (Buckets.MaxRange - 1), 0f, 1f);
        var swing = Buckets.SwingOf(sim, me);
        if (MathF.Abs(swing - want) <= 0.06f || now - _swingSince > SimClock.TicksPerSecond * 4)
        {
            Send(new BucketCommand(me, false, true, DetMath.Atan2(dy, dx), now));
            _throwAt = null;
        }
        return true;
    }

    /// <summary>What's burning (or the dragon, on the ground) that a bucket from where it stands could reach, nearest first.</summary>
    private int3? DouseTarget(Simulation sim, Entity me, PerceptionView view)
    {
        var w = sim.World;
        var here = w.Get<Position>(me).Cell;
        int3? best = null;
        var bestD = int.MaxValue;
        foreach (var e in view.Creatures())
            if (w.Get<Creature>(e) is { Dead: false, Def.Race: Race.Dragon } && w.Get<Position>(e).Cell is var at
                && at.Z - here.Z is >= -1 and <= 2 && Maps.Map.Distance(at, here) is var d && d <= DouseRange && d < bestD)
                (best, bestD) = (at, d);
        if (best != null)
            return best; // the dragon first: soaked enough, it's driven off
        // A fire the town's been told of ("Fire at the inn!") is known all over: its building's cells are.
        var announced = new HashSet<Entity>();
        foreach (var c in sim.Director.Active)
            if (c is Director.FireCrisis fire)
                announced.Add(fire.Building);
        foreach (var i in sim.Fire.BurningTiles)
        {
            var f = sim.Map.CellOf(i);
            // Ties to the lower cell: the set's order mustn't decide.
            if (f.Z - here.Z is >= -1 and <= 2 && Maps.Map.Distance(f, here) is var d && (d < bestD || (d == bestD && best is { } b && sim.Map.Idx(f) < sim.Map.Idx(b)))
                && d <= DouseRange && Known(f, d))
                (best, bestD) = (f, d);
        }
        return best;

        // Seen, or close enough to smell the smoke and hear it, or where the alarm said.
        bool Known(int3 f, int d) => d <= SmokeCells || view.CanSeeCell(f) || (announced.Count > 0 && announced.Contains(Buildings.At(sim, f)));
    }

    /// <summary>A fire this near is known without being seen: the smoke, the glow, the crackle.</summary>
    public const int SmokeCells = 10;

    /// <summary>
    /// A fire or the dragon in sight: a bucket from the well, filled, carried to a few steps off,
    /// swung and thrown; again till it's out. With nothing to throw at, the bucket goes back to the
    /// well. True while it's at it.
    /// </summary>
    private bool Douse(Simulation sim, Entity me, PerceptionView view, long now)
    {
        var w = sim.World;
        if (!sim.Round.Live || w.Get<Creature>(me).Downed)
            return false;
        var held = w.Get<Carrying>(me).Item;
        var target = DouseTarget(sim, me, view);
        if (target is not { } at)
            return PutBucketBack(sim, me, now, held);
        if (w.TryGet<Health>(me, out var h) && h.Injuries.Sum(i => i.Damage) >= SeeTheEmpathFrom)
            return false; // hurt: let the others
        // The nervy keep their heads down when it's the dragon; only the bravest go at a blaze close up.
        var dragon = view.Creatures().Any(e => w.Get<Creature>(e) is { Dead: false, Def.Race: Race.Dragon });
        if (Temper.Nerve < (dragon ? 0.4f : 0.15f))
            return PutBucketBack(sim, me, now, held);
        var here = w.Get<Position>(me).Cell;
        var wells = Buckets.Wells(sim);
        if (wells.Count == 0)
            return false;
        var well = wells.MinBy(e => Maps.Map.Distance(w.Get<Thing>(e).Cell, here))!;

        if (Buckets.IsFull(sim, held))
        {
            var d = Maps.Map.Distance(here, at);
            if (d >= ThrowFrom && d <= Buckets.MaxRange && !NearFire(sim, here))
            {
                Doing = "throwing water";
                if (Alarm($"douse:{now / SimClock.TicksPerHour}"))
                    Say(sim, me, "douse", Volume.Shout, now);
                _throwAt = at;
                _swingSince = now;
                Send(new BucketCommand(me, true, false, 0f, now));
                return true;
            }
            Doing = "carrying water to the fire";
            if (ThrowSpot(sim, me, at) is { } spot)
                Walk(sim, me, spot, now);
            return true;
        }
        if (Buckets.IsBucket(sim, held))
        {
            Doing = "drawing water";
            return AtStation(sim, me, well, now, x => Use(sim, me, x));
        }
        // A bucket in the pack: into the hands. Hands full of something else: into the pack with that.
        if (sim.PackOf(me) is { IsNull: false } pack && sim.StoredIn(pack).FirstOrDefault(e => Buckets.IsBucket(sim, e), Entity.Null) is { IsNull: false } packed)
        {
            if (held.IsNull)
                Send(new HoldCommand(me, packed));
            else
                FreeHand(sim, me);
            return true;
        }
        if (!held.IsNull)
        {
            FreeHand(sim, me); // into the pack, or a keg set down: it won't go in one
            return true;
        }
        // One lying about (at the well, mostly), in sight and not spoken for.
        var bucket = Entity.Null;
        var best = int.MaxValue;
        foreach (var e in sim.AvailableItems())
            if (Buckets.IsBucket(sim, e) && view.CanSee(e) && !sim.Reservations.IsReservedByOther(e, me)
                && Maps.Map.Distance(w.Get<Thing>(e).Cell, here) is var d && d < best && d <= DouseRange)
                (bucket, best) = (e, d);
        if (bucket.IsNull)
            return false;
        Doing = "fetching a bucket";
        return AtStation(sim, me, bucket, now, x => Use(sim, me, x));
    }

    /// <summary>Burning in its own cell or the next: no place to stand and throw from.</summary>
    private static bool NearFire(Simulation sim, int3 cell)
    {
        foreach (var c in sim.SpiralFrom(cell, 1))
            if (sim.Map.InBounds(c) && sim.Map.Fire[sim.Map.Idx(c)] > 0)
                return true;
        return false;
    }

    /// <summary>Somewhere to throw at a cell from: <see cref="ThrowFrom"/> or more off it, in range, not alight, the nearest to hand.</summary>
    private static int3? ThrowSpot(Simulation sim, Entity me, int3 target)
    {
        var here = sim.World.Get<Position>(me).Cell;
        int3? best = null;
        var bestD = int.MaxValue;
        foreach (var c in sim.SpiralFrom(target, Buckets.MaxRange - 1))
        {
            if (c.Z != here.Z && c.Z != target.Z || Maps.Map.Distance(c, target) < ThrowFrom || !sim.Map.Passable(c) || NearFire(sim, c))
                continue;
            var d = Maps.Map.Distance(here, c);
            if (d < bestD && sim.Regions.CanReach(here, c, PathEnd.OnTile))
                (best, bestD) = (c, d);
        }
        return best;
    }

    /// <summary>The fire's out: a bucket in hand or pack goes back down by the well, for the next one.</summary>
    private bool PutBucketBack(Simulation sim, Entity me, long now, Entity held)
    {
        var w = sim.World;
        var packed = sim.PackOf(me) is { IsNull: false } pack ? sim.StoredIn(pack).FirstOrDefault(e => Buckets.IsBucket(sim, e), Entity.Null) : Entity.Null;
        if ((!Buckets.IsBucket(sim, held) && packed.IsNull) || KeepsBucket)
            return false;
        var here = w.Get<Position>(me).Cell;
        var wells = Buckets.Wells(sim);
        if (wells.Count == 0)
            return false;
        var at = w.Get<Thing>(wells.MinBy(e => Maps.Map.Distance(w.Get<Thing>(e).Cell, here))!).Cell;
        Doing = "taking the bucket back";
        if (Maps.Map.Distance(here, at) > 2)
        {
            Walk(sim, me, at, now);
            return true;
        }
        Send(Buckets.IsBucket(sim, held) ? new AvatarDropCommand(me, here) : new DropFromPackCommand(me, packed));
        return true;
    }

    /// <summary>The work key at a thing in reach: what F does there (pick it up, fill a bucket, lay someone down).</summary>
    private void Use(Simulation sim, Entity me, Entity at) => Use(me, sim.LocationOf(at));

    private void Use(Entity me, int3 cell)
    {
        Send(new AvatarWorkCommand(me, true, cell));
        Send(new AvatarWorkCommand(me, false));
    }

    /// <summary>
    /// Someone down in sight: the dead called back by an empath with the mana (or carried to a
    /// shrine pallet, where they keep, if there's an empath alive); the living downed carried to a
    /// bed; the bleeding bound. True while at it.
    /// </summary>
    private bool PitchIn(Simulation sim, Entity me, PerceptionView view, long now)
    {
        if (!sim.Round.Live || sim.World.Get<Creature>(me).Downed)
            return false;
        return CarryOn(sim, me, now) || CallBack(sim, me, view, now) || CarryDowned(sim, me, view, now) || BindOther(sim, me, view, now);
    }

    /// <summary>Someone in its arms: to a bed (the dead, a shrine pallet), and laid there.</summary>
    private bool CarryOn(Simulation sim, Entity me, long now)
    {
        var w = sim.World;
        var load = Rescues.CarriedBy(sim, me);
        if (load.IsNull)
            return false;
        var dead = w.Get<Creature>(load).Dead;
        var bed = dead ? ShrinePallet(sim, me) : ShrinePallet(sim, me) is { IsNull: false } pallet ? pallet : Rescues.BedFor(sim, me, load);
        if (bed.IsNull)
        {
            Send(new AvatarDropCommand(me, w.Get<Position>(me).Cell)); // nowhere to take them
            return false;
        }
        Doing = dead ? $"carrying {w.Get<Creature>(load).Name}'s body to the shrine" : $"carrying {w.Get<Creature>(load).Name} to a bed";
        var cell = w.Get<Thing>(bed).Cell;
        if (AvatarSystem.InReach(sim, w.Get<Position>(me).Cell, bed))
            Use(me, cell);
        else
            Walk(sim, me, cell, now);
        return true;
    }

    /// <summary>A free pallet at the nearest shrine, or Entity.Null.</summary>
    private static Entity ShrinePallet(Simulation sim, Entity me)
    {
        var w = sim.World;
        var here = w.Get<Position>(me).Cell;
        var best = Entity.Null;
        var bestD = int.MaxValue;
        var lying = new HashSet<int3>();
        foreach (var e in sim.Creatures())
            if (e != me && !w.Has<Carried>(e))
                lying.Add(w.Get<Position>(e).Cell);
        foreach (var b in w.Query<PresetBuilding>())
        {
            if (w.Get<PresetBuilding>(b).Def.Name != "Shrine")
                continue;
            foreach (var bed in Shops.ShopSystem.BedsOf(sim, b))
                if (!lying.Contains(w.Get<Thing>(bed).Cell) && Maps.Map.Distance(here, w.Get<Thing>(bed).Cell) is var d && d < bestD)
                    (best, bestD) = (bed, d);
        }
        return best;
    }

    /// <summary>A fresh body in sight, nobody's carrying, not already brought back: the person it was.</summary>
    private Entity FreshBody(Simulation sim, Entity me, PerceptionView view)
    {
        var w = sim.World;
        var here = w.Get<Position>(me).Cell;
        var best = Entity.Null;
        var bestD = int.MaxValue;
        foreach (var e in view.Creatures())
            if (e != me && w.Get<Creature>(e) is { Dead: true, Def.Race: Race.Human } && Revival.Fresh(sim, e) && !w.Has<Carried>(e)
                && !w.Has<Calling>(e) && Maps.Map.Distance(w.Get<Position>(e).Cell, here) is var d && d <= HelpRange && d < bestD)
                (best, bestD) = (e, d);
        return best;
    }

    /// <summary>
    /// A fresh body: an empath with the mana kneels at it and weaves the Calling back; anyone else
    /// (or one short of mana) carries it to a shrine pallet, where it keeps longer, if there's an
    /// empath alive to call it back.
    /// </summary>
    private bool CallBack(Simulation sim, Entity me, PerceptionView view, long now)
    {
        var w = sim.World;
        var body = FreshBody(sim, me, view);
        if (body.IsNull || Revival.CallMending(sim) is not { } calling)
            return false;
        var name = w.Get<Creature>(body).Name;
        var cell = w.Get<Position>(body).Cell;
        if (w.TryGet<Mana>(me, out var mana) && mana.Current >= Revival.CallCost(sim))
        {
            Doing = $"calling {name} back";
            if (Revival.BodyBeside(sim, me) == body)
            {
                _weave = calling.RuneDefs.Select(r => r.Index).ToList();
                Send(new CallBackCommand(me, body));
            }
            else
                Walk(sim, me, cell, now);
            return true;
        }
        if (!Revival.AnyEmpath(sim) || Revival.OnShrinePallet(sim, body) || !Rescues.CanCarry(sim, me, body) || ShrinePallet(sim, me).IsNull)
            return false;
        Doing = $"picking up {name}'s body";
        if (!w.Get<Carrying>(me).Item.IsNull)
            FreeHand(sim, me);
        else if (AvatarSystem.InReach(w.Get<Position>(me).Cell, cell))
            Use(me, cell);
        else
            Walk(sim, me, cell, now);
        return true;
    }

    /// <summary>Someone downed and lying in the open, in sight: picked up, for a bed.</summary>
    private bool CarryDowned(Simulation sim, Entity me, PerceptionView view, long now)
    {
        var w = sim.World;
        var here = w.Get<Position>(me).Cell;
        var hurt = Entity.Null;
        var bestD = int.MaxValue;
        foreach (var e in view.Creatures())
            if (e != me && w.Get<Creature>(e) is { Dead: false, Downed: true, Def.Race: Race.Human } c && !Factions.Attacker(c.Faction)
                && !w.Has<Carried>(e) && !InBed(sim, e) && Rescues.CanCarry(sim, me, e)
                && Maps.Map.Distance(w.Get<Position>(e).Cell, here) is var d && d <= HelpRange && d < bestD)
                (hurt, bestD) = (e, d);
        if (hurt.IsNull || (ShrinePallet(sim, me).IsNull && Rescues.BedFor(sim, me, hurt).IsNull))
            return false;
        var cell = w.Get<Position>(hurt).Cell;
        Doing = $"picking up {w.Get<Creature>(hurt).Name}";
        if (!w.Get<Carrying>(me).Item.IsNull)
            FreeHand(sim, me);
        else if (AvatarSystem.InReach(here, cell))
        {
            if (Alarm($"carry:{hurt.Index}:{now / SimClock.TicksPerDay}"))
                Say(sim, me, "carry", Volume.Say, now, who: w.Get<Creature>(hurt).Name);
            Use(me, cell);
        }
        else
            Walk(sim, me, cell, now);
        return true;
    }

    /// <summary>Lying in a bed already: no need to carry them.</summary>
    private static bool InBed(Simulation sim, Entity e)
    {
        var cell = sim.World.Get<Position>(e).Cell;
        if (!sim.Map.InBounds(cell))
            return false;
        var site = sim.Map.Structure[sim.Map.Idx(cell)];
        return !site.IsNull && sim.World.TryGet<Thing>(site, out var t) && t.Def.Building is { IsBed: true };
    }

    /// <summary>Someone bleeding near, with bandages to hand: bound where they stand (or lie).</summary>
    private bool BindOther(Simulation sim, Entity me, PerceptionView view, long now)
    {
        var w = sim.World;
        if (Bandaging.BandagesOn(sim, me) == 0)
            return false;
        var here = w.Get<Position>(me).Cell;
        var patient = Entity.Null;
        var bestD = int.MaxValue;
        foreach (var e in view.Creatures())
            if (e != me && w.Get<Creature>(e) is { Dead: false, Def.Race: Race.Human } c && !Factions.Attacker(c.Faction) && !w.Has<Carried>(e)
                && Bandaging.WoundsToBind(sim, e, me) > 0 && Maps.Map.Distance(w.Get<Position>(e).Cell, here) is var d && d <= HelpRange / 2 && d < bestD)
                (patient, bestD) = (e, d);
        if (patient.IsNull)
            return false;
        Doing = $"binding {w.Get<Creature>(patient).Name}'s wounds";
        if (Shops.Handing.InReach(sim, me, patient))
        {
            if (Bandaging.CantBind(sim, me, patient) != null)
                return false;
            Send(new BandageCommand(me, patient));
        }
        else
            Walk(sim, me, w.Get<Position>(patient).Cell, now);
        return true;
    }
}

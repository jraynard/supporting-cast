using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Environment;

/// <summary>
/// Fighting fire with water: buckets hang at the well, are filled there, and thrown. Beating at
/// flames puts out one cell, from beside it, and the next spark lights it again; a bucket's water
/// knocks down a patch from a few steps off and soaks it (<see cref="FireSystem.Soak"/>), so it
/// won't catch for a while. That's what gives a town something to do against a dragon's burning:
/// soak the roofs ahead of it. Thrown by hand (<see cref="ThrowSession"/>: hold F to wind up,
/// let go), or by the AI as its firefighting when a bucket's to be had (<see cref="BucketJob"/>).
/// </summary>
public static class Buckets
{
    /// <summary>The bucket, empty and full (<c>data/defs/village.json</c>).</summary>
    public const string Empty = "Bucket", Full = "WaterBucket";

    /// <summary>How many hang at each well when a town's laid out.</summary>
    public const int PerWell = 4;

    /// <summary>The farthest a bucket's thrown, in cells.</summary>
    public const int MaxRange = 6;

    /// <summary>Fire knocked out where it lands, and in the cells round it.</summary>
    public const float Douse = 1f, DouseAround = 0.7f;

    /// <summary>How long a soaked cell won't catch, game hours (where it lands; round it, two thirds).</summary>
    public const float SoakHours = 2f;

    public static bool IsBucket(Simulation sim, Entity e) =>
        sim.World.IsAlive(e) && sim.World.TryGet<Thing>(e, out var t) && t.Def.Name is Empty or Full;

    public static bool IsFull(Simulation sim, Entity e) =>
        sim.World.IsAlive(e) && sim.World.TryGet<Thing>(e, out var t) && t.Def.Name == Full;

    public static bool IsWell(Simulation sim, Entity e) =>
        sim.World.IsAlive(e) && sim.World.TryGet<Thing>(e, out var t) && t.Def.Name == "Well";

    /// <summary>Every well in town, in entity order.</summary>
    public static List<Entity> Wells(Simulation sim)
    {
        var wells = new List<Entity>();
        foreach (var e in sim.World.Query<Thing>())
            if (sim.World.Get<Thing>(e).Def.Name == "Well" && sim.World.Get<Thing>(e).Spawned)
                wells.Add(e);
        return wells;
    }

    /// <summary>Hang <see cref="PerWell"/> empty buckets round each well (laying out a town).</summary>
    public static void HangAtWells(Simulation sim)
    {
        var def = sim.Defs.Thing(Empty);
        foreach (var well in Wells(sim))
        {
            var at = sim.World.Get<Thing>(well).Cell;
            for (var k = 0; k < PerWell; k++)
                sim.PlaceItemNear(sim.CreateItem(def, 1), at);
        }
    }

    /// <summary>A bucket lying this far from every well has strayed: someone carries it back.</summary>
    public const int StrayFrom = 4;

    /// <summary>A bucket left lying away from the wells (no fire going), carried back to the nearest, if any.</summary>
    public static Job? ReturnStray(Simulation sim, Entity pawn)
    {
        if (sim.Fire.BurningTiles.Count > 0)
            return null; // they're in use
        var wells = Wells(sim);
        if (wells.Count == 0)
            return null;
        var w = sim.World;
        var from = w.Get<Position>(pawn).Cell;
        foreach (var e in sim.AvailableItems())
        {
            if (!IsBucket(sim, e) || sim.Reservations.IsReservedByOther(e, pawn))
                continue;
            var at = w.Get<Thing>(e).Cell;
            var well = wells.MinBy(x => Map.Distance(w.Get<Thing>(x).Cell, at))!;
            var wellAt = w.Get<Thing>(well).Cell;
            if (Map.Distance(wellAt, at) < StrayFrom || !Navigation.CanReach(sim, pawn, from, at, PathEnd.OnTile))
                continue;
            foreach (var c in sim.SpiralFrom(wellAt, 2))
                if (c != wellAt && sim.Map.InBounds(c) && c.Z == wellAt.Z && sim.Map.Passable(c) && sim.Map.Item[sim.Map.Idx(c)].IsNull
                    && !sim.Reservations.IsTileReservedByOther(c, pawn) && sim.Regions.CanReach(from, c, PathEnd.OnTile))
                    return new HaulJob(e, c);
        }
        return null;
    }

    /// <summary>Swap what's in their hands for a bucket of the other kind (filling it, or emptying it).</summary>
    private static void Swap(Simulation sim, Entity who, string into)
    {
        var w = sim.World;
        var old = w.Get<Carrying>(who).Item;
        w.Get<Carrying>(who).Item = Entity.Null;
        sim.DestroyThing(old);
        var made = sim.CreateItem(sim.Defs.Thing(into), 1);
        w.Get<Carrying>(who).Item = made;
    }

    /// <summary>Fill the empty bucket in their hands at a well beside them. False if there's none to fill.</summary>
    public static bool Fill(Simulation sim, Entity who)
    {
        var held = sim.World.Get<Carrying>(who).Item;
        if (!IsBucket(sim, held) || IsFull(sim, held))
            return false;
        Swap(sim, who, Full);
        return true;
    }

    /// <summary>Where a throw from <paramref name="from"/> toward <paramref name="aim"/> lands, this far out (cells).</summary>
    public static int3 Landing(int3 from, int3 aim, float distance)
    {
        var dx = (float)(aim.X - from.X);
        var dy = (float)(aim.Y - from.Y);
        var len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 1e-3f)
            return from;
        var x = from.X + (int)MathF.Round(dx / len * distance);
        var y = from.Y + (int)MathF.Round(dy / len * distance);
        return new int3(x, y, from.Z);
    }

    /// <summary>
    /// Water lands here: fire knocked down and the ground soaked, in the cell and round it, from
    /// the level below to the eaves above (a bucket thrown up wets a roof's edge), and a dragon
    /// caught in it (its bulk reaching a cell further) soaked (<see cref="DragonBrain.Douse"/>).
    /// Returns how many burning cells it put out. Recorded in <see cref="Simulation.RecentSplashes"/>
    /// for the renderer and sounds (thrown by <paramref name="thrower"/>, if anyone).
    /// </summary>
    public static int Splash(Simulation sim, int3 at, Entity? thrower = null)
    {
        var map = sim.Map;
        var soak = SimClock.HoursToTicks(SoakHours);
        var put = 0;
        var by = thrower ?? Entity.Null;
        var from = !by.IsNull && sim.World.IsAlive(by) && sim.World.TryGet<Position>(by, out var tp) ? tp.Pos : Position.CenterOf(at);
        var ev = new SplashEvent(sim.Clock.Tick, by, from, at, []);
        for (var dz = -1; dz <= 2; dz++)
            for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    var c = new int3(at.X + dx, at.Y + dy, at.Z + dz);
                    if (!map.InBounds(c))
                        continue;
                    var i = map.Idx(c);
                    var middle = dx == 0 && dy == 0;
                    if (map.Fire[i] > 0)
                    {
                        ev.Doused.Add((i, map.Fire[i]));
                        map.Fire[i] = Math.Max(0f, map.Fire[i] - (middle ? Douse : DouseAround));
                        if (map.Fire[i] <= 0)
                            put++;
                    }
                    sim.Fire.Soak(sim, i, middle ? soak : soak * 2 / 3);
                }
        foreach (var e in sim.Creatures().ToList())
        {
            if (sim.World.Get<Creature>(e) is not { Dead: false, Def.Race: Race.Dragon })
                continue;
            var c = sim.World.Get<Position>(e).Cell;
            if (Math.Abs(c.X - at.X) <= 2 && Math.Abs(c.Y - at.Y) <= 2 && c.Z - at.Z is >= -1 and <= 2)
                DragonBrain.Douse(sim, e);
        }
        sim.RecentSplashes.RemoveAll(s => sim.Clock.Tick - s.Tick > SplashShowTicks);
        sim.RecentSplashes.Add(ev);
        return put;
    }

    /// <summary>How long a splash stays in <see cref="Simulation.RecentSplashes"/>.</summary>
    public const int SplashShowTicks = SimClock.TicksPerSecond * 4;

    /// <summary>
    /// With a full bucket in hand, pointing somewhere within a throw (not the well, not where you
    /// stand): throw the water there. Checked before arm's reach, as a bow's shot is.
    /// </summary>
    public static AvatarAction? ThrowAt(Simulation sim, Entity avatar, int3 cell, int3 here)
    {
        if (!IsFull(sim, sim.World.Get<Carrying>(avatar).Item) || (cell.X == here.X && cell.Y == here.Y)
            || Map.Distance(new int3(cell.X, cell.Y, here.Z), here) > MaxRange || IsWell(sim, sim.Map.Structure[sim.Map.Idx(cell)]))
            return null;
        var burning = sim.Map.Fire[sim.Map.Idx(cell)] > 0;
        return new AvatarAction(AvatarActionKind.ThrowWater, burning ? "Throw the water on the fire" : "Throw the water", cell, Entity.Null);
    }

    /// <summary>At the well with an empty bucket in hand: fill it.</summary>
    public static AvatarAction? FillAt(Simulation sim, Entity avatar, Entity site, int3 cell)
    {
        var held = sim.World.Get<Carrying>(avatar).Item;
        return IsWell(sim, site) && IsBucket(sim, held) && !IsFull(sim, held)
            ? new AvatarAction(AvatarActionKind.FillBucket, "Draw water into the bucket", cell, site)
            : null;
    }
}

/// <summary>
/// A bucket's water landing, for the renderer and sounds (not sim state): who threw it and from
/// where, where it came down, and the cells that were burning there with how hard (<c>Doused</c>:
/// map index, fire before), so the flames can be seen to go out when the water arrives.
/// </summary>
public sealed record SplashEvent(long Tick, Entity Thrower, float3 From, int3 At, List<(int Cell, float Fire)> Doused);

/// <summary>
/// Throwing a bucket of water (<see cref="Buckets"/>): hold F to swing it back, let go to throw.
/// How far it goes is how far back it was swung when let go, and the swing comes forward again if
/// held too long: catch it at the right moment to land it on the fire you aimed at. Aimed where
/// you pointed when you lifted it, and turned as it swings: A/D (or a stick) turn it any way, and
/// the mouse points it (<see cref="AimThrowCommand"/>). Unsteady hands throw a little short or long.
/// </summary>
public sealed class ThrowSession : WorkSession
{
    /// <summary>Seconds for the swing to go back and come forward again.</summary>
    public const float SwingSeconds = 1.6f;

    public int3 From { get; set; }

    /// <summary>The cell aimed at: its distance is the mark on the swing.</summary>
    public int3 Aim { get; set; }

    /// <summary>The way it's thrown, radians; NaN until first aimed (then it's toward <see cref="Aim"/>).</summary>
    public float Facing { get; set; } = float.NaN;

    /// <summary>How fast A/D turn the aim, radians a second.</summary>
    public const float TurnPerSecond = MathF.PI * 0.75f;

    private int _turn;

    /// <summary>How far back it's swung, 0..1 (to <see cref="Buckets.MaxRange"/>).</summary>
    public float Swing { get; private set; }

    /// <summary>Thrown, this many cells out.</summary>
    public float Thrown { get; private set; } = -1f;

    private int _swingSteps;
    private bool _swinging;

    public override Skill Skill => Skill.Shooting;
    public override float Xp => 2f;
    public override string Title => "Throwing water";
    public override int3? Focus(Simulation sim) => Aim;

    /// <summary>Where it'd land if let go now.</summary>
    public int3 LandsAt
    {
        get
        {
            var distance = Thrown >= 0 ? Thrown : Distance(Swing);
            var facing = Heading;
            return new int3(From.X + (int)MathF.Round(DetMath.Cos(facing) * distance), From.Y + (int)MathF.Round(DetMath.Sin(facing) * distance), From.Z);
        }
    }

    /// <summary>The way it's thrown: as turned, or toward the cell aimed at.</summary>
    public float Heading => float.IsFinite(Facing) ? Facing : DetMath.Atan2(Aim.Y - From.Y, Aim.X - From.X);

    /// <summary>Point it at a cell (the mouse): the way, and the distance marked on the swing. Not your own cell.</summary>
    public void AimAt(int3 cell)
    {
        if (cell.X == From.X && cell.Y == From.Y)
            return;
        Aim = new int3(cell.X, cell.Y, From.Z);
        Facing = DetMath.Atan2(cell.Y - From.Y, cell.X - From.X);
    }

    protected override void Axis(int value) => _turn = value;

    /// <summary>How far the aimed cell is, as a share of the swing.</summary>
    public float AimShare => Math.Clamp(DistanceTo(Aim) / Buckets.MaxRange, 0f, 1f);

    private float DistanceTo(int3 c)
    {
        var dx = (float)(c.X - From.X);
        var dy = (float)(c.Y - From.Y);
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    private static float Distance(float swing) => 1f + swing * (Buckets.MaxRange - 1);

    public override bool StillValid(Simulation sim) => Buckets.IsFull(sim, sim.World.Get<Carrying>(Worker).Item);

    protected override void Step()
    {
        if (_turn != 0 && Thrown < 0)
        {
            // Turned on the spot; the aimed distance goes round with it.
            Facing = MathF.IEEERemainder(Heading + _turn * TurnPerSecond / SimClock.TicksPerSecond, MathF.Tau);
            var reach = DistanceTo(Aim);
            Aim = new int3(From.X + (int)MathF.Round(DetMath.Cos(Facing) * reach), From.Y + (int)MathF.Round(DetMath.Sin(Facing) * reach), From.Z);
        }
        if (Held)
        {
            _swinging = true;
            Swing = Triangle(_swingSteps++, SwingSeconds);
            return;
        }
        if (!_swinging)
            return;
        // Let go: away it goes, a touch short or long for unsteady hands.
        Thrown = Math.Clamp(Distance(Swing) + Rand.Range(-1f, 1f) * Hands.Tremor * 3f / Hands.Tolerance, 1f, Buckets.MaxRange);
        var miss = MathF.Abs(Thrown - DistanceTo(Aim));
        Sample(1f - miss / 3f);
        Done = true;
    }

    public override void Finish(Simulation sim)
    {
        var w = sim.World;
        var at = LandsAt;
        if (sim.Map.InBounds(at))
        {
            var put = Buckets.Splash(sim, at, Worker);
            sim.Messages.Add(MessageKind.Info, put > 0 ? "The water hisses on the flames." : "The water splashes down.", at);
        }
        var held = w.Get<Carrying>(Worker).Item;
        w.Get<Carrying>(Worker).Item = Entity.Null;
        sim.DestroyThing(held);
        w.Get<Carrying>(Worker).Item = sim.CreateItem(sim.Defs.Thing(Buckets.Empty), 1);
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Int(From.X); w.Int(From.Y); w.Int(From.Z);
        w.Int(Aim.X); w.Int(Aim.Y); w.Int(Aim.Z);
        w.Float(Swing);
        w.Float(Thrown);
        w.Int(_swingSteps);
        w.Bool(_swinging);
        w.Float(Facing);
        w.Int(_turn);
    }

    protected override void ReadState(SaveReader r)
    {
        From = new int3(r.Int(), r.Int(), r.Int());
        Aim = new int3(r.Int(), r.Int(), r.Int());
        Swing = Math.Clamp(r.Float(), 0f, 1f);
        Thrown = r.Float();
        _swingSteps = Math.Max(0, r.Int());
        _swinging = r.Bool();
        if (r.Version >= 35)
        {
            var facing = r.Float();
            Facing = float.IsFinite(facing) ? facing : float.NaN;
            _turn = Math.Sign(r.Int());
        }
    }
}

/// <summary>
/// The AI's firefighting with water: take a bucket from by the well, fill it, carry it to a few
/// steps from the fire, throw, and back to the well for more while the fire's still going. The
/// bucket goes back to the well at the end. Run in place of beating the flames whenever a bucket
/// and a well are to hand.
/// </summary>
public sealed class BucketJob(int3 fire, Entity bucket, Entity well) : Job
{
    /// <summary>How close to the fire a thrower stands (out of its heat), and how many trips one job makes.</summary>
    public const int Stand = 3, Trips = 4;

    /// <summary>A bucket and a well for this pawn, if there are both within reach of the fire.</summary>
    public static Job? For(Simulation sim, Entity pawn, int3 fire)
    {
        var w = sim.World;
        var from = w.Get<Position>(pawn).Cell;
        var wells = Buckets.Wells(sim);
        if (wells.Count == 0)
            return null;
        var well = wells.MinBy(e => Map.Distance(w.Get<Thing>(e).Cell, fire))!;
        if (Map.Distance(w.Get<Thing>(well).Cell, fire) > 40)
            return null;
        var bucket = Entity.Null;
        var best = int.MaxValue;
        foreach (var e in sim.AvailableItems())
        {
            if (!Buckets.IsBucket(sim, e) || sim.Reservations.IsReservedByOther(e, pawn))
                continue;
            var d = Map.Distance(from, w.Get<Thing>(e).Cell);
            if (d < best && Navigation.CanReach(sim, pawn, from, w.Get<Thing>(e).Cell, PathEnd.OnTile))
                (bucket, best) = (e, d);
        }
        return bucket.IsNull ? null : new BucketJob(fire, bucket, well);
    }

    protected override IEnumerable<JobStatus> Run()
    {
        Report = "Fetching a bucket";
        if (!Reserve(bucket)) yield break;
        foreach (var s in GoTo(Get<Thing>(bucket).Cell, PathEnd.OnTile)) yield return s;
        if (HasFailed) yield break;
        if (!Sim.IsAvailable(bucket))
        {
            Fail("the bucket's gone");
            yield break;
        }
        var held = Sim.TakeFromStack(bucket, 1);
        Get<Carrying>(Pawn).Item = held;

        var target = fire;
        for (var trip = 0; trip < Trips; trip++)
        {
            if (!Buckets.IsFull(Sim, Get<Carrying>(Pawn).Item))
            {
                Report = "Drawing water";
                foreach (var s in GoToEntity(well, PathEnd.Touch)) yield return s;
                if (HasFailed) yield break;
                foreach (var s in Wait(60)) yield return s;
                if (!Buckets.Fill(Sim, Pawn))
                    break;
            }
            // The fire as it is now: the cell aimed at may be out, the next one over still going.
            if (Nearest(target) is not { } burning)
                break;
            target = burning;
            Report = "Throwing water on the fire";
            if (Map.Distance(PawnCell, target) > Stand && StandFor(target) is { } spot)
            {
                foreach (var s in GoTo(spot, PathEnd.OnTile)) yield return s;
                if (HasFailed) yield break;
            }
            foreach (var s in Wait(45)) yield return s; // the swing
            Buckets.Splash(Sim, target, Pawn);
            var full = Get<Carrying>(Pawn).Item;
            Get<Carrying>(Pawn).Item = Entity.Null;
            Sim.DestroyThing(full);
            Get<Carrying>(Pawn).Item = Sim.CreateItem(Sim.Defs.Thing(Buckets.Empty), 1);
        }

        // Back to the well with it, for whoever's next.
        Report = "Taking the bucket back";
        foreach (var s in GoToEntity(well, PathEnd.Touch)) yield return s;
        var carried = Get<Carrying>(Pawn).Item;
        Get<Carrying>(Pawn).Item = Entity.Null;
        if (Alive(carried))
            Sim.PlaceItemNear(carried, PawnCell);
    }

    /// <summary>Somewhere to throw from: two or three steps off the fire, not burning, the nearest to the pawn that it can reach.</summary>
    private int3? StandFor(int3 target)
    {
        var map = Sim.Map;
        var from = PawnCell;
        int3? best = null;
        var bestD = int.MaxValue;
        foreach (var c in Sim.SpiralFrom(target, Stand))
        {
            if (!map.InBounds(c) || c.Z != target.Z || Map.Distance(c, target) < 2 || !map.Passable(c) || map.Fire[map.Idx(c)] > 0)
                continue;
            var d = Map.Distance(from, c);
            if (d < bestD && Sim.Regions.CanReach(from, c, PathEnd.OnTile))
                (best, bestD) = (c, d);
        }
        return best;
    }

    /// <summary>A burning cell near the one aimed at (within the splash's spread of it), the nearest first.</summary>
    private int3? Nearest(int3 around)
    {
        var best = -1;
        var bestD = int.MaxValue;
        foreach (var i in Sim.Fire.BurningTiles)
        {
            var d = Map.Distance(Sim.Map.CellOf(i), around);
            // Ties to the lower cell index: the set's order mustn't decide anything.
            if (d <= 6 && (d < bestD || (d == bestD && i < best)) && (Sim.Map.Designations[i] & Designation.Burn) == 0)
                (best, bestD) = (i, d);
        }
        return best < 0 ? null : Sim.Map.CellOf(best);
    }
}

/// <summary>Point the bucket you're swinging at a cell (where the mouse is): it turns that way, and the mark moves to that distance.</summary>
public sealed record AimThrowCommand(Entity Avatar, int3 Cell) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (sim.World.IsAlive(Avatar) && sim.World.TryGet<Work.WorkSession>(Avatar, out var s) && s is ThrowSession throwing && !throwing.Done
            && sim.Map.InBounds(Cell))
            throwing.AimAt(Cell);
    }
}

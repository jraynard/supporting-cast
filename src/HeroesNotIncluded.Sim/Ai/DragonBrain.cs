using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Maps;

namespace HeroesNotIncluded.Sim.Ai;

/// <summary>
/// A dragon set on the town (the season's last crisis, <c>Director.DragonCrisis</c>): the state of
/// its <see cref="DragonBrain"/> rampage. First it flies over the town, breathing on roofs and
/// trees as it passes (<see cref="DragonStrafeJob"/>); then it comes down in the square and stays
/// down (<see cref="Down"/>), going for whoever's out and burning buildings when nobody is. Hurt,
/// or seeing a crowd out in the open, it comes down early.
/// </summary>
public struct Rampage
{
    /// <summary>Passes made over the town from the air.</summary>
    public int Passes;

    /// <summary>On the ground for good: no more passes, it fights where it is.</summary>
    public bool Down;

    /// <summary>Hurt it had when it last looked (summed wound damage): more since, and it's been engaged.</summary>
    public float HurtSeen;
}

/// <summary>
/// A dragon soaked by a bucket's water (<see cref="Environment.Buckets.Splash"/>): its fire's out
/// till <see cref="Until"/> (it can only bite), and soaked often enough (<see cref="DragonBrain.SoaksToDrive"/>)
/// it gives the town up. What gives a town with no party a way to see it off.
/// </summary>
public struct Doused
{
    public long Until;
    public int Times;
}

/// <summary>
/// A wild dragon. In the practice ring it makes passes: comes down within breath's reach of
/// something (whoever's out in the open, or a building), breathes, and stays on the ground a while,
/// circling anyone who closes on it and lunging in to bite (<see cref="FreeFight"/>), never far
/// from where it landed. Set on the town (<see cref="Rampage"/>), it harries the town from the air
/// first, then lands in the square and makes its stand there. That's when the town can hurt it.
/// Hurt enough, it leaves for good.
/// </summary>
public sealed class DragonBrain : IBrain
{
    /// <summary>Passes over the town from the air before it comes down in the square.</summary>
    public const int Passes = 3;

    /// <summary>How many of the town out in the open together it takes to bring it down on them, and how close together.</summary>
    public const int Crowd = 3, CrowdSpread = 3;

    /// <summary>How far it looks for a crowd from the air, and for someone to go for once it's down, in cells.</summary>
    public const int Spots = 22, Hunts = 16;

    /// <summary>How long a soaking puts its fire out, and how many soakings see it off.</summary>
    public const float DousedSeconds = 15f;
    public const int SoaksToDrive = 4;

    /// <summary>Soaked by water: its fire's out for a while, and the town's a step nearer seeing it off.</summary>
    public static void Douse(Simulation sim, Entity dragon)
    {
        var w = sim.World;
        if (!w.Has<Doused>(dragon))
            w.Add(dragon, new Doused());
        ref var d = ref w.Get<Doused>(dragon);
        d.Until = sim.Clock.Tick + (long)(DousedSeconds * SimClock.TicksPerSecond);
        d.Times++;
        if (w.Has<Inhaling>(dragon))
            w.Remove<Inhaling>(dragon); // the breath it was drawing goes up in steam
        var name = w.Get<Creature>(dragon).Name;
        sim.Messages.Add(MessageKind.Good, d.Times >= SoaksToDrive
            ? $"Soaked again, {name} shakes off the water and has had enough of this town."
            : $"The water hits {name} in a burst of steam: its fire's out, for now.", w.Get<Position>(dragon).Cell);
    }

    /// <summary>Its fire's out (<see cref="Doused"/>): it can only bite.</summary>
    public static bool FireOut(Simulation sim, Entity dragon) =>
        sim.World.TryGet<Doused>(dragon, out var d) && d.Until > sim.Clock.Tick;

    /// <summary>Pain past which it's had enough and goes.</summary>
    public const float FleePain = 0.5f;

    /// <summary>Damage from wounds past which it's had enough and goes, whatever the pain.</summary>
    public const float FleeDamage = 60f;

    public Job? Think(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        var tile = w.Get<Position>(pawn).Cell;
        if (w.Has<Leaving>(pawn) || DrivenOff(sim, pawn))
        {
            if (!w.Has<Leaving>(pawn))
                w.Add(pawn, new Leaving());
            return sim.NearestEdge(tile) is { } edge ? new LeaveMapJob(edge, "Flying off") : null;
        }
        if (w.Has<Rampage>(pawn))
            return Rampaging(sim, pawn);
        return Target(sim, pawn) is { } target ? new DragonPassJob(target) : null;
    }

    /// <summary>Set on the town: its passes from the air (one flight), then the ground.</summary>
    private static Job? Rampaging(Simulation sim, Entity pawn)
    {
        var tile = sim.World.Get<Position>(pawn).Cell;
        return sim.World.Get<Rampage>(pawn).Down
            ? new DragonPassJob(GroundTarget(sim, pawn) ?? tile, rampage: true)
            : new DragonStrafeJob();
    }

    /// <summary>
    /// Whether it should come down on the town now, and where: on whoever's hurt it, on a crowd out
    /// in the open, or (its passes made) the square. Null: another pass.
    /// </summary>
    internal static (int3 At, string Why)? Landing(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        ref var r = ref w.Get<Rampage>(pawn);
        var tile = w.Get<Position>(pawn).Cell;
        var hurt = w.TryGet<Health>(pawn, out var h) ? h.Injuries.Sum(i => i.Damage) : 0f;
        var provoked = hurt > r.HurtSeen + 0.5f;
        r.HurtSeen = hurt;
        if (provoked && Nearest(sim, tile, Spots, outdoors: true) is { } foe)
            return (foe, "turns on the ones who hurt it");
        if (Crowding(sim, tile) is { } crowd)
            return (crowd, "spots a crowd and drops on them");
        if (r.Passes >= Passes)
            return (sim.Round.Square ?? Round.RoundSystem.FindSquare(sim) ?? Target(sim, pawn) ?? tile, "comes down on the square");
        return null;
    }

    /// <summary>Down for good, and the town told.</summary>
    internal static void ComeDown(Simulation sim, Entity pawn, int3 at, string why)
    {
        sim.World.Get<Rampage>(pawn).Down = true;
        sim.Messages.Add(MessageKind.Danger, $"{sim.World.Get<Creature>(pawn).Name} {why}!", at);
    }

    /// <summary>
    /// Too tight for a dragon: inside a building (a room, its footprint) or in a doorway. Down on the
    /// town, it doesn't go in after anyone: it burns them out.
    /// </summary>
    internal static bool TooTight(Simulation sim, int3 c)
    {
        if (!sim.Map.InBounds(c) || sim.Rooms.RoomAt(c) is { Indoors: true } || !Systems.Buildings.At(sim, c).IsNull)
            return true;
        return sim.Map.Structure[sim.Map.Idx(c)] is { IsNull: false } s && sim.World.Get<Thing>(s).Def.Building is { IsDoor: true };
    }

    /// <summary>
    /// Room for a dragon to come down: open ground around the cell as well as under it, clear of any
    /// building (a burnt-out shell included), so a beast that size isn't left standing half in a house.
    /// </summary>
    internal static bool RoomToLand(Simulation sim, int3 c)
    {
        for (var dy = -1; dy <= 1; dy++)
            for (var dx = -1; dx <= 1; dx++)
            {
                var n = new int3(c.X + dx, c.Y + dy, c.Z);
                if (!sim.Map.InBounds(n) || !sim.Map.Passable(n) || !sim.Map.Standable(sim.Map.Idx(n)) || TooTight(sim, n))
                    return false;
            }
        return true;
    }

    /// <summary>A way that goes through a door or a house: not for a dragon.</summary>
    internal static bool Squeezes(Simulation sim, List<int3> path) => path.Exists(c => TooTight(sim, c));

    /// <summary>What a pass over the town breathes on: a roof, or every other pass the trees nearest one.</summary>
    internal static int3? StrafeTarget(Simulation sim, int pass)
    {
        var w = sim.World;
        var buildings = w.Query<PresetBuilding>().Where(b => w.Get<PresetBuilding>(b) is { Phase: Defs.BuildPhase.Done, Demolishing: false }).ToList();
        if (buildings.Count == 0)
            return sim.Round.Square;
        var at = w.Get<PresetBuilding>(buildings[sim.Rand.NextInt(buildings.Count)]).Anchor;
        if (pass % 2 == 1)
            foreach (var c in sim.SpiralFrom(at, 20))
                if (sim.Map.Structure[sim.Map.Idx(c)] is { IsNull: false } s && w.Get<Thing>(s).Def.IsTree)
                    return c;
        return at;
    }

    /// <summary>Once it's down: whoever of the town is out nearest, else a building near it to burn.</summary>
    private static int3? GroundTarget(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        var tile = w.Get<Position>(pawn).Cell;
        if (Nearest(sim, tile, Hunts, outdoors: true) is { } foe)
            return foe;
        // Nobody out: it burns them out.
        // One of the nearest few (not the one it's just burned: a few cells of road between), so a
        // house it can't get round to on foot doesn't hold it.
        var near = w.Query<PresetBuilding>()
            .Where(b => w.Get<PresetBuilding>(b) is { Phase: Defs.BuildPhase.Done, Demolishing: false } pb && Map.Distance(tile, pb.Anchor) >= DragonPassJob.MinHopDown)
            .Select(b => w.Get<PresetBuilding>(b).Anchor)
            .OrderBy(a => Map.Distance(tile, a)).ThenBy(a => a.X).ThenBy(a => a.Y)
            .Take(3).ToList();
        return near.Count > 0 ? near[sim.Rand.NextInt(near.Count)] : Target(sim, pawn);
    }

    /// <summary>The nearest of the town on their feet within <paramref name="range"/>, out in the open if asked.</summary>
    private static int3? Nearest(Simulation sim, int3 from, int range, bool outdoors)
    {
        var w = sim.World;
        var best = (int3?)null;
        var bestDist = int.MaxValue;
        foreach (var e in sim.Creatures())
        {
            if (w.Get<Creature>(e) is not { Dead: false, Downed: false } c || !Factions.Defends(c.Faction))
                continue;
            var at = w.Get<Position>(e).Cell;
            if (outdoors && TooTight(sim, at))
                continue;
            var d = Map.Distance(from, at);
            if (d <= range && d < bestDist)
                (best, bestDist) = (at, d);
        }
        return best;
    }

    /// <summary>Where <see cref="Crowd"/> or more of the town stand together out in the open, within sight of it; null if nowhere.</summary>
    private static int3? Crowding(Simulation sim, int3 from)
    {
        var w = sim.World;
        var out_ = new List<int3>();
        foreach (var e in sim.Creatures())
            if (w.Get<Creature>(e) is { Dead: false, Downed: false } c && Factions.Defends(c.Faction)
                && w.Get<Position>(e).Cell is var at && Map.Distance(from, at) <= Spots && !TooTight(sim, at))
                out_.Add(at);
        foreach (var a in out_)
            if (out_.Count(b => Map.Distance(a, b) <= CrowdSpread) >= Crowd)
                return a;
        return null;
    }

    /// <summary>Hurt enough that it'll give the town up.</summary>
    public static bool DrivenOff(Simulation sim, Entity dragon) =>
        (sim.World.TryGet<Health>(dragon, out var h)
            && (h.Pain > FleePain || h.Capacity(Defs.Capacity.Moving) < 0.5f || h.Injuries.Sum(i => i.Damage) > FleeDamage))
        || (sim.World.TryGet<Doused>(dragon, out var d) && d.Times >= SoaksToDrive);

    /// <summary>Somewhere to breathe on: whoever of the town is out in the open nearest, else a building.</summary>
    private static int3? Target(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        var tile = w.Get<Position>(pawn).Cell;
        var best = (int3?)null;
        var bestDist = int.MaxValue;
        foreach (var e in sim.Creatures())
        {
            var c = w.Get<Creature>(e);
            if (!Factions.Defends(c.Faction) || c.Dead)
                continue;
            var at = w.Get<Position>(e).Cell;
            if (sim.Rooms.RoomAt(at) is { Indoors: true })
                continue;
            var d = Map.Distance(tile, at);
            if (d < bestDist)
                (best, bestDist) = (at, d);
        }
        if (best != null && sim.Rand.Chance(0.6f))
            return best;
        var buildings = w.Query<PresetBuilding>().Where(b => w.Get<PresetBuilding>(b) is { Phase: Defs.BuildPhase.Done, Demolishing: false }).ToList();
        return buildings.Count > 0 ? w.Get<PresetBuilding>(buildings[sim.Rand.NextInt(buildings.Count)]).Anchor : best;
    }
}

/// <summary>
/// One pass: to the ground within breath's reach of the target, breathe on it, then stay down a
/// while, fighting freely: circling whoever comes at it near where it landed, lunging in to bite. Ends early if it's had enough.
/// On a <see cref="Rampage"/> it's already down: it moves on a shorter way, reaches further for
/// someone to fight and ranges further after them, and soon moves on to burn something else. It goes
/// on foot, through the streets, never through a door or a house (over them, if there's no other way),
/// and doesn't go after anyone indoors (<see cref="DragonBrain.TooTight"/>).
/// </summary>
public sealed class DragonPassJob(int3 target, bool rampage = false) : Job
{
    /// <summary>How long it stays down after a breath, in ticks.</summary>
    public const int GroundTicks = SimClock.TicksPerSecond * 20;

    /// <summary>Cells it moves at least between breaths once it's down on a rampage.</summary>
    public const int MinHopDown = 4;

    protected override IEnumerable<JobStatus> Run()
    {
        var range = (int)Environment.Breath.RangeOf(Sim, Pawn);
        Report = "Swooping on the town";
        var from = PawnCell;
        // Somewhere the target can be got at from on foot, or it'd sit out of reach (past the ring's
        // fence, across the river) where nobody can come at it.
        var spots = Sim.SpiralFrom(target, range - 2)
            .Where(c => Map.Distance(c, target) >= 4 && Clear(c) && Sim.Regions.CanReach(c, target, PathEnd.Touch))
            .ToList();
        // A pass is a pass: it comes down somewhere new each time, not where it already stands.
        var away = spots.FindAll(c => Map.Distance(c, from) >= (rampage ? MinHopDown : MinHop));
        if (away.Count > 0)
            spots = away;
        if (spots.Count == 0)
        {
            Fail("nowhere to come down");
            yield break;
        }
        var landing = spots[Sim.Rand.NextInt(spots.Count)];
        if (rampage)
        {
            // On foot through the streets, never through a house: a few tries for a spot it can walk to.
            List<int3>? path = null;
            for (var tries = 0; tries < 6 && spots.Count > 0 && path == null; tries++)
            {
                var i = Sim.Rand.NextInt(spots.Count);
                landing = spots[i];
                spots.RemoveAt(i);
                if (Sim.Pathfinder.FindPath(from, landing, PathEnd.OnTile) is { } walk && !DragonBrain.Squeezes(Sim, walk))
                    path = walk;
            }
            if (path == null)
            {
                Fail("no way there on foot");
                yield break;
            }
            foreach (var s in Follow(path)) yield return s;
        }
        else
            foreach (var s in GoTo(landing, PathEnd.OnTile)) yield return s;
        if (HasFailed) yield break;

        // Its fire put out by water (Doused), it goes straight to its jaws.
        if (!DragonBrain.FireOut(Sim, Pawn))
        {
            // It rears and draws breath where it can be seen (Combat.Inhaling): a moment to guard, or to dodge late.
            Report = "Drawing breath";
            var at = Sim.Clock.Tick + Blows.InhaleTicks;
            Sim.World.Add(Pawn, new Inhaling { Target = target, Start = Sim.Clock.Tick, At = at });
            // A moment more, as for any blow at a player (Blows.Grace), for their keys to arrive.
            foreach (var s in Wait(Blows.InhaleTicks + Blows.Grace(Sim))) yield return s;
            // Soaked mid-breath (DragonBrain.Douse took it), it goes up in steam.
            if (Sim.World.Has<Inhaling>(Pawn))
            {
                Sim.World.Remove<Inhaling>(Pawn);
                Report = "Breathing fire";
                CombatRules.Breathe(Sim, Pawn, target, at);
                foreach (var s in Wait(SimClock.TicksPerSecond / 3)) yield return s; // a beat, then at them
            }
        }

        Report = "Snapping at the town";
        // Its jaws and its claws, one or the other each time: it doesn't fight one way.
        var melee = CombatRules.Attacks(Sim, Pawn).Where(a => !a.Ranged).ToList();
        var attack = melee.Count > 0 ? melee[Sim.Rand.NextInt(melee.Count)] : null;
        // On the ground it fights freely (FreeFight): it circles whoever's come at it between blows
        // and lunges in when it's ready, rather than biting from its cell.
        if (attack != null)
            FreeFight.Join(Sim, Pawn);
        long next = 0;
        var breathAgain = Sim.Clock.Tick + (rampage ? BreathEveryRampage : BreathEvery);
        var idle = 0;
        var restless = rampage ? RestlessTicks / 2 : RingRestlessTicks;
        for (var t = 0; t < GroundTicks && idle < restless && !DragonBrain.DrivenOff(Sim, Pawn); t++)
        {
            var foe = attack == null ? Entity.Null : Foe(landing);
            // Nobody comes at it: it soon takes to the air again for another pass.
            idle = foe.IsNull ? idle + 1 : 0;
            if (foe.IsNull)
            {
                if (Sim.World.Has<FreeMover>(Pawn))
                    Sim.World.Get<FreeMover>(Pawn).Wish = float2.Zero;
                yield return JobStatus.Running;
                continue;
            }
            // Fire again, at someone keeping their distance, once it's drawn breath enough since.
            if (Sim.Clock.Tick >= breathAgain && CanBreatheAt(foe))
            {
                var started = Sim.Clock.Tick;
                foreach (var s in BreatheAt(foe)) yield return s;
                t += (int)(Sim.Clock.Tick - started);
                breathAgain = Sim.Clock.Tick + (rampage ? BreathEveryRampage : BreathEvery);
                Report = "Snapping at the town";
                continue;
            }
            var ready = Sim.Clock.Tick >= next;
            var dist = FreeFight.Steer(Sim, Pawn, foe, attack!, pressing: ready, backOffUntil: 0);
            // Its jaws or claws are wound up like any blow (Combat.Blows), quick and hard to dodge.
            if (ready && !Sim.World.Has<Windup>(Pawn) && FreeFight.InReach(Sim, Pawn, foe, attack!, dist)
                && Blows.Begin(Sim, Pawn, foe, attack!))
            {
                next = Sim.Clock.Tick + attack!.CooldownTicks;
                attack = melee[Sim.Rand.NextInt(melee.Count)]; // the next one, bite or claws
            }
            yield return JobStatus.Running;
        }
        // Back on the grid to take off.
        FreeFight.Leave(Sim, Pawn);
    }

    /// <summary>Least time between its breaths once it's down and fighting: in the ring, and on the town.</summary>
    public const int BreathEvery = SimClock.TicksPerSecond * 6, BreathEveryRampage = SimClock.TicksPerSecond * 8;

    /// <summary>How near (cells) someone has to be for a breath rather than a bite: too close and it uses its jaws.</summary>
    public const int BreathMin = 3;

    /// <summary>Someone it can breathe on from here: off at a distance, level with it, nothing in the way, and its fire lit.</summary>
    private bool CanBreatheAt(Entity foe)
    {
        if (DragonBrain.FireOut(Sim, Pawn) || Sim.World.Has<Windup>(Pawn))
            return false;
        var me = Sim.World.Get<Position>(Pawn);
        var them = Sim.World.Get<Position>(foe);
        var d = Map.Distance(me.Cell, them.Cell);
        return d >= BreathMin && d <= Environment.Breath.RangeOf(Sim, Pawn) - 3 && Math.Abs(me.Cell.Z - them.Cell.Z) <= 1
            && CombatRules.LineOfFire(Sim, me.Pos + new float3(0, 0, 0.5f), them.Pos + new float3(0, 0, 0.5f));
    }

    /// <summary>
    /// A breath mid-fight: it stops and rears where it's seen (<see cref="Inhaling"/>), aimed where
    /// they stood when it began, so stepping aside or guarding is the answer. Soaked meanwhile, it
    /// goes up in steam.
    /// </summary>
    private IEnumerable<JobStatus> BreatheAt(Entity foe)
    {
        Report = "Drawing breath";
        if (Sim.World.Has<FreeMover>(Pawn))
            Sim.World.Get<FreeMover>(Pawn).Wish = float2.Zero;
        var aim = Sim.World.Get<Position>(foe).Cell;
        var at = Sim.Clock.Tick + Blows.InhaleTicks;
        Sim.World.Add(Pawn, new Inhaling { Target = aim, Start = Sim.Clock.Tick, At = at });
        for (var t = 0; t < Blows.InhaleTicks + Blows.Grace(Sim); t++)
        {
            if (Sim.World.Has<FreeMover>(Pawn))
                Sim.World.Get<FreeMover>(Pawn).Wish = float2.Zero;
            yield return JobStatus.Running;
        }
        if (!Sim.World.Has<Inhaling>(Pawn))
            yield break;
        Sim.World.Remove<Inhaling>(Pawn);
        Report = "Breathing fire";
        CombatRules.Breathe(Sim, Pawn, aim, at);
    }

    /// <summary>Cells it moves at least, from one landing to the next.</summary>
    public const int MinHop = 8;

    /// <summary>How long it stays down with nobody in reach of its jaws before it takes off, in ticks (in the ring, sooner).</summary>
    public const int RestlessTicks = SimClock.TicksPerSecond * 8, RingRestlessTicks = SimClock.TicksPerSecond * 3;

    private bool Clear(int3 c) => DragonBrain.RoomToLand(Sim, c);

    /// <summary>
    /// In the ring (not on a <see cref="Rampage"/>): how far from it someone has to be for it to go
    /// for them, and from where it came down, in cells. The whole ring: it comes after you rather than
    /// wait, and the fence holds it in.
    /// </summary>
    public const int GroundReach = 14, Leash = 14;

    /// <summary>How far it reaches for someone, and ranges from where it came down, on a rampage.</summary>
    public const int RampageReach = 8, RampageLeash = 10;

    /// <summary>The nearest of the town on their feet within <see cref="GroundReach"/> of it and <see cref="Leash"/> of <paramref name="landing"/>.</summary>
    private Entity Foe(int3 landing)
    {
        var (reach, leash) = rampage ? (RampageReach, RampageLeash) : (GroundReach, Leash);
        var w = Sim.World;
        var here = PawnCell;
        var best = Entity.Null;
        var bestDist = int.MaxValue;
        foreach (var e in Sim.Creatures())
        {
            if (w.Get<Creature>(e) is not { Dead: false, Downed: false } c || !Factions.Defends(c.Faction))
                continue;
            var at = w.Get<Position>(e).Cell;
            if (rampage && DragonBrain.TooTight(Sim, at))
                continue; // gone indoors: it'll burn them out
            var d = Map.Distance(here, at);
            if (Math.Abs(at.Z - here.Z) <= 1 && d <= reach && d < bestDist && Map.Distance(landing, at) <= leash)
                (best, bestDist) = (e, d);
        }
        return best;
    }
}

/// <summary>
/// The passes over the town from the air (<see cref="Rampage"/>), in one flight: over a roof or a
/// stand of trees and on beyond it, then round to the next, never touching down. Closing on each it
/// rears in the air (<see cref="Inhaling"/>, seen like any breath) and breathes down on it as it
/// goes over. Its passes made (or hurt, or seeing a crowd), it comes down on the town for good.
/// </summary>
public sealed class DragonStrafeJob : Job
{
    /// <summary>How far past a pass's target it flies before turning for the next (it tries further, then nearer).</summary>
    private static readonly int[] Beyond = [9, 12, 6, 15];

    /// <summary>How near (across the ground, in cells) it draws breath, flying in.</summary>
    public const int DrawsAt = 9;

    protected override IEnumerable<JobStatus> Run()
    {
        Report = "Flying over the town";
        while (!DragonBrain.DrivenOff(Sim, Pawn))
        {
            if (DragonBrain.Landing(Sim, Pawn) is { } down)
            {
                DragonBrain.ComeDown(Sim, Pawn, down.At, down.Why);
                Report = "Swooping on the town";
                // Straight down onto open ground near it, not landed short and walked in.
                var spot = Sim.SpiralFrom(down.At, 10).Where(c => DragonBrain.RoomToLand(Sim, c) && Navigation.OpenAbove(Sim.Map, c))
                    .Cast<int3?>().FirstOrDefault();
                if (spot is { } open && Navigation.PlanFlight(Sim, Pawn, PawnCell, open, PathEnd.OnTile) is { } landing)
                    foreach (var s in Follow(landing)) yield return s;
                yield break;
            }
            ref var r = ref Sim.World.Get<Rampage>(Pawn);
            var pass = r.Passes++;
            if (DragonBrain.StrafeTarget(Sim, pass) is not { } target || Leg(target) is not { } leg)
            {
                r.Passes = DragonBrain.Passes; // no way over: down it comes
                continue;
            }
            foreach (var s in Over(leg, target)) yield return s;
        }
    }

    /// <summary>The way over <paramref name="target"/> and on past it, staying up.</summary>
    private List<int3>? Leg(int3 target)
    {
        var from = PawnCell;
        var dx = (float)(target.X - from.X);
        var dy = (float)(target.Y - from.Y);
        var len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 1f)
            (dx, dy, len) = (1f, 0f, 1f);
        foreach (var beyond in Beyond)
        {
            var x = Math.Clamp(target.X + (int)MathF.Round(dx / len * beyond), 1, Sim.Map.Width - 2);
            var y = Math.Clamp(target.Y + (int)MathF.Round(dy / len * beyond), 1, Sim.Map.Height - 2);
            if (Navigation.AloftRoute(Sim, Pawn, from, new int2(x, y)) is { } leg)
                return leg;
        }
        return null;
    }

    /// <summary>Fly a leg, drawing breath as it closes on the target and breathing as it goes over.</summary>
    private IEnumerable<JobStatus> Over(List<int3> leg, int3 target)
    {
        var mover = Get<Mover>(Pawn);
        mover.Path = leg;
        mover.PathIndex = 0;
        mover.StepProgress = 0;
        mover.StepFrom = PawnCell;
        long at = -1;
        var breathed = false;
        while (mover.Moving && !DragonBrain.DrivenOff(Sim, Pawn))
        {
            var here = PawnCell;
            var across = Math.Max(Math.Abs(here.X - target.X), Math.Abs(here.Y - target.Y));
            if (at < 0 && Navigation.Airborne(Sim.Map, here) && across <= DrawsAt && !DragonBrain.FireOut(Sim, Pawn))
            {
                // It rears in the air where it can be seen: a moment to get under cover, or guard.
                Report = "Drawing breath";
                at = Sim.Clock.Tick + Blows.InhaleTicks;
                Sim.World.Add(Pawn, new Inhaling { Target = target, Start = Sim.Clock.Tick, At = at });
            }
            if (at >= 0 && !breathed && Sim.Clock.Tick >= at + Blows.Grace(Sim) && Sim.World.Has<Inhaling>(Pawn))
            {
                breathed = true;
                if (Sim.World.Has<Inhaling>(Pawn))
                    Sim.World.Remove<Inhaling>(Pawn);
                Report = "Breathing fire";
                CombatRules.Breathe(Sim, Pawn, target, at);
            }
            yield return JobStatus.Running;
        }
        if (Sim.World.Has<Inhaling>(Pawn))
            Sim.World.Remove<Inhaling>(Pawn);
        Report = "Flying over the town";
    }
}

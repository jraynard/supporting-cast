using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Environment;

namespace HeroesNotIncluded.Sim.Systems;

/// <summary>
/// Moves creatures along their paths, and drops creatures left standing on nothing. Steps into
/// or out of cells that can't be stood in are airborne: allowed for flyers through open air,
/// at their flying speed.
/// </summary>
public sealed class MovementSystem : ISimSystem
{
    public string Name => "Movement";
    public int Interval => 1;

    /// <summary>Health lost per level fallen beyond the first.</summary>
    public float FallDamagePerLevel { get; set; } = 8f;

    public void Update(Simulation sim)
    {
        foreach (var e in sim.Creatures())
            Step(sim, e);
    }

    /// <summary>
    /// One tick of movement for one creature.
    /// </summary>
    public void Step(Simulation sim, Entity e)
    {
        var w = sim.World;
        var map = sim.Map;
        var mover = w.Get<Mover>(e);
        if (w.TryGet<Carried>(e, out var carried))
        {
            // Carried along: hangs at the carrier's position.
            if (w.IsAlive(carried.By))
                w.Get<Position>(e) = w.Get<Position>(carried.By);
            return;
        }
        if (w.Has<Combat.FreeMover>(e))
            return; // moves itself, off the grid (Combat.FreeMoveSystem)
        if (w.Get<Creature>(e).Downed)
            mover.Stop();
        if (!mover.Moving || w.Get<Mind>(e).Asleep)
        {
            Fall(sim, e, mover);
            Unstick(sim, e);
            return;
        }

        var stage = w.Get<Creature>(e).Stage(sim.Clock.Tick);
        var speed = WalkSpeedPerTick(sim, e);

        var next = mover.Path![mover.PathIndex];
        var ni = map.Idx(next);
        var from = mover.StepFrom;
        var diagonal = from.X != next.X && from.Y != next.Y;
        // Someone climbing onto furniture (or up on it) may be in its cell: that step is on foot.
        int3? perch = w.TryGet<Perched>(e, out var perched) ? perched.Cell : null;
        var airborne = (!map.Passable(next) && next != perch) || (!map.Passable(from) && from != perch);
        float stepLength;
        if (airborne)
        {
            if (!stage.CanFly || !OpenAir(sim, ni))
            {
                // Something was built in the flight path, or the dragon can't fly any more.
                mover.Stop();
                return;
            }
            speed *= stage.FlySpeedFactor;
            stepLength = diagonal ? 1.41421356f : 1f;
        }
        else
        {
            // Something may have been built in the way; then the job will notice and re-plan.
            var cost = next == perch ? ClimbCost : map.PathCost[ni];
            if (cost <= 0)
            {
                mover.Stop();
                return;
            }
            stepLength = GroundStepLength(from, next, cost);
        }
        mover.StepProgress += speed / stepLength;

        if (mover.StepProgress >= 1f)
        {
            mover.StepProgress = 0;
            mover.StepFrom = next;
            mover.PathIndex++;
            w.Get<Position>(e) = Position.At(next);
            if (perch is { } up && next != up)
                w.Remove<Perched>(e); // stepped down
        }
        else
        {
            var a = Position.CenterOf(from);
            var b = Position.CenterOf(next);
            ref var p = ref w.Get<Position>(e);
            p.Pos = a + (b - a) * mover.StepProgress;
            p.Cell = mover.StepProgress < 0.5f ? from : next;
        }
    }

    /// <summary>
    /// Cells a creature covers per tick on foot before the ground's cost: its speed, slowed by
    /// injuries and by carrying. Public so a co-op client can predict its own avatar's walk.
    /// </summary>
    public static float WalkSpeedPerTick(Simulation sim, Entity e)
    {
        var w = sim.World;
        var c = w.Get<Creature>(e);
        var stage = c.Stage(sim.Clock.Tick);
        var speed = c.Def.MoveSpeed * stage.SpeedFactor / SimClock.TicksPerSecond;
        if (w.TryGet<Health>(e, out var health))
            speed *= Math.Max(0.15f, health.Capacity(Capacity.Moving));
        if (w.Get<Carrying>(e).HasItem)
            speed *= 0.85f;
        if (w.Has<Components.FoodPoisoned>(e))
            speed *= FoodPoisoning.MoveFactor(sim, e);
        foreach (var load in w.Each<Carried>())
            if (w.Get<Carried>(load).By == e)
            {
                speed *= ButcherSpots.CarrySpeed(sim, e, load); // a carcass is heavy (people carried to bed aren't slowed)
                break;
            }
        return speed;
    }

    /// <summary>The path cost of climbing up onto a piece of furniture: slower than a step.</summary>
    public const float ClimbCost = 3f;

    /// <summary>How long a walking step is in speed units: climbs and diagonals are longer, rough ground costs more.</summary>
    public static float GroundStepLength(int3 from, int3 next, float cost) =>
        (from.Z != next.Z ? NavGraph.ClimbFactor : from.X != next.X && from.Y != next.Y ? 1.41421356f : 1f) * cost;

    /// <summary>
    /// A creature standing still inside something solid that was built around it (a wall finished
    /// while it slept, say) is nudged to the nearest free cell, so it's never sealed in.
    /// </summary>
    private static void Unstick(Simulation sim, Entity e)
    {
        var map = sim.Map;
        var cell = sim.World.Get<Position>(e).Cell;
        if (!map.InBounds(cell) || map.Passable(cell) || !map.Standable(map.Idx(cell)))
            return; // fine, or airborne / falling (handled elsewhere)
        if (sim.World.TryGet<Perched>(e, out var perched) && perched.Cell == cell)
            return; // up on a table on purpose
        foreach (var c in sim.SpiralFrom(cell, 3))
            if (map.Passable(c))
            {
                sim.World.Get<Position>(e) = Position.At(c);
                return;
            }
    }

    /// <summary>A cell a flyer can pass through: not rock, and no wall, boulder or other blocking structure.</summary>
    public static bool OpenAir(Simulation sim, int i)
    {
        var map = sim.Map;
        if (map.IsSolid(i))
            return false;
        var s = map.Structure[i];
        return s.IsNull || !sim.World.Get<Thing>(s).Def.BlocksMovement || sim.World.Has<Construction>(s);
    }

    /// <summary>
    /// A creature whose ground was dug, burned or knocked away drops to the next standable cell.
    /// Flyers that stopped mid-air (their job ended) glide down instead.
    /// </summary>
    private void Fall(Simulation sim, Entity e, Mover mover)
    {
        var map = sim.Map;
        var cell = sim.World.Get<Position>(e).Cell;
        if (!map.InBounds(cell) || map.Standable(map.Idx(cell)) || map.IsSolid(map.Idx(cell)))
            return;
        var canFly = Navigation.FlyingStage(sim, e) != null;
        var z = cell.Z;
        while (z > 0 && !map.Standable(map.Idx(cell.X, cell.Y, z)) && !map.IsSolid(map.Idx(cell.X, cell.Y, z - 1)))
            z--;
        var landed = new int3(cell.X, cell.Y, z);
        mover.Stop();
        sim.World.Get<Position>(e) = Position.At(landed);
        var levels = cell.Z - z;
        if (levels > 1 && !canFly)
        {
            var c = sim.World.Get<Creature>(e);
            sim.Messages.Add(MessageKind.Warning, $"{c.Name} fell {levels} levels.", landed);
            var part = sim.World.TryGet<Health>(e, out var h) ? HealthSystem.PartAt(sim, h, PartHeight.Low) : -1;
            HealthSystem.Damage(sim, e, FallDamagePerLevel * (levels - 1), DamageType.Blunt, part);
        }
    }
}

/// <summary>Runs creature brains and their current jobs.</summary>
public sealed class AiSystem : ISimSystem
{
    public string Name => "AI";
    public int Interval => 1;

    private readonly IBrain _human = new HumanBrain();
    private readonly IBrain _raider = new RaiderBrain();
    private readonly IBrain _prisoner = new PrisonerBrain();
    private readonly IBrain _animal = new AnimalBrain();
    private readonly IBrain _bedridden = new BedriddenBrain();
    private readonly IBrain _adventurer = new Adventurers.AdventurerBrain();
    private readonly IBrain _dragon = new DragonBrain();

    /// <summary>Most recent job failure per creature, for the inspector/debugging.</summary>
    public Dictionary<Entity, string> LastFailure { get; } = [];

    public void Update(Simulation sim)
    {
        var w = sim.World;
        var now = sim.Clock.Tick;
        foreach (var e in sim.Creatures())
        {
            var mind = w.Get<Mind>(e);
            if (w.Get<Creature>(e).Downed || w.Has<Carried>(e))
                continue;
            if (mind.Drafted)
            {
                continue; // waits for orders
            }
            // The player's avatar runs only the jobs its commands start; its brain never chooses.
            if (w.Has<Avatar>(e))
            {
                if (mind.Job != null)
                    RunJob(sim, e, mind, now);
                continue;
            }
            var creature = w.Get<Creature>(e);
            // Raiders, and anyone else on their way out (released prisoners, herds moving on), think like raiders.
            // Adventurers keep their own counsel, leaving included.
            var brain = creature.Faction == Faction.Adventurer ? _adventurer
                : creature.Def.Race == Race.Dragon && Factions.Attacker(creature.Faction) ? _dragon
                : creature.Faction is Faction.Raider or Faction.Predator || w.Has<Combat.Leaving>(e) ? _raider
                : creature.Faction == Faction.Prisoner ? _prisoner
                : creature.Def.Race == Race.Animal ? _animal
                : _human;

            // Emergencies (standing in fire, dangerous temperature) override any job but firefighting.
            if (mind.Job != null && mind.Job is not FightFireJob && (now + e.Index) % 60 == 0 && InDanger(sim, e))
            {
                EndJob(sim, e, mind, false);
                mind.NextThinkTick = now;
            }

            // Let idle activities be replaced when something important comes up.
            if (mind.Job is { Interruptible: true } && (now + e.Index) % 120 == 0)
            {
                var better = brain.Think(sim, e);
                if (better is { Interruptible: false })
                {
                    EndJob(sim, e, mind, false);
                    StartJob(sim, e, mind, better);
                }
            }

            if (mind.Job == null)
            {
                if (now < mind.NextThinkTick)
                    continue;
                var job = brain.Think(sim, e);
                if (job == null)
                {
                    mind.NextThinkTick = now + 60;
                    continue;
                }
                StartJob(sim, e, mind, job);
            }

            RunJob(sim, e, mind, now);
        }
    }

    private void RunJob(Simulation sim, Entity e, Mind mind, long now)
    {
        var status = mind.Job!.Tick();
        if (status == JobStatus.Running)
            return;
        if (status == JobStatus.Failed)
        {
            LastFailure[e] = $"{mind.Job.GetType().Name}: {mind.Job.FailReason}";
            mind.NextThinkTick = now + 30;
        }
        EndJob(sim, e, mind, status == JobStatus.Succeeded);
    }

    private static bool InDanger(Simulation sim, Entity e)
    {
        var tile = sim.World.Get<Position>(e).Cell;
        var def = sim.World.Get<Creature>(e).Def;
        var fireproof = def.SafeTempMax >= 300; // dragons: flames are no emergency
        if (!fireproof && sim.Map.Fire[sim.Map.Idx(tile)] > 0)
            return true;
        // Sleepers wake to the smell of smoke.
        if (!fireproof && sim.World.Get<Mind>(e).Asleep)
            foreach (var i in sim.Fire.BurningTiles)
            {
                var f = sim.Map.CellOf(i);
                if (Math.Abs(f.X - tile.X) <= 8 && Math.Abs(f.Y - tile.Y) <= 8 && Math.Abs(f.Z - tile.Z) <= 2)
                    return true;
            }
        // Charged by an animal: get out of the way, unless it's what they're hunting.
        if (def.Race == Race.Human && sim.World.Get<Mind>(e).Job is not (HuntJob or FleeJob) && AnimalBrain.Charger(sim, e) != null)
            return true;
        // Raiders or predators near: drop what they're doing to fight or get away (Ai/Defence).
        var faction = sim.World.Get<Creature>(e).Faction;
        if (def.Race == Race.Human && faction is Faction.Village or Faction.Adventurer && !Defence.Responding(sim.World.Get<Mind>(e).Job)
            && Defence.Threat(sim, e, faction == Faction.Adventurer ? Defence.AdventurerRange : Defence.VillagerRange) != null)
            return true;
        var t = Warmth.FeltAt(sim, tile);
        var tol = sim.World.Get<Creature>(e).Tolerance(sim.Clock.Tick);
        // A meal is quick: a hungry creature braves the cold for it (it warms up after).
        if (t < tol.SafeMin)
            return sim.World.Get<Mind>(e).Job is not EatJob;
        return t > tol.SafeMax;
    }

    /// <summary>A player order: drop whatever the creature is doing and do this instead.</summary>
    public static void Order(Simulation sim, Entity e, Job job)
    {
        var mind = sim.World.Get<Mind>(e);
        EndJob(sim, e, mind, false);
        StartJob(sim, e, mind, job);
    }

    private static void StartJob(Simulation sim, Entity e, Mind mind, Job job)
    {
        job.Start(sim, e);
        mind.Job = job;
    }

    /// <summary>End the creature's job (also used for interruptions), dropping anything still carried.</summary>
    public static void EndJob(Simulation sim, Entity e, Mind mind, bool succeeded)
    {
        mind.Job?.End(succeeded);
        mind.Job = null;
        ReleasePawn(sim, e);
    }

    /// <summary>
    /// What ending any job leaves behind in the world: the creature wakes up, puts down whoever it
    /// was carrying where it stands, and drops what it held. Loading a save runs this for every
    /// creature that was busy (jobs aren't saved), so a load matches interrupting everyone.
    /// </summary>
    public static void ReleasePawn(Simulation sim, Entity e)
    {
        var w = sim.World;
        w.Get<Mind>(e).Asleep = false;
        Combat.Engagement.Release(sim, e); // their turn at whoever they were fighting
        if (!w.Has<Avatar>(e))
            Combat.Blows.Cancel(sim, e); // a swing the job was behind (a player's lands though they've let go of F)
        Combat.FreeFight.Leave(sim, e); // an AI that fought freely walks the grid again

        var carried = Entity.Null;
        foreach (var other in w.Each<Carried>())
            if (w.Get<Carried>(other).By == e)
            {
                carried = other;
                break;
            }
        // The avatar keeps who it carries in its arms (someone downed, a carcass) until it sets them down.
        if (!carried.IsNull && !w.Has<Avatar>(e))
        {
            w.Remove<Carried>(carried);
            w.Get<Position>(carried) = Position.At(w.Get<Position>(e).Cell);
        }

        // The avatar keeps what it holds until the player puts it down; a customer, the
        // drink or the meal they're sat with (Hospitality.Table).
        ref var carrying = ref w.Get<Carrying>(e);
        if (carrying.HasItem && !w.Has<Avatar>(e) && !(w.TryGet<Hospitality.Savouring>(e, out var savouring) && savouring.Item == carrying.Item))
        {
            var item = carrying.Item;
            carrying.Item = Entity.Null;
            if (w.IsAlive(item))
                sim.PlaceItemNear(item, w.Get<Position>(e).Cell);
        }
        // A companion's pack only ever holds a work load: set it down like what's in hand.
        var pack = Inventory.WorkPack(sim, e);
        if (!pack.IsNull)
            foreach (var item in sim.StoredIn(pack))
                if (!Inventory.KeptBetweenJobs(sim, item))
                    sim.PlaceItemNear(sim.TakeFromStack(item, w.Get<Stack>(item).Count), w.Get<Position>(e).Cell);
    }

    /// <summary>
    /// Interrupt every creature's job and stop everyone moving: the state a loaded save starts
    /// from. Tests compare against it; nothing in the game needs it.
    /// </summary>
    public static void InterruptAll(Simulation sim)
    {
        foreach (var e in sim.World.Query<Mind>())
        {
            var mind = sim.World.Get<Mind>(e);
            if (mind.Job != null)
                EndJob(sim, e, mind, false);
            if (sim.World.TryGet<Mover>(e, out var mover))
                mover.Stop();
        }
    }
}

/// <summary>
/// Needs decay, sleep recovery, starvation, exposure damage, natural healing and death.
/// </summary>
public sealed class NeedsSystem : ISimSystem
{
    public string Name => "Needs";
    public int Interval => 30;

    private const float UpdatesPerDay = SimClock.TicksPerDay / 30f;

    public void Update(Simulation sim)
    {
        var w = sim.World;
        var rest = sim.Defs.Need("Rest");
        var food = sim.Defs.Need("Food");
        var fires = Warmth.Sources(sim);
        foreach (var e in sim.Creatures())
        {
            ref var c = ref w.Get<Creature>(e);
            var needs = w.Get<Needs>(e);
            var mind = w.Get<Mind>(e);
            var temp = Warmth.FeltAt(sim, w.Get<Position>(e).Cell, fires);
            var tol = c.Tolerance(sim.Clock.Tick);

            foreach (var rate in c.Def.Needs)
            {
                var def = rate.NeedDef;
                var decay = rate.DecayPerDay / UpdatesPerDay;
                if (def == rest && (mind.Asleep || c.Downed))
                    decay = -3.2f * ColdSleep(temp, tol) / UpdatesPerDay; // ~7.5 hours for a full night
                needs.Add(def, -decay);
                if (needs.Get(def) <= 0 && def.DamagePerDayAtZero > 0)
                    c.Hp -= def.DamagePerDayAtZero * c.Def.MaxHp / UpdatesPerDay;
            }

            // Exposure: damage grows with how far past the safe range we are.
            var exposure = temp < tol.SafeMin ? tol.SafeMin - temp : temp > tol.SafeMax ? temp - tol.SafeMax : 0f;
            if (exposure > 0)
            {
                c.Hp -= exposure * 0.02f * c.Def.MaxHp / 100f;
                var cold = temp < tol.SafeMin;
                if (exposure > 4f)
                    sim.Messages.AddThrottled(
                    $"exposure:{e.Index}",
                    SimClock.TicksPerHour * 3,
                    MessageKind.Warning,
                    cold ? $"{c.Name} is freezing ({temp:F0}°C)." : $"{c.Name} is overheating ({temp:F0}°C).",
                    w.Get<Position>(e).Cell
                );
            }
            else if (needs.Get(food) > 0.1f)
            {
                c.Hp = Math.Min(c.Def.MaxHp, c.Hp + 12f * c.Def.MaxHp / 100f / UpdatesPerDay);
            }

            if (c.Hp <= 0)
                Kill(sim, e, ref c);
        }
    }

    /// <summary>
    /// How fast sleep restores rest at a felt temperature: fully when comfortable, slower the
    /// colder it is, down to <see cref="ColdSleepFloor"/> at the edge of what's safe.
    /// </summary>
    public static float ColdSleep(float temp, TempTolerance tol)
    {
        if (temp >= tol.ComfortMin)
            return 1f;
        var span = Math.Max(1f, tol.ComfortMin - tol.SafeMin);
        return Math.Max(ColdSleepFloor, 1f - (1f - ColdSleepFloor) * (tol.ComfortMin - temp) / span);
    }

    /// <summary>Sleep's pace at a creature's safe minimum (and below): a cold night still rests you, slowly.</summary>
    public const float ColdSleepFloor = 0.5f;

    public static void Kill(Simulation sim, Entity e, ref Creature c, string cause = "died")
    {
        if (c.Dead)
            return;
        c.Dead = true;
        c.Downed = false;
        c.Hp = 0;
        AiSystem.EndJob(sim, e, sim.World.Get<Mind>(e), false);
        sim.World.Get<Mover>(e).Stop();
        sim.DropGear(e);
        MoodSystem.OnDeath(sim, e);
        if (c.Def.Animal != null && !sim.World.Has<Rot>(e))
            sim.World.Add(e, new Rot()); // a carcass to butcher before it goes off
        sim.Messages.Add(c.Faction == Faction.Village ? MessageKind.Danger : MessageKind.Info, $"{c.Name} has {cause}.", sim.World.Get<Position>(e).Cell);
    }
}

/// <summary>Regrowth for harvestable plants that aren't destroyed (berry bushes).</summary>
public sealed class PlantSystem : ISimSystem
{
    public string Name => "Plants";
    public int Interval => 60;

    public void Update(Simulation sim)
    {
        foreach (var e in sim.World.Query<PlantState>())
        {
            ref var p = ref sim.World.Get<PlantState>(e);
            if (p.RegrowTicksLeft > 0)
                p.RegrowTicksLeft -= Interval;
        }
    }
}

/// <summary>Fueled buildings burn through their fuel and go cold when it runs out.</summary>
public sealed class FuelSystem : ISimSystem
{
    public string Name => "Fuel";
    public int Interval => 60;

    private const float UpdatesPerDay = SimClock.TicksPerDay / 60f;

    public void Update(Simulation sim)
    {
        foreach (var e in sim.World.Query<Fueled>())
        {
            var thing = sim.World.Get<Thing>(e);
            if (!thing.Spawned)
                continue;
            ref var fueled = ref sim.World.Get<Fueled>(e);
            if (!sim.Burning(e))
                continue;
            var fuelProps = thing.Def.Building!.Fuel!;
            fueled.Fuel = Math.Max(0, fueled.Fuel - fuelProps.BurnPerDay * fueled.Burn(fuelProps).BurnFactor / UpdatesPerDay);
            if (fueled.Fuel <= 0)
                sim.Messages.Add(MessageKind.Warning, $"The {thing.Def.Label} has burned out.", thing.Cell);
        }
    }
}

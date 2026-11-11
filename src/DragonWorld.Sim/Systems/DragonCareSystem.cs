using DragonWorld.Sim.Components;
using DragonWorld.Sim.Core;
using DragonWorld.Sim.Defs;

namespace DragonWorld.Sim.Systems;

/// <summary>
/// A kept dragon's side of the bond (docs/plans/dragon-interactions.md): now and then it asks for
/// something (<see cref="DragonCare.Want"/>), which its keeper has a couple of hours awake to answer
/// before it counts as ignored; sleeping beside it counts as care; and long neglect turns into a
/// sulk (no work, no riding, until it's won back) and, for a dragon that never trusted its keeper,
/// leaving for good. Wants roll from <see cref="Rand"/>, a stream of its own, so they never shift
/// <see cref="Simulation.Rand"/>.
/// </summary>
public sealed class DragonCareSystem(ulong seed) : ISimSystem, Saving.ISaveState
{
    public string Name => "DragonCare";
    public int Interval => 120;

    private const float UpdatesPerHour = SimClock.TicksPerHour / 120f;

    /// <summary>How long a <see cref="CareEvent"/> stays in <see cref="Recent"/>.</summary>
    public const int KeepTicks = SimClock.TicksPerHour * 6;

    /// <summary>How near (cells, a level either way) sleeping beside it counts.</summary>
    public const int RestReach = 2;

    public SimRandom Rand { get; } = new(seed ^ 0xD4A6_0C4Eu);

    /// <summary>Recent care, oldest first (render-only: hearts, the inspector; not saved or hashed).</summary>
    public List<CareEvent> Recent { get; } = [];

    public void Update(Simulation sim)
    {
        var now = sim.Clock.Tick;
        Recent.RemoveAll(ev => now - ev.Tick > KeepTicks);
        var w = sim.World;
        var bond = sim.Defs.Need("Bond");
        foreach (var e in w.Query<Dragon>())
        {
            var c = w.Get<Creature>(e);
            if (c.Dead)
                continue;
            var care = Bonding.Care(sim, e);
            if (care.StayAt != null && now >= care.StayUntil)
                care.StayAt = null;
            var keeper = w.Get<Dragon>(e).Keeper;
            if (!w.IsAlive(keeper) || w.Get<Creature>(keeper).Dead || w.Has<Chained>(e))
            {
                care.Want = DragonWant.None;
                continue;
            }
            var stage = c.Stage(now);
            var asleep = w.Get<Mind>(e).Asleep;

            if (asleep)
                RestTogether(sim, e, keeper);
            Wants(sim, e, keeper, care, stage, asleep);
            Neglect(sim, e, keeper, care, stage, w.Get<Needs>(e).Get(bond));
        }
    }

    /// <summary>Both asleep within <see cref="RestReach"/>: once a night, it counts as care.</summary>
    private static void RestTogether(Simulation sim, Entity dragon, Entity keeper)
    {
        var w = sim.World;
        if (!w.TryGet<Mind>(keeper, out var km) || !km.Asleep)
            return;
        var a = sim.LocationOf(dragon);
        var b = sim.LocationOf(keeper);
        if (Math.Abs(a.Z - b.Z) > 1 || Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y)) > RestReach)
            return;
        var def = sim.Defs.AllDragonInteractions.Find(d => d.Kind == CareKind.Rest);
        if (def != null && Bonding.Novelty(sim, dragon, def) >= 1f)
            Bonding.Interact(sim, keeper, dragon, def);
    }

    private void Wants(Simulation sim, Entity dragon, Entity keeper, DragonCare care, LifeStage stage, bool asleep)
    {
        var w = sim.World;
        var now = sim.Clock.Tick;
        var c = w.Get<Creature>(dragon);
        var busy = asleep || !c.Able || sim.Combat.Involves(dragon) || w.Has<Mount>(dragon);
        if (care.Want != DragonWant.None)
        {
            if (busy)
            {
                care.WantSince += Interval; // the wait only counts while it's awake and about
                return;
            }
            if (care.Want == DragonWant.Food && w.Get<Needs>(dragon).Get(sim.Defs.Need("Food")) >= 0.9f)
            {
                care.Want = DragonWant.None; // it ate (a trough, a hunt): nothing to answer
                return;
            }
            if (now - care.WantSince > Bonding.WantWindowTicks)
                Ignored(sim, dragon, keeper, care);
            return;
        }
        if (busy || stage.WantEveryHours <= 0 || !Rand.Chance(1f / (stage.WantEveryHours * UpdatesPerHour)))
            return;
        var want = Pick(sim, dragon, care, stage);
        if (want == DragonWant.None)
            return;
        care.Want = want;
        care.WantSince = now;
        if (Bonding.PlayersDragon(sim, dragon))
            sim.Messages.Add(MessageKind.Info, $"{c.Name} {Bonding.Describe(want)}.", w.Get<Position>(dragon).Cell);
    }

    /// <summary>What it asks for, by how it is: hungry, upset, itchy, or just wanting attention.</summary>
    private DragonWant Pick(Simulation sim, Entity dragon, DragonCare care, LifeStage stage)
    {
        var w = sim.World;
        var food = sim.Defs.Need("Food");
        var options = new List<(DragonWant Want, float Weight)>();
        void Add(DragonWant want, float weight)
        {
            // Only what the keeper could answer with these defs.
            if (Bonding.Answering(sim, want) is { } def && def.Suits(stage))
                options.Add((want, weight));
        }
        if (w.Get<Needs>(dragon).Get(food) < food.SeekBelow)
            Add(DragonWant.Food, 3f);
        if (Bonding.Upset(sim, dragon))
            Add(DragonWant.Comfort, 3f);
        if (sim.Clock.Tick < care.ItchyUntil)
            Add(DragonWant.Groom, 2f);
        if (stage.CanBeRidden)
            Add(DragonWant.Treasure, 0.3f);
        Add(DragonWant.Play, 1.5f);
        Add(DragonWant.Scratch, 1f);
        if (options.Count == 0)
            return DragonWant.None;
        var roll = Rand.Range(0f, options.Sum(o => o.Weight));
        foreach (var (want, weight) in options)
            if ((roll -= weight) <= 0)
                return want;
        return options[^1].Want;
    }

    private static void Ignored(Simulation sim, Entity dragon, Entity keeper, DragonCare care)
    {
        var w = sim.World;
        var want = care.Want;
        care.Want = DragonWant.None;
        ref var d = ref w.Get<Dragon>(dragon);
        d.Affinity = Math.Max(0f, d.Affinity - Bonding.IgnoredAffinity);
        MoodSystem.Remember(sim, dragon, sim.Defs.Thoughts.IgnoredMe, keeper);
        if (Bonding.PlayersDragon(sim, dragon))
            sim.Messages.Add(MessageKind.Warning, $"{w.Get<Creature>(dragon).Name} gave up waiting: it {Bonding.Describe(want)}, and nobody came.",
                w.Get<Position>(dragon).Cell);
    }

    /// <summary>Long neglect becomes a sulk; a long sulk from a dragon that never trusted its keeper ends with it leaving.</summary>
    private static void Neglect(Simulation sim, Entity dragon, Entity keeper, DragonCare care, LifeStage stage, float bond)
    {
        var w = sim.World;
        var c = w.Get<Creature>(dragon);
        var at = w.Get<Position>(dragon).Cell;
        if (bond < 0.1f)
            care.NeglectHours += 1f / UpdatesPerHour;
        else if (bond > 0.3f)
            care.NeglectHours = 0;

        if (!care.Sulking)
        {
            if (care.NeglectHours < Bonding.SulkAfterHours)
                return;
            care.Sulking = true;
            care.SulkHours = 0;
            care.StayAt = null;
            sim.Messages.Add(MessageKind.Warning,
                $"{c.Name} is sulking: it won't work or be ridden until {w.Get<Creature>(keeper).Name} wins it back with some attention.", at);
            return;
        }
        care.SulkHours += 1f / UpdatesPerHour;
        if (bond >= Bonding.ForgivenBond)
        {
            care.Sulking = false;
            care.SulkHours = care.NeglectHours = 0;
            sim.Messages.Add(MessageKind.Good, $"{c.Name} has forgiven {w.Get<Creature>(keeper).Name}.", at);
            return;
        }
        // Only a dragon that can fly off, and never came to trust its keeper, actually leaves.
        if (care.SulkHours < Bonding.LeaveAfterHours || !stage.CanFly || Bonding.Reached(sim, dragon, Bonding.Accepting))
            return;
        Leave(sim, dragon, keeper);
    }

    /// <summary>The dragon gives up on its keeper and leaves the map for good.</summary>
    public static void Leave(Simulation sim, Entity dragon, Entity keeper)
    {
        var w = sim.World;
        var c = w.Get<Creature>(dragon);
        ref var d = ref w.Get<Dragon>(dragon);
        d.Keeper = Entity.Null;
        if (w.IsAlive(keeper) && w.Has<Keeper>(keeper))
            w.Remove<Keeper>(keeper);
        var care = Bonding.Care(sim, dragon);
        care.Sulking = false;
        care.Want = DragonWant.None;
        MoodSystem.Remember(sim, keeper, sim.Defs.Thoughts.MyDragonLeft, dragon);
        if (!w.Has<Combat.Leaving>(dragon))
            w.Add(dragon, new Combat.Leaving());
        if (w.TryGet<Mind>(dragon, out var mind) && mind.Job != null)
            AiSystem.EndJob(sim, dragon, mind, false); // the brain sends it off next
        sim.Messages.Add(MessageKind.Danger,
            $"{c.Name} has had enough of being neglected, and is leaving {w.Get<Creature>(keeper).Name} for good.", w.Get<Position>(dragon).Cell);
    }

    public void Save(Saving.SaveWriter w, Simulation sim)
    {
        var (a, b, c, d) = Rand.State;
        w.UInt(a); w.UInt(b); w.UInt(c); w.UInt(d);
    }

    public void Load(Saving.SaveReader r, Simulation sim) => Rand.State = (r.UInt(), r.UInt(), r.UInt(), r.UInt());
}

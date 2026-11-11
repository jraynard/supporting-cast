using System.Diagnostics;
using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Combat;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Environment;
using SupportingCast.Sim.Maps;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim;

/// <summary>A system run by the simulation every <see cref="Interval"/> ticks.</summary>
public interface ISimSystem
{
    string Name { get; }

    /// <summary>Run every N ticks (1 = every tick).</summary>
    int Interval { get; }

    void Update(Simulation sim);
}

/// <summary>
/// Owns all simulation state and runs systems in a fixed order each tick.
/// Headless: nothing here knows about rendering, so the whole game can run in tests.
/// </summary>
public sealed partial class Simulation
{
    public DefDatabase Defs { get; }
    public World World { get; } = new();
    public Map Map { get; }
    public SimRandom Rand { get; }
    public SimClock Clock { get; } = new();
    public MessageLog Messages { get; } = new();

    /// <summary>
    /// When true (the default: tests, bench), an exception inside a job propagates and fails loudly.
    /// The game turns it off so one buggy job fails that job, not the whole village.
    /// </summary>
    public bool StrictJobs { get; set; } = true;
    public Regions Regions { get; }
    public Pathfinder Pathfinder { get; }
    public Reservations Reservations { get; } = new();
    public Rooms Rooms { get; }
    public Climate Climate { get; }
    public TemperatureSystem Temperature { get; } = new();
    public FireSystem Fire { get; } = new();
    public SmokeSystem Smoke { get; } = new();
    public LightingSystem Lighting { get; } = new();
    public AiSystem Ai { get; } = new();
    public MovementSystem Movement { get; } = new();

    /// <summary>Sends raids. Off until <see cref="Storyteller.SetDifficulty"/> picks a difficulty.</summary>
    public Storyteller Storyteller { get; }

    /// <summary>Herds coming and going. Off until <see cref="WildlifeSystem.Enable"/> (generated maps turn it on).</summary>
    public WildlifeSystem Wildlife { get; }

    /// <summary>The village's standing order to keep meat in store (marks hunts). Off by default.</summary>
    public LarderSystem Larder { get; } = new();

    /// <summary>Preset buildings: whether they're on, and the system that raises them phase by phase.</summary>
    public BuildingSystem Presets { get; } = new();
    public SocialSystem Social { get; }

    /// <summary>The round's lobby and deal. <see cref="Round.RoundPhase.None"/> until something opens one.</summary>
    public Round.RoundSystem Round { get; }

    /// <summary>Adventuring parties coming to town during a round.</summary>
    public Adventurers.PartySystem Parties { get; }

    /// <summary>Who wants what done, for how much (docs/DESIGN.md, "Errands are the shared spine").</summary>
    public Errands.ErrandSystem Errands { get; }

    /// <summary>What's been said aloud, and who heard it.</summary>
    public Speech.SpeechSystem Speech { get; } = new();

    /// <summary>The village's book of discovered recipes (see <see cref="RecipeBooks"/>).</summary>
    public RecipeBookSystem RecipeBook { get; } = new();

    public ulong Seed { get; }

    /// <summary>Fire breaths of the last moments, for the renderer. Not part of the sim state.</summary>
    public List<Environment.BreathEvent> RecentBreaths { get; } = [];

    /// <summary>Attacks of the last moments, for the renderer. Not part of the sim state.</summary>
    public List<ShotEvent> RecentShots { get; } = [];

    private readonly List<ISimSystem> _systems = [];
    private readonly List<Core.ISimCommand> _commands = [];

    /// <summary>
    /// Queue a player command for the start of the next tick. Commands are the way player input
    /// reaches the sim in Adventure mode (see <see cref="Core.ISimCommand"/>).
    /// </summary>
    public void Enqueue(Core.ISimCommand command) => _commands.Add(command);

    /// <summary>
    /// Apply a command right away, between ticks: the overseer's orders while paused, and every
    /// command in a lockstep session (a turn's commands land before its ticks on every peer).
    /// </summary>
    public void ApplyNow(Core.ISimCommand command)
    {
        RebuildDerivedIfNeeded();
        Apply(command);
        World.FlushDeferred();
    }

    /// <summary>
    /// Run a command. With <see cref="StrictJobs"/> off (the game) one that throws only fails
    /// itself, as a crashing job does: in co-op every peer applies it, so a throw would end the
    /// session for everyone. Exceptions are deterministic, so all peers fail it alike.
    /// </summary>
    private void Apply(Core.ISimCommand command)
    {
        if (StrictJobs)
        {
            command.Apply(this);
            return;
        }
        try
        {
            command.Apply(this);
        }
        catch (Exception ex)
        {
            var name = command.GetType().Name;
            Messages.AddThrottled($"cmdcrash:{name}", SimClock.TicksPerHour, Core.MessageKind.Warning,
                $"A {name} failed unexpectedly ({ex.Message}). This is a bug; the order was dropped.");
        }
    }
    public IReadOnlyList<ISimSystem> Systems => _systems;

    /// <summary>Wall-clock milliseconds spent in each system during the most recent tick it ran.</summary>
    public Dictionary<string, double> SystemTimings { get; } = [];

    /// <summary>Total wall-clock milliseconds per system since start (for profiling).</summary>
    public Dictionary<string, double> SystemTotals { get; } = [];

    /// <summary>
    /// The slowest run of each system since <see cref="ResetPeaks"/>: averages hide the one tick
    /// that makes a frame hitch. "Derived" is the lazy path/region/room rebuild after a structure change.
    /// </summary>
    public Dictionary<string, double> SystemPeaks { get; } = [];

    public void ResetPeaks() => SystemPeaks.Clear();

    /// <summary>How many times each system (and "Derived") has run since start, so totals can be read per run.</summary>
    public Dictionary<string, int> SystemRuns { get; } = [];

    private void Record(string name, double ms)
    {
        SystemTimings[name] = ms;
        SystemTotals[name] = SystemTotals.GetValueOrDefault(name) + ms;
        SystemRuns[name] = SystemRuns.GetValueOrDefault(name) + 1;
        if (ms > SystemPeaks.GetValueOrDefault(name))
            SystemPeaks[name] = ms;
    }

    /// <summary>Default number of Z levels.</summary>
    public const int DefaultDepth = 2;

    /// <summary>
    /// A map of air with a flat layer of the first solid block def at z = 0, so creatures can
    /// stand at z = 1 straight away. Map generators replace the blocks.
    /// </summary>
    public Simulation(DefDatabase defs, int width, int height, ulong seed, int depth = DefaultDepth)
    {
        Defs = defs;
        Seed = seed;
        Rand = new SimRandom(seed);
        Map = new Map(width, height, depth, defs.Block("Air"));
        Array.Fill(Map.Block, defs.AllBlocks.First(b => b.Solid), 0, Map.LayerSize);
        Regions = new Regions(Map) { BeforeRebuild = () => EnsurePathCosts() };
        Pathfinder = new Pathfinder(Map, Regions);
        Rooms = new Rooms(this);
        Climate = new Climate(this);
        Storyteller = new Storyteller(seed);
        Wildlife = new WildlifeSystem(seed);
        Social = new SocialSystem(seed);
        Round = new Round.RoundSystem(seed);
        Parties = new Adventurers.PartySystem(seed);
        Errands = new Errands.ErrandSystem(seed);
        Messages.Now = () => Clock.Tick;

        World.Destroying += OnEntityDestroying;
    }

    /// <summary>
    /// A simulation with the standard system pipeline. Order matters: environment first, then
    /// creature state, then decisions, then movement.
    /// </summary>
    public static Simulation CreateStandard(DefDatabase defs, int width, int height, ulong seed, int depth = DefaultDepth)
    {
        var sim = new Simulation(defs, width, height, seed, depth);
        sim.AddSystem(sim.Round)
            .AddSystem(sim.Speech)
            .AddSystem(sim.Temperature)
            .AddSystem(sim.Fire)
            .AddSystem(sim.Smoke)
            .AddSystem(sim.Lighting)
            .AddSystem(new NeedsSystem())
            .AddSystem(new HealthSystem())
            .AddSystem(new PlantSystem())
            .AddSystem(new RotSystem())
            .AddSystem(new FoodPoisoningSystem())
            .AddSystem(new FuelSystem())
            .AddSystem(sim.Parties)
            .AddSystem(sim.Errands)
            .AddSystem(new MoodSystem())
            .AddSystem(new PrisonBreakSystem())
            .AddSystem(sim.Social)
            .AddSystem(sim.RecipeBook)
            .AddSystem(sim.Storyteller)
            .AddSystem(sim.Wildlife)
            .AddSystem(sim.Larder)
            .AddSystem(sim.Presets)
            .AddSystem(new Work.WorkSystem())
            .AddSystem(new AvatarSystem())
            .AddSystem(new SettlerSystem())
            .AddSystem(sim.Ai)
            .AddSystem(sim.Movement);
        return sim;
    }

    public Simulation AddSystem(ISimSystem system)
    {
        _systems.Add(system);
        return this;
    }

    /// <summary>Advance the simulation by one tick.</summary>
    public void Tick()
    {
        Clock.Tick++;
        Pathfinder.SearchesThisTick = 0;
        var derived = Stopwatch.GetTimestamp();
        if (RebuildDerivedIfNeeded())
            Record("Derived", Stopwatch.GetElapsedTime(derived).TotalMilliseconds);

        if (_commands.Count > 0)
        {
            foreach (var command in _commands)
                Apply(command);
            _commands.Clear();
            World.FlushDeferred();
        }

        foreach (var system in _systems)
        {
            if (Clock.Tick % system.Interval != 0)
                continue;
            var start = Stopwatch.GetTimestamp();
            system.Update(this);
            Record(system.Name, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
            World.FlushDeferred();
        }
    }

    public void TickMany(int ticks)
    {
        for (var i = 0; i < ticks; i++)
            Tick();
    }

    private int _pathCostVersion = -1;

    /// <summary>
    /// Path costs (and <see cref="Map.Active"/>) for the current structure; true if they were rebuilt.
    /// Regions and rooms call it before rebuilding on demand mid-tick, so they never see stale costs.
    /// </summary>
    internal bool EnsurePathCosts()
    {
        if (_pathCostVersion == Map.StructureVersion)
            return false;
        RebuildPathCosts();
        _pathCostVersion = Map.StructureVersion;
        return true;
    }

    /// <summary>Recompute caches that depend on map structure (path costs, regions, rooms). True if anything was rebuilt.</summary>
    public bool RebuildDerivedIfNeeded()
    {
        var rebuilt = false;
        var t = Stopwatch.GetTimestamp();
        if (EnsurePathCosts())
        {
            rebuilt = true;
            Record("Derived.Paths", Stopwatch.GetElapsedTime(t).TotalMilliseconds);
        }
        t = Stopwatch.GetTimestamp();
        if (Regions.UpdateIfNeeded())
        {
            rebuilt = true;
            Record("Derived.Regions", Stopwatch.GetElapsedTime(t).TotalMilliseconds);
        }
        t = Stopwatch.GetTimestamp();
        if (Rooms.UpdateIfNeeded())
        {
            rebuilt = true;
            Record("Derived.Rooms", Stopwatch.GetElapsedTime(t).TotalMilliseconds);
        }
        return rebuilt;
    }

    private bool[] _solid = [];

    /// <summary>
    /// Climb links, roof cover, path costs and active cells, in a few tight passes (this runs after
    /// every structure change, so it's written to stay quick in Debug builds too).
    /// </summary>
    private void RebuildPathCosts()
    {
        var map = Map;
        int n = map.Count, layer = map.LayerSize;
        var block = map.Block;
        var structure = map.Structure;
        var hasFloor = map.HasFloor;
        var hasRoof = map.HasRoof;
        var climb = map.Climb;
        var covered = map.Covered;
        var cost = map.PathCost;
        var active = map.Active;
        if (_solid.Length != n)
            _solid = new bool[n];
        var solid = _solid;

        // Solids, and climb links from finished ramps/ladders (ladders make the cell above standable).
        for (var i = 0; i < n; i++)
        {
            solid[i] = block[i].Solid;
            var s = structure[i];
            climb[i] = s.IsNull || World.Has<Components.Construction>(s)
                ? ClimbKind.None
                : World.Get<Components.Thing>(s).Def.Building?.Climb ?? ClimbKind.None;
        }

        // Roof cover, top-down per column: is there rock, a floor or a roof anywhere above?
        for (var col = 0; col < layer; col++)
        {
            var seen = false;
            for (var i = col + n - layer; i >= 0; i -= layer)
            {
                covered[i] = seen;
                seen |= solid[i] || hasFloor[i] || hasRoof[i];
            }
        }

        map.ActiveCells.Clear();
        for (var i = 0; i < n; i++)
        {
            if (solid[i])
            {
                cost[i] = 0;
                active[i] = false;
                continue;
            }
            var below = i - layer;
            var standable = hasFloor[i] || (below >= 0 && (solid[below] || climb[below] == ClimbKind.Ladder));

            // Floors cover the ground (and bridge water); otherwise walk on the block below.
            var c = !standable ? 0f : hasFloor[i] || !solid[Math.Max(below, 0)] ? 1f : block[below].MoveCost;
            var s = structure[i];
            if (c > 0 && !s.IsNull && !World.Has<Components.Construction>(s))
            {
                var def = World.Get<Components.Thing>(s).Def;
                c = def.BlocksMovement || World.Has<Components.Locked>(s) ? 0 : c + def.PathCost;
            }
            cost[i] = c;

            // Headroom above stairs counts too: it's the stairwell heat and smoke rise through.
            active[i] = standable || !s.IsNull || (below >= 0 && climb[below] == ClimbKind.Ramp);
            if (active[i])
                map.ActiveCells.Add(i);
        }
    }

    /// <summary>
    /// Every bit of state a save holds, hashed (<see cref="Saving.SaveGame.Hash"/>): what
    /// <see cref="StateHash"/> leaves out (fuel, egg progress, what's carried...) counts too.
    /// </summary>
    public ulong FullHash(bool withMap = true) => Saving.SaveGame.Hash(this, withMap);

    /// <summary>
    /// A stable hash (FNV-1a) of the important sim state: map layers, orders and zones, the RNG,
    /// creatures (position, health, needs, current job), stacks and construction progress. Two
    /// runs with the same seed and inputs must produce the same hash; used by the determinism
    /// test and for desync detection. Only stable values go in (no GetHashCode).
    /// <paramref name="withMap"/> false skips the per-cell layers: a cheap check (creatures, RNGs,
    /// stacks) that lockstep peers compare every second, where a map divergence soon shows anyway.
    /// </summary>
    public ulong StateHash(bool withMap = true)
    {
        // Kept as it is: it's the fingerprint benches and refactors are checked against. FullHash
        // covers everything a save holds.
        var h = 14695981039346656037UL;
        void Mix(long v)
        {
            for (var b = 0; b < 8; b++)
            {
                h ^= (byte)(v >> (b * 8));
                h *= 1099511628211UL;
            }
        }
        void MixFloat(float f) => Mix((long)(f * 1000));
        void MixText(string text)
        {
            foreach (var c in text)
                Mix(c);
        }

        Mix(Clock.Tick);
        Mix(World.AliveCount);
        Mix((long)Rand.Fingerprint);
        if (Storyteller.Difficulty != null) // off leaves the hash as it always was
        {
            Mix(Storyteller.NextRaidTick);
            Mix(Storyteller.RaidsSent);
        }
        if (Wildlife.Fingerprint is not 0 and var wildlife) // likewise: off and unfished leaves it out
            Mix(wildlife);
        Mix((long)Social.Rand.Fingerprint);
        var designations = Map.Designations;
        var zones = Map.Zone;
        for (var i = 0; i < (withMap ? Map.Count : 0); i++)
        {
            Mix(Map.Block[i].Index);
            Mix(Map.Structure[i].Index);
            Mix(Map.Item[i].Index);
            Mix((long)(Map.Temperature[i] * 100));
            MixFloat(Map.Fire[i]);
            MixFloat(Map.Smoke[i]);
            Mix((long)designations[i] | ((long)zones[i] << 8));
        }
        foreach (var e in World.Each<Components.Rot>())
            MixFloat(World.Get<Components.Rot>(e).Progress);
        foreach (var e in World.Each<Components.Creature>())
        {
            var p = World.Get<Components.Position>(e).Pos;
            MixFloat(p.X);
            MixFloat(p.Y);
            MixFloat(p.Z);
            MixFloat(World.Get<Components.Creature>(e).Hp);
            if (World.TryGet<Components.Health>(e, out var health))
            {
                MixFloat(health.Blood);
                Mix(health.Injuries.Count);
                Mix(health.Scars.Count);
                MixFloat(health.InfectionSeverity);
            }
            if (World.TryGet<Components.Needs>(e, out var needs))
                foreach (var v in needs.Values)
                    MixFloat(v);
            if (World.TryGet<Components.Mind>(e, out var mind))
                MixText(mind.Job?.GetType().Name ?? "-");
            if (World.TryGet<Components.Skills>(e, out var skills))
                foreach (var level in skills.Level)
                    Mix(level);
            if (World.TryGet<Components.Mood>(e, out var mood))
            {
                MixFloat(mood.Value);
                foreach (var m in mood.Memories)
                {
                    Mix(m.Def.Index);
                    Mix(m.TicksLeft);
                    Mix(m.Other.Index);
                }
            }
        }
        foreach (var e in World.Each<Components.Stack>())
        {
            Mix(e.Index);
            Mix(World.Get<Components.Stack>(e).Count);
        }
        // Which container (wagon, pack) holds what, and whose back each pack is on.
        foreach (var e in World.Each<Components.Stored>())
            Mix(World.Get<Components.Stored>(e).In.Index);
        foreach (var e in World.Each<Components.Apparel>())
        {
            Mix(World.Get<Components.Apparel>(e).Back.Index);
            if (!World.Get<Components.Apparel>(e).Neck.IsNull) // only once jewelry is worn, so older states hash as they did
                Mix(World.Get<Components.Apparel>(e).Neck.Index);
        }
        foreach (var e in World.Each<Components.Construction>())
        {
            var c = World.Get<Components.Construction>(e);
            Mix(e.Index);
            MixFloat(c.WorkDone);
            foreach (var d in c.Delivered)
                Mix(d);
        }
        if (RecipeBook.VillageDiscoveries.Count > 0) // none leaves the hash as it was
            Mix(RecipeBook.VillageDiscoveries.Count);
        foreach (var e in World.Each<Components.RecipeBook>())
            Mix(World.Get<Components.RecipeBook>(e).Known.Count);
        foreach (var e in World.Each<Components.Blown>())
            Mix(World.Get<Components.Blown>(e).Until);
        foreach (var e in World.Each<Components.Workbench>())
        {
            var b = World.Get<Components.Workbench>(e);
            if (b.Bills.Count == 0 && b.Idle)
                continue; // an unused station leaves the hash as it was
            Mix(e.Index);
            Mix(b.Active?.Index ?? -1);
            MixFloat(b.Progress);
            foreach (var d in b.Staged)
                Mix(d);
            foreach (var bill in b.Bills)
            {
                Mix(bill.Recipe.Index);
                Mix(bill.Done);
            }
        }
        return h;
    }
}

using HeroesNotIncluded.Sim.Components;

namespace HeroesNotIncluded.Sim.Environment;

/// <summary>
/// Per-cell heat diffusion over the map's active cells (<see cref="Maps.Map.Active"/>). Each
/// update, every active cell exchanges heat with its 4 neighbors on the same level (scaled down by
/// insulation of walls/doors) and with active cells above and below (through floors' insulation,
/// faster upwards than downwards: warm air rises), leaks towards the outdoor temperature (much
/// more slowly when roofed), and receives heat from sources: fueled buildings, fires and
/// warm-bodied creatures. Open air next to an active cell counts as outdoor air; solid blocks
/// don't exchange. Rooms emerge naturally: walls + roof trap heat, stairwells carry it up.
/// </summary>
public sealed class TemperatureSystem : ISimSystem, Saving.ISaveState
{
    public string Name => "Temperature";
    public int Interval => 10;

    /// <summary>Updates per game hour, for converting per-hour rates.</summary>
    public const float UpdatesPerHour = SimClock.TicksPerHour / 10f;

    /// <summary>
    /// The exchange coefficients below were tuned when an hour was 150 updates. They're scaled to
    /// the hour's length so heat spreads and leaks per game hour, as fires heat: a longer hour
    /// changes nothing but how long it all takes in real time.
    /// </summary>
    public const float PerTunedUpdate = 150f / UpdatesPerHour;

    /// <summary>
    /// Neighbor exchange coefficient for open air. Explicit diffusion is stable while a cell's total
    /// outflow per update stays ≤ 1: 4 × this + this × (rise + sink factors) + ambient.
    /// </summary>
    public float Conductance { get; set; } = 0.2f;

    public float OutdoorExchange { get; set; } = 0.03f;
    public float RoofedExchange { get; set; } = 0.002f;

    /// <summary>Vertical exchange when the lower cell is warmer (rising heat), relative to <see cref="Conductance"/>.</summary>
    public float RiseFactor { get; set; } = 0.5f;

    /// <summary>Vertical exchange when the upper cell is warmer (heat sinking), relative to <see cref="Conductance"/>.</summary>
    public float SinkFactor { get; set; } = 0.15f;

    /// <summary>Heat per game hour added by an intensity-1 fire tile.</summary>
    public float FireHeatPerHour { get; set; } = 600f;

    private float[] _next = [];
    private float[] _conduct = [];

    /// <summary>Openness of the floor at the bottom of each cell: 1 = no floor, 0 = perfect insulator.</summary>
    private float[] _floorOpen = [];
    private int _builtVersion = -1;

    /// <summary>Loaded from a save made while the conductance was up to date (see <see cref="AfterLoad"/>).</summary>
    private bool _loadedFresh;

    // Rebuilding resets cells that stopped being simulated to the outdoor temperature, so it's
    // state: a load must rebuild exactly when the original game would have, no sooner.
    public void Save(Saving.SaveWriter w, Simulation sim) => w.Bool(_builtVersion == sim.Map.StructureVersion);

    public void Load(Saving.SaveReader r, Simulation sim) => _loadedFresh = r.Bool();

    public void AfterLoad(Simulation sim)
    {
        if (!_loadedFresh)
            return;
        EnsureBuffers(sim.Map);
        RebuildConductance(sim, resetInactive: false);
    }

    private void EnsureBuffers(Maps.Map map)
    {
        if (_next.Length == map.Count)
            return;
        _next = new float[map.Count];
        _conduct = new float[map.Count];
        _floorOpen = new float[map.Count];
    }

    public void Update(Simulation sim)
    {
        var map = sim.Map;
        EnsureBuffers(map);
        if (_builtVersion != map.StructureVersion)
            RebuildConductance(sim, resetInactive: true);

        // Hot loop: everything hoisted into locals and the neighbor exchange written out by hand,
        // so it stays fast in Debug builds too (the scenario tests run there).
        var outdoor = sim.Climate.OutdoorTemperature;
        var t = map.Temperature;
        var next = _next;
        var conduct = _conduct;
        var floorOpen = _floorOpen;
        var roofed = map.Roofed;
        var active = map.Active;
        int w = map.Width, h = map.Height, layer = map.LayerSize, count = map.Count;
        float k = Conductance * PerTunedUpdate, rise = k * RiseFactor, sink = k * SinkFactor;
        float roofedEx = RoofedExchange * PerTunedUpdate, outdoorEx = OutdoorExchange * PerTunedUpdate;
        var cells = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(map.ActiveCells);
        foreach (var i in cells)
        {
            var rest = i % layer;
            var x = rest % w;
            var y = rest / w;
            var ti = t[i];
            var ci = conduct[i];

            // Sideways: the map edge and open sky are outdoor air; solid ground has conductance 0.
            float sum;
            if (x + 1 < w) { var j = i + 1; sum = conduct[j] * ((active[j] ? t[j] : outdoor) - ti); }
            else sum = outdoor - ti;
            if (x > 0) { var j = i - 1; sum += conduct[j] * ((active[j] ? t[j] : outdoor) - ti); }
            else sum += outdoor - ti;
            if (y + 1 < h) { var j = i + w; sum += conduct[j] * ((active[j] ? t[j] : outdoor) - ti); }
            else sum += outdoor - ti;
            if (y > 0) { var j = i - w; sum += conduct[j] * ((active[j] ? t[j] : outdoor) - ti); }
            else sum += outdoor - ti;
            var flux = k * ci * sum;

            // Up and down, through the upper cell's floor: heat rises faster than it sinks.
            var up = i + layer;
            if (up < count && active[up])
            {
                var tu = t[up];
                flux += (ti > tu ? rise : sink) * ci * conduct[up] * floorOpen[up] * (tu - ti);
            }
            var down = i - layer;
            if (down >= 0 && active[down])
            {
                var td = t[down];
                flux -= (td > ti ? rise : sink) * conduct[down] * ci * floorOpen[i] * (ti - td);
            }

            next[i] = ti + flux + (roofed[i] ? roofedEx : outdoorEx) * (outdoor - ti);
        }

        AddHeatSources(sim);
        foreach (var i in cells)
            t[i] = next[i];
    }

    /// <summary>
    /// Per-cell "openness" from insulation of finished walls/doors (0 for solid blocks) and of
    /// floors. Only cells holding buildings need world lookups, so those are visited via the
    /// things themselves.
    /// </summary>
    private void RebuildConductance(Simulation sim, bool resetInactive)
    {
        var map = sim.Map;
        var outdoor = sim.Climate.OutdoorTemperature;
        var block = map.Block;
        var active = map.Active;
        var temps = map.Temperature;
        for (var i = 0; i < map.Count; i++)
        {
            var solid = block[i].Solid;
            _conduct[i] = solid ? 0f : 1f;
            _floorOpen[i] = 1f;
            // Cells that just stopped being simulated rejoin the outdoor air.
            if (resetInactive && !active[i] && !solid)
                temps[i] = outdoor;
        }
        foreach (var e in sim.World.Query<Thing>())
        {
            var thing = sim.World.Get<Thing>(e);
            if (!thing.Spawned || thing.Def.Building is not { } b || sim.World.Has<Construction>(e))
                continue;
            var i = map.Idx(thing.Cell);
            if (map.Floor[i] == e)
                _floorOpen[i] = 1f - thing.Insulation;
            else if (map.Structure[i] == e)
                _conduct[i] = 1f - thing.Insulation;
        }
        _builtVersion = map.StructureVersion;
    }

    private void AddHeatSources(Simulation sim)
    {
        var map = sim.Map;
        var w = sim.World;

        foreach (var e in w.Query<Fueled>())
        {
            var thing = w.Get<Thing>(e);
            if (!thing.Spawned || !sim.Burning(e))
                continue;
            var b = thing.Def.Building!;
            // The fuel decides how hot: coal runs a forge hotter than charcoal.
            var burn = b.Fuel != null ? w.Get<Fueled>(e).Burn(b.Fuel) : null;
            Heat(map.Idx(thing.Cell), b.HeatOutput * (burn?.HeatFactor ?? 1f) / UpdatesPerHour, b.MaxHeatTemp + (burn?.MaxTempBonus ?? 0f));
        }

        // Bellows a smith is pumping: more heat, a little past the fire's own cap.
        foreach (var e in w.Query<Blown>())
        {
            var thing = w.Get<Thing>(e);
            if (!thing.Spawned || w.Get<Blown>(e).Until < sim.Clock.Tick || !sim.Burning(e)
                || Systems.Crafting.BellowsOf(sim, e) is not { } bellows || thing.Def.Building is not { } b)
                continue;
            var bonus = b.Fuel != null ? w.Get<Fueled>(e).Burn(b.Fuel).MaxTempBonus : 0f;
            Heat(map.Idx(thing.Cell), bellows.Heat / UpdatesPerHour, b.MaxHeatTemp + bonus + bellows.MaxTempBonus);
        }

        foreach (var i in sim.Fire.BurningTiles)
            Heat(i, FireHeatPerHour * map.Fire[i] / UpdatesPerHour, 400f);

        foreach (var e in sim.Creatures())
        {
            var c = w.Get<Creature>(e);
            var heat = c.Stage(sim.Clock.Tick).BodyHeat;
            if (heat > 0)
                Heat(map.Idx(w.Get<Position>(e).Cell), heat / UpdatesPerHour, 45f);
        }

        void Heat(int i, float amount, float cap)
        {
            if (_next[i] < cap)
                _next[i] = Math.Min(cap, _next[i] + amount);
        }
    }
}

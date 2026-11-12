using HeroesNotIncluded.Sim.Components;

namespace HeroesNotIncluded.Sim.Environment;

/// <summary>
/// Smoke as a density per air cell (<see cref="Maps.Map.Smoke"/>), kept sparse: only smoky cells
/// are processed, in sorted order for determinism. Fires emit it; each update part of it rises
/// into the cell above (floors and rock stop it, so ceilings trap it), part spreads to thinner
/// neighbors (walls and doors stop it), and it thins out fast under the open sky and slowly
/// indoors. The same machinery can carry other gases later (poison breath).
/// </summary>
public sealed class SmokeSystem : ISimSystem, Saving.ISaveState
{
    public string Name => "Smoke";
    public int Interval => 15;

    public const float UpdatesPerHour = SimClock.TicksPerHour / 15f;

    // Rates are per game hour; Update converts them with UpdatesPerHour.

    /// <summary>Density added per game hour by an intensity-1 fire.</summary>
    public float EmissionPerHour { get; set; } = 30f;

    /// <summary>Share of a cell's smoke rising into the cell above per game hour, when there's room.</summary>
    public float RisePerHour { get; set; } = 30f;

    /// <summary>Share of the difference flowing to each thinner neighbor per game hour.</summary>
    public float SpreadPerHour { get; set; } = 6f;

    /// <summary>Share lost per game hour under the open sky, and when roofed.</summary>
    public float OutdoorDecayPerHour { get; set; } = 4f;
    public float IndoorDecayPerHour { get; set; } = 0.2f;

    /// <summary>Density that makes creatures cough (message only, for now).</summary>
    public float ChokingDensity { get; set; } = 0.6f;

    private const float MinDensity = 0.01f;

    private readonly HashSet<int> _cells = [];

    public void Save(Saving.SaveWriter w, Simulation sim) => w.Ints(_cells.Order().ToArray());

    public void Load(Saving.SaveReader r, Simulation sim)
    {
        _cells.Clear();
        _cells.UnionWith(r.Ints());
    }
    private readonly List<int> _snapshot = [];
    private readonly List<int> _touched = [];
    private float[] _delta = [];

    public IReadOnlyCollection<int> SmokyCells => _cells;

    public void Update(Simulation sim)
    {
        var map = sim.Map;
        if (_delta.Length != map.Count)
            _delta = new float[map.Count];

        _snapshot.Clear();
        _snapshot.AddRange(sim.Fire.BurningTiles);
        _snapshot.Sort();
        foreach (var i in _snapshot)
        {
            map.Smoke[i] += map.Fire[i] * EmissionPerHour / UpdatesPerHour;
            _cells.Add(i);
        }
        if (_cells.Count == 0)
            return;

        _snapshot.Clear();
        _snapshot.AddRange(_cells);
        _snapshot.Sort();
        var layer = map.LayerSize;
        foreach (var i in _snapshot)
        {
            var d = map.Smoke[i];
            if (d < MinDensity)
            {
                map.Smoke[i] = 0;
                _cells.Remove(i);
                continue;
            }

            var out_ = d * (map.Roofed[i] ? IndoorDecayPerHour : OutdoorDecayPerHour) / UpdatesPerHour;
            var rise = RisePerHour / UpdatesPerHour;
            var spread = SpreadPerHour / UpdatesPerHour;

            // Rise, unless a floor or rock is in the way. Out of the top of the map it's gone.
            var up = i + layer;
            if (up >= map.Count)
                out_ += d * rise;
            else if (!map.Ceiling(up) && !sim.Rooms.IsBoundary(up))
                Move(i, up, d * rise, ref out_);

            // Spread sideways towards thinner air.
            var c = map.CellOf(i);
            foreach (var n in Maps.Map.Neighbors4)
            {
                if (!map.InBounds(c.X + n.X, c.Y + n.Y, c.Z))
                    continue;
                var j = map.Idx(c.X + n.X, c.Y + n.Y, c.Z);
                if (sim.Rooms.IsBoundary(j) || map.Smoke[j] >= d)
                    continue;
                Move(i, j, (d - map.Smoke[j]) * spread, ref out_);
            }

            Touch(i);
            _delta[i] -= Math.Min(out_, d);
        }

        foreach (var j in _touched)
        {
            map.Smoke[j] = Math.Max(0, map.Smoke[j] + _delta[j]);
            _delta[j] = 0;
            if (map.Smoke[j] >= MinDensity)
                _cells.Add(j);
        }
        _touched.Clear();

        foreach (var e in sim.Creatures())
        {
            var cell = sim.World.Get<Position>(e).Cell;
            if (!map.InBounds(cell) || map.Smoke[map.Idx(cell)] < ChokingDensity)
                continue;
            var name = sim.World.Get<Creature>(e).Name;
            sim.Messages.AddThrottled($"smoke:{e.Index}", SimClock.TicksPerHour * 2, MessageKind.Warning, $"{name} is choking on smoke.", cell);
        }
        return;

        void Move(int from, int to, float amount, ref float outflow)
        {
            Touch(to);
            _delta[to] += amount;
            outflow += amount;
        }

        void Touch(int j)
        {
            if (_delta[j] == 0)
                _touched.Add(j);
        }
    }

    /// <summary>Clear all smoke (dev tool).</summary>
    public void Clear(Simulation sim)
    {
        foreach (var i in _cells)
            sim.Map.Smoke[i] = 0;
        _cells.Clear();
    }
}

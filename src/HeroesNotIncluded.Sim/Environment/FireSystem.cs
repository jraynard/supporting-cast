using HeroesNotIncluded.Sim.Components;

namespace HeroesNotIncluded.Sim.Environment;

/// <summary>
/// Fire lives in air cells as an intensity in 0..1. It feeds on whatever is flammable there
/// (structure, item, floor, the surface of the block below), damages it, spreads to flammable
/// neighbors and dies out when nothing is left to burn. Flames climb: spreading upwards (and
/// diagonally up hillsides) is likelier than sideways, and downwards much less likely. Pawns standing in fire get hurt unless they're fireproof.
/// </summary>
public sealed class FireSystem : ISimSystem, Saving.ISaveState
{
    public string Name => "Fire";
    public int Interval => 15;

    public const float UpdatesPerHour = SimClock.TicksPerHour / 15f;

    /// <summary>
    /// The rates below are per update, tuned when an hour was 100 updates; scaled by this so a fire
    /// grows, spreads and eats its fuel per game hour, in step with the heat and smoke it makes.
    /// </summary>
    public const float PerTunedUpdate = 100f / UpdatesPerHour;

    public float SpreadChance { get; set; } = 0.035f;
    public float GrowthRate { get; set; } = 0.06f;
    public float BurnOutRate { get; set; } = 0.1f;
    public float ThingDamage { get; set; } = 3f;
    public float CreatureDamage { get; set; } = 1.2f;

    /// <summary>Spread offsets with their chance multiplier relative to sideways spread.</summary>
    private static readonly (int3 Offset, float Factor)[] Spread =
    [
        (new(1, 0, 0), 1f), (new(-1, 0, 0), 1f), (new(0, 1, 0), 1f), (new(0, -1, 0), 1f),
        (new(0, 0, 1), 2f),
        (new(1, 0, 1), 1f), (new(-1, 0, 1), 1f), (new(0, 1, 1), 1f), (new(0, -1, 1), 1f),
        (new(0, 0, -1), 0.3f),
    ];

    private readonly HashSet<int> _burning = [];

    /// <summary>Cells soaked with water (<see cref="Soak"/>), until the tick each dries.</summary>
    private readonly Dictionary<int, long> _soaked = [];

    public void Save(Saving.SaveWriter w, Simulation sim)
    {
        w.Ints(_burning.Order().ToArray());
        w.Int(_soaked.Count);
        foreach (var (i, until) in _soaked.OrderBy(kv => kv.Key))
        {
            w.Int(i);
            w.Long(until);
        }
    }

    public void Load(Saving.SaveReader r, Simulation sim)
    {
        _burning.Clear();
        _burning.UnionWith(r.Ints());
        _soaked.Clear();
        if (r.Version >= 27)
            for (var n = r.Count(1 << 24); n > 0; n--)
            {
                var i = r.Int();
                _soaked[i] = r.Long();
            }
    }

    /// <summary>
    /// Wet a cell (water thrown, <see cref="Buckets"/>): it won't catch until it dries, and a fire
    /// in it has nothing to feed on and dies down. A longer soak wins over a shorter.
    /// </summary>
    public void Soak(Simulation sim, int i, long ticks)
    {
        var until = sim.Clock.Tick + ticks;
        if (!_soaked.TryGetValue(i, out var was) || was < until)
            _soaked[i] = until;
    }

    /// <summary>Still wet from a bucket's water (anyone can see wet ground: no secret).</summary>
    public bool Soaked(Simulation sim, int i) => _soaked.TryGetValue(i, out var until) && until > sim.Clock.Tick;

    /// <summary>Every soaked cell, for drawing.</summary>
    public IReadOnlyDictionary<int, long> SoakedCells => _soaked;

    private readonly List<int> _dried = [];

    /// <summary>Forget cells that have dried.</summary>
    private void Dry(Simulation sim)
    {
        if (_soaked.Count == 0)
            return;
        _dried.Clear();
        foreach (var (i, until) in _soaked)
            if (until <= sim.Clock.Tick)
                _dried.Add(i);
        foreach (var i in _dried)
            _soaked.Remove(i);
    }
    private readonly List<int> _snapshot = [];

    public IReadOnlyCollection<int> BurningTiles => _burning;

    /// <summary>Start or intensify a fire in a cell. Returns false if there is nothing to burn.</summary>
    public bool Ignite(Simulation sim, int3 tile, float intensity = 0.3f)
    {
        var map = sim.Map;
        if (!map.InBounds(tile))
            return false;
        var i = map.Idx(tile);
        if (Fuel(sim, i) <= 0)
            return false;
        map.Fire[i] = Math.Max(map.Fire[i], intensity);
        _burning.Add(i);
        return true;
    }

    /// <summary>How readily a cell burns: max flammability of its contents and the ground under it.</summary>
    public static float Fuel(Simulation sim, int i)
    {
        var map = sim.Map;
        if (map.IsSolid(i) || sim.Fire.Soaked(sim, i))
            return 0;
        float fuel;
        if (!map.Floor[i].IsNull)
        {
            // A floor covers whatever is underneath.
            fuel = sim.World.Get<Thing>(map.Floor[i]).Flammability;
        }
        else
        {
            var ground = map.GroundUnder(i);
            if (ground is { IsWater: true })
                return 0;
            fuel = ground?.Flammability ?? 0f;
        }
        if (!map.Structure[i].IsNull)
            fuel = Math.Max(fuel, sim.World.Get<Thing>(map.Structure[i]).Flammability);
        if (!map.Item[i].IsNull)
            fuel = Math.Max(fuel, sim.World.Get<Thing>(map.Item[i]).Flammability);
        return fuel;
    }

    public void Update(Simulation sim)
    {
        Dry(sim);
        if (_burning.Count == 0)
            return;

        var map = sim.Map;
        _snapshot.Clear();
        _snapshot.AddRange(_burning);
        _snapshot.Sort(); // deterministic order

        foreach (var i in _snapshot)
        {
            var fire = map.Fire[i];
            if (fire <= 0)
            {
                _burning.Remove(i);
                continue;
            }

            var fuel = Fuel(sim, i);
            fire = fuel > 0 ? Math.Min(1f, fire + GrowthRate * PerTunedUpdate * fuel) : fire - BurnOutRate * PerTunedUpdate;
            map.Fire[i] = Math.Max(0, fire);
            if (fire <= 0)
            {
                _burning.Remove(i);
                continue;
            }

            Consume(sim, i, fire);

            var tile = map.CellOf(i);
            foreach (var (d, factor) in Spread)
            {
                var n = new int3(tile.X + d.X, tile.Y + d.Y, tile.Z + d.Z);
                if (!map.InBounds(n))
                    continue;
                var ni = map.Idx(n);
                if (map.Fire[ni] > 0)
                    continue;
                var nf = Fuel(sim, ni);
                if (nf > 0 && sim.Rand.Chance(SpreadChance * PerTunedUpdate * factor * fire * nf))
                    Ignite(sim, n, 0.15f);
            }
        }

        HurtCreatures(sim);
        if (_burning.Count > 0)
            sim.Messages.AddThrottled("fire", SimClock.TicksPerHour * 2, MessageKind.Danger, "Fire!", map.CellOf(_snapshot[0]));
    }

    private void Consume(Simulation sim, int i, float fire)
    {
        var map = sim.Map;
        var damage = ThingDamage * PerTunedUpdate * fire;

        foreach (var e in (ReadOnlySpan<Entity>)[map.Structure[i], map.Item[i], map.Floor[i]])
        {
            if (e.IsNull || !sim.World.IsAlive(e))
                continue;
            ref var thing = ref sim.World.Get<Thing>(e);
            if (thing.Flammability <= 0)
                continue;
            thing.Hp -= damage * thing.Flammability;
            if (thing.Hp <= 0)
            {
                var (def, cell, facing) = (thing.Def, thing.Cell, thing.Facing);
                if (def.Category == Defs.ThingCategory.Building)
                    sim.Messages.Add(MessageKind.Warning, $"A {def.Label} burned down.", cell);
                sim.DestroyThing(e);
                Modding.Trades.Lost(sim, def, cell, facing); // a joined piece in a town building, say: the carpenter's to set back
                // Designations stay: a tree marked for chopping is still worth chopping as a dead one.
                if (def.BurnsIntoDef is { } remains && map.Structure[i].IsNull)
                {
                    sim.SpawnStructure(remains, cell);
                    // Land being cleared with fire: the charred snags come down next.
                    if ((map.Designations[i] & Maps.Designation.Burn) != 0 && remains.Harvest?.WorkType == Defs.WorkType.Chop)
                        map.Designate(i, Maps.Designation.Chop);
                }
            }
        }

        var ground = map.Floor[i].IsNull ? map.GroundUnder(i) : null;
        if (ground?.BurnsIntoDef != null && sim.Rand.Chance(0.04f * PerTunedUpdate * fire))
        {
            map.Block[i - map.LayerSize] = ground.BurnsIntoDef;
            map.MarkCellChanged(i - map.LayerSize);
        }
    }

    private void HurtCreatures(Simulation sim)
    {
        var map = sim.Map;
        foreach (var e in sim.Creatures())
        {
            var tile = sim.World.Get<Position>(e).Cell;
            var fire = map.Fire[map.Idx(tile)];
            if (fire <= 0)
                continue;
            var c = sim.World.Get<Creature>(e);
            if (c.Def.SafeTempMax >= 300)
                continue; // dragons shrug off flames
            // Ground fire licks at the legs first.
            Systems.HealthSystem.Damage(sim, e, CreatureDamage * fire, Defs.DamageType.Burn, bias: Defs.PartHeight.Low);
            sim.Messages.AddThrottled($"burn:{e.Index}", SimClock.TicksPerHour, MessageKind.Danger, $"{c.Name} is burning!", tile);
        }
    }

    /// <summary>Put out all fires (dev tool).</summary>
    public void ExtinguishAll(Simulation sim)
    {
        foreach (var i in _burning)
            sim.Map.Fire[i] = 0;
        _burning.Clear();
    }
}

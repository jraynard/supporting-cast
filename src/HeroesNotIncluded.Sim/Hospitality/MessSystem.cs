using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Hospitality;

/// <summary>What's on the floor. Blood is a dark stain to anyone who isn't looking closer (<see cref="Perception"/>).</summary>
public enum MessKind : byte
{
    SpilledAle,
    Mud,
    Crumbs,
    Blood,
}

/// <summary>One cell's mess: what, how much of it (0..1), and since when.</summary>
public readonly record struct MessSpot(MessKind Kind, float Amount, long Since);

/// <summary>
/// Mess on the floor, a cell at a time: ale spilled at the bar or sprayed from a fumbled tap, mud
/// off the road, crumbs where someone ate, and blood wherever the wounded walk. Anyone can see it,
/// and customers think the less of a room with mess in it. It stays indoors until it's cleaned up
/// (<see cref="ScrubSession"/>); outdoors, rain and feet see it off in a few hours. Scrubbed blood is
/// gone for good, which will matter once there are bodies.
/// </summary>
public sealed class MessSystem : ISimSystem, ISaveState
{
    /// <summary>Hours mess lasts outdoors.</summary>
    public const float OutdoorHours = 6;

    /// <summary>Satisfaction a customer loses each hour per spot of mess in the room with them (up to <see cref="WorstSpots"/>).</summary>
    public const float MoodPerSpot = 0.04f;

    public const int WorstSpots = 5;

    /// <summary>Hours a customer who's said so leaves it before saying so again.</summary>
    public const float ComplainHours = 3;

    /// <summary>Hours after arriving (or coming back from a quest) that an adventurer's boots are muddy.</summary>
    public const float MuddyHours = 3;

    private readonly Dictionary<int, MessSpot> _spots = [];
    private readonly Dictionary<Entity, long> _complained = [];
    private readonly SimRandom _rand = new(0x3E55u);
    private long _lastHour;

    public string Name => "Mess";
    public int Interval => SimClock.TicksPerHour / 12;

    /// <summary>Every spot of mess by cell index. Game code reads it through <see cref="PerceptionView.Messes"/>.</summary>
    public IReadOnlyDictionary<int, MessSpot> All => _spots;

    public MessSpot? At(Simulation sim, int3 cell) =>
        sim.Map.InBounds(cell) && _spots.TryGetValue(sim.Map.Idx(cell), out var s) ? s : null;

    public static string Label(MessKind kind) => kind switch
    {
        MessKind.SpilledAle => "spilled ale",
        MessKind.Mud => "mud",
        MessKind.Crumbs => "crumbs",
        _ => "dark stain",
    };

    /// <summary>The verb for clearing it, as the work key offers it.</summary>
    public static string Verb(MessKind kind) => kind switch
    {
        MessKind.Crumbs => "Sweep up the crumbs",
        MessKind.Blood => "Scrub the dark stain",
        _ => $"Mop up the {Label(kind)}",
    };

    /// <summary>Mess at a cell (or the floor nearest it): added to what's there, the worse kind winning.</summary>
    public void Spill(Simulation sim, MessKind kind, int3 cell, float amount = 1f)
    {
        if (!sim.Map.InBounds(cell) || !sim.Map.Standable(sim.Map.Idx(cell)))
            return;
        var i = sim.Map.Idx(cell);
        if (_spots.TryGetValue(i, out var was))
            _spots[i] = new MessSpot((MessKind)Math.Max((int)was.Kind, (int)kind), Math.Min(1f, was.Amount + amount), was.Since);
        else
            _spots[i] = new MessSpot(kind, Math.Clamp(amount, 0.05f, 1f), sim.Clock.Tick);
    }

    /// <summary>Some of a cell's mess cleaned away; gone when there's none left.</summary>
    public void Clean(Simulation sim, int3 cell, float amount)
    {
        if (!sim.Map.InBounds(cell))
            return;
        var i = sim.Map.Idx(cell);
        if (!_spots.TryGetValue(i, out var s))
            return;
        if (s.Amount - amount <= 0.001f)
            _spots.Remove(i);
        else
            _spots[i] = s with { Amount = s.Amount - amount };
    }

    public void Update(Simulation sim)
    {
        var w = sim.World;
        var now = sim.Clock.Tick;
        // Outdoors it doesn't last.
        if (_spots.Count > 0)
        {
            List<int>? gone = null;
            foreach (var (i, s) in _spots)
                if (now - s.Since > SimClock.HoursToTicks(OutdoorHours) && sim.Rooms.RoomAt(sim.Map.CellOf(i)) is not { Indoors: true })
                    (gone ??= []).Add(i);
            if (gone != null)
                foreach (var i in gone)
                    _spots.Remove(i);
        }
        if (!sim.Round.Live)
            return;
        foreach (var e in sim.Creatures())
        {
            var at = w.Get<Position>(e).Cell;
            // The wounded bleed on the floor as they go.
            if (w.TryGet<Health>(e, out var h) && h.BleedPerHour > 0.02f && _rand.NextFloat() < 0.3f && !w.Get<Creature>(e).Dead)
                Spill(sim, MessKind.Blood, at, 0.5f);
            // Adventurers in off the road track mud indoors.
            if (w.TryGet<Adventurer>(e, out var a) && now - a.Arrived < SimClock.HoursToTicks(MuddyHours) && _rand.NextFloat() < 0.12f
                && sim.Rooms.RoomAt(at) is { Indoors: true } && w.Get<Mover>(e).Moving)
                Spill(sim, MessKind.Mud, at, 0.4f);
        }
        if (now - _lastHour >= SimClock.TicksPerHour)
        {
            _lastHour = now;
            Judge(sim);
        }
    }

    /// <summary>Each hour: customers in a room with mess in it think the less of the town, and now and then say so.</summary>
    private void Judge(Simulation sim)
    {
        var w = sim.World;
        if (_spots.Count == 0)
            return;
        var byRoom = new Dictionary<int, int>();
        foreach (var i in _spots.Keys)
            if (sim.Rooms.RoomAt(sim.Map.CellOf(i)) is { Indoors: true } r)
                byRoom[r.Id] = byRoom.GetValueOrDefault(r.Id) + 1;
        foreach (var e in sim.Creatures())
        {
            if (!w.Has<Adventurer>(e) || !SpeechSystem.CanAct(sim, e) || sim.Rooms.RoomAt(w.Get<Position>(e).Cell) is not { Indoors: true } room
                || !byRoom.TryGetValue(room.Id, out var spots))
                continue;
            w.Get<Adventurer>(e).Satisfaction -= MoodPerSpot * Math.Min(spots, WorstSpots);
            if (spots >= 2 && (!_complained.TryGetValue(e, out var last) || sim.Clock.Tick - last > SimClock.HoursToTicks(ComplainHours)))
            {
                _complained[e] = sim.Clock.Tick;
                sim.Speech.Say(sim, e, spots >= 4 ? "Does nobody ever clean this floor?" : "Mind the mess, it's everywhere.", Volume.Say);
            }
        }
    }

    public void Save(SaveWriter w, Simulation sim)
    {
        var (a, b, c, d) = _rand.State;
        w.UInt(a); w.UInt(b); w.UInt(c); w.UInt(d);
        w.Long(_lastHour);
        w.Int(_spots.Count);
        foreach (var (i, s) in _spots.OrderBy(kv => kv.Key))
        {
            w.Int(i);
            w.Byte((byte)s.Kind);
            w.Float(s.Amount);
            w.Long(s.Since);
        }
        w.Int(_complained.Count);
        foreach (var (e, t) in _complained.OrderBy(kv => kv.Key.Index))
        {
            w.Entity(e);
            w.Long(t);
        }
    }

    public void Load(SaveReader r, Simulation sim)
    {
        _rand.State = (r.UInt(), r.UInt(), r.UInt(), r.UInt());
        _lastHour = r.Long();
        _spots.Clear();
        for (var n = r.Count(1 << 20); n > 0; n--)
        {
            var i = r.Int();
            var kind = r.Byte();
            if (!Enum.IsDefined((MessKind)kind))
                throw new SaveException($"Corrupt save: mess kind {kind}");
            _spots[i] = new MessSpot((MessKind)kind, r.Float(), r.Long());
        }
        _complained.Clear();
        for (var n = r.Count(1 << 16); n > 0; n--)
            _complained[r.Entity()] = r.Long();
    }
}

/// <summary>
/// Cleaning up a cell's mess: the stain lies in blots on a grid over the floor, and the mop (or a
/// rag, by hand) lifts it only while it's moving across them, so standing over a blot does nothing:
/// rub it out. With the mouse, drag over it with the button held; with keys, A/D sweep the mop
/// back and forth along a row and W/S move it between rows. A mop lifts more for the distance than
/// a rag. Stop early and what's cleaned stays clean.
/// </summary>
public sealed class ScrubSession : WorkSession
{
    /// <summary>The stain's blots are kept on a grid over the floor, this many cells across and down.</summary>
    public const int GridW = 10, GridH = 6;

    /// <summary>Mess lifted for each whole width the mop or rag is rubbed across a blot (at its middle), by hand and with a mop.</summary>
    public const float ByHand = 0.5f, WithMop = 1.2f;

    /// <summary>How far round the mop reaches, and how far the keys move it a step (0..1 of the floor's width).</summary>
    public const float Reach = 0.22f, MopSpeed = 0.035f;

    /// <summary>Stain a cell can have left and still count clean.</summary>
    public const float CleanEnough = 0.05f;

    public int3 Cell { get; set; }
    public MessKind Kind { get; set; }

    /// <summary>What's left of it, in the mess's own measure (<see cref="MessSpot.Amount"/>).</summary>
    public float Amount => _initial <= 0f ? 0f : _start0 * Math.Max(0f, Remaining) / _initial;

    public bool Mop { get; set; }

    /// <summary>Times the mop's changed direction across the floor: its strokes, for the sound and the splashes.</summary>
    public int Strokes { get; private set; }

    /// <summary>The mop, 0..1 across and down the floor.</summary>
    public float MopX { get; private set; } = 0.5f;
    public float MopY { get; private set; } = 0.5f;

    private int _lastDir, _dx, _dy;
    private float _start, _start0, _initial;
    private float[] _blots = new float[GridW * GridH];

    /// <summary>Which way the last stroke went (-1, 0, +1): the mop swings with it.</summary>
    public int LastDir => _lastDir;

    /// <summary>The stain at a grid cell, 0..1.</summary>
    public float BlotAt(int x, int y) => _blots[y * GridW + x];

    /// <summary>The stain left, summed over the grid.</summary>
    private float Remaining => _blots.Sum();

    public override int3? Focus(Simulation sim) => Cell;

    public override Skill Skill => Skill.Cooking;
    public override float Xp => 2f;
    public override string Title => MessSystem.Verb(Kind);

    internal static ScrubSession? For(Simulation sim, Entity who, int3 cell)
    {
        if (sim.Mess.At(sim, cell) is not { } s)
            return null;
        return new ScrubSession { Cell = cell, Kind = s.Kind, _start = s.Amount, _start0 = s.Amount, Mop = !Toolbelt.Best(sim, who, "Mop").IsNull };
    }

    public override bool StillValid(Simulation sim) =>
        sim.Mess.At(sim, Cell) is not null && AvatarSystem.InReach(sim.World.Get<Position>(Worker).Cell, Cell);

    protected internal override void Begin() => Spread();

    protected override void Cloned() => _blots = (float[])_blots.Clone();

    /// <summary>The mess as blots: a few, darker at their middles and heavier the more there is of it.</summary>
    private void Spread()
    {
        Array.Clear(_blots);
        var blots = 2 + (_start0 > 0.5f ? 1 : 0) + Rand.NextInt(2);
        var peak = 0.4f + 0.6f * Math.Clamp(_start0, 0f, 1f);
        for (var k = 0; k < blots; k++)
        {
            float cx = Rand.Range(0.2f, 0.8f) * GridW, cy = Rand.Range(0.2f, 0.8f) * GridH, r = Rand.Range(1.3f, 2.4f);
            for (var y = 0; y < GridH; y++)
                for (var x = 0; x < GridW; x++)
                {
                    var d = MathF.Sqrt((x + 0.5f - cx) * (x + 0.5f - cx) + (y + 0.5f - cy) * (y + 0.5f - cy)) / r;
                    if (d < 1f)
                        _blots[y * GridW + x] = Math.Min(1f, _blots[y * GridW + x] + (1f - d * d) * peak * Rand.Range(0.7f, 1f));
                }
        }
        _initial = Remaining;
        if (_initial <= 0f)
            Done = true;
    }

    protected override void Axis(int value) => _dx = value;

    protected override void Axis2(int value) => _dy = value;

    /// <summary>The mop goes where the mouse points, rubbing on the way only with the button held.</summary>
    protected override void Pointed(float u, float v) => MoveTo(u, v, Held);

    protected override void Step()
    {
        if (_dx != 0 || _dy != 0)
            MoveTo(MopX + _dx * MopSpeed, MopY + _dy * MopSpeed, true); // the keys always rub: the mop's on the floor
    }

    /// <summary>Move the mop, lifting what's under it on the way if it's pressed to the floor.</summary>
    private void MoveTo(float x, float y, bool rubbing)
    {
        x = Math.Clamp(x, 0f, 1f);
        y = Math.Clamp(y, 0f, 1f);
        float dx = x - MopX, dy = y - MopY;
        var dist = MathF.Sqrt(dx * dx + dy * dy);
        // A stroke is a change of direction across the floor: one way, then back.
        var dir = Math.Abs(dx) < 0.004f ? 0 : Math.Sign(dx);
        if (dir != 0 && dir != _lastDir)
        {
            if (_lastDir != 0)
                Strokes++;
            _lastDir = dir;
        }
        if (!rubbing || dist <= 0f)
        {
            (MopX, MopY) = (x, y);
            return;
        }
        // Along the way in short pieces, so a quick drag doesn't skip what it passed over.
        var pieces = Math.Max(1, (int)MathF.Ceiling(dist / (Reach * 0.25f)));
        var lift = (Mop ? WithMop : ByHand) * (0.6f + 0.4f * Hands.Precision) * dist / pieces;
        for (var i = 1; i <= pieces; i++)
            Rub(MopX + dx * i / pieces, MopY + dy * i / pieces, lift);
        (MopX, MopY) = (x, y);
        if (_blots.All(b => b <= CleanEnough))
        {
            Array.Clear(_blots);
            Done = true;
        }
    }

    private void Rub(float at, float down, float lift)
    {
        for (var y = 0; y < GridH; y++)
            for (var x = 0; x < GridW; x++)
            {
                ref var b = ref _blots[y * GridW + x];
                if (b <= 0f)
                    continue;
                float dx = (x + 0.5f) / GridW - at, dy = (y + 0.5f) / GridH - down;
                var d = MathF.Sqrt(dx * dx + dy * dy) / Reach;
                if (d < 1f)
                    b = Math.Max(0f, b - lift * (1f - d * 0.5f));
            }
    }

    public override void Finish(Simulation sim) => Apply(sim);

    public override void Interrupted(Simulation sim) => Apply(sim);

    private void Apply(Simulation sim)
    {
        var left = Done ? 0f : Amount;
        sim.Mess.Clean(sim, Cell, _start - left);
        _start = left;
        if (left <= 0f)
            sim.Messages.Add(MessageKind.Info, Kind == MessKind.Blood ? "Scrubbed clean: not a trace of it." : $"Cleaned up the {MessSystem.Label(Kind)}.", Cell);
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Int3(Cell);
        w.Byte((byte)Kind);
        w.Float(_start0);
        w.Bool(Mop);
        w.Int(Strokes);
        w.Int(_lastDir);
        w.Float(_start);
        w.Float(MopX);
        w.Float(MopY);
        w.Int(_dx);
        w.Int(_dy);
        w.Float(_initial);
        foreach (var b in _blots)
            w.Float(b);
    }

    protected override void ReadState(SaveReader r)
    {
        Cell = r.Int3();
        var kind = r.Byte();
        Kind = Enum.IsDefined((MessKind)kind) ? (MessKind)kind : MessKind.Mud;
        _start0 = r.Float();
        Mop = r.Bool();
        Strokes = r.Int();
        _lastDir = Math.Sign(r.Int());
        _start = r.Float();
        _blots = new float[GridW * GridH];
        if (r.Version < 42) // before the blots (format 42): what was left (_start0 was its Amount), spread evenly
        {
            var left = _start0;
            _start0 = _start;
            Array.Fill(_blots, _start <= 0f ? 0f : Math.Clamp(left / _start, 0f, 1f));
            _initial = _blots.Length;
            return;
        }
        MopX = Math.Clamp(r.Float(), 0f, 1f);
        MopY = Math.Clamp(r.Float(), 0f, 1f);
        _dx = Math.Sign(r.Int());
        _dy = Math.Sign(r.Int());
        _initial = r.Float();
        for (var i = 0; i < _blots.Length; i++)
            _blots[i] = Math.Clamp(r.Float(), 0f, 1f);
    }
}

/// <summary>The work key on a mess: scrub it up (<see cref="ScrubSession"/>).</summary>
public sealed record ScrubCommand(Entity Avatar, int3 Cell) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (!InventoryCommand.Ready(sim, Avatar) || !sim.Map.InBounds(Cell) || !AvatarSystem.InReach(sim.World.Get<Position>(Avatar).Cell, Cell))
            return;
        if (ScrubSession.For(sim, Avatar, Cell) is { } s)
            WorkSystem.Start(sim, Avatar, s);
    }
}

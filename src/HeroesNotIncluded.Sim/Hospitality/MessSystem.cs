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
        if (sim.Round.Phase != RoundPhase.Playing)
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
/// Cleaning up a cell's mess: scrub back and forth (A, D, A, D...), each stroke taking some of it
/// away, more with a mop than by hand. Stop early and what's cleaned stays clean.
/// </summary>
public sealed class ScrubSession : WorkSession
{
    /// <summary>Mess a stroke takes up by hand, and with a mop.</summary>
    public const float ByHand = 0.06f, WithMop = 0.14f;

    public int3 Cell { get; set; }
    public MessKind Kind { get; set; }

    /// <summary>What's left of it.</summary>
    public float Amount { get; private set; }

    public bool Mop { get; set; }

    public int Strokes { get; private set; }

    private int _lastDir;
    private float _start;

    /// <summary>Which way the last stroke went (-1, 0, +1): the mop swings with it.</summary>
    public int LastDir => _lastDir;

    public override int3? Focus(Simulation sim) => Cell;

    public override Skill Skill => Skill.Cooking;
    public override float Xp => 2f;
    public override string Title => MessSystem.Verb(Kind);

    internal static ScrubSession? For(Simulation sim, Entity who, int3 cell)
    {
        if (sim.Mess.At(sim, cell) is not { } s)
            return null;
        return new ScrubSession { Cell = cell, Kind = s.Kind, Amount = s.Amount, _start = s.Amount, Mop = !Toolbelt.Best(sim, who, "Mop").IsNull };
    }

    public override bool StillValid(Simulation sim) =>
        sim.Mess.At(sim, Cell) is not null && AvatarSystem.InReach(sim.World.Get<Position>(Worker).Cell, Cell);

    protected override void Axis(int value)
    {
        if (value == 0 || value == _lastDir)
            return;
        // A stroke is a change of direction: one way, then back.
        if (_lastDir != 0)
        {
            Strokes++;
            Amount -= (Mop ? WithMop : ByHand) * (0.6f + 0.4f * Hands.Precision);
            if (Amount <= 0f)
                Done = true;
        }
        _lastDir = value;
    }

    protected override void Step() { }

    public override void Finish(Simulation sim) => Apply(sim);

    public override void Interrupted(Simulation sim) => Apply(sim);

    private void Apply(Simulation sim)
    {
        sim.Mess.Clean(sim, Cell, _start - Math.Max(0f, Amount));
        _start = Math.Max(0f, Amount);
        if (Amount <= 0f)
            sim.Messages.Add(MessageKind.Info, Kind == MessKind.Blood ? "Scrubbed clean: not a trace of it." : $"Cleaned up the {MessSystem.Label(Kind)}.", Cell);
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Int3(Cell);
        w.Byte((byte)Kind);
        w.Float(Amount);
        w.Bool(Mop);
        w.Int(Strokes);
        w.Int(_lastDir);
        w.Float(_start);
    }

    protected override void ReadState(SaveReader r)
    {
        Cell = r.Int3();
        var kind = r.Byte();
        Kind = Enum.IsDefined((MessKind)kind) ? (MessKind)kind : MessKind.Mud;
        Amount = r.Float();
        Mop = r.Bool();
        Strokes = r.Int();
        _lastDir = Math.Sign(r.Int());
        _start = r.Float();
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

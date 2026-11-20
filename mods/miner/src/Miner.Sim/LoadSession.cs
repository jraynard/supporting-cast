using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Miner;

/// <summary>
/// Loading a wheelbarrow (the miner's third hand game, with the walk and the tip after it; a first pass, to be found by
/// playing it). The bed seen from above, the wheel at the top and the handles at the bottom; the ore in reach to go in it,
/// a piece at a time.
/// <list type="bullet">
/// <item>WASD (or the mouse) moves over the bed's slots; F (or a click) sets the next piece there, two to a slot.</item>
/// <item>Space lifts the top piece off the slot again, to go somewhere better.</item>
/// <item>The load's balance shows as a dot where its weight centres (<see cref="Barrows.Centre"/>), inside the steady
/// ring or not: square across, a little behind the wheel. A load out of balance may tip on the way
/// (<see cref="BarrowSystem"/>), spilling some of it; nothing's ever lost here.</item>
/// <item>It's done when nothing's left to load or the barrow's full; Esc stops with what's in it.</item>
/// </list>
/// Pieces are only counted here; the items leave the world when it's over (<see cref="Finish"/>), taken from where they
/// were (the hand, the ground in reach), so what's moved away meanwhile simply isn't loaded.
/// </summary>
public sealed class LoadSession : WorkSession
{
    /// <summary>The barrow being loaded, set down.</summary>
    public Entity Barrow { get; set; }

    /// <summary>What's on the bed (the barrow's load as it was, then what's set on it).</summary>
    public List<BarrowPiece> Placed { get; private set; } = [];

    /// <summary>How many of <see cref="Placed"/>'s defs were in the barrow before (by def), so only new pieces are taken from the world.</summary>
    private Dictionary<ThingDef, int> _had = [];

    /// <summary>Pieces still to set on the bed, the next first.</summary>
    public List<ThingDef> ToLoad { get; private set; } = [];

    /// <summary>The slot the next piece hangs over.</summary>
    public int Slot { get; private set; } = Barrows.SlotAt(1, 1);

    /// <summary>Pieces set and lifted (for the panel and the sound).</summary>
    public int Sets { get; private set; }
    public int Lifts { get; private set; }

    /// <summary>Where the pieces came from: the hand, and stacks on the ground in reach.</summary>
    private List<Entity> _sources = [];

    private int _axis, _axis2;

    public override Skill Skill => Skill.Mining;
    public override float Xp => 1f + 0.2f * Sets;
    public override string Title => $"Loading the barrow ({Barrows.Describe(Placed)})";

    public override int3? Focus(Simulation sim) => sim.World.IsAlive(Barrow) && sim.World.TryGet<Thing>(Barrow, out var t) ? t.Cell : null;

    public float Mass => Placed.Sum(p => p.Def.Mass);

    public float Risk => Barrows.Risk(Placed);

    /// <summary>Whether the next piece fits: the slot has room, and the barrow can take its weight.</summary>
    public bool Fits(int slot) =>
        ToLoad.Count > 0 && Barrows.InSlot(Placed, slot) < Barrows.Layers && Mass + ToLoad[0].Mass <= Barrows.MaxMass + 1e-4f;

    /// <summary>The ore in reach of <paramref name="who"/> to load: in hand first, then lying on the ground around them, nearest cell first.</summary>
    public static List<Entity> Sources(Simulation sim, Entity who)
    {
        var w = sim.World;
        var found = new List<Entity>();
        var held = w.Get<Carrying>(who).Item;
        if (!held.IsNull && w.TryGet<Thing>(held, out var h) && Barrows.Loadable(sim, h.Def))
            found.Add(held);
        var here = w.Get<Position>(who).Cell;
        foreach (var c in sim.SpiralFrom(here, 1))
            if (sim.Map.InBounds(c) && AvatarSystem.InReach(here, c) && sim.Map.Item[sim.Map.Idx(c)] is { IsNull: false } item
                && w.TryGet<Thing>(item, out var t) && t.Spawned && Barrows.Loadable(sim, t.Def))
                found.Add(item);
        return found;
    }

    /// <summary>Why <paramref name="who"/> can't load <paramref name="barrow"/> now, or null: it's set down in reach, and there's ore in reach that fits.</summary>
    public static string? Problem(Simulation sim, Entity who, Entity barrow)
    {
        var w = sim.World;
        if (!Barrows.IsBarrow(sim, barrow) || !w.Get<Thing>(barrow).Spawned)
            return "no barrow set down";
        if (!AvatarSystem.InReach(w.Get<Position>(who).Cell, w.Get<Thing>(barrow).Cell))
            return "out of reach";
        var room = Barrows.MaxMass - Barrows.LoadOf(sim, barrow).Mass;
        return Sources(sim, who).Any(e => w.Get<Thing>(e).Def.Mass <= room + 1e-4f) ? null : "nothing in reach to load";
    }

    /// <summary>A loading of <paramref name="barrow"/>: its load as it is, and every piece of ore in reach waiting, as many as could fit.</summary>
    public static LoadSession At(Simulation sim, Entity who, Entity barrow)
    {
        var s = new LoadSession { Barrow = barrow };
        foreach (var p in Barrows.LoadOf(sim, barrow).Pieces)
        {
            s.Placed.Add(p);
            s._had[p.Def] = s._had.GetValueOrDefault(p.Def) + 1;
        }
        s._sources = Sources(sim, who);
        var mass = s.Mass;
        foreach (var src in s._sources)
        {
            var def = sim.World.Get<Thing>(src).Def;
            for (var n = sim.World.TryGet<Stack>(src, out var st) ? st.Count : 1; n > 0 && mass + def.Mass <= Barrows.MaxMass + 1e-4f
                 && s.Placed.Count + s.ToLoad.Count < Barrows.Slots * Barrows.Layers; n--)
            {
                s.ToLoad.Add(def);
                mass += def.Mass;
            }
        }
        return s;
    }

    public override bool StillValid(Simulation sim)
    {
        var w = sim.World;
        return Barrows.IsBarrow(sim, Barrow) && w.Get<Thing>(Barrow).Spawned
            && AvatarSystem.InReach(w.Get<Position>(Worker).Cell, w.Get<Thing>(Barrow).Cell);
    }

    protected override void Step()
    {
        if (!Done && ToLoad.Count == 0)
            Done = true;
    }

    // WASD steps over the slots a press at a time: A/D across, W/S toward the wheel and the handles.
    protected override void Axis(int value)
    {
        if (value != 0 && value != _axis)
            Slot = Barrows.SlotAt(Barrows.ColOf(Slot) + value, Barrows.RowOf(Slot));
        _axis = value;
    }

    protected override void Axis2(int value)
    {
        if (value != 0 && value != _axis2)
            Slot = Barrows.SlotAt(Barrows.ColOf(Slot), Barrows.RowOf(Slot) + value);
        _axis2 = value;
    }

    protected override void Pointed(float u, float v) => Slot = SlotFrom(u, v);

    protected override void Clicked(float? u, float? v)
    {
        if (u is { } x && v is { } y)
            Slot = SlotFrom(x, y);
        Press();
    }

    private static int SlotFrom(float u, float v) =>
        Barrows.SlotAt((int)(u * Barrows.Cols), (int)(v * Barrows.Rows));

    /// <summary>F: set the next piece in the slot.</summary>
    protected override void Press()
    {
        if (!Fits(Slot))
            return;
        Placed.Add(new BarrowPiece(ToLoad[0], (byte)Slot));
        ToLoad.RemoveAt(0);
        Sets++;
        if (ToLoad.Count == 0 || !Fits(Slot) && Enumerable.Range(0, Barrows.Slots).All(k => !Fits(k)))
        {
            // Loaded: the steadier, the better a loading it was.
            Sample(Math.Clamp(1f - Risk / (2f * Barrows.Steady), 0f, 1f));
            Done = true;
        }
    }

    /// <summary>Space: lift the top piece off the slot, to set it again first.</summary>
    protected override void Act()
    {
        for (var k = Placed.Count - 1; k >= 0; k--)
            if (Placed[k].Slot == Slot)
            {
                ToLoad.Insert(0, Placed[k].Def);
                Placed.RemoveAt(k);
                Lifts++;
                return;
            }
    }

    public override void Finish(Simulation sim) => Load(sim);

    /// <summary>Stopped: what's on the bed is loaded, and what wasn't set stays where it was.</summary>
    public override void Interrupted(Simulation sim) => Load(sim);

    private void Load(Simulation sim)
    {
        var w = sim.World;
        if (!Barrows.IsBarrow(sim, Barrow))
            return;
        // Pieces the barrow had that were lifted and not set again go back on, anywhere with room.
        foreach (var (def, had) in _had.OrderBy(kv => kv.Key.Index))
        {
            var missing = had - Placed.Count(p => p.Def == def);
            for (var n = 0; n < missing; n++)
                if (Enumerable.Range(0, Barrows.Slots).FirstOrDefault(k => Barrows.InSlot(Placed, k) < Barrows.Layers, -1) is >= 0 and var free)
                    Placed.Add(new BarrowPiece(def, (byte)free));
        }
        // New pieces are taken from where they lay; any that can't be found aren't loaded after all.
        foreach (var group in Placed.GroupBy(p => p.Def).ToList())
        {
            var wanted = group.Count() - _had.GetValueOrDefault(group.Key);
            var taken = 0;
            foreach (var src in _sources)
            {
                if (taken >= wanted)
                    break;
                if (!w.IsAlive(src) || !w.TryGet<Thing>(src, out var t) || t.Def != group.Key)
                    continue;
                var count = Math.Min(wanted - taken, w.TryGet<Stack>(src, out var st) ? st.Count : 1);
                var inHand = w.Get<Carrying>(Worker).Item == src;
                if (!inHand && !(t.Spawned && AvatarSystem.InReach(w.Get<Position>(Worker).Cell, t.Cell)))
                    continue;
                var piece = inHand ? Inventory.TakeFromPerson(sim, Worker, src, count) : sim.TakeFromStack(src, count);
                sim.DestroyThing(piece);
                taken += count;
            }
            for (var extra = wanted - taken; extra > 0; extra--)
                Placed.RemoveAt(Placed.FindLastIndex(p => p.Def == group.Key));
        }
        var load = Barrows.LoadOf(sim, Barrow).Copy();
        load.Pieces.Clear();
        load.Pieces.AddRange(Placed);
        w.Remove<BarrowLoad>(Barrow);
        if (load.Pieces.Count > 0)
            w.Add(Barrow, load);
    }

    protected override void Cloned()
    {
        Placed = [.. Placed];
        ToLoad = [.. ToLoad];
        _had = new Dictionary<ThingDef, int>(_had);
        _sources = [.. _sources];
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Barrow);
        w.Int(Placed.Count);
        foreach (var p in Placed)
        {
            w.Def(p.Def);
            w.Byte(p.Slot);
        }
        w.Int(_had.Count);
        foreach (var (def, n) in _had.OrderBy(kv => kv.Key.Index))
        {
            w.Def(def);
            w.Int(n);
        }
        w.Int(ToLoad.Count);
        foreach (var def in ToLoad)
            w.Def(def);
        w.Int(_sources.Count);
        foreach (var e in _sources)
            w.Entity(e);
        w.Int(Slot);
        w.Int(Sets);
        w.Int(Lifts);
        w.Int(_axis);
        w.Int(_axis2);
    }

    protected override void ReadState(SaveReader r)
    {
        const int Most = Barrows.Slots * Barrows.Layers;
        Barrow = r.Entity();
        Placed = [];
        for (int k = 0, n = r.Count(Most); k < n; k++)
        {
            var def = r.Def<ThingDef>();
            var slot = r.Byte();
            if (slot < Barrows.Slots)
                Placed.Add(new BarrowPiece(def, slot));
        }
        _had = [];
        for (int k = 0, n = r.Count(Most); k < n; k++)
        {
            var def = r.Def<ThingDef>();
            _had[def] = Math.Clamp(r.Int(), 0, Most);
        }
        ToLoad = [];
        for (int k = 0, n = r.Count(Most); k < n; k++)
            ToLoad.Add(r.Def<ThingDef>());
        _sources = [];
        for (int k = 0, n = r.Count(64); k < n; k++)
            _sources.Add(r.Entity());
        Slot = Math.Clamp(r.Int(), 0, Barrows.Slots - 1);
        Sets = Math.Max(0, r.Int());
        Lifts = Math.Max(0, r.Int());
        _axis = Math.Sign(r.Int());
        _axis2 = Math.Sign(r.Int());
    }
}

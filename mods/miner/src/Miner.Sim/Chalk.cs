using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Saving;

namespace HeroesNotIncluded.Miner;

/// <summary>A miner's chalk on a rock face: their own claim about what's behind it, never the truth.</summary>
public enum ChalkMark : byte
{
    None,

    /// <summary>"A seam's behind this": an X.</summary>
    Seam,

    /// <summary>"Nothing behind this": a ring.</summary>
    Bare,
}

/// <summary>
/// The chalk on the mine's faces (<see cref="SoundSession"/> sets it). Everyone sees it, and it says only what the
/// miner chose to write, so it gives nothing hidden away; a traitor can chalk a seam bare. A face dug out takes its
/// chalk with it. Kept by cell, in cell order.
/// </summary>
public sealed class ChalkMarks : ISimSystem, ISaveState
{
    public string Name => "ChalkMarks";

    public int Interval => SimClock.TicksPerHour / 4;

    private readonly SortedDictionary<int, ChalkMark> _marks = [];

    /// <summary>Every mark, by cell index, in order.</summary>
    public IReadOnlyDictionary<int, ChalkMark> All => _marks;

    public static ChalkMark At(Simulation sim, int3 cell) =>
        sim.SystemOrNull<ChalkMarks>() is { } chalk && sim.Map.InBounds(cell) && chalk._marks.TryGetValue(sim.Map.Idx(cell), out var m) ? m : ChalkMark.None;

    /// <summary>Chalk a face (or wipe it, with <see cref="ChalkMark.None"/>). Only solid rock takes chalk.</summary>
    public static void Set(Simulation sim, int3 cell, ChalkMark mark)
    {
        if (sim.SystemOrNull<ChalkMarks>() is not { } chalk || !sim.Map.InBounds(cell))
            return;
        var i = sim.Map.Idx(cell);
        if (mark == ChalkMark.None || !sim.Map.IsSolid(i))
            chalk._marks.Remove(i);
        else
            chalk._marks[i] = mark;
    }

    /// <summary>Faces dug out since: their chalk went with the rock.</summary>
    public void Update(Simulation sim)
    {
        List<int>? gone = null;
        foreach (var i in _marks.Keys)
            if (!sim.Map.IsSolid(i))
                (gone ??= []).Add(i);
        if (gone != null)
            foreach (var i in gone)
                _marks.Remove(i);
    }

    public void Save(SaveWriter w, Simulation sim)
    {
        w.Int(_marks.Count);
        foreach (var (i, mark) in _marks)
        {
            w.Int(i);
            w.Byte((byte)mark);
        }
    }

    public void Load(SaveReader r, Simulation sim)
    {
        _marks.Clear();
        var n = r.Int();
        for (var k = 0; k < n; k++)
        {
            var i = r.Int();
            var mark = r.Byte();
            if (i >= 0 && i < sim.Map.Block.Length && Enum.IsDefined((ChalkMark)mark) && mark != 0)
                _marks[i] = (ChalkMark)mark;
        }
    }
}

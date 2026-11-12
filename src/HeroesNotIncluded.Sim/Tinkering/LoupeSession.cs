using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Tinkering;

/// <summary>
/// A curio under the loupe. A/D move the glass along it; the eye catches as it nears a tell (a
/// maker's mark, tooling, wear, a seam), and holding F still over one studies it until it's read.
/// Drift off it and the reading's lost. Reaching the far end finishes the look, however much was
/// read: each tell read narrows what you'd put it at, and on a fake one of them gives it away
/// (<see cref="Curios.Read"/>, worked out by the sim alone: the session knows where the tells are,
/// never what they say).
/// </summary>
public sealed class LoupeSession : WorkSession
{
    /// <summary>How far the loupe moves per step held.</summary>
    public const float MovePerStep = 0.0028f;

    /// <summary>How near (along the piece) a tell catches the eye, and how near the loupe must be to study it.</summary>
    public const float CatchRange = 0.08f, StudyRange = 0.022f;

    /// <summary>Steps of holding still over a tell to read it (before skill shortens it).</summary>
    public const int StepsToRead = 150;

    public Entity Curio { get; set; } = Entity.Null;
    public uint Seed { get; set; }

    /// <summary>Where the loupe is along the piece, 0..1.</summary>
    public float At { get; private set; }

    /// <summary>What catches the eye, 0..1: rising near a tell.</summary>
    public float Catch { get; private set; }

    /// <summary>How far the tell under the loupe has been read, 0..1.</summary>
    public float Reading { get; private set; }

    /// <summary>Which tells have been read.</summary>
    public bool[] Studied { get; private set; } = [];

    private float[] _tells = [];
    private int _move;
    private int _studying = -1;

    public int ReadCount => Studied.Count(r => r);
    public int StepsPerTell => (int)(StepsToRead / Hands.Tolerance);

    public override Skill Skill => Skill.Crafting;
    public override float Xp => 15f + 10f * ReadCount;
    public override string Title => "Looking a curio over under the loupe";

    public static void TryStart(Simulation sim, Entity worker, Entity curio)
    {
        if (Curios.Problem(sim, worker, curio) is { } why)
        {
            sim.Messages.Add(MessageKind.Info, $"Can't look that over: {why}.", sim.LocationOf(worker));
            return;
        }
        WorkSystem.Start(sim, worker, new LoupeSession { Curio = curio, Seed = sim.World.Get<Curio>(curio).Seed });
    }

    public override bool StillValid(Simulation sim) => Curios.Problem(sim, Worker, Curio) == null;

    protected internal override void Begin()
    {
        _tells = Curios.Tells(Seed);
        Studied = new bool[_tells.Length];
    }

    protected override void Cloned()
    {
        Studied = (bool[])Studied.Clone();
    }

    protected override void Axis(int value) => _move = value;

    protected override void Step()
    {
        if (Done)
            return;
        At = Math.Clamp(At + _move * MovePerStep + (Rand.NextFloat() * 2f - 1f) * 0.0004f * Hands.Tremor * 4f, 0f, 1f);
        var nearest = -1;
        var near = 1f;
        for (var i = 0; i < _tells.Length; i++)
            if (MathF.Abs(At - _tells[i]) < near)
                (nearest, near) = (i, MathF.Abs(At - _tells[i]));
        var catching = Math.Max(0f, 1f - near / CatchRange);
        Catch = Math.Clamp(catching * 0.85f + Rand.NextFloat() * 0.15f * (1f + Hands.Tremor), 0f, 1f);

        if (Held && _move == 0 && nearest >= 0 && near <= StudyRange && !Studied[nearest])
        {
            if (_studying != nearest)
                (_studying, Reading) = (nearest, 0f);
            Reading += 1f / StepsPerTell;
            if (Reading >= 1f)
            {
                Studied[nearest] = true;
                Sample(1f);
                (_studying, Reading) = (-1, 0f);
            }
        }
        else if (_studying >= 0)
            (_studying, Reading) = (-1, 0f);

        if (At >= 1f)
            Done = true;
    }

    public override void Finish(Simulation sim)
    {
        if (!Curios.IsCurio(sim, Curio))
            return;
        var giveaway = Curios.Giveaway(Seed);
        var saw = giveaway < Studied.Length && Studied[giveaway];
        Curios.Appraised(sim, Worker, Curio, Curios.Read(sim, Worker, Curio, ReadCount, saw));
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Curio);
        w.UInt(Seed);
        w.Float(At);
        w.Float(Catch);
        w.Float(Reading);
        w.Int(Studied.Length);
        foreach (var r in Studied)
            w.Bool(r);
        w.Int(_move);
        w.Int(_studying);
    }

    protected override void ReadState(SaveReader r)
    {
        Curio = r.Entity();
        Seed = r.UInt();
        At = r.Float();
        Catch = r.Float();
        Reading = r.Float();
        _tells = Curios.Tells(Seed);
        var n = r.Count(8);
        if (n != _tells.Length)
            throw new SaveException("Corrupt save: a curio's tells don't match");
        Studied = new bool[n];
        for (var i = 0; i < n; i++)
            Studied[i] = r.Bool();
        _move = Math.Sign(r.Int());
        _studying = Math.Clamp(r.Int(), -1, n - 1);
    }
}

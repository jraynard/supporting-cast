using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Work;

/// <summary>
/// Packing rations at the prep table (<see cref="Rations"/>). The cloth's four corners are folded
/// in on cue, each from its own side (A, D, A, D) as the hand comes round; a fold off the cue, or
/// from the wrong side, is a sloppy one. Then the cord: hold F to draw it tight and let go to tie
/// it. It reads only as slack, snug or biting, and snug isn't all one thing: tied at the slack end
/// of snug it looks fine and lets the air in (<see cref="Ration.LooseWrap"/>, never shown);
/// pulled to biting it squashes what's inside.
/// </summary>
public sealed class PackSession : WorkSession
{
    public const int Folds = 4;

    /// <summary>Seconds the hand takes to come round for each fold.</summary>
    public const float Beat = 0.8f;

    /// <summary>How fast the cord tightens while F is held, a step: taut in a second and a half.</summary>
    public const float PullPerStep = 1f / (1.5f * SimClock.TicksPerSecond);

    /// <summary>Where the cord reads as snug, and as biting.</summary>
    public const float SnugAt = 0.45f, BitingAt = 0.9f;

    /// <summary>Tied below this it's loose, though it reads as snug.</summary>
    public const float SoundAt = 0.62f;

    public Entity Table { get; set; } = Entity.Null;

    /// <summary>Corners folded so far.</summary>
    public int Folded { get; private set; }

    /// <summary>How each fold landed, for the drawing: 0 sloppy to 1 crisp.</summary>
    public float[] FoldMarks { get; private set; } = new float[Folds];

    /// <summary>How tight the cord's drawn, 0..1+.</summary>
    public float Tension { get; private set; }

    /// <summary>Tied off (the session ends).</summary>
    public bool Tied { get; private set; }

    /// <summary>Hidden: tied slack. Read only by <see cref="Finish"/>.</summary>
    internal bool Loose { get; private set; }

    private bool _pulling;

    public float Sweep => Triangle(StepsDone, Beat);

    public bool OnCue => Sweep > 0.75f;

    /// <summary>The side the next corner folds from: -1 (A) or +1 (D).</summary>
    public int NextSide => Folded % 2 == 0 ? -1 : 1;

    /// <summary>All four corners in: the cord's next.</summary>
    public bool Wrapped => Folded >= Folds;

    /// <summary>How the cord feels: all a packer has to go on.</summary>
    public string Feel => Tension < SnugAt ? "slack" : Tension < BitingAt ? "snug" : "biting";

    public override int3? Focus(Simulation sim) => sim.World.IsAlive(Table) && sim.World.TryGet<Thing>(Table, out var t) ? t.Cell : null;

    public override Skill Skill => Skill.Cooking;
    public override float Xp => 15f;
    public override string Title => "Packing rations";

    public override bool StillValid(Simulation sim) =>
        Rations.IsPrepTable(sim, Table) && Rations.PortionsOn(sim, Worker) >= Rations.Portions
        && AvatarSystem.InReach(sim, sim.World.Get<Position>(Worker).Cell, Table);

    protected override void Axis(int value)
    {
        if (value == 0 || Wrapped)
            return;
        // A crisp fold is on the cue and from its side; anything else still folds, badly.
        var mark = OnCue ? (value == NextSide ? 1f : 0.4f) : value == NextSide ? 0.45f : 0.15f;
        FoldMarks[Folded] = mark;
        Sample(mark);
        Folded++;
    }

    protected override void Step()
    {
        if (!Wrapped || Tied)
            return;
        if (Held)
        {
            _pulling = true;
            Tension = Math.Min(1.2f, Tension + PullPerStep * (1f + Hands.Tremor));
        }
        else if (_pulling)
        {
            // Let go: it's tied where it was.
            Tied = true;
            Loose = Tension < SoundAt;
            var tie = Tension < SnugAt ? 0.2f : Tension < SoundAt ? 0.75f : Tension < BitingAt ? 1f : 0.45f;
            Sample(tie);
            Done = true;
        }
    }

    public override void Finish(Simulation sim)
    {
        var item = Rations.Make(sim, Worker, Quality, Loose);
        if (item.IsNull)
            return;
        var q = Crafting.QualityLabel(sim.World.Get<Crafted>(item).Quality);
        var tasting = sim.World.Get<Ration>(item).Flavour.Words();
        sim.Messages.Add(MessageKind.Info, $"You tie off a {q} bundle of rations, {tasting}. Set it out in the inn to sell.", sim.World.Get<Thing>(Table).Cell);
    }

    protected override void Cloned() => FoldMarks = (float[])FoldMarks.Clone();

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Table);
        w.Int(Folded);
        foreach (var m in FoldMarks)
            w.Float(m);
        w.Float(Tension);
        w.Bool(Tied);
        w.Bool(Loose);
        w.Bool(_pulling);
    }

    protected override void ReadState(SaveReader r)
    {
        Table = r.Entity();
        Folded = Math.Clamp(r.Int(), 0, Folds);
        for (var i = 0; i < Folds; i++)
            FoldMarks[i] = Math.Clamp(r.Float(), 0f, 1f);
        Tension = Math.Clamp(r.Float(), 0f, 1.2f);
        Tied = r.Bool();
        Loose = r.Bool();
        _pulling = r.Bool();
    }
}

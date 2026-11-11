using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Saving;
using SupportingCast.Sim.Work;

namespace SupportingCast.Sim.Healing;

/// <summary>
/// Casting a mending on a wound you carry: its runes come up one after another, and each is
/// played as it reaches the mark (A for L, D for R, F for F). A rune played true is a strong
/// strand of the spell; a wrong key, an early one or a missed cue is a weak one. The cast's
/// quality is how much of it held. Whether it was the right mending for the wound is only
/// known when it lands (<see cref="Empathy.Cast"/>).
/// </summary>
public sealed class CastSession : WorkSession
{
    /// <summary>Steps before the first rune, and between runes.</summary>
    public const int Lead = 60, Gap = 50;

    /// <summary>Steps either side of the mark that count as on time, before skill widens it.</summary>
    public const int Window = 9;

    public MendingDef Mending { get; set; } = null!;
    public int Wound { get; set; }

    /// <summary>The wound as it feels, when the cast began (for the title and the log).</summary>
    public string Feels { get; set; } = "";

    /// <summary>Runes played so far (hit or missed).</summary>
    public int Resolved { get; private set; }

    /// <summary>Hit (true) or missed, per rune played.</summary>
    public bool[] Hits { get; private set; } = [];

    public int WindowSteps => (int)(Window * Hands.Tolerance);
    public int DueOf(int rune) => Lead + rune * Gap;

    public override Skill Skill => Skill.Medicine;
    public override float Xp => 25f;
    public override string Title => $"{Mending.Label}: {Feels}";

    public static string? Problem(Simulation sim, Entity caster, int wound, MendingDef mending)
    {
        var w = sim.World;
        if (!w.TryGet<Mana>(caster, out var mana))
            return "you haven't the gift";
        if (Empathy.Find(sim, caster, wound) == null)
            return "you don't carry that wound";
        if (mana.Current < mending.ManaCost)
            return $"you need {mending.ManaCost:F0} mana (you have {mana.Current:F0}); meditate";
        return null;
    }

    public static void TryStart(Simulation sim, Entity caster, int wound, MendingDef mending)
    {
        if (Problem(sim, caster, wound, mending) is { } why)
        {
            sim.Messages.Add(MessageKind.Info, $"Can't cast: {why}.", sim.LocationOf(caster));
            return;
        }
        var inj = Empathy.Find(sim, caster, wound)!;
        WorkSystem.Start(sim, caster, new CastSession { Mending = mending, Wound = wound, Feels = Empathy.Feel(sim, inj.Type) });
    }

    public override bool StillValid(Simulation sim) =>
        sim.World.TryGet<Mana>(Worker, out var mana) && mana.Current >= Mending.ManaCost;

    protected internal override void Begin() => Hits = new bool[Mending.Runes.Length];

    protected override void Press() => Play('F');

    protected override void Axis(int value)
    {
        if (value != 0)
            Play(value < 0 ? 'L' : 'R');
    }

    private void Play(char rune)
    {
        if (Resolved >= Mending.Runes.Length)
            return;
        var off = StepsDone - DueOf(Resolved);
        if (off < -2 * WindowSteps)
            return; // far too early to count: a stray key
        Resolve(Math.Abs(off) <= WindowSteps && rune == Mending.Runes[Resolved]);
    }

    private void Resolve(bool hit)
    {
        Hits[Resolved++] = hit;
        Sample(hit ? 1f : 0f);
        Done = Resolved >= Mending.Runes.Length;
    }

    protected override void Step()
    {
        if (Resolved < Mending.Runes.Length && StepsDone > DueOf(Resolved) + WindowSteps)
            Resolve(false); // the cue went by
    }

    public override void Finish(Simulation sim)
    {
        var at = sim.LocationOf(Worker);
        if (Empathy.Cast(sim, Worker, Wound, Mending, Quality))
            sim.Messages.Add(MessageKind.Good, Quality >= 0.75f
                ? $"The {Mending.Label.ToLowerInvariant()} takes hold, and {Feels} eases."
                : $"The {Mending.Label.ToLowerInvariant()} takes hold, weakly: {Feels}, dulled a little.", at);
        else
            sim.Messages.Add(MessageKind.Warning, $"The {Mending.Label.ToLowerInvariant()} finds nothing to take hold of. The mana's spent.", at);
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Def(Mending);
        w.Int(Wound);
        w.String(Feels);
        w.Int(Resolved);
        w.Int(Hits.Length);
        foreach (var h in Hits)
            w.Bool(h);
    }

    protected override void ReadState(SaveReader r)
    {
        Mending = r.Def<MendingDef>();
        Wound = r.Int();
        Feels = r.String();
        Resolved = r.Int();
        Hits = new bool[r.Count(64)];
        for (var i = 0; i < Hits.Length; i++)
            Hits[i] = r.Bool();
        if (Hits.Length != Mending.Runes.Length || Resolved < 0 || Resolved > Hits.Length)
            throw new SaveException("Corrupt save: a cast's runes don't match its mending");
    }
}

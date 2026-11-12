using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Healing;

/// <summary>
/// Weaving a mending (docs/DESIGN.md, "Healer"): runes keyed one at a time (1 to 6) on the beat of
/// the empath's own heart, then F to let it go. Which mending it makes is the mix of runes woven
/// (<see cref="Weaving"/>), known only once it's let go; the empath's book says which mix makes
/// what, and a new mix that works is written in. A rune keyed on the beat is a strong strand, off
/// it a weak one, and the cast's quality is how much of it held. Pain makes the heart race and
/// stumble. Esc lets the weave go with nothing spent. Woven <see cref="IntoCharm"/>, it's knotted
/// into a strip of linen to keep (<see cref="Charms"/>) instead of landing on a wound.
/// </summary>
public sealed class CastSession : WorkSession
{
    /// <summary>Steps to the first beat, and between beats when untroubled.</summary>
    public const int Lead = 60, Beat = 54;

    /// <summary>Steps either side of a beat that count as on it, before skill widens it.</summary>
    public const int Window = 8;

    /// <summary>Beats a weave can be held before it slips away.</summary>
    public const int MaxBeats = 30;

    /// <summary>The wound it's meant for (0 for none), and how it felt when the weave began.</summary>
    public int Wound { get; set; }

    /// <summary>Into a charm, to keep: it lands on no one now.</summary>
    public bool IntoCharm { get; set; }
    public string Feels { get; set; } = "";

    /// <summary>The pain carried when the weave began, 0..1: the heart races and stumbles with it.</summary>
    public float Pain { get; set; }

    /// <summary>The runes there are to key (the defs' runes, in key order).</summary>
    public IReadOnlyList<RuneDef> Palette { get; set; } = [];

    /// <summary>Runes woven so far, as indices into <see cref="Palette"/>, and whether each was keyed on the beat.</summary>
    public List<int> Woven { get; private set; } = [];
    public List<bool> Strong { get; private set; } = [];

    /// <summary>The beat before and the beat to come, by step.</summary>
    public int PrevBeat { get; private set; } = int.MinValue / 2;
    public int NextBeat { get; private set; }

    /// <summary>Beats gone by.</summary>
    public int Beats { get; private set; }

    /// <summary>Let go with F (rather than slipping away or stopped).</summary>
    public bool Released { get; private set; }

    /// <summary>The step the last rune was keyed on, for the scene's flash (not saved: it changes nothing).</summary>
    public int LastWovenAt { get; private set; } = int.MinValue / 2;

    private int _usedBeat = int.MinValue;

    public int WindowSteps => (int)(Window * Hands.Tolerance);

    public override Skill Skill => Skill.Medicine;
    public override float Xp => 25f;
    public override string Title => IntoCharm ? "Weaving a charm" : Wound == 0 || Feels.Length == 0 ? "Weaving" : $"Weaving: {Feels}";

    public static string? Problem(Simulation sim, Entity caster, int wound, bool intoCharm = false)
    {
        var w = sim.World;
        if (!w.TryGet<Mana>(caster, out var mana))
            return "you haven't the gift";
        if (intoCharm && Systems.Bandaging.BandagesOn(sim, caster) == 0)
            return "you need a strip of linen to knot it into (a bandage)";
        if (wound != 0 && Empathy.Find(sim, caster, wound) == null)
            return "you don't carry that wound";
        if (mana.Current < 1f)
            return "you've no mana left; meditate";
        return null;
    }

    public static void TryStart(Simulation sim, Entity caster, int wound, bool intoCharm = false)
    {
        if (intoCharm)
            wound = 0;
        if (Problem(sim, caster, wound, intoCharm) is { } why)
        {
            sim.Messages.Add(MessageKind.Info, $"Can't weave: {why}.", sim.LocationOf(caster));
            return;
        }
        var feels = wound != 0 && Empathy.Find(sim, caster, wound) is { } inj ? Empathy.Feel(sim, inj.Type) : "";
        var pain = sim.World.TryGet<Health>(caster, out var h) ? Math.Clamp(h.Pain, 0f, 1f) : 0f;
        WorkSystem.Start(sim, caster, new CastSession { Wound = wound, Feels = feels, Pain = pain, Palette = sim.Defs.AllRunes, IntoCharm = intoCharm });
    }

    public override bool StillValid(Simulation sim) => sim.World.Has<Mana>(Worker);

    protected internal override void Begin() => NextBeat = Lead;

    protected override void Cloned()
    {
        Woven = [.. Woven];
        Strong = [.. Strong];
    }

    /// <summary>A racing, stumbling heart in pain: shorter beats, coming early or late.</summary>
    private int BeatLength() =>
        Math.Max(24, (int)(Beat * (1f - 0.3f * Pain) * (1f + (Rand.NextFloat() * 2f - 1f) * 0.35f * Pain)));

    protected override void Rune(int index)
    {
        if (index < 0 || index >= Palette.Count || Woven.Count >= MendingDef.MaxRunes)
            return;
        // On the nearer beat, and only one rune to a beat.
        var nearer = StepsDone - PrevBeat <= NextBeat - StepsDone ? PrevBeat : NextBeat;
        var strong = Math.Abs(StepsDone - nearer) <= WindowSteps && nearer != _usedBeat;
        if (strong)
            _usedBeat = nearer;
        Woven.Add(index);
        Strong.Add(strong);
        LastWovenAt = StepsDone;
        Sample(strong ? 1f : 0.3f);
    }

    protected override void Press()
    {
        if (Woven.Count == 0)
            return;
        Released = true;
        Done = true;
    }

    protected override void Step()
    {
        if (StepsDone <= NextBeat + WindowSteps)
            return;
        PrevBeat = NextBeat;
        NextBeat += BeatLength();
        if (++Beats < MaxBeats)
            return;
        if (Woven.Count == 0)
            Failed = "The weave slips away before it's begun.";
        else
            Done = true; // held too long: it goes, as it is
    }

    public override void Finish(Simulation sim)
    {
        var w = sim.World;
        var at = sim.LocationOf(Worker);
        var runes = Woven.Select(i => Palette[i]).ToList();
        var woven = Weaving.Match(sim.Defs, runes);
        var cost = Weaving.Cost(woven, runes.Count);
        ref var mana = ref w.Get<Mana>(Worker);
        if (mana.Current < cost - 0.01f)
        {
            mana.Current = 0f;
            sim.Messages.Add(MessageKind.Warning, "Your mana gives out before the weave is whole, and it unravels.", at);
            return;
        }
        mana.Current -= cost;
        if (woven is not { } made)
        {
            sim.Messages.Add(MessageKind.Warning, "The runes won't hold together. The weave fizzles, and the mana's spent.", at);
            return;
        }
        var m = made.Mending;
        if (!w.Has<Spellbook>(Worker))
            w.Add(Worker, new Spellbook());
        if (w.Get<Spellbook>(Worker).Known.Add(m))
            sim.Messages.Add(MessageKind.Good, $"A mending you didn't know: {m.Label}. You write its runes into your book.", at);
        var label = m.Label.ToLowerInvariant();
        if (IntoCharm)
        {
            if (Charms.Make(sim, Worker, m, made.Extra, Quality).IsNull)
                sim.Messages.Add(MessageKind.Warning, $"There's no linen to hand to knot the {label} into, and it slips away.", at);
            else
                sim.Messages.Add(MessageKind.Good, Quality >= 0.75f
                    ? $"You knot the {label} into a charm. It holds well."
                    : $"You knot the {label} into a charm. Some strands are loose.", at);
            return;
        }
        if (!Empathy.Cast(sim, Worker, Wound, m, Quality, made.Extra))
            sim.Messages.Add(MessageKind.Warning, $"The {label} finds nothing to take hold of. The mana's spent.", at);
        else if (m.Targetless)
            sim.Messages.Add(MessageKind.Good, $"The {label} settles over you. Your hands are steadier.", at);
        else
            sim.Messages.Add(MessageKind.Good, Quality >= 0.75f
                ? $"The {label} takes hold, and {Feels} eases."
                : $"The {label} takes hold, weakly: {Feels}, dulled a little.", at);
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Int(Wound);
        w.String(Feels);
        w.Float(Pain);
        w.Int(Palette.Count);
        foreach (var r in Palette)
            w.Def(r);
        w.Int(Woven.Count);
        for (var i = 0; i < Woven.Count; i++)
        {
            w.Int(Woven[i]);
            w.Bool(Strong[i]);
        }
        w.Int(PrevBeat);
        w.Int(NextBeat);
        w.Int(Beats);
        w.Bool(Released);
        w.Int(_usedBeat);
        w.Bool(IntoCharm);
    }

    protected override void ReadState(SaveReader r)
    {
        if (r.Version < 4)
        {
            // A cast from before weaving (a mending's runes played on cue): it lets go at once, unspent.
            r.Def<MendingDef>();
            Wound = r.Int();
            Feels = r.String();
            r.Int();
            var played = r.Count(64);
            for (var i = 0; i < played; i++)
                r.Bool();
            Failed = "The old weave slips away.";
            return;
        }
        Wound = r.Int();
        Feels = r.String();
        Pain = r.Float();
        var palette = new RuneDef[r.Count(9)];
        for (var i = 0; i < palette.Length; i++)
            palette[i] = r.Def<RuneDef>();
        Palette = palette;
        var n = r.Count(MendingDef.MaxRunes);
        for (var i = 0; i < n; i++)
        {
            var rune = r.Int();
            if (rune < 0 || rune >= palette.Length)
                throw new SaveException("Corrupt save: a weave's rune is out of range");
            Woven.Add(rune);
            Strong.Add(r.Bool());
        }
        PrevBeat = r.Int();
        NextBeat = r.Int();
        Beats = r.Int();
        Released = r.Bool();
        _usedBeat = r.Int();
        if (r.Version >= 5)
            IntoCharm = r.Bool();
    }
}

using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Healing;

/// <summary>
/// Weaving a mending (docs/DESIGN.md, "Empath"): runes keyed one at a time (1 to 6) on the beat of
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

    /// <summary>Begun at a body to call it back (F at the body, <see cref="Afterlife.Revival.BeginCalling"/>): whose. The scene shows the runes it wants.</summary>
    public string CallingFor { get; set; } = "";

    /// <summary>
    /// The body a Calling back is for (<see cref="CallingFor"/>). The rite is for two: no rune takes
    /// till someone kneels and holds them (<see cref="Afterlife.HoldSession"/>), and each rune keyed
    /// is a pull of the grave the keeper has to brace against.
    /// </summary>
    public Entity Body { get; set; } = Entity.Null;

    /// <summary>A Calling with nobody holding the body yet: the runes won't take.</summary>
    public bool WantsKeeper => TakesHelper && Partner.IsNull;

    /// <summary>Holds the keeper kept, and slipped (each slip costs the rune it came on).</summary>
    public int Holds { get; private set; }
    public int Slips { get; private set; }

    /// <summary>The step the keeper last held or slipped, and which (for the scene); -1 before any.</summary>
    public int LastHoldStep { get; private set; } = -1;
    public bool LastSlipped { get; private set; }

    public override bool TakesHelper => CallingFor.Length > 0 && !Body.IsNull;

    /// <summary>The weave's quality as the keeper held it: each rune whose pull slipped loses its share.</summary>
    public float KeptQuality => TakesHelper ? Quality * Math.Max(0f, 1f - Slips / (float)Math.Max(1, Woven.Count)) : Quality;

    public override string AskForHand => $"Hold {CallingFor} for me, someone! I can't call them back alone.";

    public override string HelpLine => "I've got them. Call them back.";

    public override HelperSession? HelperFor(Simulation sim, Entity helper) =>
        TakesHelper ? new Afterlife.HoldSession { Body = Body, BodyName = CallingFor } : null;

    public override int3? Focus(Simulation sim) =>
        TakesHelper && sim.World.IsAlive(Body) && sim.World.Has<Position>(Body) ? sim.LocationOf(Body) : base.Focus(sim);

    /// <summary>An AI keeper holds steady, now and then slipping (less as they're steadier on their feet).</summary>
    protected internal override Beat AiAnswer(Simulation sim, Entity helper)
    {
        var level = Systems.SkillRules.LevelOf(sim, helper, Skill.Medicine);
        return sim.Rand.NextFloat() < Math.Max(0.04f, 0.14f - 0.01f * level)
            ? new Beat(BeatKind.Slip, 0f)
            : new Beat(BeatKind.Held, Math.Clamp(0.55f + 0.03f * level + sim.Rand.Range(-0.1f, 0.1f), 0.3f, 0.9f));
    }

    /// <summary>The keeper's answer to a rune's pull, sent in by the sim: a slip takes that rune's worth.</summary>
    protected override void Received(Beat beat)
    {
        switch (beat.Kind)
        {
            case BeatKind.Held:
                Holds++;
                (LastHoldStep, LastSlipped) = (StepsDone, false);
                break;
            case BeatKind.Slip:
                Slips++;
                for (var i = Strong.Count - 1; i >= 0; i--)
                    if (Strong[i])
                    {
                        Strong[i] = false;
                        break;
                    }
                (LastHoldStep, LastSlipped) = (StepsDone, true);
                break;
        }
    }

    /// <summary>Begun lying downed: the beat's window narrows to <see cref="DownedWindow"/> of itself.</summary>
    public bool Downed { get; set; }

    /// <summary>The share of the beat's window left to an empath weaving on the ground.</summary>
    public const float DownedWindow = 0.6f;

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

    /// <summary>The step the last rune was keyed on: the scene's flash, and a Calling's last pull (<see cref="StillPulling"/>).</summary>
    public int LastWovenAt { get; private set; } = int.MinValue / 2;

    private int _usedBeat = int.MinValue;

    public int WindowSteps => (int)(Window * Hands.Tolerance * (Downed ? DownedWindow : 1f));

    /// <summary>A mending on yourself goes on downed, harder (<see cref="Downed"/>); a charm waits till you're up.</summary>
    public override bool WhileDowned => !IntoCharm;

    public override Skill Skill => Skill.Medicine;
    public override float Xp => 25f;
    public override string Title => CallingFor.Length > 0 ? $"Calling {CallingFor} back" : IntoCharm ? "Weaving a charm" : Wound == 0 || Feels.Length == 0 ? "Weaving" : $"Weaving: {Feels}";

    public static string? Problem(Simulation sim, Entity caster, int wound, bool intoCharm = false)
    {
        var w = sim.World;
        if (!w.TryGet<Mana>(caster, out var mana))
            return "you haven't the gift";
        if (intoCharm && w.TryGet<Creature>(caster, out var c) && c.Downed)
            return "not lying down; mend yourself first";
        if (intoCharm && Systems.Bandaging.BandagesOn(sim, caster) == 0)
            return "you need a strip of linen to knot it into (a bandage)";
        if (wound != 0 && Empathy.Find(sim, caster, wound) == null)
            return "you don't carry that wound";
        if (mana.Current < 1f)
            return "you've no mana left; meditate";
        return null;
    }

    /// <summary>How much a rune altar within reach steadies a weave (0 if none): the share of the caster's pain it takes off.</summary>
    public static float AltarHelp(Simulation sim, Entity caster)
    {
        var best = 0f;
        foreach (var e in Systems.Bandaging.Reachable(sim, sim.World.Get<Position>(caster).Cell))
            if (sim.World.Get<Thing>(e).Def.Building is { Weaving: > 0 } b)
                best = Math.Max(best, Math.Clamp(b.Weaving, 0f, 0.9f));
        return best;
    }

    public static void TryStart(Simulation sim, Entity caster, int wound, bool intoCharm = false, string callingFor = "", Entity? body = null)
    {
        if (intoCharm)
            wound = 0;
        if (Problem(sim, caster, wound, intoCharm) is { } why)
        {
            sim.Messages.Tell(caster, MessageKind.Info, $"Can't weave: {why}.", sim.LocationOf(caster));
            return;
        }
        var feels = wound != 0 && Empathy.Find(sim, caster, wound) is { } inj ? Empathy.Feel(sim, inj.Type) : "";
        var pain = sim.World.TryGet<Health>(caster, out var h) ? Math.Clamp(h.Pain, 0f, 1f) : 0f;
        // At the rune altar the heart steadies: the pain carried stirs the beat less.
        pain *= 1f - AltarHelp(sim, caster);
        var downed = sim.World.TryGet<Creature>(caster, out var c) && c.Downed;
        WorkSystem.Start(sim, caster, new CastSession { Wound = wound, Feels = feels, Pain = pain, Palette = sim.Defs.AllRunes, IntoCharm = intoCharm, Downed = downed, CallingFor = callingFor, Body = callingFor.Length > 0 ? body ?? Entity.Null : Entity.Null });
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
        if (index < 0 || index >= Palette.Count || Woven.Count >= MendingDef.MaxRunes || WantsKeeper)
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
        if (TakesHelper)
            Send(BeatKind.Call, strong ? 1f : 0.5f); // the grave pulls back as the rune goes in
    }

    /// <summary>A click lets the weave go only in the circle (the picture's round middle): a stray one by a rune doesn't.</summary>
    protected override void Clicked(float? u, float? v)
    {
        if (u is { } x && v is { } y && (x - 0.5f) * (x - 0.5f) + (y - 0.5f) * (y - 0.5f) <= 0.25f)
            Press();
    }

    /// <summary>A Calling's last rune still pulling at the keeper: it can't be let go till the pull's through.</summary>
    public bool StillPulling => TakesHelper && StepsDone - LastWovenAt <= Afterlife.HoldSession.PeakAt + 2 * Afterlife.HoldSession.PeakHalf + PullGrace;

    /// <summary>Steps past the last pull's peak a Calling waits for the keeper's answer to reach it.</summary>
    public const int PullGrace = 45;

    protected override void Press()
    {
        if (Woven.Count == 0 || StillPulling)
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
        if (WantsKeeper)
            return; // waiting for someone to hold them: the weave isn't begun, so it can't slip
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
            sim.Messages.Tell(Worker, MessageKind.Warning, "Your mana gives out before the weave is whole, and it unravels.", at);
            return;
        }
        mana.Current -= cost;
        if (woven is not { } made)
        {
            sim.Messages.Tell(Worker, MessageKind.Warning, "The runes won't hold together. The weave fizzles, and the mana's spent.", at);
            return;
        }
        var m = made.Mending;
        if (!w.Has<Spellbook>(Worker))
            w.Add(Worker, new Spellbook());
        if (w.Get<Spellbook>(Worker).Known.Add(m))
            sim.Messages.Tell(Worker, MessageKind.Good, $"A mending you didn't know: {m.Label}. You write its runes into your book.", at);
        var label = m.Label.ToLowerInvariant();
        if (IntoCharm)
        {
            if (Charms.Make(sim, Worker, m, made.Extra, Quality).IsNull)
                sim.Messages.Tell(Worker, MessageKind.Warning, $"There's no linen to hand to knot the {label} into, and it slips away.", at);
            else
                sim.Messages.Tell(Worker, MessageKind.Good, (Quality >= 0.75f
                    ? $"You knot the {label} into a charm. It holds well."
                    : $"You knot the {label} into a charm. Some strands are loose.")
                    + (Shops.Goods.MyDisplay(sim, Worker) is { IsNull: false } shelf ? $" Put it on the {sim.World.Get<Thing>(shelf).Label} to sell it." : ""), at);
            return;
        }
        if (m.CallsBack && !TakesHelper)
        {
            sim.Messages.Tell(Worker, MessageKind.Warning, "The calling needs someone holding them while you weave it: kneel at the body and begin it there. The mana's spent.", at);
            return;
        }
        if (m.CallsBack)
        {
            Afterlife.Revival.Call(sim, Worker, KeptQuality, TakesHelper ? Partner : Entity.Null); // it says how it went
            return;
        }
        // Woven for no wound in particular (the gift's own Weave), a mending that heals takes hold of
        // the worst one it fits, as a torn charm does: a playtester knitted with a cut to mend and
        // was told it found nothing.
        var wound = Wound;
        var feels = Feels;
        if (wound == 0 && !m.Targetless && Charms.Target(sim, Worker, m) is var fit and not 0)
        {
            wound = fit;
            feels = Empathy.Feel(sim, Empathy.Find(sim, Worker, fit)!.Type);
        }
        if (!Empathy.Cast(sim, Worker, wound, m, Quality, made.Extra))
            sim.Messages.Tell(Worker, MessageKind.Warning, Wound != 0
                ? $"The {label} doesn't answer {Feels}: it slides off. The mana's spent. (Your book says what mends what.)"
                : $"The {label} finds no wound on you that it mends. The mana's spent.", at);
        else if (m.Targetless)
            sim.Messages.Tell(Worker, MessageKind.Good, $"The {label} settles over you. Your hands are steadier.", at);
        else
            sim.Messages.Tell(Worker, MessageKind.Good, Quality >= 0.75f
                ? $"The {label} takes hold, and {feels} eases."
                : $"The {label} takes hold, weakly: {feels}, dulled a little.", at);
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
        w.Bool(Downed);
        w.String(CallingFor);
        w.Entity(Body);
        w.Int(Holds);
        w.Int(Slips);
        w.Int(LastHoldStep);
        w.Bool(LastSlipped);
        w.Int(LastWovenAt);
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
        if (r.Version >= 39)
            Downed = r.Bool();
        if (r.Version >= 40)
            CallingFor = r.String(); // came with format 40
        if (r.Version >= 58) // the keeper (format 58)
        {
            Body = r.Entity();
            Holds = r.Int();
            Slips = r.Int();
            LastHoldStep = r.Int();
            LastSlipped = r.Bool();
            LastWovenAt = r.Int();
        }
    }
}

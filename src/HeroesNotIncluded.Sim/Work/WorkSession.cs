using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Work;

/// <summary>
/// How steady and practised the worker's hands are when the job starts. Fixed for the session, so
/// a client can run the same session ahead of the sim: a wound mid-job shows up as an interruption
/// from the sim, not as a change only one side has seen yet.
/// </summary>
public readonly record struct Hands(float Precision, float Tolerance, int Level)
{
    /// <summary>Jitter amplitude on a 0..1 control: none with perfect hands.</summary>
    public float Tremor => (1f - Precision) * 0.35f;

    public static Hands Of(Simulation sim, Entity e, Skill skill)
    {
        var precision = sim.World.TryGet<Health>(e, out var h) ? Math.Clamp(h.Capacity(Capacity.Manipulation), 0.2f, 1f) : 1f;
        var level = SkillRules.LevelOf(sim, e, skill);
        return new Hands(precision, Math.Clamp(0.75f + 0.05f * level, 0.75f, 1.75f), level);
    }
}

public enum WorkInputKind : byte
{
    Press,
    Hold,
    Axis,

    /// <summary>The second direction, up and down (W/S): for moving something about, not just along.</summary>
    Axis2,

    /// <summary>One of a set of keys pressed (1 to 9; Value 0 to 8): an empath's runes.</summary>
    Rune,

    /// <summary>
    /// The game's other action, pressed once (Space): draw the piece out, taste the pot, set the
    /// pin, latch the spring. Its own input so that A/D only ever steer or mean less/more.
    /// Appended last: the kinds go over the wire by number.
    /// </summary>
    Act,

    /// <summary>
    /// The mouse over the mini-game: where it points on its panel, u and v each 0..255 packed into
    /// Value (u in the low byte). Stamped and batched like every other input, so the predicted and
    /// the replayed session see it alike. Appended last: the kinds go over the wire by number.
    /// </summary>
    Point,

    /// <summary>
    /// The mouse's left button pressed on the mini-game: where, packed as <see cref="Point"/> is, with
    /// <see cref="WorkSession.OffPicture"/> set when it was on the window but not its picture. A session
    /// that doesn't care where takes it as a press of F (<see cref="WorkSession.Clicked"/>). Appended last.
    /// </summary>
    Click,
}

/// <summary>One input to a session, stamped with the step it happened on.</summary>
public readonly record struct WorkInput(int Step, WorkInputKind Kind, int Value = 0);

/// <summary>What passes between the two halves of a two-person job (<see cref="Pairs"/>). Saved by number: append only.</summary>
public enum BeatKind : byte
{
    /// <summary>The lead's call: the smith's tap, where the sledge is to fall. Value: how true the tap was, 0..1.</summary>
    Call,

    /// <summary>The helper's answer, on the call. Value: how good, 0..1.</summary>
    Strike,

    /// <summary>The helper's blow with no call to answer: it knocks the work about. Value: how hard, 0..1.</summary>
    Offbeat,

    /// <summary>A call that went unanswered.</summary>
    Missed,

    /// <summary>The keeper held the body against the grave's pull (<see cref="Afterlife.HoldSession"/>). Value: how firmly, 0..1.</summary>
    Held,

    /// <summary>The keeper's hold slipped on a pull: the rune the empath just keyed loses its worth.</summary>
    Slip,
}

/// <summary>One thing that passed between partners (<see cref="WorkSession.Partner"/>): what, and how much.</summary>
public readonly record struct Beat(BeatKind Kind, float Value);

/// <summary>
/// One hands-on job in progress, as a component on the worker. A timing game can't wait on a
/// network round trip, so a session is a pure state machine: <see cref="Advance"/> touches nothing
/// but the session and its own random stream. The client runs a copy ahead for instant feedback
/// (<see cref="WorkPredictor"/>) and sends its inputs in timestamped batches, which the sim replays
/// to the same result. Anything that reads the world belongs in <see cref="StillValid"/> or
/// <see cref="Finish"/>, which only the sim calls. Always stored under this base type.
/// </summary>
public abstract class WorkSession
{
    private float _qualitySum;
    private int _qualitySamples;
    private List<Beat> _outbox = [];

    public Entity Worker { get; internal set; }

    /// <summary>When the job began; also names it, so stale inputs can't reach a later one.</summary>
    public long StartTick { get; internal set; }

    /// <summary>How far play has got. In the sim this trails real time by however late the inputs are.</summary>
    public int StepsDone { get; private set; }

    /// <summary>When the sim last heard from the worker. Sim-only, for spotting a player who's gone.</summary>
    internal long LastInputTick { get; set; }

    public Hands Hands { get; internal set; }

    /// <summary>
    /// The other half of a two-person job (<see cref="Pairs"/>): on a lead's session, who's helping;
    /// on a helper's, whose work it is. Entity.Null for one pair of hands. Set by the sim only.
    /// </summary>
    public Entity Partner { get; internal set; } = Entity.Null;

    /// <summary>
    /// Counts what the sim has handed this session from outside its inputs (a partner's beats). A
    /// copy running ahead (<see cref="WorkPredictor"/>) with an older revision is stale: it's taken
    /// again from the sim's and its unplayed inputs replayed on top.
    /// </summary>
    public int Revision { get; private set; }

    /// <summary>Beats this session's play has sent its partner that the sim hasn't passed on yet.</summary>
    internal IReadOnlyList<Beat> Outbox => _outbox;

    internal void ClearOutbox() => _outbox.Clear();

    /// <summary>Send the partner a beat (from <see cref="Step"/> or an input): the sim passes it on (<see cref="Pairs.Exchange"/>).</summary>
    protected void Send(BeatKind kind, float value = 0f) => _outbox.Add(new Beat(kind, value));

    /// <summary>A beat from the partner, handed over by the sim between steps. Sim-only.</summary>
    internal void Receive(Beat beat)
    {
        Received(beat);
        Revision++;
    }

    /// <summary>The sim's changed something outside the inputs (a partner joined): copies running ahead take it again.</summary>
    internal void Revise() => Revision++;

    /// <summary>What a beat from the partner does: may read and change only the session's own state (and <see cref="Rand"/>).</summary>
    protected virtual void Received(Beat beat) { }

    /// <summary>This work is for two (<see cref="Pairs"/>): someone may join it with <see cref="HelperFor"/>.</summary>
    public virtual bool TakesHelper => false;

    /// <summary>What the lead shouts when they call for a hand (<see cref="AskForHandCommand"/>).</summary>
    public virtual string AskForHand => "A hand here, someone!";

    /// <summary>The helper's own session for someone joining this work, or null if it takes none.</summary>
    public virtual HelperSession? HelperFor(Simulation sim, Entity helper) => null;

    /// <summary>What an AI villager says as it comes to lend a hand (<see cref="AskForHandCommand"/>).</summary>
    public virtual string HelpLine => "Coming! I'll lend a hand.";

    /// <summary>
    /// How an AI villager helping (<see cref="Helping"/>) answers this work's call, a moment after it
    /// (<see cref="Pairs.AnswerMin"/>): steady, never brilliant. Sim-only.
    /// </summary>
    protected internal virtual Beat AiAnswer(Simulation sim, Entity helper) => Pairs.AiBlow(sim, helper, Skill);

    protected internal SimRandom Rand { get; internal set; } = new(1);

    /// <summary>The main input is held down.</summary>
    public bool Held { get; private set; }

    /// <summary>
    /// Where the mouse last pointed on the panel (u across, v down, each 0..1), or null since a key
    /// steered. A session that moves a tool along something pulls it toward this at the tool's own pace
    /// (<see cref="Toward"/>), never jumps it there: a sweep that's felt as it passes must still pass.
    /// </summary>
    public float? PointU { get; private set; }

    /// <inheritdoc cref="PointU"/>
    public float? PointV { get; private set; }

    /// <summary>Let go of where the mouse pointed, till it moves again (a pin set: the next one starts at the bottom).</summary>
    protected void ForgetPoint() => (PointU, PointV) = (null, null);

    public bool Done { get; protected set; }

    /// <summary>Ended without success, with the reason for the log.</summary>
    public string? Failed { get; protected set; }

    public float Quality => _qualitySamples == 0 ? 0f : _qualitySum / _qualitySamples;

    public abstract Skill Skill { get; }

    /// <summary>Experience for finishing.</summary>
    public abstract float Xp { get; }

    public abstract string Title { get; }

    /// <summary>
    /// Where the work is, for drawing: the worker faces it in their working pose (the anvil, the
    /// tub, the stain being scrubbed). Null: no one place.
    /// </summary>
    public virtual int3? Focus(Simulation sim) => null;

    /// <summary>
    /// Goes on while the worker lies downed: the few things a body on the ground can still do (an
    /// empath mending themselves). Everything else stops when they collapse.
    /// </summary>
    public virtual bool WhileDowned => false;

    /// <summary>The target is still there and in reach. Sim-only; checked before every tick.</summary>
    public abstract bool StillValid(Simulation sim);

    /// <summary>Apply the result, once <see cref="Done"/>. Sim-only.</summary>
    public abstract void Finish(Simulation sim);

    /// <summary>
    /// Every tick the session's still valid, before it's finished: land on the world what play has
    /// settled so far (a wound drawn whole while the hands go on to the next). Sim-only; must not
    /// change anything <see cref="Step"/> reads.
    /// </summary>
    public virtual void Settle(Simulation sim) { }

    /// <summary>Stopped before it was done (walked off, Esc, gone quiet): keep what lasts, such as a piece's heat. Sim-only.</summary>
    public virtual void Interrupted(Simulation sim) { }

    /// <summary>Set up from <see cref="Rand"/> and <see cref="Hands"/>, once they're assigned.</summary>
    protected internal virtual void Begin() { }

    /// <summary>One step of play. Must read only this session's state.</summary>
    protected abstract void Step();

    /// <summary>The main input was pressed (not just held).</summary>
    protected virtual void Press() { }

    /// <summary>A direction held: -1, +1, or 0 when let go.</summary>
    protected virtual void Axis(int value) { }

    /// <summary>The second direction held (W -1, S +1, 0 when let go).</summary>
    protected virtual void Axis2(int value) { }

    /// <summary>A numbered key pressed, 0 for 1 (off the network: check the range).</summary>
    protected virtual void Rune(int index) { }

    /// <summary>The game's other action (<see cref="WorkInputKind.Act"/>), once per press.</summary>
    protected virtual void Act() { }

    /// <summary>
    /// The mouse pointing at (u, v) on the mini-game's panel, each 0..1 (<see cref="WorkInputKind.Point"/>).
    /// <see cref="PointU"/> and <see cref="PointV"/> still hold where it was before (null if a key had it).
    /// </summary>
    protected virtual void Pointed(float u, float v) { }

    /// <summary>
    /// The left button pressed at (u, v) on the picture, or with both null off it (on the window's
    /// words and bars). Unless a game minds where, a click is F.
    /// </summary>
    protected virtual void Clicked(float? u, float? v) => Press();

    /// <summary>A <see cref="WorkInputKind.Click"/> value's flag: on the window, off the picture.</summary>
    public const int OffPicture = 1 << 16;

    /// <summary>A pointer's place packed as a <see cref="WorkInputKind.Point"/> value: u and v (0..1) quantised to a byte each.</summary>
    public static int PackPoint(float u, float v) =>
        (int)MathF.Round(Math.Clamp(u, 0f, 1f) * 255f) | (int)MathF.Round(Math.Clamp(v, 0f, 1f) * 255f) << 8;

    /// <summary>
    /// Which way to move something at <paramref name="at"/> to come to <paramref name="target"/>
    /// (the pointer's place, mapped onto the thing's own measure) a step of <paramref name="perStep"/>
    /// at a time: -1, +1, or 0 once it's within half a step (or there's no target).
    /// </summary>
    public static int Toward(float at, float? target, float perStep) =>
        target is not { } t || MathF.Abs(t - at) <= perStep * 0.5f ? 0 : Math.Sign(t - at);

    /// <summary>The pointer's u mapped onto lo..hi of the panel (the thing drawn there), clamped to 0..1; null with no pointer.</summary>
    protected static float? Across(float? u, float lo, float hi) => u is { } x ? Math.Clamp((x - lo) / (hi - lo), 0f, 1f) : null;

    /// <summary>Count one moment or blow of work at quality 0..1.</summary>
    protected void Sample(float quality)
    {
        _qualitySum += Math.Clamp(quality, 0f, 1f);
        _qualitySamples++;
    }

    /// <summary>Play up to step <paramref name="upTo"/>, applying each input at the step it was made.</summary>
    public void Advance(IReadOnlyList<WorkInput> inputs, int upTo)
    {
        var next = 0;
        while (StepsDone < upTo && !Done && Failed == null)
        {
            while (next < inputs.Count && inputs[next].Step <= StepsDone)
                Apply(inputs[next++]);
            // Even an input that ends the job uses up its step: the batch carrying it must be
            // able to name a step past it, or the sim would never get the last blow.
            Step();
            StepsDone++;
        }
    }

    private void Apply(WorkInput input)
    {
        switch (input.Kind)
        {
            case WorkInputKind.Press:
                Press();
                break;
            case WorkInputKind.Hold:
                Held = input.Value != 0;
                break;
            case WorkInputKind.Axis:
                if (input.Value != 0)
                    PointU = null; // the keys have it till the mouse moves again
                Axis(Math.Sign(input.Value));
                break;
            case WorkInputKind.Axis2:
                if (input.Value != 0)
                    PointV = null;
                Axis2(Math.Sign(input.Value));
                break;
            case WorkInputKind.Rune:
                Rune(input.Value);
                break;
            case WorkInputKind.Act:
                Act();
                break;
            case WorkInputKind.Click:
                if ((input.Value & OffPicture) != 0)
                    Clicked(null, null);
                else
                {
                    // A click is where the mouse points, too (it may not have been sent moving there).
                    var (cu, cv) = ((input.Value & 0xFF) / 255f, (input.Value >> 8 & 0xFF) / 255f);
                    (PointU, PointV) = (cu, cv);
                    Clicked(cu, cv);
                }
                break;
            case WorkInputKind.Point:
                // Pointed sees the last place still in PointU/PointV: a game steered by how far the mouse moved reads it there.
                var (u, v) = ((input.Value & 0xFF) / 255f, (input.Value >> 8 & 0xFF) / 255f);
                Pointed(u, v);
                (PointU, PointV) = (u, v);
                break;
        }
    }

    /// <summary>A copy to run ahead on: a shallow copy plus the random stream, and <see cref="Cloned"/> for anything that isn't a value.</summary>
    public WorkSession Clone()
    {
        var copy = (WorkSession)MemberwiseClone();
        copy.Rand = new SimRandom(1) { State = Rand.State };
        copy._outbox = [.. _outbox];
        copy.Cloned();
        return copy;
    }

    /// <summary>On a fresh copy: copy anything that's shared by reference (an array), so the copy and the original don't share it.</summary>
    protected virtual void Cloned() { }

    /// <summary>A triangle wave 0..1..0 over <paramref name="period"/> seconds: a sweep with no trig, so no DetMath needed.</summary>
    protected static float Triangle(int steps, float period)
    {
        var t = steps / (float)SimClock.TicksPerSecond / period;
        var f = t - MathF.Floor(t);
        return f < 0.5f ? f * 2f : 2f - f * 2f;
    }

    protected abstract void WriteState(SaveWriter w);
    protected abstract void ReadState(SaveReader r);

    internal static void Write(SaveWriter w, WorkSession s) => Write(w, s, SaveGame.FormatVersion);

    /// <summary>As a save of <paramref name="format"/> laid it out (tests of reading old saves write them this way).</summary>
    internal static void Write(SaveWriter w, WorkSession s, int format)
    {
        w.String(Modding.SimContent.NameOf(s.GetType()));
        w.Entity(s.Worker);
        w.Long(s.StartTick);
        w.Int(s.StepsDone);
        w.Long(s.LastInputTick);
        w.Float(s.Hands.Precision);
        w.Float(s.Hands.Tolerance);
        w.Int(s.Hands.Level);
        var (a, b, c, d) = s.Rand.State;
        w.UInt(a); w.UInt(b); w.UInt(c); w.UInt(d);
        w.Bool(s.Held);
        w.Bool(s.Done);
        w.NullableString(s.Failed);
        w.Float(s._qualitySum);
        w.Int(s._qualitySamples);
        if (format >= 43)
        {
            w.Float(s.PointU ?? -1f);
            w.Float(s.PointV ?? -1f);
        }
        if (format >= 54)
        {
            w.Entity(s.Partner);
            w.Int(s.Revision);
            w.Int(s._outbox.Count);
            foreach (var beat in s._outbox)
            {
                w.Byte((byte)beat.Kind);
                w.Float(beat.Value);
            }
        }
        s.WriteState(w);
    }

    private static (int Version, Dictionary<string, Func<WorkSession>> Map)? _sessions;

    /// <summary>
    /// Every session by the name it's saved under (<see cref="Modding.SimContent.NameOf"/>): found by
    /// scanning the content (the game's sim and each mod's) for sessions with a parameterless
    /// constructor, so a new one, the game's or a mod's, needs no list. Retired names map on to what
    /// replaced them.
    /// </summary>
    internal static IReadOnlyDictionary<string, Func<WorkSession>> Sessions
    {
        get
        {
            if (_sessions is { } cached && cached.Version == Modding.SimContent.Version)
                return cached.Map;
            var map = new Dictionary<string, Func<WorkSession>>(StringComparer.Ordinal)
            {
                ["DrawSession"] = () => new Healing.RetiredSession("DrawSession"),
                ["FeelSession"] = () => new Healing.RetiredSession("FeelSession"),
            };
            foreach (var type in Modding.SimContent.TypesOf<WorkSession>())
            {
                if (type.GetConstructor(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic, Type.EmptyTypes) == null)
                    continue;
                var name = Modding.SimContent.NameOf(type);
                if (!map.TryAdd(name, () => (WorkSession)Activator.CreateInstance(type, nonPublic: true)!))
                    throw new InvalidOperationException($"Two work sessions are saved as {name}: rename one");
            }
            foreach (var (name, make) in Modding.SimContent.ModSessionAliases)
                map.TryAdd(name, make);
            _sessions = (Modding.SimContent.Version, map);
            return map;
        }
    }

    internal static WorkSession Read(SaveReader r)
    {
        var name = r.String();
        if (!Sessions.TryGetValue(name, out var make))
            throw new SaveException($"Corrupt save: unknown work session {name}");
        var s = make();
        s.Worker = r.Entity();
        s.StartTick = r.Long();
        s.StepsDone = r.Int();
        s.LastInputTick = r.Long();
        s.Hands = new Hands(r.Float(), r.Float(), r.Int());
        s.Rand = new SimRandom(1) { State = (r.UInt(), r.UInt(), r.UInt(), r.UInt()) };
        s.Held = r.Bool();
        s.Done = r.Bool();
        s.Failed = r.NullableString();
        s._qualitySum = r.Float();
        s._qualitySamples = r.Int();
        if (r.Version >= 43) // the mouse in the mini-games (format 43)
        {
            var (u, v) = (r.Float(), r.Float());
            (s.PointU, s.PointV) = (u < 0f ? null : u, v < 0f ? null : v);
        }
        if (r.Version >= 54) // two pairs of hands (format 54)
        {
            s.Partner = r.Entity();
            s.Revision = r.Int();
            for (var n = r.Count(256); n > 0; n--)
            {
                var kind = r.Byte();
                var value = r.Float();
                if (Enum.IsDefined((BeatKind)kind))
                    s._outbox.Add(new Beat((BeatKind)kind, value));
            }
        }
        s.ReadState(r);
        return s;
    }
}

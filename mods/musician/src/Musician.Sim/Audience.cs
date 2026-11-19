using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Adventurers;
using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Modding;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Musician;

/// <summary>
/// How a musician's playing comes across, read from the session as it stands (nothing kept of its own, so nothing to
/// save or drift): whether there's music at all, how lively and how steady, its mood and dance, and its tempo. A
/// carried tune says its own; free play is judged by how it's played, never by "right notes", so a tune of your own
/// draws a crowd as well as one from the book.
/// </summary>
public readonly record struct Performance(bool Playing, float Energy, float Steadiness, string Mood, string Kind, float Tempo)
{
    /// <summary>Free play counts the notes this many session steps back: a few seconds' quiet and the music's stopped.</summary>
    public const int Lately = 4 * 60;

    /// <summary>Lively and steady enough to dance to.</summary>
    public bool Danceable => Playing && Mood is "Lively" or "Stirring" && Steadiness >= 0.5f && Energy >= 0.3f;

    /// <summary>Steadiness under this is sour: no one comes to it, and whoever's there drifts off after a while.</summary>
    public const float SourBelow = 0.35f;

    /// <summary>How far out of tune it is, in cents (its strings', on average): <see cref="SourCents"/> and past is sour, however it's played.</summary>
    public float Detune { get; init; }

    /// <summary>Strings this far out on average (cents) and the music's sour to anyone listening.</summary>
    public const float SourCents = 30f;

    /// <summary>Music too ragged, or too far out of tune, to stand.</summary>
    public bool Sour => Playing && (Steadiness < SourBelow || Detune >= SourCents);

    /// <summary>
    /// The beat as a dancer steps to it, in the musician's session steps: one falls on <c>Origin</c>, and every
    /// <c>Period</c> after. A carried tune's own beat; free play's, the pulse under it from its last note. Null with no music.
    /// </summary>
    public static (float Origin, float Period)? BeatClock(PlaySession s)
    {
        if (s.Carrying != null)
            return (s.Beat, s.StepsPerBeat);
        var onsets = Onsets(s);
        if (onsets.Count < 3)
            return null;
        return (onsets[^1], Rhythm(onsets).Beat);
    }

    /// <summary>
    /// How far off the beat a moment is (sim ticks), signed, nearest beat: what a dancer's step is judged by. Null with no
    /// music to dance to.
    /// </summary>
    public static float? OffBeat(Simulation sim, Entity musician, long tick)
    {
        if (!sim.World.IsAlive(musician) || !sim.World.TryGet<WorkSession>(musician, out var w) || w is not PlaySession s
            || !Of(s).Playing || BeatClock(s) is not { } clock)
            return null;
        var at = tick - s.StartTick - clock.Origin;
        var beats = at / clock.Period;
        return (beats - MathF.Round(beats)) * clock.Period;
    }

    public static Performance Of(PlaySession s) => Heard(s) with { Detune = s.Sourness };

    private static Performance Heard(PlaySession s)
    {
        if (s.Carrying is { } tune)
            return new(true, Math.Clamp(tune.Tempo / 140f, 0.3f, 1f), s.Concentration, tune.Mood, tune.Kind, tune.Tempo * s.Pace);
        // Free: the notes of the last few seconds (a chord's strum counts once), and how well they keep to a pulse.
        var onsets = Onsets(s);
        if (onsets.Count < 3 && !(onsets.Count >= 1 && s.RingingNote != null))
            return new(false, 0f, 0f, "", "", 0f);
        var (steadiness, beat) = Rhythm(onsets);
        var energy = Math.Clamp(onsets.Count / 16f, 0f, 1f);
        var lively = energy >= 0.4f;
        return new(true, energy, steadiness, lively ? "Lively" : "Tender", lively ? "Jig" : "Air", Math.Clamp(SimClock.TicksPerSecond * 60f / beat, 40f, 200f));
    }

    /// <summary>The steps notes began on in the last few seconds, oldest first (notes struck together, once).</summary>
    public static List<int> Onsets(PlaySession s)
    {
        var onsets = new List<int>();
        for (var i = s.Notes.Count - 1; i >= 0 && s.StepsDone - s.Notes[i].Step < Lately; i--)
            if (onsets.Count == 0 || onsets[^1] != s.Notes[i].Step)
                onsets.Add(s.Notes[i].Step);
        onsets.Reverse();
        return onsets;
    }

    /// <summary>The quickest pulse a note may keep to (steps): ten a second.</summary>
    public const float LeastUnit = 6f;

    /// <summary>
    /// The most pulses a gap may span and still fit: past it, any gaps at all would fit a pulse small enough, so a gap
    /// that long counts as half a pulse out.
    /// </summary>
    public const int MostUnits = 4;

    /// <summary>
    /// How well notes keep to a pulse, 0..1, and the beat they keep (steps): a rhythm, not just even notes, so a
    /// drum's doum-tek-tek is as steady as a march, and a flourish doesn't break it. Notes struck within
    /// <see cref="LeastUnit"/> of each other are one (a grace note goes with the note it leads to). For each pulse tried
    /// (each gap, or a half, third or quarter of one), notes coming too soon after the last on it are ornaments, set aside
    /// at a small cost (<see cref="OrnamentCost"/>); the rest are fitted as whole numbers of it, few to a gap
    /// (<see cref="MostUnits"/>). The best fit wins, the larger pulse on a tie; the steadiness is how near the gaps
    /// come to it. The beat is that pulse doubled up to a dancer's step.
    /// </summary>
    public static (float Steadiness, float Beat) Rhythm(IReadOnlyList<int> onsets)
    {
        var merged = new List<int>(onsets.Count);
        foreach (var t in onsets)
            if (merged.Count > 0 && t - merged[^1] < LeastUnit)
                merged[^1] = t;
            else
                merged.Add(t);
        if (merged.Count > 17)
            merged.RemoveRange(0, merged.Count - 17);
        var n = merged.Count;
        if (n < 3)
            return (0.5f, n == 2 ? Math.Max(LeastUnit, merged[1] - merged[0]) : 60f);
        // No pulse finer than half the gap notes usually come at: against one that fine, any jumble fits.
        var gaps = new List<int>(n - 1);
        for (var i = 1; i < n; i++)
            gaps.Add(merged[i] - merged[i - 1]);
        gaps.Sort();
        var finest = gaps[gaps.Count / 2] * FinestShare;
        var (bestScore, bestErr, bestUnit) = (float.MaxValue, 0.5f, 60f);
        for (var g = 1; g < n; g++)
            for (var div = 1; div <= 4; div++)
            {
                var unit = (merged[g] - merged[g - 1]) / (float)div;
                if (unit < LeastUnit)
                    break;
                if (unit < finest)
                    continue;
                var (last, kept, ornaments) = (merged[0], 0, 0);
                float err = 0f, units = 0f, off = 0f;
                for (var i = 1; i < n; i++)
                {
                    var gap = merged[i] - last;
                    if (gap < OrnamentShare * unit)
                    {
                        ornaments++;
                        continue;
                    }
                    var x = gap / unit;
                    var whole = Math.Max(1f, MathF.Round(x));
                    err += whole <= MostUnits ? MathF.Abs(x - whole) : 0.5f;
                    // How far out it came in time, past what a hand's sure of anyway (Grace): what steadiness reads.
                    // (never more than a share of the pulse, or a tiny pulse would forgive any jumble).
                    off += whole <= MostUnits ? Math.Max(0f, MathF.Abs(x - whole) * unit - Math.Min(Grace, unit * GraceShare)) / unit : 0.5f;
                    units += whole;
                    kept++;
                    last = merged[i];
                }
                if (kept < 2)
                    continue;
                var ornamented = OrnamentCost * ornaments / n;
                err = err / kept + ornamented;
                var score = err + 0.005f * units / kept; // as good a fit in fewer, longer pulses is the likelier
                if (score < bestScore)
                    (bestScore, bestErr, bestUnit) = (score, off / kept + ornamented, unit);
            }
        var beat = bestUnit;
        while (beat < 18f)
            beat *= 2f;
        return (Math.Clamp(1f - bestErr * Strictness, 0f, 1f), beat);
    }

    /// <summary>
    /// Steps a gap between notes may come off its pulse and still be dead on (about a fifteenth of a second; a gap carries
    /// both its notes' wobble): how near a steady hand comes anyway. Without it quick notes read as ragged however well they're kept: a playtest's "doing
    /// awesome, and it shows middling at best".
    /// </summary>
    public const float Grace = 4f;

    /// <summary>The most of a pulse the grace may be.</summary>
    public const float GraceShare = 0.25f;

    /// <summary>The finest pulse tried, as a share of the middle gap between notes.</summary>
    public const float FinestShare = 0.5f;

    /// <summary>How much the time past <see cref="Grace"/> (as a share of the pulse, on average) takes off steadiness.</summary>
    public const float Strictness = 5f;

    /// <summary>A note this soon after the last on the pulse (a share of it) is an ornament, between the pulse's notes.</summary>
    public const float OrnamentShare = 0.6f;

    /// <summary>What each ornament costs the fit, as a share of the notes: little, but a tune all ornament has no pulse.</summary>
    public const float OrnamentCost = 0.15f;

    /// <summary>What <paramref name="musician"/> is playing now (nothing, if they're not).</summary>
    public static Performance Of(Simulation sim, Entity musician) =>
        sim.World.IsAlive(musician) && sim.World.TryGet<WorkSession>(musician, out var w) && w is PlaySession s ? Of(s) : default;
}

/// <summary>
/// The musician's audience (<see cref="IPastime"/>): with nothing else to do, whoever hears music clearly goes to it,
/// to dance if it's lively and steady and they've a mind to, or to stand and listen. Off as soon as it stops.
/// </summary>
public sealed class Listening : IPastime
{
    /// <summary>How likely someone is to dance to music that can be danced to, rather than listen.</summary>
    public const float DanceChance = 0.6f;

    public Job? JobFor(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        if (!w.TryGet<Position>(pawn, out var me))
            return null;
        var (musician, performance, bestDistance) = (Entity.Null, default(Performance), int.MaxValue);
        foreach (var e in w.Each<WorkSession>())
        {
            if (e == pawn || w.Get<WorkSession>(e) is not PlaySession s || !w.TryGet<Position>(e, out var at))
                continue;
            var p = Performance.Of(s);
            if (!p.Playing || p.Sour || SpeechSystem.Hear(sim, at.Cell, me.Cell, Volume.Say) != Clarity.Clear)
                continue;
            var d = Sim.Maps.Map.Distance(at.Cell, me.Cell);
            if (d < bestDistance)
                (musician, performance, bestDistance) = (e, p, d);
        }
        if (musician.IsNull)
            return null;
        var dance = performance.Danceable && sim.Rand.NextFloat() < DanceChance;
        var near = w.Get<Position>(musician).Cell;
        // A reel or a march is danced round the musician, where there's floor for a ring; anything else on the spot.
        if (dance && performance.Kind is "Reel" or "March" && Ring(sim, pawn, near) is { Count: >= Festival.DanceRadius * 4 } ring)
            return new AudienceJob(musician, ring[0], dance, sim.Rand.Range(900, 1800), ring);
        var spot = AiQueries.RandomReachableNear(sim, pawn, near, dance ? 3 : 4, publicOnly: w.Has<Adventurer>(pawn), reject: c => c == near);
        return spot is { } cell ? new AudienceJob(musician, cell, dance, sim.Rand.Range(900, 1800)) : null;
    }

    /// <summary>
    /// The ring round a musician to dance on: the cells of the festival's ring about them that can be stood on and
    /// reached, in order round, from the nearest. A ring broken by a wall goes round what's left of it.
    /// </summary>
    public static List<int3> Ring(Simulation sim, Entity pawn, int3 center)
    {
        var here = sim.World.Get<Position>(pawn).Cell;
        var ring = new List<int3>();
        foreach (var c in Festival.Ring(center, Festival.DanceRadius))
            if (sim.Map.InBounds(c) && sim.Map.Standable(sim.Map.Idx(c)) && Navigation.CanReach(sim, pawn, here, c, PathEnd.OnTile))
                ring.Add(c);
        if (ring.Count == 0)
            return ring;
        var start = 0;
        for (var k = 1; k < ring.Count; k++)
            if (Sim.Maps.Map.Distance(ring[k], here) < Sim.Maps.Map.Distance(ring[start], here))
                start = k;
        return [.. ring.Skip(start), .. ring.Take(start)];
    }
}

/// <summary>
/// In a musician's audience: over to a spot near them, then dancing (a hop to the music's beat; round them on a
/// <paramref name="ring"/> to a reel or a march) or listening, facing them, as long as the music lasts (and no longer
/// than <paramref name="most"/> ticks). Gone sour a while, they drift off, and remember that instead.
/// </summary>
public sealed class AudienceJob(Entity musician, int3 spot, bool dance, int most, List<int3>? ring = null) : Job
{
    /// <summary>Ticks in the audience before it's remembered (a few bars aren't an evening's music).</summary>
    public const int Remembered = 600;

    /// <summary>Ticks of sour music they'll stand before drifting off.</summary>
    public const int SourPatience = 3 * SimClock.TicksPerSecond;

    private bool _dancing, _soured;
    private float _tempo;

    /// <summary>Ticks spent hearing the music, and dancing to it, and the sour stretch now, and hearing the tune they asked for.</summary>
    private int _heard, _danced, _sour, _theirs;

    /// <summary>Tipped this time (or had nothing to give), and their tune answered.</summary>
    private bool _tipped, _answered;

    public override bool Interruptible => true;

    /// <summary>The musician: faced, as they're watched.</summary>
    public override Entity Focus => musician;

    public Entity Musician => musician;

    public override bool Dancing => _dancing;
    public override float DanceTempo => _tempo;

    /// <summary>Came to dance (and may be, as long as the music's lively).</summary>
    public bool ToDance => dance;

    /// <summary>Dancing round the musician, not on the spot.</summary>
    public bool InRing => ring != null;

    /// <summary>Drifted off from music gone sour.</summary>
    public bool Soured => _soured;

    /// <summary>A tick of the music heard: false when it's stopped, or been sour too long to stand.</summary>
    private bool Hearing()
    {
        if (!Alive(musician) || Performance.Of(Sim, musician) is not { Playing: true } p)
            return false;
        _sour = p.Sour ? _sour + 1 : 0;
        if (_sour > SourPatience)
        {
            _soured = true;
            return false;
        }
        _dancing = dance && p.Danceable; // a tune gone ragged or tender: they stop and listen
        _tempo = p.Tempo;
        Report = _dancing ? "Dancing to the music" : "Listening to the music";
        _heard++;
        if (_dancing)
            _danced++;
        // Heard a good while: a coin in the hat. Their own tune, played through a while: a good deal more.
        if (!_tipped && _heard >= Remembered)
        {
            _tipped = true;
            Takings.Tip(Sim, Pawn, musician, _danced >= Remembered / 2);
        }
        if (!_answered && Takings.Wants(Sim, Pawn, musician) is { } tune
            && Sim.World.Get<WorkSession>(musician) is PlaySession { Carrying: { } playing } && playing.Name == tune && ++_theirs >= Remembered / 2)
        {
            _answered = true;
            Takings.Answered(Sim, Pawn, musician);
        }
        return true;
    }

    protected override IEnumerable<JobStatus> Run()
    {
        Report = dance ? "Off to dance" : "Off to hear the music";
        foreach (var s in GoTo(spot, PathEnd.OnTile)) yield return s;
        if (HasFailed)
            yield break;
        // Come to the music, someone may call for a tune of the musician's (an adventurer, more likely).
        if (Takings.Wants(Sim, Pawn, musician) == null && Alive(musician) && Sim.World.TryGet<WorkSession>(musician, out var w) && w is PlaySession { Book.Count: > 0 } playing
            && Sim.Rand.NextFloat() < Takings.AskChance * (Sim.World.Has<Adventurer>(Pawn) ? 2f : 1f))
            Takings.Ask(Sim, Pawn, musician, playing.Book[Sim.Rand.NextInt(playing.Book.Count)]);
        if (ring is { Count: > 0 })
        {
            // Round and round, a cell at a time, while the music lasts (on the spot, hopping, while it's too tender to dance round).
            for (var k = 1; _heard < most; k++)
            {
                foreach (var s in GoTo(ring[k % ring.Count], PathEnd.OnTile))
                {
                    if (!Hearing())
                        yield break;
                    yield return s;
                }
                if (HasFailed)
                    yield break;
            }
            yield break;
        }
        while (_heard < most && Hearing())
            yield return JobStatus.Running;
    }

    /// <summary>However it ends (the music over, gone sour, or called away to work), enough of it heard is remembered.</summary>
    protected override void OnEnd(bool succeeded)
    {
        _dancing = false;
        if (_soured)
            MoodSystem.Remember(Sim, Pawn, Sim.Defs.ThoughtOrNull("SourMusic"));
        else if (_heard >= Remembered)
            MoodSystem.Remember(Sim, Pawn, Sim.Defs.ThoughtOrNull(_danced >= Remembered / 2 ? "Danced" : "HeardMusic"));
        // (their tune answered is remembered as it's answered: Takings.Answered)
    }
}

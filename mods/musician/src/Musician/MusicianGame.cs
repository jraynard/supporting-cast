using Brutal.GlfwApi;
using Brutal.ImGuiApi;
using HeroesNotIncluded.Mods;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Work;
using HeroesNotIncluded.Ui;
using Narsil.Audio;
using Narsil.Platform;
using Narsil.Ui;

namespace HeroesNotIncluded.Musician;

/// <summary>
/// The musician's game half: the note keys, the playing window, and the notes heard, your own the moment
/// they're pressed (from the predicted session) and anyone else's from where they stand.
/// </summary>
public sealed class MusicianGame : IGameMod
{
    /// <summary>The note keys' controls, the scale's first degree first.</summary>
    private static string[] _notes = [];

    private static string _octave = "", _sharp = "", _breathe = "", _chord = "", _thank = "";

    public void Register(GameRegistry r)
    {
        GlfwKey[] keys = [GlfwKey.Number1, GlfwKey.Number2, GlfwKey.Number3, GlfwKey.Number4, GlfwKey.Number5, GlfwKey.Number6, GlfwKey.Number7, GlfwKey.Number8];
        // Off X: X is the other action (Space), which turns between free play and the book.
        PadButton[] pad = [PadButton.DpadLeft, PadButton.DpadDown, PadButton.DpadRight, PadButton.DpadUp, PadButton.LT, PadButton.Y, PadButton.LB, PadButton.RB];
        _notes = [.. Enumerable.Range(0, 8).Select(i => r.Control($"note{i + 1}", $"Note {i + 1}", [Binding.Key(keys[i])], [Binding.Pad(pad[i])], "Music"))];
        _octave = r.Control("octave", "The octave above", [Binding.Key(GlfwKey.LeftShift)], [Binding.Pad(PadButton.L3)], "Music");
        // Not Ctrl: Ctrl with a number is the game's speed.
        _sharp = r.Control("sharp", "A semitone up", [Binding.Key(GlfwKey.LeftAlt)], [Binding.Pad(PadButton.RT)], "Music");
        // Live only while the hands carry a tune (the note keys aren't), so it may share a key with the octave.
        _breathe = r.Control("breathe", "Take a breath or turn the bow (hold), while your hands carry a tune", [Binding.Key(GlfwKey.LeftShift)], [Binding.Pad(PadButton.LB)], "Music: a tune");
        // Only on an instrument of chords (the lute), so it may share a pad button with the semitone.
        _chord = r.Control("chord", "Hold a chord (with a note key); F strums it", [Binding.Key(GlfwKey.Q)], [Binding.Pad(PadButton.RT)], "Music: chords");
        // Not 9: that's the hands-on work's ninth rune.
        _thank = r.Control("thank", "A bow, to whoever just tipped you or to the crowd", [Binding.Key(GlfwKey.Number0)], [], "Music");
        r.WorkInput<PlaySession>(Inputs);
        r.WorkView<PlaySession>(Draw);
        r.WorkSound<PlaySession>(Sound);
        r.WorldSounds(() => new OthersPlaying());
        r.WorkView<DanceSession>(DrawDance);
        r.WorkSound<DanceSession>(DanceSound);
    }

    private static IEnumerable<int> Inputs(WorkView view, PlaySession s)
    {
        if (view.Pressed(_thank))
            yield return PlaySession.BowBit; // a bow, playing on: to whoever just tipped, or to the crowd
        if (s.Def.Air != null)
        {
            if (view.Pressed(_breathe))
                yield return PlaySession.PackBreath(true);
            else if (view.Released(_breathe))
                yield return PlaySession.PackBreath(false);
        }
        if (s.Carrying != null)
            yield break; // the hands have the notes
        var drum = s.Def.IsDrum;
        var chord = s.Def.Chords && s.Mode == PlayMode.Free && view.Down(_chord);
        var up = !drum && view.Down(_octave);
        var sharp = !drum && !chord && view.Down(_sharp);
        if (s.Chord >= 0 && !view.Down(_chord))
            yield return PlaySession.Pack(s.Chord, false, false, chord: true); // the chord key let go: so is the chord
        // A chord's taken with the chord key and a note key, and held while the chord key is (the note key may go): F strums it.
        for (var i = 0; i < KeyCount(s); i++)
            if (chord && view.Pressed(_notes[i]))
                yield return PlaySession.Pack(i, up, true, chord: true);
            else if (view.Pressed(_notes[i]))
                yield return PlaySession.Pack(i, up, true, sharp);
            else if (view.Released(_notes[i]))
                yield return PlaySession.Pack(i, up, false); // let go: the note held stops
    }

    /// <summary>How long a key stays lit after its note, in session steps.</summary>
    private const int LitSteps = 15;

    /// <summary>The note keys an instrument plays: all eight, or a drum's strokes.</summary>
    private static int KeyCount(PlaySession s) => s.Def.IsDrum ? Math.Min(s.Def.Strokes.Count, _notes.Length) : _notes.Length;

    private static string KeysOf(PlaySession s) => $"{WorkView.Glyph(_notes[0])}-{WorkView.Glyph(_notes[KeyCount(s) - 1])}";

    /// <summary>What taking the instrument's air back is called: a breath, or the bow turned.</summary>
    private static (string Take, string Taking) AirWords(PlaySession s) => s.Def.Air == "Bow"
        ? ("Turn the bow!", "Turning the bow...")
        : ("Breathe in!", "Breathing in...");

    private static readonly string[] Ordinals = ["first", "second", "third", "fourth", "fifth", "sixth"];

    private static void Draw(WorkView view, PlaySession s)
    {
        _defs = view.Sim.Defs;
        if (s.Carrying is { } tune)
            DrawCarried(view, s, tune);
        else if (s.Mode == PlayMode.Book)
        {
            view.Begin(s.Title,
                (KeysOf(s), "a tune's call, after a pause"),
                (Controls.Pair(Act.WalkUp, Act.WalkDown), "pick out a tune"),
                (WorkView.Glyph(Act.WorkAct), "back to free play"),
                (WorkView.Glyph(Act.Back), "put it away"));
            ImGuiText.Colored(Theme.Accent, "From the book.");
            ImGuiText.Colored(Theme.Dim, s.Slipped > 0 && s.StepsDone - s.LastJudged < 4 * 60
                ? "You've lost the tune. Start it again from its opening."
                : "Play a tune's call, from any note, and your hands play it through.");
            DrawKeys(s, view);
            DrawBook(s);
        }
        else
        {
            List<(string, string)> hints = [(KeysOf(s), s.Def.IsDrum ? "a stroke: low, high, the rim" : "a note of the scale")];
            if (s.Def.IsDrum)
            {
                if (s.Def.Bend > 0f)
                    hints.Add((WorkView.Glyph(Act.WalkUp), "hold: press the head, higher"));
            }
            else
            {
                hints.Add((WorkView.Glyph(_octave), "hold for the octave above"));
                hints.Add((WorkView.Glyph(_sharp), "hold for a semitone up"));
                if (s.Def.Chords)
                {
                    hints.Add(($"{WorkView.Glyph(_chord)}+{KeysOf(s)}", "hold a chord"));
                    hints.Add((WorkView.Glyph(Act.Use), "strum it, down and up"));
                }
                if (s.Def.Bend > 0f)
                    hints.Add((Controls.Pair(Act.WalkLeft, Act.WalkRight), "hold: bend the note down, up"));
            }
            if (s.Def.Book)
                hints.Add((WorkView.Glyph(Act.WorkAct), "play from the book"));
            hints.Add((WorkView.Glyph(Act.Back), "put it away"));
            view.Begin(s.Title, [.. hints]);
            ImGuiText.Colored(Theme.Accent, "Free play.");
            ImGuiText.Colored(Theme.Dim, s.Def.IsDrum ? "Keep the town's feet moving: no tunes from the book on the drum yet."
                : s.Def.Chords && s.Chord >= 0 ? $"Holding {s.Def.ChordName(s.Chord)}: strum it with {WorkView.Glyph(Act.Use)}, down and up on the beat."
                : s.Def.Chords ? $"Each note rings and dies away. Hold {WorkView.Glyph(_chord)} and a key for its chord."
                : !s.Def.Sustains ? "Each note rings on and dies away; play them over each other for chords."
                : "Your own tune: nothing you play here turns into one from the book.");
            DrawKeys(s, view);
            var instrument = InstrumentNamed(s.InstrumentName);
            if (s.Played == 0 || instrument == null)
                ImGuiText.Colored(Theme.Dim, s.Def.IsDrum ? "Not a stroke yet." : "Not a note yet.");
            else
            {
                // What you've just played, by name: the last dozen notes, the newest last (a strum's, once, by its chord).
                var recent = s.Notes.Skip(Math.Max(0, s.Notes.Count - 12)).Select(n => instrument.NoteName(n.Degree, n.Octave, n.Accidental));
                ImGuiText.Colored(Theme.Parchment, string.Join("  ", recent));
                DrawRhythm(s);
            }
        }
        DrawAudience(view);
        view.End();
    }

    /// <summary>
    /// How well your own playing keeps a rhythm (the pulse found under it, <see cref="Performance.Rhythm"/>): what
    /// draws a crowd to dance, whatever the notes. Its beat, too, as a dancer would step to it.
    /// </summary>
    private static void DrawRhythm(PlaySession s)
    {
        var p = Performance.Of(s);
        if (!p.Playing)
        {
            ImGuiText.Colored(Theme.Dim, "Rhythm: keep playing to find one.");
            return;
        }
        var colour = p.Steadiness >= 0.75f ? Theme.Good : p.Steadiness >= 0.5f ? Theme.Accent : Theme.Warn;
        Meter("Rhythm", p.Steadiness, colour);
        ImGuiText.Colored(Theme.Dim, (p.Steadiness >= 0.75f ? "Steady as a heartbeat" : p.Steadiness >= 0.5f ? "Holding together" : "Ragged")
            + $", about {p.Tempo:0} beats a minute{(p.Danceable ? ": one to dance to." : p.Steadiness >= 0.5f ? ": livelier, and they'd dance." : ".")}");
    }

    /// <summary>Your claps, heard as you clap: a dry slap (the drum's rim stroke stands in).</summary>
    private static void DanceSound(WorkSoundContext ctx, DanceSession s)
    {
        if (ctx.Fresh)
            ctx.A = s.Moved;
        for (var i = Math.Max(ctx.A, s.Moved - s.Moves.Count); i < s.Moved; i++)
            if (s.Moves[i - (s.Moved - s.Moves.Count)].Kind == DanceSession.Clap)
                ctx.Audio.Play("musician.drum.ka", gain: 0.6f, pitch: 1.3f);
        ctx.A = s.Moved;
    }

    /// <summary>
    /// Dancing to a musician: a ring closing on each beat of their music (step as it lands), your last step lit green
    /// on the beat or red off it (judged here as you step, from the music as it stands; the sim judges it too, a tick
    /// after), and how long a run you've kept.
    /// </summary>
    private static void DrawDance(WorkView view, DanceSession s)
    {
        view.Begin("Dancing",
            (Controls.Pair(Act.WalkLeft, Act.WalkRight), "a step to the side, on the beat"),
            (WorkView.Glyph(Act.WorkAct), "a turn"),
            (WorkView.Glyph(Act.Use), "a clap"),
            (WorkView.Glyph(Act.Back), "stop dancing"));
        var sim = view.Sim;
        var music = sim.World.IsAlive(s.Musician) && sim.World.TryGet<WorkSession>(s.Musician, out var w) && w is PlaySession p ? p : null;
        var clock = music == null ? null : Performance.BeatClock(music);
        ImGuiText.Colored(Theme.Dim, music == null || clock == null ? "The music's stopped. Wait a moment, or stop dancing." : "Step with the beat as the ring closes.");
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var size = new float2(Gui.S(LaneWidth), Gui.S(130));
        var max = min + size;
        dl.AddRectFilled(in min, in max, Theme.U8(Theme.Inset), Gui.S(6), ImDrawFlags.None);
        var centre = min + new float2(Gui.S(70), size.Y * 0.5f);
        if (music != null && clock is { } c)
        {
            // The ring: from wide to the mark as the next beat comes.
            var at = sim.Clock.Tick - music.StartTick - c.Origin;
            var phase = at / c.Period - MathF.Floor(at / c.Period);
            var radius = Gui.S(10) + (1f - phase) * Gui.S(42);
            dl.AddCircle(in centre, radius, Theme.U8(Theme.Accent, 0.35f + 0.6f * phase), 32, Gui.S(2));
        }
        // Your last step, lit by how near the beat it fell.
        var lit = Theme.Parchment;
        var word = "";
        if (s.Moves.Count > 0 && s.StepsDone - s.Moves[^1].Step < 20)
        {
            var last = s.Moves[^1];
            var off = Performance.OffBeat(sim, s.Musician, s.StartTick + last.Step);
            var on = off is { } o && MathF.Abs(o) <= s.Window;
            lit = on ? Theme.Good : Theme.Bad;
            word = (last.Kind switch { DanceSession.Left => "Left", DanceSession.Right => "Right", DanceSession.Turn => "Turn", _ => "Clap" }) + (on ? "!" : "...");
        }
        dl.AddCircleFilled(in centre, Gui.S(10), Theme.U8(lit), 24);
        var wordAt = centre + new float2(Gui.S(60), -Gui.S(20));
        dl.AddText(in wordAt, Theme.U8(lit), word);
        var runAt = centre + new float2(Gui.S(60), Gui.S(4));
        dl.AddText(in runAt, Theme.U8(Theme.Parchment), $"{s.Streak} in step (best {s.BestStreak})");
        ImGui.Dummy(in size);
        ImGuiText.Colored(Theme.Dim, s.OnBeat >= DanceSession.Remembered ? "A proper dance: you'll remember this one." : $"{s.OnBeat} steps on the beat so far.");
        view.End();
    }

    /// <summary>Seconds a tip stays news in the window (and you may bow for it).</summary>
    private const float TipNews = 6f;

    /// <summary>The tip last bowed for (its tick), so one bow answers one tip.</summary>
    private static long _bowedFor = -1;

    /// <summary>
    /// Your hat as it lies at your feet, the silver by it piled in, a coin dropping in as each tip lands, and who
    /// tossed it (by name if you can see them), so you can thank them: a bow, turned to them.
    /// </summary>
    private static void DrawHat(WorkView view, PlaySession s)
    {
        var sim = view.Sim;
        var w = sim.World;
        if (!w.IsAlive(s.Hat))
        {
            ImGuiText.Colored(Theme.Dim, "No hat down: tips need somewhere to go.");
            return;
        }
        var silver = Takings.InHat(sim, s.Hat);
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var size = new float2(Gui.S(LaneWidth), Gui.S(64));
        var hatW = Gui.S(90);
        var brimTop = min.Y + Gui.S(40);
        // The coins, piled in the crown: a few for a little, more as it fills.
        var shown = Math.Min(silver, 14);
        for (var i = 0; i < shown; i++)
        {
            var c = new float2(min.X + Gui.S(24) + i % 7 * Gui.S(7), brimTop - Gui.S(4) - i / 7 * Gui.S(5));
            dl.AddCircleFilled(in c, Gui.S(4), Theme.U8(new float4(0.86f, 0.84f, 0.76f, 1f)), 12);
            dl.AddCircle(in c, Gui.S(4), Theme.U8(Theme.Cellar), 12, Gui.S(1));
        }
        var c0 = new float2(min.X + Gui.S(16), min.Y + Gui.S(20));
        var c1 = new float2(min.X + hatW - Gui.S(16), brimTop + Gui.S(4));
        dl.AddRect(in c0, in c1, Theme.U8(Theme.Oak), Gui.S(8), ImDrawFlags.None, Gui.S(3)); // the crown, open to us
        var b0 = new float2(min.X, brimTop);
        var b1 = new float2(min.X + hatW, brimTop + Gui.S(10));
        dl.AddRectFilled(in b0, in b1, Theme.U8(Theme.Oak), Gui.S(5), ImDrawFlags.None); // the brim
        var band0 = new float2(c0.X, brimTop - Gui.S(6));
        var band1 = new float2(c1.X, brimTop - Gui.S(2));
        dl.AddRectFilled(in band0, in band1, Theme.U8(Theme.HearthRed), 0f, ImDrawFlags.None);
        // The newest tip: a coin dropping in, and who it was.
        var x = min.X + hatW + Gui.S(14);
        if (s.Tips.Count > 0 && s.Tips[^1] is var tip && (sim.Clock.Tick - tip.Tick) / (float)SimClock.TicksPerSecond is var age && age < TipNews)
        {
            if (age < 0.6f)
            {
                var fall = new float2(min.X + hatW * 0.5f, min.Y + (brimTop - min.Y - Gui.S(6)) * (age / 0.6f) * (age / 0.6f));
                dl.AddCircleFilled(in fall, Gui.S(5), Theme.U8(Theme.Accent), 12);
            }
            var who = w.IsAlive(tip.From) && view.Perceived.CanSee(tip.From) ? w.Get<Creature>(tip.From).Name : "Someone";
            var line0 = new float2(x, min.Y + Gui.S(8));
            dl.AddText(in line0, Theme.U8(Theme.Good), $"{who} tossed you {tip.Silver} silver!");
            var line1 = new float2(x, min.Y + Gui.S(8) + ImGui.GetTextLineHeightWithSpacing());
            var bowed = _bowedFor == tip.Tick;
            dl.AddText(in line1, Theme.U8(bowed ? Theme.Dim : Theme.Parchment), bowed ? "You bowed your thanks." : $"{WorkView.Glyph(_thank)}: a bow, to thank them");
            if (!bowed && view.Pressed(_thank))
                _bowedFor = tip.Tick; // the bow itself goes as an input (Inputs)
        }
        var total = new float2(x, min.Y + Gui.S(8) + 2 * ImGui.GetTextLineHeightWithSpacing());
        dl.AddText(in total, Theme.U8(Theme.Dim), silver > 0 ? $"By your hat: {silver} silver." : "Nothing by it yet.");
        ImGui.Dummy(in size);
    }

    /// <summary>Who's come to the music, as far as you can see them: dancing, or stood listening.</summary>
    private static void DrawAudience(WorkView view)
    {
        var w = view.Sim.World;
        var (dancing, listening) = (0, 0);
        foreach (var e in w.Each<Mind>())
            if (w.Get<Mind>(e).Job is AudienceJob a && a.Musician == view.Me && view.Perceived.CanSee(e))
            {
                if (a.Dancing)
                    dancing++;
                else
                    listening++;
            }
        foreach (var e in w.Each<WorkSession>()) // players dancing to you
            if (w.Get<WorkSession>(e) is DanceSession d && d.Musician == view.Me && view.Perceived.CanSee(e))
                dancing++;
        ImGui.Separator();
        // The takings, and what you've heard called for (said aloud: only what reached you).
        if (w.TryGet<WorkSession>(view.Me, out var mine) && mine is PlaySession playing)
        {
            DrawHat(view, playing);
            var asked = new List<string>();
            foreach (var e in w.Each<TuneRequest>())
                if (w.Get<TuneRequest>(e) is { Heard: true } q && q.Musician == view.Me && q.Until > view.Sim.Clock.Tick
                    && playing.Book.Find(t => t.Name == q.Tune) is { } tune && !asked.Contains(tune.Label))
                    asked.Add(tune.Label);
            if (asked.Count > 0)
                ImGuiText.Colored(Theme.Accent, $"Called for: {string.Join(", ", asked)}. Play it while they're here.");
        }
        if (dancing + listening == 0)
            ImGuiText.Colored(Theme.Dim, "No one's come to listen yet.");
        else
            ImGuiText.Colored(Theme.Good, (dancing, listening) switch
            {
                (0, var l) => $"{l} {(l == 1 ? "is" : "are")} listening.",
                (var d, 0) => $"{d} {(d == 1 ? "is" : "are")} dancing.",
                var (d, l) => $"{d} dancing, {l} listening.",
            });
    }

    private static void DrawCarried(WorkView view, PlaySession s, SongDef tune)
    {
        List<(string, string)> hints = [(WorkView.Glyph(Act.Use), s.Def.Sustains ? "tap the beat; hold through a long note" : "tap the beat")];
        if (s.Def.Air != null)
            hints.Add((WorkView.Glyph(_breathe), s.Def.Air == "Bow" ? "hold: turn the bow" : "hold: breathe in"));
        hints.Add((Controls.Pair(Act.WalkLeft, Act.WalkRight), "slower, faster"));
        hints.Add((WorkView.Glyph(Act.WorkAct), "let it go, back to free play"));
        hints.Add((WorkView.Glyph(Act.Back), "put it away"));
        view.Begin(s.Title, [.. hints]);
        var (part, time) = tune.SectionAt(s.Section);
        var round = tune.Form.Count > 1 ? $": the {part.ToLowerInvariant()}{(time > 1 ? $", the {Ordinals[Math.Min(time, Ordinals.Length) - 1]} time" : "")}" : "";
        ImGuiText.Colored(Theme.Good, $"Your hands take over: {tune.Label}{round}.");
        ImGuiText.Colored(Theme.Dim, s.Def.Air == "Bow" ? "They find the notes. You keep the beat and the bow."
            : s.Def.Air != null ? "They find the notes. You keep the beat and your breath." : "They find the notes. You keep the beat.");
        var done = Math.Clamp((s.Next - tune.CarriesFrom) / (float)Math.Max(1, tune.Sequence.Count - tune.CarriesFrom), 0f, 1f);
        ImGui.ProgressBar(done, new float2(Gui.S(LaneWidth), Gui.S(6)), ""u8);
        ImGui.Dummy(new float2(1, Gui.S(4)));
        var top = ImGui.GetCursorScreenPos();
        DrawFoot(s, top, new float2(Gui.S(150), Gui.S(124)));
        DrawBreath(s, tune, top + new float2(Gui.S(162), 0), new float2(Gui.S(LaneWidth - 162), Gui.S(124)));
        ImGui.Dummy(new float2(Gui.S(LaneWidth), Gui.S(128)));
        DrawLane(s, tune);
        var mind = s.Concentration;
        var colour = mind < PlaySession.Faltering * 0.5f ? Theme.Bad : mind < PlaySession.Faltering ? Theme.Warn : Theme.Good;
        Meter("Concentration", mind, colour);
        ImGuiText.Colored(colour, mind < PlaySession.Faltering ? "Your mind's wandering: find the beat again." : mind > 0.9f ? "Lost in the tune." : "Keeping it going.");
        ImGuiText.Colored(Theme.Dim, s.Pace < 0.98f ? $"Easing it: {s.Pace:P0} of its pace." : s.Pace > 1.02f ? $"Driving it on: {s.Pace:P0} of its pace." : "At its own pace.");
    }

    /// <summary>When the beat was last judged, from its words: the foot's flash.</summary>
    private static bool BeatJudged(PlaySession s) => s.LastWhy is "on the beat" or "a beat missed";

    /// <summary>
    /// The tapping foot: the toe rises between beats and comes down on each one, a ring closing on where it lands
    /// (press F as it lands). Green as a beat's kept, red as one's missed; dots under it count the bar.
    /// </summary>
    private static void DrawFoot(PlaySession s, float2 min, float2 size)
    {
        var dl = ImGui.GetWindowDrawList();
        var max = min + size;
        dl.AddRectFilled(in min, in max, Theme.U8(Theme.Inset), Gui.S(6), ImDrawFlags.None);
        var perBeat = s.StepsPerBeat;
        // The moment of the next beat: the one being judged till its window's past.
        var next = s.StepsDone <= s.Beat + s.Window ? s.Beat : s.Beat + perBeat;
        var phase = Math.Clamp((next - s.StepsDone) / perBeat, 0f, 1f);
        var lift = 4f * phase * (1f - phase); // down on the beat, highest between
        var since = s.StepsDone - s.LastJudged;
        var flash = since < 14 && BeatJudged(s) ? (s.LastJudgedKept ? Theme.Good : Theme.Bad) : Theme.Parchment;
        var ground = min.Y + size.Y - Gui.S(34);
        var heel = new float2(min.X + Gui.S(38), ground);
        // The leg, then the shoe swung up about the heel.
        var l0 = new float2(heel.X - Gui.S(2), min.Y + Gui.S(10));
        var l1 = new float2(heel.X + Gui.S(16), ground - Gui.S(10));
        dl.AddRectFilled(in l0, in l1, Theme.U8(Theme.Oak), Gui.S(4), ImDrawFlags.None);
        var angle = -lift * 0.5f;
        float2 R(float x, float y)
        {
            var (c, sn) = (MathF.Cos(angle), MathF.Sin(angle));
            var (px, py) = (Gui.S(x), Gui.S(y));
            return heel + new float2(px * c - py * sn, px * sn + py * c);
        }
        float2[] shoe = [R(-4, 0), R(56, 0), R(62, -7), R(54, -15), R(16, -18), R(-4, -16)];
        for (var i = 1; i < shoe.Length - 1; i++)
            dl.AddTriangleFilled(in shoe[0], in shoe[i], in shoe[i + 1], Theme.U8(flash));
        // The ring closing on where the toe comes down.
        var toe = heel + new float2(Gui.S(48), 0);
        var radius = Gui.S(6) + phase * Gui.S(34);
        dl.AddCircle(in toe, radius, Theme.U8(Theme.Accent, 0.35f + 0.6f * (1f - phase)), 24, Gui.S(2));
        var g0 = new float2(min.X + Gui.S(10), ground + Gui.S(1));
        var g1 = new float2(max.X - Gui.S(10), ground + Gui.S(3));
        dl.AddRectFilled(in g0, in g1, Theme.U8(Theme.OakLight), 0f, ImDrawFlags.None);
        // The bar's beats, and the key.
        for (var k = 0; k < 4; k++)
        {
            var c = new float2(min.X + Gui.S(20) + k * Gui.S(16), max.Y - Gui.S(14));
            dl.AddCircleFilled(in c, Gui.S(k == 0 ? 5 : 4), Theme.U8(s.BeatsGone % 4 == k ? Theme.Accent : Theme.Dim, s.BeatsGone % 4 == k ? 1f : 0.5f), 12);
        }
        var key = WorkView.Glyph(Act.Use);
        var at = new float2(max.X - Gui.S(12) - ImGui.CalcTextSize(key).X, max.Y - Gui.S(22));
        dl.AddText(in at, Theme.U8(Theme.Parchment), key);
        var word = s.BeatsGone < PlaySession.WarmUpBeats ? "find the beat" : "";
        var wordAt = new float2(min.X + Gui.S(8), min.Y + Gui.S(6));
        if (word.Length > 0)
            dl.AddText(in wordAt, Theme.U8(Theme.Dim), word);
    }

    /// <summary>
    /// The breath: a gauge of the air left, and what it's wanted for now: breathe in (a gap's open), breathing,
    /// swell (a long note's sounding, which spends it faster), or what's coming, and in how many beats.
    /// </summary>
    private static void DrawBreath(PlaySession s, SongDef tune, float2 min, float2 size)
    {
        var dl = ImGui.GetWindowDrawList();
        var max = min + size;
        if (s.Def.Air == null)
        {
            // Nothing to spend: only the beat to keep.
            dl.AddRectFilled(in min, in max, Theme.U8(Theme.Inset), Gui.S(6), ImDrawFlags.None);
            var at = min + new float2(Gui.S(12), Gui.S(12));
            dl.AddText(in at, Theme.U8(Theme.Dim), s.Def.Sustains ? "Play on." : "Each note rings of itself:");
            at.Y += ImGui.GetTextLineHeightWithSpacing();
            if (!s.Def.Sustains)
                dl.AddText(in at, Theme.U8(Theme.Dim), "just keep the beat."u8);
            return;
        }
        var (take, taking) = AirWords(s);
        var open = s.InGap && !s.GapTaken;
        dl.AddRectFilled(in min, in max, Theme.U8(open ? Theme.Water : Theme.Inset, open ? 0.25f : 1f), Gui.S(6), ImDrawFlags.None);
        // The gauge.
        var g0 = min + new float2(Gui.S(12), Gui.S(10));
        var g1 = new float2(g0.X + Gui.S(22), max.Y - Gui.S(10));
        dl.AddRectFilled(in g0, in g1, Theme.U8(Theme.Cellar), Gui.S(8), ImDrawFlags.None);
        var fill = new float2(g0.X, g1.Y - (g1.Y - g0.Y) * Math.Clamp(s.Breath, 0f, 1f));
        var air = s.Breath < 0.2f ? Theme.Bad : s.Breathing ? Theme.Water : Theme.Parchment;
        dl.AddRectFilled(in fill, in g1, Theme.U8(air), Gui.S(8), ImDrawFlags.None);
        dl.AddRect(in g0, in g1, Theme.U8(Theme.OakLight), Gui.S(8), ImDrawFlags.None, Gui.S(1));
        // What it's wanted for.
        var x = g1.X + Gui.S(14);
        var y = min.Y + Gui.S(12);
        void Line(string text, float4 colour)
        {
            var at = new float2(x, y);
            dl.AddText(in at, Theme.U8(colour), text);
            y += ImGui.GetTextLineHeightWithSpacing();
        }
        var breathe = WorkView.Glyph(_breathe);
        var swell = WorkView.Glyph(Act.Use);
        if (s.Breathing && !s.InGap)
            Line("Not here! Mid-phrase.", Theme.Bad);
        else if (s.Breathing)
            Line(taking, Theme.Water);
        else if (open)
        {
            Line(take, Theme.Water);
            Line($"hold {breathe}", Theme.Parchment);
        }
        if (s.LongAt >= 0)
        {
            Line(s.Swelling ? "Swelling it..." : "A long note: swell it!", Theme.Accent);
            if (!s.Swelling)
                Line($"hold {swell}", Theme.Parchment);
            var shaped = s.Shaping;
            var b0 = new float2(x, y + Gui.S(2));
            var b1 = new float2(max.X - Gui.S(12), y + Gui.S(10));
            var b2 = new float2(b0.X + (b1.X - b0.X) * shaped, b1.Y);
            dl.AddRectFilled(in b0, in b1, Theme.U8(Theme.Cellar), Gui.S(3), ImDrawFlags.None);
            dl.AddRectFilled(in b0, in b2, Theme.U8(shaped >= 1f ? Theme.Good : Theme.Accent), Gui.S(3), ImDrawFlags.None);
            y += Gui.S(14);
        }
        if (!s.Breathing && !open && s.LongAt < 0)
        {
            // Nothing wanted now: what's coming.
            var (breathIn, longIn) = Coming(s, tune);
            var low = s.Def.Air == "Bow" ? "Running out of bow!" : "Running out of air!";
            Line(s.Breath < 0.25f ? low : "Play on.", s.Breath < 0.25f ? Theme.Bad : Theme.Dim);
            if (breathIn is { } b && b <= 4f)
                Line($"{(s.Def.Air == "Bow" ? "A bow turn" : "A breath")} in {Math.Max(1, (int)MathF.Ceiling(b))}...", Theme.Water);
            if (longIn is { } l && l <= 4f)
                Line($"A long note in {Math.Max(1, (int)MathF.Ceiling(l))}...", Theme.Accent);
        }
    }

    /// <summary>Beats until the next gap to breathe in opens, and the next long note starts (null: none soon).</summary>
    private static (float? Breath, float? Long) Coming(PlaySession s, SongDef tune)
    {
        float? breath = null, longNote = null;
        var due = s.Due;
        for (var i = s.Next; i < tune.Sequence.Count && (breath == null || longNote == null); i++)
        {
            var note = tune.Sequence[i];
            var beats = (due - s.StepsDone) / s.StepsPerBeat;
            if (beats > 8f)
                break;
            if (breath == null && (note.BreathAfter || note.IsRest))
                breath = beats;
            if (longNote == null && note.IsLong)
                longNote = beats;
            due += note.Beats * s.StepsPerBeat;
        }
        return (breath, longNote);
    }

    private const float LaneWidth = 380;

    /// <summary>A labelled bar, 0..1.</summary>
    private static void Meter(string label, float value, float4 colour)
    {
        var dl = ImGui.GetWindowDrawList();
        var labelW = Gui.S(110);
        var at = ImGui.GetCursorScreenPos();
        dl.AddText(in at, Theme.U8(Theme.Dim), label);
        var min = at + new float2(labelW, Gui.S(4));
        var size = new float2(Gui.S(LaneWidth) - labelW, Gui.S(10));
        var full = min + size;
        var held = min + new float2(size.X * Math.Clamp(value, 0f, 1f), size.Y);
        dl.AddRectFilled(in min, in full, Theme.U8(Theme.Inset), Gui.S(3), ImDrawFlags.None);
        dl.AddRectFilled(in min, in held, Theme.U8(colour), Gui.S(3), ImDrawFlags.None);
        var line = new float2(Gui.S(LaneWidth), ImGui.GetTextLineHeightWithSpacing());
        ImGui.Dummy(in line);
    }

    /// <summary>
    /// The coming notes on a lane, sliding toward the line where they sound, a tick on every beat (F as it crosses
    /// keeps the beat), a breath mark over each gap to breathe in, and a swell drawn on each long note (F held
    /// through it shapes it). The line flashes as something's kept or missed.
    /// </summary>
    private static void DrawLane(PlaySession s, SongDef tune)
    {
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos() + new float2(0, Gui.S(14));
        var width = Gui.S(LaneWidth);
        var h = Gui.S(40);
        var beatPx = Gui.S(56);
        var line = min.X + Gui.S(40);
        var perBeat = s.StepsPerBeat;
        var right = min.X + width;
        var bottom = min + new float2(width, h + Gui.S(10));
        dl.AddRectFilled(in min, in bottom, Theme.U8(Theme.Inset), Gui.S(4), ImDrawFlags.None);
        float X(float step) => line + (step - s.StepsDone) / perBeat * beatPx;
        // The gap open now, lit while it lasts.
        if (s.InGap)
        {
            var g0 = new float2(Math.Max(min.X, X(s.GapFrom)), min.Y);
            var g1 = new float2(Math.Min(right, X(s.GapTo)), min.Y + h + Gui.S(10));
            dl.AddRectFilled(in g0, in g1, Theme.U8(Theme.Water, s.GapTaken ? 0.15f : 0.35f), 0f, ImDrawFlags.None);
        }
        // The beats.
        for (var k = -1; k < 8; k++)
        {
            var x = X(s.Beat + k * perBeat);
            if (x < min.X || x > right)
                continue;
            var t0 = new float2(x - Gui.S(1), min.Y + h + Gui.S(2));
            var t1 = new float2(x + Gui.S(1), min.Y + h + Gui.S(8));
            dl.AddRectFilled(in t0, in t1, Theme.U8(k == 0 && !s.BeatKept ? Theme.Parchment : Theme.Dim), 0f, ImDrawFlags.None);
        }
        // The long note sounding, its swell filling as it's shaped.
        if (s.LongAt >= 0)
        {
            var started = s.Due - s.LongSteps;
            DrawNote(dl, tune.Sequence[s.LongAt], s.Shift, Math.Max(X(started), min.X), Math.Min(X(s.Due) - Gui.S(3), right), min.Y, h,
                s.Shaping, s.Swelling);
        }
        // The notes still to come.
        var due = s.Due;
        for (var i = s.Next; i < tune.Sequence.Count; i++)
        {
            var note = tune.Sequence[i];
            var x0 = X(due);
            if (x0 > right)
                break;
            var end = due + note.Beats * perBeat;
            due = end;
            if (note.BreathAfter || note.IsRest)
                BreathMark(dl, note.IsRest ? X(end - note.Beats * perBeat * 0.5f) : X(end), min.Y, right);
            if (note.IsRest)
                continue;
            DrawNote(dl, note, s.Shift, x0, Math.Min(X(end) - Gui.S(3), right), min.Y, h, 0f, false);
        }
        // The line, flashing as something's kept or missed.
        var since = s.StepsDone - s.LastJudged;
        var flash = since < 12 ? (s.LastJudgedKept ? Theme.Good : Theme.Bad) : Theme.Parchment;
        var l0 = new float2(line - Gui.S(since < 12 ? 2 : 1), min.Y);
        var l1 = new float2(line + Gui.S(since < 12 ? 2 : 1), min.Y + h + Gui.S(10));
        dl.AddRectFilled(in l0, in l1, Theme.U8(flash), 0f, ImDrawFlags.None);
        var size = new float2(width, h + Gui.S(26));
        ImGui.Dummy(in size);
    }

    /// <summary>
    /// A note on the lane as a bar set as high as it sounds (the hands find it: the shape's for following the tune, not
    /// for pressing); a long note thicker, its swell (a wedge widening) under it, filled as far as it's been shaped.
    /// </summary>
    private static void DrawNote(ImDrawListPtr dl, SongNote note, int shift, float x0, float x1, float top, float h, float shaped, bool swelling)
    {
        if (x1 <= x0)
            return;
        var index = Math.Clamp(note.Step + shift, 0, 14);
        var bar = Gui.S(note.IsLong ? 9 : 6);
        var room = h - Gui.S(12) - bar;
        var y = top + Gui.S(3) + room * (1f - Math.Clamp(index / 10f, 0f, 1f));
        var a = new float2(x0, y);
        var b = new float2(x1, y + bar);
        dl.AddRectFilled(in a, in b, Theme.U8(note.IsLong ? Theme.Accent : Theme.AccentSoft), Gui.S(2), ImDrawFlags.None);
        if (!note.IsLong)
            return;
        // The swell: a wedge under the note, lit as far as it's been held.
        var foot = b.Y + Gui.S(9);
        var p0 = new float2(a.X + Gui.S(2), foot);
        var p1 = new float2(b.X - Gui.S(2), foot - Gui.S(7));
        var p2 = new float2(b.X - Gui.S(2), foot);
        dl.AddTriangleFilled(in p0, in p1, in p2, Theme.U8(Theme.Cellar, 0.8f));
        if (shaped > 0f)
        {
            var q1 = new float2(p0.X + (p2.X - p0.X) * shaped, foot - Gui.S(7) * shaped);
            var q2 = new float2(q1.X, foot);
            dl.AddTriangleFilled(in p0, in q1, in q2, Theme.U8(swelling ? Theme.Good : Theme.Accent));
        }
    }

    /// <summary>A breath mark over the lane (a comma, as on a sheet of music): breathe here.</summary>
    private static void BreathMark(ImDrawListPtr dl, float x, float top, float right)
    {
        if (x > right - Gui.S(4))
            return;
        var c = new float2(x, top - Gui.S(7));
        dl.AddCircleFilled(in c, Gui.S(3.5f), Theme.U8(Theme.Water), 12);
        var t0 = new float2(x + Gui.S(3), top - Gui.S(7));
        var t1 = new float2(x - Gui.S(1), top - Gui.S(1));
        dl.AddLine(in t0, in t1, Theme.U8(Theme.Water), Gui.S(2));
    }

    /// <summary>The note keys, lit as their notes sound, each named by the note it plays now (the octave and semitone keys held or not).</summary>
    private static void DrawKeys(PlaySession s, WorkView view)
    {
        var instrument = InstrumentNamed(s.InstrumentName);
        var (up, sharp) = (view.Down(_octave) ? 1 : 0, view.Down(_sharp) ? 1 : 0);
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var key = Gui.S(46);
        var gap = Gui.S(6);
        var count = KeyCount(s);
        if (s.Def.IsDrum)
            (up, sharp) = (0, 0);
        var size = new float2(count * (key + gap) - gap, key * 1.5f);
        for (var i = 0; i < count; i++)
        {
            var degree = i;
            var lit = s.Chord == i // the chord held
                || s.Notes.FindLastIndex(n => n.Degree == degree) is var last and >= 0 && s.StepsDone - s.Notes[last].Step < LitSteps;
            var a = min + new float2(i * (key + gap), 0);
            var b = a + new float2(key, key * 1.5f);
            dl.AddRectFilled(in a, in b, Theme.U8(lit ? Theme.Accent : Theme.Inset), Gui.S(4), ImDrawFlags.None);
            dl.AddRect(in a, in b, Theme.U8(Theme.OakLight), Gui.S(4), ImDrawFlags.None, Gui.S(2));
            var label = WorkView.Glyph(_notes[i]);
            var at = a + new float2(key * 0.5f - ImGui.CalcTextSize(label).X * 0.5f, key * 0.3f);
            dl.AddText(in at, Theme.U8(lit ? Theme.Cellar : Theme.Parchment), label);
            if (instrument != null)
            {
                var chords = s.Def.Chords && s.Mode == PlayMode.Free && view.Down(_chord);
                var name = chords ? instrument.ChordName(i) : instrument.NoteName(i, up, sharp);
                var nameAt = a + new float2(key * 0.5f - ImGui.CalcTextSize(name).X * 0.5f, key * 0.85f);
                dl.AddText(in nameAt, Theme.U8(lit ? Theme.Cellar : Theme.Accent), name);
            }
        }
        ImGui.Dummy(in size);
    }

    /// <summary>The tunes known, the one picked out with its opening drawn as a guide.</summary>
    private static void DrawBook(PlaySession s)
    {
        if (s.Book.Count == 0)
            return;
        ImGui.Separator();
        ImGuiText.Colored(Theme.Parchment, "The tunes you know:");
        for (var i = 0; i < s.Book.Count; i++)
        {
            var t = s.Book[i];
            var picked = i == s.Picked;
            ImGuiText.Colored(picked ? Theme.Accent : Theme.Text, $"{(picked ? "> " : "  ")}{t.Label}, {t.Kind.ToLowerInvariant()}");
            if (picked)
            {
                var from = t.CallPart is { } part && t.Form.Count > 1 ? $"Its call, from the {part.ToLowerInvariant()}" : "Its call, how it starts";
                ImGuiText.Colored(Theme.Dim, $"{from}. The longer the key, the longer the note; a bar over it: hold {WorkView.Glyph(_octave)}.");
                DrawOpening(t);
            }
        }
    }

    /// <summary>
    /// A tune's opening as its rhythm goes: a key cap per note, as wide as the note is long, ticks under
    /// it on the beats. Only a guide: the lead-in matches the notes, not the time between them.
    /// </summary>
    private static void DrawOpening(SongDef tune)
    {
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos() + new float2(Gui.S(12), Gui.S(5));
        var beat = Gui.S(30);
        var h = Gui.S(24);
        var x = min.X;
        var n = tune.Call.Count;
        for (var i = 0; i < n; i++)
        {
            var w = tune.Call[i].Beats * beat;
            var a = new float2(x, min.Y);
            var b = new float2(x + w - Gui.S(3), min.Y + h);
            dl.AddRectFilled(in a, in b, Theme.U8(Theme.Inset), Gui.S(3), ImDrawFlags.None);
            dl.AddRect(in a, in b, Theme.U8(Theme.OakLight), Gui.S(3), ImDrawFlags.None, Gui.S(1));
            // The key that plays it; in the octave above, a bar over the cap (hold the octave key).
            var step = tune.Call[i].Step;
            var label = WorkView.Glyph(_notes[step <= 7 ? step : step - 7]) + (tune.Call[i].Accidental > 0 ? "#" : "");
            var at = new float2(a.X + Gui.S(4), a.Y + (h - ImGui.GetTextLineHeight()) * 0.5f);
            dl.AddText(in at, Theme.U8(Theme.Parchment), label);
            if (step > 7)
            {
                var o0 = new float2(a.X + Gui.S(3), a.Y - Gui.S(4));
                var o1 = new float2(b.X - Gui.S(3), a.Y - Gui.S(2));
                dl.AddRectFilled(in o0, in o1, Theme.U8(Theme.Accent), 0f, ImDrawFlags.None);
            }
            // A tick on each beat the note holds over (a short note: a tick of its own only on the beat).
            var t0 = new float2(x + Gui.S(2), min.Y + h + Gui.S(3));
            var t1 = t0 + new float2(Gui.S(4), Gui.S(3));
            dl.AddRectFilled(in t0, in t1, Theme.U8(Theme.Parchment), 0f, ImDrawFlags.None);
            x += w;
        }
        var size = new float2(x - min.X + Gui.S(12), h + Gui.S(15));
        ImGui.Dummy(in size);
    }

    /// <summary>A note's sound: the octave's root sample (its one-shot, or with <paramref name="held"/> its loop) pitched up to the scale's step.</summary>
    internal static (string Id, float Pitch)? Voice(InstrumentDef? instrument, NoteEvent n, bool held = false, float bend = 0f)
    {
        var octaves = held ? instrument?.Held : instrument?.Octaves;
        if (instrument == null || octaves == null || octaves.Count == 0)
            return null;
        if (instrument.IsDrum) // a stroke's own sound, raised as far as the head's pressed
            return n.Degree < octaves.Count ? (octaves[n.Degree], MathF.Pow(2f, bend / 12f)) : null;
        if (octaves.Count >= 16) // a sound for every key, as it is: only the semitone and the bend pitched
            return (octaves[Math.Clamp(n.Octave * 8 + n.Degree, 0, octaves.Count - 1)], MathF.Pow(2f, (n.Accidental + bend) / 12f));
        var id = octaves[Math.Min(n.Octave, octaves.Count - 1)];
        var semitones = instrument.Scale[Math.Clamp(n.Degree, 0, instrument.Scale.Count - 1)] + n.Accidental + bend;
        return (id, MathF.Pow(2f, semitones / 12f));
    }

    /// <summary>How far the session's bending its notes, in semitones.</summary>
    private static float BendOf(PlaySession s) => s.Bend * s.Def.Bend;

    /// <summary>Your own playing, heard as you press (the predicted session): every note since the last frame.</summary>
    private static void Sound(WorkSoundContext ctx, PlaySession s)
    {
        if (ctx.Fresh)
            ctx.A = s.Played;
        var fresh = Math.Min(s.Played - ctx.A, s.Notes.Count);
        var instrument = InstrumentNamed(s.InstrumentName);
        for (var i = s.Notes.Count - fresh; i < s.Notes.Count; i++)
            Speak(ctx.Audio, instrument, s.Notes[i], null, 0f, BendOf(s), Strummed(s, i));
        ctx.A = s.Played;
        Ring(ctx.Audio, instrument, s, "musician:mine", null, 0f);
    }

    /// <summary>Seconds a held note takes to come up and die away: quick, as a finger lifts.</summary>
    private const float HeldFade = 0.04f;

    /// <summary>A note as it starts: an instrument that holds its notes gives its attack (the loop comes in under it, <see cref="Ring"/>); one that doesn't, its one-shot.</summary>
    /// <summary>Seconds between a strum's strings: a down-stroke's sweep, and an up-stroke's flick.</summary>
    private const float DownGap = 0.026f, UpGap = 0.012f;

    /// <summary>An up-stroke's strings, as loud as a down-stroke's: lighter.</summary>
    private const float UpGain = 0.65f;

    /// <summary>
    /// When into a strum the note at <paramref name="i"/> comes, and how loud: notes struck on the same step are a strum,
    /// spread across its strings; one going from high to low is an up-stroke, quicker and lighter.
    /// </summary>
    private static (float Delay, float Gain) Strummed(PlaySession s, int i)
    {
        var (first, last) = (i, i);
        while (first > 0 && s.Notes[first - 1].Step == s.Notes[i].Step)
            first--;
        while (last < s.Notes.Count - 1 && s.Notes[last + 1].Step == s.Notes[i].Step)
            last++;
        if (first == last)
            return (0f, 1f);
        var up = s.Notes[first].Index > s.Notes[last].Index;
        return ((i - first) * (up ? UpGap : DownGap), up ? UpGain : 1f);
    }

    private static void Speak(AudioEngine audio, InstrumentDef? instrument, NoteEvent note, float3? at, float muffle, float bend, (float Delay, float Gain) strum = default)
    {
        if (instrument is { Held.Count: > 0 })
        {
            if (instrument.Attack is { } attack)
                audio.Play(attack, at, muffle: muffle);
        }
        else if (Voice(instrument, note, bend: bend) is { } v)
            audio.Play(v.Id, at, gain: strum.Gain == 0f ? 1f : strum.Gain, pitch: v.Pitch, muffle: muffle, delay: strum.Delay);
    }

    /// <summary>
    /// The note still sounding, kept going on its loop while the session says it rings: each note a voice of its
    /// own (keyed by its place in the count), so a note played again speaks again, and a note let go dies away.
    /// </summary>
    private static void Ring(AudioEngine audio, InstrumentDef? instrument, PlaySession s, string key, float3? at, float muffle)
    {
        if (s.RingingNote is { } note && Voice(instrument, note, held: true, bend: BendOf(s)) is { } v)
            audio.Hold($"{key}:{s.StartTick}:{s.Ringing}", v.Id, at: at, muffle: muffle, pitch: v.Pitch, fade: instrument?.Fade ?? HeldFade);
    }

    private static Sim.Defs.DefDatabase? _defs;

    /// <summary>The instrument def by name, from the defs last seen (the views and sounds have no simulation to hand).</summary>
    private static InstrumentDef? InstrumentNamed(string name) => _defs?.ModDef<InstrumentDef>(InstrumentDef.Kind, name);

    /// <summary>
    /// Anyone else's playing, heard from where they stand, as far as it carries: the notes they've played
    /// since the last frame (the sim's session, a batch behind theirs, which a listener doesn't mind).
    /// </summary>
    private sealed class OthersPlaying : IWorldSounds
    {
        private readonly Dictionary<Sim.Ecs.Entity, (long Start, int Played)> _heard = [];

        /// <summary>The last tip clinked for (sim tick); -1 before the first look, so old tips don't clink.</summary>
        private long _clinked = -1;

        /// <summary>Session steps after the last note before the background music comes back.</summary>
        private const int QuietAfter = 7 * 60;

        public void Update(WorldSoundContext ctx)
        {
            var w = ctx.Sim.World;
            _defs = ctx.Sim.Defs;
            foreach (var e in w.Query<WorkSession>())
            {
                if (w.Get<WorkSession>(e) is not PlaySession s || !w.TryGet<Position>(e, out var at))
                    continue;
                // Live music in earshot (yours too): the background music waits for it, and comes back
                // a little after the last note (a pipe merely held is no music).
                var playing = s.Carrying != null || s.Notes.Count > 0 && s.StepsDone - s.Notes[^1].Step < QuietAfter;
                if (playing && (e == ctx.View.Viewer || ctx.Reach(at.Cell) != null))
                    ctx.HoldMusic();
                if (e == ctx.View.Viewer)
                {
                    // A tip landing by your hat: the clink of it.
                    if (s.Tips.Count > 0 && s.Tips[^1].Tick > _clinked)
                    {
                        if (_clinked >= 0)
                            ctx.Audio.Play("ui.coin");
                        _clinked = s.Tips[^1].Tick;
                    }
                    continue; // your own notes are heard as you play them (Sound)
                }
                var was = _heard.TryGetValue(e, out var h) && h.Start == s.StartTick ? h.Played : s.Played; // a session first heard: from now
                _heard[e] = (s.StartTick, s.Played);
                if (ctx.Reach(at.Cell) is not { } muffle)
                    continue;
                var instrument = ctx.Sim.Defs.ModDef<InstrumentDef>(InstrumentDef.Kind, s.InstrumentName);
                var place = WorldSoundContext.At(at.Cell);
                for (var i = s.Notes.Count - Math.Min(s.Played - was, s.Notes.Count); i < s.Notes.Count; i++)
                    Speak(ctx.Audio, instrument, s.Notes[i], place, muffle, BendOf(s), Strummed(s, i));
                Ring(ctx.Audio, instrument, s, $"musician:{e.Index}", place, muffle);
            }
        }
    }
}

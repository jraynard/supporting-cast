using Brutal.GlfwApi;
using Brutal.ImGuiApi;
using HeroesNotIncluded.Mods;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Work;
using HeroesNotIncluded.Ui;
using Narsil.Audio;
using Narsil.Platform;
using Narsil.Ui;

namespace HeroesNotIncluded.Bard;

/// <summary>
/// The bard's game half: the note keys, the playing window, and the notes heard, your own the moment
/// they're pressed (from the predicted session) and anyone else's from where they stand.
/// </summary>
public sealed class BardGame : IGameMod
{
    /// <summary>The note keys' controls, the scale's first degree first.</summary>
    private static string[] _notes = [];

    private static string _octave = "";

    public void Register(GameRegistry r)
    {
        GlfwKey[] keys = [GlfwKey.Number1, GlfwKey.Number2, GlfwKey.Number3, GlfwKey.Number4, GlfwKey.Number5, GlfwKey.Number6, GlfwKey.Number7, GlfwKey.Number8];
        PadButton[] pad = [PadButton.DpadLeft, PadButton.DpadDown, PadButton.DpadRight, PadButton.DpadUp, PadButton.X, PadButton.Y, PadButton.LB, PadButton.RB];
        _notes = [.. Enumerable.Range(0, 8).Select(i => r.Control($"note{i + 1}", $"Note {i + 1}", [Binding.Key(keys[i])], [Binding.Pad(pad[i])], "Music"))];
        _octave = r.Control("octave", "The octave above", [Binding.Key(GlfwKey.LeftShift)], [Binding.Pad(PadButton.L3)], "Music");
        r.WorkInput<PlaySession>(Inputs);
        r.WorkView<PlaySession>(Draw);
        r.WorkSound<PlaySession>(Sound);
        r.WorldSounds(() => new OthersPlaying());
    }

    private static IEnumerable<int> Inputs(WorkView view, PlaySession s)
    {
        var up = view.Down(_octave);
        for (var i = 0; i < _notes.Length; i++)
            if (view.Pressed(_notes[i]))
                yield return PlaySession.Pack(i, up, true);
    }

    /// <summary>How long a key stays lit after its note, in session steps.</summary>
    private const int LitSteps = 15;

    /// <summary>A step of the scale as the keys that play it: "3", or "Shift+3" in the octave above.</summary>
    private static string KeyFor(int index) =>
        index <= 7 ? WorkView.Glyph(_notes[index]) : $"{WorkView.Glyph(_octave)}+{WorkView.Glyph(_notes[index - 7])}";

    private static void Draw(WorkView view, PlaySession s)
    {
        if (s.Carrying != null)
            view.Begin(s.Title,
                ("A/D", "slower, faster"),
                ("Space", "let it go"),
                ($"{WorkView.Glyph(_notes[0])}-{WorkView.Glyph(_notes[^1])}", "a note of your own takes it back"),
                ("Esc", "put it away"));
        else
            view.Begin(s.Title,
                ($"{WorkView.Glyph(_notes[0])}-{WorkView.Glyph(_notes[^1])}", "a note of the scale"),
                (WorkView.Glyph(_octave), "hold for the octave above"),
                ("Esc", "put it away"));
        if (s.Carrying is { } tune)
        {
            ImGuiText.Colored(Theme.Good, $"Your hands take over: {tune.Label}.");
            var done = Math.Clamp((s.Next - PlaySession.Opening) / (float)Math.Max(1, tune.Notes.Count - PlaySession.Opening), 0f, 1f);
            ImGui.ProgressBar(done, new float2(Gui.S(300), Gui.S(8)), ""u8);
            ImGuiText.Colored(Theme.Dim, s.Pace < 0.98f ? $"Easing it: {s.Pace:P0} of its pace." : s.Pace > 1.02f ? $"Driving it on: {s.Pace:P0} of its pace." : "At its own pace.");
        }
        else
            ImGuiText.Colored(Theme.Dim, "Play anything; play a tune's opening and your hands carry the rest.");
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var key = Gui.S(46);
        var gap = Gui.S(6);
        var size = new float2(_notes.Length * (key + gap) - gap, key * 1.5f);
        for (var i = 0; i < _notes.Length; i++)
        {
            var degree = i;
            var lit = s.Notes.FindLastIndex(n => n.Degree == degree) is var last and >= 0 && s.StepsDone - s.Notes[last].Step < LitSteps;
            var a = min + new float2(i * (key + gap), 0);
            var b = a + new float2(key, key * 1.5f);
            dl.AddRectFilled(in a, in b, Theme.U8(lit ? Theme.Accent : Theme.Inset), Gui.S(4), ImDrawFlags.None);
            dl.AddRect(in a, in b, Theme.U8(Theme.OakLight), Gui.S(4), ImDrawFlags.None, Gui.S(2));
            var label = WorkView.Glyph(_notes[i]);
            var at = a + new float2(key * 0.5f - ImGui.CalcTextSize(label).X * 0.5f, key * 0.55f);
            dl.AddText(in at, Theme.U8(lit ? Theme.Cellar : Theme.Parchment), label);
        }
        ImGui.Dummy(in size);
        ImGuiText.Colored(Theme.Dim, s.Played == 0 ? "Not a note yet." : $"{s.Played} note{(s.Played == 1 ? "" : "s")} played{(s.Finished > 0 ? $", {s.Finished} tune{(s.Finished == 1 ? "" : "s")} through" : "")}.");
        if (s.Book.Count > 0)
        {
            ImGui.Separator();
            ImGuiText.Colored(Theme.Parchment, "The tunes you know, by how they start (from any note):");
            foreach (var t in s.Book)
            {
                var opening = string.Join(" ", Enumerable.Range(0, Math.Min(PlaySession.Opening, t.Notes.Count)).Select(i => KeyFor(t.StepAt(i))));
                ImGuiText.Colored(t == s.Carrying ? Theme.Good : Theme.Text, $"{t.Label}, {t.Kind.ToLowerInvariant()}: {opening}");
            }
        }
        view.End();
    }

    /// <summary>A note's sound: the octave's root sample pitched up to the scale's step.</summary>
    internal static (string Id, float Pitch)? Voice(InstrumentDef? instrument, NoteEvent n)
    {
        if (instrument == null || instrument.Octaves.Count == 0)
            return null;
        var id = instrument.Octaves[Math.Min(n.Octave, instrument.Octaves.Count - 1)];
        var semitones = instrument.Scale[Math.Clamp(n.Degree, 0, instrument.Scale.Count - 1)];
        return (id, MathF.Pow(2f, semitones / 12f));
    }

    /// <summary>Your own playing, heard as you press (the predicted session): every note since the last frame.</summary>
    private static void Sound(WorkSoundContext ctx, PlaySession s)
    {
        if (ctx.Fresh)
            ctx.A = s.Played;
        var fresh = Math.Min(s.Played - ctx.A, s.Notes.Count);
        var instrument = InstrumentNamed(s.InstrumentName);
        for (var i = s.Notes.Count - fresh; i < s.Notes.Count; i++)
            if (Voice(instrument, s.Notes[i]) is { } v)
                ctx.Audio.Play(v.Id, pitch: v.Pitch);
        ctx.A = s.Played;
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

        /// <summary>Session steps after the last note before the background music comes back.</summary>
        private const int QuietAfter = 4 * 60;

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
                    continue; // your own notes are heard as you play them (Sound)
                var was = _heard.TryGetValue(e, out var h) && h.Start == s.StartTick ? h.Played : s.Played; // a session first heard: from now
                _heard[e] = (s.StartTick, s.Played);
                if (s.Played == was || ctx.Reach(at.Cell) is not { } muffle)
                    continue;
                var instrument = ctx.Sim.Defs.ModDef<InstrumentDef>(InstrumentDef.Kind, s.InstrumentName);
                for (var i = s.Notes.Count - Math.Min(s.Played - was, s.Notes.Count); i < s.Notes.Count; i++)
                    if (Voice(instrument, s.Notes[i]) is { } v)
                        ctx.Audio.Play(v.Id, WorldSoundContext.At(at.Cell), pitch: v.Pitch, muffle: muffle);
            }
        }
    }
}

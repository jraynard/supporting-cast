using Brutal.ImGuiApi;
using Narsil.Ui;
using Narsil.Platform;
using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Bots;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Work;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

// Speech, hands-on work and solo bots. The play is all in the sim; this reads keys, sends
// commands and draws what the character knows.
public sealed partial class VillageGame
{
    /// <summary><c>--bots N</c>: how many stand-in players a solo round deals in.</summary>
    public int Bots { get; init; }

    private readonly List<BotPlayer> _bots = [];
    private readonly WorkPredictor _work = new();
    private double _workSteps;
    private readonly ImInputString _speechInput = new(SpeechSystem.MaxLength + 8, ""u8);
    private int _speechSeen;

    private bool Working => HasAvatar && _sim.World.Has<WorkSession>(_avatar);

    /// <summary>Numbered keys, for work that takes them (the empath's runes); the hotbar's while not working.</summary>
    private static readonly GlfwKey[] RuneKeys =
    [
        GlfwKey.Number1, GlfwKey.Number2, GlfwKey.Number3, GlfwKey.Number4, GlfwKey.Number5,
        GlfwKey.Number6, GlfwKey.Number7, GlfwKey.Number8, GlfwKey.Number9,
    ];

    // Whether F has come up since the session in hand began (see UpdateWork).
    private long _fArmedFor = -1;
    private bool _fArmed;

    /// <summary>A frame of hands-on work: F strikes, Esc puts it down.</summary>
    private void UpdateWork(float dt)
    {
        if (!HasAvatar)
        {
            _work.Reset();
            return;
        }
        var speed = Paused ? 0 : SpeedMultipliers[_speed];
        _workSteps += dt * SimClock.TicksPerSecond * speed;
        var steps = (int)_workSteps;
        _workSteps -= steps;
        var input = Host.Input;
        if (Working && input.WasKeyPressed(GlfwKey.Escape))
            Issue(new StopWorkCommand(_avatar));
        var live = speed > 0 && Working;
        var axis = !live ? 0 : (input.IsKeyDown(GlfwKey.D) ? 1 : 0) - (input.IsKeyDown(GlfwKey.A) ? 1 : 0);
        var axis2 = !live ? 0 : (input.IsKeyDown(GlfwKey.S) ? 1 : 0) - (input.IsKeyDown(GlfwKey.W) ? 1 : 0);
        // The F that started a job doesn't also work it: a new session waits for F to come up
        // before a press or hold counts (else a long press to start the quench plunges the piece).
        var session = Working ? _sim.World.Get<WorkSession>(_avatar).StartTick : -1;
        if (session != _fArmedFor)
            (_fArmedFor, _fArmed) = (session, false);
        if (!input.IsKeyDown(GlfwKey.F))
            _fArmed = true;
        var f = _fArmed && speed > 0;
        var rune = -1;
        for (var k = 0; live && k < RuneKeys.Length; k++)
            if (input.WasKeyPressed(RuneKeys[k]))
                rune = k;
        _work.Update(_sim, _avatar, steps, f && input.WasKeyPressed(GlfwKey.F), f && live && input.IsKeyDown(GlfwKey.F), axis, Issue, axis2, rune);
    }

    private void DrawWork()
    {
        if (_work.Session is SharpenSession sharpen)
        {
            DrawSharpen(sharpen);
            return;
        }
        if (_work.Session is PourSession pour)
        {
            DrawPour(pour);
            return;
        }
        switch (_work.Session)
        {
            case Sim.Healing.FeelSession feel:
                DrawFeel(feel);
                return;
            case Sim.Healing.BandageSession bandage:
                DrawBandage(bandage);
                return;
            case Sim.Healing.DrawSession draw:
                DrawDraw(draw);
                return;
            case Sim.Healing.CastSession cast:
                DrawCast(cast);
                return;
            case Sim.Healing.MeditateSession meditate:
                DrawMeditate(meditate);
                return;
            case Sim.Tinkering.ProbeSession probe:
                DrawProbe(probe);
                return;
            case Sim.Tinkering.PickSession pick:
                DrawPick(pick);
                return;
            case Sim.Tinkering.PinSession pin:
                DrawPin(pin);
                return;
            case Sim.Tinkering.FitSession fit:
                DrawFit(fit);
                return;
            case Sim.Tinkering.TrueSession truing:
                DrawTrue(truing);
                return;
            case Sim.Tinkering.LoupeSession loupe:
                DrawLoupe(loupe);
                return;
        }
        switch (_work.Session)
        {
            case HammerSession hammer:
                DrawHammer(hammer);
                return;
            case HeatSession heat:
                DrawHeat(heat);
                return;
            case QuenchSession quench:
                DrawQuench(quench);
                return;
            case StewSession stew:
                DrawStew(stew);
                return;
            case SeasonSession season:
                DrawSeason(season);
                return;
            case WashSession wash:
                DrawWash(wash);
                return;
            case TapSession tap:
                DrawTap(tap);
                return;
            case MixSession mix:
                DrawMix(mix);
                return;
            case Sim.Hospitality.ScrubSession scrub:
                DrawScrub(scrub);
                return;
            case Sim.Hospitality.MakeBedSession bed:
                DrawMakeBed(bed);
                return;
            case null:
                DrawTongs();
                DrawPotPanel();
                DrawBarPanel();
                return;
        }
    }

    /// <summary>What your character has heard, and the box for saying something. Replaces chat once a round starts.</summary>
    private void DrawSpeech()
    {
        if (_sim.Round.Phase != RoundPhase.Playing || !HasAvatar || !_showHeard)
            return;
        BeginJournal("Heard###speech", ref _showHeard, 250);
        var heard = View.Heard().ToList();
        float2? size = new float2(0, -30);
        if (ImGui.BeginChild("##speech-log"u8, in size, ImGuiChildFlags.Borders, ImGuiWindowFlags.None))
        {
            foreach (var line in heard)
            {
                var how = line.Volume == Volume.Whisper ? " whispers" : line.Volume == Volume.Shout ? " shouts" : "";
                TextC(line.Volume == Volume.Shout ? Theme.Warn : Theme.Dim, $"{line.Who}{how}:");
                ImGui.SameLine(0, 4);
                Wrapped(line.Text);
            }
            if (heard.Count != _speechSeen && ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 4)
                ImGui.SetScrollHereY(1f);
            _speechSeen = heard.Count;
        }
        ImGui.EndChild();
        ImGui.SetNextItemWidth(-1);
        if (_focusSpeech)
        {
            ImGui.SetKeyboardFocusHere(0);
            _focusSpeech = false;
        }
        if (ImGui.InputTextWithHint("##speech"u8, "Say something. /w whispers, /s shouts."u8, _speechInput, ImGuiInputTextFlags.EnterReturnsTrue, null, IntPtr.Zero))
        {
            var (volume, text) = SpeechSystem.Parse(Narsil.Ui.ImGuiText.Value(_speechInput));
            Issue(new SayCommand(_avatar, text, volume));
            _speechInput.SetValue("".AsSpan());
            ImGui.SetKeyboardFocusHere(-1); // keep typing
        }
        Panel.End();
    }

    /// <summary>How far the tankard leans at full tilt, radians: toward the tap, so the ale runs down its side.</summary>
    private const float PourMaxLean = 0.7f;

    /// <summary>
    /// The tankard from the side under the cask's tap, leaning as it's tilted (A/D): the ale and its
    /// head keep level as it tips, the line is marked inside the cup, and the stream runs while you
    /// hold F.
    /// </summary>
    private void DrawPour(PourSession s)
    {
        BeginWork(s.Title, ("F", "hold to pour"), ("A/D", "tilt: less foam, slower"), ("Esc", "stop"));
        TextC(Theme.Dim, "Stop at the line and let the head settle.");
        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(420, 200);
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(in size);
        var max = min + size;
        Gui.Frame(dl, min, max, UiTheme.Current.Frames.Inset, Theme.Well);

        // The cask's end and its tap, top right.
        var caskC = new float2(min.X + size.X * 0.5f + Gui.S(52), min.Y + Gui.S(36));
        dl.AddCircleFilled(in caskC, Gui.S(30), new ImColor8(120, 82, 46, 255), 32);
        dl.AddCircle(in caskC, Gui.S(30), Theme.U8(Theme.Iron), 32, Gui.S(3));
        var tap0 = caskC + Gui.S(-26, 18);
        var tap1 = tap0 + Gui.S(-26, 6);
        dl.AddLine(in tap0, in tap1, new ImColor8(170, 140, 90, 255), Gui.S(5));
        var spout = tap1 + Gui.S(0, 6);

        // The tankard: its foot pivots, leaning its mouth toward the tap.
        var w = Gui.S(70);
        var h = Gui.S(110);
        var pivot = new float2(min.X + size.X * 0.5f, max.Y - Gui.S(14));
        var lean = s.Tilt * PourMaxLean;
        float cos = MathF.Cos(lean), sin = MathF.Sin(lean);
        // x across the cup, y up it (negative on screen); turned clockwise by the lean, toward the tap.
        float2 Cup(float x, float y) => pivot + new float2(x * cos - y * sin, x * sin + y * cos);
        var outer = new[] { Cup(-w / 2, 0), Cup(w / 2, 0), Cup(w / 2, -h), Cup(-w / 2, -h) };
        Fill(dl, outer, new ImColor8(70, 48, 30, 255));
        var inset = Gui.S(6);
        var inner = new[] { Cup(-w / 2 + inset, -inset), Cup(w / 2 - inset, -inset), Cup(w / 2 - inset, -h), Cup(-w / 2 + inset, -h) };
        Fill(dl, inner, new ImColor8(40, 28, 18, 255));
        // The ale and its head, level whatever the lean: filled up to the height of their share of the cup's axis.
        float LevelY(float f) => Cup(0, -inset - (h - inset) * Math.Clamp(f, 0f, 1f)).Y;
        var ale = Below(inner, LevelY(s.Level));
        Fill(dl, ale, new ImColor8(184, 122, 40, 255));
        var head = Below(Above(inner, LevelY(s.Level)), LevelY(s.Top));
        Fill(dl, head, new ImColor8(240, 232, 210, 255));
        // The line, marked on both walls inside.
        var la = Cup(-w / 2 + inset, -inset - (h - inset) * s.Line);
        var lb = Cup(w / 2 - inset, -inset - (h - inset) * s.Line);
        dl.AddLine(in la, in lb, Theme.U8(Theme.Good, 0.9f), Gui.S(2));
        // The handle on the far side.
        var h0 = Cup(-w / 2, -h * 0.25f);
        var h1 = Cup(-w / 2 - Gui.S(16), -h * 0.45f);
        var h2 = Cup(-w / 2, -h * 0.7f);
        dl.AddLine(in h0, in h1, new ImColor8(70, 48, 30, 255), Gui.S(6));
        dl.AddLine(in h1, in h2, new ImColor8(70, 48, 30, 255), Gui.S(6));

        // The stream from the tap into the mouth while you hold F.
        if (s.Held)
        {
            var mouth = Cup(w * 0.2f, -h);
            var target = new float2(spout.X, Math.Max(mouth.Y, LevelY(s.Top)));
            dl.AddLine(in spout, in target, new ImColor8(200, 140, 50, 230), Gui.S(4));
        }

        TextC(Theme.Dim, $"Head {s.Foam * 100:F0}%   {PourSession.ServingsLeft(_sim, s.Cask)} left in the cask");
        EndWork();
    }

    /// <summary>A convex shape filled, as a fan of triangles.</summary>
    private static void Fill(ImDrawListPtr dl, IReadOnlyList<float2> poly, ImColor8 colour)
    {
        for (var i = 1; i + 1 < poly.Count; i++)
        {
            var a = poly[0];
            var b = poly[i];
            var c = poly[i + 1];
            dl.AddTriangleFilled(in a, in b, in c, colour);
        }
    }

    /// <summary>The part of a convex shape below a level on screen (y down): what liquid fills.</summary>
    private static List<float2> Below(IReadOnlyList<float2> poly, float level) => Clip(poly, p => p.Y >= level, level);

    /// <summary>The part of a convex shape above a level on screen.</summary>
    private static List<float2> Above(IReadOnlyList<float2> poly, float level) => Clip(poly, p => p.Y <= level, level);

    // One edge of Sutherland-Hodgman against a horizontal line.
    private static List<float2> Clip(IReadOnlyList<float2> poly, Func<float2, bool> keep, float level)
    {
        var output = new List<float2>();
        for (var i = 0; i < poly.Count; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Count];
            bool ina = keep(a), inb = keep(b);
            if (ina)
                output.Add(a);
            if (ina != inb && MathF.Abs(b.Y - a.Y) > 1e-4f)
            {
                var t = (level - a.Y) / (b.Y - a.Y);
                output.Add(a + (b - a) * t);
            }
        }
        return output;
    }

    /// <summary>The whetstone: the blade's angle against the band, the steel's heat and the edge coming back.</summary>
    private void DrawSharpen(SharpenSession s)
    {
        BeginWork(s.Title, ("F", "hold to grind"), ("A/D", "keep the angle in the band"), ("Esc", "stop"));
        TextC(Theme.Dim, "Ease off before the steel overheats.");
        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(420, 30);
        var min = ImGui.GetCursorScreenPos();
        var max = min + size;
        ImGui.Dummy(in size);
        dl.AddRectFilled(in min, in max, Theme.U8(Theme.Well), 4f, ImDrawFlags.None);
        var bandMin = new float2(min.X + (0.5f - s.HalfWidth) * size.X, min.Y);
        var bandMax = new float2(min.X + (0.5f + s.HalfWidth) * size.X, max.Y);
        dl.AddRectFilled(in bandMin, in bandMax, Theme.U8(Theme.Good, 0.7f), 2f, ImDrawFlags.None);
        var x = min.X + s.Angle * size.X;
        var needleMin = new float2(x - 2, min.Y - 3);
        var needleMax = new float2(x + 2, max.Y + 3);
        dl.AddRectFilled(in needleMin, in needleMax, Theme.U8(s.InBand ? Theme.Accent : Theme.Bad), 1f, ImDrawFlags.None);
        Bar("Edge", s.Progress, Theme.Good);
        Bar("Heat", s.Heat, s.Heat > 0.75f ? Theme.Bad : s.Heat > 0.5f ? Theme.Warn : Theme.Dim);
        EndWork();
    }

    /// <summary>The lid's seam with the probe on it, what the probe tells the fingers, and the pressure on it.</summary>
    private void DrawProbe(Sim.Tinkering.ProbeSession s)
    {
        BeginWork(s.Title, ("A/D", "run the probe along the seam"), ("F", "hold to press in"), ("Esc", "stop"));
        TextC(Theme.Dim, "Sweep light. Where it catches, stop and ease the spring out. The far end finishes.");
        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(420, 26);
        var min = ImGui.GetCursorScreenPos();
        var max = min + size;
        ImGui.Dummy(in size);
        dl.AddRectFilled(in min, in max, new ImColor8(92, 62, 36, 255), 3f, ImDrawFlags.None);
        var seamMin = new float2(min.X + 4, min.Y + size.Y * 0.5f - 1);
        var seamMax = new float2(max.X - 4, min.Y + size.Y * 0.5f + 1);
        dl.AddRectFilled(in seamMin, in seamMax, new ImColor8(30, 22, 16, 255), 0f, ImDrawFlags.None);
        var x = min.X + 4 + s.Probe * (size.X - 8);
        var probeMin = new float2(x - 2, min.Y - 3);
        var probeMax = new float2(x + 2, max.Y + 3);
        dl.AddRectFilled(in probeMin, in probeMax, Theme.U8(Theme.Accent), 1f, ImDrawFlags.None);
        TextC(Theme.Dim, "hasp");
        ImGui.SameLine(size.X - 24, 0);
        TextC(Theme.Dim, "hinge");
        Bar("Feel", s.Feel, s.Feel > 0.6f ? Theme.Warn : Theme.Dim);
        // The pressure gauge, with the band that eases a spring out marked on it.
        var gMin = ImGui.GetCursorScreenPos();
        var gSize = Gui.S(420, 16);
        var gMax = gMin + gSize;
        ImGui.Dummy(in gSize);
        dl.AddRectFilled(in gMin, in gMax, Theme.U8(Theme.Well), 3f, ImDrawFlags.None);
        var bandMin = new float2(gMin.X + s.Low * gSize.X, gMin.Y);
        var bandMax = new float2(gMin.X + s.High * gSize.X, gMax.Y);
        dl.AddRectFilled(in bandMin, in bandMax, Theme.U8(Theme.Good, 0.6f), 2f, ImDrawFlags.None);
        var dangerMin = new float2(gMin.X + Sim.Tinkering.ProbeSession.SpringAbove * gSize.X, gMin.Y);
        dl.AddRectFilled(in dangerMin, in gMax, Theme.U8(Theme.Bad, 0.6f), 2f, ImDrawFlags.None);
        var pMin = new float2(gMin.X, gMin.Y + 4);
        var pMax = new float2(gMin.X + s.Pressure * gSize.X, gMax.Y - 4);
        dl.AddRectFilled(in pMin, in pMax, Theme.U8(s.Pressure > Sim.Tinkering.ProbeSession.LightTouch ? Theme.Accent : Theme.Text), 1f, ImDrawFlags.None);
        if (s.Progress > 0f)
            Bar("Spring", s.Progress, Theme.Good);
        EndWork();
    }

    /// <summary>The pins set so far, the one being lifted, and how much it gives.</summary>
    private void DrawPick(Sim.Tinkering.PickSession s)
    {
        BeginWork(s.Title, ("A/D", "lift the pin"), ("F", "set it where it gives most"), ("Esc", "stop"));
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var W = Gui.S(36); var H = Gui.S(120);
        ImGui.Dummy(new float2(420, H));
        for (var i = 0; i < s.Pins; i++)
        {
            var colMin = new float2(origin.X + 10 + i * (W + 12), origin.Y);
            var colMax = new float2(colMin.X + W, origin.Y + H);
            dl.AddRectFilled(in colMin, in colMax, Theme.U8(Theme.Well), 3f, ImDrawFlags.None);
            var lift = i < s.Pin ? 1f : i == s.Pin ? s.Lift : 0f;
            var pinTop = colMax.Y - 10 - lift * (H - 20);
            var pinMin = new float2(colMin.X + 8, pinTop);
            var pinMax = new float2(colMax.X - 8, pinTop + 10);
            var colour = i < s.Pin ? Theme.Good : i == s.Pin ? (s.Feel > 0.7f ? Theme.Warn : Theme.Accent) : Theme.Dim;
            dl.AddRectFilled(in pinMin, in pinMax, Theme.U8(colour), 2f, ImDrawFlags.None);
        }
        Bar("Give", s.Feel, s.Feel > 0.7f ? Theme.Warn : Theme.Dim);
        TextC(s.Slips > 0 ? Theme.Warn : Theme.Dim, $"Slips {s.Slips} of {Sim.Tinkering.PickSession.MaxSlips}");
        EndWork();
    }

    /// <summary>
    /// The work in hand's window, centred just above the hotbar: a title plate, the keys as caps,
    /// then the game. Every mini-game opens it this way; pair with <see cref="EndWork"/>.
    /// </summary>
    private void BeginWork(string title, params (string Keys, string Does)[] hints)
    {
        _hud.Next(Anchor.Bottom);
        Panel.Begin($"{title}###work", Hud.Flags | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoNav);
        _hud.Took();
        Gui.Hints(hints);
        ImGui.Spacing();
    }

    private static void EndWork() => Panel.End();

    private void RunBots()
    {
        foreach (var bot in _bots)
            bot.Tick();
    }
}

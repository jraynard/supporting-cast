using Brutal.ImGuiApi;
using Narsil.Ui;
using Narsil.Platform;
using SupportingCast.Sim;
using SupportingCast.Sim.Bots;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Round;
using SupportingCast.Sim.Speech;
using SupportingCast.Sim.Work;
using SupportingCast.Ui;

namespace SupportingCast;

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
        // The F that started a job doesn't also work it: a new session waits for F to come up
        // before a press or hold counts (else a long press to start the quench plunges the piece).
        var session = Working ? _sim.World.Get<WorkSession>(_avatar).StartTick : -1;
        if (session != _fArmedFor)
            (_fArmedFor, _fArmed) = (session, false);
        if (!input.IsKeyDown(GlfwKey.F))
            _fArmed = true;
        var f = _fArmed && speed > 0;
        _work.Update(_sim, _avatar, steps, f && input.WasKeyPressed(GlfwKey.F), f && live && input.IsKeyDown(GlfwKey.F), axis, Issue);
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
            case Sim.Healing.DrawSession draw:
                DrawDraw(draw);
                return;
            case Sim.Healing.CastSession cast:
                DrawCast(cast);
                return;
            case Sim.Healing.MeditateSession meditate:
                DrawMeditate(meditate);
                return;
            case Sim.Locks.ProbeSession probe:
                DrawProbe(probe);
                return;
            case Sim.Locks.PickSession pick:
                DrawPick(pick);
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
            case null:
                DrawTongs();
                DrawPotPanel();
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

    /// <summary>The tankard from the side: the drink, the head on it, and the line to bring it to.</summary>
    private void DrawPour(PourSession s)
    {
        BeginWork(s.Title, ("F", "hold to pour"), ("A/D", "tilt: less foam, slower"), ("Esc", "stop"));
        TextC(Theme.Dim, "Stop at the line and let the head settle.");
        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(90, 160);
        var origin = ImGui.GetCursorScreenPos();
        var min = new float2(origin.X + Gui.S(165), origin.Y);
        var max = min + size;
        ImGui.Dummy(new float2(Gui.S(420), size.Y));
        dl.AddRectFilled(in min, in max, new ImColor8(70, 48, 30, 255), 6f, ImDrawFlags.None);
        float Y(float f) => max.Y - Math.Clamp(f, 0f, 1f) * size.Y;
        var drinkMin = new float2(min.X + 6, Y(s.Level));
        var drinkMax = new float2(max.X - 6, max.Y - 4);
        dl.AddRectFilled(in drinkMin, in drinkMax, new ImColor8(184, 122, 40, 255), 2f, ImDrawFlags.None);
        var foamMin = new float2(min.X + 6, Y(s.Top));
        var foamMax = new float2(max.X - 6, Y(s.Level));
        dl.AddRectFilled(in foamMin, in foamMax, new ImColor8(240, 232, 210, 255), 2f, ImDrawFlags.None);
        var lineMin = new float2(min.X - 10, Y(s.Line) - 1);
        var lineMax = new float2(max.X + 10, Y(s.Line) + 1);
        dl.AddRectFilled(in lineMin, in lineMax, Theme.U8(Theme.Good), 0f, ImDrawFlags.None);
        TextC(Theme.Dim, $"Tilt {s.Tilt * 100:F0}%   Head {s.Foam * 100:F0}%   {PourSession.ServingsLeft(_sim, s.Cask)} left in the cask");
        EndWork();
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
    private void DrawProbe(Sim.Locks.ProbeSession s)
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
        var dangerMin = new float2(gMin.X + Sim.Locks.ProbeSession.SpringAbove * gSize.X, gMin.Y);
        dl.AddRectFilled(in dangerMin, in gMax, Theme.U8(Theme.Bad, 0.6f), 2f, ImDrawFlags.None);
        var pMin = new float2(gMin.X, gMin.Y + 4);
        var pMax = new float2(gMin.X + s.Pressure * gSize.X, gMax.Y - 4);
        dl.AddRectFilled(in pMin, in pMax, Theme.U8(s.Pressure > Sim.Locks.ProbeSession.LightTouch ? Theme.Accent : Theme.Text), 1f, ImDrawFlags.None);
        if (s.Progress > 0f)
            Bar("Spring", s.Progress, Theme.Good);
        EndWork();
    }

    /// <summary>The pins set so far, the one being lifted, and how much it gives.</summary>
    private void DrawPick(Sim.Locks.PickSession s)
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
        TextC(s.Slips > 0 ? Theme.Warn : Theme.Dim, $"Slips {s.Slips} of {Sim.Locks.PickSession.MaxSlips}");
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

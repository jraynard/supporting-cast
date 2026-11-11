using Brutal.ImGuiApi;
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
        _work.Update(_sim, _avatar, steps, speed > 0 && input.WasKeyPressed(GlfwKey.F), live && input.IsKeyDown(GlfwKey.F), axis, Issue);
    }

    private void DrawWork()
    {
        if (_work.Session is SharpenSession sharpen)
        {
            DrawSharpen(sharpen);
            return;
        }
        if (_work.Session is not HammerSession hammer)
            return;
        var screen = ImGui.GetIO().DisplaySize;
        var pos = new float2(screen.X * 0.5f, screen.Y * 0.68f);
        float2? pivot = new float2(0.5f, 0.5f);
        ImGui.SetNextWindowPos(in pos, ImGuiCond.Always, in pivot);
        ImGui.Begin("###work"u8, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav);
        TextC(Theme.Accent, hammer.Title);
        TextC(Theme.Dim, $"Blow {Math.Min(hammer.Blows + 1, HammerSession.BlowsNeeded)} of {HammerSession.BlowsNeeded}. F to strike on the mark, Esc to stop.");

        var dl = ImGui.GetWindowDrawList();
        var size = new float2(420, 34);
        var min = ImGui.GetCursorScreenPos();
        var max = min + size;
        ImGui.Dummy(in size);
        dl.AddRectFilled(in min, in max, new ImColor8(40, 30, 24, 255), 4f, ImDrawFlags.None);
        var markMin = new float2(min.X + (hammer.Mark - hammer.HalfWidth) * size.X, min.Y);
        var markMax = new float2(min.X + (hammer.Mark + hammer.HalfWidth) * size.X, max.Y);
        dl.AddRectFilled(in markMin, in markMax, Theme.U8(Theme.Good), 2f, ImDrawFlags.None);
        var x = min.X + hammer.Marker * size.X;
        var barMin = new float2(x - 2, min.Y - 3);
        var barMax = new float2(x + 2, max.Y + 3);
        dl.AddRectFilled(in barMin, in barMax, Theme.U8(hammer.Ready ? Theme.Accent : Theme.Dim), 1f, ImDrawFlags.None);
        ImGui.End();
    }

    /// <summary>What your character has heard, and the box for saying something. Replaces chat once a round starts.</summary>
    private void DrawSpeech()
    {
        if (_sim.Round.Phase != RoundPhase.Playing || !HasAvatar)
            return;
        SetNextWindow(new float2(Screen.X - 430, Screen.Y - 470), new float2(420, 230));
        ImGui.Begin("Heard###speech"u8, ImGuiWindowFlags.NoCollapse);
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
        if (ImGui.InputTextWithHint("##speech"u8, "Say something. /w whispers, /s shouts."u8, _speechInput, ImGuiInputTextFlags.EnterReturnsTrue, null, IntPtr.Zero))
        {
            var (volume, text) = SpeechSystem.Parse(Narsil.Ui.ImGuiText.Value(_speechInput));
            Issue(new SayCommand(_avatar, text, volume));
            _speechInput.SetValue("".AsSpan());
            ImGui.SetKeyboardFocusHere(-1); // keep typing
        }
        ImGui.End();
    }

    /// <summary>The whetstone: the blade's angle against the band, the steel's heat and the edge coming back.</summary>
    private void DrawSharpen(SharpenSession s)
    {
        var screen = ImGui.GetIO().DisplaySize;
        var pos = new float2(screen.X * 0.5f, screen.Y * 0.68f);
        float2? pivot = new float2(0.5f, 0.5f);
        ImGui.SetNextWindowPos(in pos, ImGuiCond.Always, in pivot);
        ImGui.Begin("###work"u8, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav);
        TextC(Theme.Accent, s.Title);
        TextC(Theme.Dim, "Hold F to grind, A/D to keep the angle in the band. Ease off before it overheats. Esc to stop.");
        var dl = ImGui.GetWindowDrawList();
        var size = new float2(420, 30);
        var min = ImGui.GetCursorScreenPos();
        var max = min + size;
        ImGui.Dummy(in size);
        dl.AddRectFilled(in min, in max, new ImColor8(40, 30, 24, 255), 4f, ImDrawFlags.None);
        var bandMin = new float2(min.X + (0.5f - s.HalfWidth) * size.X, min.Y);
        var bandMax = new float2(min.X + (0.5f + s.HalfWidth) * size.X, max.Y);
        dl.AddRectFilled(in bandMin, in bandMax, Theme.U8(Theme.Good, 0.7f), 2f, ImDrawFlags.None);
        var x = min.X + s.Angle * size.X;
        var needleMin = new float2(x - 2, min.Y - 3);
        var needleMax = new float2(x + 2, max.Y + 3);
        dl.AddRectFilled(in needleMin, in needleMax, Theme.U8(s.InBand ? Theme.Accent : Theme.Bad), 1f, ImDrawFlags.None);
        Bar("Edge", s.Progress, Theme.Good);
        Bar("Heat", s.Heat, s.Heat > 0.75f ? Theme.Bad : s.Heat > 0.5f ? Theme.Warn : Theme.Dim);
        ImGui.End();
    }

    private void RunBots()
    {
        foreach (var bot in _bots)
            bot.Tick();
    }
}

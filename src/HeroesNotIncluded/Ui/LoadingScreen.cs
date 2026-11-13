using Brutal.ImGuiApi;
using Narsil;
using Narsil.Rendering;
using Narsil.Ui;

namespace HeroesNotIncluded.Ui;

/// <summary>
/// Between the title and a village: the logo on the cellar dark, a loading line from the brand's
/// rotation, and three embers breathing, while the village is made ready on a worker thread. The
/// start (a new village's world, a save read off disk) touches only the sim, never the GPU or
/// ImGui, so the window keeps drawing; once it's ready the host switches to the game, whose own
/// <see cref="Game.Load"/> then does the GPU work on the main thread. A start that fails comes back
/// to the title with what went wrong.
/// </summary>
public sealed class LoadingScreen(Func<Game> start, Func<string, Game> back) : Game
{
    /// <summary>The brand's loading lines, one at a time (docs: brand guide, "Loading lines").</summary>
    public static readonly string[] Lines =
    [
        "Sweeping the common room...", "Hiding the good ale...", "Re-hanging the quest board...", "Practising our polite smile...",
        "Polishing tankards nobody asked about...",
    ];

    /// <summary>Seconds each line stays before the next.</summary>
    private const float LineSeconds = 2.4f;

    private Task<Game>? _work;
    private float _time;
    private readonly int _firstLine = Random.Shared.Next(Lines.Length);

    public override void Load()
    {
        var dataDirectory = Host.DataDirectory;
        _work = Task.Run(() =>
        {
            var game = start();
            (game as VillageGame)?.PrepareWorld(dataDirectory);
            return game;
        });
    }

    public override void Update(float dt)
    {
        _time += dt;
        if (_work is not { IsCompleted: true } work)
            return;
        if (work.IsCompletedSuccessfully)
        {
            Host.SwitchTo(work.Result);
            return;
        }
        var error = work.Exception?.GetBaseException() ?? new Exception("It didn't start.");
        Log.Engine.Warning($"Couldn't start: {error}");
        Host.SwitchTo(back(error is Sim.Saving.SaveException ? error.Message : $"Something went wrong getting the village ready: {error.Message}"));
        _work = null;
    }

    public override void DrawUi()
    {
        var screen = ImGui.GetIO().DisplaySize;
        var dl = ImGui.GetBackgroundDrawList(ImGui.GetMainViewport());
        var zero = float2.Zero;
        dl.AddRectFilled(in zero, in screen, Theme.U8(Theme.Cellar), 0f, ImDrawFlags.None);
        // A warm glow low in the middle, like a hearth seen through a door.
        var glowTop = Theme.U8(Theme.Accent, 0f);
        var glowBottom = Theme.U8(Theme.Accent, 0.07f + 0.02f * MathF.Sin(_time * 1.7f));
        var gMin = new float2(0, screen.Y * 0.55f);
        dl.AddRectFilledMultiColor(in gMin, in screen, glowTop, glowTop, glowBottom, glowBottom);

        var middle = screen * 0.5f;
        if (Gui.TrySprite("ui/logo", out var logo) && logo.PixelSize.Y > 0)
        {
            var h = MathF.Min(screen.Y * 0.42f, Gui.S(360));
            var w = h * logo.PixelSize.X / logo.PixelSize.Y;
            var min = new float2(middle.X - w * 0.5f, middle.Y - h * 0.62f);
            Gui.Image(dl, logo, min, min + new float2(w, h), Gui.White);
        }

        var line = Lines[(_firstLine + (int)(_time / LineSeconds)) % Lines.Length];
        var fade = Math.Clamp(MathF.Min(_time % LineSeconds, LineSeconds - _time % LineSeconds) * 3f, 0f, 1f);
        float textY;
        using (Gui.DisplayFont(28f))
        {
            var size = ImGui.CalcTextSize(line);
            var at = new float2((screen.X - size.X) * 0.5f, middle.Y + MathF.Min(screen.Y * 0.42f, Gui.S(360)) * 0.45f);
            dl.AddText(in at, Theme.U8(Theme.Parchment, fade), line);
            textY = at.Y + size.Y;
        }
        // Three embers, breathing one after another: it's working, not stuck.
        for (var i = 0; i < 3; i++)
        {
            var glow = 0.35f + 0.65f * MathF.Max(0f, MathF.Sin(_time * 3f - i * 0.9f));
            var at = new float2(middle.X + (i - 1) * Gui.S(18), textY + Gui.S(22));
            dl.AddCircleFilled(in at, Gui.S(5), Theme.U8(Theme.Accent, glow * 0.35f), 16);
            dl.AddCircleFilled(in at, Gui.S(3), Theme.U8(Theme.Accent, glow), 12);
        }
    }

    public override void Draw(in FrameContext frame) { }
}

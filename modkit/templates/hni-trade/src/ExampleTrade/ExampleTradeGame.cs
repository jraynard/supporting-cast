using Brutal.ImGuiApi;
using HeroesNotIncluded.Mods;
using HeroesNotIncluded.Ui;
using Narsil.Ui;

namespace ExampleTrade;

/// <summary>
/// The trade's game half (docs/MODDING.md): how its mini-game is drawn and sounds. A view reads only the
/// session (and what the player perceives, <see cref="WorkView.Perceived"/>): never hidden state.
/// </summary>
public sealed class ExampleTradeGame : IGameMod
{
    public void Register(GameRegistry r)
    {
        r.WorkView<ExampleTradeSession>(Draw);
        r.WorkSound<ExampleTradeSession>(Sound);
    }

    private static void Draw(WorkView view, ExampleTradeSession s)
    {
        view.Begin(s.Title, ("F", "strike when the marker's over the mark"), ("Esc", "stop"));
        ImGuiText.Colored(Theme.Dim, $"{s.Good} of {ExampleTradeSession.Strokes} good strokes.");
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var size = new float2(Gui.S(320), Gui.S(28));
        var max = min + size;
        dl.AddRectFilled(in min, in max, Theme.U8(Theme.Inset), Gui.S(4), ImDrawFlags.None);
        // The mark, then the marker over it.
        var m0 = new float2(min.X + size.X * (ExampleTradeSession.Mark - ExampleTradeSession.MarkWidth / 2f), min.Y);
        var m1 = new float2(min.X + size.X * (ExampleTradeSession.Mark + ExampleTradeSession.MarkWidth / 2f), max.Y);
        dl.AddRectFilled(in m0, in m1, Theme.U8(Theme.AccentSoft), 0f, ImDrawFlags.None);
        var x = min.X + size.X * s.Marker;
        var p0 = new float2(x - Gui.S(2), min.Y - Gui.S(3));
        var p1 = new float2(x + Gui.S(2), max.Y + Gui.S(3));
        dl.AddRectFilled(in p0, in p1, Theme.U8(Theme.Parchment), 0f, ImDrawFlags.None);
        view.TakesPointer(min, max); // a click on the bar is F
        ImGui.Dummy(in size);
        view.End();
    }

    /// <summary>A sound each good stroke (the game's own "ui.open" for now: give the mod a sounds/sounds.json of its own).</summary>
    private static void Sound(WorkSoundContext ctx, ExampleTradeSession s)
    {
        if (s.Good > ctx.A)
            ctx.Audio.Play("ui.open", gain: 0.6f);
        ctx.A = s.Good;
    }
}

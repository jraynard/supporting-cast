using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Ecs;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded;

/// <summary>The village screen as mods see it (<see cref="Mods.WorkView"/>): only what they're given here.</summary>
public sealed partial class VillageGame
{
    private Mods.WorkView? _modWorkView;

    private Mods.WorkView ModWorkView => _modWorkView ??= new Mods.WorkView(this);

    internal Simulation CurrentSim => _sim;

    internal PerceptionView Perceived => View;

    internal float Time => _time;

    internal void BeginWorkWindow(string title, (string Keys, string Does)[] hints) => BeginWork(title, hints);

    internal void EndWorkWindow() => EndWork();

    internal void IssueCommand(ISimCommand command) => Issue(command);

    internal void BeginWorkWindow(string title, bool mouse, (string Keys, string Does)[] hints) => BeginWork(title, mouse, hints);

    internal Sim.Work.WorkSession? WorkInHand => _work.Session;

    internal Entity Me => HasAvatar ? _avatar : Entity.Null;

    internal bool IsWorking => Working;

    internal void MarkPointer(float2 min, float2 max, bool wheel, bool edges) => TakesPointer(min, max, wheel, edges);

    internal void WorkAside() => _workAside = true;

    internal float2 OnScreen(float degrees)
    {
        var rad = degrees * MathF.PI / 180f;
        var (cos, sin) = (MathF.Cos(rad), MathF.Sin(rad));
        return Frame() is { } f ? f.East * cos - f.North * sin : new float2(cos, sin);
    }

    internal void ToolSwing(Brutal.ImGuiApi.ImDrawListPtr dl, string sprite, float faceAlong, float raise, float2 strike, float px, Brutal.ImGuiApi.ImColor8 tint) =>
        DrawToolSwing(dl, sprite, faceAlong, raise, strike, px, tint);

    internal void ShowTip(string text) => Tip(text);

    internal float2? AutoSize => _autoSize;

    internal bool WorkLive => !Paused && Working;

    /// <summary>In the world with a character, the game running: a mod's overlay reads its controls only then.</summary>
    internal bool InWorld => HasAvatar && !Paused;

    /// <summary>A panel in the HUD's stack at <paramref name="anchor"/> (nothing overlaps), for a mod's overlay. Pair with <see cref="Narsil.Ui.Panel.End"/>.</summary>
    internal void HudPanel(string id, Narsil.Ui.Anchor anchor)
    {
        _hud.Next(anchor);
        Narsil.Ui.Panel.Begin(id, Narsil.Ui.Hud.Flags | Brutal.ImGuiApi.ImGuiWindowFlags.AlwaysAutoResize | Brutal.ImGuiApi.ImGuiWindowFlags.NoNav
            | Brutal.ImGuiApi.ImGuiWindowFlags.NoDecoration | Brutal.ImGuiApi.ImGuiWindowFlags.NoInputs);
        _hud.Took();
    }
}

using Brutal.ImGuiApi;
using HeroesNotIncluded.Sim.Components;

namespace HeroesNotIncluded;

// Being at the work: when your hands are on a job the view eases in on you at the station, the
// work's panel stands aside to the right, and the rest of the HUD quiets, so the work happens in the
// world you're standing in, not in a box over it. It eases back when you stop. Settings' "Lean in
// at work" turns it off. Nothing here touches the sim.
public sealed partial class VillageGame
{
    /// <summary>How far in the view has leaned toward the work, 0..1.</summary>
    private float _workLean;

    /// <summary>The zoom factor last applied for the lean, so it can be taken back off as it eases.</summary>
    private float _leanZoomApplied = 1f;

    /// <summary>How much closer the view comes at work.</summary>
    private const float WorkLeanZoom = 1.55f;

    /// <summary>Seconds to lean in or back out.</summary>
    private const float WorkLeanSeconds = 0.6f;

    /// <summary>Where across the screen you're framed at work, as a share of its width left of centre (the panel takes the right).</summary>
    private const float WorkLeanAside = 0.2f;

    /// <summary>The HUD's opacity at full lean: still there to glance at, not in the way.</summary>
    private const float QuietHudAlpha = 0.3f;

    private bool WorkCameraOn => Settings.Current.WorkCamera && !_cameraMode;

    /// <summary>
    /// Ease the lean toward the work in hand (or away when there's none), and put the zoom it asks
    /// for on the camera: the player's own zoom underneath is kept and comes back as it eases off.
    /// </summary>
    private void UpdateWorkCamera(float dt)
    {
        var want = WorkCameraOn && Working ? 1f : 0f;
        var step = Math.Min(dt, 0.1f) / WorkLeanSeconds;
        _workLean = want > _workLean ? Math.Min(want, _workLean + step) : Math.Max(want, _workLean - step);
        var eased = _workLean * _workLean * (3f - 2f * _workLean);
        var factor = 1f + (WorkLeanZoom - 1f) * eased;
        if (MathF.Abs(factor - _leanZoomApplied) < 1e-4f)
            return;
        var own = _camera.Zoom / _leanZoomApplied;
        // Never past the camera's nearest: what can't be applied isn't counted as applied.
        factor = Math.Min(factor, _camera.MaxZoom / Math.Max(own, 1e-3f));
        _camera.Zoom = own * factor;
        _leanZoomApplied = factor;
    }

    /// <summary>
    /// How far the camera's aim moves off your character at work: to their side, so they stand in
    /// the clear part of the screen left of the work's panel.
    /// </summary>
    private float3 WorkLeanOffset()
    {
        if (_workLean <= 0f)
            return default;
        var eased = _workLean * _workLean * (3f - 2f * _workLean);
        var across = Screen.X * WorkLeanAside / Math.Max(_camera.Zoom, 1e-3f);
        return _camera.Right * across * eased;
    }

    /// <summary>The work's panel goes aside to the right while the view leans in on the work.</summary>
    private bool WorkPanelAside => WorkCameraOn && _workLean > 0f;

    /// <summary>Quiet the HUD panels that aren't about the work while leaning in: pair with <see cref="EndQuietHud"/>.</summary>
    private void BeginQuietHud()
    {
        var eased = _workLean * _workLean * (3f - 2f * _workLean);
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 1f - (1f - QuietHudAlpha) * eased);
    }

    private static void EndQuietHud() => ImGui.PopStyleVar();
}

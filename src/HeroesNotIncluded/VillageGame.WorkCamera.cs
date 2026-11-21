using Brutal.ImGuiApi;
using HeroesNotIncluded.Sim.Components;

namespace HeroesNotIncluded;

// Being at the work: when your hands are on a job the view eases in on you at the station, the
// work's panel drops to the bottom of the screen with you framed above it, and the rest of the HUD quiets, so the work happens in the
// world you're standing in, not in a box over it. It eases back when you stop. Settings' "Lean in
// at work" turns it off. Nothing here touches the sim.
public sealed partial class VillageGame
{
    /// <summary>How far in the view has leaned toward the work, 0..1.</summary>
    private float _workLean;

    /// <summary>The zoom factor last applied for the lean, so it can be taken back off as it eases.</summary>
    private float _leanZoomApplied = 1f;

    /// <summary>How much closer the view comes at work.</summary>
    private const float WorkLeanZoom = 2.2f;

    /// <summary>How much lower the view comes round at work, toward eye level, in degrees off its pitch (never below <see cref="WorkLeanLowestPitch"/>).</summary>
    private const float WorkLeanPitch = 14f;

    private const float WorkLeanLowestPitch = 20f;

    /// <summary>The pitch last taken off for the lean, radians, so it comes back as it eases.</summary>
    private float _leanPitchApplied;

    /// <summary>The camera's nearest zoom outside the lean, raised while leaning so a scroll mid-work doesn't snap it back.</summary>
    private float? _baseMaxZoom;

    /// <summary>Seconds to lean in or back out.</summary>
    private const float WorkLeanSeconds = 0.6f;

    /// <summary>Where you're framed at work: a share of the screen's height above its middle, and of its width left of it (the panel sits low and right).</summary>
    private const float WorkLeanLift = 0.17f, WorkLeanLeft = 0.08f;

    /// <summary>Where the work's panel sits while leaning in: its bottom this share of the screen up from the bottom, its middle this share right of centre.</summary>
    private const float WorkPanelRaise = 0.07f, WorkPanelRight = 0.1f;

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
        _baseMaxZoom ??= _camera.MaxZoom;
        _camera.MaxZoom = _baseMaxZoom.Value * factor;
        if (MathF.Abs(factor - _leanZoomApplied) > 1e-4f)
        {
            var own = _camera.Zoom / _leanZoomApplied;
            _camera.Zoom = own * factor;
            _leanZoomApplied = factor;
        }
        // Lower, toward eye level: off the player's own pitch, which comes back as it eases off.
        var ownPitch = _camera.Pitch + _leanPitchApplied;
        var lowest = WorkLeanLowestPitch * MathF.PI / 180f;
        var pitch = Math.Max(Math.Min(ownPitch, lowest), ownPitch - WorkLeanPitch * MathF.PI / 180f * eased);
        _camera.Pitch = pitch;
        _leanPitchApplied = ownPitch - _camera.Pitch;
    }

    /// <summary>
    /// How far the camera's aim moves off your character at work: enough that they stand in the upper
    /// part of the screen, clear of the work's panel below. Found through the camera itself (the ground
    /// under the middle of the screen against the ground under where they should be), so it holds at any
    /// turn and pitch of the view.
    /// </summary>
    private float3 WorkLeanOffset()
    {
        if (_workLean <= 0f)
            return default;
        var eased = _workLean * _workLean * (3f - 2f * _workLean);
        var z = _camera.Target.Z;
        var middle = Screen * 0.5f;
        var above = new float2(Screen.X * (0.5f - WorkLeanLeft * eased), Screen.Y * (0.5f - WorkLeanLift));
        return (_camera.ScreenToPlane(middle, z) - _camera.ScreenToPlane(above, z)) * eased;
    }

    /// <summary>Where the work's panel goes while leaning in (its bottom middle), or null to leave it be.</summary>
    private float2? WorkPanelAt(float2 lo, float2 hi) =>
        WorkPanelAside ? new float2((lo.X + hi.X) * 0.5f + Screen.X * WorkPanelRight, hi.Y - Screen.Y * WorkPanelRaise) : null;

    /// <summary>The work's panel goes low and a little right while the view leans in on the work.</summary>
    private bool WorkPanelAside => WorkCameraOn && _workLean > 0f;

    /// <summary>Quiet the HUD panels that aren't about the work while leaning in: pair with <see cref="EndQuietHud"/>.</summary>
    private void BeginQuietHud()
    {
        var eased = _workLean * _workLean * (3f - 2f * _workLean);
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, 1f - (1f - QuietHudAlpha) * eased);
    }

    private static void EndQuietHud() => ImGui.PopStyleVar();
}

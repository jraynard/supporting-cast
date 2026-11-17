using Brutal.ImGuiApi;
using Narsil.Platform;
using Narsil.Rendering;
using Narsil.Ui;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Ecs;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

// Camera mode (` or R3): the camera comes off your character and flies free, for filming. It
// glides (Narsil's CinematicCamera), orbits and tilts, follows whoever you point at, turns slowly
// by itself, and the HUD can go. Your character stands where they are meanwhile.
//
// It's honest in a live round: the picture is still drawn through your character's (or ghost's)
// Perception, so flying across town shows only what they'd know, and you hear from where they
// stand, not from the camera. Outside a round (practice, demos, god mode) it sees everything. The
// clock can be paused or slowed only in solo play: in co-op the town never waits for one player.
public sealed partial class VillageGame
{
    private bool _cameraMode;
    private CinematicCamera? _cine;

    /// <summary>The HUD hidden (camera mode's H): nothing but the world on screen.</summary>
    private bool _uiHidden;

    /// <summary>Who camera mode follows: a creature pointed at, or yourself (<see cref="_followMe"/>).</summary>
    private Entity _filmFollow = Entity.Null;
    private bool _followMe;

    private bool _filmPaused;
    private int _filmSlow; // index into SlowSteps

    /// <summary>Where the view stood when camera mode began: it turns and tilts back there after.</summary>
    private float _yawHome;
    private float? _pitchHome;

    /// <summary>Easing the camera back to your character after camera mode, seconds left.</summary>
    private float _cameraReturn;
    private float3 _returnFrom;

    private const float ReturnSeconds = 0.6f;
    private const float TurntableSpeed = 0.15f; // radians a second: a slow showcase turn
    private static readonly float[] SlowSteps = [1f, 0.5f, 0.25f];

    /// <summary>A live round seen as your character (or ghost): the camera shows only what they know.</summary>
    private bool CameraHonest => _sim.Round.Live && (HasAvatar || Ghosting);

    /// <summary>Only solo play may stop or slow the clock for the camera.</summary>
    private bool FilmClock => _cameraMode && _coop == null;

    /// <summary>The clock held still by camera mode (solo only).</summary>
    private bool FilmPaused => FilmClock && _filmPaused;

    /// <summary>How fast the clock runs under camera mode's slow motion (solo only), 1 otherwise.</summary>
    private float FilmTimeScale => FilmClock ? SlowSteps[_filmSlow] : 1f;

    /// <summary>Toggle camera mode (its action), and Back leaves it. True if Back was spent here.</summary>
    private bool HandleCameraModeToggle()
    {
        var map = Controls.Map;
        if (ImGui.GetIO().WantTextInput)
            return false;
        if (map.Pressed(Act.CameraMode) && (_cameraMode || !Working))
        {
            if (_cameraMode)
                LeaveCameraMode();
            else
                EnterCameraMode();
            return true;
        }
        if (_cameraMode && map.Pressed(Act.Back))
        {
            LeaveCameraMode();
            return true;
        }
        return false;
    }

    private void EnterCameraMode()
    {
        _cameraMode = true;
        _cine ??= new CinematicCamera(_camera);
        _cine.Stop();
        _cine.Bounds = _cameraController.Bounds;
        _cine.Follow = null;
        _cine.Turntable = 0f;
        _filmFollow = Entity.Null;
        _followMe = false;
        _filmPaused = false;
        _filmSlow = 0;
        _yawHome = _camera.Yaw;
        _pitchHome ??= _camera.Pitch;
        _cameraReturn = 0f;
        _lookingAt = null;
    }

    private void LeaveCameraMode()
    {
        _cameraMode = false;
        _uiHidden = false;
        _filmPaused = false;
        _filmSlow = 0;
        _filmFollow = Entity.Null;
        _followMe = false;
        if (_cine != null)
        {
            _cine.Follow = null;
            _cine.Turntable = 0f;
            _cine.Stop();
        }
        // Turn back the short way: the stepped view eases from within half a turn of home.
        _camera.Yaw = _yawHome + MathF.IEEERemainder(_camera.Yaw - _yawHome, MathF.Tau);
        if (HasAvatar || Ghosting)
        {
            _returnFrom = _camera.Target;
            _cameraReturn = ReturnSeconds;
        }
    }

    /// <summary>Once a frame in camera mode, in place of the usual camera: fly it.</summary>
    private void UpdateCameraMode(Input input, float dt)
    {
        if (_cine == null)
            return;
        if (Working)
        {
            LeaveCameraMode(); // hands-on work takes the keys back
            return;
        }
        var map = Controls.Map;
        var io = ImGui.GetIO();
        var intent = default(CinematicCamera.Intent);
        if (!io.WantTextInput)
        {
            var (x, y) = WalkAxes();
            var orbit = (map.Down(Act.CamOrbitRight) ? 1f : 0f) - (map.Down(Act.CamOrbitLeft) ? 1f : 0f);
            var tilt = (map.Down(Act.CamTiltDown) ? 1f : 0f) - (map.Down(Act.CamTiltUp) ? 1f : 0f);
            var zoom = (map.Down(Act.ZoomIn) ? 1f : 0f) - (map.Down(Act.ZoomOut) ? 1f : 0f);
            if (map.Pad is { } pad && pad.RightStick != float2.Zero)
            {
                orbit += pad.RightStick.X;
                tilt -= pad.RightStick.Y; // stick up looks up, toward the horizon
            }
            if (input.IsMouseDown(MouseButton.Right) && !io.WantCaptureMouse)
            {
                // Drag to swing it round and tilt it: a hand on the camera.
                orbit += input.MouseDelta.X * 0.12f;
                tilt += input.MouseDelta.Y * 0.12f;
            }
            var speed = input.IsKeyDown(GlfwKey.LeftShift) || input.IsKeyDown(GlfwKey.RightShift) ? 3f
                : input.IsKeyDown(GlfwKey.LeftControl) || input.IsKeyDown(GlfwKey.RightControl) ? 0.3f : 1f;
            intent = new(new float2(x, y), orbit, tilt, zoom, speed);
            if (!io.WantCaptureMouse && input.WorldScrollDelta != 0)
                _camera.ZoomAt(input.MousePosition, MathF.Pow(1.15f, input.WorldScrollDelta));

            if (map.Pressed(Act.CamHideUi))
                _uiHidden = !_uiHidden;
            if (map.Pressed(Act.CamTurntable))
                _cine.Turntable = _cine.Turntable == 0f ? TurntableSpeed : 0f;
            if (map.Pressed(Act.CamFollow))
                ToggleFilmFollow(Controls.UsingPad ? Entity.Null : PickCreature(input.MousePosition));
            if (input.WasMousePressed(MouseButton.Left) && !io.WantCaptureMouse && PickCreature(input.MousePosition) is { IsNull: false } clicked)
                ToggleFilmFollow(clicked);
            if (map.Pressed(Act.CamPause) || map.Pressed(Act.CamSlow))
            {
                if (_coop != null)
                    Notify(MessageKind.Info, "The clock can't stop or slow in co-op: the town doesn't wait.");
                else if (map.Pressed(Act.CamPause))
                    _filmPaused = !_filmPaused;
                else
                    _filmSlow = (_filmSlow + 1) % SlowSteps.Length;
            }
        }
        _cine.Follow = FilmFollowPoint();
        _cine.Update(intent, dt);
    }

    /// <summary>Follow this creature, or (pointing at nothing) yourself, or stop following.</summary>
    private void ToggleFilmFollow(Entity pointed)
    {
        if (!pointed.IsNull && pointed != _filmFollow)
        {
            _filmFollow = pointed;
            _followMe = false;
        }
        else if (!_filmFollow.IsNull || _followMe)
        {
            _filmFollow = Entity.Null;
            _followMe = false;
        }
        else
            _followMe = HasAvatar || Ghosting;
    }

    /// <summary>Where camera mode's follow is now, null if nothing (or it's gone from what you can see).</summary>
    private float3? FilmFollowPoint()
    {
        var w = _sim.World;
        if (_followMe)
            return HasAvatar ? AvatarDrawPos() : Ghosting ? _ghostPos : null;
        if (_filmFollow.IsNull)
            return null;
        // In a round you follow only someone your character can see: lose them and it stops.
        if (!w.IsAlive(_filmFollow) || !w.Has<Position>(_filmFollow) || (CameraHonest && !View.CanSee(_filmFollow)))
        {
            _filmFollow = Entity.Null;
            return null;
        }
        return _renderer.DrawPos(_sim, _filmFollow, w.Get<Position>(_filmFollow).Pos);
    }

    /// <summary>
    /// After camera mode: ease back onto your character (after <see cref="FollowAvatar"/> has said
    /// where that is) and back to the view's own tilt.
    /// </summary>
    private void EaseCameraHome(float dt)
    {
        if (_cameraMode)
            return;
        if (_cameraReturn > 0f)
        {
            _cameraReturn = Math.Max(0f, _cameraReturn - dt);
            var t = 1f - _cameraReturn / ReturnSeconds;
            t = t * t * (3f - 2f * t);
            _camera.Target = _returnFrom + (_camera.Target - _returnFrom) * t;
        }
        if (_pitchHome is { } home)
        {
            _camera.Pitch += (home - _camera.Pitch) * (1f - MathF.Exp(-dt * 8f));
            if (MathF.Abs(home - _camera.Pitch) < 1e-3f)
            {
                _camera.Pitch = home;
                _pitchHome = null;
            }
        }
    }

    /// <summary>Camera mode's keys, along the bottom; gone with the rest of the HUD on H.</summary>
    private void DrawCameraModeHint()
    {
        if (!_cameraMode)
            return;
        _hud.Next(Anchor.Bottom);
        Panel.Begin("##camera-mode", Hud.Flags | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs);
        _hud.Took();
        using (Gui.DisplayFont(18f))
            TextC(Theme.Parchment, "Camera mode");
        var state = new List<string>();
        if (_followMe || !_filmFollow.IsNull)
            state.Add("following");
        if (_cine is { Turntable: not 0f })
            state.Add("turning");
        if (FilmPaused)
            state.Add("paused");
        else if (FilmTimeScale < 1f)
            state.Add($"slow motion ×{FilmTimeScale:0.##}");
        if (state.Count > 0)
        {
            ImGui.SameLine(0, Gui.S(10));
            TextC(Theme.Good, string.Join(", ", state));
        }
        var orbit = Controls.UsingPad ? "RS" : $"{Controls.Glyph(Act.CamOrbitLeft)}/{Controls.Glyph(Act.CamOrbitRight)} or right-drag";
        var tilt = Controls.UsingPad ? "RS" : $"{Controls.Glyph(Act.CamTiltUp)}/{Controls.Glyph(Act.CamTiltDown)}";
        TextC(Theme.Text, $"{Controls.Walk()} fly · {orbit} orbit · {tilt} tilt · {Controls.Pair(Act.ZoomIn, Act.ZoomOut)} or scroll zoom"
            + (Controls.UsingPad ? "" : " · Shift faster, Ctrl slower"));
        var follow = Controls.UsingPad ? $"{Press(Act.CamFollow)} follow yourself" : $"{Press(Act.CamFollow)} or click someone to follow";
        TextC(Theme.Text, $"{follow} · {Press(Act.CamTurntable)} turn slowly · {Press(Act.CamHideUi)} hide all this · F12 screenshot");
        TextC(_coop == null ? Theme.Text : Theme.Dim, _coop == null
            ? $"{Press(Act.CamPause)} pause · {Press(Act.CamSlow)} slow motion"
            : "The clock keeps running in co-op.");
        if (CameraHonest)
            TextC(Theme.Dim, "You see only what your character knows.");
        TextC(Theme.Dim, $"{Press(Act.CameraMode)} or {Press(Act.Back)}: back to your character");
        Panel.End();
    }
}

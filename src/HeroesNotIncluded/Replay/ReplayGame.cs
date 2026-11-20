using Brutal.ImGuiApi;
using Narsil;
using Narsil.Platform;
using Narsil.Rendering;
using Narsil.Ui;
using HeroesNotIncluded.Net;
using HeroesNotIncluded.Rendering;
using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Ecs;
using HeroesNotIncluded.Sim.Replay;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded.Replay;

/// <summary>
/// Watching a round again, after it's over: the recording (<see cref="ReplayFile"/>) played in a
/// sim of its own (<see cref="ReplayPlayer"/>), seen from anywhere with the free camera
/// (<see cref="CinematicCamera"/>), all of it, since the reveal has told everything. A timeline
/// plays, pauses, speeds and slows it and jumps to its crises; going back plays it again from the
/// start on another thread (a season takes seconds), with the world held off screen meanwhile.
/// </summary>
public sealed class ReplayGame(ReplayFile file, string? path = null) : Game
{
    private DefDatabase _defs = null!;
    private ReplayPlayer? _prepared;
    private ReplayPlayer _player => _prepared!;
    private Simulation _drawn = null!; // the sim the renderers were made for
    private WorldRenderer _renderer = null!;
    private QuadRenderer? _quads;
    private readonly Interpolation _interp = new();
    private readonly Camera3D _camera = new() { Zoom = 40f, VerticalScale = WorldScale.LevelHeight };
    private CinematicCamera _cine = null!;
    private Audio.SoundDirector? _sound;
    private float _time, _accumulator;

    private static readonly float[] Speeds = [0.25f, 0.5f, 1f, 2f, 4f, 8f];
    private int _speed = 2;
    private bool _paused;
    private bool _uiHidden;
    private Entity _follow = Entity.Null;

    private Task? _seeking;
    private CancellationTokenSource? _seekCancel;
    private long _seekTo;
    private float? _scrub; // the timeline being dragged: where to, in ticks from the start

    /// <summary>Where the camera starts: the first player's character, or the middle of the town.</summary>
    private void Frame()
    {
        var sim = _player.Sim;
        var start = new float3(sim.Map.Width / 2f, sim.Map.Height / 2f, sim.Map.SurfaceZ(sim.Map.Width / 2, sim.Map.Height / 2));
        foreach (var e in sim.World.Query<PlayerCharacter>())
            if (sim.World.TryGet<Position>(e, out var p))
            {
                start = p.Pos;
                break;
            }
        _camera.Target = start;
        _camera.Yaw = 45f * MathF.PI / 180f;
    }

    /// <summary>
    /// The CPU side, off the main thread behind the loading screen: the defs, the build checked,
    /// the sim loaded. A <see cref="Sim.Saving.SaveException"/> goes back to the title with its words.
    /// </summary>
    public void Prepare(string dataDirectory)
    {
        _defs = Mods.GameContent.Current.LoadDefs(dataDirectory);
        if (file.BuildTag != CoopSession.BuildTag(_defs))
            throw new Sim.Saving.SaveException("That replay was made on another version of the game, and plays only there.");
        try
        {
            _prepared = new ReplayPlayer(file, _defs);
        }
        catch (Exception e) when (e is InvalidDataException or ArgumentException)
        {
            throw new Sim.Saving.SaveException($"That replay won't play: {e.Message}");
        }
    }

    public override void Load()
    {
        Theme.Apply(Settings.Current.UiScale);
        if (_prepared == null)
            Prepare(Host.DataDirectory); // not through the loading screen
        _cine = new CinematicCamera(_camera)
        {
            Bounds = (float2.Zero, new float2(_player.Sim.Map.Width, _player.Sim.Map.Height)),
        };
        _camera.MinZoom = 8f;
        MakeRenderers();
        Frame();
    }

    private void MakeRenderers()
    {
        _drawn = _player.Sim;
        var old = _quads;
        _renderer = new WorldRenderer(Host.Atlas) { CutawayZ = _drawn.Map.Depth - 1, Interp = _interp };
        _quads = new QuadRenderer(Host, new int3(_drawn.Map.Width, _drawn.Map.Height, _drawn.Map.Depth), ShaderReload.QuadShaders(Host)) { HideRoofs = false, SmoothAll = true };
        old?.Dispose();
        _interp.Reset();
    }

    private bool Seeking => _seeking is { IsCompleted: false };

    /// <summary>Go to a tick, on another thread; the world's held off screen till it's there.</summary>
    private void SeekTo(long tick)
    {
        if (Seeking)
            return;
        _seekTo = Math.Clamp(tick, file.StartTick, file.EndTick);
        _seekCancel = new CancellationTokenSource();
        var token = _seekCancel.Token;
        var target = _seekTo;
        _seeking = Task.Run(() => _player.Seek(target, token), token);
    }

    public override void Update(float dt)
    {
        Host.PadCursor.Enabled = false; // the stick's the camera's here (or there's nothing to point at)
        _time += dt;
        Platforms.GamePlatform.Current.Update(); // a join request waits for the title screen
        Platforms.GamePlatform.Current.SetPresence(new Platforms.Presence("#Replay"));
        var window = Host.Window.Size;
        _camera.ViewportSize = new float2(Math.Max(1, window.X), Math.Max(1, window.Y));
        Controls.Map.Update();
        Audio.GameAudio.Update(Host, dt);
        if (Seeking)
            return;
        if (_seeking != null)
        {
            if (_seeking.Exception is { } failed)
                Log.Engine.Warning($"replay: seeking failed: {failed.InnerException?.Message}");
            _seeking = null;
            if (!ReferenceEquals(_drawn, _player.Sim))
                MakeRenderers();
            _interp.Capture(_player.Sim);
            _follow = _player.Sim.World.IsAlive(_follow) ? _follow : Entity.Null;
        }

        HandleKeys();
        FlyCamera(dt);

        if (!_paused && !_player.Ended)
        {
            _accumulator += dt * SimClock.TicksPerSecond * Speeds[_speed];
            var ticks = Math.Min((int)_accumulator, 8 * SimClock.TicksPerSecond / 4);
            _accumulator -= ticks;
            if (_accumulator > 60)
                _accumulator = 0;
            for (var i = 0; i < ticks && !_player.Ended; i++)
            {
                _interp.Capture(_player.Sim);
                _player.Step();
            }
            _interp.Alpha = Math.Clamp(_accumulator, 0f, 1f);
            if (_player.Ended)
                _paused = true;
        }
        else
            _interp.Alpha = 1f;
        UpdateSound(dt);
    }

    private void HandleKeys()
    {
        var input = Host.Input;
        var map = Controls.Map;
        if (ImGui.GetIO().WantTextInput)
            return;
        if (map.Pressed(Act.CamPause))
            TogglePause();
        if (map.Pressed(Act.CamHideUi))
            _uiHidden = !_uiHidden;
        if (map.Pressed(Act.CamTurntable))
            _cine.Turntable = _cine.Turntable == 0f ? 0.15f : 0f;
        if (input.WasKeyPressed(GlfwKey.Comma))
            _speed = Math.Max(0, _speed - 1);
        if (input.WasKeyPressed(GlfwKey.Period))
            _speed = Math.Min(Speeds.Length - 1, _speed + 1);
        if (map.Pressed(Act.CamFollow))
            ToggleFollow(PickCreature(input.MousePosition));
        if (input.WasMousePressed(MouseButton.Left) && !ImGui.GetIO().WantCaptureMouse && PickCreature(input.MousePosition) is { IsNull: false } clicked)
            ToggleFollow(clicked);
        if (map.Pressed(Act.Back))
        {
            if (_uiHidden)
                _uiHidden = false;
            else
                Leave();
        }
    }

    private void TogglePause()
    {
        if (_player.Ended)
        {
            SeekTo(file.StartTick); // from the top
            _paused = false;
            return;
        }
        _paused = !_paused;
    }

    private void ToggleFollow(Entity pointed)
    {
        _follow = !pointed.IsNull && pointed != _follow ? pointed : Entity.Null;
    }

    private void Leave() => Host.SwitchTo(new TitleGame());

    private void FlyCamera(float dt)
    {
        var input = Host.Input;
        var map = Controls.Map;
        var io = ImGui.GetIO();
        float x = 0, y = 0;
        if (map.Down(Act.WalkUp)) y += 1;
        if (map.Down(Act.WalkDown)) y -= 1;
        if (map.Down(Act.WalkRight)) x += 1;
        if (map.Down(Act.WalkLeft)) x -= 1;
        float orbit = (map.Down(Act.CamOrbitRight) ? 1f : 0f) - (map.Down(Act.CamOrbitLeft) ? 1f : 0f);
        float tilt = (map.Down(Act.CamTiltDown) ? 1f : 0f) - (map.Down(Act.CamTiltUp) ? 1f : 0f);
        float zoom = (map.Down(Act.ZoomIn) ? 1f : 0f) - (map.Down(Act.ZoomOut) ? 1f : 0f);
        if (map.Pad is { } pad)
        {
            if (pad.LeftStick != float2.Zero)
                (x, y) = (pad.LeftStick.X, pad.LeftStick.Y);
            orbit += pad.RightStick.X;
            tilt -= pad.RightStick.Y;
        }
        if (input.IsMouseDown(MouseButton.Right) && !io.WantCaptureMouse)
        {
            orbit += input.MouseDelta.X * 0.12f;
            tilt += input.MouseDelta.Y * 0.12f;
        }
        if (!io.WantCaptureMouse && input.WorldScrollDelta != 0)
            _camera.ZoomAt(input.MousePosition, MathF.Pow(1.15f, input.WorldScrollDelta));
        var speed = input.IsKeyDown(GlfwKey.LeftShift) || input.IsKeyDown(GlfwKey.RightShift) ? 3f
            : input.IsKeyDown(GlfwKey.LeftControl) || input.IsKeyDown(GlfwKey.RightControl) ? 0.3f : 1f;
        var w = _player.Sim.World;
        _cine.Follow = !_follow.IsNull && w.IsAlive(_follow) && w.Has<Position>(_follow)
            ? _renderer.DrawPos(_player.Sim, _follow, w.Get<Position>(_follow).Pos) : null;
        _cine.Update(new CinematicCamera.Intent(new float2(x, y), orbit, tilt, zoom, speed), dt);
    }

    /// <summary>The creature nearest the cursor on screen, if any is close: everyone's in view in a replay.</summary>
    private Entity PickCreature(float2 mouse)
    {
        var sim = _player.Sim;
        var best = Entity.Null;
        var bestDist = 0.6f * _camera.Zoom;
        foreach (var e in sim.World.Query<Creature>())
        {
            var pos = sim.World.Get<Position>(e);
            var size = sim.World.Get<Creature>(e).Stage(sim.Clock.Tick).Size;
            var s = _camera.WorldToScreen(pos.Pos + WorldScale.Rise(size * 0.5f));
            var d = MathF.Sqrt((s.X - mouse.X) * (s.X - mouse.X) + (s.Y - mouse.Y) * (s.Y - mouse.Y));
            if (d < bestDist)
            {
                best = e;
                bestDist = d;
            }
        }
        return best;
    }

    private void UpdateSound(float dt)
    {
        var audio = Audio.GameAudio.Engine;
        _sound ??= new Audio.SoundDirector(audio);
        var sim = _player.Sim;
        var center = _camera.ViewportSize * 0.5f;
        var right = _camera.ScreenToPlane(center + new float2(64, 0), _camera.Target.Z) - _camera.ScreenToPlane(center, _camera.Target.Z);
        var height = Math.Clamp(240f / Math.Max(1f, _camera.Zoom), 1.5f, 14f);
        _sound.Update(sim, Perception.For(sim, Entity.Null), Entity.Null, _camera.Target, right, height, _paused ? 0f : dt);
    }

    public override void PrepareFrame(in FrameContext frame)
    {
        if (!Seeking && _quads != null)
            _renderer.UpdateLight(_quads, frame, _player.Sim);
    }

    public override bool DrawsScene => true;

    public override void Draw(in FrameContext frame) { }

    public override void DrawScene(in FrameContext frame)
    {
        if (Seeking || _quads == null)
            return; // the sim's being played on elsewhere: nothing of it is safe to read
        var sim = _player.Sim;
        VisualStyle.Apply(Host, _quads);
        _quads.Begin(_camera, sim.Map.Depth - 1, sim.Lighting.SkyLight(sim), true);
        _renderer.Draw(_quads, _camera, sim, Entity.Null, _time, _paused || Seeking ? 0f : Host.FrameTimeMs / 1000f * Math.Min(Speeds[_speed], 3f));
        _quads.Render(frame);
    }

    public override void DrawUi()
    {
        var screen = ImGui.GetIO().DisplaySize;
        if (Seeking)
        {
            DrawWinding(screen);
            return;
        }
        if (_uiHidden)
            return;
        _renderer.DrawLabels(_camera, _player.Sim, _time);
        DrawTitle();
        DrawTimeline(screen);
    }

    private void DrawWinding(float2 screen)
    {
        var bg = ImGui.GetBackgroundDrawList(ImGui.GetMainViewport());
        var zero = float2.Zero;
        bg.AddRectFilled(in zero, in screen, Theme.U8(Theme.Cellar), 0f, ImDrawFlags.None);
        var pos = screen * 0.5f;
        float2? pivot = new float2(0.5f, 0.5f);
        ImGui.SetNextWindowPos(in pos, ImGuiCond.Always, in pivot);
        Panel.Begin("##replay-winding", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoMove);
        using (Gui.DisplayFont(20f))
            ImGuiText.Colored(Theme.Parchment, "Winding the season on...");
        var from = file.StartTick;
        var done = (float)Math.Clamp((double)(_player.Tick - from) / Math.Max(1, _seekTo - from), 0, 1);
        float2? size = new float2(Gui.S(320), Gui.S(10));
        ImGui.ProgressBar(done, in size, ""u8);
        if (ImGui.Button("Stop here"u8))
            _seekCancel?.Cancel();
        Panel.End();
    }

    private void DrawTitle()
    {
        var pos = new float2(Gui.S(12), Gui.S(12));
        ImGui.SetNextWindowPos(in pos, ImGuiCond.Always);
        Panel.Begin("##replay-title", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoInputs);
        using (Gui.DisplayFont(20f))
            ImGuiText.Colored(Theme.Parchment, $"The season again: {file.Meta.GetValueOrDefault("map", "the town")}");
        if (file.Meta.GetValueOrDefault("headline") is { Length: > 0 } headline)
            ImGuiText.Colored(Theme.Text, headline);
        if (file.Meta.GetValueOrDefault("players") is { Length: > 0 } players)
            ImGuiText.Colored(Theme.Dim, players);
        ImGuiText.Colored(Theme.Text, _player.Sim.Clock.TimeString());
        if (_player.DriftedAt is { } drifted)
            ImGuiText.Colored(Theme.Warn, $"From {When(drifted)} this replay went its own way: what follows may not be what happened.");
        Panel.End();
    }

    /// <summary>A tick as the clock tells it, "Day 2, 14:30".</summary>
    private string When(long tick)
    {
        var total = tick + _player.Sim.Clock.StartHour * (long)SimClock.TicksPerHour;
        var day = total / SimClock.TicksPerDay + 1;
        var hour = total % SimClock.TicksPerDay / (float)SimClock.TicksPerHour;
        return $"Day {day}, {(int)hour:00}:{(int)((hour - (int)hour) * 60):00}";
    }

    private void DrawTimeline(float2 screen)
    {
        var width = Math.Min(screen.X - Gui.S(24), Gui.S(900));
        var pos = new float2(screen.X * 0.5f, screen.Y - Gui.S(12));
        float2? pivot = new float2(0.5f, 1f);
        ImGui.SetNextWindowPos(in pos, ImGuiCond.Always, in pivot);
        var size = new float2(width, 0);
        ImGui.SetNextWindowSize(in size, ImGuiCond.Always);
        Panel.Begin("##replay-timeline", ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.AlwaysAutoResize);

        if (ImGui.Button(_player.Ended ? "From the top"u8 : _paused ? "Play"u8 : "Pause"u8))
            TogglePause();
        ImGui.SameLine(0, Gui.S(10));
        for (var i = 0; i < Speeds.Length; i++)
        {
            if (i > 0)
                ImGui.SameLine(0, Gui.S(4));
            var label = Speeds[i] < 1f ? $"{Speeds[i]:0.##}x" : $"{Speeds[i]:0}x";
            if (i == _speed)
                ImGui.PushStyleColor(ImGuiCol.Button, Theme.Accent with { W = 0.55f });
            if (ImGui.Button(label))
                _speed = i;
            if (i == _speed)
                ImGui.PopStyleColor();
        }
        ImGui.SameLine(0, Gui.S(16));
        ImGuiText.Colored(Theme.Dim, $"{When(_player.Tick)} of {When(file.EndTick)}");

        // The timeline: drag to scrub (it goes there when let go), the crises marked along it.
        var span = Math.Max(1, file.EndTick - file.StartTick);
        var at = _scrub ?? (_player.Tick - file.StartTick);
        float shown = at;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.SliderFloat("##scrub"u8, ref shown, 0f, span, ""u8))
            _scrub = shown;
        if (ImGui.IsItemDeactivatedAfterEdit() && _scrub is { } to)
        {
            SeekTo(file.StartTick + (long)to);
            _scrub = null;
        }
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var dl = ImGui.GetWindowDrawList();
        foreach (var m in file.Markers)
        {
            var x = min.X + (max.X - min.X) * (m.Tick - file.StartTick) / span;
            var a = new float2(x, min.Y);
            var b = new float2(x, max.Y);
            dl.AddLine(in a, in b, Theme.U8(Theme.Warn), Gui.S(2));
        }

        if (file.Markers.Length > 0)
        {
            ImGuiText.Colored(Theme.Dim, "Jump to:");
            foreach (var m in file.Markers)
            {
                ImGui.SameLine(0, Gui.S(8));
                if (ImGui.SmallButton($"{m.Label} ({When(m.Tick)})"))
                    SeekTo(Math.Max(file.StartTick, m.Tick - SimClock.TicksPerSecond * 5)); // a few seconds before
            }
        }
        var follow = _follow.IsNull ? "click someone to follow them" : "following (click them again to stop)";
        ImGuiText.Colored(Theme.Dim, $"{Controls.Walk()} fly · Q/E or right-drag orbit · R/F tilt · scroll zoom · , and . speed · {follow} · {Controls.Glyph(Act.CamHideUi)} hide all this");
        if (ImGui.Button("Back to the title screen"u8))
            Leave();
        if (path != null)
        {
            ImGui.SameLine(0, Gui.S(8));
            ImGuiText.Colored(Theme.Dim, $"Kept as {Path.GetFileName(path)}");
        }
        Panel.End();
    }

    public override void Dispose()
    {
        _seekCancel?.Cancel();
        try { _seeking?.Wait(TimeSpan.FromSeconds(5)); } catch (AggregateException) { }
        _quads?.Dispose();
    }
}

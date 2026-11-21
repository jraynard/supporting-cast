using Brutal.ImGuiApi;
using HeroesNotIncluded.Rendering;
using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Ecs;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Work;
using Narsil;
using Narsil.Rendering;
using Narsil.Platform;

namespace HeroesNotIncluded.Debug;

/// <summary>
/// The 3D forge prototype (<c>--forge-3d</c>; docs/VISION.md, "A world in 3D, work done in it"):
/// one smithy, cel-shaded and inked (Narsil's <see cref="ToonStyle"/>), seen in first person, where a
/// sword is forged through the real sessions (<see cref="ForgeYard"/>) by hand with the mouse and no
/// mini-game panel. The tongs follow the cursor: lay the bar in the coals and it heats; drag the
/// bellows' handle to blow; drag the bar out to draw it; lay it on the anvil and the mouse is the
/// hammer; dip it in the tub and hold it under. Each gesture is only a way into the session the
/// 2D game already plays (a press, a hold, the other action, where the mouse points): the sim is
/// unchanged. Models come from tools/forge_models.py (the coordinates below match it).
/// Exploratory: no village, no co-op, nothing saved.
/// </summary>
public sealed class Forge3DGame : Game
{
    public required DefDatabase Defs { get; init; }

    /// <summary>Start partway, for screenshots: "anvil" (the piece hot, at the anvil), "tub" (shaped, at the tub), "forge" (in the fire).</summary>
    public string? StartAt { get; init; }

    // Where tools/forge_models.py put things (metres, Y up, the room's open side +Z).
    private static readonly float3 Hearth = new(-2.0f, 0.92f, -1.9f);
    private static readonly float3 AnvilFace = new(0.2f, 0.86f, 0.2f);
    private static readonly float3 Tub = new(2.0f, 0.55f, -0.9f);
    private static readonly float3 BellowsNozzle = new(-2.85f, 1.05f, -1.85f);
    /// <summary>The bellows' top board, where the hand works them (their handle is against the wall, out of view).</summary>
    private static readonly float3 BellowsHandle = BellowsNozzle + new float3(-0.42f, 0.1f, 0f);
    private const float BarLength = 0.32f, BladeLength = 0.78f, TubRadius = 0.4f;

    private ForgeYard.Yard _yard = null!;
    private readonly WorkPredictor _work = new();
    private MeshRenderer _meshes = null!;
    private readonly Dictionary<string, MeshModel> _models = [];
    private readonly PerspectiveCamera _camera = new() { FieldOfView = MathF.PI / 3.3f, Near = 0.03f, Far = 60f };
    private float3 _eye, _look;
    private float _ticks, _time;
    private bool _debug;
    private string? _shaderError;

    // ---- the hands ----
    private Place _at = Place.Forge;
    private float2 _mouse;
    private float3 _hand;            // where the cursor meets the station's work plane: the piece's middle when carried
    private float3 _inFire;          // where the piece lies in the coals while it heats
    private bool _grabBellows, _grabPiece;
    private float _pumpMotion;       // recent up-and-down of the bellows' handle, decaying
    private float _handleLift;       // the handle's drawn height off rest, from the drag
    private float _bellowsPhase;     // how far through a stroke the boards are (drawn)
    private bool _hammerArmed;       // the click that laid the piece down has come up
    private long _hammerFor = -1;
    private bool _held;              // last frame's hold, for the tub

    // ---- what the world shows of the work ----
    private int _blowsSeen;
    private long _anvilSession = -1;
    private float _struckAt = -10f;
    private readonly List<(float3 Pos, float3 Vel, float Born, float Life)> _sparks = [];
    private readonly List<(float3 Pos, float Born, float Life, float Size)> _puffs = [];
    private readonly Random _fx = new(7); // the view's own: sparks and steam never reach the sim
    private Entity _lastMade = Entity.Null;
    private long _seenMessages;
    private (string Text, float At)? _said;

    private static readonly string[] ModelNames =
    [
        "room", "hearth", "coals", "bellows", "anvil", "tub", "water", "hammer", "tongs", "bar", "blade",
        "smith_torso", "smith_head", "smith_upper_arm", "smith_forearm", "smith_leg", "spark", "puff", "mark",
    ];

    private enum Place { Forge, Anvil, Tub }

    public override bool DrawsScene => true;

    public override void Load()
    {
        _yard = ForgeYard.Setup(Defs);
        _meshes = new MeshRenderer(Host, scenePass: true, toon: ToonStyle.Default);
        var dir = Path.Combine(AppContext.BaseDirectory, "data", "models", "forge");
        foreach (var name in ModelNames)
            _models[name] = _meshes.Load(Path.Combine(dir, name + ".glb"));
        Host.Renderer.ClearColor = new float4(0.078f, 0.055f, 0.039f, 1f); // the brand's cellar
        Host.Post.Settings = new PostSettings(Bloom: 0.8f, BloomThreshold: 0.85f, Vignette: 0.35f, Warmth: 0.08f);
        _seenMessages = Sim.Messages.Total;
        if (StartAt != null && Smithing.PieceOf(Sim, Me) is { } piece)
        {
            // A dev shortcut, straight into the sim's state: the piece as it would be by then.
            if (StartAt is "anvil" or "tub")
                piece.Temp = Smithing.Window(piece.Recipe).Hi;
            if (StartAt == "tub")
                piece.Shape = 1f;
            _at = StartAt switch { "anvil" => Place.Anvil, "tub" => Place.Tub, _ => Place.Forge };
            _inFire = Hearth + new float3(0.1f, 0.04f, 0.1f);
            Sim.ApplyNow(new SmithCommand(Me, StationEntity(_at)));
            _work.Update(Sim, Me, 1, false, false, 0, c => Sim.ApplyNow(c));
        }
        (_eye, _look) = View(_at);
        _hand = Rest(_at);
    }

    public override void Dispose() => _meshes?.Dispose();

    public override void Draw(in FrameContext frame) { } // all of it is the scene (DrawScene), and ImGui on top

    private Simulation Sim => _yard.Sim;
    private Entity Me => _yard.Smith;
    private WorkSession? Session => _work.Session;
    private WorkPiece? Piece => Smithing.PieceOf(Sim, Me);

    private Entity StationEntity(Place p) => p switch { Place.Forge => _yard.Forge, Place.Anvil => _yard.Anvil, _ => _yard.Tub };

    // ---------------- the loop ----------------

    public override void Update(float dt)
    {
        _time += dt;
        var input = Host.Input;
        if (input.WasKeyPressed(Brutal.GlfwApi.GlfwKey.F1))
            _debug = !_debug;
        if (input.WasKeyPressed(Brutal.GlfwApi.GlfwKey.F7))
            _shaderError = _meshes.ReloadShaders(DevPaths.EngineShaders ?? Path.Combine(Host.DataDirectory, "shaders"));

        _ticks += dt * SimClock.TicksPerSecond;
        var steps = (int)_ticks;
        _ticks -= steps;
        for (var i = 0; i < steps; i++)
        {
            ForgeYard.Restock(_yard);
            Sim.Tick();
        }

        var io = ImGui.GetIO();
        _camera.ViewportSize = io.DisplaySize; // window units, the same as the mouse
        _mouse = input.MousePosition;
        UpdateCamera(dt);
        var down = input.IsMouseDown(MouseButton.Left);
        var pressed = input.WasMousePressed(MouseButton.Left);
        var released = input.WasMouseReleased(MouseButton.Left);
        _hand = OnWorkPlane(_mouse) ?? _hand;

        var map = Controls.Map;
        var w = Sim.World;
        if (!w.Has<WorkSession>(Me))
        {
            _work.Reset();
            (_grabBellows, _grabPiece) = (false, false);
            Carry(pressed, down);
            // Between steps, step along the room to the next station.
            if (map.Pressed(Act.WalkLeft) && _at > Place.Forge)
                _at--;
            if (map.Pressed(Act.WalkRight) && _at < Place.Tub)
                _at++;
        }
        else
        {
            bool press = false, held = false, act = false;
            var point = -1;
            var axis = (map.Down(Act.WalkRight) ? 1 : 0) - (map.Down(Act.WalkLeft) ? 1 : 0);
            switch (Session)
            {
                case HeatSession:
                    (held, act) = AtTheFire(pressed, down, released, dt);
                    axis = 0;
                    break;
                case HammerSession h:
                    (press, held, point) = AtTheAnvil(h, pressed, down);
                    if (input.WasMousePressed(MouseButton.Right) || map.Pressed(Act.Back))
                        Sim.ApplyNow(new StopWorkCommand(Me)); // pick it back up: to the fire again
                    break;
                case QuenchSession:
                    held = down; // held under while the button is
                    press = down && !_held;
                    axis = 0;
                    break;
            }
            _held = down;
            if (map.Pressed(Act.Back) && Session is not HammerSession)
                Sim.ApplyNow(new StopWorkCommand(Me));
            _work.Update(Sim, Me, steps, press, held, axis, c => Sim.ApplyNow(c), point: point, act: act);
        }

        foreach (var made in w.Query<Crafted>())
            _lastMade = made;
        Listen();
        UpdateEffects(dt);
    }

    /// <summary>Nothing on the go: the piece is in the tongs and follows the hand; a click lays it where it is.</summary>
    private void Carry(bool pressed, bool down)
    {
        if (Piece == null)
        {
            if (pressed)
                ForgeYard.NewPiece(_yard);
            return;
        }
        if (!pressed)
            return;
        if (OverWork(_at, _hand))
        {
            if (_at == Place.Forge)
                _inFire = new float3(Math.Clamp(_hand.X, Hearth.X - 0.4f, Hearth.X + 0.4f), Hearth.Y + 0.04f, Math.Clamp(_hand.Z, Hearth.Z - 0.3f, Hearth.Z + 0.35f));
            Sim.ApplyNow(new SmithCommand(Me, StationEntity(_at)));
            _hammerArmed = false;
            _held = down;
        }
    }

    /// <summary>In the fire: the mouse works the bellows' handle up and down, or takes the tongs and drags the piece out.</summary>
    private (bool Held, bool Act) AtTheFire(bool pressed, bool down, bool released, float dt)
    {
        if (pressed)
        {
            if (Near(BellowsHandle, 90f))
                _grabBellows = true;
            else if (Near(_inFire + new float3(0, 0, 0.2f), 110f))
                _grabPiece = true;
        }
        if (!down)
            (_grabBellows, _grabPiece) = (false, false);
        var act = false;
        if (_grabBellows)
        {
            var dy = Host.Input.MouseDelta.Y;
            _handleLift = Math.Clamp(_handleLift - dy * 0.0025f, -0.12f, 0.12f);
            _pumpMotion += MathF.Abs(dy);
        }
        else
            _handleLift *= MathF.Pow(0.05f, dt);
        _pumpMotion *= MathF.Pow(0.02f, dt);
        if (_grabPiece)
        {
            // Dragged within the coals it moves there; out past their edge, it's drawn.
            if (InCoals(_hand))
                _inFire = new float3(_hand.X, Hearth.Y + 0.04f, _hand.Z);
            else
            {
                act = true;
                _grabPiece = false;
            }
        }
        _ = released;
        return (_grabBellows && _pumpMotion > 40f, act);
    }

    /// <summary>At the anvil the mouse is the hammer: held, it rises; let go, it falls; moved, it shifts along the piece.</summary>
    private (bool Press, bool Held, int Point) AtTheAnvil(HammerSession h, bool pressed, bool down)
    {
        if (h.StartTick != _hammerFor)
            (_hammerFor, _hammerArmed) = (h.StartTick, !down);
        if (!down)
            _hammerArmed = true;
        // Where along the piece's working length the cursor is, as the panel's 0..1 across.
        var length = PieceLength(h.Shape);
        var x0 = AnvilTang.X + length * 0.58f - length * 0.36f;
        var u = Math.Clamp((_hand.X - x0) / (length * 0.72f), 0f, 1f);
        var point = (int)MathF.Round(u * 255f) | 128 << 8;
        return (_hammerArmed && pressed, _hammerArmed && down, point);
    }

    /// <summary>What the sim told the smith (a cold piece refused at the anvil, a burnt one): shown a while under the hint.</summary>
    private void Listen()
    {
        var log = Sim.Messages;
        for (var i = log.IndexAfter(_seenMessages); i < log.Messages.Count; i++)
            if (log.Messages[i].To is not { } to || to == Me)
                _said = (log.Messages[i].Text, _time);
        _seenMessages = log.Total;
    }

    private void UpdateEffects(float dt)
    {
        if (Session is HammerSession h)
        {
            if (h.StartTick != _anvilSession)
                (_anvilSession, _blowsSeen) = (h.StartTick, h.Blows);
            if (h.Blows > _blowsSeen)
            {
                _blowsSeen = h.Blows;
                _struckAt = _time;
                var hot = ForgeGlow.Strength(h.Temp);
                var n = (int)(6 + 28 * hot * (h.LastOffCentre ? 0.4f : 1f));
                var at = OverPiece(h.Offset, h.Shape);
                for (var i = 0; i < n; i++)
                {
                    var a = (float)(_fx.NextDouble() * Math.PI * 2);
                    var up = 0.6f + (float)_fx.NextDouble() * 1.6f;
                    var out_ = 0.6f + (float)_fx.NextDouble() * 1.8f;
                    _sparks.Add((at, new float3(MathF.Cos(a) * out_, up, MathF.Sin(a) * out_), _time, 0.25f + (float)_fx.NextDouble() * 0.45f));
                }
            }
        }
        if (Session is QuenchSession q && q.Steam > 0.02f && _fx.NextDouble() < q.Steam * dt * 40)
        {
            var p = new float3(_hand.X, Tub.Y, _hand.Z) + new float3((float)_fx.NextDouble() * 0.2f - 0.1f, 0.02f, (float)_fx.NextDouble() * 0.2f - 0.1f);
            _puffs.Add((p, _time, 1.2f + (float)_fx.NextDouble(), 0.6f + (float)_fx.NextDouble() * 0.8f));
        }
        for (var i = _sparks.Count - 1; i >= 0; i--)
        {
            var s = _sparks[i];
            if (_time - s.Born > s.Life)
            {
                _sparks.RemoveAt(i);
                continue;
            }
            s.Vel -= new float3(0, 9.8f * dt, 0);
            s.Pos += s.Vel * dt;
            if (s.Pos.Y < 0.01f)
                (s.Pos.Y, s.Vel.Y) = (0.01f, -s.Vel.Y * 0.3f);
            _sparks[i] = s;
        }
        _puffs.RemoveAll(p => _time - p.Born > p.Life);
        if (Session is HeatSession { Pumping: true })
            _bellowsPhase += dt * 2.2f;
    }

    // ---------------- the view ----------------

    /// <summary>Standing at a station, eyes on the work.</summary>
    private static (float3 Eye, float3 Look) View(Place p) => p switch
    {
        Place.Forge => (new float3(-2.05f, 1.62f, -0.55f), new float3(-2.35f, 0.85f, -1.75f)),
        Place.Anvil => (new float3(0.15f, 1.6f, 0.95f), new float3(0.12f, 0.8f, 0.1f)),
        _ => (new float3(2.0f, 1.6f, 0.15f), new float3(2.0f, 0.5f, -0.85f)),
    };

    private void UpdateCamera(float dt)
    {
        var place = Session switch
        {
            HeatSession => Place.Forge,
            HammerSession => Place.Anvil,
            QuenchSession => Place.Tub,
            _ => _at,
        };
        _at = place;
        var (eye, look) = View(place);
        // The head turns a little toward the hand.
        var size = _camera.ViewportSize;
        var c = new float2(Math.Clamp(_mouse.X / size.X * 2f - 1f, -1f, 1f), Math.Clamp(_mouse.Y / size.Y * 2f - 1f, -1f, 1f));
        var forward = float3.NormalizeOrZero(look - eye);
        var right = float3.NormalizeOrZero(float3.Cross(forward, new float3(0, 1, 0)));
        look += right * c.X * 0.18f - new float3(0, 1, 0) * c.Y * 0.1f;
        var k = 1f - MathF.Exp(-dt * 4f);
        _eye = float3.Lerp(_eye, eye, k);
        _look = float3.Lerp(_look, look, k);
        _camera.Position = _eye;
        _camera.Target = _look;
    }

    /// <summary>Where the hand rests at a station before the mouse moves.</summary>
    private static float3 Rest(Place p) => p switch
    {
        Place.Forge => Hearth + new float3(0.2f, 0.2f, 0.9f),
        Place.Anvil => AnvilFace + new float3(0f, 0.12f, 0.15f),
        _ => Tub + new float3(0f, 0.4f, 0.5f),
    };

    /// <summary>The height things are carried at, at a station: just over the coals, the anvil's face, the tub's rim.</summary>
    private float WorkHeight() => _at switch
    {
        Place.Forge => Hearth.Y + 0.14f,
        Place.Anvil => AnvilFace.Y + 0.1f,
        _ => 0.95f,
    };

    /// <summary>The cursor's ray onto the station's carrying height, in reach.</summary>
    private float3? OnWorkPlane(float2 screen)
    {
        var (o, d) = _camera.ScreenRay(screen);
        var y = WorkHeight();
        if (MathF.Abs(d.Y) < 1e-4f)
            return null;
        var t = (y - o.Y) / d.Y;
        if (t <= 0f)
            return null;
        var p = o + d * t;
        // No further than an arm and the tongs from where the smith stands.
        var stand = View(_at).Eye;
        var flat = new float3(p.X - stand.X, 0, p.Z - stand.Z);
        var len = flat.Length();
        return len > 1.7f ? new float3(stand.X, y, stand.Z) + flat * (1.7f / len) : p;
    }

    private static bool InCoals(float3 p) => MathF.Abs(p.X - Hearth.X) < 0.5f && MathF.Abs(p.Z - Hearth.Z) < 0.42f;

    private static bool OverWork(Place place, float3 p) => place switch
    {
        Place.Forge => InCoals(p),
        Place.Anvil => MathF.Abs(p.X - AnvilFace.X) < 0.4f && MathF.Abs(p.Z - AnvilFace.Z) < 0.2f,
        _ => float3.Distance(new float3(p.X, 0, p.Z), new float3(Tub.X, 0, Tub.Z)) < TubRadius,
    };

    /// <summary>The cursor within so many pixels of a point in the world.</summary>
    private bool Near(float3 world, float pixels) => _camera.WorldToScreen(world, out var s) && float2.Distance(s, _mouse) < pixels;

    // ---------------- the scene ----------------

    public override void DrawScene(in FrameContext frame)
    {
        var fire = Session is HeatSession heat ? heat.Fire : 700f; // left alone, it smoulders
        var fireGlow = ForgeGlow.Strength(fire);
        // A cool key from the door, the forge's warmth as the sky ambient: the room is lit by its fire.
        var lighting = new SceneLighting(new float3(0.55f, 0.75f, 0.5f), new float3(0.75f, 0.72f, 0.7f),
            new float3(0.22f, 0.16f, 0.12f) + new float3(0.25f, 0.1f, 0.02f) * fireGlow)
        {
            Specular = 0.35f,
            Rim = 0.3f,
        };
        _meshes.Begin(_camera, lighting);

        var I = float4x4.Identity;
        foreach (var name in (string[])["room", "hearth", "anvil", "tub"])
            _meshes.Draw(_models[name], I);
        _meshes.Draw(_models["water"], I, tint: new float4(1, 1, 1, 0.85f), blend: MeshBlend.Alpha, outline: false);
        // Coals at rest still smoulder red; blown, they run to yellow and bloom.
        var coal = ForgeGlow.Colour(fire + 120f);
        _meshes.Draw(_models["coals"], I, emissive: new float3(coal.X, coal.Y, coal.Z) * (0.5f + 1.3f * fireGlow));

        // The bellows breathe as they're worked; the handle follows the hand on it.
        var stroke = 0.5f - 0.5f * MathF.Cos(_bellowsPhase * MathF.PI * 2f);
        var squash = 1f - 0.4f * (_grabBellows ? Math.Clamp(0.5f - _handleLift * 4f, 0f, 1f) : stroke * 0.3f);
        _meshes.Draw(_models["bellows"], float4x4.CreateScale(new float3(1f, squash, 1f)) * float4x4.CreateTranslation(BellowsNozzle));

        DrawHands();

        foreach (var s in _sparks)
        {
            var life = 1f - (_time - s.Born) / s.Life;
            var dir = float3.NormalizeOrZero(s.Vel);
            var m = Orient(new float3(1, 0, 0), dir) * float4x4.CreateScale(0.6f + life) * float4x4.CreateTranslation(s.Pos);
            _meshes.Draw(_models["spark"], m, tint: new float4(1, 1, 1, life), emissive: new float3(2.4f, 1.5f, 0.5f) * life,
                unlit: true, blend: MeshBlend.Additive, outline: false);
        }
        foreach (var p in _puffs)
        {
            var age = (_time - p.Born) / p.Life;
            var m = float4x4.CreateScale(p.Size * (0.6f + age * 1.8f)) * float4x4.CreateTranslation(p.Pos + new float3(0, age * 0.9f, 0));
            _meshes.Draw(_models["puff"], m, tint: new float4(1, 1, 1, 0.45f * (1f - age)), softEdges: true, blend: MeshBlend.Alpha, outline: false);
        }
        _meshes.Render(frame);
    }

    /// <summary>The first-person hands: the left on the tongs and the piece in them, the right on the hammer or the bellows.</summary>
    private void DrawHands()
    {
        var forward = float3.NormalizeOrZero(new float3(_look.X - _eye.X, 0, _look.Z - _eye.Z));
        var right = float3.Cross(forward, new float3(0, 1, 0));
        var leftShoulder = _eye - right * 0.2f + new float3(0, -0.28f, 0) - forward * 0.05f;
        var rightShoulder = _eye + right * 0.2f + new float3(0, -0.28f, 0) - forward * 0.05f;

        var piece = Piece;
        var temp = Session switch
        {
            HeatSession s => s.Temp,
            HammerSession s => s.Temp,
            QuenchSession s => s.Temp,
            _ => piece?.Temp ?? 20f,
        };
        var shape = Session is HammerSession hs ? hs.Shape : piece?.Shape ?? 1f;

        if (piece != null)
        {
            // The piece's tang and which way it points: in the fire where it was laid, across the anvil,
            // down into the tub while it's held under, else out before the hand.
            var length = PieceLength(shape);
            float3 tang, along;
            switch (Session)
            {
                case HeatSession:
                    along = float3.NormalizeOrZero(new float3(Hearth.X - _eye.X, 0, Hearth.Z - _eye.Z));
                    tang = (_grabPiece ? new float3(_hand.X, Hearth.Y + 0.04f, _hand.Z) : _inFire) - along * length * 0.5f;
                    break;
                case HammerSession:
                    along = new float3(1, 0, 0);
                    tang = AnvilTang;
                    break;
                case QuenchSession q:
                {
                    var under = q.Dunked || _held;
                    var dip = under ? 0.4f : 0f;
                    along = float3.NormalizeOrZero(new float3(forward.X * 0.25f, -1f, forward.Z * 0.25f));
                    tang = new float3(_hand.X, Tub.Y + 0.42f - dip + 0.25f, _hand.Z);
                    break;
                }
                default:
                    along = _at == Place.Anvil ? new float3(1, 0, 0) : forward;
                    tang = _hand - along * length * 0.5f;
                    break;
            }
            DrawPiece(Orient(new float3(1, 0, 0), along) * float4x4.CreateTranslation(tang), temp, shape);
            // The tongs grip the tang from the smith's side; the left hand on their handles.
            var back = float3.NormalizeOrZero(leftShoulder - tang);
            var grip = tang + back * 0.46f;
            _meshes.Draw(_models["tongs"], Orient(new float3(1, 0, 0), float3.NormalizeOrZero(tang - grip)) * float4x4.CreateTranslation(grip));
            DrawArm(leftShoulder, grip, -forward);
        }

        switch (Session)
        {
            case HammerSession h:
                DrawHammer(h, rightShoulder, forward);
                break;
            case HeatSession:
            {
                var handle = BellowsHandle + new float3(0, _grabBellows ? _handleLift : 0.03f * MathF.Sin(_bellowsPhase * MathF.PI * 2f), 0);
                if (_grabBellows)
                    DrawArm(rightShoulder, handle, -forward);
                break;
            }
        }
    }

    /// <summary>The bar drawn out to a blade as it's shaped, glowing its heat (ForgeGlow: heat is shown only as colour).</summary>
    private void DrawPiece(in float4x4 at, float temp, float shape)
    {
        var glow = ForgeGlow.Colour(temp);
        var e = new float3(glow.X, glow.Y, glow.Z) * ForgeGlow.Strength(temp) * 2.2f;
        if (shape < 0.5f)
        {
            var stretch = 1f + shape * 1.4f;
            _meshes.Draw(_models["bar"], float4x4.CreateScale(new float3(stretch, 1f - shape * 0.5f, 1f + shape * 0.3f)) * at, emissive: e);
        }
        else
        {
            var grow = 0.7f + 0.3f * (shape - 0.5f) * 2f;
            _meshes.Draw(_models["blade"], float4x4.CreateScale(new float3(grow, 1f, 0.8f + 0.2f * grow)) * at, emissive: e);
        }
    }

    /// <summary>The piece's tang on the anvil: it overhangs the heel, the tongs holding it from there.</summary>
    private static readonly float3 AnvilTang = AnvilFace + new float3(-0.4f, 0.022f, 0f);

    /// <summary>How long the piece is as it's shaped (as <see cref="DrawPiece"/> draws it).</summary>
    private static float PieceLength(float shape) =>
        shape < 0.5f ? BarLength * (1f + shape * 1.4f) : BladeLength * (0.7f + 0.3f * (shape - 0.5f) * 2f);

    /// <summary>Where on the piece the hammer is over: Offset -1..1 along its working length, past the tang.</summary>
    private static float3 OverPiece(float offset, float shape)
    {
        var length = PieceLength(shape);
        return AnvilTang + new float3(length * 0.58f + offset * length * 0.36f, 0.01f, 0f);
    }

    private void DrawHammer(HammerSession h, float3 shoulder, float3 forward)
    {
        // The mark: a faint ring on the piece where the next blow wants to land, brighter with the hammer over it.
        var ring = OverPiece(h.Spot, h.Shape) + new float3(0, 0.012f, 0);
        var over = h.Centred ? 1f : 0.35f;
        _meshes.Draw(_models["mark"], float4x4.CreateScale(new float3(1f, 1f, 1.3f)) * float4x4.CreateTranslation(ring),
            emissive: new float3(0.9f, 0.85f, 0.6f) * over, unlit: true, blend: MeshBlend.Additive, outline: false);

        // The hammer pivots at its grip on the near side of the piece and rises with Raise; a ghost shows the mark to raise it to.
        var strike = OverPiece(h.Offset, h.Shape);
        float4x4 Hammer(float raise)
        {
            var grip = strike + new float3(0, 0.035f + raise * 0.22f, 0.43f);
            return float4x4.CreateRotationY(MathF.PI / 2f) * float4x4.CreateRotationX(raise * 1.9f) * float4x4.CreateTranslation(grip);
        }
        var since = _time - _struckAt;
        var kick = since < 0.12f ? (1f - since / 0.12f) * 0.04f : 0f; // a little bounce off the iron
        var hammer = Hammer(MathF.Max(0f, h.Raise) + kick);
        _meshes.Draw(_models["hammer"], hammer);
        if (h.Raise > 0.02f && !h.Swinging)
        {
            var near = h.InBand ? 1f : 0.35f;
            _meshes.Draw(_models["hammer"], Hammer(h.Mark), tint: new float4(1f, 0.85f, 0.5f, 0.22f + 0.25f * near), emissive: new float3(0.5f, 0.35f, 0.1f) * near,
                unlit: true, blend: MeshBlend.Alpha, outline: false);
        }
        DrawArm(shoulder, float3.Transform(new float3(0.02f, 0, 0), hammer), -forward);
    }

    /// <summary>
    /// Two bones from the shoulder to the hand, the elbow bent toward <paramref name="bendToward"/> and down. Out of reach,
    /// the shoulder leans in after the hand (there's no body drawn to give it away).
    /// </summary>
    private void DrawArm(float3 shoulder, float3 hand, float3 bendToward)
    {
        const float upper = 0.3f, lower = 0.3f;
        var to = hand - shoulder;
        if (to.Length() > upper + lower - 0.01f)
            shoulder = hand - float3.NormalizeOrZero(to) * (upper + lower - 0.01f);
        to = hand - shoulder;
        var dist = to.Length();
        var dir = float3.NormalizeOrZero(to);
        var hint = bendToward + new float3(0, -0.6f, 0);
        var bend = float3.NormalizeOrZero(hint - dir * float3.Dot(hint, dir));
        var a = (upper * upper + dist * dist - lower * lower) / (2 * dist);
        var h = MathF.Sqrt(MathF.Max(0f, upper * upper - a * a));
        var elbow = shoulder + dir * a + bend * h;
        _meshes.Draw(_models["smith_upper_arm"], Orient(new float3(0, -1, 0), float3.NormalizeOrZero(elbow - shoulder)) * float4x4.CreateTranslation(shoulder));
        _meshes.Draw(_models["smith_forearm"], float4x4.CreateTranslation(new float3(0, 0.04f, 0))
            * Orient(new float3(0, -1, 0), float3.NormalizeOrZero(hand - elbow)) * float4x4.CreateTranslation(elbow));
    }

    /// <summary>A rotation taking <paramref name="from"/> to <paramref name="to"/> (both unit).</summary>
    private static float4x4 Orient(float3 from, float3 to)
    {
        var axis = float3.Cross(from, to);
        var s = axis.Length();
        var c = float3.Dot(from, to);
        if (s < 1e-5f)
            return c > 0 ? float4x4.Identity : float4x4.CreateFromAxisAngle(MathF.Abs(from.X) < 0.9f ? new float3(1, 0, 0) : new float3(0, 0, 1), MathF.PI);
        return float4x4.CreateFromAxisAngle(axis / s, MathF.Atan2(s, c));
    }

    // ---------------- words ----------------

    public override void DrawUi()
    {
        var io = ImGui.GetIO();
        var pos = new float2(io.DisplaySize.X * 0.5f, io.DisplaySize.Y - 24f);
        var pivot = new float2(0.5f, 1f);
        ImGui.SetNextWindowPos(pos, ImGuiCond.Always, pivot);
        ImGui.SetNextWindowBgAlpha(0.55f);
        ImGui.Begin("##forge3d"u8, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoFocusOnAppearing
            | ImGuiWindowFlags.NoInputs);
        if (_said is { } said && _time - said.At < 5f)
            ImGui.TextColored(new float4(0.95f, 0.85f, 0.65f, 1f), said.Text);
        ImGui.Text(Hint());
        if (_shaderError != null)
            ImGui.TextColored(new float4(1f, 0.5f, 0.4f, 1f), _shaderError);
        ImGui.End();

        if (!_debug)
            return;
        ImGui.Begin("Forge 3D (F1)"u8, ImGuiWindowFlags.AlwaysAutoResize);
        ImGui.Text($"{_meshes.LastFrameInstances} instances, {_meshes.LastFrameDrawCalls} draws, {Host.FrameTimeMs:F2} ms");
        ImGui.Text($"hand {_hand.X:F2},{_hand.Y:F2},{_hand.Z:F2}  at {_at}  over work {OverWork(_at, _hand)}  pump {_pumpMotion:F0}");
        switch (Session)
        {
            case HeatSession s:
                ImGui.Text($"heat: fire {s.Fire:F0} ({s.FireRest:F0}..{s.FireMax:F0}), piece {s.Temp:F0} (work {s.Lo:F0}..{s.Hi:F0}, burns {s.BurnAt:F0}), ready {s.LooksReady}");
                break;
            case HammerSession s:
                ImGui.Text($"hammer: raise {s.Raise:F2} mark {s.Mark:F2} band {s.InBand}, offset {s.Offset:F2} spot {s.Spot:F2} centred {s.Centred}");
                ImGui.Text($"blows {s.Blows}, shape {s.Shape:F2}, temp {s.Temp:F0}, workable {s.Workable:F2}, quality {s.Quality:F2}");
                break;
            case QuenchSession s:
                ImGui.Text($"quench: temp {s.Temp:F0} (lift {s.Lo:F0}..{s.Hi:F0}), dunked {s.Dunked}, steam {s.Steam:F2}");
                break;
        }
        ImGui.End();
    }

    /// <summary>One line of what the hands do now: the only words on screen, the rest is in the world.</summary>
    private string Hint()
    {
        string K(string a) => Controls.Glyph(a);
        var walk = $"{K(Act.WalkLeft)}/{K(Act.WalkRight)}: step to the {(_at == Place.Anvil ? "fire or the tub" : _at == Place.Forge ? "anvil" : "anvil")}";
        return Session switch
        {
            HeatSession => _grabBellows ? "Pump it up and down" : "Drag the bellows' top board up and down to blow    Drag the piece out of the coals to draw it    (read the heat by its glow)",
            HammerSession => "Hold the mouse: raise the hammer to the ghost    Let go: strike    Move: over the ring    Right-click: pick it up",
            QuenchSession => "Hold the mouse: keep it under    Let go: lift it out",
            _ when Piece == null => $"{(_lastMade.IsNull ? "" : $"Made: {Sim.LabelOf(_lastMade)} ({Sim.World.Get<Crafted>(_lastMade).Quality}).    ")}Click: another billet",
            _ => _at switch
            {
                Place.Forge => $"Click over the coals to lay it in the fire    {walk}",
                Place.Anvil => $"Click over the anvil to lay it there    {walk}",
                _ => $"Click and hold over the water to dip it    {walk}",
            },
        };
    }
}

using Brutal.ImGuiApi;
using Narsil.Ui;
using Narsil.Platform;
using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Bots;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Work;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

// Speech, hands-on work and solo bots. The play is all in the sim; this reads keys, sends
// commands and draws what the character knows.
public sealed partial class VillageGame
{
    /// <summary><c>--bots N</c>: how many stand-in players a solo round deals in.</summary>
    public int Bots { get; init; }

    /// <summary><c>--bot-skill F</c>: how steady the bots' hands are (<see cref="BotPlayer.Skill"/>).</summary>
    public float BotSkill { get; init; } = 1f;

    private readonly List<BotPlayer> _bots = [];
    private readonly WorkPredictor _work = new();
    private double _workSteps;
    private readonly ImInputString _speechInput = new(SpeechSystem.MaxLength + 8, ""u8);
    private int _speechSeen;

    private bool Working => HasAvatar && _sim.World.Has<WorkSession>(_avatar);

    // Whether F has come up since the session in hand began (see UpdateWork).
    private long _fArmedFor = -1;
    private bool _fArmed;

    /// <summary>
    /// A frame of hands-on work, through the actions (<see cref="Controls"/>): Use (F, A) strikes,
    /// the walk (A/D and W/S, the left stick) steers, Work act (Space, X) is the game's other
    /// action, the runes (1–9, the d-pad and shoulders) weave, and Back (Esc, B) puts it down.
    /// </summary>
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
        var map = Controls.Map;
        if (Working && BackPressed())
            Issue(new StopWorkCommand(_avatar));
        var live = speed > 0 && Working;
        var axis = !live ? 0 : (map.Down(Act.WalkRight) ? 1 : 0) - (map.Down(Act.WalkLeft) ? 1 : 0);
        var axis2 = !live ? 0 : (map.Down(Act.WalkDown) ? 1 : 0) - (map.Down(Act.WalkUp) ? 1 : 0);
        // The F that started a job doesn't also work it: a new session waits for F to come up
        // before a press or hold counts (else a long press to start the quench plunges the piece).
        var session = Working ? _sim.World.Get<WorkSession>(_avatar).StartTick : -1;
        if (session != _fArmedFor)
            (_fArmedFor, _fArmed) = (session, false);
        if (!map.Down(Act.Use))
            _fArmed = true;
        var f = _fArmed && speed > 0;
        var rune = -1;
        for (var k = 0; live && k < Act.Runes.Length; k++)
            if (map.Pressed(Act.Runes[k]))
                rune = k;
        var act = live && map.Pressed(Act.WorkAct);
        if (_work.Session is Sim.Environment.ThrowSession throwing)
            axis = AimThrow(throwing, axis);
        // The mouse over the work's window: the left button is F, the right Space, and where it points
        // over the picture (or a place marked to click) steers the games that take it.
        var mouse = live && session >= 0 ? MouseOnWork(session) : WorkMouse.None;
        if ((f && map.Pressed(Act.Use)) || mouse.Click >= 0)
            _workPressedAt = _time;
        // A mod's own inputs for its session (a note on the pipe), read from its own controls.
        var custom = live ? Mods.GameMods.InputsFor(ModWorkView, _work.Session) : null;
        _work.Update(_sim, _avatar, steps, (f && map.Pressed(Act.Use)) || mouse.Press, f && live && (map.Down(Act.Use) || mouse.Held),
            axis != 0 ? axis : mouse.Axis, Issue, axis2 != 0 ? axis2 : mouse.Axis2, rune >= 0 ? rune : mouse.Rune, act || mouse.Act, mouse.Point, mouse.Click, custom);
    }

    /// <summary>What the mouse did to the work in hand this frame, as the keys would have.</summary>
    private readonly record struct WorkMouse(int Point, int Click, bool Press, bool Held, bool Act, int Rune, int Axis, int Axis2)
    {
        public static WorkMouse None => new(-1, -1, false, false, false, -1, 0, 0);
    }

    /// <summary>A place on a mini-game's picture that a click works as a key would (a rune, one side, the other action).</summary>
    private readonly record struct WorkSpot(float2 Min, float2 Max, WorkInputKind Kind, int Value);

    // When F (or a click) last went down on the work: a drawing that answers a press at once (the knife falling) reads it.
    private float _workPressedAt = -10f;

    // What the last frame's draw of the work marked for the mouse (BeginWork, TakesPointer, ClickSpot), and for which session.
    private (float2 Min, float2 Max)? _workWindow, _pointArea;
    private long _workDrawnFor = -1;
    private bool _workWheel, _pointEdges;
    private readonly List<WorkSpot> _workSpots = [];

    // A left press begun on the work, still down: it holds as F does, and drags the point along even off the picture.
    private bool _grip;

    // Where the mouse was when the session began: it points nothing till it's moved, so one resting over
    // the panel doesn't yank the tool to it.
    private long _mouseSeenFor = -1;
    private float2 _mouseAtStart;
    private bool _mouseMoved;

    // When the mouse last worked a mini-game: the hints name the mouse while it's in use.
    private float _pointerUsedAt = -100f;

    private bool UsingPointer => _time - _pointerUsedAt < 4f && !Controls.UsingPad;

    /// <summary>
    /// A mini-game's picture takes the mouse's place over this part of the screen, this frame: u and v
    /// run 0..1 across it (<see cref="WorkSession.Pointed"/>). <paramref name="wheel"/>: the wheel turns
    /// as W/S would (up is W).
    /// </summary>
    /// <paramref name="edges"/>: the mouse is read even off the picture, held to its nearest edge, for a
    /// game that moves a thing as far as the mouse moves and lets an edge push on (the hammer).
    private void TakesPointer(float2 min, float2 max, bool wheel = false, bool edges = false)
    {
        _pointArea = (min, max);
        _workWheel = wheel;
        _pointEdges = edges;
    }

    /// <summary>A click over this part of the picture works as this input, not as F.</summary>
    private void ClickSpot(float2 min, float2 max, WorkInputKind kind, int value = 0) =>
        _workSpots.Add(new WorkSpot(min, max, kind, value));

    /// <summary>The work's window, opened: it takes the mouse's buttons (<see cref="MouseOnWork"/>) unless <paramref name="mouse"/> is off.</summary>
    private void WorkWindowDrawn(bool mouse)
    {
        _workDrawnFor = _sim.World.TryGet<WorkSession>(_avatar, out var s) ? s.StartTick : -1;
        var pos = ImGui.GetWindowPos();
        _workWindow = mouse ? (pos, pos + ImGui.GetWindowSize()) : null;
        _pointArea = null;
        _workWheel = false;
        _pointEdges = false;
        _workSpots.Clear();
    }

    private static int Packed(float2 at, (float2 Min, float2 Max) r)
    {
        var size = r.Max - r.Min;
        return WorkSession.PackPoint(size.X > 0 ? (at.X - r.Min.X) / size.X : 0.5f, size.Y > 0 ? (at.Y - r.Min.Y) / size.Y : 0.5f);
    }

    private static bool Inside(float2 at, (float2 Min, float2 Max) r) =>
        at.X >= r.Min.X && at.Y >= r.Min.Y && at.X <= r.Max.X && at.Y <= r.Max.Y;

    /// <summary>
    /// The mouse on the work in hand, from what its window marked last frame: a left click there is a
    /// click (<see cref="WorkInputKind.Click"/>: F, unless the game minds where) or a marked spot's input, and holding it holds F; a right click is Space; its place
    /// over the picture is sent packed (<see cref="WorkSession.PackPoint"/>) once it's moved this session.
    /// Nothing while a pad's in use.
    /// </summary>
    private WorkMouse MouseOnWork(long session)
    {
        if (_mouseSeenFor != session)
            (_mouseSeenFor, _mouseAtStart, _mouseMoved, _grip) = (session, ImGui.GetMousePos(), false, false);
        if (_workDrawnFor != session || _workWindow is not { } window || Controls.UsingPad)
        {
            _grip = false;
            return WorkMouse.None;
        }
        var at = ImGui.GetMousePos();
        if (at != _mouseAtStart)
            _mouseMoved = true;
        var over = Inside(at, window);
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left))
            _grip = false;
        var m = WorkMouse.None;
        if (over && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _mouseMoved = true; // a click is the mouse taken up: it points from here on
            var spot = _workSpots.FindIndex(s => Inside(at, (s.Min, s.Max)));
            if (spot < 0)
                (_grip, m) = (true, m with { Click = _pointArea is { } a && Inside(at, a) ? Packed(at, a) : WorkSession.OffPicture });
            else
                m = _workSpots[spot] switch
                {
                    { Kind: WorkInputKind.Rune } s => m with { Rune = s.Value },
                    { Kind: WorkInputKind.Axis } s => m with { Axis = s.Value },
                    { Kind: WorkInputKind.Axis2 } s => m with { Axis2 = s.Value },
                    { Kind: WorkInputKind.Act } => m with { Act = true },
                    _ => m with { Press = true },
                };
        }
        if (over && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            m = m with { Act = true };
        // A notch of the wheel holds W (up) or S a moment: a turn of the wrench, or one wedge for a game counting presses.
        var wheel = ImGui.GetIO().MouseWheel;
        if (over && _workWheel && wheel != 0f)
            (_wheelWay, _wheelUntil) = (wheel > 0f ? -1 : 1, _time + WheelNotch);
        if (_workWheel && _time < _wheelUntil && m.Axis2 == 0)
            m = m with { Axis2 = _wheelWay };
        m = m with { Held = _grip };
        if (_pointArea is { } area && _mouseMoved && (_grip || _pointEdges || Inside(at, area)))
            m = m with { Point = Packed(at, area) };
        if (over && (_mouseMoved && at != _mouseLast || m.Click >= 0 || m.Press || m.Held || m.Act || m.Rune >= 0 || m.Axis != 0 || m.Axis2 != 0))
            _pointerUsedAt = _time;
        _mouseLast = at;
        return m;
    }

    private float2 _mouseLast;

    // The wheel's last notch, held as W/S till then.
    private int _wheelWay;
    private float _wheelUntil = -1f;
    private const float WheelNotch = 0.12f;

    private int3 _thrownAt;

    /// <summary>
    /// A bucket being swung: the mouse points it (sent as it moves, and set on the local copy so the
    /// landing shown follows at once), and A/D turn it the way they read on screen: D clockwise.
    /// </summary>
    private int AimThrow(Sim.Environment.ThrowSession throwing, int axis)
    {
        var pointing = !ImGui.GetIO().WantCaptureMouse && !_pointer.Stale && !Controls.UsingPad && !Host.Input.MouseCaptured;
        if (pointing && axis == 0 && _hoverCell != _thrownAt && (_hoverCell.X != throwing.From.X || _hoverCell.Y != throwing.From.Y))
        {
            _thrownAt = _hoverCell;
            throwing.AimAt(_hoverCell);
            Issue(new Sim.Environment.AimThrowCommand(_avatar, _hoverCell));
        }
        // World angles turn the other way on screen when the view is mirrored: keep D clockwise as you see it.
        var o = _camera.WorldToScreen(new float3(0, 0, 0));
        var x = _camera.WorldToScreen(new float3(1, 0, 0)) - o;
        var y = _camera.WorldToScreen(new float3(0, 1, 0)) - o;
        return x.X * y.Y - x.Y * y.X > 0 ? axis : -axis;
    }

    /// <summary>
    /// How each of the game's work sessions is drawn, by session type. A mod's view
    /// (<see cref="Mods.GameMods"/>) is looked up first, so it can add a session's look or restyle one.
    /// </summary>
    private static readonly Dictionary<Type, Action<VillageGame, WorkSession>> WorkViews = new()
    {
        [typeof(SharpenSession)] = (g, s) => g.DrawSharpen((SharpenSession)s),
        [typeof(PourSession)] = (g, s) => g.DrawPour((PourSession)s),
        [typeof(Sim.Healing.HandsOnSession)] = (g, s) => g.DrawHandsOn((Sim.Healing.HandsOnSession)s),
        [typeof(Sim.Healing.BandageSession)] = (g, s) => g.DrawBandage((Sim.Healing.BandageSession)s),
        [typeof(Sim.Healing.CastSession)] = (g, s) => g.DrawCast((Sim.Healing.CastSession)s),
        [typeof(Sim.Healing.MeditateSession)] = (g, s) => g.DrawMeditate((Sim.Healing.MeditateSession)s),
        [typeof(Sim.Tinkering.ProbeSession)] = (g, s) => g.DrawProbe((Sim.Tinkering.ProbeSession)s),
        [typeof(Sim.Tinkering.PickSession)] = (g, s) => g.DrawPick((Sim.Tinkering.PickSession)s),
        [typeof(Sim.Tinkering.PinSession)] = (g, s) => g.DrawPin((Sim.Tinkering.PinSession)s),
        [typeof(Sim.Tinkering.FitSession)] = (g, s) => g.DrawFit((Sim.Tinkering.FitSession)s),
        [typeof(Sim.Tinkering.TrueSession)] = (g, s) => g.DrawTrue((Sim.Tinkering.TrueSession)s),
        [typeof(Sim.Tinkering.SpanSession)] = (g, s) => g.DrawSpan((Sim.Tinkering.SpanSession)s),
        [typeof(Sim.Tinkering.LoupeSession)] = (g, s) => g.DrawLoupe((Sim.Tinkering.LoupeSession)s),
        [typeof(StrikerSession)] = (g, s) => g.DrawStriker((StrikerSession)s),
        [typeof(SledgeSession)] = (g, s) => g.DrawSledge((SledgeSession)s),
        [typeof(Sim.Afterlife.HoldSession)] = (g, s) => g.DrawHold((Sim.Afterlife.HoldSession)s),
        [typeof(Sim.Environment.ThrowSession)] = (g, s) => g.DrawThrow((Sim.Environment.ThrowSession)s),
        [typeof(HammerSession)] = (g, s) => g.DrawHammer((HammerSession)s),
        [typeof(HeatSession)] = (g, s) => g.DrawHeat((HeatSession)s),
        [typeof(KindleSession)] = (g, s) => g.DrawKindle((KindleSession)s),
        [typeof(BloomSession)] = (g, s) => g.DrawBloom((BloomSession)s),
        [typeof(QuenchSession)] = (g, s) => g.DrawQuench((QuenchSession)s),
        [typeof(StewSession)] = (g, s) => g.DrawStew((StewSession)s),
        [typeof(SeasonSession)] = (g, s) => g.DrawSeason((SeasonSession)s),
        [typeof(SniffSession)] = (g, s) => g.DrawSniff((SniffSession)s),
        [typeof(PackSession)] = (g, s) => g.DrawPack((PackSession)s),
        [typeof(ChopSession)] = (g, s) => g.DrawChop((ChopSession)s),
        [typeof(RoastSession)] = (g, s) => g.DrawRoast((RoastSession)s),
        [typeof(KneadSession)] = (g, s) => g.DrawKnead((KneadSession)s),
        [typeof(BakeSession)] = (g, s) => g.DrawBake((BakeSession)s),
        [typeof(CrimpSession)] = (g, s) => g.DrawCrimp((CrimpSession)s),
        [typeof(ButcherSession)] = (g, s) => g.DrawButcher((ButcherSession)s),
        [typeof(PluckSession)] = (g, s) => g.DrawPluck((PluckSession)s),
        [typeof(WashSession)] = (g, s) => g.DrawWash((WashSession)s),
        [typeof(TapSession)] = (g, s) => g.DrawTap((TapSession)s),
        [typeof(MixSession)] = (g, s) => g.DrawMix((MixSession)s),
        [typeof(Sim.Hospitality.ScrubSession)] = (g, s) => g.DrawScrub((Sim.Hospitality.ScrubSession)s),
        [typeof(Sim.Hospitality.MakeBedSession)] = (g, s) => g.DrawMakeBed((Sim.Hospitality.MakeBedSession)s),
        [typeof(Sim.Hospitality.SignInSession)] = (g, s) => g.DrawSignIn((Sim.Hospitality.SignInSession)s),
        [typeof(Sim.Hospitality.SlopSession)] = (g, s) => g.DrawSlop((Sim.Hospitality.SlopSession)s),
    };

    private void DrawWork()
    {
        var session = _work.Session;
        if (session == null)
        {
            DrawTongs();
            DrawStationCard();
            return;
        }
        if (Mods.GameMods.TryDraw(ModWorkView, session))
            return;
        for (var t = session.GetType(); t != null && t != typeof(WorkSession); t = t.BaseType)
            if (WorkViews.TryGetValue(t, out var draw))
            {
                draw(this, session);
                return;
            }
    }

    /// <summary>What your character has heard, and the box for saying something. Replaces chat once a round starts.</summary>
    private void DrawSpeech()
    {
        if (!_sim.Round.Live || (!HasAvatar && !Ghosting) || !_showHeard)
            return;
        BeginJournal("Heard###speech", ref _showHeard, 250);
        var heard = View.Heard().ToList();
        float2? size = new float2(0, -30);
        if (ImGui.BeginChild("##speech-log"u8, in size, ImGuiChildFlags.Borders, ImGuiWindowFlags.None))
        {
            foreach (var line in heard)
            {
                var how = line.Volume switch { Volume.Whisper => " whispers", Volume.Shout => " shouts", Volume.Ghost => " (dead)", _ => "" };
                TextC(line.Volume == Volume.Shout ? Theme.Warn : line.Volume == Volume.Ghost ? Theme.AccentSoft : Theme.Dim, $"{line.Who}{how}:");
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
        var said = ImGui.InputTextWithHint("##speech"u8, Ghosting ? "Speak with the dead. Only they hear you."u8 : "Say something. /w whispers, /s shouts."u8, _speechInput, ImGuiInputTextFlags.EnterReturnsTrue, null, IntPtr.Zero);
        OnScreenKeyboard.AfterTextField();
        if (said)
        {
            var (volume, text) = SpeechSystem.Parse(Narsil.Ui.ImGuiText.Value(_speechInput));
            Issue(new SayCommand(HasAvatar ? _avatar : _ghost, text, volume));
            _speechInput.SetValue("".AsSpan());
            ImGui.SetKeyboardFocusHere(-1); // keep typing
        }
        Panel.End();
    }

    /// <summary>
    /// The tankard held under the cask's tap by its mouth, tipped toward the tap as it's tilted (A/D):
    /// its foot swings out while the mouth stays under the stream, which runs down the inner wall when
    /// it's tipped and splashes into the ale when it's upright. The ale and its head keep level; the
    /// line is marked on the walls; the low lip is where it runs over. One picture, drawn to the
    /// session's numbers: the lean is the one at which the ale reaches the lip at <see cref="PourSession.Lip"/>.
    /// </summary>
    private void DrawPour(PourSession s)
    {
        BeginWork(s.Title, ("F", "hold to pour"), ("A/D|Mouse", "tip it: less foam, slower"), ("Esc", "stop"));
        var nearLip = s.Top > s.Lip - 0.06f && s.Tilt > 0.05f;
        var (colour, advice) =
            s.Lost > 0f && s.Top >= s.Lip - 0.005f ? (Theme.Bad, "It's running over the low lip! Bring it upright.")
            : nearLip ? (Theme.Warn, "Nearly at the low lip: bring it upright before it runs over.")
            : s.Held && s.Tilt > 0.5f ? (Theme.Good, "Tipped: it runs down the side and barely foams. Upright for the last of it.")
            : s.Held ? (Theme.Text, "Upright, it fills fast and foams up. Let go short of the line: the head settles.")
            : s.Level > 0.05f ? (Theme.Dim, "Settling... the head sinks a little as it goes.")
            : (Theme.Dim, "Tip the tankard (A/D) and hold F to pour. Bring it upright near the top, and let go at the line.");
        TextC(colour, advice);

        var (min, max) = PourPicture(s.Tilt, s.Level, s.Foam, s.Line, s.Lost, s.Held);
        TakesPointer(min, max); // left upright, right tipped
        var stateColour = s.Lost > 0f ? Theme.Bad : Theme.Dim;
        TextC(stateColour, $"Head {s.Foam * 100:F0}%{(s.Lost > 0f ? $"   run over {s.Lost * 100:F0}%" : "")}   {PourSession.ServingsLeft(_sim, s.Cask)} left in the cask");
        EndWork();
    }

    /// <summary>The pour's picture, from its numbers (the session's, or the scene gallery's); where it was drawn.</summary>
    private (float2 Min, float2 Max) PourPicture(float tilt, float level, float foam, float line, float lost, bool held)
    {
        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(420, 230);
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(in size);
        var max = min + size;
        Gui.Frame(dl, min, max, UiTheme.Current.Frames.Inset, Theme.Well);

        // The tankard's size, and its mouth's centre: held under the tap.
        var w = Gui.S(76);
        var h = Gui.S(116);
        var inset = Gui.S(6);
        var mouth = new float2(min.X + size.X * 0.5f + Gui.S(20), min.Y + Gui.S(92));

        // The cask's end and its tap above and to the right; the spout pours just inside the mouth.
        var caskC = new float2(mouth.X + Gui.S(70), min.Y + Gui.S(40));
        var spout = CaskAndTap(dl, caskC, MathF.Max(1f, MathF.Round(1.5f * Gui.Scale)), Gui.S(30));

        // Leaning the mouth toward the tap: at this lean, the low lip is at the session's Lip.
        var lean = MathF.Atan(PourSession.LipDrop * tilt * (h - inset) / (w * 0.5f));
        float cos = MathF.Cos(lean), sin = MathF.Sin(lean);
        // x across the cup, y up it from the foot (negative on screen), turned about the mouth.
        float2 Cup(float x, float y)
        {
            var dx = x;
            var dy = y + h; // from the mouth
            return mouth + new float2(dx * cos - dy * sin, dx * sin + dy * cos);
        }
        var outer = new[] { Cup(-w / 2, 0), Cup(w / 2, 0), Cup(w / 2, -h), Cup(-w / 2, -h) };
        // The handle, on the side away from the tap.
        var hc = new ImColor8(70, 48, 30, 255);
        var h0 = Cup(-w / 2, -h * 0.25f);
        var h1 = Cup(-w / 2 - Gui.S(18), -h * 0.42f);
        var h2 = Cup(-w / 2 - Gui.S(18), -h * 0.62f);
        var h3 = Cup(-w / 2, -h * 0.78f);
        dl.AddLine(in h0, in h1, hc, Gui.S(6));
        dl.AddLine(in h1, in h2, hc, Gui.S(6));
        dl.AddLine(in h2, in h3, hc, Gui.S(6));
        Fill(dl, outer, hc);
        var inner = new[] { Cup(-w / 2 + inset, -inset), Cup(w / 2 - inset, -inset), Cup(w / 2 - inset, -h), Cup(-w / 2 + inset, -h) };
        Fill(dl, inner, new ImColor8(40, 28, 18, 255));
        // The ale and its head, level whatever the lean: up to the height of their share of the cup's axis.
        float LevelY(float f) => Cup(0, -inset - (h - inset) * Math.Clamp(f, 0f, 1f)).Y;
        var aleTop = LevelY(level);
        var topY = LevelY(level + foam);
        Fill(dl, Below(inner, aleTop), new ImColor8(184, 122, 40, 255));
        Fill(dl, Below(Above(inner, aleTop), topY), new ImColor8(240, 232, 210, 255));
        // The line, a mark on each wall inside (not across the mouth: tipped, that's open air).
        var lineY = -inset - (h - inset) * line;
        var tick = Gui.S(12);
        var la0 = Cup(-w / 2 + inset, lineY);
        var la1 = Cup(-w / 2 + inset + tick, lineY);
        var lb0 = Cup(w / 2 - inset, lineY);
        var lb1 = Cup(w / 2 - inset - tick, lineY);
        dl.AddLine(in la0, in la1, Theme.U8(Theme.Good, 0.95f), Gui.S(2.5f));
        dl.AddLine(in lb0, in lb1, Theme.U8(Theme.Good, 0.95f), Gui.S(2.5f));
        // The low lip, warm as the ale nears it.
        var lipAt = Cup(w / 2, -h);
        var nearLip = level + foam > 1f - PourSession.LipDrop * tilt - 0.06f && tilt > 0.05f;
        if (nearLip || lost > 0f)
            dl.AddCircleFilled(in lipAt, Gui.S(4), Theme.U8(lost > 0f ? Theme.Bad : Theme.Warn, 0.9f), 12);
        // What's run over, down the outside and on the boards.
        if (lost > 0f)
        {
            var drip = new float2(lipAt.X + Gui.S(2), max.Y - Gui.S(8));
            dl.AddLine(in lipAt, in drip, new ImColor8(200, 140, 50, 200), Gui.S(3));
            dl.AddCircleFilled(in drip, Gui.S(3) + Gui.S(12) * MathF.Min(1f, lost / PourSession.SpillLimit), new ImColor8(184, 122, 40, 150), 18);
        }

        // The stream while you hold F: straight down from the spout to the ale, or to the inner wall
        // it meets first when tipped, then down that wall to the ale.
        if (held)
        {
            var stream = new ImColor8(200, 140, 50, 230);
            var x = spout.X;
            var wallY = float.MaxValue;
            float2? along = null;
            for (var i = 0; i < inner.Length; i++)
            {
                var a = inner[i];
                var b = inner[(i + 1) % inner.Length];
                if ((a.X - x) * (b.X - x) > 0f || MathF.Abs(b.X - a.X) < 1e-3f)
                    continue;
                var y = a.Y + (b.Y - a.Y) * (x - a.X) / (b.X - a.X);
                if (y > spout.Y && y < wallY && y > Cup(0, -h).Y + Gui.S(4))
                {
                    wallY = y;
                    along = (a.Y > b.Y ? a : b) - (a.Y > b.Y ? b : a); // down the wall
                }
            }
            var end = new float2(x, Math.Min(wallY, topY));
            dl.AddLine(in spout, in end, stream, Gui.S(4));
            if (wallY < topY && along is { } dir)
            {
                // Down the wall to the surface.
                var t = (topY - end.Y) / MathF.Max(1e-3f, dir.Y);
                var down = end + dir * t;
                dl.AddLine(in end, in down, stream, Gui.S(3));
            }
            else
                dl.AddCircleFilled(new float2(x, topY), Gui.S(3) + Gui.S(4) * (1f - tilt), new ImColor8(245, 238, 215, 220), 12); // the splash that makes foam
        }

        return (min, max);
    }

    /// <summary>A convex shape filled, as a fan of triangles.</summary>
    private static void Fill(ImDrawListPtr dl, IReadOnlyList<float2> poly, ImColor8 colour)
    {
        for (var i = 1; i + 1 < poly.Count; i++)
        {
            var a = poly[0];
            var b = poly[i];
            var c = poly[i + 1];
            dl.AddTriangleFilled(in a, in b, in c, colour);
        }
    }

    /// <summary>The part of a convex shape below a level on screen (y down): what liquid fills.</summary>
    private static List<float2> Below(IReadOnlyList<float2> poly, float level) => Clip(poly, p => p.Y >= level, level);

    /// <summary>The part of a convex shape above a level on screen.</summary>
    private static List<float2> Above(IReadOnlyList<float2> poly, float level) => Clip(poly, p => p.Y <= level, level);

    // One edge of Sutherland-Hodgman against a horizontal line.
    private static List<float2> Clip(IReadOnlyList<float2> poly, Func<float2, bool> keep, float level)
    {
        var output = new List<float2>();
        for (var i = 0; i < poly.Count; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Count];
            bool ina = keep(a), inb = keep(b);
            if (ina)
                output.Add(a);
            if (ina != inb && MathF.Abs(b.Y - a.Y) > 1e-4f)
            {
                var t = (level - a.Y) / (b.Y - a.Y);
                output.Add(a + (b - a) * t);
            }
        }
        return output;
    }

    /// <summary>The whetstone: the blade's angle against the band, the steel's heat and the edge coming back.</summary>
    private void DrawSharpen(SharpenSession s)
    {
        BeginWork(s.Title, ("F", "hold to grind"), ("A/D|Move", "keep the angle in the band"), ("Esc", "stop"));
        TextC(Theme.Dim, "Ease off before the steel overheats.");
        var top = ImGui.GetCursorScreenPos();
        SharpenScene(s.StartTick, s.Held, s.Angle, s.Heat, s.Progress, s.OnWheel);
        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(420, 30);
        var min = ImGui.GetCursorScreenPos();
        var max = min + size;
        ImGui.Dummy(in size);
        TakesPointer(new float2(min.X, top.Y), max); // across, the gauge's width: the blade turns as far as the mouse moves
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

    /// <summary>What the probe has felt along the seam this search, bin by bin (the player's own memory of it).</summary>
    private readonly float[] _probeTrace = new float[96];
    private long _probeTraceFor = -1;

    /// <summary>
    /// The lid's seam with the probe on it, what the probe has told the fingers along it, the
    /// pressure against the zones that matter, and a line saying what to do now. All of it reads
    /// what the player feels and does (the feel, the pressure, the probe's motion), never the trap.
    /// </summary>
    private void DrawProbe(Sim.Tinkering.ProbeSession s)
    {
        BeginWork(s.Title, ("A/D|Point", "run the probe (F let go)"), ("F", "hold, still, to press in"), ("Space", "F held: flick it out · F let go: done"), ("Esc", "stop"));
        if (_probeTraceFor != s.StartTick)
        {
            (_probeTraceFor, _probeNothing, _probeNothingAt) = (s.StartTick, 0, float.NegativeInfinity);
            Array.Clear(_probeTrace);
        }
        if (s.NothingGave != _probeNothing)
            (_probeNothing, _probeNothingAt) = (s.NothingGave, _time);
        var bin = Math.Clamp((int)(s.Probe * _probeTrace.Length), 0, _probeTrace.Length - 1);
        _probeTrace[bin] += (s.Feel - _probeTrace[bin]) * 0.2f; // smoothed: the noise settles, a catch stands up
        var caught = _probeTrace[bin] > CatchFelt;

        // What to do now, from what the fingers know.
        var sure = s.SpotRead > ProbeSureRead;
        var (colour, line) =
            s.Moving != 0 && s.TooHeavyToSweep ? (Theme.Bad, "Too heavy to sweep! Let go of F before you move.")
            : _time - _probeNothingAt < 2.5f && !s.Held ? (Theme.Dim, "Nothing gives there. Feel along for the catch.")
            : s.Pressure > s.High ? (Theme.Bad, "Past the green! Let go of F, quick, or it springs.")
            : s.Held && s.InBand ? (Theme.Good, Controls.Keys("In the green: ~Space~ to flick it out!"))
            : s.Held ? (Theme.Text, Controls.Keys("Pressing in... ~Space~ once the needle's in the green. Out of it, the trap goes off."))
            : caught && !sure ? (Theme.Warn, "It catches here. Hold still a moment: the surer your read, the wider the green.")
            : caught ? (Theme.Good, Controls.Keys("A good read. Hold ~F~ to press in, then ~Space~ while it's in the green."))
            : (Theme.Dim, UsingPointer ? "Sweep along the seam with the button up, and feel for a catch. Right-click when you're done." : "Sweep along the seam with F let go, and feel for a catch. The hinge end finishes.");
        TextC(colour, line);
        var pictureTop = ImGui.GetCursorScreenPos().Y;
        ProbeScene(s.Probe, s.Feel, s.Pressure);

        var dl = ImGui.GetWindowDrawList();
        // The feel trace over the seam: how hard it caught at each point the probe has been.
        var width = Gui.S(420);
        var traceSize = new float2(width, Gui.S(34));
        var tMin = ImGui.GetCursorScreenPos();
        var tMax = tMin + traceSize;
        ImGui.Dummy(in traceSize);
        dl.AddRectFilled(in tMin, in tMax, Theme.U8(Theme.Inset), 3f, ImDrawFlags.None);
        var catchY = tMax.Y - CatchFelt * traceSize.Y;
        dl.AddLine(new float2(tMin.X, catchY), new float2(tMax.X, catchY), Theme.U8(Theme.Warn, 0.35f), 1f);
        var step = (traceSize.X - 8) / _probeTrace.Length;
        for (var i = 0; i < _probeTrace.Length; i++)
        {
            var v = _probeTrace[i];
            if (v <= 0.01f)
                continue;
            var x0 = tMin.X + 4 + i * step;
            var top = new float2(x0, tMax.Y - 2 - v * (traceSize.Y - 4));
            var bottom = new float2(x0 + MathF.Max(1f, step - 1f), tMax.Y - 2);
            dl.AddRectFilled(in top, in bottom, Theme.U8(v > CatchFelt ? Theme.Warn : Theme.Dim, v > CatchFelt ? 0.95f : 0.55f), 0f, ImDrawFlags.None);
        }

        // The seam, with the probe on it.
        var size = Gui.S(420, 22);
        var min = ImGui.GetCursorScreenPos();
        var max = min + size;
        ImGui.Dummy(in size);
        dl.AddRectFilled(in min, in max, new ImColor8(92, 62, 36, 255), 3f, ImDrawFlags.None);
        var seamMin = new float2(min.X + 4, min.Y + size.Y * 0.5f - 1);
        var seamMax = new float2(max.X - 4, min.Y + size.Y * 0.5f + 1);
        dl.AddRectFilled(in seamMin, in seamMax, new ImColor8(30, 22, 16, 255), 0f, ImDrawFlags.None);
        var x = min.X + 4 + s.Probe * (size.X - 8);
        var probeMin = new float2(x - 2, tMin.Y);
        var probeMax = new float2(x + 2, max.Y + 3);
        dl.AddRectFilled(in probeMin, in probeMax, Theme.U8(caught ? Theme.Warn : Theme.Accent, 0.9f), 1f, ImDrawFlags.None);
        // The mouse is read across this seam, where the probe's drawn (not the box's picture above,
        // narrower, which made the same move go further one place than another), from the picture's
        // top down to here.
        TakesPointer(new float2(min.X + 4, pictureTop), new float2(max.X - 4, max.Y));
        TextC(Theme.Dim, "hasp");
        ImGui.SameLine(size.X - Gui.S(24), 0);
        TextC(Theme.Dim, "hinge");

        // The pressure gauge: light enough to sweep, the green that eases a spring out, and too much.
        var gSize = Gui.S(420, 22);
        var gMin = ImGui.GetCursorScreenPos();
        var gMax = gMin + gSize;
        ImGui.Dummy(in gSize);
        float At(float f) => gMin.X + f * gSize.X;
        dl.AddRectFilled(in gMin, in gMax, Theme.U8(Theme.Well), 3f, ImDrawFlags.None);
        Zone(dl, gMin.X, At(Sim.Tinkering.ProbeSession.LightTouch), gMin.Y, gMax.Y, Theme.Iron, 0.45f, "sweep");
        Zone(dl, At(s.Low), At(s.High), gMin.Y, gMax.Y, Theme.Good, 0.6f, "flick");
        Zone(dl, At(Sim.Tinkering.ProbeSession.SpringAbove), gMax.X, gMin.Y, gMax.Y, Theme.Bad, 0.6f, "springs");
        var light = At(Sim.Tinkering.ProbeSession.LightTouch);
        dl.AddLine(new float2(light, gMin.Y - Gui.S(3)), new float2(light, gMax.Y + Gui.S(3)), Theme.U8(Theme.Text, 0.8f), Gui.S(1.5f));
        var needle = At(s.Pressure);
        var needleColour = s.Pressure > s.High ? Theme.Bad : s.InBand ? Theme.Good : s.TooHeavyToSweep ? Theme.Warn : Theme.Text;
        dl.AddRectFilled(new float2(needle - Gui.S(2), gMin.Y - Gui.S(4)), new float2(needle + Gui.S(2), gMax.Y + Gui.S(4)), Theme.U8(needleColour), 1f, ImDrawFlags.None);
        // The read of the spot, while the probe's still: what sets how wide the green is.
        if (s.Moving == 0)
            Bar("Read", s.SpotRead, s.SpotRead > ProbeSureRead ? Theme.Good : Theme.Dim, s.SpotRead > ProbeSureRead ? "sure of the spot" : "hold still to read the spot");
        EndWork();
    }

    /// <summary>How hard the smoothed feel has to catch to count as a catch, on the trace and in the advice.</summary>
    private const float CatchFelt = 0.55f;

    /// <summary>A read of the spot this good is a sure one, in the advice (a catch's middle reads about 0.9).</summary>
    private const float ProbeSureRead = 0.75f;

    /// <summary>The probe's "nothing gave" count last seen, and when it last went up (game-side clock).</summary>
    private int _probeNothing;
    private float _probeNothingAt = float.NegativeInfinity;

    /// <summary>A labelled stretch of a gauge.</summary>
    private static void Zone(ImDrawListPtr dl, float x0, float x1, float y0, float y1, float4 colour, float alpha, string label)
    {
        dl.AddRectFilled(new float2(x0, y0), new float2(x1, y1), Theme.U8(colour, alpha), 2f, ImDrawFlags.None);
        var textSize = ImGui.CalcTextSize(label);
        if (textSize.X > x1 - x0 - 2)
            return;
        var at = new float2((x0 + x1 - textSize.X) * 0.5f, (y0 + y1 - textSize.Y) * 0.5f);
        dl.AddText(at + new float2(1, 1), new ImColor8(0, 0, 0, 200), label);
        dl.AddText(in at, Theme.U8(Theme.Text), label);
    }

    /// <summary>
    /// The work in hand's window, front and centre over everything else: a title plate, the keys as
    /// caps, then the game. Every mini-game opens it this way; pair with <see cref="EndWork"/>.
    /// </summary>
    private void BeginWork(string title, params (string Keys, string Does)[] hints) => BeginWork(title, true, hints);

    /// <summary>Set just before <see cref="BeginWork(string, ValueTuple{string, string}[])"/>: this frame's panel goes to the side, not the middle.</summary>
    private bool _workAside;

    /// <summary>
    /// <see cref="BeginWork(string, ValueTuple{string, string}[])"/>, saying whether the window takes
    /// the mouse's buttons (left as F, right as Space). While the mouse is in use the hints name it for those.
    /// </summary>
    private void BeginWork(string title, bool mouse, params (string Keys, string Does)[] hints)
    {
        // Front and centre, over everything else, every frame while it's in hand: the work comes first.
        // It isn't one of the HUD's columns; it's laid over them, in the middle of the screen below the
        // top bar, and drawn smaller if what it holds wouldn't fit there (a Steam Deck's 1280x800).
        var (lo, hi) = _hud.Area;
        var centre = (lo + hi) * 0.5f;
        float2? pivot = new float2(0.5f, 0.5f);
        if (_workAside)
        {
            // Work out in the world (a tree to fell): against the right edge, the middle of the screen left clear.
            (centre, pivot) = (new float2(hi.X, centre.Y), new float2(1f, 0.5f));
            _workAside = false;
        }
        else if (WorkPanelAt(lo, hi) is { } at)
            (centre, pivot) = (at, new float2(0.5f, 1f)); // leaned in: low and a little right, you framed above and left of it
        ImGui.SetNextWindowPos(in centre, ImGuiCond.Always, in pivot);
        var most = hi - lo;
        var none = float2.Zero;
        ImGui.SetNextWindowSizeConstraints(in none, in most, (ImGuiSizeCallback?)null, default);
        ImGui.SetNextWindowFocus();
        _workRoom = most.Y;
        var pad = ImGui.GetStyle().WindowPadding.X * 2;
        var wide = Math.Clamp((most.X - pad) / Gui.S(WorkHintsWidth), WorkFitLeast, 1f);
        Panel.Begin($"{title}###work", Hud.Flags | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoNav);
        // Too tall for the screen last frame: drawn smaller till it fits.
        Gui.PushScale(Math.Min(_workFit, wide));
        WorkWindowDrawn(mouse);
        var pointer = mouse && UsingPointer;
        Gui.Hints(Gui.S(WorkHintsWidth), [.. hints.Select(h => (pointer ? MouseKeys(h.Keys) : WorkKeys(h.Keys), h.Does))]);
        ImGui.Spacing();
    }

    /// <summary>How wide a work window's row of hints runs before it wraps: about as wide as its picture.</summary>
    private const float WorkHintsWidth = 440f;

    /// <summary>The smallest a work window is shrunk to fit (<see cref="_workFit"/>).</summary>
    private const float WorkFitLeast = 0.55f;

    /// <summary>The scale the work window's picture is drawn at, shrunk when it ran past the room it has (<see cref="EndWork"/>).</summary>
    private float _workFit = 1f;

    /// <summary>The height the work window may take, this frame.</summary>
    private float _workRoom = float.MaxValue;

    /// <summary>
    /// The work window's end: if what it held ran taller than the room it had, it's drawn that much
    /// smaller from the next frame; with room to spare again, it grows back.
    /// </summary>
    private void EndWork()
    {
        var tall = ImGui.GetCursorPosY() + ImGui.GetStyle().WindowPadding.Y;
        Gui.PopScale();
        Panel.End();
        if (tall <= 1f || _workRoom >= float.MaxValue)
            return;
        if (tall > _workRoom)
            _workFit = Math.Max(WorkFitLeast, _workFit * _workRoom * 0.97f / tall);
        else if (_workFit < 1f && tall < _workRoom * 0.85f)
            _workFit = Math.Min(1f, _workFit * _workRoom * 0.92f / tall);
    }

    /// <summary>
    /// A mini-game's hint keys as the player has them bound: the games name the default keys
    /// ("F", "A/D", "Space", "Esc", "1-6"), and this turns them into the bound caps for the device in use.
    /// "A/D|Mouse" names the mouse instead while it's working the game (<see cref="MouseKeys"/>).
    /// </summary>
    private static string WorkKeys(string keys) => keys switch
    {
        _ when keys.IndexOf('|') is var bar and >= 0 => WorkKeys(keys[..bar]), // "keys|mouse": the mouse's words while it's in use
        "F" => Controls.Glyph(Act.Use),
        "A" => Controls.Glyph(Act.WalkLeft),
        "D" => Controls.Glyph(Act.WalkRight),
        "A/D" => Controls.Pair(Act.WalkLeft, Act.WalkRight),
        "W" => Controls.Glyph(Act.WalkUp),
        "S" => Controls.Glyph(Act.WalkDown),
        "W/S" => Controls.Pair(Act.WalkUp, Act.WalkDown),
        "WASD" => Controls.Walk(),
        "Space" => Controls.Glyph(Act.WorkAct),
        "Esc" => Controls.Glyph(Act.Back),
        "F A D" => $"{Controls.Cue('F')} {Controls.Cue('A')} {Controls.Cue('D')}",
        _ when keys.Length == 3 && keys[1] == '-' && char.IsDigit(keys[0]) && char.IsDigit(keys[2]) => RuneKeys(keys[0] - '1', keys[2] - '1'),
        _ => keys,
    };

    /// <summary>
    /// Runes first..last as caps: "1-6" while they're the number keys, "D-pad" for four on the
    /// d-pad's four ways, else each as bound (runes with nothing on the device in use left out).
    /// </summary>
    private static string RuneKeys(int first, int last)
    {
        var pad = Controls.UsingPad;
        var caps = Act.Runes[first..(last + 1)]
            .Select(r => Controls.Map.Bindings(r).FirstOrDefault(b => b.IsPad == pad) is { Kind: not Narsil.Platform.BindingKind.None } b ? b.Glyph : "")
            .Where(c => c.Length > 0).ToList();
        if (caps.Count == 0)
            return "";
        if (caps.Count == last - first + 1 && caps.Select((c, i) => c == (first + i + 1).ToString()).All(x => x))
            return $"{caps[0]}-{caps[^1]}";
        var dpad = caps.FindIndex(c => c.StartsWith('D') && c.Length == 2);
        if (dpad >= 0 && caps.Skip(dpad).Take(4).Count(c => c.StartsWith('D') && c.Length == 2) == 4)
            caps.RemoveRange(dpad, 4);
        else
            dpad = -1;
        if (dpad >= 0)
            caps.Insert(dpad, "D-pad");
        return string.Join("/", caps);
    }

    /// <summary>A hint's keys while the mouse is working the game: F is the left button, Space the right.</summary>
    private static string MouseKeys(string keys) => keys switch
    {
        _ when keys.IndexOf('|') is var bar and >= 0 => keys[(bar + 1)..],
        "F" => "Click",
        "Space" => "Right-click",
        _ => WorkKeys(keys),
    };



    private void RunBots()
    {
        foreach (var bot in _bots)
            bot.Tick();
    }
}

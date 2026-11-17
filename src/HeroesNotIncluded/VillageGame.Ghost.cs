using Brutal.ImGuiApi;
using Narsil.Ui;
using Narsil.Rendering;
using HeroesNotIncluded.Sim.Afterlife;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Ecs;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

// Being dead in a round (docs/DESIGN.md, "Death"): your character's body lies where it fell and
// you're a ghost (Sim.Afterlife). You drift with the walk keys, see what's around you through
// your ghost's Perception, hear the living near you and talk with the other dead. Everything you
// learn of the town still comes through View.
public sealed partial class VillageGame
{
    private Entity _ghost = Entity.Null;

    /// <summary>Where your ghost is drawn and looked from: yours to move, the sim told as it changes.</summary>
    private float3 _ghostPos;

    private float _ghostSentAt, _diedAt = float.NegativeInfinity;
    private string? _lastWords;

    /// <summary>How fast a ghost drifts, cells a second.</summary>
    private const float GhostSpeed = 7f;

    /// <summary>How long "You have died" holds the top of the screen, seconds.</summary>
    private const float DiedBannerSeconds = 8f;

    /// <summary>You're dead and watching: a ghost of your own and no character.</summary>
    private bool Ghosting => !HasAvatar && !_ghost.IsNull && _sim.World.IsAlive(_ghost) && _sim.World.Has<Ghost>(_ghost);

    /// <summary>
    /// In a live round without a character: a ghost, or someone waiting to take up a trade. Never
    /// the overseer: no toolbar, no orders, no pausing for danger.
    /// </summary>
    private bool Spectating => _sim.Round.Live && !HasAvatar;

    /// <summary>Find your ghost, if you're dead (once a frame, after the ticks), and hold the camera on it.</summary>
    private void UpdateGhost()
    {
        NoticeSigns();
        MarkFrost(); // the living read it as well as the dead
        if (DieAt >= 0 && _time >= DieAt && HasAvatar && InRound)
            Issue(new DevKillCommand(_avatar)); // --die-at: once; the avatar is gone after
        ShowDevEmote();
        if (FestivalAt >= 0 && _time >= FestivalAt && _sim.Round.Phase == RoundPhase.Playing)
            Issue(new DevFestivalCommand(_avatar)); // --festival-at: once, you by the well; the round's in its festival after
        var was = _ghost;
        _ghost = HasAvatar ? Entity.Null : Ghosts.Of(_sim, LocalPlayer ?? Guid.Empty);
        if (_ghost.IsNull)
        {
            if (!was.IsNull && !HasAvatar)
                CameBack();
            return;
        }
        var at = _sim.World.Get<Position>(_ghost).Pos;
        var ridden = Possession.Ridden(_sim, _ghost);
        if (!ridden.IsNull)
            _ghostPos = _renderer.DrawPos(_sim, ridden, _sim.World.Get<Position>(ridden).Pos); // you go where it goes
        else if (!_wasRiding.IsNull)
            _ghostPos = at; // thrown out, or let go: from where the beast stood
        _wasRiding = ridden;
        if (was != _ghost)
        {
            // Just died (or loaded a game as a ghost): see where you fell, and how.
            _ghostPos = at;
            _diedAt = _time;
            _lastWords = View.OwnDeath() is { } death ? Ghosts.LastWords(death.Cause) : "You have died.";
            _inventoryOpen = false;
            _showBuild = false;
            _selected = Entity.Null;
        }
        if (!_cameraMode)
            _camera.Target = new float3(_ghostPos.X, _ghostPos.Y, _ghostPos.Z);
    }

    /// <summary>
    /// On a dead person's inspector: whether they can still be called back and for how long, and for
    /// an empath a button to begin (or why not yet: too far, too little mana). Freshness isn't hidden.
    /// </summary>
    private void InspectCallingBack(Entity body)
    {
        if (!_sim.World.Has<Death>(body))
            return;
        if (!Revival.Fresh(_sim, body))
        {
            TextC(Theme.Dim, _sim.World.Has<Revived>(body) ? "Already brought back once: the way won't open again." : "Gone cold: no calling can reach them now.");
            return;
        }
        var left = Math.Max(1, (int)MathF.Round(Revival.HoursLeft(_sim, body)));
        TextC(Theme.Arcane, $"An empath could still call them back: about {left} hours{(Revival.OnShrinePallet(_sim, body) ? " (on a shrine pallet)" : ", longer on a shrine pallet")}.");
        if (!HasAvatar || !_sim.World.Has<Mana>(_avatar))
            return;
        var why = Revival.CallProblem(_sim, _avatar, body);
        ImGui.BeginDisabled(why != null || Working);
        if (ImGui.Button("Call them back"u8, in _autoSize))
            Issue(new CallBackCommand(_avatar, body));
        ImGui.EndDisabled();
        GuideGlow(why == null && !Working);
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            Tip(why != null ? Cap(why) + "." : Controls.Keys("Weave the Calling back: its runes are shown as you weave. ~F~ lets it go, and then they must come to their body and answer."));
    }

    /// <summary>The beast you were in last frame: letting go puts your ghost where it stood.</summary>
    private Entity _wasRiding = Entity.Null;

    /// <summary>Frost words in sight, over where they're written, for the living and the dead alike.</summary>
    private void MarkFrost()
    {
        if (!_sim.Round.Live || (!HasAvatar && !Ghosting))
            return;
        foreach (var sign in View.Signs())
            if (sign.Kind == HauntKind.Frost && sign.Text.Length > 0)
                _renderer.Markers.Add(new(new float3(sign.Cell.X + 0.5f, sign.Cell.Y + 0.5f, sign.Cell.Z) + Rendering.WorldScale.Rise(0.6f), $"\u201c{sign.Text}\u201d", false));
    }

    private long _signsNoticed = -1;

    /// <summary>The living, in a round, are told of the signs they perceive, in words (as the sound and the breath show them).</summary>
    private void NoticeSigns()
    {
        if (_signsNoticed < 0 || !InRound || !HasAvatar)
        {
            _signsNoticed = _sim.Clock.Tick;
            return;
        }
        var newest = _signsNoticed;
        foreach (var sign in View.Signs())
            if (sign.Tick > _signsNoticed)
            {
                Notify(MessageKind.Info, sign.Words);
                newest = Math.Max(newest, sign.Tick);
            }
        _signsNoticed = newest;
        if (View.Dream() is { } dream && dream.WokeTick != _dreamNoticed)
        {
            Notify(MessageKind.Info, dream.Words);
            _dreamNoticed = dream.WokeTick;
        }
    }

    private long _dreamNoticed;

    /// <summary>Called back and answered: your character again, where your body lay, at the one speed.</summary>
    private void CameBack()
    {
        FindAvatar();
        if (!HasAvatar)
            return; // in co-op, RunCoop finds it once the sim has
        _speed = 1;
        var at = _sim.World.Get<Position>(_avatar).Pos;
        _camera.Target = at;
        _diedAt = float.NegativeInfinity;
    }

    /// <summary>
    /// The walk keys (or the stick) drift your ghost, relative to the camera; the use key takes you
    /// back to your body, or, there while you're being called, answers. The sim hears where you are
    /// as it changes, a step at a time.
    /// </summary>
    private void HandleGhostInput(float dt)
    {
        if (!Ghosting || _cameraMode || PauseHeld)
            return;
        var w = _sim.World;
        if (_ghostWords != null)
            return; // writing frost or a whisper: the keys are the text box's
        var ridden = Possession.Ridden(_sim, _ghost);
        if (!ridden.IsNull)
        {
            HandleRiding(ridden);
            return;
        }
        var there = w.Get<Position>(_ghost).Pos;
        if (Controls.Map.Pressed(Act.Use) && !ImGui.GetIO().WantTextInput)
        {
            if (View.MyBody() is { CanAnswer: true })
            {
                Issue(new AnswerCommand(_ghost));
                return;
            }
            Issue(new GhostMoveCommand(_ghost, 0, 0, 0, ToBody: true));
            if (w.Get<Ghost>(_ghost).Body is var body && w.IsAlive(body))
                _ghostPos = w.Get<Position>(body).Pos;
            _ghostSentAt = _time;
            return;
        }
        if (!ImGui.GetIO().WantTextInput)
        {
            foreach (var (act, kind) in HauntKeys)
                if (Controls.Map.Pressed(act))
                {
                    if (Hauntings.Problem(_sim, _ghost, kind) is { } why)
                        Notify(MessageKind.Info, $"You can't: {why}.");
                    else
                        Issue(new HauntCommand(_ghost, kind));
                }
            if (Controls.Map.Pressed(Act.GhostFrost))
                BeginGhostWords(Entity.Null);
            if (Controls.Map.Pressed(Act.GhostWhisper))
            {
                var sleeper = Dreams.NearestSleeper(_sim, _ghost);
                if (sleeper.IsNull)
                    Notify(MessageKind.Info, "You can't: there's nobody asleep near enough to whisper to.");
                else
                    BeginGhostWords(sleeper);
            }
            if (Controls.Map.Pressed(Act.GhostPossess))
            {
                var beast = Possession.Nearest(_sim, _ghost);
                if ((beast.IsNull ? "there's no beast near enough to get into" : Possession.Problem(_sim, _ghost, beast)) is { } why)
                    Notify(MessageKind.Info, $"You can't: {why}.");
                else
                {
                    Issue(new PossessCommand(_ghost, beast));
                    _rideWish = float2.Zero;
                    _rideAim = float.NaN;
                }
            }
        }
        var (x, y) = ImGui.GetIO().WantTextInput ? (0f, 0f) : WalkAxes();
        var right = _camera.Right;
        var ahead = _camera.Heading;
        var wish = new float2(right.X * x + ahead.X * y, right.Y * x + ahead.Y * y);
        var len = MathF.Sqrt(wish.X * wish.X + wish.Y * wish.Y);
        if (len > 1f)
            wish *= 1f / len;
        var map = _sim.Map;
        var z = _followLevel ? (int)there.Z : Math.Clamp(_renderer.CutawayZ, 0, map.Depth - 1);
        var next = new float2(_ghostPos.X + wish.X * GhostSpeed * dt, _ghostPos.Y + wish.Y * GhostSpeed * dt);
        // Never run ahead of where the sim has you by more than a step it would take.
        var lead = new float2(next.X - there.X, next.Y - there.Y);
        var ahead2 = MathF.Sqrt(lead.X * lead.X + lead.Y * lead.Y);
        if (ahead2 > Ghosts.MaxStep * 0.9f)
            next = new float2(there.X, there.Y) + lead * (Ghosts.MaxStep * 0.9f / ahead2);
        next = new float2(Math.Clamp(next.X, 0f, map.Width - 0.01f), Math.Clamp(next.Y, 0f, map.Height - 0.01f));
        _ghostPos = new float3(next.X, next.Y, _followLevel ? there.Z : z);
        var moved = MathF.Abs(next.X - there.X) + MathF.Abs(next.Y - there.Y) > 0.05f || z != (int)there.Z;
        if (moved && _time - _ghostSentAt >= (_coop != null ? 0.1f : 0f))
        {
            Issue(new GhostMoveCommand(_ghost, next.X, next.Y, z));
            _ghostSentAt = _time;
        }
    }

    /// <summary>The dead's keys (the number row) for the signs a ghost leaves where it is.</summary>
    private static readonly (string Act, HauntKind Kind)[] HauntKeys =
        [(Act.GhostChill, HauntKind.Chill), (Act.GhostGutter, HauntKind.Gutter), (Act.GhostKnock, HauntKind.Knock),
         (Act.GhostSpook, HauntKind.Spook), (Act.GhostBeckon, HauntKind.Beckon)];

    /// <summary>Steering a beast, as last told to the sim.</summary>
    private float2 _rideWish;
    private float _rideAim = float.NaN;

    /// <summary>
    /// In a beast (<see cref="Possession"/>): the walk keys steer it (relative to the camera), the
    /// mouse or right stick aims, Strike bites, Heavy bites hard, Dash springs aside, and the use
    /// key lets go. No prediction in co-op: a beast's yours only a while.
    /// </summary>
    private void HandleRiding(Entity beast)
    {
        if (ImGui.GetIO().WantTextInput)
            return;
        var map = Controls.Map;
        if (map.Pressed(Act.Use))
        {
            Issue(new ReleaseCommand(_ghost));
            return;
        }
        var (x, y) = WalkAxes();
        var right = _camera.Right;
        var ahead = _camera.Heading;
        var wish = new float2(right.X * x + ahead.X * y, right.Y * x + ahead.Y * y);
        var len = MathF.Sqrt(wish.X * wish.X + wish.Y * wish.Y);
        wish = len > 0.01f ? wish * (1f / len) : float2.Zero;
        var me = _sim.World.Get<Position>(beast).Pos;
        var aim = _rideAim;
        if (Controls.UsingPad)
        {
            var stick = Host.Gamepad.RightStick;
            aim = stick != float2.Zero ? MathF.Atan2(right.Y * stick.X + ahead.Y * stick.Y, right.X * stick.X + ahead.X * stick.Y)
                : wish != float2.Zero ? MathF.Atan2(wish.Y, wish.X) : aim;
        }
        else if (!ImGui.GetIO().WantCaptureMouse)
        {
            var at = _camera.ScreenToPlane(Host.Input.MousePosition, me.Z + 0.5f);
            if ((at.X - me.X) * (at.X - me.X) + (at.Y - me.Y) * (at.Y - me.Y) > 0.04f)
                aim = MathF.Atan2(at.Y - me.Y, at.X - me.X);
        }
        if (wish != _rideWish)
        {
            Issue(new RideMoveCommand(_ghost, wish.X, wish.Y));
            _rideWish = wish;
        }
        _rideAim = aim;
        var light = map.Pressed(Act.Strike);
        var heavy = map.Pressed(Act.Heavy);
        if ((light || heavy) && float.IsFinite(aim))
            Issue(new RideAttackCommand(_ghost, heavy, aim, Pressed: _sim.Clock.Tick));
        if (map.Pressed(Act.Dash))
            Issue(new RideDodgeCommand(_ghost, wish.X, wish.Y, _sim.Clock.Tick));
    }

    /// <summary>Writing frost (null sleeper) or a whisper for a sleeper: the text box is open while this isn't null.</summary>
    private ImInputString? _ghostWords;
    private Entity _ghostWordsFor = Entity.Null;
    private int _ghostWordsFrames;

    private void BeginGhostWords(Entity sleeper)
    {
        if (sleeper.IsNull && _sim.World.Get<Ghost>(_ghost).Presence < Hauntings.Cost(HauntKind.Frost) - 0.0001f)
        {
            Notify(MessageKind.Info, "You can't: frost takes all your presence; it gathers in time.");
            return;
        }
        _ghostWords = new ImInputString(80, ""u8);
        _ghostWordsFor = sleeper;
        _ghostWordsFrames = 0;
    }

    /// <summary>The little box for frost's words, or a whisper's: Enter writes, Esc or Leave it forgets.</summary>
    private void DrawGhostWords()
    {
        if (_ghostWords == null)
            return;
        if (!Ghosting)
        {
            _ghostWords = null;
            return;
        }
        var frost = _ghostWordsFor.IsNull;
        var screen = ImGui.GetIO().DisplaySize;
        var pos = new float2(screen.X * 0.5f, screen.Y * 0.7f);
        float2? middle = new float2(0.5f, 0.5f);
        ImGui.SetNextWindowPos(in pos, ImGuiCond.Appearing, in middle);
        Panel.Begin(frost ? "Write in frost###ghost-words" : "Whisper into a dream###ghost-words", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse);
        TextC(Theme.Dim, frost
            ? $"A few words where you are, for anyone to read till it melts (at most {Hauntings.FrostWords} words). It takes all your presence."
            : $"A few words for {_sim.World.Get<Creature>(_ghostWordsFor).Name} to wake remembering (at most {Dreams.MaxWords} words).");
        ImGui.SetNextItemWidth(Gui.S(320));
        if (++_ghostWordsFrames == 2)
            ImGui.SetKeyboardFocusHere(0);
        var done = ImGui.InputTextWithHint("##ghost-words"u8, frost ? "What the frost spells"u8 : "What they dream"u8, _ghostWords, ImGuiInputTextFlags.EnterReturnsTrue, null, IntPtr.Zero);
        OnScreenKeyboard.AfterTextField();
        done |= ImGui.Button(frost ? "Write it"u8 : "Whisper it"u8, in _autoSize);
        ImGui.SameLine(0, Gui.S(8));
        var leave = ImGui.Button("Leave it"u8, in _autoSize) || BackPressed();
        if (done)
        {
            var text = ImGuiText.Value(_ghostWords);
            var why = frost ? Hauntings.Problem(_sim, _ghost, HauntKind.Frost, text) : Dreams.Problem(_sim, _ghost, _ghostWordsFor, text);
            if (why != null)
                Notify(MessageKind.Info, $"You can't: {why}.");
            else
                Issue(frost ? new HauntCommand(_ghost, HauntKind.Frost, text) : new WhisperCommand(_ghost, _ghostWordsFor, text));
        }
        if (done || leave)
            _ghostWords = null;
        Panel.End();
    }

    /// <summary>Drawn with the world: a faint ring round each soul in sight who needs you, as far as your lingering reaches; brighter round the one you're by.</summary>
    private void DrawBusinessRings()
    {
        if (!Ghosting || !InRound)
            return;
        var ring = Host.Atlas.Get("ui/selection");
        foreach (var b in View.Business())
        {
            if (!_sim.World.TryGet<Position>(b.Who, out var pos))
                continue;
            var alpha = b.Progress != null ? (b.Paused ? 0.45f + 0.25f * MathF.Sin(_time * 6f) : 0.7f) : 0.3f;
            _renderer.Decal(ring, _renderer.DrawPos(_sim, b.Who, pos.Pos) + new float3(0, 0, 0.05f), b.Reach * 2f,
                Rgba.Pack(0.7f, 0.85f, 1f, alpha), Quad.Unlit | Quad.Always, translucent: true);
        }
    }

    /// <summary>A ghost's presence, in words: no numbers for the dead either.</summary>
    private static string PresenceWords(float p) =>
        p >= 0.95f ? "Your presence is strong." : p >= 0.5f ? "Your presence is gathering." : p >= 0.25f ? "Your presence is faint." : "You're barely there.";

    /// <summary>"You have died", and then, for as long as you're dead, what a ghost can do.</summary>
    private void DrawGhostBanner()
    {
        if (!Ghosting || !InRound)
            return;
        _hud.Next(Anchor.Top);
        Panel.Begin("##ghost", Hud.Flags | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoDecoration);
        _hud.Took();
        if (_time - _diedAt < DiedBannerSeconds)
        {
            using (Gui.DisplayFont(25f))
                TextC(Theme.Warn, _lastWords ?? "You have died.");
            TextC(Theme.Dim, "Your body lies where it fell. Until someone brings you back, you walk among the dead.");
        }
        var body = View.MyBody();
        if (body?.CallingFor is { } left)
        {
            // Someone's holding the way open: the one thing that matters now.
            TextC(Theme.Good, body.CanAnswer
                ? Controls.Keys($"You're being called back. ~F~ to answer ({left:F0}s).")
                : Controls.Keys($"You're being called back! Go to your body (~F~) and answer ({left:F0}s)."));
        }
        else if (body != null)
        {
            var where = body.Way.StartsWith("You're standing", StringComparison.Ordinal) ? "Your body lies here." : $"Your body: {char.ToLowerInvariant(body.Way[0])}{body.Way[1..]}";
            var who = body.Beside.Count switch
            {
                0 => "Nobody's with it.",
                1 => $"{body.Beside[0]} is with it.",
                _ => $"{string.Join(", ", body.Beside.Take(body.Beside.Count - 1))} and {body.Beside[^1]} are with it.",
            };
            var keeps = !body.Fresh ? " It's gone cold: no calling can reach it now." : body.OnPallet ? " It lies on a shrine pallet." : "";
            TextC(Theme.Dim, $"{where} {who}{keeps}");
            // Whether, and for how long, you can still be brought back: the way back shouldn't be a secret.
            if (body.Fresh)
                TextC(body.EmpathAlive ? Theme.Arcane : Theme.Warn, !body.EmpathAlive
                    ? "There's no empath left alive to call you back."
                    : $"An empath can call you back for about {Math.Max(1, (int)MathF.Round(body.HoursLeft))} more hours{(body.OnPallet ? "" : " (longer if you're laid on a shrine pallet)")}: they kneel at your body, then you come to it and answer.");
        }
        var riding = Possession.Ridden(_sim, _ghost);
        if (!riding.IsNull)
        {
            var label = _sim.World.Get<Creature>(riding).Def.Label;
            TextC(Theme.Arcane, $"You're in the {label}. {PresenceWords(_sim.World.Get<Ghost>(_ghost).Presence)} It drains while you stay.");
            TextC(Theme.Dim, Controls.Keys($"~WASD~ run · {Press(Act.Strike)} bite · {Press(Act.Heavy)} bite hard · {Controls.Glyph(Act.Dash)} spring aside · ~F~ let go"));
            Panel.End();
            return;
        }
        TextC(Theme.Dim, Controls.Keys($"Among the dead · ~WASD~ drift · ~F~ back to your body · {Controls.Glyph(Act.Heard)} speak with the dead"));
        // Unfinished business: who near you needs you, and how your lingering's going.
        var g = _sim.World.Get<Ghost>(_ghost);
        foreach (var b in View.Business().Take(3))
            TextC(b.Paused ? Theme.Warn : b.Progress != null ? Theme.Good : Theme.Dim, b.Progress is { } done
                ? $"Lingering: {b.Errand.Label} ({b.Name}) {new string('●', (int)(done * 5))}{new string('○', 5 - (int)(done * 5))}{(b.Paused ? " · paused: get back within the ring" : "")}"
                : $"Unfinished business: {b.Errand.Label} ({b.Name}). Stay within the ring round them a while.");
        if (!string.IsNullOrEmpty(g.LastDone))
            TextC(Theme.Dim, g.Done == 1 ? $"Done: {g.LastDone}" : $"Done: {g.LastDone} ({g.Done} in all)");
        var presence = g.Presence;
        TextC(Theme.Dim, $"{PresenceWords(presence)} {Controls.Glyph(Act.GhostChill)} a chill · {Controls.Glyph(Act.GhostGutter)} gutter a light · {Controls.Glyph(Act.GhostKnock)} a knock · {Controls.Glyph(Act.GhostFrost)} write in frost · {Controls.Glyph(Act.GhostWhisper)} whisper to a sleeper");
        TextC(Theme.Dim, $"The beasts: {Controls.Glyph(Act.GhostSpook)} startle them · {Controls.Glyph(Act.GhostBeckon)} beckon them · {Controls.Glyph(Act.GhostPossess)} get into one near you");
        Panel.End();
    }
}

using Brutal.ImGuiApi;
using Narsil.Ui;
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
        if (DieAt >= 0 && _time >= DieAt && HasAvatar && InRound)
            Issue(new DevKillCommand(_avatar)); // --die-at: once; the avatar is gone after
        if (FestivalAt >= 0 && _time >= FestivalAt && _sim.Round.Phase == RoundPhase.Playing)
            Issue(new DevFestivalCommand()); // --festival-at: once; the round's in its festival after
        var was = _ghost;
        _ghost = HasAvatar ? Entity.Null : Ghosts.Of(_sim, LocalPlayer ?? Guid.Empty);
        if (_ghost.IsNull)
        {
            if (!was.IsNull && !HasAvatar)
                CameBack();
            return;
        }
        var at = _sim.World.Get<Position>(_ghost).Pos;
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
        _camera.Target = new float3(_ghostPos.X, _ghostPos.Y, _ghostPos.Z);
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
    }

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
        if (!Ghosting)
            return;
        var w = _sim.World;
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
            foreach (var (act, kind) in HauntKeys)
                if (Controls.Map.Pressed(act))
                {
                    if (Hauntings.Problem(_sim, _ghost, kind) is { } why)
                        Notify(MessageKind.Info, $"You can't: {why}.");
                    else
                        Issue(new HauntCommand(_ghost, kind));
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

    /// <summary>The hotbar's keys, dead: the signs a ghost can leave.</summary>
    private static readonly (string Act, HauntKind Kind)[] HauntKeys =
        [(Act.Hotbar1, HauntKind.Chill), (Act.Hotbar2, HauntKind.Gutter), (Act.Hotbar3, HauntKind.Knock)];

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
        }
        TextC(Theme.Dim, Controls.Keys($"Among the dead · ~WASD~ drift · ~F~ back to your body · {Controls.Glyph(Act.Heard)} speak with the dead"));
        // Unfinished business: who near you needs you, and how your lingering's going.
        var g = _sim.World.Get<Ghost>(_ghost);
        foreach (var b in View.Business().Take(3))
            TextC(b.Progress is { } p ? Theme.Good : Theme.Dim, b.Progress is { } done
                ? $"Lingering: {b.Errand.Label} ({b.Name}) {new string('●', (int)(done * 5))}{new string('○', 5 - (int)(done * 5))}"
                : $"Unfinished business: {b.Errand.Label} ({b.Name}). Stay near them a while.");
        if (!string.IsNullOrEmpty(g.LastDone))
            TextC(Theme.Dim, g.Done == 1 ? $"Done: {g.LastDone}" : $"Done: {g.LastDone} ({g.Done} in all)");
        var presence = g.Presence;
        TextC(Theme.Dim, $"{PresenceWords(presence)} {Controls.Glyph(Act.Hotbar1)} a chill · {Controls.Glyph(Act.Hotbar2)} gutter a light · {Controls.Glyph(Act.Hotbar3)} a knock");
        Panel.End();
    }
}

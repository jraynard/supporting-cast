using Brutal.ImGuiApi;
using Narsil.Ui;
using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Core;
using Narsil.Platform;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Ecs;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

// A line over the hotbar while you're in a fight, saying how to fight: nothing else does, and the
// first raid shouldn't be where you find out F swings your sword. Raiders and predators are known
// for what they are on sight (their names are drawn hostile), so this only asks what you can see.
public sealed partial class VillageGame
{
    /// <summary>The guard the sim was last told of, and a Shift+F press spent on a heavy blow (so F's own swing waits for its release).</summary>
    private bool _guardSent, _heavyKeyHeld;

    /// <summary>
    /// The fight's keys (<see cref="Sim.Combat.Blows"/>): hold V (or the right button with a foe
    /// near) to guard, raised just as a blow lands to parry it; Space to dodge; Shift+F for a heavy
    /// blow at the foe beside you. Each is stamped with the tick it was pressed on here, so in
    /// co-op a parry counts from when you pressed it, not when it arrived.
    /// </summary>
    private void HandleCombatKeys(Input input)
    {
        var tick = _sim.Clock.Tick;
        var inFight = NearestFoe() != null;
        var free = FreeMoving;
        // R draws or sheathes your weapon (free): combat mode, or the mouse walks and acts as ever.
        if (free && input.WasKeyPressed(GlfwKey.R) && _tool == ToolKind.Select)
            Issue(new DrawWeaponCommand(_avatar, !WeaponDrawn));
        // Q (or V) holds a guard, free only with a weapon out; on the grid the right button does too,
        // in a fight. Free, the right button is the heavy blow.
        var guard = (!free || WeaponDrawn) && (input.IsKeyDown(GlfwKey.Q) || input.IsKeyDown(GlfwKey.V))
            || (!free && inFight && input.IsMouseDown(MouseButton.Right) && !ImGui.GetIO().WantCaptureMouse);
        if (guard != _guardSent)
        {
            Issue(new CombatInputCommand(_avatar, guard ? CombatInput.GuardUp : CombatInput.GuardDown, tick));
            _guardSent = guard;
        }
        if (input.WasKeyPressed(GlfwKey.Space))
        {
            if (free)
            {
                var (ax, ay, az) = Here();
                Issue(new DodgeCommand(_avatar, _wish.X, _wish.Y, tick, ax, ay, az));
                if (_coop != null)
                    _freePredictor.Dash(_sim, _avatar, _wish);
            }
            else
                Issue(new CombatInputCommand(_avatar, CombatInput.Dodge, tick));
        }
        var shift = input.IsKeyDown(GlfwKey.LeftShift) || input.IsKeyDown(GlfwKey.RightShift);
        if (!free && shift && input.WasKeyPressed(GlfwKey.F) && FoeBeside(_sim.World.Get<Position>(_avatar).Cell, _sim.World.Get<Avatar>(_avatar).Facing) is { Kind: AvatarActionKind.Hunt } strike)
        {
            Issue(new CombatInputCommand(_avatar, CombatInput.Heavy, tick, strike.Target));
            _heavyKeyHeld = true;
        }
    }

    /// <summary>A foe this close (cells) and in sight is a fight: far enough to take in an archer's stand-off.</summary>
    private const int FightHintRange = 9;

    private void DrawFightHint()
    {
        if (!HasAvatar || !(InRound || ArenaFoe != null) || !_sim.World.Get<Creature>(_avatar).Able || NearestFoe() is not { } foe)
            return;
        var w = _sim.World;
        var at = w.Get<Position>(_avatar).Cell;
        var beside = Sim.Ai.Pathfinder.Touches(at, w.Get<Position>(foe).Cell);
        var armed = w.TryGet<Equipment>(_avatar, out var eq) && w.IsAlive(eq.Weapon);
        _hud.Next(Anchor.Bottom);
        Panel.Begin("##fight-hint", Hud.Flags | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.NoInputs);
        _hud.Took();
        if (FreeMoving && !WeaponDrawn)
        {
            TextC(Theme.Warn, armed ? "Press R to draw your weapon." : "Press R to put up your fists.");
            ImGui.SameLine(0, Gui.S(10));
            TextC(Theme.Dim, "Then the mouse fights: aim with it, left click to strike, right click to swing heavy.");
            Panel.End();
            return;
        }
        if (FreeMoving)
        {
            TextC(Theme.Warn, armed ? "Left click strikes the way you aim, right click swings heavy." : "Fists only: left click to strike.");
            ImGui.SameLine(0, Gui.S(10));
            TextC(Theme.Dim, Sim.Combat.Blows.IsDragon(_sim, foe)
                ? "Hold Q to guard (it takes only some of it). Space dashes clear: late, against a dragon."
                : "Hold Q to guard, facing the blow; raise it as it lands to parry. Space dashes clear.");
            Panel.End();
            return;
        }
        if (!armed)
            TextC(Theme.Warn, "You've nothing to fight with: get a weapon from the muster rack, or get indoors.");
        else if (beside)
            TextC(Theme.Warn, $"F strikes at {w.Get<Creature>(foe).Name}, Shift+F swings heavy.");
        else
            TextC(Theme.Warn, "Close in, then F to strike.");
        ImGui.SameLine(0, Gui.S(10));
        var shoots = w.TryGet<Equipment>(foe, out var theirs) && w.IsAlive(theirs.Weapon) && w.Get<Thing>(theirs.Weapon).Def.Weapon is { Ranged: true };
        TextC(Theme.Dim, Sim.Combat.Blows.IsDragon(_sim, foe)
            ? "Its jaws and fire can't be parried: a guard (V) takes only some of it. Dodge (Space) late, just as the ring closes."
            : shoots
                ? "Arrows can't be parried: guard (V or the right button) or dodge (Space) as the ring closes."
                : "Hold V or the right button to guard: raise it as their ring closes to parry. Space dodges.");
        Panel.End();
    }

    /// <summary>Your character moves off the grid (<see cref="FreeMover"/>: the arena, and the village with the setting).</summary>
    private bool FreeMoving => HasAvatar && _sim.World.Has<FreeMover>(_avatar);

    /// <summary>Free, with your weapon drawn: combat mode, where the mouse fights.</summary>
    private bool WeaponDrawn => FreeMoving && _sim.World.Get<FreeMover>(_avatar).Drawn;

    /// <summary>Where the keys ask to go (on the map) and where the mouse points, as last told to the sim.</summary>
    private float2 _wish, _sentWish;
    private float _aim, _sentAim = float.NaN, _aimSentAt;

    /// <summary>How far the aim turns (radians) before the sim is told: what others see you face, and nothing more.</summary>
    private const float AimStep = 0.06f;

    /// <summary>Co-op: how often (seconds) your predicted position goes to the host while you move.</summary>
    private const float PositionEvery = 0.2f;

    /// <summary>Where you are on your screen, for a command (co-op's prediction); NaN with none, solo.</summary>
    private (float X, float Y, int Z) Here() =>
        _coop != null && _freePredictor.Active ? (_freePredictor.Pos.X, _freePredictor.Pos.Y, _freePredictor.Level) : (float.NaN, float.NaN, 0);

    /// <summary>
    /// Free movement's keys and mouse (<see cref="FreeMoveSystem"/>): WASD to move, relative to
    /// the camera; the mouse aims (your character faces it at once, here, before the sim hears).
    /// Changes are sent as they matter: the keys at once, the aim when it's turned a little.
    /// </summary>
    private void HandleFreeMove(Input input, float x, float y)
    {
        var w = _sim.World;
        var right = _camera.Right;
        var ahead = _camera.Heading;
        var wish = new float2(right.X * x + ahead.X * y, right.Y * x + ahead.Y * y);
        var len = MathF.Sqrt(wish.X * wish.X + wish.Y * wish.Y);
        _wish = len > 0.01f ? wish * (1f / len) : float2.Zero;
        if (!ImGui.GetIO().WantCaptureMouse && !Host.Input.MouseCaptured)
        {
            var me = w.Get<Position>(_avatar).Pos;
            var at = _camera.ScreenToPlane(input.MousePosition, me.Z + 0.5f);
            if ((at.X - me.X) * (at.X - me.X) + (at.Y - me.Y) * (at.Y - me.Y) > 0.04f)
                _aim = MathF.Atan2(at.Y - me.Y, at.X - me.X);
        }
        // Drawn, you face the mouse at once; sheathed, the way you walk (the sim's say).
        _renderer.LocalAim = WeaponDrawn ? (_avatar, _aim) : null;
        var turned = float.IsNaN(_sentAim) || MathF.Abs(MathF.IEEERemainder(_aim - _sentAim, MathF.Tau)) > AimStep;
        // In co-op, where you are goes too, now and then while you move, so the host keeps up with your screen.
        var drift = _coop != null && _freePredictor.Active && _time - _aimSentAt > PositionEvery;
        if (_wish != _sentWish || (turned && _time - _aimSentAt > 0.05f) || drift)
        {
            var (ax, ay, az) = Here();
            Issue(new FreeMoveCommand(_avatar, _wish.X, _wish.Y, _aim, ax, ay, az));
            (_sentWish, _sentAim, _aimSentAt) = (_wish, _aim, _time);
        }
    }

    /// <summary>
    /// Free movement's mouse, weapon drawn: the left button a quick blow the way you aim, the right a
    /// heavy one. True if it took the click; sheathed, clicks walk and act as ever.
    /// </summary>
    private bool HandleFreeMouse(Input input)
    {
        if (!WeaponDrawn || ImGui.GetIO().WantCaptureMouse)
            return false;
        var light = input.WasMousePressed(MouseButton.Left);
        var heavy = input.WasMousePressed(MouseButton.Right);
        if (light || heavy)
        {
            var (ax, ay, az) = Here();
            Issue(new AttackCommand(_avatar, heavy, _aim, ax, ay, az));
        }
        return light || heavy || input.IsMouseDown(MouseButton.Left) || input.IsMouseDown(MouseButton.Right);
    }

    /// <summary>The nearest raider or predator on its feet that you can see, within <see cref="FightHintRange"/>.</summary>
    private Entity? NearestFoe()
    {
        var w = _sim.World;
        var at = w.Get<Position>(_avatar).Cell;
        Entity? best = null;
        var bestDist = int.MaxValue;
        foreach (var e in View.Creatures())
        {
            if (w.Get<Creature>(e) is not { Dead: false, Downed: false } c || !Factions.Attacker(c.Faction))
                continue;
            var d = Sim.Maps.Map.Distance(at, w.Get<Position>(e).Cell);
            if (d <= FightHintRange && d < bestDist)
            {
                best = e;
                bestDist = d;
            }
        }
        return best;
    }
}

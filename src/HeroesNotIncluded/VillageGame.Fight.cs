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
        var guard = input.IsKeyDown(GlfwKey.V)
            || (inFight && input.IsMouseDown(MouseButton.Right) && !ImGui.GetIO().WantCaptureMouse);
        if (guard != _guardSent)
        {
            Issue(new CombatInputCommand(_avatar, guard ? CombatInput.GuardUp : CombatInput.GuardDown, tick));
            _guardSent = guard;
        }
        if (input.WasKeyPressed(GlfwKey.Space))
            Issue(new CombatInputCommand(_avatar, CombatInput.Dodge, tick));
        var shift = input.IsKeyDown(GlfwKey.LeftShift) || input.IsKeyDown(GlfwKey.RightShift);
        if (shift && input.WasKeyPressed(GlfwKey.F) && FoeBeside(_sim.World.Get<Position>(_avatar).Cell, _sim.World.Get<Avatar>(_avatar).Facing) is { Kind: AvatarActionKind.Hunt } strike)
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

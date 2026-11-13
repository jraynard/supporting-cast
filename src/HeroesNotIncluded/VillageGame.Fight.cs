using Brutal.ImGuiApi;
using Narsil.Ui;
using HeroesNotIncluded.Sim.Combat;
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
    /// <summary>A foe this close (cells) and in sight is a fight.</summary>
    private const int FightHintRange = 5;

    private void DrawFightHint()
    {
        if (!HasAvatar || !InRound || !_sim.World.Get<Creature>(_avatar).Able || NearestFoe() is not { } foe)
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
            TextC(Theme.Warn, $"Hold F to strike at {w.Get<Creature>(foe).Name}.");
        else
            TextC(Theme.Warn, "Close in and hold F to strike.");
        ImGui.SameLine(0, Gui.S(10));
        TextC(Theme.Dim, "They come at you one at a time. Step out of reach to catch your breath.");
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

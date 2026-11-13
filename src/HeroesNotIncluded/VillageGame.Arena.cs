using Brutal.ImGuiApi;
using Narsil.Platform;
using Narsil.Ui;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Ecs;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

// The practice ring (--arena): you, a weapon, and one foe a bout. A panel picks the next foe and
// weapon and keeps the tally; Enter (or its button) starts a new bout, both of you fresh. A dev tool,
// so it reads the world freely: there are no secrets in a ring with two people in it.
public sealed partial class VillageGame
{
    private string _arenaFoe = Arena.DefaultFoe;
    private string? _arenaWeapon;
    private int _arenaWins, _arenaLosses;

    /// <summary>The bout's foe, and whether this bout's end has been counted yet.</summary>
    private Entity _boutFoe = Entity.Null;
    private bool _boutOver;

    /// <summary>Lay out the ring, put you in it and start the first bout. Returns where to look.</summary>
    private int3 StartArena(DefDatabase defs)
    {
        _sim = Arena.NewSimulation(defs, seed);
        _sim.StrictJobs = false;
        var center = Arena.Setup(_sim);
        var (look, clothes) = HeroAppearance();
        var me = VillageRound.SpawnHero(_sim, new Hero(string.IsNullOrWhiteSpace(HeroName) ? "Arin" : HeroName, look, HeroTraits, clothes), center);
        _arenaFoe = ArenaFoe ?? Arena.DefaultFoe;
        _arenaWeapon = ArenaWeapon;
        // The prototype: free movement and blows by reach and timing, in the ring only for now.
        _sim.FreeMove.Enable(_sim);
        _boutFoe = Arena.Bout(_sim, me, _arenaFoe, _arenaWeapon ?? Arena.DefaultWeapon);
        if (_boutFoe.IsNull)
            Console.WriteLine($"warning: no raider kind called {_arenaFoe}; try one of {string.Join(", ", Arena.Foes(defs))}");
        return _sim.World.Get<Position>(me).Cell;
    }

    private void NewBout()
    {
        Issue(new ArenaBoutCommand(_avatar, _arenaFoe, _arenaWeapon));
        _boutFoe = Entity.Null; // found again once the command's landed
        _boutOver = false;
    }

    private void DrawArena()
    {
        if (ArenaFoe == null || !HasAvatar)
            return;
        var w = _sim.World;
        if (_boutFoe.IsNull || !w.IsAlive(_boutFoe))
            _boutFoe = _sim.Creatures().FirstOrDefault(e => w.Get<Creature>(e).Faction is Faction.Raider or Faction.Predator, Entity.Null);
        // Tally a bout once: whoever's down first.
        if (!_boutOver && !_boutFoe.IsNull && w.IsAlive(_boutFoe))
        {
            if (!w.Get<Creature>(_boutFoe).Able)
                (_boutOver, _arenaWins) = (true, _arenaWins + 1);
            else if (!w.Get<Creature>(_avatar).Able)
                (_boutOver, _arenaLosses) = (true, _arenaLosses + 1);
        }
        if (Host.Input.WasKeyPressed(GlfwKey.Enter) && !ImGui.GetIO().WantTextInput && _tool == ToolKind.Select)
            NewBout();

        _hud.Next(Anchor.TopRight, Gui.S(300));
        Panel.Begin("The practice ring###arena", Hud.Flags | ImGuiWindowFlags.AlwaysAutoResize);
        _hud.Took();
        Creature? foe = _boutFoe.IsNull ? null : w.Get<Creature>(_boutFoe);
        TextC(Theme.Parchment, foe is { } f ? $"{f.Name}, {KindLabel(_arenaFoe)}" : "Nobody in the ring.");
        if (foe is { } fc)
            TextC(fc.Dead ? Theme.Good : fc.Downed ? Theme.Good : Theme.Dim, fc.Dead ? "Dead." : fc.Downed ? "Down: the bout's yours." : StateOf(_boutFoe));
        if (!w.Get<Creature>(_avatar).Able)
            TextC(Theme.Bad, "You're down: the bout's theirs.");
        TextC(Theme.Dim, $"Bouts won {_arenaWins}, lost {_arenaLosses}");
        Gui.Rule();
        TextC(Theme.Dim, "Next foe");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##arena-foe"u8, KindLabel(_arenaFoe)))
        {
            foreach (var k in Arena.Foes(_sim.Defs))
                if (ImGui.Selectable(KindLabel(k), k == _arenaFoe))
                    _arenaFoe = k;
            ImGui.EndCombo();
        }
        var weapons = _sim.Defs.AllThings.Where(t => t.Weapon != null).ToList();
        var current = _arenaWeapon ?? (w.TryGet<Equipment>(_avatar, out var eq) && w.IsAlive(eq.Weapon) ? w.Get<Thing>(eq.Weapon).Def.Name : Arena.DefaultWeapon);
        TextC(Theme.Dim, "Your weapon");
        ImGui.SetNextItemWidth(-1);
        if (ImGui.BeginCombo("##arena-weapon"u8, Capitalized(_sim.Defs.Thing(current).Label)))
        {
            foreach (var t in weapons)
                if (ImGui.Selectable(Capitalized(t.Label), t.Name == current))
                    _arenaWeapon = t.Name;
            ImGui.EndCombo();
        }
        if (ImGui.Button("New bout (Enter)"u8))
            NewBout();
        Panel.End();
    }

    private string KindLabel(string kind) =>
        kind == Arena.Dragon ? "Dragon" : Capitalized(_sim.Defs.AllRaiderKinds.FirstOrDefault(k => k.Name == kind)?.Label ?? kind);

    /// <summary>How the foe's holding up, in a word: their pain and blood, as you'd see it across the ring.</summary>
    private string StateOf(Entity e) =>
        _sim.World.TryGet<Health>(e, out var h) && (h.Injuries.Count > 0 || h.Blood < 0.99f)
            ? h.Pain > 0.5f || h.Blood < 0.6f ? "Badly hurt." : "Hurt."
            : "Fresh.";
}

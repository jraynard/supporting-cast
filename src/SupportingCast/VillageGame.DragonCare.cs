using Brutal.ImGuiApi;
using Narsil.Platform;
using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Ecs;
using SupportingCast.Sim.Scenarios;
using SupportingCast.Sim.Systems;
using SupportingCast.Ui;

namespace SupportingCast;

/// <summary>
/// Adventure mode's dragon care (docs/plans/dragon-interactions.md): the care window F opens at
/// your dragon (or the toolbar's Tend), the bond section it shares with the inspector, and the
/// toolbar's Call and Stay. Every choice is a sim command.
/// </summary>
public sealed partial class VillageGame
{
    /// <summary>The dragon whose care window is open, or Entity.Null.</summary>
    private Entity _openCare = Entity.Null;

    /// <summary>The trick picked for the next lesson in the care window.</summary>
    private TrickDef? _lesson;

    private static readonly float4 Love = new(0.9f, 0.35f, 0.45f, 1f);

    private void DrawCareWindow()
    {
        if (_openCare.IsNull)
            return;
        var w = _sim.World;
        if (!HasAvatar || InCombat || !w.IsAlive(_openCare) || !w.Has<Dragon>(_openCare) || w.Get<Creature>(_openCare).Dead
            || w.Get<Dragon>(_openCare).Keeper != _avatar)
        {
            _openCare = Entity.Null;
            return;
        }
        var dragon = _openCare;
        var c = w.Get<Creature>(dragon);
        var stage = c.Stage(_sim.Clock.Tick);
        var near = AvatarSystem.InReach(w.Get<Position>(_avatar).Cell, _sim.LocationOf(dragon));
        var pos = ImGui.GetIO().DisplaySize * 0.5f + new float2(60, 0);
        float2? pivot = new float2(0f, 0.5f);
        ImGui.SetNextWindowPos(in pos, ImGuiCond.Appearing, in pivot);
        var open = true;
        ImGui.Begin($"{c.Name}###dragoncare", ref open, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings);
        DrawBond(dragon);
        ImGui.Separator();

        var held = w.Get<Carrying>(_avatar).Item;
        var food = !held.IsNull && w.IsAlive(held) ? w.Get<Thing>(held).Def : null;
        var care = Bonding.Peek(_sim, dragon);
        var busy = w.Get<Mind>(_avatar).Job is DragonCareJob;
        foreach (var def in _sim.Defs.AllDragonInteractions)
        {
            if (def.Kind is CareKind.Rest or CareKind.Gift)
                continue; // these happen another way (sleeping beside it, F with treasure in hand)
            if (!def.Suits(stage) && def.Stages.Count > 0)
                continue;
            ImGui.PushID(def.Name);
            var problem = Bonding.Problem(_sim, _avatar, dragon, def, food) ?? (!near && def.Ticks == 0 ? "walk over to it first" : null);
            ImGui.BeginDisabled(problem != null || busy);
            var icon = def.Answers != DragonWant.None && WorldRenderer.WantIcon(def.Answers) is { } i ? i : def.Kind == CareKind.Train ? "ui/icon_train" : "ui/icon_tend";
            var asked = def.Answers != DragonWant.None && care.Want == def.Answers;
            if (IconButton("care", icon, 36, asked))
            {
                Issue(new DragonCareCommand(_avatar, dragon, def, def.Kind == CareKind.Train ? _lesson : null));
                if (def.Ticks > 0)
                    Notify(MessageKind.Info, $"Hold F to {def.Label} (stay close).");
            }
            ImGui.EndDisabled();
            var novelty = Bonding.Novelty(_sim, dragon, def);
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                Tip($"{def.PromptFor(c.Name)}\n{def.Description}"
                    + (def.Ticks > 0 ? $"\nTakes about {def.Ticks / 60f:0} s of holding F." : "")
                    + (novelty < 1f ? $"\nIt's had this lately: counts {novelty * 100:0}% now." : "")
                    + (problem != null ? $"\nNot now: {problem}." : ""));
            ImGui.SameLine(0, 8);
            ImGui.BeginGroup();
            ImGui.Text(def.PromptFor(c.Name));
            if (asked)
                TextC(Good, $"It {Bonding.Describe(care.Want)}!");
            else if (problem != null)
                TextC(Dim, problem);
            else if (novelty < 1f)
                TextC(Dim, $"Had this lately ({novelty * 100:0}%)");
            else if (def.Kind == CareKind.Feed && Bonding.Favourite(_sim, dragon) is { } fav)
                TextC(Dim, $"Its favourite: {fav.Label}");
            ImGui.EndGroup();
            if (def.Kind == CareKind.Train && problem == null)
                DrawLessonPicker(dragon);
            ImGui.PopID();
        }

        ImGui.Separator();
        // Riding moved here from F: still R or the toolbar.
        var cantRide = RidingSystem.CantMount(_sim, _avatar, dragon);
        ImGui.BeginDisabled(cantRide != null);
        if (ImGui.Button($"Ride {c.Name}  [R]", in _autoSize))
        {
            Issue(new MountOrderCommand(_avatar, dragon));
            _openCare = Entity.Null;
        }
        ImGui.EndDisabled();
        if (cantRide != null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            Tip($"Not yet: {cantRide}.");
        if (Bonding.Knows(_sim, dragon, TrickEffect.Stay))
        {
            ImGui.SameLine(0, 8);
            if (ImGui.Button("Stay here"u8, in _autoSize))
                Issue(new DragonStayCommand(_avatar, dragon));
        }
        ImGui.End();
        if (!open)
            _openCare = Entity.Null;
    }

    /// <summary>Which trick the next lesson works on.</summary>
    private void DrawLessonPicker(Entity dragon)
    {
        var care = Bonding.Peek(_sim, dragon);
        var left = _sim.Defs.AllTricks.Where(t => !care.Knows(t)).ToList();
        if (left.Count == 0)
            return;
        if (_lesson == null || care.Knows(_lesson))
            _lesson = left[0];
        ImGui.SetNextItemWidth(220);
        if (ImGui.BeginCombo("##lesson"u8, $"{Cap(_lesson.Label)} ({care.Training.GetValueOrDefault(_lesson.Name):0.#}/{_lesson.Sessions})"))
        {
            foreach (var t in left)
            {
                if (ImGui.Selectable($"{Cap(t.Label)} ({care.Training.GetValueOrDefault(t.Name):0.#}/{t.Sessions})", t == _lesson))
                    _lesson = t;
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                    Tip(t.Description);
            }
            ImGui.EndCombo();
        }
    }

    /// <summary>
    /// The bond at a glance: trust level with a bar to the next (and what it brings), the Bond need,
    /// what it's asking for, a sulk, and the tricks it knows. Shared by the care window and the inspector.
    /// </summary>
    private void DrawBond(Entity dragon)
    {
        var w = _sim.World;
        var d = w.Get<Dragon>(dragon);
        var care = Bonding.Peek(_sim, dragon);
        var level = Bonding.Level(_sim, d.Affinity);
        var next = Bonding.NextLevel(_sim, d.Affinity);
        var from = level?.MinAffinity ?? 0f;
        var progress = next == null ? 1f : (d.Affinity - from) / Math.Max(0.01f, next.MinAffinity - from);
        Bar(next == null ? "Trust (as deep as it goes)" : $"Trust: next, {next.Label}", progress, Love, Cap(level?.Label ?? "?"));
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
            Tip(string.Join("\n", _sim.Defs.TrustLevels.Select(t =>
                $"{(d.Affinity >= t.MinAffinity ? "+" : "·")} {Cap(t.Label)}{(t.Unlocks.Length > 0 ? ": " + t.Unlocks : "")}"))
                + $"\nAffinity {d.Affinity:F2}. It does as asked about {DragonSystem.Obedience(_sim, dragon) * 100:F0}% of the time."
                + (Bonding.Imprinting(_sim, dragon) ? "\nStill imprinting: care counts double today." : ""));
        if (next != null && next.Unlocks.Length > 0)
            TextC(Dim, $"{Cap(next.Label)}: {next.Unlocks}");
        var bond = _sim.Defs.Need("Bond");
        Bar("Bond", w.Get<Needs>(dragon).Get(bond), new float4(0.95f, 0.6f, 0.7f, 1f));
        if (care.Sulking)
            TextC(Warn, "Sulking: won't work or be ridden until it's had some attention.");
        else if (care.Want != DragonWant.None)
            TextC(Good, $"It {Bonding.Describe(care.Want)}.");
        else if (Bonding.Imprinting(_sim, dragon))
            TextC(Good, "Imprinting: care counts double today.");
        var known = _sim.Defs.AllTricks.Where(care.Knows).Select(t => t.Label).ToList();
        if (known.Count > 0)
            TextC(Dim, $"Knows: {string.Join(", ", known)}");
    }

    /// <summary>The toolbar's care buttons: Tend (opens the window) and Call (Q), plus Stay once it knows it.</summary>
    private void DrawCareButtons(Entity dragon)
    {
        var w = _sim.World;
        var c = w.Get<Creature>(dragon);
        ImGui.SameLine(0, 6);
        if (AbilityButton("tend", "ui/icon_tend", _openCare == dragon, disabled: false, ""))
            _openCare = _openCare == dragon ? Entity.Null : dragon;
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
        {
            var care = Bonding.Peek(_sim, dragon);
            Tip($"Tend {c.Name}\nPet, feed, play, groom and train it (or F beside it)."
                + (care.Want != DragonWant.None ? $"\nIt {Bonding.Describe(care.Want)}." : ""));
        }
        ImGui.SameLine(0, 6);
        var cantCall = Bonding.CantCall(_sim, _avatar, dragon);
        if (AbilityButton("call", "ui/icon_call", false, disabled: cantCall != null, "Q"))
            Issue(new CallDragonCommand(_avatar, dragon));
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            Tip(cantCall != null ? $"Call {c.Name}  [Q]\nNot now: {cantCall}."
                : $"Call {c.Name}  [Q]\nWhistle it over."
                  + (Bonding.Knows(_sim, dragon, TrickEffect.Come) ? " It knows \"come\": it always does." : " It comes if it feels like it."));
        if (Bonding.Knows(_sim, dragon, TrickEffect.Stay))
        {
            ImGui.SameLine(0, 6);
            var staying = Bonding.Peek(_sim, dragon).StayAt != null;
            if (AbilityButton("stay", "ui/icon_stay", staying, disabled: false, ""))
                Issue(staying ? new CallDragonCommand(_avatar, dragon) : new DragonStayCommand(_avatar, dragon));
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip(staying ? $"{c.Name} is staying put. Click (or call it) to release it." : $"Stay\nTell {c.Name} to wait where it is until you call it.");
        }
    }

    /// <summary>Q calls your dragon.</summary>
    private void HandleCareKeys(Input input)
    {
        if (input.WasKeyPressed(GlfwKey.Q) && Companions.DragonOf(_sim, _avatar) is { IsNull: false } dragon)
            Issue(new CallDragonCommand(_avatar, dragon));
    }
}

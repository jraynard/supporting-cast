using Brutal.ImGuiApi;
using Brutal.Numerics;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Environment;
using SupportingCast.Sim.Systems;

namespace SupportingCast;

/// <summary>
/// Adventure mode's needs panel: your character's needs, mood, warmth and wounds at a glance in
/// the top-left corner, so you needn't select yourself and open the Needs tab. Click your name for
/// the full inspector.
/// </summary>
public sealed partial class VillageGame
{
    private const float NeedsBarWidth = 120f;

    private void DrawNeedsPanel()
    {
        if (!HasAvatar || !_sim.World.IsAlive(_avatar))
            return;
        var w = _sim.World;
        var c = w.Get<Creature>(_avatar);

        var pos = new float2(10, 34);
        float2? pivot = new float2(0f, 0f);
        ImGui.SetNextWindowPos(in pos, ImGuiCond.Always, in pivot);
        ImGui.SetNextWindowBgAlpha(0.75f);
        ImGui.Begin("##needs"u8, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav);

        // Name: click for the inspector (everything else lives there).
        if (ImGui.Selectable(c.Name, _selected == _avatar, ImGuiSelectableFlags.None))
            _selected = _selected == _avatar ? Sim.Ecs.Entity.Null : _avatar;
        if (_sim.World.TryGet<Trade>(_avatar, out var trade))
            TextC(Ui.Theme.Dim, Capitalized(trade.Def.Label));
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
            Tip("Click for your full inspector: mood, health, skills, gear.");

        // Needs.
        var needs = w.Get<Needs>(_avatar);
        foreach (var rate in c.Def.Needs)
        {
            var def = rate.NeedDef;
            var v = needs.Get(def);
            var color = v < def.UrgentBelow ? Bad : v < def.SeekBelow ? Warn : Good;
            HudBar(Capitalized(def.Label), v, color, $"{v * 100:F0}%");
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip(def.Name switch
                {
                    "Food" => v < def.UrgentBelow ? "Starving: eat now (pick Eat on some food), or you'll start to waste away."
                        : v < def.SeekBelow ? "Hungry: time to eat something." : "Fed.",
                    "Rest" => v < def.UrgentBelow ? "Exhausted: sleep (a bed with F, or N on the ground)."
                        : v < def.SeekBelow ? "Tired: find a bed soon." : "Rested.",
                    _ => $"{Capitalized(def.Label)}: {v * 100:F0}%",
                });
        }

        // Mood.
        if (w.TryGet<Mood>(_avatar, out var mood))
        {
            var trend = mood.Target > mood.Value + 0.01f ? " ↑" : mood.Target < mood.Value - 0.01f ? " ↓" : "";
            HudBar("Mood", mood.Value, MoodColor(mood.Value), $"{mood.Value * 100:F0}{trend}");
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip($"You feel {MoodRules.Describe(mood.Value)}, heading for {mood.Target * 100:F0}. Your inspector's Mood tab says why.");
        }

        // Warmth: what you feel (air plus a fire's warmth) against what's comfortable.
        var felt = Warmth.Felt(_sim, _avatar);
        var tol = c.Tolerance(_sim.Clock.Tick);
        var (warmColor, warmNote) =
            felt < tol.SafeMin ? (Bad, "freezing") :
            felt > tol.SafeMax ? (Bad, "overheating") :
            felt < tol.ComfortMin ? (Warn, "cold") :
            felt > tol.ComfortMax ? (Warn, "hot") : (Good, "comfortable");
        TextC(warmColor, $"Feels {felt:F0}°C, {warmNote}");
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
            Tip($"Comfortable from {tol.ComfortMin:F0} to {tol.ComfortMax:F0}°C; dangerous below {tol.SafeMin:F0} or above {tol.SafeMax:F0}°C."
                + "\nA lit fire within 3 tiles warms you; warmer clothes help.");

        // Wounds, only when there are any.
        if (w.TryGet<Health>(_avatar, out var h) && (h.Injuries.Count > 0 || h.Blood < 0.999f))
        {
            var bleed = h.BleedPerHour;
            if (h.Blood < 0.999f)
                HudBar("Blood", h.Blood, h.Blood < 0.6f ? Bad : Warn, $"{h.Blood * 100:F0}%");
            var parts = new List<string>(3);
            if (bleed > 0)
                parts.Add("bleeding");
            if (h.Pain > 0.05f)
                parts.Add($"pain {h.Pain * 100:F0}%");
            if (parts.Count == 0)
                parts.Add($"{h.Injuries.Count} wound{(h.Injuries.Count == 1 ? "" : "s")}");
            TextC(bleed > 0 || h.Pain > 0.4f ? Bad : Warn, $"Hurt: {string.Join(", ", parts)}");
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip(bleed > 0 ? "A bandage binds a wound and stops the bleeding (its item menu, on yourself)." : "Your inspector's Health tab lists the wounds.");
        }

        ImGui.End();
    }

    /// <summary>A short labelled bar for the HUD: narrower than the inspector's, label on the left.</summary>
    private static void HudBar(string label, float value, float4 color, string overlay)
    {
        ImGui.Text(label);
        ImGui.SameLine(52, -1);
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, in color);
        var size = (float2?)new float2(NeedsBarWidth, 0);
        ImGui.ProgressBar(Math.Clamp(value, 0, 1), in size, overlay);
        ImGui.PopStyleColor(1);
    }
}

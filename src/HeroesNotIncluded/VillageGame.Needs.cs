using Brutal.ImGuiApi;
using Narsil.Ui;
using Brutal.Numerics;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Environment;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded;

/// <summary>
/// Adventure mode's character card, top left: who you are (portrait, name, trade, purse) and your
/// needs, mood, warmth and wounds at a glance, so you needn't select yourself. Click the portrait
/// or name for the full inspector.
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

        _hud.Next(Anchor.TopLeft);
        Panel.Begin("##needs", Hud.Flags | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoNav);
        _hud.Took();

        // Who you are: portrait, name and trade, purse. Click for the inspector (everything else lives there).
        var dl = ImGui.GetWindowDrawList();
        var box = Gui.S(52, 52);
        var min = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##me"u8, in box, ImGuiButtonFlags.None);
        var hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.None);
        Gui.Trough(dl, min, min + box);
        DrawPortrait(dl, _avatar, min + Gui.S(3, 3), box - Gui.S(6, 6));
        if (hovered || _selected == _avatar)
            dl.AddRect(in min, min + box, Ui.Theme.U8(Ui.Theme.Accent), Gui.S(3), ImDrawFlags.None, Gui.S(1.5f));
        ImGui.SameLine(0, Gui.S(10));
        ImGui.BeginGroup();
        using (Gui.HeaderFont())
            ImGui.Text(c.Name);
        if (_sim.World.TryGet<Trade>(_avatar, out var trade))
            TextC(Ui.Theme.Dim, Capitalized(trade.Def.Label));
        if (_sim.Round.Phase != Sim.Round.RoundPhase.None)
        {
            if (Gui.TrySprite("ui/icon_coin", out var coin))
            {
                var at = ImGui.GetCursorScreenPos();
                var size = ImGui.GetTextLineHeight();
                Gui.Sprite(dl, coin, at, new float2(size, size));
                ImGui.Dummy(new float2(size, size));
                ImGui.SameLine(0, Gui.S(4));
            }
            TextC(Ui.Theme.Warn, $"{Sim.Shops.Coin.Count(_sim, _avatar)} silver");
        }
        ImGui.EndGroup();
        if (clicked || (ImGui.IsItemClicked(ImGuiMouseButton.Left)))
            _selected = _selected == _avatar ? Sim.Ecs.Entity.Null : _avatar;
        if (hovered)
            Tip("Click for your full inspector: mood, health, skills, gear.");
        Gui.Rule();

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
                    "Food" => v < def.UrgentBelow ? "Starving: eat now (pick Eat on some food, or F at the inn's serving hatch for a meal), or you'll start to waste away."
                        : v < def.SeekBelow ? "Hungry: time to eat something. The inn serves meals at its hatch (F there to order)." : "Fed.",
                    "Rest" => v < def.UrgentBelow ? "Exhausted: sleep (a bed with F, or N on the ground)."
                        : v < def.SeekBelow ? "Tired: find a bed soon." : "Rested.",
                    _ => $"{Capitalized(def.Label)}: {v * 100:F0}%",
                });
        }

        // Wind, in a fight or while it's coming back: what's left to swing, guard and dodge with.
        if (w.TryGet<Sim.Combat.Guard>(_avatar, out var guard) && (guard.Stamina < 0.999f || NearestFoe() != null))
        {
            HudBar("Wind", guard.Stamina, guard.Stamina < Sim.Combat.Blows.DodgeCost ? Bad : guard.Stamina < 0.5f ? Warn : Good,
                guard.Stamina < Sim.Combat.Blows.DodgeCost ? "spent" : guard.Stamina < 0.5f ? "winded" : "steady");
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip("Each swing, block and dodge takes breath; it comes back when you let your guard down. With none left, a heavy blow knocks your guard aside.");
        }

        // Mood.
        if (w.TryGet<Mood>(_avatar, out var mood))
        {
            var trend = mood.Target > mood.Value + 0.01f ? " ↑" : mood.Target < mood.Value - 0.01f ? " ↓" : "";
            // A word, not a number: the number means nothing until you've learned the scale.
            HudBar("Mood", mood.Value, MoodColor(mood.Value), MoodRules.Describe(mood.Value) + trend);
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip($"You feel {MoodRules.Describe(mood.Value)} ({mood.Value * 100:F0}), heading for {MoodRules.Describe(mood.Target)} ({mood.Target * 100:F0}). Your inspector's Mood tab says why.");
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

        Panel.End();
    }

    /// <summary>A short labelled bar for the HUD: narrower than the inspector's, label on the left.</summary>
    private static void HudBar(string label, float value, float4 color, string overlay) =>
        Gui.Bar(label, value, color, overlay, NeedsBarWidth, labelWidth: 52);
}

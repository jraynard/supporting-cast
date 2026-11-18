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

    private float _heatCheckIn;
    private bool _heatWarned;

    /// <summary>
    /// A word the moment the heat you feel turns dangerous (hours at the forge's mouth), since the
    /// needs panel's "overheating" is easy to miss with a mini-game open. Once, until you've cooled off.
    /// </summary>
    private void WarnOfHeat(float dt)
    {
        if ((_heatCheckIn -= dt) > 0 || !HasAvatar)
            return;
        _heatCheckIn = 0.5f;
        var felt = Warmth.Felt(_sim, _avatar);
        var tol = _sim.World.Get<Creature>(_avatar).Tolerance(_sim.Clock.Tick);
        if (!_heatWarned && felt > tol.SafeMax)
        {
            _heatWarned = true;
            Notify(MessageKind.Warning, $"It's {felt:F0}°C where you stand: step back from the heat before it does you harm.");
        }
        else if (felt < tol.SafeMax - 5)
            _heatWarned = false;
    }

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
        // The simple HUD keeps the card to who you are, and whatever needs you now; hovering it shows the rest.
        var all = !SimpleHud || hovered;
        var ruled = false;
        void Below()
        {
            if (!ruled)
                Gui.Rule();
            ruled = true;
        }

        // Collapsed: no bar for it, just what it means. You can still call for help, weakly.
        if (c.Downed && w.TryGet<Health>(_avatar, out var health))
        {
            Below();
            var bleeding = health.BleedPerHour > 0;
            TextC(Bad, health.Blood < 0.2f ? "You're slipping away." : bleeding ? "You've collapsed, bleeding out." : "You've collapsed.");
            TextC(Ui.Theme.Dim, bleeding
                ? $"Call for help ({Controls.Glyph(Act.Heard)}, /s to shout): someone has to bind you, or carry you to a bed."
                : $"Call for help ({Controls.Glyph(Act.Heard)}, /s to shout), or lie still until you can stand.");
            if (w.Has<Sim.Components.Mana>(_avatar))
                TextC(Ui.Theme.Dim, $"Your gift still answers, though the beat's harder to keep: weave a mending on yourself ({Controls.Glyph(Act.Gift)}, the gift).");
            ruled = false;
        }

        // Needs.
        var needs = w.Get<Needs>(_avatar);
        foreach (var rate in c.Def.Needs)
        {
            var def = rate.NeedDef;
            var v = needs.Get(def);
            if (!all && v >= def.SeekBelow)
                continue;
            Below();
            var color = v < def.UrgentBelow ? Bad : v < def.SeekBelow ? Warn : Good;
            HudBar(Capitalized(def.Label), v, color, $"{v * 100:F0}%");
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip(def.Name switch
                {
                    "Food" => v < def.UrgentBelow ? Controls.Keys("Starving: eat now (pick Eat on some food, ~F~ at the bread crock at home, or ~F~ at the inn's serving hatch for a meal), or you'll start to waste away.")
                        : v < def.SeekBelow ? Controls.Keys("Hungry: time to eat something. There's bread in the crock at home (~F~ there), and the inn serves hot meals at its hatch (~F~ there to order). The tag points the way.") : "Fed.",
                    "Rest" => v < def.UrgentBelow ? Controls.Keys("Exhausted: sleep (a bed with ~F~, or ~N~ on the ground).")
                        : v < def.SeekBelow ? "Tired: find a bed soon." : "Rested.",
                    _ => $"{Capitalized(def.Label)}: {v * 100:F0}%",
                });
        }

        // Wind, in a fight or while it's coming back: what's left to swing, guard and dodge with.
        if (w.TryGet<Sim.Combat.Guard>(_avatar, out var guard) && (guard.Stamina < 0.999f || NearestFoe() != null))
        {
            Below();
            HudBar("Wind", guard.Stamina, guard.Stamina < Sim.Combat.Blows.DodgeCost ? Bad : guard.Stamina < 0.5f ? Warn : Good,
                guard.Stamina < Sim.Combat.Blows.DodgeCost ? "spent" : guard.Stamina < 0.5f ? "winded" : "steady");
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip("Each swing, block and dodge takes breath; it comes back when you let your guard down. With none left, a heavy blow knocks your guard aside.");
        }

        // Mood.
        if (w.TryGet<Mood>(_avatar, out var mood) && (all || MoodColor(mood.Value) != Good))
        {
            Below();
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
        if (all || warmColor != Good)
        {
            Below();
            TextC(warmColor, $"Feels {felt:F0}°C, {warmNote}");
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip($"Comfortable from {tol.ComfortMin:F0} to {tol.ComfortMax:F0}°C; dangerous below {tol.SafeMin:F0} or above {tol.SafeMax:F0}°C."
                    + "\nA lit fire within 3 tiles warms you; warmer clothes help.");
        }
        // Drink in you: you feel it (your own head, through Perception).
        if (View.OwnDrink is >= 0.08f and var drunk)
        {
            Below();
            var how = Sim.Hospitality.DrinkingSystem.Looks(drunk) ?? "a little tipsy";
            TextC(drunk >= Sim.Hospitality.DrinkingSystem.ClumsyAt ? Bad : Warn, $"You're {how}");
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip("It wears off over a few hours. Too much and you'll sleep it off where you stand.");
        }

        // Wounds, only when there are any.
        if (w.TryGet<Health>(_avatar, out var h) && (h.Injuries.Count > 0 || h.Blood < 0.999f))
        {
            Below();
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
            var shrineHelps = !Sim.Healing.Empathy.Gifted(_sim, _avatar) && Sim.Healing.LayOnHands.Hurt(_sim, _avatar);
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip((bleed > 0 ? "A bandage binds a wound and stops the bleeding (its item menu, on yourself)." : "Your inspector's Health tab lists the wounds.")
                    + (shrineHelps ? $"\nA bound wound takes days to close. The empath can draw it out: {Controls.Glyph(Act.Use)} at a pallet in their shrine asks." : ""));
            // Where to go with it: a bandage only stops the bleeding.
            if (shrineHelps)
                TextC(Ui.Theme.Dim, _sim.Shops.OpenOrderOf(_avatar, Sim.Defs.ServiceKind.Healing) != null ? "Waiting on the empath" : "The empath's shrine can mend it");
        }

        Panel.End();
    }

    /// <summary>A short labelled bar for the HUD: narrower than the inspector's, label on the left.</summary>
    private static void HudBar(string label, float value, float4 color, string overlay) =>
        Gui.Bar(label, value, color, overlay, NeedsBarWidth, labelWidth: 52);
}

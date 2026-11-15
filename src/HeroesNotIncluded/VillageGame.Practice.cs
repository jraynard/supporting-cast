using Brutal.ImGuiApi;
using Narsil.Ui;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

// The practice yard (from the title menu, or --practice TRADE): Millbrook with no season to it.
// A panel lists everything the trade's tips say, and sets up what its games work on: customers
// when you want them (brought in the way the trade needs them), the workplace restocked, a patient
// on a pallet for the healer. Every setup goes through PracticeCommand, which does nothing outside
// the yard.
public sealed partial class VillageGame
{
    private void DrawPractice()
    {
        if (!_sim.Round.Practice || !HasAvatar || !_sim.World.TryGet<Trade>(_avatar, out var trade))
            return;
        var def = trade.Def;
        _hud.Next(Anchor.TopLeft, ScreenFit.Width(320, 0.3f));
        Panel.Begin("The practice yard###practice", Hud.Flags | ImGuiWindowFlags.AlwaysAutoResize);
        _hud.Took();

        if (ImGui.Button("Send customers"u8))
            Issue(new PracticeCommand(_avatar, PracticeAction.SendCustomers));
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
            Tip(CustomersFor(def.Name));
        ImGui.SameLine(0, Gui.S(6));
        if (ImGui.Button("Restock"u8))
            Issue(new PracticeCommand(_avatar, PracticeAction.Restock));
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
            Tip(def.WorkplaceDef is { } work ? $"Fill the {work.Label} with what it started with." : "Nothing to restock.");
        ImGui.SameLine(0, Gui.S(6));
        if (ImGui.Button("Freshen up"u8))
            Issue(new PracticeCommand(_avatar, PracticeAction.Freshen));
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
            Tip("Your wounds gone, fed, rested" + (_sim.World.Has<Mana>(_avatar) ? ", and your mana full." : "."));

        if (def.Name == "Healer")
        {
            TextC(Theme.Dim, "Bring in a patient:");
            var first = true;
            foreach (var kind in Sim.Scenarios.Practice.PatientWounds)
            {
                if (!first)
                    ImGui.SameLine(0, Gui.S(6));
                first = false;
                if (ImGui.Button($"{Capitalized(DamageTypes.Wound(kind))}##patient-{kind}"))
                    Issue(new PracticeCommand(_avatar, PracticeAction.Patient, (int)kind));
            }
            if (ImGui.Button("Several wounds##patient-many"u8))
                Issue(new PracticeCommand(_avatar, PracticeAction.PatientCase, (int)PatientCase.Many));
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip("Bruised, burned and feverish: draw them all in one sitting (W/S to the next).");
            ImGui.SameLine(0, Gui.S(6));
            if (ImGui.Button("Festering##patient-festering"u8))
                Issue(new PracticeCommand(_avatar, PracticeAction.PatientCase, (int)PatientCase.Festering));
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip("A cut bound days ago, festering out of sight: only a long press finds it.");
        }

        DrawLessons(def);

        if (def.Tips.Count > 0)
        {
            Gui.Rule();
            ImGui.SetNextItemOpen(def.Lessons.Count == 0, ImGuiCond.Once); // folded away under lessons
            if (ImGui.CollapsingHeader("How the trade works"u8))
                foreach (var tip in def.Tips)
                    Wrapped("· " + Controls.Keys(tip));
        }
        Panel.End();
    }

    /// <summary>What "Send customers" brings, for the trade in hand.</summary>
    private static string CustomersFor(string trade) => trade switch
    {
        "Healer" => "A party rides in, hurt: they'll queue at the shrine.",
        "Blacksmith" => "A party rides in with their blades worn: they'll want them mended.",
        "Tinker" => "A party rides in with a locked strongbox and a curio.",
        _ => "A party rides in, hungry and thirsty.",
    };
}

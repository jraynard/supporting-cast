using Brutal.ImGuiApi;
using Narsil.Ui;
using SupportingCast.Sim;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Ecs;
using SupportingCast.Sim.Expeditions;
using SupportingCast.Sim.Round;
using SupportingCast.Sim.Shops;
using SupportingCast.Ui;

namespace SupportingCast;

// The quest board: what your character believes about each quest, and, with a party member in
// reach who wants work, the means to send them. All of it comes through View (Perception).
public sealed partial class VillageGame
{
    private void DrawQuestBoard()
    {
        if (_sim.Round.Phase != RoundPhase.Playing || !HasAvatar || !_showQuests)
            return;
        var quests = View.Quests().ToList();
        if (quests.Count == 0)
            return;
        BeginJournal("Quests###quests", ref _showQuests);

        // A party member in reach whose party wants work.
        var w = _sim.World;
        var member = Entity.Null;
        float? looks = null;
        if (w.IsAlive(_selected) && w.TryGet<Adventurer>(_selected, out var a) && Handing.InReach(_sim, _avatar, _selected)
            && ExpeditionSystem.QuestOrder(_sim, a.Party) is { } asked && View.Orders().Any(e => e.Id == asked.Id))
        {
            member = _selected;
            looks = View.PartyLooks(member);
            TextC(Theme.Accent, $"{Cap(a.PartyDef.Label)} want work. They look {ExpeditionSystem.StrengthWord(looks ?? 0)}.");
            ImGui.Separator();
        }
        else
            TextC(Theme.Dim, "Select a party member in reach who's asked for work to send them.");

        foreach (var q in quests)
        {
            ImGui.PushID(q.Id);
            TextC(q.Status == QuestStatus.Open ? Theme.Text : Theme.Dim, Cap(q.Label) + q.Status switch
            {
                QuestStatus.Taken => " (a party is out there)",
                QuestStatus.Cleared => " (cleared)",
                _ => "",
            });
            Wrapped(q.Description);
            TextC(Theme.Dim, $"Danger: {ExpeditionSystem.DangerWord(q.Danger)} ({ExpeditionSystem.SureWord(q.Confidence)})"
                + (q.Reward > 0 ? $". Pays about {q.Reward:F0} silver." : "."));
            if (!member.IsNull && q.Status == QuestStatus.Open)
            {
                if (ImGui.SmallButton("Send them here"u8))
                    Issue(new OfferQuestCommand(_avatar, member, q.Id));
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                    Tip("They'll go if it sounds right for them. Too easy and they're insulted; too hard and they won't. What's really there is another matter.");
            }
            ImGui.Separator();
            ImGui.PopID();
        }
        Panel.End();
    }
}

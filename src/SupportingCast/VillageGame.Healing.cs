using Brutal.ImGuiApi;
using SupportingCast.Sim;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Ecs;
using SupportingCast.Sim.Healing;
using SupportingCast.Sim.Round;
using SupportingCast.Sim.Work;
using SupportingCast.Ui;

namespace SupportingCast;

// The empath's gift: feeling a patient's wounds and drawing them across, mending what you carry,
// meditating. Wounds come through View.FeltWounds, which names what you've drawn only by its feel.
public sealed partial class VillageGame
{
    /// <summary>For an empath with hands on someone: the wounds they feel, each to draw across.</summary>
    private void DrawLayOnHands(Entity patient)
    {
        if (!HasAvatar || patient == _avatar || !Empathy.Gifted(_sim, _avatar) || View.FeltWounds(patient) is not { Count: > 0 } wounds)
            return;
        ImGui.Separator();
        TextC(Theme.Accent, "Under your hands you feel:");
        foreach (var wound in wounds)
        {
            ImGui.PushID(wound.Key);
            if (ImGui.SmallButton("Draw"u8))
                Issue(new LayOnHandsCommand(_avatar, patient, wound.Key));
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip("Hold F to draw it into yourself, A/D to keep your composure in the band. It lands on your own body.");
            ImGui.SameLine(0, 6);
            TextC(wound.Festering ? Theme.Bad : Theme.Text, $"{Cap(wound.Severity)} {wound.Feels} in the {wound.Part}{(wound.Festering ? ", festering" : "")}");
            ImGui.PopID();
        }
    }

    /// <summary>The gift's window: mana, meditating, and a mending for each wound you carry.</summary>
    private void DrawGift()
    {
        if (_sim.Round.Phase != RoundPhase.Playing || !HasAvatar || !_sim.World.TryGet<Mana>(_avatar, out var mana))
            return;
        SetNextWindow(new float2(Screen.X - 430, 410), new float2(420, 260));
        ImGui.Begin("The gift###gift"u8, ImGuiWindowFlags.NoCollapse);
        Bar("Mana", mana.Current / Math.Max(1f, mana.Max), Theme.Accent, $"{mana.Current:F0} / {mana.Max:F0}");
        var cantMeditate = MeditateSession.Problem(_sim, _avatar);
        ImGui.BeginDisabled(cantMeditate != null || Working);
        if (ImGui.Button("Meditate"u8, in _autoSize))
            Issue(new MeditateCommand(_avatar));
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            Tip(cantMeditate != null ? Cap(cantMeditate) + "." : "Press F at the top and bottom of each breath. Don't answer the flinches. Pain makes it hard.");
        ImGui.Separator();

        var carried = View.FeltWounds(_avatar) ?? [];
        if (carried.Count == 0)
            TextC(Theme.Dim, "You carry no wounds.");
        foreach (var wound in carried)
        {
            ImGui.PushID(wound.Key);
            TextC(wound.Festering ? Theme.Bad : wound.Absorbed ? Theme.Warn : Theme.Dim,
                $"{Cap(wound.Severity)} {wound.Feels} in your {wound.Part}{(wound.Festering ? ", festering" : "")}");
            foreach (var m in _sim.Defs.AllMendings)
            {
                ImGui.SameLine(0, 4);
                var why = CastSession.Problem(_sim, _avatar, wound.Key, m);
                ImGui.BeginDisabled(why != null || Working);
                if (ImGui.SmallButton(m.Label))
                    Issue(new CastMendingCommand(_avatar, wound.Key, m));
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    Tip(why != null ? Cap(why) + "." : $"{m.Description} {m.ManaCost:F0} mana. Play its runes as they reach the mark: A, D, F.");
            }
            ImGui.PopID();
        }
        ImGui.End();
    }

    private static readonly ImGuiWindowFlags WorkFlags = ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize
        | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav;

    private static void BeginWorkWindow(float y)
    {
        var screen = ImGui.GetIO().DisplaySize;
        var pos = new float2(screen.X * 0.5f, screen.Y * y);
        float2? pivot = new float2(0.5f, 0.5f);
        ImGui.SetNextWindowPos(in pos, ImGuiCond.Always, in pivot);
        ImGui.Begin("###work"u8, WorkFlags);
    }

    private void DrawDraw(DrawSession s)
    {
        BeginWorkWindow(0.68f);
        TextC(Theme.Accent, s.Title);
        TextC(Theme.Dim, "Hold F to draw the wound in, A/D to keep your composure in the band. Let go to pause. Esc to stop.");
        var dl = ImGui.GetWindowDrawList();
        var size = new float2(420, 30);
        var min = ImGui.GetCursorScreenPos();
        var max = min + size;
        ImGui.Dummy(in size);
        dl.AddRectFilled(in min, in max, new ImColor8(30, 26, 40, 255), 4f, ImDrawFlags.None);
        var bandMin = new float2(min.X + (0.5f - s.HalfWidth) * size.X, min.Y);
        var bandMax = new float2(min.X + (0.5f + s.HalfWidth) * size.X, max.Y);
        dl.AddRectFilled(in bandMin, in bandMax, Theme.U8(Theme.Good, 0.55f), 2f, ImDrawFlags.None);
        var x = min.X + s.Composure * size.X;
        var nMin = new float2(x - 2, min.Y - 3);
        var nMax = new float2(x + 2, max.Y + 3);
        dl.AddRectFilled(in nMin, in nMax, Theme.U8(s.Calm ? Theme.Accent : Theme.Bad), 1f, ImDrawFlags.None);
        Bar("Drawn", s.Drawn, Theme.Good);
        Bar("Strain", s.Strain, s.Strain > 0.6f ? Theme.Bad : Theme.Warn);
        ImGui.End();
    }

    private void DrawCast(CastSession s)
    {
        BeginWorkWindow(0.68f);
        TextC(Theme.Accent, s.Title);
        TextC(Theme.Dim, "Play each rune as it reaches the mark: L is A, R is D, F is F. Esc to stop (the mana stays).");
        var dl = ImGui.GetWindowDrawList();
        var size = new float2(420, 40);
        var min = ImGui.GetCursorScreenPos();
        var max = min + size;
        ImGui.Dummy(in size);
        dl.AddRectFilled(in min, in max, new ImColor8(30, 26, 40, 255), 4f, ImDrawFlags.None);
        const float pxPerStep = 2.2f;
        var markX = min.X + 60;
        var markMin = new float2(markX - s.WindowSteps * pxPerStep, min.Y);
        var markMax = new float2(markX + s.WindowSteps * pxPerStep, max.Y);
        dl.AddRectFilled(in markMin, in markMax, Theme.U8(Theme.Good, 0.4f), 2f, ImDrawFlags.None);
        var runes = s.Mending.Runes;
        for (var i = 0; i < runes.Length; i++)
        {
            var rx = markX + (s.DueOf(i) - s.StepsDone) * pxPerStep;
            if (rx < min.X || rx > max.X - 12)
                continue;
            var colour = i < s.Resolved ? (s.Hits[i] ? Theme.Good : Theme.Bad) : Theme.Text;
            var at = new float2(rx - 5, min.Y + 12);
            dl.AddText(in at, Theme.U8(colour), runes[i].ToString());
        }
        ImGui.End();
    }

    private void DrawMeditate(MeditateSession s)
    {
        BeginWorkWindow(0.62f);
        TextC(Theme.Accent, s.Title);
        TextC(Theme.Dim, "Press F at the top of each breath in and the bottom of each breath out. Don't answer the flinches. Esc to stop.");
        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        ImGui.Dummy(new float2(420, 150));
        var centre = new float2(origin.X + 210, origin.Y + 75);
        var radius = 20f + 50f * s.Fullness;
        var near = Math.Abs(s.StepsDone - s.NextTurn) <= s.WindowSteps;
        dl.AddCircleFilled(in centre, radius, Theme.U8(s.Flinching ? Theme.Bad : near ? Theme.Good : Theme.AccentSoft), 48);
        TextC(Theme.Dim, $"Calm {s.Calm}   Mana gathered {s.Gained:F0}   Breath {Math.Min(s.Turns / 2 + 1, MeditateSession.Breaths)} of {MeditateSession.Breaths}");
        ImGui.End();
    }
}

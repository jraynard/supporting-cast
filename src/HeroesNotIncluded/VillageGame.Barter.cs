using Brutal.ImGuiApi;
using Narsil.Ui;
using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Ecs;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

// Trading with another player (Sim.Shops.BarterSystem): F at them with empty hands asks, F at them
// (or the panel's button) says yes; then a table each side fills from their hands, pack and purse.
// It changes hands once both agree. Everything here is read through View.Barter; the sim tells
// both how it ended.
public sealed partial class VillageGame
{
    /// <summary>An ask, to or from you, at the top of the screen while it stands.</summary>
    private void DrawBarterAsk()
    {
        if (!HasAvatar || View.Barter() is not { Open: false } ask)
            return;
        _hud.Next(Anchor.Top);
        Panel.Begin("##barter-ask", Hud.Flags | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoDecoration);
        _hud.Took();
        if (ask.IAsked)
        {
            TextC(Theme.Dim, $"Waiting for {ask.PartnerName} to say yes to a trade.");
            ImGui.SameLine(0, Gui.S(10));
            if (ImGui.Button("Never mind"u8))
                Issue(new BarterLeaveCommand(_avatar));
        }
        else
        {
            TextC(Theme.Accent, $"{ask.PartnerName} would like to trade.");
            ImGui.SameLine(0, Gui.S(10));
            var problem = BarterSystem.Problem(_sim, _avatar, ask.Partner);
            ImGui.BeginDisabled(problem != null);
            if (ImGui.Button($"Trade [{Controls.Glyph(Act.Use)}]"))
                Issue(new BarterCommand(_avatar, ask.Partner));
            ImGui.EndDisabled();
            if (problem != null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                Tip($"Step closer: {problem}.");
            ImGui.SameLine();
            if (ImGui.Button("No thanks"u8))
                Issue(new BarterLeaveCommand(_avatar));
        }
        Panel.End();
    }

    /// <summary>
    /// The table, while a trade's open: what you've put out (click to take back), what's in your
    /// hands and pack to put out (click one, right click the stack), your silver, and theirs.
    /// </summary>
    private void DrawBarterWindow()
    {
        if (!HasAvatar || View.Barter() is not { Open: true } deal)
            return;
        PlaceBeside(2 * PackColumnWidth + Gui.S(60));
        var open = true;
        ScreenFit.Next();
        Panel.Begin($"Trading with {deal.PartnerName}###barter", ref open, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings);
        PadWindow("###barter", () => Issue(new BarterLeaveCommand(_avatar)));
        if (!BeginTwoColumns("##barter-cols"))
        {
            Panel.End();
            return;
        }
        ISimCommand? chosen = null; // issued after drawing: solo applies at once and may change the table under us

        // Your side.
        Theme.Header(Host.ImGui, "You give");
        DrawTable(deal.Mine, mine: true, ref chosen);
        DrawSilver(deal, ref chosen);
        ImGui.Spacing();
        TextC(Theme.Dim, "From your hands and pack:");
        DrawOfferable(deal, ref chosen);

        // Theirs.
        ImGui.TableSetColumnIndex(1);
        Theme.Header(Host.ImGui, $"{deal.PartnerName} gives");
        DrawTable(deal.Theirs, mine: false, ref chosen);
        if (deal.TheirSilver > 0)
            TextC(Theme.Text, $"{deal.TheirSilver} silver");
        else if (deal.Theirs.Count == 0)
            TextC(Theme.Dim, "Nothing yet.");
        ImGui.Spacing();
        TextC(deal.TheyAgreed ? Theme.Good : Theme.Dim, deal.TheyAgreed ? $"{deal.PartnerName} agrees." : $"{deal.PartnerName} hasn't agreed yet.");
        ImGui.EndTable();

        ImGui.Separator();
        var empty = deal.Mine.Count == 0 && deal.MySilver == 0 && deal.Theirs.Count == 0 && deal.TheirSilver == 0;
        ImGui.BeginDisabled(empty && !deal.IAgreed);
        if (ImGui.Button(deal.IAgreed ? "Take back my agreement"u8 : "Agree"u8, new float2(Gui.S(200), 0)))
            chosen ??= new BarterAgreeCommand(_avatar, !deal.IAgreed);
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            Tip(empty ? "Put something on the table first."
                : "Once you both agree, it all changes hands at once. If either of you changes the table, both agreements are undone.");
        ImGui.SameLine();
        if (ImGui.Button("Walk away"u8))
            chosen ??= new BarterLeaveCommand(_avatar);
        if (deal.IAgreed)
            TextC(Theme.Good, deal.TheyAgreed ? "Done." : $"You agree. Waiting on {deal.PartnerName}.");
        Panel.End();
        if (!open)
            chosen = new BarterLeaveCommand(_avatar);
        if (chosen != null)
            Issue(chosen);
    }

    /// <summary>A side of the table as tiles. Yours take back one on a click, the lot on a right click.</summary>
    private void DrawTable(IReadOnlyList<TableItem> table, bool mine, ref ISimCommand? chosen)
    {
        var gap = Gui.S(4);
        var tile = Gui.S(IconSize) + 2 * Gui.S(5);
        var across = Math.Max(1, (int)((ImGui.GetContentRegionAvail().X + gap) / (tile + gap)));
        for (var k = 0; k < table.Count; k++)
        {
            var o = table[k];
            if (!_sim.World.IsAlive(o.Item))
                continue;
            if (k % across != 0)
                ImGui.SameLine(0, gap);
            ImGui.PushID((mine ? 1 : 2) * 100000 + o.Item.Index);
            var box = DrawSlotArt(o.Item, o.Count > 1 ? o.Count.ToString() : "");
            var one = ImGui.InvisibleButton("##t"u8, in box, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.EnableNav);
            var all = ImGui.IsItemClicked(ImGuiMouseButton.Right) || (ImGui.IsItemFocused() && ImGui.IsKeyPressed(ImGuiKey.GamepadFaceUp, false));
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip($"{(o.Count > 1 ? $"{o.Count} " : "")}{o.Label}" + (mine ? $"\n{(Controls.UsingPad ? "A" : "Click")}: take one back. {(Controls.UsingPad ? "Y" : "Right click")}: take them all back." : ""));
            ImGui.PopID();
            if (mine && (one || all))
                chosen ??= new BarterOfferCommand(_avatar, o.Item, all ? 0 : o.Count - 1);
        }
        if (table.Count == 0 && mine)
            TextC(Theme.Dim, "Nothing yet.");
    }

    /// <summary>Your silver on the table: a step down or up at a time.</summary>
    private void DrawSilver(PerceivedBarter deal, ref ISimCommand? chosen)
    {
        var purse = Coin.Count(_sim, _avatar);
        if (purse == 0 && deal.MySilver == 0)
            return;
        ImGui.AlignTextToFramePadding();
        TextC(Theme.Text, $"{deal.MySilver} silver");
        foreach (var step in new[] { -5, -1, 1, 5 })
        {
            ImGui.SameLine(0, Gui.S(4));
            var to = Math.Clamp(deal.MySilver + step, 0, purse);
            ImGui.BeginDisabled(to == deal.MySilver);
            if (ImGui.SmallButton(step > 0 ? $"+{step}" : $"{step}"))
                chosen ??= new BarterSilverCommand(_avatar, to);
            ImGui.EndDisabled();
        }
        TextC(Theme.Dim, $"(you have {purse})");
    }

    /// <summary>What you could put out: loose in your hands or pack, less what's out already.</summary>
    private void DrawOfferable(PerceivedBarter deal, ref ISimCommand? chosen)
    {
        var w = _sim.World;
        var gap = Gui.S(4);
        var tile = Gui.S(IconSize) + 2 * Gui.S(5);
        var across = Math.Max(1, (int)((ImGui.GetContentRegionAvail().X + gap) / (tile + gap)));
        var shown = 0;
        foreach (var item in OnMe())
        {
            if (!BarterSystem.Offerable(_sim, _avatar, item))
                continue;
            var have = w.Get<Stack>(item).Count;
            var out_ = deal.Mine.FirstOrDefault(o => o.Item == item).Count;
            if (out_ >= have)
                continue;
            if (shown++ % across != 0)
                ImGui.SameLine(0, gap);
            ImGui.PushID(item.Index);
            var box = DrawSlotArt(item, out_ > 0 ? $"{have - out_}" : "");
            var one = ImGui.InvisibleButton("##o"u8, in box, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.EnableNav);
            var all = ImGui.IsItemClicked(ImGuiMouseButton.Right) || (ImGui.IsItemFocused() && ImGui.IsKeyPressed(ImGuiKey.GamepadFaceUp, false));
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip($"{ItemLine(item)}\n{(Controls.UsingPad ? "A" : "Click")}: put one on the table." + (have - out_ > 1 ? $" {(Controls.UsingPad ? "Y" : "Right click")}: all of them." : ""));
            ImGui.PopID();
            if (one || all)
                chosen ??= new BarterOfferCommand(_avatar, item, all ? have : out_ + 1);
        }
        if (shown == 0)
            TextC(Theme.Dim, "Nothing else to offer.");
    }
}

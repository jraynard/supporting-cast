using Brutal.ImGuiApi;
using Narsil.Ui;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Work;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

// The striker at the anvil (Sim.Work.Pairs): the anvil as the smith sees it, the bar glowing as hot as it
// is, the smith's hammer where the smith's holding it and the striker's own sledge across from it; above,
// what to do now (heave it up, the smith's winding up, here it comes, Now!), read from both sessions.
// Everything here is read; nothing touches the sim.
public sealed partial class VillageGame
{
    /// <summary>Steps a word on the last blow stays up.</summary>
    private const int SledgeWordSteps = 60;

    private void DrawSledge(SledgeSession s)
    {
        BeginWork(s.Title, ("F|Hold the button", "heave the sledge up and hold it; let go on the smith's tap"), ("Esc", "step away"));
        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(420, 190);
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(in size);
        var max = min + size;
        Gui.Frame(dl, min, max, UiTheme.Current.Frames.Inset, Theme.Well);
        var glowTop = Theme.U8(Theme.Accent, 0f);
        var glowBottom = Theme.U8(Theme.Accent, 0.22f);
        dl.AddRectFilledMultiColor(new float2(min.X + Gui.S(4), min.Y + size.Y * 0.45f), max - Gui.S(4, 4), glowTop, glowTop, glowBottom, glowBottom);

        var h = _sim.World.TryGet<WorkSession>(s.Partner, out var lead) ? lead as HammerSession : null;
        var smith = h != null && _sim.World.TryGet<Creature>(s.Partner, out var c) ? c.Name : s.LeadName;

        // The anvil, the bar on it, the smith's hammer (right) and your sledge (left).
        var px = MathF.Max(1f, MathF.Round(3f * Gui.Scale));
        var lastBlow = s.LastStep >= 0 && s.StepsDone - s.LastStep < 8;
        var jolt = lastBlow && s.LastWas == SledgeResult.OnCall ? new float2(MathF.Sin(_time * 90f), MathF.Cos(_time * 70f)) * Gui.S(2f) : float2.Zero;
        if (Gui.TrySprite("ui/work/anvil", out var anvil))
        {
            var a = new float2(anvil.PixelSize.X, anvil.PixelSize.Y) * px;
            var aMin = new float2(min.X + size.X * 0.5f - a.X * 0.55f, max.Y - a.Y - Gui.S(6)) + jolt;
            Gui.Image(dl, anvil, aMin, aMin + a, Gui.White);
            var face = new float2(aMin.X + 15 * px, aMin.Y + 4 * px);
            if (Gui.TrySprite("ui/work/billet", out var billet))
            {
                var bh = billet.PixelSize.Y * px;
                var strikeX = face.X + 2 * px + billet.PixelSize.X * px * 0.6f;
                var halfLen = h != null ? (h.SpotReach + h.CentreWidth + 0.1f) * HammerShift : HammerShift * 0.6f;
                var b = new float2(halfLen * 2f, bh);
                var bMin = new float2(strikeX - halfLen, face.Y - bh + px);
                var hot = h != null ? GlowColour(h.Temp) : new float4(0.35f, 0.33f, 0.32f, 1f);
                if (h != null)
                    dl.AddEllipseFilled(bMin + b * 0.5f, new float2(b.X * 0.62f, b.X * 0.3f), Gui.U8(hot, 0.25f * GlowStrength(h.Temp)), 0f, 32);
                DrawBar(dl, bMin, b, px, hot);
                var strike = new float2(strikeX + (h?.Spot ?? 0f) * HammerShift, bMin.Y);
                // Where the smith's tap will land: where the sledge must fall.
                dl.AddTriangleFilled(new float2(strike.X - Gui.S(6), bMin.Y - Gui.S(12)), new float2(strike.X + Gui.S(6), bMin.Y - Gui.S(12)), new float2(strike.X, bMin.Y - Gui.S(4)),
                    Theme.U8(s.CallOpen ? Theme.Good : Theme.Accent, 0.8f));
                if (h != null)
                    DrawHammerSwing(dl, h.Raise, new float2(strikeX + h.Offset * HammerShift, strike.Y), px, Gui.U8(new float4(1f, 1f, 1f, 0.85f)));
                DrawSledgeSwing(dl, s.Raise, strike, px, s.Tiring ? Theme.U8(Theme.Bad) : Gui.White);
                if (lastBlow && s.LastWas == SledgeResult.OnCall)
                    dl.AddCircleFilled(strike, Gui.S(10) * (1f - (s.StepsDone - s.LastStep) / 8f), new ImColor8(255, 240, 200, 200), 16);
            }
        }

        // The heave, up the left edge: the full mark, the sledge now, red once the arms tire.
        var g0 = new float2(min.X + Gui.S(14), min.Y + Gui.S(12));
        var g1 = new float2(min.X + Gui.S(24), max.Y - Gui.S(12));
        Gui.Trough(dl, g0, g1);
        float Y(float f) => g1.Y - (g1.Y - g0.Y) * Math.Clamp(f, 0f, 1f);
        dl.AddRectFilled(new float2(g0.X, Y(1f)), new float2(g1.X, Y(SledgeSession.FullHeave)), Theme.U8(Theme.Good, 0.45f), 0f, ImDrawFlags.None);
        var heaveColour = s.Tiring ? Theme.Bad : s.Raise >= SledgeSession.FullHeave ? Theme.Good : Theme.Text;
        var now = Y(s.Raise);
        dl.AddRectFilled(new float2(g0.X - Gui.S(4), now - Gui.S(3)), new float2(g1.X + Gui.S(4), now + Gui.S(3)), Theme.U8(heaveColour), Gui.S(1), ImDrawFlags.None);

        // What to do now, big across the top: the call, the smith's swing coming, or the heave.
        var centre = new float2((min.X + max.X) * 0.5f, min.Y + Gui.S(26));
        string word;
        float4 colour;
        var big = false;
        if (s.CallOpen)
        {
            (word, colour, big) = ("Now!", Theme.Good, true);
            var left = 1f - s.CallAge / (float)Math.Max(1, s.Window);
            var b0 = new float2(centre.X - Gui.S(90), centre.Y + Gui.S(20));
            var b1 = new float2(centre.X + Gui.S(90), centre.Y + Gui.S(27));
            Gui.Trough(dl, b0, b1);
            dl.AddRectFilled(b0, new float2(b0.X + (b1.X - b0.X) * Math.Clamp(left, 0f, 1f), b1.Y), Theme.U8(left > 0.33f ? Theme.Good : Theme.Warn, 0.85f), 0f, ImDrawFlags.None);
        }
        else if (s.LastStep >= 0 && s.StepsDone - s.LastStep < SledgeWordSteps)
            (word, colour) = s.LastWas switch
            {
                SledgeResult.OnCall when s.LastGrade >= 0.8f => ("Square on it!", Theme.Good),
                SledgeResult.OnCall when s.LastGrade >= 0.5f => ("On the call", Theme.Text),
                SledgeResult.OnCall => ("Light, or late", Theme.Warn),
                SledgeResult.Offbeat => ("Off the beat!", Theme.Bad),
                _ => ("Missed the call", Theme.Warn),
            };
        else if (h is { Swinging: true })
            (word, colour, big) = ("Here it comes!", Theme.Accent, true);
        else if (h is { Raise: > 0.08f })
            (word, colour) = s.Raise >= SledgeSession.FullHeave ? ($"{smith}'s winding up... hold it", Theme.Accent) : ($"{smith}'s winding up: heave!", Theme.Warn);
        else if (s.Raise < SledgeSession.FullHeave && s.Ready)
            (word, colour) = ("Heave the sledge up (hold F)", Theme.Text);
        else
            (word, colour) = ($"Held high. Watch {smith}'s hammer", Theme.Dim);
        if (big)
            using (Gui.HeaderFont())
            {
                var ts = ImGui.CalcTextSize(word);
                dl.AddText(centre - ts * 0.5f, Theme.U8(colour), word);
            }
        else
        {
            var ts = ImGui.CalcTextSize(word);
            dl.AddText(centre - ts * 0.5f, Theme.U8(colour), word);
        }

        // How far the two of you have drawn it out.
        if (h != null)
        {
            ImGui.Spacing();
            var sMin = ImGui.GetCursorScreenPos();
            var sSize = Gui.S(420, 8);
            ImGui.Dummy(in sSize);
            Gui.Trough(dl, sMin, sMin + sSize);
            dl.AddRectFilled(sMin, new float2(sMin.X + sSize.X * Math.Clamp(h.Shape, 0f, 1f), sMin.Y + sSize.Y), Theme.U8(Theme.Accent, 0.8f), 0f, ImDrawFlags.None);
        }
        if (s.Tiring)
            TextC(Theme.Warn, "Your arms are tiring: it's sagging. Let it fall on the next call.");
        else
            TextC(Theme.Dim, $"On the call {s.Strikes}, off the beat {s.Offbeats}, missed {s.Missed}.");
        EndWork();
    }

    /// <summary>
    /// Under the smith's own anvil: on heavy work, who's on the sledge and how they're holding it, how their last
    /// blow went, or that there's nobody. The smith's taps call the sledge; alone, the piece barely moves.
    /// </summary>
    private void DrawStrikerLine(HammerSession s)
    {
        if (!s.Heavy)
            return;
        var w = _sim.World;
        if (!w.IsAlive(s.Partner) || !w.TryGet<Creature>(s.Partner, out var striker))
        {
            TextC(Theme.Warn, "Striker's work: your taps barely move it alone. Call Help! from the quick-say wheel for a hand on the sledge.");
            return;
        }
        var recent = s.LastStrikeStep >= 0 && s.StepsDone - s.LastStrikeStep < SledgeWordSteps;
        var sledge = w.TryGet<WorkSession>(s.Partner, out var hs) ? hs as SledgeSession : null;
        if (recent && s.LastStrike < 0f)
            TextC(Theme.Bad, $"{striker.Name} brought the sledge down off the beat!");
        else if (recent)
            TextC(s.LastStrike >= 0.6f ? Theme.Good : Theme.Text, $"{striker.Name} struck on your call{(s.LastStrike >= 0.8f ? ": square on it" : s.LastStrike < 0.4f ? ", light" : "")}.");
        else if (sledge is { Raise: >= SledgeSession.FullHeave })
            TextC(Theme.Good, $"{striker.Name} has the sledge up and ready: your tap's the call.");
        else if (sledge != null)
            TextC(Theme.Text, $"{striker.Name} is heaving the sledge up...");
        else
            TextC(Theme.Dim, $"{striker.Name} is at the sledge: your tap is the call.");
    }
}

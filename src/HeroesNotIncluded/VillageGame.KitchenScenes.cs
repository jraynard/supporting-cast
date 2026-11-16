using Brutal.ImGuiApi;
using Narsil.Ui;
using HeroesNotIncluded.Sim.Hospitality;
using HeroesNotIncluded.Sim.Work;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

// The kitchen's and the bar's mini-games as small scenes over their gauges, from the ui/work/* props
// (tools/work_ui_art.py), as the smith's and the healer's are. Each is drawn from the session in hand
// only: what the hands are doing and what's been done, never a hidden doneness, rot or bake.
public sealed partial class VillageGame
{
    private static readonly float4 Steam = new(0.92f, 0.9f, 0.86f, 1f);

    /// <summary>The colour of a smell word, for the wisps off what's being sniffed or baked: from what's said, never the hidden state.</summary>
    private static float4 SmellColour(string smell) =>
        smell.Contains("burn") || smell.Contains("char") || smell.Contains("acrid") ? new float4(0.35f, 0.3f, 0.28f, 1f)
        : smell.Contains("golden") || smell.Contains("toast") ? new float4(0.95f, 0.75f, 0.4f, 1f)
        : smell.Contains("bread") || smell.Contains("baking") ? new float4(0.95f, 0.85f, 0.6f, 1f)
        : smell.Contains("off") || smell.Contains("sour") || smell.Contains("rank") || smell.Contains("turn") ? new float4(0.6f, 0.7f, 0.35f, 1f)
        : Steam;

    /// <summary>Wisps rising from a point, as many as <paramref name="count"/>, drifting.</summary>
    private void Wisps(ImDrawListPtr dl, float2 from, int count, float spread, float rise, float4 colour, float alpha)
    {
        for (var k = 0; k < count; k++)
        {
            var t = (_time * 0.5f + k / (float)count) % 1f;
            var at = from + new float2((k - (count - 1) * 0.5f) * spread + MathF.Sin(_time * 1.6f + k) * Gui.S(3), -t * rise);
            dl.AddCircleFilled(in at, Gui.S(3f + 5f * t), Gui.U8(colour, alpha * (1f - t)), 12);
        }
    }

    // ---------------- The kitchen ----------------

    /// <summary>The pot over its fire: the fire as high as the flame, steam by the surface, the spoon turning while you stir, the lid on in a flare.</summary>
    private void StewScene(StewSession s)
    {
        var (dl, min, max, px) = WorkScene(130, s.Lit ? Theme.Accent : null);
        var floor = max.Y - Gui.S(6);
        var mid = (min.X + max.X) * 0.45f;
        if (s.Lit)
            Prop(dl, "fire_bed", new float2(mid, floor), px * (0.6f + 0.5f * Math.Clamp(s.Flame, 0f, 1f)));
        if (Prop(dl, "stew_pot", new float2(mid, floor - 6 * px), px) is not { } pot)
            return;
        var rim = new float2(mid, pot.Min.Y + 6 * px);
        var (count, alpha) = s.Surface switch
        {
            Surface.Still => (0, 0f),
            Surface.Steaming => (2, 0.18f),
            Surface.Simmer => (3, 0.24f),
            _ => (5, 0.32f),
        };
        Wisps(dl, rim, count, Gui.S(14), Gui.S(44), Steam, alpha);
        if (s.Flaring > 0)
            for (var k = 0; k < 4; k++)
            {
                var f = new float2(rim.X + (k - 1.5f) * Gui.S(14), rim.Y - Gui.S(8) - MathF.Abs(MathF.Sin(_time * 9f + k)) * Gui.S(18));
                dl.AddCircleFilled(in f, Gui.S(6), Gui.U8(new float4(1f, 0.55f, 0.15f, 1f), 0.8f), 10);
            }
        if (s.Lidded > 0)
            Prop(dl, "pot_lid", rim + new float2(0, 3 * px), px);
        else
            PropTurned(dl, "spoon", rim + new float2(Gui.S(10), -Gui.S(20)), new float2(0.5f, 0f), s.Stirring ? MathF.Sin(_time * 6f) * 22f : 12f, px);
    }

    /// <summary>The joint on its spit over the fire, coloured by the crust of the side turned up; fat dripping, flames leaping in a flare.</summary>
    private void RoastScene(RoastSession s)
    {
        var st = SceneFor("roast", s.StartTick);
        var (dl, min, max, px) = WorkScene(120, s.Lit ? Theme.Accent : null);
        var floor = max.Y - Gui.S(6);
        var mid = (min.X + max.X) * 0.5f;
        if (s.Lit)
            Prop(dl, "fire_bed", new float2(mid, floor), px * (s.Flaring > 0 ? 1.3f : 1f));
        if (Prop(dl, "firedogs", new float2(mid, floor), px) is not { } dogs)
            return;
        var rod = new float2(mid - 3 * px, dogs.Min.Y + 6 * px);
        var up = s.Brown.Length > 0 ? s.Brown[s.Facing % s.Brown.Length] : 0f;
        if (!s.Lifted)
            Prop(dl, "joint", rod + new float2(0, 10 * px), px, Gui.U8(CrustColour(Roasting.CrustOf(up))));
        if (s.Lit && !s.Lifted && _time - st.Progress > 0.35f)
        {
            st.Progress = _time;
            st.Motes.Add(new Mote(rod + new float2((_time * 37f % 20f - 10f) * px, 10 * px), rod + new float2(0, floor - rod.Y), _time, 0.5f));
        }
        DrawMotes(dl, st, new float4(1f, 0.8f, 0.4f, 1f), px);
        if (s.Flaring > 0)
            Wisps(dl, rod + new float2(0, 12 * px), 4, Gui.S(16), Gui.S(30), new float4(1f, 0.5f, 0.15f, 1f), 0.6f);
    }

    /// <summary>The floured board, the dough on it (it firms as it's worked), the hands pushing; a puff of flour each push.</summary>
    private void KneadScene(KneadSession s)
    {
        var st = SceneFor("knead", s.StartTick, s.Pushed);
        var (dl, min, max, px) = WorkScene(110);
        var floor = max.Y - Gui.S(6);
        var mid = (min.X + max.X) * 0.5f;
        if (Prop(dl, "knead_board", new float2(mid, floor), px) is not { } board)
            return;
        var firm = Math.Clamp(s.Pushed / 12f, 0f, 1f);
        var squash = s.Off < 3 ? 0.85f : 1f;
        Prop(dl, "dough", new float2(mid, board.Min.Y + 2 * px), px * squash, Gui.U8(new float4(0.95f, 0.9f - 0.08f * firm, 0.78f - 0.1f * firm, 1f)));
        Prop(dl, "hand", new float2(mid + s.NextSide * Gui.S(26), board.Min.Y - 12 * px + (s.Off < 3 ? 4 * px : 0)), px);
        if (s.Pushed > st.Count)
            for (var g = 0; g < 6; g++)
                st.Motes.Add(new Mote(new float2(mid, board.Min.Y - 4 * px), new float2(mid + (g - 2.5f) * 6 * px, board.Min.Y - 16 * px), _time + g * 0.02f, 0.45f));
        st.Count = s.Pushed;
        DrawMotes(dl, st, new float4(0.96f, 0.94f, 0.88f, 1f), px);
    }

    /// <summary>The oven's front, its mouth glowing while it's fired, and wisps the colour of what you smell (never what's baking inside).</summary>
    private void BakeScene(BakeSession s, float4 smell)
    {
        var (dl, min, max, px) = WorkScene(155, s.Lit ? Theme.Accent : null);
        var floor = max.Y - Gui.S(6);
        var mid = (min.X + max.X) * 0.45f;
        if (Prop(dl, "oven_front", new float2(mid, floor), px) is not { } oven)
            return;
        var mouth = new float2(mid, oven.Max.Y - 12 * px);
        if (s.Lit)
            dl.AddCircleFilled(in mouth, 9 * px * (0.9f + 0.1f * MathF.Sin(_time * 7f)), Gui.U8(new float4(1f, 0.55f, 0.18f, 1f), 0.55f), 16);
        Wisps(dl, new float2(mid, oven.Min.Y + 20 * px), 3, Gui.S(10), Gui.S(36), smell, 0.3f);
        Prop(dl, "peel", new float2(max.X - Gui.S(70), floor - Gui.S(10)), px);
    }

    /// <summary>The block, the side of pork on it parted where it's been cut, the cleaver over it; chips at each blow.</summary>
    private void ButcherScene(ButcherSession s)
    {
        var st = SceneFor("butcher", s.StartTick, s.Blows);
        var (dl, min, max, px) = WorkScene(150);
        var floor = max.Y - Gui.S(6);
        var mid = (min.X + max.X) * 0.5f;
        if (Prop(dl, "block_side", new float2(mid, floor), px) is not { } block)
            return;
        var parted = s.Parted.Count(p => p);
        Prop(dl, "pork_side", new float2(mid, block.Min.Y + 4 * px), px);
        for (var i = 0; i < parted; i++)
        {
            var gap = new float2(mid + (i - (parted - 1) * 0.5f) * 12 * px, block.Min.Y - 6 * px);
            dl.AddRectFilled(gap - new float2(px, 6 * px), gap + new float2(px, 6 * px), Gui.U8(Theme.Well), 0f, ImDrawFlags.None);
        }
        PropTurned(dl, "cleaver", new float2(mid + Gui.S(30), block.Min.Y - 24 * px), new float2(0.8f, 0.5f), -40f + 50f * s.Cleaver, px);
        if (s.Blows > st.Count)
            for (var g = 0; g < 5; g++)
                st.Motes.Add(new Mote(new float2(mid, block.Min.Y), new float2(mid + (g - 2) * 8 * px, block.Min.Y - 12 * px), _time, 0.4f));
        st.Count = s.Blows;
        DrawMotes(dl, st, new float4(0.75f, 0.55f, 0.4f, 1f), px);
    }

    /// <summary>The bird on the block, feathers thinning as it's plucked; a handful flying at each pull.</summary>
    private void PluckScene(PluckSession s)
    {
        var st = SceneFor("pluck", s.StartTick, 0, s.Plucked);
        var (dl, min, max, px) = WorkScene(165);
        var floor = max.Y - Gui.S(6);
        var mid = (min.X + max.X) * 0.5f;
        if (Prop(dl, "block_side", new float2(mid, floor), px) is not { } block)
            return;
        if (Prop(dl, "bird", new float2(mid, block.Min.Y + 2 * px), px) is not { } bird)
            return;
        var left = (int)MathF.Round(18 * (1f - Math.Clamp(s.Plucked, 0f, 1f)));
        for (var i = 0; i < left; i++)
            Prop(dl, "feather", new float2(bird.Min.X + (4 + i * 37 % 26) * px, bird.Min.Y + (8 + i * 13 % 14) * px), px);
        if (s.Plucked > st.Progress + 0.01f)
            for (var g = 0; g < 4; g++)
                st.Motes.Add(new Mote(new float2(mid, bird.Min.Y + 6 * px), new float2(mid + (g - 1.5f) * 14 * px, min.Y + 6 * px), _time + g * 0.04f, 0.9f));
        st.Progress = s.Plucked;
        DrawMotes(dl, st, new float4(0.6f, 0.47f, 0.35f, 1f), px * 2);
        Prop(dl, "hand", new float2(mid + Gui.S(24), bird.Min.Y - (s.Taut ? 8 : 2) * px), px);
    }

    /// <summary>The pie's lid with a crimp at every mark pressed, and the fingers going round to the next.</summary>
    private void CrimpScene(CrimpSession s)
    {
        var (dl, min, max, px) = WorkScene(72);
        var floor = max.Y - Gui.S(6);
        var mid = (min.X + max.X) * 0.5f;
        if (Prop(dl, "pie_top", new float2(mid, floor), px) is not { } pie)
            return;
        var marks = s.Crimps.Length;
        for (var i = 0; i < marks; i++)
            if (s.Crimps[i] > 0)
            {
                var x = pie.Min.X + (pie.Max.X - pie.Min.X) * (i + 0.5f) / marks;
                dl.AddRectFilled(new float2(x - px, pie.Min.Y + px), new float2(x + px, pie.Min.Y + 4 * px), Gui.U8(new float4(0.55f, 0.36f, 0.2f, 1f)), 0f, ImDrawFlags.None);
            }
        var hx = pie.Min.X + (pie.Max.X - pie.Min.X) * ((s.At % marks + 0.5f) / marks);
        Prop(dl, "hand", new float2(hx, pie.Min.Y), px);
    }

    /// <summary>The board, what's being chopped on it in as many pieces as cuts so far, the knife coming down on the beat.</summary>
    private void ChopScene(ChopSession s)
    {
        var st = SceneFor("chop", s.StartTick, s.Cuts);
        var (dl, min, max, px) = WorkScene(80);
        var floor = max.Y - Gui.S(6);
        var mid = (min.X + max.X) * 0.5f;
        if (Prop(dl, "chop_board", new float2(mid, floor), px) is not { } board)
            return;
        var food = s.Label.Contains("fish") ? "fish" : s.Label.Contains("cabbage") || s.Label.Contains("greens") || s.Label.Contains("root") ? "cabbage" : "meat";
        var pieces = Math.Min(s.Cuts, 8);
        for (var i = 0; i <= pieces; i++)
            Prop(dl, food, new float2(mid - Gui.S(20) + i * 4 * px + (i > 0 ? 2 * px : 0), board.Min.Y + px), px * (i == 0 ? 1f : 0.45f));
        var down = s.Off < 2;
        PropTurned(dl, "kitchen_knife", new float2(mid + Gui.S(24), board.Min.Y - (down ? 2 : 12) * px), new float2(0.1f, 0.5f), down ? 0f : -18f, px);
        if (s.Cuts > st.Count)
            for (var g = 0; g < 3; g++)
                st.Motes.Add(new Mote(new float2(mid + Gui.S(10), board.Min.Y), new float2(mid + Gui.S(10) + (g - 1) * 6 * px, board.Min.Y - 8 * px), _time, 0.3f));
        st.Count = s.Cuts;
        DrawMotes(dl, st, new float4(0.6f, 0.8f, 0.45f, 1f), px);
    }

    /// <summary>The cloth laid out, its corners folded in as they're done, the cord round it once wrapped.</summary>
    private void PackScene(PackSession s)
    {
        var (dl, min, max, px) = WorkScene(110);
        var floor = max.Y - Gui.S(6);
        var mid = (min.X + max.X) * 0.5f;
        if (Prop(dl, "cloth", new float2(mid, floor), px) is not { } cloth)
            return;
        var c = (cloth.Min + cloth.Max) * 0.5f;
        float2[] corners = [cloth.Min, new(cloth.Max.X, cloth.Min.Y), cloth.Max, new(cloth.Min.X, cloth.Max.Y)];
        var fold = Gui.U8(new float4(0.84f, 0.8f, 0.7f, 1f));
        for (var i = 0; i < Math.Min(s.Folded, 4); i++)
        {
            var a = corners[i];
            var b = a + (c - a) * 0.95f;
            var side = corners[(i + 1) % 4];
            var mid1 = a + (side - a) * 0.5f;
            var prev = corners[(i + 3) % 4];
            var mid2 = a + (prev - a) * 0.5f;
            dl.AddTriangleFilled(in mid1, in b, in mid2, fold);
            dl.AddTriangle(in mid1, in b, in mid2, Gui.U8(new float4(0.3f, 0.25f, 0.2f, 1f)), 1f);
        }
        if (s.Wrapped)
        {
            Prop(dl, "cord", new float2(c.X, c.Y + 2 * px), px);
            PropTurned(dl, "cord", c, new float2(0.5f, 0.5f), 90f, px);
        }
        else
            Prop(dl, "hand", new float2(c.X + s.NextSide * Gui.S(30), c.Y - (s.OnCue ? 0 : 6) * px), px);
    }

    /// <summary>What's being sniffed, and wisps off it the colour of what you smell (only that: never the rot itself).</summary>
    private void SniffScene(SniffSession s, float4 smell)
    {
        var (dl, min, max, px) = WorkScene(100);
        var floor = max.Y - Gui.S(6);
        var mid = (min.X + max.X) * 0.5f;
        var sprite = s.IsPot ? "stew_pot" : s.Label.Contains("fish") ? "fish" : s.Label.Contains("pork") || s.Label.Contains("meat") ? "meat" : "cabbage";
        if (Prop(dl, sprite, new float2(mid, floor), px) is not { } it)
            return;
        Wisps(dl, new float2(mid, it.Min.Y), 3 + Math.Min(s.Sniffs, 3), Gui.S(8), Gui.S(36), smell, 0.35f);
    }

    // ---------------- The bar ----------------

    /// <summary>The cask's tap and the tankard tilted under it, the ale and its head rising, a spill on the boards if it ran over.</summary>
    private void PourScene(PourSession s)
    {
        var (dl, min, max, px) = WorkScene(100);
        var floor = max.Y - Gui.S(6);
        var mid = (min.X + max.X) * 0.5f;
        Prop(dl, "spigot", new float2(mid - Gui.S(4), min.Y + Gui.S(20)), px);
        var tilt = (s.Tilt - 0.5f) * 40f;
        var cup = new float2(mid, floor);
        PropTurned(dl, "tankard", cup, new float2(0.4f, 1f), tilt, px);
        // The ale and its head inside it, drawn upright over the tankard's body.
        var body = 13 * px;
        var height = 19 * px;
        var x0 = cup.X - 0.4f * 22 * px + 3 * px;
        var ale = Math.Clamp(s.Level, 0f, 1f) * height;
        var head = Math.Clamp(s.Foam, 0f, 0.4f) * height;
        if (ale > 0)
            dl.AddRectFilled(new float2(x0, cup.Y - 2 * px - ale), new float2(x0 + body - 2 * px, cup.Y - 2 * px), Gui.U8(new float4(0.78f, 0.5f, 0.16f, 1f), 0.9f), 0f, ImDrawFlags.None);
        if (head > 0)
            dl.AddRectFilled(new float2(x0, cup.Y - 2 * px - ale - head), new float2(x0 + body - 2 * px, cup.Y - 2 * px - ale), Gui.U8(new float4(0.97f, 0.94f, 0.86f, 1f), 0.95f), 0f, ImDrawFlags.None);
        if (s.Held)
            dl.AddLine(new float2(mid - Gui.S(14), min.Y + Gui.S(22)), new float2(x0 + body * 0.5f, cup.Y - 2 * px - ale - head), Gui.U8(new float4(0.8f, 0.52f, 0.18f, 1f)), 2 * px);
        if (s.Spilled)
            dl.AddCircleFilled(new float2(cup.X + Gui.S(30), floor - px), Gui.S(9), Gui.U8(new float4(0.8f, 0.6f, 0.25f, 1f), 0.6f), 14);
    }

    /// <summary>The cup and the bottles of what goes in it; a splash at each cue landed.</summary>
    private void MixScene(MixSession s)
    {
        var st = SceneFor("mix", s.StartTick, s.Hits);
        var (dl, min, max, px) = WorkScene(100);
        var floor = max.Y - Gui.S(6);
        var mid = (min.X + max.X) * 0.5f;
        if (Prop(dl, "cup", new float2(mid, floor), px) is not { } cup)
            return;
        var adds = Math.Max(1, s.Adds.Split(',', StringSplitOptions.RemoveEmptyEntries).Length);
        for (var i = 0; i < Math.Min(adds, 4); i++)
            Prop(dl, "bottle", new float2(mid - Gui.S(70) - i * 14 * px, floor), px);
        if (s.Hits > st.Count)
            for (var g = 0; g < 5; g++)
                st.Motes.Add(new Mote(new float2(mid, cup.Min.Y), new float2(mid + (g - 2) * 6 * px, cup.Min.Y - 10 * px), _time, 0.4f));
        st.Count = s.Hits;
        DrawMotes(dl, st, new float4(0.85f, 0.6f, 0.3f, 1f), px);
    }

    // ---------------- The rooms ----------------

    /// <summary>
    /// The book open on the board, the guest's line ("Gorran, room 3") written out on today's page by the
    /// quill a letter at a time once it's begun; till then the quill waits at the line's start.
    /// </summary>
    private void SignInScene(float ink, bool writing, string line)
    {
        var (dl, min, max, px) = WorkScene(150);
        var floor = max.Y - Gui.S(6);
        var mid = (min.X + max.X) * 0.5f;
        if (Prop(dl, "ledger_page", new float2(mid, floor), px) is not { } page)
            return;
        var inkColour = Gui.U8(new float4(0.16f, 0.13f, 0.12f, 1f));
        // Today's page is the right-hand one, blank under yesterday's on the left.
        var lineY = page.Min.Y + 7 * px;
        var x0 = (page.Min.X + page.Max.X) * 0.5f + 5 * px;
        var shown = (int)MathF.Round(line.Length * Math.Clamp(ink, 0f, 1f));
        var written = line[..shown];
        var at = new float2(x0, lineY);
        float width, lineH;
        using (Gui.DisplayFont(Gui.S(13)))
        {
            dl.AddText(in at, inkColour, written);
            width = ImGui.CalcTextSize(written, false, -1).X;
            lineH = ImGui.GetTextLineHeight();
        }
        // The quill at the end of what's written, bobbing with each letter as it writes.
        var scratch = writing && ink < 1f ? MathF.Abs(MathF.Sin(_time * 22f)) * 2 * px : 0f;
        var tip = new float2(x0 + width + px, lineY + lineH - scratch);
        PropTurned(dl, "quill", tip, new float2(0f, 1f), -10f, px);
    }

    /// <summary>The chamber pot tipping over the bucket as the pour sways round; the bucket filling.</summary>
    private void SlopScene(SlopSession s)
    {
        var (dl, min, max, px) = WorkScene(110);
        var floor = max.Y - Gui.S(6);
        var mid = (min.X + max.X) * 0.5f;
        if (Prop(dl, "bucket", new float2(mid + Gui.S(20), floor), px) is not { } bucket)
            return;
        PropTurned(dl, "chamber_pot", new float2(mid - Gui.S(10), bucket.Min.Y - 4 * px), new float2(0.8f, 0.5f), s.Sweep * 80f, px, s.OnCue ? Gui.U8(Theme.Good) : null);
        if (s.Held || s.Sweep > 0.6f)
            dl.AddLine(new float2(mid - Gui.S(4), bucket.Min.Y - 2 * px), new float2(mid + Gui.S(18), bucket.Min.Y + 4 * px), Gui.U8(new float4(0.45f, 0.4f, 0.2f, 1f), 0.8f), 2 * px);
        for (var i = 0; i < s.Spills; i++)
            dl.AddCircleFilled(new float2(min.X + Gui.S(40 + 20 * i), floor - px), 4 * px, Gui.U8(new float4(0.45f, 0.4f, 0.2f, 1f), 0.6f), 12);
    }
}

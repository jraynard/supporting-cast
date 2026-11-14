using Brutal.ImGuiApi;
using Narsil.Ui;
using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Ecs;
using HeroesNotIncluded.Sim.Healing;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

// The empath's gift: feeling a patient's wounds and drawing them across, mending what you carry,
// meditating. Wounds come through View.FeltWounds, which names what you've drawn only by its feel.
public sealed partial class VillageGame
{
    /// <summary>For an empath with hands on someone: feeling them over, then the wounds they found, each to draw across.</summary>
    private void DrawLayOnHands(Entity patient)
    {
        if (!HasAvatar || patient == _avatar || !Empathy.Gifted(_sim, _avatar) || View.FeltWounds(patient) is not { } wounds)
            return;
        ImGui.Separator();
        var cantFeel = FeelSession.Problem(_sim, _avatar, patient);
        ImGui.BeginDisabled(cantFeel != null || Working);
        if (ImGui.SmallButton(wounds.Count == 0 ? "Feel them over"u8 : "Feel them over again"u8))
            Issue(new FeelCommand(_avatar, patient));
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            Tip(cantFeel != null ? Cap(cantFeel) + "." : Controls.Keys("~AD~ run your hands from head to feet; linger where it hurts to make it out. Hold ~F~ to press in: slower, but it finds festering under a dressing."));
        var cantBind = Bandaging.CantBind(_sim, _avatar, patient);
        if (Bandaging.WoundsToBind(_sim, patient) > 0)
        {
            ImGui.SameLine(0, 6);
            ImGui.BeginDisabled(cantBind != null || Working);
            if (ImGui.SmallButton("Bind their wounds"u8))
                Issue(new BandageCommand(_avatar, patient));
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                Tip(cantBind != null ? Cap(cantBind) + "." : $"No mana needed: {Bandaging.BandagesOn(_sim, _avatar)} bandages to hand (yours, and any chest in reach). For each turn of cloth, hold {Controls.Glyph(Act.Use)} to pull it tight and let go in the band.");
        }
        if (wounds.Count == 0)
            return;
        TextC(Theme.Accent, "Under your hands you feel:");
        foreach (var wound in wounds)
        {
            ImGui.PushID(wound.Key);
            if (ImGui.SmallButton("Draw"u8))
                Issue(new LayOnHandsCommand(_avatar, patient, wound.Key));
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip(Controls.Keys("Hold ~F~ to draw it into yourself, ~AD~ to keep your composure in the band. It lands on your own body."));
            ImGui.SameLine(0, 6);
            TextC(wound.Festering ? Theme.Bad : Theme.Text, $"{Cap(wound.Severity)} {wound.Feels} in the {wound.Part}{(wound.Festering ? ", festering" : "")}");
            ImGui.PopID();
        }
    }

    /// <summary>The gift's window: mana and meditating, what you carry (each to weave a mending for), and your book.</summary>
    private void DrawGift()
    {
        if (!_sim.Round.Live || !HasAvatar || !_showGift || !_sim.World.TryGet<Mana>(_avatar, out var mana))
            return;
        BeginJournal("The gift###gift", ref _showGift);
        Bar("Mana", mana.Current / Math.Max(1f, mana.Max), Theme.Accent, $"{mana.Current:F0} / {mana.Max:F0}");
        var cantMeditate = MeditateSession.Problem(_sim, _avatar);
        ImGui.BeginDisabled(cantMeditate != null || Working);
        if (ImGui.Button("Meditate"u8, in _autoSize))
            Issue(new MeditateCommand(_avatar));
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            Tip(cantMeditate != null ? Cap(cantMeditate) + "." : "Find your way inward, through a labyrinth to the still centre: each new place gathers mana, the centre most of all. Pain narrows your sight and scatters your thoughts; it's calmer at the shrine's meditation stone.");
        ImGui.SameLine(0, 6);
        var cantWeave = CastSession.Problem(_sim, _avatar, 0);
        ImGui.BeginDisabled(cantWeave != null || Working);
        if (ImGui.Button("Weave"u8, in _autoSize))
            Issue(new WeaveCommand(_avatar, 0));
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            Tip(cantWeave != null ? Cap(cantWeave) + "." : "Weave a mending meant for no wound (one that steadies you, or a mix you're trying out).");
        ImGui.SameLine(0, 6);
        var cantCharm = CastSession.Problem(_sim, _avatar, 0, intoCharm: true);
        ImGui.BeginDisabled(cantCharm != null || Working);
        if (ImGui.Button("Weave a charm"u8, in _autoSize))
            Issue(new WeaveCharmCommand(_avatar));
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            Tip(cantCharm != null ? Cap(cantCharm) + "."
                : "Weave a mending into a strip of linen (a bandage), to keep. Torn, it mends whoever tears it. Put it out in the shrine with a price, "
                + "and parties buy them as wards for the road: on a quest a charm spends itself on a wound it fits, and may turn a death into a bad wound.");
        if (!ImGui.BeginTabBar("gift"u8, ImGuiTabBarFlags.None))
        {
            Panel.End();
            return;
        }
        if (ImGui.BeginTabItem("Carried"u8, ImGuiTabItemFlags.None))
        {
            var carried = View.FeltWounds(_avatar) ?? [];
            if (carried.Count == 0)
                TextC(Theme.Dim, "You carry no wounds.");
            else if (Bandaging.WoundsToBind(_sim, _avatar) > 0)
            {
                var cantBind = Bandaging.CantBind(_sim, _avatar, _avatar);
                ImGui.BeginDisabled(cantBind != null || Working);
                if (ImGui.SmallButton("Bind your wounds"u8))
                    Issue(new BandageCommand(_avatar, _avatar));
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    Tip(cantBind != null ? Cap(cantBind) + "." : "Stop the bleeding while your mana comes back. A dressing doesn't heal it the way a mending does.");
            }
            foreach (var wound in carried)
            {
                ImGui.PushID(wound.Key);
                var why = CastSession.Problem(_sim, _avatar, wound.Key);
                ImGui.BeginDisabled(why != null || Working);
                if (ImGui.SmallButton("Weave"u8))
                    Issue(new WeaveCommand(_avatar, wound.Key));
                ImGui.EndDisabled();
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    Tip(why != null ? Cap(why) + "." : Controls.Keys("Key the runes of the mending it wants (your book has them), each on a beat of your heart. ~F~ lets it go."));
                ImGui.SameLine(0, 6);
                TextC(wound.Festering ? Theme.Bad : wound.Absorbed ? Theme.Warn : Theme.Dim,
                    $"{Cap(wound.Severity)} {wound.Feels} in your {wound.Part}{(wound.Festering ? ", festering" : "")}");
                ImGui.PopID();
            }
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Book"u8, ImGuiTabItemFlags.None))
        {
            DrawSpellbook();
            ImGui.EndTabItem();
        }
        ImGui.EndTabBar();
        Panel.End();
    }

    /// <summary>The runes (and their keys), then each mending in the book: its runes, what it does, and how the wounds it takes feel.</summary>
    private void DrawSpellbook()
    {
        var runes = _sim.Defs.AllRunes;
        for (var i = 0; i < runes.Count; i++)
        {
            if (i > 0)
                ImGui.SameLine(0, 8);
            RuneGlyphs([runes[i]], 30, keys: true);
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip($"{runes[i].Label} ({i + 1}): {runes[i].Description}");
        }
        ImGui.Separator();
        var unknown = 0;
        foreach (var m in _sim.Defs.AllMendings)
        {
            if (!Weaving.Knows(_sim, _avatar, m))
            {
                unknown++;
                continue;
            }
            RuneGlyphs(m.RuneDefs, 26);
            ImGui.SameLine(0, 6);
            TextC(Theme.Accent, m.Label);
            ImGui.SameLine(0, 6);
            TextC(Theme.Dim, $"{m.ManaCost:F0} mana");
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip($"Each extra {m.LeadRune.Label} makes it stronger: +{m.CostAt(1) - m.CostAt(0):F0} mana, +{m.HealAt(1) - m.HealAt(0):F0} healing.");
            TextC(Theme.Text, m.Description);
            foreach (var feels in m.Feels.Values.Distinct())
                TextC(Theme.Dim, $"  for {feels}");
        }
        if (unknown > 0)
            TextC(Theme.Dim, unknown == 1 ? "One mending isn't in your book yet. Try a new mix of runes."
                : $"{unknown} mendings aren't in your book yet. Try new mixes of runes.");
    }

    /// <summary>A row of rune glyphs, as one item; with their keys under them if <paramref name="keys"/>.</summary>
    private void RuneGlyphs(IReadOnlyList<Sim.Defs.RuneDef> runes, float size, bool keys = false, IReadOnlyList<bool>? strong = null)
    {
        var dl = ImGui.GetWindowDrawList();
        var px = Gui.S(size);
        var min = ImGui.GetCursorScreenPos();
        var box = new float2(Math.Max(1, runes.Count) * (px + Gui.S(2)), px + (keys ? Gui.S(14) : 0));
        ImGui.Dummy(in box);
        for (var i = 0; i < runes.Count; i++)
        {
            var at = min + new float2(i * (px + Gui.S(2)), 0);
            var weak = strong != null && i < strong.Count && !strong[i];
            DrawSprite(dl, Host.Atlas.Get(runes[i].Icon), at, new float2(px, px), weak ? Theme.U8(Theme.Text, 0.45f) : null);
            if (weak)
            {
                var max = at + new float2(px, px);
                dl.AddRect(in at, in max, Theme.U8(Theme.Bad), px / 2, ImDrawFlags.None, 1.5f);
            }
            if (keys)
            {
                var k = at + new float2(px / 2 - Gui.S(3), px + Gui.S(1));
                dl.AddText(in k, Theme.U8(Theme.Dim), (_sim.Defs.AllRunes.IndexOf(runes[i]) + 1).ToString());
            }
        }
    }

    private void DrawFeel(FeelSession s)
    {
        BeginWork(s.Title, ("A/D", "run your hands along them"), ("F", "hold to press in (slower, deeper)"), ("Esc", "stop (what you found stays found)"));
        FeelScene(s.Parts, s.Spots.Where(p => p.Found > 0.05f).Select(p => (p.At, p.Gravity, p.Found, s.FoundFestering(p))), s.Hands01, s.Held);
        Bar("Feel", s.Feel, s.Feel > 0.6f ? Theme.Bad : Theme.Warn);
        EndWork();
    }

    private void DrawBandage(BandageSession s)
    {
        BeginWork(s.Title, ("F", "hold to pull the cloth tight, let go in the band"), ("Esc", "stop"));
        BandageScene(s.Turns, s.TurnsDone, s.Tension, s.Low, s.High);
        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(420, 30);
        var min = ImGui.GetCursorScreenPos();
        var max = min + size;
        ImGui.Dummy(in size);
        dl.AddRectFilled(in min, in max, Theme.U8(Theme.Spirit), 4f, ImDrawFlags.None);
        var bandMin = new float2(min.X + s.Low * size.X, min.Y);
        var bandMax = new float2(min.X + s.High * size.X, max.Y);
        dl.AddRectFilled(in bandMin, in bandMax, Theme.U8(Theme.Good, 0.5f), 2f, ImDrawFlags.None);
        var crushMin = new float2(min.X + BandageSession.Crush * size.X, min.Y);
        dl.AddRectFilled(in crushMin, in max, Theme.U8(Theme.Bad, 0.5f), 2f, ImDrawFlags.None);
        var fillMax = new float2(min.X + s.Tension * size.X, max.Y - Gui.S(8));
        var fillMin = new float2(min.X, min.Y + Gui.S(8));
        dl.AddRectFilled(in fillMin, in fillMax, Theme.U8(Theme.Text, 0.85f), 2f, ImDrawFlags.None);
        TextC(s.Last switch { 1 => Theme.Good, 0 => Theme.Warn, 2 => Theme.Bad, _ => Theme.Dim },
            s.Last switch { 1 => "That turn sits right.", 0 => "Too slack: it slipped.", 2 => "Too tight: it bites.", _ => "Pull the first turn tight." });
        Bar("Turns", s.TurnsDone / (float)Math.Max(1, s.Turns), Theme.Accent, $"{s.TurnsDone} / {s.Turns}");
        EndWork();
    }

    private void DrawDraw(DrawSession s)
    {
        BeginWork(s.Title, ("F", "hold to draw it in, let go to pause"), ("A/D", "keep your composure in the band"), ("Esc", "stop"));
        DrawScene(s.Held, s.Calm, s.Drawn, s.Strain);
        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(420, 30);
        var min = ImGui.GetCursorScreenPos();
        var max = min + size;
        ImGui.Dummy(in size);
        dl.AddRectFilled(in min, in max, Theme.U8(Theme.Spirit), 4f, ImDrawFlags.None);
        var bandMin = new float2(min.X + (0.5f - s.HalfWidth) * size.X, min.Y);
        var bandMax = new float2(min.X + (0.5f + s.HalfWidth) * size.X, max.Y);
        dl.AddRectFilled(in bandMin, in bandMax, Theme.U8(Theme.Good, 0.55f), 2f, ImDrawFlags.None);
        var x = min.X + s.Composure * size.X;
        var nMin = new float2(x - 2, min.Y - 3);
        var nMax = new float2(x + 2, max.Y + 3);
        dl.AddRectFilled(in nMin, in nMax, Theme.U8(s.Calm ? Theme.Accent : Theme.Bad), 1f, ImDrawFlags.None);
        Bar("Drawn", s.Drawn, Theme.Good);
        Bar("Strain", s.Strain, s.Strain > 0.6f ? Theme.Bad : Theme.Warn);
        EndWork();
    }

    /// <summary>
    /// The weave as a sigil: the heartbeat a ring of light closing on the circle at its heart (key
    /// a rune as it lands), the runes woven set round the rim and joined by strands, bright for a
    /// strong strand and broken for a weak one. Under it, the runes there are, with their keys.
    /// </summary>
    private void DrawCast(CastSession s)
    {
        BeginWork(s.Title, ("1-6", "key a rune as the ring meets the circle"), ("F", "let the weave go"), ("Esc", "let it unravel (nothing spent)"));
        var dl = ImGui.GetWindowDrawList();
        // As wide as the window (its hints may make it wider than the circle), so it all sits centred.
        var size = new float2(Math.Max(Gui.S(420), ImGui.GetContentRegionAvail().X), Gui.S(300));
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(in size);
        var centre = min + new float2(size.X / 2, size.Y / 2);
        var outer = Gui.S(118f);
        var inner = Gui.S(40f);
        var glyph = Gui.S(50f);

        // The ground: a dark well, a faint rim, and the ring of the six places.
        dl.AddCircleFilled(in centre, outer + glyph * 0.6f, Theme.U8(Theme.Spirit, 0.95f), 96);
        dl.AddCircle(in centre, outer + glyph * 0.6f, Theme.U8(Theme.Arcane, 0.25f), 96, Gui.S(1.5f));
        dl.AddCircle(in centre, outer, Theme.U8(Theme.Arcane, 0.18f), 96, Gui.S(1f));

        // The heartbeat: a ring closing from the rim on the circle at the heart.
        var prev = Math.Max(s.PrevBeat, 0);
        var period = Math.Max(1, s.NextBeat - prev);
        var t = Math.Clamp((s.NextBeat - s.StepsDone) / (float)period, 0f, 1f);
        var near = Math.Min(Math.Abs(s.StepsDone - s.NextBeat), Math.Abs(s.StepsDone - s.PrevBeat)) <= s.WindowSteps;
        var window = (outer - inner) * s.WindowSteps / period;
        dl.AddCircle(in centre, inner + window * 0.5f, Theme.U8(Theme.Arcane, near ? 0.3f : 0.12f), 64, window);
        var since = s.StepsDone - s.PrevBeat;
        var thump = since is >= 0 and < 14 ? 1f - since / 14f : 0f;
        dl.AddCircleFilled(in centre, inner * (0.82f + 0.18f * thump), Theme.U8(Theme.Arcane, 0.18f + 0.5f * thump), 64);
        dl.AddCircle(in centre, inner, Theme.U8(Theme.Arcane, 0.85f), 64, Gui.S(2f));
        if (s.Woven.Count < MendingDef.MaxRunes)
            dl.AddCircle(in centre, inner + (outer - inner) * t, Theme.U8(near ? Theme.Text : Theme.Arcane, 0.55f + 0.4f * (1f - t)), 96, Gui.S(3f));

        // The places round the rim, the strands between what's woven, and the runes themselves.
        float2 Place(int i)
        {
            var a = -MathF.PI / 2 + i * MathF.Tau / MendingDef.MaxRunes;
            return centre + new float2(MathF.Cos(a), MathF.Sin(a)) * outer;
        }
        for (var i = 1; i < s.Woven.Count; i++)
        {
            var a = Place(i - 1);
            var b = Place(i);
            var strong = s.Strong[i - 1] && s.Strong[i];
            dl.AddLine(in a, in b, Theme.U8(strong ? Theme.Arcane : Theme.Bad, strong ? 0.8f : 0.4f), Gui.S(strong ? 3f : 1.5f));
        }
        if (s.Woven.Count > 0)
        {
            // A strand from the heart to the last rune woven, bright as it's keyed.
            var last = Place(s.Woven.Count - 1);
            var flash = Math.Clamp(1f - (s.StepsDone - s.LastWovenAt) / 20f, 0f, 1f);
            dl.AddLine(in centre, in last, Theme.U8(Theme.Arcane, 0.15f + 0.6f * flash), Gui.S(1.5f + 2f * flash));
        }
        for (var i = 0; i < MendingDef.MaxRunes; i++)
        {
            var at = Place(i);
            if (i >= s.Woven.Count)
            {
                dl.AddCircle(in at, glyph * 0.32f, Theme.U8(Theme.Arcane, 0.3f), 32, Gui.S(1f));
                continue;
            }
            var strong = s.Strong[i];
            var fresh = i == s.Woven.Count - 1 ? Math.Clamp(1f - (s.StepsDone - s.LastWovenAt) / 20f, 0f, 1f) : 0f;
            if (strong)
                dl.AddCircleFilled(in at, glyph * (0.62f + 0.25f * fresh), Theme.U8(Theme.Arcane, 0.3f + 0.4f * fresh), 32);
            var size2 = glyph * (1f + 0.15f * fresh);
            var corner = at - new float2(size2 / 2, size2 / 2);
            DrawSprite(dl, Host.Atlas.Get(s.Palette[s.Woven[i]].Icon), corner, new float2(size2, size2), strong ? null : Theme.U8(Theme.Text, 0.4f));
            if (!strong)
                dl.AddCircle(in at, size2 * 0.5f, Theme.U8(Theme.Bad, 0.8f), 32, Gui.S(2f));
        }

        // The runes there are, keyed 1 to 6; the one just keyed lights.
        var palette = _sim.Defs.AllRunes;
        var cell = Gui.S(46f);
        var gap = Gui.S(8f);
        var row = ImGui.GetCursorScreenPos() + new float2((size.X - palette.Count * cell - (palette.Count - 1) * gap) / 2, 0);
        var rowSize = new float2(size.X, cell + Gui.S(18));
        ImGui.Dummy(in rowSize);
        var lit = s.Woven.Count > 0 && s.StepsDone - s.LastWovenAt < 20 ? s.Woven[^1] : -1;
        for (var i = 0; i < palette.Count; i++)
        {
            var at = row + new float2(i * (cell + gap), 0);
            if (i == lit)
            {
                var c = at + new float2(cell / 2, cell / 2);
                dl.AddCircleFilled(in c, cell * 0.62f, Theme.U8(Theme.Arcane, 0.45f), 32);
            }
            DrawSprite(dl, Host.Atlas.Get(palette[i].Icon), at, new float2(cell, cell));
            var key = (i + 1).ToString();
            var k = at + new float2(cell / 2 - ImGui.CalcTextSize(key).X / 2, cell + Gui.S(1));
            dl.AddText(in k, Theme.U8(Theme.Dim), key);
        }
        if (_sim.World.TryGet<Mana>(_avatar, out var mana))
            Bar("Mana", mana.Current / Math.Max(1f, mana.Max), Theme.Arcane, $"{mana.Current:F0} / {mana.Max:F0}");
        EndWork();
    }

    /// <summary>
    /// The labyrinth inward: what's in sight clear, what's been seen dim, the rest dark; the light
    /// of the still centre always faintly there to walk toward; the way walked a faint thread.
    /// </summary>
    private void DrawMeditate(MeditateSession s)
    {
        BeginWork(s.Title, ("WASD", "find your way inward"), ("F", "hold to be still and see further"), ("Esc", "come back"));
        var dl = ImGui.GetWindowDrawList();
        var width = Math.Max(Gui.S(420), ImGui.GetContentRegionAvail().X);
        var side = Gui.S(330f);
        var origin = ImGui.GetCursorScreenPos();
        var areaSize = new float2(width, side);
        ImGui.Dummy(in areaSize);
        var min = origin + new float2((width - side) / 2, 0);
        var max = min + new float2(side, side);
        var cell = side / MeditateSession.Size;
        dl.AddRectFilled(in min, in max, Theme.U8(Theme.Spirit), 6f, ImDrawFlags.None);
        float2 Mid(int i) => min + new float2((i % MeditateSession.Size + 0.5f) * cell, (i / MeditateSession.Size + 0.5f) * cell);

        var sight = s.SightNow;
        var pulse = 0.5f + 0.5f * MathF.Sin(_time * 2.2f);
        // The light at the heart: always there to walk toward, brighter once seen.
        var heart = Mid(MeditateSession.Index(MeditateSession.Centre, MeditateSession.Centre));
        var heartSeen = s.Seen[MeditateSession.Index(MeditateSession.Centre, MeditateSession.Centre)];
        dl.AddCircleFilled(in heart, cell * (0.9f + 0.15f * pulse), Theme.U8(Theme.Accent, heartSeen ? 0.22f : 0.1f), 32);
        dl.AddCircleFilled(in heart, cell * 0.32f, Theme.U8(Theme.Accent, heartSeen ? 0.9f : 0.35f), 24);

        // The places walked, and the thread of the way here.
        for (var i = 0; i < s.Walked.Length; i++)
            if (s.Walked[i])
            {
                var c = Mid(i);
                var a = c - new float2(cell / 2, cell / 2);
                var b = c + new float2(cell / 2, cell / 2);
                dl.AddRectFilled(in a, in b, Theme.U8(Theme.Arcane, 0.08f), 0f, ImDrawFlags.None);
            }
        for (var i = 1; i < s.Trail.Count; i++)
        {
            var a = Mid(s.Trail[i - 1]);
            var b = Mid(s.Trail[i]);
            dl.AddLine(in a, in b, Theme.U8(Theme.Arcane, 0.35f), Gui.S(2f));
        }

        // The walls of what's seen: clear in sight, dim as remembered.
        var thick = Gui.S(2.5f);
        for (var y = 0; y < MeditateSession.Size; y++)
            for (var x = 0; x < MeditateSession.Size; x++)
            {
                var i = MeditateSession.Index(x, y);
                if (!s.Seen[i])
                    continue;
                var near = (x - s.X) * (x - s.X) + (y - s.Y) * (y - s.Y) <= sight * sight;
                var colour = Theme.U8(Theme.Arcane, near ? 0.9f : 0.3f);
                var a = min + new float2(x * cell, y * cell);
                var b = a + new float2(cell, cell);
                var ne = new float2(b.X, a.Y);
                var sw = new float2(a.X, b.Y);
                if (!s.CanGo(x, y, 0)) dl.AddLine(in a, in ne, colour, thick);
                if (!s.CanGo(x, y, 1)) dl.AddLine(in ne, in b, colour, thick);
                if (!s.CanGo(x, y, 2)) dl.AddLine(in sw, in b, colour, thick);
                if (!s.CanGo(x, y, 3)) dl.AddLine(in a, in sw, colour, thick);
            }

        // Them: a small light, ringed by how far they see; it flares red when a thought scatters them.
        var me = Mid(MeditateSession.Index(s.X, s.Y));
        dl.AddCircle(in me, sight * cell, Theme.U8(Theme.Arcane, 0.12f), 48, Gui.S(1f));
        dl.AddCircleFilled(in me, cell * 0.45f, Theme.U8(s.Scattered ? Theme.Bad : Theme.Text, 0.25f), 24);
        dl.AddCircleFilled(in me, cell * 0.22f, Theme.U8(s.Scattered ? Theme.Bad : Theme.Text), 24);

        Bar("Mana gathered", s.Gained / Math.Max(1f, s.Room), Theme.Arcane, $"{s.Gained:F0}");
        Bar("The sitting", 1f - s.StepsDone / (float)MeditateSession.MaxSteps, Theme.Dim);
        if (s.Scattered)
            TextC(Theme.Bad, "The pain scatters your thoughts.");
        else if (s.Stillness > 0)
            TextC(Theme.Dim, "The stone steadies you.");
        EndWork();
    }
}

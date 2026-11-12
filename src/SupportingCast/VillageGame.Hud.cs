using Brutal.ImGuiApi;
using Narsil.Ui;
using Narsil.Rendering;
using SupportingCast.Sim;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Ecs;
using SupportingCast.Ui;

namespace SupportingCast;

/// <summary>
/// Always-on HUD pieces: the villager bar (animated portraits, health and need warnings), the clock
/// dial with speed controls, message toasts, and a warm tint at dawn and dusk.
/// </summary>
public sealed partial class VillageGame
{

    private long _toastedMessages = -1;

    private float2 Screen => ImGui.GetIO().DisplaySize;

    /// <summary>Village members for the bar: humans first, then dragons, in a stable order.</summary>
    private IEnumerable<Entity> VillageMembers() =>
        (_sim.Round.Phase == Sim.Round.RoundPhase.None ? _sim.Creatures() : HasAvatar ? [_avatar] : [])
            .Where(e => _sim.World.Get<Creature>(e) is { Faction: Faction.Village, Dead: false })
            .OrderBy(e => _sim.World.Get<Creature>(e).Def.Race == Race.Dragon ? 1 : 0)
            .ThenBy(e => e.Index);

    /// <summary>The sprite to show for a creature in UI: its current animation frame, or its static sprite.</summary>
    private Sprite PortraitOf(Entity e)
    {
        if (_renderer.SheetOf(_sim, e) is { } sheet)
            return _renderer.Animator.Portrait(e, sheet).Sprite;
        var c = _sim.World.Get<Creature>(e);
        var stage = c.Stage(_sim.Clock.Tick);
        return Host.Atlas.Get(string.IsNullOrEmpty(stage.Sprite) ? c.Def.Sprite : stage.Sprite);
    }

    /// <summary>
    /// A creature's portrait: its sprite, plus its look's tinted layers on top (a person's skin,
    /// eyes, hair and worn armor; a dragon's colours).
    /// </summary>
    private void DrawPortrait(ImDrawListPtr dl, Entity e, float2 min, float2 box, uint tint = Rgba.White)
    {
        if (_renderer.SheetOf(_sim, e) is not { } sheet || _sim.World.Get<Creature>(e).Def.Race is not (Race.Human or Race.Dragon))
        {
            DrawSprite(dl, PortraitOf(e), min, box, Color8(tint));
            return;
        }
        var frame = _renderer.Animator.Portrait(e, sheet);
        // Fit the whole frame once, then place the base and each (possibly trimmed) layer by its offset.
        var full = sheet.FrameSize;
        var scale = Math.Min(box.X / Math.Max(1, full.X), box.Y / Math.Max(1, full.Y));
        if (MathF.Floor(scale) >= 1 && MathF.Floor(scale) * Math.Max(full.X, full.Y) >= Math.Max(box.X, box.Y) * 0.8f)
            scale = MathF.Floor(scale); // whole pixels when that doesn't waste much of the box
        var origin = min + (box - new float2(full.X, full.Y) * scale) * 0.5f;
        DrawFramePart(dl, frame, origin, scale, Color8(tint));
        foreach (var (layer, color) in _renderer.LookLayers(_sim, e, tint))
            if (Rendering.HumanLook.LayerFrame(sheet, layer, frame) is { } lf)
                DrawFramePart(dl, lf, origin, scale, Color8(color));
    }

    // The kit's sprite drawing (Narsil.Ui.Gui), under the names the HUD code uses.
    private static void DrawFramePart(ImDrawListPtr dl, in SheetFrame f, float2 origin, float scale, ImColor8 tint) => Gui.FramePart(dl, f, origin, scale, tint);

    private static ImColor8 Color8(uint packed) => Gui.Color8(packed);

    private static void DrawSprite(ImDrawListPtr dl, in Sprite s, float2 min, float2 box, ImColor8? tint = null) => Gui.Sprite(dl, s, min, box, tint);

    private void DrawVillagerBar()
    {
        // In Adventure your own card (top left) is you; the bar is for watching a village from above.
        if (HasAvatar)
            return;
        var members = VillageMembers().ToList();
        if (members.Count == 0)
            return;
        _hud.Next(Anchor.Top);
        ImGui.SetNextWindowBgAlpha(0.75f);
        ImGui.Begin("##villagers"u8, Hud.Flags | ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize);
        _hud.Took();
        var dl = ImGui.GetWindowDrawList();
        var w = _sim.World;
        var card = new float2(68, 84);
        for (var k = 0; k < members.Count; k++)
        {
            var e = members[k];
            if (k > 0)
                ImGui.SameLine(0, 6);
            var c = w.Get<Creature>(e);
            var mind = w.Get<Mind>(e);
            var min = ImGui.GetCursorScreenPos();
            ImGui.PushID(e.Index);
            var clicked = ImGui.InvisibleButton("card"u8, in card, ImGuiButtonFlags.None);
            var hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.None);
            ImGui.PopID();
            var max = min + card;

            var bg = _selected == e ? Theme.U8(Theme.AccentSoft, 0.9f) : hovered ? Theme.U8(Theme.Panel, 0.95f) : Theme.U8(Theme.Well, 0.86f);
            dl.AddRectFilled(in min, in max, bg, 6f, ImDrawFlags.None);
            if (_selected == e)
                dl.AddRect(in min, in max, Theme.U8(Theme.Accent), 6f, ImDrawFlags.None, 2f);

            var tint = c.Downed ? Rgba.Pack(1f, 150 / 255f, 150 / 255f) : mind.Asleep ? Rgba.Pack(170 / 255f, 175 / 255f, 210 / 255f) : Rgba.White;
            DrawPortrait(dl, e, min + new float2(6, 4), new float2(card.X - 12, 50), tint);

            var name = c.Name.Length > 9 ? c.Name[..8] + "…" : c.Name;
            var tw = ImGui.CalcTextSize(name, false, -1).X;
            var tp = new float2(min.X + (card.X - tw) * 0.5f, min.Y + 55);
            dl.AddText(in tp, Theme.U8(c.Def.Race == Race.Dragon ? new float4(1f, 0.75f, 0.55f, 1f) : Theme.Text), name);

            var health = w.TryGet<Health>(e, out var h) ? Math.Min(c.Hp / c.Def.MaxHp, h.Condition) : c.Hp / c.Def.MaxHp;
            var b0 = new float2(min.X + 6, max.Y - 7);
            var b1 = new float2(max.X - 6, max.Y - 4);
            dl.AddRectFilled(in b0, in b1, new ImColor8(0, 0, 0, 160), 2f, ImDrawFlags.None);
            var b2 = new float2(b0.X + (b1.X - b0.X) * Math.Clamp(health, 0, 1), b1.Y);
            dl.AddRectFilled(in b0, in b2, Theme.U8(health < 0.4f ? Theme.Bad : health < 0.75f ? Theme.Warn : Theme.Good), 2f, ImDrawFlags.None);

            // Status badges: top-right what they're in (asleep, down, drafted), top-left a need alarm.
            var badge = c.Downed ? "ui/icon_doctor" : mind.Asleep ? "ui/icon_sleep" : mind.Drafted ? "ui/icon_fight" : null;
            if (badge != null)
                DrawSprite(dl, Host.Atlas.Get(badge), new float2(max.X - 20, min.Y + 2), new float2(18, 18));
            var urgent = UrgentNeed(e);
            if (urgent != null)
                DrawSprite(dl, Host.Atlas.Get("ui/icon_alert"), new float2(min.X + 2, min.Y + 2), new float2(18, 18));

            if (hovered)
                Tip($"{c.Name}: {mind.Status}" + (urgent != null ? $"\nNeeds {urgent.Label.ToLowerInvariant()}!" : "") + "\nClick to select, double-click to find");
            if (clicked)
            {
                _selected = e;
                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
                    _camera.Target = w.Get<Position>(e).Pos;
            }
        }
        ImGui.End();
    }

    /// <summary>The most pressing need below its urgent threshold, if any.</summary>
    private NeedDef? UrgentNeed(Entity e)
    {
        var c = _sim.World.Get<Creature>(e);
        var needs = _sim.World.Get<Needs>(e);
        NeedDef? worst = null;
        var worstValue = float.MaxValue;
        foreach (var rate in c.Def.Needs)
        {
            var v = needs.Get(rate.NeedDef);
            if (v < rate.NeedDef.UrgentBelow && v < worstValue)
            {
                worst = rate.NeedDef;
                worstValue = v;
            }
        }
        return worst;
    }

    private static readonly string[] SpeedLabels = ["||", ">", ">>", ">>>"];

    /// <summary>A speed button drawn as a glyph: two bars for pause, one to three arrowheads for the speeds.</summary>
    private static bool SpeedButton(int speed, bool active)
    {
        float2? size = Gui.S(34, 24);
        if (active)
        {
            var c = Theme.AccentSoft;
            ImGui.PushStyleColor(ImGuiCol.Button, in c);
        }
        var pressed = ImGui.Button($"##speed{speed}", in size);
        if (active)
            ImGui.PopStyleColor(1);
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetItemRectMin();
        var mid = min + Gui.S(34, 24) * 0.5f;
        var col = Theme.U8(active ? Theme.Text : Theme.Dim);
        var h = Gui.S(6);
        if (speed == 0)
        {
            foreach (var dx in new[] { -Gui.S(4), Gui.S(1.5f) })
            {
                var a = new float2(mid.X + dx, mid.Y - h);
                var b = new float2(mid.X + dx + Gui.S(2.5f), mid.Y + h);
                dl.AddRectFilled(in a, in b, col, 0f, ImDrawFlags.None);
            }
            return pressed;
        }
        var w = Gui.S(6);
        var start = mid.X - w * speed * 0.5f;
        for (var i = 0; i < speed; i++)
        {
            var p1 = new float2(start + i * w, mid.Y - h);
            var p2 = new float2(start + i * w + w, mid.Y);
            var p3 = new float2(start + i * w, mid.Y + h);
            dl.AddTriangleFilled(in p1, in p2, in p3, col);
        }
        return pressed;
    }

    /// <summary>Top-right: a day/night dial, the time, outdoor temperature and speed buttons.</summary>
    private void DrawClock()
    {
        _hud.Next(Anchor.TopRight, HasAvatar && InRound ? Gui.S(JournalWidth) : 0f);
        Panel.Begin("##clock", Hud.Flags | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoNav);
        _hud.Took();

        var dl = ImGui.GetWindowDrawList();
        var origin = ImGui.GetCursorScreenPos();
        var r = Gui.S(24f);
        var center = origin + new float2(r + 2, r + 2);
        var day = _sim.Clock.Daylight;
        var sky = Lerp(new float4(0.08f, 0.1f, 0.25f, 1f), new float4(0.45f, 0.7f, 0.95f, 1f), day);
        dl.AddCircleFilled(in center, r, Theme.U8(sky), 32);
        // Ground: the lower half of the dial, below the horizon.
        dl.PathArcTo(in center, r - 0.5f, 0f, MathF.PI, 24);
        dl.PathFillConvex(new ImColor8(60, 46, 34, 255));
        // Sun by day, moon by night, circling once a day (noon at the top).
        var angle = (_sim.Clock.HourOfDay / 24f) * MathF.Tau + MathF.PI / 2f;
        var body = center + new float2(MathF.Cos(angle), MathF.Sin(angle)) * (r * 0.68f);
        var isDay = _sim.Clock.HourOfDay is >= 6f and < 18.5f;
        dl.AddCircleFilled(in body, Gui.S(5.5f), isDay ? new ImColor8(255, 214, 90, 255) : new ImColor8(225, 225, 240, 255), 16);
        dl.AddCircle(in center, r, Theme.U8(Theme.Iron), 32, Gui.S(2.5f));
        var dummy = new float2(r * 2 + 4, r * 2 + 4);
        ImGui.Dummy(in dummy);

        ImGui.SameLine(0, 10);
        ImGui.BeginGroup();
        using (Gui.HeaderFont())
            ImGui.Text(_sim.Clock.TimeString());
        var outdoor = _sim.Climate.OutdoorTemperature;
        TextC(outdoor < 5 ? Theme.Warn : Theme.Dim, $"Outside {outdoor:F0}°C");
        // Time runs at one speed for whoever plays a character; only god mode and the demos change it.
        if (!HasAvatar)
            for (var s = 0; s < SpeedLabels.Length; s++)
            {
                if (s > 0)
                    ImGui.SameLine(0, Gui.S(4));
                if (SpeedButton(s, _speed == s))
                    SetSpeed(s);
                Gui.ItemTip(s == 0 ? "Pause [Space]" : $"Speed {s} [Ctrl+{s}]");
            }
        ImGui.EndGroup();
        if (HasAvatar)
        {
            Gui.Rule();
            DrawJournalStrip();
        }
        Panel.End();

        static float4 Lerp(float4 a, float4 b, float t) => a + (b - a) * Math.Clamp(t, 0, 1);
    }

    /// <summary>New messages slide in at the top of the screen for a few seconds; click one to look at it.</summary>
    private void DrawToasts()
    {
        var messages = _sim.Messages.Messages;
        if (_toastedMessages < 0)
            _toastedMessages = _sim.Messages.Total; // don't replay setup history
        for (var i = _sim.Messages.IndexAfter(_toastedMessages); i < messages.Count; i++)
            if (Witnessed(messages[i]))
                Toast(messages[i]);
        _toastedMessages = _sim.Messages.Total;
        _toastStack.Draw(_hud, Anchor.Top, _time);
    }

    private void Toast(Message m)
    {
        var icon = m.Kind switch
        {
            MessageKind.Danger or MessageKind.Warning => "ui/icon_alert",
            MessageKind.Good => "ui/icon_good",
            _ => null,
        };
        Action? look = m.Location is { } loc ? () => _camera.Target = new float3(loc.X + 0.5f, loc.Y + 0.5f, loc.Z) : null;
        _toastStack.Push(m.Text, KindColor(m.Kind), icon, _time, look);
    }

    private static float4 KindColor(MessageKind kind) => kind switch
    {
        MessageKind.Good => Theme.Good,
        MessageKind.Warning => Theme.Warn,
        MessageKind.Danger => Theme.Bad,
        _ => Theme.Dim,
    };

    /// <summary>A light warm wash over the world at dawn and dusk, on top of the sim's night lighting.</summary>
    private void DrawDayTint()
    {
        var h = _sim.Clock.HourOfDay;
        float Bump(float center, float width) => Math.Max(0f, 1f - MathF.Abs(h - center) / width);
        var dawn = Bump(6.2f, 1.4f);
        var dusk = Bump(18.6f, 1.6f);
        var a = Math.Max(dawn, dusk) * 0.13f;
        if (a < 0.005f || !_renderer.ShowDarkness)
            return;
        var color = dawn > dusk ? new float4(1f, 0.72f, 0.45f, 1f) : new float4(1f, 0.5f, 0.32f, 1f);
        var dl = ImGui.GetBackgroundDrawList(ImGui.GetMainViewport());
        var min = float2.Zero;
        var max = Screen;
        dl.AddRectFilled(in min, in max, Theme.U8(color, a), 0f, ImDrawFlags.None);
    }
}

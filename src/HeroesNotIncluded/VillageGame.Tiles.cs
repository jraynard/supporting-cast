using Brutal.ImGuiApi;
using Narsil.Rendering;
using Narsil.Ui;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

/// <summary>
/// The shared look of the game's windows: tiles (an icon in a frame, with a count and a corner
/// mark) for items, ingredients and recipes, coin chips for prices, and compact cards for orders
/// and favours. Names and prices stay in sight; what explains them goes in the tooltip.
/// </summary>
public sealed partial class VillageGame
{
    /// <summary>Space between tiles in a row, before UI scale.</summary>
    private const float TileGap = 4;

    /// <summary>A tile's whole width at the given icon size: the icon and its padding.</summary>
    private static float TileWidth(float iconSize = IconSize) => Gui.S(iconSize) + 2 * Gui.S(5);

    /// <summary>How many tiles of this size fit across what's left of the line.</summary>
    private static int TilesAcross(float iconSize = IconSize)
    {
        var gap = Gui.S(TileGap);
        return Math.Max(1, (int)((ImGui.GetContentRegionAvail().X + gap) / (TileWidth(iconSize) + gap)));
    }

    /// <summary>Before the k-th tile of a wrapping row: on the same line unless the row's full.</summary>
    private static void TileFlow(int k, int across)
    {
        if (k % across != 0)
            ImGui.SameLine(0, Gui.S(TileGap));
    }

    /// <summary>
    /// A tile's frame, icon, count (bottom right, in <paramref name="countColor"/>) and corner
    /// label (top left) drawn at the cursor without taking the space: the caller lays a button of
    /// the returned size over it. <paramref name="lit"/> outlines it as chosen; <paramref name="dim"/>
    /// washes it dark. With no sprite, <paramref name="glyph"/> stands in the middle (a "?").
    /// </summary>
    private static float2 DrawTile(Sprite? sprite, ImColor8 tint, string count, float4 countColor, string corner,
        bool dim = false, bool lit = false, float iconSize = IconSize, string glyph = "")
    {
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var icon = Gui.S(iconSize);
        var pad = Gui.S(5);
        var box = new float2(icon + 2 * pad, icon + 2 * pad);
        var max = min + box;
        dl.AddRectFilled(in min, in max, lit ? Theme.U8(Theme.AccentSoft, 0.6f) : Theme.U8(new float4(0, 0, 0, 0.35f)), Gui.S(4), ImDrawFlags.None);
        dl.AddRect(in min, in max, Theme.U8(lit ? Theme.Accent : Theme.Dim), Gui.S(4), ImDrawFlags.None, lit ? Gui.S(2) : 1f);
        if (sprite is { } s)
            DrawSprite(dl, s, min + new float2(pad, pad), new float2(icon, icon), tint);
        else if (glyph.Length > 0)
        {
            var gs = ImGui.CalcTextSize(glyph, false, -1f);
            var gp = min + (box - gs) * 0.5f;
            dl.AddText(in gp, Theme.U8(Theme.Dim), glyph);
        }
        if (count.Length > 0)
        {
            var size = ImGui.CalcTextSize(count, false, -1f);
            var at = max - new float2(Gui.S(4) + size.X, size.Y + Gui.S(2));
            var shadow = at + new float2(1, 1);
            dl.AddText(in shadow, new ImColor8(0, 0, 0, 220), count);
            dl.AddText(in at, Theme.U8(countColor), count);
        }
        var c = min + new float2(Gui.S(3), Gui.S(1));
        dl.AddText(in c, Theme.U8(Theme.Dim), corner.Length <= 2 ? corner : "");
        if (dim)
            dl.AddRectFilled(in min, in max, Theme.U8(new float4(0, 0, 0, 0.55f)), Gui.S(4), ImDrawFlags.None);
        return box;
    }

    /// <summary>A def's picture: its icon if it has one, else its sprite (null if neither is in the atlas).</summary>
    private static Sprite? DefSprite(ThingDef? def)
    {
        if (def == null)
            return null;
        var name = def.Icon is { Length: > 0 } icon ? icon : def.Sprite;
        return name is { Length: > 0 } && Gui.TrySprite(name, out var s) ? s : null;
    }

    /// <summary>
    /// A tile for a kind of thing rather than one item: a recipe's product, an ingredient. With a
    /// <paramref name="need"/> its corner shows "have/need", red while short. A null def is an
    /// unknown "?". A button (the pad lands on it); the tooltip is <paramref name="tip"/>. True when pressed.
    /// </summary>
    private static bool DefTile(string id, ThingDef? def, string tip, int have = 0, int need = 0, bool selected = false,
        bool dim = false, bool enabled = true, float iconSize = IconSize)
    {
        var count = need > 0 ? $"{Math.Min(have, 999)}/{need}" : "";
        var shortOf = need > 0 && have < need;
        var box = DrawTile(DefSprite(def), Gui.White, count, shortOf ? Theme.Bad : Theme.Good, "", dim, selected, iconSize, def == null ? "?" : "");
        var flags = ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.EnableNav;
        if (!enabled)
            ImGui.BeginDisabled(true);
        var pressed = ImGui.InvisibleButton($"##tile-{id}", in box, flags);
        if (!enabled)
            ImGui.EndDisabled();
        if (tip.Length > 0 && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            Tip(tip);
        return pressed && enabled;
    }

    /// <summary>
    /// What a recipe takes, as a row of small tiles, each "have/need" (red while short), with the
    /// thing's name in its tooltip. <paramref name="have"/> counts what's to hand for an ingredient.
    /// </summary>
    private static void IngredientTiles(string id, IReadOnlyList<ThingCount> ingredients, Func<ThingCount, int> have, float iconSize = 28)
    {
        var across = TilesAcross(iconSize);
        for (var i = 0; i < ingredients.Count; i++)
        {
            var x = ingredients[i];
            var h = have(x);
            TileFlow(i, across);
            DefTile($"{id}-in{i}", x.ThingDef, $"{x.Count} {x.ThingDef.Label}: {(h >= x.Count ? "to hand" : $"{h} to hand")}",
                h, x.Count, iconSize: iconSize);
        }
    }

    /// <summary>A price's width as <see cref="CoinChip"/> draws it.</summary>
    private static float CoinChipWidth(int silver) => ImGui.GetTextLineHeight() + Gui.S(3) + ImGui.CalcTextSize(silver.ToString(), false, -1f).X;

    /// <summary>A price: a silver coin and the number, the way the shop signs show it.</summary>
    private static void CoinChip(int silver, float4 color)
    {
        var line = ImGui.GetTextLineHeight();
        var at = ImGui.GetCursorScreenPos();
        if (Gui.TrySprite("ui/icon_coin", out var coin))
            DrawSprite(ImGui.GetWindowDrawList(), coin, at, new float2(line, line), color.W < 1f || color.Equals(Theme.Dim) ? Theme.U8(Theme.Dim) : Gui.White);
        ImGui.Dummy(new float2(line, line));
        ImGui.SameLine(0, Gui.S(3));
        TextC(color, silver.ToString());
    }

    /// <summary>Where the card being drawn began, and how tall its icon stands (it fills at least that).</summary>
    private float2 _cardTop;
    private float _cardIcon;

    /// <summary>
    /// A card's first line: an icon down its left side (as tall as two lines while it's
    /// <paramref name="open"/>, one when it's done with), the title, and a price at the right
    /// (negative: none). Hovering the line shows <paramref name="tip"/>: the words, the details.
    /// What goes under it (a bar, a button) is indented past the icon; end with <see cref="CardEnd"/>.
    /// </summary>
    private void CardHead(string icon, bool open, float4 titleColor, string title, int price, string tip)
    {
        var top = ImGui.GetCursorScreenPos();
        var size = open ? ImGui.GetTextLineHeightWithSpacing() + ImGui.GetFrameHeight() : ImGui.GetTextLineHeight();
        if (Gui.TrySprite(icon, out var sprite))
            DrawSprite(ImGui.GetWindowDrawList(), sprite, top, new float2(size, size), open ? Gui.White : Theme.U8(Theme.Dim));
        _cardTop = top;
        _cardIcon = size;
        var indent = size + Gui.S(8);
        ImGui.Indent(indent);
        var lineX = ImGui.GetCursorPosX();
        var avail = ImGui.GetContentRegionAvail().X;
        ImGui.BeginGroup();
        var chip = price >= 0 ? CoinChipWidth(price) : 0f;
        var room = avail - chip - Gui.S(8);
        TextC(titleColor, Narsil.Ui.Gui.Fit(title, room));
        if (price >= 0)
        {
            ImGui.SameLine(lineX + avail - chip, 0);
            CoinChip(price, open ? Theme.Warn : Theme.Dim);
        }
        ImGui.EndGroup();
        if (tip.Length > 0 && ImGui.IsItemHovered(ImGuiHoveredFlags.None))
            Tip(tip);
    }

    /// <summary>Close a card begun with <see cref="CardHead"/>: past the icon's foot, a little space.</summary>
    private void CardEnd()
    {
        ImGui.Unindent(_cardIcon + Gui.S(8));
        var foot = _cardTop.Y + _cardIcon - ImGui.GetCursorScreenPos().Y;
        if (foot > 0)
            ImGui.Dummy(new float2(1, foot));
        ImGui.Dummy(new float2(1, Gui.S(4)));
    }

    /// <summary>
    /// A card's time-left bar, its fill coloured by how short time is, filling the line but for
    /// <paramref name="keep"/> pixels left for a button after it. Hovering it says how long's left.
    /// </summary>
    private static void CardBar(string words, float share, float4 color, string timeWords, float keep = 0f)
    {
        var width = Math.Max(Gui.S(80), ImGui.GetContentRegionAvail().X - keep) / Gui.S(1);
        Gui.Bar("", share, color, words, width);
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
            Tip(Cap(timeWords) + ".");
    }

    /// <summary>A small button's width for a label, to leave room for it after a bar.</summary>
    private static float SmallButtonWidth(string label) => ImGui.CalcTextSize(label, true, -1f).X + ImGui.GetStyle().FramePadding.X * 2 + Gui.S(8);
}

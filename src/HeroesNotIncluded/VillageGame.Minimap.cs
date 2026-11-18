using Brutal.ImGuiApi;
using Brutal.Numerics;
using Narsil.Ui;
using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Ecs;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

/// <summary>
/// The minimap in the bottom right and the town map on M. Playtesters found the edge arrows alone
/// weren't enough to find the way, so this shows the town round you, turned as the camera is,
/// with a pin on everything the arrows point at. It shows only the layout (common knowledge:
/// <c>PerceptionView.CanSee</c> always sees buildings), you, and those pins, which the game fills
/// from Perception each frame. Nobody else is drawn on it, so it's a way to find things, not a radar.
/// </summary>
public sealed partial class VillageGame
{
    private const float MinimapSize = 150f;

    /// <summary>How many cells from you to the minimap's rim.</summary>
    private const float MinimapReach = 26f;

    private bool _showTownMap;

    private ImColor8[]? _mapColours;
    private Sim.Maps.Map? _mapColoursOf;
    private int _mapColoursVersion = -1;

    /// <summary>Whose place on the map is "you": your character, or your ghost.</summary>
    private Entity MapSelf => HasAvatar ? _avatar : Ghosting ? _ghost : Entity.Null;

    private bool MapShown => !_cameraMode && !MapSelf.IsNull;

    private void ToggleTownMap()
    {
        if (MapShown)
            _showTownMap = !_showTownMap;
    }

    private void DrawMinimap()
    {
        if (!MapShown || Frame() is not { } frame)
            return;
        _hud.Next(Anchor.BottomRight);
        Panel.Begin("##minimap", Hud.Flags | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoNav
            | ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoScrollbar);
        _hud.Took();
        var size = Gui.S(MinimapSize, MinimapSize);
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(in size);
        var dl = ImGui.GetWindowDrawList();
        var centre = min + size * 0.5f;
        var r = size.X * 0.5f;
        var self = _sim.World.Get<Position>(MapSelf).Pos;
        var view = new MapView(centre, new float2(self.X, self.Y), r / MinimapReach, frame.North, frame.East);

        dl.AddCircleFilled(in centre, r, Theme.U8(Theme.Inset), 48);
        DrawCells(dl, view, MinimapReach);
        DrawPins(dl, view, r - Gui.S(7), labels: false);
        DrawSelf(dl, view, self);
        dl.AddCircle(in centre, r, Theme.U8(Theme.OakLight), 48, Gui.S(2f));
        var n = centre + frame.North * (r - Gui.S(8));
        MapText(dl, n, "N", Theme.Accent, true);
        Panel.End();
    }

    private void DrawTownMap()
    {
        if (!_showTownMap)
            return;
        if (!MapShown || Frame() is not { } frame)
        {
            _showTownMap = false;
            return;
        }
        var display = ImGui.GetIO().DisplaySize;
        var side = MathF.Min(display.X, display.Y) * 0.78f;
        var middle = display * 0.5f;
        float2? pivot = new float2(0.5f, 0.5f);
        ImGui.SetNextWindowPos(in middle, ImGuiCond.Always, in pivot);
        if (Panel.Begin("Town map###townmap", ref _showTownMap, Hud.Flags | ImGuiWindowFlags.AlwaysAutoResize
            | ImGuiWindowFlags.NoScrollbar))
        {
            var size = new float2(side, side);
            var min = ImGui.GetCursorScreenPos();
            ImGui.Dummy(in size);
            var dl = ImGui.GetWindowDrawList();
            var map = _sim.Map;
            // The whole town fits however it's turned: scale by its diagonal.
            var diag = MathF.Sqrt(map.Width * map.Width + map.Height * map.Height);
            var view = new MapView(min + size * 0.5f, new float2(map.Width * 0.5f, map.Height * 0.5f), side / diag, frame.North, frame.East);
            dl.AddRectFilled(in min, min + size, Theme.U8(Theme.Inset), Gui.S(4), ImDrawFlags.None);
            DrawCells(dl, view, diag);
            DrawBuildingNames(dl, view);
            DrawPins(dl, view, null, labels: true);
            DrawSelf(dl, view, _sim.World.Get<Position>(MapSelf).Pos);
            MapText(dl, min + Gui.S(14, 14) + frame.North * Gui.S(8), "N", Theme.Accent, true);
            var hint = $"{Controls.Glyph(Act.TownMap)} to close";
            var hintSize = ImGui.CalcTextSize(hint);
            var hintAt = min + size - hintSize - Gui.S(8, 6);
            dl.AddText(in hintAt, Theme.U8(Theme.Dim), hint);
        }
        Panel.End();
    }

    /// <summary>Which way the map's north and east are on screen: as the camera's, kept square so the town isn't skewed.</summary>
    private readonly record struct MapFrame(float2 North, float2 East);

    private MapFrame? Frame()
    {
        var from = _camera.WorldToScreen(_camera.Target);
        var d = _camera.WorldToScreen(_camera.Target + new float3(0, -1, 0)) - from;
        var len = MathF.Sqrt(d.X * d.X + d.Y * d.Y);
        if (len < 1e-4f)
            return null;
        var north = d * (1f / len);
        return new MapFrame(north, new float2(-north.Y, north.X));
    }

    /// <summary>Map cells (x, y) to the screen: <see cref="Centre"/> on screen is <see cref="Focus"/> on the map.</summary>
    private readonly record struct MapView(float2 Centre, float2 Focus, float Scale, float2 North, float2 East)
    {
        public float2 ToScreen(float x, float y) => Centre + Along(x - Focus.X, y - Focus.Y);

        /// <summary>A step on the map, on screen (+y on the map is south).</summary>
        public float2 Along(float dx, float dy) => (East * dx - North * dy) * Scale;
    }

    private void DrawCells(ImDrawListPtr dl, MapView view, float reach)
    {
        var map = _sim.Map;
        var colours = MapColours();
        var reach2 = reach * reach;
        var x0 = Math.Max(0, (int)MathF.Floor(view.Focus.X - reach));
        var x1 = Math.Min(map.Width - 1, (int)MathF.Ceiling(view.Focus.X + reach));
        var y0 = Math.Max(0, (int)MathF.Floor(view.Focus.Y - reach));
        var y1 = Math.Min(map.Height - 1, (int)MathF.Ceiling(view.Focus.Y + reach));
        // A cell's square, a hair over so neighbours meet without seams.
        const float pad = 0.04f;
        for (var y = y0; y <= y1; y++)
        for (var x = x0; x <= x1; x++)
        {
            var dx = x + 0.5f - view.Focus.X;
            var dy = y + 0.5f - view.Focus.Y;
            if (dx * dx + dy * dy > reach2)
                continue;
            var a = view.ToScreen(x - pad, y - pad);
            var b = view.ToScreen(x + 1 + pad, y - pad);
            var c = view.ToScreen(x + 1 + pad, y + 1 + pad);
            var d = view.ToScreen(x - pad, y + 1 + pad);
            dl.AddQuadFilled(in a, in b, in c, in d, colours[y * map.Width + x]);
        }
    }

    /// <summary>
    /// The pins: what the edge arrows point at (accent), the other marks (chores, the festival,
    /// signs) dimmer, and your home. With a <paramref name="rim"/>, a pin beyond it sits on the rim.
    /// </summary>
    private void DrawPins(ImDrawListPtr dl, MapView view, float? rim, bool labels)
    {
        // Hovered (the mouse, or the pad's pointer), a pin or a rim arrow says what it is: the nearest under the pointer.
        var mouse = ImGui.GetIO().MousePos;
        var (nearest, best) = ((string?)null, Gui.S(10) * Gui.S(10));
        void Hover(float2 at, string text)
        {
            var d = at - mouse;
            var d2 = d.X * d.X + d.Y * d.Y;
            if (d2 <= best && text.Length > 0)
                (nearest, best) = (text, d2);
        }
        if (_renderer.HomeMark is { } home)
            Hover(Pin(dl, view, home, rim, Theme.Barrel, house: true, null), "Your home");
        foreach (var m in _renderer.Markers)
            if (!m.Done)
                Hover(Pin(dl, view, m.At, rim, Theme.Parchment, house: false, labels ? m.Text : null), m.Text);
        foreach (var p in _renderer.Pointers)
            Hover(Pin(dl, view, p.At, rim, Theme.Accent, house: false, labels ? p.Text : null), p.Text);
        if (nearest != null)
            Tip(nearest);
    }

    /// <summary>One pin, or its arrow on the <paramref name="rim"/>; returns where it was drawn, for hovering.</summary>
    private static float2 Pin(ImDrawListPtr dl, MapView view, float3 at, float? rim, float4 colour, bool house, string? label)
    {
        var p = view.ToScreen(at.X, at.Y);
        var off = p - view.Centre;
        var dist = MathF.Sqrt(off.X * off.X + off.Y * off.Y);
        var dark = Theme.U8(Theme.Cellar);
        if (rim is { } edge && dist > edge)
        {
            // Off the map: an arrowhead on the rim, pointing the way.
            var dir = off * (1f / dist);
            var side = new float2(-dir.Y, dir.X);
            var tip = view.Centre + dir * (edge + Gui.S(4));
            var a = tip - dir * Gui.S(9) + side * Gui.S(5);
            var b = tip - dir * Gui.S(9) - side * Gui.S(5);
            dl.AddTriangleFilled(in tip, in a, in b, Theme.U8(colour));
            dl.AddTriangle(in tip, in a, in b, dark, Gui.S(1));
            var back = tip - dir * Gui.S(6);
            // Home's arrow carries the little house just inside it, so it's never taken for an errand's.
            if (house)
                House(dl, back - dir * Gui.S(8), colour, 0.7f);
            return back;
        }
        if (house)
            House(dl, p, colour, 1f);
        else
        {
            dl.AddCircleFilled(in p, Gui.S(4.5f), Theme.U8(colour), 12);
            dl.AddCircle(in p, Gui.S(4.5f), dark, 12, Gui.S(1.2f));
        }
        if (label != null)
            MapText(dl, p + new float2(Gui.S(8), -ImGui.GetTextLineHeight() * 0.5f), label, colour, false);
        return p;
    }

    /// <summary>A little house: a square and its roof, at <paramref name="p"/>.</summary>
    private static void House(ImDrawListPtr dl, float2 p, float4 colour, float scale)
    {
        var s = Gui.S(4.5f) * scale;
        var lo = p + new float2(-s, -s * 0.2f);
        var hi = p + new float2(s, s * 1.4f);
        dl.AddRectFilled(in lo, in hi, Theme.U8(colour), 0, ImDrawFlags.None);
        var ridge = p + new float2(0, -s * 1.4f);
        var l = p + new float2(-s * 1.4f, -s * 0.2f);
        var r = p + new float2(s * 1.4f, -s * 0.2f);
        dl.AddTriangleFilled(in ridge, in r, in l, Theme.U8(colour));
    }

    /// <summary>You: an arrowhead the way you face.</summary>
    private void DrawSelf(ImDrawListPtr dl, MapView view, float3 at)
    {
        var p = view.ToScreen(at.X, at.Y);
        var aim = _sim.World.TryGet<FreeMover>(MapSelf, out var m) ? m.Aim : 0f;
        var step = view.Along(MathF.Cos(aim), MathF.Sin(aim));
        var len = MathF.Sqrt(step.X * step.X + step.Y * step.Y);
        var dir = len > 1e-5f ? step * (1f / len) : -view.North;
        var side = new float2(-dir.Y, dir.X);
        var s = Gui.S(7);
        var tip = p + dir * s;
        var a = p - dir * (s * 0.7f) + side * (s * 0.7f);
        var b = p - dir * (s * 0.7f) - side * (s * 0.7f);
        var notch = p - dir * (s * 0.3f);
        var colour = Ghosting ? Theme.Arcane : Theme.Text;
        dl.AddTriangleFilled(in tip, in a, in notch, Theme.U8(colour));
        dl.AddTriangleFilled(in tip, in notch, in b, Theme.U8(colour));
        dl.AddTriangle(in tip, in a, in b, Theme.U8(Theme.Cellar), Gui.S(1.2f));
    }

    /// <summary>On the town map, the town's named places (the inn, the forge, the shrine): what you'd look up.</summary>
    private void DrawBuildingNames(ImDrawListPtr dl, MapView view)
    {
        var w = _sim.World;
        var map = _sim.Map;
        var placed = new List<(float2 Lo, float2 Hi)>();
        foreach (var e in w.Each<PresetBuilding>())
        {
            var b = w.Get<PresetBuilding>(e);
            if (b.Phase != BuildPhase.Done || b.Demolishing)
                continue;
            var name = ShopName.Of(_sim, e) ?? (b.Def.RoleFlags != TownRole.None ? Buildings.Called(_sim, e) : null);
            if (string.IsNullOrEmpty(name))
                continue;
            var c0 = Buildings.CellOf(map, b.Def, b.Anchor, b.Facing, 0, 0);
            var c1 = Buildings.CellOf(map, b.Def, b.Anchor, b.Facing, b.Def.Width - 1, b.Def.Depth - 1);
            if (!map.InBounds(c0) || !map.InBounds(c1))
                continue;
            var at = view.ToScreen((c0.X + c1.X) * 0.5f + 0.5f, (c0.Y + c1.Y) * 0.5f + 0.5f);
            var text = char.ToUpperInvariant(name[0]) + name[1..];
            var half = ImGui.CalcTextSize(text) * 0.5f;
            var lo = at - half;
            var hi = at + half;
            if (placed.Any(r => lo.X < r.Hi.X && hi.X > r.Lo.X && lo.Y < r.Hi.Y && hi.Y > r.Lo.Y))
                continue; // one name where two would overlap
            placed.Add((lo, hi));
            MapText(dl, lo, text, Theme.Text, false);
        }
    }

    /// <summary>Text with a dark edge, so it reads over any ground.</summary>
    private static void MapText(ImDrawListPtr dl, float2 at, string text, float4 colour, bool centred)
    {
        if (centred)
            at -= ImGui.CalcTextSize(text) * 0.5f;
        var dark = Theme.U8(Theme.Cellar, 0.9f);
        for (var dy = -1; dy <= 1; dy++)
        for (var dx = -1; dx <= 1; dx++)
            if (dx != 0 || dy != 0)
            {
                var o = at + new float2(dx, dy);
                dl.AddText(in o, dark, text);
            }
        dl.AddText(in at, Theme.U8(colour), text);
    }

    /// <summary>
    /// Each column's colour from above: under a roof, a building; else its top floor or ground,
    /// darker where something stands in the way (a wall, a fence, a tree). Kept till the map's
    /// structure changes (a bridge down, the festival put up).
    /// </summary>
    private ImColor8[] MapColours()
    {
        var map = _sim.Map;
        if (_mapColours != null && _mapColoursOf == map && _mapColoursVersion == map.StructureVersion)
            return _mapColours;
        var colours = _mapColours is { } old && _mapColoursOf == map ? old : new ImColor8[map.Width * map.Height];
        for (var y = 0; y < map.Height; y++)
        for (var x = 0; x < map.Width; x++)
            colours[y * map.Width + x] = Theme.U8(ColumnColour(map, x, y));
        (_mapColours, _mapColoursOf, _mapColoursVersion) = (colours, map, map.StructureVersion);
        return colours;
    }

    private static float4 ColumnColour(Sim.Maps.Map map, int x, int y)
    {
        for (var z = map.Depth - 1; z > 0; z--)
            if (map.HasRoof[map.Idx(x, y, z)])
                return MapRoof;
        for (var z = map.Depth - 1; z > 0; z--)
        {
            var i = map.Idx(x, y, z);
            var floor = map.HasFloor[i] && !map.IsSolid(i);
            if (!floor && !map.IsSolid(i))
                continue;
            var block = map.Block[i];
            var colour = floor ? MapPlanks : Ground(block);
            var above = z + 1 < map.Depth ? map.Idx(x, y, z + 1) : -1;
            if (!block.IsWater && above >= 0 && !map.IsSolid(above) && map.PathCost[above] <= 0)
                colour = new float4(colour.X * 0.62f, colour.Y * 0.62f, colour.Z * 0.62f, 1f);
            return colour;
        }
        return Theme.Inset;
    }

    private static float4 Ground(BlockDef block) => block.IsWater
        ? (block.MoveCost <= 0 ? MapDeepWater : MapShallows)
        : block.Name switch
        {
            "Grass" => MapGrass,
            "Road" => MapRoad,
            "Dirt" or "TilledSoil" or "Clay" => MapDirt,
            "Sand" => MapSand,
            _ => MapStone,
        };

    // Muted ground tones, under the brand's warm browns; the pins carry the bright colours.
    private static readonly float4 MapGrass = new(0.31f, 0.4f, 0.23f, 1f);
    private static readonly float4 MapRoad = new(0.6f, 0.49f, 0.33f, 1f);
    private static readonly float4 MapDirt = new(0.42f, 0.32f, 0.21f, 1f);
    private static readonly float4 MapSand = new(0.7f, 0.62f, 0.44f, 1f);
    private static readonly float4 MapStone = new(0.44f, 0.42f, 0.39f, 1f);
    private static readonly float4 MapShallows = new(0.3f, 0.46f, 0.56f, 1f);
    private static readonly float4 MapDeepWater = new(0.17f, 0.29f, 0.42f, 1f);
    private static readonly float4 MapPlanks = new(0.5f, 0.36f, 0.22f, 1f);
    private static readonly float4 MapRoof = new(0.22f, 0.14f, 0.1f, 1f);
}

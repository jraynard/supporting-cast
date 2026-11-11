using Brutal.ImGuiApi;
using Narsil.Platform;
using DragonWorld.Sim.Ai;
using DragonWorld.Sim.Components;
using DragonWorld.Sim.Core;
using DragonWorld.Sim.Defs;
using DragonWorld.Sim.Ecs;
using DragonWorld.Sim.Maps;
using DragonWorld.Sim.Scenarios;
using DragonWorld.Sim.Systems;
using DragonWorld.Ui;

namespace DragonWorld;

/// <summary>
/// The bottom toolbar, which replaced the old text-button "Architect" window: category tabs
/// (Orders / Build / Zones), then a row of icon buttons. Each button has a hotkey letter for the
/// open tab, and Tab cycles the tabs. Buildings use their def's <c>icon</c>, else their sprite.
/// In Adventure mode, once you have a dragon, its abilities sit at the right end whatever the tab:
/// its portrait, Ride/Dismount (R), Breathe fire (B) and, mounted, the fly toggle.
/// </summary>
public sealed partial class VillageGame
{
    private enum ToolTab
    {
        Orders,

        /// <summary>Preset buildings (<see cref="BuildingDef"/>): click to lay one out.</summary>
        Buildings,
        Build,
        Zones,
    }

    private sealed record ToolEntry(string Label, string Icon, ToolKind Kind, ThingDef? Def, GlfwKey Key, string Hint, BuildingDef? Plan = null);

    private ToolTab _toolTab;
    private List<ToolEntry>? _buildEntries;
    private List<ToolEntry>? _planEntries;

    private static readonly ToolEntry[] OrderEntries =
    [
        new("Select", "ui/icon_select", ToolKind.Select, null, GlfwKey.Unknown, "Click to inspect. Esc returns here."),
        new("Cancel", "ui/icon_cancel", ToolKind.Cancel, null, GlfwKey.X, "Drag to cancel orders and blueprints."),
        new("Chop trees", "ui/icon_chop", ToolKind.Chop, null, GlfwKey.C, "Drag over trees to fell them for wood."),
        new("Mine rock", "ui/icon_mine", ToolKind.Mine, null, GlfwKey.M, "Drag over boulders to break them into stone."),
        new("Dig", "ui/icon_dig", ToolKind.Dig, null, GlfwKey.G, "Drag to dig out earth and rock."),
        new("Harvest", "ui/icon_harvest", ToolKind.Harvest, null, GlfwKey.H, "Drag over bushes to pick berries."),
        new("Deconstruct", "ui/icon_deconstruct", ToolKind.Deconstruct, null, GlfwKey.V, "Drag to take buildings apart, top-down."),
        new("Clear (dragon)", "ui/icon_burn", ToolKind.Burn, null, GlfwKey.B,
            "Drag to mark an area for your dragons to clear: a fire dragon sets it alight, a poison dragon withers what grows there."),
        new("Hunt", "ui/icon_hunt", ToolKind.Hunt, null, GlfwKey.U, "Drag over wild animals to hunt them. Hunters need a bow."),
        new("Fish", "ui/icon_fish", ToolKind.Fish, null, GlfwKey.F, "Drag along a shore to mark fishing spots. Each stretch of water fishes out for a while."),
    ];

    private static readonly ToolEntry[] SoloOrderEntries =
    [
        OrderEntries[0],
        OrderEntries[1],
        OrderEntries[6] with { Hint = "Drag to mark buildings to take apart, top-down. Then hold F at each one." },
    ];

    private static readonly ToolEntry[] ZoneEntries =
    [
        new("Stockpile", "ui/icon_stockpile", ToolKind.Stockpile, null, GlfwKey.Z, "Drag to mark storage. Haulers bring items here."),
        new("Clear zone", "ui/icon_clearzone", ToolKind.ClearZone, null, GlfwKey.X, "Drag to remove stockpile zones."),
    ];

    private static readonly GlfwKey[] BuildKeys =
        // Not R: it turns the blueprint being placed.
        [GlfwKey.T, GlfwKey.Y, GlfwKey.U, GlfwKey.I, GlfwKey.O, GlfwKey.P, GlfwKey.G, GlfwKey.H, GlfwKey.J, GlfwKey.K, GlfwKey.L, GlfwKey.N, GlfwKey.M, GlfwKey.B, GlfwKey.V];

    private IReadOnlyList<ToolEntry> Entries(ToolTab tab) => tab switch
    {
        // Alone in Adventure mode there's nobody to give orders to: only inspect, cancel blueprints
        // and mark buildings to take apart yourself.
        ToolTab.Orders => OrdersLocked ? SoloOrderEntries : OrderEntries,
        ToolTab.Zones => ZoneEntries,
        ToolTab.Buildings => _planEntries ??= _sim.Defs.AllBuildings
            .Select((d, i) => new ToolEntry(
                d.Label,
                d.Icon,
                ToolKind.PlaceBuilding,
                null,
                i < BuildKeys.Length ? BuildKeys[i] : GlfwKey.Unknown,
                $"{d.Description}\n{PlanCost(d)}",
                d))
            .ToList(),
        // Shapes of another def (a long table) share their base's button: C cycles them while placing.
        // With preset buildings on, only what may stand outside one, and the extras that go in one.
        _ => _buildEntries ??= _sim.Defs.AllThings
            .Where(d => d.Building is { Buildable: true, PartOnly: false } && d.ShapeOfDef == null
                && (!_sim.Presets.PresetMode || d.Building.Outdoor || _sim.Defs.AllBuildings.Any(b => b.Accepts(d))))
            .Select((d, i) => new ToolEntry(
                d.Label,
                string.IsNullOrEmpty(d.Icon) ? d.Sprite : d.Icon,
                ToolKind.Build,
                d,
                i < BuildKeys.Length ? BuildKeys[i] : GlfwKey.Unknown,
                $"{d.Description}\nCost: {string.Join(", ", d.Building!.Cost.Select(c => $"{c.Count} {c.ThingDef.Label}"))}"))
            .ToList(),
    };

    /// <summary>Hotkeys for the open toolbar tab. Pressing an active tool's key again returns to Select.</summary>
    private void HandleToolHotkeys(Input input)
    {
        if (input.WasKeyPressed(GlfwKey.Tab) && !HasAvatar) // Adventure mode: Tab is the inventory
            _toolTab = (ToolTab)(((int)_toolTab + 1) % Enum.GetValues<ToolTab>().Length);
        foreach (var entry in Entries(_toolTab))
            if (entry.Key != GlfwKey.Unknown && input.WasKeyPressed(entry.Key) && !(HasAvatar && AvatarKeys.Contains(entry.Key)))
                ActivateTool(entry);
    }

    private void ActivateTool(ToolEntry entry)
    {
        var active = IsActive(entry);
        var def = entry.Def != null && _shapeChoice.TryGetValue(entry.Def, out var shape) ? shape : entry.Def;
        SetTool(active ? ToolKind.Select : entry.Kind, active ? null : def);
        _toolPlan = active ? null : entry.Plan;
    }

    /// <summary>What a preset building costs altogether, every phase's parts and furniture summed (the plain look).</summary>
    private string PlanCost(BuildingDef def)
    {
        var plan = new PresetBuilding { Def = def, Anchor = new int3(def.Width, def.Depth, 1) };
        var map = _sim.Map;
        var total = new Dictionary<ThingDef, int>();
        foreach (var phase in Enum.GetValues<BuildPhase>())
            foreach (var st in Buildings.Stamps(map, plan, phase))
                foreach (var c in st.Def.Building!.Cost)
                    total[c.ThingDef] = total.GetValueOrDefault(c.ThingDef) + c.Count;
        return $"Cost: {string.Join(", ", total.Select(kv => $"{kv.Value} {kv.Key.Label}"))} · {def.Width}x{def.Depth}";
    }

    /// <summary>The entry's tool is the one in use (any shape of a building counts as its button).</summary>
    private bool IsActive(ToolEntry entry) =>
        _tool == entry.Kind && (entry.Plan != null ? _toolPlan == entry.Plan : _toolDef == entry.Def || (_toolDef != null && entry.Def != null && _toolDef.IsA(entry.Def)));

    /// <summary>The shape last picked for each building with shapes (a table: small, long, square).</summary>
    private readonly Dictionary<ThingDef, ThingDef> _shapeChoice = [];

    /// <summary>A building and its other shapes, the base first, then in def order.</summary>
    private List<ThingDef> ShapesOf(ThingDef def)
    {
        var root = def.ShapeOfDef ?? def;
        return [root, .. _sim.Defs.AllThings.Where(d => d.ShapeOfDef == root && d.Building is { Buildable: true, PartOnly: false })];
    }

    private void PickShape(ThingDef shape)
    {
        _shapeChoice[shape.ShapeOfDef ?? shape] = shape;
        SetTool(ToolKind.Build, shape);
    }

    /// <summary>C while placing: the next shape of the building (1x1 → 1x2 → 2x2 table).</summary>
    private void CycleShape()
    {
        if (_tool != ToolKind.Build || _toolDef == null)
            return;
        var shapes = ShapesOf(_toolDef);
        if (shapes.Count > 1)
            PickShape(shapes[(shapes.IndexOf(_toolDef) + 1) % shapes.Count]);
    }

    private static string SizeLabel(ThingDef d) => $"{d.SizeX}x{d.SizeY}";

    private static string KeyName(GlfwKey key) => key == GlfwKey.Unknown ? "" : key.ToString();

    private void DrawToolbar()
    {
        var screen = ImGui.GetIO().DisplaySize;
        var pos = new float2(screen.X * 0.5f, screen.Y - 10);
        float2? pivot = new float2(0.5f, 1f);
        ImGui.SetNextWindowPos(in pos, ImGuiCond.Always, in pivot);
        ImGui.Begin("##toolbar"u8, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoFocusOnAppearing);

        // Category tabs stacked on the left, the tab's tools to the right.
        ImGui.BeginGroup();
        foreach (var tab in Enum.GetValues<ToolTab>())
        {
            var selected = _toolTab == tab;
            if (selected)
            {
                var c = Theme.AccentSoft;
                ImGui.PushStyleColor(ImGuiCol.Button, in c);
            }
            var size = (float2?)new float2(76, 0);
            if (ImGui.Button(tab.ToString(), in size))
                _toolTab = tab;
            if (selected)
                ImGui.PopStyleColor(1);
        }
        ImGui.EndGroup();
        ImGui.SameLine(0, 12);

        ImGui.BeginGroup();
        var dl = ImGui.GetWindowDrawList();
        var first = true;
        foreach (var entry in Entries(_toolTab))
        {
            if (!first)
                ImGui.SameLine(0, 6);
            first = false;
            var active = IsActive(entry);
            if (IconButton($"tool_{entry.Label}", entry.Icon, 44, active))
                ActivateTool(entry);
            var min = ImGui.GetItemRectMin();
            var max = ImGui.GetItemRectMax();
            if (active)
                dl.AddRect(in min, in max, Theme.U8(Theme.Accent), 5f, ImDrawFlags.None, 2.5f);
            // Adventure mode gives the avatar's keys to the avatar, so don't advertise them here.
            var key = HasAvatar && AvatarKeys.Contains(entry.Key) ? "" : KeyName(entry.Key);
            DrawKeyLabel(dl, max, key);
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip(key.Length > 0 ? $"{entry.Label}  [{key}]\n{entry.Hint}" : $"{entry.Label}\n{entry.Hint}");
        }
        var hint = _aimBreath && BreathDragon is { IsNull: false } breather
            ? $"Click where {_sim.World.Get<Creature>(breather).Name} should breathe fire · right-click or Esc to stop"
            : !Ridden.IsNull && _tool == ToolKind.Select
                ? $"Riding {_sim.World.Get<Creature>(Ridden).Name}: click the ground to {(_rideFly ? "fly" : "go")} there{(_rideFly ? "" : " · Ctrl: fly")} · F: shoot game · right-click game: give chase · R: dismount"
                : _tool switch
        {
            ToolKind.Select => $"{(HasAvatar ? "Tab: inventory" : "Tab: next category")} · Q/E rotate · [ ] level {_renderer.CutawayZ} · Home: all levels · \\ roofs",
            ToolKind.PlaceBuilding when _toolPlan != null => $"Placing a {_toolPlan.Variant(PlanVariant(_toolPlan)).Label.ToLowerInvariant()} {_toolPlan.Label}: click to lay it out · R: turn · {(_toolPlan.Variants.Count > 1 ? "C: look · " : "")}right-click to stop",
            ToolKind.Build when _toolDef != null => $"Placing {_toolDef.Label}{(Footprint.IsWide(_toolDef) ? $" ({SizeLabel(_toolDef)})" : "")}: drag to place · {(_toolDef.Model != null ? "R: turn · " : "")}{(ShapesOf(_toolDef).Count > 1 ? "C: shape · " : "")}Ctrl: one level up · right-click to stop",
            _ => "Drag an area · Ctrl: one level up · right-click or Esc to stop",
        };
        TextC(Theme.Dim, hint);
        ImGui.SameLine(0, 10);
        if (ImGui.SmallButton(HideRoofs ? "Show roofs##roofs"u8 : "Hide roofs##roofs"u8))
            ToggleRoofs();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
            Tip(HideRoofs
                ? "Roofs are cut away so you can see inside; an outline marks what's sheltered.  [\\]"
                : "Cut the roofs away to see into the rooms below.  [\\]");
        if (_tool == ToolKind.PlaceBuilding && _toolPlan is { Variants.Count: > 1 } plan)
        {
            // The looks to pick from, the current one lit.
            for (var v = 0; v < plan.Variants.Count; v++)
            {
                ImGui.SameLine(0, 6);
                var on = v == PlanVariant(plan);
                if (on)
                {
                    var c = Theme.AccentSoft;
                    ImGui.PushStyleColor(ImGuiCol.Button, in c);
                }
                if (ImGui.SmallButton($"{plan.Variants[v].Label}##look_{v}"))
                    _planVariant[plan] = v;
                if (on)
                    ImGui.PopStyleColor(1);
            }
        }
        if (_tool == ToolKind.Build && _toolDef != null && ShapesOf(_toolDef) is { Count: > 1 } shapes)
        {
            // The shapes to pick from: one small button each, the current one lit.
            foreach (var shape in shapes)
            {
                ImGui.SameLine(0, 6);
                var on = shape == _toolDef;
                if (on)
                {
                    var c = Theme.AccentSoft;
                    ImGui.PushStyleColor(ImGuiCol.Button, in c);
                }
                if (ImGui.SmallButton($"{SizeLabel(shape)}##shape_{shape.Name}"))
                    PickShape(shape);
                if (on)
                    ImGui.PopStyleColor(1);
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                    Tip($"{shape.Label}\n{shape.Description}");
            }
        }
        ImGui.EndGroup();
        DrawDragonAbilities();
        _toolbarHeight = ImGui.GetWindowSize().Y;
        ImGui.End();
    }

    /// <summary>A hotkey letter in a button's bottom-right corner, shadowed so it reads over any icon.</summary>
    private static void DrawKeyLabel(ImDrawListPtr dl, float2 max, string key)
    {
        if (key.Length == 0)
            return;
        var at = new float2(max.X - 12, max.Y - 18);
        var shadow = at + new float2(1, 1);
        dl.AddText(in shadow, new ImColor8(0, 0, 0, 220), key);
        dl.AddText(in at, Theme.U8(Theme.Text), key);
    }

    /// <summary>R rides or dismounts, B aims a breath (or stops aiming), Q calls it. Not while placing a blueprint: R turns it.</summary>
    private void HandleDragonKeys(Input input)
    {
        if (_tool is ToolKind.Build or ToolKind.PlaceBuilding)
            return;
        if (input.WasKeyPressed(GlfwKey.R))
            ToggleRide();
        if (input.WasKeyPressed(GlfwKey.B))
            ToggleBreathAim();
        HandleCareKeys(input);
    }

    private void ToggleRide()
    {
        if (!Ridden.IsNull)
        {
            Issue(new DismountCommand(_avatar));
            return;
        }
        var dragon = Companions.DragonOf(_sim, _avatar);
        if (dragon.IsNull)
            return;
        if (RidingSystem.CantMount(_sim, _avatar, dragon) is { } why)
            Notify(MessageKind.Info, $"Can't ride {_sim.World.Get<Creature>(dragon).Name}: {why}.");
        else
            Issue(new MountOrderCommand(_avatar, dragon));
    }

    private void ToggleBreathAim()
    {
        if (_aimBreath)
        {
            _aimBreath = false;
            return;
        }
        var dragon = BreathDragon;
        if (dragon.IsNull)
            return;
        if (CantBreathe(dragon) is { } why)
        {
            Notify(MessageKind.Info, $"{_sim.World.Get<Creature>(dragon).Name} can't breathe fire: {why}.");
            return;
        }
        SetTool(ToolKind.Select);
        _aimBreath = true;
    }

    /// <summary>"Breathe fire", "Breathe frost"...: what a breath order to <paramref name="dragon"/> is called (its element).</summary>
    private string BreatheLabel(Entity dragon) => $"Breathe {Sim.Systems.Dragons.BreathOf(_sim, dragon).Noun}";

    /// <summary>Why a breath order to <paramref name="dragon"/> would go nowhere now, or null if it may obey.</summary>
    private string? CantBreathe(Entity dragon)
    {
        var c = _sim.World.Get<Creature>(dragon);
        var stage = c.Stage(_sim.Clock.Tick);
        if (stage.BreathRange <= 0)
            return $"too young ({stage.Label})";
        if (!c.Able)
            return "not fit";
        if (_sim.World.Has<Chained>(dragon))
            return "it's in chains";
        return null;
    }

    /// <summary>
    /// Adventure mode: your dragon's portrait (click to inspect it, its bond along the bottom) and
    /// what you can ask of it, at the toolbar's right end. Unavailable abilities stay visible but
    /// greyed, with the reason in their tooltip, so you see what's coming as it grows.
    /// </summary>
    private void DrawDragonAbilities()
    {
        if (!HasAvatar || Companions.DragonOf(_sim, _avatar) is not { IsNull: false } dragon)
            return;
        var w = _sim.World;
        var c = w.Get<Creature>(dragon);
        var d = w.Get<Dragon>(dragon);
        var stage = c.Stage(_sim.Clock.Tick);
        var riding = !Ridden.IsNull;
        var dl = ImGui.GetWindowDrawList();

        // A divider between the tools and the dragon.
        var tools = ImGui.GetItemRectMax();
        var top = ImGui.GetItemRectMin();
        var a = new float2(tools.X + 8, top.Y + 2);
        var b = new float2(tools.X + 8, tools.Y - 2);
        dl.AddLine(in a, in b, Theme.U8(Theme.Dim), 1f);
        ImGui.SameLine(0, 16);

        ImGui.BeginGroup();
        // Portrait, framed like an inventory slot, with the bond as a bar along its bottom.
        var size = new float2(44, 44);
        var min = ImGui.GetCursorScreenPos();
        var max = min + size;
        if (ImGui.InvisibleButton("##dragon_portrait"u8, in size, ImGuiButtonFlags.None))
            _selected = dragon;
        var hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.None);
        dl.AddRectFilled(in min, in max, Theme.U8(new float4(0, 0, 0, 0.35f)), 5f, ImDrawFlags.None);
        DrawPortrait(dl, dragon, min + new float2(4, 2), size - new float2(8, 10));
        var barMin = new float2(min.X + 4, max.Y - 6);
        var barMax = new float2(max.X - 4, max.Y - 3);
        var barFill = new float2(barMin.X + (barMax.X - barMin.X) * Math.Clamp(d.Affinity, 0, 1), barMax.Y);
        dl.AddRectFilled(in barMin, in barMax, Theme.U8(new float4(0, 0, 0, 0.6f)), 1f, ImDrawFlags.None);
        dl.AddRectFilled(in barMin, in barFill, Theme.U8(new float4(0.9f, 0.35f, 0.45f, 1f)), 1f, ImDrawFlags.None);
        if (stage.CanBeRidden && stage.MinRideAffinity > 0 && d.Affinity < stage.MinRideAffinity)
        {
            // Where the bond lets you ride.
            var x = barMin.X + (barMax.X - barMin.X) * stage.MinRideAffinity;
            var t0 = new float2(x, barMin.Y - 2);
            var t1 = new float2(x, barMax.Y + 1);
            dl.AddLine(in t0, in t1, Theme.U8(Theme.Text), 1f);
        }
        dl.AddRect(in min, in max, Theme.U8(_selected == dragon || riding ? Theme.Accent : Theme.Dim), 5f, ImDrawFlags.None, riding ? 2f : 1f);
        if (hovered)
        {
            var doing = w.TryGet<Mind>(dragon, out var mind) && mind.Job is { Report.Length: > 0 } job ? job.Report : mind?.Asleep == true ? "Asleep" : "Idle";
            var care = Bonding.Peek(_sim, dragon);
            Tip($"{c.Name}, {stage.Label} dragon\n{doing}\nTrust: {Bonding.Level(_sim, d.Affinity)?.Label ?? "?"}{(stage.CanBeRidden && d.Affinity < stage.MinRideAffinity ? $" (rides once trusting)" : "")}"
                + (care.Sulking ? "\nSulking!" : care.Want != DragonWant.None ? $"\nIt {Bonding.Describe(care.Want)}." : "") + "\nClick to inspect.");
        }

        // Ride / dismount.
        ImGui.SameLine(0, 6);
        var calling = w.TryGet<Mind>(_avatar, out var me) && me.Job is WaitForMountJob;
        var cantRide = riding ? null : RidingSystem.CantMount(_sim, _avatar, dragon);
        if (AbilityButton("ride", "ui/icon_ride", riding || calling, disabled: cantRide != null, "R"))
            ToggleRide();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            Tip(riding ? $"Dismount  [R]\nGet down from {c.Name}'s back (on the ground)."
                : cantRide != null ? $"Ride {c.Name}  [R]\nNot yet: {cantRide}."
                : calling ? $"Ride {c.Name}  [R]\n{c.Name} is coming down for you."
                : $"Ride {c.Name}  [R]\nCall {c.Name} over and climb on its back.");

        // Breathe (fire, or its element's breath).
        ImGui.SameLine(0, 6);
        var cantBreathe = CantBreathe(dragon);
        if (AbilityButton("breathe", "ui/icon_breath", _aimBreath, disabled: cantBreathe != null, "B"))
            ToggleBreathAim();
        var breath = Sim.Systems.Dragons.BreathOf(_sim, dragon);
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            Tip(cantBreathe != null ? $"{BreatheLabel(dragon)}  [B]\nNot yet: {cantBreathe}."
                : $"{BreatheLabel(dragon)}  [B]\nClick where {c.Name} {breath.Breath} (reach {Sim.Environment.Breath.RangeOf(_sim, dragon):F0}; it closes in if further).\n{breath.Description}\n"
                  + $"It does as asked about {DragonSystem.Obedience(_sim, dragon) * 100:F0}% of the time{(riding ? "" : ", more often with you on its back")}.");

        // Care: the window, Call (and Stay once it knows it).
        if (!riding)
            DrawCareButtons(dragon);

        // Mounted on a flyer: every move flies, not only with Ctrl.
        if (riding && stage.CanFly)
        {
            ImGui.SameLine(0, 6);
            if (AbilityButton("fly", "ui/icon_fly", _rideFly, disabled: false, ""))
                _rideFly = !_rideFly;
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip(_rideFly ? "Flying: every move takes off, over trees and walls.\nClick to walk again (Ctrl still flies)."
                    : "Fly on every move\nTake off for every move, however short, instead of only with Ctrl.");
        }
        TextC(riding ? Theme.Good : Theme.Dim, riding ? $"On {c.Name}" : calling ? $"{c.Name} is coming" : c.Name);
        ImGui.EndGroup();
    }

    /// <summary>An <see cref="IconButton"/> for a dragon ability: accent ring while on, greyed with no click while unavailable.</summary>
    private bool AbilityButton(string id, string icon, bool on, bool disabled, string key)
    {
        ImGui.BeginDisabled(disabled);
        var pressed = IconButton($"ability_{id}", icon, 44, on && !disabled);
        ImGui.EndDisabled();
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        if (on && !disabled)
            dl.AddRect(in min, in max, Theme.U8(Theme.Accent), 5f, ImDrawFlags.None, 2.5f);
        DrawKeyLabel(dl, max, disabled ? "" : key);
        return pressed && !disabled;
    }

    /// <summary>A square button showing an atlas sprite, aspect-preserved and centered.</summary>
    private bool IconButton(string id, string sprite, float size, bool active)
    {
        var s = Host.Atlas.Get(sprite);
        var aspect = s.PixelSize.Y == 0 ? 1f : s.PixelSize.X / (float)s.PixelSize.Y;
        var inner = size - 10;
        var imgSize = aspect >= 1 ? new float2(inner, inner / aspect) : new float2(inner * aspect, inner);
        var pad = new float2((size - imgSize.X) * 0.5f, (size - imgSize.Y) * 0.5f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, in pad);
        if (active)
        {
            var c = Theme.AccentSoft;
            ImGui.PushStyleColor(ImGuiCol.Button, in c);
        }
        var (texture, page0, page1) = Host.AtlasImage(s);
        float2? uv0 = page0;
        float2? uv1 = page1;
        float4? bg = null;
        float4? tint = null;
        var pressed = ImGui.ImageButton(id, texture, in imgSize, in uv0, in uv1, in bg, in tint);
        if (active)
            ImGui.PopStyleColor(1);
        ImGui.PopStyleVar(1);
        return pressed;
    }
}

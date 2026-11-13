using Brutal.ImGuiApi;
using Narsil.Rendering;
using Narsil.Ui;
using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Ecs;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

/// <summary>ImGui panels: top bar, inspector, work priorities, messages, debug, credits (HUD and toolbar live in their own partials).</summary>
public sealed partial class VillageGame
{
    private bool _showWorkTab;
    private bool _showDebug;
    private bool _showProfiler = OpenProfiler;

    /// <summary>Open the profiler window at start (<c>--profiler</c>).</summary>
    public static bool OpenProfiler { get; set; }
    private SettingsWindow _settings = null!;
    private long _lastSeenMessages;

    private static readonly float4 Good = Theme.Good;
    private static readonly float4 Warn = Theme.Warn;
    private static readonly float4 Bad = Theme.Bad;
    private static readonly float4 Dim = Theme.Dim;

    public override void DrawUi()
    {
        DrawDayTint();
        _renderer.DrawLabels(_camera, _sim, _time);
        DrawAvatarPrompt();
        DrawTopBar();
        _hud.BeginFrame(Screen, ImGui.GetFrameHeight());
        // Top right: the clock and its journal strip, then the open journal panels, stacked.
        DrawClock();
        DrawGift();
        DrawShop();
        DrawQuestBoard();
        DrawSpeech();
        DrawMessages();
        // Top left: you. Top centre: the village (god mode) and the toasts.
        DrawNeedsPanel();
        DrawTips();
        DrawVillagerBar();
        DrawToasts();
        // Bottom left, before the centre so the centre keeps clear of it.
        DrawInspector();
        // Bottom centre, stacking upward: the build tools, the hotbar, and the work in hand.
        DrawToolbar();
        DrawHotbar();
        DrawWork();
        // Free windows.
        DrawStorageWindow();
        DrawSignWindow();
        DrawTinkerWindow();
        DrawCurioTalk();
        DrawWorldItemMenu();
        DrawStationWindow();
        DrawRecipeBook();
        DrawInventoryWindow();
        DrawHoverInfo();
        if (_showWorkTab && !OrdersLocked)
            DrawWorkTab();
        if (_showDebug && _devMode)
            DrawDebug();
        Narsil.Ui.ProfilerWindow.Draw(Host, ref _showProfiler);
        DrawCredits();
        DrawSaveWindows();
        DrawCoop();
        DrawReveal();
        DrawControls();
        _settings.Draw();
        WatchForDanger();
    }

    private bool _showCredits;
    private readonly CreditsWindow _credits = new(TitleGame.Title, "The people the heroes walk past.");

    /// <summary>Credits screen. BRUTAL License 2.1 requires its copyright notice here (<see cref="CreditsWindow"/>).</summary>
    private void DrawCredits() => _credits.Draw(ref _showCredits, $"{BuildInfo.Label}, build tag {BuildInfo.Tag(_sim.Defs)}");

    // Short names for Narsil's ImGuiText helpers, which escape literal % for the printf-style ImGui calls.
    private static void TextC(float4 color, string text) => ImGuiText.Colored(color, text);

    private static void Tip(string text) => ImGuiText.Tooltip(text);
    /// <summary>
    /// Text wrapped to the space there is, but never wider than <see cref="WrapCap"/>: in a window
    /// that sizes itself to its content, wrapping at the window's own edge feeds back, and the
    /// window creeps wider every frame.
    /// </summary>
    private static void Wrapped(string text)
    {
        var width = MathF.Min(ImGui.GetContentRegionAvail().X, Narsil.Ui.Gui.S(WrapCap));
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + MathF.Max(width, Narsil.Ui.Gui.S(120)));
        ImGui.TextWrapped(Narsil.Ui.ImGuiText.Escape(text));
        ImGui.PopTextWrapPos();
    }

    /// <summary><see cref="Wrapped"/> in a colour.</summary>
    private static void WrappedC(float4 color, string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, in color);
        Wrapped(text);
        ImGui.PopStyleColor(1);
    }

    /// <summary>The widest a wrapped line runs, unscaled.</summary>
    private const float WrapCap = 320f;
    private static void Bullet(string text) => ImGuiText.Bullet(text);

    /// <summary>A free window's first place (screen units) and size (unscaled), kept on screen.</summary>
    private static void SetNextWindow(float2 pos, float2 size)
    {
        float2? pivot = null;
        ImGui.SetNextWindowPos(in pos, ImGuiCond.FirstUseEver, in pivot);
        var scaled = new float2(Gui.S(size.X), Gui.S(size.Y));
        ImGui.SetNextWindowSize(in scaled, ImGuiCond.FirstUseEver);
        ScreenFit.Next();
    }

    /// <summary>Passed by <c>in</c> to ImGui overloads that take an optional size; null means "auto".</summary>
    private static readonly float2? _autoSize = null;

    private void DrawTopBar()
    {
        if (!ImGui.BeginMainMenuBar())
            return;

        using (Gui.HeaderFont())
            TextC(Theme.Accent, TitleGame.Title);
        ImGui.Separator();

        // Menu: the things you reach for now and then, folded away so the bar stays short.
        if (ImGui.BeginMenu("Menu"u8, true))
        {
            if (ImGui.MenuItem("Save"u8, "F6"u8, _saveWindow == SaveWindow.Save, true))
                OpenSaveWindow(SaveWindow.Save);
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip($"Save the village ({LastSavedText()}). F6 quicksaves.");
            if (ImGui.MenuItem("Load"u8, "F9"u8, _saveWindow == SaveWindow.Load, true))
                OpenSaveWindow(SaveWindow.Load);
            if (ImGui.MenuItem("Settings"u8, ""u8, _settings.IsOpen, true))
                _settings.Toggle();
            if (ImGui.MenuItem("Screenshot"u8, "F12"u8, false, true))
                Host.Screenshot();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip($"Save what's on screen to {Host.ScreenshotDirectory}. F12 anywhere; Shift+F12 leaves out the UI.");
            if (ImGui.MenuItem("Credits"u8, ""u8, _showCredits, true))
                _showCredits = !_showCredits;
            ImGui.Separator();
            // A submenu, so leaving takes a second, deliberate click.
            if (ImGui.BeginMenu("Back to title"u8, true))
            {
                TextC(Theme.Dim, Cap(LastSavedText()) + ".");
                if (ImGui.MenuItem("Save, then leave"u8, ""u8, false, true))
                {
                    if (SaveTo(SaveSlots.Quicksave) is { } error)
                        Notify(MessageKind.Warning, error); // stay: nothing's lost
                    else
                        Host.SwitchTo(new TitleGame());
                }
                if (ImGui.MenuItem("Leave without saving"u8, ""u8, false, true))
                    Host.SwitchTo(new TitleGame());
                ImGui.EndMenu();
            }
            ImGui.EndMenu();
        }
        if (!OrdersLocked && ImGui.MenuItem("Work"u8, ""u8, _showWorkTab, true))
            _showWorkTab = !_showWorkTab;
        if (ImGui.MenuItem("Recipes"u8, ""u8, _showRecipeBook, true))
            _showRecipeBook = !_showRecipeBook;
        if (ImGui.MenuItem("Controls"u8, "H"u8, _showControls, true))
            _showControls = !_showControls;
        if (_devMode && ImGui.MenuItem("Debug"u8, ""u8, _showDebug, true))
            _showDebug = !_showDebug;
        if (_devMode)
        {
            ImGui.Separator();
            TextC(Dim, $"{Host.FrameTimeMs:F1} ms");
            Gui.ItemTip($"Average frame time. Slowest in the last 2 s: {Host.FrameTimeMaxMs:F1} ms; {Host.Hitches} hitches so far (SC_PROFILE=1 logs each).");
        }
        ImGui.EndMainMenuBar();
    }

    private void ToolButton(string label, ToolKind tool, ThingDef? def = null)
    {
        var active = _tool == tool && _toolDef == def;
        if (active)
        {
            var c = new float4(0.25f, 0.5f, 0.85f, 1f);
            ImGui.PushStyleColor(ImGuiCol.Button, in c);
        }
        var size = (float2?)new float2(Gui.S(110), 0);
        if (ImGui.Button(label, in size))
            SetTool(active ? ToolKind.Select : tool, active ? null : def);
        if (active)
            ImGui.PopStyleColor(1);
    }

    private void DrawInspector()
    {
        var w = _sim.World;
        if (!w.IsAlive(_selected))
            return;
        var view = View;
        if (!view.CanSee(_selected))
        {
            _selected = Entity.Null; // lost from sight
            return;
        }
        _hud.Next(Anchor.BottomLeft, ScreenFit.Width(360, 0.32f), Gui.S(400));
        var open = true;
        Panel.Begin("Inspector###inspector", ref open, Hud.Flags);
        _hud.Took();
        if (!open)
            _selected = Entity.Null;

        if (w.TryGet<Creature>(_selected, out var c))
        {
            if (view.Omniscient || _selected == _avatar)
                InspectCreature(_selected, c);
            else
                InspectPerceived(_selected);
        }
        else if (w.TryGet<Thing>(_selected, out var thing))
        {
            // A building is common knowledge, but what's in it takes going to look.
            if (view.Omniscient || !thing.Spawned || view.CanSeeCell(thing.Cell))
                InspectThing(_selected, thing);
            else
                InspectorHeader(thing.Def.Sprite is { Length: > 0 } art ? Host.Atlas.Get(art) : Host.Atlas.Get(TextureAtlas.White), thing.Def.Label, "Too far to make out.");
        }

        if (_devMode && view.Omniscient && ImGui.CollapsingHeader("Components (debug)"u8, ImGuiTreeNodeFlags.None))
            foreach (var comp in w.DebugComponents(_selected))
                TextC(Dim, Describe(comp));
        Panel.End();
    }

    private static string Describe(object comp) => comp switch
    {
        Thing t => $"Thing {{ {t.Def.Name}, {t.Cell.X},{t.Cell.Y},{t.Cell.Z}, hp {t.Hp:F0}, spawned {t.Spawned} }}",
        Creature c => $"Creature {{ {c.Name}, {c.Def.Name}, hp {c.Hp:F0} }}",
        Position p => $"Position {{ {p.Pos.X:F2}, {p.Pos.Y:F2}, {p.Pos.Z:F2} }}",
        Needs n => $"Needs {{ {string.Join(", ", n.Values.Select(v => v.ToString("F2")))} }}",
        Mind m => $"Mind {{ {m.Status}, next think {m.NextThinkTick} }}",
        _ => comp.ToString() ?? comp.GetType().Name,
    };

    private static void Bar(string label, float value, float4 color, string? overlay = null) => Gui.Bar(label, value, color, overlay);

    /// <summary>Portrait, name in the header font, and a subtitle line.</summary>
    /// <remarks>
    /// <paramref name="creature"/> is nullable rather than defaulted: <c>default(Entity)</c> is
    /// entity 0, a real entity, so a defaulted parameter drew whatever entity 0 was (and crashed
    /// once that wasn't a creature, e.g. on a new map after travelling).
    /// </remarks>
    private void InspectorHeader(in Sprite portrait, string title, string subtitle, Entity? creature = null)
    {
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var box = new float2(56, 56);
        var max = min + box;
        dl.AddRectFilled(in min, in max, Theme.U8(Theme.Well), 6f, ImDrawFlags.None);
        dl.AddRect(in min, in max, Theme.U8(Theme.AccentSoft), 6f, ImDrawFlags.None, 1.5f);
        if (creature is { IsNull: false } who && _sim.World.IsAlive(who) && _sim.World.Has<Creature>(who))
            DrawPortrait(dl, who, min + new float2(4, 4), box - new float2(8, 8));
        else
            DrawSprite(dl, portrait, min + new float2(4, 4), box - new float2(8, 8));
        ImGui.Dummy(in box);
        ImGui.SameLine(0, 10);
        ImGui.BeginGroup();
        Theme.Header(Host.ImGui, title);
        TextC(Dim, subtitle);
        ImGui.EndGroup();
        ImGui.Separator();
    }

    /// <summary>Someone else, as far as anyone can tell by looking.</summary>
    private void InspectPerceived(Entity e)
    {
        if (View.Describe(e) is not { } seen)
            return;
        InspectorHeader(PortraitOf(e), seen.Name, seen.Condition, e);
        if (seen.Holding is { } held)
            TextC(Dim, $"Holding: {held}");
        DrawLayOnHands(e);
        DrawHandOver(e);
    }

    private void InspectCreature(Entity e, Creature c)
    {
        var w = _sim.World;
        var stage = c.Stage(_sim.Clock.Tick);
        InspectorHeader(PortraitOf(e), c.Name, c.Def.Race == Race.Animal ? $"wild {c.Def.Label}"
            : c.Def.Race == Race.Human && Rendering.HumanLook.Of(_sim, e).Folk == Folk.Elf ? "elf" : $"{c.Def.Label}", e);
        if (!c.Dead && Sim.Systems.FoodPoisoning.Describe(_sim, e) is { } sick)
            TextC(Warn, sick);
        if (c.Dead)
        {
            TextC(Bad, "Dead");
            if (w.TryGet<Rot>(e, out var carcass))
            {
                SpoilageBar(carcass.Progress, _sim.TemperatureAt(_sim.LocationOf(e)));
                TextC(Dim, $"Butcher it (Hunt work) for {c.Def.Animal?.MeatYield ?? 0} raw meat.");
            }
            return;
        }

        var mind = w.Get<Mind>(e);
        w.TryGet<Health>(e, out var h);
        if (!ImGui.BeginTabBar("creature"u8, ImGuiTabBarFlags.None))
            return;

        if (ImGui.BeginTabItem("Overview"u8, ImGuiTabItemFlags.None))
        {
            ImGui.Text($"Doing: {mind.Status}");
            if (_sim.Ai.LastFailure.TryGetValue(e, out var fail))
                TextC(Dim, $"Last failed: {fail}");
            var health = h != null ? Math.Min(c.Hp / c.Def.MaxHp, h.Condition) : c.Hp / c.Def.MaxHp;
            Bar("Health", health, health < 0.4f ? Bad : Good);
            if (w.TryGet<Mood>(e, out var overviewMood))
                MoodBar(e, c, overviewMood);
            if (w.TryGet<Traits>(e, out var traits) && traits.List.Count > 0)
            {
                TextC(Dim, "Traits:");
                foreach (var t in traits.List)
                {
                    ImGui.SameLine(0, 6);
                    ImGui.Text(t.Label);
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                        Tip(t.Description);
                }
            }
            if (c.Downed)
                TextC(Bad, w.Has<Carried>(e) ? "Down, being carried" : "Down: can't move or act");
            if (UrgentNeed(e) is { } urgent)
                TextC(Bad, $"Urgently needs {urgent.Label.ToLowerInvariant()}");
            InspectCreatureRoles(e, c, mind);
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Needs"u8, ImGuiTabItemFlags.None))
        {
            var needs = w.Get<Needs>(e);
            foreach (var rate in c.Def.Needs)
            {
                var v = needs.Get(rate.NeedDef);
                Bar(rate.NeedDef.Label, v, v < rate.NeedDef.UrgentBelow ? Bad : v < rate.NeedDef.SeekBelow ? Warn : Good);
            }
            var temp = _sim.TemperatureAt(w.Get<Position>(e).Cell);
            var comfy = temp >= c.Def.ComfortTempMin && temp <= c.Def.ComfortTempMax;
            TextC(comfy ? Good : Warn, $"Feels {temp:F0}°C (comfortable {c.Def.ComfortTempMin:F0}–{c.Def.ComfortTempMax:F0}°C)");
            ImGui.EndTabItem();
        }

        if (w.TryGet<Mood>(e, out var mood) && ImGui.BeginTabItem("Mood"u8, ImGuiTabItemFlags.None))
        {
            InspectMood(e, c, mood);
            ImGui.EndTabItem();
        }

        if (w.Has<Mood>(e) && ImGui.BeginTabItem("Social"u8, ImGuiTabItemFlags.None))
        {
            InspectSocial(e, c);
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Health"u8, ImGuiTabItemFlags.None))
        {
            if (h == null || (h.Injuries.Count == 0 && h.Scars.Count == 0 && h.Blood >= 0.999f))
                TextC(Good, "No wounds.");
            else
                InspectHealth(h);
            ImGui.EndTabItem();
        }

        if (w.TryGet<Skills>(e, out var skills) && ImGui.BeginTabItem("Skills"u8, ImGuiTabItemFlags.None))
        {
            foreach (var s in Enum.GetValues<Skill>())
            {
                var level = skills[s];
                var next = level < Skills.Max ? skills.Xp[(int)s] / Skills.XpToNext(level) : 1f;
                Bar(s.ToString(), (level + next) / Skills.Max, level >= 10 ? Good : level >= 4 ? Warn : Bad, $"{level}");
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                    Tip($"Level {level}: works at {Skills.WorkFactor(level) * 100:F0}% speed"
                    + (s is Skill.Melee or Skill.Shooting ? $", {(level - Skills.Average) * SkillRules.AccuracyPerLevel * 100:+0;-0}% to hit" : "")
                    + (level < Skills.Max ? $"\n{next * 100:F0}% of the way to {level + 1}" : ""));
            }
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Gear"u8, ImGuiTabItemFlags.None))
        {
            var orders = GearOrders(e);
            if (w.TryGet<Equipment>(e, out var eq) && w.IsAlive(eq.Weapon))
            {
                ImGui.Text($"Wields: {w.Get<Thing>(eq.Weapon).Def.Label}");
                if (orders)
                    GearButton(e, GearSlot.Weapon, "Drop##weapon", "Put the weapon down and don't pick one up again until told to.");
            }
            else
            {
                TextC(Dim, "Unarmed");
                if (orders)
                    BareLock(e, GearSlot.Weapon);
            }
            if (w.Has<NonViolent>(e))
                TextC(Dim, "Won't fight");
            if (c.Def.Race == Race.Human)
                InspectApparel(e);
            var carried = w.Get<Carrying>(e).Item;
            if (w.IsAlive(carried))
                ImGui.Text($"Carrying: {w.Get<Stack>(carried).Count}x {w.Get<Thing>(carried).Def.Label}");
            ImGui.EndTabItem();
        }
        ImGui.EndTabBar();
    }

    /// <summary>Can the player give this creature gear orders (one of the village's people)?</summary>
    private bool GearOrders(Entity e)
    {
        var c = _sim.World.Get<Creature>(e);
        return c is { Faction: Faction.Village, Dead: false } && c.Def.Race == Race.Human;
    }

    /// <summary>A "Take off"/"Drop" button: the slot is emptied and kept bare (<see cref="GearOrderCommand"/>).</summary>
    private void GearButton(Entity e, GearSlot slot, string label, string tip)
    {
        ImGui.SameLine(0, -1);
        if (ImGui.SmallButton(label))
            Issue(new GearOrderCommand(e, slot));
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
            Tip(tip);
    }

    /// <summary>A slot kept bare by order: say so, with a button to lift the order.</summary>
    private void BareLock(Entity e, GearSlot slot)
    {
        if (!_sim.World.TryGet<Outfit>(e, out var outfit) || !outfit.KeepBare(slot))
            return;
        ImGui.SameLine(0, -1);
        TextC(Warn, "(kept bare)");
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
            Tip("Won't put anything on here until told to.");
        ImGui.SameLine(0, -1);
        if (ImGui.SmallButton($"Allow##{slot}"))
            Issue(new GearOrderCommand(e, slot, KeepBare: false));
    }

    /// <summary>What a person wears and how worn it is, with take-off orders for the village's own.</summary>
    private void InspectApparel(Entity e)
    {
        var w = _sim.World;
        var any = false;
        var orders = GearOrders(e);
        foreach (var slot in Enum.GetValues<ApparelSlot>())
        {
            if (!w.TryGet<Apparel>(e, out var worn) || !w.IsAlive(worn[slot]))
            {
                if (orders && w.TryGet<Outfit>(e, out var o) && o.KeepBare(Outfit.Of(slot)))
                {
                    TextC(Dim, slot switch
                    {
                        ApparelSlot.Back => "No pack", ApparelSlot.Neck => "No jewelry", ApparelSlot.Tunic => "No tunic", ApparelSlot.Legs => "Bare legs",
                        ApparelSlot.Feet => "Barefoot", ApparelSlot.Cloak => "No cloak", _ => $"No {slot.ToString().ToLowerInvariant()} armor",
                    });
                    BareLock(e, Outfit.Of(slot));
                    any = true;
                }
                continue;
            }
            any = true;
            var t = w.Get<Thing>(worn[slot]);
            var a = t.Def.Apparel!;
            var hp = t.Hp / t.MaxHp;
            Bar(t.Label, hp, hp < 0.3f ? Bad : hp < 0.6f ? Warn : Good);
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip(t.Def.Container is { } bag
                    ? $"A pack: carries {bag.Capacity:0.#} kg, holding {_sim.LoadOf(worn[slot]):0.#} kg"
                    : $"Covers {string.Join(", ", a.Covers)}\nArmor: sharp {a.Sharp * 100:F0}%, blunt {a.Blunt * 100:F0}%, heat {a.Heat * 100:F0}%");
            if (orders)
                GearButton(e, Outfit.Of(slot), $"Take off##{slot}", $"Take off the {t.Label} and leave the slot bare until told otherwise.");
            if (t.Def.Container != null)
                foreach (var item in _sim.StoredIn(worn[slot]))
                    TextC(Dim, $"  {w.Get<Stack>(item).Count} {w.Get<Thing>(item).Label}");
        }
        if (!any)
            TextC(Dim, "No armor");
        if (orders)
            WearPicker(e);
    }

    /// <summary>
    /// "Wear…": gear lying about (or in storage) that a villager could go and put on, nearest
    /// first, issued as a <see cref="WearCommand"/>.
    /// </summary>
    private void WearPicker(Entity e)
    {
        var w = _sim.World;
        if (!ImGui.BeginCombo("##wear"u8, "Wear…"u8, ImGuiComboFlags.None))
            return;
        var at = w.Get<Position>(e).Cell;
        var gear = _sim.AvailableItems()
            .Where(i => w.Get<Thing>(i).Def is { } d && (d.Apparel != null || d.Weapon != null) && !_sim.Reservations.IsReservedByOther(i, e))
            .OrderBy(i => Sim.Maps.Map.Distance(at, _sim.LocationOf(i)))
            .Take(12)
            .ToList();
        if (gear.Count == 0)
            TextC(Dim, "Nothing to put on lying about.");
        foreach (var item in gear)
        {
            var t = w.Get<Thing>(item);
            var slot = t.Def.Weapon != null ? "weapon" : t.Def.Container != null ? "pack" : $"{t.Def.Apparel!.Slot.ToString().ToLowerInvariant()}";
            if (ImGui.Selectable($"{t.Label} ({slot}, {Sim.Maps.Map.Distance(at, _sim.LocationOf(item))} away)##{item.Index}", false, ImGuiSelectableFlags.None, in _autoSize))
                Issue(new WearCommand(e, item));
        }
        ImGui.EndCombo();
    }

    private static float4 MoodColor(float v) => v < MoodRules.NeutralLow ? Bad : v < 0.45f ? Warn : Good;

    /// <summary>Mood with where it's heading and what it does, in the tooltip.</summary>
    private void MoodBar(Entity e, Creature c, Mood mood)
    {
        var trend = mood.Target > mood.Value + 0.01f ? " ↑" : mood.Target < mood.Value - 0.01f ? " ↓" : "";
        Bar("Mood", mood.Value, MoodColor(mood.Value), $"{mood.Value * 100:F0}{trend}");
        if (!ImGui.IsItemHovered(ImGuiHoveredFlags.None))
            return;
        var effect = c.Def.Race == Race.Dragon
            ? $"{MoodRules.ObedienceShift(mood.Value) * 100:+0;-0;0}% obedience"
            : _sim.World.Has<Avatar>(e) ? "your own pace doesn't depend on it"
            : $"works at {MoodRules.WorkFactor(mood.Value) * 100:F0}% speed";
        Tip($"{c.Name} feels {MoodRules.Describe(mood.Value)}: {effect}.\nHeading for {mood.Target * 100:F0}.");
    }

    /// <summary>The mood bar and every thought behind it, strongest first; repeated memories grouped.</summary>
    private void InspectMood(Entity e, Creature c, Mood mood)
    {
        MoodBar(e, c, mood);
        var traits = TraitRules.Of(_sim, e);
        var rows = new List<(string Label, string Description, float Points, string When, int Count)>();
        foreach (var t in traits?.List ?? [])
            if (t.Mood != 0)
                rows.Add((t.Label, t.Description, t.Mood, "trait", 1));
        foreach (var t in mood.Situational)
            rows.Add((t.Label, t.Description, TraitRules.Factor(traits, t) * t.Mood, "now", 1));
        for (var i = 0; i < mood.Memories.Count; i++)
        {
            var m = mood.Memories[i];
            var points = MoodSystem.Weight(mood, i) * TraitRules.Factor(traits, m.Def) * m.Def.Mood;
            if (points == 0)
                continue; // opinion-only memories show in the Social tab
            var left = $"{SimClock.TicksToDays(m.TicksLeft) * 24:F0}h";
            var at = rows.FindIndex(r => r.Label == m.Def.Label);
            if (at < 0)
                rows.Add((m.Def.Label, m.Def.Description, points, left, 1));
            else
                rows[at] = rows[at] with { Points = rows[at].Points + points, Count = rows[at].Count + 1 };
        }
        if (rows.Count == 0)
        {
            TextC(Dim, "Nothing on their mind.");
            return;
        }
        rows.Sort((a, b) => Math.Abs(b.Points).CompareTo(Math.Abs(a.Points)));
        ImGui.Separator();
        foreach (var (label, description, points, when, count) in rows)
        {
            TextC(points >= 0 ? Good : Bad, $"{points:+0;-0;0}");
            ImGui.SameLine(48, -1);
            ImGui.Text(count > 1 ? $"{label} ×{count}" : label);
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None) && description.Length > 0)
                Tip(description);
            ImGui.SameLine(0, -1);
            TextC(Dim, when);
        }
    }

    /// <summary>
    /// What this creature thinks of everyone it knows (its faction, plus anyone it has memories
    /// about), best first, with the reasons in a tooltip; then its recent conversations.
    /// </summary>
    private void InspectSocial(Entity e, Creature c)
    {
        var w = _sim.World;
        var mood = w.Get<Mood>(e);
        var known = _sim.Creatures()
            .Where(o => o != e && w.Has<Mood>(o) && (w.Get<Creature>(o).Faction == c.Faction || mood.Memories.Exists(m => m.Other == o)))
            .Select(o => (Who: o, Opinion: MoodRules.Opinion(_sim, e, o)))
            .OrderByDescending(x => x.Opinion)
            .ToList();
        if (known.Count == 0)
            TextC(Dim, "Knows nobody here.");
        foreach (var (other, opinion) in known)
        {
            TextC(opinion >= MoodRules.FriendAt ? Good : opinion <= MoodRules.RivalAt ? Bad : opinion >= 0 ? Theme.Text : Warn, $"{opinion:+0;-0;0}");
            ImGui.SameLine(48, -1);
            var relation = MoodRules.Relation(opinion);
            ImGui.Text(relation.Length > 0 ? $"{w.Get<Creature>(other).Name} ({relation})" : w.Get<Creature>(other).Name);
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip(OpinionReasons(e, other));
        }

        var talk = _sim.Social.Recent.Where(ev => ev.Initiator == e || ev.Recipient == e).TakeLast(6).Reverse().ToList();
        if (talk.Count == 0)
            return;
        ImGui.Separator();
        TextC(Dim, "Lately");
        foreach (var ev in talk)
        {
            var ago = $"{SimClock.TicksToDays(_sim.Clock.Tick - ev.Tick) * 24:F0}h ago";
            var name = (Entity x) => w.IsAlive(x) ? w.Get<Creature>(x).Name : "someone";
            Bullet($"{name(ev.Initiator)} {ev.Def.Verb} {name(ev.Recipient)} ({ago})");
        }
    }

    /// <summary>"Compatibility +4 / chatted +5 ×2 / slighted me −8": the parts of an opinion.</summary>
    private string OpinionReasons(Entity e, Entity other)
    {
        var mood = _sim.World.Get<Mood>(e);
        var parts = new List<(string Label, float Points, int Count)>();
        for (var i = 0; i < mood.Memories.Count; i++)
        {
            var m = mood.Memories[i];
            if (m.Other != other || m.Def.Opinion == 0)
                continue;
            var points = MoodSystem.Weight(mood, i, perPerson: true) * TraitRules.Factor(TraitRules.Of(_sim, e), m.Def) * m.Def.Opinion;
            var at = parts.FindIndex(p => p.Label == m.Def.Label);
            if (at < 0)
                parts.Add((m.Def.Label, points, 1));
            else
                parts[at] = (m.Def.Label, parts[at].Points + points, parts[at].Count + 1);
        }
        var lines = parts.Select(p => $"{p.Label}{(p.Count > 1 ? $" ×{p.Count}" : "")}: {p.Points:+0;-0;0}").ToList();
        lines.Insert(0, $"Get on by nature: {MoodRules.Compatibility(_sim, e, other):+0;-0;0}");
        return string.Join("\n", lines);
    }

    /// <summary>Keeper bond, breath orders, drafting: the things a player clicks on a creature.</summary>
    private void InspectCreatureRoles(Entity e, Creature c, Mind mind)
    {
        var w = _sim.World;
        if (c.Def.Race == Race.Human && c.Faction == Faction.Raider)
        {
            ImGui.Separator();
            var ordered = w.Has<CaptureOrder>(e);
            if (ordered || Prisoners.Capturable(_sim, e))
            {
                if (ImGui.Checkbox("Capture"u8, ref ordered))
                    Issue(new CaptureOrderCommand(e, ordered));
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                    Tip("A doctor carries them to a free prisoner bed once the fighting is over.\nMark a bed \"For prisoners\" first.");
                if (ordered && w.Count<ForPrisoners>() == 0)
                    TextC(Warn, "No prisoner bed: select a bed and tick \"For prisoners\".");
            }
            else
            {
                TextC(Dim, "Can be captured once downed.");
            }
        }

        if (c.Def.Animal is { } animal)
        {
            ImGui.Separator();
            var hunted = w.Has<HuntOrder>(e);
            if (ImGui.Checkbox("Hunt"u8, ref hunted))
                Issue(new HuntOrderCommand(e, hunted));
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip($"Someone with a bow and Hunt work stalks it and shoots it.\nA carcass gives {animal.MeatYield} raw meat.");
            if (w.TryGet<Herd>(e, out var herd))
            {
                ImGui.SameLine(0, -1);
                if (ImGui.Button("Hunt the herd"u8, in _autoSize))
                    Issue(new HuntOrderCommand(e, true, WholeHerd: true));
            }
            if (animal.Aggressive)
                TextC(Warn, "Fights back when hurt.");
            var anyBow = false;
            foreach (var h in w.Each<Equipment>())
                if (w.Get<Creature>(h).Faction == Faction.Village && Sim.Ai.HuntJob.RangedAttack(_sim, h) != null)
                {
                    anyBow = true;
                    break;
                }
            if (!anyBow)
                TextC(Dim, "Nobody in the village has a bow.");
            var keep = _sim.Larder.KeepMeat;
            ImGui.Text("Keep meat in store:"u8);
            ImGui.SameLine(0, -1);
            ImGui.SetNextItemWidth(140);
            if (ImGui.SliderInt("##larder"u8, ref keep, 0, 300, keep == 0 ? "off"u8 : "%d"u8, ImGuiSliderFlags.AlwaysClamp) && keep != _sim.Larder.KeepMeat)
                Issue(new LarderCommand(keep));
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip("While the village has less raw meat and fish than this, it marks the next animal\nto hunt: harmless game near home, never the last of a herd.");
        }

        if (w.TryGet<Prisoner>(e, out var prisoner))
        {
            ImGui.Separator();
            TextC(Warn, "Prisoner");
            var mode = (int)prisoner.Mode;
            var changed = ImGui.RadioButton("Hold"u8, ref mode, (int)PrisonerMode.Hold);
            ImGui.SameLine(0, -1);
            changed |= ImGui.RadioButton("Recruit"u8, ref mode, (int)PrisonerMode.Recruit);
            ImGui.SameLine(0, -1);
            changed |= ImGui.RadioButton("Release"u8, ref mode, (int)PrisonerMode.Release);
            if (changed)
                Issue(new PrisonerModeCommand(e, (PrisonerMode)mode));
            if (prisoner.Mode == PrisonerMode.Recruit)
                TextC(Dim, prisoner.Resistance > 0
                    ? $"Resistance {prisoner.Resistance:F1}: a warden talks with them once a day."
                    : "Ready to listen: each daily talk may win them over.");
            // Mood decides both: content prisoners listen, miserable ones look for a way out.
            var breakPerHour = Prisoners.BreakChancePerHour(_sim, e);
            if (breakPerHour > 0)
            {
                TextC(Bad, $"Restless: may try to break out (~{(1 - MathF.Pow(1 - breakPerHour, 24)) * 100:F0}% a day).");
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                    Tip($"Prisoners below {Prisoners.BreakBelow * 100:F0} mood may escape. Feed them, keep them warm, tend their wounds and talk with them (Recruit). A villager nearby keeps watch.");
            }
            else if (prisoner.Resistance > 0)
                TextC(Dim, "Settled: not looking to escape.");
        }

        if (c.Def.Race == Race.Human && c.Faction == Faction.Village)
        {
            ImGui.Separator();
            var drafted = mind.Drafted;
            if (w.Has<Avatar>(e))
            {
                ImGui.TextDisabled("You (WASD to walk, hold F to work, Z to dig, X to put down, N to sleep, Tab: inventory, 1–4: pack to hand)"u8);
                // Lie down where you stand: in the bed there, else on the ground.
                var site = _sim.Map.Structure[_sim.Map.Idx(w.Get<Position>(e).Cell)];
                var inBed = !site.IsNull && w.Get<Thing>(site).Def.Building is { IsBed: true };
                if (mind.Asleep)
                    TextC(Dim, "Asleep (walk to wake up).");
                else if (ImGui.Button(inBed ? $"Sleep in the {w.Get<Thing>(site).Label} (N)" : "Sleep here, on the ground (N)", in _autoSize))
                    Issue(new SleepCommand(e));
            }
            else if (HasAvatar)
                DrawRequests(e); // Adventure mode: you ask companions, you don't command them
            else if (ImGui.Checkbox("Drafted (right-click to move)"u8, ref drafted))
                Issue(new DraftCommand(e, drafted));
        }
    }

    /// <summary>Blood, pain, what the creature can still do, and each wound.</summary>
    private void InspectHealth(Health h)
    {
        Bar("Blood", h.Blood, h.Blood < 0.5f ? Bad : h.Blood < 0.8f ? Warn : Good);
        var bleed = h.BleedPerHour;
        TextC(h.Pain > 0.5f ? Bad : Dim, $"Pain {h.Pain * 100:F0}%" + (bleed > 0 ? $"   bleeding {bleed * 100:F0}%/h" : ""));
        var caps = Enum.GetValues<Capacity>()
            .Where(k => k != Capacity.Flying || h.Body.Parts.Exists(p => p.Capacities.Contains(Capacity.Flying)))
            .Select(k => (k, v: h.Capacity(k)))
            .Where(x => x.v < 0.995f)
            .Select(x => $"{x.k.ToString().ToLowerInvariant()} {x.v * 100:F0}%");
        var capText = string.Join(", ", caps);
        if (capText.Length > 0)
            Wrapped(capText);
        foreach (var inj in h.Injuries.OrderBy(i => i.Part))
        {
            var part = h.Body.Parts[inj.Part];
            var state = inj.Tended ? $"bandaged, {TendQuality(inj.Quality)}" : h.BleedOf(inj) > 0 ? "bleeding" : "untended";
            // A wound an empath drew into themselves is known by its feel, not its name.
            var kind = inj.Absorbed && !View.Omniscient ? Sim.Healing.Empathy.Feel(_sim, inj.Type) : DamageTypes.Wound(inj.Type);
            TextC(inj.Infection != null ? Bad : inj.Tended ? Dim : Warn, $"  {part.Label}: {kind}, {inj.Damage:F0}/{h.PartMaxHp(inj.Part):F0} ({state})");
            if (inj.Infection is { } infection)
            {
                Bar("infection", infection.Severity, Bad);
                Bar("immunity", infection.Immunity, infection.Immunity > infection.Severity ? Good : Warn);
            }
        }
        foreach (var scar in h.Scars.OrderBy(s => s.Part))
            TextC(Dim, $"  {h.Body.Parts[scar.Part].Label}: old {DamageTypes.Wound(scar.Type)} scar (-{scar.Damage:F0})");
    }

    private static string TendQuality(float q) => q switch
    {
        < 0.3f => "poorly",
        < 0.55f => "decently",
        < 0.8f => "well",
        _ => "expertly",
    };

    /// <summary>How far food has gone off, and how fast it's going here.</summary>
    private void SpoilageBar(float progress, float celsius)
    {
        Bar("Spoilage", progress, progress > 0.7f ? Bad : progress > 0.4f ? Warn : Good);
        if (FoodPoisoning.Spoiled(progress))
            TextC(Bad, "Spoiled: eating it may cause food poisoning. Cooking it makes it safer.");
        var rate = RotSystem.RateAt(celsius);
        TextC(Dim, rate <= 0 ? "Frozen: it keeps." : rate < 1 ? "Cool here: it keeps longer." : rate > 1 ? "Hot here: it spoils fast." : "Spoiling at the usual pace.");
    }

    /// <summary>
    /// The preset building a selected part belongs to: its phases (what's built of each, what's
    /// blocked), the extras it takes, and taking it down.
    /// </summary>
    private void InspectBuilding(Entity building)
    {
        var b = _sim.World.Get<PresetBuilding>(building);
        ImGui.Separator();
        Theme.Header(Host.ImGui, Sim.Shops.ShopName.Of(_sim, building) is { } called ? $"Part of {called}, a {Buildings.Label(b)}" : $"Part of a {Buildings.Label(b)}");
        foreach (var phase in Enum.GetValues<BuildPhase>())
        {
            if (phase == BuildPhase.Done)
                continue;
            var p = Buildings.Progress(_sim, building, phase);
            if (p.Total == 0)
                continue;
            var name = Buildings.PhaseName(phase);
            if (phase < b.Phase)
                TextC(Good, $"  {name}: done");
            else if (phase == b.Phase && !b.Demolishing)
            {
                if (phase == BuildPhase.Furnish)
                    Bar(name, p.Built / (float)p.Total, Warn, $"{name}: {p.Built}/{p.Total}");
                else
                {
                    Bar($"{name} materials", p.Materials, Warn, $"{name}: materials {p.Materials * 100:F0}%");
                    Bar($"{name} work", p.Work, Good, $"{name}: built {p.Work * 100:F0}%");
                    var site = Buildings.PhaseSite(_sim, building);
                    if (!site.IsNull && _sim.World.TryGet<Construction>(site, out var c))
                    {
                        var cost = c.Target.Building!.Cost;
                        for (var i = 0; i < cost.Count; i++)
                            TextC(Dim, $"    {cost[i].ThingDef.Label}: {c.Delivered[i]}/{cost[i].Count}");
                        var builders = Buildings.Builders(_sim, site, Entity.Null);
                        TextC(Dim, builders > 0 ? $"    {builders} at work (up to {Buildings.MaxBuilders})" : "    Haulers bring materials; builders work as far as they go.");
                    }
                }
                if (p.Blocked > 0)
                    TextC(Bad, $"  {p.Blocked} part{(p.Blocked == 1 ? "" : "s")} can't go up yet: something is in the way (trees to fell, rocks to mine?).");
            }
            else
                TextC(Dim, $"    {name}");
        }
        if (b.Phase == BuildPhase.Done && !b.Demolishing)
        {
            TextC(Good, "Finished.");
            if (View.Prices(building) is { Count: > 0 } sign)
            {
                TextC(Dim, "The sign reads:");
                foreach (var p in sign)
                    TextC(p.Offered ? Theme.Text : Dim, p.Offered ? $"    {p.Label}: {p.Price} silver" : $"    {p.Label}: not to be had");
            }
            if (b.Def.Slots.Count > 0)
            {
                TextC(Dim, "Spots for extras:");
                foreach (var spot in Buildings.Slots(_sim, b))
                {
                    var filled = Buildings.InSlot(_sim, building, spot);
                    if (!filled.IsNull)
                    {
                        TextC(Dim, $"  {_sim.World.Get<Thing>(filled).Label}{(_sim.World.Has<Construction>(filled) ? " (planned)" : "")}");
                        continue;
                    }
                    ImGui.Text($"  {spot.Slot.Label}:");
                    foreach (var def in spot.Slot.ThingDefs)
                    {
                        ImGui.SameLine(0, 6);
                        var problem = Buildings.SlotProblem(_sim, building, spot.Index, def);
                        if (ImGui.SmallButton($"Add {def.Label}##slot{spot.Index}_{def.Name}") && problem == null)
                            Issue(new FillSlotCommand(building, spot.Index, def));
                        if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                            Tip(problem ?? $"{def.Description}\nCost: {string.Join(", ", def.Building!.Cost.Select(c => $"{c.Count} {c.ThingDef.Label}"))}");
                    }
                }
            }
        }
        if (b.Phase == BuildPhase.Done && !b.Demolishing && b.Def.Upgrades.Count > 0)
        {
            TextC(Dim, "Upgrades:");
            for (var i = 0; i < b.Def.Upgrades.Count; i++)
            {
                var u = b.Def.Upgrades[i];
                if (b.Upgrades.Contains(u.Name))
                {
                    TextC(Good, $"  {Cap(u.Label)} on the {u.OnDef.Label}: fitted");
                    continue;
                }
                var site = Buildings.UpgradeSite(_sim, building, u);
                if (!site.IsNull && _sim.World.TryGet<Construction>(site, out var uc))
                {
                    Bar(u.Label, uc.WorkDone / u.Work, Warn, $"{Cap(u.Label)}: materials {Buildings.Delivered(uc) * 100:F0}%, built {uc.WorkDone / u.Work * 100:F0}%");
                    continue;
                }
                var problem = Buildings.UpgradeProblem(_sim, building, i);
                if (ImGui.SmallButton($"Fit a {u.Label}##upgrade{i}") && problem == null)
                    Issue(new InstallUpgradeCommand(building, i));
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                    Tip(problem ?? $"{u.Description}\nOn the {u.OnDef.Label}. Cost: {string.Join(", ", u.Cost.Select(c => $"{c.Count} {c.ThingDef.Label}"))}");
            }
        }
        if (b.Demolishing)
            TextC(Warn, "Being taken down.");
        else if (ImGui.Button(b.Phase == BuildPhase.Done ? "Demolish building"u8 : "Cancel building"u8, in _autoSize))
            Issue(new DemolishBuildingCommand(building));
        ImGui.Separator();
    }

    private void InspectThing(Entity e, Thing thing)
    {
        var w = _sim.World;
        var def = thing.Def;
        InspectorHeader(_renderer.ArtOf(thing).Sprite, _sim.LabelOf(e), thing.Material != null ? $"{def.Category}, made of {thing.Material.Label}" : def.Category.ToString());
        if (!string.IsNullOrEmpty(def.Description))
            Wrapped(def.Description);
        // What it's made of changes it: oak is sturdy and slow to work, pine burns.
        if (thing.Material is { Character.Length: > 0 } material)
            TextC(Dim, $"{char.ToUpperInvariant(material.Label[0])}{material.Label[1..]}: {material.Character}.");
        if (def.HoardValue > 0)
            TextC(Dim, $"Treasure: worth {_sim.World.Get<Thing>(e).Def.HoardValue * (w.TryGet<Stack>(e, out var worth) ? worth.Count : 1):0} (dragons love it; it draws raiders).");
        if (w.TryGet<Stack>(e, out var stack) && stack.Count > 1)
            ImGui.Text($"Count: {stack.Count}");
        Bar("Condition", thing.Hp / thing.MaxHp, thing.Hp < thing.MaxHp * 0.5f ? Bad : Good);
        DrawForSale(e, thing);

        var tile = _sim.LocationOf(e);
        var temp = _sim.TemperatureAt(tile);
        ImGui.Text($"Temperature here: {temp:F1}°C");
        if (w.TryGet<Rot>(e, out var rot))
            SpoilageBar(rot.Progress, temp);

        var building = Buildings.Of(_sim, e);
        if (!building.IsNull)
            InspectBuilding(building);

        if (w.TryGet<Construction>(e, out var c) && !Buildings.IsPhaseSite(_sim, e)) // a phase shows in the building's section
        {
            TextC(Dim, c.MainMaterial is { } made ? $"Under construction, from {made.Label}" : "Under construction");
            var cost = c.Target.Building!.Cost;
            for (var i = 0; i < cost.Count; i++)
                ImGui.Text($"  {cost[i].ThingDef.Label}: {c.Delivered[i]}/{cost[i].Count}");
            Bar("Work", c.WorkDone / c.Target.Building.Work, Good);
            // A building's part can't be cancelled alone (it'd only be laid again): the building can.
            if (building.IsNull && ImGui.Button("Cancel construction"u8, in _autoSize))
            {
                Issue(new CancelConstructionCommand(e));
                _selected = Entity.Null;
            }
        }

        if (def.Building is { IsBed: true } && !w.Has<Construction>(e))
        {
            var forPrisoners = w.Has<ForPrisoners>(e);
            if (ImGui.Checkbox("For prisoners"u8, ref forPrisoners))
                Issue(new PrisonerBedCommand(e, forPrisoners));
            foreach (var o in w.Query<BedOwner>())
                if (w.Get<BedOwner>(o).Bed == e)
                    TextC(Dim, $"Belongs to {w.Get<Creature>(o).Name}");
        }

        if (Crafting.IsStation(_sim, e))
            InspectStation(e);

        if (w.TryGet<Fueled>(e, out var fuel) && def.Building?.Fuel is { } fp)
            Bar($"Fuel ({fuel.Fuel:F1}/{fp.Capacity})", fuel.Fuel / fp.Capacity, fuel.Fuel > 0 ? Good : Bad);

        if (def.Building is { StorageStacks: > 0 } store)
        {
            var held = _sim.StoredIn(e);
            TextC(Dim, $"Holds {held.Count}/{store.StorageStacks} stacks");
            foreach (var item in held)
                Bullet($"{w.Get<Stack>(item).Count} {w.Get<Thing>(item).Label}");
        }

        if (w.TryGet<PlantState>(e, out var plant) && !plant.Ready)
            ImGui.Text($"Regrowing: {SimClock.TicksToDays((long)plant.RegrowTicksLeft) * 24:F1} h left");

        var reservedBy = _sim.Reservations.ReservedBy(e);
        if (w.IsAlive(reservedBy) && View.Omniscient) // who means to use it next is theirs to know
            TextC(Dim, $"Claimed by {w.Get<Creature>(reservedBy).Name}");
    }

    /// <summary>The Work tab's rows: villagers, then the village's dragons.</summary>
    private List<Entity> WorkTabRows() => _sim.Creatures()
        .Where(e => _sim.World.Get<Creature>(e) is { Faction: Faction.Village } c && c.Def.Race is Race.Human or Race.Dragon)
        .OrderBy(e => _sim.World.Get<Creature>(e).Def.Race == Race.Dragon ? 1 : 0)
        .ToList();

    private void DrawWorkTab()
    {
        SetNextWindow(new float2(Gui.S(300), Gui.S(60)), new float2(640, 0));
        Narsil.Ui.Panel.Begin("Work priorities", ref _showWorkTab, ImGuiWindowFlags.AlwaysAutoResize);
        TextC(Dim, "1 = highest, 4 = lowest, 0 = never");
        var types = Enum.GetValues<WorkType>();
        var outer = (float2?)null;
        if (ImGui.BeginTable("work"u8, types.Length + 1, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg, in outer, 0))
        {
            ImGui.TableSetupColumn("Villager"u8, ImGuiTableColumnFlags.None, 0, default);
            foreach (var t in types)
                ImGui.TableSetupColumn(t.ToString(), ImGuiTableColumnFlags.None, 0, default);
            ImGui.TableHeadersRow();
            // Villagers, then the dragons: they only take on what their life stage allows.
            var workers = WorkTabRows();
            foreach (var e in workers)
            {
                var c = _sim.World.Get<Creature>(e);
                var stage = c.Stage(_sim.Clock.Tick);
                var mind = _sim.World.Get<Mind>(e);
                ImGui.TableNextRow(ImGuiTableRowFlags.None, 0);
                ImGui.TableNextColumn();
                if (ImGui.Selectable(c.Name, _selected == e, ImGuiSelectableFlags.None, in _autoSize))
                    _selected = e;
                foreach (var t in types)
                {
                    ImGui.TableNextColumn();
                    if (!c.Def.CanDo(t, stage))
                    {
                        TextC(Dim, "-");
                        if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                            Tip("Can't do this.");
                        continue;
                    }
                    ImGui.PushID(e.Index * 100 + (int)t);
                    ImGui.SetNextItemWidth(50);
                    var p = mind.WorkPriority[(int)t];
                    if (ImGui.DragInt(""u8, ref p, 0.05f, 0, 4, "%d"u8, ImGuiSliderFlags.AlwaysClamp))
                        Issue(new WorkPriorityCommand(e, t, p));
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.None) && Skills.For(t) is { } skill && _sim.World.TryGet<Skills>(e, out var skills))
                        Tip($"{skill} {skills[skill]}");
                    ImGui.PopID();
                }
            }
            ImGui.EndTable();
        }
        Narsil.Ui.Panel.End();
    }

    private readonly List<Message> _witnessedLog = [];
    private long _witnessedTotal = -1;

    /// <summary>Whether this player would know about a message: a public one, or one that happened where they can see.</summary>
    private bool Witnessed(Message m) => m.Location is not { } at || View.CanSeeCell(at);

    /// <summary>
    /// The log this player is entitled to. In a round each message is judged as it arrives, by what
    /// the character could see then, and kept here: by the next frame they may have walked away.
    /// </summary>
    private IReadOnlyList<Message> ShownMessages()
    {
        var log = _sim.Messages;
        if (_sim.Round.Phase == Sim.Round.RoundPhase.None)
            return log.Messages;
        if (_witnessedTotal < 0 || _witnessedTotal > log.Total)
            _witnessedTotal = log.Total; // nothing from before we arrived
        for (var i = log.IndexAfter(_witnessedTotal); i < log.Messages.Count; i++)
            if (Witnessed(log.Messages[i]))
                _witnessedLog.Add(log.Messages[i]);
        _witnessedTotal = log.Total;
        if (_witnessedLog.Count > MessageLog.MaxKept)
            _witnessedLog.RemoveRange(0, _witnessedLog.Count - MessageLog.MaxKept);
        return _witnessedLog;
    }

    /// <summary>
    /// Pause on danger so the player can react, like RimWorld's letters, whether or not the log is
    /// open. Not in co-op: one player's alert shouldn't stop everyone's game.
    /// </summary>
    private void WatchForDanger()
    {
        if (_sim.Messages.Total == _lastDangerCheck)
            return;
        _lastDangerCheck = _sim.Messages.Total;
        var messages = ShownMessages();
        if (AutoPause && _coop is null && Settings.Current.PauseOnDanger && messages.Count > 0 && messages[^1].Kind == MessageKind.Danger && !Paused)
            SetSpeed(0);
    }

    private long _lastDangerCheck;

    /// <summary>The message log, a journal panel (L): toasts announce new messages as they come.</summary>
    private void DrawMessages()
    {
        // God mode has no journal strip: the log is always there, under the clock.
        if (HasAvatar && !_showLog)
            return;
        if (!BeginJournal("Log###messages", ref _showLog, 220))
        {
            Panel.End();
            return;
        }
        var messages = ShownMessages();
        var start = Math.Max(0, messages.Count - 60);
        for (var i = start; i < messages.Count; i++)
        {
            var m = messages[i];
            var color = m.Kind switch
            {
                MessageKind.Good => Good,
                MessageKind.Warning => Warn,
                MessageKind.Danger => Bad,
                _ => Dim,
            };
            var when = (int)(SimClock.TicksToDays(m.Tick + _sim.Clock.StartHour * (long)SimClock.TicksPerHour) * 24 % 24);
            ImGui.PushStyleColor(ImGuiCol.Text, in color);
            ImGui.PushID(i);
            if (ImGui.Selectable($"[{when:00}h] {m.Text}", false, ImGuiSelectableFlags.None, in _autoSize) && m.Location is { } loc)
                _camera.Target = new float3(loc.X + 0.5f, loc.Y + 0.5f, loc.Z);
            ImGui.PopID();
            ImGui.PopStyleColor(1);
        }
        if (_sim.Messages.Total != _lastSeenMessages)
        {
            ImGui.SetScrollHereY(1f);
            _lastSeenMessages = _sim.Messages.Total;
        }
        Panel.End();
    }

    /// <summary>What shelters a roofed cell: the nearest roof, floor or rock above it.</summary>
    private string CoverOf(int3 cell)
    {
        var map = _sim.Map;
        for (var z = cell.Z + 1; z < map.Depth; z++)
        {
            var i = map.Idx(cell.X, cell.Y, z);
            if (map.IsSolid(i))
                return "rock above";
            if (map.HasRoof[i])
                return _sim.World.Get<Thing>(map.Floor[i]).Label;
            if (map.HasFloor[i])
                return "a floor above";
        }
        return "a room's roof";
    }

    /// <summary>
    /// What the cursor is over. For players, only what matters (fire, rock their pick can't break, why
    /// a placement won't go), so nothing trails the cursor over ordinary ground; dev mode shows the
    /// cell, its block and its temperature too.
    /// </summary>
    private void DrawHoverInfo()
    {
        if (!_sim.Map.InBounds(_hoverCell) || Host.Input.MouseCaptured)
            return;
        var map = _sim.Map;
        var i = map.Idx(_hoverCell);
        var lines = new List<string>();
        if (_placeProblem != null)
            lines.Add(_placeProblem);
        if (_devMode)
        {
            var room = _sim.Rooms.RoomAt(_hoverCell);
            var ground = map.IsSolid(i) ? Mining.ShownBlock(_sim, i) : map.GroundUnder(i) is { } ? Mining.ShownBlock(_sim, i - map.LayerSize) : null;
            lines.Add($"{ground?.Label ?? "air"}  ({_hoverCell.X},{_hoverCell.Y},{_hoverCell.Z})");
            lines.Add($"{map.Temperature[i]:F1}°C" + (room is { Indoors: true }
                ? $"  indoors, room {room.AverageTemperature:F0}°C"
                : map.Roofed[i] ? $"  roofed ({CoverOf(_hoverCell)})" : "  outdoors"));
        }
        if (map.Fire[i] > 0)
            lines.Add(_devMode ? $"ON FIRE ({map.Fire[i] * 100:F0}%)" : "On fire!");
        // Rock that wants a better pick: what it takes, and what you (or the village) have.
        if (map.InBounds(_hoverBlock) && map.IsSolid(map.Idx(_hoverBlock))
            && Mining.DigNote(_sim, map.Idx(_hoverBlock), HasAvatar ? _avatar : null) is { } dig)
            lines.Add(dig);
        if (lines.Count > 0)
            Tip(string.Join("\n", lines));
    }

    private ThingDef? _spawnDef;
    private float _climateOffset;

    private void DrawDebug()
    {
        SetNextWindow(new float2(Gui.S(300), Gui.S(400)), new float2(320, 0));
        Narsil.Ui.Panel.Begin("Debug", ref _showDebug, ImGuiWindowFlags.AlwaysAutoResize);

        ImGui.Text($"Tick {_sim.Clock.Tick}  ({_lastTickMs:F3} ms/tick)");
        ImGui.Text($"Entities {_sim.World.AliveCount}  Burning {_sim.Fire.BurningTiles.Count}  Rooms {_sim.Rooms.All.Count}");
        ImGui.Text($"Quads {_quads.LastFrameQuadCount:N0}, chunks rebuilt {_renderer.ChunksRebuiltLastFrame}");

        if (ImGui.CollapsingHeader("Overlays"u8, ImGuiTreeNodeFlags.DefaultOpen))
        {
            var overlay = (int)_renderer.Overlay;
            string[] names = Enum.GetNames<Overlay>();
            for (var k = 0; k < names.Length; k++)
            {
                if (k > 0)
                    ImGui.SameLine(0, -1);
                ImGui.RadioButton(names[k], ref overlay, k);
            }
            _renderer.Overlay = (Overlay)overlay;
            var paths = _renderer.ShowPaths;
            if (ImGui.Checkbox("Paths"u8, ref paths)) _renderer.ShowPaths = paths;
            ImGui.SameLine(0, -1);
            var dark = _renderer.ShowDarkness;
            if (ImGui.Checkbox("Night"u8, ref dark)) _renderer.ShowDarkness = dark;
            ImGui.SameLine(0, -1);
            var names2 = _renderer.ShowNames;
            if (ImGui.Checkbox("Names"u8, ref names2)) _renderer.ShowNames = names2;
            ImGui.SameLine(0, -1);
            var bubbles = _renderer.ShowBubbles;
            if (ImGui.Checkbox("Bubbles"u8, ref bubbles)) _renderer.ShowBubbles = bubbles;
        }

        if (ImGui.CollapsingHeader("Interface"u8, ImGuiTreeNodeFlags.None))
        {
            ImGui.Text($"Particles {_renderer.Particles.Count}, anim clock {_renderer.Animator.Clock:F1}s");
            if (ImGui.Button("Reload art (F5)"u8, in _autoSize))
                Host.Atlas.Reload();
        }

        if (ImGui.CollapsingHeader("Storyteller"u8, ImGuiTreeNodeFlags.None))
        {
            var st = _sim.Storyteller;
            ImGui.Text($"{st.Difficulty?.Label ?? "Off"}: {st.RaidsSent} raids sent, next raid worth {st.RaidPoints(_sim):F0} points");
            if (st.NextRaidTick >= 0)
                ImGui.Text($"Next raid in {(st.NextRaidTick - _sim.Clock.Tick) / (float)SimClock.TicksPerHour:F1} h");
            if (ImGui.Button("Raid now"u8, in _autoSize))
            {
                if (CoopForbids("send a raid") is { } no) Notify(MessageKind.Info, no);
                else st.SendRaid(_sim);
            }
        }

        if (ImGui.CollapsingHeader("Wildlife"u8, ImGuiTreeNodeFlags.None))
        {
            var wl = _sim.Wildlife;
            ImGui.Text($"{(wl.Enabled ? "On" : "Off")}: {wl.LiveAnimals(_sim)} animals, {wl.HerdsArrived} herds arrived");
            if (wl.NextArrivalTick >= 0)
                ImGui.Text($"Next herd in {(wl.NextArrivalTick - _sim.Clock.Tick) / (float)SimClock.TicksPerHour:F1} h");
            if (ImGui.Button("Herd now"u8, in _autoSize))
            {
                if (CoopForbids("send a herd") is { } no) Notify(MessageKind.Info, no);
                else wl.SendHerd(_sim);
            }
        }

        if (ImGui.CollapsingHeader("Dev tools"u8, ImGuiTreeNodeFlags.DefaultOpen))
        {
            ToolButton("Ignite", ToolKind.DevIgnite);
            ImGui.SameLine(0, -1);
            if (ImGui.Button("Extinguish all"u8, in _autoSize))
            {
                if (CoopForbids("put out every fire") is { } no)
                    Notify(MessageKind.Info, no);
                else
                {
                    _sim.Fire.ExtinguishAll(_sim);
                    _sim.Smoke.Clear(_sim);
                }
            }
            var spawnables = _sim.Defs.AllThings.ToList();
            var idx = _spawnDef == null ? 0 : spawnables.IndexOf(_spawnDef);
            ImGui.SetNextItemWidth(150);
            if (ImGui.Combo("##spawn"u8, ref idx, spawnables.Select(d => d.Label).ToArray(), -1))
                _spawnDef = spawnables[idx];
            _spawnDef ??= spawnables[0];
            ImGui.SameLine(0, -1);
            ToolButton("Spawn", ToolKind.DevSpawn, _spawnDef);
            if (_tool == ToolKind.DevSpawn)
                _toolDef = _spawnDef;

            ImGui.SetNextItemWidth(150);
            if (ImGui.SliderFloat("Weather offset °C"u8, ref _climateOffset, -30, 30, "%.0f"u8, ImGuiSliderFlags.None))
                Issue(new DevWeatherCommand(_climateOffset));
            if (ImGui.Button("Skip 1 hour"u8, in _autoSize))
            {
                if (CoopForbids("skip time") is { } no) Notify(MessageKind.Info, no);
                else _sim.TickMany(SimClock.TicksPerHour);
            }
            ImGui.SameLine(0, -1);
            if (ImGui.Button("Wolves"u8, in _autoSize))
                Issue(new DevWildEventCommand(WildEventKind.Predators));
            ImGui.SameLine(0, -1);
            if (ImGui.Button("Migration"u8, in _autoSize))
                Issue(new DevWildEventCommand(WildEventKind.Migration));
            ImGui.SameLine(0, -1);
            // A fight on demand (co-op has no storyteller yet): raiders in off the nearest edge.
            if (ImGui.Button("Send raiders"u8, in _autoSize) && _sim.Defs.AllRaiderKinds.Count > 0)
                Issue(new DevRaidCommand(_sim.Defs.AllRaiderKinds[0], 3));
            ImGui.SameLine(0, -1);
            if (ImGui.Button("Break the bridge"u8, in _autoSize))
                Issue(new DevBreakBridgeCommand());
        }

        if (ImGui.CollapsingHeader("System timings (ms)"u8, ImGuiTreeNodeFlags.None))
        {
            if (ImGui.Button("Profiler graphs"u8))
                _showProfiler = true;
            // Peaks over the last few seconds: a hitch is one slow run, which the latest value rarely shows.
            if (_time - _peaksResetAt > 5)
            {
                _shownPeaks.Clear();
                foreach (var kv in _sim.SystemPeaks)
                    _shownPeaks[kv.Key] = kv.Value;
                _sim.ResetPeaks();
                _peaksResetAt = _time;
            }
            ImGui.TextDisabled("system          last    peak 5 s"u8);
            foreach (var (name, ms) in _sim.SystemTimings.OrderByDescending(kv => _shownPeaks.GetValueOrDefault(kv.Key)))
                ImGui.Text($"{name,-15} {ms,6:F3}  {_shownPeaks.GetValueOrDefault(name),6:F3}");
        }
        Narsil.Ui.Panel.End();
    }
}

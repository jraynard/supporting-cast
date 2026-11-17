using Brutal.ImGuiApi;
using Brutal.Numerics;
using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Ecs;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Ui;
using Narsil.Ui;

namespace HeroesNotIncluded;

// Workbenches: the bills section of a station's inspector (village orders, for anyone), and the
// Adventure station window opened with F (the player making something themselves).
public sealed partial class VillageGame
{
    /// <summary>The station whose window the avatar has open, or Entity.Null.</summary>
    private Entity _openStation = Entity.Null;

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private static string Ingredients(RecipeDef r) =>
        string.Join(", ", r.Ingredients.Select(i => $"{i.Count} {i.MaterialDef?.Describe(i.ThingDef.Label) ?? i.ThingDef.Label}"));

    private static string Products(RecipeDef r) =>
        string.Join(", ", r.Products.Select(p => p.Count > 1 ? $"{p.Count} {p.ThingDef.Label}" : p.ThingDef.Label));

    /// <summary>"needs Crafting 4", "too cold: needs 600°C", or null when nothing stands in the way.</summary>
    /// <summary>"cooking pot": the label of the building upgrade a recipe needs.</summary>
    private string UpgradeLabel(RecipeDef r) =>
        _sim.Defs.AllBuildings.SelectMany(b => b.Upgrades).FirstOrDefault(u => u.Name == r.Upgrade)?.Label ?? r.Upgrade ?? "";

    private string? RecipeBlocker(Entity who, Entity station, RecipeDef r)
    {
        if (!RecipeBooks.Knows(_sim, who, r))
            return "not known yet";
        if (SkillRules.LevelOf(_sim, who, r.Skill) < r.MinSkill)
            return $"needs {r.Skill} {r.MinSkill}";
        if (!Crafting.Fitted(_sim, station, r))
            return $"the {_sim.World.Get<Thing>(station).Def.Label} needs its {UpgradeLabel(r)} first";
        if (Crafting.MissingTool(_sim, who, station, r) is { } tool)
            return $"needs {Crafting.ToolNeed(_sim, r, tool)} on you or beside the {_sim.World.Get<Thing>(station).Def.Label}";
        // A forge lights for the work, so cold isn't a reason not to start: only no fuel is.
        if (!Crafting.CanFireFor(_sim, station, r))
            return r.MinTemp > 0 ? $"needs {r.MinTemp:F0}°C here, and fuel in it" : "the fire isn't lit";
        return null;
    }

    /// <summary>A station's craft under way and its bills, in the inspector.</summary>
    private void InspectStation(Entity e)
    {
        var w = _sim.World;
        var bench = w.Get<Workbench>(e);
        var def = w.Get<Thing>(e).Def;
        ImGui.Separator();
        DrawCraftUnderWay(e, bench);

        Theme.Header(Host.ImGui, "Bills");
        if (bench.Bills.Count == 0)
            TextC(Dim, "No bills: nobody makes anything here until you add one.");
        for (var i = 0; i < bench.Bills.Count; i++)
        {
            var bill = bench.Bills[i];
            var r = bill.Recipe;
            ImGui.PushID(i);
            TextC(bill.Suspended ? Dim : Good, $"{Cap(r.Label)}");
            ImGui.SameLine(0, 6);
            TextC(Dim, bill.Mode switch
            {
                BillMode.Count => $"({bill.Done}/{bill.Target})",
                BillMode.UntilYouHave => $"(until {bill.Target})",
                _ => "(forever)",
            });
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip($"{Ingredients(r)} → {Products(r)}\n{r.Work / SimClock.TicksPerHour:0.#} h of {r.Skill} work at average skill");

            var mode = (int)bill.Mode;
            var modes = new[] { "Do N times", "Until you have N", "Forever" };
            ImGui.SetNextItemWidth(140);
            if (ImGui.BeginCombo("##mode"u8, modes[mode], ImGuiComboFlags.None))
            {
                for (var m = 0; m < modes.Length; m++)
                    if (ImGui.Selectable(modes[m], m == mode, ImGuiSelectableFlags.None, in _autoSize))
                        Issue(new EditBillCommand(e, i, r, (BillMode)m, bill.Target, bill.Suspended, bill.Material));
                ImGui.EndCombo();
            }
            if (bill.Mode != BillMode.Forever)
            {
                ImGui.SameLine(0, 6);
                if (ImGui.SmallButton("-"u8))
                    Issue(new EditBillCommand(e, i, r, bill.Mode, bill.Target - (ImGui.GetIO().KeyShift ? 10 : 1), bill.Suspended, bill.Material));
                ImGui.SameLine(0, 2);
                ImGui.Text($"{bill.Target}");
                ImGui.SameLine(0, 2);
                if (ImGui.SmallButton("+"u8))
                    Issue(new EditBillCommand(e, i, r, bill.Mode, bill.Target + (ImGui.GetIO().KeyShift ? 10 : 1), bill.Suspended, bill.Material));
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                    Tip("Shift: by 10.");
            }

            // Which wood (or other material) it may use, if any ingredient comes in materials.
            var materials = _sim.Defs.AllMaterials.Where(m => r.Ingredients.Exists(x => x.MaterialDef == null && m.Fits(x.ThingDef))).ToList();
            if (materials.Count > 0)
            {
                ImGui.SetNextItemWidth(110);
                if (ImGui.BeginCombo("##material"u8, bill.Material == null ? "any kind" : $"{bill.Material.Label} only", ImGuiComboFlags.None))
                {
                    if (ImGui.Selectable("any kind"u8, bill.Material == null, ImGuiSelectableFlags.None, in _autoSize))
                        Issue(new EditBillCommand(e, i, r, bill.Mode, bill.Target, bill.Suspended, null));
                    foreach (var m in materials)
                        if (ImGui.Selectable($"{m.Label} only", bill.Material == m, ImGuiSelectableFlags.None, in _autoSize))
                            Issue(new EditBillCommand(e, i, r, bill.Mode, bill.Target, bill.Suspended, m));
                    ImGui.EndCombo();
                }
                ImGui.SameLine(0, 6);
            }
            var suspended = bill.Suspended;
            if (ImGui.Checkbox("Paused"u8, ref suspended))
                Issue(new EditBillCommand(e, i, r, bill.Mode, bill.Target, suspended, bill.Material));
            ImGui.SameLine(0, 6);
            if (i > 0 && ImGui.SmallButton("^"u8))
                Issue(new MoveBillCommand(e, i, r, -1));
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip("Earlier bills are made first.");
            ImGui.SameLine(0, 2);
            if (i < bench.Bills.Count - 1 && ImGui.SmallButton("v"u8))
                Issue(new MoveBillCommand(e, i, r, +1));
            ImGui.SameLine(0, 6);
            if (ImGui.SmallButton("Remove"u8))
                Issue(new RemoveBillCommand(e, i, r));
            ImGui.PopID();
        }

        if (bench.Bills.Count < Crafting.MaxBills(def) && ImGui.BeginCombo("##addbill"u8, "Add a bill…"u8, ImGuiComboFlags.None))
        {
            foreach (var r in def.Recipes)
            {
                var known = RecipeBooks.KnownToVillage(_sim, r);
                var fitted = Crafting.Fitted(_sim, e, r);
                var label = !known ? $"??? ({UnlockHint(r)})" : fitted ? $"{Cap(r.Label)}: {Ingredients(r)}" : $"{Cap(r.Label)} (needs a {UpgradeLabel(r)})";
                known &= fitted; // a stew needs the hearth's pot fitted
                if (!known)
                    ImGui.BeginDisabled(true);
                if (ImGui.Selectable($"{label}##{r.Name}", false, ImGuiSelectableFlags.None, in _autoSize))
                    Issue(new AddBillCommand(e, r, BillMode.Count, 1));
                if (!known)
                    ImGui.EndDisabled();
                if (known && r.MinSkill > 0 && ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                    Tip($"Only someone with {r.Skill} {r.MinSkill} or better can make it.");
            }
            ImGui.EndCombo();
        }
    }

    /// <summary>Progress of what's being made at a station, what's been brought, and a way to call it off.</summary>
    private void DrawCraftUnderWay(Entity e, Workbench bench)
    {
        if (bench.Active is not { } r)
        {
            TextC(Dim, "Nothing being made right now.");
            return;
        }
        var by = !bench.By.IsNull && _sim.World.IsAlive(bench.By) ? $" by {_sim.World.Get<Creature>(bench.By).Name}" : "";
        ImGui.Text($"Making {r.Label}{by}");
        var staged = bench.Staged;
        IngredientTiles($"staged-{e.Index}", r.Ingredients, x => r.Ingredients.IndexOf(x) is var k && k >= 0 && k < staged.Length ? staged[k] : 0);
        Bar("Work", bench.Progress / r.Work, Good);
        if (r.MinTemp > 0)
        {
            var t = _sim.TemperatureAt(_sim.World.Get<Thing>(e).Cell);
            TextC(t >= r.MinTemp ? Good : Bad, $"Heat {t:F0}°C (needs {r.MinTemp:F0}°C)");
        }
        if (ImGui.SmallButton("Call it off"u8))
            Issue(new CancelCraftCommand(e));
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
            Tip("What was brought for it is set down by the station; the work done is lost.");
    }

    private string UnlockHint(RecipeDef r) => r.Unlock switch
    {
        { Level: > 0 } u => $"{u.Skill ?? r.Skill} {u.Level}",
        { Discover: { Length: > 0 } d } => RecipeBooks.DiscoverHint(_sim.Defs, d),
        _ => "",
    };

    /// <summary>
    /// The avatar's window at a station: its recipes with what you have toward each (hands, pack,
    /// containers in reach), and gear it could mend, a tab each; the picked recipe's button pinned
    /// at the bottom (<see cref="Sheet"/>) so a long list never hides it. A forge in a round has its
    /// own (<see cref="DrawForgeWindow"/>). Closes when you walk off.
    /// </summary>
    private void DrawStationWindow()
    {
        if (_openStation.IsNull)
            return;
        var w = _sim.World;
        if (!HasAvatar || !Crafting.IsStation(_sim, _openStation)
            || !AvatarSystem.InReach(_sim, w.Get<Position>(_avatar).Cell, _openStation))
        {
            _openStation = Entity.Null;
            return;
        }
        if (Sim.Work.Smithing.AppliesToRepair(_sim, _avatar, _openStation))
        {
            DrawForgeWindow();
            return;
        }
        var thing = w.Get<Thing>(_openStation);
        var bench = w.Get<Workbench>(_openStation);
        var has = HaveToHand();
        var repairs = Repairable(thing.Def);
        var open = true;
        Sheet.Begin($"{Cap(thing.Label)}###station", ref open, 380);
        PadWindow("###station", () => _openStation = Entity.Null);
        var tab = StationTabs((thing.Def.Recipes.Count > 0, MakeTab, false), (repairs.Count > 0, MendTab, CustomerPiece(repairs)));
        if (bench.Active != null)
        {
            DrawCraftUnderWay(_openStation, bench);
            if (bench.Stocked)
                TextC(Good, Controls.Keys("Everything's here: hold ~F~ at it to work."));
            ImGui.Separator();
        }
        var busy = !bench.Untouched && (bench.By != _avatar || bench.Active == null);
        if (tab == MendTab)
            DrawRepairs(repairs);
        else
            DrawRecipePicker(thing.Def.Recipes, bench, has);
        Sheet.Footer();
        if (tab == MendTab)
            TextC(Dim, Controls.Keys("Mending's worked like making: hold ~F~ at it."));
        else
            DrawRecipePick(thing.Def.Recipes, bench, busy, thing.Def, has);
        Sheet.End();
        if (!open)
            _openStation = Entity.Null;
    }

    private const string MakeTab = "Make", SmeltTab = "Smelt", MendTab = "Mend";

    /// <summary>The tab shown in the station window, and the station it was chosen at (a new one chooses again).</summary>
    private string _stationTab = MakeTab;
    private Entity _stationTabAt = Entity.Null;

    /// <summary>
    /// The station window's tabs, those that apply, as a row of buttons (none with only one); the
    /// one shown lit, one with something waiting marked. Opening at a new station, a marked tab is
    /// shown first (a customer's piece to mend), else <paramref name="first"/>, else the first.
    /// Returns the tab to show.
    /// </summary>
    private string StationTabs(params (bool Applies, string Name, bool Waiting)[] tabs) => StationTabs(null, tabs);

    private string StationTabs(string? first, params (bool Applies, string Name, bool Waiting)[] tabs)
    {
        var shown = tabs.Where(t => t.Applies).ToList();
        if (shown.Count == 0)
            return MakeTab;
        if (_stationTabAt != _openStation || !shown.Exists(t => t.Name == _stationTab))
        {
            _stationTabAt = _openStation;
            _stationTab = shown.Find(t => t.Waiting).Name ?? (first != null && shown.Exists(t => t.Name == first) ? first : shown[0].Name);
        }
        if (shown.Count < 2)
            return _stationTab;
        for (var i = 0; i < shown.Count; i++)
        {
            if (i > 0)
                ImGui.SameLine(0, Gui.S(4));
            var on = shown[i].Name == _stationTab;
            if (on)
                ImGui.PushStyleColor(ImGuiCol.Button, in Theme.AccentSoft);
            if (ImGui.Button($"{shown[i].Name}##station-tab"))
                _stationTab = shown[i].Name;
            if (on)
                ImGui.PopStyleColor(1);
            if (shown[i].Waiting)
                UnreadDot();
            GuideGlow(shown[i].Name == MendTab && !on && TutorialStep == "mend");
        }
        Gui.Rule();
        return _stationTab;
    }

    /// <summary>What the player has to hand (hands, pack, containers in reach), by def and material.</summary>
    private Func<ThingCount, int> HaveToHand()
    {
        var w = _sim.World;
        var have = new Dictionary<(ThingDef, MaterialDef?), int>();
        foreach (var item in StationSources())
        {
            var t = w.Get<Thing>(item);
            have[(t.Def, t.Material)] = have.GetValueOrDefault((t.Def, t.Material)) + w.Get<Stack>(item).Count;
        }
        return x => have.Where(kv => kv.Key.Item1 == x.ThingDef && (x.MaterialDef == null || kv.Key.Item2 == x.MaterialDef)).Sum(kv => kv.Value);
    }

    /// <summary>The recipe picked in a station's picker (kept while it's among the station's recipes).</summary>
    private RecipeDef? _recipePick;

    /// <summary>
    /// A station's recipes as tiles, each its product's picture (dimmed while you're short of what
    /// it takes, a "?" while you don't know it). The one picked is shown with its button by
    /// <see cref="DrawRecipePick"/>, in the window's footer.
    /// </summary>
    private void DrawRecipePicker(IReadOnlyList<RecipeDef> recipes, Workbench bench, Func<ThingCount, int> has)
    {
        if (recipes.Count == 0)
            return;
        bool Knows(RecipeDef r) => RecipeBooks.Knows(_sim, _avatar, r);
        bool Ready(RecipeDef r) => Knows(r) && RecipeBlocker(_avatar, _openStation, r) == null && r.Ingredients.TrueForAll(x => has(x) >= x.Count);
        if (_recipePick == null || !recipes.Contains(_recipePick))
            _recipePick = bench.Active is { } active && recipes.Contains(active) ? active
                : recipes.FirstOrDefault(Ready) ?? recipes.FirstOrDefault(Knows);
        var across = TilesAcross();
        for (var i = 0; i < recipes.Count; i++)
        {
            var r = recipes[i];
            TileFlow(i, across);
            var known = Knows(r);
            var product = known && r.Products.Count > 0 ? r.Products[0].ThingDef : null;
            var tip = known ? $"{Cap(r.Label)}: {string.Join(", ", r.Ingredients.Select(x => $"{x.Count} {x.ThingDef.Label}"))}" : $"??? ({UnlockHint(r)})";
            if (DefTile($"recipe-{r.Name}", product, tip, selected: r == _recipePick, dim: known && !Ready(r), enabled: known))
                _recipePick = r;
        }
    }

    /// <summary>The recipe picked among <paramref name="recipes"/>: what it takes as have/need tiles, and Make (or Bring more, once begun).</summary>
    private void DrawRecipePick(IReadOnlyList<RecipeDef> recipes, Workbench bench, bool busy, ThingDef station, Func<ThingCount, int> has)
    {
        if (_recipePick is not { } pick || !recipes.Contains(pick) || !RecipeBooks.Knows(_sim, _avatar, pick))
        {
            TextC(Dim, "Pick something to make.");
            return;
        }
        var blocker = RecipeBlocker(_avatar, _openStation, pick);
        var mine = bench.Active == pick && (bench.By == _avatar || bench.By.IsNull);
        var enabled = blocker == null && (!busy || mine);
        ImGui.Text(Cap(pick.Label));
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
            Tip($"Makes {Products(pick)}. About {pick.Work / SimClock.TicksPerHour:0.#} h of {pick.Skill} work.");
        IngredientTiles($"pick-{pick.Name}", pick.Ingredients, x => mine && bench.Stocked ? x.Count : has(x));
        if (!enabled)
            ImGui.BeginDisabled(true);
        if (ImGui.Button(mine ? "Bring more" : "Make", in _autoSize))
            Issue(new CraftCommand(_avatar, _openStation, pick));
        if (!enabled)
            ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            Tip(blocker != null ? $"You can't yet: {blocker}."
                : busy && !mine ? $"The {station.Label} is busy with {bench.Active!.Label}; call that off first."
                : "Brings what you have of it from your hands, pack and any chest or wagon in reach; once it's all here, hold " + Controls.Glyph(Act.Use) + " to work.");
    }

    // The forge window's picks: a metal, then a shape in it.
    private string? _forgeMetal;
    private RecipeDef? _forgeShape;

    /// <summary>
    /// The forge in a round, in tabs: Make (a metal, then a shape), Smelt (ore to bars) and Mend.
    /// The shape picked and its Begin sit pinned at the bottom (<see cref="Sheet"/>): on a Steam
    /// Deck the one long column ran past the screen, Begin and the mending with it. Begin puts the
    /// billet in your tongs and closes the window (the fire and the piece are the tongs panel's).
    /// </summary>
    private void DrawForgeWindow()
    {
        var w = _sim.World;
        // Begun: the window's done its job. The piece and the fire are shown in the tongs panel.
        if (View.Piece(_avatar) != null)
        {
            _openStation = Entity.Null;
            return;
        }
        var thing = w.Get<Thing>(_openStation);
        var bench = w.Get<Workbench>(_openStation);
        var has = HaveToHand();
        var blooms = thing.Def.Recipes.Where(r => r.Bloom && Sim.Work.Smithing.Applies(_sim, _avatar, _openStation, r) && RecipeBooks.Knows(_sim, _avatar, r)).ToList();
        var gear = thing.Def.Recipes.Where(r => !r.Bloom && Sim.Work.Smithing.Applies(_sim, _avatar, _openStation, r)).ToList();
        var other = thing.Def.Recipes.Where(r => !gear.Contains(r) && !blooms.Contains(r)).ToList();
        var metals = gear.GroupBy(MetalOf).OrderBy(g => g.Min(r => r.MinTemp)).ToList();
        var repairs = Repairable(thing.Def);
        // Nothing worked to hand but ore you know how to smelt: start where the work is.
        var barsToHand = metals.Any(g => g.Any(r => RecipeBooks.Knows(_sim, _avatar, r)) && g.First().Ingredients.Count > 0 && has(g.First().Ingredients[0]) > 0);
        var oreToHand = blooms.Any(r => r.Ingredients.Count > 0 && has(r.Ingredients[0]) > 0);

        var open = true;
        Sheet.Begin($"{Cap(thing.Label)}###station", ref open, 400);
        PadWindow("###station", () => _openStation = Entity.Null);
        var tab = StationTabs(!barsToHand && oreToHand ? SmeltTab : null,
            (gear.Count > 0, MakeTab, false), (blooms.Count + other.Count > 0, SmeltTab, false), (repairs.Count > 0, MendTab, CustomerPiece(repairs)));
        if (bench.Active != null && bench.By != _avatar)
        {
            DrawCraftUnderWay(_openStation, bench);
            ImGui.Separator();
        }
        var busyOther = !bench.Untouched && (bench.By != _avatar || bench.Active == null);
        switch (tab)
        {
            case SmeltTab:
                // Smelting: ore to bars, a bloom at a time, before there's metal to work.
                foreach (var r in blooms)
                    DrawBloomRow(r, bench, thing.Def, has);
                if (blooms.Count > 0 && other.Count > 0)
                    ImGui.Separator();
                DrawRecipePicker(other, bench, has);
                break;
            case MendTab:
                DrawRepairs(repairs);
                break;
            default:
                DrawForgeMake(metals, has);
                break;
        }
        Sheet.Footer();
        switch (tab)
        {
            case SmeltTab when other.Count > 0:
                DrawRecipePick(other, bench, busyOther, thing.Def, has);
                break;
            case SmeltTab:
                TextC(Dim, "Keep the fire orange while the bloom comes together.");
                break;
            case MendTab:
                TextC(Dim, "Mending takes it in your tongs: heat it, then hammer out the chips.");
                break;
            default:
                DrawForgeBegin(thing, bench, has);
                break;
        }
        Sheet.End();
        if (!open)
            _openStation = Entity.Null;
    }

    /// <summary>The forge's Make tab: the metal, a chip each with the bars you have of it, then its shapes.</summary>
    private void DrawForgeMake(List<IGrouping<string, RecipeDef>> metals, Func<ThingCount, int> has)
    {
        if (_forgeMetal == null || metals.All(g => g.Key != _forgeMetal))
            // The metal you've most of to hand, among those you know how to work.
            _forgeMetal = metals.Where(g => g.Any(r => RecipeBooks.Knows(_sim, _avatar, r)))
                .OrderByDescending(g => g.First().Ingredients.Count > 0 ? has(g.First().Ingredients[0]) : 0).FirstOrDefault()?.Key;

        // 1. The metal: a chip each, with the bars you have of it.
        TextC(Dim, "Metal");
        var first = true;
        foreach (var g in metals)
        {
            if (!first)
                ImGui.SameLine(0, 6);
            first = false;
            var bars = g.First().Ingredients.Count > 0 ? has(g.First().Ingredients[0]) : 0;
            var known = g.Any(r => RecipeBooks.Knows(_sim, _avatar, r));
            if (!known)
                ImGui.BeginDisabled(true);
            var picked = g.Key == _forgeMetal;
            if (picked)
                ImGui.PushStyleColor(ImGuiCol.Button, in Theme.AccentSoft);
            if (ImGui.Button($"{g.Key} ({bars})###metal{g.Key}"))
            {
                _forgeMetal = g.Key;
                _forgeShape = null;
            }
            if (picked)
                ImGui.PopStyleColor(1);
            if (!known)
                ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                Tip(known ? $"{bars} {g.First().Ingredients[0].ThingDef.Label} to hand. Worked {Sim.Work.Smithing.WorkingColours(g.First())}."
                    : "You don't know how to work this yet.");
        }

        // 2. The shape: an icon each, in rows.
        var shapes = metals.FirstOrDefault(g => g.Key == _forgeMetal)?.ToList() ?? [];
        if (_forgeShape != null && !shapes.Contains(_forgeShape))
            _forgeShape = null;
        ImGui.Spacing();
        TextC(Dim, "Make");
        const int perRow = 6;
        for (var i = 0; i < shapes.Count; i++)
        {
            var r = shapes[i];
            if (i % perRow != 0)
                ImGui.SameLine(0, 4);
            var product = r.Products.Count > 0 ? r.Products[0].ThingDef : null;
            var icon = product is { Icon.Length: > 0 } ? product.Icon : product?.Sprite ?? "";
            var known = RecipeBooks.Knows(_sim, _avatar, r);
            var ready = known && RecipeBlocker(_avatar, _openStation, r) == null && r.Ingredients.TrueForAll(x => has(x) >= x.Count);
            if (!known)
                ImGui.BeginDisabled(true);
            if (Narsil.Ui.Gui.IconButton($"##shape{r.Name}", icon, 44, r == _forgeShape))
                _forgeShape = r;
            if (!known)
                ImGui.EndDisabled();
            if (!ready && known)
            {
                // Dim what you can't make yet without hiding it.
                var min = ImGui.GetItemRectMin();
                var max = ImGui.GetItemRectMax();
                ImGui.GetWindowDrawList().AddRectFilled(in min, in max, Theme.U8(Theme.Well, 0.45f), 0f, ImDrawFlags.None);
            }
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                Tip(known ? $"{Cap(r.Label)}: {string.Join(", ", r.Ingredients.Select(x => $"{x.Count} {x.ThingDef.Label}"))}" : $"??? ({UnlockHint(r)})");
        }
    }

    /// <summary>The forge's footer on the Make tab: the shape picked, what it takes, and Begin.</summary>
    private void DrawForgeBegin(Thing thing, Workbench bench, Func<ThingCount, int> has)
    {
        if (_forgeShape is { } pick)
        {
            var blocker = RecipeBlocker(_avatar, _openStation, pick);
            var mine = bench.Active == pick && (bench.By == _avatar || bench.By.IsNull);
            var busy = !bench.Untouched && (bench.By != _avatar || bench.Active == null) && !mine;
            var stocked = pick.Ingredients.TrueForAll(x => has(x) >= x.Count) || (mine && bench.Stocked);
            ImGui.Text(Cap(pick.Label));
            IngredientTiles($"shape-{pick.Name}", pick.Ingredients, x => mine && bench.Stocked ? x.Count : has(x));
            var how = $"Work it {Sim.Work.Smithing.WorkingColours(pick)}; about {Sim.Work.Smithing.BlowsFor(pick)} good blows"
                + (Sim.Work.Smithing.NeedsQuench(pick) ? ", then the quench." : ".");
            var enabled = blocker == null && !busy;
            if (!enabled)
                ImGui.BeginDisabled(true);
            if (ImGui.Button(stocked ? "Begin" : "Bring what I have", in _autoSize))
                Issue(new CraftCommand(_avatar, _openStation, pick));
            if (!enabled)
                ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                Tip(blocker != null ? $"You can't yet: {blocker}."
                    : busy ? $"The {thing.Def.Label} is busy with {bench.Active!.Label}; call that off first."
                    : stocked ? how + "\nTakes what it needs from your hands, pack and any chest in reach and puts the billet in your tongs. Then " + Controls.Glyph(Act.Use) + " at the fire."
                    : how + "\nBrings what you have of it to the forge; the rest can follow.");
        }
        else
            TextC(Dim, "Pick something to make.");
    }

    /// <summary>A smelting recipe run as a bloom by hand: the ore to hand, what it takes, and the button.</summary>
    private void DrawBloomRow(RecipeDef r, Workbench bench, ThingDef station, Func<ThingCount, int> has)
    {
        var ore = r.Ingredients[0];
        var blocker = RecipeBlocker(_avatar, _openStation, r);
        var mine = bench.Active == r && (bench.By == _avatar || bench.By.IsNull);
        var busy = !bench.Untouched && (bench.By != _avatar || bench.Active == null) && !mine;
        var stocked = r.Ingredients.TrueForAll(x => has(x) >= x.Count) || (mine && bench.Stocked);
        TextC(Dim, "Smelt");
        ImGui.SameLine(0, Gui.S(6));
        ImGui.Text($"{Cap(r.Label)} from {ore.ThingDef.Label}");
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
            Tip("Keep the fire orange while the bloom comes together, then beat the slag out on the anvil. A good bloom gives a bar more; too hot and the iron runs to slag.");
        IngredientTiles($"bloom-{r.Name}", r.Ingredients, x => mine && bench.Stocked ? x.Count : has(x));
        var enabled = blocker == null && !busy;
        if (!enabled)
            ImGui.BeginDisabled(true);
        if (ImGui.Button($"{(stocked ? "Smelt a bloom" : "Bring what I have")}###bloom{r.Name}", in _autoSize))
            Issue(new CraftCommand(_avatar, _openStation, r));
        if (!enabled)
            ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            Tip(blocker != null ? $"You can't yet: {blocker}."
                : busy ? $"The {station.Label} is busy with {bench.Active!.Label}; call that off first."
                : stocked ? "Takes the ore and charcoal from your hands, pack and any chest in reach and puts them in the forge. Then " + Controls.Glyph(Act.Use) + " at the fire."
                : "Brings what you have of it to the forge; the rest can follow.");
    }

    /// <summary>The metal a forge recipe works (the first thing that goes in, less its "bar").</summary>
    private static string MetalOf(RecipeDef r)
    {
        if (r.Ingredients.Count == 0)
            return "Other";
        var label = r.Ingredients[0].ThingDef.Label;
        foreach (var tail in new[] { " bar", " bars", " ingot" })
            if (label.EndsWith(tail, StringComparison.Ordinal))
                label = label[..^tail.Length];
        return Cap(label);
    }

    /// <summary>Gear the avatar has on them that this station could mend, and what each would take, the pieces customers left first.</summary>
    private List<(Entity Item, (ThingCount Material, int Count, float Work) Cost)> Repairable(ThingDef station)
    {
        var w = _sim.World;
        var gear = _sim.Worn(_avatar).ToList();
        if (w.TryGet<Equipment>(_avatar, out var eq) && w.IsAlive(eq.Weapon))
            gear.Add(eq.Weapon);
        if (w.Get<Carrying>(_avatar).Item is { IsNull: false } held && w.IsAlive(held))
            gear.Add(held);
        if (_sim.PackOf(_avatar) is { IsNull: false } pack)
            gear.AddRange(_sim.StoredIn(pack));
        var mendable = new List<(Entity Item, (ThingCount Material, int Count, float Work) Cost)>();
        foreach (var item in gear.OrderBy(i => w.Has<Entrusted>(i) ? 0 : 1))
            if (Crafting.RepairCost(_sim, station, item) is { } cost)
                mendable.Add((item, cost));
        return mendable;
    }

    /// <summary>A customer's piece among what could be mended: the Mend tab is marked, and shown first.</summary>
    private bool CustomerPiece(List<(Entity Item, (ThingCount Material, int Count, float Work) Cost)> repairs) => repairs.Exists(r => _sim.World.Has<Entrusted>(r.Item));

    /// <summary>The Mend tab: a button for each piece, whose it is, how it's faring and what mending it takes.</summary>
    private void DrawRepairs(List<(Entity Item, (ThingCount Material, int Count, float Work) Cost)> repairs)
    {
        var w = _sim.World;
        foreach (var (item, cost) in repairs)
        {
            ImGui.PushID(item.Index);
            if (ImGui.Button("Mend"u8, in _autoSize))
                Issue(new RepairCommand(_avatar, _openStation, item));
            GuideGlow(TutorialStep == "mend");
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip(Sim.Work.Smithing.AppliesToRepair(_sim, _avatar, _openStation)
                    ? "Take it in your tongs: heat it, then hammer out the chips on the anvil. It comes off dull: grind it after."
                    : Controls.Keys("Hold ~F~ at the station to work; the material is used once it's done."));
            ImGui.SameLine(0, 8);
            var t = w.Get<Thing>(item);
            var whose = w.TryGet<Entrusted>(item, out var e) && w.IsAlive(e.Owner) && w.TryGet<Creature>(e.Owner, out var owner) ? $"{owner.Name}'s " : "";
            Wrapped($"{whose}{_sim.LabelOf(item)} ({View.Condition(item) ?? $"{t.Hp / t.MaxHp * 100:F0}%"}): {cost.Count} {cost.Material.ThingDef.Label}");
            ImGui.PopID();
        }
    }

    private bool _showRecipeBook;

    /// <summary>
    /// Every recipe, by where it's made: what it takes and makes, or "???" with a hint while it's
    /// unknown. In Adventure mode it's the player's own book (and hand recipes can be made from it);
    /// otherwise the village's.
    /// </summary>
    private void DrawRecipeBook()
    {
        if (!_showRecipeBook)
            return;
        var open = true;
        var bookSize = new float2(Narsil.Ui.Gui.S(520), Narsil.Ui.Gui.S(460));
        ImGui.SetNextWindowSize(in bookSize, ImGuiCond.FirstUseEver);
        Narsil.Ui.Panel.Begin(HasAvatar ? "Your recipe book###recipes" : "The village's recipe book###recipes", ref open, ImGuiWindowFlags.NoCollapse);
        PadWindow("###recipes", () => _showRecipeBook = false);
        var groups = _sim.Defs.AllRecipes.GroupBy(r => r.StationDef).OrderBy(g => g.Key == null ? -1 : g.Key.Index);
        foreach (var group in groups)
        {
            Theme.Header(Host.ImGui, group.Key == null ? "By hand, anywhere" : $"At a {group.Key.Label}");
            foreach (var r in group)
            {
                ImGui.PushID(r.Name);
                var known = HasAvatar ? RecipeBooks.Knows(_sim, _avatar, r) : RecipeBooks.KnownToVillage(_sim, r);
                if (!known)
                {
                    TextC(Dim, $"  ??? ({UnlockHint(r)})");
                    ImGui.PopID();
                    continue;
                }
                if (r.IsHandRecipe && HasAvatar)
                {
                    var skill = SkillRules.LevelOf(_sim, _avatar, r.Skill) >= r.MinSkill;
                    if (!skill)
                        ImGui.BeginDisabled(true);
                    if (ImGui.SmallButton("Make"u8))
                        Issue(new HandCraftCommand(_avatar, r));
                    if (!skill)
                        ImGui.EndDisabled();
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                        Tip(skill ? "Made where you stand from what you carry; walking off stops it." : $"Needs {r.Skill} {r.MinSkill}.");
                    ImGui.SameLine(0, 6);
                }
                // One line a recipe: what it makes (its picture and name) and from what; what it
                // needs and what speeds it in the tooltip.
                var line = ImGui.GetTextLineHeight();
                if (DefSprite(r.Products.Count > 0 ? r.Products[0].ThingDef : null) is { } art)
                    DrawSprite(ImGui.GetWindowDrawList(), art, ImGui.GetCursorScreenPos(), new float2(line, line));
                ImGui.Dummy(new float2(line, line));
                ImGui.SameLine(0, 6);
                ImGui.BeginGroup();
                ImGui.Text($"{Cap(r.Label)}");
                ImGui.SameLine(0, 6);
                TextC(Dim, $"{Ingredients(r)} → {Products(r)}");
                ImGui.EndGroup();
                var needs = new List<string>();
                if (r.MinSkill > 0)
                    needs.Add($"{r.Skill} {r.MinSkill}");
                if (r.MinTemp > 0)
                    needs.Add($"{r.MinTemp:F0}°C");
                needs.AddRange(r.Tools.Select(t => $"a {t.ToLowerInvariant()}"));
                if ((needs.Count > 0 || r.HelpedBy.Count > 0) && ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                {
                    var faster = r.HelpedBy.Count > 0 ? $"{(needs.Count > 0 ? "; " : "")}quicker with a {string.Join(" or ", r.HelpedBy.Select(t => t.ToLowerInvariant()))}" : "";
                    Tip(Cap($"{(needs.Count > 0 ? "needs " + string.Join(", ", needs) : "")}{faster}."));
                }
                ImGui.PopID();
            }
        }
        Narsil.Ui.Panel.End();
        if (!open)
            _showRecipeBook = false;
    }

    /// <summary>Stacks the avatar could bring to a station: in hand, in the pack, in containers in reach.</summary>
    private IEnumerable<Entity> StationSources()
    {
        var w = _sim.World;
        if (w.Get<Carrying>(_avatar).Item is { IsNull: false } held && w.IsAlive(held))
            yield return held;
        if (_sim.PackOf(_avatar) is { IsNull: false } pack)
            foreach (var e in _sim.StoredIn(pack))
                yield return e;
        var at = w.Get<Position>(_avatar).Cell;
        for (var dz = -1; dz <= 1; dz++)
            for (var dy = -1; dy <= 1; dy++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    var c = at + new int3(dx, dy, dz);
                    if (!_sim.Map.InBounds(c) || !AvatarSystem.InReach(at, c))
                        continue;
                    var s = _sim.Map.Structure[_sim.Map.Idx(c)];
                    if (!s.IsNull && w.Get<Thing>(s).Def.Building is { StorageStacks: > 0 } && !w.Has<Construction>(s))
                        foreach (var e in _sim.StoredIn(s))
                            yield return e;
                }
    }
}

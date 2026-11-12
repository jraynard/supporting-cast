using Brutal.ImGuiApi;
using Narsil.Platform;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Core;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Ecs;
using SupportingCast.Sim.Systems;
using SupportingCast.Ui;

namespace SupportingCast;

/// <summary>
/// Adventure mode's inventory: the window (Tab) with what you wear, hold and carry in your pack, and
/// the hotbar (1–4) that swaps a pack stack into your hands. Clicking an item's slot opens what
/// can be done with it (eat it, hold it, wear it, drop it). Every change is a command
/// (<c>Core/InventoryCommands.cs</c>); this only reads the sim.
/// </summary>
public sealed partial class VillageGame
{
    private bool _inventoryOpen;

    /// <summary>The toolbar's height last frame, so the hotbar sits just above it.</summary>
    private float _toolbarHeight;

    /// <summary>Hotbar slots: the pack's first stacks, in pack order.</summary>
    private const int HotbarSlots = 4;

    private static readonly GlfwKey[] HotbarKeys = [GlfwKey.Number1, GlfwKey.Number2, GlfwKey.Number3, GlfwKey.Number4];

    private const float IconSize = 30;

    /// <summary>Tab toggles the window; 1–4 take a pack stack in hand (an empty slot's key puts what you hold away).</summary>
    private void HandleInventoryKeys(Input input)
    {
        if (input.WasKeyPressed(GlfwKey.Tab))
            _inventoryOpen = !_inventoryOpen;
        if (input.IsKeyDown(GlfwKey.LeftControl) || input.IsKeyDown(GlfwKey.RightControl))
            return; // Ctrl+1–3 is game speed
        var pack = _sim.PackOf(_avatar);
        if (pack.IsNull)
            return;
        var items = _sim.StoredIn(pack);
        for (var k = 0; k < HotbarKeys.Length; k++)
        {
            if (!input.WasKeyPressed(HotbarKeys[k]))
                continue;
            if (k < items.Count)
                Issue(new HoldCommand(_avatar, items[k]));
            else
                Issue(new StowCommand(_avatar));
        }
    }

    /// <summary>The pack's first stacks and what's in hand, bottom left, with the pack's load.</summary>
    private void DrawHotbar()
    {
        if (!HasAvatar)
            return;
        var w = _sim.World;
        var pack = _sim.PackOf(_avatar);
        var screen = ImGui.GetIO().DisplaySize;
        // Centred just above the toolbar.
        var pos = new float2(screen.X * 0.5f, screen.Y - 10 - _toolbarHeight - 6);
        float2? pivot = new float2(0.5f, 1f);
        ImGui.SetNextWindowPos(in pos, ImGuiCond.Always, in pivot);
        ImGui.SetNextWindowBgAlpha(0.8f);
        ImGui.Begin("##hotbar"u8, ImGuiWindowFlags.NoDecoration | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings
            | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav);
        var held = w.Get<Carrying>(_avatar).Item;
        SlotBox("hands", held, "In hand", "Empty hands. 1–4 take a stack from your pack; X puts down what you hold.", menu: true);
        if (!pack.IsNull)
        {
            var items = _sim.StoredIn(pack);
            for (var k = 0; k < HotbarSlots; k++)
            {
                ImGui.SameLine(0, 4);
                SlotBox($"hot{k}", k < items.Count ? items[k] : Entity.Null, $"{k + 1}",
                    k < items.Count ? $"[{k + 1}] Take in hand (what you hold goes in the pack)" : $"[{k + 1}] Put what you hold in the pack", menu: true);
            }
            ImGui.SameLine(0, 8);
            LoadBar(pack, 90);
        }
        else
        {
            ImGui.SameLine(0, 8);
            TextC(Dim, "No pack: Tab for gear");
        }
        ImGui.End();
    }

    /// <summary>
    /// A framed slot showing an item (icon and count) or nothing, with a corner label and a tooltip.
    /// With <paramref name="menu"/>, clicking the item opens its <see cref="ItemMenu"/>.
    /// </summary>
    private void SlotBox(string id, Entity item, string corner, string emptyTip, bool menu = false)
    {
        var w = _sim.World;
        var dl = ImGui.GetWindowDrawList();
        var min = ImGui.GetCursorScreenPos();
        var box = new float2(IconSize + 10, IconSize + 10);
        var max = min + box;
        dl.AddRectFilled(in min, in max, Theme.U8(new float4(0, 0, 0, 0.35f)), 4f, ImDrawFlags.None);
        dl.AddRect(in min, in max, Theme.U8(Theme.Dim), 4f, ImDrawFlags.None, 1f);
        var alive = !item.IsNull && w.IsAlive(item);
        if (alive)
        {
            var (sprite, tint) = _renderer.ArtOf(w.Get<Thing>(item));
            DrawSprite(dl, sprite, min + new float2(5, 5), new float2(IconSize, IconSize), Color8(tint));
            if (w.TryGet<Stack>(item, out var stack) && stack.Count > 1)
            {
                var text = stack.Count.ToString();
                var at = max - new float2(4 + 7 * text.Length, 17);
                var shadow = at + new float2(1, 1);
                dl.AddText(in shadow, new ImColor8(0, 0, 0, 220), text);
                dl.AddText(in at, Theme.U8(Theme.Text), text);
            }
        }
        var c = min + new float2(3, 1);
        dl.AddText(in c, Theme.U8(Theme.Dim), corner.Length <= 2 ? corner : "");
        ImGui.Dummy(in box);
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
            Tip(alive ? $"{ItemLine(item)}\n{ItemDetails(item)}{(menu ? "\nClick for what you can do with it." : "")}" : emptyTip);
        if (!menu || !alive)
            return;
        if (ImGui.IsItemClicked(ImGuiMouseButton.Left) || ImGui.IsItemClicked(ImGuiMouseButton.Right))
            ImGui.OpenPopup(id);
        if (ImGui.BeginPopup(id))
        {
            ItemMenu(item);
            ImGui.EndPopup();
        }
    }

    /// <summary>
    /// What can be done with an item of yours or in reach: eat it (only from here, never from F),
    /// hold it, wear or wield it, put it in your pack, put it down. Used by the slots' popups and the
    /// world's right-click menu.
    /// </summary>
    private void ItemMenu(Entity item)
    {
        var w = _sim.World;
        if (!w.IsAlive(item) || !HasAvatar)
        {
            ImGui.CloseCurrentPopup();
            return;
        }
        var def = w.Get<Thing>(item).Def;
        var held = w.Get<Carrying>(_avatar).Item == item;
        var packed = Inventory.InOwnPack(_sim, _avatar, item);
        var pack = _sim.PackOf(_avatar);
        ImGui.TextDisabled(ItemLine(item));
        ImGui.Separator();
        var any = false;
        if (def.IsFood && AvatarSystem.CanEat(_sim, _avatar, item))
        {
            any = true;
            if (ImGui.MenuItem($"Eat {def.Label}"))
                Issue(new EatCommand(_avatar, item));
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip(EatTip(def));
        }
        if ((held || packed) && Bandaging.IsBandage(_sim, item))
        {
            // Bind wounds: your own, or anyone's beside you (your dragon). In a fight, it's the combat panel's.
            any = true;
            foreach (var patient in BandagePatients())
            {
                var wounds = Bandaging.WoundsToBind(_sim, patient);
                var label = patient == _avatar ? $"Bandage your wounds ({wounds})" : $"Bandage {w.Get<Creature>(patient).Name} ({wounds} wounds)";
                var why = Bandaging.CantBind(_sim, _avatar, patient);
                ImGui.BeginDisabled(why != null);
                if (ImGui.MenuItem(label))
                    Issue(new BandageCommand(_avatar, patient));
                ImGui.EndDisabled();
                if (why != null && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    Tip(why);
            }
        }
        if (packed)
        {
            any = true;
            if (ImGui.MenuItem("Hold"u8))
                Issue(new HoldCommand(_avatar, item));
        }
        if ((held || packed) && (def.Apparel != null || def.Weapon != null))
        {
            any = true;
            if (ImGui.MenuItem(def.Weapon != null ? "Wield"u8 : "Wear"u8))
                Issue(new EquipCommand(_avatar, item));
        }
        if ((held || packed) && Sim.Combat.WeaponWear.Edged(def))
        {
            any = true;
            var why = Sim.Work.SharpenSession.Problem(_sim, _avatar, item);
            ImGui.BeginDisabled(why != null);
            if (ImGui.MenuItem("Sharpen"u8))
                Issue(new Sim.Work.SharpenCommand(_avatar, item));
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                Tip(why != null ? Cap(why) + "." : "Hold F to grind, A/D to keep the angle. Ease off before the steel overheats.");
        }
        if ((held || packed) && View.Strongbox(item) is { } box)
        {
            // Feel for the trap first; pick the lock with one still armed and it goes off as the lid comes up.
            any = true;
            var why = Sim.Locks.Lockwork.Problem(_sim, _avatar, item);
            ImGui.BeginDisabled(why != null);
            if (ImGui.MenuItem(box.SearchedByMe ? "Feel for traps again"u8 : "Feel for traps"u8))
                Issue(new Sim.Locks.ProbeBoxCommand(_avatar, item));
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                Tip(why != null ? Cap(why) + "." : "A/D run the probe along the seam; sweep light. Where it catches, stop and hold F to ease the spring out. The far end finishes.");
            ImGui.BeginDisabled(why != null);
            if (ImGui.MenuItem($"Pick the lock ({box.Pins} pins)"))
                Issue(new Sim.Locks.PickLockCommand(_avatar, item));
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                Tip(why != null ? Cap(why) + "." : box.SearchedByMe
                    ? "A/D lift the pin; F sets it where it gives most. A slip drops a pin and wears the picks."
                    : "You haven't felt it over for traps. A trap still armed goes off when the lid comes up.");
        }
        if ((held || packed) && def.Tool != null)
        {
            any = true;
            ImGui.BeginDisabled(!Toolbelt.HasRoom(_sim, _avatar));
            if (ImGui.MenuItem("Put on belt"u8))
                Issue(new BeltCommand(_avatar, item, On: true));
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                Tip(Toolbelt.HasRoom(_sim, _avatar) ? "Tools on your belt work for you and leave your hands free." : $"Your belt is full ({Toolbelt.Slots} tools).");
        }
        if (held && !pack.IsNull && _sim.ContainerRoom(pack, def, w.Get<Thing>(item).Material) > 0)
        {
            any = true;
            if (ImGui.MenuItem("Put in pack"u8))
                Issue(new StowCommand(_avatar));
        }
        if (held)
        {
            any = true;
            if (ImGui.MenuItem("Put down"u8))
                Issue(new AvatarDropCommand(_avatar, w.Get<Position>(_avatar).Cell));
        }
        if (packed)
        {
            any = true;
            if (ImGui.MenuItem("Drop"u8))
                Issue(new DropFromPackCommand(_avatar, item));
        }
        if (!any)
            TextC(Dim, "Nothing to do with it from here.");
    }

    /// <summary>Who you could bandage: yourself, and anyone of the village beside you, wounded.</summary>
    private List<Entity> BandagePatients()
    {
        var w = _sim.World;
        var list = new List<Entity> { _avatar };
        var here = w.Get<Position>(_avatar).Cell;
        foreach (var e in View.Creatures())
            if (e != _avatar && w.Get<Creature>(e) is { Faction: Faction.Village, Dead: false }
                && Sim.Ai.Pathfinder.Touches(here, w.Get<Position>(e).Cell) && Bandaging.WoundsToBind(_sim, e) > 0)
                list.Add(e);
        return list;
    }

    /// <summary>How hungry you are and how much of this a meal takes, for the Eat options.</summary>
    private string EatTip(ThingDef def)
    {
        var food = _sim.World.Get<Needs>(_avatar).Get(_sim.Defs.Need("Food"));
        return $"Food {food * 100:F0}%. {def.Nutrition * 100:F0}% a meal per unit; you eat until you're full."
            + (food >= 0.95f ? "\nYou're not hungry." : "");
    }

    /// <summary>An "Eat" button for a food stack (in hand, packed or in reach), or nothing if it isn't one you can eat.</summary>
    private void EatButton(Entity item)
    {
        var def = _sim.World.Get<Thing>(item).Def;
        if (!def.IsFood || !AvatarSystem.CanEat(_sim, _avatar, item))
            return;
        ImGui.SameLine(0, 4);
        if (ImGui.SmallButton("Eat"u8))
            Issue(new EatCommand(_avatar, item));
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
            Tip(EatTip(def));
    }

    /// <summary>How full a pack is, as a bar in kg.</summary>
    private void LoadBar(Entity pack, float width)
    {
        var capacity = _sim.World.Get<Thing>(pack).Def.Container!.Capacity;
        var load = _sim.LoadOf(pack);
        var color = load >= capacity - 0.05f ? Warn : Good;
        ImGui.PushStyleColor(ImGuiCol.PlotHistogram, in color);
        var size = (float2?)new float2(width, 0);
        ImGui.ProgressBar(Math.Clamp(load / capacity, 0, 1), in size, $"{load:0.#}/{capacity:0.#} kg");
        ImGui.PopStyleColor(1);
    }

    /// <summary>"12 oak wood (12 kg)".</summary>
    private string ItemLine(Entity item)
    {
        var w = _sim.World;
        var t = w.Get<Thing>(item);
        var count = w.TryGet<Stack>(item, out var s) ? s.Count : 1;
        return $"{(count > 1 ? $"{count} " : "")}{t.Label} ({_sim.MassOf(item):0.#} kg)";
    }

    /// <summary>What a piece of gear does, compared with what the avatar wears in its slot.</summary>
    private string ItemDetails(Entity item)
    {
        var w = _sim.World;
        var def = w.Get<Thing>(item).Def;
        var lines = new List<string>();
        if (View.Strongbox(item) is { } box)
        {
            if (box.From.Length > 0)
                lines.Add($"Taken from {box.From}.");
            lines.Add(box.Locked ? $"Locked: {box.Pins} pins." : $"Open. {(box.Silver is > 0 and var n ? $"{n} silver inside." : "Empty.")}");
            if (box.SpentTrap != null)
                lines.Add($"Its trap, {box.SpentTrap}, is spent.");
            else if (box.Locked)
                lines.Add(box.SearchedByMe ? "You've felt it over." : "Nobody you know of has felt it over for traps.");
        }
        if (def.Apparel is { } a && def.Container == null)
        {
            lines.Add($"{a.Slot} armor, covers {string.Join(", ", a.Covers)}");
            lines.Add($"Sharp {a.Sharp * 100:F0}%, blunt {a.Blunt * 100:F0}%, heat {a.Heat * 100:F0}%");
            if (w.TryGet<Apparel>(_avatar, out var worn) && w.IsAlive(worn[a.Slot]) && worn[a.Slot] != item)
            {
                var o = w.Get<Thing>(worn[a.Slot]).Def.Apparel!;
                lines.Add($"vs. your {w.Get<Thing>(worn[a.Slot]).Label}: sharp {Diff(a.Sharp - o.Sharp)}, blunt {Diff(a.Blunt - o.Blunt)}, heat {Diff(a.Heat - o.Heat)}");
            }
        }
        if (def.Container is { } c)
            lines.Add($"A pack: carries {c.Capacity:0.#} kg" + (_sim.IsPack(item) ? $" (holds {_sim.LoadOf(item):0.#} kg now)" : ""));
        if (def.Weapon is { } weapon)
        {
            lines.Add($"Weapon: {weapon.DamageMin:0}–{weapon.DamageMax:0} {weapon.DamageType.ToString().ToLowerInvariant()}, accuracy {weapon.Accuracy * 100:F0}%"
                + (weapon.Ranged ? $", range {weapon.Range}" : ""));
            if (w.TryGet<Equipment>(_avatar, out var eq) && w.IsAlive(eq.Weapon) && eq.Weapon != item)
                lines.Add($"vs. your {w.Get<Thing>(eq.Weapon).Label}");
        }
        if (View.Condition(item) is { } condition)
            lines.Add($"Condition: {condition}");
        if (def.IsFood)
            lines.Add($"Food: {def.Nutrition * 100:F0}% a meal per unit");
        if (def.Bulky)
            lines.Add("Bulky: carried in the hands only");
        return string.Join("\n", lines);
    }

    private static string Diff(float d) => d switch { > 0.005f => $"+{d * 100:F0}%", < -0.005f => $"{d * 100:F0}%", _ => "same" };

    /// <summary>The inventory window: worn gear and hands on the left, the pack on the right.</summary>
    private void DrawInventoryWindow()
    {
        if (!_inventoryOpen || !HasAvatar)
            return;
        var w = _sim.World;
        // Beside the avatar (the camera keeps them centred), not over them.
        var beside = ImGui.GetIO().DisplaySize * 0.5f + new float2(60, 0);
        float2? left = new float2(0f, 0.5f);
        ImGui.SetNextWindowPos(in beside, ImGuiCond.Appearing, in left);
        var open = true;
        ImGui.Begin("Inventory###inventory"u8, ref open, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings);

        if (ImGui.SmallButton("Recipe book"u8))
            _showRecipeBook = !_showRecipeBook;
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
            Tip("What you know how to make, and things you can make by hand right here.");
        ImGui.BeginGroup();
        Theme.Header(Host.ImGui, "Wearing");
        foreach (var slot in new[] { GearSlot.Head, GearSlot.Body, GearSlot.Tunic, GearSlot.Legs, GearSlot.Feet, GearSlot.Cloak, GearSlot.Back, GearSlot.Neck, GearSlot.Weapon })
        {
            var item = slot == GearSlot.Weapon
                ? (w.TryGet<Equipment>(_avatar, out var eq) ? eq.Weapon : Entity.Null)
                : (w.TryGet<Apparel>(_avatar, out var worn) ? worn[Outfit.ApparelOf(slot)!.Value] : Entity.Null);
            if (!item.IsNull && !w.IsAlive(item))
                item = Entity.Null;
            SlotBox($"gear{slot}", item, "", $"Nothing on your {slot.ToString().ToLowerInvariant()}");
            ImGui.SameLine(0, 6);
            ImGui.BeginGroup();
            ImGui.TextDisabled(slot.ToString());
            if (item.IsNull)
                TextC(Dim, "—");
            else
            {
                ImGui.Text(_sim.LabelOf(item));
                ImGui.SameLine(0, 6);
                if (ImGui.SmallButton($"{(slot == GearSlot.Weapon ? "Put away" : "Take off")}##{slot}"))
                    Issue(new UnequipCommand(_avatar, slot));
                if (slot == GearSlot.Weapon && Sim.Work.SharpenSession.Problem(_sim, _avatar, item) == null)
                {
                    ImGui.SameLine(0, 6);
                    if (ImGui.SmallButton("Sharpen##weapon"u8))
                        Issue(new Sim.Work.SharpenCommand(_avatar, item));
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                        Tip($"It's {View.Condition(item)}. Hold F to grind, A/D to keep the angle.");
                }
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                    Tip(slot == GearSlot.Back
                        ? "Take the pack off: it keeps what's in it, and goes into your hands if they're free."
                        : "Into your pack if it fits, else your hands, else onto the ground.");
            }
            ImGui.EndGroup();
        }
        ImGui.Spacing();
        Theme.Header(Host.ImGui, "In hand");
        var held = w.Get<Carrying>(_avatar).Item;
        SlotBox("hand", held, "", "Empty hands", menu: true);
        ImGui.SameLine(0, 6);
        ImGui.BeginGroup();
        if (held.IsNull || !w.IsAlive(held))
            TextC(Dim, "Nothing");
        else
        {
            ImGui.Text(ItemLine(held));
            var def = w.Get<Thing>(held).Def;
            if (def.Apparel != null || def.Weapon != null)
            {
                if (ImGui.SmallButton(def.Weapon != null ? "Wield##held"u8 : "Wear##held"u8))
                    Issue(new EquipCommand(_avatar, held));
                ImGui.SameLine(0, 4);
            }
            var pack = _sim.PackOf(_avatar);
            if (!pack.IsNull && _sim.ContainerRoom(pack, def, w.Get<Thing>(held).Material) > 0)
            {
                if (ImGui.SmallButton("Put in pack"u8))
                    Issue(new StowCommand(_avatar));
                ImGui.SameLine(0, 4);
            }
            if (ImGui.SmallButton("Put down"u8))
                Issue(new AvatarDropCommand(_avatar, w.Get<Position>(_avatar).Cell));
            EatButton(held);
        }
        ImGui.EndGroup();
        DrawBelt();
        ImGui.EndGroup();

        ImGui.SameLine(0, 24);
        ImGui.BeginGroup();
        DrawPackList(_sim.PackOf(_avatar), _openStorage);
        ImGui.EndGroup();

        ImGui.End();
        if (!open)
            _inventoryOpen = false;
    }

    /// <summary>
    /// The tool belt (<see cref="Toolbelt"/>): what you chop, mine, dig rock and fish with. Each slot
    /// says how much faster than bare hands it makes the work, and can be taken off.
    /// </summary>
    private void DrawBelt()
    {
        var w = _sim.World;
        ImGui.Spacing();
        Theme.Header(Host.ImGui, "Tool belt");
        var belt = Toolbelt.Of(_sim, _avatar);
        for (var k = 0; k < Toolbelt.Slots; k++)
        {
            if (k > 0)
                ImGui.SameLine(0, 4);
            SlotBox($"belt{k}", k < belt.Count ? belt[k] : Entity.Null, "",
                "An empty loop: pick up a tool (or use Put on belt) to hang it here. Without an axe or a pick, chopping and mining go slowly.");
        }
        foreach (var tool in belt)
        {
            var t = w.Get<Thing>(tool).Def.Tool!;
            ImGui.PushID(tool.Index);
            ImGui.Text($"{_sim.LabelOf(tool)}");
            ImGui.SameLine(0, 6);
            TextC(Dim, $"{_sim.Defs.TierLabel(t.Tier)} {t.Kind.ToLowerInvariant()}, ×{t.Speed:0.##}");
            ImGui.SameLine(0, 6);
            if (ImGui.SmallButton("Take off"u8))
                Issue(new BeltCommand(_avatar, tool, On: false));
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip("Into your pack if it fits, else your hands, else onto the ground.");
            ImGui.PopID();
        }
        if (belt.Count == 0)
            TextC(Dim, $"No tools: chopping, mining and fishing go at {Crafting.BareHands * 100:F0}% bare-handed.");
    }

    /// <summary>
    /// The pack's stacks with what can be done with each: hold, wear or wield, drop, or (with a
    /// container open beside it) put in there.
    /// </summary>
    private void DrawPackList(Entity pack, Entity container)
    {
        var w = _sim.World;
        if (pack.IsNull)
        {
            Theme.Header(Host.ImGui, "No pack");
            Wrapped("Without a pack you carry one thing at a time. Put on a satchel to carry more.");
            return;
        }
        Theme.Header(Host.ImGui, $"{char.ToUpper(w.Get<Thing>(pack).Label[0])}{w.Get<Thing>(pack).Label[1..]}");
        LoadBar(pack, 220);
        var items = _sim.StoredIn(pack);
        if (items.Count == 0)
            TextC(Dim, "Empty. F picks things up into it.");
        var into = !container.IsNull && w.IsAlive(container) ? container : Entity.Null;
        for (var k = 0; k < items.Count; k++)
        {
            var item = items[k];
            var t = w.Get<Thing>(item);
            ImGui.PushID(item.Index);
            SlotBox("pack", item, k < HotbarSlots ? $"{k + 1}" : "", "", menu: true);
            ImGui.SameLine(0, 6);
            ImGui.BeginGroup();
            ImGui.Text(ItemLine(item));
            if (ImGui.SmallButton("Hold"u8))
                Issue(new HoldCommand(_avatar, item));
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip(k < HotbarSlots ? $"Take it in hand [{k + 1}]" : "Take it in hand");
            EatButton(item);
            if (t.Def.Apparel != null || t.Def.Weapon != null)
            {
                ImGui.SameLine(0, 4);
                if (ImGui.SmallButton(t.Def.Weapon != null ? "Wield"u8 : "Wear"u8))
                    Issue(new EquipCommand(_avatar, item));
            }
            if (!into.IsNull && _sim.ContainerRoom(into, t.Def, t.Material) > 0)
            {
                ImGui.SameLine(0, 4);
                if (ImGui.SmallButton($"Put in {w.Get<Thing>(into).Label}"))
                    Issue(new StoreFromPackCommand(_avatar, into, item));
            }
            ImGui.SameLine(0, 4);
            if (ImGui.SmallButton("Drop"u8))
                Issue(new DropFromPackCommand(_avatar, item));
            ImGui.EndGroup();
            ImGui.PopID();
        }
    }
}

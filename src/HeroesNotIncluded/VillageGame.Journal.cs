using Brutal.ImGuiApi;
using Narsil.Platform;
using Narsil.Ui;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

// The anchored HUD (Narsil.Ui.Hud): everything on screen hangs from an edge and stacks, so nothing
// overlaps at any resolution or UI scale. The journal panels (orders, what you've heard, quests,
// the gift, the message log, co-op players) share the right-hand column under the clock; each one
// toggles with a key, shown on the strip under the clock.
public sealed partial class VillageGame
{
    private readonly Hud _hud = new();
    private readonly ToastStack _toastStack = new();

    // Journal panels, open or not. A round starts with only the shop (and an empath's gift) open; the
    // others mark their tab when there's something new rather than filling the screen from the start.
    private bool _showShop = true, _showHeard, _showQuests, _showGift = true, _showLog;

    /// <summary>Quests known when the quest board was last open: more than this marks its tab.</summary>
    private int _questsSeen;
    private bool _showControls;

    /// <summary>In Adventure, the build toolbar is a drawer (B): closed, its letter keys are free for the journal.</summary>
    private bool _showBuild;

    /// <summary>T was pressed: put the cursor in the speech box.</summary>
    private bool _focusSpeech;

    /// <summary>Width of a journal panel, before UI scale.</summary>
    private const float JournalWidth = 400f;

    /// <summary>The most of the screen's width the journal takes, so the inspector still fits beside it on a small screen.</summary>
    private const float JournalShare = 0.36f;

    private bool InRound => _sim.Round.Live;

    /// <summary>The build drawer is open, or there's no avatar (god mode always has the toolbar).</summary>
    private bool ToolbarOpen => (!HasAvatar && !Spectating) || (HasAvatar && _showBuild);

    private readonly record struct JournalTab(string Action, string Label, Func<VillageGame, bool> Shown, Action<VillageGame> Toggle);

    private static readonly JournalTab[] JournalTabs =
    [
        new(Act.Shop, "Shop", g => g._showShop, g => g._showShop = !g._showShop),
        new(Act.Heard, "Heard", g => g._showHeard, g => g._showHeard = !g._showHeard),
        new(Act.Quests, "Quests", g => g._showQuests, g => g._showQuests = !g._showQuests),
        new(Act.Gift, "Gift", g => g._showGift, g => g._showGift = !g._showGift),
        new(Act.Log, "Log", g => g._showLog, g => g._showLog = !g._showLog),
    ];

    /// <summary>Which journal tabs apply now: the gift only to an empath, quests once any are known.</summary>
    private bool TabApplies(JournalTab tab) => tab.Label switch
    {
        "Heard" => InRound && (HasAvatar || Ghosting),
        "Shop" => InRound && HasAvatar,
        "Quests" => InRound && HasAvatar && View.Quests().Any(),
        "Gift" => InRound && HasAvatar && _sim.World.Has<Mana>(_avatar),
        _ => true,
    };

    /// <summary>Journal and HUD keys in Adventure: J/T/K/Y/L panels, B the build drawer, H the controls.</summary>
    private void HandleJournalKeys(Input input)
    {
        if ((!HasAvatar && !Ghosting) || Working)
            return;
        var map = Controls.Map;
        if (map.Pressed(Act.Build) && HasAvatar)
            _showBuild = !_showBuild;
        if (map.Pressed(Act.ControlsCard) && !_showBuild)
            _showControls = !_showControls;
        if (map.Pressed(Act.Menu))
            _padMenu = !_padMenu;
        if (_showBuild)
            return; // the drawer's letter keys are the build tools'
        foreach (var tab in JournalTabs)
            if (map.Pressed(tab.Action))
                OpenTab(tab);
    }

    private void OpenTab(JournalTab tab)
    {
        if (!TabApplies(tab))
            return;
        if (tab.Action == Act.Heard)
        {
            // Heard opens the speech box ready to type (and closes it from the keyboard only when already open and empty).
            _showHeard = true;
            _focusSpeech = true;
            return;
        }
        tab.Toggle(this);
    }

    /// <summary>A closed panel with something in it the player hasn't seen: new speech, or a new quest.</summary>
    private bool Unread(JournalTab tab) => tab.Label switch
    {
        "Heard" => View.Heard().Count() > _speechSeen,
        "Quests" => View.Quests().Count() > _questsSeen,
        _ => false,
    };

    /// <summary>The strip of journal toggles under the clock: a key cap and a name each, lit when open.</summary>
    private void DrawJournalStrip()
    {
        if (!HasAvatar && !Ghosting)
            return;
        var first = true;
        foreach (var tab in JournalTabs)
        {
            if (!TabApplies(tab))
                continue;
            if (!first)
                ImGui.SameLine(0, Gui.S(4));
            first = false;
            var open = tab.Shown(this);
            if (open)
            {
                var c = Theme.AccentSoft;
                ImGui.PushStyleColor(ImGuiCol.Button, in c);
            }
            if (ImGui.Button($"{tab.Label}##journal-{tab.Action}", in _autoSize))
                tab.Toggle(this);
            if (open)
                ImGui.PopStyleColor(1);
            var unread = !open && Unread(tab);
            if (unread)
            {
                var r = Gui.S(3.5f);
                var corner = ImGui.GetItemRectMax();
                var at = new float2(corner.X - r - Gui.S(2), ImGui.GetItemRectMin().Y + r + Gui.S(2));
                ImGui.GetWindowDrawList().AddCircleFilled(in at, r, Theme.U8(Theme.Parchment), 0);
            }
            Gui.ItemTip($"{(open ? "Close" : "Open")} {tab.Label.ToLowerInvariant()}{(unread ? " (something new)" : "")}{KeyTip(tab.Action)}");
        }
        if (!first)
            ImGui.SameLine(0, Gui.S(4));
        if (HasAvatar)
        {
            if (ImGui.Button("Build##drawer"u8, in _autoSize))
                _showBuild = !_showBuild;
            Gui.ItemTip((_showBuild ? "Close the build tools" : "Build, zones and orders") + KeyTip(Act.Build));
            ImGui.SameLine(0, Gui.S(4));
        }
        if (ImGui.Button("?##controls"u8, in _autoSize))
            _showControls = !_showControls;
        Gui.ItemTip("Controls" + KeyTip(Act.ControlsCard));
    }

    /// <summary>A journal panel in the right-hand column, under the clock. Pair with <see cref="Panel.End"/>.</summary>
    private bool BeginJournal(string title, ref bool open, float height = 0f)
    {
        _hud.Next(Anchor.TopRight, ScreenFit.Width(JournalWidth, JournalShare), height > 0 ? Gui.S(height) : 0f);
        var visible = Panel.Begin(title, ref open, Hud.Flags);
        _hud.Took();
        return visible;
    }

    /// <summary>
    /// The controls card's lines, by what you're doing: each an action (its caps as bound, for the
    /// device in use) or fixed text for what isn't an action (the mouse's clicks, screenshots).
    /// </summary>
    private static readonly (string Context, (string Keys, string Does)[] Lines)[] ControlsCard =
    [
        ("Getting about", [("@walk", "walk"), ("@" + Act.Use, "use, talk to, work at what's in front of you"), ("@" + Act.Inventory, "inventory"),
            ("@hotbar", "take a stack into your hands"), ("@" + Act.Drop, "put down what you hold"), ("@" + Act.Dig, "dig (the labourer)"),
            ("@" + Act.Sleep, "sleep where you stand"), ("@" + Act.Wheel, "held: the wheel (panels, stacks, put down, sleep)")]),
        ("Fighting", [("@" + Act.Draw, "draw or sheathe your weapon"), ("@aim", "weapon drawn: aim"), ("@" + Act.Strike, "weapon drawn: strike"),
            ("@" + Act.Heavy, "swing heavy; a bow: hold to draw, strike looses; a crossbow: hold to crank, it stays loaded"),
            ("@" + Act.Guard, "held: guard; raised as a blow lands, a parry"), ("@" + Act.Dash, "dash clear")]),
        ("The village", [("!Left-click", "walk there or act"), ("!Shift-click", "look at someone or something"), ("!Right-click", "an item's menu"), ("@" + Act.Back, "stop looking, close")]),
        ("Hands-on work", [("@" + Act.Use, "strike, pour, set, press"), ("@steer", "steer, tilt, lift"), ("@" + Act.WorkAct, "the other thing: taste, latch, draw out"),
            ("@runes", "weave a rune"), ("@" + Act.Back, "put the work down")]),
        ("Panels", [("@" + Act.Shop, "shop and favours"), ("@" + Act.Heard, "talk (what you've heard)"), ("@" + Act.Quests, "quests"), ("@" + Act.Gift, "the gift"),
            ("@" + Act.Log, "message log"), ("@" + Act.Build, "build tools"), ("@" + Act.ControlsCard, "this card"), ("@" + Act.Menu, "the game menu")]),
        ("Camera and time", [("@turn", "turn the view"), ("@zoom", "zoom (or the wheel)"), ("@levels", "level down / up"),
            ("@" + Act.LevelFollow, "all levels"), ("@" + Act.Roofs, "roofs"), ("F12", "screenshot"), ("Shift+F12", "screenshot without the UI")]),
    ];

    /// <summary>
    /// Key caps for a card line: "@id" is an action as bound for the device in use; a pad
    /// without a button for it reaches it through the wheel (null: nothing to show).
    /// </summary>
    private static string? CardKeys(string keys)
    {
        var pad = Controls.UsingPad;
        if (keys.StartsWith('!'))
            return pad ? null : keys[1..]; // the mouse's alone
        if (!keys.StartsWith('@'))
            return keys;
        var id = keys[1..];
        switch (id)
        {
            case "walk":
                return Controls.Walk();
            case "steer":
                return Controls.Pair(Act.WalkLeft, Act.WalkRight);
            case "aim":
                return pad ? "RS" : "Mouse";
            case "turn":
                return Controls.Pair(Act.TurnLeft, Act.TurnRight);
            case "zoom":
                return Controls.Pair(Act.ZoomOut, Act.ZoomIn);
            case "levels":
                return Controls.Pair(Act.LevelDown, Act.LevelUp);
            case "hotbar":
                return pad ? null : string.Join("/", Act.Hotbar.Select(Controls.Glyph).Where(g => g.Length > 0));
            case "runes":
                return RuneKeys(0, Act.Runes.Length - 1);
        }
        if (!Controls.Map.Has(id))
            return null;
        var mine = Controls.Map.Bindings(id).Where(b => b.IsPad == pad).Select(b => b.Glyph).ToList();
        return mine.Count > 0 ? string.Join("/", mine) : null; // on a pad, the wheel's line covers the rest
    }

    /// <summary>One key cap for an action, as the card would show it (the wheel's on a pad, for what has no button).</summary>
    private static string CapFor(string id) =>
        CardKeys("@" + id) is { Length: > 0 } k ? k.Split('/')[0]
        : Controls.UsingPad && WheelActions.Contains(id) ? Controls.Glyph(Act.Wheel) : Controls.Glyph(id);

    /// <summary>"  [F]" after a tooltip: what an action's bound to, for the device in use.</summary>
    private static string KeyTip(string id) => CapFor(id) is { Length: > 0 } k ? $"  [{k}]" : "";

    /// <summary>Every binding by what you're doing (H), in place of long hint lines scattered round the HUD.</summary>
    private void DrawControls()
    {
        if (!_showControls)
            return;
        // Over the HUD, not fitted between its columns: it's a card you glance at and put away.
        var center = ImGui.GetIO().DisplaySize * 0.5f;
        float2? middle = new float2(0.5f, 0.5f);
        ImGui.SetNextWindowPos(in center, ImGuiCond.Always, in middle);
        ScreenFit.Next();
        if (Panel.Begin("Controls###controls", ref _showControls, Hud.Flags | ImGuiWindowFlags.AlwaysAutoResize))
        {
            // Two columns, so it fits a Steam Deck's 800 lines. A table, so each header's rule spans its own.
            var half = (ControlsCard.Length + 1) / 2;
            var tight = Gui.S(8, 3);
            ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, in tight);
            float2? auto = null;
            if (!ImGui.BeginTable("##card"u8, 2, ImGuiTableFlags.SizingFixedFit, in auto, 0))
            {
                ImGui.PopStyleVar(1);
                Panel.End();
                return;
            }
            var colWidth = MathF.Min(Gui.S(430), ImGui.GetIO().DisplaySize.X * 0.5f - Gui.S(40));
            ImGui.TableSetupColumn("##a"u8, ImGuiTableColumnFlags.WidthFixed, colWidth, default);
            ImGui.TableSetupColumn("##b"u8, ImGuiTableColumnFlags.WidthFixed, colWidth, default);
            ImGui.TableNextRow(ImGuiTableRowFlags.None, 0);
            for (var col = 0; col < 2; col++)
            {
                ImGui.TableNextColumn();
                var x0 = ImGui.GetCursorPosX();
                foreach (var (context, lines) in ControlsCard.Skip(col * half).Take(half))
                {
                    Gui.Header(context);
                    foreach (var (line, does) in lines)
                    {
                        if (CardKeys(line) is not { Length: > 0 } keys)
                            continue;
                        ImGui.BeginGroup();
                        foreach (var key in keys.Split('/'))
                        {
                            Gui.KeyCap(key.Trim());
                            ImGui.SameLine(0, Gui.S(3));
                        }
                        ImGui.EndGroup();
                        ImGui.SameLine(0, 0);
                        ImGui.SetCursorPosX(x0 + Gui.S(120));
                        ImGui.AlignTextToFramePadding();
                        ImGui.Text(does);
                    }
                    ImGui.Spacing();
                }
            }
            ImGui.EndTable();
            ImGui.PopStyleVar(1);
        }
        Panel.End();
    }
}

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
    private bool ToolbarOpen => !HasAvatar || _showBuild;

    private readonly record struct JournalTab(string Key, string Label, Func<VillageGame, bool> Shown, Action<VillageGame> Toggle);

    private static readonly JournalTab[] JournalTabs =
    [
        new("J", "Shop", g => g._showShop, g => g._showShop = !g._showShop),
        new("T", "Heard", g => g._showHeard, g => g._showHeard = !g._showHeard),
        new("K", "Quests", g => g._showQuests, g => g._showQuests = !g._showQuests),
        new("Y", "Gift", g => g._showGift, g => g._showGift = !g._showGift),
        new("L", "Log", g => g._showLog, g => g._showLog = !g._showLog),
    ];

    /// <summary>Which journal tabs apply now: the gift only to an empath, quests once any are known.</summary>
    private bool TabApplies(JournalTab tab) => tab.Label switch
    {
        "Shop" or "Heard" => InRound,
        "Quests" => InRound && View.Quests().Any(),
        "Gift" => InRound && _sim.World.Has<Mana>(_avatar),
        _ => true,
    };

    /// <summary>Journal and HUD keys in Adventure: J/T/K/Y/L panels, B the build drawer, H the controls.</summary>
    private void HandleJournalKeys(Input input)
    {
        if (!HasAvatar || Working)
            return;
        if (input.WasKeyPressed(GlfwKey.B))
            _showBuild = !_showBuild;
        if (input.WasKeyPressed(GlfwKey.H) && !_showBuild)
            _showControls = !_showControls;
        if (_showBuild)
            return; // the drawer's letter keys are the build tools'
        foreach (var tab in JournalTabs)
        {
            if (!input.WasKeyPressed(Enum.Parse<GlfwKey>(tab.Key)) || !TabApplies(tab))
                continue;
            if (tab.Key == "T")
            {
                // T opens the speech box ready to type (and closes it from the keyboard only when already open and empty).
                _showHeard = true;
                _focusSpeech = true;
                continue;
            }
            tab.Toggle(this);
        }
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
        if (!HasAvatar)
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
            if (ImGui.Button($"{tab.Label}##journal-{tab.Key}", in _autoSize))
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
            Gui.ItemTip($"{(open ? "Close" : "Open")} {tab.Label.ToLowerInvariant()}{(unread ? " (something new)" : "")}  [{tab.Key}]");
        }
        if (!first)
            ImGui.SameLine(0, Gui.S(4));
        if (ImGui.Button(_showBuild ? "Build##drawer"u8 : "Build##drawer"u8, in _autoSize))
            _showBuild = !_showBuild;
        Gui.ItemTip(_showBuild ? "Close the build tools  [B]" : "Build, zones and orders  [B]");
        ImGui.SameLine(0, Gui.S(4));
        if (ImGui.Button("?##controls"u8, in _autoSize))
            _showControls = !_showControls;
        Gui.ItemTip("Controls  [H]");
    }

    /// <summary>A journal panel in the right-hand column, under the clock. Pair with <see cref="Panel.End"/>.</summary>
    private bool BeginJournal(string title, ref bool open, float height = 0f)
    {
        _hud.Next(Anchor.TopRight, ScreenFit.Width(JournalWidth, JournalShare), height > 0 ? Gui.S(height) : 0f);
        var visible = Panel.Begin(title, ref open, Hud.Flags);
        _hud.Took();
        return visible;
    }

    private static readonly (string Context, (string Keys, string Does)[] Lines)[] Controls =
    [
        ("Getting about", [("W/A/S/D", "walk"), ("F", "use, talk to, work at what's in front of you"), ("Tab", "inventory"), ("1/2/3/4", "take a stack into your hands"),
            ("G", "put down what you hold"), ("C", "dig (the labourer)"), ("N", "sleep where you stand")]),
        ("Fighting", [("R", "draw or sheathe your weapon"), ("Mouse", "weapon drawn: aim"), ("Left / right click", "weapon drawn: strike / swing heavy"),
            ("Right click (held), left click", "a bow: draw and aim, then loose"),
            ("Q or V (held)", "guard, facing the blow; raised as it lands, a parry"), ("Space", "dash clear")]),
        ("The village", [("Left-click", "walk there or act"), ("Shift-click", "look at someone or something"), ("Right-click", "an item's menu"), ("Esc", "stop looking")]),
        ("Hands-on work", [("F", "strike, pour, set, press"), ("A/D", "steer, tilt, lift"), ("Space", "the other thing: taste, latch, draw out"), ("Esc", "put the work down")]),
        ("Panels", [("J", "shop and favours"), ("T", "talk (what you've heard)"), ("K", "quests"), ("Y", "the gift"), ("L", "message log"), ("B", "build tools"), ("H", "this card")]),
        ("Camera and time", [("Z/X", "turn (Q/E with no character)"), ("Wheel or - / =", "zoom"), ("[ / ]", "level down / up"), ("Home", "all levels"), ("\\", "roofs"), ("N", "sleep: time runs on once everyone does"),
            ("F12", "screenshot"), ("Shift+F12", "screenshot without the UI")]),
    ];

    /// <summary>Every binding by what you're doing (H), in place of long hint lines scattered round the HUD.</summary>
    private void DrawControls()
    {
        if (!_showControls)
            return;
        _hud.Next(Anchor.Center);
        if (Panel.Begin("Controls###controls", ref _showControls, Hud.Flags | ImGuiWindowFlags.AlwaysAutoResize))
            foreach (var (context, lines) in Controls)
            {
                Gui.Header(context);
                foreach (var (keys, does) in lines)
                {
                    ImGui.BeginGroup();
                    foreach (var key in keys.Split('/'))
                    {
                        Gui.KeyCap(key.Trim());
                        ImGui.SameLine(0, Gui.S(3));
                    }
                    ImGui.EndGroup();
                    ImGui.SameLine(Gui.S(150), 0);
                    ImGui.AlignTextToFramePadding();
                    ImGui.Text(does);
                }
                ImGui.Spacing();
            }
        Panel.End();
    }
}

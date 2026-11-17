using Brutal.ImGuiApi;
using Narsil.Platform;
using Narsil.Ui;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

/// <summary>
/// The gamepad's own UI: menus driven by ImGui's navigation while one's open (the d-pad moves, A
/// picks, B closes), the action wheel for what has no button of its own (held RB, the right stick
/// picks, let go to do it), and the game menu: the top bar's things on Start, or on Esc (B) when
/// there's nothing else for it to close.
/// </summary>
public sealed partial class VillageGame
{
    /// <summary>Navigable windows drawn this frame and last, by ImGui id ("###inventory"), each with what closes it (null: it can't be).</summary>
    private List<(string Id, Action? Close)> _padWindows = [], _padWindowsShown = [];

    /// <summary>Windows in the order they opened, so B closes the newest and the d-pad lands in it.</summary>
    private readonly List<string> _padOrder = [];

    private string? _padFocused;

    /// <summary>The game menu (Start, or Esc with nothing else to close): the top bar's Menu, as buttons.</summary>
    private bool _gameMenu;

    /// <summary>
    /// Back (Esc, B) was taken this frame by something it closes, stops or puts down, so it doesn't
    /// also open the game menu. Reset as the frame's input starts; checked once every window has
    /// had its chance (<see cref="EndPadUi"/>).
    /// </summary>
    private bool _backUsed;

    /// <summary>One of the menu's own windows was up as the frame began: Back is for it, even if it closed itself on Esc.</summary>
    private bool _menuWindowUp;

    /// <summary>Back was pressed this frame, for something it closes or stops: marks it taken.</summary>
    private bool BackPressed()
    {
        if (!Controls.Map.Pressed(Act.Back))
            return false;
        _backUsed = true;
        return true;
    }

    /// <summary>A window the pad can drive is up: call after its Begin.</summary>
    private void PadWindow(string id, Action? close) => _padWindows.Add((id, close));

    /// <summary>
    /// Before the frame's input: the pad drives menus while any is open (not at hands-on work,
    /// where it's the work's), B closes the newest, and a newly opened one takes the focus.
    /// </summary>
    private void UpdatePadUi()
    {
        // A popup or a text box takes Esc itself (closing, or leaving the field).
        _backUsed = ImGui.IsPopupOpen(""u8, ImGuiPopupFlags.AnyPopup) || ImGui.GetIO().WantTextInput;
        _menuWindowUp = _settings.IsOpen || _saveWindow != SaveWindow.None || _showCredits || _showRecipeBook;
        var shown = _padWindowsShown;
        _padOrder.RemoveAll(id => !shown.Exists(w => w.Id == id));
        foreach (var (id, _) in shown)
            if (!_padOrder.Contains(id))
                _padOrder.Add(id);
        // A popup (the top bar's menus, opened from the trackpad) has the pad too: ImGui focuses it.
        var popup = ImGui.IsPopupOpen(""u8, ImGuiPopupFlags.AnyPopup);
        var nav = (_padOrder.Count > 0 || popup) && !Working && !_settings.Capturing;
        Host.ImGui.GamepadNav = nav;
        if (!nav || _padOrder.Count == 0)
        {
            _padFocused = null;
            return;
        }
        var top = _padOrder[^1];
        if (top != _padFocused)
        {
            ImGui.SetWindowFocus(top);
            _padFocused = top;
        }
        // B (the back action's pad buttons) closes the newest, when it can be closed.
        var back = false;
        foreach (var b in Controls.Map.Bindings(Act.Back))
            back |= b.IsPad && Host.Gamepad.WasPressedRaw((PadButton)b.Code);
        if (back && !ImGui.IsPopupOpen(""u8, ImGuiPopupFlags.AnyPopup) && shown.Find(w => w.Id == top).Close is { } close)
        {
            close();
            _backUsed = true;
        }
    }

    /// <summary>
    /// After the frame's UI: what was drawn is what's open next frame. A Back nothing took backs out
    /// of the menu's own windows (settings, saves, credits), else opens or closes the game menu.
    /// </summary>
    private void EndPadUi()
    {
        if (!_backUsed && !Working && !_settings.Capturing && !ImGui.IsPopupOpen(""u8, ImGuiPopupFlags.AnyPopup) && Controls.Map.Pressed(Act.Back))
        {
            if (_settings.IsOpen)
                _settings.Close();
            else if (_saveWindow != SaveWindow.None)
                _saveWindow = SaveWindow.None;
            else if (_showCredits)
                _showCredits = false;
            else if (_showRecipeBook)
                _showRecipeBook = false;
            else if (!_menuWindowUp) // settings and credits close themselves on Esc
                _gameMenu = !_gameMenu;
        }
        if (_settings.IsOpen)
            PadWindow("Settings", _settings.Close);
        if (_showCredits)
            PadWindow("###credits", () => _showCredits = false);
        (_padWindowsShown, _padWindows) = (_padWindows, _padWindowsShown);
        _padWindows.Clear();
    }

    // ---------------- The action wheel ----------------

    private readonly record struct WheelEntry(string Id, Func<VillageGame, string?> Label, Action<VillageGame> Do);

    /// <summary>What the wheel offers, clockwise from the top: the actions with no pad button by default. A null label leaves one out.</summary>
    private static readonly WheelEntry[] WheelEntries =
    [
        new(Act.Shop, g => g.TabApplies(JournalTabs[0]) ? "Shop" : null, g => g.OpenTab(JournalTabs[0])),
        new(Act.Heard, g => g.TabApplies(JournalTabs[1]) ? "Heard" : null, g => g._showHeard = !g._showHeard),
        new(Act.Quests, g => g.TabApplies(JournalTabs[2]) ? "Quests" : null, g => g.OpenTab(JournalTabs[2])),
        new(Act.Gift, g => g.TabApplies(JournalTabs[3]) ? "Gift" : null, g => g.OpenTab(JournalTabs[3])),
        new(Act.Log, g => "Log", g => g.OpenTab(JournalTabs[4])),
        new(Act.Hotbar1, g => g.HotbarLabel(0), g => g.TakeInHand(0)),
        new(Act.Hotbar2, g => g.HotbarLabel(1), g => g.TakeInHand(1)),
        new(Act.Hotbar3, g => g.HotbarLabel(2), g => g.TakeInHand(2)),
        new(Act.Hotbar4, g => g.HotbarLabel(3), g => g.TakeInHand(3)),
        new(Act.Drop, g => g._sim.World.IsAlive(g._sim.World.Get<Carrying>(g._avatar).Item) ? "Put down" : null, g => g.Drop()),
        new(Act.Sleep, g => "Sleep", g => g.Sleep()),
        new(Act.Build, g => BuildTools ? "Build" : null, g => g._showBuild = BuildTools && !g._showBuild),
        new(Act.Cheer, g => g.CanCheer ? "Cheer!" : null, g => g.Cheer()),
    ];

    /// <summary>Actions the wheel reaches (the controls card says "the wheel" for them on a pad).</summary>
    private static readonly HashSet<string> WheelActions = [.. WheelEntries.Select(e => e.Id)];

    private bool _wheelOpen;
    private int _wheelPick = -1;
    private List<(WheelEntry Entry, string Label)> _wheelShown = [];

    /// <summary>The wheel's up: the right stick picks from it rather than aims, and A and X wait.</summary>
    private bool WheelOpen => _wheelOpen;

    /// <summary>
    /// The right stick points a cursor and the right trigger clicks (<see cref="Narsil.Platform.PadCursor"/>),
    /// so a pad can click anything a mouse can: a Steam Deck playtester had only the touchscreen.
    /// Not where the stick or trigger mean something else: a weapon drawn, a full bucket, the
    /// archery butts, the wheel, camera mode, a beast ridden, or the walk moved onto the right stick.
    /// </summary>
    private bool PadPointerWanted =>
        !WeaponDrawn && !HoldingFullBucket && !WheelOpen && !_cameraMode
        && !(HasAvatar && _sim.World.Has<Sim.Work.ButtsTurn>(_avatar))
        && !(Ghosting && !Sim.Afterlife.Possession.Ridden(_sim, _ghost).IsNull)
        && StickOf(Act.WalkUp) != false;

    /// <summary>The pad's cursor is in use: hovers and clicks mean what they do with a mouse.</summary>
    private bool PadPointing => Host.PadCursor.Active;

    /// <summary>A pack stack for the wheel: its name, or nothing for an empty slot.</summary>
    private string? HotbarLabel(int slot)
    {
        var pack = _sim.PackOf(_avatar);
        if (pack.IsNull)
            return null;
        var items = _sim.StoredIn(pack);
        return slot < items.Count ? $"Hold: {_sim.World.Get<Thing>(items[slot]).Label}" : null;
    }

    /// <summary>Hold the wheel's button to open it, lean the right stick to pick, let go to do it.</summary>
    private void HandleWheel()
    {
        var can = HasAvatar && !Working && !WeaponDrawn && !Host.ImGui.GamepadNav;
        var held = can && Controls.Map.Down(Act.Wheel);
        if (held && !_wheelOpen)
        {
            _wheelShown = [.. WheelEntries.Select(e => (e, e.Label(this))).Where(x => x.Item2 != null).Select(x => (x.e, x.Item2!))];
            _wheelPick = -1;
        }
        if (held && _wheelShown.Count > 0)
        {
            var stick = Host.Gamepad.RightStick;
            if (stick.X * stick.X + stick.Y * stick.Y > 0.25f)
            {
                // Clockwise from the top, as drawn.
                var turn = MathF.Atan2(stick.X, stick.Y);
                if (turn < 0)
                    turn += MathF.Tau;
                _wheelPick = (int)MathF.Round(turn / MathF.Tau * _wheelShown.Count) % _wheelShown.Count;
            }
        }
        if (!held && _wheelOpen && can && _wheelPick >= 0 && _wheelPick < _wheelShown.Count)
            _wheelShown[_wheelPick].Entry.Do(this);
        _wheelOpen = held;
    }

    private void DrawWheel()
    {
        if (!_wheelOpen || _wheelShown.Count == 0)
            return;
        var dl = ImGui.GetForegroundDrawList(ImGui.GetMainViewport());
        var center = ImGui.GetIO().DisplaySize * 0.5f;
        var radius = Gui.S(150);
        var hub = Gui.S(26);
        dl.AddCircleFilled(in center, radius + Gui.S(46), Theme.U8(new float4(0.05f, 0.04f, 0.03f, 0.55f)), 48);
        dl.AddCircleFilled(in center, hub, Theme.U8(Theme.AccentSoft, 0.8f), 24);
        for (var i = 0; i < _wheelShown.Count; i++)
        {
            var angle = MathF.Tau * i / _wheelShown.Count;
            var at = center + new float2(MathF.Sin(angle), -MathF.Cos(angle)) * radius;
            var label = _wheelShown[i].Label;
            var size = ImGui.CalcTextSize(label, false, -1f);
            var pad = Gui.S(8);
            var min = at - size * 0.5f - new float2(pad, pad * 0.6f);
            var max = at + size * 0.5f + new float2(pad, pad * 0.6f);
            var picked = i == _wheelPick;
            dl.AddRectFilled(in min, in max, Theme.U8(picked ? Theme.AccentSoft : new float4(0.11f, 0.085f, 0.075f, 0.95f)), Gui.S(4), ImDrawFlags.None);
            dl.AddRect(in min, in max, Theme.U8(picked ? Theme.Accent : Theme.Dim, 0.8f), Gui.S(4), ImDrawFlags.None, picked ? 2f : 1f);
            var tp = at - size * 0.5f;
            dl.AddText(in tp, Theme.U8(Theme.Text), label);
        }
        if (_wheelPick >= 0)
        {
            var angle = MathF.Tau * _wheelPick / _wheelShown.Count;
            var tip = center + new float2(MathF.Sin(angle), -MathF.Cos(angle)) * (hub + Gui.S(14));
            dl.AddLine(in center, in tip, Theme.U8(Theme.Accent), Gui.S(3));
        }
    }

    // ---------------- The game menu ----------------

    private bool _padLeaving;

    /// <summary>The top bar's Menu as big buttons: Start or Esc opens it, and again (or B) closes it.</summary>
    private void DrawPadMenu()
    {
        if (!_gameMenu)
        {
            _padLeaving = false;
            return;
        }
        var center = ImGui.GetIO().DisplaySize * 0.5f;
        float2? middle = new float2(0.5f, 0.5f);
        ImGui.SetNextWindowPos(in center, ImGuiCond.Always, in middle);
        Panel.Begin("Menu###padmenu", ref _gameMenu, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings);
        PadWindow("###padmenu", () => _gameMenu = false);
        var wide = (float2?)new float2(Gui.S(260), 0);
        if (ImGui.Button("Back to the village"u8, in wide))
            _gameMenu = false;
        if (PauseMenuLabel() is { } pause && ImGui.Button(pause, in wide))
        {
            _gameMenu = false;
            AnswerPause();
        }
        if (ImGui.Button("Save"u8, in wide))
        {
            _gameMenu = false;
            OpenSaveWindow(SaveWindow.Save);
        }
        if (ImGui.Button("Load"u8, in wide))
        {
            _gameMenu = false;
            OpenSaveWindow(SaveWindow.Load);
        }
        if (ImGui.Button("Settings"u8, in wide))
        {
            _gameMenu = false;
            _settings.Open();
        }
        if (ImGui.Button("Controls"u8, in wide))
            (_gameMenu, _showControls) = (false, true);
        if (ImGui.Button("Recipes"u8, in wide))
            (_gameMenu, _showRecipeBook) = (false, true);
        if (ImGui.Button("Credits"u8, in wide))
            (_gameMenu, _showCredits) = (false, true);
        ImGui.Spacing();
        if (!_padLeaving)
        {
            if (ImGui.Button("Back to title..."u8, in wide))
                _padLeaving = true;
        }
        else
        {
            ImGuiText.Colored(Theme.Dim, Cap(LastSavedText()) + ".");
            if (ImGui.Button("Save, then leave"u8, in wide))
            {
                if (SaveTo(SaveSlots.Quicksave) is { } error)
                    Notify(MessageKind.Warning, error);
                else
                    Host.SwitchTo(new TitleGame());
            }
            if (ImGui.Button("Leave without saving"u8, in wide))
                Host.SwitchTo(new TitleGame());
        }
        Panel.End();
    }
}

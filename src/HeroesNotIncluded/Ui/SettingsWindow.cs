using Brutal.ImGuiApi;
using Narsil;
using Narsil.Platform;
using Narsil.Ui;

namespace HeroesNotIncluded.Ui;

/// <summary>
/// The options screen, shared by the title screen and the village. Edits a draft of
/// <see cref="Settings.Current"/> and applies it on Apply/OK. A window mode or resolution change
/// asks to be confirmed and reverts by itself after <see cref="ConfirmSeconds"/>, so a mode the
/// monitor can't show doesn't strand the player on a black screen.
/// </summary>
public sealed class SettingsWindow(GameHost host)
{
    public const float ConfirmSeconds = 15f;

    private static readonly int[] FpsCaps = [0, 30, 40, 45, 60, 90, 120, 144, 165, 240];
    private static readonly WindowMode[] Modes = [WindowMode.Windowed, WindowMode.Borderless, WindowMode.Fullscreen];

    public bool IsOpen { get; private set; }

    private Settings _draft = new();
    private IReadOnlyList<MonitorInfo> _monitors = [];

    /// <summary>The settings before an unconfirmed display change; non-null while the confirm prompt is up.</summary>
    private Settings? _revertTo;
    private float _confirmLeft;

    /// <summary>Passed by ref to ImGui overloads that take an optional size; null means "auto".</summary>
    private static float2? _autoSize = null;

    public void Open()
    {
        IsOpen = true;
        _draft = Settings.Current.Clone();
        _monitors = Display.Monitors(); // re-query: monitors come and go
    }

    public void Close()
    {
        IsOpen = false;
        Audio.GameAudio.Apply(Settings.Current); // volumes dragged but not applied
    }

    public void Toggle()
    {
        if (IsOpen)
            Close();
        else
            Open();
    }

    public void Draw()
    {
        if (IsOpen)
            DrawWindow();
        if (_revertTo != null)
            DrawConfirm();
    }

    private static float S(float v) => Narsil.Ui.Gui.S(v);

    private void DrawWindow()
    {
        var screen = ImGui.GetIO().DisplaySize;
        var pos = new float2(screen.X * 0.5f, screen.Y * 0.5f);
        float2? pivot = new float2(0.5f, 0.5f);
        ImGui.SetNextWindowPos(in pos, ImGuiCond.Appearing, in pivot);
        var open = true;
        ScreenFit.Next();
        if (!Narsil.Ui.Panel.Begin("Settings", ref open, ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
        {
            Narsil.Ui.Panel.End();
            IsOpen = open;
            return;
        }

        if (ImGui.BeginTabBar("settings"u8, ImGuiTabBarFlags.None))
        {
            if (ImGui.BeginTabItem("Display"u8, ImGuiTabItemFlags.None))
            {
                DisplayTab();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Sound"u8, ImGuiTabItemFlags.None))
            {
                SoundTab();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Interface"u8, ImGuiTabItemFlags.None))
            {
                InterfaceTab();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Gameplay"u8, ImGuiTabItemFlags.None))
            {
                GameplayTab();
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Network"u8, ImGuiTabItemFlags.None))
            {
                NetworkTab();
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }

        var gap = new float2(1, S(6));
        ImGui.Dummy(in gap);
        ImGui.Separator();
        Footer(ref open);

        if (ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) && ImGui.IsKeyPressed(ImGuiKey.Escape, false))
            open = false;
        Narsil.Ui.Panel.End();
        if (!open && IsOpen)
            Close();
        IsOpen = open;
    }

    private void Footer(ref bool open)
    {
        var dirty = Settings.Serialize(_draft.Sanitized()) != Settings.Serialize(Settings.Current);
        if (ImGui.Button("Defaults"u8, in _autoSize))
            _draft = new Settings { Monitor = _draft.Monitor };
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
            ImGuiText.Tooltip("Reset every option on all tabs (press Apply to use them)");

        // Right-align Cancel / Apply / OK.
        var style = ImGui.GetStyle();
        var width = S(90) * 3 + style.ItemSpacing.X * 2;
        ImGui.SameLine(0, -1);
        var avail = ImGui.GetContentRegionAvail().X;
        if (avail > width)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + avail - width);
        var size = (float2?)new float2(S(90), 0);
        if (ImGui.Button("Cancel"u8, in size))
            open = false;
        ImGui.SameLine(0, -1);
        if (!dirty)
            ImGui.BeginDisabled(true);
        if (ImGui.Button("Apply"u8, in size))
            Apply();
        if (!dirty)
            ImGui.EndDisabled();
        ImGui.SameLine(0, -1);
        if (ImGui.Button("OK"u8, in size))
        {
            if (dirty)
                Apply();
            open = false;
        }
    }

    /// <summary>A label on the left and the next widget lined up in a column to its right.</summary>
    private void Row(string label, string? tip = null)
    {
        ImGui.AlignTextToFramePadding();
        ImGui.Text(label);
        if (tip != null && ImGui.IsItemHovered(ImGuiHoveredFlags.None))
            ImGuiText.Tooltip(tip);
        ImGui.SameLine(S(170), -1);
        ImGui.SetNextItemWidth(S(280));
    }

    private static bool Combo<T>(string id, IReadOnlyList<T> items, ref int index, Func<T, string> label)
    {
        var changed = false;
        if (ImGui.BeginCombo(id, label(items[index]), ImGuiComboFlags.None))
        {
            for (var i = 0; i < items.Count; i++)
            {
                var selected = i == index;
                if (ImGui.Selectable(label(items[i]), selected, ImGuiSelectableFlags.None, in _autoSize))
                {
                    changed = i != index;
                    index = i;
                }
                if (selected)
                    ImGui.SetItemDefaultFocus();
            }
            ImGui.EndCombo();
        }
        return changed;
    }

    private static string ModeName(WindowMode mode) => mode switch
    {
        WindowMode.Borderless => "Borderless window",
        WindowMode.Fullscreen => "Full screen",
        _ => "Windowed",
    };

    private static string SizeName(int2 s) => $"{s.X} x {s.Y}";

    private void DisplayTab()
    {
        var gap = new float2(1, S(4));
        ImGui.Dummy(in gap);

        var modeIndex = Math.Max(0, Array.IndexOf(Modes, _draft.WindowMode));
        Row("Display mode", "Borderless covers the screen without changing its resolution and stays open when you alt-tab away.\nFull screen can change the resolution, and minimizes when it loses focus.");
        if (Combo("##mode", Modes, ref modeIndex, ModeName))
            _draft.WindowMode = Modes[modeIndex];

        if (_monitors.Count == 0)
        {
            ImGuiText.Colored(Theme.Warn, "No monitors detected.");
            DisplayTimingRows();
            return;
        }

        var monitorIndex = Math.Max(0, _monitors.ToList().FindIndex(m => m.Name == _draft.Monitor));
        if (_monitors.Count > 1)
        {
            Row("Monitor");
            if (Combo("##monitor", _monitors, ref monitorIndex, m => $"{m.Name} ({SizeName(m.Desktop.Size)})"))
                _draft.Monitor = _monitors[monitorIndex].Name;
        }
        var monitor = _monitors[monitorIndex];

        Row("Resolution", "Window size when windowed, screen resolution in full screen.");
        switch (_draft.WindowMode)
        {
            case WindowMode.Windowed:
            {
                var current = new int2(_draft.Width, _draft.Height);
                var sizes = Display.WindowSizes(monitor.WorkAreaSize, current);
                var index = Math.Max(0, sizes.ToList().IndexOf(current));
                if (Combo("##size", sizes, ref index, SizeName))
                    (_draft.Width, _draft.Height) = (sizes[index].X, sizes[index].Y);
                break;
            }
            case WindowMode.Fullscreen:
            {
                var modes = monitor.Modes.Count > 0 ? monitor.Modes : [monitor.Desktop];
                var picked = Display.PickMode(monitor, new int2(_draft.Width, _draft.Height));
                var index = Math.Max(0, modes.ToList().FindIndex(m => m.Size == picked.Size));
                Combo("##size", modes, ref index, m => $"{SizeName(m.Size)} @ {m.RefreshRate} Hz");
                (_draft.Width, _draft.Height) = (modes[index].Size.X, modes[index].Size.Y);
                if (Glfw.GetPlatform() == GlfwPlatform.Wayland)
                {
                    ImGui.SameLine(0, -1);
                    ImGuiText.Colored(Theme.Dim, "(?)");
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                        ImGuiText.Tooltip("Wayland compositors don't let games change the screen's resolution,\nso full screen may run at the desktop resolution regardless.");
                }
                break;
            }
            default:
                ImGui.BeginDisabled(true);
                var desktop = $"{SizeName(monitor.Desktop.Size)} (desktop)";
                if (ImGui.BeginCombo("##size"u8, desktop, ImGuiComboFlags.None))
                    ImGui.EndCombo();
                ImGui.EndDisabled();
                break;
        }

        DisplayTimingRows();
    }

    private void DisplayTimingRows()
    {
        Row("VSync", "Wait for the display's refresh: no tearing, and no wasted frames.");
        var vsync = _draft.VSync;
        if (ImGui.Checkbox("##vsync"u8, ref vsync))
            _draft.VSync = vsync;

        Row("Frame rate limit", "Cap frames per second to save power and keep the machine cool.");
        var capIndex = Array.IndexOf(FpsCaps, _draft.MaxFps);
        IReadOnlyList<int> caps = capIndex >= 0 ? FpsCaps : [.. FpsCaps, _draft.MaxFps]; // a hand-edited value
        if (capIndex < 0)
            capIndex = caps.Count - 1;
        if (Combo("##fps", caps, ref capIndex, c => c == 0 ? "Unlimited" : $"{c} fps"))
            _draft.MaxFps = caps[capIndex];
    }

    private void InterfaceTab()
    {
        var gap = new float2(1, S(4));
        ImGui.Dummy(in gap);
        Row("UI scale", "Size of text and panels. Applies when you press Apply.");
        var percent = (int)MathF.Round(_draft.UiScale * 100);
        if (ImGui.SliderInt("##uiscale"u8, ref percent, (int)(Settings.MinUiScale * 100), (int)(Settings.MaxUiScale * 100), "%d%%"u8, ImGuiSliderFlags.AlwaysClamp))
            _draft.UiScale = MathF.Round(percent / 5f) * 5f / 100f; // 5% steps

        ImGui.Dummy(in gap);
        Row("Free movement", "Prototype: move freely and fight with the mouse (R draws your weapon). Starts with the next game you start or host.");
        var freeMove = _draft.FreeMovement;
        if (ImGui.Checkbox("##freemove"u8, ref freeMove))
            _draft.FreeMovement = freeMove;

        ImGui.Dummy(in gap);
        Row("Tips", "First-day tips for your trade, and markers on its stations till you've been to each.");
        var tips = _draft.ShowTips;
        if (ImGui.Checkbox("##tips"u8, ref tips))
            _draft.ShowTips = tips;

        ImGui.Dummy(in gap);
        Row("HD-2D look", "Tilt-shift blur, glowing fire, a vignette and warmer colours. F10 toggles it in game.");
        var hd2d = _draft.Hd2d;
        if (ImGui.Checkbox("##hd2d"u8, ref hd2d))
            _draft.Hd2d = hd2d;
        if (!_draft.Hd2d)
            ImGui.BeginDisabled(true);
        Strength("Tilt-shift", "How strongly the top and bottom of the screen blur, like a miniature.", "##tilt"u8, _draft.TiltShift, v => _draft.TiltShift = v);
        Strength("Bloom", "Glow around fire and embers.", "##bloom"u8, _draft.Bloom, v => _draft.Bloom = v);
        Strength("Vignette", "Darker screen corners.", "##vignette"u8, _draft.Vignette, v => _draft.Vignette = v);
        Strength("Warmth", "Shift colours from frost (low) towards gold (high); the middle is a cool Nordic grade.", "##warmth"u8, _draft.Warmth, v => _draft.Warmth = v);
        if (!_draft.Hd2d)
            ImGui.EndDisabled();

        ImGui.Dummy(in gap);
        Row("Logs", "Log files, crash reports and co-op desync dumps. Attach the newest to a bug report.");
        if (ImGui.Button("Open logs folder"u8))
            _logsError = OpenFolder(BuildInfo.LogDirectory);
        ImGuiText.Wrapped(_logsError ?? BuildInfo.LogDirectory);
    }

    private string? _logsError;

    /// <summary>Open a folder in the file manager; returns a message (with the path) if that isn't possible.</summary>
    internal static string? OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
            return null;
        }
        catch (Exception ex)
        {
            Log.Engine.Warning($"Couldn't open {path}: {ex.Message}");
            return $"Couldn't open it here ({ex.Message}). It's at: {path}";
        }
    }

    private void Strength(string label, string hint, ImString id, float value, Action<float> set)
    {
        Row(label, hint);
        var percent = (int)MathF.Round(value * 100);
        if (ImGui.SliderInt(id, ref percent, 0, 100, "%d%%"u8, ImGuiSliderFlags.AlwaysClamp))
            set(percent / 100f);
    }

    private void SoundTab()
    {
        var gap = new float2(1, S(4));
        ImGui.Dummy(in gap);
        // Volumes are heard as they're dragged; Cancel puts them back.
        Strength("Master", "Everything at once.", "##master"u8, _draft.MasterVolume, v => _draft.MasterVolume = v);
        Strength("Music", "The songs of the season.", "##music"u8, _draft.MusicVolume, v => _draft.MusicVolume = v);
        Strength("Effects", "Hammers, pots, locks, fights and the interface.", "##effects"u8, _draft.EffectsVolume, v => _draft.EffectsVolume = v);
        Strength("World", "Wind, birds, the river, fires crackling, a full inn.", "##ambience"u8, _draft.AmbienceVolume, v => _draft.AmbienceVolume = v);
        Strength("Voices", "The babble of people talking within earshot.", "##voices"u8, _draft.VoiceVolume, v => _draft.VoiceVolume = v);
        Audio.GameAudio.Apply(_draft.Sanitized());

        ImGui.Dummy(in gap);
        Row("Babble", "Hear speech as a babble of syllables, every voice its own. Off: only the words on screen.");
        var babble = _draft.Babble;
        if (ImGui.Checkbox("##babble"u8, ref babble))
            _draft.Babble = babble;
        Row("Mute in background", "Go quiet while another window has focus.");
        var mute = _draft.MuteInBackground;
        if (ImGui.Checkbox("##mutebg"u8, ref mute))
            _draft.MuteInBackground = mute;
        if (Audio.GameAudio.Engine.IsSilent)
        {
            ImGui.Dummy(in gap);
            ImGuiText.Colored(Theme.Warn, "No sound device was found; the game is running silent.");
        }
    }

    private void GameplayTab()
    {
        var gap = new float2(1, S(4));
        ImGui.Dummy(in gap);
        Row("Pause on danger", "Pause the game when a raid, fire or other danger is announced (single player, without a character to play: a round never stops).");
        var pause = _draft.PauseOnDanger;
        if (ImGui.Checkbox("##pause"u8, ref pause))
            _draft.PauseOnDanger = pause;

        Row("Camera pan speed", "How fast WASD and the arrow keys move the camera.");
        var pan = _draft.CameraPanSpeed;
        if (ImGui.SliderFloat("##pan"u8, ref pan, Settings.MinPanSpeed, Settings.MaxPanSpeed, "%.2fx"u8, ImGuiSliderFlags.AlwaysClamp))
            _draft.CameraPanSpeed = pan;
    }

    private readonly ImInputString _relay = new(201, ""u8);
    private string? _relayShown;

    private void NetworkTab()
    {
        var gap = new float2(1, S(4));
        ImGui.Dummy(in gap);
        Row("Relay server", "For co-op over the internet: hosts get a join code, friends type it to join. Address or address:port; empty turns it off.");
        if (_relayShown != _draft.RelayServer)
        {
            _relay.SetValue(_draft.RelayServer.AsSpan());
            _relayShown = _draft.RelayServer;
        }
        ImGui.SetNextItemWidth(S(240));
        var edited = ImGui.InputTextWithHint("##relay"u8, "relay.example.com"u8, _relay, ImGuiInputTextFlags.None, null, IntPtr.Zero);
        OnScreenKeyboard.AfterTextField();
        if (edited)
            _relayShown = _draft.RelayServer = Narsil.Ui.ImGuiText.Value(_relay);

        Row("Host online", "When you host, also open the village through the relay so friends anywhere can join with a code.");
        var online = _draft.HostOnline;
        if (ImGui.Checkbox("##online"u8, ref online))
            _draft.HostOnline = online;
    }

    private void Apply()
    {
        var before = Settings.Current;
        var next = _draft.Sanitized();
        Settings.Current = next;
        Audio.GameAudio.Apply(next);
        host.Renderer.SetVSync(next.VSync);
        host.MaxFps = next.MaxFps;
        if (Math.Abs(next.UiScale - Theme.Scale) > 0.001f)
            Theme.Apply(next.UiScale);

        if (next.Display != before.Display)
        {
            // Changing again during the countdown still reverts to the last confirmed mode.
            _revertTo ??= before.Clone();
            _confirmLeft = ConfirmSeconds;
            host.SetDisplay(next.Display);
        }
        else
            Settings.Save();
        _draft = next.Clone();
    }

    private void DrawConfirm()
    {
        _confirmLeft -= ImGui.GetIO().DeltaTime;
        if (_confirmLeft <= 0)
        {
            Revert();
            return;
        }

        var screen = ImGui.GetIO().DisplaySize;
        var pos = new float2(screen.X * 0.5f, screen.Y * 0.35f);
        float2? pivot = new float2(0.5f, 0.5f);
        ImGui.SetNextWindowPos(in pos, ImGuiCond.Always, in pivot);
        ImGui.SetNextWindowFocus();
        Narsil.Ui.Panel.Begin("Keep display settings?", ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoMove);
        ImGui.Text("Keep these display settings?"u8);
        ImGuiText.Colored(Theme.Dim, $"Reverting in {MathF.Ceiling(_confirmLeft):F0} seconds.");
        var size = (float2?)new float2(S(120), 0);
        if (ImGui.Button("Keep"u8, in size))
            Keep();
        ImGui.SameLine(0, -1);
        if (ImGui.Button("Revert"u8, in size))
            Revert();
        Narsil.Ui.Panel.End();
    }

    private void Keep()
    {
        _revertTo = null;
        // What was actually applied: sizes clamped to the screen, the monitor resolved by name.
        Settings.Current.Display = host.Display;
        _draft.Display = host.Display;
        Settings.Save();
    }

    private void Revert()
    {
        var old = _revertTo!;
        _revertTo = null;
        Settings.Current.Display = old.Display;
        _draft.Display = old.Display;
        host.SetDisplay(old.Display);
        Settings.Save();
    }
}

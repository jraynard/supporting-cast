using Narsil;
using Narsil.Platform;

namespace HeroesNotIncluded;

/// <summary>
/// Everything a player does with a character, as actions (<see cref="ActionMap"/>): the keyboard
/// defaults are the keys the game has always used, the gamepad's a Souls-style layout (strikes on
/// the right shoulder, guard on the left, B to dash). Code asks <c>Controls.Map.Pressed(Act.Guard)</c>,
/// never for a key, so players can rebind (Settings, Controls) and a pad works wherever a key does.
/// The god-mode toolbar's letters, Ctrl+speed and the F-keys stay fixed.
/// </summary>
public static class Controls
{
    /// <summary>When an action's live, for clashes: a fight happens in play, the rest apart.</summary>
    public const string Play = "Play", Fight = "Fight", Work = "Work", Menu = "Menu";

    private static Binding K(GlfwKey k) => Binding.Key(k);
    private static Binding M(MouseButton b) => Binding.Mouse(b);
    private static Binding P(PadButton b) => Binding.Pad(b);

    private static ActionDef A(string id, string label, string group, string context, Binding[] keys, params Binding[] pad) =>
        new(id, label, group, context, keys, pad);

    public static readonly ActionDef[] Defs =
    [
        A(Act.WalkUp, "Walk up", "Getting about", Play, [K(GlfwKey.W)], P(PadButton.LeftUp)),
        A(Act.WalkDown, "Walk down", "Getting about", Play, [K(GlfwKey.S)], P(PadButton.LeftDown)),
        A(Act.WalkLeft, "Walk left", "Getting about", Play, [K(GlfwKey.A)], P(PadButton.LeftLeft)),
        A(Act.WalkRight, "Walk right", "Getting about", Play, [K(GlfwKey.D)], P(PadButton.LeftRight)),
        A(Act.Use, "Use, talk to, work at", "Getting about", Play, [K(GlfwKey.F)], P(PadButton.A)),
        A(Act.Dig, "Dig", "Getting about", Play, [K(GlfwKey.C)], P(PadButton.X)),
        A(Act.Drop, "Put down what you hold", "Getting about", Play, [K(GlfwKey.G)]),
        A(Act.Sleep, "Sleep where you stand", "Getting about", Play, [K(GlfwKey.N)]),
        A(Act.Inventory, "Inventory", "Getting about", Play, [K(GlfwKey.Tab)], P(PadButton.View)),
        A(Act.Hotbar1, "Take stack 1 in hand", "Getting about", Play, [K(GlfwKey.Number1)]),
        A(Act.Hotbar2, "Take stack 2 in hand", "Getting about", Play, [K(GlfwKey.Number2)]),
        A(Act.Hotbar3, "Take stack 3 in hand", "Getting about", Play, [K(GlfwKey.Number3)]),
        A(Act.Hotbar4, "Take stack 4 in hand", "Getting about", Play, [K(GlfwKey.Number4)]),
        A(Act.Wheel, "Action wheel (hold)", "Getting about", Play, [], P(PadButton.RB)),

        A(Act.Draw, "Draw or sheathe your weapon", "Fighting", Play, [K(GlfwKey.R)], P(PadButton.Y)),
        A(Act.Strike, "Strike; loose a drawn bow", "Fighting", Fight, [M(MouseButton.Left)], P(PadButton.RT)),
        A(Act.Heavy, "Swing heavy; hold to draw a bow", "Fighting", Fight, [M(MouseButton.Right)], P(PadButton.LT)),
        A(Act.Guard, "Guard (hold); parry as it lands", "Fighting", Fight, [K(GlfwKey.Q), K(GlfwKey.V)], P(PadButton.LB)),
        A(Act.Dash, "Dash clear", "Fighting", Fight, [K(GlfwKey.Space)], P(PadButton.B)),

        A(Act.WorkAct, "The other thing: taste, latch, draw out", "Hands-on work", Work, [K(GlfwKey.Space)], P(PadButton.X)),
        A(Act.Rune1, "Rune 1", "Hands-on work", Work, [K(GlfwKey.Number1)], P(PadButton.DpadUp)),
        A(Act.Rune2, "Rune 2", "Hands-on work", Work, [K(GlfwKey.Number2)], P(PadButton.DpadRight)),
        A(Act.Rune3, "Rune 3", "Hands-on work", Work, [K(GlfwKey.Number3)], P(PadButton.DpadDown)),
        A(Act.Rune4, "Rune 4", "Hands-on work", Work, [K(GlfwKey.Number4)], P(PadButton.DpadLeft)),
        A(Act.Rune5, "Rune 5", "Hands-on work", Work, [K(GlfwKey.Number5)], P(PadButton.LB)),
        A(Act.Rune6, "Rune 6", "Hands-on work", Work, [K(GlfwKey.Number6)], P(PadButton.RB)),
        A(Act.Rune7, "Rune 7", "Hands-on work", Work, [K(GlfwKey.Number7)]),
        A(Act.Rune8, "Rune 8", "Hands-on work", Work, [K(GlfwKey.Number8)]),
        A(Act.Rune9, "Rune 9", "Hands-on work", Work, [K(GlfwKey.Number9)]),
        A(Act.Back, "Back: stop, close, put the work down", "Hands-on work", Menu, [K(GlfwKey.Escape)], P(PadButton.B)),

        A(Act.Shop, "Shop and favours", "Panels", Play, [K(GlfwKey.J)]),
        A(Act.Heard, "Talk (what you've heard)", "Panels", Play, [K(GlfwKey.T)]),
        A(Act.Quests, "Quests", "Panels", Play, [K(GlfwKey.K)]),
        A(Act.Gift, "The gift", "Panels", Play, [K(GlfwKey.Y)]),
        A(Act.Log, "Message log", "Panels", Play, [K(GlfwKey.L)]),
        A(Act.Build, "Build tools", "Panels", Play, [K(GlfwKey.B)]),
        A(Act.ControlsCard, "The controls card", "Panels", Play, [K(GlfwKey.H)], P(PadButton.L3)),
        A(Act.Menu, "Game menu", "Panels", Play, [], P(PadButton.Menu)),

        A(Act.TurnLeft, "Turn the view left", "Camera", Play, [K(GlfwKey.Z)], P(PadButton.DpadLeft)),
        A(Act.TurnRight, "Turn the view right", "Camera", Play, [K(GlfwKey.X)], P(PadButton.DpadRight)),
        A(Act.ZoomIn, "Zoom in", "Camera", Play, [K(GlfwKey.Equal)], P(PadButton.DpadUp)),
        A(Act.ZoomOut, "Zoom out", "Camera", Play, [K(GlfwKey.Minus)], P(PadButton.DpadDown)),
        A(Act.LevelUp, "Show a level up", "Camera", Play, [K(GlfwKey.RightBracket), K(GlfwKey.PageUp)]),
        A(Act.LevelDown, "Show a level down", "Camera", Play, [K(GlfwKey.LeftBracket), K(GlfwKey.PageDown)]),
        A(Act.LevelFollow, "Levels follow you again", "Camera", Play, [K(GlfwKey.Home)]),
        A(Act.Roofs, "Roofs on or off", "Camera", Play, [K(GlfwKey.Backslash)]),
    ];

    /// <summary>The one map, shared by the title screen and the village; settings fill its overrides.</summary>
    public static ActionMap Map { get; } = new(Defs) { Overlap = Overlaps };

    /// <summary>A fight is play with a weapon out, so their keys clash; the rest are never live together.</summary>
    public static bool Overlaps(string a, string b) => a == b || (a, b) is (Play, Fight) or (Fight, Play);

    /// <summary>Hook the map to the window's devices and take the saved bindings.</summary>
    public static void Attach(GameHost host)
    {
        Map.Input = host.Input;
        Map.Pad = host.Gamepad;
        Map.Load(Settings.Current.Bindings);
    }

    /// <summary>A key cap's text for an action, for the device last used.</summary>
    public static string Glyph(string id) => Map.Glyph(id);

    /// <summary>
    /// Two actions as one cap pair for a hint ("A/D", "L←/L→"), or the stick alone ("LS") when
    /// both are on one stick.
    /// </summary>
    public static string Pair(string a, string b)
    {
        var (x, y) = (Glyph(a), Glyph(b));
        if (x.Length == 2 && y.Length == 2 && x[0] == y[0] && x[0] is 'L' or 'R' && "←→↑↓".Contains(x[1]) && "←→↑↓".Contains(y[1]))
            return x[0] + "S";
        return x.Length == 0 ? y : y.Length == 0 ? x : $"{x}/{y}";
    }

    /// <summary>The walk: four caps, or the stick.</summary>
    public static string Walk()
    {
        var all = Pair(Act.WalkUp, Act.WalkDown);
        var side = Pair(Act.WalkLeft, Act.WalkRight);
        return all == side && all.EndsWith('S') ? all : $"{Glyph(Act.WalkUp)}/{Glyph(Act.WalkLeft)}/{Glyph(Act.WalkDown)}/{Glyph(Act.WalkRight)}";
    }

    /// <summary>The key cap for "the mini-game's letter" in its data cues: F, A or D as the player has them bound.</summary>
    public static string Cue(char letter) => letter switch
    {
        'F' => Glyph(Act.Use),
        'A' => Glyph(Act.WalkLeft),
        'D' => Glyph(Act.WalkRight),
        'W' => Glyph(Act.WalkUp),
        'S' => Glyph(Act.WalkDown),
        _ => letter.ToString(),
    };

    /// <summary>
    /// Prose with keys marked as ~F~, ~AD~, ~Space~ and so on (the default keys' names), the
    /// marks turned into what the player has bound, for the device in use.
    /// </summary>
    public static string Keys(string text)
    {
        if (!text.Contains('~'))
            return text;
        return text
            .Replace("~F~", Glyph(Act.Use))
            .Replace("~AD~", Pair(Act.WalkLeft, Act.WalkRight))
            .Replace("~A~", Glyph(Act.WalkLeft))
            .Replace("~D~", Glyph(Act.WalkRight))
            .Replace("~WASD~", Walk())
            .Replace("~Space~", Glyph(Act.WorkAct))
            .Replace("~Esc~", Glyph(Act.Back))
            .Replace("~Tab~", Glyph(Act.Inventory))
            .Replace("~N~", UsingPad && Map.Bindings(Act.Sleep).All(b => !b.IsPad) ? "the wheel" : Glyph(Act.Sleep))
            .Replace("~G~", UsingPad && Map.Bindings(Act.Drop).All(b => !b.IsPad) ? "the wheel" : Glyph(Act.Drop))
            .Replace("~C~", Glyph(Act.Dig))
            .Replace("~J~", Glyph(Act.Shop))
            .Replace("~Y~", Glyph(Act.Gift))
            .Replace("~1-4~", UsingPad ? "the wheel" : $"{Glyph(Act.Hotbar1)}–{Glyph(Act.Hotbar4)}");
    }

    public static bool UsingPad => Map.Device == InputDevice.Gamepad;

    /// <summary>Keep only bindings for actions that exist and that parse, so a hand-edited file can't break the game.</summary>
    public static Dictionary<string, string[]>? Sanitize(Dictionary<string, string[]>? saved)
    {
        if (saved == null)
            return null;
        var probe = new ActionMap(Defs);
        probe.Load(saved);
        var clean = probe.Save();
        return clean.Count == 0 ? null : clean;
    }
}

/// <summary>Action ids (<see cref="Controls"/>); also the keys of the settings file's "Bindings".</summary>
public static class Act
{
    public const string WalkUp = "walk_up", WalkDown = "walk_down", WalkLeft = "walk_left", WalkRight = "walk_right";
    public const string Use = "use", Dig = "dig", Drop = "drop", Sleep = "sleep", Inventory = "inventory", Wheel = "wheel";
    public const string Hotbar1 = "hotbar_1", Hotbar2 = "hotbar_2", Hotbar3 = "hotbar_3", Hotbar4 = "hotbar_4";
    public const string Draw = "draw", Strike = "strike", Heavy = "heavy", Guard = "guard", Dash = "dash";
    public const string WorkAct = "work_act", Back = "back";
    public const string Rune1 = "rune_1", Rune2 = "rune_2", Rune3 = "rune_3", Rune4 = "rune_4", Rune5 = "rune_5",
        Rune6 = "rune_6", Rune7 = "rune_7", Rune8 = "rune_8", Rune9 = "rune_9";
    public const string Shop = "shop", Heard = "heard", Quests = "quests", Gift = "gift", Log = "log", Build = "build",
        ControlsCard = "controls", Menu = "menu";
    public const string TurnLeft = "turn_left", TurnRight = "turn_right", ZoomIn = "zoom_in", ZoomOut = "zoom_out",
        LevelUp = "level_up", LevelDown = "level_down", LevelFollow = "level_follow", Roofs = "roofs";

    public static readonly string[] Hotbar = [Hotbar1, Hotbar2, Hotbar3, Hotbar4];
    public static readonly string[] Runes = [Rune1, Rune2, Rune3, Rune4, Rune5, Rune6, Rune7, Rune8, Rune9];
}

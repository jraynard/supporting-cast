using Brutal.ImGuiApi;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Ui;
using Narsil.Speech;
using Narsil.Ui;

namespace HeroesNotIncluded.Voice;

/// <summary>What the HUD and the settings share for speaking aloud: the words for a volume, and the meter.</summary>
public static class VoiceUi
{
    public static Volume ToVolume(VoiceLevel level) => level switch
    {
        VoiceLevel.Quiet => Volume.Whisper,
        VoiceLevel.Loud => Volume.Shout,
        _ => Volume.Say,
    };

    public static string Word(VoiceLevel level) => level switch
    {
        VoiceLevel.Quiet => "Whisper",
        VoiceLevel.Loud => "Shout",
        _ => "Say",
    };

    /// <summary>As the Heard log colours them: a shout warm, a word plain, a whisper soft.</summary>
    public static float4 Colour(VoiceLevel level) => level switch
    {
        VoiceLevel.Quiet => Theme.Water,
        VoiceLevel.Loud => Theme.Warn,
        _ => Theme.Text,
    };

    /// <summary>
    /// An action's key for a prompt, or null on a pad with no button for it: then a prompt says
    /// where to set one rather than naming a key the player can't press.
    /// </summary>
    public static string? Key(string id) =>
        Controls.UsingPad && Controls.Map.Bindings(id).All(b => !b.IsPad) ? null : Controls.Glyph(id);

    /// <summary>"Hold O" or the pad's button, or how to get one.</summary>
    public static string HoldToTalk() => Key(Act.Talk) is { } k ? $"Hold {k}" : "Set a button for Talk (Settings, Voice), then hold it";

    /// <summary>A level in dBFS as a share of the meter: -60 (a quiet room) to -6 (near clipping).</summary>
    public static float Share(float db) => Math.Clamp((db + 60f) / 54f, 0f, 1f);

    /// <summary>
    /// The microphone's level as a bar, with the calibrated lines marked where a whisper turns to a
    /// word and a word to a shout, so a player can see which side they're speaking on.
    /// </summary>
    public static void Meter(float levelDb, VoiceCalibration? calibration, float width)
    {
        var dl = ImGui.GetWindowDrawList();
        var size = new float2(width, Gui.S(12));
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(in size);
        var max = min + size;
        Gui.Frame(dl, min, max, UiTheme.Current.Frames.Inset, Theme.Well);
        var pad = Gui.S(2);
        var inner = size.X - 2 * pad;
        var share = Share(levelDb);
        var colour = calibration is { } c && levelDb > (c.NormalDb + c.LoudDb) * 0.5f ? Theme.Warn : Theme.Good;
        var fillMin = min + new float2(pad, pad);
        var fillMax = new float2(min.X + pad + inner * share, max.Y - pad);
        if (share > 0f)
            dl.AddRectFilled(in fillMin, in fillMax, Theme.U8(colour), 0f, ImDrawFlags.None);
        if (calibration is not { Measured: true } cal)
            return;
        foreach (var line in new[] { (cal.QuietDb + cal.NormalDb) * 0.5f, (cal.NormalDb + cal.LoudDb) * 0.5f })
        {
            var x = min.X + pad + inner * Share(line);
            var a = new float2(x, min.Y);
            var b = new float2(x, max.Y);
            dl.AddLine(in a, in b, Theme.U8(Theme.Dim, 0.8f), Gui.S(1));
        }
    }

    /// <summary>A plain bar filling to <paramref name="share"/>.</summary>
    public static void Bar(float share, float4 colour, float width, float height = 6)
    {
        var dl = ImGui.GetWindowDrawList();
        var size = new float2(width, Gui.S(height));
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(in size);
        var max = min + size;
        dl.AddRectFilled(in min, in max, Theme.U8(Theme.Well), 0f, ImDrawFlags.None);
        var fillMax = new float2(min.X + size.X * Math.Clamp(share, 0f, 1f), max.Y);
        dl.AddRectFilled(in min, in fillMax, Theme.U8(colour), 0f, ImDrawFlags.None);
    }
}

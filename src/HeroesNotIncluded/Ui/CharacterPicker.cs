using Brutal.ImGuiApi;
using Narsil;
using Narsil.Rendering;
using Narsil.Ui;
using HeroesNotIncluded.Characters;
using HeroesNotIncluded.Net;
using HeroesNotIncluded.Rendering;

namespace HeroesNotIncluded.Ui;

/// <summary>
/// Your saved characters (<see cref="CharacterRoster"/>), one card each: them idling, their traits,
/// and the trades they've played. In <see cref="Mode.Choose"/> it's who you'll be in the game you're
/// starting or joining (Use); in <see cref="Mode.Manage"/> it's the main menu's Characters screen,
/// for making, changing and letting go of them. Delete asks once more before it goes.
/// </summary>
public sealed class CharacterPicker(GameHost host, CharacterRoster roster, Sim.Defs.DefDatabase defs)
{
    public enum Result { None, Use, Edit, New, Back }

    public enum Mode { Choose, Manage }

    public Mode Showing { get; set; } = Mode.Choose;

    /// <summary>The character chosen by the last <see cref="Result.Use"/> or <see cref="Result.Edit"/>.</summary>
    public SavedCharacter? Picked { get; private set; }

    private Guid? _confirmDelete;

    /// <summary>Draw inside the current window.</summary>
    public Result Draw(float time)
    {
        var result = Result.None;
        var size = (float2?)new float2(Gui.S(84), Gui.S(28));
        var manage = Showing == Mode.Manage;
        ImGuiText.Wrapped(manage
            ? "The folk you play. Their looks and what they've done go with them to every village."
            : "Bring one of your characters: they start out fresh here, with nothing but their kit.");
        ImGui.Spacing();
        if (roster.Characters.Count == 0)
            ImGui.TextDisabled("Nobody yet."u8);
        if (roster.ReadOnly)
            ImGui.TextColored(Theme.Warn, "These were saved by a newer version: changes here won't be kept."u8);
        foreach (var c in roster.Characters.ToList())
        {
            ImGui.PushID(c.Id.ToString());
            var sheet = HumanLook.Sheet(host.Atlas, c.Look);
            LookWidgets.Preview(host, sheet, HumanLook.Layers(host.Atlas, c.Look, Rgba.White, HumanLook.WornOf(defs, c.Clothes)), "idle", time, 1.5f);
            ImGui.SameLine(0, 10);
            ImGui.BeginGroup();
            Theme.Header(host.ImGui, c.Name);
            ImGui.TextDisabled(c.Traits.Count > 0 ? string.Join(", ", c.Traits) : "No traits");
            ImGui.TextDisabled(Played(c));
            if (!manage)
            {
                if (ImGui.Button("Use"u8, in size))
                {
                    Picked = c;
                    result = Result.Use;
                }
                ImGui.SameLine(0, 6);
            }
            if (ImGui.Button("Edit"u8, in size))
            {
                Picked = c;
                result = Result.Edit;
            }
            ImGui.SameLine(0, 6);
            if (_confirmDelete == c.Id)
            {
                if (ImGui.Button("Sure?"u8, in size))
                {
                    roster.Remove(c.Id);
                    _confirmDelete = null;
                }
                if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                    ImGuiText.Tooltip($"Forget {c.Name} for good, and all they've done (villages they're already in keep them).");
            }
            else if (ImGui.Button("Delete"u8, in size))
                _confirmDelete = c.Id;
            ImGui.EndGroup();
            ImGui.PopID();
            ImGui.Spacing();
        }
        var wide = (float2?)new float2(Gui.S(160), Gui.S(32));
        if (roster.Full)
            ImGui.TextDisabled("The roster's full: let someone go first."u8);
        ImGui.BeginDisabled(roster.Full);
        if (ImGui.Button("New character"u8, in wide))
            result = Result.New;
        ImGui.EndDisabled();
        ImGui.SameLine(0, 8);
        if (ImGui.Button("Back"u8, in wide))
            result = Result.Back;
        return result;
    }

    /// <summary>"Blacksmith ×3, Cook ×1": the trades they've seen a round through in, most first.</summary>
    private string Played(SavedCharacter c)
    {
        if (c.Progress.Trades.Count == 0)
            return "Not a round played yet";
        return string.Join(", ", c.Progress.Trades
            .OrderByDescending(t => t.Value.Rounds).ThenBy(t => t.Key, StringComparer.Ordinal)
            .Select(t => $"{Label(t.Key)} \u00d7{t.Value.Rounds}"));
    }

    private string Label(string trade) =>
        defs.AllTrades.FirstOrDefault(t => t.Name == trade) is { } def ? char.ToUpperInvariant(def.Label[0]) + def.Label[1..] : trade;
}

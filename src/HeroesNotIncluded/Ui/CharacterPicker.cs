using Brutal.ImGuiApi;
using Narsil;
using Narsil.Rendering;
using Narsil.Ui;
using SupportingCast.Net;
using SupportingCast.Rendering;

namespace SupportingCast.Ui;

/// <summary>
/// Pick one of your saved characters (<see cref="CharacterRoster"/>) to bring into a co-op game, or
/// make a new one. Each card shows them idling, their traits, and their dragon; Use sends them in,
/// Edit opens the creators on them. Delete asks once more before it goes.
/// </summary>
public sealed class CharacterPicker(GameHost host, CharacterRoster roster, Sim.Defs.DefDatabase defs)
{
    public enum Result { None, Use, Edit, New, Back }

    /// <summary>The character chosen by the last <see cref="Result.Use"/> or <see cref="Result.Edit"/>.</summary>
    public SavedCharacter? Picked { get; private set; }

    private Guid? _confirmDelete;

    /// <summary>Draw inside the current window.</summary>
    public Result Draw(float time)
    {
        var result = Result.None;
        var size = (float2?)new float2(84, 28);
        ImGuiText.Wrapped("Bring one of your characters: they start out fresh here, with nothing but their kit.");
        ImGui.Spacing();
        foreach (var c in roster.Characters.ToList())
        {
            ImGui.PushID(c.Id.ToString());
            var sheet = HumanLook.Sheet(host.Atlas, c.Look);
            LookWidgets.Preview(host, sheet, HumanLook.Layers(host.Atlas, c.Look, Rgba.White, HumanLook.WornOf(defs, c.Clothes)), "idle", time, 1.5f);
            ImGui.SameLine(0, 10);
            ImGui.BeginGroup();
            Theme.Header(host.ImGui, c.Name);
            ImGui.TextDisabled(c.Traits.Count > 0 ? string.Join(", ", c.Traits) : "No traits");
            if (ImGui.Button("Use"u8, in size))
            {
                Picked = c;
                result = Result.Use;
            }
            ImGui.SameLine(0, 6);
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
                    ImGuiText.Tooltip($"Forget {c.Name} for good (villages they're already in keep them).");
            }
            else if (ImGui.Button("Delete"u8, in size))
                _confirmDelete = c.Id;
            ImGui.EndGroup();
            ImGui.PopID();
            ImGui.Spacing();
        }
        var wide = (float2?)new float2(160, 32);
        if (roster.Characters.Count >= CharacterRoster.Max)
            ImGui.TextDisabled($"That's {CharacterRoster.Max}: a new one replaces the one you haven't used longest.");
        if (ImGui.Button("New character"u8, in wide))
            result = Result.New;
        ImGui.SameLine(0, 8);
        if (ImGui.Button("Back"u8, in wide))
            result = Result.Back;
        return result;
    }
}

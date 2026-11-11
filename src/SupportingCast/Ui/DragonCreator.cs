using Brutal.ImGuiApi;
using Narsil;
using Narsil.Rendering;
using SupportingCast.Rendering;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Scenarios;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Ui;

/// <summary>
/// Adventure's dragon step (after the character): name the dragon you start with, pick its breed
/// (<see cref="Dragons.Breeds"/>: its art and build) and element (what it breathes, which also
/// colours the glowing core of its flames and starts the palette: its colour, black horns and claws),
/// then adjust its scale, belly, wing, horn and eye colours, with a live preview of an adult drawn from
/// the same tinted layers the world uses (<see cref="DragonLooks"/>). With <see cref="EggOnly"/> it's
/// the egg-bearer's egg step instead: just the breed and element the egg hatches into.
/// </summary>
public sealed class DragonCreator(GameHost host, DefDatabase defs)
{
    public enum Result { None, Continue, Back }

    private static readonly string[] Names = ["Vesper", "Ashwing", "Cinder", "Mournsong", "Tamarisk", "Ember", "Rook", "Solace", "Thistle", "Brand"];

    private static readonly string[] Poses = ["idle", "fly", "breathe_windup"];

    private readonly ImInputString _name = new(Companions.MaxNameLength, "Vesper"u8);
    private DragonLook _look = DragonLooks.ForElement(DragonElement.Fire);
    private readonly CreatureDef[] _breeds = Dragons.Breeds(defs).ToArray();
    private CreatureDef _breed = Dragons.DefaultBreed(defs);
    private int _rolls;
    private int _pose;

    public DragonLook Look => _look;

    public string Name => Companions.CleanName(Narsil.Ui.ImGuiText.Value(_name));

    /// <summary>The breed picked, by def name.</summary>
    public string Breed => _breed.Name;

    /// <summary>The element picked (also <see cref="Look"/>'s).</summary>
    public DragonElement Element => _look.Element;

    /// <summary>The egg-bearer's egg step: breed and element only, the preview a hatchling.</summary>
    public bool EggOnly { get; set; }

    /// <summary>Start from a saved character's dragon.</summary>
    public void Load(string name, DragonLook look, string? breed = null)
    {
        _name.SetValue(name.AsSpan());
        _look = look;
        _breed = Dragons.BreedOrDefault(defs, breed);
    }

    /// <summary>Draw the creator inside the current window. Returns what the player chose to do.</summary>
    public Result Draw(float time)
    {
        var result = Result.None;
        ImGui.BeginGroup();

        if (!EggOnly)
        {
            ImGui.SetNextItemWidth(220);
            ImGui.InputText("Name"u8, _name, ImGuiInputTextFlags.None, null, IntPtr.Zero);
        }
        else
        {
            ImGui.PushTextWrapPos(330);
            ImGui.TextColored(Theme.Dim, "The egg in your keeping. What will hatch from it?"u8);
            ImGui.PopTextWrapPos();
        }

        DrawBreeds();
        DrawElements();

        if (!EggOnly)
        {
            LookWidgets.ColorRow("Scales", 11, ref _look.Scales, DragonLooks.ScaleColors);
            LookWidgets.ColorRow("Belly", 12, ref _look.Belly, DragonLooks.BellyColors);
            LookWidgets.ColorRow("Wings", 13, ref _look.Wings, DragonLooks.WingColors);
            LookWidgets.ColorRow("Horns and claws", 14, ref _look.Horns, DragonLooks.HornColors);
            LookWidgets.ColorRow("Eyes", 15, ref _look.Eyes, DragonLooks.EyeColors);
        }

        ImGui.TextColored(Theme.Dim, "Pose"u8);
        for (var i = 0; i < Poses.Length; i++)
        {
            if (i > 0)
                ImGui.SameLine(0, 12);
            ImGui.RadioButton(i switch { 0 => "Standing", 1 => "Flying", _ => "Fire" }, ref _pose, i);
        }

        var gap = new float2(1, 10);
        ImGui.Dummy(in gap);
        var size = (float2?)new float2(104, 34);
        if (ImGui.Button("Randomize"u8, in size))
            Randomize();
        if (!EggOnly)
        {
            ImGui.SameLine(0, 8);
            if (ImGui.Button("Reset"u8, in size))
                _look = DragonLooks.ForElement(_look.Element);
            if (ImGui.IsItemHovered())
                Narsil.Ui.ImGuiText.Tooltip("Back to the element's own colours, with black horns and claws.");
        }
        var buttons = (float2?)new float2(104, 34);
        if (ImGui.Button("Back"u8, in buttons))
            result = Result.Back;
        ImGui.SameLine(0, 8);
        if (ImGui.Button("Continue"u8, in buttons))
            result = Result.Continue;
        ImGui.EndGroup();

        ImGui.SameLine(0, 28);
        // The breed's adult (its hatchling for an egg), drawn from the same layers the world uses.
        var stage = EggOnly ? _breed.LifeStages[0] : _breed.LifeStages[^1];
        var sheetName = stage.Sprite;
        var sheet = DragonLooks.BaseSheet(host.Atlas, sheetName) ?? (host.Atlas.TryGetSheet(sheetName, out var plain) ? plain : null);
        var pose = EggOnly && _pose == 1 ? "idle" : Poses[_pose]; // hatchlings don't fly
        LookWidgets.Preview(host, sheet, DragonLooks.Layers(host.Atlas, sheetName, _look, Rgba.White), pose, time, EggOnly ? 4f : 2.5f);
        return result;
    }

    /// <summary>The breeds as buttons, the pick highlighted, each with its description as a tooltip.</summary>
    private void DrawBreeds()
    {
        ImGui.TextColored(Theme.Dim, $"Breed: {_breed.Breed!.Label}");
        var size = (float2?)new float2(140, 30);
        for (var i = 0; i < _breeds.Length; i++)
        {
            var breed = _breeds[i];
            if (i > 0)
                ImGui.SameLine(0, 6);
            ImGui.PushID(200 + i);
            var picked = breed == _breed;
            if (picked)
            {
                var c = Theme.AccentSoft;
                ImGui.PushStyleColor(ImGuiCol.Button, in c);
            }
            if (ImGui.Button(breed.Breed!.Label, in size))
                _breed = breed;
            if (picked)
                ImGui.PopStyleColor(1);
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None) && breed.Breed.Description.Length > 0)
                Narsil.Ui.ImGuiText.Tooltip(breed.Breed.Description);
            ImGui.PopID();
        }
    }

    /// <summary>
    /// The five elements as round swatches in their flame colours, the pick ringed in white, and what
    /// the pick breathes (<see cref="ElementDef.Description"/>). Choosing one gives the dragon that
    /// element's breath and colours; the rows below fine-tune the colours from there.
    /// </summary>
    private void DrawElements()
    {
        ImGui.TextColored(Theme.Dim, $"Element: {DragonLooks.Elements.First(e => e.Element == _look.Element).Label}");
        for (var i = 0; i < DragonLooks.Elements.Length; i++)
        {
            var (element, label) = DragonLooks.Elements[i];
            if (i > 0)
                ImGui.SameLine(0, 2);
            ImGui.PushID(100 + i);
            if (LookWidgets.Swatch(DragonLooks.FlameColor(element), _look.Element == element, false) && _look.Element != element)
                _look = DragonLooks.ForElement(element);
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Narsil.Ui.ImGuiText.Tooltip($"{label}: {defs.Element(element).Description}\nThe glowing core of its flames and breath is this colour; picking an element recolours the dragon (black horns and claws).");
            ImGui.PopID();
        }
        // What the pick breathes, under the swatches.
        ImGui.PushTextWrapPos(330);
        ImGui.TextColored(Theme.Dim, Narsil.Ui.ImGuiText.Escape(defs.Element(_look.Element).Description));
        ImGui.PopTextWrapPos();
    }

    private void Randomize()
    {
        _rolls++;
        var rng = new Random(Environment.TickCount ^ (_rolls * 7919));
        _look = DragonLooks.Random(rng);
        _breed = _breeds[rng.Next(_breeds.Length)];
        _name.SetValue(Names[(uint)(Environment.TickCount + _rolls) % Names.Length].AsSpan());
    }
}

using Brutal.ImGuiApi;
using Narsil;
using Narsil.Rendering;
using SupportingCast.Rendering;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Systems;
using Narsil.Ui;

namespace SupportingCast.Ui;

/// <summary>
/// Adventure's first step: name your character and pick their look and clothes, with a live
/// animated preview (turn it, watch them walk) drawn from the same tinted sprite layers the world
/// uses (<see cref="HumanLook"/>). Presets (<see cref="OutfitDef.Preset"/> outfits) set the clothes,
/// hair and face of an archetype in one click; everything stays editable after.
/// </summary>
public sealed class CharacterCreator
{
    public enum Result { None, Continue, Back }

    private enum Tab { Look, Hair, Outfit, Traits }

    private static readonly string[] Names = ["Arin", "Wren", "Tamsin", "Bram", "Isolde", "Corwin", "Maren", "Edric", "Sigrun", "Halvar", "Astrid", "Leif"];

    private static readonly string[] Views = ["down", "downside", "upside", "up"];

    private readonly GameHost host;
    private readonly DefDatabase defs;
    private readonly ImInputString _name = new(32, "Arin"u8);
    private Appearance _look = HumanLook.Default;
    private int _rolls;
    private Tab _tab = Tab.Look;
    private int _view;
    private bool _walk;

    /// <summary>What's worn in each clothes slot (null def: nothing), with its dye.</summary>
    private readonly Dictionary<ApparelSlot, (ThingDef? Def, MaterialDef? Dye)> _clothes = [];

    private readonly List<OutfitDef> _presets;

    public CharacterCreator(GameHost host, DefDatabase defs)
    {
        this.host = host;
        this.defs = defs;
        _presets = defs.AllOutfits.Where(o => o.Preset).ToList();
        if (_presets.FirstOrDefault() is { } first)
            ApplyOutfit(first);
    }

    public Appearance Look => _look;

    private readonly List<TraitDef> _traits = [];

    /// <summary>The picked traits, by name (at most <see cref="TraitRules.MaxTraits"/>, none conflicting).</summary>
    public IReadOnlyList<string> Traits => _traits.Select(t => t.Name).ToList();

    /// <summary>The picked clothes as <c>"Def:Dye"</c> (<see cref="Clothing.Parse"/>).</summary>
    public IReadOnlyList<string> Clothes =>
        Clothing.Slots.Where(s => _clothes.TryGetValue(s, out var c) && c.Def != null).Select(s => Clothing.Write(_clothes[s].Def!, _clothes[s].Dye)).ToList();

    public string Name => Narsil.Ui.ImGuiText.Value(_name).Trim() is { Length: > 0 } n ? n : "Arin";

    /// <summary>Start from a saved character (<see cref="Net.SavedCharacter"/>): editing it, or making the next one like it.</summary>
    public void Load(string name, Appearance look, IEnumerable<string> traits, IEnumerable<string>? clothes = null)
    {
        _name.SetValue(name.AsSpan());
        _look = look;
        _traits.Clear();
        foreach (var t in traits)
            if (defs.AllTraits.FirstOrDefault(d => d.Name == t) is { } def && !_traits.Exists(def.ConflictsWithTrait) && _traits.Count < TraitRules.MaxTraits)
                _traits.Add(def);
        if (clothes?.ToList() is { Count: > 0 } list)
        {
            _clothes.Clear();
            foreach (var text in list)
                if (Clothing.Parse(defs, text) is var (def, dye))
                    _clothes[def.Apparel!.Slot] = (def, dye);
        }
    }

    /// <summary>Draw the creator inside the current window. Returns what the player chose to do.</summary>
    public Result Draw(float time)
    {
        var result = Result.None;
        ImGui.BeginGroup();

        ImGui.SetNextItemWidth(220);
        ImGui.InputText("Name"u8, _name, ImGuiInputTextFlags.None, null, IntPtr.Zero);
        DrawPresets();
        ImGui.Spacing();
        DrawTabs();
        ImGui.Separator();
        switch (_tab)
        {
            case Tab.Look:
                DrawLookTab();
                break;
            case Tab.Hair:
                DrawHairTab();
                break;
            case Tab.Outfit:
                DrawOutfitTab();
                break;
            default:
                DrawTraits();
                break;
        }

        var gap = new float2(1, 10);
        ImGui.Dummy(in gap);
        var size = (float2?)new float2(104, 34);
        if (ImGui.Button("Randomize"u8, in size))
            Randomize();
        ImGui.SameLine(0, 8);
        if (ImGui.Button("Back"u8, in size))
            result = Result.Back;
        ImGui.SameLine(0, 8);
        if (ImGui.Button("Continue"u8, in size))
            result = Result.Continue;
        ImGui.EndGroup();

        ImGui.SameLine(0, 28);
        DrawPreview(time);
        return result;
    }

    /// <summary>One button per archetype: its clothes, hair and face, keeping name, body colours and traits.</summary>
    private void DrawPresets()
    {
        if (_presets.Count == 0)
            return;
        ImGui.TextColored(Theme.Dim, "Start from"u8);
        var size = (float2?)new float2(0, 26);
        for (var i = 0; i < _presets.Count; i++)
        {
            if (i % 4 != 0)
                ImGui.SameLine(0, 4);
            var p = _presets[i];
            if (ImGui.Button($"{p.Label}##preset{p.Name}", in size))
                ApplyPreset(p);
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None) && p.Description.Length > 0)
                ImGuiText.Tooltip(p.Description);
        }
    }

    private void DrawTabs()
    {
        var size = (float2?)new float2(76, 26);
        foreach (var tab in Enum.GetValues<Tab>())
        {
            if (tab != Tab.Look)
                ImGui.SameLine(0, 4);
            var active = _tab == tab;
            if (active)
            {
                var c = Theme.AccentSoft;
                ImGui.PushStyleColor(ImGuiCol.Button, in c);
            }
            if (ImGui.Button(tab.ToString(), in size))
                _tab = tab;
            if (active)
                ImGui.PopStyleColor(1);
        }
    }

    private void DrawLookTab()
    {
        DrawModelNote("Picking a body or hair style switches to a drawn figure.");
        ImGui.TextColored(Theme.Dim, "Body"u8);
        var body = (int)_look.Body;
        ImGui.RadioButton("Masculine"u8, ref body, (int)BodyType.Male);
        ImGui.SameLine(0, 12);
        ImGui.RadioButton("Feminine"u8, ref body, (int)BodyType.Female);
        if ((BodyType)body != _look.Body)
            _look.Model = null;
        _look.Body = (BodyType)body;
        LookWidgets.ColorRow("Skin", 3, ref _look.SkinColor, HumanLook.SkinTones);

        ImGui.TextColored(Theme.Dim, "Eyes"u8);
        var eyes = (int)_look.Eyes;
        for (var i = 0; i < HumanLook.EyeStyles.Length; i++)
        {
            if (i > 0)
                ImGui.SameLine(0, 12);
            ImGui.RadioButton($"{HumanLook.EyeStyles[i].Label}##eyes", ref eyes, (int)HumanLook.EyeStyles[i].Style);
        }
        _look.Eyes = (EyeStyle)eyes;
        LookWidgets.ColorRow("Eye colour", 2, ref _look.EyeColor, HumanLook.EyeColors);

        ImGui.TextColored(Theme.Dim, "Beard"u8);
        var beard = (int)_look.Beard;
        for (var i = 0; i < HumanLook.Beards.Length; i++)
        {
            if (i > 0)
                ImGui.SameLine(0, 12);
            ImGui.RadioButton($"{HumanLook.Beards[i].Label}##beard", ref beard, (int)HumanLook.Beards[i].Style);
        }
        _look.Beard = (BeardStyle)beard;

        ImGui.TextColored(Theme.Dim, "Stance"u8);
        var stance = (int)_look.Stance;
        for (var i = 0; i < HumanLook.Stances.Length; i++)
        {
            if (i > 0)
                ImGui.SameLine(0, 12);
            ImGui.RadioButton($"{HumanLook.Stances[i].Label}##stance", ref stance, (int)HumanLook.Stances[i].Style);
        }
        _look.Stance = (Stance)stance;
    }

    /// <summary>
    /// A look from a 3D model (a preset's) keeps the model's build and hair: say so, and offer the
    /// drawn figure instead.
    /// </summary>
    private void DrawModelNote(string hint)
    {
        if (_look.Model is not { } model)
            return;
        ImGui.TextColored(Theme.Dim, "Figure"u8);
        ImGuiText.Colored(Theme.Text, $"3D model: {model}");
        ImGui.SameLine(0, 12);
        if (ImGui.SmallButton("Use a drawn figure"u8))
            _look.Model = null;
        ImGui.PushStyleColor(ImGuiCol.Text, in Theme.Dim);
        ImGuiText.Wrapped(hint);
        ImGui.PopStyleColor(1);
        ImGui.Spacing();
    }

    private void DrawHairTab()
    {
        DrawModelNote("The model has its own hair; its colour still applies. Picking a style switches to a drawn figure.");
        ImGui.TextColored(Theme.Dim, "Style"u8);
        var hair = (int)_look.Hair;
        for (var i = 0; i < HumanLook.HairStyles.Length; i++)
        {
            if (i % 4 != 0)
                ImGui.SameLine(0, 12);
            ImGui.RadioButton(HumanLook.HairStyles[i].Label, ref hair, (int)HumanLook.HairStyles[i].Style);
        }
        if ((HairStyle)hair != _look.Hair)
            _look.Model = null;
        _look.Hair = (HairStyle)hair;
        LookWidgets.ColorRow("Hair colour", 1, ref _look.HairColor, HumanLook.HairColors);
    }

    /// <summary>A garment per clothes slot (or none), and for cloth ones a row of dye swatches.</summary>
    private void DrawOutfitTab()
    {
        foreach (var slot in Clothing.Slots)
        {
            ImGui.PushID((int)slot);
            _clothes.TryGetValue(slot, out var current);
            ImGui.TextColored(Theme.Dim, SlotLabel(slot));
            ImGui.SetNextItemWidth(200);
            if (ImGui.BeginCombo("##garment"u8, current.Def == null ? "Nothing" : Cap(current.Def.Label), ImGuiComboFlags.None))
            {
                if (ImGui.Selectable("Nothing"u8, current.Def == null, ImGuiSelectableFlags.None, in _autoSize))
                    _clothes[slot] = (null, null);
                foreach (var def in Garments(slot))
                    if (ImGui.Selectable(Cap(def.Label), def == current.Def, ImGuiSelectableFlags.None, in _autoSize))
                        _clothes[slot] = (def, def.MaterialsOfDef != null ? current.Dye ?? Dyes(def).FirstOrDefault() : null);
                ImGui.EndCombo();
            }
            if (current.Def is { MaterialsOfDef: not null } cloth)
            {
                ImGui.SameLine(0, 10);
                var dyes = Dyes(cloth);
                for (var i = 0; i < dyes.Count; i++)
                {
                    if (i > 0)
                        ImGui.SameLine(0, 2);
                    ImGui.PushID(i);
                    var c = dyes[i].Color;
                    if (LookWidgets.Swatch(HumanLook.FromFloat3(new float3(c[0], c[1], c[2])), dyes[i] == current.Dye, false))
                        _clothes[slot] = (cloth, dyes[i]);
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                        ImGuiText.Tooltip($"{Cap(dyes[i].Label)}: {dyes[i].Character}");
                    ImGui.PopID();
                }
            }
            ImGui.PopID();
        }
    }

    private readonly float2? _autoSize = new float2(0, 0);

    private static string SlotLabel(ApparelSlot slot) => slot switch
    {
        ApparelSlot.Tunic => "Tunic or dress",
        ApparelSlot.Body => "Over the tunic",
        ApparelSlot.Legs => "Legs",
        ApparelSlot.Feet => "Feet",
        ApparelSlot.Cloak => "Cloak or mantle",
        ApparelSlot.Neck => "Scarf",
        ApparelSlot.Head => "Head",
        _ => slot.ToString(),
    };

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    private IEnumerable<ThingDef> Garments(ApparelSlot slot) =>
        defs.AllThings.Where(t => t.Apparel?.Slot == slot && t.Apparel.Worn != null && Clothing.IsClothes(t));

    private List<MaterialDef> Dyes(ThingDef def) => defs.AllMaterials.Where(m => m.Fits(def)).ToList();

    /// <summary>Up to two traits as checkboxes; ones that clash with a pick, or past the limit, are greyed out.</summary>
    private void DrawTraits()
    {
        ImGui.TextColored(Theme.Dim, $"Traits (up to {TraitRules.MaxTraits})");
        var column = 0;
        foreach (var trait in defs.AllTraits)
        {
            var picked = _traits.Contains(trait);
            var blocked = !picked && (_traits.Count >= TraitRules.MaxTraits || _traits.Exists(trait.ConflictsWithTrait));
            if (column++ % 3 != 0)
                ImGui.SameLine(0, 12);
            if (blocked)
                ImGui.BeginDisabled(true);
            if (ImGui.Checkbox(trait.Label, ref picked))
            {
                if (picked)
                    _traits.Add(trait);
                else
                    _traits.Remove(trait);
            }
            if (blocked)
                ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGuiText.Tooltip(trait.Description);
        }
    }

    /// <summary>An outfit's pieces in their first dye, every other clothes slot emptied.</summary>
    private void ApplyOutfit(OutfitDef outfit)
    {
        _clothes.Clear();
        foreach (var piece in outfit.Pieces)
            _clothes[piece.ThingDef.Apparel!.Slot] = (piece.ThingDef, piece.MaterialDefs.FirstOrDefault());
    }

    private void ApplyPreset(OutfitDef p)
    {
        ApplyOutfit(p);
        _look = HumanLook.FromPreset(p, _look).Look;
    }

    /// <summary>Apply a preset by its outfit name (<c>--preset</c>); unknown names do nothing.</summary>
    public void ApplyPreset(string name)
    {
        if (_presets.Find(o => o.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } p)
            ApplyPreset(p);
    }

    private void Randomize()
    {
        _rolls++;
        _look = HumanLook.Derived(Environment.TickCount ^ (_rolls * 7919), fantasy: true);
        _traits.Clear();
        for (var n = Random.Shared.Next(TraitRules.MaxTraits + 1); n > 0; n--)
        {
            var open = defs.AllTraits.Where(t => !_traits.Exists(t.ConflictsWithTrait)).ToList();
            if (open.Count > 0)
                _traits.Add(open[Random.Shared.Next(open.Count)]);
        }
        _clothes.Clear();
        foreach (var slot in Clothing.Slots)
        {
            var options = Garments(slot).ToList();
            if (options.Count == 0 || (slot is ApparelSlot.Cloak or ApparelSlot.Body or ApparelSlot.Head && Random.Shared.Next(2) == 0))
                continue;
            var def = options[Random.Shared.Next(options.Count)];
            var dyes = Dyes(def);
            _clothes[slot] = (def, dyes.Count > 0 ? dyes[Random.Shared.Next(dyes.Count)] : null);
        }
        _name.SetValue(Names[(uint)(Environment.TickCount + _rolls) % Names.Length].AsSpan());
    }

    /// <summary>The character at 5× native size in a framed box, turnable, idling or walking.</summary>
    private void DrawPreview(float time)
    {
        ImGui.BeginGroup();
        var sheet = HumanLook.Sheet(host.Atlas, _look);
        var worn = HumanLook.WornOf(defs, Clothes);
        var tag = _walk ? $"walk_{Views[_view]}" : HumanLook.IdleTag(sheet, Views[_view], _look.Stance);
        LookWidgets.Preview(host, sheet, HumanLook.Layers(host.Atlas, _look, Rgba.White, worn), tag, time, 5f);
        var size = (float2?)new float2(34, 26);
        if (ImGui.Button("<"u8, in size))
            _view = (_view + Views.Length - 1) % Views.Length;
        ImGui.SameLine(0, 4);
        if (ImGui.Button(">"u8, in size))
            _view = (_view + 1) % Views.Length;
        ImGui.SameLine(0, 10);
        ImGui.Checkbox("Walk"u8, ref _walk);
        ImGui.EndGroup();
    }
}

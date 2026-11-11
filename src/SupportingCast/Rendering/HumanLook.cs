using Narsil.Rendering;
using SupportingCast.Sim;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Ecs;

namespace SupportingCast.Rendering;

/// <summary>
/// How people are drawn from their <see cref="Appearance"/> and what they wear: a base sheet per
/// body (the person in linen underclothes) plus greyscale layers tinted with the chosen colours
/// (skin, hair and its back half, eyes, beard) and each worn item's layer, whose dyed cloth is a
/// greyscale <c>_dye</c> layer tinted with the item's material (tools/chibi_people.py, docs/ART.md;
/// the sheets are drawn at four times the detail, <c>"density": 4</c>). Every layer has the base sheet's frame layout, so a layer's frame is found by
/// index. People without an Appearance get a stable look derived from their entity. A look with a
/// <see cref="Appearance.Model"/> is drawn from that 3D model's own sheets instead (its body, skin,
/// eyes, face and hair, and only the garments rendered for it), while they exist.
/// </summary>
public static class HumanLook
{
    /// <summary>Preset swatches for the character creator; derived looks pick from these too.</summary>
    public static readonly uint[] SkinTones = [0xF6D6B4, 0xE8BE96, 0xD2A27A, 0xB07A52, 0x8C5A3A, 0x5E3C28];

    /// <summary>The first <see cref="NaturalHairColors"/> are ordinary hair; the rest are fantasy dyes only a player picks.</summary>
    public static readonly uint[] HairColors = [0x2A2220, 0x5A3A22, 0x8A5A30, 0xC8A050, 0xE0D0A0, 0xA8402A, 0x9A9A9A, 0x3A64C8, 0x7A44B8, 0xD8DCE4];

    public const int NaturalHairColors = 7;

    /// <summary>As <see cref="HairColors"/>: the first <see cref="NaturalEyeColors"/> are ordinary eyes.</summary>
    public static readonly uint[] EyeColors = [0x3C2A1E, 0x6A4A2A, 0x3A6EC8, 0x4A8A4A, 0x7A8A9A, 0xC02A2A, 0xEDEDED, 0x8A4AC8];

    public const int NaturalEyeColors = 5;

    /// <summary>Hair styles in the order the creator offers them, with their labels. None stays last: derived looks skip it.</summary>
    public static readonly (HairStyle Style, string Label)[] HairStyles =
    [
        (HairStyle.Short, "Short"), (HairStyle.Shaggy, "Shaggy"), (HairStyle.Long, "Long"), (HairStyle.Ponytail, "Ponytail"),
        (HairStyle.Bun, "Bun"), (HairStyle.SideBraid, "Side braid"), (HairStyle.CrownBraid, "Crown braid"), (HairStyle.Pigtails, "Braids"),
        (HairStyle.Topknot, "Topknot"), (HairStyle.Swept, "Swept"), (HairStyle.BraidedPonytail, "Braided ponytail"), (HairStyle.CurlyShort, "Curly short"), (HairStyle.CurlyLong, "Curly long"),
        (HairStyle.None, "None"),
    ];

    public static readonly (BeardStyle Style, string Label)[] Beards =
    [
        (BeardStyle.None, "None"), (BeardStyle.Stubble, "Stubble"), (BeardStyle.Short, "Short"), (BeardStyle.Full, "Full"), (BeardStyle.Braided, "Braided"),
    ];

    public static readonly (EyeStyle Style, string Label)[] EyeStyles =
    [
        (EyeStyle.Round, "Round"), (EyeStyle.Bright, "Bright"), (EyeStyle.Narrow, "Narrow"), (EyeStyle.Sleepy, "Sleepy"),
    ];

    public static readonly (Stance Style, string Label)[] Stances =
    [
        (Stance.Relaxed, "Relaxed"), (Stance.Crossed, "Arms crossed"), (Stance.Hip, "Hand on hip"), (Stance.Coin, "Coin flip"),
    ];

    /// <summary>The idle tag for a view and stance (<c>idle_downside_coin</c>), or the plain view's when the sheet has none.</summary>
    public static string IdleTag(SpriteSheet? sheet, string view, Stance stance)
    {
        if (stance != Stance.Relaxed)
        {
            var key = ((int)stance << 3) | ViewIndex(view);
            if (!IdleTags.TryGetValue(key, out var name))
                IdleTags[key] = name = $"idle_{view}_{StanceName(stance)}";
            if (sheet?.Find(name) != null)
                return name;
        }
        return view switch { "down" => "idle_down", "downside" => "idle_downside", "upside" => "idle_upside", "up" => "idle_up", _ => "idle" };
    }

    private static int ViewIndex(string view) => view switch { "down" => 0, "downside" => 1, "upside" => 2, "up" => 3, _ => 4 };

    /// <summary>The stance's name in tags: <c>crossed</c>, <c>hip</c>, <c>coin</c>.</summary>
    public static string StanceName(Stance s) => s switch { Stance.Crossed => "crossed", Stance.Hip => "hip", Stance.Coin => "coin", _ => "" };

    private static readonly Dictionary<int, string> IdleTags = [];

    public static readonly Appearance Default = new()
    {
        Body = BodyType.Male, Hair = HairStyle.Short, SkinColor = SkinTones[1], HairColor = HairColors[1], EyeColor = EyeColors[0],
    };

    /// <summary>
    /// A creator preset (<see cref="OutfitDef"/>) on a look: its hair, beard, eyes, stance and hair
    /// colour (the body only if the outfit is for another), and its clothes in their first dye.
    /// </summary>
    public static (Appearance Look, List<string> Clothes) FromPreset(OutfitDef p, Appearance look)
    {
        if (!Sim.Systems.Clothing.Suits(p, look.Body) && Enum.TryParse<BodyType>(p.Body, true, out var body))
            look.Body = body;
        if (Enum.TryParse<HairStyle>(p.Hair, true, out var hair))
            look.Hair = hair;
        look.Beard = Enum.TryParse<BeardStyle>(p.Beard, true, out var beard) ? beard : BeardStyle.None;
        if (Enum.TryParse<EyeStyle>(p.Eyes, true, out var eyes))
            look.Eyes = eyes;
        look.Stance = Enum.TryParse<Stance>(p.Stance, true, out var stance) ? stance : Stance.Relaxed;
        look.Model = Appearance.ValidModel(p.Model);
        if (p.HairColor is { Length: 6 } hex && uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var rgb))
            look.HairColor = rgb;
        if (p.EyeColor is { Length: 6 } eyeHex && uint.TryParse(eyeHex, System.Globalization.NumberStyles.HexNumber, null, out var eye))
            look.EyeColor = eye;
        var clothes = p.Pieces.Select(x => Sim.Systems.Clothing.Write(x.ThingDef, x.MaterialDefs.FirstOrDefault())).ToList();
        return (look, clothes);
    }

    public static Appearance Of(Simulation sim, Entity e) =>
        sim.World.TryGet<Appearance>(e, out var a) ? a : Derived(e.Index);

    /// <summary>
    /// A look picked by hashing an id, so it never touches the sim's random stream. Villagers get natural
    /// colours and some hair; <paramref name="fantasy"/> (the creator's Randomize) also draws the dyed ones
    /// and the bald look. The body is the sim's <see cref="Sim.Systems.Clothing.DerivedBody"/>, so it fits the clothes.
    /// </summary>
    public static Appearance Derived(int id, bool fantasy = false)
    {
        var h = (uint)id * 2654435761u;
        uint Next()
        {
            h ^= h >> 15;
            h *= 2246822519u;
            h ^= h >> 13;
            return h;
        }
        var a = new Appearance
        {
            Body = (BodyType)(Next() % 2),
            Hair = HairStyles[(int)(Next() % (uint)(fantasy ? HairStyles.Length : HairStyles.Length - 1))].Style,
            SkinColor = SkinTones[Next() % (uint)SkinTones.Length],
            HairColor = HairColors[Next() % (uint)(fantasy ? HairColors.Length : NaturalHairColors)],
            EyeColor = EyeColors[Next() % (uint)(fantasy ? EyeColors.Length : NaturalEyeColors)],
        };
        a.Eyes = EyeStyles[Next() % (uint)EyeStyles.Length].Style;
        var beard = Next() % 10;
        a.Beard = a.Body == BodyType.Male && beard < 6 ? Beards[1 + beard % (uint)(Beards.Length - 1)].Style : BeardStyle.None;
        return a;
    }


    /// <summary>The base sheet a look is drawn on: its model's while that has sheets, else the generated one.</summary>
    public static SpriteSheet? Sheet(TextureAtlas atlas, in Appearance a)
    {
        if (ModelOf(atlas, a) is { } m && atlas.TryGetSheet(m.Base, out var model))
            return model;
        return atlas.TryGetSheet(BaseSheet(a), out var sheet) ? sheet : null;
    }

    /// <summary>A 3D model's sheet names (<c>creatures/wanderer3d</c>, <c>_skin</c>, ...), built once per model.</summary>
    public sealed record ModelSheets(string Name, string Base, string Skin, string Hair, string HairBack, string Eyes, string Face, string Beard);

    private static readonly Dictionary<string, ModelSheets> Models = [];

    /// <summary>The look's model, if it has one and the atlas has its base sheet (an old save's model may be gone).</summary>
    public static ModelSheets? ModelOf(TextureAtlas atlas, in Appearance a)
    {
        if (a.Model is not { } name)
            return null;
        if (!Models.TryGetValue(name, out var m))
        {
            var b = $"creatures/{name}";
            Models[name] = m = new ModelSheets(name, b, b + "_skin", b + "_hair", b + "_hair_back", b + "_eyes", b + "_face", b + "_beard");
        }
        return atlas.TryGetSheet(m.Base, out _) ? m : null;
    }

    // Sheet names, built once: they're looked up for every person every frame (tools/chibi_people.py
    // writes them). The base (linen underclothes) and skin are per body; hair, its back half and
    // beards are per style and fit both bodies (the heads are the same); eyes and the face's lines
    // are per body and style; a garment is <worn>_<body>.
    private static readonly string[] BodyNames = ["male", "female"];

    private static int BodyIndex(in Appearance a) => a.Body == BodyType.Female ? 1 : 0;

    private static readonly (string Base, string Skin)[] BodySheets =
        BodyNames.Select(b => ($"creatures/person_{b}", $"creatures/person_{b}_skin")).ToArray();

    private static readonly (string Front, string Back)[] HairSheets =
        Enum.GetValues<HairStyle>().Select(h => ($"creatures/person_hair_{StyleName(h)}", $"creatures/person_hair_{StyleName(h)}_back")).ToArray();

    private static readonly (string Eyes, string Face)[] EyeSheets =
        Enum.GetValues<EyeStyle>().SelectMany(e => BodyNames.Select(b => ($"creatures/person_{b}_eyes_{Lower(e)}", $"creatures/person_{b}_face_{Lower(e)}"))).ToArray();

    private static readonly string[] BeardSheets = Enum.GetValues<BeardStyle>().Select(b => $"creatures/person_beard_{Lower(b)}").ToArray();

    private static string Lower<T>(T value) where T : Enum => value.ToString().ToLowerInvariant();

    /// <summary>The sheet name part for a style (<c>curlyshort</c>).</summary>
    public static string StyleName(HairStyle style) => Lower(style);

    private static T Pick<T>(T[] names, int i) => names[(uint)i < names.Length ? i : 0];

    public static string BaseSheet(in Appearance a) => BodySheets[BodyIndex(a)].Base;
    public static string SkinSheet(in Appearance a) => BodySheets[BodyIndex(a)].Skin;
    public static string HairSheet(in Appearance a) => Pick(HairSheets, (int)a.Hair).Front;
    public static string HairBackSheet(in Appearance a) => Pick(HairSheets, (int)a.Hair).Back;
    public static string EyesSheet(in Appearance a) => Pick(EyeSheets, (int)a.Eyes * 2 + BodyIndex(a)).Eyes;
    public static string FaceSheet(in Appearance a) => Pick(EyeSheets, (int)a.Eyes * 2 + BodyIndex(a)).Face;
    public static string BeardSheet(in Appearance a) => Pick(BeardSheets, (int)a.Beard);

    /// <summary>A layer's frame matching a base-sheet frame (same index in the shared layout).</summary>
    public static SheetFrame? LayerFrame(SpriteSheet baseSheet, SpriteSheet layer, in SheetFrame frame)
    {
        // By index, never by value: frames whose pixels match (the base of crossed arms and a hand
        // on the hip) are one atlas sprite, so a value search would find the first of them.
        var i = frame.Index >= 0 ? frame.Index : Array.IndexOf(baseSheet.Frames, frame);
        return i >= 0 && i < layer.Frames.Length ? layer.Frames[i] : null;
    }

    /// <summary>
    /// What a person has on, for drawing: the apparel, how worn out it is (item HP share), and the
    /// dye of cloth garments (0xRRGGBB, their material's colour; null for undyed things like leather).
    /// </summary>
    public readonly record struct WornItem(ApparelProps Props, float Condition, uint? Dye = null);

    /// <summary>Below this condition worn armor is drawn darker (the inspector's red bar).</summary>
    public const float BatteredBelow = 0.3f;

    /// <summary>Everything a creature wears, in slot order (<see cref="Layers"/> sorts out the drawing order).</summary>
    public static List<WornItem> WornOf(Simulation sim, Entity e)
    {
        var list = new List<WornItem>(4);
        foreach (var item in sim.Worn(e))
        {
            var t = sim.World.Get<Thing>(item);
            if (t.Def.Apparel is { } a)
                list.Add(new WornItem(a, t.MaxHp > 0 ? t.Hp / t.MaxHp : 1f, DyeOf(t.Def, t.Material)));
        }
        return list;
    }

    /// <summary>Clothes written as <c>"Def:Dye"</c> (the creator's, a saved character's), as worn items for previews.</summary>
    public static List<WornItem> WornOf(DefDatabase defs, IEnumerable<string> clothes)
    {
        var list = new List<WornItem>(4);
        foreach (var text in clothes)
            if (Sim.Systems.Clothing.Parse(defs, text) is var (def, mat) && def.Apparel is { } a)
                list.Add(new WornItem(a, 1f, DyeOf(def, mat)));
        return list;
    }

    /// <summary>A garment's dye colour: its material's, when it's made of dyed cloth.</summary>
    public static uint? DyeOf(ThingDef def, MaterialDef? material) =>
        material != null && def.MaterialsOfDef != null ? FromFloat3(new float3(material.Color[0], material.Color[1], material.Color[2])) : null;

    /// <summary>
    /// A worn item's layer sheet for this look: <c>_body</c>, then the plain name; for a model only
    /// <c>_model</c> (a garment not rendered for it isn't drawn).
    /// </summary>
    public static SpriteSheet? WornSheet(TextureAtlas atlas, string worn, in Appearance a, ModelSheets? model = null)
    {
        if (model != null)
        {
            if (!ModelWorn.TryGetValue((worn, model.Name), out var name))
                ModelWorn[(worn, model.Name)] = name = $"{worn}_{model.Name}";
            return atlas.TryGetSheet(name, out var m) ? m : null;
        }
        if (!WornNames.TryGetValue(worn, out var names))
            WornNames[worn] = names = BodyNames.Select(b => $"{worn}_{b}").ToArray();
        return atlas.TryGetSheet(names[BodyIndex(a)], out var s) || atlas.TryGetSheet(worn, out s) ? s : null;
    }

    /// <summary>A worn layer's sheet name per body, so drawing doesn't build strings every frame.</summary>
    private static readonly Dictionary<string, string[]> WornNames = [];

    private static readonly Dictionary<(string Worn, string Model), string> ModelWorn = [];

    /// <summary>The <c>_dye</c>, <c>_back</c> and <c>_back_dye</c> names of a worn sheet, built once.</summary>
    private static readonly Dictionary<string, (string Dye, string Back, string BackDye)> Parts = [];

    private static (string Dye, string Back, string BackDye) PartsOf(string worn)
    {
        if (!Parts.TryGetValue(worn, out var p))
            Parts[worn] = p = (worn + "_dye", worn + "_back", worn + "_back_dye");
        return p;
    }

    /// <summary>
    /// The layers to draw over a base sheet, in order: a cloak's back panel (cut around the person,
    /// so drawn first it hangs behind them), tinted skin, eyes and lashes, clothes from the legs up,
    /// body armor, packs, the cloak, the beard, tinted hair (unless a helm hides it), head armor.
    /// Worn things are drawn as is (times <paramref name="color"/>), darker once battered, with their
    /// dyed cloth tinted by the dye.
    /// </summary>
    /// <summary>A weapon or tool in the working hand: its held shape (<see cref="ThingDef.Held"/>) and the 0xRRGGBB its metal is tinted.</summary>
    public readonly record struct HeldItem(string Shape, uint Metal);

    /// <summary>What a thing def looks like in hand, or null if it isn't drawn there.</summary>
    public static HeldItem? HeldOf(ThingDef def) => def.Held is { Length: > 0 } shape ? new HeldItem(shape, MetalTint(def.GearTier)) : null;

    /// <summary>The colour of a tier's metal (or stone): 1 stone, 2 copper, 3 bronze, 4 iron, 5 steel, 6 dragonforged.</summary>
    public static uint MetalTint(int tier) => tier switch
    {
        <= 1 => 0x8C8A86,
        2 => 0xC8743E,
        3 => 0xC09040,
        4 => 0x8C929C,
        5 => 0xC4CED8,
        _ => 0x6A2E34,
    };

    private static readonly Dictionary<string, (string Plain, string Metal, string Back, string BackMetal)> HeldNames = [];

    private static (string Plain, string Metal, string Back, string BackMetal) HeldNamesOf(string shape)
    {
        if (!HeldNames.TryGetValue(shape, out var n))
            HeldNames[shape] = n = ($"apparel/held_{shape}", $"apparel/held_{shape}_metal", $"apparel/held_{shape}_back", $"apparel/held_{shape}_back_metal");
        return n;
    }

    private static void AddHeld(TextureAtlas atlas, List<(SpriteSheet, uint)> list, in Appearance a, uint color, string plain, string metal, uint tint, ModelSheets? model)
    {
        if (WornSheet(atlas, plain, a, model) is { } p)
            list.Add((p, color));
        if (WornSheet(atlas, metal, a, model) is { } m)
            list.Add((m, Multiply(color, Dye(tint))));
    }

    public static List<(SpriteSheet Sheet, uint Color)> Layers(TextureAtlas atlas, in Appearance a, uint color, IReadOnlyList<WornItem>? worn = null, HeldItem? held = null)
    {
        var model = ModelOf(atlas, a);
        var list = new List<(SpriteSheet, uint)>(12);
        var hairColor = Multiply(color, Lift(a.HairColor, HairLift));
        // What's in hand, from behind: under everything (its frames are empty in the views facing us).
        if (held is { } h)
        {
            var hn = HeldNamesOf(h.Shape);
            AddHeld(atlas, list, a, color, hn.Back, hn.BackMetal, h.Metal, model);
        }
        // Long hair falls behind the body (its _back half): drawn first, under everything but a cloak's back.
        var hairBack = atlas.TryGetSheet(model?.HairBack ?? HairBackSheet(a), out var hb) ? hb : null;
        if (worn != null)
            foreach (var w in worn)
                if (w.Props.Slot is ApparelSlot.Cloak or ApparelSlot.Neck && w.Props.Worn is { } cloak)
                {
                    var (_, back, backDye) = PartsOf(cloak);
                    Add(atlas, list, back, w, a, color, null, model);
                    Add(atlas, list, backDye, w, a, color, w.Dye, model);
                }
        if (hairBack != null)
            list.Add((hairBack, hairColor));
        if (atlas.TryGetSheet(model?.Skin ?? SkinSheet(a), out var skin))
            list.Add((skin, Multiply(color, a.SkinColor)));
        if (atlas.TryGetSheet(model?.Eyes ?? EyesSheet(a), out var eyes))
            list.Add((eyes, Multiply(color, a.EyeColor)));
        if (atlas.TryGetSheet(model?.Face ?? FaceSheet(a), out var face))
            list.Add((face, color));
        var hidesHair = false;
        if (worn != null)
        {
            foreach (var slot in DrawOrder)
                foreach (var w in worn)
                    if (w.Props.Slot == slot)
                        AddWorn(atlas, list, w, a, color, model);
            foreach (var w in worn)
                if (w.Props.Slot is ApparelSlot.Head)
                    hidesHair |= w.Props.HidesHair && w.Props.Worn != null && WornSheet(atlas, w.Props.Worn, a, model) != null;
        }
        if (held is { } h2)                                                                       // in hand, facing us: over the clothes
        {
            var hn = HeldNamesOf(h2.Shape);
            AddHeld(atlas, list, a, color, hn.Plain, hn.Metal, h2.Metal, model);
        }
        if (a.Beard != BeardStyle.None && atlas.TryGetSheet(model?.Beard ?? BeardSheet(a), out var beard))   // a model has at most one beard
            list.Add((beard, hairColor));
        if (!hidesHair && atlas.TryGetSheet(model?.Hair ?? HairSheet(a), out var hair))
            list.Add((hair, hairColor));
        if (worn != null)
            foreach (var w in worn)
                if (w.Props.Slot is ApparelSlot.Head)
                    AddWorn(atlas, list, w, a, color, model);
        return list;
    }

    /// <summary>Worn things under the hair, inside out: clothes, then armor over them, packs, cloaks, a scarf over all.</summary>
    private static readonly ApparelSlot[] DrawOrder =
        [ApparelSlot.Legs, ApparelSlot.Feet, ApparelSlot.Tunic, ApparelSlot.Body, ApparelSlot.Back, ApparelSlot.Cloak, ApparelSlot.Neck];

    private static void AddWorn(TextureAtlas atlas, List<(SpriteSheet, uint)> list, in WornItem w, in Appearance a, uint color, ModelSheets? model)
    {
        if (w.Props.Worn is not { } worn)
            return;
        Add(atlas, list, worn, w, a, color, null, model);
        Add(atlas, list, PartsOf(worn).Dye, w, a, color, w.Dye, model);
    }

    private static void Add(TextureAtlas atlas, List<(SpriteSheet, uint)> list, string sheet, in WornItem w, in Appearance a, uint color, uint? dye,
        ModelSheets? model)
    {
        if (WornSheet(atlas, sheet, a, model) is not { } s)
            return;
        var c = w.Condition < BatteredBelow ? Multiply(color, 0xB8B0A8) : color;
        list.Add((s, dye is { } d ? Multiply(c, Dye(d)) : c));
    }

    /// <summary>
    /// The tint for a <c>_dye</c> layer: its main shade is grey 200 (tools/norse_people.py's
    /// DYE_GREYS), so the dye is scaled up by 255/200 to land there, highlights going lighter.
    /// </summary>
    public static uint Dye(uint rgb)
    {
        static uint Up(uint v) => (uint)Math.Min(255, (int)(v * 1.275f + 0.5f));
        return Up((rgb >> 16) & 0xFF) << 16 | Up((rgb >> 8) & 0xFF) << 8 | Up(rgb & 0xFF);
    }

    /// <summary>
    /// Hair is shaded from grey 96 to white, its main shade about 172: lifting the tint puts the
    /// chosen colour near that shade, so dark hair still gets a sheen lighter than its colour.
    /// Less than the full 255/172, so blond doesn't wash out to white.
    /// </summary>
    public const float HairLift = 1.3f;

    /// <summary>A 0xRRGGBB colour scaled by <paramref name="k"/>, clamped.</summary>
    public static uint Lift(uint rgb, float k)
    {
        uint Up(uint v) => (uint)Math.Min(255, (int)(v * k + 0.5f));
        return Up((rgb >> 16) & 0xFF) << 16 | Up((rgb >> 8) & 0xFF) << 8 | Up(rgb & 0xFF);
    }

    /// <summary>0xRRGGBB as a packed <see cref="Rgba"/> colour.</summary>
    public static uint Pack(uint rgb, float alpha = 1f) =>
        Rgba.Pack(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);

    /// <summary>A packed colour times a 0xRRGGBB one, keeping the packed alpha.</summary>
    public static uint Multiply(uint packed, uint rgb)
    {
        float C(uint v, int shift) => ((v >> shift) & 0xFF) / 255f;
        return Rgba.Pack(C(packed, 0) * C(rgb, 16), C(packed, 8) * C(rgb, 8), C(packed, 16) * C(rgb, 0), C(packed, 24));
    }

    /// <summary>0xRRGGBB to 0..1 components (for ImGui colour pickers) and back.</summary>
    public static float3 ToFloat3(uint rgb) => new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);

    public static uint FromFloat3(float3 c) =>
        (uint)(Math.Clamp(c.X, 0, 1) * 255 + 0.5f) << 16 | (uint)(Math.Clamp(c.Y, 0, 1) * 255 + 0.5f) << 8 | (uint)(Math.Clamp(c.Z, 0, 1) * 255 + 0.5f);
}

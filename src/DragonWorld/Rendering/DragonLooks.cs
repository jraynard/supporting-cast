using Narsil.Rendering;
using DragonWorld.Sim;
using DragonWorld.Sim.Components;
using DragonWorld.Sim.Ecs;

namespace DragonWorld.Rendering;

/// <summary>
/// How dragons are drawn from their <see cref="DragonLook"/>: each life stage's sheet has a base
/// layer (outline, face details; <c>_base</c>) plus greyscale scale, belly, wing, horn and eye
/// layers tinted with the chosen colours (see tools/placeholder_sheets.py), and a flame layer
/// (just the hot core of the flames) and an ember-vein layer tinted with the element's colour. They share the plain
/// sheet's frame layout, so <see cref="HumanLook.LayerFrame"/> finds a layer's frame. Dragons
/// without a look (hatched from an egg, wild) get one derived from their entity, cosmetic only.
/// </summary>
public static class DragonLooks
{
    /// <summary>"flame" (the hot core of the tail flame and chest glow) and "veins" (the ember veins, all of it) are tinted by the element rather than a pick.</summary>
    public static readonly string[] LayerNames = ["scales", "belly", "wings", "horns", "eyes", "flame", "veins"];

    /// <summary>
    /// The placeholder art's own colours: each group's main tone divided by its grey in the layer
    /// (scales 200, belly 230, wings 230, horns 255, eyes 255), so this look matches the plain sheet.
    /// </summary>
    public static readonly DragonLook Default = new()
    {
        Scales = 0x9E402E, Belly = 0xEDD9B1, Wings = 0x662621, Horns = 0xE4D8BE, Eyes = 0xFFD650,
    };

    /// <summary>The elements in the order the creator offers them, with their labels.</summary>
    public static readonly (DragonElement Element, string Label)[] Elements =
    [
        (DragonElement.Fire, "Fire"), (DragonElement.Ice, "Ice"), (DragonElement.Lightning, "Lightning"),
        (DragonElement.Poison, "Poison"), (DragonElement.Nature, "Nature"),
    ];

    /// <summary>The colour of a flame's glowing core (tail flame, ember veins, breath); the rest of the flame stays orange (<see cref="DragonLook.Element"/>).</summary>
    public static uint FlameColor(DragonElement element) => element switch
    {
        DragonElement.Ice => 0x5CC8FF,
        DragonElement.Lightning => 0xFFE838,
        DragonElement.Poison => 0xB24CFF,
        DragonElement.Nature => 0x4CD864,
        _ => 0xFF4A24,
    };

    /// <summary>Black: what horns and claws are made with, whatever the element.</summary>
    public const uint BlackHorns = 0x2E2A2E;

    // The presets start with each element's own colour (in Elements order), so a new dragon's pick is
    // always one of the swatches; the rest are other palettes, including the classic rust and bone.
    public static readonly uint[] ScaleColors = [0xB03A2C, 0x4A86C8, 0xD8B02A, 0x7A44A8, 0x3E8A4A, 0x3A3E48, 0xD8D4CC, 0x1E1A1C, 0x9E402E];

    public static readonly uint[] BellyColors = [0xE8B48C, 0xD2E8F6, 0xF4E8A8, 0xD6BCE6, 0xD0E2A8, 0xEDD9B1, 0xC8B89A, 0xB8B8C8];

    public static readonly uint[] WingColors = [0x6E1F1C, 0x2A4F8A, 0x8E7014, 0x4A2662, 0x2A5A32, 0x2A2C38, 0xA8A098, 0x662621];

    public static readonly uint[] HornColors = [BlackHorns, 0xE4D8BE, 0xC8B080, 0x9AA0A8, 0x6A4A30];

    public static readonly uint[] EyeColors = [0xFFB030, 0x9AE4FF, 0xFFF060, 0xDC70FF, 0x9CFF6A, 0xF0F0F0, 0xFFD650];

    /// <summary>
    /// A new dragon of an element: scales, belly, wings and eyes in its colour, black horns and claws.
    /// (<see cref="Default"/> is the older classic rust and bone, which wild dragons still mostly wear.)
    /// </summary>
    public static DragonLook ForElement(DragonElement element)
    {
        var i = Math.Clamp((int)element, 0, Elements.Length - 1);
        return new DragonLook
        {
            Element = element, Scales = ScaleColors[i], Belly = BellyColors[i], Wings = WingColors[i], Horns = BlackHorns, Eyes = EyeColors[i],
        };
    }

    /// <summary>
    /// How a dragon looks: its chosen look, else one derived from its entity. The element is always
    /// the dragon's own (<see cref="Dragon.Element"/>, what it breathes); a dragon without a look
    /// that isn't Fire (hatched from a chosen egg) wears its element's colours.
    /// </summary>
    public static DragonLook Of(Simulation sim, Entity e)
    {
        var w = sim.World;
        var element = w.TryGet<Dragon>(e, out var d) ? d.Element : DragonElement.Fire;
        if (w.TryGet<DragonLook>(e, out var look))
            return look with { Element = element };
        var derived = Derived(e.Index);
        return element == DragonElement.Fire ? derived : ForElement(element) with { Eyes = derived.Eyes };
    }

    /// <summary>
    /// A look picked by hashing an id, so it never touches the sim's random stream. Mostly the
    /// classic rust and bone, so a wild or egg-born dragon usually looks like the art.
    /// </summary>
    public static DragonLook Derived(int id)
    {
        var h = (uint)id * 2654435761u + 0x9E3779B9u;
        uint Next()
        {
            h ^= h >> 15;
            h *= 2246822519u;
            h ^= h >> 13;
            return h;
        }
        if (Next() % 3 != 0)
            return Default with { Eyes = EyeColors[Next() % (uint)EyeColors.Length] };
        var i = Next() % (uint)ScaleColors.Length;
        return new DragonLook
        {
            Scales = ScaleColors[i],
            Belly = BellyColors[Next() % (uint)BellyColors.Length],
            Wings = i < WingColors.Length ? WingColors[i] : WingColors[Next() % (uint)WingColors.Length],
            Horns = HornColors[Next() % (uint)HornColors.Length],
            Eyes = EyeColors[Next() % (uint)EyeColors.Length],
        };
    }

    /// <summary>
    /// A random look for the creator's Randomize button: a random element in its own colours, each of
    /// scales, belly, wings and eyes now and then swapped for any preset, horns and claws mostly black.
    /// </summary>
    public static DragonLook Random(Random rng)
    {
        uint Pick(uint[] colors) => colors[rng.Next(colors.Length)];
        uint Maybe(uint own, uint[] colors) => rng.Next(4) == 0 ? Pick(colors) : own;
        var look = ForElement(Elements[rng.Next(Elements.Length)].Element);
        look.Scales = Maybe(look.Scales, ScaleColors);
        look.Belly = Maybe(look.Belly, BellyColors);
        look.Wings = Maybe(look.Wings, WingColors);
        look.Eyes = Maybe(look.Eyes, EyeColors);
        if (rng.Next(5) == 0)
            look.Horns = Pick(HornColors);
        return look;
    }

    public static uint ColorOf(in DragonLook look, string layer) => layer switch
    {
        "scales" => look.Scales,
        "belly" => look.Belly,
        "wings" => look.Wings,
        "horns" => look.Horns,
        "flame" or "veins" => FlameColor(look.Element),
        _ => look.Eyes,
    };

    /// <summary>The base layer of a dragon sheet, or null for art without layers (drawn plain).</summary>
    public static SpriteSheet? BaseSheet(TextureAtlas atlas, string sheet) =>
        atlas.TryGetSheet(NamesOf(sheet)[^1], out var b) ? b : null;

    /// <summary>Per sheet: each layer's sheet name (in <see cref="LayerNames"/> order), then the base's. Built once, not per frame.</summary>
    private static readonly Dictionary<string, string[]> SheetNames = [];

    private static string[] NamesOf(string sheet)
    {
        if (!SheetNames.TryGetValue(sheet, out var names))
        {
            names = new string[LayerNames.Length + 1];
            for (var i = 0; i < LayerNames.Length; i++)
                names[i] = $"{sheet}_{LayerNames[i]}";
            names[^1] = sheet + "_base";
            SheetNames[sheet] = names;
        }
        return names;
    }

    /// <summary>The tinted layers over a dragon sheet's base, in drawing order.</summary>
    public static List<(SpriteSheet Sheet, uint Color)> Layers(TextureAtlas atlas, string sheet, in DragonLook look, uint color)
    {
        var list = new List<(SpriteSheet, uint)>(LayerNames.Length);
        var names = NamesOf(sheet);
        for (var i = 0; i < LayerNames.Length; i++)
            if (atlas.TryGetSheet(names[i], out var s))
                list.Add((s, HumanLook.Multiply(color, ColorOf(look, LayerNames[i]))));
        return list;
    }
}

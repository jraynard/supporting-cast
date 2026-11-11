using Narsil.Rendering;
using SupportingCast.Sim.Defs;

namespace SupportingCast.Rendering;

/// <summary>
/// Art for things made of a material: <c>buildings/wall_wood</c> made of birch draws
/// <c>buildings/wall_wood_birch</c> when that sprite exists, else the generic sprite tinted with
/// the material's colour. Lookups are cached per atlas version, so a hot reload picks up new art.
/// </summary>
public sealed class MaterialArt(TextureAtlas atlas)
{
    private readonly Dictionary<(string, MaterialDef), (string Sprite, uint Tint)> _cache = [];
    private int _version = -1;

    public (string Sprite, uint Tint) Resolve(string sprite, MaterialDef? material)
    {
        if (material == null)
            return (sprite, Rgba.White);
        if (_version != atlas.Version)
        {
            _cache.Clear();
            _version = atlas.Version;
        }
        if (_cache.TryGetValue((sprite, material), out var hit))
            return hit;
        var specific = sprite + material.SpriteSuffix;
        var result = atlas.TryGet(specific, out _)
            ? (specific, Rgba.White)
            : (sprite, Rgba.Pack(material.Color.ElementAtOrDefault(0), material.Color.ElementAtOrDefault(1), material.Color.ElementAtOrDefault(2)));
        _cache[(sprite, material)] = result;
        return result;
    }
}

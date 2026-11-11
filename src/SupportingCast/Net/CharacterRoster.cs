using System.Text.Json;
using Narsil;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;

namespace SupportingCast.Net;

/// <summary>
/// A character the player made once and brings into any co-op game: who they are and look like,
/// and the dragon they fly in on (Pact games). Only that carries over: skills, gear and the dragon's
/// age start fresh in every village (<see cref="Sim.Systems.CreateCharacterCommand"/>).
/// </summary>
public sealed record SavedCharacter
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "Arin";
    public Appearance Look { get; init; }
    public List<string> Traits { get; init; } = [];

    /// <summary>What they wear, as <c>"Def:Dye"</c> (<see cref="Sim.Systems.Clothing.Parse"/>): part of their look.</summary>
    public List<string> Clothes { get; init; } = [];

    public DateTime LastUsed { get; init; }

}

/// <summary>
/// The player's saved characters (<see cref="SavedCharacter"/>), in <c>characters.json</c> beside
/// their settings. Game-side only: the sim only ever sees the <c>CreateCharacterCommand</c> a pick sends.
/// </summary>
public sealed class CharacterRoster
{
    public const int Max = 8;

    /// <summary>From <c>--character</c>: use the saved character of this name without asking (scripted co-op tests).</summary>
    public static string? PickOverride { get; set; }

    private static readonly JsonSerializerOptions Json = new() { IncludeFields = true, WriteIndented = true };

    public List<SavedCharacter> Characters { get; private set; } = [];

    public string? Path { get; private set; }

    public static string PathFor(GameHost host) => System.IO.Path.Combine(host.ConfigDirectory, "characters.json");

    public static CharacterRoster Load(GameHost host, DefDatabase defs) => Load(PathFor(host), defs);

    /// <summary>Read the roster, dropping what this build doesn't know (traits since removed, bad colours). Missing or broken: empty.</summary>
    public static CharacterRoster Load(string path, DefDatabase defs)
    {
        var roster = new CharacterRoster { Path = path };
        try
        {
            if (File.Exists(path) && JsonSerializer.Deserialize<List<SavedCharacter>>(File.ReadAllText(path), Json) is { } list)
                roster.Characters = list.Select(c => Clean(c, defs)).OrderByDescending(c => c.LastUsed).Take(Max).ToList();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.Engine.Warning($"characters: couldn't read {path}: {e.Message}");
        }
        return roster;
    }

    private static SavedCharacter Clean(SavedCharacter c, DefDatabase defs)
    {
        var known = defs.AllTraits.Select(t => t.Name).ToHashSet();
        var look = c.Look;
        look.HairColor &= 0xFFFFFF;
        look.EyeColor &= 0xFFFFFF;
        look.SkinColor &= 0xFFFFFF;
        if (!Enum.IsDefined(look.Body))
            look.Body = BodyType.Male;
        if (!Enum.IsDefined(look.Hair))
            look.Hair = HairStyle.Short;
        if (!Enum.IsDefined(look.Beard))
            look.Beard = BeardStyle.None;
        if (!Enum.IsDefined(look.Eyes))
            look.Eyes = EyeStyle.Round;
        if (!Enum.IsDefined(look.Stance))
            look.Stance = Stance.Relaxed;
        return c with
        {
            Name = string.IsNullOrWhiteSpace(c.Name) ? "Arin" : c.Name.Trim(),
            Look = look,
            Traits = (c.Traits ?? []).Where(known.Contains).Distinct().Take(Sim.Systems.TraitRules.MaxTraits).ToList(),
            Clothes = (c.Clothes ?? []).Where(p => Sim.Systems.Clothing.Parse(defs, p) != null).Take(Sim.Systems.Clothing.Slots.Length).ToList(),
        };
    }

    public void Save()
    {
        if (Path == null)
            return;
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            File.WriteAllText(Path, JsonSerializer.Serialize(Characters, Json));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Engine.Warning($"characters: couldn't write {Path}: {e.Message}");
        }
    }

    /// <summary>Add or replace (by id) a character, as the most recently used, and save. The oldest goes past <see cref="Max"/>.</summary>
    public SavedCharacter Put(SavedCharacter c)
    {
        c = c with { LastUsed = DateTime.UtcNow };
        Characters.RemoveAll(x => x.Id == c.Id);
        Characters.Insert(0, c);
        if (Characters.Count > Max)
            Characters.RemoveRange(Max, Characters.Count - Max);
        Save();
        return c;
    }

    public void Remove(Guid id)
    {
        if (Characters.RemoveAll(c => c.Id == id) > 0)
            Save();
    }

    /// <summary>The one picked last (the creators start from it), if any.</summary>
    public SavedCharacter? Last => Characters.Count > 0 ? Characters[0] : null;

    public SavedCharacter? Find(string name) =>
        Characters.FirstOrDefault(c => string.Equals(c.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
}

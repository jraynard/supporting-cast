using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Ecs;

namespace HeroesNotIncluded.Sim.Systems;

/// <summary>
/// Clothes: the Tunic, Legs, Feet and Cloak apparel slots, dyed by their material (the cloth's
/// dye: woad, madder, orchil...). Villagers, settlers and players are dressed when they get their
/// starting pack, raiders by their kind's outfit, story people by their scenario. Every choice a
/// villager's clothes need comes from a stream of their own, so dressing never shifts
/// <c>sim.Rand</c>.
/// </summary>
public static class Clothing
{
    /// <summary>
    /// The slots clothes go in, inside out: a tunic, a garment over it in the Body slot (a vest:
    /// <see cref="ApparelProps.Garment"/>; armor otherwise), legs, feet, a cloak, and at the neck a
    /// scarf (a garment; jewelry otherwise).
    /// </summary>
    public static readonly ApparelSlot[] Slots = [ApparelSlot.Tunic, ApparelSlot.Body, ApparelSlot.Legs, ApparelSlot.Feet, ApparelSlot.Cloak, ApparelSlot.Neck, ApparelSlot.Head];

    public static bool IsClothes(ApparelSlot slot) => slot is ApparelSlot.Tunic or ApparelSlot.Legs or ApparelSlot.Feet or ApparelSlot.Cloak;

    /// <summary>Clothes: anything in a clothes slot, and garments worn in the Body slot.</summary>
    public static bool IsClothes(ThingDef def) => def.Apparel is { } a && (IsClothes(a.Slot) || a.Garment);

    /// <summary>The clothes item worn in a slot, or Entity.Null (armor in the Body slot isn't clothes).</summary>
    public static Entity In(Simulation sim, Entity e, ApparelSlot slot) =>
        sim.World.TryGet<Apparel>(e, out var worn) && sim.World.IsAlive(worn[slot]) && IsClothes(sim.World.Get<Thing>(worn[slot]).Def)
            ? worn[slot] : Entity.Null;

    /// <summary>Whether a creature has any clothes on.</summary>
    public static bool Dressed(Simulation sim, Entity e)
    {
        foreach (var slot in Slots)
            if (!In(sim, e, slot).IsNull)
                return true;
        return false;
    }

    /// <summary>Put on an outfit's pieces: each in its given dye, else one rolled from its list (or the first without a roll).</summary>
    public static void Dress(Simulation sim, Entity e, OutfitDef outfit, SimRandom? rand = null)
    {
        foreach (var piece in outfit.Pieces)
        {
            var mats = piece.MaterialDefs;
            var mat = mats.Count == 0 ? null : piece.Material != null || rand == null ? mats[0] : mats[rand.NextInt(mats.Count)];
            Put(sim, e, piece.ThingDef, mat);
        }
    }

    /// <summary>
    /// Put on pieces written as <c>"Def"</c> or <c>"Def:Material"</c> (the creator's choices, carried
    /// by commands and saved characters). Anything unknown or not clothes is skipped: it may come
    /// from another player's game.
    /// </summary>
    public static void Dress(Simulation sim, Entity e, IEnumerable<string> pieces)
    {
        foreach (var text in pieces.Take(Slots.Length))
            if (Parse(sim.Defs, text) is var (def, mat))
                Put(sim, e, def, mat);
    }

    /// <summary>The player's character: the clothes picked in the creator, or a villager's if none were.</summary>
    public static void DressHero(Simulation sim, Entity e, IReadOnlyList<string>? clothes)
    {
        if (clothes is { Count: > 0 })
        {
            Undress(sim, e);
            Dress(sim, e, clothes);
        }
        DressVillager(sim, e);
    }

    /// <summary>Take off and destroy someone's clothes (an existing villager taking on the player's look).</summary>
    public static void Undress(Simulation sim, Entity e)
    {
        foreach (var slot in Slots)
            if (In(sim, e, slot) is { IsNull: false } item)
            {
                sim.World.Destroy(item);
                sim.World.Get<Apparel>(e)[slot] = Entity.Null;
            }
    }

    /// <summary>A piece from <c>"Def:Material"</c>, or null if it isn't clothes we know.</summary>
    public static (ThingDef Def, MaterialDef? Material)? Parse(DefDatabase defs, string text)
    {
        var colon = text.IndexOf(':');
        var name = colon < 0 ? text : text[..colon];
        if (!defs.TryThing(name, out var def) || !IsClothes(def))
            return null;
        MaterialDef? mat = null;
        if (colon >= 0 && defs.TryMaterial(text[(colon + 1)..], out var m) && m.Fits(def))
            mat = m;
        return (def, mat);
    }

    public static string Write(ThingDef def, MaterialDef? material) => material == null ? def.Name : $"{def.Name}:{material.Name}";

    /// <summary>What a creature has on, as <see cref="Write"/> strings (for travel and saved characters).</summary>
    public static List<string> Describe(Simulation sim, Entity e)
    {
        var list = new List<string>();
        foreach (var slot in Slots)
            if (In(sim, e, slot) is { IsNull: false } item)
            {
                var t = sim.World.Get<Thing>(item);
                list.Add(Write(t.Def, t.Material));
            }
        return list;
    }

    private static void Put(Simulation sim, Entity e, ThingDef def, MaterialDef? material)
    {
        var item = sim.CreateItem(def, 1, material);
        sim.WearItem(e, item);
    }

    /// <summary>
    /// Dress someone with nothing on in a villager outfit that suits their body, rolled from their
    /// own stream. Used by <see cref="Simulation.GiveStartingPack"/>.
    /// </summary>
    public static void DressVillager(Simulation sim, Entity e)
    {
        if (Dressed(sim, e) || sim.World.Get<Creature>(e).Def.Race != Race.Human)
            return;
        var rand = StreamFor(sim, e);
        var body = sim.World.TryGet<Appearance>(e, out var look) ? look.Body : DerivedBody(LookId(sim, e));
        var choices = sim.Defs.AllOutfits.Where(o => o.Villager && Suits(o, body)).ToList();
        if (choices.Count > 0)
            Dress(sim, e, choices[rand.NextInt(choices.Count)], rand);
    }

    /// <summary>A creature's own stream for its clothes, so dressing it never shifts another stream.</summary>
    public static SimRandom StreamFor(Simulation sim, Entity e) =>
        new(sim.Seed ^ ((ulong)(LookId(sim, e) + 1) * 0x9E3779B97F4A7C15UL) ^ 0xC107Eu);

    /// <summary>The id a derived look is drawn from: their own index, or the one they had before (<see cref="LookedAs"/>).</summary>
    public static int LookId(Simulation sim, Entity e) => sim.World.TryGet<LookedAs>(e, out var l) ? l.Id : e.Index;

    public static bool Suits(OutfitDef outfit, BodyType body) =>
        string.IsNullOrEmpty(outfit.Body) || string.Equals(outfit.Body, body.ToString(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The body of someone without an <see cref="Appearance"/>: the same hash the renderer's derived
    /// look starts with (<c>HumanLook.Derived</c>), so their clothes fit the body they're drawn with.
    /// </summary>
    public static BodyType DerivedBody(int id)
    {
        var h = (uint)id * 2654435761u;
        h ^= h >> 15;
        h *= 2246822519u;
        h ^= h >> 13;
        return (BodyType)(h % 2);
    }
}

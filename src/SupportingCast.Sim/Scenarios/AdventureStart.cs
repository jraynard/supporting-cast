using SupportingCast.Sim.Components;
using SupportingCast.Sim.Maps;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Scenarios;

/// <summary>Who the player made in Adventure's creators: their character and (for the new starts) their dragon.</summary>
public sealed record Hero(
    string? Name,
    Appearance? Look,
    IReadOnlyList<string>? Traits,
    string? DragonName = null,
    DragonLook? DragonLook = null,
    IReadOnlyList<string>? Clothes = null,
    string? DragonBreed = null
)
{
    /// <summary>The chosen breed's def (the default breed if none, or an unknown name).</summary>
    public Defs.CreatureDef Breed(Defs.DefDatabase defs) => Systems.Dragons.BreedOrDefault(defs, DragonBreed);
}

/// <summary>
/// Sets up the Adventure start the player picked (the Ashford prologue is <see cref="Prologue"/>).
/// A Pact opens in the enchanted forest (<see cref="EnchantedForest"/>); its flight lands at the
/// tower's site, which co-op hosts start at directly (<see cref="PactSite"/>). Escape opens in the
/// captors' camp (<see cref="EscapeCamp"/>); its flight lands in the wild (<see cref="Refuge"/>
/// without the camp).
/// </summary>
public static class AdventureStart
{
    /// <summary>
    /// Generate the map and set the start down on it. Returns the avatar. <paramref name="difficulty"/>
    /// is the storyteller's def name: Escape turns it on now; Pact keeps it pending until the
    /// tower stands (the sorcerer's protection).
    /// </summary>
    public static Entity Setup(Simulation sim, StartKind kind, Hero hero, string? difficulty = null) => kind switch
    {
        StartKind.Pact => EnchantedForest.Setup(sim, hero, difficulty),
        StartKind.Escape => EscapeCamp.Setup(sim, hero, difficulty),
        _ => throw new ArgumentException($"{kind} isn't a dragon start", nameof(kind)),
    };

    /// <summary>A storyteller difficulty, or null for none ("Off" and null both mean no raids).</summary>
    public static string? DifficultyOrNull(string? difficulty) =>
        difficulty == null || difficulty.Equals("Off", StringComparison.OrdinalIgnoreCase) ? null : difficulty;

    /// <summary>
    /// The player's character (from the creator) and their bonded adult dragon, set down near
    /// <paramref name="near"/> on an already generated map.
    /// </summary>
    public static (Entity Avatar, Entity Dragon) SpawnHero(Simulation sim, Hero hero, int3 near)
    {
        var w = sim.World;
        var cell = sim.SpiralFrom(near + new int3(0, 2, 0), 10).FirstOrDefault(c => sim.Map.Passable(c), near);
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), string.IsNullOrWhiteSpace(hero.Name) ? "Arin" : hero.Name.Trim(), cell, ageDays: 24 * 365f);
        Array.Fill(w.Get<Needs>(me).Values, 0.75f);
        w.Add(me, new Avatar());
        sim.GiveStartingPack(me);
        if (hero.Look is { } look)
            w.Add(me, look);
        if (hero.Traits != null)
            Systems.TraitRules.Set(sim, me, hero.Traits);
        var dragon = Companions.SpawnBondedDragon(sim, me, hero.DragonName, hero.DragonLook, cell + new int3(-2, 1, 0), breed: hero.Breed(sim.Defs));
        return (me, dragon); // dressed by the caller once the start is laid out (Clothing.DressHero)
    }

    /// <summary>
    /// The Pact's new home without the forest first (co-op hosts, and <c>--adventure pact --stage tower</c>):
    /// a wild valley with the ley stone raised and the tower owed.
    /// </summary>
    public static Entity PactSite(Simulation sim, Hero hero, string? difficulty = null)
    {
        var center = MapGenerator.Generate(sim, new MapGenerator.Options { Villagers = 0, SpawnStartingKit = false });
        sim.RebuildDerivedIfNeeded();
        SliceDemo.ApplyVillageDefaults(sim, center); // a starter stockpile (and no keeper yet: nobody's here)
        sim.Story.Start = StartKind.Pact;
        sim.Story.PendingDifficulty = DifficultyOrNull(difficulty);
        var (me, dragon) = SpawnHero(sim, hero, center);
        TowerPact.Begin(sim, center + new int3(6, -4, 0));
        Clothing.DressHero(sim, me, hero.Clothes); // last, so it shifts no other entity
        sim.RebuildDerivedIfNeeded();
        var w = sim.World;
        sim.Messages.Add(MessageKind.Info,
            $"{w.Get<Creature>(me).Name} and {w.Get<Creature>(dragon).Name} come down where the sorcerer showed them, by his ley stone. "
            + "His tower must rise here; until it does, his protection keeps raiders away.",
            w.Get<Position>(me).Cell);
        return me;
    }

    /// <summary>Escape's story without the camp (<c>--adventure escape --stage site</c>): free in a wild valley, raids on.</summary>
    public static Entity Refuge(Simulation sim, Hero hero, string? difficulty)
    {
        var center = MapGenerator.Generate(sim, new MapGenerator.Options { Villagers = 0, SpawnStartingKit = false });
        sim.RebuildDerivedIfNeeded();
        SliceDemo.ApplyVillageDefaults(sim, center);
        sim.Story.Start = StartKind.Escape;
        sim.Story.PendingDifficulty = null;
        if (DifficultyOrNull(difficulty) is { } d)
            sim.Storyteller.SetDifficulty(sim, sim.Defs.Difficulty(d));
        var (me, dragon) = SpawnHero(sim, hero, center);
        Clothing.DressHero(sim, me, hero.Clothes);
        sim.RebuildDerivedIfNeeded();
        var w = sim.World;
        sim.Messages.Add(MessageKind.Info,
            $"{w.Get<Creature>(me).Name} and {w.Get<Creature>(dragon).Name} are free, and far from the camp that held them. "
            + "Find shelter, a fire and food before the first night: the captors won't stop looking.",
            w.Get<Position>(me).Cell);
        return me;
    }
}

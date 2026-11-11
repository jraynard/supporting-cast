using SupportingCast.Sim.Components;
using SupportingCast.Sim.Maps;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Scenarios;

/// <summary>
/// The Pact's opening: you and your dragon in the enchanted forest, gathering moonpetal to cure
/// your village. Once you hold enough, the sorcerer whose blooms they are walks out of his stone
/// circle to meet you; his bargain (the village's cure for a tower built far away) ends the
/// opening at <see cref="QuestStage.TakeWing"/>, and the flight lands at the tower's site
/// (<see cref="TowerPact.Begin"/> via <see cref="Travel.Arrive"/>). Placeholder story: the words
/// are in data/defs/story.json.
/// </summary>
public static class EnchantedForest
{
    public const string SorcererName = "Morvane";
    public const string BargainDialogue = "SorcererBargain";

    /// <summary>Moonpetal blooms you need in hand or pack before the sorcerer comes for them.</summary>
    public const int HerbsNeeded = 6;

    /// <summary>Patches of moonpetal around the glade, and blooms in each (more than you need: some are a walk away).</summary>
    public const int Patches = 5, BloomsPerPatch = 3;

    /// <summary>How far from the glade the patches and the stone circle lie.</summary>
    public const int PatchDistance = 20, CircleDistance = 24;

    /// <summary>Generate the forest and set the start down in it. Returns the avatar.</summary>
    public static Entity Setup(Simulation sim, Hero hero, string? difficulty)
    {
        var center = MapGenerator.Generate(sim, new MapGenerator.Options { Villagers = 0, SpawnStartingKit = false, Forest = true });
        sim.RebuildDerivedIfNeeded();
        sim.Story.Start = StartKind.Pact;
        sim.Story.PendingDifficulty = AdventureStart.DifficultyOrNull(difficulty);

        var (me, dragon) = AdventureStart.SpawnHero(sim, hero, center);
        sim.World.Add(me, new Quest { Stage = QuestStage.GatherHerbs });

        // Moonpetal grows in a few patches around the glade, deeper among the trees, each in a little
        // clearing of its own (under the canopy the blooms were near impossible to spot).
        var flower = sim.Defs.Thing("MoonpetalFlower");
        for (var k = 0; k < Patches; k++)
        {
            // Starting due south, so none falls near the stone circle to the north (it clears around itself).
            var angle = MathF.PI / 2 + k * MathF.Tau / Patches;
            var at = center + new int3((int)MathF.Round(DetMath.Cos(angle) * PatchDistance), (int)MathF.Round(DetMath.Sin(angle) * PatchDistance), 0);
            var spot = sim.SpiralFrom(at, 8).FirstOrDefault(c => sim.Map.Passable(c), at);
            foreach (var c in sim.SpiralFrom(spot, 2))
                if (c.Z == spot.Z && sim.Map.Structure[sim.Map.Idx(c)] is { IsNull: false } s && sim.World.Get<Thing>(s).Def.Category is Defs.ThingCategory.Plant or Defs.ThingCategory.Resource)
                    sim.DestroyThing(s);
            foreach (var cell in sim.SpiralFrom(spot, 3).Where(c => sim.Map.Passable(c) && sim.CanPlaceStructure(c, flower)).Take(BloomsPerPatch))
                sim.SpawnStructure(flower, cell);
        }

        // The sorcerer's stone circle, north of the glade, with him in the middle.
        var circle = sim.SpiralFrom(center + new int3(0, -CircleDistance, 0), 10).FirstOrDefault(c => sim.Map.Passable(c), center);
        foreach (var c in sim.SpiralFrom(circle, 4))
            if (sim.Map.Structure[sim.Map.Idx(c)] is { IsNull: false } s)
                sim.DestroyThing(s);
        var stone = sim.Defs.Thing("StandingStone");
        for (var k = 0; k < 6; k++)
        {
            var angle = k * MathF.PI / 3;
            var at = circle + new int3((int)MathF.Round(DetMath.Cos(angle) * 3), (int)MathF.Round(DetMath.Sin(angle) * 3), 0);
            if (sim.SpiralFrom(at, 1).FirstOrDefault(c => sim.CanPlaceStructure(c, stone) && sim.Map.Item[sim.Map.Idx(c)].IsNull, at) is var spot
                && sim.CanPlaceStructure(spot, stone))
                sim.SpawnStructure(stone, spot);
        }
        var sorcerer = sim.SpawnCreature(sim.Defs.Creature("Human"), SorcererName, circle, Faction.Stranger, ageDays: 70 * 365f);
        sim.World.Add(sorcerer, new StoryNpc { Role = StoryRole.Sorcerer, Home = circle });
        sim.World.Add(sorcerer, new Appearance
        {
            Body = BodyType.Male, Hair = HairStyle.Long, HairColor = 0x9A9A9A, EyeColor = 0x7A8A9A, SkinColor = 0xD2A27A, Beard = BeardStyle.Braided,
        });
        // Clothes last, so they don't shift the entities (and the rolls seeded by them) above.
        Clothing.DressHero(sim, me, hero.Clothes);
        if (sim.Defs.AllOutfits.Find(o => o.Name == "Sorcerer") is { } robes)
            Clothing.Dress(sim, sorcerer, robes);
        sim.RebuildDerivedIfNeeded();

        sim.Messages.Add(MessageKind.Info,
            $"The wasting sickness has come to your village, and only moonpetal cures it. {sim.World.Get<Creature>(me).Name} and "
            + $"{sim.World.Get<Creature>(dragon).Name} have come to the enchanted forest to find some. Pale blooms glow among the trees.",
            sim.World.Get<Position>(me).Cell);
        return me;
    }

    /// <summary>Moonpetal blooms the avatar holds (in hand and in their pack).</summary>
    public static int HerbsHeld(Simulation sim, Entity avatar)
    {
        var w = sim.World;
        var n = 0;
        if (w.Get<Carrying>(avatar).Item is { IsNull: false } held && IsHerb(sim, held))
            n += w.TryGet<Stack>(held, out var s) ? s.Count : 1;
        if (sim.PackOf(avatar) is { IsNull: false } pack)
            foreach (var item in sim.StoredIn(pack))
                if (IsHerb(sim, item))
                    n += w.TryGet<Stack>(item, out var s) ? s.Count : 1;
        return n;
    }

    private static bool IsHerb(Simulation sim, Entity item) => sim.World.Get<Thing>(item).Def.Name == "Moonpetal";

    /// <summary>The sorcerer, if the avatar is to speak with him and he's at this cell.</summary>
    public static Entity SorcererAt(Simulation sim, Entity avatar, int3 cell)
    {
        var w = sim.World;
        if (!w.TryGet<Quest>(avatar, out var q) || q.Stage != QuestStage.Confronted)
            return Entity.Null;
        foreach (var e in w.Query<StoryNpc>())
            if (w.Get<StoryNpc>(e).Role == StoryRole.Sorcerer && !w.Get<Creature>(e).Dead && w.Get<Position>(e).Cell == cell)
                return e;
        return Entity.Null;
    }

    /// <summary>The avatar the sorcerer is coming for (they hold his moonpetal), if any: for his brain.</summary>
    public static Entity Confronting(Simulation sim)
    {
        var w = sim.World;
        foreach (var e in w.Query<Quest>())
            if (w.Get<Quest>(e).Stage == QuestStage.Confronted && w.Has<Avatar>(e) && !w.Get<Creature>(e).Dead)
                return e;
        return Entity.Null;
    }

    /// <summary>
    /// The bargain is struck: he takes his moonpetal (the village gets its cure from him instead),
    /// and the two of you are free to fly to where the tower must stand.
    /// </summary>
    public static void StrikeBargain(Simulation sim, Entity avatar, Entity sorcerer)
    {
        var w = sim.World;
        ref var carrying = ref w.Get<Carrying>(avatar);
        if (carrying.Item is { IsNull: false } held && IsHerb(sim, held))
        {
            w.Destroy(held);
            carrying.Item = Entity.Null;
        }
        if (sim.PackOf(avatar) is { IsNull: false } pack)
            foreach (var item in sim.StoredIn(pack).ToList())
                if (IsHerb(sim, item))
                    w.Destroy(sim.TakeFromStack(item, w.TryGet<Stack>(item, out var s) ? s.Count : 1));
        w.Get<Quest>(avatar).Stage = QuestStage.TakeWing;
        var dragon = Companions.DragonOf(sim, avatar);
        sim.Messages.Add(MessageKind.Good,
            $"{w.Get<Creature>(sorcerer).Name} takes the moonpetal and presses a cold stone into your palm: it pulls north. "
            + $"Your village will have its cure. Now mount {(dragon.IsNull ? "your dragon" : w.Get<Creature>(dragon).Name)} and fly where it points.",
            w.Get<Position>(avatar).Cell);
    }

    /// <summary>The forest's beats: the sorcerer comes once you hold enough of his blooms (called each game second).</summary>
    internal static void Update(Simulation sim)
    {
        var w = sim.World;
        foreach (var e in w.Query<Quest>())
        {
            ref var q = ref w.Get<Quest>(e);
            if (q.Stage != QuestStage.GatherHerbs || HerbsHeld(sim, e) < HerbsNeeded)
                continue;
            q.Stage = QuestStage.Confronted;
            sim.Messages.Add(MessageKind.Danger,
                "The forest goes quiet. A tall figure in grey is walking toward you from the north, and the moonpetal in your pack has started to glow.",
                w.Get<Position>(e).Cell);
        }
        // Story characters don't go hungry or tired: they're here for the story.
        foreach (var e in w.Query<StoryNpc>())
            if (w.TryGet<Needs>(e, out var needs))
                Array.Fill(needs.Values, 1f);
    }

    /// <summary>What the forest asks of you right now, for the HUD (null outside it).</summary>
    public static string? Objective(Simulation sim, Entity avatar) =>
        sim.World.TryGet<Quest>(avatar, out var q) ? q.Stage switch
        {
            QuestStage.GatherHerbs => $"Gather moonpetal for your village: {Math.Min(HerbsHeld(sim, avatar), HerbsNeeded)}/{HerbsNeeded} "
                + "(pale blue flowers deep among the trees; F to pick, then pick up the bloom).",
            QuestStage.Confronted => $"A stranger is coming for you. Speak with him (F beside him).",
            _ => null,
        } : null;
}

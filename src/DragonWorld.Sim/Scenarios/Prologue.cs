using DragonWorld.Sim.Components;
using DragonWorld.Sim.Defs;
using DragonWorld.Sim.Maps;
using DragonWorld.Sim.Systems;

namespace DragonWorld.Sim.Scenarios;

/// <summary>
/// Adventure mode's opening: Ashford, a village that isn't yours. You're its youngest hand. Orla,
/// its old dragon keeper, is dying in the hall, and she sends for you: the village can't protect
/// the egg she kept, so she entrusts it to you with her packed wagon and asks you to take it far
/// away. Leaving (hitching the wagon with the egg in hand) moves the party to a new map
/// (<see cref="Travel"/>). One day the village will be a place on the overworld you can return to.
/// </summary>
public static class Prologue
{
    public const string VillageName = "Ashford";
    public const string KeeperName = "Orla";

    private static readonly string[] AshfordNames = ["Hob", "Elsa", "Tomas", "Greta"];

    /// <summary>What Orla tells you when you speak with her (a dialogue in data/defs/story.json).</summary>
    public const string HandoverDialogue = "OrlaHandover";

    /// <summary>Hours after the handover before Orla passes away.</summary>
    public const int KeeperHours = 2;

    /// <summary>Lay out the village on a freshly generated map and place the avatar in it. Returns the avatar.</summary>
    public static Entity Setup(Simulation sim, string? name = null, Appearance? look = null, string? difficulty = null, IReadOnlyList<string>? traits = null,
        IReadOnlyList<string>? clothes = null, Defs.CreatureDef? eggBreed = null, DragonElement eggElement = DragonElement.Fire)
    {
        var center = MapGenerator.Generate(sim, new MapGenerator.Options { Villagers = 0, SpawnStartingKit = false });
        sim.RebuildDerivedIfNeeded();
        sim.Story.Start = StartKind.EggBearer;
        var w = sim.World;
        var defs = sim.Defs;
        var human = defs.Creature("Human");

        // The hall: an 11 x 9 wooden room, its door to the east, a fire in the middle.
        var min = center + new int3(-5, -4, 0);
        var max = center + new int3(5, 4, 0);
        var door = new int3(max.X, center.Y, center.Z);
        foreach (var c in sim.SpiralFrom(center, 12))
            if (c.Z == center.Z && Math.Abs(c.X - center.X) <= 9 && Math.Abs(c.Y - center.Y) <= 9 && !sim.Map.Structure[sim.Map.Idx(c)].IsNull)
                sim.DestroyThing(sim.Map.Structure[sim.Map.Idx(c)]); // clear trees and rocks around the village
        for (var x = min.X; x <= max.X; x++)
        for (var y = min.Y; y <= max.Y; y++)
        {
            if (x != min.X && x != max.X && y != min.Y && y != max.Y)
                continue;
            var c = new int3(x, y, center.Z);
            sim.SpawnStructure(defs.Thing(c == door ? "Door" : "WoodWall"), c);
        }
        var fire = sim.SpawnStructure(defs.Thing("Campfire"), center);
        w.Get<Fueled>(fire).Fuel = defs.Thing("Campfire").Building!.Fuel!.Capacity;

        // Beds along the west wall for the villagers and you; Orla's in the warm corner by the fire.
        var bedDef = defs.Thing("Bed");
        var beds = new List<Entity>();
        // Headboards to the wall, feet into the hall (facing 3: the bed's front, its foot, points east).
        for (var y = min.Y + 1; y < max.Y; y += 2)
            beds.Add(sim.SpawnStructure(bedDef, new int3(min.X + 1, y, center.Z), facing: 3));
        var keeperBed = sim.SpawnStructure(bedDef, new int3(center.X + 2, min.Y + 1, center.Z));
        var eggCell = new int3(center.X + 1, center.Y - 1, center.Z);
        sim.SpawnItem(defs.Thing("DragonEgg"), 1, eggCell);
        Systems.Dragons.ChooseEgg(sim, sim.Map.Item[sim.Map.Idx(eggCell)], eggBreed, eggElement); // the egg picked in the creator (Ui/DragonCreator's egg step)

        // The village store, outside the hall to the south.
        for (var x = center.X - 3; x <= center.X + 1; x++)
        for (var y = max.Y + 2; y <= max.Y + 3; y++)
            sim.Map.SetZone(sim.Map.Idx(x, y, center.Z), 1);
        sim.SpawnItem(defs.Thing("Berries"), 90, new int3(center.X - 2, max.Y + 2, center.Z));
        sim.SpawnItem(defs.Thing("Wood"), 40, new int3(center.X, max.Y + 3, center.Z), defs.Material("Oak"));

        // Orla's wagon, packed for the road, by the east door.
        var wagonCell = sim.SpiralFrom(door + new int3(3, 1, 0), 4).First(c => sim.Map.Passable(c) && sim.Map.Item[sim.Map.Idx(c)].IsNull);
        var wagon = sim.SpawnStructure(defs.Thing("Wagon"), wagonCell);
        void Pack(string def, int count, string? material = null)
        {
            var item = sim.CreateItem(defs.Thing(def), count, material == null ? null : defs.Material(material));
            if (!sim.Store(wagon, item).IsNull)
                sim.PlaceItemNear(item, wagonCell);
        }
        Pack("Berries", 120);
        Pack("Meat", 60);
        Pack("Wood", 50, "Pine");
        Pack("Wood", 50, "Pine");
        Pack("Bow", 1);
        Pack("LeatherJerkin", 1);

        // The villagers, who live their own lives, and Orla.
        for (var i = 0; i < AshfordNames.Length; i++)
        {
            var v = sim.SpawnCreature(human, AshfordNames[i], new int3(center.X - 2 + i, center.Y + 2, center.Z), Faction.Ashford, ageDays: (24 + i * 9) * 365f);
            w.Add(v, new BedOwner { Bed = beds[i] });
        }
        var keeper = sim.SpawnCreature(human, KeeperName, w.Get<Thing>(keeperBed).Cell, Faction.Ashford, ageDays: 79 * 365f);
        w.Add(keeper, new BedOwner { Bed = keeperBed });
        w.Add(keeper, new StoryKeeper());
        w.Add(keeper, new Appearance
        {
            Body = BodyType.Female, Hair = HairStyle.Long, HairColor = 0xD8D8D0, EyeColor = 0x5A7A8A, SkinColor = 0xE0B48E,
        });


        // You: the youngest hand, with the last bed.
        var me = sim.SpawnCreature(human, string.IsNullOrWhiteSpace(name) ? "Arin" : name.Trim(), new int3(min.X + 2, max.Y - 1, center.Z), ageDays: 19 * 365f);
        Array.Fill(w.Get<Needs>(me).Values, 0.9f);
        w.Add(me, new Avatar());
        w.Add(me, new BedOwner { Bed = beds[^1] });
        w.Add(me, new Quest { Stage = QuestStage.MeetKeeper, Difficulty = difficulty });
        sim.GiveStartingPack(me);
        if (look is { } a)
            w.Add(me, a);
        if (traits != null)
            Systems.TraitRules.Set(sim, me, traits);
        // Clothes last, so they don't shift the entities (and the rolls seeded by them) above.
        foreach (var e in w.Query<Creature>())
            if (w.Get<Creature>(e) is { Faction: Faction.Ashford, Def.Race: Race.Human } && e != keeper)
                Systems.Clothing.DressVillager(sim, e);
        if (defs.AllOutfits.Find(o => o.Name == "Elder") is { } elder)
            Systems.Clothing.Dress(sim, keeper, elder);
        Systems.Clothing.DressHero(sim, me, clothes);

        sim.RebuildDerivedIfNeeded();
        sim.Messages.Add(MessageKind.Info,
            $"You are {w.Get<Creature>(me).Name}, the youngest hand in {VillageName}: you fetch, carry and mend, and nobody asks your opinion. "
            + $"This morning old {KeeperName}, the village's dragon keeper, sent for you. She's abed in the hall.",
            w.Get<Position>(me).Cell);
        return me;
    }

    /// <summary>The keeper, if the avatar is in the prologue and can speak with them at this cell.</summary>
    public static Entity KeeperAt(Simulation sim, Entity avatar, int3 cell)
    {
        var w = sim.World;
        if (!w.TryGet<Quest>(avatar, out var q) || q.Stage != QuestStage.MeetKeeper)
            return Entity.Null;
        foreach (var e in w.Query<StoryKeeper>())
            if (!w.Get<Creature>(e).Dead && w.Get<Position>(e).Cell == cell)
                return e;
        return Entity.Null;
    }

    /// <summary>The dialogue a story character speaks when the avatar talks to them (null if none).</summary>
    public static Defs.DialogueDef? DialogueOf(Simulation sim, Entity speaker)
    {
        var w = sim.World;
        if (!w.IsAlive(speaker))
            return null;
        if (w.Has<StoryKeeper>(speaker))
            return sim.Defs.Dialogue(HandoverDialogue);
        if (w.TryGet<StoryNpc>(speaker, out var npc) && npc.Role == StoryRole.Sorcerer)
            return sim.Defs.Dialogue(EnchantedForest.BargainDialogue);
        return null;
    }

    /// <summary>Orla hands over the egg (into your arms, if they're free) and her wagon.</summary>
    public static void Entrust(Simulation sim, Entity avatar, Entity keeper)
    {
        var w = sim.World;
        ref var quest = ref w.Get<Quest>(avatar);
        quest.Stage = QuestStage.Entrusted;
        quest.KeeperPassesTick = sim.Clock.Tick + SimClock.HoursToTicks(KeeperHours);
        if (!w.Has<Keeper>(avatar))
            w.Add(avatar, new Keeper { Dragon = Entity.Null });

        ref var carrying = ref w.Get<Carrying>(avatar);
        foreach (var egg in w.Query<Egg>())
        {
            if (carrying.Item == egg)
                break;
            if (carrying.HasItem)
            {
                sim.Messages.Add(MessageKind.Info, "Your hands are full: put down what you're holding and take the egg from beside the fire.", w.Get<Position>(avatar).Cell);
                break;
            }
            if (w.TryGet<Stored>(egg, out _))
                carrying.Item = sim.TakeFromStack(egg, 1);
            else
            {
                sim.DespawnThing(egg);
                carrying.Item = egg;
            }
            break;
        }
        sim.Messages.Add(MessageKind.Good,
            $"{KeeperName} has given you the dragon egg and her wagon. Hitch the wagon (F near the wagon, egg in hand) and leave {VillageName} behind.",
            w.Get<Position>(avatar).Cell);
    }

    /// <summary>How close (cells, either way, a level up or down) the avatar must be to hitch the wagon.</summary>
    public const int HitchReach = 2;

    /// <summary>
    /// A wagon the avatar could set out with from where they stand (within <see cref="HitchReach"/>),
    /// or Entity.Null: no need to find the one cell that has it under the cursor.
    /// </summary>
    public static Entity WagonToHitch(Simulation sim, Entity avatar)
    {
        var w = sim.World;
        if (!w.Has<Quest>(avatar))
            return Entity.Null; // only the prologue sets out
        var here = w.Get<Position>(avatar).Cell;
        foreach (var e in w.Query<Thing>())
        {
            var t = w.Get<Thing>(e);
            if (t.Spawned && Math.Abs(t.Cell.Z - here.Z) <= 1 && Math.Max(Math.Abs(t.Cell.X - here.X), Math.Abs(t.Cell.Y - here.Y)) <= HitchReach
                && CanDepart(sim, avatar, e))
                return e;
        }
        return Entity.Null;
    }

    /// <summary>Is the avatar ready to set out from this wagon: the egg handed over and in their arms?</summary>
    public static bool CanDepart(Simulation sim, Entity avatar, Entity wagon)
    {
        var w = sim.World;
        if (!w.TryGet<Quest>(avatar, out var q) || q.Stage != QuestStage.Entrusted)
            return false;
        if (wagon.IsNull || w.Get<Thing>(wagon).Def.Building is not { StorageStacks: > 0 })
            return false;
        var held = w.Get<Carrying>(avatar).Item;
        return !held.IsNull && w.Has<Egg>(held);
    }

    /// <summary>What the prologue asks of you right now, for the HUD (null once you've left).</summary>
    public static string? Objective(Simulation sim, Entity avatar)
    {
        if (!sim.World.TryGet<Quest>(avatar, out var q))
            return null;
        if ((EnchantedForest.Objective(sim, avatar) ?? EscapeCamp.Objective(sim, avatar)) is { } opening)
            return opening;
        return q.Stage switch
        {
            QuestStage.MeetKeeper => $"Speak with {KeeperName}: she's abed in the hall, near the fire (F beside her).",
            QuestStage.TakeWing => Companions.DragonOf(sim, avatar) is { IsNull: false } dragon
                ? sim.World.Has<Riding>(avatar)
                    ? "Fly away to a new home (the Fly away button)."
                    : $"Mount {sim.World.Get<Creature>(dragon).Name} (Ride, in your character panel) and fly away."
                : "You need your dragon to leave this place.",
            _ => sim.World.Get<Carrying>(avatar).Item is var held && !held.IsNull && sim.World.Has<Egg>(held)
                ? "Hitch the wagon by the east door and set out (F near the wagon)."
                : "Take the egg in your arms (F), then hitch the wagon by the east door.",
        };
    }
}

/// <summary>The openings' beats: Orla passing away a little while after the handover, the sorcerer coming for his moonpetal.</summary>
public sealed class PrologueSystem : ISimSystem
{
    public string Name => "Prologue";
    public int Interval => 60;

    public void Update(Simulation sim)
    {
        EnchantedForest.Update(sim);
        EscapeCamp.Update(sim);
        var w = sim.World;
        foreach (var e in w.Query<Quest>())
        {
            var q = w.Get<Quest>(e);
            if (q.Stage != QuestStage.Entrusted || sim.Clock.Tick < q.KeeperPassesTick)
                continue;
            foreach (var k in w.Query<StoryKeeper>())
            {
                ref var c = ref w.Get<Creature>(k);
                if (!c.Dead)
                    NeedsSystem.Kill(sim, k, ref c, "passed away in her sleep, by the fire she kept for forty years");
            }
        }
    }
}

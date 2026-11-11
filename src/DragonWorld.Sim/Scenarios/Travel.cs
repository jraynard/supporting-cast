using DragonWorld.Sim.Components;
using DragonWorld.Sim.Defs;
using DragonWorld.Sim.Maps;

namespace DragonWorld.Sim.Scenarios;

/// <summary>
/// One stack of cargo (in the wagon or a pack), or a piece of gear. Defs travel by name so a party
/// survives def changes. <paramref name="Hp"/> is the item's condition, or -1 for as new.
/// </summary>
public readonly record struct Cargo(string Def, int Count, string? Material, float Rot, float Hp = -1);

/// <summary>
/// The people and things leaving one map for another: the avatar (who they are, how they look,
/// what they know), the egg in their arms and the wagon's load. It's plain data on purpose: the
/// overworld will carry parties like this between places, and the place left behind can be saved
/// on its own.
/// </summary>
public sealed record TravelParty(
    string Name,
    Appearance? Look,
    float AgeDays,
    int[] SkillLevels,
    float[] SkillXp,
    float EggProgress,
    float EggHp,
    List<Cargo> Cargo,
    long Tick,
    string[]? TraitNames = null,
    List<Cargo>? Gear = null,
    List<Cargo>? PackContents = null,
    string[]? Recipes = null,
    PartyDragon? Dragon = null,
    PartyHealth? Health = null,
    bool Wagon = true,
    StartKind Start = StartKind.EggBearer,
    int TravelHours = Travel.RoadHours,
    string? Difficulty = null,
    List<Cargo>? Belt = null,
    string? EggBreed = null,
    DragonElement EggElement = DragonElement.Fire
);

/// <summary>A wound as plain data: the part by name, so a party survives body def changes.</summary>
public readonly record struct PartyWound(string Part, DamageType Type, float Damage, float PeakDamage, bool Tended, float Quality);

/// <summary>Wounds, scars and blood: what a creature carries to the next map (infections clear on the way).</summary>
public sealed record PartyHealth(float Hp, float Blood, List<PartyWound> Wounds, List<PartyWound> Scars);

/// <summary>The keeper's dragon, travelling with them (on the wing, or ridden).</summary>
public sealed record PartyDragon(
    string Name,
    DragonLook? Look,
    float AgeDays,
    float Affinity,
    int Flights,
    float[] Needs,
    PartyHealth? Health,
    Dictionary<string, float>? Training = null,
    string? Breed = null,
    DragonElement Element = DragonElement.Fire,
    Cargo? Saddle = null,
    List<Cargo>? Saddlebags = null,
    DragonSex? Sex = null
);

public static class Travel
{
    /// <summary>How long the road from the village takes (the egg is kept warm on the way).</summary>
    public const int RoadHours = 30;

    /// <summary>How long a flight to a new home takes, on dragonback.</summary>
    public const int FlightHours = 12;

    /// <summary>
    /// Pack the avatar, the egg in their arms, their dragon, and a wagon's load (if there's a
    /// wagon; Entity.Null leaves on foot or on the wing) for the road.
    /// </summary>
    public static TravelParty Pack(Simulation sim, Entity avatar, Entity wagon, int travelHours = RoadHours)
    {
        var w = sim.World;
        var c = w.Get<Creature>(avatar);
        var skills = w.Get<Skills>(avatar);
        var egg = w.Get<Carrying>(avatar).Item;
        var hasEgg = !egg.IsNull && w.Has<Egg>(egg);
        Cargo Load(Entity item)
        {
            var t = w.Get<Thing>(item);
            return new Cargo(t.Def.Name, w.TryGet<Stack>(item, out var stack) ? stack.Count : 1, t.Material?.Name,
                w.TryGet<Rot>(item, out var rot) ? rot.Progress : 0f, t.Hp);
        }
        var cargo = new List<Cargo>();
        if (!wagon.IsNull)
            foreach (var item in sim.StoredIn(wagon))
                cargo.Add(Load(item));
        // What they wear and wield, and what's in their pack, goes with them.
        var gear = sim.Worn(avatar).Select(Load).ToList();
        if (w.TryGet<Equipment>(avatar, out var eq) && w.IsAlive(eq.Weapon))
            gear.Add(Load(eq.Weapon));
        var pack = sim.PackOf(avatar);
        var packed = pack.IsNull ? [] : sim.StoredIn(pack).Select(Load).ToList();
        var belt = Systems.Toolbelt.Of(sim, avatar).Select(Load).ToList();
        return new TravelParty(
            c.Name,
            w.TryGet<Appearance>(avatar, out var look) ? look : null,
            c.AgeDays(sim.Clock.Tick),
            (int[])skills.Level.Clone(),
            (float[])skills.Xp.Clone(),
            hasEgg ? w.Get<Egg>(egg).Progress : -1f,
            hasEgg ? w.Get<Thing>(egg).Hp : 0f,
            cargo,
            sim.Clock.Tick,
            w.TryGet<Traits>(avatar, out var traits) ? traits.List.Select(t => t.Name).ToArray() : null,
            gear,
            packed,
            w.TryGet<RecipeBook>(avatar, out var book) ? book.Known.OrderBy(r => r.Index).Select(r => r.Name).ToArray() : null,
            PackDragon(sim, Companions.DragonOf(sim, avatar)),
            PackHealth(sim, avatar),
            !wagon.IsNull,
            sim.Story.Start == StartKind.None ? StartKind.EggBearer : sim.Story.Start,
            travelHours,
            sim.Story.PendingDifficulty,
            belt,
            hasEgg ? w.Get<Egg>(egg).Breed?.Name : null,
            hasEgg ? w.Get<Egg>(egg).Element : DragonElement.Fire
        );
    }

    private static PartyDragon? PackDragon(Simulation sim, Entity dragon)
    {
        if (dragon.IsNull)
            return null;
        var w = sim.World;
        var c = w.Get<Creature>(dragon);
        var d = w.Get<Dragon>(dragon);
        var saddle = Systems.Saddles.Of(sim, dragon); // saddle and saddlebags go with it
        Cargo Load(Entity item)
        {
            var t = w.Get<Thing>(item);
            return new Cargo(t.Def.Name, w.TryGet<Stack>(item, out var stack) ? stack.Count : 1, t.Material?.Name,
                w.TryGet<Rot>(item, out var rot) ? rot.Progress : 0f, t.Hp);
        }
        return new PartyDragon(
            c.Name,
            w.TryGet<DragonLook>(dragon, out var look) ? look : null,
            c.AgeDays(sim.Clock.Tick),
            d.Affinity,
            d.Flights,
            (float[])w.Get<Needs>(dragon).Values.Clone(),
            PackHealth(sim, dragon),
            new Dictionary<string, float>(Systems.Bonding.Peek(sim, dragon).Training), // its tricks go with it
            c.Def.Name,
            d.Element,
            saddle.IsNull ? null : Load(saddle),
            saddle.IsNull ? null : sim.StoredIn(saddle).Select(Load).ToList(),
            d.Sex);
    }

    /// <summary>A creature's wounds as plain data (null if it has no body to hurt).</summary>
    public static PartyHealth? PackHealth(Simulation sim, Entity e)
    {
        var w = sim.World;
        if (!w.TryGet<Health>(e, out var health))
            return null;
        var parts = health.Body.Parts;
        return new PartyHealth(
            w.Get<Creature>(e).Hp,
            health.Blood,
            health.Injuries.Select(i => new PartyWound(parts[i.Part].Name, i.Type, i.Damage, i.PeakDamage, i.Tended, i.Quality)).ToList(),
            health.Scars.Select(s => new PartyWound(parts[s.Part].Name, s.Type, s.Damage, s.Damage, true, 1f)).ToList());
    }

    /// <summary>Set a packed creature's wounds back on it (parts since removed from its body are forgotten).</summary>
    public static void UnpackHealth(Simulation sim, Entity e, PartyHealth? packed)
    {
        var w = sim.World;
        if (packed == null || !w.TryGet<Health>(e, out var health))
            return;
        w.Get<Creature>(e).Hp = packed.Hp;
        health.Blood = packed.Blood;
        health.Injuries.Clear();
        health.Scars.Clear();
        var now = sim.Clock.Tick;
        foreach (var p in packed.Wounds)
            if (health.Body.PartIndex(p.Part) is >= 0 and var part)
                health.Injuries.Add(new Injury
                {
                    Part = part, Type = p.Type, Damage = p.Damage, PeakDamage = p.PeakDamage, Tended = p.Tended, Quality = p.Quality,
                    Tick = now, TendedTick = now, InfectionRolled = true, // the road's behind it: no fresh festering
                });
        foreach (var p in packed.Scars)
            if (health.Body.PartIndex(p.Part) is >= 0 and var part)
                health.Scars.Add(new Scar(part, p.Type, p.Damage));
        health.Changed();
    }

    /// <summary>
    /// Generate a fresh map in <paramref name="sim"/> and set the party down on it: the avatar with
    /// the egg in their arms, the wagon beside them with its load. Returns the avatar.
    /// </summary>
    public static Entity Arrive(Simulation sim, TravelParty party)
    {
        // Arrive at the next morning after the road's hours, so the first thing isn't a freezing night.
        // (Clock time counts from StartHour on day 1, hence the offsets.)
        var offset = SimClock.HoursToTicks(sim.Clock.StartHour);
        var arrival = party.Tick + offset + SimClock.HoursToTicks(party.TravelHours);
        var day = arrival / SimClock.TicksPerDay;
        if (arrival % SimClock.TicksPerDay > SimClock.HoursToTicks(10))
            day++;
        sim.Clock.Tick = day * SimClock.TicksPerDay + SimClock.HoursToTicks(8) - offset;

        var center = MapGenerator.Generate(sim, new MapGenerator.Options { Villagers = 0, SpawnStartingKit = false });
        sim.RebuildDerivedIfNeeded();
        var w = sim.World;
        var defs = sim.Defs;

        sim.Story.Start = party.Start;
        sim.Story.PendingDifficulty = party.Difficulty;
        var me = sim.SpawnCreature(defs.Creature("Human"), party.Name, center + new int3(0, 2, 0), ageDays: party.AgeDays);
        Array.Fill(w.Get<Needs>(me).Values, 0.8f);
        UnpackHealth(sim, me, party.Health);
        var skills = w.Get<Skills>(me);
        Array.Copy(party.SkillLevels, skills.Level, Math.Min(party.SkillLevels.Length, skills.Level.Length));
        Array.Copy(party.SkillXp, skills.Xp, Math.Min(party.SkillXp.Length, skills.Xp.Length));
        w.Add(me, new Avatar());
        if (party.Look is { } look)
            w.Add(me, look);
        if (party.TraitNames != null)
            Systems.TraitRules.Set(sim, me, party.TraitNames);
        foreach (var name in party.Recipes ?? [])
            if (defs.AllRecipes.Find(r => r.Name == name) is { } recipe)
                Systems.RecipeBooks.Learn(sim, me, recipe); // a recipe since removed is forgotten
        SliceDemo.ApplyVillageDefaults(sim, center); // starter stockpile; the player is the keeper

        Entity Unload(Cargo c)
        {
            var item = sim.CreateItem(defs.Thing(c.Def), c.Count, c.Material == null ? null : defs.Material(c.Material));
            if (w.TryGet<Rot>(item, out _))
                w.Get<Rot>(item).Progress = c.Rot;
            if (c.Hp >= 0)
                w.Get<Thing>(item).Hp = c.Hp;
            return item;
        }
        foreach (var g in party.Gear ?? [])
        {
            var item = Unload(g);
            if (w.Get<Thing>(item).Def.Apparel != null)
                sim.WearItem(me, item);
            else
                sim.EquipItem(me, item);
        }
        if (party.Gear == null)
            sim.GiveStartingPack(me); // a party from before packs
        if (party.Belt == null)
            Systems.Toolbelt.GiveStartingTools(sim, me); // a party from before belts
        Systems.Clothing.DressVillager(sim, me); // a party from before clothes
        foreach (var c in party.Belt ?? [])
        {
            var tool = Unload(c);
            if (Systems.Toolbelt.PutOn(sim, me, tool).IsNull)
                sim.PlaceItemNear(tool, w.Get<Position>(me).Cell);
        }
        var pack = sim.PackOf(me);
        foreach (var c in party.PackContents ?? [])
        {
            var left = pack.IsNull ? Unload(c) : sim.Store(pack, Unload(c));
            if (!left.IsNull)
                sim.PlaceItemNear(left, w.Get<Position>(me).Cell);
        }

        if (party.EggProgress >= 0)
        {
            var egg = sim.CreateItem(defs.Thing("DragonEgg"), 1);
            w.Get<Egg>(egg).Progress = party.EggProgress;
            if (party.EggBreed != null)
                w.Get<Egg>(egg).Breed = Systems.Dragons.BreedOrDefault(defs, party.EggBreed);
            w.Get<Egg>(egg).Element = Enum.IsDefined(party.EggElement) ? party.EggElement : DragonElement.Fire;
            w.Get<Thing>(egg).Hp = party.EggHp;
            w.Get<Carrying>(me).Item = egg;
        }

        if (party.Wagon)
        {
            var wagonCell = sim.SpiralFrom(center + new int3(2, 3, 0), 6)
                .FirstOrDefault(c => sim.Map.Passable(c) && sim.CanPlaceStructure(c, defs.Thing("Wagon")) && sim.Map.Item[sim.Map.Idx(c)].IsNull, center);
            var wagon = sim.SpawnStructure(defs.Thing("Wagon"), wagonCell);
            foreach (var c in party.Cargo)
            {
                var item = Unload(c);
                if (sim.Store(wagon, item) is { IsNull: false } left)
                    sim.PlaceItemNear(left, wagonCell);
            }
        }
        else
        {
            // No wagon: whatever else they brought is set down beside them (or goes in the pack).
            foreach (var c in party.Cargo)
            {
                var item = Unload(c);
                var left = pack.IsNull ? item : sim.Store(pack, item);
                if (!left.IsNull)
                    sim.PlaceItemNear(left, w.Get<Position>(me).Cell);
            }
        }

        if (party.Dragon is { } d)
        {
            var dragon = ArriveDragon(sim, me, d);
            // Its saddle after it, so the new entities don't shift anything seeded by the dragon's index.
            if (d.Saddle is { } packedSaddle)
            {
                var saddle = Unload(packedSaddle);
                foreach (var c in d.Saddlebags ?? [])
                    if (sim.Store(saddle, Unload(c)) is { IsNull: false } left)
                        sim.PlaceItemNear(left, w.Get<Position>(me).Cell);
                if (Systems.Saddles.CantWear(sim, dragon, saddle) == null)
                    Systems.Saddles.PutOn(sim, dragon, saddle);
                else
                    sim.PlaceItemNear(saddle, w.Get<Position>(dragon).Cell);
            }
        }
        if (party.Start == StartKind.Pact)
            Systems.TowerPact.Begin(sim, center + new int3(6, -4, 0)); // the sorcerer's mark: the tower goes here

        sim.Messages.Add(MessageKind.Info, ArrivalMessage(party), w.Get<Position>(me).Cell);
        return me;
    }

    /// <summary>The dragon lands beside its keeper, as it was when they set out.</summary>
    private static Entity ArriveDragon(Simulation sim, Entity keeper, PartyDragon d)
    {
        var w = sim.World;
        var dragon = Companions.SpawnBondedDragon(sim, keeper, d.Name, d.Look, w.Get<Position>(keeper).Cell + new int3(-2, 1, 0), d.AgeDays,
            Systems.Dragons.BreedOrDefault(sim.Defs, d.Breed));
        Systems.Dragons.SetElement(sim, dragon, d.Element); // its own, even if its look says otherwise
        if (d.Sex is { } sex)
            w.Get<Dragon>(dragon).Sex = sex; // a new entity would roll it afresh
        ref var dragonState = ref w.Get<Dragon>(dragon);
        dragonState.Affinity = d.Affinity;
        dragonState.Flights = d.Flights;
        var needs = w.Get<Needs>(dragon).Values;
        Array.Copy(d.Needs, needs, Math.Min(d.Needs.Length, needs.Length));
        UnpackHealth(sim, dragon, d.Health);
        if (d.Training != null)
            foreach (var (trick, done) in d.Training)
                Systems.Bonding.Care(sim, dragon).Training[trick] = done;
        return dragon;
    }

    private static string ArrivalMessage(TravelParty party) => party.Start switch
    {
        StartKind.Escape =>
            $"{party.Name} and {party.Dragon?.Name ?? "their dragon"} come down in a wild valley, far from the camp and anyone who'd look for them there. "
            + "Find shelter, a fire and food before the first night.",
        StartKind.Pact =>
            $"{party.Name} and {party.Dragon?.Name ?? "their dragon"} land where the sorcerer's stone pulled them, by a ley stone humming with power. "
            + "His tower must rise here; until it does, his protection keeps raiders away.",
        _ =>
            $"After {party.TravelHours / 24 + 1} days on the road, {party.Name} stops in a quiet valley far from {Prologue.VillageName}. "
            + "Keep the egg warm through the night, and build a home others will want to share.",
    };
}

using DragonWorld.Sim.Components;
using DragonWorld.Sim.Defs;
using DragonWorld.Sim.Saving;

namespace DragonWorld.Sim.Systems;

/// <summary>
/// Who knows which recipes. Recipes without an <see cref="RecipeDef.Unlock"/> are known to
/// everyone. A skill unlock is worked out on the spot, never stored: the village knows it once any
/// member reaches the level, a player once they do. Discoveries (<c>handle:IronOre</c>,
/// <c>built:Kiln</c>) are remembered: in the village book (<see cref="RecipeBookSystem"/>) for
/// villagers' bills, and in a player character's own <see cref="RecipeBook"/>, which also takes
/// what companions teach them. Companions craft from the village book; the player from their own.
/// </summary>
public static class RecipeBooks
{
    /// <summary>A player's own character (connected or not): they keep a book of their own.</summary>
    public static bool HasOwnBook(Simulation sim, Entity who) =>
        sim.World.Has<Avatar>(who) || sim.World.Has<PlayerCharacter>(who);

    public static bool Knows(Simulation sim, Entity who, RecipeDef recipe)
    {
        if (recipe.Unlock is not { } unlock)
            return true;
        if (!HasOwnBook(sim, who))
            return KnownToVillage(sim, recipe);
        if (sim.World.TryGet<RecipeBook>(who, out var book) && book.Known.Contains(recipe))
            return true;
        return unlock.Level > 0 && SkillRules.LevelOf(sim, who, unlock.Skill ?? recipe.Skill) >= unlock.Level;
    }

    /// <summary>In the village book: a skill any village member has reached, or a discovery made.</summary>
    public static bool KnownToVillage(Simulation sim, RecipeDef recipe)
    {
        if (recipe.Unlock is not { } unlock)
            return true;
        if (unlock.Level > 0)
        {
            var skill = unlock.Skill ?? recipe.Skill;
            foreach (var e in sim.World.Each<Skills>())
                if (sim.World.Get<Creature>(e) is { Faction: Faction.Village, Dead: false } && sim.World.Get<Skills>(e)[skill] >= unlock.Level)
                    return true;
            return false;
        }
        return sim.RecipeBook.VillageKnows(recipe);
    }

    /// <summary>
    /// Something happened that teaches recipes (picking up ore, a kiln being built): those with
    /// this trigger go in the village book, and in <paramref name="who"/>'s own if they keep one
    /// (Entity.Null: every player character in the village, who all saw it).
    /// </summary>
    public static void Discover(Simulation sim, Entity who, string trigger)
    {
        if (!sim.Defs.DiscoveryTriggers.Contains(trigger))
            return;
        foreach (var r in sim.Defs.AllRecipes)
        {
            if (r.Unlock?.Discover != trigger)
                continue;
            var village = who.IsNull || sim.World.Get<Creature>(who).Faction == Faction.Village;
            var news = village && sim.RecipeBook.AddToVillage(r);
            if (who.IsNull)
            {
                foreach (var p in sim.World.Query<PlayerCharacter>())
                    news |= Learn(sim, p, r);
            }
            else if (HasOwnBook(sim, who))
                news |= Learn(sim, who, r);
            if (news)
                sim.Messages.Add(MessageKind.Info, $"New recipe: {r.Label} ({DiscoverHint(sim.Defs, trigger)}).",
                    who.IsNull ? null : sim.LocationOf(who));
        }
    }

    /// <summary>Into a player's own book; true if it's new to them.</summary>
    public static bool Learn(Simulation sim, Entity player, RecipeDef recipe)
    {
        var w = sim.World;
        if (!w.Has<RecipeBook>(player))
            w.Add(player, new RecipeBook());
        return w.Get<RecipeBook>(player).Known.Add(recipe);
    }

    /// <summary>A newly reached skill level: announce what it lets someone make.</summary>
    internal static void OnLevelUp(Simulation sim, Entity e, Skill skill, int level)
    {
        var c = sim.World.Get<Creature>(e);
        foreach (var r in sim.Defs.AllRecipes)
        {
            if (r.Unlock is not { Level: > 0 } u || (u.Skill ?? r.Skill) != skill || u.Level != level)
                continue;
            if (HasOwnBook(sim, e))
                sim.Messages.Add(MessageKind.Info, $"{c.Name} worked out how to make {r.Label}.", sim.LocationOf(e));
            else if (c.Faction == Faction.Village && !OthersReached(sim, e, skill, level))
                sim.Messages.Add(MessageKind.Info, $"{c.Name} can make {r.Label} now: it's in the village's book.", sim.LocationOf(e));
        }
    }

    private static bool OthersReached(Simulation sim, Entity e, Skill skill, int level)
    {
        foreach (var o in sim.World.Each<Skills>())
            if (o != e && sim.World.Get<Creature>(o) is { Faction: Faction.Village, Dead: false } && sim.World.Get<Skills>(o)[skill] >= level)
                return true;
        return false;
    }

    /// <summary>A recipe the teacher knows (the village's, or their own) and a player character doesn't yet, in def order; null if none.</summary>
    public static RecipeDef? Teachable(Simulation sim, Entity teacher, Entity learner)
    {
        if (!HasOwnBook(sim, learner) || HasOwnBook(sim, teacher))
            return null;
        foreach (var r in sim.Defs.AllRecipes)
            if (r.Unlock != null && Knows(sim, teacher, r) && !Knows(sim, learner, r))
                return r;
        return null;
    }

    /// <summary>The teaching interaction: the first teachable recipe goes in the learner's book.</summary>
    internal static void Teach(Simulation sim, Entity teacher, Entity learner)
    {
        if (Teachable(sim, teacher, learner) is not { } r)
            return;
        Learn(sim, learner, r);
        sim.Messages.Add(MessageKind.Info, $"{sim.World.Get<Creature>(teacher).Name} showed {sim.World.Get<Creature>(learner).Name} how to make {r.Label}.",
            sim.LocationOf(learner));
    }

    /// <summary>"handle:IronOre" as the player reads it: "found by handling iron ore".</summary>
    public static string DiscoverHint(DefDatabase defs, string trigger)
    {
        var colon = trigger.IndexOf(':');
        var (kind, what) = colon < 0 ? (trigger, "") : (trigger[..colon], trigger[(colon + 1)..]);
        var label = defs.TryThing(what, out var def) ? def.Label : what.ToLowerInvariant();
        return kind switch
        {
            "handle" => $"found by handling {label}",
            "built" => $"found once a {label} is built",
            _ => "found by chance",
        };
    }
}

/// <summary>
/// The village's book of discovered recipes (skill unlocks aren't stored, see <see cref="RecipeBooks"/>),
/// and the check for "handle" discoveries: once a second it looks at what village people and
/// players carry. Saved; it only enters the state hash once something has been discovered.
/// </summary>
public sealed class RecipeBookSystem : ISimSystem, ISaveState
{
    public string Name => "Recipes";
    public int Interval => SimClock.TicksPerSecond;

    private readonly List<RecipeDef> _village = []; // in def order

    public IReadOnlyList<RecipeDef> VillageDiscoveries => _village;

    public bool VillageKnows(RecipeDef r) => _village.Contains(r);

    internal bool AddToVillage(RecipeDef r)
    {
        if (_village.Contains(r))
            return false;
        var at = _village.FindIndex(x => x.Index > r.Index);
        _village.Insert(at < 0 ? _village.Count : at, r);
        return true;
    }

    public void Update(Simulation sim)
    {
        if (sim.Defs.HandleTriggers.Count == 0)
            return;
        var w = sim.World;
        foreach (var e in w.Query<Carrying>()) // ascending entity order
        {
            var c = w.Get<Creature>(e);
            if (c.Dead || (c.Faction != Faction.Village && !RecipeBooks.HasOwnBook(sim, e)))
                continue;
            if (w.Get<Carrying>(e).Item is { IsNull: false } held && w.IsAlive(held))
                Handled(sim, e, w.Get<Thing>(held).Def);
            if (sim.PackOf(e) is { IsNull: false } pack)
                foreach (var item in sim.StoredIn(pack))
                    Handled(sim, e, w.Get<Thing>(item).Def);
        }
    }

    private static void Handled(Simulation sim, Entity who, ThingDef def)
    {
        if (!sim.Defs.HandleTriggers.TryGetValue(def, out var trigger))
            return;
        foreach (var r in sim.Defs.AllRecipes)
            if (r.Unlock?.Discover == trigger && (!sim.RecipeBook.VillageKnows(r) || (RecipeBooks.HasOwnBook(sim, who) && !RecipeBooks.Knows(sim, who, r))))
            {
                RecipeBooks.Discover(sim, who, trigger);
                return;
            }
    }

    public void Save(SaveWriter w, Simulation sim)
    {
        w.Int(_village.Count);
        foreach (var r in _village)
            w.Def(r);
    }

    public void Load(SaveReader r, Simulation sim)
    {
        _village.Clear();
        var n = r.Count(10000);
        for (var i = 0; i < n; i++)
            AddToVillage(r.Def<RecipeDef>());
    }
}

using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Work;

/// <summary>How a pot looks on top: the only way a cook reads its heat, as a smith reads iron by its glow.</summary>
public enum Surface : byte
{
    Still,
    Steaming,
    Simmer,
    Boil,
}

/// <summary>One kind of food gone into a pot, and how much.</summary>
public readonly record struct PotIngredient(ThingDef Def, int Count);

/// <summary>
/// A stew in its pot, on the pot itself. What went in, how hot it is and how far it's cooked are
/// plain to anyone looking in; <see cref="Rot"/> (a spoiled ingredient, or a pot left cold too
/// long) and <see cref="SeasonTarget"/> (what this pot wants) are not, and only
/// <see cref="Perception"/> may describe a pot, without them.
/// </summary>
public sealed class StewPot
{
    public List<PotIngredient> In = [];

    /// <summary>The nutrition of everything put in since it was last empty.</summary>
    public float Nutrition;

    /// <summary>How hot it is, 0 (cold) to 1 (a rolling boil): it follows <see cref="Flame"/> while the fire beside it burns.</summary>
    public float Heat;

    /// <summary>How the fire under it is set, 0 banked right down to 1 roaring.</summary>
    public float Flame = 0.6f;

    /// <summary>How far it's cooked, 0..1.</summary>
    public float Doneness;

    /// <summary>How much has caught on the bottom, 0..1: it tastes of it.</summary>
    public float Scorch;

    /// <summary>Salt and herbs in it, against <see cref="SeasonTarget"/>.</summary>
    public float Seasoning;

    /// <summary>The seasoning this pot wants: different every pot, found only by tasting.</summary>
    public float SeasonTarget;

    /// <summary>Hidden: how far gone it is (<see cref="FoodPoisoning"/>), carried in by its ingredients and gained sitting cold.</summary>
    public float Rot;

    /// <summary>Bowls ladled since it was filled.</summary>
    public int Ladled;

    /// <summary>A villager stirring it now and then (<see cref="Shops.CookPotJob"/>), until this tick.</summary>
    public long TendedUntil;

    public bool Empty => Nutrition <= 0f;
    public bool Cooked => Doneness >= 1f;
}

/// <summary>
/// Cooking by taste, at a stew pot over the hearth. Whatever's in the larder goes in; the cook keeps
/// it at a simmer (read only from the surface: still, steaming, simmering, a rolling boil) by
/// feeding or banking the fire, and stirs so it doesn't catch (<see cref="StewSession"/>). It's
/// seasoned a pinch at a time, tasting as they go (<see cref="SeasonSession"/>), and nothing takes
/// salt back out. A pot gives many bowls, ladled out with a quality the diner tastes. Untended it
/// carries on cooking, and catching, as the fire is set (<see cref="CookingSystem"/>).
/// </summary>
public static class Cooking
{
    /// <summary>The heat at which each surface starts.</summary>
    public const float SteamingAt = 0.3f, SimmerAt = 0.5f, BoilAt = 0.75f;

    /// <summary>Doneness a step at a simmer: twenty seconds of it (most of a game hour) cooks a pot through.</summary>
    public const float CookPerStep = 1f / (20 * SimClock.TicksPerSecond);

    /// <summary>How fast the heat follows the fire, a step.</summary>
    public const float HeatFollow = 0.004f;

    /// <summary>How fast A/D turn the fire up or down, a step.</summary>
    public const float FlameStep = 0.008f;

    /// <summary>Scorch a step for each unit of heat over a simmer's start, left unstirred: a boil scorches through in ten seconds.</summary>
    public const float ScorchRate = 1f / 300f;

    /// <summary>Steps unstirred before the bottom starts to catch; it catches at its full rate after as long again.</summary>
    public const int CatchSteps = 4 * SimClock.TicksPerSecond;

    /// <summary>Unstirred long enough to catch at the full rate: a pot left to itself.</summary>
    public const int Untended = CatchSteps * 2;

    /// <summary>How well an attentive villager's stirring keeps it from catching (1: not at all).</summary>
    public const float TendedCatch = 0.3f;

    /// <summary>The least doneness a bowl can be ladled at.</summary>
    public const float LadleFrom = 0.5f;

    /// <summary>A pinch of seasoning, before its own small variation.</summary>
    public const float Pinch = 0.07f;

    public static Surface SurfaceOf(float heat) => heat switch
    {
        < SteamingAt => Surface.Still,
        < SimmerAt => Surface.Steaming,
        < BoilAt => Surface.Simmer,
        _ => Surface.Boil,
    };

    public static string SurfaceLabel(Surface s) => s switch
    {
        Surface.Still => "still",
        Surface.Steaming => "steaming",
        Surface.Simmer => "simmering",
        _ => "at a rolling boil",
    };

    /// <summary>
    /// One step of a pot: the heat follows the fire (or falls without one), it cooks while it's hot
    /// enough, and past a simmer it catches on the bottom the longer it goes unstirred. The one
    /// model for the cook's session and the pot left to itself.
    /// </summary>
    public static void Step(ref float heat, float flame, bool lit, ref float doneness, ref float scorch, int sinceStir, bool stirring, float catchFactor = 1f)
    {
        var target = lit ? flame : 0f;
        heat += Math.Clamp(target - heat, -HeatFollow, HeatFollow);
        var rate = SurfaceOf(heat) switch
        {
            Surface.Still => 0f,
            Surface.Steaming => 0.35f,
            Surface.Simmer => 1f,
            _ => 1.15f,
        };
        // Stirring all the time lets the heat out: it cooks slower while the spoon's going.
        doneness = Math.Min(1.5f, doneness + CookPerStep * rate * (stirring ? 0.8f : 1f));
        if (heat > SimmerAt)
        {
            var unstirred = stirring ? 0f : Math.Clamp((sinceStir - CatchSteps) / (float)CatchSteps, 0f, 1f) * catchFactor;
            scorch = Math.Min(1f, scorch + (heat - SimmerAt) * ScorchRate * unstirred);
        }
    }

    /// <summary>A pot's quality, 0..1: seasoned to its taste, not scorched, cooked through, and a mix of things in it.</summary>
    public static float QualityOf(StewPot p)
    {
        var season = Math.Clamp(1f - MathF.Abs(p.Seasoning - p.SeasonTarget) * 4f, 0f, 1f);
        var variety = Math.Min(1f, 0.55f + 0.15f * p.In.Count);
        var done = Math.Clamp(p.Doneness, 0f, 1f);
        return (0.45f * season + 0.35f * (1f - p.Scorch) + 0.2f * variety) * (0.3f + 0.7f * done * done);
    }

    /// <summary>What a taste of it says about the seasoning, given how far off it is (noise and all).</summary>
    public static string TasteWord(float off) => off switch
    {
        < -0.2f => "flat: it wants a good deal more",
        < -0.06f => "it could take a little more",
        <= 0.06f => "about right",
        <= 0.18f => "a touch salty",
        _ => "far too salty",
    };

    public static bool IsPot(Simulation sim, Entity e) =>
        !e.IsNull && sim.World.IsAlive(e) && sim.World.TryGet<Thing>(e, out var t) && t.Def.Building?.StewPot != null && !sim.World.Has<Construction>(e);

    /// <summary>The stew pot in a building (the inn's kitchen), Entity.Null if it has none.</summary>
    public static Entity PotIn(Simulation sim, Entity building)
    {
        if (building.IsNull || !sim.World.IsAlive(building))
            return Entity.Null;
        foreach (var part in Buildings.Parts(sim, building))
            if (IsPot(sim, part))
                return part;
        return Entity.Null;
    }

    /// <summary>Someone has the pot in hand in a session (tending or seasoning it).</summary>
    public static bool BeingWorked(Simulation sim, Entity pot)
    {
        foreach (var e in sim.World.Each<WorkSession>())
            if (CookingSystem.PotWorked(sim.World.Get<WorkSession>(e)) == pot)
                return true;
        return false;
    }

    /// <summary>The pot's state; made the first time anyone looks.</summary>
    public static StewPot PotOf(Simulation sim, Entity pot)
    {
        if (!sim.World.TryGet<StewPot>(pot, out var p))
            sim.World.Add(pot, p = new StewPot());
        return p;
    }

    /// <summary>Food that can go in a pot: raw, not a drink, not already a meal.</summary>
    public static bool IsIngredient(ThingDef def) => def.IsFood && !def.Cooked && !def.Drink;

    /// <summary>The food one bowl of what the pot serves holds.</summary>
    public static ThingDef Serves(Simulation sim, Entity pot) => sim.Defs.Thing(sim.World.Get<Thing>(pot).Def.Building!.StewPot!.Serves);

    /// <summary>Bowls the pot holds in all, from what's gone in.</summary>
    public static int ServingsOf(Simulation sim, Entity pot, StewPot p)
    {
        var props = sim.World.Get<Thing>(pot).Def.Building!.StewPot!;
        return (int)MathF.Floor(p.Nutrition / Math.Max(0.01f, Serves(sim, pot).Nutrition) * props.Yield + 0.001f);
    }

    public static int ServingsLeft(Simulation sim, Entity pot) =>
        IsPot(sim, pot) && sim.World.TryGet<StewPot>(pot, out var p) ? Math.Max(0, ServingsOf(sim, pot, p) - p.Ladled) : 0;

    /// <summary>A fire burning beside the pot (the hearth it hangs over): it heats only while there is one.</summary>
    public static bool Lit(Simulation sim, Entity pot)
    {
        var w = sim.World;
        var at = w.Get<Thing>(pot).Cell;
        foreach (var d in Maps.Map.Neighbors8)
        {
            var c = new int3(at.X + d.X, at.Y + d.Y, at.Z);
            if (sim.Map.InBounds(c) && sim.Map.Structure[sim.Map.Idx(c)] is { IsNull: false } s && !w.Has<Construction>(s)
                && w.Get<Thing>(s).Def.Building is { Fuel: not null, HeatOutput: > 0 } && w.TryGet<Fueled>(s, out var f) && f.Fuel > 0)
                return true;
        }
        return false;
    }

    /// <summary>Put food in the pot: its nutrition (and, unseen, its rot) goes into the stew, and it cools what's there.</summary>
    public static int Add(Simulation sim, Entity pot, Entity food)
    {
        var w = sim.World;
        var p = PotOf(sim, pot);
        var t = w.Get<Thing>(food);
        var props = t.Def.Building?.StewPot ?? w.Get<Thing>(pot).Def.Building!.StewPot!;
        var room = (int)MathF.Floor((props.Holds - p.Nutrition) / t.Def.Nutrition + 0.001f);
        var count = Math.Min(w.Get<Stack>(food).Count, room);
        if (count <= 0)
            return 0;
        if (p.Empty)
            Refill(sim, p);
        var added = count * t.Def.Nutrition;
        var total = p.Nutrition + added;
        p.Rot = (p.Rot * p.Nutrition + FoodPoisoning.RotOf(sim, food) * added) / total;
        // Cold food takes the heat down, and what's cooked so far is spread over more.
        p.Heat *= p.Nutrition / total;
        p.Doneness *= p.Nutrition / total;
        p.Seasoning *= p.Nutrition / total;
        p.Nutrition = total;
        var i = p.In.FindIndex(x => x.Def == t.Def);
        if (i >= 0)
            p.In[i] = p.In[i] with { Count = p.In[i].Count + count };
        else
            p.In.Add(new PotIngredient(t.Def, count));
        // Out of whoever's hands (TakeFromStack hands back the same entity when it takes the lot) or pack.
        var used = sim.TakeFromStack(food, count);
        foreach (var e in w.Query<Carrying>())
            if (w.Get<Carrying>(e).Item == used)
                w.Get<Carrying>(e).Item = Entity.Null;
        w.Destroy(used);
        return count;
    }

    /// <summary>An empty pot starts over: nothing in it, and a new taste to find.</summary>
    private static void Refill(Simulation sim, StewPot p)
    {
        p.In.Clear();
        p.Nutrition = p.Doneness = p.Scorch = p.Seasoning = p.Rot = 0f;
        p.Ladled = 0;
        p.SeasonTarget = sim.Rand.Range(0.3f, 0.6f);
    }

    /// <summary>The clean vessel a pot's bowls are ladled into.</summary>
    public static string BowlOf(Simulation sim, Entity pot) => sim.World.Get<Thing>(pot).Def.Building!.StewPot!.Into;

    /// <summary>
    /// A bowl from the pot, into the server's hand, in a clean bowl of theirs: the pot's quality,
    /// and its rot with it. The last bowl empties it. Entity.Null if there's nothing to ladle or
    /// nothing clean to ladle into.
    /// </summary>
    public static Entity Ladle(Simulation sim, Entity server, Entity pot, Quality? quality = null)
    {
        var w = sim.World;
        if (!IsPot(sim, pot) || ServingsLeft(sim, pot) <= 0 || !Vessels.Use(sim, server, BowlOf(sim, pot)))
            return Entity.Null;
        var p = PotOf(sim, pot);
        var bowl = sim.CreateItem(Serves(sim, pot), 1);
        w.Add(bowl, new Crafted { Quality = quality ?? Crafting.QualityFrom(QualityOf(p)), Maker = server });
        if (w.Has<Rot>(bowl))
            w.Get<Rot>(bowl).Progress = p.Rot;
        p.Ladled++;
        if (ServingsLeft(sim, pot) <= 0)
            p.Nutrition = 0f; // scraped clean
        ref var carrying = ref w.Get<Carrying>(server);
        if (carrying.Item.IsNull)
            carrying.Item = bowl;
        else
            Inventory.Stow(sim, server, bowl);
        return bowl;
    }

    /// <summary>What the work key does at a pot: put in the food you hold, tend it, or ladle a bowl. Null if nothing.</summary>
    public static string? StepAt(Simulation sim, Entity cook, Entity station)
    {
        if (!IsPot(sim, station))
            return null;
        var w = sim.World;
        if (AvatarSystem.Supply(sim, cook, IsIngredient) is { IsNull: false } food)
            return $"Put the {w.Get<Thing>(food).Def.Label} in the pot";
        if (!w.TryGet<StewPot>(station, out var p) || p.Empty)
            return null;
        if (p.Cooked && ServingsLeft(sim, station) > 0 && Vessels.Ready(sim, cook, BowlOf(sim, station)))
            return "Ladle a bowl of stew";
        return "Tend the stew";
    }

    /// <summary>Do what the pot offers (<see cref="StepAt"/>), or a step asked for by name, or say why not.</summary>
    public static void Start(Simulation sim, Entity cook, Entity station, PotStep step = PotStep.Next)
    {
        var w = sim.World;
        if (!IsPot(sim, station) || !AvatarSystem.InReach(sim, w.Get<Position>(cook).Cell, station))
            return;
        var cell = w.Get<Thing>(station).Cell;
        var p = PotOf(sim, station);
        if (step == PotStep.Next)
        {
            if (AvatarSystem.Supply(sim, cook, IsIngredient) is { IsNull: false })
                step = PotStep.Add;
            else if (p.Empty)
            {
                sim.Messages.Add(MessageKind.Info, "The pot's empty: bring it something from the larder.", cell);
                return;
            }
            else
                step = p.Cooked && ServingsLeft(sim, station) > 0 && Vessels.Ready(sim, cook, BowlOf(sim, station)) ? PotStep.Ladle : PotStep.Tend;
        }
        switch (step)
        {
            case PotStep.Add:
                if (AvatarSystem.Supply(sim, cook, IsIngredient) is not { IsNull: false } food)
                    return;
                var label = w.Get<Thing>(food).Def.Label;
                var n = Add(sim, station, food);
                sim.Messages.Add(MessageKind.Info, n > 0 ? $"Put {n} {label} in the pot." : "The pot's full.", cell);
                return;
            case PotStep.Tend:
                if (p.Empty)
                    return;
                WorkSystem.Start(sim, cook, StewSession.For(sim, station));
                return;
            case PotStep.Season:
                if (p.Empty)
                    return;
                WorkSystem.Start(sim, cook, SeasonSession.For(sim, station));
                return;
            case PotStep.Ladle:
                if (p.Doneness < LadleFrom)
                {
                    sim.Messages.Add(MessageKind.Info, "It's nowhere near cooked yet.", cell);
                    return;
                }
                var bowlDef = BowlOf(sim, station);
                if (!Vessels.Ready(sim, cook, bowlDef))
                {
                    sim.Messages.Add(MessageKind.Info, Vessels.CleanOn(sim, cook, bowlDef).IsNull
                        ? $"You need a clean {sim.Defs.Thing(bowlDef).Label} to ladle into: there are some on the kitchen shelf."
                        : $"You need a hand free for the {sim.Defs.Thing(bowlDef).Label}.", cell);
                    return;
                }
                var bowl = Ladle(sim, cook, station);
                if (!bowl.IsNull)
                    sim.Messages.Add(MessageKind.Info, $"Ladled a {Crafting.QualityLabel(w.Get<Crafted>(bowl).Quality)} bowl of {w.Get<Thing>(bowl).Def.Label}.", cell);
                return;
        }
    }
}

public enum PotStep : byte
{
    /// <summary>Whatever the work key would do.</summary>
    Next,
    Add,
    Tend,
    Season,
    Ladle,
}

/// <summary>
/// Pots left to themselves carry on: heating or cooling as the fire beside them is set, cooking,
/// catching on the bottom past a simmer, and (cold, with stew still in them) slowly going off.
/// </summary>
public sealed class CookingSystem : ISimSystem
{
    public string Name => "Cooking";
    public int Interval => 10;

    public void Update(Simulation sim)
    {
        var w = sim.World;
        if (w.Count<StewPot>() == 0)
            return;
        HashSet<Entity>? worked = null;
        foreach (var e in w.Each<WorkSession>())
            if (PotWorked(w.Get<WorkSession>(e)) is { IsNull: false } at)
                (worked ??= []).Add(at);
        var now = sim.Clock.Tick;
        foreach (var e in w.Query<StewPot>())
        {
            var p = w.Get<StewPot>(e);
            if (worked?.Contains(e) == true || !w.Has<Thing>(e))
                continue;
            var lit = Cooking.Lit(sim, e);
            var tended = p.TendedUntil > now;
            for (var i = 0; i < Interval; i++)
                Cooking.Step(ref p.Heat, p.Flame, lit, ref p.Doneness, ref p.Scorch, Cooking.Untended, false, tended ? Cooking.TendedCatch : 1f);
            if (p.Empty)
            {
                p.Doneness = p.Scorch = 0f;
                continue;
            }
            // Kept hot it keeps; gone cold it turns, as fast as the stew itself would.
            if (p.Heat < Cooking.SteamingAt)
            {
                var days = Cooking.Serves(sim, e).RotDays;
                if (days > 0)
                    p.Rot = Math.Min(1f, p.Rot + Interval / (days * SimClock.TicksPerDay));
            }
        }
    }

    /// <summary>The pot a session has in hand (it writes the pot back when done), or Entity.Null.</summary>
    public static Entity PotWorked(WorkSession s) => s switch
    {
        StewSession st => st.Pot,
        SeasonSession se => se.Pot,
        _ => Entity.Null,
    };
}

/// <summary>The work key at a stew pot, or one of its steps by name (<see cref="Cooking.Start"/>).</summary>
public sealed record PotCommand(Entity Avatar, Entity Pot, PotStep Step) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (!InventoryCommand.Ready(sim, Avatar) || !Cooking.IsPot(sim, Pot) || !Enum.IsDefined(Step))
            return;
        Cooking.Start(sim, Avatar, Pot, Step);
    }
}

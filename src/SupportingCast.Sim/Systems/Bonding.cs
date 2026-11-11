using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Core;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Maps;

namespace SupportingCast.Sim.Systems;

/// <summary>One act of care, kept a while for the hearts over the dragon and the inspector (render-only, not state).</summary>
public readonly record struct CareEvent(long Tick, Entity Keeper, Entity Dragon, DragonInteractionDef Def, float Bond, float Affinity, float Novelty, bool Answered, bool Favourite);

/// <summary>
/// The rules of caring for a dragon (docs/plans/dragon-interactions.md), one path for the player,
/// village keepers and everything else that touches the bond: novelty (the same thing again soon
/// counts less), answering what the dragon asked for, imprinting in its first day, trust levels
/// that slow affinity as it climbs, tricks, and the neglect that ends in a sulk or a departure.
/// </summary>
public static class Bonding
{
    /// <summary>Gains when an interaction answers the dragon's want.</summary>
    public const float AnsweredBonus = 1.5f;

    /// <summary>A dragon's first days imprint: affinity gains count double until it's this old.</summary>
    public const float ImprintDays = 1f;

    /// <summary>How long a want waits (awake) before it counts as ignored.</summary>
    public const int WantWindowTicks = SimClock.TicksPerHour * 2;

    /// <summary>Affinity an ignored want costs.</summary>
    public const float IgnoredAffinity = 0.01f;

    /// <summary>Mood (0..1) below which a dragon is upset, and can be comforted.</summary>
    public const float UpsetMood = 0.38f;

    /// <summary>Hours at the neglected level (Bond under 0.1) before it sulks; sulking hours before an untrusting one leaves.</summary>
    public const float SulkAfterHours = 36f, LeaveAfterHours = 72f;

    /// <summary>Bond that wins a sulking dragon back.</summary>
    public const float ForgivenBond = 0.5f;

    /// <summary>How far away (cells) an untrained dragon hears its keeper call.</summary>
    public const int CallRange = 30;

    /// <summary>How long a Stay lasts if nobody calls it.</summary>
    public const int StayTicks = SimClock.TicksPerHour * 12;

    /// <summary>How long a dragon's scales itch after it sheds (grooming then brings scales away).</summary>
    public const int ItchyTicks = SimClock.TicksPerDay;

    /// <summary>Share of a non-keeper's care that counts towards affinity (anyone feeding a starving dragon).</summary>
    public const float OthersShare = 0.5f;

    /// <summary>The dragon's care record, made if it has none (a dragon from an older save).</summary>
    public static DragonCare Care(Simulation sim, Entity dragon)
    {
        if (sim.World.TryGet<DragonCare>(dragon, out var care))
            return care;
        care = new DragonCare();
        sim.World.Add(dragon, care);
        return care;
    }

    private static readonly DragonCare Blank = new();

    /// <summary>Read-only: the care record, or a blank one (never add from a finder or the UI).</summary>
    public static DragonCare Peek(Simulation sim, Entity dragon) =>
        sim.World.TryGet<DragonCare>(dragon, out var care) ? care : Blank;

    // ---------------- Trust ----------------

    /// <summary>The trust level an affinity is at (null if the defs have none).</summary>
    public static TrustLevelDef? Level(Simulation sim, float affinity)
    {
        TrustLevelDef? at = null;
        foreach (var t in sim.Defs.TrustLevels)
            if (affinity >= t.MinAffinity)
                at = t;
        return at;
    }

    public static TrustLevelDef? Level(Simulation sim, Entity dragon) => Level(sim, sim.World.Get<Dragon>(dragon).Affinity);

    /// <summary>The next level up, or null at the top.</summary>
    public static TrustLevelDef? NextLevel(Simulation sim, float affinity) =>
        sim.Defs.TrustLevels.FirstOrDefault(t => t.MinAffinity > affinity);

    /// <summary>Has the dragon reached the named trust level? False if the defs lack it.</summary>
    public static bool Reached(Simulation sim, Entity dragon, string level)
    {
        var def = sim.Defs.TrustLevels.Find(t => t.Name == level);
        return def != null && sim.World.TryGet<Dragon>(dragon, out var d) && d.Affinity >= def.MinAffinity;
    }

    public const string Accepting = "Accepting", Devoted = "Devoted", Soulbound = "Soulbound";

    // ---------------- Tricks ----------------

    public static bool Knows(Simulation sim, Entity dragon, TrickEffect effect)
    {
        var care = Peek(sim, dragon);
        foreach (var t in sim.Defs.AllTricks)
            if (t.Effect == effect && care.Knows(t))
                return true;
        return false;
    }

    /// <summary>Obedience its tricks add for this work (<see cref="DragonSystem.Obedience(Simulation, Entity, WorkType)"/>).</summary>
    public static float TrickObedience(Simulation sim, Entity dragon, WorkType work)
    {
        var care = Peek(sim, dragon);
        var bonus = 0f;
        foreach (var t in sim.Defs.AllTricks)
            if (t.Effect == TrickEffect.Work && t.Work.Contains(work) && care.Knows(t))
                bonus += t.Obedience;
        return bonus;
    }

    /// <summary>Obedience the Steady trick adds while mounted.</summary>
    public static float SteadyObedience(Simulation sim, Entity dragon)
    {
        var care = Peek(sim, dragon);
        return sim.Defs.AllTricks.Where(t => t.Effect == TrickEffect.Steady && care.Knows(t)).Sum(t => t.Obedience);
    }

    /// <summary>The first trick it hasn't learned, or null.</summary>
    public static TrickDef? NextTrick(Simulation sim, Entity dragon)
    {
        var care = Peek(sim, dragon);
        return sim.Defs.AllTricks.FirstOrDefault(t => !care.Knows(t));
    }

    // ---------------- Novelty, favourites ----------------

    /// <summary>What doing this now would count for: 1, or ½ and ¼ when it's been done within its novelty window.</summary>
    public static float Novelty(Simulation sim, Entity dragon, DragonInteractionDef def)
    {
        if (!Peek(sim, dragon).Last.TryGetValue(def.Name, out var last))
            return 1f;
        if (sim.Clock.Tick - last.Tick >= def.NoveltyHours * SimClock.TicksPerHour)
            return 1f;
        return Math.Min(last.Repeats + 1, 2) == 1 ? 0.5f : 0.25f;
    }

    /// <summary>The dragon's favourite food among the hand-feeding def's list, hashed from its name (stable across maps and saves).</summary>
    public static ThingDef? Favourite(Simulation sim, Entity dragon)
    {
        var feed = sim.Defs.AllDragonInteractions.Find(d => d.Kind == CareKind.Feed);
        if (feed == null)
            return null;
        var c = sim.World.Get<Creature>(dragon);
        var foods = feed.FavouriteDefs.Where(f => f.EdibleBy(c.Def)).ToList();
        if (foods.Count == 0)
            return null;
        var h = 2166136261u;
        foreach (var ch in c.Name)
            h = (h ^ ch) * 16777619u;
        return foods[(int)(h % (uint)foods.Count)];
    }

    public static bool Imprinting(Simulation sim, Entity dragon) =>
        sim.World.Get<Creature>(dragon).AgeDays(sim.Clock.Tick) < ImprintDays;

    // ---------------- What can be done ----------------

    /// <summary>Is the dragon upset enough to be comforted?</summary>
    public static bool Upset(Simulation sim, Entity dragon) =>
        Peek(sim, dragon).Want == DragonWant.Comfort || Peek(sim, dragon).Sulking || MoodRules.MoodOf(sim, dragon) < UpsetMood;

    /// <summary>
    /// Why <paramref name="keeper"/> can't do this with <paramref name="dragon"/> now, or null if they
    /// can. <paramref name="food"/> is what they'd hand-feed it.
    /// </summary>
    public static string? Problem(Simulation sim, Entity keeper, Entity dragon, DragonInteractionDef def, ThingDef? food = null)
    {
        var w = sim.World;
        if (!w.IsAlive(dragon) || !w.TryGet<Dragon>(dragon, out var d))
            return "it's gone";
        var c = w.Get<Creature>(dragon);
        if (c.Dead || !c.Able)
            return "it's not fit";
        if (d.Keeper != keeper && def.Kind != CareKind.Feed)
            return "only its keeper can";
        var stage = c.Stage(sim.Clock.Tick);
        if (!def.Suits(stage))
            return $"not for a {stage.Label}";
        if (w.Get<Mind>(dragon).Asleep && def.Kind != CareKind.Rest)
            return "it's asleep";
        if (w.Has<Mount>(dragon) || w.Has<Riding>(keeper))
            return "not while riding";
        if (sim.Combat.Involves(dragon) || sim.Combat.Involves(keeper))
            return "not in a fight";
        switch (def.Kind)
        {
            case CareKind.Rest:
                return "sleep beside it";
            case CareKind.Gift:
                return "hold treasure and press F at it";
            case CareKind.Feed when food == null:
                return "hold food it eats";
            case CareKind.Feed when !food.EdibleBy(c.Def):
                return $"it won't eat {food.Label}";
            case CareKind.Comfort when !Upset(sim, dragon):
                return "it isn't upset";
            case CareKind.Train when NextTrick(sim, dragon) == null:
                return "it knows every trick";
        }
        if (def.Tool is { } tool && Toolbelt.Best(sim, keeper, tool).IsNull)
            return $"you need a {ToolName(sim, tool)} on your belt";
        return null;
    }

    /// <summary>"scale brush" for "Brush": the label of the first item that is that kind of tool.</summary>
    public static string ToolName(Simulation sim, string kind) =>
        sim.Defs.AllThings.Find(t => t.Tool?.Kind == kind)?.Label ?? kind.ToLowerInvariant();

    // ---------------- Doing it ----------------

    /// <summary>
    /// Carry out an interaction: the bond and affinity it's worth now, the memory, what its kind
    /// does (scales come away, a lesson, a want answered), a sulk ended, a new trust level announced.
    /// <paramref name="amount"/> scales the def's gains (a small gift), <paramref name="food"/> is
    /// what was hand-fed, <paramref name="trick"/> what was taught (else the next one).
    /// <paramref name="answers"/> false is care in passing (a pet while chatting nearby): it doesn't
    /// answer a want, which takes doing on purpose.
    /// </summary>
    public static CareEvent Interact(Simulation sim, Entity keeper, Entity dragon, DragonInteractionDef def,
        float amount = 1f, ThingDef? food = null, TrickDef? trick = null, bool answers = true)
    {
        var w = sim.World;
        var care = Care(sim, dragon);
        var now = sim.Clock.Tick;
        var novelty = Novelty(sim, dragon, def);
        care.Last[def.Name] = (now, novelty < 1f && care.Last.TryGetValue(def.Name, out var last) ? last.Repeats + 1 : 0);

        ref var d = ref w.Get<Dragon>(dragon);
        var c = w.Get<Creature>(dragon);
        var mine = d.Keeper == keeper;
        var answered = answers && def.Answers != DragonWant.None && care.Want == def.Answers;
        if (answered)
            care.Want = DragonWant.None;
        var favourite = def.Kind == CareKind.Feed && food != null && Favourite(sim, dragon) == food;

        var scale = amount * novelty * (answered ? AnsweredBonus : 1f);
        var bond = def.Bond * scale;
        var before = d.Affinity;
        var gain = def.Affinity * scale * (favourite ? 2f : 1f) * (Imprinting(sim, dragon) ? 2f : 1f)
            * (Level(sim, before)?.GainFactor ?? 1f) * (mine ? 1f : OthersShare);
        var bondNeed = sim.Defs.Need("Bond");
        w.Get<Needs>(dragon).Add(bondNeed, bond);
        d.Affinity += gain;

        var thought = mine ? def.ThoughtDef : def.Kind == CareKind.Feed ? sim.Defs.Thoughts.FedMe : null;
        MoodSystem.Remember(sim, dragon, thought, keeper);
        if (answered && mine)
            MoodSystem.Remember(sim, dragon, sim.Defs.Thoughts.CameWhenCalled, keeper);
        if (favourite)
            MoodSystem.Remember(sim, dragon, sim.Defs.Thoughts.AteFavourite, keeper);

        var at = w.Get<Position>(dragon).Cell;
        switch (def.Kind)
        {
            case CareKind.Groom when now < care.ItchyUntil:
                care.ItchyUntil = 0;
                if (c.Stage(now).Armor >= 0.2f && sim.Defs.TryThing("DragonScale", out var scale2))
                {
                    sim.SpawnItem(scale2, 1 + (int)(SimRandom.Hash(dragon.Index, (int)(now / SimClock.TicksPerHour), 4421) % 2), at);
                    sim.Messages.Add(MessageKind.Good, $"Loose scales come away as you groom {c.Name}: it leans into the brush.", at);
                }
                break;
            case CareKind.Train:
                trick ??= NextTrick(sim, dragon);
                if (trick != null && !care.Knows(trick))
                {
                    var done = care.Training.GetValueOrDefault(trick.Name) + novelty;
                    care.Training[trick.Name] = done;
                    if (done >= trick.Sessions)
                        sim.Messages.Add(MessageKind.Good, $"{c.Name} has learned \"{trick.Label}\"! {trick.Description}", at);
                }
                break;
        }

        if (care.Sulking && w.Get<Needs>(dragon).Get(bondNeed) >= ForgivenBond)
        {
            care.Sulking = false;
            care.SulkHours = care.NeglectHours = 0;
            sim.Messages.Add(MessageKind.Good, $"{c.Name} has forgiven {w.Get<Creature>(keeper).Name}.", at);
        }
        if (Level(sim, d.Affinity) is { } now2 && now2 != Level(sim, before) && d.Affinity > before && mine)
            sim.Messages.Add(MessageKind.Good,
                $"{c.Name} is {now2.Label} of {w.Get<Creature>(keeper).Name} now.{(now2.Unlocks.Length > 0 ? " " + now2.Unlocks : "")}", at);

        var ev = new CareEvent(now, keeper, dragon, def, bond, gain, novelty, answered, favourite);
        sim.DragonCare.Recent.Add(ev);
        return ev;
    }

    /// <summary>
    /// Hand-feed: the dragon eats what <paramref name="food"/> holds (as much as it wants) from the
    /// keeper's hand, then the care counts. Returns what's left of the stack (Entity.Null if all eaten).
    /// </summary>
    public static Entity HandFeed(Simulation sim, Entity keeper, Entity dragon, Entity food)
    {
        var w = sim.World;
        var def = w.Get<Thing>(food).Def;
        var count = Math.Min(EatJob.Wanted(sim, dragon, def), w.Get<Stack>(food).Count);
        var eaten = sim.TakeFromStack(food, count);
        var left = w.IsAlive(food) && eaten != food ? food : Entity.Null;
        var rot = FoodPoisoning.RotOf(sim, eaten);
        w.Destroy(eaten);
        EatJob.Nourish(sim, dragon, def, count, rot);
        VisitorSystem.AteVillageFood(sim, dragon);
        if (sim.Defs.AllDragonInteractions.Find(x => x.Kind == CareKind.Feed) is { } feed)
            Interact(sim, keeper, dragon, feed, food: def);
        return left;
    }

    /// <summary>The keeper's interaction that answers a want, if there is one they could do.</summary>
    public static DragonInteractionDef? Answering(Simulation sim, DragonWant want) =>
        want == DragonWant.None ? null : sim.Defs.AllDragonInteractions.Find(d => d.Answers == want);

    // ---------------- Hand-fed hatchlings ----------------

    /// <summary>Food stored in a feeding trough (<see cref="BuildingProps.DragonBowl"/>).</summary>
    public static bool InTrough(Simulation sim, Entity item) =>
        sim.World.TryGet<Stored>(item, out var s) && sim.World.IsAlive(s.In) && sim.World.TryGet<Thing>(s.In, out var t)
        && t.Def.Building?.DragonBowl == true;

    /// <summary>A meal from a trough for a hand-fed dragon, or null.</summary>
    public static Job? TroughMeal(Simulation sim, Entity dragon)
    {
        var def = sim.World.Get<Creature>(dragon).Def;
        var food = AiQueries.FindNearestItem(sim, dragon, (e, d) => d.EdibleBy(def) && InTrough(sim, e));
        return food.IsNull ? null : new EatJob(food);
    }

    // ---------------- Orders: call and stay ----------------

    /// <summary>Why calling the dragon would go unanswered, or null if it would come.</summary>
    public static string? CantCall(Simulation sim, Entity keeper, Entity dragon)
    {
        var w = sim.World;
        if (!w.IsAlive(dragon) || !w.TryGet<Dragon>(dragon, out var d) || d.Keeper != keeper)
            return "it isn't yours";
        var c = w.Get<Creature>(dragon);
        if (!c.Able)
            return "it's not fit";
        if (w.Has<Mount>(dragon))
            return "you're on its back";
        if (w.Has<Chained>(dragon))
            return "it's in chains";
        if (w.Get<Mind>(dragon).Asleep)
            return "it's asleep";
        if (sim.Combat.Involves(dragon))
            return "it's fighting";
        var come = Knows(sim, dragon, TrickEffect.Come);
        if (!come && !Reached(sim, dragon, Accepting))
            return $"it doesn't trust you enough yet ({sim.Defs.TrustLevels.Find(t => t.Name == Accepting)?.Label ?? Accepting})";
        if (!come && Map.Distance(sim.LocationOf(keeper), sim.LocationOf(dragon)) > CallRange)
            return "too far to hear you (teach it \"come\")";
        if (Peek(sim, dragon).Sulking)
            return "it's sulking";
        return null;
    }

    /// <summary>Call the dragon over: it comes unless it doesn't feel like it (never, once it knows "come").</summary>
    public static bool Call(Simulation sim, Entity keeper, Entity dragon)
    {
        var name = sim.World.Get<Creature>(dragon).Name;
        if (CantCall(sim, keeper, dragon) is { } why)
        {
            sim.Messages.Add(MessageKind.Info, $"{name} doesn't come: {why}.", sim.LocationOf(keeper));
            return false;
        }
        if (!Knows(sim, dragon, TrickEffect.Come) && !sim.Rand.Chance(DragonSystem.Obedience(sim, dragon)))
        {
            sim.Messages.Add(MessageKind.Info, $"{name} hears you, and doesn't come.", sim.LocationOf(keeper));
            return false;
        }
        Peek(sim, dragon).StayAt = null;
        AiSystem.Order(sim, dragon, new ComeToJob(keeper));
        return true;
    }

    /// <summary>Tell a dragon that knows "stay" to hold where it is (until called, or <see cref="StayTicks"/>).</summary>
    public static bool Stay(Simulation sim, Entity keeper, Entity dragon)
    {
        var w = sim.World;
        if (!w.IsAlive(dragon) || w.Get<Dragon>(dragon).Keeper != keeper || !Knows(sim, dragon, TrickEffect.Stay) || !w.Get<Creature>(dragon).Able)
            return false;
        var care = Care(sim, dragon);
        care.StayAt = w.Get<Position>(dragon).Cell;
        care.StayUntil = sim.Clock.Tick + StayTicks;
        AiSystem.Order(sim, dragon, new WanderJob(care.StayAt.Value, SimClock.TicksPerHour));
        sim.Messages.Add(MessageKind.Info, $"{w.Get<Creature>(dragon).Name} settles down to wait.", care.StayAt.Value);
        return true;
    }

    /// <summary>A player's dragon, for messages only they need: Entity.Null if its keeper is no player's.</summary>
    internal static bool PlayersDragon(Simulation sim, Entity dragon) =>
        sim.World.TryGet<Dragon>(dragon, out var d) && sim.World.IsAlive(d.Keeper)
        && (sim.World.Has<Avatar>(d.Keeper) || sim.World.Has<PlayerCharacter>(d.Keeper));

    /// <summary>"wants feeding": a want as the message and the inspector put it.</summary>
    public static string Describe(DragonWant want) => want switch
    {
        DragonWant.Food => "wants feeding",
        DragonWant.Play => "wants to play",
        DragonWant.Scratch => "wants a scratch",
        DragonWant.Comfort => "is upset and wants comforting",
        DragonWant.Groom => "is itchy and wants grooming",
        DragonWant.Treasure => "wants something shiny for its hoard",
        _ => "",
    };
}

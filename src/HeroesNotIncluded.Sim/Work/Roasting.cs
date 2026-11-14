using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Work;

/// <summary>
/// A joint on the spit, on the spit itself: how many pieces, and how each of its four sides has
/// browned (read only as colour, as iron is), with the side to the fire. How far it's cooked
/// through (<see cref="Inside"/>) doesn't show: only time and practice tell it. Its rot is hidden too.
/// </summary>
public sealed class SpitRoast
{
    public const int Sides = 4;

    public int Count;
    public float Rot;
    public float[] Brown = new float[Sides];
    public float Inside;
    public int Facing;

    public bool Empty => Count <= 0;
}

/// <summary>A side of the roast as it looks, from pale to charred: all a cook has to judge it by.</summary>
public enum Crust : byte
{
    Pale,
    Colouring,
    Golden,
    Dark,
    Charred,
}

/// <summary>
/// The spit by the kitchen fire (docs/DESIGN.md, "Cook"): raw meat on it, turned on cue. The side
/// to the fire browns, and the inside cooks all the while. Fat drips and the fire flares now and
/// then, browning the side over it twice as fast: turn it away. Lift it when every side's golden
/// and it's cooked through; a pale one's underdone, a charred one tastes burnt. Left to itself the
/// side to the fire chars (<see cref="RoastSystem"/>). A roast is a dish of its own, priced apart
/// on the sign (<see cref="Shops.Menu"/>).
/// </summary>
public static class Roasting
{
    /// <summary>Most pieces a spit takes.</summary>
    public const int MostPieces = 4;

    /// <summary>A side's browning a step, to the fire: golden in about eight seconds.</summary>
    public const float BrownPerStep = 0.95f / (8 * SimClock.TicksPerSecond);

    /// <summary>The inside a step: cooked through in about thirty seconds, a side at a time.</summary>
    public const float InsidePerStep = 1f / (30 * SimClock.TicksPerSecond);

    /// <summary>How much faster a side browns over a flare.</summary>
    public const float FlareBrowning = 2.2f;

    /// <summary>The browning at which each crust starts.</summary>
    public const float ColouringAt = 0.35f, GoldenAt = 0.75f, DarkAt = 1.1f, CharredAt = 1.3f;

    /// <summary>The thing def a roast's slices are.</summary>
    public const string Dish = "Roast";

    public static Crust CrustOf(float brown) => brown switch
    {
        < ColouringAt => Crust.Pale,
        < GoldenAt => Crust.Colouring,
        < DarkAt => Crust.Golden,
        < CharredAt => Crust.Dark,
        _ => Crust.Charred,
    };

    public static string CrustLabel(Crust c) => c switch
    {
        Crust.Pale => "pale",
        Crust.Colouring => "colouring",
        Crust.Golden => "golden",
        Crust.Dark => "dark",
        _ => "charred",
    };

    /// <summary>One step at the fire: the side to it browns (faster over a flare), and the inside cooks.</summary>
    public static void Step(float[] brown, int facing, ref float inside, bool flare)
    {
        brown[facing] = Math.Min(2f, brown[facing] + BrownPerStep * (flare ? FlareBrowning : 1f));
        inside = Math.Min(1.5f, inside + InsidePerStep);
    }

    /// <summary>How good a roast is, 0..1: every side golden, none charred, and cooked through.</summary>
    public static float QualityOf(float[] brown, float inside)
    {
        // By the crust it shows, so what the cook sees is what the diner gets.
        var sides = 0f;
        foreach (var b in brown)
            sides += CrustOf(b) switch
            {
                Crust.Golden => 1f,
                Crust.Dark => 0.65f,
                Crust.Colouring => 0.55f,
                Crust.Pale => 0.15f,
                _ => 0f,
            };
        var through = Math.Clamp(inside, 0f, 1f);
        return 0.65f * sides / brown.Length + 0.35f * through * through;
    }

    /// <summary>What most lets a roast down, as a diner would taste it.</summary>
    public static DishFault FaultOf(float[] brown, float inside)
    {
        if (brown.Any(b => b >= CharredAt))
            return DishFault.Caught;
        if (inside < 0.85f || brown.Average() < ColouringAt)
            return DishFault.Underdone;
        return DishFault.None;
    }

    public static bool IsSpit(Simulation sim, Entity e) =>
        !e.IsNull && sim.World.IsAlive(e) && sim.World.TryGet<Thing>(e, out var t) && t.Def.Building is { Spit: true } && !sim.World.Has<Construction>(e);

    /// <summary>The spit's roast; made the first time anyone looks.</summary>
    public static SpitRoast RoastOf(Simulation sim, Entity spit)
    {
        if (!sim.World.TryGet<SpitRoast>(spit, out var r))
            sim.World.Add(spit, r = new SpitRoast());
        return r;
    }

    /// <summary>Someone has the spit in hand.</summary>
    public static bool BeingWorked(Simulation sim, Entity spit)
    {
        foreach (var e in sim.World.Each<WorkSession>())
            if (sim.World.Get<WorkSession>(e) is RoastSession r && r.Spit == spit)
                return true;
        return false;
    }

    /// <summary>Whole raw meat in hand, for the spit; or Entity.Null.</summary>
    private static Entity Joint(Simulation sim, Entity cook)
    {
        var held = sim.World.Get<Carrying>(cook).Item;
        return !held.IsNull && sim.World.IsAlive(held) && sim.World.Get<Thing>(held).Def.Name == "Meat" ? held : Entity.Null;
    }

    /// <summary>What the work key does at a spit: put meat on it, or turn what's on it. Null if nothing.</summary>
    public static string? StepAt(Simulation sim, Entity cook, Entity spit)
    {
        if (!IsSpit(sim, spit))
            return null;
        var loaded = sim.World.TryGet<SpitRoast>(spit, out var r) && !r.Empty;
        if (loaded)
            return "Turn the roast";
        return Joint(sim, cook).IsNull ? null : "Put the meat on the spit";
    }

    /// <summary>Load the spit from the meat in hand (up to <see cref="MostPieces"/>) and start turning it, or turn what's on it.</summary>
    public static void Start(Simulation sim, Entity cook, Entity spit)
    {
        var w = sim.World;
        if (!IsSpit(sim, spit) || !AvatarSystem.InReach(sim, w.Get<Position>(cook).Cell, spit))
            return;
        var cell = w.Get<Thing>(spit).Cell;
        if (!Cooking.Lit(sim, spit))
        {
            sim.Messages.Add(MessageKind.Info, "The fire's out: a spit wants a fire beside it.", cell);
            return;
        }
        var r = RoastOf(sim, spit);
        if (r.Empty)
        {
            if (Joint(sim, cook) is not { IsNull: false } meat)
                return;
            var n = Math.Min(MostPieces, w.Get<Stack>(meat).Count);
            var used = sim.TakeFromStack(meat, n);
            r.Rot = FoodPoisoning.RotOf(sim, used);
            ref var hands = ref w.Get<Carrying>(cook);
            if (hands.Item == used)
                hands.Item = Entity.Null;
            w.Destroy(used);
            r.Count = n;
            Array.Clear(r.Brown);
            r.Inside = 0f;
            r.Facing = 0;
            sim.Messages.Add(MessageKind.Info, $"Put {n} meat on the spit.", cell);
        }
        WorkSystem.Start(sim, cook, RoastSession.For(sim, spit));
    }

    /// <summary>Lift the roast off: its slices into the cook's hands, as good as it was roasted, half as far gone (cooking does that).</summary>
    public static Entity Lift(Simulation sim, Entity cook, Entity spit)
    {
        var w = sim.World;
        var r = RoastOf(sim, spit);
        if (r.Empty)
            return Entity.Null;
        var item = sim.CreateItem(sim.Defs.Thing(Dish), r.Count);
        var q = QualityOf(r.Brown, r.Inside);
        w.Add(item, new Crafted { Quality = Crafting.QualityFrom(q), Maker = cook });
        w.Add(item, new Dish { Flavour = new Flavour(1f, 0, 0, 0), Fault = FaultOf(r.Brown, r.Inside) });
        if (w.Has<Rot>(item))
            w.Get<Rot>(item).Progress = r.Rot * 0.5f;
        r.Count = 0;
        ref var hands = ref w.Get<Carrying>(cook);
        if (hands.Item.IsNull)
            hands.Item = item;
        else
            Inventory.Stow(sim, cook, item);
        return item;
    }
}

/// <summary>
/// Turning the roast (<see cref="Roasting"/>): F turns the next side to the fire, Space lifts it
/// off. The crust shows; the inside doesn't. Fat flares now and then: turn away from it. Walk off
/// and it's left on the spit, the side to the fire still browning.
/// </summary>
public sealed class RoastSession : WorkSession
{
    /// <summary>The chance a step that fat drips and the fire flares: about every six seconds.</summary>
    public const float FlareChance = 1f / (6 * SimClock.TicksPerSecond);

    /// <summary>How long a flare lasts, in steps.</summary>
    public const int FlareSteps = 90;

    public Entity Spit { get; set; } = Entity.Null;
    public bool Lit { get; private set; }
    public float[] Brown { get; private set; } = new float[SpitRoast.Sides];

    /// <summary>Hidden: how far it's cooked through. Shown by nothing but the result.</summary>
    internal float Inside { get; private set; }

    public int Facing { get; private set; }

    /// <summary>Steps left of a flare under the side to the fire (0: none).</summary>
    public int Flaring { get; private set; }

    /// <summary>Lifted off (the session ends).</summary>
    public bool Lifted { get; private set; }

    public override int3? Focus(Simulation sim) => sim.World.IsAlive(Spit) && sim.World.TryGet<Thing>(Spit, out var t) ? t.Cell : null;

    public override Skill Skill => Skill.Cooking;
    public override float Xp => 25f;
    public override string Title => "Turning the roast";

    internal static RoastSession For(Simulation sim, Entity spit)
    {
        var r = Roasting.RoastOf(sim, spit);
        return new RoastSession { Spit = spit, Lit = Cooking.Lit(sim, spit), Brown = (float[])r.Brown.Clone(), Inside = r.Inside, Facing = r.Facing };
    }

    public override bool StillValid(Simulation sim) =>
        Roasting.IsSpit(sim, Spit) && !Roasting.RoastOf(sim, Spit).Empty && Cooking.Lit(sim, Spit) == Lit
        && AvatarSystem.InReach(sim, sim.World.Get<Position>(Worker).Cell, Spit);

    protected override void Press()
    {
        Facing = (Facing + 1) % SpitRoast.Sides;
        Flaring = 0; // the drip falls on the next side, and the flare dies down
    }

    protected override void Act()
    {
        Lifted = true;
        Done = true;
    }

    protected override void Step()
    {
        if (!Lit || Done)
            return;
        if (Flaring > 0)
            Flaring--;
        else if (Rand.NextFloat() < FlareChance)
            Flaring = FlareSteps;
        var inside = Inside;
        Roasting.Step(Brown, Facing, ref inside, Flaring > 0);
        Inside = inside;
    }

    public override void Finish(Simulation sim)
    {
        WriteBack(sim);
        var item = Roasting.Lift(sim, Worker, Spit);
        if (item.IsNull)
            return;
        var w = sim.World;
        var fault = w.Get<Dish>(item).Fault;
        var said = fault switch
        {
            DishFault.Caught => " One side's charred.",
            DishFault.Underdone => " It's carved pink, mind.",
            _ => "",
        };
        sim.Messages.Add(MessageKind.Info,
            $"Lifted off a {Crafting.QualityLabel(w.Get<Crafted>(item).Quality)} roast: {w.Get<Stack>(item).Count} slices.{said}", w.Get<Thing>(Spit).Cell);
    }

    public override void Interrupted(Simulation sim) => WriteBack(sim);

    private void WriteBack(Simulation sim)
    {
        if (!Roasting.IsSpit(sim, Spit))
            return;
        var r = Roasting.RoastOf(sim, Spit);
        if (r.Empty)
            return;
        Array.Copy(Brown, r.Brown, SpitRoast.Sides);
        r.Inside = Inside;
        r.Facing = Facing;
    }

    protected override void Cloned() => Brown = (float[])Brown.Clone();

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Spit);
        w.Bool(Lit);
        foreach (var b in Brown)
            w.Float(b);
        w.Float(Inside);
        w.Int(Facing);
        w.Int(Flaring);
        w.Bool(Lifted);
    }

    protected override void ReadState(SaveReader r)
    {
        Spit = r.Entity();
        Lit = r.Bool();
        for (var i = 0; i < SpitRoast.Sides; i++)
            Brown[i] = Math.Clamp(r.Float(), 0f, 2f);
        Inside = Math.Clamp(r.Float(), 0f, 1.5f);
        Facing = Math.Clamp(r.Int(), 0, SpitRoast.Sides - 1);
        Flaring = Math.Clamp(r.Int(), 0, FlareSteps);
        Lifted = r.Bool();
    }
}

/// <summary>A roast left on the spit carries on: the side to the fire browning, and charring, while the fire burns.</summary>
public sealed class RoastSystem : ISimSystem
{
    public string Name => "Roasting";
    public int Interval => 10;

    public void Update(Simulation sim)
    {
        var w = sim.World;
        if (w.Count<SpitRoast>() == 0)
            return;
        foreach (var e in w.Query<SpitRoast>())
        {
            var r = w.Get<SpitRoast>(e);
            if (r.Empty || !w.Has<Thing>(e) || Roasting.BeingWorked(sim, e) || !Cooking.Lit(sim, e))
                continue;
            for (var i = 0; i < Interval; i++)
                Roasting.Step(r.Brown, r.Facing, ref r.Inside, false);
        }
    }
}

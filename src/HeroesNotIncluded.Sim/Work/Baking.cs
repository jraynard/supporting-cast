using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Work;

/// <summary>A ball of dough (<see cref="KneadSession"/>): how well it was kneaded, and when, which says how far it's proved.</summary>
public struct Dough
{
    /// <summary>How well kneaded, 0..1: smooth and springy, or lumpy.</summary>
    public float Knead;

    /// <summary>When it was kneaded: it proves (rises) from then, and over-proves if left too long.</summary>
    public long MadeAt;

    public Entity Maker;
}

/// <summary>A loaf in the oven: how it was kneaded and proved, and how far it's baked (hidden: only smelt).</summary>
public sealed class OvenLoad
{
    public int Loaves;
    public float Knead;

    /// <summary>Hours it proved before it went in.</summary>
    public float Proved;

    /// <summary>How far baked, 0 raw to 1 a golden crust and past it to burnt. Never shown: smelt (<see cref="Baking.SmellOf"/>).</summary>
    public float Bake;

    public Entity Maker;

    public bool Empty => Loaves <= 0;
}

/// <summary>
/// Bread from the oven (docs/DESIGN.md, "Cook"). Flour is kneaded at the prep table on the beat
/// (<see cref="KneadSession"/>), and the dough proves by itself over a game hour or two, read by
/// eye (slack, rising, well risen, puffed up, sunk). Into the oven, it's baked by smell alone:
/// raw, yeasty, baking, a golden crust, toasty, burning (<see cref="BakeSession"/>). Take it out on
/// the golden crust. Left in, it burns (<see cref="OvenSystem"/>). Bread is a dish on the menu
/// (<see cref="Shops.Menu"/>), and keeps: rations with bread in them turn half as fast.
/// </summary>
public static class Baking
{
    public const string Flour = "Flour", DoughDef = "Dough", Bread = "Bread";

    /// <summary>Flour a ball of dough takes, and loaves it bakes into.</summary>
    public const int FlourPerDough = 2, LoavesPerDough = 2;

    /// <summary>Proving, in game hours: rising from, well risen from, puffed up from, sunk from.</summary>
    public const float RisingAt = 0.5f, RisenAt = 1f, PuffedAt = 2f, SunkAt = 3f;

    /// <summary>Baking a step: a golden crust in about twenty seconds.</summary>
    public const float BakePerStep = 1f / (20 * SimClock.TicksPerSecond);

    /// <summary>The bake at which each smell starts.</summary>
    public const float YeastyAt = 0.3f, BakingAt = 0.6f, GoldenAt = 0.88f, ToastyAt = 1.1f, BurningAt = 1.3f;

    /// <summary>How the dough looks after so many hours' proving.</summary>
    public static string RiseOf(float hours) => hours switch
    {
        < RisingAt => "slack",
        < RisenAt => "rising",
        < PuffedAt => "well risen",
        < SunkAt => "puffed up tall",
        _ => "sunk back on itself",
    };

    /// <summary>What the oven smells of, baked this far.</summary>
    public static string SmellOf(float bake) => bake switch
    {
        < YeastyAt => "raw dough",
        < BakingAt => "warm and yeasty",
        < GoldenAt => "bread baking",
        < ToastyAt => "a golden crust",
        < BurningAt => "toasty, getting dark",
        _ => "burning!",
    };

    /// <summary>Hours a ball of dough has proved by now.</summary>
    public static float ProvedHours(Simulation sim, Dough d) => (sim.Clock.Tick - d.MadeAt) / (float)SimClock.TicksPerHour;

    /// <summary>How good a bake is, 0..1: kneaded well, proved well (an hour or two), and out on the golden crust.</summary>
    public static float QualityOf(OvenLoad o)
    {
        var prove = o.Proved switch
        {
            < RisingAt => 0.2f,
            < RisenAt => 0.6f,
            < PuffedAt => 1f,
            < SunkAt => 0.6f,
            _ => 0.1f,
        };
        var bake = o.Bake switch
        {
            < BakingAt => 0.1f,
            < GoldenAt => 0.6f,
            < ToastyAt => 1f,
            < BurningAt => 0.55f,
            _ => 0f,
        };
        return 0.3f * Math.Clamp(o.Knead, 0f, 1f) + 0.3f * prove + 0.4f * bake;
    }

    /// <summary>What most lets a loaf down, as whoever eats it can taste.</summary>
    public static DishFault FaultOf(OvenLoad o) =>
        o.Bake >= BurningAt || o.Bake >= ToastyAt + 0.1f ? DishFault.Caught
        : o.Bake < GoldenAt - 0.1f ? DishFault.Underdone
        : o.Proved >= SunkAt || o.Proved < RisingAt || o.Knead < 0.4f ? DishFault.Heavy
        : DishFault.None;

    public static bool IsOven(Simulation sim, Entity e) =>
        !e.IsNull && sim.World.IsAlive(e) && sim.World.TryGet<Thing>(e, out var t) && t.Def.Building is { Oven: true } && !sim.World.Has<Construction>(e);

    /// <summary>The oven's own fire is burning.</summary>
    public static bool Lit(Simulation sim, Entity oven) => sim.World.TryGet<Fueled>(oven, out var f) && f.Fuel > 0;

    public static OvenLoad LoadOf(Simulation sim, Entity oven)
    {
        if (!sim.World.TryGet<OvenLoad>(oven, out var o))
            sim.World.Add(oven, o = new OvenLoad());
        return o;
    }

    public static bool BeingWorked(Simulation sim, Entity oven)
    {
        foreach (var e in sim.World.Each<WorkSession>())
            if (sim.World.Get<WorkSession>(e) is BakeSession b && b.Oven == oven)
                return true;
        return false;
    }

    /// <summary>Dough in hand, or Entity.Null.</summary>
    private static Entity DoughIn(Simulation sim, Entity cook)
    {
        var held = sim.World.Get<Carrying>(cook).Item;
        return !held.IsNull && sim.World.IsAlive(held) && sim.World.Has<Dough>(held) ? held : Entity.Null;
    }

    /// <summary>Flour in hand, enough for a ball of dough, or Entity.Null.</summary>
    public static Entity FlourIn(Simulation sim, Entity cook)
    {
        var held = sim.World.Get<Carrying>(cook).Item;
        return !held.IsNull && sim.World.IsAlive(held) && sim.World.Get<Thing>(held).Def.Name == Flour
            && sim.World.Get<Stack>(held).Count >= FlourPerDough ? held : Entity.Null;
    }

    /// <summary>What the work key does at an oven: put the dough in, or see to what's baking. Null if nothing.</summary>
    public static string? StepAt(Simulation sim, Entity cook, Entity oven)
    {
        if (!IsOven(sim, oven))
            return null;
        if (sim.World.TryGet<OvenLoad>(oven, out var o) && !o.Empty)
            return "Smell the bread";
        return DoughIn(sim, cook).IsNull ? null : "Put the dough in the oven";
    }

    /// <summary>Put the dough in hand in the oven and stand by it, or stand by what's in it.</summary>
    public static void Start(Simulation sim, Entity cook, Entity oven)
    {
        var w = sim.World;
        if (!IsOven(sim, oven) || !AvatarSystem.InReach(sim, w.Get<Position>(cook).Cell, oven))
            return;
        var cell = w.Get<Thing>(oven).Cell;
        if (!Lit(sim, oven))
        {
            sim.Messages.Add(MessageKind.Info, "The oven's cold: it wants wood.", cell);
            return;
        }
        var o = LoadOf(sim, oven);
        if (o.Empty)
        {
            if (DoughIn(sim, cook) is not { IsNull: false } dough)
                return;
            var d = w.Get<Dough>(dough);
            (o.Loaves, o.Knead, o.Proved, o.Bake, o.Maker) = (LoavesPerDough, d.Knead, ProvedHours(sim, d), 0f, cook);
            ref var hands = ref w.Get<Carrying>(cook);
            if (hands.Item == dough)
                hands.Item = Entity.Null;
            sim.DestroyThing(dough);
            sim.Messages.Add(MessageKind.Info, $"The dough goes in, {RiseOf(o.Proved)}.", cell);
        }
        WorkSystem.Start(sim, cook, BakeSession.For(sim, oven));
    }

    /// <summary>The bread comes out: loaves into the cook's hands, as good as it was made.</summary>
    public static Entity TakeOut(Simulation sim, Entity cook, Entity oven)
    {
        var w = sim.World;
        var o = LoadOf(sim, oven);
        if (o.Empty)
            return Entity.Null;
        var item = sim.CreateItem(sim.Defs.Thing(Bread), o.Loaves);
        w.Add(item, new Crafted { Quality = Crafting.QualityFrom(QualityOf(o)), Maker = o.Maker.IsNull ? cook : o.Maker });
        w.Add(item, new Dish { Flavour = Flavour.Of(sim.Defs.Thing(Bread).Flavour).Shares, Fault = FaultOf(o) });
        o.Loaves = 0;
        ref var hands = ref w.Get<Carrying>(cook);
        if (hands.Item.IsNull)
            hands.Item = item;
        else
            Inventory.Stow(sim, cook, item);
        return item;
    }
}

/// <summary>
/// Kneading at the prep table (<see cref="Baking"/>): flour in hand, folded and pushed on the beat,
/// from one side then the other (A, D, ...). On the beat and from the right side it comes smooth
/// and springy; off it, lumpy. Done, the flour's a ball of dough, proving from then.
/// </summary>
public sealed class KneadSession : WorkSession
{
    public const int Pushes = 8;

    /// <summary>Steps between beats.</summary>
    public const int Beat = 36;

    /// <summary>How near the beat a push counts as on it, in steps, before practice widens it.</summary>
    public const float Window = 5f;

    public Entity Table { get; set; } = Entity.Null;
    public Entity Flour { get; set; } = Entity.Null;
    public int Pushed { get; private set; }
    public int LastMark { get; private set; }

    public int Phase => StepsDone % Beat;
    public int Off => Math.Min(Phase, Beat - Phase);

    /// <summary>The side the next push comes from: -1 (A) or +1 (D).</summary>
    public int NextSide => Pushed % 2 == 0 ? -1 : 1;

    public override int3? Focus(Simulation sim) => sim.World.IsAlive(Table) && sim.World.TryGet<Thing>(Table, out var t) ? t.Cell : null;
    public override Skill Skill => Skill.Cooking;
    public override float Xp => 10f;
    public override string Title => "Kneading the dough";

    public override bool StillValid(Simulation sim) =>
        Rations.IsPrepTable(sim, Table) && Baking.FlourIn(sim, Worker) == Flour && !Flour.IsNull
        && AvatarSystem.InReach(sim, sim.World.Get<Position>(Worker).Cell, Table);

    protected override void Axis(int value)
    {
        if (value == 0 || Done)
            return;
        var onBeat = Off <= Window * Hands.Tolerance;
        var mark = onBeat && value == NextSide ? 1f : onBeat || value == NextSide ? 0.45f : 0.15f;
        LastMark = mark >= 1f ? 1 : 0;
        Sample(mark);
        Pushed++;
        if (Pushed >= Pushes)
            Done = true;
    }

    protected override void Step() { }

    public override void Finish(Simulation sim)
    {
        var w = sim.World;
        if (Baking.FlourIn(sim, Worker) is not { IsNull: false } flour)
            return;
        var used = sim.TakeFromStack(flour, Baking.FlourPerDough);
        ref var hands = ref w.Get<Carrying>(Worker);
        if (hands.Item == used)
            hands.Item = Entity.Null;
        w.Destroy(used);
        var dough = sim.CreateItem(sim.Defs.Thing(Baking.DoughDef), 1);
        w.Add(dough, new Dough { Knead = Quality, MadeAt = sim.Clock.Tick, Maker = Worker });
        if (w.Get<Carrying>(Worker).Item.IsNull)
            w.Get<Carrying>(Worker).Item = dough;
        else
            Inventory.Stow(sim, Worker, dough);
        sim.Messages.Add(MessageKind.Info, Quality >= 0.75f ? "The dough's smooth and springy. Let it prove an hour or two."
            : "The dough's a bit lumpy. Let it prove an hour or two.", w.Get<Thing>(Table).Cell);
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Table);
        w.Entity(Flour);
        w.Int(Pushed);
        w.Int(LastMark);
    }

    protected override void ReadState(SaveReader r)
    {
        Table = r.Entity();
        Flour = r.Entity();
        Pushed = Math.Clamp(r.Int(), 0, Pushes);
        LastMark = Math.Clamp(r.Int(), 0, 1);
    }
}

/// <summary>
/// Standing by the oven (<see cref="Baking"/>): it bakes while its fire burns, and all the baker
/// has to go on is the smell (no bar, no colour: the door's shut). Space takes it out. Walk away
/// and it keeps baking.
/// </summary>
public sealed class BakeSession : WorkSession
{
    public Entity Oven { get; set; } = Entity.Null;
    public bool Lit { get; private set; }

    /// <summary>Hidden: how far baked. Only <see cref="Smell"/> leaves the session.</summary>
    internal float Bake { get; private set; }

    public string Smell => Baking.SmellOf(Bake);

    public override int3? Focus(Simulation sim) => sim.World.IsAlive(Oven) && sim.World.TryGet<Thing>(Oven, out var t) ? t.Cell : null;
    public override Skill Skill => Skill.Cooking;
    public override float Xp => 20f;
    public override string Title => "Baking";

    internal static BakeSession For(Simulation sim, Entity oven) =>
        new() { Oven = oven, Lit = Baking.Lit(sim, oven), Bake = Baking.LoadOf(sim, oven).Bake };

    public override bool StillValid(Simulation sim) =>
        Baking.IsOven(sim, Oven) && !Baking.LoadOf(sim, Oven).Empty && Baking.Lit(sim, Oven) == Lit
        && AvatarSystem.InReach(sim, sim.World.Get<Position>(Worker).Cell, Oven);

    protected override void Act() => Done = true;

    protected override void Step()
    {
        if (Lit && !Done)
            Bake = Math.Min(2f, Bake + Baking.BakePerStep);
    }

    public override void Finish(Simulation sim)
    {
        WriteBack(sim);
        var item = Baking.TakeOut(sim, Worker, Oven);
        if (item.IsNull)
            return;
        var w = sim.World;
        var said = w.Get<Dish>(item).Fault switch
        {
            DishFault.Caught => " The crust's burnt.",
            DishFault.Underdone => " It's doughy in the middle.",
            DishFault.Heavy => " It's come out heavy.",
            _ => "",
        };
        sim.Messages.Add(MessageKind.Info, $"Out of the oven: {w.Get<Stack>(item).Count} {Crafting.QualityLabel(w.Get<Crafted>(item).Quality)} loaves.{said}", w.Get<Thing>(Oven).Cell);
    }

    public override void Interrupted(Simulation sim) => WriteBack(sim);

    private void WriteBack(Simulation sim)
    {
        if (Baking.IsOven(sim, Oven) && Baking.LoadOf(sim, Oven) is { Empty: false } o)
            o.Bake = Bake;
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Oven);
        w.Bool(Lit);
        w.Float(Bake);
    }

    protected override void ReadState(SaveReader r)
    {
        Oven = r.Entity();
        Lit = r.Bool();
        Bake = Math.Clamp(r.Float(), 0f, 2f);
    }
}

/// <summary>Bread left in the oven carries on baking while the oven's fire burns, and burns.</summary>
public sealed class OvenSystem : ISimSystem
{
    public string Name => "Baking";
    public int Interval => 10;

    public void Update(Simulation sim)
    {
        var w = sim.World;
        if (w.Count<OvenLoad>() == 0)
            return;
        foreach (var e in w.Query<OvenLoad>())
        {
            var o = w.Get<OvenLoad>(e);
            if (o.Empty || !w.Has<Thing>(e) || Baking.BeingWorked(sim, e) || !Baking.Lit(sim, e))
                continue;
            o.Bake = Math.Min(2f, o.Bake + Baking.BakePerStep * Interval);
        }
    }
}

using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Work;

/// <summary>An unbaked pie (<see cref="Pies"/>): what's in it, and how well its rim was crimped.</summary>
public sealed class RawPie
{
    /// <summary>What the filling tastes of, plain to anyone.</summary>
    public Flavour Filling;

    /// <summary>How well the rim's crimped, 0..1: a poor rim lets the filling out in the oven.</summary>
    public float Crimp;

    public Entity Maker;
}

/// <summary>
/// Pies (docs/DESIGN.md, "Cook"): a ball of dough rolled out at the prep table, two portions of raw
/// food in it, and its rim crimped all the way round on the beat (<see cref="CrimpSession"/>). It
/// bakes in the oven by smell, as bread does (<see cref="Baking"/>), into slices: a dish on the menu
/// tasting of its filling. A rim crimped badly lets the filling run out.
/// </summary>
public static class Pies
{
    public const string Raw = "RawPie", Pie = "Pie";

    /// <summary>Portions of filling a pie takes, and the slices it bakes into.</summary>
    public const int Filling = 2, Slices = 3;

    /// <summary>How much of a pie's taste is its crust (savoury and sweet alike), the rest its filling.</summary>
    public const float Crust = 0.3f;

    /// <summary>What can fill a pie: raw food (not a seasoner, a drink or a dish).</summary>
    public static bool IsFilling(ThingDef def) => def.IsFood && !def.Cooked && !def.Drink && !def.Rations && !def.IsSeasoner;

    /// <summary>The stacks of filling on someone, hand first, in a fixed order.</summary>
    public static List<Entity> FillingOn(Simulation sim, Entity who)
    {
        var w = sim.World;
        var list = new List<Entity>();
        if (w.TryGet<Carrying>(who, out var c) && !c.Item.IsNull && w.IsAlive(c.Item) && IsFilling(w.Get<Thing>(c.Item).Def))
            list.Add(c.Item);
        if (sim.PackOf(who) is { IsNull: false } pack)
            foreach (var item in sim.StoredIn(pack))
                if (IsFilling(w.Get<Thing>(item).Def))
                    list.Add(item);
        return list;
    }

    public static int FillingPortionsOn(Simulation sim, Entity who) => FillingOn(sim, who).Sum(e => sim.World.Get<Stack>(e).Count);

    /// <summary>Everything a pie needs is on the cook: dough and the filling.</summary>
    public static bool CanMake(Simulation sim, Entity cook) =>
        !Baking.DoughIn(sim, cook).IsNull && FillingPortionsOn(sim, cook) >= Filling;

    /// <summary>A raw pie on the cook (in hand, else in the pack), or Entity.Null.</summary>
    public static Entity RawIn(Simulation sim, Entity cook) => AvatarSystem.SupplyItem(sim, cook, e => sim.World.Has<RawPie>(e));

    /// <summary>What a baked pie tastes of: its filling, under the crust.</summary>
    public static Flavour Baked(Flavour filling) => filling.Shares * (1f - Crust) + new Flavour(0.5f, 0.5f, 0f, 0f) * Crust;

    /// <summary>
    /// The pie made up: the dough and the first <see cref="Filling"/> portions of filling off the
    /// cook, a raw pie back in their hands (or pack). Its rot is the filling's.
    /// </summary>
    internal static Entity Make(Simulation sim, Entity cook, float crimp)
    {
        var w = sim.World;
        if (Baking.DoughIn(sim, cook) is not { IsNull: false } dough || FillingPortionsOn(sim, cook) < Filling)
            return Entity.Null;
        var flavour = default(Flavour);
        var rot = 0f;
        var need = Filling;
        ref var hands = ref w.Get<Carrying>(cook);
        foreach (var stack in FillingOn(sim, cook))
        {
            if (need <= 0)
                break;
            var def = w.Get<Thing>(stack).Def;
            var take = Math.Min(need, w.Get<Stack>(stack).Count);
            rot += FoodPoisoning.RotOf(sim, stack) * take;
            flavour += Flavour.Of(def.Flavour) * (def.Nutrition * take);
            var used = sim.TakeFromStack(stack, take);
            if (hands.Item == used)
                hands.Item = Entity.Null;
            w.Destroy(used);
            need -= take;
        }
        if (w.Get<Carrying>(cook).Item == dough)
            w.Get<Carrying>(cook).Item = Entity.Null;
        sim.DestroyThing(sim.TakeFromStack(dough, 1));
        var pie = sim.CreateItem(sim.Defs.Thing(Raw), 1);
        w.Add(pie, new RawPie { Filling = flavour.Shares, Crimp = Math.Clamp(crimp, 0f, 1f), Maker = cook });
        if (w.Has<Rot>(pie))
            w.Get<Rot>(pie).Progress = rot / Filling;
        if (w.Get<Carrying>(cook).Item.IsNull)
            w.Get<Carrying>(cook).Item = pie;
        else
            Inventory.Stow(sim, cook, pie);
        return pie;
    }
}

/// <summary>
/// Making a pie at the prep table: the dough's rolled out and filled, and the rim crimped all the
/// way round. A pinch goes round the rim; F as it reaches each mark crimps it shut there. Off the
/// mark, the rim's left loose, and the filling finds the gap in the oven. A press a moment after the
/// mark still counts (<see cref="LateSteps"/>): the eye and the screen lag the hand.
/// </summary>
public sealed class CrimpSession : WorkSession
{
    /// <summary>Crimps round the rim.</summary>
    public const int Marks = 8;

    /// <summary>Seconds the pinch takes to go once round.</summary>
    public const float Round = 4f;

    /// <summary>How near a mark (in marks) a crimp counts as on it, before practice widens it.</summary>
    public const float Window = 0.16f;

    /// <summary>Steps after a mark's window that a crimp still lands on it.</summary>
    public const int LateSteps = 2;

    /// <summary><see cref="LateSteps"/> in marks.</summary>
    public static float LateGrace => LateSteps * Marks / (Round * SimClock.TicksPerSecond);

    public Entity Table { get; set; } = Entity.Null;

    public int Crimped { get; private set; }

    /// <summary>How each crimp went, for the drawing: 1 tight on its mark, 0 loose.</summary>
    public int[] Crimps { get; private set; } = new int[Marks];

    /// <summary>Where the pinch is round the rim, in marks (0..<see cref="Marks"/>).</summary>
    public float At => StepsDone / (Round * SimClock.TicksPerSecond) % 1f * Marks;

    /// <summary>How far the pinch is from the nearest mark, in marks.</summary>
    public float Off => MathF.Abs(At - MathF.Round(At));

    public override int3? Focus(Simulation sim) => sim.World.IsAlive(Table) && sim.World.TryGet<Thing>(Table, out var t) ? t.Cell : null;
    public override Skill Skill => Skill.Cooking;
    public override float Xp => 12f;
    public override string Title => "Making a pie";

    public override bool StillValid(Simulation sim) =>
        Rations.IsPrepTable(sim, Table) && Pies.CanMake(sim, Worker)
        && AvatarSystem.InReach(sim, sim.World.Get<Position>(Worker).Cell, Table);

    protected override void Press()
    {
        if (Done)
            return;
        var window = Window * Hands.Tolerance;
        var late = At - MathF.Round(At);
        var off = late > 0f ? Math.Max(0f, late - LateGrace) : -late;
        var tight = off <= window;
        Crimps[Crimped] = tight ? 1 : 0;
        Sample(tight ? 1f - off / window * 0.3f : 0.25f);
        Crimped++;
        if (Crimped >= Marks)
            Done = true;
    }

    protected override void Step() { }

    public override void Finish(Simulation sim)
    {
        var pie = Pies.Make(sim, Worker, Quality);
        if (pie.IsNull)
            return;
        var loose = Crimps.Count(c => c == 0);
        sim.Messages.Add(MessageKind.Info, loose == 0 ? "The pie's made, crimped tight all round. Into the oven with it."
            : $"The pie's made, the rim loose in {loose} place{(loose == 1 ? "" : "s")}. Into the oven with it.", sim.World.Get<Thing>(Table).Cell);
    }

    protected override void Cloned() => Crimps = (int[])Crimps.Clone();

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Table);
        w.Int(Crimped);
        foreach (var c in Crimps)
            w.Int(c);
    }

    protected override void ReadState(SaveReader r)
    {
        Table = r.Entity();
        Crimped = Math.Clamp(r.Int(), 0, Marks);
        for (var i = 0; i < Marks; i++)
            Crimps[i] = Math.Clamp(r.Int(), 0, 1);
    }
}

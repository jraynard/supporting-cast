using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Afterlife;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Miner;

/// <summary>
/// A loaded barrow on the move (<see cref="LoadSession"/> set its load), balanced as it's pushed: it leans
/// (<see cref="BarrowLoad.Lean"/>, −1 over left to 1 over right) and its pusher leans it back with Q/E
/// (<see cref="BarrowLeanCommand"/>). A well-loaded barrow rights itself and wants only the odd nudge after a bump; one
/// loaded off-square drifts toward its heavy side and won't right itself (<see cref="Barrows.Risk"/> past
/// <see cref="Barrows.Steady"/>), so it must be held. Turns throw it outward, rough going (a step up or down, soft or
/// broken ground) jolts it. Stood still, it settles. Over the edge it tips: the barrow down where it is and some of the
/// load spilled from the side it went, nothing lost, only time, and another chance to load it better.
/// </summary>
public sealed class BarrowSystem : ISimSystem
{
    public string Name => "Barrows";

    /// <summary>Every other tick: the lean moves smoothly enough to hold.</summary>
    public int Interval => 2;

    /// <summary>How hard a square load rights itself, per second, before its risk takes from it.</summary>
    public const float Restore = 0.6f;

    /// <summary>How fast a load off-square across drifts toward its heavy side, per second, at a whole slot off.</summary>
    public const float Drift = 0.12f;

    /// <summary>How fast Q/E lean it back, per second.</summary>
    public const float Correct = 0.7f;

    /// <summary>The jolt of a step: on even going (either way, at most), on rough; and the throw outward on a turn.</summary>
    public const float EvenJolt = 0.03f, RoughJolt = 0.2f, TurnThrow = 0.08f;

    /// <summary>Ticks without a new cell before it counts as stood still, and how fast it settles then, per second.</summary>
    public const int StillAfter = 45;
    public const float Settle = 2f;

    /// <summary>Ground costlier to cross than this is rough going for a barrow.</summary>
    public const float RoughCost = 1.05f;

    private readonly List<(Entity Who, Entity Barrow)> _pushed = [];

    public void Update(Simulation sim)
    {
        var w = sim.World;
        _pushed.Clear();
        foreach (var who in w.Each<Carrying>())
            if (Barrows.Held(sim, who) is { IsNull: false } barrow && w.Has<BarrowLoad>(barrow) && w.Has<Position>(who))
                _pushed.Add((who, barrow));
        var dt = Interval / (float)SimClock.TicksPerSecond;
        foreach (var (who, barrow) in _pushed)
        {
            var load = w.Get<BarrowLoad>(barrow);
            if (Push(sim, load, w.Get<Position>(who).Cell, dt))
                Tip(sim, who, barrow, load);
        }
    }

    /// <summary>One step of a barrow's lean as it's pushed to (or held at) <paramref name="cell"/>; true if it's gone over.</summary>
    public static bool Push(Simulation sim, BarrowLoad load, int3 cell, float dt)
    {
        var was = load.LastCell;
        if (was != cell)
        {
            load.LastCell = cell;
            load.Still = 0;
            if (was.X >= 0)
            {
                var step = new int3(Math.Sign(cell.X - was.X), Math.Sign(cell.Y - was.Y), 0);
                var jolt = Rough(sim, was, cell) ? RoughJolt : EvenJolt;
                load.Lean += (sim.Rand.NextFloat() * 2f - 1f) * jolt;
                // A turn throws the load outward: turning right (clockwise, the map's y running south), it lurches left.
                var turn = load.LastStep.X * step.Y - load.LastStep.Y * step.X;
                load.Lean -= Math.Sign(turn) * TurnThrow;
                load.LastStep = step;
            }
        }
        else
            load.Still += BarrowSystemTicks(dt);
        if (load.Still >= StillAfter)
            load.Lean -= load.Lean * Math.Min(1f, Settle * dt);
        else
        {
            var (across, _) = Barrows.Centre(load.Pieces);
            var spring = Restore - Barrows.Risk(load.Pieces);
            load.Lean += (Drift * across - spring * load.Lean) * dt;
        }
        load.Lean += load.Nudge * Correct * dt; // leaned back the way it's pressed: E to the right, Q to the left
        load.Lean = Math.Clamp(load.Lean, -1f, 1f);
        return MathF.Abs(load.Lean) >= 1f;
    }

    private static int BarrowSystemTicks(float dt) => (int)MathF.Round(dt * SimClock.TicksPerSecond);

    /// <summary>Rough going from one cell to the next: a step up or down, or ground that's slow to cross.</summary>
    public static bool Rough(Simulation sim, int3 from, int3 to)
    {
        if (from.Z != to.Z)
            return true;
        var floor = to - new int3(0, 0, 1);
        return sim.Map.InBounds(floor) && sim.Map.Block[sim.Map.Idx(floor)].MoveCost > RoughCost;
    }

    /// <summary>
    /// The barrow goes over: down where it is, and a third of its load (at least a piece) spills out from the side it went
    /// over to, around it.
    /// </summary>
    public static void Tip(Simulation sim, Entity who, Entity barrow, BarrowLoad load)
    {
        var w = sim.World;
        var cell = w.Get<Position>(who).Cell;
        var side = load.Lean != 0f ? MathF.Sign(load.Lean) : MathF.Sign(Barrows.Centre(load.Pieces).Across);
        var spill = Math.Max(1, (load.Pieces.Count + 2) / 3);
        var spilled = load.Pieces.Select((p, i) => (p, i)).OrderByDescending(x => (Barrows.ColOf(x.p.Slot) - 1) * side).ThenByDescending(x => x.i).Take(spill).ToList();
        foreach (var (_, i) in spilled.OrderByDescending(x => x.i))
            load.Pieces.RemoveAt(i);
        w.Get<Carrying>(who).Item = Entity.Null;
        Barrows.PutDown(sim, who, barrow); // on their own level: in a tunnel, "near" alone can be the hilltop
        (load.LastCell, load.LastStep, load.Lean, load.Nudge, load.Still) = (new int3(-1, -1, -1), default, 0f, 0, 0);
        foreach (var group in spilled.GroupBy(x => x.p.Def))
            Barrows.PutDown(sim, who, sim.CreateItem(group.Key, group.Count()));
        if (load.Pieces.Count == 0)
            w.Remove<BarrowLoad>(barrow);
        sim.Messages.Tell(who, MessageKind.Warning, $"The barrow goes over to the {(side < 0 ? "left" : "right")}: {Barrows.Describe(spilled.Select(x => x.p))} spills out. Load it again, the weight square and near the wheel, and lean it back with {"Q/E"} as you go.", cell);
    }

    /// <summary>
    /// Where a barrow in <paramref name="who"/>'s hands may be tipped out at <paramref name="cell"/>: the chest standing there, if
    /// it's the smithy's (<paramref name="paid"/>: the ore's paid for there) or the miner's own shed's. Entity.Null if neither.
    /// </summary>
    public static Entity TipInto(Simulation sim, Entity who, int3 cell, out bool paid)
    {
        paid = false;
        var building = Buildings.At(sim, cell);
        if (building.IsNull || !sim.World.TryGet<PresetBuilding>(building, out var b))
            return Entity.Null;
        var chest = Stock.ChestOf(sim, building);
        if (chest.IsNull || sim.World.Get<Thing>(chest).Cell != cell)
            return Entity.Null;
        paid = b.Def.Name == OreCarterSystem.SmithyDef;
        return paid || building == ShopSystem.WorkplaceOf(sim, who) ? chest : Entity.Null;
    }

    /// <summary>Who pays for ore at a building: whoever works there (the first, by entity), Entity.Null if nobody does.</summary>
    public static Entity Buyer(Simulation sim, Entity building) =>
        sim.Creatures().Where(e => !sim.World.Has<Death>(e) && ShopSystem.WorkplaceOf(sim, e) == building)
            .OrderBy(e => e.Index).DefaultIfEmpty(Entity.Null).First();

    /// <summary>
    /// Tip the barrow in <paramref name="who"/>'s hands into <paramref name="chest"/> (what doesn't fit goes beside it). At the
    /// smithy, whoever works it pays for the load at once, so far as they've the silver; the rest is owed (<see cref="OreTally"/>).
    /// </summary>
    public static void TipOut(Simulation sim, Entity who, Entity chest, bool paid)
    {
        var w = sim.World;
        var barrow = Barrows.Held(sim, who);
        if (barrow.IsNull || !w.TryGet<BarrowLoad>(barrow, out var load) || load.Pieces.Count == 0)
            return;
        var pieces = load.Pieces.ToList();
        w.Remove<BarrowLoad>(barrow);
        Deliver(sim, who, chest, pieces.Select(p => p.Def).ToList(), paid);
    }

    /// <summary>
    /// <paramref name="pieces"/> put into <paramref name="chest"/> by <paramref name="who"/> (what doesn't fit goes beside it).
    /// At the smithy (<paramref name="paid"/>), whoever works it pays for them at once at the <c>ores</c> prices, so far as
    /// they've the silver; the rest is owed (<see cref="OreTally"/>). A barrow tipped, or an AI miner's armful.
    /// </summary>
    public static void Deliver(Simulation sim, Entity who, Entity chest, IReadOnlyList<HeroesNotIncluded.Sim.Defs.ThingDef> pieces, bool paid)
    {
        var w = sim.World;
        var cell = w.Get<Thing>(chest).Cell;
        foreach (var group in pieces.GroupBy(d => d))
        {
            var rest = sim.Store(chest, sim.CreateItem(group.Key, group.Count()));
            if (!rest.IsNull)
                sim.PlaceItemNear(rest, cell);
        }
        var what = Barrows.Describe(pieces.Select(d => new BarrowPiece(d, 0)));
        if (!paid)
        {
            sim.Messages.Tell(who, MessageKind.Info, $"You tip {what} into the chest.", cell);
            return;
        }
        var worth = Barrows.Worth(sim, pieces.Select(d => new BarrowPiece(d, 0)));
        var buyer = Buyer(sim, Buildings.At(sim, cell));
        if (buyer.IsNull || worth <= 0 || buyer == who)
        {
            sim.Messages.Tell(who, MessageKind.Info, $"You tip {what} into the smithy's chest. Nobody works the smithy to pay for it.", cell);
            return;
        }
        var got = Coin.Pay(sim, buyer, who, worth);
        var name = w.Get<Creature>(who).Name;
        var smith = w.Get<Creature>(buyer).Name;
        if (got < worth)
            sim.System<OreTally>().Owe(buyer, who, worth - got);
        sim.Messages.Tell(who, MessageKind.Good, got == worth
            ? $"You tip {what} into the smithy's chest. {smith} pays you {worth} silver."
            : $"You tip {what} into the smithy's chest. {smith} pays you {got} of its {worth} silver, and owes you the rest.", cell);
        sim.Messages.Tell(buyer, MessageKind.Info, got == worth
            ? $"{name} tipped {what} into your chest, and you paid {worth} silver for it."
            : $"{name} tipped {what} into your chest. You paid {got} of its {worth} silver, and owe them the rest.", cell);
    }
}

/// <summary>
/// Silver owed for ore (<see cref="BarrowSystem.TipOut"/>) by a smith who hadn't it to hand, paid as soon as they have: so
/// the miner's never short for a smith's empty purse, only kept waiting. Kept in the order owed.
/// </summary>
public sealed class OreTally : ISimSystem, ISaveState
{
    public string Name => "OreTally";

    public int Interval => SimClock.TicksPerHour / 4;

    private readonly List<(Entity From, Entity To, int Silver)> _owed = [];

    public IReadOnlyList<(Entity From, Entity To, int Silver)> Owed => _owed;

    public int OwedBy(Entity from, Entity to) => _owed.Where(d => d.From == from && d.To == to).Sum(d => d.Silver);

    public void Owe(Entity from, Entity to, int silver)
    {
        if (silver > 0)
            _owed.Add((from, to, silver));
    }

    public void Update(Simulation sim)
    {
        var w = sim.World;
        for (var k = 0; k < _owed.Count; k++)
        {
            var (from, to, silver) = _owed[k];
            if (!w.IsAlive(from) || !w.IsAlive(to) || w.Has<Death>(from) || w.Has<Death>(to))
            {
                _owed.RemoveAt(k--);
                continue;
            }
            var paid = Coin.Pay(sim, from, to, silver);
            if (paid <= 0)
                continue;
            var cell = w.Get<Position>(to).Cell;
            sim.Messages.Tell(to, MessageKind.Good, $"{w.Get<Creature>(from).Name} pays you {paid} silver owed for ore.", cell);
            sim.Messages.Tell(from, MessageKind.Info, $"You pay {w.Get<Creature>(to).Name} {paid} silver owed for ore.", cell);
            if (paid >= silver)
                _owed.RemoveAt(k--);
            else
                _owed[k] = (from, to, silver - paid);
        }
    }

    public void Save(SaveWriter w, Simulation sim)
    {
        w.Int(_owed.Count);
        foreach (var (from, to, silver) in _owed)
        {
            w.Entity(from);
            w.Entity(to);
            w.Int(silver);
        }
    }

    public void Load(SaveReader r, Simulation sim)
    {
        _owed.Clear();
        for (int k = 0, n = r.Count(4096); k < n; k++)
        {
            var from = r.Entity();
            var to = r.Entity();
            var silver = r.Int();
            if (silver > 0)
                _owed.Add((from, to, silver));
        }
    }
}

/// <summary>Lean the barrow in hand back as it's pushed (Q/E held: −1 left, 1 right, 0 let go): <see cref="BarrowSystem"/>.</summary>
public sealed record BarrowLeanCommand(Entity Actor, int Nudge) : ISimCommand, IActorCommand
{
    public void Apply(Simulation sim)
    {
        if (sim.World.IsAlive(Actor) && Barrows.Held(sim, Actor) is { IsNull: false } barrow && sim.World.TryGet<BarrowLoad>(barrow, out var load))
            load.Nudge = Math.Sign(Nudge);
    }
}

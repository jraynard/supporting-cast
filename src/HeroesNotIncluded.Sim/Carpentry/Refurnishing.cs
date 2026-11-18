using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Carpentry;

/// <summary>A piece of furniture a fire took from a town building: where it stood and how it faced, so the carpenter can set one back.</summary>
public sealed class FurnitureLoss
{
    public int Id;
    public ThingDef Def = null!;
    public int3 Cell;
    public byte Facing;

    /// <summary>The building it stood in (the inn, a cottage), for the work list.</summary>
    public Entity Building;

    public long LostTick;

    public string Label => Def.Label;
}

/// <summary>A piece of furniture made at the bench to set back where one burned (<see cref="Refurnishing"/>): what it is, and how well it's joined.</summary>
public struct Furnishing
{
    public ThingDef Def;
    public float Quality;
    public Entity Maker;
}

/// <summary>A lost piece as anyone sees it (<c>PerceptionView.Losses</c>): what burned, where, and in which building.</summary>
public readonly record struct KnownLoss(int Id, string Label, string Piece, int3 Cell, string Where, int Planks);

/// <summary>
/// Furniture lost to fire, and the carpenter's setting it back (docs/DESIGN.md, "Carpenter"). When
/// the fire (or the dragon's) burns a joined piece in one of the town's buildings (a stool, a table,
/// a shelf: a def with <see cref="ThingDef.Joinery"/>), the town remembers it. The carpenter makes a
/// new one at the bench from planks, joint by joint (<see cref="JoinSession"/>), carries it over,
/// and sets it where the old one stood; the town pays for the work (<see cref="FeePerPlank"/>).
/// Public knowledge: anyone can see the stool's gone.
/// </summary>
public sealed class Refurnishing : ISimSystem, ISaveState
{
    public string Name => "Refurnishing";
    public int Interval => SimClock.TicksPerHour;

    /// <summary>The item a made piece is carried as, before it's set down.</summary>
    public const string ItemDef = "Furnishing";

    /// <summary>Silver the town pays the carpenter for each plank's worth of a piece set back.</summary>
    public const int FeePerPlank = 3;

    /// <summary>Ticks an AI carpenter works a piece at the bench (for each joint).</summary>
    public const int TicksPerJoint = 300;

    private readonly List<FurnitureLoss> _losses = [];
    private int _nextId = 1;

    public IReadOnlyList<FurnitureLoss> Losses => _losses;

    public FurnitureLoss? Find(int id) => _losses.Find(l => l.Id == id);

    /// <summary>The loss at a cell (a click where the stool stood), if any.</summary>
    public FurnitureLoss? At(int3 cell) => _losses.Find(l => l.Cell == cell);

    /// <summary>A loss of this piece still open (the oldest), if any.</summary>
    public FurnitureLoss? Of(ThingDef def) => _losses.Where(l => l.Def == def).OrderBy(l => l.Id).FirstOrDefault();

    /// <summary>A loss is forgotten once something else stands there, or its building's gone.</summary>
    public void Update(Simulation sim)
    {
        _losses.RemoveAll(l => !sim.World.IsAlive(l.Building) || !sim.Map.Structure[sim.Map.Idx(l.Cell)].IsNull);
    }

    /// <summary>
    /// A thing burned (<see cref="Environment.FireSystem"/>): if it was a joined piece in one of the
    /// town's buildings, in a round, the town remembers where it stood.
    /// </summary>
    public static void Burned(Simulation sim, ThingDef def, int3 cell, byte facing)
    {
        if (def.Joinery == null || !sim.Round.Live || Buildings.At(sim, cell) is not { IsNull: false } building)
            return;
        var self = sim.Refurnishing;
        self._losses.Add(new FurnitureLoss { Id = self._nextId++, Def = def, Cell = cell, Facing = facing, Building = building, LostTick = sim.Clock.Tick });
    }

    /// <summary>
    /// The fire's out and no crisis is on: time to rebuild. An AI carpenter (or a bot) doesn't sit
    /// down at the bench to join a stool while the town's still burning or the dragon's overhead.
    /// </summary>
    public static bool Settled(Simulation sim) => sim.Fire.BurningTiles.Count == 0 && sim.Director.Active.Count == 0;

    // ---------------- Making it ----------------

    /// <summary>Why this person can't make <paramref name="piece"/> at the bench now; null if they can.</summary>
    public static string? MakeProblem(Simulation sim, Entity worker, string piece)
    {
        if (!sim.Defs.TryThing(piece, out var def) || def.Joinery is not { } joinery)
            return "that isn't joinery";
        if (sim.Refurnishing.Of(def) == null)
            return $"no {def.Label} wants replacing";
        return PlanksTo(sim, worker) < joinery.Planks ? $"a {def.Label} takes {joinery.Planks} plank{(joinery.Planks == 1 ? "" : "s")}" : null;
    }

    private static bool IsPlank(Simulation sim, Entity e) =>
        sim.World.IsAlive(e) && sim.World.TryGet<Thing>(e, out var t) && t.Def.Name == Carpenters.PlanksDef;

    /// <summary>Planks to hand: in the hands, the pack, a chest in reach.</summary>
    public static int PlanksTo(Simulation sim, Entity worker) =>
        Core.CraftCommandRules.Sources(sim, worker).Where(e => IsPlank(sim, e)).Sum(e => sim.World.Get<Stack>(e).Count);

    /// <summary>Use up planks from what's to hand (hands, pack, a chest in reach). False (none used) if short.</summary>
    private static bool UsePlanks(Simulation sim, Entity worker, int count)
    {
        var w = sim.World;
        var from = Core.CraftCommandRules.Sources(sim, worker).Where(e => IsPlank(sim, e)).ToList();
        if (from.Sum(e => w.Get<Stack>(e).Count) < count)
            return false;
        foreach (var e in from)
        {
            if (count <= 0)
                break;
            var take = Math.Min(count, w.Get<Stack>(e).Count);
            count -= take;
            if ((w.Get<Stack>(e).Count -= take) <= 0)
            {
                if (w.Get<Carrying>(worker).Item == e)
                    w.Get<Carrying>(worker).Item = Entity.Null;
                sim.DestroyThing(e);
            }
        }
        return true;
    }

    /// <summary>Begin making a piece at the bench, if this worker can.</summary>
    public static void StartMaking(Simulation sim, Entity worker, Entity bench, string piece)
    {
        if (MakeProblem(sim, worker, piece) is { } why)
        {
            sim.Messages.Tell(worker, MessageKind.Info, $"Can't make it: {why}.", sim.LocationOf(worker));
            return;
        }
        var def = sim.Defs.Thing(piece);
        WorkSystem.Start(sim, worker, new JoinSession
        {
            Station = bench, Work = JoinWork.Furniture, Piece = piece, Loss = sim.Refurnishing.Of(def)!.Id, Joints = Math.Clamp(def.Joinery!.Joints, 1, 8),
        });
    }

    /// <summary>The piece is joined: its planks used, and it's in the maker's arms to carry over.</summary>
    public static Entity Made(Simulation sim, Entity worker, string piece, int loss, float quality)
    {
        if (!sim.Defs.TryThing(piece, out var def) || def.Joinery is not { } joinery || !UsePlanks(sim, worker, joinery.Planks))
            return Entity.Null;
        var item = sim.CreateItem(sim.Defs.Thing(ItemDef), 1);
        sim.World.Add(item, new Furnishing { Def = def, Quality = Math.Clamp(quality, 0f, 1f), Maker = worker });
        sim.World.Add(item, new Crafted { Quality = Crafting.QualityFrom(quality), Maker = worker });
        Hospitality.Provisions.TakeInArms(sim, worker, item);
        var how = quality >= 0.85f ? "Every joint tight." : quality < 0.45f ? "It wobbles a little." : "It'll do.";
        var where = sim.Refurnishing.Find(loss) is { } l ? $" Carry it to the {Buildings.Called(sim, l.Building)} and set it where the old one stood." : "";
        sim.Messages.Tell(worker, MessageKind.Info, $"You join a new {def.Label}. {how}{where}", sim.LocationOf(worker));
        return item;
    }

    // ---------------- Setting it back ----------------

    /// <summary>A made piece in someone's arms (<see cref="Furnishing"/>); Entity.Null if they hold none.</summary>
    public static Entity Held(Simulation sim, Entity who) =>
        sim.World.Get<Carrying>(who).Item is { IsNull: false } held && sim.World.IsAlive(held) && sim.World.Has<Furnishing>(held) ? held : Entity.Null;

    /// <summary>Why this person can't set what they hold where this loss stood; null if they can.</summary>
    public static string? SetProblem(Simulation sim, Entity who, FurnitureLoss? loss)
    {
        if (loss == null)
            return "nothing burned there";
        var held = Held(sim, who);
        if (held.IsNull)
            return $"you need a new {loss.Label} in your arms";
        if (sim.World.Get<Furnishing>(held).Def != loss.Def)
            return $"a {loss.Label} stood there, not a {sim.World.Get<Furnishing>(held).Def.Label}";
        if (!AvatarSystem.InReach(sim.World.Get<Position>(who).Cell, loss.Cell))
            return "you need to be where it stood";
        return !sim.Map.Structure[sim.Map.Idx(loss.Cell)].IsNull || !sim.CanPlaceStructure(loss.Cell, loss.Def, loss.Facing) ? "something's in the way" : null;
    }

    /// <summary>Set the piece in someone's arms where the old one stood: it's furniture again, and the town pays for the work.</summary>
    public static bool Set(Simulation sim, Entity who, FurnitureLoss? loss)
    {
        if (SetProblem(sim, who, loss) is { } why)
        {
            sim.Messages.Tell(who, MessageKind.Info, $"Can't set it there: {why}.", sim.LocationOf(who));
            return false;
        }
        var w = sim.World;
        var held = Held(sim, who);
        var made = w.Get<Furnishing>(held);
        w.Get<Carrying>(who).Item = Entity.Null;
        sim.DestroyThing(held);
        var piece = sim.SpawnStructure(loss!.Def, loss.Cell, loss.Facing);
        w.Add(piece, new Crafted { Quality = Crafting.QualityFrom(made.Quality), Maker = made.Maker });
        sim.Refurnishing._losses.Remove(loss);
        var fee = FeePerPlank * loss.Def.Joinery!.Planks;
        Inventory.Stow(sim, who, sim.CreateItem(sim.Defs.Thing(Shops.Coin.Silver), fee));
        sim.Messages.Add(MessageKind.Good, $"{w.Get<Creature>(who).Name} sets a new {loss.Label} in the {Buildings.Called(sim, loss.Building)}. The town pays {fee} silver for it.", loss.Cell);
        return true;
    }

    // ---------------- An AI carpenter ----------------

    /// <summary>A loss to set back, and the planks for it in the woodshop's chest: an AI carpenter makes it and sets it.</summary>
    public static Job? Furnish(Simulation sim, Entity pawn)
    {
        var w = sim.World;
        if (sim.Refurnishing._losses.Count == 0 || ShopSystemSmallHours(sim) || !Settled(sim))
            return null;
        var shop = Carpenters.WoodshopOf(sim, pawn);
        var chest = Carpenters.ChestOf(sim, shop);
        var bench = Carpenters.StationOf(sim, shop, Carpenters.BenchDef);
        if (chest.IsNull || bench.IsNull || sim.Reservations.IsReservedByOther(bench, pawn))
            return null;
        var from = w.Get<Position>(pawn).Cell;
        foreach (var loss in sim.Refurnishing._losses.OrderBy(l => l.Id))
            if (Carpenters.CountIn(sim, chest, Carpenters.PlanksDef) >= loss.Def.Joinery!.Planks
                && Navigation.CanReach(sim, pawn, from, loss.Cell, PathEnd.Touch))
                return new FurnishJob(loss.Id, bench, chest);
        return null;
    }

    private static bool ShopSystemSmallHours(Simulation sim) => Shops.ShopSystem.SmallHours(sim);

    public void Save(SaveWriter w, Simulation sim)
    {
        w.Int(_nextId);
        w.Int(_losses.Count);
        foreach (var l in _losses)
        {
            w.Int(l.Id);
            w.Def(l.Def);
            w.Int(l.Cell.X);
            w.Int(l.Cell.Y);
            w.Int(l.Cell.Z);
            w.Byte(l.Facing);
            w.Entity(l.Building);
            w.Long(l.LostTick);
        }
    }

    public void Load(SaveReader r, Simulation sim)
    {
        _nextId = r.Int();
        _losses.Clear();
        for (var n = r.Count(10_000); n > 0; n--)
            _losses.Add(new FurnitureLoss
            {
                Id = r.Int(), Def = r.Def<ThingDef>(), Cell = new int3(r.Int(), r.Int(), r.Int()), Facing = (byte)(r.Byte() & 3),
                Building = r.Entity(), LostTick = r.Long(),
            });
    }
}

/// <summary>An AI carpenter makes a piece to replace one that burned, at the bench from the woodshop's planks, and carries it over and sets it.</summary>
public sealed class FurnishJob(int loss, Entity bench, Entity chest) : Job
{
    public override Entity Focus => bench;

    protected override IEnumerable<JobStatus> Run()
    {
        if (Sim.Refurnishing.Find(loss) is not { } lost)
        {
            Fail("it's been replaced");
            yield break;
        }
        var joinery = lost.Def.Joinery!;
        Report = $"Making a {lost.Label}";
        if (!Reserve(bench)) yield break;
        foreach (var s in GoTo(Get<Thing>(bench).Cell, PathEnd.Touch)) yield return s;
        if (HasFailed) yield break;
        foreach (var s in Wait(Refurnishing.TicksPerJoint * joinery.Joints)) yield return s;
        if (!Alive(bench) || !Alive(chest) || Sim.Refurnishing.Find(loss) == null
            || !Carpenters.TakeFrom(Sim, chest, Carpenters.PlanksDef, joinery.Planks))
        {
            Fail("no planks, or it's been replaced");
            yield break;
        }
        SkillRules.Learn(Sim, Pawn, Skill.Crafting, SkillRules.XpPerWorkTick * Refurnishing.TicksPerJoint * joinery.Joints);
        var quality = Math.Clamp(Carpenters.ExpectedQuality(Sim, Pawn) + Sim.Rand.Range(-0.1f, 0.1f), 0.05f, 1f);
        var item = Sim.CreateItem(Sim.Defs.Thing(Refurnishing.ItemDef), 1);
        Sim.World.Add(item, new Furnishing { Def = lost.Def, Quality = quality, Maker = Pawn });
        Hospitality.Provisions.TakeInArms(Sim, Pawn, item);
        Report = $"Carrying a new {lost.Label} over";
        foreach (var s in GoTo(lost.Cell, PathEnd.Touch)) yield return s;
        if (HasFailed) yield break;
        if (!Refurnishing.Set(Sim, Pawn, Sim.Refurnishing.Find(loss)))
            Fail("couldn't set it down there");
    }
}

/// <summary>Make a piece of furniture at the carpenter's bench, to set back where one burned (<see cref="JoinSession"/>): planks from what's to hand.</summary>
public sealed record MakeFurnitureCommand(Entity Avatar, Entity Bench, string Piece) : ISimCommand, Core.IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (Core.InventoryCommand.Ready(sim, Avatar) && Carpenters.IsBench(sim, Bench) && Piece is { Length: > 0 and < 100 }
            && AvatarSystem.InReach(sim, sim.World.Get<Position>(Avatar).Cell, Bench) && !sim.World.Has<WorkSession>(Avatar))
            Refurnishing.StartMaking(sim, Avatar, Bench, Piece);
    }
}

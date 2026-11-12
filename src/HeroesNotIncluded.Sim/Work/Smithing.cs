using SupportingCast.Sim.Ai;
using SupportingCast.Sim.Combat;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Work;

/// <summary>How hot iron looks: the only way a smith reads a piece's heat. Bands by temperature, the same for every metal.</summary>
public enum Glow : byte
{
    Black,
    DullRed,
    BloodRed,
    Cherry,
    Orange,
    Yellow,
    White,
}

/// <summary>Something wrong in a piece that doesn't show: only how it fails in use gives it away.</summary>
public enum PieceFlaw : byte
{
    None,

    /// <summary>Hammered too cold: a hairline crack that may let go under a hard blow.</summary>
    Crack,

    /// <summary>Held in the quench too long: hard but brittle, it chips easily.</summary>
    Brittle,

    /// <summary>Lifted from the quench too soon: never hardened, it dulls fast.</summary>
    Soft,
}

/// <summary>
/// The piece a smith has in their tongs (on the smith, not an item: it can't be set down hot or
/// picked up by someone else). Made at a forge, its materials sit staged on that forge's bench
/// until it's finished, so calling the work off there sets them down again. Its <see cref="Flaw"/>
/// is hidden: nothing but <see cref="Perception"/> may describe a piece, and that never says it.
/// </summary>
public sealed class WorkPiece
{
    /// <summary>The forge it was begun at, whose bench holds its materials (a mend's too, for the fire).</summary>
    public Entity Forge;

    /// <summary>What's being made; for a mend, the recipe that makes the piece being mended.</summary>
    public RecipeDef Recipe = null!;

    /// <summary>The gear being mended; Entity.Null when making something new.</summary>
    public Entity Mending = Entity.Null;

    public float Temp;

    /// <summary>How far it's been beaten into shape, 0..1.</summary>
    public float Shape;

    /// <summary>Blows on the mark it takes to shape it.</summary>
    public int BlowsNeeded;

    /// <summary>The sum and count of how well each blow landed, for its quality.</summary>
    public float BlowSum;
    public int BlowCount;

    /// <summary>Steps it spent past burning heat in the fire.</summary>
    public int BurnSteps;

    public PieceFlaw Flaw;

    public bool Shaped => Shape >= 1f;
    public bool IsMend => !Mending.IsNull;
    public float BlowQuality => BlowCount == 0 ? 0f : BlowSum / BlowCount;
}

/// <summary>The hidden fault forged into a finished piece (<see cref="PieceFlaw"/>); shown by nothing.</summary>
public struct Flawed
{
    public PieceFlaw Flaw;
}

/// <summary>
/// Forging by hand, a heat at a time. A piece is begun at the forge (its materials staged there),
/// heated in the fire with the bellows, beaten on the anvil while it holds its colour and back to
/// the fire when it fades, quenched in the tub, and given its edge on the grindstone. Each of those
/// is a <see cref="WorkSession"/>; between them the piece cools in the air (<see cref="SmithingSystem"/>).
/// A mend is the same with the worn piece in the tongs: heat, hammer out the chips, then the edge.
/// Players in a round only; AI smiths keep the plain <see cref="CraftJob"/>.
/// </summary>
public static class Smithing
{
    /// <summary>Air keeps this share of a piece's heat above the room's each step (it loses about 2.4% a second): a heat on the anvil lasts some six seconds.</summary>
    public const float AirCooling = 0.9996f;

    /// <summary>Seconds past burning heat that cost the piece a grade of quality.</summary>
    public const float BurnSecondsPerGrade = 2f;

    /// <summary>A piece left this long past burning heat is burnt through and lost.</summary>
    public const float BurnSecondsToRuin = 7f;

    /// <summary>The forge's heat a recipe works at, when it doesn't say.</summary>
    private const float DefaultWorkTemp = 800f;

    public static Glow GlowOf(float temp) => temp switch
    {
        < 500f => Glow.Black,
        < 650f => Glow.DullRed,
        < 750f => Glow.BloodRed,
        < 850f => Glow.Cherry,
        < 1000f => Glow.Orange,
        < 1150f => Glow.Yellow,
        _ => Glow.White,
    };

    public static string GlowLabel(Glow g) => g switch
    {
        Glow.Black => "black",
        Glow.DullRed => "a dull red",
        Glow.BloodRed => "blood red",
        Glow.Cherry => "cherry red",
        Glow.Orange => "orange",
        Glow.Yellow => "yellow",
        _ => "white and sparking",
    };

    /// <summary>The hottest the air round a smith gets, for cooling a piece: the sim's cell heat beside a forge runs far hotter than a smithy's air.</summary>
    public const float MaxAir = 100f;

    /// <summary>The air a piece cools toward where the smith stands.</summary>
    public static float AirAt(Simulation sim, Entity smith) => MathF.Min(sim.TemperatureAt(sim.World.Get<Position>(smith).Cell), MaxAir);

    /// <summary>The heat a piece works at: from <c>Lo</c> it moves under the hammer, up to <c>Hi</c>; past <c>Burn</c> it burns in the fire.</summary>
    public static (float Lo, float Hi, float Burn) Window(RecipeDef recipe)
    {
        var t = recipe.MinTemp > 0 ? recipe.MinTemp : DefaultWorkTemp;
        return (t * 0.95f, t * 1.2f, t * 1.3f);
    }

    /// <summary>The colours to work a recipe's metal in, as a smith would say it ("cherry red to orange").</summary>
    public static string WorkingColours(RecipeDef recipe)
    {
        var (lo, hi, _) = Window(recipe);
        var a = GlowOf(lo);
        var b = GlowOf(hi);
        return a == b ? GlowLabel(a) : $"{GlowLabel(a)} to {GlowLabel(b)}";
    }

    /// <summary>Blows to shape a new piece: more work, more blows (a knife 4, a sword 12, plate 24).</summary>
    public static int BlowsFor(RecipeDef recipe) => Math.Clamp((int)MathF.Round(recipe.Work / 150f), 4, 24);

    /// <summary>Gear that hardens in the quench: weapons and tools. Armor is finished off the anvil.</summary>
    public static bool NeedsQuench(RecipeDef recipe) => recipe.Products.Exists(p => p.ThingDef.Apparel == null);

    public static WorkPiece? PieceOf(Simulation sim, Entity smith) =>
        sim.World.TryGet<WorkPiece>(smith, out var p) ? p : null;

    public static string PieceLabel(Simulation sim, WorkPiece p) =>
        p.IsMend && sim.World.IsAlive(p.Mending) ? sim.LabelOf(p.Mending) : p.Recipe.Products.Count > 0 ? p.Recipe.Products[0].ThingDef.Label : p.Recipe.Label;

    /// <summary>Should a player's work on this recipe at this station be forged by hand? In a round, at a hand-work station, for gear.</summary>
    public static bool Applies(Simulation sim, Entity worker, Entity station, RecipeDef recipe) =>
        AppliesToRepair(sim, worker, station) && recipe.Products.Exists(p => Crafting.IsGear(p.ThingDef));

    /// <summary>Should a player's mend at this station be done by hand? As for making: in a round, at a station that wants it.</summary>
    public static bool AppliesToRepair(Simulation sim, Entity worker, Entity station) =>
        sim.Round.Phase == Round.RoundPhase.Playing && sim.World.Has<Avatar>(worker)
        && sim.World.Get<Thing>(station).Def.Building?.Workbench is { HandWork: true };

    /// <summary>Take up a billet for a recipe whose materials are all staged at the forge. It goes in the fire next.</summary>
    public static void BeginMaking(Simulation sim, Entity smith, Entity forge, RecipeDef recipe)
    {
        var w = sim.World;
        var cell = w.Get<Thing>(forge).Cell;
        if (PieceOf(sim, smith) is { } held)
        {
            if (held.Forge != forge || held.Recipe != recipe || held.IsMend)
                sim.Messages.Add(MessageKind.Info, $"You already have the {PieceLabel(sim, held)} in your tongs.", cell);
            else
                sim.Messages.Add(MessageKind.Info, $"The {PieceLabel(sim, held)} is in your tongs: {NextStep(sim, smith, held)}.", cell);
            return;
        }
        w.Add(smith, new WorkPiece
        {
            Forge = forge, Recipe = recipe, Temp = AirAt(sim, smith), BlowsNeeded = BlowsFor(recipe),
        });
        sim.Messages.Add(MessageKind.Info, $"A billet for the {recipe.Label} is in your tongs: put it in the fire, and work it {WorkingColours(recipe)}.", cell);
    }

    /// <summary>Take a worn piece the smith has on them into the tongs, to heat and hammer out its chips. Firing the forge for an hour.</summary>
    public static void BeginMending(Simulation sim, Entity smith, Entity forge, Entity item)
    {
        var w = sim.World;
        var cell = w.Get<Thing>(forge).Cell;
        if (Crafting.RepairRecipe(w.Get<Thing>(forge).Def, w.Get<Thing>(item).Def) is not { } recipe)
            return;
        if (PieceOf(sim, smith) is { } held)
        {
            sim.Messages.Add(MessageKind.Info, $"You already have the {PieceLabel(sim, held)} in your tongs.", cell);
            return;
        }
        var hp = WeaponWear.HpFraction(sim, item);
        if (hp >= 0.99f)
        {
            sim.Messages.Add(MessageKind.Info, $"The {sim.LabelOf(item)} has no chips to hammer out.", cell);
            return;
        }
        if (!w.TryGet<Fueled>(forge, out var fuel) || fuel.Fuel <= 0)
        {
            sim.Messages.Add(MessageKind.Info, $"The {w.Get<Thing>(forge).Label} has no fuel.", cell);
            return;
        }
        FireForMending(sim, forge);
        w.Add(smith, new WorkPiece
        {
            Forge = forge, Recipe = recipe, Mending = item, Temp = AirAt(sim, smith),
            BlowsNeeded = Math.Max(3, (int)MathF.Round(BlowsFor(recipe) * (1f - hp) * 1.5f)),
        });
        sim.Messages.Add(MessageKind.Info, $"The {sim.LabelOf(item)} is in your tongs: heat it {WorkingColours(recipe)} and hammer out the chips.", cell);
    }

    /// <summary>A forge fires only for work: a mend keeps it lit for an hour from now.</summary>
    internal static void FireForMending(Simulation sim, Entity forge)
    {
        var w = sim.World;
        var until = sim.Clock.Tick + SimClock.TicksPerHour;
        if (w.Has<FiredForMending>(forge))
            w.Get<FiredForMending>(forge).Until = until;
        else
            w.Add(forge, new FiredForMending { Until = until });
    }

    /// <summary>What a station does for a smith with this piece, as the work key would offer it; null if nothing.</summary>
    public static string? StepAt(Simulation sim, Entity smith, Entity station)
    {
        var w = sim.World;
        if (station.IsNull || !w.IsAlive(station) || w.Get<Thing>(station).Def.Building is not { } b)
            return null;
        var piece = PieceOf(sim, smith);
        // A forge gone out is fed first: the work key offers that (AvatarSystem's refuel) instead.
        if (piece != null && b.Workbench is { HandWork: true } && b.Fuel != null)
            return w.TryGet<Fueled>(station, out var fuel) && fuel.Fuel > 0 ? $"Put the {PieceLabel(sim, piece)} in the fire" : null;
        switch (b.Smithy)
        {
            case SmithyRole.Anvil when piece is { Shaped: false }:
                return $"Hammer the {PieceLabel(sim, piece)}";
            case SmithyRole.Quench when piece is { Shaped: true } && NeedsQuench(piece.Recipe) && !piece.IsMend:
                return $"Quench the {PieceLabel(sim, piece)}";
            case SmithyRole.Grind when ToGrind(sim, smith) is { IsNull: false } blade:
                return $"Grind an edge on the {sim.LabelOf(blade)}";
        }
        return null;
    }

    /// <summary>Begin the step a station offers (<see cref="StepAt"/>), or say why not.</summary>
    public static void Start(Simulation sim, Entity smith, Entity station)
    {
        var w = sim.World;
        if (station.IsNull || !w.IsAlive(station) || w.Get<Thing>(station).Def.Building is not { } b
            || !AvatarSystem.InReach(sim, w.Get<Position>(smith).Cell, station))
            return;
        var cell = w.Get<Thing>(station).Cell;
        var piece = PieceOf(sim, smith);
        if (piece != null && b.Workbench is { HandWork: true } && b.Fuel != null)
        {
            if (!w.TryGet<Fueled>(station, out var fuel) || fuel.Fuel <= 0)
            {
                sim.Messages.Add(MessageKind.Info, $"The {w.Get<Thing>(station).Label} has no fuel.", cell);
                return;
            }
            if (piece.IsMend)
                FireForMending(sim, piece.Forge);
            WorkSystem.Start(sim, smith, HeatSession.For(sim, station, piece));
            return;
        }
        switch (b.Smithy)
        {
            case SmithyRole.Anvil when piece is { Shaped: false }:
                WorkSystem.Start(sim, smith, HammerSession.For(sim, smith, station, piece));
                return;
            case SmithyRole.Quench when piece is { Shaped: true } && NeedsQuench(piece.Recipe) && !piece.IsMend:
                if (piece.Temp < Window(piece.Recipe).Lo * 0.75f)
                    sim.Messages.Add(MessageKind.Info, $"The {PieceLabel(sim, piece)} has gone {GlowLabel(GlowOf(piece.Temp))}: too cold to harden. Heat it first.", cell);
                else
                    WorkSystem.Start(sim, smith, QuenchSession.For(sim, smith, station, piece));
                return;
            case SmithyRole.Grind when ToGrind(sim, smith) is { IsNull: false } blade:
                SharpenSession.TryStart(sim, smith, blade);
                return;
        }
        if (piece != null)
            sim.Messages.Add(MessageKind.Info, $"The {PieceLabel(sim, piece)}: {NextStep(sim, smith, piece)}.", cell);
    }

    /// <summary>What's next for a piece, as a hint ("back to the fire").</summary>
    public static string NextStep(Simulation sim, Entity smith, WorkPiece p)
    {
        var (lo, _, _) = Window(p.Recipe);
        if (!p.Shaped)
            return p.Temp >= lo ? "hot enough: to the anvil" : p.Shape > 0 ? "back to the fire" : "into the fire";
        return p.Temp >= lo * 0.75f ? "quench it while it's hot" : "heat it again, then quench it";
    }

    /// <summary>The edged blade on the smith that most wants the grindstone: in hand first, then the dullest on them.</summary>
    public static Entity ToGrind(Simulation sim, Entity smith)
    {
        var w = sim.World;
        bool Wants(Entity e) => w.IsAlive(e) && w.TryGet<Thing>(e, out var t) && WeaponWear.Edged(t.Def) && WeaponWear.Dullness(sim, e) > 0.02f;
        var held = w.Get<Carrying>(smith).Item;
        if (Wants(held))
            return held;
        var best = Entity.Null;
        var dullest = 0f;
        void Consider(Entity e)
        {
            if (Wants(e) && WeaponWear.Dullness(sim, e) > dullest)
                (best, dullest) = (e, WeaponWear.Dullness(sim, e));
        }
        if (w.TryGet<Equipment>(smith, out var eq))
            Consider(eq.Weapon);
        if (sim.PackOf(smith) is { IsNull: false } pack)
            foreach (var item in sim.StoredIn(pack))
                Consider(item);
        return best;
    }

    /// <summary>The grindstone in the smith's reach, if any: it turns a blade's edge faster than a whetstone.</summary>
    public static Entity GrindstoneInReach(Simulation sim, Entity smith)
    {
        var w = sim.World;
        var at = w.Get<Position>(smith).Cell;
        foreach (var d in Maps.Map.Neighbors8)
        {
            var c = new int3(at.X + d.X, at.Y + d.Y, at.Z);
            if (sim.Map.InBounds(c) && sim.Map.Structure[sim.Map.Idx(c)] is { IsNull: false } s && !w.Has<Construction>(s)
                && w.Get<Thing>(s).Def.Building?.Smithy == SmithyRole.Grind)
                return s;
        }
        return Entity.Null;
    }

    /// <summary>
    /// Shaped (and quenched, for what needs it): turn the billet into the finished piece at its
    /// forge, graded by the blows and the quench, a grade lost for each spell it burned. An edged
    /// one comes out blunt, for the grindstone.
    /// </summary>
    internal static void Complete(Simulation sim, Entity smith, WorkPiece p, float? quench)
    {
        var w = sim.World;
        w.Remove<WorkPiece>(smith);
        var cell = w.Get<Position>(smith).Cell;
        if (!w.IsAlive(p.Forge) || !w.TryGet<Workbench>(p.Forge, out var bench) || bench.Active != p.Recipe || bench.By != smith)
        {
            sim.Messages.Add(MessageKind.Warning, $"The work on the {p.Recipe.Label} was called off at the forge.", cell);
            return;
        }
        var q = quench is { } qq ? p.BlowQuality * 0.75f + qq * 0.25f : p.BlowQuality;
        var grade = (int)Crafting.QualityFrom(q) - (int)(p.BurnSteps / (BurnSecondsPerGrade * SimClock.TicksPerSecond));
        var quality = (Quality)Math.Max((int)Quality.Crude, grade);
        foreach (var item in Crafting.Finish(sim, p.Forge, smith, quality))
        {
            if (WeaponWear.Edged(w.Get<Thing>(item).Def))
                WeaponWear.SetDullness(sim, item, 1f);
            if (p.Flaw != PieceFlaw.None)
                w.Add(item, new Flawed { Flaw = p.Flaw });
            Inventory.Stow(sim, smith, item);
            var edge = WeaponWear.Edged(w.Get<Thing>(item).Def) ? " Grind it an edge." : "";
            sim.Messages.Add(MessageKind.Info, $"Made a {sim.LabelOf(item)}.{edge}", cell);
        }
    }

    /// <summary>
    /// The chips are beaten out as well as the blows landed: a good hand restores nearly all of it,
    /// a poor one only some. A blade comes off the anvil dull. Botched badly, the temper suffers.
    /// </summary>
    internal static void Mend(Simulation sim, Entity smith, WorkPiece p)
    {
        var w = sim.World;
        w.Remove<WorkPiece>(smith);
        var item = p.Mending;
        var cell = w.Get<Position>(smith).Cell;
        if (!w.IsAlive(item) || !w.IsAlive(p.Forge))
            return;
        var label = sim.LabelOf(item);
        if (Crafting.RepairCost(sim, w.Get<Thing>(p.Forge).Def, item) is not { } cost)
            return;
        if (!Crafting.PayForRepair(sim, smith, cost.Material, cost.Count))
        {
            sim.Messages.Add(MessageKind.Info, $"Mending the {label} needs {cost.Count} {cost.Material.ThingDef.Label}.", cell);
            return;
        }
        var quality = p.BlowQuality;
        ref var t = ref w.Get<Thing>(item);
        t.Hp += (t.MaxHp - t.Hp) * (0.4f + 0.6f * quality);
        if (WeaponWear.Edged(t.Def))
            WeaponWear.SetDullness(sim, item, Math.Max(WeaponWear.Dullness(sim, item), WeaponWear.DullAfterHammering));
        if (p.Flaw != PieceFlaw.None && !w.Has<Flawed>(item))
            w.Add(item, new Flawed { Flaw = p.Flaw });
        var burnt = p.BurnSteps >= BurnSecondsPerGrade * SimClock.TicksPerSecond;
        if ((quality < 0.3f || burnt) && w.TryGet<Crafted>(item, out var made) && made.Quality > Quality.Crude)
        {
            w.Get<Crafted>(item).Quality = made.Quality - 1;
            sim.Messages.Add(MessageKind.Warning, $"{(burnt ? "Burnt in the fire" : "Clumsy blows")}: the {label} is the worse for its mending.", cell);
        }
        else
            sim.Messages.Add(MessageKind.Info, $"Hammered the chips out of the {label}.{(WeaponWear.Edged(t.Def) ? " It wants an edge now." : "")}", cell);
    }

    /// <summary>Burnt through in the fire: the piece is lost, and what went into it with it.</summary>
    internal static void Ruin(Simulation sim, Entity smith, WorkPiece p)
    {
        var w = sim.World;
        w.Remove<WorkPiece>(smith);
        var cell = w.Get<Position>(smith).Cell;
        if (p.IsMend)
        {
            if (w.IsAlive(p.Mending))
            {
                ref var t = ref w.Get<Thing>(p.Mending);
                t.Hp = Math.Max(1f, t.Hp * 0.5f);
            }
            sim.Messages.Add(MessageKind.Warning, $"Left too long in the fire, the {PieceLabel(sim, p)} burned: it's worse than before.", cell);
            return;
        }
        if (w.IsAlive(p.Forge) && w.TryGet<Workbench>(p.Forge, out var bench) && bench.Active == p.Recipe && bench.By == smith)
            bench.Clear();
        sim.Messages.Add(MessageKind.Warning, $"Left too long in the fire, the billet for the {p.Recipe.Label} burned through.", cell);
    }
}

/// <summary>
/// Pieces in the tongs cool in the air between steps, and are let go when the work's gone: the
/// forge's bench called off, the piece being mended lost, or the smith no longer at it.
/// </summary>
public sealed class SmithingSystem : ISimSystem
{
    public string Name => "Smithing";
    public int Interval => 10;

    // AirCooling to the tenth power: one update's cooling.
    private static readonly float Cooling = Pow(Smithing.AirCooling, 10);

    public void Update(Simulation sim)
    {
        var w = sim.World;
        if (w.Count<WorkPiece>() == 0)
            return;
        foreach (var e in w.Query<WorkPiece>())
        {
            var p = w.Get<WorkPiece>(e);
            if (!Holds(sim, e, p))
            {
                w.Remove<WorkPiece>(e);
                continue;
            }
            // While a step is under way the session has the piece; it writes it back when done.
            if (w.TryGet<WorkSession>(e, out var s) && s is HeatSession or HammerSession or QuenchSession)
                continue;
            var air = Smithing.AirAt(sim, e);
            p.Temp = air + (p.Temp - air) * Cooling;
        }
    }

    private static bool Holds(Simulation sim, Entity e, WorkPiece p)
    {
        var w = sim.World;
        if (!w.TryGet<Creature>(e, out var c) || c.Dead || !w.Has<Avatar>(e))
            return false;
        if (p.IsMend)
            return w.IsAlive(p.Mending) && Inventory.OnPerson(sim, e, p.Mending);
        return w.IsAlive(p.Forge) && w.TryGet<Workbench>(p.Forge, out var bench) && bench.Active == p.Recipe && bench.By == e;
    }

    private static float Pow(float x, int n)
    {
        var r = 1f;
        for (var i = 0; i < n; i++)
            r *= x;
        return r;
    }
}

/// <summary>The work key at a smithy station: put the piece in the fire, hammer it, quench it, grind an edge (<see cref="Smithing.Start"/>).</summary>
public sealed record SmithCommand(Entity Avatar, Entity Station) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (!InventoryCommand.Ready(sim, Avatar) || !sim.World.IsAlive(Station) || !sim.World.Has<Thing>(Station))
            return;
        Smithing.Start(sim, Avatar, Station);
    }
}

/// <summary>Set aside what's in your tongs: a new piece's materials are set down by its forge, a mend stays as it was.</summary>
public sealed record SetAsidePieceCommand(Entity Avatar) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        var w = sim.World;
        if (!w.IsAlive(Avatar) || Smithing.PieceOf(sim, Avatar) is not { } p)
            return;
        w.Remove<WorkPiece>(Avatar);
        if (!p.IsMend && w.IsAlive(p.Forge) && w.TryGet<Workbench>(p.Forge, out var bench) && bench.Active == p.Recipe && bench.By == Avatar)
            Crafting.Cancel(sim, p.Forge);
    }
}

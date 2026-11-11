using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Saving;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Work;

/// <summary>
/// Beating out a piece at the forge: a marker sweeps back and forth and each blow lands where it
/// is when you press. On the mark is a good blow; the piece's quality is the average of them all,
/// so a smith's hands decide the blade in place of a skill roll. With an <see cref="Item"/> it's a
/// repair: the blows hammer out its chips and bends, and flatten its edge for the whetstone.
/// </summary>
public sealed class HammerSession : WorkSession
{
    /// <summary>Blows to finish a piece.</summary>
    public const int BlowsNeeded = 6;

    /// <summary>Seconds for the marker to go across and back.</summary>
    public const float SweepSeconds = 1.4f;

    /// <summary>Half the width of a perfect blow's mark, before skill widens it.</summary>
    public const float MarkHalfWidth = 0.07f;

    /// <summary>Steps the arm takes to come back up: a press before then does nothing.</summary>
    public const int RecoverySteps = 15;

    private int _recovery;

    public Entity Station { get; set; }
    public RecipeDef Recipe { get; set; } = null!;

    /// <summary>The piece being mended; Entity.Null when making a new one.</summary>
    public Entity Item { get; set; } = Entity.Null;

    /// <summary>What's being mended, as named when work began (for the title).</summary>
    public string ItemLabel { get; set; } = "";

    public bool Repairing => !Item.IsNull;

    /// <summary>Where the next blow should land, 0..1.</summary>
    public float Mark { get; private set; }

    public int Blows { get; private set; }

    /// <summary>The sweeping marker, 0..1.</summary>
    public float Marker => Triangle(StepsDone, SweepSeconds);

    public float HalfWidth => MarkHalfWidth * Hands.Tolerance;

    /// <summary>The arm is up and a press would land.</summary>
    public bool Ready => _recovery <= 0;

    public override Skill Skill => Recipe.Skill;
    public override float Xp => 60f;
    public override string Title => Repairing ? $"Hammering out the {ItemLabel}" : $"Hammering out {Recipe.Label}";

    /// <summary>Should a player's work at this station be done by hand? Only in a round, and only for gear at a station that wants it.</summary>
    public static bool Applies(Simulation sim, Entity worker, Entity station, RecipeDef recipe) =>
        sim.Round.Phase == Round.RoundPhase.Playing && sim.World.Has<Avatar>(worker)
        && sim.World.Get<Thing>(station).Def.Building?.Workbench is { HandWork: true }
        && recipe.Products.Exists(p => Crafting.IsGear(p.ThingDef));

    /// <summary>Begin, if the forge is ready; otherwise say why not.</summary>
    public static void TryStart(Simulation sim, Entity worker, Entity station, RecipeDef recipe)
    {
        var cell = sim.World.Get<Thing>(station).Cell;
        var label = sim.World.Get<Thing>(station).Label;
        if (!Crafting.HotEnough(sim, station, recipe))
            // A forge fires only for work, so the first try is usually what lights it.
            sim.Messages.Add(MessageKind.Info, Crafting.CanGetHotEnough(sim, station, recipe) || sim.World.TryGet<Fueled>(station, out var f) && f.Fuel > 0
                ? $"The {label} is heating up ({sim.TemperatureAt(cell):F0} of {recipe.MinTemp:F0}°C). Use it again once it's hot."
                : $"The {label} has no fuel.", cell);
        else
            WorkSystem.Start(sim, worker, new HammerSession { Station = station, Recipe = recipe });
    }

    /// <summary>Begin mending a worn piece the worker has on them, if the forge is ready; otherwise say why not.</summary>
    public static void TryStartRepair(Simulation sim, Entity worker, Entity station, Entity item)
    {
        var w = sim.World;
        var cell = w.Get<Thing>(station).Cell;
        if (Crafting.RepairRecipe(w.Get<Thing>(station).Def, w.Get<Thing>(item).Def) is not { } recipe)
            return;
        if (Combat.WeaponWear.HpFraction(sim, item) >= 0.99f)
        {
            sim.Messages.Add(MessageKind.Info, $"The {sim.LabelOf(item)} has no chips to hammer out.", cell);
            return;
        }
        if (!Crafting.CanFireFor(sim, station, recipe))
        {
            sim.Messages.Add(MessageKind.Info, $"The {w.Get<Thing>(station).Label} has no fuel.", cell);
            return;
        }
        // Asking to mend is what fires it, as making does: it burns for an hour on the strength of it.
        var until = sim.Clock.Tick + SimClock.TicksPerHour;
        if (w.Has<FiredForMending>(station))
            w.Get<FiredForMending>(station).Until = until;
        else
            w.Add(station, new FiredForMending { Until = until });
        if (!Crafting.HotEnough(sim, station, recipe))
            sim.Messages.Add(MessageKind.Info, $"The {w.Get<Thing>(station).Label} is heating up for the mend ({sim.TemperatureAt(cell):F0} of {recipe.MinTemp:F0}°C). Use it again once it's hot.", cell);
        else
            WorkSystem.Start(sim, worker, new HammerSession { Station = station, Recipe = recipe, Item = item, ItemLabel = sim.LabelOf(item) });
    }

    /// <summary>Should a player's mend at this station be done by hand? As for making: in a round, at a station that wants it.</summary>
    public static bool AppliesToRepair(Simulation sim, Entity worker, Entity station) =>
        sim.Round.Phase == Round.RoundPhase.Playing && sim.World.Has<Avatar>(worker)
        && sim.World.Get<Thing>(station).Def.Building?.Workbench is { HandWork: true };

    public override bool StillValid(Simulation sim)
    {
        var w = sim.World;
        if (!Crafting.IsStation(sim, Station) || !AvatarSystem.InReach(sim, w.Get<Position>(Worker).Cell, Station))
            return false;
        return Repairing
            ? w.IsAlive(Item) && Inventory.OnPerson(sim, Worker, Item) && Crafting.HotEnough(sim, Station, Recipe)
            : w.Get<Workbench>(Station).Active == Recipe;
    }

    protected internal override void Begin() => Mark = Rand.Range(0.15f, 0.85f);

    protected override void Step()
    {
        if (_recovery > 0)
            _recovery--;
    }

    protected override void Press()
    {
        if (!Ready)
            return;
        // Unsteady hands land a little off from where the marker was.
        var landed = Marker + (Rand.NextFloat() * 2f - 1f) * Hands.Tremor * 0.2f;
        var off = MathF.Abs(landed - Mark);
        Sample(off <= HalfWidth ? 1f : 1f - (off - HalfWidth) / (HalfWidth * 3f));
        _recovery = RecoverySteps;
        Blows++;
        Mark = Rand.Range(0.15f, 0.85f);
        Done = Blows >= BlowsNeeded;
    }

    public override void Finish(Simulation sim)
    {
        var cell = sim.World.Get<Thing>(Station).Cell;
        if (Repairing)
        {
            Mend(sim, cell);
            return;
        }
        foreach (var item in Crafting.Finish(sim, Station, Worker, Crafting.QualityFrom(Quality)))
        {
            Inventory.Stow(sim, Worker, item);
            sim.Messages.Add(MessageKind.Info, $"Made a {sim.LabelOf(item)}.", cell);
        }
    }

    /// <summary>
    /// The chips come out as well as the blows landed: a good hand restores nearly all of it, a
    /// poor one only some. A blade comes off the anvil dull. Botched badly, the temper suffers.
    /// </summary>
    private void Mend(Simulation sim, int3 cell)
    {
        var w = sim.World;
        if (Crafting.RepairCost(sim, w.Get<Thing>(Station).Def, Item) is not { } cost)
            return;
        if (!Crafting.PayForRepair(sim, Worker, cost.Material, cost.Count))
        {
            sim.Messages.Add(MessageKind.Info, $"Mending the {ItemLabel} needs {cost.Count} {cost.Material.ThingDef.Label}.", cell);
            return;
        }
        ref var t = ref w.Get<Thing>(Item);
        t.Hp += (t.MaxHp - t.Hp) * (0.4f + 0.6f * Quality);
        if (Combat.WeaponWear.Edged(t.Def))
            Combat.WeaponWear.SetDullness(sim, Item, Math.Max(Combat.WeaponWear.Dullness(sim, Item), Combat.WeaponWear.DullAfterHammering));
        if (Quality < 0.3f && w.TryGet<Crafted>(Item, out var made) && made.Quality > Components.Quality.Crude)
        {
            w.Get<Crafted>(Item).Quality = made.Quality - 1;
            sim.Messages.Add(MessageKind.Warning, $"Clumsy blows: the {ItemLabel} is the worse for its mending.", cell);
        }
        else
            sim.Messages.Add(MessageKind.Info, $"Hammered the chips out of the {ItemLabel}.", cell);
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Station);
        w.Def(Recipe);
        w.Entity(Item);
        w.String(ItemLabel);
        w.Float(Mark);
        w.Int(Blows);
        w.Int(_recovery);
    }

    protected override void ReadState(SaveReader r)
    {
        Station = r.Entity();
        Recipe = r.Def<RecipeDef>();
        Item = r.Entity();
        ItemLabel = r.String();
        Mark = r.Float();
        Blows = r.Int();
        _recovery = r.Int();
    }
}

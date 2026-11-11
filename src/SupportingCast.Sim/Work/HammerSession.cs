using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Saving;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Work;

/// <summary>
/// Beating out a piece at the forge: a marker sweeps back and forth and each blow lands where it
/// is when you press. On the mark is a good blow; the piece's quality is the average of them all,
/// so a smith's hands decide the blade in place of a skill roll.
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
    public override string Title => $"Hammering out {Recipe.Label}";

    /// <summary>Should a player's work at this station be done by hand? Only in a round, and only for gear at a station that wants it.</summary>
    public static bool Applies(Simulation sim, Entity worker, Entity station, RecipeDef recipe) =>
        sim.Round.Phase == Round.RoundPhase.Playing && sim.World.Has<Avatar>(worker)
        && sim.World.Get<Thing>(station).Def.Building?.Workbench is { HandWork: true }
        && recipe.Products.Exists(p => Crafting.IsGear(p.ThingDef));

    /// <summary>Begin, if the forge is ready; otherwise say why not.</summary>
    public static void TryStart(Simulation sim, Entity worker, Entity station, RecipeDef recipe)
    {
        var cell = sim.World.Get<Thing>(station).Cell;
        if (!Crafting.HotEnough(sim, station, recipe))
            sim.Messages.Add(MessageKind.Info, $"The {sim.World.Get<Thing>(station).Label} isn't lit, or hot enough.", cell);
        else
            WorkSystem.Start(sim, worker, new HammerSession { Station = station, Recipe = recipe });
    }

    public override bool StillValid(Simulation sim)
    {
        var w = sim.World;
        return Crafting.IsStation(sim, Station) && w.Get<Workbench>(Station).Active == Recipe
            && AvatarSystem.InReach(sim, w.Get<Position>(Worker).Cell, Station);
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
        foreach (var item in Crafting.Finish(sim, Station, Worker, Crafting.QualityFrom(Quality)))
        {
            Inventory.Stow(sim, Worker, item);
            sim.Messages.Add(MessageKind.Info, $"Made a {sim.LabelOf(item)}.", cell);
        }
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Station);
        w.Def(Recipe);
        w.Float(Mark);
        w.Int(Blows);
        w.Int(_recovery);
    }

    protected override void ReadState(SaveReader r)
    {
        Station = r.Entity();
        Recipe = r.Def<RecipeDef>();
        Mark = r.Float();
        Blows = r.Int();
        _recovery = r.Int();
    }
}

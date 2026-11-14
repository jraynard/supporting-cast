using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Work;

/// <summary>
/// Chopping at the prep table (docs/DESIGN.md, "Cook"): raw food in hand, cut down on the knife's
/// beat (F as it comes down). Done ahead in a quiet hour, it pays at the rush: chopped food goes a
/// little further and cooks faster in the pot (<see cref="Cooking.SpeedOf"/>). A cut off the beat
/// does nothing; one badly off may slip, and a slip is a real cut on the cook's arm, work for the
/// healer, landed when they put the knife down.
/// </summary>
public sealed class ChopSession : WorkSession
{
    /// <summary>Steps between the knife's beats.</summary>
    public const int Beat = 30;

    /// <summary>How near the beat a cut lands, in steps, before practice widens it.</summary>
    public const float Window = 4f;

    /// <summary>How far off the beat a cut is wild enough to slip, in steps.</summary>
    public const int WildFrom = 10;

    /// <summary>The chance a wild cut slips, for steady hands; shaky ones slip more.</summary>
    public const float SlipChance = 0.3f;

    /// <summary>How deep a slip cuts.</summary>
    public const float SlipDamage = 3f;

    public Entity Table { get; set; } = Entity.Null;

    /// <summary>The food in hand being chopped.</summary>
    public Entity Food { get; set; } = Entity.Null;

    public string Label { get; private set; } = "";

    /// <summary>Cuts it takes, and cuts made.</summary>
    public int Needed { get; private set; }
    public int Cuts { get; private set; }

    /// <summary>Slips so far: each a cut on the cook, landed when they stop.</summary>
    public int Slips { get; private set; }

    /// <summary>Steps since the knife was last down (for the drawing, and the beat).</summary>
    public int Phase => StepsDone % Beat;

    /// <summary>How far off the beat a cut now would be, in steps.</summary>
    public int Off => Math.Min(Phase, Beat - Phase);

    /// <summary>The last cut's mark, for the drawing: 1 on the beat, 0 a miss, -1 a slip.</summary>
    public int LastMark { get; private set; }

    public override int3? Focus(Simulation sim) => sim.World.IsAlive(Table) && sim.World.TryGet<Thing>(Table, out var t) ? t.Cell : null;

    public override Skill Skill => Skill.Cooking;
    public override float Xp => 8f;
    public override string Title => $"Chopping the {Label}";

    /// <summary>Cuts for a stack of so many: a few for the first, one more for each after.</summary>
    public static int CutsFor(int count) => Math.Clamp(2 + count, 3, 12);

    internal static ChopSession For(Simulation sim, Entity table, Entity food) => new()
    {
        Table = table, Food = food, Label = sim.World.Get<Thing>(food).Def.Label, Needed = CutsFor(sim.World.Get<Stack>(food).Count),
    };

    public override bool StillValid(Simulation sim) =>
        Rations.IsPrepTable(sim, Table) && sim.World.IsAlive(Food) && sim.World.Get<Carrying>(Worker).Item == Food
        && AvatarSystem.InReach(sim, sim.World.Get<Position>(Worker).Cell, Table);

    protected override void Press()
    {
        var off = Off;
        if (off <= Window * Hands.Tolerance)
        {
            Cuts++;
            LastMark = 1;
            Sample(1f - off / (Window * Hands.Tolerance + 1f) * 0.5f);
            if (Cuts >= Needed)
                Done = true;
            return;
        }
        if (off >= WildFrom && Rand.NextFloat() < SlipChance * (1f + Hands.Tremor * 2f))
        {
            Slips++;
            LastMark = -1;
            return;
        }
        LastMark = 0;
    }

    protected override void Step() { }

    public override void Finish(Simulation sim)
    {
        Cut(sim);
        var w = sim.World;
        var def = w.Get<Thing>(Food).Def;
        if (def.ChopsInto is not { } into || !sim.Defs.TryThing(into, out var chopped))
            return;
        var count = w.Get<Stack>(Food).Count;
        var rot = FoodPoisoning.RotOf(sim, Food);
        var made = sim.CreateItem(chopped, count);
        if (w.Has<Rot>(made))
            w.Get<Rot>(made).Progress = rot;
        ref var hands = ref w.Get<Carrying>(Worker);
        if (hands.Item == Food)
            hands.Item = made;
        w.Destroy(Food);
        sim.Messages.Add(MessageKind.Info, $"Chopped {count} {def.Label}: it'll go further, and cook quicker.", w.Get<Thing>(Table).Cell);
    }

    public override void Interrupted(Simulation sim) => Cut(sim);

    /// <summary>The slips land: a cut on an arm for each.</summary>
    private void Cut(Simulation sim)
    {
        var w = sim.World;
        for (var i = 0; i < Slips; i++)
        {
            var arm = w.TryGet<Health>(Worker, out var h) ? h.Body.Parts.FindIndex(p => p.Name == (sim.Rand.Chance(0.5f) ? "LeftArm" : "RightArm")) : -1;
            HealthSystem.Damage(sim, Worker, SlipDamage, Defs.DamageType.Cut, arm);
        }
        if (Slips > 0)
            sim.Messages.Add(MessageKind.Warning, $"{w.Get<Creature>(Worker).Name} cut themselves chopping{(Slips > 1 ? $", {Slips} times" : "")}.", w.Get<Thing>(Table).Cell);
        Slips = 0;
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Table);
        w.Entity(Food);
        w.String(Label);
        w.Int(Needed);
        w.Int(Cuts);
        w.Int(Slips);
        w.Int(LastMark);
    }

    protected override void ReadState(SaveReader r)
    {
        Table = r.Entity();
        Food = r.Entity();
        Label = r.String();
        Needed = Math.Clamp(r.Int(), 1, 12);
        Cuts = Math.Clamp(r.Int(), 0, 12);
        Slips = Math.Clamp(r.Int(), 0, 12);
        LastMark = Math.Clamp(r.Int(), -1, 1);
    }
}

using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Work;

/// <summary>
/// Chopping at the prep table (docs/DESIGN.md, "Cook"): raw food in hand, the knife's held still and
/// the guiding fingers drawn back along the food (A, or the mouse moved left) to bare the next
/// slice, then the knife brought down (F, or a click). A slice as wide as it should be is a clean
/// cut; a sliver or a slab is a poor one. Chop without drawing the fingers back and it's the
/// fingers that are cut: a slip, a real cut on the cook's hand, work for the healer, landed when
/// they put the knife down. Done ahead in a quiet hour, it pays at the rush: chopped food goes a
/// little further and cooks faster in the pot (<see cref="Cooking.SpeedOf"/>).
/// </summary>
public sealed class ChopSession : WorkSession
{
    /// <summary>How far the fingers draw back a step with A held, in slices: a slice in a third of a second.</summary>
    public const float DrawPerStep = 1f / 20f;

    /// <summary>The furthest the fingers draw back, in slices.</summary>
    public const float MostBared = 3f;

    /// <summary>Fingers drawn back less than this (of a slice) are still under the blade: chopping cuts them.</summary>
    public const float SafeFrom = 0.3f;

    /// <summary>How near a slice's width a cut is clean, before practice widens it.</summary>
    public const float EvenWithin = 0.25f;

    /// <summary>Slices bared per width of the picture the mouse moves left (the picture draws a slice an eighth of its width or so).</summary>
    public const float MouseDraw = 8.4f;

    /// <summary>How deep a slip cuts.</summary>
    public const float SlipDamage = 3f;

    public Entity Table { get; set; } = Entity.Null;

    /// <summary>The food in hand being chopped.</summary>
    public Entity Food { get; set; } = Entity.Null;

    public string Label { get; private set; } = "";

    /// <summary>Cuts it takes, and cuts made.</summary>
    public int Needed { get; internal set; }
    public int Cuts { get; private set; }

    /// <summary>Slips so far: each a cut on the cook, landed when they stop.</summary>
    public int Slips { get; private set; }

    /// <summary>How far the fingers are drawn back from the blade, in slices: what the next chop takes.</summary>
    public float Bared { get; private set; }

    /// <summary>How wide the last slice came off, in slices (0 for a slip).</summary>
    public float LastSlice { get; private set; }

    /// <summary>The last cut's mark, for the drawing: 1 clean, 0 a poor slice, -1 a slip.</summary>
    public int LastMark { get; private set; }

    public float Even => EvenWithin * Hands.Tolerance;

    /// <summary>The fingers are still under the blade.</summary>
    public bool Unsafe => Bared < SafeFrom;

    private int _draw;

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
        Rations.IsPrepTable(sim, Table) && sim.World.IsAlive(Food) && AvatarSystem.SupplyItem(sim, Worker, e => e == Food) == Food
        && AvatarSystem.InReach(sim, sim.World.Get<Position>(Worker).Cell, Table);

    /// <summary>A draws the fingers back (-1), D pushes them up to the blade again.</summary>
    protected override void Axis(int value) => _draw = -value;

    /// <summary>The mouse moved left draws the fingers back as far as it moves; right, up again.</summary>
    protected override void Pointed(float u, float v)
    {
        if (PointU is { } was)
            Bared = Math.Clamp(Bared + (was - u) * MouseDraw, 0f, MostBared);
    }

    protected override void Press()
    {
        if (Done)
            return;
        if (Unsafe)
        {
            // The fingers never moved: the knife comes down on them.
            Slips++;
            (LastMark, LastSlice) = (-1, 0f);
            return;
        }
        var off = MathF.Abs(Bared - 1f);
        var clean = off <= Even;
        LastMark = clean ? 1 : 0;
        LastSlice = Bared;
        Sample(clean ? 1f - 0.3f * off / Even : Math.Clamp(1f - off, 0.15f, 0.6f));
        Bared = 0f; // the slice falls away: the fingers are up against the blade again
        Cuts++;
        if (Cuts >= Needed)
            Done = true;
    }

    protected override void Step()
    {
        if (_draw == 0)
            return;
        // An unsteady hand doesn't draw back quite evenly.
        Bared = Math.Clamp(Bared + _draw * DrawPerStep * (1f + (Rand.NextFloat() * 2f - 1f) * Hands.Tremor), 0f, MostBared);
    }

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
        var inHand = hands.Item == Food;
        if (inHand)
            hands.Item = Entity.Null;
        w.Destroy(sim.TakeFromStack(Food, count)); // out of the pack, if it was there
        if (inHand)
            w.Get<Carrying>(Worker).Item = made;
        else
            Inventory.Stow(sim, Worker, made);
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
        w.Float(Bared);
        w.Float(LastSlice);
        w.Int(_draw);
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
        if (r.Version >= 47) // the fingers drawn back, not the beat (format 47)
        {
            Bared = Math.Clamp(r.Float(), 0f, MostBared);
            LastSlice = Math.Clamp(r.Float(), 0f, MostBared);
            _draw = Math.Sign(r.Int());
        }
    }
}

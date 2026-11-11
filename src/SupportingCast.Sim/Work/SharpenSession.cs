using SupportingCast.Sim.Combat;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Saving;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Work;

/// <summary>
/// Putting an edge back on a blade with a whetstone. Hold F to grind; the blade's angle wanders
/// and A/D steer it back into the band. Grinding at the right angle brings the edge up, at the
/// wrong one does little good. Grind too long without easing off and the steel overheats and
/// loses its temper. A stone takes out dullness, not chips: grinding at a chipped blade only
/// wears metal away (<see cref="WeaponWear"/>).
/// </summary>
public sealed class SharpenSession : WorkSession
{
    /// <summary>Half the width of the right angle band (0..1 scale), before skill widens it.</summary>
    public const float BandHalfWidth = 0.1f;

    /// <summary>Heat gained per step of grinding, and lost per step eased off. Full heat ruins the temper.</summary>
    public const float HeatPerStep = 0.0035f, CoolPerStep = 0.006f;

    /// <summary>How far one step of steering turns the blade.</summary>
    public const float SteerPerStep = 0.008f;

    /// <summary>Steps of good grinding a fully blunt blade needs (a slightly dull one needs less).</summary>
    public const int StepsForBlunt = 540;

    public Entity Item { get; set; } = Entity.Null;
    public string ItemLabel { get; set; } = "";

    /// <summary>How dull it was at the start (sets how long it takes).</summary>
    public float StartDullness { get; set; }

    /// <summary>The blade had chips a stone can't take out.</summary>
    public bool Chipped { get; set; }

    /// <summary>The blade's angle on the stone, 0..1; the band is centred on 0.5.</summary>
    public float Angle { get; private set; } = 0.5f;

    /// <summary>Heat in the steel, 0..1.</summary>
    public float Heat { get; private set; }

    /// <summary>How far the edge has come back, 0..1.</summary>
    public float Progress { get; private set; }

    /// <summary>Overheated: the temper's gone.</summary>
    public bool Burned { get; private set; }

    private float _drift;
    private int _steer;

    public float HalfWidth => BandHalfWidth * Hands.Tolerance;
    public bool InBand => MathF.Abs(Angle - 0.5f) <= HalfWidth;
    private int StepsNeeded => Math.Max(60, (int)(StepsForBlunt * StartDullness));

    public override Skill Skill => Skill.Crafting;
    public override float Xp => 30f;
    public override string Title => $"Sharpening the {ItemLabel}";

    /// <summary>Can this worker put an edge on this item now? Null if so, else why not.</summary>
    public static string? Problem(Simulation sim, Entity worker, Entity item)
    {
        var w = sim.World;
        if (!w.IsAlive(item) || !Inventory.OnPerson(sim, worker, item))
            return "you need the blade in hand";
        if (!WeaponWear.Edged(w.Get<Thing>(item).Def))
            return "it has no edge to sharpen";
        if (Whetstone(sim, worker).IsNull)
            return "you need a whetstone";
        if (WeaponWear.Dullness(sim, item) <= 0.02f)
            return "it's already keen";
        return null;
    }

    /// <summary>A whetstone on the worker's belt or in their pack or hands.</summary>
    public static Entity Whetstone(Simulation sim, Entity worker)
    {
        var belt = Toolbelt.Best(sim, worker, "Whetstone");
        if (!belt.IsNull)
            return belt;
        var held = sim.World.Get<Carrying>(worker).Item;
        if (IsWhetstone(sim, held))
            return held;
        var pack = sim.PackOf(worker);
        return pack.IsNull ? Entity.Null : sim.StoredIn(pack).FirstOrDefault(i => IsWhetstone(sim, i), Entity.Null);
    }

    private static bool IsWhetstone(Simulation sim, Entity e) =>
        sim.World.IsAlive(e) && sim.World.TryGet<Thing>(e, out var t) && t.Def.Tool is { Kind: "Whetstone" };

    public static void TryStart(Simulation sim, Entity worker, Entity item)
    {
        if (Problem(sim, worker, item) is { } why)
        {
            sim.Messages.Add(MessageKind.Info, $"Can't sharpen that: {why}.", sim.LocationOf(worker));
            return;
        }
        var w = sim.World;
        WorkSystem.Start(sim, worker, new SharpenSession
        {
            Item = item, ItemLabel = sim.LabelOf(item), StartDullness = WeaponWear.Dullness(sim, item),
            Chipped = WeaponWear.HpFraction(sim, item) < WeaponWear.MendedHp,
        });
    }

    public override bool StillValid(Simulation sim) =>
        sim.World.IsAlive(Item) && Inventory.OnPerson(sim, Worker, Item) && !Whetstone(sim, Worker).IsNull;

    protected internal override void Begin()
    {
        Angle = Rand.Range(0.2f, 0.8f);
        _drift = 0f;
    }

    protected override void Axis(int value) => _steer = value;

    protected override void Step()
    {
        // The angle wanders on its own, faster for unsteady hands; steering pushes it back.
        _drift = Math.Clamp(_drift + (Rand.NextFloat() * 2f - 1f) * 0.0006f * (1f + Hands.Tremor * 4f), -0.004f, 0.004f);
        Angle = Math.Clamp(Angle + _drift + _steer * SteerPerStep, 0f, 1f);
        if (!Held)
        {
            Heat = Math.Max(0f, Heat - CoolPerStep);
            return;
        }
        Heat += HeatPerStep;
        if (Heat >= 1f)
        {
            Burned = true;
            Done = true;
            return;
        }
        var inBand = InBand;
        Progress += (inBand ? 1f : 0.15f) / StepsNeeded;
        if (StepsDone % 10 == 0)
            Sample(inBand ? 1f : 0.2f);
        Done = Progress >= 1f;
    }

    /// <summary>
    /// The edge comes back as well as the grinding went. Grinding at chips only wears the blade, and
    /// an overheated blade loses its temper: worse steel, and an edge that won't hold.
    /// </summary>
    public override void Finish(Simulation sim)
    {
        var w = sim.World;
        var at = sim.LocationOf(Worker);
        var dull = WeaponWear.Dullness(sim, Item);
        WeaponWear.SetDullness(sim, Item, dull * (1f - Math.Clamp(Progress, 0f, 1f) * (0.4f + 0.6f * Quality)));
        if (Chipped)
        {
            ref var t = ref w.Get<Thing>(Item);
            t.Hp = Math.Max(1f, t.Hp - t.MaxHp * 0.05f);
            sim.Messages.Add(MessageKind.Warning, $"The stone can't take out chips this deep: grinding only wore the {ItemLabel} thinner.", at);
        }
        if (Burned)
        {
            if (w.TryGet<Crafted>(Item, out var made) && made.Quality > Components.Quality.Crude)
                w.Get<Crafted>(Item).Quality = made.Quality - 1;
            ref var t = ref w.Get<Thing>(Item);
            t.Hp = Math.Max(1f, t.Hp - t.MaxHp * 0.1f);
            sim.Messages.Add(MessageKind.Warning, $"The {ItemLabel} turned blue on the stone: the temper's gone out of it.", at);
        }
        else if (!Chipped)
            sim.Messages.Add(MessageKind.Info, $"Put an edge back on the {ItemLabel}.", at);
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Item);
        w.String(ItemLabel);
        w.Float(StartDullness);
        w.Bool(Chipped);
        w.Float(Angle);
        w.Float(Heat);
        w.Float(Progress);
        w.Bool(Burned);
        w.Float(_drift);
        w.Int(_steer);
    }

    protected override void ReadState(SaveReader r)
    {
        Item = r.Entity();
        ItemLabel = r.String();
        StartDullness = r.Float();
        Chipped = r.Bool();
        Angle = r.Float();
        Heat = r.Float();
        Progress = r.Float();
        Burned = r.Bool();
        _drift = r.Float();
        _steer = Math.Sign(r.Int());
    }
}

/// <summary>Put an edge on a blade you have on you, with a whetstone (<see cref="SharpenSession"/>).</summary>
public sealed record SharpenCommand(Entity Avatar, Entity Item) : ISimCommand, Core.IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (Core.InventoryCommand.Ready(sim, Avatar) && Core.InventoryCommand.IsItem(sim, Item) && !sim.World.Has<WorkSession>(Avatar))
            SharpenSession.TryStart(sim, Avatar, Item);
    }
}

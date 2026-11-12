using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Work;

/// <summary>
/// Drawing a drink from a cask. Hold F to pour: the drink rises toward a fill line, and pouring
/// raises a head of foam that settles back into the drink when you stop. A/D tilt the tankard:
/// tilted, it foams less but fills slower. Let go and let it settle; once it's still, it's poured.
/// The best pour settles at the line with a decent head; overfill it and it spills (a wasted
/// serving). The quality is the drink's (<see cref="Crafted"/>), and the customer can taste it.
/// It's poured into a clean tankard (<see cref="Vessels"/>), in hand or from the pack.
/// </summary>
public sealed class PourSession : WorkSession
{
    /// <summary>Rise per step of pouring, straight and fully tilted.</summary>
    public const float PourUpright = 0.006f, PourTilted = 0.0035f;

    /// <summary>Foam raised per unit poured, straight and fully tilted.</summary>
    public const float FoamUpright = 0.9f, FoamTilted = 0.15f;

    /// <summary>The share of foam that settles into drink each step it isn't poured into.</summary>
    public const float SettlePerStep = 0.012f;

    /// <summary>Steps of stillness, after pouring, that finish it.</summary>
    public const int SettledSteps = 50;

    /// <summary>The head a good pour has, as a share of the glass.</summary>
    public const float GoodHead = 0.08f;

    public Entity Cask { get; set; } = Entity.Null;
    public string DrinkLabel { get; set; } = "";

    /// <summary>Where the top of the head should come to, 0..1 of the tankard.</summary>
    public float Line { get; private set; }

    /// <summary>Drink in the tankard, 0..1.</summary>
    public float Level { get; private set; }

    /// <summary>Foam on top, 0..1.</summary>
    public float Foam { get; private set; }

    /// <summary>The tankard's tilt: 0 upright, 1 fully tilted.</summary>
    public float Tilt { get; private set; }

    public bool Spilled { get; private set; }

    private int _tiltDir;
    private int _still;
    private bool _poured;

    public float Top => Level + Foam;

    public override int3? Focus(Simulation sim) => sim.World.IsAlive(Cask) && sim.World.TryGet<Thing>(Cask, out var t) ? t.Cell : null;

    public override Skill Skill => Skill.Cooking;
    public override float Xp => 20f;
    public override string Title => $"Pouring {DrinkLabel}";

    /// <summary>Servings left in a cask (Entity.Null or not a cask: 0).</summary>
    public static int ServingsLeft(Simulation sim, Entity cask)
    {
        var w = sim.World;
        if (!w.IsAlive(cask) || !w.TryGet<Thing>(cask, out var t) || t.Def.Building?.Cask is not { } props)
            return 0;
        return Math.Max(0, props.Servings - (w.TryGet<CaskDrawn>(cask, out var d) ? d.Drawn : 0));
    }

    public static void TryStart(Simulation sim, Entity worker, Entity cask)
    {
        var w = sim.World;
        var at = w.Get<Thing>(cask).Cell;
        if (ServingsLeft(sim, cask) <= 0)
        {
            sim.Messages.Add(MessageKind.Info, $"The {w.Get<Thing>(cask).Label} is dry.", at);
            return;
        }
        var into = w.Get<Thing>(cask).Def.Building!.Cask!.Into;
        if (!Vessels.Ready(sim, worker, into))
        {
            sim.Messages.Add(MessageKind.Info, Vessels.CleanOn(sim, worker, into).IsNull
                ? $"You need a clean {sim.Defs.Thing(into).Label} to pour into: there are some on the shelf behind the bar."
                : $"You need a hand free for the {sim.Defs.Thing(into).Label}.", at);
            return;
        }
        var drink = sim.Defs.Thing(w.Get<Thing>(cask).Def.Building!.Cask!.Pours);
        WorkSystem.Start(sim, worker, new PourSession { Cask = cask, DrinkLabel = drink.Label });
    }

    public override bool StillValid(Simulation sim) =>
        ServingsLeft(sim, Cask) > 0 && AvatarSystem.InReach(sim, sim.World.Get<Position>(Worker).Cell, Cask)
        && !Vessels.CleanOn(sim, Worker, sim.World.Get<Thing>(Cask).Def.Building!.Cask!.Into).IsNull;

    protected internal override void Begin() => Line = Rand.Range(0.78f, 0.9f);

    protected override void Axis(int value) => _tiltDir = value;

    protected override void Step()
    {
        Tilt = Math.Clamp(Tilt + _tiltDir * 0.03f, 0f, 1f);
        if (Held)
        {
            var poured = PourUpright + (PourTilted - PourUpright) * Tilt;
            Level += poured;
            Foam += poured * (FoamUpright + (FoamTilted - FoamUpright) * Tilt);
            _poured = true;
            _still = 0;
        }
        else
        {
            // The head settles: some of it back into drink, the rest just goes.
            var settled = Foam * SettlePerStep;
            Foam -= settled;
            Level += settled * 0.4f;
            if (_poured)
                _still++;
        }
        if (Top > 1f)
        {
            Spilled = true;
            Done = true;
            return;
        }
        if (_poured && _still >= SettledSteps)
        {
            // How close the top came to the line, and how good a head it kept.
            var off = MathF.Abs(Top - Line);
            var head = MathF.Abs(Foam - GoodHead);
            Sample(Math.Clamp(1f - off * 5f / Hands.Tolerance - head * 3f, 0f, 1f));
            Done = true;
        }
    }

    public override void Finish(Simulation sim)
    {
        var w = sim.World;
        Draw(sim, Cask);
        var at = w.Get<Thing>(Cask).Cell;
        if (Spilled)
        {
            sim.Mess.Spill(sim, Hospitality.MessKind.SpilledAle, w.Get<Position>(Worker).Cell);
            sim.Messages.Add(MessageKind.Info, $"It foams over the rim and down your arm: that {DrinkLabel} is on the floor.", at);
            return;
        }
        Vessels.Use(sim, Worker, w.Get<Thing>(Cask).Def.Building!.Cask!.Into);
        var drink = PourInto(sim, Worker, Cask, Crafting.QualityFrom(Quality));
        sim.Messages.Add(MessageKind.Info, $"Poured a {Crafting.QualityLabel(w.Get<Crafted>(drink).Quality)} {DrinkLabel}.", at);
    }

    /// <summary>One serving out of the cask.</summary>
    public static void Draw(Simulation sim, Entity cask)
    {
        if (sim.World.Has<CaskDrawn>(cask))
            sim.World.Get<CaskDrawn>(cask).Drawn++;
        else
            sim.World.Add(cask, new CaskDrawn { Drawn = 1 });
    }

    /// <summary>A drink of a quality from a cask, into the server's hands (else pack, else at their feet).</summary>
    public static Entity PourInto(Simulation sim, Entity server, Entity cask, Quality quality)
    {
        var w = sim.World;
        var def = sim.Defs.Thing(w.Get<Thing>(cask).Def.Building!.Cask!.Pours);
        var drink = sim.CreateItem(def, 1);
        w.Add(drink, new Crafted { Quality = quality, Maker = server });
        ref var carrying = ref w.Get<Carrying>(server);
        if (carrying.Item.IsNull)
            carrying.Item = drink;
        else
            Inventory.Stow(sim, server, drink);
        return drink;
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Cask);
        w.String(DrinkLabel);
        w.Float(Line);
        w.Float(Level);
        w.Float(Foam);
        w.Float(Tilt);
        w.Bool(Spilled);
        w.Int(_tiltDir);
        w.Int(_still);
        w.Bool(_poured);
    }

    protected override void ReadState(SaveReader r)
    {
        Cask = r.Entity();
        DrinkLabel = r.String();
        Line = r.Float();
        Level = r.Float();
        Foam = r.Float();
        Tilt = r.Float();
        Spilled = r.Bool();
        _tiltDir = Math.Sign(r.Int());
        _still = r.Int();
        _poured = r.Bool();
    }
}

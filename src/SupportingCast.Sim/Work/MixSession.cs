using SupportingCast.Sim.Components;
using SupportingCast.Sim.Core;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Saving;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Work;

/// <summary>What's in a mixed drink: the sum of its base and adds, as well as it was mixed. A property of the drink itself.</summary>
public struct DrinkMix
{
    public float Strength, Warming, Rousing, Numbing, Loosening;

    /// <summary>What went in, for the label ("honey and spirits"); empty for a named drink.</summary>
    public string Adds;
}

/// <summary>
/// Mixing a drink at the bar: a press-on-cue sequence. The cues come along the bar one after
/// another, F, A or D, and each is pressed as it reaches the mark: on time is a good hand, early,
/// late or the wrong key is a sloppy one. A named drink has its own cues; a house mix takes one
/// per thing added, so any combination can be mixed. The drink is a serving from a cask behind the
/// bar, one of each add from the mixer's hands or pack, and a clean tankard; what it does
/// (<see cref="DrinkMix"/>) is what's in it, more of it the better it's mixed.
/// </summary>
public sealed class MixSession : WorkSession
{
    /// <summary>Steps before the first cue reaches the mark, and between cues.</summary>
    public const int Lead = 60, Gap = 40;

    /// <summary>Steps either side of the mark a press still counts.</summary>
    public const int Window = 10;

    public Entity Bar { get; set; } = Entity.Null;

    /// <summary>The drink being mixed (a thing def name), and for a house mix, what's added (comma separated).</summary>
    public string Drink { get; set; } = "";
    public string Adds { get; set; } = "";

    /// <summary>The cues, F, A or D, in order.</summary>
    public string Cues { get; set; } = "";

    /// <summary>Cues dealt with so far, and how well each landed.</summary>
    public int Resolved { get; private set; }
    public int Hits { get; private set; }

    /// <summary>The last cue's result, for the scene: true good, false sloppy, null none yet.</summary>
    public bool? Last { get; private set; }

    private string _label = "";
    private int _lastAxis;

    public override Skill Skill => Skill.Cooking;
    public override float Xp => 15f;
    public override string Title => $"Mixing a {_label}";

    /// <summary>The step a cue reaches the mark.</summary>
    public int DueOf(int i) => Lead + i * Gap;

    /// <summary>The adds a drink takes: a named one's own, or a house mix's choice.</summary>
    public static List<ThingDef> AddsOf(Simulation sim, ThingDef drink, string? adds) =>
        drink.Mix is { House: true }
            ? (adds ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(n => sim.Defs.TryThing(n, out var d) ? d : null).OfType<ThingDef>().Where(d => d.MixCue is { Length: > 0 }).Distinct().Take(4).ToList()
            : drink.Mix!.Adds.Select(sim.Defs.Thing).ToList();

    /// <summary>The cask behind the bar a mix's base comes from: one with the base in it, near the bar.</summary>
    public static Entity CaskFor(Simulation sim, Entity bar, ThingDef drink)
    {
        var w = sim.World;
        var at = w.Get<Thing>(bar).Cell;
        foreach (var e in w.Each<Thing>())
        {
            var t = w.Get<Thing>(e);
            if (t.Spawned && t.Def.Building?.Cask is { } c && c.Pours == drink.Mix!.Base && Maps.Map.Distance(t.Cell, at) <= 3
                && PourSession.ServingsLeft(sim, e) > 0)
                return e;
        }
        return Entity.Null;
    }

    /// <summary>Why a drink can't be mixed here now, or null if it can.</summary>
    public static string? WhyNot(Simulation sim, Entity mixer, Entity bar, ThingDef drink, string? adds)
    {
        if (drink.Mix == null)
            return "That isn't a mixed drink.";
        var cask = CaskFor(sim, bar, drink);
        if (cask.IsNull)
            return $"There's no {sim.Defs.Thing(drink.Mix.Base).Label} behind the bar.";
        var into = sim.World.Get<Thing>(cask).Def.Building!.Cask!.Into;
        if (!Vessels.Ready(sim, mixer, into))
            return $"You need a clean {sim.Defs.Thing(into).Label}, and a hand free for it.";
        var list = AddsOf(sim, drink, adds);
        if (drink.Mix.House && list.Count == 0)
            return "A house mix needs something added.";
        foreach (var add in list)
            if (AvatarSystem.Supply(sim, mixer, d => d == add).IsNull)
                return $"You need some {add.Label} to hand: there's some on the shelf behind the bar.";
        return null;
    }

    public static void TryStart(Simulation sim, Entity mixer, Entity bar, ThingDef drink, string? adds)
    {
        var w = sim.World;
        if (!w.IsAlive(bar) || w.Get<Thing>(bar).Def.Building is not { Bar: true } || !AvatarSystem.InReach(sim, w.Get<Position>(mixer).Cell, bar))
            return;
        if (WhyNot(sim, mixer, bar, drink, adds) is { } why)
        {
            sim.Messages.Add(MessageKind.Info, why, w.Get<Thing>(bar).Cell);
            return;
        }
        var list = AddsOf(sim, drink, adds);
        var cues = drink.Mix!.House ? string.Concat(list.Select(d => d.MixCue![0])) + "F" : drink.Mix.Cues;
        WorkSystem.Start(sim, mixer, new MixSession
        {
            Bar = bar, Drink = drink.Name, Adds = string.Join(",", list.Select(d => d.Name)), Cues = cues,
            _label = drink.Mix.House ? $"house mix of {string.Join(" and ", list.Select(d => d.Label))}" : drink.Label,
        });
    }

    public override bool StillValid(Simulation sim) =>
        sim.World.IsAlive(Bar) && AvatarSystem.InReach(sim, sim.World.Get<Position>(Worker).Cell, Bar);

    protected override void Press() => Input('F');

    protected override void Axis(int value)
    {
        if (value == _lastAxis)
            return;
        _lastAxis = value;
        if (value != 0)
            Input(value < 0 ? 'A' : 'D');
    }

    private void Input(char key)
    {
        if (Resolved >= Cues.Length)
            return;
        var off = Math.Abs(StepsDone - DueOf(Resolved));
        if (off > Window * 2)
            return; // nowhere near a cue: a stray press
        var good = key == Cues[Resolved] && off <= Window;
        Last = good;
        if (good)
            Hits++;
        Sample(good ? 1f - off / (float)(Window * 2) : 0.1f);
        Resolved++;
        if (Resolved >= Cues.Length)
            Done = true;
    }

    protected override void Step()
    {
        // A cue let past the mark is missed.
        if (Resolved < Cues.Length && StepsDone > DueOf(Resolved) + Window * 2)
        {
            Last = false;
            Sample(0f);
            Resolved++;
            if (Resolved >= Cues.Length)
                Done = true;
        }
    }

    public override void Finish(Simulation sim)
    {
        var w = sim.World;
        var drink = sim.Defs.Thing(Drink);
        var at = w.Get<Thing>(Bar).Cell;
        if (WhyNot(sim, Worker, Bar, drink, Adds) is { } why)
        {
            sim.Messages.Add(MessageKind.Info, why, at);
            return;
        }
        var cask = CaskFor(sim, Bar, drink);
        var into = w.Get<Thing>(cask).Def.Building!.Cask!.Into;
        PourSession.Draw(sim, cask);
        var adds = AddsOf(sim, drink, Adds);
        foreach (var add in adds)
            w.Destroy(Inventory.TakeFromPerson(sim, Worker, AvatarSystem.Supply(sim, Worker, d => d == add), 1));
        Vessels.Use(sim, Worker, into);
        var made = sim.CreateItem(drink, 1);
        var quality = Crafting.QualityFrom(Quality);
        w.Add(made, new Crafted { Quality = quality, Maker = Worker });
        w.Add(made, MixOf(sim, drink, adds, Quality));
        ref var carrying = ref w.Get<Carrying>(Worker);
        if (carrying.Item.IsNull)
            carrying.Item = made;
        else
            Inventory.Stow(sim, Worker, made);
        sim.Messages.Add(MessageKind.Info, $"Mixed a {Crafting.QualityLabel(quality)} {_label}.", at);
    }

    /// <summary>What a mix does: its base and adds summed, the better mixed, the more of it.</summary>
    public static DrinkMix MixOf(Simulation sim, ThingDef drink, List<ThingDef> adds, float quality)
    {
        var m = new DrinkMix { Adds = drink.Mix is { House: true } ? string.Join(" and ", adds.Select(d => d.Label)) : "" };
        var parts = new List<ThingDef> { sim.Defs.Thing(drink.Mix!.Base) };
        parts.AddRange(adds);
        var scale = 0.6f + 0.4f * Math.Clamp(quality, 0f, 1f);
        foreach (var p in parts)
            if (p.DrinkEffect is { } e)
            {
                m.Strength += e.Strength;
                m.Warming += e.Warming * scale;
                m.Rousing += e.Rousing * scale;
                m.Numbing += e.Numbing * scale;
                m.Loosening += e.Loosening * scale;
            }
        return m;
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Bar);
        w.String(Drink);
        w.String(Adds);
        w.String(Cues);
        w.Int(Resolved);
        w.Int(Hits);
        w.Int(Last is { } l ? (l ? 1 : 0) : -1);
        w.String(_label);
        w.Int(_lastAxis);
    }

    protected override void ReadState(SaveReader r)
    {
        Bar = r.Entity();
        Drink = r.String();
        Adds = r.String();
        Cues = r.String();
        Resolved = r.Int();
        Hits = r.Int();
        var last = r.Int();
        Last = last < 0 ? null : last == 1;
        _label = r.String();
        _lastAxis = Math.Sign(r.Int());
    }
}

/// <summary>Mix a drink at the bar: a named one, or a house mix of what's listed (<see cref="MixSession"/>).</summary>
public sealed record MixCommand(Entity Avatar, Entity Bar, ThingDef Drink, string? Adds) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (!InventoryCommand.Ready(sim, Avatar) || !sim.World.IsAlive(Bar) || !sim.World.Has<Thing>(Bar) || Drink?.Mix == null)
            return;
        MixSession.TryStart(sim, Avatar, Bar, Drink, Adds?.Length > 200 ? null : Adds);
    }
}

using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Work;

/// <summary>
/// Smelling food for whether it's turned: the cook's way into hidden rot, as feeling a wound over
/// is the healer's. Space takes a sniff of a stack (in the larder, in hand) or of the pot, and it
/// smells fresh, fine, on the turn or off: roughly, the less practised the nose, the rougher. A pot
/// only ever smells "not quite right": what turned in it can't be picked out. The true rot is read
/// once, as the sniffing starts, and only the words leave the session.
/// </summary>
public sealed class SniffSession : WorkSession
{
    /// <summary>Steps between sniffs: a nose needs a breath of air between.</summary>
    public const int Breath = 20;

    /// <summary>What's being smelt: a stack of food or a stew pot.</summary>
    public Entity Target { get; set; } = Entity.Null;

    /// <summary>A pot (its contents can't be told apart), or a stack.</summary>
    public bool IsPot { get; private set; }

    /// <summary>What it is, for the window.</summary>
    public string Label { get; private set; } = "";

    /// <summary>Hidden: how far gone it is. Read only within the session, for the sniff.</summary>
    internal float Truth { get; private set; }

    /// <summary>What the last sniff said ("" before the first), and how many there have been.</summary>
    public string Smell { get; private set; } = "";
    public int Sniffs { get; private set; }

    private int _sinceSniff = Breath;

    public override int3? Focus(Simulation sim) =>
        sim.World.IsAlive(Target) && sim.World.TryGet<Thing>(Target, out var t) && t.Cell.X >= 0 ? t.Cell : null;

    public override Skill Skill => Skill.Cooking;
    public override float Xp => 3f;
    public override string Title => IsPot ? "Smelling the pot" : $"Smelling the {Label}";

    /// <summary>A session for this target, or null if it's nothing a nose can tell anything from.</summary>
    internal static SniffSession? For(Simulation sim, Entity target)
    {
        var w = sim.World;
        if (Cooking.IsPot(sim, target))
        {
            var p = Cooking.PotOf(sim, target);
            return p.Empty ? null : new SniffSession { Target = target, IsPot = true, Label = "stew pot", Truth = p.Rot };
        }
        if (!w.IsAlive(target) || !w.TryGet<Thing>(target, out var t) || !w.Has<Rot>(target))
            return null;
        return new SniffSession { Target = target, Label = t.Def.Label, Truth = FoodPoisoning.RotOf(sim, target) };
    }

    /// <summary>Can this person get their nose to it: on them, in a store they can reach, or a pot beside them?</summary>
    public static bool Reaches(Simulation sim, Entity who, Entity target)
    {
        var w = sim.World;
        if (!w.IsAlive(target))
            return false;
        if (Cooking.IsPot(sim, target))
            return AvatarSystem.InReach(sim, w.Get<Position>(who).Cell, target);
        if (Inventory.OnPerson(sim, who, target))
            return true;
        return w.TryGet<Stored>(target, out var stored) && Inventory.CanReachContainer(sim, who, stored.In);
    }

    public override bool StillValid(Simulation sim) =>
        Reaches(sim, Worker, Target) && (!IsPot || !Cooking.PotOf(sim, Target).Empty);

    protected override void Act() => Sniff();

    private void Sniff()
    {
        if (_sinceSniff < Breath)
            return;
        _sinceSniff = 0;
        Sniffs++;
        var reading = Truth + Rand.Range(-1f, 1f) * 0.12f / Hands.Tolerance;
        Smell = IsPot ? PotWord(reading) : Word(reading);
    }

    /// <summary>What a nose makes of food this far gone (noise and all).</summary>
    public static string Word(float reading) => reading switch
    {
        < 0.15f => "fresh",
        < 0.35f => "fine",
        < 0.5f => "on the turn",
        _ => "off",
    };

    /// <summary>A pot hides what's in it: at most, something isn't right.</summary>
    public static string PotWord(float reading) => reading < 0.3f ? "good" : "not quite right";

    protected override void Step()
    {
        if (_sinceSniff < Breath)
            _sinceSniff++;
    }

    /// <summary>Done when they step away: nothing changes but what they know.</summary>
    public override void Finish(Simulation sim) { }

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Target);
        w.Bool(IsPot);
        w.String(Label);
        w.Float(Truth);
        w.String(Smell);
        w.Int(Sniffs);
        w.Int(_sinceSniff);
    }

    protected override void ReadState(SaveReader r)
    {
        Target = r.Entity();
        IsPot = r.Bool();
        Label = r.String();
        Truth = Math.Clamp(r.Float(), 0f, 1f);
        Smell = r.String();
        Sniffs = r.Int();
        _sinceSniff = r.Int();
    }
}

/// <summary>Take a sniff of a stack of food, or a pot (<see cref="SniffSession"/>).</summary>
public sealed record SniffCommand(Entity Avatar, Entity Target) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (!InventoryCommand.Ready(sim, Avatar) || !SniffSession.Reaches(sim, Avatar, Target) || SniffSession.For(sim, Target) is not { } s)
            return;
        WorkSystem.Start(sim, Avatar, s);
    }
}

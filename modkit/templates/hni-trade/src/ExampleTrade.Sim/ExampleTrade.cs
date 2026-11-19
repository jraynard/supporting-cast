using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Bots;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Modding;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace ExampleTrade;

/// <summary>
/// The trade's sim half (docs/MODDING.md): everything the sim must know of it, registered here. Worked
/// examples to read: mods/musician (a player's trade, played anywhere) and mods/carpenter (a workplace,
/// AI jobs, systems, components, lessons and a bot).
/// </summary>
public sealed class ExampleTradeMod : ISimMod
{
    public void Register(SimRegistry registry)
    {
        registry.Trade(new ExampleTradeModule());
        registry.Bot(bot => new ExampleTradeBot(bot));
        // --drill exampletrade: the tool in hand and the mini-game begun, to try it at once.
        registry.Drill("exampletrade", (sim, me) =>
        {
            var tool = ExampleTradeModule.ToolOf(sim, me);
            if (tool.IsNull)
                Inventory.Stow(sim, me, tool = sim.CreateItem(sim.Defs.Thing(ExampleTradeModule.ToolDef), 1));
            if (!sim.World.Has<WorkSession>(me))
                WorkSystem.Start(sim, me, new ExampleTradeSession { Tool = tool });
            return Entity.Null;
        });
    }
}

/// <summary>The trade's hooks (<see cref="ITradeModule"/>): each has a default that does nothing; answer only what's yours.</summary>
public sealed class ExampleTradeModule : ITradeModule
{
    public const string ToolDef = "ExampleTradeTool", WareDef = "ExampleTradeWare";

    public string? Trade => "ExampleTrade";

    public static Entity ToolOf(Simulation sim, Entity who) =>
        Inventory.CarriedOf(sim, who, sim.Defs.Thing(ToolDef)).FirstOrDefault(Entity.Null);

    /// <summary>F where you stand, the tool carried: begin the mini-game.</summary>
    public AvatarAction? EarlyActionAt(Simulation sim, Entity avatar, int3 cell, int3 here)
    {
        if (cell != here || sim.World.Has<WorkSession>(avatar) || ToolOf(sim, avatar) is not { IsNull: false } tool)
            return null;
        return new AvatarAction(AvatarActionKind.Custom, $"Work with the {sim.World.Get<Thing>(tool).Label}", cell, tool);
    }

    public void Perform(Simulation sim, Entity avatar, AvatarAction action)
    {
        if (sim.World.IsAlive(action.Target) && !sim.World.Has<WorkSession>(avatar))
            WorkSystem.Start(sim, avatar, new ExampleTradeSession { Tool = action.Target });
    }
}

/// <summary>
/// The mini-game: a marker sweeps back and forth; F when it's over the mark is a good stroke, and
/// <see cref="Strokes"/> of them make a ware as good as they were. A first rough game, to be found by
/// playing it: change one thing at a time and play it again (docs/MODDING.md, "found, not written").
/// <para><see cref="WorkSession.Advance"/> runs this ahead of the sim (the predictor) and in it, so Step and
/// the inputs read only the session's own state; the world is read in <see cref="StillValid"/> and changed in
/// <see cref="Finish"/>. Lists or arrays it holds must be copied in <see cref="WorkSession.Cloned"/>.</para>
/// </summary>
public sealed class ExampleTradeSession : WorkSession
{
    public const int Strokes = 3;

    /// <summary>Seconds the marker takes there and back.</summary>
    public const float Sweep = 1.6f;

    /// <summary>Where the mark sits on the bar (0..1), and how wide it is.</summary>
    public const float Mark = 0.7f, MarkWidth = 0.12f;

    public Entity Tool;

    /// <summary>Good strokes so far (the base class's <see cref="WorkSession.Done"/> is whether it's over).</summary>
    public int Good;

    /// <summary>Where the marker is now (0..1): read by the view and the bot, never by the world.</summary>
    public float Marker => Triangle(StepsDone, Sweep);

    public override Skill Skill => Skill.Crafting;
    public override float Xp => 10f;
    public override string Title => "Working the ware";

    public override bool StillValid(Simulation sim) => sim.World.IsAlive(Tool) && Inventory.OnPerson(sim, Worker, Tool);

    protected override void Step() { }

    protected override void Press()
    {
        // How near the mark, 1 on it to 0 at the bar's far end; a good stroke counts.
        var off = MathF.Abs(Marker - Mark);
        Sample(Math.Clamp(1f - off / 0.5f, 0f, 1f));
        if (off <= MarkWidth / 2f)
            Good++;
        if (Good >= Strokes)
            Done = true;
    }

    /// <summary>
    /// The ware, as good as the strokes were: an item keeps how well it was made as <see cref="Crafted"/>
    /// (shown as its grade, and worth more), as everything the game's trades make does.
    /// </summary>
    public override void Finish(Simulation sim)
    {
        var ware = sim.CreateItem(sim.Defs.Thing(ExampleTradeModule.WareDef), 1);
        sim.World.Add(ware, new Crafted { Quality = Crafting.QualityFrom(Quality), Maker = Worker });
        Inventory.Stow(sim, Worker, ware);
        sim.Messages.Tell(Worker, MessageKind.Good, Quality >= 0.75f ? "A fine one." : "It'll do.", sim.LocationOf(Worker));
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Tool);
        w.Int(Good);
    }

    protected override void ReadState(SaveReader r)
    {
        Tool = r.Entity();
        Good = Math.Clamp(r.Int(), 0, Strokes);
    }
}

/// <summary>
/// How a bot plays the trade (<see cref="IBotTrade"/>): only through what it perceives and the inputs a
/// player has. Here, it presses when the marker's over the mark. See mods/carpenter's CarpenterBot for one
/// that works a workplace between orders.
/// </summary>
public sealed class ExampleTradeBot(BotPlayer bot) : IBotTrade
{
    /// <summary>The bot it plays for: its Walk, Fetch, Make and Send are how a trade gets about between orders.</summary>
    public BotPlayer Bot => bot;

    public (bool Press, bool Held, int Axis)? Play(WorkSession s) =>
        s is ExampleTradeSession game ? (MathF.Abs(game.Marker - ExampleTradeSession.Mark) < ExampleTradeSession.MarkWidth / 4f, false, 0) : null;
}

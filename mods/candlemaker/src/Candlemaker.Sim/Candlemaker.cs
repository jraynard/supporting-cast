using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Bots;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Modding;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace Candlemaker;

/// <summary>
/// The candlemaker's sim half (docs/MODDING.md): a trade only players take, dipping candles with the
/// dipping rod wherever they stand. A first pass: no chandlery, no wax to buy, no AI candlemaker.
/// </summary>
public sealed class CandlemakerMod : ISimMod
{
    public void Register(SimRegistry registry)
    {
        registry.Trade(new CandlemakerModule());
        registry.Bot(bot => new CandlemakerBot(bot));
        // --drill candlemaker: the rod in hand and a candle begun, to try it at once.
        registry.Drill("candlemaker", (sim, me) =>
        {
            var rod = CandlemakerModule.RodOf(sim, me);
            if (rod.IsNull)
                Inventory.Stow(sim, me, rod = sim.CreateItem(sim.Defs.Thing(CandlemakerModule.RodDef), 1));
            if (!sim.World.Has<WorkSession>(me))
                WorkSystem.Start(sim, me, new DipSession { Rod = rod });
            return Entity.Null;
        });
    }
}

/// <summary>The candlemaker's hooks (<see cref="ITradeModule"/>): F with the rod carried begins a candle.</summary>
public sealed class CandlemakerModule : ITradeModule
{
    public const string RodDef = "DippingRod", CandleDef = "Candle", LumpyDef = "LumpyCandle";

    public string? Trade => "Candlemaker";

    public static Entity RodOf(Simulation sim, Entity who) =>
        Inventory.CarriedOf(sim, who, sim.Defs.Thing(RodDef)).FirstOrDefault(Entity.Null);

    /// <summary>F where you stand, the rod carried: dip a candle.</summary>
    public AvatarAction? EarlyActionAt(Simulation sim, Entity avatar, int3 cell, int3 here)
    {
        if (cell != here || sim.World.Has<WorkSession>(avatar) || RodOf(sim, avatar) is not { IsNull: false } rod)
            return null;
        return new AvatarAction(AvatarActionKind.Custom, "Dip a candle", cell, rod);
    }

    public void Perform(Simulation sim, Entity avatar, AvatarAction action)
    {
        if (sim.World.IsAlive(action.Target) && !sim.World.Has<WorkSession>(avatar))
            WorkSystem.Start(sim, avatar, new DipSession { Rod = action.Target });
    }
}

/// <summary>
/// Dipping a candle: each F dips the wick into the wax and adds a layer; the new layer then cools and
/// sets (<see cref="SetSeconds"/>). Dip again before it's set and the soft wax drags into a lump. After
/// <see cref="Layers"/> layers the candle's done: a candle, or a lumpy one if any dip was made too soon.
/// The skill is patience and reading the wax's colour (the view shows warmth only as colour), not speed.
/// <para>A first rough game, to be found by playing it (docs/MODDING.md, "found, not written").</para>
/// </summary>
public sealed class DipSession : WorkSession
{
    /// <summary>Dips to a whole candle.</summary>
    public const int Layers = 8;

    /// <summary>Seconds a fresh layer takes to set.</summary>
    public const float SetSeconds = 1.5f;

    public static int SetSteps => (int)(SetSeconds * SimClock.TicksPerSecond);

    public Entity Rod;

    /// <summary>Layers dipped so far.</summary>
    public int Dipped;

    /// <summary>Dips made before the last layer had set: any at all and the candle comes out lumpy.</summary>
    public int Lumps;

    /// <summary>Steps since the last dip; starts set (a bare wick has nothing to cool).</summary>
    public int SinceDip = SetSteps;

    /// <summary>How warm the outer layer still is, 1 fresh from the wax to 0 set: the view shows it as colour.</summary>
    public float Warmth => Dipped == 0 ? 0f : Math.Clamp(1f - SinceDip / (float)SetSteps, 0f, 1f);

    public bool Set => Warmth <= 0f;

    public bool Lumpy => Lumps > 0;

    public override Skill Skill => Skill.Crafting;
    public override float Xp => 12f;
    public override string Title => "Dipping a candle";

    public override bool StillValid(Simulation sim) => sim.World.IsAlive(Rod) && Inventory.OnPerson(sim, Worker, Rod);

    protected override void Step()
    {
        if (SinceDip < SetSteps)
            SinceDip++;
    }

    protected override void Press()
    {
        if (Done)
            return;
        // A dip on set wax is clean; on soft wax, the softer the worse it drags.
        var warmth = Warmth;
        if (warmth > 0f)
            Lumps++;
        Sample(warmth > 0f ? 0.4f * (1f - warmth) : 1f);
        Dipped++;
        SinceDip = 0;
        if (Dipped >= Layers)
            Done = true;
    }

    /// <summary>The candle, into the pack: lumpy if any dip was made before the wax had set.</summary>
    public override void Finish(Simulation sim)
    {
        var candle = sim.CreateItem(sim.Defs.Thing(Lumpy ? CandlemakerModule.LumpyDef : CandlemakerModule.CandleDef), 1);
        Inventory.Stow(sim, Worker, candle);
        sim.Messages.Tell(Worker, Lumpy ? MessageKind.Info : MessageKind.Good,
            Lumpy ? "A lumpy candle: it'll burn, but it won't sell well." : "A good straight candle.", sim.LocationOf(Worker));
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Rod);
        w.Int(Dipped);
        w.Int(Lumps);
        w.Int(SinceDip);
    }

    protected override void ReadState(SaveReader r)
    {
        Rod = r.Entity();
        Dipped = Math.Clamp(r.Int(), 0, Layers);
        Lumps = Math.Clamp(r.Int(), 0, Layers);
        SinceDip = Math.Clamp(r.Int(), 0, SetSteps);
    }
}

/// <summary>How a bot dips: only as a player would see it, waiting till the wax looks set.</summary>
public sealed class CandlemakerBot(BotPlayer bot) : IBotTrade
{
    public BotPlayer Bot => bot;

    public (bool Press, bool Held, int Axis)? Play(WorkSession s) =>
        s is DipSession dip ? (dip.Set, false, 0) : null;
}

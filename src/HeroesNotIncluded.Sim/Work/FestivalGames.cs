using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Work;

/// <summary>
/// A turn at the butts from the shooting mark (<see cref="FestivalGames.StartTurn"/>): arrows
/// loosed with the bow in hand (lent at the mark, <see cref="Lent"/>, if there wasn't one) and
/// scored by where each lands on the face. Over after <see cref="FestivalGames.Turns"/> arrows, or
/// on walking off the mark (scored on what's been shot).
/// </summary>
public struct ButtsTurn
{
    public Entity Target;
    public Entity Mark;
    public int Shots;
    public int Points;

    /// <summary>The festival's bow, lent at the mark and taken back at the end; Entity.Null if they brought their own.</summary>
    public Entity Lent;

    /// <summary>The weapon they had in hand before the lent bow, put back in hand after.</summary>
    public Entity Before;

    /// <summary>Their weapon was out (combat mode) before the turn: after it, it's left as it was.</summary>
    public bool WasDrawn;
}

/// <summary>
/// The festival's games for a player (<see cref="Round.FestivalSystem"/>): F at the striker starts a
/// hand game, and F at the butts' mark a turn with real arrows (<see cref="ButtsTurn"/>). Either
/// way the score out of 100 is the player's entry. The AI's turns are rolled instead.
/// </summary>
public static class FestivalGames
{
    /// <summary>Turns at a game: three swings, three arrows.</summary>
    public const int Turns = 3;

    /// <summary>The rings' edges (as a share of the face's radius) and what each scores, gold first.</summary>
    public static readonly float[] Rings = [0.12f, 0.3f, 0.5f, 0.75f, 1f];
    public static readonly int[] RingScores = [10, 8, 5, 3, 1];

    /// <summary>How far from its mark a turn may wander before it's given up, in cells.</summary>
    public const float MarkReach = 1.6f;

    /// <summary>What an arrow this far from the middle (in face radii) scores.</summary>
    public static int RingScore(float distance)
    {
        distance = MathF.Abs(distance);
        for (var k = 0; k < Rings.Length; k++)
            if (distance <= Rings[k])
                return RingScores[k];
        return 0;
    }

    /// <summary>A festival game's mark (the butts') on <paramref name="who"/>'s cell or the next, while the games are open; Entity.Null if none.</summary>
    public static Entity MarkBeside(Simulation sim, Entity who)
    {
        var w = sim.World;
        if (!sim.Festival.Open || !w.Has<Position>(who))
            return Entity.Null;
        var at = w.Get<Position>(who).Cell;
        foreach (var g in sim.Festival.Games)
            if (g.Game.MarkDef != null && sim.Festival.PlayedAt(sim, g) is { IsNull: false } mark
                && Maps.Map.Distance(w.Get<Thing>(mark).Cell, at) <= 1)
                return mark;
        return Entity.Null;
    }

    /// <summary>What's called out where an arrow lands in a target, this far across (in face radii).</summary>
    public static string Called(float across) => RingScore(across) switch
    {
        10 => "Gold!",
        0 => "On the straw",
        var score => score.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    /// <summary>What F does at a festival game (or its mark), or null where there's none (or it's closed).</summary>
    public static string? StepAt(Simulation sim, Entity who, Entity thing)
    {
        if (sim.Festival.GameAt(sim, thing) is not { } standing || sim.World.Has<WorkSession>(who) || !sim.Festival.Open)
            return null;
        if (standing.Game.MarkDef is { } mark)
            return sim.World.Has<ButtsTurn>(who) ? null
                : sim.World.Get<Thing>(thing).Def == mark ? standing.Game.Verb
                : $"{standing.Game.Verb} (from the red mark)";
        return standing.Game.Verb;
    }

    public static void Start(Simulation sim, Entity who, Entity thing)
    {
        var w = sim.World;
        if (sim.Festival.GameAt(sim, thing) is not { } standing || !sim.Festival.Open)
            return;
        if (standing.Game.MarkDef != null)
        {
            var mark = sim.Festival.PlayedAt(sim, standing);
            if (thing == mark && AvatarSystem.InReach(sim, w.Get<Position>(who).Cell, mark))
                StartTurn(sim, who, standing, mark);
            else if (!mark.IsNull && thing != mark && w.Has<Avatar>(who) && !w.Has<ButtsTurn>(who))
                AiSystem.Order(sim, who, new AvatarWalkJob(w.Get<Thing>(mark).Cell, true)); // at the butt: off to the mark, and the turn starts there
            return;
        }
        if (!AvatarSystem.InReach(sim, w.Get<Position>(who).Cell, thing))
            return;
        WorkSystem.Start(sim, who, new StrikerSession { Thing = thing, Game = standing.Game.Name });
    }

    /// <summary>
    /// A turn at the butts begins at the mark: the festival's bow lent if there's none in hand, and
    /// drawn out ready (combat mode). Shoot with the bow as ever: aim, draw, loose.
    /// </summary>
    public static void StartTurn(Simulation sim, Entity who, GameStanding standing, Entity mark)
    {
        var w = sim.World;
        var target = sim.Festival.Prop(sim, standing.Game.Def);
        if (w.Has<ButtsTurn>(who) || target.IsNull || !w.Get<Creature>(who).Able)
            return;
        var lent = Entity.Null;
        var before = w.TryGet<Equipment>(who, out var had) ? had.Weapon : Entity.Null;
        var wasDrawn = w.TryGet<Combat.FreeMover>(who, out var mover) && mover.Drawn;
        if (Combat.Archery.Bow(sim, who) is null)
        {
            if (standing.Game.LendsDef is not { } bow)
                return;
            lent = sim.CreateItem(bow, 1, Simulation.MaterialFor(bow, null));
            Inventory.PutOn(sim, who, lent);
            if (Combat.Archery.Bow(sim, who) is null)
            {
                Return(sim, who, lent);
                return;
            }
        }
        w.Add(who, new ButtsTurn { Target = target, Mark = mark, Lent = lent, Before = lent.IsNull ? Entity.Null : before, WasDrawn = wasDrawn });
        // The bow out, ready to draw, and turned to face the butt.
        var from = w.Get<Position>(who).Pos;
        var at = w.Get<Thing>(target).Cell;
        var aim = DetMath.Atan2(at.Y + 0.5f - from.Y, at.X + 0.5f - from.X);
        if (Combat.FreeMoveSystem.Ensure(sim, who))
        {
            ref var free = ref w.Get<Combat.FreeMover>(who);
            free.Drawn = true;
            free.Aim = aim;
        }
        if (w.Has<Avatar>(who))
            w.Get<Avatar>(who).Facing = new int2((int)MathF.Round(DetMath.Cos(aim)), (int)MathF.Round(DetMath.Sin(aim)));
        sim.Messages.Add(MessageKind.Info, lent.IsNull ? "At the mark: three arrows at the butts." : "At the mark, with the festival's bow: three arrows at the butts.",
            w.Get<Position>(who).Cell);
    }

    /// <summary>
    /// An arrow's flown (<see cref="Combat.Blows"/>): into a target, <paramref name="across"/> face
    /// radii from its middle, or (Entity.Null) wide or into someone. Counted on a turn at that target.
    /// </summary>
    public static void Loosed(Simulation sim, Entity who, Entity target, float across)
    {
        var w = sim.World;
        if (!w.TryGet<ButtsTurn>(who, out var turn))
            return;
        if (!target.IsNull)
            sim.Festival.Stick(target, across);
        turn.Shots++;
        if (target == turn.Target)
            turn.Points += RingScore(across);
        w.Get<ButtsTurn>(who) = turn;
        if (turn.Shots >= Turns)
            EndTurn(sim, who);
    }

    /// <summary>
    /// The turn's over (its arrows shot, or the mark left): scored on what's been shot, and the
    /// festival's bow taken back.
    /// </summary>
    public static void EndTurn(Simulation sim, Entity who)
    {
        var w = sim.World;
        if (!w.TryGet<ButtsTurn>(who, out var turn))
            return;
        w.Remove<ButtsTurn>(who);
        Return(sim, who, turn.Lent);
        // Their own weapon back in hand, and put away again if it was away: no accidents after.
        if (!turn.Before.IsNull && w.IsAlive(turn.Before) && w.TryGet<Equipment>(who, out var eq) && eq.Weapon.IsNull)
            Inventory.Equip(sim, who, turn.Before);
        if (w.Has<Combat.FreeMover>(who))
            w.Get<Combat.FreeMover>(who).Drawn = turn.WasDrawn;
        if (turn.Shots > 0 && sim.Festival.GameAt(sim, turn.Mark) is { } standing)
            Score(sim, who, standing.Game.Name, (int)MathF.Round(turn.Points * 100f / (Turns * RingScores[0])));
    }

    /// <summary>The festival's bow back from whoever had it, wherever it's got to.</summary>
    private static void Return(Simulation sim, Entity who, Entity lent)
    {
        var w = sim.World;
        if (lent.IsNull || !w.IsAlive(lent))
            return;
        if (w.TryGet<Equipment>(who, out var eq) && eq.Weapon == lent)
        {
            w.Get<Equipment>(who).Weapon = Entity.Null;
            if (w.Has<Combat.Aiming>(who))
                w.Remove<Combat.Aiming>(who);
        }
        sim.DestroyThing(lent);
    }

    /// <summary>Turns given up: walked off the mark, felled, or the games closed.</summary>
    internal static void Watch(Simulation sim)
    {
        var w = sim.World;
        foreach (var e in w.Query<ButtsTurn>())
        {
            var turn = w.Get<ButtsTurn>(e);
            var stay = sim.Festival.Open && w.IsAlive(turn.Mark) && w.Get<Creature>(e).Able
                && Maps.Map.Distance(w.Get<Position>(e).Cell, w.Get<Thing>(turn.Mark).Cell) <= MarkReach;
            if (!stay)
                EndTurn(sim, e);
        }
    }

    internal static bool Valid(Simulation sim, Entity worker, Entity thing) =>
        sim.Festival.Open && sim.World.IsAlive(thing) && AvatarSystem.InReach(sim, sim.World.Get<Position>(worker).Cell, thing);

    internal static void Score(Simulation sim, Entity worker, string game, int score)
    {
        if (sim.Defs.Festival.Games.Find(g => g.Name == game) is not { } def)
            return;
        sim.Festival.Played(sim, worker, def, score);
        var best = sim.Festival.Games.FirstOrDefault(g => g.Game == def);
        var leading = best != null && best.Best == worker;
        sim.Messages.Add(MessageKind.Info, $"{char.ToUpperInvariant(def.Label[0])}{def.Label[1..]}: {score} of 100{(leading ? ", the best tonight so far!" : $". The best tonight is {best?.BestName}'s {best?.BestScore}.")}",
            sim.World.Get<Position>(worker).Cell);
    }
}

/// <summary>Cheer the toast just said (<see cref="Round.FestivalSystem.Cheer"/>): once a toast, if you like.</summary>
public sealed record CheerCommand(Entity Avatar) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (sim.World.IsAlive(Avatar) && sim.World.Has<Creature>(Avatar) && sim.World.Has<Position>(Avatar))
            sim.Festival.Cheer(sim, Avatar);
    }
}

/// <summary>The work key at a festival game (<see cref="FestivalGames.Start"/>): for bots, which act only through commands.</summary>
public sealed record FestivalGameCommand(Entity Avatar, Entity Thing) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (!Core.InventoryCommand.Ready(sim, Avatar) || !sim.World.IsAlive(Thing) || !sim.World.Has<Thing>(Thing))
            return;
        FestivalGames.Start(sim, Avatar, Thing);
    }
}

/// <summary>
/// The test of strength: a needle swings across the gauge, faster each swing; F brings the mallet
/// down, and the puck flies as high as the needle stood. Three swings, the best counts; the top of
/// the gauge rings the bell. Shaky hands throw the swing off a little.
/// </summary>
public sealed class StrikerSession : WorkSession
{
    /// <summary>Steps the puck's flight is shown for, between swings.</summary>
    public const int FlightSteps = SimClock.TicksPerSecond;

    /// <summary>Struck this hard or better, the bell rings.</summary>
    public const float Bell = 0.9f;

    public Entity Thing { get; set; }
    public string Game { get; set; } = "";

    public int Swings { get; private set; }
    public float Best { get; private set; }

    /// <summary>The last swing's height, 0..1.</summary>
    public float Last { get; private set; }

    /// <summary>Where the needle stands, 0..1.</summary>
    public float Needle { get; private set; }

    /// <summary>Steps left of the last swing's flight; nothing can be struck meanwhile.</summary>
    public int Flight { get; private set; }

    public override Skill Skill => Skill.Melee;
    public override float Xp => 2f;
    public override string Title => "The test of strength";

    public override int3? Focus(Simulation sim) => sim.World.IsAlive(Thing) && sim.World.TryGet<Thing>(Thing, out var t) ? t.Cell : null;

    public override bool StillValid(Simulation sim) => FestivalGames.Valid(sim, Worker, Thing);

    /// <summary>Seconds for the needle to go across and back: quicker each swing.</summary>
    private float Period => 1.3f - 0.25f * Swings;

    protected override void Step()
    {
        if (Flight > 0)
        {
            if (--Flight == 0 && Swings >= FestivalGames.Turns)
                Done = true;
            return;
        }
        Needle = Triangle(StepsDone, Period);
    }

    protected override void Press()
    {
        if (Flight > 0 || Swings >= FestivalGames.Turns)
            return;
        Last = Math.Clamp(Needle + Rand.Range(-1f, 1f) * Hands.Tremor * 0.3f, 0f, 1f);
        Best = Math.Max(Best, Last);
        Swings++;
        Sample(Last);
        Flight = FlightSteps;
    }

    public override void Finish(Simulation sim) => FestivalGames.Score(sim, Worker, Game, (int)MathF.Round(Best * 100f));

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Thing);
        w.String(Game);
        w.Int(Swings);
        w.Float(Best);
        w.Float(Last);
        w.Float(Needle);
        w.Int(Flight);
    }

    protected override void ReadState(SaveReader r)
    {
        Thing = r.Entity();
        Game = r.String();
        Swings = r.Int();
        Best = r.Float();
        Last = r.Float();
        Needle = r.Float();
        Flight = r.Int();
    }
}

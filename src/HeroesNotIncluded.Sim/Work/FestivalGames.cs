using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Work;

/// <summary>
/// The festival's games for a player (<see cref="Round.FestivalSystem"/>): F at the striker or the
/// butts starts a turn, and its score is the player's entry. The AI's turns are rolled instead.
/// </summary>
public static class FestivalGames
{
    /// <summary>Turns at a game: three swings, three arrows.</summary>
    public const int Turns = 3;

    /// <summary>What F does at a festival game, or null where there's none (or it's closed).</summary>
    public static string? StepAt(Simulation sim, Entity who, Entity thing)
    {
        if (sim.Festival.GameAt(sim, thing) is not { } standing || sim.World.Has<WorkSession>(who))
            return null;
        return sim.Festival.Open ? standing.Game.Verb : null;
    }

    public static void Start(Simulation sim, Entity who, Entity thing)
    {
        var w = sim.World;
        if (sim.Festival.GameAt(sim, thing) is not { } standing || !sim.Festival.Open
            || !AvatarSystem.InReach(sim, w.Get<Position>(who).Cell, thing))
            return;
        WorkSession session = standing.Game.Name switch
        {
            "Butts" => new ButtsSession { Thing = thing, Game = standing.Game.Name },
            _ => new StrikerSession { Thing = thing, Game = standing.Game.Name },
        };
        WorkSystem.Start(sim, who, session);
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

/// <summary>
/// The archery butts: the aim sways (the breath, the wind, the hands), and the walk keys hold it
/// on the gold against the sway; F looses. Three arrows, scored by the rings they land in. A
/// practised archer's sway is smaller.
/// </summary>
public sealed class ButtsSession : WorkSession
{
    /// <summary>Steps an arrow's shown landing between shots.</summary>
    public const int LandSteps = SimClock.TicksPerSecond * 3 / 4;

    /// <summary>The rings' edges (as a share of the face's radius) and what each scores, gold first.</summary>
    public static readonly float[] Rings = [0.12f, 0.3f, 0.5f, 0.75f, 1f];
    public static readonly int[] RingScores = [10, 8, 5, 3, 1];

    /// <summary>How fast the walk keys move the aim (face radii a step).</summary>
    public const float SteerSpeed = 0.012f;

    public Entity Thing { get; set; }
    public string Game { get; set; } = "";

    /// <summary>Where the aim is on the face (-1..1 each way; 0 is the gold's middle).</summary>
    public float AimX { get; private set; }
    public float AimY { get; private set; }

    private float _steerX, _steerY;
    private int _dx, _dy;
    private float _windX, _windY;
    private float[] _hitX = new float[FestivalGames.Turns], _hitY = new float[FestivalGames.Turns];

    public int Shots { get; private set; }
    public int Points { get; private set; }
    public int Landing { get; private set; }

    public float HitX(int k) => _hitX[k];
    public float HitY(int k) => _hitY[k];

    /// <summary>What an arrow at this distance from the middle scores.</summary>
    public static int RingScore(float distance)
    {
        for (var k = 0; k < Rings.Length; k++)
            if (distance <= Rings[k])
                return RingScores[k];
        return 0;
    }

    public override Skill Skill => Skill.Shooting;
    public override float Xp => 2f;
    public override string Title => "The archery butts";

    public override int3? Focus(Simulation sim) => sim.World.IsAlive(Thing) && sim.World.TryGet<Thing>(Thing, out var t) ? t.Cell : null;

    public override bool StillValid(Simulation sim) => FestivalGames.Valid(sim, Worker, Thing);

    protected internal override void Begin() => Gust();

    protected override void Cloned()
    {
        _hitX = (float[])_hitX.Clone();
        _hitY = (float[])_hitY.Clone();
    }

    /// <summary>The wind for the next arrow: a steady push the aim has to be held against.</summary>
    private void Gust()
    {
        _windX = Rand.Range(-1f, 1f) * 0.004f;
        _windY = Rand.Range(-1f, 1f) * 0.002f;
    }

    /// <summary>How far the breath and the hands sway the aim: less for a practised archer.</summary>
    private float Sway => (0.18f + Hands.Tremor * 0.4f) / Hands.Tolerance;

    protected override void Axis(int value) => _dx = value;
    protected override void Axis2(int value) => _dy = value;

    protected override void Step()
    {
        if (Landing > 0)
        {
            if (--Landing == 0 && Shots >= FestivalGames.Turns)
                Done = true;
            return;
        }
        _steerX = Math.Clamp(_steerX + _dx * SteerSpeed + _windX, -1.5f, 1.5f);
        _steerY = Math.Clamp(_steerY + _dy * SteerSpeed + _windY, -1.5f, 1.5f);
        var sway = Sway;
        AimX = _steerX + (Triangle(StepsDone, 2.3f) - 0.5f) * sway;
        AimY = _steerY + (Triangle(StepsDone, 1.7f) - 0.5f) * sway;
    }

    protected override void Press()
    {
        if (Landing > 0 || Shots >= FestivalGames.Turns)
            return;
        _hitX[Shots] = AimX;
        _hitY[Shots] = AimY;
        var score = RingScore(MathF.Sqrt(AimX * AimX + AimY * AimY));
        Points += score;
        Sample(score / 10f);
        Shots++;
        Landing = LandSteps;
        Gust();
    }

    public override void Finish(Simulation sim) =>
        FestivalGames.Score(sim, Worker, Game, (int)MathF.Round(Points * 100f / (FestivalGames.Turns * RingScores[0])));

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Thing);
        w.String(Game);
        w.Float(AimX); w.Float(AimY);
        w.Float(_steerX); w.Float(_steerY);
        w.Int(_dx); w.Int(_dy);
        w.Float(_windX); w.Float(_windY);
        for (var k = 0; k < FestivalGames.Turns; k++)
        {
            w.Float(_hitX[k]);
            w.Float(_hitY[k]);
        }
        w.Int(Shots);
        w.Int(Points);
        w.Int(Landing);
    }

    protected override void ReadState(SaveReader r)
    {
        Thing = r.Entity();
        Game = r.String();
        AimX = r.Float(); AimY = r.Float();
        _steerX = r.Float(); _steerY = r.Float();
        _dx = r.Int(); _dy = r.Int();
        _windX = r.Float(); _windY = r.Float();
        for (var k = 0; k < FestivalGames.Turns; k++)
        {
            _hitX[k] = r.Float();
            _hitY[k] = r.Float();
        }
        Shots = r.Int();
        Points = r.Int();
        Landing = r.Int();
    }
}

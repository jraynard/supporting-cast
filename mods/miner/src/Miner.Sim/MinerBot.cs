using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Bots;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Miner;

/// <summary>
/// How a bot mines (<see cref="IBotTrade"/>): only through the sessions it sees, the keys a player has, and what anyone
/// could see of the mine (faces as they look, the chalk, ore lying about, barrows).
/// <list type="bullet">
/// <item>Hewing, it walks the point over to the crack, raises the pick past the mark, and lets go.</item>
/// <item>Sounding, it taps every spot four times, chalks the one that sounds clearly deadest as a seam, and puts the
/// hammer down; it hears the taps as a player does (<see cref="SoundSession.HeardAt"/>), never what's behind them.</item>
/// <item>Loading, it sets each piece where it keeps the load steadiest (<see cref="Barrows.Risk"/>).</item>
/// <item>Between orders (<see cref="Between"/>): with ore lying hewn, it fetches a barrow, loads it, wheels it to the
/// smithy leaning it back by its lean, and tips it in; else it hews what shows as ore or is chalked a seam, or sounds a
/// wall it hasn't, or cuts further in where it found nothing.</item>
/// </list>
/// </summary>
public sealed class MinerBot(BotPlayer bot) : IBotTrade
{
    /// <summary>Taps a bot gives each spot; and how much deader than the rest a spot must sound to be chalked a seam.</summary>
    public const int TapsEach = 4;
    public const float SeamMargin = 0.3f, SeamAtLeast = 0.45f;

    public BotPlayer Bot => bot;

    private int _lastAxis;
    private bool _stopped;
    private SoundSession? _sounding;

    public (bool Press, bool Held, int Axis)? Play(WorkSession s)
    {
        if (s is SoundSession sound)
            return Sound(sound);
        if (s is LoadSession load)
            return Load(load);
        if (s is not HewSession hew)
            return null;
        var off = hew.Crack - hew.Aim;
        var onCrack = MathF.Abs(off) <= hew.CrackWidthNow * 0.5f;
        // Line up first; then hold to raise, and let go once it's well past the mark.
        if (!onCrack && hew.Raised <= 0f)
            return (false, false, Math.Sign(off));
        return (false, hew.Raised < HewSession.NeedRaise + 0.15f, 0);
    }

    private (bool, bool, int) Sound(SoundSession s)
    {
        if (!ReferenceEquals(s, _sounding) && (_sounding == null || _sounding.Face != s.Face || s.TapCount < _sounding.TapCount))
        {
            _stopped = false;
            // A new sounding: all its spots count as sounded, and it's one more that's found nothing, till it finds a seam.
            for (var k = 0; k < SoundSession.Spots; k++)
                _sounded.Add(s.CellOf(k));
            _dryRuns++;
        }
        _sounding = s;
        bot.Act = false;
        if (_stopped || s.Cooldown > 0)
            return Idle();
        // Every spot of rock tapped a few times first.
        for (var k = 0; k < SoundSession.Spots; k++)
            if (s.Rock[k] && s.Taps[k] < TapsEach)
                return Go(s, k) ?? (true, false, 0);
        // Then the seam, if one spot stands out from the rest.
        if (Seam(s) is { } seam && s.Marks[seam] != ChalkMark.Seam)
        {
            _dryRuns = 0;
            if (Go(s, seam) is { } step)
                return step;
            bot.Act = true;
            return Idle();
        }
        _stopped = true;
        bot.Send(new StopWorkCommand(s.Worker));
        return Idle();
    }

    /// <summary>The spot that's sounded clearly deadest (by the bot's own ear), or null if none stands out.</summary>
    public static int? Seam(SoundSession s)
    {
        var heard = Enumerable.Range(0, SoundSession.Spots).Where(k => s.HeardAt(k) != null).Select(k => (k, Dead: s.HeardAt(k)!.Value)).ToList();
        if (heard.Count == 0)
            return null;
        var best = heard.MaxBy(h => h.Dead);
        var rest = heard.Where(h => h.k != best.k).Select(h => h.Dead).DefaultIfEmpty(0f).Average();
        return best.Dead >= SeamAtLeast && best.Dead - rest >= SeamMargin ? best.k : null;
    }

    /// <summary>A step of the hammer toward <paramref name="spot"/> (letting go between, as the keys go a spot a press), or null once it's there.</summary>
    private (bool, bool, int)? Go(SoundSession s, int spot)
    {
        if (s.Spot == spot)
            return null;
        if (_lastAxis != 0)
            return Idle();
        _lastAxis = Math.Sign(spot - s.Spot);
        return (false, false, _lastAxis);
    }

    private (bool, bool, int) Idle()
    {
        _lastAxis = 0;
        return (false, false, 0);
    }

    /// <summary>The slot that leaves the load steadiest with the next piece in it (the first such, by slot), or null if none fits.</summary>
    public static int? BestSlot(LoadSession s)
    {
        if (s.ToLoad.Count == 0)
            return null;
        int? best = null;
        var bestRisk = float.MaxValue;
        var trial = new List<BarrowPiece>(s.Placed) { default };
        for (var k = 0; k < Barrows.Slots; k++)
        {
            if (!s.Fits(k))
                continue;
            trial[^1] = new BarrowPiece(s.ToLoad[0], (byte)k);
            var risk = Barrows.Risk(trial) + 0.001f * k;
            if (risk < bestRisk)
                (best, bestRisk) = (k, risk);
        }
        return best;
    }

    /// <summary>Over to the steadiest slot (A/D across, W/S along, a press at a time), then set the piece there.</summary>
    private (bool, bool, int) Load(LoadSession s)
    {
        bot.Axis2 = 0;
        if (BestSlot(s) is not { } slot)
            return Idle();
        if (_lastAxis != 0 || _lastAxis2 != 0)
        {
            _lastAxis2 = 0;
            return Idle();
        }
        var across = Barrows.ColOf(slot) - Barrows.ColOf(s.Slot);
        var along = Barrows.RowOf(slot) - Barrows.RowOf(s.Slot);
        if (across != 0)
            return (false, false, _lastAxis = Math.Sign(across));
        if (along != 0)
        {
            bot.Axis2 = _lastAxis2 = Math.Sign(along);
            return (false, false, 0);
        }
        return (true, false, 0);
    }

    private int _lastAxis2;

    // ---------------- Between orders ----------------

    /// <summary>Ore lying hewn about the mine before it fetches a barrow; and how full a barrow it'll take to the smithy.</summary>
    public const int FetchAt = 6;
    public const float HaulAt = 15f;

    /// <summary>Soundings in a row that found nothing before it cuts further in instead.</summary>
    public const int DryRuns = 3;

    /// <summary>Wall it's sounded (every spot of each sounding), soundings since it last found a seam, and when it last sent a walk.</summary>
    private readonly HashSet<int3> _sounded = [];
    private int _dryRuns;
    private int3? _cutting;
    private long _walked = -1_000_000;
    private long _pressed = -1_000_000;

    /// <summary>Hewn ore it couldn't set a barrow down beside (stone all round), let be till the tick given.</summary>
    private readonly Dictionary<Entity, long> _crowded = [];

    /// <summary>How long ore with no room for a barrow beside it is let be.</summary>
    public const float CrowdedHours = 2f;

    public bool Between(Simulation sim, Entity me, PerceptionView view, long now)
    {
        var w = sim.World;
        if (!sim.Round.Live || !MinerModule.IsMiner(sim, me) || w.Has<WorkSession>(me))
            return false;
        var shed = HeroesNotIncluded.Sim.Shops.ShopSystem.WorkplaceOf(sim, me);
        if (shed.IsNull)
            return false;
        var mine = w.Get<PresetBuilding>(shed).Anchor;
        var here = w.Get<Position>(me).Cell;
        foreach (var gone in _crowded.Where(kv => kv.Value <= now || !w.IsAlive(kv.Key)).Select(kv => kv.Key).ToList())
            _crowded.Remove(gone);
        var lying = Lying(sim, mine).Where(e => !_crowded.ContainsKey(e)).ToList();
        var held = Barrows.Held(sim, me);
        if (!held.IsNull)
            return Pushing(sim, me, here, mine, held, lying, now);
        // A loaded barrow set down (it's just been loaded, or went over): back for it.
        if (Nearest(sim, here, w.Query<Thing>().Where(e => Barrows.IsBarrow(sim, e) && w.Get<Thing>(e).Spawned && Barrows.LoadOf(sim, e).Pieces.Count > 0
                && HeroesNotIncluded.Sim.Maps.Map.Distance(w.Get<Thing>(e).Cell, mine) <= OreRuns.Reach * 2)) is { IsNull: false } loaded)
        {
            bot.Doing = "taking up the barrow";
            return PressAt(sim, me, here, w.Get<Thing>(loaded).Cell, now);
        }
        // Enough lying hewn: a barrow for it.
        if (lying.Sum(e => w.Get<Stack>(e).Count) >= FetchAt && Nearest(sim, here, w.Query<Thing>().Where(e => Barrows.IsBarrow(sim, e) && w.Get<Thing>(e).Spawned)) is { IsNull: false } barrow)
        {
            bot.Doing = "fetching a barrow";
            return PressAt(sim, me, here, w.Get<Thing>(barrow).Cell, now);
        }
        return AtTheFace(sim, me, here, mine, now);
    }

    /// <summary>With a barrow in hand: loaded enough (or nothing more to load), to the smithy and tipped in; else to the ore lying hewn.</summary>
    private bool Pushing(Simulation sim, Entity me, int3 here, int3 mine, Entity barrow, List<Entity> lying, long now)
    {
        var w = sim.World;
        var load = Barrows.LoadOf(sim, barrow);
        // Leaned back by its lean, as a player watching the gauge would.
        var nudge = load.Lean < -0.2f ? 1 : load.Lean > 0.2f ? -1 : 0;
        if (nudge != load.Nudge)
            bot.Send(new BarrowLeanCommand(me, nudge));
        var room = Barrows.MaxMass - load.Mass;
        var more = lying.FirstOrDefault(e => w.Get<Thing>(e).Def.Mass <= room + 1e-4f, Entity.Null);
        if (load.Mass < HaulAt && !more.IsNull)
        {
            bot.Doing = "loading the barrow";
            var at = w.Get<Thing>(more).Cell;
            // At it, and hewn stone all round: no room to set the barrow down. Let that piece be a while.
            if (AvatarSystem.InReach(here, at) && Barrows.SpotBy(sim, me) == null)
            {
                _crowded[more] = now + SimClock.HoursToTicks(CrowdedHours);
                return true;
            }
            return PressAt(sim, me, here, at, now);
        }
        if (load.Pieces.Count == 0)
        {
            // Nothing in it and nothing to load: set it down where there's room, and back to the face.
            if (Barrows.SpotBy(sim, me) is { } spot)
                bot.Send(new AvatarDropCommand(me, spot));
            else
                bot.Walk(sim, me, mine, now);
            return true;
        }
        var chest = OreRuns.SmithyChest(sim);
        if (chest.IsNull)
            return false;
        bot.Doing = "wheeling ore to the smithy";
        return PressAt(sim, me, here, w.Get<Thing>(chest).Cell, now);
    }

    /// <summary>
    /// At the face: hew what shows as ore or is chalked a seam (nearest first); else sound a wall it hasn't (the sounding
    /// hammer in hand); else cut further in at a wall it sounded and found nothing behind, nearest the mine's end.
    /// </summary>
    private bool AtTheFace(Simulation sim, Entity me, int3 here, int3 mine, long now)
    {
        var faces = Faces(sim, me, mine);
        if (faces.Count == 0)
            return false;
        var w = sim.World;
        var hammerInHand = SoundSession.HammerInHand(sim, me);
        (int3 Face, int3 Stand)? Pick(Func<int3, bool> want) =>
            faces.Where(f => want(f.Face)).OrderBy(f => HeroesNotIncluded.Sim.Maps.Map.Distance(f.Stand, here)).Cast<(int3, int3)?>().FirstOrDefault();
        var hew = Pick(f => Mining.ShownBlock(sim, sim.Map.Idx(f)).Ore || ChalkMarks.At(sim, f) == ChalkMark.Seam);
        if (hew == null && _dryRuns < DryRuns && Pick(f => !_sounded.Contains(f) && ChalkMarks.At(sim, f) == ChalkMark.None) is { } unsounded)
        {
            bot.Doing = "sounding the rock";
            if (!hammerInHand)
                return Hold(sim, me, SoundSession.HammerDef);
            return FaceAndPress(sim, me, here, unsounded, now, null);
        }
        // Nothing found for a while: cut in where the mine's deepest, toward rock nobody's sounded.
        hew ??= faces.Where(f => _sounded.Contains(f.Face) || ChalkMarks.At(sim, f.Face) == ChalkMark.Bare)
            .OrderByDescending(f => HeroesNotIncluded.Sim.Maps.Map.Distance(f.Face, mine)).ThenBy(f => f.Face.X).ThenBy(f => f.Face.Y)
            .Cast<(int3, int3)?>().FirstOrDefault();
        if (hew is not { } at)
            return false;
        if (!Mining.ShownBlock(sim, sim.Map.Idx(at.Face)).Ore && ChalkMarks.At(sim, at.Face) != ChalkMark.Seam)
            _cutting = at.Face;
        bot.Doing = "hewing";
        if (_cutting is { } cut && !sim.Map.IsSolid(sim.Map.Idx(cut)))
            (_cutting, _dryRuns) = (null, 0); // cut through: new wall to sound
        if (hammerInHand)
        {
            bot.Send(new StowCommand(me));
            return true;
        }
        return FaceAndPress(sim, me, here, at, now, null);
    }

    /// <summary>Faces of the mine a pick can hew, each with the open cell square in front of it to stand at (as it looks: anyone could see them).</summary>
    private static List<(int3 Face, int3 Stand)> Faces(Simulation sim, Entity me, int3 mine)
    {
        var map = sim.Map;
        var found = new List<(int3, int3)>();
        int3[] square = [new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0)];
        foreach (var stand in sim.SpiralFrom(mine, OreRuns.Reach))
        {
            if (!map.InBounds(stand) || !map.Passable(stand) || !map.CoveredAbove(map.Idx(stand)) || !Buildings.At(sim, stand).IsNull)
                continue;
            foreach (var d in square)
                if (HewSession.Problem(sim, me, stand + d, stand) == null)
                    found.Add((stand + d, stand));
        }
        return found;
    }

    /// <summary>Stand square to the face, then F at it (<paramref name="started"/> told once it's pressed).</summary>
    private bool FaceAndPress(Simulation sim, Entity me, int3 here, (int3 Face, int3 Stand) at, long now, Action? started)
    {
        if (here != at.Stand)
        {
            if (now - _walked >= BotPlayer.ThinkTicks * 2)
            {
                bot.Send(new AvatarGoToCommand(me, at.Stand));
                _walked = now;
            }
            return true;
        }
        if (now - _pressed < BotPlayer.ThinkTicks)
            return true;
        bot.Send(new AvatarWorkCommand(me, true, at.Face));
        bot.Send(new AvatarWorkCommand(me, false));
        _pressed = now;
        started?.Invoke();
        return true;
    }

    /// <summary>Walk up to a cell and press F at it once in reach.</summary>
    private bool PressAt(Simulation sim, Entity me, int3 here, int3 cell, long now)
    {
        if (!AvatarSystem.InReach(here, cell))
        {
            bot.Walk(sim, me, cell, now);
            return true;
        }
        if (now - _pressed < BotPlayer.ThinkTicks)
            return true;
        bot.Send(new AvatarWorkCommand(me, true, cell));
        bot.Send(new AvatarWorkCommand(me, false));
        _pressed = now;
        return true;
    }

    /// <summary>Take a thing from the pack into the hands.</summary>
    private bool Hold(Simulation sim, Entity me, string def)
    {
        if (sim.PackOf(me) is not { IsNull: false } pack
            || sim.StoredIn(pack).FirstOrDefault(e => sim.World.Get<Thing>(e).Def.Name == def, Entity.Null) is not { IsNull: false } item)
            return false;
        bot.Send(new HoldCommand(me, item));
        return true;
    }

    /// <summary>Ore lying hewn about the mine (loadable, on the ground, in no one's hands), nearest the mine first.</summary>
    private static List<Entity> Lying(Simulation sim, int3 mine)
    {
        var w = sim.World;
        return w.Query<Thing>()
            .Where(e => w.Get<Thing>(e) is { Spawned: true } t && Barrows.Loadable(sim, t.Def) && !w.Has<Stored>(e)
                && HeroesNotIncluded.Sim.Maps.Map.Distance(t.Cell, mine) <= OreRuns.Reach)
            .OrderBy(e => HeroesNotIncluded.Sim.Maps.Map.Distance(w.Get<Thing>(e).Cell, mine)).ThenBy(e => e.Index).ToList();
    }

    private static Entity Nearest(Simulation sim, int3 here, IEnumerable<Entity> things) =>
        things.OrderBy(e => HeroesNotIncluded.Sim.Maps.Map.Distance(sim.World.Get<Thing>(e).Cell, here)).ThenBy(e => e.Index).DefaultIfEmpty(Entity.Null).First();
}

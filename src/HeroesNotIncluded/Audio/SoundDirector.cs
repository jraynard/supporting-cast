using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Environment;
using HeroesNotIncluded.Sim.Ecs;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Round;
using HeroesNotIncluded.Sim.Shops;
using HeroesNotIncluded.Sim.Speech;
using Narsil.Audio;

namespace HeroesNotIncluded.Audio;

/// <summary>
/// The village's sound, from what the local player can perceive: fights and dragon fire seen or
/// heard (through a wall they come muffled, out of earshot not at all), lines their character
/// heard as babble, the world around the listener (wind, birds or crickets, the river, hearths, a
/// full room), bells for danger, coin, and the music's mood. Like the renderer it reads the sim
/// and never changes it; other characters only through <see cref="PerceptionView"/>.
/// </summary>
public sealed class SoundDirector(AudioEngine audio)
{
    /// <summary>World units per level up, so a fight a floor below isn't heard as if beside you.</summary>
    public const float LevelHeight = 2.5f;

    /// <summary>Real seconds a sign of danger keeps the danger music on.</summary>
    public const float DangerHoldSeconds = 40f;

    /// <summary>How often the slower checks run (fires, the river, who's in the room).</summary>
    private const float SurveySeconds = 0.25f;

    private Simulation? _sim;
    private long _shotsSeen, _blowsSeen, _breathsSeen, _messagesSeen, _lineSeen;
    private int _coins = -1;
    private Entity _coinsOf = Entity.Null;
    private float _time, _survey, _dangerUntil = -1000f, _nextRoar;
    private readonly List<float> _recentFight = [];
    private readonly List<(string Key, float3 At, float Gain, float Muffle)> _loops = [];
    private float3[] _water = [];
    private float _birds, _night, _wind, _crowd;
    private bool _indoors;

    /// <summary>Babble lines older than this (a load, a join) are history and stay quiet.</summary>
    private const int FreshTicks = SimClock.TicksPerSecond * 3;

    public static float3 ToAudio(float3 cellPos) => new(cellPos.X, cellPos.Y, cellPos.Z * LevelHeight);
    public static float3 ToAudio(int3 cell) => new(cell.X + 0.5f, cell.Y + 0.5f, cell.Z * LevelHeight);

    /// <summary>The mood chosen last update (tests, the debug overlay).</summary>
    public string? Mood { get; private set; }

    /// <param name="listenAt">Where the camera looks (cell space).</param>
    /// <param name="screenRight">The screen's right in cell space, so panning matches the view.</param>
    /// <param name="height">How far above the ground the camera hears from (zoomed out hears less).</param>
    public void Update(Simulation sim, PerceptionView view, Entity avatar, float3 listenAt, float3 screenRight, float height, float dt)
    {
        if (!ReferenceEquals(sim, _sim))
            Reset(sim);
        _time += dt;

        var up = new float3(0, 0, 1);
        var right = Normalize(new float3(screenRight.X, screenRight.Y, 0), new float3(1, 0, 0));
        var forward = Cross(up, right);
        var ear = ToAudio(listenAt) + up * height;
        audio.SetListener(ear, forward, up);

        var me = !avatar.IsNull && sim.World.IsAlive(avatar) && sim.World.TryGet<Position>(avatar, out var p) ? p.Cell : (int3?)null;

        Shots(sim, view, me);
        BlowsMet(sim, view, me);
        Breaths(sim, view, me);
        Dragons();
        Messages(sim);
        Speech(sim, view);
        Signs(sim, view);

        _survey -= dt;
        if (_survey <= 0)
        {
            _survey = SurveySeconds;
            Survey(sim, view, me, listenAt);
            Coins(sim, avatar);
        }
        Ambience();
        PickMood(sim);
    }

    private void Reset(Simulation sim)
    {
        _sim = sim;
        // What happened before we were listening is history.
        _shotsSeen = _blowsSeen = _breathsSeen = _signsSeen = sim.Clock.Tick;
        _messagesSeen = sim.Messages.Total;
        _lineSeen = sim.Speech.Kept.Count > 0 ? sim.Speech.Kept[^1].Id : 0;
        _coins = -1;
        _water = WaterCells(sim);
        _recentFight.Clear();
        _dangerUntil = -1000f;
    }

    /// <summary>
    /// How a sound at <paramref name="cell"/> reaches the player: 0 seen (clear), muffled through
    /// walls if within a shout's reach, null if they can't know of it at all.
    /// </summary>
    private static float? Reach(Simulation sim, PerceptionView view, int3? me, int3 cell)
    {
        if (view.CanSeeCell(cell))
            return 0f;
        if (me is not { } at)
            return null;
        return SpeechSystem.Hear(sim, cell, at, Volume.Shout) switch
        {
            Clarity.Clear => 0.2f,
            Clarity.Muffled => 0.8f,
            _ => null,
        };
    }

    private static int3 Cell(float3 at) => new((int)MathF.Floor(at.X), (int)MathF.Floor(at.Y), (int)MathF.Floor(at.Z));

    private void Shots(Simulation sim, PerceptionView view, int3? me)
    {
        var newest = _shotsSeen;
        foreach (var shot in sim.RecentShots)
        {
            if (shot.Tick <= _shotsSeen)
                continue;
            newest = Math.Max(newest, shot.Tick);
            if ((Reach(sim, view, me, Cell(shot.To)) ?? Reach(sim, view, me, Cell(shot.From))) is not { } muffle)
                continue;
            var to = ToAudio(shot.To);
            _recentFight.Add(_time);
            _dangerUntil = Math.Max(_dangerUntil, _time + DangerHoldSeconds * 0.5f);
            if (shot.Ranged)
            {
                audio.Play(Sounds.Bow, ToAudio(shot.From), muffle: muffle);
                audio.Play(shot.Hit ? Sounds.Wound : Sounds.Miss, to, muffle: muffle, delay: 0.18f);
            }
            else
            {
                audio.Play(Sounds.Swing, to, gain: 0.7f, muffle: muffle);
                if (shot.Hit)
                {
                    audio.Play(Sounds.Clash, to, muffle: muffle, delay: 0.06f);
                    audio.Play(Sounds.Wound, to, gain: 0.6f, muffle: muffle, delay: 0.09f);
                }
                else
                    audio.Play(Sounds.Miss, to, gain: 0.8f, muffle: muffle, delay: 0.05f);
            }
        }
        _shotsSeen = newest;
        _recentFight.RemoveAll(t => _time - t > 4f);
    }

    /// <summary>
    /// How each blow went, on top of its swing and wound (<see cref="Shots"/>): a parry rings steel
    /// on steel, a block knocks dull, a broken guard knocks and clashes, a dodge whistles past; a
    /// heavy blow carries its own slower air, a spear's jab its own. Heard as the fight is, from where it is.
    /// </summary>
    private void BlowsMet(Simulation sim, PerceptionView view, int3? me)
    {
        var w = sim.World;
        var newest = _blowsSeen;
        foreach (var b in sim.RecentBlows)
        {
            if (b.Tick <= _blowsSeen)
                continue;
            newest = Math.Max(newest, b.Tick);
            var where = w.IsAlive(b.Target) && w.Has<Position>(b.Target) ? b.Target : b.Attacker;
            if (!w.IsAlive(where) || !w.Has<Position>(where))
                continue;
            var at = w.Get<Position>(where).Pos;
            if (Reach(sim, view, me, Cell(at)) is not { } muffle)
                continue;
            _recentFight.Add(_time);
            var pos = ToAudio(at);
            var held = w.IsAlive(b.Attacker) && w.TryGet<Equipment>(b.Attacker, out var eq) && w.IsAlive(eq.Weapon) ? w.Get<Thing>(eq.Weapon).Def.Held : null;
            if (held == "spear")
                audio.Play(Sounds.Thrust, pos, muffle: muffle);
            else if (b.Heavy)
                audio.Play(Sounds.Heavy, pos, muffle: muffle);
            switch (b.Outcome)
            {
                case Sim.Combat.BlowOutcome.Parried:
                    audio.Play(Sounds.Parry, pos, muffle: muffle, delay: 0.04f);
                    break;
                case Sim.Combat.BlowOutcome.Blocked:
                    audio.Play(Sounds.Block, pos, muffle: muffle, delay: 0.04f);
                    break;
                case Sim.Combat.BlowOutcome.GuardBroken:
                    audio.Play(Sounds.Block, pos, muffle: muffle, delay: 0.03f);
                    audio.Play(Sounds.Clash, pos, gain: 0.8f, muffle: muffle, delay: 0.07f);
                    break;
                case Sim.Combat.BlowOutcome.Dodged:
                    audio.Play(Sounds.Miss, pos, gain: 0.9f, muffle: muffle);
                    break;
            }
        }
        _blowsSeen = newest;
    }

    private void Breaths(Simulation sim, PerceptionView view, int3? me)
    {
        var newest = _breathsSeen;
        foreach (var b in sim.RecentBreaths)
        {
            if (b.Tick <= _breathsSeen)
                continue;
            newest = Math.Max(newest, b.Tick);
            // Dragon fire carries: seen, or heard anywhere in town (muffled indoors).
            var muffle = Reach(sim, view, me, Cell(b.Mouth)) ?? (me != null ? 0.6f : (float?)null);
            if (muffle is not { } m)
                continue;
            var at = ToAudio(b.Mouth);
            audio.Play(Sounds.Roar, at, muffle: m);
            audio.Play(Sounds.Breath, at, muffle: m, delay: 0.25f);
            _dangerUntil = _time + DangerHoldSeconds;
        }
        _breathsSeen = newest;
    }

    private float3? _dragonAt;

    private void Dragons()
    {
        if (_dragonAt is not { } at)
            return;
        audio.Hold("dragon:wings", Sounds.Wings, 1f, ToAudio(at));
        if (_time >= _nextRoar)
        {
            _nextRoar = _time + 9f + Random.Shared.NextSingle() * 14f;
            audio.Play(Sounds.Roar, ToAudio(at), gain: 0.8f, pitch: 0.9f + Random.Shared.NextSingle() * 0.2f);
        }
        _dangerUntil = _time + DangerHoldSeconds;
    }

    private void Messages(Simulation sim)
    {
        var log = sim.Messages;
        if (log.Total == _messagesSeen)
            return;
        var from = log.IndexAfter(_messagesSeen);
        var danger = false;
        var good = false;
        for (var i = from; i < log.Messages.Count; i++)
        {
            danger |= log.Messages[i].Kind == MessageKind.Danger;
            good |= log.Messages[i].Kind == MessageKind.Good;
        }
        _messagesSeen = log.Total;
        if (danger)
        {
            audio.Play(Sounds.UiBell);
            _dangerUntil = _time + DangerHoldSeconds;
        }
        else if (good)
            audio.Play(Sounds.UiGood);
    }

    /// <summary>Lines babbling at once, at most: past that a room is a murmur (amb.crowd), not a chorus.</summary>
    public const int MaxTalking = 3;

    private readonly List<(float Until, float Distance)> _talking = [];


    private long _signsSeen;

    /// <summary>The dead's signs as you perceive them (Sim.Afterlife): a knock where it was, a cold gust for a chill.</summary>
    private void Signs(Simulation sim, PerceptionView view)
    {
        var newest = _signsSeen;
        foreach (var sign in view.Signs())
        {
            if (sign.Tick <= _signsSeen)
                continue;
            newest = Math.Max(newest, sign.Tick);
            var at = ToAudio(new float3(sign.Cell.X + 0.5f, sign.Cell.Y + 0.5f, sign.Cell.Z + 0.5f));
            switch (sign.Kind)
            {
                case HeroesNotIncluded.Sim.Afterlife.HauntKind.Knock:
                    audio.Play(Sounds.Knock, at, gain: 0.9f, pitch: 0.75f);
                    audio.Play(Sounds.Knock, at, gain: 0.7f, pitch: 0.7f, delay: 0.35f);
                    break;
                case HeroesNotIncluded.Sim.Afterlife.HauntKind.Chill:
                    audio.Play(Sounds.Wind, at, gain: 0.35f, pitch: 1.4f);
                    break;
            }
        }
        _signsSeen = newest;
    }

    private void Speech(Simulation sim, PerceptionView view)
    {
        _talking.RemoveAll(t => t.Until <= _time);
        var kept = sim.Speech.Kept;
        if (kept.Count == 0 || kept[^1].Id <= _lineSeen)
            return;
        var seen = _lineSeen;
        _lineSeen = kept[^1].Id;
        if (!Settings.Current.Babble)
            return;
        // The nearest new lines first: a line under way can't be cut, so a crowd keeps the nearest few.
        var fresh = view.Heard()
            .Where(l => l.Id > seen && sim.Clock.Tick - l.Tick <= FreshTicks)
            .Select(l => (Line: l, At: ToAudio(l.At)))
            .OrderBy(l => float3.Distance(l.At, audio.Listener))
            .ToList();
        foreach (var (line, at) in fresh)
        {
            if (_talking.Count >= MaxTalking)
                break; // lost under the others
            var syllables = Babble.For(line);
            if (syllables.Count == 0)
                continue;
            // Each voice from where it was said: near ones loud, far ones soft, as in a room.
            _talking.Add((_time + syllables[^1].Delay + 0.2f, float3.Distance(at, audio.Listener)));
            foreach (var s in syllables)
                audio.Play(Sounds.Voice, at, gain: s.Gain, pitch: s.Pitch, muffle: s.Muffle, delay: s.Delay, variant: s.Variant);
        }
    }

    private void Coins(Simulation sim, Entity avatar)
    {
        if (avatar.IsNull || !sim.World.IsAlive(avatar))
        {
            _coins = -1;
            return;
        }
        var now = Coin.Count(sim, avatar);
        if (_coinsOf == avatar && _coins >= 0 && now != _coins)
            audio.Play(Sounds.UiCoin, gain: Math.Clamp(0.5f + Math.Abs(now - _coins) / 20f, 0.5f, 1f), pitch: now > _coins ? 1.05f : 0.95f);
        (_coins, _coinsOf) = (now, avatar);
    }

    /// <summary>The slower look around: loops to hold at places, and the beds' levels.</summary>
    private void Survey(Simulation sim, PerceptionView view, int3? me, float3 listenAt)
    {
        _loops.Clear();
        var here = me ?? Cell(listenAt);
        var room = sim.Rooms.RoomAt(here);
        _indoors = room is { Indoors: true };
        var day = sim.Clock.Daylight;
        _birds = _indoors ? 0f : day * 0.7f;
        _night = _indoors ? 0.1f * (1 - day) : (1 - day) * 0.8f;
        _wind = _indoors ? 0.12f : 0.45f;

        // Hearths and forges that are lit, nearest few.
        var fires = Warmth.Sources(sim)
            .Select(f => (Cell: f.Fire, Dist: Dist2(f.Cell, here)))
            .Where(f => f.Dist < 16 * 16)
            .OrderBy(f => f.Dist)
            .Take(4);
        foreach (var f in fires)
            if (Reach(sim, view, me, f.Cell) is { } m)
                _loops.Add(($"hearth:{f.Cell.X},{f.Cell.Y},{f.Cell.Z}", ToAudio(f.Cell), 1f, m));

        // Wildfire: up to three of the nearest burning cells, as roaring voices.
        var map = sim.Map;
        var blazes = sim.Fire.BurningTiles
            .Select(i => map.CellOf(i))
            .Select(c => (Cell: c, Dist: Dist2(c, here)))
            .Where(c => c.Dist < 40 * 40)
            .OrderBy(c => c.Dist)
            .Take(3)
            .ToList();
        for (var k = 0; k < blazes.Count; k++)
            _loops.Add(($"blaze:{k}", ToAudio(blazes[k].Cell), 1f, 0f));
        if (blazes.Count > 0)
            _dangerUntil = Math.Max(_dangerUntil, _time + DangerHoldSeconds * 0.5f);

        // The river: its nearest stretch.
        var listen = ToAudio(here);
        var nearest = float.MaxValue;
        var river = default(float3);
        foreach (var w in _water)
        {
            var d = float3.DistanceSquared(w, listen);
            if (d < nearest)
                (nearest, river) = (d, w);
        }
        if (nearest < 40 * 40)
            _loops.Add(("river", river, 1f, _indoors ? 0.7f : 0f));

        // A room with people in: the murmur of a full inn.
        _crowd = 0f;
        if (room is { Indoors: true })
        {
            var people = 0;
            foreach (var e in view.Creatures())
                if (e != view.Viewer && sim.World.TryGet<Position>(e, out var pos) && sim.Rooms.RoomAt(pos.Cell) == room)
                    people++;
            _crowd = Math.Clamp((people - 1) / 6f, 0f, 1f);
        }

        // A dragon in sight.
        _dragonAt = null;
        foreach (var e in view.Creatures())
            if (sim.World.TryGet<Creature>(e, out var c) && c.Def.Race == Race.Dragon && !c.Dead && sim.World.TryGet<Position>(e, out var dp))
            {
                _dragonAt = dp.Pos;
                break;
            }
    }

    private void Ambience()
    {
        audio.Hold("bed:wind", Sounds.Wind, _wind, muffle: _indoors ? 0.6f : 0f);
        audio.Hold("bed:birds", Sounds.Birds, _birds);
        audio.Hold("bed:night", Sounds.Night, _night);
        audio.Hold("bed:crowd", Sounds.Crowd, _crowd);
        audio.Hold("bed:din", Sounds.Din, Math.Clamp(_recentFight.Count / 6f, 0f, 1f));
        foreach (var (key, at, gain, muffle) in _loops)
            audio.Hold(key, key.StartsWith("hearth", StringComparison.Ordinal) ? Sounds.Hearth : key.StartsWith("blaze", StringComparison.Ordinal) ? Sounds.Blaze : Sounds.River, gain, at, muffle);
    }

    private void PickMood(Simulation sim)
    {
        var phase = sim.Round.Phase;
        Mood = phase switch
        {
            RoundPhase.Over => Moods.Reveal,
            RoundPhase.Festival => Moods.Festival,
            _ when _time < _dangerUntil => Moods.Danger,
            _ => sim.Clock.Daylight < 0.3f ? Moods.Night : Moods.Day,
        };
        audio.PlayMusic(Mood);
    }

    /// <summary>The river and ponds: columns whose top block is water, every couple of cells.</summary>
    private static float3[] WaterCells(Simulation sim)
    {
        var map = sim.Map;
        var list = new List<float3>();
        for (var y = 0; y < map.Height; y += 2)
            for (var x = 0; x < map.Width; x += 2)
                for (var z = map.Depth - 1; z >= 0; z--)
                    if (map.Block[map.Idx(new int3(x, y, z))] is { Solid: true } top)
                    {
                        if (top.IsWater)
                            list.Add(ToAudio(new int3(x, y, z + 1)));
                        break;
                    }
        return [.. list];
    }

    private static int Dist2(int3 a, int3 b)
    {
        int dx = a.X - b.X, dy = a.Y - b.Y, dz = (a.Z - b.Z) * 3;
        return dx * dx + dy * dy + dz * dz;
    }

    private static float3 Normalize(float3 v, float3 fallback)
    {
        var len = MathF.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        return len > 1e-4f ? v / len : fallback;
    }

    private static float3 Cross(float3 a, float3 b) => new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
}

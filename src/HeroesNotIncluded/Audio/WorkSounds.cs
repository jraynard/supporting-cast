using HeroesNotIncluded.Sim.Healing;
using HeroesNotIncluded.Sim.Hospitality;
using HeroesNotIncluded.Sim.Tinkering;
using HeroesNotIncluded.Sim.Work;
using Narsil.Audio;

namespace HeroesNotIncluded.Audio;

/// <summary>
/// The sound of hands-on work, from the session in hand (the predicted one, so a blow is heard the
/// frame it lands): a loop for the station while it's worked (the forge's fire, a pot on the boil)
/// and a sound each time a count the player can see moves on (a blow, a pin set, a pinch in). It
/// reads only what the mini-game already shows; a lock's trap or a binding never makes a sound.
/// </summary>
public sealed class WorkSounds(AudioEngine audio)
{
    /// <summary>Fade for loops a held key turns on and off: quick, or they never come up.</summary>
    private const float Quick = 0.08f;

    private long _session = -1;
    private int _a, _b, _c;
    private float _q, _warm;
    private bool _flag;
    private Mods.WorkSoundContext? _mod;

    /// <summary>
    /// Where it's heard from, for someone else's work (<see cref="WorkSession.HeardByOthers"/>): null for your own,
    /// which sounds in your ears. Set each frame before <see cref="Update"/>, with how muffled and how loud.
    /// </summary>
    public float3? At { get; set; }

    public float Muffle { get; set; }

    public float Gain { get; set; } = 1f;

    /// <summary>What keeps this worker's loops apart from another's ("work:" for your own).</summary>
    public string Keys { get; init; } = "work:";

    private void Play(string id, float gain = 1f, float pitch = 1f, float delay = 0f) =>
        audio.Play(id, At, gain * Gain, pitch, Muffle, delay);

    private void Hold(string key, string id, float gain = 1f, float pitch = 1f, float fade = HoldFade) =>
        audio.Hold(Keys + key, id, gain * Gain, At, Muffle, pitch, fade);

    private const float HoldFade = AudioEngine.HoldFadeSeconds;

    /// <summary>Once a frame with the session being worked, or null.</summary>
    public void Update(WorkSession? session)
    {
        if (session == null)
        {
            _session = -1;
            return;
        }
        var fresh = session.StartTick != _session;
        if (fresh)
        {
            _session = session.StartTick;
            (_a, _b, _c, _q, _warm, _flag) = (0, 0, -1, 0f, 0f, false);
            if (At == null)
                Play(Sounds.UiOpen, gain: 0.5f); // the panel opening: yours alone
        }

        // A mod's session (or a mod's sound for one of the game's) first.
        _mod ??= new Mods.WorkSoundContext(audio);
        if (fresh)
            _mod.Reset();
        _mod.Fresh = fresh;
        if (At == null && Mods.GameMods.TrySound(_mod, session))
            return;

        switch (session)
        {
            case HammerSession s:
                if (s.Blows > _a)
                {
                    // A blow's own grade: how far it moved the running average (as the anvil draws it).
                    var grade = Math.Clamp(s.Quality * s.Blows - _q * _a, 0f, 1f);
                    var id = s.LastCold ? Sounds.AnvilCold : grade >= 0.75f ? Sounds.AnvilGood : grade >= 0.4f ? Sounds.AnvilFair : Sounds.AnvilPoor;
                    // The ring climbs a little as the piece takes shape.
                    Play(id, pitch: 1f + 0.06f * Math.Clamp(s.Blows / (float)Math.Max(1, s.BlowsNeeded), 0f, 1f));
                }
                (_a, _q) = (s.Blows, s.Quality);
                Hold("fire", Sounds.ForgeFire, 0.35f);
                break;
            case HeatSession s:
                Hold("fire", Sounds.ForgeFire, 0.4f + 0.6f * Math.Clamp(s.Fire / Math.Max(1f, s.FireMax), 0f, 1f));
                Pump(s.Pumping);
                break;
            case BloomSession s:
                Hold("fire", Sounds.ForgeFire, 0.4f + 0.6f * Math.Clamp(s.Fire / Math.Max(1f, s.FireMax), 0f, 1f));
                Pump(s.Held);
                break;
            case KindleSession s:
                // Each strike of the steel a clink; a breath on the ember the bellows' sigh, softly.
                if (s.Strikes > _a)
                    Play(Sounds.Clink, pitch: 1.4f);
                _a = s.Strikes;
                Pump(s.Held && s.Ember);
                Hold("fire", Sounds.ForgeFire, s.Ember ? 0.25f * s.Glow : 0f);
                break;
            case QuenchSession s:
                if (s.Dunked && !_flag)
                    Play(Sounds.ForgeQuench);
                _flag = s.Dunked;
                Hold("steam", Sounds.ForgeSteam, s.Steam);
                break;
            case SharpenSession s:
                Hold("grind", Sounds.Grind, s.OnWheel ? 0.8f : 0f, pitch: 0.9f + 0.2f * Math.Clamp(s.Heat, 0f, 1f), fade: Quick);
                break;
            case StewSession s:
                Hold("pot", Sounds.PotBubble, s.Lit ? Math.Clamp(s.Flame, 0.2f, 1f) : 0f);
                Hold("stir", Sounds.PotStir, s.Stirring ? 0.7f : 0f, fade: Quick);
                break;
            case SeasonSession s:
                Count(s.Pinches, Sounds.Pinch);
                break;
            case PourSession s:
                // F held is pouring (the level only moves on sim steps, not every frame). The note
                // climbs as the tankard fills, as a real pour's does; the head fizzes as it settles.
                var pouring = s.Held && !s.Spilled;
                Hold("pour", Sounds.Pour, pouring ? 0.9f : 0f, pitch: 0.85f + 0.4f * Math.Clamp(s.Top, 0f, 1f), fade: Quick);
                Hold("fizz", Sounds.Fizz, Math.Clamp(s.Foam * 4f, 0f, 0.8f), fade: 0.3f);
                break;
            case WashSession s:
                Count(s.Washed, Sounds.Clink);
                Hold("scrub", Sounds.Scrub, s.Scrubbing ? 0.5f : 0f, fade: Quick);
                break;
            case TapSession s:
                Count(s.Blows + s.Misses, Sounds.Knock);
                break;
            case StrikerSession s:
                if (s.Swings > _a && s.Last >= StrikerSession.Bell)
                    Play(Sounds.UiBell, delay: 0.25f);
                Count(s.Swings, Sounds.Knock);
                break;
            case MixSession s:
                Count(s.Hits, Sounds.Clink);
                break;
            case ScrubSession s:
                Count(s.Strokes, Sounds.Scrub);
                break;
            case MakeBedSession s:
                Count(s.Movements, Sounds.Cloth);
                break;
            case SlopSession s:
                Count(s.Poured, Sounds.Scrub); // a slosh, till it has a sound of its own
                break;
            case SignInSession s:
                Count(s.Begun ? 1 : 0, Sounds.Cloth); // a quill's scratch, till it has a sound of its own
                break;
            case BandageSession s:
                Count(s.TurnsDone, Sounds.Cloth);
                break;
            case CastSession s:
                Count(s.Beats, Sounds.Rune);
                if (s.LastWovenAt != _b && !fresh)
                    Play(Sounds.Weave);
                _b = s.LastWovenAt;
                Hold("hum", Sounds.Hum, 0.5f);
                break;
            case HandsOnSession s:
            {
                // The gift's hum, up while the link's open and climbing as the strain does; unlinked,
                // it swells and brightens as the hands near a hurt (the warmth the scene shows, smoothed
                // so a trembling hand doesn't make it warble).
                _warm += (Math.Clamp(s.Feel, 0f, 1f) - _warm) * 0.15f;
                Hold("hum", Sounds.Hum, s.Linked ? 0.75f : 0.3f + 0.4f * _warm,
                    pitch: s.Linked ? 1f + 0.25f * Math.Clamp(s.Link.Strain, 0f, 1f) : 1f + 0.15f * _warm, fade: 0.2f);
                // A soft beat each time a new warmth is sensed (ones read before are known already).
                var sensed = s.Spots.Count(p => p.Sensed > 0f);
                if (_c >= 0 && sensed > _c)
                    Play(Sounds.Pulse, gain: 0.6f, pitch: 1.35f);
                _c = sensed;
                // A wound's beat under a pressing hand, each time it swells (its kind's way, as the scene shows it).
                if (s.Pulse > 0.6f && !_flag)
                    Play(Sounds.Pulse, gain: 0.4f + 0.6f * s.Pulse, pitch: s.Linked ? 1.1f : 1f);
                _flag = s.Pulse > 0.6f || (_flag && s.Pulse > 0.35f);
                if (s.LastDrawnAt > _a && !fresh)
                    Play(Sounds.Drawn);
                _a = Math.Max(_a, s.LastDrawnAt);
                if (s.Flinches > _b && !fresh)
                    Play(Sounds.Pulse, gain: 0.9f, pitch: 0.7f);
                _b = s.Flinches;
                if (s.Link.Snapped && _q == 0f)
                    Play(Sounds.Snap);
                _q = s.Link.Snapped ? 1f : 0f;
                break;
            }
            case MeditateSession:
                Hold("hum", Sounds.Hum, 0.6f);
                break;
            case PinSession s:
                Count(s.Pin, Sounds.Click);
                break;
            case PickSession s:
                Count(s.Pin, Sounds.Click);
                break;
            case FitSession s:
                Count(s.Part, Sounds.Ratchet);
                break;
            case TrueSession s:
                Count(s.Keyed, Sounds.Ratchet);
                break;
            case LoupeSession s:
                Count(s.ReadCount, Sounds.Click);
                break;
            case ProbeSession s:
                // A tick in the fingers when the probe catches (the feel it shows), not again till it's let go.
                if (!_flag && s.Feel > 0.7f)
                    Play(Sounds.Click, gain: 0.6f, pitch: 0.8f);
                _flag = s.Feel > 0.7f || (_flag && s.Feel > 0.45f);
                // The spring out: a snap, once (the session ends on it).
                if (s.Disarmed && _a == 0)
                {
                    Play(Sounds.Snap, gain: 0.8f, pitch: 1.3f);
                    _a = 1;
                }
                break;
        }
    }

    /// <summary>A sound each time <paramref name="now"/> goes up.</summary>
    private void Count(int now, string id)
    {
        if (now > _a)
            Play(id);
        _a = now;
    }

    /// <summary>A breath of the bellows on each new push.</summary>
    private void Pump(bool pumping)
    {
        if (pumping && !_flag)
            Play(Sounds.ForgeBellows);
        _flag = pumping;
    }
}

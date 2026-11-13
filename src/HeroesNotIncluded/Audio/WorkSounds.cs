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
    private long _session = -1;
    private int _a, _b;
    private float _q;
    private bool _flag;

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
            (_a, _b, _q, _flag) = (0, 0, 0f, false);
            audio.Play(Sounds.UiOpen, gain: 0.5f);
        }

        switch (session)
        {
            case HammerSession s:
                if (s.Blows > _a)
                {
                    // A blow's own grade: how far it moved the running average (as the anvil draws it).
                    var grade = Math.Clamp(s.Quality * s.Blows - _q * _a, 0f, 1f);
                    var id = s.LastCold ? Sounds.AnvilCold : grade >= 0.75f ? Sounds.AnvilGood : grade >= 0.4f ? Sounds.AnvilFair : Sounds.AnvilPoor;
                    // The ring climbs a little as the piece takes shape.
                    audio.Play(id, pitch: 1f + 0.06f * Math.Clamp(s.Blows / (float)Math.Max(1, s.BlowsNeeded), 0f, 1f));
                }
                (_a, _q) = (s.Blows, s.Quality);
                audio.Hold("work:fire", Sounds.ForgeFire, 0.35f);
                break;
            case HeatSession s:
                audio.Hold("work:fire", Sounds.ForgeFire, 0.4f + 0.6f * Math.Clamp(s.Fire / Math.Max(1f, s.FireMax), 0f, 1f));
                Pump(s.Pumping);
                break;
            case BloomSession s:
                audio.Hold("work:fire", Sounds.ForgeFire, 0.4f + 0.6f * Math.Clamp(s.Fire / Math.Max(1f, s.FireMax), 0f, 1f));
                Pump(s.Held);
                break;
            case QuenchSession s:
                if (s.Dunked && !_flag)
                    audio.Play(Sounds.ForgeQuench);
                _flag = s.Dunked;
                audio.Hold("work:steam", Sounds.ForgeSteam, s.Steam);
                break;
            case SharpenSession s:
                audio.Hold("work:grind", Sounds.Grind, s.OnWheel ? 0.8f : 0f, pitch: 0.9f + 0.2f * Math.Clamp(s.Heat, 0f, 1f));
                break;
            case StewSession s:
                audio.Hold("work:pot", Sounds.PotBubble, s.Lit ? Math.Clamp(s.Flame, 0.2f, 1f) : 0f);
                audio.Hold("work:stir", Sounds.PotStir, s.Stirring ? 0.7f : 0f);
                break;
            case SeasonSession s:
                Count(s.Pinches, Sounds.Pinch);
                break;
            case PourSession s:
                audio.Hold("work:pour", Sounds.Pour, s.Level > _q + 0.0005f ? 0.9f : 0f, pitch: 0.9f + 0.3f * Math.Clamp(s.Level, 0f, 1f));
                _q = s.Level;
                break;
            case WashSession s:
                Count(s.Washed, Sounds.Clink);
                audio.Hold("work:scrub", Sounds.Scrub, s.Scrubbing ? 0.5f : 0f);
                break;
            case TapSession s:
                Count(s.Blows + s.Misses, Sounds.Knock);
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
            case BandageSession s:
                Count(s.TurnsDone, Sounds.Cloth);
                break;
            case CastSession s:
                Count(s.Beats, Sounds.Rune);
                if (s.LastWovenAt != _b && !fresh)
                    audio.Play(Sounds.Weave);
                _b = s.LastWovenAt;
                audio.Hold("work:hum", Sounds.Hum, 0.5f);
                break;
            case FeelSession or DrawSession or MeditateSession:
                audio.Hold("work:hum", Sounds.Hum, 0.6f);
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
                Count(s.Turns, Sounds.Ratchet);
                break;
            case LoupeSession s:
                Count(s.ReadCount, Sounds.Click);
                break;
        }
    }

    /// <summary>A sound each time <paramref name="now"/> goes up.</summary>
    private void Count(int now, string id)
    {
        if (now > _a)
            audio.Play(id);
        _a = now;
    }

    /// <summary>A breath of the bellows on each new push.</summary>
    private void Pump(bool pumping)
    {
        if (pumping && !_flag)
            audio.Play(Sounds.ForgeBellows);
        _flag = pumping;
    }
}

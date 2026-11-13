using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Ecs;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Audio;

/// <summary>
/// Speech as babble: a line becomes a run of spoken syllables (<c>voice/blip_ROV.ogg</c>: register,
/// onset consonant, vowel, from tools/sfx_synth.py), picked from the line's own letters so different
/// words sound different. Each speaker has a voice of their own, steady all game: a register (deep,
/// middling, high), a pitch within it, how loud and how quick they talk. Each line has a melody,
/// drifting down as speech does, with stressed syllables, a fall at the end or a question's rise.
/// A voice you couldn't place takes all of that from the line instead, so it never gives away who.
/// Real voices (text to speech) can replace this later; the timing would still apply.
/// </summary>
public static class Babble
{
    /// <summary>One syllable: when (seconds after the first), which sound, and how.</summary>
    public readonly record struct Syllable(float Delay, int Variant, float Pitch, float Gain, float Muffle);

    /// <summary>A speaker's voice: register 0..2, pitch within it, loudness and pace.</summary>
    public readonly record struct Voice(int Register, float Pitch, float Loudness, float Pace);

    public const int Registers = 3, Onsets = 7, Vowels = 5;
    public const int MaxSyllables = 18;
    public const float Step = 0.13f, WordGap = 0.05f, Pause = 0.2f;

    public static Voice VoiceOf(Entity? speaker, long lineId)
    {
        var (a, b) = speaker is { } e ? (e.Index, e.Generation) : ((int)lineId, -1);
        float U(uint salt) => (SimRandom.Hash(a, b, salt) & 0xFFFF) / 65535f;
        // Deep and middling voices are commoner than high ones.
        var register = U(0xB0B) switch { < 0.4f => 0, < 0.82f => 1, _ => 2 };
        return new Voice(register, 0.93f + 0.15f * U(0xB0C), 0.65f + 0.5f * U(0xB0D), 0.85f + 0.35f * U(0xB0E));
    }

    /// <summary>The pitch a speaker talks at, all things considered (tests: everyone sounds their own).</summary>
    public static float VoicePitch(Entity? speaker, long lineId)
    {
        var v = VoiceOf(speaker, lineId);
        return (v.Register + 1) * v.Pitch;
    }

    public static List<Syllable> For(HeardLine line)
    {
        var list = new List<Syllable>();
        var voice = VoiceOf(line.Speaker, line.Id);
        var (gain, lift, muffle) = line.Volume switch
        {
            Volume.Whisper => (0.35f, 0.97f, 0.45f),
            Volume.Shout => (1.3f, 1.1f, 0f),
            _ => (0.8f, 1f, 0f),
        };
        gain *= voice.Loudness;
        if (line.Clarity == Clarity.Muffled)
            muffle = Math.Max(muffle, 0.85f);
        float Jitter(int i, uint salt, float amount) => 1f + ((SimRandom.Hash((int)line.Id, i, salt) & 0xFF) / 255f - 0.5f) * amount;

        // The syllables first, then the melody over them.
        var sounds = new List<(int Onset, int Vowel, bool Stressed, bool WordEnd, bool Pause)>();
        var text = line.Text;
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var word in words)
        {
            if (word.StartsWith('*'))
                continue; // stage directions: *cough*
            var syl = Syllables(word);
            if (syl.Count == 0)
                syl.Add((0, 0)); // "...", "hm", numbers
            syl = syl.Take(3).ToList();
            for (var k = 0; k < syl.Count && sounds.Count < MaxSyllables; k++)
                sounds.Add((syl[k].Onset, syl[k].Vowel, k == 0 && syl.Count > 1, k == syl.Count - 1,
                    k == syl.Count - 1 && word[^1] is ',' or '.' or '!' or '?' or ';' or ':'));
        }
        if (sounds.Count == 0)
            return list;

        var question = text.TrimEnd().EndsWith('?');
        var t = 0f;
        for (var i = 0; i < sounds.Count; i++)
        {
            var s = sounds[i];
            var u = sounds.Count == 1 ? 0f : i / (float)(sounds.Count - 1);
            var melody = 1.05f - 0.1f * u; // drifting down through the line
            if (s.Stressed)
                melody *= 1.07f;
            if (i == sounds.Count - 1)
                melody *= question ? 1.2f : 0.94f;
            var pitch = voice.Pitch * lift * melody * Jitter(i, 0x51, 0.06f);
            var variant = (voice.Register * Onsets + s.Onset) * Vowels + s.Vowel;
            list.Add(new Syllable(t, variant, pitch, gain * (s.Stressed ? 1.15f : 1f) * Jitter(i, 0x52, 0.2f), muffle));
            var step = Step * (s.Stressed ? 1.15f : 1f) * Jitter(i, 0x53, 0.25f);
            t += step / voice.Pace + (s.WordEnd ? WordGap : 0f) + (s.Pause ? Pause * Jitter(i, 0x54, 0.4f) : 0f);
        }
        return list;
    }

    /// <summary>A word's syllables: each run of vowels, with the consonant sound just before it.</summary>
    private static List<(int Onset, int Vowel)> Syllables(string word)
    {
        var found = new List<(int, int)>();
        var onset = 0;
        var inVowel = false;
        foreach (var raw in word)
        {
            var c = char.ToLowerInvariant(raw);
            var vowel = c is 'a' or 'e' or 'i' or 'o' or 'u' or 'y';
            if (vowel && !inVowel)
            {
                found.Add((onset, VowelOf(c)));
                onset = 0;
            }
            else if (!vowel && char.IsLetter(c))
                onset = OnsetOf(c);
            inVowel = vowel;
        }
        return found;
    }

    /// <summary>The onsets synthesized (tools/sfx_synth.py ONSETS): none, b, d, g, m, s, l.</summary>
    private static int OnsetOf(char c) => c switch
    {
        'b' or 'p' => 1,
        'd' or 't' => 2,
        'g' or 'k' or 'q' or 'c' or 'j' or 'x' => 3,
        'm' or 'n' => 4,
        's' or 'z' or 'f' or 'h' or 'v' => 5,
        'l' or 'r' or 'w' => 6,
        _ => 0,
    };

    private static int VowelOf(char vowel) => vowel switch
    {
        'a' => 0,
        'e' => 1,
        'i' or 'y' => 2,
        'o' => 3,
        _ => 4,
    };
}

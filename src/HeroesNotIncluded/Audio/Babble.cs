using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Ecs;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Audio;

/// <summary>
/// Speech as babble: a line becomes a run of syllable blips, timed by its words and punctuation, in
/// a voice of the speaker's own (a pitch from their entity, steady all game). A voice you couldn't
/// place gets one from the line instead, so babble never names whoever you didn't recognise.
/// Real voices (text to speech) can replace this later; the timing would still apply.
/// </summary>
public static class Babble
{
    /// <summary>One blip: when (seconds after the first), which syllable sound, and how.</summary>
    public readonly record struct Syllable(float Delay, int Variant, float Pitch, float Gain, float Muffle);

    public const int MaxSyllables = 16;
    public const float Step = 0.085f, WordGap = 0.05f, Pause = 0.16f;

    /// <summary>The voice's pitch, 0.72..1.38: low and high voices both, the same for a speaker every time.</summary>
    public static float VoicePitch(Entity? speaker, long lineId)
    {
        var h = speaker is { } e ? SimRandom.Hash(e.Index, e.Generation, 0xB0B) : SimRandom.Hash((int)lineId, 7, 0xB0B);
        var t = (h & 0xFFFF) / 65535f;
        return speaker is null ? 0.95f + 0.1f * t : 0.72f + 0.66f * t;
    }

    public static List<Syllable> For(HeardLine line)
    {
        var list = new List<Syllable>();
        var voice = VoicePitch(line.Speaker, line.Id);
        var (gain, lift, muffle) = line.Volume switch
        {
            Volume.Whisper => (0.35f, 0.97f, 0.45f),
            Volume.Shout => (1.25f, 1.08f, 0f),
            _ => (0.8f, 1f, 0f),
        };
        if (line.Clarity == Clarity.Muffled)
            muffle = Math.Max(muffle, 0.85f);
        var t = 0f;
        var text = line.Text;
        var question = text.TrimEnd().EndsWith('?');
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var w = 0; w < words.Length && list.Count < MaxSyllables; w++)
        {
            var word = words[w];
            if (word.StartsWith('*'))
                continue; // stage directions: *cough*
            var vowels = Vowels(word);
            if (vowels.Count == 0)
                vowels.Add('a'); // "..." when muffled, "hm", numbers
            foreach (var v in vowels.Take(3))
            {
                if (list.Count >= MaxSyllables)
                    break;
                var jitter = 1f + ((SimRandom.Hash((int)line.Id, list.Count, 0x51) & 0xFF) / 255f - 0.5f) * 0.12f;
                var rise = question && w == words.Length - 1 ? 1.12f : 1f;
                list.Add(new Syllable(t, Variant(v), voice * lift * jitter * rise, gain, muffle));
                t += Step;
            }
            t += WordGap + (word[^1] is ',' or '.' or '!' or '?' or ';' or ':' ? Pause : 0f);
        }
        return list;
    }

    /// <summary>The vowel sounds of a word: each run of vowels is one syllable.</summary>
    private static List<char> Vowels(string word)
    {
        var found = new List<char>();
        var inVowel = false;
        foreach (var raw in word)
        {
            var c = char.ToLowerInvariant(raw);
            var vowel = c is 'a' or 'e' or 'i' or 'o' or 'u' or 'y';
            if (vowel && !inVowel)
                found.Add(c);
            inVowel = vowel;
        }
        return found;
    }

    /// <summary>a e i o u (y as i): the order of <c>voice.blip</c>'s files.</summary>
    private static int Variant(char vowel) => vowel switch
    {
        'a' => 0,
        'e' => 1,
        'i' or 'y' => 2,
        'o' => 3,
        _ => 4,
    };
}

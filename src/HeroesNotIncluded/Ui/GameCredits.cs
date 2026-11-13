using Narsil.Ui;

namespace HeroesNotIncluded.Ui;

/// <summary>
/// Who made the game, for the Credits window (on the title screen and in game alike), so the two
/// never drift apart. Playtesters get a section of their own once there's a list of them.
/// </summary>
public static class GameCredits
{
    public static readonly IReadOnlyList<CreditsSection> People =
    [
        new("Created by", ["Justin Raynard", "Tasha Raynard"]),
        new("Made with the help of", ["Claude"]),
        // Placeholder music until our own (tools/fetch_music.py); CC-BY asks for these lines, and
        // the track titles are in NOTICE.txt, which the window shows below.
        new("Music", ["Kevin MacLeod (incompetech.com), CC BY 4.0", "Tausdei Music, CC BY 3.0", "RandomMind, CC0"]),
        new("Sound", ["Effects shared as CC0 on OpenGameArt", "by Kenney, StarNinjas, Vehicle and others"]),
    ];
}

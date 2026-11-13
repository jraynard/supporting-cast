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
        new("Made with the help of", ["Claude, by Anthropic"]),
    ];
}

namespace HeroesNotIncluded.Audio;

/// <summary>
/// Every sound id and playlist the game names, so <c>--check-art</c> (<see cref="SoundAudit"/>)
/// can hold <c>data/sounds/sounds.json</c> to them. What each id plays is the manifest's business.
/// </summary>
public static class Sounds
{
    // Interface.
    public const string UiOpen = "ui.open";
    public const string UiClose = "ui.close";
    public const string UiCoin = "ui.coin";
    public const string UiBell = "ui.bell";
    public const string UiGood = "ui.good";

    // The smithy.
    public const string AnvilGood = "anvil.good";
    public const string AnvilFair = "anvil.fair";
    public const string AnvilPoor = "anvil.poor";
    public const string AnvilCold = "anvil.cold";
    public const string ForgeFire = "forge.fire";
    public const string ForgeBellows = "forge.bellows";
    public const string ForgeQuench = "forge.quench";
    public const string ForgeSteam = "forge.steam";
    public const string Grind = "work.grind";

    // Kitchen and bar.
    public const string PotBubble = "kitchen.bubble";
    public const string PotStir = "kitchen.stir";
    public const string Pinch = "kitchen.pinch";
    public const string Pour = "bar.pour";
    public const string Fizz = "bar.fizz";
    public const string Clink = "bar.clink";
    public const string Knock = "bar.knock";
    public const string Scrub = "work.scrub";
    public const string Cloth = "work.cloth";

    // A ghost's sign: something gives under no one's weight.
    public const string Creak = "ghost.creak";

    // The tinker and the empath.
    public const string Click = "tinker.click";
    public const string Ratchet = "tinker.ratchet";
    public const string Rune = "heal.rune";
    public const string Weave = "heal.weave";
    public const string Hum = "heal.hum";

    // Fights and the dragon.
    public const string Swing = "combat.swing";
    public const string Clash = "combat.clash";
    public const string Wound = "combat.hit";
    public const string Bow = "combat.bow";
    public const string Miss = "combat.miss";
    public const string Din = "combat.din";
    public const string Parry = "combat.parry";
    public const string Block = "combat.block";
    public const string Thrust = "combat.thrust";
    public const string Heavy = "combat.heavy";
    public const string Roar = "dragon.roar";
    public const string Breath = "dragon.breath";
    public const string Wings = "dragon.wings";

    // The world around.
    public const string Wind = "amb.wind";
    public const string Birds = "amb.birds";
    public const string Night = "amb.night";
    public const string River = "amb.river";
    public const string Hearth = "amb.fire";
    public const string Blaze = "amb.blaze";
    public const string Crowd = "amb.crowd";

    /// <summary>Speech babble: one syllable per play, its variant picked from the letters.</summary>
    public const string Voice = "voice.blip";

    public static readonly IReadOnlyList<string> All =
    [
        UiOpen, UiClose, UiCoin, UiBell, UiGood,
        AnvilGood, AnvilFair, AnvilPoor, AnvilCold, ForgeFire, ForgeBellows, ForgeQuench, ForgeSteam, Grind,
        PotBubble, PotStir, Pinch, Pour, Fizz, Clink, Knock, Scrub, Cloth, Creak,
        Click, Ratchet, Rune, Weave, Hum,
        Swing, Clash, Wound, Bow, Miss, Din, Parry, Block, Thrust, Heavy, Roar, Breath, Wings,
        Wind, Birds, Night, River, Hearth, Blaze, Crowd,
        Voice,
    ];
}

/// <summary>Music by mood: the playlists in <c>sounds.json</c>'s <c>music</c>.</summary>
public static class Moods
{
    public const string Title = "title";
    public const string Day = "day";
    public const string Night = "night";
    public const string Danger = "danger";
    public const string Festival = "festival";
    public const string Reveal = "reveal";

    public static readonly IReadOnlyList<string> All = [Title, Day, Night, Danger, Festival, Reveal];
}

using Brutal.Numerics;

namespace HeroesNotIncluded.Ui;

/// <summary>
/// Whether the mouse pointer still says what the player means. Walking with a stick or WASD leaves
/// the pointer wherever it last rested (a trackpad on a Steam Deck), so once the avatar has moved
/// since the pointer did, F should act on what's in front rather than on that old spot. Moving the
/// pointer again makes it count.
/// </summary>
public struct PointerIntent
{
    private int3? _avatarCell;

    /// <summary>True when the avatar has walked since the pointer last moved.</summary>
    public bool Stale { get; private set; }

    /// <summary>Call once a frame with whether the pointer moved and where the avatar stands.</summary>
    public void Update(bool pointerMoved, int3 avatarCell)
    {
        if (pointerMoved)
            Stale = false;
        else if (_avatarCell is { } last && last != avatarCell)
            Stale = true;
        _avatarCell = avatarCell;
    }
}

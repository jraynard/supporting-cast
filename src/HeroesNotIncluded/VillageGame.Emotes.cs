using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Speech;

namespace HeroesNotIncluded;

/// <summary>
/// Emotes and quick lines (docs/ROADMAP.md, "Emotes and quick-say"): the easy ways to be understood
/// without typing, from the wheel. An emote is an <see cref="EmoteCommand"/>, seen by whoever can
/// see you; a quick line is an ordinary <see cref="SayCommand"/>, heard as any voice is.
/// </summary>
public sealed partial class VillageGame
{
    /// <summary>
    /// A quick line: its bark (<c>barks_quick.json</c>, <c>quick_{Id}</c>), the wheel's label, and how loud; offered
    /// only <paramref name="When"/> it'd mean something, if that's said (the brawl's lines, while there's a brawl).
    /// </summary>
    private readonly record struct QuickLine(string Id, string Label, Volume Volume, Func<VillageGame, bool>? When = null);

    /// <summary>A brawl at the inn under way (<see cref="Sim.Hospitality.BrawlCrisis"/>): its quick lines are on the wheel.</summary>
    private bool Brawling => Sim.Hospitality.BrawlCrisis.Of(_sim) is { Begun: true };

    /// <summary>Clockwise from the top, as the wheel shows them: help first, the alarms at the bottom.</summary>
    private static readonly QuickLine[] QuickLines =
    [
        new("help", "Help!", Volume.Shout),
        new("here", "Over here!", Volume.Shout),
        new("thanks", "Thanks", Volume.Say),
        new("aye", "Aye", Volume.Say),
        new("fire", "Fire!", Volume.Shout),
        new("raiders", "Raiders!", Volume.Shout),
        new("nay", "Nay", Volume.Say),
        new("coming", "Coming!", Volume.Say),
        // A brawl at the inn: words that may settle it (BrawlCrisis.Heard).
        new("outside", "Take it outside!", Volume.Shout, g => g.Brawling),
        new("watch", "The watch is coming!", Volume.Shout, g => g.Brawling),
        new("drinks", "Drinks on the house!", Volume.Shout, g => g.Brawling && g.HasAvatar
            && g._sim.World.TryGet<Trade>(g._avatar, out var t) && t.Def.Role == Sim.Defs.TownRole.Tavern),
    ];

    /// <summary>The emotes as the wheel offers them, clockwise from the top.</summary>
    private static readonly WheelEntry[] EmoteEntries =
        [.. Enum.GetValues<Emote>().Select(k => new WheelEntry(Act.Emote, _ => k.ToString(), g => g.ShowEmote(k), WorldRenderer.EmoteIcon(k)))];

    private static readonly WheelEntry[] QuickEntries =
        [.. QuickLines.Select(q => new WheelEntry(Act.Emote, g => q.When == null || q.When(g) ? q.Label : null, g => g.SayQuick(q)))];

    /// <summary>Picks among a quick line's wordings: presentation only, so its own random, never the sim's.</summary>
    private readonly SimRandom _quickRand = new(0x51C4);

    private string? _lastQuick;

    private bool _devEmoted;

    /// <summary><c>--emote KIND</c>: shown once, <see cref="DevEmoteAt"/> seconds in (for a screenshot).</summary>
    private void ShowDevEmote()
    {
        if (_devEmoted || DevEmote == null || _time < DevEmoteAt || !HasAvatar)
            return;
        _devEmoted = true;
        if (Enum.TryParse<Emote>(DevEmote, true, out var kind))
            ShowEmote(kind);
        else
            Notify(MessageKind.Warning, $"--emote: no emote '{DevEmote}' ({string.Join(", ", Enum.GetNames<Emote>())}).");
    }

    private void ShowEmote(Emote kind)
    {
        if (!HasAvatar)
            return;
        if (Emotes.Problem(_sim, _avatar, kind) is { } why and not "too soon")
        {
            Notify(MessageKind.Info, $"Can't {kind.ToString().ToLowerInvariant()} now: {why}.");
            return;
        }
        Issue(new EmoteCommand(_avatar, kind, kind == Emote.Point ? PointedCell() : null));
    }

    private void SayQuick(QuickLine q)
    {
        if (!HasAvatar && !Ghosting)
            return;
        // At work for two with nobody helping, "Help!" is a call for a hand there: a villager may come.
        if (q.Id == "help" && HasAvatar && Sim.Work.Pairs.WantsHand(_sim, _avatar))
        {
            Issue(new Sim.Work.AskForHandCommand(_avatar));
            return;
        }
        var text = Barks.Pick(_sim.Defs, "quick_" + q.Id, [], _quickRand, _lastQuick) ?? q.Label;
        _lastQuick = text;
        Issue(new SayCommand(HasAvatar ? _avatar : _ghost, text, q.Volume, q.Id));
    }

    /// <summary>
    /// Where a point points: the cell that was under the mouse when the wheel opened, or on a pad
    /// a few strides along your aim.
    /// </summary>
    private int3? PointedCell()
    {
        if (_wheelAimed is { } aimed)
            return aimed;
        var w = _sim.World;
        if (!w.TryGet<FreeMover>(_avatar, out var fm))
            return null;
        // Out along the aim as far as you can see, up to a few strides: never through a wall.
        var here = w.Get<Position>(_avatar).Cell;
        int3? last = null;
        for (var stride = 1; stride <= 6; stride++)
        {
            var c = new int3(here.X + (int)MathF.Round(MathF.Cos(fm.Aim) * stride), here.Y + (int)MathF.Round(MathF.Sin(fm.Aim) * stride), here.Z);
            if (!_sim.Map.InBounds(c) || _sim.Map.IsSolid(_sim.Map.Idx(c)) || !View.CanSeeCell(c))
                break;
            last = c;
        }
        return last;
    }
}

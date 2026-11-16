using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Bots;

/// <summary>
/// The cook's games, played by hand from what the session shows: the knife's beat, the cue to
/// fold, the crust on each side of the roast, the smell from the oven, the cleaver over the
/// joints, the skin pulling taut. Never what's hidden (how far a roast's cooked through, the bake,
/// where a bird's skin gives). A slip is a real input: a cut off the beat, a roast left a little
/// long, a cord tied at the first feel of snug.
/// </summary>
public sealed partial class BotPlayer
{
    /// <summary>The last thing it sniffed, and how it smelt (the worst of its sniffs): read once the sniffing's done.</summary>
    private (Entity Target, string Smell)? _sniffed;

    /// <summary>Sniffs it takes of a thing before it trusts its nose.</summary>
    public const int SniffsWanted = 2;

    /// <summary>Step away from the game in hand: it's done all it means to (or the fire's out under it).</summary>
    private void StopKitchenWork()
    {
        if (_stopped)
            return;
        _stopped = true;
        if (Players.CharacterOf(world(), player) is { IsNull: false } me)
            Send(new StopWorkCommand(me));
    }

    /// <summary>A cut on each beat of the knife. A slip: one cut hurried off the beat, which may catch an arm.</summary>
    private (bool, bool, int) Chop(ChopSession s)
    {
        if (_slip && !_slipCounted && s.Cuts == s.Needed / 2 && s.Phase == ChopSession.Beat / 2)
        {
            Slipped();
            return (true, false, 0);
        }
        var on = s.Off <= 1;
        var press = on && !_struck;
        _struck = on;
        return (press, false, 0);
    }

    /// <summary>A push on each beat, from the side it's due. A slip: one push from the wrong side.</summary>
    private (bool, bool, int) Knead(KneadSession s)
    {
        var on = s.Off <= 1;
        var push = on && !_struck;
        _struck = on;
        if (!push)
            return (false, false, 0);
        if (_slip && !_slipCounted && s.Pushed == KneadSession.Pushes / 2)
        {
            Slipped();
            return (false, false, -s.NextSide);
        }
        return (false, false, s.NextSide);
    }

    /// <summary>A crimp as the pinch comes to each mark. A slip: one crimp late, past its mark, and the rim's loose there.</summary>
    private (bool, bool, int) Crimp(CrimpSession s)
    {
        var mark = (int)MathF.Round(s.At) % CrimpSession.Marks;
        if (mark == _lastRune)
            return (false, false, 0);
        var late = _slip && !_slipCounted && mark == CrimpSession.Marks / 2;
        if (late ? s.At - MathF.Round(s.At) is < 0.28f or > 0.45f : s.Off > 0.04f)
            return (false, false, 0);
        _lastRune = mark;
        if (late)
            Slipped();
        return (true, false, 0);
    }

    /// <summary>
    /// Turn it on as each side colours, keeping them even, and away from a flare at once; lift it
    /// when every side's golden through. A slip: it's left on a little long, a side or two gone dark.
    /// </summary>
    private (bool, bool, int) Turn(RoastSession s)
    {
        if (!s.Lit)
        {
            StopKitchenWork();
            return (false, false, 0);
        }
        if (_working > 0)
        {
            _working--; // let the last turn show
            return (false, false, 0);
        }
        float least = float.MaxValue, most = 0f;
        foreach (var b in s.Brown)
            (least, most) = (Math.Min(least, b), Math.Max(most, b));
        var lift = _slip ? Roasting.DarkAt + 0.02f : 0.92f;
        if (least >= lift || (most >= Roasting.DarkAt - 0.05f && least >= Roasting.GoldenAt && !_slip))
        {
            if (_slip)
                Slipped();
            _act = true;
            return (false, false, 0);
        }
        var side = s.Brown[s.Facing];
        if (s.Flaring > 0 || side >= least + 0.2f || side >= lift + 0.03f)
        {
            _working = 4;
            return (true, false, 0);
        }
        return (false, false, 0);
    }

    /// <summary>Out on the golden crust. A slip: it's left till the smell turns toasty.</summary>
    private (bool, bool, int) Bake(BakeSession s)
    {
        if (!s.Lit)
        {
            StopKitchenWork();
            return (false, false, 0);
        }
        var golden = Baking.SmellOf(Baking.GoldenAt);
        var toasty = Baking.SmellOf(Baking.ToastyAt);
        if (s.Smell == golden && !_slip)
            _act = true;
        else if (s.Smell == toasty || s.Smell == Baking.SmellOf(Baking.BurningAt))
        {
            if (_slip)
                Slipped();
            _act = true;
        }
        return (false, false, 0);
    }

    /// <summary>
    /// The cleaver carried to the nearest joint still whole, the hand let settle, and down. A slip:
    /// one blow brought down in a hurry, before the hand's still.
    /// </summary>
    private (bool, bool, int) Cleave(ButcherSession s)
    {
        var target = -1f;
        for (var i = 0; i < ButcherSession.JointsAt.Length; i++)
            if (!s.Parted[i] && (target < 0f || MathF.Abs(ButcherSession.JointsAt[i] - s.Cleaver) < MathF.Abs(target - s.Cleaver)))
                target = ButcherSession.JointsAt[i];
        if (target < 0f)
            return (false, false, 0);
        var off = target - s.Cleaver;
        if (MathF.Abs(off) > Aim.PerStep * 0.55f)
            return (false, false, Math.Sign(off));
        if (!s.Ready)
            return (false, false, 0);
        if (_slip && !_slipCounted && s.Parted.Count(p => p) == 2)
        {
            Slipped();
            return (true, false, 0);
        }
        return (s.Settled, false, 0);
    }

    /// <summary>Short firm pulls: hold till it's pulling well, let go before it's taut. A slip: one pull held on into taut.</summary>
    private (bool, bool, int) Pluck(PluckSession s)
    {
        var greedy = _slip && !_slipCounted && _working == 1 && s.Tears == 0;
        var held = s.Pull < (greedy ? 0.88f : 0.55f);
        if (!held)
        {
            _working++;
            if (greedy)
                Slipped();
        }
        return (false, held, 0);
    }

    /// <summary>
    /// Each corner folded on its cue from its side; then the cord drawn, held on a breath past the
    /// first feel of snug, and tied. A slip: tied at the first feel of snug, slack beneath it.
    /// </summary>
    private (bool, bool, int) Pack(PackSession s)
    {
        if (!s.Wrapped)
        {
            var on = s.OnCue;
            var fold = on && !_struck;
            _struck = on;
            return (false, false, fold ? s.NextSide : 0);
        }
        if (s.Feel != "slack")
            _working++;
        var tie = s.Feel == "biting" || _working >= (_slip ? 3 : 24);
        if (tie && _slip)
            Slipped();
        return (false, !tie, 0);
    }

    /// <summary>A sniff or two, then step away with what it smelt (the worst of them).</summary>
    private (bool, bool, int) Sniff(SniffSession s)
    {
        if (s.Sniffs > _read)
        {
            _read = s.Sniffs;
            var bad = s.IsPot ? s.Smell == SniffSession.PotWord(1f) : s.Smell is "on the turn" or "off";
            if (_sniffed is not { } was || was.Target != s.Target || _read == 1 || bad)
                _sniffed = (s.Target, s.Smell);
        }
        if (s.Sniffs >= SniffsWanted)
            StopKitchenWork();
        else
            _act = true; // a breath between: the session waits it out
        return (false, false, 0);
    }
}

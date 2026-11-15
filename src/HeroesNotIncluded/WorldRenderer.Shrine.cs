using HeroesNotIncluded.Rendering;
using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Ecs;
using HeroesNotIncluded.Sim.Healing;
using Work = HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded;

/// <summary>
/// The empath's gift seen from outside: light drawn out of a patient into the healer's hands while
/// a wound comes across, the runes flaring over their hands as a mending's woven, a stillness
/// round them as they meditate. Cosmetic only, from the work in hand; never what it keeps hidden
/// (which wound, how bad, how the healer's holding up).
/// </summary>
public sealed partial class WorldRenderer
{
    /// <summary>Seconds between motes of a draw, of a sitting's stillness.</summary>
    private const float WispEvery = 0.08f, StillEvery = 0.35f;

    /// <summary>The gift's light: the brand's arcane violet, a little paler in the world.</summary>
    private static readonly float4 GiftLight = new(0.78f, 0.7f, 1f, 0.9f);

    private readonly Dictionary<int, float> _nextWisp = [];
    private readonly Dictionary<int, int> _runesSeen = [];

    private void DrawHealingLife(Simulation sim, Entity e, Work.WorkSession session)
    {
        var w = sim.World;
        var me = DrawPos(sim, e, w.Get<Position>(e).Pos);
        var hands = me + WorldScale.Rise(0.85f);
        switch (session)
        {
            case HandsOnSession { Linked: true } h when w.IsAlive(h.Patient) && w.TryGet<Position>(h.Patient, out var p):
                if (Due(e.Index, WispEvery))
                    _particles.Wisps(DrawPos(sim, h.Patient, p.Pos) + WorldScale.Rise(0.45f), hands, 1, GiftLight);
                break;
            case HandsOnSession { Held: true } h when w.IsAlive(h.Patient) && w.TryGet<Position>(h.Patient, out var p):
                // Pressing in: a faint glow gathers under the hands, now and then.
                if (Due(e.Index, WispEvery * 6))
                    _particles.Stillness(DrawPos(sim, h.Patient, p.Pos) + WorldScale.Rise(0.3f), GiftLight with { W = 0.5f });
                break;
            case CastSession c:
                if (!_runesSeen.TryGetValue(e.Index, out var seen) || seen > c.Woven.Count)
                    seen = 0;
                for (; seen < c.Woven.Count; seen++)
                    _particles.Rune(hands + WorldScale.Rise(0.2f), c.Palette[c.Woven[seen]].Icon, GiftLight); // alike, strong or weak: only its maker knows how well it went
                _runesSeen[e.Index] = seen;
                break;
            case MeditateSession:
                if (Due(e.Index, StillEvery))
                    _particles.Stillness(me + WorldScale.Rise(0.2f), GiftLight with { W = 0.7f });
                break;
        }
    }

    /// <summary>The next mote for this one is due (and is now booked).</summary>
    private bool Due(int key, float every)
    {
        if (_nextWisp.TryGetValue(key, out var next) && _anim.Clock < next)
            return false;
        _nextWisp[key] = _anim.Clock + every;
        return true;
    }
}

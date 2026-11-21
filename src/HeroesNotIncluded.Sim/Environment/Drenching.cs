using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Speech;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Environment;

/// <summary>
/// Caught by a bucket's water (<see cref="Buckets.Splash"/>): from when it lands on them till they've
/// dripped dry. Sim state, so everyone sees the same soaking at the same moment (and a replay does);
/// what it looks like (the drips, the wet look) is the renderer's. An AI villager or adventurer says
/// something about it when it lands (<see cref="DrenchSystem"/>); a player's character is left to its player.
/// </summary>
public struct Drenched
{
    /// <summary>When the water reaches them (it's in the air a moment after it's thrown), and when they're dry.</summary>
    public long Since, Until;

    /// <summary>They've reacted to this soaking already (or weren't going to).</summary>
    public bool Reacted;

    /// <summary>The water's reached them, and done what well water does (<see cref="Hospitality.Patience.Doused"/>).</summary>
    public bool Landed;
}

/// <summary>Who a splash soaks, and how long it takes to dry off.</summary>
public static class Drenching
{
    /// <summary>How long someone drips after a soaking.</summary>
    public const int DripTicks = SimClock.TicksPerSecond * 6;

    /// <summary>Soaked again this soon after the last, they don't make a fuss about it twice.</summary>
    public const int ReactGap = SimClock.TicksPerSecond * 4;

    /// <summary>
    /// How long the water's in the air from <paramref name="from"/> to <paramref name="at"/> (ticks): as the
    /// renderer draws its arc (WorldRenderer.DrawSplashes), roughly, by the cells it crosses.
    /// </summary>
    public static int LandsAfter(float3 from, int3 at)
    {
        var cells = Math.Max(Math.Abs(at.X + 0.5f - from.X), Math.Abs(at.Y + 0.5f - from.Y));
        return (int)((0.22f + 0.035f * cells) * SimClock.TicksPerSecond);
    }

    /// <summary>
    /// The splash at <paramref name="at"/> (thrown from <paramref name="from"/>) soaks whoever stands in
    /// its middle three cells on that level: people, not the dragon (it has its own <see cref="Ai.DragonBrain.Douse"/>).
    /// </summary>
    public static void Splashed(Simulation sim, int3 at, float3 from)
    {
        var w = sim.World;
        var lands = sim.Clock.Tick + LandsAfter(from, at);
        foreach (var e in sim.Creatures())
        {
            if (!w.TryGet<Position>(e, out var p) || Math.Abs(p.Cell.X - at.X) > 1 || Math.Abs(p.Cell.Y - at.Y) > 1 || p.Cell.Z != at.Z
                || w.Get<Creature>(e) is not { Dead: false, Def.Race: Race.Human })
                continue;
            var again = w.TryGet<Drenched>(e, out var was) && lands - was.Since < ReactGap;
            w.Add(e, new Drenched { Since = lands, Until = lands + DripTicks, Reacted = again || Players.Steered(sim, e) });
        }
    }
}

/// <summary>
/// Soakings landing: well water sobers a drunk and puts a fighter out of it (<see cref="Hospitality.Patience.Doused"/>,
/// who then has their own say), and an AI villager or adventurer splutters a line and laughs it off, or (one in
/// three) is cross about it. Picked by a hash, not the sim's dice. Dried off, the soaking's dropped.
/// </summary>
public sealed class DrenchSystem : ISimSystem
{
    public string Name => "Drench";
    public int Interval => 1;

    private readonly List<Entity> _dry = [];
    private readonly List<Entity> _landed = [];

    public void Update(Simulation sim)
    {
        var w = sim.World;
        var now = sim.Clock.Tick;
        _dry.Clear();
        _landed.Clear();
        foreach (var e in w.Each<Drenched>())
        {
            ref var d = ref w.Get<Drenched>(e);
            if (now >= d.Since && !d.Landed)
            {
                d.Landed = true;
                _landed.Add(e);
            }
        }
        // Outside the loop: settling a brawler ends jobs and moves components about.
        foreach (var e in _landed)
            if (Hospitality.Patience.Doused(sim, e) && w.Has<Drenched>(e))
                w.Get<Drenched>(e).Reacted = true;
        foreach (var e in w.Each<Drenched>())
        {
            ref var d = ref w.Get<Drenched>(e);
            if (now >= d.Until)
            {
                _dry.Add(e);
                continue;
            }
            if (d.Reacted || now < d.Since)
                continue;
            d.Reacted = true;
            if (!SpeechSystem.CanAct(sim, e))
                continue;
            var cross = SimRandom.Hash(e.Index, (int)d.Since, 0xD3E7) % 3 == 0;
            Barks.Say(sim, e, cross ? "soaked_cross" : "soaked_laugh", Volume.Say, cross ? "Oi! That's cold!" : "Ha! Brr!");
            Emotes.Show(sim, e, cross ? Emote.Angry : Emote.Laugh, null);
        }
        foreach (var e in _dry)
            w.Remove<Drenched>(e);
    }
}

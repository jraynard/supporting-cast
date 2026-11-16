using Narsil.Rendering;
using HeroesNotIncluded.Rendering;
using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Ecs;
using Work = HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded;

/// <summary>
/// The kitchen seen from the room: steam off the pot as its surface moves, flames when its fat
/// catches, the joint on the spit the colour of the crust turned up, the oven's mouth glowing and
/// smoking while there's bread in. All of it as the viewer sees it
/// (<see cref="Sim.Systems.PerceptionView.KitchenLook"/>): never what's in the pot or how done it is.
/// </summary>
public sealed partial class WorldRenderer
{
    private readonly Dictionary<int, float> _nextSteam = [];

    /// <summary>Seconds between puffs off a pot, by how it's moving: steaming, simmering, boiling.</summary>
    private static float SteamEveryFor(Work.Surface s) => s switch
    {
        Work.Surface.Steaming => 0.9f,
        Work.Surface.Simmer => 0.45f,
        Work.Surface.Boil => 0.18f,
        _ => float.MaxValue,
    };

    private void DrawKitchenLife(Simulation sim, Entity e, ThingDef def, float3 floor, in Thing thing, uint level)
    {
        if (def.Building is not ({ StewPot: not null } or { Spit: true } or { Oven: true }) || Sees(sim).KitchenLook(e) is not { } look)
            return;
        var height = WorldScale.HeightOf(def);
        if (def.Building.StewPot != null)
        {
            var rim = floor + new float3(0, 0, 0.55f * height);
            if (look.Flaring)
            {
                var size = (0.7f + 0.08f * MathF.Sin(_anim.Clock * 11f + e.Index)) * height;
                Billboard(S("fx/fire"), rim, size * 0.8f, size, Rgba.Pack(1f, 0.92f, 0.8f, 0.95f), level | Quad.Unlit | Quad.Glow, translucent: true);
            }
            else if (look.Surface != Work.Surface.Still && Due(_nextSteam, e.Index, SteamEveryFor(look.Surface)))
                _particles.Steam(rim, look.Surface == Work.Surface.Boil ? 0.8f : look.Surface == Work.Surface.Simmer ? 0.5f : 0.25f);
            return;
        }
        if (def.Building.Spit)
        {
            // The joint across the rod, the colour of the side turned up; fat spitting while it's over a fire.
            if (look.Crust is { } crust)
            {
                var rod = floor + new float3(0, 0, 0.5f * height);
                Billboard(S("ui/work/joint"), rod - new float3(0, 0, 0.12f), 0.46f, 0.22f, Rgba.Pack(KitchenLook.Crust(crust)), level);
                if (look.Lit && Due(_nextSteam, e.Index, 0.6f))
                    _particles.Embers(rod - new float3(0, 0, 0.2f), 1);
            }
            return;
        }
        // The oven: its mouth aglow while it's fired; smoke off it while there's bread in.
        var mouth = floor + FireOffset(def, thing.Facing);
        if (look.Lit)
        {
            var t = _anim.Clock * 3f + e.Index;
            Decal(S("props/coal"), mouth, 0.3f, 0.24f, Rgba.Pack(1f, 0.5f + 0.1f * MathF.Sin(t), 0.15f, 0.8f), level | Quad.Unlit | Quad.Glow, translucent: true, facing: thing.Facing);
        }
        if (look.Baking && Due(_nextSteam, e.Index, 1.2f))
            _particles.Wisps(mouth + new float3(0, 0, 0.3f), mouth + new float3(0, 0, 1.2f), 1, new float4(0.85f, 0.8f, 0.7f, 0.5f));
    }

    /// <summary>True (and the next one set) when a per-thing timer's run out.</summary>
    private bool Due(Dictionary<int, float> timers, int key, float every)
    {
        if (timers.TryGetValue(key, out var next) && _anim.Clock < next)
            return false;
        timers[key] = _anim.Clock + every;
        return true;
    }
}

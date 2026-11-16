using Narsil.Rendering;
using HeroesNotIncluded.Rendering;
using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Ecs;
using HeroesNotIncluded.Sim.Hospitality;
using Work = HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded;

/// <summary>
/// The inn's rooms and bar as anyone in them sees them: the keys hanging on the board, a slept-in
/// bed rumpled, a chamber pot not yet emptied, water in the basin once the jug's filled, ale running
/// from the cask while someone pours. All plain to look at; nothing left in a bed's sheets is drawn
/// until it's found.
/// </summary>
public sealed partial class WorldRenderer
{
    /// <summary>The wash tub's soapy water stands this high in its model (furniture.json WashTub).</summary>
    private const float TubSuds = 0.34f;

    /// <summary>Where the key board's hooks are on its model (furniture.json KeyBoard): three across, two rows.</summary>
    private static readonly float[] HookX = [0.23f, 0.5f, 0.77f], HookZ = [1.18f, 0.88f];

    private void DrawRoomLife(Simulation sim, Entity e, ThingDef def, float3 floor, in Thing thing, uint level)
    {
        var w = sim.World;
        var height = WorldScale.HeightOf(def);
        switch (def.Name)
        {
            case InnRooms.BoardDef:
            {
                // Each room's key on its own hook, by its number.
                foreach (var key in sim.StoredIn(e))
                {
                    var n = InnRooms.RoomOfKey(sim, key) - 1;
                    if (n < 0 || n >= 6)
                        continue;
                    var at = PropModel.RotatePoint(new float3(HookX[n % 3], 0.12f, HookZ[n / 3] * height), thing.Facing);
                    Billboard(S("items/key"), new float3(thing.Cell.X, thing.Cell.Y, thing.Cell.Z) + at - new float3(0, 0, 0.12f), 0.16f, 0.16f, Rgba.White, level);
                }
                return;
            }
            case RoomChores.PotDef when w.Has<Slops>(e):
                Decal(S("props/muck"), floor + new float3(0, 0, 0.17f * height), 0.2f, Rgba.Pack(0.75f, 0.68f, 0.45f, 0.9f), level);
                return;
            case RoomChores.StandDef when !w.Has<JugEmpty>(e):
                Decal(S("props/water"), floor + new float3(0, 0, 0.745f * height), 0.34f, Rgba.Pack(0.8f, 0.9f, 1f, 0.8f), level, translucent: true);
                return;
        }
        // A slept-in bed: last night's sheets heaped on it.
        if (def.Building is { IsBed: true } && w.Has<Unmade>(e))
        {
            var mid = Sim.Maps.Footprint.Centre(def, thing.Cell, thing.Facing);
            var top = def.Model is { } model ? PropModel.SurfaceAt(model, 0.5f, def.SizeY * 0.5f) * height : 0.3f;
            Billboard(S("items/soiled_linen"), new float3(mid.X, mid.Y, thing.Cell.Z + top), 0.42f, 0.32f, Rgba.White, level);
        }
    }

    /// <summary>Ale running from the cask into the tankard while someone holds the pour; suds thrown up off the wash tub while they scrub.</summary>
    private void DrawInnWorkLife(Simulation sim, Entity e, Work.WorkSession session)
    {
        if (session is Work.WashSession { Scrubbing: true } wash && sim.World.IsAlive(wash.Tub) && Due(_nextSteam, -e.Index - 1, 0.18f))
        {
            var tub = sim.World.Get<Thing>(wash.Tub);
            var water = new float3(tub.Cell.X + 0.5f, tub.Cell.Y + 0.5f, tub.Cell.Z) + new float3(0, 0, TubSuds * WorldScale.HeightOf(tub.Def));
            _particles.Dust(water, 2, 0.12f, new float4(0.94f, 0.96f, 1f, 0.9f), 0.18f);
            return;
        }
        if (session is not Work.PourSession { Held: true } pour || !sim.World.IsAlive(pour.Cask) || !Due(_nextSteam, -e.Index - 1, 0.05f))
            return;
        var t = sim.World.Get<Thing>(pour.Cask);
        var spout = new float3(t.Cell.X + 0.5f, t.Cell.Y + 0.5f, t.Cell.Z) + new float3(0, 0, 0.35f * WorldScale.HeightOf(t.Def));
        _particles.Dust(spout, 1, 0.03f, new float4(0.85f, 0.55f, 0.18f, 0.9f), 0.12f);
    }
}

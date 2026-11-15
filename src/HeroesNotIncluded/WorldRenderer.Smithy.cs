using Narsil.Rendering;
using HeroesNotIncluded.Rendering;
using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Ecs;
using Work = HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded;

/// <summary>
/// The smithy seen from outside: what a smith's work looks like to anyone watching (sparks off
/// the grindstone). Cosmetic only, drawn from what's in plain sight: the work being done, never
/// anything a session keeps hidden.
/// </summary>
public sealed partial class WorldRenderer
{
    /// <summary>The wheel's hub in its model (metalwork.json Grindstone), before the furniture squash.</summary>
    private static readonly float3 WheelHub = new(0.5f, 0.5f, 0.255f);

    /// <summary>How far out along the wheel's plane the blade meets the rim, and how far up from the hub.</summary>
    private const float WheelBite = 0.2f, WheelBiteRise = 0.1f;

    /// <summary>Seconds between bursts of sparks while someone bears down on the wheel.</summary>
    private const float SparkEvery = 0.07f;

    private readonly Dictionary<int, float> _nextSpark = [];

    /// <summary>
    /// A smith with a piece on the go has it in their tongs (heating, blooming, quenching, or carrying
    /// it between): the tongs in hand, the piece in their jaws the colour its heat shows, as anyone who
    /// can see them sees it (<see cref="Sim.Systems.PerceptionView.Piece"/>). At the anvil the
    /// hammer's in hand instead, and the piece lies on the anvil.
    /// </summary>
    private HumanLook.HeldItem? TongsOf(Simulation sim, Entity e, Work.WorkSession? session)
    {
        var w = sim.World;
        if (!w.Has<Work.WorkPiece>(e) || session is Work.HammerSession)
            return null;
        if ((Viewer ?? Sim.Systems.Perception.For(sim, Entity.Null)).Piece(e) is not { } piece)
            return null;
        var tongs = Sim.Systems.Crafting.ToolInHand(sim, e, "Tongs") is { IsNull: false } own ? w.Get<Thing>(own).Def : sim.Defs.Thing("Tongs");
        return HumanLook.HeldOf(tongs) is { } held ? held with { Piece = ForgeGlow.Colour(piece.Temp) } : null;
    }

    /// <summary>A held piece is drawn as it glows, not as the light falls on it; hot enough to show colour, it blooms.</summary>
    private static uint PieceFlags(uint color) => (color & 0xFF) > PieceGlowsAbove ? Quad.Unlit | Quad.Glow : Quad.Unlit;

    /// <summary>The red channel (0..255) past which a piece's colour is a glow, not cold iron's grey.</summary>
    private const uint PieceGlowsAbove = 84;

    /// <summary>The cosmetic life of someone's work at a smithy station, drawn as they're drawn.</summary>
    private void DrawWorkLife(Simulation sim, Entity e)
    {
        var w = sim.World;
        if (!w.TryGet<Work.WorkSession>(e, out var session))
            return;
        DrawHealingLife(sim, e, session);
        if (session is Work.SharpenSession { OnWheel: true, Held: true, Burned: false }
            && Sim.Work.Smithing.GrindstoneInReach(sim, e) is { IsNull: false } wheel)
            GrindSparks(sim, e, wheel);
        // The piece where it's being worked, the colour its heat shows to anyone who can see the smith.
        var (station, quench) = session switch
        {
            Work.HeatSession h => (h.Station, (Work.QuenchSession?)null),
            Work.BloomSession b => (b.Station, null),
            Work.HammerSession h => (h.Station, null),
            Work.QuenchSession q => (q.Station, q),
            _ => (Entity.Null, null),
        };
        if (station.IsNull || !w.IsAlive(station)
            || (Viewer ?? Sim.Systems.Perception.For(sim, Entity.Null)).Piece(e) is not { } piece)
            return;
        DrawPiece(sim, station, ForgeGlow.Colour(piece.Temp), quench?.Dunked == true, quench?.Steam ?? 0f, e.Index);
    }

    /// <summary>
    /// A piece at the station it's being worked at: across the anvil's face, down in the forge's coals,
    /// or under the quench tub's water (dark through it, steam rising as hot as it hisses).
    /// <paramref name="key"/> paces the steam per smith.
    /// </summary>
    private void DrawPiece(Simulation sim, Entity station, float4 glow, bool dunked, float steam, int key)
    {
        var w = sim.World;
        var at = w.Get<Thing>(station);
        var level = Quad.Level(at.Cell.Z);
        var floor = Position.CenterOf(at.Cell);
        var billet = S("ui/work/billet");
        var width = PieceLength * billet.PixelSize.Y / billet.PixelSize.X;
        var role = at.Def.Building?.Smithy;
        if (role == SmithyRole.Quench)
        {
            var surface = floor + new float3(0, 0, SurfaceUnder(sim.Map, w, at.Cell) * TubWater);
            if (dunked)
                Decal(billet, surface - new float3(0, 0, 0.005f), PieceLength, width,
                    Rgba.Pack(glow.X * 0.35f, glow.Y * 0.4f + 0.05f, glow.Z * 0.45f + 0.08f, 0.85f), level, translucent: true, facing: at.Facing);
            if (steam > 0f && !(_nextSpark.TryGetValue(key, out var due) && _anim.Clock < due))
            {
                _nextSpark[key] = _anim.Clock + SteamEvery;
                _particles.Steam(surface, steam);
            }
            return;
        }
        var color = Rgba.Pack(glow);
        // Across the anvil's face under the hammer; else down in the coals, along the fire.
        var where = role == SmithyRole.Anvil
            ? floor + new float3(0, 0, SurfaceUnder(sim.Map, w, at.Cell) + 0.01f)
            : floor + FireOffset(at.Def, at.Facing) - new float3(0, 0, 0.02f);
        Decal(billet, where, PieceLength, width, color, level | PieceFlags(color), facing: role == SmithyRole.Anvil ? at.Facing : at.Facing + 1);
    }

    /// <summary>
    /// A burning forge's coals glow under the flames, brighter while its bellows blow (<see cref="Blown"/>,
    /// which anyone near can hear and see). Only hand-worked forges: an oven's or hearth's fire is
    /// down in its mouth.
    /// </summary>
    private void DrawCoalBed(Simulation sim, Entity e, ThingDef def, float3 floor, int facing, uint level)
    {
        if (def.Building?.Workbench is not { HandWork: true } || def.FireAt == null)
            return;
        var blown = sim.World.TryGet<Blown>(e, out var b) && sim.Clock.Tick < b.Until;
        var t = _anim.Clock * (blown ? 5f : 2f) + e.Index;
        var heat = (blown ? 0.95f : 0.6f) + 0.06f * MathF.Sin(t) + 0.04f * MathF.Sin(t * 2.7f);
        var size = def.FireSize * CoalBedSize;
        Decal(S("props/coal"), floor + FireOffset(def, facing) - new float3(0, 0, CoalBedDrop * WorldScale.HeightOf(def)), size, size * 0.8f,
            Rgba.Pack(1f, 0.4f + 0.3f * heat, 0.1f, Math.Min(1f, 0.85f * heat)), level | Quad.Unlit | Quad.Glow, translucent: true, facing: facing);
    }

    /// <summary>The glowing bed's size against the fire's, and how far under the flames' foot it lies (model units).</summary>
    private const float CoalBedSize = 0.75f, CoalBedDrop = 0.03f;

    /// <summary>How long a billet's drawn in the world (cells), the art's 26 by 6 kept.</summary>
    private const float PieceLength = 0.32f;

    /// <summary>The quench tub's water stands this far up its staves (metalwork.json QuenchTub: 0.24 of 0.36).</summary>
    private const float TubWater = 0.24f / 0.36f;

    /// <summary>Seconds between puffs of steam off a quench.</summary>
    private const float SteamEvery = 0.12f;

    /// <summary>Sparks thrown off the rim where the blade bites, along the wheel's turn.</summary>
    private void GrindSparks(Simulation sim, Entity e, Entity wheel)
    {
        if (_nextSpark.TryGetValue(e.Index, out var next) && _anim.Clock < next)
            return;
        _nextSpark[e.Index] = _anim.Clock + SparkEvery;
        var t = sim.World.Get<Thing>(wheel);
        var lift = WorldScale.HeightOf(t.Def);
        var hub = new float3(t.Cell.X, t.Cell.Y, t.Cell.Z) + PropModel.RotatePoint(WheelHub * new float3(1, 1, lift), t.Facing);
        var along = PropModel.Rotate(new float3(0, 1, 0), t.Facing); // the wheel's plane runs along its model's Y
        // The blade's held to the side of the rim the smith stands on.
        var me = sim.World.Get<Position>(e).Pos;
        if ((me.X - hub.X) * along.X + (me.Y - hub.Y) * along.Y < 0)
            along = -along;
        _particles.Sparks(hub + along * WheelBite + new float3(0, 0, WheelBiteRise * lift), along, 3);
    }
}

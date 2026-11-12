using Brutal.ImGuiApi;
using SupportingCast.Sim.Speech;
using Narsil.Rendering;
using SupportingCast.Net;
using SupportingCast.Rendering;
using SupportingCast.Sim;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Ecs;
using SupportingCast.Sim.Maps;
using SupportingCast.Sim.Systems;

namespace SupportingCast;

public enum Overlay
{
    None,
    Temperature,
    Rooms,
    Light,
    Regions,
}

/// <summary>
/// Draws the simulation in 3D with the <see cref="QuadRenderer"/>: chunk meshes for blocks and
/// walls (<see cref="WorldMesher"/>), billboards for plants, items and creatures, decals for
/// floor things, zones, designations and overlays, and soft quads for fire. Everything above
/// <see cref="CutawayZ"/> is hidden (Timberborn's layer view). Night comes from the sim's light
/// grid, uploaded as a light volume. Text labels go through the ImGui background list.
/// Rendering only reads sim state. Creatures with a <see cref="SpriteSheet"/> are animated by
/// <see cref="CreatureAnimator"/> and drawn at their native pixel size (<see cref="PixelsPerCell"/>);
/// cosmetic dust, embers and Zzz come from <see cref="Particles"/>.
/// </summary>
public sealed class WorldRenderer(TextureAtlas atlas)
{
    public Overlay Overlay { get; set; }
    public bool ShowPaths { get; set; }
    public bool ShowNames { get; set; } = true;
    public bool ShowDarkness { get; set; } = true;

    /// <summary>Little speech bubbles over villagers showing what they're doing.</summary>
    public bool ShowBubbles { get; set; } = true;

    /// <summary>
    /// Cut the walls in front of indoor rooms down to stubs, as the camera faces
    /// (<see cref="WorldMesher.CutHeading"/>): every building's with the roofs hidden, else only
    /// <see cref="OpenBuilding"/>'s.
    /// </summary>
    public bool CutawayWalls { get; set; }

    private Entity _open = Entity.Null;
    private int _openLevel = int.MaxValue;
    private PresetBuilding? _openPlan;

    /// <summary>
    /// The building you're standing in, and your level in it: its roof and whatever of it is above
    /// you aren't drawn, and its near walls come down, so you can see yourself inside it; every
    /// other building keeps its roof. Entity.Null outside.
    /// </summary>
    public (Entity Building, int Level) OpenBuilding
    {
        get => (_open, _openLevel);
        set
        {
            (_open, _openLevel) = value;
            _openPlan = null;
        }
    }

    /// <summary>Is this column under the building you're in (its roof isn't drawn)?</summary>
    private bool UnderOpenRoof(Simulation sim, int3 c)
    {
        if (_open.IsNull || !sim.World.IsAlive(_open))
            return false;
        _openPlan ??= sim.World.Get<PresetBuilding>(_open);
        return Buildings.SymbolAt(_openPlan.Def, _openPlan.Anchor, _openPlan.Facing, new int3(c.X, c.Y, _openPlan.Anchor.Z)) != ' ';
    }

    private Simulation? _sim;

    /// <summary>Is this wall or door drawn cut down to a stub right now?</summary>
    public bool IsStub(Simulation sim, int3 c) => _mesher.IsStub(sim, c.X, c.Y, c.Z);

    /// <summary>Highest level drawn. Solid blocks cut at this level show their caps.</summary>
    public int CutawayZ { get; set; } = int.MaxValue;

    /// <summary>Roofs are cut away: they don't hide what's under them (<see cref="UnderDrawnFloor"/>).</summary>
    public bool HideRoofs { get; set; }

    /// <summary>
    /// A floor, roof or ground is drawn over this cell: what's in it is hidden, sprites, bubbles and
    /// labels alike. The depth test alone lets tall heads, bubbles and text show through.
    /// </summary>
    public bool UnderDrawnFloor(Map map, int3 c)
    {
        // In the building you're in, only what's drawn of it counts: nothing above your level.
        if (_sim != null && UnderOpenRoof(_sim, c))
            return c.Z > _openLevel || map.FloorAbove(c, Math.Min(CutawayZ, _openLevel), false);
        return map.FloorAbove(c, CutawayZ, !HideRoofs);
    }

    /// <summary>Whose eyes the world is drawn through; null draws everything (the title screen, the demos, no round).</summary>
    public PerceptionView? Viewer { get; set; }

    private bool Seen(Entity e) => Viewer == null || Viewer.CanSee(e);

    private bool SeenAt(float3 at) => Viewer == null
        || Viewer.CanSeeCell(new int3((int)MathF.Floor(at.X), (int)MathF.Floor(at.Y), (int)MathF.Floor(at.Z)));

    /// <summary>Art scale: a 32-pixel sprite spans one cell.</summary>
    public const float PixelsPerCell = 32f;

    private readonly Dictionary<string, Sprite> _spriteCache = [];
    private readonly WorldMesher _mesher = new(atlas);
    private readonly DoorAnimator _doors = new();
    private readonly CreatureAnimator _anim = new();
    private readonly Particles _particles = new();
    private long _lastSpeechTick = long.MaxValue;
    private readonly MaterialArt _materials = new(atlas);

    /// <summary>The sprite and tint a thing is drawn with, given what it's made of.</summary>
    public (Sprite Sprite, uint Tint) ArtOf(in Thing thing)
    {
        var (name, tint) = _materials.Resolve(thing.Def.Sprite, thing.Material);
        return (S(name), tint);
    }
    private int _atlasVersion;

    public CreatureAnimator Animator => _anim;

    /// <summary>Draws creatures between ticks instead of at their latest tick (set by the game; null draws raw positions).</summary>
    public Interpolation? Interp { get; set; }

    /// <summary>The local co-op player's predicted walk, drawn for <see cref="PredictedEntity"/> instead of the sim's position.</summary>
    public AvatarPredictor? Predicted { get; set; }

    public Entity PredictedEntity { get; set; } = Entity.Null;

    /// <summary>Where a creature is drawn this frame: its sim position, smoothed between ticks (or predicted, for the local co-op player).</summary>
    public float3 DrawPos(Simulation sim, Entity e, float3 simPos)
    {
        var p = Interp?.Smoothed(sim, e, simPos) ?? simPos;
        p = Predicted != null && e == PredictedEntity ? Predicted.DrawPos(p) : p;
        return p + new float3(0, 0, PerchLift(sim, e, p));
    }

    /// <summary>
    /// How far up someone on furniture stands (<see cref="Perched"/>): its top in its cell, easing
    /// up and down over the step on and off, so the climb reads as one.
    /// </summary>
    private static float PerchLift(Simulation sim, Entity e, float3 p)
    {
        if (!sim.World.TryGet<Perched>(e, out var perched))
            return 0f;
        var top = Position.CenterOf(perched.Cell);
        var d = MathF.Max(MathF.Abs(p.X - top.X), MathF.Abs(p.Y - top.Y));
        return SurfaceUnder(sim.Map, sim.World, perched.Cell) * Math.Clamp(1f - d, 0f, 1f);
    }

    /// <summary>World-anchored text that rises and fades ("Built!", "Hit"), drawn with the labels.</summary>
    private sealed class FloatingText
    {
        public float3 Pos;
        public string Text = "";
        public float4 Color;
        public float Born;
    }

    private readonly List<FloatingText> _floats = [];
    private float _now;

    /// <summary>Construction sites seen last frame, with how far along they were: one that vanishes when nearly done was built.</summary>
    private Dictionary<Entity, (int3 Cell, string Label, float Done)> _sites = [], _sitesNow = [];

    public void Float(float3 pos, string text, float4 color) =>
        _floats.Add(new FloatingText { Pos = pos, Text = text, Color = color, Born = _now });
    public Particles Particles => _particles;
    private Camera3D _cam = null!;
    private QuadRenderer _quads = null!;

    private byte[] _light = [];
    private (int Lighting, int Structure) _lightBuilt = (-1, -1);
    private int _lightSky = -1;
    private readonly (int, int)[] _lightUploaded = new (int, int)[Renderer.MaxFramesInFlight];

    public int ChunksRebuiltLastFrame => _mesher.ChunksRebuiltLastFrame;

    public Sprite Sprite(string name) => S(name);

    private Sprite S(string name)
    {
        if (_atlasVersion != atlas.Version)
        {
            // Art was hot-reloaded: every cached UV is stale.
            _atlasVersion = atlas.Version;
            _spriteCache.Clear();
            _mesher.Invalidate();
        }
        if (!_spriteCache.TryGetValue(name, out var s))
            _spriteCache[name] = s = atlas.Get(name);
        return s;
    }

    /// <summary>
    /// What a column shows from above with the cutaway applied: the solid block cut at the
    /// cutaway if there is one, else the first standable cell below it.
    /// </summary>
    public int3 VisibleCell(Map map, int x, int y) => map.VisibleCell(x, y, CutawayZ);

    /// <summary>Height of the floor (or cut surface) a column shows.</summary>
    public float SurfaceHeight(Map map, int3 visible) => map.IsSolid(map.Idx(visible)) ? visible.Z + 1 : visible.Z;

    /// <summary>
    /// Refresh this frame's copy of the light volume if the sim's light grid changed since that copy
    /// was last uploaded. Open sky reads as sky light; solid ground as well, so faces never sample black.
    /// </summary>
    public void UpdateLight(QuadRenderer quads, in FrameContext frame, Simulation sim)
    {
        var map = sim.Map;
        var version = (sim.Lighting.Version, map.StructureVersion);
        if (_lightUploaded[frame.FrameSlot] == version)
            return;
        if (_lightBuilt != version)
        {
            if (_light.Length != map.Count)
            {
                _light = new byte[map.Count];
                _lightSky = -1;
            }
            var sky = (byte)(sim.Lighting.SkyLight(sim) * 255f);
            // Cells outside the active set only ever hold the sky value, so they need rewriting
            // only when that or the active set (structure) changes, not on every lighting update.
            if (sky != _lightSky || map.StructureVersion != _lightBuilt.Structure)
            {
                Array.Fill(_light, sky);
                _lightSky = sky;
            }
            foreach (var i in map.ActiveCells)
                _light[i] = (byte)(Math.Clamp(map.Light[i], 0f, 1f) * 255f);
            _lightBuilt = version;
        }
        quads.UpdateLight(frame, _light);
        _lightUploaded[frame.FrameSlot] = version;
    }

    private static (int X, int Y, int Z) Unpack(int3 c) => (c.X, c.Y, c.Z);

    /// <summary>The sheet a creature is drawn with, if its sprite is animated.</summary>
    public SpriteSheet? SheetOf(Simulation sim, Entity e)
    {
        var c = sim.World.Get<Creature>(e);
        // People are drawn in layers from their look (HumanLook); this is the base layer.
        if (c.Def.Race == Race.Human && HumanLook.Sheet(atlas, HumanLook.Of(sim, e)) is { } look)
            return look;
        var name = SpriteNameOf(sim, e);
        return atlas.TryGetSheet(name, out var sheet) ? sheet : null;
    }

    /// <summary>The creature's sprite for its life stage (the plain sheet's name; layered looks add suffixes).</summary>
    private static string SpriteNameOf(Simulation sim, Entity e)
    {
        var c = sim.World.Get<Creature>(e);
        var stage = c.Stage(sim.Clock.Tick);
        return string.IsNullOrEmpty(stage.Sprite) ? c.Def.Sprite : stage.Sprite;
    }

    /// <summary>Height of a creature's sprite above its feet, in cells (labels, bubbles).</summary>
    public float VisualHeight(Simulation sim, Entity e) =>
        SheetOf(sim, e) is { } sheet ? sheet.Pivot.Y / (PixelsPerCell * sheet.Density) * 0.95f : sim.World.Get<Creature>(e).Stage(sim.Clock.Tick).Size;

    /// <param name="time">Real seconds, for flicker that should never stop.</param>
    /// <param name="animDt">Animation seconds since last frame: real time scaled by game speed (0 when paused).</param>
    public void Draw(QuadRenderer quads, Camera3D cam, Simulation sim, Entity selected, float time, float animDt)
    {
        _cam = cam;
        _quads = quads;
        _anim.Advance(animDt);
        _now = time;
        S(TextureAtlas.White); // picks up an atlas reload before the mesher runs
        var map = sim.Map;
        var w = sim.World;
        var cut = Math.Min(CutawayZ, map.Depth - 1);

        _sim = sim;
        _mesher.OpenBuilding = HideRoofs ? (Entity.Null, int.MaxValue) : (_open, _openLevel);
        _mesher.CutHeading = CutawayWalls || (!_open.IsNull && !HideRoofs) ? WorldMesher.SnapHeading(cam.Heading) : null;
        _doors.HeightOf = c => _mesher.IsStub(sim, c.X, c.Y, c.Z) ? WorldMesher.StubHeight : 1f;
        _doors.CanSee = Viewer == null ? null : Viewer.CanSeeCell;
        _mesher.Draw(quads, sim);

        // Horizontal culling box: what's visible at the bottom and top of the map, plus a margin.
        var (a0, a1) = cam.VisibleBounds(0);
        var (b0, b1) = cam.VisibleBounds(map.Depth);
        var min = float2.Min(a0, b0);
        var max = float2.Max(a1, b1);
        int x0 = Math.Max(0, (int)min.X - 2), y0 = Math.Max(0, (int)min.Y - 2);
        int x1 = Math.Min(map.Width - 1, (int)max.X + 2), y1 = Math.Min(map.Height - 1, (int)max.Y + 2);
        bool InView(int3 c) => c.X >= x0 && c.X <= x1 && c.Y >= y0 && c.Y <= y1 && c.Z <= cut && !UnderDrawnFloor(map, c);

        // Structures that aren't meshed, construction ghosts and items.
        foreach (var e in w.Each<Thing>())
        {
            var thing = w.Get<Thing>(e);
            if (!thing.Spawned || !InView(thing.Cell) || !Seen(e))
                continue;
            DrawThing(sim, e, thing);
        }
        foreach (var e in w.Each<PresetBuilding>())
            DrawRaising(sim, e, InView);

        // Shop signs, hung out over the street from their front walls (not while the wall's cut down).
        foreach (var e in w.Each<PresetBuilding>())
            // It hangs outside the wall, so the storey above doesn't hide it, as it would something in the cell.
            if (Sim.Shops.Signs.CellOf(sim, e) is { } signAt && signAt.X >= x0 && signAt.X <= x1 && signAt.Y >= y0 && signAt.Y <= y1 && signAt.Z <= cut
                && Sim.Shops.Signs.At(sim, signAt) == e && !_mesher.IsStub(sim, signAt.X, signAt.Y, signAt.Z))
                AddModel(SignModel, signAt, w.Get<PresetBuilding>(e).Facing, null, Rgba.White, Quad.Level(signAt.Z));

        // Mess on the floor, as the viewer sees it.
        foreach (var m in (Viewer ?? Perception.For(sim, Entity.Null)).Messes())
            if (InView(m.Cell))
                Decal(S(MessSprite(m.Look)), new float3(m.Cell.X + 0.5f, m.Cell.Y + 0.5f, m.Cell.Z + 0.02f), 0.6f + 0.35f * m.Amount,
                    Rgba.Pack(1f, 1f, 1f, 0.4f + 0.6f * m.Amount), Quad.Level(m.Cell.Z), translucent: true);

        _doors.Draw(quads, sim, InView, animDt, t => ArtOf(t), (at, level) => Billboard(S("props/padlock"), at - new float3(0, 0, 0.1f), 0.22f, 0.22f, Rgba.White, level));

        foreach (var (site, info) in _sites)
            if (!_sitesNow.ContainsKey(site) && !w.IsAlive(site) && info.Done > 0.97f)
                Float(Position.CenterOf(info.Cell) + new float3(0, 0, 1.2f), $"Built {info.Label}", new float4(0.6f, 0.95f, 0.47f, 1f));
        (_sites, _sitesNow) = (_sitesNow, _sites);
        _sitesNow.Clear();

        DrawWaterGlints(map, x0, y0, x1, y1, cut, time);

        // Zones and designations in view, at any level up to the cutaway. The map indexes marked
        // cells, so this visits only those rather than every visible column at every level.
        foreach (var i in map.ZonedCells)
        {
            var (x, y, z) = Unpack(map.CellOf(i));
            if (x < x0 || x > x1 || y < y0 || y > y1 || z < 1 || z > cut)
                continue;
            Decal(S(TextureAtlas.White), new float3(x + 0.5f, y + 0.5f, z), 1f, Rgba.Pack(0.3f, 0.7f, 1f, 0.22f), Quad.Level(z), translucent: true);
        }
        foreach (var i in map.AnyDesignated)
        {
            var (x, y, z) = Unpack(map.CellOf(i));
            if (x < x0 || x > x1 || y < y0 || y > y1 || z < 1 || z > cut)
                continue;
            {
                // Dig marks sit on top of the block; others on the floor of the cell.
                var floor = map.IsSolid(i) ? z + 1 : z;
                var d = map.Designations[i];
                var mark = d.HasFlag(Designation.Burn) ? Rgba.Pack(1f, 0.35f, 0.1f, 0.6f)
                    : d == Designation.Fish ? Rgba.Pack(0.35f, 0.7f, 1f, 0.55f)
                    : Rgba.Pack(1f, 0.85f, 0.2f, 0.55f);
                Decal(S("ui/designate_x"), new float3(x + 0.5f, y + 0.5f, floor + 0.03f), 0.5f, mark, Quad.Level(z) | Quad.Unlit, translucent: true);
                // An icon of the order floats over the mark, bobbing gently.
                var bob = MathF.Sin(time * 2.2f + (x * 7 + y * 13) * 0.37f) * 0.05f;
                Billboard(S(DesignationIcon(d)), new float3(x + 0.5f, y + 0.5f, floor + 0.22f + bob), 0.4f, 0.4f, Rgba.White, Quad.Level(z) | Quad.Unlit);
            }
        }

        // Creatures
        foreach (var e in w.Each<Creature>())
        {
            var p = w.Get<Position>(e);
            if (!InView(p.Cell) || !Seen(e))
                continue;
            p.Pos = DrawPos(sim, e, p.Pos); // a copy: the sim's Position is untouched
            var c = w.Get<Creature>(e);
            var stage = c.Stage(sim.Clock.Tick);
            var spriteName = string.IsNullOrEmpty(stage.Sprite) ? c.Def.Sprite : stage.Sprite;
            var sprite = S(spriteName);
            var size = stage.Size;
            var mind = w.Get<Mind>(e);
            var level = Quad.Level(p.Cell.Z);
            var tint = c.Faction switch
            {
                Faction.Raider => Rgba.Pack(1f, 0.72f, 0.68f),
                Faction.Prisoner => Rgba.Pack(1f, 0.88f, 0.6f),
                _ => Rgba.White,
            };
            if (SheetOf(sim, e) is { } sheet)
            {
                DrawAnimated(sim, e, sheet, p, tint, level);
            }
            else if (w.Has<Carried>(e))
            {
                // Slung over the carrier's shoulder.
                Decal(sprite, p.Pos + new float3(0, 0, 0.75f), size * 0.8f, tint, level);
            }
            else if (c.Dead || c.Downed || mind.Asleep)
            {
                // Lying down.
                Decal(sprite, p.Pos, size, c.Dead ? Rgba.Pack(0.5f, 0.5f, 0.5f, 0.9f) : c.Downed ? Rgba.Pack(0.85f, 0.7f, 0.7f) : tint, level);
            }
            else if (map.InBounds(p.Cell) && !map.Standable(map.Idx(p.Cell)) && !map.IsSolid(map.Idx(p.Cell)))
            {
                // Airborne: hover gently, and cast a shadow on whatever is below so height reads in
                // an orthographic view.
                var hover = MathF.Sin(time * 3f + e.Index) * 0.08f;
                Billboard(sprite, p.Pos + WorldScale.Rise(hover), size, size, tint, level);
                var ground = p.Cell.Z;
                while (ground > 0 && !map.Standable(map.Idx(p.Cell.X, p.Cell.Y, ground)) && !map.IsSolid(map.Idx(p.Cell.X, p.Cell.Y, ground - 1)))
                    ground--;
                var below = new float3(p.Pos.X, p.Pos.Y, ground + 0.03f);
                var fade = Math.Clamp(1f - (p.Pos.Z - ground) / 10f, 0.3f, 1f);
                Decal(S("fx/smoke"), below, size * 0.8f, Rgba.Pack(0, 0, 0, 0.45f * fade), Quad.Level(ground), translucent: true);
            }
            else
            {
                var bob = w.Get<Mover>(e).Moving ? MathF.Abs(MathF.Sin(time * 10f + e.Index)) * 0.06f : 0f;
                Billboard(sprite, p.Pos + WorldScale.Rise(bob), size, size, tint, level);
            }
            if (w.TryGet<Equipment>(e, out var eq) && w.IsAlive(eq.Weapon) && !c.Dead && !c.Downed && (c.Def.Race != Race.Human || w.Get<Thing>(eq.Weapon).Def.Held == null))
                Billboard(S(w.Get<Thing>(eq.Weapon).Def.Sprite), p.Pos - cam.Right * 0.3f * size + WorldScale.Rise(size * 0.25f), 0.45f, 0.45f, Rgba.White, level);
            var carried = w.Get<Carrying>(e).Item;
            if (!carried.IsNull && w.IsAlive(carried))
                Billboard(S(w.Get<Thing>(carried).Def.Sprite), p.Pos + cam.Right * 0.3f + WorldScale.Rise(size * 0.45f), 0.5f, 0.5f, Rgba.White, level);
            if (ShowBubbles && c.Faction == Faction.Village && !c.Dead && !w.Has<Carried>(e) && BubbleIcon(sim, e) is { } icon)
                DrawBubble(icon, p.Pos + WorldScale.Rise(VisualHeight(sim, e) + 0.2f), level, time + e.Index);
            else if (w.Has<HuntOrder>(e) && !c.Dead)
                DrawBubble("ui/icon_hunt", p.Pos + WorldScale.Rise(VisualHeight(sim, e) + 0.2f), level, time + e.Index);
        }

        // Fire: two flickering flames per burning cell, offset a little so a burning area looks full.
        var fireSprite = S("fx/fire");
        foreach (var i in sim.Fire.BurningTiles)
        {
            var t = map.CellOf(i);
            if (!InView(t))
                continue;
            var f = map.Fire[i];
            for (var k = 0; k < 2; k++)
            {
                var h = SimRandom.Hash(i, k, 5);
                var jitter = new float3((h & 255) / 255f - 0.5f, (h >> 8 & 255) / 255f - 0.5f, 0) * 0.5f;
                var flicker = 0.8f + 0.2f * MathF.Sin(time * (11f + k * 4f) + i * 1.7f);
                var s = (0.45f + 0.6f * f) * flicker * (k == 0 ? 1f : 0.7f);
                Billboard(fireSprite, new float3(t.X + 0.5f, t.Y + 0.5f, t.Z) + jitter, s, s * 1.25f, Rgba.Pack(1, 1, 1, 0.9f), Quad.Level(t.Z) | Quad.Unlit | Quad.Glow, translucent: true);
            }
        }

        DrawSmoke(sim, InView, time);
        DrawBreaths(sim, time);
        DrawShots(sim, time);
        EmitSpeech(sim, InView);
        _particles.Update(animDt);
        _particles.Draw(this, cut);

        DrawOverlay(sim, x0, y0, x1, y1);

        if (ShowPaths && Viewer == null)
            foreach (var e in sim.Creatures())
            {
                var mover = w.Get<Mover>(e);
                if (!mover.Moving)
                    continue;
                var prev = w.Get<Position>(e).Pos;
                for (var k = mover.PathIndex; k < mover.Path!.Count; k++)
                {
                    var next = Position.CenterOf(mover.Path[k]);
                    Line(prev, next, 0.07f, Rgba.Pack(1f, 1f, 1f, 0.6f));
                    prev = next;
                }
            }

        if (w.IsAlive(selected))
        {
            var t = sim.LocationOf(selected);
            var center = w.TryGet<Position>(selected, out var p) ? DrawPos(sim, selected, p.Pos) : Position.CenterOf(t);
            if (map.InBounds(t) && map.IsSolid(map.Idx(t)))
                center.Z += 1;
            // Sized to the creature's sprite, breathing gently so it reads as "live".
            var ring = w.Has<Creature>(selected) && SheetOf(sim, selected) is { } sheet ? Math.Max(1.15f, sheet.FrameSize.X / (PixelsPerCell * sheet.Density) * 0.8f) : 1.15f;
            ring *= 1f + 0.05f * MathF.Sin(time * 4f);
            Decal(S("ui/selection"), center + new float3(0, 0, 0.04f), ring, Rgba.Pack(1f, 0.85f, 0.35f), Quad.Unlit | Quad.Always, translucent: true);
        }
    }

    /// <summary>
    /// A hanging shop sign, in its wall cell with the street to the front (+Y): an iron arm out from
    /// the wall's top, and the board hung from it edge-on to the wall, painted both sides.
    /// </summary>
    private static readonly List<ModelBox> SignModel =
    [
        new() { Min = [0.47f, 0.92f, 0.86f], Max = [0.53f, 1.86f, 0.9f], Top = "props/stone_block", Side = "props/stone_block" },
        new() { Min = [0.48f, 1.04f, 0.8f], Max = [0.52f, 1.08f, 0.86f], Top = "props/stone_block", Side = "props/stone_block" },
        new() { Min = [0.48f, 1.72f, 0.8f], Max = [0.52f, 1.76f, 0.86f], Top = "props/stone_block", Side = "props/stone_block" },
        new() { Min = [0.475f, 1.0f, 0.28f], Max = [0.525f, 1.8f, 0.8f], Top = "props/planks", Side = "props/sign_board", Front = "props/planks" },
    ];

    private static string MessSprite(MessLook look) => look switch
    {
        MessLook.Puddle => "ui/mess_puddle",
        MessLook.Mud => "ui/mess_mud",
        MessLook.Crumbs => "ui/mess_crumbs",
        _ => "ui/mess_stain",
    };

    private static readonly float4 Dirt = new(0.62f, 0.52f, 0.4f, 0.55f);

    /// <summary>A sleeper's scale in a bed: the ~40 px figure (people sheets are 56 px tall) at full size would put the head inside the headboard.</summary>
    private const float SleeperInBed = 0.75f;

    /// <summary>The same in a bed two cells long, which has room for the whole figure.</summary>
    private const float SleeperInLongBed = 0.95f;

    /// <summary>How far toward the bed's foot the blanket decal sits, so it stops at the sleeper's chin.</summary>
    private const float BlanketShift = 0.06f;

    /// <summary>
    /// A creature with an animated sheet: pick its pose, then draw it upright (billboard, flipped to
    /// its facing) or, for humans asleep/downed, flat on the ground or tucked into bed. Also emits
    /// the cosmetic side effects of the pose: dust on takeoff, landing and hammer strikes, Zzz, the
    /// glow building in a dragon's mouth.
    /// </summary>
    private void DrawAnimated(Simulation sim, Entity e, SpriteSheet sheet, in Position p, uint tint, uint level, bool seated = false)
    {
        var w = sim.World;
        var map = sim.Map;
        var c = w.Get<Creature>(e);
        var mind = w.Get<Mind>(e);
        var pose = _anim.Pose(sim, e, sheet, _cam);
        var color = c.Dead ? Rgba.Pack(0.5f, 0.5f, 0.5f, 0.9f) : c.Downed ? Rgba.Pack(0.85f, 0.7f, 0.7f) : tint;
        // Humans are drawn lying flat (top-down art); dragons and animals lie down in their side view.
        var flat = c.Def.Race == Race.Human;
        var lying = pose.Tag is "sleep" or "downed";

        if (w.Has<Carried>(e))
        {
            var f = (CreatureAnimator.Resolve(sheet, "downed", out _)?.Frames[0]) ?? pose.Frame;
            FrameDecal(sheet, f, p.Pos + new float3(0, 0, 0.75f), 0.8f, color, level);
            var k = 0;
            foreach (var (layer, layerColor) in LookLayers(sim, e, color))
                if (HumanLook.LayerFrame(sheet, layer, f) is { } lf)
                    FrameDecal(layer, lf, p.Pos + new float3(0, 0, 0.75f + ++k * LayerStep), 0.8f, layerColor, level);
            return;
        }

        if (lying && flat)
        {
            var center = p.Pos;
            var bed = mind.Asleep && mind.Job is { } job && !job.Focus.IsNull && w.IsAlive(job.Focus) && w.Has<Thing>(job.Focus)
                && w.Get<Thing>(job.Focus).Cell == p.Cell ? job.Focus : Entity.Null;
            var facing = 0;
            var scale = 1f;
            if (!bed.IsNull)
            {
                // In a 3D bed: a little smaller so they fit between headboard and foot, and lying on
                // top of the pillow as well as the mattress, or the pillow hides their head.
                // A long bed (1x2) fits them at full size, laid along its middle.
                var bedThing = w.Get<Thing>(bed);
                var length = bedThing.Def.SizeY;
                var mid = Sim.Maps.Footprint.Centre(bedThing.Def, bedThing.Cell, bedThing.Facing);
                center = new float3(mid.X, mid.Y, Position.CenterOf(p.Cell).Z);
                if (bedThing.Def.Model is { } bedModel)
                    center.Z += Math.Max(PropModel.SurfaceAt(bedModel, 0.5f, length * 0.5f), PropModel.SurfaceAt(bedModel, 0.5f, 0.2f))
                        * WorldScale.HeightOf(bedThing.Def);
                facing = bedThing.Facing;
                scale = length > 1 ? SleeperInLongBed : SleeperInBed;
            }
            var lyingColor = bed.IsNull && mind.Asleep ? Rgba.Pack(0.85f, 0.85f, 0.85f) : color;
            FrameDecal(sheet, pose.Frame, center + new float3(0, 0, 0.02f), scale, lyingColor, level, facing);
            var k = 0;
            foreach (var (layer, layerColor) in LookLayers(sim, e, lyingColor))
                if (HumanLook.LayerFrame(sheet, layer, pose.Frame) is { } lf)
                    FrameDecal(layer, lf, center + new float3(0, 0, 0.02f + ++k * LayerStep), scale, layerColor, level, facing);
            if (!bed.IsNull)
            {
                // Pulled up to the chin: nudged toward the foot so the face stays clear.
                var blanket = center + PropModel.Rotate(new float3(0, BlanketShift, 0.03f), facing);
                Decal(S("buildings/bed_blanket"), blanket, w.Get<Thing>(bed).Def.RenderSize, Rgba.White, level, facing: facing);
            }
            if (mind.Asleep && _anim.Every(e, 1.6f))
                _particles.Zzz(center + new float3(0.15f, -0.15f, 0.35f));
            return;
        }

        var airborne = !seated && (pose.Tag is "fly" or "glide" or "takeoff" or "land"
            || (map.InBounds(p.Cell) && !map.Standable(map.Idx(p.Cell)) && !map.IsSolid(map.Idx(p.Cell))));
        var feet = p.Pos;
        // Astride, the hips sit on the saddle (the frame's pivot is the feet, which hang lower).
        if (seated && pose.Tag == "ride")
            feet -= _cam.Up * (sheet.FrameSize.Y * RideSeat / (PixelsPerCell * sheet.Density));
        if (pose.Tag == "glide")
            feet.Z += MathF.Sin(_anim.Clock * 2f + e.Index) * 0.05f;
        if (!airborne && !c.Dead)
        {
            // A soft contact shadow grounds standing figures (part of the diorama look).
            var shadowSize = Math.Clamp(sheet.FrameSize.X / (PixelsPerCell * sheet.Density) * 0.55f, 0.35f, 1.6f);
            Decal(S("fx/smoke"), new float3(feet.X, feet.Y, GroundBelow(map, p.Cell) + 0.02f), shadowSize, Rgba.Pack(0, 0, 0, 0.32f), level, translucent: true);
        }
        BillboardFrame(sheet, pose.Frame, feet, pose.Facing, pose.Roll, color, level, aboutCenter: airborne);
        var nudge = 0;
        foreach (var (layer, layerColor) in LookLayers(sim, e, color))
            if (HumanLook.LayerFrame(sheet, layer, pose.Frame) is { } lf)
                BillboardFrame(layer, lf, feet - _cam.Forward * (++nudge * LayerStep), pose.Facing, pose.Roll, layerColor, level, aboutCenter: airborne);

        if (sheet.Points.TryGetValue("mouth", out var m))
        {
            var mouth = feet + _cam.Right * ((m.X - sheet.Pivot.X) / (PixelsPerCell * sheet.Density) * pose.Facing) + _cam.Up * ((sheet.Pivot.Y - m.Y) / (PixelsPerCell * sheet.Density));
            _anim.SetMouth(e, mouth);
            if (pose.Tag == "breathe_windup")
            {
                var pulse = 0.5f + 0.5f * MathF.Sin(_anim.Clock * 9f);
                var glow = (0.18f + 0.12f * pulse) * sheet.FrameSize.X / 96f + 0.08f;
                var flame = FireColor;
                var alpha = 0.55f + 0.35f * pulse;
                Billboard(S("fx/fire"), mouth - WorldScale.Rise(glow * 0.5f), glow, glow, Rgba.Pack(1f, 0.8f, 0.5f, alpha), level | Quad.Unlit | Quad.Glow, translucent: true);
                if (CoreSprite(0) is { } core)
                {
                    var heart = glow * 0.5f;
                    Billboard(core, mouth - WorldScale.Rise(glow * 0.5f), heart, heart, Rgba.Pack(flame.X, flame.Y, flame.Z, alpha), level | Quad.Unlit | Quad.Glow, translucent: true);
                }
                if (_anim.Every(e, 0.35f))
                    _particles.Embers(mouth, 1);
            }
        }

        if (lying && mind.Asleep && _anim.Every(e, 1.6f))
            _particles.Zzz(feet + _cam.Up * (sheet.Pivot.Y * 0.5f / (PixelsPerCell * sheet.Density)) + _cam.Right * (0.3f * pose.Facing));

        var width = sheet.FrameSize.X / (PixelsPerCell * sheet.Density);
        if (pose.TagStarted && pose.Tag is "takeoff" or "land")
            _particles.Dust(new float3(feet.X, feet.Y, GroundBelow(map, p.Cell)), 10, width * 0.6f, Dirt, 0.25f + width * 0.15f);

        if (pose.EventFired && pose.Tag == "hatch")
        {
            var at = feet + new float3(0, 0, 0.35f);
            _particles.Shells(at, 12);
            _particles.Embers(at, 6);
        }

        if (pose.EventFired && pose.Tag == "work" && mind.Job is { } work)
        {
            float3? at = !work.Focus.IsNull && w.IsAlive(work.Focus) ? Position.CenterOf(sim.LocationOf(work.Focus))
                : work.FocusCell is { } fc ? Position.CenterOf(fc) : null;
            if (at is { } site)
                _particles.Dust(site + (feet - site) * 0.35f + new float3(0, 0, 0.3f), 3, 0.5f, Dirt, 0.3f);
        }

        if (airborne)
        {
            var ground = GroundBelow(map, p.Cell);
            var height = p.Pos.Z - ground;
            var fade = Math.Clamp(1f - height / 10f, 0.3f, 1f);
            var shadow = width * 0.55f * Math.Clamp(1f - height * 0.04f, 0.6f, 1f);
            Decal(S("fx/smoke"), new float3(p.Pos.X, p.Pos.Y, ground + 0.03f), shadow, Rgba.Pack(0, 0, 0, 0.45f * fade), Quad.Level((int)ground), translucent: true);
        }
    }

    /// <summary>
    /// A person's tinted skin, eye and hair layers, or a dragon's scale, belly, wing, horn and eye
    /// layers (none for anyone else). Layers share no pixels, so drawing them coplanar is safe.
    /// </summary>
    /// <summary>
    /// How far each look layer sits in front of the one before (cells): a figure's layers share its
    /// depth, so without a fixed order they z-fight in stripes. Tiny next to anything they stand by.
    /// </summary>
    private const float LayerStep = 0.002f;

    /// <summary>
    /// How far the hips sit above the feet in a person's ride frames, as a share of the frame's
    /// height (tools/chibi_people.py: hips at 42 of 60 design units, the bent legs end at 54).
    /// </summary>
    private const float RideSeat = 0.2f;

    public IEnumerable<(SpriteSheet Sheet, uint Color)> LookLayers(Simulation sim, Entity e, uint color) =>
        sim.World.Get<Creature>(e).Def.Race switch
        {
            Race.Human => HumanLook.Layers(atlas, HumanLook.Of(sim, e), color, HumanLook.WornOf(sim, e), HeldOf(sim, e)),
            _ => [],
        };

    /// <summary>
    /// What a person has in hand: the tool their job works with (the axe while felling, the pick
    /// while mining), else their weapon; nothing when they're down.
    /// </summary>
    public static HumanLook.HeldItem? HeldOf(Simulation sim, Entity e)
    {
        var w = sim.World;
        var c = w.Get<Creature>(e);
        if (c.Dead || c.Downed)
            return null;
        if (w.TryGet<Mind>(e, out var mind) && mind.Job?.ToolKind is { } kind && Sim.Systems.Crafting.ToolInHand(sim, e, kind) is { IsNull: false } tool
            && HumanLook.HeldOf(w.Get<Thing>(tool).Def) is { } t)
            return t;
        // A player at the anvil has their hammer out; scrubbing the floor, their mop.
        if (w.TryGet<Sim.Work.WorkSession>(e, out var session) && session is Sim.Work.HammerSession or Sim.Hospitality.ScrubSession
            && Sim.Systems.Crafting.ToolInHand(sim, e, session is Sim.Work.HammerSession ? "Hammer" : "Mop") is { IsNull: false } tool2
            && HumanLook.HeldOf(w.Get<Thing>(tool2).Def) is { } h)
            return h;
        if (w.TryGet<Equipment>(e, out var eq) && w.IsAlive(eq.Weapon) && HumanLook.HeldOf(w.Get<Thing>(eq.Weapon).Def) is { } weapon)
            return weapon;
        return null;
    }

    private readonly List<Quad> _modelQuads = [];

    /// <summary>A box model drawn this frame (items, blueprint ghosts, tool previews); see <see cref="PropModel"/>.</summary>
    public void AddModel(IReadOnlyList<ModelBox> model, int3 cell, int facing, MaterialDef? material, uint color, uint flags,
        bool translucent = false, int count = int.MaxValue, float rise = 1f, float lift = 0f, float heightScale = 1f)
    {
        _modelQuads.Clear();
        PropModel.Build(_modelQuads, model, cell, facing, name => _materials.Resolve(name, material) is var (n, t) ? (S(n), t) : default,
            color, flags, count: count, rise: rise, lift: lift, heightScale: heightScale);
        foreach (var q in _modelQuads)
            _quads.Add(q, translucent);
    }

    /// <summary>How high the top of the furniture in a cell is, as drawn (0 with none, or a blueprint): where items there rest, and who's climbed up stands.</summary>
    public static float SurfaceUnder(Map map, World w, int3 cell)
    {
        if (!map.InBounds(cell))
            return 0f;
        var s = map.Structure[map.Idx(cell)];
        return !s.IsNull && !w.Has<Construction>(s) && w.Get<Thing>(s).Def is { Building.SurfaceHeight: > 0 } d
            ? d.Building!.SurfaceHeight * WorldScale.HeightOf(d) : 0f;
    }

    /// <summary>Height of the first surface under a column, starting at <paramref name="cell"/>.</summary>
    private static float GroundBelow(Map map, int3 cell)
    {
        if (!map.InBounds(cell))
            return cell.Z;
        var ground = cell.Z;
        while (ground > 0 && !map.Standable(map.Idx(cell.X, cell.Y, ground)) && !map.IsSolid(map.Idx(cell.X, cell.Y, ground - 1)))
            ground--;
        return ground;
    }

    /// <summary>
    /// One sheet frame as an upright billboard at native size, anchored at the sheet's pivot (the
    /// feet), mirrored for <paramref name="facing"/> = -1 and rolled by <paramref name="roll"/>
    /// radians in screen space, about the feet or (for flyers) about the frame's middle.
    /// </summary>
    public void BillboardFrame(SpriteSheet sheet, in SheetFrame f, float3 feet, int facing, float roll, uint color, uint flags, bool aboutCenter = false)
    {
        var px = 1f / (PixelsPerCell * sheet.Density);
        var r0 = _cam.Right;
        var u0 = _cam.Up;
        float cos = MathF.Cos(roll), sin = MathF.Sin(roll);
        var r = r0 * cos + u0 * sin;
        var u = u0 * cos - r0 * sin;
        if (aboutCenter)
        {
            var cy = (sheet.Pivot.Y - sheet.FrameSize.Y * 0.5f) * px;
            feet += u0 * cy - u * cy;
        }
        var rf = r * facing;
        var lx = (f.Offset.X - sheet.Pivot.X) * px;
        var ly = (f.Offset.Y - sheet.Pivot.Y) * px;
        var size = f.Sprite.PixelSize;
        // A trimmed frame (a look layer) ends above the pivot: tell the shader where the feet are,
        // so the layer sorts with the figure rather than behind it.
        var drop = size.Y > 0 ? (sheet.Pivot.Y - (f.Offset.Y + size.Y)) / (float)size.Y : 0f;
        _quads.Add(
            new Quad
            {
                Origin = feet + rf * lx - u * ly,
                AxisU = rf * (size.X * px),
                AxisV = -u * (size.Y * px),
                Flags = flags | Quad.Billboard | Quad.Upright | Quad.FeetInAo | (sheet.Density > 1 ? Quad.Smooth : 0u),
                Color = color,
                Ao = Quad.FeetBelow(drop),
                Uv = f.Sprite.Uv,
            },
            translucent: false
        );
    }

    /// <summary>
    /// Soft grey puffs for every smoky cell in view, drifting upward and wobbling so the column
    /// reads as moving. Each cell's puffs loop within the cell, so nothing pops when cells change.
    /// </summary>
    private void DrawSmoke(Simulation sim, Func<int3, bool> inView, float time)
    {
        var map = sim.Map;
        var puff = S("fx/smoke");
        foreach (var i in sim.Smoke.SmokyCells)
        {
            var d = map.Smoke[i];
            if (d < 0.04f)
                continue;
            var c = map.CellOf(i);
            if (!inView(c))
                continue;
            var puffs = d > 0.4f ? 2 : 1;
            for (var k = 0; k < puffs; k++)
            {
                var h = SimRandom.Hash(i, k, 77);
                var phase = (h & 1023) / 1023f;
                var rise = (time * 0.25f + phase) % 1f;
                var wobble = MathF.Sin(time * 0.9f + phase * 6.3f) * 0.2f;
                var center = new float3(c.X + 0.3f + 0.4f * ((h >> 10 & 255) / 255f) + wobble, c.Y + 0.3f + 0.4f * ((h >> 18 & 255) / 255f), c.Z + rise * 0.8f);
                var size = 0.9f + 0.6f * rise;
                var alpha = Math.Min(0.6f, d * 0.7f) * (1f - MathF.Abs(rise - 0.5f) * 1.2f);
                Billboard(puff, center, size, size, Rgba.Pack(0.55f, 0.55f, 0.58f, alpha), Quad.Level(c.Z), translucent: true);
            }
        }
    }

    /// <summary>When each breath or shot was first drawn: the sim clock stands still during combat turns, so fades use real time too.</summary>
    private readonly Dictionary<object, float> _firstSeen = new(ReferenceEqualityComparer.Instance);

    private float Age(object ev, float time, float seconds)
    {
        if (!_firstSeen.TryGetValue(ev, out var seen))
            _firstSeen[ev] = seen = time;
        return (time - seen) / seconds;
    }

    /// <summary>A bubble over whoever started each new interaction since the last frame (warm for friendly, red for a slight).</summary>
    private void EmitSpeech(Simulation sim, Func<int3, bool> inView)
    {
        var w = sim.World;
        if (_lastSpeechTick == long.MaxValue)
            _lastSpeechTick = sim.Clock.Tick; // don't replay what happened before we started drawing
        foreach (var ev in sim.Social.Recent)
        {
            if (ev.Tick <= _lastSpeechTick || !w.IsAlive(ev.Initiator))
                continue;
            var cell = sim.LocationOf(ev.Initiator);
            if (!inView(cell) || !Seen(ev.Initiator))
                continue;
            var hostile = (ev.Def.RecipientThoughtDef?.Opinion ?? 0) < 0;
            var color = hostile ? new float4(1f, 0.55f, 0.5f, 0.95f) : new float4(1f, 0.97f, 0.88f, 0.95f);
            _particles.Speech(DrawPos(sim, ev.Initiator, w.Get<Position>(ev.Initiator).Pos) + new float3(0.25f, -0.25f, 1.35f), color);
        }
        if (sim.Social.Recent.Count > 0)
            _lastSpeechTick = Math.Max(_lastSpeechTick, sim.Social.Recent[^1].Tick);
    }

    /// <summary>
    /// The core of a breath flame (<c>fx/breath_core</c>, white, same frames as <c>fx/breath</c>): drawn
    /// over the orange flame and tinted with the element colour, so the flame keeps its normal body and
    /// only its heart changes. Null when an art set lacks it.
    /// </summary>
    private Sprite? CoreSprite(int frame) =>
        atlas.TryGetSheet("fx/breath_core", out var sheet) && sheet.Find("burn") is { } burn
            ? burn.Frames[((frame % burn.Frames.Length) + burn.Frames.Length) % burn.Frames.Length].Sprite
            : null;

    /// <summary>
    /// Recent breaths as jets along each ray, leaving the breather's drawn mouth. The jet shoots
    /// out over its first moments, each flame is an <c>fx/breath</c> flipbook frame offset by
    /// distance so it visibly rolls outward, and impacts leave a ground glow. Fire is an orange
    /// flame with an element-coloured heart and throws embers; the other elements tint the whole
    /// jet their colour (frost, poison, thorns), and lightning also leaps between those it
    /// struck (<see cref="Sim.Environment.BreathEvent.Arcs"/>). The whole jet fades over
    /// <see cref="Sim.Environment.Breath.ShowTicks"/> (game time) or the same in real time,
    /// whichever comes first.
    /// </summary>
    private void DrawBreaths(Simulation sim, float time)
    {
        var fire = S("fx/fire");
        atlas.TryGetSheet("fx/breath", out var flipbook);
        var glow = S("fx/smoke");
        var cut = Math.Min(CutawayZ, sim.Map.Depth - 1);
        if (_firstSeen.Count > 64)
            foreach (var k in _firstSeen.Keys.ToList())
                if (!sim.RecentBreaths.Contains(k) && !sim.RecentShots.Contains(k))
                    _firstSeen.Remove(k);
        foreach (var ev in sim.RecentBreaths)
        {
            var first = !_firstSeen.ContainsKey(ev);
            var age = Math.Max(
                (sim.Clock.Tick - ev.Tick) / (float)Sim.Environment.Breath.ShowTicks,
                Age(ev, time, Sim.Environment.Breath.ShowTicks / (float)SimClock.TicksPerSecond)
            );
            if (age > 1f)
                continue;
            var alpha = 0.9f * (1f - age);
            var flame = FireColor;
            var body = Rgba.Pack(1, 1, 1, alpha);
            var mouth = !ev.Source.IsNull && _anim.TryGetMouth(ev.Source, out var drawn) ? drawn : ev.Mouth;
            var reach = Math.Min(1f, 0.15f + age * 5f);
            if (first)
                foreach (var end in ev.RayEnds)
                    _particles.Embers(end, 3);
            for (var r = 0; r < ev.RayEnds.Count; r++)
            {
                var d = ev.RayEnds[r] - mouth;
                var len = MathF.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z);
                var steps = Math.Max(1, (int)(len / 0.6f));
                for (var k = 1; k <= steps; k++)
                {
                    var t = k / (float)steps;
                    if (t > reach)
                        break;
                    var p = mouth + d * t;
                    if (p.Z > cut + 1)
                        continue;
                    var flicker = 0.85f + 0.15f * MathF.Sin(time * 20f + r * 3.1f + k);
                    var size = (0.3f + 0.65f * t) * flicker;
                    var frame = (int)(time * 16f + r * 2 - k + 1000);
                    var sprite = flipbook?.Find("burn") is { } burn ? burn.Frames[frame % burn.Frames.Length].Sprite : fire;
                    var at = p - WorldScale.Rise(size * 0.45f);
                    var flags = Quad.Level((int)p.Z) | Quad.Unlit | Quad.Glow;
                    Billboard(sprite, at, size, size, body, flags, translucent: true);
                    if (CoreSprite(frame) is { } core)
                        Billboard(core, at, size, size, Rgba.Pack(flame.X, flame.Y, flame.Z, alpha), flags, translucent: true);
                }
                if (reach >= 1f)
                {
                    var end = ev.RayEnds[r];
                    var tint = Rgba.Pack(1f, 0.5f, 0.15f, 0.5f * (1f - age));
                    Decal(glow, new float3(end.X, end.Y, MathF.Floor(end.Z) + 0.04f), 1.1f, tint, Quad.Level((int)end.Z) | Quad.Unlit, translucent: true);
                }
            }
        }
    }

    /// <summary>The heart of a dragon's flame.</summary>
    private static readonly float4 FireColor = new(1f, 0x4A / 255f, 0x24 / 255f, 1f);

    /// <summary>Attacks: an arrow streaking to the target, then a flash (red for a wound, grey for a miss).</summary>
    private void DrawShots(Simulation sim, float time)
    {
        var white = S(TextureAtlas.White);
        foreach (var shot in sim.RecentShots)
        {
            if (!SeenAt(shot.From) && !SeenAt(shot.To))
                continue;
            if (!_firstSeen.ContainsKey(shot))
                Float(shot.To + WorldScale.Rise(1.1f), shot.Hit ? "Hit!" : "Miss", shot.Hit ? new float4(1f, 0.43f, 0.35f, 1f) : new float4(0.8f, 0.8f, 0.8f, 1f));
            var age = Age(shot, time, 0.8f);
            if (age > 1f)
                continue;
            var from = shot.From + new float3(0, 0, 0.7f);
            var to = shot.To + new float3(0, 0, 0.6f);
            var flight = shot.Ranged ? Math.Min(1f, age * 3f) : 1f;
            if (shot.Ranged && flight < 1f)
            {
                for (var k = 0; k < 4; k++)
                {
                    var t = Math.Max(0f, flight - k * 0.03f);
                    Billboard(white, from + (to - from) * t, 0.12f, 0.12f, Rgba.Pack(1f, 0.95f, 0.8f, 0.9f - k * 0.2f), Quad.Unlit | Quad.Always, translucent: true);
                }
                continue;
            }
            var flash = 1f - age;
            var color = shot.Hit ? Rgba.Pack(1f, 0.15f, 0.1f, 0.8f * flash) : Rgba.Pack(0.8f, 0.8f, 0.8f, 0.6f * flash);
            Billboard(S(shot.Hit ? "fx/fire" : "fx/smoke"), to - new float3(0, 0, 0.3f), 0.5f + 0.4f * age, 0.5f + 0.4f * age, color, Quad.Unlit | Quad.Always, translucent: true);
        }
    }

    /// <summary>
    /// A preset building's phase going up (one site over its plot, off the map's layers): a ghost
    /// of all it lays, firmer as materials arrive, with the built share rising inside it (walls
    /// climbing, floor and roof laid cell by cell) and a progress bar over the plot.
    /// </summary>
    private readonly List<Quad> _kitQuads = [];

    private void DrawRaising(Simulation sim, Entity building, Func<int3, bool> inView)
    {
        var w = sim.World;
        // Upgrades being fitted: a ghost of what goes on the furnishing, rising as it's built.
        foreach (var u in w.Get<PresetBuilding>(building).Def.Upgrades)
            if (Buildings.UpgradeSite(sim, building, u) is { IsNull: false } fitting && w.Get<Thing>(fitting) is var ft && inView(ft.Cell) && u.Model.Count > 0)
            {
                var uc = w.Get<Construction>(fitting);
                var done = Math.Clamp(uc.WorkDone / u.Work, 0, 1);
                AddModel(u.Model, ft.Cell, ft.Facing, null, Rgba.Pack(0.6f, 0.8f, 1f, 0.3f + 0.3f * Buildings.Delivered(uc)), Quad.Level(ft.Cell.Z), translucent: true,
                    heightScale: WorldScale.FurnitureHeight);
                if (done > 0)
                    AddModel(u.Model, ft.Cell, ft.Facing, null, Rgba.White, Quad.Level(ft.Cell.Z), rise: done, heightScale: WorldScale.FurnitureHeight);
                _sitesNow[fitting] = (ft.Cell, u.Label, done);
            }
        var site = Buildings.PhaseSite(sim, building);
        if (site.IsNull)
            return;
        var b = w.Get<PresetBuilding>(building);
        var c = w.Get<Construction>(site);
        var phase = w.Get<Thing>(site).Def.Phase;
        var frac = Math.Clamp(c.WorkDone / c.Target.Building!.Work, 0, 1);
        var ghost = Rgba.Pack(0.6f, 0.8f, 1f, 0.22f + 0.35f * Buildings.Delivered(c));
        var stamps = Buildings.Stamps(sim.Map, b, phase).ToList();
        var laid = (int)(frac * stamps.Count);
        for (var k = 0; k < stamps.Count; k++)
        {
            var s = stamps[k];
            if (!sim.Map.InBounds(s.Cell) || !inView(s.Cell))
                continue;
            var kit = b.VariantDef.KitDef;
            var level = Quad.Level(s.Cell.Z) | (s.Def.IsRoof ? Quad.Roof : 0);
            if (kit != null && (s.Def.Building!.IsWall || s.Def.Building.IsDoor))
            {
                // The kit's own pieces, the built share rising inside their ghost.
                foreach (var (piece, facing) in BuildingKit.WallPieces(kit, b, s.Cell))
                {
                    AddModel(piece, s.Cell, facing, c.MainMaterial, ghost, level, translucent: true);
                    if (frac > 0)
                        AddModel(piece, s.Cell, facing, c.MainMaterial, Rgba.White, level, rise: frac);
                }
                continue;
            }
            if (kit?.Roof is { } pitched && s.Def.IsRoof)
            {
                // The pitched roof, laid cell by cell as the work goes on.
                _kitQuads.Clear();
                BuildingKit.Roof(_kitQuads, pitched, b, s.Cell, (name, _) => _materials.Resolve(name, c.MainMaterial) is var (n, t) ? (S(n), t) : default,
                    k < laid ? Rgba.White : ghost, level);
                foreach (var q in _kitQuads)
                    _quads.Add(q, translucent: k >= laid);
                continue;
            }
            var (spriteName, tint) = _materials.Resolve(s.Def.IsFloor && !s.Def.IsRoof && kit is { Floor.Length: > 0 } ? kit.Floor : s.Def.Sprite, c.MainMaterial);
            var sprite = S(spriteName);
            var floor = Position.CenterOf(s.Cell);
            if (s.Def.IsFloor)
            {
                Decal(sprite, floor + new float3(0, 0, 0.02f), 1f, ghost, level, translucent: true);
                if (k < laid)
                    Decal(sprite, floor + new float3(0, 0, 0.03f), 1f, tint, level);
            }
            else
            {
                Cube(sprite, s.Cell, ghost, level, translucent: true);
                if (frac > 0)
                    CubePart(sprite, s.Cell, frac, tint, level);
            }
        }
        var top = 1.3f + (phase == BuildPhase.Roof ? b.Def.Storeys : phase is BuildPhase.UpperFloor or BuildPhase.UpperWalls ? 1 : 0);
        if (frac > 0)
            Bar(Position.CenterOf(b.Anchor) + new float3(0, 0, top), frac, Rgba.Pack(0.4f, 0.9f, 0.4f), Quad.Level(b.Anchor.Z));
        _sitesNow[site] = (b.Anchor, c.Target.Label, frac);
    }

    private void DrawThing(Simulation sim, Entity e, in Thing thing)
    {
        var w = sim.World;
        var def = thing.Def;
        var cell = thing.Cell;
        var floor = Position.CenterOf(cell);
        var level = Quad.Level(cell.Z);
        // An item in a table's (or chest's, or bench's) cell rests on its top.
        var lift = def.Category == ThingCategory.Item ? SurfaceUnder(sim.Map, w, cell) : 0f;
        floor += new float3(0, 0, lift);

        if (w.TryGet<Construction>(e, out var c))
        {
            // Ghost of the finished building, more opaque once materials arrive, plus a progress bar.
            // Once wood arrives the ghost shows the wood it'll be made of.
            var ghost = Rgba.Pack(0.6f, 0.8f, 1f, c.MaterialsComplete ? 0.6f : 0.3f);
            var (siteSprite, siteTint) = _materials.Resolve(def.Sprite, c.MainMaterial);
            var ghostSprite = S(siteSprite);
            if (def.Model is { } siteModel)
            {
                // Boxes: a see-through ghost of the whole thing, and the real one rising inside it.
                var frac = Math.Clamp(c.WorkDone / c.Target.Building!.Work, 0, 1);
                AddModel(siteModel, cell, thing.Facing, c.MainMaterial, ghost, level, translucent: true, heightScale: WorldScale.HeightOf(def));
                if (frac > 0)
                    AddModel(siteModel, cell, thing.Facing, c.MainMaterial, Rgba.White, level, rise: frac, heightScale: WorldScale.HeightOf(def));
                _sitesNow[e] = (cell, c.MainMaterial?.Describe(def.Label) ?? def.Label, frac);
                if (frac > 0)
                    Bar(floor + new float3(0, 0, 1.15f), frac, Rgba.Pack(0.4f, 0.9f, 0.4f), level);
                return;
            }
            switch (def.Shape)
            {
                case RenderShape.Block:
                    Cube(ghostSprite, cell, ghost, level, translucent: true);
                    break;
                case RenderShape.Billboard:
                    Billboard(ghostSprite, floor, def.RenderSize * Aspect(ghostSprite), def.RenderSize, ghost, level, translucent: true);
                    break;
                default:
                    Decal(ghostSprite, floor + new float3(0, 0, 0.02f), def.RenderSize, ghost, level, translucent: true);
                    break;
            }
            _sitesNow[e] = (cell, c.MainMaterial?.Describe(def.Label) ?? def.Label, Math.Clamp(c.WorkDone / c.Target.Building!.Work, 0, 1));
            if (c.WorkDone > 0)
            {
                // The finished building rises out of the ghost as work progresses.
                var frac = Math.Clamp(c.WorkDone / c.Target.Building!.Work, 0, 1);
                switch (def.Shape)
                {
                    case RenderShape.Block:
                        CubePart(ghostSprite, cell, frac, siteTint, level);
                        break;
                    case RenderShape.Billboard:
                        BillboardBottom(ghostSprite, floor, def.RenderSize * Aspect(ghostSprite), def.RenderSize, frac, siteTint, level);
                        break;
                    default:
                        DecalPart(ghostSprite, floor + new float3(0, 0, 0.025f), def.RenderSize, frac, siteTint, level);
                        break;
                }
                Bar(floor + new float3(0, 0, 1.15f), frac, Rgba.Pack(0.4f, 0.9f, 0.4f), level);
            }
            return;
        }

        var (art, tint) = ArtOf(thing);
        // A picked bush shows its bare art if there is some, else it's just greyed.
        var baseSprite = def.Sprite;
        if (w.TryGet<PlantState>(e, out var plant) && !plant.Ready)
        {
            if (atlas.TryGet(def.Sprite + "_bare", out _))
                baseSprite = def.Sprite + "_bare";
            else
                tint = Rgba.Pack(0.7f, 0.7f, 0.7f);
        }

        if (def.Model is { } model)
        {
            // Item piles grow with the stack; furniture is in the chunk meshes (WorldMesher).
            if (def.Category == ThingCategory.Item)
                AddModel(model, cell, thing.Facing, thing.Material, tint, level, count: w.TryGet<Stack>(e, out var st) ? st.Count : 1, lift: lift);
        }
        else if (def.Category == ThingCategory.Item)
        {
            var scale = 0.75f * def.RenderSize;
            Billboard(art, floor, scale, scale, tint, level);
            return;
        }

        else switch (def.Shape)
        {
            case RenderShape.Block or RenderShape.Ramp or RenderShape.Slab:
                break; // meshed
            case RenderShape.Decal:
                Decal(art, floor + new float3(0, 0, 0.02f), def.RenderSize, tint, level);
                break;
            default:
                DrawVariedBillboard(def, thing.Material == null ? baseSprite : _materials.Resolve(def.Sprite, thing.Material).Sprite, cell, floor, tint, level);
                break;
        }

        if (w.TryGet<Fueled>(e, out var fuel) && def.Building?.Fuel is { } props)
        {
            var frac = Math.Clamp(fuel.Fuel / props.Capacity, 0, 1);
            if (def.Model != null && fuel.Fuel > 0)
            {
                // A modelled fire: flickering flames over the logs.
                var t = _anim.Clock * 7f + e.Index;
                var size = 0.45f + 0.25f * frac + 0.05f * MathF.Sin(t) + 0.03f * MathF.Sin(t * 2.3f);
                Billboard(S("fx/fire"), floor + new float3(0, 0, 0.08f), size * 0.85f, size, Rgba.Pack(1f, 0.92f, 0.8f, 0.95f), level | Quad.Unlit | Quad.Glow, translucent: true);
            }
            Bar(floor + new float3(0, 0, 0.9f), frac, frac > props.RefuelAt ? Rgba.Pack(1f, 0.6f, 0.2f) : Rgba.Pack(1f, 0.2f, 0.2f), level);
        }
    }

    private static string DesignationIcon(Designation d) =>
        d.HasFlag(Designation.Burn) ? "ui/icon_burn"
        : d.HasFlag(Designation.Deconstruct) ? "ui/icon_deconstruct"
        : d.HasFlag(Designation.Dig) ? "ui/icon_dig"
        : d.HasFlag(Designation.Mine) ? "ui/icon_mine"
        : d.HasFlag(Designation.Chop) ? "ui/icon_chop"
        : d.HasFlag(Designation.Fish) ? "ui/icon_fish"
        : "ui/icon_harvest";

    /// <summary>What a villager's status bubble shows: lying states and alarms first, then the job.</summary>
    private static string? BubbleIcon(Simulation sim, Entity e)
    {
        var w = sim.World;
        var c = w.Get<Creature>(e);
        var mind = w.Get<Mind>(e);
        if (c.Downed)
            return "ui/icon_doctor";
        if (mind.Asleep)
            return null; // the Zzz say it
        var needs = w.Get<Needs>(e);
        foreach (var rate in c.Def.Needs)
            if (needs.Get(rate.NeedDef) < rate.NeedDef.UrgentBelow)
                return "ui/icon_alert";
        if (mind.Drafted)
            return "ui/icon_fight";
        return mind.Job switch
        {
            Sim.Ai.BuildJob or Sim.Ai.DeliverJob or Sim.Ai.RefuelJob => "ui/icon_build",
            Sim.Ai.DeconstructJob => "ui/icon_deconstruct",
            Sim.Ai.HarvestJob h when !h.Focus.IsNull && w.IsAlive(h.Focus) && w.Get<Thing>(h.Focus).Def.Harvest is { } hv =>
                hv.WorkType == WorkType.Chop ? "ui/icon_chop" : hv.WorkType == WorkType.Mine ? "ui/icon_mine" : "ui/icon_harvest",
            Sim.Ai.DigJob => "ui/icon_dig",
            Sim.Ai.HaulJob => "ui/icon_haul",
            Sim.Ai.EatJob => "ui/icon_eat",
            Sim.Ai.SleepJob => "ui/icon_sleep",
            Sim.Ai.FightFireJob => "ui/icon_burn",
            Sim.Ai.TendJob or Sim.Ai.RescueJob => "ui/icon_doctor",
            Sim.Ai.AssaultJob => "ui/icon_fight",
            Sim.Ai.HuntJob => "ui/icon_hunt",
            Sim.Ai.ButcherJob => "ui/icon_butcher",
            Sim.Ai.FishJob => "ui/icon_fish",
            _ => null,
        };
    }

    private void DrawBubble(string icon, float3 at, uint level, float phase)
    {
        at += WorldScale.Rise(MathF.Sin(phase * 2f) * 0.03f);
        var bubble = S("ui/bubble");
        const float size = 0.56f;
        Billboard(bubble, at, size, size * 30f / 24f, Rgba.Pack(1, 1, 1, 0.95f), level | Quad.Unlit);
        // Nudged towards the camera so the icon wins the depth test against its bubble.
        Billboard(S(icon), at + WorldScale.Rise(size * 6f / 24f + size * 0.15f) - _cam.Forward * 0.03f, size * 0.7f, size * 0.7f, Rgba.White, level | Quad.Unlit);
    }

    /// <summary>
    /// Sun glints on open water: every few water columns sparkles briefly now and then (the
    /// <c>fx/glint</c> sheet), each on its own beat. Water is baked into static meshes; this is the
    /// cheap way to make it feel alive. Real time, so the water still twinkles while paused.
    /// </summary>
    private void DrawWaterGlints(Map map, int x0, int y0, int x1, int y1, int cut, float time)
    {
        if (!atlas.TryGetSheet("fx/glint", out var sheet) || sheet.Find("sparkle") is not { } sparkle)
            return;
        var top = Math.Min(cut, map.Depth - 1);
        for (var y = y0; y <= y1; y++)
        for (var x = x0; x <= x1; x++)
        {
            var h = Variation.Hash(x, y, 0x61A);
            if (h % 5 != 0)
                continue;
            var z = top;
            while (z > 0 && !map.Block[map.Idx(x, y, z)].Solid)
                z--;
            if (!map.Block[map.Idx(x, y, z)].IsWater || z + 1 > top + 1)
                continue;
            // Visible for the first fifth of each cycle, the rest of the time quiet.
            var phase = (time * 0.3f + (h >> 8 & 1023) / 1023f) % 1f;
            if (phase > 0.2f)
                continue;
            var frame = sparkle.Frames[Math.Min(sparkle.Frames.Length - 1, (int)(phase / 0.2f * sparkle.Frames.Length))];
            var at = new float3(x + 0.2f + 0.6f * ((h >> 18 & 63) / 63f), y + 0.2f + 0.6f * ((h >> 24 & 63) / 63f), z + 1.04f);
            Billboard(frame.Sprite, at, 0.28f, 0.28f, Rgba.Pack(1f, 1f, 0.95f, 0.85f), Quad.Level(z + 1) | Quad.Unlit, translucent: true);
        }
    }

    private readonly Dictionary<string, IReadOnlyList<Sprite>> _variantCache = [];

    /// <summary>
    /// An upright thing (tree, bush, boulder) with per-cell variety: one of its sprite's variants,
    /// mirrored at random, a little bigger or smaller, nudged off the cell centre so groves don't
    /// look planted on a grid, and (plants) swaying gently. Width follows the sprite's aspect, with
    /// <see cref="ThingDef.RenderSize"/> as the height.
    /// </summary>
    private void DrawVariedBillboard(ThingDef def, string spriteName, int3 cell, float3 feet, uint tint, uint level)
    {
        if (def.Glow)
            level |= Quad.Unlit | Quad.Glow; // shows in the dark (and the bloom picks it up)
        if (_atlasVersion != atlas.Version)
            _variantCache.Clear();
        if (!_variantCache.TryGetValue(spriteName, out var variants))
            _variantCache[spriteName] = variants = atlas.Variants(spriteName);
        var h = Variation.Hash(cell.X, cell.Y, 0x7EE);
        var sprite = Variation.Mirror(variants[(int)((h >> 8) % (uint)variants.Count)], (h & 1) != 0, false);
        var plant = def.Category == ThingCategory.Plant;
        var height = def.RenderSize * (plant ? 0.9f + 0.2f * ((h >> 16 & 255) / 255f) : 1f);
        var width = height * sprite.PixelSize.X / Math.Max(1f, sprite.PixelSize.Y);
        if (plant)
            feet += new float3(((h >> 24 & 15) / 15f - 0.5f) * 0.3f, ((h >> 28 & 15) / 15f - 0.5f) * 0.3f, 0);

        var r = _cam.Right;
        var u = _cam.Up;
        // Sway: shear the top while the base stays planted.
        var sway = plant ? MathF.Sin(_now * 1.3f + cell.X * 0.71f + cell.Y * 0.43f) * 0.03f * height : 0f;
        _quads.Add(
            new Quad
            {
                Origin = feet + u * height - r * (width * 0.5f) + r * sway,
                AxisU = r * width,
                AxisV = -u * height - r * sway,
                Flags = level | Quad.Billboard | Quad.Upright | Quad.Smooth, // trees and rocks are drawn at 4x detail (chibi_nature.py)
                Color = tint,
                Ao = Quad.NoAo,
                Uv = sprite.Uv,
            },
            false
        );
    }

    // ---------------- Quad helpers ----------------

    /// <summary>An upright sprite facing the camera, standing on <paramref name="feet"/>.</summary>
    public void Billboard(in Sprite sprite, float3 feet, float width, float height, uint color, uint flags, bool translucent = false)
    {
        var r = _cam.Right;
        var u = _cam.Up;
        _quads.Add(
            new Quad
            {
                Origin = feet + u * height - r * (width * 0.5f),
                AxisU = r * width,
                AxisV = -u * height,
                Flags = flags | Quad.Billboard | Quad.Upright,
                Color = color,
                Ao = Quad.NoAo,
                Uv = sprite.Uv,
            },
            translucent
        );
    }

    /// <summary>A square sprite lying flat, centered on <paramref name="center"/> (north up, like the old top-down view).</summary>
    public void Decal(in Sprite sprite, float3 center, float size, uint color, uint flags, bool translucent = false, int facing = 0) =>
        Decal(sprite, center, size, size, color, flags, translucent, facing);

    /// <summary>A sheet frame lying flat at its native pixel scale (tall frames aren't squashed square).</summary>
    /// <summary>
    /// A sheet frame laid flat at native size around <paramref name="center"/> (the whole frame's
    /// middle): a trimmed frame (an Aseprite export, a sparse look layer) is shifted by its offset
    /// so it lines up with the rest of the frame.
    /// </summary>
    private void FrameDecal(SpriteSheet sheet, in SheetFrame frame, float3 center, float scale, uint color, uint flags, int facing = 0)
    {
        var size = frame.Sprite.PixelSize;
        var dx = (frame.Offset.X + size.X * 0.5f - sheet.FrameSize.X * 0.5f) / (PixelsPerCell * sheet.Density) * scale;
        var dy = (frame.Offset.Y + size.Y * 0.5f - sheet.FrameSize.Y * 0.5f) / (PixelsPerCell * sheet.Density) * scale;
        if (dx != 0 || dy != 0)
            center += PropModel.Rotate(new float3(dx, dy, 0), facing);
        Decal(frame.Sprite, center, size.X / (PixelsPerCell * sheet.Density) * scale, size.Y / (PixelsPerCell * sheet.Density) * scale, color,
            flags | (sheet.Density > 1 ? Quad.Smooth : 0u), false, facing);
    }

    public void Decal(in Sprite sprite, float3 center, float width, float height, uint color, uint flags, bool translucent = false, int facing = 0)
    {
        // Turned with a thing's facing (someone lying in a turned bed), about the centre.
        var u = PropModel.Rotate(new float3(width, 0, 0), facing);
        var v = PropModel.Rotate(new float3(0, height, 0), facing);
        _quads.Add(
            new Quad
            {
                Origin = center - (u + v) * 0.5f + new float3(0, 0, 0.01f),
                AxisU = u,
                AxisV = v,
                Flags = flags,
                Color = color,
                Ao = Quad.NoAo,
                Uv = sprite.Uv,
            },
            translucent
        );
    }

    /// <summary>Width over height of a sprite's art (billboards take <see cref="ThingDef.RenderSize"/> as their height).</summary>
    private static float Aspect(in Sprite s) => s.PixelSize.X / Math.Max(1f, s.PixelSize.Y);

    /// <summary>The bottom <paramref name="frac"/> of an upright sprite (a building rising as it's built).</summary>
    private void BillboardBottom(in Sprite sprite, float3 feet, float width, float height, float frac, uint color, uint flags)
    {
        var r = _cam.Right;
        var u = _cam.Up;
        var uv = sprite.Uv;
        _quads.Add(
            new Quad
            {
                Origin = feet + u * (height * frac) - r * (width * 0.5f),
                AxisU = r * width,
                AxisV = -u * (height * frac),
                Flags = flags | Quad.Billboard | Quad.Upright,
                Color = color,
                Ao = Quad.NoAo,
                Uv = new float4(uv.X, uv.W - (uv.W - uv.Y) * frac, uv.Z, uv.W),
            },
            false
        );
    }

    /// <summary>The first <paramref name="frac"/> of a flat sprite, filling along its rows.</summary>
    private void DecalPart(in Sprite sprite, float3 center, float size, float frac, uint color, uint flags)
    {
        var uv = sprite.Uv;
        _quads.Add(
            new Quad
            {
                Origin = center - new float3(size * 0.5f, size * 0.5f, 0) + new float3(0, 0, 0.01f),
                AxisU = new float3(size, 0, 0),
                AxisV = new float3(0, size * frac, 0),
                Flags = flags,
                Color = color,
                Ao = Quad.NoAo,
                Uv = new float4(uv.X, uv.Y, uv.Z, uv.Y + (uv.W - uv.Y) * frac),
            },
            false
        );
    }

    /// <summary>A block filled to <paramref name="height"/> of its cell (a wall going up), its sides stacked from the ground and cropped at the top.</summary>
    private void CubePart(Sprite sprite, int3 cell, float height, uint color, uint flags)
    {
        float x = cell.X, y = cell.Y, z = cell.Z;
        var top = z + height;
        _quads.Add(new Quad { Origin = new float3(x, y, top), AxisU = new float3(1, 0, 0), AxisV = new float3(0, 1, 0), Flags = flags, Color = color, Ao = Quad.NoAo, Uv = sprite.Uv }, false);
        CubeSides(sprite, new float3(x, y, top), height, color, flags, false, fromBottom: true);
    }

    /// <summary>A unit cube filling a cell (ghost walls, previews). Faces aren't culled.</summary>
    public void Cube(Sprite sprite, int3 cell, uint color, uint flags, bool translucent = false)
    {
        float x = cell.X, y = cell.Y, z = cell.Z;
        _quads.Add(new Quad { Origin = new float3(x, y, z + 1), AxisU = new float3(1, 0, 0), AxisV = new float3(0, 1, 0), Flags = flags, Color = color, Ao = Quad.NoAo, Uv = sprite.Uv }, translucent);
        CubeSides(sprite, new float3(x, y, z + 1), 1f, color, flags, translucent);
    }

    /// <summary>The four sides of a cell-wide box whose top is at <paramref name="corner"/>.Z, the sprite stacked up them (<see cref="WorldScale"/>).</summary>
    private void CubeSides(Sprite sprite, float3 corner, float height, uint color, uint flags, bool translucent, bool fromBottom = false)
    {
        float x = corner.X, y = corner.Y, top = corner.Z;
        var template = new Quad { Flags = flags, Color = color, Ao = Quad.NoAo };
        var tiles = WorldScale.TilesPerLevel(sprite);
        var count = WorldScale.TileCount(height, tiles);
        Side(new float3(x + 1, y + 1, top), new float3(0, -1, 0));
        Side(new float3(x, y, top), new float3(0, 1, 0));
        Side(new float3(x, y + 1, top), new float3(1, 0, 0));
        Side(new float3(x + 1, y, top), new float3(-1, 0, 0));

        void Side(float3 o, float3 u)
        {
            for (var k = 0; k < count; k++)
                _quads.Add(WorldScale.Tile(template, o, u, height, sprite.Uv, tiles, k, fromBottom), translucent);
        }
    }

    /// <summary>A flat ribbon between two points, lying on the ground (debug paths).</summary>
    public void Line(float3 a, float3 b, float thickness, uint color)
    {
        var d = b - a;
        var side = new float3(-d.Y, d.X, 0);
        var len = MathF.Sqrt(side.X * side.X + side.Y * side.Y);
        if (len < 1e-5f)
            return;
        side *= thickness / len;
        _quads.Add(
            new Quad
            {
                Origin = a - side * 0.5f + new float3(0, 0, 0.05f),
                AxisU = d,
                AxisV = side,
                Flags = Quad.Unlit | Quad.Always,
                Color = color,
                Ao = Quad.NoAo,
                Uv = atlas.WhiteSprite.Uv,
            },
            translucent: true
        );
    }

    /// <summary>A small progress bar floating at <paramref name="at"/>, facing the camera.</summary>
    private void Bar(float3 at, float fraction, uint color, uint level)
    {
        var white = atlas.WhiteSprite;
        var left = at - _cam.Right * 0.4f;
        Billboard(white, at, 0.8f, 0.1f, Rgba.Pack(0, 0, 0, 0.7f), level | Quad.Unlit);
        // Nudged towards the camera so it wins the depth test against the background bar.
        var nudge = -_cam.Forward * 0.02f;
        Billboard(white, left + _cam.Right * (0.4f * fraction) + nudge, 0.8f * fraction, 0.1f, color, level | Quad.Unlit);
    }

    // ---------------- Overlays & labels ----------------

    private void DrawOverlay(Simulation sim, int x0, int y0, int x1, int y1)
    {
        if (Overlay == Overlay.None)
            return;
        var map = sim.Map;
        var white = atlas.WhiteSprite;
        for (var y = y0; y <= y1; y++)
        for (var x = x0; x <= x1; x++)
        {
            var cell = VisibleCell(map, x, y);
            var i = map.Idx(cell);
            uint color;
            switch (Overlay)
            {
                case Overlay.Temperature:
                    color = TemperatureColor(sim.TemperatureAt(cell));
                    break;
                case Overlay.Rooms:
                {
                    var id = map.RoomId[i];
                    if (id == 0)
                        continue;
                    var room = sim.Rooms.All[id - 1];
                    if (!room.Indoors)
                        continue;
                    var h = SimRandom.Hash(id, 0, 99);
                    color = Rgba.Pack((h & 255) / 255f, (h >> 8 & 255) / 255f, (h >> 16 & 255) / 255f, 0.45f);
                    break;
                }
                case Overlay.Light:
                    color = Rgba.Pack(0, 0, 0, (1 - (map.Active[i] ? map.Light[i] : sim.Lighting.SkyLight(sim))) * 0.9f);
                    break;
                case Overlay.Regions:
                {
                    var r = map.Region[i];
                    if (r < 0)
                        continue;
                    var h = SimRandom.Hash(r, 1, 7);
                    color = Rgba.Pack((h & 255) / 255f, (h >> 8 & 255) / 255f, (h >> 16 & 255) / 255f, 0.35f);
                    break;
                }
                default:
                    continue;
            }
            Decal(white, new float3(x + 0.5f, y + 0.5f, SurfaceHeight(map, cell) + 0.04f), 1f, color, Quad.Level(cell.Z) | Quad.Unlit, translucent: true);
        }
    }

    /// <summary>Blue (cold) → green (mild) → red (hot), translucent.</summary>
    public static uint TemperatureColor(float t)
    {
        var c = t switch
        {
            < -10 => new float4(0.2f, 0.2f, 1f, 1f),
            < 10 => Lerp(new float4(0.2f, 0.2f, 1f, 1f), new float4(0.3f, 0.9f, 1f, 1f), (t + 10) / 20f),
            < 20 => Lerp(new float4(0.3f, 0.9f, 1f, 1f), new float4(0.3f, 1f, 0.3f, 1f), (t - 10) / 10f),
            < 35 => Lerp(new float4(0.3f, 1f, 0.3f, 1f), new float4(1f, 0.9f, 0.2f, 1f), (t - 20) / 15f),
            < 70 => Lerp(new float4(1f, 0.9f, 0.2f, 1f), new float4(1f, 0.15f, 0.1f, 1f), (t - 35) / 35f),
            _ => new float4(1f, 0f, 0.6f, 1f),
        };
        return Rgba.Pack(c.X, c.Y, c.Z, 0.5f);

        static float4 Lerp(float4 a, float4 b, float k) => a + (b - a) * Math.Clamp(k, 0, 1);
    }

    /// <summary>Names over creatures and stack counts, drawn with ImGui so we get a font for free.</summary>
    public void DrawLabels(Camera3D cam, Simulation sim, float time)
    {
        var dl = ImGui.GetBackgroundDrawList(ImGui.GetMainViewport());
        _floats.RemoveAll(f => time - f.Born > 1.8f);
        foreach (var f in _floats)
        {
            var t = (time - f.Born) / 1.8f;
            var p = cam.WorldToScreen(f.Pos + WorldScale.Rise(t * 0.8f));
            var c = f.Color;
            c.W *= Math.Clamp((1f - t) * 2f, 0f, 1f);
            var width = ImGui.CalcTextSize(f.Text, false, -1).X;
            Text(dl, p - new float2(width * 0.5f, 0), f.Text, (ImColor8)c);
        }
        var w = sim.World;
        var map = sim.Map;
        var cut = Math.Min(CutawayZ, map.Depth - 1);

        if (cam.Zoom >= 20)
            foreach (var e in w.Each<Stack>())
            {
                var thing = w.Get<Thing>(e);
                var count = w.Get<Stack>(e).Count;
                if (!thing.Spawned || count <= 1 || thing.Cell.Z > cut || UnderDrawnFloor(map, thing.Cell) || !Seen(e))
                    continue;
                var p = cam.WorldToScreen(Position.CenterOf(thing.Cell) + new float3(0, 0, 0.15f + SurfaceUnder(map, w, thing.Cell)));
                if (p.X < 0 || p.Y < 0 || p.X > cam.ViewportSize.X || p.Y > cam.ViewportSize.Y)
                    continue;
                Text(dl, p, $"{count}", new ImColor8(255, 255, 255, 230));
            }

        if (!ShowNames)
            return;
        foreach (var e in w.Each<Creature>())
        {
            var c = w.Get<Creature>(e);
            var pos = w.Get<Position>(e);
            if (pos.Cell.Z > cut || UnderDrawnFloor(map, pos.Cell) || !Seen(e))
                continue;
            // Under the feet (RimWorld-style), leaving the space over the head for status bubbles.
            var name = c.Dead ? $"{c.Name} (dead)" : c.Downed ? $"{c.Name} (down)" : c.Name;
            var p = cam.WorldToScreen(DrawPos(sim, e, pos.Pos)) + new float2(-ImGui.CalcTextSize(name, false, -1).X * 0.5f, 2);
            var color = Ui.Theme.U8(c.Faction == Faction.Raider ? Ui.Theme.NameHostile
                : c.Faction == Faction.Prisoner ? Ui.Theme.NamePrisoner
                : c.Def.Race == Race.Dragon ? Ui.Theme.NameDragon
                : c.Def.Race == Race.Animal ? Ui.Theme.NameAnimal
                : Ui.Theme.NamePerson);
            Text(dl, p, name, color);
        }
        DrawSpokenLines(dl, cam, sim);
    }

    /// <summary>How long (ticks) a line stays over its speaker's head.</summary>
    private const int SpokenLineTicks = SimClock.TicksPerSecond * 5;

    /// <summary>What's just been said, over the heads of those the viewer can see and could make out.</summary>
    private void DrawSpokenLines(ImDrawListPtr dl, Camera3D cam, Simulation sim)
    {
        if (Viewer == null)
            return;
        var w = sim.World;
        foreach (var line in Viewer.Heard())
        {
            if (sim.Clock.Tick - line.Tick > SpokenLineTicks || line.Speaker is not { } who || !w.IsAlive(who) || !Seen(who)
                || UnderDrawnFloor(sim.Map, w.Get<Position>(who).Cell))
                continue;
            // Above the status bubble (+0.2), so the two never sit on each other.
            var at = cam.WorldToScreen(DrawPos(sim, who, w.Get<Position>(who).Pos) + WorldScale.Rise(VisualHeight(sim, who) + 0.75f));
            var age = (sim.Clock.Tick - line.Tick) / (float)SpokenLineTicks;
            SpeechPlate(dl, at, line.Text, Math.Clamp((1f - age) * 5f, 0f, 1f), line.Volume);
        }
    }

    /// <summary>A said line on a small dark plate with a tail toward the speaker, wrapped to a few lines.</summary>
    private static void SpeechPlate(ImDrawListPtr dl, float2 tail, string text, float alpha, Volume volume)
    {
        var maxW = Narsil.Ui.Gui.S(240);
        var lines = Wrap(text, maxW, 3);
        var lineH = ImGui.GetTextLineHeight();
        var width = lines.Max(l => ImGui.CalcTextSize(l, false, -1).X);
        var pad = Narsil.Ui.Gui.S(7, 4);
        var size = new float2(width, lineH * lines.Count) + pad * 2;
        var tailH = Narsil.Ui.Gui.S(6);
        var min = new float2(tail.X - size.X * 0.5f, tail.Y - tailH - size.Y);
        var max = min + size;
        var back = Ui.Theme.U8(Ui.Theme.Panel, 0.9f * alpha);
        var rim = Ui.Theme.U8(volume == Volume.Shout ? Ui.Theme.Warn : Ui.Theme.Iron, alpha);
        dl.AddRectFilled(in min, in max, back, Narsil.Ui.Gui.S(4), ImDrawFlags.None);
        dl.AddRect(in min, in max, rim, Narsil.Ui.Gui.S(4), ImDrawFlags.None, 1f);
        var t1 = new float2(tail.X - tailH, max.Y);
        var t2 = new float2(tail.X + tailH, max.Y);
        dl.AddTriangleFilled(in t1, in t2, in tail, back);
        var ink = Ui.Theme.U8(volume == Volume.Whisper ? Ui.Theme.Dim : Ui.Theme.Text, alpha);
        for (var i = 0; i < lines.Count; i++)
        {
            var lw = ImGui.CalcTextSize(lines[i], false, -1).X;
            var p = new float2(min.X + (size.X - lw) * 0.5f, min.Y + pad.Y + i * lineH);
            dl.AddText(in p, ink, lines[i]);
        }
    }

    /// <summary>Words into lines no wider than <paramref name="width"/> pixels; past <paramref name="most"/> lines, the last is cut with an ellipsis.</summary>
    private static List<string> Wrap(string text, float width, int most)
    {
        var lines = new List<string>();
        var line = "";
        foreach (var word in text.Split(' '))
        {
            var next = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && ImGui.CalcTextSize(next, false, -1).X > width)
            {
                lines.Add(line);
                line = word;
            }
            else
                line = next;
        }
        if (line.Length > 0)
            lines.Add(line);
        if (lines.Count > most)
        {
            lines = lines.Take(most).ToList();
            lines[^1] = Narsil.Ui.Gui.Fit(lines[^1] + " …", width);
        }
        return lines.Count == 0 ? [""] : lines;
    }

    private static void Text(ImDrawListPtr dl, float2 pos, string text, ImColor8 color)
    {
        var shadow = pos + new float2(1, 1);
        dl.AddText(in shadow, new ImColor8(0, 0, 0, 200), text);
        dl.AddText(in pos, color, text);
    }
}

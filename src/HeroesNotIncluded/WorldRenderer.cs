using Brutal.ImGuiApi;
using HeroesNotIncluded.Sim.Speech;
using Narsil.Rendering;
using HeroesNotIncluded.Net;
using HeroesNotIncluded.Rendering;
using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Ecs;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded;

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
public sealed partial class WorldRenderer(TextureAtlas atlas)
{
    public Overlay Overlay { get; set; }
    public bool ShowPaths { get; set; }
    public bool ShowNames { get; set; } = true;
    public bool ShowDarkness { get; set; } = true;

    /// <summary>Off for key art (<c>--no-people</c>): the town without a soul in it, people and beasts alike.</summary>
    public bool ShowCreatures { get; set; } = true;

    /// <summary>Key art (<c>--lit-windows</c>): every window of the inn glows after dark, lamps lit or not. Presentation only.</summary>
    public bool LitWindows { get; set; }

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

    /// <summary>This frame's culling box and cutaway level (<see cref="InViewNow"/>).</summary>
    private (int X0, int Y0, int X1, int Y1, int Cut) _view;
    private Func<int3, bool>? _inView;
    private Func<int3, bool>? _shellInView;
    private Func<int3, bool>? _canSeeCell;
    private Func<Thing, (Sprite, uint)>? _artOfThing;
    private Action<float3, uint>? _padlock;
    private Func<int3, float>? _doorHeight;

    /// <summary>In this frame's culling box, at or under the cutaway, and not under a floor that's drawn.</summary>
    private bool InViewNow(int3 c) =>
        c.X >= _view.X0 && c.X <= _view.X1 && c.Y >= _view.Y0 && c.Y <= _view.Y1 && c.Z <= _view.Cut && !UnderDrawnFloor(_sim!.Map, c);

    /// <summary>
    /// <see cref="InViewNow"/> for part of a building's shell drawn apart from its walls (a door, a
    /// window's glow): a floor drawn above it (the storey over a front door) doesn't hide it from
    /// the street. What's above the level you're on in the building you're in still goes.
    /// </summary>
    private bool ShellInView(int3 c) =>
        c.X >= _view.X0 && c.X <= _view.X1 && c.Y >= _view.Y0 && c.Y <= _view.Y1 && c.Z <= _view.Cut
        && !(_sim != null && UnderOpenRoof(_sim, c) && c.Z > _openLevel);

    private PerceptionView? _omniscient;

    /// <summary>The viewer's senses, or (no viewer) one all-seeing view kept for this game rather than made anew each frame.</summary>
    private PerceptionView Sees(Simulation sim)
    {
        if (Viewer != null)
            return Viewer;
        if (_omniscient == null || !_omniscient.IsFor(sim, Entity.Null))
            _omniscient = Perception.For(sim, Entity.Null);
        return _omniscient;
    }

    private bool SeenAt(float3 at) => Viewer == null
        || Viewer.CanSeeCell(new int3((int)MathF.Floor(at.X), (int)MathF.Floor(at.Y), (int)MathF.Floor(at.Z)));

    /// <summary>Art scale: a 32-pixel sprite spans one cell.</summary>
    public const float PixelsPerCell = 32f;

    /// <summary>
    /// A customer's want over their head: the service's icon in a bubble that reddens as their
    /// patience runs out, and the quest marker when it's work for the viewer.
    /// </summary>
    public readonly record struct OrderBubble(string Icon, float TimeLeft, bool ForYou);

    /// <summary>
    /// Who's waiting for what, by customer. Filled each frame by the game from what the viewer
    /// heard (<see cref="Sim.Perception.PerceptionView.Orders"/>), never from the orders themselves.
    /// </summary>
    public Dictionary<Entity, OrderBubble> OrderBubbles { get; } = [];

    /// <summary>A label over something to do (the festival's games, the dance, the show table): set by the game each frame.</summary>
    public readonly record struct WorldMarker(float3 At, string Text, bool Done);

    /// <summary>What's marked this frame; the game fills it (presentation only, never sim state).</summary>
    public List<WorldMarker> Markers { get; } = [];

    /// <summary>
    /// Someone the player has to go to (a customer to collect from or hand back to): a tag over
    /// them, through walls, or an arrow at the screen's edge pointing their way when they're off it.
    /// </summary>
    public readonly record struct Pointer(float3 At, Entity Who, string Text);

    /// <summary>Who's pointed to this frame; the game fills it from Perception (presentation only).</summary>
    public List<Pointer> Pointers { get; } = [];

    /// <summary>Cells of the player's trade's stations they haven't been to yet: a soft ring on each.</summary>
    public List<int3> StationMarks { get; } = [];

    /// <summary>Over the player's own home (<c>VillageGame.Home</c>), a house in a bubble; null while they're inside, or have none.</summary>
    public float3? HomeMark { get; set; }

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

    public Entity PredictedEntity { get; set; } = Entity.Null;

    /// <summary>The local co-op player's predicted free movement, drawn for <see cref="PredictedEntity"/> (<see cref="FreeMovePredictor"/>).</summary>
    public FreeMovePredictor? FreePredicted { get; set; }

    /// <summary>Where a creature is drawn this frame: its sim position, smoothed between ticks (or predicted, for the local co-op player).</summary>
    public float3 DrawPos(Simulation sim, Entity e, float3 simPos)
    {
        var p = Interp?.Smoothed(sim, e, simPos) ?? simPos;
        p = FreePredicted != null && e == PredictedEntity ? FreePredicted.DrawPos(p) : p;
        // On a stair, on the tread under their feet (the sim has them at the floor until they step off).
        if (Stairs.FloorAt(sim, p) is { } floor)
            p = new float3(p.X, p.Y, floor);
        return p + new float3(0, 0, PerchLift(sim, e, p));
    }

    /// <summary>
    /// How far up someone on furniture stands (<see cref="Perched"/>): its top, all the way up while
    /// they're over it (the perch follows them along a long table), easing down over the step off
    /// till they're clear of its edge (when the sim lets the perch go), and up the same way climbing on.
    /// </summary>
    private static float PerchLift(Simulation sim, Entity e, float3 p)
    {
        if (!sim.World.TryGet<Perched>(e, out var perched))
            return 0f;
        var top = Position.CenterOf(perched.Cell);
        var d = MathF.Max(MathF.Abs(p.X - top.X), MathF.Abs(p.Y - top.Y));
        const float edge = 0.5f, clear = 0.5f + Sim.Combat.FreeMoveSystem.Radius;
        return SurfaceUnder(sim.Map, sim.World, perched.Cell) * Math.Clamp((clear - d) / (clear - edge), 0f, 1f);
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

    /// <summary>This frame's animation seconds (0 when paused), for effects that emit at a rate.</summary>
    private float _animDt;
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
        if (_looksThisFrame && _looks.TryGetValue(e.Index, out var known))
            return known.Sheet;
        var c = sim.World.Get<Creature>(e);
        // People are drawn in layers from their look (HumanLook); this is the base layer.
        var human = c.Def.Race == Race.Human;
        var look = human ? HumanLook.Of(sim, e) : default;
        var sheet = human && HumanLook.Sheet(atlas, look) is { } layered ? layered
            : atlas.TryGetSheet(SpriteNameOf(sim, e), out var plain) ? plain : null;
        if (_looksThisFrame)
            _looks[e.Index] = (look, sheet);
        return sheet;
    }

    /// <summary>
    /// Each person's look and sheet, worked out once a frame while <see cref="Draw"/> runs: the
    /// sheet, the bubble's height, the layers and a sitter's hand all want them.
    /// </summary>
    private readonly Dictionary<int, (Appearance Look, SpriteSheet? Sheet)> _looks = [];
    private bool _looksThisFrame;

    /// <summary>A person's look (<see cref="HumanLook.Of"/>), from this frame's cache while drawing.</summary>
    private Appearance LookOf(Simulation sim, Entity e)
    {
        if (_looksThisFrame)
        {
            SheetOf(sim, e);
            return _looks[e.Index].Look;
        }
        return HumanLook.Of(sim, e);
    }

    /// <summary>The creature's sprite for its life stage (the plain sheet's name; layered looks add suffixes).</summary>
    private static string SpriteNameOf(Simulation sim, Entity e)
    {
        var c = sim.World.Get<Creature>(e);
        var stage = c.Stage(sim.Clock.Tick);
        return string.IsNullOrEmpty(stage.Sprite) ? c.Def.Sprite : stage.Sprite;
    }

    /// <summary>
    /// Height of a creature's sprite above its feet, in cells (labels, bubbles). A ghost has no
    /// creature parts: it's the top of its wisp (talking as a ghost puts a bubble over it).
    /// </summary>
    public float VisualHeight(Simulation sim, Entity e)
    {
        if (!sim.World.Has<Creature>(e))
            return GhostWispHeight;
        return SheetOf(sim, e) is { } sheet ? sheet.Pivot.Y / (PixelsPerCell * sheet.Density) * 0.95f : sim.World.Get<Creature>(e).Stage(sim.Clock.Tick).Size;
    }

    /// <summary>Where a ghost's wisp tops out (<see cref="Draw"/>), for bubbles over it.</summary>
    private const float GhostWispHeight = 1.1f;

    /// <param name="time">Real seconds, for flicker that should never stop.</param>
    /// <param name="animDt">Animation seconds since last frame: real time scaled by game speed (0 when paused).</param>
    public void Draw(QuadRenderer quads, Camera3D cam, Simulation sim, Entity selected, float time, float animDt)
    {
        _cam = cam;
        _quads = quads;
        _looks.Clear();
        _looksThisFrame = true;
        try
        {
            DrawWorld(quads, cam, sim, selected, time, animDt);
        }
        finally
        {
            _looksThisFrame = false;
        }
    }

    private void DrawWorld(QuadRenderer quads, Camera3D cam, Simulation sim, Entity selected, float time, float animDt)
    {
        _anim.Advance(animDt);
        _animDt = animDt;
        _anim.LocalAim = LocalAim;
        _now = time;
        UpdateSwings(sim);
        S(TextureAtlas.White); // picks up an atlas reload before the mesher runs
        var map = sim.Map;
        var w = sim.World;
        var cut = Math.Min(CutawayZ, map.Depth - 1);

        _sim = sim;
        _mesher.OpenBuilding = HideRoofs ? (Entity.Null, int.MaxValue) : (_open, _openLevel);
        _mesher.CutHeading = CutawayWalls || (!_open.IsNull && !HideRoofs) ? WorldMesher.SnapHeading(cam.Heading) : null;
        // DoorAnimator starts with a full-height default, so this is set (once made), not ??= on its property.
        _doors.HeightOf = _doorHeight ??= c => _mesher.IsStub(_sim!, c.X, c.Y, c.Z) ? WorldMesher.StubHeight : 1f;
        if (Viewer == null)
            _canSeeCell = null;
        else if (_canSeeCell?.Target != Viewer)
            _canSeeCell = Viewer.CanSeeCell;
        _doors.CanSee = _canSeeCell;
        _mesher.Draw(quads, sim);

        // Horizontal culling box: what's visible at the bottom and top of the map, plus a margin.
        var (a0, a1) = cam.VisibleBounds(0);
        var (b0, b1) = cam.VisibleBounds(map.Depth);
        var min = float2.Min(a0, b0);
        var max = float2.Max(a1, b1);
        int x0 = Math.Max(0, (int)min.X - 2), y0 = Math.Max(0, (int)min.Y - 2);
        int x1 = Math.Min(map.Width - 1, (int)max.X + 2), y1 = Math.Min(map.Height - 1, (int)max.Y + 2);
        _view = (x0, y0, x1, y1, cut);
        var inView = _inView ??= InViewNow;

        // Buildings' lamps, hung from the beams or set on a wall: the model and a small steady flame
        // (no fuel to burn down). A wall's lamp goes when the cutaway cuts its wall down.
        foreach (var (lampCell, lamp, wallFacing) in Buildings.LitLamps(sim))
        {
            if (!InViewNow(lampCell) || lamp.Model is not { } lampModel)
                continue;
            var facing = wallFacing ?? 0;
            if (wallFacing is { } f && Footprint.Rotate(0, -1, f) is var back && IsStub(sim, lampCell + new int3(back.X, back.Y, 0)))
                continue;
            AddModel(lampModel, lampCell, facing, null, Rgba.White, Quad.Level(lampCell.Z), heightScale: WorldScale.FurnitureHeight);
            if (Viewer?.Dark(lampCell) == true)
                continue; // guttered: a ghost has it dark a while
            var flicker = 0.45f * lamp.FireSize * (1f + 0.04f * MathF.Sin(_anim.Clock * 3f + lampCell.X * 7 + lampCell.Y));
            Billboard(S("fx/fire"), Position.CenterOf(lampCell) + FireOffset(lamp, facing), flicker * 0.7f, flicker, Rgba.Pack(1f, 0.9f, 0.7f, 0.95f),
                Quad.Level(lampCell.Z) | Quad.Unlit | Quad.Glow, translucent: true);
        }

        // Lanterns standing on their own, lit for good (a quay's): the post's meshed; just the flame.
        foreach (var (lampCell, lamp) in sim.Lighting.StandingLamps(sim))
        {
            if (!InViewNow(lampCell) || Viewer?.Dark(lampCell) == true)
                continue;
            var flicker = 0.45f * lamp.FireSize * (1f + 0.04f * MathF.Sin(_anim.Clock * 3f + lampCell.X * 7 + lampCell.Y));
            Billboard(S("fx/fire"), Position.CenterOf(lampCell) + FireOffset(lamp, 0), flicker * 0.7f, flicker, Rgba.Pack(1f, 0.9f, 0.7f, 0.95f),
                Quad.Level(lampCell.Z) | Quad.Unlit | Quad.Glow, translucent: true);
        }

        // Structures that aren't meshed, construction ghosts and items.
        foreach (var e in w.Each<Thing>())
        {
            var thing = w.Get<Thing>(e);
            if (!thing.Spawned || !InViewNow(thing.Cell) || !Seen(e))
                continue;
            DrawThing(sim, e, thing);
        }
        foreach (var e in w.Each<PresetBuilding>())
        {
            DrawRaising(sim, e, inView);
            // Its shop sign, hung out over the street from its front wall (not while the wall's cut down).
            // It hangs outside the wall, so the storey above doesn't hide it, as it would something in the cell.
            if (Sim.Shops.Signs.CellOf(sim, e) is { } signAt && signAt.X >= x0 && signAt.X <= x1 && signAt.Y >= y0 && signAt.Y <= y1 && signAt.Z <= cut
                && Sim.Shops.Signs.At(sim, signAt) == e && !_mesher.IsStub(sim, signAt.X, signAt.Y, signAt.Z))
                AddModel(SignModelOf(w.Get<PresetBuilding>(e).Def), signAt, w.Get<PresetBuilding>(e).Facing, null, Rgba.White, Quad.Level(signAt.Z));
        }

        // Mess on the floor, as the viewer sees it.
        foreach (var m in Sees(sim).Messes())
            if (InViewNow(m.Cell))
                Decal(S(MessSprite(m.Look)), new float3(m.Cell.X + 0.5f, m.Cell.Y + 0.5f, m.Cell.Z + 0.02f), 0.6f + 0.35f * m.Amount,
                    Rgba.Pack(1f, 1f, 1f, 0.4f + 0.6f * m.Amount), Quad.Level(m.Cell.Z), translucent: true);

        // Ground wet from a bucket's water (it won't catch till it dries): darker, fading as it does.
        var soakTicks = SimClock.HoursToTicks(Sim.Environment.Buckets.SoakHours);
        foreach (var (i, until) in sim.Fire.SoakedCells)
        {
            var c = map.CellOf(i);
            var left = (until - sim.Clock.Tick) / (float)soakTicks;
            if (left <= 0f || !InViewNow(c) || !map.Standable(i))
                continue;
            Decal(S("ui/mess_puddle"), new float3(c.X + 0.5f, c.Y + 0.5f, c.Z + 0.015f), 1.1f,
                Rgba.Pack(0.45f, 0.62f, 0.85f, 0.15f + 0.3f * Math.Min(1f, left)), Quad.Level(c.Z), translucent: true);
        }

        DrawWindowGlow(sim);

        _doors.Draw(quads, sim, _shellInView ??= ShellInView, animDt, _artOfThing ??= t => ArtOf(t),
            _padlock ??= (at, level) => Billboard(S("props/padlock"), at - new float3(0, 0, 0.1f), 0.22f, 0.22f, Rgba.White, level));

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

        // Marks mods put on cells for anyone to see (a miner's chalk on a face), flat on the cell's top.
        foreach (var mark in Mods.GameMods.CellMarks(sim))
        {
            var (x, y, z) = (mark.Cell.X, mark.Cell.Y, mark.Cell.Z);
            if (!map.InBounds(mark.Cell) || x < x0 || x > x1 || y < y0 || y > y1 || z < 1 || z > cut)
                continue;
            var floor = map.IsSolid(map.Idx(mark.Cell)) ? z + 1 : z;
            if (mark.Floating)
            {
                var bob = MathF.Sin(time * 2.2f + (x * 7 + y * 13) * 0.37f) * 0.06f;
                Billboard(S(mark.Sprite), new float3(x + 0.5f, y + 0.5f, floor + 1.1f + bob), mark.Size, mark.Size, Rgba.Pack(mark.Colour), Quad.Level(z) | Quad.Unlit);
            }
            else
                Decal(S(mark.Sprite), new float3(x + 0.5f, y + 0.5f, floor + 0.035f), mark.Size, Rgba.Pack(mark.Colour), Quad.Level(z) | Quad.Unlit, translucent: true);
        }

        // Signs of the dead, as perceived: a chill's breath hangs in the air.
        foreach (var sign in Sees(sim).Signs())
        {
            if (sign.Kind != Sim.Afterlife.HauntKind.Chill || !InViewNow(sign.Cell))
                continue;
            var t = (sim.Clock.Tick - sign.Tick) * 0.03f;
            for (var k = 0; k < 3; k++)
            {
                var drift = new float3(0.25f * MathF.Sin(t + k * 2.1f), 0.25f * MathF.Cos(t * 0.7f + k * 1.3f), 0.5f + 0.15f * k + 0.1f * MathF.Sin(t * 1.3f + k));
                Billboard(S("fx/breath"), Position.CenterOf(sign.Cell) + drift, 0.6f, 0.45f, Rgba.Pack(0.85f, 0.92f, 1f, 0.35f),
                    Quad.Level(sign.Cell.Z), translucent: true);
            }
        }

        // The dead, seen only by the dead: a pale wisp each, bobbing where it floats.
        if (Viewer is { Ghostly: true })
            foreach (var e in w.Each<Sim.Afterlife.Ghost>())
            {
                var p = w.Get<Position>(e);
                if (!InViewNow(p.Cell) || !Seen(e))
                    continue;
                var bob = 0.12f * MathF.Sin(sim.Clock.Tick * 0.05f + e.Index);
                var mine = e == Viewer.Viewer;
                var tint = mine ? Rgba.Pack(0.7f, 0.85f, 1f, 0.6f) : Rgba.Pack(0.65f, 0.8f, 1f, 0.45f);
                Billboard(S("fx/breath_core"), p.Pos + new float3(0, 0, 0.55f + bob), 0.45f, 0.6f, tint, Quad.Level(p.Cell.Z) | Quad.Unlit, translucent: true);
                Billboard(S("fx/breath"), p.Pos + new float3(0, 0, 0.4f + bob), 0.8f, 1.1f, tint, Quad.Level(p.Cell.Z) | Quad.Unlit, translucent: true);
            }

        // Creatures
        foreach (var e in w.Each<Creature>())
        {
            var p = w.Get<Position>(e);
            if (!ShowCreatures || !InViewNow(p.Cell) || !Seen(e))
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
            _heldAt = null;
            if (SheetOf(sim, e) is { } sheet)
            {
                DrawAnimated(sim, e, sheet, p, tint, level);
            }
            else if (w.Has<Carried>(e))
            {
                // Slung over the carrier's shoulder.
                Decal(sprite, p.Pos + new float3(0, 0, 0.75f), size * 0.8f, tint, level);
            }
            else if (c.Dead || c.Downed || mind.Asleep || mind.Job is { LiesDown: true })
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
            // A stool held to throw (Hurling): up in both hands, raised higher as it's drawn back.
            if (w.TryGet<Sim.Combat.Hefting>(e, out var heft) && !c.Downed && !c.Dead)
            {
                var back = Sim.Combat.Hurling.Charge(sim, e);
                Billboard(S(heft.Def.Sprite), p.Pos + WorldScale.Rise(size * (0.75f + 0.35f * back)) - cam.Right * (0.15f * back), 0.55f, 0.55f, Rgba.White, level);
            }
            var carried = w.Get<Carrying>(e).Item;
            if (!carried.IsNull && w.IsAlive(carried) && w.Get<Thing>(carried).Def is { Pushed: true, Model: { } pushedModel } pushedDef)
                DrawPushed(sim, e, carried, pushedDef, pushedModel, p.Pos, level);
            else if (!carried.IsNull && w.IsAlive(carried))
            {
                if (_heldAt is { } hand)
                    Billboard(S(w.Get<Thing>(carried).Def.Sprite), hand - _cam.Up * 0.12f, 0.3f, 0.3f, Rgba.White, level);
                else
                    Billboard(S(w.Get<Thing>(carried).Def.Sprite), p.Pos + cam.Right * 0.3f + WorldScale.Rise(size * 0.45f), 0.5f, 0.5f, Rgba.White, level);
            }
            if (Sees(sim).EmoteOf(e) is { } emote)
                DrawEmote(sim, emote, p.Pos + WorldScale.Rise(VisualHeight(sim, e) + 0.2f), level, time);
            else if (OrderBubbles.TryGetValue(e, out var wants) && !c.Dead && !c.Downed && !w.Has<Carried>(e))
                DrawOrderBubble(wants, p.Pos + WorldScale.Rise(VisualHeight(sim, e) + 0.2f), level, time + e.Index);
            else if (ShowBubbles && c.Faction == Faction.Village && !c.Dead && !w.Has<Carried>(e) && BubbleIcon(sim, e) is { } icon)
                DrawBubble(icon, p.Pos + WorldScale.Rise(VisualHeight(sim, e) + 0.2f), level, time + e.Index);
            else if (w.Has<HuntOrder>(e) && !c.Dead)
                DrawBubble("ui/icon_hunt", p.Pos + WorldScale.Rise(VisualHeight(sim, e) + 0.2f), level, time + e.Index);
        }

        // Fire: two flickering flames per burning cell, offset a little so a burning area looks full,
        // and the life of it (FireLife): tongues licking up, embers, shimmer and black smoke.
        foreach (var i in sim.Fire.BurningTiles)
        {
            var t = map.CellOf(i);
            if (InViewNow(t))
            {
                DrawFlames(i, t, map.Fire[i], time);
                FireLife(t, map.Fire[i]);
            }
            // Seen from the street, a fire within shows over its roof (whatever floor's drawn over it).
            if (map.RoomId[i] > 0 && _burningRooms.TryGetValue(map.RoomId[i], out var roof) && roof > t.Z
                && t.X >= _view.X0 && t.X <= _view.X1 && t.Y >= _view.Y0 && t.Y <= _view.Y1)
                FireOverRoof(t, map.Fire[i], roof);
        }
        DrawDousedDragons(sim);
        DrawChimneySmoke(sim);

        DrawSmoke(sim, inView, time);
        DrawSplashes(sim, inView, time);
        DrawBreaths(sim, time);
        DrawShots(sim, time);
        DrawStuckArrows(sim);
        DrawBunting(sim);
        DrawWindups(sim);
        DrawAimLine();
        DrawSwings(sim);
        DrawInhaling(sim);
        DrawBlowWords(sim);
        EmitSpeech(sim, inView);
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

        // A newcomer's stations: a parchment ring, breathing slowly, on the floor under each.
        foreach (var cell in StationMarks)
        {
            if (!InViewNow(cell))
                continue;
            // Wide enough that the ring's corners show past the furniture standing in it.
            var ring = 1.8f * (1f + 0.08f * MathF.Sin(time * 2.5f));
            Decal(S("ui/selection"), Position.CenterOf(cell) + new float3(0, 0, 0.04f), ring,
                Rgba.Pack(Ui.Theme.Parchment.X, Ui.Theme.Parchment.Y, Ui.Theme.Parchment.Z), Quad.Level(cell.Z) | Quad.Unlit, translucent: true);
        }

        // Your home: a house in a bubble over the roof, through anything in the way, so it's found from across town.
        if (HomeMark is { } home)
            DrawBubble("ui/icon_home", home, Quad.Always, time,
                Rgba.Pack(Ui.Theme.Parchment.X, Ui.Theme.Parchment.Y, Ui.Theme.Parchment.Z, 0.95f), 1.4f);

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
    /// <remarks>
    /// The arm is wrought iron on a plate at the wall, held up by a stepped brace from below, its end
    /// curled over in a scroll (art/reference/inn3.webp); the board hangs from two rings.
    /// </remarks>
    private static readonly List<ModelBox> SignModel =
    [
        new() { Min = [0.42f, 0.9f, 0.6f], Max = [0.58f, 0.95f, 0.96f], Top = "furniture/hoop", Side = "furniture/hoop" },          // the wall plate
        new() { Min = [0.47f, 0.92f, 0.86f], Max = [0.53f, 1.86f, 0.9f], Top = "furniture/hoop", Side = "furniture/hoop" },          // the arm
        new() { Min = [0.475f, 0.95f, 0.64f], Max = [0.525f, 1.1f, 0.7f], Top = "furniture/hoop", Side = "furniture/hoop" },         // the brace, stepping up
        new() { Min = [0.475f, 1.1f, 0.7f], Max = [0.525f, 1.25f, 0.76f], Top = "furniture/hoop", Side = "furniture/hoop" },
        new() { Min = [0.475f, 1.25f, 0.76f], Max = [0.525f, 1.4f, 0.82f], Top = "furniture/hoop", Side = "furniture/hoop" },
        new() { Min = [0.475f, 1.4f, 0.82f], Max = [0.525f, 1.52f, 0.86f], Top = "furniture/hoop", Side = "furniture/hoop" },
        new() { Min = [0.475f, 1.86f, 0.86f], Max = [0.525f, 1.92f, 0.98f], Top = "furniture/hoop", Side = "furniture/hoop" },       // the scroll at its end
        new() { Min = [0.475f, 1.8f, 0.96f], Max = [0.525f, 1.88f, 1.0f], Top = "furniture/hoop", Side = "furniture/hoop" },
        new() { Min = [0.48f, 1.04f, 0.8f], Max = [0.52f, 1.08f, 0.86f], Top = "furniture/hoop", Side = "furniture/hoop" },          // the rings
        new() { Min = [0.48f, 1.72f, 0.8f], Max = [0.52f, 1.76f, 0.86f], Top = "furniture/hoop", Side = "furniture/hoop" },
        new() { Min = [0.475f, 1.0f, 0.28f], Max = [0.525f, 1.8f, 0.8f], Top = "furniture/oak_planks", Side = "props/sign_board", Front = "furniture/oak_planks" },
    ];

    private readonly Dictionary<string, List<ModelBox>> _signModels = [];

    /// <summary>The sign with this building's face painted on it (<see cref="BuildingDef.SignFace"/>), or the plain one.</summary>
    private List<ModelBox> SignModelOf(BuildingDef def)
    {
        if (def.SignFace is not { } face)
            return SignModel;
        if (!_signModels.TryGetValue(face, out var model))
        {
            var board = SignModel[^1];
            _signModels[face] = model = [.. SignModel[..^1], new() { Min = board.Min, Max = board.Max, Top = board.Top, Side = face, Front = board.Front }];
        }
        return model;
    }

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
        // Struck a moment ago: the whole figure flares white (Impact).
        if (_flashes.TryGetValue(e.Index, out var flash))
        {
            if (_now < flash)
                level |= Quad.Unlit | Quad.Glow;
            else
                _flashes.Remove(e.Index);
        }
        var map = sim.Map;
        var c = w.Get<Creature>(e);
        var mind = w.Get<Mind>(e);
        var pose = _anim.Pose(sim, e, sheet, _cam, Sees(sim).EmoteOf(e));
        var color = c.Dead ? Rgba.Pack(0.5f, 0.5f, 0.5f, 0.9f) : c.Downed ? Rgba.Pack(0.85f, 0.7f, 0.7f) : tint;
        // Humans are drawn lying flat (top-down art); dragons and animals lie down in their side view.
        var flat = c.Def.Race == Race.Human;
        var lying = pose.Tag is "sleep" or "downed";

        if (w.Has<Carried>(e))
        {
            var f = (CreatureAnimator.Resolve(sheet, "downed", out _)?.Frames[0]) ?? pose.Frame;
            FrameDecal(sheet, f, p.Pos + new float3(0, 0, 0.75f), 0.8f, color, level);
            var k = 0;
            foreach (var (layer, layerColor) in FrameLayers(sim, e, color))
                if (HumanLook.LayerFrame(sheet, layer, f) is { } lf)
                    FrameDecal(layer, lf, p.Pos + new float3(0, 0, 0.75f + ++k * LayerStep), 0.8f, layerColor, level);
            return;
        }

        if (lying && flat)
        {
            var center = p.Pos;
            var bed = (mind.Asleep || mind.Job is { LiesDown: true }) && mind.Job is { } job && !job.Focus.IsNull && w.IsAlive(job.Focus) && w.Has<Thing>(job.Focus)
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
            foreach (var (layer, layerColor) in FrameLayers(sim, e, lyingColor))
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
        // Sat on a stool: drawn a little toward the viewer, so they sit in front of its seat rather
        // than in it, with what they're drinking in hand (lifted for a sip).
        if (pose.Tag == "sit")
        {
            var toward = new float3(-_cam.Forward.X, -_cam.Forward.Y, 0f);
            var len = MathF.Sqrt(toward.X * toward.X + toward.Y * toward.Y);
            if (len > 1e-4f)
                feet += toward * (SitForward / len);
            var sip = pose.Anim is { Frames.Length: > 2 } a && pose.Frame.Index == a.Frames[2].Index;
            var hand = HumanLook.BuildPoint(LookOf(sim, e), sip ? SipHand : LapHand); // a dwarf's lap is lower
            var unit = sheet.FrameSize.Y / 60f / (PixelsPerCell * sheet.Density); // the generator's 40x60 design units
            _heldAt = feet + _cam.Right * ((hand.X - 20f) * unit * pose.Facing) + _cam.Up * ((58f - hand.Y) * unit) - _cam.Forward * 0.02f;
        }
        if (pose.Tag == "glide")
            feet.Z += MathF.Sin(_anim.Clock * 2f + e.Index) * 0.05f;
        // Dancing (round the festival's bonfire, to a musician): a hop to every beat and a sway, the
        // dance frames (CreatureAnimator) keeping the same beat.
        if (CreatureAnimator.DanceTempoOf(sim, e) is var danceTempo and >= 0f)
        {
            var beat = _anim.DanceBeat(e, danceTempo);
            feet.Z += MathF.Abs(MathF.Sin(beat)) * 0.12f;
            feet += _cam.Right * (MathF.Sin(beat * 0.5f) * 0.05f);
        }
        // Cheering a toast: quick hops, the arms pumped at the top of each (the cheer frames).
        if (sim.Festival.CheeredAt(e) is var cheered and >= 0 && sim.Clock.Tick - cheered is var since and >= 0 and < Sim.Round.FestivalSystem.CheerTicks)
            feet.Z += MathF.Abs(MathF.Sin(since / (float)SimClock.TicksPerSecond * CreatureAnimator.CheerRate)) * 0.16f;
        if (!airborne && !c.Dead)
        {
            // A soft contact shadow grounds standing figures (part of the diorama look).
            var shadowSize = Math.Clamp(sheet.FrameSize.X / (PixelsPerCell * sheet.Density) * 0.55f, 0.35f, 1.6f);
            Decal(S("fx/smoke"), new float3(feet.X, feet.Y, GroundBelow(map, p.Cell) + 0.02f), shadowSize, Rgba.Pack(0, 0, 0, 0.32f), level, translucent: true);
        }
        // A dodge's tumble turns about their middle, as a flyer's bank does.
        var tumbling = w.TryGet<Sim.Combat.FreeMover>(e, out var free) && sim.Clock.Tick < free.DashUntil;
        // A kick of dust where it leaves the ground, a smaller puff where it comes down.
        if (tumbling || _dashDust.ContainsKey(e.Index))
        {
            var ground = new float3(feet.X, feet.Y, GroundBelow(map, p.Cell) + 0.05f);
            if (tumbling && (!_dashDust.TryGetValue(e.Index, out var dust) || dust != free.DashUntil))
            {
                _dashDust[e.Index] = free.DashUntil;
                _particles.Dust(ground, 6, 0.35f, Dirt, 0.3f);
            }
            else if (!tumbling)
            {
                _dashDust.Remove(e.Index);
                _particles.Dust(ground, 3, 0.25f, Dirt, 0.22f);
            }
        }
        // Weapon sheathed (free movement), it's slung across their back: behind them as they face
        // us, over their back (and hair, and cloak) as they walk away.
        var slungDef = w.Has<Sim.Combat.FreeMover>(e) && !free.Drawn && !tumbling && !c.Dead && !c.Downed
            && w.TryGet<Equipment>(e, out var slung) && w.IsAlive(slung.Weapon) && w.Get<Thing>(slung.Weapon).Def is { Held: not null } sd ? sd : null;
        var backToUs = pose.View is "up" or "upside";
        if (slungDef != null && !backToUs)
            DrawSlung(S(slungDef.Sprite), feet + _cam.Forward * 0.03f, SlungSize(slungDef), pose.Facing, color, level);
        BillboardFrame(sheet, pose.Frame, feet, pose.Facing, pose.Roll, color, level, aboutCenter: airborne || tumbling);
        var nudge = 0;
        foreach (var (layer, layerColor) in FrameLayers(sim, e, color))
            if (HumanLook.LayerFrame(sheet, layer, pose.Frame) is { } lf)
                BillboardFrame(layer, lf, feet - _cam.Forward * (++nudge * LayerStep), pose.Facing, pose.Roll, layerColor,
                    HumanLook.IsPieceLayer(layer) ? level | PieceFlags(layerColor) : level, aboutCenter: airborne || tumbling);
        if (slungDef != null && backToUs) // the strap over the other shoulder, seen from behind
            DrawSlung(S(slungDef.Sprite), feet - _cam.Forward * ((nudge + 1) * LayerStep + 0.01f), SlungSize(slungDef), -pose.Facing, color, level);

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
                // Drawing the heat in: specks pulled into its jaws, the air over them going thick.
                var big = Math.Clamp(sheet.FrameSize.X / 96f, 0.5f, 1.5f);
                for (var n = _particles.PerFrame(26f * big, _animDt); n > 0; n--)
                    _particles.Inhale(mouth, new float4(1f, 0.75f + 0.2f * pulse, 0.45f, 0.9f));
                for (var n = _particles.PerFrame(9f, _animDt); n > 0; n--)
                    _particles.HeatHaze(mouth + WorldScale.Rise(0.15f), big);
            }
        }

        if (lying && mind.Asleep && _anim.Every(e, 1.6f))
            _particles.Zzz(feet + _cam.Up * (sheet.Pivot.Y * 0.5f / (PixelsPerCell * sheet.Density)) + _cam.Right * (0.3f * pose.Facing));

        var width = sheet.FrameSize.X / (PixelsPerCell * sheet.Density);
        if (pose.TagStarted && pose.Tag is "takeoff" or "land")
            _particles.Dust(new float3(feet.X, feet.Y, GroundBelow(map, p.Cell)), 10, width * 0.6f, Dirt, 0.25f + width * 0.15f);

        if (pose.EventFired && pose.Tag == "work" && mind.Job is { } work)
        {
            float3? at = !work.Focus.IsNull && w.IsAlive(work.Focus) ? Position.CenterOf(sim.LocationOf(work.Focus))
                : work.FocusCell is { } fc ? Position.CenterOf(fc) : null;
            if (at is { } site)
                _particles.Dust(site + (feet - site) * 0.35f + new float3(0, 0, 0.3f), 3, 0.5f, Dirt, 0.3f);
        }
        if (!c.Dead && !c.Downed)
            DrawWorkLife(sim, e);

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

    /// <summary>How far toward the viewer someone sat on a stool is drawn (cells).</summary>
    private const float SitForward = 0.1f;

    /// <summary>
    /// Where the cup is in a sitting frame, in tools/chibi_people.py's design units (40x60, the feet
    /// at 20,58): at the lap, and lifted to the mouth for a sip.
    /// </summary>
    private static readonly float2 LapHand = new(25f, 47.2f), SipHand = new(23f, 36.5f);

    /// <summary>
    /// A sheathed weapon (<see cref="Sim.Combat.FreeMover.Drawn"/> off) across someone's back: its
    /// item art upright to the camera, tilted over a shoulder, at <paramref name="at"/>: a hair
    /// behind them as they face us (the body hides most of it and the hilt and tip show), over
    /// their back as they face away.
    /// </summary>
    private void DrawSlung(in Sprite sprite, float3 at, float size, int facing, uint color, uint level)
    {
        // Item art runs corner to corner, grip at the bottom left: upright, it leans one way; mirrored with the body.
        var r = _cam.Right * (size * (facing >= 0 ? 1f : -1f));
        var u = _cam.Up * size;
        var middle = at + _cam.Up * 0.62f;
        _quads.Add(new Quad
        {
            Origin = middle - r * 0.5f + u * 0.5f,
            AxisU = r,
            AxisV = -u,
            Flags = level | Quad.Billboard | Quad.Upright,
            Color = color,
            Ao = Quad.NoAo,
            Uv = sprite.Uv,
        });
    }

    /// <summary>How big a slung weapon is drawn: the long ones reach past the head.</summary>
    private static float SlungSize(ThingDef def) => def.Held is "spear" or "longsword" or "staff" or "crossbow" ? 1.05f : 0.75f;

    /// <summary>Where DrawAnimated put someone's hand this frame, if it moved it (sitting); else the held item goes beside them.</summary>
    private float3? _heldAt;

    public IEnumerable<(SpriteSheet Sheet, uint Color)> LookLayers(Simulation sim, Entity e, uint color) =>
        sim.World.Get<Creature>(e).Def.Race switch
        {
            // Mid-swing the weapon stays in hand: the swing frames carry it (DrawSwings adds only the smear).
            Race.Human => HumanLook.Layers(atlas, HumanLook.Of(sim, e), color, HumanLook.WornOf(sim, e), HeldOf(sim, e)),
            _ => [],
        };

    private readonly List<(SpriteSheet Sheet, uint Color)> _layers = new(16);
    private readonly List<HumanLook.WornItem> _worn = new(8);

    /// <summary>
    /// <see cref="LookLayers"/> for drawing, into lists kept from frame to frame. Good until the next
    /// call: each person's layers are drawn before the next person's are asked for.
    /// </summary>
    private List<(SpriteSheet Sheet, uint Color)> FrameLayers(Simulation sim, Entity e, uint color)
    {
        if (sim.World.Get<Creature>(e).Def.Race != Race.Human)
        {
            _layers.Clear();
            return _layers;
        }
        return HumanLook.LayersInto(_layers, atlas, LookOf(sim, e), color, HumanLook.WornInto(_worn, sim, e), HeldOf(sim, e));
    }

    /// <summary>
    /// What a person has in hand: the tool their job works with (the axe while felling, the pick
    /// while mining), else their tongs with the piece in them (WorldRenderer.Smithy), else their
    /// weapon; nothing when they're down.
    /// </summary>
    public HumanLook.HeldItem? HeldOf(Simulation sim, Entity e)
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
        // The striker: a sledge in hand (the smith's hammer, for now: there's no sledge to carry).
        if (session is Sim.Work.SledgeSession && sim.Defs.AllThings.FirstOrDefault(t => t.Name == "Hammer") is { } hammerDef && HumanLook.HeldOf(hammerDef) is { } sledge)
            return sledge;
        if (TongsOf(sim, e, session) is { } tongs)
            return tongs;
        // A player's weapon's only in hand drawn, in combat mode (Combat.FreeMover).
        if (w.TryGet<Equipment>(e, out var eq) && w.IsAlive(eq.Weapon) && !(w.TryGet<Sim.Combat.FreeMover>(e, out var free) && !free.Drawn)
            && !Sim.Combat.HouseRules.Sheathed(sim, e) // the inn's rule, or a brawler's fists: it stays sheathed
            && HumanLook.HeldOf(w.Get<Thing>(eq.Weapon).Def) is { } weapon)
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

    /// <summary>How far ahead of its pusher a pushed thing's middle goes (its handles' ends just before them).</summary>
    private const float PushedAhead = 0.85f;

    /// <summary>Each pusher's last place and the way they're going (smoothed), so what's pushed turns with them.</summary>
    private readonly Dictionary<Entity, (float3 Last, float2 Heading)> _pushers = [];

    /// <summary>
    /// A thing pushed along (<see cref="ThingDef.Pushed"/>: a wheelbarrow), drawn as its model ahead of <paramref name="who"/>,
    /// its front (the model's +y) the way they're going: built as at a cell, then turned and moved there.
    /// </summary>
    private void DrawPushed(Simulation sim, Entity who, Entity item, ThingDef def, IReadOnlyList<ModelBox> model, float3 feet, uint level)
    {
        if (_pushers.Count > 64)
            _pushers.Clear();
        var (last, heading) = _pushers.TryGetValue(who, out var was) ? was : (feet, new float2(0f, 1f));
        var step = new float2(feet.X - last.X, feet.Y - last.Y);
        var moved = MathF.Sqrt(step.X * step.X + step.Y * step.Y);
        if (moved > 0.005f)
        {
            var turned = heading * 0.8f + step / moved * 0.2f;
            var len = MathF.Sqrt(turned.X * turned.X + turned.Y * turned.Y);
            if (len > 1e-4f)
                heading = turned / len;
        }
        _pushers[who] = (feet, heading);
        var right = new float2(heading.Y, -heading.X);
        var centre = new float3(feet.X + heading.X * PushedAhead, feet.Y + heading.Y * PushedAhead, feet.Z);
        float3 Turn(float3 v) => new(v.X * right.X + v.Y * heading.X, v.X * right.Y + v.Y * heading.Y, v.Z);
        var material = sim.World.Get<Thing>(item).Material;
        _modelQuads.Clear();
        PropModel.Build(_modelQuads, model, default, 0, name => _materials.Resolve(name, material) is var (n, t) ? (S(n), t) : default,
            Rgba.White, level, count: Mods.GameMods.ModelCountOf(sim, item) ?? 1);
        foreach (var q in _modelQuads)
        {
            var turned = q;
            turned.Origin = centre + Turn(q.Origin - new float3(0.5f, 0.5f, 0f));
            turned.AxisU = Turn(q.AxisU);
            turned.AxisV = Turn(q.AxisV);
            _quads.Add(turned, false);
        }
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
    /// <summary>A burning cell's flames (map index <paramref name="i"/>, burning <paramref name="f"/> hard): two flickering, offset a little so a burning area looks full.</summary>
    /// <summary>Embers a second off a modelled fire of size 1 (<see cref="ThingDef.FireSize"/>, squared), and pops of sparks.</summary>
    private const float EmbersPerSecond = 2.2f, PopsPerSecond = 0.35f, LicksPerSecond = 3f, ChimneyPuffsPerSecond = 2.5f;

    /// <summary>A modelled fire this big (<see cref="ThingDef.FireSize"/>) or more is out in the open: its flames lick up past it.</summary>
    private const float OpenFireSize = 1f;

    /// <summary>
    /// A burning cell's life (<paramref name="f"/>, its fire 0..1), as the dragon's fire has: tongues
    /// of flame licking up, embers lifted on the heat, the air shimmering over a fierce one, and black
    /// smoke rolling off it. Metered by the heat, so a street alight stays affordable.
    /// </summary>
    /// <summary>
    /// A fire under a roof (<paramref name="ridge"/>: how high its ridge is), seen from outside: black
    /// smoke pouring up off it and embers whirling out over it, drawn as the roof is (gone with it).
    /// </summary>
    private void FireOverRoof(int3 t, float f, float ridge)
    {
        var over = new float3(t.X + 0.5f, t.Y + 0.5f, ridge + 0.2f);
        _particles.OverRoof = Math.Min(t.Z, _view.Cut);
        for (var n = _particles.PerFrame(0.7f + 1.1f * f, _animDt); n > 0; n--)
            _particles.RoofSmoke(over, f);
        for (var n = _particles.PerFrame(0.5f + 1.5f * f, _animDt); n > 0; n--)
            _particles.DriftingEmbers(over, 1, 0.45f);
        _particles.OverRoof = -1;
    }

    private void FireLife(int3 t, float f)
    {
        var at = new float3(t.X + 0.5f, t.Y + 0.5f, t.Z);
        for (var n = _particles.PerFrame(2f + 5f * f, _animDt); n > 0; n--)
            _particles.FlameLick(at, f);
        for (var n = _particles.PerFrame(0.4f + 1.8f * f, _animDt); n > 0; n--)
            _particles.DriftingEmbers(at + new float3(0, 0, 0.3f), 1);
        if (f > 0.5f)
            for (var n = _particles.PerFrame(3f * (f - 0.5f), _animDt); n > 0; n--)
                _particles.HeatHaze(at + new float3(0, 0, 0.6f));
        if (f > 0.3f)
            for (var n = _particles.PerFrame(0.9f * f, _animDt); n > 0; n--)
                _particles.BreathSmoke(at + new float3(0, 0, 0.7f), 0.8f + 0.5f * f);
    }

    /// <summary>
    /// Smoke from every lit hearth's chimney (<see cref="ThingDef.SmokeAt"/>) on screen, thicker the
    /// better it's fed. Drawn as the roof is: seen with the roofs on, when the hearth itself is hidden
    /// under them, and gone with them. Where there's a storey over it, it rises from the building's
    /// chimney stack there instead (BuildingKit.Chimney).
    /// </summary>
    private void DrawChimneySmoke(Simulation sim)
    {
        var w = sim.World;
        var map = sim.Map;
        foreach (var e in w.Each<Fueled>())
        {
            if (!w.TryGet<Thing>(e, out var thing) || thing.Def is not { Model: not null, SmokeAt: [var sx, var sy, var sz], Building.Fuel: { } props } def
                || !sim.Burning(e))
                continue;
            var cell = thing.Cell;
            if (cell.X < _view.X0 - 2 || cell.X > _view.X1 + 2 || cell.Y < _view.Y0 - 2 || cell.Y > _view.Y1 + 2)
                continue;
            float3? stack = null;
            var above = cell + new int3(0, 0, 1);
            if (map.InBounds(above) && map.Structure[map.Idx(above)].IsNull)
            {
                var o = Footprint.Rotate(sx - 0.5f, sy - 0.5f, thing.Facing);
                stack = Position.CenterOf(cell) + new float3(o.X, o.Y, sz);
            }
            else if (BuildingKit.Of(sim, e) is { } kitted
                && BuildingKit.Chimney(kitted.Kit, kitted.Building, new int3(cell.X, cell.Y, kitted.Building.Anchor.Z + kitted.Building.Def.Storeys)) is { } chimney)
                stack = chimney.Top;
            if (stack is not { } from)
                continue;
            var frac = Math.Clamp(w.Get<Fueled>(e).Fuel / props.Capacity, 0, 1);
            _particles.OverRoof = Math.Min(cell.Z, _view.Cut);
            for (var n = _particles.PerFrame(ChimneyPuffsPerSecond * (0.5f + 0.5f * frac), _animDt); n > 0; n--)
                _particles.WoodSmoke(from, frac);
            _particles.OverRoof = -1;
        }
    }

    /// <summary>How many times each dragon had been soaked when last drawn, to see a new soaking.</summary>
    private readonly Dictionary<int, int> _soakings = [];

    /// <summary>
    /// A dragon soaked by a bucket (<see cref="Sim.Ai.Doused"/>): a great plume of steam bursts off it
    /// as the message says; then, while its fire's out, steam curls from its jaws instead of smoke.
    /// </summary>
    private void DrawDousedDragons(Simulation sim)
    {
        var w = sim.World;
        foreach (var e in w.Each<Sim.Ai.Doused>())
        {
            var d = w.Get<Sim.Ai.Doused>(e);
            var seen = Seen(e) && w.Has<Position>(e);
            if (_soakings.TryGetValue(e.Index, out var before) && d.Times > before && seen
                && sim.Clock.Tick - (d.Until - (long)(Sim.Ai.DragonBrain.DousedSeconds * SimClock.TicksPerSecond)) < SimClock.TicksPerSecond)
            {
                var body = DrawPos(sim, e, w.Get<Position>(e).Pos) + WorldScale.Rise(0.6f);
                _particles.SteamPlume(body, 1.6f);
            }
            _soakings[e.Index] = d.Times;
            if (seen && d.Until > sim.Clock.Tick && _anim.TryGetMouth(e, out var mouth))
                for (var n = _particles.PerFrame(3f, _animDt); n > 0; n--)
                    _particles.SteamWisp(mouth - new float3(0, 0, 0.05f));
        }
    }

    private void DrawFlames(int i, int3 t, float f, float time)
    {
        var fireSprite = S("fx/fire");
        for (var k = 0; k < 2; k++)
        {
            var h = SimRandom.Hash(i, k, 5);
            var jitter = new float3((h & 255) / 255f - 0.5f, (h >> 8 & 255) / 255f - 0.5f, 0) * 0.5f;
            var flicker = 0.8f + 0.2f * MathF.Sin(time * (11f + k * 4f) + i * 1.7f);
            var s = (0.45f + 0.6f * f) * flicker * (k == 0 ? 1f : 0.7f);
            Billboard(fireSprite, new float3(t.X + 0.5f, t.Y + 0.5f, t.Z) + jitter, s, s * 1.25f, Rgba.Pack(1, 1, 1, 0.9f), Quad.Level(t.Z) | Quad.Unlit | Quad.Glow, translucent: true);
        }
    }

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
        if (_breathFlash.Count > 0)
            foreach (var k in _breathFlash.Keys.ToList())
                if (!sim.RecentBreaths.Contains(k))
                    _breathFlash.Remove(k);
        DrawScorches(sim, cut);
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
            var body = Rgba.Pack(1f, 0.86f, 0.62f, alpha * 0.8f); // warm, not white: bloom bleaches white to nothing
            var mouth = !ev.Source.IsNull && _anim.TryGetMouth(ev.Source, out var drawn) ? drawn : ev.Mouth;
            var reach = Math.Min(1f, 0.15f + age * 5f);
            if (first && sim.Clock.Tick - ev.Tick < Sim.Environment.Breath.ShowTicks / 2)
                foreach (var end in ev.RayEnds)
                {
                    // Where it strikes: flame thrown out along the ground, embers, a black billow, a scorch left behind.
                    _particles.FlameBurst(end, flame, 7);
                    _particles.Embers(end, 6);
                    for (var k = 0; k < 2; k++)
                        _particles.BreathSmoke(end, 1.3f);
                    Scorch(end);
                }
            // The moment it comes: a white-hot flash at the jaws.
            var flashAge = (_anim.Clock - (_breathFlash.TryGetValue(ev, out var f0) ? f0 : _breathFlash[ev] = _anim.Clock)) / 0.18f;
            if (flashAge < 1f && mouth.Z <= cut + 1)
            {
                var fs = 1.4f * (1f - flashAge * 0.5f);
                Billboard(fire, mouth - WorldScale.Rise(fs * 0.5f), fs, fs, Rgba.Pack(1f, 0.95f, 0.85f, 0.9f * (1f - flashAge)), Quad.Level(Math.Max(0, (int)mouth.Z)) | Quad.Unlit | Quad.Glow, translucent: true);
            }
            // While it pours out, a stream of flame along each ray, cooling as it goes, and smoke rolling off behind.
            var pouring = age < 0.65f;
            for (var r = 0; r < ev.RayEnds.Count; r++)
            {
                var d = ev.RayEnds[r] - mouth;
                var len = MathF.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z);
                if (pouring)
                {
                    for (var n = _particles.PerFrame(Math.Min(36f, 14f + len * 2f), _animDt); n > 0; n--)
                        _particles.FlameJet(mouth, ev.RayEnds[r], flame, 0.12f * len);
                    for (var n = _particles.PerFrame(5f + len * 0.6f, _animDt); n > 0; n--)
                        _particles.BreathSmoke(mouth + d * _particles.Range(0.35f, 1f) * reach + WorldScale.Rise(0.2f));
                }
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
                    var size = (0.24f + 0.5f * t) * flicker; // a hot core: the particles give the jet its body
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

    /// <summary>When each breath's flash was first drawn (animation clock).</summary>
    private readonly Dictionary<Sim.Environment.BreathEvent, float> _breathFlash = new(ReferenceEqualityComparer.Instance);

    /// <summary>Ground blackened where breath struck: (where, when, animation clock). Only for the eye; it fades.</summary>
    private readonly List<(float3 At, float Start)> _scorches = [];

    private const float ScorchSeconds = 10f;
    private const int MaxScorches = 64;

    private void Scorch(float3 at)
    {
        if (_scorches.Count >= MaxScorches)
            _scorches.RemoveAt(0);
        _scorches.Add((at, _anim.Clock));
    }

    /// <summary>The fading black marks breath leaves on the ground.</summary>
    private void DrawScorches(Simulation sim, int cut)
    {
        var mark = S("fx/smoke");
        for (var i = _scorches.Count - 1; i >= 0; i--)
        {
            var (at, start) = _scorches[i];
            var age = (_anim.Clock - start) / ScorchSeconds;
            if (age >= 1f || age < 0f)
            {
                _scorches.RemoveAt(i);
                continue;
            }
            var ground = MathF.Floor(at.Z);
            if (ground > cut)
                continue;
            var h = SimRandom.Hash((int)(at.X * 8), (int)(at.Y * 8), 11);
            Decal(mark, new float3(at.X, at.Y, ground + 0.035f), 1.1f + (h & 15) * 0.03f, Rgba.Pack(0.07f, 0.05f, 0.04f, 0.6f * (1f - age * age)), Quad.Level((int)ground), translucent: true);
        }
    }

    /// <summary>The heart of a dragon's flame.</summary>
    private static readonly float4 FireColor = new(1f, 0x4A / 255f, 0x24 / 255f, 1f);

    /// <summary>
    /// Blows being wound up (<see cref="Sim.Combat.Windup"/>), for anyone who can see the one swinging
    /// (not your own: you know where you aimed): each one's sweep on the ground (<see cref="DrawSweep"/>).
    /// </summary>
    private void DrawWindups(Simulation sim)
    {
        var w = sim.World;
        var ring = S("ui/blow_ring");
        var now = sim.Clock.Tick + (Interp?.Alpha ?? 1f);
        foreach (var e in w.Each<Sim.Combat.Windup>())
        {
            var blow = w.Get<Sim.Combat.Windup>(e);
            if (Seen(e) && LocalAim?.E != e)
                DrawSweep(sim, e, blow, now, ring);
        }
    }

    /// <summary>The local player's bow, drawn and aimed: where an arrow would fly, how far drawn, whether it's ready, and whether they're too winded to loose it.</summary>
    public (float3 From, float3 To, float Drawn, bool Ready, bool Winded)? AimLine { get; set; }

    /// <summary>
    /// The aim line (<see cref="AimLine"/>): a ribbon on the ground from your feet to where the arrow
    /// would stop, edged dark so it reads on sand or grass. It reaches out as the bow's drawn (pale),
    /// turns ember once it's ready, with a ring where it'd strike; grey if you've no wind to loose it.
    /// </summary>
    private void DrawAimLine()
    {
        if (AimLine is not { } line)
            return;
        var white = S(TextureAtlas.White);
        var flat = new float3(line.To.X - line.From.X, line.To.Y - line.From.Y, 0);
        var length = MathF.Sqrt(flat.X * flat.X + flat.Y * flat.Y);
        if (length < 0.05f)
            return;
        var dir = flat * (1f / length);
        var side = new float3(-dir.Y, dir.X, 0);
        var c = line.Winded ? new float4(0.6f, 0.6f, 0.6f, 0.85f) : line.Ready ? HeavyBlowColor : new float4(1f, 1f, 1f, 0.95f);
        var start = 0.35f;
        var shown = start + (length - start) * (line.Ready ? 1f : 0.25f + 0.75f * line.Drawn);
        var ground = new float3(line.From.X, line.From.Y, line.From.Z + 0.06f);
        void Ribbon(float width, uint color)
        {
            var o = ground + dir * start - side * (width * 0.5f);
            _quads.Add(new Quad
            {
                Origin = o,
                AxisU = dir * (shown - start),
                AxisV = side * width,
                Flags = Quad.Unlit | Quad.Always,
                Color = color,
                Ao = Quad.NoAo,
                Uv = white.Uv,
            }, translucent: true);
        }
        Ribbon(0.22f, Rgba.Pack(0.08f, 0.05f, 0.03f, line.Ready ? 0.55f : 0.8f)); // the dark edge
        Ribbon(line.Ready ? 0.1f : 0.08f, Rgba.Pack(c.X, c.Y, c.Z, c.W));
        var end = ground + dir * shown;
        if (line.Ready)
        {
            var pulse = 0.8f + 0.2f * MathF.Sin(_now * 10f);
            Decal(S("ui/blow_ring"), end, 0.75f * pulse, Rgba.Pack(c.X, c.Y, c.Z, 0.95f), Quad.Unlit | Quad.Always, translucent: true);
        }
        else
            Decal(S("ui/blow_ring"), end, 0.4f, Rgba.Pack(c.X, c.Y, c.Z, 0.6f), Quad.Unlit | Quad.Always, translucent: true);
    }

    /// <summary>The local player's aim (radians on the map) and whose it is, drawn before the sim hears of it.</summary>
    public (Entity E, float Aim)? LocalAim { get; set; }

    /// <summary>A swing seen this frame or just landed: its windup, and the tick it landed (-1 while it's still coming).</summary>
    private sealed class Swing
    {
        public Entity Who;
        public Sim.Combat.Windup Blow;
        public long Landed = -1;
        public string Kind = "";
        public bool Thrust;
        public Sprite Sprite;
    }

    private readonly Dictionary<int, Swing> _swings = [];

    /// <summary>Whose dodge has kicked up its dust: the dash it was for (<see cref="Sim.Combat.FreeMover.DashUntil"/>).</summary>
    private readonly Dictionary<int, long> _dashDust = [];
    private readonly List<int> _swingsDone = [];

    /// <summary>How long a landed blow's strike is drawn (ticks): the sweep, or the thrust out and back.</summary>
    private const int StrikeTicks = 9;

    /// <summary>
    /// Who's swinging what this frame: every weapon being wound up (<see cref="Sim.Combat.Windup"/>)
    /// that's a blade, a haft or a point (not a bow), and each for a moment after it lands.
    /// </summary>
    private void UpdateSwings(Simulation sim)
    {
        var w = sim.World;
        var now = sim.Clock.Tick;
        foreach (var e in w.Each<Sim.Combat.Windup>())
        {
            if (!w.TryGet<Equipment>(e, out var eq) || !w.IsAlive(eq.Weapon) || w.Get<Thing>(eq.Weapon).Def is not { Weapon.Ranged: false, Held: { } held } def
                || Sim.Combat.HouseRules.Sheathed(sim, e)) // fists in the inn: no blade to swing
                continue;
            if (!_swings.TryGetValue(e.Index, out var sw) || sw.Landed >= 0 || sw.Blow.Start != w.Get<Sim.Combat.Windup>(e).Start)
                _swings[e.Index] = sw = new Swing { Who = e, Kind = held, Thrust = def.Blow == "thrust", Sprite = S(def.Sprite) };
            sw.Blow = w.Get<Sim.Combat.Windup>(e);
        }
        _swingsDone.Clear();
        foreach (var (index, sw) in _swings)
        {
            var still = false;
            foreach (var x in w.Each<Sim.Combat.Windup>())
                if (x.Index == index)
                {
                    still = w.Get<Sim.Combat.Windup>(x).Start == sw.Blow.Start;
                    break;
                }
            if (!still && sw.Landed < 0)
                sw.Landed = now;
            if (sw.Landed >= 0 && now - sw.Landed > StrikeTicks)
                _swingsDone.Add(index);
        }
        foreach (var i in _swingsDone)
            _swings.Remove(i);
    }

    /// <summary>
    /// The weapon in a swing, drawn on its own about the hand: a blade or a haft drawn back to one
    /// side as it's wound up, then swept across the way it's aimed, with a smear; a spear drawn in,
    /// then thrust out and back.
    /// </summary>
    private void DrawSwings(Simulation sim)
    {
        var w = sim.World;
        var now = sim.Clock.Tick + (Interp?.Alpha ?? 1f);
        foreach (var sw in _swings.Values)
        {
            var e = sw.Who;
            if (!w.IsAlive(e) || !Seen(e) || !w.Has<Position>(e))
                continue;
            var blow = sw.Blow;
            var feet = DrawPos(sim, e, w.Get<Position>(e).Pos);
            var aim = LocalAim is { } mine && mine.E == e ? mine.Aim : blow.Aim;
            var thrust = sw.Thrust;
            var heavy = blow.Heavy;
            // Laid flat, the camera's tilt foreshortens it: drawn a size up from the item art.
            var size = sw.Kind is "longsword" or "spear" ? 1.3f : sw.Kind == "knife" ? 0.7f : 1.0f;
            // The weapon itself is in hand (the swing frames); on the ground plane only the smear of
            // the strike, so the way it went reads from any angle.
            if (sw.Landed < 0)
                continue;
            var q = Math.Clamp((now - sw.Landed) / StrikeTicks, 0f, 1f);
            if (thrust)
            {
                DrawWeapon(sw.Sprite, feet, aim, size, -0.15f + 1.1f * MathF.Sin(MathF.PI * q), 0.35f * (1f - q));
                continue;
            }
            // Swept from the drawn-back side across the aim, fast at first, fading as it goes.
            var from = aim + (heavy ? 1.7f : 1.4f);
            var to = aim - (heavy ? 1.7f : 1.4f);
            float At(float t) => from + (to - from) * (1f - (1f - t) * (1f - t));
            for (var k = 3; k >= 0; k--)
                DrawWeapon(sw.Sprite, feet, At(Math.Max(0f, q - k * 0.08f)), size, 0f, (0.4f - k * 0.1f) * (1f - q));
        }
    }

    /// <summary>
    /// A weapon's item art (drawn corner to corner, grip at the bottom left) laid flat at hand height,
    /// pointing along <paramref name="angle"/> on the map, pushed out along it by <paramref name="reach"/>.
    /// </summary>
    private void DrawWeapon(in Sprite sprite, float3 feet, float angle, float size, float reach, float alpha)
    {
        var dir = new float3(MathF.Cos(angle), MathF.Sin(angle), 0);
        var a = angle - MathF.PI / 4;
        var u = new float3(MathF.Cos(a), MathF.Sin(a), 0) * size;
        var v = new float3(MathF.Sin(a), -MathF.Cos(a), 0) * size;
        var hand = feet + new float3(0, 0, 0.5f) + dir * (0.12f + reach);
        _quads.Add(new Quad
        {
            Origin = hand - v,
            AxisU = u,
            AxisV = v,
            Flags = Quad.Unlit | Quad.Always,
            Color = Rgba.Pack(1f, 1f, 1f, alpha),
            Ao = Quad.NoAo,
            Uv = sprite.Uv,
        }, translucent: true);
    }

    /// <summary>
    /// A dragon drawing breath (<see cref="Sim.Combat.Inhaling"/>): a wide ember ring on where it'll
    /// breathe, closing until it does, white as the fire comes.
    /// </summary>
    private void DrawInhaling(Simulation sim)
    {
        var w = sim.World;
        var ring = S("ui/blow_ring");
        var now = sim.Clock.Tick + (Interp?.Alpha ?? 1f);
        foreach (var e in w.Each<Sim.Combat.Inhaling>())
        {
            if (!Seen(e))
                continue;
            var breath = w.Get<Sim.Combat.Inhaling>(e);
            var at = Position.CenterOf(breath.Target) + new float3(0, 0, 0.06f);
            var p = Math.Clamp((now - breath.Start) / Math.Max(1f, breath.At - breath.Start), 0f, 1f);
            var c = now >= breath.At ? new float4(1f, 1f, 1f, 0.95f) : HeavyBlowColor;
            Decal(ring, at, 2.4f + 3f * (1f - p), Rgba.Pack(c.X, c.Y, c.Z, c.W * (0.35f + 0.65f * p)), Quad.Unlit | Quad.Always, translucent: true);
            Decal(ring, at, 2.4f, Rgba.Pack(c.X, c.Y, c.Z, 0.25f), Quad.Unlit | Quad.Always, translucent: true);
        }
    }

    /// <summary>
    /// With free movement, a blow's telegraph is where it'll land (<see cref="Sim.Combat.Reach"/>):
    /// marks along its sweep's edge and down its middle, filling in as it comes, white as it lands. An
    /// arrow's is its line. Step out of it to be clear.
    /// </summary>
    private void DrawSweep(Simulation sim, Entity e, in Sim.Combat.Windup blow, float now, Sprite mark)
    {
        var w = sim.World;
        var attacks = Sim.Combat.CombatRules.Attacks(sim, e);
        if (blow.Attack >= attacks.Count || !w.Has<Position>(e))
            return;
        var attack = attacks[blow.Attack];
        var from = DrawPos(sim, e, w.Get<Position>(e).Pos) + new float3(0, 0, 0.06f);
        var p = Math.Clamp((now - blow.Start) / Math.Max(1f, blow.Impact - blow.Start), 0f, 1f);
        var c = now >= blow.Impact ? new float4(1f, 1f, 1f, 0.95f) : blow.Heavy || Sim.Combat.Blows.IsDragon(sim, e) ? HeavyBlowColor : BlowColor;
        var color = Rgba.Pack(c.X, c.Y, c.Z, c.W * (0.55f + 0.45f * p));
        var size = 0.26f + 0.14f * p;
        void Mark(float angle, float r) =>
            Decal(mark, from + new float3(MathF.Cos(angle) * r, MathF.Sin(angle) * r, 0), size, color, Quad.Unlit | Quad.Always, translucent: true);
        if (attack.Ranged)
        {
            var length = Math.Min(attack.Range, 9f);
            for (var r = 1f; r <= length * p + 0.01f; r += 0.6f)
                Mark(blow.Aim, r);
            return;
        }
        var shape = Sim.Combat.Reach.ShapeOf(sim, e, attack);
        var edge = Math.Max(2, (int)(shape.HalfArc * 2 * shape.Length / 0.35f));
        for (var i = 0; i <= edge; i++)
            Mark(blow.Aim - shape.HalfArc + 2 * shape.HalfArc * i / edge, shape.Length);
        for (var r = 0.5f; r < shape.Length; r += 0.45f)
            Mark(blow.Aim, r * p + 0.3f * (1 - p));
    }

    private static readonly float4 BlowColor = new(0.99f, 0.77f, 0.33f, 0.9f);
    private static readonly float4 HeavyBlowColor = new(1f, 0.33f, 0.16f, 0.95f);

    /// <summary>A word over whoever met a blow: parried, blocked, stepped aside, guard broken, riposte.</summary>
    private void DrawBlowWords(Simulation sim)
    {
        var w = sim.World;
        foreach (var b in sim.RecentBlows)
        {
            if (_firstSeen.ContainsKey(b) || !w.IsAlive(b.Target) || !w.Has<Position>(b.Target) || !Seen(b.Target))
                continue;
            _firstSeen[b] = _now;
            Impact(sim, b);
            var (text, color) = b.Outcome switch
            {
                Sim.Combat.BlowOutcome.Parried => ("Parried!", new float4(1f, 0.95f, 0.6f, 1f)),
                Sim.Combat.BlowOutcome.Blocked => ("Blocked", new float4(0.8f, 0.85f, 0.95f, 1f)),
                Sim.Combat.BlowOutcome.Dodged => ("Dodged", new float4(0.75f, 0.95f, 0.75f, 1f)),
                Sim.Combat.BlowOutcome.GuardBroken => ("Guard broken!", new float4(1f, 0.5f, 0.35f, 1f)),
                Sim.Combat.BlowOutcome.Riposte => ("Riposte!", new float4(1f, 0.85f, 0.4f, 1f)),
                Sim.Combat.BlowOutcome.Miss when !ShotRolled(sim, b) => ("Miss", new float4(0.8f, 0.8f, 0.8f, 1f)),
                _ => ((string?)null, default(float4)),
            };
            if (text != null)
                Float(w.Get<Position>(b.Target).Pos + WorldScale.Rise(1.45f), text, color);
        }
    }

    /// <summary>Until when (<see cref="_now"/>) a struck figure is drawn flaring white, by entity index.</summary>
    private readonly Dictionary<int, float> _flashes = [];

    /// <summary>How long a struck figure flares: a light blow, and a heavy one or a riposte.</summary>
    private const float FlashLight = 0.09f, FlashHeavy = 0.14f;

    /// <summary>
    /// A blow landing, as anyone watching sees it: whoever it hurt flares white; steel meeting steel
    /// (a block, a parry, a guard broken) throws sparks where the two meet. Fists in the inn don't spark.
    /// </summary>
    private void Impact(Simulation sim, Sim.Combat.BlowEvent b)
    {
        var w = sim.World;
        switch (b.Outcome)
        {
            case Sim.Combat.BlowOutcome.Hit or Sim.Combat.BlowOutcome.Riposte:
                _flashes[b.Target.Index] = _now + (b.Heavy || b.Outcome == Sim.Combat.BlowOutcome.Riposte ? FlashHeavy : FlashLight);
                break;
            case Sim.Combat.BlowOutcome.Blocked or Sim.Combat.BlowOutcome.Parried or Sim.Combat.BlowOutcome.GuardBroken:
                if (!w.IsAlive(b.Attacker) || !w.Has<Position>(b.Attacker) || Sim.Combat.HouseRules.Sheathed(sim, b.Attacker))
                    break;
                var from = DrawPos(sim, b.Attacker, w.Get<Position>(b.Attacker).Pos);
                var to = DrawPos(sim, b.Target, w.Get<Position>(b.Target).Pos);
                var along = to - from;
                along.Z = 0;
                var len = MathF.Sqrt(along.X * along.X + along.Y * along.Y);
                along = len > 1e-4f ? along * (1f / len) : new float3(1, 0, 0);
                var at = from + (to - from) * 0.6f + new float3(0, 0, 0.55f);
                _particles.Sparks(at, -along, b.Outcome == Sim.Combat.BlowOutcome.Blocked ? 6 : 12);
                break;
        }
    }

    /// <summary>A miss the attack itself rolled already has its word (<see cref="DrawShots"/>); one out of reach doesn't.</summary>
    private static bool ShotRolled(Simulation sim, Sim.Combat.BlowEvent b) => sim.RecentShots.Any(s => s.Tick == b.Tick);

    /// <summary>Lantern posts further apart than this (cells) aren't strung together.</summary>
    private const float BuntingSpan = 12f;

    private static readonly uint[] PennantColours =
    [
        Rgba.Pack(0.71f, 0.26f, 0.17f, 1f), // madder
        Rgba.Pack(0.85f, 0.65f, 0.25f, 1f), // ochre
        Rgba.Pack(0.24f, 0.33f, 0.52f, 1f), // woad
    ];

    /// <summary>
    /// Bunting strung between the festival's lantern posts, round the square: each post to the next
    /// in a ring about their middle (if it's near enough), a cord sagging between their tops with
    /// pennants hung along it. Presentation only, from where the posts stand.
    /// </summary>
    private void DrawBunting(Simulation sim)
    {
        var w = sim.World;
        var posts = sim.Festival.PropsOf(sim, "FestivalLantern").Select(e => w.Get<Thing>(e)).ToList();
        if (posts.Count < 2)
            return;
        float cx = 0, cy = 0;
        foreach (var p in posts)
            (cx, cy) = (cx + p.Cell.X, cy + p.Cell.Y);
        (cx, cy) = (cx / posts.Count, cy / posts.Count);
        posts.Sort((a, b) => MathF.Atan2(a.Cell.Y - cy, a.Cell.X - cx).CompareTo(MathF.Atan2(b.Cell.Y - cy, b.Cell.X - cx)));
        var cord = Rgba.Pack(0.3f, 0.24f, 0.18f, 1f);
        for (var i = 0; i < posts.Count; i++)
        {
            var a = posts[i];
            var b = posts[(i + 1) % posts.Count];
            if (posts.Count == 2 && i == 1)
                break;
            var top = 1.82f * WorldScale.HeightOf(a.Def);
            var from = Sim.Components.Position.CenterOf(a.Cell) + WorldScale.Rise(top);
            var to = Sim.Components.Position.CenterOf(b.Cell) + WorldScale.Rise(top);
            var span = MathF.Sqrt((to.X - from.X) * (to.X - from.X) + (to.Y - from.Y) * (to.Y - from.Y));
            if (span > BuntingSpan || a.Cell.Z != b.Cell.Z || (!SeenAt(from) && !SeenAt(to)))
                continue;
            var steps = Math.Max(4, (int)(span * 4));
            var sag = WorldScale.Rise(0.18f * span / 4f);
            for (var k = 0; k <= steps; k++)
            {
                var t = k / (float)steps;
                var at = from + (to - from) * t - sag * (4f * t * (1f - t)); // a parabola, near enough a hanging cord
                Billboard(S(TextureAtlas.White), at, 0.04f, 0.04f, cord, Quad.Level(a.Cell.Z));
                if (k % 2 == 1 && k < steps)
                    Billboard(S(TextureAtlas.White), at - WorldScale.Rise(0.16f), 0.12f, 0.16f, PennantColours[(k / 2 + i) % PennantColours.Length], Quad.Level(a.Cell.Z));
            }
        }
    }

    /// <summary>
    /// The arrows in the butts' face (<see cref="Sim.Round.FestivalSystem.Stuck"/>): a shaft standing
    /// out of the straw where each landed, across the face as scored, at a height of its own.
    /// </summary>
    private void DrawStuckArrows(Simulation sim)
    {
        var w = sim.World;
        var white = S(TextureAtlas.White);
        var k = 0;
        foreach (var (target, across) in sim.Festival.Stuck)
        {
            k++;
            if (!w.IsAlive(target) || !w.TryGet<Thing>(target, out var t) || !SeenAt(Sim.Components.Position.CenterOf(t.Cell)))
                continue;
            var height = WorldScale.HeightOf(t.Def);
            // Across the face as scored; up and down a little, just for the look.
            var up = ((k * 37 % 11) / 10f - 0.5f) * 0.3f;
            var lateral = Sim.Maps.Footprint.Rotate(across * Sim.Combat.Reach.FaceRadius, 0.09f, t.Facing);
            var normal = Sim.Maps.Footprint.Rotate(0f, 1f, t.Facing);
            var face = Sim.Components.Position.CenterOf(t.Cell) + new float3(lateral.X, lateral.Y, 0) + WorldScale.Rise((0.7f + up) * height);
            for (var s = 0; s < 4; s++)
                Billboard(white, face + new float3(normal.X, normal.Y, 0) * (s * 0.08f), 0.05f, 0.05f, Rgba.Pack(0.45f, 0.3f, 0.18f, 1f), Quad.Level(t.Cell.Z));
            Billboard(white, face + new float3(normal.X, normal.Y, 0) * 0.32f, 0.09f, 0.09f, Rgba.Pack(0.9f, 0.88f, 0.8f, 1f), Quad.Level(t.Cell.Z));
        }
    }

    /// <summary>Attacks: an arrow streaking to the target, then a flash (red for a wound, grey for a miss).</summary>
    private void DrawShots(Simulation sim, float time)
    {
        var white = S(TextureAtlas.White);
        foreach (var shot in sim.RecentShots)
        {
            if (!SeenAt(shot.From) && !SeenAt(shot.To))
                continue;
            if (!_firstSeen.ContainsKey(shot))
                Float(shot.To + WorldScale.Rise(1.1f), shot.Word ?? (shot.Hit ? "Hit!" : "Miss"),
                    shot.Word != null ? new float4(0.95f, 0.8f, 0.4f, 1f) : shot.Hit ? new float4(1f, 0.43f, 0.35f, 1f) : new float4(0.8f, 0.8f, 0.8f, 1f));
            var age = Age(shot, time, 0.8f);
            if (age > 1f)
                continue;
            var from = shot.From + new float3(0, 0, 0.7f);
            var to = shot.To + new float3(0, 0, 0.6f);
            var flight = shot.Ranged ? Math.Min(1f, age * 3f) : 1f;
            if (shot.Ranged && flight < 1f && shot.Sprite != null)
            {
                // Something hurled (a stool in a brawl): it tumbles along a low arc.
                var at = from + (to - from) * flight + new float3(0, 0, 0.6f * flight * (1f - flight) * 4f * 0.5f);
                var tumble = 0.4f + 0.12f * MathF.Sin(flight * 18f);
                Billboard(S(shot.Sprite), at, tumble, 0.4f, Rgba.White, Quad.Always);
                continue;
            }
            if (shot.Ranged && flight < 1f)
            {
                for (var k = 0; k < 4; k++)
                {
                    var t = Math.Max(0f, flight - k * 0.03f);
                    Billboard(white, from + (to - from) * t, 0.12f, 0.12f, Rgba.Pack(1f, 0.95f, 0.8f, 0.9f - k * 0.2f), Quad.Unlit | Quad.Always, translucent: true);
                }
                continue;
            }
            if (shot.Sprite != null)
            {
                // It breaks over them: splinters.
                var burst = 1f - age;
                Billboard(S("fx/smoke"), to - new float3(0, 0, 0.2f), 0.4f + 0.5f * age, 0.4f + 0.5f * age, Rgba.Pack(0.6f, 0.45f, 0.3f, 0.8f * burst), Quad.Unlit | Quad.Always, translucent: true);
                continue;
            }
            if (shot.Word != null)
                continue; // in the straw: it stays there (DrawStuckArrows)
            var flash = 1f - age;
            var color = shot.Hit ? Rgba.Pack(1f, 0.15f, 0.1f, 0.8f * flash) : Rgba.Pack(0.8f, 0.8f, 0.8f, 0.6f * flash);
            Billboard(S(shot.Hit ? "fx/fire" : "fx/smoke"), to - new float3(0, 0, 0.3f), 0.5f + 0.4f * age, 0.5f + 0.4f * age, color, Quad.Unlit | Quad.Always, translucent: true);
        }
    }

    private readonly List<ModelBox> _glowPane = [new()];

    /// <summary>
    /// After dark, a window whose room is lit (by its lamps or its fire, brighter than the night
    /// outside) glows warm from the street: its panes alone (<c>window_glow</c>) laid over its face,
    /// brighter the brighter the room. Only the street side: from inside you see the room.
    /// </summary>
    private void DrawWindowGlow(Simulation sim)
    {
        FindBurningRooms(sim);
        var dark = 1f - sim.Clock.Daylight;
        if (dark < 0.05f && _burningRooms.Count == 0)
            return;
        var map = sim.Map;
        var sky = sim.Lighting.SkyLight(sim);
        foreach (var (cell, room) in sim.Lighting.Windows(sim))
        {
            if (!ShellInView(cell) || _mesher.IsStub(sim, cell.X, cell.Y, cell.Z))
                continue;
            // The room's light just inside the window.
            var inside = -1f;
            foreach (var d in GlowSides)
                if (map.InBounds(cell + d) && map.RoomId[map.Idx(cell + d)] == room)
                    inside = Math.Max(inside, map.Light[map.Idx(cell + d)]);
            var glow = Math.Clamp((inside - sky - 0.05f) / 0.4f, 0f, 1f) * dark;
            if (map.Structure[map.Idx(cell)] is var part && part.IsNull || BuildingKit.Of(sim, part) is not { } kitted)
                continue;
            if (LitWindows && kitted.Building.Def.Has(Sim.Defs.TownRole.Tavern))
                glow = dark; // key art: every room of the inn lamplit
            // A room on fire glows through its windows day or night, flickering orange.
            var color = Rgba.Pack(glow, glow * 0.92f, glow * 0.8f);
            if (_burningRooms.ContainsKey(room))
            {
                var flicker = 0.78f + 0.14f * MathF.Sin(_now * 3.1f + cell.X * 1.7f) + 0.08f * MathF.Sin(_now * 7.3f + cell.Y * 2.3f);
                glow = Math.Max(glow, (0.55f + 0.45f * dark) * flicker);
                color = Rgba.Pack(glow, glow * 0.5f, glow * 0.18f);
            }
            if (glow < 0.04f)
                continue;
            foreach (var (model, facing) in BuildingKit.WallPieces(kitted.Kit, kitted.Building, cell))
            foreach (var box in model)
            {
                if (string.IsNullOrEmpty(box.Front))
                    continue;
                // A film over the face to the street (+Y as made), a hair proud of it.
                _glowPane[0] = new ModelBox
                {
                    Min = [box.Min[0], box.Max[1] + 0.004f, box.Min[2]], Max = [box.Max[0], box.Max[1] + 0.008f, box.Max[2]],
                    Top = "none", Side = "none", Back = "none", Front = "buildings/window_glow",
                };
                _modelQuads.Clear();
                PropModel.Build(_modelQuads, _glowPane, cell, facing, name => (S(name), Rgba.White),
                    color, Quad.Level(cell.Z) | Quad.Glow | Quad.Unlit);
                var jut = WorldMesher.JettyOut(kitted.Kit, kitted.Building, cell.X, cell.Y, cell.Z) ?? default;
                foreach (var q in _modelQuads)
                {
                    var moved = q;
                    moved.Origin += new float3(jut.X, jut.Y, 0);
                    _quads.Add(moved);
                }
            }
        }
    }

    private static readonly int3[] GlowSides = [new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0)];

    /// <summary>Rooms with fire in them this frame, and the height of their building's roof ridge (-1: no roof found).</summary>
    private readonly Dictionary<int, float> _burningRooms = [];

    /// <summary>
    /// Which rooms are burning, and over each its roof (through a window of it: the building its wall
    /// belongs to). From outside, with the roofs on, that's all a fire within shows of itself.
    /// </summary>
    private void FindBurningRooms(Simulation sim)
    {
        _burningRooms.Clear();
        var map = sim.Map;
        foreach (var i in sim.Fire.BurningTiles)
            if (map.RoomId[i] > 0)
                _burningRooms.TryAdd(map.RoomId[i], -1);
        if (_burningRooms.Count == 0)
            return;
        foreach (var (cell, room) in sim.Lighting.Windows(sim))
            if (_burningRooms.TryGetValue(room, out var roof) && roof < 0
                && map.Structure[map.Idx(cell)] is { IsNull: false } part && BuildingKit.Of(sim, part) is { } kitted)
            {
                // The ridge: the tops of the walls, and the pitch up to the middle of the span (BuildingKit.Roof).
                var def = kitted.Building.Def;
                var span = Math.Min(def.Width, def.Depth);
                var pitch = kitted.Kit.Roof?.Pitch ?? 0f;
                _burningRooms[room] = kitted.Building.Anchor.Z + def.Storeys + pitch * (span / 2f - 0.5f);
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
                    k < laid ? Rgba.White : ghost, level, jetty: kit.Jetty);
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

    /// <summary>Where a modelled fire's flames stand from its cell's floor centre (<see cref="ThingDef.FireAt"/>), turned with the thing.</summary>
    private static float3 FireOffset(ThingDef def, int facing)
    {
        if (def.FireAt is not [var fx, var fy, var fz])
            return new float3(0, 0, 0.08f);
        var d = Footprint.Rotate(fx - 0.5f, fy - 0.5f, facing);
        return new float3(d.X, d.Y, fz * WorldScale.HeightOf(def));
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
            if (PropModel.Placed(def) is { } siteModel)
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
                AddModel(model, cell, thing.Facing, thing.Material, tint, level,
                    count: Mods.GameMods.ModelCountOf(sim, e) ?? (w.TryGet<Stack>(e, out var st) ? st.Count : 1), lift: lift);
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
                var spriteName = thing.Material == null ? baseSprite : _materials.Resolve(def.Sprite, thing.Material).Sprite;
                if (Mods.GameMods.TiltOf(sim, e, sim.Clock.Tick) is { } tilt) // a tree coming down, say (a mod's)
                    DrawToppling(spriteName, cell, floor, def, tint, level, tilt);
                else
                    DrawVariedBillboard(def, spriteName, cell, floor, tint, level);
                break;
        }

        if (w.TryGet<Fueled>(e, out var fuel) && def.Building?.Fuel is { } props)
        {
            var frac = Math.Clamp(fuel.Fuel / props.Capacity, 0, 1);
            // A forge fired only for work (OnDemand) shows flames only while it's burning.
            if (def.Model != null && sim.Burning(e))
            {
                // A modelled fire: flickering flames over the logs.
                var t = _anim.Clock * 7f + e.Index;
                var size = (0.45f + 0.25f * frac + 0.05f * MathF.Sin(t) + 0.03f * MathF.Sin(t * 2.3f)) * def.FireSize;
                Billboard(S("fx/fire"), floor + FireOffset(def, thing.Facing), size * 0.85f, size, Rgba.Pack(1f, 0.92f, 0.8f, 0.95f), level | Quad.Unlit | Quad.Glow, translucent: true);
                DrawCoalBed(sim, e, def, floor, thing.Facing, level);
                // Embers lifting off it, and now and then a log's pop throwing sparks: by the fire's
                // size, so a hearth gives the odd one and the festival's bonfire a steady stream.
                var heart = floor + FireOffset(def, thing.Facing) + new float3(0, 0, size * 0.3f);
                var big = def.FireSize * def.FireSize * (0.6f + 0.4f * frac);
                for (var n = _particles.PerFrame(EmbersPerSecond * big, _animDt); n > 0; n--)
                    _particles.DriftingEmbers(heart, 1, 0.12f + 0.12f * def.FireSize);
                if (_particles.PerFrame(PopsPerSecond * big, _animDt) > 0)
                    _particles.Embers(heart, 3 + (int)(2 * def.FireSize));
                // A big open fire (the festival's bonfire) licks up as a burning cell does; a hearth's stays in its grate.
                if (def.FireSize >= OpenFireSize)
                    for (var n = _particles.PerFrame(LicksPerSecond * big, _animDt); n > 0; n--)
                        _particles.FlameLick(heart + new float3(0, 0, size * 0.2f), Math.Min(1f, 0.5f + 0.3f * def.FireSize));
                // A chimney's smoke is drawn apart (DrawChimneySmoke: the hearth's often hidden under its roof).
                // An open fire (the festival's bonfire): a lighter smoke straight off its flames.
                if (def.SmokeAt == null && def.FireSize >= OpenFireSize)
                    for (var n = _particles.PerFrame(ChimneyPuffsPerSecond * 0.8f * def.FireSize, _animDt); n > 0; n--)
                        _particles.WoodSmoke(heart + new float3(0, 0, size * 0.6f), 0.6f * frac, 0.9f + 0.3f * def.FireSize, grey: 0.45f);
            }
            Bar(floor + new float3(0, 0, 0.9f), frac, frac > props.RefuelAt ? Rgba.Pack(1f, 0.6f, 0.2f) : Rgba.Pack(1f, 0.2f, 0.2f), level);
        }

        // What's put up on a serving hatch stands on its counter, side by side (up to three shown);
        // what's laid on the festival's show table, along its length (up to six).
        if (def.Building is { Show: true })
        {
            var pieces = new List<ThingDef>();
            foreach (var item in sim.StoredIn(e))
                if (pieces.Count < 6)
                    pieces.Add(w.Get<Thing>(item).Def);
            var top = floor + new float3(0, 0, SurfaceUnder(sim.Map, w, cell));
            var along = PropModel.Rotate(new float3(0, 1, 0), thing.Facing);
            for (var k = 0; k < pieces.Count; k++)
                Billboard(S(pieces[k].Sprite), top + along * ((k + 0.5f) / Math.Max(3, pieces.Count) * 2f - 0.5f), 0.38f, 0.38f, Rgba.White, level);
        }
        DrawKitchenLife(sim, e, def, floor, thing, level);
        DrawRoomLife(sim, e, def, floor, thing, level);
        if (def.Building is { Hatch: true })
        {
            var dishes = new List<ThingDef>();
            foreach (var item in sim.StoredIn(e))
                for (var n = w.Get<Stack>(item).Count; n > 0 && dishes.Count < 3; n--)
                    dishes.Add(w.Get<Thing>(item).Def);
            var top = floor + new float3(0, 0, SurfaceUnder(sim.Map, w, cell));
            for (var k = 0; k < dishes.Count; k++)
                Billboard(S(dishes[k].Sprite), top + _cam.Right * ((k - (dishes.Count - 1) * 0.5f) * 0.28f), 0.34f, 0.34f, Rgba.White, level);
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

    /// <summary>
    /// An order's bubble: bone while there's time, warming to hearth red as it runs out, with the
    /// ember quest marker above it when it's for the viewer (the one bright thing on screen).
    /// </summary>
    private void DrawOrderBubble(OrderBubble b, float3 at, uint level, float phase)
    {
        var urgency = Math.Clamp(1f - b.TimeLeft / 0.4f, 0f, 1f); // nothing till 40% of the wait is left
        var tint = Ui.Theme.Text + (Ui.Theme.HearthRed - Ui.Theme.Text) * urgency;
        const float size = 0.8f; // bigger than a villager's status bubble: this one asks something of you
        DrawBubble(b.Icon, at, level, phase, Rgba.Pack(tint.X, tint.Y, tint.Z, 0.95f), size);
        if (b.ForYou)
        {
            var bob = MathF.Sin(phase * 3f) * 0.04f;
            // Over the bubble's top, and nudged towards the camera so the bubble never hides it.
            Billboard(S("ui/icon_marker"), at + WorldScale.Rise(size * 30f / 24f + 0.12f + bob) - _cam.Forward * 0.06f,
                0.42f, 0.42f, Rgba.White, level | Quad.Unlit);
        }
    }

    /// <summary>The emote's glyph in a bubble, popping up as it starts; a point also rings the cell it points at.</summary>
    private void DrawEmote(Simulation sim, Sim.Speech.SeenEmote emote, float3 at, uint level, float time)
    {
        var age = (sim.Clock.Tick - emote.Since) / (float)Sim.Core.SimClock.TicksPerSecond;
        var left = (emote.Until - sim.Clock.Tick) / (float)Sim.Core.SimClock.TicksPerSecond;
        var pop = Math.Clamp(age * 6f, 0f, 1f) * Math.Clamp(left * 4f, 0f, 1f);
        var size = 0.62f * (0.6f + 0.4f * pop + 0.12f * MathF.Sin(MathF.PI * Math.Clamp(age * 3f, 0f, 1f)));
        // A wave waggles, a laugh shakes, a cheer bounces: the glyph moves as the feeling does.
        var wiggle = emote.Kind switch
        {
            Sim.Speech.Emote.Wave => _cam.Right * (0.06f * MathF.Sin(time * 12f)),
            Sim.Speech.Emote.Laugh => WorldScale.Rise(0.04f * MathF.Abs(MathF.Sin(time * 16f))),
            Sim.Speech.Emote.Cheer => WorldScale.Rise(0.1f * MathF.Abs(MathF.Sin(time * 7f))),
            Sim.Speech.Emote.Angry => _cam.Right * (0.03f * MathF.Sin(time * 30f)),
            _ => float3.Zero,
        };
        DrawBubble(EmoteIcon(emote.Kind), at + WorldScale.Rise(0.1f * pop) + wiggle, level, 0f, size: size);
        if (emote.At is { } cell && InViewNow(cell))
        {
            var ring = 1.2f * (1f + 0.15f * MathF.Sin(time * 6f));
            var parchment = Rgba.Pack(Ui.Theme.Parchment.X, Ui.Theme.Parchment.Y, Ui.Theme.Parchment.Z, 0.9f * pop);
            Decal(S("ui/selection"), Position.CenterOf(cell) + new float3(0, 0, 0.04f), ring, parchment, Quad.Level(cell.Z) | Quad.Unlit, translucent: true);
            Billboard(S("ui/emote_point"), Position.CenterOf(cell) + WorldScale.Rise(0.7f + 0.08f * MathF.Sin(time * 5f)), 0.4f, 0.4f, Rgba.White, Quad.Level(cell.Z) | Quad.Unlit);
        }
    }

    /// <summary>An emote's glyph (<c>ui/emote_wave</c>...), from <c>tools/norse_ui.py --emotes</c>.</summary>
    public static string EmoteIcon(Sim.Speech.Emote kind) => EmoteIcons[(int)kind];

    private static readonly string[] EmoteIcons = [.. Enum.GetValues<Sim.Speech.Emote>().Select(k => "ui/emote_" + k.ToString().ToLowerInvariant())];

    private void DrawBubble(string icon, float3 at, uint level, float phase, uint? tint = null, float size = 0.56f)
    {
        at += WorldScale.Rise(MathF.Sin(phase * 2f) * 0.03f);
        var bubble = S("ui/bubble");
        Billboard(bubble, at, size, size * 30f / 24f, tint ?? Rgba.Pack(1, 1, 1, 0.95f), level | Quad.Unlit);
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

    /// <summary>
    /// A thing tipping over (<see cref="Mods.Tilt"/>: a tree whose hinge has given), pivoting on its foot
    /// toward its fall, flat when the angle reaches π/2.
    /// </summary>
    private void DrawToppling(string spriteName, int3 cell, float3 feet, ThingDef def, uint tint, uint level, Mods.Tilt tilt)
    {
        if (_atlasVersion != atlas.Version)
            _variantCache.Clear();
        if (!_variantCache.TryGetValue(spriteName, out var variants))
            _variantCache[spriteName] = variants = atlas.Variants(spriteName);
        var h = Variation.Hash(cell.X, cell.Y, 0x7EE);
        var sprite = Variation.Mirror(variants[(int)((h >> 8) % (uint)variants.Count)], (h & 1) != 0, false);
        var height = def.RenderSize;
        var width = height * sprite.PixelSize.X / Math.Max(1f, sprite.PixelSize.Y);
        var angle = tilt.Angle;
        var rad = tilt.Degrees * MathF.PI / 180f;
        var toward = new float3(MathF.Cos(rad), MathF.Sin(rad), 0);
        var up = new float3(0, 0, 1) * MathF.Cos(angle) + toward * MathF.Sin(angle);
        var r = _cam.Right;
        _quads.Add(
            new Quad
            {
                Origin = feet + up * height - r * (width * 0.5f),
                AxisU = r * width,
                AxisV = -up * height,
                Flags = level | Quad.Smooth,
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
        DrawMarkers(dl, cam);
        DrawPointers(dl, cam);
        DrawSpokenLines(dl, cam, sim);
    }

    /// <summary>
    /// The game's pointers: an ember-rimmed tag over whoever it is while they're on screen (seen
    /// through roofs and walls), else pinned to the screen's edge with an arrow their way.
    /// </summary>
    private readonly List<float4> _placedTags = [];

    private void DrawPointers(ImDrawListPtr dl, Camera3D cam)
    {
        if (Pointers.Count == 0)
            return;
        var view = cam.ViewportSize;
        var margin = Narsil.Ui.Gui.S(28);
        var bob = MathF.Sin((float)ImGui.GetTime() * 3f) * Narsil.Ui.Gui.S(3);
        var lineH = ImGui.GetTextLineHeight();
        var pad = Narsil.Ui.Gui.S(7, 3);
        var back = Ui.Theme.U8(Ui.Theme.Panel, 0.92f);
        var rim = Ui.Theme.U8(Ui.Theme.Accent);
        var top = margin + ImGui.GetFrameHeight(); // clear of the menu bar
        _placedTags.Clear();
        foreach (var p in Pointers)
        {
            var screen = cam.WorldToScreen(p.At);
            var size = new float2(ImGui.CalcTextSize(p.Text, false, -1).X, lineH) + pad * 2;
            var onScreen = screen.X >= margin && screen.X <= view.X - margin && screen.Y >= top && screen.Y <= view.Y - margin;
            float2 min;
            if (onScreen)
            {
                // A tag over their head, with a tail down to them.
                var tail = screen + new float2(0, bob);
                var tailH = Narsil.Ui.Gui.S(6);
                min = new float2(tail.X - size.X * 0.5f, tail.Y - tailH - size.Y);
                var t1 = new float2(tail.X - tailH, min.Y + size.Y);
                var t2 = new float2(tail.X + tailH, min.Y + size.Y);
                dl.AddTriangleFilled(in t1, in t2, in tail, rim);
            }
            else
            {
                // Pinned where the line from the middle of the screen to them leaves it, an arrow out their way.
                var mid = new float2(view.X * 0.5f, (view.Y + top - margin) * 0.5f);
                var d = screen - mid;
                var len = MathF.Max(1e-3f, MathF.Sqrt(d.X * d.X + d.Y * d.Y));
                var dir = d / len;
                var half = new float2(view.X * 0.5f - margin, (view.Y - margin - top) * 0.5f);
                var k = MathF.Min(MathF.Abs(dir.X) > 1e-4f ? half.X / MathF.Abs(dir.X) : float.MaxValue, MathF.Abs(dir.Y) > 1e-4f ? half.Y / MathF.Abs(dir.Y) : float.MaxValue);
                var edge = mid + dir * k + dir * bob;
                var arrow = Narsil.Ui.Gui.S(12);
                var side = new float2(-dir.Y, dir.X);
                var tip = edge + dir * arrow;
                var a1 = edge + side * arrow * 0.6f;
                var a2 = edge - side * arrow * 0.6f;
                dl.AddTriangleFilled(in tip, in a1, in a2, rim);
                // The tag sits inward of the arrow, kept on screen.
                var centre = edge - dir * (Narsil.Ui.Gui.S(6) + MathF.Abs(dir.X) * size.X * 0.5f + MathF.Abs(dir.Y) * size.Y * 0.5f);
                min = centre - size * 0.5f;
                min = new float2(Math.Clamp(min.X, Narsil.Ui.Gui.S(4), view.X - size.X - Narsil.Ui.Gui.S(4)), Math.Clamp(min.Y, top - margin + Narsil.Ui.Gui.S(4), view.Y - size.Y - Narsil.Ui.Gui.S(4)));
                // Two people off the same way: the later tag steps in past the earlier one.
                for (var tries = 0; tries < 8 && _placedTags.Exists(r => min.X < r.Z && min.X + size.X > r.X && min.Y < r.W && min.Y + size.Y > r.Y); tries++)
                    min -= (MathF.Abs(dir.Y) >= MathF.Abs(dir.X) ? new float2(0, MathF.Sign(dir.Y) * (size.Y + Narsil.Ui.Gui.S(3)))
                        : new float2(0, size.Y + Narsil.Ui.Gui.S(3)));
                _placedTags.Add(new float4(min.X, min.Y, min.X + size.X, min.Y + size.Y));
            }
            var max = min + size;
            dl.AddRectFilled(in min, in max, back, Narsil.Ui.Gui.S(4), ImDrawFlags.None);
            dl.AddRect(in min, in max, rim, Narsil.Ui.Gui.S(4), ImDrawFlags.None, Narsil.Ui.Gui.S(2));
            var tp = min + pad;
            dl.AddText(in tp, Ui.Theme.U8(Ui.Theme.Text), p.Text);
        }
    }

    /// <summary>The game's markers: a small plate with a gentle bob, its parchment rim green once it's done.</summary>
    private void DrawMarkers(ImDrawListPtr dl, Camera3D cam)
    {
        var bob = MathF.Sin((float)ImGui.GetTime() * 2.4f) * Narsil.Ui.Gui.S(3);
        foreach (var m in Markers)
        {
            var tail = cam.WorldToScreen(m.At) + new float2(0, bob);
            var lineH = ImGui.GetTextLineHeight();
            var width = ImGui.CalcTextSize(m.Text, false, -1).X;
            var pad = Narsil.Ui.Gui.S(7, 3);
            var size = new float2(width, lineH) + pad * 2;
            var tailH = Narsil.Ui.Gui.S(5);
            var min = new float2(tail.X - size.X * 0.5f, tail.Y - tailH - size.Y);
            var max = min + size;
            var back = Ui.Theme.U8(Ui.Theme.Panel, m.Done ? 0.6f : 0.9f);
            var rim = Ui.Theme.U8(m.Done ? Ui.Theme.Good : Ui.Theme.Parchment, m.Done ? 0.6f : 1f);
            dl.AddRectFilled(in min, in max, back, Narsil.Ui.Gui.S(4), ImDrawFlags.None);
            dl.AddRect(in min, in max, rim, Narsil.Ui.Gui.S(4), ImDrawFlags.None, Narsil.Ui.Gui.S(m.Done ? 1 : 2));
            var t1 = new float2(tail.X - tailH, max.Y);
            var t2 = new float2(tail.X + tailH, max.Y);
            dl.AddTriangleFilled(in t1, in t2, in tail, back);
            var tp = min + pad;
            dl.AddText(in tp, Ui.Theme.U8(m.Done ? Ui.Theme.Dim : Ui.Theme.Text), m.Text);
        }
    }

    /// <summary>How long (ticks) a line stays over its speaker's head: long enough to read it.</summary>
    private static int SpokenLineTicks(string text) => SpeechSystem.ReadingTicks(text);

    private readonly HashSet<Entity> _spoke = [];

    /// <summary>What's just been said, over the heads of those the viewer can see and could make out: each speaker's latest line.</summary>
    private void DrawSpokenLines(ImDrawListPtr dl, Camera3D cam, Simulation sim)
    {
        if (Viewer == null)
            return;
        var w = sim.World;
        _spoke.Clear();
        foreach (var line in Viewer.Heard().Reverse())
        {
            var lasts = SpokenLineTicks(line.Text);
            if (sim.Clock.Tick - line.Tick > lasts || line.Speaker is not { } who || !_spoke.Add(who) || !w.IsAlive(who) || !Seen(who)
                || UnderDrawnFloor(sim.Map, w.Get<Position>(who).Cell))
                continue;
            // Above the status bubble (+0.2), so the two never sit on each other.
            var at = cam.WorldToScreen(DrawPos(sim, who, w.Get<Position>(who).Pos) + WorldScale.Rise(VisualHeight(sim, who) + 0.75f));
            var age = (sim.Clock.Tick - line.Tick) / (float)lasts;
            SpeechPlate(dl, at, line.Text, Math.Clamp((1f - age) * 5f, 0f, 1f), line.Volume);
        }
    }

    /// <summary>A said line on a small dark plate with a tail toward the speaker, wrapped (a shout is given room to be read whole).</summary>
    private static void SpeechPlate(ImDrawListPtr dl, float2 tail, string text, float alpha, Volume volume)
    {
        var maxW = Narsil.Ui.Gui.S(volume == Volume.Shout ? 300 : 240);
        var lines = Narsil.Ui.Gui.WrapLines(text, maxW, volume == Volume.Shout ? 6 : 4);
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

    private static void Text(ImDrawListPtr dl, float2 pos, string text, ImColor8 color)
    {
        var shadow = pos + new float2(1, 1);
        dl.AddText(in shadow, new ImColor8(0, 0, 0, 200), text);
        dl.AddText(in pos, color, text);
    }
}

using Narsil.Rendering;
using SupportingCast.Rendering;
using SupportingCast.Sim;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Ecs;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Maps;
using SupportingCast.Sim.Systems;

namespace SupportingCast;

/// <summary>
/// Turns the map's blocks and its cube/ramp structures (walls, stairs, slopes; not doors, which swing) into static
/// quad meshes, one per <see cref="Map.ChunkSize"/>² column chunk, rebuilt when the chunk's
/// <see cref="Map.ChunkVersion"/> changes. Faces between two solids are skipped, except buried
/// top faces, which become cutaway "caps". Top faces get per-corner ambient occlusion.
/// Everything else (plants, items, creatures, blueprints) is drawn per frame by <see cref="WorldRenderer"/>.
/// </summary>
public sealed class WorldMesher(TextureAtlas atlas)
{
    private QuadMesh[] _meshes = [];
    private int[] _builtVersion = [];
    private readonly List<Quad> _quads = [];
    private readonly Dictionary<string, Sprite> _sprites = [];

    /// <summary>Chunks rebuilt per frame at most, so a burst of changes doesn't hitch.</summary>
    public int MaxRebuildsPerFrame { get; set; } = 16;

    /// <summary>
    /// Milliseconds of remeshing per frame once something has been drawn; later chunks wait a
    /// frame (at least one is always rebuilt). Chunks never built yet don't wait: a hole in the
    /// world is worse than a slow first frame.
    /// </summary>
    public double BudgetMs { get; set; } = 3;

    public int ChunksRebuiltLastFrame { get; private set; }

    /// <summary>How tall (world z) a cut-away wall stands: knee-high, so the room's outline still reads.</summary>
    public const float StubHeight = 0.25f;

    private float2? _cutHeading;

    /// <summary>
    /// Cutaway walls: the camera's heading (into the screen, snapped to one of 8 directions), or
    /// null for walls at full height. Walls with an indoor room behind them as seen along it drop to
    /// <see cref="StubHeight"/>, so you can see into rooms. Changing it remeshes every chunk, a few
    /// a frame (the snap keeps that to rotating past a 45° mark).
    /// </summary>
    public float2? CutHeading
    {
        get => _cutHeading;
        set
        {
            if (_cutHeading == value)
                return;
            _cutHeading = value;
            for (var i = 0; i < _builtVersion.Length; i++)
                if (_builtVersion[i] != -1)
                    _builtVersion[i] = -2; // stale, but drawn until its turn
        }
    }

    /// <summary>The heading <see cref="CutHeading"/> takes for a camera heading: the nearest of the 8 compass directions.</summary>
    public static float2 SnapHeading(float3 heading)
    {
        var k = (int)MathF.Round(MathF.Atan2(heading.Y, heading.X) / (MathF.PI / 4));
        var a = k * MathF.PI / 4;
        return new float2(MathF.Cos(a), MathF.Sin(a));
    }

    private Entity _open = Entity.Null;
    private int _openLevel = int.MaxValue;

    /// <summary>
    /// The building you're inside, and the level you're on in it: its roof and anything of it above
    /// your level (the inn's upper storey, from the common room) are left out, and only its walls
    /// are cut down (with <see cref="CutHeading"/> set). Every other building keeps its roof.
    /// Entity.Null: every wall facing a room is cut (the \ view, when the shader hides every
    /// roof), or none is (no heading). Changing it remeshes.
    /// </summary>
    public (Entity Building, int Level) OpenBuilding
    {
        get => (_open, _openLevel);
        set
        {
            if (_open == value.Building && _openLevel == value.Level)
                return;
            (_open, _openLevel) = value;
            for (var i = 0; i < _builtVersion.Length; i++)
                if (_builtVersion[i] != -1)
                    _builtVersion[i] = -2;
        }
    }

    /// <summary>A part of the building you're in, above the level you're on: left out.</summary>
    private bool AboveYou(Simulation sim, Entity part, int z) => z > _openLevel && OfOpen(sim, part);

    /// <summary>Is the wall (or door) in this cell cut down to a stub just now (<see cref="CutHeading"/>, <see cref="OpenBuilding"/>)?</summary>
    public bool IsStub(Simulation sim, int x, int y, int z) =>
        _cutHeading is { } h && (_open.IsNull || OfOpen(sim, sim.Map.Structure[sim.Map.Idx(x, y, z)])) && CutAt(sim, x, y, z, h);

    /// <summary>Part of the building you're in.</summary>
    private bool OfOpen(Simulation sim, Entity part) => !_open.IsNull && !part.IsNull && Buildings.Of(sim, part) == _open;

    /// <summary>
    /// A wall or door with an indoor room on its far side, seen along <paramref name="heading"/>
    /// (a neighbour, diagonals included for corners, within 60° of straight ahead). A preset
    /// building's floor counts as indoors even when it's open to the street (the smithy's front),
    /// since it never makes a room. Stations built as walls (a kiln) stay whole: they're furniture,
    /// not the room's shell.
    /// </summary>
    public static bool CutAt(Simulation sim, int x, int y, int z, float2 heading)
    {
        var map = sim.Map;
        if (!map.InBounds(x, y, z))
            return false;
        var s = map.Structure[map.Idx(x, y, z)];
        if (s.IsNull || sim.World.Has<Construction>(s)
            || sim.World.Get<Thing>(s).Def.Building is not ({ IsWall: true, Workbench: null } or { IsDoor: true }))
            return false;
        var rooms = sim.Rooms.All;
        bool Indoors(int2 d)
        {
            int nx = x + d.X, ny = y + d.Y;
            if (!map.InBounds(nx, ny, z))
                return false;
            var id = map.RoomId[map.Idx(nx, ny, z)];
            return id > 0 && id <= rooms.Count && rooms[id - 1].Indoors || InsideBuilding(sim, new int3(nx, ny, z));
        }
        bool Ahead(int2 d) => (d.X * heading.X + d.Y * heading.Y) / MathF.Sqrt(d.X * d.X + d.Y * d.Y) > 0.5f;

        // A wall along a room goes by the room beside it; only a corner (no room straight beside
        // it) looks at the room on its diagonal, or a side wall would come down with the front.
        var beside = false;
        foreach (var d in Map.Neighbors4)
            if (Indoors(d))
            {
                beside = true;
                if (Ahead(d))
                    return true;
            }
        if (beside)
            return false;
        foreach (var d in Map.Neighbors8)
            if (d.X != 0 && d.Y != 0 && Indoors(d) && Ahead(d))
                return true;
        return false;
    }

    /// <summary>An open floor cell of a preset building's plan ('.').</summary>
    private static bool InsideBuilding(Simulation sim, int3 cell)
    {
        var w = sim.World;
        foreach (var e in w.Each<PresetBuilding>())
        {
            var b = w.Get<PresetBuilding>(e);
            if (Buildings.SymbolAt(b.Def, b.Anchor, b.Facing, cell) == '.')
                return true;
        }
        return false;
    }

    private static readonly float[] AoShades = [0.55f, 0.7f, 0.85f, 1f];

    private Sprite S(string name)
    {
        if (!_sprites.TryGetValue(name, out var s))
            _sprites[name] = s = atlas.Get(name);
        return s;
    }

    private readonly Dictionary<string, IReadOnlyList<Sprite>> _variants = [];
    private readonly MaterialArt _materials = new(atlas);

    private IReadOnlyList<Sprite> V(string name)
    {
        if (!_variants.TryGetValue(name, out var list))
            _variants[name] = list = atlas.Variants(name);
        return list;
    }

    /// <summary>
    /// A block's top face for one column: a variant picked per cell and randomly mirrored, so ground
    /// doesn't tile visibly, or the block's accent (wildflowers) where patch noise is high, with one
    /// variant per patch so each patch keeps one colour scheme. Purely cosmetic, stable per cell.
    /// </summary>
    private Sprite TopSprite(BlockDef block, int x, int y)
    {
        var h = Variation.Hash(x, y, 0x6A6);
        if (!string.IsNullOrEmpty(block.Accent))
        {
            var n = Variation.Patches(x, y, 0xF10, 7f);
            var accent = n > block.AccentAbove + 0.03f ? block.Accent
                : n > block.AccentAbove && !string.IsNullOrEmpty(block.AccentFringe) ? block.AccentFringe
                : null;
            if (accent != null)
            {
                var list = V(accent);
                var scheme = Math.Clamp((int)(Variation.Noise(x, y, 0xF11, 18f) * 1.6f * list.Count - 0.3f * list.Count), 0, list.Count - 1);
                return Variation.Mirror(list[scheme], (h & 1) != 0, (h & 2) != 0);
            }
        }
        return Variation.Mirror(Variation.Pick(V(block.Sprite), x, y, 0x6A5), (h & 1) != 0, (h & 2) != 0);
    }

    /// <summary>
    /// A block's side texture for one column and tile: a variant per cell, mirrored left-right only
    /// (sides have an up). <paramref name="z"/> counts tiles (two per level, <see cref="WorldScale"/>),
    /// so the two tiles up a level pick their variants independently.
    /// </summary>
    private Sprite SideSprite(BlockDef block, int x, int y, int z, bool low = false)
    {
        var name = block.SpriteSide;
        // The lower tile of a level skips the top band some sides have (turf, topsoil, a water
        // line) when there's art for it: <spriteSide>_low.
        if (low)
        {
            if (!_hasLow.TryGetValue(name, out var has))
                _hasLow[name] = has = atlas.TryGet(name + "_low", out _);
            if (has)
                name += "_low";
        }
        var h = Variation.Hash(x + z * 131, y, 0x51D);
        return Variation.Mirror(Variation.Pick(V(name), x + z * 131, y, 0x51E), (h & 1) != 0, false);
    }

    private readonly Dictionary<string, bool> _hasLow = [];

    /// <summary>Forget cached sprites and remesh every chunk (after the atlas was reloaded).</summary>
    public void Invalidate()
    {
        _sprites.Clear();
        _variants.Clear();
        _hasLow.Clear();
        Array.Fill(_builtVersion, -1);
    }

    /// <summary>Rebuild stale chunks and queue every chunk's mesh for drawing.</summary>
    public void Draw(QuadRenderer quads, Simulation sim)
    {
        var map = sim.Map;
        var count = map.ChunksX * map.ChunksY;
        if (_meshes.Length != count)
        {
            _meshes = new QuadMesh[count];
            for (var i = 0; i < count; i++)
                _meshes[i] = quads.CreateMesh();
            _builtVersion = new int[count];
            Array.Fill(_builtVersion, -1);
        }

        var rebuilt = 0;
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        for (var c = 0; c < count; c++)
        {
            if (_builtVersion[c] == map.ChunkVersion[c])
                continue;
            var fresh = _builtVersion[c] == -1;
            if (!fresh && rebuilt > 0 && (rebuilt >= MaxRebuildsPerFrame
                || System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds > BudgetMs))
                continue; // stale but drawn: next frame
            _builtVersion[c] = map.ChunkVersion[c];
            BuildChunk(sim, c % map.ChunksX, c / map.ChunksX);
            quads.Upload(_meshes[c], System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_quads));
            rebuilt++;
        }
        ChunksRebuiltLastFrame = rebuilt;

        foreach (var mesh in _meshes)
            quads.Draw(mesh);
    }

    private void BuildChunk(Simulation sim, int cx, int cy)
    {
        _quads.Clear();
        var map = sim.Map;
        int x0 = cx * Map.ChunkSize, y0 = cy * Map.ChunkSize;
        int x1 = Math.Min(map.Width, x0 + Map.ChunkSize), y1 = Math.Min(map.Height, y0 + Map.ChunkSize);
        for (var z = 0; z < map.Depth; z++)
        for (var y = y0; y < y1; y++)
        for (var x = x0; x < x1; x++)
        {
            var i = map.Idx(x, y, z);
            var block = map.Block[i];
            if (block.Solid)
            {
                // Hidden ore draws as the rock around it, so a cutaway doesn't give a vein away; rock
                // beside a hidden vein shows its stain (an outcrop), a hint for a sharp eye.
                if (block.Ore)
                    block = Mining.ShownBlock(sim, i);
                var outcrop = block.DigTool != null && !map.Block[i].Ore ? OutcropTint(sim, i) : Rgba.White;
                var tiles = (int)WorldScale.LevelHeight;
                AddCube(sim, x, y, z, TopSprite(block, x, y), SideSprite(block, x, y, z * tiles + 1), SideSprite(block, x, y, z * tiles, low: true), outcrop);
                continue;
            }
            if (map.HasFloor[i] && !AboveYou(sim, map.Floor[i], z))
            {
                var floor = sim.World.Get<Thing>(map.Floor[i]);
                // A preset building's foundation shows its kit's floor (planks over the plinth).
                var floorSprite = BuildingKit.Of(sim, map.Floor[i]) is { Kit.Floor.Length: > 0 } floorKit ? floorKit.Kit.Floor : floor.Def.Sprite;
                AddSlab(sim, x, y, z, S(_materials.Resolve(floorSprite, floor.Material).Sprite));
            }
            // The roof over the building you're in is left out, so you can see in.
            if (map.HasRoof[i] && !OfOpen(sim, map.Floor[i]))
            {
                var roof = sim.World.Get<Thing>(map.Floor[i]);
                if (BuildingKit.Of(sim, map.Floor[i]) is { Kit.Roof: { } pitched } roofKit)
                {
                    // Its share of the building's pitched roof.
                    var roofMaterial = roof.Material;
                    BuildingKit.Roof(_quads, pitched, roofKit.Building, new int3(x, y, z),
                        (name, pick) => _materials.Resolve(name, roofMaterial) is var (art, artTint) && V(art) is var looks ? (looks[(int)(pick % (uint)looks.Count)], artTint) : default,
                        Rgba.White, Quad.Level(z) | Quad.Roof | Quad.Occluder);
                }
                else
                {
                    var (roofSprite, roofTint) = _materials.Resolve(roof.Def.Sprite, roof.Material);
                    AddRoof(sim, x, y, z, S(roofSprite), roofTint);
                }
            }
            // An enclosed room's roof, on its walls (not a preset building's: it gets its own when its roof goes on).
            if (AutoRoofOver(map, i) && z + 1 < map.Depth && Buildings.At(sim, new int3(x, y, z)).IsNull)
                AddRoof(sim, x, y, z + 1, S(AutoRoofSprite), Rgba.White);
            var s = map.Structure[i];
            if (s.IsNull || sim.World.Has<Construction>(s) || AboveYou(sim, s, z))
                continue;
            var thing = sim.World.Get<Thing>(s);
            if (thing.Cell != new int3(x, y, z))
                continue; // a wide structure (a nest's footprint) is drawn once, from its centre
            var def = thing.Def;
            // Built from birch? Use the birch art (or tint the generic art).
            var (spriteName, tint) = _materials.Resolve(def.Sprite, thing.Material);
            if (def.Building is { IsWall: true } or { IsDoor: true } && BuildingKit.Of(sim, s) is { } walls)
            {
                // A preset building's wall, post or door frame, pieced together from its kit; cut
                // down to a stub like any wall while the cutaway shows the room behind it.
                var material = thing.Material;
                var stub = IsStub(sim, x, y, z);
                foreach (var (piece, facing) in BuildingKit.WallPieces(walls.Kit, walls.Building, new int3(x, y, z)))
                    PropModel.Build(_quads, piece, new int3(x, y, z), facing,
                        name => (S(_materials.Resolve(name, material).Sprite), _materials.Resolve(name, material).Tint),
                        Rgba.White, Quad.Level(z) | Quad.Occluder, d => Occludes(sim, x + d.X, y + d.Y, z) && !IsStub(sim, x + d.X, y + d.Y, z),
                        rise: stub ? StubHeight : 1f);
                continue; // a door's leaf still swings on its own (Rendering/DoorAnimator)
            }
            if (def.Model is { } model)
            {
                var material = thing.Material;
                PropModel.Build(_quads, model, new int3(x, y, z), thing.Facing,
                    name => (S(_materials.Resolve(name, material).Sprite), _materials.Resolve(name, material).Tint),
                    Rgba.White, Quad.Level(z), d => Occludes(sim, x + d.X, y + d.Y, z));
                // A preset building's upgrades fitted to it (the hearth's cooking pot).
                if (BuildingKit.Of(sim, s) is { } fitted)
                    foreach (var u in fitted.Building.Def.Upgrades)
                        if (fitted.Building.Upgrades.Contains(u.Name) && def.IsA(u.OnDef) && u.Model.Count > 0)
                            PropModel.Build(_quads, u.Model, new int3(x, y, z), thing.Facing,
                                name => (S(_materials.Resolve(name, null).Sprite), _materials.Resolve(name, null).Tint),
                                Rgba.White, Quad.Level(z));
            }
            else if (def.Building?.IsDoor == true)
                continue; // drawn per frame so they can swing (Rendering/DoorAnimator)
            else if (def.Shape == RenderShape.Block)
            {
                // Faces drawn as tall as a level looks (a kiln's mouth) would squash on top: such
                // blocks give their top its own art, <sprite>_top.
                var top = atlas.TryGet(spriteName + "_top", out var own) ? own : S(spriteName);
                AddCube(sim, x, y, z, top, S(spriteName), S(spriteName), tint, IsStub(sim, x, y, z) ? StubHeight : 1f);
            }
            else if (def.Shape == RenderShape.Ramp)
            {
                // Natural slopes take the look of the ground they climb (terrain/slope_grass...).
                if (thing.Material == null && map.GroundUnder(i) is { } ground)
                {
                    var onGround = $"{def.Sprite}_{ground.Name.ToLowerInvariant()}";
                    if (atlas.TryGet(onGround, out _))
                        spriteName = onGround;
                }
                AddRamp(sim, x, y, z, spriteName);
            }
        }
    }

    /// <summary>
    /// Walls and solid blocks hide each other's faces; open air and everything else don't. Doors,
    /// and walls drawn as a model (a fence's post and rails), are thinner than a cell, so the ground
    /// under them and the walls beside them still draw.
    /// </summary>
    internal static bool Occludes(Simulation sim, int x, int y, int z)
    {
        var map = sim.Map;
        if (!map.InBounds(x, y, z))
            return false;
        var i = map.Idx(x, y, z);
        if (map.Block[i].Solid)
            return true;
        var s = map.Structure[i];
        if (s.IsNull || sim.World.Has<Construction>(s))
            return false;
        var def = sim.World.Get<Thing>(s).Def;
        return def.Shape == RenderShape.Block && def.Model == null && def.Building?.IsDoor != true;
    }

    /// <summary>A wall of any kind (a fence too) or solid rock: what a door or gate lines up with.</summary>
    internal static bool WallAt(Simulation sim, int x, int y, int z)
    {
        if (Occludes(sim, x, y, z))
            return true;
        var map = sim.Map;
        if (!map.InBounds(x, y, z))
            return false;
        var s = map.Structure[map.Idx(x, y, z)];
        return !s.IsNull && !sim.World.Has<Construction>(s) && sim.World.Get<Thing>(s).Def.Building is { IsWall: true };
    }

    private static readonly int3[] Around6 =
        [new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0), new(0, -1, 0), new(0, 0, -1), new(0, 0, 1)];

    /// <summary>The stain of a hidden ore beside this rock (<see cref="BlockDef.Stain"/>), or white.</summary>
    private static uint OutcropTint(Simulation sim, int i)
    {
        var map = sim.Map;
        var c = map.CellOf(i);
        foreach (var d in Around6)
        {
            var n = c + d;
            if (!map.InBounds(n))
                continue;
            var j = map.Idx(n);
            if (map.Block[j] is { Ore: true, Stain: { Length: 3 } stain } && !Mining.Exposed(map, j))
                return Rgba.Pack(stain[0], stain[1], stain[2]);
        }
        return Rgba.White;
    }

    /// <param name="side">The side texture for the upper tiles of each face.</param>
    /// <param name="lowSide">The side texture for the bottom tile (a different variant, so the stack doesn't repeat).</param>
    /// <param name="height">Below 1: a wall cut down to a stub (<see cref="CutHeading"/>), its faces the bottom of the art.</param>
    private void AddCube(Simulation sim, int x, int y, int z, Sprite top, Sprite side, Sprite lowSide, uint color, float height = 1f)
    {
        var level = Quad.Level(z) | Quad.Occluder;
        var stub = height < 1f;
        var buried = !stub && Occludes(sim, x, y, z + 1);
        var topAo = buried ? Quad.NoAo : TopAo(sim, x, y, z + 1);
        _quads.Add(new Quad
        {
            Origin = new float3(x, y, z + height),
            AxisU = new float3(1, 0, 0),
            AxisV = new float3(0, 1, 0),
            Flags = level | (buried ? Quad.Cap : 0),
            Color = buried ? Rgba.Pack(0.62f, 0.6f, 0.58f) : color,
            Ao = topAo,
            Uv = top.Uv,
        });

        // Sides: V runs down the face so the sprite's top row is at the top edge; the texture stacks
        // up the level so its pixels stay square (WorldScale).
        Side(x + 1, y, new float3(x + 1, y + 1, 0), new float3(0, -1, 0), 0.78f);
        Side(x - 1, y, new float3(x, y, 0), new float3(0, 1, 0), 0.66f);
        Side(x, y + 1, new float3(x, y + 1, 0), new float3(1, 0, 0), 0.88f);
        Side(x, y - 1, new float3(x + 1, y, 0), new float3(-1, 0, 0), 0.6f);

        // A face is hidden by a solid neighbour, except where that neighbour is a cut-down stub:
        // then the part above the stub shows.
        void Side(int nx, int ny, float3 corner, float3 u, float shade)
        {
            if (!Occludes(sim, nx, ny, z))
                AddSide(corner with { Z = z + height }, u, shade, height);
            else if (!stub && IsStub(sim, nx, ny, z))
                AddSide(corner with { Z = z + 1 }, u, shade, 1f - StubHeight, partial: true);
        }

        void AddSide(float3 origin, float3 u, float shade, float faceHeight, bool partial = false)
        {
            // Darken the bottom edge a little: cheap contact shadow where the face meets the ground.
            var b = (uint)(shade * 255f);
            var bottom = (uint)(shade * 0.8f * 255f);
            var template = new Quad { Flags = level, Color = color };
            var tiles = WorldScale.TilesPerLevel(side);
            var n = WorldScale.TileCount(faceHeight, tiles);
            for (var k = 0; k < n; k++)
            {
                // A stub shows the bottom of the art (the low variant, meeting the ground); a full
                // face stacks down from the top; the part over a stub stops short of the ground.
                var last = k == n - 1;
                var low = stub || (last && !partial);
                var q = WorldScale.Tile(template, origin, u, faceHeight, (low ? lowSide : side).Uv, tiles, k, fromBottom: stub);
                q.Ao = low ? b | b << 8 | bottom << 16 | bottom << 24 : b | b << 8 | b << 16 | b << 24;
                _quads.Add(q);
            }
        }
    }

    /// <summary>Corner shades for a top face whose air cell is (x, y, z), from occluders around it.</summary>
    private static uint TopAo(Simulation sim, int x, int y, int z)
    {
        // Corners in quad order: (0,0) (1,0) (1,1) (0,1) → offsets towards each corner.
        ReadOnlySpan<int2> dirs = [new(-1, -1), new(1, -1), new(1, 1), new(-1, 1)];
        uint packed = 0;
        for (var k = 0; k < 4; k++)
        {
            var d = dirs[k];
            var s1 = Occludes(sim, x + d.X, y, z) ? 1 : 0;
            var s2 = Occludes(sim, x, y + d.Y, z) ? 1 : 0;
            var corner = Occludes(sim, x + d.X, y + d.Y, z) ? 1 : 0;
            var ao = s1 == 1 && s2 == 1 ? 0 : 3 - (s1 + s2 + corner);
            packed |= (uint)(AoShades[ao] * 255f) << (8 * k);
        }
        return packed;
    }

    private const float SlabThickness = 0.15f;

    /// <summary>
    /// A floor: its top flush with the cell's floor height (a hair above, so floors laid on ground
    /// don't fight the ground's top face), with thin edges where it doesn't meet another floor or a solid.
    /// </summary>
    private void AddSlab(Simulation sim, int x, int y, int z, Sprite sprite)
    {
        var map = sim.Map;
        var top = z + 0.01f;
        _quads.Add(new Quad
        {
            Origin = new float3(x, y, top),
            AxisU = new float3(1, 0, 0),
            AxisV = new float3(0, 1, 0),
            Flags = Quad.Level(z),
            Color = Rgba.White,
            Ao = TopAo(sim, x, y, z),
            Uv = sprite.Uv,
        });
        if (z > 0 && map.IsSolid(map.Idx(x, y, z - 1)))
            return; // laid on the ground: no visible edges

        Edge(x + 1, y, new float3(x + 1, y + 1, top), new float3(0, -1, 0), 0.78f);
        Edge(x - 1, y, new float3(x, y, top), new float3(0, 1, 0), 0.66f);
        Edge(x, y + 1, new float3(x, y + 1, top), new float3(1, 0, 0), 0.88f);
        Edge(x, y - 1, new float3(x + 1, y, top), new float3(-1, 0, 0), 0.6f);

        void Edge(int nx, int ny, float3 origin, float3 u, float shade)
        {
            if (map.InBounds(nx, ny, z) && (map.HasFloor[map.Idx(nx, ny, z)] || Occludes(sim, nx, ny, z)))
                return;
            var b = (uint)(shade * 255f);
            // A thin strip of the sprite, as tall as the edge looks, so the planks don't squash.
            var template = new Quad { Flags = Quad.Level(z), Color = Rgba.White, Ao = b | b << 8 | b << 16 | b << 24 };
            _quads.Add(WorldScale.Tile(template, origin, u, SlabThickness, sprite.Uv, WorldScale.TilesPerLevel(sprite), 0));
        }
    }

    /// <summary>What the game draws over enclosed rooms (which the sim roofs without a built roof).</summary>
    private const string AutoRoofSprite = "buildings/roof_turf"; // sod over birch bark: the Norse house's roof

    private const float RoofThickness = 0.12f;

    /// <summary>How far a roof reaches past its last cell (over the wall's outer face).</summary>
    private const float Eave = 0.14f;

    /// <summary>The outline drawn round a roofed area while roofs are hidden: its width, and colour.</summary>

    /// <summary>
    /// The cell at <paramref name="i"/> is under an enclosed room's roof, and nothing (a floor, a
    /// built roof, rock) is above it: the game draws that roof one level up.
    /// </summary>
    private static bool AutoRoofOver(Map map, int i) =>
        map.Roofed[i] && map.Active[i] && !map.IsSolid(i) && !map.CoveredAbove(i);

    /// <summary>A roof (built, or an enclosed room's) at the bottom of cell (x, y, z).</summary>
    internal static bool RoofAt(Map map, int x, int y, int z)
    {
        if (!map.InBounds(x, y, z))
            return false;
        var i = map.Idx(x, y, z);
        return map.HasRoof[i] || (z > 0 && AutoRoofOver(map, i - map.LayerSize));
    }

    /// <summary>
    /// A roof resting on the walls at the bottom of level <paramref name="z"/>: a thin slab, its
    /// open sides reaching out as eaves with an edge, flagged <see cref="Quad.Roof"/> so the roof
    /// toggle hides it.
    /// </summary>
    private void AddRoof(Simulation sim, int x, int y, int z, Sprite sprite, uint tint)
    {
        var map = sim.Map;
        bool east = !RoofAt(map, x + 1, y, z), west = !RoofAt(map, x - 1, y, z);
        bool south = !RoofAt(map, x, y + 1, z), north = !RoofAt(map, x, y - 1, z);
        float x0 = x - (west ? Eave : 0), x1 = x + 1 + (east ? Eave : 0);
        float y0 = y - (north ? Eave : 0), y1 = y + 1 + (south ? Eave : 0);
        var top = z + RoofThickness;
        var flags = Quad.Level(z) | Quad.Roof | Quad.Occluder;
        const uint lit = 0xF2F2F2F2; // a touch darker than the ground, so the roof reads as its own surface
        _quads.Add(new Quad
        {
            Origin = new float3(x0, y0, top),
            AxisU = new float3(x1 - x0, 0, 0),
            AxisV = new float3(0, y1 - y0, 0),
            Flags = flags,
            Color = tint,
            Ao = lit,
            Uv = sprite.Uv,
        });
        if (east) Edge(new float3(x1, y1, top), new float3(0, y0 - y1, 0), 0.78f);
        if (west) Edge(new float3(x0, y0, top), new float3(0, y1 - y0, 0), 0.66f);
        if (south) Edge(new float3(x0, y1, top), new float3(x1 - x0, 0, 0), 0.88f);
        if (north) Edge(new float3(x1, y0, top), new float3(x0 - x1, 0, 0), 0.6f);

        void Edge(float3 origin, float3 u, float shade)
        {
            var b = (uint)(shade * 255f);
            var template = new Quad { Flags = flags, Color = tint, Ao = b | b << 8 | b << 16 | b << 24 };
            _quads.Add(WorldScale.Tile(template, origin, u, RoofThickness, sprite.Uv, WorldScale.TilesPerLevel(sprite), 0));
        }
    }

    /// <summary>A slope from this cell's floor up to the next level, towards the higher neighbor.</summary>
    /// <summary>
    /// A ramp (stairs or a natural slope): the sloped surface, plus a right-triangle face on each
    /// open side so it reads as a solid wedge of earth or a stair stringer. The side art is the top
    /// sprite's name + <c>_side</c>, alpha-cut to the triangle with the high end on the left; with
    /// no side art the sides are left open.
    /// </summary>
    private void AddRamp(Simulation sim, int x, int y, int z, string spriteName)
    {
        var map = sim.Map;
        var dir = new int2(0, -1);
        foreach (var d in Map.Neighbors4)
            if (map.InBounds(x + d.X, y + d.Y, z + 1) && map.Standable(map.Idx(x + d.X, y + d.Y, z + 1)))
            {
                dir = d;
                break;
            }
        var center = new float3(x + 0.5f, y + 0.5f, z);
        var half = new float3(dir.X * 0.5f, dir.Y * 0.5f, 0);
        var high = center + half + new float3(0, 0, 1);
        var low = center - half;
        var across = new float3(-dir.Y, dir.X, 0);
        var h = Variation.Hash(x, y, 0x5A7);
        // Mirrored across the slope at random (never up/down: the art has a high end).
        var top = Variation.Mirror(Variation.Pick(V(spriteName), x, y, 0x5A8), (h & 1) != 0, false);
        _quads.Add(new Quad
        {
            // Sprite top row at the high edge.
            Origin = high - across * 0.5f,
            AxisU = across,
            AxisV = low - high,
            Flags = Quad.Level(z) | Quad.Billboard,
            Color = Rgba.White,
            Ao = Quad.NoAo,
            Uv = top.Uv,
        });

        if (!atlas.TryGet(spriteName + "_side", out var side))
            return;
        var back = -half * 2f;
        foreach (var s in (ReadOnlySpan<float>)[-0.5f, 0.5f])
        {
            var nx = x + (int)MathF.Round(across.X * s * 2f);
            var ny = y + (int)MathF.Round(across.Y * s * 2f);
            if (Occludes(sim, nx, ny, z))
                continue;
            // Darker towards the ground, like block sides.
            var shade = s < 0 ? 0.72f : 0.84f;
            var b = (uint)(shade * 255f);
            var bottom = (uint)(shade * 0.8f * 255f);
            _quads.Add(new Quad
            {
                Origin = high + across * s,
                AxisU = back,
                AxisV = new float3(0, 0, -1),
                Flags = Quad.Level(z),
                Color = Rgba.White,
                Ao = b | b << 8 | bottom << 16 | bottom << 24,
                Uv = side.Uv,
            });
        }
    }
}

using Brutal.Numerics;
using Narsil.Rendering;
using HeroesNotIncluded.Rendering;
using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Tests;

/// <summary>Preset buildings drawn with their art kits (<see cref="BuildingKit"/>): wall pieces and the pitched roof.</summary>
public class BuildingKitTests
{
    private static readonly Lazy<DefDatabase> Defs = new(() => DefDatabase.LoadDirectory(DevPaths.Defs!));
    private static readonly Sprite Art = new(new float4(0.5f, 0.25f, 0.75f, 0.5f), new int2(32, 32));

    private static (Simulation Sim, PresetBuilding Building) Finished(string name, int facing)
    {
        var sim = Simulation.CreateStandard(Defs.Value, 40, 40, 1, 5);
        sim.RebuildDerivedIfNeeded();
        var e = Buildings.SpawnFinished(sim, Defs.Value.Building(name), new int3(20, 20, 1), facing);
        return (sim, sim.World.Get<PresetBuilding>(e));
    }

    /// <summary>A model box in the cell, turned as it's drawn.</summary>
    private static (float3 Min, float3 Max) Turned(ModelBox b, int facing)
    {
        var a = PropModel.RotatePoint(new float3(b.Min[0], b.Min[1], b.Min[2]), facing);
        var c = PropModel.RotatePoint(new float3(b.Max[0], b.Max[1], b.Max[2]), facing);
        return (new float3(MathF.Min(a.X, c.X), MathF.Min(a.Y, c.Y), a.Z), new float3(MathF.Max(a.X, c.X), MathF.Max(a.Y, c.Y), c.Z));
    }

    /// <summary>
    /// No two pieces of a wall cell (a corner's post and its arms) have faces lying in the same
    /// plane, the same way, over each other where nothing covers them: those flicker as the camera
    /// moves (playtest: the inn's corners).
    /// </summary>
    [Theory]
    [InlineData("Inn", false)]
    [InlineData("Inn", true)]
    [InlineData("Cottage", false)]
    [InlineData("Cottage", true)]
    [InlineData("Dwelling", false)]
    [InlineData("Shrine", false)]
    [InlineData("Shrine", true)]
    [InlineData("Barn", false)]
    public void WallPiecesNeverShareAFace(string building, bool cutDown)
    {
        var (sim, b) = Finished(building, 0);
        var kit = b.VariantDef.KitDef!;
        const float e = 1e-5f;
        foreach (var p in Buildings.Plot(sim.Map, b.Def, b.Anchor, b.Facing))
        {
            // Cut down to a stub as the cutaway draws it (WorldMesher): each piece at its own height.
            var boxes = BuildingKit.WallPieces(kit, b, p.Cell).SelectMany((piece, i) =>
            {
                var cut = cutDown ? WorldMesher.StubHeight + BuildingKit.CutLift(i, piece.Facing) : float.MaxValue;
                return piece.Model.Where(box => box.Min[2] < cut).Select(box =>
                {
                    var t = Turned(box, piece.Facing);
                    return (Piece: i, Box: (Min: t.Min, Max: new float3(t.Max.X, t.Max.Y, MathF.Min(t.Max.Z, cut))));
                });
            }).ToList();
            bool Covered(float3 at, int axis) => boxes.Any(o =>
            {
                for (var k = 0; k < 3; k++)
                    if (at[k] < o.Box.Min[k] - e || at[k] > o.Box.Max[k] + e || (k == axis && (MathF.Abs(at[k] - o.Box.Min[k]) < e || MathF.Abs(at[k] - o.Box.Max[k]) < e)))
                        return false;
                return true;
            });
            foreach (var (pa, a) in boxes)
            foreach (var (pb, c) in boxes)
            {
                if (pa >= pb)
                    continue;
                for (var axis = 0; axis < 3; axis++)
                foreach (var high in new[] { true, false })
                {
                    if (axis == 2 && !high)
                        continue; // undersides are never seen
                    var pa0 = high ? a.Max[axis] : a.Min[axis];
                    var pb0 = high ? c.Max[axis] : c.Min[axis];
                    if (MathF.Abs(pa0 - pb0) > e)
                        continue;
                    int u = (axis + 1) % 3, v = (axis + 2) % 3;
                    float u0 = MathF.Max(a.Min[u], c.Min[u]), u1 = MathF.Min(a.Max[u], c.Max[u]);
                    float v0 = MathF.Max(a.Min[v], c.Min[v]), v1 = MathF.Min(a.Max[v], c.Max[v]);
                    if (u1 - u0 < e || v1 - v0 < e)
                        continue;
                    for (var i = 1; i < 8; i++)
                    for (var j = 1; j < 8; j++)
                    {
                        var at = new float3();
                        at[axis] = pa0;
                        at[u] = u0 + (u1 - u0) * i / 8f;
                        at[v] = v0 + (v1 - v0) * j / 8f;
                        Assert.True(Covered(at, axis), $"{building}{(cutDown ? " cut down" : "")} at {p.Cell} ({p.Symbol}): pieces {pa} and {pb} share a face on axis {axis} at {pa0}");
                    }
                }
            }
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void WallsArePiecedFromTheirNeighbours(int facing)
    {
        var (sim, b) = Finished("Dwelling", facing);
        var kit = b.VariantDef.KitDef!;
        foreach (var p in Buildings.Plot(sim.Map, b.Def, b.Anchor, b.Facing))
        {
            var pieces = BuildingKit.WallPieces(kit, b, p.Cell).ToList();
            var corner = (p.Col == 0 || p.Col == b.Def.Width - 1) && (p.Row == 0 || p.Row == b.Def.Depth - 1);
            switch (p.Symbol)
            {
                case 'D':
                    Assert.Same(kit.Door, Assert.Single(pieces).Model);
                    break;
                case 'W' when corner:
                    // A corner post and an arm along each of its two walls.
                    Assert.Equal([kit.Corner.Count, kit.Arm.Count, kit.Arm.Count], pieces.Select(x => x.Model.Count));
                    break;
                case 'W':
                    var span = Assert.Single(pieces);
                    Assert.Same(kit.Span, span.Model);
                    // It runs the way its wall does on the map, whatever the building's turn.
                    var alongX = sim.Map.Structure[sim.Map.Idx(p.Cell + new int3(1, 0, 0))] is { IsNull: false } east
                        && sim.World.Get<Thing>(east).Def.Building is { IsWall: true } or { IsDoor: true };
                    Assert.Equal(alongX ? 0 : 1, span.Facing);
                    break;
            }
        }
    }

    [Theory]
    [InlineData("Dwelling", 0)]
    [InlineData("Longhouse", 1)]
    public void TheRoofIsPitchedOverThePlot(string name, int facing)
    {
        var (sim, b) = Finished(name, facing);
        var roof = b.VariantDef.KitDef!.Roof!;
        var quads = new List<Quad>();
        var flags = Quad.Level(2) | Quad.Roof;
        var cells = Buildings.Plot(sim.Map, b.Def, b.Anchor, b.Facing).Select(p => p.Cell + new int3(0, 0, 1)).ToList();
        foreach (var c in cells)
        {
            var before = quads.Count;
            BuildingKit.Roof(quads, roof, b, c, (_, _) => (Art, Rgba.White), Rgba.White, flags);
            Assert.True(quads.Count > before, $"roof cell {c} drew nothing");
        }
        Assert.All(quads, q => Assert.Equal(flags, q.Flags)); // all hidden with the roofs, on the roof's level

        // It rises to a ridge over the middle, as high as the pitch makes it, and overhangs the plot.
        float Top(in Quad q) => Math.Max(Math.Max(q.Origin.Z, (q.Origin + q.AxisU).Z), Math.Max((q.Origin + q.AxisV).Z, (q.Origin + q.AxisU + q.AxisV).Z));
        var span = Math.Min(b.Def.Width, b.Def.Depth);
        var apex = 2 + roof.Pitch * (span / 2f - 0.5f) + roof.Thickness;
        Assert.InRange(quads.Max(q => Top(q)), apex - 0.01f, apex + roof.Pitch * roof.Horns + 0.3f);
        var xs = quads.SelectMany(q => new[] { q.Origin.X, (q.Origin + q.AxisU).X, (q.Origin + q.AxisV).X });
        var plotX = cells.Select(c => (float)c.X).ToList();
        Assert.True(xs.Min() < plotX.Min() && xs.Max() > plotX.Max() + 1, "the eaves and gables reach past the walls");
    }

    [Fact]
    public void EveryLookHasAKitWithItsArt()
    {
        var defs = Defs.Value;
        foreach (var b in defs.AllBuildings)
        foreach (var v in b.Variants)
        {
            var kit = Assert.IsType<BuildingKitDef>(v.KitDef);
            Assert.NotEmpty(kit.Span);
            Assert.NotEmpty(kit.Arm);
            Assert.NotEmpty(kit.Corner);
            Assert.NotNull(kit.Roof);
        }
    }
}

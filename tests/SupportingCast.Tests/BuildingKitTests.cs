using Brutal.Numerics;
using Narsil.Rendering;
using SupportingCast.Rendering;
using SupportingCast.Sim;
using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Tests;

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
                    Assert.Equal([kit.Corner, kit.Arm, kit.Arm], pieces.Select(x => x.Model));
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

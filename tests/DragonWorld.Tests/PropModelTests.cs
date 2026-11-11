using Brutal.Numerics;
using Narsil.Rendering;
using DragonWorld.Rendering;
using DragonWorld.Sim.Defs;

namespace DragonWorld.Tests;

/// <summary>Box models (furniture, item piles) turned into quads.</summary>
public class PropModelTests
{
    private static readonly Sprite Art = new(new float4(0.5f, 0.25f, 0.75f, 0.5f), new int2(32, 32));

    private static (Sprite, uint) Resolve(string _) => (Art, Rgba.White);

    private static ModelBox Box(float x0, float y0, float z0, float x1, float y1, float z1, int minCount = 0, string front = "") =>
        new() { Min = [x0, y0, z0], Max = [x1, y1, z1], Top = "t", Side = "s", Front = front, MinCount = minCount };

    private static List<Quad> Build(IReadOnlyList<ModelBox> model, int facing = 0, Func<int2, bool>? occluded = null, int count = int.MaxValue, float rise = 1f)
    {
        var quads = new List<Quad>();
        PropModel.Build(quads, model, new int3(10, 20, 3), facing, Resolve, Rgba.White, Quad.Level(3), occluded, count, rise);
        return quads;
    }

    private static float3 Normal(in Quad q)
    {
        float3 a = q.AxisU, b = q.AxisV;
        return new float3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    }

    [Fact]
    public void ABoxIsATopAndFourOutwardSides()
    {
        var quads = Build([Box(0.2f, 0.2f, 0, 0.8f, 0.8f, 0.5f)]);
        Assert.Equal(5, quads.Count);
        var center = new float3(10.5f, 20.5f, 3.25f);
        foreach (var q in quads)
        {
            // Every face points away from the box's middle (cross(U, V) is the outward normal).
            var mid = q.Origin + (q.AxisU + q.AxisV) * 0.5f;
            var n = Normal(q);
            var away = mid - center;
            Assert.True(n.X * away.X + n.Y * away.Y + n.Z * away.Z > 0, $"face at {mid} points inwards");
        }
        Assert.Contains(quads, q => Normal(q).Z > 0 && MathF.Abs(q.Origin.Z - 3.5f) < 1e-4f);
    }

    [Fact]
    public void FacesShowThePartOfTheSpriteMatchingTheirSize()
    {
        var quads = Build([Box(0, 0, 0, 1, 1, 0.25f)]);
        var side = quads.First(q => Normal(q).Z == 0);
        Assert.Equal(0.5f, side.Uv.X);
        Assert.Equal(0.75f, side.Uv.Z, 4); // a full cell wide
        // A quarter of a cell high, which looks LevelHeight times taller: that much of the sprite.
        Assert.Equal(0.25f + 0.25f * 0.25f * WorldScale.LevelHeight, side.Uv.W, 4);
    }

    [Fact]
    public void TurningMovesTheFrontAround()
    {
        var model = new[] { Box(0.1f, 0.1f, 0, 0.9f, 0.9f, 0.5f, front: "front") };
        Assert.Equal(new int2(0, 1), PropModel.FrontDirection(0));
        Assert.Equal(new int2(-1, 0), PropModel.FrontDirection(1));
        Assert.Equal(new int2(0, -1), PropModel.FrontDirection(2));
        Assert.Equal(new int2(1, 0), PropModel.FrontDirection(3));
        // Turned, the box stays inside its cell.
        for (var f = 0; f < 4; f++)
            foreach (var q in Build(model, f))
                foreach (var p in new[] { q.Origin, q.Origin + q.AxisU, q.Origin + q.AxisV, q.Origin + q.AxisU + q.AxisV })
                {
                    Assert.InRange(p.X, 10f, 11f);
                    Assert.InRange(p.Y, 20f, 21f);
                }
    }

    [Fact]
    public void FacesAgainstAWallAreLeftOut()
    {
        // A headboard flush with the north edge, a wall to the north.
        var model = new[] { Box(0, 0, 0, 1, 0.1f, 0.6f) };
        var open = Build(model);
        var walled = Build(model, occluded: d => d == new int2(0, -1));
        // The north face (stacked in tiles up its height; Normal isn't unit length) is left out, nothing else.
        Assert.Equal(open.Count - open.Count(q => Normal(q).Y < -1e-4f), walled.Count);
        Assert.DoesNotContain(walled, q => Normal(q).Y < -1e-4f);
        // Turned half round, the same face now points south: the north wall hides nothing of it.
        Assert.Equal(open.Count, Build(model, facing: 2, occluded: d => d == new int2(0, -1)).Count);
    }

    [Fact]
    public void PilesGrowWithTheStackAndBuildingsRise()
    {
        var pile = new[] { Box(0, 0, 0, 1, 0.3f, 0.2f, 1), Box(0, 0.4f, 0, 1, 0.7f, 0.2f, 10), Box(0, 0, 0.2f, 1, 0.3f, 0.4f, 20) };
        Assert.Equal(5, Build(pile, count: 5).Count);
        Assert.Equal(10, Build(pile, count: 10).Count);
        Assert.Equal(15, Build(pile, count: 50).Count);

        var half = Build([Box(0, 0, 0, 1, 1, 0.8f)], rise: 0.5f);
        Assert.All(half, q => Assert.True(q.Origin.Z <= 3.4001f));
        Assert.Empty(Build([Box(0, 0, 0, 1, 1, 0.8f)], rise: 0f));
    }

    [Fact]
    public void LyingOnTheMattress()
    {
        var bed = new[]
        {
            Box(0.1f, 0.1f, 0, 0.9f, 0.9f, 0.25f), Box(0.1f, 0.1f, 0.25f, 0.9f, 0.9f, 0.38f),
            Box(0.2f, 0.12f, 0.38f, 0.8f, 0.3f, 0.45f), Box(0, 0, 0, 1, 0.1f, 0.7f),
        };
        Assert.Equal(0.38f, PropModel.SurfaceAt(bed, 0.5f, 0.5f), 4);
        Assert.Equal(0.45f, PropModel.SurfaceAt(bed, 0.5f, 0.2f), 4); // the pillow
        Assert.Equal(0f, PropModel.SurfaceAt(bed, 0.95f, 0.5f), 4);
    }

    [Fact]
    public void ALiftedModelSitsOnAFurnitureTop()
    {
        var quads = new List<Quad>();
        PropModel.Build(quads, [Box(0, 0, 0, 1, 1, 0.2f)], new int3(10, 20, 3), 0, Resolve, Rgba.White, Quad.Level(3), lift: 0.66f);
        Assert.All(quads, q => Assert.True(q.Origin.Z >= 3.66f - 1e-4f));
    }

    [Fact]
    public void ItemsInAFurnishedCellRestOnItsTop()
    {
        var defs = DefDatabase.LoadDirectory(DevPaths.Defs!);
        var sim = DragonWorld.Sim.Simulation.CreateStandard(defs, 20, 20, 1);
        var at = new int3(5, 5, 1);
        Assert.Equal(0f, WorldRenderer.SurfaceUnder(sim.Map, sim.World, at));
        var site = sim.PlaceBlueprint(defs.Thing("Table"), at);
        Assert.Equal(0f, WorldRenderer.SurfaceUnder(sim.Map, sim.World, at)); // not while it's a blueprint
        sim.CompleteConstruction(site);
        Assert.Equal(defs.Thing("Table").Building!.SurfaceHeight, WorldRenderer.SurfaceUnder(sim.Map, sim.World, at));
        Assert.True(sim.CanAcceptItem(at, defs.Thing("Wood"), out _)); // and things can be put there
    }
}

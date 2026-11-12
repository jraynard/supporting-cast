using Brutal.ImGuiApi;
using Narsil;
using Narsil.Rendering;

namespace SupportingCast.Debug;

/// <summary>Sprite renderer and camera stress scene. Run with --render-test [--stress].</summary>
public sealed class RenderTestGame : Game
{
    private const int MapSize = 200;
    private readonly Camera2D _camera = new() { Position = new float2(MapSize / 2f, MapSize / 2f) };
    private CameraController _controller = null!;
    private readonly string[] _terrain = ["terrain/grass", "terrain/dirt", "terrain/sand", "terrain/rock_floor", "terrain/water"];
    private int[] _tiles = [];
    private bool _stress;
    private int _stressCount = 50_000;
    private float _time;
    private float _logTimer;
    public bool StressFromArgs { get; init; }

    public override void Load()
    {
        _stress = StressFromArgs;
        _controller = new CameraController(_camera) { Bounds = (float2.Zero, new float2(MapSize, MapSize)) };
        var rnd = new Random(1);
        _tiles = new int[MapSize * MapSize];
        for (var i = 0; i < _tiles.Length; i++)
            _tiles[i] = rnd.Next(10) < 7 ? 0 : rnd.Next(_terrain.Length);
    }

    public override void Update(float dt)
    {
        _time += dt;
        var extent = Host.Renderer.Swapchain.Extent;
        _camera.ViewportSize = new float2(extent.X, extent.Y);
        _controller.Update(Host.Input, dt);
        if (_stress && (_logTimer += dt) > 2f)
        {
            _logTimer = 0;
            Log.Engine.Info($"stress: {Host.Sprites.LastFrameSpriteCount:N0} sprites, {Host.FrameTimeMs:F2} ms/frame");
        }
    }

    public override void DrawUi()
    {
        ImGui.Begin("Render test"u8, ImGuiWindowFlags.AlwaysAutoResize);
        ImGui.Text($"GPU: {Host.Vulkan.DeviceName}");
        ImGui.Text($"Frame: {Host.FrameTimeMs:F2} ms ({1000f / Host.FrameTimeMs:F0} fps)");
        ImGui.Text($"Sprites: {Host.Sprites.LastFrameSpriteCount:N0}");
        ImGui.Text($"Camera: ({_camera.Position.X:F1}, {_camera.Position.Y:F1}) zoom {_camera.Zoom:F1}");
        var vsync = Host.Renderer.VSync;
        if (ImGui.Checkbox("VSync"u8, ref vsync))
            Host.Renderer.SetVSync(vsync);
        ImGui.Checkbox("Stress test"u8, ref _stress);
        ImGui.InputInt("Count"u8, ref _stressCount, 10_000, 100_000, ImGuiInputTextFlags.None);
        ImGui.End();
    }

    public override void Draw(in FrameContext frame)
    {
        var sb = Host.Sprites;
        var atlas = Host.Atlas;
        sb.Begin(_camera);

        // Only draw visible tiles.
        var (min, max) = _camera.VisibleBounds();
        int x0 = Math.Max(0, (int)min.X), y0 = Math.Max(0, (int)min.Y);
        int x1 = Math.Min(MapSize - 1, (int)max.X), y1 = Math.Min(MapSize - 1, (int)max.Y);
        for (var y = y0; y <= y1; y++)
        for (var x = x0; x <= x1; x++)
            sb.DrawRect(atlas.Get(_terrain[_tiles[y * MapSize + x]]), new float2(x, y), new float2(1, 1));

        var c = new float2(MapSize / 2f, MapSize / 2f);
        sb.DrawRect(atlas.Get("things/oak"), c + new float2(-3, -2), new float2(1, 1));
        sb.DrawRect(atlas.Get("things/boulder"), c + new float2(2, -2), new float2(1, 1));
        sb.DrawRect(atlas.Get("buildings/campfire"), c, new float2(1, 1));
        sb.DrawRect(atlas.Get("creatures/dragon_egg"), c + new float2(1, 0), new float2(1, 1));
        sb.DrawRect(atlas.Get("creatures/human"), c + new float2(-1, 1), new float2(1, 1));
        sb.Draw(atlas.Get("creatures/dragon_hatchling"), c + new float2(3.5f, 1.5f), new float2(1, 1), Rgba.White, MathF.Sin(_time) * 0.3f);
        sb.OutlineRect(c, new float2(1, 1), 0.05f, Rgba.Pack(1, 1, 0.3f));
        sb.DrawLine(c + new float2(-0.5f, 1.5f), c + new float2(3.5f, 1.5f), 0.06f, Rgba.Pack(1, 1, 1, 0.6f));

        if (_stress)
        {
            var sprites = new[] { atlas.Get("creatures/human"), atlas.Get("creatures/dragon_hatchling"), atlas.Get("items/wood") };
            var rnd = new Random(42);
            for (var i = 0; i < _stressCount; i++)
            {
                var p = new float2(rnd.NextSingle() * MapSize, rnd.NextSingle() * MapSize);
                var wobble = new float2(MathF.Sin(_time * 2 + i), MathF.Cos(_time * 2 + i)) * 0.3f;
                sb.Draw(sprites[i % 3], p + wobble, new float2(1, 1));
            }
        }
        sb.End();
    }
}

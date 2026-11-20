using Brutal.ImGuiApi;
using Narsil;
using Narsil.Rendering;
using HeroesNotIncluded.Rendering;

namespace HeroesNotIncluded.Debug;

/// <summary>
/// The artist's view (<c>--art-preview</c>): every animated sheet, every tag looping side by side at
/// an integer zoom, with pivot and named points marked. Saves under data/textures reload live (see
/// <see cref="ArtReloader"/>), so this is the quickest way to check an Aseprite export.
/// </summary>
public sealed class ArtPreviewGame : Game
{
    private float _time;
    private float _speed = 1f;
    private int _zoom = 3;
    private bool _flip;
    private bool _markers = true;
    private bool _dressed = true;
    private float _contentEnd;
    private float _contentMax = 4000f;
    private int _background = 1;
    private float _scroll;
    private ArtReloader _reloader = null!;

    private static readonly uint[] Backgrounds =
    [
        Rgba.Pack(0.08f, 0.08f, 0.1f),
        Rgba.Pack(0.32f, 0.45f, 0.26f),
        Rgba.Pack(0.75f, 0.75f, 0.75f),
        Rgba.Pack(0.05f, 0.07f, 0.18f),
    ];

    private string? _shaderError;

    private Sim.Defs.DefDatabase? _defs;

    /// <summary>Just the dressed people, not every sheet above them (<c>--art-preview --dressed</c>).</summary>
    public bool DressedOnly { get => _dressedOnly; set => _dressedOnly = value; }

    private bool _dressedOnly;

    public override void Load()
    {
        _reloader = new ArtReloader(Host.Atlas, DevPaths.Watch("SC_WATCH_ART"));
        try
        {
            _defs = Mods.GameContent.Current.LoadDefs(Host.DataDirectory);
        }
        catch (Exception ex)
        {
            Narsil.Log.Render.Warning($"Art preview: no defs for the dressed rows ({ex.Message})");
        }
    }

    public override void Update(float dt)
    {
        _time += dt * _speed;
        _reloader.Update(Host.Input, dt);
        if (Host.Input.WasKeyPressed(GlfwKey.F7))
            _shaderError = ShaderReload.Reload(Host, null);
        if (Host.Input.WasKeyPressed(GlfwKey.F))
            _flip = !_flip;
        if (!ImGui.GetIO().WantCaptureMouse)
            _scroll = Math.Clamp(_scroll - Host.Input.ScrollDelta * 160f, 0f, _contentMax);
    }

    public override void DrawUi()
    {
        ImGui.Begin("Art preview"u8, ImGuiWindowFlags.AlwaysAutoResize);
        ImGui.Text($"{Host.Atlas.Sheets.Count} sheets, {Host.Atlas.Sprites.Count} sprites. F5 reloads art, F7 shaders, F flips.");
        if (_shaderError != null)
            Narsil.Ui.ImGuiText.Colored(new float4(1f, 0.4f, 0.3f, 1f), $"Shader error: {_shaderError}");
        ImGui.SliderInt("Zoom"u8, ref _zoom, 1, 8);
        ImGui.SliderFloat("Speed"u8, ref _speed, 0f, 2f);
        ImGui.SliderFloat("Scroll (wheel)"u8, ref _scroll, 0f, _contentMax);
        ImGui.Checkbox("Flip (facing left)"u8, ref _flip);
        ImGui.Checkbox("Pivot & points"u8, ref _markers);
        ImGui.Checkbox("Dressed people"u8, ref _dressed);
        ImGui.SameLine();
        ImGui.Checkbox("Only them"u8, ref _dressedOnly);
        ImGui.Combo("Background"u8, ref _background, "Dark\0Grass\0Light\0Night\0"u8);
        if (ImGui.Button("Reload art (F5)"u8))
            Host.Atlas.Reload();
        ImGui.End();

        // Labels go through ImGui so we get a font.
        var dl = ImGui.GetBackgroundDrawList(ImGui.GetMainViewport());
        Layout((sheet, tag, at, _) =>
        {
            var pos = (at - new float2(0, 16)) / Host.FramebufferScale; // sprites are in framebuffer pixels, ImGui in window units
            dl.AddText(in pos, new ImColor8(230, 230, 230, 255), tag == null ? sheet.Name : tag);
        });
        Dressed((label, _, _, at, _) =>
        {
            if (label == null)
                return;
            var pos = (at - new float2(0, 16)) / Host.FramebufferScale;
            dl.AddText(in pos, new ImColor8(230, 230, 230, 255), label);
        });
    }

    public override void Draw(in FrameContext frame)
    {
        var sb = Host.Sprites;
        var extent = Host.Renderer.Swapchain.Extent;
        sb.BeginScreen(new int2(extent.X, extent.Y));
        sb.FillRect(float2.Zero, new float2(extent.X, extent.Y), Backgrounds[_background]);
        Layout((sheet, tag, at, box) =>
        {
            if (tag == null)
                return;
            var anim = sheet.Find(tag)!;
            sb.OutlineRect(at, box, 1f, Rgba.Pack(1, 1, 1, 0.15f));
            var f = anim.Frames[anim.FrameAt(_time)];
            var z = (float)_zoom;
            // Frame origin so the pivot sits at the same spot in the box whichever way it faces.
            var pivotAt = at + new float2(_flip ? box.X - sheet.Pivot.X * z : sheet.Pivot.X * z, sheet.Pivot.Y * z);
            var sprite = f.Sprite;
            var ox = (f.Offset.X - sheet.Pivot.X) * z;
            var min = new float2(_flip ? pivotAt.X - ox - sprite.PixelSize.X * z : pivotAt.X + ox, pivotAt.Y + (f.Offset.Y - sheet.Pivot.Y) * z);
            if (_flip)
                sprite = sprite with { Uv = new float4(sprite.Uv.Z, sprite.Uv.Y, sprite.Uv.X, sprite.Uv.W) };
            sb.DrawRect(sprite, min, new float2(sprite.PixelSize.X, sprite.PixelSize.Y) * z);
            if (!_markers)
                return;
            Cross(sb, pivotAt, Rgba.Pack(0.3f, 1f, 0.3f));
            foreach (var (_, p) in sheet.Points)
            {
                var px = _flip ? pivotAt.X - (p.X - sheet.Pivot.X) * z : pivotAt.X + (p.X - sheet.Pivot.X) * z;
                Cross(sb, new float2(px, pivotAt.Y + (p.Y - sheet.Pivot.Y) * z), Rgba.Pack(1f, 0.5f, 0.2f));
            }
        });
        Dressed((label, sheet, layers, at, box) =>
        {
            if (label != null || sheet == null)
                return;
            sb.OutlineRect(at, box, 1f, Rgba.Pack(1, 1, 1, 0.15f));
            foreach (var tag in (string[])["idle_down", "walk_downside", "walk_upside", "walk_up", "work_downside"])
            {
                if (sheet.Find(tag) is not { } anim)
                    continue;
                var i = anim.Frames[anim.FrameAt(_time)];
                var index = i.Index;
                var z = DressedZoom(sheet);
                DrawFrame(sb, i, at, Rgba.White, z);
                foreach (var (layer, color) in layers)
                    if (index >= 0 && index < layer.Frames.Length)
                        DrawFrame(sb, layer.Frames[index], at, color, z);
                at.X += sheet.FrameSize.X * z + 4;
            }
        });
        sb.End();
        sb.Render(frame);
    }

    // Trimmed frames (sparse look layers) sit at their offset within the frame.
    private static void DrawFrame(SpriteBatch sb, in SheetFrame f, float2 at, uint color, float z) =>
        sb.DrawRect(f.Sprite, at + new float2(4, 4) + new float2(f.Offset.X, f.Offset.Y) * z,
            new float2(f.Sprite.PixelSize.X, f.Sprite.PixelSize.Y) * z, color);

    /// <summary>The dressed rows' scale: the zoom at the size the game draws a sheet (the people's are 4x finer).</summary>
    private float DressedZoom(SpriteSheet sheet) => _zoom / Math.Max(1f, sheet.Density);

    /// <summary>The default villager tints over every look in each apparel set, below the sheets: bare, armor
    /// over a tunic, and every outfit in the defs (clothes as <c>"Def:Dye"</c>, in their first dye).</summary>
    private IEnumerable<string[]> Outfits() =>
        new[] { Array.Empty<string>(), ["Tunic:Woad", "Trousers:Walnut", "LeatherCap", "LeatherJerkin"], ["Tunic:Madder", "Trousers:IronBlack", "FurBoots", "IronHelm", "ChainShirt"] }
            .Concat(_defs!.AllOutfits.Select(o => o.Pieces.Select(p => p.MaterialDefs.Count > 0 ? $"{p.Def}:{p.MaterialDefs[0].Name}" : p.Def).ToArray()));

    private List<HumanLook.WornItem> WornOf(string[] outfit) =>
        outfit.Select(n => Sim.Systems.Clothing.Parse(_defs!, n) is var (def, mat)
                ? new HumanLook.WornItem(def.Apparel!, 1f, HumanLook.DyeOf(def, mat))
                : _defs!.AllThings.Find(t => t.Name == n)?.Apparel is { } a ? new HumanLook.WornItem(a, 1f) : (HumanLook.WornItem?)null)
            .OfType<HumanLook.WornItem>().ToList();

    /// <summary>
    /// People dressed in each outfit (worn layers over the tinted look, as the game draws them).
    /// Calls <paramref name="visit"/> with a label first, then with the sheet and layers to draw.
    /// </summary>
    private void Dressed(Action<string?, SpriteSheet?, List<(SpriteSheet, uint)>, float2, float2> visit)
    {
        if (!_dressed || _defs == null)
            return;
        var y = _dressedOnly ? 40f - _scroll : _contentEnd + 20f;
        // Each adventurer kind as it comes to town: its outfit, its armor, its folk and fixed look (Kin).
        visit("Adventurers as they come (outfit, armor, folk and fixed look)", null, [], new float2(16, y + 18), default);
        y += 22;
        var kx = 16f;
        var kindBox = default(float2);
        var seed = 1;
        foreach (var kind in _defs.AllAdventurerKinds)
        {
            var kin = kind.KinOf();
            var derived = HumanLook.Derived(seed++ * 7919, folk: kin?.Folk);
            var look = kin is { } k ? HumanLook.Fixed(derived, k) : derived;
            if (HumanLook.Sheet(Host.Atlas, look) is not { } sheet)
                continue;
            kindBox = new float2((sheet.FrameSize.X * DressedZoom(sheet) + 4) * 5 + 4, sheet.FrameSize.Y * DressedZoom(sheet) + 8);
            if (kx + kindBox.X > Host.Renderer.Swapchain.Extent.X)
            {
                kx = 16;
                y += kindBox.Y + 22;
            }
            var pieces = (kind.OutfitDef?.Pieces.Select(p => p.MaterialDefs.Count > 0 ? $"{p.Def}:{p.MaterialDefs[0].Name}" : p.Def) ?? [])
                .Concat(kind.Apparel).ToArray();
            visit($"{kind.Name} ({kind.Outfit ?? "no outfit"})", null, [], new float2(kx, y + 18), kindBox);
            visit(null, sheet, HumanLook.Layers(Host.Atlas, look, Rgba.White, WornOf(pieces)), new float2(kx, y + 18), kindBox);
            kx += kindBox.X + 12;
        }
        y += kindBox.Y + 40;
        visit("Dressed (worn apparel over each look)", null, [], new float2(16, y + 18), default);
        y += 22;
        foreach (var body in Enum.GetValues<Sim.Components.BodyType>())
            foreach (var hair in Enum.GetValues<Sim.Components.HairStyle>())
            {
                var look = HumanLook.Default with { Body = body, Hair = hair };
                if (HumanLook.Sheet(Host.Atlas, look) is not { } sheet)
                    continue;
                var box = new float2((sheet.FrameSize.X * DressedZoom(sheet) + 4) * 5 + 4, sheet.FrameSize.Y * DressedZoom(sheet) + 8);
                var x = 16f;
                foreach (var outfit in Outfits())
                {
                    var worn = WornOf(outfit);
                    if (x + box.X > Host.Renderer.Swapchain.Extent.X)
                    {
                        x = 16;
                        y += box.Y + 22;
                    }
                    visit($"{body}/{hair} {(outfit.Length == 0 ? "bare" : string.Join(" + ", outfit.Select(n => n.Split(':')[0])))}", null, [], new float2(x, y + 18), box);
                    visit(null, sheet, HumanLook.Layers(Host.Atlas, look, Rgba.White, worn), new float2(x, y + 18), box);
                    x += box.X + 12;
                }
                y += box.Y + 40;
            }
        _contentMax = Math.Max(0f, y + _scroll - Host.Renderer.Swapchain.Extent.Y * 0.5f);
    }

    private static void Cross(SpriteBatch sb, float2 c, uint color)
    {
        sb.FillRect(c - new float2(4, 0.5f), new float2(9, 1), color);
        sb.FillRect(c - new float2(0.5f, 4), new float2(1, 9), color);
    }

    /// <summary>Rows of sheets, one box per tag; calls <paramref name="visit"/> with a null tag for each row's title.</summary>
    private void Layout(Action<SpriteSheet, string?, float2, float2> visit)
    {
        var y = 40f - _scroll;
        if (_dressedOnly)
        {
            _contentEnd = y;
            return;
        }
        foreach (var sheet in Host.Atlas.Sheets.Values.OrderBy(s => s.Name, StringComparer.Ordinal))
        {
            var box = new float2(sheet.FrameSize.X, sheet.FrameSize.Y) * _zoom + new float2(8, 8);
            visit(sheet, null, new float2(16, y + 18), box);
            y += 22;
            var x = 16f;
            foreach (var tag in sheet.Anims.Keys.Order(StringComparer.Ordinal))
            {
                if (x + box.X > Host.Renderer.Swapchain.Extent.X)
                {
                    x = 16;
                    y += box.Y + 22;
                }
                visit(sheet, tag, new float2(x, y + 18), box);
                x += box.X + 12;
            }
            y += box.Y + 40;
        }
        _contentEnd = y;
        _contentMax = Math.Max(0f, y + _scroll - Host.Renderer.Swapchain.Extent.Y * 0.5f); // Dressed's rows add theirs
    }
}

using Narsil.Rendering;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;

namespace HeroesNotIncluded.Rendering;

/// <summary>
/// Checks that every sprite the defs and renderer name exists, and that creature sheets have the
/// animation tags their behaviors use. The sim can't know about textures, so this lives game-side.
/// Missing sprites are errors (they draw as white squares); missing tags are warnings (the animator
/// falls back, e.g. glide → fly → idle). Logged at startup, and <c>--check-art</c> fails on errors.
/// </summary>
public static class ArtAudit
{
    /// <summary>Sprites the renderer asks for by name.</summary>
    public static readonly string[] EngineSprites =
    [
        "fx/fire", "fx/smoke", "fx/breath", "fx/breath_core", "fx/ember", "fx/zzz", "fx/glint",
        "ui/selection", "ui/designate_x", "ui/bubble", "buildings/bed_blanket",
        "ui/icon_select", "ui/icon_cancel", "ui/icon_chop", "ui/icon_mine", "ui/icon_dig", "ui/icon_harvest",
        "ui/icon_build", "ui/icon_deconstruct", "ui/icon_burn", "ui/icon_stockpile", "ui/icon_clearzone",
        "ui/icon_sleep", "ui/icon_eat", "ui/icon_alert", "ui/icon_fight", "ui/icon_doctor", "ui/icon_haul",
        "ui/icon_hunt", "ui/icon_fish", "ui/icon_butcher", "ui/icon_home",
    ];

    public sealed record Report(List<string> Errors, List<string> Warnings);

    public static Report Run(DefDatabase defs, IReadOnlyDictionary<string, Sprite> sprites, IReadOnlyDictionary<string, SpriteSheet> sheets)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        void Need(string sprite, string who)
        {
            if (!string.IsNullOrEmpty(sprite) && !sprites.ContainsKey(sprite))
                errors.Add($"{who}: missing sprite '{sprite}'");
        }

        foreach (var b in defs.AllBlocks)
        {
            Need(b.Sprite, $"block {b.Name}");
            Need(b.SpriteSide, $"block {b.Name}");
        }
        foreach (var t in defs.AllThings)
        {
            Need(t.Sprite, $"thing {t.Name}");
            Need(t.Icon, $"thing {t.Name} icon");
            foreach (var box in t.Model ?? [])
            {
                Need(box.Top, $"thing {t.Name} model");
                Need(box.Side, $"thing {t.Name} model");
                Need(box.Front, $"thing {t.Name} model");
            }
        }
        foreach (var r in defs.AllRunes)
            Need(r.Icon, $"rune {r.Name}");
        foreach (var b in defs.AllBuildings)
            Need(b.SignFace ?? "", $"building {b.Name} sign");
        foreach (var sv in defs.AllServices)
            Need(sv.Icon, $"service {sv.Name} icon");
        foreach (var kit in defs.AllKits)
        {
            foreach (var box in kit.Span.Concat(kit.Arm).Concat(kit.Corner).Concat(kit.Post).Concat(kit.Door))
            {
                Need(box.Top, $"kit {kit.Name}");
                Need(box.Side, $"kit {kit.Name}");
                Need(box.Front, $"kit {kit.Name}");
            }
            Need(kit.Floor, $"kit {kit.Name} floor");
            if (kit.Roof is { } roof)
                foreach (var name in (string[])[roof.Sprite, roof.Gable, roof.GableFill, roof.Trim, roof.Ridge])
                    Need(name, $"kit {kit.Name} roof");
        }
        foreach (var name in EngineSprites)
            Need(name, "renderer");

        // Things made of a material (the item itself, and anything that costs it) should have art for it.
        foreach (var m in defs.AllMaterials)
            foreach (var t in defs.AllThings)
            {
                // A preset building's phase site is drawn part by part, each with its own art.
                if (t.PhaseOf != null || t.UpgradeOf != null)
                    continue;
                var madeOf = m.Fits(t) || t.Building?.Cost.Exists(c => m.Fits(c.ThingDef)) == true;
                // Box models tint their faces by material; their sprite is only an icon.
                if (madeOf && t.Model == null && !string.IsNullOrEmpty(t.Sprite) && !sprites.ContainsKey(t.Sprite + m.SpriteSuffix))
                    warnings.Add($"thing {t.Name}: no '{t.Sprite + m.SpriteSuffix}' for {m.Label} (falls back to a tint)");
            }

        foreach (var c in defs.AllCreatures)
        {
            var stages = c.LifeStages.Count > 0 ? c.LifeStages : [new LifeStage { Name = "default", Sprite = c.Sprite }];
            foreach (var s in stages)
            {
                var sprite = string.IsNullOrEmpty(s.Sprite) ? c.Sprite : s.Sprite;
                var who = $"creature {c.Name}/{s.Name}";
                Need(sprite, who);
                if (!sheets.TryGetValue(sprite, out var sheet))
                {
                    warnings.Add($"{who}: '{sprite}' is a static sprite, not an animated sheet");
                    continue;
                }
                foreach (var tag in RequiredTags(c, s))
                    if (sheet.Find(tag) == null)
                        warnings.Add($"{who}: sheet '{sprite}' has no '{tag}' animation (falls back to '{CreatureAnimator.Fallback(tag) ?? "first frame"}')");
            }
        }
        // People are drawn from layered looks (HumanLook): a base sheet per body, and every layer
        // (skin, hair and its back half, eyes and face, beards, worn things) on its frame layout
        // with its tags, since layers are matched by frame index.
        // Every build (tools/chibi_builds.py) has every body's layers too: a folk of each build stands for it.
        foreach (var build in HumanLook.Folks.GroupBy(f => f.Build).Select(g => g.First().Folk))
        foreach (var bodyType in Enum.GetValues<BodyType>())
        {
            var look = new Appearance { Body = bodyType, Folk = build };
            var body = HumanLook.BodyKey(look);
            var name = HumanLook.BaseSheet(look);
            if (!sheets.TryGetValue(name, out var baseSheet))
            {
                errors.Add($"body {body}: no sheet '{name}'");
                continue;
            }
            CheckLayer(HumanLook.SkinSheet(look), $"body {body}", required: true);
            foreach (var folk in Enum.GetValues<Folk>())
                if (folk != build && HumanLook.FolkOf(folk).Build == HumanLook.FolkOf(build).Build)
                {
                    CheckLayer(HumanLook.SkinSheet(look with { Folk = folk }), $"body {body}/{folk}", required: true);
                    if (HumanLook.EarsSheet(look with { Folk = folk }) is { } ears)
                        CheckLayer(ears, $"body {body}/{folk}", required: true);
                }
            foreach (var hair in Enum.GetValues<HairStyle>())
            {
                CheckLayer(HumanLook.HairSheet(look with { Hair = hair }), $"hair {hair}", required: hair != HairStyle.None);
                CheckLayer(HumanLook.HairBackSheet(look with { Hair = hair }), $"hair {hair}", required: false);
            }
            foreach (var eyes in Enum.GetValues<EyeStyle>())
            {
                CheckLayer(HumanLook.EyesSheet(look with { Eyes = eyes }), $"body {body}/{eyes} eyes", required: true);
                CheckLayer(HumanLook.FaceSheet(look with { Eyes = eyes }), $"body {body}/{eyes} eyes", required: true);
            }
            foreach (var beard in Enum.GetValues<BeardStyle>())
                if (beard != BeardStyle.None)
                    CheckLayer(HumanLook.BeardSheet(look with { Beard = beard }), $"beard {beard}", required: true);
            // Held weapons and tools are layers too: their shape's sheets in hand (apparel/held_<shape>_<body>).
            foreach (var shape in defs.AllThings.Select(t => t.Held).OfType<string>().Distinct())
            {
                CheckLayer($"apparel/held_{shape}_{body}", $"held {shape}", required: true);
                CheckLayer($"apparel/held_{shape}_back_{body}", $"held {shape}", required: true);
            }
            // Worn apparel is a layer too, found by HumanLook.WornSheet (<worn>_<body>).
            foreach (var t in defs.AllThings)
            {
                if (t.Apparel?.Worn is not { } worn)
                    continue;
                CheckWorn(t, worn, required: true);
                if (t.MaterialsOfDef != null)
                    CheckWorn(t, worn + "_dye", required: true);
                if (t.Apparel.Slot is ApparelSlot.Cloak or ApparelSlot.Neck)
                    CheckWorn(t, worn + "_back", required: false);
            }

            void CheckWorn(ThingDef t, string worn, bool required)
            {
                var sheetName = new[] { $"{worn}_{body}", worn }.FirstOrDefault(sheets.ContainsKey);
                if (sheetName == null)
                {
                    if (required)
                        warnings.Add($"thing {t.Name}: no worn sheet '{worn}_{body}' (drawn without it)");
                }
                else
                    CheckLayer(sheetName, $"thing {t.Name}", required: true);
            }

            void CheckLayer(string layer, string who, bool required)
            {
                if (!sheets.TryGetValue(layer, out var l))
                {
                    if (required)
                        warnings.Add($"{who}: no layer sheet '{layer}'");
                }
                else if (l.Frames.Length != baseSheet.Frames.Length)
                    errors.Add($"{who}: '{layer}' has {l.Frames.Length} frames, '{name}' has {baseSheet.Frames.Length}");
                else if (baseSheet.Anims.Keys.FirstOrDefault(tag => l.Find(tag) == null) is { } missing)
                    errors.Add($"{who}: layer '{layer}' has no '{missing}' tag");
                else if (l.Density != baseSheet.Density)
                    errors.Add($"{who}: layer '{layer}' has density {l.Density}, '{name}' has {baseSheet.Density}");
            }
        }
        // 3D models (a preset's Appearance.Model): their own base sheet, and every sheet of theirs
        // (the tint layers, garments rendered for them) on its frame layout. A missing garment
        // only means it isn't drawn on them.
        foreach (var model in defs.AllOutfits.Select(o => o.Model).OfType<string>().Distinct())
        {
            var name = $"creatures/{model}";
            if (!sheets.TryGetValue(name, out var baseSheet))
            {
                warnings.Add($"model {model}: no sheet '{name}' (its presets fall back to the drawn look)");
                continue;
            }
            foreach (var (layerName, layer) in sheets)
                if (layerName != name && (layerName.StartsWith(name + "_", StringComparison.Ordinal) || layerName.EndsWith("_" + model, StringComparison.Ordinal)))
                {
                    if (layer.Frames.Length != baseSheet.Frames.Length)
                        errors.Add($"model {model}: '{layerName}' has {layer.Frames.Length} frames, '{name}' has {baseSheet.Frames.Length}");
                    else if (baseSheet.Anims.Keys.FirstOrDefault(tag => layer.Find(tag) == null) is { } missing)
                        errors.Add($"model {model}: layer '{layerName}' has no '{missing}' tag");
                }
        }
        return new Report(errors, warnings);
    }

    private static string Lower<T>(T value) where T : Enum => value.ToString().ToLowerInvariant();

    /// <summary>The tags a creature at this stage will ask for.</summary>
    public static IEnumerable<string> RequiredTags(CreatureDef c, LifeStage s)
    {
        yield return "idle";
        yield return "walk";
        yield return "sleep";
        yield return "downed";
        if (c.Race == Race.Human)
        {
            yield return "work";
            // JRPG-style people face four ways (CreatureAnimator.Directional).
            foreach (var tag in (string[])["idle", "walk", "work"])
                foreach (var view in (string[])["down", "downside", "upside", "up"])
                    yield return $"{tag}_{view}";
        }
        if (s.CanFly)
        {
            yield return "fly";
            yield return "glide";
            yield return "takeoff";
            yield return "land";
        }
        if (s.BreathRange > 0)
        {
            yield return "breathe_windup";
            yield return "breathe";
        }
    }

    public static void Log(Report report)
    {
        foreach (var e in report.Errors)
            Narsil.Log.Render.Warning($"Art: {e}");
        foreach (var w in report.Warnings)
            Narsil.Log.Render.Info($"Art: {w}");
    }
}

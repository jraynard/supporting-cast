using HeroesNotIncluded.Rendering;
using HeroesNotIncluded.Sim.Defs;
using Narsil.Audio;
using Narsil.Mods;
using Narsil.Rendering;

namespace HeroesNotIncluded.Mods;

/// <summary>
/// <c>--check-mod PATH</c>, headless: a mod (a folder, or an installed mod's id) loaded over the core
/// with what it depends on, its defs resolved, the art and sounds audited as <c>--check-art</c> does,
/// and what it adds or replaces listed. The quick check a modder, or their coding agent, runs after
/// every change; the exit code is the number of errors (capped), so scripts and tests can use it.
/// </summary>
public static class ModCheck
{
    public sealed record Report(List<string> Errors, List<string> Warnings, List<string> Notes);

    public static int Run(string pathOrId, TextWriter output)
    {
        var report = Check(pathOrId);
        foreach (var n in report.Notes)
            output.WriteLine(n);
        foreach (var w in report.Warnings)
            output.WriteLine($"warning: {w}");
        foreach (var e in report.Errors)
            output.WriteLine($"error: {e}");
        output.WriteLine($"{report.Errors.Count} error(s), {report.Warnings.Count} warning(s)");
        return Math.Min(report.Errors.Count, 100);
    }

    public static Report Check(string pathOrId, ModCatalog? installed = null, string? dataDirectory = null)
    {
        var errors = new List<string>();
        var warnings = new List<string>();
        var notes = new List<string>();
        installed ??= GameContent.Catalog;

        ModManifest mod;
        try
        {
            mod = File.Exists(Path.Combine(pathOrId, ModManifest.FileName)) ? ModManifest.Load(pathOrId)
                : installed.Find(pathOrId) ?? throw new InvalidDataException($"no mod.json in {pathOrId}, and no installed mod has that id");
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            errors.Add(ex.Message);
            return new Report(errors, warnings, notes);
        }
        notes.Add($"{mod.Name} ({mod.Id} {mod.Version}) at {mod.Directory}");

        // This copy stands in for an installed one of the same id; its dependencies come from what's installed.
        var catalog = new ModCatalog([mod, .. installed.All.Where(m => m.Id != mod.Id)]);
        var wanted = new HashSet<string>(StringComparer.Ordinal) { mod.Id };
        for (var grew = true; grew;)
        {
            grew = false;
            foreach (var id in wanted.ToList())
                foreach (var dep in catalog.Find(id)?.DependsOn ?? [])
                    grew |= wanted.Add(dep);
        }
        var order = catalog.Resolve(wanted);
        errors.AddRange(order.Problems);
        if (!order.Ids.Contains(mod.Id))
            return new Report(errors, warnings, notes);
        if (order.Mods.Count > 1)
            notes.Add($"Loaded with: {string.Join(", ", order.Ids.Where(i => i != mod.Id))}");

        if (mod.Folder("defs") == null && mod.Folder("textures") == null && mod.Folder("sounds") == null && !mod.RunsCode)
            warnings.Add("the mod has no defs, textures, sounds or assemblies: it changes nothing");

        var content = new GameContent(order);
        DefDatabase core, defs;
        try
        {
            core = new GameContent(ModOrder.None).LoadDefs(dataDirectory);
            defs = content.LoadDefs(dataDirectory);
        }
        catch (Exception ex)
        {
            errors.Add($"defs: {ex.Message}");
            return new Report(errors, warnings, notes);
        }
        notes.AddRange(Changes(core, defs));

        if (order.Mods.Any(m => m.RunsCode))
        {
            var failed = new HashSet<string>(StringComparer.Ordinal);
            var codeProblems = new List<string>();
            var code = ModCode.Load(order, codeProblems, failed);
            errors.AddRange(codeProblems);
            foreach (var (id, asm) in code.Sim.Where(c => c.ModId == mod.Id))
                notes.Add($"Sim code: {asm.GetName().Name}");
            foreach (var (id, asm) in code.Game.Where(c => c.ModId == mod.Id))
                notes.Add($"Game code: {asm.GetName().Name}");
            if (failed.Count == 0)
            {
                // A check, not a switch: the content this run had comes back after.
                var simBefore = Sim.Modding.SimContent.Mods.ToList();
                var gameBefore = GameMods.Loaded;
                try
                {
                    Sim.Modding.SimContent.Use(code.Sim);
                    GameMods.Use(code.Game);
                    // A simulation made with it: its systems take their places, its sessions and codecs are known.
                    var sim = Sim.Simulation.CreateStandard(defs, 16, 16, 1);
                    var added = sim.Systems.Where(sys => Sim.Modding.SimContent.ModOf(sys.GetType().Assembly) == mod.Id).Select(sys => sys.Name).ToList();
                    if (added.Count > 0)
                        notes.Add($"Systems: {string.Join(", ", added)}");
                    var sessions = Sim.Work.WorkSession.KnownSessions.Where(k => k.StartsWith(mod.Id + ":", StringComparison.Ordinal)).ToList();
                    if (sessions.Count > 0)
                        notes.Add($"Work sessions: {string.Join(", ", sessions)}");
                    sim.TickMany(60);
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    errors.Add($"code: {ex.Message}");
                }
                finally
                {
                    Sim.Modding.SimContent.Use(simBefore);
                    GameMods.Use(gameBefore);
                }
            }
        }

        try
        {
            var packed = AtlasPacker.Pack(content.TextureRoots(dataDirectory));
            var coreSprites = AtlasPacker.Files(new GameContent(ModOrder.None).TextureRoots(dataDirectory)).Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (mod.Folder("textures") is { } textures)
            {
                var own = AtlasPacker.Files([textures]);
                var replaced = own.Count(f => coreSprites.Contains(f.Name));
                notes.Add($"Sprites: {own.Count - replaced} new, {replaced} replacing the game's");
            }
            var art = ArtAudit.Run(defs, packed.Sprites, packed.Sheets);
            errors.AddRange(art.Errors.Select(e => $"art: {e}"));
        }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or IOException)
        {
            errors.Add($"art: {ex.Message}");
        }

        if (mod.Folder("sounds") is { } sounds)
        {
            var path = Path.Combine(sounds, "sounds.json");
            if (!File.Exists(path))
                warnings.Add("sounds/ has no sounds.json, so none of its files are played");
            else
            {
                try
                {
                    var manifest = SoundManifest.Load(path);
                    errors.AddRange(manifest.MissingFiles.Select(m => $"sound {m}"));
                    notes.Add($"Sounds: {manifest.Sounds.Count} sound(s), {manifest.Music.Count} playlist(s)");
                }
                catch (Exception ex) when (ex is InvalidDataException or IOException)
                {
                    errors.Add($"sounds: {ex.Message}");
                }
            }
        }
        return new Report(errors, warnings, notes);
    }

    /// <summary>What the mod adds and replaces, kind by kind (the def lists that differ from the core's).</summary>
    private static IEnumerable<string> Changes(DefDatabase core, DefDatabase modded)
    {
        foreach (var prop in typeof(DefDatabase).GetProperties().Where(p => p.Name.StartsWith("All", StringComparison.Ordinal)))
        {
            if (prop.GetValue(core) is not System.Collections.IEnumerable a || prop.GetValue(modded) is not System.Collections.IEnumerable b)
                continue;
            var before = a.OfType<Def>().ToDictionary(d => d.Name, StringComparer.OrdinalIgnoreCase);
            var after = b.OfType<Def>().ToList();
            if (after.Count == 0 && before.Count == 0)
                continue;
            var added = after.Where(d => !before.ContainsKey(d.Name)).Select(d => d.Name).ToList();
            var changed = after.Where(d => before.TryGetValue(d.Name, out var old) && !ReferenceEquals(old, d) && Json(old) != Json(d)).Select(d => d.Name).ToList();
            var removed = before.Keys.Except(after.Select(d => d.Name), StringComparer.OrdinalIgnoreCase).ToList();
            var kind = prop.Name[3..];
            if (added.Count > 0)
                yield return $"{kind}: adds {string.Join(", ", added)}";
            if (changed.Count > 0)
                yield return $"{kind}: changes {string.Join(", ", changed)}";
            if (removed.Count > 0)
                yield return $"{kind}: removes {string.Join(", ", removed)}";
        }
    }

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles,
        MaxDepth = 16,
    };

    private static string Json(Def d)
    {
        try
        {
            return System.Text.Json.JsonSerializer.Serialize(d, d.GetType(), JsonOptions);
        }
        catch (Exception ex) when (ex is NotSupportedException or InvalidOperationException or System.Text.Json.JsonException)
        {
            return d.Name + "?" + d.GetHashCode(); // can't compare it: count it as changed
        }
    }
}

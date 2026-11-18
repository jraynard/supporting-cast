using System.Runtime.CompilerServices;
using HeroesNotIncluded.Sim.Modding;

namespace HeroesNotIncluded.Tests;

/// <summary>The content every test runs with: the game and the mods on by default (the carpenter), as the game starts.</summary>
public static class TestContent
{
    public static readonly IReadOnlyList<(string ModId, System.Reflection.Assembly Assembly)> Standard =
        [("carpenter", typeof(Sim.Carpentry.CarpenterMod).Assembly)];

    /// <summary>The game's content as it starts from this checkout: the game's data and the mods on by default.</summary>
    public static Mods.GameContent Content => _content.Value;

    private static readonly Lazy<Mods.GameContent> _content = new(() =>
    {
        var catalog = Narsil.Mods.ModCatalog.Discover([DevPaths.Mods!]);
        return new Mods.GameContent(catalog.Resolve(catalog.All.Where(m => m.DefaultOn).Select(m => m.Id)));
    });

    /// <summary>A fresh copy of the defs the game starts with (the checkout's, so edits count without a rebuild).</summary>
    public static Sim.Defs.DefDatabase Defs() => Sim.Defs.DefDatabase.LoadDirectories(Content.LiveDefDirectories());

    [ModuleInitializer]
    internal static void Init() => SimContent.Use(Standard);
}

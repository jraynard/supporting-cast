using System.Runtime.CompilerServices;
using HeroesNotIncluded.Sim.Modding;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// The content every test runs with: the game and the mods on by default (the carpenter), as the
/// game itself starts. Tests that swap the content put this back.
/// </summary>
public static class TestContent
{
    public static readonly IReadOnlyList<(string ModId, System.Reflection.Assembly Assembly)> Standard =
        [("carpenter", typeof(Carpentry.CarpenterMod).Assembly)];

    [ModuleInitializer]
    internal static void Init() => SimContent.Use(Standard);
}

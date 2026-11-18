using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded;

/// <summary>The village screen as mods see it (<see cref="Mods.WorkView"/>): only what they're given here.</summary>
public sealed partial class VillageGame
{
    private Mods.WorkView? _modWorkView;

    private Mods.WorkView ModWorkView => _modWorkView ??= new Mods.WorkView(this);

    internal Simulation CurrentSim => _sim;

    internal PerceptionView Perceived => View;

    internal float Time => _time;

    internal void BeginWorkWindow(string title, (string Keys, string Does)[] hints) => BeginWork(title, hints);

    internal void EndWorkWindow() => EndWork();

    internal void IssueCommand(ISimCommand command) => Issue(command);
}

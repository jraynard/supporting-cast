using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Scenarios;

/// <summary>
/// The 3D forge prototype (<c>--forge-3d</c>, docs/VISION.md, "A world in 3D"): one smith, a lit
/// forge with its bellows, an anvil and a quench tub in reach, and a sword's billet in the tongs.
/// The smith is a player in an endless round, so the hand forging (<see cref="Smithing"/>) applies
/// and every step runs through the real sessions; only the view is new. No town, no villagers.
/// </summary>
public static class ForgeYard
{
    /// <summary>The smith's player, as the round knows them.</summary>
    public static readonly Guid Player = new("00000000-0000-0000-0000-00000000f03e");

    public const string DefaultRecipe = "ForgeSword";

    /// <summary>The smithy and its smith: the stations round them all within reach of where they stand.</summary>
    public sealed record Yard(Simulation Sim, Entity Smith, Entity Forge, Entity Anvil, Entity Tub, RecipeDef Recipe);

    public static Yard Setup(DefDatabase defs, ulong seed = 1, string recipe = DefaultRecipe)
    {
        var sim = Simulation.CreateStandard(defs, 24, 24, seed);
        sim.RebuildDerivedIfNeeded();
        Array.Fill(sim.Map.Temperature, sim.Climate.OutdoorTemperature);
        int3 At(int x, int y) => new(x, y, 1);

        var smith = sim.SpawnCreature(defs.Creature("Human"), "Smith", At(10, 10), ageDays: 25 * 365);
        sim.Round.Endless = true;
        sim.Round.OpenLobby();
        sim.Round.Sit(Player, "Smith", null, null);
        sim.Round.Start(sim);

        var forge = sim.SpawnStructure(defs.Thing("Forge"), At(11, 10));
        sim.SpawnStructure(defs.Thing("Bellows"), At(12, 10));
        var anvil = sim.SpawnStructure(defs.Thing("Anvil"), At(9, 10));
        var tub = sim.SpawnStructure(defs.Thing("QuenchTub"), At(10, 11));
        sim.RebuildDerivedIfNeeded();
        var yard = new Yard(sim, smith, forge, anvil, tub, defs.Recipe(recipe));
        Restock(yard);
        NewPiece(yard);
        return yard;
    }

    /// <summary>Keep the fire fed and hot: the prototype is about the hand work, not the fuel.</summary>
    public static void Restock(Yard yard)
    {
        var sim = yard.Sim;
        sim.World.Get<Fueled>(yard.Forge).Fuel = 10;
        sim.Map.Temperature[sim.Map.Idx(sim.World.Get<Thing>(yard.Forge).Cell)] = 1200;
    }

    /// <summary>A fresh billet in the tongs, unless one's there already.</summary>
    public static void NewPiece(Yard yard)
    {
        if (Smithing.PieceOf(yard.Sim, yard.Smith) != null)
            return;
        yard.Sim.World.Get<Workbench>(yard.Forge).Begin(yard.Recipe, yard.Smith);
        Smithing.BeginMaking(yard.Sim, yard.Smith, yard.Forge, yard.Recipe);
    }

    /// <summary>The station the piece in the tongs wants next: the fire till it's hot, the anvil till it's shaped, then the tub.</summary>
    public static Entity NextStation(Yard yard)
    {
        if (Smithing.PieceOf(yard.Sim, yard.Smith) is not { } p)
            return Entity.Null;
        return !Smithing.HotEnough(p.Recipe, p.Temp) ? yard.Forge : !p.Shaped ? yard.Anvil : yard.Tub;
    }
}

using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Systems;
using static HeroesNotIncluded.Sim.Tests.TestWorld;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary><see cref="Appearance.Model"/>: a character drawn from a 3D model's sheets, by name.</summary>
public class AppearanceTests
{
    [Theory]
    [InlineData("wanderer3d", true)]
    [InlineData("a_b_9", true)]
    [InlineData("", false)]
    [InlineData("Wanderer", false)]
    [InlineData("../textures/x", false)]
    [InlineData("a b", false)]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456789", false)]
    public void ModelNamesAreSafeToBuildSheetNamesFrom(string name, bool valid) =>
        Assert.Equal(valid ? name : null, Appearance.ValidModel(name));

    [Fact]
    public void AModelSurvivesSavingAndTheWire()
    {
        var sim = Create(12, 12);
        var e = sim.World.Create();
        sim.World.Add(e, new Appearance { Hair = HairStyle.Swept, Model = "wanderer3d" });
        var (loaded, _) = SaveGame.Read(SaveGame.Write(sim), sim.Defs);
        Assert.Equal("wanderer3d", loaded.World.Get<Appearance>(e).Model);

        var command = new CreateCharacterCommand(Guid.NewGuid(), "Bea", new Appearance { Model = "wanderer3d" }, []);
        var round = (CreateCharacterCommand)CommandCodec.Decode(CommandCodec.Encode(command, sim.Defs), sim.Defs);
        Assert.Equal("wanderer3d", round.Look.Model);
    }

    [Fact]
    public void TheWandererIsDrawnNotAModelForNow() =>
        Assert.Null(LoadDefs().Outfit("Wanderer").Model);   // the 3D try is parked (art/3d, docs/ART.md); presets are drawn

    [Fact]
    public void TheValkyrieIsAPresetInAHornedHelm()
    {
        var defs = LoadDefs();
        var valkyrie = defs.Outfit("Valkyrie");
        Assert.Null(valkyrie.Model);                       // drawn from the shared people sheets like everyone
        var helm = Assert.Single(valkyrie.Pieces, p => p.Def == "HornedHelm").ThingDef;
        Assert.True(Clothing.IsClothes(helm));
        Assert.Contains(Defs.ApparelSlot.Head, Clothing.Slots);
        Assert.Contains(valkyrie.Pieces, p => p.Def == "Tunic" && p.MaterialDefs[0].Name == "Ash");
    }
}

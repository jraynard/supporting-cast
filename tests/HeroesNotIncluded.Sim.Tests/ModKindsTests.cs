using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Shops;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>A mod's own workplaces and services: named in its defs, beyond the game's enums (docs/MODDING.md).</summary>
public class ModKindsTests
{
    private const string Glassworks = """
        { "services": [ { "name": "Glasswork", "label": "a bottle blown", "at": "Glassworks", "defaultPrice": 5, "worthMin": 5, "worthMax": 9, "patienceHours": 4 } ],
          "trades": [ { "name": "Glassblower", "label": "glassblower", "workplace": "Glassworks", "serves": [ "Glasswork", "Repair" ] } ] }
        """;

    [Fact]
    public void AModsWorkplaceAndServiceAreKindsOfTheirOwn()
    {
        var defs = DefDatabase.FromJson(Glassworks);
        var service = defs.AllServices.Single(s => s.Name == "Glasswork");
        var trade = defs.Trade("Glassblower");

        // Numbered after the game's own, which keep theirs.
        Assert.True((int)service.Kind >= Enum.GetValues<ServiceKind>().Length);
        Assert.Equal(service.Kind, defs.ServiceKindOf("Glasswork"));
        Assert.Equal("Glasswork", defs.ServiceName(service.Kind));
        Assert.Equal(ServiceKind.Repair, defs.ServiceKindOf("Repair"));
        Assert.Same(service, defs.Service(service.Kind));

        // The workplace is a role of its own, and the trade and the service agree on it.
        Assert.False(Enum.IsDefined(trade.Role));
        Assert.Equal(trade.Role, service.AtRole);
        Assert.Equal("Glassworks", TownRoles.NameOf(trade.Role));
        Assert.Equal(trade.Role, TownRoles.Parse("glassworks", "test")); // the same role wherever it's named
        Assert.True(trade.Serving(service.Kind));
        Assert.True(trade.Serving(ServiceKind.Repair));
    }

    [Fact]
    public void ASignPricesAModsServiceAndASaveKeepsIt()
    {
        var defs = DefDatabase.LoadDirectories([.. TestWorld.DefDirectories(), Glassdir()]);
        var kind = defs.ServiceKindOf("Glasswork");
        var prices = ShopPrices.Defaults(defs);
        Assert.Equal(defs.ServiceKindCount, prices.Count);
        Assert.Equal(5, prices.Price[(int)kind]);
        Assert.True(prices.Offered[(int)kind]);

        var (sim, center) = TestWorld.Millbrook(defs: defs);
        var sign = sim.World.Create();
        prices.Price[(int)kind] = 7;
        sim.World.Add(sign, prices);
        var (loaded, _) = Saving.SaveGame.Read(Saving.SaveGame.Write(sim), defs);
        Assert.Equal(7, loaded.World.Get<ShopPrices>(sign).Price[(int)kind]);
    }

    private static string Glassdir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "hni-glass-" + System.Environment.ProcessId);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "glass.json"), Glassworks);
        return dir;
    }
}

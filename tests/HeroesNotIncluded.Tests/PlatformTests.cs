using System.Text;
using HeroesNotIncluded.Platforms;

namespace HeroesNotIncluded.Tests;

/// <summary>Steam's door (<c>Platforms/</c>): what decides whether a copy is Steam's, and what a friend's Join game carries.</summary>
public class PlatformTests
{
    [Fact]
    public void SteamJsonNamesTheAppAndTheStorePage()
    {
        Assert.Equal(new SteamConfig(1234560, 1234570), SteamConfig.Parse("""{ "appId": 1234560, "storeAppId": 1234570 }"""));
        // The full game's own file needn't name its store page twice.
        Assert.Equal(new SteamConfig(1234560, 1234560), SteamConfig.Parse("""{ "appId": 1234560 }"""));
        Assert.Null(SteamConfig.Parse("""{ "storeAppId": 1234570 }"""));
        Assert.Null(SteamConfig.Parse("{ not json"));
        Assert.Null(SteamConfig.Parse("""{ "appId": "soon" }"""));
    }

    [Fact]
    public void NoSteamJsonMeansNoSteam()
    {
        var dir = Directory.CreateTempSubdirectory("hni-steam-");
        try
        {
            Assert.Null(SteamConfig.Find(["--new-game"], dir.FullName));
            File.WriteAllText(Path.Combine(dir.FullName, SteamConfig.FileName), """{ "appId": 1234560 }""");
            Assert.Equal(new SteamConfig(1234560, 1234560), SteamConfig.Find(["--new-game"], dir.FullName));
            // A dev run against Valve's test app wins over the file, and never restarts through Steam.
            Assert.Equal(new SteamConfig(480, 480, Dev: true), SteamConfig.Find(["--steam", "480"], dir.FullName));
            Assert.Null(SteamConfig.Find(["--steam"], dir.FullName));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void AMacAppFindsItsSteamJsonBesideTheBundle()
    {
        var root = Path.Combine(Path.GetTempPath(), "depot");
        var macOs = Path.Combine(root, "HeroesNotIncluded.app", "Contents", "MacOS") + Path.DirectorySeparatorChar;
        Assert.Equal([macOs, Path.GetFullPath(root)], SteamConfig.Places(macOs).Select(Path.GetFullPath));
        // Anywhere else, only beside the game.
        Assert.Single(SteamConfig.Places(Path.Combine(root, "linux") + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void TheConnectStringIsTheJoinFlagWithARelayCode()
    {
        Assert.Equal("--join KX7QPM", ConnectString.For("KX7QPM"));
        Assert.Equal("KX7QPM", ConnectString.CodeOf(ConnectString.For("KX7QPM")));
        Assert.Equal("KX7QPM", ConnectString.CodeOf("--join kx7qpm"));
        // Anything else a friend's presence might carry is refused, not acted on.
        Assert.Null(ConnectString.CodeOf(null));
        Assert.Null(ConnectString.CodeOf(""));
        Assert.Null(ConnectString.CodeOf("--join 192.168.1.20"));
        Assert.Null(ConnectString.CodeOf("--join KX7QPM --mods evil"));
        Assert.Null(ConnectString.CodeOf("+connect_lobby 109775241"));
    }

    [Fact]
    public void AJoinRequestCarriesTheFriendsConnectString()
    {
        // GameRichPresenceJoinRequested_t: a CSteamID, then the connect string in 256 bytes.
        var param = new byte[8 + 256];
        BitConverter.GetBytes(76561197960287930UL).CopyTo(param, 0);
        Encoding.UTF8.GetBytes("--join KX7QPM").CopyTo(param, 8);
        Assert.Equal("--join KX7QPM", SteamCallbacks.ConnectOf(param));
        Assert.Null(SteamCallbacks.ConnectOf(new byte[8 + 256]));
        Assert.Null(SteamCallbacks.ConnectOf(new byte[4]));
    }

    [Fact]
    public void WithoutSteamEverythingIsANoOp()
    {
        var none = NoPlatform.Instance;
        none.Update();
        none.SetPresence(Presence.Menu);
        none.SetJoinCode("KX7QPM", 3);
        none.InviteFriends();
        none.OpenStore();
        Assert.False(none.CanInvite);
        Assert.False(none.HasStore);
        Assert.Null(none.TakeJoinRequest());
        Assert.Null(none.PersonaName);
    }
}

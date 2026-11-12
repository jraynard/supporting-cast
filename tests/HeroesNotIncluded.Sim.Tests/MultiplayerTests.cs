using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Maps;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Scenarios;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// Co-op: players' characters, their own eggs and dragons, who may command what, and the join
/// barrier (interrupt everyone, snapshot) that lets a newcomer step in without a desync.
/// </summary>
public class MultiplayerTests
{
    private static readonly Guid Arin = new("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
    private static readonly Guid Bea = new("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");

    private static Simulation Warm()
    {
        var sim = TestWorld.Create();
        sim.Climate.BaseTemperature = 30;
        sim.Climate.DailySwing = 0;
        Array.Fill(sim.Map.Temperature, 30f);
        sim.SpawnStructure(sim.Thing("Wagon"), C(20, 20));
        return sim;
    }

    private static Entity Create(Simulation sim, Guid player, string name)
    {
        sim.ApplyNow(new CreateCharacterCommand(player, name, new Appearance { Hair = HairStyle.Long }, ["Kind"]));
        return Players.CharacterOf(sim, player);
    }

    [Fact]
    public void HostAdoptsTheAvatarOfASinglePlayerSave()
    {
        var sim = Warm();
        var me = sim.SpawnCreature(sim.Defs.Creature("Human"), "Solo", C(5, 5), ageDays: 9000);
        sim.World.Add(me, new Avatar());
        Assert.True(Players.Join(sim, Arin, adoptLooseAvatar: true));
        Assert.Equal(me, Players.CharacterOf(sim, Arin));
        Assert.False(Players.Join(sim, Bea, adoptLooseAvatar: true), "only one loose avatar to adopt");
    }

}

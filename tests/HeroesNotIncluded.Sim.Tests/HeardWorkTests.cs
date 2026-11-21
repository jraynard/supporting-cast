using System.Runtime.CompilerServices;
using HeroesNotIncluded.Sim.Work;

namespace HeroesNotIncluded.Sim.Tests;

/// <summary>
/// Other people's work is heard only where every sound of it is a bystander's to hear
/// (<see cref="WorkSession.HeardByOthers"/>): the anvil rings across the square, but a probe catching on a
/// trap, a wound under the empath's hands or ore behind a sounded wall stays the worker's alone.
/// </summary>
public class HeardWorkTests
{
    private static bool Heard(Type session) => ((WorkSession)RuntimeHelpers.GetUninitializedObject(session)).HeardByOthers;

    [Fact]
    public void TheForgeAndTheBarCarry()
    {
        Assert.True(Heard(typeof(HammerSession)));
        Assert.True(Heard(typeof(QuenchSession)));
        Assert.True(Heard(typeof(TapSession)));
    }

    [Fact]
    public void WhatOnlyTheWorkerFeelsStaysTheirs()
    {
        Assert.False(Heard(typeof(Tinkering.ProbeSession)));
        Assert.False(Heard(typeof(Tinkering.PinSession)));
        Assert.False(Heard(typeof(Tinkering.LoupeSession)));
        Assert.False(Heard(typeof(Healing.HandsOnSession)));
        Assert.False(Heard(typeof(HeroesNotIncluded.Miner.SoundSession)));
    }
}

using Brutal.Numerics;
using HeroesNotIncluded.Rendering;
using HeroesNotIncluded.Sim.Components;

namespace HeroesNotIncluded.Tests;

/// <summary>The folk as they're drawn (<see cref="HumanLook.Folks"/>): who's about town, their builds' sheets and shapes.</summary>
public class FolkLookTests
{
    [Fact]
    public void TheTownHasEveryFolkAndAVillagerAlwaysLooksTheSame()
    {
        var looks = Enumerable.Range(1, 2000).Select(id => HumanLook.Derived(id)).ToList();
        foreach (var folk in Enum.GetValues<Folk>())
            Assert.Contains(looks, a => a.Folk == folk);
        Assert.Equal(looks[123], HumanLook.Derived(124));
        Assert.All(looks.Where(a => a.Folk == Folk.Dwarf && a.Body == BodyType.Male), a => Assert.NotEqual(BeardStyle.None, a.Beard));
        Assert.All(looks.Where(a => a.Folk == Folk.Elf), a => Assert.Equal(BeardStyle.None, a.Beard));
        Assert.All(looks.Where(a => a.Folk == Folk.Halfling), a => Assert.Contains(a.SkinColor, HumanLook.HalflingSkinTones));
    }

    [Fact]
    public void ABuildDrawsFromItsOwnSheets()
    {
        var dwarf = new Appearance { Body = BodyType.Female, Folk = Folk.Dwarf, Eyes = EyeStyle.Narrow, Hair = HairStyle.Bun, Beard = BeardStyle.Short };
        Assert.Equal("creatures/person_female_dwarf", HumanLook.BaseSheet(dwarf));
        Assert.Equal("creatures/person_female_dwarf_skin", HumanLook.SkinSheet(dwarf));
        Assert.Equal("creatures/person_female_dwarf_eyes_narrow", HumanLook.EyesSheet(dwarf));
        Assert.Equal("creatures/person_hair_bun_dwarf", HumanLook.HairSheet(dwarf));
        Assert.Equal("creatures/person_beard_short_dwarf", HumanLook.BeardSheet(dwarf));
        var elf = new Appearance { Body = BodyType.Male, Folk = Folk.Elf, Eyes = EyeStyle.Bright };
        Assert.Equal("creatures/person_male_eyes_bright", HumanLook.EyesSheet(elf));
        Assert.Equal("male_halfling", HumanLook.BodyKey(new Appearance { Folk = Folk.Halfling }));
    }

    [Fact]
    public void ABuildKeepsItsFeetOnTheGroundAndItsHeadLower()
    {
        var human = new Appearance { Folk = Folk.Human };
        var dwarf = new Appearance { Folk = Folk.Dwarf };
        var halfling = new Appearance { Folk = Folk.Halfling };
        var feet = new float2(20f, 58f);
        Assert.Equal(feet, HumanLook.BuildPoint(human, new float2(20f, 58f)));
        Assert.Equal(58f, HumanLook.BuildPoint(dwarf, feet).Y, 3);
        Assert.Equal(58f, HumanLook.BuildPoint(halfling, feet).Y, 3);
        Assert.True(HumanLook.BuildPoint(halfling, new float2(20f, 15f)).Y > HumanLook.BuildPoint(dwarf, new float2(20f, 15f)).Y, "a halfling's head is lower");
        // Whole through the neck: just above and below it land together.
        var above = HumanLook.BuildPoint(dwarf, new float2(20f, 31.49f));
        var below = HumanLook.BuildPoint(dwarf, new float2(20f, 31.51f));
        Assert.True(MathF.Abs(above.Y - below.Y) < 0.05f);
    }
}

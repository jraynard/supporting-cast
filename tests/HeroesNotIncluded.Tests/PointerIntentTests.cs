using Brutal.Numerics;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded.Tests;

public class PointerIntentTests
{
    [Fact]
    public void WalkingAwayFromAnIdlePointerMakesItStale()
    {
        var p = new PointerIntent();
        p.Update(pointerMoved: false, new int3(5, 5, 1));
        Assert.False(p.Stale); // standing still: the pointer is where you left it, and means it
        p.Update(pointerMoved: false, new int3(5, 5, 1));
        Assert.False(p.Stale);
        p.Update(pointerMoved: false, new int3(6, 5, 1));
        Assert.True(p.Stale); // walked with the stick; the trackpad sat still
        p.Update(pointerMoved: false, new int3(6, 5, 1));
        Assert.True(p.Stale);
        p.Update(pointerMoved: true, new int3(6, 5, 1));
        Assert.False(p.Stale); // pointing again
    }

    [Fact]
    public void WalkingWhilePointingKeepsThePointer()
    {
        var p = new PointerIntent();
        p.Update(pointerMoved: true, new int3(5, 5, 1));
        p.Update(pointerMoved: true, new int3(6, 5, 1));
        Assert.False(p.Stale);
    }
}

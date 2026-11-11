using SupportingCast.Sim.Ecs;

namespace SupportingCast.Sim.Tests;

public class EcsTests
{
    private struct A { public int V; }
    private struct B { public int V; }

    [Fact]
    public void AddGetRemove()
    {
        var w = new World();
        var e = w.Create();
        w.Add(e, new A { V = 5 });
        Assert.True(w.Has<A>(e));
        w.Get<A>(e).V++;
        Assert.Equal(6, w.Get<A>(e).V);
        Assert.True(w.Remove<A>(e));
        Assert.False(w.Has<A>(e));
    }

    [Fact]
    public void DestroyedHandlesAreStaleAfterReuse()
    {
        var w = new World();
        var a = w.Create();
        w.Add(a, new A { V = 1 });
        w.Destroy(a);
        var b = w.Create();
        Assert.Equal(a.Index, b.Index);
        Assert.False(w.IsAlive(a));
        Assert.True(w.IsAlive(b));
        Assert.False(w.Has<A>(b));
    }

    [Fact]
    public void QueryIntersectsAndSurvivesSwapRemove()
    {
        var w = new World();
        var es = Enumerable.Range(0, 100).Select(_ => w.Create()).ToList();
        foreach (var (e, i) in es.Select((e, i) => (e, i)))
        {
            w.Add(e, new A { V = i });
            if (i % 2 == 0)
                w.Add(e, new B { V = i });
        }
        for (var i = 0; i < 100; i += 4)
            w.Destroy(es[i]);

        var both = w.Query<A, B>();
        Assert.Equal(25, both.Count);
        Assert.All(both, e => Assert.Equal(w.Get<A>(e).V, w.Get<B>(e).V));
        Assert.Equal(75, w.Count<A>());
    }

    [Fact]
    public void EachVisitsEveryEntityAndAllowsValueWrites()
    {
        var w = new World();
        for (var i = 0; i < 5; i++)
            w.Add(w.Create(), new A { V = i });
        var sum = 0;
        foreach (var e in w.Each<A>())
        {
            sum += w.Get<A>(e).V;
            w.Get<A>(e).V *= 10; // writing values is fine
            w.Add(e, new A { V = w.Get<A>(e).V }); // so is replacing an existing component
        }
        Assert.Equal(10, sum);
        Assert.Equal(100, w.Query<A>().Sum(e => w.Get<A>(e).V));
    }

    [Fact]
    public void EachThrowsWhenTheStoreChangesUnderIt()
    {
        var w = new World();
        var a = w.Create();
        w.Add(a, new A());
        w.Add(w.Create(), new A());
        Assert.Throws<InvalidOperationException>(() =>
        {
            foreach (var e in w.Each<A>())
                w.Add(w.Create(), new A());
        });
        Assert.Throws<InvalidOperationException>(() =>
        {
            foreach (var e in w.Each<A>())
                w.Destroy(e);
        });
        // Changing a different store is fine.
        foreach (var e in w.Each<A>())
            w.Add(e, new B { V = 1 });
    }

    [Fact]
    public void StoresAreSeparatePerWorld()
    {
        var w1 = new World();
        var w2 = new World();
        w1.Add(w1.Create(), new A { V = 1 });
        Assert.Equal(1, w1.Count<A>());
        Assert.Equal(0, w2.Count<A>());
    }
}

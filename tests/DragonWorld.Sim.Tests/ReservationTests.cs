using DragonWorld.Sim.Ai;

namespace DragonWorld.Sim.Tests;

public class ReservationTests
{
    private static readonly Entity Ann = new(1, 0), Bob = new(2, 0), Log = new(3, 0), Bed = new(4, 0);

    [Fact]
    public void ReleaseAllFreesOnlyThatPawnsClaims()
    {
        var r = new Reservations();
        Assert.True(r.TryReserve(Log, Ann));
        Assert.True(r.TryReserveTile(C(1, 1), Ann));
        Assert.True(r.TryReserve(Bed, Bob));
        Assert.False(r.TryReserve(Log, Bob));
        Assert.True(r.TryReserve(Log, Ann)); // re-reserving your own claim is fine

        r.ReleaseAll(Ann);
        Assert.False(r.IsReserved(Log));
        Assert.False(r.IsTileReservedByOther(C(1, 1), Bob));
        Assert.Equal(Bob, r.ReservedBy(Bed));
        Assert.True(r.TryReserve(Log, Bob));
    }

    [Fact]
    public void ReleasingATargetThenReleaseAllDoesNotTouchTheNewOwner()
    {
        var r = new Reservations();
        r.TryReserve(Log, Ann);
        r.Release(Log);
        r.TryReserve(Log, Bob);
        r.ReleaseAll(Ann); // Ann's stale index entry must not free Bob's claim
        Assert.Equal(Bob, r.ReservedBy(Log));
    }

    [Fact]
    public void ForgetDropsClaimsOnAndByAnEntity()
    {
        var r = new Reservations();
        r.TryReserve(Log, Ann);
        r.TryReserve(Ann, Bob); // someone claimed Ann herself (a rescue)
        r.Forget(Ann);
        Assert.False(r.IsReserved(Log));
        Assert.False(r.IsReserved(Ann));
    }
}

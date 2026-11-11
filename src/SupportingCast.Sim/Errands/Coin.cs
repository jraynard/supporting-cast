using SupportingCast.Sim.Components;
using SupportingCast.Sim.Systems;

namespace SupportingCast.Sim.Errands;

/// <summary>
/// Money is silver coins, real items in hands and packs (<c>SilverCoin</c>): it can be dropped,
/// stolen, hidden or found, and paying someone moves the coins from one person to the other.
/// Prices are whole silver; gold is treasure, not change.
/// </summary>
public static class Coin
{
    public const string Silver = "SilverCoin";

    /// <summary>Silver a creature has in hand and in its pack.</summary>
    public static int Count(Simulation sim, Entity who)
    {
        var total = 0;
        foreach (var stack in Stacks(sim, who))
            total += sim.World.Get<Stack>(stack).Count;
        return total;
    }

    /// <summary>
    /// Hand over up to <paramref name="amount"/> silver from one to another (hands first, then the
    /// pack). Returns how much changed hands: less than asked if they're short.
    /// </summary>
    public static int Pay(Simulation sim, Entity from, Entity to, int amount)
    {
        var paid = 0;
        foreach (var stack in Stacks(sim, from))
        {
            if (paid >= amount)
                break;
            var take = Math.Min(amount - paid, sim.World.Get<Stack>(stack).Count);
            var coins = TakeFrom(sim, from, stack, take);
            paid += take;
            Inventory.Stow(sim, to, coins);
        }
        return paid;
    }

    /// <summary>
    /// <paramref name="count"/> units of a stack a creature has in hand or in its pack, as an
    /// unspawned item of its own (the whole stack itself if that's all of it).
    /// </summary>
    public static Entity TakeFrom(Simulation sim, Entity who, Entity stack, int count)
    {
        var w = sim.World;
        ref var carrying = ref w.Get<Carrying>(who);
        if (carrying.Item == stack && count >= w.Get<Stack>(stack).Count)
        {
            carrying.Item = Entity.Null;
            return stack;
        }
        return sim.TakeFromStack(stack, count);
    }

    private static List<Entity> Stacks(Simulation sim, Entity who)
    {
        var w = sim.World;
        var stacks = new List<Entity>();
        if (!w.IsAlive(who) || !w.Has<Carrying>(who))
            return stacks;
        var held = w.Get<Carrying>(who).Item;
        if (IsSilver(sim, held))
            stacks.Add(held);
        var pack = sim.PackOf(who);
        if (!pack.IsNull)
            foreach (var item in sim.StoredIn(pack))
                if (IsSilver(sim, item))
                    stacks.Add(item);
        return stacks;
    }

    public static bool IsSilver(Simulation sim, Entity item) =>
        sim.World.IsAlive(item) && sim.World.TryGet<Thing>(item, out var t) && t.Def.Name == Silver;
}

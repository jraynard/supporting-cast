using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Core;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Shops;

/// <summary>
/// What a shop's called, painted over its door by its keeper ("The Wild Bear"), on the building.
/// Without one it goes by its def's <see cref="Defs.BuildingDef.DefaultName"/>, if any. It's on
/// the sign for anyone to read, so it isn't a secret.
/// </summary>
public sealed class ShopName
{
    /// <summary>The longest name a sign takes.</summary>
    public const int MaxLength = 32;

    public string Name = "";

    /// <summary>The shop's name, or null if it has none (it goes by its label).</summary>
    public static string? Of(Simulation sim, Entity building)
    {
        var w = sim.World;
        if (!w.IsAlive(building) || !w.TryGet<PresetBuilding>(building, out var b))
            return null;
        if (w.TryGet<ShopName>(building, out var n) && n.Name.Length > 0)
            return n.Name;
        return b.Def.DefaultName.Length > 0 ? b.Def.DefaultName : null;
    }

    /// <summary>A name as it'll be painted: printable, single spaces, no longer than <see cref="MaxLength"/>. Empty if there's nothing left.</summary>
    public static string Clean(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "";
        // Any whitespace (a pasted newline) is a space; anything else odd goes.
        var kept = new string(name.Select(c => char.IsWhiteSpace(c) ? ' ' : c)
            .Where(c => char.IsLetterOrDigit(c) || c is ' ' or '\'' or '-' or '&' or ',' or '.' or '!').ToArray());
        var words = kept.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var clean = string.Join(' ', words);
        return clean.Length > MaxLength ? clean[..MaxLength].TrimEnd() : clean;
    }

    /// <summary>
    /// The keeper names the shop (an empty name puts the default back). False, and nothing
    /// changes, unless they keep it and their trade's the one that names it (the innkeeper's).
    /// </summary>
    public static bool Set(Simulation sim, Entity who, Entity building, string? name)
    {
        if (!ShopPrices.Keeps(sim, who, building) || !sim.World.Get<Trade>(who).Def.NamesShop)
            return false;
        var w = sim.World;
        if (!w.TryGet<ShopName>(building, out var n))
            w.Add(building, n = new ShopName());
        n.Name = Clean(name);
        return true;
    }
}

/// <summary>Name your shop (<see cref="ShopName.Set"/>): only its keeper can.</summary>
public sealed record SetShopNameCommand(Entity Avatar, Entity Shop, string? Name) : ISimCommand, IActorCommand
{
    public Entity Actor => Avatar;

    public void Apply(Simulation sim)
    {
        if (!sim.World.IsAlive(Avatar) || !sim.World.IsAlive(Shop))
            return;
        var was = ShopName.Of(sim, Shop);
        if (ShopName.Set(sim, Avatar, Shop, Name) && ShopName.Of(sim, Shop) is { } now && now != was)
            sim.Messages.Add(MessageKind.Info, $"The sign over the door reads \"{now}\" now.", sim.World.Get<PresetBuilding>(Shop).Anchor);
    }
}

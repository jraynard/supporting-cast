using SupportingCast.Sim.Components;
using SupportingCast.Sim.Defs;

namespace SupportingCast.Sim.Environment;

/// <summary>Shape of one breath: how far it reaches, how wide, how many rays, how hot.</summary>
/// <param name="SparesBuildings">
/// A sneeze, not a real breath: it scorches, but won't set alight structures, floors or anything
/// in an indoor room, so a hatchling in its nest doesn't burn the hall down.
/// </param>
public readonly record struct BreathShape(float Range, float ConeDegrees, int Rays, float Intensity, float SplashRadius, bool SparesBuildings = false);

/// <summary>What one breath did, for messages, tests and the renderer.</summary>
public sealed class BreathEvent
{
    public long Tick;

    /// <summary>Who breathed (null for unattributed test breaths). Read by the renderer to animate the breather.</summary>
    public Entity Source = Entity.Null;

    /// <summary>What was breathed (render-only: the colour and kind of effect drawn).</summary>
    public DragonElement Element;
    public float3 Mouth;
    public readonly List<float3> RayEnds = [];

    /// <summary>Lightning leaping between those it struck, as segments (render-only; <c>Combat.CombatRules.Breathe</c>).</summary>
    public readonly List<(float3 From, float3 To)> Arcs = [];

    /// <summary>Cells set alight.</summary>
    public int Ignited;

    /// <summary>What another element did to the cells it passed: fires doused, plants withered or nurtured.</summary>
    public int Affected;
}

/// <summary>
/// A dragon's breath as a deterministic cone of rays (<see cref="Maps.Map.Raycast"/>), jittered
/// with <see cref="Simulation.Rand"/>; every element shares the cone and differs in what it does
/// to the cells it passes (<see cref="ElementDef.Effect"/>). Fire sets alight anything flammable in
/// the cells it passes through and stops at rock and walls. Where it strikes a surface the flames
/// splash outward in a ring, so breath poured down into a forest spills across it; ordinary fire
/// spread takes over from there. Frost cools and douses, lightning now and then lights a fire,
/// poison withers plants and leaves a cloud (<see cref="VenomClouds"/>), thorns nurture what
/// grows. Recent breaths are kept briefly for the renderer.
/// </summary>
public static class Breath
{
    /// <summary>How long a breath stays in <see cref="Simulation.RecentBreaths"/> (and on screen).</summary>
    public const int ShowTicks = 40;

    /// <summary>How long a plant breathed on with green regrows faster (<see cref="Nurtured"/>).</summary>
    public const long NurtureTicks = SimClock.TicksPerDay;

    /// <summary>A dragon's breath reach now: its life stage's, times its element's factor.</summary>
    public static float RangeOf(Simulation sim, Entity dragon)
    {
        var stage = sim.World.Get<Creature>(dragon).Stage(sim.Clock.Tick);
        return Element(sim, dragon) is { } el ? stage.BreathRange * el.Range : stage.BreathRange;
    }

    /// <summary>
    /// The breath a dragon looses on purpose (a job, a fight): its life stage's cone scaled by its
    /// element (<see cref="ElementDef"/>; fire's factors are 1, so a fire breath is the stage's own).
    /// </summary>
    public static BreathShape ShapeOf(Simulation sim, Entity dragon)
    {
        var stage = sim.World.Get<Creature>(dragon).Stage(sim.Clock.Tick);
        if (Element(sim, dragon) is not { } el)
            return new BreathShape(stage.BreathRange, stage.BreathCone, stage.BreathRays, 0.9f, 1.5f);
        return new BreathShape(stage.BreathRange * el.Range, stage.BreathCone * el.Cone,
            Math.Max(2, (int)MathF.Round(stage.BreathRays * el.Rays)), el.Intensity, el.Splash);
    }

    /// <summary>The element def a creature breathes with (null when the defs have none: then it's fire).</summary>
    private static ElementDef? Element(Simulation sim, Entity e) =>
        sim.Defs.AllElements.Count == 0 ? null : Systems.Dragons.BreathOf(sim, e);

    /// <param name="element">What's breathed; null is the breather's own element (fire for test breaths without one).</param>
    public static BreathEvent Breathe(Simulation sim, float3 mouth, float3 target, BreathShape shape, Entity? source = null, ElementDef? element = null)
    {
        var map = sim.Map;
        var ev = new BreathEvent { Tick = sim.Clock.Tick, Mouth = mouth, Source = source ?? Entity.Null };
        var el = element ?? (sim.Defs.AllElements.Count == 0 ? null : Systems.Dragons.BreathOf(sim, ev.Source));
        var effect = el?.Effect ?? BreathEffect.Ignite;
        ev.Element = el?.Element ?? DragonElement.Fire;
        var spare = SoulboundKeeper(sim, ev.Source);
        var aim = Normalize(target - mouth);
        var spread = DetMath.Tan(shape.ConeDegrees * MathF.PI / 360f);

        // Two axes perpendicular to the aim, for jittering rays inside the cone.
        var side = MathF.Abs(aim.Z) > 0.9f ? new float3(1, 0, 0) : Normalize(Cross(aim, new float3(0, 0, 1)));
        var up = Cross(side, aim);

        for (var r = 0; r < shape.Rays; r++)
        {
            var dir = Normalize(aim + side * (sim.Rand.Range(-1f, 1f) * spread) + up * (sim.Rand.Range(-1f, 1f) * spread));
            var traversed = 0;
            var lastAir = -1;
            var hit = map.Raycast(
                mouth,
                dir,
                shape.Range,
                i =>
                {
                    if (Blocks(sim, i))
                        return true;
                    lastAir = i;
                    // Fades along the ray; the first cell (the dragon's own) is spared.
                    if (traversed++ > 0 && Effect(sim, i, shape.Intensity * (1f - 0.5f * traversed / (shape.Range + 1)), shape, spare, effect, el, ev))
                        ev.Ignited++;
                    return false;
                },
                out var cell,
                out _
            );

            if (hit && lastAir >= 0)
            {
                var impact = map.CellOf(lastAir);
                ev.RayEnds.Add(new float3(impact.X + 0.5f, impact.Y + 0.5f, impact.Z + 0.3f));
                ev.Ignited += Splash(sim, impact, shape, spare, effect, el, ev);
            }
            else
            {
                ev.RayEnds.Add(mouth + dir * shape.Range);
            }
        }

        // Poison hangs about where it lands (not a sneeze's: a hatchling's cough is harmless).
        if (effect == BreathEffect.Cloud && el?.Cloud is { } cloud && !shape.SparesBuildings)
            VenomClouds.Spawn(sim, CloudCell(sim, target, ev), cloud, el, ev.Source);

        sim.RecentBreaths.Add(ev);
        return ev;
    }

    /// <summary>Where a poison breath's cloud settles: the aim point's cell if it's open, else where the first ray ended.</summary>
    private static int3 CloudCell(Simulation sim, float3 target, BreathEvent ev)
    {
        var map = sim.Map;
        var at = new int3((int)MathF.Floor(target.X), (int)MathF.Floor(target.Y), (int)MathF.Floor(target.Z));
        if (map.InBounds(at) && !map.IsSolid(map.Idx(at)))
            return at;
        var end = ev.RayEnds.Count > 0 ? ev.RayEnds[0] : ev.Mouth;
        var c = new int3((int)MathF.Floor(end.X), (int)MathF.Floor(end.Y), (int)MathF.Floor(end.Z));
        return map.InBounds(c) ? c : new int3(Math.Clamp(c.X, 0, map.Width - 1), Math.Clamp(c.Y, 0, map.Height - 1), Math.Clamp(c.Z, 0, map.Depth - 1));
    }

    /// <summary>What a breath does to one cell it reaches; true if it set the cell alight.</summary>
    private static bool Effect(Simulation sim, int i, float intensity, BreathShape shape, int3? spare, BreathEffect effect, ElementDef? el, BreathEvent ev)
    {
        if (effect == BreathEffect.Ignite || el == null)
            return Burn(sim, i, intensity, shape, spare); // fire, exactly as it has always burned
        if (Spared(sim, i, spare))
            return false;
        switch (effect)
        {
            case BreathEffect.Freeze:
                if (Freeze(sim, i, intensity, el))
                    ev.Affected++;
                return false;
            case BreathEffect.Shock:
                return Shock(sim, i, intensity, shape, el);
            case BreathEffect.Cloud:
                if (!shape.SparesBuildings && VenomClouds.Wilt(sim, sim.Map.Structure[i], el.Wilt * intensity))
                    ev.Affected++;
                return false;
            case BreathEffect.Bloom:
                if (Nurture(sim, sim.Map.Structure[i]))
                    ev.Affected++;
                return false;
            default:
                return false;
        }
    }

    private static bool Spared(Simulation sim, int i, int3? spare) =>
        spare is { } k && sim.Map.CellOf(i) is var c && Math.Abs(c.Z - k.Z) <= 1 && Math.Max(Math.Abs(c.X - k.X), Math.Abs(c.Y - k.Y)) <= 1;

    /// <summary>Frost: cools the air toward the element's floor and beats down any fire. True if it doused one.</summary>
    private static bool Freeze(Simulation sim, int i, float intensity, ElementDef el)
    {
        var map = sim.Map;
        if (map.Active[i] && map.Temperature[i] > el.CoolFloor)
            map.Temperature[i] = Math.Max(el.CoolFloor, map.Temperature[i] - el.Cool * intensity);
        if (map.Fire[i] <= 0)
            return false;
        map.Fire[i] = Math.Max(0f, map.Fire[i] - el.Douse * intensity);
        return true;
    }

    /// <summary>Lightning: a little heat, and now and then a fire (never in a sneeze's sheltered cells).</summary>
    private static bool Shock(Simulation sim, int i, float intensity, BreathShape shape, ElementDef el)
    {
        var map = sim.Map;
        if (map.Active[i] && map.Temperature[i] < 300f)
            map.Temperature[i] = Math.Min(300f, map.Temperature[i] + 15f * intensity);
        if (shape.SparesBuildings && Sheltered(sim, i))
            return false;
        return map.Fire[i] <= 0 && el.IgniteChance > 0 && sim.Rand.Chance(el.IgniteChance) && sim.Fire.Ignite(sim, map.CellOf(i), intensity * 0.6f);
    }

    /// <summary>Green breath on a harvested plant that's growing back: it grows back faster for a day. True if it took.</summary>
    public static bool Nurture(Simulation sim, Entity plant)
    {
        var w = sim.World;
        if (plant.IsNull || !w.TryGet<PlantState>(plant, out var p) || p.Ready)
            return false;
        var until = sim.Clock.Tick + NurtureTicks;
        if (w.TryGet<Nurtured>(plant, out var n) && n.Until >= until)
            return false;
        if (w.Has<Nurtured>(plant))
            w.Get<Nurtured>(plant).Until = until;
        else
            w.Add(plant, new Nurtured { Until = until });
        return true;
    }

    /// <summary>Rock and walls stop the flames.</summary>
    private static bool Blocks(Simulation sim, int i)
    {
        if (sim.Map.IsSolid(i))
            return true;
        var s = sim.Map.Structure[i];
        return !s.IsNull && sim.World.Get<Thing>(s).Def.Building?.IsWall == true && !sim.World.Has<Construction>(s);
    }

    /// <summary>A soulbound dragon's keeper's cell: its fire never touches them (Systems.Bonding).</summary>
    private static int3? SoulboundKeeper(Simulation sim, Entity dragon)
    {
        var w = sim.World;
        if (dragon.IsNull || !w.TryGet<Dragon>(dragon, out var d) || !w.IsAlive(d.Keeper) || !Systems.Bonding.Reached(sim, dragon, Systems.Bonding.Soulbound))
            return null;
        return sim.LocationOf(d.Keeper);
    }

    private static bool Burn(Simulation sim, int i, float intensity, BreathShape shape, int3? spare)
    {
        var map = sim.Map;
        if (spare is { } k && map.CellOf(i) is var c && Math.Abs(c.Z - k.Z) <= 1 && Math.Max(Math.Abs(c.X - k.X), Math.Abs(c.Y - k.Y)) <= 1)
            return false;
        if (map.Active[i] && map.Temperature[i] < 300f)
            map.Temperature[i] = Math.Min(300f, map.Temperature[i] + 40f * intensity);
        if (shape.SparesBuildings && Sheltered(sim, i))
            return false;
        return map.Fire[i] <= 0 && sim.Fire.Ignite(sim, map.CellOf(i), intensity);
    }

    /// <summary>Part of a building: a structure or floor, or inside an indoor room.</summary>
    private static bool Sheltered(Simulation sim, int i)
    {
        var map = sim.Map;
        return !map.Structure[i].IsNull || !map.Floor[i].IsNull || sim.Rooms.RoomAt(map.CellOf(i))?.Indoors == true;
    }

    /// <summary>Flames (or frost, or poison) spilling across the surface around where a ray struck, thinning out with distance.</summary>
    private static int Splash(Simulation sim, int3 impact, BreathShape shape, int3? spare, BreathEffect effect, ElementDef? el, BreathEvent ev)
    {
        var ignited = 0;
        foreach (var c in sim.SpiralFrom(impact, (int)MathF.Ceiling(shape.SplashRadius)))
        {
            var dx = c.X - impact.X;
            var dy = c.Y - impact.Y;
            var d = MathF.Sqrt(dx * dx + dy * dy);
            if (d > shape.SplashRadius || Math.Abs(c.Z - impact.Z) > 1)
                continue;
            var falloff = 1f - d / (shape.SplashRadius + 1f);
            if (sim.Rand.Chance(0.6f * falloff) && Effect(sim, sim.Map.Idx(c), shape.Intensity * falloff, shape, spare, effect, el, ev))
                ignited++;
        }
        return ignited;
    }

    /// <summary>Forget breaths that are no longer shown.</summary>
    public static void Prune(Simulation sim) =>
        sim.RecentBreaths.RemoveAll(b => sim.Clock.Tick - b.Tick > ShowTicks);

    private static float3 Normalize(float3 v)
    {
        var len = MathF.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
        return len < 1e-6f ? new float3(1, 0, 0) : v * (1f / len);
    }

    private static float3 Cross(float3 a, float3 b) =>
        new(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
}

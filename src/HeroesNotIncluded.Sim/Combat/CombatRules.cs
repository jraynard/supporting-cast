using HeroesNotIncluded.Sim.Ai;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Environment;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Combat;

/// <summary>Why an attack has the odds it has, for the UI tooltip and the log.</summary>
public readonly record struct HitChance(
    float Chance,
    float Base,
    float Range,
    float Cover,
    float Height,
    float Size,
    float Helpless,
    float Skill,
    float Wounds,
    float DamageChance,
    string? Blocked,
    float Reaction = 1f,
    float Aim = 1f
)
{
    public bool Possible => Blocked == null;
}

public enum AttackOutcome
{
    Miss,
    Glance,
    Deflected,
    Hit,
}

public sealed record AttackResult(Entity Attacker, Entity Target, AttackDef Attack, float Chance, AttackOutcome Outcome, string? Part, float Damage)
{
    public string Describe(Simulation sim)
    {
        var a = sim.World.Get<Creature>(Attacker).Name;
        var t = sim.World.Get<Creature>(Target);
        var head = $"{a} {Attack.Verb} {t.Name} ({Chance:P0})";
        return Outcome switch
        {
            AttackOutcome.Miss => $"{head}: miss.",
            AttackOutcome.Glance => $"{head}: hit, but it glances off.",
            AttackOutcome.Deflected => $"{head}: hit, but it's turned by the {Part}.",
            _ => $"{head}: {Damage:F0} {DamageTypes.Wound(Attack.DamageType)} damage to the {Part}"
                + (t.Dead ? $". {t.Name} is killed!" : t.Downed ? $". {t.Name} goes down!" : "."),
        };
    }
}

/// <summary>
/// Attack resolution for turn-based combat, as pure functions over the sim plus rolls on
/// <see cref="Simulation.Rand"/>. An attack first rolls to hit (accuracy, range, cover, height,
/// target size, the attacker's skill and wounds), then for where it lands (coverage, skewed by
/// height), then to actually do damage (glancing blows, the armor on that part the weapon
/// doesn't penetrate). Armor that doesn't turn a blow still blunts it.
/// </summary>
public static class CombatRules
{
    public const float MinChance = 0.05f, MaxChance = 0.95f;

    /// <summary>What a creature can attack with: its weapon, then natural attacks. None if it won't fight.</summary>
    public static List<AttackDef> Attacks(Simulation sim, Entity e)
    {
        var w = sim.World;
        var list = new List<AttackDef>();
        if (w.Has<NonViolent>(e))
            return list;
        if (w.TryGet<Equipment>(e, out var eq) && w.IsAlive(eq.Weapon) && w.Get<Thing>(eq.Weapon).Def.Weapon is { } weapon)
            list.Add(weapon);
        var c = w.Get<Creature>(e);
        list.AddRange(c.Stage(sim.Clock.Tick).Attacks);
        list.AddRange(c.Def.Attacks);
        return list;
    }

    /// <summary>Eye height above a creature's feet (a rider's feet are on their mount's back).</summary>
    private static float3 Eye(Simulation sim, Entity e, int3 cell) =>
        Position.CenterOf(cell) + new float3(0, 0, 0.7f * sim.World.Get<Creature>(e).Stage(sim.Clock.Tick).Size);

    private static float3 Body(Simulation sim, Entity e, int3 cell) =>
        Position.CenterOf(cell) + new float3(0, 0, 0.5f * sim.World.Get<Creature>(e).Stage(sim.Clock.Tick).Size);

    /// <summary>Share of blows aimed at a rider that land on the mount instead.</summary>
    public const float MountTakesHit = 0.2f;

    /// <summary>Rock and finished walls stop arrows (and sight).</summary>
    private static bool Blocks(Simulation sim, int i)
    {
        if (sim.Map.IsSolid(i))
            return true;
        var s = sim.Map.Structure[i];
        return !s.IsNull && sim.World.Get<Thing>(s).Def.Building?.IsWall == true && !sim.World.Has<Construction>(s);
    }

    /// <summary>Clear line between two points, ignoring the cells they're in.</summary>
    public static bool LineOfFire(Simulation sim, float3 from, float3 to)
    {
        var d = to - from;
        var len = MathF.Sqrt(d.X * d.X + d.Y * d.Y + d.Z * d.Z);
        if (len < 1e-3f)
            return true;
        var fromIdx = sim.Map.Idx(new int3((int)MathF.Floor(from.X), (int)MathF.Floor(from.Y), (int)MathF.Floor(from.Z)));
        var toIdx = sim.Map.Idx(new int3((int)MathF.Floor(to.X), (int)MathF.Floor(to.Y), (int)MathF.Floor(to.Z)));
        return !sim.Map.Raycast(from, d * (1f / len), len, i => i != fromIdx && i != toIdx && Blocks(sim, i), out _, out _);
    }

    /// <summary>
    /// Something solid right in front of the target, on the attacker's side: a wall or boulder
    /// gives good cover, a tree some. Airborne targets have none.
    /// </summary>
    public static float Cover(Simulation sim, int3 from, int3 target)
    {
        if (Airborne(sim, target))
            return 0;
        var dx = Math.Sign(from.X - target.X);
        var dy = Math.Sign(from.Y - target.Y);
        if (dx == 0 && dy == 0)
            return 0;
        var map = sim.Map;
        var best = 0f;
        // The cell straight toward the attacker, and for diagonals its two neighbors too.
        foreach (var (x, y) in new[] { (dx, dy), (dx, 0), (0, dy) })
        {
            if (x == 0 && y == 0)
                continue;
            var c = new int3(target.X + x, target.Y + y, target.Z);
            if (!map.InBounds(c))
                continue;
            var i = map.Idx(c);
            var s = map.Structure[i];
            var cover = 0f;
            if (map.IsSolid(i))
                cover = 0.4f;
            else if (!s.IsNull && !sim.World.Has<Construction>(s))
            {
                var def = sim.World.Get<Thing>(s).Def;
                cover = def.Building?.IsWall == true || def.Category == ThingCategory.Resource ? 0.4f
                    : def.Category == ThingCategory.Plant && def.PathCost >= 2 ? 0.2f
                    : 0f;
            }
            // Only the head-on cell counts fully.
            best = Math.Max(best, x == dx && y == dy ? cover : cover * 0.5f);
        }
        return best;
    }

    /// <summary>
    /// Reactions (snap shots at a mover, swings as they pass) are hurried: their odds are scaled
    /// by this on top of the snap shot's own (<see cref="TimeUnits.SnapAccuracy"/>).
    /// </summary>
    public const float ReactionFactor = 0.8f;

    /// <summary>Odds of <paramref name="attacker"/> hitting <paramref name="target"/>, as if it stood at <paramref name="from"/>.</summary>
    public static HitChance Chance(Simulation sim, Entity attacker, int3 from, Entity target, AttackDef attack, bool reaction = false) =>
        Chance(sim, attacker, from, target, sim.World.Get<Position>(target).Cell, attack, reaction);

    /// <summary>
    /// Odds with both ends placed: the attacker at <paramref name="from"/>, the target at
    /// <paramref name="to"/> (where a move would take it; see <see cref="Reactions.Threats"/>).
    /// </summary>
    public static HitChance Chance(Simulation sim, Entity attacker, int3 from, Entity target, int3 to, AttackDef attack, bool reaction = false)
    {
        var w = sim.World;
        var tc = w.Get<Creature>(target);
        var damage = DamageChance(sim, target, attack);
        HitChance Blocked(string why) => new(0, attack.Accuracy, 0, 0, 0, 0, 0, 0, 1, damage, why);

        if (tc.Dead)
            return Blocked("already dead");
        var dist = Distance(from, to);
        var attackerAir = Airborne(sim, from);
        var targetAir = Airborne(sim, to);
        float range = 0, cover = 0, size = 0;
        if (attack.Ranged)
        {
            if (dist > attack.Range + 0.01f)
                return Blocked("out of range");
            if (!LineOfFire(sim, Eye(sim, attacker, from), Body(sim, target, to)))
                return Blocked("no line of fire");
            range = Math.Max(0, dist - attack.OptimalRange) * attack.AccuracyFalloff;
            cover = Cover(sim, from, to);
            size = Math.Max(0, tc.Stage(sim.Clock.Tick).Size - 1f) * 0.1f + (targetAir ? 0.05f : 0f);
        }
        else
        {
            if (!Pathfinder.Touches(from, to))
                return Blocked("out of reach");
            if (attackerAir != targetAir)
                return Blocked(targetAir ? "target is in the air" : "can't reach the ground from the air");
        }

        var dz = from.Z - to.Z;
        var height = Math.Clamp(dz * (attack.Ranged ? 0.05f : 0.03f), -0.15f, 0.15f);
        var wounds = 1f;
        if (w.TryGet<Health>(attacker, out var h))
            wounds = h.Capacity(Capacity.Manipulation) * (0.5f + 0.5f * h.Capacity(Capacity.Sight));
        // A collapsed or crippled target is easy to hit.
        var helpless = tc.Downed ? 0.5f : w.TryGet<Health>(target, out var th) ? 0.1f * (1f - th.Capacity(Capacity.Moving)) : 0f;
        var skill = SkillRules.Accuracy(sim, attacker, SkillFor(attack));
        var hurried = reaction ? ReactionFactor : 1f;
        const float aim = 1f;
        var chance = Math.Clamp((attack.Accuracy - range - cover + height + size + helpless + skill) * wounds * hurried * aim, MinChance, MaxChance);
        return new HitChance(chance, attack.Accuracy, range, cover, height, size, helpless, skill, wounds, damage, null, hurried, aim);
    }

    /// <summary>Bows train (and use) Shooting, everything else Melee.</summary>
    public static Skill SkillFor(AttackDef attack) => attack.Ranged ? Skill.Shooting : Skill.Melee;

    public static HitChance Chance(Simulation sim, Entity attacker, Entity target, AttackDef attack) =>
        Chance(sim, attacker, sim.World.Get<Position>(attacker).Cell, target, attack);

    /// <summary>Is this cell in open air, with nothing to stand on?</summary>
    public static bool Airborne(Simulation sim, int3 cell) =>
        sim.Map.InBounds(cell) && !sim.Map.Passable(cell) && !sim.Map.IsSolid(sim.Map.Idx(cell));

    /// <summary>Could a melee blow pass between these cells (adjacent, and both on the ground or both in the air)?</summary>
    public static bool InReach(Simulation sim, int3 a, int3 b) =>
        Pathfinder.Touches(a, b) && Airborne(sim, a) == Airborne(sim, b);

    /// <summary>Expected damage of an attack with these odds (the AI's yardstick).</summary>
    public static float Expected(in HitChance odds, AttackDef attack) =>
        odds.Possible ? odds.Chance * odds.DamageChance * (attack.DamageMin + attack.DamageMax) * 0.5f : 0f;

    /// <summary>
    /// Chance a hit actually hurts: it doesn't glance off and gets through the armor, averaged
    /// over where it might land.
    /// </summary>
    public static float DamageChance(Simulation sim, Entity target, AttackDef attack)
    {
        if (!sim.World.TryGet<Health>(target, out var h))
            return (1f - attack.Glance) * (1f - Through(ArmorAt(sim, target, -1, attack.DamageType).Armor, attack));
        float total = 0, gets = 0;
        for (var i = 0; i < h.Body.Parts.Count; i++)
        {
            if (h.PartHealth(i) <= 0)
                continue;
            var cover = h.Body.Parts[i].Coverage;
            total += cover;
            gets += cover * (1f - Through(ArmorAt(sim, target, i, attack.DamageType).Armor, attack));
        }
        return (1f - attack.Glance) * (total > 0 ? gets / total : 1f);
    }

    /// <summary>The part of the armor a weapon doesn't penetrate: the chance it turns the blow.</summary>
    private static float Through(float armor, AttackDef attack) => Math.Max(0f, armor - attack.Penetration);

    /// <summary>
    /// What protects a part (-1: the whole body) against a kind of damage: a dragon's scales or
    /// the best worn item covering it, and that item (Entity.Null for scales or nothing).
    /// </summary>
    public static (float Armor, Entity Item) ArmorAt(Simulation sim, Entity target, int part, DamageType type)
    {
        var w = sim.World;
        var best = w.Get<Creature>(target).Stage(sim.Clock.Tick).Armor;
        var item = Entity.Null;
        if (part >= 0 && w.TryGet<Health>(target, out var h))
        {
            var name = h.Body.Parts[part].Name;
            foreach (var worn in sim.Worn(target))
            {
                var a = w.Get<Thing>(worn).Def.Apparel!;
                var armor = a.Against(type) * Crafting.QualityFactorOf(sim, worn);
                if (a.CoversPart(name) && armor > best)
                {
                    best = armor;
                    item = worn;
                }
            }
        }
        return (best, item);
    }

    /// <summary>Armor that took a blow wears down by half the damage it stopped, and falls apart at 0 HP.</summary>
    public static void WearDown(Simulation sim, Entity wearer, Entity item, float absorbed)
    {
        if (item.IsNull || absorbed <= 0 || !sim.World.IsAlive(item))
            return;
        ref var t = ref sim.World.Get<Thing>(item);
        t.Hp -= absorbed * 0.5f;
        if (t.Hp > 0)
            return;
        var label = t.Label;
        ref var worn = ref sim.World.Get<Apparel>(wearer);
        worn[t.Def.Apparel!.Slot] = Entity.Null;
        sim.DestroyThing(item);
        var c = sim.World.Get<Creature>(wearer);
        sim.Messages.Add(c.Faction == Faction.Village ? MessageKind.Warning : MessageKind.Info,
            $"{c.Name}'s {label} falls apart.", sim.LocationOf(wearer));
    }

    public static float Distance(int3 a, int3 b)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>
    /// A blow that has reached its target (<see cref="Reach"/>, with free movement): no roll to hit
    /// and none to glance or be turned. Where it lands is by coverage (higher from above), and the
    /// armour there takes a fixed share of it, as much as it outmatches the weapon's bite, and wears
    /// for it. Returns what got through.
    /// </summary>
    public static AttackResult Strike(Simulation sim, Entity attacker, Entity target, AttackDef attack, float damageScale = 1f)
    {
        var w = sim.World;
        SkillRules.Learn(sim, attacker, SkillFor(attack), SkillRules.XpPerHit);
        var dz = w.Get<Position>(attacker).Cell.Z - w.Get<Position>(target).Cell.Z;
        PartHeight? bias = dz > 0 ? PartHeight.High : dz < 0 ? PartHeight.Low : null;
        var health = w.TryGet<Health>(target, out var h) ? h : null;
        var part = health != null ? HealthSystem.PickPart(sim, health, bias) : -1;
        var damage = sim.Rand.Range(attack.DamageMin, attack.DamageMax) * damageScale;
        var weapon = w.TryGet<Equipment>(attacker, out var wielded) && w.IsAlive(wielded.Weapon)
            && w.Get<Thing>(wielded.Weapon).Def.Weapon == attack ? wielded.Weapon : Entity.Null;
        if (!weapon.IsNull)
            damage *= Crafting.QualityFactorOf(sim, weapon) * WeaponWear.DamageFactor(sim, weapon);
        var (armor, item) = ArmorAt(sim, target, part, attack.DamageType);
        // What the old roll took off on average: turned outright as often as it outmatched the
        // weapon, else half of that blunted; as a share, every time.
        var t = Through(armor, attack);
        var soaked = damage * Math.Clamp(t + (1f - t) * 0.5f * t, 0f, MaxSoak);
        WearDown(sim, target, item, soaked);
        if (!weapon.IsNull)
            WeaponWear.Struck(sim, attacker, weapon, turned: false, soaked);
        damage -= soaked;
        HealthSystem.Damage(sim, target, damage, attack.DamageType, part);
        var label = health != null && part >= 0 ? health.Body.Parts[part].Label : "body";
        return new AttackResult(attacker, target, attack, 1f, AttackOutcome.Hit, label, damage);
    }

    /// <summary>The most of a blow armour takes (<see cref="Strike"/>): even plate lets something through.</summary>
    public const float MaxSoak = 0.85f;

    /// <summary>
    /// A dragon's breath in combat: the usual cone (which sets the ground alight, or frosts it, or
    /// leaves a cloud: <see cref="Breath"/>), plus a wound on every creature it passes over, friend
    /// or foe: burns. Fireproof creatures (a dragon's own kind) shrug it off. Loosed by a wild
    /// dragon on the town (<see cref="Ai.DragonPassJob"/>) after it's drawn breath where it can be
    /// seen: given when it <paramref name="drawn"/> it, a guard or a late dodge then takes some of it.
    /// </summary>
    public static (BreathEvent Breath, List<(Entity Who, float Damage)> Burned) Breathe(Simulation sim, Entity dragon, int3 target, long? drawn = null)
    {
        var w = sim.World;
        var stage = w.Get<Creature>(dragon).Stage(sim.Clock.Tick);
        var mouth = w.Get<Position>(dragon).Pos + new float3(0, 0, stage.Size * 0.6f);
        var shape = Breath.ShapeOf(sim, dragon);
        const DamageType type = DamageType.Burn;
        const float min = 1.5f, max = 3f;
        var ev = Breath.Breathe(sim, mouth, Position.CenterOf(target) + new float3(0, 0, 0.3f), shape, dragon);

        var burned = new List<(Entity, float)>();
        foreach (var e in sim.Creatures())
        {
            if (e == dragon || w.Get<Creature>(e).Def.SafeTempMax >= 300)
                continue; // fireproof
            var body = Body(sim, e, w.Get<Position>(e).Cell);
            var hits = 0;
            foreach (var end in ev.RayEnds)
                if (DistanceToSegment(body, mouth, end) < 0.6f)
                    hits++;
            if (hits == 0)
                continue;
            // Seen coming (Blows.Inhaling, drawn at drawn): a guard up then takes a little of it,
            // and a late dodge half; nothing gets anyone wholly clear of a dragon's fire.
            var share = 1f;
            if (drawn is { } when && w.Has<Guard>(e))
            {
                ref var g = ref w.Get<Guard>(e);
                if (Blows.DodgedAt(g, when, dragon: true))
                {
                    share = Blows.BreathDodged;
                    Blows.Report(sim, dragon, e, BlowOutcome.Dodged, heavy: true);
                }
                else if (g.UpAt(when) && g.StaggeredUntil <= when)
                {
                    share = Blows.DragonBlocked;
                    g.Stamina = Math.Max(0f, g.Stamina - Blows.BlockCost * 2f);
                    Blows.Report(sim, dragon, e, BlowOutcome.Blocked, heavy: true);
                }
            }
            var total = 0f;
            for (var i = 0; i < hits; i++)
            {
                total += Wound(sim, e, sim.Rand.Range(min, max) * share, type);
                if (w.Get<Creature>(e).Dead)
                    break;
            }
            burned.Add((e, total));
        }
        return (ev, burned);
    }

    /// <summary>One wound from a breath: heat armor takes some of the sting out of fire and frost (none out of venom).</summary>
    private static float Wound(Simulation sim, Entity e, float dmg, DamageType type)
    {
        var health = sim.World.TryGet<Health>(e, out var h) ? h : null;
        var part = health != null ? HealthSystem.PickPart(sim, health) : -1;
        // Breath isn't turned, but armor against it blunts it.
        var (armor, item) = ArmorAt(sim, e, part, type);
        var blunted = dmg * 0.5f * armor;
        WearDown(sim, e, item, blunted);
        dmg -= blunted;
        HealthSystem.Damage(sim, e, dmg, type, part);
        return dmg;
    }

    private static float Length(float3 v) => MathF.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);

    private static float DistanceToSegment(float3 p, float3 a, float3 b)
    {
        var ab = b - a;
        var len2 = ab.X * ab.X + ab.Y * ab.Y + ab.Z * ab.Z;
        var t = len2 < 1e-6f ? 0 : Math.Clamp(((p.X - a.X) * ab.X + (p.Y - a.Y) * ab.Y + (p.Z - a.Z) * ab.Z) / len2, 0f, 1f);
        var q = a + ab * t - p;
        return MathF.Sqrt(q.X * q.X + q.Y * q.Y + q.Z * q.Z);
    }
}

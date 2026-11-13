using HeroesNotIncluded.Sim.Combat;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;

namespace HeroesNotIncluded.Sim.Saving;

/// <summary>Writes and reads one component type's store: entity plus data, in dense order.</summary>
internal abstract class ComponentCodec
{
    public abstract string Name { get; }
    public abstract Type Type { get; }
    public abstract int Count(World world);
    public abstract void Write(SaveWriter w, World world);
    public abstract void Read(SaveReader r, World world);

    /// <summary>One value on its own, for commands that carry a component (a look picked in the creator).</summary>
    public abstract void WriteValue(SaveWriter w, object value);
    public abstract object ReadValue(SaveReader r);
}

internal sealed class ComponentCodec<T>(string name, Action<SaveWriter, T> write, Func<SaveReader, T> read) : ComponentCodec
{
    public override string Name => name;
    public override Type Type => typeof(T);
    public override int Count(World world) => world.Store<T>().Count;
    public override void WriteValue(SaveWriter w, object value) => write(w, (T)value);
    public override object ReadValue(SaveReader r) => read(r)!;

    public override void Write(SaveWriter w, World world)
    {
        var store = world.Store<T>();
        w.Int(store.Count);
        for (var i = 0; i < store.Count; i++)
        {
            w.Entity(store.Entities[i]);
            write(w, store.Components[i]);
        }
    }

    // Set in saved order, so the dense arrays (and with them Query/Each order) come back as they were.
    public override void Read(SaveReader r, World world)
    {
        var store = world.Store<T>();
        var count = r.Count();
        for (var i = 0; i < count; i++)
        {
            var e = r.Entity();
            if (!world.IsAlive(e))
                throw new SaveException($"Corrupt save: {name} on dead {e}");
            r.CurrentEntity = e;
            store.Set(e, read(r));
        }
        r.CurrentEntity = Entity.Null;
    }
}

/// <summary>
/// One codec per component type, written by hand so the file format is explicit and a renamed field
/// can't silently change it. Saving a world that has a component with no codec here throws, so a
/// new component can't be forgotten (and <c>SaveLoadTests</c> checks every type in Components).
/// </summary>
internal static class ComponentCodecs
{
    public static readonly ComponentCodec[] All =
    [
        Codec("Thing", (w, t) =>
        {
            w.Def(t.Def);
            w.Def(t.Material);
            w.Int3(t.Cell);
            w.Float(t.Hp);
            w.Bool(t.Spawned);
            w.Int(t.Facing);
        }, r => new Thing
        {
            Def = r.Def<ThingDef>(), Material = r.DefOrNull<MaterialDef>(), Cell = r.Int3(), Hp = r.Float(), Spawned = r.Bool(),
            Facing = (byte)r.Int(), // facing came with format 3
        }),
        Codec("Stack", (w, s) => w.Int(s.Count), r => new Stack { Count = r.Int() }),
        Codec("Fueled", (w, f) =>
        {
            w.Float(f.Fuel);
            w.Def(f.Loaded);
        }, r => new Fueled { Fuel = r.Float(), Loaded = r.DefOrNull<ThingDef>()}), // the loaded fuel came with format 9
        Codec("PlantState", (w, p) => w.Float(p.RegrowTicksLeft), r => new PlantState { RegrowTicksLeft = r.Float() }),
        Codec("Construction", WriteConstruction, ReadConstruction),
        // Preset buildings (format 18).
        Codec("PresetBuilding", (w, b) =>
        {
            w.Def(b.Def);
            w.Int(b.Variant);
            w.Int3(b.Anchor);
            w.Int(b.Facing);
            w.Int((int)b.Phase);
            w.Bool(b.Demolishing);
            w.Int(b.Upgrades.Count);
            foreach (var u in b.Upgrades)
                w.String(u);
        }, r =>
        {
            var b = new PresetBuilding
            {
                Def = r.Def<BuildingDef>(), Variant = r.Int(), Anchor = r.Int3(), Facing = (byte)(r.Int() & 3),
                Phase = ReadPhase(r), Demolishing = r.Bool(),
            };
            for (var n = r.Count(100); n > 0; n--) // one the defs no longer have is dropped
                if (r.String() is var name && b.Def.Upgrades.Exists(u => u.Name == name))
                    b.Upgrades.Add(name);
            return b;
        }),
        Codec("PartOf", (w, p) => w.Entity(p.Building), r => new PartOf { Building = r.Entity() }),
        Codec("Workbench", WriteWorkbench, ReadWorkbench),
        Codec("Blown", (w, b) => w.Long(b.Until), r => new Blown { Until = r.Long() }),
        // Breath of the other elements (format 14).
        Codec("FoodPoisoned", (w, p) =>
        {
            w.Long(p.Until);
            w.Float(p.Severity);
        }, r => new FoodPoisoned { Until = r.Long(), Severity = r.Float() }),
        Codec("Crafted", (w, c) =>
        {
            w.Int((int)c.Quality);
            w.Entity(c.Maker);
        }, r => new Crafted { Quality = (Quality)r.Int(), Maker = r.Entity() }),
        Codec("RecipeBook", (w, b) =>
        {
            var known = b.Known.OrderBy(d => d.Index).ToList(); // a stable order, whatever the set's
            w.Int(known.Count);
            foreach (var d in known)
                w.Def(d);
        }, r =>
        {
            var b = new RecipeBook();
            var n = r.Count(10000);
            for (var i = 0; i < n; i++)
                b.Known.Add(r.Def<RecipeDef>());
            return b;
        }),
        Codec("PlayerCharacter", (w, p) => w.String(p.PlayerId.ToString("N")), r => new PlayerCharacter { PlayerId = Guid.ParseExact(r.String(), "N") }),
        Codec("Trade", (w, t) => w.Def(t.Def), r => new Trade { Def = r.Def<TradeDef>() }),
        Codec<Work.WorkSession>("WorkSession", Work.WorkSession.Write, Work.WorkSession.Read),
        Codec("WorkInput", (w, i) =>
        {
            w.Int(i.Step);
            w.Int((int)i.Kind);
            w.Int(i.Value);
        }, r => new Work.WorkInput(r.Int(), (Work.WorkInputKind)r.Int(), r.Int())),
        Codec("Rot", (w, e) => w.Float(e.Progress), r => new Rot { Progress = r.Float() }),
        Codec("Prisoner", (w, p) =>
        {
            w.Int((int)p.Was);
            w.Int((int)p.Mode);
            w.Float(p.Resistance);
            w.Long(p.LastVisit);
        }, r => new Prisoner { Was = (Faction)r.Int(), Mode = (PrisonerMode)r.Int(), Resistance = r.Float(), LastVisit = r.Long() }),
        Tag<CaptureOrder>("CaptureOrder"),
        Tag<HuntOrder>("HuntOrder"),
        Codec("Provoked", (w, p) =>
        {
            w.Entity(p.By);
            w.Long(p.Tick);
        }, r => new Provoked { By = r.Entity(), Tick = r.Long() }),
        Codec("Pressing", (w, p) =>
        {
            w.Entity(p.Target);
            w.Long(p.Since);
        }, r => new Combat.Pressing { Target = r.Entity(), Since = r.Long() }),
        Codec("Windup", (w, b) =>
        {
            w.Entity(b.Target);
            w.Int(b.Attack);
            w.Bool(b.Heavy);
            w.Long(b.Start);
            w.Long(b.Impact);
            w.Int((int)b.Response);
        }, r => new Combat.Windup
        {
            Target = r.Entity(), Attack = Math.Max(0, r.Int()), Heavy = r.Bool(), Start = r.Long(), Impact = r.Long(),
            Response = (Combat.BlowResponse)Math.Clamp(r.Int(), 0, 2),
        }),
        Codec("Guard", (w, g) =>
        {
            w.Float(g.Stamina);
            w.Long(g.RaisedAt);
            w.Long(g.LoweredAt);
            w.Long(g.LastRaise);
            w.Long(g.DodgeFrom);
            w.Long(g.DodgeUntil);
            w.Long(g.StaggeredUntil);
            w.Entity(g.RiposteOn);
            w.Long(g.RiposteUntil);
            w.Long(g.NextSwing);
        }, r => new Combat.Guard
        {
            Stamina = Math.Clamp(r.Float(), 0f, 1f), RaisedAt = r.Long(), LoweredAt = r.Long(), LastRaise = r.Long(),
            DodgeFrom = r.Long(), DodgeUntil = r.Long(), StaggeredUntil = r.Long(), RiposteOn = r.Entity(), RiposteUntil = r.Long(), NextSwing = r.Long(),
        }),
        Codec("Herd", (w, h) =>
        {
            w.Int(h.Id);
            w.Long(h.LeaveTick);
        }, r => new Herd { Id = r.Int(), LeaveTick = r.Long() }),
        Tag<ForPrisoners>("ForPrisoners"),
        Tag<NonViolent>("NonViolent"),
        Tag<Leaving>("Leaving"),
        Tag<Militia.TownArms>("TownArms"),
        Tag<Escaping>("Escaping"),
        Codec("Equipment", (w, e) => w.Entity(e.Weapon), r => new Equipment { Weapon = r.Entity() }),
        Codec("Apparel", (w, a) =>
        {
            w.Entity(a.Head);
            w.Entity(a.Body);
            w.Entity(a.Back);
            w.Entity(a.Neck);
            w.Entity(a.Tunic);
            w.Entity(a.Legs);
            w.Entity(a.Feet);
            w.Entity(a.Cloak);
        }, r => new Apparel
        {
            Head = r.Entity(), Body = r.Entity(),
            Back = r.Entity(), // packs came with format 6
            Neck = r.Entity(), // jewelry with format 9
            Tunic = r.Entity(), // clothes with format 12
            Legs = r.Entity(),
            Feet = r.Entity(),
            Cloak = r.Entity(),
        }),
        Codec("Outfit", (w, o) =>
        {
            w.Bool(o.BareHead);
            w.Bool(o.BareBody);
            w.Bool(o.Unarmed);
            w.Entity(o.Wanted);
            w.Bool(o.BareBack);
            w.Bool(o.BareNeck);
            w.Bool(o.BareTunic);
            w.Bool(o.BareLegs);
            w.Bool(o.BareFeet);
            w.Bool(o.BareCloak);
        }, r => new Outfit
        {
            BareHead = r.Bool(), BareBody = r.Bool(), Unarmed = r.Bool(), Wanted = r.Entity(),
            BareBack = r.Bool(), BareNeck = r.Bool(),
            BareTunic = r.Bool(), BareLegs = r.Bool(), // clothes with format 12
            BareFeet = r.Bool(), BareCloak = r.Bool(),
        }),
        Codec("Creature", (w, c) =>
        {
            w.Def(c.Def);
            w.String(c.Name);
            w.Int((int)c.Faction);
            w.Long(c.BornTick);
            w.Float(c.Hp);
            w.Bool(c.Dead);
            w.Bool(c.Downed);
        }, r => new Creature
        {
            Def = r.Def<CreatureDef>(), Name = r.String(), Faction = (Faction)r.Int(), BornTick = r.Long(),
            Hp = r.Float(), Dead = r.Bool(), Downed = r.Bool(),
        }),
        Codec("Health", WriteHealth, ReadHealth),
        Codec("Carried", (w, c) => w.Entity(c.By), r => new Carried { By = r.Entity() }),
        Codec("Perched", (w, p) => w.Int3(p.Cell), r => new Perched { Cell = r.Int3() }),
        Codec("Needs", WriteNeeds, ReadNeeds),
        Codec("Position", (w, p) =>
        {
            w.Float3(p.Pos);
            w.Int3(p.Cell);
        }, r => new Position { Pos = r.Float3(), Cell = r.Int3() }),
        // Saved stopped: paths belong to jobs, and jobs are dropped on load (see SaveGame).
        Codec("Mover", (w, m) => w.Int3(m.StepFrom), r => new Mover { StepFrom = r.Int3() }),
        Codec("Mind", WriteMind, ReadMind),
        Codec("Carrying", (w, c) => w.Entity(c.Item), r => new Carrying { Item = r.Entity() }),
        Codec("Migrating", (w, m) => w.Int3(m.To), r => new Migrating { To = r.Int3() }),
        Codec("Adventurer", (w, a) =>
        {
            w.Int(a.Party);
            w.Def(a.Kind);
            w.Def(a.PartyDef);
            w.Long(a.LeaveTick);
            w.Float(a.Satisfaction);
            w.Long(a.NextDrink);
            w.Int(a.Quest);
            w.Long(a.Arrived);
        }, r => new Adventurer { Party = r.Int(), Kind = r.Def<AdventurerKindDef>(), PartyDef = r.Def<PartyDef>(), LeaveTick = r.Long(), Satisfaction = r.Float(), NextDrink = r.Long(), Quest = r.Int(), Arrived = r.Long() }),
        Tag<Locked>("Locked"),
        Codec("Edge", (w, e) => w.Float(e.Dullness), r => new Edge { Dullness = r.Float() }),
        Codec("Entrusted", (w, e) => w.Entity(e.Owner), r => new Entrusted { Owner = r.Entity() }),
        Codec("Mana", (w, m) =>
        {
            w.Float(m.Current);
            w.Float(m.Max);
        }, r => new Mana { Current = r.Float(), Max = r.Float() }),
        Codec("Spellbook", (w, b) =>
        {
            var known = b.Known.OrderBy(d => d.Index).ToList(); // a stable order, whatever the set's
            w.Int(known.Count);
            foreach (var d in known)
                w.Def(d);
        }, r =>
        {
            var b = new Spellbook();
            var n = r.Count(1000);
            for (var i = 0; i < n; i++)
                b.Known.Add(r.Def<MendingDef>());
            return b;
        }),
        Codec("Charm", (w, c) =>
        {
            w.Def(c.Mending);
            w.Int(c.Extra);
            w.Float(c.Quality);
            w.Entity(c.Maker);
        }, r => new Charm { Mending = r.Def<MendingDef>(), Extra = Math.Clamp(r.Int(), 0, MendingDef.MaxRunes), Quality = r.Float(), Maker = r.Entity() }),
        Codec("Steadied", (w, s) =>
        {
            w.Float(s.Band);
            w.Long(s.Until);
        }, r => new Steadied { Band = r.Float(), Until = r.Long() }),
        Codec("LookedAs", (w, l) => w.Int(l.Id), r => new LookedAs { Id = r.Int() }),
        Codec("ReportPending", (w, p) =>
        {
            w.Int(p.Quest);
            w.Float(p.Danger);
            w.String(p.Text ?? "");
        }, r => new ReportPending { Quest = r.Int(), Danger = r.Float(), Text = r.String() }),
        Codec("Strongbox", (w, b) =>
        {
            w.Int(b.Pins);
            w.UInt(b.LockSeed);
            w.Bool(b.Locked);
            w.Byte((byte)b.Trap);
            w.Float(b.TrapAt);
            w.Bool(b.Armed);
            w.Int(b.Silver);
            w.Entity(b.SearchedBy);
            w.String(b.From ?? "");
        }, r =>
        {
            var b = new Strongbox { Pins = r.Int(), LockSeed = r.UInt(), Locked = r.Bool() };
            var trap = r.Byte();
            if (!Enum.IsDefined((TrapKind)trap) || b.Pins is < 1 or > Tinkering.Lockwork.MaxPins)
                throw new SaveException($"Corrupt save: strongbox trap {trap}, pins {b.Pins}");
            b.Trap = (TrapKind)trap;
            b.TrapAt = r.Float();
            b.Armed = r.Bool();
            b.Silver = r.Int();
            b.SearchedBy = r.Entity();
            b.From = r.String();
            return b;
        }),
        // The tinker's (Tinkering/).
        Codec("LockCut", (w, c) =>
        {
            w.UInt(c.Seed);
            w.Int(c.Pins);
            w.Float(c.Tightness);
            w.Entity(c.Maker);
        }, r => new Tinkering.LockCut
        {
            Seed = r.UInt(), Pins = Math.Clamp(r.Int(), Tinkering.Lockwork.MinPins, Tinkering.Lockwork.MaxPins), Tightness = Math.Clamp(r.Float(), 0f, 1f), Maker = r.Entity(),
        }),
        Codec("Key", (w, k) =>
        {
            w.UInt(k.Seed);
            w.Int(k.Pins);
            w.Float(k.Fit);
        }, r => new Tinkering.Key { Seed = r.UInt(), Pins = Math.Clamp(r.Int(), Tinkering.Lockwork.MinPins, Tinkering.Lockwork.MaxPins), Fit = Math.Clamp(r.Float(), 0f, 1f) }),
        Codec("Relock", (w, x) => w.Entity(x.By), r => new Tinkering.Relock { By = r.Entity() }),
        Codec("Contraption", (w, c) =>
        {
            w.Bool(c.Binding);
            w.Entity(c.Maker);
        }, r => new Tinkering.Contraption { Binding = r.Bool(), Maker = r.Entity() }),
        Codec("PartyWagon", (w, g) =>
        {
            w.Int(g.Party);
            w.Entity(g.Owner);
            w.Byte((byte)g.Damage);
            w.UInt(g.WheelSeed);
            w.Entity(g.MendedBy);
        }, r => new Tinkering.Wagon
        {
            Party = r.Int(), Owner = r.Entity(), Damage = (Tinkering.WagonDamage)(r.Byte() & 3), WheelSeed = r.UInt(), MendedBy = r.Entity(),
        }),
        Codec("Curio", (w, c) =>
        {
            w.Int(c.Worth);
            w.Int(c.Face);
            w.Bool(c.Genuine);
            w.UInt(c.Seed);
        }, r => new Tinkering.Curio { Worth = Math.Max(0, r.Int()), Face = Math.Max(0, r.Int()), Genuine = r.Bool(), Seed = r.UInt() }),
        Codec("Appraisal", (w, a) =>
        {
            w.Entity(a.By);
            w.Int(a.Low);
            w.Int(a.High);
            w.Bool(a.Doubts);
            w.Int(a.Quoted);
        }, r => new Tinkering.Appraisal { By = r.Entity(), Low = r.Int(), High = r.Int(), Doubts = r.Bool(), Quoted = r.Int() }),
        Codec("Valued", (w, v) =>
        {
            w.Int(v.Worth);
            w.Entity(v.ToldBy);
        }, r => new Tinkering.Valued { Worth = r.Int(), ToldBy = r.Entity() }),
        Codec("CaskDrawn", (w, c) => w.Int(c.Drawn), r => new CaskDrawn { Drawn = r.Int() }),
        Codec("FiredForMending", (w, f) => w.Long(f.Until), r => new FiredForMending { Until = r.Long() }),
        Codec("WorkPiece", (w, p) =>
        {
            w.Entity(p.Forge);
            w.Def(p.Recipe);
            w.Entity(p.Mending);
            w.Float(p.Temp);
            w.Float(p.Shape);
            w.Int(p.BlowsNeeded);
            w.Float(p.BlowSum);
            w.Int(p.BlowCount);
            w.Int(p.BurnSteps);
            w.Int((int)p.Flaw);
            w.Bool(p.IsBloom);
            w.Float(p.Reduced);
            w.Float(p.Slag);
        }, r => new Work.WorkPiece
        {
            Forge = r.Entity(), Recipe = r.Def<RecipeDef>(), Mending = r.Entity(), Temp = r.Float(), Shape = r.Float(),
            BlowsNeeded = r.Int(), BlowSum = r.Float(), BlowCount = r.Int(), BurnSteps = r.Int(), Flaw = (Work.PieceFlaw)r.Int(),
            // From format 13: blooms.
            IsBloom = r.Version >= 13 && r.Bool(), Reduced = r.Version >= 13 ? Math.Clamp(r.Float(), 0f, 1f) : 0f, Slag = r.Version >= 13 ? Math.Clamp(r.Float(), 0f, 1f) : 0f,
        }),
        Codec("StewPot", (w, p) =>
        {
            w.Int(p.In.Count);
            foreach (var i in p.In)
            {
                w.Def(i.Def);
                w.Int(i.Count);
            }
            w.Float(p.Nutrition);
            w.Float(p.Heat);
            w.Float(p.Flame);
            w.Float(p.Doneness);
            w.Float(p.Scorch);
            w.Float(p.Seasoning);
            w.Float(p.SeasonTarget);
            w.Float(p.Rot);
            w.Int(p.Ladled);
            w.Long(p.TendedUntil);
        }, r =>
        {
            var p = new Work.StewPot();
            for (var n = r.Count(1000); n > 0; n--)
                p.In.Add(new Work.PotIngredient(r.Def<ThingDef>(), r.Int()));
            p.Nutrition = r.Float();
            p.Heat = r.Float();
            p.Flame = r.Float();
            p.Doneness = r.Float();
            p.Scorch = r.Float();
            p.Seasoning = r.Float();
            p.SeasonTarget = r.Float();
            p.Rot = r.Float();
            p.Ladled = r.Int();
            p.TendedUntil = r.Long();
            return p;
        }),
        Codec("Flawed", (w, f) => w.Int((int)f.Flaw), r => new Work.Flawed { Flaw = (Work.PieceFlaw)r.Int() }),
        Codec("Stored", (w, s) => w.Entity(s.In), r => new Stored { In = r.Entity() }),
        Codec("Request", (w, q) =>
        {
            w.Int((int)q.Kind);
            w.Entity(q.Target);
            w.Entity(q.From);
        }, r => new Request { Kind = (RequestKind)r.Int(), Target = r.Entity(), From = r.Entity() }),
        Codec("Appearance", (w, a) =>
        {
            w.Int((int)a.Body);
            w.Int((int)a.Hair);
            w.Int((int)a.HairColor);
            w.Int((int)a.EyeColor);
            w.Int((int)a.SkinColor);
            w.Int((int)a.Beard);
            w.Int((int)a.Eyes);
            w.Int((int)a.Stance);
            w.NullableString(a.Model);
            w.Int((int)a.Folk);
        }, r => new Appearance
        {
            Body = (BodyType)r.Int(), Hair = (HairStyle)r.Int(), HairColor = (uint)r.Int(), EyeColor = (uint)r.Int(), SkinColor = (uint)r.Int(),
            Beard = (BeardStyle)r.Int(), // beards, eye styles and 3D models came over from DragonWorld
            Eyes = (EyeStyle)r.Int(),
            Stance = (Stance)r.Int(),
            Model = Appearance.ValidModel(r.NullableString()),
            Folk = r.Version >= 12 && (Folk)r.Int() is var folk && Enum.IsDefined(folk) ? folk : Folk.Human, // the folk came with format 12
        }),
        Codec("AwaitingSettler", (w, a) => w.Long(a.ArriveTick), r => new AwaitingSettler { ArriveTick = r.Long() }),
        Codec("Avatar", (w, a) =>
        {
            w.Int(a.MoveDir.X);
            w.Int(a.MoveDir.Y);
            w.Bool(a.WorkHeld);
            w.Int(a.Facing.X);
            w.Int(a.Facing.Y);
            w.Int((int)a.Chain);
            w.Bool(a.HasPending);
            w.Int(a.PendingDir.X);
            w.Int(a.PendingDir.Y);
            w.Int3(a.PendingAt);
        }, r =>
        {
            var a = new Avatar
            {
                MoveDir = new int2(r.Int(), r.Int()), WorkHeld = r.Bool(), Facing = new int2(r.Int(), r.Int()),
                Chain = (Systems.AvatarActionKind)r.Int(), // chaining came with format 7
            };
            a.HasPending = r.Bool();
            a.PendingDir = new int2(r.Int(), r.Int());
            a.PendingAt = r.Int3();
            return a;
        }),
        Codec("BedOwner", (w, b) => w.Entity(b.Bed), r => new BedOwner { Bed = r.Entity() }),
        Codec("LodgeStay", (w, s) =>
        {
            w.Entity(s.Bed);
            w.Int(s.Night);
        }, r => new Hospitality.LodgeStay { Bed = r.Entity(), Night = r.Int() }),
        Tag<Hospitality.Unmade>("Unmade"),
        Codec("KnowsTheWay", (w, k) =>
        {
            w.Int(k.Shops.Count);
            foreach (var s in k.Shops)
                w.Entity(s);
        }, r =>
        {
            var k = new Hospitality.KnowsTheWay();
            for (var n = r.Count(1 << 12); n > 0; n--)
                k.Shops.Add(r.Entity());
            return k;
        }),
        Codec("Tipsy", (w, t) => w.Float(t.Level), r => new Hospitality.Tipsy { Level = r.Float() }),
        Codec("Savouring", (w, s) =>
        {
            w.Entity(s.Item);
            w.Long(s.Since);
            w.Long(s.Until);
            w.Bool(s.Meal);
            w.Int(s.Count);
            w.Float(s.Rot);
            w.Int(s.Talks);
        }, r => new Hospitality.Savouring { Item = r.Entity(), Since = r.Long(), Until = r.Long(), Meal = r.Bool(), Count = r.Int(), Rot = r.Float(), Talks = r.Int() }),
        Codec("DrinkMix", (w, m) =>
        {
            w.Float(m.Strength);
            w.Float(m.Warming);
            w.Float(m.Rousing);
            w.Float(m.Numbing);
            w.Float(m.Loosening);
            w.String(m.Adds ?? "");
        }, r => new Work.DrinkMix { Strength = r.Float(), Warming = r.Float(), Rousing = r.Float(), Numbing = r.Float(), Loosening = r.Float(), Adds = r.String() }),
        Codec("Skills", WriteSkills, ReadSkills),
        Codec("ShopPrices", WriteShopPrices, ReadShopPrices),
        Codec("ShopName", (w, n) => w.String(n.Name), r => new Shops.ShopName { Name = Shops.ShopName.Clean(r.String()) }),
        Codec("ForSale", (w, f) => { w.Entity(f.Seller); w.Int(f.Price); }, r => new Shops.ForSale { Seller = r.Entity(), Price = r.Int() }),
        Codec("Mood", WriteMood, ReadMood),
        Codec("Traits", (w, t) =>
        {
            w.Int(t.List.Count);
            foreach (var def in t.List)
                w.Def(def);
        }, r =>
        {
            var traits = new Traits();
            for (int i = 0, n = r.Count(100); i < n; i++)
                traits.List.Add(r.Def<TraitDef>());
            return traits;
        }),
    ];

    private static readonly Dictionary<string, ComponentCodec> ByName = All.ToDictionary(c => c.Name);
    private static readonly Dictionary<Type, ComponentCodec> ByType = All.ToDictionary(c => c.Type);

    public static bool Has(Type type) => ByType.ContainsKey(type);

    public static ComponentCodec? For(Type type) => ByType.GetValueOrDefault(type);

    /// <summary>A building's phase; format 1 had no upper storey phases (Roof was 2).</summary>
    private static BuildPhase ReadPhase(SaveReader r)
    {
        var n = r.Int();
        if (r.Version < 2 && n >= 2)
            n += BuildPhase.Roof - BuildPhase.UpperFloor;
        return (BuildPhase)Math.Clamp(n, 0, (int)BuildPhase.Done);
    }

    private static ComponentCodec<T> Codec<T>(string name, Action<SaveWriter, T> write, Func<SaveReader, T> read) => new(name, write, read);

    private static ComponentCodec<T> Tag<T>(string name) where T : struct => new(name, (_, _) => { }, _ => default);

    public static void WriteAll(SaveWriter w, World world)
    {
        foreach (var store in world.Stores)
            if (store.Count > 0 && !ByType.ContainsKey(store.ComponentType))
                throw new SaveException($"No save codec for component {store.ComponentType.Name} (add one in ComponentCodecs)");
        var present = All.Where(c => c.Count(world) > 0).ToList();
        w.Int(present.Count);
        foreach (var codec in present)
        {
            w.String(codec.Name);
            codec.Write(w, world);
        }
    }

    public static void ReadAll(SaveReader r, World world)
    {
        var n = r.Count(1000);
        for (var i = 0; i < n; i++)
        {
            var name = r.String();
            if (!ByName.TryGetValue(name, out var codec))
                throw new SaveException($"The save has '{name}' components, which this version of the game doesn't know.");
            codec.Read(r, world);
        }
    }

    // Enum-indexed arrays go by name, so adding or reordering a work type or skill keeps old saves loading.
    private static void WriteByName<TEnum>(SaveWriter w, int[] values) where TEnum : struct, Enum
    {
        var names = Enum.GetNames<TEnum>();
        w.Int(names.Length);
        for (var i = 0; i < names.Length; i++)
        {
            w.String(names[i]);
            w.Int(values[i]);
        }
    }

    private static void ReadByName<TEnum>(SaveReader r, int[] into) where TEnum : struct, Enum
    {
        var n = r.Count(1000);
        for (var i = 0; i < n; i++)
        {
            var name = r.String();
            var value = r.Int();
            if (Enum.TryParse<TEnum>(name, out var e) && Convert.ToInt32(e) is var k && k >= 0 && k < into.Length)
                into[k] = value;
        }
    }

    // Delivered counts go with the cost entry they were for (format 2), so a load against defs whose
    // cost list changed (def hot reload, a new game version) puts them back on the right material.
    private static void WriteConstruction(SaveWriter w, Construction c)
    {
        w.Def(c.Target);
        var cost = c.Target.Building!.Cost;
        w.Int(cost.Count);
        for (var i = 0; i < cost.Count; i++)
        {
            w.Def(cost[i].ThingDef);
            w.Def(cost[i].MaterialDef);
            w.Int(c.Delivered[i]);
        }
        w.Float(c.WorkDone);
        w.Int(c.Materials.Count);
        foreach (var (m, n) in c.Materials)
        {
            w.Def(m);
            w.Int(n);
        }
    }

    private static Construction ReadConstruction(SaveReader r)
    {
        var target = r.Def<ThingDef>();
        var cost = target.Building?.Cost ?? throw new SaveException($"The save has a {target.Name} being built, but it isn't a building any more.");
        var delivered = new int[cost.Count];
        var slots = r.Count(1000);
        var used = new bool[cost.Count];
        for (var i = 0; i < slots; i++)
        {
            var thing = r.Def<ThingDef>();
            var material = r.DefOrNull<MaterialDef>();
            var count = r.Int();
            // The first unclaimed cost entry for the same thing and material; dropped if none is left.
            var k = cost.FindIndex(e => e.ThingDef == thing && e.MaterialDef == material);
            while (k >= 0 && used[k])
                k = cost.FindIndex(k + 1, e => e.ThingDef == thing && e.MaterialDef == material);
            if (k < 0)
                continue;
            used[k] = true;
            delivered[k] = count;
        }
        var c = new Construction { Target = target, Delivered = delivered, WorkDone = r.Float() };
        var n = r.Count(1000);
        for (var i = 0; i < n; i++)
            c.Materials.Add((r.Def<MaterialDef>(), r.Int()));
        return c;
    }

    // Bills by recipe name; what's been brought goes with the ingredient entry it was for (as for
    // construction), so a recipe edited by a def reload keeps what still fits.
    private static void WriteWorkbench(SaveWriter w, Workbench b)
    {
        w.Int(b.Bills.Count);
        foreach (var bill in b.Bills)
        {
            w.Def(bill.Recipe);
            w.Int((int)bill.Mode);
            w.Int(bill.Target);
            w.Int(bill.Done);
            w.Bool(bill.Suspended);
            w.Def(bill.Material);
        }
        w.Def(b.Active);
        w.Entity(b.By);
        var ingredients = b.Active?.Ingredients ?? [];
        w.Int(ingredients.Count);
        for (var i = 0; i < ingredients.Count; i++)
        {
            w.Def(ingredients[i].ThingDef);
            w.Def(ingredients[i].MaterialDef);
            w.Int(b.Staged[i]);
        }
        w.Int(b.Materials.Count);
        foreach (var (m, n) in b.Materials)
        {
            w.Def(m);
            w.Int(n);
        }
        w.Float(b.Rot);
        w.Int(b.RotUnits);
        w.Float(b.Progress);
    }

    private static Workbench ReadWorkbench(SaveReader r)
    {
        var b = new Workbench();
        var bills = r.Count(1000);
        for (var i = 0; i < bills; i++)
            b.Bills.Add(new Bill
            {
                Recipe = r.Def<RecipeDef>(), Mode = (BillMode)r.Int(), Target = r.Int(), Done = r.Int(), Suspended = r.Bool(),
                Material = r.DefOrNull<MaterialDef>(),
            });
        var active = r.DefOrNull<RecipeDef>();
        var by = r.Entity();
        if (active != null)
            b.Begin(active, by);
        var slots = r.Count(1000);
        var used = new bool[b.Staged.Length];
        for (var i = 0; i < slots; i++)
        {
            var thing = r.Def<ThingDef>();
            var material = r.DefOrNull<MaterialDef>();
            var count = r.Int();
            if (active == null)
                continue;
            var k = active.Ingredients.FindIndex(e => e.ThingDef == thing && e.MaterialDef == material);
            while (k >= 0 && used[k])
                k = active.Ingredients.FindIndex(k + 1, e => e.ThingDef == thing && e.MaterialDef == material);
            if (k < 0)
                continue;
            used[k] = true;
            b.Staged[k] = Math.Min(count, active.Ingredients[k].Count);
        }
        var n = r.Count(1000);
        for (var i = 0; i < n; i++)
            b.Materials.Add((r.Def<MaterialDef>(), r.Int()));
        b.Rot = r.Float();
        b.RotUnits = r.Int();
        b.Progress = r.Float();
        return b;
    }

    private static void WriteHealth(SaveWriter w, Health h)
    {
        w.Def(h.Body);
        w.Float(h.Scale);
        w.Float(h.Blood);
        w.Int(h.Injuries.Count);
        foreach (var inj in h.Injuries)
        {
            w.Int(inj.Part);
            w.Int((int)inj.Type);
            w.Float(inj.Damage);
            w.Float(inj.PeakDamage);
            w.Bool(inj.Tended);
            w.Float(inj.Quality);
            w.Long(inj.TendedTick);
            w.Long(inj.Tick);
            w.Bool(inj.InfectionRolled);
            w.Bool(inj.Absorbed);
            w.Entity(inj.FeltBy);
            w.Bool(inj.FeltFestering);
            w.Bool(inj.Infection != null);
            if (inj.Infection is { } inf)
            {
                w.Float(inf.Severity);
                w.Float(inf.Immunity);
                w.Float(inf.ImmunityRate);
            }
        }
        w.Int(h.Scars.Count);
        foreach (var s in h.Scars)
        {
            w.Int(s.Part);
            w.Int((int)s.Type);
            w.Float(s.Damage);
        }
        var (capacities, pain) = h.Cache;
        w.Float(pain);
        w.Bool(capacities != null);
        if (capacities != null)
            w.Ints(System.Runtime.InteropServices.MemoryMarshal.Cast<float, int>(capacities));
    }

    private static Health ReadHealth(SaveReader r)
    {
        var h = new Health { Body = r.Def<BodyDef>(), Scale = r.Float(), Blood = r.Float() };
        var injuries = r.Count(10_000);
        for (var i = 0; i < injuries; i++)
        {
            var inj = new Injury
            {
                Part = r.Int(), Type = (DamageType)r.Int(), Damage = r.Float(), PeakDamage = r.Float(), Tended = r.Bool(),
                Quality = r.Float(), TendedTick = r.Long(), Tick = r.Long(), InfectionRolled = r.Bool(), Absorbed = r.Bool(),
            };
            if (r.Version >= 4)
            {
                inj.FeltBy = r.Entity();
                inj.FeltFestering = r.Bool();
            }
            if (r.Bool())
                inj.Infection = new Infection { Severity = r.Float(), Immunity = r.Float(), ImmunityRate = r.Float() };
            if (inj.Part < 0 || inj.Part >= h.Body.Parts.Count)
                throw new SaveException($"Corrupt save: injury on part {inj.Part} of a {h.Body.Name}");
            h.Injuries.Add(inj);
        }
        var scars = r.Count(10_000);
        for (var i = 0; i < scars; i++)
            h.Scars.Add(new Scar(r.Int(), (DamageType)r.Int(), r.Float()));
        var pain = r.Float();
        var capacities = r.Bool() ? System.Runtime.InteropServices.MemoryMarshal.Cast<int, float>(r.Ints()).ToArray() : null;
        if (capacities != null && capacities.Length != Enum.GetValues<Capacity>().Length)
            capacities = null; // capacities were added or removed: recompute
        h.Cache = (capacities, pain);
        return h;
    }

    // Need values by def name, all of them: StateHash mixes in the unused slots too.
    private static void WriteNeeds(SaveWriter w, Needs n)
    {
        w.Int(n.Values.Length);
        for (var i = 0; i < n.Values.Length; i++)
        {
            w.Def(w.Defs.AllNeeds.Find(d => d.Index == i));
            w.Bool(n.Has[i]);
            w.Float(n.Values[i]);
        }
    }

    private static Needs ReadNeeds(SaveReader r)
    {
        var count = r.Defs.AllNeeds.Count;
        var needs = new Needs { Values = new float[count], Has = new bool[count] };
        var n = r.Count(1000);
        for (var i = 0; i < n; i++)
        {
            var def = r.DefOrNull<NeedDef>();
            var has = r.Bool();
            var value = r.Float();
            if (def != null && def.Index < count)
            {
                needs.Has[def.Index] = has;
                needs.Values[def.Index] = value;
            }
        }
        return needs;
    }

    // Value and memories; the target and situational list are rebuilt on the next update.
    private static void WriteMood(SaveWriter w, Mood m)
    {
        w.Float(m.Value);
        w.Float(m.Target);
        w.Int(m.Memories.Count);
        foreach (var memory in m.Memories)
        {
            w.Def(memory.Def);
            w.Int(memory.TicksLeft);
            w.Entity(memory.Other);
        }
    }

    private static Mood ReadMood(SaveReader r)
    {
        var mood = new Mood { Value = r.Float(), Target = r.Float() };
        var n = r.Count(10000);
        for (var i = 0; i < n; i++)
            mood.Memories.Add(new Memory { Def = r.Def<ThoughtDef>(), TicksLeft = r.Int(), Other = r.Entity() });
        return mood;
    }

    private static void WriteMind(SaveWriter w, Mind m)
    {
        w.Bool(m.Job != null);
        w.Long(m.NextThinkTick);
        w.Bool(m.Asleep);
        w.Bool(m.Drafted);
        WriteByName<WorkType>(w, m.WorkPriority);
    }

    private static Mind ReadMind(SaveReader r)
    {
        var hadJob = r.Bool();
        var m = new Mind { NextThinkTick = r.Long(), Asleep = r.Bool(), Drafted = r.Bool() };
        ReadByName<WorkType>(r, m.WorkPriority);
        if (hadJob)
            r.PawnsWithJobs.Add(r.CurrentEntity);
        return m;
    }

    private static void WriteShopPrices(SaveWriter w, Shops.ShopPrices p)
    {
        WriteByName<ServiceKind>(w, p.Price);
        WriteByName<ServiceKind>(w, p.Offered.Select(o => o ? 1 : 0).ToArray());
    }

    // A service added since the save keeps its default price, offered.
    private static Shops.ShopPrices ReadShopPrices(SaveReader r)
    {
        var p = Shops.ShopPrices.Defaults(r.Defs);
        ReadByName<ServiceKind>(r, p.Price);
        var offered = p.Offered.Select(o => o ? 1 : 0).ToArray();
        ReadByName<ServiceKind>(r, offered);
        for (var i = 0; i < offered.Length; i++)
            p.Offered[i] = offered[i] != 0;
        return p;
    }

    private static void WriteSkills(SaveWriter w, Skills s)
    {
        WriteByName<Skill>(w, s.Level);
        var names = Enum.GetNames<Skill>();
        w.Int(names.Length);
        for (var i = 0; i < names.Length; i++)
        {
            w.String(names[i]);
            w.Float(s.Xp[i]);
        }
    }

    private static Skills ReadSkills(SaveReader r)
    {
        var s = new Skills();
        Array.Fill(s.Level, -1); // a skill the save lacks (added since) is rolled after loading (SaveGame)
        ReadByName<Skill>(r, s.Level);
        var n = r.Count(1000);
        for (var i = 0; i < n; i++)
        {
            var name = r.String();
            var xp = r.Float();
            if (Enum.TryParse<Skill>(name, out var k))
                s.Xp[(int)k] = xp;
        }
        return s;
    }
}

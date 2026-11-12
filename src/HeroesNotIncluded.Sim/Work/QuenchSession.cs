using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Defs;
using HeroesNotIncluded.Sim.Saving;
using HeroesNotIncluded.Sim.Systems;

namespace HeroesNotIncluded.Sim.Work;

/// <summary>
/// Hardening a shaped piece in the quench tub (<see cref="Smithing"/>). Hold to plunge it and keep
/// it under; let go to lift it out, which ends the quench. It roars and boils, then the steam
/// thins to a hiss as it cools: lifted while it still hisses, it's hard and keeps some spring.
/// Too soon and it never hardens (soft); too long and it's glass-hard (brittle). Neither shows.
/// How fast a piece cools varies a little with each one, so it's read from the steam, not counted.
/// </summary>
public sealed class QuenchSession : WorkSession
{
    /// <summary>Water's heat: what the piece cools toward under it.</summary>
    public const float WaterTemp = 20f;

    /// <summary>The heat to lift it at, before skill widens it: the steam's last hiss.</summary>
    public const float LiftLo = 150f, LiftHi = 300f;

    /// <summary>Lifted this far above or below the window, the piece is flawed.</summary>
    public const float SoftPast = 150f, BrittlePast = 60f;

    /// <summary>Left under until it's this cold, it's lifted for you.</summary>
    public const float Cold = WaterTemp + 25f;

    /// <summary>The tub.</summary>
    public Entity Station { get; set; }

    public RecipeDef Recipe { get; set; } = null!;
    public string ItemLabel { get; set; } = "";

    public float Temp { get; private set; }

    /// <summary>Steps for the piece to lose most of its heat under water: bigger pieces hold it longer.</summary>
    public float TauSteps { get; private set; }

    /// <summary>The air, for a piece held over the tub before it goes in.</summary>
    public float Air { get; private set; }

    /// <summary>It's been under the water.</summary>
    public bool Dunked { get; private set; }

    /// <summary>The heat it came out at, once lifted.</summary>
    public float LiftedAt { get; private set; }

    public PieceFlaw Flaw { get; private set; }

    /// <summary>The window to lift it in, widened by a practised hand.</summary>
    public float Lo => LiftLo - 60f * (Hands.Tolerance - 0.75f);
    public float Hi => LiftHi + 120f * (Hands.Tolerance - 0.75f);

    /// <summary>How much steam is coming off, 0..1: what the smith watches.</summary>
    public float Steam => Dunked && Held ? Math.Clamp((Temp - 90f) / 600f, 0f, 1f) : 0f;

    public override int3? Focus(Simulation sim) => sim.World.IsAlive(Station) && sim.World.TryGet<Thing>(Station, out var t) ? t.Cell : null;

    public override Skill Skill => Skill.Crafting;
    public override float Xp => 20f;
    public override string Title => $"Quenching the {ItemLabel}";

    internal static QuenchSession For(Simulation sim, Entity smith, Entity tub, WorkPiece p)
    {
        var mass = p.Recipe.Products.Count > 0 ? p.Recipe.Products[0].ThingDef.Mass : 1f;
        return new QuenchSession
        {
            Station = tub, Recipe = p.Recipe, ItemLabel = Smithing.PieceLabel(sim, p), Temp = p.Temp, Flaw = p.Flaw,
            TauSteps = (0.9f + 0.5f * Math.Clamp(mass, 0f, 4f) / 4f) * SimClock.TicksPerSecond,
            Air = Smithing.AirAt(sim, smith),
        };
    }

    public override bool StillValid(Simulation sim)
    {
        var w = sim.World;
        return w.IsAlive(Station) && AvatarSystem.InReach(sim, w.Get<Position>(Worker).Cell, Station) && Smithing.PieceOf(sim, Worker) != null;
    }

    // Each piece cools at its own pace (a fifth either way), so the moment comes from the steam.
    protected internal override void Begin() => TauSteps *= Rand.Range(0.8f, 1.2f);

    protected override void Step()
    {
        if (Held)
        {
            Dunked = true;
            Temp = WaterTemp + (Temp - WaterTemp) * (1f - 1f / TauSteps);
            if (Temp <= Cold)
                Lift();
        }
        else if (Dunked)
            Lift();
        else
            Temp = Air + (Temp - Air) * Smithing.AirCooling;
    }

    private void Lift()
    {
        LiftedAt = Temp;
        float q;
        if (Temp > Hi)
            q = 1f - (Temp - Hi) / 250f;
        else if (Temp < Lo)
            q = 1f - (Lo - Temp) / (Lo - WaterTemp);
        else
            q = 1f;
        Sample(q);
        if (Flaw == PieceFlaw.None)
            Flaw = Temp > Hi + SoftPast ? PieceFlaw.Soft : Temp < Lo - BrittlePast ? PieceFlaw.Brittle : PieceFlaw.None;
        Done = true;
    }

    public override void Finish(Simulation sim)
    {
        if (Smithing.PieceOf(sim, Worker) is not { } p)
            return;
        p.Temp = LiftedAt;
        p.Flaw = Flaw;
        Smithing.Complete(sim, Worker, p, Quality);
    }

    public override void Interrupted(Simulation sim)
    {
        if (Smithing.PieceOf(sim, Worker) is { } p)
            p.Temp = Temp;
    }

    protected override void WriteState(SaveWriter w)
    {
        w.Entity(Station);
        w.Def(Recipe);
        w.String(ItemLabel);
        w.Float(Temp);
        w.Float(TauSteps);
        w.Float(Air);
        w.Bool(Dunked);
        w.Float(LiftedAt);
        w.Int((int)Flaw);
    }

    protected override void ReadState(SaveReader r)
    {
        Station = r.Entity();
        Recipe = r.Def<RecipeDef>();
        ItemLabel = r.String();
        Temp = r.Float();
        TauSteps = r.Float();
        Air = r.Float();
        Dunked = r.Bool();
        LiftedAt = r.Float();
        Flaw = (PieceFlaw)r.Int();
    }
}

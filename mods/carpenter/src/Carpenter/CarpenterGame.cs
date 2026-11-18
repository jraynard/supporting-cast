using Brutal.ImGuiApi;
using HeroesNotIncluded.Audio;
using HeroesNotIncluded.Mods;
using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Carpentry;
using HeroesNotIncluded.Sim.Ecs;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Tinkering;
using HeroesNotIncluded.Sim.Work;
using HeroesNotIncluded.Ui;
using Narsil.Ui;

namespace HeroesNotIncluded.Carpentry;

/// <summary>
/// The carpenter's game half (docs/MODDING.md): how its mini-games look and sound, its trees coming
/// down, its bench's Rebuild tab and a wagon's woodwork, registered as a mod registers them.
/// </summary>
public sealed class CarpenterGame : IGameMod
{
    public void Register(GameRegistry r)
    {
        var views = new CarpenterViews();
        r.WorkView<SawSession>((v, s) => views.With(v).DrawSaw(s));
        r.WorkView<FletchSession>((v, s) => views.With(v).DrawFletch(s));
        r.WorkView<BowSession>((v, s) => views.With(v).DrawBow(s));
        r.WorkView<JoinSession>((v, s) => views.With(v).DrawJoin(s));
        r.WorkView<PitSession>((v, s) => views.With(v).DrawPit(s));
        r.WorkView<LiftSession>((v, s) => views.With(v).DrawLift(s));
        r.WorkView<FellSession>((v, s) => views.With(v).DrawFell(s));
        r.WorkView<BuckSession>((v, s) => views.With(v).DrawBuck(s));
        r.WorkView<CrosscutSession>((v, s) => views.With(v).DrawCrosscut(s));
        r.WorkView<SplitSession>((v, s) => views.With(v).DrawSplit(s));
        r.WorkView<NailSession>((v, s) => views.With(v).DrawNail(s));
        r.AimLine<FellSession>((v, _) => views.With(v).FellLine());
        r.WorkSound<SawSession>(CarpenterSounds.Update);
        r.WorkSound<FellSession>(CarpenterSounds.Update);
        r.WorkSound<BuckSession>(CarpenterSounds.Update);
        r.WorkSound<CrosscutSession>(CarpenterSounds.Update);
        r.WorkSound<SplitSession>(CarpenterSounds.Update);
        r.WorkSound<NailSession>(CarpenterSounds.Update);
        r.WorkSound<FletchSession>(CarpenterSounds.Update);
        r.WorkSound<JoinSession>(CarpenterSounds.Update);
        r.WorkSound<BowSession>(CarpenterSounds.Update);
        r.Tilt(Toppling);
        r.WorldSounds(() => new TreesFalling());
        r.StationTab("Rebuild", (v, station) => Carpenters.IsBench(v.Sim, station) && v.Perceived.Losses().Any(), DrawRebuild,
            "Carry it over in your arms, and ~F~ where the old one stood to set it down. The town pays for it.");
        r.MendHint(MendHint);
        r.WagonPanel(DrawWagon);
    }

    /// <summary>
    /// A tree whose hinge has given (<see cref="Falling"/>): it creaks over a few degrees toward its fall,
    /// then comes down faster and faster, pivoting on its stump, flat as it lands.
    /// </summary>
    private static Tilt? Toppling(Simulation sim, Entity e, long tick)
    {
        if (!sim.World.TryGet<Falling>(e, out var falling))
            return null;
        var t = Math.Clamp((tick - falling.At) / (float)Felling.CreakTicks, 0f, 1f);
        const float creak = 0.4f, lean = 0.09f;
        var angle = t < creak ? lean * t / creak : lean + (MathF.PI / 2f - lean) * MathF.Pow((t - creak) / (1f - creak), 2f);
        return new Tilt(falling.Dir, angle);
    }

    private static string Cap(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];

    /// <summary>The Rebuild tab (the carpenter's bench): each piece the fire took, and a button to join a new one.</summary>
    private static void DrawRebuild(WorkView v, Entity station)
    {
        var losses = v.Perceived.Losses().ToList();
        var size = v.ButtonSize;
        ImGuiText.Colored(Theme.Dim, "Burned in the fire, for the town to have back:");
        foreach (var l in losses)
        {
            ImGui.PushID(l.Id);
            var why = Refurnishing.MakeProblem(v.Sim, v.Me, l.Piece);
            ImGui.BeginDisabled(why != null || v.Working);
            if (ImGui.Button("Make"u8, in size))
                v.Issue(new MakeFurnitureCommand(v.Me, station, l.Piece));
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                v.Tip(why != null ? Cap(why) + "." : Controls.Keys("For each joint: hold ~F~ to raise the mallet, let go to strike. Chop the mortise to the mark (~Space~ when it's deep enough), then drive the tenon home: lighter blows as it tightens, or it splits."));
            ImGui.SameLine(0, Gui.S(8));
            ImGuiText.Wrapped($"A {l.Label}{(l.Where.Length > 0 ? $" in the {l.Where}" : "")} ({l.Planks} plank{(l.Planks == 1 ? "" : "s")})");
            ImGui.PopID();
        }
    }

    private static string? MendHint(WorkView v, Entity station, Entity item) =>
        !Carpenters.AppliesToRepair(v.Sim, v.Me, station, item) ? null
        : Carpenters.IsBow(v.Sim.World.Get<Sim.Components.Thing>(item).Def)
            ? "Put it on the tillering stick: scrape back the limbs where they've set stiff, then string it again."
            : "Cut the split in the haft clean with the chisel, then drive a peg through it.";

    /// <summary>A wagon's woodwork, the carpenter's: a new axle hewn, a plank nailed across a sprung board.</summary>
    private static void DrawWagon(WorkView v, Entity wagonAt)
    {
        if (!v.Sim.World.TryGet<Wagon>(wagonAt, out var wagon))
            return;
        if ((wagon.Damage & WagonDamage.Axle) != 0)
        {
            var why = Wagons.AxleProblem(v.Sim, v.Me, wagonAt);
            ImGui.BeginDisabled(why != null || v.Working);
            if (ImGui.Button($"Hew a new axle ({Wagons.AxleWood} log)"))
                v.Issue(new FitAxleCommand(v.Me, wagonAt));
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                v.Tip(why != null ? Cap(why) + "." : Controls.Keys("Carpenter's work. At each end: hold ~F~ to raise the mallet and let go to strike, trimming the arm to the mark (~Space~ when it's there); then drive the hub on, lighter as it tightens."));
        }
        if ((wagon.Damage & WagonDamage.Board) != 0)
        {
            var why = WagonJoinery.BoardProblem(v.Sim, v.Me, wagonAt);
            ImGui.BeginDisabled(why != null || v.Working);
            if (ImGui.Button($"Nail a plank across the sprung board ({Wagons.BoardPlanks} plank)"))
                v.Issue(new PegBoardCommand(v.Me, wagonAt));
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                v.Tip(why != null ? Cap(why) + "." : Controls.Keys("Carpenter's work, with the claw hammer. Tap each nail to set it, then drive it: hold ~F~ to raise, ~A~/~D~ keep the head over the nail, let go to strike. A bent one's drawn with the claw (~Space~)."));
        }
    }

    /// <summary>A tree coming down (<see cref="Falling"/>), as far as it carries: the hinge's creak, then the crash as it lands.</summary>
    private sealed class TreesFalling : IWorldSounds
    {
        private long _heard = -1;

        public void Update(WorldSoundContext ctx)
        {
    var sim = ctx.Sim;
            var w = sim.World;
            if (_heard < 0)
                _heard = sim.Clock.Tick; // a new game: only trees that fall from now
            var newest = _heard;
            foreach (var tree in w.Each<Falling>())
            {
                var at = w.Get<Falling>(tree).At;
                if (at <= _heard)
                    continue;
                newest = Math.Max(newest, at);
                var cell = w.Get<Sim.Components.Thing>(tree).Cell;
                if (sim.Clock.Tick - at <= FreshTicks && ctx.Reach(cell) is { } muffle)
                {
                    var land = Felling.CreakTicks / (float)SimClock.TicksPerSecond;
                    ctx.Audio.Play(Sounds.Creak, WorldSoundContext.At(cell), muffle: muffle, pitch: 0.6f);
                    ctx.Audio.Play(Sounds.Heavy, WorldSoundContext.At(cell), muffle: muffle, pitch: 0.5f, delay: land);
                    ctx.Audio.Play(Sounds.Knock, WorldSoundContext.At(cell), muffle: muffle, pitch: 0.45f, delay: land + 0.12f);
                }
            }
            _heard = newest;
        }

        /// <summary>A sound's news for this long after the tick it happened (as the game's own).</summary>
        private const long FreshTicks = SimClock.TicksPerSecond * 3;
    }
}

/// <summary>The sound of the carpenter's hands-on work, from the session in hand: as the game's own work sounds do.</summary>
internal static class CarpenterSounds
{
    /// <summary>Fade for loops a held key turns on and off: quick, or they never come up.</summary>
    private const float Quick = 0.08f;

    public static void Update(WorkSoundContext ctx, WorkSession session)
    {
        switch (session)
        {
            case SawSession s:
                // The axe's thunk on each blow while hewing; then the saw's rasp on the downstroke, louder rushed.
                if (s.Hewing)
                    Count(ctx, s.Blows, Sounds.Knock);
                ctx.Audio.Hold("work:saw", Sounds.Saw, s.Cutting ? 0.5f + 0.5f * Math.Min(1f, s.Pace / SawSession.MostPace) : 0f, fade: Quick);
                break;
            case FellSession s:
                // The axe biting into the trunk on each blow, deeper notes as the cut goes in.
                Count(ctx, s.Blows, Sounds.Knock);
                break;
            case BuckSession s:
                // The crosscut's rasp on the pull; a snap when the saw binds in the kerf.
                ctx.Audio.Hold("work:saw", Sounds.Saw, s.Cutting ? 0.4f + 0.5f * Math.Min(1f, s.Pace / BuckSession.MostPace) : 0f, pitch: 0.85f, fade: Quick);
                Count(ctx, s.Binds, Sounds.Snap);
                break;
            case CrosscutSession s:
                ctx.Audio.Hold("work:saw", Sounds.Saw, s.Held && s.Drawn < 1f ? 0.45f : 0f, pitch: 0.85f, fade: Quick);
                break;
            case SplitSession s:
                // Each blow knocks; a split cracks the log open.
                if (s.Blows > ctx.A)
                    ctx.Audio.Play(s.LastBlow == SplitBlow.Split ? Sounds.Snap : Sounds.Knock, pitch: s.LastBlow == SplitBlow.Split ? 0.8f : 1f);
                ctx.A = s.Blows;
                break;
            case NailSession s:
                // The hammer on the nail, ringing as it goes home; a dull knock on a miss or a bend.
                if (s.Blows > ctx.A)
                    ctx.Audio.Play(s.LastBlow is NailBlow.Set or NailBlow.Drove or NailBlow.Bruised ? Sounds.AnvilFair : Sounds.Knock,
                        gain: s.LastBlow == NailBlow.Set ? 0.5f : 0.8f, pitch: 1.6f + 0.3f * s.Depth);
                ctx.A = s.Blows;
                break;
            case FletchSession s:
                // The knife along the shaft; the head scraping on; a rustle for each feather.
                ctx.Audio.Hold("work:shave", Sounds.Scrub, s.Stage == ArrowStage.Shave && s.Held ? 0.5f : 0f, pitch: 1.4f, fade: Quick);
                ctx.Audio.Hold("work:seat", Sounds.Grind, s.Stage == ArrowStage.Head && s.Held ? 0.35f : 0f, pitch: 0.7f, fade: Quick);
                Count(ctx, s.Set, Sounds.Cloth);
                break;
            case JoinSession s:
                // The mallet on the chisel or the tenon; a crack when a joint splits.
                Count(ctx, s.Blows, Sounds.Knock);
                if (s.Splits > ctx.B)
                    ctx.Audio.Play(Sounds.Snap);
                ctx.B = s.Splits;
                break;
            case BowSession s:
                ctx.Audio.Hold("work:scrape", Sounds.Scrub, s.Scraping ? 0.45f : 0f, pitch: 0.8f, fade: Quick);
                Count(ctx, s.Missed, Sounds.Snap); // the loop slipping past the nock
                break;
        }
    }

    private static void Count(WorkSoundContext ctx, int now, string id)
    {
        if (now > ctx.A)
            ctx.Audio.Play(id);
        ctx.A = now;
    }

    private static void Pump(WorkSoundContext ctx, bool pumping)
    {
        if (pumping && !ctx.Flag)
            ctx.Audio.Play(Sounds.ForgeBellows);
        ctx.Flag = pumping;
    }
}

using Brutal.ImGuiApi;
using Narsil.Sim.Core;
using Narsil.Ui;
using HeroesNotIncluded.Sim;
using HeroesNotIncluded.Sim.Components;
using HeroesNotIncluded.Sim.Systems;
using HeroesNotIncluded.Sim.Work;
using HeroesNotIncluded.Ui;

namespace HeroesNotIncluded;

// The inn's kitchen: the stew pot from the side as it's tended (its heat shown only by the
// surface and the steam), seasoning by taste, and the pot beside you between times. Everything
// here is read from the session or Perception: never the pot's rot or the seasoning it wants.
public sealed partial class VillageGame
{
    private static readonly float4 StewColour = new(0.47f, 0.27f, 0.13f, 1f);

    /// <summary>The pot over the fire: the flames as the fire's set, the surface as hot as it is, the spoon when you stir.</summary>
    private void DrawStew(StewSession s)
    {
        BeginWork(s.Title, ("F", "hold to stir"), ("A/D", "bank or feed the fire"), ("Esc", "leave it"));
        TextC(Theme.Dim, "Keep it at a simmer, and stir before it catches.");
        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(420, 180);
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(in size);
        var max = min + size;
        Gui.Frame(dl, min, max, UiTheme.Current.Frames.Inset, Theme.Well);

        // The fire under it: taller and brighter as it's fed.
        var potMin = new float2(min.X + size.X * 0.25f, min.Y + size.Y * 0.28f);
        var potMax = new float2(max.X - size.X * 0.25f, max.Y - Gui.S(44));
        var fireY = max.Y - Gui.S(10);
        for (var i = 0; i < 9; i++)
        {
            var x = potMin.X + Gui.S(14) + i * (potMax.X - potMin.X - Gui.S(28)) / 8f;
            var flicker = 0.7f + 0.3f * MathF.Sin(_time * (6f + i % 3) + i * 2.1f);
            var h = Gui.S(6 + 30 * s.Flame) * flicker;
            var a = new float2(x - Gui.S(7), fireY);
            var b = new float2(x + Gui.S(7), fireY);
            var c = new float2(x, fireY - h);
            dl.AddTriangleFilled(in a, in b, in c, Gui.U8(new float4(0.95f, 0.45f + 0.3f * s.Flame, 0.1f, 0.85f)));
        }

        // The pot (the iron pot's art at a whole-pixel scale, sat on the fire), and the stew's surface in it.
        float2 surfMin, surfMax;
        if (Gui.TrySprite("ui/work/stew_pot", out var potArt))
        {
            var px = MathF.Max(1f, MathF.Floor((potMax.X - potMin.X + Gui.S(12)) / 44f));
            var pMin = new float2(MathF.Round((potMin.X + potMax.X) * 0.5f - 22 * px), MathF.Round(potMax.Y - 30 * px));
            Gui.Image(dl, potArt, pMin, pMin + new float2(44, 30) * px, Gui.White);
            surfMin = pMin + new float2(6, 5) * px;
            surfMax = pMin + new float2(38, 9) * px;
        }
        else
        {
            dl.AddRectFilled(in potMin, in potMax, new ImColor8(38, 36, 38, 255), Gui.S(14), ImDrawFlags.None);
            var rimMin = new float2(potMin.X - Gui.S(6), potMin.Y - Gui.S(6));
            var rimMax = new float2(potMax.X + Gui.S(6), potMin.Y + Gui.S(4));
            dl.AddRectFilled(in rimMin, in rimMax, new ImColor8(58, 56, 60, 255), Gui.S(3), ImDrawFlags.None);
            surfMin = new float2(potMin.X + Gui.S(8), potMin.Y + Gui.S(4));
            surfMax = new float2(potMax.X - Gui.S(8), potMin.Y + Gui.S(16));
        }
        var stew = StewColour * (0.85f + 0.15f * s.Heat);
        stew.W = 1f;
        dl.AddRectFilled(in surfMin, in surfMax, Gui.U8(stew), Gui.S(4), ImDrawFlags.None);

        // Bubbles: none when still, a lazy few at a simmer, a churn at the boil.
        var bubbles = s.Surface switch { Surface.Still => 0, Surface.Steaming => 1, Surface.Simmer => 4, _ => 10 };
        for (var i = 0; i < bubbles; i++)
        {
            var t = (_time * (0.6f + 0.25f * i) + i * 0.37f) % 1f;
            var c = new float2(surfMin.X + ((i * 0.618034f) % 1f) * (surfMax.X - surfMin.X), surfMin.Y + Gui.S(6));
            dl.AddCircle(in c, Gui.S(2 + 4 * t), new ImColor8(210, 170, 120, (byte)(200 * (1f - t))), 10, Gui.S(1.2f));
        }

        // Steam off the top, thicker as it heats.
        var dt = ImGui.GetIO().DeltaTime;
        _steamDue += dt * 50f * Math.Max(0f, s.Heat - 0.2f);
        var seed = (int)(_time * 1000f);
        while (_steamDue >= 1f)
        {
            _steamDue -= 1f;
            var r = new SimRandom((ulong)(seed++ * 2654435761u + 11));
            var at = new float2(surfMin.X + r.NextFloat() * (surfMax.X - surfMin.X), surfMin.Y);
            _steam.Add(new Puff(at, new float2(r.Range(-12f, 12f), r.Range(-60f, -30f)) * Gui.Scale, _time, r.Range(0.6f, 1.2f), Gui.S(r.Range(5f, 10f))));
        }
        _steam.RemoveAll(p => _time - p.Born > p.Life);
        foreach (var p in _steam)
        {
            var t = _time - p.Born;
            var at = p.Pos + p.Vel * t;
            dl.AddCircleFilled(in at, p.Size * (1f + t), new ImColor8(230, 232, 236, (byte)(90 * (1f - t / p.Life))), 12);
        }

        // The spoon: going round while you stir, resting against the rim when you don't.
        var mid = (surfMin + surfMax) * 0.5f;
        var tip = s.Stirring
            ? mid + new float2(MathF.Cos(_time * 7f) * (surfMax.X - surfMin.X) * 0.3f, Gui.S(2))
            : new float2(surfMax.X - Gui.S(14), surfMin.Y + Gui.S(4));
        var handle = tip + new float2(Gui.S(46), -Gui.S(70));
        dl.AddLine(in tip, in handle, new ImColor8(150, 110, 70, 255), Gui.S(4));

        ImGui.Spacing();
        TextC(s.Surface is Surface.Simmer ? Theme.Good : s.Surface == Surface.Boil ? Theme.Warn : Theme.Dim,
            $"It's {Cooking.SurfaceLabel(s.Surface)}.");
        if (s.Catching)
        {
            ImGui.SameLine(0, 12);
            TextC(Theme.Bad, "Something's catching on the bottom!");
        }
        Bar("Cooked", Math.Clamp(s.Doneness, 0f, 1f), Theme.Accent);
        if (!s.Lit)
            TextC(Theme.Bad, "The fire's out: it'll only cool.");
        EndWork();
    }

    /// <summary>Seasoning by taste: pinches in, a taste, and what it says.</summary>
    private void DrawSeason(SeasonSession s)
    {
        BeginWork(s.Title, ("F", "a pinch"), ("Space", "taste it"), ("Esc", "that'll do"));
        TextC(Theme.Dim, "Every pot wants its own. Taste as you go: it can't come back out.");
        SeasonScene(s.StartTick, s.Pinches);
        ImGui.Spacing();
        ImGui.Text($"{s.Pinches} pinch{(s.Pinches == 1 ? "" : "es")} in.");
        if (s.TastedAt < 0)
            TextC(Theme.Dim, "You haven't tasted it yet.");
        else
        {
            var fresh = s.TastedAt == s.Pinches;
            var colour = s.Taste == "about right" ? Theme.Good : s.Taste.Contains("salty") ? Theme.Bad : Theme.Warn;
            TextC(fresh ? colour : Theme.Dim, fresh ? $"\"{Cap(s.Taste)}.\"" : $"Last taste: \"{s.Taste}\" (before {s.Pinches - s.TastedAt} more).");
        }
        EndWork();
    }

    /// <summary>Smelling food (or the pot): a sniff at a time, and what the last one said. Never the rot itself.</summary>
    private void DrawSniff(SniffSession s)
    {
        BeginWork(s.Title, ("Space", "sniff"), ("Esc", "that'll do"));
        TextC(Theme.Dim, s.IsPot ? "A pot hides what's in it. At best you'll know something's off." : "Fresh, fine, on the turn, or off. A practised nose is surer.");
        ImGui.Spacing();
        if (s.Sniffs == 0)
            TextC(Theme.Dim, Controls.Keys("You haven't smelt it yet: ~Space~."));
        else
        {
            var bad = s.Smell is "off" or "not quite right";
            var colour = bad ? Theme.Bad : s.Smell == "on the turn" ? Theme.Warn : Theme.Good;
            TextC(colour, $"It smells {s.Smell}.");
            if (s.Sniffs > 1)
                TextC(Theme.Dim, $"({s.Sniffs} sniffs.)");
        }
        EndWork();
    }

    /// <summary>
    /// Packing rations: the cloth's four corners folded on cue, each from its side, then the cord
    /// drawn and tied. The cord reads only as slack, snug or biting: how snug is the packer's own
    /// judgement, and a slack-ish knot never shows.
    /// </summary>
    private void DrawPack(PackSession s)
    {
        if (s.Wrapped)
            BeginWork(s.Title, ("F", "hold to draw the cord, let go to tie"), ("Esc", "leave it"));
        else
            BeginWork(s.Title, ("A/D", "fold the corner from its side, as the hand comes round"), ("Esc", "leave it"));
        var dl = ImGui.GetWindowDrawList();
        var size = Gui.S(420, 150);
        var min = ImGui.GetCursorScreenPos();
        ImGui.Dummy(in size);
        var max = min + size;
        Gui.Frame(dl, min, max, UiTheme.Current.Frames.Inset, Theme.Well);

        // The cloth from above, its corners folding in, and the food in the middle.
        var c = (min + max) * 0.5f;
        var half = Gui.S(46);
        var cloth = new float4(0.86f, 0.82f, 0.72f, 1f);
        var a = c + new float2(-half, -half);
        var b = c + new float2(half, half);
        dl.AddRectFilled(in a, in b, Gui.U8(cloth), Gui.S(3), ImDrawFlags.None);
        var food = Gui.S(16);
        var fa = c - new float2(food, food);
        var fb = c + new float2(food, food);
        dl.AddRectFilled(in fa, in fb, Gui.U8(new float4(0.55f, 0.42f, 0.25f, 1f)), Gui.S(6), ImDrawFlags.None);
        // Folded corners: a triangle over the middle, crisp or crumpled as it landed.
        float2[] corners = [new(-1, -1), new(1, -1), new(1, 1), new(-1, 1)];
        for (var i = 0; i < PackSession.Folds; i++)
        {
            if (i >= s.Folded)
                continue;
            var k = corners[i];
            var side1 = c + new float2(k.X * half, -k.Y * half);
            var side2 = c + new float2(-k.X * half, k.Y * half);
            var shade = 0.7f + 0.25f * s.FoldMarks[i];
            var crumple = (1f - s.FoldMarks[i]) * Gui.S(10);
            var inward = c + k * crumple;
            dl.AddTriangleFilled(in side1, in side2, in inward, Gui.U8(new float4(cloth.X * shade, cloth.Y * shade, cloth.Z * shade, 1f)));
        }
        // The cord, once it's wrapped: drawn tighter as it's pulled.
        if (s.Wrapped)
        {
            var sag = Gui.S(18) * Math.Max(0f, 1f - s.Tension);
            var l = c + new float2(-half - Gui.S(30), sag);
            var r = c + new float2(half + Gui.S(30), sag);
            var cord = new ImColor8(120, 84, 50, 255);
            dl.AddLine(in l, in c, cord, Gui.S(3));
            dl.AddLine(in c, in r, cord, Gui.S(3));
        }
        else
        {
            // The hand coming round: a mark sweeping toward the cue at the top.
            var dir = s.NextSide < 0 ? "A" : "D";
            var cueA = new float2(min.X + Gui.S(12), min.Y + Gui.S(12));
            ImGui.SetCursorScreenPos(cueA);
            TextC(s.OnCue ? Theme.Good : Theme.Dim, Controls.Keys($"Corner {s.Folded + 1} of {PackSession.Folds}: ~{dir}~"));
            ImGui.SetCursorScreenPos(new float2(min.X, max.Y));
        }
        ImGui.Spacing();
        if (!s.Wrapped)
            Bar("", s.Sweep, s.OnCue ? Theme.Good : Theme.Dim);
        else
            TextC(s.Feel == "snug" ? Theme.Good : s.Feel == "biting" ? Theme.Bad : Theme.Dim, $"The cord feels {s.Feel}.");
        EndWork();
    }

    /// <summary>The stew pot beside you, between times: what's in it, how it looks, and its steps.</summary>
    private void DrawPotPanel()
    {
        if (!HasAvatar || PotBeside() is not { } p)
            return;
        _hud.Next(Anchor.Bottom);
        Panel.Begin("The stew pot###pot", Hud.Flags | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoNav);
        _hud.Took();
        if (p.Empty)
            TextC(Theme.Dim, Controls.Keys("Empty. Bring it food from the larder: ~F~ at the pot puts in what you carry."));
        else
        {
            Wrapped($"In it: {p.Contents}. It tastes {p.Tastes}.");
            if (p.House is { } house)
                TextC(Theme.Good, $"It tastes like your {house}: it's the house dish.");
            TextC(p.Surface == Surface.Simmer ? Theme.Good : Theme.Dim, $"It's {Cooking.SurfaceLabel(p.Surface)}{(p.Lit ? "" : ", and the fire beside it is out")}.");
            if (p.Caught is { } caught)
                TextC(Theme.Bad, Cap(caught) + ".");
            if (!p.Cooked)
                Bar("Cooked", p.Doneness, Theme.Accent);
            else
                TextC(Theme.Good, $"Cooked through: {p.Bowls} bowl{(p.Bowls == 1 ? "" : "s")} in it.");
            if (ImGui.SmallButton("Tend it"u8))
                Issue(new PotCommand(_avatar, p.Pot, PotStep.Tend));
            ImGui.SameLine(0, 6);
            if (ImGui.SmallButton("Season it"u8))
                Issue(new PotCommand(_avatar, p.Pot, PotStep.Season));
            ImGui.SameLine(0, 6);
            if (ImGui.SmallButton("Smell it"u8))
                Issue(new PotCommand(_avatar, p.Pot, PotStep.Sniff));
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip("Has anything in it turned? You can't pick out what. Better to smell it before it goes in.");
            if (p.Doneness >= Cooking.LadleFrom && p.Bowls > 0)
            {
                ImGui.SameLine(0, 6);
                if (ImGui.SmallButton("Ladle a bowl"u8))
                    Issue(new PotCommand(_avatar, p.Pot, PotStep.Ladle));
            }
            if (p.Cooked && _sim.World.TryGet<Trade>(_avatar, out var trade) && trade.Def.Serving(Sim.Defs.ServiceKind.Meal))
                DrawNameDish(p);
        }
        Panel.End();
    }

    private ImInputString? _dishNameInput;

    /// <summary>Name a cooked pot the house dish: a button, then the name. The sim says if it isn't good enough.</summary>
    private void DrawNameDish(KnownPot p)
    {
        if (_dishNameInput == null)
        {
            if (ImGui.SmallButton(p.House != null ? "Rename the house dish"u8 : "Make it the house dish"u8))
                _dishNameInput = new ImInputString(Sim.Shops.ShopName.MaxLength + 8, ""u8);
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.None))
                Tip("Put a name to a fine pot. It goes on the sign, and any pot that tastes like it is the same dish. Some will ask for it.");
            return;
        }
        ImGui.SetNextItemWidth(Gui.S(200));
        var done = ImGui.InputTextWithHint("##dishname"u8, "What's it called?"u8, _dishNameInput, ImGuiInputTextFlags.EnterReturnsTrue, null, IntPtr.Zero);
        OnScreenKeyboard.AfterTextField();
        ImGui.SameLine(0, 6);
        done |= ImGui.SmallButton("Name it"u8);
        ImGui.SameLine(0, 6);
        if (ImGui.SmallButton("Cancel"u8))
            _dishNameInput = null;
        else if (done)
        {
            Issue(new NameDishCommand(_avatar, p.Pot, Narsil.Ui.ImGuiText.Value(_dishNameInput)));
            _dishNameInput = null;
        }
    }

    /// <summary>A stew pot in arm's reach of you, as you see it.</summary>
    private KnownPot? PotBeside()
    {
        var at = _sim.World.Get<Position>(_avatar).Cell;
        var map = _sim.Map;
        for (var dy = -1; dy <= 1; dy++)
        for (var dx = -1; dx <= 1; dx++)
        {
            var c = new int3(at.X + dx, at.Y + dy, at.Z);
            if (map.InBounds(c) && map.Structure[map.Idx(c)] is { IsNull: false } s && Cooking.IsPot(_sim, s) && View.Pot(s) is { } p)
                return p;
        }
        return null;
    }
}

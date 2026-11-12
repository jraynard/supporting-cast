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

        // The pot, and the stew's surface in it.
        dl.AddRectFilled(in potMin, in potMax, new ImColor8(38, 36, 38, 255), Gui.S(14), ImDrawFlags.None);
        var rimMin = new float2(potMin.X - Gui.S(6), potMin.Y - Gui.S(6));
        var rimMax = new float2(potMax.X + Gui.S(6), potMin.Y + Gui.S(4));
        dl.AddRectFilled(in rimMin, in rimMax, new ImColor8(58, 56, 60, 255), Gui.S(3), ImDrawFlags.None);
        var surfMin = new float2(potMin.X + Gui.S(8), potMin.Y + Gui.S(4));
        var surfMax = new float2(potMax.X - Gui.S(8), potMin.Y + Gui.S(16));
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
        BeginWork(s.Title, ("F", "a pinch"), ("D", "taste it"), ("A", "that'll do"), ("Esc", "stop"));
        TextC(Theme.Dim, "Every pot wants its own. Taste as you go: it can't come back out.");
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

    /// <summary>The stew pot beside you, between times: what's in it, how it looks, and its steps.</summary>
    private void DrawPotPanel()
    {
        if (!HasAvatar || PotBeside() is not { } p)
            return;
        _hud.Next(Anchor.Bottom);
        Panel.Begin("The stew pot###pot", Hud.Flags | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoNav);
        _hud.Took();
        if (p.Empty)
            TextC(Theme.Dim, "Empty. Bring it food from the larder: F at the pot puts in what you carry.");
        else
        {
            Wrapped($"In it: {p.Contents}.");
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
            if (p.Doneness >= Cooking.LadleFrom && p.Bowls > 0)
            {
                ImGui.SameLine(0, 6);
                if (ImGui.SmallButton("Ladle a bowl"u8))
                    Issue(new PotCommand(_avatar, p.Pot, PotStep.Ladle));
            }
        }
        Panel.End();
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

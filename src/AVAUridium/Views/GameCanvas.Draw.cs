using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAUridium.Engine;
using Effect = AVAUridium.Engine.Effect;

namespace AVAUridium.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAUridium/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Cyan = 0xFF6AE8FF, Orange = 0xFFFF9A30;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);

    private bool InGame => Engine.State is GameState.Attract or GameState.Playing or GameState.Paused or GameState.GameOver;

    private const double T = Field.Tile;
    /// <summary>Each dreadnought's colours: its deck, the lines of its plates, its markings, and its towers.</summary>
    private static readonly (uint Deck, uint Lines, uint Mark, uint Tower)[] Hulls =
    [
        (0xFF3A4250, 0xFF2A3038, 0xFFE08A30, 0xFF7A8698),   // basalt
        (0xFF6A6A84, 0xFF4E4E66, 0xFF60E0F0, 0xFFB0B0CC),   // quartz
        (0xFF6A3A30, 0xFF4E2A22, 0xFFF0D040, 0xFFA8705E),   // jasper
        (0xFF5A2234, 0xFF421826, 0xFFFF80B0, 0xFF9A4A62),   // garnet
        (0xFF26242E, 0xFF18161E, 0xFFB070FF, 0xFF5A5468),   // obsidian
        (0xFF2A5A50, 0xFF1E423A, 0xFFB0F060, 0xFF5E9A8A),   // beryl
        (0xFF34343A, 0xFF242428, 0xFFF0F0F0, 0xFF707078),   // onyx
        (0xFF6A5222, 0xFF4E3C18, 0xFF60D0FF, 0xFFB09050),   // topaz
    ];

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF000000), new Rect(0, 0, Field.Width, Field.Height));
        if (InGame) DrawRun(dc);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default:
                if (InGame && g.Phase == Phase.Reactor) DrawReactor(dc);
                DrawMessages(dc);
                break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(512, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(512, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(512, 360), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(512, Field.Bottom - 16), 24, Yellow, center: true);
    }

    // ───────────────────────────── the dreadnought ─────────────────────────────

    private void DrawRun(DrawingContext dc)
    {
        var w = Engine.World;
        var pal = Hulls[w.Index % Hulls.Length];
        using (dc.PushClip(new Rect(0, Field.Top, Field.Width, Field.Bottom - Field.Top)))
        {
            DrawStars(dc, w.CamX);
            using (dc.PushTransform(Matrix.CreateTranslation(-Math.Round(w.CamX), 0)))
            {
                var d = w.Dread;
                int c0 = (int)Math.Floor(w.CamX / T) - 2, c1 = c0 + Field.ViewCols + 4;
                // The deck, then the towers' shadows, then the towers.
                for (int c = Math.Max(0, c0); c < Math.Min(d.Cols, c1); c++)
                for (int r = 0; r < Field.Rows; r++)
                    DrawDeck(dc, w, c, r, pal);
                for (int c = Math.Max(0, c0); c < Math.Min(d.Cols, c1); c++)
                for (int r = 0; r < Field.Rows; r++)
                    if (d.Grid[c, r] == Tile.Wall) dc.FillRectangle(Brush(0x70000000), new Rect(c * T + 7, Field.Top + r * T + 9, T, T));
                foreach (var e in w.Enemies) if (e.OnDeck) DrawDeckThing(dc, e, w);
                for (int c = Math.Max(0, c0); c < Math.Min(d.Cols, c1); c++)
                for (int r = 0; r < Field.Rows; r++)
                    if (d.Grid[c, r] == Tile.Wall) DrawTower(dc, d, c, r, pal);
                DrawShipShadow(dc, w);
                foreach (var e in w.Enemies) if (!e.OnDeck) DrawFighter(dc, e);
                foreach (var m in w.Missiles) DrawPellet(dc, m);
                foreach (var s in w.Shots) DrawShot(dc, s);
                DrawShip(dc, w);
                foreach (var e in w.Effects) DrawEffect(dc, e);
                if (Engine.Phase == Phase.Destruct) DrawDestruct(dc, w);
            }
        }
    }

    private void DrawStars(DrawingContext dc, double camX)
    {
        dc.FillRectangle(Brush(0xFF04040C), new Rect(0, Field.Top, Field.Width, Field.Bottom - Field.Top));
        for (int layer = 0; layer < 2; layer++)
        {
            double speed = 0.15 + layer * 0.2;
            for (int i = 0; i < 50; i++)
            {
                uint h = Hash(i, layer, 9);
                double x = ((h % 1100) - camX * speed) % 1100;
                if (x < 0) x += 1100;
                double y = Field.Top + (h >> 12) % 640;
                dc.FillRectangle(Brush(Alpha(0xFFC0D0FF, 0.4 + layer * 0.3)), new Rect(x - 30, y, 1 + layer, 1 + layer));
            }
        }
    }

    private void DrawDeck(DrawingContext dc, World w, int c, int r, (uint Deck, uint Lines, uint Mark, uint Tower) pal)
    {
        var d = w.Dread;
        var t = d.Grid[c, r];
        if (t == Tile.Space) return;
        double x = c * T, y = Field.Top + r * T;
        if (t == Tile.Runway)
        {
            dc.FillRectangle(Brush(0xFF1A1C22), new Rect(x, y, T + 0.5, T + 0.5));
            bool edge = r == d.Runway.R0 || r == d.Runway.R1;
            if (edge) dc.FillRectangle(Brush(w.LandNow && (int)(_clock * 4) % 2 == 0 ? 0xFFFFE040 : 0xFFE0E0E0), new Rect(x, r == d.Runway.R0 ? y : y + T - 3, T + 0.5, 3));
            else if (c % 4 == 0) Poly(dc, w.LandNow ? 0xFF60FF80 : 0xFF6A7080, null, 0, new Point(x + 2, y + 2), new Point(x + 12, y + T), new Point(x + 2, y + T * 2 - 2));
            return;
        }
        dc.FillRectangle(Brush(pal.Deck), new Rect(x, y, T + 0.5, T + 0.5));
        uint h = Hash(c, r, d.Index);
        if (c % 4 == 0) dc.FillRectangle(Brush(pal.Lines), new Rect(x, y, 1.5, T + 0.5));
        if (r % 3 == 0) dc.FillRectangle(Brush(pal.Lines), new Rect(x, y, T + 0.5, 1.5));
        if (h % 11 == 0) dc.FillRectangle(Brush(Alpha(0xFF000000, 0.3)), new Rect(x + 3, y + 4, 10, 3));
        if (h % 17 == 0) { dc.FillEllipse(x + 4, y + 4, 1.4, pal.Lines); dc.FillEllipse(x + 12, y + 12, 1.4, pal.Lines); }
        // The edge of the hull, and its markings.
        bool top = d.At(c, r - 1) == Tile.Space, bottom = d.At(c, r + 1) == Tile.Space;
        if (top) dc.FillRectangle(Brush(Alpha(0xFFFFFFFF, 0.35)), new Rect(x, y, T + 0.5, 2));
        if (bottom) dc.FillRectangle(Brush(Alpha(0xFF000000, 0.5)), new Rect(x, y + T - 3, T + 0.5, 3));
        if ((r == DreadBook.Mid - 7 || r == DreadBook.Mid + 6) && c % 6 < 3) dc.FillRectangle(Brush(Alpha(pal.Mark, 0.8)), new Rect(x, y + 6, T + 0.5, 4));
    }

    private static void DrawTower(DrawingContext dc, DreadDef d, int c, int r, (uint Deck, uint Lines, uint Mark, uint Tower) pal)
    {
        double x = c * T, y = Field.Top + r * T;
        dc.FillRectangle(Brush(pal.Tower), new Rect(x, y, T + 0.5, T + 0.5));
        // Lit from the top left.
        if (d.At(c, r - 1) != Tile.Wall) dc.FillRectangle(Brush(Alpha(0xFFFFFFFF, 0.5)), new Rect(x, y, T + 0.5, 3));
        if (d.At(c - 1, r) != Tile.Wall) dc.FillRectangle(Brush(Alpha(0xFFFFFFFF, 0.3)), new Rect(x, y, 3, T + 0.5));
        if (d.At(c, r + 1) != Tile.Wall) dc.FillRectangle(Brush(Alpha(0xFF000000, 0.45)), new Rect(x, y + T - 4, T + 0.5, 4));
        if (d.At(c + 1, r) != Tile.Wall) dc.FillRectangle(Brush(Alpha(0xFF000000, 0.35)), new Rect(x + T - 3, y, 3, T + 0.5));
        if (Hash(c, r, 3) % 5 == 0) dc.FillRectangle(Brush(Alpha(pal.Mark, 0.7)), new Rect(x + 5, y + 5, 6, 6));
    }

    // ───────────────────────────── the Shrike ─────────────────────────────

    /// <summary>How the ship looks now: which way (squashed in a loop), how big (higher in a loop), and how rolled.</summary>
    private static (double Sx, double Sy, double Lift) Pose(Ship s)
    {
        double sx = s.Facing, lift = 0;
        if (s.Turn > 0)
        {
            double p = 1 - s.Turn / Ship.TurnTime;
            sx = s.Facing * Math.Cos(p * Math.PI);
            lift = Math.Sin(p * Math.PI);
        }
        return (sx * (1 + 0.35 * lift), (1 - 0.65 * Math.Min(1, s.Roll)) * (1 + 0.35 * lift), lift);
    }

    private void DrawShipShadow(DrawingContext dc, World w)
    {
        var s = w.Ship;
        if (s.Dead || w.Dread.AtPoint(s.X, s.Y) == Tile.Space) return;
        var (sx, sy, lift) = Pose(s);
        using (dc.PushTransform(Matrix.CreateScale(sx, sy) * Matrix.CreateTranslation(s.X + 10 + lift * 16, s.Y + 14 + lift * 22)))
            Poly(dc, 0x60000000, null, 0, ShrikeOutline());
    }

    private static Point[] ShrikeOutline() =>
    [
        new(24, 0), new(4, -6), new(-6, -22), new(-14, -22), new(-10, -6), new(-18, -4), new(-18, 4), new(-10, 6), new(-14, 22), new(-6, 22), new(4, 6),
    ];

    private void DrawShip(DrawingContext dc, World w)
    {
        var s = w.Ship;
        if (s.Dead) return;
        if (s.Safe > 0 && (int)(_clock * 14) % 2 == 0) return;
        DrawShrike(dc, s.X, s.Y, Pose(s));
    }

    private void DrawShrike(DrawingContext dc, double x, double y, (double Sx, double Sy, double Lift) pose)
    {
        using (dc.PushTransform(Matrix.CreateScale(pose.Sx, pose.Sy) * Matrix.CreateTranslation(x, y)))
        {
            double fl = 8 + 5 * Math.Sin(_clock * 45);
            Poly(dc, 0xFFFF9030, null, 0, new Point(-18, -4), new Point(-18 - fl, 0), new Point(-18, 4));
            Poly(dc, 0xFFD8DEE8, 0xFF4A5468, 1.5, ShrikeOutline());
            // Wing markings and the cockpit.
            Poly(dc, 0xFF3A70D0, null, 0, new Point(-2, -8), new Point(-8, -20), new Point(-12, -20), new Point(-8, -8));
            Poly(dc, 0xFF3A70D0, null, 0, new Point(-2, 8), new Point(-8, 20), new Point(-12, 20), new Point(-8, 8));
            dc.DrawEllipse(Brush(0xFF50D0FF), Pen(0xFF205080, 1), new Point(6, 0), 7, 3.5);
            dc.FillRectangle(Brush(0xFFE04030), new Rect(-16, -1.5, 14, 3));
        }
    }

    private void DrawShot(DrawingContext dc, Shot s)
    {
        double dir = Math.Sign(s.Vx);
        dc.FillRectangle(Brush(0x6040FFFF), new Rect(s.X - 14, s.Y - 3, 28, 6), 3);
        dc.DrawLine(Pen(0xFFE0FFFF, 3), new Point(s.X - 12 * dir, s.Y), new Point(s.X + 10 * dir, s.Y));
    }

    private void DrawPellet(DrawingContext dc, Missile m)
    {
        double p = 4.5 + Math.Sin(_clock * 20 + m.X) * 1.2;
        dc.FillEllipse(m.X, m.Y, p + 2, 0x60FF8040);
        dc.FillEllipse(m.X, m.Y, p, 0xFFFFA050);
        dc.FillEllipse(m.X, m.Y, 2, 0xFFFFFFFF);
    }

    // ───────────────────────────── the defences ─────────────────────────────

    private void DrawFighter(DrawingContext dc, Enemy e)
    {
        double x = e.X, y = e.Y;
        bool flash = e.Hurt > 0 && (int)(_clock * 30) % 2 == 0;
        uint W(uint c) => flash ? 0xFFFFFFFF : c;
        double f = e.Vx >= 0 ? 1 : -1;
        // A shadow on the deck below.
        dc.DrawEllipse(Brush(0x50000000), null, new Point(x + 8, y + 12), 13, 6);
        switch (e.Kind)
        {
            case Foe.Dart:
                using (dc.PushTransform(Matrix.CreateScale(f, 1) * Matrix.CreateTranslation(x, y)))
                {
                    Poly(dc, W(0xFFE0603A), 0xFF401810, 1.2, new Point(16, 0), new Point(-12, -11), new Point(-6, 0), new Point(-12, 11));
                    dc.FillEllipse(4, 0, 3, 0xFFFFE0A0);
                }
                break;
            case Foe.Saucer:
                dc.DrawEllipse(Brush(W(0xFF8A9AB0)), Pen(0xFF303A48, 1.5), new Point(x, y), 17, 10);
                dc.DrawEllipse(Brush(W(0xFF60E0A0)), null, new Point(x, y - 2), 7, 5);
                for (int k = 0; k < 4; k++)
                    dc.FillEllipse(x - 12 + k * 8, y + 4, 1.8, (int)(_clock * 8 + k) % 4 == 0 ? 0xFFFFFF80 : 0xFF404858);
                break;
            case Foe.Swooper:
            {
                double flap = Math.Sin(e.T * 12) * 4;
                using (dc.PushTransform(Matrix.CreateScale(f, 1) * Matrix.CreateTranslation(x, y)))
                {
                    Poly(dc, W(0xFFB050E0), 0xFF301040, 1.2, new Point(12, 0), new Point(-4, -14 - flap), new Point(-14, -8), new Point(-6, 0), new Point(-14, 8), new Point(-4, 14 + flap));
                    dc.FillEllipse(4, 0, 3, 0xFFFFD0FF);
                }
                break;
            }
            case Foe.Chaser:
                using (dc.PushTransform(Matrix.CreateRotation(e.T * 6) * Matrix.CreateTranslation(x, y)))
                    for (int k = 0; k < 3; k++)
                    {
                        double a = k * Math.PI * 2 / 3;
                        Poly(dc, W(0xFFF0D040), null, 0, new Point(0, 0), new Point(Math.Cos(a) * 16, Math.Sin(a) * 16), new Point(Math.Cos(a + 0.8) * 9, Math.Sin(a + 0.8) * 9));
                    }
                dc.FillEllipse(x, y, 5, W(0xFFFF6030));
                break;
            case Foe.Mine:
            {
                bool blink = (int)(_clock * 6 + e.Id) % 2 == 0;
                for (int k = 0; k < 6; k++)
                {
                    double a = k * Math.PI / 3 + e.T * 2;
                    dc.DrawLine(Pen(0xFF8A8A9A, 2), new Point(x, y), new Point(x + Math.Cos(a) * 11, y + Math.Sin(a) * 11));
                }
                dc.FillEllipse(x, y, 7, W(blink ? 0xFFFF3030 : 0xFF8A1A1A));
                break;
            }
        }
    }

    private void DrawDeckThing(DrawingContext dc, Enemy e, World w)
    {
        double x = e.X, y = e.Y;
        bool flash = e.Hurt > 0 && (int)(_clock * 30) % 2 == 0;
        uint W(uint c) => flash ? 0xFFFFFFFF : c;
        switch (e.Kind)
        {
            case Foe.Pod:
            {
                double gl = 0.6 + 0.4 * Math.Sin(_clock * 4 + e.Id);
                dc.FillRectangle(Brush(0xFF20262E), new Rect(x - 16, y - 16, 32, 32), 4);
                dc.FillEllipse(x, y, 13, W(0xFF2A8A5A));
                dc.FillEllipse(x, y, 9, W(Alpha(0xFF60FFB0, gl)));
                dc.DrawEllipse(null, Pen(0xFFA0FFD0, 1.5), new Point(x, y), 13, 13);
                break;
            }
            case Foe.Gun:
            {
                var s = w.Ship;
                double a = Math.Atan2(s.Y - y, s.X - x);
                dc.FillRectangle(Brush(0xFF2A2E36), new Rect(x - 14, y - 14, 28, 28), 3);
                dc.DrawLine(Pen(W(0xFF1A1C22), 6), new Point(x, y), new Point(x + Math.Cos(a) * 18, y + Math.Sin(a) * 18));
                dc.DrawEllipse(Brush(W(0xFFA08A70)), Pen(0xFF3A3028, 1.5), new Point(x, y), 10, 10);
                dc.FillEllipse(x, y, 3, 0xFFFF6030);
                break;
            }
            case Foe.Port:
            {
                double open = e.FireIn < 0.6 ? 1 - e.FireIn / 0.6 : 0.2;
                dc.DrawRectangle(Brush(W(0xFF505868)), Pen(0xFF1A1C22, 2), new Rect(x - 15, y - 15, 30, 30), 2, 2);
                dc.FillEllipse(x, y, 10, 0xFF101014);
                dc.FillEllipse(x, y, 9 * open, 0xFFFF4040);
                for (int k = 0; k < 4; k++) dc.FillRectangle(Brush(0xFFE0B020), new Rect(x - 15 + (k % 2) * 26, y - 15 + (k / 2) * 26, 4, 4));
                break;
            }
        }
    }

    private void DrawEffect(DrawingContext dc, Effect e)
    {
        double a = 1 - e.Age / e.Life;
        switch (e.Kind)
        {
            case EffectKind.Score:
                Text(dc, e.Text, new Point(e.X, e.Y - e.Age * 40), 22, Alpha(e.Colour, a), center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(e.X, e.Y - e.Age * 30), 26, Alpha(e.Colour, Math.Min(1, a * 2)), center: true);
                break;
            case EffectKind.Burst:
            {
                int n = int.TryParse(e.Text, out int v) ? v : 10;
                dc.FillEllipse(e.X, e.Y, 18 * (1 - a) + 4, Alpha(0xFFFFFFFF, a * 0.6));
                for (int i = 0; i < n; i++)
                {
                    double ang = i * Math.PI * 2 / n + i, r = 6 + e.Age * 140;
                    dc.FillEllipse(e.X + Math.Cos(ang) * r, e.Y + Math.Sin(ang) * r, 3.5 * a + 1, Alpha(e.Colour, a));
                }
                break;
            }
            case EffectKind.Blast:
            {
                double r = 10 + e.Age * 120;
                dc.FillEllipse(e.X, e.Y, r * 0.6, Alpha(0xFFFFF0C0, a * 0.8));
                dc.DrawEllipse(null, Pen(Alpha(e.Colour, a), 6 * a + 1), new Point(e.X, e.Y), r, r);
                for (int i = 0; i < 14; i++)
                {
                    double ang = i * 0.45 + e.X, rr = r * (0.6 + (i % 3) * 0.25);
                    dc.FillEllipse(e.X + Math.Cos(ang) * rr, e.Y + Math.Sin(ang) * rr, 4 * a + 1, Alpha(0xFFFF8030, a));
                }
                break;
            }
        }
    }

    /// <summary>The dreadnought going up: blasts all over what's on the screen, more and more of them.</summary>
    private void DrawDestruct(DrawingContext dc, World w)
    {
        double t = Engine.PhaseTime;
        int n = (int)(t * 10);
        for (int i = 0; i < Math.Min(n, 50); i++)
        {
            uint h = Hash(i, w.Index, 5);
            double born = i / 10.0, age = t - born;
            if (age > 1) continue;
            double x = w.CamX + 40 + h % 940, y = Field.Top + 20 + (h >> 10) % 600, r = 10 + age * 90;
            dc.FillEllipse(x, y, r * 0.7, Alpha(0xFFFFF0C0, 1 - age));
            dc.DrawEllipse(null, Pen(Alpha(0xFFFF8030, 1 - age), 5), new Point(x, y), r, r);
        }
        if (t > 3.2) dc.FillRectangle(Brush(Alpha(0xFFFFFFFF, Math.Min(1, (t - 3.2) / 0.8))), new Rect(w.CamX - 10, Field.Top, Field.Width + 20, Field.Bottom - Field.Top));
    }

    // ───────────────────────────── the reactor ─────────────────────────────

    private void DrawReactor(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2, cy = Field.Top + 330;
        dc.DrawRectangle(Brush(0xE0080C14), Pen(0xFF3A6A9A, 3), new Rect(cx - 300, cy - 230, 600, 460), 16, 16);
        Text(dc, "THE REACTOR", new Point(cx, cy - 196), 40, Cyan, center: true);
        // The core.
        double p = 1 + 0.1 * Math.Sin(_clock * 8);
        dc.FillEllipse(cx, cy, 60 * p, 0x5060E0FF);
        dc.FillEllipse(cx, cy, 36, 0xFF80F0FF);
        dc.FillEllipse(cx, cy, 18, 0xFFFFFFFF);
        var cells = GameEngine.ReactorCells;
        for (int i = 0; i < cells.Length; i++)
        {
            double a = -Math.PI / 2 + i * Math.PI * 2 / cells.Length;
            double x = cx + Math.Cos(a) * 140, y = cy + Math.Sin(a) * 140;
            bool lit = g.ReactorLit == i;
            dc.DrawRectangle(Brush(lit ? 0xFFFFD040 : 0xFF14202E), Pen(lit ? 0xFFFFFFFF : 0xFF3A6A9A, 2), new Rect(x - 46, y - 22, 92, 44), 8, 8);
            Text(dc, cells[i].ToString(), new Point(x, y), 30, lit ? 0xFF101010 : 0xFFB0D0FF, center: true, shadow: false);
        }
        Text(dc, g.ReactorLeft > 0 ? $"FIRE TO STOP THE LIGHT - {g.ReactorLeft} LEFT" : "THE REACTOR IS RUNNING WILD", new Point(cx, cy + 196), 26, Yellow, center: true);
        Text(dc, $"WON {g.ReactorWon}", new Point(cx, cy + 170), 24, White, center: true);
    }

    // ───────────────────────────── around the edges ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        var w = g.World;
        dc.FillRectangle(Brush(0xFF06060E), new Rect(0, 0, Field.Width, Field.Hud));
        dc.FillRectangle(Brush(0xFF06060E), new Rect(0, Field.Bottom, Field.Width, Field.Height - Field.Bottom));
        dc.DrawLine(Pen(0xFF2A3A6A, 2), new Point(0, Field.Hud), new Point(Field.Width, Field.Hud));
        Text(dc, "SCORE", new Point(20, 24), 22, Dim);
        Text(dc, g.Score.ToString("0000000"), new Point(90, 24), 32, White);
        Text(dc, "HI " + Math.Max(g.HighScores.TopScore, g.Score).ToString("0000000"), new Point(Field.Width / 2 - 40, 24), 28, Yellow, center: true);
        if (!InGame) return;
        for (int i = 0; i < Math.Min(g.Lives - 1, 5); i++)
            DrawShrike(dc, Field.Width - 30 - i * 40, 24, (0.6, 0.6, 0));
        if (w.LandNow)
        {
            if ((int)(_clock * 3) % 2 == 0) Text(dc, "LAND NOW", new Point(Field.Width / 2 + 210, 24), 32, Red, center: true);
        }
        else Text(dc, $"PODS {w.PodsLeft}", new Point(Field.Width / 2 + 210, 24), 26, 0xFF60FFB0, center: true);
        DrawScanner(dc, w);
    }

    /// <summary>The whole dreadnought in a strip: its hull, towers, pods and strip, where the ship is, and the part on the screen.</summary>
    private void DrawScanner(DrawingContext dc, World w)
    {
        var d = w.Dread;
        double x0 = 80, y0 = Field.Bottom + 12, wd = 864, ht = 56;
        double k = wd / (d.Cols + 40), ky = ht / Field.Rows;
        double X(double px) => x0 + (px / T + 20) * k;
        dc.DrawRectangle(Brush(0xFF0A1018), Pen(0xFF2A3A6A, 1.5), new Rect(x0, y0, wd, ht), 4, 4);
        for (int c = 0; c < d.Cols; c += 2)
        {
            int top = 0, bottom = Field.Rows - 1;
            while (top < Field.Rows && d.Grid[c, top] == Tile.Space) top++;
            while (bottom >= 0 && d.Grid[c, bottom] == Tile.Space) bottom--;
            if (top <= bottom) dc.FillRectangle(Brush(0xFF2A3448), new Rect(X(c * T), y0 + top * ky, k * 2 + 0.5, (bottom - top + 1) * ky));
        }
        foreach (var (c0, c1) in d.Fences) dc.FillRectangle(Brush(0xFF6A7A98), new Rect(X(c0 * T), y0 + 1, Math.Max(1.5, (c1 - c0 + 1) * k), ht - 2));
        dc.FillRectangle(Brush(w.LandNow ? 0xFFFFE040 : 0xFF6A6A6A), new Rect(X(d.Runway.C0 * T), y0 + d.Runway.R0 * ky, (d.Runway.C1 - d.Runway.C0 + 1) * k, (d.Runway.R1 - d.Runway.R0 + 1) * ky));
        foreach (var e in w.Enemies)
            if (e.Kind == Foe.Pod) dc.FillEllipse(X(e.X), y0 + (e.Y - Field.Top) / T * ky, 2.5, 0xFF60FFB0);
        dc.DrawRectangle(null, Pen(0x80FFFFFF, 1), new Rect(X(w.CamX), y0, Field.Width / T * k, ht));
        if (!w.Ship.Dead) dc.FillEllipse(X(w.Ship.X), y0 + (w.Ship.Y - Field.Top) / T * ky, 3, (int)(_clock * 6) % 2 == 0 ? 0xFFFFFFFF : 0xFF60C0FF);
        Text(dc, w.Dread.Name, new Point(x0 / 2, y0 + ht / 2), 18, Dim, center: true);
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = Field.Width / 2, cy = Field.Top + 150;
        dc.FillRectangle(Brush(0xA0000000), new Rect(cx - 420, cy - 52, 840, 108), 10);
        Text(dc, msg, new Point(cx, cy - 12), msg.Length > 22 ? 44 : 56, 0xFFFFC040, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 34), 26, White, center: true);
    }

    // ───────────────────────────── title and scores ─────────────────────────────

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        DrawStars(dc, _clock * 140);
        // A dreadnought's deck sliding by underneath, with the Shrike over it.
        var d = DreadBook.All[(int)(_clock / 12) % DreadBook.All.Length];
        var pal = Hulls[d.Index];
        double cam = (_clock * 140) % (d.Length - Field.Width);
        using (dc.PushClip(new Rect(0, 560, Field.Width, Field.Bottom - 560)))
        using (dc.PushTransform(Matrix.CreateTranslation(-Math.Round(cam), 0)))
        {
            var w = new World { Dread = d, Index = d.Index };
            for (int c = (int)(cam / T); c < Math.Min(d.Cols, (int)(cam / T) + Field.ViewCols + 2); c++)
            for (int r = 0; r < Field.Rows; r++)
            {
                DrawDeck(dc, w, c, r, pal);
                if (d.Grid[c, r] == Tile.Wall) DrawTower(dc, d, c, r, pal);
            }
        }
        DrawShrike(dc, 300 + Math.Sin(_clock * 0.7) * 120, 620 + Math.Sin(_clock * 1.3) * 20, (1, 1 - 0.5 * Math.Abs(Math.Cos(_clock * 1.3)), 0));
        string title = "AVA URIDIUM";
        for (int i = 0; i < title.Length; i++)
        {
            double tx = cx - title.Length * 32 + 32 + i * 64;
            double ty = 150 + Math.Sin(_clock * 2.2 + i * 0.45) * 4;
            Text(dc, title[i].ToString(), new Point(tx + 3, ty + 4), 120, 0xFF101828, center: true, shadow: false);
            Text(dc, title[i].ToString(), new Point(tx, ty), 120, i < 3 ? 0xFFFFB040 : (i % 2 == 0 ? 0xFFB0C8E8 : 0xFF8AA0C8), center: true, shadow: false);
        }
        Text(dc, "AFTER GRAFTGOLD'S 1986 COMMODORE 64 GAME", new Point(cx, 226), 24, Dim, center: true);
        Text(dc, "EIGHT DREADNOUGHTS ARE STRIPPING THE MOONS OF THE LYRA SYSTEM.", new Point(cx, 284), 28, White, center: true);
        Text(dc, "FLY THE SHRIKE LOW OVER EACH ONE: SHOOT ITS FUEL PODS, LAND ON ITS STRIP,", new Point(cx, 316), 28, White, center: true);
        Text(dc, "AND SET ITS REACTOR RUNNING WILD.", new Point(cx, 348), 28, White, center: true);
        Text(dc, "UP OR DOWN ROLLS THE SHIP ON ITS SIDE, THIN ENOUGH FOR THE NARROWEST GAPS.", new Point(cx, 392), 24, 0xFFFFA070, center: true);
        Text(dc, "PULL BACK AGAINST YOUR SPEED TO SLOW DOWN AND LOOP ROUND.", new Point(cx, 418), 24, 0xFFFFA070, center: true);
        if ((int)(_clock * 2) % 2 == 0)
            Text(dc, "PRESS ENTER TO LAUNCH", new Point(cx, 476), 44, Yellow, center: true);
        Text(dc, "ARROWS: FLY    X, Z OR SPACE: FIRE    P: PAUSE    M: SOUND", new Point(cx, 526), 20, 0xFFB0A0D0, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        DrawStars(dc, _clock * 60);
        Text(dc, "THE TOP PILOTS", new Point(cx, 120), 64, 0xFFFFB040, center: true);
        Text(dc, "RANK NAME     SCORE  DREADNOUGHTS", new Point(cx, 190), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,9}   {e.Stage,6}      ", new Point(cx, 236 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 680), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        DrawStars(dc, _clock * 60);
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}

internal static class DrawingExtensions
{
    public static void FillEllipse(this DrawingContext dc, double x, double y, double r, uint colour) =>
        dc.DrawEllipse(GameCanvas.Brush(colour), null, new Point(x, y), r, r);
}

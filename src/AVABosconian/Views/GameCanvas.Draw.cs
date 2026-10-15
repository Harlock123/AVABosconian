using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVABosconian.Engine;
using Effect = AVABosconian.Engine.Effect;

namespace AVABosconian.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVABosconian/Assets/Fonts#VT323"));

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

    private const uint White = 0xFFF2F4FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A98B8, Green = 0xFF4AE06A, Cyan = 0xFF6AE0FF;

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

    /// <summary>Where a point in space is on the screen, seen from the ship.</summary>
    private Point View(double x, double y)
    {
        var s = Engine.Ship;
        var (dx, dy) = Field.Delta(s.X, s.Y, x, y);
        return new Point(Field.ViewX + dx, Field.ViewY + dy);
    }

    private static bool OnScreen(Point p, double margin = 80) =>
        p.X > Field.ViewLeft - margin && p.X < Field.ViewRight + margin && p.Y > Field.ViewTop - margin && p.Y < Field.ViewBottom + margin;

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF04050C), new Rect(0, 0, Field.Width, Field.Height));
        bool inGame = g.State is GameState.Playing or GameState.Attract or GameState.Paused or GameState.GameOver;
        if (inGame)
        {
            using (dc.PushClip(new Rect(Field.ViewLeft, Field.ViewTop, Field.ViewW, Field.ViewH)))
            {
                DrawSpace(dc);
                foreach (var e in g.Effects) DrawEffect(dc, e);
                DrawMessages(dc);
            }
            DrawPanel(dc);
        }
        else DrawStarsBehind(dc);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.ViewX, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.ViewX, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(Field.ViewX, 330), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER", new Point(Field.ViewX, Field.Height - 18), 26, Yellow, center: true);
    }

    // ───────────────────────────── space ─────────────────────────────

    /// <summary>Stars in three layers drifting past at different speeds as the ship flies.</summary>
    private void DrawStarfield(DrawingContext dc, double camX, double camY)
    {
        for (int layer = 0; layer < 3; layer++)
        {
            double k = .25 + layer * .3, tile = 256;
            double ox = camX * k, oy = camY * k;
            int tx0 = (int)Math.Floor((ox - Field.ViewW / 2) / tile), ty0 = (int)Math.Floor((oy - Field.ViewH / 2) / tile);
            for (int tx = tx0; tx <= tx0 + (int)(Field.ViewW / tile) + 1; tx++)
                for (int ty = ty0; ty <= ty0 + (int)(Field.ViewH / tile) + 1; ty++)
                    for (int n = 0; n < 4; n++)
                    {
                        uint h = Hash(tx, ty, n + layer * 7);
                        double x = tx * tile + h % 256 - ox + Field.ViewX, y = ty * tile + (h >> 8) % 256 - oy + Field.ViewY;
                        double tw = .5 + .5 * Math.Sin(_clock * 3 + h % 50);
                        uint colour = (h % 5) switch { 0 => 0xFFFFC0A0, 1 => 0xFFA0C0FF, _ => 0xFFFFFFFF };
                        dc.FillRectangle(Brush(Alpha(colour, (.25 + .3 * layer) * (.6 + .4 * tw))), new Rect(x, y, 1 + layer * .6, 1 + layer * .6));
                    }
        }
    }

    private void DrawStarsBehind(DrawingContext dc) => DrawStarfield(dc, _clock * 40, _clock * 25);

    private void DrawSpace(DrawingContext dc)
    {
        var g = Engine;
        DrawStarfield(dc, g.Ship.X, g.Ship.Y);
        foreach (var r in g.Rocks)
        {
            var p = View(r.X, r.Y);
            if (OnScreen(p)) DrawRock(dc, r, p);
        }
        foreach (var b in g.Bases)
        {
            var p = View(b.X, b.Y);
            if (OnScreen(p, 120)) DrawBase(dc, b, p);
        }
        foreach (var sh in g.Shots)
        {
            var p = View(sh.X, sh.Y);
            if (!OnScreen(p, 10)) continue;
            if (sh.Enemy)
            {
                dc.DrawEllipse(Brush(Alpha(Red, .4)), null, p, 6, 6);
                dc.DrawEllipse(Brush(0xFFFFC0A0), null, p, 2.6, 2.6);
            }
            else
            {
                double sp = Math.Sqrt(sh.Vx * sh.Vx + sh.Vy * sh.Vy);
                var tail = new Point(p.X - sh.Vx / sp * 9, p.Y - sh.Vy / sp * 9);
                dc.DrawLine(Pen(Alpha(Yellow, .4), 5), tail, p);
                dc.DrawLine(Pen(0xFFFFFFE0, 2), tail, p);
            }
        }
        foreach (var e in g.Enemies)
        {
            var p = View(e.X, e.Y);
            if (OnScreen(p, 30)) DrawEnemy(dc, e, p, 1);
        }
        var s = g.Ship;
        if (s.Alive && (s.Safe <= 0 || (int)(_clock * 12) % 2 == 0)) DrawShip(dc, new Point(Field.ViewX, Field.ViewY), Dirs.Angle(s.Dir), 1, true);
    }

    private void DrawRock(DrawingContext dc, Rock r, Point p)
    {
        if (r.Mine)
        {
            // A mine: a spiked ball with a blinking heart.
            for (int k = 0; k < 8; k++)
            {
                double a = r.Angle + k * Math.PI / 4;
                dc.DrawLine(Pen(0xFFC03A3A, 2), new Point(p.X + Math.Cos(a) * 6, p.Y + Math.Sin(a) * 6), new Point(p.X + Math.Cos(a) * 12, p.Y + Math.Sin(a) * 12));
            }
            dc.DrawEllipse(Brush(0xFF5A1010), Pen(0xFFFF6A5A, 1.6), p, 7, 7);
            if ((int)(_clock * 3 + r.X) % 2 == 0) dc.DrawEllipse(Brush(0xFFFF4A3A), null, p, 2.5, 2.5);
            return;
        }
        var pts = r.Shape.Select((k, i) =>
        {
            double a = r.Angle + i * Math.PI * 2 / r.Shape.Length;
            return new Point(p.X + Math.Cos(a) * r.R * k, p.Y + Math.Sin(a) * r.R * k);
        }).ToArray();
        Poly(dc, 0xFF3A3430, 0xFF9A8C7C, 1.6, pts);
        dc.DrawEllipse(null, Pen(0xFF5A5048, 1.2), new Point(p.X - r.R * .2, p.Y - r.R * .15), r.R * .25, r.R * .2);
    }

    /// <summary>A base: a hexagonal frame with six gun pods round a glowing core, an opening running through it along its axis.</summary>
    private void DrawBase(DrawingContext dc, Base b, Point c)
    {
        var (ax, ay) = b.Axis;
        // The frame: a hexagon, with the opening cut through it along the axis.
        double R = Base.Body + 14, gap = Base.CoreRadius + 2;
        var hex = Enumerable.Range(0, 6).Select(k => { double a = k * Math.PI / 3 + (b.Upright ? Math.PI / 6 : 0); return new Point(c.X + Math.Cos(a) * R, c.Y + Math.Sin(a) * R); }).ToArray();
        dc.DrawGeometry(Brush(0x3032C850), Pen(0xFF3AC85A, 2.2), Shape(hex));
        var along = new Vector(ax, ay) * R * .9;
        var acrossV = new Vector(-ay, ax) * gap;
        Poly(dc, 0xFF04050C, null, 0, c - along - acrossV, c + along - acrossV, c + along + acrossV, c - along + acrossV);
        foreach (int e in new[] { 1, -1 }) dc.DrawLine(Pen(0xFF2A8A40, 1.5), c - along + acrossV * e, c + along + acrossV * e);
        // The core, pulsing.
        double pulse = .5 + .5 * Math.Sin(_clock * 6 + b.X);
        dc.DrawEllipse(Brush(Alpha(0xFFFF7A3A, .25 + .2 * pulse)), null, c, Base.CoreRadius + 5, Base.CoreRadius + 5);
        dc.DrawEllipse(Brush(0xFFFF9A4A), Pen(0xFFFFE0A0, 1.5), c, Base.CoreRadius - 3, Base.CoreRadius - 3);
        // The pods.
        for (int i = 0; i < 6; i++)
        {
            var (px, py) = b.Pod(i);
            var p = new Point(c.X + px - b.X, c.Y + py - b.Y);
            if (b.Pods[i])
            {
                dc.DrawEllipse(Brush(0xFF1A6A2A), Pen(0xFF8AFFA0, 2), p, Base.PodRadius, Base.PodRadius);
                dc.DrawEllipse(Brush(0xFFC0FFD0), null, p, 3.5, 3.5);
            }
            else dc.DrawEllipse(null, Pen(0x803AC85A, 1.2), p, Base.PodRadius * .7, Base.PodRadius * .7);
        }
    }

    private static StreamGeometry Shape(Point[] pts)
    {
        var g = new StreamGeometry();
        using var c = g.Open();
        c.BeginFigure(pts[0], true);
        foreach (var p in pts.Skip(1)) c.LineTo(p);
        c.EndFigure(true);
        return g;
    }

    /// <summary>The ship: a white fighter with red wings, guns fore and aft, and an exhaust flickering behind.</summary>
    private void DrawShip(DrawingContext dc, Point c, double angle, double scale, bool flame)
    {
        double k = scale, sn = Math.Sin(angle), cs = Math.Cos(angle);
        Point P(double x, double y) => new(c.X + (x * cs - y * sn) * k, c.Y + (x * sn + y * cs) * k);
        if (flame && (int)(_clock * 30) % 2 == 0) Poly(dc, 0xFFFFB040, null, 0, P(-3, 12), P(3, 12), P(0, 19));
        Poly(dc, 0xFFD02A2A, 0xFF7A0A0A, 1.2, P(-14, 8), P(-5, -2), P(5, -2), P(14, 8), P(5, 10), P(-5, 10));
        Poly(dc, 0xFFF0F0FA, 0xFF6A6A80, 1.2, P(0, -16), P(5, -2), P(4, 12), P(-4, 12), P(-5, -2));
        dc.DrawEllipse(Brush(0xFF2AA0FF), null, P(0, -4), 2.6 * k, 4 * k);
        // The guns: one forward, one back.
        dc.DrawLine(Pen(0xFFC0C8D8, 2), P(0, -16), P(0, -20));
        dc.DrawLine(Pen(0xFFC0C8D8, 2), P(0, 12), P(0, 15));
    }

    private void DrawEnemy(DrawingContext dc, Enemy e, Point c, double scale)
    {
        double sn = Math.Sin(e.Angle), cs = Math.Cos(e.Angle), k = scale;
        Point P(double x, double y) => new(c.X + (x * cs - y * sn) * k, c.Y + (x * sn + y * cs) * k);
        uint col = GameEngine.Colour(e.Kind);
        switch (e.Kind)
        {
            case EnemyKind.Dart:
                Poly(dc, Alpha(col, .3), col, 1.8, P(0, -12), P(7, 8), P(0, 4), P(-7, 8));
                break;
            case EnemyKind.Wasp:
                Poly(dc, Alpha(col, .3), col, 1.8, P(0, -10), P(10, -2), P(4, 9), P(-4, 9), P(-10, -2));
                dc.DrawEllipse(Brush(col), null, P(0, 0), 2.5 * k, 2.5 * k);
                break;
            case EnemyKind.Raider:
                Poly(dc, Alpha(col, .3), col, 1.8, P(0, -12), P(5, -4), P(12, 6), P(4, 4), P(0, 10), P(-4, 4), P(-12, 6), P(-5, -4));
                break;
            case EnemyKind.Spy:
            {
                // A scout: a disc with spinning vanes.
                dc.DrawEllipse(Brush(Alpha(col, .25)), Pen(col, 2), c, 12 * k, 12 * k);
                for (int i = 0; i < 3; i++)
                {
                    double a = e.T * 8 + i * Math.PI * 2 / 3;
                    dc.DrawLine(Pen(0xFFFFFFFF, 1.8), new Point(c.X + Math.Cos(a) * 4, c.Y + Math.Sin(a) * 4), new Point(c.X + Math.Cos(a) * 16, c.Y + Math.Sin(a) * 16));
                }
                break;
            }
            case EnemyKind.Leader:
                Poly(dc, Alpha(0xFFFFE14A, .3), 0xFFFFFFFF, 2, P(0, -15), P(9, -3), P(15, 9), P(0, 5), P(-15, 9), P(-9, -3));
                dc.DrawEllipse(Brush(0xFFFFE14A), null, P(0, -2), 3 * k, 3 * k);
                break;
            default:
                Poly(dc, Alpha(col, .3), col, 1.6, P(0, -9), P(7, 6), P(-7, 6));
                break;
        }
    }

    private void DrawEffect(DrawingContext dc, Effect e)
    {
        double t = e.Age / e.Life;
        var c = View(e.X, e.Y);
        if (!OnScreen(c, 120)) return;
        switch (e.Kind)
        {
            case EffectKind.Burst:
            {
                double r = (8 + 30 * t) * e.Size;
                dc.DrawEllipse(null, Pen(Alpha(e.Colour, 1 - t), 2.5), c, r, r);
                for (int k = 0; k < 10; k++)
                {
                    double a = k * Math.PI / 5 + Hash(k, (int)e.X, 1) % 10 * .06, d = r * (.6 + Hash(k, 2, 3) % 10 * .06);
                    dc.FillRectangle(Brush(Alpha(0xFFFFF0C0, 1 - t)), new Rect(c.X + Math.Cos(a) * d - 2, c.Y + Math.Sin(a) * d - 2, 3.5, 3.5));
                }
                break;
            }
            case EffectKind.Blast:
            {
                double r = (20 + 90 * Math.Sqrt(t)) * e.Size;
                dc.DrawEllipse(Brush(Alpha(0xFFFFE080, (1 - t) * .6)), null, c, r * .5, r * .5);
                dc.DrawEllipse(null, Pen(Alpha(e.Colour, 1 - t), 4), c, r, r);
                dc.DrawEllipse(null, Pen(Alpha(0xFFFF8A3A, 1 - t), 2), c, r * .75, r * .75);
                for (int k = 0; k < 16; k++)
                {
                    double a = k * Math.PI / 8 + Hash(k, (int)e.Y, 5) % 10 * .05, d = r * (.8 + Hash(k, 3, 5) % 10 * .04);
                    dc.FillRectangle(Brush(Alpha(k % 2 == 0 ? 0xFFFFFFFF : e.Colour, 1 - t)), new Rect(c.X + Math.Cos(a) * d - 2.5, c.Y + Math.Sin(a) * d - 2.5, 5, 5));
                }
                break;
            }
            case EffectKind.Score:
                Text(dc, e.Text, new Point(c.X, c.Y - 14 - t * 24), 26, e.Colour, center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 30), 36, e.Colour, center: true);
                break;
        }
    }

    // ───────────────────────────── the panel and the screens ─────────────────────────────

    private static uint AlertColour(Alert a) => a switch { Alert.Green => Green, Alert.Yellow => Yellow, _ => Red };

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        Text(dc, "SCORE", new Point(16, 18), 22, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(16, 48), 36, Yellow);
        Text(dc, "HI", new Point(Field.Width - 16, 18), 22, Dim, right: true);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(Field.Width - 16, 48), 30, Dim, right: true);
        if (g.State is GameState.Playing or GameState.Attract or GameState.Paused)
            Text(dc, $"ROUND {g.Round}", new Point(Field.ViewX, 36), 38, White, center: true);
    }

    /// <summary>The panel: the alert, the radar with every base on it, and the ships to come.</summary>
    private void DrawPanel(DrawingContext dc)
    {
        var g = Engine;
        double x0 = Field.ViewRight, w = Field.Width - x0, cxp = x0 + w / 2;
        dc.FillRectangle(Brush(0xFF0A0C1A), new Rect(x0, Field.ViewTop, w, Field.ViewH));
        dc.DrawLine(Pen(0xFF2A3A6A, 2), new Point(x0, Field.ViewTop), new Point(x0, Field.ViewBottom));
        // The alert.
        uint ac = AlertColour(g.Alert);
        bool flash = g.Alert == Alert.Red && (int)(_clock * 4) % 2 == 0;
        var box = new Rect(cxp - 100, 86, 200, 54);
        dc.DrawRectangle(Brush(flash ? Alpha(ac, .35) : 0xFF0A0C1A), Pen(ac, 2.5), box, 4);
        Text(dc, "CONDITION", new Point(cxp, 102), 22, Dim, center: true);
        Text(dc, g.Alert.ToString().ToUpperInvariant(), new Point(cxp, 124), 30, ac, center: true);
        // The radar: all of space, the bases as green blocks and the ship as a white dot.
        double rs = 216, rx = cxp - rs / 2, ry = 170;
        dc.DrawRectangle(Brush(0xFF02060A), Pen(0xFF2A6A4A, 2), new Rect(rx, ry, rs, rs));
        double k = rs / Field.World;
        foreach (var b in g.Bases)
        {
            double bx = rx + b.X * k, by = ry + b.Y * k;
            dc.FillRectangle(Brush(Green), new Rect(bx - 4, by - 4, 8, 8));
        }
        // The view's edges, and the ship.
        var s = g.Ship;
        dc.DrawRectangle(null, Pen(0x60FFFFFF, 1), new Rect(rx + (s.X - Field.ViewW / 2) * k, ry + (s.Y - Field.ViewH / 2) * k, Field.ViewW * k, Field.ViewH * k));
        if ((int)(_clock * 4) % 2 == 0 || !s.Alive) dc.FillRectangle(Brush(0xFFFFFFFF), new Rect(rx + s.X * k - 2.5, ry + s.Y * k - 2.5, 5, 5));
        foreach (var e in g.Enemies.Where(e => e.Kind is EnemyKind.Spy or EnemyKind.Leader))
            dc.FillRectangle(Brush(e.Kind == EnemyKind.Spy ? Cyan : Red), new Rect(rx + e.X * k - 2, ry + e.Y * k - 2, 4, 4));
        Text(dc, $"BASES LEFT  {g.Bases.Count}", new Point(cxp, ry + rs + 24), 26, White, center: true);
        Text(dc, "SHIPS", new Point(cxp, ry + rs + 70), 26, Dim, center: true);
        int spare = Math.Max(0, g.Lives - (s.Alive ? 1 : 0));
        for (int i = 0; i < Math.Min(spare, 8); i++)
            DrawShip(dc, new Point(cxp - 66 + i % 4 * 44, ry + rs + 110 + i / 4 * 44), 0, .9, false);
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = Field.ViewX, cy = Field.ViewY - 150;
        var (w, h) = (Math.Min(Field.ViewW - 40, Math.Max(msg.Length * 28, (g.SubMessage?.Length ?? 0) * 15) + 60), g.SubMessage != null ? 110.0 : 74.0);
        dc.FillRectangle(Brush(0xB0060812), new Rect(cx - w / 2, cy - 48, w, h), 6);
        uint colour = msg.Contains("RED") || msg.Contains("ALERT") || msg.Contains("ESCAPED") ? Red : msg.Contains("YELLOW") ? Yellow : g.Phase == Phase.Dying ? Red : Yellow;
        Text(dc, msg, new Point(cx, cy - 12), msg.Length > 16 ? 46 : 56, colour, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 36), 28, White, center: true);
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        Text(dc, "AVA BOSCONIAN", new Point(cx, 112), 110, Cyan, center: true);
        Text(dc, "AFTER THE 1981 ARCADE CLASSIC", new Point(cx, 180), 28, Dim, center: true);
        DrawBase(dc, new Base { X = 0, Y = 0, Upright = true }, new Point(cx - 230, 290));
        DrawShip(dc, new Point(cx - 230, 400), 0, 1.4, true);
        dc.DrawLine(Pen(Alpha(Yellow, .7), 2), new Point(cx - 230, 375), new Point(cx - 230, 312));
        string[] lines =
        [
            "DESTROY EVERY BASE: SHOOT ALL SIX PODS,",
            "OR FIRE STRAIGHT DOWN THE OPENING INTO THE CORE",
            "YOUR GUNS FIRE FORE AND AFT AT ONCE",
            "DON'T LET THE SPY SHIP GET AWAY",
        ];
        for (int i = 0; i < lines.Length; i++) Text(dc, lines[i], new Point(cx + 110, 252 + i * 32), 25, White, center: true);
        (EnemyKind Kind, string Name, string Points)[] table =
        [
            (EnemyKind.Dart, "DART", "50"), (EnemyKind.Wasp, "WASP", "60"), (EnemyKind.Raider, "RAIDER", "70"),
            (EnemyKind.Spy, "SPY", "800"), (EnemyKind.Leader, "LEADER", "500-1500"),
        ];
        for (int i = 0; i < table.Length; i++)
        {
            double x = cx - 320 + i * 160;
            DrawEnemy(dc, new Enemy { Kind = table[i].Kind, T = _clock }, new Point(x, 462), 1.6);
            Text(dc, table[i].Name, new Point(x, 500), 24, GameEngine.Colour(table[i].Kind), center: true);
            Text(dc, table[i].Points, new Point(x, 526), 26, Yellow, center: true);
        }
        Text(dc, "POD 100   BASE 1500   ASTEROID 10   MINE 20", new Point(cx, 566), 26, Dim, center: true);
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER OR START", new Point(cx, 630), 46, Yellow, center: true);
        Text(dc, "ARROWS: STEER (EIGHT WAYS)    SPACE: FIRE", new Point(cx, 690), 26, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "THE TOP PILOTS", new Point(cx, 130), 72, Cyan, center: true);
        Text(dc, "RANK NAME    SCORE   ROUND", new Point(cx, 196), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4} ", new Point(cx, 240 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 700), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
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

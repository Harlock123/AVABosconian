namespace AVABosconian.Engine;

/// <summary>
/// The computer pilot, for the demonstration (and the tests). Each moment it weighs the eight headings:
/// how well each takes it where it wants to go, how soon it would meet an asteroid, a mine, a base, a fighter
/// or a shot along it, and whether it puts an enemy in line with the guns fore or aft. Where it wants to go is
/// the nearest base: out to a point on the line of its opening, then a run straight down it, guns blazing at
/// the core, veering off at the last moment. A spy ship in sight comes first.
/// </summary>
public sealed class ShipPilot
{
    /// <summary>What it is up to, for the probe.</summary>
    public string Doing { get; private set; } = "";

    public Controls Decide(GameEngine g)
    {
        var s = g.Ship;
        if (!s.Alive || g.Phase is not (Phase.Play or Phase.Start)) return default;
        var (gx, gy) = Goal(g);
        double gl = Math.Max(1e-6, Math.Sqrt(gx * gx + gy * gy));
        (gx, gy) = (gx / gl, gy / gl);

        int best = s.Dir;
        double bestScore = double.MinValue;
        for (int d = 0; d < 8; d++)
        {
            var (ux, uy) = Dirs.Vec(d);
            int steps = Math.Min(((d - s.Dir) % 8 + 8) % 8, ((s.Dir - d) % 8 + 8) % 8);
            double score = (ux * gx + uy * gy) * 3 - steps * .12;
            score -= Danger(g, d, steps) * 12;
            score += InLine(g, ux, uy) * .8;
            if (score > bestScore) { bestScore = score; best = d; }
        }
        var (bx, by) = Dirs.Vec(best);
        return new Controls(Math.Sign(Math.Round(bx * 2)), Math.Sign(Math.Round(by * 2)), Fire: true);
    }

    /// <summary>Which way it wants to go.</summary>
    private (double X, double Y) Goal(GameEngine g)
    {
        var s = g.Ship;
        // A spy in sight: after it.
        if (g.Enemies.FirstOrDefault(e => e.Kind == EnemyKind.Spy && Field.Distance(e.X, e.Y, s.X, s.Y) < 520) is { } spy)
        {
            Doing = "spy";
            var (dx, dy) = Field.Delta(s.X, s.Y, spy.X, spy.Y);
            return (dx + Math.Sin(spy.Angle) * 60, dy - Math.Cos(spy.Angle) * 60);
        }
        var target = g.Bases.OrderBy(b => Field.Distance(b.X, b.Y, s.X, s.Y)).FirstOrDefault();
        if (target == null) { Doing = "cruise"; var (vx, vy) = Dirs.Vec(s.Dir); return (vx, vy); }
        var (tx, ty) = Field.Delta(s.X, s.Y, target.X, target.Y);
        var (ax, ay) = target.Axis;
        // Where the ship is along the opening's line, and how far off it.
        double along = -(tx * ax + ty * ay), off = Math.Abs(ty * ax - tx * ay);
        int side = along >= 0 ? 1 : -1;
        var (hx, hy) = Dirs.Vec(s.Dir);
        bool headingIn = (hx * -ax * side + hy * -ay * side) > .9;
        if (off < 12 && Math.Abs(along) > 120 && Math.Abs(along) < 560 && headingIn)
        {
            Doing = "run";
            return (tx, ty);
        }
        if (Math.Abs(along) <= 130 && off < 70)
        {
            // Too close: off sideways, away from the line.
            Doing = "veer";
            double k = tx * ay - ty * ax >= 0 ? 1 : -1;
            return (-ay * k, ax * k);
        }
        if (off < 70 && Math.Abs(along) > 140)
        {
            // Near the line: onto it, a little nearer the base each moment, so it lines up exactly.
            Doing = "close in";
            double ahead = Math.Abs(along) - 20;
            return (tx + ax * side * ahead, ty + ay * side * ahead);
        }
        // Out to a point on the line of the opening, well clear of the base.
        double px = tx + ax * side * 320, py = ty + ay * side * 320;
        Doing = "line up";
        return (px, py);
    }

    /// <summary>How soon trouble lies along a heading (0 for none, nearer 1 the sooner).</summary>
    private static double Danger(GameEngine g, int dir, int steps)
    {
        var s = g.Ship;
        var (ux, uy) = Dirs.Vec(dir);
        var (cx, cy) = Dirs.Vec(s.Dir);
        double turn = steps * GameEngine.TurnEvery;
        double worst = 0;
        for (double t = .1; t <= 1.2; t += .1)
        {
            // Along its present heading while it turns, then along the new one.
            double t0 = Math.Min(t, turn), t1 = t - t0;
            double x = s.X + (cx * t0 + ux * t1) * GameEngine.ShipSpeed, y = s.Y + (cy * t0 + uy * t1) * GameEngine.ShipSpeed;
            double weight = 1.3 - t;
            foreach (var r in g.Rocks)
                if (Field.Distance(x, y, r.X, r.Y) < r.R + (r.Mine ? 22 : 16)) worst = Math.Max(worst, weight);
            // Bases only matter close to: a run at one veers off in good time anyway.
            if (t <= .45)
                foreach (var b in g.Bases)
                    if (Field.Distance(x, y, b.X, b.Y) < Base.Body + 20) worst = Math.Max(worst, weight);
            foreach (var e in g.Enemies)
            {
                double ex = e.X + Math.Sin(e.Angle) * e.Speed * t, ey = e.Y - Math.Cos(e.Angle) * e.Speed * t;
                if (Field.Distance(x, y, ex, ey) < 30) worst = Math.Max(worst, weight * .8);
            }
            foreach (var sh in g.Shots)
            {
                if (!sh.Enemy) continue;
                if (Field.Distance(x, y, sh.X + sh.Vx * t, sh.Y + sh.Vy * t) < 18) worst = Math.Max(worst, weight);
            }
        }
        return worst;
    }

    /// <summary>Whether a heading puts an enemy in line with a gun, fore or aft.</summary>
    private static double InLine(GameEngine g, double ux, double uy)
    {
        var s = g.Ship;
        double bonus = 0;
        foreach (var e in g.Enemies)
        {
            var (dx, dy) = Field.Delta(s.X, s.Y, e.X, e.Y);
            double along = dx * ux + dy * uy, across = Math.Abs(dx * uy - dy * ux);
            if (Math.Abs(along) < 330 && Math.Abs(along) > 30 && across < 14) bonus = Math.Max(bonus, e.Kind is EnemyKind.Spy or EnemyKind.Leader ? 2 : 1);
        }
        return bonus;
    }
}

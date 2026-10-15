namespace AVABosconian.Engine;

/// <summary>The stick (eight ways) and the fire button.</summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Fire = false);

/// <summary>
/// The screen and the space it shows. Space is a square that wraps round at its edges; the view on the left
/// of the screen follows the ship, and the panel on the right holds the score and the radar.
/// </summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const double ViewLeft = 0, ViewRight = 760, ViewTop = 64, ViewBottom = 768;
    public const double ViewW = ViewRight - ViewLeft, ViewH = ViewBottom - ViewTop;
    public const double ViewX = (ViewLeft + ViewRight) / 2, ViewY = (ViewTop + ViewBottom) / 2;
    /// <summary>The size of space, each way.</summary>
    public const double World = 3600;

    public static double Wrap(double v) => ((v % World) + World) % World;

    /// <summary>The shortest way from a to b across wrapping space.</summary>
    public static (double X, double Y) Delta(double ax, double ay, double bx, double by)
    {
        double dx = bx - ax, dy = by - ay;
        if (dx > World / 2) dx -= World; else if (dx < -World / 2) dx += World;
        if (dy > World / 2) dy -= World; else if (dy < -World / 2) dy += World;
        return (dx, dy);
    }

    public static double Distance(double ax, double ay, double bx, double by)
    {
        var (dx, dy) = Delta(ax, ay, bx, by);
        return Math.Sqrt(dx * dx + dy * dy);
    }
}

/// <summary>The eight ways the ship can head, numbered clockwise from up.</summary>
public static class Dirs
{
    public static (double X, double Y) Vec(int d)
    {
        double a = Angle(d);
        return (Math.Sin(a), -Math.Cos(a));
    }

    /// <summary>The heading as an angle, clockwise from up.</summary>
    public static double Angle(int d) => d * Math.PI / 4;

    /// <summary>The heading the stick points (or -1 if it's centred).</summary>
    public static int Of(int dx, int dy) => (dx, dy) switch
    {
        (0, -1) => 0, (1, -1) => 1, (1, 0) => 2, (1, 1) => 3, (0, 1) => 4, (-1, 1) => 5, (-1, 0) => 6, (-1, -1) => 7, _ => -1,
    };

    /// <summary>The heading nearest a direction.</summary>
    public static int Nearest(double dx, double dy) => ((int)Math.Round(Math.Atan2(dx, -dy) / (Math.PI / 4)) % 8 + 8) % 8;
}

public sealed class Ship
{
    public double X, Y;
    public int Dir;
    public bool Alive = true;
    /// <summary>A moment after launching when nothing can hurt it.</summary>
    public double Safe;
    public double FireCool, TurnCool;
    public (double X, double Y) Velocity(double speed) { var (x, y) = Dirs.Vec(Dir); return (x * speed, y * speed); }
}

public sealed class Shot
{
    public double X, Y, Vx, Vy, Age;
    public bool Dead, Enemy;
}

/// <summary>
/// An enemy base: six gun pods round a core. Shoot every pod, or put a shot into the core through the
/// opening that runs through the base along its axis, and the whole base goes up.
/// </summary>
public sealed class Base
{
    public const double PodRing = 40, PodRadius = 11, CoreRadius = 13, Body = 50;

    public double X, Y;
    /// <summary>Whether the opening runs up and down (otherwise across).</summary>
    public bool Upright;
    public readonly bool[] Pods = [true, true, true, true, true, true];
    public bool Dead;
    public double FireIn, LaunchIn;

    /// <summary>The way the opening runs, as a unit vector.</summary>
    public (double X, double Y) Axis => Upright ? (0, 1) : (1, 0);

    /// <summary>Where a pod sits: three each side of the opening.</summary>
    public (double X, double Y) Pod(int i)
    {
        double a = (Upright ? 0 : Math.PI / 2) + (i < 3 ? Math.PI / 4 * (i + 1) : Math.PI + Math.PI / 4 * (i - 2));
        return (X + Math.Sin(a) * PodRing, Y - Math.Cos(a) * PodRing);
    }
}

public enum EnemyKind
{
    /// <summary>Quick, light fighters that come at the ship head on.</summary>
    Dart,
    /// <summary>Nimble fighters that turn hard.</summary>
    Wasp,
    /// <summary>Heavier fighters that shoot.</summary>
    Raider,
    /// <summary>A scout that flies past: let it get away and the alert goes to red.</summary>
    Spy,
    /// <summary>The leader of a squadron, with four wingmen flying in formation on it.</summary>
    Leader,
    Wing,
}

public sealed class Enemy
{
    public EnemyKind Kind;
    public double X, Y, Angle, Speed, T, FireIn;
    public bool Dead;
    public Enemy? Leader;
    /// <summary>A wingman's place in the formation, relative to its leader's heading.</summary>
    public double Slot;
}

/// <summary>An asteroid, or a mine (which goes off, taking anything near with it).</summary>
public sealed class Rock
{
    public double X, Y, R, Spin, Angle;
    public bool Mine, Dead;
    public double[] Shape = [];
}

public enum Alert { Green, Yellow, Red }

public enum EffectKind { Score, Burst, Word, Blast }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 0.7, Size = 1;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}

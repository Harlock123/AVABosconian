using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVABosconian.Engine;
using AVABosconian.Views;

namespace AVABosconian.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set BOSCO_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avabosconian-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("BOSCO_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVABosconian-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            g.Tick(g.Pilot.Decide(g));
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }

    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("ACE", 84250, 7);
        g.HighScores.Insert("AVA", 52800, 5);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        Shot(w, c, "high-scores");
    }

    [AvaloniaFact]
    public void Flying_and_fighting()
    {
        var (w, c, g) = Open(2);
        g.StartGame();
        g.SetLives(9);
        g.Idle(60);
        c.Advance(.5);
        Shot(w, c, "round-start");
        bool Near(GameEngine g, double d) => g.Bases.Any(b => Field.Distance(b.X, b.Y, g.Ship.X, g.Ship.Y) < d);
        Assert.True(PlayUntil(g, c, g => g.Ship.Alive && Near(g, 300) && g.Enemies.Count >= 1 && g.Shots.Any(s => !s.Enemy), 200));
        Shot(w, c, "play");
        Assert.True(PlayUntil(g, c, g => g.Pilot.Doing == "run" && Near(g, 230), 200));
        Shot(w, c, "run");
        Assert.True(PlayUntil(g, c, g => g.Effects.Any(e => e.Kind == EffectKind.Blast && e.Size > 1.5 && e.Age > .25), 200));
        Shot(w, c, "base-down");
    }

    [AvaloniaFact]
    public void Condition_red()
    {
        var (w, c, g) = Open(3);
        g.Go(3);
        g.SetLives(9);
        g.SetRoundTime(GameEngine.RedAfter - 1);
        g.SquadronForTest();
        var lead = g.Enemies.Single(e => e.Kind == EnemyKind.Leader);
        (lead.X, lead.Y, lead.Angle) = (g.Ship.X + 220, g.Ship.Y - 200, Math.PI * 1.25);
        Assert.True(PlayUntil(g, c, g => g.Alert == Alert.Red && g.Message == null && g.Enemies.Count(e => e.Kind == EnemyKind.Wing) >= 3
                                       && g.Enemies.Any(e => e.Kind == EnemyKind.Leader && Field.Distance(e.X, e.Y, g.Ship.X, g.Ship.Y) < 300), 60));
        Shot(w, c, "condition-red");
        var spy = g.ReleaseSpy(g.Ship.X - 300, g.Ship.Y - 120, Math.PI / 2);
        Assert.True(PlayUntil(g, c, g => Field.Distance(spy.X, spy.Y, g.Ship.X, g.Ship.Y) < 180 || spy.Dead, 30));
        Shot(w, c, "spy");
    }

    [AvaloniaFact]
    public void A_ship_lost_and_a_round_cleared()
    {
        var (w, c, g) = Open();
        g.Go(1);
        g.SetLives(3);
        Assert.True(PlayUntil(g, c, g => g.RoundTime > 4 && g.Ship.Alive));
        g.Ship.Safe = 0;
        g.PutRock(g.Ship.X, g.Ship.Y - 30, mine: true);
        for (int t = 0; t < 30; t++) { g.Tick(default); c.Advance(1 / 60.0); }
        Shot(w, c, "lost");
        g.Go(1);
        g.SetLives(9);
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Clear && g.PhaseTime > .5, 200));
        Shot(w, c, "clear");
    }
}

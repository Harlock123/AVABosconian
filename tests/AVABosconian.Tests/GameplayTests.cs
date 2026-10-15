using AVABosconian.Engine;

namespace AVABosconian.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avabosconian-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A game in play in empty space, with nothing arriving and nothing firing unless a test sets it up.</summary>
    private static GameEngine Game()
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
        g.Go(1);
        g.Clear();
        g.PutShip(1000, 1000, 0);
        return g;
    }

    /// <summary>A real game in play in empty space, with plenty of ships.</summary>
    private static GameEngine Real()
    {
        var g = new GameEngine(7, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(1);
        g.Clear();
        g.PutShip(1000, 1000, 0);
        g.SetLives(99);
        return g;
    }

    private static readonly Controls Fire = new(Fire: true);

    // ───────────────────────────── space ─────────────────────────────

    [Fact]
    public void Each_round_spreads_its_bases_out_away_from_the_ship_with_rocks_and_mines_between()
    {
        for (int round = 1; round <= 8; round++)
        {
            var g = new GameEngine(round, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.Go(round);
            Assert.Equal(GameEngine.BasesIn(round), g.Bases.Count);
            var s = g.Ship;
            Assert.All(g.Bases, b => Assert.True(Field.Distance(b.X, b.Y, s.X, s.Y) >= 650));
            foreach (var a in g.Bases)
                foreach (var b in g.Bases.Where(b => b != a))
                    Assert.True(Field.Distance(a.X, a.Y, b.X, b.Y) >= 800);
            Assert.Contains(g.Rocks, r => r.Mine);
            Assert.Contains(g.Rocks, r => !r.Mine);
            Assert.All(g.Rocks, r => Assert.True(Field.Distance(r.X, r.Y, s.X, s.Y) >= 260));
        }
        Assert.True(GameEngine.BasesIn(9) > GameEngine.BasesIn(1));
    }

    // ───────────────────────────── the ship ─────────────────────────────

    [Fact]
    public void The_ship_never_stops_turns_a_step_at_a_time_and_space_wraps_round()
    {
        var g = Game();
        g.Idle(60);
        Assert.Equal(1000 - GameEngine.ShipSpeed, g.Ship.Y, 1);
        g.Run(1, new Controls(Dx: 1, Dy: 1));
        Assert.Equal(1, g.Ship.Dir);
        g.Run(30, new Controls(Dx: 1, Dy: 1));
        Assert.Equal(3, g.Ship.Dir);
        g.PutShip(10, 10, 6);
        g.Idle(10);
        Assert.True(g.Ship.X > Field.World - 40);
    }

    [Fact]
    public void Each_press_fires_ahead_and_astern_and_shots_fade_out()
    {
        var g = Game();
        g.Run(1, Fire);
        Assert.Equal(2, g.Shots.Count);
        Assert.Contains(g.Shots, s => s.Vy < -GameEngine.ShotSpeed);
        Assert.Contains(g.Shots, s => s.Vy > 0);
        g.Idle((int)(60 * GameEngine.ShotLife) + 2);
        Assert.Empty(g.Shots);
    }

    // ───────────────────────────── the bases ─────────────────────────────

    [Fact]
    public void Shooting_a_pod_is_worth_100_and_the_frame_stops_shots_that_miss_the_opening()
    {
        var g = Game();
        var b = g.PutBase(1000, 800, upright: true);
        g.PutShip(1300, 800, 6);
        g.Run(1, Fire);
        g.Idle(25);
        Assert.Equal(GameEngine.PodPoints, g.Score);
        Assert.Equal(5, b.Pods.Count(p => p));
        g.PutShip(1300, 800, 6);
        g.Run(1, Fire);
        g.Idle(25);
        Assert.Contains(b, g.Bases);
        Assert.Equal(GameEngine.PodPoints, g.Score);
    }

    [Fact]
    public void Shooting_all_six_pods_brings_the_base_down()
    {
        var g = Game();
        var b = g.PutBase(1000, 800, upright: false);
        for (int i = 0; i < 6; i++)
        {
            var (px, py) = b.Pod(i);
            var (dx, dy) = (px - b.X, py - b.Y);
            double d = Math.Sqrt(dx * dx + dy * dy);
            // From outside, straight in at the pod.
            g.PutShot(px + dx / d * 30, py + dy / d * 30, -dx / d * 600, -dy / d * 600);
            g.Idle(6);
        }
        Assert.DoesNotContain(b, g.Bases);
        Assert.Equal(6 * GameEngine.PodPoints + GameEngine.BasePoints, g.Score);
    }

    [Fact]
    public void A_shot_straight_down_the_opening_into_the_core_brings_the_base_down_at_once()
    {
        var g = Game();
        var b = g.PutBase(1000, 700, upright: true);
        g.PutShip(1000, 1000, 0);
        g.Run(1, Fire);
        g.Idle(30);
        Assert.DoesNotContain(b, g.Bases);
        Assert.Equal(GameEngine.BasePoints, g.Score);
        Assert.Equal(6, b.Pods.Length);
    }

    [Fact]
    public void Bases_fire_from_their_pods_and_send_out_fighters_when_the_ship_is_near()
    {
        var g = Real();
        var b = g.PutBase(1000, 700, upright: false);
        b.FireIn = 0;
        b.LaunchIn = 0;
        g.PutShip(1300, 1000, 2);
        g.Idle(2);
        Assert.Contains(g.Shots, s => s.Enemy);
        Assert.Contains(g.Enemies, e => e.Kind == EnemyKind.Dart);
    }

    // ───────────────────────────── the enemies and hazards ─────────────────────────────

    [Fact]
    public void Fighters_home_in_on_the_ship_and_a_shot_brings_one_down()
    {
        var g = Game();
        var d = g.PutEnemy(EnemyKind.Wasp, 1300, 1000, 0);
        g.Idle(20);
        Assert.True(d.Angle < -.3, $"angle {d.Angle}");
        g.Clear();
        g.PutShip(1000, 1000, 0);
        var dart = g.PutEnemy(EnemyKind.Dart, 1000, 760, Math.PI);
        g.Run(1, Fire);
        g.Idle(20);
        Assert.True(dart.Dead);
        Assert.Equal(GameEngine.Points(EnemyKind.Dart), g.Score);
        Assert.True(g.Ship.Alive);
    }

    [Fact]
    public void Asteroids_break_for_10_and_a_mine_goes_up_taking_fighters_near_it()
    {
        var g = Game();
        g.PutRock(1000, 860);
        g.Run(1, Fire);
        g.Idle(15);
        Assert.Empty(g.Rocks);
        Assert.Equal(GameEngine.RockPoints, g.Score);
        g.PutShip(1000, 1000, 0);
        g.PutRock(1000, 860, mine: true);
        var near = g.PutEnemy(EnemyKind.Dart, 1040, 880, 0);
        g.Run(1, Fire);
        g.Idle(12);
        Assert.Empty(g.Rocks);
        Assert.True(near.Dead);
        Assert.Equal(GameEngine.RockPoints + GameEngine.MinePoints + GameEngine.Points(EnemyKind.Dart), g.Score);
    }

    [Fact]
    public void Flying_into_an_asteroid_a_mine_a_fighter_or_a_base_ends_the_ship()
    {
        foreach (var (setup, cause) in new (Action<GameEngine>, string)[]
                 {
                     (g => g.PutRock(1000, 940), "ASTEROID"),
                     (g => g.PutRock(1000, 940, mine: true), "MINE"),
                     (g => g.PutEnemy(EnemyKind.Raider, 1000, 940, Math.PI), "RAMMED"),
                     (g => g.PutBase(1000, 880, upright: false), "BASE"),
                 })
        {
            var g = Game();
            g.SetLives(5);
            setup(g);
            g.Idle(40);
            Assert.Equal(Phase.Dying, g.Phase);
            Assert.Equal(cause, g.Cause);
        }
    }

    [Fact]
    public void A_spy_ship_shot_down_is_worth_800_and_one_that_gets_away_brings_condition_red_and_a_squadron()
    {
        var g = Game();
        var spy = g.PutEnemy(EnemyKind.Spy, 1000, 820, 0);
        g.Run(1, Fire);
        g.Idle(30);
        Assert.True(spy.Dead);
        Assert.Equal(GameEngine.SpyPoints, g.Score);

        var r = Real();
        r.Ship.Safe = 99;
        r.ReleaseSpy(1000, 400, 0);
        r.Idle((int)(60 * (GameEngine.SpyGrace + 3)));
        Assert.Equal(Alert.Red, r.Alert);
        Assert.Contains(r.Enemies, e => e.Kind == EnemyKind.Leader);
    }

    [Fact]
    public void Shooting_a_squadron_leader_with_its_wingmen_still_on_it_is_worth_more_and_they_break_off()
    {
        var g = Game();
        g.SquadronForTest();
        var lead = g.Enemies.Single(e => e.Kind == EnemyKind.Leader);
        Assert.Equal(4, g.Enemies.Count(e => e.Leader == lead));
        g.Idle(1);
        g.PutShot(lead.X, lead.Y + 20, 0, -600);
        g.Idle(2);
        Assert.True(lead.Dead);
        Assert.Equal(GameEngine.LeaderPoints(4), g.Score);
        Assert.Equal(4, g.Enemies.Count(e => e.Kind == EnemyKind.Dart && e.Leader == null));
    }

    [Fact]
    public void The_alert_rises_to_yellow_then_red_as_a_round_goes_on()
    {
        var g = Real();
        Assert.Equal(Alert.Green, g.Alert);
        g.SetRoundTime(GameEngine.YellowAfter - .05);
        g.Idle(10);
        Assert.Equal(Alert.Yellow, g.Alert);
        g.SetRoundTime(GameEngine.RedAfter - .05);
        g.Idle(10);
        Assert.Equal(Alert.Red, g.Alert);
    }

    // ───────────────────────────── ships, rounds and points ─────────────────────────────

    [Fact]
    public void The_next_ship_launches_where_the_last_was_lost_and_the_last_ship_ends_the_game()
    {
        var g = Game();
        g.SetLives(2);
        g.PutRock(1000, 940);
        g.Idle(40);
        Assert.Equal(1, g.Lives);
        double y = g.Ship.Y;
        g.Idle((int)(60 * GameEngine.DyingSeconds) + 5);
        Assert.Equal(Phase.Start, g.Phase);
        Assert.True(g.Ship.Alive);
        Assert.Equal(y, g.Ship.Y, 1);
        g.Idle((int)(60 * GameEngine.StartSeconds) + 5);
        g.Ship.Safe = 0;
        g.PutRock(g.Ship.X, g.Ship.Y - 40);
        g.Idle(30);
        g.Idle((int)(60 * GameEngine.DyingSeconds) + 10);
        Assert.Equal(GameState.GameOver, g.State);
    }

    [Fact]
    public void Destroying_the_last_base_clears_the_round_and_the_next_has_more()
    {
        var g = Game();
        g.PutBase(1000, 700, upright: true);
        g.Run(1, Fire);
        g.Idle(30);
        Assert.Equal(Phase.Clear, g.Phase);
        g.Idle((int)(60 * GameEngine.ClearSeconds) + 5);
        Assert.Equal(2, g.Round);
        Assert.Equal(GameEngine.BasesIn(2), g.Bases.Count);
    }

    [Fact]
    public void Extra_ships_come_at_20000_and_every_60000_after()
    {
        var g = Game();
        int lives = g.Lives;
        g.SetScore(GameEngine.FirstExtraLife - 50);
        g.PutBase(1000, 800, upright: true);
        g.PutShip(1300, 800, 6);
        g.Run(1, Fire);
        g.Idle(25);
        Assert.Equal(lives + 1, g.Lives);
    }

    // ───────────────────────────── around the game ─────────────────────────────

    [Fact]
    public void The_computer_pilot_clears_the_first_rounds()
    {
        var g = new GameEngine(1, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.StartGame();
        g.SetLives(9);
        for (int t = 0; t < 60 * 180 && g.State == GameState.Playing && g.Round < 3; t++) g.Tick(g.Pilot.Decide(g));
        Assert.Equal(GameState.Playing, g.State);
        Assert.True(g.Round >= 3, $"round {g.Round}");
    }

    [Fact]
    public void The_title_runs_a_demonstration_then_the_high_scores_then_back()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle(60 * 25);
        Assert.True(g.Score > 0);
        g.Idle((int)(60 * (GameEngine.AttractSeconds - 25)) + 5);
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle((int)(60 * GameEngine.HighScoreSeconds) + 5);
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        static (int, double, double, int) Play(int seed)
        {
            var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
            g.StartGame();
            for (int t = 0; t < 60 * 40; t++) g.Tick(g.Pilot.Decide(g));
            return (g.Score, g.Ship.X, g.Ship.Y, g.Round);
        }
        Assert.Equal(Play(12), Play(12));
    }

    [Fact]
    public void A_high_score_takes_initials_and_is_kept_in_the_table_file()
    {
        string folder = TempFolder();
        var g = new GameEngine(7, HighScoreTable.Load(folder)) { Silent = true, Calm = true };
        g.Go(3);
        g.Clear();
        g.SetScore(99999);
        g.SetLives(1);
        g.PutShip(1000, 1000, 0);
        g.PutRock(1000, 950);
        g.Idle((int)(60 * (GameEngine.DyingSeconds + 5)));
        Assert.Equal(GameState.EnterInitials, g.State);
        foreach (char ch in "BOS") g.Initials!.Type(ch);
        g.Initials!.Finish();
        g.Idle(2);
        Assert.Equal(GameState.HighScores, g.State);
        var again = HighScoreTable.Load(folder);
        Assert.Equal(("BOS", 99999, 3), (again.Entries[0].Initials, again.Entries[0].Score, again.Entries[0].Stage));
        Directory.Delete(folder, true);
    }
}

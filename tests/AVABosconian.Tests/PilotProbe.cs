using AVABosconian.Engine;

namespace AVABosconian.Tests;

/// <summary>How the computer pilot gets on, round by round (set BOSCO_PROBE to run it, for tuning).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_plays()
    {
        string? mode = Environment.GetEnvironmentVariable("BOSCO_PROBE");
        if (mode is null) return;
        foreach (int seed in new[] { 1, 2, 3 })
        {
            var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "bosco-probe-" + Guid.NewGuid().ToString("N")))) { Silent = true };
            g.StartGame();
            g.SetLives(99);
            var deaths = new Dictionary<string, int>();
            var rounds = new List<string>();
            var doing = new Dictionary<string, int>();
            var last = g.Phase;
            int round = 1, t = 0;
            double start = 0;
            for (; t < 60 * 900 && g.Round <= 6; t++)
            {
                var c = g.Pilot.Decide(g);
                g.Tick(c);
                if (g.Phase == Phase.Play) doing[g.Pilot.Doing] = doing.GetValueOrDefault(g.Pilot.Doing) + 1;
                if (g.Phase == Phase.Dying && last != Phase.Dying) deaths[$"R{g.Round} {g.Cause}"] = deaths.GetValueOrDefault($"R{g.Round} {g.Cause}") + 1;
                if (g.Round != round) { rounds.Add($"R{round}:{(t - start) / 60:0}s"); round = g.Round; start = t; }
                last = g.Phase;
            }
            output.WriteLine($"seed {seed}: round {g.Round} after {t / 60:0}s score {g.Score} bases left {g.Bases.Count} alert {g.Alert} | {string.Join(" ", rounds)}");
            output.WriteLine("   deaths " + string.Join(", ", deaths.Select(d => $"{d.Key} x{d.Value}")));
            output.WriteLine("   doing " + string.Join(", ", doing.OrderByDescending(d => d.Value).Select(d => $"{d.Key} {d.Value / 60}s")));
        }
    }
}

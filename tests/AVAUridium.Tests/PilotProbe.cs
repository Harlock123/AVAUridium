using AVAUridium.Engine;

namespace AVAUridium.Tests;

/// <summary>
/// How the computer pilot gets on over each dreadnought (set URIDIUM_PROBE to run it, for tuning: "1" for the
/// pilot, "lazy" for a pilot who only flies on along the middle, firing).
/// </summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_pilot_does()
    {
        string? mode = Environment.GetEnvironmentVariable("URIDIUM_PROBE");
        if (mode is null) return;
        var seeds = (Environment.GetEnvironmentVariable("URIDIUM_SEEDS") ?? "1,2,3").Split(',').Select(int.Parse);
        var dreads = (Environment.GetEnvironmentVariable("URIDIUM_DREADS") ?? "0,1,2,3,4,5,6,7").Split(',').Select(int.Parse);
        foreach (int seed in seeds)
        foreach (int index in dreads)
        {
            var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "uridium-probe-" + Guid.NewGuid().ToString("N"))), seed) { Silent = true };
            g.Go(index);
            g.SetLives(30);
            g.Pilot.Reset();
            var deaths = new Dictionary<string, int>();
            var doing = new Dictionary<string, int>();
            int t = 0, pods = g.World.PodsLeft;
            double landAt = 0;
            var last = g.Phase;
            for (; t < 60 * 600 && g.State == GameState.Playing && g.Cleared == 0; t++)
            {
                var c = mode == "lazy" ? new Controls(1, 0, true) : g.Pilot.Decide(g);
                g.Tick(c);
                doing[g.Pilot.Doing] = doing.GetValueOrDefault(g.Pilot.Doing) + 1;
                if (g.World.LandNow && landAt == 0) landAt = t / 60.0;
                if (g.Phase == Phase.Dying && last != Phase.Dying) { var key = $"{g.World.Cause} at {(int)(g.World.Ship.X / Field.Tile)}"; deaths[key] = deaths.GetValueOrDefault(key) + 1; }
                last = g.Phase;
            }
            output.WriteLine($"seed {seed} {DreadBook.All[index].Name}: {(g.Cleared > 0 ? "DESTROYED" : "not destroyed")} after {t / 60}s, {deaths.Values.Sum()} deaths, land order at {landAt:0}s, score {g.Score}");
            output.WriteLine("   deaths " + string.Join(", ", deaths.OrderByDescending(d => d.Value).Take(10).Select(d => $"{d.Key} x{d.Value}")));
            output.WriteLine("   doing " + string.Join(", ", doing.OrderByDescending(d => d.Value).Take(6).Select(d => $"{d.Key} {d.Value / 60}s")));
        }
    }
}

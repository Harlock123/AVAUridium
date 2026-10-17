using AVAUridium.Engine;

namespace AVAUridium.Tests;

public class DreadTests
{
    /// <summary>
    /// Every square a ship of the given height (in rows) can reach from the space before the nose, flying round the
    /// towers and through the fences' gaps.
    /// </summary>
    private static bool[,] Reach(DreadDef d, int rows)
    {
        int cols = d.Cols + 20;
        var seen = new bool[cols, Field.Rows];
        bool Fits(int c, int r)
        {
            for (int k = 0; k < rows; k++)
            for (int dc = -1; dc <= 1; dc++)
                if (r + k >= Field.Rows || d.At(c - 10 + dc, r + k) == Tile.Wall) return false;
            return true;
        }
        var q = new Queue<(int, int)>();
        for (int r = 0; r < Field.Rows; r++)
            if (Fits(2, r)) { seen[2, r] = true; q.Enqueue((2, r)); }
        while (q.Count > 0)
        {
            var (c, r) = q.Dequeue();
            foreach (var (dc, dr) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nc = c + dc, nr = r + dr;
                if (nc < 0 || nc >= cols || nr < 0 || nr >= Field.Rows || seen[nc, nr] || !Fits(nc, nr)) continue;
                seen[nc, nr] = true;
                q.Enqueue((nc, nr));
            }
        }
        return seen;
    }

    [Fact]
    public void A_ship_on_its_side_can_reach_every_pod_and_the_strip_on_every_dreadnought()
    {
        foreach (var d in DreadBook.All)
        {
            var seen = Reach(d, 2);
            foreach (var o in d.Objects)
                Assert.True(seen[o.Col + 10, o.Row] || seen[o.Col + 11, o.Row], $"{d.Name}: {o.Kind} at {o.Col},{o.Row} can't be reached");
            Assert.True(seen[d.Runway.C0 + 10, d.Runway.R0], $"{d.Name}: the strip can't be reached");
        }
    }

    [Fact]
    public void The_first_two_dreadnoughts_can_be_flown_level_and_later_ones_need_the_roll()
    {
        foreach (var d in DreadBook.All.Take(2))
            Assert.True(Reach(d, 3)[d.Runway.C0 + 10, d.Runway.R0], $"{d.Name}: should be passable without rolling");
        Assert.Contains(DreadBook.All.Skip(2), d => !Reach(d, 3)[d.Runway.C0 + 10, d.Runway.R0]);
    }

    [Fact]
    public void Each_dreadnought_is_longer_better_defended_and_has_fences_and_a_clear_strip()
    {
        for (int i = 0; i < DreadBook.All.Length; i++)
        {
            var d = DreadBook.All[i];
            Assert.Equal(8 + i, d.Objects.Count(o => o.Kind == Foe.Pod));
            Assert.True(d.Fences.Count >= 8, $"{d.Name}: {d.Fences.Count} fences");
            if (i > 0) Assert.True(d.Cols > DreadBook.All[i - 1].Cols);
            for (int c = d.Runway.C0 - 4; c <= d.Runway.C1 + 4; c++)
            for (int r = d.Runway.R0 - 3; r <= d.Runway.R1 + 3; r++)
                Assert.NotEqual(Tile.Wall, d.At(c, r));
        }
    }

    [Fact]
    public void The_computer_pilot_destroys_every_dreadnought()
    {
        for (int i = 0; i < DreadBook.All.Length; i++)
        {
            var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "uridium-dread-" + Guid.NewGuid().ToString("N"))), 5) { Silent = true };
            g.Go(i);
            g.SetLives(30);
            g.Pilot.Reset();
            int deaths = 0;
            var last = g.Phase;
            for (int t = 0; t < 60 * 400 && g.Cleared == 0; t++)
            {
                g.Tick(g.Pilot.Decide(g));
                if (g.Phase == Phase.Dying && last != Phase.Dying) deaths++;
                last = g.Phase;
            }
            Assert.True(g.Cleared == 1, $"{DreadBook.All[i].Name}: not destroyed");
            Assert.True(deaths <= (i < 2 ? 1 : 6), $"{DreadBook.All[i].Name}: {deaths} ships lost");
        }
    }
}

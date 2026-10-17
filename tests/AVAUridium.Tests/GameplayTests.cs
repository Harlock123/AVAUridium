using Avalonia.Input;
using AVAUridium.Engine;

namespace AVAUridium.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avauridium-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>The first dreadnought with nothing coming (no waves), the ship placed and safe to hit.</summary>
    private static World Quiet(int index = 0, double col = 3, int row = DreadBook.Mid)
    {
        var w = new World();
        Sim.Start(w, index, 1, 1);
        w.WaveIn = 1e9;
        w.Enemies.Clear();
        w.PodsLeft = 99;
        w.Ship.X = col * Field.Tile;
        w.Ship.Y = Field.Top + (row + 0.5) * Field.Tile;
        w.Ship.Safe = 0;
        w.CamX = w.Ship.X - 340;
        return w;
    }

    private static void Run(World w, int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Sim.Step(w, c);
    }

    private static Enemy Put(World w, Foe kind, double dx, double dy, int hp = 1)
    {
        var e = new Enemy { Kind = kind, X = w.Ship.X + dx, Y = w.Ship.Y + dy, Hp = hp, Id = w.NextId++, Entered = true, FireIn = 99 };
        w.Enemies.Add(e);
        return e;
    }

    /// <summary>A column on a dreadnought clear of walls for a stretch, at the middle row.</summary>
    private static int OpenCol(DreadDef d, int from)
    {
        for (int c = from; c < d.Cols; c++)
        {
            bool clear = true;
            for (int k = -4; k <= 12 && clear; k++)
            for (int r = DreadBook.Mid - 3; r <= DreadBook.Mid + 3 && clear; r++)
                if (d.At(c + k, r) == Tile.Wall) clear = false;
            if (clear) return c;
        }
        return from;
    }

    // ───────────────────────────── flying ─────────────────────────────

    [Fact]
    public void The_ship_speeds_up_the_way_it_faces_and_keeps_its_speed()
    {
        var w = Quiet();
        w.Ship.Vx = 0;
        Run(w, 60, new Controls(1, 0));
        Assert.True(w.Ship.Vx > 500);
        double v = w.Ship.Vx;
        Run(w, 30);
        Assert.True(w.Ship.Vx > v - 30, "it coasts");
        Assert.True(w.Ship.Vx <= Ship.MaxSpeed);
    }

    [Fact]
    public void Pulling_back_slows_it_and_then_it_loops_round_to_face_the_other_way()
    {
        var w = Quiet();
        w.Ship.Vx = 300;
        Run(w, 10, new Controls(-1, 0));
        Assert.Equal(1, w.Ship.Facing);
        Assert.True(w.Ship.Vx < 300);
        for (int i = 0; i < 60 && w.Ship.Turn <= 0; i++) Sim.Step(w, new Controls(-1, 0));
        Assert.True(w.Ship.Turn > 0);
        double x = w.Ship.X;
        Run(w, (int)(60 * Ship.TurnTime) + 2);
        Assert.Equal(-1, w.Ship.Facing);
        // Round on the spot (and just off the other way).
        Assert.InRange(w.Ship.X - x, -6, 0.5);
    }

    [Fact]
    public void Up_or_down_rolls_it_on_its_side_and_it_stays_rolled_a_moment()
    {
        var w = Quiet();
        double y = w.Ship.Y;
        Run(w, 10, new Controls(0, -1));
        Assert.True(w.Ship.OnSide);
        Assert.True(w.Ship.Y < y);
        Run(w, (int)(60 * Ship.RollHoldTime * 0.6));
        Assert.True(w.Ship.OnSide);
        Run(w, 60);
        Assert.False(w.Ship.OnSide);
    }

    [Fact]
    public void Taps_up_and_down_roll_the_ship_without_moving_it()
    {
        var w = Quiet();
        double y = w.Ship.Y;
        for (int i = 0; i < 60; i++) Sim.Step(w, new Controls(0, i % 4 < 2 ? 1 : -1));
        Assert.True(w.Ship.OnSide);
        Assert.Equal(y, w.Ship.Y);
    }

    [Fact]
    public void Flying_into_a_tower_is_the_end_of_the_ship_but_looping_over_one_is_not()
    {
        var d = DreadBook.All[0];
        var (c0, _) = d.Fences[0];
        int wallRow = Enumerable.Range(0, Field.Rows).First(r => d.At(c0, r) == Tile.Wall && d.At(c0, r + 1) == Tile.Wall && d.At(c0, r - 1) == Tile.Wall);
        var w = Quiet(0, c0 - 6, wallRow);
        w.Ship.Vx = 300;
        Run(w, 60, new Controls(1, 0));
        Assert.True(w.Ship.Dead);
        Assert.Equal("the superstructure", w.Cause);
    }

    [Fact]
    public void On_its_side_the_ship_slips_through_a_narrow_gap_that_stops_it_level()
    {
        // A fence with a gap two squares high, at the middle.
        var d = DreadBook.All.First(x => x.Fences.Any(f => Gap(x, f.C0) == 2));
        var fence = d.Fences.First(f => Gap(d, f.C0) == 2);
        int row = Enumerable.Range(1, Field.Rows - 2).First(r => d.At(fence.C0, r) != Tile.Wall);
        // A little off the middle of the gap.
        double y = Field.Top + (row + 1) * Field.Tile + 7;
        var level = new World();
        Sim.Start(level, d.Index, 1, 1);
        level.WaveIn = 1e9;
        level.Enemies.Clear();
        level.Ship.X = (fence.C0 - 5) * Field.Tile;
        level.Ship.Y = y;
        level.Ship.Vx = 200;
        level.Ship.Safe = 0;
        var side = level.Copy();
        Run(level, 40, new Controls(1, 0));
        Assert.True(level.Ship.Dead, "level, the ship is too tall to be that far off the middle of the gap");
        for (int i = 0; i < 70; i++) Sim.Step(side, new Controls(1, i % 2 == 0 ? 1 : -1));
        Assert.False(side.Ship.Dead, side.Cause);
        Assert.True(side.Ship.X > (fence.C1 + 1) * Field.Tile, $"{d.Name} fence {fence} row {row} ship {side.Ship.X / Field.Tile:0.0},{(side.Ship.Y - Field.Top) / Field.Tile:0.0} vx {side.Ship.Vx:0} turn {side.Ship.Turn} landed {side.Landed}");
    }

    private static int Gap(DreadDef d, int c)
    {
        int open = Enumerable.Range(0, Field.Rows).Count(r => d.At(c, r) != Tile.Wall);
        return open;
    }

    // ───────────────────────────── shooting ─────────────────────────────

    [Fact]
    public void Shots_go_the_way_the_ship_faces_three_at_a_time_and_stop_at_towers()
    {
        var w = Quiet(0, OpenCol(DreadBook.All[0], 30));
        w.Ship.Vx = 0;
        Run(w, 20, new Controls(0, 0, true));
        Assert.Equal(3, w.Shots.Count);
        Assert.All(w.Shots, s => Assert.True(s.Vx > 0));
    }

    [Fact]
    public void A_pod_takes_three_shots_and_counts_toward_the_order_to_land()
    {
        var w = Quiet();
        w.PodsLeft = 2;
        var pod = Put(w, Foe.Pod, 160, 0, 3);
        Run(w, 40, new Controls(0, 0, true));
        Assert.True(pod.Dead);
        Assert.Equal(1, w.PodsLeft);
        Assert.False(w.LandNow);
        Assert.True(w.Score >= Sim.Points(Foe.Pod));
    }

    [Fact]
    public void Every_pod_shot_brings_the_order_to_land()
    {
        var w = Quiet();
        w.PodsLeft = 2;
        foreach (int dy in new[] { -100, 100 }) Sim.Destroy(w, Put(w, Foe.Pod, 300, dy, 3));
        Sim.Step(w, default);
        Assert.True(w.LandNow);
        Assert.Contains(Sfx.SayLand, w.Heard);
    }

    [Fact]
    public void A_dreadnought_held_out_long_enough_gives_the_order_anyway()
    {
        var w = Quiet();
        w.Clock = Sim.LandAfter - 0.01;
        Run(w, 2);
        Assert.True(w.LandNow);
        Assert.True(w.PodsLeft > 0);
    }

    [Fact]
    public void A_whole_wave_shot_down_is_a_bonus()
    {
        var w = Quiet();
        w.Waves.Add(2);
        var a = Put(w, Foe.Dart, 300, 0);
        var b = Put(w, Foe.Dart, 340, 0);
        a.Wave = b.Wave = 0;
        Sim.Destroy(w, a);
        int score = w.Score;
        Sim.Destroy(w, b);
        Assert.Equal(Sim.Points(Foe.Dart) + Sim.WaveBonus, w.Score - score);
    }

    // ───────────────────────────── the defences ─────────────────────────────

    [Fact]
    public void Fighters_and_mines_are_the_end_of_the_ship_but_it_flies_over_what_stands_on_the_deck()
    {
        var w = Quiet();
        Put(w, Foe.Gun, 0, 0, 2);
        Put(w, Foe.Port, 0, 0, 4);
        Run(w, 2);
        Assert.False(w.Ship.Dead);
        Put(w, Foe.Mine, 4, 0);
        Run(w, 2);
        Assert.True(w.Ship.Dead);
        Assert.Equal("a mine", w.Cause);
    }

    [Fact]
    public void A_port_lets_out_mines_that_hunt_the_ship()
    {
        var w = Quiet(0, 20);
        var port = Put(w, Foe.Port, 300, 100, 4);
        port.FireIn = 0.1;
        w.Ship.Safe = 99;
        Run(w, 30);
        var mine = Assert.Single(w.Enemies, e => e.Kind == Foe.Mine);
        double d0 = Math.Abs(mine.X - w.Ship.X) + Math.Abs(mine.Y - w.Ship.Y);
        Run(w, 60);
        Assert.True(Math.Abs(mine.X - w.Ship.X) + Math.Abs(mine.Y - w.Ship.Y) < d0);
    }

    [Fact]
    public void Waves_of_five_fighters_come_in_from_the_edges()
    {
        var w = Quiet();
        w.WaveIn = 0.01;
        Run(w, 1);
        var fighters = w.Enemies.Where(e => !e.OnDeck).ToList();
        Assert.Equal(5, fighters.Count);
        Assert.All(fighters, f => Assert.True(f.X < w.CamX - 20 || f.X > w.CamX + Field.Width + 20));
    }

    // ───────────────────────────── landing ─────────────────────────────

    /// <summary>The first dreadnought with the order given, the ship lined up on the strip.</summary>
    private static World OnFinals(double speed)
    {
        var w = Quiet();
        w.LandNow = true;
        var d = w.Dread;
        w.Ship.X = (d.Runway.C0 - 2) * Field.Tile;
        w.Ship.Y = d.RunwayAt.Y;
        w.Ship.Vx = speed;
        w.CamX = w.Ship.X - 340;
        return w;
    }

    [Fact]
    public void Slow_and_level_over_the_strip_the_ship_lands()
    {
        var w = OnFinals(180);
        Run(w, 30);
        Assert.True(w.Landed);
    }

    [Fact]
    public void Too_fast_it_goes_over_the_strip_without_landing()
    {
        var w = OnFinals(500);
        Run(w, 30, new Controls(1, 0));
        Assert.False(w.Landed);
    }

    [Fact]
    public void Without_the_order_the_strip_is_just_deck()
    {
        var w = OnFinals(180);
        w.LandNow = false;
        w.Clock = 0;
        Run(w, 30);
        Assert.False(w.Landed);
    }

    [Fact]
    public void After_landing_the_reactor_lights_are_stopped_three_times_then_the_dreadnought_goes_up()
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder()), 1) { Silent = true };
        g.Go(0);
        var w = g.World;
        w.LandNow = true;
        w.Ship.X = (w.Dread.Runway.C0 + 2) * Field.Tile;
        w.Ship.Y = w.Dread.RunwayAt.Y;
        w.Ship.Vx = 150;
        w.Ship.Safe = 99;
        g.Tick(default);
        Assert.Equal(Phase.Reactor, g.Phase);
        int score = g.Score;
        g.Run(40);
        for (int i = 0; i < GameEngine.ReactorStops; i++)
        {
            g.Tick(new Controls(0, 0, true));
            g.Run(20);
        }
        Assert.Equal(0, g.ReactorLeft);
        Assert.True(g.ReactorWon >= 3 * GameEngine.ReactorCells.Min());
        Assert.Equal(score + g.ReactorWon, g.Score);
        g.Run(120);
        Assert.Equal(Phase.Destruct, g.Phase);
        g.Run((int)(60 * GameEngine.DestructSeconds) + 5);
        Assert.Equal(1, g.World.Index);
        Assert.Equal(1, g.Cleared);
    }

    // ───────────────────────────── copies ─────────────────────────────

    [Fact]
    public void A_copy_of_the_world_plays_out_exactly_as_the_world_does()
    {
        var a = new World();
        Sim.Start(a, 2, 1, 7);
        Run(a, 120, new Controls(1, 0, true));
        var b = a.Copy();
        var c = new Controls(1, -1, true);
        Run(a, 400, c);
        Run(b, 400, c);
        Assert.Equal(a.Ship.X, b.Ship.X);
        Assert.Equal(a.Ship.Y, b.Ship.Y);
        Assert.Equal(a.Score, b.Score);
        Assert.Equal(a.Enemies.Count, b.Enemies.Count);
        Assert.Equal(a.Ship.Dead, b.Ship.Dead);
    }

    // ───────────────────────────── the game ─────────────────────────────

    [Fact]
    public void A_ship_lost_comes_back_further_back_along_the_deck_with_what_was_shot_still_shot()
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder()), 1) { Silent = true };
        g.Go(0);
        var w = g.World;
        var pod = w.Enemies.First(e => e.Kind == Foe.Pod);
        Sim.Destroy(w, pod);
        int pods = w.PodsLeft;
        w.Ship.X = (w.Dread.Fences[2].C1 + 8) * Field.Tile;
        Sim.Kill(w, "test");
        g.Run((int)(60 * GameEngine.DyingSeconds) + 5);
        Assert.Equal(GameEngine.Lives0 - 1, g.Lives);
        Assert.Equal(Phase.Play, g.Phase);
        Assert.InRange(g.World.Ship.X - (w.Dread.Fences[2].C1 + 3.5) * Field.Tile, 0, 12);
        Assert.Equal(pods, g.World.PodsLeft);
        Assert.True(g.World.Ship.Safe > 0);
    }

    [Fact]
    public void The_last_ship_lost_ends_the_game_and_a_good_score_goes_on_the_table()
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder()), 1) { Silent = true };
        g.Go(0);
        g.SetLives(1);
        g.World.Score = 90000;
        Sim.Kill(g.World, "test");
        g.Run((int)(60 * (GameEngine.DyingSeconds + 4.5)));
        Assert.Equal(GameState.EnterInitials, g.State);
    }

    [Fact]
    public void Left_alone_the_title_runs_the_demonstration_then_the_high_scores()
    {
        var g = new GameEngine(HighScoreTable.Load(TempFolder()), 1) { Silent = true };
        g.Run((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        g.Run((int)(60 * GameEngine.AttractSeconds) + 5);
        Assert.Equal(GameState.HighScores, g.State);
        g.Run((int)(60 * GameEngine.HighScoreSeconds) + 5);
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void The_keys_fly_and_fire()
    {
        var input = new InputHandler();
        input.OnKeyDown(Key.Up);
        Assert.Equal(-1, input.Player.Dy);
        input.OnKeyUp(Key.Up);
        input.OnKeyDown(Key.Right);
        Assert.Equal(1, input.Player.Dx);
        Assert.False(input.Player.Fire);
        input.OnKeyDown(Key.X);
        Assert.True(input.Player.Fire);
        input.OnKeyUp(Key.X);
        input.OnKeyDown(Key.Space);
        Assert.True(input.Player.Fire);
        input.Dispose();
        // Up on the keys flies up the screen.
        var w = Quiet();
        double y = w.Ship.Y;
        Run(w, 20, new Controls(0, -1));
        Assert.True(w.Ship.Y < y);
    }
}

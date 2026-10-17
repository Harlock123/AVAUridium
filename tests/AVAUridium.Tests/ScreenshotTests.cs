using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAUridium.Engine;
using AVAUridium.Views;
using Controls = AVAUridium.Engine.Controls;

namespace AVAUridium.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set URIDIUM_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avauridium-ui-" + Guid.NewGuid().ToString("N"))), seed) { Silent = true };
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
        var dir = Environment.GetEnvironmentVariable("URIDIUM_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAUridium-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            var move = g.Pilot.Decide(g);
            g.Tick(move);
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }
    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(3.1);
        Shot(w, c, "title");
        g.HighScores.Insert("ACE", 214900, 8);
        g.HighScores.Insert("AVA", 88100, 3);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 30 && g.State != GameState.HighScores; i++) g.Idle(1);
        Shot(w, c, "high-scores");
    }

    /// <summary>A dreadnought flown by the computer pilot until something worth showing.</summary>
    private static void Play(int index, string name, Func<GameEngine, bool> moment, int seconds = 300)
    {
        var (w, c, g) = Open();
        g.Go(index);
        g.SetLives(30);
        g.Pilot.Reset();
        Assert.True(PlayUntil(g, c, moment, seconds), $"never saw the moment for {name}");
        Shot(w, c, name);
    }

    [AvaloniaFact]
    public void Over_the_deck() => Play(0, "deck", g => g.World.Ship.X > 30 * Field.Tile && g.World.Enemies.Any(e => !e.OnDeck && Math.Abs(e.X - g.World.Ship.X) < 400) && g.World.Shots.Count >= 2);

    [AvaloniaFact]
    public void A_pod_shot() => Play(1, "pod", g => g.World.Effects.Any(e => e.Kind == EffectKind.Burst && e.Colour == 0xFF60FFB0 && e.Age < 0.15));

    [AvaloniaFact]
    public void A_wave() => Play(2, "wave", g => g.World.Enemies.Count(e => !e.OnDeck && e.Kind != Foe.Mine && e.X > g.World.CamX + 40 && e.X < g.World.CamX + Field.Width - 40) >= 4);

    [AvaloniaFact]
    public void On_its_side_through_a_gap() => Play(4, "gap", g => g.World.Ship.OnSide && g.World.Dread.Fences.Any(f => Math.Abs((f.C0 + f.C1 + 1) / 2.0 * Field.Tile - g.World.Ship.X) < 14));

    [AvaloniaFact]
    public void A_loop() => Play(3, "loop", g => g.World.Ship.Turn > Ship.TurnTime * 0.4 && g.World.Ship.Turn < Ship.TurnTime * 0.6 && g.World.Ship.X > 20 * Field.Tile);

    [AvaloniaFact]
    public void Mines() => Play(5, "mines", g => g.World.Enemies.Count(e => e.Kind == Foe.Mine) >= 2);

    [AvaloniaFact]
    public void Land_now() => Play(1, "land-now", g => g.World.LandNow && g.World.Ship.X > g.World.Dread.Runway.C0 * Field.Tile - 500 && !g.World.Landed);

    [AvaloniaFact]
    public void The_reactor() => Play(0, "reactor", g => g.Phase == Phase.Reactor && g.ReactorLeft == 2 && g.PhaseTime > 0.3);

    [AvaloniaFact]
    public void Destruct() => Play(0, "destruct", g => g.Phase == Phase.Destruct && g.PhaseTime > 2.2);

    [AvaloniaFact]
    public void A_later_dreadnought() => Play(7, "topaz", g => g.World.Ship.X > 60 * Field.Tile && g.World.Enemies.Any(e => !e.OnDeck && Math.Abs(e.X - g.World.Ship.X) < 500));
}

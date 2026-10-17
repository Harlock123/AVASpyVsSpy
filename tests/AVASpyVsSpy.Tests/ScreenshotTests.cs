using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVASpyVsSpy.Engine;
using AVASpyVsSpy.Views;
using Controls = AVASpyVsSpy.Engine.Controls;

namespace AVASpyVsSpy.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set SPY_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avaspyvsspy-ui-" + Guid.NewGuid().ToString("N"))), seed) { Silent = true };
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
        var dir = Environment.GetEnvironmentVariable("SPY_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVASpyVsSpy-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets two computer spies play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            var move = g.Pilots[0].Decide(g.World);
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
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("AGT", 48210, 9);
        g.HighScores.Insert("AVA", 21500, 4);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        for (int i = 0; i < 60 * 300 && g.State != GameState.HighScores; i++) g.Idle(1);
        Shot(w, c, "high-scores");
    }

    /// <summary>An embassy played by two computer spies until something worth showing.</summary>
    private static void Play(int embassy, string name, Func<GameEngine, bool> moment, int seconds = 300, int seed = 11)
    {
        var (w, c, g) = Open(seed);
        g.Go(embassy);
        g.SetLives(20);
        g.Pilots[0].Reset(seed);
        Assert.True(PlayUntil(g, c, moment, seconds), $"never saw the moment for {name}");
        Shot(w, c, name);
    }

    private static bool Together(GameEngine g) => g.World.Spies[0].Room == g.World.Spies[1].Room && g.World.Spies.All(s => s.Active);

    [AvaloniaFact]
    public void The_embassy() => Play(0, "embassy", g => g.World.Clock > 6 && g.World.Spies.All(s => s.Doing == Doing.Free));

    [AvaloniaFact]
    public void Searching() => Play(1, "search", g => g.World.Spies.Any(s => s.Doing == Doing.Searching && s.Timer < 0.2) && g.World.Spies.Sum(s => s.Held) >= 2);

    [AvaloniaFact]
    public void A_fight() => Play(2, "fight", g => Together(g) && g.World.Effects.Any(f => f.Kind == Flash.Hit && f.T < 0.15));

    [AvaloniaFact]
    public void A_bomb() => Play(3, "bomb", g => g.World.Effects.Any(f => f.Kind == Flash.Bomb && f.T > 0.25));

    [AvaloniaFact]
    public void A_door_trap() => Play(3, "door-trap", g => g.World.Effects.Any(f => f.Kind is Flash.Pail or Flash.Gun && f.T > 0.3), 600, 5);

    [AvaloniaFact]
    public void Away() => Play(1, "away", g => g.Phase == Phase.Over && g.PhaseTime > 0.3);

    [AvaloniaFact]
    public void Game_over()
    {
        var (w, c, g) = Open();
        g.Go(0);
        g.SetLives(1);
        for (int i = 0; i < 60 * 600 && !(g.State == GameState.GameOver && g.StateTime > 1); i++) { g.Tick(default); if (i % 3 == 0) c.Advance(0.05); }
        Shot(w, c, "game-over");
    }
}

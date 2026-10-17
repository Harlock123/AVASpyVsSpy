using AVASpyVsSpy.Engine;

namespace AVASpyVsSpy.Tests;

/// <summary>How the computer spies get on against each other in each embassy (set SPY_PROBE to run it, for tuning).</summary>
public sealed class PilotProbe(ITestOutputHelper output)
{
    [Fact]
    public void How_the_computer_spies_do()
    {
        if (Environment.GetEnvironmentVariable("SPY_PROBE") is not { } mode) return;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < EmbassyBook.Count; i++)
        {
            int[] wins = new int[3];
            double time = 0;
            int deaths = 0, traps = 0, fights = 0;
            var causes = new Dictionary<string, int>();
            const int games = 12;
            for (int game = 0; game < games; game++)
            {
                var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "spy-probe-" + Guid.NewGuid().ToString("N"))), 100 + game) { Silent = true };
                g.Go(i);
                g.SetLives(30);
                var white = new Pilot(0) { Skill = 0.7 };
                white.Reset(game);
                int t = 0;
                for (; t < 60 * 400 && g.Phase == Phase.Play; t++)
                {
                    var c = mode == "lazy" ? default : white.Decide(g.World);
                    g.Tick(c);
                }
                var w = g.World;
                wins[w.Winner < 0 ? 2 : w.Winner]++;
                time += t / 60.0;
                foreach (var s in w.Spies) deaths += s.Deaths;
                traps += w.Log.Count(l => l.StartsWith("trapped"));
                foreach (var l in w.Log.Where(l => l.StartsWith("died"))) fights++;
                foreach (var s in w.Spies) if (s.Cause != "") causes[s.Cause] = causes.GetValueOrDefault(s.Cause) + 1;
            }
            output.WriteLine($"{i + 1} {EmbassyBook.Build(i, 1).Name}: white {wins[0]}, black {wins[1]}, nobody {wins[2]}; {time / games:0}s a game, {deaths / (double)games:0.0} deaths, {traps / (double)games:0.0} traps set ({string.Join(", ", causes.Select(c => $"{c.Key} {c.Value}"))})");
        }
        output.WriteLine($"total {sw.ElapsedMilliseconds} ms");
    }
}

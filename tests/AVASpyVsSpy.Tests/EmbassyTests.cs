using AVASpyVsSpy.Engine;

namespace AVASpyVsSpy.Tests;

/// <summary>The five embassies: built to be played, and played out by the computer spies.</summary>
public class EmbassyTests(ITestOutputHelper output)
{
    public static TheoryData<int> Embassies() => [.. Enumerable.Range(0, EmbassyBook.Count)];

    [Theory]
    [MemberData(nameof(Embassies))]
    public void Every_room_can_be_reached_and_the_way_out_is_on_an_outside_wall(int index)
    {
        for (int seed = 0; seed < 20; seed++)
        {
            var e = EmbassyBook.Build(index, seed);
            Assert.All(e.Distances(0), d => Assert.True(d >= 0));
            Assert.Equal(-1, e.Through(e.ExitRoom, e.ExitSide));
            Assert.True(e.Door[e.ExitRoom, (int)e.ExitSide]);
            Assert.NotEqual(e.Start[0], e.Start[1]);
            for (int r = 0; r < e.Rooms; r++)
            {
                int n = e.In(r).Count();
                Assert.InRange(n, 2, 4);
                // Doors match on both sides.
                for (int s = 0; s < 4; s++)
                    if (e.Through(r, (Side)s) is var o && o >= 0) Assert.True(e.Door[o, (int)Kinds.Opposite((Side)s)]);
            }
        }
    }

    [Fact]
    public void Each_embassy_is_larger_than_the_one_before_with_more_time()
    {
        for (int i = 1; i < EmbassyBook.Count; i++)
        {
            Assert.True(EmbassyBook.Build(i, 1).Rooms > EmbassyBook.Build(i - 1, 1).Rooms);
            Assert.True(EmbassyBook.Build(i, 1).Seconds > EmbassyBook.Build(i - 1, 1).Seconds);
        }
    }

    [Theory]
    [MemberData(nameof(Embassies))]
    public void The_five_things_and_the_four_remedies_are_hidden_once_each_away_from_the_starts(int index)
    {
        var w = new World();
        var e = EmbassyBook.Build(index, 3);
        Sim.Start(w, e, 3);
        var all = w.Contents.SelectMany((c, i) => c.Select(t => (t, i))).ToList();
        Assert.Equal(9, all.Count);
        Assert.Equal(5, all.Count(x => x.t.Item != null));
        Assert.Equal(9, all.Select(x => x.i).Distinct().Count());
        Assert.All(all, x => Assert.DoesNotContain(e.Furniture[x.i].Room, e.Start));
        Assert.All(w.Spies, s => Assert.Equal(e.Start[s.Index], s.Room));
    }

    [Theory]
    [MemberData(nameof(Embassies))]
    public void The_computer_spies_play_it_out_and_one_gets_away(int index)
    {
        int[] wins = new int[3];
        for (int game = 0; game < 4; game++)
        {
            var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avaspyvsspy-tests-" + Guid.NewGuid().ToString("N"))), 40 + game) { Silent = true };
            g.Go(index);
            var white = new Pilot(0) { Skill = 0.7 };
            white.Reset(game);
            for (int t = 0; t < 60 * 400 && g.Phase == Phase.Play; t++) g.Tick(white.Decide(g.World));
            wins[g.World.Winner < 0 ? 2 : g.World.Winner]++;
        }
        output.WriteLine($"{EmbassyBook.Build(index, 1).Name}: white {wins[0]}, black {wins[1]}, nobody {wins[2]}");
        Assert.True(wins[0] + wins[1] >= 3, "someone should get away");
        if (index == 0) Assert.True(wins[0] >= 2, "the first embassy's black spy is slow");
    }

    [Fact]
    public void Doing_nothing_the_black_spy_gets_away()
    {
        for (int index = 0; index < EmbassyBook.Count; index++)
        {
            var g = new GameEngine(HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avaspyvsspy-tests-" + Guid.NewGuid().ToString("N"))), 9) { Silent = true };
            g.Go(index);
            for (int t = 0; t < 60 * 400 && g.Phase == Phase.Play; t++) g.Tick(default);
            Assert.Equal(1, g.World.Winner);
        }
    }
}

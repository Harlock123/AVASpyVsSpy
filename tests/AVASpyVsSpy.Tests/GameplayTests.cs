using Avalonia.Input;
using AVASpyVsSpy.Engine;

namespace AVASpyVsSpy.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avaspyvsspy-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>Two rooms side by side, a door between them and the way out on the far side of the second.</summary>
    private static EmbassyDef Two()
    {
        var e = new EmbassyDef { Name = "TEST", Cols = 2, Rows = 1, Seconds = 100, Door = new bool[2, 4], ExitRoom = 1, ExitSide = Side.East, Start = [0, 1] };
        e.Door[0, (int)Side.East] = e.Door[1, (int)Side.West] = e.Door[1, (int)Side.East] = true;
        e.Furniture.Add(new FurnitureDef(0, 0, 70, Piece.Desk));
        e.Furniture.Add(new FurnitureDef(0, 3, 530, Piece.Safe));
        e.Furniture.Add(new FurnitureDef(1, 0, 70, Piece.Locker));
        e.Furniture.Add(new FurnitureDef(1, 3, 530, Piece.Clock));
        return e;
    }

    /// <summary>The two rooms, nothing hidden anywhere; the white spy in the first, the black spy out of the way in the second.</summary>
    private static World Fresh()
    {
        var w = new World();
        Sim.Start(w, Two(), 1);
        foreach (var c in w.Contents) c.Clear();
        Put(w.Spies[0], 0, 300, 90);
        Put(w.Spies[1], 1, 300, 170);
        return w;
    }

    private static void Put(Spy s, int room, double x, double z)
    {
        s.Room = room;
        s.X = x;
        s.Z = z;
    }

    private static void Run(World w, int steps, Controls white = default, Controls black = default)
    {
        for (int i = 0; i < steps; i++) Sim.Step(w, white, black);
    }

    /// <summary>One press of a button, and let go.</summary>
    private static void Press(World w, Controls c, int after = 40)
    {
        Sim.Step(w, c, default);
        Run(w, after);
    }

    private static readonly Controls Act = new(Act: true), Trap = new(Trap: true), Next = new(Next: true);

    // ───────────────────────────── walking ─────────────────────────────

    [Fact]
    public void He_walks_through_a_door_into_the_next_room()
    {
        var w = Fresh();
        Run(w, 120, new Controls(1, 0));
        Assert.Equal(1, w.Spies[0].Room);
        Assert.True(w.Spies[0].X < 200, "just inside the west door");
        Assert.Contains(Sfx.Door, w.Heard);
    }

    [Fact]
    public void A_wall_without_a_door_stops_him()
    {
        var w = Fresh();
        Run(w, 120, new Controls(-1, 0));
        Assert.Equal(0, w.Spies[0].Room);
        Assert.Equal(20, w.Spies[0].X, 3);
        Run(w, 120, new Controls(0, -1));
        Assert.Equal(6, w.Spies[0].Z, 3);
        Assert.Equal(0, w.Spies[0].Room);
    }

    [Fact]
    public void Up_goes_towards_the_back_wall()
    {
        var w = Fresh();
        Run(w, 20, new Controls(0, -1));
        Assert.True(w.Spies[0].Z < 90);
    }

    // ───────────────────────────── searching ─────────────────────────────

    [Fact]
    public void Searching_furniture_finds_what_is_hidden_in_it()
    {
        var w = Fresh();
        w.Contents[0].Add(Thing.Of(Item.Plans));
        Put(w.Spies[0], 0, 70, 12);
        Press(w, Act);
        Assert.True(w.Spies[0].Items[(int)Item.Plans]);
        Assert.Equal(100, w.Spies[0].Score);
        Assert.Empty(w.Contents[0]);
        Assert.Contains(Sfx.Find, w.Heard);
        // And again: nothing.
        Press(w, Act);
        Assert.Equal("NOTHING HERE", w.Spies[0].Message);
    }

    [Fact]
    public void Away_from_furniture_the_button_swings_the_club()
    {
        var w = Fresh();
        Sim.Step(w, Act, default);
        Assert.Equal(Doing.Swinging, w.Spies[0].Doing);
    }

    [Fact]
    public void A_spy_carries_one_remedy_and_leaves_the_old_one_behind()
    {
        var w = Fresh();
        w.Spies[0].Remedy = Remedy.Water;
        w.Contents[1].Add(Thing.Of(Remedy.Cutters));
        Put(w.Spies[0], 0, 530, 12);
        Press(w, Act);
        Assert.Equal(Remedy.Cutters, w.Spies[0].Remedy);
        Assert.Equal([Thing.Of(Remedy.Water)], w.Contents[1]);
    }

    // ───────────────────────────── traps ─────────────────────────────

    [Fact]
    public void A_bomb_in_the_furniture_kills_whoever_searches_it()
    {
        var w = Fresh();
        w.FurnitureTraps[0] = new SetTrap(TrapKind.Bomb, 1);
        Put(w.Spies[0], 0, 70, 12);
        Press(w, Act);
        Assert.Equal(Doing.Dead, w.Spies[0].Doing);
        Assert.Equal("BLOWN UP", w.Spies[0].Cause);
        Assert.Equal(300, w.Spies[1].Score);
        Assert.Null(w.FurnitureTraps[0]);
        Assert.Contains(w.Effects, f => f.Kind == Flash.Bomb);
    }

    [Fact]
    public void Water_puts_the_bomb_out()
    {
        var w = Fresh();
        w.FurnitureTraps[0] = new SetTrap(TrapKind.Bomb, 1);
        w.Contents[0].Add(Thing.Of(Item.Key));
        w.Spies[0].Remedy = Remedy.Water;
        Put(w.Spies[0], 0, 70, 12);
        Press(w, Act);
        Assert.True(w.Spies[0].Active);
        Assert.Null(w.FurnitureTraps[0]);
        Assert.True(w.Spies[0].Items[(int)Item.Key], "and what's in it is still found");
        Assert.Contains(Sfx.Defuse, w.Heard);
    }

    [Theory]
    [InlineData(TrapKind.Pail, Remedy.Umbrella, "PAILED")]
    [InlineData(TrapKind.Gun, Remedy.Scissors, "SHOT")]
    public void A_trap_over_a_door_gets_whoever_goes_through_unless_he_has_the_remedy(TrapKind kind, Remedy cure, string cause)
    {
        var w = Fresh();
        w.DoorTraps[w.DoorKey(0, Side.East)] = new SetTrap(kind, 1);
        Run(w, 120, new Controls(1, 0));
        Assert.Equal(Doing.Dead, w.Spies[0].Doing);
        Assert.Equal(cause, w.Spies[0].Cause);
        Assert.Equal(0, w.Spies[0].Room);

        var safe = Fresh();
        safe.Spies[0].Remedy = cure;
        safe.DoorTraps[safe.DoorKey(1, Side.West)] = new SetTrap(kind, 1);
        Run(safe, 120, new Controls(1, 0));
        Assert.True(safe.Spies[0].Active);
        Assert.Equal(1, safe.Spies[0].Room);
        Assert.Empty(safe.DoorTraps);
    }

    [Fact]
    public void Setting_a_trap_and_springing_it_oneself()
    {
        var w = Fresh();
        Put(w.Spies[0], 0, 530, 12);
        Assert.Equal(TrapKind.Bomb, w.Spies[0].Chosen);
        Press(w, Trap);
        Assert.Equal(new SetTrap(TrapKind.Bomb, 0), w.FurnitureTraps[1]);
        Assert.Equal(2, w.Spies[0].Traps[(int)TrapKind.Bomb]);
        // His own trap is no kinder to him.
        Press(w, Act);
        Assert.Equal(Doing.Dead, w.Spies[0].Doing);
        Assert.Equal(0, w.Spies[1].Score);
    }

    [Fact]
    public void Door_traps_go_over_doors_and_furniture_traps_in_furniture()
    {
        var w = Fresh();
        var s = w.Spies[0];
        Put(s, 0, 70, 12);
        Press(w, Next);
        Press(w, Next);
        Assert.Equal(TrapKind.Pail, s.Chosen);
        Press(w, Trap);
        Assert.Null(w.FurnitureTraps[0]);
        Assert.Equal("THE PAIL GOES OVER A DOOR", s.Message);
        Put(s, 0, Field.RoomW - 30, Field.DoorZ);
        Press(w, Trap);
        Assert.Equal(new SetTrap(TrapKind.Pail, 0), w.DoorTrap(1, Side.West));
        Assert.Equal(1, s.Traps[(int)TrapKind.Pail]);
    }

    [Fact]
    public void Choosing_skips_traps_used_up()
    {
        var s = new Spy { Traps = [1, 0, 0, 2], Chosen = TrapKind.Bomb };
        Sim.Choose(s, 1);
        Assert.Equal(TrapKind.Gun, s.Chosen);
        Sim.Choose(s, 1);
        Assert.Equal(TrapKind.Bomb, s.Chosen);
    }

    // ───────────────────────────── fighting ─────────────────────────────

    [Fact]
    public void Clubbed_five_times_a_spy_is_done_for_and_drops_what_he_carried()
    {
        var w = Fresh();
        var (a, b) = (w.Spies[0], w.Spies[1]);
        Put(a, 0, 300, 100);
        Put(b, 0, 350, 100);
        b.Items[(int)Item.Money] = b.Items[(int)Item.Passport] = true;
        for (int hit = 1; hit <= 5; hit++)
        {
            b.X = 350;
            Press(w, Act, 30);
            if (hit < 5) Assert.Equal(Spy.Strength0 - hit, b.Strength);
        }
        Assert.Equal(Doing.Dead, b.Doing);
        Assert.Equal("CLUBBED", b.Cause);
        Assert.Equal(500, a.Score);
        Assert.Equal(0, b.Held);
        // What he had is hidden in this room's furniture.
        var hidden = w.Contents[0].Concat(w.Contents[1]).ToList();
        Assert.Contains(Thing.Of(Item.Money), hidden);
        Assert.Contains(Thing.Of(Item.Passport), hidden);
    }

    [Fact]
    public void A_blow_knocks_him_back_and_stuns_him()
    {
        var w = Fresh();
        Put(w.Spies[0], 0, 300, 100);
        Put(w.Spies[1], 0, 350, 100);
        Sim.Step(w, Act, default);
        Run(w, 12);
        Assert.Equal(Doing.Stunned, w.Spies[1].Doing);
        Assert.Equal(396, w.Spies[1].X, 3);
    }

    [Fact]
    public void A_dead_spy_comes_back_after_a_while_somewhere_else()
    {
        var w = Fresh();
        Put(w.Spies[1], 0, 400, 100);
        Sim.Kill(w, w.Spies[0], "TEST", 1);
        w.Spies[0].Strength = 1;
        Run(w, (int)(60 * Sim.DeadTime) - 5);
        Assert.Equal(Doing.Dead, w.Spies[0].Doing);
        Run(w, 10);
        Assert.Equal(Doing.Free, w.Spies[0].Doing);
        Assert.Equal(Spy.Strength0, w.Spies[0].Strength);
        Assert.Equal(1, w.Spies[0].Room);
    }

    // ───────────────────────────── the way out, and the clock ─────────────────────────────

    [Fact]
    public void The_way_out_needs_all_five()
    {
        var w = Fresh();
        var s = w.Spies[0];
        Put(s, 1, 400, Field.DoorZ);
        s.Items = [true, true, true, true, false];
        Run(w, 90, new Controls(1, 0));
        Assert.True(s.Active);
        Assert.Equal(-1, w.Winner);
        Assert.Contains(Sfx.Locked, w.Heard);
        s.Items[4] = true;
        Run(w, 90, new Controls(1, 0));
        Assert.Equal(Doing.Escaped, s.Doing);
        Assert.Equal(0, w.Winner);
    }

    [Fact]
    public void When_his_clock_runs_out_he_is_out()
    {
        var w = Fresh();
        w.Spies[0].Clock = 1;
        Run(w, 70);
        Assert.Equal(Doing.OutOfTime, w.Spies[0].Doing);
        Assert.True(w.Spies[1].Active);
    }

    // ───────────────────────────── the game ─────────────────────────────

    private static GameEngine Engine(int seed = 1) => new(HighScoreTable.Load(TempFolder()), seed) { Silent = true };

    [Fact]
    public void Getting_away_scores_the_time_left_and_goes_on_to_the_next_embassy()
    {
        var g = Engine();
        g.Go(0);
        var s = g.World.Spies[0];
        s.Clock = 100.5;
        s.Doing = Doing.Escaped;
        g.World.Winner = 0;
        g.Run(1);
        Assert.Equal(Phase.Over, g.Phase);
        Assert.True(g.Won);
        Assert.Equal(GameEngine.EscapeBonus + 100 * GameEngine.SecondPoints, g.Score);
        g.Run((int)(60 * GameEngine.OverSeconds) + 2);
        Assert.Equal(1, g.EmbassyIndex);
        Assert.Equal(1, g.Cleared);
        Assert.Equal(GameEngine.Lives0, g.Lives);
        Assert.Equal(GameEngine.EscapeBonus + 100 * GameEngine.SecondPoints, g.Score);
    }

    [Fact]
    public void When_the_black_spy_gets_away_it_is_the_same_embassy_again_one_agent_down()
    {
        var g = Engine();
        g.Go(0);
        g.World.Spies[1].Doing = Doing.Escaped;
        g.World.Winner = 1;
        g.Run(1);
        Assert.False(g.Won);
        g.Run((int)(60 * GameEngine.OverSeconds) + 2);
        Assert.Equal(GameEngine.Lives0 - 1, g.Lives);
        Assert.Equal(0, g.EmbassyIndex);
        Assert.Equal(Phase.Start, g.Phase);
    }

    [Fact]
    public void The_last_agent_lost_is_game_over_then_the_high_scores()
    {
        var g = Engine();
        g.Go(0);
        g.SetLives(1);
        g.World.Spies[0].Score = 25000;
        g.World.Spies[0].Clock = 0.01;
        g.Run(2);
        Assert.Equal(Phase.Over, g.Phase);
        g.Run((int)(60 * GameEngine.OverSeconds) + 2);
        Assert.Equal(GameState.GameOver, g.State);
        g.Run((int)(60 * GameEngine.GameOverSeconds) + 2);
        Assert.Equal(GameState.EnterInitials, g.State);
        foreach (char ch in "AGT") g.Initials!.Type(ch);
        g.Run(1);
        Assert.Equal(GameState.HighScores, g.State);
        Assert.Equal("AGT", g.HighScores.Entries[g.NewEntryIndex].Initials);
    }

    [Fact]
    public void The_title_gives_way_to_the_demonstration_and_enter_starts_a_game()
    {
        var g = Engine();
        g.Idle((int)(60 * GameEngine.TitleIdleSeconds) + 5);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle(60 * 8);
        Assert.Contains(g.World.Log, l => l.StartsWith("searched 0"));
        g.Tick(default, start: true);
        Assert.Equal(GameState.Playing, g.State);
        Assert.Equal(0, g.EmbassyIndex);
        Assert.Equal(GameEngine.Lives0, g.Lives);
    }

    [Fact]
    public void Paused_the_clocks_stop()
    {
        var g = Engine();
        g.Go(0);
        g.Run(30);
        g.Pause();
        double clock = g.World.Spies[0].Clock;
        g.Run(120);
        Assert.Equal(clock, g.World.Spies[0].Clock);
        g.Resume();
        g.Run(10);
        Assert.True(g.World.Spies[0].Clock < clock);
    }

    [Fact]
    public void The_keys_walk_search_and_set_traps()
    {
        using var input = new InputHandler();
        input.OnKeyDown(Key.Up);
        Assert.Equal(-1, input.Player.Dy);
        input.OnKeyUp(Key.Up);
        input.OnKeyDown(Key.S);
        Assert.Equal(1, input.Player.Dy);
        input.OnKeyUp(Key.S);
        input.OnKeyDown(Key.Left);
        Assert.Equal(-1, input.Player.Dx);
        input.OnKeyUp(Key.Left);
        input.OnKeyDown(Key.Space);
        Assert.True(input.Player.Act);
        input.OnKeyUp(Key.Space);
        input.OnKeyDown(Key.X);
        Assert.True(input.Player.Trap);
        input.OnKeyUp(Key.X);
        input.OnKeyDown(Key.C);
        Assert.True(input.Player.Next);
        input.OnKeyUp(Key.C);

        // Through to the spy: up the screen is towards the back wall.
        var w = Fresh();
        input.OnKeyDown(Key.Up);
        Run(w, 20, input.Player);
        Assert.True(w.Spies[0].Z < 90);
    }

    [Fact]
    public void The_clock_running_low_is_heard_once()
    {
        var g = Engine();
        var heard = new List<Sfx>();
        g.Silent = false;
        g.Sound += heard.Add;
        g.Go(0);
        g.World.Spies[0].Clock = 21;
        g.Run(120);
        Assert.Single(heard, s => s == Sfx.TimeLow);
    }
}

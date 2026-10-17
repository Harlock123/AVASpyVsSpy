namespace AVASpyVsSpy.Engine;

public enum GameState { Title, Attract, Playing, Paused, GameOver, EnterInitials, HighScores }

public enum Phase
{
    /// <summary>The embassy named; a moment before the clocks start.</summary>
    Start,
    Play,
    /// <summary>One spy away (or the time gone): the result, then the next embassy, the same one again, or the end.</summary>
    Over,
}

/// <summary>Sounds the engine asks for; the host decides how they sound.</summary>
public enum Sfx
{
    Search, Nothing, Find, Remedy, AllFive, SetTrap, Bomb, Spring, Pail, Gun, Defuse, Swing, Hit, Die, Back, Door,
    Locked, Escape, OutOfTime, TimeLow, ExtraLife,
    Music, MusicStop, TitleTune, GameOver, Victory, Lost,
}

/// <summary>
/// The whole game with no UI. Call <see cref="Update"/> with the elapsed time and the controls; it runs in fixed
/// 60 Hz steps, so it plays the same at any frame rate. The player is the white spy; the computer plays the black.
/// </summary>
public sealed class GameEngine
{
    public const double TitleIdleSeconds = 16;
    public const double AttractSeconds = 60;
    public const double HighScoreSeconds = 9;
    public const double StartSeconds = 2.2, OverSeconds = 4, GameOverSeconds = 4;
    public const int Lives0 = 3, ExtraEvery = 10000, EscapeBonus = 1000, SecondPoints = 10;

    private double _accumulator, _idle;
    private bool _startPrev, _tunePending = true, _music, _warned;
    private int _nextExtra, _banked, _seed, _attempt;

    public GameEngine(HighScoreTable? table = null, int seed = 1)
    {
        HighScores = table ?? HighScoreTable.Load();
        _seed = seed;
        NewGame();
        State = GameState.Title;
    }

    public event Action<Sfx>? Sound;

    public GameState State { get; private set; } = GameState.Title;
    public Phase Phase { get; private set; } = Phase.Start;
    public double StateTime { get; private set; }
    public double PhaseTime { get; private set; }
    public HighScoreTable HighScores { get; }
    public InitialsEntry? Initials { get; private set; }
    public int NewEntryIndex { get; private set; } = -1;
    public bool Silent { get; set; }
    public World World { get; private set; } = new();
    /// <summary>The computer spies: the black one always; the white one too in the demonstration.</summary>
    public Pilot[] Pilots { get; } = [new(0), new(1)];
    public Pilot Pilot => Pilots[1];
    public int Lives { get; private set; }
    public int Score => _banked + World.Spies[0].Score;
    public int EmbassyIndex { get; private set; }
    /// <summary>Embassies got away from this game.</summary>
    public int Cleared { get; private set; }
    /// <summary>How the last mission ended: the white spy away (true), or not.</summary>
    public bool Won { get; private set; }
    public string? Message { get; private set; }

    private void Play(Sfx s)
    {
        if (!Silent) Sound?.Invoke(s);
    }

    public void Update(double dt, Controls c, bool start)
    {
        _accumulator += Math.Min(dt, 0.25);
        while (_accumulator >= Field.Step)
        {
            _accumulator -= Field.Step;
            Tick(c, start);
        }
    }

    public void Tick(Controls c, bool start = false)
    {
        double dt = Field.Step;
        StateTime += dt;
        bool startPressed = start && !_startPrev;
        _startPrev = start;
        switch (State)
        {
            case GameState.Title:
                if (_tunePending) { _tunePending = false; Play(Sfx.TitleTune); }
                _idle += dt;
                if (startPressed || c.Act && StateTime > 0.5) StartGame();
                else if (_idle > TitleIdleSeconds) StartAttract();
                break;
            case GameState.Attract:
                Simulate(dt, Pilots[0].Decide(World));
                if (startPressed) { StartGame(); break; }
                if (State == GameState.Attract && StateTime > AttractSeconds) { Music(false); ShowHighScores(-1); }
                break;
            case GameState.Playing:
                Simulate(dt, c);
                break;
            case GameState.GameOver:
                if (StateTime > GameOverSeconds)
                {
                    if (HighScores.Qualifies(Score))
                    {
                        State = GameState.EnterInitials;
                        StateTime = 0;
                        Initials = new InitialsEntry(HighScores.LastInitials);
                    }
                    else ShowHighScores(-1);
                }
                break;
            case GameState.EnterInitials:
                if (Initials is { IsDone: true } done)
                {
                    int place = HighScores.Insert(done.Initials, Score, Cleared);
                    HighScores.Save();
                    ShowHighScores(place);
                }
                break;
            case GameState.HighScores:
                if (startPressed) StartGame();
                else if (StateTime > HighScoreSeconds) ReturnToTitle();
                break;
        }
    }

    private void NewGame()
    {
        World = new World();
        Lives = Lives0;
        Cleared = 0;
        _banked = 0;
        _attempt = 0;
        _nextExtra = ExtraEvery;
        Enter(0);
    }

    /// <summary>Into an embassy: built afresh (a different one each try), with everything hidden anew.</summary>
    private void Enter(int index)
    {
        EmbassyIndex = index;
        _attempt++;
        int seed = _seed * 7919 + _attempt * 104729 + index;
        var e = EmbassyBook.Build(index, seed);
        // Round again after the last, the clocks a little shorter.
        e.Seconds = Math.Max(120, e.Seconds - index / EmbassyBook.Count * 30);
        Sim.Start(World, e, seed);
        _warned = false;
        SetPhase(Phase.Start);
        Message = e.Name;
        for (int i = 0; i < 2; i++) Pilots[i].Reset(seed + i);
        Pilots[0].Skill = 0.7;
        Pilots[1].Skill = State == GameState.Attract ? 0.7 : Math.Min(0.95, 0.35 + index * 0.12);
    }

    public void StartGame()
    {
        State = GameState.Playing;
        NewGame();
        StateTime = 0;
        NewEntryIndex = -1;
    }

    private void StartAttract()
    {
        State = GameState.Attract;
        _seed++;
        NewGame();
        StateTime = 0;
    }

    public void ReturnToTitle()
    {
        State = GameState.Title;
        StateTime = 0;
        _idle = 0;
        _tunePending = true;
        Music(false);
        Message = null;
        NewGame();
    }

    private void ShowHighScores(int place)
    {
        State = GameState.HighScores;
        StateTime = 0;
        NewEntryIndex = place;
    }

    public void Pause()
    {
        if (State != GameState.Playing) return;
        State = GameState.Paused;
        Music(false);
    }

    public void Resume()
    {
        if (State != GameState.Paused) return;
        State = GameState.Playing;
        if (Phase == Phase.Play) Music(true);
    }

    private void Music(bool on)
    {
        if (on == _music) return;
        _music = on;
        Play(on ? Sfx.Music : Sfx.MusicStop);
    }

    private void SetPhase(Phase p)
    {
        Phase = p;
        PhaseTime = 0;
    }

    private void Simulate(double dt, Controls c)
    {
        PhaseTime += dt;
        var w = World;
        switch (Phase)
        {
            case Phase.Start:
                if (PhaseTime > StartSeconds) { SetPhase(Phase.Play); Music(true); Message = null; }
                break;
            case Phase.Play:
            {
                int before = Score;
                Sim.Step(w, c, Pilots[1].Decide(w));
                if (Score != before) ExtraLives();
                var me = w.Spies[0];
                if (!_warned && me.Clock < 20 && me.Active) { _warned = true; Play(Sfx.TimeLow); }
                if (w.Winner >= 0 || me.Doing == Doing.OutOfTime || w.Spies.All(s => s.Doing == Doing.OutOfTime))
                {
                    Won = w.Winner == 0;
                    if (Won)
                    {
                        me.Score += EscapeBonus + (int)me.Clock * SecondPoints;
                        ExtraLives();
                    }
                    Message = Won ? "MISSION ACCOMPLISHED" : w.Winner == 1 ? "THE BLACK SPY GOT AWAY" : "OUT OF TIME";
                    SetPhase(Phase.Over);
                    Music(false);
                    Play(Won ? Sfx.Victory : Sfx.Lost);
                }
                break;
            }
            case Phase.Over:
                if (PhaseTime > OverSeconds)
                {
                    _banked += w.Spies[0].Score;
                    w.Spies[0].Score = 0;
                    if (Won)
                    {
                        Cleared++;
                        Enter(EmbassyIndex + 1);
                        break;
                    }
                    Lives--;
                    if (Lives <= 0)
                    {
                        Play(Sfx.GameOver);
                        if (State == GameState.Attract) { ShowHighScores(-1); return; }
                        State = GameState.GameOver;
                        StateTime = 0;
                        return;
                    }
                    Enter(EmbassyIndex);
                }
                break;
        }
        foreach (var s in w.Heard) Play(s);
        w.Heard.Clear();
    }

    private void ExtraLives()
    {
        while (Score >= _nextExtra)
        {
            _nextExtra += ExtraEvery;
            Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    // ───────────────────────────── for tests ─────────────────────────────

    public void Run(int steps, Controls c = default)
    {
        for (int i = 0; i < steps; i++) Tick(c);
    }

    public void Idle(int steps) => Run(steps);

    /// <summary>Straight into an embassy, past its name.</summary>
    public void Go(int embassy = 0)
    {
        State = GameState.Playing;
        NewGame();
        if (embassy != 0) Enter(embassy);
        SetPhase(Phase.Play);
        Message = null;
    }

    public void SetLives(int n) => Lives = n;
}

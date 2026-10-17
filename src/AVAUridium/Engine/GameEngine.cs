namespace AVAUridium.Engine;

public enum GameState { Title, Attract, Playing, Paused, GameOver, EnterInitials, HighScores }

/// <summary>How a dreadnought is going.</summary>
public enum Phase
{
    /// <summary>The dreadnought named; the Shrike coming in over its nose.</summary>
    Start,
    Play,
    /// <summary>Shot down: a moment, then the ship again further back along the deck, or the game is over.</summary>
    Dying,
    /// <summary>Down on the strip: the reactor's lights go round, and each press of fire stops them on a prize.</summary>
    Reactor,
    /// <summary>The ship away, and the dreadnought blowing up from end to end.</summary>
    Destruct,
}

/// <summary>Sounds the engine asks for; the host decides how they sound.</summary>
public enum Sfx
{
    Fire, Loop, Boom, BigBoom, Hit, PodDie, WaveIn, WaveBonus, EnemyFire, MineOut, Die, ExtraLife,
    LandNow, Land, Tick, Stop, Destruct,
    SayDread, SayLand, SayDestroyed,
    Music, MusicStop, TitleTune, GameOver, Victory,
}

/// <summary>
/// The whole game with no UI. Call <see cref="Update"/> with the elapsed time and the controls; it runs in fixed
/// 60 Hz steps, so it plays the same at any frame rate.
/// </summary>
public sealed class GameEngine
{
    public const double TitleIdleSeconds = 16;
    public const double AttractSeconds = 50;
    public const double HighScoreSeconds = 9;
    public const double StartSeconds = 2.5, DyingSeconds = 2.4, DestructSeconds = 4.5;
    public const int Lives0 = 3, FirstExtra = 10000, ExtraEvery = 25000;
    /// <summary>The reactor's lights: what each one is worth.</summary>
    public static readonly int[] ReactorCells = [500, 1000, 200, 2000, 500, 5000, 1000, 200];
    public const int ReactorStops = 3;

    private double _accumulator, _idle, _reactorClock;
    private bool _startPrev, _tunePending = true, _music, _firePrev;
    private readonly int _seed;
    private int _tries, _nextExtra;

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
    public Pilot Pilot { get; } = new();
    public int Lives { get; private set; }
    public int Score => World.Score;
    /// <summary>Dreadnoughts destroyed this game.</summary>
    public int Cleared { get; private set; }
    public string? Message { get; private set; }
    public string? SubMessage { get; private set; }
    public double MessageTime { get; private set; }
    /// <summary>The reactor: which light is lit, the stops left, and what's been won.</summary>
    public int ReactorLit { get; private set; }
    public int ReactorLeft { get; private set; }
    public int ReactorWon { get; private set; }
    /// <summary>The bonus for destroying the dreadnought.</summary>
    public int DestructBonus => 2000 + 1000 * World.Index;

    private void Play(Sfx s)
    {
        if (!Silent) Sound?.Invoke(s);
    }

    private void Say(string text, string? sub = null, double seconds = 2.5)
    {
        Message = text;
        SubMessage = sub;
        MessageTime = seconds;
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
        if (MessageTime > 0) { MessageTime -= dt; if (MessageTime <= 0) Message = SubMessage = null; }
        switch (State)
        {
            case GameState.Title:
                if (_tunePending) { _tunePending = false; Play(Sfx.TitleTune); }
                _idle += dt;
                if (startPressed || c.Fire && StateTime > 0.5) StartGame();
                else if (_idle > TitleIdleSeconds) StartAttract();
                break;
            case GameState.Attract:
                Simulate(dt, Pilot.Decide(this));
                if (startPressed) { StartGame(); break; }
                if (State == GameState.Attract && StateTime > AttractSeconds) { Music(false); ShowHighScores(-1); }
                break;
            case GameState.Playing:
                Simulate(dt, c);
                break;
            case GameState.GameOver:
                if (StateTime > 4)
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
        _tries = 0;
        _nextExtra = FirstExtra;
        Begin(0, 1);
    }

    private void Begin(int index, int round)
    {
        int score = World.Score;
        Sim.Start(World, index, round, _seed * 1000 + index * 31 + _tries++);
        World.Score = score;
        SetPhase(Phase.Start);
        Say("DREADNOUGHT " + World.Dread.Name, round > 1 ? $"ROUND {round}" : index == 0 ? "SHOOT THE FUEL PODS, THEN LAND" : null, StartSeconds);
    }

    public void StartGame()
    {
        NewGame();
        State = GameState.Playing;
        StateTime = 0;
        NewEntryIndex = -1;
    }

    private void StartAttract()
    {
        NewGame();
        State = GameState.Attract;
        StateTime = 0;
        Pilot.Reset();
    }

    public void ReturnToTitle()
    {
        State = GameState.Title;
        StateTime = 0;
        _idle = 0;
        _tunePending = true;
        Music(false);
        Message = SubMessage = null;
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

    private void GameOver()
    {
        Music(false);
        Play(Sfx.GameOver);
        if (State == GameState.Attract) { ShowHighScores(-1); return; }
        State = GameState.GameOver;
        StateTime = 0;
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
        bool fire = c.Fire && !_firePrev;
        _firePrev = c.Fire;
        switch (Phase)
        {
            case Phase.Start:
                if (PhaseTime > StartSeconds)
                {
                    SetPhase(Phase.Play);
                    Music(true);
                    Play(Sfx.SayDread);
                }
                break;
            case Phase.Play:
                int before = w.Score;
                bool order = w.LandNow;
                Sim.Step(w, c);
                if (w.Score != before) ExtraLives();
                if (w.LandNow && !order) Say("LAND NOW", "ON THE STRIP AT THE TAIL", 4);
                if (w.Ship.Dead) { SetPhase(Phase.Dying); Music(false); }
                else if (w.Landed)
                {
                    SetPhase(Phase.Reactor);
                    Music(false);
                    ReactorLit = 0;
                    ReactorLeft = ReactorStops;
                    ReactorWon = 0;
                    _reactorClock = 0;
                    Message = SubMessage = null;
                }
                break;
            case Phase.Dying:
                Sim.Step(w, default);
                if (PhaseTime > DyingSeconds)
                {
                    Lives--;
                    if (Lives <= 0) { GameOver(); return; }
                    Sim.Respawn(w);
                    SetPhase(Phase.Play);
                    Music(true);
                }
                break;
            case Phase.Reactor:
                Reactor(dt, fire);
                break;
            case Phase.Destruct:
                Sim.Step(w, default);
                if ((int)(PhaseTime / 0.35) != (int)((PhaseTime - dt) / 0.35)) Play(Sfx.BigBoom);
                if (PhaseTime > DestructSeconds)
                {
                    Cleared++;
                    bool last = w.Index == DreadBook.All.Length - 1;
                    if (last) Play(Sfx.Victory);
                    Begin(last ? 0 : w.Index + 1, last ? w.Round + 1 : w.Round);
                }
                break;
        }
        foreach (var s in w.Heard) Play(s);
        w.Heard.Clear();
    }

    /// <summary>The lights go round, quicker after each stop; fire stops them, and the lit prize is won.</summary>
    private void Reactor(double dt, bool fire)
    {
        if (ReactorLeft > 0)
        {
            double rate = 7 + 4 * (ReactorStops - ReactorLeft);
            _reactorClock += dt * rate;
            if (_reactorClock >= 1)
            {
                _reactorClock -= 1;
                ReactorLit = (ReactorLit + 1) % ReactorCells.Length;
                Play(Sfx.Tick);
            }
            if (fire && PhaseTime > 0.6)
            {
                int prize = ReactorCells[ReactorLit];
                ReactorWon += prize;
                World.Score += prize;
                ExtraLives();
                ReactorLeft--;
                Play(Sfx.Stop);
                if (ReactorLeft == 0) PhaseTime = 0;
            }
            return;
        }
        if (PhaseTime > 1.6)
        {
            World.Score += DestructBonus;
            ExtraLives();
            SetPhase(Phase.Destruct);
            Play(Sfx.Destruct);
            Play(Sfx.SayDestroyed);
            Say("DREADNOUGHT " + World.Dread.Name + " DESTROYED", $"BONUS {DestructBonus}", DestructSeconds);
        }
    }

    private void ExtraLives()
    {
        while (World.Score >= _nextExtra)
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

    /// <summary>Straight onto a dreadnought, past its name.</summary>
    public void Go(int index = 0, int round = 1)
    {
        NewGame();
        State = GameState.Playing;
        Begin(index, round);
        SetPhase(Phase.Play);
        Message = null;
    }

    public void SetLives(int n) => Lives = n;
}

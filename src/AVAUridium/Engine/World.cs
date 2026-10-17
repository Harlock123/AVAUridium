namespace AVAUridium.Engine;

/// <summary>A small random number generator whose state can be copied (so a copy of the world plays out the same).</summary>
public struct Dice
{
    private ulong _s;
    public Dice(int seed) => _s = (ulong)seed * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL;

    public double Next()
    {
        _s ^= _s << 13;
        _s ^= _s >> 7;
        _s ^= _s << 17;
        return (_s >> 11) * (1.0 / (1UL << 53));
    }

    public int Next(int n) => (int)(Next() * n);
    public double Between(double a, double b) => a + Next() * (b - a);
}

/// <summary>
/// The Shrike: where it is, which way it faces and how fast it's going, whether it's rolled on its side (to slip
/// through a narrow gap) or halfway through a loop (to turn about).
/// </summary>
public sealed class Ship
{
    public const double HalfWidth = 22, HalfHeight = 13, SideHalfHeight = 5;
    public const double MaxSpeed = 520, Accel = 650, Brake = 1100, Climb = 280, TurnTime = 0.55;
    public double X, Y, Vx, Vy, Turn, Roll, RollHold, FireCool, DeadTime, Safe;
    /// <summary>How long a roll is held after up or down is let go.</summary>
    public const double RollHoldTime = 0.6;
    public int Facing = 1;
    /// <summary>Which way up or down is held, and for how many steps.</summary>
    public int LastDy, DyHeld;
    public bool Dead;
    /// <summary>On its side: thin enough for a gap a single square high.</summary>
    public bool OnSide => Roll > 0.75;
    public double HalfH => OnSide ? SideHalfHeight : HalfHeight;
    public Ship Copy() => (Ship)MemberwiseClone();
}

public sealed class Shot
{
    public double X, Y, Vx, Life;
    public bool Dead;
    public Shot Copy() => (Shot)MemberwiseClone();
}

public sealed class Enemy
{
    public Foe Kind;
    public int Id, Hp = 1, Wave = -1, State;
    public double X, Y, Vx, Vy, T, FireIn, Hurt, HomeY;
    public bool Dead, Entered;
    public Enemy Copy() => (Enemy)MemberwiseClone();
    /// <summary>Standing on the deck (and so flown over, not into).</summary>
    public bool OnDeck => Kind is Foe.Gun or Foe.Pod or Foe.Port;
    public (double W, double H) Size => Kind switch
    {
        Foe.Mine => (9, 9),
        Foe.Gun => (13, 13),
        Foe.Pod => (15, 15),
        Foe.Port => (14, 14),
        Foe.Saucer => (16, 10),
        _ => (15, 10),
    };
}

/// <summary>A pellet fired at the ship.</summary>
public sealed class Missile
{
    public double X, Y, Vx, Vy, Life;
    public bool Dead;
    public Missile Copy() => (Missile)MemberwiseClone();
}

public enum EffectKind { Score, Burst, Word, Blast }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 0.8;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}

/// <summary>
/// Everything about a dreadnought run that moves or changes, kept together so it can be copied whole: the
/// computer pilot tries its moves out on a copy before making them.
/// </summary>
public sealed class World
{
    public DreadDef Dread = DreadBook.All[0];
    public int Index, Round = 1;
    public Ship Ship = new();
    public List<Shot> Shots = [];
    public List<Enemy> Enemies = [];
    public List<Missile> Missiles = [];
    public List<Effect> Effects = [];
    public double CamX, Clock, WaveIn;
    public int Score, NextId, PodsLeft;
    /// <summary>How many of each wave of fighters are still flying (or -1, once one has got away).</summary>
    public List<int> Waves = [];
    /// <summary>The order to land has come; the ship is down on the strip.</summary>
    public bool LandNow, Landed;
    public Dice Dice;
    /// <summary>Sounds asked for this step (the game plays them; a copy's are thrown away).</summary>
    public List<Sfx> Heard = [];
    public string Cause = "";

    public World Copy() => new()
    {
        Dread = Dread, Index = Index, Round = Round, Ship = Ship.Copy(),
        Shots = Shots.Select(s => s.Copy()).ToList(),
        Enemies = Enemies.Select(e => e.Copy()).ToList(),
        Missiles = Missiles.Select(m => m.Copy()).ToList(),
        CamX = CamX, Clock = Clock, WaveIn = WaveIn, Score = Score, NextId = NextId, PodsLeft = PodsLeft,
        Waves = [.. Waves], LandNow = LandNow, Landed = Landed, Dice = Dice, Cause = Cause,
    };
}

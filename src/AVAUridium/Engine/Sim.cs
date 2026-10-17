namespace AVAUridium.Engine;

/// <summary>
/// A run over a dreadnought, step by step: the Shrike's flying, rolling and looping, its guns, the waves of
/// fighters, the guns, pods and mine ports on the deck, the hits, and the order to land. Works on a
/// <see cref="World"/>, so the computer pilot can run it on copies.
/// </summary>
public static class Sim
{
    public const double ShotSpeed = 1100, ShotLife = 0.9;
    /// <summary>How long a dreadnought holds out before the order to land comes anyway.</summary>
    public const double LandAfter = 150;
    /// <summary>How slowly the ship must be going, heading for the tail, to come down on the strip.</summary>
    public const double LandingSpeed = 280;
    private const double Dt = Field.Step, T = Field.Tile;

    // ───────────────────────────── starting ─────────────────────────────

    public static void Start(World w, int index, int round, int seed)
    {
        var d = DreadBook.All[index];
        w.Dread = d;
        w.Index = index;
        w.Round = round;
        w.Shots.Clear();
        w.Enemies.Clear();
        w.Missiles.Clear();
        w.Effects.Clear();
        w.Waves.Clear();
        w.Heard.Clear();
        w.Clock = 0;
        w.WaveIn = 3;
        w.LandNow = w.Landed = false;
        w.Dice = new Dice(seed);
        w.Cause = "";
        foreach (var o in d.Objects)
        {
            var e = Add(w, o.Kind, (o.Col + 1) * T, Field.Top + (o.Row + 1) * T, o.Kind switch { Foe.Pod => 3, Foe.Gun => 2, Foe.Port => 4, _ => 1 });
            e.FireIn = w.Dice.Between(0.5, 2.5);
        }
        w.PodsLeft = d.Objects.Count(o => o.Kind == Foe.Pod);
        w.Ship = new Ship { X = 3 * T, Y = Field.Top + DreadBook.Mid * T, Vx = 200, Facing = 1, Safe = 2 };
        w.CamX = Camera(w);
    }

    /// <summary>
    /// After a loss: the ship again at the start of the stretch of deck it was lost in (or the one before), at a
    /// height clear of the towers, heading for the tail. What was shot on the deck stays shot.
    /// </summary>
    public static void Respawn(World w)
    {
        var d = w.Dread;
        double x0 = w.Ship.X;
        int col = 4;
        foreach (var (_, c1) in d.Fences)
            if ((c1 + 3) * T < x0 - 40) col = c1 + 3;
        int row = DreadBook.Mid;
        for (int k = 0; k < Field.Rows; k++)
        {
            int r = DreadBook.Mid + (k % 2 == 0 ? k / 2 : -(k / 2 + 1));
            if (r < 2 || r > Field.Rows - 3) continue;
            bool clear = true;
            for (int c = col - 2; c <= col + 8 && clear; c++)
            for (int rr = r - 2; rr <= r + 2 && clear; rr++)
                if (d.At(c, rr) == Tile.Wall) clear = false;
            if (clear) { row = r; break; }
        }
        w.Ship = new Ship { X = (col + 0.5) * T, Y = Field.Top + (row + 0.5) * T, Vx = 120, Facing = 1, Safe = 2.5 };
        w.Enemies.RemoveAll(e => !e.OnDeck);
        for (int i = 0; i < w.Waves.Count; i++) w.Waves[i] = -1;
        w.Missiles.Clear();
        w.Shots.Clear();
        w.WaveIn = 3;
        w.Cause = "";
        w.CamX = Camera(w);
    }

    private static double Camera(World w)
    {
        var s = w.Ship;
        double target = s.X - (s.Facing > 0 ? 340 : Field.Width - 340);
        return Math.Clamp(target, -320, w.Dread.Length - Field.Width + 320);
    }

    private static Enemy Add(World w, Foe kind, double x, double y, int hp = 1)
    {
        var e = new Enemy { Kind = kind, X = x, Y = y, HomeY = y, Hp = hp, Id = w.NextId++ };
        w.Enemies.Add(e);
        return e;
    }

    // ───────────────────────────── a step ─────────────────────────────

    public static void Step(World w, Controls c)
    {
        w.Clock += Dt;
        var s = w.Ship;
        if (s.Dead) s.DeadTime += Dt;
        else if (!w.Landed) Fly(w, c);
        double target = Camera(w);
        w.CamX += (target - w.CamX) * Math.Min(1, Dt * 3);
        Waves(w);
        MoveShots(w);
        for (int i = 0, n = w.Enemies.Count; i < n; i++)
            if (!w.Enemies[i].Dead) Behave(w, w.Enemies[i]);
        foreach (var m in w.Missiles)
        {
            m.X += m.Vx * Dt;
            m.Y += m.Vy * Dt;
            m.Life -= Dt;
            if (m.Life <= 0 || w.Dread.AtPoint(m.X, m.Y) == Tile.Wall) m.Dead = true;
        }
        Hits(w);
        Orders(w);
        foreach (var e in w.Effects) e.Age += Dt;
        w.Shots.RemoveAll(x => x.Dead);
        w.Enemies.RemoveAll(x => x.Dead);
        w.Missiles.RemoveAll(x => x.Dead);
        w.Effects.RemoveAll(x => x.Done);
    }

    // ───────────────────────────── the Shrike ─────────────────────────────

    private static void Fly(World w, Controls c)
    {
        var s = w.Ship;
        s.Safe -= Dt;
        s.FireCool -= Dt;
        // Up or down rolls it onto its side, and it stays rolled a moment after.
        if (c.Dy != 0) s.RollHold = Ship.RollHoldTime;
        else s.RollHold -= Dt;
        s.Roll += Math.Clamp((s.RollHold > 0 ? 1 : 0) - s.Roll, -Dt * 7, Dt * 7);
        int f = s.Facing;
        if (s.Turn > 0)
        {
            // Halfway through a loop: up and over on the spot, and back the other way.
            s.Turn -= Dt;
            s.Vx = 0;
            if (s.Turn <= 0)
            {
                s.Facing = -f;
                s.Vx = s.Facing * 140;
            }
        }
        else
        {
            double speed = s.Vx * f;
            if (c.Dx == f) speed = Math.Min(Ship.MaxSpeed, speed + Ship.Accel * Dt);
            else if (c.Dx == -f)
            {
                speed -= Ship.Brake * Dt;
                if (speed <= 60)
                {
                    speed = 0;
                    s.Turn = Ship.TurnTime;
                    w.Heard.Add(Sfx.Loop);
                }
            }
            else speed = Math.Max(0, speed - 30 * Dt);
            s.Vx = speed * f;
        }
        s.X += s.Vx * Dt;
        // Up or down held moves the ship; a tap only rolls it.
        s.DyHeld = c.Dy != 0 && c.Dy == s.LastDy ? s.DyHeld + 1 : c.Dy != 0 ? 1 : 0;
        s.LastDy = c.Dy;
        int climb = s.DyHeld >= 3 ? c.Dy : 0;
        double rate = climb != 0 ? 2400 : 3600;
        s.Vy += Math.Clamp(climb * Ship.Climb - s.Vy, -rate * Dt, rate * Dt);
        s.Y += s.Vy * Dt;
        s.X = Math.Clamp(s.X, -360, w.Dread.Length + 360);
        if (s.Y < Field.Top + Ship.HalfHeight || s.Y > Field.Bottom - Ship.HalfHeight) s.Vy = 0;
        s.Y = Math.Clamp(s.Y, Field.Top + Ship.HalfHeight, Field.Bottom - Ship.HalfHeight);
        if (c.Fire && s.FireCool <= 0 && s.Turn <= 0 && w.Shots.Count < 3)
        {
            w.Shots.Add(new Shot { X = s.X + s.Facing * 24, Y = s.Y, Vx = s.Facing * ShotSpeed + s.Vx, Life = ShotLife });
            s.FireCool = 0.09;
            w.Heard.Add(Sfx.Fire);
        }
        // Up over the top of a loop, it clears the towers; otherwise they're the end of it.
        if (s.Turn > 0) return;
        double hw = Ship.HalfWidth * 0.9, hh = s.HalfH * 0.9;
        foreach (var (px, py) in new[] { (s.X + f * Ship.HalfWidth, s.Y), (s.X - hw, s.Y - hh), (s.X + hw, s.Y - hh), (s.X - hw, s.Y + hh), (s.X + hw, s.Y + hh), (s.X, s.Y - hh), (s.X, s.Y + hh) })
            if (w.Dread.AtPoint(px, py) == Tile.Wall)
            {
                Kill(w, "the superstructure");
                return;
            }
    }

    public static void Kill(World w, string cause)
    {
        var s = w.Ship;
        if (s.Dead) return;
        s.Dead = true;
        s.DeadTime = 0;
        w.Cause = cause;
        w.Effects.Add(new Effect { Kind = EffectKind.Blast, X = s.X, Y = s.Y, Life = 1.2, Colour = 0xFFFFC040 });
        w.Heard.Add(Sfx.Die);
    }

    private static void MoveShots(World w)
    {
        foreach (var x in w.Shots)
        {
            x.X += x.Vx * Dt;
            x.Life -= Dt;
            if (x.Life <= 0 || x.X < w.CamX - 40 || x.X > w.CamX + Field.Width + 40 || w.Dread.AtPoint(x.X, x.Y) == Tile.Wall) x.Dead = true;
        }
    }

    // ───────────────────────────── the defences ─────────────────────────────

    /// <summary>How much fiercer things are: a little more each dreadnought, and more again each round.</summary>
    public static double Rf(World w) => 1 + 0.06 * w.Index + 0.3 * (w.Round - 1);

    private static void Waves(World w)
    {
        if (w.Landed || w.Ship.Dead) return;
        w.WaveIn -= Dt;
        if (w.WaveIn > 0) return;
        // Fewer once the order to land has come.
        w.WaveIn = w.Dice.Between(4.5, 7) / Rf(w) * (w.LandNow ? 1.8 : 1);
        if (w.Enemies.Count(e => !e.OnDeck && e.Kind != Foe.Mine) > 6) return;
        Foe[] pool = w.Index switch { 0 => [Foe.Dart, Foe.Saucer], 1 => [Foe.Dart, Foe.Saucer, Foe.Swooper], _ => [Foe.Dart, Foe.Saucer, Foe.Swooper, Foe.Chaser] };
        var kind = pool[w.Dice.Next(pool.Length)];
        // Mostly from ahead; sometimes from behind.
        int side = w.Dice.Next() < 0.7 ? w.Ship.Facing : -w.Ship.Facing;
        double x0 = side > 0 ? w.CamX + Field.Width + 40 : w.CamX - 40;
        double y = Field.Top + (4 + w.Dice.Next(Field.Rows - 8)) * T;
        int wave = w.Waves.Count;
        w.Waves.Add(5);
        double speed = kind switch { Foe.Dart => 380, Foe.Saucer => 250, Foe.Swooper => 330, _ => 240 } * Math.Min(1.4, Rf(w));
        for (int i = 0; i < 5; i++)
        {
            var e = Add(w, kind, x0 + side * i * 52, y);
            e.Vx = -side * speed;
            e.Wave = wave;
            e.FireIn = w.Dice.Between(1.2, 3.5);
            e.T = i * 0.3;
        }
        w.Heard.Add(Sfx.WaveIn);
    }

    private static void Behave(World w, Enemy e)
    {
        e.T += Dt;
        e.Hurt -= Dt;
        var s = w.Ship;
        double sx = s.X, sy = s.Y, rf = Rf(w);
        bool onScreen = e.X > w.CamX - 10 && e.X < w.CamX + Field.Width + 10;
        if (onScreen) e.Entered = true;
        switch (e.Kind)
        {
            case Foe.Dart:
                e.Y += Math.Clamp(sy - e.Y, -1, 1) * 50 * Dt;
                break;
            case Foe.Saucer:
                e.Y = e.HomeY + 90 * Math.Sin(e.T * 2.4);
                break;
            case Foe.Swooper:
                if (e.State == 0 && Math.Abs(e.X - sx) < 320 && Math.Sign(sx - e.X) == Math.Sign(e.Vx))
                {
                    e.State = 1;
                    e.Vy = Math.Clamp((sy - e.Y) * 2.2, -260, 260);
                }
                break;
            case Foe.Chaser:
            case Foe.Mine:
            {
                double speed = (e.Kind == Foe.Mine ? 170 : 210) * rf, turn = e.Kind == Foe.Mine ? 1.8 : 0.9;
                double dx = sx - e.X, dy = sy - e.Y, d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
                e.Vx += (dx / d * speed - e.Vx) * Dt * turn;
                e.Vy += (dy / d * speed - e.Vy) * Dt * turn;
                if (e.Kind == Foe.Mine && e.T > 6.5) { e.Dead = true; Burst(w, e.X, e.Y, 0xFF80FF80, 6); return; }
                break;
            }
            case Foe.Gun:
                if (onScreen && !s.Dead && Near(e, s, 650)) FireAt(w, e, 300, 2.0, 3.2);
                break;
            case Foe.Port:
                if (onScreen && !s.Dead && Near(e, s, 600))
                {
                    e.FireIn -= Dt;
                    if (e.FireIn <= 0)
                    {
                        e.FireIn = 3.4 / rf;
                        if (w.Enemies.Count(m => m.Kind == Foe.Mine) < 4)
                        {
                            var m = Add(w, Foe.Mine, e.X, e.Y);
                            m.Entered = true;
                            w.Heard.Add(Sfx.MineOut);
                        }
                    }
                }
                break;
        }
        if (e.OnDeck) return;
        e.X += e.Vx * Dt;
        if (e.Kind != Foe.Saucer) e.Y += e.Vy * Dt;
        if (e.Kind is not (Foe.Mine or Foe.Chaser) && onScreen) FireAt(w, e, 330, 1.6, 3.4);
        if (e.Entered && (e.X < w.CamX - 260 || e.X > w.CamX + Field.Width + 260 || e.Y < Field.Top - 60 || e.Y > Field.Bottom + 60))
        {
            e.Dead = true;
            if (e.Wave >= 0) w.Waves[e.Wave] = -1;
        }
    }

    private static bool Near(Enemy e, Ship s, double d) => Math.Abs(e.X - s.X) < d && Math.Abs(e.Y - s.Y) < d;

    private static void FireAt(World w, Enemy e, double speed, double min, double max)
    {
        e.FireIn -= Dt;
        if (e.FireIn > 0) return;
        double rf = Rf(w);
        e.FireIn = w.Dice.Between(min, max) / rf;
        var s = w.Ship;
        if (s.Dead || w.Landed) return;
        double dx = s.X - e.X, dy = s.Y - e.Y, d = Math.Max(1, Math.Sqrt(dx * dx + dy * dy));
        if (d < 120) return;
        w.Missiles.Add(new Missile { X = e.X, Y = e.Y, Vx = dx / d * speed * Math.Min(1.4, rf) + (e.OnDeck ? 0 : e.Vx * 0.3), Vy = dy / d * speed * Math.Min(1.4, rf), Life = 3 });
        w.Heard.Add(Sfx.EnemyFire);
    }

    // ───────────────────────────── hits ─────────────────────────────

    private static void Hits(World w)
    {
        foreach (var x in w.Shots)
        {
            if (x.Dead) continue;
            foreach (var e in w.Enemies)
            {
                if (e.Dead) continue;
                var (hw, hh) = e.Size;
                if (Math.Abs(x.X - e.X) > hw + 10 || Math.Abs(x.Y - e.Y) > hh + 3) continue;
                x.Dead = true;
                e.Hp--;
                e.Hurt = 0.1;
                if (e.Hp <= 0) Destroy(w, e);
                else w.Heard.Add(Sfx.Hit);
                break;
            }
        }
        var s = w.Ship;
        if (s.Dead || w.Landed || s.Safe > 0) return;
        foreach (var e in w.Enemies)
        {
            if (e.Dead || e.OnDeck) continue;
            var (hw, hh) = e.Size;
            if (Math.Abs(s.X - e.X) > hw + Ship.HalfWidth * 0.75 || Math.Abs(s.Y - e.Y) > hh + s.HalfH * 0.85) continue;
            Destroy(w, e);
            Kill(w, Name(e.Kind));
            return;
        }
        foreach (var m in w.Missiles)
        {
            if (m.Dead || Math.Abs(m.X - s.X) > Ship.HalfWidth * 0.7 + 3 || Math.Abs(m.Y - s.Y) > s.HalfH * 0.8 + 3) continue;
            m.Dead = true;
            Kill(w, "a shot");
            return;
        }
    }

    public static string Name(Foe f) => f switch
    {
        Foe.Dart => "a dart",
        Foe.Saucer => "a saucer",
        Foe.Swooper => "a swooper",
        Foe.Chaser => "a chaser",
        Foe.Mine => "a mine",
        _ => f.ToString(),
    };

    public static int Points(Foe f) => f switch
    {
        Foe.Dart => 150,
        Foe.Saucer => 150,
        Foe.Swooper => 200,
        Foe.Chaser => 250,
        Foe.Mine => 50,
        Foe.Gun => 300,
        Foe.Pod => 1500,
        _ => 600,
    };

    public const int WaveBonus = 1000;

    public static void Destroy(World w, Enemy e)
    {
        if (e.Dead) return;
        e.Dead = true;
        int pts = Points(e.Kind);
        w.Score += pts;
        Burst(w, e.X, e.Y, e.Kind == Foe.Pod ? 0xFF60FFB0 : 0xFFFFD080, e.OnDeck ? 14 : 9);
        w.Heard.Add(e.Kind == Foe.Pod ? Sfx.PodDie : e.OnDeck ? Sfx.BigBoom : Sfx.Boom);
        if (e.OnDeck) w.Effects.Add(new Effect { Kind = EffectKind.Score, X = e.X, Y = e.Y - 18, Text = pts.ToString(), Colour = 0xFFFFE070 });
        if (e.Kind == Foe.Pod) w.PodsLeft--;
        if (e.Wave >= 0 && w.Waves[e.Wave] > 0 && --w.Waves[e.Wave] == 0)
        {
            w.Score += WaveBonus;
            w.Effects.Add(new Effect { Kind = EffectKind.Word, X = e.X, Y = e.Y - 20, Text = "WAVE BONUS " + WaveBonus, Life = 1.4, Colour = 0xFF80FFFF });
            w.Heard.Add(Sfx.WaveBonus);
        }
    }

    private static void Burst(World w, double x, double y, uint colour, int n) =>
        w.Effects.Add(new Effect { Kind = EffectKind.Burst, X = x, Y = y, Text = n.ToString(), Life = 0.5, Colour = colour });

    // ───────────────────────────── landing ─────────────────────────────

    private static void Orders(World w)
    {
        if (!w.LandNow && (w.PodsLeft <= 0 || w.Clock > LandAfter))
        {
            w.LandNow = true;
            w.Heard.Add(Sfx.LandNow);
            w.Heard.Add(Sfx.SayLand);
        }
        var s = w.Ship;
        if (w.LandNow && !w.Landed && CanLand(w, s))
        {
            w.Landed = true;
            s.Vx = 0;
            s.Roll = 0;
            w.Missiles.Clear();
            foreach (var e in w.Enemies) if (!e.OnDeck) e.Dead = true;
            w.Heard.Add(Sfx.Land);
        }
    }

    /// <summary>Whether the ship, where it is now, is set to come down on the strip: over it, slow, level, heading for the tail.</summary>
    public static bool CanLand(World w, Ship s) =>
        !s.Dead && s.Turn <= 0 && s.Facing == 1 && s.Vx >= 0 && s.Vx < LandingSpeed && s.Roll < 0.6 && w.Dread.OnRunway(s.X, s.Y);
}

namespace AVAUridium.Engine;

/// <summary>
/// The computer pilot, for the demonstration and the tests. It knows where it wants to be (the nearest fuel pod
/// it can see, or, once the order comes, the landing strip) and has a map of how far every square is from there,
/// around the fences and towers. From that it knows what a pilot would do: follow the map, line up on a pod and
/// shoot, slow down onto the strip. Every few steps it tries each of the nine ways to steer, followed by flying on
/// that way, on copies of the world, and takes the way it wants if that's safe (or else the safest), firing all
/// the while. On the strip it stops the reactor's lights on the biggest prize.
/// </summary>
public sealed class Pilot
{
    private int _held, _tick;
    private bool _fired;
    private int[] _far = [];
    private int _farCols;
    private static readonly (int Dx, int Dy)[] Moves = [(0, 0), (1, 0), (-1, 0), (0, -1), (0, 1), (1, -1), (1, 1), (-1, -1), (-1, 1)];
    public const int Hold = 8, Ahead = 36, Margin = 24, Stand = 190;

    public string Doing { get; private set; } = "";
    /// <summary>For tuning: each move tried and what it was worth, when set.</summary>
    public List<string>? Trace;
    /// <summary>Where the pilot is making for.</summary>
    public (double X, double Y) Goal { get; private set; }
    private Enemy? _pod;

    public void Reset()
    {
        _held = 0;
        _move = default;
        _fired = false;
        _pod = null;
        Doing = "";
    }

    public Controls Decide(GameEngine g)
    {
        var w = g.World;
        _tick++;
        if (g.Phase == Phase.Reactor)
        {
            // Fire just as the biggest prize lights.
            int best = Array.IndexOf(GameEngine.ReactorCells, GameEngine.ReactorCells.Max());
            bool fire = g.ReactorLit == best && !_fired;
            _fired = fire;
            return new Controls(0, 0, fire);
        }
        if (g.Phase != Phase.Play || w.Ship.Dead) { _held = 0; return default; }
        if (_held > 0)
        {
            _held--;
            return Act(_move, _tick);
        }
        Map(w);
        var want = Want(w, _tick);
        string doing = Doing;
        double bestValue = double.MinValue;
        var pick = want;
        foreach (var m in Moves.Append(want).Distinct())
        {
            double value = Try(w, m) + (m == want ? 400 : 0);
            if (Trace != null) Trace.Add($"{m}={value:0}{(m == want ? "*" : "")}");
            if (value > bestValue) { bestValue = value; pick = m; }
        }
        _move = pick;
        _held = 3;
        Doing = pick == want ? doing : "dodging";
        return Act(pick, _tick);
    }

    private (int Dx, int Dy) _move;

    /// <summary>A move as controls: up and down in turn, a step at a time, to rock the ship and hold its roll.</summary>
    private static Controls Act((int Dx, int Dy) m, int tick) => new(m.Dx, m.Dy == Rock ? (tick % 2 == 0 ? 1 : -1) : m.Dy, true);

    /// <summary>The up-and-down rocking that keeps the ship on its side.</summary>
    public const int Rock = 2;

    // ───────────────────────────── where to, and how ─────────────────────────────

    /// <summary>The goal, and how far each square is from it (in squares, round the walls).</summary>
    private void Map(World w)
    {
        var d = w.Dread;
        var s = w.Ship;
        _pod = null;
        if (w.LandNow)
        {
            var (_, ry) = d.RunwayAt;
            Goal = ((d.Runway.C0 - 6) * Field.Tile, ry);
        }
        else
        {
            double near = double.MaxValue;
            foreach (var e in w.Enemies)
            {
                if (e.Kind != Foe.Pod || e.Dead) continue;
                double dist = Math.Abs(e.X - s.X) + 3 * Math.Abs(e.Y - s.Y);
                if (dist < near) { near = dist; _pod = e; }
            }
            Goal = _pod != null ? (_pod.X, _pod.Y) : d.RunwayAt;
        }
        _farCols = d.Cols + 2 * Margin;
        int n = _farCols * Field.Rows;
        if (_far.Length != n || _costFor != d) { _far = new int[n]; _cost = Costs(d, _farCols); }
        Array.Fill(_far, int.MaxValue);
        int gc = Math.Clamp((int)Math.Floor(Goal.X / Field.Tile) + Margin, 0, _farCols - 1), gr = Math.Clamp((int)((Goal.Y - Field.Top) / Field.Tile), 0, Field.Rows - 1);
        // Round the walls, keeping clear of them where there's room.
        var queue = new PriorityQueue<int, int>();
        _far[gr * _farCols + gc] = 0;
        queue.Enqueue(gr * _farCols + gc, 0);
        while (queue.TryDequeue(out int i, out int dist))
        {
            if (dist > _far[i]) continue;
            int c = i % _farCols, r = i / _farCols;
            foreach (var (dc, dr) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                int nc = c + dc, nr = r + dr;
                if (nc < 0 || nc >= _farCols || nr < 0 || nr >= Field.Rows) continue;
                int j = nr * _farCols + nc;
                if (_cost[j] == int.MaxValue) continue;
                int nd = dist + _cost[j];
                if (nd >= _far[j]) continue;
                _far[j] = nd;
                queue.Enqueue(j, nd);
            }
        }
    }

    private int[] _cost = [];
    private DreadDef? _costFor;

    /// <summary>What each square costs to cross: a ship's length clear of walls side to side, and dearer the nearer walls are.</summary>
    private int[] Costs(DreadDef d, int cols)
    {
        _costFor = d;
        var cost = new int[cols * Field.Rows];
        for (int c = 0; c < cols; c++)
        for (int r = 0; r < Field.Rows; r++)
        {
            int dc0 = c - Margin;
            bool open = true;
            for (int k = -2; k <= 2 && open; k++) if (d.At(dc0 + k, r) == Tile.Wall) open = false;
            if (!open) { cost[r * cols + c] = int.MaxValue; continue; }
            int near = 0;
            for (int k = -3; k <= 3; k++)
            for (int m = -2; m <= 2; m++)
                if (d.At(dc0 + k, r + m) == Tile.Wall) near = Math.Max(near, 3 - Math.Max(Math.Abs(m), Math.Abs(k) / 2));
            cost[r * cols + c] = 2 + near * 3;
        }
        return cost;
    }

    /// <summary>A square the ship (on its side, at least) can be in: no wall in it or either side of it.</summary>
    private static bool Open(DreadDef d, int c, int r) => d.At(c, r) != Tile.Wall && d.At(c - 1, r) != Tile.Wall && d.At(c + 1, r) != Tile.Wall;

    private int Far(int c, int r)
    {
        c += Margin;
        if (c < 0 || c >= _farCols || r < 0 || r >= Field.Rows) return int.MaxValue;
        return _far[r * _farCols + c];
    }

    /// <summary>Towers just ahead, near the ship's row.</summary>
    private static bool Towers(DreadDef d, int c, int r, int f)
    {
        for (int k = -2; k <= 7; k++)
        for (int dr = -2; dr <= 2; dr++)
            if (d.At(c + f * k, r + dr) == Tile.Wall) return true;
        return false;
    }

    /// <summary>What a pilot would do now: follow the map; near a pod in plain sight, line up and shoot; onto the strip, slowly.</summary>
    private (int Dx, int Dy) Want(World w, int tick)
    {
        var s = w.Ship;
        var d = w.Dread;
        int c = (int)Math.Floor(s.X / Field.Tile), r = (int)((s.Y - Field.Top) / Field.Tile);
        int f = s.Facing;
        if (s.Turn > 0) return (0, 0);
        // A pod in sight along the ship's row: hang back from it, in line, facing it.
        if (_pod != null && !w.LandNow && Math.Abs(_pod.X - s.X) < 520 && Clear(d, s.X, _pod.X, _pod.Y))
        {
            Doing = "attack";
            int side = Math.Sign(_pod.X - s.X);
            double off = Math.Abs(_pod.X - s.X);
            int dy = Math.Abs(_pod.Y - s.Y) > 3 ? Math.Sign(_pod.Y - s.Y) : 0;
            if (side != f) return off < Stand ? (f, dy) : (-f, dy);
            return (off > Stand + 80 && s.Vx * f < 160 ? f : 0, dy);
        }
        // Down onto the strip.
        if (w.LandNow && s.X > Math.Max(d.Runway.C0 - 30, d.Fences.Count > 0 ? d.Fences[^1].C1 + 3 : 0) * Field.Tile && s.X < (d.Runway.C1 - 4) * Field.Tile)
        {
            Doing = "landing";
            var (_, ry) = d.RunwayAt;
            int dy = Math.Abs(ry - s.Y) > 6 ? Math.Sign(ry - s.Y) : 0;
            if (f < 0) return (1, dy);
            double v = s.Vx;
            return (v < 110 ? 1 : v > Sim.LandingSpeed - 40 ? -1 : 0, dy);
        }
        // Along the map: toward the nearest square a few steps nearer the goal.
        Doing = w.LandNow ? "to the strip" : "hunting";
        // Down the map a step at a time (round the walls, not through them), and make for the furthest square
        // along it that the ship can fly straight to without clipping a wall.
        int here = Far(c, r);
        int bc = c, br = r;
        if (here == int.MaxValue) { bc = (int)(Goal.X / Field.Tile); br = (int)((Goal.Y - Field.Top) / Field.Tile); }
        else
        {
            var path = new List<(int C, int R)>();
            int pc = c, pr = r;
            for (int step = 0; step < 14; step++)
            {
                int best = Far(pc, pr), nc = pc, nr = pr;
                foreach (var (dc, dr) in new[] { (f, 0), (0, 1), (0, -1), (-f, 0) })
                {
                    int v = Far(pc + dc, pr + dr);
                    if (v < best) { best = v; nc = pc + dc; nr = pr + dr; }
                }
                if (nc == pc && nr == pr) break;
                pc = nc;
                pr = nr;
                path.Add((pc, pr));
            }
            for (int k = path.Count - 1; k >= 0; k--)
            {
                var (qc, qr) = path[k];
                if (k == 0 || Straight(d, s.X, s.Y, (qc + 0.5) * Field.Tile, Field.Top + (qr + 0.5) * Field.Tile)) { bc = qc; br = qr; break; }
            }
        }
        double tx = (bc + 0.5) * Field.Tile, ty = Field.Top + (br + 0.5) * Field.Tile;
        // Only turn about for somewhere well behind; a little way behind, just drift.
        double ahead = (tx - s.X) * f;
        int wantDx = ahead > 10 ? f : ahead < -72 ? -f : 0;
        int wantDy = Math.Abs(ty - s.Y) > 2.5 ? Math.Sign(ty - s.Y) : 0;
        // Near towers: on its side (rocking to hold the roll, keeping to the row), and not too fast.
        bool towers = Towers(d, c, r, f);
        if (towers && wantDy == 0) wantDy = Rock;
        if (wantDx == f && s.Vx * f > (towers ? 220 : 460)) wantDx = 0;
        return (wantDx, wantDy);
    }

    /// <summary>Whether the ship (on its side) can fly in a straight line between two points without touching a wall.</summary>
    private static bool Straight(DreadDef d, double x0, double y0, double x1, double y1)
    {
        int n = (int)Math.Max(2, Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0)) / 6);
        for (int i = 0; i <= n; i++)
        {
            double x = x0 + (x1 - x0) * i / n, y = y0 + (y1 - y0) * i / n;
            foreach (double ox in new[] { -Ship.HalfWidth, 0, Ship.HalfWidth })
            foreach (double oy in new[] { -Ship.SideHalfHeight - 2, 0, Ship.SideHalfHeight + 2 })
                if (d.AtPoint(x + ox, y + oy) == Tile.Wall) return false;
        }
        return true;
    }

    /// <summary>No wall between two x along a row.</summary>
    private static bool Clear(DreadDef d, double x0, double x1, double y)
    {
        double a = Math.Min(x0, x1), b = Math.Max(x0, x1);
        for (double x = a; x <= b; x += Field.Tile / 2)
            if (d.AtPoint(x, y) == Tile.Wall) return false;
        return true;
    }

    // ───────────────────────────── trying it out ─────────────────────────────

    private double Try(World start, (int Dx, int Dy) m)
    {
        var w = start.Copy();
        Run(w, m, Hold);
        int tick = Hold;
        for (int i = 0; i < Ahead && !w.Ship.Dead && !w.Landed; i++)
        {
            Sim.Step(w, Act(Want(w, tick + i), tick + i));
        }
        if (Trace != null && w.Ship.Dead) Trace.Add($"<{m} dies: {w.Cause} at {w.Ship.X / Field.Tile:0.0},{(w.Ship.Y - Field.Top) / Field.Tile:0.0} after {w.Clock - start.Clock:0.00}s vx {w.Ship.Vx:0}>");
        return Value(start, w);
    }

    private static void Run(World w, (int Dx, int Dy) m, int steps)
    {
        for (int i = 0; i < steps && !w.Ship.Dead && !w.Landed; i++) Sim.Step(w, Act(m, i));
    }

    private double Value(World before, World after)
    {
        var s = after.Ship;
        if (s.Dead) return -1e6 + after.Clock * 1000;
        if (after.Landed) return 1e5 - after.Clock;
        double v = (after.Score - before.Score) * 0.5;
        int c = (int)Math.Floor(s.X / Field.Tile), r = (int)((s.Y - Field.Top) / Field.Tile);
        int far = Far(c, r);
        v -= Math.Min(far, 2000) * 3;
        foreach (var m in after.Missiles)
        {
            double d = Math.Sqrt((m.X - s.X) * (m.X - s.X) + (m.Y - s.Y) * (m.Y - s.Y));
            if (d < 70) v -= (70 - d) * 10;
        }
        foreach (var e in after.Enemies)
        {
            if (e.OnDeck) continue;
            double d = Math.Sqrt((e.X - s.X) * (e.X - s.X) + (e.Y - s.Y) * (e.Y - s.Y));
            if (d < 100) v -= (100 - d) * 8;
        }
        return v;
    }
}

namespace AVAUridium.Engine;

/// <summary>Left and right, up and down (up the screen is -1), and fire (held for a stream of shots).</summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Fire = false);

/// <summary>The screen: the score along the top, a window onto the dreadnought 64 squares wide and 40 high, and the scanner below.</summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768, Hud = 48;
    public const double Tile = 16;
    public const int Rows = 40, ViewCols = 64;
    public const double Top = Hud, Bottom = Top + Rows * Tile;
}

/// <summary>What a square of a dreadnought is, seen from above.</summary>
public enum Tile : byte
{
    /// <summary>Open space, off the edges of the hull.</summary>
    Space,
    /// <summary>The hull's deck: flown over.</summary>
    Hull,
    /// <summary>Superstructure standing up off the deck: towers, fins, the walls of the fences. Flying into it is the end.</summary>
    Wall,
    /// <summary>The landing strip, near the tail.</summary>
    Runway,
}

public enum Foe
{
    // Fighters, which come in waves of five.
    /// <summary>Straight across, edging toward the ship's height.</summary>
    Dart,
    /// <summary>Weaving up and down.</summary>
    Saucer,
    /// <summary>Across, then a dive at the ship.</summary>
    Swooper,
    /// <summary>Hunts the ship.</summary>
    Chaser,
    /// <summary>A homing mine, from a port in the deck.</summary>
    Mine,
    // On the deck.
    /// <summary>A gun on the deck that fires at the ship.</summary>
    Gun,
    /// <summary>A fuel pod: shoot them all (or hold out long enough) and the dreadnought must be landed on.</summary>
    Pod,
    /// <summary>A port that lets out mines.</summary>
    Port,
}

/// <summary>Something on a dreadnought's deck, at a column and row.</summary>
public readonly record struct Placed(Foe Kind, int Col, int Row);

/// <summary>
/// A dreadnought: its deck and superstructure square by square, the fences across it with their gaps, what
/// stands on its deck, and its landing strip.
/// </summary>
public sealed class DreadDef
{
    public string Name = "";
    public int Index, Cols;
    public Tile[,] Grid = new Tile[0, 0];
    public List<(int C0, int C1)> Fences = [];
    public List<Placed> Objects = [];
    public (int C0, int C1, int R0, int R1) Runway;

    public double Length => Cols * Field.Tile;

    public Tile At(int c, int r) => r < 0 || r >= Field.Rows || c < 0 || c >= Cols ? Tile.Space : Grid[c, r];
    public Tile AtPoint(double x, double y) => At((int)Math.Floor(x / Field.Tile), (int)Math.Floor((y - Field.Top) / Field.Tile));

    public bool OnRunway(double x, double y)
    {
        int c = (int)Math.Floor(x / Field.Tile), r = (int)Math.Floor((y - Field.Top) / Field.Tile);
        return c >= Runway.C0 && c <= Runway.C1 && r >= Runway.R0 && r <= Runway.R1;
    }

    /// <summary>The middle of the runway.</summary>
    public (double X, double Y) RunwayAt => ((Runway.C0 + Runway.C1 + 1) / 2.0 * Field.Tile, Field.Top + (Runway.R0 + Runway.R1 + 1) / 2.0 * Field.Tile);
}

/// <summary>The eight dreadnoughts, each built from its own seed: longer, more walled and better defended in turn.</summary>
public static class DreadBook
{
    public static readonly string[] Names = ["BASALT", "QUARTZ", "JASPER", "GARNET", "OBSIDIAN", "BERYL", "ONYX", "TOPAZ"];

    public static readonly DreadDef[] All = Names.Select((n, i) => Build(n, i, 300 + 24 * i, 101 + i * 37)).ToArray();

    /// <summary>The middle of the hull, top to bottom.</summary>
    public const int Mid = Field.Rows / 2;

    public static DreadDef Build(string name, int index, int cols, int seed)
    {
        var d = new Dice(seed);
        var s = new DreadDef { Name = name, Index = index, Cols = cols, Grid = new Tile[cols, Field.Rows] };
        // The hull: tapering at the nose and the tail, the full height of the screen less a strip of space top and bottom.
        for (int c = 0; c < cols; c++)
        {
            int taper = Math.Min(c - 6, cols - 7 - c);
            int half = taper < 0 ? -1 : Math.Min(16, 3 + taper);
            for (int r = 0; r < Field.Rows; r++)
                s.Grid[c, r] = half >= 0 && r >= Mid - half && r < Mid + half ? Tile.Hull : Tile.Space;
        }
        // The landing strip near the tail.
        s.Runway = (cols - 44, cols - 18, Mid - 2, Mid + 1);
        for (int c = s.Runway.C0; c <= s.Runway.C1; c++)
        for (int r = s.Runway.R0; r <= s.Runway.R1; r++)
            s.Grid[c, r] = Tile.Runway;
        // Fences across the whole height, each with gaps (later on, some only a ship on its side gets through).
        int col = 26;
        while (col < s.Runway.C0 - 14)
        {
            int thick = 1 + d.Next(index >= 3 ? 3 : 2);
            bool narrow = d.Next() < 0.07 * index;
            int gaps = 1 + d.Next(3);
            var open = new bool[Field.Rows];
            for (int g = 0; g < gaps; g++)
            {
                int size = narrow ? 2 : 3 + d.Next(3);
                int row = 1 + d.Next(Field.Rows - 2 - size);
                for (int r = row; r < row + size; r++) open[r] = true;
            }
            for (int c = col; c < col + thick; c++)
            for (int r = 0; r < Field.Rows; r++)
                if (!open[r]) s.Grid[c, r] = Tile.Wall;
            s.Fences.Add((col, col + thick - 1));
            col += thick + 16 + d.Next(14) - Math.Min(index, 4);
        }
        // Towers standing on the deck between the fences.
        var bounds = new List<int> { 8 };
        foreach (var (c0, c1) in s.Fences) { bounds.Add(c0); bounds.Add(c1 + 1); }
        bounds.Add(s.Runway.C0 - 6);
        for (int i = 0; i + 1 < bounds.Count; i += 2)
        {
            int a = bounds[i] + 4, b = bounds[i + 1] - 6;
            if (b - a < 6 || d.Next() < 0.35) continue;
            int w = 2 + d.Next(2), h = 3 + d.Next(4);
            int c0 = a + d.Next(b - a - w), r0 = 6 + d.Next(Field.Rows - 12 - h);
            for (int c = c0; c < c0 + w; c++)
            for (int r = r0; r < r0 + h; r++)
                if (s.Grid[c, r] == Tile.Hull) s.Grid[c, r] = Tile.Wall;
        }
        // On the deck: fuel pods, guns and mine ports, apart from each other and clear of the walls.
        bool Run(int c0, int c1, int r)
        {
            for (int c = c0; c <= c1; c++)
            for (int rr = r - 1; rr <= r + 2; rr++)
                if (s.At(c, rr) == Tile.Wall) return false;
            return true;
        }
        void Place(Foe kind, int count)
        {
            for (int tries = 0; count > 0 && tries < 4000; tries++)
            {
                int c = 12 + d.Next(s.Runway.C0 - 18), r = 7 + d.Next(Field.Rows - 14);
                bool clear = true;
                for (int dc = -2; dc <= 3 && clear; dc++)
                for (int dr = -2; dr <= 3 && clear; dr++)
                    if (s.At(c + dc, r + dr) != Tile.Hull) clear = false;
                if (!clear || s.Objects.Any(o => Math.Abs(o.Col - c) < 6 && Math.Abs(o.Row - r) < 6)) continue;
                // A pod has a clear run along its rows on one side at least, to be shot from.
                if (kind == Foe.Pod && !Run(c - 16, c - 1, r) && !Run(c + 2, c + 17, r)) continue;
                s.Objects.Add(new Placed(kind, c, r));
                count--;
            }
        }
        Place(Foe.Pod, 8 + index);
        Place(Foe.Gun, 5 + index);
        Place(Foe.Port, 2 + index / 2);
        return s;
    }
}

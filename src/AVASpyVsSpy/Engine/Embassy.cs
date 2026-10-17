namespace AVASpyVsSpy.Engine;

/// <summary>
/// A spy's controls: walking (left and right across the room, up towards the back wall and down towards the front),
/// the action button (search, or swing the club), the trap button (set the chosen trap), and the button that picks
/// the next trap.
/// </summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Act = false, bool Trap = false, bool Next = false);

/// <summary>The screen and a room's floor, in the room's own units.</summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    /// <summary>A room's floor: across (x) and from the back wall (z = 0) to the front (z = Depth).</summary>
    public const double RoomW = 600, Depth = 180;
    /// <summary>The middle of the back and front doors, and of the side doors.</summary>
    public const double DoorX = RoomW / 2, DoorZ = Depth / 2;
    public const double DoorHalf = 42;
}

/// <summary>Which wall of a room.</summary>
public enum Side { North, East, South, West }

/// <summary>The secret things: all five, the case to carry them in among them, are needed to get away.</summary>
public enum Item { Briefcase, Plans, Key, Money, Passport }

/// <summary>Traps: the bomb and the spring go in furniture; the pail of water and the gun on a string over doors.</summary>
public enum TrapKind { Bomb, Spring, Pail, Gun }

/// <summary>What gets a spy through each trap: water for the bomb's fuse, cutters for the spring, an umbrella for the pail, scissors for the string.</summary>
public enum Remedy { None, Water, Cutters, Umbrella, Scissors }

/// <summary>The furniture against a room's back wall, each worth searching.</summary>
public enum Piece { Desk, Cabinet, Bookcase, Dresser, Safe, Locker, Bureau, Clock }

public static class Kinds
{
    public static Remedy Cure(TrapKind t) => t switch
    {
        TrapKind.Bomb => Remedy.Water,
        TrapKind.Spring => Remedy.Cutters,
        TrapKind.Pail => Remedy.Umbrella,
        _ => Remedy.Scissors,
    };

    public static bool OnDoor(TrapKind t) => t is TrapKind.Pail or TrapKind.Gun;

    public static (int Dc, int Dr) Step(Side s) => s switch
    {
        Side.North => (0, -1),
        Side.East => (1, 0),
        Side.South => (0, 1),
        _ => (-1, 0),
    };

    public static Side Opposite(Side s) => (Side)(((int)s + 2) % 4);
}

/// <summary>A piece of furniture: where it stands along the back wall, and what it is.</summary>
public readonly record struct FurnitureDef(int Room, int Slot, double X, Piece Piece);

/// <summary>
/// An embassy: rooms on a grid, the doors between them, the furniture in each, where each spy comes in, and the
/// door out to the airport.
/// </summary>
public sealed class EmbassyDef
{
    public int Index;
    public string Name = "";
    public uint Wall, Floor, Trim;
    public int Cols, Rows;
    public int Rooms => Cols * Rows;
    /// <summary>Doors, by room and side.</summary>
    public bool[,] Door = new bool[0, 4];
    public List<FurnitureDef> Furniture = [];
    public int[] Start = [0, 0];
    /// <summary>The door out: in which room, and on which (outside) wall.</summary>
    public int ExitRoom;
    public Side ExitSide;
    public double Seconds;

    public (int Col, int Row) Cell(int room) => (room % Cols, room / Cols);

    /// <summary>The room through a door, or -1 (a wall, or the edge).</summary>
    public int Through(int room, Side s)
    {
        if (!Door[room, (int)s]) return -1;
        var (c, r) = Cell(room);
        var (dc, dr) = Kinds.Step(s);
        int nc = c + dc, nr = r + dr;
        return nc < 0 || nr < 0 || nc >= Cols || nr >= Rows ? -1 : nr * Cols + nc;
    }

    public bool IsExit(int room, Side s) => room == ExitRoom && s == ExitSide;

    public IEnumerable<FurnitureDef> In(int room) => Furniture.Where(f => f.Room == room);

    /// <summary>How many doors apart every room is from one, going through doors (-1 for none).</summary>
    public int[] Distances(int from, Func<int, Side, bool>? shut = null)
    {
        var d = Enumerable.Repeat(-1, Rooms).ToArray();
        d[from] = 0;
        var q = new Queue<int>([from]);
        while (q.Count > 0)
        {
            int r = q.Dequeue();
            for (int s = 0; s < 4; s++)
            {
                int n = Through(r, (Side)s);
                if (n < 0 || d[n] >= 0 || shut != null && shut(r, (Side)s)) continue;
                d[n] = d[r] + 1;
                q.Enqueue(n);
            }
        }
        return d;
    }
}

/// <summary>The embassies, one after another, each larger than the last.</summary>
public static class EmbassyBook
{
    private static readonly (string Name, int Cols, int Rows, uint Wall, uint Floor, uint Trim, double Seconds)[] Plans =
    [
        ("THE CONSULATE", 3, 2, 0xFF3A6EA5, 0xFF8A6A3A, 0xFFE0D0A0, 180),
        ("THE LEGATION", 3, 3, 0xFF6A8A4A, 0xFF6A5A4A, 0xFFF0E0B0, 210),
        ("THE CHANCERY", 4, 3, 0xFF8A4A5A, 0xFF5A4A3A, 0xFFE8C890, 240),
        ("THE MISSION", 4, 4, 0xFF4A6A8A, 0xFF7A6040, 0xFFD0E0F0, 270),
        ("THE GRAND EMBASSY", 5, 4, 0xFF7A5A9A, 0xFF5A4030, 0xFFF0D080, 300),
    ];

    public static int Count => Plans.Length;

    /// <summary>Slots along the back wall, clear of the door in its middle.</summary>
    public static readonly double[] Slots = [70, 170, 430, 530];

    public static EmbassyDef Build(int index, int seed)
    {
        var p = Plans[index % Plans.Length];
        var d = new Dice(seed);
        var e = new EmbassyDef
        {
            Index = index, Name = p.Name, Cols = p.Cols, Rows = p.Rows, Wall = p.Wall, Floor = p.Floor, Trim = p.Trim,
            Seconds = p.Seconds,
        };
        e.Door = new bool[e.Rooms, 4];
        // A maze through every room (so all can be got to), then a few more doors so there's more than one way round.
        var seen = new bool[e.Rooms];
        var stack = new Stack<int>([0]);
        seen[0] = true;
        while (stack.Count > 0)
        {
            int r = stack.Peek();
            var (c, row) = e.Cell(r);
            var ways = new List<Side>();
            for (int s = 0; s < 4; s++)
            {
                var (dc, dr) = Kinds.Step((Side)s);
                int nc = c + dc, nr = row + dr;
                if (nc >= 0 && nr >= 0 && nc < e.Cols && nr < e.Rows && !seen[nr * e.Cols + nc]) ways.Add((Side)s);
            }
            if (ways.Count == 0) { stack.Pop(); continue; }
            var way = ways[d.Next(ways.Count)];
            Open(e, r, way);
            int n = e.Through(r, way);
            seen[n] = true;
            stack.Push(n);
        }
        for (int r = 0; r < e.Rooms; r++)
            foreach (var s in new[] { Side.East, Side.South })
            {
                var (c, row) = e.Cell(r);
                var (dc, dr) = Kinds.Step(s);
                if (c + dc < e.Cols && row + dr < e.Rows && !e.Door[r, (int)s] && d.Next(100) < 45) Open(e, r, s);
            }
        // Furniture: two to four pieces in each room.
        for (int r = 0; r < e.Rooms; r++)
        {
            var slots = Enumerable.Range(0, Slots.Length).OrderBy(_ => d.Next()).Take(2 + d.Next(3)).OrderBy(s => s);
            foreach (int s in slots) e.Furniture.Add(new FurnitureDef(r, s, Slots[s], (Piece)d.Next(8)));
        }
        // The spies start in opposite corners; the way out is about as far from each.
        e.Start = [0, e.Rooms - 1];
        var a = e.Distances(e.Start[0]);
        var b = e.Distances(e.Start[1]);
        var outs = new List<(int Room, Side Side, int Score)>();
        for (int r = 0; r < e.Rooms; r++)
        {
            var (c, row) = e.Cell(r);
            foreach (var s in new[] { Side.North, Side.East, Side.South, Side.West })
            {
                bool edge = s switch { Side.North => row == 0, Side.South => row == e.Rows - 1, Side.West => c == 0, _ => c == e.Cols - 1 };
                if (edge && r != e.Start[0] && r != e.Start[1]) outs.Add((r, s, Math.Abs(a[r] - b[r]) * 10 - Math.Min(a[r], b[r])));
            }
        }
        int best = outs.Min(o => o.Score);
        var pick = outs.Where(o => o.Score <= best + 1).ToList();
        var exit = pick[d.Next(pick.Count)];
        e.ExitRoom = exit.Room;
        e.ExitSide = exit.Side;
        e.Door[exit.Room, (int)exit.Side] = true;
        return e;
    }

    private static void Open(EmbassyDef e, int r, Side s)
    {
        e.Door[r, (int)s] = true;
        int n = e.Through(r, s);
        e.Door[n, (int)Kinds.Opposite(s)] = true;
    }
}

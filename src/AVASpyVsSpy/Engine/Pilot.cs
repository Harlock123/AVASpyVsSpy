namespace AVASpyVsSpy.Engine;

/// <summary>
/// A computer spy, for the opponent, the demonstration and the tests. It knows the embassy's plan (as the map on
/// the screen shows it) but not what's hidden where: it searches the furniture nearest first, remembering what it
/// has found empty, and forgets a room's furniture when someone dies there (whatever they carried is hidden there
/// again). It keeps clear of its own traps, and of any it saw the other spy set. Now and then it sets a trap of its
/// own. When the other spy is close it fights; when the other spy has what it still needs and there's nothing left
/// to search, it goes after him. With all five things it makes for the way out.
/// </summary>
public sealed class Pilot(int me)
{
    public int Me { get; } = me;
    /// <summary>How sharp it is: 0 (slow to think, slow to swing) to 1.</summary>
    public double Skill = 0.6;
    private readonly HashSet<int> _empty = [];
    private readonly HashSet<int> _trappedFurniture = [];
    private readonly HashSet<(int, Side)> _trappedDoors = [];
    private int _log;
    private bool _pressed;
    private double _think;
    private int _lastRoom = -1;
    private Dice _dice = new(7);
    private bool _doorPlanned;

    public string Doing { get; private set; } = "";

    public void Reset(int seed = 7)
    {
        _empty.Clear();
        _trappedFurniture.Clear();
        _trappedDoors.Clear();
        _log = 0;
        _pressed = false;
        _think = 0;
        _lastRoom = -1;
        _dice = new Dice(seed + Me * 101);
        _doorPlanned = false;
        Doing = "";
    }

    public Controls Decide(World w)
    {
        var s = w.Spies[Me];
        var o = w.Spies[1 - Me];
        Notice(w, s, o);
        if (!s.Active || s.Doing != Engine.Doing.Free) { _pressed = false; return default; }
        // A button pressed last step is let go of first (each press is one action).
        if (_pressed) { _pressed = false; return default; }
        if (_think > 0) { _think -= Field.Step; return default; }
        var e = w.Embassy;
        bool arrived = s.Room != _lastRoom;
        _lastRoom = s.Room;
        if (arrived) _doorPlanned = false;

        // A trap left in what's just been searched (if it's still to hand).
        if (_plantNext >= 0)
        {
            int f = _plantNext;
            var k = s.Traps[(int)TrapKind.Bomb] > 0 ? TrapKind.Bomb : TrapKind.Spring;
            if (Sim.NearFurniture(w, s) == f && w.FurnitureTraps[f] == null && s.Traps[(int)k] > 0 && o.Room != s.Room)
            {
                if (s.Chosen == k) _plantNext = -1;
                return SetTrap(s, k);
            }
            _plantNext = -1;
        }

        // The other spy, close by: fight (unless much the weaker and carrying nothing worth taking off him).
        if (o.Room == s.Room && o.Active && o.Doing != Engine.Doing.Dead)
        {
            bool worth = o.Held > 0 && !s.HasAll || s.Strength >= o.Strength - 1;
            if (worth && Math.Abs(o.X - s.X) < 260)
            {
                Doing = "fighting";
                if (Sim.InReach(s, o))
                {
                    // Not every chance is taken at once.
                    if (_dice.Next() < 0.10 + Skill * 0.25) return Press(new Controls(Act: true));
                    return default;
                }
                int side = Math.Sign(s.X - o.X) is var d && d != 0 ? d : 1;
                return Toward(s, o.X + side * 54, o.Z);
            }
        }

        // All five: away.
        if (s.HasAll)
        {
            Doing = "escaping";
            return Route(w, s, e.ExitRoom, () => DoorControls(s, e.ExitSide));
        }

        // A trap over the door just come through, now and then (only where there's another way round for itself).
        if (arrived && !_doorPlanned && _dice.Next() < 0.25 * Skill && o.Room != s.Room)
        {
            _doorPlanned = true;
            if (Sim.NearDoor(w, s) is { } door && !e.IsExit(s.Room, door) && w.DoorTrap(s.Room, door) == null && Spare(e, s.Room, door))
                foreach (var k in new[] { TrapKind.Pail, TrapKind.Gun })
                    if (s.Traps[(int)k] > 0) return SetTrap(s, k);
        }

        // The nearest furniture not yet known to be empty (or trapped).
        var dist = RoomDistances(w, s);
        int best = -1;
        double bestCost = double.MaxValue;
        for (int i = 0; i < e.Furniture.Count; i++)
        {
            var f = e.Furniture[i];
            if (_empty.Contains(i) || _trappedFurniture.Contains(i) || dist[f.Room] < 0) continue;
            double cost = dist[f.Room] * 700 + (f.Room == s.Room ? Math.Abs(f.X - s.X) : 0);
            if (cost < bestCost) { bestCost = cost; best = i; }
        }
        bool hunt = o.Active && o.Held > 0 && (best < 0 || o.Held >= 3 && dist[o.Room] >= 0 && dist[o.Room] <= 1);
        if (hunt)
        {
            Doing = "hunting";
            return Route(w, s, o.Room, () => Toward(s, o.X, o.Z));
        }
        if (best < 0)
        {
            // Everything searched, and nothing to show for it: search again (things get hidden again when spies die).
            _empty.Clear();
            Doing = "starting over";
            return default;
        }
        var target = e.Furniture[best];
        Doing = "searching";
        return Route(w, s, target.Room, () =>
        {
            if (Math.Abs(s.X - target.X) > 10 || s.Z > 18) return Toward(s, target.X, 12);
            return Press(new Controls(Act: true));
        });
    }

    /// <summary>A trap of a kind: chosen first (a press on the button that picks), then set.</summary>
    private Controls SetTrap(Spy s, TrapKind k)
    {
        Doing = "setting a trap";
        if (s.Chosen != k) return Press(new Controls(Next: true));
        _think = 0.3;
        return Press(new Controls(Trap: true));
    }

    private Controls Press(Controls c)
    {
        _pressed = true;
        return c;
    }

    /// <summary>Takes note of what has happened since it last looked.</summary>
    private void Notice(World w, Spy s, Spy o)
    {
        var e = w.Embassy;
        for (; _log < w.Log.Count; _log++)
        {
            var p = w.Log[_log].Split(' ');
            switch (p[0])
            {
                case "searched" when int.Parse(p[1]) == Me:
                {
                    int f = int.Parse(p[2]);
                    _empty.Add(f);
                    _think = 0.15 + (1 - Skill) * 0.6;
                    // Sometimes a trap is left behind in what's just been searched.
                    if (_dice.Next() < 0.3 * Skill && o.Room != s.Room)
                        _plantNext = f;
                    break;
                }
                case "died":
                {
                    int room = int.Parse(p[2]);
                    foreach (var i in Enumerable.Range(0, e.Furniture.Count).Where(i => e.Furniture[i].Room == room)) _empty.Remove(i);
                    break;
                }
                case "trapped" when int.Parse(p[1]) != Me && o.Room == s.Room && s.Active:
                    // Seen set: kept clear of.
                    if (p[2] == "f") _trappedFurniture.Add(int.Parse(p[3]));
                    else _trappedDoors.Add(w.DoorKey(int.Parse(p[3]), Enum.Parse<Side>(p[4])));
                    break;
            }
        }
        // Its own traps it always knows; ones gone off it forgets.
        for (int i = 0; i < e.Furniture.Count; i++)
            if (w.FurnitureTraps[i] is { } t && t.Owner == Me) _trappedFurniture.Add(i);
            else if (w.FurnitureTraps[i] == null) _trappedFurniture.Remove(i);
        foreach (var (k, t) in w.DoorTraps)
            if (t.Owner == Me) _trappedDoors.Add(k);
        _trappedDoors.RemoveWhere(k => !w.DoorTraps.ContainsKey(k));
    }

    private int _plantNext = -1;

    /// <summary>How many doors to every room, keeping out of doors known to be trapped (unless it has the remedy).</summary>
    private int[] RoomDistances(World w, Spy s)
    {
        var e = w.Embassy;
        var d = e.Distances(s.Room, (r, side) => Shut(w, s, r, side));
        return d;
    }

    private bool Shut(World w, Spy s, int room, Side side)
    {
        var key = w.DoorKey(room, side);
        if (!_trappedDoors.Contains(key)) return false;
        var t = w.DoorTraps.TryGetValue(key, out var tt) ? tt : (SetTrap?)null;
        return t == null || s.Remedy != Kinds.Cure(t.Value.Kind);
    }

    /// <summary>Whether a door can be done without: every room still reached from this one the other way round.</summary>
    private static bool Spare(EmbassyDef e, int room, Side door)
    {
        int other = e.Through(room, door);
        if (other < 0) return false;
        var d = e.Distances(room, (r, s) => r == room && s == door || r == other && s == Kinds.Opposite(door));
        return d.All(x => x >= 0);
    }

    /// <summary>To a room (the next door on the shortest way), then whatever is to be done there.</summary>
    private Controls Route(World w, Spy s, int room, Func<Controls> there)
    {
        if (s.Room == room) return there();
        var e = w.Embassy;
        // Backwards from the room wanted: the door out of this one that's a step nearer.
        var back = e.Distances(room, (r, side) => Shut(w, s, r, side));
        if (back[s.Room] < 0) back = e.Distances(room);
        Side? way = null, risky = null;
        for (int i = 0; i < 4; i++)
        {
            int n = e.Through(s.Room, (Side)i);
            if (n < 0 || back[n] < 0 || back[n] >= back[s.Room]) continue;
            if (!Shut(w, s, s.Room, (Side)i)) { way = (Side)i; break; }
            risky ??= (Side)i;
        }
        way ??= risky;
        if (way == null) return default;
        return DoorControls(s, way.Value);
    }

    /// <summary>To a door, then out through it.</summary>
    private static Controls DoorControls(Spy s, Side side)
    {
        switch (side)
        {
            case Side.North:
                if (Math.Abs(s.X - Field.DoorX) > 8) return Toward(s, Field.DoorX, Math.Max(s.Z, 20));
                return new Controls(0, -1);
            case Side.South:
                if (Math.Abs(s.X - Field.DoorX) > 8) return Toward(s, Field.DoorX, Math.Min(s.Z, Field.Depth - 20));
                return new Controls(0, 1);
            case Side.West:
                if (Math.Abs(s.Z - Field.DoorZ) > 6) return Toward(s, Math.Max(s.X, 40), Field.DoorZ);
                return new Controls(-1, 0);
            default:
                if (Math.Abs(s.Z - Field.DoorZ) > 6) return Toward(s, Math.Min(s.X, Field.RoomW - 40), Field.DoorZ);
                return new Controls(1, 0);
        }
    }

    private static Controls Toward(Spy s, double x, double z)
    {
        int dx = Math.Abs(x - s.X) > 4 ? Math.Sign(x - s.X) : 0;
        int dz = Math.Abs(z - s.Z) > 3 ? Math.Sign(z - s.Z) : 0;
        return new Controls(dx, dz);
    }
}

namespace AVASpyVsSpy.Engine;

/// <summary>
/// An embassy, step by step: two spies walking from room to room, searching furniture, setting traps and springing
/// them, fighting with clubs when they meet, and making for the way out with the five secret things.
/// </summary>
public static class Sim
{
    private const double Dt = Field.Step;
    public const double SearchTime = 0.4, SetTime = 0.5, SwingTime = 0.36, SwingHit = 0.16, StunTime = 0.4, DeadTime = 6;
    /// <summary>How near a piece of furniture he must be to search it or trap it.</summary>
    public const double NearX = 46, NearZ = 40;

    public static void Start(World w, EmbassyDef e, int seed)
    {
        w.Embassy = e;
        w.Dice = new Dice(seed);
        w.Clock = 0;
        w.Winner = -1;
        w.Contents = e.Furniture.Select(_ => new List<Thing>()).ToArray();
        w.FurnitureTraps = new SetTrap?[e.Furniture.Count];
        w.DoorTraps.Clear();
        w.Effects.Clear();
        w.Log.Clear();
        // The five secret things and the four remedies, each hidden in a different piece of furniture, none in the
        // rooms the spies start in.
        var places = Enumerable.Range(0, e.Furniture.Count).Where(i => e.Furniture[i].Room != e.Start[0] && e.Furniture[i].Room != e.Start[1]).ToList();
        if (places.Count < 9) places = Enumerable.Range(0, e.Furniture.Count).ToList();
        Shuffle(ref w.Dice, places);
        int k = 0;
        foreach (Item it in Enum.GetValues<Item>()) w.Contents[places[k++ % places.Count]].Add(Thing.Of(it));
        foreach (var r in new[] { Remedy.Water, Remedy.Cutters, Remedy.Umbrella, Remedy.Scissors }) w.Contents[places[k++ % places.Count]].Add(Thing.Of(r));
        for (int i = 0; i < 2; i++)
        {
            var s = w.Spies[i] = new Spy { Index = i, Clock = e.Seconds };
            Arrive(w, s, e.Start[i], null);
        }
    }

    private static void Shuffle<T>(ref Dice d, List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = d.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    public static void Step(World w, Controls white, Controls black)
    {
        w.Clock += Dt;
        foreach (var fx in w.Effects) fx.T += Dt;
        w.Effects.RemoveAll(fx => fx.T > 1.4);
        for (int i = 0; i < 2; i++)
        {
            var s = w.Spies[i];
            if (s.MessageTime > 0 && (s.MessageTime -= Dt) <= 0) s.Message = null;
            if (s.Doing is Doing.Escaped or Doing.OutOfTime) continue;
            s.Clock -= Dt;
            if (s.Clock <= 0)
            {
                s.Clock = 0;
                if (s.Doing != Doing.Dead) Drop(w, s);
                s.Doing = Doing.OutOfTime;
                s.Say("OUT OF TIME", 99);
                w.Log.Add($"out {i}");
                w.Heard.Add(Sfx.OutOfTime);
                continue;
            }
            Act(w, s, i == 0 ? white : black);
        }
    }

    private static void Act(World w, Spy s, Controls c)
    {
        bool act = c.Act && !s.ActPrev, trap = c.Trap && !s.TrapPrev, next = c.Next && !s.NextPrev;
        s.ActPrev = c.Act;
        s.TrapPrev = c.Trap;
        s.NextPrev = c.Next;
        var other = w.Spies[1 - s.Index];
        switch (s.Doing)
        {
            case Doing.Dead:
                if ((s.Timer -= Dt) <= 0) Respawn(w, s);
                return;
            case Doing.Stunned:
                if ((s.Timer -= Dt) <= 0) s.Doing = Doing.Free;
                return;
            case Doing.Searching:
                if ((s.Timer -= Dt) <= 0) { s.Doing = Doing.Free; Search(w, s, s.Target); }
                return;
            case Doing.Setting:
                if ((s.Timer -= Dt) <= 0) { s.Doing = Doing.Free; Place(w, s); }
                return;
            case Doing.Swinging:
                s.Timer -= Dt;
                if (!s.SwingLanded && s.Timer <= SwingTime - SwingHit)
                {
                    s.SwingLanded = true;
                    if (InReach(s, other)) Hit(w, s, other);
                }
                if (s.Timer <= 0) s.Doing = Doing.Free;
                return;
        }
        if (next) Choose(s, 1);
        if (act)
        {
            int f = NearFurniture(w, s);
            if (InReach(s, other) || f < 0)
            {
                // The club, at the other spy (or the air).
                s.Doing = Doing.Swinging;
                s.Timer = SwingTime;
                s.SwingLanded = false;
                if (other.Room == s.Room && other.Active) s.Facing = Math.Sign(other.X - s.X) is var d && d != 0 ? d : s.Facing;
                w.Heard.Add(Sfx.Swing);
                return;
            }
            s.Doing = Doing.Searching;
            s.Timer = SearchTime;
            s.Target = f;
            w.Heard.Add(Sfx.Search);
            return;
        }
        if (trap)
        {
            if (s.Traps[(int)s.Chosen] == 0) Choose(s, 1);
            var kind = s.Chosen;
            if (s.Traps[(int)kind] == 0) { s.Say("NO TRAPS LEFT"); return; }
            if (Kinds.OnDoor(kind))
            {
                if (NearDoor(w, s) is { } door && w.DoorTrap(s.Room, door) == null && !w.Embassy.IsExit(s.Room, door))
                {
                    s.Doing = Doing.Setting;
                    s.Timer = SetTime;
                    s.Target = -1;
                    s.TargetDoor = door;
                    w.Heard.Add(Sfx.SetTrap);
                }
                else s.Say(kind == TrapKind.Pail ? "THE PAIL GOES OVER A DOOR" : "THE GUN GOES OVER A DOOR");
            }
            else
            {
                int f = NearFurniture(w, s);
                if (f >= 0 && w.FurnitureTraps[f] == null)
                {
                    s.Doing = Doing.Setting;
                    s.Timer = SetTime;
                    s.Target = f;
                    w.Heard.Add(Sfx.SetTrap);
                }
                else s.Say(kind == TrapKind.Bomb ? "THE BOMB GOES IN FURNITURE" : "THE SPRING GOES IN FURNITURE");
            }
            return;
        }
        Walk(w, s, c);
    }

    /// <summary>Picks the next kind of trap he still has.</summary>
    public static void Choose(Spy s, int dir)
    {
        for (int i = 1; i <= 4; i++)
        {
            var k = (TrapKind)(((int)s.Chosen + dir * i + 8) % 4);
            if (s.Traps[(int)k] > 0) { s.Chosen = k; return; }
        }
    }

    public static bool InReach(Spy a, Spy b) =>
        b.Room == a.Room && b.Active && b.Doing != Doing.Dead && Math.Abs(b.X - a.X) < Spy.Reach && Math.Abs(b.Z - a.Z) < Spy.ReachZ;

    public static int NearFurniture(World w, Spy s)
    {
        int best = -1;
        double bd = double.MaxValue;
        var list = w.Embassy.Furniture;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Room != s.Room) continue;
            double d = Math.Abs(list[i].X - s.X);
            if (d < NearX && s.Z < NearZ && d < bd) { bd = d; best = i; }
        }
        return best;
    }

    /// <summary>The door he's standing at, if any.</summary>
    public static Side? NearDoor(World w, Spy s)
    {
        var e = w.Embassy;
        bool Has(Side side) => e.Door[s.Room, (int)side];
        if (s.Z < 34 && Math.Abs(s.X - Field.DoorX) < Field.DoorHalf && Has(Side.North)) return Side.North;
        if (s.Z > Field.Depth - 34 && Math.Abs(s.X - Field.DoorX) < Field.DoorHalf && Has(Side.South)) return Side.South;
        if (s.X < 60 && Math.Abs(s.Z - Field.DoorZ) < 34 && Has(Side.West)) return Side.West;
        if (s.X > Field.RoomW - 60 && Math.Abs(s.Z - Field.DoorZ) < 34 && Has(Side.East)) return Side.East;
        return null;
    }

    private static void Walk(World w, Spy s, Controls c)
    {
        if (c.Dx == 0 && c.Dy == 0) return;
        if (c.Dx != 0) s.Facing = c.Dx;
        s.Walk += Dt;
        double x = s.X + c.Dx * Spy.Speed * Dt, z = s.Z + c.Dy * Spy.SpeedZ * Dt;
        const double minX = 20, maxX = Field.RoomW - 20, minZ = 6, maxZ = Field.Depth - 6;
        // Out through a door: pushing at a wall where there's a door in it.
        Side? through = null;
        if (z < minZ && c.Dy < 0 && Math.Abs(x - Field.DoorX) < Field.DoorHalf) through = Side.North;
        else if (z > maxZ && c.Dy > 0 && Math.Abs(x - Field.DoorX) < Field.DoorHalf) through = Side.South;
        else if (x < minX && c.Dx < 0 && Math.Abs(z - Field.DoorZ) < 30) through = Side.West;
        else if (x > maxX && c.Dx > 0 && Math.Abs(z - Field.DoorZ) < 30) through = Side.East;
        s.X = Math.Clamp(x, minX, maxX);
        s.Z = Math.Clamp(z, minZ, maxZ);
        if (through is { } side && w.Embassy.Door[s.Room, (int)side]) Go(w, s, side);
    }

    private static void Go(World w, Spy s, Side side)
    {
        var e = w.Embassy;
        if (w.DoorTrap(s.Room, side) is { } t && Spring(w, s, t, s.X, s.Z, () => w.DoorTraps.Remove(w.DoorKey(s.Room, side)))) return;
        if (e.IsExit(s.Room, side))
        {
            if (s.HasAll)
            {
                s.Doing = Doing.Escaped;
                w.Winner = s.Index;
                w.Effects.Add(new Effect(s.Room, Flash.Escape, s.X, s.Z));
                w.Heard.Add(Sfx.Escape);
                w.Log.Add($"escaped {s.Index}");
                s.Say("AWAY TO THE AIRPORT!", 99);
            }
            else
            {
                s.Say($"THE WAY OUT - BRING ALL FIVE ({s.Held} OF 5)");
                w.Heard.Add(Sfx.Locked);
                // Back off the door.
                if (side is Side.North or Side.South) s.Z = side == Side.North ? 24 : Field.Depth - 24;
                else s.X = side == Side.West ? 44 : Field.RoomW - 44;
            }
            return;
        }
        int to = e.Through(s.Room, side);
        if (to < 0) return;
        Arrive(w, s, to, Kinds.Opposite(side));
        w.Heard.Add(Sfx.Door);
    }

    /// <summary>Into a room, by a door (just inside it) or (null) in the middle.</summary>
    public static void Arrive(World w, Spy s, int room, Side? by)
    {
        s.Room = room;
        (s.X, s.Z) = by switch
        {
            Side.North => (Field.DoorX, 14.0),
            Side.South => (Field.DoorX, Field.Depth - 14),
            Side.West => (26.0, Field.DoorZ),
            Side.East => (Field.RoomW - 26, Field.DoorZ),
            _ => (Field.DoorX + (s.Index == 0 ? -60 : 60), Field.DoorZ + 30),
        };
        if (by is Side.West) s.Facing = 1;
        if (by is Side.East) s.Facing = -1;
    }

    /// <summary>
    /// A trap goes off on him: with the remedy in hand he's through it and it's gone; without, it's the end of him.
    /// Whether he died.
    /// </summary>
    private static bool Spring(World w, Spy s, SetTrap t, double x, double z, Action remove)
    {
        remove();
        if (s.Remedy == Kinds.Cure(t.Kind))
        {
            s.Say(t.Kind switch
            {
                TrapKind.Bomb => "BOMB! DOUSED WITH WATER",
                TrapKind.Spring => "SPRING! CUT WITH THE CUTTERS",
                TrapKind.Pail => "PAIL! KEPT OFF BY THE UMBRELLA",
                _ => "GUN! THE STRING SNIPPED",
            });
            w.Effects.Add(new Effect(s.Room, Flash.Defused, x, z));
            w.Heard.Add(Sfx.Defuse);
            w.Log.Add($"defused {s.Index} {t.Kind}");
            return false;
        }
        w.Effects.Add(new Effect(s.Room, t.Kind switch { TrapKind.Bomb => Flash.Bomb, TrapKind.Spring => Flash.Spring, TrapKind.Pail => Flash.Pail, _ => Flash.Gun }, x, z));
        w.Heard.Add(t.Kind switch { TrapKind.Bomb => Sfx.Bomb, TrapKind.Spring => Sfx.Spring, TrapKind.Pail => Sfx.Pail, _ => Sfx.Gun });
        if (t.Owner != s.Index) w.Spies[t.Owner].Score += 300;
        Kill(w, s, t.Kind switch { TrapKind.Bomb => "BLOWN UP", TrapKind.Spring => "SPRUNG", TrapKind.Pail => "PAILED", _ => "SHOT" }, t.Owner);
        return true;
    }

    private static void Search(World w, Spy s, int f)
    {
        var def = w.Embassy.Furniture[f];
        if (w.FurnitureTraps[f] is { } t && Spring(w, s, t, def.X, 0, () => w.FurnitureTraps[f] = null)) return;
        var found = new List<string>();
        var list = w.Contents[f];
        var keep = new List<Thing>();
        Remedy put = Remedy.None;
        foreach (var th in list)
        {
            if (th.Item is { } it)
            {
                s.Items[(int)it] = true;
                s.Score += 100;
                found.Add(Name(it));
            }
            else if (th.Remedy != Remedy.None && put == Remedy.None)
            {
                // A spy carries one remedy at a time: the one he had is left in its place.
                put = s.Remedy;
                s.Remedy = th.Remedy;
                found.Add(Name(th.Remedy));
            }
            else keep.Add(th);
        }
        if (put != Remedy.None) keep.Add(Thing.Of(put));
        w.Contents[f] = keep;
        w.Log.Add($"searched {s.Index} {f} {found.Count}");
        if (found.Count == 0) { s.Say("NOTHING HERE", 1.2); w.Heard.Add(Sfx.Nothing); return; }
        s.Say("FOUND " + string.Join(", ", found), 2.2);
        w.Heard.Add(found.Any(n => n.StartsWith("THE ") && !IsRemedyName(n)) ? Sfx.Find : Sfx.Remedy);
        if (s.HasAll) { s.Say("ALL FIVE! NOW FOR THE WAY OUT", 3); w.Heard.Add(Sfx.AllFive); }
    }

    private static bool IsRemedyName(string n) => n is "THE WATER" or "THE CUTTERS" or "THE UMBRELLA" or "THE SCISSORS";

    public static string Name(Item i) => i switch
    {
        Item.Briefcase => "THE BRIEFCASE",
        Item.Plans => "THE PLANS",
        Item.Key => "THE KEY",
        Item.Money => "THE MONEY",
        _ => "THE PASSPORT",
    };

    public static string Name(Remedy r) => r switch
    {
        Remedy.Water => "THE WATER",
        Remedy.Cutters => "THE CUTTERS",
        Remedy.Umbrella => "THE UMBRELLA",
        Remedy.Scissors => "THE SCISSORS",
        _ => "",
    };

    private static void Place(World w, Spy s)
    {
        var kind = s.Chosen;
        if (s.Traps[(int)kind] == 0) return;
        s.Traps[(int)kind]--;
        if (s.Target >= 0)
        {
            w.FurnitureTraps[s.Target] = new SetTrap(kind, s.Index);
            w.Log.Add($"trapped {s.Index} f {s.Target} {kind}");
        }
        else
        {
            w.DoorTraps[w.DoorKey(s.Room, s.TargetDoor)] = new SetTrap(kind, s.Index);
            w.Log.Add($"trapped {s.Index} d {s.Room} {s.TargetDoor} {kind}");
        }
        s.Say(kind switch { TrapKind.Bomb => "BOMB SET", TrapKind.Spring => "SPRING SET", TrapKind.Pail => "PAIL SET OVER THE DOOR", _ => "GUN SET OVER THE DOOR" }, 1.4);
        if (s.Traps[(int)kind] == 0) Choose(s, 1);
    }

    private static void Hit(World w, Spy a, Spy b)
    {
        b.Strength--;
        w.Effects.Add(new Effect(b.Room, Flash.Hit, b.X, b.Z));
        w.Heard.Add(Sfx.Hit);
        if (b.Strength <= 0)
        {
            a.Score += 500;
            Kill(w, b, "CLUBBED", a.Index);
            return;
        }
        b.Doing = Doing.Stunned;
        b.Timer = StunTime;
        b.X = Math.Clamp(b.X + Math.Sign(b.X - a.X == 0 ? a.Facing : b.X - a.X) * 46, 20, Field.RoomW - 20);
    }

    public static void Kill(World w, Spy s, string cause, int by)
    {
        if (!s.Active) return;
        s.Doing = Doing.Dead;
        s.Timer = DeadTime;
        s.Cause = cause;
        s.Deaths++;
        s.Say(cause, DeadTime);
        w.Heard.Add(Sfx.Die);
        w.Log.Add($"died {s.Index} {s.Room}");
        Drop(w, s);
    }

    /// <summary>Whatever he carried is hidden again, in the furniture of the room he fell in.</summary>
    private static void Drop(World w, Spy s)
    {
        var here = Enumerable.Range(0, w.Embassy.Furniture.Count).Where(i => w.Embassy.Furniture[i].Room == s.Room).ToList();
        if (here.Count == 0) here = Enumerable.Range(0, w.Embassy.Furniture.Count).ToList();
        for (int i = 0; i < 5; i++)
            if (s.Items[i])
            {
                s.Items[i] = false;
                w.Contents[here[w.Dice.Next(here.Count)]].Add(Thing.Of((Item)i));
            }
        if (s.Remedy != Remedy.None)
        {
            w.Contents[here[w.Dice.Next(here.Count)]].Add(Thing.Of(s.Remedy));
            s.Remedy = Remedy.None;
        }
    }

    private static void Respawn(World w, Spy s)
    {
        var other = w.Spies[1 - s.Index];
        int room;
        do room = w.Dice.Next(w.Embassy.Rooms);
        while (w.Embassy.Rooms > 1 && room == other.Room);
        s.Doing = Doing.Free;
        s.Strength = Spy.Strength0;
        s.Message = null;
        Arrive(w, s, room, null);
        w.Log.Add($"back {s.Index} {room}");
        w.Heard.Add(Sfx.Back);
    }
}

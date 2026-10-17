namespace AVASpyVsSpy.Engine;

/// <summary>A small random number generator whose state can be copied.</summary>
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
}

/// <summary>Something hidden in a piece of furniture: one of the secret things, or a remedy.</summary>
public readonly record struct Thing(Item? Item, Remedy Remedy = Remedy.None)
{
    public static Thing Of(Item i) => new(i);
    public static Thing Of(Remedy r) => new(null, r);
}

/// <summary>A trap set, and whose it is.</summary>
public readonly record struct SetTrap(TrapKind Kind, int Owner);

/// <summary>What a spy is busy with.</summary>
public enum Doing { Free, Searching, Setting, Swinging, Stunned, Dead, Escaped, OutOfTime }

/// <summary>One of the two spies: where he is, what he carries and what he's doing.</summary>
public sealed class Spy
{
    public const double Speed = 210, SpeedZ = 140, Reach = 74, ReachZ = 26;
    public const int Strength0 = 5;
    public int Index;
    public int Room;
    public double X, Z, Walk;
    public int Facing = 1;
    public bool[] Items = new bool[5];
    public Remedy Remedy;
    /// <summary>Traps still in his trapulator, by kind.</summary>
    public int[] Traps = [3, 3, 2, 2];
    public TrapKind Chosen;
    public int Strength = Strength0;
    public Doing Doing;
    /// <summary>How long the thing he's doing has left (or how long he's been dead).</summary>
    public double Timer;
    /// <summary>The furniture he's searching or trapping, or the door he's trapping.</summary>
    public int Target = -1;
    public Side TargetDoor;
    public double Clock;
    public int Score;
    public bool ActPrev, TrapPrev, NextPrev, SwingLanded;
    public string? Message;
    public double MessageTime;
    /// <summary>How he last died (for the screen).</summary>
    public string Cause = "";
    /// <summary>Times he's been killed, by anything.</summary>
    public int Deaths;

    public bool Active => Doing is not (Doing.Dead or Doing.Escaped or Doing.OutOfTime);
    public bool HasAll => Items.All(i => i);
    public int Held => Items.Count(i => i);

    public void Say(string text, double seconds = 2)
    {
        Message = text;
        MessageTime = seconds;
    }
}

/// <summary>Something to be seen happening in a room for a moment: a bang, a splash, a puff.</summary>
public enum Flash { Bomb, Spring, Pail, Gun, Defused, Hit, Puff, Escape }

public sealed record Effect(int Room, Flash Kind, double X, double Z)
{
    public double T;
}

/// <summary>Everything in an embassy that moves or changes.</summary>
public sealed class World
{
    public EmbassyDef Embassy = null!;
    public Spy[] Spies = [new() { Index = 0 }, new() { Index = 1 }];
    /// <summary>What's hidden in each piece of furniture (by its place in the embassy's list), and any trap in it.</summary>
    public List<Thing>[] Contents = [];
    public SetTrap?[] FurnitureTraps = [];
    /// <summary>Traps over doors, by the room and wall of one side of it (the lower-numbered room's side).</summary>
    public Dictionary<(int Room, Side Side), SetTrap> DoorTraps = [];
    public List<Effect> Effects = [];
    public List<Sfx> Heard = [];
    /// <summary>Things that happened, for the computer spies to take note of (and the tests).</summary>
    public List<string> Log = [];
    public Dice Dice = new(1);
    public double Clock;
    /// <summary>The spy who got away (or -1).</summary>
    public int Winner = -1;

    public (int Room, Side Side) DoorKey(int room, Side s)
    {
        int other = Embassy.Through(room, s);
        return other < 0 || room < other ? (room, s) : (other, Kinds.Opposite(s));
    }

    public SetTrap? DoorTrap(int room, Side s) => DoorTraps.TryGetValue(DoorKey(room, s), out var t) ? t : null;
}

using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVASpyVsSpy.Engine;

namespace AVASpyVsSpy.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVASpyVsSpy/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Cyan = 0xFF6AE8FF, Orange = 0xFFFF9A30;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);


    private bool InGame => Engine.State is GameState.Attract or GameState.Playing or GameState.Paused;

    private const double ViewH = 360, BandH = 48, RoomRight = 690;
    private const uint Ink = 0xFF101018, Paper = 0xFFF4F4EC;

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF000000), new Rect(0, 0, Field.Width, Field.Height));
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            case GameState.GameOver: DrawGameOver(dc); break;
            default:
                DrawView(dc, g.World, 0, 0);
                DrawView(dc, g.World, 1, ViewH + BandH);
                DrawBand(dc, ViewH);
                if (g.Phase == Phase.Start && g.Message is { } name)
                {
                    Banner(dc, name, "FIND THE BRIEFCASE, THE PLANS, THE KEY, THE MONEY AND THE PASSPORT, THEN GET OUT");
                }
                else if (g.Phase == Phase.Over && g.Message is { } result)
                {
                    Banner(dc, result, g.Won ? $"TIME LEFT {(int)g.World.Spies[0].Clock} SECONDS" : g.Lives > 1 || g.State == GameState.Attract ? "TRY AGAIN" : "");
                }
                break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(512, 300), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(512, 360), 26, White, center: true);
        }
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(512, ViewH + BandH / 2 + 14), 20, Yellow, center: true);
    }

    private void Banner(DrawingContext dc, string big, string small)
    {
        dc.FillRectangle(Brush(0xD0101018), new Rect(112, 300, 800, 168), 8);
        dc.DrawRectangle(null, Pen(Yellow, 3), new Rect(112, 300, 800, 168), 8, 8);
        Text(dc, big, new Point(512, 360), 56, Yellow, center: true);
        if (small != "") Text(dc, small, new Point(512, 425), 22, White, center: true);
    }

    // ───────────────────────────── the band between the two views ─────────────────────────────

    private void DrawBand(DrawingContext dc, double y)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF20202C), new Rect(0, y, Field.Width, BandH));
        dc.FillRectangle(Brush(0xFFB02020), new Rect(0, y, Field.Width, 3));
        dc.FillRectangle(Brush(0xFFB02020), new Rect(0, y + BandH - 3, Field.Width, 3));
        Text(dc, $"SCORE {g.Score:000000}", new Point(16, y + 16), 28, White);
        Text(dc, $"{g.World.Embassy.Name}  ·  AGENTS {Math.Max(0, g.Lives)}", new Point(512, y + 16), 26, Yellow, center: true);
        Text(dc, $"HIGH {Math.Max(g.HighScores.TopScore, g.Score):000000}", new Point(1008, y + 16), 28, Dim, right: true);
    }

    // ───────────────────────────── a spy's view ─────────────────────────────

    /// <summary>Where a point on a room's floor is drawn in a view, and how big things there are.</summary>
    private static (double X, double Y, double S) Floor(double x, double z, double oy)
    {
        double t = z / Field.Depth;
        double left = 110 - t * 100, right = 580 + t * 100;
        return (left + x / Field.RoomW * (right - left), oy + 150 + t * 200, 0.72 + 0.5 * t);
    }

    private void DrawView(DrawingContext dc, World w, int who, double oy)
    {
        var s = w.Spies[who];
        var e = w.Embassy;
        using (dc.PushClip(new Rect(0, oy, Field.Width, ViewH)))
        {
            DrawRoom(dc, w, s.Room, oy);
            // The spies in this room, back ones first.
            foreach (var spy in w.Spies.Where(p => p.Room == s.Room && p.Doing != Doing.Escaped).OrderBy(p => p.Z))
                DrawSpy(dc, spy, oy);
            foreach (var fx in w.Effects.Where(f => f.Room == s.Room)) DrawEffect(dc, fx, oy);
            if (s.Doing == Doing.OutOfTime)
            {
                dc.FillRectangle(Brush(0xA0000000), new Rect(0, oy, RoomRight, ViewH));
                Text(dc, "OUT OF TIME", new Point(RoomRight / 2, oy + 170), 60, Red, center: true);
            }
            else if (s.Doing == Doing.Escaped)
            {
                dc.FillRectangle(Brush(0xA0000000), new Rect(0, oy, RoomRight, ViewH));
                Text(dc, "AWAY TO THE AIRPORT!", new Point(RoomRight / 2, oy + 170), 52, Yellow, center: true);
            }
            if (s.Message is { } msg && s.Doing is not (Doing.Escaped or Doing.OutOfTime))
            {
                var ft = Measure(msg, 24);
                double wdt = ft + 28;
                dc.FillRectangle(Brush(0xE0000000), new Rect(RoomRight / 2 - wdt / 2, oy + 6, wdt, 32), 6);
                Text(dc, msg, new Point(RoomRight / 2, oy + 22), 24, s.Doing == Doing.Dead ? Red : Yellow, center: true);
            }
            DrawPanel(dc, w, who, oy);
        }
    }

    private static double Measure(string text, double size) =>
        new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(White)).Width;

    private void DrawRoom(DrawingContext dc, World w, int room, double oy)
    {
        var e = w.Embassy;
        uint wall = e.Wall, side = Shade(e.Wall, 0.78), floor = e.Floor, trim = e.Trim;
        // Ceiling, side walls, back wall, floor.
        Poly(dc, 0xFF14141C, null, 0, new Point(0, oy), new Point(RoomRight, oy), new Point(580, oy + 24), new Point(110, oy + 24));
        Poly(dc, side, null, 0, new Point(0, oy), new Point(110, oy + 24), new Point(110, oy + 150), new Point(10, oy + 350), new Point(0, oy + 360));
        Poly(dc, side, null, 0, new Point(RoomRight, oy), new Point(580, oy + 24), new Point(580, oy + 150), new Point(680, oy + 350), new Point(RoomRight, oy + 360));
        dc.FillRectangle(Brush(wall), new Rect(110, oy + 24, 470, 126));
        // Wallpaper stripes.
        for (double x = 122; x < 580; x += 24) dc.FillRectangle(Brush(Shade(wall, 0.9)), new Rect(x, oy + 24, 8, 126));
        Poly(dc, floor, null, 0, new Point(110, oy + 150), new Point(580, oy + 150), new Point(680, oy + 350), new Point(10, oy + 350));
        // Floorboards, running back to front.
        for (int i = 1; i < 12; i++)
        {
            double fx = i / 12.0 * Field.RoomW;
            var a = Floor(fx, 0, oy);
            var b = Floor(fx, Field.Depth, oy);
            dc.DrawLine(Pen(Shade(floor, 0.82), 1.5), new Point(a.X, a.Y), new Point(b.X, b.Y));
        }
        dc.FillRectangle(Brush(0xFF000000), new Rect(0, oy + 350, RoomRight, 10));
        // Skirting along the back.
        dc.FillRectangle(Brush(trim), new Rect(110, oy + 142, 470, 8));
        Poly(dc, Shade(trim, 0.8), null, 0, new Point(110, oy + 142), new Point(110, oy + 150), new Point(10, oy + 350), new Point(10, oy + 338));
        Poly(dc, Shade(trim, 0.8), null, 0, new Point(580, oy + 142), new Point(580, oy + 150), new Point(680, oy + 350), new Point(680, oy + 338));
        // A picture, and the room's number on a brass plate.
        var h = Hash(room, e.Index, 3);
        double px = (h & 1) == 0 ? 245 : 410;
        dc.FillRectangle(Brush(0xFF6A4A20), new Rect(px, oy + 40, 46, 34));
        dc.FillRectangle(Brush((h >> 4 & 1) == 0 ? 0xFF6AA0D0 : 0xFFD0A060), new Rect(px + 5, oy + 45, 36, 24));
        dc.FillEllipse(px + 23, oy + 60, 6, (h >> 6 & 1) == 0 ? 0xFFF0E070 : 0xFF407040);
        // Doors.
        for (int sd = 0; sd < 4; sd++)
            if (e.Door[room, sd]) DrawDoor(dc, w, room, (Side)sd, oy);
        // Furniture.
        for (int i = 0; i < e.Furniture.Count; i++)
            if (e.Furniture[i].Room == room) DrawPiece(dc, w, i, oy);
    }

    private static uint Shade(uint c, double k)
    {
        uint r = (uint)Math.Min(255, ((c >> 16) & 0xFF) * k), gg = (uint)Math.Min(255, ((c >> 8) & 0xFF) * k), b = (uint)Math.Min(255, (c & 0xFF) * k);
        return 0xFF000000 | r << 16 | gg << 8 | b;
    }

    private void DrawDoor(DrawingContext dc, World w, int room, Side side, double oy)
    {
        var e = w.Embassy;
        bool exit = e.IsExit(room, side);
        uint frame = exit ? 0xFF40C040 : e.Trim, leaf = exit ? 0xFF207020 : 0xFF5A3A1A;
        switch (side)
        {
            case Side.North:
            {
                var a = Floor(Field.DoorX - Field.DoorHalf, 0, oy);
                var b = Floor(Field.DoorX + Field.DoorHalf, 0, oy);
                dc.FillRectangle(Brush(frame), new Rect(a.X - 5, oy + 52, b.X - a.X + 10, a.Y - oy - 52));
                dc.FillRectangle(Brush(0xFF080808), new Rect(a.X, oy + 58, b.X - a.X, a.Y - oy - 58));
                if (exit) ExitSign(dc, (a.X + b.X) / 2, oy + 40);
                break;
            }
            case Side.South:
            {
                var a = Floor(Field.DoorX - Field.DoorHalf, Field.Depth, oy);
                var b = Floor(Field.DoorX + Field.DoorHalf, Field.Depth, oy);
                dc.FillRectangle(Brush(frame), new Rect(a.X - 6, a.Y - 6, b.X - a.X + 12, 12));
                dc.FillRectangle(Brush(0xFF080808), new Rect(a.X, a.Y - 3, b.X - a.X, 10));
                Poly(dc, exit ? 0xFF40C040 : Alpha(e.Trim, 0.7), null, 0, new Point((a.X + b.X) / 2 - 12, a.Y - 22), new Point((a.X + b.X) / 2 + 12, a.Y - 22), new Point((a.X + b.X) / 2, a.Y - 10));
                if (exit) ExitSign(dc, (a.X + b.X) / 2, a.Y - 40);
                break;
            }
            default:
            {
                bool west = side == Side.West;
                double x = west ? 0 : Field.RoomW;
                var a = Floor(x, Field.DoorZ - 34, oy);
                var b = Floor(x, Field.DoorZ + 34, oy);
                double ha = 112 * a.S, hb = 112 * b.S;
                Poly(dc, frame, null, 0, new Point(a.X, a.Y + 2), new Point(b.X, b.Y + 2), new Point(b.X, b.Y - hb - 6), new Point(a.X, a.Y - ha - 6));
                double ia = west ? 3 : -3;
                Poly(dc, 0xFF080808, null, 0, new Point(a.X + ia, a.Y), new Point(b.X + ia, b.Y), new Point(b.X + ia, b.Y - hb), new Point(a.X + ia, a.Y - ha));
                if (exit) ExitSign(dc, (a.X + b.X) / 2, Math.Min(a.Y - ha, b.Y - hb) - 14);
                break;
            }
        }
    }

    private static void ExitSign(DrawingContext dc, double x, double y)
    {
        dc.FillRectangle(Brush(0xFF108010), new Rect(x - 40, y - 11, 80, 22), 3);
        Text(dc, "AIRPORT", new Point(x, y), 18, 0xFFE0FFE0, center: true, shadow: false);
    }

    private void DrawPiece(DrawingContext dc, World w, int i, double oy)
    {
        var f = w.Embassy.Furniture[i];
        var (x, y, _) = Floor(f.X, 0, oy);
        bool open = w.Spies.Any(s => s.Doing == Doing.Searching && s.Target == i);
        const uint wood = 0xFF8A5A2A, dark = 0xFF5A3A18, metal = 0xFF8A949E, metalDark = 0xFF5A646E;
        switch (f.Piece)
        {
            case Piece.Desk:
                dc.FillRectangle(Brush(wood), new Rect(x - 40, y - 52, 80, 10));
                dc.FillRectangle(Brush(dark), new Rect(x - 38, y - 42, 24, 42));
                dc.FillRectangle(Brush(dark), new Rect(x + 14, y - 42, 24, 42));
                for (int k = 0; k < 3; k++) Knob(dc, x - 26, y - 34 + k * 13);
                if (open) dc.FillRectangle(Brush(wood), new Rect(x - 42, y - 30, 30, 9));
                dc.FillRectangle(Brush(0xFF2A6A2A), new Rect(x + 8, y - 64, 18, 12));
                break;
            case Piece.Cabinet:
                dc.FillRectangle(Brush(metal), new Rect(x - 22, y - 96, 44, 96));
                for (int k = 0; k < 4; k++)
                {
                    dc.DrawRectangle(null, Pen(metalDark, 2), new Rect(x - 18, y - 92 + k * 23, 36, 19));
                    dc.FillRectangle(Brush(metalDark), new Rect(x - 6, y - 84 + k * 23, 12, 3));
                }
                if (open) dc.FillRectangle(Brush(metal), new Rect(x - 24, y - 70, 48, 21));
                break;
            case Piece.Bookcase:
                dc.FillRectangle(Brush(dark), new Rect(x - 32, y - 110, 64, 110));
                for (int k = 0; k < 4; k++)
                {
                    dc.FillRectangle(Brush(wood), new Rect(x - 30, y - 108 + k * 27, 60, 3));
                    for (int b = 0; b < 7; b++)
                    {
                        var hb = Hash(i, k * 7 + b, 9);
                        uint col = (hb % 5) switch { 0 => 0xFFB03030u, 1 => 0xFF3050B0u, 2 => 0xFF30A050u, 3 => 0xFFD0B040u, _ => 0xFF8040A0u };
                        dc.FillRectangle(Brush(col), new Rect(x - 28 + b * 8, y - 102 + k * 27 + (hb >> 8) % 5, 7, 21 - (hb >> 8) % 5));
                    }
                }
                if (open) dc.FillRectangle(Brush(0xFFB03030), new Rect(x - 8, y - 64, 9, 18));
                break;
            case Piece.Dresser:
                dc.FillRectangle(Brush(wood), new Rect(x - 34, y - 54, 68, 54));
                for (int k = 0; k < 3; k++)
                {
                    dc.DrawRectangle(null, Pen(dark, 2), new Rect(x - 30, y - 50 + k * 17, 60, 14));
                    Knob(dc, x, y - 43 + k * 17);
                }
                dc.FillRectangle(Brush(dark), new Rect(x - 20, y - 96, 40, 42));
                dc.FillRectangle(Brush(0xFFB8D8E8), new Rect(x - 16, y - 92, 32, 34));
                if (open) dc.FillRectangle(Brush(wood), new Rect(x - 36, y - 36, 72, 10));
                break;
            case Piece.Safe:
                dc.FillRectangle(Brush(0xFF3A3A44), new Rect(x - 28, y - 58, 56, 58), 3);
                dc.DrawRectangle(null, Pen(0xFF6A6A78, 2), new Rect(x - 23, y - 53, 46, 48));
                dc.FillEllipse(x, y - 29, 10, 0xFFB0B0B8);
                dc.FillEllipse(x, y - 29, 4, 0xFF3A3A44);
                if (open) Poly(dc, 0xFF4A4A54, 0xFF6A6A78, 2, new Point(x - 28, y - 58), new Point(x - 50, y - 64), new Point(x - 50, y + 4), new Point(x - 28, y));
                break;
            case Piece.Locker:
                dc.FillRectangle(Brush(0xFF4A7A9A), new Rect(x - 20, y - 112, 40, 112));
                for (int k = 0; k < 5; k++) dc.FillRectangle(Brush(0xFF2A4A6A), new Rect(x - 12, y - 104 + k * 6, 24, 3));
                dc.FillRectangle(Brush(0xFFD0D0D0), new Rect(x + 10, y - 60, 4, 12));
                if (open) Poly(dc, 0xFF5A8AAA, null, 0, new Point(x - 20, y - 112), new Point(x - 38, y - 118), new Point(x - 38, y + 4), new Point(x - 20, y));
                break;
            case Piece.Bureau:
                dc.FillRectangle(Brush(wood), new Rect(x - 36, y - 46, 72, 46));
                Poly(dc, dark, null, 0, new Point(x - 36, y - 46), new Point(x + 36, y - 46), new Point(x + 30, y - 80), new Point(x - 30, y - 80));
                for (int k = 0; k < 2; k++) { dc.DrawRectangle(null, Pen(dark, 2), new Rect(x - 32, y - 40 + k * 19, 64, 16)); Knob(dc, x, y - 32 + k * 19); }
                if (open) Poly(dc, 0xFFE8E0C8, null, 0, new Point(x - 30, y - 80), new Point(x + 30, y - 80), new Point(x + 26, y - 92), new Point(x - 26, y - 92));
                break;
            default:
                // A grandfather clock.
                dc.FillRectangle(Brush(dark), new Rect(x - 16, y - 118, 32, 118));
                dc.FillEllipse(x, y - 98, 13, 0xFFF0E8D0);
                double a = Math.Sin(_clock * 3 + i) * 0.4;
                dc.DrawLine(Pen(Ink, 2), new Point(x, y - 98), new Point(x + Math.Sin(_clock * 0.5) * 9, y - 98 - Math.Cos(_clock * 0.5) * 9));
                dc.DrawLine(Pen(0xFFD0B040, 2), new Point(x, y - 76), new Point(x + Math.Sin(a) * 26, y - 76 + Math.Cos(a) * 26));
                dc.FillEllipse(x + Math.Sin(a) * 26, y - 76 + Math.Cos(a) * 26, 5, 0xFFD0B040);
                if (open) Poly(dc, 0xFF6A4A28, null, 0, new Point(x - 16, y - 82), new Point(x - 30, y - 86), new Point(x - 30, y - 6), new Point(x - 16, y - 4));
                break;
        }
    }

    private static void Knob(DrawingContext dc, double x, double y) => dc.FillEllipse(x, y, 2.5, 0xFFE0C060);

    /// <summary>A spy in his trench coat and hat: white or black.</summary>
    private void DrawSpy(DrawingContext dc, Spy s, double oy)
    {
        var (x, y, k) = Floor(s.X, s.Z, oy);
        bool white = s.Index == 0;
        uint coat = white ? 0xFFF4F4EC : 0xFF22222A, edge = white ? 0xFF9A9AA0 : 0xFF6A6A78, belt = white ? 0xFFC8C0A8 : 0xFF3A3A48;
        if (s.Doing == Doing.Dead)
        {
            // His ghost, rising.
            double up = (Sim.DeadTime - s.Timer) * 26;
            using (dc.PushOpacity(Math.Clamp(s.Timer / 2.5, 0, 0.8)))
            {
                double gy = y - up - 40 * k;
                Poly(dc, white ? 0xFFF8F8FF : 0xFF50505C, null, 0, new Point(x - 16 * k, gy + 30 * k), new Point(x - 12 * k, gy - 20 * k), new Point(x + 12 * k, gy - 20 * k), new Point(x + 16 * k, gy + 30 * k));
                dc.FillEllipse(x, gy - 28 * k, 11 * k, white ? 0xFFF8F8FF : 0xFF50505C);
                dc.DrawEllipse(null, Pen(0xFFFFE060, 2), new Point(x, gy - 46 * k), 10 * k, 3 * k);
                Poly(dc, 0xC0FFFFFF, null, 0, new Point(x - 12 * k, gy - 12 * k), new Point(x - 34 * k, gy - 26 * k), new Point(x - 26 * k, gy + 4 * k));
                Poly(dc, 0xC0FFFFFF, null, 0, new Point(x + 12 * k, gy - 12 * k), new Point(x + 34 * k, gy - 26 * k), new Point(x + 26 * k, gy + 4 * k));
            }
            return;
        }
        int f = s.Facing;
        // Shadow.
        dc.DrawEllipse(Brush(0x50000000), null, new Point(x, y), 22 * k, 6 * k);
        double step = s.Doing == Doing.Free ? Math.Sin(s.Walk * 14) * 6 * k : 0;
        bool crouch = s.Doing is Doing.Setting;
        double lift = crouch ? 14 * k : 0;
        // Legs.
        dc.DrawLine(Pen(Ink, 5 * k), new Point(x - 6 * k, y - 26 * k + lift), new Point(x - 6 * k + step, y - 2));
        dc.DrawLine(Pen(Ink, 5 * k), new Point(x + 6 * k, y - 26 * k + lift), new Point(x + 6 * k - step, y - 2));
        dc.FillRectangle(Brush(Ink), new Rect(x - 10 * k + step + (f > 0 ? 2 : -4) * k, y - 4 * k, 10 * k, 4 * k));
        dc.FillRectangle(Brush(Ink), new Rect(x + 2 * k - step + (f > 0 ? 2 : -4) * k, y - 4 * k, 10 * k, 4 * k));
        // The coat.
        double top = y - 78 * k + lift;
        Poly(dc, coat, edge, 1.5, new Point(x - 13 * k, top), new Point(x + 13 * k, top), new Point(x + 19 * k, y - 22 * k + lift), new Point(x - 19 * k, y - 22 * k + lift));
        dc.FillRectangle(Brush(belt), new Rect(x - 16 * k, top + 30 * k, 32 * k, 5 * k));
        Poly(dc, edge, null, 0, new Point(x, top), new Point(x + f * 8 * k, top + 14 * k), new Point(x, top + 30 * k));
        // Arm, and the club.
        var shoulder = new Point(x + f * 8 * k, top + 8 * k);
        double angle = s.Doing switch
        {
            Doing.Swinging => -2.4 + (1 - s.Timer / Sim.SwingTime) * 3.2,
            Doing.Searching => -0.2,
            Doing.Setting => 0.4,
            _ => 1.1,
        };
        var hand = new Point(shoulder.X + f * Math.Cos(angle) * 24 * k, shoulder.Y + Math.Sin(angle) * 24 * k);
        dc.DrawLine(Pen(coat == 0xFFF4F4EC ? 0xFFDCDCD4 : 0xFF34343E, 7 * k), shoulder, hand);
        dc.DrawLine(Pen(edge, 1), shoulder, hand);
        if (s.Doing == Doing.Swinging)
        {
            var end = new Point(hand.X + f * Math.Cos(angle - 0.5) * 30 * k, hand.Y + Math.Sin(angle - 0.5) * 30 * k);
            dc.DrawLine(Pen(0xFF7A4A1A, 6 * k), hand, end);
            dc.FillEllipse(end.X, end.Y, 5 * k, 0xFF7A4A1A);
        }
        dc.FillEllipse(hand.X, hand.Y, 4 * k, 0xFFE8C8A0);
        // Head: a long nose under the hat brim, dark glasses.
        double hy = top - 12 * k;
        dc.FillEllipse(x, hy, 11 * k, 0xFFE8C8A0);
        Poly(dc, 0xFFE8C8A0, 0xFFB89870, 1, new Point(x + f * 6 * k, hy - 3 * k), new Point(x + f * 22 * k, hy + 2 * k), new Point(x + f * 7 * k, hy + 6 * k));
        dc.FillRectangle(Brush(Ink), new Rect(x + (f > 0 ? 0 : -10) * k, hy - 5 * k, 10 * k, 5 * k));
        // The hat.
        uint hat = white ? 0xFFF8F8F0 : 0xFF18181E;
        dc.FillRectangle(Brush(hat), new Rect(x - 18 * k, hy - 11 * k, 36 * k, 4 * k));
        dc.DrawRectangle(null, Pen(edge, 1), new Rect(x - 18 * k, hy - 11 * k, 36 * k, 4 * k));
        Poly(dc, hat, edge, 1, new Point(x - 11 * k, hy - 11 * k), new Point(x + 11 * k, hy - 11 * k), new Point(x + 9 * k, hy - 24 * k), new Point(x - 9 * k, hy - 24 * k));
        dc.FillRectangle(Brush(white ? 0xFF303038 : 0xFFC03030), new Rect(x - 11 * k, hy - 15 * k, 22 * k, 3 * k));
        if (s.Doing == Doing.Stunned)
            for (int i = 0; i < 3; i++)
            {
                double a = _clock * 6 + i * 2.1;
                Star(dc, x + Math.Cos(a) * 18 * k, hy - 28 * k + Math.Sin(a) * 5 * k, 5 * k, Yellow);
            }
    }

    private static void Star(DrawingContext dc, double x, double y, double r, uint c)
    {
        var pts = new Point[10];
        for (int i = 0; i < 10; i++)
        {
            double a = -Math.PI / 2 + i * Math.PI / 5, rr = i % 2 == 0 ? r : r * 0.45;
            pts[i] = new Point(x + Math.Cos(a) * rr, y + Math.Sin(a) * rr);
        }
        Poly(dc, c, null, 0, pts);
    }

    private void DrawEffect(DrawingContext dc, AVASpyVsSpy.Engine.Effect fx, double oy)
    {
        var (x, y, k) = Floor(fx.X, fx.Z, oy);
        double t = fx.T;
        switch (fx.Kind)
        {
            case Flash.Bomb:
            {
                double r = Math.Min(1, t * 4) * 90 * k;
                using (dc.PushOpacity(Math.Clamp(1.3 - t, 0, 1)))
                {
                    dc.FillEllipse(x, y - 50 * k, r, 0xFFFF8020);
                    dc.FillEllipse(x, y - 50 * k, r * 0.65, 0xFFFFE040);
                    dc.FillEllipse(x, y - 50 * k, r * 0.3, 0xFFFFFFFF);
                }
                if (t < 0.8) Text(dc, "KABOOM!", new Point(x, y - 150 * k), 44, Red, center: true);
                break;
            }
            case Flash.Spring:
            {
                double h = Math.Min(1, t * 5) * 110 * k;
                var pts = new List<Point>();
                for (int i = 0; i <= 12; i++) pts.Add(new Point(x + (i % 2 == 0 ? -14 : 14) * k, y - i * h / 12));
                for (int i = 0; i < pts.Count - 1; i++) dc.DrawLine(Pen(0xFFC0C0C8, 4), pts[i], pts[i + 1]);
                if (t < 0.9) Text(dc, "BOING!", new Point(x, y - h - 30), 40, Cyan, center: true);
                break;
            }
            case Flash.Pail:
            {
                double drop = Math.Min(1, t * 3);
                double py = y - 150 * k + drop * 70 * k;
                Poly(dc, 0xFF8A949E, 0xFF5A646E, 2, new Point(x - 18 * k, py), new Point(x + 18 * k, py), new Point(x + 13 * k, py + 26 * k), new Point(x - 13 * k, py + 26 * k));
                for (int i = 0; i < 9; i++)
                {
                    double a = i / 8.0 * Math.PI;
                    dc.FillEllipse(x + Math.Cos(a) * t * 90 * k, py + 30 * k + Math.Sin(a) * t * 50 * k + t * t * 60, 4 * k, 0xFF60A0FF);
                }
                if (t < 0.9) Text(dc, "SPLOSH!", new Point(x, py - 30), 40, Cyan, center: true);
                break;
            }
            case Flash.Gun:
                if (t < 0.25) Star(dc, x, y - 70 * k, 40 * k, 0xFFFFE040);
                if (t < 0.9) Text(dc, "BANG!", new Point(x, y - 140 * k), 48, Red, center: true);
                break;
            case Flash.Defused:
                using (dc.PushOpacity(Math.Clamp(1.2 - t, 0, 1)))
                    for (int i = 0; i < 5; i++) dc.FillEllipse(x - 30 + i * 15, y - 90 * k - t * 50 - (i % 2) * 12, 14 + t * 10, 0xFFD8D8E0);
                if (t < 1) Text(dc, "PHEW!", new Point(x, y - 150 * k), 40, White, center: true);
                break;
            case Flash.Hit:
                if (t < 0.5)
                {
                    Star(dc, x, y - 80 * k, (24 + t * 30) * k, 0xFFFFE040);
                    Text(dc, "WHACK!", new Point(x, y - 120 * k), 34, Orange, center: true);
                }
                break;
            case Flash.Escape:
                Text(dc, "BON VOYAGE!", new Point(x, y - 120 * k - t * 20), 40, Yellow, center: true);
                break;
        }
    }

    // ───────────────────────────── the panel: clock, strength, map, trapulator, what he carries ─────────────────────────────

    private void DrawPanel(DrawingContext dc, World w, int who, double oy)
    {
        var s = w.Spies[who];
        var e = w.Embassy;
        bool white = who == 0;
        double x0 = RoomRight, wdt = Field.Width - RoomRight;
        dc.FillRectangle(Brush(white ? 0xFFE8E4D8 : 0xFF24242C), new Rect(x0, oy, wdt, ViewH));
        uint ink = white ? Ink : 0xFFE8E8F0, faint = white ? 0xFF8A8478 : 0xFF6A6A7A;
        bool computer = !white || Engine.State == GameState.Attract;
        Text(dc, white ? "WHITE SPY" : "BLACK SPY", new Point(x0 + 14, oy + 20), 30, ink, shadow: false);
        Text(dc, computer ? "COMPUTER" : "YOU", new Point(x0 + 14, oy + 44), 18, faint, shadow: false);
        // The clock.
        int secs = (int)Math.Ceiling(s.Clock);
        bool low = s.Clock < 20 && s.Active;
        Text(dc, $"{secs / 60}:{secs % 60:00}", new Point(x0 + wdt - 14, oy + 28), 44, low && (int)(_clock * 4) % 2 == 0 ? Red : ink, right: true, shadow: false);
        // Strength.
        for (int i = 0; i < Spy.Strength0; i++)
            dc.FillRectangle(Brush(i < s.Strength && s.Doing != Doing.Dead ? 0xFFD03030 : faint), new Rect(x0 + 180 + i * 14, oy + 40, 10, 10), 2);
        // The map: his own room lit, the way out marked.
        double mapTop = oy + 64, mapH = 128, mapW = wdt - 28;
        double cell = Math.Min(mapW / e.Cols, mapH / e.Rows);
        double mx = x0 + 14 + (mapW - cell * e.Cols) / 2, my = mapTop + (mapH - cell * e.Rows) / 2;
        for (int r = 0; r < e.Rooms; r++)
        {
            var (c, row) = e.Cell(r);
            var rect = new Rect(mx + c * cell + 2, my + row * cell + 2, cell - 4, cell - 4);
            bool here = r == s.Room;
            dc.FillRectangle(Brush(here ? (white ? 0xFF3060C0 : 0xFFC03030) : white ? 0xFFC8C2B0 : 0xFF3A3A48), rect);
            uint doorC = white ? 0xFFE8E4D8 : 0xFF24242C;
            if (e.Door[r, (int)Side.East] && !e.IsExit(r, Side.East)) dc.FillRectangle(Brush(doorC), new Rect(rect.Right - 1, rect.Y + cell / 2 - 6, 6, 8));
            if (e.Door[r, (int)Side.South] && !e.IsExit(r, Side.South)) dc.FillRectangle(Brush(doorC), new Rect(rect.X + cell / 2 - 6, rect.Bottom - 1, 8, 6));
        }
        {
            var (c, row) = e.Cell(e.ExitRoom);
            double ex = mx + c * cell + cell / 2, ey = my + row * cell + cell / 2;
            var (dx, dy) = Kinds.Step(e.ExitSide);
            Star(dc, ex + dx * cell * 0.56, ey + dy * cell * 0.56, 8, 0xFF20C020);
        }
        // The trapulator.
        double ty = oy + 214;
        Text(dc, "TRAPS", new Point(x0 + 14, ty - 12), 18, faint, shadow: false);
        for (int i = 0; i < 4; i++)
        {
            double bx = x0 + 14 + i * 78;
            bool chosen = (int)s.Chosen == i;
            dc.FillRectangle(Brush(chosen ? 0xFFFFD040 : white ? 0xFFD4CEBC : 0xFF34343E), new Rect(bx, ty, 70, 46), 5);
            TrapIcon(dc, (TrapKind)i, bx + 22, ty + 23);
            Text(dc, s.Traps[i].ToString(), new Point(bx + 58, ty + 23), 26, s.Traps[i] == 0 ? faint : chosen ? Ink : ink, center: true, shadow: false);
        }
        // What he carries: the five things, and a remedy.
        double iy = oy + 296;
        Text(dc, "CARRYING", new Point(x0 + 14, iy - 20), 18, faint, shadow: false);
        for (int i = 0; i < 5; i++)
        {
            double bx = x0 + 14 + i * 50;
            dc.FillRectangle(Brush(white ? 0xFFD4CEBC : 0xFF34343E), new Rect(bx, iy - 6, 44, 44), 5);
            using (dc.PushOpacity(s.Items[i] ? 1 : 0.18))
                ItemIcon(dc, (Item)i, bx + 22, iy + 16);
        }
        double rx = x0 + 14 + 5 * 50 + 6;
        dc.FillRectangle(Brush(s.Remedy != Remedy.None ? 0xFF50B0E0 : white ? 0xFFD4CEBC : 0xFF34343E), new Rect(rx, iy - 6, 44, 44), 5);
        if (s.Remedy != Remedy.None) RemedyIcon(dc, s.Remedy, rx + 22, iy + 16);
    }

    public static void ItemIcon(DrawingContext dc, Item it, double x, double y)
    {
        switch (it)
        {
            case Item.Briefcase:
                dc.FillRectangle(Brush(0xFF7A4A1A), new Rect(x - 15, y - 8, 30, 20), 3);
                dc.DrawRectangle(null, Pen(0xFF4A2A0A, 3), new Rect(x - 6, y - 14, 12, 7), 2, 2);
                dc.FillRectangle(Brush(0xFFE0C060), new Rect(x - 3, y - 2, 6, 4));
                break;
            case Item.Plans:
                dc.FillRectangle(Brush(0xFF3060C0), new Rect(x - 14, y - 12, 28, 24), 2);
                for (int i = 0; i < 3; i++) dc.DrawLine(Pen(0xFFE0F0FF, 1.5), new Point(x - 10, y - 6 + i * 6), new Point(x + 10, y - 6 + i * 6));
                dc.DrawRectangle(null, Pen(0xFFE0F0FF, 1.5), new Rect(x - 4, y - 9, 10, 8));
                break;
            case Item.Key:
                dc.DrawEllipse(null, Pen(0xFFE0B020, 4), new Point(x - 8, y), 6, 6);
                dc.DrawLine(Pen(0xFFE0B020, 4), new Point(x - 2, y), new Point(x + 14, y));
                dc.DrawLine(Pen(0xFFE0B020, 3), new Point(x + 10, y), new Point(x + 10, y + 6));
                dc.DrawLine(Pen(0xFFE0B020, 3), new Point(x + 5, y), new Point(x + 5, y + 5));
                break;
            case Item.Money:
                dc.FillRectangle(Brush(0xFF2A8A3A), new Rect(x - 15, y - 9, 30, 18), 2);
                dc.DrawRectangle(null, Pen(0xFFB0F0B0, 1.5), new Rect(x - 12, y - 6, 24, 12));
                Text(dc, "$", new Point(x, y), 18, 0xFFE0FFE0, center: true, shadow: false);
                break;
            default:
                dc.FillRectangle(Brush(0xFF8A1A2A), new Rect(x - 11, y - 14, 22, 28), 2);
                dc.DrawEllipse(null, Pen(0xFFE0C060, 1.5), new Point(x, y - 2), 6, 6);
                dc.FillRectangle(Brush(0xFFE0C060), new Rect(x - 6, y + 7, 12, 2));
                break;
        }
    }

    public static void TrapIcon(DrawingContext dc, TrapKind k, double x, double y)
    {
        switch (k)
        {
            case TrapKind.Bomb:
                dc.FillEllipse(x, y + 3, 11, 0xFF18181E);
                dc.FillEllipse(x - 4, y - 1, 3, 0xFF6A6A78);
                dc.DrawLine(Pen(0xFFB08040, 2), new Point(x + 6, y - 7), new Point(x + 11, y - 13));
                Star(dc, x + 12, y - 14, 5, 0xFFFFA020);
                break;
            case TrapKind.Spring:
                for (int i = 0; i < 6; i++) dc.DrawLine(Pen(0xFF8A8A98, 3), new Point(x + (i % 2 == 0 ? -10 : 10), y + 12 - i * 4.6), new Point(x + (i % 2 == 0 ? 10 : -10), y + 12 - (i + 1) * 4.6));
                break;
            case TrapKind.Pail:
                Poly(dc, 0xFF8A949E, 0xFF5A646E, 1.5, new Point(x - 11, y - 8), new Point(x + 11, y - 8), new Point(x + 8, y + 12), new Point(x - 8, y + 12));
                dc.FillRectangle(Brush(0xFF60A0FF), new Rect(x - 10, y - 8, 20, 4));
                break;
            default:
                dc.FillRectangle(Brush(0xFF6A6A78), new Rect(x - 13, y - 7, 24, 7), 2);
                Poly(dc, 0xFF5A3A1A, null, 0, new Point(x - 13, y - 1), new Point(x - 5, y - 1), new Point(x - 8, y + 12), new Point(x - 15, y + 11));
                dc.DrawLine(Pen(0xFFE0E0E0, 1), new Point(x + 11, y - 4), new Point(x + 14, y - 14));
                break;
        }
    }

    public static void RemedyIcon(DrawingContext dc, Remedy r, double x, double y)
    {
        switch (r)
        {
            case Remedy.Water:
                Poly(dc, 0xFF80C0FF, 0xFF3060A0, 1.5, new Point(x - 9, y - 12), new Point(x + 9, y - 12), new Point(x + 11, y + 13), new Point(x - 11, y + 13));
                dc.FillRectangle(Brush(0xFF3080E0), new Rect(x - 10, y, 20, 12));
                break;
            case Remedy.Cutters:
                dc.DrawLine(Pen(0xFFC03030, 4), new Point(x - 10, y + 13), new Point(x, y));
                dc.DrawLine(Pen(0xFFC03030, 4), new Point(x + 10, y + 13), new Point(x, y));
                dc.DrawLine(Pen(0xFF9A9AA8, 4), new Point(x, y), new Point(x - 4, y - 13));
                dc.DrawLine(Pen(0xFF9A9AA8, 4), new Point(x, y), new Point(x + 4, y - 13));
                break;
            case Remedy.Umbrella:
                Poly(dc, 0xFFC03050, null, 0, new Point(x - 16, y - 2), new Point(x - 10, y - 12), new Point(x, y - 15), new Point(x + 10, y - 12), new Point(x + 16, y - 2));
                dc.DrawLine(Pen(0xFF303030, 2), new Point(x, y - 2), new Point(x, y + 12));
                dc.DrawLine(Pen(0xFF303030, 2), new Point(x, y + 12), new Point(x - 5, y + 12));
                break;
            case Remedy.Scissors:
                dc.DrawEllipse(null, Pen(0xFF3050C0, 3), new Point(x - 7, y + 9), 5, 5);
                dc.DrawEllipse(null, Pen(0xFF3050C0, 3), new Point(x + 7, y + 9), 5, 5);
                dc.DrawLine(Pen(0xFF9A9AA8, 3), new Point(x - 4, y + 5), new Point(x + 6, y - 14));
                dc.DrawLine(Pen(0xFF9A9AA8, 3), new Point(x + 4, y + 5), new Point(x - 6, y - 14));
                break;
        }
    }

    // ───────────────────────────── the title and the end ─────────────────────────────

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xFF14141C), new Rect(0, 0, Field.Width, Field.Height));
        // Half white, half black, the way the two spies see it.
        dc.FillRectangle(Brush(0xFFE8E4D8), new Rect(0, 0, cx, 420));
        dc.FillRectangle(Brush(0xFF24242C), new Rect(cx, 0, cx, 420));
        Text(dc, "AVA SPY", new Point(cx - 40, 70), 96, Ink, right: true, shadow: false);
        dc.FillEllipse(cx, 72, 30, 0xFFD03030);
        Text(dc, "VS", new Point(cx, 70), 40, 0xFFFFFFFF, center: true, shadow: false);
        Text(dc, "SPY", new Point(cx + 40, 70), 96, 0xFFF4F4EC, shadow: false);
        // The two spies, clubs up, facing off.
        var white = new Spy { Index = 0, X = 0, Z = 0, Facing = 1, Doing = Doing.Swinging, Timer = Sim.SwingTime * (0.75 + Math.Sin(_clock * 3) * 0.2) };
        var black = new Spy { Index = 1, X = 0, Z = 0, Facing = -1, Doing = Doing.Swinging, Timer = Sim.SwingTime * (0.75 + Math.Sin(_clock * 3 + 1.5) * 0.2) };
        using (dc.PushTransform(Matrix.CreateScale(2.0, 2.0) * Matrix.CreateTranslation(cx - 150 - 2 * 110, 380 - 2 * 150)))
            DrawSpy(dc, white, 0);
        using (dc.PushTransform(Matrix.CreateScale(2.0, 2.0) * Matrix.CreateTranslation(cx + 150 - 2 * 110, 380 - 2 * 150)))
            DrawSpy(dc, black, 0);
        Text(dc, "AFTER FIRST STAR SOFTWARE'S 1984 GAME", new Point(cx, 132), 22, 0xFF8A8478, center: true, shadow: false);
        Text(dc, "TWO SPIES, ONE EMBASSY. SEARCH THE FURNITURE FOR THE BRIEFCASE, THE PLANS, THE KEY,", new Point(cx, 456), 22, White, center: true);
        Text(dc, "THE MONEY AND THE PASSPORT, THEN GET OUT TO THE AIRPORT BEFORE YOUR CLOCK RUNS DOWN.", new Point(cx, 482), 22, White, center: true);
        Text(dc, "BOOBY-TRAP THE FURNITURE AND THE DOORS - AND CLUB THE OTHER SPY WHEN YOU MEET.", new Point(cx, 508), 22, White, center: true);
        // The traps and what gets you through them.
        for (int i = 0; i < 4; i++)
        {
            double bx = cx - 330 + i * 170;
            dc.FillRectangle(Brush(0xFF2A2A36), new Rect(bx, 540, 150, 56), 6);
            TrapIcon(dc, (TrapKind)i, bx + 32, 568);
            Text(dc, "→", new Point(bx + 75, 568), 26, Dim, center: true, shadow: false);
            RemedyIcon(dc, Kinds.Cure((TrapKind)i), bx + 118, 568);
        }
        Text(dc, "PRESS ENTER TO START", new Point(cx, 636), 40, (int)(_clock * 2) % 2 == 0 ? Yellow : Orange, center: true);
        Text(dc, "ARROWS: WALK   SPACE: SEARCH / CLUB   X: SET TRAP   C: NEXT TRAP   P: PAUSE   M: SOUND", new Point(cx, 690), 20, Dim, center: true);
        Text(dc, $"HIGH SCORE {Engine.HighScores.TopScore:000000}", new Point(cx, 730), 24, Cyan, center: true);
    }

    private void DrawGameOver(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        Text(dc, "GAME OVER", new Point(cx, 200), 110, Red, center: true);
        Text(dc, "THE BLACK SPY HAS THE EMBASSY TO HIMSELF", new Point(cx, 290), 30, White, center: true);
        var black = new Spy { Index = 1, Facing = -1, Doing = Doing.Swinging, Timer = Sim.SwingTime * (0.6 + Math.Sin(_clock * 5) * 0.3) };
        using (dc.PushTransform(Matrix.CreateScale(2.2, 2.2) * Matrix.CreateTranslation(cx - 2.2 * 110, 640 - 2.2 * 150)))
            DrawSpy(dc, black, 0);
        Text(dc, $"SCORE {Engine.Score}", new Point(cx, 700), 40, Yellow, center: true);
    }


    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "TOP AGENTS", new Point(cx, 120), 64, 0xFFFFE040, center: true);
        Text(dc, "RANK NAME    SCORE  EMBASSIES", new Point(cx, 190), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,6}", new Point(cx, 236 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 680), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }
}

internal static class DrawingExtensions
{
    public static void FillEllipse(this DrawingContext dc, double x, double y, double r, uint colour) =>
        dc.DrawEllipse(GameCanvas.Brush(colour), null, new Point(x, y), r, r);
    public static void FillRectangle(this DrawingContext dc, IBrush brush, Rect rect, double radius) => dc.FillRectangle(brush, rect, (float)radius);
}

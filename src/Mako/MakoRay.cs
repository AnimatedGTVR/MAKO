using Raylib_cs;
using System.Numerics;

namespace Mako;

/// MakoRay — MAKO's 2D/3D game and graphics layer built on Raylib.
///
/// MAKO usage pattern:
///
///   using MakoRay;
///
///   main() {
///       MakoRay.init(800, 600, "My Game");
///       MakoRay.fps(60);
///
///       while MakoRay.running() {
///           MakoRay.begin();
///           MakoRay.clear(MakoRay.BLACK);
///           MakoRay.text("Hello!", 10, 10, 20, MakoRay.WHITE);
///           MakoRay.end();
///       }
///
///       MakoRay.close();
///   }
///
static class MakoRay
{
    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public static object? Init(List<object?> args)
    {
        int w     = args.Count > 0 ? (int)Convert.ToDouble(args[0]) : 800;
        int h     = args.Count > 1 ? (int)Convert.ToDouble(args[1]) : 600;
        string t  = args.Count > 2 ? args[2]?.ToString() ?? "MAKO" : "MAKO";
        Raylib.InitWindow(w, h, t);
        return null;
    }

    public static object? SetFps(List<object?> args)
    {
        Raylib.SetTargetFPS(args.Count > 0 ? (int)Convert.ToDouble(args[0]) : 60);
        return null;
    }

    public static object? Running(List<object?> _) =>
        (object?)(!Raylib.WindowShouldClose());

    public static object? Begin(List<object?> _)
    {
        Raylib.BeginDrawing();
        return null;
    }

    public static object? End(List<object?> _)
    {
        Raylib.EndDrawing();
        return null;
    }

    public static object? Close(List<object?> _)
    {
        Raylib.CloseWindow();
        return null;
    }

    public static object? GetDelta(List<object?> _) =>
        (object?)(double)Raylib.GetFrameTime();

    public static object? GetFps(List<object?> _) =>
        (object?)(double)Raylib.GetFPS();

    public static object? GetTime(List<object?> _) =>
        (object?)(double)Raylib.GetTime();

    public static object? GetWidth(List<object?> _) =>
        (object?)(double)Raylib.GetScreenWidth();

    public static object? GetHeight(List<object?> _) =>
        (object?)(double)Raylib.GetScreenHeight();

    public static object? SetTitle(List<object?> args)
    {
        Raylib.SetWindowTitle(args.Count > 0 ? args[0]?.ToString() ?? "" : "");
        return null;
    }

    // ── Drawing ───────────────────────────────────────────────────────────────

    public static object? Clear(List<object?> args)
    {
        Raylib.ClearBackground(ToColor(args.Count > 0 ? args[0] : null));
        return null;
    }

    public static object? DrawText(List<object?> args)
    {
        string txt = args.Count > 0 ? args[0]?.ToString() ?? "" : "";
        int x      = args.Count > 1 ? (int)Convert.ToDouble(args[1]) : 0;
        int y      = args.Count > 2 ? (int)Convert.ToDouble(args[2]) : 0;
        int size   = args.Count > 3 ? (int)Convert.ToDouble(args[3]) : 20;
        var color  = args.Count > 4 ? ToColor(args[4]) : Color.White;
        Raylib.DrawText(txt, x, y, size, color);
        return null;
    }

    public static object? DrawFps(List<object?> args)
    {
        int x = args.Count > 0 ? (int)Convert.ToDouble(args[0]) : 10;
        int y = args.Count > 1 ? (int)Convert.ToDouble(args[1]) : 10;
        Raylib.DrawFPS(x, y);
        return null;
    }

    // ── Shapes ────────────────────────────────────────────────────────────────

    public static object? DrawRect(List<object?> args)
    {
        int x = (int)Convert.ToDouble(args[0]);
        int y = (int)Convert.ToDouble(args[1]);
        int w = (int)Convert.ToDouble(args[2]);
        int h = (int)Convert.ToDouble(args[3]);
        var c = args.Count > 4 ? ToColor(args[4]) : Color.White;
        Raylib.DrawRectangle(x, y, w, h, c);
        return null;
    }

    public static object? DrawRectLines(List<object?> args)
    {
        int x = (int)Convert.ToDouble(args[0]);
        int y = (int)Convert.ToDouble(args[1]);
        int w = (int)Convert.ToDouble(args[2]);
        int h = (int)Convert.ToDouble(args[3]);
        var c = args.Count > 4 ? ToColor(args[4]) : Color.White;
        Raylib.DrawRectangleLines(x, y, w, h, c);
        return null;
    }

    public static object? DrawRectRound(List<object?> args)
    {
        float x        = (float)Convert.ToDouble(args[0]);
        float y        = (float)Convert.ToDouble(args[1]);
        float w        = (float)Convert.ToDouble(args[2]);
        float h        = (float)Convert.ToDouble(args[3]);
        float roundness = args.Count > 4 ? (float)Convert.ToDouble(args[4]) : 0.2f;
        int   segs     = args.Count > 5 ? (int)Convert.ToDouble(args[5]) : 8;
        var   c        = args.Count > 6 ? ToColor(args[6]) : Color.White;
        Raylib.DrawRectangleRounded(new Rectangle(x, y, w, h), roundness, segs, c);
        return null;
    }

    public static object? DrawCircle(List<object?> args)
    {
        int   x = (int)Convert.ToDouble(args[0]);
        int   y = (int)Convert.ToDouble(args[1]);
        float r = (float)Convert.ToDouble(args[2]);
        var   c = args.Count > 3 ? ToColor(args[3]) : Color.White;
        Raylib.DrawCircle(x, y, r, c);
        return null;
    }

    public static object? DrawCircleLines(List<object?> args)
    {
        int   x = (int)Convert.ToDouble(args[0]);
        int   y = (int)Convert.ToDouble(args[1]);
        float r = (float)Convert.ToDouble(args[2]);
        var   c = args.Count > 3 ? ToColor(args[3]) : Color.White;
        Raylib.DrawCircleLines(x, y, r, c);
        return null;
    }

    public static object? DrawLine(List<object?> args)
    {
        int x1 = (int)Convert.ToDouble(args[0]);
        int y1 = (int)Convert.ToDouble(args[1]);
        int x2 = (int)Convert.ToDouble(args[2]);
        int y2 = (int)Convert.ToDouble(args[3]);
        var c  = args.Count > 4 ? ToColor(args[4]) : Color.White;
        Raylib.DrawLine(x1, y1, x2, y2, c);
        return null;
    }

    public static object? DrawTriangle(List<object?> args)
    {
        var v1 = ToVec2(args, 0);
        var v2 = ToVec2(args, 2);
        var v3 = ToVec2(args, 4);
        var c  = args.Count > 6 ? ToColor(args[6]) : Color.White;
        Raylib.DrawTriangle(v1, v2, v3, c);
        return null;
    }

    // ── Input — keyboard ──────────────────────────────────────────────────────

    public static object? IsKeyDown(List<object?> args) =>
        (object?)Raylib.IsKeyDown(ToKey(args[0]));

    public static object? IsKeyPressed(List<object?> args) =>
        (object?)Raylib.IsKeyPressed(ToKey(args[0]));

    public static object? IsKeyReleased(List<object?> args) =>
        (object?)Raylib.IsKeyReleased(ToKey(args[0]));

    public static object? GetKey(List<object?> _) =>
        (object?)(double)(int)Raylib.GetKeyPressed();

    // ── Input — mouse ─────────────────────────────────────────────────────────

    public static object? MouseX(List<object?> _) =>
        (object?)(double)Raylib.GetMouseX();

    public static object? MouseY(List<object?> _) =>
        (object?)(double)Raylib.GetMouseY();

    public static object? IsMouseDown(List<object?> args) =>
        (object?)Raylib.IsMouseButtonDown(ToMouseBtn(args[0]));

    public static object? IsMousePressed(List<object?> args) =>
        (object?)Raylib.IsMouseButtonPressed(ToMouseBtn(args[0]));

    public static object? MouseWheel(List<object?> _) =>
        (object?)(double)Raylib.GetMouseWheelMove();

    // ── Colors ────────────────────────────────────────────────────────────────

    public static object? MakeColor(List<object?> args)
    {
        byte r = (byte)Convert.ToDouble(args[0]);
        byte g = (byte)Convert.ToDouble(args[1]);
        byte b = (byte)Convert.ToDouble(args[2]);
        byte a = args.Count > 3 ? (byte)Convert.ToDouble(args[3]) : (byte)255;
        return ColorToList(new Color(r, g, b, a));
    }

    public static object? Fade(List<object?> args)
    {
        var c = ToColor(args[0]);
        float alpha = args.Count > 1 ? (float)Convert.ToDouble(args[1]) : 1f;
        return ColorToList(Raylib.Fade(c, alpha));
    }

    // Named color constants — returned as lists [r, g, b, a] that ToColor() accepts
    public static readonly Dictionary<string, object?> Colors = new()
    {
        ["BLACK"]     = ColorToList(Color.Black),
        ["WHITE"]     = ColorToList(Color.White),
        ["RED"]       = ColorToList(Color.Red),
        ["GREEN"]     = ColorToList(Color.Green),
        ["BLUE"]      = ColorToList(Color.Blue),
        ["YELLOW"]    = ColorToList(Color.Yellow),
        ["ORANGE"]    = ColorToList(Color.Orange),
        ["PURPLE"]    = ColorToList(Color.Purple),
        ["PINK"]      = ColorToList(Color.Pink),
        ["GRAY"]      = ColorToList(Color.Gray),
        ["DARKGRAY"]  = ColorToList(Color.DarkGray),
        ["LIGHTGRAY"] = ColorToList(Color.LightGray),
        ["SKYBLUE"]   = ColorToList(Color.SkyBlue),
        ["BROWN"]     = ColorToList(Color.Brown),
        ["BEIGE"]     = ColorToList(Color.Beige),
        ["LIME"]      = ColorToList(Color.Lime),
        ["GOLD"]      = ColorToList(Color.Gold),
        ["VIOLET"]    = ColorToList(Color.Violet),
        ["MAROON"]    = ColorToList(Color.Maroon),
        ["BLANK"]     = ColorToList(Color.Blank),
        ["RAYWHITE"]  = ColorToList(new Color(245, 245, 245, 255)),
    };

    // ── Audio ─────────────────────────────────────────────────────────────────

    public static object? InitAudio(List<object?> _)
    {
        Raylib.InitAudioDevice();
        return null;
    }

    public static object? CloseAudio(List<object?> _)
    {
        Raylib.CloseAudioDevice();
        return null;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static List<object?> ColorToList(Color c) =>
        new List<object?> { (object?)(double)c.R, (double)c.G, (double)c.B, (double)c.A };

    internal static Color ToColor(object? v)
    {
        if (v is List<object?> list && list.Count >= 3)
        {
            byte r = (byte)Convert.ToDouble(list[0]);
            byte g = (byte)Convert.ToDouble(list[1]);
            byte b = (byte)Convert.ToDouble(list[2]);
            byte a = list.Count > 3 ? (byte)Convert.ToDouble(list[3]) : (byte)255;
            return new Color(r, g, b, a);
        }
        // Fallback: white
        return Color.White;
    }

    private static Vector2 ToVec2(List<object?> args, int offset) =>
        new Vector2(
            (float)Convert.ToDouble(args[offset]),
            (float)Convert.ToDouble(args[offset + 1]));

    private static KeyboardKey ToKey(object? v)
    {
        if (v is string s)
        {
            // Accept single-char strings as key names
            if (s.Length == 1) return (KeyboardKey)(int)char.ToUpper(s[0]);
            return s.ToUpper() switch
            {
                "SPACE"  => KeyboardKey.Space,
                "ENTER"  => KeyboardKey.Enter,
                "ESCAPE" => KeyboardKey.Escape,
                "UP"     => KeyboardKey.Up,
                "DOWN"   => KeyboardKey.Down,
                "LEFT"   => KeyboardKey.Left,
                "RIGHT"  => KeyboardKey.Right,
                "SHIFT"  => KeyboardKey.LeftShift,
                "CTRL"   => KeyboardKey.LeftControl,
                "ALT"    => KeyboardKey.LeftAlt,
                "TAB"    => KeyboardKey.Tab,
                "BACKSPACE" => KeyboardKey.Backspace,
                _        => KeyboardKey.Null,
            };
        }
        return (KeyboardKey)(int)Convert.ToDouble(v);
    }

    private static MouseButton ToMouseBtn(object? v)
    {
        if (v is string s)
            return s.ToLower() switch
            {
                "left"   => MouseButton.Left,
                "right"  => MouseButton.Right,
                "middle" => MouseButton.Middle,
                _ => MouseButton.Left,
            };
        return (MouseButton)(int)Convert.ToDouble(v);
    }

    // ── Function dispatch table ───────────────────────────────────────────────

    public static readonly Dictionary<string, Func<List<object?>, object?>> Funcs = new()
    {
        // Lifecycle
        ["init"]        = Init,
        ["fps"]         = SetFps,
        ["running"]     = Running,
        ["begin"]       = Begin,
        ["end"]         = End,
        ["close"]       = Close,
        ["delta"]       = GetDelta,
        ["get_fps"]     = GetFps,
        ["get_time"]    = GetTime,
        ["width"]       = GetWidth,
        ["height"]      = GetHeight,
        ["title"]       = SetTitle,
        // Drawing
        ["clear"]       = Clear,
        ["text"]        = DrawText,
        ["draw_fps"]    = DrawFps,
        // Shapes
        ["rect"]        = DrawRect,
        ["rect_lines"]  = DrawRectLines,
        ["rect_round"]  = DrawRectRound,
        ["circle"]      = DrawCircle,
        ["circle_lines"]= DrawCircleLines,
        ["line"]        = DrawLine,
        ["triangle"]    = DrawTriangle,
        // Keyboard
        ["key_down"]    = IsKeyDown,
        ["key_pressed"] = IsKeyPressed,
        ["key_released"]= IsKeyReleased,
        ["get_key"]     = GetKey,
        // Mouse
        ["mouse_x"]     = MouseX,
        ["mouse_y"]     = MouseY,
        ["mouse_down"]  = IsMouseDown,
        ["mouse_pressed"] = IsMousePressed,
        ["mouse_wheel"] = MouseWheel,
        // Color
        ["color"]       = MakeColor,
        ["fade"]        = Fade,
        // Audio
        ["init_audio"]  = InitAudio,
        ["close_audio"] = CloseAudio,
    };
}

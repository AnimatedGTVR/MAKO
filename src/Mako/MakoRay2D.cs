using Raylib_cs;
using System.Numerics;

namespace Mako;

/// Mako2D — 2D game rendering: sprites, textures, Camera2D, animation.
///
///   using Mako2D;
///
///   main() {
///       Mako2D.init(800, 600, "My Game");
///       Mako2D.fps(60);
///       img = Mako2D.load("player.png");
///       cam = Mako2D.camera(0, 0);
///       x = 100; y = 100;
///       while Mako2D.running() {
///           if Mako2D.key_down("RIGHT") { x = x + 3; }
///           Mako2D.begin();
///           Mako2D.clear(Mako2D.BLACK);
///           Mako2D.begin_cam(cam);
///           Mako2D.sprite(img, x, y);
///           Mako2D.end_cam();
///           Mako2D.text("Score: 0", 10, 10, 20, Mako2D.WHITE);
///           Mako2D.end();
///       }
///       Mako2D.close();
///   }
///
static class MakoRay2D
{
    // ── Texture / camera handles ──────────────────────────────────────────────

    private static readonly List<Texture2D> _textures = [];
    private static readonly List<Camera2D>  _cameras  = [];

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public static object? Init(List<object?> a)
    {
        int w    = a.Count > 0 ? (int)Convert.ToDouble(a[0]) : 800;
        int h    = a.Count > 1 ? (int)Convert.ToDouble(a[1]) : 600;
        string t = a.Count > 2 ? a[2]?.ToString() ?? "Mako2D" : "Mako2D";
        Raylib.InitWindow(w, h, t);
        // Disable raylib's built-in ESC-to-quit: scripts decide when to exit.
        // (A stray ESC event on window focus was closing windows instantly.)
        Raylib.SetExitKey(KeyboardKey.Null);
        // Grab keyboard focus — under Wayland/Hyprland a new XWayland window
        // doesn't always take focus, leaving keys going to the terminal.
        Raylib.SetWindowFocused();
        return null;
    }

    public static object? SetFps(List<object?> a)    { Raylib.SetTargetFPS(a.Count > 0 ? (int)Convert.ToDouble(a[0]) : 60); return null; }
    public static object? Running(List<object?> _)    => (object?)(bool)(!Raylib.WindowShouldClose());
    public static object? Begin(List<object?> _)      { Raylib.BeginDrawing(); return null; }
    public static object? End(List<object?> _)        { Raylib.EndDrawing();   return null; }
    public static object? Close(List<object?> _)      { if (Raylib.IsWindowReady()) Raylib.CloseWindow(); return null; }
    public static object? Delta(List<object?> _)      => (object?)(double)Raylib.GetFrameTime();
    public static object? GetFps(List<object?> _)     => (object?)(double)Raylib.GetFPS();
    public static object? Width(List<object?> _)      => (object?)(double)Raylib.GetScreenWidth();
    public static object? Height(List<object?> _)     => (object?)(double)Raylib.GetScreenHeight();
    public static object? SetTitle(List<object?> a)   { Raylib.SetWindowTitle(a.Count > 0 ? a[0]?.ToString() ?? "" : ""); return null; }
    public static object? DrawFps(List<object?> a)    { Raylib.DrawFPS(a.Count > 0 ? (int)Convert.ToDouble(a[0]) : 10, a.Count > 1 ? (int)Convert.ToDouble(a[1]) : 10); return null; }

    // ── Textures ──────────────────────────────────────────────────────────────

    public static object? Load(List<object?> a)
    {
        string path = a.Count > 0 ? a[0]?.ToString() ?? "" : "";
        path = MakoAssets.Resolve(path);
        if (!File.Exists(path))
            throw new MakoError($"Mako2D.load(): file not found: '{path}'");
        var tex = Raylib.LoadTexture(path);
        _textures.Add(tex);
        return (object?)(double)(_textures.Count - 1);
    }

    public static object? Unload(List<object?> a)
    {
        int id = (int)Convert.ToDouble(a[0]);
        if (id >= 0 && id < _textures.Count) Raylib.UnloadTexture(_textures[id]);
        return null;
    }

    public static object? TexWidth(List<object?> a)
    {
        int id = (int)Convert.ToDouble(a[0]);
        return id >= 0 && id < _textures.Count ? (object?)(double)_textures[id].Width : 0d;
    }

    public static object? TexHeight(List<object?> a)
    {
        int id = (int)Convert.ToDouble(a[0]);
        return id >= 0 && id < _textures.Count ? (object?)(double)_textures[id].Height : 0d;
    }

    // ── Camera2D ──────────────────────────────────────────────────────────────

    /// camera(target_x, target_y, zoom=1, rotation=0) → handle
    public static object? MakeCamera(List<object?> a)
    {
        float tx  = a.Count > 0 ? (float)Convert.ToDouble(a[0]) : 0;
        float ty  = a.Count > 1 ? (float)Convert.ToDouble(a[1]) : 0;
        float z   = a.Count > 2 ? (float)Convert.ToDouble(a[2]) : 1f;
        float rot = a.Count > 3 ? (float)Convert.ToDouble(a[3]) : 0f;
        var cam = new Camera2D
        {
            Target   = new Vector2(tx, ty),
            Offset   = new Vector2(Raylib.GetScreenWidth() / 2f, Raylib.GetScreenHeight() / 2f),
            Zoom     = z,
            Rotation = rot,
        };
        _cameras.Add(cam);
        return (object?)(double)(_cameras.Count - 1);
    }

    public static object? SetCamera(List<object?> a)
    {
        int id  = (int)Convert.ToDouble(a[0]);
        if (id < 0 || id >= _cameras.Count) return null;
        var cam = _cameras[id];
        if (a.Count > 1) cam.Target.X  = (float)Convert.ToDouble(a[1]);
        if (a.Count > 2) cam.Target.Y  = (float)Convert.ToDouble(a[2]);
        if (a.Count > 3) cam.Zoom      = (float)Convert.ToDouble(a[3]);
        if (a.Count > 4) cam.Rotation  = (float)Convert.ToDouble(a[4]);
        _cameras[id] = cam;
        return null;
    }

    public static object? BeginCam(List<object?> a)
    {
        int id = a.Count > 0 ? (int)Convert.ToDouble(a[0]) : 0;
        if (id >= 0 && id < _cameras.Count) Raylib.BeginMode2D(_cameras[id]);
        return null;
    }

    public static object? EndCam(List<object?> _) { Raylib.EndMode2D(); return null; }

    // ── Drawing ───────────────────────────────────────────────────────────────

    public static object? Clear(List<object?> a) { Raylib.ClearBackground(MakoRay.ToColor(a.Count > 0 ? a[0] : null)); return null; }

    public static object? DrawText(List<object?> a)
    {
        string s  = a.Count > 0 ? a[0]?.ToString() ?? "" : "";
        int x     = a.Count > 1 ? (int)Convert.ToDouble(a[1]) : 0;
        int y     = a.Count > 2 ? (int)Convert.ToDouble(a[2]) : 0;
        int size  = a.Count > 3 ? (int)Convert.ToDouble(a[3]) : 20;
        var col   = a.Count > 4 ? MakoRay.ToColor(a[4]) : Color.White;
        Raylib.DrawText(s, x, y, size, col);
        return null;
    }

    /// sprite(texture_id, x, y, scale=1, tint=WHITE)
    public static object? Sprite(List<object?> a)
    {
        int id    = a.Count > 0 ? (int)Convert.ToDouble(a[0]) : -1;
        float x   = a.Count > 1 ? (float)Convert.ToDouble(a[1]) : 0;
        float y   = a.Count > 2 ? (float)Convert.ToDouble(a[2]) : 0;
        float sc  = a.Count > 3 ? (float)Convert.ToDouble(a[3]) : 1f;
        var tint  = a.Count > 4 ? MakoRay.ToColor(a[4]) : Color.White;
        if (id < 0 || id >= _textures.Count) return null;
        Raylib.DrawTextureEx(_textures[id], new Vector2(x, y), 0f, sc, tint);
        return null;
    }

    /// sprite_frame(texture_id, x, y, frame_x, frame_y, frame_w, frame_h, scale=1, tint=WHITE)
    /// Draws a sub-rectangle of the texture (for spritesheets/animation frames).
    public static object? SpriteFrame(List<object?> a)
    {
        int id     = a.Count > 0 ? (int)Convert.ToDouble(a[0]) : -1;
        float x    = a.Count > 1 ? (float)Convert.ToDouble(a[1]) : 0;
        float y    = a.Count > 2 ? (float)Convert.ToDouble(a[2]) : 0;
        float fx   = a.Count > 3 ? (float)Convert.ToDouble(a[3]) : 0;
        float fy   = a.Count > 4 ? (float)Convert.ToDouble(a[4]) : 0;
        float fw   = a.Count > 5 ? (float)Convert.ToDouble(a[5]) : 32;
        float fh   = a.Count > 6 ? (float)Convert.ToDouble(a[6]) : 32;
        float sc   = a.Count > 7 ? (float)Convert.ToDouble(a[7]) : 1f;
        var tint   = a.Count > 8 ? MakoRay.ToColor(a[8]) : Color.White;
        if (id < 0 || id >= _textures.Count) return null;
        var src  = new Rectangle(fx, fy, fw, fh);
        var dest = new Rectangle(x, y, fw * sc, fh * sc);
        Raylib.DrawTexturePro(_textures[id], src, dest, Vector2.Zero, 0f, tint);
        return null;
    }

    /// sprite_rot(texture_id, x, y, rotation_deg, scale=1, tint=WHITE)
    public static object? SpriteRot(List<object?> a)
    {
        int id    = a.Count > 0 ? (int)Convert.ToDouble(a[0]) : -1;
        float x   = a.Count > 1 ? (float)Convert.ToDouble(a[1]) : 0;
        float y   = a.Count > 2 ? (float)Convert.ToDouble(a[2]) : 0;
        float rot = a.Count > 3 ? (float)Convert.ToDouble(a[3]) : 0;
        float sc  = a.Count > 4 ? (float)Convert.ToDouble(a[4]) : 1f;
        var tint  = a.Count > 5 ? MakoRay.ToColor(a[5]) : Color.White;
        if (id < 0 || id >= _textures.Count) return null;
        var tex  = _textures[id];
        var src  = new Rectangle(0, 0, tex.Width, tex.Height);
        var dest = new Rectangle(x, y, tex.Width * sc, tex.Height * sc);
        var orig = new Vector2(tex.Width * sc / 2f, tex.Height * sc / 2f);
        Raylib.DrawTexturePro(tex, src, dest, orig, rot, tint);
        return null;
    }

    // ── Shapes (2D) ───────────────────────────────────────────────────────────

    public static object? Rect(List<object?> a)
    {
        int x = (int)Convert.ToDouble(a[0]); int y = (int)Convert.ToDouble(a[1]);
        int w = (int)Convert.ToDouble(a[2]); int h = (int)Convert.ToDouble(a[3]);
        Raylib.DrawRectangle(x, y, w, h, a.Count > 4 ? MakoRay.ToColor(a[4]) : Color.White);
        return null;
    }
    public static object? RectLines(List<object?> a)
    {
        int x = (int)Convert.ToDouble(a[0]); int y = (int)Convert.ToDouble(a[1]);
        int w = (int)Convert.ToDouble(a[2]); int h = (int)Convert.ToDouble(a[3]);
        Raylib.DrawRectangleLines(x, y, w, h, a.Count > 4 ? MakoRay.ToColor(a[4]) : Color.White);
        return null;
    }
    public static object? RectRound(List<object?> a)
    {
        float x = (float)Convert.ToDouble(a[0]); float y = (float)Convert.ToDouble(a[1]);
        float w = (float)Convert.ToDouble(a[2]); float h = (float)Convert.ToDouble(a[3]);
        float r = a.Count > 4 ? (float)Convert.ToDouble(a[4]) : 0.2f;
        int   s = a.Count > 5 ? (int)Convert.ToDouble(a[5])   : 8;
        Raylib.DrawRectangleRounded(new Rectangle(x, y, w, h), r, s, a.Count > 6 ? MakoRay.ToColor(a[6]) : Color.White);
        return null;
    }
    public static object? Circle(List<object?> a)
    {
        int x = (int)Convert.ToDouble(a[0]); int y = (int)Convert.ToDouble(a[1]);
        float r = (float)Convert.ToDouble(a[2]);
        Raylib.DrawCircle(x, y, r, a.Count > 3 ? MakoRay.ToColor(a[3]) : Color.White);
        return null;
    }
    public static object? CircleLines(List<object?> a)
    {
        int x = (int)Convert.ToDouble(a[0]); int y = (int)Convert.ToDouble(a[1]);
        float r = (float)Convert.ToDouble(a[2]);
        Raylib.DrawCircleLines(x, y, r, a.Count > 3 ? MakoRay.ToColor(a[3]) : Color.White);
        return null;
    }
    public static object? Line(List<object?> a)
    {
        Raylib.DrawLine(
            (int)Convert.ToDouble(a[0]), (int)Convert.ToDouble(a[1]),
            (int)Convert.ToDouble(a[2]), (int)Convert.ToDouble(a[3]),
            a.Count > 4 ? MakoRay.ToColor(a[4]) : Color.White);
        return null;
    }
    public static object? Triangle(List<object?> a)
    {
        var v1 = new Vector2((float)Convert.ToDouble(a[0]), (float)Convert.ToDouble(a[1]));
        var v2 = new Vector2((float)Convert.ToDouble(a[2]), (float)Convert.ToDouble(a[3]));
        var v3 = new Vector2((float)Convert.ToDouble(a[4]), (float)Convert.ToDouble(a[5]));
        Raylib.DrawTriangle(v1, v2, v3, a.Count > 6 ? MakoRay.ToColor(a[6]) : Color.White);
        return null;
    }

    // ── Input ─────────────────────────────────────────────────────────────────

    public static object? KeyDown(List<object?> a)     => (object?)(bool)Raylib.IsKeyDown(ToKey(a[0]));
    public static object? KeyPressed(List<object?> a)  => (object?)(bool)Raylib.IsKeyPressed(ToKey(a[0]));
    public static object? KeyReleased(List<object?> a) => (object?)(bool)Raylib.IsKeyReleased(ToKey(a[0]));
    public static object? MouseX(List<object?> _)      => (object?)(double)Raylib.GetMouseX();
    public static object? MouseY(List<object?> _)      => (object?)(double)Raylib.GetMouseY();
    public static object? MouseDown(List<object?> a)   => (object?)(bool)Raylib.IsMouseButtonDown(ToMouseBtn(a[0]));
    public static object? MousePressed(List<object?> a)=> (object?)(bool)Raylib.IsMouseButtonPressed(ToMouseBtn(a[0]));
    public static object? MouseWheel(List<object?> _)  => (object?)(double)Raylib.GetMouseWheelMove();

    /// screen_to_world(cam, screen_x, screen_y) → [world_x, world_y]
    public static object? ScreenToWorld(List<object?> a)
    {
        int id  = (int)Convert.ToDouble(a[0]);
        float sx = (float)Convert.ToDouble(a[1]);
        float sy = (float)Convert.ToDouble(a[2]);
        if (id < 0 || id >= _cameras.Count) return new List<object?> { (object?)sx, sy };
        var wv = Raylib.GetScreenToWorld2D(new Vector2(sx, sy), _cameras[id]);
        return new List<object?> { (object?)(double)wv.X, (double)wv.Y };
    }

    // ── Colors ────────────────────────────────────────────────────────────────

    public static object? MakeColor(List<object?> a)
    {
        byte r = (byte)Convert.ToDouble(a[0]); byte g = (byte)Convert.ToDouble(a[1]);
        byte b = (byte)Convert.ToDouble(a[2]); byte al = a.Count > 3 ? (byte)Convert.ToDouble(a[3]) : (byte)255;
        return ColorList(new Color(r, g, b, al));
    }
    public static object? Fade(List<object?> a)
    {
        var c = MakoRay.ToColor(a[0]);
        float alpha = a.Count > 1 ? (float)Convert.ToDouble(a[1]) : 1f;
        return ColorList(Raylib.Fade(c, alpha));
    }

    public static readonly Dictionary<string, object?> Colors = MakoRay.Colors
        .ToDictionary(kv => kv.Key, kv => kv.Value);

    // ── Cleanup ───────────────────────────────────────────────────────────────

    public static void UnloadAll()
    {
        // GPU unloads need a live GL context; skip them if the window is gone.
        if (Raylib.IsWindowReady())
            foreach (var t in _textures) Raylib.UnloadTexture(t);
        _textures.Clear();
        _cameras.Clear();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static List<object?> ColorList(Color c) =>
        new() { (object?)(double)c.R, (double)c.G, (double)c.B, (double)c.A };

    private static KeyboardKey ToKey(object? v) => MakoInputs.ToKey(v);

    private static MouseButton ToMouseBtn(object? v)
    {
        if (v is string s) return s.ToLower() switch
        { "left" => MouseButton.Left, "right" => MouseButton.Right,
          "middle" => MouseButton.Middle, _ => MouseButton.Left };
        return (MouseButton)(int)Convert.ToDouble(v);
    }

    // ── Dispatch table ────────────────────────────────────────────────────────

    public static readonly Dictionary<string, Func<List<object?>, object?>> Funcs = new()
    {
        ["init"]         = Init,         ["fps"]          = SetFps,
        ["running"]      = Running,      ["begin"]        = Begin,
        ["end"]          = End,          ["close"]        = Close,
        ["delta"]        = Delta,        ["get_fps"]      = GetFps,
        ["width"]        = Width,        ["height"]       = Height,
        ["title"]        = SetTitle,     ["draw_fps"]     = DrawFps,
        ["load"]         = Load,         ["unload"]       = Unload,
        ["tex_width"]    = TexWidth,     ["tex_height"]   = TexHeight,
        ["camera"]       = MakeCamera,   ["set_camera"]   = SetCamera,
        ["begin_cam"]    = BeginCam,     ["end_cam"]      = EndCam,
        ["clear"]        = Clear,        ["text"]         = DrawText,
        ["sprite"]       = Sprite,       ["sprite_frame"] = SpriteFrame,
        ["sprite_rot"]   = SpriteRot,
        ["rect"]         = Rect,         ["rect_lines"]   = RectLines,
        ["rect_round"]   = RectRound,    ["circle"]       = Circle,
        ["circle_lines"] = CircleLines,  ["line"]         = Line,
        ["triangle"]     = Triangle,
        ["key_down"]     = KeyDown,      ["key_pressed"]  = KeyPressed,
        ["key_released"] = KeyReleased,
        ["mouse_x"]      = MouseX,       ["mouse_y"]      = MouseY,
        ["mouse_down"]   = MouseDown,    ["mouse_pressed"]= MousePressed,
        ["mouse_wheel"]  = MouseWheel,   ["screen_to_world"] = ScreenToWorld,
        ["color"]        = MakeColor,    ["fade"]         = Fade,
    };
}

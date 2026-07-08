using Raylib_cs;
using System.Numerics;

namespace Mako;

/// Mako3D — 3D game rendering: Camera3D, primitives, models, lighting.
///
///   using Mako3D;
///
///   main() {
///       Mako3D.init(800, 600, "My 3D Game");
///       Mako3D.fps(60);
///       cam = Mako3D.camera(0, 10, 10,  0, 0, 0);
///       angle = 0;
///       while Mako3D.running() {
///           angle = angle + Mako3D.delta() * 50;
///           Mako3D.begin();
///           Mako3D.clear(Mako3D.RAYWHITE);
///           Mako3D.begin_3d(cam);
///           Mako3D.cube(0, 1, 0,  2, 2, 2,  Mako3D.RED);
///           Mako3D.sphere(4, 1, 0,  1.5,  Mako3D.BLUE);
///           Mako3D.grid(10, 1);
///           Mako3D.end_3d();
///           Mako3D.draw_fps(10, 10);
///           Mako3D.end();
///       }
///       Mako3D.close();
///   }
///
static class MakoRay3D
{
    // ── Camera / model handles ────────────────────────────────────────────────

    private static readonly List<Camera3D> _cameras = [];
    private static readonly List<Model>    _models  = [];
    private static readonly List<Texture2D>_textures= [];

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public static object? Init(List<object?> a)
    {
        int w    = a.Count > 0 ? (int)Convert.ToDouble(a[0]) : 800;
        int h    = a.Count > 1 ? (int)Convert.ToDouble(a[1]) : 600;
        string t = a.Count > 2 ? a[2]?.ToString() ?? "Mako3D" : "Mako3D";
        Raylib.InitWindow(w, h, t);
        return null;
    }

    public static object? SetFps(List<object?> a)  { Raylib.SetTargetFPS(a.Count > 0 ? (int)Convert.ToDouble(a[0]) : 60); return null; }
    public static object? Running(List<object?> _)  => (object?)(!Raylib.WindowShouldClose());
    public static object? Begin(List<object?> _)    { Raylib.BeginDrawing(); return null; }
    public static object? End(List<object?> _)      { Raylib.EndDrawing();   return null; }
    public static object? Close(List<object?> _)    { Raylib.CloseWindow();  return null; }
    public static object? Delta(List<object?> _)    => (object?)(double)Raylib.GetFrameTime();
    public static object? GetFps(List<object?> _)   => (object?)(double)Raylib.GetFPS();
    public static object? Width(List<object?> _)    => (object?)(double)Raylib.GetScreenWidth();
    public static object? Height(List<object?> _)   => (object?)(double)Raylib.GetScreenHeight();
    public static object? SetTitle(List<object?> a) { Raylib.SetWindowTitle(a.Count > 0 ? a[0]?.ToString() ?? "" : ""); return null; }
    public static object? DrawFps(List<object?> a)  { Raylib.DrawFPS(a.Count > 0 ? (int)Convert.ToDouble(a[0]) : 10, a.Count > 1 ? (int)Convert.ToDouble(a[1]) : 10); return null; }

    // ── Camera3D ──────────────────────────────────────────────────────────────

    /// camera(pos_x, pos_y, pos_z,  target_x, target_y, target_z,  fov=45) → handle
    public static object? MakeCamera(List<object?> a)
    {
        var pos    = Vec3(a, 0);
        var target = a.Count > 3 ? Vec3(a, 3) : Vector3.Zero;
        float fov  = a.Count > 6 ? (float)Convert.ToDouble(a[6]) : 45f;
        var cam = new Camera3D
        {
            Position   = pos,
            Target     = target,
            Up         = Vector3.UnitY,
            FovY       = fov,
            Projection = CameraProjection.Perspective,
        };
        _cameras.Add(cam);
        return (object?)(double)(_cameras.Count - 1);
    }

    /// move_camera(handle, pos_x, pos_y, pos_z,  target_x, target_y, target_z)
    public static object? MoveCamera(List<object?> a)
    {
        int id = (int)Convert.ToDouble(a[0]);
        if (id < 0 || id >= _cameras.Count) return null;
        var cam = _cameras[id];
        if (a.Count > 1) cam.Position = Vec3(a, 1);
        if (a.Count > 4) cam.Target   = Vec3(a, 4);
        _cameras[id] = cam;
        return null;
    }

    /// orbit_camera(handle, angle_deg, distance, height) — rotate camera around target
    public static object? OrbitCamera(List<object?> a)
    {
        int id   = (int)Convert.ToDouble(a[0]);
        if (id < 0 || id >= _cameras.Count) return null;
        var cam  = _cameras[id];
        float ang = a.Count > 1 ? (float)(Convert.ToDouble(a[1]) * Math.PI / 180.0) : 0f;
        float dist= a.Count > 2 ? (float)Convert.ToDouble(a[2]) : 10f;
        float hy  = a.Count > 3 ? (float)Convert.ToDouble(a[3]) : cam.Position.Y;
        cam.Position = new Vector3(
            cam.Target.X + (float)Math.Cos(ang) * dist,
            hy,
            cam.Target.Z + (float)Math.Sin(ang) * dist);
        _cameras[id] = cam;
        return null;
    }

    public static object? Begin3D(List<object?> a)
    {
        int id = a.Count > 0 ? (int)Convert.ToDouble(a[0]) : 0;
        if (id >= 0 && id < _cameras.Count) Raylib.BeginMode3D(_cameras[id]);
        return null;
    }

    public static object? End3D(List<object?> _) { Raylib.EndMode3D(); return null; }

    // ── 3D Primitives ─────────────────────────────────────────────────────────

    /// cube(x, y, z,  w, h, d,  color)
    public static object? DrawCube(List<object?> a)
    {
        var pos   = Vec3(a, 0);
        float w   = a.Count > 3 ? (float)Convert.ToDouble(a[3]) : 1;
        float h   = a.Count > 4 ? (float)Convert.ToDouble(a[4]) : 1;
        float d   = a.Count > 5 ? (float)Convert.ToDouble(a[5]) : 1;
        var col   = a.Count > 6 ? MakoRay.ToColor(a[6]) : Color.Red;
        Raylib.DrawCube(pos, w, h, d, col);
        Raylib.DrawCubeWires(pos, w, h, d, Color.Black);
        return null;
    }

    /// cube_raw(x, y, z,  w, h, d,  color)  — no wireframe
    public static object? DrawCubeRaw(List<object?> a)
    {
        var pos = Vec3(a, 0);
        float w = (float)Convert.ToDouble(a[3]);
        float h = (float)Convert.ToDouble(a[4]);
        float d = (float)Convert.ToDouble(a[5]);
        Raylib.DrawCube(pos, w, h, d, a.Count > 6 ? MakoRay.ToColor(a[6]) : Color.White);
        return null;
    }

    /// sphere(x, y, z,  radius,  color)
    public static object? DrawSphere(List<object?> a)
    {
        var pos   = Vec3(a, 0);
        float r   = a.Count > 3 ? (float)Convert.ToDouble(a[3]) : 1;
        var col   = a.Count > 4 ? MakoRay.ToColor(a[4]) : Color.Blue;
        Raylib.DrawSphere(pos, r, col);
        Raylib.DrawSphereWires(pos, r, 8, 8, Color.Black);
        return null;
    }

    /// sphere_raw(x, y, z,  radius,  color)  — no wireframe
    public static object? DrawSphereRaw(List<object?> a)
    {
        var pos = Vec3(a, 0);
        float r = a.Count > 3 ? (float)Convert.ToDouble(a[3]) : 1;
        Raylib.DrawSphere(pos, r, a.Count > 4 ? MakoRay.ToColor(a[4]) : Color.White);
        return null;
    }

    /// cylinder(x, y, z,  radius_top, radius_bottom, height,  color)
    public static object? DrawCylinder(List<object?> a)
    {
        var pos   = Vec3(a, 0);
        float rt  = a.Count > 3 ? (float)Convert.ToDouble(a[3]) : 1;
        float rb  = a.Count > 4 ? (float)Convert.ToDouble(a[4]) : 1;
        float h   = a.Count > 5 ? (float)Convert.ToDouble(a[5]) : 2;
        var col   = a.Count > 6 ? MakoRay.ToColor(a[6]) : Color.Green;
        Raylib.DrawCylinder(pos, rt, rb, h, 16, col);
        Raylib.DrawCylinderWires(pos, rt, rb, h, 16, Color.Black);
        return null;
    }

    /// plane(x, y, z,  width, depth,  color)
    public static object? DrawPlane(List<object?> a)
    {
        var pos   = Vec3(a, 0);
        float w   = a.Count > 3 ? (float)Convert.ToDouble(a[3]) : 10;
        float d   = a.Count > 4 ? (float)Convert.ToDouble(a[4]) : 10;
        var col   = a.Count > 5 ? MakoRay.ToColor(a[5]) : Color.DarkGray;
        Raylib.DrawPlane(pos, new Vector2(w, d), col);
        return null;
    }

    /// grid(slices, spacing)
    public static object? DrawGrid(List<object?> a)
    {
        int   sl = a.Count > 0 ? (int)Convert.ToDouble(a[0])    : 10;
        float sp = a.Count > 1 ? (float)Convert.ToDouble(a[1]) : 1f;
        Raylib.DrawGrid(sl, sp);
        return null;
    }

    /// line3d(x1,y1,z1, x2,y2,z2, color)
    public static object? DrawLine3D(List<object?> a)
    {
        var v1  = Vec3(a, 0);
        var v2  = Vec3(a, 3);
        var col = a.Count > 6 ? MakoRay.ToColor(a[6]) : Color.White;
        Raylib.DrawLine3D(v1, v2, col);
        return null;
    }

    /// point3d(x, y, z, color)
    public static object? DrawPoint3D(List<object?> a)
    {
        var pos = Vec3(a, 0);
        var col = a.Count > 3 ? MakoRay.ToColor(a[3]) : Color.White;
        Raylib.DrawPoint3D(pos, col);
        return null;
    }

    // ── Models ────────────────────────────────────────────────────────────────

    /// load_model(path) → handle
    public static object? LoadModelFile(List<object?> a)
    {
        string path = a.Count > 0 ? a[0]?.ToString() ?? "" : "";
        if (!File.Exists(path))
            throw new MakoError($"Mako3D.load_model(): file not found: '{path}'");
        var m = Raylib.LoadModel(path);
        _models.Add(m);
        return (object?)(double)(_models.Count - 1);
    }

    /// draw_model(handle, x, y, z, scale, color)
    public static object? DrawModelHandle(List<object?> a)
    {
        int id    = (int)Convert.ToDouble(a[0]);
        var pos   = Vec3(a, 1);
        float sc  = a.Count > 4 ? (float)Convert.ToDouble(a[4]) : 1f;
        var col   = a.Count > 5 ? MakoRay.ToColor(a[5]) : Color.White;
        if (id >= 0 && id < _models.Count)
            Raylib.DrawModel(_models[id], pos, sc, col);
        return null;
    }

    // ── Sky / background ──────────────────────────────────────────────────────

    /// sky(r, g, b)  — set sky color (same as clear but named for 3D context)
    public static object? Sky(List<object?> a)
    {
        Raylib.ClearBackground(MakoRay.ToColor(a.Count > 0 ? a[0] : null));
        return null;
    }

    public static object? Clear(List<object?> a)
    {
        Raylib.ClearBackground(MakoRay.ToColor(a.Count > 0 ? a[0] : null));
        return null;
    }

    // ── 2D overlays (HUD) ─────────────────────────────────────────────────────

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

    // ── Input ─────────────────────────────────────────────────────────────────

    public static object? KeyDown(List<object?> a)     => (object?)Raylib.IsKeyDown(ToKey(a[0]));
    public static object? KeyPressed(List<object?> a)  => (object?)Raylib.IsKeyPressed(ToKey(a[0]));
    public static object? KeyReleased(List<object?> a) => (object?)Raylib.IsKeyReleased(ToKey(a[0]));
    public static object? MouseX(List<object?> _)      => (object?)(double)Raylib.GetMouseX();
    public static object? MouseY(List<object?> _)      => (object?)(double)Raylib.GetMouseY();
    public static object? MouseDeltaX(List<object?> _) => (object?)(double)Raylib.GetMouseDelta().X;
    public static object? MouseDeltaY(List<object?> _) => (object?)(double)Raylib.GetMouseDelta().Y;
    public static object? MouseDown(List<object?> a)   => (object?)Raylib.IsMouseButtonDown(ToMouseBtn(a[0]));
    public static object? MousePressed(List<object?> a)=> (object?)Raylib.IsMouseButtonPressed(ToMouseBtn(a[0]));
    public static object? MouseWheel(List<object?> _)  => (object?)(double)Raylib.GetMouseWheelMove();
    public static object? HideCursor(List<object?> _)  { Raylib.DisableCursor(); return null; }
    public static object? ShowCursor(List<object?> _)  { Raylib.EnableCursor();  return null; }

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
        return ColorList(Raylib.Fade(c, a.Count > 1 ? (float)Convert.ToDouble(a[1]) : 1f));
    }

    public static readonly Dictionary<string, object?> Colors = MakoRay.Colors
        .ToDictionary(kv => kv.Key, kv => kv.Value);

    // ── Cleanup ───────────────────────────────────────────────────────────────

    public static void UnloadAll()
    {
        foreach (var m in _models)  Raylib.UnloadModel(m);
        foreach (var t in _textures) Raylib.UnloadTexture(t);
        _models.Clear(); _textures.Clear(); _cameras.Clear();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Vector3 Vec3(List<object?> a, int off) => new(
        a.Count > off     ? (float)Convert.ToDouble(a[off])     : 0,
        a.Count > off + 1 ? (float)Convert.ToDouble(a[off + 1]) : 0,
        a.Count > off + 2 ? (float)Convert.ToDouble(a[off + 2]) : 0);

    private static List<object?> ColorList(Color c) =>
        new() { (object?)(double)c.R, (double)c.G, (double)c.B, (double)c.A };

    private static KeyboardKey ToKey(object? v)
    {
        if (v is string s)
        {
            if (s.Length == 1) return (KeyboardKey)(int)char.ToUpper(s[0]);
            return s.ToUpper() switch
            {
                "SPACE" => KeyboardKey.Space, "ENTER" => KeyboardKey.Enter,
                "ESCAPE" => KeyboardKey.Escape, "UP" => KeyboardKey.Up,
                "DOWN" => KeyboardKey.Down, "LEFT" => KeyboardKey.Left,
                "RIGHT" => KeyboardKey.Right, "SHIFT" => KeyboardKey.LeftShift,
                "CTRL" => KeyboardKey.LeftControl, "ALT" => KeyboardKey.LeftAlt,
                "W" => KeyboardKey.W, "A" => KeyboardKey.A,
                "S" => KeyboardKey.S, "D" => KeyboardKey.D,
                _ => KeyboardKey.Null,
            };
        }
        return (KeyboardKey)(int)Convert.ToDouble(v);
    }
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
        ["init"]         = Init,          ["fps"]          = SetFps,
        ["running"]      = Running,       ["begin"]        = Begin,
        ["end"]          = End,           ["close"]        = Close,
        ["delta"]        = Delta,         ["get_fps"]      = GetFps,
        ["width"]        = Width,         ["height"]       = Height,
        ["title"]        = SetTitle,      ["draw_fps"]     = DrawFps,
        ["camera"]       = MakeCamera,    ["move_camera"]  = MoveCamera,
        ["orbit_camera"] = OrbitCamera,
        ["begin_3d"]     = Begin3D,       ["end_3d"]       = End3D,
        ["cube"]         = DrawCube,      ["cube_raw"]     = DrawCubeRaw,
        ["sphere"]       = DrawSphere,    ["sphere_raw"]   = DrawSphereRaw,
        ["cylinder"]     = DrawCylinder,  ["plane"]        = DrawPlane,
        ["grid"]         = DrawGrid,      ["line3d"]       = DrawLine3D,
        ["point3d"]      = DrawPoint3D,
        ["load_model"]   = LoadModelFile, ["draw_model"]   = DrawModelHandle,
        ["sky"]          = Sky,           ["clear"]        = Clear,
        ["text"]         = DrawText,
        ["key_down"]     = KeyDown,       ["key_pressed"]  = KeyPressed,
        ["key_released"] = KeyReleased,
        ["mouse_x"]      = MouseX,        ["mouse_y"]      = MouseY,
        ["mouse_delta_x"]= MouseDeltaX,   ["mouse_delta_y"]= MouseDeltaY,
        ["mouse_down"]   = MouseDown,     ["mouse_pressed"]= MousePressed,
        ["mouse_wheel"]  = MouseWheel,
        ["hide_cursor"]  = HideCursor,    ["show_cursor"]  = ShowCursor,
        ["color"]        = MakeColor,     ["fade"]         = Fade,
    };
}

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
        // Disable raylib's built-in ESC-to-quit: scripts decide when to exit.
        // (A stray ESC event on window focus was closing windows instantly.)
        Raylib.SetExitKey(KeyboardKey.Null);
        // Grab keyboard focus — under Wayland/Hyprland a new XWayland window
        // doesn't always take focus, leaving keys going to the terminal.
        Raylib.SetWindowFocused();
        return null;
    }

    public static object? SetFps(List<object?> a)  { Raylib.SetTargetFPS(a.Count > 0 ? (int)Convert.ToDouble(a[0]) : 60); return null; }
    public static object? Running(List<object?> _)  => (object?)(bool)(!Raylib.WindowShouldClose());
    public static object? Begin(List<object?> _)    { Raylib.BeginDrawing(); return null; }
    public static object? End(List<object?> _)      { Raylib.EndDrawing();   return null; }
    public static object? Close(List<object?> _)    { if (Raylib.IsWindowReady()) Raylib.CloseWindow(); return null; }
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

    /// update_camera(handle, speed=5) — interactive camera control:
    ///   WASD / arrow keys  — fly forward/back/strafe
    ///   Q / E              — move up / down
    ///   Middle mouse drag  — orbit around target
    ///   Scroll wheel       — zoom toward/away from target
    public static object? UpdateCamera(List<object?> a)
    {
        int id      = a.Count > 0 ? (int)Convert.ToDouble(a[0]) : 0;
        if (id < 0 || id >= _cameras.Count) return null;
        float speed = a.Count > 1 ? (float)Convert.ToDouble(a[1]) : 5f;

        var cam = _cameras[id];
        float dt = Raylib.GetFrameTime();

        var forward = Vector3.Normalize(cam.Target - cam.Position);
        var right   = Vector3.Normalize(Vector3.Cross(forward, cam.Up));

        // WASD + arrows — move position and target together (fly)
        if (Raylib.IsKeyDown(KeyboardKey.W) || Raylib.IsKeyDown(KeyboardKey.Up))
            { cam.Position += forward * speed * dt; cam.Target += forward * speed * dt; }
        if (Raylib.IsKeyDown(KeyboardKey.S) || Raylib.IsKeyDown(KeyboardKey.Down))
            { cam.Position -= forward * speed * dt; cam.Target -= forward * speed * dt; }
        if (Raylib.IsKeyDown(KeyboardKey.A) || Raylib.IsKeyDown(KeyboardKey.Left))
            { cam.Position -= right * speed * dt; cam.Target -= right * speed * dt; }
        if (Raylib.IsKeyDown(KeyboardKey.D) || Raylib.IsKeyDown(KeyboardKey.Right))
            { cam.Position += right * speed * dt; cam.Target += right * speed * dt; }
        if (Raylib.IsKeyDown(KeyboardKey.Q))
            { cam.Position -= cam.Up * speed * dt; cam.Target -= cam.Up * speed * dt; }
        if (Raylib.IsKeyDown(KeyboardKey.E))
            { cam.Position += cam.Up * speed * dt; cam.Target += cam.Up * speed * dt; }

        // Middle mouse drag — orbit around target
        if (Raylib.IsMouseButtonDown(MouseButton.Middle))
        {
            var delta = Raylib.GetMouseDelta();
            var toPos = cam.Position - cam.Target;
            float dist = toPos.Length();

            if (delta.X != 0)  // yaw
                toPos = Vector3.Transform(toPos, Matrix4x4.CreateRotationY(-delta.X * 0.005f));

            if (delta.Y != 0)  // pitch, clamped so we can't flip over the pole
            {
                var axis    = Vector3.Normalize(Vector3.Cross(toPos, cam.Up));
                var rotated = Vector3.Transform(toPos,
                    Matrix4x4.CreateFromAxisAngle(axis, -delta.Y * 0.005f));
                if (Math.Abs(Vector3.Normalize(rotated).Y) < 0.98f)
                    toPos = rotated;
            }
            cam.Position = cam.Target + Vector3.Normalize(toPos) * dist;
        }

        // Scroll wheel — zoom toward/away from target
        float wheel = Raylib.GetMouseWheelMove();
        if (wheel != 0)
        {
            var toTarget = cam.Target - cam.Position;
            float dist   = toTarget.Length();
            float step   = wheel * Math.Max(dist * 0.1f, 0.1f);
            if (dist - step > 0.5f)  // don't pass through the target
                cam.Position += Vector3.Normalize(toTarget) * step;
        }

        _cameras[id] = cam;
        return null;
    }

    /// camera_pos(handle) → [x, y, z] of the camera's position
    public static object? CameraPos(List<object?> a)
    {
        int id = a.Count > 0 ? (int)Convert.ToDouble(a[0]) : 0;
        if (id < 0 || id >= _cameras.Count) return new List<object?> { 0d, 0d, 0d };
        var p = _cameras[id].Position;
        return new List<object?> { (object?)(double)p.X, (double)p.Y, (double)p.Z };
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

    /// wire_cube(x,y,z, w,h,d, color) — outline only, no filled faces.
    /// Handy for selection highlights and debug bounds (e.g. object_bounds()).
    public static object? DrawWireCube(List<object?> a)
    {
        var pos = Vec3(a, 0);
        float w = (float)Convert.ToDouble(a[3]);
        float h = (float)Convert.ToDouble(a[4]);
        float d = (float)Convert.ToDouble(a[5]);
        Raylib.DrawCubeWires(pos, w, h, d, a.Count > 6 ? MakoRay.ToColor(a[6]) : Color.White);
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
        path = MakoAssets.Resolve(path);
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

    public static object? KeyDown(List<object?> a)     => (object?)(bool)Raylib.IsKeyDown(ToKey(a[0]));
    public static object? KeyPressed(List<object?> a)  => (object?)(bool)Raylib.IsKeyPressed(ToKey(a[0]));
    public static object? KeyReleased(List<object?> a) => (object?)(bool)Raylib.IsKeyReleased(ToKey(a[0]));
    public static object? MouseX(List<object?> _)      => (object?)(double)Raylib.GetMouseX();
    public static object? MouseY(List<object?> _)      => (object?)(double)Raylib.GetMouseY();
    public static object? MouseDeltaX(List<object?> _) => (object?)(double)Raylib.GetMouseDelta().X;
    public static object? MouseDeltaY(List<object?> _) => (object?)(double)Raylib.GetMouseDelta().Y;
    public static object? MouseDown(List<object?> a)   => (object?)(bool)Raylib.IsMouseButtonDown(ToMouseBtn(a[0]));
    public static object? MousePressed(List<object?> a)=> (object?)(bool)Raylib.IsMouseButtonPressed(ToMouseBtn(a[0]));
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

    // ── Scene / objects ───────────────────────────────────────────────────────
    //
    // A small retained-mode layer over the immediate-mode draw calls above:
    // spawn_*() registers an object once; draw_scene() draws everything that's
    // still registered, every frame, so you stop hand-writing a cube() call
    // per object per frame. Objects are plain data (position, scale, Y-axis
    // rotation, color, visibility) — set_object_*() mutates them in place.

    private enum ObjShape { Cube, Sphere, Cylinder, Plane }

    private sealed class SceneObject
    {
        public ObjShape Shape;
        public Vector3  Pos;
        // Meaning of Scale depends on Shape:
        //   Cube:     X,Y,Z = width, height, depth
        //   Sphere:   X     = radius
        //   Cylinder: X,Y,Z = radius_top, radius_bottom, height
        //   Plane:    X,Z   = width, depth
        public Vector3 Scale = Vector3.One;
        public float   RotationY;   // degrees — Y-axis only, for v1
        public Color   Tint = Color.White;
        public bool    Visible = true;
        public bool    Wires = true;
    }

    private static readonly List<SceneObject?> _objects = [];

    public static object? SpawnCube(List<object?> a)
    {
        var obj = new SceneObject
        {
            Shape = ObjShape.Cube,
            Pos   = Vec3(a, 0),
            Scale = new Vector3(a.Count > 3 ? (float)Convert.ToDouble(a[3]) : 1,
                                 a.Count > 4 ? (float)Convert.ToDouble(a[4]) : 1,
                                 a.Count > 5 ? (float)Convert.ToDouble(a[5]) : 1),
            Tint  = a.Count > 6 ? MakoRay.ToColor(a[6]) : Color.White,
        };
        _objects.Add(obj);
        return (object?)(double)(_objects.Count - 1);
    }

    public static object? SpawnSphere(List<object?> a)
    {
        var obj = new SceneObject
        {
            Shape = ObjShape.Sphere,
            Pos   = Vec3(a, 0),
            Scale = new Vector3(a.Count > 3 ? (float)Convert.ToDouble(a[3]) : 1, 0, 0),
            Tint  = a.Count > 4 ? MakoRay.ToColor(a[4]) : Color.White,
        };
        _objects.Add(obj);
        return (object?)(double)(_objects.Count - 1);
    }

    public static object? SpawnCylinder(List<object?> a)
    {
        var obj = new SceneObject
        {
            Shape = ObjShape.Cylinder,
            Pos   = Vec3(a, 0),
            Scale = new Vector3(a.Count > 3 ? (float)Convert.ToDouble(a[3]) : 1,
                                 a.Count > 4 ? (float)Convert.ToDouble(a[4]) : 1,
                                 a.Count > 5 ? (float)Convert.ToDouble(a[5]) : 2),
            Tint  = a.Count > 6 ? MakoRay.ToColor(a[6]) : Color.White,
        };
        _objects.Add(obj);
        return (object?)(double)(_objects.Count - 1);
    }

    public static object? SpawnPlane(List<object?> a)
    {
        var obj = new SceneObject
        {
            Shape = ObjShape.Plane,
            Pos   = Vec3(a, 0),
            Scale = new Vector3(a.Count > 3 ? (float)Convert.ToDouble(a[3]) : 10, 0,
                                 a.Count > 4 ? (float)Convert.ToDouble(a[4]) : 10),
            Tint  = a.Count > 5 ? MakoRay.ToColor(a[5]) : Color.White,
        };
        _objects.Add(obj);
        return (object?)(double)(_objects.Count - 1);
    }

    private static SceneObject? GetObj(List<object?> a, int i = 0)
    {
        int id = a.Count > i ? (int)Convert.ToDouble(a[i]) : -1;
        return id >= 0 && id < _objects.Count ? _objects[id] : null;
    }

    public static object? SetObjectPos(List<object?> a)
    {
        var o = GetObj(a); if (o is null) return null;
        o.Pos = Vec3(a, 1);
        return null;
    }

    public static object? SetObjectColor(List<object?> a)
    {
        var o = GetObj(a); if (o is null) return null;
        if (a.Count > 1) o.Tint = MakoRay.ToColor(a[1]);
        return null;
    }

    public static object? SetObjectScale(List<object?> a)
    {
        var o = GetObj(a); if (o is null) return null;
        o.Scale = Vec3(a, 1);
        return null;
    }

    /// set_object_rotation(handle, degrees) — rotation around the Y axis only.
    public static object? SetObjectRotation(List<object?> a)
    {
        var o = GetObj(a); if (o is null) return null;
        if (a.Count > 1) o.RotationY = (float)Convert.ToDouble(a[1]);
        return null;
    }

    private static string ShapeName(ObjShape s) => s switch
    {
        ObjShape.Cube => "cube", ObjShape.Sphere => "sphere",
        ObjShape.Cylinder => "cylinder", ObjShape.Plane => "plane", _ => "cube",
    };

    /// object_info(handle) → dict with the object's current shape, position,
    /// scale, rotation, color, visibility, and wireframe flag — the read side
    /// to go with set_object_*(), e.g. for populating an editor/inspector
    /// panel with a selected object's live values.
    public static object? ObjectInfo(List<object?> a)
    {
        var o = GetObj(a);
        if (o is null) return null;
        return new Dictionary<string, object?>
        {
            ["shape"]    = ShapeName(o.Shape),
            ["x"] = (double)o.Pos.X, ["y"] = (double)o.Pos.Y, ["z"] = (double)o.Pos.Z,
            ["sx"] = (double)o.Scale.X, ["sy"] = (double)o.Scale.Y, ["sz"] = (double)o.Scale.Z,
            ["rotation"] = (double)o.RotationY,
            ["color"]    = ColorList(o.Tint),
            ["visible"]  = o.Visible,
            ["wires"]    = o.Wires,
        };
    }

    public static object? SetObjectVisible(List<object?> a)
    {
        var o = GetObj(a); if (o is null) return null;
        if (a.Count > 1) o.Visible = a[1] switch { bool b => b, double d => d != 0, _ => true };
        return null;
    }

    public static object? SetObjectWires(List<object?> a)
    {
        var o = GetObj(a); if (o is null) return null;
        if (a.Count > 1) o.Wires = a[1] switch { bool b => b, double d => d != 0, _ => true };
        return null;
    }

    public static object? RemoveObject(List<object?> a)
    {
        int id = a.Count > 0 ? (int)Convert.ToDouble(a[0]) : -1;
        if (id >= 0 && id < _objects.Count) _objects[id] = null;
        return null;
    }

    public static object? ClearObjects(List<object?> _)
    {
        _objects.Clear();
        return null;
    }

    public static object? ObjectCount(List<object?> _) =>
        (object?)(double)_objects.Count(o => o != null);

    /// object_bounds(handle) → [min_x,min_y,min_z, max_x,max_y,max_z] — an
    /// axis-aligned box around the object, ignoring rotation. Feed into
    /// box3d_overlap()/dist() for simple 3D collision against other objects.
    private static Vector3 ObjectHalfExtent(SceneObject o) => o.Shape switch
    {
        ObjShape.Cube     => o.Scale / 2f,
        ObjShape.Sphere   => new Vector3(o.Scale.X, o.Scale.X, o.Scale.X),
        ObjShape.Cylinder => new Vector3(Math.Max(o.Scale.X, o.Scale.Y), o.Scale.Z / 2f, Math.Max(o.Scale.X, o.Scale.Y)),
        ObjShape.Plane    => new Vector3(o.Scale.X / 2f, 0.01f, o.Scale.Z / 2f),
        _                 => Vector3.One,
    };

    public static object? ObjectBounds(List<object?> a)
    {
        var o = GetObj(a);
        if (o is null) return null;
        var half = ObjectHalfExtent(o);
        var min = o.Pos - half;
        var max = o.Pos + half;
        return new List<object?>
        {
            (object?)(double)min.X, (double)min.Y, (double)min.Z,
            (double)max.X, (double)max.Y, (double)max.Z,
        };
    }

    /// draw_scene() — call between begin_3d()/end_3d(). Draws every object
    /// spawned with spawn_cube/spawn_sphere/spawn_cylinder/spawn_plane at its
    /// current position/scale/rotation/color.
    public static object? DrawScene(List<object?> _)
    {
        foreach (var o in _objects)
        {
            if (o is null || !o.Visible) continue;

            bool rotated = Math.Abs(o.RotationY) > 0.001f;
            if (rotated)
            {
                Rlgl.PushMatrix();
                Rlgl.Translatef(o.Pos.X, o.Pos.Y, o.Pos.Z);
                Rlgl.Rotatef(o.RotationY, 0, 1, 0);
            }
            var pos = rotated ? Vector3.Zero : o.Pos;

            switch (o.Shape)
            {
                case ObjShape.Cube:
                    Raylib.DrawCube(pos, o.Scale.X, o.Scale.Y, o.Scale.Z, o.Tint);
                    if (o.Wires) Raylib.DrawCubeWires(pos, o.Scale.X, o.Scale.Y, o.Scale.Z, Color.Black);
                    break;
                case ObjShape.Sphere:
                    Raylib.DrawSphere(pos, o.Scale.X, o.Tint);
                    if (o.Wires) Raylib.DrawSphereWires(pos, o.Scale.X, 8, 8, Color.Black);
                    break;
                case ObjShape.Cylinder:
                    Raylib.DrawCylinder(pos, o.Scale.X, o.Scale.Y, o.Scale.Z, 16, o.Tint);
                    if (o.Wires) Raylib.DrawCylinderWires(pos, o.Scale.X, o.Scale.Y, o.Scale.Z, 16, Color.Black);
                    break;
                case ObjShape.Plane:
                    Raylib.DrawPlane(pos, new Vector2(o.Scale.X, o.Scale.Z), o.Tint);
                    break;
            }

            if (rotated) Rlgl.PopMatrix();
        }
        return null;
    }

    /// pick_object(cam, [screen_x, screen_y]) → handle of the closest
    /// visible spawned object under the given screen point (defaults to the
    /// current mouse position), or none if nothing was hit. Click-to-select,
    /// for editors/toolbars — ray/AABB test against object_bounds(), ignoring
    /// rotation (same as object_bounds() itself).
    public static object? PickObject(List<object?> a)
    {
        int camId = a.Count > 0 ? (int)Convert.ToDouble(a[0]) : 0;
        if (camId < 0 || camId >= _cameras.Count) return null;

        var screenPos = a.Count > 2
            ? new Vector2((float)Convert.ToDouble(a[1]), (float)Convert.ToDouble(a[2]))
            : Raylib.GetMousePosition();

        var ray = Raylib.GetScreenToWorldRay(screenPos, _cameras[camId]);

        int best = -1;
        float bestDist = float.MaxValue;
        for (int i = 0; i < _objects.Count; i++)
        {
            var o = _objects[i];
            if (o is null || !o.Visible) continue;
            var half = ObjectHalfExtent(o);
            var box = new BoundingBox(o.Pos - half, o.Pos + half);
            var hit = Raylib.GetRayCollisionBox(ray, box);
            if (hit.Hit && hit.Distance < bestDist)
            {
                bestDist = hit.Distance;
                best = i;
            }
        }
        return best >= 0 ? (object?)(double)best : null;
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    public static void UnloadAll()
    {
        // GPU unloads need a live GL context; skip them if the window is gone.
        if (Raylib.IsWindowReady())
        {
            foreach (var m in _models)  Raylib.UnloadModel(m);
            foreach (var t in _textures) Raylib.UnloadTexture(t);
        }
        _models.Clear(); _textures.Clear(); _cameras.Clear(); _objects.Clear();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static Vector3 Vec3(List<object?> a, int off) => new(
        a.Count > off     ? (float)Convert.ToDouble(a[off])     : 0,
        a.Count > off + 1 ? (float)Convert.ToDouble(a[off + 1]) : 0,
        a.Count > off + 2 ? (float)Convert.ToDouble(a[off + 2]) : 0);

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
        ["init"]         = Init,          ["fps"]          = SetFps,
        ["running"]      = Running,       ["begin"]        = Begin,
        ["end"]          = End,           ["close"]        = Close,
        ["delta"]        = Delta,         ["get_fps"]      = GetFps,
        ["width"]        = Width,         ["height"]       = Height,
        ["title"]        = SetTitle,      ["draw_fps"]     = DrawFps,
        ["camera"]       = MakeCamera,    ["move_camera"]  = MoveCamera,
        ["orbit_camera"] = OrbitCamera,  ["update_camera"]= UpdateCamera,
        ["camera_pos"]   = CameraPos,
        ["begin_3d"]     = Begin3D,       ["end_3d"]       = End3D,
        ["cube"]         = DrawCube,      ["cube_raw"]     = DrawCubeRaw,
        ["wire_cube"]    = DrawWireCube,
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
        // Scene / objects
        ["spawn_cube"]         = SpawnCube,
        ["spawn_sphere"]       = SpawnSphere,
        ["spawn_cylinder"]     = SpawnCylinder,
        ["spawn_plane"]        = SpawnPlane,
        ["set_object_pos"]     = SetObjectPos,
        ["set_object_color"]   = SetObjectColor,
        ["set_object_scale"]   = SetObjectScale,
        ["set_object_rotation"]= SetObjectRotation,
        ["set_object_visible"] = SetObjectVisible,
        ["set_object_wires"]   = SetObjectWires,
        ["remove_object"]      = RemoveObject,
        ["clear_objects"]      = ClearObjects,
        ["object_count"]       = ObjectCount,
        ["object_bounds"]      = ObjectBounds,
        ["object_info"]        = ObjectInfo,
        ["draw_scene"]         = DrawScene,
        ["pick_object"]        = PickObject,
    };
}

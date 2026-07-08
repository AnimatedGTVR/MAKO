using System.Numerics;

namespace Mako;

/// Hanami — MAKO's lighting engine. Headless (no window of its own): compute
/// lighting for any point in your Mako3D/Mako2D scene and tint your own draw
/// calls with the result. Pairs with a MakoUI-based Lighting Manager tool
/// for visually placing lights and baking, saved to a JSON config any game
/// script can load.
///
///   using Hanami;
///   using Mako3D;
///
///   main() {
///       Mako3D.init(800, 600, "Lit scene");
///       Hanami.set_mode("realtime");
///       Hanami.set_ambient(0.08, 0.08, 0.12, 1);
///       light = Hanami.add_light("point", 0, 5, 0,  1, 0.9, 0.7,  2.0, 12);
///
///       while Mako3D.running() {
///           c = Hanami.light_at(0, 1, 0);
///           col = Hanami.shade(Mako3D.RED, c);
///           # ... Mako3D.cube(0, 1, 0, 2,2,2, col) between begin_3d/end_3d ...
///       }
///   }
///
/// Modes:
///   unlit     — no lighting; light_at() always returns full white
///   baked     — precomputed static lighting from bake(); fast, non-moving
///   realtime  — every light recomputed every frame; supports moving lights
///   mixed     — baked probes for static lights + realtime for the rest
///   voxel     — grid-based light propagation for block/voxel worlds
static class Hanami
{
    private sealed class HLight
    {
        public string Type = "point";      // "point" | "directional"
        public Vector3 Pos;
        public Vector3 Dir = new(0, -1, 0);
        public Vector3 Color = Vector3.One;
        public float Intensity = 1f;
        public float Range = 10f;
        public bool IsStatic;
        public bool Enabled = true;
    }

    private sealed class Occluder { public Vector3 Min, Max; }

    // ── State ─────────────────────────────────────────────────────────────────

    private static readonly List<HLight?>   _lights    = [];
    private static readonly List<Occluder?> _occluders = [];
    private static Vector3 _ambient = new(0.05f, 0.05f, 0.07f);
    private static float   _ambientIntensity = 1f;
    private static string  _mode = "realtime";

    // Baked probe grid
    private static readonly Dictionary<(int, int, int), Vector3> _probes = [];
    private static Vector3 _bakeMin, _bakeMax;
    private static int     _bakeRes = 6;
    private static bool    _baked;

    // Voxel grid
    private static int _vsx, _vsy, _vsz;
    private static bool[,,]? _voxelSolid;
    private static int[,,]?  _voxelEmissive;
    private static int[,,]?  _voxelLight;

    // ── Mode & ambient ───────────────────────────────────────────────────────

    public static object? SetMode(List<object?> a)
    {
        var m = a.Count > 0 ? a[0]?.ToString()?.ToLower() ?? "realtime" : "realtime";
        if (m is not ("unlit" or "baked" or "realtime" or "mixed" or "voxel"))
            throw new MakoError($"Hanami.set_mode(): unknown mode '{m}' — expected unlit, baked, realtime, mixed, or voxel");
        _mode = m;
        return null;
    }

    public static object? GetMode(List<object?> _) => (object?)_mode;

    /// set_ambient(r, g, b, intensity=1) — base light applied everywhere, even in shadow.
    public static object? SetAmbient(List<object?> a)
    {
        _ambient = new Vector3(F(a, 0), F(a, 1), F(a, 2));
        _ambientIntensity = a.Count > 3 ? F(a, 3) : 1f;
        return null;
    }

    // ── Lights ────────────────────────────────────────────────────────────────

    /// add_light(type, x, y, z, r, g, b, intensity=1, range=10, is_static=false) → handle
    /// type: "point" or "directional" (for directional, x,y,z is the direction, not position)
    public static object? AddLight(List<object?> a)
    {
        string type = a.Count > 0 ? a[0]?.ToString()?.ToLower() ?? "point" : "point";
        var light = new HLight
        {
            Type      = type is "directional" ? "directional" : "point",
            Color     = new Vector3(F(a, 4), F(a, 5), F(a, 6)),
            Intensity = a.Count > 7 ? F(a, 7) : 1f,
            Range     = a.Count > 8 ? F(a, 8) : 10f,
            IsStatic  = a.Count > 9 && Truthy(a[9]),
        };
        if (light.Type == "directional")
            light.Dir = Vector3.Normalize(new Vector3(F(a, 1), F(a, 2), F(a, 3)) is { } d && d != Vector3.Zero ? d : new Vector3(0, -1, 0));
        else
            light.Pos = new Vector3(F(a, 1), F(a, 2), F(a, 3));

        _lights.Add(light);
        return (object?)(double)(_lights.Count - 1);
    }

    public static object? RemoveLight(List<object?> a)
    {
        int id = Idx(a, 0);
        if (id >= 0 && id < _lights.Count) _lights[id] = null;
        return null;
    }

    public static object? SetLightPos(List<object?> a)
    {
        var l = Get(_lights, Idx(a, 0)); if (l is null) return null;
        l.Pos = new Vector3(F(a, 1), F(a, 2), F(a, 3));
        return null;
    }

    public static object? SetLightColor(List<object?> a)
    {
        var l = Get(_lights, Idx(a, 0)); if (l is null) return null;
        l.Color = new Vector3(F(a, 1), F(a, 2), F(a, 3));
        return null;
    }

    public static object? SetLightIntensity(List<object?> a)
    {
        var l = Get(_lights, Idx(a, 0)); if (l is null) return null;
        l.Intensity = F(a, 1);
        return null;
    }

    public static object? SetLightRange(List<object?> a)
    {
        var l = Get(_lights, Idx(a, 0)); if (l is null) return null;
        l.Range = F(a, 1);
        return null;
    }

    public static object? SetLightEnabled(List<object?> a)
    {
        var l = Get(_lights, Idx(a, 0)); if (l is null) return null;
        l.Enabled = a.Count > 1 && Truthy(a[1]);
        return null;
    }

    public static object? LightCount(List<object?> _) => (object?)(double)_lights.Count(l => l != null);

    /// light_info(handle) → dict of the light's current fields, or none if removed.
    public static object? LightInfo(List<object?> a)
    {
        var l = Get(_lights, Idx(a, 0)); if (l is null) return null;
        return new Dictionary<string, object?>
        {
            ["type"]      = l.Type,
            ["x"]         = (double)l.Pos.X, ["y"] = (double)l.Pos.Y, ["z"] = (double)l.Pos.Z,
            ["r"]         = (double)l.Color.X, ["g"] = (double)l.Color.Y, ["b"] = (double)l.Color.Z,
            ["intensity"] = (double)l.Intensity,
            ["range"]     = (double)l.Range,
            ["is_static"] = l.IsStatic,
            ["enabled"]   = l.Enabled,
        };
    }

    // ── Occluders (shadow casters — simple AABBs) ────────────────────────────

    /// add_occluder(x, y, z, w, h, d) → handle — an axis-aligned box that blocks light rays.
    public static object? AddOccluder(List<object?> a)
    {
        var pos  = new Vector3(F(a, 0), F(a, 1), F(a, 2));
        var half = new Vector3(F(a, 3), F(a, 4), F(a, 5)) / 2f;
        _occluders.Add(new Occluder { Min = pos - half, Max = pos + half });
        return (object?)(double)(_occluders.Count - 1);
    }

    public static object? RemoveOccluder(List<object?> a)
    {
        int id = Idx(a, 0);
        if (id >= 0 && id < _occluders.Count) _occluders[id] = null;
        return null;
    }

    public static object? ClearOccluders(List<object?> _) { _occluders.Clear(); return null; }
    public static object? ClearLights(List<object?> _)    { _lights.Clear();    return null; }

    // ── Lighting evaluation ───────────────────────────────────────────────────

    /// light_at(x, y, z) → [r, g, b] — evaluate lighting for a world point
    /// under the current mode. Multiply against your object's base color
    /// (or use Hanami.shade) before drawing.
    public static object? LightAt(List<object?> a)
    {
        var pos = new Vector3(F(a, 0), F(a, 1), F(a, 2));
        var result = _mode switch
        {
            "unlit"    => Vector3.One,
            "baked"    => LookupBaked(pos),
            "mixed"    => LookupBaked(pos) + ComputeRealtime(pos, onlyNonStatic: true),
            "voxel"    => _ambient * _ambientIntensity,   // use voxel_light()/voxel_color() for voxel scenes
            _          => ComputeRealtime(pos, onlyNonStatic: false),   // realtime
        };
        result = Vector3.Clamp(result, Vector3.Zero, new Vector3(4f));
        return new List<object?> { (object?)(double)result.X, (double)result.Y, (double)result.Z };
    }

    /// shade(base_color, light_rgb) → a Mako2D/Mako3D-ready [r,g,b,a] color list.
    /// base_color is a normal 0-255 color list; light_rgb is what light_at() returned.
    public static object? Shade(List<object?> a)
    {
        if (a.Count < 2 || a[0] is not List<object?> baseCol || a[1] is not List<object?> light)
            throw new MakoError("Hanami.shade() expects (base_color, light_rgb)");
        double r = D(baseCol, 0) * D(light, 0);
        double g = D(baseCol, 1) * D(light, 1);
        double b = D(baseCol, 2) * D(light, 2);
        double al = baseCol.Count > 3 ? D(baseCol, 3) : 255;
        return new List<object?>
        {
            (object?)Math.Clamp(r, 0, 255), Math.Clamp(g, 0, 255), Math.Clamp(b, 0, 255), al,
        };
    }

    private static Vector3 ComputeRealtime(Vector3 pos, bool onlyNonStatic)
    {
        var sum = _ambient * _ambientIntensity;
        foreach (var light in _lights)
        {
            if (light is null || !light.Enabled) continue;
            if (onlyNonStatic && light.IsStatic) continue;

            Vector3 toLight;
            float atten;
            if (light.Type == "directional")
            {
                toLight = -light.Dir;
                atten = 1f;
            }
            else
            {
                var delta = light.Pos - pos;
                float dist = delta.Length();
                if (dist > light.Range) continue;
                toLight = dist > 0.0001f ? delta / dist : Vector3.UnitY;
                float t = Math.Clamp(1f - dist / light.Range, 0f, 1f);
                atten = t * t;
            }

            var lightPoint = light.Type == "directional" ? pos + toLight * 1000f : light.Pos;
            if (IsOccluded(pos, lightPoint)) continue;

            sum += light.Color * light.Intensity * atten;
        }
        return sum;
    }

    private static bool IsOccluded(Vector3 from, Vector3 to)
    {
        var dir = to - from;
        float maxT = dir.Length();
        if (maxT < 0.0001f) return false;
        dir /= maxT;

        foreach (var occ in _occluders)
        {
            if (occ is null) continue;
            if (RayAabb(from, dir, occ.Min, occ.Max, out float t) && t > 0.01f && t < maxT - 0.01f)
                return true;
        }
        return false;
    }

    /// Slab-method ray/AABB intersection. Returns the entry distance in t.
    private static bool RayAabb(Vector3 origin, Vector3 dir, Vector3 min, Vector3 max, out float t)
    {
        float tMin = 0f, tMax = float.MaxValue;
        for (int i = 0; i < 3; i++)
        {
            float o = i == 0 ? origin.X : i == 1 ? origin.Y : origin.Z;
            float d = i == 0 ? dir.X    : i == 1 ? dir.Y    : dir.Z;
            float lo = i == 0 ? min.X   : i == 1 ? min.Y   : min.Z;
            float hi = i == 0 ? max.X   : i == 1 ? max.Y   : max.Z;

            if (Math.Abs(d) < 1e-8f)
            {
                if (o < lo || o > hi) { t = 0; return false; }
                continue;
            }
            float t1 = (lo - o) / d, t2 = (hi - o) / d;
            if (t1 > t2) (t1, t2) = (t2, t1);
            tMin = Math.Max(tMin, t1);
            tMax = Math.Min(tMax, t2);
            if (tMin > tMax) { t = 0; return false; }
        }
        t = tMin;
        return true;
    }

    // ── Baking ────────────────────────────────────────────────────────────────

    /// bake(min_x,min_y,min_z, max_x,max_y,max_z, resolution=6) — precompute a
    /// grid of light probes across a bounding box from all static lights.
    public static object? Bake(List<object?> a)
    {
        _bakeMin = new Vector3(F(a, 0), F(a, 1), F(a, 2));
        _bakeMax = new Vector3(F(a, 3), F(a, 4), F(a, 5));
        _bakeRes = a.Count > 6 ? Math.Max(2, (int)D(a, 6)) : 6;
        _probes.Clear();

        for (int xi = 0; xi < _bakeRes; xi++)
        for (int yi = 0; yi < _bakeRes; yi++)
        for (int zi = 0; zi < _bakeRes; zi++)
        {
            var pos = ProbePos(xi, yi, zi);
            var lit = ComputeRealtime(pos, onlyNonStatic: false);
            // Only static lights count toward the bake — non-static ones are
            // re-added live each frame in "mixed" mode, so excluding them here
            // avoids double-counting.
            var staticOnly = _ambient * _ambientIntensity;
            foreach (var light in _lights)
            {
                if (light is null || !light.Enabled || !light.IsStatic) continue;
                staticOnly += ContributionOf(light, pos);
            }
            _probes[(xi, yi, zi)] = staticOnly;
        }
        _baked = true;
        return null;
    }

    private static Vector3 ContributionOf(object lightObj, Vector3 pos)
    {
        // Re-run a single light's contribution the same way ComputeRealtime does.
        var light = (HLight)lightObj;
        Vector3 toLight; float atten;
        if (light.Type == "directional") { toLight = -light.Dir; atten = 1f; }
        else
        {
            var delta = light.Pos - pos;
            float dist = delta.Length();
            if (dist > light.Range) return Vector3.Zero;
            toLight = dist > 0.0001f ? delta / dist : Vector3.UnitY;
            float t = Math.Clamp(1f - dist / light.Range, 0f, 1f);
            atten = t * t;
        }
        var lightPoint = light.Type == "directional" ? pos + toLight * 1000f : light.Pos;
        if (IsOccluded(pos, lightPoint)) return Vector3.Zero;
        return light.Color * light.Intensity * atten;
    }

    private static Vector3 ProbePos(int xi, int yi, int zi)
    {
        var size = _bakeMax - _bakeMin;
        var step = new Vector3(
            _bakeRes > 1 ? size.X / (_bakeRes - 1) : 0,
            _bakeRes > 1 ? size.Y / (_bakeRes - 1) : 0,
            _bakeRes > 1 ? size.Z / (_bakeRes - 1) : 0);
        return _bakeMin + new Vector3(step.X * xi, step.Y * yi, step.Z * zi);
    }

    private static Vector3 LookupBaked(Vector3 pos)
    {
        if (!_baked) return _ambient * _ambientIntensity;
        var size = _bakeMax - _bakeMin;
        int Clamp01(float v, float sizeAxis) => sizeAxis < 1e-6f
            ? 0
            : Math.Clamp((int)Math.Round((v) / sizeAxis * (_bakeRes - 1)), 0, _bakeRes - 1);

        var rel = pos - _bakeMin;
        int xi = Clamp01(rel.X, size.X), yi = Clamp01(rel.Y, size.Y), zi = Clamp01(rel.Z, size.Z);
        return _probes.TryGetValue((xi, yi, zi), out var v) ? v : _ambient * _ambientIntensity;
    }

    public static object? IsBaked(List<object?> _) => (object?)_baked;

    // ── Voxel lighting ────────────────────────────────────────────────────────

    /// voxel_init(size_x, size_y, size_z) — allocate a fresh voxel grid.
    public static object? VoxelInit(List<object?> a)
    {
        _vsx = (int)D(a, 0); _vsy = (int)D(a, 1); _vsz = (int)D(a, 2);
        _voxelSolid    = new bool[_vsx, _vsy, _vsz];
        _voxelEmissive = new int[_vsx, _vsy, _vsz];
        _voxelLight    = new int[_vsx, _vsy, _vsz];
        return null;
    }

    public static object? VoxelSetSolid(List<object?> a)
    {
        if (_voxelSolid is null) return null;
        var (x, y, z) = (Ix(a, 0), Iy(a, 1), Iz(a, 2));
        if (InBounds(x, y, z)) _voxelSolid[x, y, z] = a.Count > 3 && Truthy(a[3]);
        return null;
    }

    /// voxel_set_emissive(x, y, z, level 0-15) — a light-emitting block (e.g. a torch, lava, lamp).
    public static object? VoxelSetEmissive(List<object?> a)
    {
        if (_voxelEmissive is null) return null;
        var (x, y, z) = (Ix(a, 0), Iy(a, 1), Iz(a, 2));
        if (InBounds(x, y, z)) _voxelEmissive[x, y, z] = Math.Clamp((int)D(a, 3), 0, 15);
        return null;
    }

    /// voxel_bake() — propagate light from emissive blocks through open space
    /// (Minecraft-style multi-source BFS, attenuating by 1 per step).
    public static object? VoxelBake(List<object?> _)
    {
        if (_voxelSolid is null || _voxelEmissive is null || _voxelLight is null) return null;

        Array.Clear(_voxelLight);
        var queue = new Queue<(int x, int y, int z)>();

        for (int x = 0; x < _vsx; x++)
        for (int y = 0; y < _vsy; y++)
        for (int z = 0; z < _vsz; z++)
        {
            if (_voxelEmissive[x, y, z] <= 0) continue;
            _voxelLight[x, y, z] = _voxelEmissive[x, y, z];
            queue.Enqueue((x, y, z));
        }

        Span<(int dx, int dy, int dz)> dirs = stackalloc[]
        { (1,0,0), (-1,0,0), (0,1,0), (0,-1,0), (0,0,1), (0,0,-1) };

        while (queue.Count > 0)
        {
            var (x, y, z) = queue.Dequeue();
            int lvl = _voxelLight[x, y, z];
            if (lvl <= 1) continue;

            foreach (var (dx, dy, dz) in dirs)
            {
                int nx = x + dx, ny = y + dy, nz = z + dz;
                if (!InBounds(nx, ny, nz) || _voxelSolid[nx, ny, nz]) continue;
                if (_voxelLight[nx, ny, nz] >= lvl - 1) continue;
                _voxelLight[nx, ny, nz] = lvl - 1;
                queue.Enqueue((nx, ny, nz));
            }
        }
        return null;
    }

    /// voxel_light(x, y, z) → 0-15 light level (0 for solid or out-of-bounds cells).
    public static object? VoxelLight(List<object?> a)
    {
        if (_voxelLight is null) return 0d;
        var (x, y, z) = (Ix(a, 0), Iy(a, 1), Iz(a, 2));
        if (!InBounds(x, y, z)) return 0d;
        return (object?)(double)_voxelLight[x, y, z];
    }

    /// voxel_color(x, y, z, base_color) → base_color scaled by light level / 15,
    /// plus the current ambient — ready to pass straight into Mako3D.cube(..., color).
    public static object? VoxelColor(List<object?> a)
    {
        var (x, y, z) = (Ix(a, 0), Iy(a, 1), Iz(a, 2));
        int lvl = InBounds(x, y, z) && _voxelLight != null ? _voxelLight[x, y, z] : 0;
        float t = Math.Clamp(lvl / 15f + _ambientIntensity * 0.15f, 0f, 1f);

        if (a.Count > 3 && a[3] is List<object?> baseCol)
            return new List<object?>
            {
                (object?)Math.Clamp(D(baseCol, 0) * t, 0, 255),
                Math.Clamp(D(baseCol, 1) * t, 0, 255),
                Math.Clamp(D(baseCol, 2) * t, 0, 255),
                baseCol.Count > 3 ? D(baseCol, 3) : 255,
            };
        double v = t * 255;
        return new List<object?> { (object?)v, v, v, 255d };
    }

    public static object? VoxelSolid(List<object?> a)
    {
        var (x, y, z) = (Ix(a, 0), Iy(a, 1), Iz(a, 2));
        return (object?)(InBounds(x, y, z) && _voxelSolid != null && _voxelSolid[x, y, z]);
    }

    private static bool InBounds(int x, int y, int z) =>
        _voxelSolid != null && x >= 0 && x < _vsx && y >= 0 && y < _vsy && z >= 0 && z < _vsz;

    // ── Config save / load (bridges the MakoUI Lighting Manager <-> a game) ──

    /// save_config(path) — serialize mode, ambient, lights, and occluders to JSON.
    public static object? SaveConfig(List<object?> a)
    {
        string path = a.Count > 0 ? a[0]?.ToString() ?? "hanami_config.json" : "hanami_config.json";
        var lights = new List<object?>();
        foreach (var l in _lights)
        {
            if (l is null) continue;
            lights.Add(new Dictionary<string, object?>
            {
                ["type"] = l.Type,
                ["x"] = (double)l.Pos.X, ["y"] = (double)l.Pos.Y, ["z"] = (double)l.Pos.Z,
                ["dx"] = (double)l.Dir.X, ["dy"] = (double)l.Dir.Y, ["dz"] = (double)l.Dir.Z,
                ["r"] = (double)l.Color.X, ["g"] = (double)l.Color.Y, ["b"] = (double)l.Color.Z,
                ["intensity"] = (double)l.Intensity,
                ["range"] = (double)l.Range,
                ["is_static"] = l.IsStatic,
                ["enabled"] = l.Enabled,
            });
        }
        var occluders = new List<object?>();
        foreach (var o in _occluders)
        {
            if (o is null) continue;
            occluders.Add(new Dictionary<string, object?>
            {
                ["min_x"] = (double)o.Min.X, ["min_y"] = (double)o.Min.Y, ["min_z"] = (double)o.Min.Z,
                ["max_x"] = (double)o.Max.X, ["max_y"] = (double)o.Max.Y, ["max_z"] = (double)o.Max.Z,
            });
        }
        var config = new Dictionary<string, object?>
        {
            ["mode"]              = _mode,
            ["ambient_r"]         = (double)_ambient.X,
            ["ambient_g"]         = (double)_ambient.Y,
            ["ambient_b"]         = (double)_ambient.Z,
            ["ambient_intensity"] = (double)_ambientIntensity,
            ["lights"]            = lights,
            ["occluders"]         = occluders,
        };
        File.WriteAllText(path, Json.Encode(config));
        return null;
    }

    /// load_config(path) — replace current lights/occluders/ambient/mode from a saved config.
    public static object? LoadConfig(List<object?> a)
    {
        string path = a.Count > 0 ? a[0]?.ToString() ?? "" : "";
        if (!File.Exists(path)) throw new MakoError($"Hanami.load_config(): file not found: '{path}'");

        if (Json.Decode(File.ReadAllText(path)) is not Dictionary<string, object?> config)
            throw new MakoError("Hanami.load_config(): malformed config (expected a JSON object)");

        _mode = config.GetValueOrDefault("mode") as string ?? "realtime";
        _ambient = new Vector3(
            (float)ToD(config.GetValueOrDefault("ambient_r")),
            (float)ToD(config.GetValueOrDefault("ambient_g")),
            (float)ToD(config.GetValueOrDefault("ambient_b")));
        _ambientIntensity = (float)ToD(config.GetValueOrDefault("ambient_intensity", 1.0));

        _lights.Clear();
        if (config.GetValueOrDefault("lights") is List<object?> lightList)
            foreach (var item in lightList)
                if (item is Dictionary<string, object?> ld)
                    _lights.Add(new HLight
                    {
                        Type      = ld.GetValueOrDefault("type") as string ?? "point",
                        Pos       = new Vector3((float)ToD(ld.GetValueOrDefault("x")), (float)ToD(ld.GetValueOrDefault("y")), (float)ToD(ld.GetValueOrDefault("z"))),
                        Dir       = new Vector3((float)ToD(ld.GetValueOrDefault("dx")), (float)ToD(ld.GetValueOrDefault("dy", -1.0)), (float)ToD(ld.GetValueOrDefault("dz"))),
                        Color     = new Vector3((float)ToD(ld.GetValueOrDefault("r", 1.0)), (float)ToD(ld.GetValueOrDefault("g", 1.0)), (float)ToD(ld.GetValueOrDefault("b", 1.0))),
                        Intensity = (float)ToD(ld.GetValueOrDefault("intensity", 1.0)),
                        Range     = (float)ToD(ld.GetValueOrDefault("range", 10.0)),
                        IsStatic  = ld.GetValueOrDefault("is_static") is true,
                        Enabled   = ld.GetValueOrDefault("enabled") is not false,
                    });

        _occluders.Clear();
        if (config.GetValueOrDefault("occluders") is List<object?> occList)
            foreach (var item in occList)
                if (item is Dictionary<string, object?> od)
                    _occluders.Add(new Occluder
                    {
                        Min = new Vector3((float)ToD(od.GetValueOrDefault("min_x")), (float)ToD(od.GetValueOrDefault("min_y")), (float)ToD(od.GetValueOrDefault("min_z"))),
                        Max = new Vector3((float)ToD(od.GetValueOrDefault("max_x")), (float)ToD(od.GetValueOrDefault("max_y")), (float)ToD(od.GetValueOrDefault("max_z"))),
                    });

        _baked = false;
        return null;
    }

    /// reset() — clear all lights, occluders, baked probes, and voxel state.
    public static object? Reset(List<object?> _)
    {
        _lights.Clear(); _occluders.Clear(); _probes.Clear();
        _baked = false; _mode = "realtime";
        _ambient = new Vector3(0.05f, 0.05f, 0.07f); _ambientIntensity = 1f;
        _voxelSolid = null; _voxelEmissive = null; _voxelLight = null;
        return null;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool Truthy(object? v) => v switch
    {
        bool b => b, double d => d != 0, string s => s.Length > 0,
        List<object?> l => l.Count > 0, null => false, _ => true,
    };

    private static double ToD(object? v) => v switch
    {
        double d => d, bool b => b ? 1 : 0, string s when double.TryParse(s, out var r) => r, _ => 0,
    };

    private static float  F(List<object?> a, int i) => a.Count > i ? (float)ToD(a[i]) : 0f;
    private static double D(List<object?> a, int i) => a.Count > i ? ToD(a[i]) : 0;
    private static int    Idx(List<object?> a, int i) => a.Count > i ? (int)ToD(a[i]) : -1;
    private static int    Ix(List<object?> a, int i) => (int)D(a, i);
    private static int    Iy(List<object?> a, int i) => (int)D(a, i);
    private static int    Iz(List<object?> a, int i) => (int)D(a, i);

    private static HLight? Get(List<HLight?> list, int id) =>
        id >= 0 && id < list.Count ? list[id] : null;

    // ── Dispatch table ────────────────────────────────────────────────────────

    public static readonly Dictionary<string, Func<List<object?>, object?>> Funcs = new()
    {
        ["set_mode"]           = SetMode,
        ["get_mode"]           = GetMode,
        ["set_ambient"]        = SetAmbient,
        ["add_light"]          = AddLight,
        ["remove_light"]       = RemoveLight,
        ["set_light_pos"]      = SetLightPos,
        ["set_light_color"]    = SetLightColor,
        ["set_light_intensity"]= SetLightIntensity,
        ["set_light_range"]    = SetLightRange,
        ["set_light_enabled"]  = SetLightEnabled,
        ["light_count"]        = LightCount,
        ["light_info"]         = LightInfo,
        ["add_occluder"]       = AddOccluder,
        ["remove_occluder"]    = RemoveOccluder,
        ["clear_occluders"]    = ClearOccluders,
        ["clear_lights"]       = ClearLights,
        ["light_at"]           = LightAt,
        ["shade"]              = Shade,
        ["bake"]               = Bake,
        ["is_baked"]           = IsBaked,
        ["voxel_init"]         = VoxelInit,
        ["voxel_set_solid"]    = VoxelSetSolid,
        ["voxel_set_emissive"] = VoxelSetEmissive,
        ["voxel_bake"]         = VoxelBake,
        ["voxel_light"]        = VoxelLight,
        ["voxel_color"]        = VoxelColor,
        ["voxel_solid"]        = VoxelSolid,
        ["save_config"]        = SaveConfig,
        ["load_config"]        = LoadConfig,
        ["reset"]              = Reset,
    };
}

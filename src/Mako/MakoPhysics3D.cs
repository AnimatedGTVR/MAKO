using System.Numerics;

namespace Mako;

/// A small, rendering-independent 3D rigid-body simulation — the 3D sibling of
/// MakoPhysics2D. MAKO-facing API uses numeric handles so this core can later
/// sit behind Mako3D or an editor/engine integration without exposing C#
/// objects to scripts. Supports linear/angular motion, primitive collision,
/// static/kinematic/dynamic bodies, characters, sleep, and game queries.
/// Orientation is a quaternion internally; the script boundary
/// always speaks degrees (matching MakoPhysics2D's degrees-at-the-boundary
/// convention), never raw quaternion components.
static class MakoPhysics3D
{
    private enum BodyKind { Static, Kinematic, Dynamic }
    private enum ShapeKind { Sphere, Box, Plane, Capsule }

    private sealed class Body
    {
        public required int Id;
        public required BodyKind Kind;
        public required ShapeKind Shape;
        public Vector3 Position;
        public Vector3 Velocity;
        public Vector3 Force;
        public Quaternion Rotation = Quaternion.Identity;
        public Vector3 AngularVelocity;
        public Vector3 Torque;
        public float Mass;
        public float InverseMass;
        public Vector3 InertiaDiagonal;
        public Vector3 InverseInertiaDiagonal;
        public bool RotationLocked;
        public float LinearDamping = 0.05f;
        public float AngularDamping = 0.08f;
        public bool Awake = true;
        public float SleepTimer;
        public bool Grounded;
        public readonly HashSet<int> Contacts = [];
        public string Layer = "default";
        public readonly HashSet<string> IgnoredLayers = new(StringComparer.OrdinalIgnoreCase);
        public bool IsTrigger;
        public bool CharacterTriggered;

        // Shape data — meaning depends on Shape.
        public float Radius;         // Sphere, Capsule
        public Vector3 Size;         // Box (full width/height/depth)
        public Vector3 PlaneNormal;  // Plane (unit, local == world; planes don't rotate)
        public float PlaneOffset;    // Plane: signed distance from origin along PlaneNormal
        public float CapsuleHalfHeight; // Capsule: half the distance between the two sphere centres

        public float Restitution;
        public float Friction;
    }

    private sealed class World
    {
        public string Backend = "mako";
        public Vector3 Gravity;
        public float FixedStep;
        public float Accumulator;
        public int Substeps;
        public readonly List<Body?> Bodies = [];
        public readonly List<Character?> Characters = [];
        public readonly List<Spring?> Springs = [];
    }

    private sealed class Spring
    {
        public required int Id;
        public required int BodyA;
        public required int BodyB;
        public float RestLength;
        public float Strength;
        public float Damping;
    }

    /// A capsule character controller: kinematic (never affected by
    /// impulses/collision response like a rigid body), moved every step by
    /// its own sweep-and-slide integrator instead of the normal rigid-body
    /// solver. Deliberately a separate structure rather than a Body/
    /// BodyKind variant — it must never be picked up by the pairwise
    /// rigid-body collision loop in FixedStep, only tested against it
    /// on-demand from MoveCharacter, so keeping it out of world.Bodies
    /// entirely rules that class of bug out by construction.
    private sealed class Character
    {
        public required int Id;
        public Vector3 Position;
        public Vector3 Velocity;   // vertical (gravity/jump) velocity only
        public float Radius;
        public float HalfHeight;   // capsule core segment half-length (excludes the two spherical caps)
        public float Speed;        // ground move speed, units/sec
        public float JumpSpeed;    // initial vertical speed on jump
        public float AirControl = 0.35f;   // 0..1 fraction of ground control while airborne
        public bool Grounded;
        public int GroundBodyId = -1;
        public float CoyoteTimer;          // time since last grounded, for late-jump forgiveness
        public float JumpBufferTimer;      // time since jump was requested, for early-jump forgiveness
        public Vector3 PendingMove;        // this step's desired horizontal direction, set by character_move()
        public readonly HashSet<int> TriggerContacts = [];
        public const float CoyoteDuration = 0.12f;
        public const float JumpBufferDuration = 0.12f;
    }

    private readonly record struct Contact(Body A, Body B, Vector3 Normal, float Penetration, List<Vector3> Points);
    private static readonly List<World?> Worlds = [];
    private static string DefaultBackend = "mako";

    public static void ResetAll()
    {
        Worlds.Clear();
        DefaultBackend = "mako";
    }

    public static void SelectDefaultBackend(string importName, string backendName)
    {
        var backend = PhysicsBackends.Find(backendName);
        if (backend.Dimension != "3d" || !backend.Installed)
            throw new MakoError($"using {importName}: {backend.Status}");
        if (!backend.BuiltIn)
            throw new MakoError($"using {importName}: {backend.Name} is installed, but this MAKO runtime was not compiled with its bridge.");
        if (DefaultBackend != "mako" && DefaultBackend != backend.Id)
            throw new MakoError($"using {importName}: Physics3D already selected '{DefaultBackend}'. Use only one 3D physics backend per script.");
        DefaultBackend = backend.Id;
    }

    // ── Worlds and bodies ────────────────────────────────────────────────────

    public static object? CreateWorld(List<object?> a)
    {
        bool namedBackend = a.Count > 0 && a[0] is string;
        string backendName = namedBackend ? a[0]?.ToString() ?? DefaultBackend : DefaultBackend;
        var backend = PhysicsBackends.Find(backendName);
        if (backend.Dimension is "2d" or "unknown" || !backend.Installed)
            throw new MakoError($"Physics3D.world(): {backend.Status}");
        if (!backend.BuiltIn)
            throw new MakoError($"Physics3D.world(): {backend.Name} adapter was found, but its runtime bridge is not enabled in this MAKO build. Rebuild MAKO with the '{backend.Id}' adapter.");
        int offset = namedBackend ? 1 : 0;
        float gx = Number(a, offset, 0);
        float gy = Number(a, offset + 1, -9.81f);
        float gz = Number(a, offset + 2, 0);
        float fixedStep = Number(a, offset + 3, 1f / 60f);
        int substeps = (int)Number(a, offset + 4, 4);
        if (fixedStep <= 0) throw new MakoError("Physics3D.world(): fixed_step must be greater than 0");
        if (substeps < 1 || substeps > 16) throw new MakoError("Physics3D.world(): substeps must be between 1 and 16");
        Worlds.Add(new World { Backend = backend.Id, Gravity = new Vector3(gx, gy, gz), FixedStep = fixedStep, Substeps = substeps });
        return (double)(Worlds.Count - 1);
    }

    public static object? BackendInfo(List<object?> a)
    {
        var backend = PhysicsBackends.Find(Text(a, 0, "mako"));
        return new Dictionary<string, object?> {
            ["id"] = backend.Id, ["name"] = backend.Name, ["dimension"] = backend.Dimension,
            ["built_in"] = backend.BuiltIn, ["installed"] = backend.Installed, ["status"] = backend.Status
        };
    }

    public static object? DestroyWorld(List<object?> a)
    {
        int id = Handle(a, 0, "Physics3D.destroy_world");
        if (id >= 0 && id < Worlds.Count) Worlds[id] = null;
        return null;
    }

    public static object? ClearWorld(List<object?> a)
    {
        var world = WorldAt(a, "Physics3D.clear");
        world.Bodies.Clear();
        world.Characters.Clear();
        world.Springs.Clear();
        return null;
    }

    public static object? CreateBox(List<object?> a)
    {
        var world = WorldAt(a, "Physics3D.box");
        bool legacy = a.Count > 1 && a[1] is string;
        string kindText = legacy ? a[1]?.ToString() ?? "dynamic" : "dynamic";
        var kind = ParseKind(kindText, "Physics3D.box");

        int o = legacy ? 2 : 1;
        float x = Number(a, o, 0), y = Number(a, o + 1, 0), z = Number(a, o + 2, 0);
        float w = Number(a, o + 3, 1), h = Number(a, o + 4, 1), d = Number(a, o + 5, 1);
        if (w <= 0 || h <= 0 || d <= 0)
            throw new MakoError("Physics3D.box(): width, height, and depth must be greater than 0");

        float mass = Number(a, o + 6, 1);
        if (kind == BodyKind.Dynamic && mass <= 0)
            throw new MakoError("Physics3D.box(): a dynamic body's mass must be greater than 0");

        // Solid-box inertia tensor (diagonal, in local/principal axes).
        var inertia = new Vector3(
            mass * (h * h + d * d) / 12f,
            mass * (w * w + d * d) / 12f,
            mass * (w * w + h * h) / 12f);

        int id = world.Bodies.Count;
        var body = MakeBody(id, kind, ShapeKind.Box, new Vector3(x, y, z), mass, inertia, a, o + 9);
        body.Size = new Vector3(w, h, d);
        body.Restitution = Math.Clamp(Number(a, o + 7, 0.2f), 0, 1);
        body.Friction = Math.Clamp(Number(a, o + 8, 0.4f), 0, 1);
        world.Bodies.Add(body);
        return (double)id;
    }

    public static object? CreateSphere(List<object?> a)
    {
        var world = WorldAt(a, "Physics3D.sphere");
        bool legacy = a.Count > 1 && a[1] is string;
        string kindText = legacy ? a[1]?.ToString() ?? "dynamic" : "dynamic";
        var kind = ParseKind(kindText, "Physics3D.sphere");

        int o = legacy ? 2 : 1;
        float x = Number(a, o, 0), y = Number(a, o + 1, 0), z = Number(a, o + 2, 0);
        float radius = Number(a, o + 3, 1);
        if (radius <= 0) throw new MakoError("Physics3D.sphere(): radius must be greater than 0");

        float mass = Number(a, o + 4, 1);
        if (kind == BodyKind.Dynamic && mass <= 0)
            throw new MakoError("Physics3D.sphere(): a dynamic body's mass must be greater than 0");

        float i = 0.4f * mass * radius * radius; // solid sphere: 2/5 m r^2
        var inertia = new Vector3(i, i, i);

        int id = world.Bodies.Count;
        var body = MakeBody(id, kind, ShapeKind.Sphere, new Vector3(x, y, z), mass, inertia, a, o + 7);
        body.Radius = radius;
        body.Restitution = Math.Clamp(Number(a, o + 5, 0.2f), 0, 1);
        body.Friction = Math.Clamp(Number(a, o + 6, 0.4f), 0, 1);
        world.Bodies.Add(body);
        return (double)id;
    }

    /// A plane is always static (infinite, so kinematic/dynamic wouldn't make
    /// sense) — defined by a unit normal and a signed distance from the
    /// world origin along that normal, i.e. all points p with dot(p, normal) == offset.
    public static object? CreatePlane(List<object?> a)
    {
        var world = WorldAt(a, "Physics3D.plane");
        float nx = Number(a, 1, 0), ny = Number(a, 2, 1), nz = Number(a, 3, 0);
        var normal = new Vector3(nx, ny, nz);
        if (normal.LengthSquared() < 0.0001f)
            throw new MakoError("Physics3D.plane(): normal must not be the zero vector");
        normal = Vector3.Normalize(normal);
        float offset = Number(a, 4, 0);

        int id = world.Bodies.Count;
        var body = new Body
        {
            Id = id, Kind = BodyKind.Static, Shape = ShapeKind.Plane,
            Position = normal * offset,
            PlaneNormal = normal, PlaneOffset = offset,
            Restitution = Math.Clamp(Number(a, 5, 0.2f), 0, 1),
            Friction = Math.Clamp(Number(a, 6, 0.6f), 0, 1),
        };
        world.Bodies.Add(body);
        return (double)id;
    }

    public static object? CreateCapsule(List<object?> a)
    {
        var world = WorldAt(a, "Physics3D.capsule");
        bool legacy = a.Count > 1 && a[1] is string;
        string kindText = legacy ? a[1]?.ToString() ?? "dynamic" : "dynamic";
        var kind = ParseKind(kindText, "Physics3D.capsule");

        int o = legacy ? 2 : 1;
        float x = Number(a, o, 0), y = Number(a, o + 1, 0), z = Number(a, o + 2, 0);
        float radius = Number(a, o + 3, 0.5f);
        float height = Number(a, o + 4, 2f);
        if (radius <= 0) throw new MakoError("Physics3D.capsule(): radius must be greater than 0");
        if (height <= 0) throw new MakoError("Physics3D.capsule(): height must be greater than 0");
        // height is the total capsule length including the two hemispherical
        // caps, matching how a character's total height is usually specified.
        float halfHeight = MathF.Max(0, height * 0.5f - radius);

        float mass = Number(a, o + 5, 1);
        if (kind == BodyKind.Dynamic && mass <= 0)
            throw new MakoError("Physics3D.capsule(): a dynamic body's mass must be greater than 0");

        // Capsule inertia (cylinder + two hemispherical caps), Y is the long axis.
        float cylinderHeight = halfHeight * 2f;
        float cylinderMass = mass * cylinderHeight / (cylinderHeight + 4f / 3f * radius);
        float capsMass = mass - cylinderMass;
        float iy = 0.5f * cylinderMass * radius * radius +
                   0.4f * capsMass * radius * radius;
        float ix = cylinderMass * (3 * radius * radius + cylinderHeight * cylinderHeight) / 12f +
                   capsMass * (0.4f * radius * radius + 0.5f * cylinderHeight * cylinderHeight + 0.75f * cylinderHeight * radius);
        var inertia = new Vector3(ix, iy, ix);

        int id = world.Bodies.Count;
        var body = MakeBody(id, kind, ShapeKind.Capsule, new Vector3(x, y, z), mass, inertia, a, o + 8);
        body.Radius = radius;
        body.CapsuleHalfHeight = halfHeight;
        body.Restitution = Math.Clamp(Number(a, o + 6, 0.2f), 0, 1);
        body.Friction = Math.Clamp(Number(a, o + 7, 0.4f), 0, 1);
        world.Bodies.Add(body);
        return (double)id;
    }

    private static Body MakeBody(int id, BodyKind kind, ShapeKind shape, Vector3 position, float mass, Vector3 inertia,
        List<object?> a, int rotationOffset)
    {
        bool dynamic = kind == BodyKind.Dynamic;
        return new Body
        {
            Id = id,
            Kind = kind,
            Shape = shape,
            Position = position,
            Mass = dynamic ? mass : 0,
            InverseMass = dynamic ? 1f / mass : 0,
            InertiaDiagonal = dynamic ? inertia : Vector3.Zero,
            InverseInertiaDiagonal = dynamic
                ? new Vector3(SafeInv(inertia.X), SafeInv(inertia.Y), SafeInv(inertia.Z))
                : Vector3.Zero,
            Rotation = QuaternionFromDegrees(Number(a, rotationOffset, 0), Number(a, rotationOffset + 1, 0), Number(a, rotationOffset + 2, 0)),
        };
    }

    private static float SafeInv(float v) => v > 0 ? 1f / v : 0;

    private static BodyKind ParseKind(string kindText, string fn) => kindText.ToLowerInvariant() switch
    {
        "static" => BodyKind.Static,
        "kinematic" => BodyKind.Kinematic,
        "dynamic" => BodyKind.Dynamic,
        _ => throw new MakoError($"{fn}(): body type must be 'static', 'kinematic', or 'dynamic'")
    };

    // Friendly constructors. The normal shape calls create dynamic bodies;
    // these names make immovable geometry obvious without a quoted type or
    // a long options object.
    public static object? CreateStaticBox(List<object?> a) => CreateBox(WithKind(a, "static"));
    public static object? CreateStaticSphere(List<object?> a) => CreateSphere(WithKind(a, "static"));
    public static object? CreateStaticCapsule(List<object?> a) => CreateCapsule(WithKind(a, "static"));
    public static object? CreateMovingBox(List<object?> a) => CreateBox(WithKind(a, "kinematic"));
    public static object? CreateMovingSphere(List<object?> a) => CreateSphere(WithKind(a, "kinematic"));
    public static object? CreateMovingCapsule(List<object?> a) => CreateCapsule(WithKind(a, "kinematic"));

    public static object? CreateFloor(List<object?> a)
    {
        var plane = new List<object?> { a.Count > 0 ? a[0] : null, 0d, 1d, 0d, Number(a, 1, 0) };
        if (a.Count > 2) plane.Add(a[2]);
        if (a.Count > 3) plane.Add(a[3]);
        return CreatePlane(plane);
    }

    private static List<object?> WithKind(List<object?> a, string kind)
    {
        var result = new List<object?>(a.Count + 1);
        if (a.Count > 0) result.Add(a[0]);
        result.Add(kind);
        for (int i = 1; i < a.Count; i++) result.Add(a[i]);
        return result;
    }

    public static object? RemoveBody(List<object?> a)
    {
        var world = WorldAt(a, "Physics3D.remove_body");
        int id = Handle(a, 1, "Physics3D.remove_body");
        if (id >= 0 && id < world.Bodies.Count)
        {
            world.Bodies[id] = null;
            foreach (var body in world.Bodies) body?.Contacts.Remove(id);
            for (int i = 0; i < world.Springs.Count; i++)
                if (world.Springs[i] is { } spring && (spring.BodyA == id || spring.BodyB == id))
                    world.Springs[i] = null;
        }
        return null;
    }

    public static object? BodyCount(List<object?> a) =>
        (double)WorldAt(a, "Physics3D.body_count").Bodies.Count(b => b != null);

    public static object? SetLayer(List<object?> a)
    {
        var body = BodyAt(a, "Physics3D.layer");
        string layer = Text(a, 2, "default").Trim();
        if (layer.Length == 0) throw new MakoError("Physics3D.layer(): layer name must not be empty");
        body.Layer = layer;
        return null;
    }

    public static object? IgnoreLayer(List<object?> a)
    {
        var body = BodyAt(a, "Physics3D.ignore_layer");
        string layer = Text(a, 2, "default").Trim();
        bool ignored = a.Count <= 3 || IsTruthy(a[3]);
        if (ignored) body.IgnoredLayers.Add(layer);
        else body.IgnoredLayers.Remove(layer);
        return null;
    }

    public static object? SetTrigger(List<object?> a)
    {
        var body = BodyAt(a, "Physics3D.trigger");
        body.IsTrigger = a.Count <= 2 || IsTruthy(a[2]);
        return null;
    }

    public static object? IsTriggered(List<object?> a)
    {
        var body = BodyAt(a, "Physics3D.is_triggered");
        return body.IsTrigger && (body.Contacts.Count > 0 || body.CharacterTriggered);
    }

    public static object? CreateSpring(List<object?> a)
    {
        var world = WorldAt(a, "Physics3D.spring");
        int bodyA = Handle(a, 1, "Physics3D.spring");
        int bodyB = Handle(a, 2, "Physics3D.spring");
        if (bodyA == bodyB) throw new MakoError("Physics3D.spring(): bodies must be different");
        var aBody = BodyById(world, bodyA, "Physics3D.spring");
        var bBody = BodyById(world, bodyB, "Physics3D.spring");
        float currentLength = Vector3.Distance(aBody.Position, bBody.Position);
        float restLength = Number(a, 3, -1);
        if (restLength < 0) restLength = currentLength;
        float strength = Number(a, 4, 80);
        float damping = Number(a, 5, 8);
        if (restLength < 0 || strength < 0 || damping < 0)
            throw new MakoError("Physics3D.spring(): rest, strength, and damping must not be negative");
        int id = world.Springs.Count;
        world.Springs.Add(new Spring
        {
            Id = id, BodyA = bodyA, BodyB = bodyB,
            RestLength = restLength, Strength = strength, Damping = damping,
        });
        Wake(aBody); Wake(bBody);
        return (double)id;
    }

    public static object? SetSpring(List<object?> a)
    {
        var spring = SpringAt(a, "Physics3D.set_spring");
        spring.RestLength = MathF.Max(0, Number(a, 2, spring.RestLength));
        spring.Strength = MathF.Max(0, Number(a, 3, spring.Strength));
        spring.Damping = MathF.Max(0, Number(a, 4, spring.Damping));
        return null;
    }

    public static object? SpringInfo(List<object?> a)
    {
        var world = WorldAt(a, "Physics3D.spring_info");
        int id = Handle(a, 1, "Physics3D.spring_info");
        if (id < 0 || id >= world.Springs.Count || world.Springs[id] is not { } spring) return null;
        var bodyA = BodyById(world, spring.BodyA, "Physics3D.spring_info");
        var bodyB = BodyById(world, spring.BodyB, "Physics3D.spring_info");
        return new Dictionary<string, object?>
        {
            ["id"] = (double)spring.Id, ["a"] = (double)spring.BodyA, ["b"] = (double)spring.BodyB,
            ["length"] = (double)Vector3.Distance(bodyA.Position, bodyB.Position),
            ["rest"] = (double)spring.RestLength, ["strength"] = (double)spring.Strength,
            ["damping"] = (double)spring.Damping,
        };
    }

    public static object? RemoveSpring(List<object?> a)
    {
        var world = WorldAt(a, "Physics3D.remove_spring");
        int id = Handle(a, 1, "Physics3D.remove_spring");
        if (id >= 0 && id < world.Springs.Count) world.Springs[id] = null;
        return null;
    }

    public static object? SpringCount(List<object?> a) =>
        (double)WorldAt(a, "Physics3D.spring_count").Springs.Count(s => s != null);

    public static object? SetGravity(List<object?> a)
    {
        var world = WorldAt(a, "Physics3D.gravity");
        world.Gravity = new Vector3(Number(a, 1, world.Gravity.X), Number(a, 2, world.Gravity.Y), Number(a, 3, world.Gravity.Z));
        foreach (var body in world.Bodies)
            if (body?.Kind == BodyKind.Dynamic) Wake(body);
        return null;
    }

    // ── Character controller ─────────────────────────────────────────────────
    //
    // A capsule character controller built on the same collision
    // primitives as rigid bodies (CapsuleBox/CapsulePlane/CapsuleSphere/
    // CapsuleCapsule), but moved by its own sweep-and-slide integrator each
    // step rather than the impulse solver — a walking character wants
    // "push me out of geometry, don't bounce me," which is a fundamentally
    // different response than a dynamic rigid body's contact resolution.

    /// character(world, x, y, z, height=1.8, radius=0.4, speed=6, jump=8,
    /// air_control=0.35) -> handle. The old options dictionary is accepted
    /// for compatibility, but MAKO code should use this short form.
    public static object? CreateCharacter(List<object?> a)
    {
        var world = WorldAt(a, "Physics3D.character");
        var options = a.Count > 1 && a[1] is Dictionary<string, object?> dict ? dict : null;

        var posList = options?.GetValueOrDefault("position") as List<object?>;
        float px = options != null ? (posList != null && posList.Count > 0 ? (float)Convert.ToDouble(posList[0]) : 0) : Number(a, 1, 0);
        float py = options != null ? (posList != null && posList.Count > 1 ? (float)Convert.ToDouble(posList[1]) : 0) : Number(a, 2, 0);
        float pz = options != null ? (posList != null && posList.Count > 2 ? (float)Convert.ToDouble(posList[2]) : 0) : Number(a, 3, 0);

        float height = options != null ? OptionOrDefault(options, "height", 1.8f) : Number(a, 4, 1.8f);
        float radius = options != null ? OptionOrDefault(options, "radius", 0.4f) : Number(a, 5, 0.4f);
        if (height <= radius * 2f) throw new MakoError("Physics3D.character(): height must be greater than 2 * radius");
        if (radius <= 0) throw new MakoError("Physics3D.character(): radius must be greater than 0");

        int id = world.Characters.Count;
        world.Characters.Add(new Character
        {
            Id = id,
            Position = new Vector3(px, py, pz),
            Radius = radius,
            HalfHeight = height * 0.5f - radius,
            Speed = options != null ? OptionOrDefault(options, "speed", 6f) : Number(a, 6, 6f),
            JumpSpeed = options != null ? OptionOrDefault(options, "jump", 8f) : Number(a, 7, 8f),
            AirControl = Math.Clamp(options != null ? OptionOrDefault(options, "air_control", 0.35f) : Number(a, 8, 0.35f), 0f, 1f),
        });
        return (double)id;
    }

    private static float OptionOrDefault(Dictionary<string, object?>? options, string key, float fallback)
    {
        if (options == null || !options.TryGetValue(key, out var v) || v == null) return fallback;
        try { return (float)Convert.ToDouble(v); } catch { return fallback; }
    }

    private static Character CharacterAt(List<object?> a, string fn)
    {
        var world = WorldAt(a, fn);
        int id = Handle(a, 1, fn);
        if (id < 0 || id >= world.Characters.Count || world.Characters[id] is not { } c)
            throw new MakoError($"{fn}(): invalid character handle {id}");
        return c;
    }

    /// character_move(world, id, x, z) — sets desired horizontal movement
    /// direction for this step (x/z need not be normalized; only the
    /// direction matters, scaled by the character's configured speed).
    /// Actually moving happens inside Physics3D.step() so movement is
    /// swept against the same fixed-timestep world every other body uses.
    public static object? MoveCharacter(List<object?> a)
    {
        var c = CharacterAt(a, "Physics3D.character_move");
        float x = Number(a, 2, 0), z = Number(a, 3, 0);
        var dir = new Vector3(x, 0, z);
        c.PendingMove = dir.LengthSquared() > 1f ? Vector3.Normalize(dir) : dir;
        return null;
    }

    /// character_jump(world, id) → bool, true if the jump actually took
    /// effect this call (grounded, or within the coyote window). Always
    /// buffers the request for a short window even if it returns false, so
    /// a jump pressed just before landing still fires the instant the
    /// character touches down.
    public static object? JumpCharacter(List<object?> a)
    {
        var c = CharacterAt(a, "Physics3D.character_jump");
        bool canJumpNow = c.Grounded || c.CoyoteTimer > 0;
        if (canJumpNow)
        {
            c.Velocity.Y = c.JumpSpeed;
            c.Grounded = false;
            c.CoyoteTimer = 0;
            c.JumpBufferTimer = 0;
        }
        else
        {
            c.JumpBufferTimer = Character.JumpBufferDuration;
        }
        return (object?)canJumpNow;
    }

    public static object? CharacterInfo(List<object?> a)
    {
        var world = WorldAt(a, "Physics3D.character_info");
        int id = Handle(a, 1, "Physics3D.character_info");
        if (id < 0 || id >= world.Characters.Count || world.Characters[id] is not { } c) return null;
        return new Dictionary<string, object?>
        {
            ["id"] = (double)c.Id,
            ["x"] = (double)c.Position.X, ["y"] = (double)c.Position.Y, ["z"] = (double)c.Position.Z,
            ["vy"] = (double)c.Velocity.Y,
            ["grounded"] = c.Grounded,
            ["ground_body"] = c.GroundBodyId >= 0 ? (double)c.GroundBodyId : null,
            ["triggers"] = c.TriggerContacts.Order().Select(id => (object?)(double)id).ToList(),
            ["radius"] = (double)c.Radius,
            ["height"] = (double)((c.HalfHeight + c.Radius) * 2f),
        };
    }

    public static object? RemoveCharacter(List<object?> a)
    {
        var world = WorldAt(a, "Physics3D.remove_character");
        int id = Handle(a, 1, "Physics3D.remove_character");
        if (id >= 0 && id < world.Characters.Count) world.Characters[id] = null;
        return null;
    }

    /// Advances every character in the world: gravity, jump buffering/
    /// coyote time, then a sweep-and-slide move against every rigid body —
    /// called once per fixed step from FixedStep, using the same dt as
    /// everything else so character motion doesn't desync from the rest of
    /// the simulation at high/low frame rates.
    private static void StepCharacters(World world, float dt)
    {
        foreach (var c in world.Characters)
        {
            if (c == null) continue;

            var carry = Vector3.Zero;
            if (c.GroundBodyId >= 0 && c.GroundBodyId < world.Bodies.Count && world.Bodies[c.GroundBodyId] is { } ground)
                carry = ground.Velocity * dt;
            c.GroundBodyId = -1;

            // Gravity — characters aren't rigid bodies, so they don't read
            // World.Gravity through the normal integration path; apply it
            // directly here instead.
            if (!c.Grounded) c.Velocity.Y += world.Gravity.Y * dt;
            else if (c.Velocity.Y < 0) c.Velocity.Y = -1f; // small stick-to-ground bias, not a free fall

            // Coyote time: keep "was grounded recently" true for a short
            // window after walking off a ledge, so a jump pressed just
            // after that still works instead of feeling like a dropped input.
            c.CoyoteTimer = c.Grounded ? Character.CoyoteDuration : MathF.Max(0, c.CoyoteTimer - dt);
            // Jump buffering: a jump requested just before landing still
            // fires the instant the character touches down.
            if (c.JumpBufferTimer > 0)
            {
                c.JumpBufferTimer = MathF.Max(0, c.JumpBufferTimer - dt);
                if (c.Grounded)
                {
                    c.Velocity.Y = c.JumpSpeed;
                    c.Grounded = false;
                    c.CoyoteTimer = 0;
                    c.JumpBufferTimer = 0;
                }
            }

            float control = c.Grounded ? 1f : c.AirControl;
            var horizontal = c.PendingMove * c.Speed * control * dt;
            var vertical = new Vector3(0, c.Velocity.Y * dt, 0);

            MoveAndSlide(world, c, carry + horizontal + vertical);
            ProbeGround(world, c);

            c.PendingMove = Vector3.Zero; // consumed — character_move() must be called again each step to keep moving
        }
    }

    /// Iterative collide-and-slide: apply the desired displacement, then
    /// repeatedly push the character out of anything it's overlapping along
    /// the contact normal (removing the into-surface component of the
    /// remaining displacement each time) — the standard approach for a
    /// walking character, as opposed to a rigid body's bouncy impulse
    /// response. Long movement is subdivided by capsule radius before these
    /// iterations, so even unusually fast characters test thin geometry.
    private static void MoveAndSlide(World world, Character c, Vector3 delta)
    {
        // Break long moves into capsule-sized pieces. This is inexpensive for
        // normal walking and prevents a fast character crossing a thin wall
        // between two overlap tests.
        int moveSteps = Math.Clamp((int)MathF.Ceiling(delta.Length() / MathF.Max(c.Radius * 0.5f, 0.05f)), 1, 64);
        var move = delta / moveSteps;
        for (int moveStep = 0; moveStep < moveSteps; moveStep++)
        {
            c.Position += move;
        const int iterations = 4;
        for (int i = 0; i < iterations; i++)
        {
            bool anyPenetration = false;
            foreach (var body in world.Bodies)
            {
                if (body == null) continue;
                if (!CharacterOverlaps(c, body, out var normal, out var penetration)) continue;
                if (body.IsTrigger)
                {
                    c.TriggerContacts.Add(body.Id);
                    body.CharacterTriggered = true;
                    continue;
                }
                anyPenetration = true;
                c.Position += normal * penetration;
                // Landing on top of something (normal points up) grounds the
                // character and zeroes downward velocity so gravity doesn't
                // reaccumulate into the floor next step. Explicitly excludes
                // the case where the character has real upward velocity
                // (actively jumping this step) — the capsule can still be
                // geometrically overlapping the surface it just launched
                // from in the same step the jump was requested (a small
                // residual penetration hasn't been fully cleared by the
                // jump's displacement yet), and without this check that
                // overlap re-grounds the character and kills the jump
                // before it ever leaves the floor.
                if (normal.Y > 0.5f && c.Velocity.Y <= 0.01f)
                {
                    c.Grounded = true;
                    c.GroundBodyId = body.Id;
                    if (c.Velocity.Y < 0) c.Velocity.Y = 0;
                }
                else if (normal.Y < -0.5f && c.Velocity.Y > 0)
                {
                    c.Velocity.Y = 0; // hit a ceiling
                }
            }
            if (!anyPenetration) break;
        }
        }
    }

    /// Ground probe: after depenetration, a character standing on flat
    /// ground has already been marked Grounded by MoveAndSlide's normal
    /// check. This additionally handles walking off a ledge onto a lower
    /// surface within step height, and clears Grounded when there's
    /// genuinely nothing underneath — cast a short capsule segment
    /// downward and see if it still touches anything.
    private static void ProbeGround(World world, Character c)
    {
        if (c.Grounded) return; // already confirmed by contact this step
        if (c.Velocity.Y > 0.01f) return; // actively moving upward (jumping) — not grounded, don't snap back down
        const float probeDistance = 0.15f;
        var probe = new Character
        {
            Id = -1, Position = c.Position - new Vector3(0, probeDistance, 0),
            Radius = c.Radius, HalfHeight = c.HalfHeight,
        };
        foreach (var body in world.Bodies)
        {
            if (body == null || body.IsTrigger) continue;
            if (CharacterOverlaps(probe, body, out var normal, out var penetration) && normal.Y > 0.5f)
            {
                // Move only across the measured gap. Moving the entire probe
                // distance embeds the capsule and causes a visible downward
                // pop whenever it walks onto a slightly lower surface.
                c.Position.Y -= Math.Clamp(probeDistance - penetration, 0, probeDistance);
                c.Grounded = true;
                c.GroundBodyId = body.Id;
                if (c.Velocity.Y < 0) c.Velocity.Y = 0;
                return;
            }
        }
    }

    /// Tests a character's capsule against one rigid body using the same
    /// narrowphase functions bodies use against each other, by wrapping the
    /// character in a throwaway kinematic capsule Body — reuses the proven
    /// CapsuleBox/CapsulePlane/CapsuleSphere/CapsuleCapsule code instead of
    /// a second, parallel collision implementation.
    private static bool CharacterOverlaps(Character c, Body other, out Vector3 normal, out float penetration)
    {
        var capsule = new Body
        {
            Id = -1, Kind = BodyKind.Kinematic, Shape = ShapeKind.Capsule,
            Position = c.Position, Radius = c.Radius, CapsuleHalfHeight = c.HalfHeight,
        };
        if (TryCollide(capsule, other, out var contact))
        {
            // TryCollide's normal points capsule -> other; a character wants
            // to be pushed the opposite way, away from what it hit.
            normal = -contact.Normal;
            penetration = contact.Penetration;
            return true;
        }
        normal = Vector3.Zero; penetration = 0;
        return false;
    }

    // ── Stepping ──────────────────────────────────────────────────────────────

    public static object? Step(List<object?> a)
    {
        var world = WorldAt(a, "Physics3D.step");
        float elapsed = Number(a, 1, world.FixedStep);
        if (elapsed < 0) throw new MakoError("Physics3D.step(): delta must not be negative");

        // Avoid a debugger pause or window drag causing a huge catch-up burst.
        world.Accumulator += Math.Min(elapsed, 0.25f);
        int steps = 0;
        while (world.Accumulator + 0.0000001f >= world.FixedStep)
        {
            FixedStep(world);
            world.Accumulator -= world.FixedStep;
            steps++;
        }
        return (double)steps;
    }

    private static void FixedStep(World world)
    {
        foreach (var body in world.Bodies)
            if (body != null && (body.IsTrigger || body.Kind != BodyKind.Dynamic || body.Awake))
            {
                body.Contacts.Clear();
                body.Grounded = false;
                body.CharacterTriggered = false;
            }
        foreach (var c in world.Characters)
            if (c != null) { c.Grounded = false; c.TriggerContacts.Clear(); }

        int substeps = AdaptiveSubsteps(world);
        float dt = world.FixedStep / substeps;
        for (int substep = 0; substep < substeps; substep++)
        {
            ApplySprings(world, dt);
            foreach (var body in world.Bodies)
            {
                if (body == null) continue;
                if (body.Kind == BodyKind.Dynamic && body.Awake)
                {
                    body.Velocity += (world.Gravity + body.Force * body.InverseMass) * dt;
                    body.Velocity *= MathF.Exp(-body.LinearDamping * dt);
                    body.Position += body.Velocity * dt;

                    if (!body.RotationLocked)
                    {
                        body.AngularVelocity += ApplyInverseInertia(body, body.Torque) * dt;
                        body.AngularVelocity *= MathF.Exp(-body.AngularDamping * dt);
                        body.Rotation = IntegrateRotation(body.Rotation, body.AngularVelocity, dt);
                    }
                }
                else if (body.Kind == BodyKind.Kinematic)
                {
                    body.Position += body.Velocity * dt;
                    if (!body.RotationLocked)
                        body.Rotation = IntegrateRotation(body.Rotation, body.AngularVelocity, dt);
                }
            }

            // Sequential impulses over several iterations reduce tunnelling
            // and keep stacks from injecting energy — same approach as Physics2D.
            for (int iteration = 0; iteration < 6; iteration++)
            {
                foreach (var pair in BroadPhasePairs(world))
                {
                    var a = pair.A; var b = pair.B;
                    // A sleeping body should only be woken by a neighbor that
                    // is actually moving — a neighbor that's awake but already
                    // below the sleep-velocity threshold (mid-countdown to
                    // sleeping itself, e.g. the body above it in a settling
                    // stack) doesn't count. Without this check, two resting
                    // bodies stacked on each other wake each other back up in
                    // an infinite ping-pong the instant either one reaches its
                    // sleep timer, and neither can ever actually go to sleep.
                    if (a.Kind == BodyKind.Dynamic && !a.Awake &&
                        (b.Kind == BodyKind.Kinematic || (b.Kind == BodyKind.Dynamic && b.Awake && IsMoving(b)))) Wake(a);
                    if (b.Kind == BodyKind.Dynamic && !b.Awake &&
                        (a.Kind == BodyKind.Kinematic || (a.Kind == BodyKind.Dynamic && a.Awake && IsMoving(a)))) Wake(b);
                    if (!a.IsTrigger && !b.IsTrigger &&
                        (a.Kind != BodyKind.Dynamic || !a.Awake) &&
                        (b.Kind != BodyKind.Dynamic || !b.Awake)) continue;
                    if (!TryCollide(a, b, out var contact)) continue;
                    a.Contacts.Add(b.Id); b.Contacts.Add(a.Id);
                    if (a.IsTrigger || b.IsTrigger) continue;
                    // contact.Normal points A -> B (world space, Y-up). If it
                    // points up, B is resting on top of A (B is grounded); if
                    // it points down, A is resting on top of B (A is grounded).
                    if (contact.Normal.Y < -0.45f) a.Grounded = true;
                    if (contact.Normal.Y > 0.45f) b.Grounded = true;
                    Resolve(contact);
                }
            }
        }

        foreach (var body in world.Bodies)
        {
            if (body == null) continue;
            body.Force = Vector3.Zero;
            body.Torque = Vector3.Zero;
            UpdateSleep(body, world.FixedStep);
        }

        // Characters move once per fixed step (not per substep, unlike
        // rigid-body integration above) — sweep-and-slide doesn't need
        // multiple sub-iterations the way impulse resolution does, and
        // running it once keeps character movement simple to reason about
        // against a single, whole-step displacement.
        StepCharacters(world, world.FixedStep);
    }

    private static void ApplySprings(World world, float dt)
    {
        foreach (var spring in world.Springs)
        {
            if (spring == null) continue;
            if (spring.BodyA < 0 || spring.BodyA >= world.Bodies.Count || world.Bodies[spring.BodyA] is not { } a) continue;
            if (spring.BodyB < 0 || spring.BodyB >= world.Bodies.Count || world.Bodies[spring.BodyB] is not { } b) continue;
            var delta = b.Position - a.Position;
            float length = delta.Length();
            if (length < 0.00001f) continue;
            var direction = delta / length;
            float relativeSpeed = Vector3.Dot(b.Velocity - a.Velocity, direction);
            float pull = (length - spring.RestLength) * spring.Strength + relativeSpeed * spring.Damping;
            var impulse = direction * pull * dt;
            if (impulse.LengthSquared() < 0.0000000001f) continue;
            if (a.Kind == BodyKind.Dynamic)
            {
                a.Velocity += impulse * a.InverseMass;
                Wake(a);
            }
            if (b.Kind == BodyKind.Dynamic)
            {
                b.Velocity -= impulse * b.InverseMass;
                Wake(b);
            }
        }
    }

    private static int AdaptiveSubsteps(World world)
    {
        int needed = world.Substeps;
        foreach (var body in world.Bodies)
        {
            if (body == null || body.Kind == BodyKind.Static || !body.Awake) continue;
            float feature = body.Shape switch
            {
                ShapeKind.Sphere or ShapeKind.Capsule => body.Radius,
                ShapeKind.Box => MathF.Min(body.Size.X, MathF.Min(body.Size.Y, body.Size.Z)) * 0.5f,
                _ => 1f,
            };
            float travel = body.Velocity.Length() * world.FixedStep;
            needed = Math.Max(needed, (int)MathF.Ceiling(travel / MathF.Max(feature * 0.5f, 0.05f)));
        }
        return Math.Clamp(needed, world.Substeps, 64);
    }

    private static Vector3 ApplyInverseInertia(Body body, Vector3 worldVector)
    {
        if (body.RotationLocked || body.InverseMass == 0) return Vector3.Zero;
        var inverseRotation = Quaternion.Conjugate(body.Rotation);
        var local = Vector3.Transform(worldVector, inverseRotation);
        local *= body.InverseInertiaDiagonal;
        return Vector3.Transform(local, body.Rotation);
    }

    private static Quaternion IntegrateRotation(Quaternion rotation, Vector3 angularVelocity, float dt)
    {
        if (angularVelocity == Vector3.Zero) return rotation;
        var spin = new Quaternion(angularVelocity.X * dt, angularVelocity.Y * dt, angularVelocity.Z * dt, 0f);
        var delta = spin * rotation;
        var result = new Quaternion(
            rotation.X + 0.5f * delta.X,
            rotation.Y + 0.5f * delta.Y,
            rotation.Z + 0.5f * delta.Z,
            rotation.W + 0.5f * delta.W);
        return Quaternion.Normalize(result);
    }

    // The multi-point box-box contact manifold resolves each point's impulse
    // independently (not as one joint solve), so a resting stack carries a
    // small residual angular velocity — a sub-degree-per-second "micro-rock"
    // — that a strict 1deg/s threshold never fully clears, leaving stacked
    // boxes permanently awake despite being visually at rest. 5deg/s is
    // imperceptible and matches the looser angular sleep thresholds common
    // in other engines for exactly this reason.
    private static bool IsMoving(Body body) =>
        body.Velocity.LengthSquared() >= 0.01f || body.AngularVelocity.LengthSquared() >= DegreesToRadians(5) * DegreesToRadians(5);

    private static void UpdateSleep(Body body, float fixedStep)
    {
        if (body.Kind != BodyKind.Dynamic || !body.Awake) return;
        const float sleepDelay = 0.6f;
        if (!IsMoving(body))
        {
            body.SleepTimer += fixedStep;
            if (body.SleepTimer >= sleepDelay)
            {
                body.Awake = false;
                body.Velocity = Vector3.Zero;
                body.AngularVelocity = Vector3.Zero;
            }
        }
        else
        {
            body.SleepTimer = 0;
        }
    }

    private static void Wake(Body body)
    {
        body.Awake = true;
        body.SleepTimer = 0;
    }

    // ── Contact resolution ───────────────────────────────────────────────────

    private static void Resolve(Contact c)
    {
        float invMassSum = c.A.InverseMass + c.B.InverseMass;
        if (invMassSum <= 0) return;

        // Push overlapping bodies apart, leaving a tiny slop to prevent jitter.
        float correctionSize = MathF.Max(c.Penetration - 0.005f, 0) / invMassSum * 0.65f;
        var correction = c.Normal * correctionSize;
        c.A.Position -= correction * c.A.InverseMass;
        c.B.Position += correction * c.B.InverseMass;

        int pointCount = Math.Max(1, c.Points.Count);
        foreach (var point in c.Points)
        {
            var ra = point - c.A.Position;
            var rb = point - c.B.Position;
            var relativeVelocity = VelocityAt(c.B, rb) - VelocityAt(c.A, ra);
            float alongNormal = Vector3.Dot(relativeVelocity, c.Normal);
            if (alongNormal > 0) continue;

            // Small resting impacts should settle instead of bouncing forever.
            float restitution = alongNormal < -3f ? MathF.Min(c.A.Restitution, c.B.Restitution) : 0;
            var raCrossN = Vector3.Cross(ra, c.Normal);
            var rbCrossN = Vector3.Cross(rb, c.Normal);
            float normalMass = invMassSum +
                Vector3.Dot(ApplyInverseInertia(c.A, raCrossN), raCrossN) +
                Vector3.Dot(ApplyInverseInertia(c.B, rbCrossN), rbCrossN);
            if (normalMass <= 0) continue;
            float impulseSize = -(1 + restitution) * alongNormal / normalMass / pointCount;
            var impulse = c.Normal * impulseSize;
            ApplyContactImpulse(c.A, -impulse, ra);
            ApplyContactImpulse(c.B, impulse, rb);

            relativeVelocity = VelocityAt(c.B, rb) - VelocityAt(c.A, ra);
            var tangent = relativeVelocity - Vector3.Dot(relativeVelocity, c.Normal) * c.Normal;
            if (tangent.LengthSquared() < 0.0000001f) continue;
            tangent = Vector3.Normalize(tangent);
            var raCrossT = Vector3.Cross(ra, tangent);
            var rbCrossT = Vector3.Cross(rb, tangent);
            float tangentMass = invMassSum +
                Vector3.Dot(ApplyInverseInertia(c.A, raCrossT), raCrossT) +
                Vector3.Dot(ApplyInverseInertia(c.B, rbCrossT), rbCrossT);
            if (tangentMass <= 0) continue;
            float frictionImpulse = -Vector3.Dot(relativeVelocity, tangent) / tangentMass / pointCount;
            float friction = MathF.Sqrt(c.A.Friction * c.B.Friction);
            frictionImpulse = Math.Clamp(frictionImpulse, -impulseSize * friction, impulseSize * friction);
            var frictionVector = tangent * frictionImpulse;
            ApplyContactImpulse(c.A, -frictionVector, ra);
            ApplyContactImpulse(c.B, frictionVector, rb);
        }
    }

    private static Vector3 VelocityAt(Body body, Vector3 arm) =>
        body.Velocity + Vector3.Cross(body.AngularVelocity, arm);

    private static void ApplyContactImpulse(Body body, Vector3 impulse, Vector3 arm)
    {
        body.Velocity += impulse * body.InverseMass;
        if (!body.RotationLocked)
            body.AngularVelocity += ApplyInverseInertia(body, Vector3.Cross(arm, impulse));
    }

    // ── Collision detection ──────────────────────────────────────────────────
    //
    // Dispatch order covers sphere-sphere, sphere-plane,
    // sphere-box, box-plane, box-box (SAT), capsule-plane, capsule-box.
    // Capsule-capsule and box-vs-capsule use the same closest-point machinery.
    // Sphere/capsule are treated as "a point (or line segment) with a
    // radius," which keeps every pair a variation on a handful of primitives:
    // closest-point-on-box, closest-point-on-segment, and 3D SAT for box-box.

    private static bool TryCollide(Body a, Body b, out Contact contact)
    {
        switch (a.Shape, b.Shape)
        {
            case (ShapeKind.Sphere, ShapeKind.Sphere):
                return SphereSphere(a, b, out contact);
            case (ShapeKind.Sphere, ShapeKind.Plane):
                return SpherePlane(a, b, out contact);
            case (ShapeKind.Plane, ShapeKind.Sphere):
                return Swap(SpherePlane(b, a, out var c1), a, b, c1, out contact);
            case (ShapeKind.Sphere, ShapeKind.Box):
                return SphereBox(a, b, out contact);
            case (ShapeKind.Box, ShapeKind.Sphere):
                return Swap(SphereBox(b, a, out var c2), a, b, c2, out contact);
            case (ShapeKind.Box, ShapeKind.Plane):
                return BoxPlane(a, b, out contact);
            case (ShapeKind.Plane, ShapeKind.Box):
                return Swap(BoxPlane(b, a, out var c3), a, b, c3, out contact);
            case (ShapeKind.Box, ShapeKind.Box):
                return BoxBox(a, b, out contact);
            case (ShapeKind.Capsule, ShapeKind.Plane):
                return CapsulePlane(a, b, out contact);
            case (ShapeKind.Plane, ShapeKind.Capsule):
                return Swap(CapsulePlane(b, a, out var c4), a, b, c4, out contact);
            case (ShapeKind.Capsule, ShapeKind.Box):
                return CapsuleBox(a, b, out contact);
            case (ShapeKind.Box, ShapeKind.Capsule):
                return Swap(CapsuleBox(b, a, out var c5), a, b, c5, out contact);
            case (ShapeKind.Capsule, ShapeKind.Sphere):
                return CapsuleSphere(a, b, out contact);
            case (ShapeKind.Sphere, ShapeKind.Capsule):
                return Swap(CapsuleSphere(b, a, out var c6), a, b, c6, out contact);
            case (ShapeKind.Capsule, ShapeKind.Capsule):
                return CapsuleCapsule(a, b, out contact);
            default:
                contact = default;
                return false; // plane-plane: infinite planes never usefully collide
        }
    }

    private readonly record struct Bounds(Vector3 Min, Vector3 Max);
    private readonly record struct BodyPair(Body A, Body B);

    private static List<BodyPair> BroadPhasePairs(World world)
    {
        var finite = new List<(Body Body, Bounds Bounds)>();
        var planes = new List<Body>();
        foreach (var body in world.Bodies)
        {
            if (body == null) continue;
            if (body.Shape == ShapeKind.Plane) planes.Add(body);
            else finite.Add((body, BodyBounds(body)));
        }
        finite.Sort((a, b) => a.Bounds.Min.X.CompareTo(b.Bounds.Min.X));

        var pairs = new List<BodyPair>();
        for (int i = 0; i < finite.Count; i++)
        {
            var a = finite[i];
            for (int j = i + 1; j < finite.Count && finite[j].Bounds.Min.X <= a.Bounds.Max.X; j++)
            {
                var b = finite[j];
                if (!ShouldCollide(a.Body, b.Body)) continue;
                if (a.Body.InverseMass == 0 && b.Body.InverseMass == 0 && !a.Body.IsTrigger && !b.Body.IsTrigger) continue;
                if (a.Bounds.Min.Y > b.Bounds.Max.Y || a.Bounds.Max.Y < b.Bounds.Min.Y ||
                    a.Bounds.Min.Z > b.Bounds.Max.Z || a.Bounds.Max.Z < b.Bounds.Min.Z) continue;
                pairs.Add(new BodyPair(a.Body, b.Body));
            }
        }
        foreach (var plane in planes)
        foreach (var item in finite)
            if (ShouldCollide(plane, item.Body) &&
                !(plane.InverseMass == 0 && item.Body.InverseMass == 0 && !plane.IsTrigger && !item.Body.IsTrigger))
                pairs.Add(new BodyPair(plane, item.Body));
        return pairs;
    }

    private static bool ShouldCollide(Body a, Body b) =>
        !a.IgnoredLayers.Contains(b.Layer) && !b.IgnoredLayers.Contains(a.Layer);

    private static bool BoundsMayOverlap(Body a, Body b)
    {
        // Infinite planes cannot use a finite AABB. Let narrow phase decide.
        if (a.Shape == ShapeKind.Plane || b.Shape == ShapeKind.Plane) return true;
        var aa = BodyBounds(a);
        var bb = BodyBounds(b);
        return aa.Min.X <= bb.Max.X && aa.Max.X >= bb.Min.X &&
               aa.Min.Y <= bb.Max.Y && aa.Max.Y >= bb.Min.Y &&
               aa.Min.Z <= bb.Max.Z && aa.Max.Z >= bb.Min.Z;
    }

    private static Bounds BodyBounds(Body body)
    {
        Vector3 extent;
        switch (body.Shape)
        {
            case ShapeKind.Sphere:
                extent = new Vector3(body.Radius);
                break;
            case ShapeKind.Capsule:
                BoxAxes(body, out _, out var up, out _);
                extent = new Vector3(MathF.Abs(up.X), MathF.Abs(up.Y), MathF.Abs(up.Z)) * body.CapsuleHalfHeight + new Vector3(body.Radius);
                break;
            case ShapeKind.Box:
                BoxAxes(body, out var x, out var y, out var z);
                var half = body.Size * 0.5f;
                extent = new Vector3(
                    MathF.Abs(x.X) * half.X + MathF.Abs(y.X) * half.Y + MathF.Abs(z.X) * half.Z,
                    MathF.Abs(x.Y) * half.X + MathF.Abs(y.Y) * half.Y + MathF.Abs(z.Y) * half.Z,
                    MathF.Abs(x.Z) * half.X + MathF.Abs(y.Z) * half.Y + MathF.Abs(z.Z) * half.Z);
                break;
            default:
                extent = new Vector3(float.MaxValue * 0.25f);
                break;
        }
        return new Bounds(body.Position - extent, body.Position + extent);
    }

    private static bool Swap(bool hit, Body a, Body b, Contact swapped, out Contact contact)
    {
        contact = hit ? new Contact(a, b, -swapped.Normal, swapped.Penetration, swapped.Points) : default;
        return hit;
    }

    private static bool SphereSphere(Body a, Body b, out Contact contact)
    {
        var delta = b.Position - a.Position;
        float radius = a.Radius + b.Radius;
        float distanceSq = delta.LengthSquared();
        if (distanceSq >= radius * radius) { contact = default; return false; }
        float distance = MathF.Sqrt(distanceSq);
        var normal = distance > 0.00001f ? delta / distance : Vector3.UnitY;
        var point = a.Position + normal * (a.Radius - (radius - distance) * 0.5f);
        contact = new Contact(a, b, normal, radius - distance, [point]);
        return true;
    }

    // Convention (matches every other pair below): normal points from A
    // toward B. The plane's own outward normal points away from its surface
    // toward the sphere sitting on it — i.e. from B(plane) to A(sphere) — so
    // it must be negated here to point A(sphere)->B(plane) instead.
    private static bool SpherePlane(Body a, Body b, out Contact contact)
    {
        float distance = Vector3.Dot(a.Position, b.PlaneNormal) - b.PlaneOffset;
        if (distance >= a.Radius) { contact = default; return false; }
        var point = a.Position - b.PlaneNormal * distance;
        contact = new Contact(a, b, -b.PlaneNormal, a.Radius - distance, [point]);
        return true;
    }

    private static bool SphereBox(Body a, Body b, out Contact contact)
    {
        var half = b.Size * 0.5f;
        var local = Vector3.Transform(a.Position - b.Position, Quaternion.Inverse(b.Rotation));
        var closest = Vector3.Clamp(local, -half, half);
        var sphereToBox = closest - local;
        float distanceSq = sphereToBox.LengthSquared();
        if (distanceSq > a.Radius * a.Radius) { contact = default; return false; }

        if (distanceSq > 0.0000001f)
        {
            float distance = MathF.Sqrt(distanceSq);
            var normal = Vector3.Transform(sphereToBox / distance, b.Rotation);
            var point = b.Position + Vector3.Transform(closest, b.Rotation);
            contact = new Contact(a, b, normal, a.Radius - distance, [point]);
            return true;
        }

        // Sphere centre is inside the box: push out through the nearest face.
        float dx = half.X - MathF.Abs(local.X), dy = half.Y - MathF.Abs(local.Y), dz = half.Z - MathF.Abs(local.Z);
        Vector3 localNormal;
        float penetration;
        if (dx <= dy && dx <= dz)
        {
            localNormal = new Vector3(local.X < 0 ? -1 : 1, 0, 0);
            closest.X = local.X < 0 ? -half.X : half.X;
            penetration = a.Radius + dx;
        }
        else if (dy <= dz)
        {
            localNormal = new Vector3(0, local.Y < 0 ? -1 : 1, 0);
            closest.Y = local.Y < 0 ? -half.Y : half.Y;
            penetration = a.Radius + dy;
        }
        else
        {
            localNormal = new Vector3(0, 0, local.Z < 0 ? -1 : 1);
            closest.Z = local.Z < 0 ? -half.Z : half.Z;
            penetration = a.Radius + dz;
        }
        contact = new Contact(a, b, Vector3.Transform(-localNormal, b.Rotation), penetration,
            [b.Position + Vector3.Transform(closest, b.Rotation)]);
        return true;
    }

    private static bool BoxPlane(Body a, Body b, out Contact contact)
    {
        var corners = BoxCorners(a);
        float deepest = float.MaxValue;
        var points = new List<Vector3>();
        foreach (var corner in corners)
        {
            float distance = Vector3.Dot(corner, b.PlaneNormal) - b.PlaneOffset;
            deepest = MathF.Min(deepest, distance);
        }
        if (deepest >= 0) { contact = default; return false; }
        foreach (var corner in corners)
        {
            float distance = Vector3.Dot(corner, b.PlaneNormal) - b.PlaneOffset;
            if (distance <= deepest + 0.01f)
                points.Add(corner - b.PlaneNormal * distance);
        }
        contact = new Contact(a, b, -b.PlaneNormal, -deepest, points);
        return true;
    }

    private static bool CapsulePlane(Body a, Body b, out Contact contact)
    {
        var (p0, p1) = CapsuleSegment(a);
        float d0 = Vector3.Dot(p0, b.PlaneNormal) - b.PlaneOffset;
        float d1 = Vector3.Dot(p1, b.PlaneNormal) - b.PlaneOffset;
        float deepest = MathF.Min(d0, d1);
        if (deepest >= a.Radius) { contact = default; return false; }

        var points = new List<Vector3>();
        if (d0 - a.Radius <= deepest + 0.01f) points.Add(p0 - b.PlaneNormal * (d0 - a.Radius));
        // Keep both endpoints when a sideways capsule lies flat. Equal
        // endpoint heights are two distinct contact points, not a duplicate;
        // collapsing them to one under-constrains rotation and makes the
        // capsule rock/sink around one end.
        if (d1 - a.Radius <= deepest + 0.01f && Vector3.DistanceSquared(p0, p1) > 0.0000001f)
            points.Add(p1 - b.PlaneNormal * (d1 - a.Radius));
        contact = new Contact(a, b, -b.PlaneNormal, a.Radius - deepest, points);
        return true;
    }

    private static bool CapsuleSphere(Body a, Body b, out Contact contact)
    {
        var (p0, p1) = CapsuleSegment(a);
        var closest = ClosestPointOnSegment(b.Position, p0, p1);
        var delta = b.Position - closest;
        float radius = a.Radius + b.Radius;
        float distanceSq = delta.LengthSquared();
        if (distanceSq >= radius * radius) { contact = default; return false; }
        float distance = MathF.Sqrt(distanceSq);
        var normal = distance > 0.00001f ? delta / distance : Vector3.UnitY;
        var point = closest + normal * (a.Radius - (radius - distance) * 0.5f);
        contact = new Contact(a, b, normal, radius - distance, [point]);
        return true;
    }

    private static bool CapsuleCapsule(Body a, Body b, out Contact contact)
    {
        var (a0, a1) = CapsuleSegment(a);
        var (b0, b1) = CapsuleSegment(b);
        var (closestA, closestB) = ClosestPointsBetweenSegments(a0, a1, b0, b1);
        var delta = closestB - closestA;
        float radius = a.Radius + b.Radius;
        float distanceSq = delta.LengthSquared();
        if (distanceSq >= radius * radius) { contact = default; return false; }
        float distance = MathF.Sqrt(distanceSq);
        var normal = distance > 0.00001f ? delta / distance : Vector3.UnitY;
        var point = closestA + normal * (a.Radius - (radius - distance) * 0.5f);
        contact = new Contact(a, b, normal, radius - distance, [point]);
        return true;
    }

    /// Treats the capsule's core segment against the box using the same
    /// closest-point approach as sphere-box, then applies the capsule radius.
    private static bool CapsuleBox(Body a, Body b, out Contact contact)
    {
        var (p0, p1) = CapsuleSegment(a);
        var half = b.Size * 0.5f;
        var invRotation = Quaternion.Inverse(b.Rotation);
        var local0 = Vector3.Transform(p0 - b.Position, invRotation);
        var local1 = Vector3.Transform(p1 - b.Position, invRotation);

        // Squared distance from a point travelling along a segment to an AABB
        // is convex. A bounded ternary solve finds the continuous closest
        // point, avoiding the gaps created by the prototype's seven samples.
        float lo = 0, hi = 1;
        for (int i = 0; i < 32; i++)
        {
            float t1 = lo + (hi - lo) / 3f;
            float t2 = hi - (hi - lo) / 3f;
            if (PointBoxDistanceSquared(Vector3.Lerp(local0, local1, t1), half) <=
                PointBoxDistanceSquared(Vector3.Lerp(local0, local1, t2), half)) hi = t2;
            else lo = t1;
        }
        float bestT = (lo + hi) * 0.5f;
        var bestLocal = Vector3.Lerp(local0, local1, bestT);
        var bestClosest = Vector3.Clamp(bestLocal, -half, half);
        float bestDistSq = Vector3.DistanceSquared(bestLocal, bestClosest);

        Vector3 normal; float penetration;
        var localToBox = bestClosest - bestLocal;
        if (bestDistSq > 0.0000001f)
        {
            float distance = MathF.Sqrt(bestDistSq);
            if (distance > a.Radius) { contact = default; return false; }
            normal = Vector3.Transform(localToBox / distance, b.Rotation);
            penetration = a.Radius - distance;
        }
        else
        {
            // Capsule axis point is inside the box: push out the nearest face.
            float dx = half.X - MathF.Abs(bestLocal.X), dy = half.Y - MathF.Abs(bestLocal.Y), dz = half.Z - MathF.Abs(bestLocal.Z);
            Vector3 localNormal;
            if (dx <= dy && dx <= dz) { localNormal = new Vector3(bestLocal.X < 0 ? -1 : 1, 0, 0); penetration = a.Radius + dx; }
            else if (dy <= dz) { localNormal = new Vector3(0, bestLocal.Y < 0 ? -1 : 1, 0); penetration = a.Radius + dy; }
            else { localNormal = new Vector3(0, 0, bestLocal.Z < 0 ? -1 : 1); penetration = a.Radius + dz; }
            normal = Vector3.Transform(-localNormal, b.Rotation);
        }

        // Contact points: project BOTH capsule endpoints onto the box along
        // the resolved normal and keep whichever are actually within
        // (roughly) contact range. A single point here — the old
        // behaviour — under-constrains rotation about the contact point, so
        // a capsule resting flat against a box face never fully stops
        // rocking/spinning in place; with both endpoints represented, the
        // solver has two levers instead of one and can actually cancel
        // rotation, the same fix that was needed for box-box stacking.
        var points = new List<Vector3>();
        foreach (var localEnd in new[] { local0, local1 })
        {
            var closest = Vector3.Clamp(localEnd, -half, half);
            float endDist = Vector3.Distance(localEnd, closest);
            if (endDist <= a.Radius + 0.02f)
                points.Add(b.Position + Vector3.Transform(closest, b.Rotation));
        }
        if (points.Count == 0)
            points.Add(b.Position + Vector3.Transform(bestClosest, b.Rotation));

        contact = new Contact(a, b, normal, penetration, points);
        return true;
    }

    private static float PointBoxDistanceSquared(Vector3 point, Vector3 half)
    {
        var closest = Vector3.Clamp(point, -half, half);
        return Vector3.DistanceSquared(point, closest);
    }

    // 3D SAT over 15 candidate axes: each box's 3 face normals, plus the 9
    // cross products of edge-axis pairs (needed for edge-edge contacts a
    // face-only test would miss).
    private static bool BoxBox(Body a, Body b, out Contact contact)
    {
        var delta = b.Position - a.Position;
        BoxAxes(a, out var ax, out var ay, out var az);
        BoxAxes(b, out var bx, out var by, out var bz);
        Span<Vector3> faceAxes = [ax, ay, az, bx, by, bz];

        float leastOverlap = float.MaxValue;
        var normal = Vector3.UnitX;
        bool leastIsEdgeEdge = false;

        foreach (var axis in faceAxes)
        {
            if (axis.LengthSquared() < 0.0000001f) continue;
            float distance = Vector3.Dot(delta, axis);
            float overlap = ProjectionRadius(a, axis) + ProjectionRadius(b, axis) - MathF.Abs(distance);
            if (overlap <= 0) { contact = default; return false; }
            if (overlap < leastOverlap)
            {
                leastOverlap = overlap;
                normal = distance < 0 ? -axis : axis;
                leastIsEdgeEdge = false;
            }
        }

        Span<Vector3> edgesA = [ax, ay, az];
        Span<Vector3> edgesB = [bx, by, bz];
        foreach (var ea in edgesA)
        foreach (var eb in edgesB)
        {
            var axis = Vector3.Cross(ea, eb);
            if (axis.LengthSquared() < 0.0001f) continue; // near-parallel edges: skip, face axes already cover this
            axis = Vector3.Normalize(axis);
            float distance = Vector3.Dot(delta, axis);
            float overlap = ProjectionRadius(a, axis) + ProjectionRadius(b, axis) - MathF.Abs(distance);
            if (overlap <= 0) { contact = default; return false; }
            if (overlap < leastOverlap)
            {
                leastOverlap = overlap;
                normal = distance < 0 ? -axis : axis;
                leastIsEdgeEdge = true;
            }
        }

        var points = leastIsEdgeEdge
            ? [(Support(a, normal) + Support(b, -normal)) * 0.5f]
            : BoxContactPoints(a, b, normal);
        contact = new Contact(a, b, normal, leastOverlap, points);
        return true;
    }

    private static void BoxAxes(Body body, out Vector3 x, out Vector3 y, out Vector3 z)
    {
        var m = Matrix4x4.CreateFromQuaternion(body.Rotation);
        x = new Vector3(m.M11, m.M12, m.M13);
        y = new Vector3(m.M21, m.M22, m.M23);
        z = new Vector3(m.M31, m.M32, m.M33);
    }

    private static float ProjectionRadius(Body body, Vector3 axis)
    {
        if (body.Shape == ShapeKind.Sphere) return body.Radius;
        if (body.Shape == ShapeKind.Capsule)
        {
            BoxAxes(body, out _, out var up, out _);
            return body.Radius + body.CapsuleHalfHeight * MathF.Abs(Vector3.Dot(axis, up));
        }
        BoxAxes(body, out var x, out var y, out var z);
        return body.Size.X * 0.5f * MathF.Abs(Vector3.Dot(axis, x)) +
               body.Size.Y * 0.5f * MathF.Abs(Vector3.Dot(axis, y)) +
               body.Size.Z * 0.5f * MathF.Abs(Vector3.Dot(axis, z));
    }

    private static Vector3 Support(Body body, Vector3 direction)
    {
        if (body.Shape == ShapeKind.Sphere)
            return body.Position + Vector3.Normalize(direction) * body.Radius;
        if (body.Shape == ShapeKind.Capsule)
        {
            BoxAxes(body, out _, out var up, out _);
            float side = MathF.Sign(Vector3.Dot(direction, up));
            var center = body.Position + up * (body.CapsuleHalfHeight * side);
            return center + Vector3.Normalize(direction) * body.Radius;
        }
        BoxAxes(body, out var x, out var y, out var z);
        float dx = Vector3.Dot(direction, x), dy = Vector3.Dot(direction, y), dz = Vector3.Dot(direction, z);
        float sx = MathF.Abs(dx) < 0.00001f ? 0 : MathF.CopySign(body.Size.X * 0.5f, dx);
        float sy = MathF.Abs(dy) < 0.00001f ? 0 : MathF.CopySign(body.Size.Y * 0.5f, dy);
        float sz = MathF.Abs(dz) < 0.00001f ? 0 : MathF.CopySign(body.Size.Z * 0.5f, dz);
        return body.Position + x * sx + y * sy + z * sz;
    }

    // Sutherland-Hodgman reference/incident face clipping: whichever box's
    // face normal is most aligned with the collision normal becomes the
    // reference face; the other box's most-anti-aligned face is the incident
    // face. The incident face's 4 corners are clipped against the reference
    // face's 4 side planes, then any clipped points still behind the
    // reference face become contact points. This produces up to 4 points for
    // a flat face-to-face rest (instead of 1), which is what actually lets a
    // box stack stop rocking/jittering and go to sleep — a single averaged
    // point under-constrains rotation and requires ongoing correction every
    // frame, which looks like a persistent tiny "clip" even though the boxes
    // never truly interpenetrate.
    private static List<Vector3> BoxContactPoints(Body a, Body b, Vector3 normal)
    {
        var (refBody, refAxisIndex, refSign, incBody) = PickReferenceFace(a, b, normal);
        BoxAxes(refBody, out var rx, out var ry, out var rz);
        var refAxis = refAxisIndex switch { 0 => rx, 1 => ry, _ => rz };
        var refNormal = refAxis * refSign;

        var incFace = IncidentFace(incBody, refNormal);
        var clipped = ClipFaceAgainstBox(incFace, refBody, refAxisIndex);

        float refHalfExtent = refAxisIndex switch { 0 => refBody.Size.X, 1 => refBody.Size.Y, _ => refBody.Size.Z } * 0.5f;
        var refCenter = refBody.Position + refNormal * refHalfExtent;
        var points = new List<Vector3>();
        foreach (var p in clipped)
        {
            float depth = Vector3.Dot(refCenter - p, refNormal);
            if (depth >= -0.01f) points.Add(p - refNormal * MathF.Min(depth, 0));
        }
        if (points.Count == 0)
        {
            // Degenerate clip (shouldn't normally happen once SAT confirms
            // overlap) — fall back to the single-point approximation rather
            // than leaving the contact with no points to resolve.
            return [(Support(a, normal) + Support(b, -normal)) * 0.5f];
        }
        return points;
    }

    /// Picks whichever box's local axis (0=X, 1=Y, 2=Z) is most aligned
    /// (positively or negatively) with the collision normal as the reference
    /// face. Returns an axis index rather than a Vector3 so later steps don't
    /// need float-equality comparisons to identify which axis was chosen.
    private static (Body RefBody, int AxisIndex, float Sign, Body IncBody) PickReferenceFace(Body a, Body b, Vector3 normal)
    {
        BoxAxes(a, out var ax, out var ay, out var az);
        BoxAxes(b, out var bx, out var by, out var bz);
        Span<Vector3> aAxes = [ax, ay, az];
        Span<Vector3> bAxes = [bx, by, bz];

        float bestDot = -1; int bestIndex = 0; float bestSign = 1; bool useA = true;
        for (int i = 0; i < 3; i++)
        {
            float d = Vector3.Dot(aAxes[i], normal);
            if (MathF.Abs(d) > bestDot) { bestDot = MathF.Abs(d); bestIndex = i; bestSign = MathF.Sign(d); useA = true; }
        }
        for (int i = 0; i < 3; i++)
        {
            // b's outward normal at collision-normal-facing side is -normal
            // relative to a->b convention, so align against -normal here.
            float d = Vector3.Dot(bAxes[i], -normal);
            if (MathF.Abs(d) > bestDot) { bestDot = MathF.Abs(d); bestIndex = i; bestSign = MathF.Sign(d); useA = false; }
        }

        return useA ? (a, bestIndex, bestSign, b) : (b, bestIndex, bestSign, a);
    }

    /// The 4 corners of the box's face whose outward normal is closest to
    /// `direction` (the reference face's outward normal, pointing at this box).
    private static Vector3[] IncidentFace(Body body, Vector3 direction)
    {
        BoxAxes(body, out var x, out var y, out var z);
        var half = body.Size * 0.5f;
        Span<(Vector3 Axis, float Extent, Vector3 U, float UExtent, Vector3 V, float VExtent)> faces =
        [
            (x, half.X, y, half.Y, z, half.Z), (-x, half.X, y, half.Y, z, half.Z),
            (y, half.Y, x, half.X, z, half.Z), (-y, half.Y, x, half.X, z, half.Z),
            (z, half.Z, x, half.X, y, half.Y), (-z, half.Z, x, half.X, y, half.Y),
        ];
        float best = float.MinValue;
        var chosen = faces[0];
        foreach (var f in faces)
        {
            float d = Vector3.Dot(f.Axis, direction);
            if (d > best) { best = d; chosen = f; }
        }
        var center = body.Position + chosen.Axis * chosen.Extent;
        return
        [
            center - chosen.U * chosen.UExtent - chosen.V * chosen.VExtent,
            center + chosen.U * chosen.UExtent - chosen.V * chosen.VExtent,
            center + chosen.U * chosen.UExtent + chosen.V * chosen.VExtent,
            center - chosen.U * chosen.UExtent + chosen.V * chosen.VExtent,
        ];
    }

    /// Clips a quad (the incident face) against the reference box's 4 side
    /// planes (the two axes perpendicular to the reference axis index),
    /// Sutherland-Hodgman style.
    private static List<Vector3> ClipFaceAgainstBox(Vector3[] face, Body refBody, int refAxisIndex)
    {
        BoxAxes(refBody, out var x, out var y, out var z);
        var half = refBody.Size * 0.5f;
        Vector3 u, v; float ue, ve;
        switch (refAxisIndex)
        {
            case 0: u = y; v = z; ue = half.Y; ve = half.Z; break;
            case 1: u = x; v = z; ue = half.X; ve = half.Z; break;
            default: u = x; v = y; ue = half.X; ve = half.Y; break;
        }

        var poly = new List<Vector3>(face);
        poly = ClipAgainstPlane(poly, refBody.Position + u * ue, u);
        poly = ClipAgainstPlane(poly, refBody.Position - u * ue, -u);
        poly = ClipAgainstPlane(poly, refBody.Position + v * ve, v);
        poly = ClipAgainstPlane(poly, refBody.Position - v * ve, -v);
        return poly;
    }

    private static List<Vector3> ClipAgainstPlane(List<Vector3> poly, Vector3 planePoint, Vector3 planeNormal)
    {
        if (poly.Count == 0) return poly;
        var output = new List<Vector3>();
        for (int i = 0; i < poly.Count; i++)
        {
            var current = poly[i];
            var previous = poly[(i - 1 + poly.Count) % poly.Count];
            float currentDist = Vector3.Dot(current - planePoint, planeNormal);
            float previousDist = Vector3.Dot(previous - planePoint, planeNormal);
            bool currentInside = currentDist <= 0;
            bool previousInside = previousDist <= 0;
            if (currentInside)
            {
                if (!previousInside)
                {
                    float t = previousDist / (previousDist - currentDist);
                    output.Add(Vector3.Lerp(previous, current, t));
                }
                output.Add(current);
            }
            else if (previousInside)
            {
                float t = previousDist / (previousDist - currentDist);
                output.Add(Vector3.Lerp(previous, current, t));
            }
        }
        return output;
    }

    private static Vector3[] BoxCorners(Body body)
    {
        BoxAxes(body, out var x, out var y, out var z);
        x *= body.Size.X * 0.5f; y *= body.Size.Y * 0.5f; z *= body.Size.Z * 0.5f;
        var p = body.Position;
        return
        [
            p - x - y - z, p + x - y - z, p + x + y - z, p - x + y - z,
            p - x - y + z, p + x - y + z, p + x + y + z, p - x + y + z,
        ];
    }

    private static (Vector3 P0, Vector3 P1) CapsuleSegment(Body body)
    {
        BoxAxes(body, out _, out var up, out _);
        return (body.Position - up * body.CapsuleHalfHeight, body.Position + up * body.CapsuleHalfHeight);
    }

    private static Vector3 ClosestPointOnSegment(Vector3 point, Vector3 p0, Vector3 p1)
    {
        var segment = p1 - p0;
        float lengthSq = segment.LengthSquared();
        if (lengthSq < 0.0000001f) return p0;
        float t = Math.Clamp(Vector3.Dot(point - p0, segment) / lengthSq, 0f, 1f);
        return p0 + segment * t;
    }

    // Closest points between two line segments (standard bilinear-clamp solve).
    private static (Vector3 A, Vector3 B) ClosestPointsBetweenSegments(Vector3 p0, Vector3 p1, Vector3 q0, Vector3 q1)
    {
        var d1 = p1 - p0; var d2 = q1 - q0; var r = p0 - q0;
        float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r);
        float s, t;
        if (a < 0.0000001f && e < 0.0000001f) { s = 0; t = 0; }
        else if (a < 0.0000001f) { s = 0; t = Math.Clamp(f / e, 0f, 1f); }
        else
        {
            float c = Vector3.Dot(d1, r);
            if (e < 0.0000001f) { t = 0; s = Math.Clamp(-c / a, 0f, 1f); }
            else
            {
                float b = Vector3.Dot(d1, d2);
                float denom = a * e - b * b;
                s = denom > 0.0000001f ? Math.Clamp((b * f - c * e) / denom, 0f, 1f) : 0f;
                t = (b * s + f) / e;
                if (t < 0) { t = 0; s = Math.Clamp(-c / a, 0f, 1f); }
                else if (t > 1) { t = 1; s = Math.Clamp((b - c) / a, 0f, 1f); }
            }
        }
        return (p0 + d1 * s, q0 + d2 * t);
    }

    // ── Mutators ──────────────────────────────────────────────────────────────

    public static object? SetVelocity(List<object?> a)
    {
        var body = BodyAt(a, "Physics3D.set_velocity");
        body.Velocity = new Vector3(Number(a, 2, 0), Number(a, 3, 0), Number(a, 4, 0));
        Wake(body);
        return null;
    }

    public static object? SetPosition(List<object?> a)
    {
        var body = BodyAt(a, "Physics3D.set_position");
        body.Position = new Vector3(Number(a, 2, 0), Number(a, 3, 0), Number(a, 4, 0));
        Wake(body);
        return null;
    }

    public static object? SetRotation(List<object?> a)
    {
        var body = BodyAt(a, "Physics3D.set_rotation");
        body.Rotation = QuaternionFromDegrees(Number(a, 2, 0), Number(a, 3, 0), Number(a, 4, 0));
        Wake(body);
        return null;
    }

    public static object? SetAngularVelocity(List<object?> a)
    {
        var body = BodyAt(a, "Physics3D.set_angular_velocity");
        if (!body.RotationLocked)
            body.AngularVelocity = new Vector3(
                DegreesToRadians(Number(a, 2, 0)), DegreesToRadians(Number(a, 3, 0)), DegreesToRadians(Number(a, 4, 0)));
        Wake(body);
        return null;
    }

    public static object? LockRotation(List<object?> a)
    {
        var body = BodyAt(a, "Physics3D.lock_rotation");
        body.RotationLocked = a.Count > 2 ? IsTruthy(a[2]) : true;
        if (body.RotationLocked) body.AngularVelocity = Vector3.Zero;
        return null;
    }

    public static object? SetDamping(List<object?> a)
    {
        var body = BodyAt(a, "Physics3D.set_damping");
        body.LinearDamping = MathF.Max(0, Number(a, 2, body.LinearDamping));
        body.AngularDamping = MathF.Max(0, Number(a, 3, body.AngularDamping));
        return null;
    }

    public static object? SetMaterial(List<object?> a)
    {
        var body = BodyAt(a, "Physics3D.material");
        body.Restitution = Math.Clamp(Number(a, 2, body.Restitution), 0, 1);
        body.Friction = Math.Max(0, Number(a, 3, body.Friction));
        return null;
    }

    public static object? TuneCharacter(List<object?> a)
    {
        var c = CharacterAt(a, "Physics3D.character_tune");
        c.Speed = Math.Max(0, Number(a, 2, c.Speed));
        c.JumpSpeed = Math.Max(0, Number(a, 3, c.JumpSpeed));
        c.AirControl = Math.Clamp(Number(a, 4, c.AirControl), 0, 1);
        return null;
    }

    public static object? ApplyForce(List<object?> a)
    {
        var body = BodyAt(a, "Physics3D.apply_force");
        if (body.Kind == BodyKind.Dynamic)
        {
            body.Force += new Vector3(Number(a, 2, 0), Number(a, 3, 0), Number(a, 4, 0));
            Wake(body);
        }
        return null;
    }

    public static object? ApplyImpulse(List<object?> a)
    {
        var body = BodyAt(a, "Physics3D.apply_impulse");
        if (body.Kind == BodyKind.Dynamic)
        {
            body.Velocity += new Vector3(Number(a, 2, 0), Number(a, 3, 0), Number(a, 4, 0)) * body.InverseMass;
            Wake(body);
        }
        return null;
    }

    public static object? ApplyImpulseAt(List<object?> a)
    {
        var body = BodyAt(a, "Physics3D.apply_impulse_at");
        if (body.Kind == BodyKind.Dynamic)
        {
            var impulse = new Vector3(Number(a, 2, 0), Number(a, 3, 0), Number(a, 4, 0));
            var point = new Vector3(
                Number(a, 5, body.Position.X), Number(a, 6, body.Position.Y), Number(a, 7, body.Position.Z));
            body.Velocity += impulse * body.InverseMass;
            if (!body.RotationLocked)
            {
                var r = point - body.Position;
                var angularImpulse = Vector3.Cross(r, impulse);
                body.AngularVelocity += ApplyInverseInertia(body, angularImpulse);
            }
            Wake(body);
        }
        return null;
    }

    public static object? ApplyTorque(List<object?> a)
    {
        var body = BodyAt(a, "Physics3D.apply_torque");
        if (body.Kind == BodyKind.Dynamic && !body.RotationLocked)
        {
            body.Torque += new Vector3(Number(a, 2, 0), Number(a, 3, 0), Number(a, 4, 0));
            Wake(body);
        }
        return null;
    }

    public static object? ApplyAngularImpulse(List<object?> a)
    {
        var body = BodyAt(a, "Physics3D.apply_angular_impulse");
        if (body.Kind == BodyKind.Dynamic && !body.RotationLocked)
        {
            var angularImpulse = new Vector3(Number(a, 2, 0), Number(a, 3, 0), Number(a, 4, 0));
            body.AngularVelocity += ApplyInverseInertia(body, angularImpulse);
            Wake(body);
        }
        return null;
    }

    public static object? WakeBody(List<object?> a) { Wake(BodyAt(a, "Physics3D.wake")); return null; }
    public static object? IsSleeping(List<object?> a) => !BodyAt(a, "Physics3D.is_sleeping").Awake;

    public static object? IsColliding(List<object?> a) => BodyAt(a, "Physics3D.is_colliding").Contacts.Count > 0;
    public static object? IsGrounded(List<object?> a) => BodyAt(a, "Physics3D.is_grounded").Grounded;

    public static object? Contacts(List<object?> a) =>
        BodyAt(a, "Physics3D.contacts").Contacts.Order().Select(id => (object?)(double)id).ToList();

    public static object? BodyPosition(List<object?> a)
    {
        var body = BodyAt(a, "Physics3D.position");
        return new List<object?> { (double)body.Position.X, (double)body.Position.Y, (double)body.Position.Z };
    }

    public static object? BodyVelocity(List<object?> a)
    {
        var body = BodyAt(a, "Physics3D.velocity");
        return new List<object?> { (double)body.Velocity.X, (double)body.Velocity.Y, (double)body.Velocity.Z };
    }

    public static object? BodyTransform(List<object?> a)
    {
        var body = BodyAt(a, "Physics3D.transform");
        return new List<object?>
        {
            (double)body.Position.X, (double)body.Position.Y, (double)body.Position.Z,
            (double)body.Rotation.X, (double)body.Rotation.Y, (double)body.Rotation.Z, (double)body.Rotation.W,
        };
    }

    public static object? OverlapSphere(List<object?> a)
    {
        var world = WorldAt(a, "Physics3D.overlap_sphere");
        var probe = new Body
        {
            Id = -1, Kind = BodyKind.Kinematic, Shape = ShapeKind.Sphere,
            Position = new Vector3(Number(a, 1, 0), Number(a, 2, 0), Number(a, 3, 0)),
            Radius = Number(a, 4, 1),
        };
        if (probe.Radius <= 0) throw new MakoError("Physics3D.overlap_sphere(): radius must be greater than 0");
        string? onlyLayer = a.Count > 5 ? Text(a, 5, "") : null;
        var hits = new List<object?>();
        foreach (var body in world.Bodies)
            if (body != null && (onlyLayer == null || body.Layer.Equals(onlyLayer, StringComparison.OrdinalIgnoreCase)) &&
                BoundsMayOverlap(probe, body) && TryCollide(probe, body, out _))
                hits.Add((double)body.Id);
        return hits;
    }

    public static object? Raycast(List<object?> a)
    {
        var world = WorldAt(a, "Physics3D.raycast");
        var origin = new Vector3(Number(a, 1, 0), Number(a, 2, 0), Number(a, 3, 0));
        var direction = new Vector3(Number(a, 4, 0), Number(a, 5, 0), Number(a, 6, 0));
        float maxDistance = Number(a, 7, 1000);
        string? onlyLayer = a.Count > 8 ? Text(a, 8, "") : null;
        if (direction.LengthSquared() < 0.0000001f) throw new MakoError("Physics3D.raycast(): direction must not be zero");
        if (maxDistance <= 0) throw new MakoError("Physics3D.raycast(): distance must be greater than 0");
        direction = Vector3.Normalize(direction);

        Body? closestBody = null;
        float closestDistance = maxDistance;
        var closestNormal = Vector3.Zero;
        foreach (var body in world.Bodies)
        {
            if (body == null || (onlyLayer != null && !body.Layer.Equals(onlyLayer, StringComparison.OrdinalIgnoreCase)) ||
                !RayBody(body, origin, direction, out float distance, out var normal)) continue;
            if (distance < 0 || distance > closestDistance) continue;
            closestBody = body;
            closestDistance = distance;
            closestNormal = normal;
        }
        if (closestBody == null) return null;
        var point = origin + direction * closestDistance;
        return new Dictionary<string, object?>
        {
            ["body"] = (double)closestBody.Id, ["distance"] = (double)closestDistance,
            ["x"] = (double)point.X, ["y"] = (double)point.Y, ["z"] = (double)point.Z,
            ["nx"] = (double)closestNormal.X, ["ny"] = (double)closestNormal.Y, ["nz"] = (double)closestNormal.Z,
        };
    }

    private static bool RayBody(Body body, Vector3 origin, Vector3 direction, out float distance, out Vector3 normal)
    {
        switch (body.Shape)
        {
            case ShapeKind.Sphere:
                return RaySphere(origin, direction, body.Position, body.Radius, out distance, out normal);
            case ShapeKind.Plane:
                float denom = Vector3.Dot(direction, body.PlaneNormal);
                if (MathF.Abs(denom) < 0.000001f) { distance = 0; normal = default; return false; }
                distance = (body.PlaneOffset - Vector3.Dot(origin, body.PlaneNormal)) / denom;
                normal = denom < 0 ? body.PlaneNormal : -body.PlaneNormal;
                return distance >= 0;
            case ShapeKind.Box:
                return RayBox(body, origin, direction, out distance, out normal);
            case ShapeKind.Capsule:
                return RayCapsule(body, origin, direction, out distance, out normal);
            default:
                distance = 0; normal = default; return false;
        }
    }

    private static bool RaySphere(Vector3 origin, Vector3 direction, Vector3 center, float radius, out float distance, out Vector3 normal)
    {
        var offset = origin - center;
        float b = Vector3.Dot(offset, direction);
        float c = offset.LengthSquared() - radius * radius;
        float h = b * b - c;
        if (h < 0) { distance = 0; normal = default; return false; }
        distance = -b - MathF.Sqrt(h);
        if (distance < 0) distance = -b + MathF.Sqrt(h);
        if (distance < 0) { normal = default; return false; }
        normal = Vector3.Normalize(origin + direction * distance - center);
        return true;
    }

    private static bool RayBox(Body body, Vector3 origin, Vector3 direction, out float distance, out Vector3 normal)
    {
        var inverse = Quaternion.Conjugate(body.Rotation);
        var localOrigin = Vector3.Transform(origin - body.Position, inverse);
        var localDirection = Vector3.Transform(direction, inverse);
        var half = body.Size * 0.5f;
        float near = 0, far = float.MaxValue;
        var nearNormal = Vector3.Zero;
        for (int axis = 0; axis < 3; axis++)
        {
            float o = axis == 0 ? localOrigin.X : axis == 1 ? localOrigin.Y : localOrigin.Z;
            float d = axis == 0 ? localDirection.X : axis == 1 ? localDirection.Y : localDirection.Z;
            float h = axis == 0 ? half.X : axis == 1 ? half.Y : half.Z;
            if (MathF.Abs(d) < 0.000001f)
            {
                if (o < -h || o > h) { distance = 0; normal = default; return false; }
                continue;
            }
            float t1 = (-h - o) / d, t2 = (h - o) / d;
            float sign = -1;
            if (t1 > t2) { (t1, t2) = (t2, t1); sign = 1; }
            if (t1 > near)
            {
                near = t1;
                nearNormal = axis == 0 ? new Vector3(sign, 0, 0) : axis == 1 ? new Vector3(0, sign, 0) : new Vector3(0, 0, sign);
            }
            far = MathF.Min(far, t2);
            if (near > far) { distance = 0; normal = default; return false; }
        }
        distance = near;
        normal = Vector3.Transform(nearNormal, body.Rotation);
        return far >= 0;
    }

    private static bool RayCapsule(Body body, Vector3 origin, Vector3 direction, out float distance, out Vector3 normal)
    {
        var (p0, p1) = CapsuleSegment(body);
        var axis = p1 - p0;
        var offset = origin - p0;
        float axisSq = Vector3.Dot(axis, axis);
        float axisRay = Vector3.Dot(axis, direction);
        float axisOffset = Vector3.Dot(axis, offset);
        float rayOffset = Vector3.Dot(direction, offset);
        float offsetSq = Vector3.Dot(offset, offset);
        float aa = axisSq - axisRay * axisRay;
        float bb = axisSq * rayOffset - axisOffset * axisRay;
        float cc = axisSq * offsetSq - axisOffset * axisOffset - body.Radius * body.Radius * axisSq;
        float best = float.MaxValue;
        var bestNormal = Vector3.Zero;
        float discriminant = bb * bb - aa * cc;
        if (MathF.Abs(aa) > 0.000001f && discriminant >= 0)
        {
            float t = (-bb - MathF.Sqrt(discriminant)) / aa;
            float along = axisOffset + t * axisRay;
            if (t >= 0 && along >= 0 && along <= axisSq)
            {
                var center = p0 + axis * (along / axisSq);
                best = t;
                bestNormal = Vector3.Normalize(origin + direction * t - center);
            }
        }
        if (RaySphere(origin, direction, p0, body.Radius, out float cap0, out var normal0) && cap0 < best)
        { best = cap0; bestNormal = normal0; }
        if (RaySphere(origin, direction, p1, body.Radius, out float cap1, out var normal1) && cap1 < best)
        { best = cap1; bestNormal = normal1; }
        distance = best;
        normal = bestNormal;
        return best < float.MaxValue;
    }

    public static object? BodyInfo(List<object?> a)
    {
        var body = TryBodyAt(a, "Physics3D.body_info");
        if (body == null) return null;
        var (pitch, yaw, roll) = QuaternionToDegrees(body.Rotation);
        var angDeg = new Vector3(RadiansToDegrees(body.AngularVelocity.X), RadiansToDegrees(body.AngularVelocity.Y), RadiansToDegrees(body.AngularVelocity.Z));
        var info = new Dictionary<string, object?>
        {
            ["id"] = (double)body.Id,
            ["type"] = body.Kind.ToString().ToLowerInvariant(),
            ["shape"] = body.Shape.ToString().ToLowerInvariant(),
            ["x"] = (double)body.Position.X, ["y"] = (double)body.Position.Y, ["z"] = (double)body.Position.Z,
            ["vx"] = (double)body.Velocity.X, ["vy"] = (double)body.Velocity.Y, ["vz"] = (double)body.Velocity.Z,
            ["pitch"] = (double)pitch, ["yaw"] = (double)yaw, ["roll"] = (double)roll,
            // qx/qy/qz/qw: the same orientation as pitch/yaw/roll, but as a
            // raw quaternion — use these (with Mako3D.cube_rot_q / a future
            // sphere_rot_q) for rendering. Euler angles (pitch/yaw/roll) are
            // for human-readable inspection only: extracting them involves
            // atan2/asin, which can snap 180 degrees between one frame and
            // the next for a body that's genuinely tumbling continuously
            // (not a rendering bug, just what Euler extraction does near its
            // wraparound points) — the quaternion has no such discontinuity.
            ["qx"] = (double)body.Rotation.X, ["qy"] = (double)body.Rotation.Y,
            ["qz"] = (double)body.Rotation.Z, ["qw"] = (double)body.Rotation.W,
            ["angular_velocity_x"] = (double)angDeg.X, ["angular_velocity_y"] = (double)angDeg.Y, ["angular_velocity_z"] = (double)angDeg.Z,
            ["mass"] = (double)body.Mass,
            ["rotation_locked"] = body.RotationLocked,
            ["sleeping"] = body.Kind == BodyKind.Dynamic && !body.Awake,
            ["linear_damping"] = (double)body.LinearDamping,
            ["angular_damping"] = (double)body.AngularDamping,
            ["bounce"] = (double)body.Restitution,
            ["friction"] = (double)body.Friction,
            ["colliding"] = body.Contacts.Count > 0,
            ["grounded"] = body.Grounded,
            ["layer"] = body.Layer,
            ["trigger"] = body.IsTrigger,
            ["triggered"] = body.IsTrigger && (body.Contacts.Count > 0 || body.CharacterTriggered),
        };
        switch (body.Shape)
        {
            case ShapeKind.Sphere:
                info["radius"] = (double)body.Radius;
                break;
            case ShapeKind.Box:
                info["width"] = (double)body.Size.X; info["height"] = (double)body.Size.Y; info["depth"] = (double)body.Size.Z;
                break;
            case ShapeKind.Capsule:
                info["radius"] = (double)body.Radius;
                info["height"] = (double)((body.CapsuleHalfHeight + body.Radius) * 2f);
                break;
            case ShapeKind.Plane:
                info["nx"] = (double)body.PlaneNormal.X; info["ny"] = (double)body.PlaneNormal.Y; info["nz"] = (double)body.PlaneNormal.Z;
                info["offset"] = (double)body.PlaneOffset;
                break;
        }
        return info;
    }

    // ── MAKO dispatch and argument helpers ───────────────────────────────────

    private static World WorldAt(List<object?> a, string fn)
    {
        int id = Handle(a, 0, fn);
        if (id < 0 || id >= Worlds.Count || Worlds[id] == null)
            throw new MakoError($"{fn}(): invalid world handle {id}");
        return Worlds[id]!;
    }

    private static Body? TryBodyAt(List<object?> a, string fn)
    {
        var world = WorldAt(a, fn);
        int id = Handle(a, 1, fn);
        return id >= 0 && id < world.Bodies.Count ? world.Bodies[id] : null;
    }

    private static Body BodyAt(List<object?> a, string fn) =>
        TryBodyAt(a, fn) ?? throw new MakoError($"{fn}(): invalid body handle");

    private static Body BodyById(World world, int id, string fn) =>
        id >= 0 && id < world.Bodies.Count && world.Bodies[id] is { } body
            ? body
            : throw new MakoError($"{fn}(): invalid body handle {id}");

    private static Spring SpringAt(List<object?> a, string fn)
    {
        var world = WorldAt(a, fn);
        int id = Handle(a, 1, fn);
        if (id < 0 || id >= world.Springs.Count || world.Springs[id] is not { } spring)
            throw new MakoError($"{fn}(): invalid spring handle {id}");
        return spring;
    }

    private static int Handle(List<object?> a, int index, string fn)
    {
        if (a.Count <= index) throw new MakoError($"{fn}(): missing handle argument");
        try { return (int)Convert.ToDouble(a[index]); }
        catch { throw new MakoError($"{fn}(): handle must be a number"); }
    }

    private static float Number(List<object?> a, int index, float fallback)
    {
        if (a.Count <= index) return fallback;
        try { return (float)Convert.ToDouble(a[index]); }
        catch { throw new MakoError("Physics3D: expected a number"); }
    }

    private static string Text(List<object?> a, int index, string fallback) =>
        a.Count > index && a[index] != null ? a[index]!.ToString() ?? fallback : fallback;

    private static bool IsTruthy(object? v) => v switch
    {
        null => false,
        bool b => b,
        double d => d != 0,
        _ => true,
    };

    private static float DegreesToRadians(float degrees) => degrees * MathF.PI / 180f;
    private static float RadiansToDegrees(float radians) => radians * 180f / MathF.PI;

    private static Quaternion QuaternionFromDegrees(float pitchDeg, float yawDeg, float rollDeg) =>
        Quaternion.CreateFromYawPitchRoll(DegreesToRadians(yawDeg), DegreesToRadians(pitchDeg), DegreesToRadians(rollDeg));

    private static (float Pitch, float Yaw, float Roll) QuaternionToDegrees(Quaternion q)
    {
        // Quaternion -> Euler extraction matched to .NET's own axis convention
        // for Quaternion.CreateFromYawPitchRoll (X=pitch axis, Y=yaw axis,
        // Z=roll axis), verified against it directly so round-tripping
        // set_rotation(...) -> body_info() is exact for single-axis rotations.
        // Combined multi-axis rotations near gimbal lock will drift, which is
        // an inherent property of Euler angles, not a bug in this extraction.
        float sinxCosy = 2 * (q.W * q.X + q.Y * q.Z);
        float cosxCosy = 1 - 2 * (q.X * q.X + q.Y * q.Y);
        float pitch = MathF.Atan2(sinxCosy, cosxCosy);

        float siny = 2 * (q.W * q.Y - q.Z * q.X);
        float yaw = MathF.Abs(siny) >= 1 ? MathF.CopySign(MathF.PI / 2, siny) : MathF.Asin(siny);

        float sinzCosy = 2 * (q.W * q.Z + q.X * q.Y);
        float coszCosy = 1 - 2 * (q.Y * q.Y + q.Z * q.Z);
        float roll = MathF.Atan2(sinzCosy, coszCosy);

        return (RadiansToDegrees(pitch), RadiansToDegrees(yaw), RadiansToDegrees(roll));
    }

    public static readonly Dictionary<string, Func<List<object?>, object?>> Funcs = new()
    {
        ["world"] = CreateWorld, ["backend"] = BackendInfo, ["destroy_world"] = DestroyWorld, ["clear"] = ClearWorld,
        ["box"] = CreateBox, ["sphere"] = CreateSphere, ["capsule"] = CreateCapsule,
        ["static_box"] = CreateStaticBox, ["static_sphere"] = CreateStaticSphere,
        ["static_capsule"] = CreateStaticCapsule, ["floor"] = CreateFloor, ["plane"] = CreatePlane,
        ["moving_box"] = CreateMovingBox, ["moving_sphere"] = CreateMovingSphere,
        ["moving_capsule"] = CreateMovingCapsule,
        ["remove_body"] = RemoveBody, ["body_count"] = BodyCount, ["gravity"] = SetGravity,
        ["layer"] = SetLayer, ["ignore_layer"] = IgnoreLayer,
        ["trigger"] = SetTrigger, ["is_triggered"] = IsTriggered,
        ["spring"] = CreateSpring, ["set_spring"] = SetSpring,
        ["spring_info"] = SpringInfo, ["remove_spring"] = RemoveSpring, ["spring_count"] = SpringCount,
        ["step"] = Step,
        ["set_velocity"] = SetVelocity, ["set_position"] = SetPosition,
        ["set_rotation"] = SetRotation, ["set_angular_velocity"] = SetAngularVelocity,
        ["lock_rotation"] = LockRotation, ["set_damping"] = SetDamping, ["material"] = SetMaterial,
        ["wake"] = WakeBody, ["is_sleeping"] = IsSleeping,
        ["apply_force"] = ApplyForce, ["apply_impulse"] = ApplyImpulse,
        ["apply_impulse_at"] = ApplyImpulseAt, ["apply_torque"] = ApplyTorque,
        ["apply_angular_impulse"] = ApplyAngularImpulse,
        ["position"] = BodyPosition, ["velocity"] = BodyVelocity, ["transform"] = BodyTransform,
        ["body_info"] = BodyInfo, ["is_colliding"] = IsColliding, ["is_grounded"] = IsGrounded,
        ["contacts"] = Contacts, ["overlap_sphere"] = OverlapSphere, ["raycast"] = Raycast,
        ["character"] = CreateCharacter, ["character_move"] = MoveCharacter,
        ["character_jump"] = JumpCharacter, ["character_tune"] = TuneCharacter, ["character_info"] = CharacterInfo,
        ["remove_character"] = RemoveCharacter,
    };
}

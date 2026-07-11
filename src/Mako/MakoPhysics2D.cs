using System.Numerics;

namespace Mako;

/// A small, rendering-independent 2D rigid-body simulation.  The MAKO-facing
/// API uses numeric handles so this core can later sit behind either Mako2D or
/// an editor/engine integration without exposing C# objects to scripts.
static class MakoPhysics2D
{
    private enum BodyKind { Static, Kinematic, Dynamic }
    private enum ShapeKind { Circle, Box }

    private sealed class Body
    {
        public required int Id;
        public required BodyKind Kind;
        public required ShapeKind Shape;
        public Vector2 Position;
        public Vector2 Velocity;
        public Vector2 Force;
        public float Rotation;
        public float AngularVelocity;
        public float Torque;
        public float Radius;
        public Vector2 Size;
        public float Mass;
        public float InverseMass;
        public float Inertia;
        public float InverseInertia;
        public bool RotationLocked;
        public float Restitution;
        public float Friction;
        public float LinearDamping = 0.08f;
        public float AngularDamping = 0.12f;
        public bool Awake = true;
        public float SleepTimer;
        public bool Grounded;
        public int CollisionGroup = -1;
        public readonly HashSet<int> Contacts = [];
    }

    private sealed class Spring
    {
        public required int Id;
        public required int BodyA;
        public required int BodyB;
        public Vector2 LocalAnchorA;
        public Vector2 LocalAnchorB;
        public float RestLength;
        public float Stiffness;
        public float Damping;
        public float MaxLength = float.PositiveInfinity;
    }

    private sealed class Slime
    {
        public required int Id;
        public required List<int> Nodes;
        public required List<int> Springs;
        public float Radius;
        public float ParticleRadius;
        public float TargetArea;
        public float AreaStrength = 0.65f;
        public float MoveSpeed = 240;
        public float MoveForce = 8000;
        public float AirControl = 0.35f;
        public float JumpSpeed = 380;
        public float JumpHoldForce = 1200;
        public float CoyoteDuration = 0.1f;
        public float BufferDuration = 0.12f;
        public float CoyoteTimer;
        public float JumpBufferTimer;
        public float JumpHoldTimer;
        public float JumpCooldown;
    }

    private sealed class World
    {
        public Vector2 Gravity;
        public float FixedStep;
        public float Accumulator;
        public int Substeps;
        public readonly List<Body?> Bodies = [];
        public readonly List<Spring?> Springs = [];
        public readonly List<Slime?> Slimes = [];
    }

    private readonly record struct Contact(Body A, Body B, Vector2 Normal, float Penetration, List<Vector2> Points);
    private static readonly List<World?> Worlds = [];

    public static void ResetAll() => Worlds.Clear();

    // ── Worlds and bodies ────────────────────────────────────────────────────

    public static object? CreateWorld(List<object?> a)
    {
        float gx = Number(a, 0, 0);
        float gy = Number(a, 1, 980);
        float fixedStep = Number(a, 2, 1f / 60f);
        int substeps = (int)Number(a, 3, 4);
        if (fixedStep <= 0) throw new MakoError("Physics2D.world(): fixed_step must be greater than 0");
        if (substeps < 1 || substeps > 16) throw new MakoError("Physics2D.world(): substeps must be between 1 and 16");
        Worlds.Add(new World { Gravity = new Vector2(gx, gy), FixedStep = fixedStep, Substeps = substeps });
        return (double)(Worlds.Count - 1);
    }

    public static object? DestroyWorld(List<object?> a)
    {
        int id = Handle(a, 0, "Physics2D.destroy_world");
        if (id >= 0 && id < Worlds.Count) Worlds[id] = null;
        return null;
    }

    public static object? ClearWorld(List<object?> a)
    {
        var world = WorldAt(a, "Physics2D.clear");
        world.Bodies.Clear();
        world.Springs.Clear();
        world.Slimes.Clear();
        return null;
    }

    public static object? CreateCircle(List<object?> a) => CreateBody(a, ShapeKind.Circle);
    public static object? CreateBox(List<object?> a) => CreateBody(a, ShapeKind.Box);

    private static object? CreateBody(List<object?> a, ShapeKind shape)
    {
        string fn = shape == ShapeKind.Circle ? "Physics2D.circle" : "Physics2D.box";
        var world = WorldAt(a, fn);
        string kindText = a.Count > 1 ? a[1]?.ToString() ?? "dynamic" : "dynamic";
        var kind = kindText.ToLowerInvariant() switch
        {
            "static" => BodyKind.Static,
            "kinematic" => BodyKind.Kinematic,
            "dynamic" => BodyKind.Dynamic,
            _ => throw new MakoError($"{fn}(): body type must be 'static', 'kinematic', or 'dynamic'")
        };

        float x = Number(a, 2, 0), y = Number(a, 3, 0);
        int materialOffset;
        float radius = 0;
        Vector2 size = default;
        if (shape == ShapeKind.Circle)
        {
            radius = Number(a, 4, 1);
            if (radius <= 0) throw new MakoError($"{fn}(): radius must be greater than 0");
            materialOffset = 5;
        }
        else
        {
            float width = Number(a, 4, 1), height = Number(a, 5, 1);
            if (width <= 0 || height <= 0) throw new MakoError($"{fn}(): width and height must be greater than 0");
            size = new Vector2(width, height);
            materialOffset = 6;
        }

        float mass = Number(a, materialOffset, 1);
        if (kind == BodyKind.Dynamic && mass <= 0)
            throw new MakoError($"{fn}(): a dynamic body's mass must be greater than 0");

        float inertia = shape == ShapeKind.Circle
            ? 0.5f * mass * radius * radius
            : mass * (size.X * size.X + size.Y * size.Y) / 12f;
        int id = world.Bodies.Count;
        world.Bodies.Add(new Body
        {
            Id = id,
            Kind = kind,
            Shape = shape,
            Position = new Vector2(x, y),
            Radius = radius,
            Size = size,
            Mass = kind == BodyKind.Dynamic ? mass : 0,
            InverseMass = kind == BodyKind.Dynamic ? 1f / mass : 0,
            Inertia = kind == BodyKind.Dynamic ? inertia : 0,
            InverseInertia = kind == BodyKind.Dynamic ? 1f / inertia : 0,
            Restitution = Math.Clamp(Number(a, materialOffset + 1, 0.2f), 0, 1),
            Friction = Math.Clamp(Number(a, materialOffset + 2, 0.4f), 0, 1),
            Rotation = DegreesToRadians(Number(a, materialOffset + 3, 0)),
        });
        return (double)id;
    }

    public static object? RemoveBody(List<object?> a)
    {
        var world = WorldAt(a, "Physics2D.remove_body");
        int id = Handle(a, 1, "Physics2D.remove_body");
        if (id >= 0 && id < world.Bodies.Count)
        {
            world.Bodies[id] = null;
            foreach (var other in world.Bodies)
            {
                if (other == null) continue;
                if (other.Contacts.Remove(id))
                {
                    other.Grounded = false;
                    Wake(other);
                }
            }
            for (int i = 0; i < world.Springs.Count; i++)
                if (world.Springs[i] is { } spring && (spring.BodyA == id || spring.BodyB == id))
                    world.Springs[i] = null;
            for (int i = 0; i < world.Slimes.Count; i++)
                if (world.Slimes[i]?.Nodes.Contains(id) == true) world.Slimes[i] = null;
        }
        return null;
    }

    public static object? BodyCount(List<object?> a) =>
        (double)WorldAt(a, "Physics2D.body_count").Bodies.Count(b => b != null);

    // ── Spring joints ─────────────────────────────────────────────────────────

    public static object? CreateSpring(List<object?> a)
    {
        var world = WorldAt(a, "Physics2D.spring");
        int bodyA = Handle(a, 1, "Physics2D.spring"), bodyB = Handle(a, 2, "Physics2D.spring");
        var ba = BodyById(world, bodyA, "Physics2D.spring");
        var bb = BodyById(world, bodyB, "Physics2D.spring");
        if (bodyA == bodyB) throw new MakoError("Physics2D.spring(): bodies must be different");
        var anchorA = new Vector2(Number(a, 6, 0), Number(a, 7, 0));
        var anchorB = new Vector2(Number(a, 8, 0), Number(a, 9, 0));
        float currentLength = Vector2.Distance(ba.Position + Rotate(anchorA, ba.Rotation),
                                               bb.Position + Rotate(anchorB, bb.Rotation));
        float restLength = Number(a, 3, -1);
        if (restLength < 0) restLength = currentLength;
        float stiffness = Number(a, 4, 120), damping = Number(a, 5, 12);
        if (stiffness < 0 || damping < 0)
            throw new MakoError("Physics2D.spring(): stiffness and damping must not be negative");
        int id = world.Springs.Count;
        world.Springs.Add(new Spring
        {
            Id = id, BodyA = bodyA, BodyB = bodyB,
            LocalAnchorA = anchorA, LocalAnchorB = anchorB,
            RestLength = restLength, Stiffness = stiffness, Damping = damping,
        });
        Wake(ba); Wake(bb);
        return (double)id;
    }

    public static object? RemoveSpring(List<object?> a)
    {
        var world = WorldAt(a, "Physics2D.remove_spring");
        int id = Handle(a, 1, "Physics2D.remove_spring");
        if (id >= 0 && id < world.Springs.Count) world.Springs[id] = null;
        return null;
    }

    public static object? SpringCount(List<object?> a) =>
        (double)WorldAt(a, "Physics2D.spring_count").Springs.Count(s => s != null);

    public static object? SetSpring(List<object?> a)
    {
        var spring = SpringAt(a, "Physics2D.set_spring");
        if (a.Count > 2)
        {
            float rest = Number(a, 2, spring.RestLength);
            if (rest < 0) throw new MakoError("Physics2D.set_spring(): rest length must not be negative");
            spring.RestLength = rest;
        }
        if (a.Count > 3) spring.Stiffness = MathF.Max(0, Number(a, 3, spring.Stiffness));
        if (a.Count > 4) spring.Damping = MathF.Max(0, Number(a, 4, spring.Damping));
        return null;
    }

    public static object? SpringInfo(List<object?> a)
    {
        var world = WorldAt(a, "Physics2D.spring_info");
        int id = Handle(a, 1, "Physics2D.spring_info");
        if (id < 0 || id >= world.Springs.Count || world.Springs[id] == null) return null;
        var spring = world.Springs[id]!;
        var ba = BodyById(world, spring.BodyA, "Physics2D.spring_info");
        var bb = BodyById(world, spring.BodyB, "Physics2D.spring_info");
        var pa = ba.Position + Rotate(spring.LocalAnchorA, ba.Rotation);
        var pb = bb.Position + Rotate(spring.LocalAnchorB, bb.Rotation);
        return new Dictionary<string, object?>
        {
            ["id"] = (double)spring.Id,
            ["body_a"] = (double)spring.BodyA, ["body_b"] = (double)spring.BodyB,
            ["ax"] = (double)pa.X, ["ay"] = (double)pa.Y,
            ["bx"] = (double)pb.X, ["by"] = (double)pb.Y,
            ["length"] = (double)Vector2.Distance(pa, pb),
            ["rest_length"] = (double)spring.RestLength,
            ["stiffness"] = (double)spring.Stiffness, ["damping"] = (double)spring.Damping,
        };
    }

    // ── Easy slime API ────────────────────────────────────────────────────────

    public static object? CreateSlime(List<object?> a)
    {
        var world = WorldAt(a, "Physics2D.slime");
        int worldId = Handle(a, 0, "Physics2D.slime");
        float x = Number(a, 1, 0), y = Number(a, 2, 0), radius = Number(a, 3, 40);
        if (radius <= 0) throw new MakoError("Physics2D.slime(): radius must be greater than 0");
        var options = a.Count > 4 && a[4] is Dictionary<string, object?> dict ? dict : null;
        int points = Math.Clamp((int)Option(options, "points", 14), 6, 24);
        float squish = Math.Clamp(Option(options, "squish", 0.65f), 0, 1);
        float totalMass = MathF.Max(0.1f, Option(options, "mass", 8));
        // Size the hidden perimeter circles so neighbouring colliders overlap
        // even at the spring stretch limit. This closes the gaps that allowed
        // thin platforms to thread through the ring like a net.
        float coverage = MathF.Sin(MathF.PI / points) * 1.45f;
        float defaultParticleRadius = radius * coverage / (1 + coverage);
        float particleRadius = Math.Clamp(Option(options, "particle_radius", defaultParticleRadius), 2, radius * 0.45f);
        float ringRadius = MathF.Max(particleRadius * 1.1f, radius - particleRadius);
        float bounce = Math.Clamp(Option(options, "bounce", 0.05f), 0, 1);
        float friction = Math.Clamp(Option(options, "friction", 0.8f), 0, 1);
        float stiffness = Option(options, "stiffness", 260 - squish * 170);
        float damping = Option(options, "damping", 14);
        float moveSpeed = MathF.Max(0, Option(options, "speed", 240));
        float moveForce = MathF.Max(0, Option(options, "move_force", 8000));
        float airControl = Math.Clamp(Option(options, "air_control", 0.35f), 0, 1);
        float jumpSpeed = MathF.Max(0, Option(options, "jump_speed", 380));
        float jumpHoldForce = MathF.Max(0, Option(options, "jump_hold_force", 1200));
        float coyoteTime = MathF.Max(0, Option(options, "coyote_time", 0.1f));
        float jumpBuffer = MathF.Max(0, Option(options, "jump_buffer", 0.12f));
        float stretchLimit = Math.Clamp(Option(options, "stretch_limit", 0.35f), 0.05f, 2);
        float areaStrength = Math.Clamp(Option(options, "shape_recovery", 0.65f), 0, 1);

        int slimeId = world.Slimes.Count;
        var nodes = new List<int>();
        var springs = new List<int>();
        float edgeLength = 2 * ringRadius * MathF.Sin(MathF.PI / points);
        for (int i = 0; i < points; i++)
        {
            float angle = 2 * MathF.PI * i / points;
            int node = (int)(double)CreateCircle([
                (double)worldId, "dynamic", (double)(x + MathF.Cos(angle) * ringRadius),
                (double)(y + MathF.Sin(angle) * ringRadius), (double)particleRadius,
                (double)(totalMass / points), (double)bounce, (double)friction])!;
            var body = world.Bodies[node]!;
            body.LinearDamping = 0.18f;
            body.AngularDamping = 0.25f;
            body.CollisionGroup = slimeId;
            nodes.Add(node);
        }

        int AddSpring(int ia, int ib, float rest, float strength, float damp)
        {
            int id = (int)(double)CreateSpring([
                (double)worldId, (double)nodes[ia], (double)nodes[ib],
                (double)rest, (double)MathF.Max(0, strength), (double)MathF.Max(0, damp)])!;
            world.Springs[id]!.MaxLength = rest * (1 + stretchLimit);
            springs.Add(id);
            return id;
        }

        for (int i = 0; i < points; i++)
        {
            AddSpring(i, (i + 1) % points, edgeLength, stiffness, damping);
            AddSpring(i, (i + 2) % points,
                2 * ringRadius * MathF.Sin(2 * MathF.PI / points), stiffness * 0.75f, damping * 0.8f);
        }
        if (points % 2 == 0)
            for (int i = 0; i < points / 2; i++)
                AddSpring(i, i + points / 2, ringRadius * 2, stiffness * 0.65f, damping * 0.75f);

        world.Slimes.Add(new Slime
        {
            Id = slimeId, Nodes = nodes, Springs = springs, Radius = radius, ParticleRadius = particleRadius,
            TargetArea = 0.5f * points * ringRadius * ringRadius * MathF.Sin(2 * MathF.PI / points),
            AreaStrength = areaStrength,
            MoveSpeed = moveSpeed, MoveForce = moveForce, AirControl = airControl,
            JumpSpeed = jumpSpeed, JumpHoldForce = jumpHoldForce,
            CoyoteDuration = coyoteTime, BufferDuration = jumpBuffer,
        });
        return (double)slimeId;
    }

    public static object? RemoveSlime(List<object?> a)
    {
        var world = WorldAt(a, "Physics2D.remove_slime");
        int id = Handle(a, 1, "Physics2D.remove_slime");
        if (id < 0 || id >= world.Slimes.Count || world.Slimes[id] == null) return null;
        var slime = world.Slimes[id]!;
        foreach (int spring in slime.Springs)
            if (spring >= 0 && spring < world.Springs.Count) world.Springs[spring] = null;
        foreach (int node in slime.Nodes)
            if (node >= 0 && node < world.Bodies.Count) world.Bodies[node] = null;
        world.Slimes[id] = null;
        return null;
    }

    public static object? SlimeCount(List<object?> a) =>
        (double)WorldAt(a, "Physics2D.slime_count").Slimes.Count(s => s != null);

    public static object? SetSlimePosition(List<object?> a)
    {
        var (world, slime) = SlimeAt(a, "Physics2D.slime_set_position");
        float x = Number(a, 2, 0), y = Number(a, 3, 0);
        bool resetVelocity = a.Count <= 4 || Convert.ToBoolean(a[4]);
        var live = slime.Nodes.Select(id => world.Bodies[id]).Where(body => body != null).Cast<Body>().ToList();
        if (live.Count == 0) return null;
        var center = new Vector2(live.Average(body => body.Position.X), live.Average(body => body.Position.Y));
        var offset = new Vector2(x, y) - center;
        foreach (var body in live)
        {
            body.Position += offset;
            if (resetVelocity)
            {
                body.Velocity = Vector2.Zero;
                body.AngularVelocity = 0;
            }
            body.Contacts.Clear();
            body.Grounded = false;
            Wake(body);
        }
        slime.CoyoteTimer = slime.JumpBufferTimer = slime.JumpHoldTimer = slime.JumpCooldown = 0;
        return null;
    }

    public static object? MoveSlime(List<object?> a)
    {
        var (world, slime) = SlimeAt(a, "Physics2D.slime_move");
        float direction = Math.Clamp(Number(a, 2, 0), -1, 1);
        float speed = MathF.Max(0, Number(a, 3, slime.MoveSpeed));
        float maxForce = MathF.Max(0, Number(a, 4, slime.MoveForce));
        bool grounded = slime.Nodes.Any(id => world.Bodies[id]?.Grounded == true);
        float control = grounded ? 1 : slime.AirControl;
        float perNodeLimit = maxForce / slime.Nodes.Count;
        foreach (int node in slime.Nodes)
        {
            var body = world.Bodies[node];
            if (body == null) continue;
            Wake(body);
            float force = (direction * speed - body.Velocity.X) * body.Mass * 10 * control;
            body.Force.X += Math.Clamp(force, -perNodeLimit, perNodeLimit);
            if (direction != 0 && MathF.Abs(body.Velocity.X) > speed && MathF.Sign(body.Velocity.X) == MathF.Sign(direction))
                body.Velocity.X = direction * speed;
        }
        return null;
    }

    public static object? JumpSlime(List<object?> a)
    {
        var (world, slime) = SlimeAt(a, "Physics2D.slime_jump");
        bool grounded = slime.Nodes.Any(id => world.Bodies[id]?.Grounded == true);
        float speed = MathF.Max(0, Number(a, 2, slime.JumpSpeed));
        if (slime.JumpCooldown > 0) return false;
        if (!grounded && slime.CoyoteTimer <= 0)
        {
            slime.JumpBufferTimer = slime.BufferDuration;
            return false;
        }
        ExecuteSlimeJump(world, slime, speed);
        return true;
    }

    public static object? HoldSlimeJump(List<object?> a)
    {
        var (world, slime) = SlimeAt(a, "Physics2D.slime_hold_jump");
        bool held = a.Count > 2 && Convert.ToBoolean(a[2]);
        float force = MathF.Max(0, Number(a, 3, slime.JumpHoldForce));
        if (!held)
        {
            if (slime.JumpHoldTimer > 0)
                foreach (int node in slime.Nodes)
                    if (world.Bodies[node] is { Velocity.Y: < 0 } body) body.Velocity.Y *= 0.55f;
            slime.JumpHoldTimer = 0;
            return null;
        }
        if (slime.JumpHoldTimer <= 0) return null;
        float perNodeForce = force / slime.Nodes.Count;
        foreach (int node in slime.Nodes)
        {
            var body = world.Bodies[node];
            if (body == null) continue;
            Wake(body);
            body.Force.Y -= perNodeForce;
        }
        return null;
    }

    private static void ExecuteSlimeJump(World world, Slime slime, float speed)
    {
        foreach (int node in slime.Nodes)
        {
            var body = world.Bodies[node];
            if (body == null) continue;
            Wake(body);
            body.Velocity.Y = MathF.Min(body.Velocity.Y, 0) - speed;
            body.Grounded = false;
        }
        slime.CoyoteTimer = 0;
        slime.JumpBufferTimer = 0;
        slime.JumpHoldTimer = 0.18f;
        slime.JumpCooldown = 0.12f;
    }

    public static object? SlimeInfo(List<object?> a)
    {
        var (world, slime) = SlimeAt(a, "Physics2D.slime_info");
        var live = slime.Nodes.Select(id => world.Bodies[id]).Where(b => b != null).Cast<Body>().ToList();
        if (live.Count == 0) return null;
        var points = live.Select(body => (object?)new List<object?>
            { (double)body.Position.X, (double)body.Position.Y }).ToList();
        float width = live.Max(body => body.Position.X) - live.Min(body => body.Position.X);
        float height = live.Max(body => body.Position.Y) - live.Min(body => body.Position.Y);
        float area = 0;
        for (int i = 0; i < live.Count; i++)
            area += Cross(live[i].Position, live[(i + 1) % live.Count].Position);
        area = MathF.Abs(area * 0.5f);
        return new Dictionary<string, object?>
        {
            ["id"] = (double)slime.Id,
            ["x"] = (double)live.Average(body => body.Position.X),
            ["y"] = (double)live.Average(body => body.Position.Y),
            ["vx"] = (double)live.Average(body => body.Velocity.X),
            ["vy"] = (double)live.Average(body => body.Velocity.Y),
            ["radius"] = (double)slime.Radius,
            ["particle_radius"] = (double)slime.ParticleRadius,
            ["grounded"] = live.Any(body => body.Grounded),
            ["sleeping"] = live.All(body => !body.Awake),
            ["points"] = points,
            ["point_count"] = (double)live.Count,
            ["width"] = (double)width, ["height"] = (double)height,
            ["squash"] = (double)(width / (slime.Radius * 2)),
            ["stretch"] = (double)(height / (slime.Radius * 2)),
            ["area"] = (double)area,
            ["area_ratio"] = (double)(area / slime.TargetArea),
            ["coyote_time"] = (double)slime.CoyoteTimer,
            ["jump_buffered"] = slime.JumpBufferTimer > 0,
        };
    }

    // ── Simulation ───────────────────────────────────────────────────────────

    public static object? Step(List<object?> a)
    {
        var world = WorldAt(a, "Physics2D.step");
        float elapsed = Number(a, 1, world.FixedStep);
        if (elapsed < 0) throw new MakoError("Physics2D.step(): delta must not be negative");

        foreach (var body in world.Bodies)
            if (body != null && (body.Kind != BodyKind.Dynamic || body.Awake))
            {
                body.Contacts.Clear();
                body.Grounded = false;
            }

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
        int actualSubsteps = AdaptiveSubsteps(world);
        float dt = world.FixedStep / actualSubsteps;
        for (int substep = 0; substep < actualSubsteps; substep++)
        {
            ApplySpringForces(world, dt);
            foreach (var body in world.Bodies)
            {
                if (body == null) continue;
                if (body.Kind == BodyKind.Dynamic && body.Awake)
                {
                    body.Velocity += (world.Gravity + body.Force * body.InverseMass) * dt;
                    body.AngularVelocity += body.Torque * body.InverseInertia * dt;
                    body.Velocity *= MathF.Exp(-body.LinearDamping * dt);
                    body.AngularVelocity *= MathF.Exp(-body.AngularDamping * dt);
                    body.Position += body.Velocity * dt;
                    body.Rotation += body.AngularVelocity * dt;
                }
                else if (body.Kind == BodyKind.Kinematic)
                {
                    body.Position += body.Velocity * dt;
                    body.Rotation += body.AngularVelocity * dt;
                }
            }
            EnforceSpringLimits(world);
            EnforceSlimeAreas(world);

            // Sequential impulses over several small substeps greatly reduce
            // tunnelling and keep angular stacks from injecting energy.
            for (int iteration = 0; iteration < 6; iteration++)
            {
                for (int i = 0; i < world.Bodies.Count; i++)
                for (int j = i + 1; j < world.Bodies.Count; j++)
                {
                    var a = world.Bodies[i]; var b = world.Bodies[j];
                    if (a == null || b == null || (a.InverseMass == 0 && b.InverseMass == 0)) continue;
                    if (a.CollisionGroup >= 0 && a.CollisionGroup == b.CollisionGroup) continue;
                    if (a.Kind == BodyKind.Dynamic && !a.Awake &&
                        (b.Kind == BodyKind.Kinematic || (b.Kind == BodyKind.Dynamic && b.Awake))) Wake(a);
                    if (b.Kind == BodyKind.Dynamic && !b.Awake &&
                        (a.Kind == BodyKind.Kinematic || (a.Kind == BodyKind.Dynamic && a.Awake))) Wake(b);
                    if ((a.Kind != BodyKind.Dynamic || !a.Awake) &&
                        (b.Kind != BodyKind.Dynamic || !b.Awake)) continue;
                    if (!TryCollide(a, b, out var contact)) continue;
                    a.Contacts.Add(b.Id); b.Contacts.Add(a.Id);
                    if (contact.Normal.Y > 0.45f) a.Grounded = true;
                    if (contact.Normal.Y < -0.45f) b.Grounded = true;
                    Resolve(contact);
                }
            }
        }

        foreach (var body in world.Bodies)
        {
            if (body == null) continue;
            body.Force = Vector2.Zero;
            body.Torque = 0;
            UpdateSleep(body, world.FixedStep);
        }
        UpdateSlimeControllers(world);
    }

    private static void UpdateSlimeControllers(World world)
    {
        foreach (var slime in world.Slimes)
        {
            if (slime == null) continue;
            bool grounded = slime.Nodes.Any(id => world.Bodies[id]?.Grounded == true);
            slime.CoyoteTimer = grounded
                ? slime.CoyoteDuration
                : MathF.Max(0, slime.CoyoteTimer - world.FixedStep);
            slime.JumpBufferTimer = MathF.Max(0, slime.JumpBufferTimer - world.FixedStep);
            slime.JumpHoldTimer = MathF.Max(0, slime.JumpHoldTimer - world.FixedStep);
            slime.JumpCooldown = MathF.Max(0, slime.JumpCooldown - world.FixedStep);
            if (slime.JumpBufferTimer > 0 && slime.CoyoteTimer > 0 && slime.JumpCooldown <= 0)
                ExecuteSlimeJump(world, slime, slime.JumpSpeed);
        }
    }

    private static int AdaptiveSubsteps(World world)
    {
        float maxTravel = 0, smallestFeature = float.MaxValue;
        foreach (var body in world.Bodies)
        {
            if (body == null) continue;
            if ((body.Kind == BodyKind.Dynamic && body.Awake) || body.Kind == BodyKind.Kinematic)
                maxTravel = MathF.Max(maxTravel, body.Velocity.Length() * world.FixedStep);
            float feature = body.Shape == ShapeKind.Circle
                ? body.Radius
                : MathF.Min(body.Size.X, body.Size.Y) * 0.5f;
            smallestFeature = MathF.Min(smallestFeature, feature);
        }
        if (smallestFeature == float.MaxValue || maxTravel <= 0) return world.Substeps;
        float safeTravel = MathF.Max(0.5f, smallestFeature * 0.5f);
        return Math.Clamp(Math.Max(world.Substeps, (int)MathF.Ceiling(maxTravel / safeTravel)), 1, 64);
    }

    private static void ApplySpringForces(World world, float dt)
    {
        foreach (var spring in world.Springs)
        {
            if (spring == null) continue;
            var a = world.Bodies[spring.BodyA]; var b = world.Bodies[spring.BodyB];
            if (a == null || b == null) continue;
            if (a.Kind == BodyKind.Dynamic && !a.Awake && b.Kind == BodyKind.Dynamic && b.Awake) Wake(a);
            if (b.Kind == BodyKind.Dynamic && !b.Awake && a.Kind == BodyKind.Dynamic && a.Awake) Wake(b);
            if ((a.Kind != BodyKind.Dynamic || !a.Awake) && (b.Kind != BodyKind.Dynamic || !b.Awake)) continue;

            var armA = Rotate(spring.LocalAnchorA, a.Rotation);
            var armB = Rotate(spring.LocalAnchorB, b.Rotation);
            var delta = (b.Position + armB) - (a.Position + armA);
            float length = delta.Length();
            if (length < 0.0001f) continue;
            var direction = delta / length;
            float relativeSpeed = Vector2.Dot(VelocityAt(b, armB) - VelocityAt(a, armA), direction);
            float magnitude = spring.Stiffness * (length - spring.RestLength) + spring.Damping * relativeSpeed;
            float inverseMassSum = a.InverseMass + b.InverseMass;
            float maxMagnitude = inverseMassSum > 0 ? 25f / (inverseMassSum * dt) : 0;
            magnitude = Math.Clamp(magnitude, -maxMagnitude, maxMagnitude);
            var force = direction * magnitude;
            if (a.Kind == BodyKind.Dynamic && a.Awake)
            {
                a.Velocity += force * a.InverseMass * dt;
                a.AngularVelocity += Cross(armA, force) * a.InverseInertia * dt;
            }
            if (b.Kind == BodyKind.Dynamic && b.Awake)
            {
                b.Velocity -= force * b.InverseMass * dt;
                b.AngularVelocity -= Cross(armB, force) * b.InverseInertia * dt;
            }
        }
    }

    private static void EnforceSpringLimits(World world)
    {
        foreach (var spring in world.Springs)
        {
            if (spring == null || float.IsPositiveInfinity(spring.MaxLength)) continue;
            var a = world.Bodies[spring.BodyA]; var b = world.Bodies[spring.BodyB];
            if (a == null || b == null) continue;
            var pa = a.Position + Rotate(spring.LocalAnchorA, a.Rotation);
            var pb = b.Position + Rotate(spring.LocalAnchorB, b.Rotation);
            var delta = pb - pa;
            float length = delta.Length();
            if (length <= spring.MaxLength || length < 0.0001f) continue;
            var direction = delta / length;
            float inverseMassSum = a.InverseMass + b.InverseMass;
            if (inverseMassSum <= 0) continue;
            var correction = direction * (length - spring.MaxLength) / inverseMassSum;
            if (a.Kind == BodyKind.Dynamic) a.Position += correction * a.InverseMass;
            if (b.Kind == BodyKind.Dynamic) b.Position -= correction * b.InverseMass;

            float separatingSpeed = Vector2.Dot(b.Velocity - a.Velocity, direction);
            if (separatingSpeed > 0)
            {
                float impulse = separatingSpeed / inverseMassSum;
                if (a.Kind == BodyKind.Dynamic) a.Velocity += direction * impulse * a.InverseMass;
                if (b.Kind == BodyKind.Dynamic) b.Velocity -= direction * impulse * b.InverseMass;
            }
        }
    }

    private static void EnforceSlimeAreas(World world)
    {
        foreach (var slime in world.Slimes)
        {
            if (slime == null || slime.AreaStrength <= 0 || slime.Nodes.Count < 3) continue;
            for (int pass = 0; pass < 2; pass++)
            {
                var bodies = slime.Nodes.Select(id => world.Bodies[id]).ToList();
                if (bodies.Any(body => body == null)) break;
                float area = 0;
                for (int i = 0; i < bodies.Count; i++)
                    area += Cross(bodies[i]!.Position, bodies[(i + 1) % bodies.Count]!.Position);
                area *= 0.5f;
                float error = area - slime.TargetArea;
                if (MathF.Abs(error) < slime.TargetArea * 0.01f) break;

                var gradients = new Vector2[bodies.Count];
                float denominator = 0;
                for (int i = 0; i < bodies.Count; i++)
                {
                    var previous = bodies[(i - 1 + bodies.Count) % bodies.Count]!.Position;
                    var next = bodies[(i + 1) % bodies.Count]!.Position;
                    gradients[i] = new Vector2((next.Y - previous.Y) * 0.5f,
                                               (previous.X - next.X) * 0.5f);
                    denominator += bodies[i]!.InverseMass * gradients[i].LengthSquared();
                }
                if (denominator < 0.000001f) break;
                float lambda = -error / denominator * slime.AreaStrength;
                float maxCorrection = slime.ParticleRadius * 0.2f;
                for (int i = 0; i < bodies.Count; i++)
                {
                    var body = bodies[i]!;
                    if (body.Kind != BodyKind.Dynamic) continue;
                    var correction = gradients[i] * (lambda * body.InverseMass);
                    float length = correction.Length();
                    if (length > maxCorrection) correction *= maxCorrection / length;
                    body.Position += correction;
                    if (correction.LengthSquared() > 0.0001f) Wake(body);
                }
            }
        }
    }

    private static void UpdateSleep(Body body, float dt)
    {
        if (body.Kind != BodyKind.Dynamic || !body.Awake) return;
        if (body.Velocity.LengthSquared() < 0.25f && MathF.Abs(body.AngularVelocity) < DegreesToRadians(1))
        {
            body.SleepTimer += dt;
            if (body.SleepTimer >= 0.6f)
            {
                body.Awake = false;
                body.Velocity = Vector2.Zero;
                body.AngularVelocity = 0;
            }
        }
        else body.SleepTimer = 0;
    }

    private static void Wake(Body body)
    {
        if (body.Kind != BodyKind.Dynamic) return;
        body.Awake = true;
        body.SleepTimer = 0;
    }

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
            float alongNormal = Vector2.Dot(relativeVelocity, c.Normal);
            if (alongNormal > 0) continue;

            // Small resting impacts should settle instead of bouncing forever.
            float restitution = alongNormal < -30 ? MathF.Min(c.A.Restitution, c.B.Restitution) : 0;
            float raCrossN = Cross(ra, c.Normal), rbCrossN = Cross(rb, c.Normal);
            float normalMass = invMassSum +
                raCrossN * raCrossN * c.A.InverseInertia +
                rbCrossN * rbCrossN * c.B.InverseInertia;
            if (normalMass <= 0) continue;
            float impulseSize = -(1 + restitution) * alongNormal / normalMass / pointCount;
            var impulse = c.Normal * impulseSize;
            ApplyContactImpulse(c.A, -impulse, ra);
            ApplyContactImpulse(c.B, impulse, rb);

            relativeVelocity = VelocityAt(c.B, rb) - VelocityAt(c.A, ra);
            var tangent = relativeVelocity - Vector2.Dot(relativeVelocity, c.Normal) * c.Normal;
            if (tangent.LengthSquared() < 0.0000001f) continue;
            tangent = Vector2.Normalize(tangent);
            float raCrossT = Cross(ra, tangent), rbCrossT = Cross(rb, tangent);
            float tangentMass = invMassSum +
                raCrossT * raCrossT * c.A.InverseInertia +
                rbCrossT * rbCrossT * c.B.InverseInertia;
            if (tangentMass <= 0) continue;
            float frictionImpulse = -Vector2.Dot(relativeVelocity, tangent) / tangentMass / pointCount;
            float friction = MathF.Sqrt(c.A.Friction * c.B.Friction);
            frictionImpulse = Math.Clamp(frictionImpulse, -impulseSize * friction, impulseSize * friction);
            var frictionVector = tangent * frictionImpulse;
            ApplyContactImpulse(c.A, -frictionVector, ra);
            ApplyContactImpulse(c.B, frictionVector, rb);
        }
    }

    private static Vector2 VelocityAt(Body body, Vector2 arm) =>
        body.Velocity + new Vector2(-body.AngularVelocity * arm.Y, body.AngularVelocity * arm.X);

    private static void ApplyContactImpulse(Body body, Vector2 impulse, Vector2 arm)
    {
        body.Velocity += impulse * body.InverseMass;
        body.AngularVelocity += Cross(arm, impulse) * body.InverseInertia;
    }

    private static float Cross(Vector2 a, Vector2 b) => a.X * b.Y - a.Y * b.X;

    // ── Collision detection ──────────────────────────────────────────────────

    private static bool TryCollide(Body a, Body b, out Contact contact)
    {
        if (a.Shape == ShapeKind.Circle && b.Shape == ShapeKind.Circle)
            return CircleCircle(a, b, out contact);
        if (a.Shape == ShapeKind.Box && b.Shape == ShapeKind.Box)
            return BoxBox(a, b, out contact);
        if (a.Shape == ShapeKind.Circle)
            return CircleBox(a, b, out contact);

        if (CircleBox(b, a, out var swapped))
        {
            contact = new Contact(a, b, -swapped.Normal, swapped.Penetration, swapped.Points);
            return true;
        }
        contact = default;
        return false;
    }

    private static bool CircleCircle(Body a, Body b, out Contact contact)
    {
        var delta = b.Position - a.Position;
        float radius = a.Radius + b.Radius;
        float distanceSq = delta.LengthSquared();
        if (distanceSq >= radius * radius) { contact = default; return false; }
        float distance = MathF.Sqrt(distanceSq);
        var normal = distance > 0.00001f ? delta / distance : Vector2.UnitX;
        var point = a.Position + normal * (a.Radius - (radius - distance) * 0.5f);
        contact = new Contact(a, b, normal, radius - distance, [point]);
        return true;
    }

    private static bool BoxBox(Body a, Body b, out Contact contact)
    {
        var delta = b.Position - a.Position;
        BoxAxes(a, out var ax, out var ay);
        BoxAxes(b, out var bx, out var by);
        Vector2[] axes = [ax, ay, bx, by];
        float leastOverlap = float.MaxValue;
        var normal = Vector2.UnitX;

        foreach (var axis in axes)
        {
            float distance = Vector2.Dot(delta, axis);
            float overlap = ProjectionRadius(a, axis) + ProjectionRadius(b, axis) - MathF.Abs(distance);
            if (overlap <= 0) { contact = default; return false; }
            if (overlap < leastOverlap)
            {
                leastOverlap = overlap;
                normal = distance < 0 ? -axis : axis;
            }
        }

        contact = new Contact(a, b, normal, leastOverlap, BoxContactPoints(a, b, normal));
        return true;
    }

    // Normal points from the circle (a) toward the box (b).
    private static bool CircleBox(Body a, Body b, out Contact contact)
    {
        var half = b.Size * 0.5f;
        var local = Rotate(a.Position - b.Position, -b.Rotation);
        var closest = Vector2.Clamp(local, -half, half);
        var circleToBox = closest - local;
        float distanceSq = circleToBox.LengthSquared();
        if (distanceSq > a.Radius * a.Radius) { contact = default; return false; }

        if (distanceSq > 0.0000001f)
        {
            float distance = MathF.Sqrt(distanceSq);
            var normal = Rotate(circleToBox / distance, b.Rotation);
            var point = b.Position + Rotate(closest, b.Rotation);
            contact = new Contact(a, b, normal, a.Radius - distance, [point]);
            return true;
        }

        // Circle centre is inside the box: choose the nearest face.
        float dx = half.X - MathF.Abs(local.X), dy = half.Y - MathF.Abs(local.Y);
        Vector2 localNormal;
        float penetration;
        if (dx < dy)
        {
            localNormal = new Vector2(local.X < 0 ? 1 : -1, 0);
            closest.X = local.X < 0 ? -half.X : half.X;
            penetration = a.Radius + dx;
        }
        else
        {
            localNormal = new Vector2(0, local.Y < 0 ? 1 : -1);
            closest.Y = local.Y < 0 ? -half.Y : half.Y;
            penetration = a.Radius + dy;
        }
        contact = new Contact(a, b, Rotate(localNormal, b.Rotation), penetration,
            [b.Position + Rotate(closest, b.Rotation)]);
        return true;
    }

    private static void BoxAxes(Body body, out Vector2 x, out Vector2 y)
    {
        float c = MathF.Cos(body.Rotation), s = MathF.Sin(body.Rotation);
        x = new Vector2(c, s);
        y = new Vector2(-s, c);
    }

    private static float ProjectionRadius(Body body, Vector2 axis)
    {
        if (body.Shape == ShapeKind.Circle) return body.Radius;
        BoxAxes(body, out var x, out var y);
        return body.Size.X * 0.5f * MathF.Abs(Vector2.Dot(axis, x)) +
               body.Size.Y * 0.5f * MathF.Abs(Vector2.Dot(axis, y));
    }

    private static Vector2 Support(Body body, Vector2 direction)
    {
        if (body.Shape == ShapeKind.Circle)
            return body.Position + Vector2.Normalize(direction) * body.Radius;
        BoxAxes(body, out var x, out var y);
        float dx = Vector2.Dot(direction, x), dy = Vector2.Dot(direction, y);
        float sx = MathF.Abs(dx) < 0.00001f ? 0 : MathF.CopySign(body.Size.X * 0.5f, dx);
        float sy = MathF.Abs(dy) < 0.00001f ? 0 : MathF.CopySign(body.Size.Y * 0.5f, dy);
        return body.Position + x * sx + y * sy;
    }

    private static List<Vector2> BoxContactPoints(Body a, Body b, Vector2 normal)
    {
        var ac = BoxCorners(a); var bc = BoxCorners(b);
        var candidates = new List<Vector2>();
        foreach (var point in ac) if (PointInBox(point, b)) AddUnique(candidates, point);
        foreach (var point in bc) if (PointInBox(point, a)) AddUnique(candidates, point);
        for (int i = 0; i < 4; i++)
        for (int j = 0; j < 4; j++)
            if (SegmentIntersection(ac[i], ac[(i + 1) % 4], bc[j], bc[(j + 1) % 4], out var hit))
                AddUnique(candidates, hit);

        if (candidates.Count == 0)
            return [(Support(a, normal) + Support(b, -normal)) * 0.5f];

        var tangent = new Vector2(-normal.Y, normal.X);
        float minT = candidates.Min(p => Vector2.Dot(p, tangent));
        float maxT = candidates.Max(p => Vector2.Dot(p, tangent));
        float plane = (Vector2.Dot(Support(a, normal), normal) +
                       Vector2.Dot(Support(b, -normal), normal)) * 0.5f;
        var first = normal * plane + tangent * minT;
        if (maxT - minT < 0.01f) return [first];
        return [first, normal * plane + tangent * maxT];
    }

    private static Vector2[] BoxCorners(Body body)
    {
        BoxAxes(body, out var x, out var y);
        x *= body.Size.X * 0.5f; y *= body.Size.Y * 0.5f;
        return [body.Position - x - y, body.Position + x - y,
                body.Position + x + y, body.Position - x + y];
    }

    private static bool PointInBox(Vector2 point, Body box)
    {
        var local = Rotate(point - box.Position, -box.Rotation);
        var half = box.Size * 0.5f;
        return MathF.Abs(local.X) <= half.X + 0.001f && MathF.Abs(local.Y) <= half.Y + 0.001f;
    }

    private static bool SegmentIntersection(Vector2 p, Vector2 p2, Vector2 q, Vector2 q2, out Vector2 hit)
    {
        var r = p2 - p; var s = q2 - q;
        float denominator = Cross(r, s);
        if (MathF.Abs(denominator) < 0.000001f) { hit = default; return false; }
        float t = Cross(q - p, s) / denominator;
        float u = Cross(q - p, r) / denominator;
        if (t < -0.0001f || t > 1.0001f || u < -0.0001f || u > 1.0001f)
            { hit = default; return false; }
        hit = p + r * t;
        return true;
    }

    private static void AddUnique(List<Vector2> points, Vector2 point)
    {
        if (!points.Any(existing => Vector2.DistanceSquared(existing, point) < 0.0001f))
            points.Add(point);
    }

    private static Vector2 Rotate(Vector2 value, float radians)
    {
        float c = MathF.Cos(radians), s = MathF.Sin(radians);
        return new Vector2(value.X * c - value.Y * s, value.X * s + value.Y * c);
    }

    // ── Body control and inspection ─────────────────────────────────────────

    public static object? SetVelocity(List<object?> a)
    {
        var body = BodyAt(a, "Physics2D.set_velocity");
        Wake(body);
        body.Velocity = new Vector2(Number(a, 2, 0), Number(a, 3, 0));
        return null;
    }

    public static object? SetPosition(List<object?> a)
    {
        var body = BodyAt(a, "Physics2D.set_position");
        Wake(body);
        body.Position = new Vector2(Number(a, 2, 0), Number(a, 3, 0));
        return null;
    }

    public static object? SetRotation(List<object?> a)
    {
        var body = BodyAt(a, "Physics2D.set_rotation");
        Wake(body);
        body.Rotation = DegreesToRadians(Number(a, 2, 0));
        return null;
    }

    public static object? SetAngularVelocity(List<object?> a)
    {
        var body = BodyAt(a, "Physics2D.set_angular_velocity");
        Wake(body);
        if (!body.RotationLocked)
            body.AngularVelocity = DegreesToRadians(Number(a, 2, 0));
        return null;
    }

    public static object? LockRotation(List<object?> a)
    {
        var body = BodyAt(a, "Physics2D.lock_rotation");
        bool locked = a.Count <= 2 || Convert.ToBoolean(a[2]);
        body.RotationLocked = locked;
        body.InverseInertia = locked || body.Kind != BodyKind.Dynamic ? 0 : 1f / body.Inertia;
        if (locked) body.AngularVelocity = 0;
        Wake(body);
        return null;
    }

    public static object? SetDamping(List<object?> a)
    {
        var body = BodyAt(a, "Physics2D.set_damping");
        body.LinearDamping = MathF.Max(0, Number(a, 2, body.LinearDamping));
        body.AngularDamping = MathF.Max(0, Number(a, 3, body.AngularDamping));
        Wake(body);
        return null;
    }

    public static object? ApplyForce(List<object?> a)
    {
        var body = BodyAt(a, "Physics2D.apply_force");
        if (body.Kind == BodyKind.Dynamic)
        {
            Wake(body);
            body.Force += new Vector2(Number(a, 2, 0), Number(a, 3, 0));
        }
        return null;
    }

    public static object? ApplyImpulse(List<object?> a)
    {
        var body = BodyAt(a, "Physics2D.apply_impulse");
        if (body.Kind == BodyKind.Dynamic)
        {
            Wake(body);
            body.Velocity += new Vector2(Number(a, 2, 0), Number(a, 3, 0)) * body.InverseMass;
        }
        return null;
    }

    public static object? ApplyImpulseAt(List<object?> a)
    {
        var body = BodyAt(a, "Physics2D.apply_impulse_at");
        if (body.Kind == BodyKind.Dynamic)
        {
            Wake(body);
            var impulse = new Vector2(Number(a, 2, 0), Number(a, 3, 0));
            var point = new Vector2(Number(a, 4, body.Position.X), Number(a, 5, body.Position.Y));
            ApplyContactImpulse(body, impulse, point - body.Position);
        }
        return null;
    }

    public static object? ApplyTorque(List<object?> a)
    {
        var body = BodyAt(a, "Physics2D.apply_torque");
        if (body.Kind == BodyKind.Dynamic && !body.RotationLocked)
        {
            Wake(body);
            body.Torque += Number(a, 2, 0);
        }
        return null;
    }

    public static object? ApplyAngularImpulse(List<object?> a)
    {
        var body = BodyAt(a, "Physics2D.apply_angular_impulse");
        if (body.Kind == BodyKind.Dynamic && !body.RotationLocked)
        {
            Wake(body);
            body.AngularVelocity += Number(a, 2, 0) * body.InverseInertia;
        }
        return null;
    }

    public static object? WakeBody(List<object?> a) { Wake(BodyAt(a, "Physics2D.wake")); return null; }
    public static object? IsSleeping(List<object?> a) => !BodyAt(a, "Physics2D.is_sleeping").Awake;

    public static object? IsColliding(List<object?> a) => BodyAt(a, "Physics2D.is_colliding").Contacts.Count > 0;

    public static object? Contacts(List<object?> a) =>
        BodyAt(a, "Physics2D.contacts").Contacts.Order().Select(id => (object?)(double)id).ToList();

    public static object? BodyInfo(List<object?> a)
    {
        var body = TryBodyAt(a, "Physics2D.body_info");
        if (body == null) return null;
        var info = new Dictionary<string, object?>
        {
            ["id"] = (double)body.Id,
            ["type"] = body.Kind.ToString().ToLowerInvariant(),
            ["shape"] = body.Shape.ToString().ToLowerInvariant(),
            ["x"] = (double)body.Position.X, ["y"] = (double)body.Position.Y,
            ["vx"] = (double)body.Velocity.X, ["vy"] = (double)body.Velocity.Y,
            ["rotation"] = (double)RadiansToDegrees(body.Rotation),
            ["angular_velocity"] = (double)RadiansToDegrees(body.AngularVelocity),
            ["mass"] = (double)body.Mass,
            ["inertia"] = (double)body.Inertia,
            ["rotation_locked"] = body.RotationLocked,
            ["sleeping"] = body.Kind == BodyKind.Dynamic && !body.Awake,
            ["linear_damping"] = (double)body.LinearDamping,
            ["angular_damping"] = (double)body.AngularDamping,
            ["bounce"] = (double)body.Restitution,
            ["friction"] = (double)body.Friction,
            ["colliding"] = body.Contacts.Count > 0,
        };
        if (body.Shape == ShapeKind.Circle) info["radius"] = (double)body.Radius;
        else { info["width"] = (double)body.Size.X; info["height"] = (double)body.Size.Y; }
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

    private static Body BodyById(World world, int id, string fn)
    {
        if (id < 0 || id >= world.Bodies.Count || world.Bodies[id] == null)
            throw new MakoError($"{fn}(): invalid body handle {id}");
        return world.Bodies[id]!;
    }

    private static Spring SpringAt(List<object?> a, string fn)
    {
        var world = WorldAt(a, fn);
        int id = Handle(a, 1, fn);
        if (id < 0 || id >= world.Springs.Count || world.Springs[id] == null)
            throw new MakoError($"{fn}(): invalid spring handle {id}");
        return world.Springs[id]!;
    }

    private static (World World, Slime Slime) SlimeAt(List<object?> a, string fn)
    {
        var world = WorldAt(a, fn);
        int id = Handle(a, 1, fn);
        if (id < 0 || id >= world.Slimes.Count || world.Slimes[id] == null)
            throw new MakoError($"{fn}(): invalid slime handle {id}");
        return (world, world.Slimes[id]!);
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
        catch { throw new MakoError("Physics2D: expected a number"); }
    }

    private static float Option(Dictionary<string, object?>? options, string key, float fallback)
    {
        if (options == null || !options.TryGetValue(key, out var value)) return fallback;
        try { return (float)Convert.ToDouble(value); }
        catch { throw new MakoError($"Physics2D.slime(): option '{key}' must be a number"); }
    }

    private static float DegreesToRadians(float degrees) => degrees * MathF.PI / 180f;
    private static float RadiansToDegrees(float radians) => radians * 180f / MathF.PI;

    public static readonly Dictionary<string, Func<List<object?>, object?>> Funcs = new()
    {
        ["world"] = CreateWorld, ["destroy_world"] = DestroyWorld, ["clear"] = ClearWorld,
        ["circle"] = CreateCircle, ["box"] = CreateBox,
        ["remove_body"] = RemoveBody, ["body_count"] = BodyCount,
        ["spring"] = CreateSpring, ["remove_spring"] = RemoveSpring,
        ["spring_count"] = SpringCount, ["spring_info"] = SpringInfo, ["set_spring"] = SetSpring,
        ["slime"] = CreateSlime, ["remove_slime"] = RemoveSlime, ["slime_count"] = SlimeCount,
        ["slime_set_position"] = SetSlimePosition, ["slime_reset"] = SetSlimePosition,
        ["slime_move"] = MoveSlime, ["slime_jump"] = JumpSlime,
        ["slime_hold_jump"] = HoldSlimeJump, ["slime_info"] = SlimeInfo,
        ["step"] = Step, ["set_velocity"] = SetVelocity, ["set_position"] = SetPosition,
        ["set_rotation"] = SetRotation, ["set_angular_velocity"] = SetAngularVelocity,
        ["lock_rotation"] = LockRotation, ["set_damping"] = SetDamping,
        ["wake"] = WakeBody, ["is_sleeping"] = IsSleeping,
        ["apply_force"] = ApplyForce, ["apply_impulse"] = ApplyImpulse,
        ["apply_impulse_at"] = ApplyImpulseAt, ["apply_torque"] = ApplyTorque,
        ["apply_angular_impulse"] = ApplyAngularImpulse,
        ["body_info"] = BodyInfo, ["is_colliding"] = IsColliding, ["contacts"] = Contacts,
    };
}

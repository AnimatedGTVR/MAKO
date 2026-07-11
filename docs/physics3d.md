# Physics3D

Physics3D is MAKO's built-in 3D rigid-body system. Its common API is kept
positional and short: dynamic shapes use their shape name, immovable shapes
use `static_`, and a normal ground plane is simply `floor`.

```mako
using Physics3D;

main() {
    world = Physics3D.world(0, -9.81, 0);
    Physics3D.floor(world);

    crate = Physics3D.box(world, 0, 6, 0, 2, 2, 2);
    ball = Physics3D.sphere(world, 3, 8, 0, 0.5);
    wall = Physics3D.static_box(world, 6, 2, 0, 1, 4, 8);

    Physics3D.material(world, ball, 0.7, 0.3);
    Physics3D.apply_impulse(world, ball, -4, 2, 0);

    while true {
        Physics3D.step(world, 1 / 60);
    }
}
```

## Creating a world

`world(gx=0, gy=-9.81, gz=0, fixed_step=1/60, substeps=4)` creates a world.
The engine automatically adds more substeps for objects moving far enough to
skip thin collision geometry.

MAKO Physics is the default. You may name it when you want the choice to be
obvious: `world = Physics3D.world("mako", 0, -9.81, 0);`. Optional native
adapters use the same form: `Physics3D.world("jolt")`, `world("physx")`, or
`world("bullet")`. MAKO never silently falls back when an adapter is missing.
Use `Physics3D.backend("jolt")` to see its install status. Box2D is intentionally
only available through Physics2D because it is a 2D physics engine.

You can instead select a backend once at the top. This also turns on Physics3D,
so you do not need a second import:

```mako
using JoltPhysics;

main() {
    world = Physics3D.world();
}
```

The other imports are `using PhysX;` and `using BulletPhysics;`. Box2D remains
under `using Physics2D;` because it cannot create a 3D world. Import only one
3D backend per script. MAKO reports a clear startup error if its optional
adapter is unavailable.

For moving rigid bodies, prefer spheres, capsules, boxes, or convex hulls.
Triangle mesh colliders are intended mainly for static level geometry: they are
more expensive, have sharp internal edges, and some backends restrict dynamic
mesh collisions.

## Shapes

- `box(world, x, y, z, width, height, depth, mass=1)`
- `sphere(world, x, y, z, radius, mass=1)`
- `capsule(world, x, y, z, radius, height, mass=1)`
- `static_box`, `static_sphere`, and `static_capsule` use the same geometry arguments.
- `moving_box`, `moving_sphere`, and `moving_capsule` create kinematic shapes
  for elevators, doors, and platforms. Set their motion with `set_velocity`.
- `floor(world, y=0)` creates an infinite upward-facing floor.
- `plane(world, nx, ny, nz, offset)` creates an advanced arbitrary plane.

Use `material(world, body, bounce, friction)` and
`set_rotation(world, body, pitch, yaw, roll)` when a shape needs extra setup.
This keeps the constructor readable instead of hiding normal work in an options
dictionary or a long tail of rarely used arguments.

## Character controller

```mako
player = Physics3D.character(world, 0, 3, 0);
Physics3D.character_tune(world, player, 7, 9, 0.35);

Physics3D.character_move(world, player, move_x, move_z);
if jump_pressed {
    Physics3D.character_jump(world, player);
}
```

The optional character arguments are `height`, `radius`, `speed`, `jump`, and
`air_control`, in that order. The defaults are intended to work without tuning.
Movement includes coyote time, jump buffering, ground snapping, collide-and-slide,
and subdivision for fast motion through thin walls. Characters inherit the
velocity of the body beneath them, so they ride `moving_box` platforms.

## Simple game queries

- `position(world, body)` returns `[x, y, z]`.
- `velocity(world, body)` returns `[vx, vy, vz]`.
- `transform(world, body)` returns `[x, y, z, qx, qy, qz, qw]` for rendering.
- `overlap_sphere(world, x, y, z, radius)` returns every body handle touching
  that area. It is useful for explosions, pickups, checkpoints, and triggers.
- `raycast(world, x, y, z, dx, dy, dz, distance=1000)` returns the nearest
  hit as `{body, distance, x, y, z, nx, ny, nz}`, or `none`. It works with
  spheres, boxes, capsules, and planes.
- `gravity(world, gx, gy, gz)` changes gravity and wakes dynamic bodies.

## Named collision layers

Layers use words instead of bitmasks:

```mako
Physics3D.layer(world, player, "player");
Physics3D.layer(world, ghost, "ghost");
Physics3D.ignore_layer(world, player, "ghost");
```

`ignore_layer(world, body, name, ignored=true)` controls whether one body
ignores another named layer. Pass `false` to turn that collision back on.
`raycast` accepts an optional layer after distance, and `overlap_sphere` accepts
an optional layer after radius.

## Triggers

```mako
pickup = Physics3D.static_box(world, 4, 1, 0, 2, 2, 2);
Physics3D.trigger(world, pickup);

if Physics3D.is_triggered(world, pickup) {
    print "Something entered the pickup";
}
```

A trigger detects overlaps but never pushes bodies away. Characters pass
through triggers and list their handles in `character_info(...)["triggers"]`.

## Spring joints

```mako
joint = Physics3D.spring(world, anchor, weight);
Physics3D.set_spring(world, joint, 3, 60, 8);
```

`spring(world, a, b, rest=-1, strength=80, damping=8)` connects body centers.
The default `rest=-1` captures their current distance. Use `spring_info`,
`spring_count`, `set_spring`, and `remove_spring` to inspect and manage joints.

## Reading and changing bodies

`body_info(world, body)` returns position, velocity, rotation, material, sleep,
grounding, and shape data. Use `set_position`, `set_velocity`, `set_rotation`,
`apply_force`, `apply_impulse`, `apply_impulse_at`, `apply_torque`, and
`apply_angular_impulse` to change a body.

The older constructors with a quoted body type and the dictionary character
constructor remain supported so existing MAKO projects do not break. New code
should use the shorter API above.

## Solver design

Physics3D uses fixed timesteps, adaptive substeps for fast bodies, world-space
inertia for rotated shapes, multi-point box contacts, and sweep-and-prune broad
phase bounds. Capsule/box collision evaluates the full capsule segment rather
than sampling a few locations. These details stay behind the easy API, but are
documented here so engine work can rely on defined behavior.

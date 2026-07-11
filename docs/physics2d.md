# Physics2D — 2D rigid-body simulation

`Physics2D` is MAKO's rendering-independent 2D physics package. It can be used
with `Mako2D` today and embedded into a larger engine later.

## Easy slime API

The normal slime workflow does not expose particles or springs:

```mako
slime = Physics2D.slime(world, 300, 200, 50);

Physics2D.slime_move(world, slime, input);
if jump_pressed {
    Physics2D.slime_jump(world, slime);
}
Physics2D.slime_hold_jump(world, slime, jump_held);

info = Physics2D.slime_info(world, slime);
```

Optional tuning stays in one readable dictionary:

```mako
slime = Physics2D.slime(world, 300, 200, 50, {
    "squish": 0.75,
    "friction": 0.9,
    "mass": 8
});
```

| Function | Description |
|---|---|
| `slime(world, x, y, radius, options={})` | Create a complete slime and return one handle |
| `slime_move(world, slime, direction, speed=240, force=8000)` | Move left/right with `direction` from -1 to 1 |
| `slime_jump(world, slime, speed=380)` | Jump only while grounded; returns whether it jumped |
| `slime_hold_jump(world, slime, held, force=1200)` | Hold for a higher jump; release early for a short jump |
| `slime_info(world, slime)` | Center, velocity, outline, deformation, grounded/sleeping state, and controller state |
| `slime_set_position(world, slime, x, y, reset_velocity=true)` | Move the complete hidden rig safely |
| `slime_reset(...)` | Short alias for `slime_set_position`, intended for checkpoints and falls |
| `slime_count(world)` | Number of live high-level slimes |
| `remove_slime(world, slime)` | Remove the slime and all hidden bodies/springs |

Options include `points` (6–24, default 14), `squish` (0–1), `mass`, `particle_radius`,
`bounce`, `friction`, `stiffness`, `damping`, `speed`, `move_force`,
`air_control`, `jump_speed`, `jump_hold_force`, `coyote_time`, and
`jump_buffer`. `stretch_limit` controls how far a hidden spring may extend
beyond its rest length (default `0.35`, or 35%). Most games should start with
the defaults and change only
`squish`, `friction`, or `speed`.

`shape_recovery` controls hidden area preservation from 0–1 (default `0.65`).
This is the anti-pancake setting: impacts can temporarily compress the slime,
but gravity cannot leave the whole body flattened indefinitely.

Movement accelerates toward a capped speed, uses full traction on the ground,
and reduces steering in the air. Coyote time accepts a jump just after leaving
an edge; jump buffering remembers a slightly early press and fires it on
landing. Both are automatic—game scripts only report button state.

Particles inside the same slime do not collide with one another; the hidden
spring topology defines its shape. Spring impulses are capped per substep and
hard stretch limits prevent a single node from being launched through a thin
platform or producing a long torn-looking polygon spike.

`slime_info` includes `area` and `area_ratio` alongside width/height and
squash/stretch. An `area_ratio` near 1 means the intended volume is preserved;
lower values represent temporary compression.

The default hidden particle radius is derived from the point count, outer slime
radius, and stretch limit. Adjacent perimeter colliders therefore continue to
overlap at maximum stretch: thin platforms cannot pass between nodes and become
trapped inside the spring ring.

```mako
using Physics2D;

main() {
    world = Physics2D.world(0, 980);
    floor = Physics2D.box(world, "static", 400, 580, 800, 40);
    ball = Physics2D.circle(world, "dynamic", 400, 100, 24, 1, 0.7, 0.4);

    while true {
        Physics2D.step(world, 1.0 / 60.0);
        info = Physics2D.body_info(world, ball);
        print "{info[\"x\"]}, {info[\"y\"]}";
    }
}
```

Coordinates describe body centers. Boxes use full width and height, circles use
radius, and script-facing angles use degrees. The solver supports circles and
oriented boxes, including angular collision response.

## Worlds

| Function | Description |
|---|---|
| `world(gravity_x=0, gravity_y=980, fixed_step=1/60, substeps=4)` | Create a world and return its handle |
| `step(world, delta=fixed_step)` | Advance using a fixed-step accumulator; returns the number of simulated steps |
| `body_count(world)` | Number of live bodies |
| `clear(world)` | Remove every body |
| `destroy_world(world)` | Release a world handle |

`step` clamps one call to 0.25 seconds so a debugger pause or dragged window
does not produce a runaway catch-up burst. Each fixed step uses at least the
configured number of internal substeps. Fast bodies automatically raise that
count (up to 64) based on collider size and travel distance, substantially
reducing tunnelling through thin colliders.

## Bodies

Body type is `"dynamic"`, `"static"`, or `"kinematic"`.

| Function | Description |
|---|---|
| `circle(world, type, x, y, radius, mass=1, bounce=0.2, friction=0.4, rotation=0)` | Create a circle collider |
| `box(world, type, x, y, width, height, mass=1, bounce=0.2, friction=0.4, rotation=0)` | Create an oriented box collider |
| `remove_body(world, body)` | Remove a body |
| `set_position(world, body, x, y)` | Teleport a body |
| `set_velocity(world, body, vx, vy)` | Set linear velocity |
| `apply_force(world, body, fx, fy)` | Add force for the next fixed step |
| `apply_impulse(world, body, ix, iy)` | Immediately change velocity according to mass |
| `set_rotation(world, body, degrees)` | Set body rotation |
| `set_angular_velocity(world, body, degrees_per_second)` | Set angular velocity |
| `apply_torque(world, body, torque)` | Add torque for the next fixed step |
| `apply_angular_impulse(world, body, impulse)` | Immediately change spin according to shape inertia |
| `apply_impulse_at(world, body, ix, iy, world_x, world_y)` | Apply an impulse at a world point, producing linear and angular motion |
| `lock_rotation(world, body, locked=true)` | Enable or disable angular response |
| `set_damping(world, body, linear, angular)` | Set drag coefficients; defaults are `0.08` and `0.12` |
| `wake(world, body)` | Manually wake a sleeping dynamic body |
| `is_sleeping(world, body)` | Whether a settled dynamic body is asleep |

Static bodies never move. Kinematic bodies follow their linear/angular velocity
but ignore gravity and forces. Dynamic bodies respond to gravity, forces,
torque, impulses, and collisions. Circle inertia is `mass * radius² / 2`; box
inertia is `mass * (width² + height²) / 12`.

Dynamic bodies with very low linear and angular velocity sleep after 0.6
seconds. Forces, impulses, teleports, velocity changes, moving connected bodies,
and kinematic collisions wake them. This keeps resting stacks from accumulating
tiny solver motion indefinitely.

## Spring joints

Springs connect two bodies at optional local-space anchor points. They apply
equal and opposite forces using Hooke stiffness plus velocity damping, and can
produce torque when their anchors are off-center.

| Function | Description |
|---|---|
| `spring(world, a, b, rest=-1, stiffness=120, damping=12, ax=0, ay=0, bx=0, by=0)` | Create a spring; `rest=-1` captures its current length |
| `spring_info(world, spring)` | Bodies, world-space endpoints, current/rest length, stiffness, and damping |
| `set_spring(world, spring, rest, stiffness, damping)` | Retune a spring at runtime |
| `spring_count(world)` | Number of live springs |
| `remove_spring(world, spring)` | Remove a spring |

Removing a body also removes every spring attached to it. A ring of circle
bodies connected by edge, brace, and cross springs forms a useful first slime
rig. These low-level calls remain available for advanced physics tools; ordinary
game code should prefer `slime()`.

## Inspection and contacts

| Function | Description |
|---|---|
| `body_info(world, body)` | Dict containing type, shape, position, rotation, velocities, mass/inertia, damping, sleep state, material, dimensions, and contacts |
| `is_colliding(world, body)` | Whether the body touched anything during the latest `step` call |
| `contacts(world, body)` | List of body handles touched during the latest `step` call |

Run `mko examples/physics_2d.mko` for the interactive sandbox.

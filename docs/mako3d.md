# Mako3D — 3D rendering

```mako
using Mako3D;

main() {
    Mako3D.init(800, 600, "3D");
    Mako3D.fps(60);
    cam = Mako3D.camera(10, 8, 10,  0, 0, 0);   # position, target

    while Mako3D.running() {
        Mako3D.update_camera(cam, 8);           # WASD + mouse controls

        Mako3D.begin();
        Mako3D.clear(Mako3D.color(18, 18, 28));

        Mako3D.begin_3d(cam);
        Mako3D.grid(20, 1);
        Mako3D.cube(0, 1, 0,  2, 2, 2,  Mako3D.RED);
        Mako3D.end_3d();

        Mako3D.text("HUD text draws after end_3d", 10, 10, 18, Mako3D.WHITE);
        Mako3D.end();
    }
    Mako3D.close();
}
```

Lifecycle (`init fps running begin end close delta get_fps width height
title draw_fps`) and colors (`color`, `fade`, 21 named constants) work
exactly like [Mako2D](mako2d.md).

## Camera3D

| Function | Description |
|---|---|
| `camera(px, py, pz, tx, ty, tz, fov=45)` | Create → handle |
| `update_camera(cam, speed=5)` | **Interactive controls in one call:** WASD/arrows fly, Q/E up/down, middle-mouse drag orbits, scroll zooms |
| `move_camera(cam, px, py, pz, tx, ty, tz)` | Set position + target directly (camera-follow) |
| `orbit_camera(cam, angle_deg, distance, height)` | Circle around the target |
| `camera_pos(cam)` | `[x, y, z]` — feed this to positional audio as the listener |

Everything between `begin_3d(cam)` and `end_3d()` renders in 3D; text and
2D shapes drawn after `end_3d()` become the HUD.

## Primitives

| Function | Description |
|---|---|
| `cube(x,y,z, w,h,d, color)` | Cube with black wireframe |
| `cube_raw(...)` | Cube without wireframe |
| `sphere(x,y,z, r, color)` / `sphere_raw(...)` | Sphere |
| `cylinder(x,y,z, r_top, r_bottom, height, color)` | Cylinder |
| `plane(x,y,z, width, depth, color)` | Flat ground plane |
| `grid(slices, spacing)` | Reference grid at y=0 |
| `line3d(x1,y1,z1, x2,y2,z2, color)` | 3D line — great for debug rays/paths |
| `point3d(x,y,z, color)` | Single point |

## Models

| Function | Description |
|---|---|
| `load_model(path)` | Load `.obj` / `.glb` → handle |
| `draw_model(model, x, y, z, scale, color)` | Draw it |

## Scene / objects

The primitives above are immediate-mode — draw calls you repeat every
frame. For anything you don't want to hand-redraw each frame (props, a
level's static geometry, enemies), spawn it once and let `draw_scene()`
draw everything that's still registered:

```mako
cube = Mako3D.spawn_cube(0, 1, 0,  2, 2, 2, Mako3D.RED);
Mako3D.spawn_plane(0, 0, 0,  20, 20, Mako3D.DARKGRAY);

while Mako3D.running() {
    Mako3D.set_object_rotation(cube, t * 40);   # mutate by handle, any frame

    Mako3D.begin(); Mako3D.clear(Mako3D.BLACK);
    Mako3D.begin_3d(cam);
    Mako3D.draw_scene();                         # draws every spawned object
    Mako3D.end_3d();
    Mako3D.end();
}
```

| Function | Description |
|---|---|
| `spawn_cube(x,y,z, w,h,d, color)` | → handle |
| `spawn_sphere(x,y,z, r, color)` | → handle |
| `spawn_cylinder(x,y,z, r_top, r_bottom, height, color)` | → handle |
| `spawn_plane(x,y,z, width, depth, color)` | → handle |
| `set_object_pos(h, x, y, z)` | Move it |
| `set_object_color(h, color)` | Recolor it |
| `set_object_scale(h, x, y, z)` | Resize it (meaning depends on shape — see below) |
| `set_object_rotation(h, degrees)` | Rotate around the Y axis (v1 — Y-axis only) |
| `set_object_visible(h, bool)` | Hide/show without removing |
| `set_object_wires(h, bool)` | Toggle the black wireframe outline |
| `remove_object(h)` | Remove permanently |
| `clear_objects()` | Remove everything |
| `object_count()` | How many are currently registered |
| `object_bounds(h)` | `[min_x,min_y,min_z, max_x,max_y,max_z]` — an axis-aligned box for collision, or `none` if removed |
| `draw_scene()` | Draw every visible spawned object — call between `begin_3d()`/`end_3d()` |

`set_object_scale`'s three numbers mean different things per shape:
cube = width/height/depth, sphere = radius (first number only), cylinder =
radius_top/radius_bottom/height, plane = width/(unused)/depth.

Spawning, mutating, removing, and counting objects are pure data operations
— they work even before a window is open, so they're fully unit-testable
(see `tests/mako3d_scene.mko`). Only `draw_scene()` needs an active
`begin_3d()` context.

Collision helpers for 3D scenes:

```mako
dist3d(x1, y1, z1, x2, y2, z2)                          # 3D distance
box3d_overlap(min1x,min1y,min1z, max1x,max1y,max1z,      # two AABBs —
              min2x,min2y,min2z, max2x,max2y,max2z)      # object_bounds()'s own format
```

See `examples/scene_demo.mko` for a scene with dozens of objects, a
rotating handle-driven pillar, and runtime removal.

## Input extras

Mirrors of the [Inputs package](inputs.md) plus `mouse_delta_x()` /
`mouse_delta_y()` and `hide_cursor()` / `show_cursor()` for mouse-look.

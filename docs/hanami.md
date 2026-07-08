# Hanami — the lighting engine

Hanami is MAKO's lighting engine — the beginning of a full game-engine layer
built on Mako3D/Mako2D. It's **headless**: no window of its own, just the
math for lights, shadows, baking, and voxel light propagation. You call
`Hanami.light_at(x, y, z)` for any point in your scene and use the result to
tint your own `Mako3D`/`Mako2D` draw calls.

```mako
using Mako3D;
using Hanami;

main() {
    Mako3D.init(800, 600, "Lit Scene");
    Hanami.set_mode("realtime");
    Hanami.set_ambient(0.06, 0.06, 0.09, 1);
    Hanami.add_light("point", 0, 5, 0,  1, 0.9, 0.7,  3.0, 15);

    cam = Mako3D.camera(8, 6, 8,  0, 0, 0);
    while Mako3D.running() {
        Mako3D.update_camera(cam, 8);
        Mako3D.begin();
        Mako3D.clear(Mako3D.BLACK);
        Mako3D.begin_3d(cam);

        lit = Hanami.light_at(0, 1, 0);
        col = Hanami.shade(Mako3D.RED, lit);
        Mako3D.cube(0, 1, 0,  2, 2, 2, col);

        Mako3D.end_3d();
        Mako3D.end();
    }
}
```

## Why headless?

`Mako3D`/`Mako2D` open a **raylib** window; `MakoUI` opens a separate
**Silk.NET** window — they can't share one OS window. Hanami sidesteps this
by doing no rendering at all. That also means it can run **inside a MakoUI
tool** with no conflict, which is exactly what the Lighting Manager below
does — edit lighting visually, save it, and your Mako3D game loads the same
config at startup.

## Modes — `Hanami.set_mode(name)`

| Mode | What it means | When to use it |
|---|---|---|
| `"unlit"` | No lighting at all — `light_at()` always returns full white | UI, debug views, retro/flat-shaded looks |
| `"baked"` | Lighting precomputed once by `bake()` into a probe grid, looked up (not recomputed) every frame | Static levels/scenes that don't change |
| `"realtime"` | Every enabled light recomputed every frame, with shadow raycasts | Moving lights, flashlights, muzzle flashes, enemy glow |
| `"mixed"` | Baked probes for lights flagged static + realtime for the rest | The normal case for most games — static world lighting, dynamic characters/effects |
| `"voxel"` | Grid-based light propagation for block worlds | Minecraft-ish scenes, chunk lighting |

## Lights

```mako
h = Hanami.add_light("point", x, y, z,  r, g, b,  intensity, range, is_static);
h = Hanami.add_light("directional", dx, dy, dz,  r, g, b,  intensity);
```

- **`point`** lights fall off with distance (squared falloff, zero past `range`).
- **`directional`** lights (sun/moon) don't fall off — `dx,dy,dz` is a
  direction, not a position.
- **`is_static`** (point lights only, default `false`) marks a light as
  eligible for baking. In `"mixed"` mode, static lights come from the bake
  and non-static ones are computed live every frame.

| Function | Description |
|---|---|
| `add_light(type, x,y,z, r,g,b, intensity=1, range=10, is_static=false)` | → handle |
| `remove_light(h)` | Remove |
| `set_light_pos/color/intensity/range(h, ...)` | Move or retune a light live |
| `set_light_enabled(h, bool)` | Toggle without removing |
| `light_count()` | Active light count |
| `light_info(h)` | Dict of the light's current fields, or `none` if removed |
| `clear_lights()` | Remove all lights |

## Evaluating light

```mako
lit = Hanami.light_at(x, y, z);          # → [r, g, b], your mode's math applied
col = Hanami.shade(Mako3D.RED, lit);     # → a ready-to-draw [r,g,b,a] color
```

`shade(base_color, light_rgb)` multiplies a normal 0–255 color by the light
result and clamps back to 0–255 — the usual one-liner between `light_at()`
and a draw call.

## Shadows — occluders

```mako
Hanami.add_occluder(x, y, z,  width, height, depth);   # an axis-aligned box
```

Any point/directional light contribution is skipped if an occluder box
sits between the sample point and the light (a proper ray/AABB shadow
test — not just a distance check). `remove_occluder(h)` / `clear_occluders()`
to manage them.

## Baking

```mako
Hanami.bake(min_x, min_y, min_z,  max_x, max_y, max_z,  resolution=6);
```

Computes a `resolution³` grid of light probes across the box, using only
`is_static` lights and current occluders. `light_at()` in `"baked"` mode
looks up the nearest probe instead of recomputing lighting — much cheaper
for scenes that don't move. `is_baked()` reports whether `bake()` has run.

Re-bake whenever static lights, occluders, or the mode-relevant geometry
change; a bake is a snapshot, not a live subscription.

## Voxel lighting

```mako
Hanami.voxel_init(size_x, size_y, size_z);
Hanami.voxel_set_solid(x, y, z, true);       # a wall/floor block
Hanami.voxel_set_emissive(x, y, z, 15);      # a torch — 0-15 light level
Hanami.voxel_bake();                          # propagate (Minecraft-style BFS)

level = Hanami.voxel_light(x, y, z);          # 0-15
col   = Hanami.voxel_color(x, y, z, base_color);  # scaled + ready to draw
```

Light spreads outward from every emissive cell through non-solid neighbors,
losing one level per step (a multi-source breadth-first flood fill, the same
technique Minecraft uses) — call `voxel_bake()` again after changing solid
or emissive cells.

## The Lighting Manager (`examples/hanami_lighting_manager.mko`)

A MakoUI-based visual editor: place up to 6 lights, tune ambient, set the
mode, bake, and save. Run it, build your lighting, then:

```mako
using Hanami;
main() {
    Hanami.load_config("hanami_config.json");
    # mode, ambient, lights, and occluders are now set up exactly as designed
}
```

| Function | Description |
|---|---|
| `save_config(path="hanami_config.json")` | Write mode/ambient/lights/occluders as JSON |
| `load_config(path)` | Replace current state from a saved config |
| `reset()` | Clear everything back to defaults |

## Full demo (`examples/hanami_demo.mko`)

One scene, five lighting modes, switchable live with keys **1–5**:
unlit → baked → realtime (orbiting light with real shadows) → mixed →
a small voxel room with a torch. Press **L** to load a config you built in
the Lighting Manager.

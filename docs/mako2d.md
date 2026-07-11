# Mako2D — 2D rendering

```mako
using Mako2D;

main() {
    Mako2D.init(800, 600, "Window Title");
    Mako2D.fps(60);
    while Mako2D.running() {
        Mako2D.begin();
        Mako2D.clear(Mako2D.BLACK);
        # ... draw here ...
        Mako2D.end();
    }
    Mako2D.close();
}
```

## Lifecycle

| Function | Description |
|---|---|
| `init(w, h, title)` | Open the window |
| `fps(n)` | Cap the frame rate (`0` = uncapped) |
| `running()` | `false` once the window should close |
| `begin()` / `end()` | Frame brackets — draw between them |
| `close()` | Close the window |
| `delta()` | Seconds since last frame — multiply all movement by this |
| `get_fps()` | Current FPS |
| `width()` / `height()` | Window size |
| `title(s)` | Change the window title |
| `draw_fps(x, y)` | Built-in FPS counter |

## Colors

21 named constants: `BLACK WHITE RED GREEN BLUE YELLOW ORANGE PURPLE PINK
GRAY DARKGRAY LIGHTGRAY SKYBLUE BROWN BEIGE LIME GOLD VIOLET MAROON BLANK
RAYWHITE` — used as `Mako2D.RED`.

| Function | Description |
|---|---|
| `color(r, g, b, a=255)` | Custom color (0–255 channels) |
| `fade(color, alpha)` | Copy with alpha 0–1 |

Colors are `[r, g, b, a]` lists — you can build them by hand.

## Shapes & text

| Function | Description |
|---|---|
| `clear(color)` | Fill the background |
| `text(s, x, y, size, color)` | Draw text |
| `rect(x, y, w, h, color)` | Filled rectangle |
| `rect_lines(x, y, w, h, color)` | Outline |
| `rect_round(x, y, w, h, roundness, segments, color)` | Rounded rectangle |
| `rect_rot(cx, cy, w, h, rotation, color)` | Filled rectangle rotated around its center |
| `rect_rot_lines(cx, cy, w, h, rotation, color)` | Outline of a center-anchored rotated rectangle |
| `circle(x, y, r, color)` / `circle_lines(...)` | Circle |
| `line(x1, y1, x2, y2, color)` | Line |
| `triangle(x1,y1, x2,y2, x3,y3, color)` | Filled triangle (counter-clockwise) |

## Sprites & textures

| Function | Description |
|---|---|
| `load(path)` | Load a PNG/JPG → texture handle |
| `sprite(tex, x, y, scale=1, tint=WHITE)` | Draw a texture |
| `sprite_frame(tex, x, y, fx, fy, fw, fh, scale=1, tint)` | Draw a sub-rectangle — spritesheet animation |
| `sprite_rot(tex, x, y, rotation_deg, scale=1, tint)` | Rotated around its centre |
| `tex_width(tex)` / `tex_height(tex)` | Texture size |
| `unload(tex)` | Free it |

Asset paths resolve relative to the script's own directory, so
`Mako2D.load("assets/player.png")` works from anywhere.

Spritesheet animation pattern:

```mako
frame = 0; anim_t = 0;
# in the loop:
anim_t = anim_t + Mako2D.delta();
if anim_t > 0.12 { anim_t = 0; frame = (frame + 1) % 4; }
Mako2D.sprite_frame(sheet, x, y, frame * 32, 0, 32, 32, 2);
```

## Camera2D

| Function | Description |
|---|---|
| `camera(target_x, target_y, zoom=1, rotation=0)` | Create → handle |
| `set_camera(cam, x, y, zoom, rotation)` | Move / zoom it |
| `begin_cam(cam)` / `end_cam()` | Draw world-space content between these |
| `screen_to_world(cam, sx, sy)` | Mouse position → world coords `[x, y]` |

Draw the world inside `begin_cam`/`end_cam`, and HUD text after `end_cam`.

## Input (convenience mirror)

`key_down/key_pressed/key_released(key)`, `mouse_x() mouse_y()`,
`mouse_down/mouse_pressed(btn)`, `mouse_wheel()` — same behavior as the
[Inputs package](inputs.md), which has the full set.

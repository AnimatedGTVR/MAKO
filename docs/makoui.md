# MakoUI — desktop UIs (Dear ImGui)

Immediate-mode GUI for tools, apps, and in-game toolbars. Widgets are drawn
every frame; the functions return the new value / whether they were
activated.

MakoUI runs in one of two modes:

## Standalone window

Its own window (Silk.NET/OpenGL) — for tools and editors that don't need a
3D/2D scene.

```mako
using MakoUI;

main() {
    MakoUI.init("My App", 900, 600);
    MakoUI.theme_mako();                 # cherry blossom theme

    count = 0;
    while MakoUI.running() {
        MakoUI.begin();

        MakoUI.begin_window("Controls");
        MakoUI.text("Count: {count}");
        if MakoUI.button("Increment") { count = count + 1; }
        MakoUI.end_window();

        MakoUI.end();
    }
}
```

## Embedded in a Mako3D/Mako2D window

No second window — MakoUI renders directly into the same raylib window as
your 3D/2D scene, using raylib's own draw calls (`Rlgl`) instead of a
separate GL context. This is what makes a real in-game toolbar possible:
panels, tabs, and an FPS counter that sit right over your scene.

```mako
using Mako3D;
using MakoUI;

main() {
    Mako3D.init(1280, 720, "My Game");
    MakoUI.attach();                     # attach to the window Mako3D opened
    MakoUI.theme_mako();

    cam = Mako3D.camera(8, 6, 8,  0, 0, 0);

    while Mako3D.running() {
        Mako3D.update_camera(cam, 8);

        Mako3D.begin();                  # Mako3D owns clearing + swapping
        Mako3D.clear(Mako3D.BLACK);
        Mako3D.begin_3d(cam);
        Mako3D.cube(0, 1, 0,  2, 2, 2, Mako3D.RED);
        Mako3D.end_3d();

        MakoUI.begin();                  # starts the ImGui frame only
        MakoUI.begin_window("Toolbar");
        MakoUI.fps_counter();
        MakoUI.end_window();
        MakoUI.end();                    # renders ImGui only

        Mako3D.end();                    # Mako3D swaps buffers once
    }
}
```

Order matters: draw your 3D/2D scene first, then `MakoUI.begin()` /
widgets / `MakoUI.end()`, then close with the renderer's own `end()` — that
way the UI draws on top of the scene in the same frame. `MakoUI.attach()`
requires a window to already be open (`Mako3D.init()`/`Mako2D.init()` first)
and errors clearly if none exists.

## Lifecycle & windows

`init(title, w, h)` (standalone) · `attach()` (embedded) · `running()` ·
`begin()` / `end()` · `begin_window(title)` / `end_window()` ·
`set_window_size(w, h)` · `set_window_pos(x, y)` ·
`begin_window_menu(title)` (window with a menu bar)

## FPS counter

```mako
MakoUI.fps_counter();
```

A real, self-tracked widget — not a borrowed or static number: a 90-frame
rolling buffer of actual frame times, a color-coded readout (green ≥55,
gold ≥30, red below), the frame time in ms, and a live line graph. Works
in both standalone and embedded mode.

## Tabs

```mako
if MakoUI.begin_tab_bar("main_tabs") {
    if MakoUI.begin_tab_item("Scene") {
        MakoUI.text("Scene controls here");
        MakoUI.end_tab_item();
    }
    if MakoUI.begin_tab_item("Settings") {
        MakoUI.text("Settings here");
        MakoUI.end_tab_item();
    }
    MakoUI.end_tab_bar();
}
```

Only the content inside the active tab's `begin_tab_item`/`end_tab_item`
block renders — good for grouping settings inside an inspector-style panel.
See `examples/embedded_ui_demo.mko`.

## Toolbar

```mako
MakoUI.begin_toolbar("main_toolbar", 48);   # id, height (default 44)

if MakoUI.button("Pause") { ... }
MakoUI.same_line();
MakoUI.separator();                          # renders as a vertical divider
MakoUI.same_line();
if MakoUI.button("Grid: On") { ... }

MakoUI.end_window();                         # any Begin() closes this way
```

A real toolbar (not a resizable floating window pretending to be one): pinned
to the top of the viewport, spanning its full width, no title bar, can't be
dragged/resized/scrolled. `same_line()` chains buttons into a row;
`separator()` right after `same_line()` draws as a thin vertical divider
between button groups instead of a horizontal rule.

A common toolbar pattern — an FPS cap selector, cycling through presets on
click:

```mako
const FPS_CAPS   = [30, 60, 120, 144, 240, 0];   # 0 = uncapped
const FPS_LABELS = ["30", "60", "120", "144", "240", "Uncapped"];
cap_i = 5;

if MakoUI.button("FPS: {FPS_LABELS[cap_i]}##fps_cap") {
    cap_i = (cap_i + 1) % len(FPS_CAPS);
    Mako3D.fps(FPS_CAPS[cap_i]);             # 0 = uncapped in Mako3D too
}
```

(The `##fps_cap` suffix gives the button a stable ID independent of its
changing label text — standard practice for any button whose text updates.)

If a `begin_main_menu_bar()` is also present, `begin_toolbar()` automatically
sits just below it — the viewport's work area already excludes the menu
bar's height.

## Widgets

Value widgets **return the updated value** — assign it back:

```mako
name    = MakoUI.input_text("Name", name);
volume  = MakoUI.slider("Volume", volume, 0, 1);
enabled = MakoUI.checkbox("Enabled", enabled);
choice  = MakoUI.combo("Fruit", choice, ["Apple", "Banana", "Cherry"]);
```

| Widget | Notes |
|---|---|
| `text(s)` / `text_colored(s, r, g, b, a=1.0)` | Labels — colors are 0-1 floats (ImGui convention), not 0-255 |
| `button(label)` / `small_button(label)` | `true` when clicked |
| `checkbox(label, value)` | Returns new bool |
| `slider(label, value, min, max)` / `slider_int(...)` | Returns new value |
| `drag(label, value, speed)` / `drag_int(...)` | Drag number fields |
| `drag_range(label, lo, hi, speed=1.0)` | Two-handle drag widget for a `[lo, hi]` range — returns updated `[lo, hi]` |
| `input_text(label, value)` / `input_text_multi(label, value, lines)` | Text fields |
| `input_number(label, value)` | Numeric field |
| `combo(label, index, items_list)` | Dropdown, returns new index |
| `progress(fraction)` | Progress bar 0–1 |
| `collapsing(label)` | Collapsible header — `true` while open |
| `color_picker(label, r, g, b)` | A wheel-style picker (hue ring + saturation/value square) — returns updated `[r, g, b]` |

`color_picker` takes and returns 0-255 channels, matching every other color
convention in MAKO, so its result plugs straight into `Mako3D.color(...)` or
`Mako2D.color(...)`:

```mako
col = MakoUI.color_picker("Object color##picker", col[0], col[1], col[2]);
Mako3D.set_object_color(selected, Mako3D.color(col[0], col[1], col[2]));
```

`drag_range` works the same way — assign both handles back from the returned list:

```mako
range = [10, 90];
range = MakoUI.drag_range("Spawn range", range[0], range[1]);
```

## Layout

`separator()` · `same_line()` · `spacing()` · `new_line()`

## Menus

```mako
if MakoUI.begin_main_menu_bar() {
    if MakoUI.begin_menu("File") {
        if MakoUI.menu_item("Quit", "Ctrl+Q") { ... }
        MakoUI.end_menu();
    }
    MakoUI.end_main_menu_bar();
}
```

Per-window menu bars: `begin_menu_bar()` / `end_menu_bar()` inside a window
created with `begin_window_menu`.

## Popups & modals

```mako
if MakoUI.button("Delete...") { MakoUI.open_popup("confirm"); }
if MakoUI.begin_modal("confirm") {
    MakoUI.text("Are you sure?");
    if MakoUI.button("Yes") { MakoUI.close_popup(); }
    MakoUI.end_popup();
}
```

`begin_modal(id)` blocks interaction with the rest of the UI until closed —
use it for confirmations. `begin_popup(id)` is the non-modal form: it opens
at the current position, stays interactive alongside the rest of the UI,
and closes automatically on an outside click.

```mako
if MakoUI.button("Options") { MakoUI.open_popup("opts"); }
if MakoUI.begin_popup("opts") {
    if MakoUI.button("Save")   { MakoUI.close_popup(); }
    if MakoUI.button("Cancel") { MakoUI.close_popup(); }
    MakoUI.end_popup();
}
```

## Tables

```mako
if MakoUI.begin_table("data", 3) {
    MakoUI.table_column("Name");
    MakoUI.table_column("Score");
    MakoUI.table_column("Rank");
    MakoUI.table_header_row();
    for row in rows {
        MakoUI.table_next_row();
        MakoUI.table_next_col(); MakoUI.text(row["name"]);
        MakoUI.table_next_col(); MakoUI.text("{row[\"score\"]}");
        MakoUI.table_next_col(); MakoUI.text("{row[\"rank\"]}");
    }
    MakoUI.end_table();
}
```

## Tooltips, queries, themes

- `tooltip(text)` — attach to the previous widget (shows on hover);
  `set_tooltip(text)` — unconditional
- `is_hovered()` / `is_clicked()` — state of the previous widget
- `is_key_pressed(key)` · `get_time()` · `framerate()`
- `wants_mouse()` — true while the mouse is over any MakoUI panel/widget;
  check this before your own 3D picking so a click on the UI doesn't also
  select an object underneath it (`if Inputs.mouse_pressed("left") and not
  MakoUI.wants_mouse() { selected = Mako3D.pick_object(cam); }`)
- Themes: `theme_dark()` · `theme_light()` · `theme_mako()` (cherry blossom)
- Fine styling: `push_color(idx, r, g, b, a)` / `pop_color(n)` ·
  `push_var(idx, value)` / `pop_var(n)`

See `examples/ui_demo.mko` for the standalone-window widget tour, and
`examples/embedded_ui_demo.mko` for the embedded toolbar-over-a-3D-scene
pattern (tabs, FPS counter, live scene controls).


# MAKO

**A simple, sharp programming language — with game engine tools built in.**

MAKO blends the readability of Python, the simplicity of Lua, and the structure of C-style languages. Write a script, run it with `mko`, and you have windows, 2D/3D graphics, synthesized sound, input, UI, and early game logic tools — no project files, no build step, no asset pipeline required.

> **Status:** v0.1.0 — the first official release. Structs & methods, dicts, lambdas, try/catch, formatter, REPL, type hints, `mko check`, a versioned package manager, and native packages: **MakoUI**, **Mako2D**, **Mako3D**, **Inputs**, **Audio**, **Net**, and **System**. Builds ship for Linux, Windows, macOS, and the web (WebAssembly, language-only).
>
> MAKO is still young — syntax, APIs, and examples may still change — but it's no longer pre-release.

> **Design rule:** If something is hard to type, it is hard to understand.
> MAKO keeps advanced machinery available underneath, but its normal APIs must
> make the common path short, readable, and obvious.

---

## Screenshots

<p align="center">
  <img src="Images/image.png" alt="MAKO Foundry game builder" width="92%">
</p>

<p align="center">
  <strong>Foundry — build games without exposing the toolchain.</strong>
</p>

<p align="center">
  <img src="Images/image%20copy.png" alt="MAKO Physics2D soft-body slime and command log" width="45%">
  <img src="Images/image%20copy%202.png" alt="MAKO Physics3D rigid-body playground" width="45%">
</p>

<p align="center">
  <img src="Images/image%20copy%204.png" alt="MAKO Physics3D sandbox with embedded MakoUI controls" width="70%">
</p>

<p align="center">
  <img src="Images/image%20copy%206.png" alt="A readable Pong game written in MAKO" width="52%">
</p>

<p align="center">
  <img src="Images/image%20copy%205.png" alt="mko check reporting that a MAKO script has no issues" width="60%">
</p>

---

## Example

```mako
using Mako2D;
using Inputs;
using Audio;

main() {
    Mako2D.init(800, 600, "My Game");
    Mako2D.fps(60);

    beep = Audio.tone("square", 440, 0.1); # synthesized — no sound files
    x = 400;

    while Mako2D.running() {
        if Inputs.key_down("RIGHT") {
            x = x + 300 * Mako2D.delta();
        }

        if Inputs.key_pressed("SPACE") {
            Audio.play(beep);
        }

        Mako2D.begin();
        Mako2D.clear(Mako2D.BLACK);
        Mako2D.circle(x, 300, 24, Mako2D.SKYBLUE);
        Mako2D.end();
    }
}
````

---

## Install

Needs the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and git.

```bash
# Arch / Abora
sudo pacman -S dotnet-sdk

# Debian / Ubuntu
sudo apt install dotnet-sdk-8.0
```

```bash
git clone https://github.com/AnimatedGTVR/MAKO
cd MAKO
./build.sh install
```

That installs `mko` to `~/.local/bin`, along with native libraries and examples.

Then, from anywhere:

```bash
mko hello.mko
mko pong.mko
mko repl
```

Scripts can also be made executable directly:

```mako
#!/usr/bin/env mko

main() {
    print "hello!";
}
```

---

## Try the games

All examples install globally. Run these from any directory:

| Command                | What it is                                                               |
| ---------------------- | ------------------------------------------------------------------------ |
| `mko block_world.mko`  | LEGO-like 3D building sandbox — Roblox-style character, real block collisions, colors, controller tools, and an auto-saved world |
| `mko bike_simulator.mko` | 3D bicycle time trial — active balance physics, falls, keyboard/gamepad steering, skids, chase camera, sound, and saved best laps |
| `mko pong.mko`         | Pong vs an AI paddle — synth sound and positional audio                  |
| `mko snake.mko`        | Snake — speeds up as you grow with pitch-shifting eat sound              |
| `mko gem_hunter.mko`   | 3D arena collector — camera follow, patrol enemies, and audio navigation |
| `mko slime_platformer.mko` | Googly-eyed Physics2D platformer prototype — squishy movement, collectibles, pits, platforms, and a goal |
| `mko monster_maze.mko` | Stealth game — monster AI with vision cone, hearing, and A* pathfinding  |
| `mko music_maker.mko`  | Playable synth piano — 5 waveforms and melody baking                     |
| `mko settings.mko`     | FPS overlay, graph, audio sliders, and FPS cap selector                  |
| `mko ui_demo.mko`      | Dear ImGui desktop UI — menus, tables, modals, cherry-blossom theme      |
| `mko embedded_ui_demo.mko` | MakoUI embedded live in a 3D scene — menu bar, toolbar, click an object to edit it live in the Inspector, no second window |
| `mko scene_demo.mko` | Mako3D's scene/object system — spawn dozens of objects once, `draw_scene()` handles the rest, click to select |
| `mko mesh_edit_demo.mko` | Blender-style Edit Mode — see and pick an object's actual vertices/edges/faces |
| `mko sound_3d.mko`     | 3D positional audio — beacons that pan and fade as you fly               |
| `mko input_test.mko`   | Visual input tester — every key/button lights up live                    |

---

## The language in 60 seconds

```mako
script "Tour";

const MAX = 100;

fn greet(name) {
    return "Hello, {name}!";
}

main() {
    # Types: numbers, strings, booleans, none, lists, dicts
    nums = [3, 1, 2];
    user = {
        "name": "Robin",
        "score": 42
    };

    # Lambdas + higher-order builtins
    doubled = map(nums, fn(x) => x * 2);
    big = filter(nums, fn(x) => x > 1);

    # Control flow
    for n in nums {
        print n;
    }

    while user["score"] < MAX {
        user["score"] = user["score"] + 10;
    }

    # Error handling
    try {
        assert(false, "boom");
    } catch err {
        print "oops: {err}";
    }

    # Files, shell, time
    write("save.txt", "data");
    run "echo from the shell";
    print time();
}
```

Errors point at the problem and suggest fixes:

```txt
  print greeet("World");
        ^^^^^^
mko: error (line 4): function 'greeet' wasn't found
  hint: did you mean 'greet'?
```

---

## CLI

| Command                                     | Does                                    |
| ------------------------------------------- | --------------------------------------- |
| `mko <file.mko>` / `mko run <file>`         | Run a script                            |
| `mko repl`                                  | Interactive REPL                        |
| `mko test [dir]`                            | Run every `*.mko` file under `tests/` as a regression suite |
| `mko fmt <file>` / `mko fmt <file> --check` | Format source while preserving comments |
| `mko foundry [project]`                     | Open the MakoUI game builder |
| `mko build [project] --target linux-x64`    | Export a self-contained game |
| `mko get <pkg> [github:User/Repo]`          | Install a package                       |
| `mko list`                                  | List installed packages                 |
| `mko cache clear`                           | Clear the package cache                 |

MAKO's compiler is beginning its bootstrap into MAKO itself. See the
[self-hosting plan](docs/self-hosting.md) and the first hosted runtime module in
[`selfhost/runtime.mko`](selfhost/runtime.mko).

Packages can come from GitHub:

```mako
using mylib from "github:User/Repo";
```

### Foundry

Foundry turns a MAKO game into a distributable application through a MakoUI
frontend or the same backend from the CLI. Linux portable exports work now;
Windows `.exe`, AppImage, APK, macOS, Web, VR, and console adapters share the
same target model and will arrive incrementally. See [Foundry](docs/foundry.md).

---

## Native packages

| Package         | What it gives you                                                                    |
| --------------- | ------------------------------------------------------------------------------------ |
| `using Mako2D;` | 2D games: sprites, spritesheets, Camera2D, shapes, text                              |
| `using Mako3D;` | 3D games: Camera3D, fly/orbit controls, cubes, spheres, models, grid — plus a spawn/draw_scene() object system so you stop hand-redrawing static geometry every frame |
| `using Models;` | Load your `.glb` / `.obj` files once and draw them by a readable name |
| `using Players;` | Local multiplayer for players 1–4 with keyboard defaults, gamepads, movement, and named actions |
| `using Controllers;` | Named controller buttons, both sticks, connection checks, and controller names |
| `using Save;` | Automatic local save data, settings, progress, and named unlockables |
| `using Physics2D;` | Easy slimes plus advanced rigid bodies, oriented collision, stable stacks, adaptive substeps, sleeping, torque, contacts, and springs |
| `using Physics3D;` | Easy 3D bodies and characters, primitive collision, rotation, friction, sleeping, and adaptive fast-body substeps |
| `using Inputs;` | Unified input: keyboard, mouse, cursor lock, gamepad                                 |
| `using Audio;`  | Sound files, synthesizer, music streaming, 2D/3D positional sound, 8-voice polyphony |
| `using Net;`    | HTTP requests (GET/POST/PUT/DELETE) + JSON encode/decode                            |
| `using System;` | Directories, running other processes, environment variables — for scripting and small build/tooling tasks |
| `using MakoUI;` | Desktop UIs via Dear ImGui: windows, menus, tables, modals, tabs, themes — standalone window or embedded live in a Mako3D/Mako2D scene |

Game-dev builtins are available without an import:

```mako
clamp(v, 0, 10)
lerp(a, b, t)
dist(x1, y1, x2, y2)
sin(x)
cos(x)
atan2(y, x)
pi()

rects_overlap(...)
circles_overlap(...)
point_in_rect(...)

find_path(grid, sx, sy, ex, ey)
line_of_sight(grid, x1, y1, x2, y2)
```

---

## MakoUI

MakoUI is MAKO’s GUI layer, powered by Dear ImGui.

It is built for:

* tools
* editors
* debug panels
* tables
* menus
* modals
* quick app interfaces

Example:

```mako
using MakoUI;

main() {
    MakoUI.init("MakoUI Demo", 900, 600);

    count = 0;

    while MakoUI.running() {
        MakoUI.begin();

        MakoUI.begin_window("Counter");

        MakoUI.text("Count: {count}");

        if MakoUI.button("Increment") {
            count = count + 1;
        }

        if MakoUI.button("Reset") {
            count = 0;
        }

        MakoUI.end_window();

        MakoUI.end();
    }
}
```

---

## Mako2D

Mako2D is MAKO’s 2D rendering package.

It is built for:

* 2D games
* sprites
* shapes
* tilemaps
* camera movement
* simple visual tools
* game prototypes

Example:

```mako
using Mako2D;
using Inputs;

main() {
    Mako2D.init(800, 600, "Mako2D Demo");
    Mako2D.fps(60);

    x = 400;
    y = 300;
    speed = 250;

    while Mako2D.running() {
        dt = Mako2D.delta();

        if Inputs.key_down("RIGHT") { x = x + speed * dt; }
        if Inputs.key_down("LEFT")  { x = x - speed * dt; }
        if Inputs.key_down("DOWN")  { y = y + speed * dt; }
        if Inputs.key_down("UP")    { y = y - speed * dt; }

        Mako2D.begin();
        Mako2D.clear(Mako2D.BLACK);

        Mako2D.circle(x, y, 24, Mako2D.SKYBLUE);
        Mako2D.text("Arrow keys to move", 10, 10, 20, Mako2D.WHITE);

        Mako2D.end();
    }

    Mako2D.close();
}
```

---

## Mako3D

Mako3D is MAKO’s 3D rendering package.

It is built for:

* 3D scenes
* cameras
* cubes
* spheres
* models
* lighting
* raycasts
* simple game AI
* 3D experiments

Example:

```mako
using Mako3D;
using Inputs;

main() {
    Mako3D.init(800, 600, "Mako3D Demo");
    Mako3D.fps(60);

    cam = Mako3D.camera(10, 8, 10, 0, 0, 0);

    while Mako3D.running() {
        Mako3D.update_camera(cam, 8);

        if Inputs.key_pressed("ESCAPE") {
            Mako3D.close();
        }

        Mako3D.begin();
        Mako3D.clear(Mako3D.color(18, 18, 28));

        Mako3D.begin_3d(cam);

        Mako3D.grid(20, 1);
        Mako3D.cube(0, 1, 0, 2, 2, 2, Mako3D.RED);
        Mako3D.sphere(4, 1.5, 0, 1.5, Mako3D.SKYBLUE);

        Mako3D.end_3d();

        Mako3D.text("WASD/arrows + middle mouse camera", 10, 10, 16, Mako3D.WHITE);

        Mako3D.end();
    }

    Mako3D.close();
}
```

---

## Audio

MAKO includes an early audio package for sound effects, synthesis, music, and positional audio.

Example:

```mako
using Audio;

main() {
    Audio.init();

    beep = Audio.tone("square", 440, 0.1);
    Audio.play(beep);

    Audio.close();
}
```

Audio supports early ideas like:

* sound playback
* synthesized tones
* waveforms
* melody baking
* music streaming
* 2D/3D positional sound
* basic polyphony

---

## Project goals

MAKO is designed around a few simple rules:

* Easy to learn like Python
* Easy to remember like Lua/Luau
* Structured like C-style languages
* Easy to type on a normal keyboard
* Useful for real scripts and tools
* Powerful enough to grow into rendering, audio, UI, networking, and engine-style workflows
* Small core, expandable packages

MAKO is not trying to replace large engines like Modularity. The goal is to make MAKO a lightweight language and toolkit for experiments, small games, tools, and fast ideas.

---

## Documentation

* [Language reference](docs/language.md) — syntax, types, control flow, functions, modules
* [Standard library](docs/stdlib.md) — every builtin function
* [CLI](docs/cli.md) — every `mko` command
* [Mako2D](docs/mako2d.md)
* [Mako3D](docs/mako3d.md)
* [Physics3D](docs/physics3d.md)
* [Inputs](docs/inputs.md)
* [Audio](docs/audio.md)
* [Net](docs/net.md) — HTTP requests and JSON
* [MakoUI](docs/makoui.md)
* [Game development guide](docs/games.md) — delta time, collision, AI patterns, positional audio

---

## Building from source

```bash
./build.sh dev
./build.sh release
./build.sh install
./build.sh clean
```

Implementation:

* Tree-walk interpreter
* Written in C# / .NET 8
* Graphics via Raylib-cs
* UI via Dear ImGui / ImGui.NET / Silk.NET
* CLI command: `mko`
* File extension: `.mko`

---

## License

MIT

```
```

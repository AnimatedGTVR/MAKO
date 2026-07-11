# Foundry — build and export MAKO games

Foundry is MAKO's game builder. Its MakoUI frontend and command-line interface
share one backend, so a GUI build and a CI build produce the same artifact.

```bash
mko foundry path/to/game.mko
```

The first ready target is a self-contained Linux x64 folder. It contains the
MAKO runtime, native libraries, game scripts/assets, build metadata, and one
executable launcher. The generated game does not need the MAKO SDK installed.

```bash
mko build path/to/game.mko --target linux-x64
```

## Targets

| Target | Artifact | Status |
|---|---|---|
| Linux x64 | Portable executable folder | Ready |
| Windows x64 | `.exe` game distribution | Planned |
| AppImage | `.AppImage` | Planned |
| Android | `.apk` | Planned |
| macOS | `.app` bundle | Planned |
| Web | WebAssembly/browser bundle | Later |
| VR | OpenXR platform package | Later |
| Consoles | Licensed SDK package | Later |

Unavailable targets remain visible in Foundry with a reason, but cannot be
built accidentally.

## Projects

A single script requires no project file:

```bash
mko foundry slime_platformer.mko
```

For a larger game, add `foundry.json` next to the entry script:

```json
{
  "name": "Googly Slime Run",
  "version": "0.1.0",
  "entry": "main.mko",
  "output": "dist",
  "target": "linux-x64",
  "icon": "assets/icon.png",
  "include": ["assets", "levels"]
}
```

Directory projects include top-level sibling `.mko` modules, the conventional
`assets/` directory, and every declared include. Building one explicit `.mko`
file includes only that game plus declared content, not unrelated neighboring
scripts.

## Commands

```bash
mko foundry [project]             # graphical MakoUI builder
mko foundry [project] --term      # project and target status
mko build [project]               # build the saved/default target
mko build game.mko --target linux-x64
mko build game.mko --output ./release
```

## Linux artifact

```text
dist/my-game-linux-x64/
├── my-game                 executable launcher
├── foundry-build.json      reproducible build metadata
├── game/
│   ├── main.mko
│   └── assets/
└── runtime/
    ├── mko                 self-contained MAKO runtime
    └── native libraries
```

Foundry builds into a staging directory and only replaces the final target
folder after a successful export.

## Physics backends

MAKO Physics is always built in. Optional Jolt, Bullet, and PhysX adapters will
be installed separately and compiled only when a project selects them. Foundry
will validate the selected backend before building and bundle only the runtime
needed by that game.

Select one in `foundry.json` with `"physics3D": "jolt"` (or `mako`, `physx`,
or `bullet`). The default is `mako`. Optional adapters live under
`~/.local/share/mko/physics/<name>/` and contain a `backend.json` manifest plus
their native bridge library. A build stops with a useful error if the adapter
is missing, has the wrong ABI, or was not compiled into that MAKO runtime.
Box2D belongs to Physics2D and cannot be selected as a Physics3D backend.
Scripts can select the same setting with `using JoltPhysics;`, `using PhysX;`,
or `using BulletPhysics;`. Box2D remains a Physics2D backend.

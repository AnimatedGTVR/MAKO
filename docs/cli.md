# The `mko` CLI

## Running scripts

```bash
mko script.mko            # shorthand
mko run script.mko        # explicit form
./script.mko              # with a #!/usr/bin/env mko shebang + chmod +x
```

Script paths resolve in order: as given → `~/.local/share/mko/<path>` →
`~/.local/share/mko/examples/<path>` → `~/.local/share/mko/scripts/<path>`.
The `.mko` extension is optional for the global locations, so `mko pong`
works from any directory.

## REPL

```bash
mko repl
```

Interactive prompt. Expressions print their value automatically; functions
and constants persist across lines. `exit` or Ctrl+C to quit.

## Formatter

```bash
mko fmt script.mko            # format in place
mko fmt script.mko --check    # exit 1 if the file needs formatting
```

AST-based: normalizes indentation (4 spaces), spacing, and brace style.
Preserves standalone comments and template strings exactly.

## Tests

```bash
mko test              # run every *.mko file under ./tests/
mko test some/dir      # or a custom directory
```

Each test file is a normal MAKO script that uses `assert(cond, msg)`. A file
**passes** if it runs to completion; it **fails** if any assertion throws (or
any other error occurs), printing the message and line number. Exit code is
`0` only if every file passed — wire it into CI as-is.

```
PASS  tests/lists.mko
FAIL  tests/dicts.mko: Assertion failed: merge — second wins (line 27)

4 passed, 1 failed — see FAIL lines above
```

## Packages

```bash
mko get somelib github:User/Repo    # install from GitHub
mko get somelib                     # install from the registry
mko list                            # show installed packages
mko cache clear                     # remove all cached packages
mko cache clear somelib             # remove one
```

Packages live in `~/.local/share/mko/packages/`. A package repo needs an
`index.mko` that declares a `namespace`.

In a script, `using name from "github:User/Repo";` fetches automatically on
first run.

## Foundry builds

```bash
mko foundry game.mko                  # open the MakoUI builder
mko foundry game.mko --term           # list target readiness in the terminal
mko build game.mko --target linux-x64 # direct/CI build
```

Foundry's first ready target produces a self-contained Linux x64 executable
folder. See [foundry.md](foundry.md) for project manifests, artifact layout,
and the Windows/AppImage/Android/macOS/Web/VR/console roadmap.

## Misc

```bash
mko version
mko help
```

## Install layout

`./build.sh install` puts things here:

| Path | What |
|---|---|
| `~/.local/bin/mko` | Launcher (sets LD_LIBRARY_PATH, runs the real binary) |
| `~/.local/share/mko/bin/` | `mko.bin` + native libraries (raylib, cimgui, glfw) |
| `~/.local/share/mko/examples/` | All example scripts + assets |
| `~/.local/share/mko/packages/` | Cached GitHub packages |

# System — directories, processes, and environment

Single-file operations (`read`, `write`, `append`, `exists`, `delete`,
`lines`) and `env(name)` / `args()` are already global builtins — see
[stdlib.md](stdlib.md#files) — no import needed. `System` adds what those
don't cover: directory manipulation, running other processes, and setting
environment variables.

```mako
using System;

main() {
    if not System.dir_exists("build") {
        System.make_dir("build");
    }

    res = System.exec("gcc", ["-o", "build/app", "main.c"]);
    if System.ok(res) {
        print "build succeeded";
    } else {
        print System.stderr(res);
        exit(1);
    }
}
```

## Files

| Function | Description |
|---|---|
| `copy_file(src, dst)` | Copy a file, overwriting `dst` if it exists |

## Directories

| Function | Description |
|---|---|
| `list_dir(path=".")` | List of entry names (files and subdirectories) directly inside `path` |
| `make_dir(path)` | Create a directory, including any missing parent directories |
| `remove_dir(path, recursive=false)` | Delete a directory; pass `true` to delete non-empty ones |
| `dir_exists(path)` | `true` if `path` exists and is a directory (the global `exists()` matches files *or* directories; use this when you need to know which) |
| `cwd()` | The current working directory |

## Processes

```mako
res = System.exec("git", ["status", "--short"]);
if System.ok(res) {
    print System.stdout(res);
} else {
    print "failed ({System.code(res)}): {System.stderr(res)}";
}
```

`exec(command, args=[])` runs `command` with the given argument list, waits
for it to finish, and captures its output. It never throws for a failed or
missing command — a result is always a plain dict — `{"code": 0, "stdout":
"...", "stderr": "...", "ok": true}` — so check it with `System.ok(res)`.
Arguments are passed directly to the process (no shell involved), so no
quoting or escaping is needed for spaces or special characters in `args`.

| Function | Description |
|---|---|
| `exec(command, args=[])` | Run a process and wait for it to exit |
| `ok(res)` | `true` if the process exited with code `0` |
| `code(res)` | The process's exit code (`-1` if it never started) |
| `stdout(res)` | Captured standard output |
| `stderr(res)` | Captured standard error |

## Environment

| Function | Description |
|---|---|
| `set_env(name, value)` | Set an environment variable for this process (and anything it `exec`s afterward) — pairs with the global `env(name)` for reading |
| `platform()` | `"linux"`, `"macos"`, `"windows"`, or `"unknown"` |

## Example — a tiny build script

```mako
using System;

main() {
    if not System.dir_exists("build") {
        System.make_dir("build");
    }

    res = System.exec("gcc", ["-o", "build/app", "main.c"]);
    if System.ok(res) {
        print "build succeeded";
    } else {
        print "build failed:";
        print System.stderr(res);
        exit(1);
    }
}
```

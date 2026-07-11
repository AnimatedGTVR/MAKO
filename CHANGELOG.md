# Changelog

All notable changes to MAKO are recorded here.

---

## [0.1.0] — 2026-07-10 — first official release

### Added

**Foundry — MAKO's MakoUI game builder.** `mko foundry [project]` opens a target
browser and build screen backed by the same exporter used by the new
`mko build` CLI. The first ready target creates a self-contained Linux x64 game
folder containing a published MAKO runtime, native libraries, selected scripts,
assets/includes, executable launcher, and build metadata. Builds stage output
before replacing the final artifact. Windows `.exe`, AppImage, Android APK,
macOS app, Web, VR, and licensed console adapters are represented explicitly as
planned/later targets instead of pretending to work. Single-script builds do
not sweep unrelated neighboring `.mko` files into the artifact.

**`Physics2D` native package — the first rigid-body engine milestone.**
MAKO now has a rendering-independent, fixed-step 2D simulation with dynamic,
static, and kinematic bodies; circle and axis-aligned box colliders; gravity,
forces, impulses, restitution, friction, positional correction, and contact
queries. Numeric world/body handles keep the script API simple and leave the
simulation reusable by Mako2D or a future editor engine. Includes a headless
regression suite, full API documentation, and an interactive Mako2D sandbox at
`examples/physics_2d.mko`.

The first angular-physics pass adds rotation/angular velocity, torque, angular
impulses, circle/box moments of inertia, rotation locks, impulses at arbitrary
world points, oriented-box SAT collision, and contact-point response that makes
off-center impacts spin bodies. Mako2D gained `rect_rot`/`rect_rot_lines` so the
upgraded crooked-tower sandbox renders the simulated orientation directly.

The stabilization pass replaces the single approximate box contact with a
two-point face manifold, runs configurable internal substeps, and automatically
adds more substeps for fast bodies (up to 64) to prevent thin-wall tunnelling.
Linear/angular damping, low-speed restitution suppression, and wake/sleep state
stop settled cubes from jittering or drifting forever. `set_damping`, `wake`,
and `is_sleeping` expose the controls to scripts.

**Damped spring joints and the first slime rig.** `Physics2D.spring()` connects
two bodies at center or local-space anchors with configurable rest length,
Hooke stiffness, and velocity damping; off-center anchors generate torque.
Springs can be inspected, retuned, and removed at runtime, and are cleaned up
when either body is removed. The sandbox now builds a soft slime prototype from
a ring of circle bodies with edge springs, braces, and cross-springs.

**Easy slime API.** The manual particle/spring recipe is now hidden behind
`Physics2D.slime(world, x, y, radius, options?)`. `slime_move` distributes
movement forces, `slime_jump` aggregates ground contacts and prevents accidental
double jumps, and `slime_info` returns the center, velocity, state, and ordered
outline points needed for rendering. Advanced body/spring handles remain
available, but ordinary game code no longer needs to understand their topology.

**Game-feel slime controller.** `slime_move` now accelerates toward a real speed
cap with strong ground traction and configurable reduced air control. Jumping
adds automatic coyote time and input buffering; `slime_hold_jump` supports
variable height and cuts upward velocity when released early. `slime_info`
reports width/height deformation, squash/stretch ratios, coyote state, and
whether a jump is buffered. The sandbox is now a small multi-level obstacle
course driven with A/D and Space.

**Slime topology stabilization and embedded command log.** Particles belonging
to the same slime no longer collide with and kick one another. Hidden springs
now cap the velocity they can inject per substep and enforce a configurable
`stretch_limit`, preventing isolated nodes from tunnelling through platforms or
tearing the rendered outline into long spikes. The Physics2D sandbox now embeds
a compact MakoUI runtime command/event log with `jump`, `left`, `right`, `stop`,
`status`, `clear`, and `help` commands. `MakoUI.wants_keyboard()` was added so
typing in an embedded panel does not also drive game controls.

**Collision-continuous slime perimeter and first platformer.** Slime creation
now derives hidden particle size from point count, requested outer radius, and
the stretch limit, with 14 points by default. Adjacent colliders overlap even at
maximum stretch, closing the holes that let a thin platform enter and become
trapped inside the ring. `slime_set_position` / `slime_reset` safely move the
whole hidden rig for checkpoints and falls. `examples/slime_platformer.mko`
starts the actual game: a one-screen route with gaps, raised platforms,
collectibles, a goal, reset/death tracking, an embedded MakoUI HUD, and the most
important rendering feature—velocity-reactive googly eyes.

**Anti-pancake area constraint.** Springs preserve distances but not polygon
volume, so a legal spring configuration could leave the slime flattened across
a platform. High-level slimes now retain their initial area with a bounded
per-substep positional constraint. The default permits visible squash on impact
and then restores the blob automatically; `shape_recovery` tunes the effect.
`slime_info` exposes `area` and `area_ratio` for animation and debugging.

**REPL: `fn`/`struct` declarations now persist across lines.**
Previously every REPL line was unconditionally wrapped in `main() { ... }`
before parsing, which made top-level-only declarations (`fn name(...)`,
`struct Name {...}`) impossible to type at the prompt at all — a pre-existing
`fn` bug, not something the struct work introduced, but both shared the same
root cause. The REPL now tokenizes each line first and detects real
declarations (via actual token types, not string prefix matching — a
variable named `fnord` is not mistaken for an `fn` declaration) to parse them
unwrapped at top level, registering into the same persistent interpreter
state every other REPL line already shares:
```
> fn square(x) { return x * x; }
> struct Point { x, y }
> print square(5);
25
> p = Point { x: 1, y: 2 };
> print p.x;
1
```
`fn(x) => ...` lambda expressions are unaffected — still routed through the
normal wrap-in-`main()` path since they're expressions, not declarations.

**`throw expr;` — raise a custom catchable error.**
Previously the only way to raise a custom error from MAKO code was abusing
`assert(false, "message")`. `throw` is a proper, dedicated statement: `expr`
is stringified as the error message (so `throw "bad: {n}";` works via
normal string interpolation), and it's caught by `try`/`catch` exactly like
any built-in error — `err` in `catch err { }` is still a plain string, so
this is a purely additive change with no effect on existing catch blocks.
An uncaught `throw` crashes with a clean message pointing at the `throw`
line, same as any other MAKO error. `mko fmt` formats it correctly. See
`docs/language.md#error-handling`.

**Structs and methods — `struct Name { fields }`, `fn Type.method(self, ...)`.**
The biggest language-core gap: dicts were the only way to model structured
data, with no attached behavior. Now:
```mako
struct Point { x, y }
fn Point.dist(self, other) { return dist(self.x, self.y, other.x, other.y); }

p1 = Point { x: 0, y: 0 };
p2 = Point { x: 3, y: 4 };
print p1.dist(p2);   # 5
p1.x = 10;            # fields are read/write
print type(p1);       # "Point"
```
A struct instance is a dict underneath (tagged with an internal `__type`
key, hidden from `keys()`/`len()`/`for...in`) — every existing dict
operation (indexing, `merge()`, `json_encode()`, etc.) keeps working on it
unchanged. Field access (`p.x`) and method calls (`p.dist(...)`) are new
postfix syntax resolved at runtime rather than parse time: `Ident.Ident` was
already used for namespace calls/constants (`Net.get(...)`, `MakoRay.RED`),
so the ambiguity is resolved by checking whether the base identifier is a
variable holding a struct instance before falling back to the existing
namespace-call path — existing scripts using `Ns.func()`/`Ns.CONST` are
unaffected (full regression pass + all 13 existing test scripts still
pass). No inheritance, no private fields, no constructors beyond the
`Name { field: value }` literal. `mko fmt` formats struct/method
declarations and field access correctly. (The REPL couldn't parse `struct`/
`fn` declarations at all when this landed — see the REPL fix above, shipped
in the same release, for why that's no longer true.) See
`docs/language.md#structs`.

**`System` native package — directories, processes, environment.**
`using System;` unlocks `copy_file`, `list_dir`/`make_dir`/`remove_dir`/
`dir_exists`/`cwd`, `exec` (run a process, capture stdout/stderr/exit code
without throwing on failure — check `System.ok(res)`), and `set_env`/
`platform`. MAKO previously had no way to run another program or manipulate
directories at all — single-file ops and `env`/`args` were already global
builtins (`read`/`write`/`exists`/etc., see `stdlib.md`), so `System` only
adds what those didn't cover rather than shadowing them under different
names. Follows the same native-package pattern as `Net` (flat dispatch
table in `MakoSystem.cs`, gated behind `using System;`). See `docs/system.md`.

### Fixed

**GitHub lookup could silently close the whole browser window.**
`GithubPackageLookup.Fetch()` had one un-wrapped call (reading the HTTP
response body) that could throw something other than `MakoError`; its only
caller in the package browser only caught `MakoError`, so anything else
unwound straight out of the ImGui frame loop — the window just closed with
no visible error. `--term` never showed this (its own catch is a plain
`Exception`), which is why the CLI path looked fine while the GUI path
looked like "nothing happens." Fetch() now wraps its entire body in one
outer catch so nothing but `MakoError` can ever leave it, and the browser's
call site now also catches `Exception` as a second layer, so a regression
here shows an error message instead of a silently vanishing window.

### Added

**Live GitHub package lookup.** `mko search github:User/Repo` / `mko info
github:User/Repo` fetch that repo's `mako.json` manifest straight from
GitHub's API (no cloning) and preview it — name, description, version,
usage line — the same way as a registry entry, before you decide whether to
`mko get` it. A repo needs a `mako.json` at its root (`name`, `description`
required; `version`/`usage` optional) to be discoverable this way; repos
without one still work fine with `using X from "github:...";`, they just
won't show up in search/info. **MakoVR** moved from a standalone registry
entry to a version of **Mako3D** (VR is scoped as a 3D-rendering extension,
not its own package) — same "planned, not implemented" status as before.

The GitHub lookup also works from inside an already-open `mko search`
window now, not just as a CLI argument (`mko search github:...`) before it
opens — typing `github:User/Repo` into the live search box surfaces a "Look
up ... (GitHub)" entry; selecting it fetches and shows the result inline,
with fetch errors (no manifest, repo not found) shown in the detail panel
instead of silently doing nothing. Typing `github:` followed by something
that isn't `User/Repo` (a colon instead of a slash, for example) now shows
a "Format: github:User/Repo" hint instead of just falling through to a
silent "No matches."

**Package detail panel now has tabs.** The registry gained a `versions` field —
other identities of the same package (MakoGUI is now listed as a version of
MakoUI rather than its own top-level registry entry, so it no longer clutters
search results as a separate match). The detail panel splits into "Usage &
Docs" and "Versions" tabs; searching or looking up a variant's name (e.g.
`mko info MakoGUI`) resolves to its parent entry. Also fixed: the package
browser's window filled a fixed 760x460 regardless of what the real OS
window came out as (compositor/DPI scaling), leaving dead space below the
panels — now sizes off the actual display size every frame. Em-dashes in
registry text rendered as tofu in ImGui's default font; swapped for plain
hyphens.

**Package discovery: `mko search` / `mko info`.** MAKO could already install
and use packages (`mko get`, `using X from "github:...";`) but had no way to
ask "what's out there?" first — you needed the exact name already. Both new
commands read a small embedded registry (`registry.json`) and, by default,
open a real MakoUI-based graphical browser (search box, results list,
description panel with the exact `using` line to copy); pass `--term` for
plain text instead, and the GUI falls back to text automatically if a window
can't be opened. `using UnknownPackage;` errors now suggest `mko search`/
`mko info` when the name doesn't match anything installed or registered.

**MakoGUI**, discoverable via the new registry, is the standalone-desktop-app
identity of `MakoUI.init(...)` — same package as MakoUI (which also covers the
in-game-overlay `MakoUI.attach()` case), just named for people who want to
build a plain GUI app with no game loop. **MakoVR** is also listed, explicitly
marked `planned` — there's no VR implementation, just an honest "this is
coming" entry to show the registry format supports that.

### Fixed

**`MakoUI.DragRange` was dead code.** Implemented in C# but never wired into
the interpreter's dispatch table, so it was uncallable from any `.mko` script
despite being a real, working widget. Wired up as `MakoUI.drag_range(label,
lo, hi, speed=1.0)`, returning `[lo, hi]` like `color_picker` does, so a
script does `[lo, hi] = MakoUI.drag_range(...)`.

**Documentation audit.** Cross-checked every native package's implementation
against its docs file and found real gaps:

- `Mako3D.sky(color)` — a `clear()`-equivalent alias for a 3D scene's
  backdrop — existed in the interpreter with zero mention in `mako3d.md`.
- `sort_by(xs)`'s one-argument form (sorts by natural order, no key function
  needed) wasn't documented — only the two-argument `sort_by(xs, fn)` form was.
- `docs/language.md`'s native-packages list omitted `Net`.
- `docs/language-reference.md` was badly stale (predated dicts, lambdas,
  try/catch, native/GitHub packages, and most of the standard library —
  everything it said was still true, it just described maybe half the
  language). Rewritten to cover all of the above, plus string indexing,
  `slice`, and the full built-in function surface (higher-order, dict,
  geometry/collision, pathfinding, file I/O, system, JSON).

**Segfault on exit after closing a raylib window.** raylib's `CloseWindow`
tears down GL/GLFW unconditionally, so closing twice crashed the process
(`GLFW: Error: 65537` + SIGSEGV after "Window closed successfully"). This hit
any `MakoRay` script that called `close()` itself — the interpreter's cleanup
then closed again. Now `close()` in `MakoRay`/`Mako2D`/`Mako3D` is idempotent,
interpreter cleanup only closes a still-open window (and now also closes
windows that `Mako2D`/`Mako3D` scripts forgot to close), and `UnloadAll` skips
GPU unloads once the GL context is gone.

### Error reporting overhaul

**Precise carets.** The lexer now tracks columns, and every token records where
it starts and ends. Errors point at the exact spot — a missing `;` caret sits
right where the semicolon belongs, and name errors underline the whole name:

```
  if value < lo { return loo; }
                         ^^^
mako: error (line 4): type, function, or name 'loo' wasn't found (did you mean 'lo'?)
```

**"Did you mean ...?" suggestions** (edit-distance based) for:

- misspelled variables/functions at runtime (`loo` → `lo`, `gret()` → `greet`)
- misspelled statement keywords (`whle` → `while`, `func`/`def` → `fn`, `elif` → `else if`)
- `=` where `==` was meant (`if x = 5 {`)
- `&`/`|` → `and`/`or`, `'...'` → use double quotes
- `let x = 5;` / `var x = 5;` → "assign directly: `x = 5;`"

**New errors that used to be silent, confusing, or crashes:**

- unterminated string → `missing closing '"'` pointing at the end of the line it
  opened on — and when the stray quote sits right after a word (`script Functions";`),
  it reports `missing opening '"' before 'Functions'` with the caret before the word
- unterminated `/* ... */` comment
- invalid numbers (`3.14.15`) and names starting with a digit (`12abc`)
  (previously an internal crash / silent mis-lex)
- missing `,` between arguments, parameters, or list items
  (previously `f(1 2)` silently parsed as `f(1, 2)`)
- missing `}` / `)` / `]` now name the line the block or paren was opened on
- `else` without `if`, `fn` inside a block, statements at top level,
  duplicate `main()` — each with a specific message
- runtime type errors carry positions and plain-English explanations:
  `to_num("abc")`, `"abc" - 5`, `sqrt("nine")`, `for i in 5` (suggests `range(n)`),
  index out of range (shows the valid range), division by zero, const reassignment

**Module-aware errors.** Errors inside a file imported with `use` now show that
file's source line and are labelled `line N in module.mko` (previously the
snippet came from the wrong file).

**Bug fix:** `{{` / `}}` literal-brace escapes were mangled when the string also
contained a real `{expr}` interpolation; template strings are now parsed from
verbatim source, which also makes carets inside interpolations land exactly.

### Added — embedding, web export, and Windows/macOS builds

**Host embedding API.** MAKO can now run as the scripting layer inside a
host C# application (e.g. a game engine embedding MAKO), not just as the
`mko` CLI. `MakoHostContext.RegisterFunction("Namespace.function", args =>
...)` and `RegisterPackage("Namespace")` let a host expose its own callable
surface to scripts under the exact same `Namespace.function` calling
convention every built-in native package already uses; `new
Interpreter(hostContext).Run(source)` parses and runs a script without the
host ever touching MAKO's internal AST/lexer/parser types, which stay
private to the assembly. There's no NuGet package yet — a host references
`src/Mako/Mako.csproj` (or links individual graphics-free `.cs` files, the
way MAKO's own web build does) directly.

**Web export — `mko build game.mko --target web`.** Foundry can now
compile a game to WebAssembly and bundle it with a browser page, entirely
via a new `Mako.Web` Blazor WebAssembly project that links (not copies)
the graphics-free source files from `src/Mako` — `Interpreter.cs` itself
compiles completely unmodified between the native and web builds.
Language-only scope: the core language and `Physics2D`/`Physics3D` (pure
`System.Numerics` math, zero native dependencies) run for real in the
browser; `Mako2D`/`Mako3D`/`MakoUI`/`Audio`/`Inputs` (native
raylib-cs/Silk.NET/ImGui.NET libraries, no WASM target) and `Net` (blocks
synchronously on `HttpClient`, which deadlocks on browser-wasm's
single-threaded runtime) are stubbed to fail with a clear "not available
in a web build yet" error instead of crashing or silently doing nothing.
Verified end-to-end in a real headless Chromium session: a script typed
into the page's editor runs the actual interpreter and produces correct
physics output, not a mock.

**Windows and macOS Foundry targets.** `mko build game.mko --target
windows-x64` (self-contained portable folder + native DLLs, cross-compiled
from Linux via `dotnet publish -r win-x64`) and `--target macos` (unsigned
`.app` bundle for Apple Silicon, standard `Contents/MacOS`+`Info.plist`
layout) both produce real, structurally verified artifacts — the native
graphics libraries (raylib, GLFW, cimgui) correctly bundle per-target-OS,
which is the part that could otherwise silently break with no error until
someone actually launched the game on that platform. macOS builds aren't
code-signed (no Apple Developer certificate in this pipeline), so first
launch needs "Open Anyway" in System Settings past Gatekeeper. Both
targets, like the web target, fall back to a copy of the source MAKO
project installed under `~/.local/share/mko/src/` when Foundry is run from
the installed `mko` binary rather than a repo checkout.

### Added — type hints, `mko check`, and a versioned package manager

**Optional type hints.** `name: string = "Alice";` — purely syntactic and
never enforced by the interpreter; parsed onto `AssignStmt.TypeHint` for
tooling only, and preserved by `mko fmt`. Only a plain assignment can carry
a hint (`count: number += 1;` is a parse error, by design — a hint belongs
on the variable's first, non-compound assignment).

**`mko check file.mko` — a new lint command.** Three rules chosen
deliberately narrow to keep the false-positive rate low: type-hint
mismatches against literal values, unused local variables (prefix a name
with `_` to opt out), and unreachable code after `return`/`break`/
`continue`. Deliberately does not attempt undefined-variable-use checking
— that needs real scope/closure tracking to avoid false positives in a
dynamic language, and a lint tool that cries wolf gets ignored. Verified
against every script in `examples/` (~40 files): 2 genuine hits, both real
dead variables, zero false positives.

**Package manager: version pinning, a lockfile, and `mko update`.**
Previously `using X from "github:User/Repo";` cloned once from whatever
the default branch's HEAD happened to be at that instant, then froze
forever with no way to intentionally move forward short of deleting the
cache and re-cloning blind. Now:
- `using X from "github:User/Repo@ref";` pins a tag, branch, or commit
  (git doesn't distinguish these at checkout, so neither does MAKO);
  `mko get <pkg> <source@ref>` supports the same syntax. Omitting `@ref`
  keeps the exact previous behavior.
- `mako.lock` records the exact resolved commit for every installed
  package, so a fresh install on a different machine (or after `mko cache
  clear`) reproduces that exact commit rather than re-resolving a ref that
  may have since moved.
- `mko update [pkg]` is the only sanctioned way to intentionally move a
  package forward — re-resolves the pin (or HEAD, if unpinned), re-clones,
  and updates the lockfile.
- `mko list` now shows each installed package's pin and resolved commit.

**Bug fix:** `PackageManager.Clone()` passed `git clone "<url>" "<dir>"` as
a single unsplit `ProcessStartInfo.Arguments` string — a shell-metacharacter
risk in a package name or URL. Switched to `ArgumentList`, matching the
safer pattern `Foundry.cs`'s `RunProcess` already used.

All of the above verified against the real, live `AnimatedGTVR/MAKO`
GitHub repository: pinned install, lockfile-reproduced reinstall, pinned
and unpinned update, and a bad-ref install (clean error, no orphaned
directory or lockfile entry left behind) all behave correctly, with the
full 18-test regression suite passing throughout.

---

## [0.02] — 2026-07-07

### Major update — loops, functions, lists, namespaces, and more

**New language features:**

- `while condition { }` — while loops
- `for item in list { }` — for-each loops over lists
- `break;` / `continue;` — loop control
- `fn name(params) { }` — user-defined functions with proper scoping
- `return expr;` — return values from functions (recursive calls supported)
- `and` / `or` — short-circuit logical operators
- `not expr` — keyword alternative to `!`
- `%` — modulo operator
- `+=` `-=` `*=` `/=` — compound assignment operators
- Unary `-` — negation (`-x`)
- `none` — null literal
- `[1, 2, 3]` — list literals
- `list[i]` — indexing (negative indices supported: `list[-1]`)
- `list[i] = val;` — index assignment
- `[a] + [b]` — list concatenation with `+`
- `namespace Name;` — declare a module namespace
- `use "file.mko";` — import another module's functions
- `Namespace.func(args)` — namespaced function calls
- `const name = expr;` — immutable bindings (enforced at runtime)
- `"Hello, {name}!"` — string interpolation with arbitrary expressions
- `printnl expr;` — print without trailing newline
- `/* block comments */`

**New built-in functions:**

- `range(n)` / `range(start, stop)` / `range(start, stop, step)` — generate number lists
- `assert(cond, msg?)` — assertion with optional message
- `exit(code?)` — exit the program
- String: `upper` `lower` `trim` `contains` `starts_with` `ends_with` `replace` `split` `join`
- List: `push` `pop` `first` `last` `reverse` `has`
- Math: `abs` `floor` `ceil` `sqrt` `round` `pow` `max` `min`
- Util: `type` `to_num` `to_str` `len` (strings and lists)

**Interpreter improvements:**

- Proper lexical scope stack — functions get their own scope
- `const` bindings enforced across all scopes
- Better error messages — shows offending source line with `^^^` pointer
- Relative-path module resolution for `use` imports

**New examples:**

- `loops.mko` — while, for, FizzBuzz
- `functions.mko` — fn, return, recursion, built-ins
- `lists.mko` — list creation, indexing, push/pop, for-each
- `strings.mko` — all string built-ins
- `control.mko` — break, continue, not, printnl
- `mathlib.mko` — namespace module (Math library)
- `namespaces.mko` — use + Namespace.func() demo
- `v02features.mko` — const, range, assert, interpolation showcase

---

## [0.01] — 2026-07-07

### First working release

**Language features:**
- `script "Name";` — optional script declaration
- `main() { }` — program entry point
- `print expr;` — output to stdout
- `name = expr;` — variable assignment (dynamically typed)
- `name = input "prompt";` — read a line from stdin
- `"string" + value` — string joining with automatic coercion
- Arithmetic: `+` `-` `*` `/`
- Comparisons: `==` `!=` `<` `>` `<=` `>=`
- `!expr` — logical NOT
- `true` `false` — boolean literals
- `if condition { } else if condition { } else { }` — conditionals
- `run "command";` — execute a shell command
- `// comments` — line comments

**Interpreter:**
- Tree-walk interpreter written in C# (.NET 8)
- Error messages with line numbers
- `mko run file.mko` CLI
- `mko version` and `mko help`

**Examples:** `hello` `input` `variables` `math` `booleans` `greet` `temperature` `quiz` `shell`

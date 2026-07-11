# MAKO Roadmap

This document tracks what has been built and what is planned for future versions.

---

## v0.01 — First Working Interpreter ✅

- [x] `.mko` file extension
- [x] `script "Name";` declaration
- [x] `main() { }` entry point
- [x] `print expr;`
- [x] Variables — `name = value;`
- [x] String literals and joining with `+`
- [x] Number literals (integers and decimals)
- [x] Boolean literals — `true`, `false`
- [x] `none` keyword
- [x] `input "prompt"` — reads a line from stdin
- [x] Arithmetic — `+` `-` `*` `/`
- [x] Comparisons — `==` `!=` `<` `>` `<=` `>=`
- [x] `!` logical NOT
- [x] `if` / `else if` / `else`
- [x] `run "shell command";`
- [x] `//` line comments
- [x] Automatic type coercion (string + number, truthy checks)
- [x] Clean error messages with line numbers
- [x] `mko run`, `mko version`, `mko help` CLI

---

## v0.02 — Loops, Functions, Lists, Namespaces ✅

- [x] `while condition { }` loop
- [x] `for item in list { }` loop
- [x] `break` and `continue`
- [x] `fn name(params) { }` user-defined functions
- [x] `return expr;` — return values from functions
- [x] Recursive function calls
- [x] Lexical scoping — functions get their own scope
- [x] `and` / `or` — short-circuit logical operators
- [x] `not` keyword (alternative to `!`)
- [x] `-x` unary negation
- [x] `%` modulo operator
- [x] `+=` `-=` `*=` `/=` compound assignment
- [x] `const name = expr;` — immutable bindings
- [x] `[1, 2, 3]` list literals
- [x] `list[i]` indexing (negative indices supported)
- [x] `list[i] = val;` index assignment
- [x] `list + list` concatenation
- [x] `namespace Name;` — declare a module
- [x] `use "file.mko";` — import a module
- [x] `Namespace.func(args)` — namespaced calls
- [x] `"Hello, {name}!"` — string interpolation
- [x] `printnl expr;` — print without newline
- [x] `/* block comments */`
- [x] `#` hash comments
- [x] `range(n)` / `range(start, stop)` / `range(start, stop, step)`
- [x] `assert(cond, msg?)` — assertions
- [x] `exit(code?)` — exit the program
- [x] String built-ins: `len` `upper` `lower` `trim` `contains` `starts_with` `ends_with` `replace` `split` `join`
- [x] List built-ins: `len` `push` `pop` `first` `last` `reverse` `has`
- [x] Math built-ins: `abs` `floor` `ceil` `sqrt` `round` `pow` `max` `min`
- [x] Utility: `type` `to_num` `to_str`
- [x] Better error messages — shows offending source line with `^^^`
- [x] Single-file self-contained binary (`PublishSingleFile=true`)

---

## v0.03 — Data, Errors, Structure, Tooling ✅

Structured data, real error handling, struct-based modeling, and dev tooling.
Broader than originally scoped when this version was first planned — most of
v0.04/v0.05's goals below landed here too.

- [x] Dicts (key-value maps): `{"key": value, ...}`, `d["key"]`, `d["key"] = v`
- [x] Nested dicts and lists
- [x] `for key in dict { }` iteration
- [x] `has(d, key)` / `keys(d)` / `values(d)` / `remove(d, key)` / `merge(a, b)`
- [x] Lambdas: `fn(x) => expr` and `fn(x, y) { ... }`
- [x] Higher-order builtins: `map` `filter` `reduce` `sort_by` `each` `any` `all`
- [x] `try { } catch err { }` — catch runtime errors (`err` is the message string)
- [x] `throw expr;` — raise a custom catchable error
- [x] `struct Name { fields }` — named, shaped structured data
- [x] `fn Type.method(self, ...)` + `instance.method(args)` — methods on structs
- [x] `instance.field` / `instance.field = value` — dot field access on dicts/structs
- [x] `type(instance)` reports the struct name, not just `"dict"`
- [x] `json_encode(v)` / `json_decode(s)` — built into the language, no import needed
- [x] `mko fmt file.mko` / `mko fmt file.mko --check` — auto-formatter
- [x] `mko repl` — interactive REPL, including `fn`/`struct` declarations
      persisting across lines
- [x] Package system: `using Name;`, `using Name from "github:User/Repo";`,
      native packages, `mko search` / `mko info` / package browser GUI

Not done from the original plan for this version: multi-line (triple-quote)
strings, `%=` modulo-assign, `floor_div()`. Small, low-risk — candidates for
whenever they come up, not blocking anything.

---

## v0.04 — Native Packages ✅ (in progress — packages ship incrementally)

Built-in packages accessible via `using Name;` (not `use "std:x";` as
originally sketched — MAKO settled on native packages instead of a
`std:`-prefixed module scheme):

- [x] `using System;` — directories (`list_dir`/`make_dir`/`remove_dir`/
      `dir_exists`/`cwd`), running other processes (`exec`, capturing
      stdout/stderr/exit code), `set_env`/`platform`. Single-file I/O
      (`read`/`write`/`append`/`exists`/`delete`/`lines`) and `env`/`args`
      are global builtins already, not part of this package — see stdlib.md.
- [x] `time()` / `sleep(seconds)` — global builtins, no import needed
- [x] `json_encode` / `json_decode` — global builtins, no import needed
- [x] `using Net;` — HTTP GET/POST/PUT/DELETE, JSON-friendly responses
- [x] `using MakoUI;` / `Mako2D;` / `Mako3D;` / `Inputs;` / `Audio;` — game/UI packages
- [ ] Extended math module (log, exponents beyond `pow`, etc.) — `sin`/`cos`/
      `tan`/`atan2`/`pi` are already global builtins; nothing beyond that yet

---

## v1.0 — First Stable Release — functionally complete

Every item below is done except the bytecode VM, which was evaluated
with real measurements and deliberately deferred (see its entry) rather
than left undone by default. Current version string is still `0.03` in
`Program.cs` — bumping that to `1.0.0` is a release-process decision, not
a remaining engineering task, and is left for whoever cuts the release.

- [~] Bytecode compiler + VM for better performance — **evaluated and
      deliberately deferred, not abandoned.** Before scoping this (still
      "the biggest remaining structural change": rewriting statement/
      expression execution, redesigning variable storage from a
      scope-chain of dictionaries to array-indexed locals, and touching
      the calling convention all 9 native packages — MakoUI, Mako2D,
      Mako3D, Physics2D, Physics3D, Inputs, Audio, Net, System — rely
      on), actually measured whether real MAKO usage is bottlenecked by
      the tree-walker at all. It isn't, by a wide margin:
      - `fib(28)` (832K recursive calls, a genuinely worst-case
        workload for a tree-walker) — 0.57s.
      - 3600 `Physics3D.step()` calls on a 50-body scene (equivalent to
        60 real seconds of gameplay at 60fps) — 1.3s total, **0.36ms
        per step**, against a 16.7ms/frame budget. Interpreter overhead
        is not what would limit a real MAKO game's frame rate.
      - No prior performance complaint exists anywhere in this repo —
        CHANGELOG.md, code comments, this file — before this note. The
        VM item was aspirational, not a response to an observed
        problem.
      Root cause of what real overhead exists: `GetVar`/`SetVar` do a
      linear scan up `_scopes` (a `List<Scope>` of
      `Dictionary<string,object?>`) on every single variable read/write
      — the classic tree-walker cost a bytecode VM's array-indexed
      locals would eliminate. If a real bottleneck ever does show up in
      practice, that's the first, cheapest thing worth fixing — likely
      sufficient on its own, well short of a full VM rewrite. Revisit
      this item if that ever changes; until then, v1.0 ships without it.
- [x] Optional type hints (not enforced, just for tooling):
  ```mako
  name: string = "Alice";
  age:  number = 30;
  ```
  Purely syntactic — `AssignStmt.TypeHint`, parsed and preserved by
  `mko fmt`, never read by the interpreter. Only plain assignment can
  carry a hint (`count: number += 1;` is a parse error, by design).
- [x] `mko check file.mko` — lint and hint validation. Three rules
      chosen deliberately narrow to keep the false-positive rate low:
      type-hint mismatches against literal values, unused local
      variables (prefix a name with `_` to opt out), and unreachable
      code after return/break/continue. Deliberately does NOT check
      undefined-variable-use — that needs real scope/closure tracking to
      avoid false positives, left for a future pass. Verified against
      all ~40 scripts in `examples/` — only 2 genuine hits (both real
      dead variables), no false positives.
- [x] `mko fmt file.mko` — auto-formatter (done in v0.03, ahead of schedule)
- [x] Official documentation site — mako-landing-page's `/docs` section
      covers every real package (System, Net, MakoUI, Mako2D, Mako3D,
      Inputs, Audio, Physics2D, Physics3D), the language core including
      `struct` (was undocumented, now has its own entry with a real
      working example), and the host-embedding API
      (`MakoHostContext`/`Interpreter.Run`, new "Embedding" doc group).
      Has search and sidebar navigation. Not a separate deployed site
      from the landing page — folded into the same Next.js app rather
      than standing alone; revisit if that split ever matters.
- [x] Windows and macOS support in official releases — `mko build
      game.mko --target windows-x64` (portable folder + native DLLs,
      cross-compiled from Linux) and `--target macos` (unsigned .app
      bundle, Apple Silicon) both work via Foundry now, from either a
      repo checkout or the installed `mko` binary (`FindMakoProject` has
      the same `~/.local/share/mko/src/Mako` fallback the web target's
      `FindMakoWebProject` already had). macOS builds aren't code-signed
      (no Apple Developer cert in this pipeline) — Gatekeeper requires
      "Open Anyway" on first launch.
- [x] Package manager beyond `using X from "github:...";` — version
      pinning + a real update mechanism, scoped deliberately narrower
      than a hosted registry (npm/crates.io-style central index was
      considered and explicitly deferred — much bigger scope, needs a
      server and a publish/ownership story of its own):
  - `using X from "github:User/Repo@ref";` — `@ref` is optional and can
    be a tag, branch, or commit SHA (git doesn't distinguish these at
    checkout, so neither does MAKO). Omitting it keeps today's exact
    behavior. `mko get <pkg> <source@ref>` supports the same syntax.
  - `mako.lock` (`~/.local/share/mko/packages/mako.lock`) records the
    exact resolved commit for every installed package. A fresh install
    (new machine, or after `mko cache clear`) checks out that recorded
    commit rather than re-resolving the ref — reproducible even if a
    branch has since moved.
  - `mko update [pkg]` — the only sanctioned way to intentionally move
    a package forward: re-resolves the pin (or HEAD if unpinned),
    re-clones, updates the lockfile. No package ever silently drifts.
  - `mko list` now shows each package's pin and resolved commit.
  - Fixed a real bug found while building this: `Clone()` passed
    `git clone "<url>" "<dir>"` as a single unsplit `Arguments` string
    (shell-metacharacter risk) — switched to `ArgumentList`, matching
    the safer pattern `Foundry.cs`'s `RunProcess` already used.
  - Verified against the real, live `AnimatedGTVR/MAKO` GitHub repo:
    pinned install, lockfile-reproduced reinstall, unpinned update,
    pinned update, and a bad-ref install (clean error, no orphaned
    directory or lockfile entry) all behave correctly.
- [x] REPL: `fn`/`struct` declarations persisting across lines (done in v0.03)
- [x] Web export — `mko build game.mko --target web` compiles to
      WebAssembly via Foundry (moved up from "Long term", done ahead of
      schedule). Language-only scope: the core language and
      Physics2D/Physics3D run for real in the browser; Mako2D/3D, MakoUI,
      Audio, Inputs, and Net aren't ported yet (native/graphics deps with
      no WASM path, or — for Net — a blocking-sync-over-async pattern
      that deadlocks on the single-threaded browser runtime). Full
      graphics support stays a v1.0+ item.

---

## Long term

- Compile to native binary via LLVM or QBE
- Embeddable as a scripting engine in C# / Rust applications ✅ — done: see
  `MakoHostContext`/`Interpreter.Run(source, baseDir)` in `Interpreter.cs`,
  a minimal generic host-embedding API (deliberately host-agnostic — no
  specific engine integration was built alongside it)
- MAKO for game scripting
- ~~MAKO on the web via WASM~~ ✅ — done, moved up: see "Web export" under
  v1.0 below (language-only scope: core language + Physics2D/Physics3D
  work; Mako2D/3D, MakoUI, Audio, Inputs, Net aren't ported yet)
- Multi-line (triple-quote) strings
- Struct inheritance / composition (deliberately out of scope for v0.03's
  struct system — evaluate demand before adding)

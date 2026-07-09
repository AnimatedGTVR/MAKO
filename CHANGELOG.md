# Changelog

All notable changes to MAKO are recorded here.

---

## [Unreleased]

### Added

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

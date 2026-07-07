# Changelog

All notable changes to MAKO are recorded here.

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
- `mako run file.mko` CLI
- `mako version` and `mako help`

**Examples:** `hello` `input` `variables` `math` `booleans` `greet` `temperature` `quiz` `shell`

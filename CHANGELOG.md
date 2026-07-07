# Changelog

All notable changes to MAKO are recorded here.

---

## [0.1.0] — 2026-07-07

### First working release

**Language features:**
- `script "Name";` — optional script declaration
- `main() { }` — program entry point
- `print expr;` — output to stdout
- `name = expr;` — variable assignment (dynamically typed)
- `name = input "prompt";` — read a line from stdin
- `"string" + value` — string joining with automatic coercion
- Arithmetic: `+`, `-`, `*`, `/`
- Comparisons: `==`, `!=`, `<`, `>`, `<=`, `>=`
- `!expr` — logical NOT
- `true`, `false` — boolean literals
- `if condition { } else if condition { } else { }` — conditionals
- `run "command";` — execute a shell command
- `// comments` — line comments

**Interpreter:**
- Tree-walk interpreter written in C#
- Clean error messages with line numbers
- `mako run file.mko` CLI
- `mako version` and `mako help`

**Tooling:**
- `build.sh` — build and install script
- `.gitignore` for .NET projects

**Examples:**
- `hello.mko` — minimal hello world
- `input.mko` — reading user input
- `variables.mko` — variable types and assignment
- `math.mko` — arithmetic and comparisons
- `booleans.mko` — boolean values and `!`
- `greet.mko` — input + if/else if/else
- `temperature.mko` — temperature converter
- `quiz.mko` — simple quiz
- `shell.mko` — running shell commands

**Documentation:**
- `docs/getting-started.md`
- `docs/language-reference.md`
- `docs/roadmap.md`

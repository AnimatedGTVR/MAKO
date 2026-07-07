# MAKO

**A simple, sharp programming language — easy to learn, easy to build with.**

MAKO blends the readability of Python, the simplicity of Lua/Luau, and the structure of C-style languages. No heavy boilerplate, no confusing syntax. Just write code and run it.

> **Status:** v0.02 — loops, functions, lists, namespaces, string interpolation, and more.

---

## Quick look

```mako
script "Hello";

main() {
    name = input "What's your name? ";
    print "Hello, {name}! Welcome to MAKO.";
}
```

```mako
script "FizzBuzz";

main() {
    for i in range(1, 101) {
        if i % 15 == 0      { print "FizzBuzz"; }
        else if i % 3 == 0  { print "Fizz"; }
        else if i % 5 == 0  { print "Buzz"; }
        else                 { print i; }
    }
}
```

```mako
script "Functions";

fn greet(name) {
    return "Hello, {name}!";
}

fn factorial(n) {
    if n <= 1 { return 1; }
    return n * factorial(n - 1);
}

main() {
    print greet("World");
    print "10! = {factorial(10)}";
}
```

---

## Design goals

- **Easy to learn** — beginner-friendly, minimal concepts to remember
- **Easy to type** — no symbols that are hard to reach on a normal keyboard
- **Structured** — C-style braces `{}`, not indentation-sensitive
- **Practical** — useful for scripts, tools, and small programs
- **Modular** — namespaces and `use` for splitting code across files
- **Expandable** — built to grow from scripts into larger programs

---

## Build and install

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download).

```bash
# Arch / Abora
sudo pacman -S dotnet-sdk

# Debian / Ubuntu
sudo apt install dotnet-sdk-8.0
```

```bash
git clone https://github.com/AnimatedGTVR/MAKO
cd MAKO

./build.sh release   # build to bin/mako
./build.sh install   # build and copy to ~/.local/bin/mako
```

---

## Run a program

```bash
mako run examples/hello.mko
mako run examples/loops.mko
mako run examples/functions.mko
```

During development (without installing):

```bash
cd src/Mako
dotnet run -- run ../../examples/hello.mko
```

---

## Language at a glance

| Feature              | Syntax                                          |
|----------------------|-------------------------------------------------|
| Script name          | `script "My App";`                              |
| Entry point          | `main() { }`                                    |
| Print (newline)      | `print "Hello";`                                |
| Print (no newline)   | `printnl "Hello ";`                             |
| Variable             | `name = "Alice";`                               |
| Constant             | `const PI = 3.14159;`                           |
| Input                | `name = input "Enter name: ";`                  |
| String interpolation | `print "Hello, {name}! Pi = {PI}";`             |
| Arithmetic           | `result = (a + b) * 2;`                         |
| Modulo               | `x = 10 % 3;`                                   |
| Compound assign      | `x += 1;`  `x -= 2;`  `x *= 3;`  `x /= 4;`    |
| Boolean              | `active = true;`                                |
| None                 | `x = none;`                                     |
| If / else if         | `if x > 10 { } else if x == 10 { } else { }`   |
| Logical              | `x > 0 and x < 10`  /  `a or b`  /  `not done` |
| While loop           | `while i < 10 { i += 1; }`                     |
| For loop             | `for item in list { }`                          |
| Range                | `for i in range(10) { }`                        |
| Break / continue     | `break;`  /  `continue;`                        |
| Function             | `fn add(a, b) { return a + b; }`                |
| Function call        | `result = add(3, 4);`                           |
| List                 | `nums = [1, 2, 3];`                             |
| List index           | `nums[0]`  /  `nums[-1]`                        |
| Shell command        | `run "echo hello";`                             |
| Comment              | `// line`  /  `/* block */`                     |
| Namespace            | `namespace Math;`                               |
| Import               | `use "mathlib.mko";`                            |
| Namespaced call      | `Math.add(3, 4)`                                |

---

## Built-in functions

| Category | Functions |
|----------|-----------|
| Type     | `type(x)` `to_num(x)` `to_str(x)` |
| Math     | `abs` `floor` `ceil` `sqrt` `round` `pow` `max` `min` |
| Range    | `range(n)` `range(start, stop)` `range(start, stop, step)` |
| String   | `len` `upper` `lower` `trim` `contains` `starts_with` `ends_with` `replace` `split` `join` |
| List     | `len` `push` `pop` `first` `last` `reverse` `has` |
| Program  | `assert(cond, msg)` `exit(code)` |

---

## Namespaces

Split your code into modules with `namespace` and `use`:

```mako
// mathlib.mko
namespace Math;

fn add(a, b) { return a + b; }
fn clamp(v, lo, hi) {
    if v < lo { return lo; }
    if v > hi { return hi; }
    return v;
}
```

```mako
// main.mko
script "My App";
use "mathlib.mko";

main() {
    print Math.add(10, 5);
    print Math.clamp(99, 0, 10);
}
```

---

## Examples

| File                        | What it shows                            |
|-----------------------------|------------------------------------------|
| `examples/hello.mko`        | Minimal hello world                      |
| `examples/variables.mko`    | All variable types                       |
| `examples/input.mko`        | Reading user input                       |
| `examples/math.mko`         | Arithmetic and comparisons               |
| `examples/booleans.mko`     | Boolean values                           |
| `examples/greet.mko`        | Input + if/else if/else                  |
| `examples/temperature.mko`  | Temperature converter                    |
| `examples/quiz.mko`         | Simple quiz game                         |
| `examples/shell.mko`        | Running shell commands                   |
| `examples/loops.mko`        | while + for + FizzBuzz                   |
| `examples/functions.mko`    | fn, return, recursion, built-ins         |
| `examples/lists.mko`        | Lists, indexing, push/pop, for-each      |
| `examples/strings.mko`      | String built-ins                         |
| `examples/control.mko`      | break, continue, not, printnl            |
| `examples/mathlib.mko`      | Namespace module (Math library)          |
| `examples/namespaces.mko`   | use + Namespace.func() calls             |
| `examples/v02features.mko`  | const, range, assert, interpolation      |

---

## Docs

- [Getting Started](docs/getting-started.md) — install, build, first program
- [Language Reference](docs/language-reference.md) — complete spec
- [Roadmap](docs/roadmap.md) — what is planned next

---

## Project structure

```
MAKO/
  src/
    Mako/
      Mako.csproj       project file
      Program.cs        CLI entry point
      Token.cs          token types
      Lexer.cs          source text → token list
      Ast.cs            AST node types
      Parser.cs         token list → AST (recursive descent)
      Interpreter.cs    AST → execution (tree-walk)
      MakoError.cs      error type with line numbers
  examples/             sample .mko programs
  docs/                 language docs and roadmap
  build.sh              build and install script
  CHANGELOG.md          version history
```

---

## License

MIT — see [LICENSE](LICENSE).

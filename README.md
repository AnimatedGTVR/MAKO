# MAKO

**A simple, sharp programming language — easy to learn, easy to build with.**

MAKO blends the readability of Python, the simplicity of Lua/Luau, and the structure of C-style languages. No heavy boilerplate, no confusing syntax. Just write code and run it.

> **Status:** v0.1.0 — first working interpreter. Core language is running.

---

## Quick look

```mako
script "Hello";

main() {
    print "Hello from MAKO";
}
```

```mako
script "Greeter";

main() {
    name = input "What is your name? ";
    time = input "Morning, afternoon, or evening? ";

    if time == "morning" {
        greeting = "Good morning";
    }
    else if time == "afternoon" {
        greeting = "Good afternoon";
    }
    else {
        greeting = "Good evening";
    }

    print greeting + ", " + name + "!";
}
```

---

## Design goals

- **Easy to learn** — beginner-friendly, minimal concepts to remember
- **Easy to type** — no symbols that are hard to reach on a normal keyboard
- **No boilerplate** — no `class Program`, no `namespace`, no ceremony
- **Structured** — C-style braces `{}`, not indentation-sensitive
- **Practical** — useful for real tools, scripts, backends, and utilities
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
mako run examples/greet.mko
mako run examples/temperature.mko
```

During development (without installing):

```bash
cd src/Mako
dotnet run -- run ../../examples/hello.mko
```

---

## Language at a glance

| Feature         | Syntax                                    |
|-----------------|-------------------------------------------|
| Script name     | `script "My App";`                        |
| Entry point     | `main() { }`                              |
| Print           | `print "Hello";`                          |
| Variable        | `name = "Alice";`                         |
| Input           | `name = input "Enter name: ";`            |
| Arithmetic      | `result = (a + b) * 2;`                   |
| String join     | `print "Hello " + name;`                  |
| Boolean         | `active = true;`                          |
| If / else if    | `if x > 10 { } else if x == 10 { } else { }` |
| NOT             | `if !done { }`                            |
| Shell command   | `run "echo hello";`                       |
| Comment         | `// this is a comment`                    |

---

## Examples

| File                          | What it shows                              |
|-------------------------------|--------------------------------------------|
| `examples/hello.mko`          | Minimal hello world                        |
| `examples/variables.mko`      | All variable types                         |
| `examples/input.mko`          | Reading user input                         |
| `examples/math.mko`           | Arithmetic and comparisons                 |
| `examples/booleans.mko`       | Boolean values and `!`                     |
| `examples/greet.mko`          | Input + if / else if / else                |
| `examples/temperature.mko`    | Temperature converter                      |
| `examples/quiz.mko`           | Simple quiz game                           |
| `examples/shell.mko`          | Running shell commands                     |

---

## Docs

- [Getting Started](docs/getting-started.md) — install, build, first program
- [Language Reference](docs/language-reference.md) — complete v0.1 spec
- [Roadmap](docs/roadmap.md) — what is planned for v0.2 and beyond

---

## Project structure

```
MAKO/
  src/
    Mako/
      Mako.csproj       project file
      Program.cs        CLI entry point (mako run / version / help)
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

# MAKO Roadmap

This document tracks what has been built and what is planned for future versions.

---

## v0.1 — First Working Interpreter ✅

The core of the language is running.

- [x] `.mko` file extension
- [x] `script "Name";` declaration
- [x] `main() { }` entry point
- [x] `print expression;`
- [x] Variables — `name = value;`
- [x] String literals and joining with `+`
- [x] Number literals (integers and decimals)
- [x] Boolean literals — `true`, `false`
- [x] `input "prompt"` — reads a line from stdin
- [x] Arithmetic — `+`, `-`, `*`, `/`
- [x] Comparisons — `==`, `!=`, `<`, `>`, `<=`, `>=`
- [x] `!` logical NOT
- [x] `if` / `else if` / `else`
- [x] `run "shell command";`
- [x] `//` line comments
- [x] Automatic type coercion (string + number, truthy checks)
- [x] Clean error messages with line numbers

---

## v0.2 — Loops and Functions

Make MAKO useful for real logic and reusable code.

- [ ] `while condition { }` loop
- [ ] `loop { } until condition` — loop that always runs at least once
- [ ] `break` and `continue` inside loops
- [ ] User-defined functions:
  ```mako
  fn greet(name) {
      print "Hello " + name;
  }

  main() {
      greet("Alice");
  }
  ```
- [ ] `return` from functions
- [ ] Functions can return values:
  ```mako
  fn add(a, b) {
      return a + b;
  }
  result = add(3, 4);
  ```
- [ ] `none` keyword for null / empty value

---

## v0.3 — Lists and Built-ins

Data structures and useful built-in tools.

- [ ] Lists (ordered, dynamic):
  ```mako
  items = [1, 2, 3];
  items.push(4);
  print items[0];   // 1
  print items.len;  // 4
  ```
- [ ] String methods:
  ```mako
  name = "  hello  ";
  print name.trim;
  print name.upper;
  print name.lower;
  print name.len;
  ```
- [ ] Number functions: `abs`, `floor`, `ceil`, `round`
- [ ] `print` with no newline: `write "text";`
- [ ] Multi-line strings (triple-quote):
  ```mako
  msg = """
  Line one
  Line two
  """;
  ```

---

## v0.4 — Modules and Imports

Let programs be split across files and use capability layers.

- [ ] `use math;` — standard math tools
- [ ] `use file;` — read and write files
- [ ] `use time;` — dates, timestamps, sleep
- [ ] `use json;` — parse and build JSON
- [ ] `use http;` — simple HTTP requests (for scripting tools)
- [ ] `using graphics;` — 2D rendering capability flag
- [ ] Import from other `.mko` files:
  ```mako
  use "./utils";
  ```

---

## v0.5 — Tables / Objects

Structured data without heavy OOP overhead.

- [ ] Tables (key-value maps):
  ```mako
  person = {
      name = "Alice",
      age  = 30,
  };
  print person.name;
  person.age = 31;
  ```
- [ ] Nested tables
- [ ] Tables passed to functions by reference

---

## v1.0 — First Stable Release

MAKO is polished and ready for real use.

- [ ] Full standard library
- [ ] Compiled to bytecode for better performance
- [ ] Optional static type hints (not enforced, just for tooling):
  ```mako
  name: string = "Alice";
  age:  number = 30;
  ```
- [ ] `mako check file.mko` — lint and type-hint validation
- [ ] `mako fmt file.mko` — auto-formatter
- [ ] Official docs site
- [ ] Package manager (`mako install package-name`)
- [ ] Windows and macOS support in official releases

---

## Long term

- Compile to native binary via LLVM or QBE
- Embeddable as a scripting engine in Rust/C# applications
- MAKO for game scripting (2D/3D via `using graphics`)
- MAKO on the web (via WASM)

# MAKO Language Reference

**Version:** 0.03  
**File extension:** `.mko`

---

## Overview

MAKO is a dynamically-typed, imperative scripting language. Programs are easy to read, easy to type, and easy to remember. MAKO uses C-style braces `{}`, requires no boilerplate, and runs with a single command.

---

## File structure

A MAKO program is a `.mko` file. At the top level you can have a script declaration, a namespace declaration, use/using imports, function definitions, and a main block.

```mako
script "My App";         // optional — human-readable name
namespace MyLib;         // optional — makes this file a module

using Mako2D;             // native package (built into the interpreter)
use "mathlib.mko";        // import another module (optional, repeatable)

fn helper(x) {           // function definitions (optional, repeatable)
    return x * 2;
}

main() {                 // entry point — code runs here
    print "Hello!";
}
```

Only `main()` is required for a runnable program. Library files (modules) omit `main()` and use `namespace` instead.

---

## Comments

```mako
# hash comment (Python-style)
// double-slash comment
/* block comment
   spans multiple lines */

print "hello"; # inline comment
```

---

## Variables

Declare a variable by assigning to a name — no keyword needed:

```mako
name    = "MAKO";
version = 0.02;
active  = true;
nothing = none;
```

Names can contain letters, digits, and `_`, but must start with a letter or `_`.  
Variables can be reassigned and can change type freely.

### Constants

Use `const` for values that must never change:

```mako
const PI      = 3.14159;
const APP     = "MAKO";
const MAX_LEN = 100;
```

Attempting to reassign a `const` is a runtime error.

---

## Types

| Type    | Example             | `type(x)` returns | Notes                                        |
|---------|---------------------|--------------------|----------------------------------------------|
| String  | `"hello"`           | `"string"`         | Double-quoted. Supports escape sequences.    |
| Number  | `42`, `3.14`        | `"number"`         | All numbers are 64-bit floats internally.    |
| Boolean | `true`, `false`     | `"bool"`            | Literal keywords.                            |
| List    | `[1, "two", true]`  | `"list"`            | Ordered, mixed-type, dynamic.                |
| Dict    | `{"hp": 100}`       | `"dict"`            | String-keyed map. See [Dicts](#dicts).       |
| Fn      | `fn(x) => x * 2`    | `"fn"`              | A lambda value. See [Lambdas](#lambdas).     |
| None    | `none`              | `"none"`            | Represents "no value".                       |

---

## Strings

```mako
greeting = "Hello, world!";
```

### Escape sequences

| Sequence | Meaning      |
|----------|--------------|
| `\n`     | Newline      |
| `\t`     | Tab          |
| `\"`     | Double quote |
| `\\`     | Backslash    |
| `\r`     | Carriage return |

### Indexing

```mako
s = "hello";
print s[0];    // "h"
print s[-1];   // "o"  (last character)
```

Negative indices count from the end, same as list indexing. Use `slice(s, start, end)`
(see [Built-in functions](#built-in-functions)) for a substring.

### Concatenation

`+` joins strings. If either side is a string, the other side is coerced:

```mako
print "Score: " + 42;       // Score: 42
print "Pi is " + 3.14159;   // Pi is 3.14159
```

### String interpolation

Embed any expression directly inside a string with `{expr}`:

```mako
name = "Alice";
age  = 30;
print "Hello, {name}! You are {age} years old.";
print "Next year you will be {age + 1}.";
print "Pi = {3.14159}";
```

Use `{{` and `}}` for literal braces:

```mako
print "Use {{braces}} like this";   // Use {braces} like this
```

---

## Numbers

All numbers are 64-bit floats. Whole numbers print without a decimal point.

### Arithmetic

| Operator | Meaning        |
|----------|----------------|
| `a + b`  | Addition       |
| `a - b`  | Subtraction    |
| `a * b`  | Multiplication |
| `a / b`  | Division       |
| `a % b`  | Modulo         |
| `-a`     | Negation       |

### Compound assignment

```mako
x = 10;
x += 5;   // x = 15
x -= 3;   // x = 12
x *= 2;   // x = 24
x /= 4;   // x = 6
```

---

## Booleans

```mako
active = true;
done   = false;
```

### Truthiness

| Value                | Treated as |
|----------------------|------------|
| `true`               | true       |
| `false`              | false      |
| `0`                  | false      |
| Any non-zero number  | true       |
| `""`                 | false      |
| Any non-empty string | true       |
| `[]`                 | false      |
| Any non-empty list   | true       |
| `none`               | false      |

---

## Comparisons

| Operator | Meaning               |
|----------|-----------------------|
| `==`     | Equal                 |
| `!=`     | Not equal             |
| `<`      | Less than             |
| `>`      | Greater than          |
| `<=`     | Less than or equal    |
| `>=`     | Greater than or equal |

`==` works for any type, including lists.

---

## Logical operators

| Operator   | Meaning                            |
|------------|------------------------------------|
| `a and b`  | True if both are truthy (short-circuits) |
| `a or b`   | True if either is truthy (short-circuits) |
| `not a`    | Inverts truthiness                 |
| `!a`       | Same as `not a`                    |

```mako
if x > 0 and x < 100 {
    print "in range";
}

result = value or "default";   // returns "default" if value is falsy
```

---

## Operator precedence

From highest to lowest:

| Level | Operators                               |
|-------|-----------------------------------------|
| 1     | `!` `-` (unary)                         |
| 2     | `*` `/` `%`                             |
| 3     | `+` `-`                                 |
| 4     | `==` `!=` `<` `>` `<=` `>=`            |
| 5     | `and` `or`                              |

Use `()` to override:

```mako
result = (2 + 3) * 4;   // 20, not 14
```

---

## If / else if / else

```mako
if condition {
    // ...
} else if other {
    // ...
} else {
    // ...
}
```

Conditions do not need parentheses.

```mako
score = 85;
if score >= 90      { print "A"; }
else if score >= 80 { print "B"; }
else if score >= 70 { print "C"; }
else                { print "Below C"; }
```

---

## While loops

```mako
i = 0;
while i < 5 {
    print i;
    i += 1;
}
```

---

## For loops

Iterate over a list:

```mako
fruits = ["apple", "banana", "cherry"];
for fruit in fruits {
    print fruit;
}
```

Use `range()` to loop over numbers:

```mako
for i in range(5)          { print i; }        // 0 1 2 3 4
for i in range(1, 6)       { print i; }        // 1 2 3 4 5
for i in range(0, 10, 2)   { print i; }        // 0 2 4 6 8
for i in range(10, 0, -1)  { print i; }        // 10 9 8 ... 1
```

---

## Break and continue

```mako
i = 0;
while i < 10 {
    i += 1;
    if i % 2 == 0 { continue; }   // skip even numbers
    if i == 7     { break; }      // stop at 7
    print i;
}
```

Both work inside `while` and `for` loops.

---

## Functions

Define with `fn`, call by name:

```mako
fn add(a, b) {
    return a + b;
}

result = add(3, 4);   // 7
```

Functions can call themselves recursively:

```mako
fn factorial(n) {
    if n <= 1 { return 1; }
    return n * factorial(n - 1);
}

print factorial(10);   // 3628800
```

Functions have their own scope — variables inside do not leak out. Parameters shadow outer variables with the same name.

`return` with no value returns `none`. Reaching the end of a function also returns `none`.

---

## Lambdas

An anonymous function value, in two forms:

```mako
double = fn(x) => x * 2;        // arrow form — single expression, implicit return
apply  = fn(x) {                // block form — full statements, explicit return
    print x;
    return x + 1;
};

print double(21);   // 42, call it like any function
```

Lambdas capture the variables in scope at the point they're created (closures).
`type(double)` returns `"fn"`.

They're most useful with the higher-order built-ins:

```mako
evens   = filter([1, 2, 3, 4], fn(x) => x % 2 == 0);   // [2, 4]
squares = map([1, 2, 3], fn(x) => x * x);              // [1, 4, 9]
total   = reduce([1, 2, 3], fn(a, b) => a + b, 0);     // 6
```

See [Built-in functions](#built-in-functions) for the full list (`map`, `filter`,
`reduce`, `sort_by`, `each`, `any`, `all`).

---

## Lists

```mako
nums   = [1, 2, 3, 4, 5];
mixed  = ["hello", 42, true, none];
empty  = [];
```

### Indexing

```mako
print nums[0];    // 1  (first)
print nums[-1];   // 5  (last)
print nums[-2];   // 4  (second from last)
```

### Assignment

```mako
nums[0] = 99;
```

### Concatenation

```mako
a = [1, 2];
b = [3, 4];
c = a + b;   // [1, 2, 3, 4]
```

### Slicing

```mako
xs = [10, 20, 30, 40, 50];
print slice(xs, 1, 3);   // [20, 30] — end-exclusive
```

`start`/`end` are clamped into range rather than erroring, so an out-of-bounds
slice just returns as much as exists (down to an empty list) instead of crashing.
`slice` works the same way on strings.

---

## Dicts

A dict is a string-keyed map:

```mako
d = {"name": "slime", "hp": 100};
print d["hp"];          // 100
d["hp"] = d["hp"] - 10; // write by key
d["new"] = true;        // add a key
```

### Nesting

Dicts and lists can contain each other freely:

```mako
player = {
    "name": "Robin",
    "pos": [0, 0],
    "inventory": [{"item": "sword", "qty": 1}],
};
print player["inventory"][0]["item"];   // "sword"
```

### Iteration

`for key in dict` iterates keys:

```mako
for key in d { print "{key} = {d[key]}"; }
```

### Dict built-ins

| Function | Description |
|---|---|
| `has(d, key)` | `true` if `key` exists |
| `keys(d)` | List of keys |
| `values(d)` | List of values |
| `remove(d, key)` | Delete a key (mutates in place) |
| `get(d, key, default?)` | Read a key, or `default` (or `none`) if missing — never errors |
| `merge(d1, d2)` | New dict with `d2`'s keys layered over `d1`'s |

---

## Print

```mako
print "Hello";        // with newline
printnl "Hello ";     // without newline (continues on same line)
printnl "World";
print "";             // just a newline
```

---

## Input

```mako
name = input "Enter your name: ";
```

Always returns a string. For numeric input, coercion happens automatically in math expressions, or use `to_num()` explicitly.

---

## Run

Execute a shell command:

```mako
run "echo hello";
run "ls -lh " + folder;
```

Runs in `/bin/sh`, blocks until complete.

---

## Namespaces and modules

### Defining a module

```mako
// mathlib.mko
namespace Math;

fn add(a, b)  { return a + b; }
fn sub(a, b)  { return a - b; }
fn clamp(v, lo, hi) {
    if v < lo { return lo; }
    if v > hi { return hi; }
    return v;
}
```

### Using a module

```mako
script "My App";
use "mathlib.mko";

main() {
    print Math.add(10, 5);
    print Math.clamp(99, 0, 10);
}
```

- `use` paths are relative to the script being run
- The imported file must have a `namespace` declaration
- Functions are called as `Namespace.funcname(args)`
- Multiple `use` lines are allowed

### Native packages

Built into the interpreter — no file to write, just activate with `using`:

```mako
using MakoUI;   // desktop UI (Dear ImGui)
using Mako2D;   // 2D rendering
using Mako3D;   // 3D rendering
using Inputs;   // keyboard/mouse/gamepad polling
using Audio;    // playback + synthesized sound
using Net;      // HTTP requests + JSON

main() {
    Mako2D.init(800, 600, "My Game");
    // ...
}
```

Each has its own reference doc: [MakoUI](makoui.md), [Mako2D](mako2d.md),
[Mako3D](mako3d.md), [Inputs](inputs.md), [Audio](audio.md), [Net](net.md).

### GitHub packages

Fetched and cached on first run:

```mako
using coollib from "github:Someone/coollib";

main() {
    coollib.do_thing();
}
```

`mko list` shows installed packages; `mko cache clear [pkg]` removes cached ones.
`mko search [query]` opens a graphical browser of every known package (native and
GitHub) — pass `--term` for a plain-text listing instead. `mko info <pkg>` shows
one package's description, status, and exact `using` line.

### Discovering a GitHub package before installing it

`mko search github:User/Repo` / `mko info github:User/Repo` fetch that repo's
`mako.json` manifest live (no cloning) and show it the same way as a registry
entry. A repo needs a `mako.json` at its root to be discoverable this way:

```json
{
  "name": "CoolLib",
  "description": "Does cool things.",
  "version": "1.2.0",
  "usage": "using coollib from \"github:Someone/coollib\";"
}
```

`name`/`description` are required; `version` and `usage` are optional (`usage`
defaults to `using <name> from "github:User/Repo";` if omitted). A repo with
no `mako.json` isn't an error to have — `using X from "github:...";` still
works without one — it just won't show up in `mko search`/`mko info` lookups.

---

## Error handling

`try`/`catch` runs a block and recovers from any runtime error instead of
crashing the script:

```mako
try {
    n = to_num("not a number");
} catch err {          // err is bound to the error message (a string)
    print "failed: {err}";
}

try { risky(); } catch { }   // the catch variable is optional
```

Only the `try` block is protected — an error inside `catch` itself is not caught.
`assert(cond, "message")` is the usual way to raise an error deliberately; it
throws (and is catchable) when `cond` is falsy.

---

## Built-in functions

### Type and conversion

| Function     | Description                              |
|--------------|------------------------------------------|
| `type(x)`    | Returns `"string"`, `"number"`, `"bool"`, `"list"`, `"dict"`, `"fn"`, or `"none"` |
| `to_num(x)`  | Converts a string to a number            |
| `to_str(x)`  | Converts any value to a string           |

### Math

| Function          | Description                      |
|-------------------|-----------------------------------|
| `abs(x)`          | Absolute value                   |
| `floor(x)`        | Round down                       |
| `ceil(x)`         | Round up                         |
| `round(x)`        | Round to nearest (half up)       |
| `sqrt(x)`         | Square root                      |
| `pow(x, y)`       | x to the power of y              |
| `max(a, b)`       | Larger of two numbers            |
| `min(a, b)`       | Smaller of two numbers           |
| `clamp(v, lo, hi)`| Pin `v` into `[lo, hi]`           |
| `lerp(a, b, t)`   | Linear interpolation: `a + (b-a)*t` |
| `sign(x)`         | `-1`, `0`, or `1`                 |
| `sin(x)` `cos(x)` `tan(x)` | Trig functions, radians  |
| `atan2(y, x)`     | Angle of vector `(y, x)`, radians |
| `pi()`            | `3.14159265358979...`             |

### Range

| Function                    | Description                                |
|-----------------------------|--------------------------------------------|
| `range(n)`                  | List `[0, 1, ..., n-1]`                   |
| `range(start, stop)`        | List from start up to (not including) stop |
| `range(start, stop, step)`  | With custom step (can be negative)         |

### String

| Function                     | Description                                  |
|------------------------------|----------------------------------------------|
| `len(s)`                     | Length of string                             |
| `upper(s)`                   | Uppercase                                    |
| `lower(s)`                   | Lowercase                                    |
| `trim(s)`                    | Remove leading/trailing whitespace           |
| `contains(s, sub)`           | True if `sub` is found in `s`               |
| `starts_with(s, prefix)`     | True if `s` starts with `prefix`            |
| `ends_with(s, suffix)`       | True if `s` ends with `suffix`              |
| `replace(s, old, new)`       | Replace all occurrences of `old` with `new` |
| `split(s, sep)`              | Split string into a list                     |
| `join(list, sep)`            | Join list elements into a string             |
| `slice(s, start, end)`       | Substring, end-exclusive, clamped to range   |

### List

| Function         | Description                                    |
|------------------|------------------------------------------------|
| `len(list)`      | Number of elements                             |
| `push(list, val)`| Append a value (mutates in place)              |
| `pop(list)`      | Remove and return last element                 |
| `first(list)`    | First element                                  |
| `last(list)`     | Last element                                   |
| `reverse(list)`  | Return a new reversed list                     |
| `has(list, val)` | True if value is in the list                   |
| `slice(list, start, end)` | Sub-list, end-exclusive, clamped to range |

### Dict

See [Dicts](#dicts) for the full list: `has`, `keys`, `values`, `remove`, `get`, `merge`.
`len(dict)` also works (number of keys).

### Higher-order (take a lambda — see [Lambdas](#lambdas))

| Function | Description |
|---|---|
| `map(xs, fn)` | New list — transform each element |
| `filter(xs, fn)` | New list — keep elements where `fn` is truthy |
| `reduce(xs, fn, init)` | Fold to a single value: `fn(acc, item)` each step |
| `sort_by(xs, fn?)` | New sorted list by `fn`'s key — omit `fn` to sort numbers/strings by natural order |
| `each(xs, fn)` | Call `fn(item)` for each element (side effects) |
| `any(xs, fn)` / `all(xs, fn)` | `true` if `fn` is truthy for at least one / every element |

### Geometry & collision

| Function | Description |
|---|---|
| `dist(x1, y1, x2, y2)` | 2D distance between two points |
| `dist3d(x1, y1, z1, x2, y2, z2)` | 3D distance |
| `rects_overlap(ax, ay, aw, ah, bx, by, bw, bh)` | AABB overlap test, two `(x, y, w, h)` rects |
| `circles_overlap(x1, y1, r1, x2, y2, r2)` | Circle overlap test |
| `box3d_overlap(min1x, min1y, min1z, max1x, max1y, max1z, min2x, min2y, min2z, max2x, max2y, max2z)` | 3D AABB overlap — takes two boxes already in min/max form, matching what `Mako3D.object_bounds()` returns |
| `point_in_rect(px, py, rx, ry, rw, rh)` | `true` if the point falls inside the rect |

### Pathfinding (for game AI)

| Function | Description |
|---|---|
| `find_path(grid, sx, sy, ex, ey)` | A* pathfinding — `grid` is a list of rows, each a list where `0`/`false` = walkable, anything truthy = wall. Returns a list of `[x, y]` steps (excludes start, includes goal), or `[]` if unreachable |
| `line_of_sight(grid, x1, y1, x2, y2)` | `true` if no wall cell blocks the straight line between two grid cells (Bresenham) |

### File I/O

| Function | Description |
|---|---|
| `read(path)` | Read a whole file as a string (errors if missing) |
| `write(path, content)` | Overwrite a file with `content` (converted to string) |
| `append(path, content)` | Append `content` to a file |
| `exists(path)` | `true` if a file or directory exists at `path` |
| `delete(path)` | Delete a file if it exists |
| `lines(path)` | Read a file as a list of lines |

### System

| Function | Description |
|---|---|
| `time()` | Current Unix time in seconds (fractional) |
| `random()` | Random number in `[0, 1)` |
| `random(lo, hi)` | Random number in `[lo, hi)` |
| `random_int(lo, hi)` | Random integer in `[lo, hi]` (inclusive) |
| `sleep(seconds)` | Block for `seconds` (fractional allowed) |
| `env(name)` | Environment variable value, or `""` if unset |
| `args()` | List of command-line arguments passed to the script |

### JSON

| Function | Description |
|---|---|
| `json_encode(v)` | Serialize any MAKO value to a JSON string |
| `json_decode(s)` | Parse a JSON string into MAKO values (dicts/lists/etc.) |

### Program control

| Function           | Description                                  |
|--------------------|----------------------------------------------|
| `assert(cond)`     | Throws an error if `cond` is falsy           |
| `assert(cond, msg)`| Throws an error with `msg` if `cond` is falsy |
| `exit()`           | Exit with code 0                             |
| `exit(code)`       | Exit with given code                         |

---

## Semicolons

Every statement ends with `;`. Blocks `{ }` do not.

```mako
name = "Alice";
print "Hi, {name}";

if name == "Alice" {
    print "Found her";
}
```

---

## Keywords

Reserved and cannot be used as variable names:

`script` `namespace` `main` `fn` `return`  
`const` `if` `else` `while` `for` `in`  
`break` `continue` `print` `printnl` `input`  
`run` `use` `using` `from` `try` `catch`  
`and` `or` `not`  
`true` `false` `none`

---

## Full example

```mako
script "Student Grades";

fn grade(score) {
    if score >= 90 { return "A"; }
    if score >= 80 { return "B"; }
    if score >= 70 { return "C"; }
    if score >= 60 { return "D"; }
    return "F";
}

fn average(scores) {
    total = 0;
    for s in scores { total += s; }
    return total / len(scores);
}

main() {
    scores = [92, 85, 78, 96, 61];
    avg    = average(scores);

    print "Scores: {scores}";
    print "Average: {round(avg)}";
    print "Grade: {grade(avg)}";

    print "--- Individual grades ---";
    for s in scores {
        print "  {s} → {grade(s)}";
    }
}
```

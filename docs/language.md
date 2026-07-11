# MAKO Language Reference

## Program structure

```mako
script "MyApp";          # optional — names the script
namespace Utils;         # optional — namespaces this file's functions

using Mako2D;                            # native package
using mylib from "github:User/Repo";     # GitHub package
use "helpers.mko";                       # local file import

const MAX_LIVES = 3;     # top-level constants

fn helper(x) { return x * 2; }

main() {                 # entry point
    print helper(21);
}
```

Statements end with `;`. Blocks use `{ }`. Comments start with `#` (or `//`).

## Variables & types

Variables are created by assignment — no declaration keyword:

```mako
count = 10;              # number (64-bit float)
name = "Robin";          # string
alive = true;            # boolean (true / false)
nothing = none;          # null value
items = [1, "two", 3.5]; # list — mixed types allowed
user = {"name": "R", "hp": 100};   # dict — string keys
```

`const` creates an immutable binding (top-level or inside a block):

```mako
const GRAVITY = 9.81;
GRAVITY = 10;            # error: cannot reassign a constant
```

Compound assignment: `+=  -=  *=  /=`.

## Strings

```mako
s = "hello";
c = s[1];                        # "e" — indexing
msg = "score: {points * 10}";    # interpolation — any expression in {}
long = "a" + "b";                # concatenation
```

## Lists

```mako
xs = [1, 2, 3];
xs[0] = 99;              # index assignment
push(xs, 4);             # append (in place)
pop(xs);                 # remove last (in place)
ys = [0] + xs;           # concatenation makes a new list
part = slice(xs, 1, 3);  # sub-list, end-exclusive
for x in xs { print x; }
```

## Dicts

```mako
d = {"hp": 100, "name": "slime"};
d["hp"] = d["hp"] - 10;      # read / write by key
d["new"] = true;             # add a key
remove(d, "new");            # delete a key
if has(d, "hp") { ... }      # key check
for key in d { print "{key} = {d[key]}"; }
```

## Structs

A named shape for a dict, plus functions that take it as their first
argument (`self`) and can be called with `instance.method(...)` syntax.

```mako
struct Point {
    x, y
}

fn Point.dist(self, other) {
    return dist(self.x, self.y, other.x, other.y);
}

main() {
    p1 = Point { x: 0, y: 0 };
    p2 = Point { x: 3, y: 4 };

    print p1.dist(p2);   # 5
    print p1.x;          # 0

    p1.x = 10;            # fields are read/write, like dict keys
    print type(p1);       # "Point", not "dict"
}
```

`struct Name { field, field, ... }` declares the shape — every field is
required at construction time (`Name { field: value, ... }`, in any order).
Methods are ordinary top-level functions named `Type.method`, declared
outside `main()` like any other `fn`; the first parameter is always the
instance (conventionally named `self`), and it's filled in automatically —
`p1.dist(p2)` calls `Point.dist` with `p1` as `self` and `p2` as `other`.

A struct instance *is* a dict underneath — `keys()`, `len()`, `for key in
instance`, `json_encode()`, and dict indexing (`p1["x"]`) all still work on
it exactly like a plain `{...}` dict. `type()` is the one thing that tells
them apart, returning the struct's name instead of `"dict"`.

There's no inheritance, no private fields, and no constructors beyond the
`Name { field: value }` literal — keep instance setup in the literal itself
or a plain function that returns one.

## Control flow

```mako
if hp <= 0 {
    print "dead";
} else if hp < 20 {
    print "hurt";
} else {
    print "fine";
}

while hp > 0 { hp = hp - 1; }

for item in [1, 2, 3] { print item; }
for i in range(10) { }        # 0..9
for i in range(2, 10) { }     # 2..9
for i in range(0, 10, 2) { }  # 0,2,4,6,8

break;      # exit the loop
continue;   # next iteration
```

Logic: `and`, `or`, `not` (short-circuit). Comparison: `==  !=  <  <=  >  >=`.
Arithmetic: `+  -  *  /  %`, unary `-x`.

Truthiness: `false`, `0`, `""`, `[]`, and `none` are falsy; everything else is truthy.

## Functions

```mako
fn add(a, b) {
    return a + b;
}

fn shout(msg) {          # no return → returns none
    print upper(msg);
}
```

Functions are recursive and can be called before their definition.

## Lambdas

```mako
double = fn(x) => x * 2;             # arrow form — single expression
apply  = fn(x) {                     # block form
    print x;
    return x + 1;
};

print double(21);                    # call like any function

# Higher-order builtins
evens   = filter([1,2,3,4], fn(x) => x % 2 == 0);
squares = map([1,2,3], fn(x) => x * x);
total   = reduce([1,2,3], fn(a, b) => a + b, 0);
```

Lambdas capture the variables in scope when they're created.

## Error handling

```mako
try {
    n = to_num("not a number");
} catch err {                 # err is the error message string
    print "failed: {err}";
}

try { risky(); } catch { }    # catch variable is optional
```

`throw expr;` raises a catchable error — `expr` is stringified as the
message (so `throw "bad input: {n}";` works via normal string
interpolation). An uncaught `throw` stops the program with a clean error
message pointing at the `throw` line, same as any other MAKO error.

```mako
fn parse_age(text) {
    n = to_num(text);
    if n < 0 {
        throw "age can't be negative: {n}";
    }
    return n;
}

try {
    parse_age("-5");
} catch err {
    print "invalid: {err}";   # "invalid: age can't be negative: -5"
}
```

`assert(cond, "message")` throws when the condition is falsy — equivalent
to `if not cond { throw "message"; }`, useful as a one-liner precondition
check.

## Modules & packages

**Local files** — `use "file.mko";` imports a file's functions. The file must
declare a `namespace`, and you call through it:

```mako
# mathlib.mko
namespace MathLib;
fn square(x) { return x * x; }

# main.mko
use "mathlib.mko";
main() { print MathLib.square(8); }
```

**Native packages** — built into the interpreter, activated by `using`:
`MakoUI`, `Mako2D`, `Mako3D`, `Inputs`, `Audio`, `Net`.

**GitHub packages** — cloned and cached on first use:

```mako
using coollib from "github:Someone/coollib";
```

Not sure what's out there? `mko search` opens a graphical package browser
(`--term` for plain text); `mko info <pkg>` shows one package's details.
Point either at a specific repo — `mko search github:User/Repo` — to fetch
its `mako.json` manifest live and preview it before installing.

## Shell & input

```mako
run "ls -la";                     # run a shell command
answer = input "Your name? ";     # prompt and read a line
print "no newline";               # print adds a newline
printnl "same line";              # printnl doesn't
```

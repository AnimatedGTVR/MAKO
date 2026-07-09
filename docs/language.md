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

`assert(cond, "message")` throws when the condition is falsy.

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

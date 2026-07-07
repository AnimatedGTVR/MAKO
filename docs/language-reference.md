# MAKO Language Reference

**Version:** 0.1  
**File extension:** `.mko`

---

## Overview

MAKO is a dynamically-typed, imperative scripting language. Programs are easy to read, easy to type, and easy to remember. MAKO uses C-style braces `{}` and requires no boilerplate to get started.

---

## File structure

Every MAKO file is a `.mko` file. A program has two optional top-level parts: a script declaration and a main block.

```mako
script "Name of Script";   // optional — names the program

main() {                   // required — code runs here
    // statements go here
}
```

---

## Comments

Comments start with `//` and run to the end of the line.

```mako
// This is a comment
print "Hello";  // This is an inline comment
```

---

## Variables

Variables are declared by assigning to a name. No keyword or type annotation is needed.

```mako
name    = "MAKO";
version = 0.1;
active  = true;
nothing = none;
```

Variable names can contain letters, digits, and underscores, but must start with a letter or underscore.

```mako
my_variable = 42;
_private     = "ok";
count2       = 100;
```

Variables can be reassigned at any time and can change type:

```mako
x = 10;
x = "now a string";
```

---

## Types

| Type    | Example          | Notes                                      |
|---------|------------------|--------------------------------------------|
| String  | `"hello"`        | Double-quoted. Supports `\n`, `\t`, `\\`, `\"`. |
| Number  | `42`, `3.14`     | All numbers are 64-bit floats internally.  |
| Boolean | `true`, `false`  | Literal keywords.                          |
| None    | `none`           | Represents "no value". Coming in v0.2.     |

---

## Print

Print a value to standard output (with a newline).

```mako
print "Hello, world";
print 42;
print "Sum: " + (1 + 2);
print active;   // prints: true
```

---

## Input

Read a line of text typed by the user. Returns a string.

```mako
name = input "Enter your name: ";
```

The prompt is displayed on the same line as the cursor, with no newline before reading. The returned value is always a string. To use it as a number, MAKO performs automatic type coercion in math expressions.

```mako
age  = input "Your age? ";
next = age + 1;            // age is coerced to a number
print "Next year: " + next;
```

---

## Strings

String literals use double quotes:

```mako
greeting = "Hello";
```

### Escape sequences

| Sequence | Meaning      |
|----------|--------------|
| `\n`     | Newline      |
| `\t`     | Tab          |
| `\"`     | Double quote |
| `\\`     | Backslash    |

### Joining strings

Use `+` to join strings. If either side of `+` is a string, the other side is automatically converted:

```mako
name   = "Mako";
year   = 2026;
print "Welcome to " + name + " " + year;   // Welcome to Mako 2026
```

---

## Numbers

MAKO supports integers and decimals. Internally they are all 64-bit floats.

```mako
x = 10;
y = 3.5;
```

### Arithmetic

| Operator | Meaning        |
|----------|----------------|
| `a + b`  | Addition       |
| `a - b`  | Subtraction    |
| `a * b`  | Multiplication |
| `a / b`  | Division       |

```mako
result = (10 + 5) * 2;   // 30
half   = 7 / 2;          // 3.5
```

Whole numbers are printed without a decimal point: `42`, not `42.0`.

---

## Booleans

Boolean values are `true` and `false`.

```mako
flag = true;
done = false;
```

### Logical NOT

```mako
if !done {
    print "Still going.";
}
```

---

## Comparisons

Comparison operators return a boolean (`true` or `false`).

| Operator | Meaning               |
|----------|-----------------------|
| `==`     | Equal                 |
| `!=`     | Not equal             |
| `<`      | Less than             |
| `>`      | Greater than          |
| `<=`     | Less than or equal    |
| `>=`     | Greater than or equal |

```mako
x = 10;
y = 20;

if x < y {
    print "x is less";
}

if x != y {
    print "not equal";
}
```

`==` works for any type:

```mako
name = "MAKO";
if name == "MAKO" { print "yes"; }

active = true;
if active == true { print "active"; }
```

---

## If / Else if / Else

```mako
if condition {
    // runs if condition is true
}
else if other_condition {
    // runs if the first was false and this is true
}
else {
    // runs if all conditions above were false
}
```

Conditions do not need parentheses.

```mako
score = input "Enter score: ";

if score >= 90 {
    print "A";
}
else if score >= 80 {
    print "B";
}
else if score >= 70 {
    print "C";
}
else {
    print "Below C";
}
```

### Truthiness

Any value can be used as a condition:

| Value              | Treated as |
|--------------------|------------|
| `true`             | true       |
| `false`            | false      |
| `0`                | false      |
| any non-zero number | true      |
| `""`               | false      |
| any non-empty string | true     |
| `none`             | false      |

---

## Run

Execute a shell command. Output goes directly to the terminal.

```mako
run "echo Hello";
run "ls -lh";
run "ls " + folder;   // dynamic commands with string joining
```

The command runs in `/bin/sh`. It blocks until the command finishes.

---

## Semicolons

Every statement must end with a semicolon `;`. Blocks `{}` do not need one after the closing brace.

```mako
name = "Alice";      // semicolon required
print "Hi " + name;  // semicolon required

if name == "Alice" {
    print "Found Alice";
}                    // no semicolon after }
```

---

## Operator precedence

From highest to lowest:

| Level | Operators       |
|-------|-----------------|
| 1     | `!` (unary NOT) |
| 2     | `*`, `/`        |
| 3     | `+`, `-`        |
| 4     | `==`, `!=`, `<`, `>`, `<=`, `>=` |

Use `()` to control evaluation order:

```mako
result = (2 + 3) * 4;   // 20, not 14
```

---

## Keywords

These names are reserved and cannot be used as variable names:

`script` `main` `print` `input` `if` `else` `run` `using` `use` `true` `false`

---

## Full example

```mako
script "Greeter";

main() {
    name = input "Enter your name: ";
    time = input "Is it morning, afternoon, or evening? ";

    if time == "morning" {
        greeting = "Good morning";
    }
    else if time == "afternoon" {
        greeting = "Good afternoon";
    }
    else if time == "evening" {
        greeting = "Good evening";
    }
    else {
        greeting = "Hello";
    }

    if name == "" {
        print greeting + ", stranger!";
    }
    else {
        print greeting + ", " + name + "!";
    }
}
```

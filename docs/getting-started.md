# Getting Started with MAKO

MAKO is a simple, sharp programming language. This guide gets you from zero to running your first program in under five minutes.

---

## 1. Install the .NET 8 SDK

MAKO's interpreter is written in C# and needs the .NET runtime to build.

**Arch Linux / Abora:**
```bash
sudo pacman -S dotnet-sdk
```

**Debian / Ubuntu:**
```bash
sudo apt install dotnet-sdk-8.0
```

**Other Linux / macOS / Windows:**  
Download from https://dotnet.microsoft.com/download

---

## 2. Get MAKO

```bash
git clone https://github.com/AnimatedGTVR/MAKO
cd MAKO
```

---

## 3. Build and install

```bash
./build.sh install
```

This builds a single self-contained binary and copies it to `~/.local/bin/mko`. Make sure that folder is in your `PATH`:

```bash
echo 'export PATH="$HOME/.local/bin:$PATH"' >> ~/.bashrc
source ~/.bashrc
```

Verify it works:

```bash
mko version   # MAKO 0.02
mko help
```

---

## 4. Your first program

Create a file called `hello.mko`:

```mako
script "Hello";

main() {
    name = input "What's your name? ";
    print "Hello, {name}! Welcome to MAKO.";
}
```

Run it:

```bash
mko run hello.mko
```

---

## 5. Try the examples

```bash
mko run examples/hello.mko
mko run examples/loops.mko
mko run examples/functions.mko
mko run examples/lists.mko
mko run examples/strings.mko
mko run examples/control.mko
mko run examples/namespaces.mko
mko run examples/v02features.mko
```

---

## 6. Key syntax at a glance

```mako
script "Demo";

fn greet(name) {
    return "Hello, {name}!";
}

main() {
    # variables
    x     = 42;
    const PI = 3.14159;

    # string interpolation
    print "x = {x}, PI = {PI}";

    # if / else
    if x > 10 {
        print "big number";
    } else {
        print "small number";
    }

    # while loop
    i = 0;
    while i < 3 {
        print "i = {i}";
        i += 1;
    }

    # for loop with range
    for n in range(1, 4) {
        print n;
    }

    # lists
    fruits = ["apple", "banana", "cherry"];
    for fruit in fruits {
        print upper(fruit);
    }

    # functions
    print greet("World");
}
```

---

## 7. Splitting code into modules

Create a library file:

```mako
// utils.mko
namespace Utils;

fn shout(msg) {
    return upper(msg) + "!!!";
}
```

Use it from your main script:

```mako
script "My App";
use "utils.mko";

main() {
    print Utils.shout("hello");   // HELLO!!!
}
```

---

## 8. What's next

- [Language Reference](language-reference.md) — the complete spec
- [Roadmap](roadmap.md) — what's built and what's still planned

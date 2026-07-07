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

## 3. Build

```bash
./build.sh release
```

This produces `bin/mako`. To install it system-wide:

```bash
./build.sh install
```

This copies `mako` to `~/.local/bin`. Make sure that folder is in your `PATH`:

```bash
echo 'export PATH="$HOME/.local/bin:$PATH"' >> ~/.bashrc
source ~/.bashrc
```

---

## 4. Your first program

Create a file called `hello.mko`:

```mako
script "Hello";

main() {
    print "Hello from MAKO";
}
```

Run it:

```bash
mako run hello.mko
```

Output:

```
Hello from MAKO
```

---

## 5. Try the examples

```bash
mako run examples/hello.mko
mako run examples/input.mko
mako run examples/math.mko
mako run examples/variables.mko
mako run examples/greet.mko
mako run examples/booleans.mko
mako run examples/temperature.mko
mako run examples/quiz.mko
mako run examples/shell.mko
```

---

## 6. What's next

Read the [Language Reference](language-reference.md) to learn everything MAKO can do in v0.1.

Check the [Roadmap](roadmap.md) to see what is coming.

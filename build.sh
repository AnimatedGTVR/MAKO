#!/usr/bin/env bash
# build.sh — build and optionally install the MAKO interpreter

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
SRC="$SCRIPT_DIR/src/Mako"
OUT="$SCRIPT_DIR/bin"

usage() {
    echo "Usage: ./build.sh [dev|release|install|clean]"
    echo ""
    echo "  dev      Build debug binary for testing  (default)"
    echo "  release  Build optimised release binary"
    echo "  install  Build release binary and install to ~/.local/bin"
    echo "  clean    Remove build artifacts"
}

cmd="${1:-dev}"

case "$cmd" in
    dev)
        echo "Building MAKO (debug)..."
        cd "$SRC"
        dotnet build -c Debug
        echo "Run with: dotnet run -- run examples/hello.mko"
        ;;

    release)
        echo "Building MAKO (release) → $OUT"
        mkdir -p "$OUT"
        cd "$SRC"
        dotnet publish -c Release -r linux-x64 --self-contained -o "$OUT"
        echo "Binary: $OUT/mako"
        ;;

    install)
        echo "Building MAKO (release) and installing to ~/.local/bin..."
        mkdir -p "$OUT"
        cd "$SRC"
        dotnet publish -c Release -r linux-x64 --self-contained -o "$OUT"
        mkdir -p ~/.local/bin
        cp "$OUT/mako" ~/.local/bin/mako
        chmod +x ~/.local/bin/mako
        echo "Installed: ~/.local/bin/mako"
        echo "Make sure ~/.local/bin is in your PATH."
        ;;

    clean)
        echo "Cleaning..."
        rm -rf "$SRC/bin" "$SRC/obj" "$OUT"
        echo "Done."
        ;;

    help|--help|-h)
        usage
        ;;

    *)
        echo "Unknown command: $cmd"
        usage
        exit 1
        ;;
esac

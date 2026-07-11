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
        dotnet publish -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o "$OUT"
        echo "Binary: $OUT/mko"
        ;;

    install)
        echo "Building MAKO (release) and installing to ~/.local/bin..."
        mkdir -p "$OUT"
        cd "$SRC"
        dotnet publish -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o "$OUT"

        # Install native libs and binary to ~/.local/share/mko/bin/
        MKO_BIN="$HOME/.local/share/mko/bin"
        mkdir -p "$MKO_BIN"
        cp "$OUT/mko" "$MKO_BIN/mko.bin"
        # Copy all native .so files
        cp "$OUT"/*.so*        "$MKO_BIN/" 2>/dev/null || true
        cp "$OUT"/runtimes/linux-x64/native/*.so* "$MKO_BIN/" 2>/dev/null || true
        chmod +x "$MKO_BIN/mko.bin"

        # Install a thin wrapper to ~/.local/bin that sets LD_LIBRARY_PATH
        mkdir -p ~/.local/bin
        cat > ~/.local/bin/mko << 'WRAPPER'
#!/usr/bin/env bash
MKO_BIN="$HOME/.local/share/mko/bin"
exec env LD_LIBRARY_PATH="$MKO_BIN${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}" "$MKO_BIN/mko.bin" "$@"
WRAPPER
        chmod +x ~/.local/bin/mko
        echo "Installed: ~/.local/bin/mko"

        # Install examples so `mko examples/hello.mko` works from anywhere.
        MKO_DATA="$HOME/.local/share/mko"
        mkdir -p "$MKO_DATA"
        if [ -d "$SCRIPT_DIR/examples" ]; then
            cp -r "$SCRIPT_DIR/examples" "$MKO_DATA/"
            echo "Installed examples → $MKO_DATA/examples/"
        fi
        if [ -d "$SCRIPT_DIR/Images" ]; then
            cp -r "$SCRIPT_DIR/Images" "$MKO_DATA/"
            echo "Installed example images → $MKO_DATA/Images/"
        fi

        # Install Mako.Web's source too, so `mko build --target web` can
        # `dotnet publish` it even when running the installed binary from
        # outside the repo checkout (Foundry.cs's FindMakoWebProject looks
        # here as a fallback). Mako.Web.csproj links (not copies) files out
        # of src/Mako via relative "..\Mako\X.cs" paths, so src/Mako must be
        # installed alongside it at the same relative layout.
        if [ -d "$SCRIPT_DIR/src/Mako.Web" ]; then
            mkdir -p "$MKO_DATA/src"
            rm -rf "$MKO_DATA/src/Mako.Web" "$MKO_DATA/src/Mako"
            cp -r "$SCRIPT_DIR/src/Mako.Web" "$MKO_DATA/src/"
            cp -r "$SCRIPT_DIR/src/Mako" "$MKO_DATA/src/"
            rm -rf "$MKO_DATA/src/Mako.Web/bin" "$MKO_DATA/src/Mako.Web/obj" \
                   "$MKO_DATA/src/Mako/bin" "$MKO_DATA/src/Mako/obj"
            echo "Installed Mako.Web source → $MKO_DATA/src/Mako.Web/ (for web builds)"
        fi
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

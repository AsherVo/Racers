#!/bin/bash
# Builds EngineNative for the browser host: fonts + Rive on WebGL 2, as static wasm libraries that
# the .NET build links into dotnet.native.wasm.
#
# Everything is compiled with the emsdk .NET links with (the one tools/build-sdl-wasm.sh installs),
# because static wasm libraries must match the linker's Emscripten version.
# Output: artifacts/native/wasm/EngineNative.a (the name must match the [LibraryImport] name) plus
# Rive's libraries next to it.
set -euo pipefail

ROOT=$(cd "$(dirname "$0")/.." && pwd)
EMSDK_VERSION=${EMSDK_VERSION:-3.1.56}   # .NET 10's Emscripten version
OUT="$ROOT/artifacts/native/wasm"
. "$ROOT/tools/fetch-rive.sh"

EMSDK="${SDL_WASM_WORK:-$WORK/sdl-wasm}/emsdk"
if [ ! -d "$EMSDK" ]; then
  echo "Missing $EMSDK. Run tools/build-sdl-wasm.sh first." >&2
  exit 1
fi
"$EMSDK/emsdk" activate "$EMSDK_VERSION" >/dev/null
# shellcheck disable=SC1091
EMSDK_QUIET=1 . "$EMSDK/emsdk_env.sh"

# .NET links with native wasm exceptions, so setjmp (libpng, libjpeg) must use wasm SjLj too.
# No LTO: .NET's linker would compile LTO bitcode with its own, newer LLVM, which emits a setjmp
# ABI (__wasm_setjmp) that its Emscripten 3.1.56 runtime doesn't provide. Compiling to wasm here
# lowers setjmp with emsdk 3.1.56's LLVM, which matches.
WASM_FLAGS="-fwasm-exceptions -sSUPPORT_LONGJMP=wasm"

# RIVE_EMSDK_VERSION=none: use the emsdk activated above instead of the one Rive pins.
# EMCC_CFLAGS reaches every emcc call; ninja doesn't track it, hence the dedicated out dir.
(
  cd "$RIVE/renderer"
  RIVE_EMSDK_VERSION=none RIVE_OUT=out/wasm_dotnet_release EMCC_CFLAGS="$WASM_FLAGS" \
    RIVE_PREMAKE_ARGS="--with_rive_text --with_rive_layout --no-lto" \
    "$RIVE/build/build_rive.sh" ninja release wasm -- $RIVE_TARGETS
)
LIBS="$RIVE/renderer/out/wasm_dotnet_release"

# Reuse the -D flags Rive was compiled with; they change class layouts.
DEFINES=$(grep -m1 '^CXXFLAGS' "$LIBS/build_rive.ninja" | tr ' ' '\n' | grep '^-D' | tr '\n' ' ')

FLAGS="-O2 -msimd128 $WASM_FLAGS -I$ROOT/native/include"
BUILD="$WORK/engine-native-wasm"
rm -rf "$BUILD" && mkdir -p "$BUILD" "$OUT"

emcc $FLAGS -std=c11 -I"$ROOT/native/third_party" -c "$ROOT/native/src/font.c" -o "$BUILD/font.o"
for src in rive_common.cpp rive_webgl.cpp; do
  # shellcheck disable=SC2086
  em++ $FLAGS -std=c++17 -fno-exceptions -fno-rtti $DEFINES \
    -I"$RIVE/include" -I"$RIVE/renderer/include" \
    -c "$ROOT/native/src/$src" -o "$BUILD/${src%.*}.o"
done

rm -f "$OUT"/*.a
emar rcs "$OUT/EngineNative.a" "$BUILD/font.o" "$BUILD/rive_common.o" "$BUILD/rive_webgl.o"
for lib in $RIVE_LIBS; do
  cp "$LIBS/$lib" "$OUT/"
done

echo "$RIVE_COMMIT / emsdk $EMSDK_VERSION" > "$OUT/VERSION"
echo "Built $OUT"
ls -la "$OUT"

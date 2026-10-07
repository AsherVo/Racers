#!/bin/bash
# Builds libEngineNative.dylib (fonts + Rive on Metal) for macOS, universal (arm64 + x86_64).
#
# Rive's runtime and renderer are built from a pinned rive-runtime commit with Rive's own build
# script (premake + ninja), in a cache directory outside the repo. The bridge in native/ is then
# compiled with the same feature defines (they change class layouts) and linked into one dylib.
# Output: artifacts/native/osx/libEngineNative.dylib
set -euo pipefail

ROOT=$(cd "$(dirname "$0")/.." && pwd)
OUT="$ROOT/artifacts/native/osx"
MIN_MACOS=12.0   # matches LSMinimumSystemVersion in package-macos.sh
. "$ROOT/tools/fetch-rive.sh"
mkdir -p "$OUT"

# Text (HarfBuzz/SheenBidi) and layout (Yoga) are on so files that use them render; the scripted
# canvas and OpenGL are off.
(
  cd "$RIVE/renderer"
  RIVE_PREMAKE_ARGS="--with_rive_text --with_rive_layout --no_gl" \
    "$RIVE/build/build_rive.sh" ninja release universal -- $RIVE_TARGETS
)
LIBS="$RIVE/renderer/out/universal_release"

# Reuse the -D flags Rive was compiled with.
DEFINES=$(grep -m1 '^CXXFLAGS' "$LIBS/build_rive.ninja" | tr ' ' '\n' | grep '^-D' | tr '\n' ' ')

FLAGS="-arch arm64 -arch x86_64 -mmacosx-version-min=$MIN_MACOS -O2 -fvisibility=hidden -I$ROOT/native/include"
BUILD="$WORK/engine-native-osx"
mkdir -p "$BUILD"

clang $FLAGS -std=c11 -I"$ROOT/native/third_party" -c "$ROOT/native/src/font.c" -o "$BUILD/font.o"
for src in rive_common.cpp rive_metal.mm; do
  # shellcheck disable=SC2086
  clang++ $FLAGS -std=c++17 -fobjc-arc -fno-exceptions -fno-rtti $DEFINES \
    -I"$RIVE/include" -I"$RIVE/renderer/include" \
    -c "$ROOT/native/src/$src" -o "$BUILD/${src%.*}.o"
done

# Rive's archives are LTO bitcode, so clang does the final optimization here.
clang++ $FLAGS -dynamiclib -flto=full -dead_strip \
  -install_name @rpath/libEngineNative.dylib \
  "$BUILD/font.o" "$BUILD/rive_common.o" "$BUILD/rive_metal.o" \
  $(for lib in $RIVE_LIBS; do echo "$LIBS/$lib"; done) \
  -framework Metal -framework QuartzCore -framework CoreVideo -framework IOSurface \
  -framework Foundation -framework CoreGraphics -framework CoreText -framework ImageIO \
  -o "$OUT/libEngineNative.dylib"

echo "$RIVE_COMMIT" > "$OUT/RIVE_VERSION"
echo "Built $OUT/libEngineNative.dylib"
lipo -info "$OUT/libEngineNative.dylib"

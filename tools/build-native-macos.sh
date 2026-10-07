#!/bin/bash
# Builds libEngineNative.dylib (fonts + Rive on Metal) for macOS, universal (arm64 + x86_64).
#
# Rive's runtime and renderer are built from a pinned rive-runtime commit with Rive's own build
# script (premake + ninja), in a cache directory outside the repo. The bridge in native/ is then
# compiled with the same feature defines (they change class layouts) and linked into one dylib.
# Output: artifacts/native/osx/libEngineNative.dylib
set -euo pipefail

ROOT=$(cd "$(dirname "$0")/.." && pwd)
RIVE_COMMIT=${RIVE_COMMIT:-b2c28dde1f8c51872a81ae4f9b4fbbceac1d23ed}
WORK=${NATIVE_WORK:-${XDG_CACHE_HOME:-$HOME/.cache}/csharp2026}
RIVE="$WORK/rive-runtime-$RIVE_COMMIT"
OUT="$ROOT/artifacts/native/osx"
MIN_MACOS=12.0   # matches LSMinimumSystemVersion in package-macos.sh

mkdir -p "$WORK" "$OUT"

if [ ! -d "$RIVE" ]; then
  # Blobless and sparse: the tests and Skia trees are large and not needed.
  git clone --filter=blob:none --no-checkout https://github.com/rive-app/rive-runtime.git "$RIVE"
  git -C "$RIVE" sparse-checkout set --no-cone '/*' '!/tests/' '!/skia/' '!/cg_renderer/' '!/dev/' '!/rivinfo/'
  git -C "$RIVE" checkout "$RIVE_COMMIT"
fi

# Text (HarfBuzz/SheenBidi) and layout (Yoga) are on so files that use them render; the scripted
# canvas and OpenGL are off.
(
  cd "$RIVE/renderer"
  RIVE_PREMAKE_ARGS="--with_rive_text --with_rive_layout --no_gl" \
    "$RIVE/build/build_rive.sh" ninja release universal \
    -- rive rive_pls_renderer rive_decoders rive_harfbuzz rive_sheenbidi rive_yoga libpng libjpeg libwebp zlib
)
LIBS="$RIVE/renderer/out/universal_release"

# Reuse the -D flags Rive was compiled with.
DEFINES=$(grep -m1 '^CXXFLAGS' "$LIBS/build_rive.ninja" | tr ' ' '\n' | grep '^-D' | tr '\n' ' ')

FLAGS="-arch arm64 -arch x86_64 -mmacosx-version-min=$MIN_MACOS -O2 -fvisibility=hidden -I$ROOT/native/include"
BUILD="$WORK/engine-native-build"
mkdir -p "$BUILD"

clang $FLAGS -std=c11 -I"$ROOT/native/third_party" -c "$ROOT/native/src/font.c" -o "$BUILD/font.o"
# shellcheck disable=SC2086
clang++ $FLAGS -std=c++17 -fobjc-arc -fno-exceptions -fno-rtti $DEFINES \
  -I"$RIVE/include" -I"$RIVE/renderer/include" \
  -c "$ROOT/native/src/rive_metal.mm" -o "$BUILD/rive_metal.o"

# Rive's archives are LTO bitcode, so clang does the final optimization here.
clang++ $FLAGS -dynamiclib -flto=full -dead_strip \
  -install_name @rpath/libEngineNative.dylib \
  "$BUILD/font.o" "$BUILD/rive_metal.o" \
  "$LIBS/librive_pls_renderer.a" "$LIBS/librive_decoders.a" "$LIBS/librive.a" \
  "$LIBS/librive_harfbuzz.a" "$LIBS/librive_sheenbidi.a" "$LIBS/librive_yoga.a" \
  "$LIBS/liblibpng.a" "$LIBS/liblibjpeg.a" "$LIBS/liblibwebp.a" "$LIBS/libzlib.a" \
  -framework Metal -framework QuartzCore -framework CoreVideo -framework IOSurface \
  -framework Foundation -framework CoreGraphics -framework CoreText -framework ImageIO \
  -o "$OUT/libEngineNative.dylib"

echo "$RIVE_COMMIT" > "$OUT/RIVE_VERSION"
echo "Built $OUT/libEngineNative.dylib"
lipo -info "$OUT/libEngineNative.dylib"

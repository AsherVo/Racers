#!/bin/bash
# Sourced by the build-native-* scripts: checks out the pinned rive-runtime commit into the cache
# and sets RIVE to its path. Blobless and sparse: the tests and Skia trees are large and unneeded.
RIVE_COMMIT=${RIVE_COMMIT:-b2c28dde1f8c51872a81ae4f9b4fbbceac1d23ed}
WORK=${NATIVE_WORK:-${XDG_CACHE_HOME:-$HOME/.cache}/csharp2026}
RIVE="$WORK/rive-runtime-$RIVE_COMMIT"

mkdir -p "$WORK"
if [ ! -d "$RIVE" ]; then
  git clone --filter=blob:none --no-checkout https://github.com/rive-app/rive-runtime.git "$RIVE"
  git -C "$RIVE" sparse-checkout set --no-cone '/*' '!/tests/' '!/skia/' '!/cg_renderer/' '!/dev/' '!/rivinfo/'
  git -C "$RIVE" checkout "$RIVE_COMMIT"
fi

# Rive's libraries plus the image decoders and compression they link against.
RIVE_TARGETS="rive rive_pls_renderer rive_decoders rive_harfbuzz rive_sheenbidi rive_yoga libpng libjpeg libwebp zlib"
RIVE_LIBS="librive_pls_renderer.a librive_decoders.a librive.a librive_harfbuzz.a librive_sheenbidi.a librive_yoga.a liblibpng.a liblibjpeg.a liblibwebp.a libzlib.a"

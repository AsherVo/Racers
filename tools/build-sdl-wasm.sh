#!/bin/sh
# Builds SDL3 as a static WebAssembly library for the browser host.
#
# The library must be compiled with the same Emscripten version the .NET wasm-tools workload
# links with, so this installs that exact emsdk into a cache directory outside the repo (it's
# ~1.5 GB, which shouldn't live in a synced folder).
# Output: artifacts/sdl-wasm/SDL3.a (the file name must match the [DllImport("SDL3")] name).
set -eu

ROOT=$(cd "$(dirname "$0")/.." && pwd)
EMSDK_VERSION=${EMSDK_VERSION:-3.1.56}   # .NET 10's Emscripten version
SDL_VERSION=${SDL_VERSION:-3.4.18}       # keep in sync with SDL3CSVersion in Directory.Packages.props
WORK=${SDL_WASM_WORK:-${XDG_CACHE_HOME:-$HOME/.cache}/csharp2026/sdl-wasm}
OUT="$ROOT/artifacts/sdl-wasm"

mkdir -p "$WORK" "$OUT"
cd "$WORK"

if [ ! -d emsdk ]; then
  git clone --depth 1 https://github.com/emscripten-core/emsdk.git
fi
./emsdk/emsdk install "$EMSDK_VERSION"
./emsdk/emsdk activate "$EMSDK_VERSION" >/dev/null
# shellcheck disable=SC1091
EMSDK_QUIET=1 . ./emsdk/emsdk_env.sh

if [ ! -d "SDL3-$SDL_VERSION" ]; then
  curl -fsSL "https://github.com/libsdl-org/SDL/releases/download/release-$SDL_VERSION/SDL3-$SDL_VERSION.tar.gz" | tar xz
fi

BUILD="$WORK/build-$SDL_VERSION-em$EMSDK_VERSION"
emcmake cmake -S "SDL3-$SDL_VERSION" -B "$BUILD" -G Ninja \
  -DCMAKE_BUILD_TYPE=Release \
  -DSDL_SHARED=OFF -DSDL_STATIC=ON \
  -DSDL_TEST_LIBRARY=OFF -DSDL_TESTS=OFF -DSDL_EXAMPLES=OFF \
  -DSDL_PTHREADS=OFF
cmake --build "$BUILD"

cp "$BUILD/libSDL3.a" "$OUT/SDL3.a"
echo "$SDL_VERSION / emsdk $EMSDK_VERSION" > "$OUT/VERSION"
echo "Built $OUT/SDL3.a"

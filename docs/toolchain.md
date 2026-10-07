## Building

Prerequisites: .NET 10 SDK; `sudo dotnet workload install wasm-tools ios android`; Xcode (Apple targets).

**Desktop** (verified on macOS arm64 and x64):
```sh
dotnet run --project hosts/Desktop
dotnet publish hosts/Desktop -c Release -r osx-arm64     # or win-x64, linux-x64 (on that OS)
tools/package-macos.sh                                    # universal, ad-hoc signed Racers.app
SIGN_IDENTITY="Developer ID Application: …" tools/package-macos.sh   # hardened runtime, for notarization
```

**Fonts and Rive** need EngineNative, built so far for macOS (universal, verified with `dotnet run`
and the NativeAOT `.app`) and the browser (verified in Chromium on SwiftShader and on the GPU).
iOS and Android still build, but `LoadFont`/`LoadRive` fail there:
```sh
tools/build-native-macos.sh   # once: pinned rive-runtime + premake/ninja → artifacts/native/osx/
tools/build-native-wasm.sh    # once, after build-sdl-wasm.sh → artifacts/native/wasm/
```

**Browser** (verified in headless Chromium):
```sh
tools/build-sdl-wasm.sh                 # once: emsdk 3.1.56 + SDL → artifacts/sdl-wasm/SDL3.a
tools/build-native-wasm.sh              # once: fonts + Rive → artifacts/native/wasm/
dotnet publish hosts/Browser -c Release -o artifacts/browser
python3 -m http.server -d artifacts/browser/wwwroot 8000
node tools/browser-smoke.mjs "/Applications/Chromium.app/Contents/MacOS/Chromium" http://127.0.0.1:8000/ /tmp
```

**iOS** (verified in the iOS 26.3 simulator, Debug/Mono and Release/NativeAOT):
```sh
dotnet build hosts/iOS -r iossimulator-arm64                               # Debug, Mono
dotnet build hosts/iOS -c Release -r iossimulator-arm64 -p:_IsPublishing=true   # NativeAOT
dotnet publish hosts/iOS -c Release -r ios-arm64                           # device (needs signing)
```

**Android** (verified on an Android 16 arm64 emulator). Needs JDK 17+ and the Android SDK. This
machine has them at the paths below, installed with .NET's `InstallAndroidDependencies` target:
```sh
export JAVA_HOME=~/Library/Java/jdk-17 ANDROID_HOME=~/Library/Android/sdk
dotnet build hosts/Android -t:Run -p:AndroidSdkDirectory=$ANDROID_HOME -p:JavaSdkDirectory=$JAVA_HOME
~/Library/Android/sdk/emulator/emulator -avd csharp2026      # emulator created for testing
```
Debug builds use Fast Deployment, so install with `-t:Install` or `-t:Run`, not `adb install`.

**Debug capture:** set `ENGINE_CAPTURE=/path/frame.png` and the engine saves frame 90 as a PNG on any platform.

## Build gotchas

These cost real time. Each one is handled in the project files, build scripts or code, with a
comment at the fix. Gotchas about how
the engine works at runtime are in [docs/architecture.md](docs/architecture.md).

1. **iOS static linking needs three fixes:**
   - The import name `"SDL3"` is resolved to the main program (`SDL.Resolver.cs`).
   - The engine's SDL functions are kept through `-dead_strip` with `ReferenceNativeSymbol`
     items generated from `SDL.cs` (`build/StaticSdl.targets`).
   - CoreBluetooth has to be linked; SDL3-CS.iOS's targets omit it.
2. **The iOS host must reference an iOS API.** Otherwise the trimmer drops `Microsoft.iOS.dll`
   and the .NET iOS runtime aborts at startup.
3. **The workload's iOS SDK pack must match Xcode.** The iOS workload installed 27.0, which
   needs Xcode 27. The host pins `net10.0-ios26.0` and sets `ValidateXcodeVersion=false` for
   Xcode 26.3. Remove both once Xcode and the workload agree.
4. **SDL for wasm must match .NET's Emscripten version** (3.1.56 for .NET 10).
   `tools/build-sdl-wasm.sh` pins it and caches emsdk outside the repo (~1.5 GB).
5. **SDL3-CS.Android packages `libSDL3.so` twice** (in `runtimes/` and inside its `.aar`),
   which causes warning XA4301. It's harmless.
6. **macOS hardened runtime rejects ad-hoc signed dylibs** (library validation). The packaging
   script enables hardened runtime only for real identities.
7. **Rive's archives are LTO bitcode and its feature defines change class layouts.** The bridge is
   compiled with the exact `-D` flags Rive was built with, and linked with `-flto` (plus ImageIO,
   which Rive uses to decode images on Apple platforms).
8. **`EmccExtraLDFlags` is a property, not an item.** Declared as an item it's silently ignored,
   which left the browser build on WebGL 1 (`-sMAX_WEBGL_VERSION=2` never reached the linker).
9. **Rive's wasm libraries must not be LTO bitcode.** .NET links with its own LLVM 19.1, which
   lowers `setjmp` (libpng, libjpeg) to `__wasm_setjmp`, but its Emscripten 3.1.56 runtime only has
   the older `saveSetjmp`. `build-native-wasm.sh` compiles Rive with emsdk 3.1.56, `--no-lto`,
   `-fwasm-exceptions -sSUPPORT_LONGJMP=wasm`.
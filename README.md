# CSharp2026

A prototype 2D game engine in C# on **SDL 3.4** and **.NET 10**, targeting Windows, macOS (no Catalyst),
iOS, Android and the browser from one codebase. Rendering uses SDL_Renderer ("Option A"), so the same
2D path runs everywhere, including WebGL.

## Layout

```
src/Engine/            Platform-agnostic engine (net10.0, AOT-compatible)
  Native/SDL.cs          Hand-written SDL binding: the ~30 functions the engine uses
  Native/SDL.Resolver.cs Points "SDL3" at the main program on iOS (static link)
  build/StaticSdl.targets  Keeps those SDL symbols through the iOS native link
  GameRunner.cs          Init / Iterate / HandleEvent / Frame: the per-frame core
  GameHost.cs            SDL_RunApp → SDL_EnterAppMainCallbacks (desktop, iOS, Android)
  Graphics/SpriteBatch.cs  Batched quads → SDL_RenderGeometry (sprites, text and Rive, in any order)
  Graphics/Font.cs       TrueType → glyph atlas (stb_truetype in EngineNative)
  Graphics/Rive.cs       Rive files/instances; each instance renders into its own texture
src/HelloSprite/       Sample game: Rive background, cars, text, a translucent Rive inset; click to add cars
native/                EngineNative: flat C API over stb_truetype and the Rive runtime (Metal)
hosts/Desktop/         Windows/macOS/Linux: plain net10.0 exe, NativeAOT
hosts/Browser/         .NET WebAssembly + SDL3 compiled with Emscripten
hosts/iOS/             net10.0-ios, SDL linked statically; NativeAOT in Release
hosts/Android/         net10.0-android, SDL's Java SDLActivity calls MainActivity.Main()
tools/                 macOS bundling, SDL wasm build, headless browser smoke test
```

Games subclass `Engine.Game` and never see SDL or the platform. Hosts are a few lines each.

## How each platform runs the loop

| Platform | Entry | Who drives frames |
|---|---|---|
| Desktop | `Program.Main` → `GameHost.Run` | SDL's generic callback loop (blocks until quit) |
| iOS | `Main.cs` → `GameHost.Run` → `SDL_RunApp` → `UIApplicationMain` | SDL's `CADisplayLink` (`SDL_EnterAppMainCallbacks` returns immediately) |
| Android | `SDLActivity` thread → `MainActivity.Main()` → `GameHost.Run` | SDL's generic callback loop |
| Browser | `main.js` → `[JSExport] Init()` | `requestAnimationFrame` → `[JSExport] Frame()` |

The callbacks are `[UnmanagedCallersOnly]` static methods, so no delegates are kept alive and
everything works under NativeAOT and Mono AOT.

## Building

Prerequisites: .NET 10 SDK; `sudo dotnet workload install wasm-tools ios android`; Xcode (Apple targets).

**Desktop** (verified on macOS arm64 and x64):
```sh
dotnet run --project hosts/Desktop
dotnet publish hosts/Desktop -c Release -r osx-arm64     # or win-x64, linux-x64 (on that OS)
tools/package-macos.sh                                    # universal, ad-hoc signed HelloSprite.app
SIGN_IDENTITY="Developer ID Application: …" tools/package-macos.sh   # hardened runtime, for notarization
```

**Fonts and Rive** need `libEngineNative`, built so far for macOS only (universal, verified with
`dotnet run` and the NativeAOT `.app`). Other hosts still build, but `LoadFont`/`LoadRive` fail there:
```sh
tools/build-native-macos.sh   # once: pinned rive-runtime + premake/ninja → artifacts/native/osx/
```

**Browser** (verified in headless Chromium):
```sh
tools/build-sdl-wasm.sh                 # once: emsdk 3.1.56 + SDL → artifacts/sdl-wasm/SDL3.a
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

## Findings and gotchas

These cost real time. Each one is handled in the code, with a comment at the fix.

1. **SDL3-CS's managed bindings don't work in the browser.** They route every SDL call through
   one of ~1,250+ static delegate fields. Mono's wasm AOT compiler crashes on that assembly
   (`sgen-alloc.c:409`). Leaving it interpreted then fails with a mixed-mode entry-wrapper
   assertion (`interp.c:3420`). The engine therefore has its own small `[LibraryImport]`
   binding, and uses SDL3-CS only for its *native* packages (iOS static libs, desktop
   dylibs/DLLs, Android Java bridge).
2. **SDL3-CS's `RunMainCallbacks` doesn't work on iOS.** On iOS, `SDL_EnterAppMainCallbacks`
   returns immediately and must be called inside `SDL_RunApp`. `GameHost` does what
   `SDL_main.h` does in C.
3. **Variadic `SDL_Log` can't be P/Invoked portably**, and it breaks on wasm. `Log` calls
   `SDL_GetDefaultLogOutputFunction()` instead.
4. **iOS static linking needs three fixes:**
   - The import name `"SDL3"` is resolved to the main program (`SDL.Resolver.cs`).
   - The engine's SDL functions are kept through `-dead_strip` with `ReferenceNativeSymbol`
     items generated from `SDL.cs` (`build/StaticSdl.targets`).
   - CoreBluetooth has to be linked; SDL3-CS.iOS's targets omit it.
5. **The iOS host must reference an iOS API.** Otherwise the trimmer drops `Microsoft.iOS.dll`
   and the .NET iOS runtime aborts at startup.
6. **The workload's iOS SDK pack must match Xcode.** The iOS workload installed 27.0, which
   needs Xcode 27. The host pins `net10.0-ios26.0` and sets `ValidateXcodeVersion=false` for
   Xcode 26.3. Remove both once Xcode and the workload agree.
7. **Browser content goes through the Emscripten FS.** `WasmFilesToIncludeInFileSystem` is
   ignored by `Microsoft.NET.Sdk.WebAssembly`. Content is served as static files with a
   generated manifest, and `main.js` copies the files into `/Content` before `Init()`.
8. **SDL for wasm must match .NET's Emscripten version** (3.1.56 for .NET 10).
   `tools/build-sdl-wasm.sh` pins it and caches emsdk outside the repo (~1.5 GB).
9. **Mobile orientation and fullscreen come from SDL, not the manifest or Info.plist.** A
   resizable SDL window asks for "any" orientation, and Android 15+ ignores fullscreen themes.
   The engine sets `SDL_HINT_ORIENTATIONS` from `GameOptions.Orientation` and creates
   fullscreen windows on mobile.
10. **SDL3-CS.Android packages `libSDL3.so` twice** (in `runtimes/` and inside its `.aar`),
   which causes warning XA4301. It's harmless.
11. **macOS hardened runtime rejects ad-hoc signed dylibs** (library validation). The packaging
   script enables hardened runtime only for real identities.

12. **Rive renders into textures SDL samples without a copy.** Each instance's target is an
   IOSurface-backed `CVPixelBuffer`; Rive draws into it through Metal and SDL's Metal renderer wraps
   it (`SDL_PROP_TEXTURE_CREATE_METAL_PIXELBUFFER_POINTER`). SDL doesn't expose its command queue,
   so Rive uses its own queue: the engine waits for Rive's GPU work each frame before drawing, and
   each instance rotates three textures so it never overwrites one SDL may still be sampling.
13. **Rive output is premultiplied.** Those textures use `SDL_BLENDMODE_BLEND_PREMULTIPLIED` and
   `SpriteBatch` premultiplies the tint, so straight-alpha sprites and text interleave with Rive freely.
14. **Rive's archives are LTO bitcode and its feature defines change class layouts.** The bridge is
   compiled with the exact `-D` flags Rive was built with, and linked with `-flto` (plus ImageIO,
   which Rive uses to decode images on Apple platforms).

## Sizes (Release)

| Target | Size |
|---|---|
| macOS arm64 executable (NativeAOT) | 1.0 MB + libSDL3.dylib 2.9 MB |
| libEngineNative.dylib (Rive + fonts, universal) | 9.9 MB (~5 MB per architecture) |
| Browser download (brotli) | 1.9 MB |
| iOS simulator app (NativeAOT) | 8.2 MB |

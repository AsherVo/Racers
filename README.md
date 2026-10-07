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
native/                EngineNative: flat C API over stb_truetype and the Rive runtime (Metal, WebGL 2)
hosts/Desktop/         Windows/macOS/Linux: plain net10.0 exe, NativeAOT
hosts/Browser/         .NET WebAssembly + SDL3 compiled with Emscripten
hosts/iOS/             net10.0-ios, SDL linked statically; NativeAOT in Release
hosts/Android/         net10.0-android, SDL's Java SDLActivity calls MainActivity.Main()
tools/                 Native builds (SDL wasm, EngineNative), macOS bundling, browser smoke test
docs/                  architecture.md: how the engine works and why
```

Games subclass `Engine.Game` and never see SDL or the platform. Hosts are a few lines each.
[docs/architecture.md](docs/architecture.md) explains how it all fits together: the frame loop on
each platform, the SDL binding, content loading, `SpriteBatch`, text, and how Rive renders into SDL
textures on Metal and WebGL.

## Building

More detailed toolchain notes and debug tools are in [docs/toolchain.md](docs/toolchain.md)

**Desktop** (verified on macOS arm64 and x64):
```sh
dotnet run --project hosts/Desktop
```

**Browser** (verified in headless Chromium):
```sh
dotnet publish hosts/Browser -c Release -o artifacts/browser
python3 -m http.server -d artifacts/browser/wwwroot 8000
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
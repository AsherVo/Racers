# Architecture

A 2D engine in C# on SDL 3.4 and .NET 10 that runs one codebase on Windows, macOS, iOS, Android
and the browser. This document explains how the pieces fit and why they're shaped the way they
are. Building and toolchain issues are in the [README](../README.md).

## Layers

```
 Game (Racers)               subclasses Engine.Game; never sees SDL or the platform
 ───────────────────────────────────────────────────────────────────────────────────
 Engine (src/Engine)         GameRunner · Graphics · SpriteBatch · ContentManager
                             Texture · Font · RiveFile / RiveInstance / RiveRuntime
 ───────────────────────────────────────────────────────────────────────────────────
 Bindings (Engine/Native)    SDL.cs            EngineNative.cs
                             ([LibraryImport]: blittable, no delegates, AOT-safe)
 ───────────────────────────────────────────────────────────────────────────────────
 Native                      SDL3              EngineNative (native/)
                                               stb_truetype · Rive runtime + renderer
 ───────────────────────────────────────────────────────────────────────────────────
 Platform                    SDL_Renderer: Metal (Apple) · D3D (Windows) · GL/GLES/WebGL 2
```

Hosts (`hosts/*`) are a few lines each. They hand a `Game` factory and `GameOptions` to the
engine and get out of the way.

Four rules shape everything else:

- **Games only see the engine.** SDL types, handles and constants stay `internal` to `Engine`.
- **One 2D path everywhere.** Rendering goes through SDL_Renderer, which has a backend on every
  target, including WebGL. Nothing is written against Metal, D3D or GL directly, except the Rive
  bridge, which has to be (see [Rive](#rive)).
- **Interop must survive every AOT.** Desktop and iOS use NativeAOT and the browser uses Mono's wasm
  AOT. So all native calls are `[LibraryImport]` with blittable signatures, and callbacks from native
  code are static `[UnmanagedCallersOnly]` methods. There are no delegates to keep alive and no
  reflection.
- **Native code sits behind a flat C API.** C++ libraries (Rive) are wrapped in `extern "C"`
  functions over opaque handles (`native/include/engine_native.h`), so C# binds them the same way it
  binds SDL.

## Hosts and the frame loop

`GameRunner` is the platform-agnostic core: `Init`, then `Iterate` once per frame and
`HandleEvent` per SDL event. `Frame` polls events and iterates, for loops that SDL doesn't drive.
Who calls these depends on the platform:

| Platform | Entry | Who drives frames |
|---|---|---|
| Desktop | `Program.Main` → `GameHost.Run` | SDL's generic callback loop (blocks until quit) |
| iOS | `Main.cs` → `GameHost.Run` → `SDL_RunApp` → `UIApplicationMain` | SDL's `CADisplayLink` (`SDL_EnterAppMainCallbacks` returns immediately) |
| Android | `SDLActivity` thread → `MainActivity.Main()` → `GameHost.Run` | SDL's generic callback loop |
| Browser | `main.js` → `[JSExport] Init()` | `requestAnimationFrame` → `[JSExport] Frame()` |

`GameHost.Run` does what `SDL_main.h` does for C apps: `SDL_RunApp` → `main` →
`SDL_EnterAppMainCallbacks`. SDL3-CS's `RunMainCallbacks` skips the `SDL_RunApp` step, and that
breaks iOS, where `SDL_EnterAppMainCallbacks` returns immediately and must run inside `SDL_RunApp`.
The four callbacks are static `[UnmanagedCallersOnly]` methods. Each one catches exceptions, which
must never unwind through SDL's native frames.

The browser can't block, so `main.js` copies content into the file system (see [Content](#content)),
calls `Init()`, then calls `Frame()` from `requestAnimationFrame`.

### One frame

`GameRunner.Iterate` runs, in order:

1. Measures `dt` and clamps it to 0.1 s, so a debugger break or a backgrounded tab doesn't produce a
   huge step.
2. Calls `Game.Update(time)`.
3. Calls `Graphics.UpdateRive(dt)`: advances every Rive instance and re-renders the ones that
   changed. It runs after `Update`, so inputs the game just set take effect this frame.
4. Clears, then `SpriteBatch.Begin` → `Game.Draw(batch)` → `SpriteBatch.End`.
5. Saves a PNG at frame 90 if `ENGINE_CAPTURE` is set (an automated check that works on any platform).
6. Presents.

When the app enters the background (`SDL_EVENT_WILL_ENTER_BACKGROUND`) the runner stops updating
and drawing. On return it resets the clock so no time is skipped.

### Window, resolution and input

Each game has a `config.yaml` in its content folder (`GameOptions.ConfigPath`), mapped to `GameConfig`. The
runner reads it right after `SDL_Init`, before the window exists:

```yaml
title: Resource Racers
resolutionX: 400        # game pixels; (0, 0) is the bottom-left, (resolutionX, resolutionY) the top-right
resolutionY: 225
pixelPerfect: false     # true: scale only by whole numbers (1x, 2x, ...)
dynamicSize: false      # true: the resolution follows the window
dynamicPixelScale: 4    # with dynamicSize: window points per game pixel
```

`ViewFit` turns the config and the window's size into the game's resolution (`Graphics.Width/Height`) and an
SDL logical presentation mode:

| | Fixed size | `dynamicSize` |
|---|---|---|
| Resolution | `resolutionX × resolutionY` | window points ÷ `dynamicPixelScale` |
| `pixelPerfect: false` | `LETTERBOX`: as large as fits | `LETTERBOX` (bars under one game pixel) |
| `pixelPerfect: true` | `INTEGER_SCALE`: whole multiples only | `INTEGER_SCALE`, pixel size rounded to whole output pixels |
| Desktop window opens at | the largest whole multiple that fits 90% of the screen | resolution × `dynamicPixelScale`, shrunk to fit |

SDL fills the letterbox with the clear color, so each frame clears the whole window black and then fills only
the game's area with `GameOptions.ClearColor`. On `SDL_EVENT_WINDOW_PIXEL_SIZE_CHANGED` the runner refits and,
if the resolution or `Graphics.PixelScale` (output pixels per game pixel) changed, calls `Game.OnResize`.
`PixelScale` is the density to bake fonts and render Rive at for sharp output.

Windows are resizable and high-DPI. On mobile they're fullscreen, and in the browser they fill the
page. Alt+Enter (Option+Return) toggles borderless fullscreen on desktop.

Mobile orientation and fullscreen come from SDL, not from the Android manifest or iOS Info.plist. A
resizable SDL window asks for "any" orientation, and Android 15+ ignores fullscreen themes. So the
runner sets `SDL_HINT_ORIENTATIONS` from `GameOptions.Orientation` before `SDL_Init`, and creates
fullscreen windows on mobile.

Pointer events are converted to game pixels (`SDL_ConvertEventToRenderCoordinates`, then y flipped
to point up) before they reach `Game.OnPointer`. SDL synthesizes mouse
events from touch, so one path covers both. Key events reach `Game.OnKey` as physical keys (`Key`
values are SDL scancodes), with auto-repeat filtered out.

## Native interop

### SDL binding

`Native/SDL.cs` is a hand-written binding for just the SDL functions the engine uses. The engine uses
the SDL3-CS NuGet packages only for their *native* binaries: desktop dylibs/DLLs, iOS static
libraries and Android's Java bridge. It doesn't use their managed bindings, because those don't work
in the browser:
- They route every SDL call through one of ~1,250+ static delegate fields.
- Mono's wasm AOT compiler crashes on that assembly (`sgen-alloc.c:409`).
- Leaving it interpreted instead fails with a mixed-mode entry-wrapper assertion (`interp.c:3420`).

The `"SDL3"` import name resolves differently per platform:
- **Desktop:** normal library probing finds `libSDL3.dylib`, `SDL3.dll` or `libSDL3.so` next to the
  executable.
- **Browser:** SDL is linked statically into `dotnet.native.wasm`. Referencing `SDL3.a` makes the
  .NET build generate P/Invoke stubs for that name.
- **iOS:** SDL is linked statically into the app. A module initializer in `SDL.Resolver.cs` points
  `"SDL3"` at the main program.

`Log` writes through `SDL_GetDefaultLogOutputFunction()`, so messages reach logcat, os_log or stderr.
Variadic `SDL_Log` can't be P/Invoked portably and breaks on wasm. In the browser, `Log` uses
`Console`.

### EngineNative

`libEngineNative` holds everything native that isn't SDL: font rasterization (stb_truetype) and Rive.
Its C API is `native/include/engine_native.h`, and `Native/EngineNative.cs` binds it. It's built per
platform by `tools/build-native-*.sh`:

| Platform | Artifact | Rive backend |
|---|---|---|
| macOS | `artifacts/native/osx/libEngineNative.dylib` (universal), copied next to the executable | Metal |
| Browser | `artifacts/native/wasm/EngineNative.a` + Rive's `.a` files, linked into `dotnet.native.wasm` | WebGL 2 |
| iOS, Android, Windows, Linux | not built yet: `LoadFont`/`LoadRive` throw | none |

Like `SDL3.a`, the archive name `EngineNative.a` is what makes the browser build generate stubs for
the `"EngineNative"` imports.

## Content

`ContentManager` loads everything through SDL's file I/O (`SDL_LoadPNG`, `SDL_LoadFile`). SDL
already knows each platform's storage, so the root just needs to point at the right place:

| Platform | Root |
|---|---|
| Desktop, iOS | `SDL_GetBasePath()` + `Content/` (inside a macOS `.app`, that's `Contents/Resources/Content/`) |
| Android | `Content/`, relative, which SDL resolves inside the APK's assets |
| Browser | `/Content/` in the Emscripten file system |

The browser needs one extra step. `Microsoft.NET.Sdk.WebAssembly` ignores
`WasmFilesToIncludeInFileSystem`, so content can't be preloaded into the Emscripten file system the
usual way. Instead, the build serves `Content/` as static files with a generated `manifest.json`,
and `main.js` fetches each file into `/Content` before calling `Init()`.

## Game data (YAML)

Game data is YAML, read and written by `src/Engine/Yaml`. There are two layers:

- **Nodes.** `Yaml.Parse` turns text into `YamlMap` / `YamlList` / `YamlScalar` nodes, and `Yaml.Write` turns them
  back into text. Use nodes directly for arbitrary data.
- **Objects.** `Yaml.Deserialize<T>` / `Yaml.Serialize` map YAML to C# objects with reflection.
  `ContentManager.LoadYaml<T>(path)` loads a file from `Content/` the same way on every platform.

The parser is hand-written YAML 1.2. It covers block and flow collections, every scalar style, anchors and
aliases, tags, directives and multi-document streams. It passes the official
[yaml-test-suite](https://github.com/yaml/yaml-test-suite) except one case: it rejects duplicate keys, even two
empty ones. Comments are skipped, so writing a parsed file drops them. The writer quotes a string only when it
would otherwise read back as something else (`"true"`, `"123"`, `"yes"`), and writes multi-line strings as
literal blocks.

### Mapping objects

Public fields and public get/set properties (including `init`) map to keys of the same name, base class members
first. Classes need a parameterless constructor, which can be private.

| Attribute | Effect |
|---|---|
| `[YamlIgnore]` | Never read or written. `Condition = WhenNull / WhenDefault` only omits it from output. |
| `[YamlMember("key")]` | Renames a member, or includes a non-public one. |
| `[YamlRequired]` | A missing key is an error. Otherwise missing members keep their initial value. |
| `[YamlPolymorphic("type")]` | On a base class or interface: the `type` key chooses a concrete subclass, by class name or `[YamlTypeName]`. Subclasses are found in the base's assembly, plus any listed with `[YamlDerivedType]`. |
| `[YamlConverter(typeof(X))]` | On a type or member: an `IYamlConverter` reads and writes it instead (e.g. a color as `"#FF8800"`). |

Supported member types:
- `string`, `char`, `bool`, all integer types, `float`, `double`, `decimal`.
- Enums by name. `[Flags]` enums as `A, B` or `[A, B]`.
- `Nullable<T>`.
- `T[]`, `List<T>`, `HashSet<T>`, `Dictionary<TKey, TValue>`.
- Nested classes and structs.
- `object` (arbitrary data as `bool`/`long`/`double`/`string`/`List<object?>`/`Dictionary<string, object?>`).
- `YamlNode` (raw YAML).

Interface collections (`IReadOnlyList<T>`) aren't supported, because creating one needs runtime code generation.
Unsupported members throw `InvalidOperationException` naming the member.

Reading doesn't stop at the first problem. Every error is collected with its file, line, column and key path, then
thrown together in one `YamlException` (or returned by `TryDeserialize`):

```
items/sword.yaml:12:3: components[0].valeu: Unknown key 'valeu' for Knob0.
items/sword.yaml:18:11: onHit.children[0].type: Unknown BehaviorNode type 'GainSheild'. Expected one of: ...
```

Unknown keys are errors by default, which catches typos. `YamlReadOptions.AllowUnknownKeys` turns that off.

### Reflection under AOT

Trimming and NativeAOT remove members nothing references statically, and reflection is the only thing that
reads data classes' setters. `hosts/Directory.Build.targets` handles this in two steps:

1. **Rooting.** It roots the data assemblies (`YamlDataAssemblies`: `Engine;Racers.Game`), so ILLink and ILC
   keep every type and member in them. A new assembly that holds data types must be added there.
2. **Collections (NativeAOT only).** NativeAOT can't create generic collections such as
   `Dictionary<string, Stats>` through reflection unless their code was generated ahead of time. Rooting doesn't
   cover these, because the collection types live in the framework. Before ILC runs, the hosts build `tools/Cli`
   and run `cli yaml aot-directives`. It lists every `List`, `HashSet` and `Dictionary` that appears in a field
   or property of the data assemblies, as rd.xml directives (`obj/.../yaml.rd.xml`).

Mono targets (Android, and the browser, which falls back to its interpreter) don't need step 2.

## Rendering

### SpriteBatch

`SpriteBatch` is the only drawing API games see. Every draw is a textured quad: a sprite, a glyph,
or a Rive artboard. Quads are built on the CPU (position, rotation, origin, scale, source rectangle,
tint) into a vertex array and submitted with `SDL_RenderGeometry`. The batch flushes when the
texture changes, or when it reaches 4,096 quads. `DrawCalls` reports how many submits the last frame
made.

Positions are in game pixels with y up, and rotations are counter-clockwise. SDL's y points down, so
`SpriteBatch` flips each vertex as it writes it. Images stay upright, and sources and origins stay in texture
pixels from the image's top-left. A plain position or a destination rectangle places an image's bottom-left
corner; `DrawString` places the first line's top-left, and further lines go below it.

Two consequences:

- **Draw order is call order.** Sprites, text and Rive layer exactly as the game issues them, in any
  interleaving.
- **Cost depends on texture changes, not quad count.** Alternating two textures every quad makes one
  draw call per quad. Grouping draws by texture (for example, drawing all name tags after all cars)
  collapses them. The sample went from 39 draw calls to 6 that way.

Only `Flush` touches SDL_Renderer, so a different backend could replace it without changing games.

### Textures and alpha

Each `Texture` has its own blend mode, which SDL applies per draw call. That's what lets
straight-alpha and premultiplied textures sit in one frame:

| Source | Alpha | SDL blend mode |
|---|---|---|
| PNG (`LoadTexture`), font atlases | straight | `SDL_BLENDMODE_BLEND` |
| Rive output | premultiplied | `SDL_BLENDMODE_BLEND_PREMULTIPLIED` |

For premultiplied textures, `SpriteBatch` premultiplies the tint as well (`rgb × a, a`), so
`Color.WithAlpha(0.5f)` fades a Rive artboard the same way it fades a sprite. `TextureFilter.PixelArt`
selects SDL's pixel-art scaling for crisp upscaled sprites. `Linear` is the default.

### Text

`ContentManager.LoadFont(path, size, density = 2)` bakes a TrueType font into a glyph atlas:

- stb_truetype (`native/src/font.c`) rasterizes printable ASCII at `size × density` pixels into an
  8-bit coverage atlas. The atlas starts at 256² and doubles until the glyphs fit. It also returns
  each glyph's metrics and a kerning table (from the font's `kern` table only).
- The atlas becomes a texture of white texels with coverage as alpha, so the draw color tints the
  text.
- `SpriteBatch.DrawString` lays out glyphs with advance and kerning, handles `\n`, and draws each
  glyph as a quad scaled by `1 / density`. Baking at `Graphics.PixelScale` keeps text sharp at the
  window's scale.

Text is ordinary quads, so it batches with itself and layers with everything else.
`Font.MeasureString` gives the layout box. Characters outside ASCII draw as `?`.

## Rive

### Model

```
ContentManager.LoadRive ──► RiveFile ──CreateInstance(artboard, stateMachine, resolution)──► RiveInstance
                                │                                                               │
                                └──────────────── RiveRuntime (one per Graphics) ───────────────┘
                                   owns the native context; advances + renders every instance
```

- **`RiveFile`** is a loaded `.riv` file. Instances keep a reference to the native file, so a
  `RiveFile` can be disposed while its instances are still alive.
- **`RiveInstance`** is one artboard plus its state machine. If no state machine is named, it uses
  the default one, else the first, else the first linear animation. If the artboard has a default
  view model, it's bound. The instance exposes:
  - inputs (`SetNumber`, `SetBool`, `Fire`)
  - text runs (`SetText`)
  - pointer events, in artboard units
  - `Paused`
  - `Texture`, its most recent frame

  `resolution` is texture pixels per artboard unit. Choose it for the size the artboard will be
  shown at.
- **`RiveRuntime`** is created on the first `LoadRive`, so games without Rive never load it. Each
  frame it advances every instance and re-renders only the ones whose state machine reports a
  change, plus any marked dirty (newly created, or text changed). A settled artboard costs nothing.
  On shutdown, instances and files are disposed before the context that created their GPU resources.

`SpriteBatch.Draw(RiveInstance, …)` overloads take positions, origins and scales in artboard units,
and convert them to the texture's pixels. Drawing an artboard works the same as drawing a sprite.

### Rendering into SDL textures

Rive has its own GPU renderer, which needs direct access to Metal, GL or another graphics API. SDL
deliberately hides those behind SDL_Renderer. The bridge connects them by having Rive render each
instance into a texture that SDL can sample directly, with no copy. The mechanism depends on the
backend:

| | Metal (macOS) | WebGL 2 (browser) |
|---|---|---|
| Target | `CVPixelBuffer` backed by an IOSurface, created by the bridge | RGBA texture created by SDL |
| SDL side | wraps the pixel buffer: `SDL_PROP_TEXTURE_CREATE_METAL_PIXELBUFFER_POINTER` | owns the texture; its GL name comes from `SDL_PROP_TEXTURE_OPENGLES2_TEXTURE_NUMBER` |
| Rive side | `RenderTargetMetal` on an `MTLTexture` over the same IOSurface | `TextureRenderTargetGL` on that GL name, with top-down rows (`setBottomUp(false)`) |
| GPU work | Rive's own `MTLCommandQueue` on SDL's device (from its `CAMetalLayer`) | SDL's own WebGL context |
| Ordering | `en_rive_finish` waits for Rive's last command buffer before SDL draws | automatic, since it's one context |
| Textures per instance | 3, rotated | 1 |

**Metal.** SDL doesn't expose its command queue, so Rive can't submit work on it. Rive uses its own
queue on the same device instead. Commands on different queues aren't ordered, which causes two
hazards:
- **SDL reading a frame before Rive finishes it.** The engine blocks on Rive's last command buffer
  after each frame's Rive pass. That costs about one Rive render of GPU time.
- **Rive overwriting a texture an in-flight SDL frame is still sampling.** Each instance rotates
  through three textures.

**WebGL.** Rive and SDL share SDL's context, so the GPU orders their work, but they share GL state as
well. `RiveRuntime.BeginRender/EndRender` and `rive_webgl.cpp` hand the context back and forth:

1. `SDL_FlushRenderer` submits SDL's queued draws and drops SDL's cached GL state.
2. Rive's `invalidateGLState()` makes Rive re-read the context before rendering.
3. After rendering, Rive's `unbindGLInternalResources()`, then `restore_sdl_state()` resets the state
   SDL's GLES2 renderer sets once at startup and assumes afterwards:
   - no depth, stencil, cull or scissor
   - pixel-store alignment 1
   - vertex attributes 0 and 1 enabled
   - VAO 0, framebuffer 0, `TEXTURE0` active
   - no sampler objects bound (they would override SDL's filtering)
4. `SDL_FlushRenderer` again, so SDL re-applies everything it tracks per draw.

Rive needs WebGL 2. SDL's GLES2 renderer only keeps a GLES 3.0 request if the window is already an
OpenGL window; otherwise it reconfigures the window for ES 2.0, which Emscripten maps to WebGL 1.
So in the browser, `GameRunner` creates the window with `SDL_WINDOW_OPENGL` and GLES 3.0
attributes. Without the `WEBGL_shader_pixel_local_storage` extension, which most browsers lack,
Rive falls back to 4× MSAA and logs that once.

### Bridge source layout

| File | Contents |
|---|---|
| `native/src/rive_bridge.hpp` | Shared handle types (`EnRiveFile`, `EnRiveInstance`) and the backend hooks |
| `native/src/rive_common.cpp` | Backend-independent: loading files, instances, state machine inputs, text runs, pointer events, drawing an instance |
| `native/src/rive_metal.mm` | Metal context, IOSurface targets, submit and wait |
| `native/src/rive_webgl.cpp` | GL context, SDL-texture targets, GL state handoff |

A new backend (for example Metal on iOS, or GLES/Vulkan on Android) only needs the four context and
target functions, plus `en_rive_backend()` so `RiveRuntime` knows which texture handoff to use.
`tools/fetch-rive.sh` pins the rive-runtime commit, so every platform builds the same runtime.

## Platform support

| | Desktop (macOS) | Desktop (Windows/Linux) | Browser | iOS | Android |
|---|---|---|---|---|---|
| Sprites, input, content | ✓ | ✓ | ✓ | ✓ | ✓ |
| Text (`Font`) | ✓ | needs EngineNative build | ✓ | needs EngineNative build | needs EngineNative build |
| Rive | ✓ Metal | needs a backend (D3D/GL) | ✓ WebGL 2 | needs build (Metal bridge should port) | needs a backend (GLES/Vulkan) |

## Known limits

- **Rive on Metal** waits on the GPU once per frame, because it can't share SDL's command queue.
- **Rive and font resolution** is fixed when the instance or font is created. The sample uses the
  `Graphics.PixelScale` it starts with, so text and Rive soften if the window grows later.
- **YAML** comments are lost when a parsed file is written back. `DateTime` members aren't supported.
- **Fonts** cover printable ASCII only, and kerning comes only from the font's `kern` table (not GPOS).
- **Draw calls:** sprites and glyphs each have their own texture. A shared atlas would let
  interleaved sprites and text batch together.

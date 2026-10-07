using System.Numerics;
using Engine.Native;

namespace Engine;

/// <summary>
/// Platform-agnostic frame driver. Hosts call <see cref="Init"/> once, then either let SDL's main
/// callbacks drive <see cref="Iterate"/>/<see cref="HandleEvent"/> (desktop, iOS, Android), or call
/// <see cref="Frame"/> from an external loop such as the browser's requestAnimationFrame.
/// </summary>
public sealed unsafe class GameRunner(Game game, GameOptions options)
{
    nint _window;
    nint _renderer;
    SpriteBatch _batch = null!;
    ulong _lastTicks;
    double _totalSeconds;
    bool _quit;
    bool _paused;
    int _frameCount;

    // Debug aid: ENGINE_CAPTURE=/path/to/frame.png saves one frame once the scene has settled.
    const int CaptureFrame = 90;
    readonly string? _capturePath = Environment.GetEnvironmentVariable("ENGINE_CAPTURE");

    public bool Init()
    {
        bool mobile = OperatingSystem.IsAndroid() || OperatingSystem.IsIOS() || OperatingSystem.IsTvOS();
        if (mobile)
        {
            SDL.SDL_SetHint(SDL.HINT_ORIENTATIONS, options.Orientation switch
            {
                Orientation.Landscape => "LandscapeLeft LandscapeRight",
                Orientation.Portrait => "Portrait",
                _ => "LandscapeLeft LandscapeRight Portrait PortraitUpsideDown",
            });
        }

        if (!SDL.SDL_Init(SDL.INIT_VIDEO | SDL.INIT_EVENTS))
            return Fail("SDL_Init");

        ulong flags = SDL.WINDOW_RESIZABLE | SDL.WINDOW_HIGH_PIXEL_DENSITY;
        if (OperatingSystem.IsBrowser())
            flags |= SDL.WINDOW_FILL_DOCUMENT;
        else if (mobile)
            flags |= SDL.WINDOW_FULLSCREEN; // hides the status and navigation bars

        if (!SDL.SDL_CreateWindowAndRenderer(options.Title, options.Width, options.Height, flags, out _window, out _renderer))
            return Fail("SDL_CreateWindowAndRenderer");

        // In the browser, requestAnimationFrame already paces frames to the display.
        if (!OperatingSystem.IsBrowser() && !SDL.SDL_SetRenderVSync(_renderer, 1))
            Log.Error($"SDL_SetRenderVSync failed: {SDL.GetError()}");
        SDL.SDL_SetRenderLogicalPresentation(_renderer, options.Width, options.Height, SDL.LOGICAL_PRESENTATION_LETTERBOX);

        Log.Info($"Platform: {SDL.GetPlatform()}, renderer: {SDL.Utf8(SDL.SDL_GetRendererName(_renderer))}");

        game.Graphics = new Graphics(_renderer, options.Width, options.Height);
        game.Content = new ContentManager(_renderer, options.ContentRoot ?? ContentManager.DefaultRoot());
        _batch = new SpriteBatch(game.Graphics);

        game.Load();
        _lastTicks = SDL.SDL_GetTicksNS();
        return true;
    }

    /// <summary>Polls events and runs one frame. Returns false when the game should stop.</summary>
    public bool Frame()
    {
        SDL.Event e;
        while (SDL.SDL_PollEvent(&e))
        {
            if (!HandleEvent(ref e))
                return false;
        }

        return Iterate();
    }

    public bool Iterate()
    {
        ulong now = SDL.SDL_GetTicksNS();
        float dt = (now - _lastTicks) / 1e9f;
        _lastTicks = now;

        if (_paused)
            return !_quit;

        // Clamp so a debugger break or a backgrounded tab doesn't produce a huge step.
        dt = MathF.Min(dt, 0.1f);
        _totalSeconds += dt;
        game.Update(new GameTime(_totalSeconds, dt));

        var c = options.ClearColor;
        SDL.SDL_SetRenderDrawColorFloat(_renderer, c.R, c.G, c.B, c.A);
        SDL.SDL_RenderClear(_renderer);

        _batch.Begin();
        game.Draw(_batch);
        _batch.End();

        if (++_frameCount == CaptureFrame && _capturePath is not null)
            Capture(_capturePath);

        SDL.SDL_RenderPresent(_renderer);
        return !(_quit || game.ExitRequested);
    }

    /// <summary>Returns false when the event means the game should stop.</summary>
    internal bool HandleEvent(ref SDL.Event e)
    {
        switch (e.Type)
        {
            case SDL.EVENT_QUIT:
            case SDL.EVENT_TERMINATING:
                _quit = true;
                return false;

            case SDL.EVENT_WILL_ENTER_BACKGROUND:
                _paused = true;
                break;

            case SDL.EVENT_DID_ENTER_FOREGROUND:
                _paused = false;
                _lastTicks = SDL.SDL_GetTicksNS();
                break;

            // SDL synthesizes mouse events from touch by default, so this covers mobile too.
            case SDL.EVENT_MOUSE_MOTION:
                fixed (SDL.Event* p = &e)
                    SDL.SDL_ConvertEventToRenderCoordinates(_renderer, p);
                game.OnPointer(new PointerEvent(PointerAction.Move, new Vector2(e.Motion.X, e.Motion.Y)));
                break;

            case SDL.EVENT_MOUSE_BUTTON_DOWN:
            case SDL.EVENT_MOUSE_BUTTON_UP:
                fixed (SDL.Event* p = &e)
                    SDL.SDL_ConvertEventToRenderCoordinates(_renderer, p);
                var action = e.Button.Down != 0 ? PointerAction.Down : PointerAction.Up;
                game.OnPointer(new PointerEvent(action, new Vector2(e.Button.X, e.Button.Y)));
                break;

            case SDL.EVENT_KEY_DOWN when IsFullscreenToggle(ref e.Key):
                if (e.Key.Repeat == 0)
                    ToggleFullscreen();
                break;

            case SDL.EVENT_KEY_DOWN:
            case SDL.EVENT_KEY_UP:
                if (e.Key.Repeat == 0)
                    game.OnKey((Key)e.Key.Scancode, e.Key.Down != 0);
                break;
        }

        return !game.ExitRequested;
    }

    static readonly bool Desktop = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() || OperatingSystem.IsLinux();

    /// <summary>Alt+Enter (Option+Return on macOS), desktop only. Mobile is always fullscreen and the browser fills the page.</summary>
    static bool IsFullscreenToggle(ref SDL.KeyboardEvent key) =>
        Desktop
        && (key.Mod & SDL.KMOD_ALT) != 0
        && key.Scancode is SDL.SCANCODE_RETURN or SDL.SCANCODE_KP_ENTER;

    /// <summary>Switches between a window and borderless fullscreen at desktop resolution.</summary>
    public void ToggleFullscreen()
    {
        bool fullscreen = (SDL.SDL_GetWindowFlags(_window) & SDL.WINDOW_FULLSCREEN) != 0;
        if (!SDL.SDL_SetWindowFullscreen(_window, !fullscreen))
            Log.Error($"SDL_SetWindowFullscreen failed: {SDL.GetError()}");
    }

    /// <summary>Saves the current back buffer as a PNG, for automated checks on any platform.</summary>
    public void Capture(string path)
    {
        nint surface = SDL.SDL_RenderReadPixels(_renderer, null);
        if (surface == 0 || !SDL.SDL_SavePNG(surface, path))
            Log.Error($"Capture to '{path}' failed: {SDL.GetError()}");
        else
            Log.Info($"Captured frame {_frameCount} to {path}");

        if (surface != 0)
            SDL.SDL_DestroySurface(surface);
    }

    public void Shutdown()
    {
        game.Unload();
        game.Graphics?.Dispose();
        if (_renderer != 0) SDL.SDL_DestroyRenderer(_renderer);
        if (_window != 0) SDL.SDL_DestroyWindow(_window);
        SDL.SDL_Quit();
    }

    static bool Fail(string what)
    {
        Log.Error($"{what} failed: {SDL.GetError()}");
        return false;
    }
}

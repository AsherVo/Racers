using System.Numerics;
using Engine.Native;

namespace Engine;

/// <summary>A loaded .riv file. Create instances from it to play its artboards.</summary>
public sealed class RiveFile : IDisposable
{
    readonly RiveRuntime _runtime;
    nint _handle;

    internal RiveFile(RiveRuntime runtime, nint handle)
    {
        _runtime = runtime;
        _handle = handle;
    }

    /// <summary>
    /// Instantiates an artboard and starts its state machine. Instances keep what they need from
    /// the file, so the file can be disposed while they're alive.
    /// </summary>
    /// <param name="artboard">Artboard name, or null for the file's default artboard.</param>
    /// <param name="stateMachine">State machine name, or null for the default (else the first
    /// state machine, else the first animation).</param>
    /// <param name="resolution">Texture pixels per artboard unit. Use the display's pixel density
    /// (2 on Retina) for artboards drawn at their natural size; lower it for large, soft content.</param>
    public RiveInstance CreateInstance(string? artboard = null, string? stateMachine = null, float resolution = 1f)
    {
        ObjectDisposedException.ThrowIf(_handle == 0, this);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(resolution);

        nint instance = EngineNative.en_rive_instance_create(_handle, artboard, stateMachine, out float width, out float height);
        if (instance == 0)
            throw new ArgumentException($"No artboard '{artboard ?? "(default)"}' with state machine '{stateMachine ?? "(default)"}'.");

        return _runtime.Add(new RiveInstance(_runtime, instance, width, height, resolution));
    }

    public void Dispose()
    {
        if (_handle == 0)
            return;

        _runtime.Remove(this);
        EngineNative.en_rive_file_destroy(_handle);
        _handle = 0;
    }
}

/// <summary>
/// A playing Rive artboard. The engine advances it every frame and renders it into a texture, so
/// it draws through <see cref="SpriteBatch"/> like a sprite, in any order with sprites and text.
/// </summary>
public sealed class RiveInstance : IDisposable
{
    /// <summary>
    /// Rive renders on its own Metal queue while SDL may still be sampling earlier frames, so each
    /// instance rotates through a few textures instead of overwriting the one on screen.
    /// </summary>
    const int Buffers = 3;

    readonly RiveRuntime _runtime;
    readonly nint[] _targets = new nint[Buffers];
    readonly Texture[] _textures = new Texture[Buffers];
    nint _handle;
    int _current;
    bool _dirty = true;

    /// <summary>Artboard size, in artboard units.</summary>
    public float Width { get; }
    public float Height { get; }
    public Vector2 Size => new(Width, Height);

    /// <summary>The most recently rendered frame (premultiplied alpha).</summary>
    public Texture Texture => _textures[_current];

    /// <summary>When true, the engine stops advancing the state machine and keeps the last frame.</summary>
    public bool Paused { get; set; }

    internal RiveInstance(RiveRuntime runtime, nint handle, float width, float height, float resolution)
    {
        _runtime = runtime;
        _handle = handle;
        Width = width;
        Height = height;

        int w = Math.Max(1, (int)MathF.Ceiling(width * resolution));
        int h = Math.Max(1, (int)MathF.Ceiling(height * resolution));
        for (int i = 0; i < Buffers; i++)
            (_targets[i], _textures[i]) = runtime.CreateTarget(w, h);

        // Draw the first frame now so the texture is valid even before the next engine update.
        Advance(0f);
        Render();
        runtime.Finish();
    }

    /// <summary>Sets a number input on the state machine. Returns false if there's no such input.</summary>
    public bool SetNumber(string name, float value) => EngineNative.en_rive_instance_set_number(Live, name, value) != 0;

    /// <summary>Sets a boolean input on the state machine. Returns false if there's no such input.</summary>
    public bool SetBool(string name, bool value) => EngineNative.en_rive_instance_set_bool(Live, name, value ? 1 : 0) != 0;

    /// <summary>Fires a trigger input on the state machine. Returns false if there's no such input.</summary>
    public bool Fire(string name) => EngineNative.en_rive_instance_fire(Live, name) != 0;

    /// <summary>Replaces the text of a named text run. Returns false if there's no such run.</summary>
    public bool SetText(string run, string text)
    {
        _dirty = true;
        return EngineNative.en_rive_instance_set_text(Live, run, text) != 0;
    }

    /// <summary>Pointer events for the state machine's listeners, in artboard coordinates. Return true on a hit.</summary>
    public bool PointerDown(Vector2 position) => Pointer(0, position);
    public bool PointerMove(Vector2 position) => Pointer(1, position);
    public bool PointerUp(Vector2 position) => Pointer(2, position);

    bool Pointer(int action, Vector2 position) =>
        EngineNative.en_rive_instance_pointer(Live, action, position.X, position.Y) != 0;

    nint Live
    {
        get
        {
            ObjectDisposedException.ThrowIf(_handle == 0, this);
            return _handle;
        }
    }

    /// <summary>Returns true if the frame changed and needs rendering.</summary>
    internal bool Advance(float seconds)
    {
        if (Paused)
            return _dirty;

        bool changed = EngineNative.en_rive_instance_advance(_handle, seconds) != 0;
        return changed || _dirty;
    }

    internal void Render()
    {
        _current = (_current + 1) % Buffers;
        EngineNative.en_rive_render(_runtime.Context, _handle, _targets[_current]);
        _dirty = false;
    }

    public void Dispose()
    {
        if (_handle == 0)
            return;

        _runtime.Remove(this);
        for (int i = 0; i < Buffers; i++)
        {
            _textures[i].Dispose();
            EngineNative.en_rive_target_destroy(_targets[i]);
        }

        EngineNative.en_rive_instance_destroy(_handle);
        _handle = 0;
    }
}

/// <summary>
/// Owns the Rive render context and drives every live instance once per frame, between the game's
/// Update and Draw. Created on first use, so games without Rive never load it.
/// </summary>
internal sealed unsafe class RiveRuntime : IDisposable
{
    readonly nint _renderer;
    readonly List<RiveInstance> _instances = [];
    readonly List<RiveFile> _files = [];

    public nint Context { get; private set; }

    RiveRuntime(nint renderer, nint context)
    {
        _renderer = renderer;
        Context = context;
    }

    public static RiveRuntime Create(nint renderer)
    {
        // The Metal bridge is the only Rive backend so far.
        if (!OperatingSystem.IsMacOS())
            throw new PlatformNotSupportedException("Rive is only implemented for macOS (Metal) so far.");

        nint layer = SDL.SDL_GetRenderMetalLayer(renderer);
        if (layer == 0)
            throw new PlatformNotSupportedException($"Rive needs SDL's Metal renderer, not '{SDL.Utf8(SDL.SDL_GetRendererName(renderer))}'.");

        nint context = EngineNative.en_rive_context_create(layer);
        if (context == 0)
            throw new InvalidOperationException("Couldn't create the Rive render context.");

        return new RiveRuntime(renderer, context);
    }

    public RiveFile Load(ReadOnlySpan<byte> bytes, string name)
    {
        nint file;
        fixed (byte* p = bytes)
            file = EngineNative.en_rive_file_load(Context, p, bytes.Length);

        if (file == 0)
            throw new InvalidDataException($"'{name}' isn't a Rive file this runtime can read.");

        var riveFile = new RiveFile(this, file);
        _files.Add(riveFile);
        return riveFile;
    }

    public (nint Target, Texture Texture) CreateTarget(int width, int height)
    {
        nint target = EngineNative.en_rive_target_create(Context, width, height, out nint pixelBuffer);
        if (target == 0)
            throw new InvalidOperationException($"Couldn't create a {width}x{height} Rive render target.");

        // SDL wraps the IOSurface Rive draws into, so there's no copy between them.
        uint props = SDL.SDL_CreateProperties();
        SDL.SDL_SetNumberProperty(props, SDL.PROP_TEXTURE_CREATE_FORMAT_NUMBER, SDL.PIXELFORMAT_ARGB8888);
        SDL.SDL_SetNumberProperty(props, SDL.PROP_TEXTURE_CREATE_WIDTH_NUMBER, width);
        SDL.SDL_SetNumberProperty(props, SDL.PROP_TEXTURE_CREATE_HEIGHT_NUMBER, height);
        SDL.SDL_SetPointerProperty(props, SDL.PROP_TEXTURE_CREATE_METAL_PIXELBUFFER_POINTER, pixelBuffer);
        nint texture = SDL.SDL_CreateTextureWithProperties(_renderer, props);
        SDL.SDL_DestroyProperties(props);

        if (texture == 0)
        {
            EngineNative.en_rive_target_destroy(target);
            throw new InvalidOperationException($"SDL couldn't wrap the Rive target: {SDL.GetError()}");
        }

        return (target, new Texture(texture, TextureFilter.Linear, premultiplied: true));
    }

    public RiveInstance Add(RiveInstance instance)
    {
        _instances.Add(instance);
        return instance;
    }

    public void Remove(RiveInstance instance) => _instances.Remove(instance);

    public void Remove(RiveFile file) => _files.Remove(file);

    public void Finish() => EngineNative.en_rive_finish(Context);

    /// <summary>Advances every instance and re-renders the ones that changed.</summary>
    public void Update(float seconds)
    {
        bool rendered = false;
        foreach (var instance in _instances)
        {
            if (instance.Advance(seconds))
            {
                instance.Render();
                rendered = true;
            }
        }

        // SDL samples these textures from another queue, so they must be finished before it draws.
        if (rendered)
            Finish();
    }

    public void Dispose()
    {
        if (Context == 0)
            return;

        // Everything that holds GPU resources goes before the context that made them.
        for (int i = _instances.Count - 1; i >= 0; i--)
            _instances[i].Dispose();
        for (int i = _files.Count - 1; i >= 0; i--)
            _files[i].Dispose();

        EngineNative.en_rive_context_destroy(Context);
        Context = 0;
    }
}

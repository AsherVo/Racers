/*
 * EngineNative: the engine's native helpers beyond SDL, behind a flat C API that [LibraryImport]
 * can bind directly. Fonts (stb_truetype) are plain C and build anywhere. Rive is the C++ runtime
 * plus its GPU renderer, with a Metal backend (macOS) and a WebGL 2 backend (browser).
 */
#pragma once

#include <stdint.h>

#if defined(_WIN32)
#define EN_API __declspec(dllexport)
#else
#define EN_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif

/* ---- Fonts -------------------------------------------------------------------------------- */

/* Same layout as stbtt_packedchar: atlas rectangle, then the quad's offset from the pen. */
typedef struct EnGlyph
{
    uint16_t x0, y0, x1, y1;
    float xoff, yoff, xadvance;
    float xoff2, yoff2;
} EnGlyph;

typedef struct EnFontMetrics
{
    float ascent, descent, lineGap; /* pixels; descent is negative */
} EnFontMetrics;

/*
 * Rasterizes `count` codepoints starting at `firstCodepoint` into an 8-bit coverage atlas of
 * width x height. Returns 1 on success, 0 if the glyphs don't fit (retry with a larger atlas),
 * and -1 if the font can't be read. `kerning` (count * count floats, may be null) receives the
 * kerning adjustment for each pair, indexed [left * count + right].
 */
EN_API int en_font_bake(const uint8_t* ttf, int ttfLength, float pixelHeight,
                        int firstCodepoint, int count,
                        uint8_t* atlas, int width, int height,
                        EnGlyph* glyphs, float* kerning, EnFontMetrics* metrics);

/* ---- Rive --------------------------------------------------------------------------------- */

typedef struct EnRiveContext EnRiveContext;
typedef struct EnRiveFile EnRiveFile;
typedef struct EnRiveInstance EnRiveInstance;
typedef struct EnRiveTarget EnRiveTarget;

enum
{
    EN_RIVE_NONE = 0,
    /* Targets are IOSurface pixel buffers; Rive renders on its own queue (call en_rive_finish). */
    EN_RIVE_METAL = 1,
    /*
     * Targets are textures SDL created; Rive renders on SDL's WebGL 2 context. Call
     * SDL_FlushRenderer before the first en_rive_render and after en_rive_finish, which leaves the
     * GL state the way SDL's GLES2 renderer expects it.
     */
    EN_RIVE_WEBGL = 2,
};

/* Which Rive backend this build of the library has (EN_RIVE_*). */
EN_API int en_rive_backend(void);

/*
 * Metal: `metalLayer` is the renderer's CAMetalLayer (SDL_GetRenderMetalLayer); Rive renders on
 * its device. WebGL: pass null; Rive uses the current context, which must be SDL's WebGL 2 one.
 */
EN_API EnRiveContext* en_rive_context_create(void* metalLayer);
EN_API void en_rive_context_destroy(EnRiveContext*);

EN_API EnRiveFile* en_rive_file_load(EnRiveContext*, const uint8_t* bytes, int length);
EN_API void en_rive_file_destroy(EnRiveFile*);

/*
 * Instantiates an artboard (null name = the default artboard) and a state machine (null name =
 * the default, else the first, else the first linear animation). Binds the artboard's default
 * view model instance if it has one. Writes the artboard size.
 */
EN_API EnRiveInstance* en_rive_instance_create(EnRiveFile*, const char* artboard, const char* stateMachine,
                                               float* width, float* height);
EN_API void en_rive_instance_destroy(EnRiveInstance*);

/* Advances the scene. Returns 1 if it changed and should be redrawn. */
EN_API int en_rive_instance_advance(EnRiveInstance*, float seconds);

/* State machine inputs and text runs. Each returns 0 if nothing has that name. */
EN_API int en_rive_instance_set_number(EnRiveInstance*, const char* name, float value);
EN_API int en_rive_instance_set_bool(EnRiveInstance*, const char* name, int value);
EN_API int en_rive_instance_fire(EnRiveInstance*, const char* name);
EN_API int en_rive_instance_set_text(EnRiveInstance*, const char* run, const char* text);

/* Pointer events in artboard coordinates. action: 0 = down, 1 = move, 2 = up. Returns 1 on a hit. */
EN_API int en_rive_instance_pointer(EnRiveInstance*, int action, float x, float y);

/*
 * A width x height render target, with rows top-down like SDL textures.
 * Metal: `pixelBuffer` receives the CVPixelBufferRef (BGRA, IOSurface) that backs it, for
 * SDL_PROP_TEXTURE_CREATE_METAL_PIXELBUFFER_POINTER, so SDL samples the memory Rive draws into
 * with no copy. The target owns the pixel buffer. `glTexture` is ignored.
 * WebGL: `glTexture` is an RGBA texture SDL created (SDL_PROP_TEXTURE_OPENGLES2_TEXTURE_NUMBER),
 * which Rive draws into. SDL keeps owning it. `pixelBuffer` receives null.
 */
EN_API EnRiveTarget* en_rive_target_create(EnRiveContext*, int width, int height, uint32_t glTexture, void** pixelBuffer);
EN_API void en_rive_target_destroy(EnRiveTarget*);

/* Clears the target to transparent and draws the instance scaled to fill it (premultiplied alpha). */
EN_API void en_rive_render(EnRiveContext*, EnRiveInstance*, EnRiveTarget*);

/*
 * Ends a run of en_rive_render calls. Metal: blocks until they've finished on the GPU, since SDL
 * samples the targets from another queue. WebGL: hands the GL context back to SDL.
 */
EN_API void en_rive_finish(EnRiveContext*);

#ifdef __cplusplus
}
#endif

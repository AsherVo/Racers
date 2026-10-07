/*
 * EngineNative: the engine's native helpers beyond SDL, behind a flat C API that [LibraryImport]
 * can bind directly. Fonts (stb_truetype) are plain C and build anywhere. Rive is the C++ runtime
 * plus its GPU renderer; only the Metal backend (macOS) is implemented so far.
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

/* Returns 1 if this build of the library has a Rive renderer for the current platform. */
EN_API int en_rive_supported(void);

/* `metalLayer` is the renderer's CAMetalLayer (SDL_GetRenderMetalLayer); Rive renders on its device. */
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
 * A width x height BGRA render target. `pixelBuffer` receives the CVPixelBufferRef that backs it
 * (IOSurface), for SDL_PROP_TEXTURE_CREATE_METAL_PIXELBUFFER_POINTER: SDL samples the same memory
 * Rive draws into, with no copy. The target owns the pixel buffer.
 */
EN_API EnRiveTarget* en_rive_target_create(EnRiveContext*, int width, int height, void** pixelBuffer);
EN_API void en_rive_target_destroy(EnRiveTarget*);

/* Clears the target to transparent and draws the instance scaled to fill it (premultiplied alpha). */
EN_API void en_rive_render(EnRiveContext*, EnRiveInstance*, EnRiveTarget*);

/* Blocks until every render submitted since the last call has finished on the GPU. */
EN_API void en_rive_finish(EnRiveContext*);

#ifdef __cplusplus
}
#endif

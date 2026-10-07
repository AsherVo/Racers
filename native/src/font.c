#include "engine_native.h"

#include <string.h>

#define STB_TRUETYPE_IMPLEMENTATION
#define STBTT_STATIC
#include "stb_truetype.h"

_Static_assert(sizeof(EnGlyph) == sizeof(stbtt_packedchar), "EnGlyph must match stbtt_packedchar");

int en_font_bake(const uint8_t* ttf, int ttfLength, float pixelHeight,
                 int firstCodepoint, int count,
                 uint8_t* atlas, int width, int height,
                 EnGlyph* glyphs, float* kerning, EnFontMetrics* metrics)
{
    stbtt_fontinfo info;
    int offset = stbtt_GetFontOffsetForIndex(ttf, 0);
    if (ttfLength <= 0 || offset < 0 || !stbtt_InitFont(&info, ttf, offset))
        return -1;

    float scale = stbtt_ScaleForPixelHeight(&info, pixelHeight);
    int ascent, descent, lineGap;
    stbtt_GetFontVMetrics(&info, &ascent, &descent, &lineGap);
    metrics->ascent = ascent * scale;
    metrics->descent = descent * scale;
    metrics->lineGap = lineGap * scale;

    memset(atlas, 0, (size_t)width * height);
    stbtt_pack_context pack;
    // One pixel of padding keeps bilinear filtering from bleeding neighbors into each glyph.
    if (!stbtt_PackBegin(&pack, atlas, width, height, 0, 1, NULL))
        return 0;
    int packed = stbtt_PackFontRange(&pack, ttf, 0, pixelHeight, firstCodepoint, count, (stbtt_packedchar*)glyphs);
    stbtt_PackEnd(&pack);
    if (!packed)
        return 0;

    if (kerning)
    {
        for (int a = 0; a < count; a++)
            for (int b = 0; b < count; b++)
                kerning[a * count + b] = scale * stbtt_GetCodepointKernAdvance(&info, firstCodepoint + a, firstCodepoint + b);
    }
    return 1;
}

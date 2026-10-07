// Rive on WebGL 2 (browser): Rive renders on SDL's own GL context into textures SDL created, so
// SDL samples them directly. Sharing one context means handing GL state back and forth: Rive is
// told when SDL has touched the context, and SDL's GLES2 renderer gets back the state it assumes.

#include "rive_bridge.hpp"

#include "rive/renderer/gl/render_context_gl_impl.hpp"
#include "rive/renderer/gl/render_target_gl.hpp"
#include "rive/renderer/rive_renderer.hpp"

#include <GLES3/gl3.h>

using namespace rive;
using namespace rive::gpu;

struct EnRiveContext
{
    std::unique_ptr<RenderContext> context;
    std::unique_ptr<RiveRenderer> renderer;
    bool rendering = false; // between the first en_rive_render and en_rive_finish

    RenderContextGLImpl* impl() { return context->static_impl_cast<RenderContextGLImpl>(); }
};

struct EnRiveTarget
{
    rcp<TextureRenderTargetGL> target;
};

Factory* en_rive_factory(EnRiveContext* ctx) { return ctx->context.get(); }

// WebGL pixel-store parameters (WebGL-only, so not in the GLES headers).
#define EN_UNPACK_FLIP_Y_WEBGL 0x9240
#define EN_UNPACK_PREMULTIPLY_ALPHA_WEBGL 0x9241

// Puts back what SDL's GLES2 renderer sets once at creation and then assumes (see
// GLES2_CreateRenderer). Everything it tracks per draw is reset by SDL_FlushRenderer instead.
static void restore_sdl_state()
{
    glBindVertexArray(0);
    glBindBuffer(GL_ARRAY_BUFFER, 0);
    glBindBuffer(GL_UNIFORM_BUFFER, 0);
    glBindFramebuffer(GL_FRAMEBUFFER, 0);
    glUseProgram(0);

    glDisable(GL_DEPTH_TEST);
    glDisable(GL_STENCIL_TEST);
    glDisable(GL_CULL_FACE);
    glDisable(GL_SCISSOR_TEST);
    glDisable(GL_POLYGON_OFFSET_FILL);
    glDisable(GL_SAMPLE_ALPHA_TO_COVERAGE);
    glDisable(GL_RASTERIZER_DISCARD);
    glColorMask(GL_TRUE, GL_TRUE, GL_TRUE, GL_TRUE);
    glDepthMask(GL_TRUE);

    glPixelStorei(GL_PACK_ALIGNMENT, 1);
    glPixelStorei(GL_UNPACK_ALIGNMENT, 1);
    glPixelStorei(GL_UNPACK_ROW_LENGTH, 0);
    glPixelStorei(EN_UNPACK_FLIP_Y_WEBGL, 0);
    glPixelStorei(EN_UNPACK_PREMULTIPLY_ALPHA_WEBGL, 0);

    // Sampler objects override texture parameters, which is how SDL sets filtering.
    for (GLuint unit = 0; unit < 8; unit++)
        glBindSampler(unit, 0);
    glActiveTexture(GL_TEXTURE0);

    // SDL's position and color attributes are always on; it toggles texcoords itself.
    glEnableVertexAttribArray(0);
    glEnableVertexAttribArray(1);
}

extern "C" {

int en_rive_backend(void) { return EN_RIVE_WEBGL; }

EnRiveContext* en_rive_context_create(void*)
{
    auto context = RenderContextGLImpl::MakeContext();
    if (!context)
        return nullptr;

    auto* ctx = new EnRiveContext();
    ctx->context = std::move(context);
    ctx->renderer = std::make_unique<RiveRenderer>(ctx->context.get());
    ctx->impl()->unbindGLInternalResources();
    restore_sdl_state();
    return ctx;
}

void en_rive_context_destroy(EnRiveContext* ctx)
{
    if (!ctx)
        return;
    en_rive_finish(ctx);
    ctx->renderer.reset();
    ctx->context.reset();
    restore_sdl_state();
    delete ctx;
}

EnRiveTarget* en_rive_target_create(EnRiveContext*, int width, int height, uint32_t glTexture, void** pixelBuffer)
{
    auto* target = new EnRiveTarget();
    target->target = make_rcp<TextureRenderTargetGL>((uint32_t)width, (uint32_t)height);
    target->target->setTargetTexture(glTexture);
    target->target->setBottomUp(false); // SDL textures are top-down
    *pixelBuffer = nullptr;
    return target;
}

void en_rive_target_destroy(EnRiveTarget* target) { delete target; }

void en_rive_render(EnRiveContext* ctx, EnRiveInstance* inst, EnRiveTarget* target)
{
    if (!ctx->rendering)
    {
        // SDL has been using the context since Rive last did.
        ctx->impl()->invalidateGLState();
        ctx->rendering = true;
    }

    uint32_t w = target->target->width(), h = target->target->height();
    ctx->context->beginFrame({
        .renderTargetWidth = w,
        .renderTargetHeight = h,
        .loadAction = LoadAction::clear,
        .clearColor = 0x00000000,
    });
    en_rive_draw_instance(ctx->renderer.get(), inst, w, h);
    ctx->context->flush({.renderTarget = target->target.get()});
}

void en_rive_finish(EnRiveContext* ctx)
{
    if (!ctx->rendering)
        return;

    // One context, so GL already orders Rive's draws before SDL's; only the state needs handing back.
    ctx->impl()->unbindGLInternalResources();
    restore_sdl_state();
    ctx->rendering = false;
}

} // extern "C"

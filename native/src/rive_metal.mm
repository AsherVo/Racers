// Rive on Metal (macOS): each instance renders into an IOSurface-backed texture that SDL's Metal
// renderer wraps as an ordinary SDL_Texture, so the game composites it like any sprite.

#include "rive_bridge.hpp"

#include "rive/renderer/metal/render_context_metal_impl.h"
#include "rive/renderer/rive_renderer.hpp"

#import <CoreVideo/CoreVideo.h>
#import <Metal/Metal.h>
#import <QuartzCore/CAMetalLayer.h>

using namespace rive;
using namespace rive::gpu;

struct EnRiveContext
{
    id<MTLDevice> device;
    id<MTLCommandQueue> queue;
    std::unique_ptr<RenderContext> context;
    std::unique_ptr<RiveRenderer> renderer;
    id<MTLCommandBuffer> lastCommandBuffer;
};

struct EnRiveTarget
{
    CVPixelBufferRef pixelBuffer;
    id<MTLTexture> texture;
    rcp<RenderTargetMetal> target;
};

Factory* en_rive_factory(EnRiveContext* ctx) { return ctx->context.get(); }

extern "C" {

int en_rive_backend(void) { return EN_RIVE_METAL; }

EnRiveContext* en_rive_context_create(void* metalLayer)
{
    @autoreleasepool
    {
        // Use SDL's device so the shared IOSurface textures never cross GPUs.
        CAMetalLayer* layer = (__bridge CAMetalLayer*)metalLayer;
        id<MTLDevice> device = layer != nil && layer.device != nil ? layer.device : MTLCreateSystemDefaultDevice();
        if (device == nil)
            return nullptr;

        auto* ctx = new EnRiveContext();
        ctx->device = device;
        ctx->queue = [device newCommandQueue];
        ctx->queue.label = @"Rive";
        ctx->context = RenderContextMetalImpl::MakeContext(device);
        if (!ctx->context)
        {
            delete ctx;
            return nullptr;
        }
        ctx->context->static_impl_cast<RenderContextMetalImpl>()->setCommandQueue(ctx->queue);
        ctx->renderer = std::make_unique<RiveRenderer>(ctx->context.get());
        return ctx;
    }
}

void en_rive_context_destroy(EnRiveContext* ctx)
{
    if (!ctx)
        return;
    en_rive_finish(ctx);
    @autoreleasepool
    {
        ctx->renderer.reset();
        ctx->context.reset();
        delete ctx;
    }
}

EnRiveTarget* en_rive_target_create(EnRiveContext* ctx, int width, int height, uint32_t, void** pixelBuffer)
{
    @autoreleasepool
    {
        NSDictionary* attributes = @{
            (id)kCVPixelBufferIOSurfacePropertiesKey : @{},
            (id)kCVPixelBufferMetalCompatibilityKey : @YES,
        };
        CVPixelBufferRef buffer = nullptr;
        if (CVPixelBufferCreate(kCFAllocatorDefault, (size_t)width, (size_t)height, kCVPixelFormatType_32BGRA,
                                (__bridge CFDictionaryRef)attributes, &buffer) != kCVReturnSuccess)
            return nullptr;

        MTLTextureDescriptor* desc = [MTLTextureDescriptor texture2DDescriptorWithPixelFormat:MTLPixelFormatBGRA8Unorm
                                                                                        width:(NSUInteger)width
                                                                                       height:(NSUInteger)height
                                                                                    mipmapped:NO];
        desc.usage = MTLTextureUsageRenderTarget | MTLTextureUsageShaderRead;
        id<MTLTexture> texture = [ctx->device newTextureWithDescriptor:desc iosurface:CVPixelBufferGetIOSurface(buffer) plane:0];
        if (texture == nil)
        {
            CVPixelBufferRelease(buffer);
            return nullptr;
        }

        auto* target = new EnRiveTarget();
        target->pixelBuffer = buffer;
        target->texture = texture;
        target->target = ctx->context->static_impl_cast<RenderContextMetalImpl>()->makeRenderTarget(
            MTLPixelFormatBGRA8Unorm, (uint32_t)width, (uint32_t)height);
        target->target->setTargetTexture(texture);
        *pixelBuffer = buffer;
        return target;
    }
}

void en_rive_target_destroy(EnRiveTarget* target)
{
    if (!target)
        return;
    @autoreleasepool
    {
        CVPixelBufferRelease(target->pixelBuffer);
        delete target;
    }
}

void en_rive_render(EnRiveContext* ctx, EnRiveInstance* inst, EnRiveTarget* target)
{
    @autoreleasepool
    {
        uint32_t w = target->target->width(), h = target->target->height();
        ctx->context->beginFrame({
            .renderTargetWidth = w,
            .renderTargetHeight = h,
            .loadAction = LoadAction::clear,
            .clearColor = 0x00000000,
        });
        en_rive_draw_instance(ctx->renderer.get(), inst, w, h);

        id<MTLCommandBuffer> commandBuffer = [ctx->queue commandBuffer];
        ctx->context->flush({
            .renderTarget = target->target.get(),
            .externalCommandBuffer = (__bridge void*)commandBuffer,
        });
        [commandBuffer commit];
        ctx->lastCommandBuffer = commandBuffer;
    }
}

void en_rive_finish(EnRiveContext* ctx)
{
    // Command buffers on one queue complete in order, so waiting on the last covers them all.
    [ctx->lastCommandBuffer waitUntilCompleted];
    ctx->lastCommandBuffer = nil;
}

} // extern "C"

// Rive on Metal: each instance renders into an IOSurface-backed texture that SDL's Metal renderer
// wraps as an ordinary SDL_Texture, so the game composites it like any sprite.

#include "engine_native.h"

#include "rive/animation/state_machine_input_instance.hpp"
#include "rive/animation/state_machine_instance.hpp"
#include "rive/artboard.hpp"
#include "rive/file.hpp"
#include "rive/renderer.hpp"
#include "rive/renderer/metal/render_context_metal_impl.h"
#include "rive/renderer/rive_renderer.hpp"
#include "rive/text/text_value_run.hpp"
#include "rive/viewmodel/viewmodel_instance.hpp"

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

struct EnRiveFile
{
    rcp<File> file;
};

struct EnRiveInstance
{
    rcp<File> file; // keeps the file's assets alive while the instance exists
    std::unique_ptr<ArtboardInstance> artboard;
    std::unique_ptr<StateMachineInstance> stateMachine; // null when playing a linear animation
    std::unique_ptr<Scene> animation;
    rcp<ViewModelInstance> viewModel;

    Scene* scene() const { return stateMachine ? static_cast<Scene*>(stateMachine.get()) : animation.get(); }
};

struct EnRiveTarget
{
    CVPixelBufferRef pixelBuffer;
    id<MTLTexture> texture;
    rcp<RenderTargetMetal> target;
};

extern "C" {

int en_rive_supported(void) { return 1; }

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

EnRiveFile* en_rive_file_load(EnRiveContext* ctx, const uint8_t* bytes, int length)
{
    @autoreleasepool
    {
        // The default asset loader decodes in-band images and fonts through the render context.
        auto file = File::import(Span<const uint8_t>(bytes, (size_t)length), ctx->context.get());
        return file ? new EnRiveFile{std::move(file)} : nullptr;
    }
}

void en_rive_file_destroy(EnRiveFile* file) { delete file; }

EnRiveInstance* en_rive_instance_create(EnRiveFile* file, const char* artboardName, const char* stateMachineName,
                                        float* width, float* height)
{
    auto artboard = artboardName ? file->file->artboardNamed(artboardName) : file->file->artboardDefault();
    if (!artboard)
        return nullptr;

    auto* inst = new EnRiveInstance();
    inst->file = file->file;
    if (stateMachineName)
    {
        inst->stateMachine = artboard->stateMachineNamed(stateMachineName);
        if (!inst->stateMachine)
        {
            delete inst;
            return nullptr;
        }
    }
    else
    {
        inst->stateMachine = artboard->defaultStateMachine();
        if (!inst->stateMachine && artboard->stateMachineCount() > 0)
            inst->stateMachine = artboard->stateMachineAt(0);
        if (!inst->stateMachine && artboard->animationCount() > 0)
            inst->animation = artboard->animationAt(0);
    }

    if (file->file->defaultArtboardViewModel(artboard.get()) != nullptr)
    {
        inst->viewModel = file->file->createDefaultViewModelInstance(artboard.get());
        if (inst->viewModel)
        {
            if (inst->stateMachine)
                inst->stateMachine->bindViewModelInstance(inst->viewModel);
            else
                artboard->bindViewModelInstance(inst->viewModel);
        }
    }

    *width = artboard->width();
    *height = artboard->height();
    inst->artboard = std::move(artboard);
    return inst;
}

void en_rive_instance_destroy(EnRiveInstance* inst) { delete inst; }

int en_rive_instance_advance(EnRiveInstance* inst, float seconds)
{
    if (Scene* scene = inst->scene())
        return scene->advanceAndApply(seconds) ? 1 : 0;
    return inst->artboard->advance(seconds) ? 1 : 0;
}

int en_rive_instance_set_number(EnRiveInstance* inst, const char* name, float value)
{
    SMINumber* input = inst->stateMachine ? inst->stateMachine->getNumber(name) : nullptr;
    if (input)
        input->value(value);
    return input != nullptr;
}

int en_rive_instance_set_bool(EnRiveInstance* inst, const char* name, int value)
{
    SMIBool* input = inst->stateMachine ? inst->stateMachine->getBool(name) : nullptr;
    if (input)
        input->value(value != 0);
    return input != nullptr;
}

int en_rive_instance_fire(EnRiveInstance* inst, const char* name)
{
    SMITrigger* input = inst->stateMachine ? inst->stateMachine->getTrigger(name) : nullptr;
    if (input)
        input->fire();
    return input != nullptr;
}

int en_rive_instance_set_text(EnRiveInstance* inst, const char* run, const char* text)
{
    TextValueRun* textRun = inst->artboard->getTextRun(run, "");
    if (textRun)
        textRun->text(text);
    return textRun != nullptr;
}

int en_rive_instance_pointer(EnRiveInstance* inst, int action, float x, float y)
{
    Scene* scene = inst->scene();
    if (!scene)
        return 0;

    Vec2D position(x, y);
    HitResult result = action == 0   ? scene->pointerDown(position)
                       : action == 1 ? scene->pointerMove(position)
                                     : scene->pointerUp(position);
    return result != HitResult::none;
}

EnRiveTarget* en_rive_target_create(EnRiveContext* ctx, int width, int height, void** pixelBuffer)
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

        Renderer* renderer = ctx->renderer.get();
        renderer->save();
        renderer->transform(computeAlignment(Fit::fill, Alignment::center, AABB(0, 0, (float)w, (float)h),
                                             inst->artboard->bounds()));
        if (Scene* scene = inst->scene())
            scene->draw(renderer);
        else
            inst->artboard->draw(renderer);
        renderer->restore();

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

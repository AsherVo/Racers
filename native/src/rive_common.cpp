#include "rive_bridge.hpp"

#include "rive/animation/state_machine_input_instance.hpp"
#include "rive/text/text_value_run.hpp"

using namespace rive;

extern "C" {

EnRiveFile* en_rive_file_load(EnRiveContext* ctx, const uint8_t* bytes, int length)
{
    // The default asset loader decodes in-band images and fonts through the render context.
    auto file = File::import(Span<const uint8_t>(bytes, (size_t)length), en_rive_factory(ctx));
    return file ? new EnRiveFile{std::move(file)} : nullptr;
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

} // extern "C"

void en_rive_draw_instance(Renderer* renderer, EnRiveInstance* inst, uint32_t width, uint32_t height)
{
    renderer->save();
    renderer->transform(computeAlignment(Fit::fill, Alignment::center, AABB(0, 0, (float)width, (float)height),
                                         inst->artboard->bounds()));
    if (Scene* scene = inst->scene())
        scene->draw(renderer);
    else
        inst->artboard->draw(renderer);
    renderer->restore();
}

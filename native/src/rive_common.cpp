#include "rive_bridge.hpp"

#include "rive/animation/state_machine_input_instance.hpp"
#include "rive/text/text_value_run.hpp"
#include "rive/viewmodel/runtime/viewmodel_instance_boolean_runtime.hpp"
#include "rive/viewmodel/runtime/viewmodel_instance_color_runtime.hpp"
#include "rive/viewmodel/runtime/viewmodel_instance_enum_runtime.hpp"
#include "rive/viewmodel/runtime/viewmodel_instance_list_runtime.hpp"
#include "rive/viewmodel/runtime/viewmodel_instance_number_runtime.hpp"
#include "rive/viewmodel/runtime/viewmodel_instance_string_runtime.hpp"
#include "rive/viewmodel/runtime/viewmodel_instance_trigger_runtime.hpp"
#include "rive/viewmodel/viewmodel_instance_enum.hpp"
#include "rive/viewmodel/viewmodel_instance_trigger.hpp"
#include "utils/no_op_factory.hpp"

#include <cstring>
#include <string>
#include <vector>

using namespace rive;

namespace
{

Factory* headlessFactory()
{
    static NoOpFactory factory;
    return &factory;
}

bool parseRowIndex(const std::string& segment, int* index)
{
    if (segment.empty() || segment.size() > 9)
        return false;

    int value = 0;
    for (char c : segment)
    {
        if (c < '0' || c > '9')
            return false;
        value = value * 10 + (c - '0');
    }
    *index = value;
    return true;
}

/*
 * Walks every segment of `path` but the last through nested view models and list rows, and returns
 * the view model instance that holds the property named by the last segment, or null. Runtime
 * wrappers are cached by their parents (nested view models) and lists (rows), so the result stays
 * valid while the property it was resolved through exists.
 */
ViewModelInstanceRuntime* resolveOwner(EnRiveInstance* inst, const char* path, std::string* name)
{
    if (!inst->viewModelRuntime || !path || !*path)
        return nullptr;

    std::vector<std::string> segments;
    const char* start = path;
    for (const char* c = path;; c++)
    {
        if (*c == '/' || *c == '\0')
        {
            segments.emplace_back(start, c - start);
            if (*c == '\0')
                break;
            start = c + 1;
        }
    }

    rcp<ViewModelInstanceRuntime> owner = inst->viewModelRuntime;
    size_t last = segments.size() - 1;
    for (size_t i = 0; i < last; i++)
    {
        int row;
        if (i + 1 < last && parseRowIndex(segments[i + 1], &row))
        {
            ViewModelInstanceListRuntime* list = owner->propertyList(segments[i]);
            owner = list ? list->instanceAt(row) : nullptr;
            i++;
        }
        else
        {
            owner = owner->propertyViewModel(segments[i]);
        }

        if (!owner)
            return nullptr;
    }

    if (segments[last].empty())
        return nullptr;

    *name = segments[last];
    return owner.get();
}

template <typename T, typename Lookup>
T* resolveProperty(EnRiveInstance* inst, const char* path, Lookup lookup)
{
    std::string name;
    ViewModelInstanceRuntime* owner = resolveOwner(inst, path, &name);
    return owner ? (owner->*lookup)(name) : nullptr;
}

ViewModelInstanceListRuntime* resolveList(EnRiveInstance* inst, const char* path)
{
    return resolveProperty<ViewModelInstanceListRuntime>(inst, path, &ViewModelInstanceRuntime::propertyList);
}

} // namespace

extern "C" {

EnRiveFile* en_rive_file_load(EnRiveContext* ctx, const uint8_t* bytes, int length)
{
    // The default asset loader decodes in-band images and fonts through the render context.
    Factory* factory = ctx ? en_rive_factory(ctx) : headlessFactory();
    auto file = File::import(Span<const uint8_t>(bytes, (size_t)length), factory);
    return file ? new EnRiveFile{std::move(file)} : nullptr;
}

void en_rive_file_destroy(EnRiveFile* file) { delete file; }

int en_rive_artboard_size(EnRiveFile* file, const char* artboardName, float* width, float* height)
{
    Artboard* artboard = artboardName ? file->file->artboard(artboardName) : file->file->artboard();
    if (!artboard)
        return 0;

    *width = artboard->width();
    *height = artboard->height();
    return 1;
}

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

    // Null when the artboard has no view model.
    inst->viewModel = file->file->createDefaultViewModelInstance(artboard.get());
    if (inst->viewModel)
    {
        inst->viewModelRuntime = make_rcp<ViewModelInstanceRuntime>(inst->viewModel);
        if (inst->stateMachine)
            inst->stateMachine->bindViewModelInstance(inst->viewModel);
        else
            artboard->bindViewModelInstance(inst->viewModel);
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

void en_rive_instance_resize(EnRiveInstance* inst, float width, float height)
{
    inst->artboard->width(width);
    inst->artboard->height(height);
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

int en_rive_instance_set_vm_number(EnRiveInstance* inst, const char* path, float value)
{
    auto* property = resolveProperty<ViewModelInstanceNumberRuntime>(inst, path, &ViewModelInstanceRuntime::propertyNumber);
    if (property)
        property->value(value);
    return property != nullptr;
}

int en_rive_instance_set_vm_string(EnRiveInstance* inst, const char* path, const char* value)
{
    auto* property = resolveProperty<ViewModelInstanceStringRuntime>(inst, path, &ViewModelInstanceRuntime::propertyString);
    if (property)
        property->value(value);
    return property != nullptr;
}

int en_rive_instance_set_vm_bool(EnRiveInstance* inst, const char* path, int value)
{
    auto* property = resolveProperty<ViewModelInstanceBooleanRuntime>(inst, path, &ViewModelInstanceRuntime::propertyBoolean);
    if (property)
        property->value(value != 0);
    return property != nullptr;
}

int en_rive_instance_set_vm_color(EnRiveInstance* inst, const char* path, uint32_t argb)
{
    auto* property = resolveProperty<ViewModelInstanceColorRuntime>(inst, path, &ViewModelInstanceRuntime::propertyColor);
    if (property)
        property->value((int)argb);
    return property != nullptr;
}

int en_rive_instance_set_vm_enum(EnRiveInstance* inst, const char* path, const char* value)
{
    auto* property = resolveProperty<ViewModelInstanceEnumRuntime>(inst, path, &ViewModelInstanceRuntime::propertyEnum);
    return property && property->viewModelInstanceValue()->as<ViewModelInstanceEnum>()->value(value);
}

int en_rive_instance_fire_vm_trigger(EnRiveInstance* inst, const char* path)
{
    auto* property = resolveProperty<ViewModelInstanceTriggerRuntime>(inst, path, &ViewModelInstanceRuntime::propertyTrigger);
    if (property)
        property->trigger();
    return property != nullptr;
}

int en_rive_instance_get_vm_number(EnRiveInstance* inst, const char* path, float* value)
{
    auto* property = resolveProperty<ViewModelInstanceNumberRuntime>(inst, path, &ViewModelInstanceRuntime::propertyNumber);
    if (property)
        *value = property->value();
    return property != nullptr;
}

int en_rive_instance_get_vm_bool(EnRiveInstance* inst, const char* path, int* value)
{
    auto* property = resolveProperty<ViewModelInstanceBooleanRuntime>(inst, path, &ViewModelInstanceRuntime::propertyBoolean);
    if (property)
        *value = property->value() ? 1 : 0;
    return property != nullptr;
}

int en_rive_instance_get_vm_string(EnRiveInstance* inst, const char* path, char* buffer, int capacity)
{
    auto* property = resolveProperty<ViewModelInstanceStringRuntime>(inst, path, &ViewModelInstanceRuntime::propertyString);
    if (!property)
        return -1;

    const std::string& value = property->value();
    if (capacity > 0)
    {
        size_t copied = value.size() < (size_t)capacity ? value.size() : (size_t)capacity - 1;
        memcpy(buffer, value.data(), copied);
        buffer[copied] = '\0';
    }
    return (int)value.size();
}

int en_rive_instance_get_vm_trigger(EnRiveInstance* inst, const char* path, uint32_t* count)
{
    auto* property = resolveProperty<ViewModelInstanceTriggerRuntime>(inst, path, &ViewModelInstanceRuntime::propertyTrigger);
    if (property)
        *count = property->viewModelInstanceValue()->as<ViewModelInstanceTrigger>()->propertyValue();
    return property != nullptr;
}

int en_rive_instance_vm_list_count(EnRiveInstance* inst, const char* path)
{
    ViewModelInstanceListRuntime* list = resolveList(inst, path);
    return list ? (int)list->size() : -1;
}

int en_rive_instance_vm_list_insert(EnRiveInstance* inst, const char* path, int index, const char* viewModel)
{
    ViewModelInstanceListRuntime* list = resolveList(inst, path);
    if (!list || !viewModel || index < 0 || (size_t)index > list->size())
        return 0;

    // File::viewModelByName would keep a new runtime wrapper alive in the file on every call.
    ViewModel* model = inst->file->viewModel(viewModel);
    if (!model)
        return 0;

    rcp<ViewModelInstance> instance = inst->file->createDefaultViewModelInstance(model);
    if (!instance)
        return 0;

    auto row = make_rcp<ViewModelInstanceRuntime>(instance);
    return list->addInstanceAt(row.get(), index) ? 1 : 0;
}

int en_rive_instance_vm_list_remove(EnRiveInstance* inst, const char* path, int index)
{
    ViewModelInstanceListRuntime* list = resolveList(inst, path);
    if (!list || index < 0 || (size_t)index >= list->size())
        return 0;

    list->removeInstanceAt(index);
    return 1;
}

int en_rive_instance_vm_list_swap(EnRiveInstance* inst, const char* path, int a, int b)
{
    ViewModelInstanceListRuntime* list = resolveList(inst, path);
    if (!list || a < 0 || b < 0 || (size_t)a >= list->size() || (size_t)b >= list->size())
        return 0;

    list->swap((uint32_t)a, (uint32_t)b);
    return 1;
}

int en_rive_instance_vm_list_clear(EnRiveInstance* inst, const char* path)
{
    ViewModelInstanceListRuntime* list = resolveList(inst, path);
    if (!list)
        return 0;

    list->removeAllInstances();
    return 1;
}

int en_rive_instance_pointer(EnRiveInstance* inst, int action, float x, float y)
{
    Scene* scene = inst->scene();
    if (!scene)
        return 0;

    Vec2D position(x, y);
    HitResult result;
    switch (action)
    {
        case EN_RIVE_POINTER_DOWN:
            result = scene->pointerDown(position);
            break;
        case EN_RIVE_POINTER_MOVE:
            result = scene->pointerMove(position);
            break;
        case EN_RIVE_POINTER_UP:
            result = scene->pointerUp(position);
            break;
        case EN_RIVE_POINTER_EXIT:
            result = scene->pointerExit(position);
            break;
        default:
            return 0;
    }
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

// Shared between the Rive backends: files, instances and drawing are backend-independent; each
// backend (rive_metal.mm, rive_webgl.cpp) supplies the context, render targets and submission.
#pragma once

#include "engine_native.h"

#include "rive/animation/state_machine_instance.hpp"
#include "rive/artboard.hpp"
#include "rive/file.hpp"
#include "rive/renderer.hpp"
#include "rive/viewmodel/viewmodel_instance.hpp"

struct EnRiveFile
{
    rive::rcp<rive::File> file;
};

struct EnRiveInstance
{
    rive::rcp<rive::File> file; // keeps the file's assets alive while the instance exists
    std::unique_ptr<rive::ArtboardInstance> artboard;
    std::unique_ptr<rive::StateMachineInstance> stateMachine; // null when playing a linear animation
    std::unique_ptr<rive::Scene> animation;
    rive::rcp<rive::ViewModelInstance> viewModel;

    rive::Scene* scene() const
    {
        return stateMachine ? static_cast<rive::Scene*>(stateMachine.get()) : animation.get();
    }
};

// Implemented by the backend.
rive::Factory* en_rive_factory(EnRiveContext*);

// Draws the instance scaled to fill a width x height target.
void en_rive_draw_instance(rive::Renderer*, EnRiveInstance*, uint32_t width, uint32_t height);

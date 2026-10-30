# Rive UI

**Goal:** complex UI is built in Rive with Rive layouts. One artboard holds a whole screen, many
buttons included, and the game can add and remove those buttons while it runs.

**Shape:** Rive owns hit testing and visuals *inside* a screen; the world owns what things *mean*.

```
                    pointer (artboard coords)
PointerSystem ─────────────────────────────► Rive screen (one DrawRive entity)
     ▲                                          │  layout + list of rows
     │ topmost target only                      │  row listeners: hover/press visuals
     │                                          │  a row's `clicked` trigger fires
     │                                          ▼
     │                         RiveListSystem: polls triggers, maps row → entity
     │                                          │
     └──── other targets (grid cells)           ▼
                                   Click { target = buttonEntity }   (same message as today)
```

Today every button is its own entity and artboard: `PointerSystem` hit-tests each one by texture
alpha, and `ButtonSystem` drives its hover and click visuals with view model triggers. That stays
for single-artboard buttons like `Cell`. This worklist adds the second path.

---

## Rules

1. **Entities are the source of truth.** A button in a Rive list is a view entity. The Rive list is a
   cache that `RiveListSystem` rebuilds from those entities, so it survives a system restart or a
   world load like any other cache (ECS Rule 7). The list the `.riv` file was authored with is
   cleared the first time the game takes it over.
2. **Downstream code doesn't care where a button lives.** A click on a list row becomes the same
   `Click { target = entity }` view message a single-artboard button sends.
3. **Game-to-Rive and Rive-to-game use different trigger names.** The game fires `click` into an
   artboard; a row fires `clicked` back. A system that fired the same trigger it listens for would
   hear its own fire and loop.
4. **Rive trigger counts, not change flags.** A view model trigger's value counts up by one per fire
   and is never reset (`ViewModelInstanceTrigger` in the runtime), so comparing counts can't miss a
   fire or see one twice.
5. **All of this is view code.** Clicks still reach the sim only as commands. No Rive state is
   saved or checksummed.
6. **The pointer goes to one target.** The topmost thing under the pointer gets it, so UI drawn over
   the grid blocks clicks to the grid.

---

## Worklist

### Phase 1: Engine Rive API

- [x] Headless file loading: `en_rive_file_load` with a null context imports through Rive's
      `NoOpFactory`, so the view model, list, pointer and resize calls can be tested without a GPU.
      *(`no_op_factory.cpp` isn't in Rive's libraries, so both native build scripts compile it.)*
- [x] View model paths can index into lists: `buttons/2/label` resolves the third row of the
      `buttons` list. Applies to every getter and setter.
      *(`resolveOwner` in `rive_common.cpp`; the runtime's resolver doesn't know list rows.)*
- [x] Getters: number, string, bool, and a trigger's fire count.
- [x] Lists: count, insert a new instance of a named view model at an index, remove at an index,
      swap two rows, clear.
      *(Insert uses `File::viewModel`: `viewModelByName` keeps a new wrapper in the file per call.
      Instance creation no longer calls `defaultArtboardViewModel` for the same reason.)*
- [x] Pointer exit, so hover clears when the pointer leaves an artboard.
- [x] Resize: the artboard takes a new size and its layout reflows; `RiveInstance.Resize` rebuilds
      its render targets at the new size.
- [x] C# wrappers on `RiveInstance`, and `architecture.md`'s Rive section updated.
- [x] Tests in `Engine.Tests` against a fixture `.riv` built by the Rive CLI (macOS, where
      `libEngineNative` is built; skipped elsewhere).
      *(`RiveViewModelTests`, 25 tests, including clicking a row inserted at runtime and a layout
      reflowing after a resize.)*
- [x] The wasm build compiles the new bridge code.
      *(`build-native-wasm.sh`, then `dotnet publish hosts/Browser` links it.)*

### Phase 2: Pointer routing

- [ ] A `PointerDown` view message, so Rive sees presses (today only moves and releases reach the
      world).
- [ ] `PointerSystem` finds the topmost target under the pointer, front to back: canvas
      `SortOrder`, then entity `SortOrder`, then id, skipping `Hidden`. Hover and clicks go to that
      target only (Rule 6).
- [ ] `RiveInput` component: the entity's Rive instance gets pointer moves, presses and releases in
      artboard coordinates. A press captures the pointer until the release, so dragging off a button
      doesn't strand it pressed. Leaving the artboard sends a pointer exit.
- [ ] A `Screens` data group: `PrefabSystem` gives its entities `Interactable` and `RiveInput`, the
      way `Buttons` gives click and hover targets.

### Phase 3: `RiveListSystem`

- [ ] `RiveListItem` relation from a row entity to its screen, naming the list property and the
      row's view model.
- [ ] Each frame, sync each screen's lists to its row entities, ordered by `SortOrder` then id:
      remove, then swap or insert, so rows that stay keep their Rive state. The diff is a pure
      function with unit tests.
- [ ] A new Rive instance for a screen (first sync, or `RenderSystem` restarted) clears the list and
      rebuilds it (Rule 1).
- [ ] Poll each row's `clicked` count; each new fire sends `Click { target = row }`.
- [ ] `ButtonLabel` works on rows too: it's written to the row's `label` in the screen's view model.
- [ ] `DrawRive.layout`: the artboard follows the entity's `Rect` instead of being scaled to it, so
      its layout reflows.

### Phase 4: The menu as one Rive screen

- [ ] `menu_ui.riv`: a `Menu` screen artboard (a layout column holding an `ArtboardComponentList`
      bound to a `buttons` list) and a `MenuItem` row artboard whose own listeners drive hover and
      press, and fire `clicked` on release. Verified with Rive CLI captures.
- [ ] `MenuViewSystem` creates one screen entity plus a row entity per label, and adds or removes
      rows at runtime by creating and destroying entities.
- [ ] Checked in game: hover, press, click, labels, adding and removing rows, and clicks not
      reaching the grid under the menu.
- [ ] The single-artboard `Button` and `MenuButton.yaml` are removed.

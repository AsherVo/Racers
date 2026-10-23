# Determinism

**Goal:** bit-identical simulation on every platform (Windows, macOS, iOS, Android, browser). The
sim's state is a pure function of:

```
( content, seed, [ ( tick, command ), ... ] )
```

Frame rate, wall-clock time, window size, runtime (CoreCLR, NativeAOT, Mono wasm) and rendering
have no effect on it.

**Shape:** one `World`. Systems implement `ISimSystem` (runs on the fixed tick) or
`IViewSystem` (runs once per frame). Components, entities and messages are classified the same way.

---

## Rules

These are what the worklist enforces. Check new code against them.

1. **The sim never sees real time.** Time in the sim is `world.tick` (an integer). Durations are tick counts.
2. **The sim never sees floats.** Use `int`/`long`, or `Fixed` once it exists. That covers `float`,
   `double`, `Vector2`, `MathF` and `Math.Sin`/`Pow`/etc. `float` is fine in view code.
3. **Data flows one way.** The view reads sim state and never writes it. The sim never reads view
   state: no `Position`/`Rect`/`Draw*`, no `graphics.*`, no Rive values or events.
4. **Input reaches the sim only as commands.** Each command is stamped with the tick it applies on.
   Nothing else from the platform gets in.
5. **Randomness comes only from the sim RNG.** Its state lives in a sim component. Never use
   `System.Random`. Cosmetic effects use a separate view RNG.
6. **Sim iteration order is explicit.** Iterate by sorted entity id. Never rely on
   `Dictionary`/`HashSet` enumeration order, `GetHashCode()` or `Type` ordering. Filters are
   rebuilt from the world on every system start, so after a load their order is unrelated to an
   uninterrupted run.
7. **Systems are disposable.** Systems are never serialized. Everything that persists lives in
   `World`, its entities and its components. A system's fields are caches: `StartSystem` rebuilds
   them from the world (which may have just been loaded mid-match) and `StopSystem` releases them.
   View systems may lose cosmetic state on a restart (hover state, Rive state machines,
   coroutines). Sim systems may lose nothing.
8. **No concurrency in the sim.** No threads, `Task`, `async` or `Parallel`.
9. **Coroutines are view-only and transient.** `IEnumerator` state can't be serialized, so
   coroutines end on `StopSystem` and don't survive a load. Sim sequencing is plain component state
   plus tick counts.
10. **No one reads messages across ticks or frames.** There is no `ReadOld`. Messages aren't
   serialized, so saves happen only between ticks, when no sim messages are in flight.

---

## Worklist

### Phase 1: Fixed timestep (Engine)

- [x] Add `ticksPerSecond` to `GameConfig` (`config.yaml`), validated > 0.
- [x] Add an accumulator to `GameRunner.Iterate` (`src/Engine/GameRunner.cs:101`) in integer
      nanoseconds (`ulong`, from `SDL_GetTicksNS`). Never accumulate floats. Run whole ticks while
      `accumulator >= tickNs`.
- [x] Cap catch-up at N ticks per frame. When the cap is hit, drop the excess real time, not sim ticks.
      *(Done: `FixedTimestep` in `src/Engine`, cap 8, unit-tested in `FixedTimestepTests`.)*
- [x] Split the `Game` hooks:
      - `FixedUpdate ()`: sim, runs 0..N times per frame. The game decides whether to simulate
        the tick, which is how a mid-game pause works (see **Time** in Phase 2).
      - `Update ( FrameTime time )`: view, runs once per frame. `FrameTime` carries
        `float realDt` (unclamped, includes time in the background), `float appDt` (clamped, zero
        while backgrounded) and `float alpha` (accumulator / tickNs, for interpolation).
- [x] Clear the accumulator on pause and on returning from the background (where `lastTicks` is
      reset today).
- [x] `Graphics.UpdateRive( dt )` keeps using frame `dt`, since it's view-only.
- [x] Update the frame-loop section of `docs/architecture.md`.

### Phase 2: Split the ECS (one World, two phases)

**Systems**

- [x] Remove `TickSystem` from `ISystem`, leaving `StartSystem`/`StopSystem`. A system with only
      those (e.g. `PrefabSystem`) is a utility.
- [x] Add `ISimSystem : ISystem { void FixedUpdate ( World world ); }`.
- [x] Add `IViewSystem : ISystem { void Update ( World world, FrameTime time ); }`.
- [x] A system implements at most one of the two. `World.Start` errors on both.
- [x] Audit every system against Rule 7: `StartSystem` works on a fresh world, on a world loaded
      mid-match, and after any number of earlier stop/start cycles.
- [x] `World.FixedTick()` runs sim systems in start order and increments `world.tick`.
      `World.Update( time )` runs view systems. `IDrawSystem` is unchanged.

**Time**

- [x] `world.tick` (`long`): how many sim ticks have been simulated. It lives on `World` itself,
      next to `nextEntityId`, and is serialized and checksummed. `World.FixedTick()` increments it
      before the sim systems run.
- [x] Clock entities: one view entity per `Time` (`Realtime`, `App`, `Game`), each with a
      `Clock { Time type; double total; float delta; long lastTick; }` view component. Clocks
      are serialized with the world, so totals survive a load.
      - `TimeSystem` creates any missing clocks lazily on its first `AdvanceFrame`. That runs in
        the view phase, so clocks get view-range ids and never shift sim ids, whether or not
        `TimeSystem` is running (e.g. in the headless runner).
- [x] `TimeSystem` stays as a utility the world feeds, and implements neither interface. Its only
      fields are cached clock entity ids.
      - `World.Update( time )` calls `AdvanceFrame( time )` before the view systems run, which
        updates every clock.
      - `Realtime`: from `FrameTime.realDt`. Never pauses.
      - `App`: from `FrameTime.appDt`. Stops while the app is backgrounded.
      - `Game`: Matches `App` time for now (until a pause menu is implemented)
- [x] Lookups for the view: `GetDelta( Time )` (`float` seconds this frame), `GetTotal( Time )`
      (`double` seconds) and `ticksThisFrame`. `Time.None` is an error.
      *(`Clock` also has `int deltaTicks`, which backs `ticksThisFrame`. `Time`, `Clock` and
      `TimeSystem` moved into `MintECS`, since `World.Update` feeds `TimeSystem`.)*
- [x] Sim helpers in integer math, e.g. `TicksFromMilliseconds( int ms )` rounded up, so any
      nonzero wait is at least 1 tick. *(On `World`, with `TicksFromSeconds` and `ticksPerSecond`,
      so they work without a `TimeSystem`, e.g. in the headless runner.)*

**Components**

- [x] Add a marker interface, `ISimComponent`. Components that implement it are sim components and
      everything else is a view component. It's an interface rather than a base class so relations
      can be sim components too (`Cell : Relation, ISimComponent`).
- [x] Startup reflection check: an `ISimComponent` type may not have `float`, `double`, `Vector2`,
      `Color`, `Dictionary`, `HashSet` or reference-to-view-type fields. Fail loudly.

**Entities**

- [x] Phase-based id allocation. A sim entity's id must not depend on view activity. If one counter
      is shared, view entities created at frame-dependent times (a hover highlight, an explosion,
      a tooltip) shift every later sim id. The same car is then entity 41 at 60 Hz and 47 at
      120 Hz, which breaks checksums of stored entity references, commands that name entities,
      desync diffs and rollback.
      - Keep a single `Create()`. The world picks the counter from the current phase.
      - **Default phase** (`Load`, `FixedTick`, anything outside the view): ids come from
        `nextEntityId` (1, 2, 3…). Only entities the sim needs go here. View-only entities
        made in `Load` (canvases, UI) are created in the view phase, so a UI change never shifts
        sim ids or breaks recorded replays.
      - **View phase** (`Update`, `Draw`, input handlers): ids come from `nextViewEntityId`, which
        starts at `VIEW_ENTITY_ID_START = 1_000_000` so they stay readable in the inspector.
      - `World.IsViewEntity( id )` checks against the start of the view range.
      - Both counters go into world serialization. A rollback restores only `nextEntityId`.
- [x] The editor and inspector stay unchanged: one `entities` dictionary, one flat list. View-phase
      entities just have large ids. Optionally tag them in the inspector.
- [x] Sim entities may carry view components; the car can have `InCell` and `DrawSprite`. View
      entities may not carry sim components: `Add` of an `ISimComponent` to a view-range entity is
      an error in any phase.
- [x] View filter `onRemove` callbacks can fire during sim `Destroy`. They must not touch sim state.

**Messages**

- [x] **Commands** (`Command : Message`): queued by the view, applied at the start of the next
      fixed tick and stamped with that tick. Only sim systems read them. The pending queue lives
      on `World` and is serialized, so commands queued just before a save still apply after the load.
- [x] **Sim messages**: sent during `FixedTick`, which clears them per tick. They are also
      appended to a view inbox that the view drains once per frame, because a frame can contain
      0..N ticks. Without the inbox, the view drops or duplicates events like "car crashed".
- [x] **View messages**: sent during `Update` and cleared per frame. The sim never reads them.

**Guards** (debug builds)

- [x] During `Update`:
      - `Add`/`Remove` of an `ISimComponent` type is an error.
      - `Create` makes view entities.
- [x] During `FixedTick`:
      - `Get`/`Has`/`TryGet` of a view component is an error.
      - `Add`/`Remove` of a view component is allowed, so the sim can spawn entities with their
        visuals attached (e.g. `PrefabSystem.CreateFromData`). Sim code uses `Add( entity, new T() )`,
        not `Add< T >( entity )`, which returns an existing component and so counts as a read.
      - Reading view messages is an error.
      - `TimeSystem.GetDelta`/`GetTotal` are errors. The view-component guard already catches the
        clock reads; this gives a clearer message. The sim reads `world.tick` only.
      - `ReadOld` is an error (Rule 10). *(Superseded: `ReadOld` and `oldMessages` are deleted.
        Messages are commands, sim messages and frame-local view messages; see `docs/ECS.md`.)*
- [x] During `Update`, also checksum sim state before and after the view phase. Component
      references are mutable, so this is the only way to catch a view write to a sim field.
      *(Also around `Draw` and `EnterViewPhase()` scopes. Filters remember the phase they started
      in and run their callbacks in it, so view filters fired by a tick follow view rules.)*

**Cleanup**

- [x] Fix `RefreshFilter` (`World.cs:739`). It assigns `filter.entities` before diffing, so
      `toAdd`/`toRemove` are always empty.

### Phase 3: Migrate existing systems

| System | Becomes |
|---|---|
| `TimeSystem` | Kept as a utility the world feeds. Clocks live on entities and ticks on `World` (see **Time** in Phase 2). |
| `TimePassed` message | Deleted. Readers look up `TimeSystem.GetDelta( Time )` instead. |
| `PointerSystem` | View. Hit-testing reads texture alpha, Rive and float rects, so it can't be sim. It loses hover state on a restart, which is fine. |
| `ButtonSystem` | View. Turns clicks into **commands**. *(Superseded in Phase 4: `ButtonSystem` only does hover visuals, and `GridViewSystem` turns cell clicks into `SelectCell`.)* |
| `GridSystem` | Split into `GridSystem` (sim) and `GridViewSystem` (view, with `DressGrid( canvas )` called from `Load`). A new `Grid` entity holds `width`/`height`. `Cell` becomes a relation that points at its grid (`Cell : Relation, ISimComponent`, keeping `coords`). `cells[]`/`highlights[]` become caches rebuilt in `StartSystem`, so a highlight needs a link back to its cell. Grid rules and occupancy go to sim; cell visuals and highlights go to view. |
| `SpriteAnimationSystem` | View (frame `dt` is fine). |
| `CoroutineSystem` / `WaitForSeconds` | View. Each coroutine follows its `Time` type through `TimeSystem.GetDelta`. A `Game` coroutine pauses with the sim. Coroutines end on `StopSystem` and don't survive a load (Rule 9). |
| `RenderSystem` | View + draw. Rive instances are rebuilt from `DrawRive` on start. Rive state-machine state resets after a load. |
| `PrefabSystem` | Utility. No special path: called from `Load` or a sim system it creates sim-range ids; called from a view system it creates view-range ids. |

- [x] `RacersGame.Update` calls `world.Update( time )`. The new `RacersGame.FixedUpdate` calls
      `world.FixedTick()`.
- [x] The car: sim state is `GridPos` (int cell, plus sub-cell progress in integer units). The view
      derives `Position` from it and interpolates with `alpha`.
      *(`GridViewSystem` derives `Position` from `GridPos` every frame. Interpolation with `alpha`
      is left to the Phase 7 polish item: nothing moves yet, so there's no previous state to blend.)*
      *(Later: `GridPos` was removed. Grid position is the `InCell` relation; moves are discrete
      and `UnitViewSystem` animates them on `UnitMoved`.)*

### Phase 4: Input as commands

- [x] `OnPointer`/`OnKey` send view messages only. They no longer reach the sim directly.
      *(`PointerHover`/`PointerClick` and `KeyInput`, inside `world.EnterViewPhase()`. Escape still
      calls `Exit()` directly, which is app-level and never touches the world.)*
- [x] View systems translate input into game commands such as `SelectCell { x, y }` and
      `ChangeLane { lane }`. These contain integers, never raw pointer positions.
      *(`GridViewSystem` turns clicks on cells into `SelectCell`; `GridSystem` logs it as a test.
      `PressButton` is gone. Command types are checked by `SimComponentRules` like sim components.
      `ChangeLane` waits for lanes to exist.)*
- [x] `CommandQueue`: pending commands are stamped with the next tick and applied at the start of
      that tick, in the order they were queued.
      *(Built in Phase 2 as `World.pendingCommands`; no separate class needed.)*
- [x] Every applied `( tick, command )` is recorded. This is the replay stream, and later the
      network protocol. The recorder lives outside the world (in `RacersGame` or the engine),
      because it watches the world across stops, starts and loads.
      *(`CommandRecorder` in `MintECS`, fed by `World.onCommandApplied`, owned by `RacersGame`.
      Writing it to a replay file is Phase 6.)*

### Phase 5: Deterministic primitives (Engine)

- [ ] `DeterministicRandom` (PCG32 or xoshiro128**). Its state lives in a sim singleton component,
      so it's checksummed and snapshotted. The seed comes from match setup.
- [ ] Separate cosmetic RNG for the view, so effects never use up sim randomness.
- [x] Sorted iteration for sim systems. Either sim filters keep a sorted `List< uint >`, or
      the sim iterates `filter.entities` through a sorted view. Pick one and make it the only API
      sim systems get.
      *(Every filter keeps a sorted list: `Filter.entities` is a `SortedEntities`, which iterates
      without allocating. `GetRelations`/`GetEntities< T >` and `GetEntitiesNonAlloc` return ids in
      ascending order, and `GetSingleEntity` returns the lowest. Tested in `SortedIterationTests`.)*
- [x] Stable type order wherever the sim enumerates component types (checksum, `Destroy`): sort by
      `Type.FullName` with ordinal comparison.
      *(`World.ComponentTypesInOrder()`, cached until a new type appears. `Checksum()` already
      sorted.)*
- [ ] `Fixed` (Q32.32 in a `long`, or Q16.16 in an `int`), `FixedVector2`, and integer `Sqrt`.
      Add trig/LUT only if movement needs it. Defer until the first real movement logic, but
      before any sim component stores a fraction.
- [ ] YAML: parse `Fixed` straight from decimal text, so sim data never takes a float detour.
      Sim data in YAML is integers or `Fixed`.

### Phase 6: Verification

- [ ] `World.Checksum()`: hash all `ISimComponent`s, sorted by entity id and then type name, plus
      `world.tick` and the RNG state. *(Built in Phase 2 for the view-phase guard, with FNV-1a; still
      needs the RNG state once it exists.)* Use a fixed hash such as FNV-1a or xxHash64, not
      `HashCode` (which is randomized per process).
- [ ] Replay file: moved to `docs/worklists/replays.md` Phase 1.
- [x] Headless runner: build the world with only sim and utility systems (no SDL,
      graphics or content textures) and play back a replay. It must create the same sim-range
      entities as a full run. Either it runs the same `Load` with stubbed graphics, or `Load`
      creates view-only entities (canvases, UI, cell visuals) in the view phase.
      *(`HeadlessRunner` in the game assembly. `Load` already made view entities in the view
      phase, so there's no stubbing. What changed: `PrefabSystem` no longer loads textures or Rive
      files to size `Rect`; it adds `Rect` with 0 for "from the asset", and `RenderSystem` fills
      that in when the entity appears. `ContentManager( root )` reads files with no graphics.
      `SimSystems.Start` is the one list of sim systems, used by `RacersGame` and the runner.
      The SDL library is still loaded, for `Log` and file reads, but never initialized.
      Checked: replays recorded headless match every checksum in the desktop build.)*
- [ ] `ENGINE_REPLAY=path`: moved to `docs/worklists/replays.md` Phase 2.
- [x] Test: play the same replay twice in-process and compare checksums.
      *(`ReplayTests` in `tests/Racers.Tests`, on a sim-only match built without content: the
      real `GridSystem` and `RaceSystem` with two units.)*
- [x] Save/load test (enforces Rule 7): run to tick N, serialize the world, load it into a new
      world with new systems, and keep going. The checksums must match an uninterrupted run.
      This catches any system that keeps state of its own.
      *(`WorldSaveTests.LoadingMidMatchMatchesAnUninterruptedRun`, comparing every tick. Needed
      world serialization, which didn't exist: `World.Save()`/`World.Load()` in `World.Save.cs`.)*
- [ ] Cross-platform golden test: one checked-in replay, with the expected checksum log, played on
      desktop CoreCLR, NativeAOT, browser (Mono wasm), iOS and Android. Run in CI where possible.
- [x] Desync report: the first tick whose checksum differs, plus a YAML dump of sim state at that
      tick for diffing.
      *(`ReplayPlayer.desync` and `World.SaveSimState()`; `RacersGame` writes the dump next to the
      replay. It finds the first recorded checksum that differs, so the divergence happened within
      the previous checksum interval. Narrowing it to the exact tick means recording with an
      interval of 1.)*

### Phase 7: Later

- [ ] Snapshots and rollback: stop systems, restore world data, start systems. That's the same
      path Rule 7 and the save/load test already require. Restore `nextEntityId`, and leave
      `nextViewEntityId` running.
- [ ] Move sim systems and sim components into their own assembly (still one `World`)
      and turn on `Microsoft.CodeAnalysis.BannedApiAnalyzers` to ban `float`, `MathF`,
      `System.Random`, `DateTime`, `Stopwatch`, `Task` and `Parallel` at compile time.
- [ ] View interpolation polish: smooth sim positions with `FrameTime.alpha`.

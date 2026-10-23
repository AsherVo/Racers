# ECS

Racers runs its game in MintECS, a small entity-component-system in `src/Racers/Scripts/ECS`
(namespace `MintECS`). This document explains how it's put together and the rules game code follows.

Parts marked *(planned)* are agreed but not built yet. They're tracked in
[worklists/determinism.md](worklists/determinism.md). The engine side (fixed ticks, `FrameTime`) is in
[architecture.md](architecture.md#one-frame).

## The pieces

```
World
 ├─ entities     uint ids                       ┐
 ├─ components   data attached to entities      ├─ serialized: everything that persists
 ├─ tick, id counters, pending commands         ┘
 ├─ messages     short-lived events             ┐
 ├─ filters      live entity queries            ├─ not serialized: rebuilt or discarded
 └─ systems      the code that runs each tick   ┘
                 or frame
```

### World

`World` owns everything else. A game normally has one, and Racers will keep exactly one. The
simulation and the view share it, and the split between them is made inside it (see
[Sim and view](#sim-and-view)).

The world registers itself with the engine inspector in its constructor, so Compee's World pane
shows every live entity as a flat list, with its components and its relations' targets.

### Entities

An entity is a `uint`. `0` means "no entity".

- `Create()` hands out ids from a counter that only goes up, so an id is never reused. There are two
  counters, and the world picks one from the current phase (see [Entity ids](#entity-ids)).
  `Create( name, components... )` also adds a `Name`.
- `Destroy( entity )` removes all of its components. An entity with `Undestroyable` refuses.
- `Exists( entity )`. Dead ids stay in `entities` as `false` until enough pile up to compact.
- `GetName( entity )` gives `"Car-12"`, which is useful in logs. It works in any phase.

### Components

A component is a class deriving from `Component` that holds data. An entity has at most one
component of each type.

```C#
public class Position : Component
{
    public Vector2 value;
}
```

| Call | Does |
|---|---|
| `Add( entity, new T { ... } )` | Adds, replacing any existing `T`. Sim code adds this way. |
| `Add< T >( entity )` | Returns the existing `T` if there is one, otherwise adds a new one. |
| `Get< T >( entity )` / `TryGet` / `Has` | Reads. `Get` returns null if the component is missing. |
| `GetOrAdd< T >( entity )` | Same as `Add< T >( entity )`. |
| `Remove< T >( entity )` | Removes. |

Components are serialized with the world, so their fields must be types the YAML mapper supports
(see [Game data](architecture.md#mapping-objects)).

A component that implements the marker interface `ISimComponent` is sim state; every other component
is view state (see [Sim and view](#sim-and-view)). It's an interface rather than a base class so
relations can be sim components too:

```C#
public class Cell : Relation, ISimComponent
{
    public Vector2Int coords;
}
```

A sim component's fields may hold integers, bools, chars, strings, enums, `decimal`, and arrays,
`List`s, nullables and structs of those. `float`, `double`, `Vector2`, `Color`, `Dictionary`,
`HashSet`, `object`, interfaces and references to view components are errors.
`SimComponentRules` checks every sim component in the game assembly when the first `World` is
created, and any other the first time it's used, and throws a list of every bad field:

```
Sim components must be deterministic:
BadSim.position.x is a Single. Floats round differently across platforms; use int, long or Fixed.
BadSim.map is a Dictionary, which enumerates in hash order. Use a sorted list or an array.
```

### Relations

A relation is a component that points at another entity: `Relation : Component` with a
`uint relation` field. Because an entity has one component per type, it has at most one relation
of each type. Relations are many-to-one: many cells can point at one grid, but a cell points at
only one grid.

```C#
public class OnCanvas : Relation {}

SetRelation< OnCanvas >( sprite, canvas );
GetRelated< OnCanvas >( sprite );      // → canvas
GetEntities< OnCanvas >( canvas );     // → every entity on that canvas
```

`GetEntities` and `GetRelations` scan every relation of that type, so use a filter
(`Related< T >( target )`) for anything you query every frame.

A relation can carry data of its own: `Cell : Relation, ISimComponent` keeps its `coords` and points
at its `Grid`. `SetRelation< T >( from, to )` retargets an existing relation and keeps its data;
`SetRelation( from, to, new T { ... } )` replaces it.

### Messages

A message is a class deriving from `Message`. Messages are short-lived events. There are three kinds,
and each flows one way:

| Kind | Sent in | Read by | Lasts |
|---|---|---|---|
| **Command** (derives from `Command`) | the view, input handlers, loading | sim systems on the next tick, then the view | one tick, then the view's inbox |
| **Sim message** | a tick | later sim systems in the same tick, then the view | one tick, then the view's inbox |
| **View message** | the view, input handlers, loading | view systems | until the end of the next `Update` |

Commands carry input into the sim, and sim messages carry events ("car crashed") out of it. View
messages are the view talking to itself: input (`PointerClick`) and UI events (`StartHover`, `Click`).
They can't be commands, because UI has to keep working while the sim is paused (a pause menu's
"Resume" button), and cosmetic events don't belong in the replay.

- `Send( new Click { target = entity } )`
- `Read< T >()` returns the current messages of that type: in a tick, that tick's sim messages and
  commands; in the view, this frame's view messages plus every sim message from the ticks since the
  last frame. A frame can hold 0..N ticks, so the inbox is what keeps the view from dropping or
  repeating an event like "car crashed". `ReadSingle< T >()` returns the first one.
- At the end of `Update`, the view's messages and the inbox are cleared. Nothing reads messages from
  an earlier tick or frame.
- `Destroy( message )` consumes a message, so later systems won't see it. A sim message destroyed in
  the sim never reaches the view.

The sim can't read view messages at all: in a tick, `Read` only sees the sim's own store.

Messages are never serialized. Pending commands are (`pendingCommands` on `World`), so a command
sent just before a save still applies after the load.

### Commands

Commands are the only way input reaches the sim. Input arrives as view messages (`PointerClick`,
`KeyInput`), view systems work out what it means, and they send a command that says it in game terms:

```C#
public class SelectCell : Command
{
    public int x;
    public int y;
}

// GridViewSystem (view): a click on a cell becomes a command.
foreach ( var click in Read< Click >() )
{
    if ( Exists( click.target ) && TryGet( click.target, out Cell cell ) )
        Send( new SelectCell { x = cell.coords.x, y = cell.coords.y } );
}

// GridSystem (sim): the next tick applies it.
foreach ( var select in Read< SelectCell >() )
    ...
```

The world keeps the command in `pendingCommands` until the next `FixedTick`, which stamps it with
that tick (`command.tick`) and delivers it with the tick's sim messages, in the order the commands
were sent. Sending a command from the sim is an error.

Commands are sim input, so their fields follow the same rules as sim components: integers (cells,
lanes, sim entity ids), never floats or pointer positions. `SimComponentRules` checks each command
type at startup and the first time it's sent.

### Recording

`World.onCommandApplied` fires for each command as a tick applies it. `CommandRecorder` listens and
keeps every applied command, each stamped with its tick: with the content and seed, that's
everything needed to replay a match, and later the network protocol. The recorder lives outside the
world (`RacersGame` owns it), so it keeps recording while systems stop and start. After loading a
world, `Attach` it to the new one.

### Filters

A filter is a live query. You describe it with `FilterBuilder` and start it, and the world keeps
its `entities` set up to date as components and relations change.

```C#
filter = new FilterBuilder()
    .Include< SpriteAnimation >()
    .Include< DrawSprite >()
    .ToFilter();

Start( filter );

foreach ( var entity in filter.entities )
    ...
```

- `Include< T >`, `Exclude< T >`, `Related< T >( targets )`, `NotRelated< T >( targets )`.
- `onAdd` and `onRemove` fire as entities enter and leave. A callback can't change the filter
  that's calling it.
- `Start( filter )` builds the set from scratch, so a filter is always rebuilt when its system
  starts. Its order then has nothing to do with any earlier run (see Rule 6).
- A filter remembers the phase it was started in, and its callbacks always run in that phase. A view
  system's filter can fire during a tick (the sim spawns an entity with a `DrawRive`), and its
  callback still follows view rules: entities it creates get view ids, and it can't touch sim state.
- A sim system's filter can only name sim components.
- For one-off queries, use `GetEntitiesNonAlloc( builder, results )` and `GetSingleEntity( builder )`.

### Systems

A system is code. `World.Start( system )` calls `StartSystem( world )`, and `World.Stop` calls
`StopSystem( world )`. Sim systems' `StartSystem` and `StopSystem` run in the sim phase, and view and
draw systems' in the view phase, so the same rules apply as when they update. Systems run in the
order they were started. `World.Get< T >()` returns a running system, which is how systems call each
other's helpers (`Get< PrefabSystem >().CreateFromData( ... )`).

`WorldManipulator` is an optional base class that forwards `Create`, `Get`, `Send`, `Start` and so
on to its `world`, so system code can call them directly.

| Interface | Runs |
|---|---|
| `ISystem` | `StartSystem` / `StopSystem` only |
| `ISimSystem : ISystem` | `FixedUpdate( world )` on every fixed tick |
| `IViewSystem : ISystem` | `Update( world, time )` once per frame |
| `IDrawSystem` | `DrawSystem( world, batch )` once per frame, after updating |

A system is sim (`ISimSystem`) or view (`IViewSystem` and/or `IDrawSystem`), never both:
`World.Start` throws. A system with neither, like `PrefabSystem` or `TimeSystem`, is a **utility**:
it has helpers but doesn't run on its own, and its helpers follow the rules of whoever calls them.

Racers' systems:

| System | Kind | Notes |
|---|---|---|
| `TimeSystem` | utility | Fed by `World.Update`. See [Time](#time). |
| `PrefabSystem` | utility | Called from `Load` or a sim system, it creates sim ids; from the view, view ids. `AddFromData` dresses an existing entity. |
| `GridSystem` | sim | Owns the `Grid` entity and `cells[]` (a cache rebuilt from `Cell` relations). Grid position is the `InCell` relation from an entity to its cell; `GetContents`/`GetAll< T >` query it. |
| `RaceSystem` | sim | Applies `SelectCell`: moves a unit's `InCell` to the selected cell and sends `UnitMoved`. |
| `PointerSystem` | view | Hit-tests against texture alpha, Rive and float rects. Rebuilds its hover set from `HoverTarget.hovering` on start. |
| `ButtonSystem` | view | Fires Rive hover triggers. |
| `GridViewSystem` | view | Turns clicks on cells into `SelectCell` commands. Cell layout and visuals (`DressGrid`) and highlights (a cache rebuilt from `CellHighlight` relations). |
| `UnitViewSystem` | view | Animates units on `UnitMoved`. When not animating, derives each unit's `Position` from its `InCell` cell. |
| `SpriteAnimationSystem` | view | Follows `Time.Game`. |
| `CoroutineSystem` | view | Each coroutine follows its own `Time` type. Coroutines end on `StopSystem`. |
| `RenderSystem` | view + draw | Rive instances are rebuilt from `DrawRive` on start; their state machines reset after a load. |

## Sim and view

The game is split into a **sim**, which is deterministic and bit-identical on every platform, and a
**view**, which draws it and turns input into commands. Both live in the one world. Everything in it
is classified one way or the other:

| | Sim | View |
|---|---|---|
| **Systems** | `ISimSystem`, run by `World.FixedTick()` | `IViewSystem`, run by `World.Update( time )` |
| **Components** | Implement the marker `ISimComponent`. No floats, `Vector2`, `Color`, `Dictionary` or `HashSet` fields (checked at startup). | Everything else (`Position`, `Rect`, `Draw*`, `Clock`, ...) |
| **Entity ids** | From `nextEntityId` (1, 2, 3...): anything created in `Load`, `FixedTick`, or outside the view | From `nextViewEntityId`, starting at 1,000,000: anything created in `Update`, `Draw`, or input handlers |
| **Messages** | Sent during a tick, cleared each tick, and moved to the view's inbox | Sent outside a tick, cleared each frame |
| **Time** | `world.tick` | `TimeSystem.GetDelta( Time )` / `GetTotal( Time )` |

### Phases

`world.phase` says which rules apply right now:

| Phase | When | New entity ids | Messages go to |
|---|---|---|---|
| `Default` | Loading, and anything outside a tick or frame | sim range | view |
| `Sim` | `FixedTick()`, sim systems' start and stop | sim range | sim |
| `View` | `Update()`, `Draw()`, view systems' start and stop, `EnterViewPhase()` | view range | view |

Code that runs outside the world's own loop, like input handlers, enters the view phase explicitly:

```C#
protected override void OnPointer ( PointerEvent e )
{
    using var view = world.EnterViewPhase();
    world.Send( new PointerClick { position = e.position } );
}
```

### Entity ids

There is one `Create()`. The world picks the id counter from the current phase, so a view entity
created at frame-dependent times (a hover highlight, an explosion, a tooltip) can never shift a sim
entity's id. Without that, the same car would be entity 41 at 60 Hz and 47 at 120 Hz.

- Sim range: `nextEntityId`, 1, 2, 3... up to `VIEW_ENTITY_ID_START` (1,000,000). Only entities the
  sim needs belong here.
- View range: `nextViewEntityId`, from `VIEW_ENTITY_ID_START` up. `World.IsViewEntity( id )` checks.
  The inspector tags these entities `view`.
- Both counters are serialized. A rollback *(planned)* restores only `nextEntityId`.

Data flows one way:

```
input ──► view systems ──► commands ──► sim systems ──► sim components
                                                             │
view components ◄── view systems ◄── sim messages (inbox) ◄──┘
```

- Sim entities may carry view components: the car has a sim `InCell` and a view `DrawSprite`.
  View entities may not carry sim components; adding one throws in any phase and any build.
- The sim may add or remove view components (to spawn an entity with its visuals), but it never
  reads them. Adding with `Add( entity, new T() )` isn't a read; `Add< T >( entity )` is, because it
  returns an existing component.
- **Commands** are the only way input reaches the sim (see [Commands](#commands)).

### One frame

1. The engine runs 0..N fixed ticks, depending on how much app time has built up. For each one,
   `RacersGame.FixedUpdate` calls `world.FixedTick()` (a pause menu will skip it *(planned)*):
   - `world.tick++`
   - stamp pending commands with the new tick and deliver them as sim messages
   - run sim systems in start order
   - move the tick's sim messages to the view's inbox
2. `world.Update( time )`:
   - `TimeSystem.AdvanceFrame( time )` advances the clocks, if a `TimeSystem` is running
   - run view systems in start order
   - clear this frame's view messages and the inbox
3. `world.Draw( batch )` runs draw systems in the view phase.

### Time

- **Sim:** `world.tick` counts simulated ticks. It lives on `World` and is serialized and
  checksummed. Durations are tick counts: `world.TicksFromMilliseconds( 250 )` rounds up, so any
  nonzero wait lasts at least one tick, and `world.TicksFromSeconds( 2 )`. `world.ticksPerSecond`
  comes from `config.yaml`.
- **View:** each `Time` type has a clock entity with a `Clock` view component, which `TimeSystem`
  creates lazily during `Update`, so clocks always get view ids. Clocks are serialized, so totals
  survive a load.

  | `Time` | Delta from | Pauses |
  |---|---|---|
  | `Realtime` | `FrameTime.realDt` | never; includes time in the background |
  | `App` | `FrameTime.appDt` | while the app is in the background |
  | `Game` | `FrameTime.appDt` for now | will also pause with a pause menu *(planned)* |

  `TimeSystem` answers `GetDelta( Time )` (seconds this frame), `GetTotal( Time )` and
  `ticksThisFrame`. `Time.None` is an error, and so is calling them from the sim.
- Coroutines are view-only. Each one follows the `Time` type it was started with.

## Rules

Check new code against these. Rules 1-10 come from the determinism work. Rules 11-12 are
conventions that follow from them.

1. **The sim never sees real time.** Time in the sim is `world.tick`. Durations are tick counts.
2. **The sim never sees floats.** Use `int`/`long`, or `Fixed` once it exists. That covers `float`,
   `double`, `Vector2`, `MathF`, and `Math.Sin`/`Pow`/etc. Floats are fine in view code.
3. **Data flows one way.** The view reads sim state and never writes it. The sim never reads view
   state: no view components, no `graphics.*`, no Rive values or events.
4. **Input reaches the sim only as commands**, each stamped with the tick it applies on. Commands
   carry integers (cells, lanes), never raw pointer positions.
5. **Randomness comes only from the sim RNG**, whose state lives in a sim component. Never use
   `System.Random`. Cosmetic effects use a separate view RNG.
6. **Sim iteration order is explicit.** Iterate by sorted entity id. Never rely on
   `Dictionary`/`HashSet` order, `GetHashCode()` or `Type` order. Filters are rebuilt whenever a
   system starts, so their order after a load is unrelated to an uninterrupted run.
7. **Systems are disposable.** Systems are never serialized. Everything that persists lives in
   `World`, its entities and its components. A system's fields are caches: `StartSystem` rebuilds
   them from the world (which may have just been loaded mid-match) and `StopSystem` releases them.
   View systems may lose cosmetic state on a restart. Sim systems may lose nothing.
8. **No concurrency in the sim.** No threads, `Task`, `async` or `Parallel`.
9. **Coroutines are view-only and temporary.** They end on `StopSystem` and don't survive a load.
   Sim sequences are component state plus tick counts.
10. **No one reads messages across ticks or frames.** Sim messages last one tick (plus the view's
    inbox) and view messages one frame. Saves happen only between ticks.
11. **State that systems share lives on an entity, not in a system.** When a system needs
    world-level data, give it an entity. For example, a `Grid` entity holds `width`/`height`,
    cells relate to it, and `GridSystem` rebuilds its lookup arrays from those relations on start.
    Each `Time` type gets a clock entity instead of `TimeSystem` keeping totals.
12. **Prefer `Add( entity, new T() )` in sim code.** `Add< T >( entity )` returns an existing
    component, which counts as reading it.

In debug builds, guards catch breaches of Rules 3 and 10 while they happen, by throwing
`InvalidOperationException`:
- During a tick: `Get`/`Has`/`TryGet`/`GetRelations` of a view component, a filter or query that
  names one, `GetComponents`, and `TimeSystem` lookups. (Reading view messages can't
  happen: the sim only sees its own.)
- During the view phase: adding, removing or retargeting an `ISimComponent`, or destroying an entity
  that has one.
- `World.Checksum()` of sim state, taken when the view phase starts and again when it ends, catches
  the view writing to a sim field through a component reference.

In every build: adding a sim component to a view entity, sending a command from the sim, and
nesting `FixedTick`/`Update` inside another phase all throw.

`World.Checksum()` is a 64-bit FNV-1a hash of `tick`, `nextEntityId` and every sim component, by
entity id then type name, with fields in a fixed order. It's the same on every platform for the same
sim state.

## Writing a system

1. **Decide whether it's sim, view or utility.** If it touches floats, pixels, textures, Rive,
   input or wall-clock time, it's view.
2. **Make `StartSystem` work on any world:** fresh, loaded mid-match, or restarted. Start filters
   and rebuild caches there. Create missing world-level entities lazily, and in the view phase if
   they're view-only.
3. **Release everything in `StopSystem`:** stop filters, dispose native resources, null out
   `world`.
4. **Keep state in components.** If losing a field on restart would change the game, it belongs
   on an entity.
5. **Sim only:** iterate in sorted order *(planned API)*, read only `ISimComponent`s and commands,
   and time things with `world.tick`. Add components with `Add( entity, new T() )`.
6. **View only:** derive what you draw from sim state every frame (like `UnitViewSystem` turning
   `InCell` into `Position`) instead of caching it, and send commands for anything the sim should
   do.

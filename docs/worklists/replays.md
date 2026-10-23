# Replays

**Goal:** any match can be replayed tick-for-tick from a small file, and a player can send a bug
report with a replay that reproduces what they saw.

**Shape:** a replay is the determinism equation written to disk:

```
( build, content, race, seed, [ ( tick, command ), ... ], [ ( tick, checksum ), ... ] )
```

There are two layers:

- **Sim replay** (Phases 1–4): commands injected at their ticks. Deterministic, and works on any
  platform. Reproduces anything in the sim.
- **Input replay** (Phase 5): recorded frame times and raw input fed through the real view code,
  which turns them back into commands. Works only on the same platform and build. Reproduces UI and input
  bugs that never got as far as a command.

---

## Relationship to `determinism.md`

`determinism.md` makes the sim deterministic and proves it: checksums, the headless runner, the
save/load test, golden tests and desync reports. This worklist uses that work to record,
play back and report matches. It owns the replay file format and `ENGINE_REPLAY`, which moved here
from `determinism.md` Phase 6.

Dependencies on `determinism.md`:

| Here | Needs |
|---|---|
| Phase 1 seed in header | Phase 5 `DeterministicRandom` (record `0` until it exists) |
| Phase 2 desync reporting | Phase 6 desync report (built once and shared) |
| Phase 3 seek | Phase 6 save/load test passing, and Phase 7 restore path (stop systems, restore, start systems) |
| Phase 6 regression tests | Phase 6 headless runner |

Everything in `determinism.md`'s Rules applies here too.

---

## Rules

1. **A replay is valid only for its exact build and content.** Replays aren't migrated. Playing one on
   a different build is allowed (it's how bisecting works), but the player warns about the mismatch,
   and checksums decide whether the result means anything.
2. **Recording never changes the sim.** Recorders only watch. They never create entities, queue
   commands or touch sim components. Checksums are identical with and without a recorder attached.
3. **Recorded data is copied when it's recorded.** Sim systems read the live `Command` objects, so the
   recorder stores a copy (or the serialized YAML), never a reference.
4. **Reporting never crashes the game.** If writing a report fails, the failure is logged and the game
   keeps going. The crash handler can't throw.
5. **A report contains nothing personal** beyond what the player types into it.

---

## Worklist

### Phase 1: Replay file and recording

- [x] `RaceConfig` (content YAML, `races/<name>.yaml`, loaded with `Loader.Race`): grid size, unit
      placements and background. `RaceSystem.Setup( RaceConfig )` builds the match from it, so a
      replay doesn't depend on code that changes between builds.
- [ ] The replay header stores a full copy of the `RaceConfig`, not its path, so playback doesn't
      depend on the race file being unchanged.
- [ ] The seed comes from `RaceSave` (a sub-state of `GameSave`, loaded by `SaveController` in the
      toolbox, which doesn't exist yet). The recorder copies it into the replay header at race
      start, so playback doesn't depend on the player's save. Until `SaveController` exists, record
      `0`.
- [ ] Build id: embed the git commit (plus a dirty flag) at build time, e.g. through
      `SourceRevisionId` / `AssemblyInformationalVersion`. Expose it from the engine.
- [ ] Content hash: hash every file under the content root, with paths sorted ordinally, using the same
      FNV-1a as `World.Checksum()`. Compute it once at startup.
- [ ] `Replay` type (YAML, read and written with `Engine.Yaml`):
      - `header`: `build`, `dirty`, `contentHash`, `ticksPerSecond`, `race` (`RaceConfig`), `seed`,
        `platform`, `runtime`, `recordedAt`
      - `commands`: the `( tick, command )` list, using `Command`'s `[YamlPolymorphic]` tags
      - `checksums`: `( tick, checksum )` every `CHECKSUM_INTERVAL` ticks (start with
        `ticksPerSecond`), plus the final tick
- [ ] `CommandRecorder` copies each command as it's applied (Rule 3).
- [ ] Checksum recording: the recorder takes a checksum after every `CHECKSUM_INTERVAL`th tick. This needs
      a hook after `FixedTick` finishes, either a `World.onTickEnd` event or a call from
      `RacersGame.FixedUpdate`. Like the recorder, it lives outside the world.
- [ ] Recording starts at match start (tick 0, right after `RaceSystem.Setup`) and runs for the whole match.
      Commands are tiny, so there's no rolling buffer.
- [ ] `Replay.Write( path )` / `Replay.Read( path )`, plus a round-trip test.
- [ ] Debug key to save the current replay to disk, for testing before bug reports exist.

### Phase 2: Playback

- [ ] `ReplayPlayer`, which lives outside the world like the recorder: before each `FixedTick`, it queues the
      recorded commands for `world.tick + 1`, in recorded order. After the tick, it asserts each
      applied command's stamped tick matches the recording.
- [ ] While a player drives the world, commands queued by the view are dropped and logged once.
      View systems keep running, so hover and animations still work.
- [ ] After each recorded checksum tick, compare checksums. On the first mismatch, pause and
      produce the desync report (`determinism.md` Phase 6).
- [ ] `ENGINE_REPLAY=path` (same pattern as `ENGINE_CAPTURE` and `ENGINE_INSPECTOR`): `Load` builds the
      match from the replay's `race` instead of loading `races/test`, then attaches a player.
- [ ] On load, compare `build` and `contentHash` with the running game. Warn loudly on a mismatch
      and play anyway (Rule 1).
- [ ] At the end of the replay, pause on the last tick.
- [ ] Test: drive a match with scripted commands, record it, play it into a new world and compare
      every checksum. This shares setup with the "play twice" test in `determinism.md`.

### Phase 3: Playback controls (debug builds)

- [ ] Pause/resume and single-tick step. Pausing skips `world.FixedTick()` in
      `RacersGame.FixedUpdate`, which is how mid-game pause already works.
- [ ] Speed: ×0.25 to ×8. Slow-motion skips ticks; faster speeds run extra `world.FixedTick()` calls
      from `RacersGame.Update` before `world.Update`.
- [ ] Fast-forward to tick N: run ticks in a loop within a per-frame time budget, so the window keeps
      responding.
- [ ] Snapshots: during playback, keep a sim snapshot every `SNAPSHOT_INTERVAL` ticks (start with 10
      seconds' worth), in memory.
- [ ] Seek: restore the nearest earlier snapshot (stop systems, restore, start systems), then
      fast-forward. Seeking backward depends on the save/load test passing.
- [ ] Take over: detach the player at the current tick and resume live input. The recorder keeps
      going, so the session can be saved as a new replay whose header records the source replay
      and the take-over tick.
- [ ] Overlay on the debug canvas: current tick and total, speed, checksum status (matching,
      mismatch at tick N, no checksum yet), and key hints.
- [ ] Check that the inspector stays usable while paused at any tick.

### Phase 4: Bug reports

- [ ] Writable reports directory on each platform: `SDL_GetPrefPath` on desktop and mobile. In the
      browser, offer the bundle as a download.
- [ ] `Log` keeps a ring buffer of the last `LOG_TAIL_LINES` lines for reports.
- [ ] Bug report bundle (a zip, through `System.IO.Compression`):
      - `replay.yaml` (Phase 1)
      - `world.yaml`: the full world (sim and view) at report time, so the state can be inspected
        even if the replay won't play
      - `log.txt`: the log tail
      - `screenshot.png`: reuse `GameRunner.Capture`
      - `report.yaml`: build, content hash, platform, runtime (CoreCLR, NativeAOT, Mono wasm), window and
        pixel size, the tick at report time, and the player's note
- [ ] "Report bug" debug key. A menu entry with a note field comes once there's a pause menu.
- [ ] Crash capture: on an unhandled exception, write the bundle synchronously, with
      `crash.txt` (the exception and stack) instead of a screenshot. Never throw (Rule 4).
- [ ] On the next launch, find unsent crash bundles and offer to send them.
- [ ] Delivery: see **Open decisions**.
- [ ] Size check: log the bundle's size. If `world.yaml` gets large, cap or compress it.

### Phase 5: Input replay (same platform and build)

- [ ] Engine: `FrameRecorder` in `GameRunner` records each frame's `appElapsed`/`realElapsed` (integer
      nanoseconds), the number of ticks run, and every event delivered to the game (pointer, key,
      resize), in order and tagged with the frame index. It records from match start, like the
      command recorder.
- [ ] Engine: frame playback mode. `GameRunner` takes elapsed time and events from the recording
      instead of SDL, so `FixedTimestep` produces the same tick counts and `alpha`. Live input is
      ignored.
- [ ] Record the window size and pixel scale in the header. Playback forces them, or warns if it can't.
- [ ] Compare the commands the view regenerates with the recorded command stream, and report the
      first difference: that's where the view diverged.
- [ ] Add the frame stream to the bug report bundle as `frames.yaml` (or a compact binary format if
      YAML is too large).
- [ ] Document the limits: Rive state, platform input quirks and GPU differences can still diverge.
      Input replay is best effort.

### Phase 6: Regression and tooling

- [ ] `tests/Replays/`: checked-in replays from fixed bug reports, each with its expected checksum
      log, played by the headless runner in `dotnet test`.
- [ ] CLI (`tools/Cli`): `replay info <path>` prints the header and stats. `replay verify <path>` plays
      it headless and exits nonzero on the first checksum mismatch.
- [ ] Document bisecting: `git bisect run` with `replay verify` finds the commit that changed a
      replay's outcome.

---

## Open decisions

- **Delivery:** email attachment, an HTTP upload endpoint, or a Discord/Slack webhook. Local
  bundles plus "open the folder" is enough until there are outside players.
- **Replay compatibility across builds:** none for now (Rule 1). Revisit if player-shareable
  replays become a feature, which would need versioned commands.
- **Snapshot storage:** in-memory sim snapshots only, or also written into bug reports for
  faster seeking in long matches.

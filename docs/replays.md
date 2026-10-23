# Viewing replays

A replay is a recorded match: its setup, every command with the tick it applied on, and a checksum of the sim
every second. Playing one rebuilds the match from its setup and feeds it the same commands, so the sim reaches
the same state on every tick, on any platform. This document is for tools that launch the game to show a
replay, record one, or check one. How replays work inside the game is in [ECS.md](ECS.md#recording-and-replays),
and what's still planned is in [worklists/replays.md](worklists/replays.md).

## Watching a replay

Start the desktop game with `ENGINE_REPLAY` set to the replay file:

```sh
ENGINE_REPLAY=/abs/path/to/replay.yaml hosts/Desktop/bin/Debug/net10.0/Racers
# or, building first:
ENGINE_REPLAY=/abs/path/to/replay.yaml dotnet run --project hosts/Desktop
```

Use an absolute path. A relative one resolves against the game's working directory, and so does the desync
report written next to it. `ENGINE_REPLAY` combines with the other variables: set `ENGINE_INSPECTOR` too
and the World pane works during playback, exactly as in a live match.

What the game does:

1. Reads the replay. If it can't, it logs an error and starts a normal live match.
2. Builds the match from the replay's own race setup (`header.race`), not from `races/test.yaml`, so the race
   file can change without breaking old replays.
3. Plays in real time in the normal window. Fixed ticks run at the usual rate, and the view runs as normal: units
   animate, Rive plays, hover works. If the replay was recorded at a different tick rate from `config.yaml`, the
   sim uses the replay's rate and the playback speed differs; the outcome doesn't.
4. Ignores the player's game input. Clicks that would become commands (selecting a cell) are dropped, because
   live input would change the outcome. UI that doesn't reach the sim keeps working.
5. Checks the sim against every recorded checksum as it reaches that tick.
6. Stops ticking at the end of the replay, and on the first checksum that doesn't match. The window stays open,
   showing that tick, until it's closed.

There are no playback controls yet (pause, step, speed, seek). They're replays Phase 3.

## What the game reports

Progress is only reported through the game's log, which goes to stderr on desktop. Each line is prefixed by
the platform logger (a timestamp and process id on macOS), and errors also carry `ERROR:`. Match on the text
from `Replay:` onward:

| Line | Meaning |
|---|---|
| `Replay: playing <path> to tick <end>.` | The replay loaded and playback started. |
| `Replay: couldn't read <path>, so playing live.` | The file is missing or isn't a valid replay. The reason follows on the next line. The game runs a live match instead. |
| `Replay: recorded at <n> ticks per second, but config.yaml says <m>. ...` | Warning only. |
| `Replay: dropping commands from the view while the replay drives the sim.` | The player tried to play during the replay. Logged once. |
| `Replay: finished at tick <end>, <n> checksums matched.` | Reached the end with every checksum matching. Logged once. |
| `Replay: desync at tick <t>: expected <hex>, got <hex>. Pausing.` | The sim diverged from the recording at or before tick `<t>`. Playback stops. |
| `Replay: sim state at the desync written to <path>` | The desync report: `<replay path>.desync-<t>.yaml`. |
| `Replay: couldn't write the desync report to <path>: <reason>` | The report couldn't be written, usually because the replay's folder is read-only. |

A tool that wants to show "finished" or "desynced" can watch for those lines. The inspector protocol doesn't
carry replay state yet.

A desync means the code or content running now doesn't produce what the recording did: a sim change since it was
recorded, or a determinism bug. The desync report holds the sim state at that tick (`World.SaveSimState()`).
Diff it against the same tick from another run to find the difference. The checksum is only compared once per
recorded checksum, every second of match time, so the divergence happened at some point in the second before
`<t>`.

## Recording a replay

Press **F5** during a match. The game writes everything since the match started to
`<working directory>/replays/replay-<yyyyMMdd-HHmmss>.yaml` and logs:

```
Replay: saved ticks 0-<tick> to <path>
```

A tool controls where replays land by choosing the game's working directory. F5 also works while a replay
is playing, and saves a copy of what has played so far.

## Checking a replay without a window

The developer CLI plays replays headless, with the game's own sim setup and no window, as fast as it can.
It's the quick way to check a replay before showing it, or to check many:

```sh
tools/cli.sh replay info <file-or-directory>...                # header, length, command counts
tools/cli.sh replay verify <file-or-directory>... [--quiet]    # plays and checks every checksum
tools/cli.sh replay verify bug.yaml --dump /tmp/dumps          # also writes the sim state at a desync
tools/cli.sh replay rerecord <file-or-directory>...            # rewrites checksums after an intended sim change
```

`replay verify` prints one line per replay and exits 0 if every replay matched, 1 if any failed, and 2 for a
usage error:

```
ok    tests/Replays/test-race-moves.yaml (tick 300, 6/6 checksums)
FAIL  bug.yaml: desync at tick 120: expected D195817F0300C0D6, got D973F3D3C336E2CF (2/6 checksums)
1 replay verified: 1 failed.
```

`--content <dir>` builds matches from a different content folder. The default is the copy built next to the CLI.
Replays in `tests/Replays/` also run in `dotnet test`.

## The replay file

YAML, read and written by `Replay` (`src/Racers/Scripts/Data/Replay.cs`):

```yaml
header:
  version: 1                 # file format; the game refuses other versions
  ticksPerSecond: 60
  seed: 0                    # always 0 until the sim has randomness
  startTick: 0
  endTick: 300               # playback stops here
  race:                      # a full copy of the RaceConfig the match was built from
    width: 9
    height: 3
    units:
    - id: Car
      x: 2
      y: 2
    background: CloudyRoad
  platform: osx-arm64        # where it was recorded, for information only
  runtime: .NET 10.0.1
  recordedAt: 2026-10-23T00:00:00Z
commands:                    # in the order they applied
- type: SelectCell
  tick: 30
  x: 4
  y: 1
checksums:                   # World.Checksum() after each listed tick, including startTick and endTick
- tick: 0
  checksum: 2423094935105891561
- tick: 60
  checksum: 16910562738666960980
```

To show a replay's length before opening it, use `(endTick - startTick) / ticksPerSecond` seconds.

## Limits

- **Desktop only.** `ENGINE_REPLAY` is read on every platform, but only a desktop launch can set it.
- **Replays must start at tick 0.** Every replay the game records does. A replay that starts mid-match can't
  be played, because its starting state isn't in the file.
- **No build or content check.** A replay doesn't record which build or content made it yet, so playing an old
  replay on new code just desyncs, with no warning first. Replays are never migrated: re-record them, or
  `rerecord` them if the sim change was intended.
- **Rive and other view state isn't replayed.** Only the sim is. Animations come from the replayed sim, so they
  look the same, but anything driven by the player's own pointer (hover) doesn't.

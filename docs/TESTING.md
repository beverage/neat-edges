# TESTING

```bash
devtools/run-harness.sh              # alongside whatever is already running
devtools/run-harness.sh --full       # your own mod list, copied
devtools/run-harness.sh --exclusive  # refuse if any RimWorld is up
```

Builds Release, launches an isolated instance with `-quicktest
-neatedges-harness`, waits for it to run every case and quit itself, prints the
report, and exits non-zero if a case failed.

**Alongside is the default here**, unlike the other children — several mods in
this constellation get worked on at once, and refusing to start beside a running
instance makes the gate unrunnable most of the day. `--exclusive` is the right
mode for a final pre-release run.

**A game running beside the harness keeps the dll it loaded.** `Mods/NeatEdges`
is a symlink to this checkout, so that game loaded `Assemblies/NeatEdges.dll`
from here. Neither build writes that file: both compile into `dist/build/`, and
each result is copied in as `NeatEdges.dll.new` and renamed over the target. A
rename swaps the directory entry and nothing else, so a running game keeps the
file it opened and the next one to start loads the new one. On the way out the
script runs `check-shipped-dll.py` and lists `Assemblies/`, and warns if that
directory holds anything but the one shipping dll.

A bare `dotnet build` promises none of that, so check for a running game before
building straight into `Assemblies/`. On macOS with the .NET 9 SDK it happens to
leave a running game alone, because MSBuild's copy unlinks the old file and
clones in the new one (measured 2026-09-23). Where the runtime cannot clone, as
on a volume without clone support, it truncates the existing file and writes
into it.

## Alongside is not unattended

**When the new window appears, click it once.** An unfocused RimWorld window
does not composite, and `LongEventHandler` advances the loading screen off the
main-thread update, so a background instance never leaves the loading screen: it
stops around 49 log lines, sits near 0% CPU, and never recovers.

Already ruled out by test, so nobody repeats them: App Nap
(`NSAppSleepDisabled` is already 1 and it stalls anyway), display and system
sleep, fullscreen, and "a second instance exists" — a clean machine stalled
identically with nothing else running. Whether an off-screen-but-compositing
window is achievable is still open; until it is, this gate cannot run in CI.

The script bails at `STARTUP_GRACE` (120s) rather than burning the full timeout,
and distinguishes "never reached RimWorld's own startup" from "started but the
harness never ran" — those are different failures and one message for both once
misdirected an entire session.

## What the cases cover

Every rule bug this mod has had lived in `EdgeMaskAt`, which is a pure function
from marker layout to an 8-bit mask. That is decidable without rendering
anything, and each of these cost a game restart and a screenshot to find the
first time.

| Case | Pins |
|---|---|
| `transpiler.applied` | anchors found — and prints the report, so an ambiguous anchor is visible |
| `defs.*` | both markers exist |
| `mask.rotation.*` | `Rotation` names the hugged edge, no offset — guards the art relationship |
| `mask.single.three` | one edge hardens 3 directions |
| `mask.corner.five` | two adjacent harden 5 |
| `mask.runner.six` | two opposite harden 6 |
| `mask.allSides.eight` | all four harden 8 |
| `mask.twoSided` | the cell *across* the edge hardens too |
| `mask.cornerSeal` | the neighbour gets its shared corner and nothing else |
| `mask.stacking.equalsAllSides` | four stacked singles equal the all-sides def |

Each case clears its fixtures first. With two-sided hardening and corner
sealing, a stray marker reaches beyond its own cell, so leaked state would make
later cases depend on earlier ones.

## What it deliberately does not cover

**Whether the edges look right.** That is a taste question and stays a
screenshot. Use the compare toggle (debug menu → Neat Edges → Toggle hard edges)
to shoot both states from one camera position; it suppresses the rendering while
leaving the overlay showing where every marker is.

**An offline contact sheet**, of the kind Fine Establishments uses for its art.
Our output is vanilla's nine-vertex fan mesh, not our own textures, so drawing
it offline would mean reimplementing `Regenerate` — and the sheet would then
certify the model rather than the game. That is the trap of approximating engine
maths and then trusting the approximation.

**The IL anchors being semantically right.** The harness asserts the transpiler
*applied* and prints what it resolved, which would have made the wrong-store bug
obvious, but it cannot tell a correct anchor from a plausible wrong one. That
gap is why the anchor report includes the candidate count.

## Interop

`--full` is the only list that exercises the mods this one shares a method with.
**Dub's Paint Shop** transpiles the same `SectionLayer_Terrain.Regenerate`;
paint disappearing from every floor in the colony is the signature of a
composition failure, and is what happened when this was a prefix returning
false. There is no automated case for it yet — checking a painted floor still
has its colour after a `--full` run is the manual step, and it belongs in the
harness.

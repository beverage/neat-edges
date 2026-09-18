#!/usr/bin/env bash
#
# Run the Neat Edges regression harness end to end, in an ISOLATED game
# instance. Ported from shift-change's run-harness.sh — same isolation, same
# safety posture, same exit-code contract — with ONE deliberate difference:
# this one runs ALONGSIDE by default.
#
#   devtools/run-harness.sh              # beside whatever is already running
#   devtools/run-harness.sh --full       # your own mod list, copied
#   devtools/run-harness.sh --exclusive  # refuse if any RimWorld is up
#
# WHY ALONGSIDE IS THE DEFAULT HERE
#
# Several mods in this constellation get worked on at once — a colony session,
# a media session, and a harness run are routinely all wanted at the same time
# (decided 2026-09-04). Refusing to start beside them, which is what the
# other children do, makes the gate unrunnable most of the day. `--exclusive`
# is the opt-out, and it is the right mode for a final pre-release run where
# nothing should be competing for the machine.
#
# READ THIS BEFORE YOU WALK AWAY: ALONGSIDE IS NOT UNATTENDED.
#
# Four sessions went into this and the diagnosis is closed: an unfocused
# RimWorld window does not composite, and a background instance never gets past
# the loading screen. It stops at ~49 log lines, sits near 0% CPU, and never
# recovers — but only when the instance's Prefs.xml leaves runInBackground off,
# which is the default for a fresh -savedatafolder. This script now seeds it on,
# and an unfocused run loads normally. See the Prefs.xml block below.
#
# Ruled out, each by test, so nobody re-runs them: App Nap
# (NSAppSleepDisabled is already 1, it stalls anyway), display and system sleep
# (caffeinate -dimsu changed nothing), fullscreen (windowed prefs are seeded
# below and a stall followed anyway), and "a second instance exists" (a clean
# machine stalled identically with nothing else running).
#
# What that means in practice: nothing any more, on a seeded instance. Clicking
# the window was the workaround for two mods for six weeks; it is no longer
# needed. The bail-out still exists, still fires fast, and still says which
# failure it was.
#
# ISOLATION: -savedatafolder gives the test instance its own ModsConfig.xml,
# Saves/ and Prefs; -logfile moves Player.log. Nothing under
# ~/Library/Application Support/RimWorld or ~/Library/Logs is read or written
# (the live ModsConfig.xml is READ once, never written).
#
# IT NEVER TOUCHES A GAME IT DID NOT START. Every kill targets the pid this
# script launched. If you are about to reach for pkill to get past a message
# here: don't. That is somebody's colony with unsaved progress in it.
#
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
# Derived from $HOME, never written out. These were absolute paths carrying a
# home directory, in a public repository — and they pinned the script to
# exactly one machine. Override either for a non-default Steam library.
APP="${RIMWORLD_APP:-$HOME/Library/Application Support/Steam/steamapps/common/RimWorld/RimWorldMac.app}"
LIVE_CONFIG="${RIMWORLD_CONFIG:-$HOME/Library/Application Support/RimWorld/Config/ModsConfig.xml}"
TESTDATA="$REPO/dist/testdata"
LOG="$REPO/dist/harness.log"
PROC="RimWorld by Ludeon Studios"

# Generous, because it has to cover the slow case: a full mod list beside a
# live colony.
TIMEOUT=1200

# The stall budget, separate from TIMEOUT because the two failures are nothing
# alike. A game that has not reached its own startup by now is blocked, not
# slow — no stalled run has ever recovered — so waiting out TIMEOUT to learn
# that costs twenty minutes and teaches nothing.
#
# `with mods:` is Verse's own output, from both "Initializing new game with
# mods:" and "Loading game from file … with mods:", so it covers the -quicktest
# path and a save load. It is deliberately NOT `Loaded assemblies`: that line
# is printed by a MOD, is absent from a minimal list, and shipping it as the
# marker once reported a healthy run as a stall. A marker has to be verified
# against the list it will actually run against.
#
# Headroom note: a focused run reaches startup in ~60s, so 120 is not much
# slack if a human takes a moment to find the window.
#
# STARTUP_GRACE IS A FLOOR, NOT A DEADLINE. It was an elapsed-only deadline
# until now, and in that form it shoots healthy runs: a large mod list takes
# minutes just to reach `with mods:`, and the sibling mod's copy of this script
# killed a run that was 761 lines deep and still printing mod banners. The
# liveness signal is log GROWTH — a loading game writes constantly, and the
# stall writes nothing ever again — so the question is only asked once the log
# has also been completely silent for STALL_QUIET.
STARTUP_GRACE=120
STALL_QUIET=60

MINIMAL_MODS=(
  ludeon.rimworld
  brrainz.harmony
  mrbeverage.neatedges
)

FULL=0
EXCLUSIVE=0
for arg in "$@"
do
  case "$arg" in
    --full) FULL=1 ;;
    --exclusive) EXCLUSIVE=1 ;;
    *) printf 'unknown option: %s (--full | --exclusive)\n' "$arg" >&2; exit 2 ;;
  esac
done

die() { printf 'error: %s\n' "$1" >&2; exit 1; }

# WHAT THE STALL LOOKED LIKE, PRINTED RATHER THAN ASSERTED.
#
# Sampled BEFORE the kill, because `ps` needs the process alive and "blocked,
# not slow" is the entire diagnosis: a stalled instance sits near 0% CPU with
# real CPU time already banked. The frontmost reading is here because every
# previous record of this failure says "focus state unknown" — a run that
# stalls should say what had the foreground, without anyone having to recall it.
stall_evidence() {
  lines=$(wc -l < "$LOG" 2>/dev/null | tr -d ' ' || printf 0)
  cpu=$(ps -o %cpu=,time= -p "$GAME_PID" 2>/dev/null | sed 's/^ *//;s/  */ /g' \
    || printf '(process gone)')
  [ -n "$cpu" ] || cpu='(process gone)'
  front=$(osascript -e \
    'tell application "System Events" to get name of first process whose frontmost is true' \
    2>/dev/null || printf '(unavailable)')
  printf '       log lines:    %s\n' "$lines"
  printf '       cpu / time:   %s\n' "$cpu"
  printf '       frontmost:    %s\n' "$front"
  printf '       last 5 lines:\n'
  tail -n 5 "$LOG" 2>/dev/null | sed 's/^/         | /'
}

# --exclusive is the pre-release posture: nothing else competing, and no other
# window owning the foreground.
if pgrep -x "$PROC" >/dev/null && [ "$EXCLUSIVE" = "1" ]
then
  die "RimWorld is already running and --exclusive was passed.
       This script will not touch that instance. Quit it by hand, or drop
       --exclusive to start a second, fully isolated one beside it."
fi

# The dll the game loads is the one on disk, not the one in your editor.
#
# -p:Harness=true is what compiles the harness and -neatedges-harness back in.
# They are NOT in a shipping build (see the configuration table in the csproj),
# so plain Release would launch a game that ignores the flag and sits there
# until the timeout. Release codegen otherwise, exactly as shipped.
dotnet build "$REPO/Source/NeatEdges/NeatEdges.csproj" -c Release -p:Harness=true >/dev/null \
  || die "harness build failed — fix that first"

# Leave Assemblies/ holding the SHIPPING dll again, whatever happens below.
# The game's Mods entry is a symlink to this checkout, so an un-swept harness
# build is what the next play session loads and what a careless `git add`
# commits. Same philosophy as the csproj's CleanDevArtifacts target: hygiene by
# construction, not by memory. It never changes this script's exit status.
restore_shipping_dll() {
  dotnet build "$REPO/Source/NeatEdges/NeatEdges.csproj" -c Release >/dev/null \
    || printf 'WARNING: Assemblies/ still holds the HARNESS build. Rebuild with:
         dotnet build Source/NeatEdges/NeatEdges.csproj -c Release\n' >&2
}
trap restore_shipping_dll EXIT

# THE BUILD IS NOT THE THING THE GAME LOADS.
#
# -savedatafolder isolates Config, Saves and Prefs, but NOT the mod itself: the
# game reads Mods/NeatEdges out of the app bundle, and whatever that resolves to
# is what gets tested. A release-staging COPY left in place has silently shadowed
# a checkout before, and a green run asserted against pre-fix bits.
#
# Without this check the failure is a PASS, which is the worst shape a test
# result can take.
MODS_ENTRY="$APP/Mods/NeatEdges"
[ -e "$MODS_ENTRY" ] || die "no Mods/NeatEdges entry — the game cannot load this mod at all"
realpath_of() { python3 -c 'import os,sys; print(os.path.realpath(sys.argv[1]))' "$1"; }
ENTRY_REAL="$(realpath_of "$MODS_ENTRY")"
REPO_REAL="$(realpath_of "$REPO")"
if [ "$ENTRY_REAL" != "$REPO_REAL" ]
then
  die "the game would NOT load the build this script just made.

       built:  $REPO_REAL
       loads:  $ENTRY_REAL

       Point Mods/NeatEdges at the checkout under test and run again. If the
       entry is a real directory rather than a symlink, it is release-staging
       residue — park it, do not delete it, and restore the symlink."
fi
printf 'load path: %s\n' "$ENTRY_REAL"

rm -rf "$TESTDATA"
mkdir -p "$TESTDATA/Config"

if [ "$FULL" = "1" ]
then
  [ -f "$LIVE_CONFIG" ] || die "no live ModsConfig.xml to copy from"
  # Read-only copy. The live file is never written.
  cp "$LIVE_CONFIG" "$TESTDATA/Config/ModsConfig.xml"
  printf 'mod list: yours, copied (not swapped)\n'
else
  version="$(grep -m1 '<version>' "$LIVE_CONFIG" 2>/dev/null || printf '  <version>1.6.4871 rev597</version>')"
  {
    printf '<?xml version="1.0" ?>\n<ModsConfigData>\n'
    printf '%s\n  <activeMods>\n' "$version"
    for mod in "${MINIMAL_MODS[@]}"
    do
      printf '    <li>%s</li>\n' "$mod"
    done
    printf '  </activeMods>\n</ModsConfigData>\n'
  } > "$TESTDATA/Config/ModsConfig.xml"
  xmllint --noout "$TESTDATA/Config/ModsConfig.xml" || die "generated mod list is not well-formed"
  printf 'mod list: minimal (%s mods, isolated)\n' "${#MINIMAL_MODS[@]}"
fi

# Windowed and muted. A fresh -savedatafolder has no Prefs.xml, so the instance
# would start on RimWorld's defaults, and the default is fullscreen — which on
# macOS asks for a fullscreen space a second instance cannot have.
#
# THIS IS NOT THE STALL FIX. Seeding these prefs got one clean pass, then an
# identical stall on the next run, and the stall was later reproduced with them
# in place and nothing else running. Kept because windowed-and-muted is the
# right shape for a throwaway instance, not because it cures anything.
#
# runInBackground is one of RimWorld's own preferences (Verse/PrefsData.cs:68)
# and it DEFAULTS TO FALSE — the field has no initializer, and PrefsData.Apply()
# hands it to Unity as `Application.runInBackground` (:153). A fresh
# -savedatafolder therefore produced a test instance with it OFF while a real
# player's config may well have it on, which was an uncontrolled difference
# between the instance that stalls and the one that does not.
#
# MEASURED 2026-09-11, and it is the fix: 6 runs on the minimal list with focus
# deliberately held on another app throughout — True passed 3/3 (startup in
# 20s), False stalled 3/3 (log frozen at 48 lines, ~0.8% CPU, killed at 120s).
#
# It lands early enough to matter because Prefs.Init() ENDS with Apply()
# (Prefs.cs:861 — an unqualified call, and missing it is what produced a wrong
# prediction here three days earlier), and Prefs.Init() runs inside
# Root.CheckGlobalInit() (Root.cs:104). PrefsData.Apply() sets the flag (:153)
# and then changes the resolution (:154-161) — so the flag is set immediately
# before a Unity window reconfiguration, which is precisely the operation that
# needs a live update loop. Root.cs:72 sets the same flag true unconditionally
# and is TOO LATE: with the pref false, an unfocused instance never reaches it.
{
  printf '<?xml version="1.0" encoding="utf-8"?>\n<PrefsData>\n'
  printf '  <screenWidth>1280</screenWidth>\n'
  printf '  <screenHeight>720</screenHeight>\n'
  printf '  <fullscreen>False</fullscreen>\n'
  printf '  <runInBackground>True</runInBackground>\n'
  printf '  <volumeMaster>0</volumeMaster>\n'
  printf '</PrefsData>\n'
} > "$TESTDATA/Config/Prefs.xml"
xmllint --noout "$TESTDATA/Config/Prefs.xml" || die "generated Prefs.xml is not well-formed"

printf 'save data: %s\n' "$TESTDATA"

if [ "$EXCLUSIVE" = "0" ] && pgrep -x "$PROC" >/dev/null
then
  printf '\n  *** ANOTHER RIMWORLD IS RUNNING AND OWNS THE FOREGROUND. ***\n'
  printf '  *** CLICK THE NEW WINDOW ONCE WHEN IT APPEARS, or this run   ***\n'
  printf '  *** stalls at the loading screen and is killed in %ss.      ***\n\n' "$STARTUP_GRACE"
fi

printf 'launching…\n'

# The binary directly, not `open`: `open` returns before the child exists and
# gives no PID back, so the only way to wait would be "is ANY RimWorld running",
# which is precisely the check that cannot tell this instance from someone's
# colony. Launching it here makes $! ours, and ours alone.
"$APP/Contents/MacOS/$PROC" -quicktest -neatedges-harness \
  "-savedatafolder=$TESTDATA" -logfile "$LOG" >/dev/null 2>&1 &
GAME_PID=$!
printf 'pid: %s\n' "$GAME_PID"

elapsed=0
started=0
logsize=0
quiet=0
until [ "$elapsed" -ge "$TIMEOUT" ]
do
  sleep 5
  elapsed=$((elapsed + 5))

  # Growth, sampled every tick. A load that is progressing writes on every one
  # of these; the stall writes on none. This is the liveness signal — see
  # STALL_QUIET.
  size=$(wc -c < "$LOG" 2>/dev/null || printf 0)
  if [ "$size" -gt "$logsize" ]
  then
    logsize=$size
    quiet=0
  else
    quiet=$((quiet + 5))
  fi

  if [ "$started" -eq 0 ] && grep -q "with mods:" "$LOG" 2>/dev/null
  then
    started=1
    printf 'reached RimWorld startup after ~%ss\n' "$elapsed"
  fi

  # Both conditions are required. Past the floor AND silent — a big mod list is
  # slow, not stalled, and it goes on writing the whole time.
  if [ "$started" -eq 0 ] && [ "$elapsed" -ge "$STARTUP_GRACE" ] && [ "$quiet" -ge "$STALL_QUIET" ]
  then
    evidence="$(stall_evidence)"
    kill "$GAME_PID" 2>/dev/null || true

    # A GAME THROWING EVERY FRAME ALSO GOES SILENT, AND LOOKS IDENTICAL.
    #
    # RimWorld stops logging entirely after "Reached max messages limit", so a
    # per-frame exception storm presents to a liveness check exactly as a stall
    # does. Bailing out is right either way, but blaming the window for a mod
    # fault sends the next reader somewhere there is nothing to find.
    if grep -q "Reached max messages limit" "$LOG" 2>/dev/null
    then
      die "the log went silent because RimWorld STOPPED LOGGING, not because the
       game is blocked: it hit its own message cap, which is what happens when
       something throws every frame. That is a fault in the mod list under test.
       This is NOT the focus stall, whatever the timing looks like.

$evidence
       Log: $LOG"
    fi

    die "stalled before RimWorld started (log silent ${quiet}s, ${elapsed}s in).

       This is NOT a mod-wiring problem: Unity's preamble finished and the game
       stopped before loading any assembly. The known cause is the window never
       coming to the front. A near-zero CPU reading below confirms it is blocked
       rather than slow; a busy one means look elsewhere.

$evidence
       Bring the new RimWorld window to the front and run again. Running
       alongside cannot do that for you: the other instance owns the
       foreground. For an unattended run, quit the others and use --exclusive.

       Log: $LOG"
  fi

  kill -0 "$GAME_PID" 2>/dev/null || break
done

if kill -0 "$GAME_PID" 2>/dev/null
then
  kill "$GAME_PID" 2>/dev/null || true
  die "timed out after ${TIMEOUT}s; stopped pid $GAME_PID — check $LOG"
fi
printf 'game exited after ~%ss\n\n' "$elapsed"

[ -f "$LOG" ] || die "no log at $LOG — did -logfile take?"

# TWO FAILURES, TWO MESSAGES. A single line here once blamed mod wiring for a
# stall that happens before any mod loads, and misdirected a whole session.
grep -q "with mods:" "$LOG" \
  || die "the game exited without ever reaching RimWorld's own startup — the
       focus stall, not a wiring problem. See $LOG"
grep -q "harness auto-run" "$LOG" \
  || die "the game started but the harness never ran — is -neatedges-harness
       still wired up? See $LOG"

sed -n '/\[NeatEdges\] regression harness/,/harness auto-run/p' "$LOG"

grep -q "harness auto-run: PASSED" "$LOG"

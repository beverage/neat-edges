#!/usr/bin/env python3
"""Assert the built assembly is a shippable Release build.

    ./devtools/check-shipped-dll.py [path/to/NeatEdges.dll]

Ported from shift-change's and apparel-painter's gate of the same name
(2026-09-18), replacing the inline python step CI had carried since the harness
was evicted from the shipping build. Two properties, where the siblings have
three:

  1. NO HARNESS, and no `-neatedges-harness` launch flag. Dev tooling is not a
     player's to carry: the flag spawns fixtures and then quits the game. It is
     compiled out of Release entirely (NeatEdges.csproj's HARNESS switch), so
     this asserts an ABSENCE, and the absence is the whole point of the switch.

     It used to be the opposite check across the constellation — the harness was
     REQUIRED to ship, on the argument that a gate asserting against a build
     nobody installs asserts nothing. What replaced that argument: HARNESS is
     only ever a whole-file guard, so the harness build and the shipping build
     differ by the presence of Harness.cs's types and by nothing else.
     check-invariants.py enforces that half; this file enforces this one.

  2. The feature surface IS there. Absence checks pass trivially on an empty or
     truncated file, so something has to be asserted present. This mod is
     unusually exposed to the over-gating failure, because everything it does is
     INVISIBLE BY DESIGN: a dll that loads, patches nothing and hardens no edge
     looks exactly like a map with no markers on it.

The siblings' third property — NO SCENES fixtures — has no counterpart here.
This mod has two configurations and one switch (Debug, Release, Release+Harness)
and no SCENES build to mis-stage, because it never grew the destructive stage
builders: there is nothing to film that a marker and a screenshot do not cover.
If a SCENES configuration ever arrives, its stage types belong in
FORBIDDEN_TYPES on the day it is added, not on the day someone ships one.

--------------------------------------------------------------------------
THE TRAP THIS SCRIPT EXISTS TO AVOID

A .NET assembly keeps names in THREE places with DIFFERENT encodings:

    #Strings  type and member names       UTF-8
    #US       string literals in code     UTF-16
    #Blob     attribute ARGUMENTS         UTF-8, length-prefixed

So `grep -a HarnessBoot` works (a type name, UTF-8) while
`grep -a neatedges-harness` finds NOTHING WHATEVER SHIPS — it is a literal,
stored as UTF-16. A guard written the obvious way therefore passes forever and
asserts nothing about the flag, which is the one thing a player could actually
type. Key every check on a TYPE NAME where possible; when a literal is the only
handle, search utf-16-le explicitly.

Measured on this mod's own scratch builds, 2026-09-18:

    HarnessBoot            utf8=0  utf16=0   on Release
    HarnessBoot            utf8=1  utf16=0   on -p:Harness=true
    neatedges-harness      utf8=0  utf16=0   on Release
    neatedges-harness      utf8=0  utf16=2   on -p:Harness=true   <-- the trap
    DebugTools_NeatEdges   utf8=1  utf16=0   on BOTH — it ships, deliberately;
                                             see the FORBIDDEN_TYPES comment
--------------------------------------------------------------------------
"""

import os
import re
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
DEFAULT_DLL = os.path.join(ROOT, "Assemblies", "NeatEdges.dll")

# Type names, so UTF-8. Both live in Harness.cs, which is wrapped in
# #if HARNESS and must compile out under a plain Release build.
#
# DebugTools_NeatEdges IS NOT IN THIS LIST, and that is the one judgement in
# this file a porter would get wrong: the siblings forbid every DebugTools_*
# type they have, so the reflex is to add ours. Ours ships on purpose. The
# constellation's bar for the debug menu is DESTRUCTIVENESS, not reachability,
# and the toggle destroys nothing, persists nothing and is undone by pressing it
# again — it is the only way a player can answer "is this mod doing anything?"
# about a feature whose successful state is that you see no seam. Read the file
# header for the full argument; it also cannot be gated without changing the
# shape of shipping code, since Patch_SidedFadeBlock.MaskForCell reads its flag
# on every cell.
FORBIDDEN_TYPES = [
    "HarnessBoot",
    "HarnessDriver",
]

# A literal, so UTF-16 — the one place we cannot key on a type.
FORBIDDEN_LITERALS = [
    "neatedges-harness",
]

# The catch-all, and the only check here that covers code nobody has written
# yet: whatever a future harness file is called, it will contain this word
# somewhere. Measured on the shipping build, the word appears zero times in
# either encoding, so the bar is exact rather than a threshold. It is also what
# catches the GameObject name the driver allocates, which is a literal and not a
# type: "NeatEdgesHarnessDriver".
#
# If a player-facing string ever legitimately contains "harness", this fires,
# and the fix is to narrow THIS list, not to delete the check.
FORBIDDEN_TOKENS = [
    "harness",
]

# Present, or the absence checks above mean nothing. Each of these is a separate
# subsystem whose silent loss is invisible in game rather than loud:
#
#   Building_InvisibleEdge   the markers' thingClass, bound from XML by name
#   BlocksTerrainFade        the DefModExtension the markers carry, ditto
#   HarmonyInit              without it nothing below is ever patched in
#   Patch_SidedFadeBlock     the replacement Regenerate body — the whole feature
#   Patch_SidedFadeInvalidate  what keeps the mask cache honest as markers move
#   MapComponent_EdgeOverlay the placement overlay
#   Area_HardEdges           the painted area, named in every save that has one
#   Designator_AreaHardEdges*  its two tools, bound from Patches/ by name
#   Patch_AreaMigration      what loads Perspective: Paths' saved areas as ours
#
# The bound-by-name entries are the reason this list is not belt-and-braces: a
# missing thingClass or modExtension is a def-load error a player sees as two
# buildings that place and do nothing, a missing designator is a Floors tab
# without the paint tools, and the dll still loads fine in every case.
REQUIRED_TYPES = [
    "Building_InvisibleEdge",
    "BlocksTerrainFade",
    "HarmonyInit",
    "Patch_SidedFadeBlock",
    "Patch_SidedFadeInvalidate",
    "MapComponent_EdgeOverlay",
    "Area_HardEdges",
    "Designator_AreaHardEdgesExpand",
    "Designator_AreaHardEdgesClear",
    "Patch_AreaMigration",
]

failures = []


def fail(message):
    failures.append(message)


def utf8_count(blob, needle):
    return blob.count(needle.encode("utf-8"))


def utf16_count(blob, needle):
    return blob.count(needle.encode("utf-16-le"))


def token_hits(blob, token):
    """Every readable run containing the token, in both encodings.

    The UTF-16 pass strips the interleaved NULs first rather than searching for
    a decorated pattern, so a hit reports the same way whichever heap it came
    from.
    """
    pattern = re.compile(br"[\x20-\x7e]{0,40}" + re.escape(token.encode("utf-8"))
                         + br"[\x20-\x7e]{0,40}", re.IGNORECASE)
    hits = [match.decode("ascii", "replace") for match in pattern.findall(blob)]
    wide = blob.decode("utf-16-le", "ignore").encode("ascii", "ignore")
    hits += [match.decode("ascii", "replace") for match in pattern.findall(wide)]
    return hits


def main():
    path = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_DLL
    if not os.path.exists(path):
        print("FAIL no assembly at %s" % path)
        return 1
    blob = open(path, "rb").read()

    for name in FORBIDDEN_TYPES:
        if utf8_count(blob, name):
            fail("%s is present — this is a Debug or -p:Harness=true build. "
                 "Rebuild with plain -c Release, which also sweeps "
                 "Assemblies/." % name)

    for literal in FORBIDDEN_LITERALS:
        if utf16_count(blob, literal):
            fail("the \"%s\" launch flag is present (found in UTF-16, which is "
                 "where literals live) — dev tooling does not ship. Rebuild "
                 "with plain -c Release." % literal)

    for token in FORBIDDEN_TOKENS:
        hits = token_hits(blob, token)
        if hits:
            fail("the word \"%s\" survives in the assembly (%d place(s), e.g. "
                 "%r) — something dev-only is not behind #if HARNESS."
                 % (token, len(hits), hits[0]))

    for name in REQUIRED_TYPES:
        if not utf8_count(blob, name):
            fail("%s is MISSING — this assembly is not the mod. Something "
                 "over-gated it, or the build wrote somewhere else." % name)

    for failure in failures:
        print("FAIL %s" % failure)
    if failures:
        print("\n%d problem(s) in %s" % (len(failures), path))
        return 1
    print("shipped dll: no harness, no launch flag, feature surface intact")
    return 0


if __name__ == "__main__":
    sys.exit(main())

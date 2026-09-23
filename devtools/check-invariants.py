#!/usr/bin/env python3
"""Static checks that need no game, no build and no network.

    ./devtools/check-invariants.py

Run it before pushing; CI runs the same script, so a green run here is a green
run there. It reports every failure it finds, then exits non-zero.

Each check exists because something broke that way, or because the failure is
silent in game — which is worse, since the symptom then arrives as a player's
screenshot rather than a red line.

Ported from the sibling mod that already ran these. Two checks are adapted
rather than copied, because this repository has a different shape: there is no
keyed language file here yet, and no Workshop preview until first publish.
Both adapt by activating when the shape changes rather than by being deleted,
so neither can be added later without its check waking up.
"""

import os
import re
import sys
import xml.etree.ElementTree as ET

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SOURCE = os.path.join(ROOT, "Source")
KEYED = os.path.join(ROOT, "Languages", "English", "Keyed", "NeatEdges.xml")
PREVIEW = os.path.join(ROOT, "About", "Preview.png")

# The C# namespace XML binds to by name. One constant, because every binding
# check below keys off it and a namespace rename must not half-apply.
NAMESPACE = "NeatEdges"

# Steam rejects a Workshop preview over 1 MiB. The game does not check:
# Workshop.cs tests only that the file exists before handing it to
# SteamUGC.SetItemPreview, so an oversized one fails mid-publish as a bare
# result code, after the mod is already staged in Mods/.
PREVIEW_MAX = 1048576
PREVIEW_WARN = 1000000

# Keys we use that vanilla defines. Ours would all be NeatEdges.*; anything
# else here is deliberate reuse and must be justified in this list.
VANILLA_KEYS = set()

failures = []
notes = []


def fail(category, message):
    failures.append("%s: %s" % (category, message))


def cs_files():
    for base, _, names in os.walk(SOURCE):
        if "obj" in base or "bin" in base:
            continue
        for name in names:
            if name.endswith(".cs"):
                yield os.path.join(base, name)


def rel(path):
    return os.path.relpath(path, ROOT)


# ---------------------------------------------------------------- hot reload

# This codebase is internal-by-default: no `private` members, and no
# auto-properties.
#
# The rule comes from hot-swapping method bodies. A swapped body executes in a
# separate assembly and Unity's Mono honours only InternalsVisibleTo, so a
# `private` member — or an auto-property's compiler-generated backing field,
# which is private no matter what the property declares — throws
# FieldAccessException the first time a swapped body touches it. Both compile
# clean, so nothing says so until the swap runs.
#
# There is no hot-reload rig wired up in this mod today (see docs/DEVELOPMENT.md
# for why the cost is not repaid here). The convention is kept anyway, so that
# adding one is a tooling change rather than a 41-site refactor of shipped
# source, and so this mod reads like its siblings.
#
# These patterns replace two shell greps that were anchored to the start of a
# line and to a single line respectively, and so missed `static private int x;`,
# `[Attr] private int y;`, `class N { private int z; }`, and EVERY multi-line
# auto-property — the `{` and the `get;` are never on the same line in the style
# this codebase actually writes.
PRIVATE_RE = re.compile(
    r"(?:^|[{;]|\]\s*)\s*(?:(?:static|readonly|volatile|unsafe|new|sealed|override|virtual|extern|partial|async)\s+)*"
    r"private\s+(?!protected)",
    re.M,
)
AUTOPROP_RE = re.compile(r"\{[^{}]*\b(?:get|set)\s*;[^{}]*\}", re.S)


def check_hot_reload():
    for path in cs_files():
        text = open(path, encoding="utf-8").read()
        stripped = re.sub(r"//[^\n]*", "", text)
        stripped = re.sub(r"/\*.*?\*/", "", stripped, flags=re.S)
        for match in PRIVATE_RE.finditer(stripped):
            line = stripped.count("\n", 0, match.start()) + 1
            fail("hot-reload", "%s:%d private member — this codebase is "
                 "internal-by-default" % (rel(path), line))
        for match in AUTOPROP_RE.finditer(stripped):
            body = match.group(0)
            # An expression-bodied or full property is fine; only `get;`/`set;`
            # with no body is an auto-property.
            if re.search(r"\b(?:get|set)\s*;", body) and "=>" not in body:
                line = stripped.count("\n", 0, match.start()) + 1
                fail("hot-reload", "%s:%d auto-property — its backing field is "
                     "private beyond reach of the IVT grants" % (rel(path), line))


# ------------------------------------------------------------ translation keys

# LoadedLanguage.TryGetTextFromKey sets translated = key and returns false with
# no log line, so a key that exists in code but not in the XML renders as the
# raw key on the player's screen and nothing anywhere says so.
#
# This mod ships no Languages/ tree yet — its player-facing strings are all in
# defs. So the check is conditional rather than absent: the moment someone
# writes a .Translate() call, the missing keyed file becomes the failure it
# would be in any other repo, instead of this check quietly not applying.
def check_translation_keys():
    used = {}
    for path in cs_files():
        text = open(path, encoding="utf-8").read()
        for match in re.finditer(r'"([A-Za-z0-9_.]+)"\s*\.Translate\s*\(', text):
            used.setdefault(match.group(1), rel(path))

    if not os.path.exists(KEYED):
        if used:
            where = sorted(set(used.values()))
            fail("keys", "%s calls .Translate() but there is no keyed file at "
                 "%s — every key renders as its own raw text on screen, silently"
                 % (", ".join(where), rel(KEYED)))
        return

    defined = set()
    for child in ET.parse(KEYED).getroot():
        if isinstance(child.tag, str):
            defined.add(child.tag)

    for key, where in sorted(used.items()):
        if key in defined or key in VANILLA_KEYS:
            continue
        fail("keys", "%s uses \"%s\" — not in %s, and not an allowed vanilla key"
             % (where, key, rel(KEYED)))

    for key in sorted(defined - set(used) - VANILLA_KEYS):
        fail("keys", "%s defines \"%s\" — nothing uses it" % (rel(KEYED), key))


# ---------------------------------------------------------------- XML bindings

# XML names C# types as strings. Rename the type and the def keeps the old
# name: the comp never attaches, the mod loads without error, the edges look
# entirely normal, and nothing happens. AGENTS.md notes these names are scribed
# into saves, which makes renaming them a deliberate multi-file act — exactly
# the edit that drops one.
def check_xml_bindings():
    declared = set()
    for path in cs_files():
        text = open(path, encoding="utf-8").read()
        for match in re.finditer(r"\b(?:class|struct)\s+([A-Za-z0-9_]+)", text):
            declared.add(match.group(1))

    # Only where XML actually names a TYPE: a Class="" attribute, an element
    # whose name ends in Class (compClass, thingClass, graphicClass), or a bare
    # list entry holding a namespaced name, which is how a type list such as
    # specialDesignatorClasses is written. All three are live here — <thingClass>
    # on the buildings, Class="" on the modExtension, and <li> in
    # Patches/NeatEdges_Designators.xml — so no part of this pattern is
    # speculative. The last one fails quietest of all: a renamed designator just
    # leaves the Floors tab without its tools.
    binding = re.compile(
        r'(?:Class\s*=\s*"%s\.([A-Za-z0-9_]+)"'
        r'|<[A-Za-z0-9_]*[Cc]lass>\s*%s\.([A-Za-z0-9_]+)\s*</'
        r'|<li>\s*%s\.([A-Za-z0-9_]+)\s*</li>)' % (NAMESPACE, NAMESPACE, NAMESPACE)
    )
    for base, _, names in os.walk(ROOT):
        if any(part in base for part in (".git", "obj", "bin", "dist", "Languages")):
            continue
        for name in names:
            if not name.endswith(".xml"):
                continue
            path = os.path.join(base, name)
            try:
                text = open(path, encoding="utf-8").read()
            except OSError:
                continue
            for match in binding.finditer(text):
                named = match.group(1) or match.group(2) or match.group(3)
                if named not in declared:
                    line = text.count("\n", 0, match.start()) + 1
                    fail("bindings", "%s:%d names %s.%s — no such type in Source/"
                         % (rel(path), line, NAMESPACE, named))


# -------------------------------------------------------------------- preview

# Absent is a note, not a failure: this mod is pre-release and has no Workshop
# item yet. Oversized is a failure whenever the file exists, because that one
# only ever surfaces mid-publish.
def check_preview():
    if not os.path.exists(PREVIEW):
        notes.append("no About/Preview.png — fine until first publish, and "
                     "required at it: Steam takes a 640x360 preview and "
                     "rejects one over %d bytes." % PREVIEW_MAX)
        return
    size = os.path.getsize(PREVIEW)
    if size > PREVIEW_MAX:
        fail("preview", "About/Preview.png is %d bytes, over Steam's %d limit"
             % (size, PREVIEW_MAX))
    elif size > PREVIEW_WARN:
        notes.append("About/Preview.png is %d bytes — %.1f%% of Steam's %d "
                     "limit, so any re-export is likely to cross it"
                     % (size, 100.0 * size / PREVIEW_MAX, PREVIEW_MAX))


# ------------------------------------------------------------ private tracking

# This repository is public. The backlog and decision log that drive it are not,
# and their identifiers mean nothing to a reader here — a bare "as the tracker
# decided" reference in a source comment points at a document nobody outside
# can open, and leaks how the work is organised into a mod's shipped source.
#
# No literal identifier appears in this file, deliberately: it is on the skip
# list below, so an example written here would be the one place the check
# cannot see.
#
# Write the REASON in the comment instead; the tracking item is where the
# argument lives, not what a stranger reading the code needs.
#
# This mod is why the check is here rather than only in its sibling: a tracker
# id and the internal role word both reached devtools/run-harness.sh, were
# stripped by hand, and nothing stopped them coming back (2026-09-08). Eleven
# more sites across source, docs and the agent brief turned up when this ran
# for the first time. Everything below the repo root is scanned except this
# script and the untracked agent context file.
TRACKING_ID = re.compile(r"\b(?:BL|DEC)-\d{3}\b")
TRACKING_SKIP_DIRS = {".git", "obj", "bin", "dist", "node_modules"}
# CLAUDE.md is gitignored — an untracked, machine-local agent file, so it is
# not a publishing surface. AGENTS.md is checked in, and is.
TRACKING_SKIP_FILES = {"check-invariants.py", "CLAUDE.md"}
TRACKING_EXTS = (".cs", ".py", ".sh", ".md", ".xml", ".yml", ".yaml", ".csproj", ".bbcode")


def scanned_files():
    for base, dirs, names in os.walk(ROOT):
        dirs[:] = [d for d in dirs if d not in TRACKING_SKIP_DIRS]
        for name in names:
            if name in TRACKING_SKIP_FILES or not name.endswith(TRACKING_EXTS):
                continue
            path = os.path.join(base, name)
            try:
                text = open(path, encoding="utf-8").read()
            except (UnicodeDecodeError, OSError):
                continue
            yield path, text


def check_private_tracking_refs():
    for path, text in scanned_files():
        for number, line in enumerate(text.splitlines(), 1):
            match = TRACKING_ID.search(line)
            if match:
                fail("tracking-ref",
                     "%s:%d references %s — the tracker is private; state the "
                     "reason in the comment instead"
                     % (rel(path), number, match.group(0)))


# ------------------------------------------------------------- home-dir paths

# An absolute home path in a tracked file does two bad things at once: it names
# the author in a PUBLIC repository, and it pins the script to exactly one
# machine. Both were true of run-harness.sh, which hardcoded the Steam app and
# the live ModsConfig.xml. They are now $HOME-derived with RIMWORLD_APP and
# RIMWORLD_CONFIG overrides, which is the shape this check exists to push
# toward.
#
# `$HOME`, `~` and `os.path.expanduser` are all fine and deliberately NOT
# matched. Only a literal absolute home directory is rejected. Linux and macOS
# shapes both, so a contributor on either produces the same failure.
HOME_PATH = re.compile(r"/(?:Users|home)/[A-Za-z0-9._-]+/")

# Vocabulary from the private working notes that means nothing to a reader of
# this repository. "principal" was the worst of it — it names an internal role
# rather than saying the useful part, which is that the choice was deliberate
# and when. "(decided DATE)", "(confirmed DATE)" and "(observed in play)" all
# carry the same weight to someone who has never seen the tracker.
INTERNAL_VOCAB = re.compile(r"\bprincipals?\b", re.IGNORECASE)


def check_home_paths():
    for path, text in scanned_files():
        for number, line in enumerate(text.splitlines(), 1):
            match = INTERNAL_VOCAB.search(line)
            if match:
                fail("internal-vocab",
                     "%s:%d says %s — that is private working vocabulary. Say "
                     "what a stranger needs instead: '(decided YYYY-MM-DD)' "
                     "for a deliberate choice."
                     % (rel(path), number, match.group(0)))
            match = HOME_PATH.search(line)
            if match:
                fail("home-path",
                     "%s:%d hardcodes %s — this repository is public, and the "
                     "path pins the script to one machine. Use $HOME, ~ or "
                     "os.path.expanduser, with an env override where the "
                     "location can differ."
                     % (rel(path), number, match.group(0)))


# ----------------------------------------------------------------- patch roots

# ModContentPack.LoadPatches discards EVERY operation in a file whose root is
# not exactly <Patch>, with one log line as the only symptom. The engine walks
# Patches/ with SearchOption.AllDirectories, so this must recurse too.
#
# This mod ships no Patches/ today — it reaches other mods' terrain through
# Harmony, not PatchOperations. The walk is kept because the first compat patch
# is exactly the moment the trap is live and nobody is thinking about it.
def check_patch_roots():
    patches = os.path.join(ROOT, "Patches")
    if not os.path.isdir(patches):
        return
    for base, _, names in os.walk(patches):
        for name in names:
            if not name.endswith(".xml"):
                continue
            path = os.path.join(base, name)
            root = ET.parse(path).getroot().tag
            if root != "Patch":
                fail("patch-root", "%s root is <%s> — must be <Patch>, or every "
                     "operation in the file is silently discarded" % (rel(path), root))


# -------------------------------------------------------------- harness gating

# The regression harness and its launch flag are dev tooling and do not ship:
# the csproj defines HARNESS in every configuration EXCEPT a plain Release
# build, so `-c Release` compiles the whole thing away and a player's install
# has no such flag in it. That switch is worth exactly as much as two
# properties of the source, and neither is visible at a glance:
#
#   1. Harness code is actually behind the guard. A new harness file nobody
#      wrapped compiles into the shipping dll and puts a launch flag in a
#      player's install, which is the entire thing this prevents.
#
#   2. The guard is WHOLE-FILE, never inline. That is what makes a harness
#      build and a shipping build differ by the presence of whole types and by
#      nothing else — no shipping code path changes shape between the build the
#      harness asserts against and the build that goes out. Lose it and a
#      harness run stops being evidence about the shipped assembly, which was
#      the whole argument for shipping the harness in the first place, back
#      when it did.
#
# check-shipped-dll.py asserts the outcome on the artifact; this asserts the
# shape of the source, which is where the mistake is actually made. Neither
# subsumes the other: a self-contained harness file named something else
# entirely passes rule 1 here and is caught there by the "harness" token sweep.
def check_harness_gating():
    for path in cs_files():
        text = open(path, encoding="utf-8").read()
        lines = text.split("\n")
        guards = [i for i, line in enumerate(lines)
                  if line.startswith("#if") and "HARNESS" in line]
        named_harness = "Harness" in os.path.basename(path) or "/Harness/" in path

        if not guards:
            if named_harness:
                fail("harness", "%s is harness code and carries no `#if HARNESS` "
                     "— it would compile into the shipping dll" % rel(path))
            elif "CommandLineArgPassed" in text:
                fail("harness", "%s reads a launch flag outside `#if HARNESS` — "
                     "dev-only flags are not a player's to carry" % rel(path))
            continue

        if len(guards) > 1:
            fail("harness", "%s has %d `#if HARNESS` directives — the guard is "
                 "whole-file, exactly one per file" % (rel(path), len(guards)))
        first_code = next((i for i, line in enumerate(lines)
                           if line.strip() and not line.lstrip().startswith("//")),
                          None)
        if first_code != guards[0]:
            fail("harness", "%s:%d `#if HARNESS` is not the file's first code "
                 "line — an inline guard makes the shipping build differ from "
                 "the harness build in shape, not just in contents"
                 % (rel(path), guards[0] + 1))
        body = [line for line in lines if line.strip()]
        if not body or body[-1].strip() != "#endif":
            fail("harness", "%s does not close with `#endif` as its last line — "
                 "the guard has to cover the whole file" % rel(path))
        if any(line.strip() == "#else" for line in lines):
            fail("harness", "%s has an `#else` under its HARNESS guard — that is "
                 "shipping code living inside a harness file; move it out"
                 % rel(path))


def main():
    check_hot_reload()
    check_translation_keys()
    check_xml_bindings()
    check_preview()
    check_patch_roots()
    check_private_tracking_refs()
    check_home_paths()
    check_harness_gating()

    for note in notes:
        print("note: %s" % note)
    for failure in failures:
        print("FAIL %s" % failure)
    if failures:
        print("\n%d problem(s)" % len(failures))
        return 1
    print("static invariants: all clear")
    return 0


if __name__ == "__main__":
    sys.exit(main())

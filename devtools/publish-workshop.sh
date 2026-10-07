#!/usr/bin/env bash
#
# Stage the mod for a Steam Workshop upload, and swap it into the game's Mods
# folder in place of the development symlink. Ported from apparel-painter's
# publish-workshop.sh (itself shift-change's): same verbs, same safety
# posture, this mod's names and file set.
#
# WHY THIS EXISTS
#
# RimWorld's in-game uploader publishes the mod's folder verbatim. Workshop.cs
# hands `hook.Directory.FullName` straight to `SteamUGC.SetItemContent`, that
# resolves to `ModMetaData.GetWorkshopUploadDirectory()`, and that returns
# `RootDir` with no filtering: the one hook that could strip anything,
# `PrepareForWorkshopUpload()`, has an empty body. `CanToUploadToWorkshop()`
# also requires the mod to sit in `Mods/`.
#
# `Mods/NeatEdges` is a symlink to the working repository. Uploading through it
# would publish `Source/`, `media/`, `devtools/`, `dist/`, `.DS_Store` and the
# entire `.git` directory to every subscriber; a sibling mod shipped 61 MB
# exactly that way, twice, on 2026-08-22. This script stages the release file
# set and uploads from that instead.
#
# USAGE
#
#   devtools/publish-workshop.sh            # stage + install, the safe default
#   devtools/publish-workshop.sh stage      # build + assemble dist/NeatEdges
#   devtools/publish-workshop.sh install    # swap it into Mods/, dev link aside
#   devtools/publish-workshop.sh strip      # with the game OPEN: drop the .dds it wrote
#   devtools/publish-workshop.sh restore    # dev link back, recover the item id
#
# The normal run is (no argument) -> launch the game -> strip -> upload in
# game -> restore.
#
# THE GAME ADDS FILES TO THE UPLOAD AFTER STAGING. Faster Game Loading writes a
# .dds beside every texture in every mod folder while the game starts, so the
# installed copy grows between install and upload: v1.0.0's first upload went
# out at 2.9 MB instead of the 744 KB staged, with 28 .dds files in it. The
# game reads a .dds in preference to the PNG beside it, so subscribers would
# draw from those copies, and an update that changed a PNG would leave its old
# .dds drawing the old art. `strip` runs while the game is open, after its
# textures have loaded, and removes them just before the upload.
#
# STAGING ALONE CHANGES NOTHING ABOUT WHAT THE GAME UPLOADS. `stage` only fills
# dist/; until `install` has replaced the dev symlink, the uploader still
# publishes the whole repository through it. That is why the bare invocation
# does both, and why `stage` says so on its way out.
#
set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
DIST="$REPO/dist/NeatEdges"
MODS="${RIMWORLD_APP:-$HOME/Library/Application Support/Steam/steamapps/common/RimWorld/RimWorldMac.app}/Mods"
LIVE="$MODS/NeatEdges"

# The release allowlist: what the game loads, plus the human-readable
# documents. Never media/, Source/, devtools/ or dist/.
CONTENT=(About Assemblies Defs Languages Patches Textures docs LICENSE README.md)

die() { printf 'error: %s\n' "$1" >&2; exit 1; }

# install and restore both replace the path a running game loaded the mod
# from, and the game rewrites ModsConfig.xml on exit over whatever the swap
# did. Same refusal as run-harness.sh: quit first.
ensure_game_closed() {
  if pgrep -x "RimWorld by Ludeon Studios" >/dev/null; then
    die "RimWorld is running; quit it before touching Mods/"
  fi
}

cmd_stage() {
  command -v dotnet >/dev/null || die "dotnet not on PATH"

  # Release, always, and never -p:Harness=true: a harness build writes the
  # same Assemblies/ path and carries the harness and its launch flag.
  dotnet build "$REPO/Source/NeatEdges/NeatEdges.csproj" -c Release

  rm -rf "$DIST"
  mkdir -p "$DIST"
  for item in "${CONTENT[@]}"; do
    [ -e "$REPO/$item" ] || die "missing from the repo: $item"
    cp -R "$REPO/$item" "$DIST/"
  done

  # Steam identifies the item by a file the game writes INSIDE the upload root
  # after a successful publish. If a previous publish left one in the repo, it
  # has to travel with the staged copy, or the uploader takes the create
  # branch and mints a second, duplicate listing.
  if [ -f "$REPO/About/PublishedFileId.txt" ]; then
    printf 'carrying existing item id: %s\n' "$(cat "$REPO/About/PublishedFileId.txt")"
  else
    printf 'no PublishedFileId.txt: the uploader will CREATE a Workshop item.\n'
    printf 'On the FIRST publish that is exactly right (the file appears after\n'
    printf 'the upload; restore recovers it, then commit it). On any LATER\n'
    printf 'publish it means the item id was lost: stop and recover it from\n'
    printf 'git history before uploading, or a duplicate listing is minted.\n'
  fi

  # Belt and braces. A stray dll under Assemblies/ is the one thing that
  # reaches the game's load path, and a .dds beside a PNG silently shadows it
  # in game, no timestamp check; texture tools on this machine write .dds into
  # the repo through the dev symlink. Neither ever ships.
  find "$DIST" -name '.DS_Store' -delete
  find "$DIST" -name '*.dds' -delete
  local strays
  strays="$(find "$DIST/Assemblies" -type f ! -name 'NeatEdges.dll' | wc -l | tr -d ' ')"
  [ "$strays" = "0" ] || die "Assemblies/ holds $strays file(s) besides NeatEdges.dll"

  # What is about to go out, checked as the bytes about to go out: no harness,
  # no -neatedges-harness launch flag, feature surface intact.
  python3 "$REPO/devtools/check-shipped-dll.py" "$DIST/Assemblies/NeatEdges.dll" \
    || die "the staged dll is not shippable; see above, and do not upload it"

  printf '\nstaged %s\n' "$DIST"
  du -sh "$DIST"
  printf '\ncontents:\n'
  ls -1 "$DIST"

  if [ -L "$LIVE" ]; then
    printf '\n!! STAGED ONLY. %s is still the dev symlink, so an upload now\n' "$LIVE"
    printf '!! would publish the whole repository. Run: %s install\n' "$0"
  fi
}

cmd_install() {
  ensure_game_closed
  [ -d "$DIST" ] || die "nothing staged; run 'stage' first"
  [ -d "$MODS" ] || die "game Mods folder not found: $MODS"

  # Two folders sharing packageId MrBeverage.NeatEdges would both appear in
  # the mod list, and ModLister's first-wins race would decide which one the
  # uploader publishes. A dot prefix does NOT hide a directory from the game
  # (ModLister enumerates GetDirectories() unfiltered), so the dev symlink
  # cannot be parked anywhere inside Mods/. Delete it instead; restore
  # recreates it with one ln -s.
  if [ -L "$LIVE" ]; then
    rm "$LIVE"
    printf 'removed the dev symlink (restore recreates it)\n'
  elif [ -d "$LIVE" ]; then
    die "$LIVE is a real directory, not the dev symlink; resolve by hand"
  fi

  cp -R "$DIST" "$LIVE"

  # The confirmation Steam never gives you: no manifest, no size and no file
  # list before publishing.
  printf '\n=== this is what the game will upload ===\n'
  printf 'from:      %s\n' "$LIVE"
  if [ -f "$LIVE/About/PublishedFileId.txt" ]; then
    printf 'item id:   %s (updates the existing item)\n' "$(cat "$LIVE/About/PublishedFileId.txt")"
  else
    printf 'item id:   NONE: the uploader will CREATE the Workshop item.\n'
    printf '           Expected on the FIRST publish and on no other: if this\n'
    printf '           mod is already on the Workshop, STOP; uploading now\n'
    printf '           mints a second, duplicate listing.\n'
  fi
  printf 'size:      %s\n' "$(du -sh "$LIVE" | cut -f1)"
  printf 'top level: %s\n' "$(ls -1 "$LIVE" | tr '\n' ' ')"
  printf '=========================================\n'
  cat <<'EOF'

Now, in game (launch fresh; Development mode must be ON in Options, or the
upload entry does not exist at all):

  1. Enable Harmony and Neat Edges in the mod list and confirm it loads
     clean: exactly ONE Neat Edges entry, no "same packageId multiple times"
     error, and the startup line "[NeatEdges] terrain edge patch applied ...
     trims kept out of the texture atlas".
  2. TERMINAL, with the game still open at the main menu:
     devtools/publish-workshop.sh strip
     It removes the .dds files the game wrote into the copy while starting,
     and must report the staged size again before you upload.
  3. Select it -> More actions -> Upload to Steam Workshop -> Confirm. Leave
     "Tag as translation" UNTICKED, or the item drops out of the default mod
     browse.
  4. Wait for the progress dialog to finish and the item page to open. Then
     quit the game.

THEN, IMMEDIATELY, IN THIS ORDER:

  1. BROWSER: set the item's visibility to PRIVATE and confirm it by eye. The
     game never sets visibility, so until checked the item sits in whatever
     state Steam defaulted it to.

  2. BROWSER: confirm the item actually published: open its page from a
     private window. The game reports success even if the Workshop Legal
     Agreement was never accepted.

  3. TERMINAL: devtools/publish-workshop.sh restore
     Then commit About/PublishedFileId.txt: it is the only record of which
     Workshop item this mod is, and skipping restore leaves the game loading
     a frozen snapshot instead of the checkout.

  4. BROWSER: paste media/steam-description.bbcode into the description.
     RimWorld sets the description only when it CREATES the item, from
     About.xml, so the page opens showing the in-game blurb and no later
     update replaces it. Validate first if it changed:
     devtools/bbcode-preview.py reports tags and the 8,000-character margin.
     Its banners load from github.com/beverage/neat-edges, which must stay
     public.

  5. BROWSER: add the gallery images through the item page's owner controls:
     media/cards/card-bedrooms.png, card-church.png, card-lab.png,
     card-garden.png and card-bar.png. The preview needs nothing: the game
     uploads About/Preview.png, which is the store card's GIF under the PNG
     name, and the Workshop page plays it.

  6. VERIFY AS A SUBSCRIBER, still private: subscribe in the browser, launch
     the game, and enable the STEAM-sourced Neat Edges entry (with the dev
     symlink back, both copies appear; leave the local one disabled). Confirm
     a clean load, the hard edge tools on the Floors and Zone tabs, a trim
     placed and hardening its edge, and no red errors. Quit, unsubscribe,
     re-enable the local copy.

  7. Only then set visibility to PUBLIC.

  8. REPO: add the live Workshop link to README.md and commit it with
     PublishedFileId.txt.
EOF
}

cmd_strip() {
  # Deliberately no ensure_game_closed: this runs with the game open, after it
  # has loaded every texture, so the files it removes are no longer read.
  [ -d "$LIVE" ] && [ ! -L "$LIVE" ] || die "$LIVE is not an installed copy; run install first"
  local count
  count="$(find "$LIVE" \( -name '*.dds' -o -name '.DS_Store' \) | wc -l | tr -d ' ')"
  find "$LIVE" \( -name '*.dds' -o -name '.DS_Store' \) -delete
  printf 'removed %s file(s) the game wrote into the upload\n' "$count"
  printf 'size:      %s (staged: %s)\n' "$(du -sh "$LIVE" | cut -f1)" "$(du -sh "$DIST" | cut -f1)"
  printf 'top level: %s\n' "$(ls -1 "$LIVE" | tr '\n' ' ')"
  # Compared against the staged copy file by file, so anything else the game
  # added between install and upload shows up here too. The item id is the
  # one file the uploader itself writes, so it is the one difference allowed.
  local drift
  drift="$(diff -rq "$DIST" "$LIVE" | grep -v 'PublishedFileId.txt' || true)"
  [ -z "$drift" ] || die "the installed copy differs from the staged one; do not upload:
$drift"
  printf 'matches the staged copy: ready to upload\n'
}

cmd_restore() {
  ensure_game_closed
  # The uploader wrote the new item id into the staged copy. Losing it means
  # the next upload creates a duplicate listing instead of updating this one.
  # Guarded on the staged copy actually being present: with the dev symlink
  # already back there is nothing to recover, and a repeat run is a no-op.
  if [ ! -L "$LIVE" ] && [ -f "$LIVE/About/PublishedFileId.txt" ]; then
    local id
    id="$(cat "$LIVE/About/PublishedFileId.txt")"
    if [ -f "$REPO/About/PublishedFileId.txt" ] \
       && ! diff -q "$LIVE/About/PublishedFileId.txt" "$REPO/About/PublishedFileId.txt" >/dev/null; then
      die "item id changed ($id); a duplicate listing was probably created; resolve by hand"
    fi
    cp "$LIVE/About/PublishedFileId.txt" "$REPO/About/"
    printf 'recovered item id %s into the repo: COMMIT IT\n' "$id"
  elif [ ! -L "$LIVE" ] && [ -d "$LIVE" ]; then
    printf 'no PublishedFileId.txt in the uploaded copy (upload not run, or it failed)\n'
  fi

  if [ -d "$LIVE" ] && [ ! -L "$LIVE" ]; then
    rm -rf "$LIVE"
  fi
  ln -sfn "$REPO" "$LIVE"
  printf 'dev symlink in place\n'
}

case "${1:-publish}" in
  publish) cmd_stage; cmd_install ;;
  stage)   cmd_stage ;;
  install) cmd_install ;;
  strip)   cmd_strip ;;
  restore) cmd_restore ;;
  *)       die "unknown command: $1 (publish | stage | install | strip | restore)" ;;
esac

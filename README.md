# Neat Edges

Stop soil and water washing a ragged fringe across your floors.

RimWorld fades every terrain one tile into whatever it touches, and the
receiving tile gets no say in it. That is why a clean stone terrace meeting open
ground always looks like it is dissolving at the rim. Bridges are the one thing
in the game that escape it, and only because they are bridges.

Neat Edges gives you that clean line anywhere.

## What you get

Everything is on the **Floors** tab of the Architect menu:

- **Hard edge** hardens one edge of a tile. Rotate it to pick the edge, and
  stack several on one tile for any combination.
- **Expand hard edge area** and **Clear hard edge area** do whole tiles, every
  edge at once, painted by dragging the way you paint a home area. Painting is
  free and instant, with no build order and no pawn involved.

Nothing is drawn on the map. The floor you already laid meets the boundary
cleanly instead of being washed over.

**Corners close themselves.** Where two hardened edges meet, the corner between
them is sealed too, and a run ends crisply instead of the fringe curling around
its last tile. A painted tile gets the same treatment on every side: terrain
stops fading into it, and it stops fading out onto its neighbours.

**The markers cost no materials**, since they change how an edge draws and
nothing else. Each still takes a moment of work, so a mis-dragged run can be
cancelled like any other order.

**An overlay toggle** on the bottom-right row shows every marker and the painted
area. An invisible thing you cannot find is an invisible thing you cannot
remove.

## Coming from Perspective: Paths

Remove it and load your save. The areas you painted with it load as Neat Edges'
hard edge area, and a message confirms it once the game is up. Both mods can
stay installed while you switch: Perspective: Paths' own areas keep working
until you remove it.

One visible difference: Perspective: Paths stopped terrain fading into a painted
tile, but still let the painted tile's own terrain fade out onto unpainted
neighbours. Neat Edges stops both, so edges around painted areas come out a
little crisper.

The painted area follows Owlchemist's Perspective: Paths, which solved the
whole-tile case first.

## Compatibility

Works with any floor from any mod, alongside vanilla bridges, and with Dub's
Paint Shop colours intact. Requires
[Harmony](https://steamcommunity.com/workshop/filedetails/?id=2009463077).

You can add it to a running save. Removing it drops the markers and the painted
area: RimWorld logs an error for each missing type on the next load, and your
floors and terrain are untouched.

If you also run **Fine Establishments**, its floor border strips harden the edge
they hug automatically while this is installed.

## Licence

MIT. See [LICENSE](LICENSE).

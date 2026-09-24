# Neat Edges

Stop soil and water washing a ragged fringe across your floors.

RimWorld fades every terrain one tile into whatever it touches, and the
receiving tile gets no say in it. That is why a clean stone terrace meeting open
ground always looks like it is dissolving at the rim. Bridges are the one thing
in the game that escape it, and only because they are bridges.

Neat Edges gives you that clean line anywhere.

## What you get

All of it is in the Architect menu:

- **Hard edge**, on the **Floors** tab, hardens one edge of a tile. Rotate it to
  pick the edge, and stack several on one tile for any combination.
- **Expand hard edge area** and **Clear hard edge area**, on the **Zone** tab
  beside the home area, do whole tiles, every edge at once, painted by dragging
  the way you paint a home area. Painting is free and instant, with no build
  order and no pawn involved.
- **Floor border**, **floor border corner** and **floor runner border**, on the
  **Floors** tab, are visible trim: strips set into the floor along one edge, around a corner, or
  down both sides of a corridor. Built from wood, stone or metal, they take
  paint, and each hardens the edges it covers.

The marker and the area draw nothing on the map. The floor you already laid
meets the boundary cleanly instead of being washed over.

**Corners close themselves.** Where two hardened edges meet, the corner between
them is sealed too, and a run ends crisply instead of the fringe curling around
its last tile. A painted tile gets the same treatment on every side: terrain
stops fading into it, and it stops fading out onto its neighbours.

**The marker costs no materials**, since it changes how an edge draws and
nothing else. Each still takes a moment of work, so a mis-dragged run can be
cancelled like any other order. The trims cost their material, like any other
building.

**An overlay toggle** on the bottom-right row shows every marker and the painted
area. An invisible thing you cannot find is an invisible thing you cannot
remove.

## With Perspective: Paths

**Using both?** While Perspective: Paths is installed, Neat Edges leaves
whole-tile painting to it. The two hard edge area tools are hidden, and its own
tool on the Zone tab does that job. The marker and the trims work alongside it,
because they harden one side of an edge, which its areas cannot. If a map
already has a Neat Edges hard edge area, it moves into Perspective: Paths' area
when the save loads, and a message says so.

**Switching over?** Remove it and load your save. The areas you painted with it
load as Neat Edges' hard edge area, and a message confirms it once the game is
up. The tools are on the Zone tab, where its were.

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

You can add it to a running save. Removing it drops the markers, the trims and
the painted area: RimWorld logs an error for each missing type on the next load,
and your floors and terrain are untouched.

Other mods can make their own decorations harden edges too, through the same
extension the trims carry, with no dependency on this one beyond a `MayRequire`.

## Licence

MIT. See [LICENSE](LICENSE).

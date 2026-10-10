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
- **Floor borders**, on the **Floors** tab, are visible trim set into the
  floor, in four styles: the floor border, grooved with lit lips; the inlay, one
  fine line set in from the edge; the vine, carved along the edge; and pebbles
  in grout, for paths outdoors. Each style comes in seven shapes: a strip along
  one edge, a corner, a runner down both sides of a corridor, an end cap closing
  off a runner or a one-tile path, a frame around a single tile, an inside
  corner, and a diagonal. Built from wood, stone or metal (pebbles from stone),
  they take paint, and each hardens the edges it covers. On the vine, paint
  colours only the vine itself, its leaves and stem, so a wooden vine painted
  green is a green vine on a wooden border.

The marker and the area draw nothing on the map. The floor you already laid
meets the boundary cleanly instead of being washed over.

**Each shape is one button.** Left-click it to build the style it shows, or
right-click it for the other styles; the one you pick stays on the button. Where a
border turns around an inside corner, such as a wall jutting into the room, the
inside corner piece fills the notch the two strips leave between them; where
runners cross, the middle tile takes four, one in each corner. The vine
and the pebbles come in four patterns, and every tile of border draws one by
where it lies, so a long path's edge never repeats on a beat.

**Diagonal walls get a diagonal border.** Build it on the wall's own tile, under
the wall, and rotate it until the band sits on the side you want, in the room or
out. It joins whatever border ends beside it: square along a run of diagonals,
and mitred where the line turns into a straight or another diagonal, as at the
corners of an octagonal room. It hardens its whole tile, which on a wall built
as two staggered rows is what stops the ground under the outer row creeping
into the room. Built and tested with Diagonal Walls 2.

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
up. The tools are on the Zone tab, where its were. Removing it on its own
is not safe: it adds its zone to every map, painted or not, the game gives no
way to delete it, and a save that still holds one loses the map's whole area
list, home area included. Neat Edges loads those zones as its own instead.

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

You can add it to a running save. Before removing it, use **Clear hard edge
area** on every map you painted: a save that still holds the area loses the
map's whole area list, home area included. Clearing the last painted tile
removes the area itself. Markers and trims can stay; RimWorld drops each one
with a load error, and your floors and terrain are untouched.

## For other mods

Other mods can use two parts of Neat Edges: the extension that hardens edges,
with art of their own, and the renderer that draws trims from one strip.

### Hardening edges

Any def can harden edges by carrying the extension the trims carry. Gate it
with `MayRequire` and your mod has no dependency on this one: without Neat
Edges the line is dropped at load, and the def works as before with soft edges.

```xml
<modExtensions>
  <li Class="NeatEdges.BlocksTerrainFade" MayRequire="MrBeverage.NeatEdges">
    <edges>
      <li>0</li>
    </edges>
  </li>
</modExtensions>
```

`edges` counts quarter-turns clockwise from the thing's rotation, so one def
covers all four facings: `0` is the edge the thing faces, `0` and `1` an L,
`0` and `2` two opposite edges. Leave the list empty, or out, to harden all
four. Any spawned thing on a tile counts, and the terrain redraws when one is
placed or removed. This has worked since Neat Edges 1.0.0.

### Trims drawn from a strip

Since Neat Edges 1.1.0, a mod can draw trims the way Neat Edges draws its own:
one strip texture laid out as geometry, rather than a texture for every shape
and facing. Give each shape its own def, with:

- `NeatEdges.Graphic_StripTrim` as its graphic class, and the strip as its
  texture;
- a `NeatEdges.TrimPiece` extension naming the shape: `Straight`, `Corner`,
  `InsideCorner`, `Runner`, `EndCap`, `Frame` or `Diagonal`;
- a `uiIconPath`, or its build button shows the strip.

The graphic class lives in Neat Edges' assembly, so these defs cannot load
without it. Put `MayRequire="MrBeverage.NeatEdges"` on each trim def, and on
any abstract parent of them, instead of on the extension. The game then skips
them whenever Neat Edges is missing, so your trims are absent rather than
broken. Gated on the extension alone, it logs "Could not find a type named
NeatEdges.Graphic_StripTrim" and loads each trim with no graphic. Copy the
building fields of `NE_TrimBase` in
`Defs/ThingDefs_Buildings/NeatEdges_Trims.xml` rather than inheriting it: a def
inheriting another mod's abstract parent needs `loadAfter` on that mod, and the
parents are not part of what this README promises to keep.

A strip is bands stacked top to bottom, each a quarter tile deep: the band as it
sits on a north edge, then the band on a south edge, then, if the extension sets
`bands` to 3, a band lit from the side, which a diagonal draws when its wall
runs north-west to south-east. With two bands, that diagonal draws the north
one. Along the band the strip repeats at whatever length keeps its texels
square, once a tile for `Textures/NeatEdges/Trim/FloorBorderStrip.png` at
256 × 192. Set `variants` and the strip's width splits into that many patterns,
each beginning and ending alike; every stretch of band one pattern long draws
one of them, picked by where it lies. Set `paintOverlay` and the strip is twice
as tall: the bands fill its top half, and the bottom half holds the same layout
mirrored, so its last row is the north band's outer edge. A trim draws the
bottom half over the top, and paint colours only the bottom half; the top keeps
the material's colour. The vine keeps its leaves and stem there. A piece's own
arms meet in a 45 degree mitre, and a diagonal cuts its ends to meet the trims
beside it. Built trims that share a strip draw in one call per map section,
whatever their material or paint. Draw the strip in greys: a built trim
multiplies it by its material's colour, or by the paint's.

To harden the edges a trim covers, give it `BlocksTerrainFade` as well, with
the lists Neat Edges' own trims carry:

| Shape | `edges` |
|---|---|
| `Straight` | `0` |
| `Corner` | `0`, `1` |
| `Runner` | `0`, `2` |
| `EndCap` | `3`, `0`, `1` |
| `Frame` | empty, for all four |
| `Diagonal` | empty, for its whole tile |
| `InsideCorner` | none: leave the extension off |

The inside corner covers a corner of its tile, not an edge, and the strips
beside it already seal that corner, so an empty list there would harden a tile
it only touches.

A trim that sets `canGenerateDefaultDesignator` to false joins the button for
its shape in its build category, as one more style on that button's right-click
menu, so a mod adding a style adds no buttons. Give it a `designatorDropdown`
group with `includeEyeDropperTool` set as well: on its Floors tab, Better
Architect Menu lists a button's trims one by one when none of them has such a
group. A def gated on Neat Edges can name Neat Edges' own, `NE_TrimStraights`
to `NE_TrimDiagonals`. Give it a `uiOrder` above 2096, where Neat Edges' trims
end: a button shows its lowest-ordered style and takes that style's place on
the tab, so a lower number puts your style on the button's face and moves the
button.

A corner, put together:

```xml
<ThingDef ParentName="BuildingBase" MayRequire="MrBeverage.NeatEdges">
  <defName>MyMod_TrimCorner</defName>
  <label>my trim corner</label>
  <description>Two border strips joined in an L around one corner of a tile.</description>
  <graphicData>
    <texPath>MyMod/Trims/MyTrimStrip</texPath>
    <graphicClass>NeatEdges.Graphic_StripTrim</graphicClass>
  </graphicData>
  <uiIconPath>MyMod/Trims/MyTrimCorner_MenuIcon</uiIconPath>
  <!-- NE_TrimBase's building fields, and your own costs and stats -->
  <designationCategory>Floors</designationCategory>
  <canGenerateDefaultDesignator>false</canGenerateDefaultDesignator>
  <designatorDropdown>NE_TrimCorners</designatorDropdown>
  <uiOrder>2100</uiOrder>
  <modExtensions>
    <li Class="NeatEdges.TrimPiece">
      <shape>Corner</shape>
      <bands>3</bands>
    </li>
    <li Class="NeatEdges.BlocksTerrainFade">
      <edges>
        <li>0</li>
        <li>1</li>
      </edges>
    </li>
  </modExtensions>
</ThingDef>
```

Every def with a `TrimPiece` and a `thingClass` of `Building`, or any other
`ThingWithComps`, is given the component that redraws its neighbours when it
is built or removed, so a diagonal meets your trims the way it meets these.

## Status

Live on the [Steam Workshop](https://steamcommunity.com/sharedfiles/filedetails/?id=3815430443).

## Licence

MIT. See [LICENSE](LICENSE).

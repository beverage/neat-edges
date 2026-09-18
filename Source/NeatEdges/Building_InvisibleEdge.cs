using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace NeatEdges
{
    /// <summary>
    /// A hard edge with no appearance on the map — but a normal one everywhere
    /// the player needs to see it.
    ///
    /// The first attempt was a fully transparent texture. That does make it
    /// invisible, and it also makes it unplaceable: the placement ghost, the
    /// rotation cue and the selected thing all draw from the same graphic, so
    /// the player had nothing to aim with (observed in play).
    ///
    /// So the def keeps a REAL graphic — ghost, menu icon and rotation preview
    /// all work as they do for the visible strips — and only the map drawing is
    /// suppressed, by no-opping Print. Buildings at MapMeshOnly are printed into
    /// the section mesh through this call, while `GhostDrawer` and the icon take
    /// entirely different paths and are untouched.
    ///
    /// Preferred over `drawerType None`: no vanilla building uses that value, so
    /// its behaviour is unverified, and it would suppress the ghost too.
    ///
    /// Selection is handled separately below, because brackets alone around an
    /// empty tile do not read as "there is a thing here".
    /// </summary>
    public class Building_InvisibleEdge : Building
    {
        /// <summary>How far into the tile the highlight reaches, in cells.</summary>
        internal const float BandDepth = 0.25f;

        /// <summary>Sub-bands used to fake the gradient. More is smoother.</summary>
        internal const int Steps = 8;

        internal const float PeakAlpha = 1f;

        internal static Material[] bandMats;

        /// <summary>
        /// One material per sub-band, opaque at the boundary and fading inward.
        /// `SimpleSolidColorMaterial` caches by Color32, so these are allocated
        /// once for the whole game — vanilla uses the same helper for its own
        /// translucent overlays (AimPieMaterial, CellBoolDrawer).
        /// </summary>
        internal static Material[] BandMats
        {
            get
            {
                if (bandMats != null) return bandMats;

                bandMats = new Material[Steps];
                for (int i = 0; i < Steps; i++)
                {
                    float t = (i + 0.5f) / Steps;

                    // 1 - t^2, not (1 - t)^2. The squared-decay version read as
                    // "very very subtle" in play: it put the only bright strip
                    // in the outermost ~3 pixels and dropped to a tenth of that
                    // by the halfway mark. This holds close to full alpha across
                    // the first half of the band and falls away at the inner
                    // boundary, so the band reads as a solid edge that softens
                    // rather than a bright hairline with a faint tail.
                    float a = PeakAlpha * (1f - t * t);

                    bandMats[i] = SolidColorMaterials.SimpleSolidColorMaterial(
                        new Color(1f, 1f, 1f, a));
                }
                return bandMats;
            }
        }

        /// <summary>
        /// Nothing on the map mesh. This is the whole point of the def: the
        /// floor beneath should read to the tile boundary with nothing over it.
        /// </summary>
        public override void Print(SectionLayer layer)
        {
        }

        /// <summary>
        /// Selecting it marks the EDGES it hardens, not the tile it sits on.
        /// Outlining the whole cell was the first attempt and it overstated the
        /// thing: this is a kerb along one boundary, and a full square reads as
        /// "the tile is affected" (observed in play).
        ///
        /// Edges come from the same <see cref="BlocksTerrainFade"/> data the
        /// renderer uses, so a piece that ever hardens more than one edge draws
        /// all of them without this needing to know which def it is.
        ///
        /// The overlay toggle on the bottom-right row remains the way to FIND
        /// one; this is only confirmation once clicked.
        /// </summary>
        public override void DrawExtraSelectionOverlays()
        {
            base.DrawExtraSelectionOverlays();

            List<int> edges = def?.GetModExtension<BlocksTerrainFade>()?.edges;
            float y = AltitudeLayer.MetaOverlays.AltitudeFor();

            if (edges.NullOrEmpty())
            {
                for (int r = 0; r < 4; r++) DrawEdge(r, y);
                return;
            }

            for (int i = 0; i < edges.Count; i++)
            {
                DrawEdge((Rotation.AsInt + edges[i]) % 4, y);
            }
        }

        /// <summary>
        /// A band hugging one edge, brightest at the boundary and fading into
        /// the tile. Rot4 order: 0 = N, 1 = E, 2 = S, 3 = W.
        ///
        /// Drawn as <see cref="Steps"/> stacked strips rather than one
        /// gradient-textured quad: `DrawLineBetween` already builds a correctly
        /// oriented quad of a given width, so stacking translucent strips gets a
        /// gradient with no mesh building, no shader assumptions and no custom
        /// texture. At six steps the banding is not visible at map zoom.
        /// </summary>
        internal void DrawEdge(int dir, float y)
        {
            // Position is the cell's south-west corner in world space, and the
            // cell spans one unit each way from there.
            float x0 = Position.x;
            float z0 = Position.z;
            float x1 = x0 + 1f;
            float z1 = z0 + 1f;

            float step = BandDepth / Steps;
            Material[] mats = BandMats;

            for (int i = 0; i < Steps; i++)
            {
                // Centre of this strip, measured inward from the boundary.
                float o = (i + 0.5f) * step;

                Vector3 a, b;
                switch (dir)
                {
                    case 0:
                        a = new Vector3(x0, y, z1 - o); b = new Vector3(x1, y, z1 - o);
                        break;
                    case 1:
                        a = new Vector3(x1 - o, y, z0); b = new Vector3(x1 - o, y, z1);
                        break;
                    case 2:
                        a = new Vector3(x0, y, z0 + o); b = new Vector3(x1, y, z0 + o);
                        break;
                    default:
                        a = new Vector3(x0 + o, y, z0); b = new Vector3(x0 + o, y, z1);
                        break;
                }

                GenDraw.DrawLineBetween(a, b, mats[i], step);
            }
        }
    }
}

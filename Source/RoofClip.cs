using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace IsoRim
{
    // Nothing under a roof rises through it. Roofs are drawn as lids at the tops of the walls (SectionLayer_IsoRimRoofs),
    // and a thing standing under one can be taller than that (a full-grown timbershroom, a thrumbo, a tree a roof was
    // built over), so its top used to come out through the slates near the eaves. Instead of fitting every thing to the
    // roof over it, the drawing itself is clipped, in the fragment stage, at the underside of the roof that is drawn
    // over that spot: what is cut away lies above the underside, where the roof hides it anyway, so nothing is squeezed
    // and buildings (which are no taller than the walls) are never touched.
    //
    // One mechanism for everything drawn from a 3D model:
    //   - the roofs layer, as it builds, writes the roof over each cell into the map's roof texture (IsoCellMap, one
    //     texel per cell: r = lid kind 1 built, 2 rock, 3 ship deck; g, b = the built roof's corner levels, as drawn);
    //   - things that may be clipped say so per vertex in texture coordinate 1 (z flags, as the shader cut-away):
    //       Own:    baked into the map mesh (ModelBaker.Bake: buildings, plants, trees, realtime bodies); x, y = the
    //               cell the thing stands in; the vertex's own (mesh) position is in that cell's map space;
    //       Moving: drawn every frame (DynamicModels: pawns, animals, mechs, door leaves, worn and held models); the
    //               thing's cell is where its matrix puts the part's origin;
    //     plant cards (IsoRimCard.vert.glsl) use their hinge;
    //   - IsoRim.vert.glsl / IsoRimTex.vert.glsl hand the owner cell and the position in roof space on to
    //     IsoRimSolid.frag.glsl (in the otherwise unused vNormal / vPosLocal: vNormal.x = 4 marks it), which drops a
    //     fragment when its owner's cell has a roof (of any kind; open or not) and the fragment is above the underside
    //     of the roof drawn over the fragment's own cell, or over the eave strip just outside it. Cells whose roof is
    //     open (cut-away near the pointer, a lifted room roof) or not drawn (roofs hidden) clip nothing.
    // Things outside a roof are never clipped (a tree beside a house keeps the branches that reach over its eaves).
    // Not covered (drawn with the game's own shaders, or no taller than the walls): sprites drawn by the game (pawns
    // without a model, motes, fire, projectiles), held weapons and carried items (SpriteSolids), blueprints and frames
    // (ghosts), extruded sprites and walls. Stacked floors (Floors): a floor above is a slab, not a roof, and clips
    // nothing; a roof on an upper floor clips the things on that floor (roof space = that floor's own rows and height).
    //
    // The fog clips the same way, with the fog lid on or off (the lid off: the game's flat fog, and the clip height is
    // still the cell's top, so over fogged open ground nothing rises above the ground). The game leaves fogged things out of what
    // it draws, but in 3D a thing drawn beside or into a fogged cell (a tree of IsoRim's own trees layer, a figure half
    // into the fog, a crown over it) rose through the fog lid, which lies on the cell's top (IsoCellMap r, g: its highest
    // block, or its edifice). In a cell the game's fog grid hides (IsoCellMap a & 4), a fragment of a clipped thing above
    // the lid (the cell's top, as the cut-away leaves it, plus the lid's lift) is dropped, and so is any fragment of a
    // thing standing in a fogged cell that rises above its own cell's lid. Where a roof is drawn too, the lower clips.
    // Reads only the game's fog grid; with stacked floors each floor's cells are its own rows, as for roofs. Things not
    // on this path are listed above (they are left out in fog by the game, or by their own drawer).
    public static class RoofClip
    {
        public const int Own = 8192;       // bit 13 of the cut-away flags: clipped, owner cell in x, y
        public const int Moving = 16384;   // bit 14: clipped, owner cell where the matrix puts the origin

        // Harness only (probe roofclip=0): off for before/after shots.
        public static bool Enabled = true;

        // Harness only (probe fogclip=0): the fog clip off for before/after shots.
        public static bool FogEnabled = true;

        // While the roofs are drawn in the 3D view.
        public static bool On
        {
            get { return Enabled && View3D.Active && IsoRimMod.Settings.showRoofs; }
        }

        // In the 3D view, with the fog lid on or off: the fog clips what stands in fogged cells at the cell's top.
        public static bool FogOn
        {
            get { return FogEnabled && View3D.Active; }
        }

        // _IsoRoof: x = what clips (0 nothing, 1 roofs, 2 the fog, 3 both), y = wall height (true cells), z = how far a
        // built roof sits above the walls, w = how much it rises per cell (SectionLayer_IsoRimRoofs.Rise / ClipPitch: 0 for
        // the concrete flat roof, whose slab starts at the wall tops, FlatRoofs; the
        // shaders know the overhang and the margins).
        public static Vector4 Globals
        {
            get
            {
                float mode = (On ? 1f : 0f) + (FogOn ? 2f : 0f);
                return mode <= 0f ? Vector4.zero : new Vector4(mode, View3D.WallHeight, SectionLayer_IsoRimRoofs.Rise, SectionLayer_IsoRimRoofs.ClipPitch);
            }
        }

        // The texel for one cell: the lid kind and, for a built roof, the levels of its corners SW, SE, NW, NE.
        public static Color32 Pack(int kind, int sw, int se, int nw, int ne)
        {
            if (kind < 1 || kind > 3)
            {
                return default(Color32);
            }
            return new Color32((byte)kind, (byte)(sw | (se << 4)), (byte)(nw | (ne << 4)), 0);
        }

        // ---- tagging the vertices of a baked model

        private static readonly List<int[]> marks = new List<int[]>();
        private static int depth;

        // Before a model is written into a layer: how many vertices each piece has. Returns the mark for Tag.
        public static int Mark(SectionLayer layer)
        {
            List<LayerSubMesh> subs = layer.subMeshes;
            if (marks.Count <= depth)
            {
                marks.Add(new int[64]);
            }
            int[] m = marks[depth];
            if (m.Length < subs.Count + 1)
            {
                m = marks[depth] = new int[subs.Count * 2 + 1];
            }
            m[0] = subs.Count;
            for (int i = 0; i < subs.Count; i++)
            {
                m[i + 1] = subs[i].verts.Count;
            }
            return depth++;
        }

        // After it: every vertex written since the mark is clipped under the roof of owner's cell. Vertices that already
        // carry a cut-away tag (a tree's leaves) keep it and its cell, and get the flag added.
        public static void Tag(SectionLayer layer, int mark, IntVec3 owner)
        {
            if (mark < 0)
            {
                return;
            }
            depth = mark;
            int[] m = marks[mark];
            Vector3 tag = new Vector3(owner.x + 0.5f, owner.z + 0.5f, Own);
            List<LayerSubMesh> subs = layer.subMeshes;
            for (int i = 0; i < subs.Count; i++)
            {
                LayerSubMesh sm = subs[i];
                int from = i < m[0] ? m[i + 1] : 0;
                int to = sm.verts.Count;
                if (to <= from)
                {
                    continue;
                }
                List<Vector3> t = sm.uvsChannelTwo;
                while (t.Count < from)
                {
                    t.Add(Vector3.zero);
                }
                for (int k = from; k < to; k++)
                {
                    if (k >= t.Count)
                    {
                        t.Add(tag);
                        continue;
                    }
                    Vector3 v = t[k];
                    int flags = Mathf.RoundToInt(v.z);
                    if (flags == 0)
                    {
                        t[k] = tag;
                    }
                    else if ((flags & Own) == 0)
                    {
                        t[k] = new Vector3(v.x, v.y, flags | Own);
                    }
                }
            }
        }

        // Before a piece is finished: one texture coordinate 1 per vertex once any has one (pieces of the game's own
        // layers get untagged vertices written after a tagged model). (Patches, LayerSubMesh.FinalizeMesh.)
        public static void Pad(LayerSubMesh sm)
        {
            List<Vector3> t = sm.uvsChannelTwo;
            int n = sm.verts.Count;
            if (t.Count == 0 || t.Count == n)
            {
                return;
            }
            if (t.Count > n)
            {
                t.RemoveRange(n, t.Count - n);
                return;
            }
            while (t.Count < n)
            {
                t.Add(Vector3.zero);
            }
        }

        // The texture coordinate 1 of every vertex of a moving model's mesh.
        public static readonly Vector3 MovingTag = new Vector3(0f, 0f, Moving);
    }
}

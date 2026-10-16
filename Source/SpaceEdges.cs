using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using EdgeType = RimWorld.SectionLayer_TerrainEdges.EdgeType;

namespace IsoRim
{
    // Odyssey's space-edge terrain in 3D: orbital and mechanoid platforms, asteroid rock and every other terrain
    // with a spaceEdgeGraphicData (all natural terrain and the generated stone floors get the asteroid set; mods
    // can add their own through the def field) reads as a slab floating in space.
    //
    // In 2D the game (SectionLayer_TerrainEdges, space maps only) paints two things: a border on the platform's
    // own top cells along the rim (OShape/UShape/Corner/Flat), which stays as it is, and the "loop" strip, the
    // platform's machinery side seen from above, laid flat on the void south of every south rim. In 3D that strip
    // lay flat on the ground and read as a sheet hanging off the rim. Instead, every side of such a cell that
    // faces the void gets an upright face hanging from the rim, with the game's own loop textures on it:
    //   - which texture: the game's rule, turned to each side: the run carries on to the left/right while the cell
    //     beside it on that side is solid (not void), so a run gets LoopLeft/LoopRight ends, a lone cell LoopSingle,
    //     the inside Loop picked by the outside cell's hash (south sides pick exactly the game's piece);
    //   - texture and colour: the game's own material for that piece (SpaceEdgeGraphicData.GetMaterial: the
    //     terrain's override colour or colour), so modded edge sets come along;
    //   - depth: the texture's own height over its width in cells (the game draws it that tall, 2 cells for the
    //     Odyssey sets), the top row at the rim.
    // Faces are drawn before the ground (queue 1999 with depth, as the game's own edge shader), so a platform's
    // own floor and anything nearer hides the sides behind it: all four sides are built once and nothing is
    // redrawn when the camera turns. Lit like the walls (sun curve), the light of the cell from the overlay.
    // Which cells, which sides, the solid test and the map gate are the game's (ShouldDrawRockEdges /
    // ShouldDrawLoop / Regenerate). With the 3D view off nothing changes.
    [StaticConstructorOnStartup]
    public static class SpaceEdges
    {
        // Harness only (probe spaceedges=0): the game's flat strips for before/after shots.
        public static bool Enabled = true;

        public const int Queue = 1999;   // the game's TerrainEdge queue: before the ground (2000), which then covers the far sides

        private static HashSet<Material> loopMaterials;
        private static readonly Dictionary<long, Material> faceMaterials = new Dictionary<long, Material>();

        static SpaceEdges()
        {
            if (!ModsConfig.OdysseyActive)
            {
                return;
            }
            try
            {
                var target = AccessTools.Method(typeof(SectionLayer_TerrainEdges), nameof(SectionLayer_TerrainEdges.Regenerate));
                if (target == null)
                {
                    ModLog.Warn("[IsoRim] SectionLayer_TerrainEdges.Regenerate not found: the game's flat space-edge strips stay beside the 3D sides.");
                    return;
                }
                new Harmony("isorim.spaceedges").Patch(target, postfix: new HarmonyMethod(typeof(SpaceEdges), nameof(Regenerate_Postfix)));
                ModLog.Note("[IsoRim] Hooked SectionLayer_TerrainEdges.Regenerate (3D platform sides).");
            }
            catch (Exception e)
            {
                ModLog.Warn("[IsoRim] Could not hook the game's space-edge strips: " + e.Message);
            }
        }

        public static bool Active
        {
            get { return Enabled && View3D.Active && ModsConfig.OdysseyActive; }
        }

        // The game's gate (SectionLayer_TerrainEdges.Regenerate): space maps and maps without a world tile.
        public static bool DrawsOn(Map map)
        {
            if (map == null)
            {
                return false;
            }
            PlanetTile tile = map.Tile;
            return !tile.Valid || (tile.LayerDef != null && tile.LayerDef.isSpace);
        }

        // After the game rebuilds its edge layer: while the 3D sides are drawn, its flat loop strips
        // (LoopLeft/Right/Single/Loop of every edge set) are emptied; the rim border on the top cells stays.
        public static void Regenerate_Postfix(SectionLayer_TerrainEdges __instance)
        {
            if (!Active || __instance.subMeshes.Count == 0 || !DrawsOn(MapSections.MapOf(__instance)))
            {
                return;   // (the game draws nothing on other maps)
            }
            try
            {
                HashSet<Material> loops = LoopMaterials();
                List<LayerSubMesh> subs = __instance.subMeshes;
                for (int i = 0; i < subs.Count; i++)
                {
                    if (subs[i].material != null && loops.Contains(subs[i].material) && subs[i].verts.Count > 0)
                    {
                        subs[i].Clear(MeshParts.All);   // not finalized: not drawn, and left out of the culling boxes
                    }
                }
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[IsoRim] Hiding the game's flat space-edge strips failed: " + e, 93611451);
            }
        }

        private static HashSet<Material> LoopMaterials()
        {
            if (loopMaterials != null)
            {
                return loopMaterials;
            }
            HashSet<Material> set = new HashSet<Material>();
            foreach (TerrainDef t in DefDatabase<TerrainDef>.AllDefsListForReading)
            {
                TerrainDef.SpaceEdgeGraphicData data = t.spaceEdgeGraphicData;
                if (data == null)
                {
                    continue;
                }
                try
                {
                    set.Add(data.GetMaterial(t, EdgeType.LoopLeft));
                    set.Add(data.GetMaterial(t, EdgeType.LoopRight));
                    set.Add(data.GetMaterial(t, EdgeType.LoopSingle));
                    int n = data.LoopTexPaths != null ? data.LoopTexPaths.Count : 0;
                    for (int i = 0; i < n; i++)
                    {
                        set.Add(data.GetMaterial(t, EdgeType.Loop, i));
                    }
                }
                catch (Exception e)
                {
                    ModLog.Warn("[IsoRim] Space-edge set of " + t.defName + " could not be read: " + e.Message);
                }
            }
            set.Remove(null);
            loopMaterials = set;
            return set;
        }

        // The lit material for one of the game's loop materials: its texture and colour on IsoRim's textured
        // wall material, drawn in the game's edge queue.
        public static Material FaceMaterial(Material vanilla)
        {
            if (vanilla == null || vanilla.mainTexture == null)
            {
                return null;
            }
            Color32 c = vanilla.color;
            long key = ((long)vanilla.mainTexture.GetInstanceID() << 32) ^ (uint)(c.r | c.g << 8 | c.b << 16);
            Material m;
            if (faceMaterials.TryGetValue(key, out m))
            {
                return m;
            }
            m = R3DMaterials.Make(R3DMaterials.SolidTexDef, "IsoRim_SpaceEdge", Queue, R3DMaterials.LightWall);
            if (m != null)
            {
                m.mainTexture = vanilla.mainTexture;   // the game's texture as it is (its wrap and filter untouched)
                m.color = new Color(vanilla.color.r, vanilla.color.g, vanilla.color.b, 1f);
                IsoGlow.Use(m, IsoGlow.None);   // lit by the overlay: the far sides it covers must stay unmarked
            }
            faceMaterials[key] = m;
            return m;
        }

        // The piece the game would draw for a side of cell c facing d: whether the solid run goes on past the
        // cell to the left and right of that side (as seen from outside) decides the end pieces.
        public static EdgeType PieceFor(bool left, bool right)
        {
            if (left && right)
            {
                return EdgeType.Loop;
            }
            if (right)
            {
                return EdgeType.LoopLeft;    // the run starts here, on its left end
            }
            if (left)
            {
                return EdgeType.LoopRight;
            }
            return EdgeType.LoopSingle;
        }
    }

    // The upright sides of space-edge terrain (see SpaceEdges).
    public class SectionLayer_IsoRimSpaceEdges : SectionLayer
    {
        private const float MinDepth = 0.5f;
        private const float MaxDepth = 3f;

        private static readonly IntVec3[] Sides = { IntVec3.North, IntVec3.East, IntVec3.South, IntVec3.West };

        public SectionLayer_IsoRimSpaceEdges(Section section) : base(section)
        {
            relevantChangeTypes = MapMeshFlagDefOf.Terrain;
        }

        public override bool Visible
        {
            get { return View3D.Ready && SpaceEdges.Enabled; }
        }

        public override void Regenerate()
        {
            ClearSubMeshes(MeshParts.All);
            Map map = Map;
            if (!SpaceEdges.Active || !SpaceEdges.DrawsOn(map))
            {
                FinalizeMesh(MeshParts.All);
                return;
            }
            try
            {
                TerrainGrid grid = map.terrainGrid;
                CellRect r = section.CellRect;
                // Stacked floors (Floors): only the ground rows; rows above count as outside the map.
                int ground = Floors.GroundRowsOf(map);
                int h = ground > 0 ? ground : map.Size.z;
                r.maxZ = Mathf.Min(r.maxZ, h - 1);
                float squash = View3D.Squash;
                float top = -0.002f / squash;
                bool clippers = map.DrawMapClippers;
                foreach (IntVec3 c in r)
                {
                    TerrainDef t = grid.BaseTerrainAt(c);
                    TerrainDef.SpaceEdgeGraphicData data = t != null ? t.spaceEdgeGraphicData : null;
                    if (data == null)
                    {
                        continue;
                    }
                    for (int s = 0; s < 4; s++)
                    {
                        IntVec3 d = Sides[s];
                        IntVec3 o = c + d;
                        bool outside = !o.InBounds(map) || o.z >= h;
                        // The game's ShouldDrawRockEdges: a void neighbour, or past the edge of a map without edge sheets.
                        if (outside ? clippers : !Void(grid, o))
                        {
                            continue;
                        }
                        IntVec3 left = new IntVec3(d.z, 0, -d.x);   // to the left as seen from outside, looking at the side
                        bool goesLeft = Solid(map, grid, c + left, h);
                        bool goesRight = Solid(map, grid, c - left, h);
                        EdgeType piece = SpaceEdges.PieceFor(goesLeft, goesRight);
                        Material vanilla = data.GetMaterial(t, piece, o.GetHashCode());
                        Material mat = SpaceEdges.FaceMaterial(vanilla);
                        LayerSubMesh sm = mat != null ? MeshWriter.Sub(this, mat) : null;
                        if (sm == null)
                        {
                            continue;
                        }
                        Texture tex = vanilla.mainTexture;
                        float depth = Mathf.Clamp(tex.width > 0 ? (float)tex.height / tex.width : 2f, MinDepth, MaxDepth);
                        float bottom = top - depth / squash;
                        Vector3 mid = new Vector3(c.x + 0.5f + d.x * 0.5f, 0f, c.z + 0.5f + d.z * 0.5f);
                        Vector3 half = new Vector3(left.x * 0.5f, 0f, left.z * 0.5f);
                        Vector3 pl = mid + half;
                        Vector3 pr = mid - half;
                        // One winding: the material does not cull. u runs left to right seen from outside, v = 1 at the rim.
                        MeshWriter.TexturedQuad(sm,
                            new Vector3(pl.x, bottom, pl.z), new Vector3(pl.x, top, pl.z), new Vector3(pr.x, top, pr.z), new Vector3(pr.x, bottom, pr.z),
                            new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f), new Color32(255, 255, 255, 255));
                    }
                }
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[IsoRim] Drawing the sides of space platforms failed: " + e, 93611452);
            }
            FinalizeMesh(MeshParts.All);
        }

        // The game's void test (dontRender: space).
        private static bool Void(TerrainGrid grid, IntVec3 c)
        {
            TerrainDef t = grid.TerrainAt(c);
            return t == null || t.dontRender;
        }

        // The run goes on into this cell (the game's corner test: outside the map never does).
        private static bool Solid(Map map, TerrainGrid grid, IntVec3 c, int h)
        {
            return c.InBounds(map) && c.z < h && !Void(grid, c);
        }
    }
}

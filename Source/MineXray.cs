using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace IsoRim
{
    // X-ray of the floor below while mining upstairs (stacked floors, Floors): with the game's Mine tool in hand
    // (Designator_Mine, MineVein too) and an upper floor viewed, the floor directly below is drawn faintly over the
    // viewed one, at the height of the rock tops the tool points at: its open cells (tunnels, rooms) outlined, with a
    // light fill, rock and walls left empty; the pawns down there as dots. Only under what the viewed floor covers (its
    // rock, floors, buildings): below the open air the floor is seen anyway. A crosshair runs Reach cells along x and z
    // from the pointer's cell, to line the marks up with the tunnels below. All on the overlay shader (no depth test),
    // so the rock never hides it. Only what the player knows is shown: fogged cells below count as solid and pawns on
    // them are left out.
    //
    // Cheap per frame: the outline is one mesh per floor, remade only when something on the map that changes it
    // (buildings, terrain, fog: Floors.MapMeshDirty_Postfix -> Dirty) has happened, at most every RebuildSeconds.
    [StaticConstructorOnStartup]
    public static class MineXray
    {
        public const int Reach = 5;                 // crosshair cells each way from the pointer
        private const float RebuildSeconds = 0.5f;
        private const float Line = 0.07f;           // outline width (cells)

        private static readonly Color Edge = new Color(0.55f, 0.85f, 1f, 0.6f);
        private static readonly Color Fill = new Color(0.55f, 0.85f, 1f, 0.14f);
        private static readonly Color Cross = new Color(1f, 0.9f, 0.45f, 0.12f);
        private static readonly Color CrossCell = new Color(1f, 0.9f, 0.45f, 0.35f);
        private static readonly Color Colonist = new Color(0.6f, 1f, 0.6f, 0.85f);
        private static readonly Color Other = new Color(1f, 0.55f, 0.45f, 0.85f);

        private static Mesh outline;
        private static Map outlineMap;
        private static int outlineFloor = -1;
        private static int outlineRows;
        private static bool dirty = true;
        private static float builtAt = -100f;
        private static float top = -1f;             // drawn height over the viewed floor (the rock tops), last known
        private static Mesh quad;
        private static Mesh disc;
        private static Material mat;
        private static MaterialPropertyBlock block;
        private static int colorId;

        // Harness: drawn this frame (probe readout).
        public static bool Shown;
        public static int OutlineQuads;
        public static double BuildMs;               // the last outline rebuild
        public static int Builds;

        // Something that can change the outline happened somewhere on the map (cheap: a flag).
        public static void Dirty(ulong flags)
        {
            if ((flags & ((ulong)MapMeshFlagDefOf.Buildings | (ulong)MapMeshFlagDefOf.Terrain | (ulong)MapMeshFlagDefOf.FogOfWar)) != 0)
            {
                dirty = true;
            }
        }

        public static bool Tool(Designator d)
        {
            return d is Designator_Mine;   // Designator_MineVein derives from it
        }

        // Every frame while the 3D view draws (View3D, with the drag box).
        public static void Draw()
        {
            Shown = false;
            Map map = Find.CurrentMap;
            DesignatorManager dm = Find.DesignatorManager;
            IsoRimSettings st = IsoRimMod.Settings;
            if (!Floors.On || map == null || dm == null || !Tool(dm.SelectedDesignator) || !st.mineXray && !st.mineCrosshair)
            {
                return;
            }
            int k = Floors.PointerTop;
            if (k <= 0)
            {
                return;
            }
            int below = k - 1;
            float hK;
            int zK;
            Floors.FloorPlane(k, out hK, out zK);
            float hB;
            int zB;
            Floors.FloorPlane(below, out hB, out zB);
            int rows = Floors.GroundRowsOf(map);
            if (rows <= 0)
            {
                return;
            }

            IntVec3 cell = Interaction.DesignatorMouseCell();
            UpdateTop(map, cell, k);
            if (st.mineXray && (outline == null || outlineMap != map || outlineFloor != below || outlineRows != rows
                || dirty && Time.realtimeSinceStartup - builtAt >= RebuildSeconds))
            {
                Build(map, below, zB, zK, rows);
            }
            if (Mat == null)
            {
                return;
            }
            Shown = true;
            Matrix4x4 lift = View3D.LiftMatrix(top + 0.03f);
            // The floor below's rows, moved up into the viewed floor's (the drawing map puts them on that floor).
            if (st.mineXray && outline != null)
            {
                Graphics.DrawMesh(outline, lift * Matrix4x4.Translate(new Vector3(0f, 0f, zK)), mat, 0, null, 0, Block(Color.white));
            }

            // Pawns below, where the player can see them.
            IReadOnlyList<Pawn> pawns = st.mineXray ? map.mapPawns.AllPawnsSpawned : (IReadOnlyList<Pawn>)System.Array.Empty<Pawn>();
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                IntVec3 pc = p.Position;
                if (pc.z < zB || pc.z >= zB + rows || Floors.ColumnFloor(map, pc, out float ph, out int pz) != below || map.fogGrid.IsFogged(pc) || !Covered(map, new IntVec3(pc.x, 0, pc.z - zB + zK)))
                {
                    continue;
                }
                Vector3 d = p.DrawPos;
                Matrix4x4 m = lift * Matrix4x4.TRS(new Vector3(d.x, 0f, d.z - zB + zK), Quaternion.identity, new Vector3(0.55f, 1f, 0.55f));
                Graphics.DrawMesh(Disc, m, mat, 0, null, 0, Block(p.Faction != null && p.Faction == Faction.OfPlayer ? Colonist : Other));
            }

            // The crosshair, on the viewed floor's rows.
            if (st.mineCrosshair && cell.InBounds(map) && Floors.FloorOfRow(cell.z) == k)
            {
                Rect(lift, cell.x - Reach, cell.z, cell.x + Reach + 1, cell.z + 1, Cross);
                Rect(lift, cell.x, cell.z - Reach, cell.x + 1, cell.z, Cross);
                Rect(lift, cell.x, cell.z + 1, cell.x + 1, cell.z + Reach + 1, Cross);
                Rect(lift, cell.x, cell.z, cell.x + 1, cell.z + 1, CrossCell);
            }
        }

        // The rock tops the Mine tool points at on the viewed floor (kept while the pointer is over open cells).
        private static void UpdateTop(Map map, IntVec3 cell, int k)
        {
            if (cell.InBounds(map) && Floors.FloorOfRow(cell.z) == k)
            {
                Thing rock = cell.GetFirstMineable(map);
                if (rock != null)
                {
                    top = Interaction.PickHeight(rock);
                }
            }
            if (top < 0f)
            {
                top = View3D.WallHeight;
            }
        }

        // Open = walkable and known (not fogged); on an upper floor also a built floor (not the open air).
        private static bool Open(Map map, IntVec3 c, int floor)
        {
            if (!c.InBounds(map) || map.fogGrid.IsFogged(c) || !c.Walkable(map))
            {
                return false;
            }
            return floor == 0 || !map.terrainGrid.TerrainAt(c).dontRender;
        }

        private static readonly List<Vector3> verts = new List<Vector3>();
        private static readonly List<Color32> cols = new List<Color32>();
        private static readonly List<int> tris = new List<int>();

        // The viewed floor hides what is below this cell: it has a floor there or something built or rock on it (not the
        // open air, where the floor below is seen anyway).
        private static bool Covered(Map map, IntVec3 up)
        {
            return up.InBounds(map) && (!map.terrainGrid.TerrainAt(up).dontRender || up.GetEdifice(map) != null);
        }

        private static void Build(Map map, int floor, int zB, int zK, int rows)
        {
            System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
            dirty = false;
            builtAt = Time.realtimeSinceStartup;
            outlineMap = map;
            outlineFloor = floor;
            outlineRows = rows;
            verts.Clear();
            cols.Clear();
            tris.Clear();
            int w = map.Size.x;
            bool[] open = new bool[w * rows];
            bool[] shown = new bool[w * rows];
            for (int z = 0; z < rows; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    bool o = Open(map, new IntVec3(x, 0, z + zB), floor);
                    open[z * w + x] = o;
                    shown[z * w + x] = o && Covered(map, new IntVec3(x, 0, z + zK));
                }
            }
            Color32 fill = Fill;
            Color32 edge = Edge;
            for (int z = 0; z < rows; z++)
            {
                for (int x = 0; x < w; x++)
                {
                    if (!shown[z * w + x])
                    {
                        continue;
                    }
                    if (x == 0 || !shown[z * w + x - 1])
                    {
                        int run = x + 1;   // the fill: one quad per run of shown cells along the row
                        while (run < w && shown[z * w + run])
                        {
                            run++;
                        }
                        Add(x, z, run, z + 1, fill);
                    }
                    // edges: only toward what is solid below (a tunnel's mouth under open air gets none)
                    if (x == 0 || !open[z * w + x - 1])
                    {
                        Add(x, z, x + Line, z + 1, edge);
                    }
                    if (x == w - 1 || !open[z * w + x + 1])
                    {
                        Add(x + 1 - Line, z, x + 1, z + 1, edge);
                    }
                    if (z == 0 || !open[(z - 1) * w + x])
                    {
                        Add(x, z, x + 1, z + Line, edge);
                    }
                    if (z == rows - 1 || !open[(z + 1) * w + x])
                    {
                        Add(x, z + 1 - Line, x + 1, z + 1, edge);
                    }
                }
            }
            if (outline == null)
            {
                outline = new Mesh();
                outline.name = "IsoRim_MineXray";
                outline.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }
            outline.Clear();
            outline.SetVertices(verts);
            outline.SetColors(cols);
            outline.SetTriangles(tris, 0);
            outline.RecalculateBounds();
            OutlineQuads = verts.Count / 4;
            BuildMs = sw.Elapsed.TotalMilliseconds;
            Builds++;
        }

        // A flat quad (both sides) in the outline mesh.
        private static void Add(float x0, float z0, float x1, float z1, Color32 c)
        {
            int b = verts.Count;
            verts.Add(new Vector3(x0, 0f, z0));
            verts.Add(new Vector3(x0, 0f, z1));
            verts.Add(new Vector3(x1, 0f, z1));
            verts.Add(new Vector3(x1, 0f, z0));
            cols.Add(c);
            cols.Add(c);
            cols.Add(c);
            cols.Add(c);
            tris.Add(b);
            tris.Add(b + 1);
            tris.Add(b + 2);
            tris.Add(b);
            tris.Add(b + 2);
            tris.Add(b + 3);
        }

        private static void Rect(Matrix4x4 lift, float x0, float z0, float x1, float z1, Color c)
        {
            Matrix4x4 m = lift * Matrix4x4.TRS(new Vector3((x0 + x1) * 0.5f, 0f, (z0 + z1) * 0.5f), Quaternion.identity, new Vector3(x1 - x0, 1f, z1 - z0));
            Graphics.DrawMesh(Quad, m, mat, 0, null, 0, Block(c));
        }

        private static MaterialPropertyBlock Block(Color c)
        {
            if (block == null)
            {
                block = new MaterialPropertyBlock();
                colorId = Shader.PropertyToID("_Color");
            }
            block.SetColor(colorId, c);
            return block;
        }

        private static Material Mat
        {
            get
            {
                if (mat == null && R3DMaterials.Solid != null)
                {
                    mat = R3DMaterials.Make(R3DMaterials.OverlayDef, "IsoRim_MineXray", 3650);
                }
                return mat;
            }
        }

        // A unit square centred on the origin, flat, both sides.
        private static Mesh Quad
        {
            get
            {
                if (quad == null)
                {
                    quad = new Mesh();
                    quad.SetVertices(new List<Vector3> { new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f) });
                    Color32 wh = new Color32(255, 255, 255, 255);
                    quad.SetColors(new List<Color32> { wh, wh, wh, wh });
                    quad.SetTriangles(new[] { 0, 1, 2, 0, 2, 3, 0, 2, 1, 0, 3, 2 }, 0);
                    quad.RecalculateBounds();
                }
                return quad;
            }
        }

        // A disc one unit across, flat, both sides.
        private static Mesh Disc
        {
            get
            {
                if (disc == null)
                {
                    const int N = 16;
                    List<Vector3> v = new List<Vector3> { Vector3.zero };
                    List<Color32> c = new List<Color32> { new Color32(255, 255, 255, 255) };
                    List<int> t = new List<int>();
                    for (int i = 0; i < N; i++)
                    {
                        float a = i * Mathf.PI * 2f / N;
                        v.Add(new Vector3(Mathf.Cos(a) * 0.5f, 0f, Mathf.Sin(a) * 0.5f));
                        c.Add(new Color32(255, 255, 255, 255));
                        int j = 1 + i;
                        int n = 1 + (i + 1) % N;
                        t.Add(0); t.Add(j); t.Add(n);
                        t.Add(0); t.Add(n); t.Add(j);
                    }
                    disc = new Mesh();
                    disc.SetVertices(v);
                    disc.SetColors(c);
                    disc.SetTriangles(t, 0);
                    disc.RecalculateBounds();
                }
                return disc;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace IsoRim
{
    // The cells of a gravship (ShipFlight): the valid substructure (Deck), the hull corner pieces standing beside it and
    // the cells of buildings on it (Ship), and the ring of cells round the deck (Ring). Made once when the cutscene
    // starts, while the ship still stands on the map.
    public sealed class ShipCells
    {
        public const byte Ring = 1, Ship = 2, Deck = 3;
        private static readonly int[] DX = { 0, 0, 0, -1, 1, -1, 1, -1, 1 };
        private static readonly int[] DZ = { 0, -1, 1, 0, 0, -1, -1, 1, 1 };

        public CellRect bounds;      // the substructure's bounds widened by one cell (vanilla's capture bounds)
        public Vector3 pivot;        // their centre: the cutscene's centre
        public CellRect area;        // bounds widened a little more: where the classes are kept
        private byte[] cls;
        private readonly Dictionary<int, Vector2> corners = new Dictionary<int, Vector2>();   // corner cell: toward its two hull legs

        public static ShipCells Of(Building_GravEngine engine)
        {
            Map map = engine.Map;
            HashSet<IntVec3> deck = engine.ValidSubstructure;
            ShipCells s = new ShipCells();
            s.bounds = CellRect.FromCellList(deck).ExpandedBy(1);
            s.pivot = s.bounds.CenterVector3;
            s.pivot.y = 0f;
            s.area = s.bounds.ExpandedBy(4);
            s.cls = new byte[s.area.Width * s.area.Height];
            foreach (IntVec3 c in deck)
            {
                s.cls[s.Index(c.x, c.z)] = Deck;
            }
            foreach (IntVec3 c in s.bounds)
            {
                int i = s.Index(c.x, c.z);
                RimWorld.SectionLayer_GravshipHull.CornerType type;
                IntVec3 a, b;
                if (s.cls[i] == 0 && c.InBounds(map) && c.GetEdifice(map) == null && HullCorners.At(map, c, out type, out _) && HullCorners.Legs(type, out a, out b))
                {
                    s.cls[i] = Ship;
                    s.corners[i] = new Vector2(a.x + b.x, a.z + b.z);
                }
            }
            foreach (IntVec3 c in deck)
            {
                List<Thing> things = c.GetThingList(map);
                for (int k = 0; k < things.Count; k++)
                {
                    if (things[k] is Pawn || things[k].def.category != ThingCategory.Building)
                    {
                        continue;
                    }
                    foreach (IntVec3 o in things[k].OccupiedRect())
                    {
                        int i = s.Index(o.x, o.z);
                        if (i >= 0 && s.cls[i] < Ship)
                        {
                            s.cls[i] = Ship;
                        }
                    }
                }
            }
            foreach (IntVec3 c in deck)
            {
                for (int d = 1; d < 9; d++)
                {
                    int i = s.Index(c.x + DX[d], c.z + DZ[d]);
                    if (i >= 0 && s.cls[i] == 0)
                    {
                        s.cls[i] = Ring;
                    }
                }
            }
            return s;
        }

        private int Index(int x, int z)
        {
            return x < area.minX || z < area.minZ || x > area.maxX || z > area.maxZ ? -1 : (z - area.minZ) * area.Width + (x - area.minX);
        }

        // The class of the cell under a map point (drawn x, z).
        public byte At(float x, float z)
        {
            int i = Index(Mathf.FloorToInt(x), Mathf.FloorToInt(z));
            return i < 0 ? (byte)0 : cls[i];
        }

        // The deck cells, and whether a cell is deck.
        public IEnumerable<IntVec3> DeckCells()
        {
            foreach (IntVec3 c in bounds)
            {
                if (IsDeck(c.x, c.z))
                {
                    yield return c;
                }
            }
        }

        public bool IsDeck(int x, int z)
        {
            int i = Index(x, z);
            return i >= 0 && cls[i] == Deck;
        }

        // A hull corner cell (its wedge stands toward its two hull legs, legs = their two directions added).
        public bool Corner(int x, int z, out Vector2 legs)
        {
            int i = Index(x, z);
            legs = Vector2.zero;
            return i >= 0 && corners.TryGetValue(i, out legs);
        }

        public IEnumerable<IntVec3> CornerCells()
        {
            foreach (IntVec3 c in bounds)
            {
                Vector2 legs;
                if (Corner(c.x, c.z, out legs))
                {
                    yield return c;
                }
            }
        }

        // How much of the cell's ground the ship covers at a point inside it (local 0-1): all of a deck or building cell,
        // the half of a corner cell toward its hull legs (the wedge).
        public bool Covers(int x, int z, float u, float v)
        {
            int i = Index(x, z);
            if (i < 0 || cls[i] < Ship)
            {
                return false;
            }
            Vector2 legs;
            if (corners.TryGetValue(i, out legs))
            {
                return (u - 0.5f) * legs.x + (v - 0.5f) * legs.y >= 0f;
            }
            return true;
        }
    }

    // The ship's moving parts in the frame before it leaves (ShipFlight): pawns, animals, door leaves, the engine's
    // orb, spinners, held and carried things, glass. Everything IsoRim draws per frame passes one of its chokepoints
    // (IsoGlow.DrawLit: DynamicModels and the sprite slabs; SpriteSolids' faces; Glass; DrawHook: the game's own pawn
    // and sprite drawing, rewritten). While recording, each of them notes its draw here when the matrix puts its origin
    // on one of the ship's cells. The last whole frame is kept: the game is paused through the cutscene, so it is the
    // pose the player saw.
    public static class ShipCapture
    {
        public struct Rec
        {
            public Mesh mesh;
            public Matrix4x4 matrix;
            public Material mat;
            public MaterialPropertyBlock props;
        }

        // Set while the chokepoints call Note (recording, hiding or both).
        public static bool Recording;
        private static bool storing;
        private static bool hiding;
        private static ShipCells cells;
        private static List<Rec> cur = new List<Rec>();
        private static List<Rec> last = new List<Rec>();
        private static int curFrame = -1;

        // Records the ship's moving parts; with hide (a landing ship, placed on the map under the frozen ground until
        // touchdown) they are not drawn either.
        public static void Start(ShipCells c, bool hide)
        {
            cells = c;
            cur.Clear();
            last.Clear();
            curFrame = -1;
            storing = c != null;
            hiding = c != null && hide;
            Recording = c != null;
        }

        // Stops storing draws (the snapshot has its frame) but keeps hiding them, if it did.
        public static void Hold()
        {
            storing = false;
            cur.Clear();
            last.Clear();
            Recording = hiding;
        }

        public static void Stop()
        {
            Recording = false;
            storing = false;
            hiding = false;
            cells = null;
            cur.Clear();
            last.Clear();
        }

        // Notes a draw. True: it belongs to a ship being hidden, so the caller must not draw it.
        public static bool Note(Mesh mesh, Matrix4x4 matrix, Material mat, MaterialPropertyBlock props)
        {
            if (!Recording || mesh == null || mat == null || cells == null)
            {
                return false;
            }
            if (cells.At(matrix.m03, matrix.m23) < ShipCells.Ship)
            {
                return false;
            }
            if (storing)
            {
                Store(new Rec { mesh = mesh, matrix = matrix, mat = mat, props = props });
            }
            return hiding;
        }

        private static void Store(Rec r)
        {
            int f = Time.frameCount;
            if (f != curFrame)
            {
                List<Rec> t = last;
                last = cur;
                cur = t;
                cur.Clear();
                curFrame = f;
            }
            cur.Add(r);
        }

        // The last whole frame: the latest one if the game has moved on to a new frame since, else the one before.
        public static List<Rec> Take()
        {
            return Time.frameCount != curFrame ? new List<Rec>(cur) : new List<Rec>(last);
        }
    }

    // The 3D "terrain curtain" of a landing (ShipFlight): vanilla photographs the landing area just before the ship is
    // placed (GravshipCapturer.BeginTerrainRender) and lays that picture over the map until touchdown. In 3D the
    // sections over that area are frozen instead: each layer's finished meshes are kept as they are and drawn in place of
    // the layer's own from then on, while the layer gets a fresh list and is rebuilt into it at once (so it carries on as
    // usual: the placed ship goes there, and the snapshot reads it). At touchdown the frozen meshes are dropped and the
    // live ones show again. Nothing is copied: the old meshes are only kept.
    public static class ShipFreeze
    {
        public static bool Active;
        private static readonly Dictionary<MapDrawLayer, List<LayerSubMesh>> frozen = new Dictionary<MapDrawLayer, List<LayerSubMesh>>();
        private static readonly HarmonyLib.AccessTools.FieldRef<Section, List<SectionLayer>> sectionLayers =
            HarmonyLib.AccessTools.FieldRefAccess<Section, List<SectionLayer>>("layers");

        public static int Sections { get; private set; }

        public static void Freeze(Map map, CellRect rect)
        {
            Unfreeze();
            System.Array sections = MapSections.SectionsOf(map);
            if (sections == null)
            {
                return;
            }
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            CellRect r = rect.ClipInsideMap(map);
            int n = 0;
            for (int sx = r.minX / Section.Size; sx <= r.maxX / Section.Size && sx < sections.GetLength(0); sx++)
            {
                for (int sz = r.minZ / Section.Size; sz <= r.maxZ / Section.Size && sz < sections.GetLength(1); sz++)
                {
                    Section sec = sections.GetValue(sx, sz) as Section;
                    if (sec == null)
                    {
                        continue;
                    }
                    bool any = false;
                    foreach (SectionLayer layer in sectionLayers(sec))
                    {
                        if (layer is SectionLayer_Dynamic || !layer.Visible)
                        {
                            continue;
                        }
                        frozen[layer] = layer.subMeshes;
                        layer.subMeshes = new List<LayerSubMesh>();
                        any = true;
                    }
                    if (any)
                    {
                        sec.RegenerateAllLayers();
                        n++;
                    }
                }
            }
            Sections = n;
            Active = frozen.Count > 0;
            ModLog.Note("[IsoRim] Gravship landing: froze " + n + " sections (" + frozen.Count + " layers) over " + rect + " in " + watch.ElapsedMilliseconds + " ms");
        }

        // The frozen ground still shows what stood where the ship lands (trees, rock, plants, buildings: placing the ship
        // cleared them). Once the ship comes down low over its place, those would rise through its deck: the frozen
        // meshes are filtered then, dropping every triangle that is not flat ground and belongs to a cell inside the
        // ship (its owner cell when tagged, as ShipSnapshot reads them, else the cell under its middle).
        public static void CutOut(ShipCells cells)
        {
            float squash = View3D.Squash;
            int dropped = 0;
            foreach (List<LayerSubMesh> subs in frozen.Values)
            {
                for (int k = 0; k < subs.Count; k++)
                {
                    LayerSubMesh sm = subs[k];
                    if (sm == null || !sm.finalized || sm.disabled || sm.verts.Count == 0)
                    {
                        continue;
                    }
                    List<Vector3> v = sm.verts;
                    List<int> t = sm.tris;
                    List<Vector3> tag = sm.uvsChannelTwo.Count == v.Count ? sm.uvsChannelTwo : null;
                    List<int> keep = null;
                    for (int i = 0; i + 2 < t.Count; i += 3)
                    {
                        Vector3 a = v[t[i]], b = v[t[i + 1]], c = v[t[i + 2]];
                        bool flat = a.y * squash < 0.02f && b.y * squash < 0.02f && c.y * squash < 0.02f;
                        bool inShip = false;
                        if (!flat)
                        {
                            int flags = tag != null ? (int)(tag[t[i]].z + 0.5f) : 0;
                            inShip = flags != 0
                                ? cells.At(Mathf.Floor(tag[t[i]].x) + 0.5f, Mathf.Floor(tag[t[i]].y) + 0.5f) >= ShipCells.Ship
                                : cells.At((a.x + b.x + c.x) / 3f, (a.z + b.z + c.z) / 3f) >= ShipCells.Ship;
                        }
                        if (inShip)
                        {
                            if (keep == null)
                            {
                                keep = new List<int>(t.Count);
                                for (int j = 0; j < i; j++)
                                {
                                    keep.Add(t[j]);
                                }
                            }
                            dropped++;
                        }
                        else if (keep != null)
                        {
                            keep.Add(t[i]);
                            keep.Add(t[i + 1]);
                            keep.Add(t[i + 2]);
                        }
                    }
                    if (keep == null)
                    {
                        continue;
                    }
                    UnityEngine.Object.Destroy(sm.mesh);
                    if (keep.Count == 0)
                    {
                        sm.mesh = new Mesh();
                        sm.disabled = true;
                        continue;
                    }
                    // A new mesh from the kept CPU lists (as LayerSubMesh.FinalizeMesh makes it).
                    Mesh m = new Mesh();
                    m.SetVertices(v);
                    m.SetTriangles(keep, 0);
                    if (sm.colors.Count == v.Count)
                    {
                        m.SetColors(sm.colors);
                    }
                    if (sm.uvs.Count == v.Count)
                    {
                        m.SetUVs(0, sm.uvs);
                    }
                    if (tag != null)
                    {
                        m.SetUVs(1, tag);
                    }
                    if (sm.normals.Count == v.Count)
                    {
                        m.SetNormals(sm.normals);
                    }
                    m.RecalculateBounds();
                    sm.mesh = m;
                    sm.tris.Clear();
                    sm.tris.AddRange(keep);
                }
            }
            ModLog.Note("[IsoRim] Gravship landing: " + dropped + " triangles of what stood under the ship taken out of the frozen ground");
        }

        // Lets the frozen meshes of some layers go early (the live ones show again).
        public static void Thaw(Predicate<MapDrawLayer> which)
        {
            List<MapDrawLayer> gone = new List<MapDrawLayer>();
            foreach (KeyValuePair<MapDrawLayer, List<LayerSubMesh>> kv in frozen)
            {
                if (which(kv.Key))
                {
                    foreach (LayerSubMesh sm in kv.Value)
                    {
                        if (sm != null && sm.mesh != null)
                        {
                            UnityEngine.Object.Destroy(sm.mesh);
                        }
                    }
                    gone.Add(kv.Key);
                }
            }
            foreach (MapDrawLayer l in gone)
            {
                frozen.Remove(l);
            }
            Active = frozen.Count > 0;
        }

        public static void Unfreeze()
        {
            if (frozen.Count == 0)
            {
                Active = false;
                return;
            }
            foreach (KeyValuePair<MapDrawLayer, List<LayerSubMesh>> kv in frozen)
            {
                foreach (LayerSubMesh sm in kv.Value)
                {
                    if (sm != null && sm.mesh != null)
                    {
                        UnityEngine.Object.Destroy(sm.mesh);
                    }
                }
            }
            frozen.Clear();
            Active = false;
        }

        // MapDrawLayer.DrawLayer, first of all prefixes (IsoRim's own DrawLayer prefix then draws the frozen meshes too):
        // the frozen list in place for the draw, the live one back after it.
        public static void DrawLayer_Prefix(MapDrawLayer __instance, out List<LayerSubMesh> __state)
        {
            __state = null;
            List<LayerSubMesh> old;
            if (Active && frozen.TryGetValue(__instance, out old))
            {
                __state = __instance.subMeshes;
                __instance.subMeshes = old;
            }
        }

        // A finalizer, so the live list goes back even if the draw throws.
        public static System.Exception DrawLayer_Finalizer(MapDrawLayer __instance, List<LayerSubMesh> __state, System.Exception __exception)
        {
            if (__state != null)
            {
                __instance.subMeshes = __state;
            }
            return __exception;
        }
    }
}

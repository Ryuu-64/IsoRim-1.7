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

        public static bool Recording;
        private static ShipCells cells;
        private static List<Rec> cur = new List<Rec>();
        private static List<Rec> last = new List<Rec>();
        private static int curFrame = -1;

        public static void Start(ShipCells c)
        {
            cells = c;
            cur.Clear();
            last.Clear();
            curFrame = -1;
            Recording = c != null;
        }

        public static void Stop()
        {
            Recording = false;
            cells = null;
            cur.Clear();
            last.Clear();
        }

        public static void Note(Mesh mesh, Matrix4x4 matrix, Material mat, MaterialPropertyBlock props)
        {
            if (!Recording || mesh == null || mat == null || cells == null)
            {
                return;
            }
            int f = Time.frameCount;
            if (f != curFrame)
            {
                List<Rec> t = last;
                last = cur;
                cur = t;
                cur.Clear();
                curFrame = f;
            }
            if (cells.At(matrix.m03, matrix.m23) < ShipCells.Ship)
            {
                return;
            }
            cur.Add(new Rec { mesh = mesh, matrix = matrix, mat = mat, props = props });
        }

        // The last whole frame: the latest one if the game has moved on to a new frame since, else the one before.
        public static List<Rec> Take()
        {
            return Time.frameCount != curFrame ? new List<Rec>(cur) : new List<Rec>(last);
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace IsoRim
{
    // Harness keys for the cliff-fix work (cliff script, ProbeCliff.cs):
    //   findseam=storey,skip   a cell on that storey (no edifice; on an upper storey with a floor) whose rough-stone terrain
    //                          meets a different rough stone to the east or north; the skip-th by distance from the centre.
    //                          look=seam,size or look=seam,dx,dz,size then looks at it (dx, dz in cells: a pan strip).
    //   Every shot logs how many SectionLayer_Terrain rebuilds happened since the shot before (regen count).
    public static partial class Probe
    {
        private static IntVec3 seamAt = IntVec3.Invalid;
        private static int terrainRegens;
        private static int terrainRegensUpper;
        private static bool regenWatched;

        private static void WatchTerrainRegens()
        {
            if (regenWatched)
            {
                return;
            }
            regenWatched = true;
            new HarmonyLib.Harmony("isorim.probe.regens").Patch(HarmonyLib.AccessTools.Method(typeof(SectionLayer_Terrain), nameof(SectionLayer_Terrain.Regenerate)),
                postfix: new HarmonyLib.HarmonyMethod(typeof(Probe), nameof(TerrainRegen_Postfix)));
        }

        public static void TerrainRegen_Postfix(SectionLayer_Terrain __instance)
        {
            terrainRegens++;
            Section s = HarmonyLib.Traverse.Create(__instance).Field("section").GetValue<Section>();
            float h;
            int z;
            if (s != null && Floors.ColumnFloor(s.map, s.botLeft, out h, out z) >= 1)
            {
                terrainRegensUpper++;
            }
        }

        private static string RegenNote()
        {
            string n = ", terrain rebuilds since last shot " + terrainRegens + " (upper " + terrainRegensUpper + ")";
            terrainRegens = 0;
            terrainRegensUpper = 0;
            return n;
        }

        // panstrip=dx,dz,n,name: n consecutive frames, the camera moved by (dx, dz) cells before each, every frame captured as
        // renders/<tag>/<name>_000.png ...; the terrain rebuilds per frame are logged at the end.
        private static int panLeft;
        private static int panIndex;
        private static Vector3 panStep;
        private static string panName;
        private static readonly List<int> panRegens = new List<int>();
        private static readonly HarmonyLib.AccessTools.FieldRef<CameraDriver, Vector3> camRoot = HarmonyLib.AccessTools.FieldRefAccess<CameraDriver, Vector3>("rootPos");

        private static void StartPanStrip(float dx, float dz, int n, string name)
        {
            WatchTerrainRegens();
            panStep = new Vector3(dx, 0f, dz);
            panLeft = n;
            panIndex = 0;
            panName = name;
            panRegens.Clear();
            terrainRegens = 0;
            terrainRegensUpper = 0;
            foreach (Window w in new List<Window>(Find.WindowStack.Windows))
            {
                Find.WindowStack.TryRemove(w, doCloseSound: false);
            }
        }

        private static bool PanStripTick()
        {
            if (panLeft <= 0)
            {
                return false;
            }
            panRegens.Add(terrainRegens);
            terrainRegens = 0;
            CameraDriver cd = Find.CameraDriver;
            cd.SetRootPosAndSize(camRoot(cd) + panStep, cd.RootSize);
            string dir = System.IO.Path.Combine(GenFilePaths.SaveDataFolderPath, "renders", HarnessTag());
            System.IO.Directory.CreateDirectory(dir);
            ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, panName + "_" + panIndex.ToString("000") + ".png"));
            panIndex++;
            if (--panLeft == 0)
            {
                Log.Message("[IsoRim probe] panstrip " + panName + ": " + panIndex + " frames, terrain rebuilds per frame " + string.Join(",", panRegens));
                scriptWait = 10;
            }
            return true;
        }

        // findore=storey,skip: a fogged ore (resource rock) on that storey with natural rock on the storey above it (deep in
        // the mountain), the skip-th by distance from the centre; seamAt is set to it (look=seam,...).
        private static void FindOre(Map map, int storey, int skip)
        {
            int rows = Floors.GroundRowsOf(map);
            List<KeyValuePair<float, IntVec3>> found = new List<KeyValuePair<float, IntVec3>>();
            IntVec3 centre = new IntVec3(map.Size.x / 2, 0, rows / 2);
            for (int x = 10; x < map.Size.x - 10; x++)
            {
                for (int z = 10; z < rows - 10; z++)
                {
                    IntVec3 g = new IntVec3(x, 0, z);
                    IntVec3 c = storey == 0 ? g : Floors.Stacked(map, g, storey);
                    IntVec3 up = Floors.Stacked(map, g, storey + 1);
                    if (!c.IsValid || !c.InBounds(map) || !up.IsValid || !up.InBounds(map) || !map.fogGrid.IsFogged(c))
                    {
                        continue;
                    }
                    Building b = c.GetEdifice(map);
                    Building a = up.GetEdifice(map);
                    if (b != null && b.def.building != null && b.def.building.isResourceRock && a != null && a.def.building != null && a.def.building.isNaturalRock)
                    {
                        found.Add(new KeyValuePair<float, IntVec3>((g - centre).LengthHorizontalSquared, g));
                    }
                }
            }
            found.Sort((p, q) => p.Key.CompareTo(q.Key));
            if (found.Count > skip)
            {
                seamAt = found[skip].Value;
            }
            IntVec3 at = seamAt.IsValid ? (storey == 0 ? seamAt : Floors.Stacked(map, seamAt, storey)) : IntVec3.Invalid;
            Log.Message("[IsoRim probe] findore storey " + storey + ": " + found.Count + " found, using " + seamAt
                + (at.IsValid && at.InBounds(map) && at.GetEdifice(map) != null ? " (" + at.GetEdifice(map).def.defName + ")" : ""));
        }

        // findcap=storey,skip: an unfogged natural rock on that storey (not ore) with a mountain top (a floor, nothing on it)
        // beside it to the south and west: with that floor viewed the rock's cap and the top's terrain lie side by side.
        private static void FindCap(Map map, int storey, int skip)
        {
            int rows = Floors.GroundRowsOf(map) > 0 ? Floors.GroundRowsOf(map) : map.Size.z;   // (a plain map: its rows)
            List<KeyValuePair<float, IntVec3>> found = new List<KeyValuePair<float, IntVec3>>();
            IntVec3 centre = new IntVec3(map.Size.x / 2, 0, rows / 2);
            Func<IntVec3, IntVec3> at = g => storey == 0 ? g : Floors.Stacked(map, g, storey);
            for (int x = 10; x < map.Size.x - 10; x++)
            {
                for (int z = 10; z < rows - 10; z++)
                {
                    IntVec3 g = new IntVec3(x, 0, z);
                    IntVec3 c = at(g);
                    if (!c.IsValid || !c.InBounds(map) || map.fogGrid.IsFogged(c))
                    {
                        continue;
                    }
                    Building b = c.GetEdifice(map);
                    if (b == null || b.def.building == null || !b.def.building.isNaturalRock || b.def.building.isResourceRock)
                    {
                        continue;
                    }
                    bool ok = true;
                    foreach (IntVec3 d in new[] { IntVec3.South, IntVec3.West, IntVec3.South * 2, IntVec3.West * 2 })
                    {
                        IntVec3 n = at(g + d);
                        ok &= n.IsValid && n.InBounds(map) && n.GetEdifice(map) == null && (storey == 0 || Floors.HasFloor(map, n)) && !map.fogGrid.IsFogged(n)
                            && RoughStone(map.terrainGrid.TerrainAt(n));
                    }
                    if (ok)
                    {
                        found.Add(new KeyValuePair<float, IntVec3>((g - centre).LengthHorizontalSquared, g));
                    }
                }
            }
            found.Sort((p, q) => p.Key.CompareTo(q.Key));
            if (found.Count > skip)
            {
                seamAt = found[skip].Value;
            }
            Log.Message("[IsoRim probe] findcap storey " + storey + ": " + found.Count + " found, using " + seamAt);
        }

        private static bool RoughStone(TerrainDef t)
        {
            return t != null && t.defName.EndsWith("_Rough");
        }

        // findtop=storey,skip: the middle of a 9x9 patch of mountain top on that storey (a floor, nothing on it, natural
        // rock under every cell), the skip-th by distance from the centre; logged, and look=seam,... then looks at it
        // (seamAt is reused). digcorner=dx,dz digs, on the storey below, the three cells round the top cell's north-east
        // corner other than the top cell itself (cells offset by dx, dz from it): its rock is left with a convex corner
        // under the floor.
        private static void FindTop(Map map, int storey, int skip)
        {
            seamAt = IntVec3.Invalid;
            int rows = Floors.GroundRowsOf(map);
            List<KeyValuePair<float, IntVec3>> found = new List<KeyValuePair<float, IntVec3>>();
            IntVec3 centre = new IntVec3(map.Size.x / 2, 0, rows / 2);
            Func<IntVec3, bool> top = g =>
            {
                IntVec3 c = Floors.Stacked(map, g, storey);
                IntVec3 b = Floors.Stacked(map, g, storey - 1);
                if (!c.IsValid || !c.InBounds(map) || !b.IsValid || !b.InBounds(map) || c.GetEdifice(map) != null || !Floors.HasFloor(map, c))
                {
                    return false;
                }
                Building r = b.GetEdifice(map);
                return r != null && r.def.building != null && r.def.building.isNaturalRock && !r.def.building.isResourceRock;
            };
            for (int x = 10; x < map.Size.x - 10; x++)
            {
                for (int z = 10; z < rows - 10; z++)
                {
                    bool ok = true;
                    for (int dx = -4; dx <= 4 && ok; dx++)
                    {
                        for (int dz = -4; dz <= 4 && ok; dz++)
                        {
                            ok = top(new IntVec3(x + dx, 0, z + dz));
                        }
                    }
                    if (ok)
                    {
                        found.Add(new KeyValuePair<float, IntVec3>((new IntVec3(x, 0, z) - centre).LengthHorizontalSquared, new IntVec3(x, 0, z)));
                    }
                }
            }
            found.Sort((p, q) => p.Key.CompareTo(q.Key));
            if (found.Count > skip)
            {
                seamAt = found[skip].Value;
            }
            Log.Message("[IsoRim probe] findtop storey " + storey + ": " + found.Count + " found, using " + seamAt);
        }

        // holescan=storey,skip: cells of that storey whose slab top over rock is cut at a corner (the rock below is convex
        // there) while a cell beside that corner on the same storey still has a floor or a block: a gap in the top.
        private static void HoleScan(Map map, int storey, int skip)
        {
            int rows = Floors.GroundRowsOf(map);
            int n = 0;
            List<string> first = new List<string>();
            int[] cx = { 0, 0, 1, 1 };
            int[] cz = { 0, 1, 1, 0 };
            for (int x = 1; x < map.Size.x - 1; x++)
            {
                for (int z = 1; z < rows - 1; z++)
                {
                    IntVec3 g = new IntVec3(x, 0, z);
                    IntVec3 c = Floors.Stacked(map, g, storey);
                    Vector3[] q;
                    if (!c.IsValid || !c.InBounds(map) || !Floors.HasFloor(map, c) || !WallBoxes.SlabOverRock(map, c, out q))
                    {
                        continue;
                    }
                    for (int k = 0; k < 4; k++)
                    {
                        if (Mathf.Abs(q[k].x - cx[k]) < 0.01f && Mathf.Abs(q[k].z - cz[k]) < 0.01f)
                        {
                            continue;
                        }
                        // the two cells sharing an edge with this one at that corner
                        IntVec3 sx = c + new IntVec3(cx[k] == 0 ? -1 : 1, 0, 0);
                        IntVec3 sz = c + new IntVec3(0, 0, cz[k] == 0 ? -1 : 1);
                        float h;
                        bool covered = sx.InBounds(map) && (Floors.HasFloor(map, sx) || Floors.FullBlock(map, sx, out h))
                            || sz.InBounds(map) && (Floors.HasFloor(map, sz) || Floors.FullBlock(map, sz, out h));
                        if (covered)
                        {
                            if (n == skip)
                            {
                                seamAt = g;
                            }
                            n++;
                            if (first.Count < 12)
                            {
                                first.Add(g.x + "," + g.z + "/k" + k);
                            }
                        }
                    }
                }
            }
            Log.Message("[IsoRim probe] holescan storey " + storey + ": " + n + " cut corners beside a floor, e.g. " + string.Join(" ", first) + "; using " + seamAt);
        }

        private static void FindSeam(Map map, int storey, int skip)
        {
            WatchTerrainRegens();
            seamAt = IntVec3.Invalid;
            int rows = Floors.GroundRowsOf(map);
            int zMax = rows > 0 ? rows : map.Size.z;
            List<KeyValuePair<float, IntVec3>> found = new List<KeyValuePair<float, IntVec3>>();
            IntVec3 centre = new IntVec3(map.Size.x / 2, 0, zMax / 2);
            Func<IntVec3, IntVec3> at = g => storey == 0 ? g : Floors.Stacked(map, g, storey);
            Func<IntVec3, bool> open = c => c.IsValid && c.InBounds(map) && c.GetEdifice(map) == null && (storey == 0 || Floors.HasFloor(map, c)) && !map.fogGrid.IsFogged(c);
            for (int x = 8; x < map.Size.x - 8; x++)
            {
                for (int z = 8; z < zMax - 8; z++)
                {
                    IntVec3 g = new IntVec3(x, 0, z);
                    IntVec3 c = at(g);
                    if (!open(c))
                    {
                        continue;
                    }
                    TerrainDef t = map.terrainGrid.TerrainAt(c);
                    if (!RoughStone(t))
                    {
                        continue;
                    }
                    int same = 0, other = 0;
                    for (int dx = -2; dx <= 2; dx++)
                    {
                        for (int dz = -2; dz <= 2; dz++)
                        {
                            IntVec3 n = at(new IntVec3(x + dx, 0, z + dz));
                            if (!open(n))
                            {
                                continue;
                            }
                            TerrainDef tn = map.terrainGrid.TerrainAt(n);
                            if (tn == t) same++;
                            else if (RoughStone(tn)) other++;
                        }
                    }
                    if (same >= 8 && other >= 8)
                    {
                        found.Add(new KeyValuePair<float, IntVec3>((g - centre).LengthHorizontalSquared, g));
                    }
                }
            }
            found.Sort((p, q) => p.Key.CompareTo(q.Key));
            if (found.Count > skip)
            {
                seamAt = found[skip].Value;
            }
            Log.Message("[IsoRim probe] findseam storey " + storey + ": " + found.Count + " found, using " + seamAt
                + (seamAt.IsValid ? " (" + map.terrainGrid.TerrainAt(at(seamAt)).defName + ")" : ""));
        }
    }
}

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

        private static bool RoughStone(TerrainDef t)
        {
            return t != null && t.defName.EndsWith("_Rough");
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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace IsoRim
{
    // The gravship takeoff (and later landing) cutscene in the 3D view (plan: isorim-gravship-launch-plan.md).
    //
    // Vanilla photographs the ship from above (GravshipCapturer), takes it off the map and flies that one flat picture
    // along its curves (GravshipRenderer). In 3D the picture is wrong (the capture camera looks with IsoRim's matrices)
    // and a flat sheet. Instead, just before the game takes the ship off the map (GravshipUtility.GenerateGravship),
    // a snapshot of its 3D geometry is made (ShipSnapshot): the triangles of the map's section meshes that belong to the
    // ship's cells, combined per material. Every frame of the cutscene the snapshot is flown along vanilla's own curves,
    // with vanilla's fake height (a slide up the screen) turned into a real height, and the camera follows it.
    //
    // One switch: the mode is decided when the cutscene starts (the 3D view ready); otherwise every hook passes through
    // to vanilla. While any cutscene runs, IsoRim's own camera keys are locked (View3D.ControlsLocked), as vanilla locks
    // its own camera.
    [StaticConstructorOnStartup]
    public static class ShipFlight
    {
        // Harness only (probe shipflight=0): the vanilla cutscene in 3D (before/after shots).
        public static bool Enabled = true;

        private static bool mode3D;      // this cutscene is drawn in 3D
        private static bool takeoff;
        private static ShipSnapshot snap;

        // The pose drawn this frame (true heights).
        private static bool posed;
        private static Vector3 ground;
        private static float height;
        private static Quaternion turn = Quaternion.identity;

        // The camera: the player's view before the cutscene, and the followed one.
        private static bool camSaved;
        private static Vector3 savedPos;
        private static float savedSize;
        private static bool camFollowing;
        private static Vector3 camFocus;
        private static float camSize;

        private static readonly AccessTools.FieldRef<GravshipRenderer, Gravship> rGravship = AccessTools.FieldRefAccess<GravshipRenderer, Gravship>("gravship");
        private static readonly AccessTools.FieldRef<GravshipRenderer, Vector3> rOffset = AccessTools.FieldRefAccess<GravshipRenderer, Vector3>("gravshipOffset");
        private static readonly AccessTools.FieldRef<GravshipRenderer, Vector3> rCenter = AccessTools.FieldRefAccess<GravshipRenderer, Vector3>("takeoffOrLandingPosition");
        private static readonly AccessTools.FieldRef<GravshipRenderer, Vector3> rEnginePos = AccessTools.FieldRefAccess<GravshipRenderer, Vector3>("takeoffOrLandingEnginePos");
        private static readonly AccessTools.FieldRef<GravshipRenderer, MaterialPropertyBlock> rFlameBlock = AccessTools.FieldRefAccess<GravshipRenderer, MaterialPropertyBlock>("thrusterFlameBlock");
        private static readonly AccessTools.FieldRef<GravshipRenderer, Dictionary<Thing, EventQueue>> rExhaustTimers = AccessTools.FieldRefAccess<GravshipRenderer, Dictionary<Thing, EventQueue>>("exhaustTimers");
        private static readonly AccessTools.FieldRef<CameraDriver, Vector3> camRootPos = AccessTools.FieldRefAccess<CameraDriver, Vector3>("rootPos");
        private static readonly AccessTools.FieldRef<WorldComponent_GravshipController, Gravship> cGravship = AccessTools.FieldRefAccess<WorldComponent_GravshipController, Gravship>("gravship");
        private static readonly Action<GravshipRenderer, CompProperties_GravshipThruster.ExhaustSettings, Vector3, Quaternion, Quaternion> emitSmoke =
            AccessTools.MethodDelegate<Action<GravshipRenderer, CompProperties_GravshipThruster.ExhaustSettings, Vector3, Quaternion, Quaternion>>(AccessTools.Method(typeof(GravshipRenderer), "EmitSmoke"));
        private static readonly Material matExtenderGlow = AccessTools.Field(typeof(GravshipRenderer), "MatGravFieldExtenderGlow").GetValue(null) as Material;
        private static readonly Material matEngineGlow = AccessTools.Field(typeof(GravshipRenderer), "MatGravEngineGlow").GetValue(null) as Material;
        private static readonly int Color2 = Shader.PropertyToID("_Color2");

        static ShipFlight()
        {
            if (!ModsConfig.OdysseyActive)
            {
                return;
            }
            try
            {
                Harmony h = new Harmony("isorim.gravship");
                Type ctl = typeof(WorldComponent_GravshipController);
                h.Patch(AccessTools.Method(ctl, nameof(WorldComponent_GravshipController.InitiateTakeoff)), postfix: new HarmonyMethod(typeof(ShipFlight), nameof(InitiateTakeoff_Postfix)));
                h.Patch(AccessTools.Method(ctl, nameof(WorldComponent_GravshipController.InitiateLanding)), postfix: new HarmonyMethod(typeof(ShipFlight), nameof(InitiateLanding_Postfix)));
                h.Patch(AccessTools.Method(typeof(GravshipUtility), nameof(GravshipUtility.GenerateGravship)), prefix: new HarmonyMethod(typeof(ShipFlight), nameof(GenerateGravship_Prefix)));
                h.Patch(AccessTools.Method(typeof(GravshipRenderer), nameof(GravshipRenderer.UpdateTakeoff)), prefix: new HarmonyMethod(typeof(ShipFlight), nameof(UpdateTakeoff_Prefix)));
                h.Patch(AccessTools.Method(ctl, "DrawTerrainCurtain"), prefix: new HarmonyMethod(typeof(ShipFlight), nameof(DrawTerrainCurtain_Prefix)));
                h.Patch(AccessTools.Method(ctl, "DrawGravship"), postfix: new HarmonyMethod(typeof(ShipFlight), nameof(DrawGravship_Postfix)));
                h.Patch(AccessTools.Method(ctl, "ResetCutscene"), postfix: new HarmonyMethod(typeof(ShipFlight), nameof(End)));
                h.Patch(AccessTools.Method(ctl, nameof(WorldComponent_GravshipController.FinalizeInit)), postfix: new HarmonyMethod(typeof(ShipFlight), nameof(End)));
                ModLog.Note("[IsoRim] Hooked the gravship cutscene (3D ship flight).");
            }
            catch (Exception e)
            {
                ModLog.Warn("[IsoRim] Could not hook the gravship cutscene: " + e.Message);
            }
        }

        // ---- start and end

        public static void InitiateTakeoff_Postfix()
        {
            Begin(true);
        }

        public static void InitiateLanding_Postfix()
        {
            Begin(false);
        }

        private static void Begin(bool isTakeoff)
        {
            Clear();
            takeoff = isTakeoff;
            mode3D = Enabled && View3D.Ready;
            if (!mode3D)
            {
                return;
            }
            if (FirstPerson.On)
            {
                FirstPerson.Leave(null);
            }
            CameraDriver cd = Find.CameraDriver;
            if (cd != null)
            {
                savedPos = camRootPos(cd);
                savedSize = cd.RootSize;
                camSaved = true;
            }
        }

        // The cutscene ended (or a game was loaded): after a takeoff the map camera goes back to the player's view (the
        // game shows the world now; the landing hands the camera back where the ship landed); everything is dropped.
        public static void End()
        {
            if (mode3D && takeoff && camSaved && Find.CameraDriver != null)
            {
                Find.CameraDriver.SetRootPosAndSize(savedPos, savedSize);
            }
            Clear();
            mode3D = false;
        }

        private static void Clear()
        {
            if (snap != null)
            {
                snap.Dispose();
                snap = null;
            }
            posed = false;
            camSaved = false;
            camFollowing = false;
        }

        // ---- the snapshot: the ship is still on the map here, the game takes it off right after

        public static void GenerateGravship_Prefix(Building_GravEngine engine)
        {
            if (!mode3D || !takeoff || !WorldComponent_GravshipController.CutsceneInProgress || engine == null || !engine.Spawned)
            {
                return;
            }
            try
            {
                if (snap != null)
                {
                    snap.Dispose();
                }
                snap = ShipSnapshot.Build(engine);
            }
            catch (Exception e)
            {
                snap = null;
                Log.ErrorOnce("[IsoRim] Making the 3D snapshot of the gravship failed (the game's flat cutscene is shown): " + e, 93611501);
            }
        }

        // ---- drawing

        public static bool UpdateTakeoff_Prefix(GravshipRenderer __instance, float progress)
        {
            if (!mode3D || snap == null)
            {
                return true;
            }
            try
            {
                Takeoff(__instance, progress);
                return false;
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[IsoRim] Flying the 3D gravship failed (the game's flat cutscene is shown): " + e, 93611502);
                snap.Dispose();
                snap = null;
                return true;
            }
        }

        public static bool DrawTerrainCurtain_Prefix()
        {
            // The curtain is a flat picture of the landing area taken with IsoRim's camera: laid flat it is wrong in 3D.
            return !mode3D;
        }

        private static void Takeoff(GravshipRenderer r, float progress)
        {
            Gravship g = rGravship(r);
            // ---- vanilla 1.7 GravshipRenderer.UpdateTakeoff (the curves; only the slide up the screen becomes a height)
            float num = Mathf.Pow(progress, 3.5f);
            float slide = Find.Camera.orthographicSize * 2.5f * num;
            Vector3 normalized = g.launchDirection.ToVector3().normalized;
            float num2 = (Mathf.Approximately(normalized.x, 1f) || Mathf.Approximately(normalized.z, -1f)) ? (-1f) : 1f;
            float y = Mathf.SmoothStep(0f, num2 * 20f, Mathf.InverseLerp(0.2f, 1f, progress + 0.02f * Mathf.Sin(23.561945f * progress)));
            Quaternion quaternion = Quaternion.Euler(0f, y, 0f);
            rOffset(r) += quaternion * normalized * (25f * Time.deltaTime * Mathf.InverseLerp(0.2f, 1f, progress));
            // ---- end vanilla
            ground = rCenter(r) + rOffset(r);
            height = HeightFor(slide);
            turn = quaternion;
            posed = true;
            snap.Draw(ground, height, turn);
            Flames(r, g, rEnginePos(r) + rOffset(r), progress);
        }

        // Vanilla's fake height is a slide up the screen by that many cells; a point raised h cells moves h cos(pitch)
        // up the screen, so h = slide / cos(pitch). Kept well below the eye (and the near plane).
        private static float HeightFor(float slide)
        {
            float c = Mathf.Max(0.05f, Mathf.Cos(View3D.Pitch * Mathf.Deg2Rad));
            float cap = Mathf.Max(2f, 0.4f * View3D.EyePosition.y);
            return Mathf.Min(slide / c, cap);
        }

        // Thruster flames, the field extender and engine glows and the exhaust: vanilla's (GravshipRenderer.DrawGravship,
        // vanilla 1.7), still flat quads, lifted to the ship's height. (Phase 3 stands them up.)
        private static void Flames(GravshipRenderer r, Gravship g, Vector3 enginePos, float progress)
        {
            if (progress <= 0f)
            {
                return;
            }
            Vector3 lift = new Vector3(0f, height / View3D.Squash, 0f);
            Color value = new Color(1f, 1f, 1f, 1f);
            value *= Mathf.Lerp(0.75f, 1f, Mathf.PerlinNoise1D(progress * 100f));
            value.a = Mathf.InverseLerp(0f, 0.1f, progress);
            MaterialPropertyBlock block = rFlameBlock(r);
            Dictionary<Thing, EventQueue> timers = rExhaustTimers(r);
            foreach (KeyValuePair<Thing, PositionData.Data> kv in g.ThrusterPlacements)
            {
                Thing thing = kv.Key;
                PositionData.Data data = kv.Value;
                if (data.rotation.AsIntVec3 == -g.launchDirection)
                {
                    continue;
                }
                CompProperties_GravshipThruster props = thing.TryGetComp<CompGravshipThruster>().Props;
                float size = thing.def.size.x * props.flameSize;
                Vector3 offset = thing.Rotation.AsQuat * props.flameOffsetsPerDirection[thing.Rotation.AsInt];
                Vector3 local = GenThing.TrueCenter(data.local, thing.Rotation, thing.def.size, 0f) - thing.Rotation.AsIntVec3.ToVector3() * (thing.def.size.z * 0.5f + size * 0.5f) + offset;
                Vector3 pos = Turned(enginePos + local).SetToAltitude(AltitudeLayer.Skyfaller).WithYOffset(0.07317074f);
                MaterialRequest req = new MaterialRequest(props.FlameShaderType.Shader);
                req.renderQueue = 3201;
                Material mat = MaterialPool.MatFrom(req);
                block.Clear();
                block.SetColor(Color2, value);
                foreach (ShaderParameter p in props.flameShaderParameters)
                {
                    p.Apply(block);
                }
                Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(pos + lift, turn * data.rotation.AsQuat, new Vector3(size, 1f, size)), mat, 0, null, 0, block);
                EventQueue q;
                if (props.exhaustSettings != null && props.exhaustSettings.enabled && timers != null && timers.TryGetValue(thing, out q))
                {
                    q.Push(Time.deltaTime);
                    while (q.Pop())
                    {
                        emitSmoke(r, props.exhaustSettings, pos, turn, data.rotation.AsQuat);
                    }
                }
            }
            if (matExtenderGlow != null)
            {
                matExtenderGlow.SetColor(Color2, value);
                foreach (IntVec3 c in g.GravFieldExtenderPositions)
                {
                    Vector3 p = Turned(enginePos + c.ToVector3() + ThingDefOf.GravFieldExtender.graphicData.drawSize.ToVector3() * 0.5f)
                        .SetToAltitude(AltitudeLayer.MetaOverlays).WithYOffset(0.07317074f);
                    Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(p + lift, Quaternion.identity, new Vector3(8f, 1f, 8f)), matExtenderGlow, 0);
                }
            }
            if (matEngineGlow != null)
            {
                matEngineGlow.SetColor(Color2, value);
                Vector3 p = Turned(enginePos + g.Engine.def.Size.ToVector3() * 0.25f).SetToAltitude(AltitudeLayer.MetaOverlays).WithYOffset(0.07317074f);
                Graphics.DrawMesh(MeshPool.plane10, Matrix4x4.TRS(p + lift, Quaternion.identity, new Vector3(12.5f, 1f, 12.5f)), matEngineGlow, 0);
            }
        }

        private static Vector3 Turned(Vector3 p)
        {
            return turn * (p - ground) + ground;
        }

        // ---- the camera: follows the ship (eased), keeps the player's turn and tilt, and zooms out so the whole ship
        // fits, at least as far as vanilla zooms (DrawGravship sets its size first; this runs after it and wins)

        public static void DrawGravship_Postfix(WorldComponent_GravshipController __instance, float progress)
        {
            if (!mode3D || !posed || snap == null)
            {
                return;
            }
            CameraDriver cd = Find.CameraDriver;
            Map map = Find.CurrentMap;
            if (cd == null || map == null || cd.config.gravshipFreeCam)
            {
                return;
            }
            try
            {
                Follow(__instance, cd, map, progress);
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[IsoRim] Following the gravship with the camera failed: " + e, 93611503);
            }
        }

        private static void Follow(WorldComponent_GravshipController ctl, CameraDriver cd, Map map, float progress)
        {
            if (!camFollowing)
            {
                camFollowing = true;
                camFocus = camRootPos(cd);
                camFocus.y = 0f;
                camSize = cd.RootSize;
            }
            float dt = Time.deltaTime;
            Vector3 fwd = View3D.Forward;
            // The ground point seen behind the ship's middle (along the line of sight), kept on the map.
            Vector3 mid = snap.Middle(ground, height, turn);
            Vector3 target = fwd.y < -0.01f ? mid + fwd * (mid.y / -fwd.y) : mid;
            target.x = Mathf.Clamp(target.x, 2f, map.Size.x - 2f);
            target.z = Mathf.Clamp(target.z, 2f, map.Size.z - 2f);
            target.y = 0f;
            camFocus = Vector3.Lerp(camFocus, target, 1f - Mathf.Exp(-4f * dt));

            // Vanilla's zoom for this progress (WorldComponent_GravshipController.DrawGravship / GetCutsceneZoomRange, 1.7).
            CameraMapConfig config = cd.config;
            Gravship g = cGravship(ctl);
            FloatRange range = config.gravshipEnableOverrideSizeRange || g == null || g.capture == null
                ? config.gravshipOverrideSizeRange : new FloatRange(g.capture.minCameraSize, g.capture.maxCameraSize);
            float x = progress.RemapClamped(0f, takeoff ? 0.7f : 0.8f, 0f, 1f);
            float eased = x < 0.5f ? 4f * x * x * x : 1f - Mathf.Pow(-2f * x + 2f, 3f) * 0.5f;
            float vanillaSize = takeoff ? Mathf.Lerp(range.min, range.max, eased) : Mathf.Lerp(range.max, range.min, eased);
            float fit = snap.FitSize(ground, height, turn, camFocus, Find.Camera.aspect);
            float want = Mathf.Min(Mathf.Max(vanillaSize, fit), config.sizeRange.max * 1.5f);
            camSize = Mathf.Lerp(camSize, want, Mathf.Clamp01(5f * dt));

            Vector3 root = camRootPos(cd);
            cd.SetRootPosAndSize(new Vector3(camFocus.x, root.y, camFocus.z), camSize);
        }

        // For the harness log.
        public static string Describe()
        {
            if (!mode3D)
            {
                return "flight: vanilla";
            }
            if (snap == null)
            {
                return "flight: 3D, no snapshot";
            }
            return "flight: 3D, ground " + ground.ToString("F1") + " height " + height.ToString("0.0") + " turn " + turn.eulerAngles.y.ToString("0.0")
                + ", camera focus " + camFocus.ToString("F1") + " size " + camSize.ToString("0.0");
        }
    }

    // The ship's 3D geometry at takeoff, combined per material, in drawn space relative to the cutscene's centre (the
    // centre of the substructure's bounds widened by one cell, as vanilla's capture).
    //
    // The ship's cells S: the valid substructure, the hull corner pieces standing beside it (HullCorners) and the cells
    // of buildings on it. From the section meshes over S (an allow-list of layers, regenerated first so they are
    // current), a triangle belongs to the ship when:
    //   - it carries a cut-away / roof-clip tag (texture coordinate 1): its owner cell is in S. Of the cut-away flags,
    //     a twig (shown only for a thinned tree) is dropped, and a side shown only while a neighbour is lower (Cond) is
    //     dropped when that neighbour is in S too (an inner join) and kept when it is outside (exposed in flight);
    //   - otherwise, the cell under its middle is in S;
    //   - and a flat triangle (all of it on the ground) only when the cell under its middle is substructure (so the
    //     ground bands and lips a hull cell prints around itself stay on the ground). The game's substructure props and
    //     hull layers draw their lips and corner ground pieces just outside the deck: for those, one ring more.
    // Tag data is dropped (texture coordinate 1 = 0): the copy is never cut away or roof-clipped, and nothing in it
    // reads the map's cell textures at the old cells.
    public sealed class ShipSnapshot
    {
        private const int MaxVerts = 65000;   // 16-bit indices (UInt32 index support in Waylith unknown)

        private readonly List<Mesh> meshes = new List<Mesh>();
        private readonly List<Material> mats = new List<Material>();      // drawn with
        private readonly List<Material> sources = new List<Material>();   // the section's own (for the light-pass mark)
        private readonly List<int> layers = new List<int>();
        private readonly List<bool> depths = new List<bool>();
        private Vector3 pivot;
        private Vector3 min, max;   // drawn x, z relative to the pivot; y in true cells

        private static readonly AccessTools.FieldRef<Section, List<SectionLayer>> sectionLayers = AccessTools.FieldRefAccess<Section, List<SectionLayer>>("layers");
        private static readonly int[] DX = { 0, 0, 0, -1, 1, -1, 1, -1, 1 };   // ShaderCut direction codes 1-8
        private static readonly int[] DZ = { 0, -1, 1, 0, 0, -1, -1, 1, 1 };
        private static readonly HashSet<string> skippedLogged = new HashSet<string>();

        // Cell classes.
        private const byte Ring = 1, Ship = 2, Deck = 3;

        private sealed class Chunk
        {
            public readonly List<Vector3> verts = new List<Vector3>();
            public readonly List<int> tris = new List<int>();
            public readonly List<Color32> colors = new List<Color32>();
            public readonly List<Vector3> uvs = new List<Vector3>();
            public readonly List<Vector3> normals = new List<Vector3>();
            public bool hasNormals;
        }

        private sealed class Group
        {
            public Material mat;
            public int layer;
            public bool depth;   // the deck's terrain: its depth is written first (DepthMat)
            public readonly List<Chunk> chunks = new List<Chunk>();
        }

        public int Vertices { get; private set; }
        public int Triangles { get; private set; }

        public static ShipSnapshot Build(Building_GravEngine engine)
        {
            Stopwatch watch = Stopwatch.StartNew();
            Map map = engine.Map;
            HashSet<IntVec3> deck = engine.ValidSubstructure;
            CellRect bounds = CellRect.FromCellList(deck).ExpandedBy(1);   // vanilla's capture bounds (GravshipCapturer)
            ShipSnapshot s = new ShipSnapshot { pivot = bounds.CenterVector3 };
            s.pivot.y = 0f;

            // ---- the ship's cells
            CellRect area = bounds.ExpandedBy(4);
            byte[] cls = new byte[area.Width * area.Height];
            Func<int, int, int> at = (x, z) => x < area.minX || z < area.minZ || x > area.maxX || z > area.maxZ ? -1 : (z - area.minZ) * area.Width + (x - area.minX);
            foreach (IntVec3 c in deck)
            {
                cls[at(c.x, c.z)] = Deck;
            }
            foreach (IntVec3 c in bounds)
            {
                int i = at(c.x, c.z);
                if (cls[i] == 0 && c.InBounds(map) && c.GetEdifice(map) == null && HullCorners.At(map, c, out _, out _))
                {
                    cls[i] = Ship;
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
                        int i = at(o.x, o.z);
                        if (i >= 0 && cls[i] < Ship)
                        {
                            cls[i] = Ship;
                        }
                    }
                }
            }
            foreach (IntVec3 c in deck)
            {
                for (int d = 1; d < 9; d++)
                {
                    int i = at(c.x + DX[d], c.z + DZ[d]);
                    if (i >= 0 && cls[i] == 0)
                    {
                        cls[i] = Ring;
                    }
                }
            }
            Func<float, float, byte> cellAt = (x, z) =>
            {
                int i = at(Mathf.FloorToInt(x), Mathf.FloorToInt(z));
                return i < 0 ? (byte)0 : cls[i];
            };

            // ---- the sections over them, current
            Array sections = MapSections.SectionsOf(map);
            if (sections == null)
            {
                return s;
            }
            CellRect over = bounds.ExpandedBy(1).ClipInsideMap(map);
            List<Section> list = new List<Section>();
            for (int sx = over.minX / Section.Size; sx <= over.maxX / Section.Size && sx < sections.GetLength(0); sx++)
            {
                for (int sz = over.minZ / Section.Size; sz <= over.maxZ / Section.Size && sz < sections.GetLength(1); sz++)
                {
                    if (sections.GetValue(sx, sz) is Section sec)
                    {
                        sec.RegenerateAllLayers();
                        list.Add(sec);
                    }
                }
            }
            long regenMs = watch.ElapsedMilliseconds;

            // ---- gathering
            float squash = View3D.Squash;
            Dictionary<string, int[]> perLayer = new Dictionary<string, int[]>();
            List<Group> groups = new List<Group>();
            List<string> skipped = new List<string>();
            int[] remap = new int[0];
            foreach (Section sec in list)
            {
                foreach (SectionLayer layer in sectionLayers(sec))
                {
                    int kind = Kind(layer);
                    if (kind == 0)
                    {
                        if (Prefs.DevMode && layer.Visible && Touches(layer, cellAt))
                        {
                            skipped.Add(layer.GetType().Name);
                        }
                        continue;
                    }
                    if (!layer.Visible)
                    {
                        continue;
                    }
                    string lname = layer.GetType().Name;
                    int[] stat;
                    if (!perLayer.TryGetValue(lname, out stat))
                    {
                        perLayer[lname] = stat = new int[2];
                    }
                    byte flatNeed = kind == 2 ? Ring : Deck;
                    foreach (LayerSubMesh sm in layer.subMeshes)
                    {
                        if (!sm.finalized || sm.disabled || sm.verts.Count == 0 || sm.material == null)
                        {
                            continue;
                        }
                        List<Vector3> v = sm.verts;
                        List<int> t = sm.tris;
                        List<Vector3> tag = sm.uvsChannelTwo.Count == v.Count ? sm.uvsChannelTwo : null;
                        Group grp = GroupFor(groups, sm.material, sm.renderLayer);
                        grp.depth |= lname == "SectionLayer_Terrain";
                        Chunk ch = grp.chunks[grp.chunks.Count - 1];
                        if (remap.Length < v.Count)
                        {
                            remap = new int[Math.Max(v.Count, remap.Length * 2)];
                        }
                        for (int i = 0; i < v.Count; i++)
                        {
                            remap[i] = -1;
                        }
                        for (int k = 0; k + 2 < t.Count; k += 3)
                        {
                            int a = t[k], b = t[k + 1], c = t[k + 2];
                            stat[0]++;
                            Vector3 va = v[a], vb = v[b], vc = v[c];
                            float mx = (va.x + vb.x + vc.x) / 3f, mz = (va.z + vb.z + vc.z) / 3f;
                            byte under = cellAt(mx, mz);
                            bool flat = va.y * squash < 0.02f && vb.y * squash < 0.02f && vc.y * squash < 0.02f;
                            int flags = tag != null ? (int)(tag[a].z + 0.5f) : 0;
                            bool keep;
                            if (flags != 0)
                            {
                                int ox = Mathf.FloorToInt(tag[a].x), oz = Mathf.FloorToInt(tag[a].y);
                                keep = cellAt(ox + 0.5f, oz + 0.5f) >= Ship && (flags & ShaderCut.Twig) == 0;
                                int cond = (flags >> ShaderCut.CondShift) & 15;
                                if (keep && cond > 0 && cond < 9 && cellAt(ox + DX[cond] + 0.5f, oz + DZ[cond] + 0.5f) >= Ship)
                                {
                                    keep = false;   // an inner join: hidden on the map, hidden in flight
                                }
                            }
                            else
                            {
                                keep = under >= Ship || (flat && under >= flatNeed);
                            }
                            if (!keep || (flat && under < flatNeed))
                            {
                                continue;
                            }
                            if (ch.verts.Count + 3 > MaxVerts)
                            {
                                ch = new Chunk();
                                grp.chunks.Add(ch);
                                for (int i = 0; i < v.Count; i++)
                                {
                                    remap[i] = -1;
                                }
                            }
                            ch.tris.Add(Vertex(sm, ch, a, remap, s.pivot));
                            ch.tris.Add(Vertex(sm, ch, b, remap, s.pivot));
                            ch.tris.Add(Vertex(sm, ch, c, remap, s.pivot));
                            stat[1]++;
                        }
                    }
                }
            }

            // ---- meshes
            int verts = 0, tris = 0;
            s.min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            s.max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            foreach (Group grp in groups)
            {
                foreach (Chunk ch in grp.chunks)
                {
                    if (ch.tris.Count == 0)
                    {
                        continue;
                    }
                    Mesh m = new Mesh();
                    m.name = "IsoRimShipSnapshot";
                    m.SetVertices(ch.verts);
                    m.SetTriangles(ch.tris, 0);
                    m.SetColors(ch.colors);
                    m.SetUVs(0, ch.uvs);
                    if (ch.hasNormals)
                    {
                        m.SetNormals(ch.normals);
                    }
                    m.RecalculateBounds();
                    s.meshes.Add(m);
                    s.mats.Add(Flying(grp.mat));
                    s.sources.Add(grp.mat);
                    s.depths.Add(grp.depth);
                    s.layers.Add(grp.layer);
                    verts += ch.verts.Count;
                    tris += ch.tris.Count / 3;
                    foreach (Vector3 p in ch.verts)
                    {
                        Vector3 q = new Vector3(p.x, p.y * squash, p.z);
                        s.min = Vector3.Min(s.min, q);
                        s.max = Vector3.Max(s.max, q);
                    }
                }
            }
            if (s.meshes.Count == 0)
            {
                s.min = s.max = Vector3.zero;
            }
            s.Vertices = verts;
            s.Triangles = tris;

            List<string> parts = new List<string>();
            foreach (KeyValuePair<string, int[]> kv in perLayer)
            {
                parts.Add(kv.Key.Replace("SectionLayer_", "") + " " + kv.Value[1] + "/" + kv.Value[0]);
            }
            ModLog.Note("[IsoRim] Gravship snapshot: " + deck.Count + " deck cells, " + list.Count + " sections (regenerated in " + regenMs + " ms), "
                + s.meshes.Count + " meshes, " + verts + " vertices, " + tris + " triangles, about " + ((verts * 40L + tris * 12L) / 1024) + " KB, built in "
                + watch.ElapsedMilliseconds + " ms; triangles kept per layer: " + string.Join(", ", parts)
                + "; box " + s.min.ToString("F1") + " to " + s.max.ToString("F1"));
            if (skipped.Count > 0)
            {
                skipped.RemoveAll(n => !skippedLogged.Add(n));
                if (skipped.Count > 0)
                {
                    ModLog.Warn("[IsoRim] Gravship snapshot: layers with geometry on the ship that are not flown: " + string.Join(", ", skipped));
                }
            }
            return s;
        }

        // 0 = not part of the snapshot, 1 = gathered, 2 = gathered with one ring more for flat pieces (lips).
        private static int Kind(SectionLayer layer)
        {
            switch (layer.GetType().Name)
            {
                case "SectionLayer_ThingsGeneral":
                case "SectionLayer_Terrain":
                case "SectionLayer_IsoRimWalls":
                case "SectionLayer_IsoRimBodies":
                case "SectionLayer_IsoRimRoofs":     // only while roofs are shown (its Visible)
                case "SectionLayer_IsoRimDamage":
                    return 1;
                case "SectionLayer_BuildingsDamage":
                    return IsoRimMod.Settings.damage3D ? 0 : 1;   // drawn only without the 3D marks (Patches.DrawLayer_Section)
                case "SectionLayer_SubstructureProps":
                case "SectionLayer_GravshipHull":
                    return 2;
            }
            return 0;
        }

        // Layers that are not geometry of the ship (overlays, masks, shadows): not reported as skipped.
        private static readonly HashSet<string> notGeometry = new HashSet<string>
        {
            "SectionLayer_LightingOverlay", "SectionLayer_FogOfWar", "SectionLayer_IndoorMask", "SectionLayer_GravshipMask",
            "SectionLayer_SunShadows", "SectionLayer_EdgeShadows", "SectionLayer_BuildingsDamage", "SectionLayer_ThingsPowerGrid"
        };

        private static bool Touches(SectionLayer layer, Func<float, float, byte> cellAt)
        {
            if (notGeometry.Contains(layer.GetType().Name))
            {
                return false;
            }
            foreach (LayerSubMesh sm in layer.subMeshes)
            {
                if (!sm.finalized || sm.disabled)
                {
                    continue;
                }
                for (int i = 0; i < sm.verts.Count; i += 4)
                {
                    if (cellAt(sm.verts[i].x, sm.verts[i].z) >= Ship)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static Group GroupFor(List<Group> groups, Material mat, int layer)
        {
            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i].mat == mat && groups[i].layer == layer)
                {
                    return groups[i];
                }
            }
            Group g = new Group { mat = mat, layer = layer };
            g.chunks.Add(new Chunk());
            groups.Add(g);
            return g;
        }

        private static int Vertex(LayerSubMesh sm, Chunk ch, int i, int[] remap, Vector3 pivot)
        {
            if (remap[i] >= 0)
            {
                return remap[i];
            }
            int n = ch.verts.Count;
            ch.verts.Add(sm.verts[i] - pivot);
            ch.colors.Add(sm.colors.Count > i ? sm.colors[i] : new Color32(255, 255, 255, 255));
            ch.uvs.Add(sm.uvs.Count > i ? sm.uvs[i] : Vector3.zero);
            if (sm.normals.Count > i)
            {
                if (!ch.hasNormals)
                {
                    ch.hasNormals = true;
                    while (ch.normals.Count < n)
                    {
                        ch.normals.Add(Vector3.up);
                    }
                }
                ch.normals.Add(sm.normals[i]);
            }
            else if (ch.hasNormals)
            {
                ch.normals.Add(Vector3.up);
            }
            remap[i] = n;
            return n;
        }

        // The game's ground materials (terrain, the deck's hull and prop pieces: queue 2000-2099) write no depth: drawn in the
        // same queue as the map's own ground, whichever comes later wins, and the ground showed through the flying deck.
        // The ship's copies draw a little later (+50, their order among themselves kept), so they always cover the ground
        // under them; depth tests still let anything standing in front of the ship hide it. IsoRim's own materials write
        // depth and are left as they are.
        private static readonly Dictionary<Material, Material> lifted = new Dictionary<Material, Material>();
        private static HashSet<Shader> ownShaders;

        // The game's terrain writes no depth either, so the flying deck hid nothing drawn after it (ground plants and
        // decals showed through): the deck's terrain first writes its depth alone, before anything of the map is drawn.
        private static Material depthMat;
        private static bool depthTried;

        private static Material DepthMat
        {
            get
            {
                if (!depthTried)
                {
                    depthTried = true;
                    Shader d = R3DMaterials.ShaderOf("IsoRimDepthPlane");
                    if (d != null)
                    {
                        depthMat = new Material(d);
                        depthMat.name = "IsoRim_ShipDeckDepth";
                        depthMat.renderQueue = 1990;
                    }
                }
                return depthMat;
            }
        }

        private static Material Flying(Material m)
        {
            if (m == null || m.renderQueue >= 2100)
            {
                return m;
            }
            if (ownShaders == null)
            {
                ownShaders = new HashSet<Shader>();
                foreach (ShaderDef d in DefDatabase<ShaderDef>.AllDefsListForReading)
                {
                    if (d.defName.StartsWith("IsoRim") && d.Shader != null)
                    {
                        ownShaders.Add(d.Shader);
                    }
                }
            }
            if (ownShaders.Contains(m.shader))
            {
                return m;
            }
            Material copy;
            if (!lifted.TryGetValue(m, out copy) || copy == null)
            {
                copy = new Material(m.shader);
                copy.CopyPropertiesFromMaterial(m);
                copy.renderQueue = m.renderQueue + 50;
                copy.name = m.name + "_ShipFlight";
                lifted[m] = copy;
            }
            return copy;
        }

        // ---- flying: drawn at the ground point (the cutscene centre moved by vanilla's drift), raised by a true height and
        // turned about the vertical (vanilla's wobble). The turn does not mix with the squashed height.

        public void Draw(Vector3 ground, float height, Quaternion turn)
        {
            Matrix4x4 m = Matrix4x4.TRS(new Vector3(ground.x, height / View3D.Squash, ground.z), turn, Vector3.one);
            for (int i = 0; i < meshes.Count; i++)
            {
                if (depths[i] && DepthMat != null)
                {
                    Graphics.DrawMesh(meshes[i], m, DepthMat, layers[i]);
                }
                Graphics.DrawMesh(meshes[i], m, mats[i], layers[i]);
                Material mark = View3D.Ready ? IsoGlow.MarkFor(sources[i]) : null;   // as IsoGlow.DrawLit
                if (mark != null)
                {
                    Graphics.DrawMesh(meshes[i], m, mark, layers[i]);
                }
            }
        }

        // The middle of the ship's box, in true space (map x, height in cells, map z).
        public Vector3 Middle(Vector3 ground, float height, Quaternion turn)
        {
            Vector3 c = turn * new Vector3((min.x + max.x) * 0.5f, 0f, (min.z + max.z) * 0.5f);
            return new Vector3(ground.x + c.x, height + (min.y + max.y) * 0.5f, ground.z + c.z);
        }

        // The camera size (half the screen height, in cells) that shows the whole box round the focus, with a margin.
        public float FitSize(Vector3 ground, float height, Quaternion turn, Vector3 focus, float aspect)
        {
            Vector3 right = View3D.Right, up = View3D.Up;
            float need = 0f;
            for (int k = 0; k < 8; k++)
            {
                Vector3 c = new Vector3((k & 1) == 0 ? min.x : max.x, 0f, (k & 2) == 0 ? min.z : max.z);
                Vector3 p = turn * c + new Vector3(ground.x, height + ((k & 4) == 0 ? min.y : max.y), ground.z);
                Vector3 d = p - focus;
                need = Mathf.Max(need, Mathf.Max(Mathf.Abs(Vector3.Dot(d, up)), Mathf.Abs(Vector3.Dot(d, right)) / Mathf.Max(0.3f, aspect)));
            }
            return need * 1.15f + 2f;
        }

        public void Dispose()
        {
            foreach (Mesh m in meshes)
            {
                if (m != null)
                {
                    UnityEngine.Object.Destroy(m);
                }
            }
            meshes.Clear();
            mats.Clear();
            sources.Clear();
            depths.Clear();
            layers.Clear();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace IsoRim
{
    // Harness-only keys for the gravship takeoff and landing cutscenes (ShipFlight.cs), run through the game's own
    // flow with real ticks: fuel is used, the engine cools down, the ship travels on the world map. A grav anchor on
    // the map lets the ship land back on the same map (layout_gship_launch). Cells are map cells.
    //   gship=launch,cx,cz[,here|<tile>]  what the dev gizmo "DEV: Launch instantly" on the pilot console in cell cx,cz
    //                                does (launch quality 1, no bad outcome, then the destination picker), followed by
    //                                what a click on the tile does (the picker's own check and choice); default: here
    //   gship=land,here|x,z[,rot]    queued: once the ship has arrived and its landing marker waits, the marker put with
    //                                the engine at (x,z) (here: where the engine took off from) facing rot (0-3, default 0),
    //                                as the move designator places it, then what the Confirm button does
    //   cutburst=k,tag[,p:p:...]     screenshots while a cutscene runs: every k-th frame (k = 0: none), and the first frame
    //                                at or after each listed progress (0-1) of the takeoff and of the landing
    //                                (renders\<harness tag>\cut_<tag>_<T|L>_<progress>.png); the progress is logged
    //   shipflight=0|1               the cutscene flown in 3D (ShipFlight.Enabled, default 1) or the game's own (before/after)
    // The harness (Capture17) is held from the launch until the landing has ended (or 6000 frames without one).
    public static partial class Probe
    {
        private static bool gshipHooked;
        private static bool gshipActive;
        private static int gshipFrames;
        private static IntVec3 gshipTakeoffEngine = IntVec3.Invalid;
        private static IntVec3 gshipLandAt = IntVec3.Invalid;
        private static bool gshipLandHere;
        private static Rot4 gshipLandRot = Rot4.North;
        private static bool gshipLandQueued;
        private static int gshipMarkerSeen = -1;
        private static bool gshipLanding;
        private static bool gshipLastCut;
        private static bool gshipLastTakeoff;
        private static string cutTag;
        private static int cutEvery;
        private static int cutFrame;
        private static List<float> cutMarks = new List<float>();
        private static int cutNextT;
        private static int cutNextL;
        private static string gshipLastStage = "";

        private static readonly AccessTools.FieldRef<WorldComponent_GravshipController, float> ctlTimeLeft =
            AccessTools.FieldRefAccess<WorldComponent_GravshipController, float>("timeLeft");
        private static readonly AccessTools.FieldRef<WorldComponent_GravshipController, bool> ctlIsTakeoff =
            AccessTools.FieldRefAccess<WorldComponent_GravshipController, bool>("isTakeoff");
        private static readonly AccessTools.FieldRef<WorldComponent_GravshipController, Gravship> ctlGravship =
            AccessTools.FieldRefAccess<WorldComponent_GravshipController, Gravship>("gravship");
        private static readonly AccessTools.FieldRef<WorldComponent_GravshipController, GravshipLandingMarker> ctlMarker =
            AccessTools.FieldRefAccess<WorldComponent_GravshipController, GravshipLandingMarker>("landingMarker");

        private static bool GshipKey(string key, string[] a, Map map)
        {
            switch (key)
            {
                case "gship":
                    HookGship();
                    if (a[0] == "launch")
                    {
                        GshipLaunch(map, new IntVec3(I(a[1]), 0, I(a[2])), a.Length > 3 ? a[3] : "here");
                    }
                    else if (a[0] == "land")
                    {
                        gshipLandHere = a[1] == "here";
                        gshipLandAt = gshipLandHere ? IntVec3.Invalid : new IntVec3(I(a[1]), 0, I(a[2]));
                        int ri = gshipLandHere ? 2 : 3;
                        gshipLandRot = a.Length > ri ? new Rot4(I(a[ri])) : Rot4.North;
                        gshipLandQueued = true;
                        gshipMarkerSeen = -1;
                    }
                    else
                    {
                        Log.Warning("[IsoRim probe] gship: unknown action " + a[0]);
                    }
                    return true;
                case "shipflight":
                    ShipFlight.Enabled = a[0] == "1";
                    return true;
                case "cutburst":
                    HookGship();
                    cutEvery = Math.Max(0, I(a[0]));
                    cutTag = a.Length > 1 ? a[1] : "c";
                    cutMarks = a.Length > 2 ? a[2].Split(':').Select(s => float.Parse(s, CultureInfo.InvariantCulture)).OrderBy(p => p).ToList() : new List<float>();
                    cutFrame = 0;
                    cutNextT = 0;
                    cutNextL = 0;
                    return true;
            }
            return false;
        }

        // The per-frame step runs after the controller's own update (the cutscene is drawn by then), also while the
        // world map is shown (the map camera does not update then).
        private static void HookGship()
        {
            if (gshipHooked)
            {
                return;
            }
            gshipHooked = true;
            new Harmony("isorim.probe.gship").Patch(AccessTools.Method(typeof(WorldComponent_GravshipController), nameof(WorldComponent_GravshipController.WorldComponentUpdate)),
                postfix: new HarmonyMethod(typeof(Probe), nameof(GshipFrame)));
        }

        private static void GshipLaunch(Map map, IntVec3 consoleCell, string target)
        {
            Building b = consoleCell.GetFirstBuilding(map);
            CompPilotConsole console = b != null ? b.TryGetComp<CompPilotConsole>() : null;
            if (console == null || console.engine == null)
            {
                Log.Error("[IsoRim probe] gship launch: no pilot console with an engine at " + consoleCell);
                return;
            }
            Building_GravEngine engine = console.engine;
            PlanetTile tile = target == "here" ? map.Tile : new PlanetTile(int.Parse(target, CultureInfo.InvariantCulture), map.Tile.Layer);
            Log.Message("[IsoRim probe] gship launch: engine " + engine.Position + ", substructure " + engine.ValidSubstructure.Count
                + " cells, fuel " + engine.TotalFuel.ToString("0") + "/" + engine.MaxFuel.ToString("0") + ", cooldown until " + engine.cooldownCompleteTick
                + " (now " + Find.TickManager.TicksGame + "), anchor " + map.listerThings.AnyThingWithDef(ThingDefOf.GravAnchor) + ", tile " + tile
                + ", 3D " + View3D.Active + ", yaw " + View3D.Yaw.ToString("0") + ", pitch " + View3D.Pitch.ToString("0.0"));
            gshipTakeoffEngine = engine.Position;
            // "DEV: Launch instantly" (CompPilotConsole.CompGetGizmosExtra).
            engine.launchInfo = new LaunchInfo { quality = 1f, doNegativeOutcome = false };
            console.StartChoosingDestination_NewTemp();
            TilePicker picker = Find.TilePicker;
            Func<PlanetTile, bool> validator = Traverse.Create(picker).Field("validator").GetValue<Func<PlanetTile, bool>>();
            Action<PlanetTile> chosen = Traverse.Create(picker).Field("tileChosen").GetValue<Action<PlanetTile>>();
            if (!picker.Active || validator == null || chosen == null)
            {
                Log.Error("[IsoRim probe] gship launch: the destination picker did not start");
                return;
            }
            // What the picker's Next button does with the chosen tile (TilePicker.TileSelectorOnGUI).
            if (!validator(tile))
            {
                Log.Error("[IsoRim probe] gship launch: tile " + tile + " refused by the picker");
                picker.StopTargeting();
                return;
            }
            AccessTools.Method(typeof(TilePicker), "StopTargetingInt").Invoke(picker, null);
            chosen(tile);
            AcceptDialogs("launch");
            gshipActive = true;
            gshipFrames = 0;
            gshipLanding = false;
            Log.Message("[IsoRim probe] gship launch: chosen, cutscene " + WorldComponent_GravshipController.CutsceneInProgress
                + ", fuel left " + engine.TotalFuel.ToString("0"));
        }

        // A confirmation the launch may ask for (settling near other factions) is accepted as a click would.
        private static void AcceptDialogs(string why)
        {
            foreach (Window w in Find.WindowStack.Windows.ToList())
            {
                if (w is Dialog_MessageBox box)
                {
                    Log.Message("[IsoRim probe] gship " + why + ": accepting dialog \"" + box.text + "\"");
                    Find.WindowStack.TryRemove(box, doCloseSound: false);
                    box.buttonAAction?.Invoke();
                }
            }
        }

        public static void GshipFrame()
        {
            try
            {
                GshipStep();
            }
            catch (Exception e)
            {
                Log.Error("[IsoRim probe] gship step failed: " + e);
                gshipActive = false;
            }
        }

        private static void GshipStep()
        {
            if (!gshipActive || Current.ProgramState != ProgramState.Playing || Current.Game == null)
            {
                return;
            }
            HoldHarness();
            gshipFrames++;
            WorldComponent_GravshipController ctl = Find.GravshipController;
            bool cut = WorldComponent_GravshipController.CutsceneInProgress;
            bool takeoff = ctlIsTakeoff(ctl);
            float timeLeft = ctlTimeLeft(ctl);
            bool drawing = cut && ctlGravship(ctl) != null && timeLeft > 0f;
            float progress = Mathf.Clamp01(1f - timeLeft / 10f);

            string stage = !cut ? (ctl.IsGravshipTravelling ? "travelling" : (ctl.LandingAreaConfirmationInProgress ? "marker" : "idle"))
                : (takeoff ? (drawing ? "takeoff" : "takeoff-prep") : (drawing ? "landing" : "landing-prep"));
            if (stage != gshipLastStage)
            {
                Log.Message("[IsoRim probe] gship stage " + stage + " at frame " + gshipFrames + " (Unity " + Time.frameCount + "), tick "
                    + Find.TickManager.TicksGame + ", world shown " + WorldRendererUtility.WorldSelected + ", map " + Find.CurrentMap
                    + ", camera " + Find.CameraDriver.MapPosition + " size " + Find.CameraDriver.RootSize.ToString("0.0"));
                gshipLastStage = stage;
            }
            if (cut && !takeoff)
            {
                gshipLanding = true;
            }

            if (drawing && cutTag != null)
            {
                CutBurst(takeoff, progress, timeLeft);
            }

            // The landing marker waits: put it (as the move designator does) and confirm.
            if (gshipLandQueued && !cut && ctl.LandingAreaConfirmationInProgress && ctl.landingMap != null
                && Find.CurrentMap == ctl.landingMap && WorldRendererUtility.DrawingMap)
            {
                if (gshipMarkerSeen < 0)
                {
                    gshipMarkerSeen = gshipFrames;
                }
                else if (gshipFrames - gshipMarkerSeen >= 30)
                {
                    gshipLandQueued = false;
                    GshipLand(ctl);
                }
            }

            if (gshipLanding && !cut)
            {
                gshipActive = false;
                GshipReport("landed");
            }
            else if (gshipFrames > 6000)
            {
                gshipActive = false;
                Log.Error("[IsoRim probe] gship: no landing after " + gshipFrames + " frames (stage " + stage + ")");
            }
            gshipLastCut = cut;
            gshipLastTakeoff = takeoff;
        }

        private static void CutBurst(bool takeoff, float progress, float timeLeft)
        {
            bool shoot = cutEvery > 0 && cutFrame % cutEvery == 0;
            string name = null;
            int next = takeoff ? cutNextT : cutNextL;
            if (next < cutMarks.Count && progress >= cutMarks[next] - 1e-4f)
            {
                shoot = true;
                name = (takeoff ? "T_" : "L_") + cutMarks[next].ToString("0.00", CultureInfo.InvariantCulture);
                while (next < cutMarks.Count && progress >= cutMarks[next] - 1e-4f)
                {
                    next++;
                }
                if (takeoff)
                {
                    cutNextT = next;
                }
                else
                {
                    cutNextL = next;
                }
            }
            if (shoot)
            {
                if (name == null)
                {
                    name = (takeoff ? "T_f" : "L_f") + cutFrame.ToString("0000");
                }
                string dir = System.IO.Path.Combine(GenFilePaths.SaveDataFolderPath, "renders", HarnessTag());
                System.IO.Directory.CreateDirectory(dir);
                ScreenCapture.CaptureScreenshot(System.IO.Path.Combine(dir, "cut_" + cutTag + "_" + name + ".png"));
            }
            if (shoot || cutEvery > 0)
            {
                Log.Message("[IsoRim probe] cutburst " + cutTag + " " + (takeoff ? "takeoff" : "landing") + " frame " + cutFrame
                    + (shoot ? " SHOT " + name : "") + ": progress " + progress.ToString("0.000") + ", time left " + timeLeft.ToString("0.00")
                    + ", dt " + (Time.deltaTime * 1000f).ToString("0.0") + " ms, camera " + Find.CameraDriver.MapPosition
                    + " size " + Find.CameraDriver.RootSize.ToString("0.0") + ", yaw " + View3D.Yaw.ToString("0") + ", pitch " + View3D.Pitch.ToString("0.0")
                    + ", " + ShipFlight.Describe());
            }
            cutFrame++;
        }

        private static void GshipLand(WorldComponent_GravshipController ctl)
        {
            GravshipLandingMarker marker = ctlMarker(ctl);
            if (marker == null)
            {
                Log.Error("[IsoRim probe] gship land: no landing marker");
                return;
            }
            IntVec3 root = gshipLandHere ? gshipTakeoffEngine : gshipLandAt;
            marker.GravshipRotation = gshipLandRot;
            Designator_MoveGravship d = ctl.MoveDesignator();
            // The cell a click would pick so the marker's root (where the engine lands) is that cell
            // (Designator_MoveGravship.DesignateSingleCell / GetSizeRotAdjustedCell).
            IntVec2 size = marker.gravship.Bounds.Size;
            IntVec3 click = IntVec3.Invalid;
            int reach = Math.Max(size.x, size.z) + 2;
            for (int dx = -reach; dx <= reach && !click.IsValid; dx++)
            {
                for (int dz = -reach; dz <= reach && !click.IsValid; dz++)
                {
                    IntVec3 c = root + new IntVec3(dx, 0, dz);
                    if (PrefabUtility.GetRoot(SizeRotAdjusted(c, size, gshipLandRot), size, gshipLandRot) == root)
                    {
                        click = c;
                    }
                }
            }
            if (!click.IsValid)
            {
                Log.Error("[IsoRim probe] gship land: no click cell puts the engine at " + root);
                return;
            }
            AcceptanceReport ok = d.CanDesignateCell(click);
            Log.Message("[IsoRim probe] gship land: engine at " + root + " rot " + gshipLandRot + " (click " + click + "): " + (ok.Accepted ? "valid" : "INVALID " + ok.Reason));
            d.DesignateSingleCell(click);
            // The Confirm button (WorldComponent_GravshipController.WorldComponentOnGUI).
            if (marker.Spawned && Find.DesignatorManager.SelectedDesignator != d)
            {
                marker.BeginLanding(ctl);
                ctlMarker(ctl) = null;
                SoundDefOf.Gravship_Land.PlayOneShotOnCamera();
                Log.Message("[IsoRim probe] gship land: confirmed, cutscene " + WorldComponent_GravshipController.CutsceneInProgress);
            }
            else
            {
                Log.Error("[IsoRim probe] gship land: the marker was not placed");
            }
        }

        // Designator_MoveGravship.GetSizeRotAdjustedCell (private), vanilla 1.7.
        private static IntVec3 SizeRotAdjusted(IntVec3 cell, IntVec2 size, Rot4 rot)
        {
            IntVec3 hx = new IntVec3(size.x / 2, 0, 0);
            IntVec3 hz = new IntVec3(0, 0, size.z / 2);
            if (rot == Rot4.North)
            {
                return cell + hx + hz;
            }
            if (rot == Rot4.East)
            {
                return cell + hx - hz;
            }
            if (rot == Rot4.South)
            {
                return cell - hx - hz;
            }
            return cell - hx + hz;
        }

        private static void GshipReport(string what)
        {
            Map map = Find.CurrentMap;
            Building_GravEngine engine = map != null ? map.listerThings.ThingsOfDef(ThingDefOf.GravEngine).OfType<Building_GravEngine>().FirstOrDefault() : null;
            string pawns = map == null ? "-" : string.Join(" ", map.mapPawns.AllPawnsSpawned.Where(p => p.Faction == Faction.OfPlayer)
                .Select(p => p.LabelShort + "@" + p.Position + (engine != null && engine.ValidSubstructure.Contains(p.Position) ? "(aboard)" : "")));
            Log.Message("[IsoRim probe] gship " + what + " after " + gshipFrames + " frames: engine " + (engine != null ? engine.Position + ", substructure " + engine.ValidSubstructure.Count
                + ", fuel " + engine.TotalFuel.ToString("0") + ", cooldown until " + engine.cooldownCompleteTick : "none") + ", tick " + Find.TickManager.TicksGame
                + ", camera " + Find.CameraDriver.MapPosition + " size " + Find.CameraDriver.RootSize.ToString("0.0") + ", pawns: " + pawns);
        }
    }
}

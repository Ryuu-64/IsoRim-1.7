using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace IsoRim
{
    // Asked for each projectile card IsoRim draws itself (Projectiles.DrawAt: a projectile without a graphic class of its
    // own, not a lobbed shell). at and rotation are where and how IsoRim would draw the card (drawn space). Return true
    // and the full matrix to draw it with instead; false draws it as usual.
    public delegate bool ProjectileCardHook(Projectile p, Vector3 at, Quaternion rotation, out Matrix4x4 matrix);

    // The public surface for gameplay addons (stacked floors and the like). Addons should use only Api, IFloorLayout and
    // the delegate and enum types next to them: everything else in IsoRim is internal detail and may change between
    // versions.
    // Positions: "true space" is x and z in map cells, y the height in cells; "drawn space" is where IsoRim puts things
    // (drawn y = true height / HeightSquash).
    public static partial class Api
    {
        // Raised when members are added (2: view, projectile, skyfaller, drawing and option members of this file; 3: the
        // fog lid lock; 4: collapse debris, Debris.cs; 5: XrayDesignator).
        public static int Version => 5;

        // ---- the view

        // The 3D view is on and has drawn a frame (its camera maths below are valid).
        public static bool ViewReady => View3D.Ready;

        // The camera's tilt in degrees (90 looks straight down) and its turn about the vertical in degrees.
        public static float ViewPitch => View3D.Pitch;

        public static float ViewYaw => View3D.Yaw;

        // How the 3D view squashes heights: drawn y = true height / HeightSquash.
        public static float HeightSquash => View3D.Squash;

        // The height of a full wall in cells (the option, or the locked value).
        public static float WallHeight => View3D.WallHeight;

        // For an addon that needs the 3D view: it starts on and can't be turned off (by names the addon in the tooltip),
        // and the wall height is fixed at wallHeight cells (0 leaves the option to the player).
        public static void LockView(string by, float wallHeight)
        {
            View3D.LockedOn = true;
            View3D.LockedBy = by;
            View3D.LockedWallHeight = wallHeight;
        }

        // Who locked the view on (null when nobody did), and the locked wall height (0: the option decides).
        public static string ViewLockedBy => View3D.LockedOn ? View3D.LockedBy : null;

        public static float LockedWallHeight => View3D.LockedWallHeight;

        // For an addon whose current game needs the fog lid on or off (Version 3): the lid follows on, the player's own
        // option is kept unchanged (back after UnlockFogLid, and saved as it was), and the option is greyed out with tip
        // as its tooltip (who locked it, in the player's language). Not saved: lock again for each game that needs it.
        public static void LockFogLid(string tip, bool on)
        {
            View3D.FogLidLocked = true;
            View3D.LockedFogLid = on;
            View3D.FogLidLockTip = tip;
        }

        // The player's own fog lid option decides again.
        public static void UnlockFogLid()
        {
            View3D.FogLidLocked = false;
            View3D.FogLidLockTip = null;
        }

        // The fog lid lock's tooltip (null when not locked), and whether the fog lid is drawn now (the option, or the
        // locked value).
        public static string FogLidLockedBy => View3D.FogLidLocked ? View3D.FogLidLockTip ?? "" : null;

        public static bool FogLidOn => View3D.FogLid;

        // Where a true-space point shows on screen, pixels from the bottom left.
        public static Vector2 WorldToScreen(Vector3 p) => View3D.WorldToScreen(p);

        // The ground point under a screen pixel (y 0), and the point at a true height under it.
        public static Vector3 ScreenToGround(Vector2 px) => View3D.ScreenToGround(px);

        public static Vector3 ScreenToHeight(Vector2 px, float height) => View3D.ScreenToHeight(px, height);

        // The map position under a screen pixel, on the floor the pointer ray lands on (stacked floors), else the ground.
        public static Vector3 ScreenToMap(Vector2 px) => IsoRim.Floors.ScreenToMap(px);

        // Roofs shown or hidden (the player's roof switch; saved with IsoRim's settings).
        public static void ShowRoofs(bool on) => Roofs.Set(on);

        // The cut-away has lowered the wall in this cell (it is near the pointer).
        public static bool WallCutAway(Map map, IntVec3 c) => Cutaway.Hovered(map, c);

        // Sets one of IsoRim's options by the name it is saved under in IsoRim's settings file (for example "freeYaw",
        // "isoPitch", "wallHeight", "groundShadows", "lightingFix"); returns the old value, or null for an unknown name.
        // Not saved: the next save of the settings keeps it.
        public static object SetOption(string name, object value)
        {
            IsoRimSettings st = IsoRimMod.Settings;
            FieldInfo f = st == null ? null : AccessTools.Field(typeof(IsoRimSettings), name);
            if (f == null)
            {
                return null;
            }
            object old = f.GetValue(st);
            f.SetValue(st, value);
            return old;
        }

        // For an addon's own designators (Version 5): while one of this type (or a subclass) is in hand, the mining x-ray
        // (the Mine tool's faint view of the floors next to the viewed one; IsoRim's mineXray option) shows the floor
        // below and/or the floor above, without the crosshair. Both false removes it.
        public static void XrayDesignator(System.Type designator, bool below, bool above) => MineXray.Register(designator, below, above);

        // ---- projectiles (all only change how they are drawn)

        // How high a shooter fires from over its feet, in cells (where its bullets start).
        public static float MuzzleHeight(Thing launcher) => Projectiles.MuzzleHeight(launcher);

        // The true height (cells over the ground) IsoRim draws a projectile at when the game has it at point at of its
        // flat path.
        public static float ProjectileHeight(Projectile p, Vector3 at) => Projectiles.Height(p, at);

        // The height of what a projectile is aimed at, over its floor.
        public static float AimHeight(Projectile p) => Projectiles.AimHeight(p);

        // The muzzle of a gun: x and z on the map, y the height over the owner's feet.
        public static Vector3 Muzzle(Thing owner, ThingDef gun, Vector3 from, Vector3 dir) => Projectiles.Muzzle(owner, gun, from, dir);

        // For an addon that moves a shot's launch point elsewhere on the map (stacked floors: onto the target floor's
        // rows): where the shot really left from. Call before the projectile is first drawn.
        public static void SetLaunchPoint(Projectile p, Vector3 from) => Projectiles.SetLaunchPoint(p, from);

        // Where the drawn flight starts: x and z in the game's rows, y the height over the ground in cells.
        public static Vector3 DrawnStart(Projectile p) => Projectiles.DrawnStart(p);

        // Replaces the matrix of projectile cards (e.g. shots between floors pitched along their 3D flight). Null:
        // drawn as usual.
        public static ProjectileCardHook ProjectileCard;

        // ---- skyfallers

        // Where IsoRim draws a skyfaller now: its ground point (ground-band x, z), the height of its bottom over the
        // ground, its landing floor's height and the rows from that floor down to the ground band. False when the 3D view
        // is off or its floor is hidden.
        public static bool SkyfallerPose(Skyfaller s, out Vector3 ground, out float height, out float floorHeight, out float rowShift)
        {
            ground = Vector3.zero;
            height = floorHeight = rowShift = 0f;
            Skyfallers.Pose p;
            if (s == null || !View3D.Ready || !Skyfallers.PoseOf(s, s.DrawPos, out p))
            {
                return false;
            }
            ground = p.ground;
            height = p.height;
            floorHeight = p.floorHeight;
            rowShift = p.rowShift;
            return true;
        }

        // ---- drawing

        // A new see-through, vertex-coloured material: depth-tested without depth write, both sides, unlit, at this
        // render queue.
        public static Material MakeBlendMaterial(string name, int queue) => R3DMaterials.Make(R3DMaterials.BlendDef, name, queue);

        // Draws a vertex-coloured mesh lit as IsoRim lights its models (sun shading per face, night and lamp light per
        // cell). Call from draw-time code; does nothing before the material is ready.
        public static void DrawLit(Mesh mesh, Matrix4x4 matrix)
        {
            Material mat = R3DMaterials.LitModel;
            if (mesh != null && mat != null)
            {
                IsoGlow.DrawLit(mesh, matrix, mat, 0);
            }
        }

        // ---- for addons' dev tests only: may change without notice

        public static partial class Dev
        {
            // Stacked floors are drawn this frame.
            public static bool FloorsDrawn => IsoRim.Floors.On;

            // The 3D view's switch, without its usual side effects (caller regenerates the map meshes).
            public static bool View3DActive
            {
                get { return View3D.Active; }
                set { View3D.Active = value; }
            }

            // Upper floors' sun shadows fill the gap between their parts (A/B switch).
            public static bool ShadowGapFill
            {
                get { return IsoRim.Floors.ShadowGapFill; }
                set { IsoRim.Floors.ShadowGapFill = value; }
            }

            // The floor above the cell exists (the cell's slab).
            public static bool SlabOver(Map map, IntVec3 c) => IsoRim.Floors.SlabOver(map, c);

            private static readonly AccessTools.FieldRef<IsoGlowMap, Color32[]> corners = AccessTools.FieldRefAccess<IsoGlowMap, Color32[]>("corners");

            // The light IsoRim's glow texture has at the south-west corner of a cell; null when out of range.
            public static Color32? GlowCorner(Map map, IntVec3 c)
            {
                IsoGlowMap g = IsoGlowMap.For(map);
                Color32[] all = g == null ? null : corners(g);
                int i = c.z * (g == null ? 0 : g.width) + c.x;
                return all != null && i >= 0 && i < all.Length ? all[i] : (Color32?)null;
            }
        }
    }
}

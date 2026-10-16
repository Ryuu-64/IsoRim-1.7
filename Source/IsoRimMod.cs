using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace IsoRim
{
    public class IsoRimSettings : ModSettings
    {
        public float pitch = 55f;        // camera angle in degrees; 90 looks straight down
        public bool isometric = true;    // camera turned 45 degrees and lowered, as in isometric games
        public float isoPitch = 35.3f;   // camera angle while isometric; 35.3 is the true isometric angle
        public float wallHeight = 1.2f;    // height of a full wall, in cells
        public float squash = 0.003f;    // how far the game's flat draw layers are pressed together
        public float feetOffset = 0.3f;  // where a pawn's feet sit, as a fraction of sprite height below centre
        public float saturation = 0.85f; // custom look: how much of the colour is kept (1 = untouched)
        public float brightness = 0.95f; // custom look: how much of the brightness is kept (1 = untouched)
        public float treeShadowLength = 0.2f;   // shadow height per cell of tree, on the game's scale where a wall is 1
        public float cutHeight = 0.5f;   // how much is left of a wall that is cut down to show the room behind
        public float dissolveRadius = 3.5f; // radius around the mouse cursor within which walls are cut down

        public bool uprightPawns = true;
        public bool uprightPlants = true;
        public bool walls3D = true;
        public bool texturedWalls = true;
        public bool autoExtrude = true;
        public bool thickItems = true;
        public bool itemsFromArt = true;
        public bool rawModels = true;    // raw materials (steel, wood, blocks ...) keep the mod's 3D models even with itemsFromArt
        public bool ghosts3D = true;
        public bool cutRock = true;
        public bool mapEdge3D = true;
        public float itemThickness = 1f;
        public bool workAnimations = true;
        public bool damage3D = true;
        public bool outlines = true;
        public bool texturedModels = true;
        public bool ruggedRock = true;
        public bool trees3D = true;
        public bool smallPlants3D = true;
        public bool groundStyle = true;
        public bool grassSoil = true;
        public bool floorStyle = true;
        public bool waterStyle = true;   // the mod's own stylised water
        public bool cutHover = true;     // walls under the pointer are cut down
        public bool cutRooms = false;    // walls in front of every roofed room are cut down
        public int turn = 0;             // eighth turns of the camera, clockwise
        public bool snap45 = true;       // Q and E turn an eighth (45 degrees) at a time; otherwise a quarter
        public bool freeLook = true;     // the view can be turned and tilted by dragging with the mouse
        public float freeYaw = 0f;       // degrees turned by free look, on top of the eighth turns
        public float lookSensitivity = 0.25f;   // degrees per pixel dragged
        public bool invertY = false;     // dragging up tilts toward top-down instead of lower
        public bool perspective = false; // perspective camera (Rim3D-like) instead of orthographic
        public float fov = 40f;          // perspective: vertical field of view, degrees
        public float viewDistance = 160f; // perspective: how far past the screen centre the map is drawn, cells
        public float labelFade = 1.4f;   // perspective: labels fade out past this many times the distance to the screen centre (4 = never)
        public float fpFov = 70f;        // first person: vertical field of view, degrees
        public float fpEyeHeight = 0.95f; // first person: eye height, cells (a full wall is wallHeight)
        public bool sky = true;          // perspective: a sky behind the map instead of black
        public bool distanceFog = true;  // perspective: the far map fades into the sky's horizon
        public float fogStrength = 1f;   // 1 = solid at the view distance (hides the edge)
        public float fogStart = 0.35f;   // where between the screen centre and the view distance the fog starts
        public bool stars = true;        // sky at night: stars
        public bool milkyWay = true;     // sky at night: the Milky Way band
        public bool planets = true;      // sky at night: three planets (realistic points)
        public bool gasGiant = true;     // sky: a ringed gas giant (sci-fi), faint by day
        public float gasGiantSize = 1f;
        public float starBrightness = 1f;
        public bool moonPhases = true;   // the moon waxes and wanes over a quadrum
        public bool lowSky = true;       // sun, moon and gas giant drawn lower, where the map camera can see them
        public bool antiAlias = true;    // FXAA pass over the 3D view (the engine has no MSAA)
        public float antiAliasStrength = 1f;
        public int lookMode = 0;         // 0 = Ctrl (R3D_LookModifier) + middle button, 1 = right button with nothing selected
        public bool pawns3D = true;
        public bool effects3D = true;
        public bool groundShadows = true;
        public bool treeShadows = true;
        public bool grade = true;        // colour grade on
        public string lookName = "Gentle";
        public float lookStrength = 1f;
        public string lutFile = "";      // a LUT file picked by name instead of a look's own
        public bool animals3D = true;
        public bool mechs3D = true;
        public bool heldWeapons3D = true;
        public bool doors3D = true;
        public bool models3D = true;
        public bool lightingFix = true;
        public bool fogLid = true;
        public bool showRoofs = false;
        public int roofStyle = 0;        // slate, terracotta, thatch, tin
        public bool sunMoves = true;
        public bool cutFrontOnly = false;        // only walls between the camera and the pointer are lowered
        public bool cutNearColonists = false;    // walls round colonists are lowered too
        public float colonistRadius = 2.5f;
        public bool treesNearPointer = true;     // trees near the pointer drop their leaves so what is under them shows
        public bool treesNearColonists = false;
        public float treeRadius = 2.5f;
        public bool roofHover = true;            // the roof of the room under the pointer is lifted off
        public bool roofOverColonists = false;   // and of every room a colonist is in     // the sun crosses the sky through the day; otherwise it stays put

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref pitch, "pitch", 55f);
            Scribe_Values.Look(ref isometric, "isometric", true);
            Scribe_Values.Look(ref isoPitch, "isoPitch", 35.3f);
            Scribe_Values.Look(ref wallHeight, "wallHeight", 1.2f);
            Scribe_Values.Look(ref squash, "squash", 0.02f);
            Scribe_Values.Look(ref feetOffset, "feetOffset", 0.3f);
            Scribe_Values.Look(ref saturation, "saturation", 0.85f);
            Scribe_Values.Look(ref brightness, "brightness", 0.95f);
            Scribe_Values.Look(ref treeShadowLength, "treeShadowLength", 0.2f);
            Scribe_Values.Look(ref uprightPawns, "uprightPawns", true);
            Scribe_Values.Look(ref uprightPlants, "uprightPlants", true);
            Scribe_Values.Look(ref walls3D, "walls3D", true);
            Scribe_Values.Look(ref texturedWalls, "texturedWalls", true);
            Scribe_Values.Look(ref autoExtrude, "autoExtrude", true);
            Scribe_Values.Look(ref thickItems, "thickItems", true);
            Scribe_Values.Look(ref itemsFromArt, "itemsFromArt", true);
            Scribe_Values.Look(ref rawModels, "rawModels", true);
            Scribe_Values.Look(ref ghosts3D, "ghosts3D", true);
            Scribe_Values.Look(ref cutRock, "cutRock", true);
            Scribe_Values.Look(ref mapEdge3D, "mapEdge3D", true);
            Scribe_Values.Look(ref itemThickness, "itemThickness", 1f);
            Scribe_Values.Look(ref workAnimations, "workAnimations", true);
            Scribe_Values.Look(ref damage3D, "damage3D", true);
            Scribe_Values.Look(ref outlines, "outlines", true);
            Scribe_Values.Look(ref texturedModels, "texturedModels", true);
            Scribe_Values.Look(ref ruggedRock, "ruggedRock", true);
            Scribe_Values.Look(ref trees3D, "trees3D", true);
            Scribe_Values.Look(ref smallPlants3D, "smallPlants3D", true);
            Scribe_Values.Look(ref groundStyle, "groundStyle", true);
            Scribe_Values.Look(ref grassSoil, "grassSoil", true);
            Scribe_Values.Look(ref floorStyle, "floorStyle", true);
            Scribe_Values.Look(ref waterStyle, "waterStyle", true);
            Scribe_Values.Look(ref cutHover, "cutHover", true);
            Scribe_Values.Look(ref cutRooms, "cutRooms", false);
            Scribe_Values.Look(ref turn, "turnSteps", 0);
            Scribe_Values.Look(ref snap45, "snap45", true);
            Scribe_Values.Look(ref freeLook, "freeLook", true);
            Scribe_Values.Look(ref freeYaw, "freeYaw", 0f);
            Scribe_Values.Look(ref lookSensitivity, "lookSensitivity", 0.25f);
            Scribe_Values.Look(ref invertY, "invertY", false);
            Scribe_Values.Look(ref lookMode, "lookMode", 0);
            Scribe_Values.Look(ref perspective, "perspective", false);
            Scribe_Values.Look(ref fov, "fov", 40f);
            Scribe_Values.Look(ref viewDistance, "viewDistance", 160f);
            Scribe_Values.Look(ref labelFade, "labelFade", 1.4f);
            Scribe_Values.Look(ref fpFov, "fpFov", 70f);
            Scribe_Values.Look(ref sky, "sky", true);
            Scribe_Values.Look(ref distanceFog, "distanceFog", true);
            Scribe_Values.Look(ref fogStrength, "fogStrength", 1f);
            Scribe_Values.Look(ref fogStart, "fogStart", 0.35f);
            Scribe_Values.Look(ref stars, "stars", true);
            Scribe_Values.Look(ref milkyWay, "milkyWay", true);
            Scribe_Values.Look(ref planets, "planets", true);
            Scribe_Values.Look(ref gasGiant, "gasGiant", true);
            Scribe_Values.Look(ref gasGiantSize, "gasGiantSize", 1f);
            Scribe_Values.Look(ref starBrightness, "starBrightness", 1f);
            Scribe_Values.Look(ref moonPhases, "moonPhases", true);
            Scribe_Values.Look(ref lowSky, "lowSky", true);
            Scribe_Values.Look(ref antiAlias, "antiAlias", true);
            Scribe_Values.Look(ref antiAliasStrength, "antiAliasStrength", 1f);
            Scribe_Values.Look(ref fpEyeHeight, "fpEyeHeight", 0.95f);
            Scribe_Values.Look(ref pawns3D, "pawns3D", true);
            Scribe_Values.Look(ref effects3D, "effects3D", true);
            Scribe_Values.Look(ref cutHeight, "cutHeight", 0.5f);
            Scribe_Values.Look(ref dissolveRadius, "dissolveRadius", 3.5f);
            Scribe_Values.Look(ref groundShadows, "groundShadows", true);
            Scribe_Values.Look(ref treeShadows, "treeShadows", true);
            Scribe_Values.Look(ref grade, "grade", true);
            Scribe_Values.Look(ref lookName, "lookName", "Gentle");
            Scribe_Values.Look(ref lookStrength, "lookStrength", 1f);
            Scribe_Values.Look(ref lutFile, "lutFile", "");
            Scribe_Values.Look(ref animals3D, "animals3D", true);
            Scribe_Values.Look(ref mechs3D, "mechs3D", true);
            Scribe_Values.Look(ref heldWeapons3D, "heldWeapons3D", true);
            Scribe_Values.Look(ref doors3D, "doors3D", true);
            Scribe_Values.Look(ref models3D, "models3D", true);
            Scribe_Values.Look(ref lightingFix, "lightingFix", true);
            Scribe_Values.Look(ref fogLid, "fogLid", true);
            Scribe_Values.Look(ref showRoofs, "showRoofs", false);
            Scribe_Values.Look(ref roofStyle, "roofStyle", 0);
            Scribe_Values.Look(ref sunMoves, "sunMoves", true);
            Scribe_Values.Look(ref cutFrontOnly, "cutFrontOnly", false);
            Scribe_Values.Look(ref cutNearColonists, "cutNearColonists", false);
            Scribe_Values.Look(ref colonistRadius, "colonistRadius", 2.5f);
            Scribe_Values.Look(ref treesNearPointer, "treesNearPointer", true);
            Scribe_Values.Look(ref treesNearColonists, "treesNearColonists", false);
            Scribe_Values.Look(ref treeRadius, "treeRadius", 2.5f);
            Scribe_Values.Look(ref roofHover, "roofHover", true);
            Scribe_Values.Look(ref roofOverColonists, "roofOverColonists", false);
            if (lookName == null)
            {
                lookName = "Gentle";
            }
            if (lutFile == null)
            {
                lutFile = "";
            }
        }

        // Puts every option back to its default.
        public void Reset()
        {
            IsoRimSettings fresh = new IsoRimSettings();
            foreach (FieldInfo f in typeof(IsoRimSettings).GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                f.SetValue(this, f.GetValue(fresh));
            }
        }
    }

    public class IsoRimMod : Mod
    {
        public static IsoRimSettings Settings;
        public static string RootDir;   // this mod's folder
        private static IsoRimMod instance;

        public IsoRimMod(ModContentPack content) : base(content)
        {
            instance = this;
            Settings = GetSettings<IsoRimSettings>();
            RootDir = content.RootDir;
        }

        // Writes the options to disk now (they are also written when the options window closes).
        public static void Save()
        {
            if (instance != null)
            {
                instance.WriteSettings();
            }
        }

        public override string SettingsCategory()
        {
            return "IsoRim";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            SettingsWindow.Draw(inRect, Settings);
        }
    }

    // The options screen: a header with the main switch, then four tabs, each in two columns of
    // titled sections.
    public static class SettingsWindow
    {
        private static int tab;
        private static int scrollTab = -1;
        private static Vector2 scroll;
        private static float contentHeight;
        private static readonly Color Muted = new Color(0.72f, 0.72f, 0.72f);
        private static readonly Color Rule = new Color(1f, 1f, 1f, 0.18f);
        private static readonly Color Accent = new Color(0.55f, 0.78f, 0.95f);
        private static readonly Color[] Palette =
        {
            new Color(0.42f, 0.55f, 0.30f), new Color(0.55f, 0.42f, 0.30f), new Color(0.58f, 0.57f, 0.55f), new Color(0.30f, 0.50f, 0.60f),
            new Color(0.88f, 0.70f, 0.58f), new Color(0.86f, 0.88f, 0.92f), new Color(0.72f, 0.22f, 0.20f), new Color(0.18f, 0.17f, 0.16f),
        };

        public static void Draw(Rect inRect, IsoRimSettings st)
        {
            Text.Font = GameFont.Small;
            Rect header = new Rect(inRect.x, inRect.y, inRect.width, 52f);
            Header(header, st);

            Rect body = new Rect(inRect.x, header.yMax + 36f, inRect.width, inRect.height - header.height - 36f);
            List<TabRecord> tabs = new List<TabRecord>
            {
                new TabRecord("View and camera", delegate { tab = 0; }, tab == 0),
                new TabRecord("See-through", delegate { tab = 4; }, tab == 4),
                new TabRecord("Colour and light", delegate { tab = 1; }, tab == 1),
                new TabRecord("Ground, water, roofs", delegate { tab = 2; }, tab == 2),
                new TabRecord("What is 3D", delegate { tab = 3; }, tab == 3),
            };
            Widgets.DrawMenuSection(body);
            TabDrawer.DrawTabs(body, tabs);

            // Both columns scroll together when a tab is taller than the window (the height is
            // measured from the last frame).
            Rect inner = body.ContractedBy(14f);
            if (scrollTab != tab)
            {
                scrollTab = tab;
                scroll = Vector2.zero;
            }
            bool scrolls = contentHeight > inner.height;
            Rect view = new Rect(0f, 0f, inner.width - (scrolls ? 18f : 0f), Mathf.Max(contentHeight, inner.height));
            Widgets.BeginScrollView(inner, ref scroll, view);
            float gap = 24f;
            float colW = (view.width - gap) / 2f;
            Rect left = new Rect(0f, 0f, colW, 4000f);
            Rect right = new Rect(colW + gap, 0f, colW, 4000f);
            GUI.color = Rule;
            Widgets.DrawLineVertical(colW + gap / 2f, 0f, view.height);
            GUI.color = Color.white;

            // One column at a time: a listing opens a drawing group, and two open at once would
            // nest, putting everything in the second outside the first and drawing nothing.
            Listing_Standard l = new Listing_Standard();
            l.Begin(left);
            Tab(l, null, st);
            float leftH = l.CurHeight;
            l.End();
            Listing_Standard r = new Listing_Standard();
            r.Begin(right);
            Tab(null, r, st);
            float rightH = r.CurHeight;
            r.End();
            Widgets.EndScrollView();
            contentHeight = Mathf.Max(leftH, rightH) + 8f;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = Color.white;
        }

        // ---------------------------------------------------------------- header

        private static void Header(Rect rect, IsoRimSettings st)
        {
            Widgets.DrawBoxSolid(rect, new Color(0.10f, 0.12f, 0.14f, 0.65f));
            GUI.color = Accent;
            Widgets.DrawBoxSolid(new Rect(rect.x, rect.y, 4f, rect.height), Accent);
            GUI.color = Color.white;

            Text.Anchor = TextAnchor.MiddleLeft;
            Text.Font = GameFont.Small;
            GUI.color = Muted;
            string status = (View3D.Active ? "3D view is on" : "3D view is off")
                + "   ·   camera turns " + (st.snap45 ? "45°" : "90°") + " a step";
            Widgets.Label(new Rect(rect.x + 16f, rect.y, rect.width - 300f, rect.height), status);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            Text.Anchor = TextAnchor.UpperLeft;

            Rect toggle = new Rect(rect.xMax - 270f, rect.y + 11f, 150f, 30f);
            if (View3D.LockedOn)
            {
                Widgets.ButtonText(toggle, "Turn 3D view off", true, false, false);
                TooltipHandler.TipRegion(toggle, "Always on with " + View3D.LockedBy);
            }
            else if (Widgets.ButtonText(toggle, View3D.Active ? "Turn 3D view off" : "Turn 3D view on"))
            {
                View3D.Active = !View3D.Active;
            }
            Rect reset = new Rect(rect.xMax - 110f, rect.y + 11f, 100f, 30f);
            if (Widgets.ButtonText(reset, "Reset all"))
            {
                Find.WindowStack.Add(Dialog_MessageBox.CreateConfirmation("Put every IsoRim option back to its default?", delegate
                {
                    bool roofs = st.showRoofs;
                    st.Reset();
                    if (roofs != st.showRoofs)
                    {
                        st.showRoofs = roofs;
                        Roofs.Set(!roofs);
                    }
                }));
            }
        }

        // ---------------------------------------------------------------- building blocks

        private static void Section(Listing_Standard l, string title, string note = null)
        {
            if (l.CurHeight > 1f)
            {
                l.Gap(10f);
            }
            Text.Font = GameFont.Medium;
            Rect r = l.GetRect(30f);
            Widgets.Label(r, title);
            Text.Font = GameFont.Small;
            GUI.color = Rule;
            Widgets.DrawLineHorizontal(r.x, r.yMax - 1f, r.width);
            GUI.color = Color.white;
            if (note != null)
            {
                Text.Font = GameFont.Tiny;
                GUI.color = Muted;
                l.Label(note);
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
            }
            l.Gap(4f);
        }

        private static void Note(Listing_Standard l, string text)
        {
            Text.Font = GameFont.Tiny;
            GUI.color = Muted;
            l.Label(text);
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
        }

        private static float Slider(Listing_Standard l, string label, float value, float min, float max, string shown, string tip = null)
        {
            Rect r = l.GetRect(22f);
            Widgets.Label(r.LeftPart(0.72f), label);
            Text.Anchor = TextAnchor.MiddleRight;
            GUI.color = Accent;
            Widgets.Label(r.RightPart(0.28f), shown);
            GUI.color = Color.white;
            Text.Anchor = TextAnchor.UpperLeft;
            if (tip != null)
            {
                TooltipHandler.TipRegion(r, tip);
            }
            return l.Slider(value, min, max);
        }

        private static void Check(Listing_Standard l, string label, ref bool value, string tip = null)
        {
            l.CheckboxLabeled(label, ref value, tip);
        }

        private static string KeyOf(KeyBindingDef key)
        {
            if (key == null || key.MainKey == KeyCode.None)
            {
                return "-";
            }
            return key.MainKeyLabel;
        }

        private static void KeyRow(Listing_Standard l, KeyBindingDef key, string what)
        {
            Rect r = l.GetRect(22f);
            GUI.color = Accent;
            Widgets.Label(r.LeftPart(0.22f), KeyOf(key));
            GUI.color = Color.white;
            Widgets.Label(r.RightPart(0.78f), what);
        }

        // ---------------------------------------------------------------- tabs

        // Draws the chosen tab's left column (l set) or right column (r set).
        private static void Tab(Listing_Standard l, Listing_Standard r, IsoRimSettings st)
        {
            switch (tab)
            {
                case 0:
                    ViewTab(l, r, st);
                    break;
                case 1:
                    ColourTab(l, r, st);
                    break;
                case 2:
                    WorldTab(l, r, st);
                    break;
                case 4:
                    SeeThroughTab(l, r, st);
                    break;
                default:
                    ObjectsTab(l, r, st);
                    break;
            }
        }

        private static void ViewTab(Listing_Standard l, Listing_Standard r, IsoRimSettings st)
        {
            if (l != null)
            {
                Section(l, "Camera", "How the map is seen while the 3D view is on.");
                Check(l, "Isometric view", ref st.isometric, "The camera turned 45 degrees and lowered, as in isometric games. Off: north is up.");
                Check(l, "Turn in 45° steps", ref st.snap45, "Q and E turn the camera an eighth of a turn at a time. Off: a quarter turn. Either way the camera swings smoothly.");
                Check(l, "Free look", ref st.freeLook, "Drag with the mouse to turn the camera all the way round and tilt it (20° to 89°). Q and E snap to the next step. The reset key (unbound at first) is in the game's keyboard options.");
                if (st.freeLook)
                {
                    bool rightButton = st.lookMode == 1;
                    Check(l, "Free look with the right button", ref rightButton, "Drag with the right mouse button while nothing is selected. Off: hold " + (R3DKeyDefOf.R3D_LookModifier != null ? R3DKeyDefOf.R3D_LookModifier.MainKeyLabel : "Ctrl") + " and drag with the middle button.");
                    st.lookMode = rightButton ? 1 : 0;
                    st.lookSensitivity = Slider(l, "Free look speed", st.lookSensitivity, 0.05f, 1f, st.lookSensitivity.ToString("0.00") + "°/px");
                    Check(l, "Invert up and down", ref st.invertY, "Dragging up tilts the camera toward top-down.");
                }
                st.fpFov = Slider(l, "First person field of view", st.fpFov, 40f, 100f, st.fpFov.ToString("0") + "°",
                    (R3DKeyDefOf.R3D_FirstPerson != null ? R3DKeyDefOf.R3D_FirstPerson.MainKeyLabel : "F5") + " with one person selected: the camera in their head. Middle button drag looks round.");
                st.fpEyeHeight = Slider(l, "First person eye height", st.fpEyeHeight, 0.5f, 2f, st.fpEyeHeight.ToString("0.00") + " cells");
                Check(l, "Smooth edges (anti-aliasing)", ref st.antiAlias, "Softens the stair steps on the edges of walls and models (FXAA). The interface is not touched.");
                if (st.antiAlias)
                {
                    st.antiAliasStrength = Slider(l, "Edge smoothing", st.antiAliasStrength, 0.2f, 1f, (st.antiAliasStrength * 100f).ToString("0") + "%");
                }
                Check(l, "Perspective", ref st.perspective, "A real perspective camera: far things get smaller, as in Rim3D. Off: the flat (orthographic) view.");
                if (st.perspective)
                {
                    st.fov = Slider(l, "Field of view", st.fov, 15f, 90f, st.fov.ToString("0") + "°", "Wider shows more round the edges and makes depth stronger.");
                    st.viewDistance = Slider(l, "View distance", st.viewDistance, 40f, 400f, st.viewDistance.ToString("0") + " cells",
                        "How far past the middle of the screen the map is drawn when the camera is low. Further costs frame time.");
                    Check(l, "Sky", ref st.sky, "A sky with the sun and moon behind the map, following the time of day and the weather.");
                    Check(l, "Distance fog", ref st.distanceFog, "The far map fades into the horizon, so the edge of what is drawn never shows.");
                    if (st.distanceFog)
                    {
                        st.fogStart = Slider(l, "Fog starts", st.fogStart, 0f, 0.95f, (st.fogStart * 100f).ToString("0") + "%", "Where between the middle of the screen and the view distance the fog begins.");
                        st.fogStrength = Slider(l, "Fog strength", st.fogStrength, 0.2f, 1f, (st.fogStrength * 100f).ToString("0") + "%", "100% is solid at the view distance and hides the edge.");
                    }
                    st.labelFade = Slider(l, "Far labels fade from", st.labelFade, 1f, 4f, st.labelFade >= 4f ? "never" : "×" + st.labelFade.ToString("0.0"),
                        "Labels (stack counts, quality, names) fade out past this many times the distance to the middle of the screen. Your colonists keep their names.");
                }
                if (st.isometric)
                {
                    st.isoPitch = Slider(l, "Isometric camera angle", st.isoPitch, View3D.MinPitch, View3D.MaxPitch, st.isoPitch.ToString("0") + "°",
                        "35 is the true isometric angle.");
                }
                else
                {
                    st.pitch = Slider(l, "Camera angle", st.pitch, View3D.MinPitch, View3D.MaxPitch, st.pitch.ToString("0") + "°", "90 looks straight down.");
                }
                if (View3D.LockedWallHeight > 0f)
                {
                    // Fixed by an addon (ProjectZ): greyed out, with who fixed it.
                    GUI.color = Muted;
                    Slider(l, "Wall height", View3D.LockedWallHeight, 0.5f, 4f, View3D.LockedWallHeight.ToString("0.00") + " cells");
                    GUI.color = Color.white;
                    Note(l, "Fixed by " + View3D.LockedBy + ".");
                }
                else
                {
                    st.wallHeight = Slider(l, "Wall height", st.wallHeight, 0.5f, 4f, st.wallHeight.ToString("0.0") + " cells");
                }

            }
            if (r != null)
            {
                Section(r, "Keys", "Change them under Options > Keyboard configuration > IsoRim.");
                KeyRow(r, R3DKeyDefOf.R3D_ToggleView, "3D view on and off");
                KeyRow(r, R3DKeyDefOf.R3D_Isometric, "isometric or straight-on");
                KeyRow(r, R3DKeyDefOf.R3D_TurnLeft, "turn the camera left");
                KeyRow(r, R3DKeyDefOf.R3D_TurnRight, "turn the camera right");
                KeyRow(r, R3DKeyDefOf.R3D_TiltDown, "lower the camera");
                KeyRow(r, R3DKeyDefOf.R3D_TiltUp, "raise the camera");
                KeyRow(r, R3DKeyDefOf.R3D_ToggleRoofs, "roofs on and off");
                Note(r, "The 3D view and the roofs also have buttons in the bottom right corner of the screen, next to the game's own.");

                if (st.perspective && st.sky)
                {
                    Section(r, "Night sky", "The sky turns with the hour round a pole set by the map's latitude.");
                    Check(r, "Stars", ref st.stars, "Thousands of stars that twinkle, more of them near the horizon's haze. Hidden by rain, snow and fog.");
                    Check(r, "Milky Way", ref st.milkyWay, "A band of the galaxy across the sky, with dark dust lanes and a bright core.");
                    Check(r, "Planets (realistic)", ref st.planets, "Three steady points of light that wander along the sky from day to day.");
                    Check(r, "Gas giant with rings (sci-fi)", ref st.gasGiant, "A big ringed planet high in the sky, lit by the sun so it shows phases. Faint by day.");
                    if (st.gasGiant)
                    {
                        st.gasGiantSize = Slider(r, "Gas giant size", st.gasGiantSize, 0.4f, 2.5f, "×" + st.gasGiantSize.ToString("0.0"));
                    }
                    Check(r, "Sun, moon and gas giant low in the sky", ref st.lowSky, "Drawn about a third as high, so the map camera (which cannot look up) sees them. The light and shadows do not change. Off: where they really are, seen in first person.");
                    Check(r, "Moon phases", ref st.moonPhases, "The moon goes from new to full and back over a quadrum (15 days). Off: always full.");
                    st.starBrightness = Slider(r, "Night sky brightness", st.starBrightness, 0.2f, 2f, (st.starBrightness * 100f).ToString("0") + "%", "Stars, Milky Way and planets.");
                }

                Section(r, "Advanced");
                st.feetOffset = Slider(r, "Where upright sprites stand", st.feetOffset, 0f, 0.5f, st.feetOffset.ToString("0.00"),
                    "Only for people and animals still drawn as flat sprites.");
                st.squash = Slider(r, "Layer flattening", st.squash, 0.002f, 0.004f, st.squash.ToString("0.0000"),
                    "How tightly the game's flat drawing layers are pressed together. Leave it unless things flicker.");
            }
        }

        private static void SeeThroughTab(Listing_Standard l, Listing_Standard r, IsoRimSettings st)
        {
            if (l != null)
            {
                Section(l, "Walls", "Walls are lowered so you can see and click what is behind them. Natural rock never is.");
                Check(l, "Lower walls near the pointer", ref st.cutHover);
                if (st.cutHover)
                {
                    st.dissolveRadius = Slider(l, "Reach round the pointer", st.dissolveRadius, 1.5f, 8f, st.dissolveRadius.ToString("0.0") + " cells");
                    Check(l, "Only walls in front of the pointer", ref st.cutFrontOnly, "Only the walls between the camera and the pointer are lowered; the ones behind it stay up.");
                }
                Check(l, "Lower walls near colonists", ref st.cutNearColonists, "Walls between the camera and each colonist are lowered wherever they go. Costs a little more as they move about.");
                if (st.cutNearColonists)
                {
                    st.colonistRadius = Slider(l, "Reach round each colonist", st.colonistRadius, 1f, 5f, st.colonistRadius.ToString("0.0") + " cells");
                }
                Check(l, "Lower walls in front of every room", ref st.cutRooms, "Walls between the camera and a roofed room are always drawn lowered.");
                Check(l, "  ...and rock in front of rooms dug into mountains", ref st.cutRock, "Up to three cells of rock between the camera and a room are lowered; the rest of the mountain stays whole.");
                st.cutHeight = Slider(l, "Height left of a lowered wall", st.cutHeight, 0.05f, 0.9f, (st.cutHeight * 100f).ToString("0") + "%");
            }
            if (r != null)
            {
                Section(r, "Trees", "Trees drop their leaves while something you want to see is under them.");
                Check(r, "Thin out trees near the pointer", ref st.treesNearPointer);
                Check(r, "Thin out trees near colonists", ref st.treesNearColonists);
                if (st.treesNearPointer || st.treesNearColonists)
                {
                    st.treeRadius = Slider(r, "Reach", st.treeRadius, 1f, 6f, st.treeRadius.ToString("0.0") + " cells");
                }

                Section(r, "Roofs", "Only while roofs are shown.");
                Check(r, "Lift the roof of the room under the pointer", ref st.roofHover);
                Check(r, "Lift the roofs of rooms with colonists in them", ref st.roofOverColonists);
            }
        }

        private static void ColourTab(Listing_Standard l, Listing_Standard r, IsoRimSettings st)
        {
            if (l != null)
            {
                Section(l, "Colour look", "A colour grade over the 3D view. The interface is not affected.");
                Check(l, "Colour grade on", ref st.grade);
                if (st.grade)
                {
                    foreach (LookPreset p in Looks.Presets)
                    {
                        if (!st.lutFile.NullOrEmpty() && p.lutFile == st.lutFile)
                        {
                            // a file picked by name before a look used it: show it as that look (same picture)
                            st.lookName = p.name;
                            st.lutFile = "";
                        }
                    }
                    LookPreset look = Looks.Find(st.lookName);
                    Grade.Table table = st.lutFile.NullOrEmpty() ? null : Grade.TableFor(st.lutFile);
                    Grade.Table own = table == null && !look.lutFile.NullOrEmpty() ? Grade.TableFor(look.lutFile) : null;
                    Rect b = l.GetRect(30f);
                    if (Widgets.ButtonText(b, "Look: " + (table != null ? st.lutFile + " (LUT)" : look.label)))
                    {
                        List<FloatMenuOption> options = new List<FloatMenuOption>();
                        foreach (LookPreset p in Looks.Presets)
                        {
                            LookPreset chosen = p;
                            options.Add(new FloatMenuOption(p.label, delegate
                            {
                                st.lookName = chosen.name;
                                st.lutFile = "";
                            }));
                        }
                        Grade.Rescan();
                        foreach (string f in Grade.Files)
                        {
                            if (Grade.UsedByLook(f))
                            {
                                continue;
                            }
                            string chosen = f;
                            options.Add(new FloatMenuOption(f + " (LUT)", delegate { st.lutFile = chosen; }));
                        }
                        Find.WindowStack.Add(new FloatMenu(options));
                    }
                    if (table != null)
                    {
                        Note(l, "A lookup table from the mod's LUTs folder. Your own 1024 x 32 strips can go there too.");
                        Preview(l, c => Grade.ApplyFile(table, st.lookStrength, c));
                        st.lookStrength = Slider(l, "Strength", st.lookStrength, 0f, 1f, (st.lookStrength * 100f).ToString("0") + "%");
                    }
                    else
                    {
                        Note(l, look.description);
                        LookPreset current = Looks.Current();
                        if (own != null)
                        {
                            Preview(l, c => Grade.ApplyFile(own, st.lookStrength, c));
                        }
                        else
                        {
                            Preview(l, c => Looks.Apply(current, c));
                        }
                        if (look.name == Looks.CustomName)
                        {
                            st.saturation = Slider(l, "Colour kept", st.saturation, 0.5f, 1f, (st.saturation * 100f).ToString("0") + "%");
                            st.brightness = Slider(l, "Brightness", st.brightness, 0.6f, 1f, (st.brightness * 100f).ToString("0") + "%");
                        }
                        else
                        {
                            st.lookStrength = Slider(l, "Strength", st.lookStrength, 0f, 1f, (st.lookStrength * 100f).ToString("0") + "%");
                        }
                    }
                }
            }
            if (r != null)
            {
                Section(r, "Shadows");
                Check(r, "Soft shadows under everything", ref st.groundShadows);
                Check(r, "Trees cast sun shadows", ref st.treeShadows);
                if (st.treeShadows)
                {
                    st.treeShadowLength = Slider(r, "Tree shadow length", st.treeShadowLength, 0.05f, 0.5f, st.treeShadowLength.ToString("0.00"));
                }

                Section(r, "Light");
                Check(r, "Night and indoor darkness on raised things", ref st.lightingFix);
                Check(r, "Unexplored areas are a solid lid", ref st.fogLid);
                Check(r, "Sun moves through the day", ref st.sunMoves, "The sun rises in the east, stands in the south at noon and sets in the west, and its light on walls, roofs and furniture moves on a few times a day. Off: it stays at its mid-morning place. Turning the camera never changes the light.");
            }
        }

        private static void WorldTab(Listing_Standard l, Listing_Standard r, IsoRimSettings st)
        {
            if (l != null)
            {
                Section(l, "Ground", "Only the picture changes, and only while the 3D view is on.");
                Check(l, "Natural ground in the mod's style", ref st.groundStyle);
                if (st.groundStyle)
                {
                    Check(l, "Soil drawn as soft grass", ref st.grassSoil, "Rich soil gets a darker, lusher grass so farmland still stands out.");
                }
                Check(l, "Built floors in the mod's style", ref st.floorStyle);

                Section(l, "Water");
                Check(l, "Stylised water", ref st.waterStyle, "Faceted bands of colour from the shore to the deep, foam along the shores, streaks on rivers and sparkles.");
            }
            if (r != null)
            {
                Section(r, "Roofs", "Built roofs as slate lids over your rooms, and rock roofs over caves.");
                bool roofs = st.showRoofs;
                Check(r, "Show roofs", ref roofs, "All roofs or none: switch them off again to see inside.");
                if (roofs != st.showRoofs)
                {
                    Roofs.Set(roofs);
                }
                Rect rb = r.GetRect(28f);
                if (Widgets.ButtonText(rb, "Roof colour: " + SectionLayer_IsoRimRoofs.StyleNames[Mathf.Clamp(st.roofStyle, 0, 3)]))
                {
                    List<FloatMenuOption> options = new List<FloatMenuOption>();
                    for (int i = 0; i < SectionLayer_IsoRimRoofs.StyleNames.Length; i++)
                    {
                        int chosen = i;
                        options.Add(new FloatMenuOption(SectionLayer_IsoRimRoofs.StyleNames[i], delegate
                        {
                            st.roofStyle = chosen;
                            Roofs.Rebuild();
                        }));
                    }
                    Find.WindowStack.Add(new FloatMenu(options));
                }
                Note(r, "Built roofs rise from every edge to a ridge, like hipped roofs. There is a button for them in the bottom right corner of the screen, and a key you can set.");
            }
        }

        private static void ObjectsTab(Listing_Standard l, Listing_Standard r, IsoRimSettings st)
        {
            if (l != null)
            {
                Section(l, "Buildings and nature", "Switch one off if it misbehaves.");
                Check(l, "Walls and rock as solid blocks", ref st.walls3D);
                Check(l, "Walls use the mod's textures", ref st.texturedWalls);
                Check(l, "Rock has broken, sloping edges", ref st.ruggedRock);
                Check(l, "Doors", ref st.doors3D);
                Check(l, "Hand-made models", ref st.models3D, "Furniture, machines, wrecks, people, animals and armour.");
                Check(l, "Trees", ref st.trees3D);
                Check(l, "Grass, flowers and crops", ref st.smallPlants3D, "Thousands of these on a big map cost memory; switch off if it drags.");
                Check(l, "Everything else, from its sprite", ref st.autoExtrude);
                Check(l, "Items get a little thickness", ref st.thickItems);
                if (st.thickItems)
                {
                    st.itemThickness = Slider(l, "  How thick items are", st.itemThickness, 0f, 1f,
                        st.itemThickness <= 0.02f ? "flat" : (st.itemThickness * 100f).ToString("0") + "%",
                        "Thins out items, weapons and clothes on the ground. All the way down lays them flat.");
                }
                Check(l, "Items and weapons use the game's art", ref st.itemsFromArt,
                    "Items, weapons and clothing are made solid from their own game sprites, so they look exactly like RimWorld. Off: the mod's hand-made item models.");
                Check(l, "3D raw materials", ref st.rawModels, "Steel, wood, blocks, cloth, leather and other resources as 3D models, by stack size.");
                Check(l, "Other plants stand upright", ref st.uprightPlants);
                Check(l, "Map edge cut through the earth", ref st.mapEdge3D, "The edge of the map shows layers of soil and rock, like a diorama.");
            }
            if (r != null)
            {
                Section(r, "Creatures and effects");
                Check(r, "Colonists and other people", ref st.pawns3D);
                Check(r, "Animals", ref st.animals3D);
                Check(r, "Mechanoids", ref st.mechs3D);
                Check(r, "Weapons in hand", ref st.heldWeapons3D);
                Check(r, "Work animations with tools", ref st.workAnimations, "Axes, pickaxes, hammers, scythes, hoes and brooms while colonists work.");
                Check(r, "Blueprints show the finished build", ref st.ghosts3D, "Blueprints, frames and the placement preview as see-through 3D models.");
                Check(r, "Damage marks on buildings", ref st.damage3D);
                Check(r, "Flames, smoke, steam and sparkles", ref st.effects3D);
                Check(r, "Sprites stand upright", ref st.uprightPawns, "For people and animals without a model.");

                Section(r, "Model style");
                Check(r, "Textures on models", ref st.texturedModels);
                Check(r, "Dark outlines on models", ref st.outlines);
            }
        }

        // Two rows of colour chips: as they are, and under the look.
        private static void Preview(Listing_Standard l, Func<Color, Color> grade)
        {
            Rect r = l.GetRect(34f);
            float w = r.width / Palette.Length;
            for (int i = 0; i < Palette.Length; i++)
            {
                Rect top = new Rect(r.x + i * w, r.y, w - 2f, 15f);
                Rect bottom = new Rect(r.x + i * w, r.y + 17f, w - 2f, 15f);
                Widgets.DrawBoxSolid(top, Palette[i]);
                Widgets.DrawBoxSolid(bottom, grade(Palette[i]));
            }
            TooltipHandler.TipRegion(r, "Top: colours as they are. Bottom: under this look.");
            l.Gap(2f);
        }
    }

    [DefOf]
    public static class R3DKeyDefOf
    {
        public static KeyBindingDef R3D_ToggleView;
        public static KeyBindingDef R3D_Isometric;
        public static KeyBindingDef R3D_TurnLeft;
        public static KeyBindingDef R3D_TurnRight;
        public static KeyBindingDef R3D_TiltUp;
        public static KeyBindingDef R3D_TiltDown;
        public static KeyBindingDef R3D_ToggleRoofs;
        public static KeyBindingDef R3D_ResetCamera;
        public static KeyBindingDef R3D_LookModifier;
        public static KeyBindingDef R3D_FirstPerson;

        static R3DKeyDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(R3DKeyDefOf));
        }
    }

    // The mod's buttons for the bottom right corner. Loaded on the main thread at startup.
    [StaticConstructorOnStartup]
    public static class R3DIcons
    {
        public static readonly Texture2D View3D = ContentFinder<Texture2D>.Get("IsoRim/UI/View3D", false) ?? BaseContent.BadTex;
        public static readonly Texture2D Roofs = ContentFinder<Texture2D>.Get("IsoRim/UI/Roofs", false) ?? BaseContent.BadTex;
    }

    // Runs once on the main thread after all defs are loaded.
    [StaticConstructorOnStartup]
    public static class IsoRimStartup
    {
        static IsoRimStartup()
        {
            ModLog.Note("[IsoRim] Code loaded.");

            // 1.7 port testing: "-isorim3d" on the command line starts with the 3D view on
            // (used by the screenshot harness). The normal default (off) is unchanged.
            // A probe file whose first line is "#startoff" keeps it off (to film the first switch-on).
            if (GenCommandLine.CommandLineArgPassed("isorim3d") && !Probe.StartOff)
            {
                View3D.Active = true;
                Log.Message("[IsoRim] -isorim3d: 3D view starts on.");
            }
            // A gameplay addon that needs the 3D view locks it on (set in the addon's mod constructor, before this runs).
            if (View3D.LockedOn)
            {
                View3D.Active = true;
                ModLog.Note("[IsoRim] 3D view locked on by " + View3D.LockedBy + ".");
            }

            try
            {
                Patches.ApplyAll();
            }
            catch (Exception e)
            {
                Log.Error("[IsoRim] Could not apply patches: " + e);
            }

            try
            {
                ModelLibrary.LoadAll();
            }
            catch (Exception e)
            {
                Log.Error("[IsoRim] Could not load models: " + e);
            }
        }
    }
}

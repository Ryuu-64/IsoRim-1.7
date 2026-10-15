using System;
using RimWorld;
using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace IsoRim
{
    // Sky and distance fog for the perspective camera (C1 and C2).
    //
    // The sky (Shaders\IsoRimSky.*) is a full-screen quad at the far end of the depth range, so it
    // only covers pixels nothing was drawn on: above the horizon, and the void past the map. The fog
    // (Shaders\IsoRimDistFog.*) is a stack of full-screen quads at set distances, each depth-tested:
    // the engine has no depth texture to read, but a layer only shows where the scene is further away
    // than it, so the stack gives fog by real distance. It ends solid in the horizon colour of the sky
    // just before the view distance, so the edge of what is drawn never shows.
    //
    // Colours follow the game: the sun's glow picks night, dusk or day, rain, snow and fog grey it,
    // and the sun and moon sit where IsoRim's light comes from.
    public static class Sky
    {
        private const int FogLayers = 24;

        private static bool tried;
        private static Material skyMat;
        private static Material fogMat;
        private static Material planeMat;
        private static Mesh plane;
        private static IntVec3 planeSize;
        private static Mesh quad;
        private static Mesh fogMesh;

        private static readonly int fwdId = Shader.PropertyToID("_SkyFwd");
        private static readonly int rightId = Shader.PropertyToID("_SkyRight");
        private static readonly int upId = Shader.PropertyToID("_SkyUp");
        private static readonly int zenithId = Shader.PropertyToID("_SkyZenith");
        private static readonly int horizonId = Shader.PropertyToID("_SkyHorizon");
        private static readonly int sunId = Shader.PropertyToID("_SkySun");
        private static readonly int sunColorId = Shader.PropertyToID("_SkySunColor");
        private static readonly int moonId = Shader.PropertyToID("_SkyMoon");
        private static readonly int fogColorId = Shader.PropertyToID("_FogColor");
        private static readonly int fogDepthId = Shader.PropertyToID("_FogDepth");
        private static readonly int fogShapeId = Shader.PropertyToID("_FogShape");

        // The colour the fog fades into this frame (the sky's horizon).
        public static Color Horizon { get; private set; }

        private static readonly Color NightZenith = new Color(0.015f, 0.025f, 0.06f);
        private static readonly Color NightHorizon = new Color(0.06f, 0.08f, 0.14f);
        private static readonly Color DuskZenith = new Color(0.26f, 0.30f, 0.55f);
        private static readonly Color DuskHorizon = new Color(0.93f, 0.60f, 0.40f);
        private static readonly Color DayZenith = new Color(0.30f, 0.52f, 0.86f);
        private static readonly Color DayHorizon = new Color(0.72f, 0.82f, 0.92f);

        // Called from View3D.ApplyCamera's frame, with the perspective camera's basis.
        // tanX and tanY are the tangents of half the field of view across and up the screen.
        public static void Draw(Camera cam, Vector3 fwd, Vector3 right, Vector3 up, float tanX, float tanY,
            float near, float far, float fogStart, float fogEnd)
        {
            IsoRimSettings st = IsoRimMod.Settings;
            if ((!st.sky && !st.distanceFog) || cam == null || !WorldRendererUtility.DrawingMap)
            {
                return;
            }
            if (!tried)
            {
                Init();
            }
            Map map = Find.CurrentMap;
            if (map == null)
            {
                return;
            }
            try
            {
                Color zenith, horizon;
                float sunShow, moonShow;
                Colours(map, out zenith, out horizon, out sunShow, out moonShow);
                Horizon = horizon;

                // The game's terrain writes no depth: give the ground its depth first, or the fog and the
                // sky would cover it as if it were empty.
                if (planeMat != null)
                {
                    Graphics.DrawMesh(PlaneFor(map), Matrix4x4.identity, planeMat, 0, cam);
                }

                if (st.distanceFog && fogMat != null)
                {
                    fogMat.SetVector(fogColorId, new Vector4(horizon.r, horizon.g, horizon.b, 1f));
                    fogMat.SetVector(fogDepthId, new Vector4(near, far, fogStart, Mathf.Max(fogEnd, fogStart + 1f)));
                    fogMat.SetVector(fogShapeId, new Vector4(1.6f, FogLayers, Mathf.Clamp01(st.fogStrength), 0f));
                    Graphics.DrawMesh(fogMesh, Matrix4x4.identity, fogMat, 0, cam);
                }

                if (st.sky && skyMat != null)
                {
                    float hour = GenLocalDate.HourFloat(map);
                    Vector3 sun = R3DMaterials.SunDirAt(Mathf.Clamp(hour, 4.5f, 19.5f));
                    Vector3 moon = R3DMaterials.MoonDir(map);
                    // The sun only shows while it is up.
                    sunShow *= Mathf.Clamp01(sun.y * 6f + 0.3f);
                    Color sunColor = Color.Lerp(new Color(1f, 0.55f, 0.3f), new Color(1f, 0.95f, 0.82f), Mathf.Clamp01(sun.y * 2.5f));

                    skyMat.SetVector(fwdId, fwd);
                    skyMat.SetVector(rightId, right * tanX);
                    skyMat.SetVector(upId, up * tanY);
                    skyMat.SetVector(zenithId, (Vector4)zenith);
                    skyMat.SetVector(horizonId, (Vector4)horizon);
                    skyMat.SetVector(sunId, new Vector4(sun.x, sun.y, sun.z, sunShow));
                    skyMat.SetVector(sunColorId, (Vector4)sunColor);
                    skyMat.SetVector(moonId, new Vector4(moon.x, moon.y, moon.z, moonShow));
                    Graphics.DrawMesh(quad, Matrix4x4.identity, skyMat, 0, cam);
                }
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[IsoRim] Drawing the sky failed: " + e, 93611951);
            }
        }

        // Night, dusk or day by the sun's glow; greyed by rain, snow and fog.
        private static void Colours(Map map, out Color zenith, out Color horizon, out float sunShow, out float moonShow)
        {
            float glow = GenCelestial.CurCelestialSunGlow(map);
            if (glow < 0.45f)
            {
                float t = Mathf.InverseLerp(0.1f, 0.45f, glow);
                zenith = Color.Lerp(NightZenith, DuskZenith, t);
                horizon = Color.Lerp(NightHorizon, DuskHorizon, t);
            }
            else
            {
                float t = Mathf.InverseLerp(0.45f, 0.8f, glow);
                zenith = Color.Lerp(DuskZenith, DayZenith, t);
                horizon = Color.Lerp(DuskHorizon, DayHorizon, t);
            }
            float night = R3DMaterials.NightWeight(glow);
            sunShow = 1f - night;
            moonShow = night;

            float grey = 0f;
            WeatherManager w = map.weatherManager;
            if (w != null && w.curWeather != null)
            {
                float lerp = w.TransitionLerpFactor;
                float fogNow = w.curWeather.defName.Contains("Fog") ? 0.8f : 0f;
                float fogLast = w.lastWeather != null && w.lastWeather.defName.Contains("Fog") ? 0.8f : 0f;
                grey = Mathf.Clamp01(w.RainRate * 0.9f + w.SnowRate * 0.9f + Mathf.Lerp(fogLast, fogNow, lerp));
            }
            if (grey > 0f)
            {
                zenith = Greyed(zenith, grey);
                horizon = Greyed(horizon, grey);
                sunShow *= 1f - grey;
                moonShow *= 1f - grey;
            }
        }

        private static Color Greyed(Color c, float amount)
        {
            float l = c.r * 0.3f + c.g * 0.55f + c.b * 0.15f;
            Color g = new Color(l * 0.88f, l * 0.9f, l * 0.95f);
            return Color.Lerp(c, g, amount);
        }

        // The map's ground, one quad at height 0 (rebuilt when the map size changes).
        private static Mesh PlaneFor(Map map)
        {
            IntVec3 size = map.Size;
            if (plane != null && size == planeSize)
            {
                return plane;
            }
            planeSize = size;
            if (plane == null)
            {
                plane = new Mesh();
                plane.name = "IsoRim_DepthPlane";
            }
            plane.vertices = new Vector3[] { new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, size.z), new Vector3(size.x, 0f, size.z), new Vector3(size.x, 0f, 0f) };
            plane.triangles = new int[] { 0, 1, 2, 0, 2, 3 };
            plane.RecalculateBounds();
            MeshGuard.Check(plane, "sky depth plane");
            return plane;
        }

        private static void Init()
        {
            tried = true;
            Shader depth = R3DMaterials.ShaderOf("IsoRimDepthPlane");
            if (depth != null)
            {
                planeMat = new Material(depth);
                planeMat.name = "IsoRim_DepthPlane";
            }
            Shader sky = R3DMaterials.ShaderOf("IsoRimSky");
            Shader fog = R3DMaterials.ShaderOf("IsoRimDistFog");
            if (sky != null)
            {
                skyMat = new Material(sky);
                skyMat.name = "IsoRim_Sky";
            }
            if (fog != null)
            {
                fogMat = new Material(fog);
                fogMat.name = "IsoRim_DistFog";
            }

            quad = new Mesh();
            quad.name = "IsoRim_Sky";
            quad.vertices = new Vector3[] { new Vector3(-1f, -1f, 0f), new Vector3(-1f, 1f, 0f), new Vector3(1f, 1f, 0f), new Vector3(1f, -1f, 0f) };
            quad.triangles = new int[] { 0, 1, 2, 0, 2, 3 };
            quad.RecalculateBounds();
            MeshGuard.Check(quad, "sky quad");

            // One quad per layer; uv x tells the vertex stage which layer (and so how far and how see-through).
            Vector3[] verts = new Vector3[FogLayers * 4];
            Vector2[] uvs = new Vector2[FogLayers * 4];
            int[] tris = new int[FogLayers * 6];
            for (int i = 0; i < FogLayers; i++)
            {
                float t = (i + 1) / (float)FogLayers;
                int v = i * 4;
                verts[v] = new Vector3(-1f, -1f, 0f);
                verts[v + 1] = new Vector3(-1f, 1f, 0f);
                verts[v + 2] = new Vector3(1f, 1f, 0f);
                verts[v + 3] = new Vector3(1f, -1f, 0f);
                for (int k = 0; k < 4; k++)
                {
                    uvs[v + k] = new Vector2(t, 0f);
                }
                int q = i * 6;
                tris[q] = v;
                tris[q + 1] = v + 1;
                tris[q + 2] = v + 2;
                tris[q + 3] = v;
                tris[q + 4] = v + 2;
                tris[q + 5] = v + 3;
            }
            fogMesh = new Mesh();
            fogMesh.name = "IsoRim_DistFog";
            fogMesh.vertices = verts;
            fogMesh.uv = uvs;
            fogMesh.triangles = tris;
            fogMesh.RecalculateBounds();
            MeshGuard.Check(fogMesh, "distance fog layers");
        }
    }
}

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using Verse;

namespace IsoRim
{
    // PROTOTYPE (branch fadeproto, setting fadeUpper, default off): the floors above the viewed one are drawn faded instead
    // of hidden, by screen-door transparency. They stay opaque, depth-written geometry; each of their fragments is dropped
    // where a 4 x 4 ordered dither pattern fixed on the screen is at or above the floor's kept share (Keep), so the viewed
    // floor shows through the gaps. No sorting, no blending.
    //
    // Which floors (Floors.Refresh): the viewed floor k + 1 keeps Keep[0] of its pixels, k + 2 Keep[1], ...; floors further
    // up stay hidden as before. The pointer never works on them (Floors.PointerFloorAt starts at the viewed floor).
    //
    // How a draw is faded: everything drawn for a faded floor passes Graphics with Floors.FadeSlot set (a block of that
    // floor drawn: DrawSection_Prefix; a thing on it drawn every frame: ThingPhase_Prefix) or with a world matrix in that
    // floor's rows (Floors.Fade_Prefix). There its material is swapped for a copy (For) whose _IsoFade is the kept share:
    // IsoRim's own fragment stages (IsoRimSolid, cards, fog lid) read _IsoFade; the game's Cutout, CutoutComplex and
    // terrain shaders get copies with the same drop (Shaders\IsoRimFade*.frag.glsl, defs IsoRimFade*). Materials of any
    // other shader are drawn unfaded. Copies follow their source (material version) and are kept per source and share.
    public static class FadeFloors
    {
        // Kept share of pixels for the floors 1, 2, ... above the viewed one (more entries: more floors drawn faded).
        public static float[] Keep = { 0.5f, 0.25f };

        // Where in the dither pattern (0-1, wrapping) each of those kept ranges starts. Where two faded floors overlap on the
        // screen, the upper covers the lower only in the pixels their ranges share: 0 and 0.375 overlap by 2 of 4 (as alpha
        // blending: k + 1 shows 37.5%, k + 2 25%, the viewed floor 37.5%); 0 and 0 nest them (k + 2 steals all its pixels
        // from k + 1); 0 and 0.5 keep them apart (the viewed floor gets only what both leave).
        public static float[] From = { 0f, 0.375f };

        public static bool Enabled
        {
            get { return IsoRimMod.Settings != null && IsoRimMod.Settings.fadeUpper; }
        }

        private static readonly int fadeId = Shader.PropertyToID("_IsoFade");
        private static readonly int fromId = Shader.PropertyToID("_IsoFadeFrom");

        private sealed class Copy
        {
            public Material mat;
            public int version = int.MinValue;
            public float keep = -1f;
            public float from = -1f;
        }

        private sealed class Entry
        {
            public Copy[] copies;
        }

        private static readonly ConditionalWeakTable<Material, Entry> copies = new ConditionalWeakTable<Material, Entry>();
        private static Dictionary<Shader, Shader> shaderMap;

        // Vanilla ShaderDefs with a faded twin, and IsoRim's own defs whose fragment stage reads _IsoFade itself.
        private static readonly string[][] Twins =
        {
            new[] { "Cutout", "IsoRimFadeCutout" },
            new[] { "CutoutComplex", "IsoRimFadeCutoutComplex" },
            new[] { "TerrainHard", "IsoRimFadeTerrainHard" },
            new[] { "TerrainFade", "IsoRimFadeTerrainFade" },
            new[] { "TerrainFadeRough", "IsoRimFadeTerrainFadeRough" },
        };

        private static readonly string[] Own =
        {
            "IsoRimSolid", "IsoRimSolidTex", "IsoRimOutline", "IsoRimGhost", "IsoRimBlend", "IsoRimLitMark", "IsoRimLitMarkTex",
            "IsoRimLitMarkCard", "IsoRimCardPlant", "IsoRimCardSnow", "IsoRimFog", "IsoRimFadeCutout", "IsoRimFadeCutoutComplex",
            "IsoRimFadeTerrainHard", "IsoRimFadeTerrainFade", "IsoRimFadeTerrainFadeRough",
        };

        private static void BuildMap()
        {
            shaderMap = new Dictionary<Shader, Shader>();
            foreach (string name in Own)
            {
                ShaderDef d = DefDatabase<ShaderDef>.GetNamedSilentFail(name);
                if (d != null && d.Shader != null)
                {
                    shaderMap[d.Shader] = d.Shader;
                }
            }
            foreach (string[] t in Twins)
            {
                ShaderDef from = DefDatabase<ShaderDef>.GetNamedSilentFail(t[0]);
                ShaderDef to = DefDatabase<ShaderDef>.GetNamedSilentFail(t[1]);
                if (from != null && to != null && from.Shader != null && to.Shader != null)
                {
                    shaderMap[from.Shader] = to.Shader;
                }
            }
        }

        private static float FromOf(int slot)
        {
            return slot - 1 < From.Length ? From[slot - 1] : 0f;
        }

        // The faded copy of a material for slot (1 = the floor right above the viewed one), or the material itself when its
        // shader has no faded form.
        public static Material For(Material src, int slot)
        {
            if (src == null || slot <= 0 || slot > Keep.Length || src.IsDeferredMaterial)
            {
                return src;
            }
            if (shaderMap == null)
            {
                BuildMap();
            }
            Shader s = src.shader;
            Shader to;
            if (s == null || !shaderMap.TryGetValue(s, out to))
            {
                return src;
            }
            Entry e = copies.GetValue(src, _ => new Entry { copies = new Copy[Keep.Length] });
            if (e.copies.Length < Keep.Length)
            {
                System.Array.Resize(ref e.copies, Keep.Length);
            }
            Copy c = e.copies[slot - 1];
            if (c == null)
            {
                c = e.copies[slot - 1] = new Copy { mat = new Material(to) { name = src.name + "_Fade" + slot } };
            }
            int v = src.engineMaterial.Version;
            if (c.version != v || c.keep != Keep[slot - 1] || c.from != FromOf(slot))
            {
                c.mat.CopyPropertiesFromMaterial(src);
                c.mat.renderQueue = src.renderQueue;
                c.mat.SetFloat(fadeId, Keep[slot - 1]);
                c.mat.SetFloat(fromId, FromOf(slot));
                c.version = v;
                c.keep = Keep[slot - 1];
                c.from = FromOf(slot);
            }
            return c.mat;
        }
    }
}

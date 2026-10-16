using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace IsoRim
{
    // A part of a model that bobs up and down (the grav engine's orb): the entry's bobModel, drawn every frame over the
    // baked body, raised by the game's own motion: 0.5 * (1 + sin(2 pi * ticks / bobPeriod)) * bobHeight cells, by game
    // ticks, so it keeps pace with the game speed and stands still while paused. A grav engine's orb rests at its lowest
    // point while the engine cools down after a flight (as Building_GravEngine.DrawAt does). The game's own flat orb
    // (drawn in Building_GravEngine.DrawAt after the body) is left out while the model stands in; with the 3D view or
    // models off the game draws everything as usual.
    public static class Bobbers
    {
        private static List<ThingDef> defs;
        private static Action<ThingWithComps, Vector3, bool> baseDrawAt;

        private static void Init()
        {
            defs = new List<ThingDef>();
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefs)
            {
                ModelEntry entry;
                if (ModelLibrary.TryGet(def, out entry) && entry.bob != null && entry.HasModel)
                {
                    defs.Add(def);
                }
            }
        }

        // How far up the part is now, in cells.
        public static float Rise(Thing t, ModelDef def)
        {
            Building_GravEngine engine = t as Building_GravEngine;
            if (engine != null && Find.TickManager.TicksGame < engine.cooldownCompleteTick)
            {
                return 0f;
            }
            int period = Mathf.Max(1, def.bobPeriod);
            return 0.5f * (1f + Mathf.Sin(Mathf.PI * 2f * GenTicks.TicksGame / period)) * def.bobHeight;
        }

        // Called every frame while the 3D view is on.
        public static void DrawAll()
        {
            Map map = Find.CurrentMap;
            if (!IsoRimMod.Settings.models3D || map == null || Find.TickManager == null)
            {
                return;
            }
            try
            {
                if (defs == null)
                {
                    Init();
                }
                if (defs.Count == 0)
                {
                    return;
                }
                CellRect view = Find.CameraDriver.CurrentViewRect.ExpandedBy(3);
                for (int d = 0; d < defs.Count; d++)
                {
                    ThingForm form = Forms.For(defs[d]);
                    if (form.form != Form3D.Model || form.model == null || form.model.bob == null)
                    {
                        continue;
                    }
                    List<Thing> things = map.listerThings.ThingsOfDef(defs[d]);
                    for (int i = 0; i < things.Count; i++)
                    {
                        Thing t = things[i];
                        if (view.Contains(t.Position) && !t.Fogged())
                        {
                            Draw(t, form.model);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[IsoRim] Drawing bobbing parts failed: " + e, 93611251);
            }
        }

        private static void Draw(Thing t, ModelEntry entry)
        {
            ModelDef def = entry.def;
            float yaw = entry.Yaw(t.Rotation);
            if (entry.Swapped(t.def))
            {
                yaw += 90f;
            }
            Vector3 at = t.TrueCenter();
            Vector3 offset = Quaternion.AngleAxis(yaw, Vector3.up) * def.offset;
            DynamicModels.Draw(entry.bob, new Vector3(at.x + offset.x, offset.y + Rise(t, def), at.z + offset.z), yaw,
                entry.Fit(t.def) * def.scale, R3DTextures.ModelTint(t), R3DTextures.ModelSwatch(t));
        }

        // Building_GravEngine.DrawAt prefix: while the model stands in (body baked, orb drawn here), the engine is drawn
        // as a plain ThingWithComps (body held back by the Thing.DrawAt prefix, comps drawn as usual), without its flat orb.
        public static bool GravEngineDrawAt_Prefix(Building_GravEngine engine, Vector3 drawLoc, bool flip)
        {
            try
            {
                if (engine == null || !IsoRimMod.Settings.models3D || !SectionLayer_IsoRimBodies.StandsIn(engine))
                {
                    return true;
                }
                ModelEntry entry = Forms.For(engine.def).model;
                if (entry == null || entry.bob == null)
                {
                    return true;
                }
                if (baseDrawAt == null)
                {
                    baseDrawAt = AccessTools.MethodDelegate<Action<ThingWithComps, Vector3, bool>>(
                        AccessTools.DeclaredMethod(typeof(ThingWithComps), "DrawAt", new[] { typeof(Vector3), typeof(bool) }), null, false, (Type[])null);
                }
                baseDrawAt(engine, drawLoc, flip);
                return false;
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[IsoRim] Leaving out the grav engine's flat orb failed: " + e, 93611252);
                return true;
            }
        }

        // For the harness: how far up a thing's bobbing part is, and whether the model stands in for the body.
        public static string Report(Thing t)
        {
            ModelEntry entry;
            if (!ModelLibrary.TryGet(t.def, out entry) || entry.bob == null)
            {
                return "no bob part";
            }
            return "rise " + Rise(t, entry.def).ToString("0.000") + " standsIn " + SectionLayer_IsoRimBodies.StandsIn(t);
        }
    }
}

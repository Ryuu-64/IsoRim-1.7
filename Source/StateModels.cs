using System.Collections.Generic;
using RimWorld;
using Verse;

namespace IsoRim
{
    // Buildings the game draws differently once they are empty (CompEmptyStateGraphic: an opened
    // mech capsule). With a model whose entry has an emptyModel, ModelBaker.Bake picks that model
    // while the game would draw the empty state, and the block is rebuilt when the state changes
    // (checked as the game draws the thing each frame, Patches.ThingDraw_Prefix). Either way, once
    // a model stands in, the game's flat "empty" sprite drawn every frame over the body is left
    // out (Patches.EmptyStatePostDraw_Prefix); the one it prints into the map mesh already is,
    // with the rest of the sprite.
    public static class StateModels
    {
        // Things with an empty-state sprite whose model is in a built mesh, with the state it was built in.
        private static readonly Dictionary<int, bool> baked = new Dictionary<int, bool>();
        // hackedGlowOff copies, by the id of the model they were made from.
        private static readonly Dictionary<int, Model> glowOff = new Dictionary<int, Model>();

        public static CompEmptyStateGraphic CompOf(Thing t)
        {
            ThingWithComps twc = t as ThingWithComps;
            return twc != null ? twc.GetComp<CompEmptyStateGraphic>() : null;
        }

        public static bool IsEmpty(Thing t)
        {
            CompEmptyStateGraphic c = CompOf(t);
            return c != null && c.ParentIsEmpty;
        }

        // Called by ModelBaker.Bake for the real thing (not a ghost): which model to use, and noted.
        public static Model Choose(Thing t, ModelEntry entry, Model normal)
        {
            // A map portal with an open model (ancient stockpile hatch): open while it can be entered, as the
            // game's Print picks its open sprite. The game redraws the map mesh when that changes (hacked).
            if (entry.open != null && t is MapPortal portal)
            {
                return portal.IsEnterable(out string _) ? entry.open : normal;
            }
            // A hacked model (relay stabilizer: its core goes dark), as the game's CompHackable.PostPrintOnto prints its
            // hacked sprite. Hacking redraws the map mesh (CompHackable.Hack: DirtyMapMesh), so the block is rebuilt.
            // hackedGlowOff: the same model with its glow faces dark (the ancient grav reactor's panel).
            if (entry.hacked != null || entry.def.hackedGlowOff)
            {
                CompHackable hack = (t as ThingWithComps)?.GetComp<CompHackable>();
                if (hack != null && hack.IsHacked)
                {
                    if (entry.hacked != null)
                    {
                        return entry.hacked;
                    }
                    Model off;
                    if (!glowOff.TryGetValue(normal.id, out off))
                    {
                        off = ObjLoader.GlowOff(normal);
                        glowOff[normal.id] = off;
                    }
                    return off;
                }
            }
            CompEmptyStateGraphic c = CompOf(t);
            if (c == null)
            {
                return normal;
            }
            bool empty = c.ParentIsEmpty;
            baked[t.thingIDNumber] = empty;
            return empty && entry.empty != null ? entry.empty : normal;
        }

        // True while a model stands in for this thing's body (and so for its empty-state sprite).
        public static bool StandsIn(Thing t)
        {
            return View3D.Ready && baked.ContainsKey(t.thingIDNumber) && Forms.For(t.def).form == Form3D.Model;
        }

        // Each frame the game draws the thing: rebuilds its block when it has just been emptied (or filled).
        public static void Track(Thing t)
        {
            bool was;
            if (!View3D.Ready || !baked.TryGetValue(t.thingIDNumber, out was))
            {
                return;
            }
            ThingForm f = Forms.For(t.def);
            if (f.form != Form3D.Model || f.model == null || f.model.empty == null)
            {
                return;
            }
            bool now = IsEmpty(t);
            if (now == was || t.Map == null)
            {
                return;
            }
            baked[t.thingIDNumber] = now;
            if (t.def.drawerType == DrawerType.RealtimeOnly)
            {
                MapSections.QueueAround(t.Map, t.Position, 0, typeof(SectionLayer_IsoRimBodies), true);
            }
            else
            {
                t.DirtyMapMesh(t.Map);
            }
        }

        // For the harness: the state each such thing was last built in.
        public static string Report(Thing t)
        {
            bool was;
            return baked.TryGetValue(t.thingIDNumber, out was) ? (was ? "empty" : "full") : "not built";
        }
    }
}

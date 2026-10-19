using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace IsoRim
{
    // Rock (and any mineable block: ore, collapsed rock) marked for mining (the game's Mine / MineVein cell designations)
    // is drawn with its whole block tinted toward the colour of the game's mine mark, so the marked blocks stand out in 3D,
    // where the flat mark on a tall block is hard to see. The game's mark itself stays as it is.
    //
    // One place: the block printers (WallBoxes.PrintBlock / PrintRock / PrintPlainSides) take their colour through Tint.
    // The game redraws nothing when a designation is added or removed (its cell marks are drawn every frame), so the
    // patches on DesignationManager.AddDesignation / RemoveDesignation (Patches.MineDesignation_Postfix) mark the block of
    // that cell to be redrawn: the wall layer of its section only (or the game's things layer for a block it prints, with
    // the mod's wall layer off). No cost per frame; one section redraw per changed section, when the game next draws it.
    public static class MineMarks
    {
        // The mark's colour. The game's Designations/Mine (and MineVein) texture is a square of dusty terracotta,
        // RGB (166, 117, 107), at about 10% opacity with a dark rim: too grey and faint to read as a tint on rock.
        // The tint keeps its hue (about 10 degrees, red-orange) made clear: more saturated and full brightness.
        public static readonly Color MarkColour = new Color(1f, 0.46f, 0.35f, 1f);

        // How far the block's colour goes toward the mark's colour (0 = not at all, 1 = the mark's colour).
        public const float DefaultStrength = 0.4f;   // the stone and ore still read through it

        // Harness A/B (probe keys minetint=, minetintcol=): another strength (below 0: the default) or colour.
        public static float StrengthOverride = -1f;
        public static Color? ColourOverride;

        public static float Strength
        {
            get { return StrengthOverride >= 0f ? StrengthOverride : DefaultStrength; }
        }

        public static Color Colour
        {
            get { return ColourOverride ?? MarkColour; }
        }

        // Whether the block is a mineable whose cell is marked for mining.
        public static bool Marked(Thing t)
        {
            if (t == null || !t.Spawned || !t.def.mineable)
            {
                return false;
            }
            Map map = t.Map;
            DesignationManager dm = map != null ? map.designationManager : null;
            if (dm == null)
            {
                return false;
            }
            IntVec3 c = t.Position;
            return dm.DesignationAt(c, DesignationDefOf.Mine) != null || dm.DesignationAt(c, DesignationDefOf.MineVein) != null;
        }

        // The colour a block is drawn in: its own, or tinted toward the mark's colour when marked for mining.
        public static Color Tint(Thing t, Color own)
        {
            return Marked(t) ? Toward(own) : own;
        }

        // A colour tinted toward the mark's colour.
        public static Color Toward(Color own)
        {
            Color c = Color.Lerp(own, Colour, Strength);
            c.a = own.a;
            return c;
        }

        // A mine designation was added to or removed from cell c: its block is redrawn (the wall layer of its section; or
        // the game's things layer when the block is printed there).
        public static void Changed(Map map, Designation des)
        {
            if (des == null || map == null || !View3D.Active || Current.ProgramState != ProgramState.Playing
                || (des.def != DesignationDefOf.Mine && des.def != DesignationDefOf.MineVein) || des.target.HasThing)
            {
                return;
            }
            try
            {
                IntVec3 c = des.target.Cell;
                if (!c.InBounds(map) || map.mapDrawer == null)
                {
                    return;
                }
                Building b = c.GetEdifice(map);
                if (b == null)
                {
                    return;
                }
                if (SectionLayer_IsoRimWalls.Handles(b))
                {
                    SectionLayer_IsoRimWalls.Mark(map, c);
                }
                else
                {
                    map.mapDrawer.MapMeshDirty(c, MapMeshFlagDefOf.Things);
                }
            }
            catch (Exception e)
            {
                Log.ErrorOnce("[IsoRim] Redrawing a block marked for mining failed: " + e, 93611290);
            }
        }
    }
}

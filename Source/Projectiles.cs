using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace IsoRim
{
    // Bullets, arrows and shells in 3D. The game draws a projectile flat on the ground along its flight, so in the 3D
    // view shots seemed to leave from the shooter's feet. Here it flies from the muzzle (the height the shooter holds
    // its weapon, or a turret's gun) and its height blends along the flight to the height of what it was aimed at
    // (a standing pawn's chest, a lying pawn, a building's middle, the ground). A lobbed shell's arc, which the game
    // draws as a shift up the screen, becomes real height. The shadow stays on the ground. The game launches every shot
    // from the shooter's middle; it is drawn leaving from the muzzle in front instead (Muzzle, the same point as the
    // flash below) and back on the game's path by the target. Only the drawing moves: the flight and the hit are the
    // game's. A shell from a manned turret leaves from the turret's gun, not the pawn's hands (MuzzleOwner). With the
    // 3D view off the game draws as usual.
    //
    // The shot's muzzle flash too (ShotFlash): the game puts it flat on the ground in the middle of the shooter's
    // cell, which in 3D is inside the shooter's body. Here it is put at the muzzle the bullet leaves from (Muzzle: a
    // gun's length toward what it shoots at, MuzzleReach, at MuzzleHeight) and drawn standing up (Emitters.Raise). Every
    // shot flash of the game (1.7 source, Core and DLC defs):
    //   - Verb.TryCastNextBurstShot: FleckMaker.Static(caster.Position, ..., ShotFlash, verbProps.muzzleFlashScale)
    //     for every verb with a muzzleFlashScale, the only use of either in code. Covers guns held by people and
    //     mechs (Core, Biotech, Odyssey, Anomaly weapons), turret guns (security and mech turrets, mech cluster mini
    //     turrets, Odyssey's ancient armored turret, Anomaly's tactical turret; manned mortars too: the caster is the
    //     turret), a mech's turret comp (CompTurretGun, the centurion's charge blaster: the caster is the mech),
    //     apparel verbs (Royalty's grenadier and phoenix armour) and abilities (Anomaly's heatspikes, fleshmelter
    //     bolt). Replaced here (Patches.ShotFlash_Transpiler). No vanilla animal verb has a muzzle flash; a modded one
    //     comes out of the animal's front, as a mech's.
    //   - Effecters with ShotFlash as their fleck (JumpFlightEffect, JumpMechFlightEffect: a jump pack's take-off;
    //     SpikecoreSparks: a ritual): not shots, left as the game's.
    public static class Projectiles
    {
        private sealed class Flight
        {
            public float start;   // cells above the ground at the muzzle
            public float end;     // cells above the ground at the target
            public int launcherRow;   // map rows of the shooter and the target (their floors, Floors)
            public int targetRow;
            public Vector3 lead;      // from the game's launch point (the shooter's middle) to the muzzle, flat
        }

        private static readonly ConditionalWeakTable<Projectile, Flight> flights = new ConditionalWeakTable<Projectile, Flight>();

        private static readonly AccessTools.FieldRef<Projectile, Vector3> originRef = AccessTools.FieldRefAccess<Projectile, Vector3>("origin");
        private static readonly AccessTools.FieldRef<Projectile, Vector3> destinationRef = AccessTools.FieldRefAccess<Projectile, Vector3>("destination");
        private static readonly Func<Projectile, float> arcHeight = AccessTools.MethodDelegate<Func<Projectile, float>>(AccessTools.PropertyGetter(typeof(Projectile), "ArcHeightFactor"));
        private static readonly Func<Projectile, float> arcFraction = AccessTools.MethodDelegate<Func<Projectile, float>>(AccessTools.PropertyGetter(typeof(Projectile), "DistanceCoveredFractionArc"));
        private static readonly Action<ThingWithComps> postDraw = AccessTools.MethodDelegate<Action<ThingWithComps>>(AccessTools.Method(typeof(ThingWithComps), "Comps_PostDraw"));
        private static readonly AccessTools.FieldRef<Material> shadowMat = AccessTools.StaticFieldRefAccess<Material>(AccessTools.Field(typeof(Projectile), "shadowMaterial"));

        // Projectile.DrawAt in 3D: the game's drawing redone with the height added. False = drawn here.
        public static bool DrawAt(Projectile p, Vector3 drawLoc)
        {
            if (!View3D.Ready || p == null || p.def?.projectile == null)
            {
                return true;
            }
            // Stacked floors (Floors): the projectile is drawn over the ground cell of its row, its height counted from the
            // ground (a shot between floors blends from the shooter's floor to the target's); hidden with its floor.
            Vector3 row = Vector3.zero;
            if (Floors.On)
            {
                Matrix4x4 unused;
                if (!Floors.RowMatrix(p.Position.z, out unused))
                {
                    return false;
                }
                row = Floors.RowOffset(p.Position.z);
            }
            float arc = arcHeight(p) * GenMath.InverseParabola(arcFraction(p));
            float h = Height(p, drawLoc) + arc;
            // Drawn leaving from the muzzle, not the shooter's middle, and back on the game's path by the target (only
            // the drawing moves: the flight, the hit and ExactPosition are the game's).
            drawLoc += flights.GetValue(p, Begin).lead * (1f - Flown(p, drawLoc));
            bool wasFloor = DrawHook.floor;
            DrawHook.floor = false;   // the floor is in the position already (the graphic's draw goes through DrawHook)
            try
            {
                if (p.def.projectile.shadowSize > 0f)
                {
                    DrawShadow(p, drawLoc + new Vector3(0f, row.y / View3D.Squash, row.z), arc);
                }
                Vector3 at = drawLoc + new Vector3(0f, h / View3D.Squash, row.z);
                Quaternion rotation = p.ExactRotation;
                if (p.def.projectile.spinRate != 0f)
                {
                    float period = 60f / p.def.projectile.spinRate;
                    rotation = Quaternion.AngleAxis(Find.TickManager.TicksGame % period / period * 360f, Vector3.up);
                }
                if (p.def.projectile.useGraphicClass)
                {
                    p.Graphic.Draw(at, p.Rotation, p, rotation.eulerAngles.y);
                }
                else
                {
                    Graphics.DrawMesh(MeshPool.GridPlane(p.def.graphicData.drawSize), at, rotation, p.DrawMat, 0);
                }
                postDraw(p);
            }
            finally
            {
                DrawHook.floor = wasFloor;
            }
            return false;
        }

        // Height of the floor a row is on (cells), zero on the ground and without floors.
        private static float FloorHeight(int z)
        {
            return Floors.On ? Floors.RowHeight(z) : 0f;
        }

        // Height above the ground (cells) at this point of the flight: muzzle to target, by the share of the way flown.
        public static float Height(Projectile p, Vector3 at)
        {
            Flight f = flights.GetValue(p, Begin);
            return Mathf.Lerp(f.start + FloorHeight(f.launcherRow), f.end + FloorHeight(f.targetRow), Flown(p, at));
        }

        // The share of the way (flat) from launch to destination at this point of the game's path, 0 to 1.
        private static float Flown(Projectile p, Vector3 at)
        {
            Vector3 o = originRef(p);
            Vector3 d = destinationRef(p);
            float total = new Vector2(d.x - o.x, d.z - o.z).magnitude;
            float done = new Vector2(at.x - o.x, at.z - o.z).magnitude;
            return total > 0.001f ? Mathf.Clamp01(done / total) : 1f;
        }

        private static Flight Begin(Projectile p)
        {
            Flight f = new Flight();
            try
            {
                Vector3 o = originRef(p);
                Thing owner = FromMuzzle ? MuzzleOwner(p.Launcher, p.EquipmentDef, o, p.Map) : p.Launcher;
                f.start = MuzzleHeight(owner);
                // From the muzzle (as the flash, ShotFlash) when the shot really left from the shooter (not a shell
                // dropped from elsewhere with a launcher named).
                if (FromMuzzle && owner != null && owner.Spawned && owner.Map == p.Map && new Vector2(owner.DrawPos.x - o.x, owner.DrawPos.z - o.z).magnitude < 1.5f)
                {
                    Vector3 muzzle = Muzzle(owner, p.EquipmentDef, o, FlatDirection(o, destinationRef(p)));
                    f.start = muzzle.y;
                    f.lead = new Vector3(muzzle.x - o.x, 0f, muzzle.z - o.z);
                }
                f.end = AimHeight(p);
                f.launcherRow = owner != null && owner.Spawned ? owner.Position.z : p.Position.z;
                f.targetRow = Mathf.FloorToInt(destinationRef(p).z);   // where it comes down (a gameplay addon may move it)
            }
            catch (Exception e)
            {
                f.start = f.end = 0.5f;
                Log.ErrorOnce("[IsoRim] Working out a projectile's height failed: " + e, 93611501);
            }
            return f;
        }

        // How high the shooter fires from, in cells above its feet.
        public static float MuzzleHeight(Thing launcher)
        {
            if (launcher == null)
            {
                return 0.5f;
            }
            if (launcher is Pawn pawn)
            {
                if (pawn.GetPosture() != PawnPosture.Standing)
                {
                    return 0.2f;
                }
                // About where a 3D figure or a standing sprite holds its weapon (People, Patches.HandHeight).
                return Interaction.PickHeight(pawn) * (pawn.RaceProps != null && pawn.RaceProps.Humanlike ? 0.42f : 0.6f);
            }
            if (launcher is Building_Turret turret)
            {
                Vector2 tip;
                if (FromMuzzle && GunTip(turret, out tip))
                {
                    return tip.y;
                }
                ModelEntry entry;
                if (ModelLibrary.TryGet(turret.def, out entry) && entry.HasModel && entry.def.topHeight > 0f)
                {
                    return entry.def.topHeight * entry.Fit(turret.def).y;
                }
                return Mathf.Clamp(Interaction.PickHeight(turret) * 0.75f, 0.3f, 1.5f);
            }
            return Mathf.Clamp(Interaction.PickHeight(launcher) * 0.5f, 0.1f, 1.5f);
        }

        // Harness only (probe muzzle=0): the flash and the bullet as before (flat flash, bullet from the middle).
        public static bool FromMuzzle = true;

        // Shot flashes so far, and who fired the last one (probe tickuntil=flash).
        public static int Flashes;
        public static Thing LastFlasher;

        // In place of the game's FleckMaker.Static(cell, map, ShotFlash, scale) in Verb.TryCastNextBurstShot.
        public static void ShotFlash(IntVec3 cell, Map map, FleckDef fleck, float scale, Verb verb)
        {
            Flashes++;
            Thing caster = verb != null ? verb.caster : null;
            LastFlasher = caster;
            if (FromMuzzle && View3D.Ready && caster != null && caster.Spawned && caster.Map == map)
            {
                try
                {
                    // The same point the bullet is drawn leaving from (Muzzle; the game launches it from caster.DrawPos).
                    Thing owner = MuzzleOwner(caster, verb.EquipmentSource != null ? verb.EquipmentSource.def : null, caster.DrawPos, map);
                    Vector3 at = Muzzle(owner, verb.EquipmentSource != null ? verb.EquipmentSource.def : null, caster.DrawPos, Aim(verb));
                    FleckCreationData data = FleckMaker.GetDataStatic(new Vector3(at.x, 0f, at.z), map, fleck, scale);
                    Emitters.Raise(data.spawnPosition, at.y, 0f, fleck);
                    map.flecks.CreateFleck(data);
                    return;
                }
                catch (Exception e)
                {
                    Log.ErrorOnce("[IsoRim] Putting a muzzle flash at the muzzle failed: " + e, 93611503);
                }
            }
            FleckMaker.Static(cell, map, fleck, scale);
        }

        // Which way the shot leaves the gun (flat, unit length; zero if it cannot tell): as the gun is drawn, toward
        // the target as seen (between floors too) or along a spraying gun's sweep (AimAngleOverride).
        private static Vector3 Aim(Verb verb)
        {
            float? over = verb.AimAngleOverride;
            if (over.HasValue)
            {
                return Quaternion.AngleAxis(over.Value - 90f, Vector3.up) * Vector3.right;
            }
            LocalTargetInfo target = verb.CurrentTarget;
            if (!target.IsValid)
            {
                return Vector3.zero;
            }
            return FlatDirection(verb.caster.DrawPos, target.HasThing ? target.Thing.DrawPos : target.Cell.ToVector3Shifted());
        }

        // From one point toward another as drawn (between floors too), flat, unit length; zero if they are the same.
        private static Vector3 FlatDirection(Vector3 from, Vector3 to)
        {
            Vector3 d = Floors.FlatDelta(from, to);
            return d.x * d.x + d.z * d.z > 0.001f ? d.normalized : Vector3.zero;
        }

        // The muzzle, the one point both a shot's flash and its bullet's drawn flight start from: from (where the game
        // launches the shot, the shooter's middle) moved MuzzleReach along dir (flat), at MuzzleHeight (in y, cells
        // above the ground). gun is the weapon's def (null if unknown).
        public static Vector3 Muzzle(Thing owner, ThingDef gun, Vector3 from, Vector3 dir)
        {
            Vector3 at = from + dir * MuzzleReach(owner, gun);
            return new Vector3(at.x, MuzzleHeight(owner), at.z);
        }

        // Who the shot leaves from: the turret, when its gun was fired by the pawn manning it (a mortar: the game
        // names the pawn as the launcher but launches from the turret); otherwise the launcher itself.
        public static Thing MuzzleOwner(Thing launcher, ThingDef gun, Vector3 from, Map map)
        {
            if (launcher is Pawn && gun != null && map != null)
            {
                IntVec3 c = from.ToIntVec3();
                Building_Turret turret = c.InBounds(map) ? c.GetEdifice(map) as Building_Turret : null;
                // (Verb_LaunchProjectile passes the manned turret itself as the equipment; its gun def too, to be safe.)
                if (turret != null && (turret.def == gun || (turret.def.building != null && turret.def.building.turretGunDef == gun)))
                {
                    return turret;
                }
            }
            return launcher;
        }

        private static readonly Dictionary<Model, float> tipHeights = new Dictionary<Model, float>();

        // The end of a turret's 3D gun as Patches.DrawTurret_Prefix draws it: x how far in front of the pivot (along the
        // way it aims), y how high above the ground (a mortar's barrel points up: its mouth is well above the pivot).
        // The gun model points along +x; the mouth is the middle height of its vertices at the far end. False without one.
        private static bool GunTip(Building_Turret turret, out Vector2 tip)
        {
            tip = Vector2.zero;
            ModelEntry entry;
            if (!IsoRimMod.Settings.models3D || !ModelLibrary.TryGet(turret.def, out entry) || entry.top == null)
            {
                return false;
            }
            Model gun = entry.top;
            float y;
            if (!tipHeights.TryGetValue(gun, out y))
            {
                float near = gun.maxX - 0.05f * Mathf.Max(0.01f, gun.maxX - gun.minX);
                float lo = float.MaxValue, hi = float.MinValue;
                for (int i = 0; i < gun.positions.Length; i++)
                {
                    if (gun.positions[i].x >= near)
                    {
                        lo = Mathf.Min(lo, gun.positions[i].y);
                        hi = Mathf.Max(hi, gun.positions[i].y);
                    }
                }
                y = lo <= hi ? (lo + hi) * 0.5f : 0f;
                tipHeights[gun] = y;
            }
            Vector3 fit = entry.Fit(turret.def) * entry.def.scale;
            tip = new Vector2(gun.maxX * fit.x, (entry.def.topHeight + y) * fit.y);
            return true;
        }

        // How far in front of the shooter's middle its muzzle is (flat, cells).
        public static float MuzzleReach(Thing owner, ThingDef gun)
        {
            if (owner is Building_Turret turret)
            {
                // The tip of the 3D gun, or of the game's flat one.
                Vector2 tip;
                if (GunTip(turret, out tip))
                {
                    return tip.x;
                }
                return turret.def.building != null ? turret.def.building.turretTopDrawSize * 0.4f : 0.5f;
            }
            // Where a person holds the gun (Patches.DrawHeldWeapon); a mech or an animal fires from about its front.
            float reach = 0.3f;
            Pawn pawn = owner as Pawn;
            if (pawn != null && pawn.RaceProps != null && !pawn.RaceProps.Humanlike)
            {
                GraphicData body = pawn.ageTracker != null && pawn.ageTracker.CurKindLifeStage != null ? pawn.ageTracker.CurKindLifeStage.bodyGraphicData : null;
                reach = Mathf.Max(reach, (body != null ? body.drawSize.x : 1f) * 0.3f);
            }
            // Plus half the gun in its hands (SpriteSolids draws it at 0.8 of its drawn size, centred on the hands);
            // not a mech's turret comp or an apparel or ability verb.
            ThingWithComps held = pawn != null && pawn.equipment != null ? pawn.equipment.Primary : null;
            if (held != null && gun != null && held.def == gun && gun.graphicData != null)
            {
                reach += gun.graphicData.drawSize.x * 0.4f;
            }
            return reach;
        }

        // How high what the shot is aimed at is hit: the thing hit, or for a miss the thing it was meant for.
        public static float AimHeight(Projectile p)
        {
            Thing t = p.usedTarget.Thing;
            if (t == null && p.intendedTarget.HasThing && p.intendedTarget.Cell.InHorDistOf(p.usedTarget.Cell, 4f))
            {
                t = p.intendedTarget.Thing;
            }
            if (t == null)
            {
                return 0.1f;
            }
            if (t is Pawn pawn)
            {
                return pawn.Dead || pawn.GetPosture() != PawnPosture.Standing ? 0.12f : Interaction.PickHeight(pawn) * 0.45f;
            }
            return Mathf.Clamp(Interaction.PickHeight(t) * 0.5f, 0.05f, 1f);
        }

        // The game's ground shadow (shells), at the shadow's place on the ground: the arc is height now, not a shift.
        private static void DrawShadow(Projectile p, Vector3 drawLoc, float arc)
        {
            Material mat = shadowMat();
            if (mat == null)
            {
                return;
            }
            float size = p.def.projectile.shadowSize * Mathf.Lerp(1f, 0.6f, arc);
            Matrix4x4 m = Matrix4x4.TRS(drawLoc + new Vector3(0f, -0.01f, 0f), Quaternion.identity, new Vector3(size, 1f, size));
            Graphics.DrawMesh(MeshPool.plane10, m, mat, 0);
        }
    }
}

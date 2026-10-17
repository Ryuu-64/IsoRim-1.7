using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace IsoRim
{
    // Harness-only keys for how 3D figures hold their weapons (FigureWeapons.cs). Real jobs, run with real ticks.
    //   carrydowned=x,z,tx,tz      the drafted colonist in (x,z) given the game's drafted carry job (CarryDownedPawnDrafted)
    //                              for the downed pawn in (tx,tz)
    //   tickjitter=N,x,z,min       game ticks one at a time (at most N) until the pawn in (x,z) is pushed by its jitter
    //                              (a melee lunge, a hit) at least min cells
    //   shieldfull=x,z             the shield belt worn by the pawn in (x,z) charged full
    //   weaponcheck=1              logs every standing colonist's held weapon as drawn (FigureWeapons.Report, by reflection:
    //                              builds without it too), its jitter and lean
    public static partial class Probe
    {
        private static readonly AccessTools.FieldRef<Pawn_DrawTracker, JitterHandler> probeJitter =
            AccessTools.FieldRefAccess<Pawn_DrawTracker, JitterHandler>("jitterer");

        private static bool WeaponKey(string key, string[] a, Map map)
        {
            switch (key)
            {
                case "carrydowned":
                {
                    Pawn p = PawnAt(map, I(a[0]), I(a[1]));
                    Pawn down = PawnAt(map, I(a[2]), I(a[3]));
                    if (p == null || down == null)
                    {
                        return true;
                    }
                    Job job = JobMaker.MakeJob(JobDefOf.CarryDownedPawnDrafted, down);
                    job.count = 1;
                    job.playerForced = true;
                    p.jobs.StartJob(job, JobCondition.InterruptForced);
                    Log.Message("[IsoRim probe] carrydowned: " + p.LabelShort + " (drafted " + p.Drafted + ") carries " + down.LabelShort + " (downed " + down.Downed + "): " + (p.CurJob == job));
                    return true;
                }
                case "tickjitter":
                {
                    Pawn p = PawnAt(map, I(a[1]), I(a[2]));
                    if (p == null)
                    {
                        return true;
                    }
                    int limit = Mathf.Clamp(I(a[0]), 1, 900), done = 0;
                    float min = F(a[3]);
                    while (done < limit && probeJitter(p.Drawer).CurrentOffset.magnitude < min)
                    {
                        Find.TickManager.DoSingleTick();
                        done++;
                    }
                    foreach (Pawn tp in map.mapPawns.AllPawnsSpawned)
                    {
                        tp.Drawer.tweener.ResetTweenedPosToRoot();
                    }
                    Log.Message("[IsoRim probe] tickjitter " + p.LabelShort + ": " + done + " ticks, jitter " + probeJitter(p.Drawer).CurrentOffset.ToString("F3"));
                    return true;
                }
                case "visuals":
                {
                    // visuals=N: the per-frame visual catch-up the game runs while unpaused (Pawn.ProcessPostTickVisuals: lean
                    // out of cover, jitter decay) for N ticks; the harness game is paused, so these never advance by themselves.
                    int n = Mathf.Clamp(I(a[0]), 1, 900);
                    CellRect all = CellRect.WholeMap(map);
                    foreach (Pawn p in map.mapPawns.AllPawnsSpawned.ToList())
                    {
                        p.ProcessPostTickVisuals(n, all);
                    }
                    return true;
                }
                case "shieldfull":
                {
                    Pawn p = PawnAt(map, I(a[0]), I(a[1]));
                    CompShield shield = p != null && p.apparel != null ? p.apparel.WornApparel.Select(x => x.GetComp<CompShield>()).FirstOrDefault(x => x != null) : null;
                    if (shield == null)
                    {
                        Log.Warning("[IsoRim probe] shieldfull: no shield belt");
                        return true;
                    }
                    Traverse.Create(shield).Field("energy").SetValue(shield.parent.GetStatValue(StatDefOf.EnergyShieldEnergyMax));
                    Log.Message("[IsoRim probe] shieldfull " + p.LabelShort + ": energy " + shield.Energy.ToString("0.00") + ", state " + shield.ShieldState);
                    return true;
                }
                case "weaponcheck":
                {
                    System.Reflection.MethodInfo report = AccessTools.Method("IsoRim.FigureWeapons:Report");
                    foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
                    {
                        if (p.GetPosture() != PawnPosture.Standing)
                        {
                            continue;
                        }
                        string drawn = report != null ? (string)report.Invoke(null, new object[] { p }) : "no FigureWeapons in this build";
                        Log.Message("[IsoRim probe] weaponcheck " + p.LabelShort + " at " + p.Position + " job " + (p.CurJob != null ? p.CurJob.def.defName : "-")
                            + " stance " + (p.stances != null && p.stances.curStance != null ? p.stances.curStance.GetType().Name : "-")
                            + " drafted " + p.Drafted + " carrying " + (p.carryTracker != null && p.carryTracker.CarriedThing != null ? p.carryTracker.CarriedThing.LabelShort : "-")
                            + " rot " + p.Rotation + " jitter " + probeJitter(p.Drawer).CurrentOffset.ToString("F3") + " lean " + p.Drawer.leaner.LeanOffset.ToString("F3")
                            + ": " + drawn);
                    }
                    return true;
                }
            }
            return false;
        }
    }
}

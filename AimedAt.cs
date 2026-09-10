using System;
using UnityEngine;

namespace SbgShields
{
    /// <summary>
    /// EXPERIMENTAL (0.7.8): the line-of-fire read for hitscan weapons.
    ///
    /// The pistol and the elephant gun are raycasts on the shooter's client: nothing
    /// is ever in flight, so the reach-based parry in ShieldState can never see them
    /// coming. What every client CAN see is the shooter: whether they are aiming
    /// (PlayerInfo.NetworkedIsAimingItem), what they hold (NetworkedEquippedItem) and
    /// their aim yaw (AnimatorIo.AimingYawOffset, all three SyncVars). So the read
    /// becomes: someone has a bead on you when you let go.
    ///
    /// Nothing here names an item. Any aimed item counts, which is what makes it
    /// generic: a rocket fired the moment you release is the same read, and the
    /// unblockables (railgun, laser) are refused later by the cost gate anyway.
    ///
    /// Yaw only. The aim pitch lives in the shooter's animator and is not a SyncVar,
    /// so the test is a horizontal corridor from the shooter through your bubble.
    /// Good enough for a first feel; if it reads wrong in play, delete this file and
    /// the one call in ShieldState.ArmParryOnRelease and the Parry.AimedAt* entries.
    /// </summary>
    internal static class AimedAt
    {
        private static bool _loggedRanges;

        /// <summary>
        /// True if any other player is aiming an item along a line that passes within
        /// <paramref name="tolerance"/> metres of <paramref name="centre"/>, out to
        /// Parry.AimedAtRange. <paramref name="who"/> describes the closest such shooter.
        /// </summary>
        internal static bool Anyone(PlayerInfo me, Vector3 centre, float tolerance, out string who)
        {
            who = null;
            if (!Plugin.AimedAtParry.Value) return false;

            if (!_loggedRanges)
            {
                _loggedRanges = true;
                try
                {
                    var s = GameManager.ItemSettings;
                    Plugin.Log.LogInfo($"Game gun ranges: pistol {s.DuelingPistolMaxShotDistance:0} m, elephant gun {s.ElephantGunMaxShotDistance:0} m. Parry.AimedAtRange is {Plugin.AimedAtRange.Value:0} m.");
                }
                catch { }
            }

            float range = Mathf.Max(1f, Plugin.AimedAtRange.Value);
            float bestMiss = float.MaxValue;

            System.Collections.Generic.IReadOnlyList<PlayerInfo> remote;
            try { remote = GameManager.RemotePlayers; } catch { return false; }
            if (remote == null) return false;

            foreach (var p in remote)
            {
                if (p == null || ReferenceEquals(p, me)) continue;
                try
                {
                    if (!p.NetworkedIsAimingItem || p.NetworkedEquippedItem == ItemType.None) continue;

                    Vector3 from = p.ChestBone != null ? p.ChestBone.position : p.transform.position + Vector3.up;
                    Vector3 to = centre - from;
                    float dist = to.magnitude;
                    if (dist > range) continue;

                    // Aim direction = body facing turned by the replicated yaw offset.
                    float yaw = 0f;
                    try { yaw = p.AnimatorIo.AimingYawOffset; } catch { }
                    Vector3 aim = Quaternion.AngleAxis(yaw, Vector3.up) * p.transform.forward;
                    aim.y = 0f;
                    if (aim.sqrMagnitude < 1e-4f) continue;
                    aim.Normalize();

                    Vector3 flat = to; flat.y = 0f;
                    float along = Vector3.Dot(flat, aim);
                    if (along <= 0f) continue;                       // facing away
                    float miss = Vector3.Cross(aim, flat).magnitude; // perpendicular distance, horizontal
                    if (miss > tolerance) continue;

                    if (miss < bestMiss)
                    {
                        bestMiss = miss;
                        string name; try { name = p.PlayerId.PlayerNameNoRichText; } catch { name = "a player"; }
                        who = $"{name} aiming {p.NetworkedEquippedItem} from {dist:0.0}m (off by {miss:0.0}m)";
                    }
                }
                catch (Exception e)
                {
                    if (Plugin.VerboseLogging.Value) Plugin.Log.LogWarning("AimedAt check: " + e.Message);
                }
            }
            return who != null;
        }
    }
}

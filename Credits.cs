using System;
using UnityEngine;

namespace SbgShields
{
    /// <summary>
    /// A star KO in the game's own kill feed, on every screen: "attacker  [icon]  victim".
    /// The feed is the game's (InfoFeed), so it looks and stacks exactly like every other
    /// elimination. Only the server can post to it, so the host does this when the dying
    /// player's StarKo message arrives. The icon is the elimination icon of the hit that
    /// launched them, when the game has one for it.
    /// </summary>
    internal static class Credits
    {
        internal static void PostStarKo(PlayerInfo victim, PlayerInfo by, KnockoutType type)
        {
            if (victim == null || !Plugin.KillFeedStarKo.Value) return;
            try
            {
                if (!Enum.TryParse(type.ToString(), out EliminationReason reason) || !GameManager.EliminationSettings.TryGetEliminationData(reason, out _))
                    reason = EliminationReason.Swing;
                if (by != null && !ReferenceEquals(by, victim))
                    InfoFeed.ShowEliminationMessage(by, victim, reason);
                else
                    InfoFeed.ShowSelfEliminationMessage(victim, reason);
            }
            catch (Exception e) { Plugin.Log.LogWarning("Kill feed post failed: " + e.Message); }
        }
    }
}

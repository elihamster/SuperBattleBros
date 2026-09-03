using UnityEngine;

namespace SbgShields
{
    /// <summary>
    /// One launch, from the hit to wherever it ends. What is left of this after
    /// 0.7.1 is just the timeline, which two things read:
    ///
    ///  - IS CLOUD HIT gates hang time to launches taken at or above
    ///    CloudHitMinPercent. That is the only reader left.
    ///
    /// Two features used to live here and are gone. The APEX WAKE-UP ended the
    /// knockout at the top of the arc; getting the air back is now a consequence of
    /// hitstun getting SHORTER as percent climbs (Percent.HitstunMultiplierAtMax),
    /// which does the same job through the game's own timer instead of reaching in
    /// and calling RecoverFromKnockout. DISTANCE SENT ("sent 34 m", session best,
    /// high-score sound) is out until the record itself is designed -- what it is
    /// measured against, and over what span.
    /// </summary>
    internal static class Launch
    {
        private static bool   _active;
        private static bool   _cloudHit;   // launched at or above CloudHitMinPercent
        private static double _startedAt;

        /// <summary>True from the hit until the launch ends (landing, recovery, kill, respawn).</summary>
        internal static bool Active => _active;

        /// <summary>This launch is big enough for the cloud-hit extras (hang time).</summary>
        internal static bool IsCloudHit => _active && _cloudHit;

        internal static void Begin()
        {
            _active = true;
            _cloudHit = ShieldState.Percent >= Plugin.CloudHitMinPercent.Value;
            _startedAt = Time.timeAsDouble;
        }

        internal static void Cancel() { _active = false; }

        /// <summary>Called from the FixedUpdate prefix for the local player.</summary>
        internal static void Tick(PlayerMovement mv, Rigidbody rb)
        {
            if (!_active) return;
            double now = Time.timeAsDouble;

            // Let go if something else took over (respawn, star KO) or it has gone on too long.
            if (mv.IsRespawningOrDrowning || KillZone.IsLingering || now - _startedAt > 12.0) { End("cancelled"); return; }

            // Over when you are back on the ground, or when the stun ran out in the air
            // and you have control again. (IsGrounded is the walking check and stays
            // false while tumbling; the knockout state machine has its own ground test.)
            bool down = mv.IsGrounded || mv.KnockoutState == KnockoutState.OnGround;
            if (down && now - _startedAt > 0.2) End("landed");
            else if (!mv.IsKnockedOutOrRecovering && now - _startedAt > 0.2) End("recovered");
        }

        private static void End(string how)
        {
            _active = false;
            if (Plugin.VerboseLogging.Value) Plugin.Log.LogInfo($"Launch ended ({how}) after {Time.timeAsDouble - _startedAt:0.00}s.");
        }
    }
}

using HarmonyLib;
using UnityEngine;

namespace SbgShields
{
    /// <summary>
    /// One launch, from the hit to wherever it ends. Two things read it:
    ///
    ///  - IS CLOUD HIT gates hang time to launches taken at or above CloudHitMinPercent.
    ///  - DIRECTIONAL INFLUENCE: for DIWindow after the hit, the first real stick input
    ///    bends the launch. Push toward where on the SCREEN you want to drift and the
    ///    launch's horizontal direction turns toward it, by up to DIMaxYaw at full
    ///    stick. W is into the screen, S is toward the camera, A and D are screen left
    ///    and right: the same camera-relative stick the game uses for walking. Speed
    ///    and elevation never change, so it is where you land, not how far or how high.
    ///
    ///    An earlier version turned the launch left or right of its own travel, so A
    ///    meant screen-left when you flew away from the camera and screen-right when
    ///    you flew toward it. Camera-relative has one rule and no exceptions. Smash
    ///    reads DI on the hit frame; reading the first input inside a short window is
    ///    the same idea with a little forgiveness for reaction time. Local: the launch
    ///    is the victim's own velocity, which replicates like any movement.
    ///
    /// Two features used to live here and are gone. The APEX WAKE-UP ended the
    /// knockout at the top of the arc; it was replaced by hitstun getting SHORTER as
    /// percent climbs (Percent.HitstunMultiplierAtMax), through the game's own timer.
    /// Since 0.7.8 the timer ending mid-air no longer wakes you either: with
    /// Launch.StayDownUntilLanding on, AirHold in Patches.cs raises the shield and
    /// keeps you tumbling until you land. DISTANCE SENT ("sent 34 m", session best,
    /// high-score sound) is out until the record itself is designed -- what it is
    /// measured against, and over what span.
    /// </summary>
    internal static class Launch
    {
        private static bool   _active;
        private static bool   _cloudHit;   // launched at or above CloudHitMinPercent
        private static double _startedAt;

        private static double _diUntil = double.MinValue;
        private static bool   _diDone = true;

        /// <summary>True from the hit until the launch ends (landing, recovery, kill, respawn).</summary>
        internal static bool Active => _active;

        /// <summary>This launch is big enough for the cloud-hit extras (hang time).</summary>
        internal static bool IsCloudHit => _active && _cloudHit;

        internal static void Begin()
        {
            _active = true;
            _cloudHit = ShieldState.Percent >= Plugin.CloudHitMinPercent.Value;
            _startedAt = Time.timeAsDouble;
            _diDone  = !Plugin.DirectionalInfluence.Value;
            _diUntil = _startedAt + Mathf.Max(0f, Plugin.DIWindow.Value);
        }

        internal static void Cancel() { _active = false; _diDone = true; }

        /// <summary>Called from the FixedUpdate prefix for the local player.</summary>
        internal static void Tick(PlayerMovement mv, Rigidbody rb)
        {
            if (!_active) return;
            double now = Time.timeAsDouble;

            // Let go if something else took over (respawn, star KO) or it has gone on too long.
            if (mv.IsRespawningOrDrowning || KillZone.IsLingering || now - _startedAt > 12.0) { End("cancelled"); return; }

            if (!_diDone)
            {
                if (now >= _diUntil) _diDone = true;
                else TryDirectionalInfluence(mv, rb);
            }

            // Over when you are back on the ground. Waking in the air does NOT end it:
            // the launch is still carrying you, and TumbleGravityPatch reads this to
            // keep the fall a tumble's fall. (IsGrounded is the walking check and stays
            // false while tumbling; the knockout state machine has its own ground test.)
            bool down = mv.IsGrounded || mv.KnockoutState == KnockoutState.OnGround;
            if (down && now - _startedAt > 0.2) End("landed");
        }

        // The game's camera-relative stick, already turned into a world direction: the
        // one walking uses. Private, but written every frame by ProcessMovementInput
        // whether or not movement is suppressed, which is exactly what we need.
        private static AccessTools.FieldRef<PlayerMovement, Vector3> _rawWorldMove;
        private static bool _rawLooked, _rawFailed;

        private static bool BindInput()
        {
            if (_rawLooked) return !_rawFailed;
            _rawLooked = true;
            try { _rawWorldMove = AccessTools.FieldRefAccess<PlayerMovement, Vector3>("rawWorldMoveVector3d"); }
            catch (System.Exception e)
            {
                _rawFailed = true;
                Plugin.Log.LogWarning("rawWorldMoveVector3d not found; directional influence disabled. " + e.Message);
            }
            return !_rawFailed;
        }

        private static void TryDirectionalInfluence(PlayerMovement mv, Rigidbody rb)
        {
            if (rb == null || !BindInput()) { _diDone = true; return; }

            Vector3 want = _rawWorldMove(mv);
            want.y = 0f;
            float mag = Mathf.Clamp01(want.magnitude);
            if (mag < 0.3f) return;                       // no real input yet; keep listening inside the window
            _diDone = true;
            want /= Mathf.Max(want.magnitude, 1e-4f);

            Vector3 v     = rb.linearVelocity;
            float   speed = v.magnitude;
            var     h     = new Vector3(v.x, 0f, v.z);
            float   hMag  = h.magnitude;
            if (speed < 1f || hMag < 0.1f) return;        // straight up: nothing to steer
            Vector3 hDir  = h / hMag;

            // Turn the horizontal direction toward where the stick points on screen, by
            // up to DIMaxYaw at full stick. Elevation and speed are left alone.
            float toward = Vector3.SignedAngle(hDir, want, Vector3.up);
            float yaw    = Mathf.Clamp(toward, -Plugin.DIMaxYaw.Value, Plugin.DIMaxYaw.Value) * mag;

            Vector3 newH = Quaternion.AngleAxis(yaw, Vector3.up) * hDir;
            rb.linearVelocity = newH * hMag + Vector3.up * v.y;

            Plugin.Log.LogInfo($"DI: stick {mag:0.00} pointing {toward:+0;-0}° off the launch, turned {yaw:+0;-0}°.");
        }

        private static void End(string how)
        {
            _active = false;
            _diDone = true;
            if (Plugin.VerboseLogging.Value) Plugin.Log.LogInfo($"Launch ended ({how}) after {Time.timeAsDouble - _startedAt:0.00}s.");
        }
    }
}

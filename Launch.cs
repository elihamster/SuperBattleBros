using UnityEngine;

namespace SbgShields
{
    /// <summary>
    /// One launch, from the hit to wherever it ends. Two things read it:
    ///
    ///  - IS CLOUD HIT gates hang time to launches taken at or above CloudHitMinPercent.
    ///  - DIRECTIONAL INFLUENCE: for DIWindow after the hit, the first real stick input
    ///    bends the launch, Smash-style. The stick's vertical axis (W/S) steepens or
    ///    flattens it by up to DIMaxPitch; its horizontal axis (A/D) curves the flight
    ///    left or right of where it is going by up to DIMaxYaw. Speed never changes,
    ///    so it is where you land, not how far.
    ///
    ///    Why the raw stick and not the camera-relative world vector: on a keyboard
    ///    "W" has to mean one thing. Camera-relative, W was "toward wherever the camera
    ///    looks", which flipped between higher and flatter depending on which side you
    ///    were hit from. Raw, W is always up, the way a Smash player holds up to
    ///    survive. Smash reads DI on the hit frame; reading the first input inside a
    ///    short window is the same idea with a little forgiveness for reaction time.
    ///    Local: the launch is the victim's own velocity, which replicates like any
    ///    movement.
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

            // Over when you are back on the ground, or when the stun ran out in the air
            // and you have control again. (IsGrounded is the walking check and stays
            // false while tumbling; the knockout state machine has its own ground test.)
            bool down = mv.IsGrounded || mv.KnockoutState == KnockoutState.OnGround;
            if (down && now - _startedAt > 0.2) End("landed");
            else if (!mv.IsKnockedOutOrRecovering && now - _startedAt > 0.2) End("recovered");
        }

        private static void TryDirectionalInfluence(PlayerMovement mv, Rigidbody rb)
        {
            if (rb == null) { _diDone = true; return; }

            // The game's raw stick: x = A/D, y = W/S, public, written every frame by
            // PlayerInput even while movement is suppressed.
            Vector2 stick = mv.rawMoveVector2d;
            float mag = Mathf.Clamp01(stick.magnitude);
            if (mag < 0.3f) return;                       // no real input yet; keep listening inside the window
            _diDone = true;
            if (stick.magnitude > 1f) stick /= stick.magnitude;

            Vector3 v     = rb.linearVelocity;
            float   speed = v.magnitude;
            var     h     = new Vector3(v.x, 0f, v.z);
            float   hMag  = h.magnitude;
            if (speed < 1f || hMag < 0.1f) return;        // straight up: nothing to steer
            Vector3 hDir  = h / hMag;

            // D curves you to the right of your flight (clockwise seen from above), A to
            // the left. W raises the arc, S flattens it.
            float yaw     = stick.x * Plugin.DIMaxYaw.Value;
            float elev    = Mathf.Atan2(v.y, hMag) * Mathf.Rad2Deg;
            float newElev = Mathf.Clamp(elev + stick.y * Plugin.DIMaxPitch.Value, -89f, 89f);

            Vector3 newH = Quaternion.AngleAxis(yaw, Vector3.up) * hDir;
            float   rad  = newElev * Mathf.Deg2Rad;
            rb.linearVelocity = newH * (speed * Mathf.Cos(rad)) + Vector3.up * (speed * Mathf.Sin(rad));

            Plugin.Log.LogInfo($"DI: stick ({stick.x:0.00}, {stick.y:0.00}), curve {yaw:+0;-0}°, elevation {elev:0}° -> {newElev:0}°.");
        }

        private static void End(string how)
        {
            _active = false;
            _diDone = true;
            if (Plugin.VerboseLogging.Value) Plugin.Log.LogInfo($"Launch ended ({how}) after {Time.timeAsDouble - _startedAt:0.00}s.");
        }
    }
}

using System;
using System.Collections.Generic;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace SbgShields
{
    /// <summary>
    /// A parried homing ball goes back to whoever hit it, for every parrier, not only the host.
    ///
    /// The ball lives on the host. A parry read on the host's own machine opens the
    /// reflect window right where the ball is simulated, so the bubble bounces it in time.
    /// A client's read reaches the host a ping later, by which point the ball has usually
    /// already hit them: the client's game then sees the hit, the parry fires on it, and
    /// until 0.7.40 that counted as an ordinary parry (stunning the attacker) with the
    /// ball just dropping. Now the client tells the host which ball it parried, and the
    /// host relaunches it at the attacker the way the game's own shield reflect does.
    /// </summary>
    internal static class SendBack
    {
        private static readonly Dictionary<PlayerInfo, double> _lastBy = new Dictionary<PlayerInfo, double>();

        /// <summary>How far from the parrier the ball can be when the host gets the request (it keeps flying for a ping).</summary>
        private const float MaxBallDistance = 10f;

        private static readonly Action<Hittable, Hittable> _setHomingTarget = Bind<Action<Hittable, Hittable>>("ServerSetHomingTargetHittable");
        private static readonly Action<Hittable, PlayerGolfer, bool> _becomeProjectile = Bind<Action<Hittable, PlayerGolfer, bool>>("BecomeSwingProjectile");

        private static T Bind<T>(string method) where T : Delegate
        {
            try
            {
                var m = AccessTools.Method(typeof(Hittable), method);
                return m != null ? AccessTools.MethodDelegate<T>(m) : null;
            }
            catch (Exception e) { Plugin.Log.LogWarning($"SendBack: cannot bind Hittable.{method}: {e.Message}"); return null; }
        }

        /// <summary>The swing-projectile knockouts: a golf ball (or a giant's or a rocket driver's) coming in.</summary>
        internal static bool IsBall(KnockoutType type) =>
            type == KnockoutType.SwingProjectile || type == KnockoutType.ReflectedSwingProjectile
            || type == KnockoutType.RocketDriverSwingProjectile || type == KnockoutType.JumboBurgerGiantSwingProjectile;

        /// <summary>Host only: relaunch ball (netId) from the parrier's bubble at the attacker.</summary>
        internal static void ServerRelaunch(PlayerInfo parrier, PlayerInfo attacker, uint ballId)
        {
            if (!NetworkServer.active || parrier == null || attacker == null || ReferenceEquals(parrier, attacker)) return;
            if (!Plugin.ParryReflectsHoming.Value) return;
            if (_setHomingTarget == null || _becomeProjectile == null) return;

            double now = Time.timeAsDouble;
            if (_lastBy.TryGetValue(parrier, out double last) && now - last < 0.5) return;   // one parry, one ball

            if (!NetworkServer.spawned.TryGetValue(ballId, out var identity) || identity == null) { Log($"ball {ballId} is gone"); return; }
            var ball = identity.GetComponent<Hittable>();
            if (ball == null || ball.AsEntity == null || ball.AsEntity.IsPlayer || ball.AsEntity.Rigidbody == null) { Log($"{ballId} is not a ball"); return; }
            var rb = ball.AsEntity.Rigidbody;
            var shield = parrier.ElectromagnetShieldCollider;
            Vector3 centre = shield != null ? shield.transform.position : parrier.transform.position + Vector3.up;
            if ((rb.worldCenterOfMass - centre).sqrMagnitude > MaxBallDistance * MaxBallDistance) { Log("the ball is too far from the parrier"); return; }
            var target = attacker.AsHittable;
            var golfer = parrier.AsGolfer;
            if (target == null || golfer == null) return;
            _lastBy[parrier] = now;

            // Out of the bubble, on the attacker's side, so it does not start inside the parrier.
            Vector3 aim = attacker.transform.position + Vector3.up;
            Vector3 dir = aim - centre; dir.y = 0f;
            dir = dir.sqrMagnitude > 0.01f ? dir.normalized : parrier.transform.forward;
            float radius = 1f;
            if (shield != null)
                radius = shield.radius * Mathf.Max(shield.transform.lossyScale.x, shield.transform.lossyScale.y, shield.transform.lossyScale.z);
            Vector3 start = centre + dir * (radius + 0.6f);
            rb.position = start;
            ball.transform.position = start;

            // The game's own reflect (Hittable.ServerReflectOffElectromagnetShield), aimed at the attacker.
            _setHomingTarget(ball, target);
            Vector3 flat = aim - start; flat.y = 0f;
            ball.NetworkhomingInitialHorizontalDistance = flat.magnitude;
            _becomeProjectile(ball, golfer, true);
            float speed = ball.SwingSettings.MaxPowerSwingHitSpeed * (1f + GameManager.GolfSettings.MaxSwingOvercharge);
            Vector3 v = Vector3.RotateTowards(dir * speed, Vector3.up, GameManager.ItemSettings.ElectromagnetShieldProjectileReflectTiltUpMaxAngle * Mathf.Deg2Rad, 0f);
            rb.linearVelocity = v;
            ball.NetworkswingHitPosition = ball.transform.position;
            Plugin.Log.LogInfo($"Sent the ball back from {Name(parrier)} at {Name(attacker)}.");
        }

        private static void Log(string why)
        {
            if (Plugin.VerboseLogging.Value) Plugin.Log.LogInfo("Send-back skipped: " + why + ".");
        }

        private static string Name(PlayerInfo p)
        {
            try { return p.PlayerId.PlayerNameNoRichText; } catch { return "a player"; }
        }

        internal static void Reset() => _lastBy.Clear();
    }
}

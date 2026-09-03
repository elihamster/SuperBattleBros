using System;
using HarmonyLib;
using UnityEngine;

namespace SbgShields
{
    internal static class Local
    {
        internal static bool Is(PlayerInfo p) =>
            p != null && ReferenceEquals(p, GameManager.LocalPlayerInfo);

        /// <summary>Only OUR Shift shield roots. The vanilla magnet item's shield is left alone.</summary>
        internal static bool IsRooted(PlayerInfo p) =>
            Is(p) && p.IsElectromagnetShieldActive && Plugin.WeActivated;
    }

    // =====================================================================
    //  M2 -- Rooting
    // =====================================================================

    [HarmonyPatch(typeof(PlayerMovement), "CanMove")]
    internal static class RootMovementPatch
    {
        private static void Postfix(PlayerMovement __instance, ref bool __result)
        {
            if (__result && Plugin.RootWhileShielded.Value && Local.IsRooted(__instance.PlayerInfo))
                __result = false;
        }
    }

    [HarmonyPatch(typeof(PlayerMovement), "CanJump")]
    internal static class RootJumpPatch
    {
        private static void Postfix(PlayerMovement __instance, ref bool __result)
        {
            if (__result && Plugin.BlockJumpWhileShielded.Value && Local.IsRooted(__instance.PlayerInfo))
                __result = false;
        }
    }

    [HarmonyPatch(typeof(PlayerMovement), nameof(PlayerMovement.TryDive))]
    internal static class RootDivePatch
    {
        [HarmonyPriority(Priority.Low)]   // can skip the original
        private static bool Prefix(PlayerMovement __instance, ref bool __result)
        {
            if (Plugin.BlockDiveWhileShielded.Value && Local.IsRooted(__instance.PlayerInfo))
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(PlayerGolfer), nameof(PlayerGolfer.TryStartChargingSwing))]
    internal static class RootSwingPatch
    {
        [HarmonyPriority(Priority.Low)]   // can skip the original
        private static bool Prefix(PlayerGolfer __instance, ref bool __result)
        {
            if (Plugin.BlockSwingWhileShielded.Value && Local.IsRooted(__instance.PlayerInfo))
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    // =====================================================================
    //  M3/M4 -- Pips, percent, hitstun
    // =====================================================================

    /// <summary>
    /// The single chokepoint for every knockout type. Runs on the victim's client.
    /// Prefix decides whether the shield absorbs; postfix commits percent if the
    /// vanilla knockout actually happened.
    /// </summary>
    [HarmonyPatch(typeof(PlayerMovement), nameof(PlayerMovement.TryKnockOut))]
    internal static class KnockoutEconomyPatch
    {
        [HarmonyPriority(Priority.Low)]   // can skip the original; see BreakStunHoldPatch
        private static bool Prefix(PlayerMovement __instance, KnockoutType knockoutType,
            Vector3 localOrigin, float distance, Vector3 incomingVelocityChange, ref bool __result, ref bool isNewKnockout,
            ref bool blockedByTeamProtection, out bool __state)
        {
            __state = false;
            if (!Local.Is(__instance.PlayerInfo)) return true;
            __state = true;

            // The game passes where the hit came from (in our local space) and how far
            // away it was. Explosions use both: direction to make the launch radial from
            // the blast, distance to scale percent and force so a graze is not a direct hit.
            ShieldState.PendingHitDistance    = distance;
            ShieldState.PendingHitLocalOrigin = localOrigin;

            bool proceed = ShieldState.ResolveKnockout(__instance.PlayerInfo, knockoutType, incomingVelocityChange);
            if (!proceed)
            {
                __result = false;
                isNewKnockout = false;
                blockedByTeamProtection = false;
                return false;
            }
            return true;
        }

        private static void Postfix(bool __result, bool __state)
        {
            if (!__state) return;
            ShieldState.OnKnockoutResolved(__result);
        }
    }

    /// <summary>
    /// Stash whether the incoming ball was locked on to us. TryKnockOut does not
    /// receive the projectile, but this handler runs immediately before it.
    /// </summary>
    [HarmonyPatch(typeof(PlayerMovement), "OnLocalPlayerWillApplySwingProjectileHitPhysics")]
    internal static class ProjectileTargetingPatch
    {
        private static void Prefix(PlayerMovement __instance, Hittable hitter)
        {
            if (!Local.Is(__instance.PlayerInfo)) return;
            try
            {
                var target = hitter != null ? hitter.NetworkhomingTargetHittable : null;
                ShieldState.PendingProjectileWasTargeted =
                    target != null && ReferenceEquals(target, __instance.PlayerInfo.AsHittable);
            }
            catch { ShieldState.PendingProjectileWasTargeted = false; }
        }
    }

    /// <summary>
    /// timeUntilKnockoutRecovery is a flat constant set inside SetKnockOutState(InAir).
    /// Scale it here. This is the whole hitstun system.
    /// </summary>
    [HarmonyPatch(typeof(PlayerMovement), "SetKnockOutState")]
    internal static class HitstunPatch
    {
        private static AccessTools.FieldRef<PlayerMovement, float> _timeUntilRecovery;

        private static bool Prepare()
        {
            try
            {
                _timeUntilRecovery = AccessTools.FieldRefAccess<PlayerMovement, float>("timeUntilKnockoutRecovery");
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("timeUntilKnockoutRecovery not found; hitstun scaling disabled. " + e.Message);
                return false;
            }
        }

        private static void Prefix(PlayerMovement __instance, out bool __state)
        {
            // Was the player already knocked out before this call? The game only
            // resets the recovery timer on the None -> knocked-out transition, and
            // this setter is called every physics frame while airborne.
            __state = __instance.IsKnockedOutOrRecovering;
        }

        private static void Postfix(PlayerMovement __instance, KnockoutState state, bool __state)
        {
            if (state != KnockoutState.InAir || !Local.Is(__instance.PlayerInfo)) return;
            if (__state || !__instance.IsKnockedOutOrRecovering) return; // not a fresh knockout

            ref float t = ref _timeUntilRecovery(__instance);
            float before = t;

            // Once per session, on the first knockout of any kind: the game's own
            // constants. KnockoutDuration is the number HitstunMultiplierAtMax scales,
            // so tuning stun without it is guesswork. One line, always on.
            if (!_loggedTimeouts)
            {
                _loggedTimeouts = true;
                try
                {
                    var s = GameManager.PlayerMovementSettings;
                    Plugin.Log.LogInfo($"Game knockout constants: KnockoutDuration={s.KnockoutDuration:0.00}s, KnockoutTimeOutDuration={s.KnockoutTimeOutDuration:0.00}s, LongImmunity={s.PostKnockoutImmunityLongDuration:0.00}s");
                }
                catch { }
            }

            if (ShieldState.PendingHitstunAbsolute >= 0f)
            {
                t = ShieldState.PendingHitstunAbsolute;
                // Break stun: also hold the recovery gate shut for exactly this long,
                // whatever else the game decides (see BreakStunHoldPatch).
                ShieldState.BreakStunUntil = Time.timeAsDouble + t;
                ShieldState.LastKnockoutWasBreakStun = true;
                BreakTrace.Log($"stun applied: timer={t:0.00}s, hold until +{t:0.00}s");
            }
            else
            {
                ShieldState.BreakStunUntil = double.MinValue;
                ShieldState.LastKnockoutWasBreakStun = false;
                if (ShieldState.PendingHitstunMultiplier >= 0f)
                    t *= ShieldState.PendingHitstunMultiplier;
                else
                    t *= ShieldState.HitstunMultiplier; // knockout from a path we did not see (should be rare)
            }

            ShieldState.PendingHitstunAbsolute   = -1f;
            ShieldState.PendingHitstunMultiplier = -1f;

            if (Plugin.VerboseLogging.Value && Mathf.Abs(before - t) > 0.01f)
                Plugin.Log.LogInfo($"Hitstun {before:0.00}s -> {t:0.00}s");
        }

        private static bool _loggedTimeouts;
    }

    /// <summary>
    /// Belt and braces for the break stun. The game recovers a knocked-out player
    /// through RecoverFromKnockout, reached from its recovery timer, its knockout
    /// time-out, and a few other places. While a break stun is running, refuse
    /// all of them. Freezing and the direct SetKnockOutState(None) paths
    /// (teleport, respawn, elimination, invisibility) are untouched.
    /// </summary>
    /// <summary>
    /// Always-on trace of what the game does during a shield-break stun. Fires only
    /// for a few seconds after a break, so it costs nothing the rest of the time,
    /// and it is the thing to paste when "the stun does not hold".
    /// </summary>
    internal static class BreakTrace
    {
        private static double _start = double.MinValue;
        private static double _until = double.MinValue;

        /// <summary>Called the moment our shield breaks, BEFORE the game decides the knockout.</summary>
        internal static void Begin(float seconds)
        {
            _start = Time.timeAsDouble;
            _until = _start + seconds + 1.5;
        }

        internal static bool Active => Time.timeAsDouble < _until;

        internal static void Log(string what)
        {
            if (!Active) return;
            var mv = GameManager.LocalPlayerInfo != null ? GameManager.LocalPlayerInfo.Movement : null;
            string st = mv != null ? mv.KnockoutState.ToString() : "?";
            double left = ShieldState.BreakStunUntil - Time.timeAsDouble;
            string hold = ShieldState.BreakStunUntil == double.MinValue ? "none" : left.ToString("0.00");
            Plugin.Log.LogInfo($"[break +{Time.timeAsDouble - _start:0.00}s] {what} | state={st} holdLeft={hold}");
        }
    }

    [HarmonyPatch(typeof(PlayerMovement), "SetKnockOutState")]
    internal static class KnockoutStateTracePatch
    {
        private static void Prefix(PlayerMovement __instance, KnockoutState state)
        {
            if (!BreakTrace.Active || !Local.Is(__instance.PlayerInfo)) return;
            if (state != __instance.KnockoutState) BreakTrace.Log($"SetKnockOutState {__instance.KnockoutState} -> {state}");
        }
    }

    [HarmonyPatch(typeof(PlayerMovement), "StartKnockoutImmunity")]
    internal static class ImmunityTracePatch
    {
        private static void Prefix(PlayerMovement __instance, bool fromPlayerAggression)
        {
            if (!BreakTrace.Active || !Local.Is(__instance.PlayerInfo)) return;
            BreakTrace.Log($"StartKnockoutImmunity(fromPlayerAggression={fromPlayerAggression})");
        }
    }

    [HarmonyPatch(typeof(PlayerMovement), "CanBeKnockedOutBy")]
    internal static class CanBeKnockedOutTracePatch
    {
        private static void Postfix(PlayerMovement __instance, bool __result, bool suppressKnockoutImmunity, bool blockedByTeamProtection)
        {
            if (!BreakTrace.Active || !Local.Is(__instance.PlayerInfo)) return;
            BreakTrace.Log($"CanBeKnockedOutBy -> {__result} (immunitySuppressed={suppressKnockoutImmunity}, team={blockedByTeamProtection}, hasImmunity={__instance.KnockoutImmunityStatus.hasImmunity}, shieldActive={__instance.PlayerInfo.IsElectromagnetShieldActive})");
        }
    }

    [HarmonyPatch(typeof(PlayerMovement), "RecoverFromKnockout")]
    internal static class BreakStunHoldPatch
    {
        private static double _lastLog = double.MinValue;

        /// <summary>
        /// Low priority, like every prefix of ours that can return false: running last
        /// means any other mod's prefix on this method has already had its say, so we
        /// only ever skip the original method, never someone else's patch.
        /// </summary>
        [HarmonyPriority(Priority.Low)]
        private static bool Prefix(PlayerMovement __instance)
        {
            if (!Local.Is(__instance.PlayerInfo)) return true;
            double now = Time.timeAsDouble;
            if (now >= ShieldState.BreakStunUntil) { BreakTrace.Log("RecoverFromKnockout allowed (hold expired)"); return true; }
            if (!__instance.IsKnockedOut) { BreakTrace.Log("RecoverFromKnockout allowed (not knocked out)"); return true; }
            try { if (__instance.PlayerInfo.AsHittable.FrozenState == FrozenState.Frozen) return true; } catch { }
            if (__instance.IsRespawningOrDrowning) return true;

            if (now - _lastLog > 0.5)
            {
                _lastLog = now;
                BreakTrace.Log("RecoverFromKnockout BLOCKED by hold");
            }
            return false;
        }
    }

    /// <summary>
    /// The knockout-immunity bubble the game grants on recovery. After a break
    /// stun (and only then) its duration is ours, scaled by percent. Every other
    /// knockout keeps the vanilla rule, including the orange repeat protection.
    /// </summary>
    [HarmonyPatch(typeof(PlayerMovement), "StartKnockoutImmunity")]
    internal static class BreakImmunityPatch
    {
        private static AccessTools.FieldRef<PlayerMovement, Coroutine> _routine;
        private static AccessTools.FieldRef<PlayerMovement, System.Collections.Generic.List<double>> _recent;

        private static bool Prepare()
        {
            try
            {
                _routine = AccessTools.FieldRefAccess<PlayerMovement, Coroutine>("knockoutImmunityRoutine");
                _recent  = AccessTools.FieldRefAccess<PlayerMovement, System.Collections.Generic.List<double>>("recentKnockoutImmunityTimestamps");
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Knockout immunity fields not found; break immunity stays vanilla. " + e.Message);
                return false;
            }
        }

        [HarmonyPriority(Priority.Low)]
        private static bool Prefix(PlayerMovement __instance, bool fromPlayerAggression)
        {
            if (!fromPlayerAggression || !Local.Is(__instance.PlayerInfo)) return true;
            if (!Plugin.BreakImmunityScalesWithPercent.Value) return true;
            if (!ShieldState.ConsumeBreakStunImmunity()) return true;

            float duration = ShieldState.BreakImmunityDuration;
            if (Plugin.VerboseLogging.Value)
                Plugin.Log.LogInfo($"Break immunity: {duration:0.00}s at {ShieldState.Percent:0}%.");

            try
            {
                // Count it for the game's repeat-hit escalation so vanilla still sees the juggle.
                _recent(__instance)?.Add(Time.timeAsDouble);

                ref var routine = ref _routine(__instance);
                if (routine != null) __instance.StopCoroutine(routine);
                if (duration <= 0.05f)
                {
                    routine = null;
                    __instance.NetworkknockoutImmunityStatus = PlayerMovement.KnockOutImmunity.Reset(__instance.KnockoutImmunityStatus);
                    return false;
                }
                routine = __instance.StartCoroutine(Immunity(__instance, duration));
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Break immunity failed, falling back to vanilla: " + e.Message);
                return true;
            }
        }

        private static System.Collections.IEnumerator Immunity(PlayerMovement m, float duration)
        {
            m.NetworkknockoutImmunityStatus = PlayerMovement.KnockOutImmunity.Get(KnockOutVfxColor.Blue);
            // Same shape as the vanilla routine: hold through the get-up animation,
            // then for `duration` after the player is fully up.
            while (m != null && (m.KnockoutState == KnockoutState.Recovering ||
                   (!m.IsKnockedOutOrRecovering && Time.timeAsDouble - m.IsKnockedOutTimestamp < duration)))
                yield return null;
            if (m != null)
                m.NetworkknockoutImmunityStatus = PlayerMovement.KnockOutImmunity.Reset(m.KnockoutImmunityStatus);
        }
    }

    /// <summary>
    /// The game applies the full knockback before we get a say. Apply the
    /// correction on the next physics step, before integration.
    /// </summary>
    [HarmonyPatch(typeof(PlayerMovement), "FixedUpdate")]
    internal static class VelocityCorrectionPatch
    {
        private static AccessTools.FieldRef<PlayerMovement, Rigidbody> _rigidbody;

        private static bool Prepare()
        {
            try
            {
                _rigidbody = AccessTools.FieldRefAccess<PlayerMovement, Rigidbody>("rigidbody");
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("rigidbody field not found; knockback scaling disabled. " + e.Message);
                return false;
            }
        }

        private static void Prefix(PlayerMovement __instance)
        {
            if (!Local.Is(__instance.PlayerInfo)) return;
            var rb = _rigidbody(__instance);
            if (rb == null || rb.isKinematic) return;

            if (ShieldState.HasPendingVelocityCorrection)
            {
                ShieldState.HasPendingVelocityCorrection = false;
                rb.linearVelocity += ShieldState.PendingVelocityCorrection;
                ShieldState.PendingVelocityCorrection = Vector3.zero;
            }

            Launch.Tick(__instance, rb);

            // S-curve: strong speed-proportional drag right after a launch, so the
            // burst is fast and the tail is floaty. Only while tumbling in the air.
            // Drag only bites on speed ABOVE a threshold. The old version scaled all
            // horizontal speed down every step, which over the drag window removed
            // ~85% of it: launches went up, stopped dead sideways, and floated down.
            // Now a launch keeps its travel and only the extreme burst is tamed.
            if (Time.timeAsDouble < ShieldState.LaunchDragUntil && __instance.IsKnockedOutOrRecovering && !__instance.IsGrounded)
            {
                var v = rb.linearVelocity;
                float floor = Plugin.LaunchDragAboveSpeed.Value;
                float k = Plugin.LaunchDrag.Value * Time.fixedDeltaTime;

                var h = new Vector2(v.x, v.z);
                float hMag = h.magnitude;
                if (hMag > floor)
                {
                    float excess = hMag - floor;
                    excess *= 1f - Mathf.Clamp01(k);
                    h = h / hMag * (floor + excess);
                    v.x = h.x; v.z = h.y;
                }
                if (v.y > floor)
                {
                    float excess = v.y - floor;
                    excess *= 1f - Mathf.Clamp01(k * Plugin.LaunchVerticalDragFactor.Value);
                    v.y = floor + excess;
                }
                rb.linearVelocity = v;
            }

            // Hang time. Without this a launch is over about as fast as a vanilla hit,
            // because the same gravity is pulling on a body that went higher. Gravity is
            // scaled down near the top of the arc -- where vertical speed is smallest --
            // so the rise and fall keep their shape and only the apex stretches. Scales
            // with percent, so it is the big hits that float.
            // Hang time only on cloud hits, and off by default now: on ordinary hits it read
            // as the body slowing down mid-flight and speeding up again, which is exactly
            // what it is. Keep it for the big launches if it earns its place there.
            if (Plugin.LaunchHangTime.Value > 0f && Launch.IsCloudHit && ShieldState.LaunchHangUntil > Time.timeAsDouble &&
                __instance.IsKnockedOutOrRecovering && !__instance.IsGrounded)
            {
                var v = rb.linearVelocity;
                float apex = 1f - Mathf.Clamp01(Mathf.Abs(v.y) / Mathf.Max(1f, Plugin.LaunchHangWindow.Value));
                if (apex > 0f)
                {
                    float relief = Plugin.LaunchHangTime.Value * apex * ShieldState.LaunchHangScale;
                    // Cancel part of the gravity Unity is about to apply this step.
                    rb.linearVelocity = v - Physics.gravity * relief * Time.fixedDeltaTime;
                }
            }
        }
    }

    /// <summary>
    /// Every shield hit, including server-side reflections we never see a
    /// TryKnockOut for, ends up here on the owner's client. If nothing charged
    /// the shield in the last moment, this is a reflection: identify what bounced
    /// by looking at what is touching the shield, and charge for it.
    /// </summary>
    [HarmonyPatch(typeof(PlayerInfo), "PlayElectromagnetShieldHitInternal")]
    internal static class ReflectionChargePatch
    {
        private static readonly Collider[] _buffer = new Collider[32];

        // Everything we look for here is a ball, a rocket or a freeze bomb, and those
        // all live on hittable/ball layers. Querying every layer (~0) meant walking
        // terrain, foliage and trigger volumes on every single shield hit.
        private static int _mask;
        private static bool _maskReady;

        private static int Mask()
        {
            if (_maskReady) return _mask;
            try
            {
                var l = GameManager.LayerSettings;
                _mask = l.HittablesMask | l.BallMask | l.DynamicBallMask | l.ProjectileHittablesMask;
                _maskReady = true;
                if (Plugin.VerboseLogging.Value) Plugin.Log.LogInfo($"Reflection search mask: {_mask:X}");
            }
            catch (Exception e)
            {
                _mask = ~0;   // fall back to everything rather than missing reflections
                _maskReady = true;
                Plugin.Log.LogWarning("Layer masks unavailable; reflection search will scan all layers. " + e.Message);
            }
            return _mask;
        }

        private static void Postfix(PlayerInfo __instance, bool isExplosion)
        {
            if (isExplosion || !Local.Is(__instance) || !__instance.IsElectromagnetShieldActive) return;
            if (Time.timeAsDouble - ShieldState.LastKnockoutChargeTime < 0.2) return; // already charged by TryKnockOut

            var col = __instance.ElectromagnetShieldCollider;
            if (col == null) return;

            float radius = col.radius * Mathf.Max(col.transform.lossyScale.x, col.transform.lossyScale.y, col.transform.lossyScale.z);
            int n = Physics.OverlapSphereNonAlloc(col.transform.position, radius + Plugin.ReflectionSearchMargin.Value,
                _buffer, Mask(), QueryTriggerInteraction.Collide);

            int    cost = 1;
            string what = "unknown projectile";
            bool   found = false;

            for (int i = 0; i < n && !found; i++)
            {
                var c = _buffer[i];
                if (c == null) continue;

                if (c.GetComponentInParent<Rocket>() != null)
                {
                    cost = ShieldState.CostFullBreak; what = "rocket"; found = true; break;
                }
                if (c.GetComponentInParent<FreezeBomb>() != null)
                {
                    cost = ShieldState.CostFullBreak; what = "freeze bomb"; found = true; break;
                }
                var h = c.GetComponentInParent<Hittable>();
                if (h != null && h.AsEntity != null && !h.AsEntity.IsPlayer && h.SwingProjectileState != SwingProjectileState.None)
                {
                    bool targeted = false;
                    try { targeted = h.NetworkhomingTargetHittable != null && ReferenceEquals(h.NetworkhomingTargetHittable, __instance.AsHittable); }
                    catch { }
                    cost = targeted ? ShieldState.CostFullBreak : 1;
                    what = targeted ? "targeted ball" : "ball";
                    found = true;
                    break;
                }
            }

            ShieldState.ChargeReflection(__instance, cost, what);
        }
    }

    /// <summary>Halve percent on respawn. No pip restore.</summary>
    [HarmonyPatch(typeof(PlayerMovement), "LocalPlayerBeginRespawn")]
    internal static class RespawnPercentPatch
    {
        private static void Postfix(PlayerMovement __instance)
        {
            if (Local.Is(__instance.PlayerInfo)) ShieldState.OnRespawn();
        }
    }

    /// <summary>
    /// No aiming while the shield is up. Blocked at the input flag rather than at the
    /// aim state itself, so the game runs its own transitions (camera, animation,
    /// railgun reticle) exactly as if you had let go of the button. Swapping weapons
    /// is deliberately still allowed.
    /// </summary>
    [HarmonyPatch(typeof(PlayerInput), "IsHoldingAimSwing", MethodType.Setter)]
    internal static class BlockAimWhileShieldedPatch
    {
        // PlayerInput keeps its PlayerInfo in a private field, not a property.
        private static AccessTools.FieldRef<PlayerInput, PlayerInfo> _playerInfo;

        private static bool Prepare()
        {
            try
            {
                _playerInfo = AccessTools.FieldRefAccess<PlayerInput, PlayerInfo>("playerInfo");
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("PlayerInput.playerInfo not found; aiming will not be blocked while shielded. " + e.Message);
                return false;
            }
        }

        private static void Prefix(PlayerInput __instance, ref bool value)
        {
            if (!value || !Plugin.BlockAimWhileShielded.Value) return;
            try
            {
                if (Local.IsRooted(_playerInfo(__instance))) value = false;
            }
            catch { }
        }
    }

    /// <summary>
    /// No item use while the shield is up: the shield is a commitment, not a stance
    /// you act out of. Covers both public entry points, including spring boots.
    /// </summary>
    [HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.TryUseItem))]
    internal static class BlockItemUseWhileShieldedPatch
    {
        [HarmonyPriority(Priority.Low)]   // can skip the original
        private static bool Prefix(PlayerInventory __instance, ref bool shouldEatInput, ref bool __result)
        {
            if (!Plugin.BlockItemUseWhileShielded.Value) return true;
            if (!Local.IsRooted(__instance.PlayerInfo)) return true;
            shouldEatInput = true;   // swallow it, so it does not fire the instant the shield drops
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(PlayerInventory), nameof(PlayerInventory.TryUseSpringBoots))]
    internal static class BlockSpringBootsWhileShieldedPatch
    {
        [HarmonyPriority(Priority.Low)]   // can skip the original
        private static bool Prefix(PlayerInventory __instance, ref bool __result)
        {
            if (!Plugin.BlockItemUseWhileShielded.Value) return true;
            if (!Local.IsRooted(__instance.PlayerInfo)) return true;
            __result = false;
            return false;
        }
    }

    /// <summary>
    /// THE fix for "the comeback bubble cancels my break stun". It never cancelled
    /// anything: TryKnockOut's inner CanKnockOut passes suppressKnockoutImmunity:false
    /// unconditionally, so with the blue or gold bubble up the knockout is refused
    /// outright and no stun is ever created. Breaking someone's shield should stun
    /// them regardless of comeback protection, so while our own break is resolving we
    /// flip that argument. Team protection, frozen and self-hit checks are untouched.
    /// </summary>
    [HarmonyPatch(typeof(PlayerMovement), "CanBeKnockedOutBy")]
    internal static class BreakBypassesImmunityPatch
    {
        private static void Prefix(PlayerMovement __instance, ref bool suppressKnockoutImmunity)
        {
            if (suppressKnockoutImmunity) return;
            if (!ShieldState.ForcingBreakKnockout) return;
            if (!Local.Is(__instance.PlayerInfo)) return;
            suppressKnockoutImmunity = true;
            if (Plugin.VerboseLogging.Value)
                Plugin.Log.LogInfo("Shield break overriding comeback immunity so the stun actually lands.");
        }
    }

    // =====================================================================
    //  Audio
    // =====================================================================

    /// <summary>
    /// Kills the lowpass snapshot and the looping hum without touching the activation
    /// one-shot. Both are persistent EventInstances on private fields, so we let the
    /// original method run and then stop just those two instances.
    /// </summary>
    [HarmonyPatch(typeof(PlayerAudio), "SetElectromagnetShieldActiveLocalOnly")]
    internal static class SuppressShieldAudioPatch
    {
        private static AccessTools.FieldRef<PlayerAudio, FMOD.Studio.EventInstance> _muffle;
        private static AccessTools.FieldRef<PlayerAudio, FMOD.Studio.EventInstance> _hum;

        private static bool Prepare()
        {
            try
            {
                _muffle = AccessTools.FieldRefAccess<PlayerAudio, FMOD.Studio.EventInstance>(
                    "electromagnetShieldMuffleSnapshotInstance");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Muffle field not found; muffle will still play. " + e.Message);
            }

            try
            {
                _hum = AccessTools.FieldRefAccess<PlayerAudio, FMOD.Studio.EventInstance>(
                    "electromagnetShieldHumInstance");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Hum field not found; hum will still play. " + e.Message);
            }

            return _muffle != null || _hum != null;
        }

        private static void Postfix(PlayerAudio __instance, bool isActive)
        {
            if (!isActive) return;
            // The vanilla magnet item keeps its stock audio; only our Shift shield is quieted.
            if (!Plugin.WeActivated) return;

            if (Plugin.SuppressMuffle.Value && _muffle != null)
            {
                ref var muffle = ref _muffle(__instance);
                if (muffle.isValid())
                    muffle.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            }

            if (Plugin.SuppressHum.Value && _hum != null)
            {
                ref var hum = ref _hum(__instance);
                if (hum.isValid())
                    hum.stop(FMOD.Studio.STOP_MODE.IMMEDIATE);
            }
        }
    }
}

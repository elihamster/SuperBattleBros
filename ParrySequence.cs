using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace SbgShields
{
    /// <summary>
    /// The parry as a committed animation, the same on every screen. A perfect parry plays:
    ///
    ///   1. IMPACT FRAME (ParryFreeze, ~80 ms). The parrier's body and pose freeze, the bubble
    ///      flares. Nothing can hit the parrier. The attacker does not freeze.
    ///   2. HOLD (ParryHold). The bubble stays up, lit, and the parrier stays rooted.
    ///   3. SNAP OFF. The bubble disappears instantly, no fade, and control returns.
    ///
    /// Meanwhile the parried hit resolves the normal way: a homing item goes back to whoever
    /// fired it (ParryReflect), anything else is absorbed for free, and the attacker is
    /// knocked down for a moment (ParryStun) unless their own item is coming back at them.
    ///
    /// Who decides what: the parrier's machine decides the parry (knockouts are decided by
    /// the victim), freezes its own body, holds and snaps the bubble (a SyncVar every client
    /// sees), and sends one Parry message. Every other machine freezes its copy of the
    /// parrier's pose on that message. The attacker's machine applies the stun to itself,
    /// because only it can knock its own player out.
    /// </summary>
    internal static class ParrySequence
    {
        /// <summary>The local player's sequence: the parry moment and the end of the freeze.</summary>
        private static double _freezeUntil = double.MinValue;

        /// <summary>The local player is in the parry's impact frame: untouchable, body held still.</summary>
        internal static bool LocalFrozen => Time.timeAsDouble < _freezeUntil;

        /// <summary>
        /// Starts the local player's sequence. The bubble hold itself (raise if needed,
        /// rooted, snap off at the end) lives with the rest of the bubble state in Plugin.
        /// </summary>
        internal static void BeginLocal(PlayerInfo player)
        {
            float freeze = Plugin.ParryFreezeSeconds;
            double now = Time.timeAsDouble;
            _freezeUntil = now + freeze;
            Plugin.StartParryHold(player, now + freeze + Plugin.ParryHoldSeconds);
            HitStop.Begin(player, freeze);
        }

        internal static void Clear() { _freezeUntil = double.MinValue; }
    }

    /// <summary>
    /// A brief freeze of one player's pose (every machine) and, for the local player, their
    /// body. Never Time.timeScale: on the host that is the server's clock, and it would pause
    /// the match for everyone. Never a kinematic body: the game skips hits on those.
    /// </summary>
    internal static class HitStop
    {
        private class Frozen { public double Until; public Animator Animator; public float Speed; public Rigidbody Body; }

        private static readonly Dictionary<PlayerInfo, Frozen> _frozen = new Dictionary<PlayerInfo, Frozen>();
        private static readonly List<PlayerInfo> _done = new List<PlayerInfo>();

        private static AccessTools.FieldRef<PlayerAnimatorIo, Animator> _animator;
        private static bool _bound, _bindFailed;

        private static Animator AnimatorOf(PlayerInfo p)
        {
            if (!_bound)
            {
                _bound = true;
                try { _animator = AccessTools.FieldRefAccess<PlayerAnimatorIo, Animator>("animator"); }
                catch (Exception e) { _bindFailed = true; Plugin.Log.LogWarning("PlayerAnimatorIo.animator not found; the parry freeze holds the body but not the pose. " + e.Message); }
            }
            if (_bindFailed || p == null || p.AnimatorIo == null) return null;
            try { return _animator(p.AnimatorIo); } catch { return null; }
        }

        internal static bool IsFrozen(PlayerInfo p) =>
            p != null && _frozen.TryGetValue(p, out var f) && Time.timeAsDouble < f.Until;

        internal static void Begin(PlayerInfo p, float seconds)
        {
            if (p == null || seconds <= 0.001f) return;
            double until = Time.timeAsDouble + seconds;
            if (_frozen.TryGetValue(p, out var f))
            {
                if (until > f.Until) f.Until = until;
                return;
            }
            var anim = AnimatorOf(p);
            f = new Frozen { Until = until, Animator = anim, Speed = anim != null ? anim.speed : 1f };
            if (anim != null) anim.speed = 0f;

            // Our own body: lock its position for the freeze. The game only rewrites the
            // player's constraints when the knockout state changes, and a lock stops BOTH
            // ways a hit's knockback arrives (a velocity added now, or an AddForce the engine
            // integrates on the next step), which zeroing the velocity alone cannot.
            if (Local.Is(p))
            {
                try
                {
                    var rb = p.AsHittable != null && p.AsHittable.AsEntity != null ? p.AsHittable.AsEntity.Rigidbody : null;
                    if (rb != null && !rb.isKinematic)
                    {
                        rb.linearVelocity = Vector3.zero;
                        rb.angularVelocity = Vector3.zero;
                        rb.constraints |= RigidbodyConstraints.FreezePosition;
                        f.Body = rb;
                    }
                }
                catch (Exception e) { if (Plugin.VerboseLogging.Value) Plugin.Log.LogWarning("Parry freeze could not lock the body: " + e.Message); }
            }
            _frozen[p] = f;
        }

        private static void Release(Frozen f)
        {
            if (f.Animator != null) f.Animator.speed = f.Speed > 0f ? f.Speed : 1f;
            if (f.Body != null)
            {
                f.Body.constraints &= ~RigidbodyConstraints.FreezePosition;
                f.Body.linearVelocity = Vector3.zero;
            }
        }

        /// <summary>Every frame: let go of poses whose freeze is over.</summary>
        internal static void Tick()
        {
            if (_frozen.Count == 0) return;
            double now = Time.timeAsDouble;
            _done.Clear();
            foreach (var kv in _frozen) if (kv.Key == null || now >= kv.Value.Until) _done.Add(kv.Key);
            foreach (var p in _done)
            {
                if (_frozen.TryGetValue(p, out var f)) Release(f);
                _frozen.Remove(p);
            }
        }

        /// <summary>
        /// Holds the LOCAL frozen body still for this physics step. Called from the
        /// FixedUpdate prefix and postfix: the prefix stops what is there, the postfix stops
        /// the gravity the game's own step just added.
        /// </summary>
        internal static void HoldBody(PlayerMovement mv, Rigidbody rb)
        {
            if (rb == null || rb.isKinematic || !IsFrozen(mv.PlayerInfo)) return;
            rb.linearVelocity  = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        internal static void ClearAll()
        {
            foreach (var kv in _frozen) Release(kv.Value);
            _frozen.Clear();
        }
    }

    /// <summary>
    /// The window in which a parrier's bubble is a real wall again, so a homing item that
    /// reaches it bounces back the game's own way (the projectile retargets whoever fired
    /// it). Armed on release only when a homing item is coming at you. Every machine keeps
    /// the same window from the ParryReflect message, because whichever machine simulates
    /// the projectile is the one that decides the bounce: the host for balls and rockets.
    /// </summary>
    internal static class ParryReflect
    {
        private static readonly Dictionary<PlayerInfo, double> _until = new Dictionary<PlayerInfo, double>();
        private static readonly List<PlayerInfo> _done = new List<PlayerInfo>();

        /// <summary>This player's bubble is up and inside a reflect window: it should bounce homing items.</summary>
        internal static bool IsSolid(PlayerInfo p)
        {
            if (p == null || !_until.TryGetValue(p, out double until) || Time.timeAsDouble >= until) return false;
            try { return p.IsElectromagnetShieldActive; } catch { return false; }
        }

        internal static void Mark(PlayerInfo p, float seconds)
        {
            if (p == null || seconds <= 0f) return;
            _until[p] = Time.timeAsDouble + Mathf.Min(seconds, 2f);
            BubbleColliderPatch.Refresh(p);
        }

        internal static void End(PlayerInfo p)
        {
            if (p == null || !_until.Remove(p)) return;
            BubbleColliderPatch.Refresh(p);
        }

        internal static void Tick()
        {
            if (_until.Count == 0) return;
            double now = Time.timeAsDouble;
            _done.Clear();
            foreach (var kv in _until) if (kv.Key == null || now >= kv.Value) _done.Add(kv.Key);
            foreach (var p in _done)
            {
                _until.Remove(p);
                if (p != null) BubbleColliderPatch.Refresh(p);
            }
        }

        internal static void ClearAll()
        {
            _done.Clear();
            _done.AddRange(_until.Keys);
            _until.Clear();
            foreach (var p in _done) if (p != null) BubbleColliderPatch.Refresh(p);
        }

        /// <summary>
        /// Is a homing item locked on to this player and close enough to matter? Read once
        /// per release, so a scene search is affordable. Rockets carry their homing target
        /// on the Rocket; balls on their Hittable.
        /// </summary>
        internal static bool HomingAt(PlayerInfo player, Vector3 centre, float range, out string what)
        {
            what = null;
            var me = player != null ? player.AsHittable : null;
            if (me == null) return false;
            float r2 = range * range;
            try
            {
                foreach (var rocket in UnityEngine.Object.FindObjectsByType<Rocket>(FindObjectsSortMode.None))
                {
                    if (rocket == null || !ReferenceEquals(rocket.NetworkhomingTargetHittable, me)) continue;
                    if ((rocket.transform.position - centre).sqrMagnitude > r2) continue;
                    what = "homing rocket";
                    return true;
                }
                foreach (var h in UnityEngine.Object.FindObjectsByType<Hittable>(FindObjectsSortMode.None))
                {
                    if (h == null || h.AsEntity == null || h.AsEntity.IsPlayer) continue;
                    if (h.SwingProjectileState == SwingProjectileState.None) continue;
                    if (!ReferenceEquals(h.NetworkhomingTargetHittable, me)) continue;
                    if ((h.transform.position - centre).sqrMagnitude > r2) continue;
                    what = "homing ball";
                    return true;
                }
            }
            catch (Exception e) { if (Plugin.VerboseLogging.Value) Plugin.Log.LogWarning("Homing search failed: " + e.Message); }
            return false;
        }
    }

    /// <summary>
    /// The attacker's side of a parry: told by the parrier's machine, applied here, because
    /// only a player's own game can knock them out. A zero-velocity knockout through the
    /// game's TryKnockOut, so the fall, the stars, the comeback shield afterwards and the
    /// kill feed ("parrier knocked out attacker") are all the game's own.
    /// </summary>
    internal static class ParryStun
    {
        /// <summary>Set while we ask the game for the stun knockout; ResolveKnockout sees it and stays out of the way.</summary>
        internal static bool Requesting;

        internal static void ApplyToLocal(PlayerInfo parrier)
        {
            if (!Plugin.ParryStunsAttacker.Value || !ModHandshake.GameplayEnabled) return;
            var local = GameManager.LocalPlayerInfo;
            var mv = local != null ? local.Movement : null;
            if (mv == null || parrier == null || ReferenceEquals(parrier, local)) return;
            if (mv.IsKnockedOutOrRecovering || mv.IsRespawningOrDrowning) return;

            string who; try { who = parrier.PlayerId.PlayerNameNoRichText; } catch { who = "a player"; }
            Requesting = true;
            try
            {
                bool ok = mv.TryKnockOut(parrier, KnockoutType.ElectromagnetShieldExplosion, false,
                    Vector3.zero, 0f, Vector3.zero, ElectromagnetShieldHitBlockType.FullyBlocked,
                    ItemUseId.Invalid, false, false, out _, out _);
                Plugin.Log.LogInfo(ok
                    ? $"Parried by {who}: stunned for {Plugin.ParryAttackerStun.Value:0.0}s."
                    : $"Parried by {who}: the game refused the stun (comeback shield, team or frozen).");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Parry stun failed: " + e.Message); }
            finally { Requesting = false; }
        }
    }
}

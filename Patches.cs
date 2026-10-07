using System;
using HarmonyLib;
using UnityEngine;

namespace SbgShields
{
    internal static class Local
    {
        internal static bool Is(PlayerInfo p) =>
            p != null && ReferenceEquals(p, GameManager.LocalPlayerInfo);

        /// <summary>
        /// Only OUR Shift bubble roots (the magnet item's shield is left alone), plus the
        /// moment after a tech and the whole parry sequence (freeze and hold).
        /// </summary>
        internal static bool IsRooted(PlayerInfo p) =>
            Is(p) && ((p.IsElectromagnetShieldActive && Plugin.WeActivated) || ShieldState.TechRooted
                      || Plugin.InParrySequence || ParrySequence.LocalFrozen);
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
        [HarmonyPriority(Priority.Low)]   // can skip the original; see RecoveryHoldPatch
        private static bool Prefix(PlayerMovement __instance, PlayerInfo responsiblePlayer, KnockoutType knockoutType,
            Vector3 localOrigin, float distance, Vector3 incomingVelocityChange, ref bool __result, ref bool isNewKnockout,
            ref bool blockedByTeamProtection, out bool __state)
        {
            __state = false;
            if (!Local.Is(__instance.PlayerInfo)) return true;
            __state = true;

            // Your own rocket, your own back-blast: a launch you paid for yourself.
            ShieldState.PendingSelfInflicted = responsiblePlayer != null && ReferenceEquals(responsiblePlayer, __instance.PlayerInfo);

            // The game passes where the hit came from (in our local space) and how far
            // away it was. Explosions use both: direction to make the launch radial from
            // the blast, distance to scale percent and force so a graze is not a direct hit.
            ShieldState.PendingHitDistance    = distance;
            ShieldState.PendingHitLocalOrigin = localOrigin;

            bool proceed;
            try { proceed = ShieldState.ResolveKnockout(__instance.PlayerInfo, responsiblePlayer, knockoutType, incomingVelocityChange); }
            catch (Exception e)
            {
                // This prefix runs inside the game's hit handler (an RPC or a command). An
                // exception escaping it makes Mirror drop the connection, so a bug here
                // must degrade to "vanilla hit", never to a disconnect.
                Plugin.Log.LogError("Hit resolution failed; this hit is vanilla: " + e);
                ShieldState.AbandonThisHit();
                return true;
            }
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
            try { ShieldState.OnKnockoutResolved(__result); }
            catch (Exception e) { Plugin.Log.LogError("Knockout bookkeeping failed: " + e); }
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
                ShieldState.PendingProjectileBall = hitter != null ? hitter.netId : 0u;
            }
            catch { ShieldState.PendingProjectileWasTargeted = false; ShieldState.PendingProjectileBall = 0u; }
        }
    }

    /// <summary>
    /// timeUntilKnockoutRecovery is a flat constant set inside SetKnockOutState(InAir).
    /// The percent layer scales it here on a fresh knockout. The landing stun, the
    /// stun floors and the air hold adjust it later through ClampRecoveryTimer and
    /// SetRecoveryTimer.
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

        /// <summary>Shorten the game's recovery timer, never lengthen it. False if the field is unavailable.</summary>
        internal static bool ClampRecoveryTimer(PlayerMovement mv, float seconds, out float before)
        {
            before = -1f;
            if (_timeUntilRecovery == null || mv == null) return false;
            ref float t = ref _timeUntilRecovery(mv);
            before = t;
            if (t > seconds) t = seconds;
            return true;
        }

        /// <summary>Set the game's recovery timer outright. False if the field is unavailable.</summary>
        internal static bool SetRecoveryTimer(PlayerMovement mv, float seconds)
        {
            if (_timeUntilRecovery == null || mv == null) return false;
            _timeUntilRecovery(mv) = seconds;
            return true;
        }

        /// <summary>The game's recovery timer, or +infinity if the field is unavailable.</summary>
        internal static float RecoveryTimer(PlayerMovement mv) =>
            _timeUntilRecovery == null || mv == null ? float.PositiveInfinity : _timeUntilRecovery(mv);

        /// <summary>
        /// True while the game is inside SetKnockOutState. StartKnockoutImmunity is called
        /// from there (recoveries) and also from the dive-spam and thaw paths; only the
        /// first is ours to shorten (TechImmunityPatch).
        /// </summary>
        internal static int SettingKnockoutState;

        private static void Prefix(PlayerMovement __instance, out bool __state)
        {
            // Was the player already knocked out before this call? The game only
            // resets the recovery timer on the None -> knocked-out transition, and
            // this setter is called every physics frame while airborne.
            __state = __instance.IsKnockedOutOrRecovering;
            SettingKnockoutState++;
        }

        private static Exception Finalizer(Exception __exception)
        {
            if (SettingKnockoutState > 0) SettingKnockoutState--;
            return __exception;
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

            ShieldState.CurrentKnockoutIsBreak = ShieldState.PendingIsBreak;
            ShieldState.CurrentKnockoutSelfInflicted = ShieldState.PendingSelfInflicted;
            // Only an explicit multiplier from ResolveKnockout scales the stun. Every
            // path that leaves it unset (standing down, outside a hole, percent off, the
            // magnet item, a knockout we did not see) is meant to be the game's own stun;
            // it used to fall back to the percent curve, so a percent left over from the
            // last match shortened knockouts in the range.
            if (ShieldState.PendingHitstunMultiplier >= 0f)
                t *= ShieldState.PendingHitstunMultiplier;
            if (ShieldState.PendingIsBreak) BreakTrace.Log($"break stun applied: timer={t:0.00}s (x{ShieldState.PendingHitstunMultiplier:0.00})");

            ShieldState.PendingIsBreak           = false;
            ShieldState.PendingHitstunMultiplier = -1f;

            if (Plugin.VerboseLogging.Value && Mathf.Abs(before - t) > 0.01f)
                Plugin.Log.LogInfo($"Hitstun {before:0.00}s -> {t:0.00}s");
        }

        private static bool _loggedTimeouts;
    }

    /// <summary>
    /// Always-on trace of what the game does after a bubble break. Fires only for a
    /// few seconds after a break, so it costs nothing the rest of the time, and it is
    /// the thing to paste when a break does not bounce or does not stun.
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
            Plugin.Log.LogInfo($"[break +{Time.timeAsDouble - _start:0.00}s] {what} | state={st}");
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

    /// <summary>The tumbling body touched down: the tech window closes here.</summary>
    [HarmonyPatch(typeof(PlayerMovement), "SetKnockOutState")]
    internal static class TumbleLandingPatch
    {
        private static void Prefix(PlayerMovement __instance, out KnockoutState __state) => __state = __instance.KnockoutState;

        private static void Postfix(PlayerMovement __instance, KnockoutState __state)
        {
            if (__state != KnockoutState.InAir || __instance.KnockoutState != KnockoutState.OnGround) return;
            if (!Local.Is(__instance.PlayerInfo)) return;
            ShieldState.OnTumbleLanded(__instance);
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

    /// <summary>
    /// The death launch never wakes up. KillZone fires at the apex and needs the body
    /// still knocked out to get there; if the stun timer ran out first (it does -- the
    /// kill boost makes the rise far longer than any stun) the game recovered you
    /// mid-air, KillZone saw a player who was no longer knocked out, and disarmed. That
    /// was "you don't die at 250%". The game's 10 s time-out is held too, because the
    /// rise can be long. The air hold does not live here any more: it pins the timer
    /// instead (AirHold), so the game's own ground check keeps running.
    /// </summary>
    [HarmonyPatch(typeof(PlayerMovement), "RecoverFromKnockout")]
    internal static class RecoveryHoldPatch
    {
        private static double _loggedFor = double.MinValue;

        /// <summary>
        /// Low priority, like every prefix of ours that can return false: running last
        /// means any other mod's prefix on this method has already had its say, so we
        /// only ever skip the original method, never someone else's patch.
        /// </summary>
        [HarmonyPriority(Priority.Low)]
        private static bool Prefix(PlayerMovement __instance)
        {
            if (!Local.Is(__instance.PlayerInfo)) return true;
            if (!__instance.IsKnockedOut) return true;
            if (!KillZone.IsArmed || !ModHandshake.GameplayEnabled) return true;
            try { if (__instance.PlayerInfo.AsHittable.FrozenState == FrozenState.Frozen) return true; } catch { }
            if (__instance.IsRespawningOrDrowning) return true;

            // One line per death launch, not one every half second.
            if (_loggedFor != __instance.IsKnockedOutTimestamp)
            {
                _loggedFor = __instance.IsKnockedOutTimestamp;
                Plugin.Log.LogInfo("Death launch: holding the knockout until the apex.");
            }
            return false;
        }
    }

    /// <summary>
    /// The knockout timer ran out while you were still in the air.
    ///
    /// Vanilla recovers you on the spot: RecoverFromKnockout's ShouldRecoverInstantly is
    /// true for any state that is not OnGround, so the state flips straight to None with
    /// no landing check. Two things happen on that flip. SetKnockOutState starts the
    /// comeback bubble, and UpdatePhysicsParameters stops using KnockOutGravityFactor:
    /// the body stops falling like a body and floats down at walking-state gravity.
    ///
    /// The hold PINS the game's recovery timer just above zero while you are in the air
    /// (from the FixedUpdate prefix, before UpdateKnockOutState runs). The game then
    /// keeps running its own ground check every step; it is the only place OnGround is
    /// ever set. The first hold version skipped RecoverFromKnockout instead, which also
    /// skipped that check: the landing was never seen, so no tech, no landing stun and
    /// no get-up animation, just a snap upright when the 3 s cap ran out.
    ///
    /// From the player's chair: the comeback bubble appears mid-air when the stun runs
    /// out and you cannot be juggled, you keep tumbling and fall at knockout gravity,
    /// and on landing the landing stun (or a tech) and the normal get-up play.
    /// StayDownMaxTime caps the hold for a body that never finds ground; after it the
    /// timer is let go and vanilla recovers you where you are.
    /// </summary>
    internal static class AirHold
    {
        private static AccessTools.FieldRef<PlayerMovement, Coroutine> _routine;
        private static bool _bound, _bindFailed;

        /// <summary>IsKnockedOutTimestamp of the knockout the hold belongs to. A new knockout has a new stamp.</summary>
        private static double _holdFor = double.MinValue;
        private static bool   _holding;
        private static double _holdingSince;

        internal static bool IsHolding => _holding;

        /// <summary>
        /// Every physics step, from the FixedUpdate prefix, before UpdateKnockOutState.
        /// Starts, keeps or ends the hold for the local player.
        /// </summary>
        internal static void Tick(PlayerMovement mv)
        {
            // A hold that belongs to an earlier knockout (respawn, teleport, elimination
            // and fog all end a knockout without RecoverFromKnockout) ends here.
            if (_holding && (!mv.IsKnockedOut || _holdFor != mv.IsKnockedOutTimestamp)) Released(mv, mv.IsKnockedOut ? "new knockout" : "ended");

            if (mv.KnockoutState != KnockoutState.InAir) return;
            if (!Plugin.StayDownUntilLanding.Value || !ModHandshake.GameplayEnabled || KillZone.IsArmed || ShieldState.CurrentKnockoutIsParryStun) { Released(mv, "not holding"); return; }
            try { if (mv.PlayerInfo.AsHittable.FrozenState == FrozenState.Frozen) { Released(mv, "frozen"); return; } } catch { }
            if (mv.IsRespawningOrDrowning) { Released(mv, "respawning"); return; }

            double now = Time.timeAsDouble;
            float timeout = 10f;
            try { timeout = GameManager.PlayerMovementSettings.KnockoutTimeOutDuration; } catch { }
            if (now - mv.IsKnockedOutTimestamp >= timeout) { Released(mv, "knockout timed out"); return; }

            // Our own, shorter cap: a body that has not found ground this long after its
            // stun ended is stuck on something, and the game's 10 s is too long to wait.
            if (_holding && now - _holdingSince >= Mathf.Max(0.5f, Plugin.StayDownMaxTime.Value)) { Released(mv, "held long enough"); return; }
            // Past the cap for THIS knockout: do not start again.
            if (!_holding && _holdFor == mv.IsKnockedOutTimestamp) return;

            float pin = 2f * Time.fixedDeltaTime;
            if (HitstunPatch.RecoveryTimer(mv) > pin) return;   // the stun is still running on its own
            if (!HitstunPatch.SetRecoveryTimer(mv, pin)) return;

            if (!_holding)
            {
                _holding = true;
                _holdFor = mv.IsKnockedOutTimestamp;
                _holdingSince = now;
                Grant(mv);
                Plugin.Log.LogInfo($"Stun ended mid-air at {ShieldState.EffectivePercent:0}% after {now - mv.IsKnockedOutTimestamp:0.00}s: bubble up, staying down until landing.");
            }
        }

        /// <summary>Logs the end of a hold once. Cheap: the flag is false almost always.</summary>
        internal static void Released(PlayerMovement mv, string how)
        {
            if (!_holding) return;
            _holding = false;
            Plugin.Log.LogInfo($"Air hold released ({how}) after {Time.timeAsDouble - _holdingSince:0.00}s.");
        }

        private static bool Bind()
        {
            if (_bound) return !_bindFailed;
            _bound = true;
            try { _routine = AccessTools.FieldRefAccess<PlayerMovement, Coroutine>("knockoutImmunityRoutine"); }
            catch (Exception e)
            {
                _bindFailed = true;
                Plugin.Log.LogWarning("knockoutImmunityRoutine not found; the air hold will keep you down but cannot raise the bubble early. " + e.Message);
            }
            return !_bindFailed;
        }

        private static void Grant(PlayerMovement mv)
        {
            if (!Bind()) return;
            // The game never gives a Jumbo Burger giant knockout immunity (CanHaveKnockoutImmunity).
            try { if (mv.PlayerInfo.IsInJumboBurgerGiantForm) return; } catch { }
            try
            {
                // Same slot the game uses, so its own StartKnockoutImmunity (on landing,
                // or on any direct SetKnockOutState(None)) stops this and takes over.
                ref var routine = ref _routine(mv);
                if (routine != null) mv.StopCoroutine(routine);
                routine = mv.StartCoroutine(Bubble(mv));
            }
            catch (Exception e) { Plugin.Log.LogWarning("Air hold could not raise the bubble: " + e.Message); }
        }

        private static System.Collections.IEnumerator Bubble(PlayerMovement m)
        {
            m.NetworkknockoutImmunityStatus = PlayerMovement.KnockOutImmunity.Get(KnockOutVfxColor.Blue);
            while (m != null && m.IsKnockedOutOrRecovering) yield return null;
            // Still running here means the game never took over (frozen, scored). Clean up.
            if (m != null) m.NetworkknockoutImmunityStatus = PlayerMovement.KnockOutImmunity.Reset(m.KnockoutImmunityStatus);
        }
    }

    /// <summary>
    /// The immunity the game grants on recovery, the blue comeback shield. Ours in
    /// two cases: after a TECH it is TechImmunity (may be zero, not counted toward
    /// the repeat rule); after an ordinary get-up it is RecoveryImmunity, unless the
    /// game's repeat rule would have made this a GOLD shield, in which case vanilla
    /// runs untouched -- that one is the anti-juggle rule and keeps its teeth. With
    /// RecoveryImmunity 0 every non-tech recovery is vanilla.
    /// </summary>
    [HarmonyPatch(typeof(PlayerMovement), "StartKnockoutImmunity")]
    internal static class TechImmunityPatch
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
                Plugin.Log.LogWarning("Knockout immunity fields not found; recoveries get the vanilla bubble. " + e.Message);
                return false;
            }
        }

        [HarmonyPriority(Priority.Low)]
        private static bool Prefix(PlayerMovement __instance, bool fromPlayerAggression)
        {
            if (!fromPlayerAggression || !Local.Is(__instance.PlayerInfo)) return true;
            // Standing down means vanilla: the lobby's own recovery protection.
            if (!ModHandshake.GameplayEnabled) { ShieldState.ConsumeTechRecovery(); return true; }
            // A giant gets no immunity at all (the game's CanHaveKnockoutImmunity); let it say so.
            try { if (__instance.PlayerInfo.IsInJumboBurgerGiantForm) { ShieldState.ConsumeTechRecovery(); return true; } } catch { }
            // Only a RECOVERY is ours to shorten. The game also calls this for the third
            // dive hit in a row and on thawing from a freeze bomb; those keep vanilla's
            // protection. Recoveries come from inside SetKnockOutState.
            if (HitstunPatch.SettingKnockoutState <= 0) return true;

            float duration;
            if (ShieldState.ConsumeTechRecovery())
            {
                duration = Mathf.Max(0f, Plugin.TechImmunity.Value);
            }
            else
            {
                duration = Plugin.RecoveryImmunity.Value;
                if (duration <= 0f) return true;   // the game's rule
                try
                {
                    // Would vanilla go gold? Same arithmetic it uses: prune the window, count, compare.
                    var list = _recent(__instance);
                    var s = GameManager.PlayerMovementSettings;
                    double now = Time.timeAsDouble;
                    if (list != null)
                    {
                        for (int i = list.Count - 1; i >= 0; i--)
                            if (now - list[i] > s.KnockoutImmunityLongDurationKnockoutTimeWindow) list.RemoveAt(i);
                        bool repeatRule = MatchSetupRules.GetValueAsBool(MatchSetupRules.Rule.RepeatRecoveryProtection);
                        if (repeatRule && list.Count + 1 >= s.MinRecentKnockoutCountForLongImmunityDuration) return true;   // gold: vanilla's
                        list.Add(now);   // ours counts toward the next gold, as vanilla's would
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogWarning("Recovery immunity: could not read the repeat rule, using vanilla. " + e.Message);
                    return true;
                }
            }

            try
            {
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
                Plugin.Log.LogWarning("Tech immunity failed, falling back to vanilla: " + e.Message);
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
    /// The game applies the full knockback right after TryKnockOut, before we get a
    /// say. Apply the correction on the next physics step, before integration. Also
    /// the home of every per-step job for the local player: tech, air hold, launch
    /// (DI, drag, hang).
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

        private static bool _warned;

        /// <summary>The game's step just added gravity; a frozen body still does not move.</summary>
        private static void Postfix(PlayerMovement __instance)
        {
            if (!Local.Is(__instance.PlayerInfo)) return;
            try { HitStop.HoldBody(__instance, _rigidbody(__instance)); } catch { }
        }

        private static void Prefix(PlayerMovement __instance)
        {
            if (!Local.Is(__instance.PlayerInfo)) return;
            // A throw here would skip the player's whole FixedUpdate on every step.
            try { Run(__instance); }
            catch (Exception e)
            {
                if (_warned) return;
                _warned = true;
                Plugin.Log.LogError("Physics step failed (logged once; the game's own step still runs): " + e);
            }
        }

        private static void Run(PlayerMovement __instance)
        {
            var rb = _rigidbody(__instance);
            if (rb == null || rb.isKinematic) return;

            // The parry's impact frame: the body does not move.
            HitStop.HoldBody(__instance, rb);

            // Before the game's own UpdateKnockOutState on this step can start the get-up.
            ShieldState.TryExecuteTech(__instance, rb);
            AirHold.Tick(__instance);

            if (ShieldState.HasPendingVelocityCorrection)
            {
                ShieldState.ConsumeCorrection(out Vector3 correction);
                rb.linearVelocity += correction;
            }

            Launch.Tick(__instance, rb);

            // S-curve: strong speed-proportional drag right after a launch, so the
            // burst is fast and the tail is floaty. Only while tumbling in the air.
            // Drag only bites on speed ABOVE a threshold. The old version scaled all
            // horizontal speed down every step, which over the drag window removed
            // ~85% of it: launches went up, stopped dead sideways, and floated down.
            // Now a launch keeps its travel and only the extreme burst is tamed. Not on
            // the launch's first step: item and ball knockback is an AddForce the engine
            // only integrates after this step, so the velocity here is not the launch yet.
            if (Time.timeAsDouble < ShieldState.LaunchDragUntil && Launch.PastFirstStep && __instance.IsKnockedOutOrRecovering && !__instance.IsGrounded)
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
            // so the rise and fall keep their shape and only the apex stretches. Only on
            // cloud hits (CloudHitMinPercent and up), ramping to full at HangFullPercent,
            // so it is the big hits that float.
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

        /// <summary>Also the base of the parry's threat search (ShieldState.ArmParryOnRelease adds players and carts).</summary>
        internal static int Mask()
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
            // With the bubble a trigger nothing reflects, except a homing item during a
            // parry's reflect window: then this effect IS the reflection, and the parry
            // happens here. Every other shield-hit effect is one the absorb plays itself,
            // and charging for those would bill every absorbed hit twice.
            if (!Plugin.BubbleReflects.Value)
            {
                if (!isExplosion && Local.Is(__instance) && ParryReflect.IsSolid(__instance))
                {
                    try { ShieldState.OnParryReflection(__instance); }
                    catch (Exception e) { Plugin.Log.LogWarning("Parry reflection failed: " + e.Message); }
                }
                return;
            }
            if (isExplosion || !Local.Is(__instance) || !__instance.IsElectromagnetShieldActive) return;
            if (Time.timeAsDouble - ShieldState.LastKnockoutChargeTime < 0.5) return; // already charged by TryKnockOut (the RPC echo can lag a ping)

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

    /// <summary>
    /// A bubble break should be heard across the hole, not just next to it. The break
    /// plays the game's shield-explosion event at the breaker's position, and FMOD
    /// attenuates it by settings baked into the event, which we cannot edit. So on
    /// every client farther away than a few metres, play the same event again at a
    /// point a few metres toward the breaker: same sound, same direction, heard.
    /// Hangs off the game's own replicated hit call, so it reaches every modded
    /// client with no message layer. It also fires for the vanilla magnet item's
    /// explosion, which is the same event and arguably deserves the same treatment.
    /// </summary>
    [HarmonyPatch(typeof(PlayerInfo), "PlayElectromagnetShieldHitInternal")]
    internal static class BreakSoundCarryPatch
    {
        private const float Near = 8f;   // where the re-played copy sits, in metres from the listener

        private static void Postfix(PlayerInfo __instance, bool isExplosion)
        {
            if (!isExplosion || __instance == null) return;
            Vector3 src;
            try { src = __instance.ChestBone != null ? __instance.ChestBone.position : __instance.transform.position; } catch { return; }
            CarryToListener(GameManager.AudioSettings.ElectromagnetShieldExplosionEvent, src, Plugin.BreakSoundCarry.Value, "Break sound carry");
        }

        /// <summary>
        /// Re-play an event a few metres from the listener, in the direction of src, if
        /// src is farther than that and within carry. The event's own copy at src is
        /// left alone; this is the one the listener actually hears. Shared by the break
        /// and the star KO.
        /// </summary>
        internal static void CarryToListener(FMODUnity.EventReference ev, Vector3 src, float carry, string what)
        {
            if (carry <= Near) return;
            try
            {
                var cam = GameManager.Camera;
                if (cam == null) return;
                Vector3 listener = cam.transform.position;
                Vector3 to = src - listener;
                float dist = to.magnitude;
                if (dist <= Near || dist > carry) return;   // close enough to hear it as-is, or out of range
                FMODUnity.RuntimeManager.PlayOneShot(ev, listener + to / dist * Near);
            }
            catch (Exception e) { if (Plugin.VerboseLogging.Value) Plugin.Log.LogWarning(what + ": " + e.Message); }
        }
    }

    /// <summary>
    /// A held bubble absorbs; it does not bounce things back. Vanilla makes the shield
    /// a physical wall: balls, rockets and bombs collide with it and reflect, and gun
    /// rays stop on it. Which machine decides that is whichever one simulates the
    /// projectile, so the only CONSISTENT way to change it is for every client to make
    /// every shield a trigger. Projectiles then pass into the body, the hit arrives as
    /// an ordinary knockout on the victim, and ResolveKnockout absorbs it for pips.
    /// Gun rays ignore triggers and land the same way. This applies to the magnet
    /// item's shield too: from another machine the two cannot be told apart. The
    /// setting has to match across the lobby, which is what config sync is for.
    /// </summary>
    [HarmonyPatch(typeof(PlayerInfo), "OnIsElectromagnetShieldActiveChanged")]
    internal static class BubbleColliderPatch
    {
        private static void Postfix(PlayerInfo __instance)
        {
            Refresh(__instance);
            // The game can take our bubble down by itself (respawn, going invisible). Our
            // linger and hold must not outlive it, or the next shield (the magnet item's)
            // is mistaken for ours.
            try { if (Local.Is(__instance) && !__instance.IsElectromagnetShieldActive) Plugin.NotifyShieldDropped(); } catch { }
        }

        /// <summary>
        /// The one rule for whether a shield is a wall, applied to every player by every
        /// machine: a trigger (absorbs) while the mod runs, a wall when standing down
        /// (vanilla, the magnet item included) or during a parry's reflect window.
        /// </summary>
        internal static void Refresh(PlayerInfo p)
        {
            try
            {
                var col = p != null ? p.ElectromagnetShieldCollider : null;
                if (col == null) return;
                bool trigger = p.IsElectromagnetShieldActive && !Plugin.BubbleReflects.Value && ModHandshake.GameplayEnabled
                               && !ParryReflect.IsSolid(p);
                if (col.isTrigger != trigger) col.isTrigger = trigger;
            }
            catch { }
        }

        /// <summary>Plugin unload: hand every shield back to the game as a wall.</summary>
        internal static void RestoreAll()
        {
            try
            {
                var local = GameManager.LocalPlayerInfo;
                if (local != null && local.ElectromagnetShieldCollider != null) local.ElectromagnetShieldCollider.isTrigger = false;
                var remote = GameManager.RemotePlayers;
                if (remote != null)
                    foreach (var p in remote)
                        if (p != null && p.ElectromagnetShieldCollider != null) p.ElectromagnetShieldCollider.isTrigger = false;
            }
            catch { }
        }
    }

    /// <summary>
    /// Whatever wakes you in mid-air -- the stay-down cap, the game's own time-out,
    /// a direct recovery -- the fall stays a tumble's fall. The game swaps
    /// KnockOutGravityFactor for 1 the moment you are no longer knocked out; this
    /// keeps the knockout factor until the launch that put you up there has landed.
    /// The original slow-float complaint fixed at its root, so the stay-down hold is
    /// no longer the only thing standing between you and a slow descent.
    /// </summary>
    [HarmonyPatch(typeof(PlayerMovement), "UpdatePhysicsParameters")]
    internal static class TumbleGravityPatch
    {
        private static AccessTools.FieldRef<PlayerMovement, float> _gravityFactor;

        private static bool Prepare()
        {
            try { _gravityFactor = AccessTools.FieldRefAccess<PlayerMovement, float>("gravityFactor"); return true; }
            catch (Exception e) { Plugin.Log.LogWarning("gravityFactor not found; a mid-air wake-up falls at walking gravity. " + e.Message); return false; }
        }

        private static void Postfix(PlayerMovement __instance)
        {
            if (!Plugin.TumbleGravityUntilLanding.Value || !Launch.Active) return;
            if (!Local.Is(__instance.PlayerInfo)) return;
            if (__instance.IsKnockedOutOrRecovering || __instance.IsGrounded) return;
            try { _gravityFactor(__instance) = GameManager.PlayerMovementSettings.KnockOutGravityFactor; } catch { }
        }
    }

    /// <summary>
    /// The instant-kill items go straight through the bubble. The game builds a
    /// "protective state" for every item hit from the victim's shield SyncVar, and on
    /// the HOST turns it into an elimination reason: with the shield flag set, a laser
    /// or lightning strike or railgun becomes a shield hit that depletes the shield
    /// and knocks out, never eliminates. So a bubble was a free life against the
    /// three hits that are supposed to be certain death. This strips the flag for
    /// those three on the host, before the reason is chosen. The victim's own client
    /// still sees a shield-variant knockout, which the cost table already treats as
    /// unblockable (bubble drops, hit lands); the elimination the host sends is what
    /// actually happens. Peripheral hits keep their normal cost.
    /// </summary>
    [HarmonyPatch(typeof(PlayerGolfer), "OnServerWasHitByItem")]
    internal static class InstantKillIgnoresBubblePatch
    {
        private static void Prefix(PlayerGolfer __instance, ItemType itemType, ref ProtectiveState protectiveState)
        {
            if (!ModHandshake.GameplayEnabled) return;
            if (itemType != ItemType.OrbitalLaser && itemType != ItemType.Thunderstorm && itemType != ItemType.Railgun) return;
            if ((protectiveState & ProtectiveState.ElectromagnetShield) == 0) return;
            // The parry's impact frame is untouchable, instant kills included. The host knows
            // about the freeze from the Parry message every machine receives.
            try { if (__instance != null && HitStop.IsFrozen(__instance.PlayerInfo)) return; } catch { }
            protectiveState &= ~ProtectiveState.ElectromagnetShield;
            Plugin.Log.LogInfo($"{itemType} hit a bubbled player: bubble ignored, elimination rules apply.");
        }
    }

    /// <summary>
    /// Turning giant cancels the shield with the game's EXPLODE path, which hits everyone
    /// within 7 m. With the Shift bubble up while eating a Jumbo Burger, that blew up your
    /// friends. Drop our bubble quietly first, so the game finds nothing to explode. The
    /// magnet item's own shield is vanilla and is left to explode as the game intends.
    /// Raising the bubble while giant is refused (Plugin.IsGiant).
    /// </summary>
    [HarmonyPatch(typeof(PlayerInfo), nameof(PlayerInfo.LocalPlayerActivateJumboBurgerGiantForm))]
    internal static class GiantFormDropsBubblePatch
    {
        private static void Prefix(PlayerInfo __instance)
        {
            if (!Local.Is(__instance) || !__instance.IsElectromagnetShieldActive) return;
            if (!Plugin.WeActivated && !Plugin.ShieldLingering) return;
            Plugin.ForceDrop("giant form");
        }
    }

    /// <summary>
    /// The magnet item raised while our bubble is up or lingering. The game would only
    /// refresh the existing shield, which keeps our placeholder item id and dies when the
    /// linger ends: the item was spent and its 7 s shield vanished. Hand the shield over
    /// cleanly: drop ours (no parry, no linger), then let the item raise a fresh one with
    /// its own id and its own timer.
    /// </summary>
    /// <summary>
    /// A flash camera that blinds you also drops your bubble. The game tells only the
    /// blinded player, and only blinds within FlashCameraMaxRange; the same check here.
    /// </summary>
    [HarmonyPatch(typeof(PlayerInfo), "UserCode_RpcInformOfFlashCameraHit__Single")]
    internal static class FlashDropsBubblePatch
    {
        private static void Postfix(PlayerInfo __instance, float distance)
        {
            try
            {
                if (!Plugin.FlashDropsBubble.Value || !Local.Is(__instance)) return;
                if (distance > GameManager.ItemSettings.FlashCameraMaxRange) return;
                if (ParrySequence.LocalFrozen) return;   // nothing lands during the parry freeze
                if (!(Plugin.WeActivated || Plugin.ShieldLingering)) return;   // only the Shift bubble; the magnet item is the game's
                Plugin.ForceDrop("flashed by a camera");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Flash drop failed: " + e.Message); }
        }
    }

    [HarmonyPatch(typeof(PlayerInfo), nameof(PlayerInfo.LocalPlayerActivateElectromagnetShield))]
    internal static class ItemShieldTakesOverPatch
    {
        private static void Prefix(PlayerInfo __instance)
        {
            if (Plugin.ActivatingOurs || !Local.Is(__instance) || !__instance.IsElectromagnetShieldActive) return;
            if (!Plugin.WeActivated && !Plugin.ShieldLingering) return;
            Plugin.ForceDrop("magnet item used");
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
            // The vanilla magnet item keeps its stock audio; only our Shift shield (and a
            // parry bringing it back for its sequence) is quieted.
            if (!Plugin.WeActivated && !Plugin.ActivatingOurs) return;

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

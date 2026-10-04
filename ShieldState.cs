using System;
using FMODUnity;
using HarmonyLib;
using UnityEngine;

namespace SbgShields
{
    /// <summary>
    /// All shield economy state for the LOCAL player. Knockouts in this game are
    /// victim-authoritative (TryKnockOut / CanBeKnockedOutBy run on the victim's
    /// own client), so pips, percent and hitstun all live here and are decided on
    /// the victim's machine. Other players learn the visible parts (percent, pips,
    /// parries, star KOs) through SbgNet. Every player runs this for themselves.
    /// </summary>
    internal static class ShieldState
    {
        // Sentinel costs. Positive ints are chip costs in pips.
        internal const int CostFullBreak   = 1000;
        internal const int CostUnblockable = -1;
        /// <summary>The bubble is not consulted: no pips, no parry, no break. The hit lands as if there were no bubble.</summary>
        internal const int CostBypass      = -2;

        // ---- Live state -----------------------------------------------------

        internal static int    Pips;
        internal static float  Percent;

        /// <summary>0..1, how much bubble is left. The HUD, the bubble's own brightness and the wire all read this.</summary>
        internal static float PipFraction => Mathf.Clamp01(Pips / (float)Mathf.Max(1, Plugin.MaxPips.Value));

        /// <summary>When pips last went down, and which HUD circle took it (index, two pips per circle). Drives the loss flash.</summary>
        internal static double LastPipLossAt = double.MinValue;
        internal static int    LastPipLossDot;

        private static void LosePips(int n)
        {
            if (n <= 0) return;
            int before = Pips;
            Pips = Mathf.Max(0, Pips - n);
            LastPipLossAt  = Time.timeAsDouble;
            LastPipLossDot = Pips / 2;
            // Always on: the one line that says what the circles should be showing.
            Plugin.Log.LogInfo($"Bubble: {before} -> {Pips} pips (-{n}); circles {Pips / 2} full{(Pips % 2 == 1 ? " + a half" : "")}.");
        }

        internal static double UseCooldownUntil   = double.MinValue;

        /// <summary>When the bubble was last deliberately released (key up). The parry log reports the parry's delay from here.</summary>
        internal static double LoweredAt = double.MinValue;

        /// <summary>When the last perfect parry landed, for anything that wants to react to it.</summary>
        internal static double LastParryAt = double.MinValue;
        internal static double BreakCooldownUntil = double.MinValue;

        /// <summary>Fired with the new percent whenever it goes up. HUD uses this for the shake.</summary>
        internal static event Action<float> PercentIncreased;

        // ---- Pending per-hit data, consumed by the PlayerMovement patches -----

        /// <summary>
        /// Velocity to add on the next FixedUpdate, correcting the knockback the game
        /// applies. An ACCUMULATOR: every hit resolved before the next physics step adds
        /// its own delta, and only the FixedUpdate consumer (and ClearPending) empties
        /// it. It used to be a single slot, so a second hit inside one step (a rocket
        /// volley, a blast and its cart) wiped the first hit's correction.
        /// </summary>
        internal static Vector3 PendingVelocityCorrection;
        internal static bool    HasPendingVelocityCorrection;

        /// <summary>What the hit being resolved added to the accumulator, so a refusal can take back exactly that.</summary>
        private static Vector3 _thisHitCorrection;

        private static void AddCorrection(Vector3 delta)
        {
            PendingVelocityCorrection   += delta;
            _thisHitCorrection          += delta;
            HasPendingVelocityCorrection = PendingVelocityCorrection.sqrMagnitude > 1e-6f;
        }

        /// <summary>Called by the FixedUpdate consumer once the correction is on the body.</summary>
        internal static void ConsumeCorrection(out Vector3 correction)
        {
            correction = PendingVelocityCorrection;
            PendingVelocityCorrection    = Vector3.zero;
            HasPendingVelocityCorrection = false;
        }

        /// <summary>Applied in SetKnockOutState(InAir) to the game's knockout timer. Negative = none.</summary>
        internal static float PendingHitstunMultiplier = -1f;

        /// <summary>Set by the swing-projectile handler right before TryKnockOut so we can tell targeted from untargeted balls.</summary>
        internal static bool PendingProjectileWasTargeted;

        /// <summary>Percent to add if the knockout actually goes through (checked in the postfix).</summary>
        internal static float PendingPercentGain;

        /// <summary>Last time TryKnockOut charged the shield. Reflection notifications within a short window of this are duplicates.</summary>
        internal static double LastKnockoutChargeTime = double.MinValue;

        // ---- Break -----------------------------------------------------------

        /// <summary>The knockout being resolved is a bubble break: HitstunPatch lengthens its stun and marks it un-techable.</summary>
        internal static bool PendingIsBreak;

        /// <summary>The knockout currently running began as a break. Set by HitstunPatch on the fresh knockout.</summary>
        internal static bool CurrentKnockoutIsBreak;

        // ---- Refund on a refused knockout ------------------------------------
        // We have to spend the shield in the PREFIX: while it is up the game's own
        // CanBeKnockedOutBy returns false for every FullyBlocked hit, so the shield
        // must be down before the original method runs. But the game can still refuse
        // the knockout afterwards (comeback immunity, team protection, frozen, self
        // hit) -- and then the shield was spent for nothing and no stun ever started.
        // So we snapshot the cost here and hand it back in the postfix.

        private static bool   _awaitingResult;

        /// <summary>
        /// True only for the moment between our shield breaking and the game deciding
        /// the knockout. BreakBypassesImmunityPatch reads it to force the stun through
        /// comeback protection. Always cleared in OnKnockoutResolved.
        /// </summary>
        internal static bool ForcingBreakKnockout;
        private static int    _pipsBeforeHit;
        private static double _breakCdBeforeHit, _useCdBeforeHit;
        private static bool   _shieldWasUpBeforeHit;

        private static void SnapshotForRefund(bool shieldWasUp)
        {
            _pipsBeforeHit        = Pips;
            _breakCdBeforeHit     = BreakCooldownUntil;
            _useCdBeforeHit       = UseCooldownUntil;
            _shieldWasUpBeforeHit = shieldWasUp;
        }

        private static void RefundRefusedHit()
        {
            if (!_shieldWasUpBeforeHit) return;
            if (Pips == _pipsBeforeHit && BreakCooldownUntil == _breakCdBeforeHit) return; // nothing was spent

            int spent = _pipsBeforeHit - Pips;
            Pips               = _pipsBeforeHit;
            BreakCooldownUntil = _breakCdBeforeHit;
            UseCooldownUntil   = _useCdBeforeHit;
            Plugin.Log.LogInfo($"Knockout refused: refunded {spent} pip(s). The bubble still popped, but it costs you nothing.");
        }

        /// <summary>Set by KillZone right before it triggers the respawn, so OnRespawn does not also halve the (already reset) percent.</summary>
        internal static bool KillZoneDeathPending;

        /// <summary>
        /// What a break does to your body: cancel the hit that broke it, pop straight up
        /// at BreakBounceSpeed, tumble, land, get up. The stun is the game's own timer
        /// times BreakStunMultiplier and cannot be teched. Everything else that punishes
        /// a break is off the body: the boom everyone hears, and BreakCooldown.
        ///
        /// Before 0.7.8 a break was a stun in place, 2.5-3.5 s by percent, with its own
        /// hold on recovery and its own percent-scaled immunity. That was the most
        /// punishing thing in the mod and it read as a freeze, not a hit.
        /// </summary>
        private static void QueueBreakBounce(PlayerInfo player, Vector3 incomingVelocityChange)
        {
            Vector3 current = Vector3.zero;
            try { current = player.Movement.Velocity; } catch { }
            // Next FixedUpdate: rb.velocity += correction. We want exactly (0, bounce, 0).
            // The bounce is ABSOLUTE, so it replaces whatever the accumulator held: at
            // TryKnockOut time the game has not yet added this hit's knockback (it adds
            // it right after, by += or AddForce), so "- incoming - current" lands on the
            // bounce either way.
            Vector3 bounce = Vector3.up * Mathf.Max(0f, Plugin.BreakBounceSpeed.Value) - incomingVelocityChange - current;
            _thisHitCorrection           = bounce - PendingVelocityCorrection;
            PendingVelocityCorrection    = bounce;
            HasPendingVelocityCorrection = true;
            PendingHitstunMultiplier     = Mathf.Max(0.05f, Plugin.BreakStunMultiplier.Value);
            PendingIsBreak               = true;
            // A break is not a percent launch: no hang or drag left over from an earlier hit.
            LaunchHangUntil = double.MinValue;
            LaunchDragUntil = double.MinValue;
            Launch.Begin(isBreakBounce: true);   // the bounce is a launch too: tumble gravity until it lands, but no DI
            _launchBegunThisHit = true;
        }

        // ---- Where the economy is live ---------------------------------------

        /// <summary>
        /// Percent only exists inside a real hole. The driving range, the lobby and
        /// the hole overview are practice space: pips and the bubble still work so
        /// people can test the shield, but nothing accrues and nothing launches.
        /// </summary>
        internal static bool InPlayableHole
        {
            get
            {
                try
                {
                    if (!ModHandshake.GameplayEnabled) return false;   // standing down: vanilla rules
                    if (SingletonBehaviour<DrivingRangeManager>.HasInstance) return false;
                    switch (CourseManager.MatchState)
                    {
                        case MatchState.TeeOff:
                        case MatchState.Ongoing:
                        case MatchState.CountingDownToEnd:
                        case MatchState.Overtime:
                            return true;
                        default:
                            return false;
                    }
                }
                // Cannot read the match state: there is no match. This used to return
                // true, which made the lobby hub a "hole" where percent accrued and showed.
                catch { return false; }
            }
        }

        /// <summary>True in the driving range, where the shield works but percent does not.</summary>
        internal static bool InDrivingRange
        {
            get { try { return SingletonBehaviour<DrivingRangeManager>.HasInstance; } catch { return false; } }
        }

        /// <summary>
        /// The percent that counts right now: zero outside a hole, while standing down,
        /// or with the percent layer off. A percent left over from the last match must
        /// not leak into the range (trails, tech gate, embers) or into a hole after
        /// percent is switched off.
        /// </summary>
        internal static float EffectivePercent => Plugin.PercentEnabled.Value && InPlayableHole ? Percent : 0f;

        // ---- Lifecycle -------------------------------------------------------

        /// <summary>Wipes everything. Used when a match or a practice session starts.</summary>
        internal static void FullReset(string why)
        {
            Pips = Plugin.MaxPips.Value;
            Percent = 0f;
            UseCooldownUntil   = double.MinValue;
            BreakCooldownUntil = double.MinValue;
            KillZoneDeathPending = false;
            _lastRespawnTime = double.MinValue;
            KillZone.Disarm();
            KillZone.CancelLinger();
            Launch.Cancel();
            DisarmParry();
            ClearPending();
            Plugin.Log.LogInfo($"Full reset ({why}): pips={Pips}, percent=0.");
        }

        internal static void ResetForNewHole()
        {
            Pips = Plugin.MaxPips.Value;
            Percent *= 1f - Mathf.Clamp01(Plugin.PercentReductionBetweenHoles.Value);
            UseCooldownUntil   = double.MinValue;
            BreakCooldownUntil = double.MinValue;
            KillZoneDeathPending = false;
            _lastRespawnTime = double.MinValue;
            KillZone.Disarm();
            KillZone.CancelLinger();
            Launch.Cancel();
            DisarmParry();
            ClearPending();
            if (Plugin.VerboseLogging.Value)
                Plugin.Log.LogInfo($"New hole: pips={Pips}, percent={Percent:0}");
        }

        private static double _lastRespawnTime = double.MinValue;

        internal static void OnRespawn()
        {
            double now = Time.timeAsDouble;
            bool fatigued = now - _lastRespawnTime < Plugin.RespawnFatigueWindow.Value;
            _lastRespawnTime = now;
            KillZone.Disarm();
            Launch.Cancel();
            if (KillZoneDeathPending)
            {
                KillZoneDeathPending = false;
                if (Plugin.VerboseLogging.Value) Plugin.Log.LogInfo($"Respawn after star KO: percent is {Percent:0}.");
                return;
            }
            if (fatigued)
            {
                if (Plugin.VerboseLogging.Value) Plugin.Log.LogInfo($"Respawn within fatigue window: percent kept at {Percent:0}.");
                return;
            }
            float before = Percent;
            Percent = Mathf.Max(0f, Percent - Mathf.Max(0f, Plugin.PercentLostOnRespawn.Value));
            if (Plugin.VerboseLogging.Value) Plugin.Log.LogInfo($"Respawn: percent {before:0} -> {Percent:0}.");
            // Deliberately no pip restore: dying does not give you your shield back.
        }

        internal static void Tick()
        {
            double now = Time.timeAsDouble;
            if (Pips <= 0 && Plugin.RestoreAfterBreakCooldown.Value && now >= BreakCooldownUntil && BreakCooldownUntil > double.MinValue)
            {
                Pips = Plugin.MaxPips.Value;
                BreakCooldownUntil = double.MinValue;
                if (Plugin.VerboseLogging.Value) Plugin.Log.LogInfo("Break cooldown over: shield restored.");
            }
        }

        internal static void ClearPending()
        {
            HasPendingVelocityCorrection = false;
            PendingVelocityCorrection = Vector3.zero;
            _thisHitCorrection = Vector3.zero;
            PendingIsBreak           = false;
            PendingHitstunMultiplier = -1f;
            PendingPercentGain = 0f;
            PendingProjectileWasTargeted = false;
            LaunchDragUntil = double.MinValue;
            LaunchHangUntil = double.MinValue;
        }

        // ---- Activation ------------------------------------------------------

        internal static bool IsOnBreakCooldown => Time.timeAsDouble < BreakCooldownUntil;
        internal static bool IsOnUseCooldown   => Time.timeAsDouble < UseCooldownUntil;

        /// <summary>Seconds until the shield can be raised again, 0 if ready.</summary>
        internal static float SecondsUntilReady
        {
            get
            {
                double now = Time.timeAsDouble;
                double until = Math.Max(UseCooldownUntil, BreakCooldownUntil);
                if (Pips <= 0 && !Plugin.RestoreAfterBreakCooldown.Value) return float.PositiveInfinity;
                return (float)Math.Max(0.0, until - now);
            }
        }

        internal static bool CanActivate(out string reason)
        {
            if (Pips <= 0)          { reason = "no pips";         return false; }
            if (IsOnBreakCooldown)  { reason = "break cooldown";  return false; }
            if (IsOnUseCooldown)    { reason = "use cooldown";    return false; }
            reason = null;
            return true;
        }

        /// <summary>Starts the use cooldown. Every raise is a commitment, whether the key let go or the game forced the drop.</summary>
        internal static void OnShieldReleased()
        {
            UseCooldownUntil = Time.timeAsDouble + Plugin.UseCooldown.Value;
        }

        // ---- Cost table ------------------------------------------------------

        private static System.Collections.Generic.Dictionary<KnockoutType, int> _overrides;

        /// <summary>Config was edited in game: re-parse on next use.</summary>
        internal static void InvalidateOverrides() => _overrides = null;

        /// <summary>Parses "[Costs] Overrides", e.g. "Landmine=3, Rocket=4, Swing=full". Called once per edit.</summary>
        private static void EnsureOverrides()
        {
            if (_overrides != null) return;
            _overrides = new System.Collections.Generic.Dictionary<KnockoutType, int>();
            string raw = Plugin.CostOverrides.Value ?? "";
            foreach (var part in raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split('=');
                if (kv.Length != 2) { Plugin.Log.LogWarning($"Bad cost override '{part}'"); continue; }
                if (!Enum.TryParse(kv[0].Trim(), true, out KnockoutType kt)) { Plugin.Log.LogWarning($"Unknown knockout type '{kv[0].Trim()}'"); continue; }
                string v = kv[1].Trim().ToLowerInvariant();
                int cost;
                if (v == "full" || v == "break") cost = CostFullBreak;
                else if (v == "unblockable") cost = CostUnblockable;
                else if (v == "bypass" || v == "through") cost = CostBypass;
                else if (!int.TryParse(v, out cost) || cost < 0) { Plugin.Log.LogWarning($"Bad cost '{kv[1]}' for {kt}"); continue; }
                _overrides[kt] = cost;
            }
            if (_overrides.Count > 0) Plugin.Log.LogInfo($"Cost overrides: {raw}");
        }

        internal static int GetCost(KnockoutType type, bool projectileTargeted)
        {
            EnsureOverrides();
            if (_overrides.TryGetValue(type, out int o)) return o;

            switch (type)
            {
                // Ten pips, drawn as five circles of two. A cost of 1 is HALF a circle.
                //   stray ball 1 (half a circle)   homing ball 2 (one circle)   guns 2
                //   explosions, carts 3 (a circle and a half)
                //   swing = full break             freeze bomb 0
                // These were doubled along with the pip count in 0.7.16 and a rocket took
                // three whole circles; the user's numbers were always in halves.

                // The penalty stroke (your own ball dropped on your head after going out
                // of bounds) is a penalty, not an attack: it goes through the bubble as if
                // it were not there. No pips, no parry, no break.
                case KnockoutType.ReturnedBall:
                    return CostBypass;

                // Guns: one circle each (user, 11 Sep 2026). The elephant gun hits harder
                // (60 m/s against the pistol's 30) but that shows up as percent, not pips.
                case KnockoutType.DuelingPistol:
                case KnockoutType.DeflectedDuelingPistolShot:
                case KnockoutType.ElephantGun:
                case KnockoutType.DeflectedElephantGunShot:
                    return 2;

                // Balls: a homing ball is a bigger hit than a stray one, but it is still a
                // ball. It used to be a full break, which meant any locked-on ball popped
                // a full bubble on contact; that read as the bubble not being there.
                case KnockoutType.SwingProjectile:
                case KnockoutType.ReflectedSwingProjectile:
                case KnockoutType.RocketDriverSwingProjectile:
                    return projectileTargeted ? 2 : 1;

                // Free. A freeze bomb still freezes everyone else in range; the bubble's
                // holder is spared, and that is the whole reward.
                case KnockoutType.FreezeBomb:
                case KnockoutType.ReflectedFreezeBomb:
                    return 0;

                // A circle and a half: explosions (the blast still happens, against
                // everyone else nearby) and the heavy kinetic hits.
                case KnockoutType.Rocket:
                case KnockoutType.ReflectedRocket:
                case KnockoutType.Landmine:
                case KnockoutType.RocketBackBlast:
                case KnockoutType.ThunderstormPeripheralHit:
                case KnockoutType.OrbitalLaserPeripheralHit:
                case KnockoutType.ElectromagnetShieldExplosion:
                case KnockoutType.GolfCart:
                case KnockoutType.TrafficVehicle:
                case KnockoutType.RocketDriverSwing:
                case KnockoutType.RocketDriverSwingPostHitSpin:
                    return 3;

                // Unblockable: the shield drops and you eat the hit.
                case KnockoutType.OrbitalLaserDirectHit:
                case KnockoutType.ThunderstormDirectHit:
                case KnockoutType.RailgunDirectHit:
                case KnockoutType.OrbitalLaserElectromagnetShieldDirectHit:
                case KnockoutType.ThunderstormElectromagnetShieldDirectHit:
                case KnockoutType.RailgunElectromagnetShieldHit:
                    return CostUnblockable;

                // Everything else -- Swing, giant swings and collisions, and any type
                // added later.
                default:
                    return CostFullBreak;
            }
        }

        /// <summary>How far the local player was from the blast, from TryKnockOut.</summary>
        internal static float PendingHitDistance;

        /// <summary>Where the hit came from, in the player's local space, from TryKnockOut.</summary>
        internal static Vector3 PendingHitLocalOrigin;

        /// <summary>Knockout types that come from a blast radius rather than a direct hit.</summary>
        private static bool IsExplosive(KnockoutType t)
        {
            switch (t)
            {
                case KnockoutType.Rocket:
                case KnockoutType.ReflectedRocket:
                case KnockoutType.RocketBackBlast:
                case KnockoutType.Landmine:
                case KnockoutType.ElectromagnetShieldExplosion:
                case KnockoutType.FreezeBomb:
                case KnockoutType.ReflectedFreezeBomb:
                case KnockoutType.OrbitalLaserPeripheralHit:
                case KnockoutType.ThunderstormPeripheralHit:
                    return true;
                default:
                    return false;
            }
        }

        // ---- Perfect parry ---------------------------------------------------

        /// <summary>
        /// A parry is armed by what is NEAR you when you let go, not by when you let go.
        ///
        /// On release we look ParryReach metres past the bubble's edge. A moving
        /// non-player thing arms a PROJECTILE parry: ball, rocket, bomb, cart, anything
        /// the game gives an Entity and a Rigidbody. Another golfer winding up or
        /// mid-swing arms a SWING parry. The arming lasts ParryArmTime, long enough for
        /// what was in reach to arrive. A hit of the armed class inside that time is
        /// free: no pips, no percent, no knockback, and the use cooldown is cleared.
        /// What it buys over a normal absorb is the hits pips cannot cover -- a cart or
        /// a targeted ball would break the shield outright, and a read stops it dead.
        ///
        /// Why this is never luck: releasing with nothing in reach arms nothing, so
        /// tapping the shield cannot fish for a window. Every parry corresponds to a
        /// real object that was within reach at the moment you let go. It can still be
        /// a surprise -- a rocket passing by that was never aimed at you -- but that is
        /// a catch, not a coin flip. Only a deliberate release (the key going up) arms
        /// anything; a bubble that broke or that the game forced down does not.
        /// Teammates arm nothing: the game refuses their hits anyway.
        ///
        /// Nothing here names an item. Entity.IsPlayer and Entity.Rigidbody are the
        /// game's own classification, so an item added in a later update arms a parry
        /// the same way. The swing/projectile split on the receiving side reads the
        /// KnockoutType enum's names, so a new type sorts itself too.
        /// </summary>
        internal static bool GameplayParryEnabled => ModHandshake.GameplayEnabled && Plugin.PerfectParry.Value;

        private static double _parryArmedUntil = double.MinValue;
        private static bool   _parryProjectile, _parrySwing;
        private static string _parryThreat = "";
        private static readonly Collider[] _threatBuffer = new Collider[128];   // the search now reaches 15 m; a busy hole has more than 48 colliders in that
        private static readonly System.Collections.Generic.HashSet<UnityEngine.Object> _threatSeen = new System.Collections.Generic.HashSet<UnityEngine.Object>();
        private static readonly System.Text.StringBuilder _threatText = new System.Text.StringBuilder();
        private static readonly System.Text.StringBuilder _rejectText = new System.Text.StringBuilder();
        private static int _rejectCount;

        private static void Reject(string what)
        {
            if (_rejectCount++ >= 6) return;
            _rejectText.Append(_rejectText.Length > 0 ? ", " : "").Append(what);
        }
        private static bool _loggedShieldRadius;

        /// <summary>Below this a thing is lying there, not coming at you. A resting ball or a placed mine arms nothing.</summary>
        private const float ThreatMinSpeed = 2f;

        /// <summary>Forget any armed parry: a forced drop, a new hole, the player's own knockout.</summary>
        internal static void DisarmParry()
        {
            _parryArmedUntil = double.MinValue;
            _parryProjectile = _parrySwing = false;
            _parryThreat = "";
        }

        /// <summary>Called from Plugin.ReleaseShield on a deliberate release, while the shield collider still exists.</summary>
        internal static void ArmParryOnRelease(PlayerInfo player)
        {
            DisarmParry();
            if (!GameplayParryEnabled || player == null) return;

            try
            {
                Vector3 centre;
                float shieldRadius = 0f;
                var col = player.ElectromagnetShieldCollider;
                if (col != null)
                {
                    centre = col.transform.position;
                    shieldRadius = col.radius * Mathf.Max(col.transform.lossyScale.x, col.transform.lossyScale.y, col.transform.lossyScale.z);
                }
                else centre = player.transform.position + Vector3.up * 0.9f;

                if (!_loggedShieldRadius) { _loggedShieldRadius = true; Plugin.Log.LogInfo($"Shield radius is {shieldRadius:0.00} m; parry reach is {Plugin.ParryReach.Value:0.00} m past its edge."); }

                float reach  = shieldRadius + Mathf.Max(0f, Plugin.ParryReach.Value);              // the catch radius
                float search = shieldRadius + Mathf.Max(Plugin.ParryReach.Value, Plugin.ParryReadRange.Value);   // how far we look for something incoming
                float maxTti = 0f;
                int mask = ReflectionChargePatch.Mask();
                try { var l = GameManager.LayerSettings; mask |= l.PlayersMask | l.GolfCartsMask; } catch { }

                int n = Physics.OverlapSphereNonAlloc(centre, search, _threatBuffer, mask, QueryTriggerInteraction.Collide);
                _threatSeen.Clear();
                _threatText.Clear();
                _rejectText.Clear();
                _rejectCount = 0;
                for (int i = 0; i < n; i++)
                {
                    var c = _threatBuffer[i];
                    if (c == null) continue;

                    // Distance from the bubble's edge to the nearest point of the thing.
                    float dist = Mathf.Max(0f, Vector3.Distance(centre, c.bounds.ClosestPoint(centre)) - shieldRadius);

                    var other = c.GetComponentInParent<PlayerInfo>();
                    if (other != null)
                    {
                        if (ReferenceEquals(other, player) || !_threatSeen.Add(other)) continue;
                        string name; try { name = other.PlayerId.PlayerNameNoRichText; } catch { name = "a player"; }
                        bool teammate = false;
                        try { teammate = player.IsTeammateOf(other, excludeSelf: true); } catch { }
                        if (teammate) { Reject($"{name} teammate"); continue; }   // the game never lets a teammate's swing land
                        if (dist > Plugin.ParryReach.Value + 1f) { Reject($"{name} {dist:0.0}m away"); continue; }   // a club has to be able to reach
                        var g = other.AsGolfer;
                        if (g == null || !(g.IsChargingSwing || g.IsSwinging)) { Reject($"{name} not swinging"); continue; }
                        _parrySwing = true;
                        _threatText.Append(_threatText.Length > 0 ? ", " : "").Append(g.IsSwinging ? "swing" : "wind-up").Append(" from ").Append(name).Append($" {dist:0.0}m");
                        continue;
                    }

                    // Not a player: anything with a rigidbody that is moving. The COLLIDER's
                    // rigidbody, not the game's cached Entity one: balls and carts are
                    // Mirror-predicted, and prediction moves the physics body onto a
                    // separate object that carries no Entity at all. Looking for the Entity
                    // there found nothing, which is why the parry never armed on a ball.
                    var rb = c.attachedRigidbody;
                    if (rb == null) { Reject($"{c.name} static"); continue; }
                    if (!_threatSeen.Add(rb)) continue;
                    string  rbName = rb.name.Replace("(Clone)", "");
                    Vector3 vel    = rb.linearVelocity;
                    float   speed  = vel.magnitude;
                    if (speed < ThreatMinSpeed) { Reject($"{rbName} {speed:0.0}m/s"); continue; }

                    // Two ways in. CLOSE, whatever it is doing: the catch. Or INCOMING fast
                    // enough to arrive within ParryReadTime: the read. Distance alone was
                    // useless against a homing ball -- at 20 m/s, 1.5 m is 75 ms.
                    Vector3 toMe    = centre - rb.worldCenterOfMass;
                    float   gap     = Mathf.Max(0.01f, toMe.magnitude);
                    float   closing = Vector3.Dot(vel, toMe / gap);
                    float   tti     = closing > 0.5f ? Mathf.Max(0f, gap - shieldRadius) / closing : float.PositiveInfinity;
                    bool close    = dist <= Plugin.ParryReach.Value;
                    bool incoming = tti <= Plugin.ParryReadTime.Value;
                    if (!close && !incoming)
                    {
                        Reject($"{rbName} {dist:0.0}m, {(float.IsInfinity(tti) ? "not closing" : $"{tti:0.00}s out")}");
                        continue;
                    }
                    _parryProjectile = true;
                    if (incoming) maxTti = Mathf.Max(maxTti, tti);
                    _threatText.Append(_threatText.Length > 0 ? ", " : "").Append(rbName).Append($" {dist:0.0}m at {speed:0}m/s")
                               .Append(incoming ? $", {tti:0.00}s out" : "");
                }

                // Hitscan read (experimental, see AimedAt.cs): a bead on you counts as a projectile in reach.
                if (AimedAt.Anyone(player, centre, reach, out string shooter))
                {
                    _parryProjectile = true;
                    _threatText.Append(_threatText.Length > 0 ? ", " : "").Append(shooter);
                }

                if (_parrySwing || _parryProjectile)
                {
                    // Live at least ParryArmTime, and in any case long enough for the
                    // farthest incoming thing to actually get here.
                    float armFor = Mathf.Max(Mathf.Max(0.05f, Plugin.ParryArmTime.Value), maxTti + 0.15f);
                    _parryThreat = _threatText.ToString();
                    _parryArmedUntil = Time.timeAsDouble + armFor;
                    Plugin.Log.LogInfo($"Parry armed for {armFor:0.00}s: {_parryThreat}.");
                }
                else
                    // Always on while the parry is being tuned: one line per release, naming what was
                    // in reach and why it did not count. This is the line to paste when "parry does not work".
                    Plugin.Log.LogInfo($"Bubble released: nothing armed. {n} collider(s) within {Plugin.ParryReach.Value:0.0}m of the edge" +
                                       (_rejectText.Length > 0 ? $": {_rejectText}" : "") + ".");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Parry arming failed: " + e.Message);
            }
        }

        /// <summary>
        /// Club or ball? Read off the game's own enum names, so a type added later sorts
        /// itself: anything with "Swing" in the name that is not a "...Projectile" is a club.
        /// </summary>
        internal static bool IsSwingClass(KnockoutType t) => IsSwingClassName(t.ToString());

        private static bool IsSwingClassName(string n) =>
            n.IndexOf("Swing", StringComparison.Ordinal) >= 0 && n.IndexOf("Projectile", StringComparison.Ordinal) < 0;

        /// <summary>The bypass branch (penalty stroke) runs before this is ever asked, so it needs no case here.</summary>
        internal static bool IsPerfectParry(int cost, bool swingClass)
        {
            if (!Plugin.PerfectParry.Value) return false;
            if (Time.timeAsDouble > _parryArmedUntil) return false;
            if (swingClass ? !_parrySwing : !_parryProjectile) return false;
            if (cost == CostUnblockable) return Plugin.PerfectParryBeatsUnblockable.Value;
            if (cost == CostFullBreak)   return Plugin.PerfectParryBeatsFullBreak.Value;
            return true;
        }

        /// <summary>Class of the last parried hit (club or projectile), for the coverage window.</summary>
        private static bool _lastParrySwingClass;

        /// <summary>
        /// A hit arriving within ParryCoverWindow of a parry, of the same class, is part
        /// of the same read: a rocket volley, a ball and the thing it knocked loose. The
        /// arm itself is still spent by the first hit, so nothing new can be fished for.
        /// It obeys the same rules as the parry itself: never an unblockable, and a
        /// full-break hit only if PerfectParryBeatsFullBreak allows it.
        /// </summary>
        private static bool CoveredByRecentParry(int cost, bool swingClass) =>
            cost != CostUnblockable
            && (cost != CostFullBreak || Plugin.PerfectParryBeatsFullBreak.Value)
            && LastParryAt > double.MinValue
            && Time.timeAsDouble - LastParryAt <= Mathf.Max(0f, Plugin.ParryCoverWindow.Value)
            && swingClass == _lastParrySwingClass;

        private static void Parry(PlayerInfo player, string what, int cost, bool swingClass)
        {
            LastParryAt = Time.timeAsDouble;
            _lastParrySwingClass = swingClass;
            _parryArmedUntil = double.MinValue;   // one release, one parry
            if (Plugin.PerfectParryRefundsUse.Value) UseCooldownUntil = double.MinValue;

            Plugin.Log.LogInfo($"PERFECT PARRY on {what} ({(Time.timeAsDouble - LoweredAt) * 1000.0:0} ms after release; armed by {_parryThreat}; would have cost {CostLabel(cost)}). {Pips} pips kept, shove cancelled.");

            // The parry has already happened by here. A failure in the effects or the
            // message must not throw out of the TryKnockOut prefix: that runs inside the
            // game's hit handler, and an exception there makes Mirror drop the connection.
            try { ParryFx.Play(player); }   // flash, burst, sound, shake -- the same thing everyone else sees over SbgNet
            catch (Exception e) { Plugin.Log.LogWarning("Parry effects failed: " + e.Message); }
            try { SbgNet.Send(SbgNet.Kind.Parry, 0f); }   // so everyone else sees and hears it too
            catch (Exception e) { Plugin.Log.LogWarning("Parry message failed: " + e.Message); }
        }

        private static string CostLabel(int cost) =>
            cost == CostFullBreak ? "a break" : cost == CostUnblockable ? "an unblockable" : cost == CostBypass ? "nothing (bypasses the bubble)" : cost + " pips";

        /// <summary>
        /// Percent scales with how hard the hit was: the game's own knockback speed for
        /// the hit, over a reference of a full-power swing. A point-blank elephant gun
        /// (60 m/s) is twice a swing; a pistol at range (15) is half. Speed already
        /// encodes distance for explosions, so the separate explosion falloff steps
        /// aside while this is on.
        /// </summary>
        internal static float SpeedFactor(float speed)
        {
            if (!Plugin.PercentScalesWithSpeed.Value || speed < 0f) return 1f;
            float reference = Mathf.Max(1f, Plugin.PercentReferenceSpeed.Value);
            return Mathf.Clamp(speed / reference, Plugin.PercentSpeedFactorMin.Value, Plugin.PercentSpeedFactorMax.Value);
        }

        internal static float GetPercentGain(int cost, KnockoutType? type, float speed)
        {
            float baseGain;
            if (cost == CostUnblockable)    baseGain = Plugin.PercentPerUnblockableHit.Value;
            else if (cost == CostFullBreak) baseGain = Plugin.PercentPerFullBreakHit.Value;
            else if (cost == CostBypass)    baseGain = Plugin.PercentPerHitBase.Value;   // an ordinary hit with no pip part
            else                            baseGain = Plugin.PercentPerHitBase.Value + Plugin.PercentPerPip.Value * cost;

            if (Plugin.PercentScalesWithSpeed.Value && speed >= 0f)
                return baseGain * SpeedFactor(speed);

            // Explosions already lose knockback with distance, so percent should follow:
            // standing at the edge of a rocket blast should not cost the same as eating it.
            if (Plugin.ExplosionPercentFalloff.Value && type.HasValue && IsExplosive(type.Value))
            {
                float radius = Mathf.Max(0.5f, Plugin.ExplosionFalloffRadius.Value);
                float t = Mathf.Clamp01(PendingHitDistance / radius);
                float mult = Mathf.Lerp(1f, Mathf.Clamp01(Plugin.ExplosionPercentAtEdge.Value), t);
                if (Plugin.VerboseLogging.Value)
                    Plugin.Log.LogInfo($"{type} at {PendingHitDistance:0.0}m: percent x{mult:0.00}.");
                baseGain *= mult;
            }
            return baseGain;
        }

        // ---- Scaling ---------------------------------------------------------

        /// <summary>0..1 over the FIRST 100% (PercentForMaxScaling). Everything percent-driven saturates here, knockback included.</summary>
        internal static float ScaleT => Mathf.Clamp01(Percent / Mathf.Max(1f, Plugin.PercentForMaxScaling.Value));

        /// <summary>
        /// The knockback curve. A power curve from 1.0x at 0% to ForceMultiplierAtMax at
        /// PercentForMaxScaling, then FLAT. With the defaults (2.3x, exponent 1.5):
        ///   40% 1.3x   60% 1.6x   80% 1.9x   100%+ 2.3x
        /// It used to keep climbing to the kill line (6x at 250%). That tied the height
        /// of an ordinary launch to a setting that exists for a different reason, sent
        /// people so high the camera lost them, and made a death launch look like just a
        /// bigger hit. Now every survivable launch tops out here, and the only thing that
        /// goes higher is the kill boost -- so when someone rockets off screen, they are dead.
        /// </summary>
        internal static float KnockbackCurve(float t)
        {
            float e = Mathf.Max(1f, Plugin.KnockbackExponent.Value);
            return Mathf.Pow(Mathf.Clamp01(t), e);
        }

        internal static float ForceMultiplier      => 1f + (Plugin.ForceMultiplierAtMax.Value - 1f)      * KnockbackCurve(ScaleT);
        internal static float HorizontalMultiplier => 1f + (Plugin.HorizontalMultiplierAtMax.Value - 1f) * KnockbackCurve(ScaleT);
        internal static float HitstunMultiplier    => Mathf.Lerp(1f, Plugin.HitstunMultiplierAtMax.Value, ScaleT);   // < 1 by default: big hits give the air back sooner

        // ---- Hit resolution --------------------------------------------------

        /// <summary>
        /// Called from the TryKnockOut prefix for the local player.
        /// Returns true if the vanilla knockout should proceed, false if the
        /// shield fully absorbed the hit and the knockout must be skipped.
        /// </summary>
        internal static bool ResolveKnockout(PlayerInfo player, PlayerInfo responsiblePlayer, KnockoutType type, Vector3 incomingVelocityChange)
        {
            ClearPendingHitOnly();
            _pendingType = type;

            // Standing down (public lobby, version mismatch): hand the hit straight
            // back to the game. This has to be here, above the absorb branch, or the
            // shield keeps eating hits while the mod claims to be off.
            if (!ModHandshake.GameplayEnabled) return true;

            // Our own knockout request after a reflection break (see BounceAfterBreak).
            // No hit landed, so no percent: just the bounce.
            if (RequestingBreakBounce)
            {
                QueueBreakBounce(player, Vector3.zero);
                PendingPercentGain = 0f;
                _awaitingResult = true;
                ForcingBreakKnockout = Plugin.BreakStunIgnoresComebackImmunity.Value;
                BreakTrace.Log($"asking the game for a knockout to carry the break bounce (stun x{PendingHitstunMultiplier:0.00})");
                return true;
            }

            bool targeted = PendingProjectileWasTargeted;
            PendingProjectileWasTargeted = false;

            int  cost      = GetCost(type, targeted);
            bool swing     = IsSwingClass(type);
            bool lingering = Plugin.ShieldLingering;
            bool shieldUp  = player.IsElectromagnetShieldActive;
            _awaitingResult = false;
            ForcingBreakKnockout = false;
            SnapshotForRefund(shieldUp && Plugin.WeActivated);

            if (shieldUp && !Plugin.WeActivated && !lingering)
            {
                // The vanilla magnet item's shield: leave it entirely to the game.
                // A lingering shield is still ours, so it must not fall in here.
                return true;
            }

            // The game's own refusals come FIRST: a teammate's hit, comeback immunity,
            // domination protection, a frozen body. The game would refuse these with its
            // own blocked effect, so the bubble must not spend pips on them, a parry must
            // not fire on them, and nothing must be queued. Hand them back untouched.
            bool alreadyDown = false;
            try { alreadyDown = player.Movement != null && player.Movement.IsKnockedOut; } catch { }

            if (GameRefuses(player, responsiblePlayer, type, out string refusedBy))
            {
                // The game still applies a refused hit's knockback. On a body that is
                // already down (the air hold raises the comeback shield mid-tumble) that
                // meant being juggled round the sky, so it moves nothing; standing up, a
                // refused hit keeps the game's shove.
                if (alreadyDown) AddCorrection(-incomingVelocityChange);
                Plugin.Log.LogInfo($"Hit {type} refused by the game ({refusedBy}){(alreadyDown ? ": shove cancelled" : "")}; the bubble is not involved.");
                return true;
            }

            // Bypass (the penalty stroke): the bubble is not part of this hit at all.
            // If it is up, it just goes away -- no pop, no break, no cooldown -- and the
            // hit lands on the body like any other. Above the parry on purpose.
            if (cost == CostBypass)
            {
                if (shieldUp) Plugin.DropShieldQuietly($"{type} goes through the bubble");
                shieldUp = false;
            }
            // Perfect parry. Checked here, ABOVE the shielded branch, because the whole
            // point is that the shield is already down by the time the hit arrives --
            // inside that branch it could never fire. Never on a body that is already
            // knocked out: an arm left over from before your own knockout is not a read.
            else if (!alreadyDown && IsPerfectParry(cost, swing))
            {
                Parry(player, type.ToString(), cost, swing);
                AddCorrection(-incomingVelocityChange);
                return false;
            }
            // The tail of a parry: a second hit of the same class right behind the one
            // that was read (the next rocket of a volley).
            else if (!alreadyDown && CoveredByRecentParry(cost, swing))
            {
                Plugin.Log.LogInfo($"PARRY also covers {type} ({(Time.timeAsDouble - LastParryAt) * 1000.0:0} ms after the parry; would have cost {CostLabel(cost)}; shove cancelled).");
                AddCorrection(-incomingVelocityChange);
                return false;
            }

            // A lingering body (ParryLinger, after the key is up) is drawn but stops
            // nothing: the release is the commitment. Only the parry can save you now.
            // The game does not know that: its own CanBeKnockedOutBy refuses every
            // ordinary hit while the shield flag is up, so the body has to actually go
            // before the hit can land, or the linger is a free block after every release.
            if (shieldUp && lingering)
            {
                Plugin.DropShieldQuietly($"{type} arrived during the linger");
                shieldUp = false;
            }

            if (shieldUp && Plugin.ShieldAbsorbsHits.Value)
            {
                LastKnockoutChargeTime = Time.timeAsDouble;

                if (cost == CostUnblockable)
                {
                    // Shield drops, hit lands in full. No in-place stun: the hit itself stuns.
                    Break(player, playBreakEffect: true);
                    Plugin.Log.LogInfo($"Bubble hit by unblockable {type}: broken, the hit goes through.");
                }
                else if (cost == CostFullBreak || cost >= Pips)
                {
                    // THE BREAK. Any hit the bubble cannot fully absorb breaks it, and
                    // every break bounces you (QueueBreakBounce). What the excess decides
                    // is percent only: a full-break type (swing, rocket) counts as
                    // PercentGainOnFullBreak of its percent; a costed hit that beat your
                    // remaining pips counts the fraction that was not covered.
                    float excess = (cost == CostFullBreak) ? Plugin.PercentGainOnFullBreak.Value : 1f - (float)Pips / cost;
                    int before = Pips;
                    Break(player, playBreakEffect: true);

                    QueueBreakBounce(player, incomingVelocityChange);
                    BreakTrace.Begin(3f);
                    BreakTrace.Log($"BUBBLE BREAK by {type} ({before} pips vs cost {cost}): bounce {Plugin.BreakBounceSpeed.Value:0.0} m/s up, stun x{PendingHitstunMultiplier:0.00}");
                    PendingPercentGain = GetPercentGain(cost, type, incomingVelocityChange.magnitude) * Mathf.Clamp01(excess);
                    _pendingLandedLine = $"Bubble broken by {type}: bounced, +{PendingPercentGain:0}%.";
                    _awaitingResult = true;
                    ForcingBreakKnockout = Plugin.BreakStunIgnoresComebackImmunity.Value;
                    return true;
                }
                else
                {
                    LosePips(cost);
                    bool cancelled = Plugin.AbsorbedHitsCancelKnockback.Value;
                    if (cancelled) AddCorrection(-incomingVelocityChange);
                    if (GameShowsNoSparksFor(type)) PlayAbsorbSparks(player);
                    Plugin.Log.LogInfo($"Bubble absorbed {type}: -{cost} pips{(cancelled ? ", shove cancelled" : "")}.");
                    return false;
                }
            }

            string fromWhere = IsExplosive(type) ? $" from {PendingHitDistance:0.0}m" : "";

            // Hit while already knocked out (stunned in place or mid-tumble): the game's
            // own knockback, no percent scaling, no shaping. Percent still accrues.
            if (alreadyDown)
            {
                _pendingWhileDown = true;
                _pendingIncoming  = incomingVelocityChange;
                PendingPercentGain = GetPercentGain(cost, type, incomingVelocityChange.magnitude);
                _pendingLandedLine = $"Hit {type} landed while down{fromWhere}: vanilla knockback {incomingVelocityChange.magnitude:0.0} m/s, +{PendingPercentGain:0}%.";
                _awaitingResult = true;
                return true;
            }

            // Outside a hole there is no percent, so there is nothing to scale:
            // leave the knockback exactly as the game made it. Same with the percent
            // layer switched off: that is what "off" means.
            if (!InPlayableHole || !Plugin.PercentEnabled.Value)
            {
                _pendingLandedLine = $"Hit {type} landed{fromWhere} ({(Plugin.PercentEnabled.Value ? "outside a hole" : "percent off")}): vanilla launch {incomingVelocityChange.magnitude:0.0} m/s, no percent.";
                _awaitingResult = true;
                return true;
            }

            // Knockout proceeds.
            float hitstunMult = HitstunMultiplier;
            HitCategory cat = Categorize(type);

            // Percent scaling of the force. For explosions it is ALSO scaled by how far
            // away the blast was: full at the centre, down to ExplosionForceAtEdge at the
            // falloff radius. So 100% does not mean 100% knockback -- a distant landmine
            // at high percent is still a distant landmine.
            float forceMult = ForceMultiplier;
            bool explosive = IsExplosive(type);
            if (explosive && Plugin.ExplosionPercentFalloff.Value)
            {
                float radius = Mathf.Max(0.5f, Plugin.ExplosionFalloffRadius.Value);
                float dt = Mathf.Clamp01(PendingHitDistance / radius);
                float distFactor = Mathf.Lerp(1f, Mathf.Clamp01(Plugin.ExplosionForceAtEdge.Value), dt);
                forceMult = 1f + (forceMult - 1f) * distFactor;
            }

            float catScale = CategoryForce(cat);
            Vector3 launchIn = incomingVelocityChange * (forceMult * catScale);

            // Explosions: rebuild the direction from where the blast actually was. The
            // game forces a minimum upward speed on explosion knockback regardless of
            // geometry, and once multiplied that floor swallowed the sideways component;
            // a blast at your feet and one off to your left felt the same. The blast
            // position arrives in our local space, so the true radial is recoverable.
            if (explosive && Plugin.ExplosionRadialLaunch.Value)
            {
                Vector3 worldOrigin = player.transform.TransformPoint(PendingHitLocalOrigin);
                Vector3 from = player.transform.position + Vector3.up * 0.9f;   // chest-ish, not feet
                Vector3 radial = from - worldOrigin;
                if (radial.sqrMagnitude > 0.01f)
                {
                    radial.Normalize();
                    // Keep the game's speed, take our direction. Blend so a blast directly
                    // underneath still goes up, and a blast far to one side goes sideways.
                    float speed = launchIn.magnitude;
                    Vector3 vanillaDir = launchIn.sqrMagnitude > 1e-6f ? launchIn.normalized : radial;
                    Vector3 dir = Vector3.Slerp(vanillaDir, radial, Mathf.Clamp01(Plugin.ExplosionRadialWeight.Value)).normalized;
                    launchIn = dir * speed;
                }
            }

            Vector3 shaped = ShapeLaunch(player, launchIn, cat);
            AddCorrection(shaped - incomingVelocityChange);
            PendingHitstunMultiplier     = Mathf.Max(0.05f, hitstunMult);
            PendingPercentGain           = GetPercentGain(cost, type, incomingVelocityChange.magnitude);
            LaunchDragUntil              = Time.timeAsDouble + Plugin.LaunchDragDuration.Value;
            LaunchHangUntil              = Time.timeAsDouble + Plugin.LaunchHangDuration.Value;
            // Hang ramps from nothing at CloudHitMinPercent to full at HangFullPercent: a
            // 70% launch barely lingers, a 150% one floats at the top for everyone to see.
            LaunchHangScale              = Mathf.InverseLerp(Plugin.CloudHitMinPercent.Value, Mathf.Max(Plugin.CloudHitMinPercent.Value + 1f, Plugin.HangFullPercent.Value), Percent);
            Launch.Begin();
            _launchBegunThisHit = true;
            // Printed from OnKnockoutResolved only if the knockout really happens: one
            // line per hit that lands, so "I got knocked back after X" starts here.
            _pendingLandedLine = $"Hit {type} landed at {Percent:0}%{fromWhere}: +{PendingPercentGain:0}%, launch {shaped.magnitude:0.0} m/s" +
                                 (Plugin.VerboseLogging.Value ? $" [{cat}] force x{forceMult:0.00} x{catScale:0.00}, |v| {incomingVelocityChange.magnitude:0.0} (h {new Vector2(shaped.x, shaped.z).magnitude:0.0}, up {shaped.y:0.0}), hitstun x{PendingHitstunMultiplier:0.00}" : "") + ".";
            _awaitingResult = true;
            return true;
        }

        // ---- Per-hit bookkeeping for OnKnockoutResolved -------------------------

        private static KnockoutType _pendingType;
        private static bool   _launchBegunThisHit;
        private static string _pendingLandedLine;

        /// <summary>
        /// Would the game refuse this knockout on its own? The same checks, in the same
        /// order, as the game's CanBeKnockedOutBy minus the shield (deciding the shield is
        /// our job): teammate protection, comeback immunity, domination protection, frozen.
        /// The teammate rule mirrors TryKnockOut's DoesBypassTeamProtection.
        /// </summary>
        private static bool GameRefuses(PlayerInfo player, PlayerInfo responsible, KnockoutType type, out string why)
        {
            why = null;
            var mv = player.Movement;
            if (mv == null) return false;
            try
            {
                if (responsible != null && !BypassesTeamProtection(type) && player.IsTeammateOf(responsible, excludeSelf: true)) { why = "teammate"; return true; }
                if (mv.KnockoutImmunityStatus.hasImmunity) { why = "comeback shield"; return true; }
                if (responsible != null && mv.IsKnockoutProtectedFromPlayer(responsible)) { why = "domination protection"; return true; }
                if (player.AsHittable != null && player.AsHittable.FrozenState == FrozenState.Frozen) { why = "frozen"; return true; }
            }
            catch (Exception e)
            {
                if (!_refusalCheckWarned) { _refusalCheckWarned = true; Plugin.Log.LogWarning("Could not read the game's knockout rules: " + e.Message); }
            }
            return false;
        }
        private static bool _refusalCheckWarned;

        /// <summary>The game's DoesBypassTeamProtection (PlayerMovement.TryKnockOut), copied: which hits ignore teammate protection.</summary>
        private static bool BypassesTeamProtection(KnockoutType t)
        {
            switch (t)
            {
                case KnockoutType.DuelingPistol:
                case KnockoutType.DeflectedDuelingPistolShot:
                case KnockoutType.ElephantGun:
                case KnockoutType.DeflectedElephantGunShot:
                    return !MatchSetupRules.GetValueAsBool(MatchSetupRules.Rule.FirearmTeammateProtection);
                case KnockoutType.GolfCart:
                    return !MatchSetupRules.GetValueAsBool(MatchSetupRules.Rule.GolfCartTeammateProtection);
                case KnockoutType.Landmine:
                case KnockoutType.Rocket:
                case KnockoutType.RocketBackBlast:
                case KnockoutType.ReflectedRocket:
                case KnockoutType.ElectromagnetShieldExplosion:
                case KnockoutType.OrbitalLaserPeripheralHit:
                case KnockoutType.OrbitalLaserDirectHit:
                case KnockoutType.OrbitalLaserElectromagnetShieldDirectHit:
                case KnockoutType.ThunderstormPeripheralHit:
                case KnockoutType.ThunderstormDirectHit:
                case KnockoutType.ThunderstormElectromagnetShieldDirectHit:
                case KnockoutType.RailgunDirectHit:
                case KnockoutType.RailgunElectromagnetShieldHit:
                    return !MatchSetupRules.GetValueAsBool(MatchSetupRules.Rule.ExplosionTeammateProtection);
                default:
                    return false;
            }
        }

        /// <summary>
        /// Hit types whose caller in the game plays no shield-hit effect when the knockout
        /// is refused with the shield up: balls, guns and the magnet explosion. Everything
        /// else (rockets, mines, edge hits, carts, rocket-driver hits) already gets the
        /// game's own sparks, and playing ours as well showed two bursts.
        /// </summary>
        private static bool GameShowsNoSparksFor(KnockoutType t)
        {
            switch (t)
            {
                case KnockoutType.SwingProjectile:
                case KnockoutType.ReflectedSwingProjectile:
                case KnockoutType.RocketDriverSwingProjectile:
                case KnockoutType.JumboBurgerGiantSwingProjectile:
                case KnockoutType.DuelingPistol:
                case KnockoutType.DeflectedDuelingPistolShot:
                case KnockoutType.ElephantGun:
                case KnockoutType.DeflectedElephantGunShot:
                case KnockoutType.ElectromagnetShieldExplosion:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Hang time is active until this moment; set with a percent launch.</summary>
        internal static double LaunchHangUntil = double.MinValue;

        /// <summary>How strongly this particular launch floats, 0-1 by percent.</summary>
        internal static float LaunchHangScale;

        /// <summary>While set, the FixedUpdate patch applies extra air drag for the fast-then-floaty arc.</summary>
        internal static double LaunchDragUntil = double.MinValue;

        // ---- Hit categories --------------------------------------------------

        /// <summary>
        /// Three kinds of hit, three feels. Explosives lift you; bullets shove you along
        /// the ground; melee is the small one. Each gets its own force multiplier and its
        /// own share of the elevation floor, and bullets additionally get a ceiling so a
        /// gunshot never reads as taking off.
        /// </summary>
        internal enum HitCategory { Explosive, Bullet, Melee }

        /// <summary>
        /// Explosives are the known blast-radius types. Bullets are read off the enum's
        /// own names (Pistol, Gun, Shot), so a new firearm sorts itself. Everything else
        /// -- swings, balls, carts, vehicles -- is melee.
        /// </summary>
        internal static HitCategory Categorize(KnockoutType t)
        {
            if (IsExplosive(t)) return HitCategory.Explosive;
            string n = t.ToString();
            if (n.IndexOf("Pistol", StringComparison.Ordinal) >= 0 ||
                n.IndexOf("Gun",    StringComparison.Ordinal) >= 0 ||
                n.IndexOf("Shot",   StringComparison.Ordinal) >= 0)
                return HitCategory.Bullet;
            return HitCategory.Melee;
        }

        private static float CategoryForce(HitCategory c)
        {
            switch (c)
            {
                case HitCategory.Explosive: return Mathf.Max(0f, Plugin.ExplosiveForceScale.Value);
                case HitCategory.Bullet:    return Mathf.Max(0f, Plugin.BulletForceScale.Value);
                default:                    return Mathf.Max(0f, Plugin.MeleeForceScale.Value);
            }
        }

        private static float CategoryFloorScale(HitCategory c)
        {
            switch (c)
            {
                case HitCategory.Explosive: return Mathf.Clamp01(Plugin.ExplosiveAngleFloorScale.Value);
                case HitCategory.Bullet:    return Mathf.Clamp01(Plugin.BulletAngleFloorScale.Value);
                default:                    return Mathf.Clamp01(Plugin.MeleeAngleFloorScale.Value);
            }
        }

        /// <summary>
        /// Raise the launch angle with percent and cap horizontal speed, keeping the
        /// total speed. Vertical launches are what stop the juggle: while you are in
        /// the air above the fight, carts and swings cannot reach you.
        /// </summary>
        internal static Vector3 ShapeLaunch(PlayerInfo player, Vector3 v, HitCategory cat)
        {
            float speed = v.magnitude;
            if (speed < 0.01f || !Plugin.ShapeLaunches.Value) return v;

            float minAngle = Mathf.Lerp(Plugin.MinLaunchAngleAtZero.Value, Plugin.MinLaunchAngleAtMax.Value, ScaleT) * CategoryFloorScale(cat) * Mathf.Deg2Rad;
            var   h        = new Vector3(v.x, 0f, v.z);
            float hMag     = h.magnitude;
            float elev     = Mathf.Atan2(v.y, hMag);

            if (elev < minAngle) elev = minAngle;
            if (cat == HitCategory.Bullet)
            {
                float maxElev = Mathf.Clamp(Plugin.BulletMaxElevation.Value, 0f, 90f) * Mathf.Deg2Rad;
                if (elev > maxElev) elev = maxElev;
            }

            float hSpeed = speed * Mathf.Cos(elev);
            float vSpeed = speed * Mathf.Sin(elev);

            // Extra horizontal reach at high percent, so late-game hits carry you across
            // the hole rather than just higher. Applied after the angle floor so it does
            // not fight it: the arc keeps its elevation, the whole thing just goes further.
            float hBoost = HorizontalMultiplier;
            if (hBoost != 1f) hSpeed *= hBoost;

            // The boost changes the total, so the cap has to spend the BOOSTED speed.
            // It used to recompute the vertical from the pre-boost magnitude, which
            // meant that above the cap the horizontal multiplier did nothing at all:
            // its extra speed was taken off horizontal and never given to height. Since
            // the cap binds on most hits past ~100%, that was the whole knob voided at
            // exactly the percents it existed for.
            float boosted = Mathf.Sqrt(hSpeed * hSpeed + vSpeed * vSpeed);

            float cap = Plugin.MaxHorizontalLaunchSpeed.Value;
            if (cap > 0f && hSpeed > cap)
            {
                // Spend the excess on height rather than losing it.
                vSpeed = Mathf.Sqrt(Mathf.Max(0f, boosted * boosted - cap * cap));
                hSpeed = cap;
            }

            Vector3 hDir;
            if (hMag > 0.05f) hDir = h / hMag;
            else
            {
                // Straight-up hit: push away from the direction we're facing so it still reads.
                var f = player.transform.forward; f.y = 0f;
                hDir = f.sqrMagnitude > 0.01f ? -f.normalized : Vector3.forward;
            }
            return hDir * hSpeed + Vector3.up * vSpeed;
        }

        /// <summary>
        /// Called from the TryKnockOut postfix, which Harmony runs even when our prefix
        /// skipped the original. Commits percent only if the knockout really happened.
        ///
        /// The refusal cleanup runs only for a hit we actually handed to the game
        /// (_awaitingResult). A parry, a parry-cover or an absorb also arrives here with
        /// knockedOut false, because the prefix returned false; until 0.7.34 the cleanup
        /// ran for those too and wiped the shove cancel they had just queued, so a
        /// perfect parry still threw you at full force.
        /// </summary>
        internal static void OnKnockoutResolved(bool knockedOut)
        {
            if (knockedOut)
            {
                if (PendingPercentGain > 0f) AddPercent(PendingPercentGain);
                if (_pendingLandedLine != null) Plugin.Log.LogInfo(_pendingLandedLine);
                if (InPlayableHole && Plugin.PercentEnabled.Value && Plugin.KillZoneEnabled.Value && Percent >= Plugin.KillPercent.Value && !KillZone.IsArmed)
                    KillZone.Arm();
                // Your own knockout spends any parry still armed from before it.
                DisarmParry();
            }
            else if (_awaitingResult)
            {
                // Vanilla refused a hit we handed it (self-hit, giant form, or a refusal
                // our pre-check could not see). Hand back what was spent, and take back
                // only what THIS hit queued: a boosted launch with no stun is a free
                // gap-clear, but an earlier hit in the same physics step keeps its own.
                if (Plugin.RefundPipsOnRefusedKnockout.Value) RefundRefusedHit();
                PendingIsBreak           = false;
                PendingHitstunMultiplier = -1f;
                PendingVelocityCorrection   -= _thisHitCorrection;
                HasPendingVelocityCorrection = PendingVelocityCorrection.sqrMagnitude > 1e-6f;
                if (_launchBegunThisHit)
                {
                    Launch.Cancel();
                    LaunchDragUntil = double.MinValue;
                    LaunchHangUntil = double.MinValue;
                }

                // Refused while already down: the comeback shield stopped the knockout, but
                // the game still shoves the body. Immune-and-tumbling used to mean being
                // juggled around the sky, never landing, until the 10 s time-out forced a
                // wake-up under the gold shield. A refused hit on a downed body moves nothing.
                if (_pendingWhileDown) AddCorrection(-_pendingIncoming);

                Plugin.Log.LogInfo($"Hit {_pendingType} refused by the game: no stun{(_pendingWhileDown ? ", shove cancelled" : "")}.");
                BreakTrace.Log("game REFUSED the knockout: no stun will happen");
            }
            PendingPercentGain = 0f;
            _awaitingResult = false;
            ForcingBreakKnockout = false;
            _pendingWhileDown = false;
            _launchBegunThisHit = false;
            _pendingLandedLine = null;
            _thisHitCorrection = Vector3.zero;
        }

        private static bool    _pendingWhileDown;
        private static Vector3 _pendingIncoming;

        /// <summary>Testing hook: jump straight to a percent, shake and all.</summary>
        internal static void SetPercent(float value)
        {
            float before = Percent;
            Percent = Mathf.Clamp(value, 0f, Plugin.MaxPercent.Value);
            if (Percent > before) PercentIncreased?.Invoke(Percent);
            Plugin.Log.LogInfo($"Percent set: {before:0} -> {Percent:0}.");
            if (Plugin.KillZoneEnabled.Value && Percent >= Plugin.KillPercent.Value && !KillZone.IsArmed)
                Plugin.Log.LogInfo("At or above the kill line: the next knockout kills.");
        }

        internal static void AddPercent(float amount)
        {
            if (amount <= 0f) return;
            if (!Plugin.PercentEnabled.Value) return;
            if (!InPlayableHole)
            {
                if (Plugin.VerboseLogging.Value) Plugin.Log.LogInfo($"Percent gain of {amount:0} ignored: not in a hole.");
                return;
            }
            float before = Percent;
            Percent = Mathf.Min(Plugin.MaxPercent.Value, Percent + amount);
            if (Percent > before) PercentIncreased?.Invoke(Percent);
            if (Plugin.VerboseLogging.Value) Plugin.Log.LogInfo($"Percent {before:0} -> {Percent:0}");
        }

        /// <summary>
        /// A projectile bounced off the shield server-side. We never see a TryKnockOut
        /// for it, so charge here. If the shield could not afford it, it breaks (the
        /// projectile still bounced -- reflection is server-authoritative).
        /// </summary>
        internal static void ChargeReflection(PlayerInfo player, int cost, string what)
        {
            if (!player.IsElectromagnetShieldActive || !Plugin.WeActivated || !Plugin.ShieldAbsorbsHits.Value) return;
            if (cost == CostUnblockable || cost == CostBypass) return;

            // A reflection never reaches TryKnockOut, so the parry check in
            // ResolveKnockout never sees it. Without this, timing a shield onto a rocket
            // would bounce it and still cost you the pips.
            if (IsPerfectParry(cost, swingClass: false)) { Parry(player, "reflected " + what, cost, swingClass: false); return; }

            if (cost == CostFullBreak || cost >= Pips)
            {
                BreakTrace.Begin(3f);
                BreakTrace.Log($"BUBBLE BREAK by reflected {what} ({Pips} pips vs cost {cost})");
                Break(player, playBreakEffect: true);
                // A reflection never goes through TryKnockOut -- the projectile bounced,
                // nothing hit us -- so without this there is no knockout and no bounce.
                BounceAfterBreak(player);
            }
            else
            {
                LosePips(cost);
                if (Plugin.VerboseLogging.Value) Plugin.Log.LogInfo($"Reflected {what}: -{cost} pips, {Pips} left.");
            }
        }

        /// <summary>
        /// Set while we are asking the game to knock us out purely to carry the bounce
        /// after a reflection break. ResolveKnockout sees it and queues the bounce
        /// instead of treating it as an incoming hit.
        /// </summary>
        internal static bool RequestingBreakBounce;

        /// <summary>
        /// Creates the knockout for a break that did not come from a hit. Uses the
        /// game's own TryKnockOut with zero velocity so animation, credit and the
        /// recovery timer all run through vanilla code; our prefix adds the bounce.
        /// </summary>
        private static void BounceAfterBreak(PlayerInfo player)
        {
            var mv = player.Movement;
            if (mv == null) return;
            if (mv.IsKnockedOutOrRecovering)
            {
                BreakTrace.Log("already knocked out; not requesting a second knockout");
                return;
            }

            RequestingBreakBounce = true;
            try
            {
                bool ok = mv.TryKnockOut(player, KnockoutType.ElectromagnetShieldExplosion, false,
                    Vector3.zero, 0f, Vector3.zero, ElectromagnetShieldHitBlockType.FullyBlocked,
                    ItemUseId.Invalid, false, false, out _, out _);
                BreakTrace.Log(ok ? "bounce knockout accepted by the game" : "bounce knockout REFUSED by the game");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Break bounce request failed: " + e.Message);
            }
            finally { RequestingBreakBounce = false; }
        }

        /// <summary>
        /// With the bubble no longer a physical wall (Bubble.BubbleReflects off), balls,
        /// guns and the magnet explosion reach the body without the game playing any
        /// shield sparks (see GameShowsNoSparksFor). Play them at the point on the bubble
        /// facing where the hit came from, for everyone. The game's offset argument is in
        /// world space from the bubble's centre; the hit origin arrives in the player's
        /// local space, so it is converted first.
        /// </summary>
        private static void PlayAbsorbSparks(PlayerInfo player)
        {
            if (Plugin.BubbleReflects.Value) return;   // the collision already did it
            try
            {
                var col = player.ElectromagnetShieldCollider;
                float r = col != null ? col.radius : 1f;
                Vector3 centre = col != null ? col.transform.position : player.transform.position + Vector3.up * 0.9f;
                Vector3 dir = player.transform.TransformPoint(PendingHitLocalOrigin) - centre;
                if (dir.sqrMagnitude < 0.01f) dir = Vector3.up;
                player.PlayElectromagnetShieldHitForAllClients(dir.normalized * r, isExplosion: false);
            }
            catch (Exception e) { if (Plugin.VerboseLogging.Value) Plugin.Log.LogWarning("Absorb sparks: " + e.Message); }
        }

        internal static void Break(PlayerInfo player, bool playBreakEffect)
        {
            LosePips(Pips);
            double now = Time.timeAsDouble;
            BreakCooldownUntil = now + Plugin.BreakCooldown.Value;
            UseCooldownUntil   = Math.Max(UseCooldownUntil, now + Plugin.UseCooldown.Value);
            DisarmParry();

            // A break snaps the bubble off: no soft release dissolve under the shatter,
            // on any screen. Never the game's explode path, which hits everyone nearby.
            Plugin.SnapOffShield(player, "broken");
            Plugin.NotifyShieldDropped();

            if (playBreakEffect)
            {
                // Vanilla break VFX + sound + screenshake, replicated to everyone.
                // No damage: the damage lives in ExplodeElectromagnetShield, not here.
                try { player.PlayElectromagnetShieldHitForAllClients(default, isExplosion: true); }
                catch (Exception e) { Plugin.Log.LogWarning("Break effect failed: " + e.Message); }
            }
        }

        // ---- Teching -----------------------------------------------------------
        // Smash: press shield just before your tumbling body hits the ground and you
        // are up instantly instead of lying there. Here the get-up is the game's
        // Recovering animation plus the full comeback bubble; a tech skips both and
        // gives only TechImmunity. Nothing to do with the attacker, so it is entirely
        // local, like everything else the victim decides.

        private static double _shieldPressAt = double.MinValue;
        private static double _landedAt      = double.MinValue;
        private static bool   _techPending;

        /// <summary>True only for the synchronous moment SetKnockOutState(None) runs for a tech. TechImmunityPatch reads it.</summary>
        private static bool _techRecovery;

        private static System.Reflection.MethodInfo _setKnockOutState;
        private static bool _setKnockOutStateLooked;

        /// <summary>The knockout being resolved was caused by the victim (own rocket, own back-blast).</summary>
        internal static bool PendingSelfInflicted;
        internal static bool CurrentKnockoutSelfInflicted;

        /// <summary>After a tech you are up but not yet acting: rooted for TechRecovery. The rooting patches read this.</summary>
        internal static double TechRootUntil = double.MinValue;
        internal static bool   TechRooted => Time.timeAsDouble < TechRootUntil;

        /// <summary>
        /// Shift went down while knocked out. One press is one attempt: it is live for
        /// TechWindow, and if the ground does not arrive in that time the attempt is
        /// spent and nothing counts again until TechLockout has passed. Holding or
        /// mashing the key therefore gives you exactly one badly-timed attempt, the
        /// way Smash treats it. Before this, every press refreshed the timer and
        /// mashing was a guaranteed tech.
        /// </summary>
        internal static void NoteShieldPress(PlayerInfo player)
        {
            try
            {
                if (player == null || player.Movement == null || !player.Movement.IsKnockedOut) return;
                double now = Time.timeAsDouble;
                if (now < _shieldPressAt + Plugin.TechWindow.Value + Plugin.TechLockout.Value) return;   // previous attempt still live or locked out
                _shieldPressAt = now;
            }
            catch { }
        }

        /// <summary>Called from the SetKnockOutState postfix on the InAir -> OnGround transition of the local player.</summary>
        internal static void OnTumbleLanded(PlayerMovement mv)
        {
            if (!ModHandshake.GameplayEnabled) return;
            double now = Time.timeAsDouble;
            _landedAt = now;
            if (KillZone.IsArmed) return;          // dead: the star is coming, nothing to get up for

            bool pressed = now - _shieldPressAt <= Mathf.Max(0.02f, Plugin.TechWindow.Value);
            // Only a launch big enough to smoke can be teched: the same rule as the trail
            // (speed and percent). A small shove is a stun you sit through; the tech is
            // the reward for reading a real hit. User's rule, 13 Sep 2026.
            bool bigEnough = !Plugin.TechNeedsBigLaunch.Value || LaunchVfx.LocalBigLaunchAt >= mv.IsKnockedOutTimestamp;
            bool tech = Plugin.TechEnabled.Value
                        && !CurrentKnockoutIsBreak   // the break bounce is the one stun you sit through
                        && (Plugin.TechSelfInflicted.Value || !CurrentKnockoutSelfInflicted)   // no free movement tech off your own rocket
                        && pressed && bigEnough;
            bool held = AirHold.IsHolding;
            if (tech) { _techPending = true; return; }
            if (pressed && !bigEnough && Plugin.TechEnabled.Value && !CurrentKnockoutIsBreak)
                Plugin.Log.LogInfo($"TECH refused: launch too small to tech (no smoke trail; needs {Plugin.LaunchTrailStartSpeed.Value:0} m/s at {Plugin.LaunchTrailMinPercent.Value:0}%+, you were at {EffectivePercent:0}%).");

            // No tech: the flight WAS the stun. Start the get-up shortly after touchdown
            // instead of lying there for whatever is left of the game's own timer (3 s
            // by its constants), which is why a short launch felt like a longer stun
            // than a huge one. With percent off the game's own stun stands -- unless the
            // air hold kept you down past it, in which case the game's timer is spent
            // and the landing stun is the get-up delay.
            if (!CurrentKnockoutIsBreak && !Plugin.PercentEnabled.Value && !held) return;
            float stun = Mathf.Max(0f, CurrentKnockoutIsBreak ? Plugin.BreakLandingStun.Value : Plugin.LandingStun.Value);
            // A floor on the whole thing, hit to get-up. "The flight is the stun" alone
            // was silly at low percent: a one-second hop and you were up. So: the flight
            // OR the floor, whichever is longer. A short launch lies there until
            // MinStunAfterHit has passed since the hit; a long flight has already spent
            // it and gets up on landing. A break has its own, longer floor -- losing the
            // bubble is the moment that costs you. The tech skips whatever ground time
            // is left, which is what gives it a purpose on the hits that need one.
            float sinceHit = (float)(now - mv.IsKnockedOutTimestamp);
            float floorTotal = CurrentKnockoutIsBreak ? Plugin.BreakMinStun.Value : Plugin.MinStunAfterHit.Value;
            if (!CurrentKnockoutIsBreak && !Plugin.PercentEnabled.Value) floorTotal = 0f;   // floors belong to the percent layer
            float floor = Mathf.Max(0f, floorTotal - sinceHit);
            stun = Mathf.Max(stun, floor);
            if (held)
            {
                // The hold pinned the game's timer near zero so its ground check could run;
                // SET it now so the landing stun and the floors still apply.
                if (HitstunPatch.SetRecoveryTimer(mv, stun))
                    Plugin.Log.LogInfo($"Landed{(CurrentKnockoutIsBreak ? " from a break" : "")} {sinceHit:0.00}s after the hit, after the air hold: get-up in {stun:0.00}s.");
                AirHold.Released(mv, "landed");
            }
            else if (HitstunPatch.ClampRecoveryTimer(mv, stun, out float before) && before > stun)
                Plugin.Log.LogInfo($"Landed{(CurrentKnockoutIsBreak ? " from a break" : "")} {sinceHit:0.00}s after the hit: get-up in {stun:0.00}s (the game's timer had {before:0.00}s left).");
        }

        /// <summary>
        /// Runs in the FixedUpdate prefix, i.e. before the game's own knockout update
        /// on this step can start the get-up. Calls the game's private
        /// SetKnockOutState(None) directly, which is the same transition vanilla uses
        /// for an instant recovery, so animation and physics reset the vanilla way.
        /// </summary>
        internal static void TryExecuteTech(PlayerMovement mv, Rigidbody rb)
        {
            if (!_techPending) return;
            _techPending = false;
            double pressLead = _landedAt - _shieldPressAt;
            _shieldPressAt = double.MinValue;
            if (!mv.IsKnockedOut) return;

            if (!_setKnockOutStateLooked)
            {
                _setKnockOutStateLooked = true;
                _setKnockOutState = AccessTools.Method(typeof(PlayerMovement), "SetKnockOutState", new[] { typeof(KnockoutState) });
                if (_setKnockOutState == null) Plugin.Log.LogWarning("SetKnockOutState not found; teching disabled.");
            }
            if (_setKnockOutState == null) return;

            try
            {
                _techRecovery = true;
                if (rb != null) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
                _setKnockOutState.Invoke(mv, new object[] { KnockoutState.None });
                AirHold.Released(mv, "teched");
                TechRootUntil = Time.timeAsDouble + Mathf.Max(0f, Plugin.TechRecovery.Value);
                Plugin.Log.LogInfo($"TECH: pressed {pressLead * 1000.0:0} ms before landing; up instantly, rooted {Plugin.TechRecovery.Value:0.00}s, {Plugin.TechImmunity.Value:0.00}s bubble.");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Tech failed: " + e.Message); }
            finally { _techRecovery = false; }   // StartKnockoutImmunity fires synchronously inside the call above
        }

        internal static bool ConsumeTechRecovery()
        {
            if (!_techRecovery) return false;
            _techRecovery = false;
            return true;
        }

        /// <summary>
        /// Per-hit state only. The velocity accumulator is deliberately NOT touched: it
        /// belongs to the physics step, and an earlier hit in the same step keeps its part.
        /// </summary>
        private static void ClearPendingHitOnly()
        {
            _pendingWhileDown = false;
            _thisHitCorrection = Vector3.zero;
            _launchBegunThisHit = false;
            _pendingLandedLine = null;
            PendingIsBreak           = false;
            PendingHitstunMultiplier = -1f;
            PendingPercentGain = 0f;
        }
    }
}

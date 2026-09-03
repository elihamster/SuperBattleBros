using System;
using FMODUnity;
using UnityEngine;

namespace SbgShields
{
    /// <summary>
    /// All shield economy state for the LOCAL player. Knockouts in this game are
    /// victim-authoritative (TryKnockOut / CanBeKnockedOutBy run on the victim's
    /// own client), so pips, percent and hitstun can all live here with no new
    /// network messages. Every player runs this for themselves.
    /// </summary>
    internal static class ShieldState
    {
        // Sentinel costs. Positive ints are chip costs in pips.
        internal const int CostFullBreak   = 1000;
        internal const int CostUnblockable = -1;

        // ---- Live state -----------------------------------------------------

        internal static int    Pips;
        internal static float  Percent;

        internal static double UseCooldownUntil   = double.MinValue;

        /// <summary>When the shield was last deliberately released. The perfect-parry window is measured from here.</summary>
        internal static double LoweredAt = double.MinValue;

        /// <summary>When the last perfect parry landed, for anything that wants to react to it.</summary>
        internal static double LastParryAt = double.MinValue;
        internal static double BreakCooldownUntil = double.MinValue;

        /// <summary>Fired with the new percent whenever it goes up. HUD uses this for the shake.</summary>
        internal static event Action<float> PercentIncreased;

        // ---- Pending per-hit data, consumed by the PlayerMovement patches -----

        /// <summary>Velocity to add on the next FixedUpdate, correcting the launch the game already applied.</summary>
        internal static Vector3 PendingVelocityCorrection;
        internal static bool    HasPendingVelocityCorrection;

        /// <summary>Applied in SetKnockOutState(InAir). Absolute wins over multiplier. Negative = none.</summary>
        internal static float PendingHitstunAbsolute   = -1f;
        internal static float PendingHitstunMultiplier = -1f;

        /// <summary>Set by the swing-projectile handler right before TryKnockOut so we can tell targeted from untargeted balls.</summary>
        internal static bool PendingProjectileWasTargeted;

        /// <summary>Percent to add if the knockout actually goes through (checked in the postfix).</summary>
        internal static float PendingPercentGain;

        /// <summary>Last time TryKnockOut charged the shield. Reflection notifications within a short window of this are duplicates.</summary>
        internal static double LastKnockoutChargeTime = double.MinValue;

        // ---- Break stun ------------------------------------------------------

        /// <summary>While now is before this, BreakStunHoldPatch refuses knockout recovery. Set by HitstunPatch.</summary>
        internal static double BreakStunUntil = double.MinValue;

        /// <summary>The knockout currently running started as a break stun; the immunity that follows it is percent-scaled.</summary>
        internal static bool LastKnockoutWasBreakStun;

        /// <summary>A launch resolved while stunned in place (juggle): once it lands, hand the knockout back to vanilla rules.</summary>
        private static bool _pendingReleasesBreakStun;

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
            if (Plugin.VerboseLogging.Value)
                Plugin.Log.LogInfo($"Knockout refused: refunded {spent} pip(s). The bubble still popped, but it costs you nothing.");
        }

        /// <summary>Set by KillZone right before it triggers the respawn, so OnRespawn does not also halve the (already reset) percent.</summary>
        internal static bool KillZoneDeathPending;

        /// <summary>Called by BreakImmunityPatch when the game starts post-knockout immunity. True once per break stun.</summary>
        internal static bool ConsumeBreakStunImmunity()
        {
            if (!LastKnockoutWasBreakStun) return false;
            LastKnockoutWasBreakStun = false;
            BreakStunUntil = double.MinValue;
            return true;
        }

        /// <summary>
        /// Stun-in-place on a break, lerped over the percent scale. Deliberately the
        /// opposite slope to HitstunMultiplier: a launch hands control back earlier at
        /// high percent, a break holds you longer.
        /// </summary>
        internal static float BreakStun =>
            !Plugin.BreakStunScalesWithPercent.Value
                ? Plugin.BreakStunDuration.Value
                : Mathf.Max(0.05f, Mathf.Lerp(Plugin.BreakStunDuration.Value, Plugin.BreakStunDurationAtMax.Value, ScaleT));

        /// <summary>Immunity after a break stun: lerp between the two config ends on the percent scale.</summary>
        internal static float BreakImmunityDuration =>
            Mathf.Max(0f, Mathf.Lerp(Plugin.BreakImmunityAtZeroPercent.Value, Plugin.BreakImmunityAtMaxPercent.Value, ScaleT));

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
                catch { return true; }
            }
        }

        /// <summary>True in the driving range, where the shield works but percent does not.</summary>
        internal static bool InDrivingRange
        {
            get { try { return SingletonBehaviour<DrivingRangeManager>.HasInstance; } catch { return false; } }
        }

        // ---- Lifecycle -------------------------------------------------------

        /// <summary>Wipes everything. Used when a match or a practice session starts.</summary>
        internal static void FullReset(string why)
        {
            Pips = Plugin.MaxPips.Value;
            Percent = 0f;
            UseCooldownUntil   = double.MinValue;
            BreakCooldownUntil = double.MinValue;
            BreakStunUntil     = double.MinValue;
            LastKnockoutWasBreakStun = false;
            KillZoneDeathPending = false;
            _lastRespawnTime = double.MinValue;
            KillZone.Disarm();
            KillZone.CancelLinger();
            ClearPending();
            if (Plugin.VerboseLogging.Value) Plugin.Log.LogInfo($"Full reset ({why}): pips={Pips}, percent=0.");
        }

        internal static void ResetForNewHole()
        {
            Pips = Plugin.MaxPips.Value;
            Percent *= 1f - Mathf.Clamp01(Plugin.PercentReductionBetweenHoles.Value);
            UseCooldownUntil   = double.MinValue;
            BreakCooldownUntil = double.MinValue;
            BreakStunUntil     = double.MinValue;
            LastKnockoutWasBreakStun = false;
            KillZone.Disarm();
            KillZone.CancelLinger();
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
            BreakStunUntil = double.MinValue;
            LastKnockoutWasBreakStun = false;
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
            PendingHitstunAbsolute   = -1f;
            PendingHitstunMultiplier = -1f;
            PendingPercentGain = 0f;
            PendingProjectileWasTargeted = false;
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
                // 1 pip
                case KnockoutType.DuelingPistol:
                case KnockoutType.ElephantGun:
                case KnockoutType.DeflectedDuelingPistolShot:
                case KnockoutType.DeflectedElephantGunShot:
                case KnockoutType.ReturnedBall:
                    return 1;

                // Balls: targeted = full break, untargeted = chip.
                case KnockoutType.SwingProjectile:
                case KnockoutType.ReflectedSwingProjectile:
                case KnockoutType.RocketDriverSwingProjectile:
                    return projectileTargeted ? CostFullBreak : 1;

                // 2 pips
                case KnockoutType.RocketBackBlast:
                case KnockoutType.ThunderstormPeripheralHit:
                case KnockoutType.OrbitalLaserPeripheralHit:
                case KnockoutType.ElectromagnetShieldExplosion:
                    return 2;

                // 3 pips
                case KnockoutType.GolfCart:
                case KnockoutType.TrafficVehicle:
                case KnockoutType.RocketDriverSwing:
                case KnockoutType.RocketDriverSwingPostHitSpin:
                case KnockoutType.Landmine:
                    return 3;

                // Unblockable: the shield drops and you eat the hit.
                case KnockoutType.OrbitalLaserDirectHit:
                case KnockoutType.ThunderstormDirectHit:
                case KnockoutType.RailgunDirectHit:
                case KnockoutType.OrbitalLaserElectromagnetShieldDirectHit:
                case KnockoutType.ThunderstormElectromagnetShieldDirectHit:
                case KnockoutType.RailgunElectromagnetShieldHit:
                    return CostUnblockable;

                // Everything else -- Swing, Rocket, ReflectedRocket, Landmine,
                // FreezeBomb, giant swings and collisions, and any type added later.
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
        /// A hit arriving inside PerfectParryWindow of the shield coming DOWN. Free: no
        /// pips, no percent, no knockback. What it buys over a normal absorb is the hits
        /// pips cannot cover -- a cart or a targeted ball would break the shield
        /// outright, and a read stops it dead.
        ///
        /// Measuring from the release rather than the raise is what makes it a read
        /// instead of a reaction. Holding the shield out and being hit is an ordinary
        /// block that costs pips; letting go into the hit has to be committed to before
        /// the hit lands. Only a deliberate release opens the window -- a shield that
        /// broke (NotifyShieldDropped) does not.
        ///
        /// It cannot punish the attacker. Doing that means changing another player's
        /// state, and there is nowhere to say it: the server is the stock game. The
        /// reward has to be entirely on the defender's side, which is why it is a
        /// refund and a sound rather than a counter-stun.
        /// </summary>
        /// <summary>Whether the parry (and therefore the linger) should run at all.</summary>
        internal static bool GameplayParryEnabled => ModHandshake.GameplayEnabled && Plugin.PerfectParry.Value;

        internal static bool IsPerfectParry(int cost)
        {
            if (!Plugin.PerfectParry.Value) return false;
            if (Time.timeAsDouble - LoweredAt > Plugin.PerfectParryWindow.Value) return false;
            if (cost == CostUnblockable) return Plugin.PerfectParryBeatsUnblockable.Value;
            if (cost == CostFullBreak)   return Plugin.PerfectParryBeatsFullBreak.Value;
            return true;
        }

        private static void Parry(PlayerInfo player, string what, int cost)
        {
            LastParryAt = Time.timeAsDouble;
            if (Plugin.PerfectParryRefundsUse.Value) UseCooldownUntil = double.MinValue;
            ShieldTint.ParryFlash(player);

            if (Plugin.PerfectParrySound.Value)
            {
                // The game's own "your immunity refused that knockout" sting. Already
                // means "that hit did not land", which is exactly the read we want.
                try { RuntimeManager.PlayOneShot(GameManager.AudioSettings.KnockoutImmunityBlockedKnockoutEvent, player.transform.position); }
                catch (Exception e) { if (Plugin.VerboseLogging.Value) Plugin.Log.LogWarning("Parry sound: " + e.Message); }
            }

            Plugin.Log.LogInfo($"PERFECT PARRY on {what} ({(Time.timeAsDouble - LoweredAt) * 1000.0:0} ms after release, would have cost {CostLabel(cost)}). {Pips} pips kept.");
        }

        private static string CostLabel(int cost) =>
            cost == CostFullBreak ? "a break" : cost == CostUnblockable ? "an unblockable" : cost + " pips";

        internal static float GetPercentGain(int cost) => GetPercentGain(cost, null);

        internal static float GetPercentGain(int cost, KnockoutType? type)
        {
            float baseGain;
            if (cost == CostUnblockable)    baseGain = Plugin.PercentPerUnblockableHit.Value;
            else if (cost == CostFullBreak) baseGain = Plugin.PercentPerFullBreakHit.Value;
            else                            baseGain = Plugin.PercentPerHitBase.Value + Plugin.PercentPerPip.Value * cost;

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

        /// <summary>0..1 over the FIRST 100% (PercentForMaxScaling). Used by the things that should saturate: angle floor, hang, immunity, HUD.</summary>
        internal static float ScaleT => Mathf.Clamp01(Percent / Mathf.Max(1f, Plugin.PercentForMaxScaling.Value));

        /// <summary>0..1 over the WHOLE range to the kill line. Knockback runs on this, so it never plateaus.</summary>
        internal static float KillT => Mathf.Clamp01(Percent / Mathf.Max(1f, Plugin.KillPercent.Value));

        /// <summary>
        /// The knockback curve. A power curve from 1.0x at 0% to ForceMultiplierAtKill at
        /// the kill line: flat early, steep late. With the defaults (6x, exponent 1.5):
        ///   40% 1.4x   80% 1.9x   100% 2.3x   150% 3.3x   200% 4.6x   250% 6.0x
        /// Linear to 100% and clamped after it -- the old shape -- made 80% feel like 100%
        /// and 100% feel like 240%.
        /// </summary>
        internal static float KnockbackCurve(float t)
        {
            float e = Mathf.Max(1f, Plugin.KnockbackExponent.Value);
            return Mathf.Pow(Mathf.Clamp01(t), e);
        }

        internal static float ForceMultiplier      => 1f + (Plugin.ForceMultiplierAtKill.Value - 1f)      * KnockbackCurve(KillT);
        internal static float HorizontalMultiplier => 1f + (Plugin.HorizontalMultiplierAtKill.Value - 1f) * KnockbackCurve(KillT);
        internal static float HitstunMultiplier    => Mathf.Lerp(1f, Plugin.HitstunMultiplierAtMax.Value, ScaleT);   // < 1 by default: big hits give the air back sooner

        // ---- Hit resolution --------------------------------------------------

        /// <summary>
        /// Called from the TryKnockOut prefix for the local player.
        /// Returns true if the vanilla knockout should proceed, false if the
        /// shield fully absorbed the hit and the knockout must be skipped.
        /// </summary>
        internal static bool ResolveKnockout(PlayerInfo player, KnockoutType type, Vector3 incomingVelocityChange)
        {
            ClearPendingHitOnly();

            // Standing down (public lobby, version mismatch): hand the hit straight
            // back to the game. This has to be here, above the absorb branch, or the
            // shield keeps eating hits while the mod claims to be off.
            if (!ModHandshake.GameplayEnabled) return true;

            // Our own stun request after a reflection break (see StunInPlaceAfterBreak).
            // No hit landed, so no percent and no launch: just the stun.
            if (RequestingBreakStun)
            {
                PendingHitstunAbsolute = BreakStun;
                PendingVelocityCorrection = -player.Movement.Velocity;   // freeze on the spot
                HasPendingVelocityCorrection = PendingVelocityCorrection.sqrMagnitude > 1e-6f;
                PendingPercentGain = 0f;
                _awaitingResult = true;
                ForcingBreakKnockout = Plugin.BreakStunIgnoresComebackImmunity.Value;
                BreakTrace.Log($"asking the game for a {PendingHitstunAbsolute:0.00}s stun");
                return true;
            }

            bool targeted = PendingProjectileWasTargeted;
            PendingProjectileWasTargeted = false;

            int   cost     = GetCost(type, targeted);
            float fraction = 1f;   // how much of the hit gets through
            _awaitingResult = false;
            ForcingBreakKnockout = false;
            SnapshotForRefund(player.IsElectromagnetShieldActive && Plugin.WeActivated);

            if (player.IsElectromagnetShieldActive && !Plugin.WeActivated && !Plugin.ShieldLingering)
            {
                // The vanilla magnet item's shield: leave it entirely to the game.
                // A lingering shield is still ours, so it must not fall in here.
                return true;
            }

            // Perfect parry. Checked here, ABOVE the shielded branch, because the whole
            // point is that the shield is already down by the time the hit arrives --
            // inside that branch it could never fire.
            if (IsPerfectParry(cost))
            {
                Parry(player, type.ToString(), cost);
                PendingVelocityCorrection    = -incomingVelocityChange;
                HasPendingVelocityCorrection = true;
                return false;
            }

            if (player.IsElectromagnetShieldActive && Plugin.ShieldAbsorbsHits.Value)
            {
                LastKnockoutChargeTime = Time.timeAsDouble;

                if (cost == CostUnblockable)
                {
                    // Shield drops, hit lands in full. No in-place stun: the hit itself stuns.
                    Break(player, playBreakEffect: true);
                    if (Plugin.VerboseLogging.Value) Plugin.Log.LogInfo($"Shield hit by unblockable {type}: broken, hit goes through.");
                }
                else if (cost == CostFullBreak || cost >= Pips)
                {
                    // THE BREAK. Any hit the shield cannot fully absorb breaks it, and
                    // every break stuns in place for the same fixed time. What the excess
                    // decides is percent only: a full-break type (swing, rocket) counts
                    // as PercentGainOnFullBreak of its percent; a costed hit that beat
                    // your remaining pips counts the fraction that was not covered.
                    //
                    // History: partial breaks used to launch you at that fraction AND cut
                    // the hitstun to that fraction, so a landmine popping your last two
                    // pips gave a third of a vanilla knockdown -- you were up before the
                    // break effect finished, and the comeback bubble appeared right after.
                    // That was the "instant wake-up" reported for weeks.
                    float excess = (cost == CostFullBreak) ? Plugin.PercentGainOnFullBreak.Value : 1f - (float)Pips / cost;
                    int before = Pips;
                    Break(player, playBreakEffect: true);

                    if (!Plugin.PartialBreakLaunches.Value || cost == CostFullBreak || excess <= 0f)
                    {
                        PendingVelocityCorrection    = -incomingVelocityChange;
                        HasPendingVelocityCorrection = true;
                        PendingHitstunAbsolute       = BreakStun;
                        BreakTrace.Begin(PendingHitstunAbsolute);
                        BreakTrace.Log($"SHIELD BREAK by {type} ({before} pips vs cost {cost}): asking the game for a {PendingHitstunAbsolute:0.00}s stun");
                        PendingPercentGain           = GetPercentGain(cost, type) * Mathf.Clamp01(excess);
                        if (Plugin.VerboseLogging.Value) Plugin.Log.LogInfo($"SHIELD BREAK by {type} ({before} pips). Stunned in place {PendingHitstunAbsolute:0.00}s, +{PendingPercentGain:0}%.");
                        _awaitingResult = true;
                        ForcingBreakKnockout = Plugin.BreakStunIgnoresComebackImmunity.Value;
                        return true;
                    }

                    // Legacy option: partial breaks launch at the uncovered fraction.
                    fraction = excess;
                    if (Plugin.VerboseLogging.Value) Plugin.Log.LogInfo($"Shield broken through by {type}: cost {cost} vs {before} pips, {fraction:P0} of the hit lands.");
                }
                else
                {
                    Pips -= cost;
                    if (Plugin.AbsorbedHitsCancelKnockback.Value)
                    {
                        PendingVelocityCorrection    = -incomingVelocityChange;
                        HasPendingVelocityCorrection = true;
                    }
                    if (Plugin.VerboseLogging.Value) Plugin.Log.LogInfo($"Shield absorbed {type}: -{cost} pips, {Pips} left.");
                    return false;
                }
            }

            // Hit while already knocked out (stunned in place or mid-tumble): the game's
            // own knockback, no percent scaling, no shaping. Percent still accrues.
            bool alreadyDown = false;
            try { alreadyDown = player.Movement != null && player.Movement.IsKnockedOut; } catch { }
            if (alreadyDown)
            {
                PendingPercentGain        = GetPercentGain(cost, type) * fraction;
                _pendingReleasesBreakStun = Time.timeAsDouble < BreakStunUntil; // juggled out of a break stun
                if (Plugin.VerboseLogging.Value) Plugin.Log.LogInfo($"Hit {type} while down: vanilla knockback, +{PendingPercentGain:0}% pending.");
                _awaitingResult = true;
                return true;
            }

            // Outside a hole there is no percent, so there is nothing to scale:
            // leave the knockback exactly as the game made it.
            if (!InPlayableHole)
            {
                if (Plugin.VerboseLogging.Value) Plugin.Log.LogInfo($"Hit {type} outside a hole: vanilla knockback, no percent.");
                _awaitingResult = true;
                return true;
            }

            // Knockout proceeds with `fraction` of the hit.
            float hitstunMult = HitstunMultiplier;

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

            Vector3 launchIn = incomingVelocityChange * (fraction * forceMult);

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

            Vector3 shaped = ShapeLaunch(player, launchIn);
            PendingVelocityCorrection    = shaped - incomingVelocityChange;
            HasPendingVelocityCorrection = PendingVelocityCorrection.sqrMagnitude > 1e-6f;
            PendingHitstunMultiplier     = Mathf.Max(0.05f, fraction * hitstunMult);
            PendingPercentGain           = GetPercentGain(cost, type) * fraction;
            LaunchDragUntil              = Time.timeAsDouble + Plugin.LaunchDragDuration.Value;
            LaunchHangUntil              = Time.timeAsDouble + Plugin.LaunchHangDuration.Value;
            LaunchHangScale              = ScaleT;
            Launch.Begin();
            if (Plugin.VerboseLogging.Value)
                Plugin.Log.LogInfo($"Hit {type} at {Percent:0}%{(explosive ? $" from {PendingHitDistance:0.0}m" : "")}: force x{fraction * forceMult:0.00}, |v| {incomingVelocityChange.magnitude:0.0} -> {shaped.magnitude:0.0} (h {new Vector2(shaped.x, shaped.z).magnitude:0.0}, up {shaped.y:0.0}), hitstun x{PendingHitstunMultiplier:0.00}");
            _awaitingResult = true;
            return true;
        }

        /// <summary>While set, the FixedUpdate patch applies extra air drag for the fast-then-floaty arc.</summary>
        /// <summary>Hang time is active until this moment; set with the launch.</summary>
        internal static double LaunchHangUntil = double.MinValue;

        /// <summary>How strongly this particular launch floats, 0-1 by percent.</summary>
        internal static float LaunchHangScale;

        internal static double LaunchDragUntil = double.MinValue;

        /// <summary>
        /// Raise the launch angle with percent and cap horizontal speed, keeping the
        /// total speed. Vertical launches are what stop the juggle: while you are in
        /// the air above the fight, carts and swings cannot reach you.
        /// </summary>
        internal static Vector3 ShapeLaunch(PlayerInfo player, Vector3 v)
        {
            float speed = v.magnitude;
            if (speed < 0.01f || !Plugin.ShapeLaunches.Value) return v;

            float minAngle = Mathf.Lerp(Plugin.MinLaunchAngleAtZero.Value, Plugin.MinLaunchAngleAtMax.Value, ScaleT) * Mathf.Deg2Rad;
            var   h        = new Vector3(v.x, 0f, v.z);
            float hMag     = h.magnitude;
            float elev     = Mathf.Atan2(v.y, hMag);

            if (elev < minAngle) elev = minAngle;

            float hSpeed = speed * Mathf.Cos(elev);
            float vSpeed = speed * Mathf.Sin(elev);

            // Extra horizontal reach at high percent, so late-game hits carry you across
            // the hole rather than just higher. Applied after the angle floor so it does
            // not fight it: the arc keeps its elevation, the whole thing just goes further.
            float hBoost = HorizontalMultiplier;
            if (hBoost != 1f) hSpeed *= hBoost;

            // The boost changes the total, so the cap has to spend the BOOSTED speed.
            // It used to recompute the vertical from the pre-boost magnitude, which
            // meant that above the cap HorizontalMultiplierAtKill did nothing at all:
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

        /// <summary>Called from the TryKnockOut postfix. Commits percent only if the knockout really happened.</summary>
        internal static void OnKnockoutResolved(bool knockedOut)
        {
            if (knockedOut && PendingPercentGain > 0f)
                AddPercent(PendingPercentGain);
            PendingPercentGain = 0f;
            if (knockedOut && InPlayableHole && Plugin.KillZoneEnabled.Value && Percent >= Plugin.KillPercent.Value && !KillZone.IsArmed)
                KillZone.Arm();
            if (knockedOut && _pendingReleasesBreakStun)
            {
                // Launched while stunned in place: no longer a stun, so no hold and vanilla immunity.
                BreakStunUntil = double.MinValue;
                LastKnockoutWasBreakStun = false;
            }
            _pendingReleasesBreakStun = false;
            if (!knockedOut)
            {
                // Only refund when a knockout was actually attempted; an absorbed chip
                // hit also lands here with knockedOut false and must keep its cost.
                if (_awaitingResult && Plugin.RefundPipsOnRefusedKnockout.Value) RefundRefusedHit();
                // Vanilla refused (immunity, team protection, frozen, self-hit). Drop BOTH the
                // stale hitstun and the shaped launch, otherwise you get a boosted percent
                // launch with no stun -- a free gap-clear.
                PendingHitstunAbsolute   = -1f;
                PendingHitstunMultiplier = -1f;
                HasPendingVelocityCorrection = false;
                PendingVelocityCorrection    = Vector3.zero;
                LaunchDragUntil = double.MinValue;
                BreakTrace.Log("game REFUSED the knockout: no stun will happen");
                if (Plugin.VerboseLogging.Value) Plugin.Log.LogInfo("Knockout refused by the game (immunity/team/self/frozen); launch shaping discarded, no stun.");
            }
            _awaitingResult = false;
            ForcingBreakKnockout = false;
        }

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
            if (cost == CostUnblockable) return;

            // A reflection never reaches TryKnockOut, so the parry check in
            // ResolveKnockout never sees it. Without this, timing a shield onto a rocket
            // would bounce it and still cost you the pips.
            if (IsPerfectParry(cost)) { Parry(player, "reflected " + what, cost); return; }

            if (cost == CostFullBreak || cost >= Pips)
            {
                BreakTrace.Begin(BreakStun);
                BreakTrace.Log($"SHIELD BREAK by reflected {what} ({Pips} pips vs cost {cost})");
                Break(player, playBreakEffect: true);
                // A reflection never goes through TryKnockOut -- the projectile bounced,
                // nothing hit us -- so without this there is no knockout and no stun.
                StunInPlaceAfterBreak(player);
            }
            else
            {
                Pips -= cost;
                if (Plugin.VerboseLogging.Value) Plugin.Log.LogInfo($"Reflected {what}: -{cost} pips, {Pips} left.");
            }
        }

        /// <summary>
        /// Set while we are asking the game to knock us out purely to stun us after a
        /// reflection break. ResolveKnockout sees it and turns the request into the
        /// in-place stun instead of treating it as an incoming hit.
        /// </summary>
        internal static bool RequestingBreakStun;

        /// <summary>
        /// Creates the stun for a break that did not come from a hit. Uses the game's
        /// own TryKnockOut with zero velocity so animation, credit and the recovery
        /// timer all run through vanilla code; our prefix only sets the duration.
        /// </summary>
        private static void StunInPlaceAfterBreak(PlayerInfo player)
        {
            var mv = player.Movement;
            if (mv == null) return;
            if (mv.IsKnockedOutOrRecovering)
            {
                BreakTrace.Log("already knocked out; not requesting a second knockout");
                return;
            }

            RequestingBreakStun = true;
            try
            {
                bool ok = mv.TryKnockOut(player, KnockoutType.ElectromagnetShieldExplosion, false,
                    Vector3.zero, 0f, Vector3.zero, ElectromagnetShieldHitBlockType.FullyBlocked,
                    ItemUseId.Invalid, false, false, out _, out _);
                BreakTrace.Log(ok ? "stun knockout accepted by the game" : "stun knockout REFUSED by the game");
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Break stun request failed: " + e.Message);
            }
            finally { RequestingBreakStun = false; }
        }

        internal static void Break(PlayerInfo player, bool playBreakEffect)
        {
            Pips = 0;
            double now = Time.timeAsDouble;
            BreakCooldownUntil = now + Plugin.BreakCooldown.Value;
            UseCooldownUntil   = Math.Max(UseCooldownUntil, now + Plugin.UseCooldown.Value);

            if (player.IsElectromagnetShieldActive)
            {
                // explode:false -- the vanilla explode path fires an AoE hit on everyone nearby.
                player.LocalPlayerCancelElectromagnetShield(false);
            }
            Plugin.NotifyShieldDropped();

            if (playBreakEffect)
            {
                // Vanilla break VFX + sound + screenshake, replicated to everyone.
                // No damage: the damage lives in ExplodeElectromagnetShield, not here.
                try { player.PlayElectromagnetShieldHitForAllClients(default, isExplosion: true); }
                catch (Exception e) { Plugin.Log.LogWarning("Break effect failed: " + e.Message); }
            }
        }

        private static void ClearPendingHitOnly()
        {
            _pendingReleasesBreakStun = false;
            HasPendingVelocityCorrection = false;
            PendingVelocityCorrection = Vector3.zero;
            PendingHitstunAbsolute   = -1f;
            PendingHitstunMultiplier = -1f;
            PendingPercentGain = 0f;
        }
    }
}

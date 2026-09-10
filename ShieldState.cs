using System;
using FMODUnity;
using HarmonyLib;
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
            if (Plugin.VerboseLogging.Value)
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
            PendingVelocityCorrection    = Vector3.up * Mathf.Max(0f, Plugin.BreakBounceSpeed.Value) - incomingVelocityChange - current;
            HasPendingVelocityCorrection = true;
            PendingHitstunMultiplier     = Mathf.Max(0.05f, Plugin.BreakStunMultiplier.Value);
            PendingIsBreak               = true;
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
            PendingIsBreak           = false;
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

                // Balls: a homing ball is a bigger hit than a stray one, but it is still a
                // ball. It used to be a full break, which meant any locked-on ball popped
                // a full bubble on contact; that read as the bubble not being there.
                case KnockoutType.SwingProjectile:
                case KnockoutType.ReflectedSwingProjectile:
                case KnockoutType.RocketDriverSwingProjectile:
                    return projectileTargeted ? 2 : 1;

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
        /// a catch, not a coin flip. Only a deliberate release arms anything; a shield
        /// that broke (NotifyShieldDropped) does not.
        ///
        /// Nothing here names an item. Entity.IsPlayer and Entity.Rigidbody are the
        /// game's own classification, so an item added in a later update arms a parry
        /// the same way. The swing/projectile split on the receiving side reads the
        /// KnockoutType enum's names, so a new type sorts itself too.
        ///
        /// It cannot punish the attacker. Doing that means changing another player's
        /// state, and there is nowhere to say it: the server is the stock game. The
        /// reward has to be entirely on the defender's side, which is why it is a
        /// refund and a sound rather than a counter-stun.
        /// </summary>
        internal static bool GameplayParryEnabled => ModHandshake.GameplayEnabled && Plugin.PerfectParry.Value;

        private static double _parryArmedUntil = double.MinValue;
        private static bool   _parryProjectile, _parrySwing;
        private static string _parryThreat = "";
        private static readonly Collider[] _threatBuffer = new Collider[48];
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

        /// <summary>Called from Plugin.ReleaseShield while the shield collider still exists.</summary>
        internal static void ArmParryOnRelease(PlayerInfo player)
        {
            _parryArmedUntil = double.MinValue;
            _parryProjectile = _parrySwing = false;
            _parryThreat = "";
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
        internal static bool IsSwingClass(KnockoutType t)
        {
            string n = t.ToString();
            return n.IndexOf("Swing", StringComparison.Ordinal) >= 0 && n.IndexOf("Projectile", StringComparison.Ordinal) < 0;
        }

        internal static bool IsPerfectParry(int cost, bool swingClass)
        {
            if (!Plugin.PerfectParry.Value) return false;
            if (Time.timeAsDouble > _parryArmedUntil) return false;
            if (swingClass ? !_parrySwing : !_parryProjectile) return false;
            if (cost == CostUnblockable) return Plugin.PerfectParryBeatsUnblockable.Value;
            if (cost == CostFullBreak)   return Plugin.PerfectParryBeatsFullBreak.Value;
            return true;
        }

        private static void Parry(PlayerInfo player, string what, int cost)
        {
            LastParryAt = Time.timeAsDouble;
            _parryArmedUntil = double.MinValue;   // one release, one parry
            if (Plugin.PerfectParryRefundsUse.Value) UseCooldownUntil = double.MinValue;
            ShieldTint.ParryFlash(player);

            if (Plugin.PerfectParrySound.Value)
            {
                // The game's own "your immunity refused that knockout" sting. Already
                // means "that hit did not land", which is exactly the read we want.
                try { RuntimeManager.PlayOneShot(GameManager.AudioSettings.KnockoutImmunityBlockedKnockoutEvent, player.transform.position); }
                catch (Exception e) { if (Plugin.VerboseLogging.Value) Plugin.Log.LogWarning("Parry sound: " + e.Message); }
            }

            Plugin.Log.LogInfo($"PERFECT PARRY on {what} ({(Time.timeAsDouble - LoweredAt) * 1000.0:0} ms after release; armed by {_parryThreat}; would have cost {CostLabel(cost)}). {Pips} pips kept.");
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
        internal static bool ResolveKnockout(PlayerInfo player, KnockoutType type, Vector3 incomingVelocityChange)
        {
            ClearPendingHitOnly();

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

            int   cost     = GetCost(type, targeted);
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
            if (IsPerfectParry(cost, IsSwingClass(type)))
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
                    // THE BREAK. Any hit the bubble cannot fully absorb breaks it, and
                    // every break bounces you (QueueBreakBounce). What the excess decides
                    // is percent only: a full-break type (swing, rocket) counts as
                    // PercentGainOnFullBreak of its percent; a costed hit that beat your
                    // remaining pips counts the fraction that was not covered.
                    //
                    // History: partial breaks used to launch you at that fraction AND cut
                    // the hitstun to that fraction, so a landmine popping your last two
                    // pips gave a third of a vanilla knockdown. That was the "instant
                    // wake-up" bug; it lived on behind PartialBreakLaunches until 0.7.8.
                    float excess = (cost == CostFullBreak) ? Plugin.PercentGainOnFullBreak.Value : 1f - (float)Pips / cost;
                    int before = Pips;
                    Break(player, playBreakEffect: true);

                    QueueBreakBounce(player, incomingVelocityChange);
                    BreakTrace.Begin(3f);
                    BreakTrace.Log($"BUBBLE BREAK by {type} ({before} pips vs cost {cost}): bounce {Plugin.BreakBounceSpeed.Value:0.0} m/s up, stun x{PendingHitstunMultiplier:0.00}");
                    PendingPercentGain           = GetPercentGain(cost, type) * Mathf.Clamp01(excess);
                    if (Plugin.VerboseLogging.Value) Plugin.Log.LogInfo($"BUBBLE BREAK by {type} ({before} pips). Bounced, +{PendingPercentGain:0}%.");
                    _awaitingResult = true;
                    ForcingBreakKnockout = Plugin.BreakStunIgnoresComebackImmunity.Value;
                    return true;
                }
                else
                {
                    Pips -= cost;
                    if (Plugin.AbsorbedHitsCancelKnockback.Value)
                    {
                        PendingVelocityCorrection    = -incomingVelocityChange;
                        HasPendingVelocityCorrection = true;
                    }
                    PlayAbsorbSparks(player);
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
                PendingPercentGain        = GetPercentGain(cost, type);
                if (Plugin.VerboseLogging.Value) Plugin.Log.LogInfo($"Hit {type} while down: vanilla knockback, +{PendingPercentGain:0}% pending.");
                _awaitingResult = true;
                return true;
            }

            // Outside a hole there is no percent, so there is nothing to scale:
            // leave the knockback exactly as the game made it. Same with the percent
            // layer switched off: that is what "off" means.
            if (!InPlayableHole || !Plugin.PercentEnabled.Value)
            {
                if (Plugin.VerboseLogging.Value) Plugin.Log.LogInfo($"Hit {type} {(Plugin.PercentEnabled.Value ? "outside a hole" : "with percent off")}: vanilla knockback, no percent.");
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
            PendingVelocityCorrection    = shaped - incomingVelocityChange;
            HasPendingVelocityCorrection = PendingVelocityCorrection.sqrMagnitude > 1e-6f;
            PendingHitstunMultiplier     = Mathf.Max(0.05f, hitstunMult);
            PendingPercentGain           = GetPercentGain(cost, type);
            LaunchDragUntil              = Time.timeAsDouble + Plugin.LaunchDragDuration.Value;
            LaunchHangUntil              = Time.timeAsDouble + Plugin.LaunchHangDuration.Value;
            LaunchHangScale              = ScaleT;
            Launch.Begin();
            if (Plugin.VerboseLogging.Value)
                Plugin.Log.LogInfo($"Hit {type} [{cat}] at {Percent:0}%{(explosive ? $" from {PendingHitDistance:0.0}m" : "")}: force x{forceMult:0.00} x{catScale:0.00}, |v| {incomingVelocityChange.magnitude:0.0} -> {shaped.magnitude:0.0} (h {new Vector2(shaped.x, shaped.z).magnitude:0.0}, up {shaped.y:0.0}), hitstun x{PendingHitstunMultiplier:0.00}");
            _awaitingResult = true;
            return true;
        }

        /// <summary>While set, the FixedUpdate patch applies extra air drag for the fast-then-floaty arc.</summary>
        /// <summary>Hang time is active until this moment; set with the launch.</summary>
        internal static double LaunchHangUntil = double.MinValue;

        /// <summary>How strongly this particular launch floats, 0-1 by percent.</summary>
        internal static float LaunchHangScale;

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

        /// <summary>Called from the TryKnockOut postfix. Commits percent only if the knockout really happened.</summary>
        internal static void OnKnockoutResolved(bool knockedOut)
        {
            if (knockedOut && PendingPercentGain > 0f)
                AddPercent(PendingPercentGain);
            PendingPercentGain = 0f;
            if (knockedOut && InPlayableHole && Plugin.KillZoneEnabled.Value && Percent >= Plugin.KillPercent.Value && !KillZone.IsArmed)
                KillZone.Arm();
            if (!knockedOut)
            {
                // Only refund when a knockout was actually attempted; an absorbed chip
                // hit also lands here with knockedOut false and must keep its cost.
                if (_awaitingResult && Plugin.RefundPipsOnRefusedKnockout.Value) RefundRefusedHit();
                // Vanilla refused (immunity, team protection, frozen, self-hit). Drop BOTH the
                // stale hitstun and the shaped launch, otherwise you get a boosted percent
                // launch with no stun -- a free gap-clear.
                PendingIsBreak           = false;
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
            if (cost == CostUnblockable) return;

            // A reflection never reaches TryKnockOut, so the parry check in
            // ResolveKnockout never sees it. Without this, timing a shield onto a rocket
            // would bounce it and still cost you the pips.
            if (IsPerfectParry(cost, swingClass: false)) { Parry(player, "reflected " + what, cost); return; }

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
                Pips -= cost;
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
        /// With the bubble no longer a physical wall (Bubble.BubbleReflects off) nothing
        /// collides with it, so the game never plays its own hit sparks. Play them at the
        /// point on the bubble facing where the hit came from, for everyone.
        /// </summary>
        private static void PlayAbsorbSparks(PlayerInfo player)
        {
            if (Plugin.BubbleReflects.Value) return;   // the collision already did it
            try
            {
                float r = 1f;
                var col = player.ElectromagnetShieldCollider;
                if (col != null) r = col.radius;
                Vector3 local = PendingHitLocalOrigin.sqrMagnitude > 0.01f ? PendingHitLocalOrigin.normalized * r : Vector3.up * r;
                player.PlayElectromagnetShieldHitForAllClients(local, isExplosion: false);
            }
            catch (Exception e) { if (Plugin.VerboseLogging.Value) Plugin.Log.LogWarning("Absorb sparks: " + e.Message); }
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

        /// <summary>Shift went down. Only counts as a tech input while you are knocked out.</summary>
        internal static void NoteShieldPress(PlayerInfo player)
        {
            try { if (player != null && player.Movement != null && player.Movement.IsKnockedOut) _shieldPressAt = Time.timeAsDouble; }
            catch { }
        }

        /// <summary>Called from the SetKnockOutState postfix on the InAir -> OnGround transition of the local player.</summary>
        internal static void OnTumbleLanded(PlayerMovement mv)
        {
            if (!ModHandshake.GameplayEnabled) return;
            double now = Time.timeAsDouble;
            _landedAt = now;
            if (KillZone.IsArmed) return;          // dead: the star is coming, nothing to get up for

            bool tech = Plugin.TechEnabled.Value
                        && !CurrentKnockoutIsBreak   // the break bounce is the one stun you sit through
                        && now - _shieldPressAt <= Mathf.Max(0.02f, Plugin.TechWindow.Value);
            if (tech) { _techPending = true; return; }

            // No tech: the flight WAS the stun. Start the get-up shortly after touchdown
            // instead of lying there for whatever is left of the game's own timer (3 s
            // by its constants), which is why a short launch felt like a longer stun
            // than a huge one.
            if (!CurrentKnockoutIsBreak && !Plugin.PercentEnabled.Value) return;   // percent off = the game's own stun
            float stun = Mathf.Max(0f, CurrentKnockoutIsBreak ? Plugin.BreakLandingStun.Value : Plugin.LandingStun.Value);
            if (HitstunPatch.ClampRecoveryTimer(mv, stun, out float before) && before > stun)
                Plugin.Log.LogInfo($"Landed{(CurrentKnockoutIsBreak ? " from a break" : "")}: get-up in {stun:0.00}s (the game's timer had {before:0.00}s left).");
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
                Plugin.Log.LogInfo($"TECH: pressed {pressLead * 1000.0:0} ms before landing; up instantly, {Plugin.TechImmunity.Value:0.00}s bubble.");
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

        private static void ClearPendingHitOnly()
        {
            HasPendingVelocityCorrection = false;
            PendingVelocityCorrection = Vector3.zero;
            PendingIsBreak           = false;
            PendingHitstunMultiplier = -1f;
            PendingPercentGain = 0f;
        }
    }
}

using System;
using FMODUnity;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace SbgShields
{
    /// <summary>
    /// Smash's blast zone, but vertical. Once a knockout lands you at or above
    /// KillPercent the sequence is:
    ///   1. the killing blow gets a big upward boost (you are dead anyway, so this
    ///      is pure showmanship) -- you rocket up out of the fight,
    ///   2. at the apex, a star flash, a boom and a heavy shake,
    ///   3. you vanish and the camera holds on the spot for KillZoneDeathLinger,
    ///   4. the vanilla respawn runs.
    ///
    /// Local only. The game eliminates players from the server (ServerEliminate)
    /// and offers no client command for it, so this uses TryBeginRespawn, which
    /// the local player IS allowed to call. That respawns correctly but does not
    /// post to the KO feed or credit the attacker -- that needs a host-side
    /// message, same as pip sync.
    /// </summary>
    internal static class KillZone
    {
        private static bool   _armed;
        private static double _armedAt;
        private static bool   _wentUp;

        /// <summary>Set while the player is dead and hidden, waiting out the linger.</summary>
        private static bool   _lingering;
        private static double _respawnAt;

        private static GameObject _flashGo;
        private static ParticleSystem _flashPs;
        private static ParticleSystem _sparkPs;
        private static Material _flashMat, _sparkMat;
        private static Texture2D _starTex, _dotTex;

        internal static bool IsArmed => _armed;
        internal static bool IsLingering => _lingering;

        /// <summary>
        /// Called when a knockout has just committed percent at or above the kill line.
        /// Adds the death boost to the launch the game is about to apply.
        /// </summary>
        internal static void Arm()
        {
            _armed = true;
            _armedAt = Time.timeAsDouble;
            _wentUp = false;

            float boost = Plugin.KillZoneUpwardBoost.Value;
            if (boost > 0f)
            {
                // Rides along with the shaped-launch correction the FixedUpdate patch applies.
                ShieldState.PendingVelocityCorrection += Vector3.up * boost;
                ShieldState.HasPendingVelocityCorrection = true;
                ShieldState.LaunchDragUntil = double.MinValue; // no drag on the death launch
            }

            if (Plugin.VerboseLogging.Value) Plugin.Log.LogInfo($"Kill zone armed at {ShieldState.Percent:0}%: +{boost:0} m/s up, waiting for the apex.");
        }

        internal static void Disarm() { _armed = false; }

        internal static void Tick()
        {
            if (_lingering) { TickLinger(); return; }
            if (!_armed) return;
            var p = GameManager.LocalPlayerInfo;
            var mv = p != null ? p.Movement : null;
            if (mv == null) { _armed = false; return; }

            double now = Time.timeAsDouble;
            if (mv.IsRespawningOrDrowning) { _armed = false; return; }
            if (!mv.IsKnockedOut)
            {
                // The knockout ended before an apex (refused late, frozen, teleported): let it go after a moment.
                if (now - _armedAt > 1.0) _armed = false;
                return;
            }

            float vy = mv.Velocity.y;
            if (vy > 0.5f) _wentUp = true;

            bool apex      = _wentUp && vy <= 0f;
            bool neverRose = !_wentUp && now - _armedAt > 0.35;                    // stun-in-place or a flat hit
            bool timedOut  = now - _armedAt > Plugin.KillZoneMaxRiseTime.Value;    // stuck under a ceiling
            if (apex || neverRose || timedOut) Fire(p, mv);
        }

        private static void Fire(PlayerInfo p, PlayerMovement mv)
        {
            _armed = false;
            Vector3 pos = p.ChestBone != null ? p.ChestBone.position : p.transform.position + Vector3.up;

            if (Plugin.VerboseLogging.Value) Plugin.Log.LogInfo($"STAR KO at {ShieldState.Percent:0}% (height {pos.y:0.0}).");

            PlayStar(p, pos);
            SbgNet.Send(SbgNet.Kind.StarKo, 0f);   // everyone else draws it too

            ShieldState.Percent = Mathf.Max(0f, Plugin.PercentAfterKillZoneDeath.Value);

            // Vanish on the spot. The camera keeps looking at where you were, which is
            // the pause that makes the death land. Then the normal respawn.
            float linger = Mathf.Max(0f, Plugin.KillZoneDeathLinger.Value);
            if (linger > 0.01f && Hide(mv))
            {
                _lingering = true;
                _respawnAt = Time.timeAsDouble + linger;
                return;
            }
            BeginRespawn(mv);
        }

        /// <summary>The flash, boom and shake, for any player at any position. Local star and remote star share it.</summary>
        private static void PlayStar(PlayerInfo p, Vector3 pos)
        {
            if (Plugin.KillZoneFlash.Value)
            {
                try { PlayFlash(pos, Skin.Of(p)); }
                catch (Exception e) { Plugin.Log.LogWarning("Kill flash failed: " + e.Message); }
            }
            if (Plugin.KillZoneBoom.Value)
            {
                try { RuntimeManager.PlayOneShot(GameManager.AudioSettings.ElectromagnetShieldExplosionEvent, pos); }
                catch (Exception e) { if (Plugin.VerboseLogging.Value) Plugin.Log.LogWarning("Kill boom failed: " + e.Message); }
                try { CameraModuleController.Shake(GameManager.CameraGameplaySettings.RocketExplosionScreenshakeSettings, pos, 1.6f, 1.4f); }
                catch { try { CameraModuleController.Shake(GameManager.CameraGameplaySettings.ElectromagnetExplosionScreenshakeSettings, pos); } catch { } }
            }
        }

        /// <summary>Another player's star KO, told to us over SbgNet. Their body vanishes on its own (isVisible is a SyncVar).</summary>
        internal static void PlayRemoteStar(PlayerInfo p)
        {
            if (p == null) return;
            Vector3 pos = p.ChestBone != null ? p.ChestBone.position : p.transform.position + Vector3.up;
            PlayStar(p, pos);
        }

        /// <summary>
        /// Hides the player through the same SyncVar the game uses while respawning.
        /// Its own visibility handler clears the knockout state and zeroes velocity,
        /// so the body stops where it died instead of tumbling on invisibly.
        /// </summary>
        private static bool Hide(PlayerMovement mv)
        {
            try { mv.NetworkisVisible = false; return true; }
            catch (Exception e) { Plugin.Log.LogWarning("Kill zone could not hide the player: " + e.Message); return false; }
        }

        private static void TickLinger()
        {
            var p = GameManager.LocalPlayerInfo;
            var mv = p != null ? p.Movement : null;
            if (mv == null) { _lingering = false; return; }

            if (mv.IsRespawningOrDrowning) { _lingering = false; return; } // something else took over

            // No per-frame re-assert here: LocalPlayerUpdateVisibilityPatch holds the
            // hidden state at the source instead, so we only wait.
            if (Time.timeAsDouble < _respawnAt) return;

            _lingering = false;
            BeginRespawn(mv);
        }

        private static void BeginRespawn(PlayerMovement mv)
        {
            ShieldState.KillZoneDeathPending = true; // OnRespawn must not also subtract on top

            bool ok = false;
            try { ok = mv.TryBeginRespawn(false, RespawnTarget.TeeOrCheckpoint, true); }
            catch (Exception e) { Plugin.Log.LogWarning("Kill respawn failed: " + e.Message); }
            if (!ok)
            {
                ShieldState.KillZoneDeathPending = false;
                Plugin.Log.LogWarning("Kill zone: the game refused the respawn (already respawning / match resolved).");
                try { mv.NetworkisVisible = true; } catch { }
            }
        }

        /// <summary>Match end, hole change or a plugin reload should not leave us invisible.</summary>
        internal static void CancelLinger()
        {
            if (!_lingering) return;
            _lingering = false;
            var mv = GameManager.LocalPlayerInfo != null ? GameManager.LocalPlayerInfo.Movement : null;
            if (mv != null) { try { mv.NetworkisVisible = true; } catch { } }
        }

        // ---- Flash VFX ---------------------------------------------------------

        /// <summary>
        /// The GameObject is an ordinary scene object, like the launch trails, so a
        /// scene load destroys it and this rebuilds it on demand. (It used to be
        /// DontDestroyOnLoad while the trails were not, which meant the two behaved
        /// differently across holes for no reason.) Materials and textures are not
        /// scene objects, so those are made once and kept.
        /// </summary>
        private static void EnsureFlash()
        {
            if (_flashGo != null) return;   // Unity-null-aware: false after a scene load

            if (_starTex == null) _starTex = MakeStarTexture(128);
            if (_dotTex  == null) _dotTex  = MakeDotTexture(32);
            if (_flashMat == null) _flashMat = LaunchVfx.MakeUnlitMaterial(_starTex, additive: true, intensity: Plugin.BitsGlow.Value);
            if (_sparkMat == null) _sparkMat = LaunchVfx.MakeUnlitMaterial(_dotTex,  additive: true, intensity: Plugin.BitsGlow.Value);
            if (_flashMat == null) return;

            _flashGo = new GameObject("SbgKillFlash");

            // The star: one particle, grows fast, holds, fades.
            _flashPs = _flashGo.AddComponent<ParticleSystem>();
            _flashPs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = _flashPs.main;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.loop = false; m.playOnAwake = false;
            m.duration = 0.1f;
            m.startLifetime = 0.55f;
            m.startSpeed = 0f;
            m.startSize = 1f;
            m.gravityModifier = 0f;
            m.maxParticles = 4;
            var em = _flashPs.emission; em.enabled = true; em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            var sh = _flashPs.shape; sh.enabled = false;
            var sol = _flashPs.sizeOverLifetime; sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.05f), new Keyframe(0.12f, 1f), new Keyframe(0.5f, 1.15f), new Keyframe(1f, 1.4f)));
            var col = _flashPs.colorOverLifetime; col.enabled = true;
            var rot = _flashPs.rotationOverLifetime; rot.enabled = true; rot.z = 0.9f;
            var fr = _flashGo.GetComponent<ParticleSystemRenderer>();
            fr.sharedMaterial = _flashMat; fr.renderMode = ParticleSystemRenderMode.Billboard;
            fr.shadowCastingMode = ShadowCastingMode.Off; fr.receiveShadows = false;

            // The bling: a ring of sparks thrown outward.
            var sparkGo = new GameObject("Sparks");
            sparkGo.transform.SetParent(_flashGo.transform, false);
            _sparkPs = sparkGo.AddComponent<ParticleSystem>();
            _sparkPs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var sm = _sparkPs.main;
            sm.simulationSpace = ParticleSystemSimulationSpace.World;
            sm.loop = false; sm.playOnAwake = false;
            sm.duration = 0.1f;
            sm.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
            sm.startSpeed = new ParticleSystem.MinMaxCurve(6f, 14f);
            sm.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
            sm.gravityModifier = 0.3f;
            sm.maxParticles = 64;
            var sem = _sparkPs.emission; sem.enabled = true; sem.rateOverTime = 0f;
            sem.SetBursts(new[] { new ParticleSystem.Burst(0f, 28) });
            var ssh = _sparkPs.shape; ssh.enabled = true; ssh.shapeType = ParticleSystemShapeType.Sphere; ssh.radius = 0.3f;
            var ssol = _sparkPs.sizeOverLifetime; ssol.enabled = true;
            ssol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f)));
            var scol = _sparkPs.colorOverLifetime; scol.enabled = true;
            var sr = sparkGo.GetComponent<ParticleSystemRenderer>();
            sr.sharedMaterial = _sparkMat; sr.renderMode = ParticleSystemRenderMode.Stretch;
            sr.lengthScale = 3f; sr.velocityScale = 0.05f;
            sr.shadowCastingMode = ShadowCastingMode.Off; sr.receiveShadows = false;
        }

        private static void PlayFlash(Vector3 pos, Color skin)
        {
            EnsureFlash();
            if (_flashGo == null) return;

            float size = Plugin.KillZoneFlashSize.Value;
            var m = _flashPs.main; m.startSize = size;

            // White core, skin-tinted tail.
            Color tint = Color.Lerp(Color.white, skin, 0.6f);
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 0.35f), new GradientColorKey(tint, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.4f), new GradientAlphaKey(0f, 1f) });
            var col = _flashPs.colorOverLifetime; col.color = new ParticleSystem.MinMaxGradient(g);

            var sg = new Gradient();
            sg.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(tint, 1f) },
                       new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            var scol = _sparkPs.colorOverLifetime; scol.color = new ParticleSystem.MinMaxGradient(sg);

            _flashGo.transform.position = pos;
            _flashPs.Play(true);
        }

        private static Texture2D MakeStarTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[size * size];
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - r) / r, dy = (y + 0.5f - r) / r;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float ang = Mathf.Atan2(dy, dx);
                // Four long points, four short ones between, plus a soft round glow.
                float four  = Mathf.Pow(Mathf.Abs(Mathf.Cos(2f * ang)), 12f);
                float eight = Mathf.Pow(Mathf.Abs(Mathf.Cos(4f * ang)), 20f) * 0.45f;
                float reach = 0.18f + 0.82f * Mathf.Max(four, eight);
                float star  = Mathf.Clamp01((reach - d) / 0.06f);
                float glow  = Mathf.Exp(-d * d * 9f) * 0.8f;
                float a = Mathf.Clamp01(star + glow);
                // Premultiplied: additive blending ignores alpha entirely, so the shape
                // has to live in RGB or the quad renders as a solid white box.
                px[y * size + x] = new Color(a, a, a, a);
            }
            tex.SetPixels(px); tex.Apply(true);
            return tex;
        }

        private static Texture2D MakeDotTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size];
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r)) / r;
                float a = Mathf.Clamp01(1f - d * d);
                px[y * size + x] = new Color(a, a, a, a); // premultiplied, see MakeStarTexture
            }
            tex.SetPixels(px); tex.Apply();
            return tex;
        }

        internal static void DestroyAll()
        {
            CancelLinger();
            if (_flashGo != null) UnityEngine.Object.Destroy(_flashGo);
            if (_flashMat != null) UnityEngine.Object.Destroy(_flashMat);
            if (_sparkMat != null) UnityEngine.Object.Destroy(_sparkMat);
            if (_starTex  != null) UnityEngine.Object.Destroy(_starTex);
            if (_dotTex   != null) UnityEngine.Object.Destroy(_dotTex);
            _flashGo = null; _flashPs = null; _sparkPs = null;
            _flashMat = null; _sparkMat = null; _starTex = null; _dotTex = null;
            _armed = false;
        }
    }

    /// <summary>
    /// While a star KO is playing out, the player must stay hidden. Rather than
    /// overwriting the visibility SyncVar every frame and fighting whatever set it,
    /// this holds the state where the game decides it: the game recomputes visibility
    /// from its own conditions, and we force the answer to "hidden" for the duration.
    /// </summary>
    [HarmonyPatch(typeof(PlayerMovement), "LocalPlayerUpdateVisibility")]
    internal static class LocalPlayerUpdateVisibilityPatch
    {
        [HarmonyPriority(Priority.Low)]   // can skip the original
        private static bool Prefix(PlayerMovement __instance)
        {
            if (!KillZone.IsLingering) return true;
            var local = GameManager.LocalPlayerInfo;
            if (local == null || !ReferenceEquals(__instance.PlayerInfo, local)) return true;
            try { if (__instance.IsVisible) __instance.NetworkisVisible = false; } catch { }
            return false;
        }
    }
}

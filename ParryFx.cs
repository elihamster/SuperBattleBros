using System;
using System.IO;
using FMODUnity;
using UnityEngine;
using UnityEngine.Rendering;

namespace SbgShields
{
    /// <summary>
    /// What a parry looks and sounds like, on every screen. The parrier's own machine calls
    /// Play from ResolveKnockout; everyone else's from the SbgNet Parry message, so the
    /// flash, the ring, the sound and the camera kick are the same thing everywhere.
    ///
    /// Before 0.7.23 the parrier got a 0.35 s tint flash that the 0.3 s linger usually cut
    /// short, and everyone else got a sound; the user asked for something the people
    /// around a parry could actually see. Now: the bubble goes hot and stays drawn for the
    /// flash, a ring of light in the parrier's colour bursts out of it with a spray of
    /// sparks, a sound plays at the bubble (a file shipped with the mod, or the game's own
    /// blocked-hit sting if it is missing), and nearby cameras get a short kick.
    /// </summary>
    internal static class ParryFx
    {
        internal static void Play(PlayerInfo p)
        {
            if (p == null) return;

            Vector3 pos;
            try
            {
                var col = p.ElectromagnetShieldCollider;
                pos = col != null ? col.transform.position : p.transform.position + Vector3.up * 0.9f;
            }
            catch { pos = p.transform.position + Vector3.up * 0.9f; }
            Color skin = Skin.Of(p);

            // The bubble itself goes hot, and stays drawn long enough to be seen doing it.
            try
            {
                ShieldTint.ParryFlash(p);
                if (Local.Is(p)) Plugin.ExtendLinger(Mathf.Max(0.05f, Plugin.ParryGlowDuration.Value) + 0.05f);
            }
            catch (Exception e) { if (Plugin.VerboseLogging.Value) Plugin.Log.LogWarning("Parry flash: " + e.Message); }

            if (Plugin.ParryBurst.Value)
            {
                try { Burst(pos, skin); }
                catch (Exception e) { Plugin.Log.LogWarning("Parry burst failed: " + e.Message); }
            }

            if (Plugin.PerfectParrySound.Value) PlaySound(pos);

            if (Plugin.ParryShake.Value)
            {
                try { CameraModuleController.Shake(GameManager.CameraGameplaySettings.ElectromagnetExplosionScreenshakeSettings, pos, 0.6f, 0.6f); }
                catch (Exception e) { if (Plugin.VerboseLogging.Value) Plugin.Log.LogWarning("Parry shake: " + e.Message); }
            }
        }

        // ---- The ring and the sparks ----------------------------------------------

        private static GameObject _go;
        private static ParticleSystem _ring, _sparks;
        private static Material _ringMat, _sparkMat;
        private static Texture2D _ringTex, _dotTex;

        private static void Ensure()
        {
            if (_go != null) return;   // Unity-null-aware: false after a scene load

            if (_ringTex == null) _ringTex = MakeRingTexture(128);
            if (_dotTex == null)  _dotTex  = MakeDotTexture(32);
            if (_ringMat == null) _ringMat  = LaunchVfx.MakeUnlitMaterial(_ringTex, additive: true, intensity: Plugin.BitsGlow.Value);
            if (_sparkMat == null) _sparkMat = LaunchVfx.MakeUnlitMaterial(_dotTex, additive: true, intensity: Plugin.BitsGlow.Value);
            if (_ringMat == null) return;

            _go = new GameObject("SbgParryBurst");

            // The ring: one billboard that grows fast and fades. Emitted per parry with
            // its own position, colour and size, so two parries close together both show.
            _ring = _go.AddComponent<ParticleSystem>();
            _ring.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var m = _ring.main;
            m.simulationSpace = ParticleSystemSimulationSpace.World;
            m.loop = false; m.playOnAwake = false;
            m.startLifetime = 0.4f;
            m.startSpeed = 0f;
            m.startSize = 5f;
            m.gravityModifier = 0f;
            m.maxParticles = 8;
            var em = _ring.emission; em.enabled = false;
            var sh = _ring.shape; sh.enabled = false;
            var sol = _ring.sizeOverLifetime; sol.enabled = true;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.3f), new Keyframe(0.25f, 0.8f), new Keyframe(1f, 1f)));
            var col = _ring.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            // Fades in RGB: additive blending ignores alpha, and black adds nothing.
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 0.3f), new GradientColorKey(Color.black, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);
            var rr = _go.GetComponent<ParticleSystemRenderer>();
            rr.sharedMaterial = _ringMat; rr.renderMode = ParticleSystemRenderMode.Billboard;
            rr.shadowCastingMode = ShadowCastingMode.Off; rr.receiveShadows = false;

            // The sparks: thrown outward from the bubble's surface.
            var sparkGo = new GameObject("Sparks");
            sparkGo.transform.SetParent(_go.transform, false);
            _sparks = sparkGo.AddComponent<ParticleSystem>();
            _sparks.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var sm = _sparks.main;
            sm.simulationSpace = ParticleSystemSimulationSpace.World;
            sm.loop = false; sm.playOnAwake = false;
            sm.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
            sm.startSpeed = new ParticleSystem.MinMaxCurve(5f, 11f);
            sm.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.4f);
            sm.gravityModifier = 0.4f;
            sm.maxParticles = 128;
            var sem = _sparks.emission; sem.enabled = false;
            var ssh = _sparks.shape; ssh.enabled = true; ssh.shapeType = ParticleSystemShapeType.Sphere; ssh.radius = 1.2f;
            var ssol = _sparks.sizeOverLifetime; ssol.enabled = true;
            ssol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f)));
            var scol = _sparks.colorOverLifetime; scol.enabled = true;
            var sg = new Gradient();
            sg.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.black, 1f) },
                       new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            scol.color = new ParticleSystem.MinMaxGradient(sg);
            var sr = sparkGo.GetComponent<ParticleSystemRenderer>();
            sr.sharedMaterial = _sparkMat; sr.renderMode = ParticleSystemRenderMode.Stretch;
            sr.lengthScale = 2.5f; sr.velocityScale = 0.04f;
            sr.shadowCastingMode = ShadowCastingMode.Off; sr.receiveShadows = false;
        }

        private static void Burst(Vector3 pos, Color skin)
        {
            Ensure();
            if (_go == null) return;

            Color tint = Color.Lerp(Color.white, skin, 0.7f);
            tint.a = 1f;

            var ring = new ParticleSystem.EmitParams
            {
                position = pos,
                startColor = tint,
                startSize = Mathf.Max(1f, Plugin.ParryBurstSize.Value),
                startLifetime = 0.4f,
                applyShapeToPosition = false,
            };
            _ring.Emit(ring, 1);

            _sparks.transform.position = pos;
            var spark = new ParticleSystem.EmitParams { startColor = tint, applyShapeToPosition = true };
            _sparks.Emit(spark, 24);
        }

        private static Texture2D MakeRingTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[size * size];
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r)) / r;
                // A soft ring near the edge with a faint glow filling the middle.
                float ring = Mathf.Exp(-Mathf.Pow((d - 0.8f) / 0.07f, 2f));
                float fill = Mathf.Exp(-d * d * 5f) * 0.2f;
                float a = Mathf.Clamp01(ring + fill);
                px[y * size + x] = new Color(a, a, a, a);   // premultiplied, see KillZone.MakeStarTexture
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
                px[y * size + x] = new Color(a, a, a, a);
            }
            tex.SetPixels(px); tex.Apply();
            return tex;
        }

        // ---- The sound ------------------------------------------------------------
        // A plain audio file through FMOD's core API, the same engine the game's own
        // sounds go through (Unity's audio may not be enabled in an FMOD game). Loaded
        // once, played 3D at the bubble, loud within 6 m and gone by 80.

        private static FMOD.Sound _sound;
        private static bool _loaded, _warnedLoad, _warnedPlay;
        private static string _loadedPath;

        private static void PlaySound(Vector3 pos)
        {
            if (TryLoad(out var snd))
            {
                try
                {
                    var sys = RuntimeManager.CoreSystem;
                    sys.getMasterChannelGroup(out var group);
                    var r = sys.playSound(snd, group, true, out var ch);
                    if (r == FMOD.RESULT.OK)
                    {
                        var fp = pos.ToFMODVector();
                        var fv = Vector3.zero.ToFMODVector();
                        ch.set3DAttributes(ref fp, ref fv);
                        ch.setVolume(Mathf.Clamp(Plugin.ParrySoundVolume.Value, 0f, 2f));
                        ch.setPaused(false);
                        return;
                    }
                    if (!_warnedPlay) { _warnedPlay = true; Plugin.Log.LogWarning($"Parry sound: playSound returned {r}; using the game's sting instead."); }
                }
                catch (Exception e) { if (!_warnedPlay) { _warnedPlay = true; Plugin.Log.LogWarning("Parry sound: " + e.Message + "; using the game's sting instead."); } }
            }

            // The game's own "your immunity refused that knockout" sting: already means
            // "that hit did not land", which is the read we want.
            try { RuntimeManager.PlayOneShot(GameManager.AudioSettings.KnockoutImmunityBlockedKnockoutEvent, pos); }
            catch (Exception e) { if (Plugin.VerboseLogging.Value) Plugin.Log.LogWarning("Parry sting: " + e.Message); }
        }

        private static bool TryLoad(out FMOD.Sound sound)
        {
            sound = _sound;
            string name = (Plugin.ParrySoundFile.Value ?? "").Trim();
            if (name.Length == 0) return false;

            string path = Resolve(name);
            if (path == null)
            {
                if (!_warnedLoad) { _warnedLoad = true; Plugin.Log.LogInfo($"Parry sound: no '{name}' next to the mod or in its sounds folder; using the game's sting."); }
                return false;
            }
            if (_loaded && path == _loadedPath) return true;

            Release();
            try
            {
                var mode = FMOD.MODE._3D | FMOD.MODE.LOOP_OFF | FMOD.MODE.CREATESAMPLE | FMOD.MODE._3D_LINEARSQUAREROLLOFF;
                var r = RuntimeManager.CoreSystem.createSound(path, mode, out _sound);
                if (r != FMOD.RESULT.OK)
                {
                    if (!_warnedLoad) { _warnedLoad = true; Plugin.Log.LogWarning($"Parry sound: could not load '{path}' ({r}); using the game's sting."); }
                    return false;
                }
                _sound.set3DMinMaxDistance(6f, 80f);
                _loaded = true; _loadedPath = path; sound = _sound;
                Plugin.Log.LogInfo($"Parry sound: {path}");
                return true;
            }
            catch (Exception e)
            {
                if (!_warnedLoad) { _warnedLoad = true; Plugin.Log.LogWarning($"Parry sound: {e.GetType().Name}: {e.Message}; using the game's sting."); }
                return false;
            }
        }

        /// <summary>Next to the DLL first, then in its sounds\ subfolder.</summary>
        private static string Resolve(string name)
        {
            string dir = Plugin.PluginDirectory;
            if (string.IsNullOrEmpty(dir)) return null;
            foreach (var candidate in new[] { Path.Combine(dir, name), Path.Combine(dir, "sounds", name) })
                if (File.Exists(candidate)) return candidate;
            return null;
        }

        private static void Release()
        {
            if (!_loaded) return;
            try { _sound.release(); } catch { }
            _loaded = false; _loadedPath = null;
        }

        internal static void DestroyAll()
        {
            Release();
            if (_go != null) UnityEngine.Object.Destroy(_go);
            if (_ringMat != null) UnityEngine.Object.Destroy(_ringMat);
            if (_sparkMat != null) UnityEngine.Object.Destroy(_sparkMat);
            if (_ringTex != null) UnityEngine.Object.Destroy(_ringTex);
            if (_dotTex != null) UnityEngine.Object.Destroy(_dotTex);
            _go = null; _ringMat = null; _sparkMat = null; _ringTex = null; _dotTex = null;
        }
    }
}

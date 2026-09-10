using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace SbgShields
{
    /// <summary>
    /// Our own puffy smoke trail, fully skin-coloured, attached to any player who is
    /// knocked out and moving fast. One emitter per player, built once and reused.
    /// Works for remote players too: knockout state is a SyncVar and velocity replicates.
    /// </summary>
    internal static class LaunchVfx
    {
        private class Trail
        {
            public GameObject Go;
            public ParticleSystem Ps;
            public ParticleSystemRenderer Renderer;
            public Material Material;
            public bool Emitting;
            public double StartedAt;
        }

        private static readonly Dictionary<PlayerInfo, Trail> _trails = new Dictionary<PlayerInfo, Trail>();
        private static readonly List<PlayerInfo> _scratch = new List<PlayerInfo>();
        private static readonly List<PlayerInfo> _dead = new List<PlayerInfo>();
        private static Texture2D _puffTex;

        // ---- Shared helpers (also used by KillZone) ----------------------------

        private static Shader _shader;
        private static bool _shaderSearched;

        internal static Shader FindShader()
        {
            if (!_shaderSearched)
            {
                _shaderSearched = true;
                _shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                       ?? Shader.Find("Universal Render Pipeline/Unlit")
                       ?? Shader.Find("Sprites/Default");
                if (_shader != null) Plugin.Log.LogInfo("Trail shader: " + _shader.name);
                else Plugin.Log.LogWarning("No shader for the launch trail / kill flash.");
            }
            return _shader;
        }

        /// <summary>Transparent unlit particle material around a texture. additive = glow blend.</summary>
        internal static Material MakeUnlitMaterial(Texture2D tex, bool additive)
        {
            var sh = FindShader();
            if (sh == null) return null;
            var mat = new Material(sh);
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend"))   mat.SetFloat("_Blend", additive ? 2f : 0f);
            if (mat.HasProperty("_SrcBlend")) mat.SetInt("_SrcBlend", (int)(additive ? BlendMode.One : BlendMode.SrcAlpha));
            if (mat.HasProperty("_DstBlend")) mat.SetInt("_DstBlend", (int)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            if (mat.HasProperty("_ZWrite"))  mat.SetInt("_ZWrite", 0);
            if (mat.HasProperty("_ColorMode")) mat.SetFloat("_ColorMode", additive ? 1f : 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.renderQueue = (int)RenderQueue.Transparent;
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            else mat.mainTexture = tex;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            else mat.color = Color.white;
            return mat;
        }

        internal static void Tick()
        {
            RageTick();

            if (!Plugin.LaunchTrail.Value)
            {
                if (_trails.Count > 0) StopAll();
                return;
            }

            _scratch.Clear();
            var local = GameManager.LocalPlayerInfo;
            if (local != null) _scratch.Add(local);
            try { var r = GameManager.RemotePlayers; if (r != null) _scratch.AddRange(r); } catch { }

            _dead.Clear();
            foreach (var kv in _trails)
                if (kv.Key == null || !_scratch.Contains(kv.Key)) _dead.Add(kv.Key);
            foreach (var k in _dead) { Destroy(_trails[k]); _trails.Remove(k); }

            double now = Time.timeAsDouble;
            foreach (var p in _scratch)
            {
                if (p == null) continue;

                bool tumbling = false;
                float speed = 0f;
                try
                {
                    var mv = p.Movement;
                    tumbling = mv != null && mv.IsKnockedOutOrRecovering && !mv.IsGrounded;
                    // A remote player's rigidbody is driven by interpolation and reports no
                    // velocity of its own; the game syncs one separately. Reading the local
                    // one for everyone is why nobody else's trail ever appeared.
                    speed = mv == null ? 0f : (ReferenceEquals(p, local) ? mv.Velocity : mv.SyncedVelocity).magnitude;
                }
                catch { }

                // Percent gate: only our own percent is known, so remote players keep
                // the speed-only rule until pips/percent are synced.
                bool percentOk = !ReferenceEquals(p, local) ||
                                 !Plugin.PercentEnabled.Value ||
                                 ShieldState.Percent >= Plugin.LaunchTrailMinPercent.Value ||
                                 KillZone.IsArmed;

                _trails.TryGetValue(p, out var t);

                if (t == null || !t.Emitting)
                {
                    if (tumbling && percentOk && speed >= Plugin.LaunchTrailStartSpeed.Value)
                    {
                        if (t == null) { t = Create(p); if (t == null) continue; _trails[p] = t; }
                        Start(t, p);
                        t.StartedAt = now;
                    }
                    continue;
                }

                // Follow the chest so the puffs spawn from the body, not the feet.
                var anchor = p.ChestBone != null ? p.ChestBone : p.transform;
                t.Go.transform.position = anchor.position;

                // Everyone's trail now runs on the same rule: tumbling, and still fast
                // enough. It stops when you stop being knocked out; with
                // StayDownUntilLanding on that is the landing, so the smoke runs the
                // whole arc and reads as "still stunned".
                bool keep = tumbling && speed >= Plugin.LaunchTrailStopSpeed.Value;
                if (!keep && now - t.StartedAt > 0.15) Stop(t);
            }
        }

        private static Trail Create(PlayerInfo p)
        {
            try
            {
                if (_puffTex == null) _puffTex = MakePuffTexture(64);
                if (FindShader() == null) return null;

                var go = new GameObject("SbgLaunchTrail");
                go.transform.SetParent(null, false);
                var ps = go.AddComponent<ParticleSystem>();
                ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

                var main = ps.main;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.loop = true;
                main.playOnAwake = false;
                main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 0.9f);
                main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.gravityModifier = -0.03f;
                main.maxParticles = 800;

                var emission = ps.emission;
                emission.enabled = true;

                var shape = ps.shape;
                shape.enabled = true;
                shape.shapeType = ParticleSystemShapeType.Sphere;
                shape.radius = 0.45f;

                // Clouds keep swelling as they thin out; they never shrink back to a dot.
                var sol = ps.sizeOverLifetime;
                sol.enabled = true;
                var grow = new AnimationCurve(new Keyframe(0f, 0.45f), new Keyframe(0.25f, 0.9f), new Keyframe(1f, 1.25f));
                sol.size = new ParticleSystem.MinMaxCurve(1f, grow);

                var col = ps.colorOverLifetime;
                col.enabled = true;

                var rot = ps.rotationOverLifetime;
                rot.enabled = true;
                rot.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);

                var r = go.GetComponent<ParticleSystemRenderer>();
                var mat = MakeUnlitMaterial(_puffTex, additive: false);
                if (mat == null) { UnityEngine.Object.Destroy(go); return null; }

                r.sharedMaterial = mat;
                r.renderMode = ParticleSystemRenderMode.Billboard;
                r.sortMode = ParticleSystemSortMode.Distance;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;

                return new Trail { Go = go, Ps = ps, Renderer = r, Material = mat };
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Launch trail create failed: " + e.Message);
                return null;
            }
        }

        private static void Start(Trail t, PlayerInfo p)
        {
            Color c = Skin.Of(p);
            Color lighter = Color.Lerp(c, Color.white, 0.5f);
            float a = Mathf.Clamp01(Plugin.LaunchTrailAlpha.Value);

            var main = t.Ps.main;
            float life = Plugin.LaunchTrailLifetime.Value;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.75f, life);
            main.startSize = new ParticleSystem.MinMaxCurve(Plugin.LaunchTrailSize.Value * 0.7f, Plugin.LaunchTrailSize.Value);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(lighter.r, lighter.g, lighter.b, a), new Color(c.r, c.g, c.b, a));

            var emission = t.Ps.emission;
            emission.rateOverTime = Plugin.LaunchTrailRate.Value;
            emission.rateOverDistance = Plugin.LaunchTrailRatePerMetre.Value;

            // Stay dense for most of the life, then fade out over the last third.
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(lighter, 0f), new GradientColorKey(c, 0.45f), new GradientColorKey(c * 0.85f, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.9f, 0.55f), new GradientAlphaKey(0.35f, 0.85f), new GradientAlphaKey(0f, 1f) });
            var col = t.Ps.colorOverLifetime;
            col.color = new ParticleSystem.MinMaxGradient(g);

            var anchor = p.ChestBone != null ? p.ChestBone : p.transform;
            t.Go.transform.position = anchor.position;
            t.Go.SetActive(true);
            t.Ps.Play(true);
            t.Emitting = true;
        }

        private static void Stop(Trail t)
        {
            if (t == null || !t.Emitting) return;
            t.Emitting = false;
            try { t.Ps.Stop(true, ParticleSystemStopBehavior.StopEmitting); } catch { }
        }

        private static void Destroy(Trail t)
        {
            if (t == null) return;
            if (t.Material != null) UnityEngine.Object.Destroy(t.Material);
            if (t.Go != null) UnityEngine.Object.Destroy(t.Go);
        }

        internal static void StopAll()
        {
            foreach (var kv in _trails) Destroy(kv.Value);
            _trails.Clear();
        }

        /// <summary>Plugin unload: drop the generated texture too, not just the emitters.</summary>
        internal static void DestroyAll()
        {
            StopAll();
            DestroyRage();
            if (_puffTex != null) { UnityEngine.Object.Destroy(_puffTex); _puffTex = null; }
            _shader = null;
            _shaderSearched = false;
        }

        // ---- Rage embers -------------------------------------------------------
        // Smash's rage is the attacker hitting harder at high percent; here the
        // victim's own percent already does that job. What was missing was the LOOK:
        // a player at 150% should read as one. Embers rise off the chest from
        // RageVisualMinPercent, thicker and redder toward the kill line. Local only,
        // because only this client knows this percent.

        private static GameObject _rageGo;
        private static ParticleSystem _ragePs;
        private static Material _rageMat;
        private static Texture2D _emberTex;
        private static bool _rageOn;

        private static void RageTick()
        {
            var p = GameManager.LocalPlayerInfo;
            bool want = p != null && Plugin.RageVisual.Value && Plugin.PercentEnabled.Value && ShieldState.InPlayableHole &&
                        ShieldState.Percent >= Plugin.RageVisualMinPercent.Value && !KillZone.IsLingering;
            try { if (want && p.Movement != null && (p.Movement.IsRespawningOrDrowning || !p.Movement.IsVisible)) want = false; } catch { }

            if (!want)
            {
                if (_rageOn)
                {
                    _rageOn = false;
                    try { if (_ragePs != null) _ragePs.Stop(true, ParticleSystemStopBehavior.StopEmitting); } catch { }
                }
                return;
            }

            if (_rageGo == null && !CreateRage()) return;

            float min = Plugin.RageVisualMinPercent.Value;
            float t = Mathf.InverseLerp(min, Mathf.Max(min + 1f, Plugin.KillPercent.Value), ShieldState.Percent);

            var em = _ragePs.emission;
            em.rateOverTime = Mathf.Lerp(10f, 45f, t);
            var main = _ragePs.main;
            Color hot  = Color.Lerp(new Color(1f, 0.62f, 0.18f), new Color(1f, 0.15f, 0.06f), t);
            Color core = new Color(1f, 0.88f, 0.4f);
            main.startColor = new ParticleSystem.MinMaxGradient(hot, core);

            var anchor = p.ChestBone != null ? p.ChestBone : p.transform;
            _rageGo.transform.position = anchor.position;
            if (!_rageOn) { _rageOn = true; _ragePs.Play(true); }
        }

        private static bool CreateRage()
        {
            try
            {
                if (_emberTex == null) _emberTex = MakeEmberTexture(32);
                if (_rageMat == null) _rageMat = MakeUnlitMaterial(_emberTex, additive: true);
                if (_rageMat == null) return false;

                _rageGo = new GameObject("SbgRageEmbers");
                _ragePs = _rageGo.AddComponent<ParticleSystem>();
                _ragePs.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

                var main = _ragePs.main;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.loop = true; main.playOnAwake = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.9f);
                main.startSpeed    = new ParticleSystem.MinMaxCurve(0.6f, 1.6f);
                main.startSize     = new ParticleSystem.MinMaxCurve(0.1f, 0.26f);
                main.gravityModifier = -0.15f;             // embers drift up
                main.maxParticles = 200;

                var shape = _ragePs.shape;
                shape.enabled = true; shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = 0.45f;

                // Unity insists all three axes share a curve mode, or it logs
                // "Particle Velocity curves must all be in the same mode" every frame
                // from every worker thread. Two-constants on all three.
                var vel = _ragePs.velocityOverLifetime;
                vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
                vel.x = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
                vel.y = new ParticleSystem.MinMaxCurve(0.8f, 1.8f);
                vel.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);

                var sol = _ragePs.sizeOverLifetime; sol.enabled = true;
                sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f)));

                var col = _ragePs.colorOverLifetime; col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                          new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
                col.color = new ParticleSystem.MinMaxGradient(g);

                var r = _rageGo.GetComponent<ParticleSystemRenderer>();
                r.sharedMaterial = _rageMat;
                r.renderMode = ParticleSystemRenderMode.Billboard;
                r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false;
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Rage embers create failed: " + e.Message);
                return false;
            }
        }

        private static void DestroyRage()
        {
            _rageOn = false;
            if (_rageGo != null) UnityEngine.Object.Destroy(_rageGo);
            if (_rageMat != null) UnityEngine.Object.Destroy(_rageMat);
            if (_emberTex != null) UnityEngine.Object.Destroy(_emberTex);
            _rageGo = null; _ragePs = null; _rageMat = null; _emberTex = null;
        }

        private static Texture2D MakeEmberTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color[size * size];
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r)) / r;
                float a = Mathf.Clamp01(1f - d * d);
                px[y * size + x] = new Color(a, a, a, a);   // premultiplied for additive, see KillZone
            }
            tex.SetPixels(px); tex.Apply();
            return tex;
        }

        private static Texture2D MakePuffTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[size * size];
            float r = size * 0.5f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f - r) / r, dy = (y + 0.5f - r) / r;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                // Lumpy-edged blob with a solid core: overlapping puffs build up into a
                // cloud instead of a haze of soft dots.
                float ang = Mathf.Atan2(dy, dx);
                float wobble = 0.10f * Mathf.Sin(ang * 5f) + 0.07f * Mathf.Sin(ang * 3f + 1.3f) + 0.04f * Mathf.Sin(ang * 8f + 0.4f);
                float edge = 0.90f + wobble;
                float a = Mathf.Clamp01((edge - d) / (edge * 0.55f)); // flat centre, falls off over the outer 55%
                a = a * a * (3f - 2f * a);
                px[y * size + x] = new Color(1f, 1f, 1f, a);
            }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }
    }
}

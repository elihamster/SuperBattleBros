using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

namespace SbgShields
{
    /// <summary>
    /// A soft ring of the bubble's colour drawn around every active bubble, on every
    /// screen. The bubble itself is pushed past white-point (BubbleGlow) so that where
    /// the game runs bloom it glows on its own; this halo is the part of the glow that
    /// does not depend on bloom, and it follows the bubble exactly: thinner as pips go,
    /// hot on a parry or the last-circle blink, gone with the body.
    ///
    /// One camera-facing quad per bubble, additive, no collider, built from a radial
    /// texture whose peak sits just outside the sphere. Colour comes from
    /// ShieldTint.CurrentColour so the two can never disagree.
    /// </summary>
    internal static class BubbleHalo
    {
        private class Halo
        {
            public GameObject Go;
            public MeshRenderer Renderer;
            public MaterialPropertyBlock Block;
            public Color Last;
        }

        private static readonly Dictionary<PlayerInfo, Halo> _halos = new Dictionary<PlayerInfo, Halo>();
        private static readonly List<PlayerInfo> _dead = new List<PlayerInfo>();
        private static Material _mat;
        private static Texture2D _tex;
        private static string _colorProp = "_BaseColor";
        private static bool _matFailed;

        internal static void Tick()
        {
            bool on = Plugin.BubbleHalo.Value && ShieldTint.Enabled;
            if (!on)
            {
                if (_halos.Count > 0) RemoveAll();
                return;
            }

            try
            {
                Touch(GameManager.LocalPlayerInfo);
                var remote = GameManager.RemotePlayers;
                if (remote != null) foreach (var p in remote) Touch(p);
            }
            catch { }

            // Bubbles that went down, players that left: drop their halo.
            _dead.Clear();
            foreach (var kv in _halos)
            {
                bool active = false;
                try { active = kv.Key != null && kv.Key.IsElectromagnetShieldActive; } catch { }
                if (!active || kv.Value.Go == null) _dead.Add(kv.Key);
            }
            foreach (var p in _dead) Remove(p);

            LogBloomOnce();
        }

        private static void Touch(PlayerInfo p)
        {
            if (p == null) return;
            bool active;
            try { active = p.IsElectromagnetShieldActive; } catch { return; }
            if (!active) return;
            var col = p.ElectromagnetShieldCollider;
            if (col == null) return;

            if (!_halos.TryGetValue(p, out var h))
            {
                h = Create();
                if (h == null) return;
                _halos[p] = h;
            }
            if (h.Go == null) { _halos.Remove(p); return; }

            float radius = col.radius * Mathf.Max(col.transform.lossyScale.x, col.transform.lossyScale.y, col.transform.lossyScale.z);
            var t = h.Go.transform;
            t.position = col.transform.position;
            var cam = GameManager.Camera;
            if (cam == null) cam = Camera.main;
            if (cam != null) t.rotation = Quaternion.LookRotation(t.position - cam.transform.position);   // the quad's face is -Z
            t.localScale = Vector3.one * (radius * 2f * Mathf.Max(1f, Plugin.BubbleHaloSize.Value));

            // Match the bubble, then fold its thinness into brightness (additive ignores alpha).
            var c = ShieldTint.CurrentColour(p);
            float k = Mathf.Max(0f, Plugin.BubbleHaloStrength.Value) * Mathf.Max(0.1f, Plugin.BitsGlow.Value) * Mathf.Clamp01(c.a);
            var glow = new Color(c.r * k, c.g * k, c.b * k, 1f);
            if (glow != h.Last)
            {
                h.Last = glow;
                h.Block.SetColor(_colorProp, glow);
                h.Renderer.SetPropertyBlock(h.Block);
            }
        }

        private static Halo Create()
        {
            if (_matFailed) return null;
            try
            {
                if (_tex == null) _tex = MakeHaloTexture(128);
                if (_mat == null)
                {
                    _mat = LaunchVfx.MakeUnlitMaterial(_tex, additive: true);
                    if (_mat == null) { _matFailed = true; return null; }
                    _colorProp = _mat.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
                }

                var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = "SbgBubbleHalo";
                var mc = go.GetComponent<Collider>();
                if (mc != null) UnityEngine.Object.Destroy(mc);   // a primitive comes with a collider; the mine must not see it
                var r = go.GetComponent<MeshRenderer>();
                r.sharedMaterial = _mat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                return new Halo { Go = go, Renderer = r, Block = new MaterialPropertyBlock(), Last = new Color(-1f, 0f, 0f, 0f) };
            }
            catch (Exception e)
            {
                _matFailed = true;
                Plugin.Log.LogWarning("Bubble halo failed: " + e.Message);
                return null;
            }
        }

        /// <summary>Faint inside the sphere, brightest just past its edge, gone by the quad's edge.</summary>
        private static Texture2D MakeHaloTexture(int size)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[size * size];
            float r = size * 0.5f;
            float rim = 1f / Mathf.Max(1f, Plugin.BubbleHaloSize.Value);   // where the sphere's edge falls on the quad, 0..1
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r)) / r;
                float a;
                if (d <= rim) a = 0.12f + 0.38f * Mathf.Pow(d / rim, 3f);                       // gentle fill rising to the rim
                else           a = 0.5f * Mathf.Exp(-Mathf.Pow((d - rim) / ((1f - rim) * 0.45f), 2f));   // soft falloff outward
                if (d > 1f) a = 0f;
                px[y * size + x] = new Color(a, a, a, a);   // premultiplied: additive blending reads RGB
            }
            tex.SetPixels(px); tex.Apply(true);
            return tex;
        }

        private static void Remove(PlayerInfo p)
        {
            if (_halos.TryGetValue(p, out var h) && h.Go != null) UnityEngine.Object.Destroy(h.Go);
            _halos.Remove(p);
        }

        private static void RemoveAll()
        {
            foreach (var kv in _halos) if (kv.Value.Go != null) UnityEngine.Object.Destroy(kv.Value.Go);
            _halos.Clear();
        }

        internal static void DestroyAll()
        {
            RemoveAll();
            if (_mat != null) UnityEngine.Object.Destroy(_mat);
            if (_tex != null) UnityEngine.Object.Destroy(_tex);
            _mat = null; _tex = null; _matFailed = false;
        }

        // ---- Diagnostics -------------------------------------------------------------
        // Whether the game's post-processing has bloom on decides whether BubbleGlow and
        // BitsGlow read as a glow or just as brighter. Read once through reflection (the
        // render-pipeline assemblies are not referenced) and logged, so tuning is not a guess.

        private static bool _bloomLogged;
        private static double _firstTick = -1;

        private static void LogBloomOnce()
        {
            if (_bloomLogged) return;
            if (_firstTick < 0) { _firstTick = Time.timeAsDouble; return; }
            if (Time.timeAsDouble - _firstTick < 3.0) return;   // let the pipeline settle
            _bloomLogged = true;
            try
            {
                var vmType    = Type.GetType("UnityEngine.Rendering.VolumeManager, Unity.RenderPipelines.Core.Runtime");
                var bloomType = Type.GetType("UnityEngine.Rendering.Universal.Bloom, Unity.RenderPipelines.Universal.Runtime");
                if (vmType == null || bloomType == null) { Plugin.Log.LogInfo("Post-processing: URP volume types not found; cannot tell if bloom is on."); return; }
                var instance = vmType.GetProperty("instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
                var stack    = vmType.GetProperty("stack", BindingFlags.Public | BindingFlags.Instance)?.GetValue(instance);
                if (stack == null) { Plugin.Log.LogInfo("Post-processing: no volume stack yet; cannot tell if bloom is on."); return; }
                MethodInfo getComponent = null;
                foreach (var m in stack.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
                    if (m.Name == "GetComponent" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0) { getComponent = m; break; }
                var bloom = getComponent?.MakeGenericMethod(bloomType).Invoke(stack, null);
                if (bloom == null) { Plugin.Log.LogInfo("Post-processing: no bloom component in the volume stack; the glow is brightness only."); return; }
                bool active = false;
                try { active = (bool)bloomType.GetMethod("IsActive", BindingFlags.Public | BindingFlags.Instance).Invoke(bloom, null); } catch { }
                string intensity = "?";
                try
                {
                    var param = bloomType.GetField("intensity", BindingFlags.Public | BindingFlags.Instance)?.GetValue(bloom);
                    var value = param?.GetType().GetProperty("value")?.GetValue(param);
                    if (value != null) intensity = Convert.ToSingle(value).ToString("0.00");
                }
                catch { }
                Plugin.Log.LogInfo(active
                    ? $"Post-processing: bloom is on (intensity {intensity}); BubbleGlow and BitsGlow read as a glow."
                    : "Post-processing: bloom is off; BubbleGlow and BitsGlow only brighten. The halo carries the glow.");
            }
            catch (Exception e) { Plugin.Log.LogInfo("Post-processing: could not read the volume stack (" + e.GetType().Name + ")."); }
        }
    }
}

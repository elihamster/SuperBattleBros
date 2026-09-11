using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace SbgShields
{
    /// <summary>Skin colour lookup, used by the tint, the trail and the HUD.</summary>
    internal static class Skin
    {
        internal static Color Of(PlayerInfo p)
        {
            try
            {
                var settings = GameManager.PlayerCosmeticsSettings;
                var cos = p.Cosmetics;
                if (settings != null && cos != null && settings.skinColors != null)
                {
                    int i = cos.NetworkskinColorIndex;
                    if (i >= 0 && i < settings.skinColors.Length)
                    {
                        // The game forces alpha to 1 before it uses this colour
                        // (PlayerCosmeticsSwitcher.ApplyCurrentSkinColorToMaterial); the
                        // stored value can carry any alpha. Drawn raw, a skin with alpha 0
                        // gave an invisible HUD icon and invisible pips.
                        var c = settings.skinColors[i].baseColor;
                        c.a = 1f;
                        return c;
                    }
                }
            }
            catch { }
            return Color.white;
        }
    }

    /// <summary>
    /// Recolours the vanilla shield VFX (activation, hold, dissolve, hit sparks,
    /// break) to the owner's skin colour, for OUR Shift shield only.
    ///
    /// How: every one of those effects is set up by the game with
    /// <c>TeamColorVfxHandler.SetTeam(team)</c> immediately before <c>Play()</c>.
    /// We arm a colour in a prefix on the game method that does that, and a
    /// postfix on SetTeam retints the freshly-teamed particle systems. That puts
    /// the tint in place before the first particle is emitted, and it recolours
    /// the authored gradients key by key (alpha and brightness curves kept), so
    /// the intro/fade animation plays exactly as authored, just in a new hue.
    /// If the effect has no TeamColorVfxHandler, the hook postfix tints after Play.
    ///
    /// Materials on the persistent bubble are forced by BubbleVfxMaterialHandler
    /// every frame, so those go through BubbleMaterialTintPatch instead.
    /// </summary>
    internal static class ShieldTint
    {
        private static Color? _armed;
        private static bool   _consumed;

        private static readonly List<ParticleSystem> _systems = new List<ParticleSystem>();
        private static readonly List<ParticleSystemRenderer> _renderers = new List<ParticleSystemRenderer>();
        private static readonly List<BubbleVfxMaterialHandler> _handlers = new List<BubbleVfxMaterialHandler>();
        private static readonly HashSet<string> _dumped = new HashSet<string>();

        private class Instanced { public Material Original, Instance; }
        private static readonly Dictionary<ParticleSystemRenderer, Instanced> _instances = new Dictionary<ParticleSystemRenderer, Instanced>();
        private static readonly List<ParticleSystemRenderer> _sweep = new List<ParticleSystemRenderer>();
        private static bool   _restored = true;   // materials currently handed back to the game
        private static double _lastSweep;
        private static double _lastAnyActive = double.MinValue;

        /// <summary>Any player's shield up right now. The pooled materials go back to the game only once none is.</summary>
        private static bool AnyShieldActive()
        {
            try
            {
                var local = GameManager.LocalPlayerInfo;
                if (local != null && local.IsElectromagnetShieldActive) return true;
                var remote = GameManager.RemotePlayers;
                if (remote != null)
                    foreach (var p in remote)
                        if (p != null && p.IsElectromagnetShieldActive) return true;
            }
            catch { }
            return false;
        }

        /// <summary>The player whose shield this VFX hangs under, or null.</summary>
        internal static PlayerInfo OwnerOf(Transform t)
        {
            if (t == null) return null;
            try
            {
                var direct = t.GetComponentInParent<PlayerInfo>();
                if (direct != null) return direct;
                var local = GameManager.LocalPlayerInfo;
                if (local != null && local.ElectromagnetShieldCollider != null && t.IsChildOf(local.ElectromagnetShieldCollider.transform)) return local;
                var remote = GameManager.RemotePlayers;
                if (remote != null)
                    foreach (var p in remote)
                        if (p != null && p.ElectromagnetShieldCollider != null && t.IsChildOf(p.ElectromagnetShieldCollider.transform)) return p;
            }
            catch { }
            return null;
        }

        // ---- Arming ------------------------------------------------------------

        internal static bool Enabled => Plugin.TintVanillaShield.Value;

        /// <summary>
        /// Whose bubbles get tinted: everyone's. Every player in a lobby that passed the
        /// handshake runs this mod, and from another machine a Shift bubble and a magnet
        /// item's shield are the same SyncVar, so the item is tinted too. Until 0.7.11
        /// only the local Shift bubble was, which is why a friend's bubble stayed blue.
        /// </summary>
        internal static bool IsOurs(PlayerInfo p) => p != null;

        internal static void Arm(PlayerInfo p)
        {
            _armed = null; _consumed = false;
            if (!Enabled || !IsOurs(p)) return;
            _armed = BubbleColour(p);
        }

        // ---- Bubble state, visible to everyone ----------------------------------
        // A bubble looks like what it has left: its skin colour at full pips, getting
        // THINNER (alpha) and only slightly lighter as they go, and a hot saturated
        // flash the instant one is lost. Ours reads ShieldState; everyone else's reads
        // the pip fraction SbgNet carries, so the attacker sees the bubble weaken as
        // they chip it. Three rules learned the hard way: never darker (reads as a
        // black stain, 0.7.8 and 0.7.16), never all the way to white (every colour
        // ends up the same, 0.7.20), and the colour must stay recognisable at one pip.

        private static readonly Dictionary<PlayerInfo, float>  _lastFraction = new Dictionary<PlayerInfo, float>();
        private static readonly Dictionary<PlayerInfo, double> _crackUntil   = new Dictionary<PlayerInfo, double>();
        private static readonly Dictionary<PlayerInfo, Color>  _lastApplied  = new Dictionary<PlayerInfo, Color>();
        private const double CrackFlash = 0.15;

        private static float FractionOf(PlayerInfo p)
        {
            if (Local.Is(p)) return ShieldState.PipFraction;
            return SbgNet.TryGetPipFraction(p, out float f) ? f : 1f;
        }

        /// <summary>The colour this player's bubble should be right now.</summary>
        internal static Color BubbleColour(PlayerInfo p)
        {
            var skin = Skin.Of(p);
            if (p == null) return skin;
            double now = Time.timeAsDouble;

            float f = FractionOf(p);
            if (_lastFraction.TryGetValue(p, out float last) && f < last - 0.001f) _crackUntil[p] = now + CrackFlash;
            _lastFraction[p] = f;

            if (_crackUntil.TryGetValue(p, out double until) && now < until) return Hot(skin);

            float worn = 1f - f;
            var c = Color.Lerp(skin, Color.white, Mathf.Clamp01(Plugin.BubbleWornWhiteness.Value) * worn);
            c.a = Mathf.Lerp(1f, Mathf.Clamp01(Plugin.BubbleWornAlpha.Value), worn);   // thinner, not darker
            return c;
        }

        /// <summary>The skin colour pushed over 1: the tint pipeline scales by intensity, so this reads as a bright pop of the same colour, not white.</summary>
        internal static Color Hot(Color skin) => new Color(skin.r * 1.4f, skin.g * 1.4f, skin.b * 1.4f, 1f);

        /// <summary>
        /// The colour this player's bubble is being DRAWN right now, flash and blink
        /// included: what anything that wants to match the bubble (the halo) should use.
        /// </summary>
        internal static Color CurrentColour(PlayerInfo p)
        {
            if (p == null) return Color.white;
            if (_flashUntil != double.MinValue && ReferenceEquals(_flashOn, p))
            {
                var c = Skin.Of(p);
                float b = Mathf.Max(1f, Plugin.ParryGlowBoost.Value);
                return new Color(c.r * b, c.g * b, c.b * b, 1f);
            }
            if (Local.Is(p) && _warnActive && _warnDim) return Hot(Skin.Of(p));
            return BubbleColour(p);
        }

        /// <summary>Re-tint any active bubble whose state changed since the last frame.</summary>
        private static void TickBubbleState()
        {
            if (!Enabled) return;
            TouchPlayer(GameManager.LocalPlayerInfo);
            try { var r = GameManager.RemotePlayers; if (r != null) foreach (var p in r) TouchPlayer(p); } catch { }
        }

        private static void TouchPlayer(PlayerInfo p)
        {
            if (p == null) return;
            try
            {
                if (!p.IsElectromagnetShieldActive) { _lastApplied.Remove(p); return; }
                if (_flashUntil != double.MinValue && ReferenceEquals(_flashOn, p)) return;   // the parry flash owns this bubble's colour right now (ours or a remote's)
                if (Local.Is(p) && _warnActive) return;                                        // the pip blink owns ours
                var col = p.ElectromagnetShieldCollider;
                if (col == null) return;
                var c = BubbleColour(p);
                if (_lastApplied.TryGetValue(p, out var prev) && prev == c) return;
                _lastApplied[p] = c;
                Apply(col.transform, c, "bubble state");
            }
            catch { }
        }

        internal static void Disarm() { _armed = null; }

        // ---- Parry flash -------------------------------------------------------

        private static double _flashUntil = double.MinValue;
        private static PlayerInfo _flashOn;

        /// <summary>
        /// Blow the shield's colour out bright for a moment. Only visible while the
        /// shield still has a body, which during a parry is what ParryLinger is for
        /// (ParryFx extends it to cover the flash). Runs on every client for the
        /// parrier's bubble: ours from ResolveKnockout, a remote's from the SbgNet
        /// Parry message.
        /// </summary>
        internal static void ParryFlash(PlayerInfo p)
        {
            if (!Enabled || !Plugin.ParryGlow.Value || p == null) return;
            var col = p.ElectromagnetShieldCollider;
            if (col == null) return;

            var c = Skin.Of(p);
            float b = Mathf.Max(1f, Plugin.ParryGlowBoost.Value);
            var hot = new Color(c.r * b, c.g * b, c.b * b, 1f);

            _flashOn = p;
            _flashUntil = Time.timeAsDouble + Mathf.Max(0.05f, Plugin.ParryGlowDuration.Value);
            Apply(col.transform, hot, "parry flash");
        }

        /// <summary>Put the normal colour back when the flash is done.</summary>
        private static void TickParryFlash()
        {
            if (_flashUntil == double.MinValue || Time.timeAsDouble < _flashUntil) return;
            _flashUntil = double.MinValue;

            var p = _flashOn; _flashOn = null;
            if (p == null) return;
            var col = p.ElectromagnetShieldCollider;
            if (col != null) Apply(col.transform, BubbleColour(p), "parry flash over");
        }

        // ---- Pip warning -------------------------------------------------------

        private static bool _warnActive, _warnDim;

        /// <summary>
        /// Last pip: the bubble blinks BRIGHT. The tint pipeline is re-run on each phase
        /// change with either the skin colour or the skin colour pushed toward white, so
        /// it costs a handful of material writes ten times a second and nothing while
        /// the bubble is healthy. It used to blink dark instead, which on an additive
        /// bubble reads as a black stain rather than a warning. The parry flash has
        /// priority for the moment it runs.
        /// </summary>
        private static void TickPipWarning()
        {
            var p = GameManager.LocalPlayerInfo;
            // Last circle: two pips or fewer, since a circle is two.
            bool want = Enabled && Plugin.PipWarning.Value && Plugin.WeActivated && ShieldState.Pips > 0 && ShieldState.Pips <= 2 &&
                        p != null && p.IsElectromagnetShieldActive && _flashUntil == double.MinValue;

            if (!want)
            {
                if (_warnActive)
                {
                    _warnActive = false;
                    if (_warnDim && p != null && p.ElectromagnetShieldCollider != null)
                        Apply(p.ElectromagnetShieldCollider.transform, BubbleColour(p), "pip warning over");
                    _warnDim = false;
                }
                return;
            }

            var col = p.ElectromagnetShieldCollider;
            if (col == null) return;
            bool bright = (Time.timeAsDouble * 5.0) % 1.0 < 0.5;   // 5 Hz
            if (_warnActive && bright == _warnDim) return;
            if (!_warnActive) Plugin.Log.LogInfo($"Last circle ({ShieldState.Pips} pips): bubble blinking.");
            _warnActive = true; _warnDim = bright;
            // Blink between the worn, pale state colour and a hot saturated pop of the skin.
            Apply(col.transform, bright ? Hot(Skin.Of(p)) : BubbleColour(p), "pip warning");
        }

        /// <summary>
        /// Called from the SetTeam postfix, which sits on a method the whole game uses.
        /// The armed check is a nullable read and returns immediately for every VFX
        /// that is not our shield, and nothing here is allowed to throw.
        /// </summary>
        internal static void OnSetTeam(TeamColorVfxHandler h)
        {
            if (!_armed.HasValue || h == null) return;
            _consumed = true;
            Apply(h.transform, _armed.Value, "SetTeam");
        }

        /// <summary>Called from the shield hook postfix: fallback if no SetTeam consumed the arm.</summary>
        internal static void OnShieldHookDone(PlayerInfo p)
        {
            if (_armed.HasValue && !_consumed && p.ElectromagnetShieldCollider != null)
                Apply(p.ElectromagnetShieldCollider.transform, _armed.Value, "hook fallback");
            _armed = null; _consumed = false;
        }

        // ---- Tint --------------------------------------------------------------

        internal static void Apply(Transform root, Color c, string why)
        {
            try
            {
                _systems.Clear();
                root.GetComponentsInChildren(true, _systems);
                foreach (var ps in _systems) TintSystem(ps, c);

                _handlers.Clear();
                root.GetComponentsInChildren(true, _handlers);
                foreach (var h in _handlers) BubbleMaterialTintPatch.Register(h, c);

                _renderers.Clear();
                root.GetComponentsInChildren(true, _renderers);
                foreach (var r in _renderers)
                {
                    if (r.GetComponentInParent<BubbleVfxMaterialHandler>() != null) continue;
                    var shared = r.sharedMaterial;
                    if (shared == null) continue;
                    if (!_instances.TryGetValue(r, out var inst) || inst.Instance == null || inst.Instance.shader != shared.shader)
                    {
                        inst = new Instanced { Original = shared, Instance = new Material(shared) };
                        _instances[r] = inst;
                    }
                    else if (!ReferenceEquals(shared, inst.Instance))
                    {
                        inst.Original = shared;
                        inst.Instance.CopyPropertiesFromMaterial(shared);
                    }
                    BubbleMaterialTintPatch.TintMaterial(inst.Instance, c);
                    r.sharedMaterial = inst.Instance;
                    _restored = false;
                }

                if (Plugin.VerboseLogging.Value && _dumped.Add(root.name))
                    Dump(root, why);
            }
            catch (Exception e)
            {
                if (Plugin.VerboseLogging.Value) Plugin.Log.LogWarning("Shield tint failed: " + e.Message);
            }
        }

        private static void TintSystem(ParticleSystem ps, Color c)
        {
            var main = ps.main;
            main.startColor = Retint(main.startColor, c);

            var col = ps.colorOverLifetime;
            if (col.enabled) col.color = Retint(col.color, c);

            var cbs = ps.colorBySpeed;
            if (cbs.enabled) cbs.color = Retint(cbs.color, c);

            var tr = ps.trails;
            if (tr.enabled)
            {
                tr.colorOverLifetime = Retint(tr.colorOverLifetime, c);
                tr.colorOverTrail    = Retint(tr.colorOverTrail, c);
            }
        }

        /// <summary>
        /// Same shape as the source: Color stays Color, Gradient stays Gradient, etc.
        /// Each colour key becomes the skin colour scaled by the key's own peak
        /// component, which keeps relative brightness and HDR intensity; alpha is
        /// untouched, so fades and pulses survive.
        /// </summary>
        internal static ParticleSystem.MinMaxGradient Retint(ParticleSystem.MinMaxGradient src, Color c)
        {
            switch (src.mode)
            {
                case ParticleSystemGradientMode.Color:
                    return new ParticleSystem.MinMaxGradient(Hue(src.color, c));
                case ParticleSystemGradientMode.TwoColors:
                    return new ParticleSystem.MinMaxGradient(Hue(src.colorMin, c), Hue(src.colorMax, c));
                case ParticleSystemGradientMode.Gradient:
                    return new ParticleSystem.MinMaxGradient(Hue(src.gradient, c));
                case ParticleSystemGradientMode.TwoGradients:
                    return new ParticleSystem.MinMaxGradient(Hue(src.gradientMin, c), Hue(src.gradientMax, c));
                case ParticleSystemGradientMode.RandomColor:
                    return new ParticleSystem.MinMaxGradient(Hue(src.gradient, c)) { mode = ParticleSystemGradientMode.RandomColor };
                default:
                    return src;
            }
        }

        private static Color Hue(Color k, Color c)
        {
            float i = k.maxColorComponent;
            if (i <= 0.0001f) return k; // black stays black (usually a fade-to-dark key)
            // The tint's own alpha scales the authored alpha: this is how a worn bubble
            // gets THINNER rather than darker or whiter. Fades and pulses keep their shape.
            return new Color(c.r * i, c.g * i, c.b * i, k.a * c.a);
        }

        private static Gradient Hue(Gradient g, Color c)
        {
            if (g == null) return null;
            var keys = g.colorKeys;
            var outKeys = new GradientColorKey[keys.Length];
            for (int i = 0; i < keys.Length; i++)
                outKeys[i] = new GradientColorKey(Hue(keys[i].color, c), keys[i].time);
            var ng = new Gradient { mode = g.mode };
            ng.SetKeys(outKeys, g.alphaKeys);
            return ng;
        }

        // ---- Housekeeping ------------------------------------------------------

        /// <summary>
        /// Two jobs, both cheap and both skippable most frames:
        ///  - once our shield has been down a while, hand the pooled renderers their
        ///    stock materials back, since the vanilla magnet item shares those prefabs,
        ///  - every few seconds, drop entries whose renderer the engine has destroyed
        ///    and destroy the Material we made for them. Without this the dictionary
        ///    grows by a few materials per scene load and never shrinks.
        /// </summary>
        internal static void Tick()
        {
            TickParryFlash();   // before the early-out: a flash can be pending with nothing instanced yet
            TickPipWarning();
            TickBubbleState();
            BubbleHalo.Tick();  // after the colour decisions above, so the halo matches this frame's bubble
            if (_instances.Count == 0) return;

            double now = Time.timeAsDouble;
            if (AnyShieldActive()) _lastAnyActive = now;
            bool allIdle = now - _lastAnyActive >= 3.0;

            if (!_restored && allIdle)
            {
                foreach (var kv in _instances)
                {
                    var r = kv.Key; var inst = kv.Value;
                    if (r != null && inst.Original != null && ReferenceEquals(r.sharedMaterial, inst.Instance))
                        r.sharedMaterial = inst.Original;
                }
                _restored = true;   // do not walk the dictionary again until we retint
            }

            if (now - _lastSweep < 5.0) return;
            _lastSweep = now;
            _sweep.Clear();
            foreach (var kv in _instances)
                if (kv.Key == null) _sweep.Add(kv.Key);   // Unity-destroyed renderer
            foreach (var r in _sweep)
            {
                if (_instances.TryGetValue(r, out var inst) && inst.Instance != null)
                    UnityEngine.Object.Destroy(inst.Instance);
                _instances.Remove(r);
            }
            if (_sweep.Count > 0 && Plugin.VerboseLogging.Value)
                Plugin.Log.LogInfo($"Shield tint: released {_sweep.Count} material instance(s) for destroyed renderers.");
            BubbleMaterialTintPatch.Sweep();
        }

        /// <summary>Full teardown on plugin unload: give everything back and destroy what we made.</summary>
        internal static void DestroyAll()
        {
            foreach (var kv in _instances)
            {
                var r = kv.Key; var inst = kv.Value;
                if (r != null && inst.Original != null && ReferenceEquals(r.sharedMaterial, inst.Instance))
                    r.sharedMaterial = inst.Original;
                if (inst.Instance != null) UnityEngine.Object.Destroy(inst.Instance);
            }
            _instances.Clear();
            _dumped.Clear();
            _restored = true;
            _armed = null;
            _lastFraction.Clear(); _crackUntil.Clear(); _lastApplied.Clear();
            BubbleMaterialTintPatch.DestroyAll();
            BubbleHalo.DestroyAll();
        }

        private static void Dump(Transform root, string why)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"Shield VFX '{root.name}' tinted via {why}:");
            foreach (var ps in _systems)
            {
                var m = ps.main; var col = ps.colorOverLifetime;
                sb.Append($"\n  PS '{ps.name}' startColor={m.startColor.mode} col={(col.enabled ? col.color.mode.ToString() : "off")}" +
                          $" custom1={(ps.customData.enabled ? ps.customData.GetMode(ParticleSystemCustomData.Custom1).ToString() : "off")}" +
                          $" delay={m.startDelay.constant:0.00} loop={m.loop}");
                var r = ps.GetComponent<ParticleSystemRenderer>();
                var mat = r != null ? r.sharedMaterial : null;
                if (mat != null)
                {
                    sb.Append($"\n     mat '{mat.name}' shader '{mat.shader.name}' handler={(r.GetComponentInParent<BubbleVfxMaterialHandler>() != null)}");
                    var sh = mat.shader;
                    int n = sh.GetPropertyCount();
                    for (int i = 0; i < n; i++)
                        if (sh.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Color)
                            sb.Append($" {sh.GetPropertyName(i)}={mat.GetColor(sh.GetPropertyName(i))}");
                }
            }
            Plugin.Log.LogInfo(sb.ToString());
        }
    }

    // =========================================================================
    //  Harmony hooks for the tint
    // =========================================================================

    /// <summary>Arm before the game sets up the shield/dissolve VFX, fall back after.</summary>
    [HarmonyPatch(typeof(PlayerInfo), "OnIsElectromagnetShieldActiveChanged")]
    internal static class ShieldVfxTintHook
    {
        private static void Prefix(PlayerInfo __instance)
        {
            ShieldTint.Arm(__instance);
            if (Plugin.VerboseLogging.Value && Local.Is(__instance) && Plugin.WeActivated)
                Plugin.Log.LogInfo($"Shield hook fired {(Time.timeAsDouble - Plugin.LastActivationTime) * 1000.0:0} ms after activation (active={__instance.IsElectromagnetShieldActive}).");
        }

        private static void Postfix(PlayerInfo __instance) => ShieldTint.OnShieldHookDone(__instance);
    }

    /// <summary>Arm around the hit/break effects so sparks and the break burst match the shield.</summary>
    [HarmonyPatch(typeof(PlayerInfo), "PlayElectromagnetShieldHitInternal")]
    internal static class ShieldHitTintHook
    {
        private static void Prefix(PlayerInfo __instance) => ShieldTint.Arm(__instance);
        private static void Postfix() => ShieldTint.Disarm();
    }

    [HarmonyPatch(typeof(TeamColorVfxHandler), nameof(TeamColorVfxHandler.SetTeam))]
    internal static class TeamColorSetTeamPatch
    {
        private static void Postfix(TeamColorVfxHandler __instance) => ShieldTint.OnSetTeam(__instance);
    }

    /// <summary>
    /// Replaces BubbleVfxMaterialHandler.Update for shields we have tinted: same
    /// above/below-water swap, but between tinted copies of its two materials.
    /// Registration expires shortly after our shield drops, so the pooled prefab
    /// goes back to vanilla for the next user.
    /// </summary>
    [HarmonyPatch(typeof(BubbleVfxMaterialHandler), "Update")]
    internal static class BubbleMaterialTintPatch
    {
        private class Tinted
        {
            public Material Normal, Stencil;
            public Color Color;
            public PlayerInfo Owner;
            public double LastActive;
        }

        private static readonly Dictionary<BubbleVfxMaterialHandler, Tinted> _reg = new Dictionary<BubbleVfxMaterialHandler, Tinted>();

        private static AccessTools.FieldRef<BubbleVfxMaterialHandler, ParticleSystemRenderer> _renderer;
        private static AccessTools.FieldRef<BubbleVfxMaterialHandler, Material> _normal;
        private static AccessTools.FieldRef<BubbleVfxMaterialHandler, Material> _stencil;

        private static bool Prepare()
        {
            try
            {
                _renderer = AccessTools.FieldRefAccess<BubbleVfxMaterialHandler, ParticleSystemRenderer>("particleSystemRenderer");
                _normal   = AccessTools.FieldRefAccess<BubbleVfxMaterialHandler, Material>("normalMaterial");
                _stencil  = AccessTools.FieldRefAccess<BubbleVfxMaterialHandler, Material>("stencilMaterial");
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("BubbleVfxMaterialHandler fields not found; shield tint will be overridden by the game. " + e.Message);
                return false;
            }
        }

        internal static void Register(BubbleVfxMaterialHandler h, Color c)
        {
            if (h == null || _normal == null) return;
            if (!_reg.TryGetValue(h, out var t))
            {
                t = new Tinted();
                _reg[h] = t;
            }
            if (t.Owner == null) t.Owner = ShieldTint.OwnerOf(h.transform);
            t.LastActive = Time.timeAsDouble;
            var n = _normal(h); var st = _stencil(h);
            if (t.Normal == null  && n  != null) t.Normal  = new Material(n);
            if (t.Stencil == null && st != null) t.Stencil = new Material(st);
            if (t.Color != c || t.Normal == null)
            {
                t.Color = c;
                if (t.Normal  != null && n  != null) { t.Normal.CopyPropertiesFromMaterial(n);   TintMaterial(t.Normal, c); }
                if (t.Stencil != null && st != null) { t.Stencil.CopyPropertiesFromMaterial(st); TintMaterial(t.Stencil, c); }
            }
            // Apply now rather than waiting for the next Update, so frame 0 is tinted.
            var r = _renderer(h);
            if (r != null) Swap(r, t);
        }

        internal static void TintMaterial(Material m, Color c)
        {
            var sh = m.shader;
            int count = sh.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                if (sh.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Color) continue;
                string prop = sh.GetPropertyName(i);
                var oc = m.GetColor(prop);
                // The material's own intensity (these are HDR colours) times BubbleGlow: past
                // white-point the game's bloom picks the bubble up as a glow.
                float intensity = Mathf.Max(oc.maxColorComponent, 1f) * Mathf.Max(0.1f, Plugin.BubbleGlow.Value);
                m.SetColor(prop, new Color(c.r * intensity, c.g * intensity, c.b * intensity, oc.a * c.a));
            }
        }

        /// <summary>Keep the tint while the owner's shield is up, and for a moment after so the dissolve stays coloured.</summary>
        private static bool StillOurs(Tinted t)
        {
            if (t.Owner == null) return false;
            bool active = false;
            try { active = t.Owner.IsElectromagnetShieldActive; } catch { }
            if (active) t.LastActive = Time.timeAsDouble;
            return active || Time.timeAsDouble - t.LastActive < 3.0;
        }

        private static void Swap(ParticleSystemRenderer r, Tinted t)
        {
            var tracker = GameManager.CameraLevelBoundsTracker;
            if (tracker == null) return;

            float waterHeight = float.NegativeInfinity;
            var secondary = tracker.CurrentSecondaryHazardLocalOnly;
            if (secondary == null)
            {
                if (MainOutOfBoundsHazard.Type == OutOfBoundsHazard.Water)
                    waterHeight = tracker.CurrentOutOfBoundsHazardWorldHeightLocalOnly;
            }
            else if (secondary.Type == OutOfBoundsHazard.Water)
            {
                waterHeight = tracker.CurrentOutOfBoundsHazardWorldHeightLocalOnly;
            }

            var want = tracker.transform.position.y > waterHeight ? t.Stencil : t.Normal;
            if (want == null) want = t.Normal ?? t.Stencil;
            if (want != null && !ReferenceEquals(r.sharedMaterial, want)) r.sharedMaterial = want;
        }

        /// <summary>
        /// Runs for every bubble VFX in the scene, including other players' magnet
        /// shields, so the first line is a dictionary miss and an immediate hand-back
        /// to the game. Low priority: if another mod prefixes this method, theirs runs
        /// first and we never skip it.
        /// </summary>
        [HarmonyPriority(Priority.Low)]
        private static bool Prefix(BubbleVfxMaterialHandler __instance)
        {
            try
            {
                if (!_reg.TryGetValue(__instance, out var t)) return true;
                if (!StillOurs(t)) { Release(__instance, t); return true; }
                var r = _renderer(__instance);
                if (r == null) return false;
                Swap(r, t);
                return false;
            }
            catch (Exception e)
            {
                // Never take the game's bubble rendering down with us.
                Plugin.Log.LogWarning("Bubble tint error, reverting to vanilla for this handler: " + e.Message);
                try { _reg.Remove(__instance); } catch { }
                return true;
            }
        }

        private static void Release(BubbleVfxMaterialHandler h, Tinted t)
        {
            _reg.Remove(h);
            if (t == null) return;
            if (t.Normal  != null) UnityEngine.Object.Destroy(t.Normal);
            if (t.Stencil != null) UnityEngine.Object.Destroy(t.Stencil);
        }

        private static readonly List<BubbleVfxMaterialHandler> _regSweep = new List<BubbleVfxMaterialHandler>();

        /// <summary>
        /// Handlers destroyed by a scene change never run Update again, so their entries
        /// would sit here forever holding two Materials each. Called from ShieldTint.Tick.
        /// </summary>
        internal static void Sweep()
        {
            if (_reg.Count == 0) return;
            _regSweep.Clear();
            foreach (var kv in _reg) if (kv.Key == null) _regSweep.Add(kv.Key);
            foreach (var h in _regSweep)
            {
                if (_reg.TryGetValue(h, out var t))
                {
                    if (t.Normal  != null) UnityEngine.Object.Destroy(t.Normal);
                    if (t.Stencil != null) UnityEngine.Object.Destroy(t.Stencil);
                }
                _reg.Remove(h);
            }
        }

        internal static void DestroyAll()
        {
            foreach (var kv in _reg)
            {
                if (kv.Value.Normal  != null) UnityEngine.Object.Destroy(kv.Value.Normal);
                if (kv.Value.Stencil != null) UnityEngine.Object.Destroy(kv.Value.Stencil);
            }
            _reg.Clear();
        }
    }
}

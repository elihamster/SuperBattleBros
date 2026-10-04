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
    /// break) to the owner's skin colour, for every player's shield (see IsOurs).
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
        private static PlayerInfo _armedFor;
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
            _armed = null; _armedFor = null; _consumed = false;
            if (!Enabled || !IsOurs(p)) return;
            _armed = CurrentColour(p);
            _armedFor = p;
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
            if (IsFlashing(p)) return FlashColour(p);
            if (Local.Is(p) && _warnActive && _warnBright) return Hot(Skin.Of(p));
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
                if (IsFlashing(p)) return;                 // the parry flash owns this bubble's colour right now (ours or a remote's)
                if (Local.Is(p) && _warnActive) return;    // the pip blink owns ours
                var col = p.ElectromagnetShieldCollider;
                if (col == null) return;
                var c = BubbleColour(p);
                if (_lastApplied.TryGetValue(p, out var prev) && prev == c) return;
                _lastApplied[p] = c;
                Apply(col.transform, c, "bubble state", p);
            }
            catch { }
        }

        internal static void Disarm() { _armed = null; _armedFor = null; }

        // ---- Parry flash -------------------------------------------------------

        /// <summary>When each player's parry flash ends. Per player: two parries close together each keep their own.</summary>
        private static readonly Dictionary<PlayerInfo, double> _flashUntil = new Dictionary<PlayerInfo, double>();
        private static readonly List<PlayerInfo> _flashDone = new List<PlayerInfo>();

        internal static bool IsFlashing(PlayerInfo p) =>
            p != null && _flashUntil.TryGetValue(p, out double until) && Time.timeAsDouble < until;

        private static Color FlashColour(PlayerInfo p)
        {
            var c = Skin.Of(p);
            float b = Mathf.Max(1f, Plugin.ParryGlowBoost.Value);
            return new Color(c.r * b, c.g * b, c.b * b, 1f);
        }

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

            _flashUntil[p] = Time.timeAsDouble + Plugin.ParryFlashSeconds;
            var hot = FlashColour(p);
            _lastApplied[p] = hot;
            Apply(col.transform, hot, "parry flash", p);
        }

        /// <summary>Put each player's normal colour back when their flash is done.</summary>
        private static void TickParryFlash()
        {
            if (_flashUntil.Count == 0) return;
            double now = Time.timeAsDouble;
            _flashDone.Clear();
            foreach (var kv in _flashUntil) if (kv.Key == null || now >= kv.Value) _flashDone.Add(kv.Key);
            foreach (var p in _flashDone)
            {
                _flashUntil.Remove(p);
                if (p == null) continue;
                try
                {
                    if (!p.IsElectromagnetShieldActive) { _lastApplied.Remove(p); continue; }
                    var col = p.ElectromagnetShieldCollider;
                    if (col == null) continue;
                    var c = CurrentColour(p);
                    _lastApplied[p] = c;
                    Apply(col.transform, c, "parry flash over", p);
                }
                catch { }
            }
        }

        // ---- Pip warning -------------------------------------------------------

        private static bool _warnActive, _warnBright;

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
            bool flashing = IsFlashing(p);
            bool want = Enabled && Plugin.PipWarning.Value && Plugin.WeActivated && ShieldState.Pips > 0 && ShieldState.Pips <= 2 &&
                        p != null && p.IsElectromagnetShieldActive && !flashing;

            if (!want)
            {
                if (_warnActive)
                {
                    _warnActive = false;
                    // Back to the plain state colour -- unless the parry flash took over, which owns it now.
                    if (_warnBright && !flashing && p != null && p.ElectromagnetShieldCollider != null)
                    {
                        var c = BubbleColour(p);
                        _lastApplied[p] = c;
                        Apply(p.ElectromagnetShieldCollider.transform, c, "pip warning over", p);
                    }
                    _warnBright = false;
                }
                return;
            }

            var col = p.ElectromagnetShieldCollider;
            if (col == null) return;
            bool bright = (Time.timeAsDouble * 5.0) % 1.0 < 0.5;   // 5 Hz
            if (_warnActive && bright == _warnBright) return;
            if (!_warnActive) Plugin.Log.LogInfo($"Last circle ({ShieldState.Pips} pips): bubble blinking.");
            _warnActive = true; _warnBright = bright;
            // Blink between the worn, pale state colour and a hot saturated pop of the skin.
            var blink = bright ? Hot(Skin.Of(p)) : BubbleColour(p);
            _lastApplied[p] = blink;
            Apply(col.transform, blink, "pip warning", p);
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
            Apply(h.transform, _armed.Value, "SetTeam", _armedFor);
        }

        /// <summary>Called from the shield hook postfix: fallback if no SetTeam consumed the arm.</summary>
        internal static void OnShieldHookDone(PlayerInfo p)
        {
            if (_armed.HasValue && !_consumed && p.ElectromagnetShieldCollider != null)
                Apply(p.ElectromagnetShieldCollider.transform, _armed.Value, "hook fallback", p);
            if (_armed.HasValue && p != null && p.IsElectromagnetShieldActive) _lastApplied[p] = _armed.Value;
            _armed = null; _armedFor = null; _consumed = false;
        }

        // ---- Tint --------------------------------------------------------------

        /// <param name="owner">Whose bubble this is. Pooled effects move between players, so it is bound on every apply.</param>
        internal static void Apply(Transform root, Color c, string why, PlayerInfo owner)
        {
            try
            {
                _systems.Clear();
                root.GetComponentsInChildren(true, _systems);
                foreach (var ps in _systems) TintSystem(ps, c);

                _handlers.Clear();
                root.GetComponentsInChildren(true, _handlers);
                foreach (var h in _handlers) BubbleMaterialTintPatch.Register(h, c, owner);

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
                    BubbleMaterialTintPatch.TintMaterial(inst.Instance, c, inst.Original);   // from the original, never from our own tinted copy
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

        /// <summary>A particle system's colours as the game authored them, taken the first time we see it.</summary>
        private class Authored
        {
            public ParticleSystem.MinMaxGradient Start, Life, Speed, TrailLife, TrailTrail;
        }
        private static readonly Dictionary<ParticleSystem, Authored> _authored = new Dictionary<ParticleSystem, Authored>();
        private static readonly List<ParticleSystem> _authoredSweep = new List<ParticleSystem>();

        /// <summary>
        /// Always tint FROM the authored colours, never from the live ones: those are our
        /// own previous tint. Re-tinting the live values multiplied brightness and alpha
        /// into themselves on every apply (every raise applied twice, and the pip blink
        /// and the parry flash many times), so a held bubble drifted darker or brighter
        /// over a few seconds. The same bug as the material glow in 0.7.25.
        /// </summary>
        private static void TintSystem(ParticleSystem ps, Color c)
        {
            if (!_authored.TryGetValue(ps, out var a))
            {
                var tr0 = ps.trails;
                a = new Authored
                {
                    Start = ps.main.startColor,
                    Life = ps.colorOverLifetime.color,
                    Speed = ps.colorBySpeed.color,
                    TrailLife = tr0.colorOverLifetime,
                    TrailTrail = tr0.colorOverTrail,
                };
                _authored[ps] = a;
            }

            var main = ps.main;
            main.startColor = Retint(a.Start, c);

            var col = ps.colorOverLifetime;
            if (col.enabled) col.color = Retint(a.Life, c);

            var cbs = ps.colorBySpeed;
            if (cbs.enabled) cbs.color = Retint(a.Speed, c);

            var tr = ps.trails;
            if (tr.enabled)
            {
                tr.colorOverLifetime = Retint(a.TrailLife, c);
                tr.colorOverTrail    = Retint(a.TrailTrail, c);
            }
        }

        /// <summary>Put a system's authored colours back (unload).</summary>
        private static void RestoreSystem(ParticleSystem ps, Authored a)
        {
            var main = ps.main; main.startColor = a.Start;
            var col = ps.colorOverLifetime; col.color = a.Life;
            var cbs = ps.colorBySpeed; cbs.color = a.Speed;
            var tr = ps.trails; tr.colorOverLifetime = a.TrailLife; tr.colorOverTrail = a.TrailTrail;
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
            _authoredSweep.Clear();
            foreach (var kv in _authored) if (kv.Key == null) _authoredSweep.Add(kv.Key);
            foreach (var ps in _authoredSweep) _authored.Remove(ps);
            BubbleMaterialTintPatch.Sweep();
            PrunePlayers();
        }

        private static readonly List<PlayerInfo> _gonePlayers = new List<PlayerInfo>();

        /// <summary>Players who left (Unity-destroyed) are dropped from the per-player caches.</summary>
        private static void PrunePlayers()
        {
            _gonePlayers.Clear();
            foreach (var k in _lastFraction.Keys) if (k == null) _gonePlayers.Add(k);
            foreach (var k in _lastApplied.Keys) if (k == null && !_gonePlayers.Contains(k)) _gonePlayers.Add(k);
            foreach (var k in _gonePlayers) { _lastFraction.Remove(k); _crackUntil.Remove(k); _lastApplied.Remove(k); _flashUntil.Remove(k); }
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
            foreach (var kv in _authored) if (kv.Key != null) { try { RestoreSystem(kv.Key, kv.Value); } catch { } }
            _authored.Clear();
            _dumped.Clear();
            _restored = true;
            _armed = null; _armedFor = null;
            _lastFraction.Clear(); _crackUntil.Clear(); _lastApplied.Clear(); _flashUntil.Clear();
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
    /// Registration expires shortly after its owner's shield drops, so the pooled
    /// prefab goes back to vanilla. The bubble effect is pooled and handed from player
    /// to player, so the owner is rebound on every tint, never kept from first sight.
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

        internal static void Register(BubbleVfxMaterialHandler h, Color c, PlayerInfo owner)
        {
            if (h == null || _normal == null) return;
            if (!_reg.TryGetValue(h, out var t))
            {
                t = new Tinted();
                _reg[h] = t;
            }
            t.Owner = owner != null ? owner : ShieldTint.OwnerOf(h.transform);
            t.LastActive = Time.timeAsDouble;
            var n = _normal(h); var st = _stencil(h);
            if (t.Normal == null  && n  != null) t.Normal  = new Material(n);
            if (t.Stencil == null && st != null) t.Stencil = new Material(st);
            if (t.Color != c || t.Normal == null)
            {
                t.Color = c;
                if (t.Normal  != null && n  != null) { t.Normal.CopyPropertiesFromMaterial(n);   TintMaterial(t.Normal, c, n); }
                if (t.Stencil != null && st != null) { t.Stencil.CopyPropertiesFromMaterial(st); TintMaterial(t.Stencil, c, st); }
            }
            // Apply now rather than waiting for the next Update, so frame 0 is tinted.
            var r = _renderer(h);
            if (r != null) Swap(r, t);
        }

        /// <summary>
        /// Tints every colour property of m to c. The material's OWN intensity is read
        /// from source (the game's untouched material), never from m: m is our instance
        /// and gets re-tinted every time the bubble's state changes, and reading the
        /// already-tinted value multiplied BubbleGlow into itself on every pass, so a
        /// spammed bubble grew brighter and brighter (0.7.24-25).
        /// </summary>
        internal static void TintMaterial(Material m, Color c, Material source = null)
        {
            var sh = m.shader;
            var from = source != null && source.shader == sh ? source : m;
            int count = sh.GetPropertyCount();
            for (int i = 0; i < count; i++)
            {
                if (sh.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Color) continue;
                string prop = sh.GetPropertyName(i);
                var oc = from.GetColor(prop);
                // The material's own intensity (these are HDR colours) times BubbleGlow: past
                // white-point the game's bloom picks the bubble up as a glow.
                float intensity = Mathf.Max(oc.maxColorComponent, 1f) * Mathf.Max(0.1f, Plugin.BubbleGlow.Value);
                m.SetColor(prop, new Color(c.r * intensity, c.g * intensity, c.b * intensity, oc.a * c.a));
            }
        }

        /// <summary>Keep the tint while the owner's shield is up, and for a moment after so the dissolve stays coloured.</summary>
        private static bool StillOurs(BubbleVfxMaterialHandler h, Tinted t)
        {
            // The owner can be unknown for a frame when the game tints before parenting
            // the effect; resolve it from where the effect hangs now.
            if (t.Owner == null) t.Owner = ShieldTint.OwnerOf(h.transform);
            if (t.Owner == null) return Time.timeAsDouble - t.LastActive < 0.5;
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
                if (!ShieldTint.Enabled || !StillOurs(__instance, t)) { Release(__instance, t); return true; }
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

using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace SbgShields
{
    /// <summary>
    /// Smash's invulnerability look: the body flickers washed-out white while the
    /// game's knockout immunity (the comeback shield) is up.
    ///
    /// Driven by the immunity SyncVar, so every modded client draws it for every
    /// player, local and remote, from the same state the game uses to refuse hits.
    /// Drawn with material property blocks on the body's renderers: no material is
    /// ever instanced or edited, and clearing is one call per renderer. The skin
    /// shader takes its colour from _Color (PlayerCosmeticsSwitcher writes it there);
    /// cosmetics on other shaders may use _BaseColor, so both are washed when present.
    ///
    /// Immunity.HideGameBubble additionally stops the game's own bubble particle so
    /// the flicker is the only tell. Off by default until it has been seen in play.
    /// </summary>
    internal static class ImmunityFlicker
    {
        private class Entry
        {
            public readonly List<Renderer> Renderers = new List<Renderer>();
            public bool Active;
            public bool Washed;
        }

        private static readonly Dictionary<PlayerInfo, Entry> _entries = new Dictionary<PlayerInfo, Entry>();
        private static readonly List<PlayerInfo> _scratch = new List<PlayerInfo>();
        private static readonly List<PlayerInfo> _dead = new List<PlayerInfo>();
        private static readonly List<SkinnedMeshRenderer> _skinned = new List<SkinnedMeshRenderer>();
        private static readonly List<MeshRenderer> _meshes = new List<MeshRenderer>();
        private static readonly MaterialPropertyBlock _block = new MaterialPropertyBlock();
        private static readonly int ColorId     = Shader.PropertyToID("_Color");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        internal static void Tick()
        {
            if (!Plugin.ImmunityFlickerEnabled.Value)
            {
                if (_entries.Count > 0) ClearAll();
                return;
            }

            _scratch.Clear();
            var local = GameManager.LocalPlayerInfo;
            if (local != null) _scratch.Add(local);
            try { var r = GameManager.RemotePlayers; if (r != null) _scratch.AddRange(r); } catch { }

            _dead.Clear();
            foreach (var kv in _entries)
                if (kv.Key == null || !_scratch.Contains(kv.Key)) _dead.Add(kv.Key);
            foreach (var p in _dead) { if (_entries.TryGetValue(p, out var e)) Clear(e); _entries.Remove(p); }

            float rate = Mathf.Max(1f, Plugin.ImmunityFlickerRate.Value);
            bool washPhase = (Time.timeAsDouble * rate) % 1.0 < 0.5;

            foreach (var p in _scratch)
            {
                if (p == null) continue;
                bool has = false;
                try { has = p.Movement != null && p.Movement.KnockoutImmunityStatus.hasImmunity && p.Movement.IsVisible; } catch { }

                _entries.TryGetValue(p, out var entry);
                if (!has)
                {
                    if (entry != null && entry.Active) Clear(entry);
                    continue;
                }

                if (entry == null) { entry = new Entry(); _entries[p] = entry; }
                if (!entry.Active)
                {
                    entry.Active = true;
                    entry.Washed = false;
                    Gather(p, entry);   // fresh each time: cosmetics can change between immunities
                }
                if (washPhase != entry.Washed)
                {
                    entry.Washed = washPhase;
                    if (washPhase) Wash(entry); else Unwash(entry);
                }
            }
        }

        private static void Gather(PlayerInfo p, Entry e)
        {
            e.Renderers.Clear();
            try
            {
                _skinned.Clear(); p.GetComponentsInChildren(true, _skinned);
                foreach (var r in _skinned) e.Renderers.Add(r);
                _meshes.Clear(); p.GetComponentsInChildren(true, _meshes);
                foreach (var r in _meshes) e.Renderers.Add(r);
            }
            catch (Exception ex) { if (Plugin.VerboseLogging.Value) Plugin.Log.LogWarning("Flicker gather: " + ex.Message); }
        }

        private static void Wash(Entry e)
        {
            float wash = Mathf.Clamp01(Plugin.ImmunityFlickerWash.Value);
            foreach (var r in e.Renderers)
            {
                if (r == null) continue;
                try
                {
                    var mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var m = mats[i];
                        if (m == null) continue;
                        bool hasC = m.HasProperty(ColorId), hasB = m.HasProperty(BaseColorId);
                        if (!hasC && !hasB) continue;
                        _block.Clear();
                        r.GetPropertyBlock(_block, i);
                        if (hasC) _block.SetColor(ColorId,     Color.Lerp(m.GetColor(ColorId),     Color.white, wash));
                        if (hasB) _block.SetColor(BaseColorId, Color.Lerp(m.GetColor(BaseColorId), Color.white, wash));
                        r.SetPropertyBlock(_block, i);
                    }
                }
                catch { }
            }
        }

        private static void Unwash(Entry e)
        {
            foreach (var r in e.Renderers)
            {
                if (r == null) continue;
                try
                {
                    int n = r.sharedMaterials.Length;
                    for (int i = 0; i < n; i++) r.SetPropertyBlock(null, i);
                }
                catch { }
            }
        }

        private static void Clear(Entry e)
        {
            if (e.Washed) Unwash(e);
            e.Washed = false;
            e.Active = false;
        }

        internal static void ClearAll()
        {
            foreach (var kv in _entries) Clear(kv.Value);
            _entries.Clear();
        }
    }

    /// <summary>
    /// The game's bubble particle lives in PlayerMovement.knockoutImmunityVfx and is
    /// (re)played from UpdateKnockoutImmunityVfx on every status change. Letting the
    /// original run keeps its bookkeeping (the red domination flag, the end pop);
    /// stopping the particle afterwards is all it takes to make it invisible.
    /// </summary>
    [HarmonyPatch(typeof(PlayerMovement), "UpdateKnockoutImmunityVfx")]
    internal static class HideGameBubblePatch
    {
        private static AccessTools.FieldRef<PlayerMovement, PoolableParticleSystem> _vfx;

        private static bool Prepare()
        {
            try { _vfx = AccessTools.FieldRefAccess<PlayerMovement, PoolableParticleSystem>("knockoutImmunityVfx"); return true; }
            catch (Exception e) { Plugin.Log.LogWarning("knockoutImmunityVfx not found; HideGameBubble does nothing. " + e.Message); return false; }
        }

        private static void Postfix(PlayerMovement __instance)
        {
            if (!Plugin.ImmunityFlickerEnabled.Value || !Plugin.HideGameBubble.Value) return;
            try
            {
                var v = _vfx(__instance);
                if (v != null) v.Stop(ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            catch { }
        }
    }
}

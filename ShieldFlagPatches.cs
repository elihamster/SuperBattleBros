using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace SbgShields
{
    /// <summary>
    /// The game decides "did that hit the shield?" in two ways, and making the collider a
    /// trigger (BubbleColliderPatch) only handles one of them.
    ///
    ///  1. PHYSICS. Balls, rockets and bombs collide with the shield sphere; gun rays stop
    ///     on it. The trigger removes that: projectiles pass into the body.
    ///  2. THE FLAG. PlayerInfo.IsElectromagnetShieldActive is a SyncVar every machine sees.
    ///     The ball's collision handler (on the host) and the gun's hitscan (on the shooter)
    ///     look at the flag on the player they just hit and reflect if it is up, without
    ///     ever asking the collider. So with the trigger alone a held bubble still bounced
    ///     balls and bullets, for free: the hit never reached the victim's TryKnockOut and
    ///     the pip costs for balls and guns were never charged. Found by reading the game
    ///     code for 0.7.23; nobody had shot a bubble on purpose.
    ///
    /// And one more place the bubble was a body: the distance helper every blast uses to
    /// measure how far you were from it walks every enabled collider on the player, the
    /// shield sphere included. That is the mine's proximity fuse (its edge popped a mine
    /// from a metre away), the explosion falloff, and the laser and thunderstorm centre
    /// check that decides a lethal direct hit -- a bubble made you a 1.25 m bigger target
    /// for the very hits it cannot stop.
    ///
    /// Both are fixed here, on every machine, so every machine agrees (handoff §0). Applied
    /// by hand from Plugin.Awake rather than by attribute: the gun and ball methods are
    /// compiler-generated local functions found by name, and a missing one must degrade to
    /// a warning line, not take every other patch down with it.
    /// </summary>
    internal static class ShieldFlagPatches
    {
        internal static void ApplyAll(Harmony harmony)
        {
            try
            {
                var target = AccessTools.Method(typeof(ComponentExtensions), nameof(ComponentExtensions.TryGetClosestPointOnAllActiveColliders));
                if (target == null)
                    Plugin.Log.LogWarning("Body distance patch: TryGetClosestPointOnAllActiveColliders not found; the bubble counts as body for mines, blasts, laser and thunderstorm.");
                else
                    harmony.Patch(target, prefix: new HarmonyMethod(typeof(ShieldFlagPatches), nameof(ClosestPointPrefix)));
            }
            catch (Exception e) { Plugin.Log.LogWarning("Body distance patch failed: " + e.Message); }

            int patched = 0;
            var targets = new[]
            {
                (type: typeof(PlayerInventory), prefix: "<ShootDuelingPistolRoutine>g__Shoot|", what: "pistol"),
                (type: typeof(PlayerInventory), prefix: "<ShootElephantGunRoutine>g__Shoot|",   what: "elephant gun"),
                (type: typeof(Hittable),        prefix: "<OnCollisionEnter>g__ParseCollision|", what: "ball collision"),
            };
            foreach (var t in targets)
            {
                try
                {
                    var m = FindByPrefix(t.type, t.prefix);
                    if (m == null)
                    {
                        Plugin.Log.LogWarning($"Shield flag gate: no '{t.prefix}*' on {t.type.Name} (game updated?); a held bubble reflects the {t.what} for free.");
                        continue;
                    }
                    harmony.Patch(m, transpiler: new HarmonyMethod(typeof(ShieldFlagPatches), nameof(Transpiler)));
                    patched++;
                }
                catch (Exception e) { Plugin.Log.LogWarning($"Shield flag gate ({t.what}) failed: {e.Message}"); }
            }
            Plugin.Log.LogInfo($"Shield flag gate: {patched}/3 game methods rerouted (pistol, elephant gun, ball collision).");
        }

        private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        /// <summary>A compiler-generated local function, by the stable part of its name, on the type or any nested type.</summary>
        private static MethodInfo FindByPrefix(Type type, string prefix)
        {
            foreach (var m in type.GetMethods(All))
                if (m.Name.StartsWith(prefix, StringComparison.Ordinal)) return m;
            foreach (var n in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                var m = FindByPrefix(n, prefix);
                if (m != null) return m;
            }
            return null;
        }

        // ---- The flag no longer reflects ----------------------------------------

        /// <summary>
        /// Stands in for PlayerInfo.IsElectromagnetShieldActive inside the three reflect
        /// checks. True only when reflecting is the rule: BubbleReflects on, or the mod
        /// standing down (public lobby, version mismatch), where vanilla must stay vanilla.
        /// </summary>
        public static bool FlagReflects(PlayerInfo p) =>
            p.IsElectromagnetShieldActive && (Plugin.BubbleReflects.Value || !ModHandshake.GameplayEnabled);

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
        {
            var getter = AccessTools.PropertyGetter(typeof(PlayerInfo), nameof(PlayerInfo.IsElectromagnetShieldActive));
            var gate   = AccessTools.Method(typeof(ShieldFlagPatches), nameof(FlagReflects));
            int n = 0;
            foreach (var ci in instructions)
            {
                if (getter != null && gate != null
                    && (ci.opcode == OpCodes.Callvirt || ci.opcode == OpCodes.Call)
                    && ci.operand is MethodInfo mi && mi == getter)
                {
                    // Copy constructor keeps the labels and exception blocks that pointed at the original call.
                    var call = new CodeInstruction(ci) { opcode = OpCodes.Call, operand = gate };
                    n++;
                    yield return call;
                }
                else yield return ci;
            }
            if (n == 0)
                Plugin.Log.LogWarning($"Shield flag gate: no shield check inside {original.DeclaringType?.Name}.{original.Name}; that path still reflects.");
            else if (Plugin.VerboseLogging.Value)
                Plugin.Log.LogInfo($"Shield flag gate: {n} check(s) rerouted in {original.DeclaringType?.Name}.{original.Name}.");
        }

        // ---- Distance to the body ignores the bubble ----------------------------

        private static int _shieldLayer = -1;

        private static bool ShieldLayerKnown()
        {
            if (_shieldLayer >= 0) return true;
            try { _shieldLayer = GameManager.LayerSettings.ElectromagnetShieldLayer; } catch { }
            return _shieldLayer >= 0;
        }

        /// <summary>
        /// The game's loop, minus the shield sphere. Returns true (run the original) when
        /// reflecting is the rule or the layer cannot be read, so vanilla stays vanilla.
        /// </summary>
        private static bool ClosestPointPrefix(Component component, Vector3 worldPosition, ref Vector3 closestPoint,
                                               ref float distanceSquared, int layerMask, ref bool __result)
        {
            if (Plugin.BubbleReflects.Value || !ModHandshake.GameplayEnabled || !ShieldLayerKnown()) return true;
            try
            {
                closestPoint = default(Vector3);
                distanceSquared = float.MaxValue;
                foreach (var c in component.GetComponentsInChildren<Collider>(false))
                {
                    if (!c.enabled || ((1 << c.gameObject.layer) & layerMask) == 0 || c.GetType().Name == "WheelCollider") continue;   // by name: no VehiclesModule reference
                    if (c.gameObject.layer == _shieldLayer) continue;   // the bubble is not a body
                    Vector3 p = c.ClosestPoint(worldPosition);
                    float d = (p - worldPosition).sqrMagnitude;
                    if (d < distanceSquared) { closestPoint = p; distanceSquared = d; }
                }
                __result = distanceSquared < float.MaxValue;
                return false;
            }
            catch { return true; }
        }
    }
}

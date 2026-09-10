#if SBG_DEV
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using UnityEngine;

namespace SbgShields
{
    /// <summary>
    /// DEV BUILD. The numbers the decompiler cannot see: knockout durations, gun ranges,
    /// ball speeds, shield radius, immunity windows. They live in Unity asset objects
    /// that the game exposes as GameManager.*Settings, each of which is null until the
    /// game has loaded it (some only once a hole is running). Every couple of seconds
    /// this looks at all of them and appends any it has not written yet to
    /// BepInEx\SbgSettingsDump.txt, so whatever the session reaches gets captured and
    /// a later, deeper session adds the rest. Read that file instead of guessing.
    /// </summary>
    internal static class SettingsDump
    {
        private static readonly HashSet<string> _written = new HashSet<string>();
        private static double _nextLook = double.MinValue;
        private static bool _announced;
        private static string Path => System.IO.Path.Combine(Paths.BepInExRootPath, "SbgSettingsDump.txt");

        internal static void Tick()
        {
            if (Time.timeAsDouble < _nextLook) return;
            _nextLook = Time.timeAsDouble + 2.0;
            try { Look(); }
            catch (Exception e) { Plugin.Log.LogWarning("Settings dump failed: " + e.Message); }
        }

        private static void Look()
        {
            if (!_announced)
            {
                _announced = true;
                if (!File.Exists(Path)) File.WriteAllText(Path, $"# SBG Shields settings dump. Every GameManager.*Settings object, public members only, appended as the game loads them.\n");
                Plugin.Log.LogInfo($"Settings dump: writing to {Path} as settings objects appear (some only exist once a hole is running).");
            }

            foreach (var prop in typeof(GameManager).GetProperties(BindingFlags.Public | BindingFlags.Static))
            {
                if (!prop.Name.EndsWith("Settings", StringComparison.Ordinal) || _written.Contains(prop.Name)) continue;
                object obj;
                try { obj = prop.GetValue(null); } catch { continue; }
                if (obj == null) continue;

                // One object that throws must not stall every object after it, forever.
                _written.Add(prop.Name);
                var sb = new StringBuilder();
                sb.AppendLine($"\n## {prop.Name} ({obj.GetType().Name}) at {DateTime.Now:HH:mm:ss}");
                try { DumpObject(sb, obj, ""); }
                catch (Exception e) { sb.AppendLine($"  <dump aborted: {e.GetType().Name}: {e.Message}>"); }
                File.AppendAllText(Path, sb.ToString());
                Plugin.Log.LogInfo($"Settings dump: wrote {prop.Name}.");
            }
        }

        private static void DumpObject(StringBuilder sb, object obj, string indent)
        {
            var t = obj.GetType();
            foreach (var f in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
                Line(sb, indent, f.Name, Safe(() => f.GetValue(obj)));
            foreach (var p in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.GetIndexParameters().Length > 0 || !p.CanRead) continue;
                Line(sb, indent, p.Name, Safe(() => p.GetValue(obj)));
            }
        }

        private static object Safe(Func<object> get) { try { return get(); } catch (Exception e) { return "<" + e.GetType().Name + ">"; } }

        private static void Line(StringBuilder sb, string indent, string name, object v)
        {
            if (v == null) { sb.AppendLine($"{indent}{name} = null"); return; }
            var t = v.GetType();
            if (t.IsPrimitive || t.IsEnum || v is string || v is Vector2 || v is Vector3 || v is Vector4 || v is Color || v is Quaternion || v is LayerMask)
            {
                sb.AppendLine($"{indent}{name} = {v}");
                return;
            }
            if (v is AnimationCurve curve)
            {
                sb.Append($"{indent}{name} = curve[");
                foreach (var k in curve.keys) sb.Append($"({k.time:0.###},{k.value:0.###}) ");
                sb.AppendLine("]");
                return;
            }
            if (v is UnityEngine.Object uo) { sb.AppendLine($"{indent}{name} = <{t.Name} '{uo.name}'>"); return; }
            if (v is IEnumerable list && !(v is string))
            {
                int n = 0;
                sb.AppendLine($"{indent}{name} = [");
                foreach (var item in list)
                {
                    if (n++ >= 64) { sb.AppendLine($"{indent}  ..."); break; }
                    if (item == null) { sb.AppendLine($"{indent}  null"); continue; }
                    var it = item.GetType();
                    if (it.IsPrimitive || it.IsEnum || item is string || item is UnityEngine.Object) Line(sb, indent + "  ", "-", item);
                    else { sb.AppendLine($"{indent}  - {it.Name}"); DumpObject(sb, item, indent + "    "); }
                }
                sb.AppendLine($"{indent}]");
                return;
            }
            // A nested plain struct/class (e.g. a per-item block): one level down.
            if (indent.Length < 8) { sb.AppendLine($"{indent}{name} ({t.Name}):"); DumpObject(sb, v, indent + "  "); }
            else sb.AppendLine($"{indent}{name} = <{t.Name}>");
        }
    }
}
#endif

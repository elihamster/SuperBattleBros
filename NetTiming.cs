using System;
using System.Reflection;
using System.Text;
using Mirror;
using UnityEngine;

namespace SbgShields
{
    /// <summary>
    /// Read-only: logs the game's network timing (Mirror's send rate, the snapshot
    /// interpolation buffer, the measured ping) once per connection, 20 seconds in, so
    /// there are real numbers before anything is tuned. Changes nothing.
    /// </summary>
    internal static class NetTiming
    {
        private static double _connectedAt = -1;
        private static bool _logged;

        internal static void Tick()
        {
            if (!NetworkClient.isConnected) { _connectedAt = -1; _logged = false; return; }
            double now = Time.timeAsDouble;
            if (_connectedAt < 0) _connectedAt = now;
            if (_logged || now - _connectedAt < 20.0) return;
            _logged = true;

            var sb = new StringBuilder("Net timing: ");
            sb.Append(NetworkServer.active ? "host" : "client");
            try { var nm = NetworkManager.singleton; if (nm != null) sb.Append($", sendRate {nm.sendRate}/s"); } catch { }
            try { sb.Append($", ping {NetworkTime.rtt * 1000.0:0} ms (jitter {NetworkTime.rttVariance * 1000.0:0})"); } catch { }
            sb.Append(", snapshot buffer: ").Append(Fields(typeof(NetworkClient), "snapshotSettings"));
            sb.Append(", bufferTime ").Append(Member(typeof(NetworkClient), "bufferTime"));
            Plugin.Log.LogInfo(sb.ToString());
        }

        private static string Member(Type t, string name)
        {
            try
            {
                const BindingFlags f = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
                var p = t.GetProperty(name, f);
                if (p != null) return Format(p.GetValue(null));
                var fi = t.GetField(name, f);
                if (fi != null) return Format(fi.GetValue(null));
            }
            catch { }
            return "?";
        }

        private static string Fields(Type t, string name)
        {
            try
            {
                var fi = t.GetField(name, BindingFlags.Public | BindingFlags.Static);
                var v = fi?.GetValue(null);
                if (v == null) return "?";
                var sb = new StringBuilder("{");
                foreach (var f in v.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                    sb.Append(sb.Length > 1 ? ", " : "").Append(f.Name).Append(' ').Append(Format(f.GetValue(v)));
                return sb.Append('}').ToString();
            }
            catch { return "?"; }
        }

        private static string Format(object o) =>
            o is double d ? d.ToString("0.###") : o is float fl ? fl.ToString("0.###") : o?.ToString() ?? "null";
    }
}

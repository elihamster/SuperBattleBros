using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace SbgShields
{
    /// <summary>
    /// The mod's own wire. Everything the mod adds on top of vanilla -- percent, the
    /// star KO, a parry -- exists only on the machine of the player it belongs to,
    /// because vanilla replicates knockouts, shields and the break explosion and
    /// nothing else. This carries the rest.
    ///
    /// Rules that keep it safe:
    ///  - Nothing is ever SENT unless every remote player has announced this exact
    ///    version (ModHandshake.AllPeersConfirmed, stricter than the gameplay gate,
    ///    which grants a newcomer a grace period). Mirror disconnects a peer that
    ///    receives a message id it has no handler for; a vanilla peer must never get one.
    ///  - Handlers are registered on this client and, when hosting, on the server as
    ///    soon as Mirror is up, so a message cannot arrive before its handler.
    ///  - The server accepts a message only from the connection that owns the player
    ///    it is about, clamps every value, and only then fans it out. No client can
    ///    speak for another.
    ///  - Receiving drives VFX and a cache of other players' percent. It never drives
    ///    gameplay state; the victim's own machine still decides every knockout.
    ///
    /// Mirror's weaver generates writers for the game's own message types at build
    /// time; this assembly is not weaved, so the writer and reader are registered by
    /// hand. The message id is Mirror's stable hash of the struct's full name.
    /// </summary>
    internal static class SbgNet
    {
        /// <summary>
        /// Percent and Pips: a value. StarKo: an event. Parry: an event, A = the freeze length.
        /// ParryStun: an event aimed at Target (the attacker), whose own game knocks them down.
        /// ParryReflect: A = seconds the sender's bubble bounces homing items back.
        /// </summary>
        internal enum Kind : byte { Percent = 1, StarKo = 2, Parry = 3, Pips = 4, ParryStun = 5, ParryReflect = 6 }
        private const byte MaxKind = (byte)Kind.ParryReflect;

        internal struct Msg : NetworkMessage
        {
            public byte  Kind;
            public uint  NetId;    // who the message is about: always the sender
            public float A;
            public uint  Target;   // the other player involved, or 0
        }

        private static bool _serverReg, _clientReg, _serializersReg;
        private static readonly HashSet<string> _warned = new HashSet<string>();
        private static double _lastPercentSend = double.MinValue;
        private static float  _lastPercentSent = float.MinValue;
        private static double _lastPipsSend = double.MinValue;
        private static float  _lastPipsSent = float.MinValue;
        private static readonly Dictionary<uint, float> _remotePercent = new Dictionary<uint, float>();
        private static readonly Dictionary<uint, float> _remotePips = new Dictionary<uint, float>();
        private static readonly Dictionary<uint, float> _remotePeak = new Dictionary<uint, float>();

        /// <summary>Other players' peak percent this hole, for the hole summary line. Empty when nobody has told us.</summary>
        internal static string RemotePeaksText()
        {
            if (_remotePeak.Count == 0) return "";
            var sb = new System.Text.StringBuilder(" Others' peaks:");
            foreach (var kv in _remotePeak)
            {
                var p = FindPlayer(kv.Key);
                string name; try { name = p != null ? p.PlayerId.PlayerNameNoRichText : "?"; } catch { name = "?"; }
                sb.Append(' ').Append(name).Append(' ').Append(kv.Value.ToString("0")).Append('%');
            }
            return sb.Append('.').ToString();
        }

        internal static void ResetRemotePeaks() => _remotePeak.Clear();

        private static void EnsureSerializers()
        {
            if (_serializersReg) return;
            _serializersReg = true;
            Writer<Msg>.write = (w, m) => { w.WriteByte(m.Kind); w.WriteUInt(m.NetId); w.WriteFloat(m.A); w.WriteUInt(m.Target); };
            Reader<Msg>.read  = r => new Msg { Kind = r.ReadByte(), NetId = r.ReadUInt(), A = r.ReadFloat(), Target = r.ReadUInt() };
        }

        internal static void Tick()
        {
            try
            {
                EnsureSerializers();

                if (NetworkServer.active)
                {
                    if (!_serverReg) { NetworkServer.ReplaceHandler<Msg>(OnServer, true); _serverReg = true; Plugin.Log.LogInfo("SbgNet: server handler registered."); }
                }
                else _serverReg = false;

                if (NetworkClient.active)
                {
                    if (!_clientReg) { NetworkClient.ReplaceHandler<Msg>(OnClient, true); _clientReg = true; Plugin.Log.LogInfo("SbgNet: client handler registered."); }
                }
                else
                {
                    _clientReg = false;
                    _remotePercent.Clear();
                    _remotePips.Clear();
                    _remotePeak.Clear();
                    _lastPercentSent = float.MinValue;
                    _lastPipsSent = float.MinValue;
                }

                // Percent: on change, at most ten times a second, plus a refresh every two
                // seconds so a late joiner and a reconnect catch up without a request.
                if (ModHandshake.AllPeersConfirmed && NetworkClient.isConnected)
                {
                    double now = Time.timeAsDouble;
                    float pct = Plugin.PercentEnabled.Value && ShieldState.InPlayableHole ? ShieldState.Percent : 0f;
                    bool changed = Mathf.Abs(pct - _lastPercentSent) >= 0.5f;
                    if ((changed && now - _lastPercentSend >= 0.1) || now - _lastPercentSend >= 2.0)
                    {
                        if (Send(Kind.Percent, pct)) { _lastPercentSent = pct; _lastPercentSend = now; }
                    }

                    // Pips as a fraction, same cadence: the bubble's brightness on other
                    // screens is drawn from this.
                    float pips = ShieldState.PipFraction;
                    bool pipsChanged = Mathf.Abs(pips - _lastPipsSent) >= 0.01f;
                    if ((pipsChanged && now - _lastPipsSend >= 0.05) || now - _lastPipsSend >= 2.0)
                    {
                        if (Send(Kind.Pips, pips)) { _lastPipsSent = pips; _lastPipsSend = now; }
                    }
                }
            }
            catch (Exception e) { WarnOnce("tick", e); }
        }

        /// <summary>Send a message about the local player. False if it could not go out.</summary>
        internal static bool Send(Kind kind, float a) => SendInternal(kind, a, 0u);

        /// <summary>Send a message about the local player that involves another player (the parry stun's target).</summary>
        internal static bool SendTo(Kind kind, PlayerInfo target) => target != null && SendInternal(kind, 0f, target.netId);

        private static bool SendInternal(Kind kind, float a, uint target)
        {
            if (!ModHandshake.AllPeersConfirmed) return false;   // never into a lobby that might hold a vanilla peer
            try
            {
                if (!NetworkClient.isConnected) return false;
                var local = GameManager.LocalPlayerInfo;
                if (local == null) return false;
                NetworkClient.Send(new Msg { Kind = (byte)kind, NetId = local.netId, A = a, Target = target });
                return true;
            }
            catch (Exception e) { WarnOnce("send", e); return false; }
        }

        /// <summary>
        /// The host relays a player's message to the players who can read it, and to
        /// nobody else. Mirror adds a connection the moment a joiner's transport connects,
        /// long before anyone knows whether they have the mod, and a vanilla client that
        /// receives an unknown message is disconnected. So: nothing is relayed while the
        /// host's own gate is closed, the host's local client always gets it, and a remote
        /// connection gets it only if its player has announced exactly this version.
        /// (SendToAll, used until 0.7.33, reached joiners that were still loading.)
        /// </summary>
        private static void OnServer(NetworkConnectionToClient conn, Msg m)
        {
            try
            {
                if (conn == null) return;
                if (m.Kind < (byte)Kind.Percent || m.Kind > MaxKind) return;
                if (m.Target != 0u && FindPlayer(m.Target) == null) return;   // a target that is not in this lobby
                if (float.IsNaN(m.A) || float.IsInfinity(m.A)) return;
                var p = FindPlayer(m.NetId);
                if (p == null || p.connectionToClient != conn) return;   // only about yourself
                if (!ModHandshake.GameplayEnabled) return;
                m.A = Mathf.Clamp(m.A, 0f, 1000f);
                Relay(m);
            }
            catch (Exception e) { WarnOnce("server", e); }
        }

        private static void Relay(Msg m)
        {
            var local = NetworkServer.localConnection;
            if (local != null) local.Send(m);
            var remote = GameManager.RemotePlayers;
            if (remote == null) return;
            foreach (var p in remote)
            {
                if (p == null || !ModHandshake.IsConfirmed(p)) continue;
                var c = p.connectionToClient;
                if (c != null && c != local) c.Send(m);
            }
        }

        private static void OnClient(Msg m)
        {
            try
            {
                var p = FindPlayer(m.NetId);
                if (p == null) return;
                if (Local.Is(p)) return;   // our own echo; we already know
                switch ((Kind)m.Kind)
                {
                    case Kind.Percent:
                        _remotePercent[m.NetId] = m.A;
                        if (!_remotePeak.TryGetValue(m.NetId, out float peak) || m.A > peak) _remotePeak[m.NetId] = m.A;
                        break;
                    case Kind.StarKo:
                        KillZone.PlayRemoteStar(p);
                        break;
                    case Kind.Parry:
                        RemoteParryFeedback(p, m.A);
                        break;
                    case Kind.Pips:
                        _remotePips[m.NetId] = Mathf.Clamp01(m.A);
                        break;
                    case Kind.ParryStun:
                        var local = GameManager.LocalPlayerInfo;
                        if (local != null && m.Target == local.netId) ParryStun.ApplyToLocal(p);
                        break;
                    case Kind.ParryReflect:
                        ParryReflect.Mark(p, Mathf.Clamp(m.A, 0f, 2f));
                        break;
                }
            }
            catch (Exception e) { WarnOnce("client", e); }
        }

        /// <summary>Another player's percent, if they have told us. False for players we have not heard from.</summary>
        internal static bool TryGetPercent(PlayerInfo p, out float pct)
        {
            pct = 0f;
            if (p == null) return false;
            try { return _remotePercent.TryGetValue(p.netId, out pct); } catch { return false; }
        }

        /// <summary>Another player's pips as a 0..1 fraction, if they have told us.</summary>
        internal static bool TryGetPipFraction(PlayerInfo p, out float fraction)
        {
            fraction = 1f;
            if (p == null) return false;
            try { return _remotePips.TryGetValue(p.netId, out fraction); } catch { return false; }
        }

        private static void RemoteParryFeedback(PlayerInfo p, float freeze)
        {
            // Another player's parry: the same flash, burst, sound and kick they saw, at their
            // bubble, and their pose held for the impact frame. Their body is already still
            // (their own game holds it); this stops the animation on our copy too.
            try { HitStop.Begin(p, Mathf.Clamp(freeze, 0f, 0.5f)); }
            catch (Exception e) { if (Plugin.VerboseLogging.Value) Plugin.Log.LogWarning("Remote parry freeze: " + e.Message); }
            try { ParryReflect.End(p); } catch { }
            try { ParryFx.Play(p); }
            catch (Exception e) { if (Plugin.VerboseLogging.Value) Plugin.Log.LogWarning("Remote parry effects: " + e.Message); }
        }

        private static PlayerInfo FindPlayer(uint netId)
        {
            try
            {
                var local = GameManager.LocalPlayerInfo;
                if (local != null && local.netId == netId) return local;
                var remote = GameManager.RemotePlayers;
                if (remote != null)
                    foreach (var p in remote)
                        if (p != null && p.netId == netId) return p;
            }
            catch { }
            return null;
        }

        /// <summary>One full line per kind of failure, so a second, different failure is not hidden behind the first.</summary>
        private static void WarnOnce(string where, Exception e)
        {
            if (!_warned.Add(where + ":" + e.GetType().Name)) return;
            Plugin.Log.LogWarning($"SbgNet ({where}) failed; further {e.GetType().Name} errors here are not logged: {e}");
        }

        internal static void Shutdown()
        {
            try { NetworkServer.UnregisterHandler<Msg>(); } catch { }
            try { NetworkClient.UnregisterHandler<Msg>(); } catch { }
            _serverReg = _clientReg = false;
            _remotePercent.Clear();
            _remotePips.Clear();
        }
    }
}

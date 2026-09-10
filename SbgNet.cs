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
    ///  - Nothing is ever SENT unless the handshake gate is open, i.e. every peer has
    ///    announced this exact version. Mirror disconnects a peer that receives a
    ///    message id it has no handler for; a vanilla peer must never get one.
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
        internal enum Kind : byte { Percent = 1, StarKo = 2, Parry = 3 }

        internal struct Msg : NetworkMessage
        {
            public byte  Kind;
            public uint  NetId;
            public float A;
        }

        private static bool _serverReg, _clientReg, _serializersReg, _warned;
        private static double _lastPercentSend = double.MinValue;
        private static float  _lastPercentSent = float.MinValue;
        private static readonly Dictionary<uint, float> _remotePercent = new Dictionary<uint, float>();
        private static int _received, _sent;

        internal static int Received => _received;
        internal static int Sent => _sent;

        private static void EnsureSerializers()
        {
            if (_serializersReg) return;
            _serializersReg = true;
            Writer<Msg>.write = (w, m) => { w.WriteByte(m.Kind); w.WriteUInt(m.NetId); w.WriteFloat(m.A); };
            Reader<Msg>.read  = r => new Msg { Kind = r.ReadByte(), NetId = r.ReadUInt(), A = r.ReadFloat() };
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
                    _lastPercentSent = float.MinValue;
                }

                // Percent: on change, at most ten times a second, plus a refresh every two
                // seconds so a late joiner and a reconnect catch up without a request.
                if (ModHandshake.GameplayEnabled && NetworkClient.isConnected)
                {
                    double now = Time.timeAsDouble;
                    float pct = Plugin.PercentEnabled.Value && ShieldState.InPlayableHole ? ShieldState.Percent : 0f;
                    bool changed = Mathf.Abs(pct - _lastPercentSent) >= 0.5f;
                    if ((changed && now - _lastPercentSend >= 0.1) || now - _lastPercentSend >= 2.0)
                    {
                        if (Send(Kind.Percent, pct)) { _lastPercentSent = pct; _lastPercentSend = now; }
                    }
                }
            }
            catch (Exception e) { WarnOnce("tick", e); }
        }

        /// <summary>Send a message about the local player. False if it could not go out.</summary>
        internal static bool Send(Kind kind, float a)
        {
            if (!ModHandshake.GameplayEnabled) return false;   // never into a lobby that might hold a vanilla peer
            try
            {
                if (!NetworkClient.isConnected) return false;
                var local = GameManager.LocalPlayerInfo;
                if (local == null) return false;
                NetworkClient.Send(new Msg { Kind = (byte)kind, NetId = local.netId, A = a });
                _sent++;
                return true;
            }
            catch (Exception e) { WarnOnce("send", e); return false; }
        }

        private static void OnServer(NetworkConnectionToClient conn, Msg m)
        {
            try
            {
                if (conn == null) return;
                if (m.Kind < (byte)Kind.Percent || m.Kind > (byte)Kind.Parry) return;
                if (float.IsNaN(m.A) || float.IsInfinity(m.A)) return;
                var p = FindPlayer(m.NetId);
                if (p == null || p.connectionToClient != conn) return;   // only about yourself
                m.A = Mathf.Clamp(m.A, 0f, 1000f);
                NetworkServer.SendToAll(m);
            }
            catch (Exception e) { WarnOnce("server", e); }
        }

        private static void OnClient(Msg m)
        {
            try
            {
                _received++;
                var p = FindPlayer(m.NetId);
                if (p == null) return;
                if (Local.Is(p)) return;   // our own echo; we already know
                switch ((Kind)m.Kind)
                {
                    case Kind.Percent:
                        _remotePercent[m.NetId] = m.A;
                        break;
                    case Kind.StarKo:
                        KillZone.PlayRemoteStar(p);
                        break;
                    case Kind.Parry:
                        RemoteParryFeedback(p);
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

        private static void RemoteParryFeedback(PlayerInfo p)
        {
            // What the person who just got parried hears: the game's own blocked-hit sting, where it happened.
            if (!Plugin.PerfectParrySound.Value) return;
            try { FMODUnity.RuntimeManager.PlayOneShot(GameManager.AudioSettings.KnockoutImmunityBlockedKnockoutEvent, p.transform.position); }
            catch { }
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

        private static void WarnOnce(string where, Exception e)
        {
            if (_warned) return;
            _warned = true;
            Plugin.Log.LogWarning($"SbgNet ({where}) failed; shared visuals are off for this session: {e.GetType().Name}: {e.Message}");
        }

        internal static void Shutdown()
        {
            try { NetworkServer.UnregisterHandler<Msg>(); } catch { }
            try { NetworkClient.UnregisterHandler<Msg>(); } catch { }
            _serverReg = _clientReg = false;
            _remotePercent.Clear();
        }
    }
}

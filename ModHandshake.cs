using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace SbgShields
{
    /// <summary>
    /// Makes sure everybody in the lobby is running the same version of this mod, and
    /// puts the mod to sleep if they are not.
    ///
    /// WHY CHAT: the obvious implementation is a custom Mirror message, but Mirror
    /// disconnects a peer that receives a message id it has no handler for. A modded
    /// client talking to a vanilla host would get itself kicked; a modded host talking
    /// to vanilla clients would kick them. So instead we announce over the game's own
    /// text chat, which is an ordinary feature every build understands. A vanilla
    /// player simply sees one line of text saying who is modded, which is exactly the
    /// information they need anyway. Modded clients swallow the line before it renders.
    ///
    /// WHAT THIS IS NOT: this is not anti-cheat. Everything here runs on the client,
    /// so anyone willing to edit the DLL can strip it out. Real enforcement would have
    /// to live on the server, and the server is the stock game. What this does prevent
    /// is the accidental case -- someone joins on an old version, or without the mod,
    /// and the match silently plays by two different rule sets.
    /// </summary>
    internal static class ModHandshake
    {
        internal const string Token = "SBGSHIELDS";

        private class Peer
        {
            public string Version;
            public double FirstSeen;
            public bool   Announced;
            public bool   Warned;
            public bool   Nudged;         // we re-announced once because they stayed silent
            public bool   RepeatReplied;  // we answered one repeat announce from them since our last reset
        }

        private static readonly Dictionary<ulong, Peer> _peers = new Dictionary<ulong, Peer>();
        private static readonly List<PlayerInfo> _scratch = new List<PlayerInfo>();
        private static readonly HashSet<ulong> _present = new HashSet<ulong>();
        private static readonly List<ulong> _gone = new List<ulong>();
        private static readonly List<ulong> _forget = new List<ulong>();

        // The 3s minimum interval IS the rate limit. There is no message ceiling: a long
        // session with people cycling in and out would hit any fixed cap, and the mod
        // would then stop handshaking without saying so.
        private const double MinAnnounceInterval = 3.0;

        private static double _lastAnnounce = double.MinValue;
        private static double _nextAnnounceDue = double.MaxValue;   // set by Reset()
        private static bool   _announcePending = true;
        // A second, unconditional announce a few seconds after the first. Every scene
        // change resets everyone, and a client that loads slower than the host has no
        // chat manager yet when the host's first line goes out. The line is lost and the
        // client times the host out ("Hamster does not have SBG Shields installed").
        private static double _secondAnnounceDue = double.MaxValue;
        private static bool   _secondAnnouncePending;
        // Starts CLOSED. The mod does not run until something has affirmatively said
        // the lobby is private and the peers check out. Failing open would mean a
        // future game update that breaks the lobby-mode read silently re-enables the
        // mod everywhere.
        private static bool   _gateOpen;
        private static bool   _evaluatedOnce;
        private static string _blockReason;

        /// <summary>False when someone in the lobby is unmodded or on a different version.</summary>
        internal static bool GameplayEnabled => _gateOpen;

        /// <summary>
        /// The strict gate, for anything that SENDS. GameplayEnabled gives a newcomer
        /// HandshakeTimeout seconds of grace before the mod stands down, which is right
        /// for gameplay and wrong for the wire: a custom message reaching a peer who
        /// turns out to be vanilla disconnects them. So sending requires every remote
        /// player to have announced this exact version, no grace at all.
        /// </summary>
        internal static bool AllPeersConfirmed
        {
            get
            {
                if (!_gateOpen) return false;
                try
                {
                    var r = GameManager.RemotePlayers;
                    if (r == null) return true;
                    foreach (var p in r)
                    {
                        ulong g = GuidOf(p);
                        if (g == 0UL) return false;
                        if (!_peers.TryGetValue(g, out var peer) || !peer.Announced || peer.Version != Plugin.Version) return false;
                    }
                    return true;
                }
                catch { return false; }
            }
        }
        internal static string BlockReason => _blockReason;

        /// <summary>When the gate last closed, so the HUD knows how long to shout.</summary>
        internal static double BlockedSince { get; private set; } = double.MinValue;

        /// <summary>
        /// Clears unverified peers and re-arms our own announcement. Players who already
        /// announced are KEPT: wiping them would gate the first couple of seconds of every
        /// tee-off closed while everyone re-announced, for no gain. The per-tick sweep
        /// removes them the moment they actually leave.
        /// </summary>
        internal static void Reset(string why)
        {
            _forget.Clear();
            foreach (var kv in _peers)
                if (!kv.Value.Announced) _forget.Add(kv.Key);
            foreach (var g in _forget) _peers.Remove(g);
            foreach (var kv in _peers) kv.Value.RepeatReplied = false;   // one reply per peer per reset
            _gateOpen = false;
            _evaluatedOnce = false;
            _blockReason = null;
            _announcePending = true;
            _nextAnnounceDue = Time.timeAsDouble + 1.5; // let the lobby settle before talking
            _secondAnnouncePending = true;
            _secondAnnounceDue = Time.timeAsDouble + 5.0;
            if (Plugin.VerboseLogging.Value) Plugin.Log.LogInfo($"Handshake reset ({why}).");
        }

        // ---- Sending -----------------------------------------------------------

        /// <summary>
        /// Sent ONCE when we join, and once more only when a player we have not met
        /// announces themselves (so they learn about us in return). Never on a timer:
        /// in a lobby where everyone has the mod these lines are swallowed and nobody
        /// ever sees one, and in a lobby with a vanilla player they see a single line
        /// per modded player rather than a repeating drip.
        /// </summary>
        /// <summary>
        /// Returns true only if the message actually went out. A failed send (no chat
        /// manager yet, e.g. sitting in the main menu) must NOT count as done, or the
        /// announcement is silently lost and nobody in the lobby ever hears from us.
        /// </summary>
        private static bool Announce()
        {
            double now = Time.timeAsDouble;
            if (now - _lastAnnounce < MinAnnounceInterval) return false;

            string msg = Token + "/" + Plugin.Version;
            if (!ChatBypass.Send(msg)) return false;

            _lastAnnounce = now;
            // Always on: when a peer says we never announced, this line is the first thing to check.
            Plugin.Log.LogInfo($"Handshake announced: {msg} ({(Mirror.NetworkServer.active ? "as host" : "as client")})");
            return true;
        }

        // ---- Receiving ---------------------------------------------------------

        /// <summary>
        /// Called from the chat receive patch. Returns true if this was one of our
        /// announcements and should not be shown to the player.
        /// </summary>
        internal static bool TryConsume(string message, PlayerInfo sender)
        {
            if (message == null) return false;
            if (!message.StartsWith(Token + "/", StringComparison.Ordinal)) return false;

            string version = message.Substring(Token.Length + 1).Trim();

            // A handshake line whose sender did not resolve on this client (their
            // PlayerInfo not spawned here yet, typically right after a join). We cannot
            // credit it to anyone, so swallow it and say so; the nudge below gives the
            // peer a second chance before the timeout.
            if (sender == null)
            {
                Plugin.Log.LogWarning($"Handshake line '{message}' arrived with no sender; could not be credited to a player.");
                return true;
            }

            // Our own announcement comes back to us. Swallow it, but do not record
            // ourselves as a peer: the departed-player sweep compares against the
            // REMOTE player list, so a local entry would be forgotten and re-added
            // on a loop.
            if (ReferenceEquals(sender, GameManager.LocalPlayerInfo)) return true;

            ulong guid = GuidOf(sender);
            if (guid == 0UL) return true;

            bool isNewPeer = !_peers.TryGetValue(guid, out var p) || !p.Announced;
            if (p == null)
            {
                p = new Peer { FirstSeen = Time.timeAsDouble };
                _peers[guid] = p;
            }
            p.Version = version;
            p.Announced = true;

            Plugin.Log.LogInfo($"{NameOf(sender)} is running SBG Shields {version}.");
            if (isNewPeer)
            {
                // They just arrived and cannot know about us yet, so reply once.
                // A small random delay stops everyone answering on the same frame.
                _announcePending = true;
                _nextAnnounceDue = Time.timeAsDouble + UnityEngine.Random.Range(0.3f, 1.2f);
            }
            else if (!p.RepeatReplied)
            {
                // A player we already know is announcing AGAIN: they reset (scene change)
                // or they nudged because they never heard us. Either way they are waiting
                // on our line, so answer once. Once per peer per reset, so two modded
                // clients cannot ping-pong forever.
                p.RepeatReplied = true;
                _announcePending = true;
                _nextAnnounceDue = Time.timeAsDouble + UnityEngine.Random.Range(0.3f, 1.2f);
            }
            Evaluate();
            return true;
        }

        // ---- The gate ----------------------------------------------------------

        internal static void Tick()
        {
            // The lobby check comes FIRST and is not optional. Turning off the version
            // check must never silently turn off the public-lobby block as well.
            // Also: nothing to announce in a lobby we will not run in, so stay silent
            // rather than advertising a mod to strangers. The pending flag is KEPT, not
            // cleared: the lobby reads as public for a moment on every scene change, and
            // clearing it there meant the host never announced after a match start, so
            // every client timed the host out at the first tee-off (0.7.25 and earlier).
            if (!LobbyAllowed(out string lobbyProblem))
            {
                SetGate(false, lobbyProblem);
                return;
            }

            if (!Plugin.RequireAllPlayersModded.Value)
            {
                SetGate(true, null);
                return;
            }

            double now = Time.timeAsDouble;
            if (_announcePending && now >= _nextAnnounceDue)
            {
                if (Announce()) _announcePending = false;
                else _nextAnnounceDue = now + 1.0;   // retry until there is a chat manager
            }
            else if (_secondAnnouncePending && now >= _secondAnnounceDue)
            {
                if (Announce()) _secondAnnouncePending = false;
                else _secondAnnounceDue = now + 1.0;
            }

            // Track who is here, so we can time out anyone who never says hello.
            _scratch.Clear();
            try { var r = GameManager.RemotePlayers; if (r != null) _scratch.AddRange(r); } catch { }

            foreach (var p in _scratch)
            {
                ulong guid = GuidOf(p);
                if (guid == 0UL) continue;
                if (_peers.TryGetValue(guid, out var peer)) continue;

                // Somebody we have never seen. They cannot know about us, so say hello.
                _peers[guid] = new Peer { FirstSeen = now };
                _announcePending = true;
                _nextAnnounceDue = now + UnityEngine.Random.Range(0.3f, 1.2f);
                if (Plugin.VerboseLogging.Value) Plugin.Log.LogInfo($"Handshake: new player {guid}, will announce.");
            }

            // Forget anyone who left, every tick. Comparing counts was not enough: one
            // player leaving as another joins keeps the totals equal and the stale entry
            // would survive, letting someone rejoin without the mod and still pass.
            _present.Clear();
            foreach (var p in _scratch)
            {
                ulong g = GuidOf(p);
                if (g != 0UL) _present.Add(g);
            }
            _gone.Clear();
            foreach (var kv in _peers)
                if (!_present.Contains(kv.Key)) _gone.Add(kv.Key);
            foreach (var g in _gone)
            {
                _peers.Remove(g);
                if (Plugin.VerboseLogging.Value) Plugin.Log.LogInfo($"Handshake: forgetting departed player {g}.");
            }

            Evaluate();
        }

        /// <summary>
        /// Reads the lobby's privacy setting. It is a SyncVar on MatchSetupMenu, so
        /// clients see the host's real value, not a local default.
        /// </summary>
        internal static LobbyMode CurrentLobbyMode()
        {
            try
            {
                if (SingletonNetworkBehaviour<MatchSetupMenu>.HasInstance)
                {
                    var menu = SingletonNetworkBehaviour<MatchSetupMenu>.Instance;
                    if (menu != null) return menu.lobbyMode;
                }
                return BNetworkManager.LobbyMode;
            }
            // If we cannot read it, assume the most restrictive answer.
            catch { return LobbyMode.Public; }
        }

        private static bool LobbyAllowed(out string why)
        {
            why = null;
            // Plugin.PrivateLobbiesOnly is a const true by design; there is deliberately
            // no way to switch this off from configuration.
            if (CurrentLobbyMode() != LobbyMode.Public) return true;
            why = "this is a public lobby";
            return false;
        }

        private static void Evaluate()
        {
            double now = Time.timeAsDouble;
            float timeout = Mathf.Max(2f, Plugin.HandshakeTimeout.Value);

            _scratch.Clear();
            try { var r = GameManager.RemotePlayers; if (r != null) _scratch.AddRange(r); } catch { }

            string problem;
            if (!LobbyAllowed(out problem))
            {
                SetGate(false, problem);
                return;
            }

            foreach (var p in _scratch)
            {
                ulong guid = GuidOf(p);
                if (guid == 0UL || !_peers.TryGetValue(guid, out var peer)) continue;

                if (peer.Announced)
                {
                    if (peer.Version != Plugin.Version)
                    {
                        problem = $"{NameOf(p)} is on SBG Shields {peer.Version}, you are on {Plugin.Version}";
                        if (!peer.Warned) { peer.Warned = true; Plugin.Log.LogWarning("Version mismatch: " + problem); }
                        break;
                    }
                }
                else if (now - peer.FirstSeen > timeout)
                {
                    problem = $"{NameOf(p)} does not have SBG Shields installed";
                    if (!peer.Warned) { peer.Warned = true; Plugin.Log.LogWarning(problem + "; the mod is standing down."); }
                    break;
                }
                else if (!peer.Nudged && now - peer.FirstSeen > timeout * 0.5f)
                {
                    // Halfway to giving up on them and still nothing. Our first announce
                    // may have gone out before their client could credit it (see
                    // TryConsume), so say hello once more. Bounded: once per peer.
                    peer.Nudged = true;
                    _announcePending = true;
                    _nextAnnounceDue = now;
                    Plugin.Log.LogInfo($"No announce from {NameOf(p)} after {now - peer.FirstSeen:0}s; announcing once more.");
                }
            }

            SetGate(problem == null, problem);
        }

        private static void SetGate(bool open, string problem)
        {
            if (open != _gateOpen || !_evaluatedOnce)
            {
                _gateOpen = open;
                if (!open) BlockedSince = Time.timeAsDouble;
                Plugin.Log.LogWarning(open
                    ? "SBG Shields is active."
                    : $"SBG Shields is inactive: {problem}. Vanilla rules apply.");
                if (!open) Plugin.ForceReleaseForHandshake();
            }
            _evaluatedOnce = true;
            _blockReason = problem;
        }

        private static ulong GuidOf(PlayerInfo p)
        {
            try { return p != null && p.PlayerId != null ? p.PlayerId.Guid : 0UL; }
            catch { return 0UL; }
        }

        private static string NameOf(PlayerInfo p)
        {
            try { return p.PlayerId.PlayerNameNoRichText; } catch { return "A player"; }
        }
    }

    /// <summary>
    /// Sends a chat message without the mute check. TextChatManager.SendChatMessage
    /// refuses to send anything when the player has chat muted, and pops a "chat is
    /// muted" notice while doing it -- which would break the handshake for anyone who
    /// plays with chat off. The underlying Command has no such check, so we call it
    /// directly. It is still rate limited server-side like any other message.
    /// </summary>
    internal static class ChatBypass
    {
        private static System.Reflection.MethodInfo _cmd;
        private static bool _looked;

        internal static bool Send(string message)
        {
            try
            {
                if (!SingletonNetworkBehaviour<TextChatManager>.HasInstance) return false;
                var inst = SingletonNetworkBehaviour<TextChatManager>.Instance;
                if (inst == null) return false;

                if (!_looked)
                {
                    _looked = true;
                    _cmd = AccessTools.Method(typeof(TextChatManager), "CmdSendMessageInternal",
                        new[] { typeof(string), typeof(Mirror.NetworkConnectionToClient) });
                    if (_cmd == null) Plugin.Log.LogWarning("Chat command not found; falling back to the normal send path.");
                }

                if (_cmd != null) { _cmd.Invoke(inst, new object[] { message, null }); return true; }
                TextChatManager.SendChatMessage(message);
                return true;
            }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Handshake send failed: " + e.Message);
                return false;
            }
        }
    }

    /// <summary>
    /// Swallows handshake lines before they reach the chat window. Anyone without the
    /// mod sees them as ordinary text, which is the intended fallback.
    /// </summary>
    [HarmonyPatch(typeof(TextChatManager), "UserCode_RpcMessage__String__PlayerInfo")]
    internal static class ChatHandshakePatch
    {
        [HarmonyPriority(Priority.Low)]   // can skip the original
        private static bool Prefix(string message, PlayerInfo sender)
        {
            try { return !ModHandshake.TryConsume(message, sender); }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Handshake receive failed: " + e.Message);
                return true;
            }
        }
    }
}

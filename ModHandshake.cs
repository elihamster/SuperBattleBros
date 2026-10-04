using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace SbgShields
{
    /// <summary>
    /// Makes sure everybody in the lobby is running the same version of this mod. It is
    /// an "every player needs it" mod: a modded HOST removes anyone who joins without it
    /// or on another version (with a chat line saying why), and a modded client in a
    /// lobby it cannot vouch for puts the mod to sleep.
    ///
    /// WHY CHAT: the obvious implementation is a custom Mirror message, but Mirror
    /// disconnects a peer that receives a message id it has no handler for. A modded
    /// client talking to a vanilla host would get itself kicked; a modded host talking
    /// to vanilla clients would kick them. So instead we announce over the game's own
    /// text chat, which is an ordinary feature every build understands. A vanilla
    /// player simply sees one line of text saying who is modded, which is exactly the
    /// information they need anyway. Modded clients swallow the line before it renders.
    ///
    /// WHAT THIS IS NOT: this is not anti-cheat. Everything here runs on the players'
    /// own machines, so anyone willing to edit the DLL can strip it out. What it does
    /// prevent is the accidental case -- someone joins on an old version, or without the
    /// mod, and the match silently plays by two different rule sets.
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
            public bool   Nudged;          // we re-announced once because they stayed silent
            public bool   RepeatReplied;   // we answered one repeat announce from them since our last reset
            public bool   HelloSinceReset; // they have announced since OUR last reset: they are loaded and ready
            public double KickedAt = double.MinValue;   // host only: when we asked the game to remove them
            public double ProblemSince = double.MinValue;   // client only: when this peer first looked unmodded or mismatched
        }

        private static readonly Dictionary<ulong, Peer> _peers = new Dictionary<ulong, Peer>();
        private static readonly List<PlayerInfo> _scratch = new List<PlayerInfo>();
        private static readonly HashSet<ulong> _present = new HashSet<ulong>();
        private static readonly List<ulong> _gone = new List<ulong>();
        private static readonly List<ulong> _forget = new List<ulong>();

        // The minimum interval IS the rate limit. The game's server drops chat past 5
        // messages in 10 s per player; 3.5 s spacing keeps us at 3. There is no message
        // ceiling: a long session with people cycling in and out would hit any fixed
        // cap, and the mod would then stop handshaking without saying so.
        private const double MinAnnounceInterval = 3.5;

        private static double _lastAnnounce = double.MinValue;
        private static double _nextAnnounceDue = double.MaxValue;   // set by Reset()
        private static bool   _announcePending = true;
        private static double _resetAt = double.MinValue;
        private static bool   _warnedNotReady;

        // WHY WE KEEP TALKING. Every scene change resets everyone (Plugin's scene hook calls
        // Reset), and while a client is
        // loading a course Mirror marks it "not ready" and silently drops traffic both
        // ways: RPCs the host sends to it (our announce never arrives) and commands it
        // sends (its own announce never leaves, though the call "succeeds"). A fixed
        // announce at +1.5 s and +5 s reached nobody on a slow load, and the client timed
        // the host out at the first tee-off ("Hamster does not have SBG Shields
        // installed", 0.7.26-0.7.30). So: a client never counts a send while not ready,
        // and everyone re-announces every few seconds after a reset until every player
        // present has said hello since that reset, bounded by the timeout window.
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
        /// <summary>This player announced exactly our version. The host relays SbgNet messages only to these.</summary>
        internal static bool IsConfirmed(PlayerInfo p)
        {
            ulong g = GuidOf(p);
            return g != 0UL && _peers.TryGetValue(g, out var peer) && peer.Announced && peer.Version == Plugin.Version;
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
            foreach (var kv in _peers) { kv.Value.RepeatReplied = false; kv.Value.HelloSinceReset = false; }   // one reply per peer per reset; everyone owes a hello
            _gateOpen = false;
            _evaluatedOnce = false;
            _blockReason = null;
            _announcePending = true;
            _resetAt = Time.timeAsDouble;
            _nextAnnounceDue = Time.timeAsDouble + 1.5; // let the lobby settle before talking
            if (Plugin.VerboseLogging.Value) Plugin.Log.LogInfo($"Handshake reset ({why}).");
        }

        // ---- Sending -----------------------------------------------------------

        /// <summary>
        /// Sent after every reset (join, scene load) and repeated at the rate limit until
        /// every player present has said hello back; also once in reply to a newcomer or
        /// to a known player announcing again. Modded players swallow these lines, so in a
        /// fully modded lobby nobody ever sees one.
        ///
        /// Returns true only if the message actually went out. A failed send (no chat
        /// manager yet, e.g. sitting in the main menu) must NOT count as done, or the
        /// announcement is silently lost and nobody in the lobby ever hears from us.
        /// </summary>
        private static bool Announce()
        {
            double now = Time.timeAsDouble;
            if (now - _lastAnnounce < MinAnnounceInterval) return false;

            // A client that is not ready (still loading the course) can call the chat
            // command all it likes; Mirror drops it. Do not count it, try again later.
            try
            {
                if (!Mirror.NetworkServer.active && Mirror.NetworkClient.active && !Mirror.NetworkClient.ready)
                {
                    if (!_warnedNotReady) { _warnedNotReady = true; Plugin.Log.LogInfo("Handshake: client not ready yet (loading); announce deferred."); }
                    return false;
                }
            }
            catch { }
            _warnedNotReady = false;

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
            p.HelloSinceReset = true;

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
                else _nextAnnounceDue = now + 1.0;   // retry until there is a chat manager / the client is ready
            }
            else if (!_announcePending && now - _resetAt < Mathf.Max(2f, Plugin.HandshakeTimeout.Value) + MinAnnounceInterval
                     && now - _lastAnnounce >= MinAnnounceInterval && AnyoneOwesHello())
            {
                // Somebody present has not said hello since our reset, so they may still
                // have been loading when our last line went out. Say it again, at the
                // rate limit, until they do or the timeout window closes.
                _announcePending = true;
                _nextAnnounceDue = now;
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

        /// <summary>True if any player present has not announced since our last reset.</summary>
        private static bool AnyoneOwesHello()
        {
            try
            {
                var r = GameManager.RemotePlayers;
                if (r == null) return false;
                foreach (var p in r)
                {
                    ulong g = GuidOf(p);
                    if (g == 0UL) continue;
                    if (!_peers.TryGetValue(g, out var peer) || !peer.HelloSinceReset) return true;
                }
            }
            catch { }
            return false;
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

            bool weAreHost = false;
            try { weAreHost = Mirror.NetworkServer.active; } catch { }
            bool hostVouches = !weAreHost && HostIsConfirmed();

            foreach (var p in _scratch)
            {
                ulong guid = GuidOf(p);
                if (guid == 0UL || !_peers.TryGetValue(guid, out var peer)) continue;

                string bad = null;
                if (peer.Announced)
                {
                    if (peer.Version != Plugin.Version)
                        bad = $"{NameOf(p)} is on SBG Shields {peer.Version}, this lobby runs {Plugin.Version}";
                }
                else if (now - peer.FirstSeen > timeout)
                    bad = $"{NameOf(p)} does not have SBG Shields installed";
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

                if (bad == null) { peer.ProblemSince = double.MinValue; continue; }

                // The host removes them: nobody plays a modded lobby without the mod.
                if (weAreHost)
                {
                    // Silence gets extra time before it costs someone their seat: a modded
                    // friend on a slow load cannot announce until their client is ready.
                    // A wrong version is certain the moment it is announced.
                    if (!peer.Announced && now - peer.FirstSeen <= timeout + KickExtraWait) continue;
                    if (peer.KickedAt == double.MinValue) { peer.KickedAt = now; Kick(p, bad); }
                    // Being removed: not a reason for the host to stand down -- for a few seconds.
                    // If they are still here after that the removal failed, and the host must
                    // not keep playing modded rules with a player who is not.
                    if (now - peer.KickedAt < KickConfirmWait) continue;
                    if (now - peer.KickedAt > KickConfirmWait + 10.0) { peer.KickedAt = now; Kick(p, bad + " (again)"); }
                }
                else
                {
                    // A client whose host has the mod waits a few seconds for the host to
                    // remove them, rather than flipping the whole lobby to vanilla and back.
                    if (peer.ProblemSince == double.MinValue) peer.ProblemSince = now;
                    if (hostVouches && !IsHost(p) && now - peer.ProblemSince < KickGrace) continue;
                }

                problem = bad;
                if (!peer.Warned) { peer.Warned = true; Plugin.Log.LogWarning(problem + "; the mod is standing down."); }
                break;
            }

            SetGate(problem == null, problem);
        }

        /// <summary>Seconds the host waits for a removal to take before treating the player as a problem.</summary>
        private const double KickConfirmWait = 4.0;

        /// <summary>Seconds past HandshakeTimeout the host waits on a silent player before removing them.</summary>
        private const double KickExtraWait = 8.0;

        /// <summary>
        /// Seconds a client waits for the host to remove a bad peer before standing down
        /// itself. Covers the host's extra wait plus a margin.
        /// </summary>
        private const double KickGrace = KickExtraWait + 4.0;

        private static bool IsHost(PlayerInfo p)
        {
            try { return CourseManager.TryGetPlayerState(p, out var s) && s.isHost; } catch { return false; }
        }

        /// <summary>The lobby's host has announced exactly our version, so it will enforce the rule for everyone.</summary>
        private static bool HostIsConfirmed()
        {
            try
            {
                var r = GameManager.RemotePlayers;
                if (r == null) return false;
                foreach (var p in r) if (IsHost(p)) return IsConfirmed(p);
            }
            catch { }
            return false;
        }

        private static System.Reflection.MethodInfo _disconnectWithMessage;
        private static bool _disconnectLooked;

        /// <summary>
        /// Host only. Says why in chat (everyone sees it, the player being removed
        /// included), then disconnects them with the game's own "kicked from lobby"
        /// message. Not the game's ServerKickConnection: that also bans them for the
        /// session, and a friend who installs the mod should be able to come straight back.
        /// </summary>
        private static void Kick(PlayerInfo p, string why)
        {
            string name = NameOf(p);
            Plugin.Log.LogWarning($"Removing {name} from the lobby: {why}.");
            try { ChatBypass.Send($"{name} was removed: this lobby runs the Super Battle Bros mod (SBG Shields {Plugin.Version}) and every player needs the same version. Get it in r2modman, then rejoin."); }
            catch { }
            try
            {
                var conn = p.connectionToClient;
                if (conn == null) return;
                if (!_disconnectLooked)
                {
                    _disconnectLooked = true;
                    _disconnectWithMessage = AccessTools.Method(typeof(BNetworkManager), "ServerDisconnectClientWithMessage");
                    if (_disconnectWithMessage == null) Plugin.Log.LogWarning("The game's disconnect-with-message is gone; removing players without a reason shown.");
                }
                var manager = Mirror.NetworkManager.singleton as BNetworkManager;
                if (_disconnectWithMessage != null && manager != null)
                    _disconnectWithMessage.Invoke(manager, new object[] { conn, DisconnectReason.KickedFromLobby });
                else
                    conn.Disconnect();
            }
            catch (Exception e) { Plugin.Log.LogWarning($"Could not remove {name}: {(e.InnerException ?? e).Message}"); }
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
                if (!open) Plugin.OnStandDown(problem);
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

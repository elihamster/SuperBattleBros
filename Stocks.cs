using System;
using System.Collections.Generic;
using HarmonyLib;
using Mirror;
using UnityEngine;

namespace SbgShields
{
    /// <summary>
    /// Stock mode: every player starts each hole with a number of lives. Every star KO
    /// costs one; the last one puts you out of the hole (the game's own "eliminated" result,
    /// so the scoreboard shows it and the hole carries on without you). Works in free-for-all
    /// and in team mode: a team is beaten when all its players are out.
    ///
    /// Who decides what:
    ///  - The HOST picks the mode (Stocks.StocksPerHole on the host; 0 = off) and tells
    ///    everyone with a StockMode message, so every player plays the same rules.
    ///  - Each player counts their own lives (a star KO is decided on the victim's machine,
    ///    like every knockout) and tells everyone their count, for the HUD.
    ///  - Putting a player out is a server action, so the last life's message goes to the
    ///    host, which sets that player's result to eliminated.
    /// </summary>
    internal static class Stocks
    {
        private static int    _hostMode;                 // what the host last told us
        private static double _hostModeAt = double.MinValue;
        private static double _lastModeSent = double.MinValue;
        private static double _lastCountSent = double.MinValue;
        private static readonly Dictionary<uint, int> _remote = new Dictionary<uint, int>();

        /// <summary>Lives each player gets this hole; 0 = stock mode off. The host's setting, everywhere.</summary>
        internal static int Mode
        {
            get
            {
                bool host = false;
                try { host = NetworkServer.active; } catch { }
                if (host) return Mathf.Clamp(Plugin.StocksPerHole.Value, 0, 99);
                return Time.timeAsDouble - _hostModeAt < 6.0 ? _hostMode : 0;
            }
        }

        /// <summary>Stocks only count where star KOs exist: in a hole, with the percent layer and the kill zone on.</summary>
        internal static bool On => Mode > 0 && ShieldState.InPlayableHole && Plugin.PercentEnabled.Value && Plugin.KillZoneEnabled.Value;

        /// <summary>The local player's lives left this hole, or -1 before the first count.</summary>
        internal static int Remaining = -1;

        /// <summary>
        /// The local player ran out this hole: hidden, out, until the next hole resets it.
        /// A flag of its own, not "On and zero": the mode can read off mid-hole (a lost host
        /// message, the hole ending), and the player must not pop back into the sky then.
        /// </summary>
        internal static bool OutLocal;
        private static double _outSentAt = double.MinValue;

        internal static void OnHoleStart()
        {
            OutLocal = false;
            Remaining = Mode > 0 ? Mode : -1;
            _remote.Clear();
            _lastCountSent = double.MinValue;
            if (Remaining > 0) Plugin.Log.LogInfo($"Stocks: {Remaining} lives this hole.");
        }

        /// <summary>Our star KO just fired. Spend a life; the last one takes us out of the hole.</summary>
        internal static void OnLocalStarKo()
        {
            if (!On) return;
            if (Remaining < 0) Remaining = Mode;
            Remaining = Mathf.Max(0, Remaining - 1);
            ShieldState.Stats.StocksLost++;
            SbgNet.Send(SbgNet.Kind.Stocks, Remaining);
            _lastCountSent = Time.timeAsDouble;
            if (Remaining > 0)
            {
                Plugin.Log.LogInfo($"Stocks: a life lost, {Remaining} left.");
                return;
            }
            Plugin.Log.LogInfo("Stocks: out of lives; out of this hole.");
            OutLocal = true;
            AskToBePutOut();
        }

        /// <summary>The host is the one that can mark us out. Ask, and keep asking every 2 s until it has.</summary>
        private static void AskToBePutOut()
        {
            _outSentAt = Time.timeAsDouble;
            bool host = false;
            try { host = NetworkServer.active; } catch { }
            if (host) ServerPutOut(GameManager.LocalPlayerInfo);
            else SbgNet.Send(SbgNet.Kind.OutOfStocks, 0f);
        }

        internal static int RemoteCount(PlayerInfo p) =>
            p != null && _remote.TryGetValue(p.netId, out int n) ? n : -1;

        internal static void OnRemoteCount(PlayerInfo p, float count)
        {
            if (p == null) return;
            _remote[p.netId] = Mathf.Clamp(Mathf.RoundToInt(count), 0, 99);
        }

        internal static void OnHostMode(float stocks)
        {
            int m = Mathf.Clamp(Mathf.RoundToInt(stocks), 0, 99);
            if (m != _hostMode) Plugin.Log.LogInfo(m > 0 ? $"Stocks: the host is playing {m} lives per hole." : "Stocks: the host turned stock mode off.");
            _hostMode = m;
            _hostModeAt = Time.timeAsDouble;
            if (m > 0 && Remaining < 0 && ShieldState.InPlayableHole) Remaining = m;
        }

        /// <summary>Every frame: the host repeats the mode, everyone repeats their own count, both every 2 s.</summary>
        internal static void Tick()
        {
            double now = Time.timeAsDouble;
            bool host = false;
            try { host = NetworkServer.active; } catch { }
            if (host && now - _lastModeSent >= 2.0 && SbgNet.Send(SbgNet.Kind.StockMode, Mode)) _lastModeSent = now;
            if (On && Remaining >= 0 && now - _lastCountSent >= 2.0 && SbgNet.Send(SbgNet.Kind.Stocks, Remaining)) _lastCountSent = now;

            if (OutLocal && now - _outSentAt >= 2.0)
            {
                bool resolved = false;
                try { var g = GameManager.LocalPlayerInfo != null ? GameManager.LocalPlayerInfo.AsGolfer : null; resolved = g != null && g.IsMatchResolved; } catch { }
                if (!resolved) AskToBePutOut();
            }
        }

        private static System.Reflection.MethodInfo _setResolution;
        private static bool _looked;

        /// <summary>
        /// Host only: the game's own "eliminated" result for this hole. Set directly rather
        /// than through ServerEliminate, which respawns players or reports them as timed out.
        /// </summary>
        internal static void ServerPutOut(PlayerInfo p)
        {
            if (p == null || p.AsGolfer == null) return;
            try
            {
                // Not gated on this machine's own reading of the mode: the player's game already
                // counted the lives, and refusing here left them hidden and never out.
                if (!NetworkServer.active || !ModHandshake.GameplayEnabled) return;
                if (!_looked)
                {
                    _looked = true;
                    _setResolution = AccessTools.Method(typeof(PlayerGolfer), "ServerTrySetMatchResolution");
                    if (_setResolution == null) Plugin.Log.LogWarning("Stocks: the game's match-result setter is gone; running out of lives cannot take a player out.");
                }
                if (_setResolution == null) return;
                bool ok = (bool)_setResolution.Invoke(p.AsGolfer, new object[] { PlayerMatchResolution.Eliminated });
                string name; try { name = p.PlayerId.PlayerNameNoRichText; } catch { name = "a player"; }
                Plugin.Log.LogInfo(ok ? $"Stocks: {name} is out of lives and out of this hole." : $"Stocks: the game would not mark {name} out (already finished?).");
            }
            catch (Exception e) { Plugin.Log.LogWarning("Stocks: could not put a player out: " + (e.InnerException ?? e).Message); }
        }
    }
}

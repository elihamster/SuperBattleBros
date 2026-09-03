#if SBG_DEV
using System;
using HarmonyLib;
using UnityEngine;

namespace SbgShields
{
    /// <summary>
    /// Debug commands typed straight into the game's text chat. The message is caught
    /// on the way out and never reaches the network, so nobody else sees it.
    ///
    /// /give &lt;item&gt;   spawn an item into your inventory  (host only, see below)
    /// /pct &lt;n&gt;       set your percent
    /// /pips &lt;n&gt;      set your pips
    /// /sbg           print status to chat (local only)
    ///
    /// This whole file is development scaffolding and comes out for the public build.
    /// </summary>
    internal static class ChatCommands
    {
        /// <summary>Returns true if the message was a command and should not be sent.</summary>
        internal static bool TryHandle(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return false;

            string msg = raw.Trim();
            if (msg.Length < 2 || msg[0] != '/') return false;

            // Commands are compiled in but switched off: say so locally and swallow the
            // line, rather than broadcasting "/give rocketlauncher" to the whole lobby
            // and leaving you wondering why nothing happened.
            if (!Plugin.EnableChatCommands.Value)
            {
                Say("chat commands are off. Turn on Debug > ChatCommands in ModConfig.");
                return true;
            }

            string[] parts = msg.Substring(1).Split(new[] { ' ' }, 2, StringSplitOptions.RemoveEmptyEntries);
            string cmd = parts[0].ToLowerInvariant();
            string arg = parts.Length > 1 ? parts[1].Trim() : "";

            try
            {
                switch (cmd)
                {
                    case "give":   return Give(arg);
                    case "pct":
                    case "percent": return SetPercent(arg);
                    case "pips":   return SetPips(arg);
                    case "sbg":
                    case "shields": return Status();
                    default: return false;   // not ours: let it go out as normal chat
                }
            }
            catch (Exception e)
            {
                Say("command failed: " + e.Message);
                return true;
            }
        }

        private static bool Give(string arg)
        {
            if (arg.Length == 0) { Say("usage: /give rocketlauncher   (" + string.Join(", ", Enum.GetNames(typeof(ItemType))) + ")"); return true; }

            if (!Enum.TryParse(arg.Replace(" ", ""), true, out ItemType item) || item == ItemType.None)
            {
                Say($"'{arg}' is not an item.");
                return true;
            }

            var player = GameManager.LocalPlayerInfo;
            if (player == null || player.Inventory == null) { Say("no local player yet."); return true; }

            if (!Mirror.NetworkServer.active)
            {
                // ServerTryAddItem is a [Server] method: on a client it logs a warning
                // internally and returns without doing anything. Inventories are
                // server-authoritative, so there is no client-side way around this.
                Say("only the host can spawn items -- inventories are server-side.");
                return true;
            }
            if (!player.Inventory.HasSpaceForItem(out int slot)) { Say("inventory is full."); return true; }

            int uses = 1;
            if (GameManager.AllItems != null && GameManager.AllItems.TryGetItemData(item, out var data) && data.MaxUses > 0)
                uses = data.MaxUses;

            bool ok = player.Inventory.ServerTryAddItem(item, uses);
            Say(ok ? $"gave {item} x{uses} (slot {slot})." : $"the game refused {item}.");
            return true;
        }

        private static bool SetPercent(string arg)
        {
            if (!float.TryParse(arg, out float v)) { Say("usage: /pct 150"); return true; }
            if (!ShieldState.InPlayableHole) { Say("percent only exists during a hole."); return true; }
            ShieldState.SetPercent(v);
            Say($"percent = {ShieldState.Percent:0}.");
            return true;
        }

        private static bool SetPips(string arg)
        {
            if (!int.TryParse(arg, out int v)) { Say("usage: /pips 3"); return true; }
            ShieldState.Pips = Mathf.Clamp(v, 0, Plugin.MaxPips.Value);
            Say($"pips = {ShieldState.Pips}/{Plugin.MaxPips.Value}.");
            return true;
        }

        private static bool Status()
        {
            string gate = ModHandshake.GameplayEnabled ? "active" : "STANDING DOWN (" + ModHandshake.BlockReason + ")";
            Say($"v{Plugin.Version} | {gate} | pips {ShieldState.Pips}/{Plugin.MaxPips.Value} | percent {ShieldState.Percent:0} | " +
                (Mirror.NetworkServer.active ? "hosting" : "client"));
            return true;
        }

        /// <summary>Local-only chat line. Never leaves this machine.</summary>
        private static void Say(string text)
        {
            try { TextChatUi.ShowMessage("<b>[shields]</b> " + text); }
            catch { Plugin.Log.LogInfo("[shields] " + text); }
        }
    }

    /// <summary>
    /// Catches our slash commands before the chat message is sent to the server.
    /// Everything else passes straight through untouched.
    /// </summary>
    [HarmonyPatch(typeof(TextChatManager), nameof(TextChatManager.SendChatMessage))]
    internal static class ChatCommandPatch
    {
        [HarmonyPriority(Priority.Low)]   // can skip the original
        private static bool Prefix(string message)
        {
            try { return !ChatCommands.TryHandle(message); }
            catch (Exception e)
            {
                Plugin.Log.LogWarning("Chat command error: " + e.Message);
                return true;
            }
        }
    }
}
#endif

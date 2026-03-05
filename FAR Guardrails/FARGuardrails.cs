using System;
using System.Collections.Generic;

namespace Oxide.Plugins
{
    [Info("FAR: Guardrails", "miniMe", "1.0.0")]
    [Description("Kicks players with inappropriate names and handles idle/inactive players.")]
    public class FARGuardrails : RustPlugin
    {
        #region Config

        readonly bool debug = false;
        private Configuration _config;

        private class Configuration
        {
            public int IdleKickMinutes = 20;
            public string IdleKickMessage = "Idle for {minutes} minutes";
            public string NameKickMessage = "No advertising, no profanities! Change your name and try again to connect to this server.";
            public List<string> ForbiddenNameParts = new List<string>();

            public static Configuration Default() => new Configuration
            {
                ForbiddenNameParts = new List<string> { "example.com", "badword" }
            };
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            _config = Config.ReadObject<Configuration>();
            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            _config = Configuration.Default();
            SaveConfig();
        }

        protected override void SaveConfig() =>
            Config.WriteObject(_config);

        #endregion

        #region State

        // tracks players who are not yet "active": connecting/dead but not yet awake
        private readonly Dictionary<ulong, DateTime> _inactiveSince = new Dictionary<ulong, DateTime>();

        private Timer _checkTimer;

        #endregion

        #region Lifecycle

        private bool IsRealPlayer(BasePlayer player) =>
            player != null && player.userID.IsSteamId();

        private void OnServerInitialized() =>
            _checkTimer = timer.Every(60f, CheckInactivePlayers);

        private void Unload()
        {
            _checkTimer?.Destroy();
            _inactiveSince.Clear();
        }

        #endregion

        #region Name Check

        // earliest hook where we can see the connecting player's name
        private void OnPlayerConnected(BasePlayer player)
        {
            var playerName = player.displayName;
            if (HasInappropriateName(playerName))
            {
                if (debug)
                    Puts($"[DEBUG] Would kick player \"{playerName}\" for offending player name.");
                else
                    NextTick(() => player.Kick(_config.NameKickMessage));
                return;
            }

            if (debug) Puts($"[DEBUG] Adding \"{playerName}\" to inactive player list.");

            // player connected but has not yet woken up - start tracking
            _inactiveSince[player.userID] = DateTime.UtcNow;
        }

        private bool HasInappropriateName(string name)
        {
            var lower = name.ToLower();
            foreach (var part in _config.ForbiddenNameParts)
                if (lower.Contains(part.ToLower()))
                    return true;

            return false;
        }

        #endregion

        #region Active State Detection

        // player has fully woken up and is in the world
        private void OnPlayerSleepEnded(BasePlayer player)
        {
            if (debug) Puts($"[DEBUG] Player woke up. Removing \"{player.displayName}\" from inactive player list.");
            _inactiveSince.Remove(player.userID);
        }

        // player died - they must respawn to be considered active again
        private void OnPlayerDeath(BasePlayer player, HitInfo info)
        {   // let's not try to kick NPCs ...
            if (!IsRealPlayer(player)) return;

            if (debug) Puts($"[DEBUG] Player died. Adding \"{player.displayName}\" to inactive player list.");
            _inactiveSince[player.userID] = DateTime.UtcNow;
        }

        // player disconnected cleanly - no need to track them
        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (debug) Puts($"[DEBUG] Player disconnected. Removing \"{player.displayName}\" from inactive player list.");
            _inactiveSince.Remove(player.userID);
        }

        #endregion

        #region Idle Check

        private void CheckInactivePlayers()
        {
            var message = _config.IdleKickMessage.Replace("{minutes}", _config.IdleKickMinutes.ToString());
            var threshold = TimeSpan.FromMinutes(_config.IdleKickMinutes);
            var toKick = new List<ulong>();
            var now = DateTime.UtcNow;

            foreach (var entry in _inactiveSince)
                if (now - entry.Value >= threshold)
                    toKick.Add(entry.Key);

            foreach (var uid in toKick)
            {
                var player = BasePlayer.FindByID(uid);
                if (player != null && player.IsConnected && (player.IsSleeping() || player.IsDead()))
                {
                    if (debug)
                        Puts($"[DEBUG] Would kick player \"{player.displayName}\" for being idle > {_config.IdleKickMinutes.ToString()} minutes.");
                    else
                        player.Kick(message);
                }
                if (debug) Puts($"[DEBUG] Player kicked. Removing \"{player?.displayName ?? uid.ToString()}\" from inactive player list.");
                _inactiveSince.Remove(uid);
            }
        }

        // Rust's built-in idle kick doesn't cover all inactive states (e.g. connect-and-never-wake,
        // die-and-never-respawn). This plugin handles those corner cases. If Rust's idle kick fires
        // regardless, we clean up our tracking dictionary accordingly.
        private object OnIdleKick(BasePlayer player)
        {
            if (debug) Puts($"[DEBUG] Removing \"{player.displayName}\" from inactive player list.");
            _inactiveSince.Remove(player.userID);
            return null;
        }

        #endregion
    }
}
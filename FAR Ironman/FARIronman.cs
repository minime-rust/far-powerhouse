using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("FAR: Ironman", "miniMe", "1.0.0")]
    [Description("The island shapes survivors, not gods. Stay alive long enough to resist the elements. One mistake resets everything.")]
    public class FARIronman : RustPlugin
    {
        private Dictionary<ulong, float> survivalTimes = new Dictionary<ulong, float>();
        private const string PermissionOptIn = "ironman.optin";
        private const float TickInterval = 60f;

        // --- Configuration (KISS) ---
        private const float MaxIdleTime = 300f;
        private const float BaseTimeMultiplier = 0.1f;
        private const float MaxSurvivalHours = 10f;
        private const float MaxReduction = 0.5f;

        #region Localization
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["DeathReset"] = "<color=#cd422b>FAR: Ironman</color> You died. Your survival progress has been reset.",
                ["StatusTime"] = "Active Survival Time: <color=#55aa55>{0:F2} hours</color>",
                ["StatusResist"] = "Environmental Resistance: <color=#55aa55>{0:F0}%</color>",
                ["OptInSuccess"] = "<color=#55aa55>Ironman Mode Active!</color> Your survival is now being tracked.",
                ["OptOutSuccess"] = "<color=#cd422b>Ironman Mode Disabled!</color> Your stats have been wiped.",
                ["StatusInactive"] = "Ironman Mode: <color=#grey>Inactive</color> - opt in by typing /ironman on",
                ["StatusActive"] = "Ironman Mode: <color=#55aa55>Active</color>",
                ["TopPlayer"] = "Top Survivor: <color=#cd422b>{0}</color> ({1:F2} hours)"
            }, this);
        }

        private string GetMsg(string key, string userId = null) => lang.GetMessage(key, this, userId);
        #endregion

        #region Hooks & Logic

        private string GetPlayerName(ulong steamId)
        {
            // Check active players first (fastest)
            BasePlayer player = BasePlayer.FindByID(steamId);
            if (player != null) return player.displayName;

            // Fallback to Covalence for sleepers/offline players
            IPlayer iPlayer = covalence.Players.FindPlayerById(steamId.ToString());
            return iPlayer?.Name ?? "Unknown Survivor";
        }

        // Detect a server wipe
        private void OnNewSave(string filename)
        {
            Puts("New save detected → Clearing Ironman database for the fresh wipe.");
            survivalTimes = new Dictionary<ulong, float>();
            SaveData(); // Force-write the empty dictionary to disk immediately

            // wipe permission from default group
            permission.RevokeGroupPermission("default", PermissionOptIn);

            // also wipe permission from each player
            var users = permission.GetPermissionUsers(PermissionOptIn);
            if (users != null)
                foreach (var user in users)
                    permission.RevokeUserPermission(user.Split(' ')[0], PermissionOptIn);
        }

        void Init()
        {
            permission.RegisterPermission(PermissionOptIn, this);
            // load data file from disk - if server wiped, it will be empty
            survivalTimes = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, float>>(Name)
                         ?? new Dictionary<ulong, float>();
        }

        void OnServerInitialized()
        {
            Puts($"Running with {survivalTimes.Count} survival records.");
            timer.Every(TickInterval, UpdateSurvivalTimes);
        }

        bool HasPermission(BasePlayer player) =>
            player != null && permission.UserHasPermission(player.UserIDString, PermissionOptIn);

        private void UpdateSurvivalTimes()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player == null || player.net?.connection == null || !player.IsConnected)
                    continue;

                // We don't count time if they are dead, sleeping, or not an Ironman
                if (player.IsDead() || player.IsSleeping() || !HasPermission(player))
                    continue;

                // AFK Idle Check
                if (player.IdleTime > MaxIdleTime) continue;

                // Survival Aggregation
                float gain = TickInterval / 3600f;
                if (player.InSafeZone() || player.GetBuildingPrivilege() != null)
                    gain *= BaseTimeMultiplier;

                if (!survivalTimes.ContainsKey(player.userID))
                    survivalTimes[player.userID] = 0f;

                survivalTimes[player.userID] += gain;
            }
        }

        object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            var player = entity as BasePlayer;
            if (player == null || info == null || info.damageTypes.Total() <= 0) return null;
            if (!HasPermission(player)) return null;

            float hours;
            if (!survivalTimes.TryGetValue(player.userID, out hours) || hours <= 0) return null;

            float scale = Mathf.Min(hours / MaxSurvivalHours, 1.0f);
            float reduction = scale * MaxReduction;

            switch (info.damageTypes.GetMajorityDamageType())
            {
                case Rust.DamageType.Bite:
                case Rust.DamageType.Cold:
                case Rust.DamageType.Fall:
                case Rust.DamageType.Hunger:
                case Rust.DamageType.Thirst:
                case Rust.DamageType.Radiation:
                    info.damageTypes.ScaleAll(1.0f - reduction);
                    break;

                case Rust.DamageType.Slash when !(info.Initiator is BasePlayer):
                    info.damageTypes.ScaleAll(1.0f - reduction);
                    break;
            }
            return null;
        }

        void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            BasePlayer player = entity as BasePlayer;
            if (player == null || !HasPermission(player))
                return;

            survivalTimes[player.userID] = 0f;
        }

        void OnPlayerRespawned(BasePlayer player)
        {
            if (player == null || !HasPermission(player)) return;

            timer.Once(2f, () =>
            {
                if (player != null && player.IsConnected)
                    SendReply(player, GetMsg("DeathReset", player.UserIDString));
            });
        }

        void OnPlayerSleepEnded(BasePlayer player)
        {
            if (player == null || !player.IsConnected) return;

            // 2-second delay to get off the loading screen 
            timer.Once(2.0f, () =>
            {
                if (player != null && player.IsConnected)
                    CmdIronman(player, string.Empty, new string[0]);
            });
        }

        void OnServerSave() => SaveData();
        void Unload() => SaveData();
        private void SaveData() =>
            Interface.Oxide.DataFileSystem.WriteObject(Name, survivalTimes);

        #endregion

        #region Chat Commands

        [ChatCommand("ironman")]
        void CmdIronman(BasePlayer player, string command, string[] args)
        {
            string userId = player.UserIDString;

            // 1. Handling Subcommands (on/off)
            if (args.Length > 0)
            {
                string subCommand = args[0].ToLower();
                if (subCommand == "on")
                {
                    permission.GrantUserPermission(userId, PermissionOptIn, this);
                    SendReply(player, GetMsg("OptInSuccess", userId));
                    return;
                }
                if (subCommand == "off")
                {
                    permission.RevokeUserPermission(userId, PermissionOptIn);
                    survivalTimes.Remove(player.userID);
                    SendReply(player, GetMsg("OptOutSuccess", userId));
                    return;
                }
            }

            // 2. Build the Status Message Block
            bool isOptedIn = HasPermission(player);
            string message = isOptedIn ? GetMsg("StatusActive", userId) : GetMsg("StatusInactive", userId);

            if (isOptedIn)
            {
                float hours = 0f;
                survivalTimes.TryGetValue(player.userID, out hours);
                float reduction = Mathf.Min(hours / MaxSurvivalHours, 1.0f) * MaxReduction * 100f;

                message += "\n" + string.Format(GetMsg("StatusTime", userId), hours);
                message += "\n" + string.Format(GetMsg("StatusResist", userId), reduction);
            }

            // 3. Add Leaderboard to the same message
            if (survivalTimes.Count > 0)
            {
                var topEntry = survivalTimes
                    .OrderByDescending(x => x.Value)
                    .FirstOrDefault(x =>
                    {
                        IPlayer iPlayer = covalence.Players.FindPlayerById(x.Key.ToString());
                        return iPlayer != null && !iPlayer.IsAdmin;
                    });

                if (topEntry.Key != 0)
                    message += "\n" + string.Format(GetMsg("TopPlayer", userId), GetPlayerName(topEntry.Key), topEntry.Value);
            }

            // 4. Send the single, multi-line message
            SendReply(player, message);
        }
        #endregion
    }
}
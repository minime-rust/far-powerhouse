using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("FAR: Ironman", "miniMe", "1.0.2")]
    [Description("The island shapes survivors, not gods. Stay alive long enough to resist the elements. One mistake resets everything.")]
    public class FARIronman : RustPlugin
    {
        private Dictionary<ulong, float> survivalTimes = new Dictionary<ulong, float>();
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
                ["TopPlayer"] = "Top Survivor: <color=#cd422b>{0}</color> ({1:F2} hours)",
                ["TopTitle"] = "<color=#ffff00>--- Top Survivors ---</color>",
                ["TopItem"] = "<color=#55aa55>{0,6:F2}h</color> - {1}"
            }, this);
        }

        private string GetMsg(string key, string userId = null) => lang.GetMessage(key, this, userId);
        #endregion

        #region Hooks & Logic

        private void OnNewSave(string filename)
        {   // Detect a server wipe
            Puts("New save detected → Clearing Ironman database for the fresh wipe.");
            survivalTimes = new Dictionary<ulong, float>();
            SaveData(); // Force-write the empty dictionary to disk immediately
        }

        // load data file from disk - if server wiped, it will be empty
        void Init() =>
            survivalTimes = Interface.Oxide.DataFileSystem.ReadObject<Dictionary<ulong, float>>(Name)
                         ?? new Dictionary<ulong, float>();

        void OnServerInitialized()
        {
            Puts($"Running with {survivalTimes.Count} survival records.");
            timer.Every(TickInterval, UpdateSurvivalTimes);
        }

        // replace permission check with simple dictionary lookup
        bool IsIronman(ulong steamId) =>
            survivalTimes.ContainsKey(steamId);

        private void UpdateSurvivalTimes()
        {
            foreach (var player in BasePlayer.activePlayerList)
            {
                if (player == null || player.net?.connection == null || !player.IsConnected)
                    continue;

                // We don't count time if they are dead, sleeping, or not an Ironman
                if (player.IsDead() || player.IsSleeping() || !IsIronman(player.userID))
                    continue;

                // AFK Idle Check
                if (player.IdleTime > MaxIdleTime)
                    continue;

                // Survival Aggregation
                float gain = TickInterval / 3600f;
                if (player.InSafeZone() || player.GetBuildingPrivilege() != null)
                    gain *= BaseTimeMultiplier;

                survivalTimes[player.userID] += gain;
            }
        }

        object OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            var player = entity as BasePlayer;
            if (player == null || info == null || info.damageTypes.Total() <= 0 || !IsIronman(player.userID))
                return null;

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
            if (player == null || !IsIronman(player.userID))
                return;

            survivalTimes[player.userID] = 0f;
        }

        void OnPlayerRespawned(BasePlayer player)
        {
            if (player == null || !IsIronman(player.userID))
                return;

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

        void OnServerSave() =>
            SaveData();
        void Unload() =>
            SaveData();
        private void SaveData() =>
            Interface.Oxide.DataFileSystem.WriteObject(Name, survivalTimes);

        #endregion

        #region Chat Commands

        [ChatCommand("ironman")]
        void CmdIronman(BasePlayer player, string command, string[] args)
        {
            string userId = player.UserIDString;
            ulong steamId = player.userID;

            if (args.Length > 0)
            {
                string subCommand = args[0].ToLower();
                // opt in or out of Ironman Mode
                if (subCommand == "on")
                {
                    if (!IsIronman(steamId))
                    {
                        survivalTimes[steamId] = 0f;
                        SaveData();
                    }
                    SendReply(player, GetMsg("OptInSuccess", userId));
                    return;
                }
                if (subCommand == "off")
                {
                    if (IsIronman(steamId))
                    {
                        survivalTimes.Remove(steamId);
                        SaveData();
                    }
                    SendReply(player, GetMsg("OptOutSuccess", userId));
                    return;
                }

                // --- /ironman top ---
                if (subCommand == "top")
                {
                    var topList = survivalTimes
                        .OrderByDescending(x => x.Value)
                        .Select(x => new { Hours = x.Value, IPlayer = covalence.Players.FindPlayerById(x.Key.ToString()) })
                        .Where(x => x.IPlayer != null && !x.IPlayer.IsAdmin)
                        .Take(5)
                        .ToList();

                    if (topList.Count == 0) return;

                    string topMsg = GetMsg("TopTitle", userId);
                    foreach (var entry in topList)
                        topMsg += "\n" + string.Format(GetMsg("TopItem", userId), entry.Hours, entry.IPlayer.Name);

                    SendReply(player, topMsg);
                    return;
                }
            }

            // 2. Build the Status Message Block
            bool isOptedIn = IsIronman(player.userID);
            string message = isOptedIn ? GetMsg("StatusActive", userId) : GetMsg("StatusInactive", userId);

            if (isOptedIn)
            {
                float hours = survivalTimes[steamId];
                float reduction = Mathf.Min(hours / MaxSurvivalHours, 1.0f) * MaxReduction * 100f;

                message += "\n" + string.Format(GetMsg("StatusResist", userId), reduction);
                message += "\n" + string.Format(GetMsg("StatusTime", userId), hours);
            }

            // 3. Add ONLY the #1 Survivor
            var topOne = survivalTimes
                .OrderByDescending(x => x.Value)
                .Select(x => new { Hours = x.Value, IPlayer = covalence.Players.FindPlayerById(x.Key.ToString()) })
                .Where(x => x.IPlayer != null && !x.IPlayer.IsAdmin)
                .FirstOrDefault();

            if (topOne != null)
                message += "\n" + string.Format(GetMsg("TopPlayer", userId), topOne.IPlayer.Name, topOne.Hours);

            // 4. Send the message
            SendReply(player, message);
        }
        #endregion
    }
}
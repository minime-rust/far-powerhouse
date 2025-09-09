using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json; // For config and data (works on both Oxide/Carbon)
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using UnityEngine;

/*
    This plugin provides event logging and Discord webhook integration for Rust servers running Oxide/uMod or Carbon.
    All major behaviour is configurable, and player-visible messages are localized through lang files.
    Each event type can be toggled and given a separate Discord webhook in config.
*/

namespace Oxide.Plugins
{
    [Info("FAR: Logger", "miniMe", "1.2.4")]
    [Description("A flexible, Discord-integrated event logger for admins")]

    public class FARLogger : CovalencePlugin
    {
        [PluginReference(Name = "FARMapHelper")]
        private Plugin FARMapHelper;

        // ==== CONFIG, DATA & LANG ====

        #region CONFIG

        private ConfigData config;

        // Config structure used for deserialization and serialization to JSON
        private class ConfigData
        {
            public GeneralConfig General { get; set; } = new GeneralConfig(); // Initialized here
            public WebhooksConfig Webhooks { get; set; } = new WebhooksConfig();
            public DiscordConfig Discord { get; set; } = new DiscordConfig();
            public AirdropConfig Airdrop { get; set; } = new AirdropConfig();
            public ServerWipesConfig ServerWipes { get; set; } = new ServerWipesConfig();
            public FARTrapperConfig FARTrapper { get; set; } = new FARTrapperConfig();
            public FARNoPvPConfig FARNoPvP { get; set; } = new FARNoPvPConfig();
            public BasesConfig Bases { get; set; } = new BasesConfig(); // This handles Abandoned and Raidable Bases
            public TravellingVendorConfig TravellingVendor { get; set; } = new TravellingVendorConfig();
            public PluginMonitorConfig PluginMonitor { get; set; } = new PluginMonitorConfig();
            public ScheduledCommandConfig ScheduledCommand { get; set; } = new ScheduledCommandConfig();
            public StartupCommandsConfig StartupCommands { get; set; } = new StartupCommandsConfig();
            public UsersCfgConfig UsersCfg { get; set; } = new UsersCfgConfig();
            public GTNConfig GuessTheNumber { get; set; } = new GTNConfig();
            public DoorKnockersConfig DoorKnockers { get; set; } = new DoorKnockersConfig();
        }

        private class GeneralConfig
        {
            public int PluginMonitorStartupIgnoreSeconds { get; set; } = 120; // seconds to ignore plugin load events after server start
            public string DefaultLanguage { get; set; } = "en";
            public bool Use24HourTime { get; set; } = true; // Player's culture used for date/time format when possible
        }

        private class ScheduledCommandConfig
        {
            public bool Enabled { get; set; } = false;                      // enable (true) or disable the scheduler
            public string TimeUtc { get; set; } = "00:45";                  // scheduled time on real-time clock
            public string Command { get; set; } = "restart 900 \"nightly restart\""; // the to-be-executed command
        }

        private class StartupCommandsConfig
        {
            public bool Enable { get; set; } = false;
            public int DelaySeconds { get; set; } = 60;
            public List<string> Commands { get; set; } = new List<string>();
        }

        private class WebhooksConfig
        {
            public string AirdropWebhook { get; set; } = "";
            public string WipeWebhook { get; set; } = "";
            public string TrapperWebhook { get; set; } = "";
            public string NoPvPWebhook { get; set; } = "";
            public string BasesWebhook { get; set; } = "";
            public string VendorWebhook { get; set; } = "";
            public string PluginsWebhook { get; set; } = "";
            public string UsersCfgWebhook { get; set; } = "";
            public string GuessNumberWebhook { get; set; } = "";
            public string DoorKnockersWebhook { get; set; } = "";
        }

        private class DiscordConfig
        {
            public bool EscapeMarkdown { get; set; } = true;          // Formatting
            public bool BreakMentions { get; set; } = false;          // "@everyone" -> "@ ​everyone"
            public bool SuppressPings { get; set; } = true;           // use allowed_mentions: []
            public bool TruncateToLimit { get; set; } = true;
            public int ContentLimit { get; set; } = 2000;
            public string TruncationSuffix { get; set; } = "…";

            public string Username { get; set; } = "";                // default webhook username if set
            public string AvatarUrl { get; set; } = "";               // default webhook avatar if set

            public int TimeoutSeconds { get; set; } = 10;             // webrequest timeout
            public bool LogFailures { get; set; } = true;             // warn on non-2xx
        }

        private class AirdropConfig
        {
            public bool Enabled { get; set; } = false;
            public bool ChatNotify { get; set; } = false;
            public bool DiscordNotify { get; set; } = false;
        }

        private class ServerWipesConfig
        {
            public bool Enabled { get; set; } = false;
            public bool DiscordNotify { get; set; } = false;
        }

        private class FARTrapperConfig
        {
            public bool Enabled { get; set; } = false;
            public bool DiscordNotify { get; set; } = false;
        }

        private class FARNoPvPConfig
        {
            public bool Enabled { get; set; } = false;
            public bool DiscordNotify { get; set; } = false;
        }

        private class BasesConfig
        {
            public bool Enabled { get; set; } = false;
            public bool DiscordNotify { get; set; } = false;
        }

        private class TravellingVendorConfig
        {
            public bool Enabled { get; set; } = false;
            public bool DiscordNotify { get; set; } = false;
            public bool NotifyDespawn { get; set; } = false;
        }

        private class PluginMonitorConfig
        {
            public bool Enabled { get; set; } = false;
            public bool DiscordNotify { get; set; } = false;
        }

        private class UsersCfgConfig
        {
            public bool Enabled { get; set; } = false;
            public bool DiscordNotify { get; set; } = false;
        }

        private class GTNConfig
        {
            public bool Enabled { get; set; } = false;
            public bool DiscordNotify { get; set; } = false;
        }

        private class DoorKnockersConfig
        {
            public bool Enabled { get; set; } = false;
            public bool DiscordNotify { get; set; } = false;
        }

        // IMPORTANT: This method is called automatically by the Oxide/Carbon framework
        // if your plugin's config file doesn't exist or is invalid.
        // You generally DO NOT call this directly yourself.
        protected override void LoadDefaultConfig()
        {
            config = new ConfigData(); // This initializes the top-level config object
            SaveConfig(); // This saves it, but doesn't set defaults for nested objects
        }

        // IMPORTANT: This method is called automatically by the Oxide/Carbon framework
        // when your plugin loads. You generally DO NOT call this directly yourself.
        protected override void LoadConfig()
        {
            base.LoadConfig(); // This MUST be the first line. It loads the file into 'Config' and calls LoadDefaultConfig if needed.
            try
            {
                config = Config.ReadObject<ConfigData>();
                // After reading, ensure all nested objects are initialized if they somehow weren't (e.g., from old config file)
                // This 'fixes' older configs that might be missing sections.
                config.Airdrop ??= new AirdropConfig();
                config.Bases ??= new BasesConfig();
                config.Discord ??= new DiscordConfig();
                config.DoorKnockers ??= new DoorKnockersConfig();
                config.FARNoPvP ??= new FARNoPvPConfig();
                config.FARTrapper ??= new FARTrapperConfig();
                config.General ??= new GeneralConfig();
                config.GuessTheNumber ??= new GTNConfig();
                config.PluginMonitor ??= new PluginMonitorConfig();
                config.ScheduledCommand ??= new ScheduledCommandConfig();
                config.ServerWipes ??= new ServerWipesConfig();
                config.TravellingVendor ??= new TravellingVendorConfig();
                config.UsersCfg ??= new UsersCfgConfig();
                config.Webhooks ??= new WebhooksConfig();
            }
            catch (Exception ex)
            {
                LoadDefaultConfig(); // Fallback to default config if deserialization fails
            }

            // After loading (or defaulting), always save the config to ensure it's up-to-date
            // and correctly formatted on disk, especially if default values were just created or updated.
            SaveConfig();
        }

        // This method is called automatically by base.SaveConfig() or can be called explicitly
        protected override void SaveConfig() =>
            Config.WriteObject(config);

        #endregion

        #region DATA

        // #####################################################################
        // Compact single-file data lifecycle
        private StoredData data;
        private Timer saveDebounce;
        private Timer _UsersCfgTimer;
        private const float SaveDebounceSec = 2f;
        private string _usersCfgPath;

        [Serializable]
        private class UsersCfgData
        { public string LastHash = string.Empty; }

        [Serializable]
        private class WipeData
        { public uint Seed = 0u; }

        [Serializable]
        private class StoredData
        {
            public int Schema = 1;
            public WipeData Wipe = new WipeData();
            public UsersCfgData UsersCfg = new UsersCfgData();
        }

        private void LoadData()
        { data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name) ?? new StoredData(); }

        private void SaveDataDebounced()
        {
            saveDebounce?.Destroy();
            saveDebounce = timer.Once(SaveDebounceSec, SaveDataImmediate);
        }

        private void SaveDataImmediate()
        { Interface.Oxide.DataFileSystem.WriteObject(Name, data); }

        // Wipe (server.seed) Monitor
        private uint GetWipeSeed() => data?.Wipe?.Seed ?? 0u;
        private void SetWipeSeed(uint seed) { data.Wipe.Seed = seed; SaveDataDebounced(); }
        // "server/{identity}/cfg/users.cfg" Monitor
        private string GetLastHash() => data?.UsersCfg?.LastHash ?? string.Empty;
        private void SetLastHash(string hash) { data.UsersCfg.LastHash = hash; SaveDataDebounced(); }
        // #####################################################################

        // Airdrop tracking: in-memory to avoid double notification per crate
        private readonly HashSet<ulong> lootedSupplyDrops = new HashSet<ulong>();

        // Plugin load time
        private DateTime pluginStartTime = DateTime.UtcNow;

        // Get timestamp for Discord
        private string GetDiscordTimestamp()
        {
            DateTime utcNow = DateTime.UtcNow;
            DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            TimeSpan timeSpan = utcNow - epoch;
            long unixTimeSeconds = (long)timeSpan.TotalSeconds;
            return $"<t:{unixTimeSeconds}:t>";
        }

        #endregion

        #region PLAYER NAME RESOLVER

        // Resolve a best-effort player name from an ID (online, sleeper, covalence, server users).
        // Returns a trimmed display name, or the ID string if nothing else is available.
        private string GetPlayerName(ulong playerId)
        {
            if (playerId == 0) return "0";

            // Local sanitizer keeps the logic in one place without a separate class-level method.
            static string Sanitize(string name)
            {
                if (string.IsNullOrWhiteSpace(name)) return name;
                // Normalize whitespace and trim
                name = name.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ').Trim();
                // Hard cap to avoid abuse in chat/webhooks/logs
                const int Max = 64;
                return name.Length <= Max ? name : name.Substring(0, Max);
            }

            // 1) Online
            var bp = BasePlayer.FindByID(playerId);
            if (bp != null && !string.IsNullOrWhiteSpace(bp.displayName))
                return Sanitize(bp.displayName);

            // 2) Sleeper
            bp = BasePlayer.FindSleeping(playerId);
            if (bp != null && !string.IsNullOrWhiteSpace(bp.displayName))
                return Sanitize(bp.displayName);

            // 3) Covalence
            var idStr = playerId.ToString(CultureInfo.InvariantCulture);
            var ip = covalence?.Players?.FindPlayerById(idStr);
            if (ip != null && !string.IsNullOrWhiteSpace(ip.Name))
                return Sanitize(ip.Name);

            // 4) ServerUsers
            var su = ServerUsers.Get(playerId);
            if (su != null && !string.IsNullOrWhiteSpace(su.username))
                return Sanitize(su.username);

            // 5) Fallback
            return idStr;
        }
        #endregion

        #region LANG

        // Language keys (see lang .json for values/strings)
        private const string Lang_MapHelperNear = "MapHelperNear";
        private const string Lang_SupplyDropLooted = "SupplyDropLooted";
        private const string Lang_SupplyDropLootedDiscord = "SupplyDropLootedDiscord";
        private const string Lang_ServerWipeDetectedDiscord = "ServerWipeDetectedDiscord";
        private const string Lang_TrapPlacedDiscord = "TrapPlacedDiscord";
        private const string Lang_NoPvPDeathKickDiscord = "NoPvPDeathKickDiscord";
        private const string Lang_NoPvPDeathWarnDiscord = "NoPvPDeathWarnDiscord";
        private const string Lang_AbandonedBaseFoundDiscord = "AbandonedBaseFoundDiscord";
        private const string Lang_AbandonedBaseStartedDiscord = "AbandonedBaseStartedDiscord";
        private const string Lang_AbandonedBaseCompletedDiscord = "AbandonedBaseCompletedDiscord";
        private const string Lang_AbandonedBaseEndedDiscord = "AbandonedBaseEndedDiscord";
        private const string Lang_RaidableBasePurchasedDiscord = "RaidableBasePurchasedDiscord";
        private const string Lang_RaidableBaseStartedDiscord = "RaidableBaseStartedDiscord";
        private const string Lang_RaidableBaseCompletedDiscord = "RaidableBaseCompletedDiscord";
        private const string Lang_RaidableBaseEndedDiscord = "RaidableBaseEndedDiscord";
        private const string Lang_VendorSpawnedDiscord = "VendorSpawnedDiscord";
        private const string Lang_VendorDespawnedDiscord = "VendorDespawnedDiscord";
        private const string Lang_PluginEventDiscord = "PluginEventDiscord";
        private const string Lang_UsersCfgChangedDiscord = "UsersCfgChangedDiscord";
        private const string Lang_DoorKnockersDiscord = "DoorKnockersDiscord";
        private const string Lang_GTNStartDiscord = "GTNStartDiscord";
        private const string Lang_GTNTimeoutDiscord = "GTNTimeoutDiscord";
        private const string Lang_GTNWinDiscord = "GTNWinDiscord";
        private const string Lang_BasesDifficulty0 = "BasesDifficulty0";
        private const string Lang_BasesDifficulty1 = "BasesDifficulty1";
        private const string Lang_BasesDifficulty2 = "BasesDifficulty2";
        private const string Lang_BasesDifficulty3 = "BasesDifficulty3";
        private const string Lang_BasesDifficulty4 = "BasesDifficulty4";
        private const string Lang_BasesNoRaiders = "BasesNoRaiders";
        private const string Lang_BasesRaiders = "BasesRaiders";
        private const string Lang_BasesUnowned = "BasesUnowned";
        private const string Lang_TimeRemaining = "TimeRemaining";
        private const string Lang_LondonTime = "LondonTime";
        private const string Lang_NextWipe = "NextWipe";

        // Plugin registers the default English language file and loads translations
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                // Server Wipes
                [Lang_NextWipe] = "Next wipe: {0} {1} ({2})",
                [Lang_TimeRemaining] = "Time until wipe: {0} days {1} hours {2} minutes",
                [Lang_ServerWipeDetectedDiscord] = "**Server wipe detected!**\nNew map: {0}",
                [Lang_LondonTime] = "London time",
                // Abandoned Bases
                [Lang_AbandonedBaseFoundDiscord] = ":homes: {0} `{1}`'s base ({2}) became abandoned at `{3}`{4}",
                [Lang_AbandonedBaseStartedDiscord] = ":homes: {0} `{1}`'s abandoned base ({2}) became raidable at `{3}`{4}",
                [Lang_AbandonedBaseCompletedDiscord] = ":homes: {0} `{1}`'s abandoned base ({2}) completed at `{3}`{4}",
                [Lang_AbandonedBaseEndedDiscord] = ":homes: {0} `{1}`'s abandoned base ({2}) ended at `{3}`{4}",
                // Raidable Bases
                [Lang_RaidableBasePurchasedDiscord] = ":homes: {0} `{1}` has paid for the {2} Raidable **{3}** Base (`{4}`) at `{5}`",
                [Lang_RaidableBaseStartedDiscord] = ":homes: {0} {1} Raidable **{2}** Base spawned at `{3}`",
                [Lang_RaidableBaseCompletedDiscord] = ":homes: {0} `{1}`'s {2} Raidable **{3}** Base completed at `{4}`",
                [Lang_RaidableBaseEndedDiscord] = ":homes: {0} {1} Raidable **{2}** Base ended at `{3}`",
                [Lang_BasesDifficulty0] = "Easy",
                [Lang_BasesDifficulty1] = "Medium",
                [Lang_BasesDifficulty2] = "Hard",
                [Lang_BasesDifficulty3] = "Expert",
                [Lang_BasesDifficulty4] = "Nightmare",
                [Lang_BasesNoRaiders] = "No Raiders",
                [Lang_BasesUnowned] = "Nobody",
                [Lang_BasesRaiders] = " ][ Raiders: {0}",
                // Travelling Vendor
                [Lang_VendorSpawnedDiscord] = ":compass: {0} The `Travelling Vendor` has entered the map at `{1}`{2}",
                [Lang_VendorDespawnedDiscord] = ":compass: {0} The `Travelling Vendor` has left the map at `{1}`{2}",
                // Other rule enforcements and notifications
                [Lang_MapHelperNear] = " near ",
                [Lang_NoPvPDeathKickDiscord] = ":skull_crossbones: {0} `{1}` was kicked for PvP after attacking `{2}` at `{3}`{4}",
                [Lang_NoPvPDeathWarnDiscord] = ":skull_crossbones: {0} Both `{1}` and `{2}` were warned for (un)friendly fire at `{3}`{4}",
                [Lang_SupplyDropLooted] = "{0} is looting a Supply Drop at {1}{2}",
                [Lang_SupplyDropLootedDiscord] = ":gift: {0} `{1}` looted a Supply Drop at `{2}`{3}",
                [Lang_PluginEventDiscord] = ":pause: {0} `{1}` plugin v`{2}` by `{3}` was `{4}`",
                [Lang_UsersCfgChangedDiscord] = ":skull_crossbones: {0} The `users.cfg` file has changed. Checksums old|new: {1}|{2}",
                [Lang_DoorKnockersDiscord] = ":no_entry: {0} Rejected: `{1}` ({2}) for `{3}` from {4}",
                [Lang_TrapPlacedDiscord] = ":skull_crossbones: {0} `{1}` tried to place a wild trap (`{2}`) at `{3}`{4}",
                [Lang_GTNStartDiscord] = ":1234: {0} The `Guess the Number` event started! Number range `{1}` - `{2}`",
                [Lang_GTNWinDiscord] = ":1234: {0} `{1}` has won the `Guess the Number` event! Winning number: `{2}`",
                [Lang_GTNTimeoutDiscord] = ":1234: {0} The `Guess the Number` event timed out, nobody won! Winning number: `{1}`"
            }, this, "en");
        }     // Puts($"Rejected: {name} ({sid}) - {reason} from {ip}{(port.Length > 0 ? ":" + port : "")}");

        private string Lang(string key, string playerId, params object[] args)
        {
            var message = lang.GetMessage(key, this, playerId); // null -> default language
            return (args == null || args.Length == 0) ? message : string.Format(message, args);
        }

        #endregion

        // ==== HOOK REGISTRATION & EVENTS ====

        #region PLUGIN LIFECYCLE

        // OnLoaded: plugin initialization
        private void Init()
        {
            LoadData();
            _usersCfgPath = Path.Combine(ConVar.Server.GetServerFolder("cfg"), "users.cfg");
            // Subscribe to the Unity log stream for the Rejected Connections watcher
            Application.logMessageReceived += OnLog;
        }

        private void Unload()
        {
            // Save Wipe and UsersCfg Data
            SaveDataImmediate();
            // Clear Supply Drops on plugin unload
            lootedSupplyDrops.Clear();
            // Clear our timers
            saveDebounce?.Destroy();
            _UsersCfgTimer?.Destroy();
            saveDebounce = null;
            _UsersCfgTimer = null;
            // Clean up the FAR: Map Helper instance
            FARMapHelper = null;
            // Unsubscribe from the Unity log stream
            Application.logMessageReceived -= OnLog;
            // Clear Task Scheduler
            StopDailyUtcCommand();

            // Unsubscribe from Abandoned Bases hooks
            Unsubscribe(nameof(OnPotentialAbandonedBaseFound));
            Unsubscribe(nameof(OnAbandonedBaseStarted));
            Unsubscribe(nameof(OnAbandonedBaseCompleted));
            Unsubscribe(nameof(OnAbandonedBaseEventEnd));
            // Unsubscribe from FAR NoPVP hooks
            Unsubscribe(nameof(OnPvPDeath));
            // Unsubscribe from FAR Trapper hook
            Unsubscribe(nameof(OnWildTrapPlaced));
            // Unsubscribe from Guess The Number hooks
            Unsubscribe(nameof(OnGTNEventStart));
            Unsubscribe(nameof(OnGTNTimeout));
            Unsubscribe(nameof(OnGTNWin));
            // Unsubscribe from Raidable Bases hooks
            Unsubscribe(nameof(OnRaidableBasePurchased));
            Unsubscribe(nameof(OnRaidableBaseStarted));
            Unsubscribe(nameof(OnRaidableBaseCompleted));
            Unsubscribe(nameof(OnRaidableBaseEnded));
        }

        // Called whenever server starts or dependency state changes
        private void VerifyMapHelper()
        {
            if (IsMapHelperReady())
            { Puts("FAR Map Helper plugin found. We will be using it's API."); }
            else
            { Puts("FAR Map Helper not loaded. Map Helper API is not available."); }
        }

        private void OnServerInitialized()
        {
            pluginStartTime = DateTime.UtcNow;

            // Check if FARMapHelper is loaded
            NextTick(VerifyMapHelper);

            // Task Scheduler
            if (config?.ScheduledCommand?.Enabled ?? false)
            {
                StartDailyUtcCommand(config?.ScheduledCommand?.TimeUtc, config?.ScheduledCommand?.Command);
                Puts($"Task Scheduler enabled. Running [{config?.ScheduledCommand?.Command}] @{config?.ScheduledCommand?.TimeUtc}h UTC");
            }

            // Startup Commands
            TryRunStartupCommands();

            // Airdrop drop tracking
            lootedSupplyDrops.Clear();

            // Wipe detection (seed check)
            if (config?.ServerWipes?.Enabled ?? false)
            {
                CheckWipeOnStartup();
                Puts("Wipe Detector enabled. Watching server.seed once per server start.");
            }

            // users.cfg checksum monitoring - set timer to 300 seconds to run checksum
            if (config?.UsersCfg?.Enabled ?? false)
            {
                timer.Every(300f, CheckUsersCfgHash);
                Puts($"UsersCfg monitor enabled. Watching this file: {_usersCfgPath}");
            }

            // Door Knockers monitoring (rejected user logins)
            if (config?.DoorKnockers?.Enabled ?? false)
                Puts($"Door Knockers monitor enabled. Watching rejected user logins.");

            // --- GUESS THE NUMBER HOOK SUBSCRIPTION ---
            // Only attempt to subscribe if the feature is enabled in *this* plugin's config.
            if (config?.GuessTheNumber?.Enabled ?? false)
            {
                // IMPORTANT: Replace "GuessTheNumber" with the exact name of the plugin.
                // This is typically the C# class name, or the file name without .cs extension.
                var gtnPlugin = plugins?.Find("GuessTheNumber");

                if (gtnPlugin != null)
                {
                    Puts("Guess The Number plugin found. Subscribing to its hooks.");
                    Subscribe(nameof(OnGTNEventStart));
                    Subscribe(nameof(OnGTNTimeout));
                    Subscribe(nameof(OnGTNWin));
                }
                else
                    Puts("Guess The Number plugin not found. GTN notifications will be disabled.");
            }

            // --- ABANDONED BASES HOOK SUBSCRIPTION ---
            // Only attempt to subscribe if the feature is enabled in *this* plugin's config.
            if (config?.Bases?.Enabled ?? false) // Assuming you have a 'Bases' section in your config
            {
                // IMPORTANT: Use the exact class name of the AbandonedBases plugin.
                // If the file is AbandonedBases.cs, the plugin class name is most likely "AbandonedBases".
                var abandonedBasesPlugin = plugins?.Find("AbandonedBases");

                if (abandonedBasesPlugin != null)
                {
                    Puts("Abandoned Bases plugin found. Subscribing to its hooks.");
                    Subscribe(nameof(OnPotentialAbandonedBaseFound));
                    Subscribe(nameof(OnAbandonedBaseStarted));
                    Subscribe(nameof(OnAbandonedBaseCompleted));
                    Subscribe(nameof(OnAbandonedBaseEventEnd));
                }
                else
                    Puts("Abandoned Bases plugin not found. Abandoned Base notifications will be disabled.");
            }

            // --- FAR NOPVP HOOK SUBSCRIPTION ---
            // Only attempt to subscribe if the feature is enabled in *this* plugin's config.
            if (config?.FARNoPvP?.Enabled ?? false) // Assuming you have a FARNoPvP section in your config
            {
                // IMPORTANT: Use the exact class name of the FARNoPVP plugin.
                // If the file is FARNoPVP.cs, the plugin class name is most likely "FARNoPVP".
                var farNoPvPPlugin = plugins?.Find("FARNoPVP");

                if (farNoPvPPlugin != null)
                {
                    Puts("FAR NoPVP plugin found. Subscribing to its hooks.");
                    Subscribe(nameof(OnPvPDeath));
                }
                else
                    Puts("FAR NoPVP plugin not found. NoPVP death notifications will be disabled.");
            }

            // --- RAIDABLE BASES HOOK SUBSCRIPTION ---
            // Assuming the 'Bases' config section also covers Raidable Bases if it's the same notification category
            if (config?.Bases?.Enabled ?? false)
            {
                // IMPORTANT: Use the exact class name of the RaidableBases plugin.
                // If the file is RaidableBases.cs, the plugin class name is most likely "RaidableBases".
                var raidableBasesPlugin = plugins?.Find("RaidableBases");

                if (raidableBasesPlugin != null)
                {
                    Puts("Raidable Bases plugin found. Subscribing to its hooks.");
                    Subscribe(nameof(OnRaidableBasePurchased));
                    Subscribe(nameof(OnRaidableBaseStarted));
                    Subscribe(nameof(OnRaidableBaseCompleted));
                    Subscribe(nameof(OnRaidableBaseEnded));
                }
                else
                    Puts("Raidable Bases plugin not found. Raidable Base notifications will be disabled.");
            }

            // --- FAR TRAPPER HOOK SUBSCRIPTION ---
            // Only attempt to subscribe if the feature is enabled in *this* plugin's config.
            if (config?.FARTrapper?.Enabled ?? false) // Assuming you have a FARTrapper section in your config
            {
                // IMPORTANT: Use the exact class name of the FARTrapper plugin.
                // If the file is FARTrapper.cs, the plugin class name is most likely "FARTrapper".
                var farTrapperPlugin = plugins?.Find("FARTrapper");

                if (farTrapperPlugin != null)
                {
                    Puts("FAR Trapper plugin found. Subscribing to its hooks.");
                    Subscribe(nameof(OnWildTrapPlaced));
                }
                else
                    Puts("FAR Trapper plugin not found. Trapper notifications will be disabled.");
            }
        }

        // Support execution of startup command(s) if configured
        private void TryRunStartupCommands()
        {
            var sc = config.StartupCommands;
            if (!sc.Enable || sc.DelaySeconds <= 0 || sc.Commands == null || sc.Commands.Count == 0)
                return;

            timer.Once(sc.DelaySeconds, () =>
            {
                for (int i = 0; i < sc.Commands.Count; i++)
                {
                    string cmd = sc.Commands[i];
                    timer.Once(i * 5f, () =>
                    {
                        if (!string.IsNullOrWhiteSpace(cmd))
                        {
                            Puts($"[StartupCommands] Executing: {cmd}");
                            server.Command(cmd);
                        }
                    });
                }
            });
        }

        #endregion

        // ==== LOGIC HOOKS FOR EACH EVENT ====

        #region AIRDROP

        // Listen for airdrop being looted
        private object OnLootEntity(BasePlayer player, BaseEntity entity)
        {
            // Immediately leave, if not a supply_drop
            var drop = entity as SupplyDrop;
            if (drop == null)
                return null;

            // Check config
            if (!(config?.Airdrop?.Enabled ?? false) || drop == null || player == null)
                return null;

            // Get the id of the supply_drop
            ulong id = drop.net?.ID.Value ?? 0UL;
            // Bail out if supply_drop is 0 or already known
            if (id == 0UL || lootedSupplyDrops.Contains(id))
                return null;

            // ... otherwise add id to HashSet
            lootedSupplyDrops.Add(id);
            var pos = drop.transform.position;

            var monumentPrefix = Lang("MapHelperNear", null);
            GetMapSquareAndMonument(pos, out var mapSquare, out var monument);
            var monumentName = string.IsNullOrWhiteSpace(monument)
                     ? string.Empty : $"{monumentPrefix}{monument}";

            var dis_msg = Lang("SupplyDropLootedDiscord", null, GetDiscordTimestamp(), player.displayName, mapSquare, monumentName);
            var chatmsg = Lang("SupplyDropLooted", null, player.displayName, mapSquare, monumentName);

            // Notify in chat if enabled
            if (config?.Airdrop?.ChatNotify ?? false)
                Server.Broadcast(chatmsg);

            // Send Discord if configured
            var webhookURL = config?.Webhooks?.AirdropWebhook ?? string.Empty;
            if ((config?.Airdrop?.DiscordNotify ?? false) && !string.IsNullOrWhiteSpace(webhookURL))
                SendDiscordMessage(webhookURL, dis_msg);
            return null;
        }

        #endregion

        #region SERVER WIPES

        private static readonly TimeZoneInfo LondonTimeZone =
#if UNITY_STANDALONE_WIN
        TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");
#else
        TimeZoneInfo.FindSystemTimeZoneById("Europe/London");
#endif

        private int[] validWeeks = { 1 }; // default to "monthly"
        private readonly DayOfWeek wipeDay = DayOfWeek.Thursday;
        private readonly TimeSpan wipeTime = new TimeSpan(19, 0, 0); // 19:00

        private void ParseTagsToSetSchedule()
        {
            var tags = ConVar.Server.tags?.ToLowerInvariant() ?? string.Empty;
            if (tags.Contains("biweekly")) validWeeks = new[] { 1, 3, 5 };
            else if (tags.Contains("weekly")) validWeeks = new[] { 1, 2, 3, 4, 5 };
            else validWeeks = new[] { 1 };  // default to "monthly"
        }

        private void CheckWipeOnStartup()
        {
            var mapSeed = unchecked((uint)ConVar.Server.seed);
            if (GetWipeSeed() == 0u)        // No need to check for wipe on first run
            {
                SetWipeSeed(mapSeed);
                return;
            }

            if (GetWipeSeed() == mapSeed)   // Map Seed didn't change ... just leave
                return;

            // Seed changed -> treat as wipe
            SetWipeSeed(mapSeed);

            int worldSize = ConVar.Server.worldsize;
            var newMapUrl = $"https://rustmaps.com/map/{worldSize}_{mapSeed}";
            var message = Lang("ServerWipeDetectedDiscord", null, newMapUrl);
            var webhookURL = config?.Webhooks?.WipeWebhook ?? string.Empty;

            if ((config?.ServerWipes?.DiscordNotify ?? false) &&
                !string.IsNullOrWhiteSpace(webhookURL))
                SendDiscordMessage(webhookURL, message);

        }

        [ChatCommand("wipe")]
        private void CmdWipe(BasePlayer player, string command, string[] args)
        {
            if (!(config?.ServerWipes?.Enabled ?? false) || player == null || !player.IsConnected) return;
            // Always use fresh config to allow dynamic tag/config changes
            ParseTagsToSetSchedule();
            // Calculate wipe date+time and time now, both London time, get remaining time until wipe
            var nowLondon = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, LondonTimeZone);
            var nextWipe = GetNextWipeDate(nowLondon);
            var remaining = nextWipe - nowLondon;
            if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
            // Language via Oxide Lang system; expects string ID
            var userLang = (lang.GetLanguage(player.UserIDString) ?? config?.General?.DefaultLanguage ?? "en").ToLowerInvariant();
            // ChatGPT Version
            CultureInfo culture;
            try { culture = CultureInfo.GetCultureInfo(userLang); }
            catch { culture = CultureInfo.InvariantCulture; }
            // Daylight Saving
            bool isDst = LondonTimeZone.IsDaylightSavingTime(nextWipe);
            var zoneLabel = isDst ? "BST" : "GMT";
            var londonTime = Lang("LondonTime", player.UserIDString);
            // Determine date format
            var format = config.General.Use24HourTime ? "d MMM yyyy HH'h'" : "d MMM yyyy hh'h' tt";
            var formattedDate = nextWipe.ToString(format, culture);
            // Assemble chat message
            var line1 = Lang("NextWipe", player.UserIDString, formattedDate, zoneLabel, londonTime);
            var line2 = Lang("TimeRemaining", player.UserIDString, remaining.Days, remaining.Hours, remaining.Minutes);
            // Play chat message
            player.ChatMessage($"{line1}\n{line2}");
        }

        /// <summary>
        /// Returns the next scheduled wipe London time from a supplied instant, based on validWeeks/wipe logic
        /// </summary>
        private DateTime GetNextWipeDate(DateTime fromLondon)
        {
            for (int i = 0; i < 12; i++) // calculates wipe dates 12 months into the future (!)
            {
                var month = fromLondon.AddMonths(i);
                var wipeDayDates = Enumerable.Range(1, DateTime.DaysInMonth(month.Year, month.Month))
                    .Select(day => new DateTime(month.Year, month.Month, day))
                    .Where(d => d.DayOfWeek == wipeDay)
                    .ToList();

                foreach (var week in validWeeks)
                {
                    if (wipeDayDates.Count >= week)
                    {
                        var wipeDate = wipeDayDates[week - 1].Date + wipeTime;
                        if (wipeDate > fromLondon) return wipeDate;
                    }
                }
            } // We should never end up here - wipe is monthly and never (!) 12 months in the future
            throw new InvalidOperationException("Unable to determine next wipe date in 12 months.");
        }

        #endregion

        #region FAR TRAPPER

        // Listens for OnWildTrapPlaced hook via plugin call
        private object OnWildTrapPlaced(BasePlayer player, BaseEntity entity)
        {
            if (!(config?.FARTrapper?.Enabled ?? false) ||
                !(config?.FARTrapper?.DiscordNotify ?? false) ||
                player == null || entity == null)
                return null;

            var monumentPrefix = Lang("MapHelperNear", null);
            GetMapSquareAndMonument(entity.transform.position, out var mapSquare, out var monument);
            var monumentName = string.IsNullOrWhiteSpace(monument) ? string.Empty : $"{monumentPrefix}{monument}";

            var message = Lang("TrapPlacedDiscord", null, GetDiscordTimestamp(), player.displayName, entity.ShortPrefabName, mapSquare, monumentName);
            var webhookURL = config?.Webhooks?.TrapperWebhook ?? string.Empty;

            if (!string.IsNullOrWhiteSpace(webhookURL))
                SendDiscordMessage(webhookURL, message);
            return null;
        }

        #endregion

        #region REJECTED CONNECTIONS + AB TEMP

        private static readonly Regex RxReject = new Regex(
            @"^(?<ip>\d{1,3}(?:\.\d{1,3}){3})(?::(?<port>\d+))?/(?<sid>\d{17})/(?<name>.+?)\s+Rejecting connection\s*-\s*(?<reason>.+)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(50)); // tiny safety net

        // Unity’s LogType enum covers: Log, Warning, Error, Assert, Exception.
        private void OnLog(string condition, string stackTrace, LogType type)
        {   // ignore empty logs
            if (string.IsNullOrEmpty(condition)) return;

            // Cheap prefilter to cover AB while nivex is adding required hooks to his plugin:
            if (condition.IndexOf("[abandoned bases]", StringComparison.OrdinalIgnoreCase) >= 0)
            {   // piggyback on this function for the time being <<< this is an ugly workaround!
                var webhookURLAB = config?.Webhooks?.BasesWebhook ?? string.Empty;
                if ((config?.Bases?.Enabled ?? false) &&
                    (config?.Bases?.DiscordNotify ?? false) &&
                    !string.IsNullOrWhiteSpace(webhookURLAB))
                {
                    var messageAB = $":homes: {GetDiscordTimestamp()} {condition}";
                    NextTick(() => SendDiscordMessage(webhookURLAB, messageAB));
                }
                return;
            }

            // Cheap prefilter to avoid regex work on most lines
            if (condition.IndexOf("rejecting connection", StringComparison.OrdinalIgnoreCase) < 0)
                return;

            // exit early if feature or Discord notification disabled, or webhook unconfigured
            var enabled = config?.DoorKnockers?.Enabled ?? false;
            var notify = config?.DoorKnockers?.DiscordNotify ?? false;
            var webhookURL = config?.Webhooks?.DoorKnockersWebhook ?? string.Empty;
            if (!enabled || !notify || string.IsNullOrWhiteSpace(webhookURL))
                return;

            // run precompiled regex
            var m = RxReject.Match(condition);
            if (!m.Success) return;

            // Your handling here (short and allocation-light reads of Groups)
            var ip = m.Groups["ip"].Value;
            var port = m.Groups["port"].Value;
            var steamid = m.Groups["sid"].Value;
            var name = m.Groups["name"].Value;
            var reason = m.Groups["reason"].Value;

            // build message and send to Discord
            var message = Lang("DoorKnockersDiscord", null, GetDiscordTimestamp(), name, steamid, reason, ip);
            NextTick(() => SendDiscordMessage(webhookURL, message));
        }

        #endregion

        #region FAR NOPVP

        // Listens for OnPvPDeath hook via plugin call
        private object OnPvPDeath(string victim, string killer, bool sameTeam, string mapSquare, string monumentName)
        {
            // Combined early return conditions
            if (!(config?.FARNoPvP?.Enabled ?? false) ||
                !(config?.FARNoPvP?.DiscordNotify ?? false))
                return null;

            var webhookURL = config?.Webhooks?.NoPvPWebhook ?? string.Empty;
            if (string.IsNullOrWhiteSpace(webhookURL))
                return null;

            // Ease the part with empty or populated monumentName
            var monumentPrefix = Lang("MapHelperNear", null);
            var monumentText = string.IsNullOrEmpty(monumentName)
                ? string.Empty : $"{monumentPrefix}{monumentName}";
            // 1. If teams are different (!sameTeam) -> always use Lang_NoPvPDeathKickDiscord
            // 2. If teams are same but no monument  ->        use Lang_NoPvPDeathKickDiscord
            // 3. If teams are same and has monument ->        use Lang_NoPvPDeathWarnDiscord
            var message = !sameTeam || string.IsNullOrWhiteSpace(monumentName)
                ? Lang("NoPvPDeathKickDiscord", null, GetDiscordTimestamp(), victim, killer, mapSquare, monumentText)
                : Lang("NoPvPDeathWarnDiscord", null, GetDiscordTimestamp(), victim, killer, mapSquare, monumentText);

            // Send notification to Discord
            SendDiscordMessage(webhookURL, message);

            return null;
        }

        #endregion

        #region ABANDONED BASES / RAIDABLE BASES

        // ################################################################
        // Abandoned Base Events
        // ################################################################
        // OnPotentialAbandonedBaseFound
        // Arguments (Vector3 position, ulong ownerId)
        // nivex: this signature does not exist
        // ################################################################
        private void OnPotentialAbandonedBaseFound(Vector3 eventPos, ulong ownerId)
        {   // check if feature enabled
            Puts("OnPotentialAbandonedBaseFound triggered");
            var webhookURL = config?.Webhooks?.BasesWebhook ?? string.Empty;
            if (!(config?.Bases?.Enabled ?? false) || string.IsNullOrWhiteSpace(webhookURL))
                return;
            Puts("Check config: passed");
            // check for valid position
            if (eventPos == Vector3.zero || float.IsNaN(eventPos.x) || float.IsNaN(eventPos.y) || float.IsNaN(eventPos.z))
                return;
            Puts("Check position: passed");
            // Get event position on the map
            var monumentPrefix = Lang("MapHelperNear", null);
            GetMapSquareAndMonument(eventPos, out var mapSquare, out var monument);
            var monumentName = string.IsNullOrWhiteSpace(monument) ? string.Empty : $"{monumentPrefix}{monument}";
            Puts("Send Discord message ...");
            // Assemble message to be sent to Discord
            var message = Lang("AbandonedBaseFoundDiscord", null, GetDiscordTimestamp(), GetPlayerName(ownerId), mapSquare, monumentName);
            SendBaseEventDiscord(message, webhookURL);
        }
        // ################################################################
        // OnAbandonedBaseStarted
        // Arguments (Vector3 position, ulong ownerId, bool allowPVP, List<BaseEntity> entities)
        // nivex: OnAbandonedBaseStarted(Vector3 center, float radius, bool allowPVP, List<BasePlayer> intruders, List<ulong> intruderIds, List<BaseEntity> entities)
        // ################################################################
        private void OnAbandonedBaseStarted(Vector3 eventPos, ulong ownerId, bool allowPVP, List<BaseEntity> entities)
        {   // check if feature enabled
            Puts("OnAbandonedBaseStarted triggered");
            var webhookURL = config?.Webhooks?.BasesWebhook ?? string.Empty;
            if (!(config?.Bases?.Enabled ?? false) || string.IsNullOrWhiteSpace(webhookURL))
                return;
            Puts("Check config: passed");
            // check for valid position
            if (eventPos == Vector3.zero || float.IsNaN(eventPos.x) || float.IsNaN(eventPos.y) || float.IsNaN(eventPos.z))
                return;
            Puts("Check position: passed");
            // Get event position on the map
            var monumentPrefix = Lang("MapHelperNear", null);
            GetMapSquareAndMonument(eventPos, out var mapSquare, out var monument);
            var monumentName = string.IsNullOrWhiteSpace(monument) ? string.Empty : $"{monumentPrefix}{monument}";
            var PvX = allowPVP ? "PVP" : "PVE";
            Puts("Send Discord message ...");
            // Assemble message to be sent to Discord
            var message = Lang("AbandonedBaseStartedDiscord", null, GetDiscordTimestamp(), GetPlayerName(ownerId), PvX, mapSquare, monumentName);
            SendBaseEventDiscord(message, webhookURL);
        }
        // ################################################################
        // OnAbandonedBaseCompleted
        // Arguments (Vector3 position, ulong ownerId, bool allowPVP, Dictionary<ulong, BasePlayer> participants, int lootCount)
        // nivex: this signature does not exist
        // ################################################################
        private void OnAbandonedBaseCompleted(Vector3 eventPos, ulong ownerId, bool allowPVP, Dictionary<ulong, BasePlayer> participants, int lootCount)
        {   // check if feature enabled
            Puts("OnAbandonedBaseCompleted triggered");
            var webhookURL = config?.Webhooks?.BasesWebhook ?? string.Empty;
            if (!(config?.Bases?.Enabled ?? false) || string.IsNullOrWhiteSpace(webhookURL))
                return;
            Puts("Check config: passed");
            // check for valid position
            if (eventPos == Vector3.zero || float.IsNaN(eventPos.x) || float.IsNaN(eventPos.y) || float.IsNaN(eventPos.z))
                return;
            Puts("Check position: passed");
            // Get event position on the map
            var monumentPrefix = Lang("MapHelperNear", null);
            GetMapSquareAndMonument(eventPos, out var mapSquare, out var monument);
            var monumentName = string.IsNullOrWhiteSpace(monument) ? string.Empty : $"{monumentPrefix}{monument}";
            var PvX = allowPVP ? "PVP" : "PVE";
            // Build participants list (excluding ownerId)
            var listRaiders = string.Join(", ",
                (participants?.Keys ?? Enumerable.Empty<ulong>())
                    .Select(GetPlayerName)
                    .Where(n => !string.IsNullOrWhiteSpace(n)));
            Puts("Send Discord message ...");
            // Assemble message to be sent to Discord
            var message = Lang("AbandonedBaseCompletedDiscord", null, GetDiscordTimestamp(), GetPlayerName(ownerId), PvX, mapSquare, monumentName) +
                          Lang("BasesRaiders", null, listRaiders);
            SendBaseEventDiscord(message, webhookURL);
        }
        // ################################################################
        // OnAbandonedBaseEventEnd
        // Arguments (Vector3 position, ulong ownerId, bool isPVP)
        // nivex: OnAbandonedBaseEnded(Vector3 eventPos, float radius, bool allowPVP, List<BasePlayer> participants,  List<ulong> participantIds, List<BaseEntity> entities)
        // ################################################################
        private void OnAbandonedBaseEventEnd(Vector3 eventPos, ulong ownerId, bool allowPVP)
        {   // check if feature enabled
            Puts("OnAbandonedBaseEventEnd triggered");
            var webhookURL = config?.Webhooks?.BasesWebhook ?? string.Empty;
            if (!(config?.Bases?.Enabled ?? false) || string.IsNullOrWhiteSpace(webhookURL))
                return;
            Puts("Check config: passed");
            // check for valid position
            if (eventPos == Vector3.zero || float.IsNaN(eventPos.x) || float.IsNaN(eventPos.y) || float.IsNaN(eventPos.z))
                return;
            Puts("Check position: passed");
            // Get event position on the map
            var monumentPrefix = Lang("MapHelperNear", null);
            GetMapSquareAndMonument(eventPos, out var mapSquare, out var monument);
            var monumentName = string.IsNullOrWhiteSpace(monument) ? string.Empty : $"{monumentPrefix}{monument}";
            var PvX = allowPVP ? "PVP" : "PVE";
            // Assemble message to be sent to Discord
            Puts("Send Discord message ...");
            var message = Lang("AbandonedBaseEndedDiscord", null, GetDiscordTimestamp(), GetPlayerName(ownerId), PvX, mapSquare, monumentName);
            SendBaseEventDiscord(message, webhookURL);
        }
        // ################################################################
        // Raidable Bases
        // ################################################################
        // OnRaidableBasePurchased
        // Arguments (string owner, Vector3 eventPos, string gridLabel, int mode, bool allowPVP, float unknownId,
        //                          float loadTime, string BaseName, DateTime spawnDateTime, DateTime despawnDateTime)
        private void OnRaidableBasePurchased(string owner, Vector3 eventPos, string gridLabel, int mode, bool allowPVP, float unknownId, float loadTime, string BaseName, DateTime spawnDateTime, DateTime despawnDateTime)
        {   // check if feature enabled
            var webhookURL = config?.Webhooks?.BasesWebhook ?? string.Empty;
            if (!(config?.Bases?.Enabled ?? false) || string.IsNullOrWhiteSpace(webhookURL))
                return;
            // check for valid position
            if (eventPos == Vector3.zero || float.IsNaN(eventPos.x) || float.IsNaN(eventPos.y) || float.IsNaN(eventPos.z))
                return;
            // resolve owner name from SteamID string (compact + safe)
            var ownerName = ulong.TryParse(owner?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var ownerId) && ownerId != 0UL
                          ? GetPlayerName(ownerId)
                          : string.Empty;
            if (string.IsNullOrWhiteSpace(ownerName) || ownerName == "0" ||
                string.Equals(ownerName.Trim(), ownerId.ToString(CultureInfo.InvariantCulture), System.StringComparison.Ordinal))
                ownerName = Lang("BasesUnowned", null);
            // get / set raid properties
            var PvX = allowPVP ? "PVP" : "PVE";
            var difficulty = Lang($"BasesDifficulty{mode}", null);
            var raidProfile = string.IsNullOrWhiteSpace(BaseName) ? string.Empty : BaseName.Trim();
            GetMapSquareAndMonument(eventPos, out string mapSquare, out string monument);
            // Assemble message to be sent to Discord
            var message = Lang("RaidableBasePurchasedDiscord", null, GetDiscordTimestamp(), ownerName, difficulty, PvX, raidProfile, mapSquare);
            SendBaseEventDiscord(message, webhookURL);
        }
        // ################################################################
        // OnRaidableBaseStarted
        // Arguments: (Vector3 eventPos, int mode, bool allowPVP)
        private void OnRaidableBaseStarted(Vector3 eventPos, int mode, bool allowPVP)
        {   // check if feature enabled
            var webhookURL = config?.Webhooks?.BasesWebhook ?? string.Empty;
            if (!(config?.Bases?.Enabled ?? false) || string.IsNullOrWhiteSpace(webhookURL))
                return;
            // check for valid position
            if (eventPos == Vector3.zero || float.IsNaN(eventPos.x) || float.IsNaN(eventPos.y) || float.IsNaN(eventPos.z))
                return;
            // get / set raid properties
            var PvX = allowPVP ? "PVP" : "PVE";
            var difficulty = Lang($"BasesDifficulty{mode}", null);
            GetMapSquareAndMonument(eventPos, out string mapSquare, out string monument);
            // compose message, send, done
            var message = Lang("RaidableBaseStartedDiscord", null, GetDiscordTimestamp(), difficulty, PvX, mapSquare);
            SendBaseEventDiscord(message, webhookURL);
        }
        // ################################################################
        // OnRaidableBaseCompleted
        // Arguments: (Vector3 eventPos, int mode, bool allowPVP, ulong ownerId, List<ulong> raidersList)
        private void OnRaidableBaseCompleted(Vector3 eventPos, int mode, bool allowPVP, ulong ownerId, List<ulong> raidersList)
        {   // check if feature enabled
            var webhookURL = config?.Webhooks?.BasesWebhook ?? string.Empty;
            if (!(config?.Bases?.Enabled ?? false) || string.IsNullOrWhiteSpace(webhookURL))
                return;
            // check for valid position
            if (eventPos == Vector3.zero || float.IsNaN(eventPos.x) || float.IsNaN(eventPos.y) || float.IsNaN(eventPos.z))
                return;
            // get / set raid properties
            var PvX = allowPVP ? "PVP" : "PVE";
            var difficulty = Lang($"BasesDifficulty{mode}", null);
            GetMapSquareAndMonument(eventPos, out string mapSquare, out string monument);
            var ownerName = GetPlayerName(ownerId) ?? Lang("BasesUnowned", null) ?? string.Empty;
            // build raiders list (player names of participants, comma-separated)
            var raidersNames = string.Join(", ",
                (raidersList ?? Enumerable.Empty<ulong>())
                                .Select(GetPlayerName)
                                .Where(n => !string.IsNullOrWhiteSpace(n)));
            // Assemble message to be sent to Discord
            var message = Lang("RaidableBaseCompletedDiscord", null, GetDiscordTimestamp(), ownerName, difficulty, PvX, mapSquare) +
                          Lang("BasesRaiders", null, raidersNames);
            SendBaseEventDiscord(message, webhookURL);
        }
        // ################################################################
        // OnRaidableBaseEnded
        // Arguments: (Vector3 eventPos, int mode, bool allowPVP)
        private void OnRaidableBaseEnded(Vector3 eventPos, int mode, bool allowPVP)
        {   // check if feature enabled
            var webhookURL = config?.Webhooks?.BasesWebhook ?? string.Empty;
            if (!(config?.Bases?.Enabled ?? false) || string.IsNullOrWhiteSpace(webhookURL))
                return;
            // check for valid position
            if (eventPos == Vector3.zero || float.IsNaN(eventPos.x) || float.IsNaN(eventPos.y) || float.IsNaN(eventPos.z))
                return;
            // get / set raid properties
            var PvX = allowPVP ? "PVP" : "PVE";
            var difficulty = Lang($"BasesDifficulty{mode}", null);
            GetMapSquareAndMonument(eventPos, out string mapSquare, out string monument);
            // Assemble message to be sent to Discord
            var message = Lang("RaidableBaseEndedDiscord", null, GetDiscordTimestamp(), difficulty, PvX, mapSquare);
            SendBaseEventDiscord(message, webhookURL);
        }

        private void SendBaseEventDiscord(string msg, string webhookURL)
        {
            if (config.Bases.DiscordNotify)
            {
                webhookURL = config.Webhooks.BasesWebhook ?? string.Empty;
                if (!string.IsNullOrEmpty(webhookURL))
                    SendDiscordMessage(webhookURL, msg);
            }
        }

        #endregion

        #region TRAVELLING VENDOR

        // Listen for Travelling Vendor spawn
        private void OnEntitySpawned(BaseNetworkable entity)
        {
            var vendor = entity as TravellingVendor;
            if (vendor == null) return;

            // Early exit if feature is disabled or webhook isn't configured
            if (!(config?.TravellingVendor?.Enabled ?? false) ||
                !(config?.TravellingVendor?.DiscordNotify ?? false) ||
                string.IsNullOrWhiteSpace(config?.Webhooks?.VendorWebhook ?? null))
                return;

            // Snapshot the network ID so we can safely re-fetch next tick
            var vendorId = vendor.net?.ID ?? default;

            // Use nextTick to ensure the entity is fully initialized
            NextTick(() =>
            {
                // Re-resolve the entity to avoid stale references
                var v = BaseNetworkable.serverEntities.Find(vendorId) as TravellingVendor;
                if (v == null || v.IsDestroyed)
                    return;

                // Validate position
                var pos = v.transform.position;
                if (pos == Vector3.zero || float.IsNaN(pos.x) || float.IsNaN(pos.y) || float.IsNaN(pos.z))
                    return;

                var monumentPrefix = Lang("MapHelperNear", null);
                GetMapSquareAndMonument(pos, out var mapSquare, out var monument);
                var monumentName = string.IsNullOrWhiteSpace(monument) ? string.Empty : $"{monumentPrefix}{monument}";

                SendDiscordMessage(config.Webhooks.VendorWebhook,
                    Lang("VendorSpawnedDiscord", null, GetDiscordTimestamp(), mapSquare, monumentName));
            });
        }

        // Stretch: Vendor despawn (must be reliable and safe)
        // Listen for Travelling Vendor despawn and the exact location of the despawn
        private void OnEntityKill(BaseNetworkable entity)
        {
            var vendor = entity as TravellingVendor;
            if (vendor == null) return;

            // Early exit if feature is disabled or notifications are disabled
            if (!(config?.TravellingVendor?.Enabled ?? false) ||
                !(config?.TravellingVendor?.NotifyDespawn ?? false) ||
                string.IsNullOrEmpty(config?.Webhooks?.VendorWebhook ?? null))
                return;

            // Snapshot position immediately; the entity is being torn down during this hook
            var pos = Vector3.zero;
            var tr = vendor.transform;
            if (tr != null)
                pos = tr.position;

            // Validate the snapshot before doing any work
            var hasPos = !(pos == Vector3.zero || float.IsNaN(pos.x) || float.IsNaN(pos.y) || float.IsNaN(pos.z));

            // Safely attempt to get position
            var monument = string.Empty;
            var mapSquare = string.Empty;
            var monumentName = string.Empty;

            if (hasPos)
            {
                var monumentPrefix = Lang("MapHelperNear", null);
                GetMapSquareAndMonument(pos, out mapSquare, out monument);
                monumentName = string.IsNullOrWhiteSpace(monument) ? string.Empty : $"{monumentPrefix}{monument}";
            }

            SendDiscordMessage(config.Webhooks.VendorWebhook,
                Lang("VendorDespawnedDiscord", null, GetDiscordTimestamp(), mapSquare, monumentName));
        }

        #endregion

        #region PLUGIN LOAD/UNLOAD

        private void OnPluginLoaded(Plugin plugin)
        {
            if (plugin == null) return;
            if (plugin?.Name == "FARMapHelper") FARMapHelper = plugin; // point to fresh instance
            if (PluginMonitorIgnoreEvent() || !(config?.PluginMonitor?.DiscordNotify ?? false))
                return;
            // Check config
            var webhookURL = config?.Webhooks?.PluginsWebhook ?? string.Empty;
            if (!(config?.PluginMonitor?.Enabled ?? false) || string.IsNullOrWhiteSpace(webhookURL)) return;
            // We're good, send message to Discord
            var message = Lang("PluginEventDiscord", null, GetDiscordTimestamp(), plugin.Name, plugin.Version, plugin.Author, "loaded");
            message = message.Replace(":pause:", ":ballot_box_with_check:");    // change placeholder to correct emoji
            SendDiscordMessage(webhookURL, message);
        }
        private void OnPluginUnloaded(Plugin plugin)
        {
            if (plugin == null) return;
            if (plugin?.Name == "FARMapHelper") FARMapHelper = null; // clear stale reference
            if (PluginMonitorIgnoreEvent() || !(config?.PluginMonitor?.DiscordNotify ?? false))
                return;
            // Check config
            var webhookURL = config?.Webhooks?.PluginsWebhook ?? string.Empty;
            if (!(config?.PluginMonitor?.Enabled ?? false) || string.IsNullOrWhiteSpace(webhookURL)) return;
            // We're good, send message to Discord
            var message = Lang("PluginEventDiscord", null, GetDiscordTimestamp(), plugin.Name, plugin.Version, plugin.Author, "unloaded");
            message = message.Replace(":pause:", ":stop_sign:");            // change placeholder to correct emoji
            SendDiscordMessage(webhookURL, message);
        }
        private bool IsMapHelperReady() =>
            FARMapHelper != null && FARMapHelper.IsLoaded;
        private bool PluginMonitorIgnoreEvent()
        {
            var ignoreStartupSeconds = config?.General?.PluginMonitorStartupIgnoreSeconds ?? 120;
            // returns true if server within (120) seconds after start, or if server is shutting down. Otherwise false.
            return (DateTime.UtcNow - pluginStartTime).TotalSeconds < ignoreStartupSeconds || Interface.Oxide.IsShuttingDown;
        }

        #endregion

        #region users.cfg

        private void CheckUsersCfgHash()
        {
            if (!(config?.UsersCfg?.Enabled ?? false))
                return;

            if (string.IsNullOrWhiteSpace(_usersCfgPath) ||
                !File.Exists(_usersCfgPath))
                return;

            try
            {
                byte[] data = File.ReadAllBytes(_usersCfgPath);
                var checksum = ComputeChecksum(data);
                var lastHash = GetLastHash();

                // If checksum changed, update and notify
                if (!string.Equals(lastHash, checksum, StringComparison.Ordinal))
                {   // first save the changed hash
                    SetLastHash(checksum);

                    bool firstRun = string.IsNullOrWhiteSpace(lastHash);
                    bool notifyEnabled = config?.UsersCfg?.DiscordNotify ?? false;
                    var webhookURL = config?.Webhooks?.UsersCfgWebhook ?? string.Empty;

                    // Notify only when: not first run, notifications enabled, and webhook is configured
                    if (!firstRun && !string.IsNullOrWhiteSpace(webhookURL) && notifyEnabled)
                        SendDiscordMessage(webhookURL, Lang("UsersCfgChangedDiscord", null, GetDiscordTimestamp(), lastHash, checksum));

                }
            }
            catch (Exception ex)
            {
                PrintWarning($"Error checking users.cfg checksum: {ex.Message}");
            }
        }

        // Fletcher-16 checksum (big-endian), returned as 4-char uppercase hex (e.g., "1A2B")
        private string ComputeChecksum(byte[] bytes)
        {
            // Fletcher-16 over 8-bit data
            int sum1 = 0;
            int sum2 = 0;

            // Using branchless reductions to avoid the cost of % in tight loops.
            // This is safe because both sums are guaranteed to stay < 510 between reductions.
            for (int i = 0; i < bytes.Length; i++)
            {
                sum1 += bytes[i];
                if (sum1 >= 255) sum1 -= 255;

                sum2 += sum1;
                if (sum2 >= 255) sum2 -= 255;
            }

            ushort check = (ushort)((sum2 << 8) | sum1);
            return check.ToString("X4");
        }

        #endregion

        #region GUESS THE NUMBER

        // Helper method to do the actual messaging to Discord
        private void SendGTNNotification(string langKey, params object[] args)
        {
            if (!(config?.GuessTheNumber?.Enabled ?? false) ||
                !(config?.GuessTheNumber?.DiscordNotify ?? false))
                return;

            var webhookURL = config?.Webhooks?.GuessNumberWebhook ?? string.Empty;
            var message = Lang(langKey, null, args);

            if (!string.IsNullOrEmpty(webhookURL))
                SendDiscordMessage(webhookURL, message);
        }

        // Event handlers now call the helper method
        private object OnGTNEventStart(int min, int max)
        {
            SendGTNNotification("GTNStartDiscord", GetDiscordTimestamp(), min, max);
            return null;
        }

        private object OnGTNWin(BasePlayer player, int number)
        {
            if (player == null) return null;
            SendGTNNotification("GTNWinDiscord", GetDiscordTimestamp(), GetPlayerName(player.userID), number);
            return null;
        }

        private object OnGTNTimeout(int number)
        {
            SendGTNNotification("GTNTimeoutDiscord", GetDiscordTimestamp(), number);
            return null;
        }

        #endregion

        #region SCHEDULED COMMAND (UTC)
        // Minimal, config-driven daily scheduler (UTC) for a console command.
        private Timer _dailyCmdTimer;

        // hourly align helper (self-managed by AdjustDailyUtcCommand)
        private Timer _alignTimer;

        // a handle to the local ScheduleNext() so Adjust can re-arm without duplicating logic
        private Action _scheduleNextHandle;

        // tuneables (make config-driven if you prefer)
        private const int AlignPeriodSeconds = 3600;   // 1 hour
        private const int SkipAdjustWithinSeconds = 1800; // 30 minutes

        private void StartDailyUtcCommand(string timeUtc, string fullConsoleLine)
        {
            StopDailyUtcCommand();

            if (string.IsNullOrWhiteSpace(fullConsoleLine)) { PrintWarning("No command set."); return; }
            if (!TimeSpan.TryParseExact(timeUtc, new[] { @"hh\:mm\:ss", @"hh\:mm" }, CultureInfo.InvariantCulture, out var target))
            {
                PrintWarning($"Invalid UTC time '{timeUtc}'. Use HH:mm or HH:mm:ss.");
                return;
            }

            void ScheduleNext()
            {
                var now = DateTime.UtcNow;
                var runAt = now.Date.Add(target);
                if (runAt <= now) runAt = runAt.AddDays(1); // midnight rollover

                var delay = Math.Max(1.0, (runAt - now).TotalSeconds);
                _dailyCmdTimer = timer.Once((float)delay, () =>
                {
                    try { ConsoleSystem.Run(ConsoleSystem.Option.Server.Quiet(), fullConsoleLine); }
                    catch (Exception ex) { PrintError($"Daily UTC command error: {ex}"); }
                    ScheduleNext(); // re-arm for the next day
                });
            }

            // expose the local function to the adjuster without duplicating any logic
            _scheduleNextHandle = ScheduleNext;

            ScheduleNext();

            // set up the adjuster once; it will maintain the long timer from here on
            AdjustDailyUtcCommand(target);
        }

        private void StopDailyUtcCommand()
        {
            _dailyCmdTimer?.Destroy();
            _dailyCmdTimer = null;

            _alignTimer?.Destroy();
            _alignTimer = null;

            _scheduleNextHandle = null;
        }

        // Adjuster: runs hourly, keeps the daily long timer aligned to wall clock.
        // It never executes the command; it only re-arms the long timer via ScheduleNext().
        private void AdjustDailyUtcCommand(TimeSpan target)
        {
            // clear any previous align loop
            _alignTimer?.Destroy();

            _alignTimer = timer.Every(AlignPeriodSeconds, () =>
            {
                // If scheduling is not active, nothing to do.
                if (_scheduleNextHandle == null) return;

                // If the next intended wall-clock run is within the skip window, leave the current timer untouched.
                var now = DateTime.UtcNow;
                var nextIntended = now.Date.Add(target);
                if (nextIntended <= now) nextIntended = nextIntended.AddDays(1);

                var untilNext = nextIntended - now;
                if (untilNext.TotalSeconds <= SkipAdjustWithinSeconds) return;

                // Re-arm: kill the current long timer and immediately recreate it by calling ScheduleNext().
                _dailyCmdTimer?.Destroy();
                _dailyCmdTimer = null;                // <- ensure we don't hold a disposed handle
                _scheduleNextHandle.Invoke();         // <- this calls your local ScheduleNext() and recreates the long timer
            });
        }

        #endregion

        #region UTILITY METHODS

        // Try to get map square and monument via external API_MapInfo if available
        private void GetMapSquareAndMonument(Vector3 position, out string mapSquare, out string monument)
        {
            // Try to fetch data from the hook
            var apiResult = FARMapHelper?.Call("API_MapInfo", position);

            mapSquare = "noAPI";
            monument = string.Empty;

            if (apiResult is Tuple<string, string> t) { mapSquare = t.Item1 ?? mapSquare; monument = t.Item2; }
            else if (apiResult is ValueTuple<string, string> vt) { mapSquare = vt.Item1 ?? mapSquare; monument = vt.Item2; }
            else if (apiResult is object[] arr && arr.Length >= 1) { mapSquare = arr[0]?.ToString() ?? mapSquare; if (arr.Length >= 2) monument = arr[1]?.ToString(); }
            else if (apiResult != null) mapSquare = apiResult.ToString();
        }

        // Posts a simple string message to Discord webhook using Oxide's webrequests library
        private static readonly Dictionary<string, string> JsonHeaders = new Dictionary<string, string>
        {
            ["Content-Type"] = "application/json"
        };

        // Centralized Discord-specific text preparation
        private static class DiscordText
        {
            // Call this before sending to Discord. Non-Discord paths should use the original message.
            public static string Prepare(string input, DiscordConfig opt)
            {
                if (string.IsNullOrWhiteSpace(input) || opt == null) return input;

                var text = input;

                if (opt.EscapeMarkdown)
                    text = EscapeMarkdown(text);

                if (opt.BreakMentions)
                    text = BreakMentions(text); // belt-and-braces; allowed_mentions also used

                if (opt.TruncateToLimit && opt.ContentLimit > 0 && text.Length > opt.ContentLimit)
                    text = Truncate(text, opt.ContentLimit, opt.TruncationSuffix);

                return text;
            }

            // Escapes common Discord markdown tokens to avoid accidental formatting
            public static string EscapeMarkdown(string value)
            {
                if (string.IsNullOrEmpty(value)) return value;

                // Characters to escape in Discord markdown
                // \ * _ ~ ` | > [ ] ( ) #
                var sb = new StringBuilder(value.Length + 16);
                for (int i = 0; i < value.Length; i++)
                {
                    char c = value[i];
                    switch (c)
                    {
                        case '\\':
                        case '*':
                        case '_':
                        case '~':
                        case '`':
                        case '|':
                        case '>':
                        case '[':
                        case ']':
                        case '(':
                        case ')':
                        case '#':
                            sb.Append('\\').Append(c);
                            break;
                        default:
                            sb.Append(c);
                            break;
                    }
                }
                return sb.ToString();
            }

            // Breaks @mentions with a zero-width space to avoid pings even in code/unexpected areas
            public static string BreakMentions(string value)
            {
                if (string.IsNullOrEmpty(value)) return value;
                // inserts U+200B after '@'
                return value.Replace("@", "@\u200B");
            }

            public static string Truncate(string value, int max, string suffix)
            {
                if (string.IsNullOrEmpty(value) || value.Length <= max) return value;

                var keep = Math.Max(0, max - (suffix?.Length ?? 0));
                if (keep == 0) return value.Substring(0, max);

                var truncated = value.Substring(0, keep);

                // If we cut inside a code fence, close it (simple heuristic)
                var openFences = CountOccurrences(truncated, "```") % 2 == 1;
                if (openFences) truncated += "```";

                return truncated + (suffix ?? string.Empty);
            }

            private static int CountOccurrences(string haystack, string needle)
            {
                if (string.IsNullOrEmpty(haystack) || string.IsNullOrEmpty(needle)) return 0;

                int count = 0, pos = 0;
                while ((pos = haystack.IndexOf(needle, pos, StringComparison.Ordinal)) >= 0)
                {
                    count++;
                    pos += needle.Length;
                }
                return count;
            }
        }

        private static string JsonEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            var sb = new StringBuilder(s.Length + 16);
            foreach (var c in s)
            {
                switch (c)
                {
                    case '\"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 32) sb.AppendFormat("\\u{0:X4}", (int)c);
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }


        /// <summary>
        /// Send a message to a DiscordConfig webhook.
        /// NOTE: changed parameter type to DiscordConfig (your config class).
        /// </summary>
        private void SendDiscordMessage(string webhookURL, string content) =>
                     SendDiscordMessage(webhookURL, content, config?.Discord);
        private void SendDiscordMessage(string webhookURL, string content, DiscordConfig opt, string overrideUsername = null, string overrideAvatarUrl = null)
        {
            if (string.IsNullOrWhiteSpace(webhookURL) || string.IsNullOrWhiteSpace(content) || opt == null)
                return;

            var prepared = DiscordText.Prepare(content, opt);

            // Build minimal JSON payload with optional username/avatar and allowed_mentions
            var username = string.IsNullOrWhiteSpace(overrideUsername) ? opt.Username : overrideUsername;
            var avatar = string.IsNullOrWhiteSpace(overrideAvatarUrl) ? opt.AvatarUrl : overrideAvatarUrl;

            // allowed_mentions empty to suppress @everyone/@here/role mentions if desired
            var allowedMentionsFragment = opt.SuppressPings ? "\"allowed_mentions\":{\"parse\":[]}" : null;

            var parts = new List<string>
                { $"\"content\":\"{JsonEscape(prepared)}\"" };
            if (!string.IsNullOrWhiteSpace(username)) parts.Add($"\"username\":\"{JsonEscape(username)}\"");
            if (!string.IsNullOrWhiteSpace(avatar)) parts.Add($"\"avatar_url\":\"{JsonEscape(avatar)}\"");
            if (allowedMentionsFragment != null) parts.Add(allowedMentionsFragment);

            var payload = "{" + string.Join(",", parts) + "}";

            // Use configured timeout (seconds) and log control
            float timeout = Math.Max(1f, (float)opt.TimeoutSeconds);

            var headers = new Dictionary<string, string>(JsonHeaders);

            // Enqueue the web request. Replace any previous webrequest.Enqueue call with this.
            webrequest.EnqueuePost(webhookURL, payload, (code, response) =>
            {
                if (code >= 200 && code < 300) return;

                if (opt.LogFailures)
                {
                    var snippet = string.IsNullOrEmpty(response) ? "<empty>" :
                                  response.Length > 160 ? response.Substring(0, 160) + "..." : response;
                    PrintWarning($"Discord webhook failed: HTTP {code} | {snippet}");
                }
            }, this, headers, timeout);
        }
        #endregion
    }
}
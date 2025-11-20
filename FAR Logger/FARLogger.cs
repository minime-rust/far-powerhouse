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
    [Info("FAR: Logger", "miniMe", "1.3.0")]
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
            public AirdropConfig Airdrop { get; set; } = new AirdropConfig();
            public BasesConfig Bases { get; set; } = new BasesConfig(); // This handles Abandoned and Raidable Bases
            public DoorKnockersConfig DoorKnockers { get; set; } = new DoorKnockersConfig();
            public GTNConfig GuessTheNumber { get; set; } = new GTNConfig();
            public PluginMonitorConfig PluginMonitor { get; set; } = new PluginMonitorConfig();
            public ScheduledCommandConfig ScheduledCommand { get; set; } = new ScheduledCommandConfig();
            public ServerWipesConfig ServerWipes { get; set; } = new ServerWipesConfig();
            public StartupCommandsConfig StartupCommands { get; set; } = new StartupCommandsConfig();
            public TravellingVendorConfig TravellingVendor { get; set; } = new TravellingVendorConfig();
            public UsersCfgConfig UsersCfg { get; set; } = new UsersCfgConfig();
            public WebhooksConfig Webhooks { get; set; } = new WebhooksConfig();
        }

        private class GeneralConfig
        {
            public int PluginMonitorStartupIgnoreSeconds { get; set; } = 120; // seconds to ignore plugin load events after server start
            public bool Use24HourTime { get; set; } = true; // Player's culture used for date/time format when possible
        }

        private class AirdropConfig
        {
            public bool Enabled { get; set; } = false;
            public bool ChatNotify { get; set; } = false;
            public bool DiscordNotify { get; set; } = false;
        }

        private class BasesConfig
        {
            public bool Enabled { get; set; } = false;
            public bool DiscordNotify { get; set; } = false;
        }

        private class DoorKnockersConfig
        {
            public bool Enabled { get; set; } = false;
            public bool DiscordNotify { get; set; } = false;
        }

        private class GTNConfig
        {
            public bool Enabled { get; set; } = false;
            public bool DiscordNotify { get; set; } = false;
        }

        private class PluginMonitorConfig
        {
            public bool Enabled { get; set; } = false;
            public bool DiscordNotify { get; set; } = false;
        }

        private class ScheduledCommandConfig
        {
            public bool Enabled { get; set; } = false;                      // enable (true) or disable the scheduler
            public string TimeUtc { get; set; } = "00:45";                  // scheduled time on real-time clock
            public string Command { get; set; } = "restart 900 \"nightly restart\""; // the to-be-executed command
        }

        private class ServerWipesConfig
        {
            public bool Enabled { get; set; } = false;
            public bool DiscordNotify { get; set; } = false;
            public string RustMapsApiKey { get; set; } = "";
        }

        private class StartupCommandsConfig
        {
            public bool Enable { get; set; } = false;
            public int DelaySeconds { get; set; } = 60;
            public List<string> Commands { get; set; } = new List<string>();
        }

        private class TravellingVendorConfig
        {
            public bool Enabled { get; set; } = false;
            public bool DiscordNotify { get; set; } = false;
            public bool NotifyDespawn { get; set; } = false;
        }

        private class UsersCfgConfig
        {
            public bool Enabled { get; set; } = false;
            public bool DiscordNotify { get; set; } = false;
        }

        private class WebhooksConfig
        {
            public string AirdropWebhook { get; set; } = "";
            public string BasesWebhook { get; set; } = "";
            public string DoorKnockersWebhook { get; set; } = "";
            public string GuessNumberWebhook { get; set; } = "";
            public string PluginsWebhook { get; set; } = "";
            public string UsersCfgWebhook { get; set; } = "";
            public string VendorWebhook { get; set; } = "";
            public string WipeWebhook { get; set; } = "";
        }

        protected override void LoadDefaultConfig()
        {
            config = new ConfigData();
            SaveConfig();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                config = Config.ReadObject<ConfigData>();
                config.General ??= new GeneralConfig();
                config.Airdrop ??= new AirdropConfig();
                config.Bases ??= new BasesConfig();
                config.DoorKnockers ??= new DoorKnockersConfig();
                config.GuessTheNumber ??= new GTNConfig();
                config.PluginMonitor ??= new PluginMonitorConfig();
                config.ScheduledCommand ??= new ScheduledCommandConfig();
                config.ServerWipes ??= new ServerWipesConfig();
                config.TravellingVendor ??= new TravellingVendorConfig();
                config.UsersCfg ??= new UsersCfgConfig();
                config.Webhooks ??= new WebhooksConfig();
            }
            // Fallback to default config if deserialization fails
            catch (Exception ex)
            {
                PrintWarning($"LoadConfig failed, falling back to defaults: {ex.Message}");
                LoadDefaultConfig();
            }
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

        // #####################################################################
        // Wipe (server.seed) Monitor
        private uint GetWipeSeed() => data?.Wipe?.Seed ?? 0u;
        private void SetWipeSeed(uint seed) { data.Wipe.Seed = seed; SaveDataDebounced(); }
        // "server/{identity}/cfg/users.cfg" Monitor
        private string GetLastHash() => data?.UsersCfg?.LastHash ?? string.Empty;
        private void SetLastHash(string hash) { data.UsersCfg.LastHash = hash; SaveDataDebounced(); }
        // Airdrop tracking: in-memory to avoid double notification per crate
        private readonly HashSet<ulong> lootedSupplyDrops = new HashSet<ulong>();

        // #####################################################################
        // Discord Message Queue
        private class DiscordMessage
        {
            public string WebhookUrl;
            public string Payload; // already JSON-serialized payload ({"content":"..."})
        }
        private bool _discordSendInProgress; // controls whether a message can be removed from queue
        private int _discordRetryAfterMs; // 0 = normal, >0 = wait that many ms before trying again

        private Timer _discordQueueTimer;
        private readonly Queue<DiscordMessage> _discordQueue = new Queue<DiscordMessage>();
        private const float DiscordIntervalSeconds = 1.0f; // 1 message per second default (tuneable)
        private readonly object _discordQueueLock = new object();
        // #####################################################################
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
                // Other helpers, rule enforcements and notifications
                [Lang_MapHelperNear] = " near ",
                [Lang_SupplyDropLooted] = "{0} is looting a Supply Drop at {1}{2}",
                [Lang_SupplyDropLootedDiscord] = ":gift: {0} `{1}` looted a Supply Drop at `{2}`{3}",
                [Lang_PluginEventDiscord] = ":pause: {0} `{1}` plugin v`{2}` by `{3}` was `{4}`",
                [Lang_UsersCfgChangedDiscord] = ":skull_crossbones: {0} The `users.cfg` file has changed. Checksums old|new: {1}|{2}",
                [Lang_DoorKnockersDiscord] = ":no_entry: {0} Rejected: `{1}` ({2}) for `{3}` from {4}",
                [Lang_GTNStartDiscord] = ":1234: {0} The `Guess the Number` event started! Number range `{1}` - `{2}`",
                [Lang_GTNWinDiscord] = ":1234: {0} `{1}` has won the `Guess the Number` event! Winning number: `{2}`",
                [Lang_GTNTimeoutDiscord] = ":1234: {0} The `Guess the Number` event timed out, nobody won! Winning number: `{1}`"
            }, this, "en");
        }

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
            _discordQueueTimer?.Destroy();
            saveDebounce?.Destroy();
            _UsersCfgTimer?.Destroy();
            _discordQueueTimer = null;
            saveDebounce = null;
            _UsersCfgTimer = null;
            // Clean up the FAR: Map Helper instance
            FARMapHelper = null;
            // Unsubscribe from the Unity log stream
            Application.logMessageReceived -= OnLog;
            // Clear Task Scheduler
            StopDailyUtcCommand();
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
                _UsersCfgTimer?.Destroy();
                _UsersCfgTimer = timer.Every(300f, CheckUsersCfgHash);
                Puts($"UsersCfg monitor enabled. Watching this file: {_usersCfgPath}");
            }

            // Door Knockers monitoring (rejected user logins)
            if (config?.DoorKnockers?.Enabled ?? false)
                Puts($"Door Knockers monitor enabled. Watching rejected user logins.");

            // --- GUESS THE NUMBER ---
            if (config?.GuessTheNumber?.Enabled ?? false)
            {
                if (plugins?.Find("GuessTheNumber") != null)
                    Puts("Guess The Number plugin found. Subscribing to its hooks.");
                else
                    Puts("Guess The Number plugin not found. GTN notifications will be disabled.");
            }

            // --- RAIDABLE BASES ---
            if (config?.Bases?.Enabled ?? false)
            {
                if (plugins?.Find("RaidableBases") != null)
                    Puts("Raidable Bases plugin found. Subscribing to its hooks.");
                else
                    Puts("Raidable Bases plugin not found. Raidable Base notifications will be disabled.");
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
            if (config?.Airdrop?.DiscordNotify ?? false)
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
            int worldSize = ConVar.Server.worldsize;
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

            var rustMapsApiKey = config?.ServerWipes?.RustMapsApiKey ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(rustMapsApiKey))
                RequestRustMapGeneration(rustMapsApiKey, worldSize, mapSeed);
        }

        private void RequestRustMapGeneration(string apiKey, int worldSize, uint mapSeed)
        {

            var jobKey = $"{worldSize}_{mapSeed}";
            const int PollIntervalSeconds = 600; // 10 min
            const int MaxElapsedSeconds = 3600;  // 1 hour
            var mapUrl = $"https://rustmaps.com/map/{jobKey}";
            var message = Lang("ServerWipeDetectedDiscord", null, mapUrl);
            var webhookURL = config?.Webhooks?.WipeWebhook ?? string.Empty;

            void PollForReadyMap(int elapsed)
            {
                string genUrl = $"https://api.rustmaps.com/v4/maps/{worldSize}/{mapSeed}";
                var headers = new Dictionary<string, string>
                {
                    ["accept"] = "application/json",
                    ["X-API-Key"] = apiKey
                };
                webrequest.Enqueue(genUrl, null, (code, response) =>
                {
                    if (code == 200 || elapsed >= MaxElapsedSeconds)
                    {
                        // Map is ready! Notify Discord.
                        if (config?.ServerWipes?.DiscordNotify ?? false)
                            SendDiscordMessage(webhookURL, message);
                        return;
                    }

                    // Otherwise, wait and retry.
                    timer.Once(PollIntervalSeconds, () => PollForReadyMap(elapsed + PollIntervalSeconds));
                }, this, Oxide.Core.Libraries.RequestMethod.GET, headers);
            }

            // Always start polling (even if POST returns 409)
            string reqUrl = "https://api.rustmaps.com/v4/maps";
            string reqPayload = $"{{\"size\":{worldSize},\"seed\":{mapSeed},\"staging\":false}}";
            var reqHeaders = new Dictionary<string, string>
            {
                ["Content-Type"] = "application/json",
                ["accept"] = "application/json",
                ["X-API-Key"] = apiKey
            };

            webrequest.Enqueue(reqUrl, reqPayload, (code, response) =>
            {
                // Log only for debugging, not notification. Start polling regardless of code.
                PollForReadyMap(0);
            }, this, Oxide.Core.Libraries.RequestMethod.POST, reqHeaders);
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
            var userLang = (lang.GetLanguage(player.UserIDString) ?? "en").ToLowerInvariant();
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
                    (config?.Bases?.DiscordNotify ?? false))
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

        #region RAIDABLE BASES

        // ################################################################
        // Raidable Bases
        // ################################################################
        // OnRaidableBasePurchased
        // Arguments (string owner, Vector3 eventPos, string gridLabel, int mode, bool allowPVP, float unknownId,
        //                          float loadTime, string BaseName, DateTime spawnDateTime, DateTime despawnDateTime)
        private void OnRaidableBasePurchased(string owner, Vector3 eventPos, string gridLabel, int mode, bool allowPVP, float unknownId, float loadTime, string BaseName, DateTime spawnDateTime, DateTime despawnDateTime)
        {   // check if feature enabled
            var webhookURL = config?.Webhooks?.BasesWebhook ?? string.Empty;
            if (!(config?.Bases?.Enabled ?? false) || !(config?.Bases?.DiscordNotify ?? false) || string.IsNullOrWhiteSpace(webhookURL))
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
            SendDiscordMessage(webhookURL, message);
        }
        // ################################################################
        // OnRaidableBaseStarted
        // Arguments: (Vector3 eventPos, int mode, bool allowPVP)
        private void OnRaidableBaseStarted(Vector3 eventPos, int mode, bool allowPVP)
        {   // check if feature enabled
            var webhookURL = config?.Webhooks?.BasesWebhook ?? string.Empty;
            if (!(config?.Bases?.Enabled ?? false) || !(config?.Bases?.DiscordNotify ?? false) || string.IsNullOrWhiteSpace(webhookURL))
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
            SendDiscordMessage(webhookURL, message);
        }
        // ################################################################
        // OnRaidableBaseCompleted
        // Arguments: (Vector3 eventPos, int mode, bool allowPVP, ulong ownerId, List<ulong> raidersList)
        private void OnRaidableBaseCompleted(Vector3 eventPos, int mode, bool allowPVP, ulong ownerId, List<ulong> raidersList)
        {   // check if feature enabled
            var webhookURL = config?.Webhooks?.BasesWebhook ?? string.Empty;
            if (!(config?.Bases?.Enabled ?? false) || !(config?.Bases?.DiscordNotify ?? false) || string.IsNullOrWhiteSpace(webhookURL))
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
            SendDiscordMessage(webhookURL, message);
        }
        // ################################################################
        // OnRaidableBaseEnded
        // Arguments: (Vector3 eventPos, int mode, bool allowPVP)
        private void OnRaidableBaseEnded(Vector3 eventPos, int mode, bool allowPVP)
        {   // check if feature enabled
            var webhookURL = config?.Webhooks?.BasesWebhook ?? string.Empty;
            if (!(config?.Bases?.Enabled ?? false) || !(config?.Bases?.DiscordNotify ?? false) || string.IsNullOrWhiteSpace(webhookURL))
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
            SendDiscordMessage(webhookURL, message);
        }

        #endregion

        #region TRAVELLING VENDOR

        // Listen for Travelling Vendor spawn and the exact location of the spawn
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
            if (!(config?.UsersCfg?.Enabled ?? false) ||
                string.IsNullOrWhiteSpace(_usersCfgPath) ||
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
            catch (Exception ex) { PrintWarning($"Error checking users.cfg checksum: {ex.Message}"); }
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

            if (apiResult is ValueTuple<string, string>(string square, string mon))
            {
                mapSquare = square ?? mapSquare;
                monument = mon;
            }
        }

        // Public-facing entrypoint for other plugins to hand off Discord messages
        private object API_SendDiscordMessage(string webhookUrl, string message)
        {
            if (string.IsNullOrWhiteSpace(webhookUrl) || string.IsNullOrWhiteSpace(message))
                return null; // invalid handoff

            try
            {  // handshake / ack
                SendDiscordMessage(webhookUrl, message);
                return true;
            }
            catch (Exception ex)
            {   // signal failure, caller should fallback
                PrintWarning($"API_SendDiscordMessage failed: {ex.Message}");
                return null;
            }
        }

        // Enqueue or send message (public facing)
        private void SendDiscordMessage(string webhookUrl, string message)
        {
            if (string.IsNullOrWhiteSpace(webhookUrl) || string.IsNullOrWhiteSpace(message))
                return; // nothing to send

            // Discord has a strict 2000-character limit
            const int maxLen = 2000;
            if (message.Length > maxLen)
                message = message.Substring(0, maxLen - 3) + "...";

            // Prepare JSON payload
            var payload = JsonConvert.SerializeObject(new { content = message });

            EnqueueDiscordMessage(webhookUrl, payload);
        }

        // Push a prepared payload into the queue
        private void EnqueueDiscordMessage(string webhookUrl, string jsonPayload)
        {
            if (string.IsNullOrWhiteSpace(webhookUrl) || string.IsNullOrWhiteSpace(jsonPayload))
                return;

            lock (_discordQueueLock)
            {
                _discordQueue.Enqueue(new DiscordMessage { WebhookUrl = webhookUrl, Payload = jsonPayload });

                // start queue worker if not already running
                if (_discordQueueTimer == null)
                    _discordQueueTimer = timer.Every(DiscordIntervalSeconds, ProcessDiscordQueue);
            }
        }

        // Worker: processes one message (FIFO) per tick
        private void ProcessDiscordQueue()
        {
            if (_discordSendInProgress)
                return; // still waiting on a send to complete

            if (_discordRetryAfterMs > 0)
            {
                _discordRetryAfterMs -= (int)(DiscordIntervalSeconds * 1000);
                if (_discordRetryAfterMs > 0)
                    return; // still cooling down
                _discordRetryAfterMs = 0;
            }

            DiscordMessage item = null;
            lock (_discordQueueLock)
            {
                if (_discordQueue.Count == 0)
                {
                    _discordQueueTimer?.Destroy();
                    _discordQueueTimer = null;
                    return;
                }
                item = _discordQueue.Peek();
            }

            if (item == null || string.IsNullOrWhiteSpace(item.WebhookUrl))
            {
                lock (_discordQueueLock)
                {
                    if (_discordQueue.Count > 0)
                        _discordQueue.Dequeue(); // drop invalid
                }
                return;
            }

            _discordSendInProgress = true;

            webrequest.Enqueue(
                item.WebhookUrl,
                item.Payload,
                (code, response) =>
                {
                    try
                    {
                        if (code == 200 || code == 204)
                        {
                            // success: let worker remove next tick
                            lock (_discordQueueLock)
                                if (_discordQueue.Count > 0) _discordQueue.Dequeue();
                        }
                        else if (code == 429)
                        {
                            int retryAfterMs = 5000; // safe fallback
                            try
                            {
                                var obj = JsonConvert.DeserializeObject<Dictionary<string, object>>(response);
                                if (obj != null && obj.TryGetValue("retry_after", out var retryVal))
                                    if (int.TryParse(retryVal.ToString(), out var parsed))
                                        retryAfterMs = parsed;
                            }
                            catch { /* swallow */ }

                            PrintWarning($"[Discord] Rate limited, retrying in {retryAfterMs} ms");
                            _discordRetryAfterMs = retryAfterMs;
                            // don’t dequeue, worker will retry same item
                        }
                        else if (code >= 500)
                        {
                            // Discord server error → transient → keep in queue for retry
                            PrintWarning($"[Discord] Server error {code}, will retry");
                        }
                        else
                        {
                            // Client error (bad webhook, unauthorized, etc.) → permanent → drop
                            PrintWarning($"[Discord] Permanent failure ({code}), dropping message");
                            lock (_discordQueueLock)
                                if (_discordQueue.Count > 0) _discordQueue.Dequeue();
                        }
                    }
                    finally { _discordSendInProgress = false; }
                }, this, Oxide.Core.Libraries.RequestMethod.POST,
                new Dictionary<string, string> { ["Content-Type"] = "application/json" }
            );
        }

        #endregion
    }
}
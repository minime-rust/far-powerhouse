using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("FAR Buyable Events", "miniMe", "1.1.0")]
    [Description("Make Patrol Chopper and Launchsite Bradley buyable with configured item")]
    public class FARBuyableEvents : RustPlugin
    {
        [PluginReference] private Plugin FARLogger, MonumentFinder;

        class ConfigData
        {
            public bool ServerRestartsDaily = false;
            public bool BradleyBuyableDuringEndGame = false;
            public string EventBuyableWith = "supply.signal";
            public float PatrolSpawnDistance = 1000f;
            public float PatrolSpawnHeight = 120f;
            public int HeliCrateLockMinutes = 10;
            public int DataCleanupMinutes = 30;
            public string DiscordWebhook = string.Empty;
            public DayOfWeek WipeDayOfWeek = DayOfWeek.Thursday;
            public int WipeHourOfDay = 19;
            public string TimeZoneId = "Europe/London";
            public double EndgameHoursBeforeWipe = 24;
        }
        private ConfigData cfg;

        Timer cleanupTimer;
        Timer endgameTimer;
        bool isEndgame = false;

        #region Oxide hooks & config
        protected override void LoadDefaultConfig()
        {
            cfg = new ConfigData();
            Config.WriteObject(cfg, true);
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                cfg = Config.ReadObject<ConfigData>();
                if (cfg == null) throw new Exception();
            }
            catch
            {
                LoadDefaultConfig();
            }
        }

        void Init()
        {
            lang.RegisterMessages(Messages, this);
            EvaluateEndgame();

            // Clean Patrol Helicopter damage tracking every ... minutes
            if (cfg != null && cfg.DataCleanupMinutes > 0)
                cleanupTimer = timer.Every(cfg.DataCleanupMinutes * 60f, CleanupDictionaries);

            // Console summary
            Puts($"Config → AssumeDailyRestart: {cfg.ServerRestartsDaily}," +
                 $"\nBradleyBuyableDuringEndgame: {cfg.BradleyBuyableDuringEndGame}, " +
                 $"isEndgame: {isEndgame}," +
                 $"\nEventBuyableWith: '{cfg.EventBuyableWith}', " +
                 $"\nPatrolSpawnDistance/Height: {cfg.PatrolSpawnDistance}m/{cfg.PatrolSpawnHeight}m," +
                 $"\nDiscordWebhookConfigured: {(string.IsNullOrWhiteSpace(cfg.DiscordWebhook) ? "No" : "Yes")}");
        }

        void Unload()
        {
            cleanupTimer?.Destroy();
            cleanupTimer = null;

            endgameTimer?.Destroy();
            endgameTimer = null;
        }
        #endregion

        #region Lang helper
        // simple localization helper
        string L(string key, BasePlayer player = null, params object[] args)
            => string.Format(lang.GetMessage(key, this, player?.UserIDString), args);
        readonly Dictionary<string, string> Messages = new Dictionary<string, string>
        {
            {"NoBradleyEndgame", "No Bradley during end game. Enough Bradleys on the roads, go and get yours!"},
            {"NoSupply", "No {0} in your inventory? Oops, you might have forgotten something!"},
            {"NotInMonument", "Not at Launch Site? Go to Launch Site and try again!"},
            {"NoPatrolExists", "There is already a Patrol Chopper on the map. Try again later!"},
            {"PatrolSpawnFail", "Spawning Patrol Chopper failed; no payment was taken."},
            {"PatrolSpawnSuccess", "Patrol Chopper called to your position — 1 {0} consumed. Good luck!"},
            {"HeliWinner", "{0} dealt the most damage to the Patrol Helicopter. Only {0} (and team) can loot the heli crates for the next {1} minutes."},
            {"HeliUnlock", "{0} minutes passed, heli crates can now be looted by everyone."},
            {"HeliNotYourCrate", "This crate is locked!"},
            {"BradleySpawnFail", "Spawning Bradley failed; no payment was taken."},
            {"BradleySpawnSuccess", "Launch Site Bradley called — 1 {0} consumed. Good luck!"},
            {"BradleyExists", "A Bradley already exists at this monument."},
            {"NoMonumentFinder", "MonumentFinder plugin not available."}
        };
        #endregion

        #region Chat commands
        [ChatCommand("buyheli")]
        void CmdBuyHeli(BasePlayer player, string cmd, string[] args)
        {
            if (player == null) return;

            if (HasPatrolChopper())
            {
                player.ChatMessage(L("NoPatrolExists", player));
                return;
            }

            if (!PlayerCanPay(player))
            {
                player.ChatMessage(L("NoSupply", player, cfg?.EventBuyableWith ?? string.Empty));
                return;
            }

            // attempt spawn
            SpawnPatrolChopperAtPlayer(player);

            // verify spawn shortly after attempting, only take payment on success
            timer.Once(2f, () =>
            {
                if (HasPatrolChopper())
                {
                    if (TryConsumePayment(player))
                    {
                        player.ChatMessage(L("PatrolSpawnSuccess", player, cfg?.EventBuyableWith));
                        Server.Broadcast($"{player.displayName} has paid for the Patrol Helicopter Event!");
                        // notify Discord if configured (filter checks exist in SendDiscordMessage, keep it stupid here)
                        var message = $":dagger: {GetDiscordTimestamp()} `{player.displayName}` has paid for `Patrol Chopper`";
                        SendDiscordMessage(cfg?.DiscordWebhook, message);
                    }
                }
                else
                    player.ChatMessage(L("PatrolSpawnFail", player));
            });
        }

        void SpawnPatrolChopperAtPlayer(BasePlayer player)
        {
            if (player == null) return;

            const string heliPrefab = "assets/prefabs/npc/patrol helicopter/patrolhelicopter.prefab";

            float angle = UnityEngine.Random.Range(0f, 360f); // Random horizontal angle
            float distance = Mathf.Clamp(cfg.PatrolSpawnDistance, 100f, 1500f); // reasonable distance
            float height = Mathf.Clamp(cfg.PatrolSpawnHeight, 50f, 150f); // reasonable ceiling

            // Calculate spawn position
            Vector3 dir = new Vector3(Mathf.Cos(angle * Mathf.Deg2Rad), 0f, Mathf.Sin(angle * Mathf.Deg2Rad));
            Vector3 spawnPos = player.transform.position + dir.normalized * distance;

            // Raycast from high above the candidate position straight down to find terrain/water
            float rayOriginHeight = 5000f;
            Vector3 rayStart = new Vector3(spawnPos.x, rayOriginHeight, spawnPos.z);
            if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, rayOriginHeight + 100f, LayerMask.GetMask("World")))
                // place heli at configured height above ground
                spawnPos.y = hit.point.y + height;
            else
                // fallback if no ground hit (e.g. on map edge) → relative to player
                spawnPos.y = player.transform.position.y + height;

            BaseEntity heliEnt = GameManager.server.CreateEntity(heliPrefab, spawnPos, Quaternion.identity, true);
            if (heliEnt == null) return;

            heliEnt.Spawn();

            // Make it target the player
            var heliAI = heliEnt.GetComponent<PatrolHelicopterAI>();
            if (heliAI != null && player != null)
                heliAI.SetInitialDestination(player.transform.position);
        }

        [ChatCommand("buybradley")]
        void CmdBuyBradley(BasePlayer player, string cmd, string[] args)
        {
            if (player == null) return;

            if (isEndgame)
            {
                player.ChatMessage(L("NoBradleyEndgame", player));
                return;
            }

            if (MonumentFinder == null)
            {
                player.ChatMessage(L("NoMonumentFinder", player));
                return;
            }

            var mon = GetClosestMonument(player.transform.position);
            if (mon == null || !mon.IsInBounds(player.transform.position) || !IsLaunchsite(mon))
            {
                player.ChatMessage(L("NotInMonument", player));
                return;
            }

            if (BradleyExistsInMonument(mon))
            {
                player.ChatMessage(L("BradleyExists", player));
                return;
            }

            if (!PlayerCanPay(player))
            {
                player.ChatMessage(L("NoSupply", player, cfg?.EventBuyableWith ?? string.Empty));
                return;
            }

            // attempt spawn (default command; server owner can change plugin or adjust code if needed)
            TrySpawnBradleyAdminless();

            // verify and only charge on success
            timer.Once(2f, () =>
            {
                if (BradleyExistsInMonument(mon))
                {
                    if (TryConsumePayment(player))
                    {
                        player.ChatMessage(L("BradleySpawnSuccess", player, cfg?.EventBuyableWith));
                        Server.Broadcast($"{player.displayName} has paid for the Launch Site Bradley Event!");
                        // notify Discord if configured (filter checks exist in SendDiscordMessage, keep it stupid here)
                        var message = $":dagger: {GetDiscordTimestamp()} `{player.displayName}` has paid for `Launch Site Bradley`";
                        SendDiscordMessage(cfg?.DiscordWebhook, message);
                    }
                }
                else
                    player.ChatMessage(L("BradleySpawnFail", player));
            });
        }

        // Triggers the Launch Site Bradley via the native spawner, avoiding admin-only console paths.
        void TrySpawnBradleyAdminless()
        {
            var spawner = UnityEngine.Object.FindObjectOfType<BradleySpawner>();
            if (spawner == null) return;

            // Unity Invoke avoids access modifier differences across builds (public/private/internal).
            spawner.Invoke("SpawnBradley", 0f);
        }

        #endregion

        #region Endgame evaluation (lean)
        // Evaluates "endgame" strictly from config day/hour/timezone,
        // while the cadence (weekly/biweekly/monthly) is inferred from server.tags.
        void EvaluateEndgame()
        {
            Puts($"BradleyBuyableDuringEndGame = {cfg?.BradleyBuyableDuringEndGame}");
            // If Bradley is allowed during endgame, we can skip the flag altogether.
            if (cfg?.BradleyBuyableDuringEndGame ?? false) return;

            endgameTimer?.Destroy();
            endgameTimer = null;

            int wipeHour = Mathf.Clamp(cfg?.WipeHourOfDay ?? 19, 0, 23);
            string tz = string.IsNullOrWhiteSpace(cfg?.TimeZoneId) ? "GMT Standard Time" : cfg.TimeZoneId;
            double eventHours = Math.Max(0, cfg?.EndgameHoursBeforeWipe ?? 24);
            DayOfWeek wipeDay = cfg?.WipeDayOfWeek ?? DayOfWeek.Thursday;

            // Resolve timezone (fallback keeps it lean)
            TimeZoneInfo zone;
            try { zone = TimeZoneInfo.FindSystemTimeZoneById(tz); }
            catch { zone = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time"); }

            // "now" in server wipe timezone
            var now = TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, zone);

            // Cadence comes from server tags (do not remove)
            var tags = (ConVar.Server.tags ?? string.Empty).ToLowerInvariant();
            string mode = tags.Contains("biweekly") ? "biweekly"
                        : tags.Contains("weekly") ? "weekly"
                        : "monthly";

            // Compute next wipe with month-anchored definitions
            var nextWipe = GetNextWipe(now, wipeDay, wipeHour, mode, zone);

            var endgameStart = nextWipe.AddHours(-eventHours);
            isEndgame = now >= endgameStart && now < nextWipe;

            // Optional: set a one-shot to flip into endgame after daily restart (kept from your version)
            double hoursLeft = (nextWipe - now).TotalHours;
            Puts($"isEndgame = {isEndgame} | hoursleft = {(nextWipe - now).TotalHours}");
            if (cfg.ServerRestartsDaily && hoursLeft > eventHours && hoursLeft < 48)
            {
                double fireInSeconds = (hoursLeft - eventHours) * 3600.0;
                endgameTimer = timer.Once((float)Math.Max(1, fireInSeconds), () => isEndgame = true);
            }
        }

        // Definitions (exact):
        // - weekly   = every occurrence of cfg.WipeDayOfWeek in a month
        // - biweekly = 1st, 3rd, 5th occurrence of cfg.WipeDayOfWeek in a month
        // - monthly  = 1st occurrence of cfg.WipeDayOfWeek in a month
        DateTimeOffset GetNextWipe(DateTimeOffset now, DayOfWeek target, int hour, string mode, TimeZoneInfo zone)
        {
            // Create a DateTimeOffset at the given local date/hour with the proper zone offset (DST-aware)
            DateTimeOffset AtZone(int year, int month, int day, int h)
            {
                var local = new DateTime(year, month, day, h, 0, 0, DateTimeKind.Unspecified);
                var offset = zone.GetUtcOffset(local);
                return new DateTimeOffset(local, offset);
            }

            // Nth (1-based) occurrence of "day" in the given month; MinValue if it doesn't exist (e.g. 5th may not exist)
            DateTimeOffset NthWeekday(int year, int month, DayOfWeek day, int n, int h)
            {
                var first = new DateTime(year, month, 1, h, 0, 0, DateTimeKind.Unspecified);
                int diff = ((int)day - (int)first.DayOfWeek + 7) % 7;
                int dayNum = 1 + diff + 7 * (n - 1);
                int daysInMonth = DateTime.DaysInMonth(year, month);
                if (dayNum > daysInMonth) return DateTimeOffset.MinValue;
                return AtZone(year, month, dayNum, h);
            }

            // WEEKLY: next occurrence of target DOW at "hour", irrespective of month boundary
            if (mode == "weekly")
            {
                var baseLocal = new DateTime(now.Year, now.Month, now.Day, hour, 0, 0, DateTimeKind.Unspecified);
                var candidate = new DateTimeOffset(baseLocal, zone.GetUtcOffset(baseLocal));
                int daysAhead = ((int)target - (int)candidate.DayOfWeek + 7) % 7;
                candidate = candidate.AddDays(daysAhead);
                if (candidate <= now) candidate = candidate.AddDays(7);
                return candidate;
            }

            // BIWEEKLY: next among 1st, 3rd, 5th occurrences this month; else next month 1st
            if (mode == "biweekly")
            {
                var cands = new[]
                {
                    NthWeekday(now.Year, now.Month, target, 1, hour),
                    NthWeekday(now.Year, now.Month, target, 3, hour),
                    NthWeekday(now.Year, now.Month, target, 5, hour),
                }
                .Where(dt => dt != DateTimeOffset.MinValue && dt > now)
                .OrderBy(dt => dt)
                .ToArray();

                if (cands.Length > 0) return cands[0];

                // None left this month → next month 1st occurrence
                var nm = now.AddMonths(1);
                return NthWeekday(nm.Year, nm.Month, target, 1, hour);
            }

            // MONTHLY: 1st occurrence this month; if already passed, next month 1st
            var thisMonthFirst = NthWeekday(now.Year, now.Month, target, 1, hour);
            if (thisMonthFirst > now) return thisMonthFirst;

            var nextMonth = now.AddMonths(1);
            return NthWeekday(nextMonth.Year, nextMonth.Month, target, 1, hour);
        }
        #endregion

        #region Tracking Heli Spawns & Damage
        private readonly Dictionary<PatrolHelicopterAI, Dictionary<ulong, float>> heliDamage = new();
        private readonly Dictionary<ulong, (LootContainer crate, ulong winner)> trackedCrates = new();

        // Small queue of recently dead heli winners
        private readonly Queue<ulong> recentDeadHeliWinners = new();
        private const float RecentWinnerTTL = 2f; // seconds

        // Loot prevention
        object CanLootEntity(BasePlayer player, BaseEntity target)
        {
            if (player == null || target == null) return null;

            if (target is LockedByEntCrate crate && crate.ShortPrefabName == "heli_crate")
            {
                if (!trackedCrates.TryGetValue(crate.net.ID.Value, out var tracked))
                    return null; // not tracked → allow loot

                var winnerId = tracked.winner;

                if (player.userID == winnerId) return null; // winner himself

                // Get winner team via Covalence
                var winnerPlayer = BasePlayer.FindByID(winnerId);
                if (winnerPlayer != null && winnerPlayer.currentTeam != 0)
                    if (player.currentTeam == winnerPlayer.currentTeam) return null; // teammate

                // Otherwise block loot
                player.ChatMessage(L("HeliNotYourCrate"));
                return false;
            }

            return null;
        }

        // Track damage
        void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (info?.InitiatorPlayer == null || entity == null) return;

            var heli = entity.GetComponent<PatrolHelicopterAI>();
            if (heli == null) return;

            var id = info.InitiatorPlayer.userID;

            if (!heliDamage.TryGetValue(heli, out var dmgDict))
                heliDamage[heli] = dmgDict = new Dictionary<ulong, float>();

            dmgDict[id] = dmgDict.TryGetValue(id, out var dmg)
                        ? dmg + info.damageTypes.Total()
                        : info.damageTypes.Total();
        }

        // Handle heli death
        void OnEntityDeath(BaseCombatEntity entity, HitInfo info)
        {
            var heli = entity.GetComponent<PatrolHelicopterAI>();
            if (heli == null) return;

            if (!heliDamage.TryGetValue(heli, out var dmgDict) || dmgDict.Count == 0) return;

            var winner = dmgDict.OrderByDescending(kvp => kvp.Value).First().Key;
            heliDamage.Remove(heli); // remove dead heli immediately

            var winnerPlayer = BasePlayer.FindByID(winner);
            var minutes = cfg.HeliCrateLockMinutes;

            if (winnerPlayer != null)
                Server.Broadcast(L("HeliWinner", null, winnerPlayer.displayName, minutes));

            // Add winner to recent dead queue for crate assignment
            recentDeadHeliWinners.Enqueue(winner);
            timer.Once(RecentWinnerTTL, () =>
            {
                if (recentDeadHeliWinners.Count > 0 && recentDeadHeliWinners.Peek() == winner)
                    recentDeadHeliWinners.Dequeue();
            });

            // Schedule crate unlock
            timer.Once(minutes * 60f, () =>
            {
                var cratesToUnlock = trackedCrates
                    .Where(kvp => kvp.Value.winner == winner && kvp.Value.crate != null && !kvp.Value.crate.IsDestroyed)
                    .Select(kvp => kvp.Value.crate)
                    .ToList();

                // Remove tracked crates
                foreach (var crate in cratesToUnlock)
                    trackedCrates.Remove(crate.net.ID.Value);

                // Only broadcast if there were any valid crates
                if (cratesToUnlock.Count > 0)
                    Server.Broadcast(L("HeliUnlock", null, minutes));
            });
        }

        // Assign winner when crate spawns
        void OnEntitySpawned(BaseNetworkable entity)
        {
            if (entity == null) return;

            // Check for Patrol Helicopter
            var heli = entity.GetComponent<PatrolHelicopterAI>();
            if (heli != null)
            {
                heliDamage[heli] = new Dictionary<ulong, float>();
                return;
            }

            // Check for heli crate
            if (entity is LootContainer crate && crate.PrefabName == "assets/prefabs/npc/patrol helicopter/heli_crate.prefab")
            {
                // Assign the most recent dead heli winner
                var winner = recentDeadHeliWinners.Count > 0 ? recentDeadHeliWinners.Peek() : 0UL;

                // Track the crate with its winner
                trackedCrates[crate.net.ID.Value] = (crate, winner);
            }
        }


        // Cleanup destroyed crates
        private void CleanupDictionaries()
        {
            var goneCrates = trackedCrates
                .Where(kvp => kvp.Value.crate == null || kvp.Value.crate.IsDestroyed)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var id in goneCrates)
                trackedCrates.Remove(id);
        }
        #endregion

        #region Helpers: MonumentFinder adapter & entity checks
        MonumentAdapter GetClosestMonument(Vector3 pos)
        {
            var dict = MonumentFinder?.Call("API_GetClosest", pos) as Dictionary<string, object>;
            return dict != null ? new MonumentAdapter(dict) : null;
        }

        bool IsLaunchsite(MonumentAdapter mon)
        {
            if (mon == null) return false;
            return (mon.Alias ?? mon.ShortName ?? mon.PrefabName ?? string.Empty)
                .ToLowerInvariant()
                .Contains("launch");
        }

        bool BradleyExistsInMonument(MonumentAdapter mon)
        {
            if (mon == null) return false;
            const string bradleyPrefab = "assets/prefabs/npc/m2bradley/bradleyapc.prefab";

            foreach (var net in BaseNetworkable.serverEntities)
            {
                if (net is BaseEntity ent && ent.PrefabName == bradleyPrefab && !ent.IsDestroyed)
                {
                    try { if (mon.IsInBounds(ent.transform.position)) return true; }
                    catch { /* ignore malformed monument callbacks */ }
                }
            }
            return false;
        }

        bool HasPatrolChopper()
        {
            const string heliPrefab = "assets/prefabs/npc/patrol helicopter/patrolhelicopter.prefab";

            foreach (var net in BaseNetworkable.serverEntities)
            {
                if (net is BaseEntity ent && ent.PrefabName == heliPrefab && !ent.IsDestroyed)
                    return true;
            }
            return false;
        }
        #endregion

        #region Helpers: inventory / payment
        bool PlayerCanPay(BasePlayer player)
        {
            var item = cfg?.EventBuyableWith ?? string.Empty;
            var def = ItemManager.FindItemDefinition(item);
            if (def == null) return false;

            var containers = new[] { player.inventory.containerBelt, player.inventory.containerMain, player.inventory.containerWear };
            foreach (var c in containers)
            {
                if (c == null) continue;
                foreach (var it in c.itemList)
                {
                    if (it == null) continue;
                    if (it.info == def && it.amount > 0) return true;
                }
            }
            return false;
        }

        private void SendDiscordMessage(string webhookUrl, string message)
        {
            if (string.IsNullOrWhiteSpace(webhookUrl) || string.IsNullOrWhiteSpace(message))
                return; // nothing to send

            // Discord has a strict 2000-character limit
            const int maxLen = 2000;
            if (message.Length > maxLen)
                message = message.Substring(0, maxLen - 3) + "...";

            // #####################################################
            // Try to hand off to FAR Logger if available
            var result = FARLogger?.Call("API_SendDiscordMessage", webhookUrl, message);

            if (result is bool ok && ok)
                return; // Hand-off succeeded, FAR Logger took it

            // #####################################################
            // FAR Logger not present, or refused → fallback to direct webrequest

            // Escape safely by letting JSON serializer handle quotes, slashes, etc.
            var payload = JsonConvert.SerializeObject(new { content = message });

            webrequest.Enqueue(
                webhookUrl,
                payload,
                (code, response) =>
                {
                    if (code != 200 && code != 204)
                        PrintWarning($"[Discord] Failed ({code}): {response}");
                },
                this,
                Oxide.Core.Libraries.RequestMethod.POST,
                new Dictionary<string, string> { ["Content-Type"] = "application/json" }
            );
        }

        private string GetDiscordTimestamp()
        {
            DateTime utcNow = DateTime.UtcNow;
            DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            TimeSpan timeSpan = utcNow - epoch;
            long unixTimeSeconds = (long)timeSpan.TotalSeconds;
            return $"<t:{unixTimeSeconds}:t>";
        }

        bool TryConsumePayment(BasePlayer player)
        {
            var item = cfg?.EventBuyableWith ?? string.Empty;
            var def = ItemManager.FindItemDefinition(item);
            if (def == null) return false;

            var containers = new[] { player.inventory.containerBelt, player.inventory.containerMain, player.inventory.containerWear };
            foreach (var c in containers)
            {
                if (c == null) continue;
                // copy to avoid collection modification issues
                for (int i = 0; i < c.itemList.Count; i++)
                {
                    var it = c.itemList[i];
                    if (it == null) continue;
                    if (it.info == def && it.amount > 0)
                    {
                        // use one unit
                        try
                        {
                            it.UseItem(1);
                        }
                        catch
                        {
                            // fallback: decrement/remove
                            it.amount -= 1;
                            if (it.amount <= 0) it.RemoveFromContainer();
                        }
                        player.inventory.ServerUpdate(0f);
                        return true;
                    }
                }
            }
            return false;
        }
        #endregion

        #region MonumentAdapter (minimal wrapper)
        private sealed class MonumentAdapter
        {
            private readonly Dictionary<string, object> _monumentInfo;

            public MonumentAdapter(Dictionary<string, object> monumentInfo) =>
                _monumentInfo = monumentInfo;

            public string Alias => _monumentInfo.TryGetValue("Alias", out var v) ? v as string : null;
            public string ShortName => _monumentInfo.TryGetValue("ShortName", out var v) ? v as string : null;
            public string PrefabName => _monumentInfo.TryGetValue("PrefabName", out var v) ? v as string : null;

            public bool IsInBounds(Vector3 position)
            {
                if (_monumentInfo.TryGetValue("IsInBounds", out var v) && v is Func<Vector3, bool> del)
                    return del(position);
                return false;
            }
        }
        #endregion
    }
}
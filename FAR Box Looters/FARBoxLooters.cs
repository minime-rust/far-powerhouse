using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Oxide.Core;
using Oxide.Core.Database;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Core.SQLite.Libraries;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("FAR: Box Looters", "miniMe", "1.1.5")]
    [Description("Logs container accesses and item changes into SQLite. Minimal, self-contained, Oxide/Carbon neutral.")]
    public class FARBoxLooters : RustPlugin
    {
        // --- config / constants
        private const int ChatLineLimit = 15;
        private const string DbFileName = "FARBoxLooters.sqlite";
        private const float FlushInterval = 60f; // seconds

        // --- Oxide SQLite
        private Connection _conn;
        private readonly SQLite _sqlite = Interface.Oxide.GetLibrary<SQLite>();
        private string DbPath => Path.Combine(Interface.Oxide.DataDirectory, DbFileName);

        // --- write queue
        private readonly object _queueLock = new object();
        private readonly object _pendingLock = new object();
        private readonly List<Action> _writeQueue = new List<Action>();

        #region Oxide lifecycle
        private void Init()
        {
            EnsureDb();
            OpenConnection();
            CreateTables(); // creates tables + indexes

            cmd.AddChatCommand("box", this, "CmdBoxChat");
            cmd.AddConsoleCommand("box", this, "CmdBoxConsole");

            timer.Every(FlushInterval, TryFlushQueued);
        }

        private void Unload()
        {
            TryFlushQueued(true);
            CloseConnection();
        }
        #endregion

        #region Database setup
        private void EnsureDb()
        {
            if (!Directory.Exists(Interface.Oxide.DataDirectory))
                Directory.CreateDirectory(Interface.Oxide.DataDirectory);
            if (!File.Exists(DbPath))
                File.Create(DbPath).Dispose();
        }

        private void OpenConnection()
        {
            CloseConnection();
            _conn = _sqlite.OpenDb(DbPath, this);
        }

        private void CloseConnection()
        {
            try { if (_conn != null) { _sqlite.CloseDb(_conn); _conn = null; } }
            catch { /* swallow on unload */ }
        }

        private void CreateTables()
        {
            // keep schema exactly as you requested
            var sql = Sql.Builder;
            sql.Append(@"CREATE TABLE IF NOT EXISTS boxes (
              netid INTEGER PRIMARY KEY,
              prefab TEXT,
              x REAL, y REAL, z REAL,
              owner INTEGER,
              destroyed_at TEXT,
              pickedup_at TEXT
            );");
            _sqlite.ExecuteNonQuery(sql, _conn);

            sql = Sql.Builder;
            sql.Append(@"CREATE TABLE IF NOT EXISTS loots (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              netid INTEGER,
              looter_steamid INTEGER,
              looter_name TEXT,
              time TEXT,
              auth_on_tc INTEGER,
              teammate INTEGER
            );");
            _sqlite.ExecuteNonQuery(sql, _conn);

            sql = Sql.Builder;
            sql.Append(@"CREATE TABLE IF NOT EXISTS loot_items (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              loot_id INTEGER,
              shortname TEXT,
              qty_change INTEGER
            );");
            _sqlite.ExecuteNonQuery(sql, _conn);

            // Add indexes that help read performance; no schema semantics changed
            // These are lightweight and safe to run even if they already exist.
            sql = Sql.Builder;
            sql.Append("CREATE INDEX IF NOT EXISTS idx_loots_netid ON loots(netid);");
            _sqlite.ExecuteNonQuery(sql, _conn);

            sql = Sql.Builder;
            sql.Append("CREATE INDEX IF NOT EXISTS idx_loots_looter ON loots(looter_steamid);");
            _sqlite.ExecuteNonQuery(sql, _conn);

            sql = Sql.Builder;
            sql.Append("CREATE INDEX IF NOT EXISTS idx_loot_items_lootid ON loot_items(loot_id);");
            _sqlite.ExecuteNonQuery(sql, _conn);

            sql = Sql.Builder;
            sql.Append("CREATE INDEX IF NOT EXISTS idx_loots_looter_netid ON loots(looter_steamid, netid);");
            _sqlite.ExecuteNonQuery(sql, _conn);
        }
        #endregion

        #region Recording logic (queued)
        private void EnqueueWrite(Action a) { lock (_queueLock) _writeQueue.Add(a); }

        private void TryFlushQueued() => TryFlushQueued(false);
        private void TryFlushQueued(bool force)
        {
            List<Action> batch = null;
            lock (_queueLock)
            {
                if (!force && _writeQueue.Count == 0) return;
                batch = new List<Action>(_writeQueue);
                _writeQueue.Clear();
            }

            try { foreach (var a in batch) a(); }
            catch (Exception ex)
            {
                PrintError($"[BoxLooters] Flush failed: {ex}");
                lock (_queueLock) _writeQueue.InsertRange(0, batch);
            }
        }
        #endregion

        #region Helpers to queue SQL statements (writes preserved)
        private void InsertOrUpdateBoxQueued(BaseEntity ent)
        {
            if (ent == null) return;
            // only store real storage containers (exclude ephemeral LootContainer subclasses)
            if (!(ent is StorageContainer) || (ent is LootContainer)) return;

            var net = ent.net?.ID ?? default(NetworkableId);
            if (!net.IsValid) return;
            ulong netId = net.Value;

            var prefab = ent.ShortPrefabName ?? ent.PrefabName ?? "unknown";
            var pos = ent.transform.position;
            ulong ownerId = (ent as StorageContainer)?.OwnerID ?? 0;

            EnqueueWrite(() =>
            {
                var sql = Sql.Builder.Append(
                    "INSERT INTO boxes (netid,prefab,x,y,z,owner) VALUES (@0,@1,@2,@3,@4,@5) " +
                    "ON CONFLICT(netid) DO UPDATE SET prefab=@1,x=@2,y=@3,z=@4,owner=@5;",
                    (long)netId, prefab, pos.x, pos.y, pos.z, (long)unchecked(ownerId)
                );
                _sqlite.ExecuteNonQuery(sql, _conn);
            });
        }

        private readonly Dictionary<ulong, long> lastLootForContainer = new Dictionary<ulong, long>();

        private void RecordLootQueued(BasePlayer player, BaseEntity ent)
        {
            if (player == null || ent == null) return;
            if (!(ent is StorageContainer) || (ent is LootContainer)) return;

            var net = ent.net?.ID ?? default(NetworkableId);
            if (!net.IsValid) return;
            ulong netId = net.Value;

            InsertOrUpdateBoxQueued(ent);

            int authedOnTC = player.IsBuildingAuthed(ent.GetBuildingPrivilege()) ? 1 : 0;
            ulong ownerId = (ent as StorageContainer)?.OwnerID ?? 0;
            var team = RelationshipManager.ServerInstance?.FindPlayersTeam(player.userID);
            int teammate = (team?.members != null && ownerId != 0 && team.members.Contains(ownerId)) ? 1 : 0;

            var time = DateTime.UtcNow.ToString("o");

            EnqueueWrite(() =>
            {
                var insertLoot = Sql.Builder.Append(
                    "INSERT INTO loots (netid,looter_steamid,looter_name,time,auth_on_tc,teammate) VALUES (@0,@1,@2,@3,@4,@5);",
                    Convert.ToInt64(netId), Convert.ToInt64(player.userID), player.displayName, time, authedOnTC, teammate
                );
                _sqlite.ExecuteNonQuery(insertLoot, _conn);

                // Capture last inserted row id immediately
                var rowIdSql = Sql.Builder.Append("SELECT last_insert_rowid() AS lastId;");
                _sqlite.Query(rowIdSql, _conn, results =>
                {
                    if (results != null && results.Count > 0)
                    {
                        long lootId = Convert.ToInt64(results[0]["lastId"]);
                        if (lootId > 0)
                        {
                            lock (_pendingLock)
                            {
                                lastLootForContainer[netId] = lootId;

                                // Flush and remove pending items under the same lock
                                if (pendingItems.TryGetValue(netId, out var list))
                                {
                                    // Build one SQL batch (BEGIN/INSERT/INSERT/.../COMMIT) to reduce write pressure.
                                    // Use simple escaping for shortnames (single quote escape).
                                    var sb = new System.Text.StringBuilder();
                                    sb.Append("BEGIN TRANSACTION;");
                                    foreach (var (sn, sign) in list)
                                    {
                                        // escape any single quotes in the shortname
                                        var esc = (sn ?? "").Replace("'", "''");
                                        sb.AppendFormat("INSERT INTO loot_items (loot_id,shortname,qty_change) VALUES ({0},'{1}',{2});", lootId, esc, sign);
                                    }
                                    sb.Append("COMMIT;");

                                    var batchSqlText = sb.ToString();
                                    EnqueueWrite(() =>
                                    {
                                        var sqlBatch = Sql.Builder.Append(batchSqlText);
                                        _sqlite.ExecuteNonQuery(sqlBatch, _conn);
                                    });

                                    pendingItems.Remove(netId);
                                }
                            }
                        }
                    }
                });
            });

        }

        // pending items keyed by netid
        // we only need the item shortname and a sign (+1 for add, -1 for remove)
        private readonly Dictionary<ulong, List<(string shortname, int sign)>> pendingItems = new();

        private void RecordItemChangeQueued(ulong containerNet, string shortname, int sign)
        {
            // sign is +1 for an add, -1 for a removal
            lock (_pendingLock)
            {
                if (lastLootForContainer.TryGetValue(containerNet, out var lootId) && lootId > 0)
                {
                    // We already know the lootId → write immediately (single insert).
                    // We keep this simple and fast; pending cases are batched when the loot row is created.
                    EnqueueWrite(() =>
                    {
                        var sql = Sql.Builder.Append(
                            "INSERT INTO loot_items (loot_id,shortname,qty_change) VALUES (@0,@1,@2);",
                            lootId, shortname, sign
                        );
                        _sqlite.ExecuteNonQuery(sql, _conn);
                    });
                }
                else
                {
                    // LootId not yet known → stash into pendingItems (shortname + sign)
                    if (!pendingItems.TryGetValue(containerNet, out var list))
                    {
                        list = new List<(string shortname, int sign)>();
                        pendingItems[containerNet] = list;
                    }
                    list.Add((shortname, sign));
                }
            }
        }

        #endregion

        #region Hooks: entity events (writes preserved)

        private void OnServerSave()
        {
            TryFlushQueued();

            // small safety net, prevents stale entries
            lock (_pendingLock)
            {
                lastLootForContainer.Clear();
            }

            // Emit a single-line health/diag summary to the server console so admins can
            // quickly monitor plugin health without multiple chat lines.
            // This is intentionally light-weight: one formatted string per server save.
            int queuedCount;
            int pendingCount;
            int lastLootCount;
            int nameCacheCount;

            lock (_queueLock) queuedCount = _writeQueue.Count;
            lock (_pendingLock)
            {
                pendingCount = pendingItems.Count;
                lastLootCount = lastLootForContainer.Count;
            }
            nameCacheCount = _nameCache.Count;

            // Single-line message — cheap and easy to grep in server logs.
            Puts($"diag: queuedSql={queuedCount}, pendingItems={pendingCount}, lastLootEntries={lastLootCount}, nameCache={nameCacheCount}");
        }

        // Track who is actively looting a container
        private readonly Dictionary<ulong, BasePlayer> activeLooters = new();

        // When a player starts looting, remember it
        private void OnLootEntity(BasePlayer player, BaseEntity entity)
        {
            if (player == null || player.IsNpc) return;
            if (!(entity is StorageContainer sc) || (entity is LootContainer)) return;

            RecordLootQueued(player, entity);

            var net = sc.net?.ID ?? default(NetworkableId);
            if (!net.IsValid) return;
            ulong netId = net.Value;

            lock (_pendingLock)
            {
                activeLooters[netId] = player;
            }
        }

        // When they stop looting, remove them
        private void OnLootEntityEnd(BasePlayer player, BaseEntity entity)
        {
            if (entity is StorageContainer sc)
            {
                var net = sc.net?.ID ?? default(NetworkableId);
                if (!net.IsValid) return;
                ulong netId = net.Value;

                lock (_pendingLock)
                {
                    activeLooters.Remove(netId);
                }
            }
        }

        private void OnEntityKill(BaseNetworkable entity)
        {
            var be = entity as BaseEntity; if (be == null) return;
            if (!(be is StorageContainer) || (be is LootContainer)) return;

            var net = be.net?.ID ?? default(NetworkableId);
            if (!net.IsValid) return;
            ulong netId = net.Value;

            EnqueueWrite(() =>
            {
                var sql = Sql.Builder.Append(
                    "UPDATE boxes SET destroyed_at=@0 WHERE netid=@1;",
                    DateTime.UtcNow.ToString("o"),
                    (long)netId
                );
                _sqlite.ExecuteNonQuery(sql, _conn);
            });

            // Remove any cached last-loot reference for this container to avoid unbounded growth.
            lock (_pendingLock)
            {
                if (lastLootForContainer.ContainsKey(netId))
                    lastLootForContainer.Remove(netId);
                if (pendingItems.ContainsKey(netId))
                    pendingItems.Remove(netId);
            }
        }

        object OnItemPickup(Item item, BasePlayer player)
        {
            if (item == null || player == null) return null;

            var entity = item.GetWorldEntity() as BaseEntity ?? item.parent?.entityOwner as BaseEntity;
            if (entity == null) return null;

            var box = entity.GetComponent<StorageContainer>();
            if (box == null) return null;
            if (box is LootContainer) return null;

            var net = box.net?.ID ?? default(NetworkableId);
            if (!net.IsValid) return null;
            ulong netId = net.Value;

            EnqueueWrite(() =>
            {
                var sql = Sql.Builder.Append(
                    "UPDATE boxes SET pickedup_at=@0 WHERE netid=@1;",
                    DateTime.UtcNow.ToString("o"),
                    (long)netId
                );
                _sqlite.ExecuteNonQuery(sql, _conn);
            });

            return null;
        }

        // Updated item change hooks
        private void OnItemAddedToContainer(ItemContainer container, Item item)
        {
            try
            {
                var ent = container.entityOwner as BaseEntity; if (ent == null) return;
                var net = ent.net?.ID ?? default(NetworkableId); if (!net.IsValid) return;
                ulong netId = net.Value;

                BasePlayer looter;
                lock (_pendingLock)
                {   // only if player actively looting
                    if (!activeLooters.TryGetValue(netId, out looter)) return;
                }

                // We only record the event (added) as +1 — no counts
                RecordItemChangeQueued(netId, item.info.shortname, +1);
            }
            catch (Exception ex) { PrintError($"OnItemAddedToContainer error: {ex}"); }
        }

        private void OnItemRemovedFromContainer(ItemContainer container, Item item)
        {
            try
            {
                var ent = container.entityOwner as BaseEntity; if (ent == null) return;
                var net = ent.net?.ID ?? default(NetworkableId); if (!net.IsValid) return;
                ulong netId = net.Value;

                BasePlayer looter;
                lock (_pendingLock)
                {   // only if player actively looting
                    if (!activeLooters.TryGetValue(netId, out looter)) return;
                }

                // We only record the event (removed) as -1 — no counts
                RecordItemChangeQueued(netId, item.info.shortname, -1);
            }
            catch (Exception ex) { PrintError($"OnItemRemovedFromContainer error: {ex}"); }
        }
        #endregion

        #region Commands (async reads)
        private void CmdBoxChat(BasePlayer player, string command, string[] args)
        {
            if (player == null) return;
            if (player.net == null || player.net.connection == null || player.net.connection.authLevel < 1) return;
            // do not block: start async DB read and return
            HandleBoxCommandAsync(player, args);
        }

        [ConsoleCommand("box")]
        private void CmdBoxConsole(ConsoleSystem.Arg arg)
        {
            // If invoked by an in-game player via F1 console, arg.Player() returns the player.
            var player = arg.Player() as BasePlayer;

            // If called from the *server console*, player will be null. We still support
            // a few admin commands from server console (help, diag, clear).
            if (player == null)
            {
                // Handle a small safe subset from server console.
                var args = arg.Args ?? new string[0];
                if (args.Length == 0 || args[0].Equals("help", StringComparison.OrdinalIgnoreCase))
                {
                    Puts("box <command> - available commands: help, diag, clear");
                    Puts("  diag - print plugin diag info to server console");
                    Puts("  clear - wipe DB (server console only; no confirmation)");
                    return;
                }

                var cmd = args[0].ToLowerInvariant();
                switch (cmd)
                {
                    case "diag":
                        // Print diag summary (same values as OnServerSave prints).
                        int queuedCount;
                        int pendingCount;
                        int lastLootCount;
                        int nameCacheCount;
                        lock (_queueLock) queuedCount = _writeQueue.Count;
                        lock (_pendingLock)
                        {
                            pendingCount = pendingItems.Count;
                            lastLootCount = lastLootForContainer.Count;
                        }
                        nameCacheCount = _nameCache.Count;

                        Puts($"diag: queuedSql={queuedCount}, pendingItems={pendingCount}, lastLootEntries={lastLootCount}, nameCache={nameCacheCount}");
                        break;

                    case "clear":
                        // server console: perform immediate clear
                        CloseConnection();
                        if (File.Exists(DbPath)) File.Delete(DbPath);
                        EnsureDb();
                        OpenConnection();
                        CreateTables();
                        Puts("FAR:BoxLooters: database cleared and re-created.");
                        break;

                    default:
                        Puts($"Unknown console-box command '{cmd}'. Use 'help'.");
                        break;
                }
                return;
            }

            // Called by a connected player in F1 console. Keep previous permission checks.
            if (player.net == null || player.net.connection == null) return;
            if (player.net.connection.authLevel < 1) return;

            // Delegate to the same async handler used by chat command.
            HandleBoxCommandAsync(player, arg.Args);
        }

        // Entry point for interactive commands. This schedules DB queries and returns immediately.
        private void HandleBoxCommandAsync(BasePlayer requester, string[] args)
        {
            if (args == null || args.Length == 0)
            {
                // default: describe the box the player is looking at
                var look = FindLookEntity(requester);
                if (look == null) { requester.ChatMessage("No box found where you're looking."); return; }
                var net = look.net?.ID ?? default(NetworkableId);
                if (!net.IsValid) { requester.ChatMessage("Entity has no valid NetId."); return; }
                ulong netid = net.Value;
                AsyncDescribeBoxCompact(requester, netid);
                return;
            }

            var cmd = args[0].ToLowerInvariant();
            switch (cmd)
            {
                case "help":
                    SendHelp(requester);
                    break;

                case "clear":
                    if (requester.net.connection.authLevel < 2) { requester.ChatMessage("Permission denied: clear requires auth level 2."); return; }
                    CloseConnection();
                    if (File.Exists(DbPath)) File.Delete(DbPath);
                    EnsureDb();
                    OpenConnection();
                    CreateTables();
                    requester.ChatMessage("BoxLooters: database cleared and re-created.");
                    break;

                case "diag":
                    // Output a concise single-line diagnostic string to the requester.
                    int queuedCountDiag;
                    int pendingCountDiag;
                    int lastLootDiag;
                    int nameCacheDiag;
                    lock (_queueLock) queuedCountDiag = _writeQueue.Count;
                    lock (_pendingLock)
                    {
                        pendingCountDiag = pendingItems.Count;
                        lastLootDiag = lastLootForContainer.Count;
                    }
                    nameCacheDiag = _nameCache.Count;

                    requester.ChatMessage($"[FAR: Box Looters]\nqueuedSql={queuedCountDiag}, pendingItems={pendingCountDiag},\nlastLootEntries={lastLootDiag}, nameCache={nameCacheDiag}");
                    break;

                case "id":
                    if (args.Length < 2) { requester.ChatMessage("Usage: box id <netid>"); return; }
                    if (!ulong.TryParse(args[1], out var id)) { requester.ChatMessage("Invalid netid"); return; }
                    AsyncDescribeBoxCompact(requester, id);
                    break;

                case "near":
                    double radius = 10.0;
                    if (args.Length >= 2) double.TryParse(args[1], out radius);
                    AsyncFindBoxesNear(requester, requester.transform.position, radius);
                    break;

                case "player":
                    if (args.Length < 2)
                    {
                        requester.ChatMessage("Usage: box player <partialname/id> <opt:radius>");
                        return;
                    }
                    double r2 = 50.0;
                    if (args.Length >= 3)
                        double.TryParse(args[2], out r2);

                    ResolveSteamIdOrName(args[1], steamId =>
                    {
                        if (steamId != 0)
                            AsyncFindPlayerLootsBySteamId(requester, steamId, requester.transform.position, r2);
                        else
                            requester.ChatMessage($"No player found matching '{args[1]}'.");
                    });
                    break;

                case "detail":
                    int lines = 25; if (args.Length >= 2) int.TryParse(args[1], out lines);
                    var looking = FindLookEntity(requester);
                    if (looking == null) { requester.ChatMessage("No box found where you're looking."); return; }
                    var nidStruct = looking.net?.ID ?? default(NetworkableId);
                    if (!nidStruct.IsValid) { requester.ChatMessage("Entity has no valid NetId."); return; }
                    AsyncDescribeBoxDetail(requester, nidStruct.Value, lines);
                    break;

                default:
                    // default: unknown command sent
                    SendHelp(requester);
                    break;
            }
        }

        private void SendHelp(BasePlayer p)
        {
            p.ChatMessage("/box - show info for box you're looking at");
            p.ChatMessage("/box id <netid> - show info for box with NetId");
            p.ChatMessage("/box near <radius> - overlay boxes in radius (default 10m)");
            p.ChatMessage("/box player <partialname/id> <opt:radius> - overlay boxes touched by player");
            p.ChatMessage("/box detail <lines> - show last <lines> item transactions for the box you're looking at (console preferred)");
            p.ChatMessage("/box clear - wipe all recorded data (auth level 2)");
        }
        #endregion

        #region Async read helpers (DB -> assemble -> send to player on main thread)

        // Describe compact info + last 5 looters asynchronously
        private void AsyncDescribeBoxCompact(BasePlayer player, ulong netid)
        {
            var lines = new List<string>();
            QueryBoxHeader(netid, row =>
            {
                if (row == null)
                {
                    lines.Add($"No box with NetId {netid} found in DB.");
                    timer.Once(0f, () => SendLinesToPlayer(player, lines));
                    return;
                }
                var prefab = row["prefab"]?.ToString() ?? "";
                long owner = row["owner"] != null ? Convert.ToInt64(row["owner"]) : 0;

                Action<string> continueWithOwnerName = ownerName =>
                {
                    var ownerText = owner != 0
                        ? (!string.IsNullOrEmpty(ownerName) ? $"<color=#d4a017>{ownerName}</color>" : "<unknown>") + $" ({owner})"
                        : "unowned";

                    lines.Add($"<color=#55ff55>{prefab}</color> ({netid})\nOwner: {ownerText}");

                    QueryLastLooters(netid, 5, looterLines =>
                    {
                        lines.AddRange(looterLines);
                        timer.Once(0f, () => SendLinesToPlayer(player, lines));
                    });
                };

                if (owner != 0)
                    GetPlayerNameCached((ulong)owner, ownerName => continueWithOwnerName(ownerName));
                else
                    continueWithOwnerName(null);
            });
        }

        private void AsyncDescribeBoxDetail(BasePlayer player, ulong netid, int limitLines)
        {
            var lines = new List<string>();

            QueryBoxHeader(netid, row =>
            {
                if (row == null)
                {
                    lines.Add($"No box with NetId {netid} found in DB.");
                    timer.Once(0f, () => SendLinesToPlayer(player, lines));
                    return;
                }
                var prefab = row["prefab"]?.ToString() ?? "";
                long owner = row["owner"] != null ? Convert.ToInt64(row["owner"]) : 0;

                Action<string> continueWithOwnerName = ownerName =>
                {
                    var ownerText = owner != 0
                        ? (!string.IsNullOrEmpty(ownerName) ? $"<color=#d4a017>{ownerName}</color>" : "<unknown>") + $" ({owner})"
                        : "unowned";

                    lines.Add($"<color=#55ff55>{prefab}</color> ({netid})\nOwner: {ownerText}");

                    // Distinct last 5 looters
                    QueryLastLooters(netid, 5, looterLines =>
                    {
                        lines.AddRange(looterLines);
                        // then run the itemsSql query afterwards (unchanged)
                        var itemsSql = Sql.Builder.Append(
                            @"SELECT li.shortname, li.qty_change, l.looter_name, l.time
                            FROM loot_items li
                            JOIN loots l ON li.loot_id = l.id
                            WHERE l.netid=@0
                            ORDER BY l.time DESC, li.id DESC
                            LIMIT @1;",
                            (long)netid, limitLines
                        );

                        _sqlite.Query(itemsSql, _conn, itemsResults =>
                        {
                            lines.Add("--- Last item transactions ---");
                            var anyItems = false;
                            if (itemsResults != null)
                            {
                                foreach (var r in itemsResults)
                                {
                                    anyItems = true;
                                    var sn = r["shortname"]?.ToString() ?? "";
                                    var q = Convert.ToInt32(r["qty_change"] ?? 0);
                                    var name = r["looter_name"]?.ToString() ?? "";
                                    var timeStr = r["time"]?.ToString() ?? "";
                                    DateTime parsedItem;
                                    var formattedTime = DateTime.TryParse(timeStr, out parsedItem)
                                        ? parsedItem.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC"
                                        : timeStr;

                                    var signText = q > 0 // q is +1 or -1 in the new model
                                        ? "(<color=#55ff55>++</color>)" : q < 0
                                        ? "(<color=#ff5555>--</color>)" : "(  )";

                                    lines.Add($"{formattedTime} {name} {signText} {sn}");
                                }
                            }

                            if (!anyItems) lines.Add(" <no item transactions recorded>");

                            timer.Once(0f, () => SendLinesToPlayer(player, lines));
                        });
                    });
                };

                // Owner resolution (same as compact)
                if (owner != 0)
                    GetPlayerNameCached((ulong)owner, ownerName => continueWithOwnerName(ownerName));
                else
                    continueWithOwnerName(null);
            });
        }

        private void AsyncFindBoxesNear(BasePlayer player, Vector3 center, double radius)
        {
            var sql = Sql.Builder.Append("SELECT netid,prefab,x,y,z,destroyed_at,pickedup_at FROM boxes;");
            _sqlite.Query(sql, _conn, results =>
            {
                // Format rows into lines using shared formatter (already returns a List<string>)
                var boxLines = FormatBoxRows(results, center, radius);

                if (boxLines.Count == 0) boxLines.Add("No boxes found in radius.");
                timer.Once(0f, () => DrawOverlay(player, boxLines, 60f)); // DrawOverlay will chat the lines
            });
        }

        private void AsyncFindPlayerLootsBySteamId(BasePlayer player, ulong steamId, Vector3 center, double radius)
        {
            // Filter at the DB level using 2D distance on X/Z (no sqrt)
            var r2 = radius * radius;
            var sql = Sql.Builder.Append(@"
                SELECT b.netid, b.prefab, b.x, b.y, b.z, b.destroyed_at, b.pickedup_at
                FROM loots l
                JOIN boxes b ON l.netid = b.netid
                WHERE l.looter_steamid=@0
                  AND ((b.x - @1) * (b.x - @1) + (b.z - @2) * (b.z - @2)) <= @3
                GROUP BY b.netid;",
                (long)steamId, center.x, center.z, r2
            );

            _sqlite.Query(sql, _conn, results =>
            {
                // Format rows (FormatBoxRows still checks radius; harmless double-check)
                var boxLines = FormatBoxRows(results, center, radius);
                if (boxLines == null || boxLines.Count == 0)
                    boxLines = new List<string> { "No loots found for that player in radius." };

                timer.Once(0f, () => DrawOverlay(player, boxLines, 60f));
            });
        }

        private List<string> FormatBoxRows(IEnumerable<IDictionary<string, object>> rows, Vector3 center, double radius)
        {
            var result = new List<string>();
            if (rows == null) return result;

            foreach (var row in rows)
            {
                var net = row.ContainsKey("netid") ? (ulong)Convert.ToInt64(row["netid"]) : 0;
                var prefab = row["prefab"]?.ToString() ?? "";
                var x = ToDouble(row["x"]);
                var y = ToDouble(row["y"]);
                var z = ToDouble(row["z"]);
                var destroyed = row["destroyed_at"]?.ToString();
                var picked = row["pickedup_at"]?.ToString();
                var pos = new Vector3((float)x, (float)y, (float)z);

                if (Vector3.Distance(center, pos) <= radius)
                {
                    var status = "active:";
                    if (!string.IsNullOrEmpty(destroyed)) status = "<color=#ff5555>destroyed:</color>";
                    if (!string.IsNullOrEmpty(picked)) status = "<color=#ff5555>picked up:</color>";
                    result.Add($"{status} <color=#55ff55>{prefab}</color> ({net})\nteleportpos ({pos.x:0.0}, {pos.y:0.0}, {pos.z:0.0})");
                }
            }
            return result;
        }

        private void QueryBoxHeader(ulong netid, Action<IDictionary<string, object>> callback)
        {
            var sql = Sql.Builder.Append(
                "SELECT prefab,x,y,z,owner,destroyed_at,pickedup_at FROM boxes WHERE netid=@0;",
                (long)netid
            );
            _sqlite.Query(sql, _conn, results =>
            {
                if (results != null && results.Count > 0)
                    callback(results[0]);
                else
                    callback(null);
            });
        }

        // Send lines to player on main thread; respects ChatLineLimit and notifies about large results
        private void SendLinesToPlayer(BasePlayer player, List<string> lines)
        {
            if (player == null) return;
            if (player.net == null || player.net.connection == null) return;

            if (lines == null || lines.Count == 0) { player.ChatMessage("<no results>"); return; }

            if (lines.Count > ChatLineLimit)
            {
                player.ChatMessage($"Result has {lines.Count} lines. Please run this command from the F1 console for full output.");
                // still send first ChatLineLimit lines so user sees immediate result
                for (int i = 0; i < Math.Min(ChatLineLimit, lines.Count); i++) player.ChatMessage(lines[i]);
                return;
            }

            foreach (var l in lines) player.ChatMessage(l);
        }

        #endregion

        #region Queries & overlays (helpers reused for overlay)
        private BaseEntity FindLookEntity(BasePlayer player)
        {
            var ray = new Ray(player.eyes.position, player.eyes.HeadForward());
            if (Physics.Raycast(ray, out var hit, 6f, LayerMask.GetMask("Deployed")))
            {
                var ent = hit.collider.GetComponentInParent<BaseEntity>();
                return ent;
            }
            return null;
        }

        private void DrawOverlay(BasePlayer player, List<string> boxes, float duration)
        {
            // Fallback: chat listing. Cap at ChatLineLimit unless user asked console.
            if (player == null) return;
            player.ChatMessage("Overlay not available (using chat listing):");
            var shown = 0;
            foreach (var l in boxes)
            {
                player.ChatMessage(l);
                shown++;
                if (shown >= ChatLineLimit) break;
            }
        }
        #endregion

        #region Utility
        // Resolve SteamID -> name with cache
        private void GetPlayerNameCached(ulong steamid, Action<string> callback)
        {
            if (steamid == 0) { callback(null); return; }

            if (_nameCache.TryGetValue(steamid, out var cached))
            {
                callback(cached);
                return;
            }

            var covPlayer = covalence.Players.FindPlayerById(steamid.ToString());
            if (covPlayer != null && !string.IsNullOrEmpty(covPlayer.Name))
            {
                _nameCache[steamid] = covPlayer.Name;
                callback(covPlayer.Name);
                return;
            }

            var sql = Sql.Builder.Append("SELECT looter_name FROM loots WHERE looter_steamid=@0 ORDER BY time DESC LIMIT 1;", (long)steamid);
            _sqlite.Query(sql, _conn, results =>
            {
                var name = results != null && results.Count > 0 ? results[0]["looter_name"]?.ToString() : null;
                if (!string.IsNullOrEmpty(name)) _nameCache[steamid] = name;
                callback(name);
            });
        }

        // Cache for SteamID -> display name lookups
        private readonly Dictionary<ulong, string> _nameCache = new();

        // Resolve input (SteamID or partial name) into a SteamID; falls back to DB name search
        private void ResolveSteamIdOrName(string identifier, Action<ulong> callback)
        {
            // Direct SteamID
            if (ulong.TryParse(identifier, out var sid) && identifier.Length == 17)
            { callback(sid); return; }

            // Online match (covalence supports partial names)
            var found = covalence.Players.FindPlayer(identifier);
            if (found != null && ulong.TryParse(found.Id, out sid))
            { callback(sid); return; }

            // Offline fallback: search DB by name, but parse value safely
            var sql = Sql.Builder.Append("SELECT looter_steamid FROM loots WHERE LOWER(looter_name) LIKE @0 LIMIT 1;", "%" + identifier.ToLowerInvariant() + "%");
            _sqlite.Query(sql, _conn, results =>
            {
                if (results != null && results.Count > 0)
                {
                    var val = results[0]["looter_steamid"];
                    if (val != null)
                    {
                        // handle long/int/string DB types safely
                        if (val is long lv) { callback((ulong)lv); return; }
                        if (val is int iv) { callback((ulong)iv); return; }
                        var s = val.ToString();
                        if (ulong.TryParse(s, out var parsed)) { callback(parsed); return; }
                    }
                }
                callback(0);
            });
        }

        // Shared looter query + formatting
        private void QueryLastLooters(ulong netid, int maxLooters, Action<List<string>> callback)
        {
            var lootersSql = Sql.Builder.Append(@"
                SELECT l.looter_name, l.looter_steamid, l.time, l.auth_on_tc, l.teammate
                FROM loots l
                JOIN (
                    SELECT looter_steamid AS sid, MAX(time) AS maxtime
                    FROM loots
                    WHERE netid=@0
                    GROUP BY looter_steamid
                ) m ON l.looter_steamid = m.sid AND l.time = m.maxtime
                WHERE l.netid=@1
                ORDER BY l.time DESC
                LIMIT @2;",
                (long)netid, (long)netid, maxLooters
            );

            _sqlite.Query(lootersSql, _conn, lootResults =>
            {
                var lines = new List<string>();
                lines.Add($"Last {maxLooters} looters:");
                var any = false;

                if (lootResults != null)
                {
                    foreach (var r in lootResults)
                    {
                        any = true;
                        var name = r["looter_name"]?.ToString() ?? "";
                        var sid = r["looter_steamid"] != null ? Convert.ToInt64(r["looter_steamid"]) : 0L;
                        var timeStr = r["time"]?.ToString() ?? "";
                        DateTime parsed;
                        string formattedTime = DateTime.TryParse(timeStr, out parsed)
                            ? parsed.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC"
                            : timeStr;

                        var auth = r["auth_on_tc"] != null ? Convert.ToInt32(r["auth_on_tc"]) : 0;
                        var tm = r["teammate"] != null ? Convert.ToInt32(r["teammate"]) : 0;
                        var flags = $"{(auth == 1 ? "(a)" : "(-)")}{(tm == 1 ? "(t)" : "(-)")}";

                        lines.Add($"<color=#ff5555>{name}</color> [{sid}]\n{flags} {formattedTime}");
                    }
                }

                if (!any) lines.Add("  <no looters recorded>");
                callback(lines);
            });
        }

        // --- helper: safe numeric conversion from DB row values ---
        private static double ToDouble(object obj)
        {
            if (obj == null) return 0.0;
            switch (obj)
            {
                case double d: return d;
                case float f: return (double)f;
                case decimal m: return (double)m;
                case long l: return (double)l;
                case int i: return (double)i;
                case string s:
                    if (double.TryParse(s, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var v))
                        return v;
                    return 0.0;
                default:
                    try { return Convert.ToDouble(obj); } catch { return 0.0; }
            }
        }

        // Helper: robustly parse a DB row value into a ulong netId
        private bool TryGetNetId(object obj, out ulong netId)
        {
            netId = 0;
            if (obj == null) return false;
            switch (obj)
            {
                case long l:
                    netId = (ulong)l;
                    return true;
                case int i:
                    netId = (ulong)i;
                    return true;
                case string s when ulong.TryParse(s, out var u):
                    netId = u;
                    return true;
                default:
                    try
                    {
                        // fallback: try Convert.ToInt64 then cast
                        var v = Convert.ToInt64(obj);
                        netId = (ulong)v;
                        return true;
                    }
                    catch { return false; }
            }
        }

        #endregion
    }
}
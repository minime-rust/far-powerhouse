using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core;
using Oxide.Core.Libraries;
using Oxide.Core.Libraries.Covalence;
using Rust;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("FAR: Vehicle Locks", "miniMe", "1.0.5")]
    [Description("OwnerID-driven vehicle access control, decay, and hints without physical locks.")]
    public class FARVehicleLocks : RustPlugin
    {
        #region Config

        private PluginConfig _config;

        public class VehicleTypeConfig
        {
            [JsonProperty("Vehicle Display Name")] public string DisplayName = "vehicle";
            [JsonProperty("Rust Prefab Shortname(s)")] public List<string> PrefabShortnames = new List<string>();
            [JsonProperty("Can ownership decay?")] public bool DecayableOwnership = false;
            [JsonProperty("Minutes until ownership decays outside TC range?")] public int OwnershipDecayMinutes = 15;
            [JsonProperty("Minutes until owned usage hint is shown again (prevent spam)")] public int OwnerHintUsageMinutes = 10;
            [JsonProperty("Minutes until dismount hint is shown again? (to prevent spam)")] public int RiderHintCooldownMinutes = 10;
            [JsonProperty("How many vehicles of this kind can a player lock?")] public int MaxLocksPerPlayer = 2;
        }

        public class PluginConfig
        {
            [JsonProperty("VehicleTypes")]
            public Dictionary<string, VehicleTypeConfig> VehicleTypes = new Dictionary<string, VehicleTypeConfig>
            {
                ["attackhelicopter"] = new VehicleTypeConfig
                {
                    DisplayName = "Attack Helicopter",
                    PrefabShortnames = new List<string> { "attackhelicopter" },
                    MaxLocksPerPlayer = 1
                },
                ["minicopter"] = new VehicleTypeConfig
                {
                    DisplayName = "Minicopter",
                    PrefabShortnames = new List<string> { "minicopter" },
                    MaxLocksPerPlayer = 2
                },
                ["scraptransporthelicopter"] = new VehicleTypeConfig
                {
                    DisplayName = "Scrap Transport Helicopter",
                    PrefabShortnames = new List<string> { "scraptransporthelicopter" },
                    MaxLocksPerPlayer = 1
                },
                ["modularcar"] = new VehicleTypeConfig
                {
                    DisplayName = "Modular Car",
                    PrefabShortnames = new List<string> { "2module_car", "3module_car", "4module_car" },
                    MaxLocksPerPlayer = 3
                },
                ["ridablehorse"] = new VehicleTypeConfig
                {
                    DisplayName = "Horse",
                    PrefabShortnames = new List<string> { "ridablehorse" },
                    DecayableOwnership = true,
                    OwnershipDecayMinutes = 15,
                    MaxLocksPerPlayer = 2
                },
                ["bicycle"] = new VehicleTypeConfig
                {
                    DisplayName = "Bicycle",
                    PrefabShortnames = new List<string> { "pedalbike", "pedaltrike" },
                    DecayableOwnership = true,
                    OwnershipDecayMinutes = 15,
                    MaxLocksPerPlayer = 2
                },
                ["motorbike"] = new VehicleTypeConfig
                {
                    DisplayName = "Motorbike",
                    PrefabShortnames = new List<string> { "motorbike" },
                    DecayableOwnership = true,
                    OwnershipDecayMinutes = 15,
                    MaxLocksPerPlayer = 2
                }
            };

            [JsonProperty("Discord webhook to send vehicle lock, unlock and destructions")] public string DiscordWebhook = string.Empty;
            [JsonProperty("Check interval in seconds for ownership decay")] public int DecayCheckIntervalSeconds = 30;
            [JsonProperty("Make the horse rear on unauthorized access attempt")] public bool RearHorseOnDenied = true;
            [JsonProperty("Warn seconds before vehicle ownership decays")] public int WarnWindowSec = 180;
            [JsonProperty("Set the TC radius in meters (1-100)")] public float TCRadiusMeters = 30f;
        }

        protected override void LoadDefaultConfig()
        {
            _config = new PluginConfig();
            SaveConfig();
        }
        protected override void LoadConfig()
        {
            base.LoadConfig();
            try { _config = Config.ReadObject<PluginConfig>() ?? new PluginConfig(); }
            catch { PrintWarning("Config invalid, creating default."); LoadDefaultConfig(); }
        }
        protected override void SaveConfig() => Config.WriteObject(_config, true);

        #endregion

        #region Data & Lang

        private StoredData _data;

        private class VehicleState
        {
            public ulong NetId;             // the vehicle's NetId
            public string TypeKey;          // e.g. modularcar
            public string OwnerName;        // owner's displayName
            public ulong LastRiderId;       // last rider's userID
            public string LastRiderName;    // last rider's displayName
            public double LastDismounted;   // for decayable types
        }

        private bool _dirty = false;    // indicator for changed VehicleState which needs to be saved
        private bool _debug = false;    // helper during development - we want to produce logs ...
        private class StoredData { public Dictionary<ulong, VehicleState> Vehicles = new Dictionary<ulong, VehicleState>(); }
        private void LoadData() => _data = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name) ?? new StoredData();
        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject(Name, _data);

        private const string PermBypass = "vehiclelocks.bypass";
        private void InitLang()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["NoVehiclesOwned"] = "You do not currently own any vehicles.",
                ["NotMountedDriver"] = "You must be sitting in the driver seat of a supported vehicle.",
                ["TypeLimitReached"] = "You have reached the maximum of {0} locked {1}(s).",
                ["ListLockedAndOwned"] = "{0} | in TC range: {1} | last rider: {2} | mounted: {3}",
                ["LockedNowOwnerAssigned"] = "Locked this {0}. You are now its owner.",
                ["UnlockedNow"] = "Unlocked this {0}. It is now public again.",
                ["HintCommandUsage"] = "Use <color=#55ff55>/vehicle lock</color> - lock the vehicle you are driving.\nUse <color=#55ff55>/vehicle unlock</color> - unlock the vehicle you are driving.\nUse <color=#55ff55>/vehicle list</color> - show your currently owned vehicles.",
                ["HintLockUnowned"] = "Use <color=#55ff55>/vehicle list</color> - show your currently owned vehicles.\nUse <color=#55ff55>/vehicle lock</color> in chat to make this {0} yours or team-only.",
                ["HintUnlockOwned"] = "Use <color=#55ff55>/vehicle list</color> - show your currently owned vehicles.\nUse <color=#55ff55>/vehicle unlock</color> in chat to make this {0} public again.",
                ["DeniedDriverSeat"] = "This {0} is <color=#ff5555>locked</color> by {1}.",
                ["DeniedInteract"] = "You can't access this {0} while it is <color=#ff5555>locked</color> by {1}.",
                ["DecayWarning"] = "<color=#d4a017>Important:</color> You have {0} minute(s) to mount your {1} again, or it will become public.",
                ["DecayedOwnership"] = "{0} ownership has decayed; it is now public.",
                ["PruneStart"] = "Pruning vehicle data...",
                ["PruneDone"] = "Pruning complete: removed {0} stale entries.",
                ["nobody"] = "nobody",
            }, this, "en");
        }

        private static readonly HashSet<uint> DriverSeatIds = new HashSet<uint>
        {
            // cars
            StringPool.Get("assets/prefabs/vehicle/seats/modularcardriverseat.prefab"), // Car driver seat
            // boats
            StringPool.Get("assets/prefabs/vehicle/seats/smallboatdriver.prefab"),      // Small Rowboat
            StringPool.Get("assets/prefabs/vehicle/seats/rhibdriver.prefab"),           // RHIB driver stand
            // bikes
            StringPool.Get("assets/prefabs/vehicle/seats/bikedriverseat.prefab"),       // Bicycles
            StringPool.Get("assets/prefabs/vehicle/seats/motorbikedriverseat.prefab"),  // both Motorbikes
            // heli
            StringPool.Get("assets/prefabs/vehicle/seats/attackhelidriver.prefab"),     // Attack Helicopter
            StringPool.Get("assets/prefabs/vehicle/seats/miniheliseat.prefab"),         // Minicopter
            StringPool.Get("assets/prefabs/vehicle/seats/transporthelipilot.prefab"),   // Scrap Transport Heli
            // horse saddle
            StringPool.Get("assets/prefabs/vehicle/seats/horsesaddle.prefab"),          // Horse saddle (front)
            // standing driver (workcart/workcart aboveground etc. sometimes use a standing mount)
            StringPool.Get("assets/prefabs/vehicle/seats/locomotivedriver.prefab"),     // Trains
            StringPool.Get("assets/prefabs/vehicle/seats/standingdriver.prefab"),       // unsure what the frick this is
        };

        #endregion

        #region Runtime

        private readonly Dictionary<string, string> _prefabToTypeKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<ulong, double> _lastWarnAt = new Dictionary<ulong, double>();
        private readonly Dictionary<ulong, double> _lastHintAt = new Dictionary<ulong, double>();
        private Timer _decayTimer;

        private static double Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        private static ulong GetVehicleRootId(BaseMountable mount)
        {
            var parent = mount.GetParentEntity() as BaseEntity;
            return parent != null
                 ? (parent.net?.ID.Value ?? 0UL)
                 : (mount.net?.ID.Value ?? 0UL);
        }

        private bool IsDriverSeat(BaseMountable mount)
        {
            if (mount == null) return false;

            // Fast path: exact match against known driver seats
            uint id = mount.prefabID;

            // Let's get a bit of logging ...
            if (_debug) Puts($"mount.name is \"{mount.name}\" | IsDriverSeat = \"{DriverSeatIds.Contains(id)}\"");

            return DriverSeatIds.Contains(id);
        }

        // Returns true if any driver seat of this vehicle (recursively) is occupied
        private bool IsVehicleMounted(BaseEntity vehicle)
        {
            if (vehicle == null) return false;

            // Traverse all descendants
            var mounts = Facepunch.Pool.GetList<BaseMountable>();
            vehicle.GetComponentsInChildren(mounts);

            for (int i = 0; i < mounts.Count; i++)
            {
                var m = mounts[i];
                if (m != null && IsDriverSeat(m) && m.GetMounted() != null)
                {
                    Facepunch.Pool.FreeList(ref mounts);
                    return true;
                }
            }

            Facepunch.Pool.FreeList(ref mounts);
            return false;
        }

        private BaseEntity TopEntity(BaseEntity e)
        {
            var root = e;
            while (root != null && root.HasParent()) root = root.GetParentEntity();
            return root ?? e;
        }

        private string ResolveTypeKey(BaseEntity vehicle)
        {
            if (vehicle == null) return null;
            var sn = vehicle.ShortPrefabName;
            if (string.IsNullOrEmpty(sn)) return null;
            if (_prefabToTypeKey.TryGetValue(sn, out var tk)) return tk;

            foreach (var kv in _config.VehicleTypes)
            {
                var cfg = kv.Value;
                if (cfg.PrefabShortnames.Any(p => sn.Equals(p, StringComparison.OrdinalIgnoreCase) || sn.Contains(p, StringComparison.OrdinalIgnoreCase)))
                {
                    _prefabToTypeKey[sn] = kv.Key;
                    return kv.Key;
                }
            }
            return null;
        }

        private bool IsAuthorized(BaseEntity vehicle, BasePlayer player)
        {
            if (vehicle.OwnerID == 0) return true;
            if (player.userID == vehicle.OwnerID) return true;

            var team = RelationshipManager.ServerInstance?.FindPlayersTeam(vehicle.OwnerID);
            return team != null && team.members != null && team.members.Contains(player.userID);
        }

        private bool IsWithinOwnersTC(BaseEntity vehicle, ulong ownerId)
        {
            if (ownerId == 0) return false;
            var pos = vehicle.transform.position;
            var privs = Facepunch.Pool.GetList<BuildingPrivlidge>();
            var radius = Mathf.Clamp(_config.TCRadiusMeters, 1f, 100f); // to prevent a “cover the map” situation
            Vis.Entities(pos, radius, privs, Rust.Layers.Mask.Default | Rust.Layers.Mask.Deployed, QueryTriggerInteraction.Ignore);
            foreach (var tc in privs)
            {
                if (!tc.IsDestroyed && tc.authorizedPlayers != null && tc.authorizedPlayers.Any(a => a.userid == ownerId))
                { Facepunch.Pool.FreeList(ref privs); return true; }
            }
            Facepunch.Pool.FreeList(ref privs);
            return false;
        }

        private VehicleState EnsureDataForOwned(BaseEntity vehicle, string typeKey)
        {
            var id = vehicle.net?.ID.Value ?? 0UL;
            if (id == 0) return null;

            if (!_data.Vehicles.TryGetValue(id, out var vs))
            {
                // Initialize only if truly new
                vs = new VehicleState
                {
                    NetId = id,
                    TypeKey = typeKey,
                    OwnerName = OwnerName(vehicle.OwnerID),
                    LastDismounted = 0,
                    LastRiderId = 0,
                    LastRiderName = null
                };
                _data.Vehicles[id] = vs;
                _dirty = true;
            }
            else
            {
                // Do not overwrite LastRiderId/Name or LastDismounted
                vs.TypeKey = typeKey;
                vs.OwnerName = OwnerName(vehicle.OwnerID);
            }

            return vs;
        }

        private void RemoveFromData(ulong netId)
        {
            if (_data.Vehicles.Remove(netId))
                _dirty = true;
        }

        private void SaveVehicleState(VehicleState vs)
        {
            if (vs == null) return;
            _data.Vehicles[vs.NetId] = vs;
            _dirty = true;
        }

        private string OwnerName(ulong ownerId)
        {
            var iplayer = covalence.Players.FindPlayerById(ownerId.ToString());
            if (iplayer != null) return iplayer.Name;
            var bp = BasePlayer.FindByID(ownerId) ?? BasePlayer.FindSleeping(ownerId);
            return bp != null ? bp.displayName : ownerId.ToString();
        }

        private void HorseRear(RidableHorse horse, BasePlayer player)
        {
            if (!_config.RearHorseOnDenied || horse == null ||
                permission.UserHasPermission(player.UserIDString, PermBypass))
                return;

            // Simulate double-tap crouch to trigger rear animation
            horse.duckDoubleTapped = true;
            horse.lastDuckTapTime = UnityEngine.Time.time;
            horse.MovementsUpdate();
        }

        private void PruneData(bool announce = false)
        {
            if (announce) Puts(lang.GetMessage("PruneStart", this));
            int removed = 0;
            foreach (var key in _data.Vehicles.Keys.ToList())
            {
                var ent = BaseNetworkable.serverEntities?.Find(new NetworkableId((uint)key)) as BaseEntity;
                if (ent == null || ent.IsDestroyed || ent.OwnerID == 0)
                { _data.Vehicles.Remove(key); removed++; _dirty = true; }
            }
            if (announce) Puts(string.Format(lang.GetMessage("PruneDone", this), removed));
        }

        private int CountOwnedBy(ulong playerId, string typeKey) =>
            _data.Vehicles.Values.Count(v =>
            {
                var ent = BaseNetworkable.serverEntities?.Find(new NetworkableId((uint)v.NetId)) as BaseEntity;
                return ent != null &&
                       ent.OwnerID == playerId &&
                       string.Equals(v.TypeKey, typeKey, StringComparison.OrdinalIgnoreCase);
            });

        // New: centralized decision — does this entity/type decay?
        private bool IsDecayable(BaseEntity entity, string typeKey)
        {
            if (string.IsNullOrEmpty(typeKey)) return false;
            if (!_config.VehicleTypes.TryGetValue(typeKey, out var cfg)) return false;
            return cfg.DecayableOwnership;
        }

        private void NotifyDecayHint(BaseEntity vehicleEntity, ulong ownerId, int remainingMinutes, string typeKey = null)
        {
            if (vehicleEntity == null || ownerId == 0) return;

            if (string.IsNullOrWhiteSpace(typeKey))
                typeKey = ResolveTypeKey(vehicleEntity);

            // Get VehicleState from data
            var vs = _data.Vehicles.TryGetValue(vehicleEntity.net.ID.Value, out var state) ? state : null;

            ulong recipientId = ownerId;

            // Prefer last rider if known and online
            if (vs != null && vs.LastRiderId != 0)
            {
                var rider = BasePlayer.FindByID(vs.LastRiderId);
                if (rider != null && rider.IsConnected)
                    recipientId = vs.LastRiderId;
            }

            var player = BasePlayer.FindByID(recipientId);
            if (player == null || !player.IsConnected) return;

            // Suppress duplicates using RiderHintCooldownMinutes
            if (_config.VehicleTypes.TryGetValue(typeKey, out var cfg))
            {
                var suppressWindowSec = cfg.RiderHintCooldownMinutes * 60;
                if (_lastWarnAt.TryGetValue(recipientId, out var lastAt))
                {
                    var elapsed = Now - lastAt;
                    if (elapsed < suppressWindowSec && (remainingMinutes * 60 > _config.WarnWindowSec))
                        return;
                }
            }

            _lastWarnAt[recipientId] = Now;

            if (_debug)
                Puts($"{OwnerName(recipientId)} is being reminded: {remainingMinutes} min left before {typeKey} ownership decays.");

            player.ChatMessage(Lang("DecayWarning", player, remainingMinutes, _config.VehicleTypes[typeKey].DisplayName));
        }

        private string GetDiscordTimestamp()
        {
            DateTime utcNow = DateTime.UtcNow;
            DateTime epoch = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            TimeSpan timeSpan = utcNow - epoch;
            long unixTimeSeconds = (long)timeSpan.TotalSeconds;
            return $"<t:{unixTimeSeconds}:t>";
        }

        private void SendDiscordMessage(string webhookUrl, string message)
        {
            if (string.IsNullOrWhiteSpace(webhookUrl) || string.IsNullOrWhiteSpace(message))
                return; // nothing to send

            // Discord has a strict 2000-character limit
            const int maxLen = 2000;
            if (message.Length > maxLen)
                message = message.Substring(0, maxLen - 3) + "...";

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
                RequestMethod.POST,
                new Dictionary<string, string> { ["Content-Type"] = "application/json" }
            );
        }

        private string Lang(string key, BasePlayer player = null, params object[] args)
        {
            string userId = player?.UserIDString;
            var template = lang.GetMessage(key, this, userId);
            return args.Length > 0 ? string.Format(template, args) : template;
        }

        #endregion

        #region Hooks

        private void Init()
        {
            permission.RegisterPermission(PermBypass, this);
            InitLang();
            LoadData();

            _prefabToTypeKey.Clear();
            foreach (var kv in _config.VehicleTypes)
                foreach (var sn in kv.Value.PrefabShortnames)
                    if (!string.IsNullOrEmpty(sn)) _prefabToTypeKey[sn] = kv.Key;

            cmd.AddChatCommand("vehicle", this, nameof(CmdVehicle));
        }

        private void OnServerInitialized()
        {
            PruneData(true);
            _decayTimer?.Destroy();
            _decayTimer = timer.Every(Mathf.Clamp(_config.DecayCheckIntervalSeconds, 5, 300), DecaySweep);
        }

        private void Unload()
        {
            _decayTimer?.Destroy();
            _decayTimer = null;

            if (_dirty) // make sure to actually save VehicleState if required
            {
                SaveData();
                _dirty = false;
            }
        }

        private void OnEntityTakeDamage(BaseCombatEntity entity, HitInfo info)
        {
            if (entity == null || info == null) return;

            var netId = entity.net?.ID.Value ?? 0UL;
            if (netId == 0 || !_data.Vehicles.ContainsKey(netId))
                return; // not managed by us

            // local helper
            void NullifyDamage(HitInfo hit)
            {
                hit.damageTypes = new DamageTypeList();
                hit.HitMaterial = 0;
                hit.HitEntity = null;
            }

            BasePlayer attacker = info.InitiatorPlayer;

            // Case 1: direct player damage
            if (attacker != null)
            {
                if (!permission.UserHasPermission(attacker.UserIDString, PermBypass))
                {
                    NullifyDamage(info);

                    if (_debug)
                        Puts($"Blocked direct player damage to vehicle {entity.ShortPrefabName} ({netId}) by {attacker.displayName}");
                }
                return;
            }

            // Case 2: explosions/projectiles without direct player initiator
            var initiator = info.Initiator;
            if (initiator != null)
            {
                var creator = initiator.creatorEntity as BasePlayer;
                if (creator != null && !permission.UserHasPermission(creator.UserIDString, PermBypass))
                {
                    NullifyDamage(info);

                    if (_debug)
                        Puts($"Blocked explosive/projectile damage to vehicle {entity.ShortPrefabName} ({netId}) from {initiator.ShortPrefabName}, created by {creator.displayName}");
                }
            }
        }

        private object CanMountEntity(BasePlayer player, BaseMountable mount)
        {
            if (player == null || mount == null || !IsDriverSeat(mount) ||
                permission.UserHasPermission(player.UserIDString, PermBypass))
                return null;

            var vehicle = TopEntity(mount);
            var typeKey = ResolveTypeKey(vehicle);

            if (_debug) Puts($"{player.displayName} is on the driver seat of a (typeKey \"{typeKey}\" | vehicle \"{vehicle}\")!");

            if (string.IsNullOrEmpty(typeKey)) return null;
            var cfg = _config.VehicleTypes[typeKey];

            // Ensure data presence for pre-owned vehicles (e.g., bought at vendors)
            if (vehicle.OwnerID != 0) EnsureDataForOwned(vehicle, typeKey);

            if (vehicle.OwnerID == 0)
                player.ChatMessage(Lang("HintLockUnowned", player, cfg.DisplayName));

            if (!IsAuthorized(vehicle, player))
            {
                var ownerName = OwnerName(vehicle.OwnerID);
                if (vehicle is RidableHorse horse) HorseRear(horse, player);
                player.ChatMessage(Lang("DeniedDriverSeat", player, cfg.DisplayName, ownerName));
                return false;
            }

            // reset LastDismounted when the vehicle is mounted (no warnings when driving)
            if (vehicle.OwnerID != 0 && IsAuthorized(vehicle, player))
                UpdateRiderInfo(TopEntity(mount), player);

            // Owner mounts -> show unlock hint (with cooldown)
            if (player.userID == vehicle.OwnerID)
            {
                var cooldownSec = Math.Max(60, cfg.OwnerHintUsageMinutes * 60);
                if (!_lastHintAt.TryGetValue(player.userID, out var lastAt) || (Now - lastAt) >= cooldownSec)
                {
                    player.ChatMessage(Lang("HintUnlockOwned", player, cfg.DisplayName));
                    _lastHintAt[player.userID] = Now;
                }
            }

            return null;
        }

        private void OnPlayerMounted(BaseMountable mount, BasePlayer player)
        {
            if (mount == null || player == null) return;
            if (!IsDriverSeat(mount)) return; // only care about driver seat

            UpdateRiderInfo(TopEntity(mount), player);
        }

        private void UpdateRiderInfo(BaseEntity vehicle, BasePlayer player)
        {
            if (vehicle == null || player == null) return;
            var typeKey = ResolveTypeKey(vehicle);
            if (string.IsNullOrEmpty(typeKey)) return;

            var vs = EnsureDataForOwned(vehicle, typeKey);
            if (vs == null) return;

            vs.LastDismounted = 0;
            vs.LastRiderId = player.userID;
            vs.LastRiderName = player.displayName;

            SaveVehicleState(vs);
        }

        private object OnHorseLead(RidableHorse horse, BasePlayer player)
        {
            if (horse == null || player == null ||
                permission.UserHasPermission(player.UserIDString, PermBypass))
                return null;

            var vehicle = (BaseEntity)horse;
            var typeKey = ResolveTypeKey(vehicle);
            if (string.IsNullOrEmpty(typeKey)) return null;
            if (vehicle.OwnerID == 0) return null;

            EnsureDataForOwned(vehicle, typeKey);

            if (!IsAuthorized(vehicle, player))
            {
                HorseRear(horse, player);
                var ownerName = OwnerName(vehicle.OwnerID);
                player.ChatMessage(Lang("DeniedInteract", player, _config.VehicleTypes[typeKey].DisplayName, ownerName));
                return false; // <-- BLOCK the leading action
            }
            return null;
        }

        private object CanLootEntity(BasePlayer player, BaseEntity entity)
        {
            if (player == null || entity == null ||
                permission.UserHasPermission(player.UserIDString, PermBypass))
                return null;

            var vehicle = TopEntity(entity);
            if (vehicle == null) return null;

            var typeKey = ResolveTypeKey(vehicle);
            if (string.IsNullOrEmpty(typeKey) || vehicle.OwnerID == 0) return null;

            EnsureDataForOwned(vehicle, typeKey);

            if (!IsAuthorized(vehicle, player))
            {
                var ownerName = OwnerName(vehicle.OwnerID);
                if (vehicle is RidableHorse horse) HorseRear(horse, player);
                player.ChatMessage(Lang("DeniedInteract", player, _config.VehicleTypes[typeKey].DisplayName, ownerName));
                return false;
            }
            return null;
        }

        private void OnEntityMounted(BaseMountable mount, BasePlayer player)
        {
            if (mount == null || player == null) return;
            if (!IsDriverSeat(mount)) return; // only care about driver seat

            UpdateRiderInfo(TopEntity(mount), player);
        }

        private void OnEntityDismounted(BaseMountable mount, BasePlayer player)
        {
            if (player == null || mount == null || !IsDriverSeat(mount) ||
                !IsDriverSeat(mount) || // only care about driver seat
                permission.UserHasPermission(player.UserIDString, PermBypass))
                return;

            var vehicle = TopEntity(mount);
            var typeKey = ResolveTypeKey(vehicle);
            if (string.IsNullOrWhiteSpace(typeKey)) return;

            if (!IsDecayable(vehicle, typeKey) || vehicle.OwnerID == 0) return;

            var vs = EnsureDataForOwned(vehicle, typeKey);
            if (vs == null) return;

            vs.LastDismounted = Now;
            SaveVehicleState(vs);

            if (!IsWithinOwnersTC(vehicle, vehicle.OwnerID))
            {
                if (_debug) Puts($"{OwnerName(vehicle.OwnerID)} unmounted their {typeKey}");
                var remaining = _config.VehicleTypes.TryGetValue(typeKey, out var cfg) ? cfg.OwnershipDecayMinutes : 15;

                // Always notify on dismount, no cooldown
                NotifyDecayHint(vehicle, vehicle.OwnerID, remaining, typeKey);
            }
        }

        private void OnEntityKill(BaseNetworkable entity)
        {
            var be = entity as BaseEntity;
            if (be == null || be.net?.ID == null) return;

            var netId = be.net.ID.Value;
            if (_data.Vehicles.TryGetValue(netId, out var vs))
            {
                var typeKey = vs.TypeKey ?? ResolveTypeKey(be);
                if (!string.IsNullOrEmpty(typeKey) && _config.VehicleTypes.TryGetValue(typeKey, out var cfg))
                {
                    var msg = $":boom: {GetDiscordTimestamp()} `{cfg.DisplayName}` owned by `{vs.OwnerName}` was destroyed.";
                    SendDiscordMessage(_config.DiscordWebhook, msg);
                }

                // Always remove entry at the end
                RemoveFromData(netId);
            }
        }

        private void DecaySweep()
        {
            var now = Now;
            foreach (var kv in _data.Vehicles.ToList())
            {
                var vs = kv.Value;
                var ent = BaseNetworkable.serverEntities?.Find(new NetworkableId((uint)vs.NetId)) as BaseEntity;
                if (ent == null || ent.IsDestroyed || ent.OwnerID == 0)
                { _data.Vehicles.Remove((uint)vs.NetId); continue; }

                var typeKey = ResolveTypeKey(ent);
                if (string.IsNullOrEmpty(typeKey)) continue;
                if (!IsDecayable(ent, typeKey)) continue;

                // never decay while mounted (driver seat only)
                if (IsVehicleMounted(ent)) continue;

                if (IsWithinOwnersTC(ent, ent.OwnerID)) continue;

                var minutes = _config.VehicleTypes.TryGetValue(typeKey, out var cfg) ? cfg.OwnershipDecayMinutes : 15;
                var limitSec = Math.Max(60, minutes * 60);
                var last = vs.LastDismounted > 0 ? vs.LastDismounted : now; // if never recorded, treat as just mounted

                // If close to decay but not yet decayed, notify owner/last rider.
                var remainingSec = limitSec - (now - last);
                var remainingMin = (int)(remainingSec / 60); // truncate

                // Use NotifyDecayHint to message owner or last rider
                if (remainingMin > 0 && remainingSec <= _config.WarnWindowSec)
                    NotifyDecayHint(ent, ent.OwnerID, remainingMin, typeKey);

                if (now - last >= limitSec)
                {
                    // Decay: reset OwnerID and remove from data
                    var prevOwnerId = ent.OwnerID;
                    ent.OwnerID = 0;
                    ent.SendNetworkUpdate();

                    RemoveFromData(vs.NetId);

                    var owner = BasePlayer.FindByID(prevOwnerId);
                    if (owner != null && owner.IsConnected)
                        owner.ChatMessage(Lang("DecayedOwnership", owner, _config.VehicleTypes[typeKey].DisplayName));
                }
            }

            if (_dirty) // make sure to actually save VehicleState if required
            {
                SaveData();
                _dirty = false;
            }
        }

        #endregion

        #region Commands

        [ChatCommand("vehicle")]
        private void CmdVehicle(BasePlayer player, string command, string[] args)
        {
            var sub = (args != null && args.Length > 0)
                ? args[0].ToLowerInvariant()
                : string.Empty;

            if (sub == "lock") CmdLock(player);
            else if (sub == "list") CmdList(player);
            else if (sub == "unlock") CmdUnlock(player);
            else player.ChatMessage(Lang("HintCommandUsage", player));
        }

        private void CmdLock(BasePlayer player)
        {
            var mount = player.GetMounted() as BaseMountable;
            if (mount == null || !IsDriverSeat(mount))
            { player.ChatMessage(Lang("NotMountedDriver", player)); return; }

            var vehicle = TopEntity(mount);
            var typeKey = ResolveTypeKey(vehicle);
            if (string.IsNullOrEmpty(typeKey))
            { player.ChatMessage(Lang("NotMountedDriver", player)); return; }

            var cfg = _config.VehicleTypes[typeKey];

            // Only lock if unowned or owned by self
            if (vehicle.OwnerID != 0 && vehicle.OwnerID != player.userID)
            {
                var ownerName = OwnerName(vehicle.OwnerID);
                player.ChatMessage(Lang("DeniedDriverSeat", player, cfg.DisplayName, ownerName));
                return;
            }

            // Enforce per-type limit
            var currentlyOwned = CountOwnedBy(player.userID, typeKey);
            if (vehicle.OwnerID == 0 && currentlyOwned >= Math.Max(0, cfg.MaxLocksPerPlayer))
            {
                player.ChatMessage(Lang("TypeLimitReached", player, cfg.MaxLocksPerPlayer, cfg.DisplayName));
                return;
            }

            // Lock: set OwnerID and add to data
            vehicle.OwnerID = player.userID;
            vehicle.SendNetworkUpdate();

            var vs = EnsureDataForOwned(vehicle, typeKey);
            // we don't update LastDismounted here
            vs.LastRiderId = player.userID;
            vs.LastRiderName = player.displayName;

            SaveVehicleState(vs);
            player.ChatMessage(Lang("LockedNowOwnerAssigned", player, cfg.DisplayName));

            // notify Discord
            var message = $":carousel_horse: {GetDiscordTimestamp()} `{cfg.DisplayName}` was locked by `{player.displayName}`";
            SendDiscordMessage(_config.DiscordWebhook, message);
        }

        private void CmdUnlock(BasePlayer player)
        {
            var mount = player.GetMounted() as BaseMountable;
            if (mount == null || !IsDriverSeat(mount))
            { player.ChatMessage(Lang("NotMountedDriver", player)); return; }

            var vehicle = TopEntity(mount);
            var typeKey = ResolveTypeKey(vehicle);
            if (string.IsNullOrEmpty(typeKey))
            { player.ChatMessage(Lang("NotMountedDriver", player)); return; }

            var cfg = _config.VehicleTypes[typeKey];
            if (vehicle.OwnerID != player.userID)
            {
                var ownerName = vehicle.OwnerID == 0 ? lang.GetMessage("nobody", this, player.UserIDString) : OwnerName(vehicle.OwnerID);
                player.ChatMessage(Lang("DeniedDriverSeat", player, cfg.DisplayName, ownerName));
                return;
            }

            // Unlock: reset OwnerID to 0 and remove from data
            vehicle.OwnerID = 0;
            vehicle.SendNetworkUpdate();
            RemoveFromData(vehicle.net.ID.Value);
            _lastHintAt.Remove(player.userID);

            player.ChatMessage(Lang("UnlockedNow", player, cfg.DisplayName));

            // notify Discord
            var message = $":carousel_horse: {GetDiscordTimestamp()} `{cfg.DisplayName}` was unlocked by `{player.displayName}`";
            SendDiscordMessage(_config.DiscordWebhook, message);
        }

        private void CmdList(BasePlayer player)
        {
            var owned = _data.Vehicles
                .Where(kv =>
                {
                    var ent = BaseNetworkable.serverEntities?.Find(new NetworkableId(kv.Key)) as BaseEntity;
                    return ent != null && ent.OwnerID == player.userID;
                });

            if (!owned.Any())
            {
                player.ChatMessage(Lang("NoVehiclesOwned", player));
                return;
            }

            foreach (var kv in owned)
            {
                var ent = BaseNetworkable.serverEntities?.Find(new NetworkableId(kv.Key)) as BaseEntity;
                if (ent == null) continue;

                var vs = kv.Value;
                var inTc = IsWithinOwnersTC(ent, ent.OwnerID);
                var riderName = !string.IsNullOrEmpty(vs.LastRiderName) ? vs.LastRiderName : Lang("nobody", player);

                // minimal call to helper
                bool isMounted = IsVehicleMounted(ent);

                player.ChatMessage(Lang("ListLockedAndOwned", player, _config.VehicleTypes[vs.TypeKey].DisplayName, inTc, riderName, isMounted));
            }
        }

        #endregion
    }
}
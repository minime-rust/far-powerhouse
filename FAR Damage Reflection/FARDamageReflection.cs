using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;             // for CultureInfo.InvariantCulture
using System.Reflection;
using System.Runtime.CompilerServices;  // "aggressive inlining"
using System.Text;                      // stringbuilder
using Facepunch;                        // sometimes needed for Unity-style objects
using Facepunch.Models;                 // needed for F7 Feedback Reports
using Newtonsoft.Json;                  // Json lists in config
using Oxide.Core;                       // for ServerUsers
using Oxide.Core.Libraries;             // webrequests
using Oxide.Core.Libraries.Covalence;   // for covalence?.Players?.FindPlayerById
using Oxide.Core.Plugins;
using Rust;
using UnityEngine;                      // BasePlayer, GameObject, etc.

namespace Oxide.Plugins
{
    [Info("FAR: Damage Reflection", "miniMe", "1.1.7")]
    [Description("Based on Chernarust's 'ReflectDamage' plugin. Reflects configurable portions of damage back to players, amplifies headshot damage, and optionally applies a bleeding effect to the attacker. Improves basic TC security. Requires specific permission for bypass.")]

    public class FARDamageReflection : RustPlugin
    {
        private const string PermBypass = "damagereflection.bypass";
        private bool debug;                          // true = don't actually punish the attacker, print debug info to console instead
        private bool applyBleeding;                  // true = reflect configured damage to attacker and apply configured bleeding
        private bool enableBypassPermission;         // true = admins can bypass plugin restrictions, mirrored on CanEntityTakeDamage hook
        private bool enableDamageToVictim;           // true = allow damage to victim or entity
        private bool enableDeathPenalty;             // true = kill the attacker when forgivableStrikesMax reached
        private bool enableForbiddenDeployables;     // true = remove forbidden deployables outside the owner's TC range
        private bool enablePveHook;                  // do we expose CanEntityTakeDamage to other plugins?
        private bool enableAutoBan;                  // determins whether severe raid attempts (expl + vital parts) get a temp ban response
        private bool enableAutoKick;                 // true = in addition to counting up strikes, auto-kick player when max strikes reached
        private bool enableForgiveness;              // determines if events are forgivable
        private bool enableReflectionPVP;            // determines if damage inflicted to players shall be reflected
        private bool enableReflectionEntity;         // determines if damage inflicted to entities shall be reflected
        private bool enableheadshotMultiplier;       // determines if headshot damage shall be reflected multiplied
        private bool isHeadShotForgivable;           // can a headshot be forgiven?
        private uint raidTempBanHours;               // 0 to disable temp ban; fallback to kick
        private uint headshotMultiplier;             // 0 to disable multiplier; multiplies damage % reflected to attacker
        private uint forgivableStrikesDecay;         // 0 to disable strike decay (strikes do not persist a server reboot)
        private uint forgivableStrikesMax;           // 0 to disable strikes (disabled strikes = disabled forgiveness)
        private float bleedingIntensity;             // 0 to disable; configure how much bleeding to apply
        private float reflectPercentagePVP;          // 0 to disable; configure how much % of the damage to reflect
        private float reflectPercentageEntity;       // 0 to disable; configure how much % of the damage to reflect
        private bool warnVictimWhileForgivable;      // true = victim gets chat info when online & being attacked
        private bool warnAttackerWhileForgivable;    // true = attacker gets chat warning and strike counter
        private bool enableTcAuthGuardProtection;    // true = prevent users from changing auth on unlocked tool cupboards
        private bool pvePluginInstalled;             // server owner’s belief; we may override to true via tripwire
        private bool pveHookDetected = false;        // tripwire - will be set and stay true if CanEntityTakeDamage gets called
        private readonly float reflectionBypassWindow = 0.25f; // seconds a reflected hit is auto-allowed (configurable)
        private static float Now() => UnityEngine.Time.realtimeSinceStartup; // Fast time access

        private HashSet<ulong> playersInMonumentZone = new HashSet<ulong>();
        private HashSet<ulong> playersInRaidableBases = new HashSet<ulong>();
        private HashSet<ulong> playersInAbandonedBases = new HashSet<ulong>();
        private HashSet<BasePlayer> currentlyProcessing = new HashSet<BasePlayer>();

        // support event zones to exempt players from Damage Reflection
        [PluginReference] Plugin DynamicPVP, FARLogger, ZoneManager;

        #region Localization
        // Notice: no explicit "en" argument here. If you omit it, Oxide automatically treats this as the
        // default language and writes it to en/<PluginName>.json if the file doesn’t exist.
        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                ["pvpAutoKick"] = "No PVP allowed! Kicked for PVP on Player {0}",
                ["pvpWarnAttacker"] = "<color=#ff5555>No PVP allowed!</color> Strike: {0}/{1}",
                ["pvpWarnVictim"] = "<color=#55ff55>No PVP allowed!</color> You were attacked by {0}",
                ["forbiddenRemoved"] = "<color=#ff5555>You are not allowed to place {0} here!</color> The item was removed.",
                ["forbiddenReminder"] = "<color=#ffaa00>Reminder:</color> Placement of {0} is restricted to your own base.",
                ["raidAutoBan"] = "Unauthorized raiding! Temporary ban {0}h",
                ["raidAutoKick"] = "Unauthorized raiding! This is not a Raidable Base event!",
                ["raidIgnore"] = "<color=#55ff55>Watch your fire!</color> You hit {0}'s building!",
                ["raidStrike"] = "<color=#ff5555>Raiding is not allowed!</color> Strike: {0}/{1}.",
                ["TcAuthGuard"] = "This TC needs to be <color=#ffaa00>locked</color> before you can change auth!",
            }, this);
        }

        #endregion

        #region CONFIG

        private DamageConfig _config;  // Non-static, instance field
        public sealed class DamageConfig
        {
            [JsonProperty("General")]
            public GeneralOptions General = new GeneralOptions();

            [JsonProperty("Discord")]
            public DiscordOptions Discord = new DiscordOptions();

            [JsonProperty("Entity")]
            public EntityOptions Entity = new EntityOptions();

            [JsonProperty("ForbiddenDeployables")]
            public ForbiddenDeployablesOptions ForbiddenDeployables = new ForbiddenDeployablesOptions();

            [JsonProperty("PvP")]
            public PvPOptions PvP = new PvPOptions();

            public static DamageConfig CreateDefault() => new DamageConfig();
        }

        public sealed class GeneralOptions
        {
            [JsonProperty("debug")]
            public bool debug = false;

            [JsonProperty("enableF7Reports")]
            public bool enableF7Reports = false;

            [JsonProperty("enableBypassPermission")]
            public bool enableBypassPermission = false;

            [JsonProperty("enableTcAuthGuardProtection")]
            public bool enableTcAuthGuardProtection = false;

            [JsonProperty("enablePveHook")]
            public bool enablePveHook = true;

            [JsonProperty("pvePluginInstalled")]
            public bool pvePluginInstalled = false;

            [JsonProperty("enableDamageToVictim")]
            public bool enableDamageToVictim = false;

            [JsonProperty("forgivableStrikesMax")]
            public uint forgivableStrikesMax = 3u;

            [JsonProperty("forgivableStrikesDecayMinutes")]
            public uint forgivableStrikesDecay = 20u;

            [JsonProperty("enableDeathPenalty")]
            public bool enableDeathPenalty = false;

            [JsonProperty("enableAutoKick")]
            public bool enableAutoKick = false;
        }

        public sealed class PvPOptions
        {
            [JsonProperty("applyBleeding")]
            public bool applyBleeding = true;

            [JsonProperty("bleedingIntensity")]
            public float bleedingIntensity = 5f;

            [JsonProperty("reflectPercentagePVP")]
            public float reflectPercentagePVP = 100f;

            [JsonProperty("isHeadShotForgivable")]
            public bool isHeadShotForgivable = false;

            [JsonProperty("headshotMultiplier")]
            public uint headshotMultiplier = 2u;

            [JsonProperty("warnVictimWhileForgivable")]
            public bool warnVictimWhileForgivable = false;

            [JsonProperty("warnAttackerWhileForgivable")]
            public bool warnAttackerWhileForgivable = false;
        }

        public sealed class ForbiddenDeployablesOptions
        {
            [JsonProperty("enable")]
            public bool enable = false;

            [JsonProperty("forbiddenDeployables", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> forbiddenDeployables { get; set; } = new List<string>
            {
                "autoturret",
                "barricade.medieval",
                "barricade.metal",
                "barricade.wood",
                "barricade.woodwire",
                "beartrap",
                "flameturret",
                "guntrap",
                "landmine",
                "spikes.floor"
            };
        }

        public sealed class EntityOptions
        {
            [JsonProperty("reflectPercentageEntity")]
            public float reflectPercentageEntity = 100f;

            [JsonProperty("raidTempBanHours")]
            public uint raidTempBanHours = 24u; // 0 disables temp ban; fallback to kick

            /// <summary>
            /// Include specific entities to be considered raid-relevant.
            /// You can enter either:
            ///   - A type name (e.g. "HighWall", "Fridge")
            ///   - A prefab path (e.g. "assets/prefabs/building/barricades/barricade.sandbags.prefab")
            /// </summary>
            [JsonProperty("IncludedEntities", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> IncludedEntities { get; set; } = new List<string>
            {
                "BuildingPrivlidge",    // tool cupboards
                "BuildingBlock*",       // walls, floors, roofs, embrasures, window bars, high externals, etc.
                "SimpleBuildingBlock*", // Window bars (metal/wood/toptier), reinforced glass window, etc.
                "Door*",                // wooden, sheet metal, garage doors, hatches, shutters
                "BaseOven*",            // furnace, large furnace, bbq, campfire
                "IOEntity*",            // components like pumps, purifiers, etc.
                "StorageContainer*",    // wooden boxes, lockers, fridge, etc. (LootContainer excluded separately)

                // single specific entities
                "Fridge",
                "Workbench",
                "RepairBench",
                "ResearchTable",
                "HitchTrough",
                "Barricade"             // all barricades
            };

            /// <summary>
            /// Exclude specific entities from being considered raid-relevant.
            /// You can enter either:
            ///   - A type name (e.g. "LootContainer", "BaseTrap")
            ///   - A prefab path (e.g. "assets/prefabs/building/barricades/barricade.sandbags.prefab")
            /// </summary>
            [JsonProperty("ExcludedEntities", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> ExcludedEntities { get; set; } = new List<string>
            {
                "LootContainer",        // don’t reflect damage on barrels, crates, etc.
                "BaseTrap"              // exclude traps if you want them non-raid-relevant
            };
        }

        public sealed class DiscordOptions
        {
            [JsonProperty("pvpViolationWebhook")]
            public string pvpViolationWebhook = string.Empty;

            [JsonProperty("entityViolationWebhook")]
            public string entityViolationWebhook = string.Empty;

            [JsonProperty("raidAutoBanWebhook")]
            public string raidAutoBanWebhook = string.Empty;

            [JsonProperty("raidAutoKickWebhook")]
            public string raidAutoKickWebhook = string.Empty;

            [JsonProperty("pvpAutoKickWebhook")]
            public string pvpAutoKickWebhook = string.Empty;

            [JsonProperty("f7ReportsWebhook")]
            public string f7ReportsWebhook = string.Empty;

            [JsonProperty("forbiddenDeployablesWebhook")]
            public string forbiddenDeployablesWebhook = string.Empty;
        }

        protected override void LoadDefaultConfig() => _config = DamageConfig.CreateDefault();

        private void NormalizeConfig()
        {
            if (_config == null) _config = DamageConfig.CreateDefault();
            if (_config.General == null) _config.General = new GeneralOptions();
            if (_config.Discord == null) _config.Discord = new DiscordOptions();
            if (_config.Entity == null) _config.Entity = new EntityOptions();
            if (_config.PvP == null) _config.PvP = new PvPOptions();
            if (_config.ForbiddenDeployables == null) _config.ForbiddenDeployables = new ForbiddenDeployablesOptions();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try
            {
                _config = Config.ReadObject<DamageConfig>();
                NormalizeConfig();
                SaveConfig(); // write back missing defaults if any
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                PrintWarning("Config invalid. Creating a fresh one.");
                LoadDefaultConfig();
                SaveConfig();
            }
        }

        protected override void SaveConfig() => Config.WriteObject(_config, true);

        private void Init()
        {
            InitMainConfig();
            InitExclusions();
            InitRaidRelevantTypes();
            InitForbiddenDeployables();
            playersInMonumentZone?.Clear();

            // Framework calls LoadConfig before Init, but keep a belt-and-braces guard.
            if (_config == null)
            {
                PrintWarning("Config was null in Init; creating defaults.");
                LoadDefaultConfig();
                SaveConfig();
            }

            // After building up all the configuration, print a summary to server console.
            Puts(DamageReflectionStatus());
        }

        // Optional hygiene if your zone system misses exit on disconnect/respawn:
        private void OnPlayerDisconnected(BasePlayer player, string reason)
        {
            if (player == null) return;
            playersInMonumentZone.Remove(player.userID);
        }

        private void OnPlayerRespawned(BasePlayer player)
        {
            if (player == null) return;
            playersInMonumentZone.Remove(player.userID);
        }

        private void InitMainConfig()
        {
            // 1) handle debug and hooks
            debug = _config.General.debug;                                          // debug disables punishments
            enablePveHook = _config.General.enablePveHook;                          // enable our CanEntityTakeDamage hook
            pvePluginInstalled = _config.General.pvePluginInstalled;                // server owner's belief: PVE plugin (not) installed
            enableBypassPermission = _config.General.enableBypassPermission;        // controls whether bypass will be available
            enableForbiddenDeployables = _config.ForbiddenDeployables.enable;       // takes over from FAR: Trapper - handle stray traps
            if (enableBypassPermission) permission.RegisterPermission(PermBypass, this);
            enableTcAuthGuardProtection = _config.General.enableTcAuthGuardProtection; // control TC Lock Auth Guard protection
            // 2) set configured % for proportional damage reflection
            reflectPercentagePVP = _config.PvP.reflectPercentagePVP;                // disables reflection for PVP events if set to 0.0%
            reflectPercentageEntity = _config.Entity.reflectPercentageEntity;       // disables reflection for Entity events if set to 0.0%
            // 3) enable or disable reflection based on configuration
            enableReflectionPVP = reflectPercentagePVP > 0f;                        // determines whether to enable reflection for PVP
            enableReflectionEntity = reflectPercentageEntity > 0f;                  // determines whether to enable reflection for Entities
            // 4) evaluate forgiveness (global setting, combines both PvP and Entity damages)
            // Forgiveness is enabled if max strikes are > 0. If 1, it means first strike triggers punishment.
            // This is valid: A forgivableStrikesMax of 1 means the first strike is the "last" forgivable one,
            // and the punishment applies on that same strike. Prerequisite for auto-kicks or death-penalties!
            enableForgiveness = _config.General.forgivableStrikesMax > 0u &&        // forgiveness requires
                                (_config.General.enableAutoKick ||                  // either auto-kick, or
                                _config.General.enableDeathPenalty);                // death penalty enabled
            // 5) enable or disable Death Penalty (don't just reflect damage, kill attacker instead)
            // Death penalty is an alternative punishment. The player may e.g. attack another player 2 times
            // without consequences if reflection is disabled, but be killed on the 3rd strike.
            enableDeathPenalty = enableForgiveness &&                               // forgiveness
                                _config.General.enableDeathPenalty;                 // death penalty is enabled
            // 6) bleeding if reflection is enabled AND bleeding is enabled in config AND intensity is positive AND
            // Death Penalty is NOT enabled. Bleeding only applies to reflected damage, not to the Death Penalty.
            applyBleeding = (enableReflectionPVP || enableReflectionEntity) &&      // Reflection is generally enabled
                                _config.PvP.applyBleeding &&                        // Bleeding is enabled in config
                                _config.PvP.bleedingIntensity > 0f;                 // Intensity is positive
            bleedingIntensity = applyBleeding ? _config.PvP.bleedingIntensity : 0f;
            // 7) allow damage to victim?
            enableDamageToVictim = _config.General.enableDamageToVictim;            // allow damage to victim
            // 8) evaluate headshot multiplier
            enableheadshotMultiplier = enableReflectionPVP &&                       // reflection is enabled
                                _config.PvP.headshotMultiplier > 0u;                // multiplier is > 0
            headshotMultiplier = enableheadshotMultiplier ? _config.PvP.headshotMultiplier : 1u;
            // 9) strike handling
            forgivableStrikesMax = enableForgiveness ? _config.General.forgivableStrikesMax : 0u;
            forgivableStrikesDecay = enableForgiveness ? _config.General.forgivableStrikesDecay : 0u;
            enableAutoKick = enableForgiveness && _config.General.enableAutoKick;
            isHeadShotForgivable = enableForgiveness && _config.PvP.isHeadShotForgivable;
            raidTempBanHours = _config.Entity.raidTempBanHours;                     // uint: already non-negative
            enableAutoBan = raidTempBanHours > 0;                                   // true if any temp-ban hours configured
            // 10) warnings to attacker and victim
            warnVictimWhileForgivable = enableForgiveness && _config.PvP.warnVictimWhileForgivable;
            warnAttackerWhileForgivable = enableForgiveness && _config.PvP.warnAttackerWhileForgivable;
        }

        #endregion

        #region OnEntityTakeDamage

        // Hook: Rust calls this; we keep work minimal and branch early.
        private void OnEntityTakeDamage(BaseCombatEntity victim, HitInfo info)
        {
            // 1) Sanity + feature gates
            if (victim == null || info == null || info.damageTypes == null) return;

            //   - ignore fun "weapons" watergun and waterpistol, catapult ...
            if (info?.WeaponPrefab != null)
            {
                var w = info.WeaponPrefab.ShortPrefabName;
                if (w == "waterpistol.entity" || w == "watergun.entity" || w == "catapult.entity")
                    return; // ignore
            }

            // 2) Extract attacker once, then cheapest exits first
            var attacker = info.InitiatorPlayer;
            if (!IsRealPlayer(attacker)) return;                // Only care about real human attackers
            if (!attacker.IsAlive()) return;                    // Dead attacker? Ignore
            if (ReferenceEquals(attacker, victim)) return;      // Self-hit (splash, etc.), skip
            if (victim.IsDestroyed) return;                     // ignore destroyed entity

            // 3) Bypass guards (cheap field/lookup work)
            if (HasBypass(attacker)) return;

            // 4) Compute damage total ONCE and branch
            float total = info.damageTypes.Total();
            // // If total == 0, check if this was one of our blocked events
            if (total <= 0f)
            {
                var intent = ConsumeReflectionIntent(victim.net?.ID.Value ?? 0, attacker.userID);
                if (intent == null) return; // not ours, ignore

                total = intent.Damage; // restore context for punishment
                info.damageTypes.Set(intent.DamageType, intent.Damage);
                info.HitBone = intent.HitBone;
            }

            if (total <= 0f) return; // still irrelevant

            // 5) PvP branch: victim must be a player (sleepers allowed), privilege does not apply
            if (victim is BasePlayer vPlayer && IsRealPlayer(vPlayer))
            { HandlePlayerDamage(attacker, vPlayer, info, total); return; } // Exit after handling PvP

            // 6) Entity branch: only if feature enabled AND attacker is building-blocked (i.e., not authed)
            ulong ownerId = victim.OwnerID;
            if (ownerId != 0UL &&
                IsRaidRelevantEntity(victim) &&
                !IsPlayerInRaidableZone(attacker) &&
                IsAttackerUnauthedOnEntityTC(attacker.userID, victim))
            { HandleEntityDamage(attacker, victim, info, total); }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsRaidRelevantEntity(BaseCombatEntity e)
        {
            // Cheap exits first
            if (e == null) return false;
            if (e is BaseNpc || e is BasePlayer || e is BaseVehicle || e is BaseCorpse || e is LootContainer) return false;
            if (e is BaseTrap || e is BaseHelicopter || e is CH47HelicopterAIController || e is ScientistNPC) return false;

            // Type check (use precomputed allowlist, but respect exclusions)
            var et = e.GetType();
            if (RaidRelevantTypeSet.Contains(et) && !_excludedTypes.Contains(et))
                return true;

            // Prefab check (HashSet<uint> fast path)
            if (_excludedPrefabs.Contains(e.prefabID))
                return false;

            return _includedPrefabs.Contains(e.prefabID);
        }

        private HashSet<Type> _excludedTypes = new HashSet<Type>();
        private HashSet<uint> _excludedPrefabs = new HashSet<uint>();
        private HashSet<uint> _includedPrefabs = new HashSet<uint>();

        private void InitExclusions()
        {
            _excludedTypes.Clear();
            _excludedPrefabs.Clear();

            var asm = typeof(BaseEntity).Assembly;

            foreach (var entry in _config.Entity.ExcludedEntities)
            {
                if (string.IsNullOrWhiteSpace(entry))
                    continue;

                // Prefab exclusion
                if (entry.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
                {
                    uint prefabId = entry.ToPrefabID();
                    _excludedPrefabs.Add(prefabId);
                    if (debug)
                        Puts($"[DEBUG] Excluded prefab: {entry} ({prefabId})");
                }
                else
                {
                    // Try resolve type name in Rust assembly
                    var t = asm.GetType(entry, false, true);
                    if (t != null)
                    {
                        _excludedTypes.Add(t);
                        if (debug)
                            Puts($"[DEBUG] Excluded type: {t.FullName}");
                    }
                    else if (debug)
                    {
                        Puts($"[DEBUG] Warning: exclusion '{entry}' did not resolve as prefab or type.");
                    }
                }
            }

            if (debug)
            {
                Puts($"[DEBUG] Exclusion setup complete. " +
                     $"Types excluded: {_excludedTypes.Count}, Prefabs excluded: {_excludedPrefabs.Count}");
            }
        }

        private static readonly HashSet<Type> RaidRelevantTypeSet = new HashSet<Type>();

        private void InitRaidRelevantTypes()
        {
            RaidRelevantTypeSet.Clear();
            _includedPrefabs.Clear();

            var asm = typeof(BaseEntity).Assembly;

            foreach (var entry in _config.Entity.IncludedEntities)
            {
                if (string.IsNullOrWhiteSpace(entry))
                    continue;

                if (entry.StartsWith("assets/", StringComparison.OrdinalIgnoreCase))
                {
                    // Prefab inclusion
                    uint prefabId = entry.ToPrefabID();
                    if (!_excludedPrefabs.Contains(prefabId))
                    {
                        _includedPrefabs.Add(prefabId);
                        if (debug)
                            Puts($"[DEBUG] Included prefab: {entry} ({prefabId})");
                    }
                }
                else
                {
                    bool withSubclasses = entry.EndsWith("*");
                    string typeName = withSubclasses ? entry.TrimEnd('*') : entry;

                    var t = asm.GetType(typeName, false, true);
                    if (t != null)
                    {
                        if (withSubclasses)
                        {
                            AddTypeAndSubclasses(t);
                            if (debug)
                                Puts($"[DEBUG] Included type with subclasses: {t.FullName}");
                        }
                        else
                        {
                            if (!_excludedTypes.Contains(t))
                            {
                                RaidRelevantTypeSet.Add(t);
                                if (debug)
                                    Puts($"[DEBUG] Included type: {t.FullName}");
                            }
                            else if (debug)
                            {
                                Puts($"[DEBUG] Skipped excluded type: {t.FullName}");
                            }
                        }
                    }
                    else if (debug)
                    {
                        Puts($"[DEBUG] Warning: include '{entry}' did not resolve as prefab or type.");
                    }
                }
            }

            if (debug)
            {
                Puts($"[DEBUG] RaidRelevantTypeSet initialized with {RaidRelevantTypeSet.Count} types, " +
                     $"{_includedPrefabs.Count} prefabs.");
                // Print complete list of included types
                foreach (var t in RaidRelevantTypeSet)
                    Puts($"  - {t.FullName}");
            }
        }

        /// <summary>
        /// Adds a base type and all subclasses from the Rust assembly,
        /// with optional hardcoded exclusions, and also respects _excludedTypes.
        /// </summary>
        private void AddTypeAndSubclasses(Type baseType, Type[] exclude = null)
        {
            var asm = typeof(BaseEntity).Assembly;
            foreach (var t in asm.GetTypes())
            {
                if (t == baseType || t.IsSubclassOf(baseType))
                {
                    // Skip hardcoded exclusions (like LootContainer)
                    if (exclude != null)
                    {
                        bool skip = false;
                        for (int i = 0; i < exclude.Length; i++)
                        {
                            if (t == exclude[i] || t.IsSubclassOf(exclude[i]))
                            {
                                skip = true;
                                if (debug) Puts($"[DEBUG] Skipping excluded subclass: {t.FullName}");
                                break;
                            }
                        }
                        if (skip) continue;
                    }

                    if (!_excludedTypes.Contains(t))
                        RaidRelevantTypeSet.Add(t);
                }
            }
        }

        // Precise: is the attacker unauthed on the very TC that governs this entity?
        private bool IsAttackerUnauthedOnEntityTC(ulong attackerId, BaseCombatEntity entity)
        {
            if (entity == null) return false;

            // Ignore unowned (common-sense rule you requested)
            ulong ownerId = entity.OwnerID;
            if (ownerId == 0UL) return false;

            // “Governing” TC for this exact entity position
            var tc = entity.GetBuildingPrivilege() as BuildingPrivlidge;
            if (tc == null) return false;

            // Attacker must NOT be authed on THIS TC
            return !HasAuth(tc, attackerId);
        }

        // Helper that works across Rust versions (uses native method if present, otherwise scans the set/list)
        private static bool HasAuth(BuildingPrivlidge tc, ulong userId)
        {
            if (tc == null) return false;

            // Preferred fast path — use the engine helper if present
            try { return tc.IsAuthed(userId); }
            catch { /* fall through for older builds */ }

            var apObj = (object)tc.authorizedPlayers;
            if (apObj == null) return false;

            // Common modern shapes
            if (apObj is HashSet<ulong> hs)
                return hs.Contains(userId);

            if (apObj is List<ulong> ul)
                return ul.Contains(userId);

            if (apObj is IEnumerable<ulong> eul)
            {
                foreach (var id in eul)
                    if (id == userId) return true;
                return false;
            }

            // Legacy/unknown shape: scan and try to read a 'userid' ulong via reflection
            foreach (var e in (IEnumerable)apObj)
            {
                if (e == null) continue;
                var t = e.GetType();

                var fld = t.GetField("userid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (fld != null && fld.FieldType == typeof(ulong))
                {
                    if ((ulong)fld.GetValue(e) == userId) return true;
                    continue;
                }

                var prop = t.GetProperty("userid", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (prop != null && prop.PropertyType == typeof(ulong))
                {
                    if ((ulong)prop.GetValue(e, null) == userId) return true;
                }
            }

            return false;
        }

        #endregion

        #region HANDLE PVP

        private void HandlePlayerDamage(BasePlayer attacker, BasePlayer victim, HitInfo info, float totalDamagePvP)
        {
            // Defensive null checks
            if (attacker == null || victim == null || info == null || victim.net == null) return;

            // If this damage is our reflection, consume and fast-path to “allow/ignore” logic to avoid loops
            if (ConsumeReflectionToken(victim.userID, attacker.userID)) return;

            // Nothing to do. Damage to victim is allowed hence not modified. Return.
            if (!enableReflectionPVP && !enableForgiveness && enableDamageToVictim) return;

            // Optional allow-lists (evaluate only after attacker gate)
            if ((IsPlayerInZoneManagerZone(victim) && IsPlayerInZoneManagerZone(attacker)) ||
                (IsPlayerInRaidableZone(victim) && IsPlayerInRaidableZone(attacker)) ||
                (IsPlayerInDynamicZone(victim) && IsPlayerInDynamicZone(attacker)))
                return;

            // Prepare relevant checks, hit region, punishment and damage amount to reflect
            var isCurrentHitForgivable = false;                 // mainly how to treat headshots
            var strikeLimitReached = false;                     // strikes and limits
            var hitRegion = info.isHeadshot ? "head" : "body";  // headshot or body damage?
            var damageToReflect = ComputeReflectionDamage(attacker, victim, info, totalDamagePvP);
            var killPlayer = attacker.health - damageToReflect <= 0f; // predict player death
            var attackerName = GetPlayerName(attacker.userID);  // get the attacker's sanitized player name
            var victimName = GetPlayerName(victim.userID);      // get the victim's sanitized player name
            var victimPos = FormatPosition(victim.transform.position); // get the victim player's position
            var message = string.Empty;                         // initialize variable
            var webhook = string.Empty;                         // initialize variable

            // Check global configuration whether to allow damage to victim, overruled by CanEntityTakeDamage
            if (!enableDamageToVictim || pvePluginInstalled)
            {
                if (debug)
                {
                    var reason = pvePluginInstalled ? "PvE plugin" : "Global setting";
                    Puts($"[DEBUG] {reason} blocked damage to {victimName} @{victimPos}, nullified.");
                }
                info.damageTypes.ScaleAll(0f); // force no victim damage
                info.HitEntity = null;
                info.DoHitEffects = false;
                info.PointStart = Vector3.zero;
                info.PointEnd = Vector3.zero;
            }

            // Check whether strikes are available and need to be added
            if (enableForgiveness)
            {
                strikeLimitReached = !AddStrike(attacker.userID) || GetStrikeCount(attacker.userID) == 0;

                // Notify Discord
                webhook = _config?.Discord?.pvpViolationWebhook ?? string.Empty;
                message = $"`{attackerName}` got a strike for attacking `{victimName}` @{victimPos} (hit: {hitRegion}) with their `{GetWeaponShortName(info)}`. Strike limit reached: `{strikeLimitReached}`.";
                if (!string.IsNullOrWhiteSpace(webhook)) SendDiscordMessage(webhook, message);
            }

            // Check if we need to notify attacker and / or victim
            if (debug)
            {
                if (killPlayer)
                    Puts($"[DEBUG] Since {attackerName} would be determined to die from their punishment, we're neither informing attacker nor victim.");
                else if (enableForgiveness && !strikeLimitReached)
                {
                    if (warnAttackerWhileForgivable)
                        Puts($"[DEBUG] Would inform {attackerName} that they reached strike {GetStrikeCount(attacker.userID)}/{forgivableStrikesMax} for attacking player {victimName}.");
                    if (warnVictimWhileForgivable)
                        Puts($"[DEBUG] The victim would be informed about the attack.");
                }
            }
            else
            {
                if (enableForgiveness && !strikeLimitReached && !killPlayer)
                {
                    if (warnAttackerWhileForgivable)
                        attacker.ChatMessage(Lang("pvpWarnAttacker", attacker.UserIDString, GetStrikeCount(attacker.userID), forgivableStrikesMax));
                    if (warnVictimWhileForgivable)
                        victim.ChatMessage(Lang("pvpWarnVictim", victim.UserIDString, attackerName));
                }
            }

            // we need to capture monument zones before applying a punishment
            var attackerInMonumentZone = IsPlayerInMonumentZone(attacker);
            var victimInMonumentZone = IsPlayerInMonumentZone(victim);

            // Does a PVE implementation limit damage reflection?
            // We can still hurt or kill the attacker!
            if (damageToReflect > 0f)
            {
                // Delegate to punishment function
                if (!debug)
                    ApplyReflectionPunishment(attacker, victim, DamageType.Generic, damageToReflect, info.HitBone);
            }

            // Apply bleeding if configured and attacker survived punishment
            if (applyBleeding && attacker.IsAlive())
            {
                if (debug)
                    Puts($"[DEBUG] Would apply bleeding {bleedingIntensity} to {attackerName}");
                else
                    attacker.metabolism.bleeding.Add(bleedingIntensity);
            }
            else if (debug)
                Puts($"[DEBUG] Bleeding not applied | Alive: {attacker.IsAlive()} | Enabled: {applyBleeding}");

            // Auto-Kick is configured
            if (enableAutoKick && (strikeLimitReached || killPlayer))
            {
                if (attackerInMonumentZone && victimInMonumentZone && IsSameTeam(attacker, victim))
                {
                    if (debug)
                        Puts($"[DEBUG] Not kicking {attackerName} because it is team friendly fire inside monument bounds");
                }
                else
                {
                    if (debug)
                        Puts($"[DEBUG] Would kick {attackerName} for exceeding maximum allowed strike count or dying from reflection");
                    else
                    {
                        attacker.Kick(Lang("pvpAutoKick", null, victimName));
                        Puts($"Attacker in monument: {attackerInMonumentZone} | Victim in monument: {victimInMonumentZone} | Both in same team: {IsSameTeam(attacker, victim)}");

                        // Notify Discord
                        webhook = _config?.Discord?.pvpAutoKickWebhook ?? string.Empty;
                        message = $"`{attackerName}` was kicked for attacking `{victimName}` @{victimPos} (hit: {hitRegion}) with their `{GetWeaponShortName(info)}`.";
                        if (!string.IsNullOrWhiteSpace(webhook)) SendDiscordMessage(webhook, message);
                    }
                }
            }
        }

        private static string GetWeaponShortName(HitInfo info)
        {
            return info?.Weapon?.ShortPrefabName
                ?? info?.WeaponPrefab?.ShortPrefabName
                ?? "unknown";
        }

        /// <summary>
        /// Applies reflection punishment: victim is credited as killer, attacker takes damage.
        /// Weapon is shown as "Damage Reflection".
        /// </summary>
        private void ApplyReflectionPunishment(BasePlayer attacker, BasePlayer victim, DamageType damageType, float damage, uint hitBone)
        {
            // reentrancy guards (cheap field/lookup work)
            if (currentlyProcessing.Contains(attacker) || currentlyProcessing.Contains(victim)) return;

            // The fact that we are here means: attacker and victim have been verified, and there is damage to reflect. Let's go!
            var attackerName = GetPlayerName(attacker.userID);  // get the attacker's sanitized player name
            var victimName = GetPlayerName(victim.userID);      // get the victim's sanitized player name
            var damageMethod = string.Empty;                    // determine damage reflection method

            try
            {
                currentlyProcessing.Add(attacker);
                currentlyProcessing.Add(victim);

                // If debug is true don't apply damage, only log.
                if (debug)
                    Puts($"[DEBUG] Would apply {damage:F1} reflected damage to {attackerName} (current health {attacker.health:F1}) from victim {victimName}");
                // debug is false, let's kick some butt ...
                else
                {
                    // Health before punishment
                    var healthBefore = attacker.health;

                    // We can reflect damage only if no PVE plugin prevents player-to-player damage ... and we know the victim
                    if (!pvePluginInstalled || pveHookDetected)
                    {
                        damageMethod = "Reflect";
                        // Add one-shot bypass for our reflected hit since we decided to reflect with credit to victim
                        AddReflectionToken(attacker.userID, victim.userID);
                        attacker.Hurt(damage, DamageType.Generic, victim);
                    }

                    // OnAttack() failed → fall back to generic Hurt without attribution
                    if (attacker.IsAlive() && Mathf.Max(0f, healthBefore - attacker.health) <= 0.05f)
                    {
                        damageMethod = "Hurt";
                        attacker.Hurt(damage, DamageType.Generic, null);
                    }

                    // Hurt() failed → fall back to Die()
                    if (attacker.IsAlive() && damage >= healthBefore && Mathf.Max(0f, healthBefore - attacker.health) <= 0.05f)
                    {
                        damageMethod = "Die";
                        attacker.Die();
                    }

                    if (attacker.IsAlive() && Mathf.Max(0f, healthBefore - attacker.health) <= 0.05f)
                        damageMethod = "Fail";

                    Puts($"[PUNISH] Damage applied: {attackerName} now at {attacker.health:F1} after {damage:F1} reflected by {victimName} with method {damageMethod}");
                }
            }
            catch (Exception ex)
            {
                PrintError($"Error applying reflection punishment: {ex}");
            }
            finally
            {
                currentlyProcessing.Remove(attacker);
                currentlyProcessing.Remove(victim);
            }
        }

        #endregion

        #region HANDLE ENTITY

        private enum RaidSeverity { None, Ignore, Strike, Ban }
        private static bool IsBuildingBlock(BaseCombatEntity ent) =>
            ent is SimpleBuildingBlock ||
            ent is BuildingBlock ||
            ent is IOEntity ||
            ent is Door;
        private static bool IsToolCupboard(BaseCombatEntity ent) =>
            ent is BuildingPrivlidge;

        private void HandleEntityDamage(BasePlayer attacker, BaseCombatEntity victim, HitInfo info, float totalDamage)
        {
            // 1) Assess severity first — ban logic must always run
            var severity = GetRaidSeverity(victim, info);

            // 2) Nullify victim damage if rules forbid it
            if (!enableDamageToVictim || pvePluginInstalled)
            {
                if (debug)
                {
                    var reason = pvePluginInstalled ? "PvE plugin" : "Global setting";
                    Puts($"[DEBUG] {reason} blocked damage to entity {victim.ShortPrefabName}, nullified.");
                }
                info.damageTypes.ScaleAll(0f);
            }

            // 3) Prepare names (fixed variable name)
            ulong ownerId = victim.OwnerID;                                         // get the entity owner's
            var ownerName = ownerId != 0UL ? GetPlayerName(ownerId) : "unknown";    // sanitized player name
            var victimPos = FormatPosition(victim.transform.position); // get the damaged entity's position
            var attackerName = GetPlayerName(attacker.userID);  // get the attacker's sanitized player name
            var victimKind = victim.ShortPrefabName;            // get short prefab name of damaged entity
            var message = string.Empty;                         // initialize variable
            var webhook = string.Empty;                         // initialize variable

            switch (severity)
            {
                case RaidSeverity.Ignore:
                    {
                        // Light poke on building blocks → warn only
                        if (!string.IsNullOrEmpty(ownerName))
                            SendReply(attacker, Lang("raidIgnore", attacker.UserIDString, ownerName));
                        if (debug) Puts($"[DEBUG] Ignore: {attackerName} lightly hit {victimKind} (owner {ownerName}) @{victimPos}.");
                        return;
                    }

                case RaidSeverity.Strike:
                    {
                        // Non‑explosive damage to deployables → strike, then kick when exceeded (shared limits)
                        bool strikeLimitReached = enableForgiveness ? !AddStrike(attacker.userID) : true;

                        if (strikeLimitReached)
                        {
                            if (enableAutoKick || !enableForgiveness)
                            {
                                if (!debug) attacker.Kick(Lang("raidAutoKick", null));
                                else Puts($"[DEBUG] Kick: {attackerName} attacked deployable {victimKind} of {ownerName} @{victimPos}.");
                                return;
                            }
                        }

                        // Notify Discord
                        webhook = _config?.Discord?.entityViolationWebhook ?? string.Empty;
                        message = $"`{attackerName}` got a strike for damaging `{ownerName}`'s {victimKind} with their `{GetWeaponShortName(info)}` @{victimPos}. Strike limit reached: `{strikeLimitReached}`.";
                        if (!string.IsNullOrWhiteSpace(webhook)) SendDiscordMessage(webhook, message);

                        // Inform about the strike
                        if (enableForgiveness && warnAttackerWhileForgivable)
                            SendReply(attacker, Lang("raidStrike", attacker.UserIDString, GetStrikeCount(attacker.userID), forgivableStrikesMax));
                        if (debug) Puts($"[DEBUG] Strike: {attackerName} attacked deployable {victimKind} @{victimPos} of {ownerName}.");
                        return;
                    }

                case RaidSeverity.Ban:
                    {
                        // Explosive/incendiary or TC → temp ban (or kick if disabled)
                        int hours = Math.Max(0, (int)raidTempBanHours);
                        if (enableAutoBan && hours > 0)
                        {
                            var reason = Lang("raidAutoBan", attacker.UserIDString, hours);
                            var command = $"banid {attacker.UserIDString} \"{attackerName}\" \"{reason}\" {hours}";
                            // Try ban
                            try { ConsoleSystem.Run(ConsoleSystem.Option.Server.Quiet(), command); }
                            catch (Exception ex) { PrintError($"[ERROR] Failed to temp-ban {attackerName}: {ex}"); }

                            // Notify Discord
                            webhook = _config?.Discord?.raidAutoBanWebhook ?? string.Empty;
                            message = $"`{attackerName}` was temp-banned for damaging `{ownerName}`'s {victimKind} @{victimPos} with their `{GetWeaponShortName(info)}`.";
                            if (!string.IsNullOrWhiteSpace(webhook)) SendDiscordMessage(webhook, message);
                        }
                        else
                        {
                            // Ban disabled → strong fallback: immediate kick
                            if (!debug) attacker.Kick(Lang("raidAutoKick", null));
                            else Puts($"[DEBUG] Kick: {attackerName} ({attacker.UserIDString}) severe damage on {ownerName}'s {victimKind} @{victimPos}.");

                            // Notify Discord
                            webhook = _config?.Discord?.raidAutoKickWebhook ?? string.Empty;
                            message = $"`{attackerName}` was kicked (ban disabled) for damaging `{ownerName}`'s {victimKind} @{victimPos} with their `{GetWeaponShortName(info)}`.";
                            if (!string.IsNullOrWhiteSpace(webhook)) SendDiscordMessage(webhook, message);
                        }
                        return;
                    }

                default:
                    return;
            }
        }

        // "Deployable" for our purpose: owned, decays, but not BuildingBlock/TC (e.g., boxes, workbenches, furnaces, doors, etc.)
        private static bool IsDeployable(BaseCombatEntity ent)
        {
            if (ent is BuildingBlock) return false;
            if (ent is BuildingPrivlidge) return false;
            return ent is DecayEntity; // broad but effective for player-placed entities
        }

        // Explosive/incendiary signals: damage types first (robust), then common ammo/prefab shortnames.
        private static readonly HashSet<string> ExplIncAmmo = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "ammo.rifle.explosive",
            "ammo.rifle.incendiary",
            "ammo.pistol.fire",
            "ammo.shotgun.fire",
            "ammo.rocket.basic",
            "ammo.rocket.hv",
            "ammo.rocket.fire",
            "ammo.grenadelauncher.he",
            "ammo.grenadelauncher.fire",
        };

        // Safe prefixes for occasional variants we may see as ShortPrefabName
        // (Some servers/mods append ".entity" or create " (Clone)" variants)
        private static readonly string[] ExplosivePrefabHints =
        {
            "explosive.timed",   // covers "explosive.timed.deployed"
            "explosive.satchel", // covers "explosive.satchel.deployed"
            "grenade.f",         // covers "grenade.flashbang" and "grenade.f1"
            "rocket_",           // covers "rocket_basic", "rocket_hv", "rocket_mlrs"
            "survey_charge"      // let's not forget the survey_charge explosives
        };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string NormalizeShortName(string s)
        {
            if (string.IsNullOrEmpty(s)) return s;
            int idx = s.IndexOf(" (Clone)", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0) s = s.Substring(0, idx);
            if (s.EndsWith(".entity", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(0, s.Length - ".entity".Length);
            return s;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool HasExplosiveHint(string shortName)
        {
            if (string.IsNullOrEmpty(shortName)) return false;
            var n = NormalizeShortName(shortName);
            for (int i = 0; i < ExplosivePrefabHints.Length; i++)
            {
                if (n.IndexOf(ExplosivePrefabHints[i], StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return false;
        }

        private static bool IsExplosiveOrIncendiary(HitInfo info)
        {
            // 1. Damage types
            var types = info.damageTypes;
            if (types != null && (types.Has(DamageType.Explosion) || types.Has(DamageType.Heat)))
                return true;

            // 2. Ammo shortname (mostly bullets)
            var ammoShort = info.Weapon?.GetItem()?.info?.shortname;
            if (!string.IsNullOrEmpty(ammoShort) && ExplIncAmmo.Contains(ammoShort))
                return true;

            // 2b. Special case: rockets (launcher doesn't expose ammo shortname)
            if (info.ProjectilePrefab != null)
            {
                // Check the projectile's ItemDefinition via its prefab name
                var projectileName = info.ProjectilePrefab.name;
                var itemDef = ItemManager.FindItemDefinition(projectileName);
                var rocketAmmoShort = itemDef?.shortname;

                if (!string.IsNullOrEmpty(rocketAmmoShort) && ExplIncAmmo.Contains(rocketAmmoShort))
                    return true;
            }

            // 3. Weapon prefab
            var weaponPrefab = NormalizeShortName(info.WeaponPrefab?.ShortPrefabName);
            if (HasExplosiveHint(weaponPrefab))
                return true;

            // 4. Projectile prefab
            var projPrefab = NormalizeShortName(info.ProjectilePrefab?.name);
            if (HasExplosiveHint(projPrefab))
                return true;

            // 5. Initiator entity
            var initiatorPrefab = NormalizeShortName((info.Initiator as BaseEntity)?.ShortPrefabName);
            if (HasExplosiveHint(initiatorPrefab))
                return true;

            return false;
        }

        private RaidSeverity GetRaidSeverity(BaseCombatEntity victim, HitInfo info)
        {
            // Vital: TC or any explosive/incendiary damage → Ban
            if (IsToolCupboard(victim) || IsExplosiveOrIncendiary(info))
                return RaidSeverity.Ban;

            // Light hits to structural blocks → Ignore
            if (IsBuildingBlock(victim))
                return RaidSeverity.Ignore;

            // Deployables (non‑explosive) → Strike
            if (IsDeployable(victim))
                return RaidSeverity.Strike;

            // Fallback: treat unknown owned things as Strike to be on the safe side
            return RaidSeverity.Strike;
        }

        #endregion

        #region CanEntityTakeDamage

        // Hook: exposed to the ecosystem. Keep it tiny; do not allocate or log in hot path.
        private object CanEntityTakeDamage(BaseCombatEntity victim, HitInfo info)
        {
            // 1) Sanity + feature gates
            if (!enablePveHook || victim == null || info == null) return null;

            // - ignore fun "weapons" watergun and waterpistol, but block damage
            if (info?.WeaponPrefab != null)
            {
                var w = info.WeaponPrefab.ShortPrefabName;
                if (w == "waterpistol.entity" || w == "watergun.entity")
                    return false; // block damage
            }

            // 2) Tripwire: detect once (no allocs/logging)
            if (!pveHookDetected)
            {
                // Challenge the server owner’s setting once; prefer safety.
                if (!pvePluginInstalled) pvePluginInstalled = true;
                pveHookDetected = true;
            }

            // 3) Extract attacker once, then cheapest exits first
            var attacker = info.InitiatorPlayer;
            if (!IsRealPlayer(attacker)) return null;                   // only care about real human attackers
            if (!attacker.IsAlive()) return null;                       // dead attacker? Ignore
            if (ReferenceEquals(attacker, victim)) return null;         // self-hit (splash, etc.), skip
            if (victim.IsDestroyed) return null;                        // ignore destroyed entity
            if (HasBypass(attacker)) return true;                       // admin may damage anything

            // 4) Compute damage total ONCE and branch
            float total = info.damageTypes.Total();
            if (total <= 0f) return null;

            // 5) Branch A: PvP (attacker = player, victim = player)
            var vPlayer = victim as BasePlayer;
            if (IsRealPlayer(vPlayer))
            {   // reflected-hit shortcut: allow, but do NOT consume here
                if (PeekReflectionToken(vPlayer.userID, attacker.userID))
                    return true;    // allow this specific damage event

                // bail out if this is a Raidable Base event
                if (IsPlayerInRaidableZone(vPlayer) && IsPlayerInRaidableZone(attacker))
                    return null;

                // Decide if this PvP hit should be blocked
                bool blockPvp = false;

                // Global settings or PvE plugin presence → always block
                if (!enableDamageToVictim || pvePluginInstalled)
                    blockPvp = true;

                // Zones that we want to manage ourselves (no PvP allowed except in special cases)
                else if ((IsPlayerInMonumentZone(vPlayer) && IsPlayerInMonumentZone(attacker)) ||
                         (IsPlayerInDynamicZone(vPlayer) && IsPlayerInDynamicZone(attacker)) ||
                         (IsPlayerInZoneManagerZone(vPlayer) && IsPlayerInZoneManagerZone(attacker)))
                    blockPvp = true;

                if (blockPvp)
                {
                    var damageToReflect = ComputeReflectionDamage(attacker, vPlayer, info, total);
                    AddReflectionIntent(victim.net?.ID.Value ?? 0, attacker.userID, damageToReflect, DamageType.Generic, info.HitBone);
                    return false;   // authoritative block, but remember
                }

                return null;        // no opinion → let others/default handle it
            }

            // 6) Branch B: Entity (attacker = player, victim = non-player entity)
            ulong ownerId = victim.OwnerID;
            if (ownerId != 0UL &&
                IsRaidRelevantEntity(victim) &&
                !IsPlayerInRaidableZone(attacker) &&
                IsAttackerUnauthedOnEntityTC(attacker.userID, victim) &&
                (!enableDamageToVictim || pvePluginInstalled))
            {
                AddReflectionIntent(victim.net?.ID.Value ?? 0, attacker.userID, total, DamageType.Generic, info.HitBone);
                return false;
            }

            // 7) Default: not our concern
            return null;            // no opinion; let others/default handle it
        }

        private float ComputeReflectionDamage(BasePlayer attacker, BasePlayer victim, HitInfo info, float totalDamage)
        {
            var isCurrentHitForgivable = false;                 // mainly how to treat headshots
            var strikeLimitReached = false;
            var damageToReflect = 0f;
            var killPlayer = false;

            // Check whether the player is subject to be killed due to death penalty
            if (enableForgiveness)
            {
                isCurrentHitForgivable = isHeadShotForgivable || !info.isHeadshot;
                strikeLimitReached = GetStrikeCount(attacker.userID) + 1 >= forgivableStrikesMax;
                killPlayer = (!isCurrentHitForgivable || strikeLimitReached) && enableDeathPenalty;
            }

            // Death penalty does not apply. How about damage reflection?
            if (!killPlayer && enableReflectionPVP)
            {
                // Headshots and multiplier handled in global config, multiplier is 1f when disabled
                damageToReflect = totalDamage * (float)headshotMultiplier * (reflectPercentagePVP / 100f);
                // keep it simple. predict death if damage > attacker.health (ignore armor)
                killPlayer = (attacker.health - damageToReflect) <= 0f;
            }

            // The attacker is destined to die - make sure to apply enough damage
            if (killPlayer)
                damageToReflect = Math.Max(1.5f * attacker.health, 50f); // 50f = minimum guaranteed lethal damage

            return damageToReflect;
        }

        private readonly Dictionary<(ulong victimId, ulong initiatorId), float> _reflectionBypass = new Dictionary<(ulong, ulong), float>(64);

        // Add a one-shot bypass for the reflected hit we are about to cause.
        // IMPORTANT: pass the IDs that will actually appear on the reflected HitInfo:
        //   - reflectedVictim: the entity you will damage (usually original attacker)
        //   - reflectedInitiator: the initiator you will set on Hurt/HitInfo (usually original victim to credit them)
        private void AddReflectionToken(ulong reflectedVictimId, ulong reflectedInitiatorId)
        {
            var key = (reflectedVictimId, reflectedInitiatorId);
            _reflectionBypass[key] = Now() + reflectionBypassWindow;

            // Opportunistic cleanup (O(1) avg, bounded by small table)
            if (_reflectionBypass.Count > 256) PruneReflectionToken();
        }

        // Non-destructive check used in CanEntityTakeDamage
        private bool PeekReflectionToken(ulong victimId, ulong initiatorId)
        {
            float expiry;
            return _reflectionBypass.TryGetValue((victimId, initiatorId), out expiry) && Now() <= expiry;
        }

        // Check and consume a bypass token if present and fresh
        private bool ConsumeReflectionToken(ulong victimId, ulong initiatorId)
        {
            var key = (victimId, initiatorId);
            if (_reflectionBypass.TryGetValue(key, out var expiry))
            {
                if (Now() <= expiry)
                {
                    _reflectionBypass.Remove(key);
                    return true;
                }
                _reflectionBypass.Remove(key);
            }
            return false;
        }

        // Remove expired entries (called occasionally)
        private void PruneReflectionToken()
        {
            var now = Now();
            // Avoid allocs: copy keys to a small list once (bounded by small dictionary)
            List<(ulong, ulong)> toRemove = null;
            foreach (var kvp in _reflectionBypass)
                if (kvp.Value < now)
                    (toRemove ??= new List<(ulong, ulong)>(8)).Add(kvp.Key);
            if (toRemove != null)
                for (var i = 0; i < toRemove.Count; i++) _reflectionBypass.Remove(toRemove[i]);
        }

        // Intent = "we blocked this damage on CanEntityTakeDamage and want to handle it later"
        // Without it, we'd tell other plugins to zero the damage, and in OnEntityTakeDamage we
        // wouldn't remember it was a damage we wanted to deal with in the first place.
        private readonly Dictionary<(ulong victimId, ulong attackerId), ReflectionIntent> _reflectionIntent
            = new Dictionary<(ulong, ulong), ReflectionIntent>(64);

        private sealed class ReflectionIntent
        {
            public float Damage;
            public DamageType DamageType;
            public uint HitBone;
            public double ExpireAt;
        }

        private void AddReflectionIntent(ulong victimId, ulong attackerId, float damage, DamageType dmgType, uint hitBone)
        {
            if (victimId == 0 || attackerId == 0 || damage <= 0f) return;

            _reflectionIntent[(victimId, attackerId)] = new ReflectionIntent
            {
                Damage = damage,
                DamageType = dmgType,
                HitBone = hitBone,
                ExpireAt = Now() + 0.25 // ~250ms window
            };

            if (_reflectionIntent.Count > 256) PruneReflectionIntent();
        }

        private ReflectionIntent ConsumeReflectionIntent(ulong victimId, ulong attackerId)
        {
            var key = (victimId, attackerId);
            if (_reflectionIntent.TryGetValue(key, out var intent))
            {
                if (Now() <= intent.ExpireAt)
                {
                    _reflectionIntent.Remove(key);
                    return intent;
                }
                _reflectionIntent.Remove(key);
            }
            return null;
        }

        private void PruneReflectionIntent()
        {
            var now = Now();
            List<(ulong, ulong)> toRemove = null;
            foreach (var kvp in _reflectionIntent)
            {
                if (kvp.Value.ExpireAt < now)
                    (toRemove ??= new List<(ulong, ulong)>(8)).Add(kvp.Key);
            }
            if (toRemove != null)
                for (var i = 0; i < toRemove.Count; i++) _reflectionIntent.Remove(toRemove[i]);
        }

        #endregion

        #region STRIKES

        // in-memory strikes; not persisted across server restarts
        private readonly Dictionary<ulong, StrikeRecord> _strikes = new Dictionary<ulong, StrikeRecord>();

        private sealed class StrikeRecord
        {
            public int StrikeCount;     // Current number of active strikes
            public double LastStrikeAt; // Time of last strike in seconds
        }

        /// <summary>
        /// Adds a strike for the given player.
        /// - Before adding, applies decay logic (forgivableStrikesDecay in minutes).
        /// - Returns true if the strike was added and the total remains below forgivableStrikesMax.
        /// - Returns false if the player reached or exceeded forgivableStrikesMax (strikes reset automatically).
        /// </summary>
        private bool AddStrike(ulong steamId)
        {
            // Ensure the player has a record
            if (!_strikes.TryGetValue(steamId, out var rec))
            {
                rec = new StrikeRecord();
                _strikes[steamId] = rec;
            }

            // Current time in seconds
            var now = Now();

            // Apply decay based on elapsed time
            DecayIfElapsed(ref rec.StrikeCount, ref rec.LastStrikeAt, now, (int)forgivableStrikesDecay);

            // Increment strike count and update timestamp
            rec.StrikeCount++;
            rec.LastStrikeAt = now;

            // Check threshold
            if (rec.StrikeCount >= forgivableStrikesMax)
            {
                // Reset after reaching/exceeding max
                rec.StrikeCount = 0;
                rec.LastStrikeAt = now;
                return false;
            }

            return true;
        }

        // Returns the current strike count for a player (0 if none recorded yet)
        private int GetStrikeCount(ulong steamId)
        { return _strikes.TryGetValue(steamId, out var rec) ? rec.StrikeCount : 0; }

        /// <summary>
        /// Resets strike count if enough time has elapsed since the last strike.
        /// - nowSeconds: current time in seconds (UnityEngine.Time.realtimeSinceStartup).
        /// - resetMinutes: decay threshold in minutes (forgivableStrikesDecay).
        /// </summary>
        private static void DecayIfElapsed(ref int count, ref double lastAt, float nowSeconds, int resetMinutes)
        {
            if (count <= 0 || resetMinutes <= 0) return;
            // Initialize timestamp if it was never set
            if (lastAt <= 0)
            {
                lastAt = nowSeconds;
                return;
            }

            // Calculate elapsed time in minutes
            var elapsedMinutes = (nowSeconds - lastAt) / 60.0;
            if (elapsedMinutes >= resetMinutes)
            {
                count = 0;
                lastAt = nowSeconds;
            }
        }

        #endregion

        #region FORBIDDEN DEPLOYABLES

        private HashSet<string> _forbiddenDeployables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private void InitForbiddenDeployables()
        {
            _forbiddenDeployables.Clear();
            if (!enableForbiddenDeployables) return;

            foreach (var shortname in _config.ForbiddenDeployables.forbiddenDeployables)
            {
                if (string.IsNullOrWhiteSpace(shortname)) continue;
                _forbiddenDeployables.Add(shortname.Trim());
            }

            if (debug)
                Puts($"[DEBUG] Forbidden deployables loaded: {_forbiddenDeployables.Count}");
        }

        private void OnEntityBuilt(Planner plan, GameObject go)
        {
            if (!enableForbiddenDeployables) return;
            var player = plan?.GetOwnerPlayer();

            if (!IsRealPlayer(player)) return;
            var playerPos = FormatPosition(player.transform.position);

            var entity = go.ToBaseEntity();
            if (entity == null) return;

            var shortname = entity?.ShortPrefabName;
            if (string.IsNullOrEmpty(shortname)) return;

            if (!_forbiddenDeployables.Contains(shortname)) return;

            // Check placement context: is player authed on governing TC?
            var tc = entity.GetBuildingPrivilege() as BuildingPrivlidge;
            bool authed = tc != null && HasAuth(tc, player.userID);

            if (!authed)
            {
                // Outside own TC → schedule destroy
                NextTick(() => { if (entity != null && !entity.IsDestroyed) entity.Kill(); });
                player.ChatMessage(Lang("forbiddenRemoved", player.UserIDString, shortname));

                // Notify Discord
                var webhook = _config?.Discord?.forbiddenDeployablesWebhook ?? string.Empty;
                var message = $"`{player.displayName}` tried to place a stray `{entity}` @{playerPos} - the item was removed!";
                if (!string.IsNullOrWhiteSpace(webhook)) SendDiscordMessage(webhook, message);
            }
            else
                // Inside own TC → allow, but warn
                player.ChatMessage(Lang("forbiddenReminder", player.UserIDString, shortname));
        }

        #endregion

        #region F7 REPORTS

        // Hook signature confirmed by uMod staff.
        // void OnPlayerReported(BasePlayer reporter, string targetName, string targetId, string subject, string message, string reportType)
        private void OnPlayerReported(BasePlayer reporter, string targetName, string targetId, string subject, string body, string reportType)
        {
            if (!(_config?.General?.enableF7Reports ?? false)) return;

            try
            {
                var webhook = _config?.Discord?.f7ReportsWebhook ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(webhook))
                {
                    SendDiscordMessage(webhook, BuildPlayerReportMessage(reporter, targetName, targetId, subject, body, reportType));
                    Puts($"[F7 Report] {reporter.displayName} -> {targetName} | {subject} | {body} | Type: {reportType}");
                }
            }
            catch (Exception e) { PrintWarning($"OnPlayerReported formatting failed: {e}"); }
        }

        private string BuildPlayerReportMessage(BasePlayer reporter, string targetName, string targetId, string subject, string body, string reportType)
        {
            // Resolve some optional context
            ulong targetSteamId;
            ulong.TryParse(targetId, out targetSteamId);

            string reporterName = reporter?.displayName ?? "Unknown";
            string reporterId = reporter?.UserIDString ?? "0";
            string pos = reporter != null ? FormatPosition(reporter.transform.position) : "n/a";

            // Keep it Discord-friendly and compact
            var sb = new StringBuilder(256);
            sb.AppendLine($"**[F7 Player Report]** Type: `{reportType}`");
            sb.AppendLine($"Reporter: `{reporterName}` ({reporterId})");
            sb.AppendLine($"Target: `{targetName}` ({targetId})");
            if (!string.IsNullOrEmpty(subject)) sb.AppendLine($"Subject: `{subject}`");
            if (!string.IsNullOrEmpty(body)) sb.AppendLine($"Message: `{body}`");
            sb.AppendLine($"> Pos: {pos}");
            sb.AppendLine($"> Time: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            return sb.ToString();
        }

        // Hook signature according to Carbon documentation. Requires "using Facepunch.Models"
        // void OnFeedbackReported(BasePlayer player, string subject, string message, ReportType type)
        private void OnFeedbackReported(BasePlayer reporter, string subject, string message, ReportType type)
        {
            if (!(_config?.General?.enableF7Reports ?? false)) return;

            try
            {
                string webhook = _config?.Discord?.f7ReportsWebhook ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(webhook))
                {
                    SendDiscordMessage(webhook, BuildFeedbackMessage(reporter, subject, message, type));
                    Puts($"[F7 Feedback] {reporter.displayName} | Subject: {subject} | Message: {message} | Type: {type}");
                }

            }
            catch (Exception e) { PrintWarning($"OnFeedbackReported formatting failed: {e}"); }
        }

        private string BuildFeedbackMessage(BasePlayer reporter, string s1, string s2, ReportType s3)
        {
            string reporterName = reporter?.displayName ?? "Unknown";
            string reporterId = reporter?.UserIDString ?? "0";
            string typeStr = string.IsNullOrEmpty(s3.ToString()) ? "Unknown" : s3.ToString();
            string pos = reporter != null ? FormatPosition(reporter.transform.position) : "n/a";

            var sb = new StringBuilder(256);
            sb.AppendLine($"**[F7 Feedback]** Type: `{typeStr}`");
            sb.AppendLine($"Reporter: `{reporterName}` ({reporterId})");
            if (!string.IsNullOrEmpty(s1)) sb.AppendLine($"Subject: `{s1}`");
            if (!string.IsNullOrEmpty(s2)) sb.AppendLine($"Message: `{s2}`");
            sb.AppendLine($"> Pos: {pos}");
            sb.AppendLine($"> Time: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            return sb.ToString();
        }

        #endregion

        #region TC AUTH GUARD

        object OnCupboardAuthorize(BuildingPrivlidge tc, BasePlayer player)
        {
            if (!enableTcAuthGuardProtection || tc == null || HasBypass(player)) return null;

            // Allow initial owner authorization on a freshly placed TC (auth list empty)
            if ((tc.authorizedPlayers == null || tc.authorizedPlayers.Count == 0) &&
                tc.OwnerID != 0UL && player.userID == tc.OwnerID) return null;

            if (IsCupboardLocked(tc)) return null;  // locked: vanilla behavior allowed
            Notify(player, this); return true;      // no lock: block new auths
        }

        object OnCupboardClearList(BuildingPrivlidge tc, BasePlayer player)
        {
            if (!enableTcAuthGuardProtection || tc == null || HasBypass(player) || IsCupboardLocked(tc)) return null;
            Notify(player, this); return true;      // no lock: block de-auths
        }

        object OnCupboardDeauthorize(BuildingPrivlidge tc, BasePlayer player)
        {
            if (!enableTcAuthGuardProtection || tc == null || HasBypass(player) || IsCupboardLocked(tc)) return null;
            Notify(player, this); return true;      // no lock: block auth clearing
        }

        bool HasBypass(BasePlayer player) =>
            player != null && enableBypassPermission && permission.UserHasPermission(player.UserIDString, PermBypass);
        static bool IsCupboardLocked(BuildingPrivlidge tc)
        {
            var lockEntity = tc.GetSlot(BaseEntity.Slot.Lock) as BaseLock;
            return lockEntity != null && lockEntity.IsLocked();
        }

        private static void Notify(BasePlayer player, FARDamageReflection plugin)
        {
            if (player == null || plugin == null) return;
            player.ChatMessage(plugin.Lang("TcAuthGuard", player.UserIDString));
        }

        #endregion

        #region HELPERS

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

        private bool IsSameTeam(BasePlayer player1, BasePlayer player2)
        {
            return player1 != null && player2 != null &&
                   player1.currentTeam == player2.currentTeam &&
                   player1.currentTeam != 0;
        }

        [ChatCommand("drcheck")]
        private void CmdDrCheck(BasePlayer player, string command, string[] args)
        {
            if (player == null || !HasBypass(player)) return;

            // Send to player
            SendReply(player, DamageReflectionStatus());
        }

        private string DamageReflectionStatus()
        {
            var enableF7Reports = _config.General.enableF7Reports;
            var hasStrikes = _config.General.forgivableStrikesMax > 0;
            var blockStrayItems = _forbiddenDeployables.Count;
            var raidRelevantTypes = RaidRelevantTypeSet.Count;
            var excludedItemTypes = _excludedTypes.Count;
            var excludedPrefabs = _excludedPrefabs.Count;
            var debugMode = debug ? "punishment disabled; log only" : "punishment enabled";
            var entityMode = (enableAutoBan, enableAutoKick) switch
            {
                (true, true) => $"enabled; Auto-Ban {raidTempBanHours}h; Auto-Kick: on",
                (true, false) => $"enabled; Auto-Ban {raidTempBanHours}h; Auto-Kick: off",
                (false, true) => $"enabled; Auto-Ban {raidTempBanHours}h; Auto-Kick: on",
                (_, _) => $"disabled; Auto-Ban {raidTempBanHours}h; Auto-Kick: off"
            };
            var forgivenessMode = (hasStrikes, enableAutoKick, enableDeathPenalty) switch
            {
                (true, true, true) => "enabled; Auto-Kick on; Death Penalty on",
                (true, true, false) => "enabled; Auto-Kick on; Death Penalty off",
                (true, false, true) => "enabled; Auto-Kick off; Death Penalty on",
                (_, _, _) => "disabled; Reflection only"
            };

            return
                $"<color=#00ffff>Your current DamageReflection configuration:</color>\n" +
                $"{enableReflectionPVP} for PVP damages ({reflectPercentagePVP}% reflection)\n" +
                $"{enableReflectionEntity} for Entity damages ({reflectPercentageEntity}% reflection)\n" +
                $"  -> {entityMode}\n" +
                $"  -> {raidRelevantTypes} Entity types; {excludedItemTypes} excl. types; {excludedPrefabs} excl. prefabs\n" +
                $"{enableForgiveness} for PVP Strike system\n" +
                $"  -> {forgivenessMode}\n" +
                $"  -> {forgivableStrikesMax} max. strikes; {forgivableStrikesDecay} min. until strikes decay\n" +
                $"{warnVictimWhileForgivable} for chat warnings to the victim while forgivable\n" +
                $"{warnAttackerWhileForgivable} for chat warnings to the attacker while forgivable\n\n" +
                $"<color=#00ffff>Operational status of internal Damage handling:</color>\n" +
                $"{enablePveHook} for exposing our own PVE hook\n" +
                $"{pveHookDetected} for our own PVE hook being consumed\n" +
                $"{debug} for Debug mode ({debugMode})\n\n" +
                $"<color=#00ffff>Other features:</color>\n" +
                $"{enableF7Reports} for handling F7 reports\n" +
                $"{enableTcAuthGuardProtection} for TC Lock Auth Guard\n" +
                $"{enableForbiddenDeployables} for handling stray deployables or traps\n" +
                $"  -> {blockStrayItems} blocked deployables in list\n";
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
                RequestMethod.POST,
                new Dictionary<string, string> { ["Content-Type"] = "application/json" }
            );
        }

        private string Lang(string key, string playerId = null, params object[] args)
        {
            var msg = lang.GetMessage(key, this, playerId);
            return args.Length > 0 ? string.Format(msg, args) : msg;
        }

        private static string FormatPosition(Vector3? position)
        {
            if (!position.HasValue) return "n/a";
            var p = position.Value;
            return FormattableString.Invariant($"({p.x:0.0}, {p.y:0.0}, {p.z:0.0})");
        }

        private static bool IsRealPlayer(BasePlayer bp) =>
            bp != null && !bp.IsNpc && bp.userID.IsSteamId();

        // Handle players entering and leaving Abandoned Base Zones
        private void OnPlayerEnteredAbandonedBase(BasePlayer player) =>
            playersInAbandonedBases.Add(player.userID);
        private void OnPlayerExitAbandonedBase(BasePlayer player) =>
            playersInAbandonedBases.Remove(player.userID);

        // Handle players entering and leaving Monument Zones (Monument Owner)
        private void OnPlayerEnteredMonument(BasePlayer player, Vector3 zonePos, float zoneRadius)
        {
            if (player == null) return;
            playersInMonumentZone.Add(player.userID);
        }
        private void OnPlayerExitedMonument(BasePlayer player, Vector3 zonePos, float zoneRadius)
        {
            if (player == null) return;
            playersInMonumentZone.Remove(player.userID);
        }

        // Handle players entering and leaving Raidable Base Zones
        private void OnPlayerEnteredRaidableBase(BasePlayer player) =>
            playersInRaidableBases.Add(player.userID);

        private void OnPlayerExitedRaidableBase(BasePlayer player) =>
            playersInRaidableBases.Remove(player.userID);

        // Handle players entering and leaving dynamic (PVP) Zones
        private bool IsPlayerInZoneManagerZone(BasePlayer player)
        {
            string[] zoneIDs = (string[])ZoneManager?.Call("GetPlayerZoneIDs", player);
            if (zoneIDs != null)
            {
                foreach (string zoneID in zoneIDs)
                    if ((bool)ZoneManager?.Call("IsPlayerInZone", zoneID, player)) return true;
            }
            return false;
        }

        private bool IsPlayerInDynamicZone(BasePlayer player)
        {
            string[] dynamicPVPZones = (string[])DynamicPVP?.Call("AllDynamicPVPZones");
            if (dynamicPVPZones != null)
            {
                foreach (string zoneID in dynamicPVPZones)
                    if ((bool)DynamicPVP?.Call("IsDynamicPVPZone", zoneID) &&
                        (bool)DynamicPVP?.Call("IsPlayerInPVPDelay", player.userID))
                        return true;
            }
            return false;
        }

        private bool IsPlayerInRaidableZone(BasePlayer player) =>
            playersInAbandonedBases.Contains(player.userID) ||
            playersInRaidableBases.Contains(player.userID);

        private bool IsPlayerInMonumentZone(BasePlayer player) =>
            playersInMonumentZone.Contains(player.userID);
    }

    #endregion

    // Keep this inside the Oxide.Plugins namespace so the extension is
    // unambiguously in scope everywhere you use "…".ToPrefabID()
    internal static class FARPrefabExtensions
    {
        public static uint ToPrefabID(this string prefabPath)
        {
            if (string.IsNullOrWhiteSpace(prefabPath))
                return 0u;

            // Only add to StringPool if it, in fact, exists
            var go = GameManager.server?.FindPrefab(prefabPath);
            return go == null ? 0u : StringPool.Get(prefabPath);
        }
    }
}
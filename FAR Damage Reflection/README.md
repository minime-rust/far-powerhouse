# FAR Damage Reflection

## Licensing & Credits
This plugin is a derivative work of "ReflectDamage" by [Chernarust]. The original license is provided in `ORIGINAL-LICENSE.txt`, and further attribution details can be found in `NOTICE.FARDamageReflection.md`. This derivative work is also licensed under the MIT License.

## Description
The **FAR Damage Reflection** plugin stands as the cornerstone of fair play and rule enforcement on PvE Rust servers. It meticulously monitors and manages all forms of player-initiated damage, transforming what are often unenforced rule violations into immediately consequential events. By actively reflecting damage back to the aggressor, integrating a sophisticated rule engine, offering extensive configuration, and providing robust PVE plugin compatibility, FAR Damage Reflection auto-moderates common transgressions like player-to-player aggression and raiding attempts. This plugin not only makes rule violations visible but also ensures that every breach carries an immediate, proportionate, and configurable consequence, significantly reducing staff workload and fostering a respectful server environment.

## The Problem: Unenforceable Rules on PvE Servers
Many PvE Rust servers operate under a strict code of conduct: "no stealing, no killing, no bullying, no griefing, no harassment." However, traditional PvE plugins often disable player-to-player damage outright, creating a vacuum where rule violations go unpunished and unnoticed. Players can shoot at each other with impunity, damage bases without consequence, and undermine the server's rules, leading to frustration for legitimate players and an increasing burden on server staff. This often renders server rules effectively unenforceable.

## The FAR Solution: Consequences and Clarity
**FAR Damage Reflection** fundamentally changes this dynamic. Instead of merely preventing damage, it leverages the damage event itself as a mechanism for enforcement:
*   **Active Deterrence:** Player-to-player damage is **enabled but reflected back to the attacker**. This ensures that aggressors immediately face consequences for their actions.
*   **Visible Violations:** Damage reflection leads to aggressors dying and their deaths being recorded in Discord death logs (if configured), making rule violations undeniable and visible server-wide.
*   **Empowered Community:** Other players witness the consequences of rule-breaking, serving as a deterrent and encouraging them to report persistent offenders, knowing action will be taken.
*   **Automatic Moderation:** The plugin's advanced rule engine automates the detection and punishment of rule violations, reducing the need for constant manual intervention by server staff.

## The Evolution: Beyond Basic Reflection
FAR Damage Reflection is built upon the brilliant foundational work of "ReflectDamage" by Chernarust. We have significantly expanded upon this foundation, transforming it into a comprehensive auto-moderation tool:
*   **Advanced Rule Engine:** A sophisticated system validates and elevates configuration settings, ensuring logical consistency and preventing conflicting rules.
*   **Full PvE Plugin Support:** Seamless integration with existing PvE solutions (like SimplePvE or TruePvE) through full `CanEntityTakeDamage` and `OnEntityTakeDamage` coverage, including a 'tripwire' to confirm hook consumption.
*   **Discord Notifications:** Configurable webhooks for various violation types, keeping staff and the community informed in real-time.
*   **Granular Consequences:** A highly configurable strike system, death penalties, auto-kicks, and even auto-bans for severe raiding offenses.
*   **Comprehensive Monitoring:** Tracks both player-to-player damage and player-to-entity damage, covering common forms of rule-breaking.
*   **Extensive Configuration:** A metric ton of options to fine-tune reflection percentages, headshot multipliers, forgiveness windows, specific entity inclusions/exclusions for raiding, and more.

This is our most complex and ambitious plugin to date, representing a significant achievement in automated server management.

## Changelog

### 1.1.4 - 2025-09-19
*   **Improved Auto-Kick:** Adjusted monument detection to avoid unwanted kicks on friendly fire between team members inside monument boundaries.

## Key Features

**Core Damage Handling:**
*   **Monitors Player-to-Player Damage:** Reflects damage back to the attacker, enforcing "no PvP" rules.
*   **Monitors Player-to-Entity Damage:** Detects and punishes damage to player bases/deployables, enforcing "no raiding" rules.
*   **Configurable Victim Damage:** Option to allow damage to still hit the victim (player or entity).
*   **Headshot Specifics:** Configurable reflection multiplier for headshots and option to make headshots unforgivable.
*   **Bleeding Application:** Option to apply bleeding on top of reflected damage for PvP violations.

**PvE Plugin Integration:**
*   **PvE Hook Exposure:** By default (configurable) exposes our own `CanEntityTakeDamage` hook to effectively manage or override existing PvE plugin logic.
*   **Tripwire Confirmation:** Utilizes a 'tripwire' to verify if its exposed PvE hook is being actively consumed by other plugins, ensuring proper integration.
*   **Assumed PvE Presence:** Can be configured to assume a PvE plugin like SimplePvE is installed, optimizing its behavior. Overridden by tripwire if our hook is exposed and tripwire triggered.

**Strike & Punishment System:**
*   **"Forgiving" Strike System:** Configurable maximum strikes before severe penalties.
*   **Strike Decay:** Strikes can be configured to decay after a set number of minutes, allowing for accidental damages to be forgotten.
*   **Death Penalty:** Administers a death penalty for exceeding strikes or unforgivable actions (e.g., headshots).
*   **Auto-Kick & Auto-Ban:** Configurable automatic kicking or temporary banning of rule violators.
*   **Chat Warnings:** Configurable in-game chat warnings for both attacker and victim while damage is still within forgivable limits.

**Raid Monitoring & Punishment:**
*   **Raid-Relevant Entities:** Configurable list of entities considered "raid relevant" (e.g., `BuildingPrivlidge`, `StorageContainer`, `Door*`) for granular damage tracking.
*   **Severity-Based Raid Punishments:**
    *   **Minor Nicks:** Stray gunshots at walls, windows, or doors are ignored and trigger a friendly warning ("look out, you hit PlayerXY's base").
    *   **Deployable Damage:** Damage to player deployables or loot containers adds a strike (if configured) and is logged.
    *   **Severe Raiding:** Any damage to a player's Tool Cupboard (TC) or the use of explosive/incendiary ammunition against a non-owned base results in an immediate, configurable temporary ban (if configured) or kick.

**Utility & Integration:**
*   **Integrated TC Lock Auth Guard Protection:** Can be enabled to prevent unauthorized building/modifying within TC range (also available as a standalone plugin).
*   **Forbidden Deployables:** Prevents players from placing specific items (e.g., `spikes.floor`, `autoturret`) outside of their TC range, acting as an anti-griefing measure.
*   **Zone Manager Compatibility:** Supports detection of special zones (Zone Manager, Dynamic PvP, Raidable Bases, Abandoned Bases, Monument Owner) to avoid interference. Will not interfere in Raidable Bases or Zone Manager zones, and won't kick players under specific circumstances in Monument Zones.
*   **F7 Report Handling:** Processes both feedback and player reports from the F7 menu, forwarding them to Discord (if webhook configured).

## Smart Rule Engine & Admin Commands

Upon plugin load, all configuration settings are fed into an internal rule engine. This engine validates the configuration, resolves potential conflicts (e.g., mutually exclusive options), and establishes the actual, effective rules for damage reflection and moderation. This ensures that your server's configured rules are always logically consistent and operational.

Administrators (Auth Level 1 or 2, no specific permission required) can inspect the currently applied rules at any time:
*   `/drcheck` (chat command)
    *   **Function:** Outputs a compact summary of the effective Damage Reflection configuration and operational status to the local chat.
    *   **Example Output:**
        ```
        Your current DamageReflection configuration:
        True for PVP damages (100% reflection)
        True for Entity damages (100% reflection)
          -> enabled; Auto-Ban 0h; Auto-Kick: on
          -> 183 Entity types; 9 excl. types; 0 excl. prefabs
        True for PVP Strike system
          -> enabled; Auto-Kick on; Death Penalty on
          -> 3 max. strikes; 20 min. until strikes decay
        True for chat warnings to the victim while forgivable
        True for chat warnings to the attacker while forgivable

        Operational status of internal Damage handling:
        True for exposing our own PVE hook
        True for our own PVE hook being consumed
        False for Debug mode (punishment enabled)

        Other features:
        True for handling F7 reports
        True for TC Lock Auth Guard
        True for handling stray deployables or traps
          -> 10 blocked deployables in list
        ```

## Discord Integration

FAR Damage Reflection offers several optional Discord webhooks, allowing you to route specific notifications to different channels:
*   `pvpViolationWebhook`
*   `entityViolationWebhook`
*   `raidAutoBanWebhook`
*   `raidAutoKickWebhook`
*   `pvpAutoKickWebhook`
*   `f7ReportsWebhook`
*   `forbiddenDeployablesWebhook`

## Caveats: Ensuring Correct Operation

**Crucial Load Order:**
For "FAR Damage Reflection" to function correctly, it is **imperative that it loads AFTER any other PvE plugin** (e.g., SimplePvE, TruePvE) you may be running. This ensures that our plugin's damage handling hooks are correctly registered and take precedence.

**The Load Order Challenge & Our Solution:**
Unfortunately, there is no reliable, guaranteed way in Oxide/uMod or Carbon to enforce plugin load sequences. While alphabetical loading is common, it is not assured, and we prefer not to resort to crude naming conventions like "ZZ_Damage_Reflection." To counteract this, we recommend using another of our plugins "FAR: Logger" (or a custom startup command) to **reload "FAR Damage Reflection" e.g. one minute after server startup**. This method ensures that our plugin registers its hooks correctly, regardless of initial load order, and functions as intended even if your PvE plugin allows player-to-player damage.

## Localization
FAR Damage Reflection supports full localization for all in-game messages echoed to players. Ready-to-use localization files for various languages are provided in the `language` folder of our GitHub repository. You can grab the English default or provide your own translations.

## Installation
1.  Download the `FARDamageReflection.cs` file.
2.  Place the file into your server's `oxide/plugins` (or `carbon/plugins`) folder.
3.  **Critical Step:** Implement a method to reload `FARDamageReflection.cs` a short time after your server starts. This can typically be done via a custom RCON command executed by a separate plugin (e.g. our FAR: Logger plugin) or a server startup script.
4.  The plugin will automatically load (and then reload) and begin enforcing your server's rules.
5.  If "FAR: Damage Reflection" can't be reloaded and loads after your PVE implementation our plugin will still work, but be unable to reflect damage.

## Permissions
*   `damagereflection.bypass` - allows players with permission to bypass all restrictions (you usually give staff that permission).

## Configuration

The plugin offers an extensive configuration to fine-tune every aspect of damage reflection and moderation. A sample of the default configuration is provided below:

```json
{
  "General": {
    "debug": false, // Set to true for debug output in console (punishments will be disabled).
    "enableF7Reports": true, // Enable/disable processing of F7 feedback and player reports.
    "enableBypassPermission": true, // Enable/disable a permission to bypass damage reflection (useful for staff).
    "enableTcAuthGuardProtection": true, // Enable/disable the integrated TC Auth Guard protection.
    "enablePveHook": true, // Enable/disable exposing our own CanEntityTakeDamage hook.
    "pvePluginInstalled": true, // Set to true if a PVE plugin (SimplePVE, TruePVE) is installed.
    "enableDamageToVictim": false, // If true, a portion of damage will still hit the victim.
    "forgivableStrikesMax": 3, // Maximum number of forgivable strikes before a permanent punishment.
    "forgivableStrikesDecayMinutes": 20, // Minutes after which a strike will decay.
    "enableDeathPenalty": true, // Enable/disable death penalty for PvP violations.
    "enableAutoKick": true // Enable/disable automatic kicking for violations.
  },
  "Discord": {
    "pvpViolationWebhook": "", // Webhook for PvP violation notifications.
    "entityViolationWebhook": "", // Webhook for general entity damage notifications.
    "raidAutoBanWebhook": "", // Webhook for auto-ban raid events.
    "raidAutoKickWebhook": "", // Webhook for auto-kick raid events.
    "pvpAutoKickWebhook": "", // Webhook for auto-kick PvP events.
    "f7ReportsWebhook": "", // Webhook for F7 reports.
    "forbiddenDeployablesWebhook": "" // Webhook for forbidden deployable placement attempts.
  },
  "Entity": {
    "reflectPercentageEntity": 100.0, // Percentage of entity damage reflected to attacker.
    "raidTempBanHours": 24, // Duration of temporary ban for severe raid violations.
    "IncludedEntities": [ // List of entity shortnames or prefabs to consider "raid relevant".
      "BuildingPrivlidge",
      "BuildingBlock*",
      "SimpleBuildingBlock*",
      "Door*",
      "BaseOven*",
      "IOEntity*",
      "StorageContainer*",
      "Fridge",
      "Workbench",
      "RepairBench",
      "ResearchTable",
      "HitchTrough",
      "Barricade"
    ],
    "ExcludedEntities": [ // List of entity shortnames or prefabs to exclude from raid monitoring.
      "AutoTurret",
      "BaseTrap",
      "FlameTurret",
      "GunTrap",
      "HackableLockedCrate",
      "LootContainer",
      "NaturalBeehive",
      "NPCAutoTurret",
      "ReactiveTarget"
    ]
  },
  "ForbiddenDeployables": {
    "enable": true, // Enable/disable forbidden deployables feature.
    "forbiddenDeployables": [ // List of deployable shortnames that are forbidden outside TC range.
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
    ]
  },
  "PvP": {
    "applyBleeding": true, // Apply bleeding effect on PvP reflection.
    "bleedingIntensity": 5.0, // Intensity of the bleeding effect.
    "reflectPercentagePVP": 100.0, // Percentage of PvP damage reflected to attacker.
    "isHeadShotForgivable": false, // If false, headshots are never forgivable and lead to immediate punishment.
    "headshotMultiplier": 2, // Multiplier for reflected damage from headshots.
    "warnVictimWhileForgivable": true, // Show chat warning to the victim during forgivable PvP damage.
    "warnAttackerWhileForgivable": true // Show chat warning to the attacker during forgivable PvP damage.
  }
}
# FAR Vehicle Locks

## Description
The **FAR Vehicle Locks** plugin offers a robust, configurable, and significantly improved solution for managing vehicle ownership and preventing theft on your Rust server. Inspired by the need for secure vehicle control, this plugin allows players to claim and protect a wide range of vehicles, ensuring that only they or their authorized teammates can interact with, drive, or loot them.

**This plugin was entirely re-written from scratch due to the unspecified and non-permissive licensing of its conceptual inspiration, the "Horse Lock" plugin on CodeFling.com. While acknowledging the original idea, FAR Vehicle Locks expands far beyond the original implementation, offering a much broader, more flexible, and user-centric approach to vehicle security.**

## Changelog

### 1.0.6 - 2025-09-21
*   **Added Push Prevention:** Added a method to prevent pushing vehicles which are owned by another player (non-team)
*   **Adjusted Discord Messages** Adjusted messages sent to Discord to use other emojis and be more meaningful

### 1.0.5 - 2025-09-16
*   **Added Discord Notifications:** Added notifications to a Discord webhook in case of locking or unlocking and death (destruction) of a vehicle

## Why FAR Vehicle Locks? (Addressing Flaws & Expanding Control)
The original "Horse Lock" plugin, while a good concept, had significant limitations. Its primary flaw was the automatic claiming of *every* horse a player mounted, which often led to unintended ownership of vehicles a player merely wanted to inspect or briefly use. And, well, it was limited to horses while there are other vehicles to worry about. This created unnecessary restrictions and frustration.

**FAR Vehicle Locks fundamentally re-imagines vehicle ownership with a "player-opt-in" philosophy and extensive configurability:**

*   **Opt-In Ownership:** Unlike automatic claiming, players must explicitly use the `/vehicle lock` command to claim a vehicle. This means you can mount a vehicle to check its vitals, move it, or examine its contents without inadvertently "owning" it.
*   **Config-Driven Flexibility:** The plugin is not limited to horses. Server owners gain full control over which vehicle types are managed, can add new vehicles, and customize their behavior (e.g., decay settings, display names, player limits) directly through the configuration.
*   **Comprehensive Security:** Beyond simple locking, the plugin provides nuanced protections: unauthorized players cannot access driver seats, loot storage, or fuel, and even specific horse interactions (like leading) are restricted.
*   **Damage Protection:** Locked vehicles are protected from player-inflicted damage, preventing malicious destruction for loot.

## Features
*   **Configurable Vehicle Types:** Easily add or remove any vehicle type (including horses, modular cars, helicopters, etc.) from management via the configuration file.
*   **Optional Ownership Decay:** Configure whether ownership for specific vehicle types can decay after a set time outside of Tool Cupboard (TC) range.
*   **Custom Display Names:** Set user-friendly display names for vehicles, shown to players in game messages.
*   **Player Lock Limits:** Define how many vehicles of each type a single player can simultaneously lock.
*   **Chat Spam Prevention:** Configurable cooldowns for chat messages when mounting or dismounting, preventing spam.
*   **TC Range Protection:** Ownership decay is paused when a locked vehicle is within its owner's (configurable) TC range.
*   **Exclusive Access:** Only the owner or their team members can drive, access fuel, loot storage, or interact with a locked vehicle.
*   **Passenger Access (Optional):** Unauthorized players may be able to mount passenger seats but are strictly prevented from the driver's seat.
*   **Unlootable & Uninteractable:** Prevents unauthorized players from looting, accessing fuel, leading horses, or any other primary interaction.
*   **Damage Protection:** Locked vehicles are protected from player-inflicted damage. (e.g., another player cannot kill your locked horse). This protection does not extend to environmental damage (falling) or server-controlled entities (patrol helicopter).
*   **Decay Warnings:** For decayable vehicles, players receive multiple warnings before ownership expires, giving them time to reclaim / remount their vehicle.
*   **Integrated Localization:** Full language support allows server owners to customize all plugin messages. Ready-to-use localization files for various languages are provided in the `language` folder of our GitHub repository - or you grab the English default and do your own translation as you like.

## Commands
All commands are prefixed with `/vehicle`.

*   `/vehicle list` - Lists all locked vehicles for the player, including last rider (could be a team member), current mounted status, and TC range status.
*   `/vehicle lock` - Locks the currently mounted vehicle (driver seat/saddle) to the player. Only possible if the player hasn't exceeded their maximum allowed vehicles of that type.
*   `/vehicle unlock` - Removes the lock from the currently mounted vehicle, making it available to everyone. Only the owner can unlock. Locks are also removed upon vehicle destruction/death or ownership decay.

## Permissions
*   `farvehiclelocks.bypass` - Allows players with permission to bypass all locking restrictions, interacting with any locked vehicle as if it were unlocked. (Primarily for staff).

## Configuration
The plugin's extensive features are managed through the `FARVehicleLocks.json` configuration file, generated upon first load in your `oxide/config` (or `carbon/config`) folder.

Here's an example configuration with explanations:

```json
{
  "VehicleTypes": {                                // Defines settings for different types of vehicles
    "attackhelicopter": {
      "Vehicle Display Name": "Attack Helicopter", // Name shown to players
      "Rust Prefab Shortname(s)": [                // List of Rust prefab shortnames for this vehicle type
        "attackhelicopter"
      ],
      "Can ownership decay?": false, // Set to true if this vehicle's ownership should expire
      "Minutes until ownership decays outside TC range?": 15, // How long before decay starts if outside TC range
      "Minutes until owned usage hint is shown again (prevent spam)": 10, // Cooldown for "you own this" message
      "Minutes until dismount hint is shown again? (to prevent spam)": 10, // Cooldown for "you dismounted" message
      "How many vehicles of this kind can a player lock?": 1 // Max number of this vehicle type a player can own
    },
    // ... (other vehicle types like minicopter, scraptransporthelicopter, modularcar, ridablehorse, bicycle, motorbike)
    // You can add or remove entries for any Rust vehicle prefab here.
  },
  "Check interval in seconds for ownership decay": 30, // How often the plugin checks for decay status
  "Make the horse rear on unauthorized access attempt": true, // If a horse should rear when an unauthorized player attempts to mount
  "Warn seconds before vehicle ownership decays": 180, // How many seconds before decay the first warning is sent
  "Set the TC radius in meters (1-100)": 30.0 // The radius around a Tool Cupboard to consider a vehicle "in TC range"
}

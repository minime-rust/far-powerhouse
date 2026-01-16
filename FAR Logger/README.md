# FAR Logger

## Description
The **FAR Logger** plugin is a versatile, all-in-one logging and event notification solution, reimagined and written from scratch to go far beyond traditional logging utilities. While initially conceived as an extension to existing Discord logger plugins, FAR Logger has evolved into a robust "swiss army knife" for server administrators. It provides real-time insights and automated actions across a multitude of server events, from player interactions with supply drops to critical server configuration changes and scheduled commands, all seamlessly integrated with Discord notifications. This plugin enhances server transparency, aids in moderation, and simplifies essential server management tasks.

## Features
FAR Logger provides comprehensive monitoring and utility features, including:
*   **Supply Drop Loot Announcements:** Notifies global chat and Discord when players loot a supply drop, including location details.
*   **Server Wipe Detection:** Monitors server `worldSize` and `mapSeed` to automatically announce wipes to Discord with a direct link to the new map on rustmaps.com.
*   **`users.cfg` Integrity Monitoring:** Utilizes a lightweight checksum to detect and notify Discord of any changes to the server's `users.cfg` file, serving as a critical security tripwire.
*   **Startup Commands:** Executes a configurable list of console commands after server load, ideal for setting variables or reloading specific plugins.
*   **Scheduled Commands:** Allows scheduling a single, recurring console command (e.g., nightly server restarts).
*   **Failed Connection Monitoring ("Doorknockers"):** Tracks and notifies Discord of players attempting to connect but failing (due to bans, Steam auth issues, etc.), including IP addresses for diagnostic purposes.
*   **Event Monitoring:**
    *   **Travelling Vendor:** Notifies Discord of the Travelling Vendor's spawn and despawn.
    *   **"Guess the Number" Plugin:** Monitors and notifies Discord of game events (requires a patched version of the plugin for full hook integration).
    *   **Plugin Management:** Notifies Discord of plugin load, unload, and reload events.
    *   **Raidable Bases Events:** Provides detailed Discord notifications for Raidable Bases events.
    *   **Abandoned Bases Events:** Utilizes a robust workaround to monitor and notify Discord of Abandoned Bases events.
*   **`/wipe` Chat Command:** Allows players to retrieve the exact date, time, and remaining time until the next server wipe via an in-game chat command, with full localization support.
*   **Granular Control:** All monitored functions can be individually enabled/disabled, with separate Discord notification toggles, allowing flexible control without needing to modify webhook configurations.

## API - For Plugin Developers
The plugin exposes a straightforward API that other plugins can leverage to hand-off and dispatch Discord messages. This API returns a non-null response on success.

This allows other plugins to easily hand-off Discord messages and rely on FAR Logger's internal message queue and dispatching without having to reinvent the wheel.

**API Usage Example:**

```csharp
// To call the FAR Logger API from another plugin. First, get a reference to the plugin:
var farLoggerPlugin = Plugin.Find("FARLogger");

if (farLoggerPlugin != null)
{
    // Define the webhookUrl and message you want to send to Discord
    string webhookUrl = "https://discord.app/your/webhook/goes/here";   // Example webhook
    string message = "Your Discord message goes here"                   // Example message

    if (string.IsNullOrWhiteSpace(webhookUrl) || string.IsNullOrWhiteSpace(message))
        return; // nothing to send

    // Try to hand off to FAR Logger if available
    var result = farLoggerPlugin.Call("API_SendDiscordMessage", webhookUrl, message);

    if (result is bool ok && ok)
    {
        return; // Hand-off succeeded, FAR Logger took your message
    }
    else
    {
        /* your logic for such cases where the hand-off failed */
    }
}

```

## Highlights

### Automated Supply Drop Loot Announcements: Ending PvE Frustration
On PvE servers, the claiming of Supply Drops can often be a source of confusion and frustration. Players may forget to claim in global chat, claim incorrectly, or even attempt to "steal" drops from others. FAR Logger solves this problem entirely. When a player begins looting a Supply Drop, the plugin automatically broadcasts a clear message in global chat (e.g., "miniMe is looting a Supply Drop in square A17 (near Launch Site)"). This feature has been exceptionally well-received by players, fostering a more natural and immersive gameplay experience by making manual claims obsolete.

### Intelligent Server Wipe Notifications to Discord
Keeping your community informed about server wipes is crucial. FAR Logger automates this process by monitoring the server's `worldSize` and `mapSeed` at startup. Any change in these core parameters signals a new wipe, triggering an immediate Discord notification: "Server Wipe detected!" Crucially, this message includes a dynamically generated link to rustmaps.com, pre-populated with the new world size and map seed, allowing your community to instantly preview the new map directly within Discord. This ensures players always know when they can reconnect and what the new landscape looks like, even if they can't be online for the wipe itself.
If you're a registered rustmaps.com user you can now configure your API Key and have **FAR: Logger** automatically request map generation for you.

### "Doorknockers" - Monitoring Failed Player Connections
Understanding why players fail to connect is vital for server health and support. The "Doorknockers" feature monitors and logs all failed connection attempts, notifying Discord with details such as the player's IP address and the reason for failure (e.g., banned, Steam authentication issues). This real-time visibility is an invaluable tool for diagnosing player-reported connection problems, identifying potential bad actors, or flagging wider Steam service outages.

### `users.cfg` Integrity Monitoring: A Critical Security Tripwire
The `users.cfg` file contains critical server configuration, including staff permissions. To safeguard against unauthorized or accidental changes, FAR Logger implements a lightweight, checksum-based monitoring system. Instead of relying on easily misleading file timestamps, a checksum ensures that any actual content modification to `users.cfg` triggers an immediate Discord notification. This provides server owners with an essential security alert, helping to maintain the integrity of server administration.

### Custom Event Integrations: Bridging the Gaps
FAR Logger goes the extra mile to integrate with other popular Rust plugins:
*   **"Guess the Number" Plugin:** We have developed a patch for the umod.org "Guess the Number" plugin to expose necessary hooks, allowing FAR Logger to monitor its events and send notifications to Discord. We have requested that these hooks be integrated upstream to remove the need for manual patching.
*   **Abandoned Bases Events:** Despite the lack of native hooks, FAR Logger implements a robust **workaround** by listening to server console output for messages originating from the "Abandoned Bases" plugin. This ensures that even without direct API integration, you still receive timely Discord notifications about these events.
*   **Raidable Bases Events:** Leveraging the native hooks provided by the "Raidable Bases" plugin, FAR Logger delivers seamless and configurable Discord notifications for all relevant raiding events.

### `/wipe` Chat Command: Player Convenience
Players can simply type `/wipe` in chat to instantly receive information about the next server wipe, including the exact date and time, and the time remaining. This command supports full localization, ensuring all players receive the information in their preferred language.

## Configuration
The plugin offers extensive configuration options to fine-tune every aspect of logging and notification. A sample of the default configuration is provided below:

```json
{
  "General": {
    "PluginMonitorStartupIgnoreSeconds": 120, // Number of seconds to ignore plugin load/unload/reload events at startup to avoid spam.
    "Use24HourTime": true // Use 24-hour format for time displays.
  },
  "Airdrop": {
    "Enabled": true, // Enable/disable Airdrop monitoring.
    "ChatNotify": true, // Enable/disable global chat notifications for Airdrop looting.
    "DiscordNotify": true // Enable/disable Discord notifications for Airdrop looting.
  },
  "Bases": {
    "Enabled": true, // Enable/disable Raidable/Abandoned Bases monitoring.
    "DiscordNotify": true // Enable/disable Discord notifications for Bases events.
  },
  "DoorKnockers": {
    "Enabled": true, // Enable/disable failed connection monitoring.
    "DiscordNotify": true // Enable/disable Discord notifications for failed connections.
  },
  "GuessTheNumber": {
    "Enabled": true, // Enable/disable Guess the Number monitoring.
    "DiscordNotify": true // Enable/disable Discord notifications for Guess the Number events.
  },
  "PluginMonitor": {
    "Enabled": true, // Enable/disable plugin load/unload/reload monitoring.
    "DiscordNotify": true // Enable/disable Discord notifications for plugin events.
  },
  "ScheduledCommand": {
    "Enabled": true, // Enable/disable scheduled command execution.
    "TimeUtc": "00:45", // UTC time (HH:MM) when the scheduled command should run daily.
    "Command": "restart 900 \"nightly restart\"" // The console command to execute.
  },
  "ServerWipes": {
    "Enabled": true, // Enable/disable server wipe monitoring.
    "DiscordNotify": true, // Enable/disable Discord notifications for server wipes.
    "RustMapsApiKey": "" // rustmaps.com API Key to automate map generation requests.
  },
  "StartupCommands": {
    "Enable": true, // Enable/disable startup command execution.
    "DelaySeconds": 60, // Delay in seconds before executing startup commands.
    "Commands": [ // List of console commands to execute at startup.
      "c.reload FARDamageReflection",
      "weather.storm_chance 0.1",
      "weather.rain_chance 0.2",
      "del cargoship"
    ]
  },
  "TravellingVendor": {
    "Enabled": true, // Enable/disable Travelling Vendor monitoring.
    "DiscordNotify": true, // Enable/disable Discord notifications for Travelling Vendor.
    "NotifyDespawn": true // Enable/disable notification when the Travelling Vendor despawns.
  },
  "UsersCfg": {
    "Enabled": true, // Enable/disable users.cfg integrity monitoring.
    "DiscordNotify": true // Enable/disable Discord notifications for users.cfg changes.
  },
  "Webhooks": {
    "AirdropWebhook": "", // Discord webhook URL for Airdrop looting notifications.
    "WipeWebhook": "", // Discord webhook URL for server wipe notifications.
    "BasesWebhook": "", // Discord webhook URL for Raidable/Abandoned Bases notifications.
    "VendorWebhook": "", // Discord webhook URL for Travelling Vendor notifications.
    "PluginsWebhook": "", // Discord webhook URL for plugin load/unload/reload notifications.
    "UsersCfgWebhook": "", // Discord webhook URL for users.cfg change notifications.
    "GuessNumberWebhook": "", // Discord webhook URL for Guess the Number events.
    "DoorKnockersWebhook": "" // Discord webhook URL for failed player connection attempts.
  }
}
```

## Localization
FAR Logger supports **partial localization** for messages related to the /wipe chat command. Messages destined for Discord are designed to remain in English to maintain consistency for administrative oversight. Lines 2, 3, and 5 of the language file are intended for translation, while other lines will largely be ignored by the plugin. Ready-to-use localization files for various languages are provided in the `language` folder of our GitHub repository. You can grab the English default or provide your own translations. If you use our language files, simply reload `FAR: Logger` after storing the files in their respective folder to automatically add any missing default translations.

## Dependencies
*  **FAR: Map Helper:** We rely on our "FAR: Map Helper" plugin to resolve Supply Drop locations into specific map squares and monument names. FAR Logger will still function and announce the looting of Supply Drops if Map Helper is not present, but the usefulness will be degraded due to the absence of detailed location information.

## Permissions
None.

## Installation
1.  Download the FARLogger.cs file.
2.  Place the file into your server's `oxide/plugins` (or `carbon/plugins`) folder.
3.  The plugin will automatically load upon server restart or when manually reloaded.
4.  Optionally install "FAR: Map Helper" for enhanced Supply Drop location details.

## Changelog

### 1.3.3 - 2026-01-16
*   **Adapt to update AB Hooks** Added support for new hooks in Abandoned Bases. Improved Discord output to always mention the base owner if known

### 1.3.2 - 2026-01-14
*   **Improve AB integration** Added support for detection of different base types, improved output sent to Discord including entity and item count at event start

### 1.3.1 - 2026-01-12
*   **Code refactor:** Proper integration of Abandoned Bases' hooks, improved wipe detection, simplified timers and data storage. This has been a major clean-up.

### 1.3.0 - 2025-11-20
*   **Improved rustmaps.com integration:** Delayed Discord notification on map change to allow map generation to finish and ensure Discord has a proper embed / map preview

### 1.2.6 - 2025-09-22
*   **Message Injection:** Added a method to (optionally) handle Discord notifications from other plugins in the FAR framework

### 1.2.6 - 2025-09-21
*   **Code cleanup:** Simplified the queue, solving a race condition between concurrent timers. Now there is only 1 worker responsible for the queue.

### 1.2.5 - 2025-09-20
*   **Added rustmaps.com support:** If you configure your API Key for rustmaps.com your new map can now be requested to be generated automatically
*   **Code cleanup:** Removed unused code and configuration, improved Discord messages with a simple queue and retry to handle getting rate limited
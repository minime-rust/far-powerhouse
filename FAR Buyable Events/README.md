# FAR Buyable Events

## Description
The **FAR Buyable Events** plugin introduces a dynamic new layer of risk and reward to your Rust server. Moving beyond the often-underwhelming vanilla Supply Drop, this plugin empowers players to trade their Supply Signals for a chance to summon more challenging and potentially far more lucrative events: the Launch Site Bradley APC or the formidable Patrol Helicopter. Unlike other event-calling plugins, FAR Buyable Events focuses on seamlessly integrating these high-stakes opportunities into existing gameplay without intruding on the server's core experience, offering a unique concept that transforms a low-risk item into a high-impact gamble.

## Changelog

### 1.1.1 - 2026-02-24
*   **Added Discord notification:** Added a notification to Discord to inform about the player who dealt the majority of damage to Patrol Chopper

### 1.1.0 - 2025-09-27
*   **Added heli loot protection:** Heli loot is now reserved for the player (and team) who dealt the most damage to chopper, then released to everyone

### 1.0.1 - 2025-09-25
*   **Corrected spawn distance:** Fixed a bug where the patrol helicopter would not spawn at the configured distance from the player
*   **Added global chat warning:** Added a warning in global chat - so that everyone on the server knows when Bradley or a Chopper was called

### 1.0.0 - 2025-09-22
*   **Hand-Off to FAR Logger:** Added a method to (optionally) hand-off Discord notifications to FAR Logger's message queue

## The Concept: Turning "Meh" into "More!"

Vanilla Supply Signals, while a classic Rust item, often yield rewards that don't quite match the effort of acquiring them, especially on PvE servers where competition is minimal. What if a Supply Signal could be a key to something more exciting?

FAR Buyable Events redefines the Supply Signal's value by offering players a choice:
*   **Zero Risk, Moderate Reward (Vanilla):** Toss a Supply Signal for a standard, uncontested Supply Drop (especially with plugins like FAR Supply Lock).
*   **Moderate Risk, Good Reward (FAR Buyable Events):** Use a Supply Signal to challenge the **Launch Site Bradley APC**.
*   **High Risk, Excellent Reward (FAR Buyable Events):** Use a Supply Signal to face off against the **Patrol Helicopter**.

This plugin transforms a predictable outcome into a strategic decision, injecting excitement and a genuine sense of risk-versus-reward into player interactions.

## How it Works: Event-Specific Conditions

Players can trigger these events through simple chat commands, but only if specific conditions are met, ensuring a balanced and contextual challenge:

### 1. Launch Site Bradley
Summoning Bradley APC requires a strategic approach and specific circumstances:
*   **Location:** The player must be physically located within the monument bounds of **Launch Site**.
*   **Availability:** There must be no existing Bradley APC currently active within the Launch Site monument.
*   **Cost:** The player must possess **1 (one) Supply Signal** in their inventory.

### 2. Patrol Helicopter
Summoning the Patrol Helicopter is a higher-risk endeavor with fewer location constraints:
*   **Availability:** There can be only **one** Patrol Helicopter active on the entire map at any given time.
*   **Cost:** The player must possess **1 (one) Supply Signal** in their inventory.

## Chat Commands

Players utilize straightforward chat commands to attempt to summon these events:

*   `/buybradley`
    *   **Function:** Initiates the checks for spawning a Bradley APC at Launch Site.
    *   **Success:** If all conditions (location, no existing Bradley, Supply Signal in inventory) are met, the plugin attempts to spawn Bradley. After a 2-second confirmation, if successful, one Supply Signal is consumed from the player's inventory.
    *   **Failure:** If any condition is not met, an error message is displayed to the player, and no Supply Signal is consumed.
*   `/buyheli`
    *   **Function:** Initiates the checks for spawning a Patrol Helicopter.
    *   **Success:** If all conditions (no existing Patrol Heli, Supply Signal in inventory) are met, the plugin attempts to spawn a Patrol Helicopter approximately 1000 meters away from the player in a random direction. The Helicopter is then instructed to fly towards the player's current position, ensuring an immediate and intense confrontation. One Supply Signal is consumed from the player's inventory.
    *   **Failure:** If any condition is not met, an error message is displayed to the player, and no Supply Signal is consumed.

## Consequences & The Gamble

Using FAR Buyable Events is a true gamble, where players can risk their Supply Signal for a potentially much greater reward, but also face significant threats:
*   **Bradley APC:** Generally considered a moderate-risk target, Bradley offers good loot for its challenge. It's a stepping stone between the 'meh' of a supply drop and the 'mayhem' of a chopper.
*   **Patrol Helicopter:** The ultimate wildcard. It can be surprisingly easy or incredibly deadly, often requiring players to chase it across the map. The loot, however, is typically of the highest value, making it a compelling, high-stakes proposition. Players could lose their Supply Signal, be killed, or even fail to take down the event and be left with nothing.

## Dependencies

The "FAR Buyable Events" plugin relies on the umod.org **"Monument Finder"** plugin to accurately determine monument boundaries for the Launch Site Bradley spawn conditions.
*   **Crucial Note:** If the "Monument Finder" plugin is not installed, the `/buybradley` command will not function correctly as it cannot verify the player's location within Launch Site or the presence of existing Bradleys. The `/buyheli` command will function independently.

## Configuration

FAR Buyable Events offers flexible configuration options to tailor the event buying experience to your server's unique setup and wipe schedule:

```json
{
  "BradleyBuyableDuringEndGame": false,  // Set to true to allow Bradley to be bought during the 'Endgame' period.
  "DiscordWebhook": "",                  // Optional: Discord webhook URL to send notifications when an event is bought. Leave empty to disable.
  "EndgameHoursBeforeWipe": 24.0,        // Defines how many hours before the next wipe the 'Endgame' period begins.
  "EventBuyableWith": "supply.signal",   // The item shortname (e.g., "supply.signal", "scrap") required to buy an event. Quantity is always 1.
  "PatrolSpawnDistance": 1000.0,         // The distance (in meters) from the player where the Patrol Helicopter will initially spawn.
  "PatrolSpawnHeight": 120.0,            // The height (in meters) above the terrain where the Patrol Helicopter will initially spawn.
  "HeliCrateLockMinutes": 10,            // The amount of time the loot is reserved to the "winning" player and team, then released to everyone
  "DataCleanupMinutes": 30,              // Clean up internal data after configured minutes to avoid unnecessary memory usage
  "ServerRestartsDaily": false,          // Set to true if your server restarts daily. This helps with wipe schedule calculations.
  "TimeZoneId": "Europe/London",         // Your server's time zone ID (e.g., "America/New_York", "Asia/Tokyo"). Used for accurate wipe calculations.
  "WipeDayOfWeek": 4,                    // The day of the week your server typically wipes (0=Sunday, 1=Monday... 6=Saturday).
  "WipeHourOfDay": 19                    // The hour of the day (24-hour format) your server typically wipes in your specified TimeZoneId.
}

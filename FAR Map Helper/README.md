# FAR Map Helper

## Description
The **FAR Map Helper** plugin is a versatile utility designed to provide valuable geographical information within your Rust server. It serves a dual purpose: offering a robust API for other plugins to accurately determine map locations and providing convenient chat commands for players and administrators to query their position and general map details. This plugin streamlines the process of integrating location-aware features into your server and empowers users with precise in-game spatial awareness.

## Dual-Use Functionality: API & Chat Commands

FAR Map Helper is engineered to benefit both plugin developers and everyday server users:

### 1. For Plugin Developers (API)
The plugin exposes a straightforward API that other plugins can leverage to resolve any given `Vector3` position on the map. This API returns a `Tuple<string, string>`, providing:
*   The **map grid square** (e.g., "A13").
*   The **monument name** (e.g., "Arctic Research Base"), if the position falls within the bounds of a recognized monument. If no monument is found at that location, the second string in the tuple will be empty.

This allows other plugins to easily add location-specific logic, messaging, or features without needing to implement their own complex map resolution.

**API Usage Example:**

```csharp
// To call the FAR Map Helper API from another plugin. First, get a reference to the plugin:
var farMapHelperPlugin = Plugin.Find("FARMapHelper");

if (farMapHelperPlugin != null)
{
    // Define the position you want to query
    Vector3 targetPosition = new Vector3(1234f, 0f, 5678f); // Example position

    // Call the API_MapInfo hook with the position
    var apiResult = farMapHelperPlugin.Call("API_MapInfo", targetPosition);

    // Process the result, which is expected to be a Tuple<string, string>
    if (apiResult is Tuple<string, string> resultTuple)
    {
        string mapSquare = resultTuple.Item1;    // e.g., "A13" or "off-grid" if outside of the map
        string monumentName = resultTuple.Item2; // e.g., "Arctic Research Base" or string.Empty

        // Now you can use mapSquare and monumentName in your plugin logic
        // Example: Puts($"Position is in {mapSquare} (Monument: {monumentName})");
    }
    else
    {
        // Handle cases where the API call might return null or an unexpected type
        Puts("FAR Map Helper API returned an unexpected result or is not available.");
    }
}
else
{
    Puts("FAR Map Helper plugin not found.");
}
```

### 2. For Players & Admins (Chat Commands)
The plugin also provides player-accessible chat commands for on-demand location information:

*   `/whereami`: Echos the player's current map location.
    *   **Example Output:** "You are currently in square A13 (near Arctic Research Base)"
    *   If not near a monument, it would simply be: "You are currently in square A13"
*   `/whereami full`: Provides the same information as `/whereami` but additionally includes the player's exact X and Z coordinates on the map.
    *   **Example Output:** "You are currently in square A13 (near Arctic Research Base) at X: 1234.5, Z: 6789.0"
*   `/maphelper`: Intended for administrative verification, this command displays overall map information. It provides details like the `worldSize` (e.g., 4250), `gridSize` (e.g., 29x29), and the range of `squares` (e.g., A0 - AC28). All chat commands are available to every player.

## Dependencies

The "FAR Map Helper" plugin relies on the umod.org **"Monument Finder"** plugin to identify and name monuments within its API and chat command responses.

*   **Crucial Note:** If the "Monument Finder" plugin is not installed or available on your server, "FAR Map Helper" will still function correctly for determining map grid squares and coordinates, but it **will not be able to identify or display any monument names.** You can still use the plugin without "Monument Finder" if monument detection is not a critical requirement for your server.

## Installation
1.  Download the `FARMapHelper.cs` file.
2.  Place the file into your server's `oxide/plugins` (or `carbon/plugins`) folder.
3.  **Optional, but Recommended:** Download and install the "Monument Finder" plugin from umod.org to enable monument detection functionality.
4.  The plugin will automatically load upon server restart or when manually reloaded.

## Configuration
No configuration is required for this plugin. Its behavior is primarily driven by its internal logic and, optionally, the "Monument Finder" plugin.

## Localization
FAR Map Helper supports full localization for all in-game messages echoed to players.
Ready-to-use localization files for various languages are provided in the `language` folder of our GitHub repository - or you grab the English default and do your own translation as you like.

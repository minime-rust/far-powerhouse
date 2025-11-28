# FAR Auto Sort

## Description
*   Sort the container you’re currently looking at by category, by item display name, or both.
*   No GUI, no fluff - just quick chat commands.
*   Note: It does not sort your personal inventory. Only the storage you’re interacting with (yes, that includes your TC… and roadside crates, which is fun but also gloriously pointless).

## Chat Commands
Simply look at the storage you want to sort, then issue one of these chat commands:
*   `/sort` → Sort by category (default)
*   `/sort name` → Sort by item display name
*   `/sort both` → Sort by category, then name

## Best practice (make it a habit)
Bind your favorite key once, then tap this key to sort while looting.
Example - enter in F1 console: `bind j chat.say "/sort name"`
Prefer another key? Use any key you like: `bind <yourKey> chat.say "/sort <mode>"`

## Installation
1.  Download the `FARAutoSort.cs` file.
2.  Place the file into your server's `oxide/plugins` (or `carbon/plugins`) folder.
3.  The plugin will automatically load upon server restart or when manually reloaded.

## Configuration
No configuration required

## Changelog

### 1.0.1 - 2025-11-28
*   **Bugfix:** Fixed a bug where sorting a box with guns would disappear any equipped attachments.
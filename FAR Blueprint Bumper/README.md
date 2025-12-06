# 💾 FAR Blueprint Bumper

**Automated Blueprint File Bumping for Rust Servers.**

## 💻 RCON Command
```bash
blueprint.bump
```

## 📝 Description
The **FAR: Blueprint Bumper** plugin is an essential helper designed to integrate seamlessly into your existing **server wipe procedures or scripts**.  

Since the Facepunch update in October 2025, the Rust game server enforces a monthly blueprint wipe by looking for a blueprint database file with an incremented version number (e.g., switching from `player.blueprints.8.db` to `player.blueprints.9.db`).

## Why use this plugin?
If your community server operates under a "no blueprint wipe" promise, you must ensure that the existing blueprint data is correctly carried over to the new version number before the monthly force wipe.

This plugin automates the tedious manual process:
1.  It automatically **searches for the highest existing** blueprint version file (e.g., `.8.db`).
2.  It creates an **exact copy** of the existing `.db` and `.db-wal` files, incrementing the version number (e.g., creating `.9.db`).
3.  This ensures all existing **player progress is preserved** and available to the game server immediately after the wipe.

## Important Note on File Cleanup
This plugin only adds the new files. As a best practice, you should integrate routine maintenance into your scripts to periodically clean up and remove outdated, older `.db` files to prevent unnecessary storage growth.

## ⚙️ Installation
1.  Download the `FARBlueprintBumper.cs` file.
2.  Place the file into your server's `oxide/plugins` (or `carbon/plugins`) folder.
3.  The plugin will automatically load upon server restart or when manually reloaded.
4.  The plugin is compatible with both the **Oxide/uMod** and **Carbon** frameworks.

## 📜 Commands
The plugin registers a **Console Command** that can only be executed via the **Server Console** or **RCON**. This design ensures regular players cannot trigger the command via the in-game F1 client console, maintaining security as no player authentication checks are implemented.

| Command | Usage | Description |
| :--- | :--- | :--- |
| `blueprint.bump` | RCON/Console | Triggers the blueprint file duplication and version bump. The result will be printed directly to the server console. |

## 🔧 Configuration
*   No configuration required
*   No permissions implemented

## Changelog
*   **2025-12-06** `1.0.0` Initial version 
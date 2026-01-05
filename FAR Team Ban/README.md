# 💾 FAR Team Ban

**Efficient enforcement of kicks, bans, or full wipes for teams on Rust servers.**

## 💻 Commands

All commands require **SteamID** of any team member. The plugin will automatically detect other team members and show a **confirmation summary** before executing.

| Command | Usage | Description |
| :--- | :--- | :--- |
| `teamkick <SteamId> ["Reason"]` | Console / RCon / F1 (permission required) | Kick the entire team of the given SteamID. Optional reason in double quotes. |
| `teamban <SteamId> ["Reason"] [Hours]` | Console / RCon / F1 | Ban the entire team. Optional reason in double quotes. Ban duration in hours only applies if a reason is given. Permanent if omitted. |
| `teamwipe <SteamId>` | Console / RCon / F1 | Wipe the entire team: inventories, blueprints, entities, and optionally kill alive/sleeper players. Best-effort; offline/dead players may not have data removed. |

## 📝 How it works

1. Enter the command with a single team member’s SteamID.  
2. The plugin builds an **internal list of all team members**.  
3. You get a **summary of targeted players**, and must **confirm by re-entering the exact same command** within **5 minutes**.  
4. If you modify the command (e.g., choose a different player), the plugin updates the summary and requires confirmation again.  

**Notes:**
- Only **SteamID** is used, not player names (avoids ambiguity or issues with Unicode characters).  
- `["Reason"]` is optional. Default: `"No Reason"`.  
- `[Hours]` only applies for bans and only if a reason is given.

**Pro Tipp:** Use `teamwipe` to fully remove inventories, blueprints, and entities of misbehaving players.

## ⚙️ Installation

1. Download `FARTeamBan.cs`.  
2. Place it in `oxide/plugins` (or `carbon/plugins`).  
3. Reload plugin or restart server.  
4. Compatible with **Oxide/uMod** and **Carbon** frameworks.

## 🔒 Permissions

- `farteamban.use` — allows players (moderators/staff) to use commands in F1 console.  
- Commands executed via **RCon / server console** bypass permissions.  
- Regular players **cannot** trigger these commands.

## Changelog
*   **2026-01-05** `1.0.2` corrected input echoed back to user correctly for confirmation
*   **2025-12-18** `1.0.1` Sanitized information shown to moderators, code clean up
*   **2025-12-15** `1.0.0` Initial version
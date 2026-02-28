# FAR: Ironman

> "The island shapes survivors, not gods. Stay alive long enough, and the cold won’t freeze you as fast. The radiation won’t burn as deep. But step wrong just once, and you’ll learn why Rust doesn’t forgive."

**FAR: Ironman** is a high-stakes survival modification for Rust PvE servers. It introduces an opt-in "Ironman Mode" where players accrue environmental resistance through pure survival time. There are no shortcuts: safety slows your progress, and death erases everything.

## 🛡️ Core Mechanics

### 1. Survival Accrual
Survival time is tracked in real-time while you are active on the server.
* **Active Gameplay:** 1:1 time accrual while exploring and fighting.
* **Safe Zone/Base Penalty:** Accrual is slowed to **10%** of normal speed while inside a Building Privilege zone or a Safe Zone (e.g., Outpost).
* **AFK Protection:** Time stops counting if you are idle for more than 5 minutes.
* **Sleepers:** Time does not count while you are logged off or sleeping.

### 2. Environmental Resistance
As you survive, your body hardens. At the **10-hour cap**, you reach a maximum of **50% damage reduction** against the elements.

| Damage Type | Vanilla | Ironman (10h+) | Note |
| :--- | :---: | :---: | :--- |
| **Fall Damage** | 100% | **50%** | Leap from higher cliffs with less risk. |
| **Radiation** | 100% | **50%** | Linger in Rad Towns significantly longer. |
| **Cold/Heat** | 100% | **50%** | Survival in the snow becomes manageable. |
| **Animal Bites** | 100% | **50%** | Bears and Wolves are less likely to end a run. |
| **Hunger/Thirst** | 100% | **50%** | Metabolism slows down, requiring less frequent eating. |
| **Bullets/Melee** | 100% | **100%** | **No Resistance.** NPCs and Scientists remain lethal. |

### 3. Death
If you die, your "Survival Time" and "Environmental Resistance" are immediately reset to **0**.

## 💬 Chat Commands
* `/ironman` — View your current survival time, resistance %, and the server's Top Survivor.
* `/ironman on` — Opt-in to Ironman mode and start your journey.
* `/ironman off` — Opt-out and **wipe all your survival stats**.

## 🔑 Permissions
* `ironman.optin` — This permission is managed automatically by the plugin when players use the chat commands. You do not need to assign it manually.

## 🛠️ Installation
1.  Download `FARIronman.cs`.
2.  Place it in your `oxide/plugins` or `carbon/plugins` folder.
3.  The plugin will generate a data file in `oxide/data/FARIronman.json` to persist stats across restarts.

## ⚙️ Configuration
This plugin follows the **KISS** principle (Keep It Simple, Stupid). There is no external `.json` config. If you wish to tweak the balance, edit the constants at the top of the `.cs` file:
* `MaxIdleTime`: How long before AFK stops the clock.
* `BaseTimeMultiplier`: The accrual rate inside bases (Default 0.1).
* `MaxSurvivalHours`: Hours needed to reach max resistance (Default 10).
* `MaxReduction`: The maximum resistance percentage (Default 0.5 for 50%).

## 📜 Changelog
### 1.0.0
* Initial Release Candidate.
* Linear resistance scaling for environmental damage.
* Safe Zone and TC accrual penalties.
* Automatic leaderboard filtering (Admins excluded).
* Death-to-Respawn notification loop.
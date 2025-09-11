# FAR Supply Lock

## Description
The **FAR Supply Lock** plugin is a re-engineered and significantly improved version of the original Supply Lock concept for Rust servers by Ghosty. Its primary purpose is to ensure that supply drops (from both supply signals and Excavator events) are exclusively lootable by the player who legitimately called them. This prevents opportunistic players from stealing valuable loot and ensures a fair reward for those who invested in or triggered the drop.

**This plugin is based on the original "Supply Lock" plugin on CodeFling.com, created by Ghosty. While credit for the foundational idea goes to the original creator, our improved version features a completely redesigned, robust, and reliable detection mechanism to address critical flaws present in the original implementation.**

## Why FAR Supply Lock? (Addressing the Original's Flaws)
The original token-based approach used by the original plugin, while effective for single drops, suffered from inherent vulnerabilities to **race conditions**. When multiple players called supply drops around the same time, the plugin could easily mix up ownership, leading to incorrect locking, stolen loot, and player frustration (as documented in the original plugin's support section). Furthermore, the original plugin has not been updated in a long while to address these known issues.

**FAR Supply Lock fundamentally re-imagines the ownership detection chain to be "bullet-proof":**

*   **For Supply Signals:** We leverage a precise signal-based tracking system. When a player throws a supply signal, the plugin captures its unique ID via `OnExplosiveThrown` hook. Immediately following, the `OnCargoPlaneSignaled` hook provides a direct reference to that specific supply signal, allowing us to accurately link it to the incoming Cargo Plane ID. Finally, when the plane drops the Supply Drop, its ID is connected to this established chain, ensuring undeniable ownership.
*   **For Excavator Drops:** The process is similarly robust. The `OnExcavatorSuppliesRequested` hook delivers the Cargo Plane ID directly. From there, the plugin waits for the subsequent Supply Drop and establishes ownership by following the chain from the plane to the drop.

This meticulously tracked, signal-based logic eliminates race conditions, guaranteeing that each supply drop is correctly attributed and locked to its rightful owner, regardless of server load or simultaneous events.

## Features
*   **Reliable Ownership:** Guarantees that supply drops are locked to the exact player who called them, every time, without mix-ups.
*   **Race-Condition Free:** The redesigned signal-based detection chain eliminates the vulnerabilities found in token-based systems.
*   **Griefing & Theft Prevention:** Protects players' valuable loot from opportunistic theft - only the rightful owner/team can loot.
*   **Comprehensive Coverage:** Locks supply drops from both traditional Supply Signals and the Giant Excavator event.
*   **Flexible Integration:** Designed to work seamlessly with existing Rust server setups.

## Installation
1.  Download the `SupplyLock.cs` file.
2.  Place the file into your server's `oxide/plugins` (or `carbon/plugins`) folder.
3.  The plugin will automatically load upon server restart or when manually reloaded.

## Configuration
No configuration required

## Commands
No commands implemented

## Localization
Should you want to change the "Looting Denied" message you can do this by editing Line 83 in the .cs source code.
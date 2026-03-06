# FAR Fair Play

A small, opinionated quality‑of‑life plugin for Rust servers that focuses on **fairness, safety, and zero‑nonsense gameplay**.

No GUI.  
No permissions to manage.  
No configuration required.  

Just smart server‑side logic.  

## ✨ Features

### 🧹 Automatic Sleeper Cleanup
Cleans up players who:
- join your server,
- disconnect without actually playing,
- and log out in places they **don’t own**.

This prevents abandoned sleepers from cluttering bases and terrain while respecting legitimate logouts.

### 🛏️ Sleeper Relocation for Teams
Allows team members to **safely relocate sleeping teammates** *inside their own base*.

- Sleeper must belong to the same team
- Both sleeper and destination must be governed by the **same Tool Cupboard (TC)**
- Caller must be **authorized on the TC**
- Only one player can move a sleeper at a time
- No admin privileges required

Designed for:
- cleaning up logoff positions
- fixing awkward sleeper placement
- improving base organization

## 💬 Chat Command

### `/move`
A two‑step interaction:

1. **Look at a sleeping teammate** and type `/move`
2. **Look at a destination** (floor, rug, bed, deployable, etc.) and type `/move` again

If no destination is selected within 60 seconds, the request expires automatically.

## ⌨️ Best Practice (Highly Recommended)
Bind the command to a key for smooth usage.  
Example - enter in F1 console: `bind m chat.say "/move"`  
Prefer another key? Use any key you like: `bind <yourKey> chat.say "/move"`  

This allows you to simply:
- look at a sleeper → tap key
- look at destination → tap key

Fast, intuitive, and distraction‑free.

## 🧠 Design Notes

- Destination selection is **location‑based**, not entity‑based  
  → You can aim at floors, rugs, beds, boxes, or bare ground.
- The ray distance is intentionally short  
  → If you can’t physically reach the spot, neither should the sleeper.
- All decisions are enforced server‑side  
  → No exploits, no client trust.

## 📦 Installation

1. Download `FARFairPlay.cs`
2. Place it into:
   - `oxide/plugins/` **or**
   - `carbon/plugins/`
3. Reload the plugin or restart the server

The plugin loads automatically.

## ⚙️ Configuration

None.  
  
The plugin is intentionally **config‑free** and opinionated by design.

## 🌍 Localization

FAR: Fair Play supports full localization for all player‑facing messages related to `/move`.

- Default English messages are embedded in the plugin
- Additional language files are provided in the repository’s `language` folder

To add a translation:
1. Copy the language file
2. Translate it to how you like
3. Place it into:
   - `oxide/lang/<language>.json` **or**
   - `carbon/lang/<language>.json`

## 🔌 Plugin Hooks

The plugin exposes hooks for integration with other plugins:

- `OnFairPlayScheduledForRemoval(ulong userId, Vector3 position)`
- `OnFairPlayPlayerRelocated(ulong callerId, ulong sleeperId, Vector3 from, Vector3 to)`

These allow other plugins to react to cleanup or relocation events.

## 📝 Changelog

### 1.0.2 — 2026‑03‑06
- Added queue to handle reconnecting players
- Actual removals only when sleeper in queue

### 1.0.1 — 2026‑02‑22
- Added bypass permissions
- Smaller improvements around webhooks

### 1.0.0 — 2026‑01‑11
- Initial release
- Sleeper cleanup logic
- Team‑based sleeper relocation
- Localization support
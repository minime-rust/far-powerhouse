# FAR: Admin Blue

## 📋 Licensing & Credits

This plugin is a derivative work of "[No Green](https://umod.org/plugins/no-green)" by `Iv Misticos`. The original license is provided in `ORIGINAL-LICENSE.txt`, and further attribution details can be found in `NOTICE.FARAdminBlue.md`. This derivative work is also licensed under the MIT License.

All credit goes to `Iv Misticos` for creating the foundation. This version improves upon the original by fixing RCON logging behavior and reducing unnecessary console spam while maintaining the core stealth admin functionality.


## 📖 Description

**FAR: Admin Blue** is a lightweight Rust plugin that makes server administrators appear as regular players in chat. Admin messages are displayed with the standard blue player name color (`#5af`) instead of the default green, allowing admins to interact with players without revealing their elevated privileges.

All admin powers remain fully functional - this plugin **only** changes the visual appearance of admin names in chat messages.


## 🤔 Why "FAR: Admin Blue"?

While the original "No Green" plugin worked well, it had a few issues that needed addressing:

### **Problems with the Original:**
1. **Excessive Console Spam** - Redundant `[CHAT]` and `[TEAM CHAT]` logs cluttered the server console
2. **Code Inefficiency** - Contained unnecessary server console logging that duplicated vanilla behavior

### **Improvements in FAR: Admin Blue:**
✅ **Clean RCON Logging** - Messages appear once in RCON, exactly as intended  
✅ **Reduced Console Spam** - Removed redundant logging while preserving vanilla behavior  
✅ **Same Functionality** - All stealth features work identically to the original  

**The result:** A plugin that does the same job with less overhead and better integration with Rust's native systems.


## ✨ Features

- 🎭 **Stealth Mode** - Admins appear as regular players in all chat channels
- 🔧 **Full Admin Powers** - All administrative commands and permissions remain unchanged
- 🌐 **All Chat Channels** - Works seamlessly with:
  - Global chat
  - Team chat
  - Local/proximity chat
- 📊 **Clean Logging** - Proper RCON integration without duplication
- ⚡ **Lightweight** - Minimal performance impact (~80 lines of code)
- 🔌 **Framework Agnostic** - Works with both Oxide/uMod and Carbon


## 📦 Installation

1. Download `FARAdminBlue.cs`
2. Place it in your server's `oxide/plugins` (Oxide/uMod) or `carbon/plugins` (Carbon) directory
3. The plugin will auto-load on server restart or via `oxide.reload FARAdminBlue` / `c.reload FARAdminBlue`

**Requirements:**
- Rust server with Oxide/uMod or Carbon
- No dependencies on other plugins


## ⚙️ Configuration

**This plugin requires no configuration!**

- ❌ No config files
- ❌ No language files
- ❌ No data files
- ✅ Works immediately after installation

The blue color is hardcoded to match vanilla Rust's player name color for consistency.


## 🎮 Commands

**This plugin has no commands!**

It works automatically and silently in the background. Simply install it and your admin chat messages will appear with blue names instead of green.


## 🔍 How It Works

The plugin intercepts admin chat messages via the `OnPlayerChat` hook, then:

1. Checks for message coming from players with auth level > 0
2. Recreates the chat message with blue color instead of green
3. Sends it through the appropriate channel (global/team/local)
4. Logs to RCON exactly once (no duplicates)
5. Suppresses the original green message

**All chat channels are handled correctly**, including proximity-based fading for local chat.


## 🛠️ For Developers

Feel free to:
- 📖 **Read the code** - It's clean, commented, and easy to follow
- 🔧 **Suggest improvements** - Open an issue or pull request
- 🎨 **Fork it** - Create your own variation
- 💡 **Learn from it** - Use it as a reference for chat manipulation

The codebase demonstrates:
- Proper `OnPlayerChat` hook usage
- RCON integration without duplication
- Multi-channel chat handling
- Team and proximity-based messaging


## 🐛 Issues & Support

Found a bug or have a suggestion? Please open an issue on GitHub!


## 📜 Changelog

### v1.0.1 (2025-10-04)
- Code refactor and clean up

### v1.0.0 (Initial Release)
- Forked from "No Green" by `Iv Misticos`
- Fixed duplicate RCON logging
- Removed redundant console spam
- Cleaned up codebase
- Improved code maintainability
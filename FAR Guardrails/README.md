# FAR Guardrails

## Description
The **FAR Guardrails** plugin keeps your server clean by kicking players with inappropriate or advertising names on connect, and by kicking players who remain inactive for too long — either dead without respawning, or connected but never waking up and using up player slots on your server, hindering other players from connecting.

## Features
*   **Name Enforcement:** Players with names containing forbidden words or advertising URLs are kicked immediately on connect with a clear message.
*   **Inactivity Kick:** Players who die and never respawn, or connect and never wake up, are kicked after a configurable timeout.
*   **Complements Rust's Idle Kick:** Rust's built-in idle kick does not cover all inactive states. This plugin handles the corner cases Rust misses, making the two work together as a complete solution.

## Why use FAR Guardrails?
Rust's built-in idle kick only handles players who are awake and idle. It does not catch players who connect and never wake up, or die and never respawn. FAR Guardrails fills that gap, while also giving server owners control over player name standards. It is recommended that you have the following launch parameters for your Rust server, so that Rust itself handles Idle Kicks as a foundational work:

*   `+server.idlekick 20`    - (30) - kick after 20 minutes of being idle
*   `+server.idlekickmode 2` -  (1) - always kick, even if your server is empty

## Installation
1.  Download the `FARGuardrails.cs` file.
2.  Place the file into your server's `oxide/plugins` (or `carbon/plugins`) folder.
3.  The plugin will automatically load upon server restart or when manually reloaded.

## Configuration
Edit the `FARGuardrails.json` file in `oxide/config` (or `carbon/config`). This file is created automatically on first load.

| Key | Default | Description |
|---|---|---|
| `IdleKickMinutes` | `20` | Minutes before an inactive player is kicked |
| `IdleKickMessage` | `"Idle for {minutes} minutes"` | Kick message shown to the player. `{minutes}` is replaced automatically |
| `NameKickMessage` | `"No advertising, no profanities! Change your name and try again to connect to this server"` | Kick message shown to players with a forbidden name |
| `ForbiddenNameParts` | `[ "example.com", "badword", "discord.gg" ]` | List of substrings to match against player names |

## Name Matching
`ForbiddenNameParts` entries are matched as **case-insensitive substrings**. A player is kicked if their name contains any entry from the list anywhere within it. For example, the entry `"discord.gg"` would match all of the following names:

*   `discord.gg/myserver`
*   `JOIN discord.gg/cool`
*   `DISCORD.GG/RUST`

You do not need wildcards or exact matches — if the substring appears anywhere in the name, the player is kicked.

## Commands
No commands implemented

## Permissions
No permissions implemented
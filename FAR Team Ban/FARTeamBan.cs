using System;
using System.Collections.Generic;
using System.Linq;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("FAR: Team Ban", "miniMe", "1.0.1")]
    [Description("Allows admins to execute kick, ban, or full data wipe actions on entire teams via console or RCON.")]
    public class FARTeamBan : CovalencePlugin
    {

        private const int ConfirmationTimeoutMinutes = 5;
        private const string DefaultReason = "No Reason";
        private const bool debug = false; // set true to bypass functions when testing

        private Dictionary<string, PendingAction> pendingActions = new Dictionary<string, PendingAction>();

        private class PendingAction
        {
            public List<ulong> TargetSteamIDs { get; set; }
            public DateTime Timestamp { get; set; }
            public string BaseCommand { get; set; } // e.g. "teamban"
            public string CommandKey { get; set; }  // the complete command string
            public string Reason { get; set; }
            public float Hours { get; set; }
        }

        // --- RCON Commands ---

        [ConsoleCommand("teamban")]
        private void ConsoleCmdTeamBan(ConsoleSystem.Arg arg) =>
            HandleTeamCommand(arg, "teamban");

        [ConsoleCommand("teamkick")]
        private void ConsoleCmdTeamKick(ConsoleSystem.Arg arg) =>
            HandleTeamCommand(arg, "teamkick");

        [ConsoleCommand("teamwipe")]
        private void ConsoleCmdTeamWipe(ConsoleSystem.Arg arg) =>
            HandleTeamCommand(arg, "teamwipe");

        // --- Main logic (parse and confirm) ---

        private const string PermUse = "farteamban.use";

        private void Init() =>
            permission.RegisterPermission(PermUse, this);

        private void HandleTeamCommand(ConsoleSystem.Arg arg, string baseCommand)
        {
            // 0. Authorization gate
            BasePlayer issuer = arg.Connection?.player as BasePlayer;

            if (issuer != null && !permission.UserHasPermission(issuer.UserIDString, PermUse))
            {
                arg.ReplyWith("Error: You do not have permission to use this command.");
                return;
            }

            // 1. Get arguments
            if (arg.Args == null || arg.Args.Length < 1)
            {
                string usage;

                switch (baseCommand)
                {
                    case "teamban":
                        usage = $"{baseCommand} <SteamID> [\"Reason\"] [Hours]";
                        break;
                    case "teamkick":
                        usage = $"{baseCommand} <SteamID> [\"Reason\"]";
                        break;
                    case "teamwipe":
                        usage = $"{baseCommand} <SteamID>";
                        break;
                    default:
                        usage = $"{baseCommand} <SteamID> [...]";
                        break;
                }

                arg.ReplyWith($"Error: Missing required argument <SteamID>. Usage: {usage}");
                return;
            }

            string targetInput = arg.GetString(0);

            // Rust's Arg system handles quoted strings correctly for arg.GetString(1)
            string reason = arg.GetString(1, DefaultReason);
            float hours = 0f;

            // Get hours only if a reason was given.
            if (reason != DefaultReason && baseCommand == "teamban" && arg.Args.Length > 2)
                hours = arg.GetFloat(2, 0f);

            // 2. Get target team
            ulong targetSteamId;
            List<ulong> teamIDs = GetTeamMemberSteamIDs(targetInput, out targetSteamId);

            if (teamIDs == null || teamIDs.Count == 0)
            {
                arg.ReplyWith($"Error: Could not resolve target player '{targetInput}' or the associated team.");
                return;
            }

            // 3. Check confirmation
            CheckAndConfirmAction(arg, baseCommand, reason, hours, teamIDs);
        }

        private void CheckAndConfirmAction(ConsoleSystem.Arg arg, string baseCommand, string reason, float hours, List<ulong> teamIDs)
        {
            string commandKey = arg.FullString.Trim();

            // 1. Lazy cleanup
            CleanupPendingActions();

            if (pendingActions.ContainsKey(commandKey))
            {
                // CONFIRMATION STEP
                PendingAction action = pendingActions[commandKey];

                if ((DateTime.UtcNow - action.Timestamp).TotalMinutes <= ConfirmationTimeoutMinutes)
                {
                    // Valid confirmation. Go for it!
                    ExecuteAction(arg, action);
                    pendingActions.Remove(commandKey);
                    arg.ReplyWith($"Success: The command '{action.CommandKey}' has been executed.");
                }
                else
                {
                    // Timeout! Needs another confirmation.
                    pendingActions.Remove(commandKey);
                    arg.ReplyWith($"Warning: The command '{action.CommandKey}' has timed out! Please repeat!");
                    StartNewAction(arg, commandKey, teamIDs, baseCommand, reason, hours);
                }
            }
            else
            {
                // PRE-CONFIRMATION STEP
                // Starts new action, ignores older or similar commands (if commandKey changed)
                StartNewAction(arg, commandKey, teamIDs, baseCommand, reason, hours);
            }
        }

        private void StartNewAction(ConsoleSystem.Arg arg, string commandKey, List<ulong> teamIDs, string baseCommand, string reason, float hours)
        {
            pendingActions[commandKey] = new PendingAction
            {
                TargetSteamIDs = teamIDs,
                CommandKey = commandKey,
                BaseCommand = baseCommand,
                Reason = reason,
                Hours = hours,
                Timestamp = DateTime.UtcNow
            };

            // 1. List all team members (Name and ID)
            List<string> memberDetails = new List<string>();
            foreach (ulong steamId in teamIDs)
            {
                BasePlayer player = BasePlayer.FindAwakeOrSleeping(steamId.ToString());
                string playerName = GetPlayerName(steamId, player);
                memberDetails.Add($"Player **{playerName}** [{steamId}]");
            }

            // 2. Summary and formatting
            string actionVerb = GetActionVerb(baseCommand, teamIDs.Count);
            string teamDesignation = teamIDs.Count > 1 ? $"team of {teamIDs.Count}" : "solo player";
            string reasonPart = baseCommand == "teamban" && hours > 0 ? $" for {hours} hours (Reason: '{reason}')" : $" (Reason: '{reason}')";
            if (baseCommand == "teamwipe") reasonPart = "";

            // 3. Ensure consistant and meaningful output
            string line1 =
                $"WARNING: Pre-confirmation started for command '{commandKey}' " + $"targeting {teamIDs.Count} player(s).";
            string line2 =
                $"Affected Members: {string.Join(", ", memberDetails)}";
            string line3 =
                $"TO EXECUTE: Repeat the exact command '{commandKey}' within " + $"{ConfirmationTimeoutMinutes} minutes.";

            arg.ReplyWith(line1);   // output on the originating channel (server console / RCon *OR* F1 console)
            arg.ReplyWith(line2);
            arg.ReplyWith(line3);

            if (!arg.IsRcon)        // make sure *this* is visible in the server log
            {
                Puts(line1);
                Puts(line2);
                Puts(line3);
            }
        }

        private List<ulong> GetTeamMemberSteamIDs(string input, out ulong targetSteamId)
        {
            targetSteamId = 0;

            if (!ulong.TryParse(input, out targetSteamId))
                return null;

            // This works for online AND offline players
            RelationshipManager.PlayerTeam team =
                RelationshipManager.ServerInstance.FindPlayersTeam(targetSteamId);

            // Player has a team
            if (team != null && team.members != null && team.members.Count > 0)
                return team.members.ToList();

            // Solo player fallback
            return new List<ulong> { targetSteamId };
        }

        private string GetPlayerName(ulong steamId, BasePlayer player)
        {
            IPlayer iPlayer = covalence.Players.FindPlayerById(steamId.ToString());
            return player?.displayName ?? iPlayer?.Name ?? steamId.ToString();
        }

        private void ExecuteAction(ConsoleSystem.Arg arg, PendingAction action)
        {
            arg.ReplyWith($"Executing action '{action.BaseCommand}' on {action.TargetSteamIDs.Count} player(s)...");

            foreach (ulong steamId in action.TargetSteamIDs)
            {
                // Try to find player online, sleeper or covalence
                BasePlayer player = BasePlayer.FindAwakeOrSleeping(steamId.ToString());
                string playerName = GetPlayerName(steamId, player);

                switch (action.BaseCommand)
                {
                    case "teamban":
                        // banid <SteamID> <Name> [Reason] [Hours]
                        int hoursInt = (int)action.Hours;
                        if (debug)
                        {
                            arg.ReplyWith($"[DEBUG] -> BAN: Player \"{playerName}\" [{steamId}] for {action.Hours}h.");
                        }
                        else
                        {
                            ConsoleSystem.Run(ConsoleSystem.Option.Server, $"banid {steamId} \"{playerName}\" \"{action.Reason}\" {hoursInt}");
                            arg.ReplyWith($"-> BAN: Player {playerName} [{steamId}] for {action.Hours}h.");
                        }

                        break;

                    case "teamkick":
                        // kick <SteamID> [Reason]
                        if (debug)
                        {
                            arg.ReplyWith($"[DEBUG] -> KICK: Player \"{playerName}\" [{steamId}].");
                        }
                        else
                        {
                            ConsoleSystem.Run(ConsoleSystem.Option.Server, $"kick {steamId} \"{action.Reason}\"");
                            arg.ReplyWith($"-> KICK: Player \"{playerName}\" [{steamId}].");
                        }
                        break;

                    case "teamwipe":
                        // wipe player/team (best effort)
                        if (debug)
                        {
                            arg.ReplyWith($"[DEBUG] -> WIPE: Player \"{playerName}\" [{steamId}]. Data and entities wiped.");
                        }
                        else
                        {
                            WipePlayerData(arg, steamId, player, playerName);
                            arg.ReplyWith($"-> WIPE: Player \"{playerName}\" [{steamId}]. Data and entities wiped.");
                        }
                        break;
                }
            }

            string summary = $"Action '{action.BaseCommand}' completed successfully for {action.TargetSteamIDs.Count} player(s).";
            if (!arg.IsRcon) Puts(summary); // make sure *this* is visible in the server log
            arg.ReplyWith(summary);
        }

        private void WipePlayerData(ConsoleSystem.Arg arg, ulong steamId, BasePlayer player, string playerName)
        {
            // a) Strip inventory (only possible if player alive)
            if (player != null && !player.IsDead())
            {
                player.inventory.Strip();
                player.SendNetworkUpdateImmediate();
                arg.ReplyWith($"  > Inventory cleared for \"{playerName}\".");
            }
            else
            {
                arg.ReplyWith($"  > Inventory clear skipped (no active player entity): [{steamId}].");
            }

            // b) Blueprint wipe (only possible if player alive)
            if (player != null && !player.IsDead())
            {
                player.blueprints.Reset();
                player.SendNetworkUpdateImmediate();
                arg.ReplyWith($"  > Blueprints reset for \"{playerName}\".");
            }
            else
            {
                arg.ReplyWith($"  > Blueprint wipe skipped (no active player entity): [{steamId}]");
            }

            // c) Delete player entities
            ConsoleSystem.Run(ConsoleSystem.Option.Server, $"deleteby {steamId}");
            arg.ReplyWith($"  > Entities wiped for {steamId} using 'deleteby' command.");

            // d) Remove player (only possible if player alive)
            if (player != null && !player.IsDead())
                player.Die();
            else
                arg.ReplyWith($"  > Player kill skipped (no active player entity): [{steamId}].");

        }

        private string GetActionVerb(string baseCommand, int count)
        {
            string target = count > 1 ? "team" : "solo player";
            switch (baseCommand)
            {
                case "teamban": return $"Ban {target}";
                case "teamkick": return $"Kick {target}";
                case "teamwipe": return $"Wipe data for {target}";
                default: return $"Execute action on {target}";
            }
        }

        private void CleanupPendingActions()
        {
            DateTime cutoff = DateTime.UtcNow.AddMinutes(-ConfirmationTimeoutMinutes);
            var keysToRemove = new List<string>();

            foreach (var pair in pendingActions)
                if (pair.Value.Timestamp < cutoff)
                    keysToRemove.Add(pair.Key);

            foreach (string key in keysToRemove)
                pendingActions.Remove(key);
        }
    }
}